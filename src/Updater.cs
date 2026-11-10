using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PrintPrice
{
    public class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string Title;
        public string Notes;
        public string PageUrl;
        public string DownloadUrl;
        public string Sha256;      // lower-case hex from GitHub's asset digest
        public long Size;
    }

    public class UpdateException : Exception
    {
        public UpdateException(string message) : base(message) { }
    }

    /// Updates the program from the latest GitHub release of gettic/PrintPrice.
    /// The release must contain an asset named PrintPrice.exe; GitHub's SHA-256 digest of it is verified.
    public static class Updater
    {
        public const string Repo = "gettic/PrintPrice";
        const string Api = "https://api.github.com/repos/" + Repo + "/releases?per_page=100";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
        const string AssetName = "PrintPrice.exe";
        const string AllowedHost = "github.com";

        public static Version Current
        {
            get { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); }
        }

        public static string CurrentText
        {
            get { return Short(Current); }
        }

        /// "1.2.3.0" -> "1.2.3"
        public static string Short(Version v)
        {
            return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
        }

        static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        /// "v1.0.1" / "1.0.1" -> 1.0.1.0; null if the tag isn't a version.
        public static Version ParseTag(string tag)
        {
            var m = Regex.Match(tag ?? "", @"^[vV]?(\d+(?:\.\d+){1,3})$");
            Version v;
            if (!m.Success || !Version.TryParse(m.Groups[1].Value, out v)) return null;
            return Normalize(v);
        }

        static WebClient Client()
        {
            Net.EnsureTls();   // the exe download
            return Net.Client("PrintPrice-updater/" + CurrentText);
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            return Net.Str(d, key);
        }

        /// Reads GitHub's "latest release" JSON. Returns null if it has no usable PrintPrice.exe.
        public static UpdateInfo ParseRelease(string json)
        {
            return FromRelease(Json.Deserialize<Dictionary<string, object>>(json));
        }

        /// One release object from GitHub's JSON. Null if draft/prerelease, not a version tag, or no usable PrintPrice.exe.
        static UpdateInfo FromRelease(Dictionary<string, object> d)
        {
            if (d == null) return null;
            if (Str(d, "draft") == "True" || Str(d, "prerelease") == "True") return null;
            var info = new UpdateInfo
            {
                Tag = Str(d, "tag_name"),
                Title = Str(d, "name"),
                Notes = Str(d, "body") ?? "",
                PageUrl = Str(d, "html_url") ?? ReleasesPage
            };
            info.Version = ParseTag(info.Tag);
            if (info.Version == null) return null;
            var assets = d.ContainsKey("assets") ? d["assets"] as IEnumerable : null;
            if (assets == null) return null;
            foreach (var o in assets)
            {
                var a = o as Dictionary<string, object>;
                if (a == null || !string.Equals(Str(a, "name"), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                info.DownloadUrl = Str(a, "browser_download_url");
                long size; long.TryParse(Str(a, "size"), out size);
                info.Size = size;
                var dg = Regex.Match(Str(a, "digest") ?? "", @"^sha256:([0-9a-fA-F]{64})$");
                info.Sha256 = dg.Success ? dg.Groups[1].Value.ToLowerInvariant() : null;
            }
            return string.IsNullOrEmpty(info.DownloadUrl) ? null : info;
        }

        /// Returns the latest release if it is newer than this program, otherwise null.
        // Conditional-request cache (per process, memory only). It saves downloading the list again; our requests are
        // anonymous, so a 304 reply still counts toward GitHub's rate limit (60 an hour per IP address).
        static readonly object CacheLock = new object();
        static string _etag;
        static UpdateInfo _cached;
        static bool _hasCache;
        static DateTime _nextAllowedUtc = DateTime.MinValue;

        /// After a rate-limit reply from GitHub: the earliest time to ask again (UTC). Otherwise in the past.
        public static DateTime NextAllowedUtc
        {
            get { lock (CacheLock) return _nextAllowedUtc; }
        }

        static string TestModeMessage()
        {
            return L.S("Updates are off in test mode.", "Opdateringer er slået fra i testtilstand.");
        }

        /// Returns the latest release if it is newer than this program, otherwise null.
        /// Throws UpdateException if GitHub can't be reached or the rate limit is hit.
        public static UpdateInfo CheckForUpdate()
        {
            if (AppSettings.TestMode) return null;
            bool limited;
            var r = Fetch(out limited);
            if (limited) throw new UpdateException(L.S("GitHub is limiting requests right now. Try again in a little while.",
                "GitHub begrænser forespørgsler lige nu. Prøv igen om lidt."));
            return r;
        }

        /// For the background timer: same as CheckForUpdate but never throws and never shows anything.
        /// Returns the newest release (or the last known one if GitHub is rate limiting or unreachable), null if none.
        /// Does nothing (returns the cached result) while NextAllowedUtc is in the future. Call off the UI thread.
        public static UpdateInfo CheckQuiet()
        {
            _lastQuietOk = false;
            if (AppSettings.TestMode) return null;
            try
            {
                lock (CacheLock)
                    if (DateTime.UtcNow < _nextAllowedUtc) return _hasCache ? _cached : null;
                bool limited;
                var r = Fetch(out limited);
                _lastQuietOk = !limited;
                return r;
            }
            catch (Exception)
            {
                lock (CacheLock) return _hasCache ? _cached : null;
            }
        }

        static volatile bool _lastQuietOk;
        /// True when the last CheckQuiet got a real answer from GitHub (200, 304, or 404 = no releases), so a null
        /// result truly means "nothing newer". False on failure, rate limit, backoff wait or test mode.
        public static bool LastQuietOk { get { return _lastQuietOk; } }

        static UpdateInfo Fetch(out bool rateLimited)
        {
            rateLimited = false;
            Net.EnsureTls();   // this request doesn't go through Net.Client; without it the first check after a start fails
            string etag; bool hadCache;
            lock (CacheLock) { etag = _etag; hadCache = _hasCache; }
            var req = (HttpWebRequest)WebRequest.Create(Api);
            req.UserAgent = "PrintPrice-updater/" + CurrentText;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            if (hadCache && !string.IsNullOrEmpty(etag)) req.Headers[HttpRequestHeader.IfNoneMatch] = etag;
            HttpWebResponse resp = null;
            try
            {
                try { resp = (HttpWebResponse)req.GetResponse(); }
                catch (WebException ex)
                {
                    resp = ex.Response as HttpWebResponse;
                    if (resp == null) throw new UpdateException(L.F("Couldn't reach GitHub ({0})", "Kunne ikke nå GitHub ({0})", ex.Message));
                    var code = resp.StatusCode;
                    if (code == HttpStatusCode.NotModified)
                    {
                        lock (CacheLock) return _hasCache ? _cached : null;
                    }
                    if (code == HttpStatusCode.NotFound) return null;   // no releases yet
                    if (code == HttpStatusCode.Forbidden || (int)code == 429)
                    {
                        SetBackoff(resp, true);
                        rateLimited = true;
                        lock (CacheLock) return _hasCache ? _cached : null;
                    }
                    throw new UpdateException(L.F("Couldn't reach GitHub ({0})", "Kunne ikke nå GitHub ({0})", ex.Message));
                }
                string json;
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
                var info = ParseReleases(json, Current);
                lock (CacheLock)
                {
                    _etag = resp.Headers["ETag"];
                    _cached = info;
                    _hasCache = true;
                }
                SetBackoff(resp, false);
                return info;
            }
            finally { if (resp != null) resp.Close(); }
        }

        /// Reads Retry-After / X-RateLimit-* headers. On a rate-limit reply always sets a wait; on a normal
        /// reply only when no requests remain.
        static void SetBackoff(HttpWebResponse resp, bool limitedReply)
        {
            DateTime until = DateTime.MinValue;
            int secs, remaining; long reset;
            if (int.TryParse(resp.Headers["Retry-After"], out secs) && secs > 0) until = DateTime.UtcNow.AddSeconds(secs);
            if (int.TryParse(resp.Headers["X-RateLimit-Remaining"], out remaining) && remaining <= 0 &&
                long.TryParse(resp.Headers["X-RateLimit-Reset"], out reset))
            {
                var r = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(reset);
                if (r > until) until = r;
            }
            else if (!limitedReply) return;
            if (limitedReply && until <= DateTime.UtcNow) until = DateTime.UtcNow.AddMinutes(15);
            var max = DateTime.UtcNow.AddHours(2);
            if (until > max) until = max;
            if (until > DateTime.UtcNow) lock (CacheLock) _nextAllowedUtc = until;
        }

        /// Reads GitHub's release list. The target is the newest version above `current` with a usable
        /// PrintPrice.exe; its Notes become every release in (current, target], newest first, each under "## vX.Y.Z".
        /// Returns null if there is nothing newer.
        public static UpdateInfo ParseReleases(string json, Version current)
        {
            var list = Json.Deserialize<object>(json) as object[];
            if (list == null) return null;
            var all = new List<Dictionary<string, object>>();
            UpdateInfo target = null;
            foreach (var o in list)
            {
                var d = o as Dictionary<string, object>;
                if (d == null || Str(d, "draft") == "True" || Str(d, "prerelease") == "True") continue;
                var v = ParseTag(Str(d, "tag_name"));
                if (v == null || v <= current) continue;
                all.Add(d);
                if (target != null && v <= target.Version) continue;
                var info = FromRelease(d);
                if (info != null) target = info;
            }
            if (target == null) return null;
            var notes = new List<KeyValuePair<Version, string>>();
            var seen = new HashSet<Version>();
            foreach (var d in all)
            {
                var v = ParseTag(Str(d, "tag_name"));
                if (v > target.Version || !seen.Add(v)) continue;
                notes.Add(new KeyValuePair<Version, string>(v, Str(d, "body")));
            }
            notes.Sort((a, b) => b.Key.CompareTo(a.Key));
            var sb = new StringBuilder();
            foreach (var n in notes)
            {
                string body = Regex.Replace((n.Value ?? "").Replace("\r", ""), @"^#+\s*", "", RegexOptions.Multiline).Trim();
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append("## v").Append(Short(n.Key)).Append("\n").Append(body.Length > 0 ? body : L.S("No notes", "Ingen noter"));
            }
            target.Notes = sb.ToString();
            return target;
        }

        /// Plain-text version of the release notes (markdown stripped a little), shortened.
        public static string NotesText(UpdateInfo u, int max)
        {
            string s = (u.Notes ?? "").Replace("\r", "");
            // keep our "## v1.2.3" version headings (the UI shows them bold), strip other headings
            s = Regex.Replace(s, @"^(?>#+)(?![ \t]*v\d+(?:\.\d+){1,3}[ \t]*$)\s*", "", RegexOptions.Multiline);
            s = Regex.Replace(s, @"\*\*(.+?)\*\*", "$1");
            s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]+\)", "$1");
            s = Regex.Replace(s, @"^\s*[-*]\s+", "• ", RegexOptions.Multiline);
            s = Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
            if (max > 0 && s.Length > max) s = s.Substring(0, max).TrimEnd() + "…";
            return s;
        }

        static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        static bool IsTrustedUrl(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps &&
                   uri.Host.Equals(AllowedHost, StringComparison.OrdinalIgnoreCase) &&
                   uri.AbsolutePath.StartsWith("/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase);
        }

        /// Downloads the new exe next to the running one and verifies it. Returns the downloaded file's path.
        public static string Download(UpdateInfo u, IProgress<int> progress, CancellationToken ct)
        {
            if (AppSettings.TestMode) throw new UpdateException(TestModeMessage());
            if (!IsTrustedUrl(u.DownloadUrl)) throw new UpdateException(L.F("The download link doesn't point to the {0} releases on GitHub.",
                "Download-linket peger ikke på udgivelserne af {0} på GitHub.", Repo));
            if (u.Sha256 == null) throw new UpdateException(L.S("GitHub didn't provide a checksum for this release, so it can't be verified.",
                "GitHub gav ingen kontrolsum for denne version, så den kan ikke kontrolleres."));

            string target = System.Windows.Forms.Application.ExecutablePath + ".download";
            try { File.Delete(target); } catch { }
            try { File.WriteAllBytes(target, new byte[0]); }
            catch (Exception) { throw new UpdateException(L.F("The program's folder isn't writable ({0}). Download the new version manually.",
                "Der kan ikke skrives i programmets mappe ({0}). Hent den nye version manuelt.", Path.GetDirectoryName(target))); }

            using (var wc = Client())
            using (ct.Register(wc.CancelAsync))
            {
                var done = new ManualResetEvent(false);
                Exception error = null;
                bool cancelled = false;
                wc.DownloadProgressChanged += (s, e) => { if (progress != null) progress.Report(e.ProgressPercentage); };
                wc.DownloadFileCompleted += (s, e) => { error = e.Error; cancelled = e.Cancelled; done.Set(); };
                wc.DownloadFileAsync(new Uri(u.DownloadUrl), target);
                done.WaitOne();
                if (cancelled) { TryDelete(target); throw new OperationCanceledException(); }
                if (error != null) { TryDelete(target); throw new UpdateException(L.F("Download failed ({0})", "Download mislykkedes ({0})", error.Message)); }
            }

            try
            {
                if (u.Size > 0 && new FileInfo(target).Length != u.Size)
                    throw new UpdateException(L.S("The download is incomplete.", "Downloaden er ufuldstændig."));
                if (Sha256Of(target) != u.Sha256)
                    throw new UpdateException(L.S("The download's checksum doesn't match GitHub's, so it wasn't installed.",
                        "Downloadens kontrolsum passer ikke med GitHubs, så den blev ikke installeret."));
                AssemblyName name;
                try { name = AssemblyName.GetAssemblyName(target); }
                catch (Exception) { throw new UpdateException(L.S("The download isn't a valid program file.", "Downloaden er ikke en gyldig programfil.")); }
                if (name.Name != Assembly.GetExecutingAssembly().GetName().Name || Normalize(name.Version) != u.Version)
                    throw new UpdateException(L.F("The downloaded program isn't version {0} of this app.",
                        "Det hentede program er ikke version {0} af dette program.", Short(u.Version)));
            }
            catch { TryDelete(target); throw; }
            return target;
        }

        /// Swaps the running exe for the downloaded one (a running exe can be renamed, not overwritten)
        /// and starts the new version. The caller should exit right after.
        public static void InstallAndRestart(string downloaded)
        {
            if (AppSettings.TestMode) throw new UpdateException(TestModeMessage());
            string exe = System.Windows.Forms.Application.ExecutablePath;
            string old = exe + ".old";
            TryDelete(old);
            File.Move(exe, old);
            try { File.Move(downloaded, exe); }
            catch (Exception ex)
            {
                try { File.Move(old, exe); } catch { }   // roll back
                throw new UpdateException(L.F("Couldn't replace the program file ({0})", "Kunne ikke udskifte programfilen ({0})", ex.Message));
            }
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        /// Removes leftovers from a previous update. Called at startup.
        public static void CleanUp()
        {
            string exe = System.Windows.Forms.Application.ExecutablePath;
            // the old process may still be closing for a moment after the restart
            for (int i = 0; i < 10 && File.Exists(exe + ".old"); i++)
            {
                TryDelete(exe + ".old");
                if (File.Exists(exe + ".old")) Thread.Sleep(300);
            }
            TryDelete(exe + ".download");
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    /// Shared by Updater, Printables and PowerPrice: TLS 1.2, one WebClient setup, one JSON string reader.
    internal static class Net
    {
        static Net()
        {
            EnsureTls();
        }

        /// Turns TLS 1.2 on (GitHub and the other services refuse older TLS). The exe has no target-framework
        /// attribute, so .NET would otherwise offer only SSL3/TLS 1.0. Safe to call any number of times, from any thread;
        /// call it before every request that doesn't go through Net.Client (and once at startup).
        public static void EnsureTls()
        {
            if ((ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls12) == 0)
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public const string DefaultUserAgent = "PrintPrice/1.0 (3D print price calculator)";

        public static WebClient Client(string userAgent)
        {
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = userAgent;
            return wc;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : null;
        }

        /// True if the bytes start like a PNG or JPEG file.
        public static bool IsImage(byte[] b)
        {
            if (b == null || b.Length < 8) return false;
            bool png = b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
            bool jpg = b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
            return png || jpg;
        }
    }
}
