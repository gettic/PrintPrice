using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

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

        static WebClient Client()
        {
            return Net.Client(Net.DefaultUserAgent);
        }

        static Dictionary<string, object> Query(string query)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string body = json.Serialize(new Dictionary<string, object> { { "query", query } });
            string resp;
            using (var wc = Client())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                try { resp = wc.UploadString(Endpoint, body); }
                catch (WebException ex) { throw new SliceException(L.F("Could not reach Printables: {0}", "Kunne ikke nå Printables: {0}", ex.Message)); }
            }
            var d = json.Deserialize<Dictionary<string, object>>(resp);
            object data;
            if (!d.TryGetValue("data", out data) || data == null)
                throw new SliceException(L.S("Printables returned an error for this model.", "Printables svarede med en fejl for denne model."));
            return (Dictionary<string, object>)data;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            return Net.Str(d, key);
        }

        public static RemoteModel GetModel(string id)
        {
            if (!Regex.IsMatch(id ?? "", @"^\d+$")) throw new ArgumentException("Bad model id");
            var data = Query("query { print(id: \"" + id + "\") { id name image { filePath } images { filePath } stls { id name fileSize } } }");
            var print = data["print"] as Dictionary<string, object>;
            if (print == null) throw new SliceException(L.F("Model {0} was not found on Printables (it may be private or removed).",
                "Model {0} blev ikke fundet på Printables (den er måske privat eller fjernet).", id));
            var model = new RemoteModel { Id = id, Name = Str(print, "name"), ImageUrl = ThumbUrl(ImagePath(print)) };
            var stls = print.ContainsKey("stls") ? print["stls"] as IEnumerable : null;
            if (stls != null)
            {
                foreach (var o in stls)
                {
                    var f = o as Dictionary<string, object>;
                    if (f == null) continue;
                    long size; long.TryParse(Str(f, "fileSize"), out size);
                    model.Files.Add(new RemoteFile { Id = Str(f, "id"), Name = Str(f, "name"), Size = size });
                }
            }
            return model;
        }

        public static void Download(string printId, RemoteFile file, string targetPath)
        {
            if (!Regex.IsMatch(file.Id ?? "", @"^\d+$")) throw new ArgumentException("Bad file id");
            var data = Query("mutation { getDownloadLink(id: \"" + file.Id + "\", printId: \"" + printId +
                             "\", fileType: stl, source: model_detail) { ok output { link } } }");
            var dl = data["getDownloadLink"] as Dictionary<string, object>;
            var output = dl == null ? null : dl["output"] as Dictionary<string, object>;
            string link = Str(output, "link");
            if (string.IsNullOrEmpty(link)) throw new SliceException(L.F("Printables did not provide a download link for {0}.",
                "Printables gav intet download-link til {0}.", file.Name));
            using (var wc = Client())
            {
                try { wc.DownloadFile(link, targetPath); }
                catch (WebException ex) { throw new SliceException(L.F("Download of {0} failed: {1}", "Download af {0} mislykkedes: {1}", file.Name, ex.Message)); }
            }
        }

        const string MediaHost = "https://media.printables.com/";
        const int MaxImageBytes = 2 * 1048576;

        /// Main picture's filePath from a GraphQL reply (whole JSON text), or null.
        public static string ImagePathFromReply(string json)
        {
            try
            {
                var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
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
                    var bytes = wc.DownloadData(url);
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
    }
}
