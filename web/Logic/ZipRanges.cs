using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace PrintPrice.Web
{
    /// Reads byte ranges of a file (in the browser: File.slice; in tests: a byte array or a file on disk).
    public interface IRangeSource
    {
        long Length { get; }
        /// Up to `count` bytes from `offset` (fewer at the end of the file).
        Task<byte[]> Read(long offset, int count);
    }

    public sealed class BytesSource : IRangeSource
    {
        readonly byte[] data;
        public int Reads;   // tests: how many reads were made
        public long BytesRead;
        public BytesSource(byte[] data) { this.data = data; }
        public long Length { get { return data.Length; } }
        public Task<byte[]> Read(long offset, int count)
        {
            Reads++;
            if (offset < 0 || offset >= data.Length || count <= 0) return Task.FromResult(new byte[0]);
            int n = (int)Math.Min(count, data.Length - offset);
            var b = new byte[n];
            Buffer.BlockCopy(data, (int)offset, b, 0, n);
            BytesRead += n;
            return Task.FromResult(b);
        }
    }

    /// A small zip reader that fetches only the end-of-zip record, the entry list and the entries asked for, so a big
    /// sliced .3mf is never read whole (the plate G-code inside is skipped). Stored and deflated entries; no Zip64.
    public sealed class ZipRanges
    {
        public sealed class Entry
        {
            public string Name;
            public int Method;              // 0 stored, 8 deflate
            public long CompressedSize, Size, HeaderOffset;
        }

        public const int TailBytes = 256 * 1024;
        const int MaxDirectoryBytes = 8 * 1024 * 1024;

        readonly IRangeSource src;
        public readonly List<Entry> Entries = new List<Entry>();

        ZipRanges(IRangeSource src) { this.src = src; }

        /// Null when it isn't a zip file (or a broken / Zip64 one).
        public static async Task<ZipRanges> Open(IRangeSource src)
        {
            long len = src.Length;
            if (len < 22) return null;
            int tailLen = (int)Math.Min(len, TailBytes);
            long tailStart = len - tailLen;
            byte[] tail = await src.Read(tailStart, tailLen);
            int eocd = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
                if (U32(tail, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) return null;
            int count = U16(tail, eocd + 10);
            long dirSize = U32(tail, eocd + 12), dirOffset = U32(tail, eocd + 16);
            if (dirSize == 0xFFFFFFFF || dirOffset == 0xFFFFFFFF || count == 0xFFFF) return null;   // Zip64
            if (dirSize > MaxDirectoryBytes || dirOffset + dirSize > len) return null;
            byte[] dir;
            if (dirOffset >= tailStart && dirOffset + dirSize <= len)
            {
                dir = new byte[dirSize];
                Buffer.BlockCopy(tail, (int)(dirOffset - tailStart), dir, 0, (int)dirSize);
            }
            else dir = await src.Read(dirOffset, (int)dirSize);
            var z = new ZipRanges(src);
            int p = 0;
            for (int n = 0; n < count; n++)
            {
                if (p + 46 > dir.Length || U32(dir, p) != 0x02014b50) return null;
                int flags = U16(dir, p + 8);
                var e = new Entry
                {
                    Method = U16(dir, p + 10),
                    CompressedSize = U32(dir, p + 20),
                    Size = U32(dir, p + 24),
                    HeaderOffset = U32(dir, p + 42)
                };
                int nameLen = U16(dir, p + 28), extraLen = U16(dir, p + 30), commentLen = U16(dir, p + 32);
                if (p + 46 + nameLen > dir.Length) return null;
                var enc = (flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.Latin1;
                e.Name = enc.GetString(dir, p + 46, nameLen);
                z.Entries.Add(e);
                p += 46 + nameLen + extraLen + commentLen;
            }
            return z;
        }

        public Entry Find(string name)
        {
            foreach (var e in Entries) if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        public IEnumerable<string> Names
        {
            get { foreach (var e in Entries) yield return e.Name; }
        }

        /// The entry's bytes, or null when missing, bigger than maxSize, or unreadable.
        public async Task<byte[]> ReadEntry(string name, long maxSize)
        {
            var e = Find(name);
            if (e == null || e.Size > maxSize || e.CompressedSize > maxSize || e.Size == 0xFFFFFFFF) return null;
            if (e.Method != 0 && e.Method != 8) return null;
            byte[] head = await src.Read(e.HeaderOffset, 30);
            if (head.Length < 30 || U32(head, 0) != 0x04034b50) return null;
            long dataStart = e.HeaderOffset + 30 + U16(head, 26) + U16(head, 28);
            byte[] data = e.CompressedSize == 0 ? new byte[0] : await src.Read(dataStart, (int)e.CompressedSize);
            if (data.Length != e.CompressedSize) return null;
            if (e.Method == 0) return data;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (var outp = new MemoryStream((int)Math.Min(e.Size, int.MaxValue)))
                {
                    ds.CopyTo(outp);
                    return outp.ToArray();
                }
            }
            catch (InvalidDataException) { return null; }
        }

        public async Task<string> ReadText(string name, long maxSize)
        {
            var b = await ReadEntry(name, maxSize);
            if (b == null) return null;
            int start = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;   // BOM, as StreamReader skips it
            return Encoding.UTF8.GetString(b, start, b.Length - start);
        }

        static int U16(byte[] b, int i) { return b[i] | (b[i + 1] << 8); }
        static long U32(byte[] b, int i) { return (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24)); }
    }
}
