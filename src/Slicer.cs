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
        public double TowerSeconds;    // issue #8 step 3: feature_type_times "Prime tower", summed over plates
        public List<double[]> ObjectSizes = new List<double[]>();   // issue #8: per object {width X, depth Y, height Z} mm, as sliced (scaled)
        // plans\too-small: per object {plate id (0 = missing), bbox x, bbox y, width, depth, height} as placed (SlicedOffBed)
        public List<double[]> ObjectBoxes = new List<double[]>();
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

        /// The CLI's exit code for "doesn't fit the plate / a plate is empty"; RunSlice also uses it for SlicedOffBed, so
        /// SliceException.IsBedError covers both.
        public const int BedErrorCode = -50;

        /// The error text for a model that doesn't fit the printer's build volume (CLI exit code -50, or SlicedOffBed).
        /// Points to the app's own "Scale…" link (MainForm.ScaleLinkText).
        public static string BedErrorText(string model)
        {
            var p = FindPrinter(model);
            return L.F("The model doesn't fit on the {0}'s build plate ({1} × {2} × {3} mm), or a plate in the file is empty. " +
                       "Choose a bigger printer, or use Scale… to make it smaller.",
                       "Modellen passer ikke på byggepladen i {0} ({1} × {2} × {3} mm), eller en plade i filen er tom. " +
                       "Vælg en større printer, eller brug Skalér… til at gøre den mindre.",
                       p.MachineName, p.BedX, p.BedY, p.BedZ);
        }

        /// The user-facing text when the CLI fails: -50 = BedErrorText(model), -100 (IsFailedSlice) = FailedSliceText(file),
        /// else "could not slice <file>: <error>".
        public static string SliceErrorText(int exitCode, string fileName, string cliError, string model)
        {
            if (exitCode == BedErrorCode) return BedErrorText(model);
            if (IsFailedSlice(exitCode, cliError)) return FailedSliceText(fileName);
            return L.F("Bambu Studio could not slice {0}: {1}", "Bambu Studio kunne ikke slice {0}: {1}",
                fileName, string.IsNullOrEmpty(cliError) ? L.F("it stopped with error {0}", "den stoppede med fejl {0}", exitCode) : cliError);
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
            if (depth > 25) throw new SliceException(L.F("This Bambu Studio profile can't be read ({0}). Pick another in Settings.",
                "Denne Bambu Studio-profil kan ikke læses ({0}). Vælg en anden i Indstillinger.", name));
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
            PrepareProfiles(exe, model, nozzleSize, process, filament, bedType, workDir, true);
        }

        /// Issue #8 step 3: as above, with the prime tower on or off (process.json "enable_prime_tower"; the CLI has no option
        /// for it, and our process wins over a .3mf project's own setting). Bambu prints no tower for one colour either way.
        public static void PrepareProfiles(string exe, string model, string nozzleSize, string process, string filament, string bedType, string workDir,
                                           bool primeTower)
        {
            var roots = Roots(exe);
            if (roots.Count == 0) throw new SliceException(L.S("Could not find Bambu Studio's printer profiles.", "Kunne ikke finde Bambu Studios printerprofiler."));
            model = FindPrinter(model).Key;
            Directory.CreateDirectory(workDir);
            var json = Serializer();
            File.WriteAllText(Path.Combine(workDir, "machine.json"), json.Serialize(Flatten(roots, "machine", MachinePreset(model, nozzleSize), 0)));
            var proc = Flatten(roots, "process", Resolve(roots, "process", model, nozzleSize, process), 0);
            if (!string.IsNullOrEmpty(bedType)) proc["curr_bed_type"] = bedType;   // CLI default is Cool Plate
            proc["enable_prime_tower"] = primeTower ? "1" : "0";                    // string, as in Bambu's profiles
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
            return QuoteArg(a);
        }

        /// One command-line argument in quotes, by Windows' rules (CommandLineToArgvW): backslashes before a quote or
        /// before the closing quote are doubled, a quote becomes \". "C:\a b\" -> "\"C:\\a b\\\\\"". Pure (tests).
        internal static string QuoteArg(string a)
        {
            var sb = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in a ?? "")
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', slashes); sb.Append(c); }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2);
            return sb.Append('"').ToString();
        }

        // ---------- running Bambu Studio processes (killed when the app closes) ----------

        static readonly object RunningLock = new object();
        static readonly List<Process> Running = new List<Process>();
        static readonly List<int> StartedIdList = new List<int>();
        static volatile bool stopping;

        /// Process ids of the last (up to 20) Bambu Studio runs started by RunSlice (test hooks only).
        internal static List<int> StartedIds
        {
            get { lock (RunningLock) return new List<int>(StartedIdList); }
        }

        /// Kills every running Bambu Studio started by this app and waits for each (3 s at most); after this no new run
        /// starts (RunSlice throws OperationCanceledException). Call when the app closes. Never throws.
        public static void StopAll()
        {
            List<Process> list;
            lock (RunningLock)
            {
                stopping = true;
                list = new List<Process>(Running);
            }
            foreach (var p in list)
            {
                try { if (!p.HasExited) { p.Kill(); p.WaitForExit(3000); } } catch { }
            }
        }

        /// Slicing went over TimeoutMinutes (SliceColours doesn't retry the files one by one after this).
        sealed class SliceTimeout : SliceException
        {
            public SliceTimeout(string message) : base(message) { }
        }

        /// Slices one model file (all plates) with the profiles written by PrepareProfiles. A project .3mf with 2+ filament
        /// slots gets one filament profile per slot (SlotFilaments): slots naming nothing (IsUnstatedFilament) get
        /// `defaultFilament` (Settings' filament), named ones keep theirs; nozzleSize / defaultFilament null = one filament
        /// (slot 1). printer = "A1", "A1M", "P1S"; null = the one PrepareProfiles used for workDir.
        /// A model too big for the printer (CLI -50) throws BedErrorText.
        /// scalePct (issue #8): 100 / 0 = as the file is; else "--scale --ensure-on-bed" (ScaleArgs; a G-code throws ScaleGcodeText).
        public static SliceResult SliceFile(string exe, string model, string workDir, string printer, string nozzleSize, string defaultFilament, CancellationToken ct,
                                            double scalePct = 100)
        {
            return SliceFile(exe, model, workDir, printer, nozzleSize, defaultFilament, ct, ScaleXyz.Uniform(scalePct));
        }

        /// Step 2: X/Y/Z. Uniform = --scale as above; different axes = a scaled copy of the model in workDir
        /// (PrepareScaled: STL, OBJ, 3MF; STEP/AMF throw ScaleXyzFormatText) + "--ensure-on-bed". null = not scaled.
        public static SliceResult SliceFile(string exe, string model, string workDir, string printer, string nozzleSize, string defaultFilament, CancellationToken ct,
                                            ScaleXyz scale)
        {
            scale = scale ?? ScaleXyz.None;
            if (scale.IsScaled && IsGcodeFile(model)) throw new SliceException(ScaleGcodeText());
            var scaleArgs = ScaleArgs(scale);
            model = PrepareScaled(model, scale, workDir);
            printer = string.IsNullOrEmpty(printer) ? WorkDirPrinter(workDir) : FindPrinter(printer).Key;
            string outDir = Path.Combine(workDir, "out_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(outDir);
            string filaments = null;
            if (!string.IsNullOrEmpty(nozzleSize) && !string.IsNullOrEmpty(defaultFilament))
                filaments = SlotFilaments(exe, model, printer, nozzleSize, defaultFilament, outDir);
            return RunSlice(exe, new[] { model }, workDir, printer, filaments, filaments ?? Path.Combine(workDir, "filament.json"),
                            scaleArgs.Count > 0 ? scaleArgs : null, outDir, Path.GetFileName(model), ct);
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
            Process proc;
            lock (RunningLock)
            {
                // the app is closing (StopAll): don't start another Bambu Studio
                if (stopping) throw new OperationCanceledException();
                proc = Process.Start(psi);
                Running.Add(proc);
                try { StartedIdList.Add(proc.Id); if (StartedIdList.Count > 20) StartedIdList.RemoveAt(0); } catch { }
            }
            try
            {
                while (!proc.WaitForExit(250))
                {
                    if (ct.IsCancellationRequested || sw.Elapsed.TotalMinutes > TimeoutMinutes)
                    {
                        // wait for it, so the work folder's files are free to delete
                        try { proc.Kill(); proc.WaitForExit(5000); } catch { }
                        ct.ThrowIfCancellationRequested();
                        throw new SliceTimeout(L.F("Slicing took longer than {0} minutes and was stopped.",
                            "Slicing tog mere end {0} minutter og blev stoppet.", TimeoutMinutes));
                    }
                }
                exit = proc.ExitCode;
            }
            finally
            {
                lock (RunningLock) Running.Remove(proc);
                proc.Dispose();
            }
            if (stopping) throw new OperationCanceledException();   // killed by StopAll: not a slicing error

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
                throw new SliceException(SliceErrorText(exit, shownName, error, printer, bed, clash), exit, error);
            }

            var res = ReadSliceInfo(out3mf);
            if (res == null) throw new SliceException(L.F("Bambu Studio returned no print estimate for {0}.",
                "Bambu Studio gav intet printestimat for {0}.", shownName));
            var stats = ParseSliceResult(resultText);
            // our own slice only (pre-sliced files and G-code never come here): a model the arrange step left hanging off
            // the plate is refused like the CLI's own -50
            if (SlicedOffBed(printer, stats)) throw new SliceException(BedErrorText(printer), BedErrorCode, null);
            ApplySliceStats(res, stats);
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
                    string xml = EntryText(zip, "Metadata/slice_info.config");   // null = missing, empty or over 64 MB
                    if (xml == null) return false;
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
                // issue #8 step 3: the tower needs the whole file (one streamed pass; failure = unknown)
                try
                {
                    fs.Position = 0;
                    using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8, true, 65536, true))
                        ApplyTower(res, TowerFromGcode(Lines(sr)));
                }
                catch (Exception) { }
                return res;
            }
        }

        /// Only the thumbnail of a G-code file (optimizer v1.28: reopening an old record), without the time/grams parse and
        /// the whole-file tower pass of ReadGcodeFile: GcodeThumbnail of the first GcodeHeadBytes; not found there = the
        /// head + last GcodeTailBytes (as ReadGcodeFile reads them). Binary G-code, no thumbnail or any error = null.
        public static byte[] ReadGcodePicture(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long len = fs.Length;
                    byte[] head = ReadRange(fs, 0, (int)Math.Min(len, GcodeHeadBytes));
                    if (IsBinaryGcode(path, head)) return null;
                    string text = System.Text.Encoding.UTF8.GetString(head);
                    var pic = GcodeThumbnail(text);
                    if (pic != null || len <= head.Length) return pic;
                    long start = Math.Max(head.Length, len - GcodeTailBytes);
                    byte[] tail = ReadRange(fs, start, (int)(len - start));
                    return GcodeThumbnail(text + "\n" + System.Text.Encoding.UTF8.GetString(tail));
                }
            }
            catch (Exception) { return null; }
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
            GcodeTower tower;
            using (var zip = ZipFile.OpenRead(path3mf))
            {
                xml = EntryText(zip, "Metadata/slice_info.config");   // null = missing, empty or over 64 MB
                if (xml == null) return null;
                settings = ProjectSettings(zip);
                modelSettings = EntryText(zip, "Metadata/model_settings.config");
                tower = PlateTower(zip);
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
                    if (f.Slot >= 1 && f.Slot <= profiles.Count) f.Profile = StripSlotTag(profiles[f.Slot - 1]);   // our " #2" (SlotFilaments)
                ApplyTower(res, tower);   // issue #8 step 3; null (no plate G-code) = unknown
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

        /// Issue #8 step 3: the prime tower of every Metadata/plate_N.gcode in an open 3MF, streamed line by line (no size
        /// cap: the G-code is never held in memory). Null = no plate G-code, or it couldn't be read.
        static GcodeTower PlateTower(ZipArchive zip)
        {
            try
            {
                GcodeTower total = null;
                foreach (var e in zip.Entries)
                {
                    if (!e.FullName.StartsWith("Metadata/plate_", StringComparison.OrdinalIgnoreCase)
                        || !e.FullName.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var sr = new StreamReader(e.Open()))
                    {
                        var plate = TowerFromGcode(Lines(sr));
                        if (total == null) total = new GcodeTower();
                        AddTower(total, plate);
                    }
                }
                return total;
            }
            catch { return null; }
        }

        /// A reader's lines, one at a time.
        static IEnumerable<string> Lines(TextReader r)
        {
            string line;
            while ((line = r.ReadLine()) != null) yield return line;
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
                // no slot has a profile for the printer: every slot keeps the project's own filament (own files, so the
                // prime tower stays); the template for their key list = Settings' filament for this printer
                string templateName = plan.FirstOrDefault(p => p != null)
                                      ?? ResolveOrNull(roots, "filament", printer, nozzleSize, ShortName(defaultFilament));
                if (templateName == null) return null;
                var json = Serializer();
                var flat = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
                var parts = new List<string>();
                Dictionary<string, object> template = null;   // a loaded profile, for the slots that keep the file's filament
                for (int i = 0; i < plan.Count; i++)
                {
                    if (plan[i] == null)
                    {
                        // issue #8 step 3: an empty entry (= the file's own filament) isn't counted by the CLI, so with only
                        // one loaded file it would drop the prime tower. Load the file's own filament as a file of its own.
                        if (template == null) template = Flatten(roots, "filament", templateName, 0);
                        var own = OwnSlotProfile(template, parsed, profiles, i);
                        string colour0 = i < colours.Count ? NormColour(colours[i]) : "";
                        if (colour0.Length > 0) own["filament_colour"] = new[] { colour0 };
                        string ownPath = Path.Combine(outDir, "slot" + (i + 1) + ".json");
                        File.WriteAllText(ownPath, json.Serialize(own));
                        parts.Add(ownPath);
                        continue;
                    }
                    Dictionary<string, object> f;
                    if (!flat.TryGetValue(plan[i], out f)) { f = Flatten(roots, "filament", plan[i], 0); flat[plan[i]] = f; }
                    var slot = new Dictionary<string, object>(f);
                    string colour = i < colours.Count ? NormColour(colours[i]) : "";
                    if (colour.Length > 0) slot["filament_colour"] = new[] { colour };
                    // own name per slot, else the CLI drops the prime tower when all slots share one preset (SlotNameTag);
                    // "from" stays (a changed "from" fails with -17), ReadSliceInfo strips the tag again
                    object name;
                    if (slot.TryGetValue("name", out name) && name is string && ((string)name).Length > 0)
                        slot["name"] = TaggedName((string)name, i + 1);
                    string path = Path.Combine(outDir, "slot" + (i + 1) + ".json");
                    File.WriteAllText(path, json.Serialize(slot));
                    parts.Add(path);
                }
                return string.Join(";", parts);
            }
            catch (Exception) { return null; }
        }

        // keys of a filament profile that describe the profile itself, never copied from the project
        static readonly HashSet<string> OwnSlotSkip = new HashSet<string>(StringComparer.Ordinal) {
            "name", "from", "inherits", "type", "setting_id", "instantiation", "version",
            "compatible_printers", "compatible_printers_condition", "compatible_prints", "compatible_prints_condition" };

        /// Issue #8 step 3, pure: a filament profile for slot index i that keeps the project's own filament: a copy of
        /// `template` (a flattened Bambu profile, for the key list and "from"), where every key the project stores per slot
        /// (an array with one value per slot) takes the project's value for this slot; name = the project's name + our tag.
        /// Checked with the CLI: same model grams / density / colour as an empty entry, and the tower stays.
        internal static Dictionary<string, object> OwnSlotProfile(Dictionary<string, object> template, Dictionary<string, object> project,
                                                                  List<string> fileProfiles, int i)
        {
            var own = new Dictionary<string, object>(template ?? new Dictionary<string, object>());
            int slots = fileProfiles == null ? 0 : fileProfiles.Count;
            foreach (var key in own.Keys.ToList())
            {
                if (OwnSlotSkip.Contains(key)) continue;
                var vals = SlotStrings(project, key);
                if (slots >= 2 && vals.Count == slots) own[key] = new[] { vals[i] };
            }
            string name = fileProfiles != null && i >= 0 && i < slots ? (fileProfiles[i] ?? "").Trim() : "";
            if (name.Length == 0) { object t; name = own.TryGetValue("name", out t) ? (t as string ?? "") : ""; }
            own["name"] = TaggedName(name, i + 1);
            return own;
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
                                               string nozzleSize, string defaultFilament, IProgress<string> progress, CancellationToken ct,
                                               double scalePct = 100)
        {
            return SliceColours(exe, models, slots, colours, together, workDir, printer, nozzleSize, defaultFilament, progress, ct,
                                ScaleXyz.Uniform(scalePct));
        }

        /// Step 2: the same with X/Y/Z (one scale for every file of the job; null = not scaled). Different axes: every
        /// file must be STL / OBJ / 3MF (else ScaleXyzFormatText, before anything runs); plain files are sliced from their
        /// scaled copies (PrepareScaled), projects through SliceFile.
        public static SliceResult SliceColours(string exe, IList<KeyValuePair<string, string>> models, IList<int> slots,
                                               IList<ColourChoice> colours, bool together, string workDir, string printer,
                                               string nozzleSize, string defaultFilament, IProgress<string> progress, CancellationToken ct,
                                               ScaleXyz scale)
        {
            if (models == null || models.Count == 0) throw new SliceException(L.S("No model file to slice.", "Ingen modelfil at slice."));
            scale = scale ?? ScaleXyz.None;
            // issue #8: one scale for every file of the job (refused for a G-code, checked before anything runs)
            if (scale.IsScaled && models.Any(m => IsGcodeFile(m.Value))) throw new SliceException(ScaleGcodeText());
            if (scale.IsScaled && !scale.IsUniform)
                foreach (var m in models)
                    if (!CanScaleXyz(m.Value)) throw new SliceException(ScaleXyzFormatText(m.Key));
            var scaleArgs = ScaleArgs(scale);
            var scaledCopies = new Dictionary<int, string>();
            Func<int, string> plainPath = i =>
            {
                string p;
                if (!scaledCopies.TryGetValue(i, out p)) { p = PrepareScaled(models[i].Value, scale, workDir); scaledCopies[i] = p; }
                return p;
            };
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
                        var r = RunSlice(exe, plain.Select(plainPath).ToList(), workDir, printer, load, load,
                                         TogetherArgs(plain.Select(slotOf).ToList(), types).Concat(scaleArgs).ToList(), outDir,
                                         string.Join(", ", plain.Select(i => models[i].Key)), ct);
                        // too big for one plate: the CLI makes 2+ plates and puts plate 2 on filament 1 (wrong colours),
                        // so that counts as a failure
                        if (r.Plates > 1 || r.PlateList.Count > 1) throw new TogetherDoesNotFit();
                        AddResult(total, r, "");
                        total.Together = true;
                        separate.Clear();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (SliceTimeout) { throw; }   // 15 minutes already: don't start the separate runs too
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
                    r = RunSlice(exe, new[] { plainPath(i) }, workDir, printer, cf, cf, scaleArgs.Count > 0 ? scaleArgs : null, NewOutDir(workDir), Path.GetFileName(path), ct);
                    offset = slot - 1;   // the run's slot 1 = this colour
                }
                else
                {
                    if (ReadAsSliced(path, scale)) { r = ReadSliceInfo(path, defaultFilament); r.FromSlicedFile = true; }
                    else { Report(progress, name, i, models.Count); r = SliceFile(exe, path, workDir, printer, nozzleSize, defaultFilament, ct, scale); }
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
                    object fto;
                    var ft = p.TryGetValue("feature_type_times", out fto) ? fto as Dictionary<string, object> : null;
                    if (ft != null) st.TowerSeconds += Math.Max(0, JsonNum(ft, "Prime tower"));
                    object oo;
                    var objs = p.TryGetValue("objects", out oo) ? oo as System.Collections.IEnumerable : null;
                    if (objs != null)
                        foreach (var o in objs)
                        {
                            var ob = o as Dictionary<string, object>;
                            object bo;
                            var bb = ob != null && ob.TryGetValue("bbox", out bo) ? bo as Dictionary<string, object> : null;
                            if (bb == null) continue;
                            double w = JsonNum(bb, "width"), dp = JsonNum(bb, "depth"), h = JsonNum(bb, "height");
                            if (w > 0 && dp > 0 && h > 0)
                            {
                                st.ObjectSizes.Add(new[] { w, dp, h });
                                object xo, yo;
                                double bx = bb.TryGetValue("x", out xo) ? JsonNum(bb, "x") : double.NaN;
                                double by = bb.TryGetValue("y", out yo) ? JsonNum(bb, "y") : double.NaN;
                                st.ObjectBoxes.Add(new[] { p.ContainsKey("id") ? JsonNum(p, "id") : 0, bx, by, w, dp, h });
                            }
                        }
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
            if (res != null && stats != null) SetSize(res, stats.ObjectSizes);
            if (res != null && stats != null) res.TowerSeconds = stats.TowerSeconds;   // 0 = no tower
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

        // ---------- same-size scaling (issue #8 step 1) ----------
        // One scale per job, in percent (100 = as the file is). Bambu Studio's CLI scales with "--scale <factor>"; without
        // "--ensure-on-bed" it scales around the model's centre and a bigger model loses its bottom (the slice "works" with
        // wrong numbers) and a smaller one floats (slice fails), so the two always go together (plans\issue8\report.md).

        /// The scale window's limits (percent).
        public const double MinScalePct = 10, MaxScalePct = 500;

        /// True when pct asks for a real scale: > 0 and not 100 (0 / negative / NaN = not scaled, like an old record).
        public static bool IsScaled(double pct)
        {
            return !double.IsNaN(pct) && !double.IsInfinity(pct) && pct > 0 && Math.Abs(pct - 100) >= 0.005;
        }

        /// Pure: the extra CLI arguments for a scale: none when !IsScaled, else "--scale", the factor with an invariant
        /// dot (150 -> "1.5", 33,3 -> "0.333", never "1,5" on Danish Windows), "--ensure-on-bed". Outside
        /// MinScalePct..MaxScalePct throws SliceException (ScaleRangeText).
        public static List<string> ScaleArgs(double pct)
        {
            var list = new List<string>();
            if (!IsScaled(pct)) return list;
            if (pct < MinScalePct - 0.005 || pct > MaxScalePct + 0.005) throw new SliceException(ScaleRangeText());
            list.Add("--scale");
            list.Add((Math.Round(pct, 2) / 100.0).ToString("0.####", CultureInfo.InvariantCulture));
            list.Add("--ensure-on-bed");
            return list;
        }

        /// "The size must be between 10 % and 500 %." (both languages).
        public static string ScaleRangeText()
        {
            return L.F("The size must be between {0} % and {1} %.", "Størrelsen skal være mellem {0} % og {1} %.",
                       MinScalePct.ToString("0", Num.Dk), MaxScalePct.ToString("0", Num.Dk));
        }

        /// Pure: the scale box's text -> percent. Accepts "150", "150 %", "150%", "87,5", "87.5" (Num.TryParse rules);
        /// false when empty, not a number, or outside MinScalePct..MaxScalePct. Rounded to 1 decimal.
        public static bool TryParseScale(string text, out double pct)
        {
            pct = 0;
            string s = (text ?? "").Trim().TrimEnd('%').Trim();
            double v;
            if (!Num.TryParse(s, out v)) return false;
            v = Math.Round(v, 1);
            if (v < MinScalePct || v > MaxScalePct) return false;
            pct = v;
            return true;
        }

        /// A G-code can't be scaled (it is already toolpaths); models and .3mf files (also sliced ones) can.
        public static bool CanScale(string path)
        {
            return !string.IsNullOrEmpty(path) && !IsGcodeFile(path);
        }

        /// Read the file's own numbers instead of slicing: an already-sliced file and no scale. A scaled sliced .3mf is
        /// sliced again (it holds the model); a scaled G-code is not read either, so it goes on to SliceFile, which throws
        /// ScaleGcodeText (never its 100 % numbers under a scaled name). Use this where IsSlicedProject decided "read, don't slice".
        public static bool ReadAsSliced(string path, double scalePct)
        {
            return IsSlicedProject(path) && !IsScaled(scalePct);
        }

        /// "A G-code file can't be scaled …" (both languages).
        public static string ScaleGcodeText()
        {
            return L.S("A G-code file can't be scaled; it is already sliced. Scale the model file instead.",
                       "En G-code-fil kan ikke skaleres, den er allerede slicet. Skalér modelfilen i stedet.");
        }

        // ---------- X/Y/Z scaling (issue #8 step 2) ----------
        // Bambu Studio's CLI only scales uniformly. Different axes are written into a copy of the model in the work folder:
        // 3MF = each <build><item transform>'s 3x3 part times the scale per bed axis (translation kept, meshes untouched),
        // STL / OBJ = scaled vertices (normals n / s, normalised). Then "--ensure-on-bed" as for uniform scaling.

        /// Same as IsScaled(pct) for an X/Y/Z scale (null = not scaled).
        public static bool IsScaled(ScaleXyz s) { return s != null && s.IsScaled; }

        /// Pure: the CLI arguments for an X/Y/Z scale: none when not scaled; uniform = ScaleArgs(pct) ("--scale f
        /// --ensure-on-bed"); different axes = only "--ensure-on-bed" (the file is scaled by PrepareScaled). An axis outside
        /// MinScalePct..MaxScalePct throws SliceException (ScaleRangeText).
        public static List<string> ScaleArgs(ScaleXyz s)
        {
            if (!IsScaled(s)) return new List<string>();
            if (!s.InRange) throw new SliceException(ScaleRangeText());
            if (s.IsUniform) return ScaleArgs(s.Pct);
            return new List<string> { "--ensure-on-bed" };
        }

        /// As ReadAsSliced(path, pct) for an X/Y/Z scale.
        public static bool ReadAsSliced(string path, ScaleXyz s)
        {
            return IsSlicedProject(path) && !IsScaled(s);
        }

        /// Files that can get different X/Y/Z: .stl, .obj, .3mf (also sliced .gcode.3mf). STEP / AMF / G-code can't.
        public static bool CanScaleXyz(string path)
        {
            string p = (path ?? "").Trim().ToLowerInvariant();
            return p.EndsWith(".stl") || p.EndsWith(".obj") || p.EndsWith(".3mf");
        }

        /// "<name> can only be scaled evenly. To scale X, Y and Z separately, …" (both languages).
        public static string ScaleXyzFormatText(string name)
        {
            return L.F("{0} can only be scaled evenly. To scale X, Y and Z separately, use an STL, OBJ or 3MF file.",
                       "{0} kan kun skaleres ens i alle retninger. Brug en STL-, OBJ- eller 3MF-fil for at skalere X, Y og Z hver for sig.",
                       string.IsNullOrEmpty(name) ? L.S("This file", "Denne fil") : name);
        }

        /// The file to slice for this scale: the file itself when not scaled or uniform (--scale does it); else a scaled
        /// copy with the same file name in workDir\scaled_<id>\ (STL, OBJ, 3MF; else SliceException ScaleXyzFormatText).
        /// Read/write problems throw SliceException with the file's name.
        public static string PrepareScaled(string path, ScaleXyz s, string workDir)
        {
            if (!IsScaled(s) || s.IsUniform) return path;
            if (!s.InRange) throw new SliceException(ScaleRangeText());
            string name = Path.GetFileName(path);
            if (!CanScaleXyz(path)) throw new SliceException(ScaleXyzFormatText(name));
            string dir = Path.Combine(workDir, "scaled_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, name);
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".3mf") Scale3mfFile(path, target, s);
                else if (ext == ".obj")
                {
                    using (var r = new StreamReader(path, System.Text.Encoding.UTF8, true))
                    using (var w = new StreamWriter(target, false, new System.Text.UTF8Encoding(false)))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null) w.WriteLine(ScaleObjLine(line, s));
                    }
                }
                else
                {
                    using (var inp = File.OpenRead(path))
                    using (var outp = File.Create(target))
                        ScaleStl(inp, outp, s);
                }
            }
            catch (SliceException) { throw; }
            catch (Exception ex)
            {
                throw new SliceException(L.F("Could not make the scaled copy of {0}: {1}", "Kunne ikke lave den skalerede kopi af {0}: {1}",
                                             name, ex.Message));
            }
            return target;
        }

        /// Pure: a normal for a model scaled by (fx, fy, fz) = (nx/fx, ny/fy, nz/fz) normalised; a zero normal stays zero.
        internal static double[] ScaleNormal(double nx, double ny, double nz, double fx, double fy, double fz)
        {
            double x = nx / fx, y = ny / fy, z = nz / fz;
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (!(len > 1e-12)) return new[] { 0.0, 0.0, 0.0 };
            return new[] { x / len, y / len, z / len };
        }

        /// Pure: true when the stream is a binary STL (length = 84 + 50 × the triangle count in bytes 80-83); else ASCII.
        internal static bool IsBinaryStl(long length, byte[] first84)
        {
            if (first84 == null || first84.Length < 84 || length < 84) return false;
            long count = BitConverter.ToUInt32(first84, 80);
            return 84 + 50 * count == length;
        }

        /// Pure (streams): writes `input` (a binary or ASCII STL, must be seekable) scaled by s to `output`. Binary: the
        /// 80-byte header, count and each triangle's 2 attribute bytes are kept; normals = ScaleNormal. ASCII: ScaleStlLine.
        public static void ScaleStl(Stream input, Stream output, ScaleXyz s)
        {
            double fx = s.X / 100.0, fy = s.Y / 100.0, fz = s.Z / 100.0;
            var head = new byte[84];
            int got = 0;
            while (got < 84) { int n = input.Read(head, got, 84 - got); if (n <= 0) break; got += n; }
            bool binary = got == 84 && IsBinaryStl(input.Length, head);
            long count = got == 84 ? BitConverter.ToUInt32(head, 80) : 0;
            if (!binary && got == 84 && !StartsWithSolid(head))
            {
                // not "solid…" text and the count doesn't match the length: a binary STL whose count field is wrong (some
                // exporters write 0) when the rest is whole triangles, else a broken file
                if ((input.Length - 84) % 50 != 0) throw new InvalidDataException("not a complete STL file");
                count = (input.Length - 84) / 50;
                binary = true;
            }
            if (!binary)
            {
                input.Seek(0, SeekOrigin.Begin);
                var r = new StreamReader(input, new System.Text.UTF8Encoding(false), false, 65536, true);
                var w = new StreamWriter(output, new System.Text.UTF8Encoding(false), 65536, true);
                string line;
                while ((line = r.ReadLine()) != null) w.WriteLine(ScaleStlLine(line, s));
                w.Flush();
                return;
            }
            output.Write(head, 0, 80);
            output.Write(BitConverter.GetBytes((uint)count), 0, 4);
            var tri = new byte[50];
            for (long t = 0; t < count; t++)
            {
                int k = 0;
                while (k < 50) { int n = input.Read(tri, k, 50 - k); if (n <= 0) throw new InvalidDataException("STL ends early"); k += n; }
                var nn = ScaleNormal(BitConverter.ToSingle(tri, 0), BitConverter.ToSingle(tri, 4), BitConverter.ToSingle(tri, 8), fx, fy, fz);
                PutFloat(tri, 0, nn[0]); PutFloat(tri, 4, nn[1]); PutFloat(tri, 8, nn[2]);
                for (int v = 0; v < 3; v++)
                {
                    int o = 12 + v * 12;
                    PutFloat(tri, o, BitConverter.ToSingle(tri, o) * fx);
                    PutFloat(tri, o + 4, BitConverter.ToSingle(tri, o + 4) * fy);
                    PutFloat(tri, o + 8, BitConverter.ToSingle(tri, o + 8) * fz);
                }
                output.Write(tri, 0, 50);
            }
        }

        /// The first non-space bytes are "solid" (an ASCII STL).
        static bool StartsWithSolid(byte[] b)
        {
            int i = 0;
            while (i < b.Length && (b[i] == ' ' || b[i] == '\t' || b[i] == '\r' || b[i] == '\n' || b[i] == 0xEF || b[i] == 0xBB || b[i] == 0xBF)) i++;
            return i + 5 <= b.Length && System.Text.Encoding.ASCII.GetString(b, i, 5).Equals("solid", StringComparison.OrdinalIgnoreCase);
        }

        static void PutFloat(byte[] b, int at, double v)
        {
            var x = BitConverter.GetBytes((float)v);
            Buffer.BlockCopy(x, 0, b, at, 4);
        }

        static readonly Regex StlVertexRx = new Regex(@"^(\s*)(vertex|facet\s+normal)(\s+)(\S+)(\s+)(\S+)(\s+)(\S+)(.*)$", RegexOptions.IgnoreCase);
        static readonly Regex ObjVertexRx = new Regex(@"^(\s*)(v|vn)(\s+)(\S+)(\s+)(\S+)(\s+)(\S+)(.*)$");

        /// Pure: one ASCII STL line scaled: "vertex x y z" × (X, Y, Z) %, "facet normal …" = ScaleNormal; other lines (and
        /// lines whose numbers don't parse) unchanged. Spacing kept; numbers written with an invariant dot ("R").
        public static string ScaleStlLine(string line, ScaleXyz s)
        {
            var m = StlVertexRx.Match(line ?? "");
            if (!m.Success) return line;
            bool normal = !m.Groups[2].Value.Equals("vertex", StringComparison.OrdinalIgnoreCase);
            return ScaleTriple(m, normal, s) ?? line;
        }

        /// Pure: one OBJ line scaled: "v x y z [w / colours…]" × (X, Y, Z) %, "vn x y z" = ScaleNormal; everything else
        /// ("vt", "f", "o", comments, …) unchanged.
        public static string ScaleObjLine(string line, ScaleXyz s)
        {
            var m = ObjVertexRx.Match(line ?? "");
            if (!m.Success) return line;
            return ScaleTriple(m, m.Groups[2].Value == "vn", s) ?? line;
        }

        static string ScaleTriple(Match m, bool normal, ScaleXyz s)
        {
            double x, y, z;
            var inv = CultureInfo.InvariantCulture;
            if (!double.TryParse(m.Groups[4].Value, NumberStyles.Float, inv, out x) ||
                !double.TryParse(m.Groups[6].Value, NumberStyles.Float, inv, out y) ||
                !double.TryParse(m.Groups[8].Value, NumberStyles.Float, inv, out z)) return null;
            double fx = s.X / 100.0, fy = s.Y / 100.0, fz = s.Z / 100.0;
            double[] v = normal ? ScaleNormal(x, y, z, fx, fy, fz) : new[] { x * fx, y * fy, z * fz };
            return m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + v[0].ToString("R", inv) + m.Groups[5].Value +
                   v[1].ToString("R", inv) + m.Groups[7].Value + v[2].ToString("R", inv) + m.Groups[9].Value;
        }

        /// Pure: a 3MF transform ("m00 m01 m02 m10 m11 m12 m20 m21 m22 tx ty tz", row vectors: p' = p·M + t) scaled along
        /// the bed's X/Y/Z around the object's position: column j of the 3x3 part × the scale of axis j, translation kept.
        /// null / empty = the identity. Not 12 numbers = null.
        public static string ScaleTransform(string transform, ScaleXyz s)
        {
            var inv = CultureInfo.InvariantCulture;
            string t = (transform ?? "").Trim();
            if (t.Length == 0) t = "1 0 0 0 1 0 0 0 1 0 0 0";
            var parts = t.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 12) return null;
            var m = new double[12];
            for (int i = 0; i < 12; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, inv, out m[i])) return null;
            var f = new[] { s.X / 100.0, s.Y / 100.0, s.Z / 100.0 };
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    m[row * 3 + col] *= f[col];
            return string.Join(" ", m.Select(v => v.ToString("0.#########", inv)));
        }

        static readonly Regex BuildRx = new Regex(@"<(?:\w+:)?build\b[^>]*?(?:/>|>.*?</(?:\w+:)?build>)", RegexOptions.Singleline);
        static readonly Regex ItemRx = new Regex(@"<(?:\w+:)?item\b[^>]*?/?>", RegexOptions.Singleline);
        static readonly Regex TransformAttrRx = new Regex(@"\btransform\s*=\s*(""([^""]*)""|'([^']*)')");

        /// Pure: the 3MF root model's XML with every <build><item> scaled (ScaleTransform; an item without a transform
        /// gets one). Only the <build> part is touched. Throws SliceException when there is no build item or a
        /// transform can't be read.
        public static string ScaleBuildItems(string modelXml, ScaleXyz s)
        {
            var b = BuildRx.Match(modelXml ?? "");
            if (!b.Success) throw new SliceException(L.S("The 3MF file has no objects to scale.", "3MF-filen har ingen objekter at skalere."));
            int items = 0;
            bool bad = false;
            string build = ItemRx.Replace(b.Value, im =>
            {
                items++;
                string tag = im.Value;
                var tm = TransformAttrRx.Match(tag);
                string old = tm.Success ? (tm.Groups[2].Success && tm.Groups[2].Value.Length > 0 ? tm.Groups[2].Value : tm.Groups[3].Value) : null;
                string scaled = ScaleTransform(old, s);
                if (scaled == null) { bad = true; return tag; }
                if (tm.Success) return tag.Substring(0, tm.Index) + "transform=\"" + scaled + "\"" + tag.Substring(tm.Index + tm.Length);
                int end = tag.EndsWith("/>") ? tag.Length - 2 : tag.Length - 1;
                return tag.Substring(0, end).TrimEnd() + " transform=\"" + scaled + "\"" + tag.Substring(end);
            });
            if (items == 0 || bad) throw new SliceException(L.S("The 3MF file's objects could not be scaled.", "3MF-filens objekter kunne ikke skaleres."));
            return modelXml.Substring(0, b.Index) + build + modelXml.Substring(b.Index + b.Length);
        }

        const long MaxRootModelBytes = 256L * 1048576;   // a root model holding the whole mesh (some slicers) up to 256 MB

        /// Pure: the root model part of a 3MF from _rels/.rels (the Target of the 3dmodel relationship), "3D/3dmodel.model"
        /// when not found. Without the leading "/".
        internal static string RootModelPath(string relsXml)
        {
            var m = Regex.Match(relsXml ?? "", @"<Relationship\b[^>]*Type\s*=\s*""[^""]*3dmodel""[^>]*>", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var t = Regex.Match(m.Value, @"Target\s*=\s*""([^""]+)""");
                if (t.Success) return t.Groups[1].Value.TrimStart('/');
            }
            return "3D/3dmodel.model";
        }

        /// Copies a 3MF to target with its root model's build items scaled (ScaleBuildItems); every other part is copied
        /// as it is (meshes, colours, plates, settings).
        static void Scale3mfFile(string path, string target, ScaleXyz s)
        {
            using (var src = ZipFile.OpenRead(path))
            {
                var relsEntry = src.GetEntry("_rels/.rels");
                string rels = null;
                if (relsEntry != null && relsEntry.Length < 1048576)
                    using (var r = new StreamReader(relsEntry.Open())) rels = r.ReadToEnd();
                string rootName = RootModelPath(rels);
                var root = src.Entries.FirstOrDefault(e => string.Equals(e.FullName, rootName, StringComparison.OrdinalIgnoreCase));
                if (root == null) throw new SliceException(L.S("The 3MF file has no model part.", "3MF-filen har ingen modeldel."));
                if (root.Length > MaxRootModelBytes)
                    throw new SliceException(L.S("The 3MF file is too big to scale X, Y and Z separately. Use the same % for all three.",
                                                 "3MF-filen er for stor til at skalere X, Y og Z hver for sig. Brug samme % for alle tre."));
                string xml;
                using (var r = new StreamReader(root.Open(), System.Text.Encoding.UTF8, true)) xml = r.ReadToEnd();
                string scaled = ScaleBuildItems(xml, s);
                using (var dst = ZipFile.Open(target, ZipArchiveMode.Create))
                {
                    foreach (var e in src.Entries)
                    {
                        var ne = dst.CreateEntry(e.FullName, CompressionLevel.Fastest);
                        using (var o = ne.Open())
                        {
                            if (e == root)
                            {
                                var bytes = new System.Text.UTF8Encoding(false).GetBytes(scaled);
                                o.Write(bytes, 0, bytes.Length);
                            }
                            else using (var i = e.Open()) i.CopyTo(o);
                        }
                    }
                }
            }
        }

        /// Pure: the object sizes from result.json -> res.SizeX/Y/Z (the biggest by box volume) and SizeObjects.
        /// None = left as they are.
        internal static void SetSize(SliceResult res, IList<double[]> sizes)
        {
            if (res == null || sizes == null || sizes.Count == 0) return;
            double best = -1;
            foreach (var s in sizes)
            {
                if (s == null || s.Length < 3) continue;
                double v = s[0] * s[1] * s[2];
                if (v > best) { best = v; res.SizeX = s[0]; res.SizeY = s[1]; res.SizeZ = s[2]; }
            }
            res.SizeObjects = sizes.Count(s => s != null && s.Length >= 3);
        }

        /// Pure: a size measured at fromPct, at toPct (both > 0; else the size as it is). 48 mm at 100 % -> 72 at 150 %.
        public static double SizeAt(double mm, double fromPct, double toPct)
        {
            if (!(fromPct > 0) || !(toPct > 0)) return mm;
            return mm * toPct / fromPct;
        }

        /// Pure: "60,0 × 31,0 × 48,0 mm" (W × D × H, 1 decimal, Danish comma in both languages like the rest of the app).
        public static string SizeText(double x, double y, double z)
        {
            return x.ToString("0.0", Num.Dk) + " × " + y.ToString("0.0", Num.Dk) + " × " + z.ToString("0.0", Num.Dk) + " mm";
        }

        /// Pure: does an object of x × y × z mm fit the printer's build volume (X and Y may swap)? printer = "A1", "A1M",
        /// "P1S" (unknown = A1). Unknown size (any <= 0) = true.
        public static bool FitsBed(string printer, double x, double y, double z)
        {
            if (x <= 0 || y <= 0 || z <= 0) return true;
            var p = FindPrinter(printer);
            const double eps = 0.01;
            bool xy = (x <= p.BedX + eps && y <= p.BedY + eps) || (y <= p.BedX + eps && x <= p.BedY + eps);
            return xy && z <= p.BedZ + eps;
        }

        /// "Too big for the Bambu Lab A1 (max 256 × 256 × 256 mm)." (both languages).
        public static string TooBigText(string printer)
        {
            var p = FindPrinter(printer);
            return L.F("Too big for the {0} (max {1} × {2} × {3} mm).", "For stor til {0} (maks. {1} × {2} × {3} mm).",
                       p.MachineName, p.BedX, p.BedY, p.BedZ);
        }

        // ---------- off the plate after slicing (plans\too-small) ----------
        // A model too big for the bed is turned (e.g. 45°) by the CLI's arrange and still sliced with exit 0, hanging off the
        // plate (Benchy 500 % on the A1: bbox 321,75 × 321,75 mm from x -18,5). RunSlice refuses such a slice with BedErrorText.

        /// How far (mm) an object may lie past the plate edge before SlicedOffBed counts it (rounding in result.json).
        public const double OffBedSlackMm = 0.5;

        /// Pure: true when an object as sliced (bbox from result.json, mm) doesn't fit the printer (FitsBed) or, with a known
        /// position (minX / minY not NaN), lies outside 0..BedX / 0..BedY by more than OffBedSlackMm. printer = "A1",
        /// "A1M", "P1S" (unknown = A1). Unknown size (any <= 0) = false.
        public static bool SlicedOffBed(string printer, double minX, double minY, double sizeX, double sizeY, double sizeZ)
        {
            if (!(sizeX > 0) || !(sizeY > 0) || !(sizeZ > 0)) return false;
            if (!FitsBed(printer, sizeX, sizeY, sizeZ)) return true;
            var p = FindPrinter(printer);
            if (!double.IsNaN(minX) && !double.IsInfinity(minX) && (minX < -OffBedSlackMm || minX + sizeX > p.BedX + OffBedSlackMm)) return true;
            if (!double.IsNaN(minY) && !double.IsInfinity(minY) && (minY < -OffBedSlackMm || minY + sizeY > p.BedY + OffBedSlackMm)) return true;
            return false;
        }

        /// Pure: SlicedOffBed for every object in a result.json (stats.ObjectBoxes). The position is checked on plate 1 only:
        /// later plates' x / y include the plate's offset in Bambu Studio's plate grid (plate 2 at x 360 on the A1), so
        /// they get the size check alone. null stats = false.
        public static bool SlicedOffBed(string printer, SliceStats stats)
        {
            if (stats == null || stats.ObjectBoxes == null) return false;
            foreach (var b in stats.ObjectBoxes)
            {
                if (b == null || b.Length < 6) continue;
                bool plate1 = Math.Abs(b[0] - 1) < 0.001;
                if (SlicedOffBed(printer, plate1 ? b[1] : double.NaN, plate1 ? b[2] : double.NaN, b[3], b[4], b[5])) return true;
            }
            return false;
        }

        // ---------- too small to print (plans\too-small) ----------
        // Bambu Studio fails with -100 "Failed slicing the model" (warning "empty layer between A and B") when parts get
        // thinner than one line (~0,4 mm). The thinnest part decides, not the overall size (Benchy fails at 27 %, works at
        // 30 %; a 20 mm block slices at 5 %), so no size or % rule predicts it: only the failure on a scaled-down job counts.

        /// The CLI's exit code for "Failed slicing the model" (too thin parts, faulty mesh, or a floating object).
        public const int FailedSliceCode = -100;

        /// Pure: the CLI's "Failed slicing the model": exit -100, or an unknown exit (0) with error_string starting with
        /// "Failed slicing the model". Any other exit code = false.
        public static bool IsFailedSlice(int exitCode, string cliError)
        {
            if (exitCode == FailedSliceCode) return true;
            if (exitCode != 0) return false;
            return !string.IsNullOrEmpty(cliError) &&
                   cliError.TrimStart().StartsWith("Failed slicing the model", StringComparison.OrdinalIgnoreCase);
        }

        /// Pure: true when a failed slice (IsFailedSlice) happened on a job scaled down (any axis under 100 %). Not scaled /
        /// only scaled up / null scale = false (then the original itself has the problem: keep the normal error).
        public static bool IsTooSmallFailure(int exitCode, string cliError, ScaleXyz scale)
        {
            if (scale == null || !IsFailedSlice(exitCode, cliError)) return false;
            return scale.X < 100 - 0.005 || scale.Y < 100 - 0.005 || scale.Z < 100 - 0.005;
        }

        /// Same as above for a caught SliceException (null = false).
        public static bool IsTooSmallFailure(SliceException ex, ScaleXyz scale)
        {
            return ex != null && IsTooSmallFailure(ex.ExitCode, ex.CliError, scale);
        }

        /// The plain text for a -100 that isn't about our scale: "Bambu Studio could not slice <file>: parts of the model
        /// are too thin to print, or the model is damaged." (both languages).
        public static string FailedSliceText(string fileName)
        {
            return L.F("Bambu Studio could not slice {0}: parts of the model are too thin to print, or the model is damaged.",
                       "Bambu Studio kunne ikke slice {0}: dele af modellen er for tynde til at printe, eller modellen er beskadiget.",
                       fileName);
        }

        /// Pure: the result-line text for IsTooSmallFailure. x100/y100/z100 = the model's size at 100 % (mm; any <= 0 /
        /// NaN = unknown, then no size in the text). The size shown is at the job's scale (per axis).
        /// "Too small to print at 20 %: the model would be only 12,0 × 6,2 × 9,6 mm, and its thinnest parts vanish when
        /// sliced. Try a bigger size."
        public static string TooSmallText(ScaleXyz scale, double x100, double y100, double z100)
        {
            var s = scale ?? ScaleXyz.None;
            if (Known(x100) && Known(y100) && Known(z100))
                return L.F("Too small to print at {0}: the model would be only {1}, and its thinnest parts vanish when sliced. Try a bigger size.",
                           "For lille til at printe ved {0}: modellen ville kun være {1}, og de tyndeste dele forsvinder ved slicing. Prøv en større størrelse.",
                           s.Text, SizeText(x100 * s.X / 100, y100 * s.Y / 100, z100 * s.Z / 100));
            return L.F("Too small to print at {0}: the thinnest parts vanish when sliced. Try a bigger size.",
                       "For lille til at printe ved {0}: de tyndeste dele forsvinder ved slicing. Prøv en større størrelse.", s.Text);
        }

        static bool Known(double mm) { return !double.IsNaN(mm) && !double.IsInfinity(mm) && mm > 0; }

        /// The Scale window's soft note shows when the scaled model's smallest side is under this (mm).
        public const double SmallSizeMm = 10;

        /// Pure: the Scale window's soft note: true when the smallest side of x × y × z mm (as SizeText shows it, 1 decimal)
        /// is under SmallSizeMm. Unknown size (any <= 0 / NaN) = false. Never blocks anything.
        public static bool IsSmallSize(double x, double y, double z)
        {
            if (!Known(x) || !Known(y) || !Known(z)) return false;
            double min = Math.Min(x, Math.Min(y, z));
            return Math.Round(min, 1, MidpointRounding.AwayFromZero) < SmallSizeMm;
        }

        /// "Small prints can lose thin parts or fail to slice." (both languages).
        public static string SmallScaleNote()
        {
            return L.S("Small prints can lose thin parts or fail to slice.", "Små print kan miste tynde dele eller fejle ved slicing.");
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

        // ---------- old job folders (MainForm.RunJob's %TEMP%\PrintPrice\<guid>) ----------

        static readonly Regex JobFolderRx = new Regex("^[0-9a-fA-F]{32}$");

        /// Pure: of (folder path, newest of its creation / last-write time in UTC), the paths to delete: the folder's name
        /// is 32 hex digits (a job folder) and that time is at least minAge before nowUtc. "studio", other names, young
        /// and future-dated folders are kept.
        public static List<string> OldJobFolders(IEnumerable<KeyValuePair<string, DateTime>> folders, DateTime nowUtc, TimeSpan minAge)
        {
            var list = new List<string>();
            if (folders == null) return list;
            foreach (var f in folders)
            {
                if (string.IsNullOrEmpty(f.Key)) continue;
                if (!JobFolderRx.IsMatch(Path.GetFileName(TrimSlash(f.Key)) ?? "")) continue;
                if (f.Value > nowUtc || nowUtc - f.Value < minAge) continue;
                list.Add(f.Key);
            }
            return list;
        }

        /// Deletes job folders under root left by earlier runs (crash, Bambu Studio still holding a file) that are at
        /// least a day old, so another running copy of the app is never hit. Never throws; returns how many went.
        public static int CleanJobFolders(string root)
        {
            int n = 0;
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return 0;
                var folders = new List<KeyValuePair<string, DateTime>>();
                foreach (var dir in Directory.GetDirectories(root))
                {
                    try
                    {
                        var info = new DirectoryInfo(dir);
                        DateTime t = info.LastWriteTimeUtc > info.CreationTimeUtc ? info.LastWriteTimeUtc : info.CreationTimeUtc;
                        folders.Add(new KeyValuePair<string, DateTime>(dir, t));
                    }
                    catch { }
                }
                foreach (var dir in OldJobFolders(folders, DateTime.UtcNow, TimeSpan.FromDays(1)))
                    try { Directory.Delete(dir, true); n++; } catch { }
            }
            catch { }
            return n;
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
