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

namespace PrintPrice
{
    /// Numbers from the Bambu CLI's result.json, summed over all sliced plates.
    public class SliceStats
    {
        public SortedDictionary<int, double> MainGrams = new SortedDictionary<int, double>();    // per slot, model only
        public SortedDictionary<int, double> TotalGrams = new SortedDictionary<int, double>();   // per slot, incl. purge
        public double MainSeconds;     // print time without colour changes
        public double TotalSeconds;    // full print time
        public int ColourChanges;      // filament_change_times
    }

    /// Drives the Bambu Studio command line to slice models with Bambu Lab A1 / A1 mini / P1S profiles.
    public static partial class BambuSlicer
    {
        // ---------- printer models (several printers) ----------

        /// Bambu's machine preset, e.g. ("A1M", "0.4") -> "Bambu Lab A1 mini 0.4 nozzle". Unknown model = A1.
        public static string MachinePreset(string model, string nozzleSize)
        {
            return FindPrinter(model).MachineName + " " + nozzleSize + " nozzle";
        }

        /// Process/filament preset suffix for a family and nozzle: 0.4 is " @BBL A1", others " @BBL A1 0.6 nozzle".
        internal static string FamilySuffix(string family, string nozzleSize)
        {
            return nozzleSize == "0.4" ? " @BBL " + family : " @BBL " + family + " " + nozzleSize + " nozzle";
        }

        /// Path of Bambu Studio's own picture of the printer (<exe folder>\resources\images\printer_thumbnail_<x>_png.png),
        /// null when the exe or the file is missing. Never throws.
        public static string PrinterPicture(string exe, string model)
        {
            try
            {
                if (string.IsNullOrEmpty(exe)) return null;
                string p = Path.Combine(Path.GetDirectoryName(exe), "resources", "images",
                    "printer_thumbnail_" + FindPrinter(model).Thumb + "_png.png");
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        /// The error text for a model that doesn't fit the printer's build volume (CLI exit code -50).
        public static string BedErrorText(string model)
        {
            var p = FindPrinter(model);
            return L.F("The model doesn't fit on the {0}'s build plate ({1} × {2} × {3} mm), or a plate in the file is empty. " +
                       "Choose a bigger printer, or scale the model down in Bambu Studio.",
                       "Modellen passer ikke på byggepladen i {0} ({1} × {2} × {3} mm), eller en plade i filen er tom. " +
                       "Vælg en større printer, eller skalér modellen ned i Bambu Studio.",
                       p.MachineName, p.BedX, p.BedY, p.BedZ);
        }

        /// The user-facing text when the CLI fails: -50 = BedErrorText(model), else "could not slice <file>: <error>".
        public static string SliceErrorText(int exitCode, string fileName, string cliError, string model)
        {
            if (exitCode == -50) return BedErrorText(model);
            return L.F("Bambu Studio could not slice {0}: {1}", "Bambu Studio kunne ikke slice {0}: {1}",
                fileName, string.IsNullOrEmpty(cliError) ? L.F("exit code {0}", "fejlkode {0}", exitCode) : cliError);
        }

        /// As above, plus the plate/filament clash ("Filaments are not compatible with the plate type", CLI -61):
        /// PlateErrorText(bedType, clashing filaments).
        public static string SliceErrorText(int exitCode, string fileName, string cliError, string model, string bedType, IList<string> filaments)
        {
            if (IsPlateError(exitCode, cliError)) return PlateErrorText(bedType, filaments);
            return SliceErrorText(exitCode, fileName, cliError, model);
        }

        /// True when Bambu Studio refused the plate type for the filament(s): its text, or code -61 without a text.
        public static bool IsPlateError(int exitCode, string cliError)
        {
            if (!string.IsNullOrEmpty(cliError))
                return cliError.IndexOf("not compatible with the plate type", StringComparison.OrdinalIgnoreCase) >= 0;
            return exitCode == -61;
        }

        /// Our text for a filament that can't go on the chosen build plate (plate = Bambu bed type key; filaments = short
        /// names, null/empty = "The filament").
        public static string PlateErrorText(string bedType, IList<string> filaments)
        {
            string plate = (bedType ?? "").Trim();
            if (plate.Length > 0)
                foreach (var p in Wear.Plates)
                    if (string.Equals(p.Key, plate, StringComparison.OrdinalIgnoreCase)) { plate = p.Label; break; }
            if (plate.Length == 0) plate = L.S("this build plate", "denne byggeplade");
            else plate = L.S("the ", "") + plate;
            var names = (filaments ?? new string[0]).Where(n => !string.IsNullOrEmpty(n) && n.Trim().Length > 0)
                .Select(n => ShortName(n).Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string fil = names.Count == 0 ? L.S("The filament", "Filamentet") : string.Join(", ", names);
            return L.F("{0} can't be printed on {1} (Bambu Studio doesn't allow it). Pick another build plate in Settings > Printer, " +
                       "or another filament.",
                       "{0} kan ikke printes på {1} (Bambu Studio tillader det ikke). Vælg en anden byggeplade i Indstillinger > Printer, " +
                       "eller et andet filament.", fil, plate);
        }

        /// The filament profile key holding the bed temperature for a Bambu bed type ("" unknown). 0 there = not allowed.
        public static string PlateTempKey(string bedType)
        {
            switch ((bedType ?? "").Trim().ToLowerInvariant())
            {
                case "cool plate": return "cool_plate_temp";
                case "engineering plate": return "eng_plate_temp";
                case "high temp plate": return "hot_plate_temp";
                case "textured pei plate": return "textured_plate_temp";
                case "supertack plate": return "supertack_plate_temp";
                case "textured cool plate": return "textured_cool_plate_temp";
                default: return "";
            }
        }

        /// After a failed slice: the bed type from workDir\process.json and the loaded filaments (";"-list or filament.json)
        /// whose temperature for that plate is 0 (none found = all of them). Never throws.
        static List<string> ClashingFilaments(string workDir, string filamentFiles, out string bedType)
        {
            bedType = "";
            var all = new List<string>();
            var clash = new List<string>();
            try
            {
                var proc = Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(workDir, "process.json")));
                bedType = FirstString(proc, "curr_bed_type") ?? "";
                string key = PlateTempKey(bedType);
                var files = (filamentFiles ?? Path.Combine(workDir, "filament.json")).Split(';');
                foreach (var f in files)
                {
                    if (string.IsNullOrEmpty(f) || !File.Exists(f)) continue;
                    var d = Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                    string name = ShortName(FirstString(d, "name") ?? "").Trim();
                    if (name.Length == 0) continue;
                    all.Add(name);
                    double t;
                    if (key.Length > 0 && double.TryParse(FirstString(d, key), NumberStyles.Float, CultureInfo.InvariantCulture, out t) && t <= 0)
                        clash.Add(name);
                }
            }
            catch { }
            return clash.Count > 0 ? clash : all;
        }

        // ---------- profile index: which process/filament profiles fit a machine preset ----------
        // Bambu's names can't be guessed from the printer (the P1S uses "@BBL X1C" processes and a mix of P1S/X1C/P1P
        // filaments), so profiles are picked by their own "compatible_printers" list. Read once per profile folder set
        // (~2 400 files, ~0,3 s), kept in memory.

        sealed class ProfileEntry
        {
            public string Name;
            public string Inherits;
            public bool HasCompat;
            public bool Instance = true;
            public List<string> Compat = new List<string>();
        }

        static readonly object indexLock = new object();
        static readonly Dictionary<string, List<ProfileEntry>> indexCache = new Dictionary<string, List<ProfileEntry>>();
        static readonly Regex CompatRx = new Regex("\"compatible_printers\"\\s*:\\s*\\[([^\\]]*)\\]");
        static readonly Regex JsonStringRx = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
        static readonly Regex InheritsRx = new Regex("\"inherits\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        static readonly Regex InstanceRx = new Regex("\"instantiation\"\\s*:\\s*\"false\"", RegexOptions.IgnoreCase);

        /// The "compatible_printers" list of a profile's JSON text; empty when missing or bad. Never null.
        public static List<string> ParseCompatiblePrinters(string json)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(json)) return list;
            var m = CompatRx.Match(json);
            if (!m.Success) return list;
            foreach (Match s in JsonStringRx.Matches(m.Groups[1].Value))
            {
                string v;
                try { v = Regex.Unescape(s.Groups[1].Value).Trim(); } catch { v = s.Groups[1].Value.Trim(); }
                if (v.Length > 0) list.Add(v);
            }
            return list;
        }

        static List<ProfileEntry> Index(List<string> roots, string kind)
        {
            string key = kind + "|" + string.Join("|", roots);
            lock (indexLock)
            {
                List<ProfileEntry> cached;
                if (indexCache.TryGetValue(key, out cached)) return cached;
                var byName = new Dictionary<string, ProfileEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in roots)
                {
                    string dir = Path.Combine(r, kind);
                    if (!Directory.Exists(dir)) continue;
                    string[] files;
                    try { files = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories); } catch { continue; }
                    foreach (var f in files.OrderBy(x => x.Length - Path.GetFileName(x).Length))   // top-level files first
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        if (byName.ContainsKey(name)) continue;
                        string text;
                        try { text = File.ReadAllText(f); } catch { continue; }
                        var im = InheritsRx.Match(text);
                        byName[name] = new ProfileEntry {
                            Name = name, Inherits = im.Success ? im.Groups[1].Value : "",
                            HasCompat = CompatRx.IsMatch(text), Compat = ParseCompatiblePrinters(text), Instance = !InstanceRx.IsMatch(text) };
                    }
                }
                foreach (var e in byName.Values)
                {
                    if (e.HasCompat) continue;
                    ProfileEntry p = e;
                    for (int d = 0; d < 25 && !p.HasCompat && p.Inherits.Length > 0; d++)
                        if (!byName.TryGetValue(p.Inherits, out p)) break;
                    if (p != null && p.HasCompat) e.Compat = p.Compat;
                }
                var list = byName.Values.Where(e => e.Instance && e.Compat.Count > 0).ToList();
                if (list.Count > 0) indexCache[key] = list;
                return list;
            }
        }

        /// Full profile names of a kind that list this machine preset as compatible.
        static List<string> CompatibleNames(List<string> roots, string kind, string machinePreset)
        {
            return Index(roots, kind)
                .Where(e => e.Compat.Any(c => string.Equals(c, machinePreset, StringComparison.OrdinalIgnoreCase)))
                .Select(e => e.Name).ToList();
        }

        /// Pure: from compatible full names, the one for this short name: the family's own name with the nozzle beats the
        /// family's plain name, earlier families beat later ones, any other compatible name comes last (then by name).
        /// Null when none has this short name.
        public static string PickProfile(IEnumerable<string> compatibleNames, string shortName, string model, string nozzleSize, bool process)
        {
            if (compatibleNames == null) return null;
            var p = FindPrinter(model);
            var families = process ? p.ProcessFamilies : p.FilamentFamilies;
            string best = null;
            int bestRank = int.MaxValue;
            string want = ShortName(shortName);
            foreach (var n in compatibleNames)
            {
                if (n == null || !string.Equals(ShortName(n), want, StringComparison.OrdinalIgnoreCase)) continue;
                int rank = 1000;
                int at = n.IndexOf("@BBL ", StringComparison.OrdinalIgnoreCase);
                string rest = at < 0 ? "" : n.Substring(at + 5).Trim();
                for (int i = 0; i < families.Length; i++)
                {
                    if (string.Equals(rest, families[i] + " " + nozzleSize + " nozzle", StringComparison.OrdinalIgnoreCase)) { rank = 2 * i; break; }
                    if (string.Equals(rest, families[i], StringComparison.OrdinalIgnoreCase)) { rank = 2 * i + 1; break; }
                }
                if (rank < bestRank || (rank == bestRank && string.CompareOrdinal(n, best) < 0)) { best = n; bestRank = rank; }
            }
            return best;
        }

        /// The full profile name for a short name on this printer + nozzle, or null when no compatible one exists.
        static string ResolveOrNull(List<string> roots, string kind, string model, string nozzleSize, string shortName)
        {
            return PickProfile(CompatibleNames(roots, kind, MachinePreset(model, nozzleSize)), shortName, model, nozzleSize, kind == "process");
        }

        /// As ResolveOrNull; none = the family's usual name (FindProfile then reports it as missing).
        static string Resolve(List<string> roots, string kind, string model, string nozzleSize, string shortName)
        {
            string n = ResolveOrNull(roots, kind, model, nozzleSize, shortName);
            if (n != null) return n;
            var p = FindPrinter(model);
            return ShortName(shortName) + FamilySuffix((kind == "process" ? p.ProcessFamilies : p.FilamentFamilies)[0], nozzleSize);
        }

        /// Reads the profile index off the UI thread so the Settings lists open fast. Never throws.
        public static void WarmProfiles(string exe)
        {
            try { var roots = Roots(exe); Index(roots, "process"); Index(roots, "filament"); } catch { }
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

        /// Short preset names (without "@BBL …") of a kind that fit the printer + nozzle (by compatible_printers), sorted.
        static List<string> ListShort(string exe, string kind, string model, string nozzleSize)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in CompatibleNames(Roots(exe), kind, MachinePreset(model, nozzleSize)))
            {
                string s = ShortName(n).Trim();
                if (s.Length > 0) set.Add(s);
            }
            return set.ToList();
        }

        public static List<string> ListProcesses(string exe, string model, string nozzleSize)
        {
            return ListShort(exe, "process", model, nozzleSize);
        }

        /// Filaments Bambu lists as compatible with the printer + nozzle (0.6/0.8 include the standard ones, 0.2 not).
        public static List<string> ListFilaments(string exe, string model, string nozzleSize)
        {
            return ListShort(exe, "filament", model, nozzleSize);
        }

        /// A preset name without its printer part: "0.20mm Standard @BBL X1C" / "Bambu PLA Basic @BBL A1M 0.2 nozzle" -> the
        /// part before " @BBL"; OrcaSlicer's library names "Generic PLA @System" -> "Generic PLA" (only a final " @System").
        public static string ShortName(string preset)
        {
            return Regex.Replace(preset ?? "", @"\s*(@BBL\b.*|@System\s*)$", "");
        }

        /// Reads filament type and whether Bambu marks it abrasive (required nozzle hardness ≥ 40 HRC).
        public static void FilamentInfo(string exe, string model, string shortName, string nozzleSize, out string type, out bool abrasive)
        {
            var roots = Roots(exe);
            var f = Flatten(roots, "filament", Resolve(roots, "filament", model, nozzleSize, shortName), 0);
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

        static JavaScriptSerializer Serializer()
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
            var own = Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(FindProfile(roots, kind, name)));
            var merged = new Dictionary<string, object>();
            object parent;
            if (own.TryGetValue("inherits", out parent) && parent is string && ((string)parent).Length > 0)
                merged = Flatten(roots, kind, (string)parent, depth + 1);
            foreach (var kv in own) merged[kv.Key] = kv.Value;
            return merged;
        }

        /// Writes fully merged machine/process/filament profiles for the printer model ("A1", "A1M", "P1S"; unknown = A1)
        /// and nozzle into workDir. Also writes printer.txt (the model key) so SliceFile knows the printer for per-slot
        /// filaments and the bed-size error.
        public static void PrepareProfiles(string exe, string model, string nozzleSize, string process, string filament, string bedType, string workDir)
        {
            var roots = Roots(exe);
            if (roots.Count == 0) throw new SliceException(L.S("Could not find Bambu Studio's printer profiles.", "Kunne ikke finde Bambu Studios printerprofiler."));
            model = FindPrinter(model).Key;
            Directory.CreateDirectory(workDir);
            var json = Serializer();
            File.WriteAllText(Path.Combine(workDir, "machine.json"), json.Serialize(Flatten(roots, "machine", MachinePreset(model, nozzleSize), 0)));
            var proc = Flatten(roots, "process", Resolve(roots, "process", model, nozzleSize, process), 0);
            if (!string.IsNullOrEmpty(bedType)) proc["curr_bed_type"] = bedType;   // CLI default is Cool Plate
            File.WriteAllText(Path.Combine(workDir, "process.json"), json.Serialize(proc));
            File.WriteAllText(Path.Combine(workDir, "filament.json"),
                json.Serialize(Flatten(roots, "filament", Resolve(roots, "filament", model, nozzleSize, filament), 0)));
            File.WriteAllText(Path.Combine(workDir, PrinterFile), model);
        }

        const string PrinterFile = "printer.txt";

        /// The printer key PrepareProfiles wrote into workDir; "A1" when missing.
        static string WorkDirPrinter(string workDir)
        {
            try
            {
                string p = Path.Combine(workDir, PrinterFile);
                if (File.Exists(p)) return FindPrinter(File.ReadAllText(p)).Key;
            }
            catch { }
            return "A1";
        }

        static string Quote(string a)
        {
            return "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        /// Slices one model file (all plates) with the profiles written by PrepareProfiles. A project .3mf with 2+ filament
        /// slots gets one filament profile per slot (SlotFilaments): slots naming nothing (IsUnstatedFilament) get
        /// `defaultFilament` (Settings' filament), named ones keep theirs; nozzleSize / defaultFilament null = one filament
        /// (slot 1). printer = "A1", "A1M", "P1S"; null = the one PrepareProfiles used for workDir.
        /// A model too big for the printer (CLI -50) throws BedErrorText.
        public static SliceResult SliceFile(string exe, string model, string workDir, string printer, string nozzleSize, string defaultFilament, CancellationToken ct)
        {
            printer = string.IsNullOrEmpty(printer) ? WorkDirPrinter(workDir) : FindPrinter(printer).Key;
            string outDir = Path.Combine(workDir, "out_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(outDir);
            string filaments = null;
            if (!string.IsNullOrEmpty(nozzleSize) && !string.IsNullOrEmpty(defaultFilament))
                filaments = SlotFilaments(exe, model, printer, nozzleSize, defaultFilament, outDir);
            return RunSlice(exe, new[] { model }, workDir, printer, filaments, filaments ?? Path.Combine(workDir, "filament.json"),
                            null, outDir, Path.GetFileName(model), ct);
        }

        /// Runs the slicer CLI on `models` (one call) with the given --load-filaments value and extra options, in outDir
        /// (working folder and --outputdir); reads out.3mf + result.json. slotFiles = the per-slot list for the
        /// plate-clash check (null = filament.json). Errors as SliceFile's.
        static SliceResult RunSlice(string exe, IList<string> models, string workDir, string printer, string slotFiles,
                                    string loadFilaments, IList<string> extra, string outDir, string shownName, CancellationToken ct)
        {
            string filaments = slotFiles;
            var list = new List<string> {
                "--slice", "0", "--arrange", "1", "--orient", "0",
                "--load-settings", Path.Combine(workDir, "machine.json") + ";" + Path.Combine(workDir, "process.json"),
                "--load-filaments", loadFilaments,
                "--outputdir", outDir, "--export-3mf", "out.3mf" };
            if (extra != null) list.AddRange(extra);
            list.AddRange(models);
            var args = list.ToArray();
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
                    var r = Serializer().Deserialize<Dictionary<string, object>>(resultText);
                    object e;
                    if (r.TryGetValue("error_string", out e)) error = e as string;
                }
                catch { }
            }
            string out3mf = Path.Combine(outDir, "out.3mf");
            if (exit != 0 || !File.Exists(out3mf))
            {
                string bed = "";
                var clash = IsPlateError(exit, error) ? ClashingFilaments(workDir, filaments, out bed) : null;
                throw new SliceException(SliceErrorText(exit, shownName, error, printer, bed, clash));
            }

            var res = ReadSliceInfo(out3mf);
            if (res == null) throw new SliceException(L.F("Bambu Studio returned no print estimate for {0}.",
                "Bambu Studio gav intet printestimat for {0}.", shownName));
            ApplySliceStats(res, ParseSliceResult(resultText));
            return res;
        }

        /// True for a .3mf that already contains sliced plates (e.g. exported "*.gcode.3mf"), and for any G-code file
        /// (IsGcodeFile: read by ReadSliceInfo, which says so when it can't find the numbers).
        public static bool IsSlicedProject(string path)
        {
            if (IsGcodeFile(path)) return true;
            if (!path.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                using (var zip = ZipFile.OpenRead(path))
                {
                    bool hasGcode = zip.Entries.Any(e => e.FullName.StartsWith("Metadata/plate_", StringComparison.OrdinalIgnoreCase)
                                                      && e.FullName.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase));
                    if (!hasGcode) return false;
                    // = ReadSliceInfo(path) != null, without the picture and project_settings it would also read
                    var entry = zip.GetEntry("Metadata/slice_info.config");
                    if (entry == null) return false;
                    string xml;
                    using (var sr = new StreamReader(entry.Open())) xml = sr.ReadToEnd();
                    return ParseSliceInfo(xml) != null;
                }
            }
            catch { return false; }
        }

        /// Reads a sliced G-code file (Bambu Studio, OrcaSlicer, PrusaSlicer, Cura): the first 1 MB, plus the last 256 KB
        /// when something is still missing (ParseGcode). SliceException for binary G-code or when time/grams aren't found.
        public static SliceResult ReadGcodeFile(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long len = fs.Length;
                byte[] head = ReadRange(fs, 0, (int)Math.Min(len, GcodeHeadBytes));
                if (IsBinaryGcode(path, head)) throw new SliceException(BinaryGcodeText());
                string text = System.Text.Encoding.UTF8.GetString(head);
                var info = ParseGcode(text);
                if (!info.Complete && len > head.Length)
                {
                    long start = Math.Max(head.Length, len - GcodeTailBytes);
                    byte[] tail = ReadRange(fs, start, (int)(len - start));
                    info = ParseGcode(text + "\n" + System.Text.Encoding.UTF8.GetString(tail));
                }
                if (!info.HasValues) throw new SliceException(GcodeMissingText());
                var res = info.ToResult();
                res.FromSlicedFile = true;
                return res;
            }
        }

        static byte[] ReadRange(Stream s, long offset, int count)
        {
            var buf = new byte[count];
            s.Position = offset;
            int done = 0;
            while (done < count)
            {
                int n = s.Read(buf, done, count - done);
                if (n <= 0) break;
                done += n;
            }
            if (done < count) Array.Resize(ref buf, done);
            return buf;
        }

        /// Sums print time and filament weight over all sliced plates in a Bambu .3mf (a G-code file: ReadGcodeFile).
        public static SliceResult ReadSliceInfo(string path3mf)
        {
            if (IsGcodeFile(path3mf)) return ReadGcodeFile(path3mf);
            string xml, settings, modelSettings;
            using (var zip = ZipFile.OpenRead(path3mf))
            {
                var entry = zip.GetEntry("Metadata/slice_info.config");
                if (entry == null) return null;
                using (var sr = new StreamReader(entry.Open())) xml = sr.ReadToEnd();
                settings = ProjectSettings(zip);
                modelSettings = EntryText(zip, "Metadata/model_settings.config");
            }
            var res = ParseSliceInfo(xml);
            if (res != null)
            {
                ApplyPlateNames(res, modelSettings);   // idea 21: plate names for the plates list
                res.Picture = FromProject(path3mf);
                res.StudioFile = path3mf;
                var parsed = ParseSettings(settings);   // once for the slots and the printer
                var profiles = SlotStrings(parsed, "filament_settings_id");   // = ParseFilamentSlots
                foreach (var f in res.Filaments)
                    if (f.Slot >= 1 && f.Slot <= profiles.Count) f.Profile = profiles[f.Slot - 1];
                var pp = ProjectPrinterFrom(parsed, xml);
                if (pp != null)
                {
                    res.PrinterModel = pp.Key;
                    res.PrinterName = pp.Name;
                    res.PrinterNozzle = pp.Nozzle;
                    res.UnsupportedPrinter = pp.Key.Length == 0;
                }
            }
            return res;
        }

        /// Metadata/project_settings.config of an open 3MF as text, null if missing, too big (64 MB) or unreadable.
        static string ProjectSettings(ZipArchive zip)
        {
            return EntryText(zip, "Metadata/project_settings.config");
        }

        /// A zip entry of an open 3MF as text, null if missing, empty, too big (64 MB) or unreadable.
        static string EntryText(ZipArchive zip, string name)
        {
            try
            {
                var e = zip.GetEntry(name);
                if (e == null || e.Length <= 0 || e.Length >= 64 * 1048576) return null;
                using (var sr = new StreamReader(e.Open())) return sr.ReadToEnd();
            }
            catch { return null; }
        }

        // ---------- default filament per colour (idea 17) ----------

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

        /// For a project .3mf with 2+ filament slots: writes one flattened filament profile per planned slot into outDir
        /// (colour = the file's slot colour, "from" kept) and returns the --load-filaments value ("a;b;;d", empty = the
        /// file's own). Null = use filament.json as before: not a .3mf, 0-1 slots, no slot with a profile for the printer, or any error.
        static string SlotFilaments(string exe, string model, string printer, string nozzleSize, string defaultFilament, string outDir)
        {
            if (!model.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                string settings;
                using (var zip = ZipFile.OpenRead(model)) settings = ProjectSettings(zip);
                var parsed = ParseSettings(settings);
                var profiles = SlotStrings(parsed, "filament_settings_id");   // = ParseFilamentSlots
                if (profiles.Count < 2) return null;
                var colours = SlotStrings(parsed, "filament_colour");
                var roots = Roots(exe);
                var plan = SlotPlan(profiles, ShortName(defaultFilament), n => ResolveOrNull(roots, "filament", printer, nozzleSize, n));
                if (plan.All(p => p == null)) return null;
                var json = Serializer();
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
        /// A file sliced for a printer we don't support is refused: SliceException(UnsupportedPrinterText).
        public static SliceResult ReadSliceInfo(string path3mf, string defaultFilament)
        {
            var res = ReadSliceInfo(path3mf);
            if (res != null && res.UnsupportedPrinter) throw new SliceException(UnsupportedPrinterText(res.PrinterName));
            if (res != null) ApplyDefaultFilament(res.Filaments, defaultFilament);
            return res;
        }

        // ---------- a colour per file (idea 21) ----------

        /// Most colours for "together" (one plate): the AMS lite has 4 slots.
        public const int MaxTogetherColours = 4;

        /// Writes one flattened filament profile per colour choice into workDir (colour<N>.json, N = slot; "from" kept,
        /// filament_colour = the chosen colour when set) and returns their paths (index i = slot i+1). An empty
        /// Filament = defaultFilament. Call after PrepareProfiles. Profiles missing -> SliceException.
        public static List<string> PrepareColours(string exe, string printer, string nozzleSize, IList<ColourChoice> colours,
                                                  string defaultFilament, string workDir)
        {
            var paths = new List<string>();
            if (colours == null || colours.Count == 0) return paths;
            var roots = Roots(exe);
            if (roots.Count == 0) throw new SliceException(L.S("Could not find Bambu Studio's printer profiles.", "Kunne ikke finde Bambu Studios printerprofiler."));
            printer = FindPrinter(printer).Key;
            Directory.CreateDirectory(workDir);
            var json = Serializer();
            for (int i = 0; i < colours.Count; i++)
            {
                var c = colours[i] ?? new ColourChoice();
                string shortName = string.IsNullOrEmpty(c.Filament) ? defaultFilament : c.Filament;
                var f = new Dictionary<string, object>(Flatten(roots, "filament", Resolve(roots, "filament", printer, nozzleSize, shortName), 0));
                string colour = NormColour(c.Colour);
                if (colour.Length > 0) f["filament_colour"] = new[] { colour };
                string path = Path.Combine(workDir, "colour" + (i + 1) + ".json");
                File.WriteAllText(path, json.Serialize(f));
                paths.Add(path);
            }
            return paths;
        }

        /// The extra CLI options for slicing plain files together on one plate: --load-filament-ids (slots in file order,
        /// 1-based), --allow-multicolor-oneplate, and --allow-mix-temp when the colours' filament types differ (types =
        /// per colour, "" ignored). Pure (tests).
        internal static List<string> TogetherArgs(IList<int> slots, IList<string> types)
        {
            var args = new List<string> { "--load-filament-ids", string.Join(",", slots.Select(s => s.ToString(CultureInfo.InvariantCulture))),
                                          "--allow-multicolor-oneplate" };
            if (types != null && types.Where(t => !string.IsNullOrEmpty(t)).Select(t => t.Trim().ToUpperInvariant()).Distinct().Count() > 1)
                args.Add("--allow-mix-temp");
            return args;
        }

        /// The one call for several files with a colour each (idea 21). models = (display name, path); slots[i] = the
        /// 1-based colour (index into `colours`) of a plain model file (stl/obj/step/stp/amf). A .3mf project or G-code
        /// among them is handled as today (sliced with its own colours, or read when already sliced; slots[i] ignored)
        /// and its colours go on the slots after the user's (slot offset colours.Count), so they never merge with them.
        /// together (and 2+ plain files, 2+ different colours, colours.Count <= MaxTogetherColours): the plain files are
        /// sliced in one run on one plate (Together = true); if that fails (anything but cancel) TogetherProblem gets the
        /// reason and they're sliced separately. Separately: one run per file in its colour, summed per colour.
        /// Call PrepareProfiles(…, workDir) first. Plates: PlateList has every plate, with File = the display name
        /// (the together run's plate has File ""). StudioFile = the together slice, else the first file's (still in
        /// workDir: copy it out with KeepForStudio).
        public static SliceResult SliceColours(string exe, IList<KeyValuePair<string, string>> models, IList<int> slots,
                                               IList<ColourChoice> colours, bool together, string workDir, string printer,
                                               string nozzleSize, string defaultFilament, IProgress<string> progress, CancellationToken ct)
        {
            if (models == null || models.Count == 0) throw new SliceException(L.S("No model file to slice.", "Ingen modelfil at slice."));
            colours = colours ?? new List<ColourChoice>();
            printer = string.IsNullOrEmpty(printer) ? WorkDirPrinter(workDir) : FindPrinter(printer).Key;
            var colourFiles = PrepareColours(exe, printer, nozzleSize, colours, defaultFilament, workDir);
            var total = new SliceResult();
            var plain = new List<int>();     // indexes of plain model files
            for (int i = 0; i < models.Count; i++)
                if (!IsProjectOrSliced(models[i].Value)) plain.Add(i);
            Func<int, int> slotOf = i =>
            {
                int s = slots != null && i < slots.Count ? slots[i] : 1;
                return s >= 1 && s <= colourFiles.Count ? s : 1;
            };
            if (colourFiles.Count == 0 && plain.Count > 0)
                throw new SliceException(L.S("Pick a colour for each model file.", "Vælg en farve til hver modelfil."));

            var separate = new List<int>(plain);
            if (together && plain.Count >= 2 && plain.Select(slotOf).Distinct().Count() >= 2)
            {
                if (colourFiles.Count > MaxTogetherColours)
                    total.TogetherProblem = L.F("More than {0} colours can't share one plate on the AMS lite, so the files were sliced separately.",
                                                "Mere end {0} farver kan ikke dele én plade med AMS lite, så filerne blev slicet hver for sig.", MaxTogetherColours);
                else
                {
                    try
                    {
                        if (progress != null) progress.Report(L.F("Slicing {0} files together in Bambu Studio…", "Slicer {0} filer samlet i Bambu Studio…", plain.Count));
                        var types = new List<string>();
                        foreach (var p in colourFiles) types.Add(FilamentTypeOf(p));
                        string outDir = NewOutDir(workDir);
                        string load = string.Join(";", colourFiles);
                        var r = RunSlice(exe, plain.Select(i => models[i].Value).ToList(), workDir, printer, load, load,
                                         TogetherArgs(plain.Select(slotOf).ToList(), types), outDir,
                                         string.Join(", ", plain.Select(i => models[i].Key)), ct);
                        // too big for one plate: the CLI makes 2+ plates and puts plate 2 on filament 1 (wrong colours),
                        // so that counts as a failure
                        if (r.Plates > 1 || r.PlateList.Count > 1) throw new TogetherDoesNotFit();
                        AddResult(total, r, "");
                        total.Together = true;
                        separate.Clear();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (TogetherDoesNotFit)
                    {
                        ct.ThrowIfCancellationRequested();
                        total = new SliceResult();
                        total.TogetherProblem = L.S("The files don't fit on one plate together, so they were sliced separately.",
                                                    "Filerne kan ikke være på én plade sammen, så de blev slicet hver for sig.");
                    }
                    catch (Exception ex)
                    {
                        ct.ThrowIfCancellationRequested();
                        total = new SliceResult();
                        total.TogetherProblem = L.F("Slicing the files together on one plate didn't work ({0}), so they were sliced separately.",
                                                    "Det lykkedes ikke at slice filerne samlet på én plade ({0}), så de blev slicet hver for sig.",
                                                    ex.Message.TrimEnd('.'));
                    }
                }
            }

            for (int i = 0; i < models.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                bool isPlain = plain.Contains(i);
                if (isPlain && !separate.Contains(i)) continue;
                string name = models[i].Key, path = models[i].Value;
                SliceResult r;
                int offset;
                if (isPlain)
                {
                    int slot = slotOf(i);
                    Report(progress, name, i, models.Count);
                    string cf = colourFiles[slot - 1];
                    r = RunSlice(exe, new[] { path }, workDir, printer, cf, cf, null, NewOutDir(workDir), Path.GetFileName(path), ct);
                    offset = slot - 1;   // the run's slot 1 = this colour
                }
                else
                {
                    if (IsSlicedProject(path)) { r = ReadSliceInfo(path, defaultFilament); r.FromSlicedFile = true; }
                    else { Report(progress, name, i, models.Count); r = SliceFile(exe, path, workDir, printer, nozzleSize, defaultFilament, ct); }
                    offset = colourFiles.Count;
                }
                AddResult(total, r, name, offset);
            }
            return total;
        }

        /// A together run that came back with 2+ plates (SliceColours falls back to separate).
        sealed class TogetherDoesNotFit : Exception { }

        static void Report(IProgress<string> progress, string name, int i, int count)
        {
            if (progress == null) return;
            progress.Report(L.S("Slicing ", "Slicer ") + name + (count > 1 ? " (" + (i + 1) + "/" + count + ")" : "") +
                            L.S(" in Bambu Studio…", " i Bambu Studio…"));
        }

        static string NewOutDir(string workDir)
        {
            string outDir = Path.Combine(workDir, "out_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(outDir);
            return outDir;
        }

        /// A .3mf (project or sliced) or a G-code: sliced/read with its own colours, not a user colour.
        static bool IsProjectOrSliced(string path)
        {
            return IsGcodeFile(path) || (path ?? "").EndsWith(".3mf", StringComparison.OrdinalIgnoreCase);
        }

        /// filament_type of a written filament profile ("" when unreadable).
        static string FilamentTypeOf(string profilePath)
        {
            try
            {
                var d = Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(profilePath));
                object t;
                if (d == null || !d.TryGetValue("filament_type", out t) || t == null) return "";
                var s = t as string;
                if (s != null) return s.Trim();
                var list = t as System.Collections.IEnumerable;
                if (list != null) foreach (var o in list) return (o as string ?? "").Trim();
                return "";
            }
            catch { return ""; }
        }

        /// Per-filament model/total grams and model/total time from the CLI's result.json, summed over plates.
        /// Null on missing or bad input.
        public static SliceStats ParseSliceResult(string resultJson)
        {
            if (string.IsNullOrEmpty(resultJson)) return null;
            try
            {
                var d = Serializer().Deserialize<Dictionary<string, object>>(resultJson);
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

        // ---- open in Bambu Studio (idea 13) ----
        // Our out.3mf (model + our A1 profiles + plate gcode) opens in Studio's Prepare tab with our printer, process, plate
        // and filament (system names, not "modified"); Studio needs one "Slice plate" click before printing. A model-less
        // .gcode.3mf opens ready to print (Preview). Studio's "single_instance" setting (off by default) decides between a
        // new window and the open one.

        /// Folder for files kept for "Open in Bambu Studio": %TEMP%\PrintPrice\studio.
        public static string StudioRoot
        {
            get { return Path.Combine(Path.GetTempPath(), "PrintPrice", "studio"); }
        }

        /// Copies a sliced .3mf out of the (soon deleted) work folder into StudioRoot\<guid>\<model name>.3mf and deletes the
        /// older kept copies. Returns the copy's path, or null on any failure (never throws).
        public static string KeepForStudio(string sliced3mf, string modelName)
        {
            try
            {
                if (string.IsNullOrEmpty(sliced3mf) || !File.Exists(sliced3mf)) return null;
                string stem = Path.GetFileNameWithoutExtension(modelName ?? "");
                if (stem.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(0, stem.Length - 6);
                stem = Printables.SafeFileName(stem).Trim();
                if (stem.Length == 0) stem = "model";
                if (stem.Length > 80) stem = stem.Substring(0, 80);
                string dir = Path.Combine(StudioRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, stem + ".3mf");
                File.Copy(sliced3mf, target);
                CleanStudioFiles(target);
                return target;
            }
            catch { return null; }
        }

        /// Deletes every folder in StudioRoot except the one holding `keep` (null = all). Never throws; a file Studio
        /// still has open just stays until next time.
        public static void CleanStudioFiles(string keep)
        {
            try
            {
                if (!Directory.Exists(StudioRoot)) return;
                foreach (var dir in StudioFoldersToDelete(Directory.GetDirectories(StudioRoot), keep))
                    try { Directory.Delete(dir, true); } catch { }
            }
            catch { }
        }

        /// Pure: the folders to delete = all except the kept file's own folder (compared without case and trailing '\').
        public static List<string> StudioFoldersToDelete(IEnumerable<string> folders, string keepFile)
        {
            string keepDir = string.IsNullOrEmpty(keepFile) ? null : TrimSlash(Path.GetDirectoryName(keepFile));
            var list = new List<string>();
            if (folders == null) return list;
            foreach (var f in folders)
            {
                if (string.IsNullOrEmpty(f)) continue;
                if (keepDir != null && string.Equals(TrimSlash(f), keepDir, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(f);
            }
            return list;
        }

        static string TrimSlash(string p)
        {
            return (p ?? "").TrimEnd('\\', '/');
        }

        /// Pure: Bambu Studio's command line for one file = the full path in quotes.
        public static string StudioArguments(string file)
        {
            return Quote(Path.GetFullPath(file));
        }

        /// Starts Bambu Studio with the file (no shell, doesn't wait). Throws SliceException with a user-facing text.
        public static void OpenInStudio(string exe, string file)
        {
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new SliceException(L.S("Bambu Studio was not found. Install it from bambulab.com, or set its location in Settings.",
                                             "Bambu Studio blev ikke fundet. Installér det fra bambulab.com, eller angiv stien i Indstillinger."));
            if (string.IsNullOrEmpty(file) || file.IndexOf('"') >= 0 || !IsModelFile(file) || !Path.IsPathRooted(file) || !File.Exists(file))
                throw new SliceException(L.S("The sliced file is gone. Slice again to open it in Bambu Studio.",
                                             "Den slicede fil er væk. Slice igen for at åbne den i Bambu Studio."));
            try
            {
                var psi = new ProcessStartInfo(exe, StudioArguments(file))
                {
                    UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)
                };
                using (Process.Start(psi)) { }
            }
            catch (Exception ex)
            {
                throw new SliceException(L.F("Bambu Studio could not be started: {0}", "Bambu Studio kunne ikke startes: {0}", ex.Message));
            }
        }
    }
}
