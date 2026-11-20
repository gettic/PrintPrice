using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if !WEB
using System.Net;
using System.Threading;
using System.Threading.Tasks;
#endif
using System.Text;
using System.Text.RegularExpressions;

// The website (WEB defined) links this file for ParseId only; the network part is desktop-only.

namespace PrintPrice
{
    public class RemoteFile
    {
        public string Id;
        public string Name;
        public long Size;

        public override string ToString()
        {
            return Size > 0 ? Name + "   (" + (Size / 1048576.0).ToString("0.0", Num.Dk) + " MB)" : Name;
        }
    }

    public class RemoteModel
    {
        public string Id;
        public string Name;
        public string ImageUrl;   // 320x240 JPEG thumbnail of the main picture, null if none
        public List<RemoteFile> Files = new List<RemoteFile>();
    }

    /// Minimal client for the public Printables GraphQL API (no login needed).
    public static class Printables
    {
        const string Endpoint = "https://api.printables.com/graphql/";
        static readonly Regex IdRx = new Regex(@"printables\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?model/(\d+)", RegexOptions.IgnoreCase);

        public static string ParseId(string url)
        {
            var m = IdRx.Match(url ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

#if !WEB
        static WebClient Client()
        {
            return Net.Client(Net.DefaultUserAgent);
        }

        const int QueryStallMs = 20000, DownloadStallMs = 30000;
        const long MaxReplyBytes = 4 * 1048576;
        public const long MaxModelBytes = 1024L * 1048576;   // 1 GB per file

        static Dictionary<string, object> Query(string query, CancellationToken ct)
        {
            string body = Json.Serialize(new Dictionary<string, object> { { "query", query } });
            string resp = null;
            using (var wc = Client())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                try
                {
                    Task<string> call = null;
                    Net.Run(wc, () => call = wc.UploadStringTaskAsync(new Uri(Endpoint), body), ct, QueryStallMs, MaxReplyBytes);
                    resp = call.Result;
                }
                catch (OperationCanceledException) { throw; }
                catch (TimeoutException) { throw new SliceException(L.S("Printables didn't answer in time. Try again.", "Printables svarede ikke i tide. Prøv igen.")); }
                catch (InvalidDataException) { throw new SliceException(UnreadableText()); }
                catch (Exception ex) { throw new SliceException(L.F("Could not reach Printables: {0}", "Kunne ikke nå Printables: {0}", ex.Message)); }
            }
            return ParseReply(resp);
        }

        static string UnreadableText()
        {
            return L.S("Printables sent an answer PrintPrice can't read. Try again later.",
                       "Printables sendte et svar, som PrintPrice ikke kan læse. Prøv igen senere.");
        }

        /// The "data" object of a GraphQL reply. SliceException for text that isn't JSON (e.g. an HTML error page)
        /// and for a reply without data (GraphQL errors). Pure (tests).
        internal static Dictionary<string, object> ParseReply(string text)
        {
            Dictionary<string, object> d = null;
            try { d = Json.Deserialize<Dictionary<string, object>>(text ?? ""); }
            catch (Exception) { d = null; }
            if (d == null) throw new SliceException(UnreadableText());
            object data;
            var dd = d.TryGetValue("data", out data) ? data as Dictionary<string, object> : null;
            if (dd == null)
                throw new SliceException(L.S("Printables returned an error for this model.", "Printables svarede med en fejl for denne model."));
            return dd;
        }

        /// The model (name, picture, files) from the "data" object of the print query. SliceException when the print
        /// is missing (private or removed). Files without an id or name are left out. Pure (tests).
        internal static RemoteModel ModelFromData(string id, Dictionary<string, object> data)
        {
            object o;
            var print = data != null && data.TryGetValue("print", out o) ? o as Dictionary<string, object> : null;
            if (print == null) throw new SliceException(L.F("Model {0} was not found on Printables (it may be private or removed).",
                "Model {0} blev ikke fundet på Printables (den er måske privat eller fjernet).", id));
            var model = new RemoteModel { Id = id, Name = Str(print, "name") ?? "", ImageUrl = ThumbUrl(ImagePath(print)) };
            var stls = print.TryGetValue("stls", out o) ? o as IEnumerable : null;
            if (stls != null && !(stls is string))
            {
                foreach (var x in stls)
                {
                    var f = x as Dictionary<string, object>;
                    if (f == null) continue;
                    string fid = Str(f, "id"), name = Str(f, "name");
                    if (string.IsNullOrEmpty(fid) || string.IsNullOrEmpty(name)) continue;
                    long size; long.TryParse(Str(f, "fileSize"), out size);
                    model.Files.Add(new RemoteFile { Id = fid, Name = name, Size = size });
                }
            }
            return model;
        }

        /// The download link from the "data" object of the getDownloadLink mutation; null when missing or not an
        /// absolute https address (file:, http: and anything else are refused). Pure (tests).
        internal static string LinkFromData(Dictionary<string, object> data)
        {
            object o;
            var dl = data != null && data.TryGetValue("getDownloadLink", out o) ? o as Dictionary<string, object> : null;
            var output = dl != null && dl.TryGetValue("output", out o) ? o as Dictionary<string, object> : null;
            string link = Str(output, "link");
            Uri uri;
            if (string.IsNullOrEmpty(link) || !Uri.TryCreate(link, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
            return link;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            return Net.Str(d, key);
        }

        public static RemoteModel GetModel(string id) { return GetModel(id, CancellationToken.None); }

        /// The model's name, picture and files. ct stops the lookup (OperationCanceledException).
        public static RemoteModel GetModel(string id, CancellationToken ct)
        {
            if (!Regex.IsMatch(id ?? "", @"^\d+$")) throw new ArgumentException("Bad model id");
            var data = Query("query { print(id: \"" + id + "\") { id name image { filePath } images { filePath } stls { id name fileSize } } }", ct);
            return ModelFromData(id, data);
        }

        public static void Download(string printId, RemoteFile file, string targetPath) { Download(printId, file, targetPath, CancellationToken.None); }

        /// Downloads one file to targetPath (https only, max 1 GB, stops after 30 s without data). ct stops it
        /// (OperationCanceledException). On any failure the partial file is deleted.
        public static void Download(string printId, RemoteFile file, string targetPath, CancellationToken ct)
        {
            if (!Regex.IsMatch(printId ?? "", @"^\d+$")) throw new ArgumentException("Bad model id");
            if (file == null || !Regex.IsMatch(file.Id ?? "", @"^\d+$")) throw new ArgumentException("Bad file id");
            if (file.Size > MaxModelBytes) throw new SliceException(TooBigText(file.Name));
            var data = Query("mutation { getDownloadLink(id: \"" + file.Id + "\", printId: \"" + printId +
                             "\", fileType: stl, source: model_detail) { ok output { link } } }", ct);
            string link = LinkFromData(data);
            if (link == null) throw new SliceException(L.F("Printables did not provide a usable download link for {0}, so it wasn't downloaded.",
                "Printables gav intet brugbart download-link til {0}, så den blev ikke hentet.", file.Name));
            using (var wc = Client())
            {
                try { Net.Run(wc, () => wc.DownloadFileTaskAsync(new Uri(link), targetPath), ct, DownloadStallMs, MaxModelBytes); }
                catch (OperationCanceledException) { TryDelete(targetPath); throw; }
                catch (TimeoutException)
                {
                    TryDelete(targetPath);
                    throw new SliceException(L.F("Download of {0} stopped (no data for 30 seconds). Try again.",
                        "Download af {0} gik i stå (ingen data i 30 sekunder). Prøv igen.", file.Name));
                }
                catch (InvalidDataException) { TryDelete(targetPath); throw new SliceException(TooBigText(file.Name)); }
                catch (Exception ex)
                {
                    TryDelete(targetPath);
                    throw new SliceException(L.F("Download of {0} failed: {1}", "Download af {0} mislykkedes: {1}", file.Name, ex.Message));
                }
            }
        }

        static string TooBigText(string name)
        {
            return L.F("{0} is bigger than 1 GB, so it wasn't downloaded.", "{0} er større end 1 GB, så den blev ikke hentet.", name);
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        const string MediaHost = "https://media.printables.com/";
        const int MaxImageBytes = 2 * 1048576;

        /// Main picture's filePath from a GraphQL reply (whole JSON text), or null.
        public static string ImagePathFromReply(string json)
        {
            try
            {
                var d = Json.Deserialize<Dictionary<string, object>>(json);
                object data, print;
                if (d == null || !d.TryGetValue("data", out data)) return null;
                var dd = data as Dictionary<string, object>;
                if (dd == null || !dd.TryGetValue("print", out print)) return null;
                return ImagePath(print as Dictionary<string, object>);
            }
            catch { return null; }
        }

        /// "image.filePath", else the first "images[].filePath".
        static string ImagePath(Dictionary<string, object> print)
        {
            if (print == null) return null;
            object o;
            string p = print.TryGetValue("image", out o) ? Str(o as Dictionary<string, object>, "filePath") : null;
            if (!string.IsNullOrEmpty(p)) return p;
            var list = print.TryGetValue("images", out o) ? o as IEnumerable : null;
            if (list == null) return null;
            foreach (var i in list)
            {
                p = Str(i as Dictionary<string, object>, "filePath");
                if (!string.IsNullOrEmpty(p)) return p;
            }
            return null;
        }

        /// 320x240 JPEG thumbnail address. The folder after 320x240/ is the ORIGINAL's extension (as-is), the file is always
        /// "<stem>.jpg" (the server converts), e.g. ".../2_x/x.png" -> ".../2_x/thumbs/inside/320x240/png/x.jpg",
        /// ".../img.jpeg" -> ".../320x240/jpeg/img.jpg" (checked against media.printables.com 2026-11-04; "jpg/x.jpg" 404s for png/jpeg).
        /// No or odd extension -> "jpg" folder. Null for anything that isn't a plain relative path under media/.
        public static string ThumbUrl(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            string p = filePath.Trim().TrimStart('/');
            if (!p.StartsWith("media/", StringComparison.Ordinal) || p.Contains("..") || p.Contains("\\")
                || p.Contains("?") || p.Contains("#") || p.Contains(":")) return null;
            int slash = p.LastIndexOf('/');
            string dir = p.Substring(0, slash), file = p.Substring(slash + 1);
            int dot = file.LastIndexOf('.');
            string stem = dot > 0 ? file.Substring(0, dot) : file;
            if (stem.Length == 0) return null;
            string ext = dot > 0 ? file.Substring(dot + 1) : "";
            if (!Regex.IsMatch(ext, @"^[A-Za-z0-9]{1,5}$")) ext = "jpg";
            var parts = dir.Split('/').Select(Uri.EscapeDataString);
            return MediaHost + string.Join("/", parts) + "/thumbs/inside/320x240/" + ext + "/" + Uri.EscapeDataString(stem) + ".jpg";
        }

        /// Downloads a Printables picture (media.printables.com only, max 2 MB). Null on any failure; never throws.
        public static byte[] DownloadImage(string url)
        {
            if (url == null || !url.StartsWith(MediaHost, StringComparison.Ordinal)) return null;
            try
            {
                using (var wc = Client())
                {
                    Task<byte[]> call = null;
                    Net.Run(wc, () => call = wc.DownloadDataTaskAsync(new Uri(url)), CancellationToken.None, 15000, MaxImageBytes);
                    var bytes = call.Result;
                    return bytes != null && bytes.Length <= MaxImageBytes && Net.IsImage(bytes) ? bytes : null;
                }
            }
            catch { return null; }
        }

        public static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
#endif
    }
}
