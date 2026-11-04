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
        public List<FilamentUse> Filaments = new List<FilamentUse>();   // per slot, summed over plates, sorted by slot
        public double PurgeGrams = -1;          // flush/purge waste (already inside Grams); -1 = unknown
        public double ColourChangeHours = -1;   // extra print time from colour changes (inside Seconds); -1 = unknown
    }

    /// One filament slot used by a sliced print (summed over all plates).
    public class FilamentUse
    {
        public int Slot;              // 1-based filament slot in the project
        public string Colour = "";    // "#RRGGBB", "" if unknown
        public string Type = "";      // e.g. "PLA"
        public string Profile = "";   // e.g. "Bambu PLA Basic @BBL A1", "" if unknown
        public double Grams;          // incl. purge/flush
        public double ModelGrams = -1;   // model only (no purge); -1 = unknown
        public double Metres;
    }

    /// Numbers from the Bambu CLI's result.json, summed over all sliced plates.
    public class SliceStats
    {
        public SortedDictionary<int, double> MainGrams = new SortedDictionary<int, double>();    // per slot, model only
        public SortedDictionary<int, double> TotalGrams = new SortedDictionary<int, double>();   // per slot, incl. purge
        public double MainSeconds;     // print time without colour changes
        public double TotalSeconds;    // full print time
        public int ColourChanges;      // filament_change_times
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

        /// Slices one model file (all plates) with the profiles written by PrepareProfiles (one filament, slot 1).
        public static SliceResult SliceFile(string exe, string model, string workDir, CancellationToken ct)
        {
            return SliceFile(exe, model, workDir, null, null, ct);
        }

        /// As above; a project .3mf with 2+ filament slots gets one filament profile per slot (SlotFilaments):
        /// slots naming nothing (IsUnstatedFilament) get `defaultFilament` (Settings' filament), named ones keep theirs.
        /// nozzleSize / defaultFilament null = one filament as before.
        public static SliceResult SliceFile(string exe, string model, string workDir, string nozzleSize, string defaultFilament, CancellationToken ct)
        {
            string outDir = Path.Combine(workDir, "out_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(outDir);
            string filaments = null;
            if (!string.IsNullOrEmpty(nozzleSize) && !string.IsNullOrEmpty(defaultFilament))
                filaments = SlotFilaments(exe, model, nozzleSize, defaultFilament, outDir);
            var args = new[] {
                "--slice", "0", "--arrange", "1", "--orient", "0",
                "--load-settings", Path.Combine(workDir, "machine.json") + ";" + Path.Combine(workDir, "process.json"),
                "--load-filaments", filaments ?? Path.Combine(workDir, "filament.json"),
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
            string resultText = null;
            string resultJson = Path.Combine(outDir, "result.json");
            if (File.Exists(resultJson))
            {
                try
                {
                    resultText = File.ReadAllText(resultJson);
                    var r = Json().Deserialize<Dictionary<string, object>>(resultText);
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
            ApplySliceStats(res, ParseSliceResult(resultText));
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
            string xml, settings;
            using (var zip = ZipFile.OpenRead(path3mf))
            {
                var entry = zip.GetEntry("Metadata/slice_info.config");
                if (entry == null) return null;
                using (var sr = new StreamReader(entry.Open())) xml = sr.ReadToEnd();
                settings = ProjectSettings(zip);
            }
            var res = ParseSliceInfo(xml);
            if (res != null)
            {
                res.Picture = FromProject(path3mf);
                var profiles = ParseFilamentSlots(settings);
                foreach (var f in res.Filaments)
                    if (f.Slot >= 1 && f.Slot <= profiles.Count) f.Profile = profiles[f.Slot - 1];
            }
            return res;
        }

        /// Filament profile names per slot from Metadata/project_settings.config (JSON, "filament_settings_id").
        /// Index 0 = slot 1. Empty list on missing or bad input, never null.
        public static List<string> ParseFilamentSlots(string projectSettingsJson)
        {
            return ParseSlotStrings(projectSettingsJson, "filament_settings_id");
        }

        /// One string per slot from a project_settings.config array (e.g. "filament_colour"). Empty list on missing or bad input.
        public static List<string> ParseSlotStrings(string projectSettingsJson, string key)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(projectSettingsJson)) return list;
            try
            {
                var d = Json().Deserialize<Dictionary<string, object>>(projectSettingsJson);
                object v;
                if (d == null || !d.TryGetValue(key, out v) || v == null) return list;
                if (v is string) { list.Add((string)v); return list; }
                var items = v as System.Collections.IEnumerable;
                if (items != null)
                    foreach (var o in items) list.Add(Convert.ToString(o, CultureInfo.InvariantCulture) ?? "");
            }
            catch { list.Clear(); }
            return list;
        }

        /// Metadata/project_settings.config of an open 3MF as text, null if missing, too big (64 MB) or unreadable.
        static string ProjectSettings(ZipArchive zip)
        {
            var ps = zip.GetEntry("Metadata/project_settings.config");
            if (ps == null || ps.Length <= 0 || ps.Length >= 64 * 1048576) return null;
            try { using (var sr = new StreamReader(ps.Open())) return sr.ReadToEnd(); }
            catch { return null; }
        }

        // ---------- default filament per colour (idea 17) ----------

        /// A filament name without its printer part (" @BBL X1C", " @System" …) and a " - Copy" / "(Copy)" ending, trimmed.
        public static string FilamentBaseName(string profile)
        {
            string s = Regex.Replace(profile ?? "", @"\s*@.*$", "").Trim();
            string before;
            do
            {
                before = s;
                s = Regex.Replace(s, @"\s*(-\s*copy|\(\s*copy\s*\))\s*$", "", RegexOptions.IgnoreCase).Trim();
            } while (s != before);
            return s;
        }

        /// True when a slot names no real filament: empty, or a generic PLA placeholder ("PLA", "Generic PLA",
        /// "Creality Generic PLA", "PLA @BBL A1", "PLA - Copy"). Named filaments ("Bambu PLA Silk+", "PLA+",
        /// "Generic PETG", "Generic PLA Silk") are stated.
        public static bool IsUnstatedFilament(string profile)
        {
            string s = FilamentBaseName(profile);
            return s.Length == 0 || Regex.IsMatch(s, @"^(?:.*\bgeneric\s+)?pla$", RegexOptions.IgnoreCase);
        }

        /// Per slot, the A1 filament profile to load: unstated slots -> a1Name(default), named ones -> a1Name(their base
        /// name); null = no A1 profile, the slot keeps the file's own filament (empty --load-filaments entry).
        /// a1Name maps a short name to the A1 profile name, or null when it doesn't exist. Never null.
        public static List<string> SlotPlan(List<string> fileProfiles, string defaultShort, Func<string, string> a1Name)
        {
            var plan = new List<string>();
            if (fileProfiles == null || a1Name == null) return plan;
            foreach (var p in fileProfiles)
            {
                string name = IsUnstatedFilament(p) ? FilamentBaseName(defaultShort) : FilamentBaseName(p);
                string a1 = null;
                if (name.Length > 0) { try { a1 = a1Name(name); } catch { a1 = null; } }
                plan.Add(a1);
            }
            return plan;
        }

        /// The A1 profile name for a short filament name (the nozzle's own, else the 0.4 one), null if neither exists.
        static string A1Filament(List<string> roots, string shortName, string nozzleSize)
        {
            foreach (var name in new[] { shortName + Suffix(nozzleSize), shortName + Suffix("0.4") })
                foreach (var r in roots)
                {
                    string dir = Path.Combine(r, "filament");
                    if (!Directory.Exists(dir)) continue;
                    if (File.Exists(Path.Combine(dir, name + ".json")) ||
                        Directory.GetFiles(dir, name + ".json", SearchOption.AllDirectories).Length > 0) return name;
                }
            return null;
        }

        /// For a project .3mf with 2+ filament slots: writes one flattened filament profile per planned slot into outDir
        /// (colour = the file's slot colour, "from" kept) and returns the --load-filaments value ("a;b;;d", empty = the
        /// file's own). Null = use filament.json as before: not a .3mf, 0-1 slots, no slot with an A1 profile, or any error.
        static string SlotFilaments(string exe, string model, string nozzleSize, string defaultFilament, string outDir)
        {
            if (!model.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                string settings;
                using (var zip = ZipFile.OpenRead(model)) settings = ProjectSettings(zip);
                var profiles = ParseFilamentSlots(settings);
                if (profiles.Count < 2) return null;
                var colours = ParseSlotStrings(settings, "filament_colour");
                var roots = Roots(exe);
                var plan = SlotPlan(profiles, ShortName(defaultFilament), n => A1Filament(roots, n, nozzleSize));
                if (plan.All(p => p == null)) return null;
                var json = Json();
                var flat = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
                var parts = new List<string>();
                for (int i = 0; i < plan.Count; i++)
                {
                    if (plan[i] == null) { parts.Add(""); continue; }
                    Dictionary<string, object> f;
                    if (!flat.TryGetValue(plan[i], out f)) { f = Flatten(roots, "filament", plan[i], 0); flat[plan[i]] = f; }
                    var slot = new Dictionary<string, object>(f);
                    string colour = i < colours.Count ? NormColour(colours[i]) : "";
                    if (colour.Length > 0) slot["filament_colour"] = new[] { colour };
                    string path = Path.Combine(outDir, "slot" + (i + 1) + ".json");
                    File.WriteAllText(path, json.Serialize(slot));
                    parts.Add(path);
                }
                return string.Join(";", parts);
            }
            catch (Exception) { return null; }
        }

        /// Reads an already-sliced .3mf like ReadSliceInfo(path); with 2+ colours, colours naming no filament get
        /// `defaultFilament` as their name (ApplyDefaultFilament). Grams and time stay the file's.
        public static SliceResult ReadSliceInfo(string path3mf, string defaultFilament)
        {
            var res = ReadSliceInfo(path3mf);
            if (res != null) ApplyDefaultFilament(res.Filaments, defaultFilament);
            return res;
        }

        /// 2+ filaments only: each one whose Profile is unstated (IsUnstatedFilament) and whose Type is empty or PLA gets
        /// Profile = defaultFilament. One filament, null list or empty default: unchanged.
        public static void ApplyDefaultFilament(List<FilamentUse> filaments, string defaultFilament)
        {
            if (filaments == null || string.IsNullOrEmpty(defaultFilament)) return;
            if (filaments.Count(f => f != null) < 2) return;
            foreach (var f in filaments)
            {
                if (f == null || !IsUnstatedFilament(f.Profile)) continue;
                string type = (f.Type ?? "").Trim();
                if (type.Length == 0 || string.Equals(type, "PLA", StringComparison.OrdinalIgnoreCase)) f.Profile = defaultFilament;
            }
        }

        /// Per-filament model/total grams and model/total time from the CLI's result.json, summed over plates.
        /// Null on missing or bad input.
        public static SliceStats ParseSliceResult(string resultJson)
        {
            if (string.IsNullOrEmpty(resultJson)) return null;
            try
            {
                var d = Json().Deserialize<Dictionary<string, object>>(resultJson);
                object platesObj;
                if (d == null || !d.TryGetValue("sliced_plates", out platesObj)) return null;
                var plates = platesObj as System.Collections.IEnumerable;
                if (plates == null) return null;
                var st = new SliceStats();
                int count = 0;
                foreach (var po in plates)
                {
                    var p = po as Dictionary<string, object>;
                    if (p == null) continue;
                    count++;
                    st.MainSeconds += JsonNum(p, "main_predication");
                    st.TotalSeconds += JsonNum(p, "total_predication");
                    st.ColourChanges += (int)JsonNum(p, "filament_change_times");
                    object fo;
                    var fils = p.TryGetValue("filaments", out fo) ? fo as System.Collections.IEnumerable : null;
                    if (fils == null) continue;
                    foreach (var o in fils)
                    {
                        var f = o as Dictionary<string, object>;
                        if (f == null) continue;
                        int slot = (int)JsonNum(f, "id");
                        if (slot <= 0) continue;
                        AddTo(st.MainGrams, slot, JsonNum(f, "main_used_g"));
                        AddTo(st.TotalGrams, slot, JsonNum(f, "total_used_g"));
                    }
                }
                return count > 0 ? st : null;
            }
            catch { return null; }
        }

        static double JsonNum(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null || v is string) return 0;
            try
            {
                double x = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return double.IsNaN(x) || double.IsInfinity(x) ? 0 : x;
            }
            catch { return 0; }
        }

        static void AddTo(SortedDictionary<int, double> d, int key, double v)
        {
            double old;
            d.TryGetValue(key, out old);
            d[key] = old + v;
        }

        /// Fills ModelGrams per filament, PurgeGrams and ColourChangeHours from result.json numbers.
        /// Does nothing when stats is null (e.g. a user's .gcode.3mf): those stay -1. Seconds/Grams are not changed.
        public static void ApplySliceStats(SliceResult res, SliceStats stats)
        {
            if (res == null || stats == null || stats.TotalGrams.Count == 0) return;
            foreach (var f in res.Filaments)
            {
                double main;
                if (stats.MainGrams.TryGetValue(f.Slot, out main)) f.ModelGrams = main;
            }
            double purge = 0;
            foreach (var kv in stats.TotalGrams)
            {
                double main;
                stats.MainGrams.TryGetValue(kv.Key, out main);
                purge += kv.Value - main;
            }
            res.PurgeGrams = Math.Max(0, purge);
            if (stats.TotalSeconds > 0 && stats.MainSeconds > 0)
                res.ColourChangeHours = Math.Max(0, stats.TotalSeconds - stats.MainSeconds) / 3600.0;
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
            var slots = new SortedDictionary<int, FilamentUse>();
            foreach (XmlNode plate in doc.SelectNodes("/config/plate"))
            {
                double seconds = Meta(plate, "prediction");
                if (seconds <= 0) continue;
                double grams = Meta(plate, "weight");
                bool sumFilaments = grams <= 0;
                foreach (XmlNode f in plate.SelectNodes("filament"))
                {
                    double g = Attr(f, "used_g");
                    if (sumFilaments) grams += g;
                    int slot = (int)Attr(f, "id");
                    if (g <= 0 || slot <= 0) continue;
                    FilamentUse u;
                    if (!slots.TryGetValue(slot, out u))
                    {
                        u = new FilamentUse { Slot = slot, Colour = NormColour(AttrText(f, "color")), Type = AttrText(f, "type") };
                        slots[slot] = u;
                    }
                    u.Grams += g;
                    u.Metres += Attr(f, "used_m");
                }
                res.Seconds += seconds;
                res.Grams += grams;
                res.Plates++;
            }
            res.Filaments = slots.Values.ToList();
            return res.Plates > 0 ? res : null;
        }

        static string AttrText(XmlNode n, string name)
        {
            var a = n.Attributes == null ? null : n.Attributes[name];
            return a == null ? "" : (a.Value ?? "").Trim();
        }

        /// "#rrggbb" or "#RRGGBBAA" -> "#RRGGBB"; anything else -> "".
        static string NormColour(string c)
        {
            if (c == null) return "";
            c = c.Trim();
            if (!c.StartsWith("#")) c = "#" + c;
            if (c.Length < 7 || !Regex.IsMatch(c.Substring(1, 6), "^[0-9A-Fa-f]{6}$")) return "";
            return c.Substring(0, 7).ToUpperInvariant();
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
