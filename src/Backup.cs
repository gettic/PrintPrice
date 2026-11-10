using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PrintPrice
{
    /// Online backup (idea 27): the backup code and the encrypted blob. Pure (no network, no files); the scheme is written
    /// down in plans\history-backup\scheme.md so the Worker / website match it byte for byte.
    ///
    /// Code: 20 characters of Crockford Base32 (0-9 A-Z without I L O U), shown as 4 groups of 5: 19 random + 1 check
    /// character = Alphabet[SHA-256(ASCII of the first 19)[0] & 31]. Typed codes are normalised (upper case, spaces and
    /// dashes dropped, O -> 0, I/L -> 1).
    /// Keys: HMAC-SHA256(key = ASCII of the 20-character code, message = UTF-8 label):
    ///   id = lower-case hex of the first 16 bytes ("PrintPrice backup id v1"), enc ("... enc v1"), mac ("... mac v1").
    /// Blob: "PPB1" | IV (16) | AES-256-CBC/PKCS7(enc, IV, GZip(UTF-8 backup JSON)) | HMAC-SHA256(mac, everything before).
    public static class Backup
    {
        public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";   // Crockford Base32, 32 characters
        public const int CodeLength = 20;
        public const int GroupLength = 5;
        public const string IdLabel = "PrintPrice backup id v1";
        public const string EncLabel = "PrintPrice backup enc v1";
        public const string MacLabel = "PrintPrice backup mac v1";
        public const int MaxUnpacked = 64 * 1024 * 1024;   // gzip bomb guard (5000 quotes is ~7 MB of JSON)
        static readonly byte[] Magic = { (byte)'P', (byte)'P', (byte)'B', (byte)'1' };
        const int IvLength = 16, MacLength = 32;

        static readonly RandomNumberGenerator rng = RandomNumberGenerator.Create();

        // ---------- the code ----------

        /// A new random code, "K7QF2-9XMRT-B3HDW-P5ZNC" (95 random bits + check character).
        public static string NewCode()
        {
            var b = new byte[CodeLength - 1];
            lock (rng) rng.GetBytes(b);
            return NewCode(delegate(int i) { return b[i] & 31; });   // 32 = 2^5: no bias
        }

        /// As above with an own source: pick(i) = the index (0-31) of character i (0-18). For tests.
        public static string NewCode(Func<int, int> pick)
        {
            var sb = new StringBuilder(CodeLength);
            for (int i = 0; i < CodeLength - 1; i++) sb.Append(Alphabet[pick(i) & 31]);
            sb.Append(CheckChar(sb.ToString()));
            return Format(sb.ToString());
        }

        /// The check character of the first 19 characters (normalised).
        public static char CheckChar(string first19)
        {
            using (var sha = SHA256.Create())
                return Alphabet[sha.ComputeHash(Encoding.ASCII.GetBytes(first19 ?? ""))[0] & 31];
        }

        /// The 20 characters without dashes (upper case, O -> 0, I/L -> 1, spaces and dashes dropped), or null when it
        /// isn't a valid code (wrong length, other characters, wrong check character).
        public static string Normalize(string typed)
        {
            string s = Clean(typed);
            if (s == null || s.Length != CodeLength) return null;
            return CheckChar(s.Substring(0, CodeLength - 1)) == s[CodeLength - 1] ? s : null;
        }

        /// The typed characters as code characters (upper case, O -> 0, I/L -> 1, spaces and dashes dropped), any length;
        /// null when one is never in a code (or typed is null). Normalize, and BackupForm's typo check.
        public static string Clean(string typed)
        {
            if (typed == null) return null;
            var sb = new StringBuilder(CodeLength);
            foreach (char ch in typed.ToUpperInvariant())
            {
                if (ch == '-' || char.IsWhiteSpace(ch)) continue;
                char c = ch == 'O' ? '0' : (ch == 'I' || ch == 'L') ? '1' : ch;
                if (Alphabet.IndexOf(c) < 0) return null;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// "K7QF29XMRTB3HDWP5ZNC" -> "K7QF2-9XMRT-B3HDW-P5ZNC" (a valid code; anything else is returned unchanged).
        public static string Format(string code)
        {
            string n = code == null ? null : code.Replace("-", "");
            if (n == null || n.Length != CodeLength) return code;
            return n.Substring(0, 5) + "-" + n.Substring(5, 5) + "-" + n.Substring(10, 5) + "-" + n.Substring(15, 5);
        }

        // ---------- keys ----------

        static string Valid(string code)
        {
            string n = Normalize(code);
            if (n == null) throw new ArgumentException(InvalidCodeText());
            return n;
        }

        static byte[] Derive(string normalized, string label)
        {
            using (var h = new HMACSHA256(Encoding.ASCII.GetBytes(normalized)))
                return h.ComputeHash(Encoding.UTF8.GetBytes(label));
        }

        /// Where the blob is stored: 32 lower-case hex characters. The server never sees the code. Throws on an invalid code.
        public static string StorageId(string code)
        {
            byte[] h = Derive(Valid(code), IdLabel);
            var sb = new StringBuilder(32);
            for (int i = 0; i < 16; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }

        public static byte[] EncKey(string code) { return Derive(Valid(code), EncLabel); }
        public static byte[] MacKey(string code) { return Derive(Valid(code), MacLabel); }

        // ---------- the blob ----------

        /// The encrypted blob of a backup text (History.ToBackup). Throws on an invalid code.
        public static byte[] Pack(string backupJson, string code)
        {
            var iv = new byte[IvLength];
            lock (rng) rng.GetBytes(iv);
            return Seal(Gzip(Encoding.UTF8.GetBytes(backupJson ?? "")), code, iv);
        }

        /// The backup text, or null with problem set (not a PrintPrice blob, wrong code or damaged, invalid code). Never throws.
        public static string Unpack(byte[] blob, string code, out string problem)
        {
            byte[] plain = Open(blob, code, out problem);
            if (plain == null) return null;
            try { return new UTF8Encoding(false, true).GetString(Gunzip(plain)); }
            catch
            {
                problem = Damaged();
                return null;
            }
        }

        public static string Unpack(byte[] blob, string code)
        {
            string problem;
            return Unpack(blob, code, out problem);
        }

        /// "PPB1" | iv | AES-256-CBC(plain) | HMAC. Pack = Seal(GZip(json)); public for the test vector.
        public static byte[] Seal(byte[] plain, string code, byte[] iv)
        {
            if (iv == null || iv.Length != IvLength) throw new ArgumentException("iv");
            string n = Valid(code);
            byte[] ct;
            using (var aes = NewAes(n, iv))
            using (var enc = aes.CreateEncryptor())
                ct = enc.TransformFinalBlock(plain ?? new byte[0], 0, plain == null ? 0 : plain.Length);
            var blob = new byte[Magic.Length + IvLength + ct.Length + MacLength];
            Buffer.BlockCopy(Magic, 0, blob, 0, Magic.Length);
            Buffer.BlockCopy(iv, 0, blob, Magic.Length, IvLength);
            Buffer.BlockCopy(ct, 0, blob, Magic.Length + IvLength, ct.Length);
            byte[] mac;
            using (var h = new HMACSHA256(Derive(n, MacLabel)))
                mac = h.ComputeHash(blob, 0, blob.Length - MacLength);
            Buffer.BlockCopy(mac, 0, blob, blob.Length - MacLength, MacLength);
            return blob;
        }

        /// The plain bytes of a blob (HMAC checked first, in constant time), or null with problem set. Never throws.
        public static byte[] Open(byte[] blob, string code, out string problem)
        {
            problem = null;
            string n = Normalize(code);
            if (n == null)
            {
                problem = InvalidCodeText();
                return null;
            }
            if (!IsBlob(blob))
            {
                problem = History.NotBackupText();
                return null;
            }
            int ctLen = blob.Length - Magic.Length - IvLength - MacLength;
            if (ctLen <= 0 || ctLen % 16 != 0) { problem = Damaged(); return null; }
            byte[] mac;
            using (var h = new HMACSHA256(Derive(n, MacLabel)))
                mac = h.ComputeHash(blob, 0, blob.Length - MacLength);
            int diff = 0;
            for (int i = 0; i < MacLength; i++) diff |= mac[i] ^ blob[blob.Length - MacLength + i];
            if (diff != 0) { problem = Damaged(); return null; }
            try
            {
                var iv = new byte[IvLength];
                Buffer.BlockCopy(blob, Magic.Length, iv, 0, IvLength);
                using (var aes = NewAes(n, iv))
                using (var dec = aes.CreateDecryptor())
                    return dec.TransformFinalBlock(blob, Magic.Length + IvLength, ctLen);
            }
            catch
            {
                problem = Damaged();
                return null;
            }
        }

        /// True when the bytes start with "PPB1" and are long enough for iv + one block + HMAC.
        public static bool IsBlob(byte[] blob)
        {
            if (blob == null || blob.Length < Magic.Length + IvLength + 16 + MacLength) return false;
            for (int i = 0; i < Magic.Length; i++) if (blob[i] != Magic[i]) return false;
            return true;
        }

        // AES-256-CBC, PKCS7, key = the code's enc key
        static Aes NewAes(string normalized, byte[] iv)
        {
            var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Derive(normalized, EncLabel);
            aes.IV = iv;
            return aes;
        }

        static string InvalidCodeText()
        {
            return L.S("This is not a valid backup code.", "Det er ikke en gyldig sikkerhedskode.");
        }

        static string Damaged()
        {
            return L.S("Wrong code, or the backup is damaged.", "Forkert kode, eller sikkerhedskopien er beskadiget.");
        }

        static byte[] Gzip(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionMode.Compress, true))
                    gz.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        static byte[] Gunzip(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buf = new byte[81920];
                int n;
                while ((n = gz.Read(buf, 0, buf.Length)) > 0)
                {
                    if (output.Length + n > MaxUnpacked) throw new InvalidDataException("too big");
                    output.Write(buf, 0, n);
                }
                return output.ToArray();
            }
        }
    }
}
