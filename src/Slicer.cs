using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;

namespace PrintPrice
{
    public class SliceResult
    {
        public double Seconds;
        public double Grams;
        public int Plates;
        public bool FromSlicedFile;   // values read from an already-sliced .gcode.3mf
        public byte[] Picture;        // plate picture (PNG) from the sliced 3MF, null if none
    }

    public class SliceException : Exception
    {
        public SliceException(string message) : base(message) { }
    }

    /// Drives the Bambu Studio command line to slice models with Bambu Lab A1 profiles.
    public static class BambuSlicer
    {
        public static string MachinePreset(string nozzleSize)
        {
            return "Bambu Lab A1 " + nozzleSize + " nozzle";
        }

        /// Process/filament preset suffix for a nozzle: 0.4 is "@BBL A1", others "@BBL A1 0.6 nozzle".
        public static string Suffix(string nozzleSize)
        {
            return nozzleSize == "0.4" ? " @BBL A1" : " @BBL A1 " + nozzleSize + " nozzle";
        }

        public static readonly string[] ModelExtensions = { ".stl", ".3mf", ".obj", ".step", ".stp", ".amf" };
        const int TimeoutMinutes = 15;

        public static bool IsModelFile(string name)
        {
            string ext = Path.GetExtension(name ?? "").ToLowerInvariant();
            return ModelExtensions.Contains(ext);
        }

        public static string FindExe(string configured)
        {
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
            var candidates = new List<string>();
            foreach (var pf in new[] {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, "Bambu Studio", "bambu-studio.exe"));
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Bambu Studio", "bambu-studio.exe"));
            foreach (var c in candidates) if (File.Exists(c)) return c;
            return null;
        }

        /// Profile folders: Bambu Studio's updated copy in %APPDATA% first, then the install folder.
        static List<string> Roots(string exe)
        {
            var roots = new List<string>();
            string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BambuStudio", "system", "BBL");
            if (Directory.Exists(Path.Combine(user, "machine"))) roots.Add(user);
            if (exe != null)
            {
                string inst = Path.Combine(Path.GetDirectoryName(exe), "resources", "profiles", "BBL");
                if (Directory.Exists(Path.Combine(inst, "machine"))) roots.Add(inst);
            }
            return roots;
        }

        /// Short preset names (without "@BBL A1 …") that exist for the given suffix.
        static SortedSet<string> ListShort(string exe, string kind, string suffix)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Roots(exe))
            {
                string dir = Path.Combine(r, kind);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*" + suffix + ".json", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        set.Add(name.Substring(0, name.Length - suffix.Length));
                }
            }
            return set;
        }

        public static List<string> ListProcesses(string exe, string nozzleSize)
        {
            return ListShort(exe, "process", Suffix(nozzleSize)).ToList();
        }

        /// 0.2 nozzles need dedicated filament presets; 0.6/0.8 can also use the standard (0.4) ones.
        public static List<string> ListFilaments(string exe, string nozzleSize)
        {
            var set = ListShort(exe, "filament", Suffix(nozzleSize));
            if (nozzleSize != "0.2" && nozzleSize != "0.4") set.UnionWith(ListShort(exe, "filament", Suffix("0.4")));
            return set.ToList();
        }

        static string ResolveFilament(List<string> roots, string shortName, string nozzleSize)
        {
            string specific = shortName + Suffix(nozzleSize);
            foreach (var r in roots)
            {
                string dir = Path.Combine(r, "filament");
                if (Directory.Exists(dir) && Directory.GetFiles(dir, specific + ".json", SearchOption.AllDirectories).Length > 0)
                    return specific;
            }
            return shortName + Suffix("0.4");
        }

        public static string ShortName(string preset)
        {
            return System.Text.RegularExpressions.Regex.Replace(preset ?? "", @"\s*@BBL A1.*$", "");
        }

        /// Reads filament type and whether Bambu marks it abrasive (required nozzle hardness ≥ 40 HRC).
        public static void FilamentInfo(string exe, string shortName, string nozzleSize, out string type, out bool abrasive)
        {
            var roots = Roots(exe);
            var f = Flatten(roots, "filament", ResolveFilament(roots, shortName, nozzleSize), 0);
            type = FirstString(f, "filament_type") ?? "";
            double hrc;
            abrasive = double.TryParse(FirstString(f, "required_nozzle_HRC"), NumberStyles.Float, CultureInfo.InvariantCulture, out hrc) && hrc >= 40;
        }

        static string FirstString(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            var list = v as System.Collections.IEnumerable;
            if (list != null && !(v is string))
                foreach (var o in list) return Convert.ToString(o, CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };
        }

        static string FindProfile(List<string> roots, string kind, string name)
        {
            foreach (var r in roots)
            {
                string p = Path.Combine(r, kind, name + ".json");
                if (File.Exists(p)) return p;
            }
            foreach (var r in roots)
            {
                string dir = Path.Combine(r, kind);
                if (!Directory.Exists(dir)) continue;
                var hits = Directory.GetFiles(dir, name + ".json", SearchOption.AllDirectories);
                if (hits.Length > 0) return hits[0];
            }
            throw new SliceException(L.F("Bambu Studio profile not found: \"{0}\". Pick another profile in Settings.",
                "Bambu Studio-profilen blev ikke fundet: \"{0}\". Vælg en anden profil i Indstillinger.", name));
        }

        /// The CLI does not resolve "inherits" chains, so merge parent → child ourselves.
        static Dictionary<string, object> Flatten(List<string> roots, string kind, string name, int depth)
        {
            if (depth > 25) throw new SliceException(L.F("Profile inheritance too deep: {0}", "Profilen arver i for mange led: {0}", name));
            var own = Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(FindProfile(roots, kind, name)));
            var merged = new Dictionary<string, object>();
            object parent;
            if (own.TryGetValue("inherits", out parent) && parent is string && ((string)parent).Length > 0)
                merged = Flatten(roots, kind, (string)parent, depth + 1);
            foreach (var kv in own) merged[kv.Key] = kv.Value;
            return merged;
        }

        /// Writes fully merged machine/process/filament profiles for the chosen nozzle into workDir.
        public static void PrepareProfiles(string exe, string nozzleSize, string process, string filament, string bedType, string workDir)
        {
            var roots = Roots(exe);
            if (roots.Count == 0) throw new SliceException(L.S("Could not find Bambu Studio's printer profiles.", "Kunne ikke finde Bambu Studios printerprofiler."));
            Directory.CreateDirectory(workDir);
            var json = Json();
            File.WriteAllText(Path.Combine(workDir, "machine.json"), json.Serialize(Flatten(roots, "machine", MachinePreset(nozzleSize), 0)));
            var proc = Flatten(roots, "process", ShortName(process) + Suffix(nozzleSize), 0);
            if (!string.IsNullOrEmpty(bedType)) proc["curr_bed_type"] = bedType;   // CLI default is Cool Plate
            File.WriteAllText(Path.Combine(workDir, "process.json"), json.Serialize(proc));
            File.WriteAllText(Path.Combine(workDir, "filament.json"),
                json.Serialize(Flatten(roots, "filament", ResolveFilament(roots, ShortName(filament), nozzleSize), 0)));
        }

        static string Quote(string a)
        {
            return "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        /// Slices one model file (all plates) with the profiles written by PrepareProfiles.
        public static SliceResult SliceFile(string exe, string model, string workDir, CancellationToken ct)
        {
            string outDir = Path.Combine(workDir, "out_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(outDir);
            var args = new[] {
                "--slice", "0", "--arrange", "1", "--orient", "0",
                "--load-settings", Path.Combine(workDir, "machine.json") + ";" + Path.Combine(workDir, "process.json"),
                "--load-filaments", Path.Combine(workDir, "filament.json"),
                "--outputdir", outDir, "--export-3mf", "out.3mf", model };
            var psi = new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote)))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = outDir
            };

            int exit;
            var sw = Stopwatch.StartNew();
            using (var proc = Process.Start(psi))
            {
                while (!proc.WaitForExit(250))
                {
                    if (ct.IsCancellationRequested || sw.Elapsed.TotalMinutes > TimeoutMinutes)
                    {
                        try { proc.Kill(); } catch { }
                        ct.ThrowIfCancellationRequested();
                        throw new SliceException(L.F("Slicing took longer than {0} minutes and was stopped.",
                            "Slicing tog mere end {0} minutter og blev stoppet.", TimeoutMinutes));
                    }
                }
                exit = proc.ExitCode;
            }

            string error = null;
            string resultJson = Path.Combine(outDir, "result.json");
            if (File.Exists(resultJson))
            {
                try
                {
                    var r = Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(resultJson));
                    object e;
                    if (r.TryGetValue("error_string", out e)) error = e as string;
                }
                catch { }
            }
            string out3mf = Path.Combine(outDir, "out.3mf");
            if (exit != 0 || !File.Exists(out3mf))
                throw new SliceException(L.F("Bambu Studio could not slice {0}: {1}", "Bambu Studio kunne ikke slice {0}: {1}",
                    Path.GetFileName(model), string.IsNullOrEmpty(error) ? L.F("exit code {0}", "fejlkode {0}", exit) : error));

            var res = ReadSliceInfo(out3mf);
            if (res == null) throw new SliceException(L.F("Bambu Studio returned no print estimate for {0}.",
                "Bambu Studio gav intet printestimat for {0}.", Path.GetFileName(model)));
            return res;
        }

        /// True for a .3mf that already contains sliced plates (e.g. exported "*.gcode.3mf").
        public static bool IsSlicedProject(string path)
        {
            if (!path.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                using (var zip = ZipFile.OpenRead(path))
                {
                    bool hasGcode = zip.Entries.Any(e => e.FullName.StartsWith("Metadata/plate_", StringComparison.OrdinalIgnoreCase)
                                                      && e.FullName.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase));
                    if (!hasGcode) return false;
                }
                return ReadSliceInfo(path) != null;
            }
            catch { return false; }
        }

        /// Sums print time and filament weight over all sliced plates in a Bambu .3mf.
        public static SliceResult ReadSliceInfo(string path3mf)
        {
            string xml;
            using (var zip = ZipFile.OpenRead(path3mf))
            {
                var entry = zip.GetEntry("Metadata/slice_info.config");
                if (entry == null) return null;
                using (var sr = new StreamReader(entry.Open())) xml = sr.ReadToEnd();
            }
            var res = ParseSliceInfo(xml);
            if (res != null) res.Picture = FromProject(path3mf);
            return res;
        }

        const long MaxPictureBytes = 4 * 1048576;

        /// Picture of the model from a .3mf (sliced or not): PNG bytes, or null if none / on any error.
        public static byte[] FromProject(string path3mf)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(path3mf))
                {
                    string name = PickPictureEntry(zip.Entries.Select(e => e.FullName));
                    if (name == null) return null;
                    var entry = zip.GetEntry(name);
                    if (entry == null || entry.Length <= 0 || entry.Length > MaxPictureBytes) return null;
                    using (var s = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        var bytes = ms.ToArray();
                        return Net.IsImage(bytes) ? bytes : null;
                    }
                }
            }
            catch { return null; }
        }

        /// Which zip entry holds the best model picture: Metadata/plate_1.png, then Metadata/thumbnail.png,
        /// then any other Metadata/*.png (sorted; pick_/top_/no_light helper renders and _small last or skipped). Null if none.
        public static string PickPictureEntry(IEnumerable<string> names)
        {
            var pngs = (names ?? Enumerable.Empty<string>())
                .Where(n => n != null && n.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)
                         && n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         && n.IndexOf('/', 9) < 0)
                .ToList();
            foreach (var want in new[] { "Metadata/plate_1.png", "Metadata/thumbnail.png" })
            {
                var hit = pngs.FirstOrDefault(n => string.Equals(n, want, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            return pngs
                .Where(n => !Regex.IsMatch(n, @"^Metadata/(pick_|top_|plate_no_light_)", RegexOptions.IgnoreCase))
                .OrderBy(n => n.EndsWith("_small.png", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        public static SliceResult ParseSliceInfo(string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var res = new SliceResult();
            foreach (XmlNode plate in doc.SelectNodes("/config/plate"))
            {
                double seconds = Meta(plate, "prediction");
                if (seconds <= 0) continue;
                double grams = Meta(plate, "weight");
                if (grams <= 0)
                {
                    foreach (XmlNode f in plate.SelectNodes("filament"))
                        grams += Attr(f, "used_g");
                }
                res.Seconds += seconds;
                res.Grams += grams;
                res.Plates++;
            }
            return res.Plates > 0 ? res : null;
        }

        static double Meta(XmlNode plate, string key)
        {
            var n = plate.SelectSingleNode("metadata[@key='" + key + "']");
            return n == null ? 0 : Attr(n, "value");
        }

        static double Attr(XmlNode n, string name)
        {
            var a = n.Attributes == null ? null : n.Attributes[name];
            double v;
            if (a != null && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0) return v;
            return 0;
        }
    }
}
