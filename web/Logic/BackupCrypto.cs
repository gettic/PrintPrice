using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PrintPrice.Web
{
    /// The online backup blob on the web: the same bytes as the desktop's Backup.Pack / Unpack (src\Backup.cs, linked),
    /// but AES-256-CBC comes from the browser (crypto.subtle; .NET in the browser has none), so these are async with the
    /// AES step passed in. Code, keys, storage id, HMAC and GZip are Backup.cs's / .NET's own.
    /// aes(key, iv, data, encrypt) = AES-256-CBC with PKCS7 padding (tests pass .NET's Aes).
    public static class BackupCrypto
    {
        public delegate Task<byte[]> AesCbc(byte[] key, byte[] iv, byte[] data, bool encrypt);

        static readonly byte[] Magic = { (byte)'P', (byte)'P', (byte)'B', (byte)'1' };
        const int IvLength = 16, MacLength = 32;

        /// Backup.Pack: "PPB1" | iv | AES(GZip(UTF-8 json)) | HMAC. Throws ArgumentException on an invalid code.
        public static Task<byte[]> Pack(string json, string code, AesCbc aes)
        {
            var iv = new byte[IvLength];
            RandomNumberGenerator.Fill(iv);
            return Seal(Backup.Gzip(Encoding.UTF8.GetBytes(json ?? "")), code, iv, aes);
        }

        /// Backup.Seal with the browser's AES.
        public static async Task<byte[]> Seal(byte[] plain, string code, byte[] iv, AesCbc aes)
        {
            if (iv == null || iv.Length != IvLength) throw new ArgumentException("iv");
            byte[] enc = Backup.EncKey(code), mac = Backup.MacKey(code);   // throw on an invalid code
            byte[] ct = await aes(enc, iv, plain ?? new byte[0], true);
            var blob = new byte[Magic.Length + IvLength + ct.Length + MacLength];
            Buffer.BlockCopy(Magic, 0, blob, 0, Magic.Length);
            Buffer.BlockCopy(iv, 0, blob, Magic.Length, IvLength);
            Buffer.BlockCopy(ct, 0, blob, Magic.Length + IvLength, ct.Length);
            byte[] h;
            using (var hm = new HMACSHA256(mac)) h = hm.ComputeHash(blob, 0, blob.Length - MacLength);
            Buffer.BlockCopy(h, 0, blob, blob.Length - MacLength, MacLength);
            return blob;
        }

        public sealed class Opened
        {
            public string Text;      // the backup JSON, null = problem
            public string Problem;
        }

        /// Backup.Unpack: HMAC checked first (constant time), then the browser's AES, then gunzip (64 MB cap). Never throws.
        public static async Task<Opened> Unpack(byte[] blob, string code, AesCbc aes)
        {
            var res = new Opened();
            if (Backup.Normalize(code) == null) { res.Problem = Backup.InvalidCodeText(); return res; }
            if (!Backup.IsBlob(blob)) { res.Problem = History.NotBackupText(); return res; }
            int ctLen = blob.Length - Magic.Length - IvLength - MacLength;
            if (ctLen <= 0 || ctLen % 16 != 0) { res.Problem = Backup.Damaged(); return res; }
            byte[] h;
            using (var hm = new HMACSHA256(Backup.MacKey(code))) h = hm.ComputeHash(blob, 0, blob.Length - MacLength);
            int diff = 0;
            for (int i = 0; i < MacLength; i++) diff |= h[i] ^ blob[blob.Length - MacLength + i];
            if (diff != 0) { res.Problem = Backup.Damaged(); return res; }
            try
            {
                var iv = new byte[IvLength];
                Buffer.BlockCopy(blob, Magic.Length, iv, 0, IvLength);
                var ct = new byte[ctLen];
                Buffer.BlockCopy(blob, Magic.Length + IvLength, ct, 0, ctLen);
                byte[] plain = await aes(Backup.EncKey(code), iv, ct, false);
                res.Text = new UTF8Encoding(false, true).GetString(Backup.Gunzip(plain));
            }
            catch (Exception) { res.Problem = Backup.Damaged(); res.Text = null; }
            return res;
        }
    }
}
