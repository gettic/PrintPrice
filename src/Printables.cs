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
        public List<RemoteFile> Files = new List<RemoteFile>();
    }

    /// Minimal client for the public Printables GraphQL API (no login needed).
    public static class Printables
    {
        const string Endpoint = "https://api.printables.com/graphql/";
        static readonly Regex IdRx = new Regex(@"printables\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?model/(\d+)", RegexOptions.IgnoreCase);

        static Printables()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public static string ParseId(string url)
        {
            var m = IdRx.Match(url ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

        static WebClient Client()
        {
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "PrintPrice/1.0 (3D print price calculator)";
            return wc;
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
                catch (WebException ex) { throw new SliceException("Could not reach Printables: " + ex.Message); }
            }
            var d = json.Deserialize<Dictionary<string, object>>(resp);
            object data;
            if (!d.TryGetValue("data", out data) || data == null)
                throw new SliceException("Printables returned an error for this model.");
            return (Dictionary<string, object>)data;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : null;
        }

        public static RemoteModel GetModel(string id)
        {
            if (!Regex.IsMatch(id ?? "", @"^\d+$")) throw new ArgumentException("Bad model id");
            var data = Query("query { print(id: \"" + id + "\") { id name stls { id name fileSize } } }");
            var print = data["print"] as Dictionary<string, object>;
            if (print == null) throw new SliceException("Model " + id + " was not found on Printables (it may be private or removed).");
            var model = new RemoteModel { Id = id, Name = Str(print, "name") };
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
            if (string.IsNullOrEmpty(link)) throw new SliceException("Printables did not provide a download link for " + file.Name + ".");
            using (var wc = Client())
            {
                try { wc.DownloadFile(link, targetPath); }
                catch (WebException ex) { throw new SliceException("Download of " + file.Name + " failed: " + ex.Message); }
            }
        }

        public static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
