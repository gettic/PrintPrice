using System;
using System.Globalization;
using System.IO;
#if !WEB
using System.Net;
#endif

namespace PrintPrice
{
    /// A failed online backup call; Message is the user-facing text (English/Danish). Status = the HTTP status,
    /// 0 = no connection / timeout, -1 = a problem found on this PC (invalid code, too big, wrong code or damaged backup).
    /// DailyLimit: the Worker's 5-uploads-a-day cap was hit (status 429); ResetUtc = when uploading works again.
    public class OnlineBackupException : Exception
    {
        public readonly int Status;
        public readonly bool DailyLimit;
        public readonly DateTime? ResetUtc;
        public OnlineBackupException(string message, int status) : this(message, status, false, null) { }
        public OnlineBackupException(string message, int status, bool dailyLimit, DateTime? resetUtc) : base(message)
        {
            Status = status; DailyLimit = dailyLimit; ResetUtc = resetUtc;
        }
    }

    /// What the Worker said about today's upload allowance (after a successful upload, or with the daily-limit 429).
    public class UploadResult
    {
        public int UploadsLeft = -1;          // -1 = the server didn't say
        public DateTime? ResetUtc;            // when the count starts over (UTC midnight), null = unknown
    }

    /// Online backup (idea 27) through PrintPrice's own Cloudflare Worker (source in worker\). Only the storage id
    /// (Backup.StorageId) and the encrypted blob (Backup.Pack) leave this PC; the code never does.
    ///   PUT /b/<id>  (blob, max 512 KB; kept 400 days, renewed on every upload; 5 uploads a day per IP)
    ///   GET /b/<id>   DELETE /b/<id>   (not counted)
    /// Every call blocks: run it off the UI thread (Task.Run). Failures throw OnlineBackupException, nothing else.
    public static class OnlineBackup
    {
        public const string Server = "https://printprice.gtd-relay.workers.dev";
        public const int MaxBlob = 512 * 1024;       // the Worker refuses more (413)
        public const int KeptDays = 400;             // the Worker's KV time-to-live, renewed by every upload
        public const int DailyUploads = 5;           // the Worker's cap per IP per UTC day
        const int TimeoutMs = 20000, ReadWriteTimeoutMs = 30000;

#if !WEB
        /// Uploads (or replaces) the backup for this code. backupJson = History.ToBackup(...).
        /// Returns the uploads left today and when the count resets.
        public static UploadResult Upload(string code, string backupJson) { return Upload(Server, code, backupJson); }

        /// The backup text for this code (for History.ReadBackup).
        public static string Download(string code) { return Download(Server, code); }

        /// Deletes the online backup for this code (no error when there is none).
        public static void Delete(string code) { Delete(Server, code); }

        internal static UploadResult Upload(string server, string code, string backupJson)
        {
            string url = Url(server, code);
            byte[] blob;
            try { blob = Backup.Pack(backupJson, code); }
            catch (Exception ex) { throw new OnlineBackupException(ex.Message, -1); }
            string tooBig = SizeProblem(blob.Length);
            if (tooBig != null) throw new OnlineBackupException(tooBig, -1);
            var r = Send("PUT", url, blob, false);
            return ParseLimitHeaders(r.Left, r.Reset, r.RetryAfter, DateTime.UtcNow);
        }

        internal static string Download(string server, string code)
        {
            byte[] blob = Send("GET", Url(server, code), null, true).Body;
            string problem;
            string text = Backup.Unpack(blob, code, out problem);
            if (text == null) throw new OnlineBackupException(problem, -1);
            return text;
        }

        internal static void Delete(string server, string code)
        {
            try { Send("DELETE", Url(server, code), null, false); }
            catch (OnlineBackupException ex) { if (ex.Status != 404) throw; }
        }
#endif

        /// server + "/b/" + the code's storage id. Throws OnlineBackupException (-1) on an invalid code.
        public static string Url(string server, string code)
        {
            try { return server.TrimEnd('/') + "/b/" + Backup.StorageId(code); }
            catch (ArgumentException ex) { throw new OnlineBackupException(ex.Message, -1); }
        }

        /// null when a blob of this many bytes may be uploaded, else the "too big" text.
        public static string SizeProblem(int blobLength)
        {
            return blobLength > MaxBlob ? ErrorText(413, false) : null;
        }

        /// The Worker's X-Uploads-Left, X-Uploads-Reset (ISO UTC, "2026-11-11T00:00:00Z") and Retry-After (seconds)
        /// headers (any may be null). The reset time comes from X-Uploads-Reset, else nowUtc + Retry-After.
        public static UploadResult ParseLimitHeaders(string left, string reset, string retryAfter, DateTime nowUtc)
        {
            var r = new UploadResult();
            int n;
            if (left != null && int.TryParse(left.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n)) r.UploadsLeft = n;
            DateTime t;
            if (reset != null && DateTime.TryParse(reset.Trim(), CultureInfo.InvariantCulture,
                                                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t))
                r.ResetUtc = DateTime.SpecifyKind(t, DateTimeKind.Utc);
            else if (retryAfter != null && int.TryParse(retryAfter.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n))
                r.ResetUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc).AddSeconds(n);
            return r;
        }

        /// The user-facing text for a failed call: status 0 = no connection / timeout, else the HTTP status.
        /// isGet: the call was a download (404 = no backup for this code). 429 here = busy (see DailyLimitText).
        public static string ErrorText(int status, bool isGet)
        {
            switch (status)
            {
                case 0:
                    return L.S("Could not reach the online backup service. Check the internet connection and try again.",
                               "Kunne ikke nå online-kopi-tjenesten. Tjek internetforbindelsen og prøv igen.");
                case 404:
                    if (isGet)
                        return L.F("No online backup was found for this code. Check the code; a backup is deleted {0} days after the last upload.",
                                   "Der blev ikke fundet nogen online-kopi med denne kode. Tjek koden; en kopi slettes {0} dage efter seneste kopi.",
                                   KeptDays);
                    break;
                case 413:
                    return L.F("The backup is too big to keep online (max. {0} KB). Save a backup file instead.",
                               "Kopien er for stor til at gemme online (maks. {0} KB). Gem en kopi-fil i stedet.", MaxBlob / 1024);
                case 429:
                case 503:
                    return L.S("The online backup service is busy or has reached today's limit. Try again later, or save a backup file.",
                               "Online-kopi-tjenesten har travlt eller har nået dagens grænse. Prøv igen senere, eller gem en kopi-fil.");
                case 400:
                    return L.S("The online backup service refused the backup. Update PrintPrice and try again.",
                               "Online-kopi-tjenesten afviste kopien. Opdater PrintPrice og prøv igen.");
            }
            return L.F("The online backup service has a problem right now (error {0}). Try again later.",
                       "Online-kopi-tjenesten har et problem lige nu (fejl {0}). Prøv igen senere.", status);
        }

        /// "Daily limit reached (5 online backups a day). You can back up again in 3 h 20 min." (resetUtc null = no time).
        public static string DailyLimitText(DateTime? resetUtc, DateTime nowUtc)
        {
            string head = L.F("Daily limit reached ({0} online backups a day).", "Dagens grænse er nået ({0} online-kopier om dagen).", DailyUploads);
            if (resetUtc == null)
                return head + " " + L.S("You can back up again tomorrow.", "Du kan tage en kopi igen i morgen.");
            string again = L.F("You can back up again in {0}", "Du kan tage en kopi igen om {0}", WaitText(resetUtc.Value - nowUtc));
            return head + " " + (again.EndsWith(".") ? again : again + ".");   // Danish "min." already ends the sentence
        }

        /// "3 h 20 min" / "3 t. 20 min."; minutes rounded up, at least 1 min.
        public static string WaitText(TimeSpan wait)
        {
            int mins = (int)Math.Ceiling(Math.Max(0, wait.TotalMinutes));
            if (mins < 1) mins = 1;
            int h = mins / 60, m = mins % 60;
            if (h == 0) return L.F("{0} min", "{0} min.", m);
            if (m == 0) return L.F("{0} h", "{0} t.", h);
            return L.F("{0} h {1} min", "{0} t. {1} min.", h, m);
        }

#if !WEB
        class Reply
        {
            public byte[] Body;
            public string Left, Reset, RetryAfter;
        }

        /// One request. readBody: Reply.Body = the body (at most MaxBlob bytes).
        static Reply Send(string method, string url, byte[] body, bool readBody)
        {
            bool isGet = method == "GET";
            Net.EnsureTls();
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.UserAgent = Net.DefaultUserAgent;
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = ReadWriteTimeoutMs;
                req.AllowAutoRedirect = false;
                req.ServicePoint.Expect100Continue = false;   // send the body at once (no 350 ms wait for "100 Continue")
                if (body != null)
                {
                    req.ContentType = "application/octet-stream";
                    req.ContentLength = body.Length;
                    using (var rs = req.GetRequestStream()) rs.Write(body, 0, body.Length);
                }
                else if (method != "GET") req.ContentLength = 0;

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code > 299) throw Failure(resp, isGet);
                    var r = new Reply { Left = resp.Headers["X-Uploads-Left"], Reset = resp.Headers["X-Uploads-Reset"], RetryAfter = resp.Headers["Retry-After"] };
                    if (!readBody) return r;
                    using (var s = resp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        var buf = new byte[16384];
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            ms.Write(buf, 0, n);
                            if (ms.Length > MaxBlob) throw new OnlineBackupException(ErrorText(502, isGet), 502);
                        }
                        r.Body = ms.ToArray();
                        return r;
                    }
                }
            }
            catch (OnlineBackupException) { throw; }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null)
                {
                    try { throw Failure(resp, isGet); }
                    finally { resp.Close(); }
                }
                throw new OnlineBackupException(ErrorText(0, isGet), 0);
            }
            catch (Exception)
            {
                throw new OnlineBackupException(ErrorText(0, isGet), 0);
            }
        }

        /// A non-2xx answer as an exception. A 429 with X-Uploads-Reset is the daily cap; any other 429 is "busy".
        static OnlineBackupException Failure(HttpWebResponse resp, bool isGet)
        {
            int code = (int)resp.StatusCode;
            string reset = resp.Headers["X-Uploads-Reset"];
            if (code == 429 && reset != null)
            {
                DateTime now = DateTime.UtcNow;
                var lim = ParseLimitHeaders("0", reset, resp.Headers["Retry-After"], now);
                return new OnlineBackupException(DailyLimitText(null, now), 429, true, lim.ResetUtc);
            }
            return new OnlineBackupException(ErrorText(code, isGet), code);
        }
#endif
    }
}
