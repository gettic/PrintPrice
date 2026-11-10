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
        // file to open in Bambu Studio: ReadSliceInfo sets the .3mf it read (our out.3mf in the work folder, or the user's
        // .gcode.3mf); after a slice, KeepForStudio moves it out of the work folder. null = nothing to open
        public string StudioFile;
        // printer the file was sliced for (ReadSliceInfo, from project_settings / slice_info): PrinterModel = our key ("A1",
        // "A1M", "P1S"), "" = a printer we don't support, null = the file doesn't say. PrinterName = the file's name for it
        // ("Bambu Lab X1 Carbon"), PrinterNozzle = "0.4" etc. ("" unknown). UnsupportedPrinter = PrinterModel == "".
        public string PrinterModel;
        public string PrinterName = "";
        public string PrinterNozzle = "";
        public bool UnsupportedPrinter;
    }

    /// One Bambu Lab printer model the app can slice for (BambuSlicer.Printers).
    public sealed class BambuPrinter
    {
        public string Key;              // "A1", "A1M", "P1S" (= AppSettings.PrinterModel)
        public string MachineName;      // Bambu's printer_model, e.g. "Bambu Lab A1 mini"
        public string ModelId;          // slice_info printer_model_id, e.g. "N1"
        public string Thumb;            // resources\images\printer_thumbnail_<Thumb>_png.png
        public string[] ProcessFamilies;    // "@BBL <family>" process names, best first
        public string[] FilamentFamilies;   // "@BBL <family>" filament names, best first
        public int BedX, BedY, BedZ;    // build volume in mm
    }

    /// The printer a (sliced) project file was made for (BambuSlicer.ParseProjectPrinter).
    public sealed class ProjectPrinter
    {
        public string Key = "";      // our key, "" = not one we support
        public string Name = "";     // the file's printer name, e.g. "Bambu Lab X1 Carbon"
        public string Nozzle = "";   // "0.4" etc., "" unknown
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

    /// Drives the Bambu Studio command line to slice models with Bambu Lab A1 / A1 mini / P1S profiles.
    public static class BambuSlicer
    {
        // ---------- printer models (several printers) ----------

        /// The supported printers. The P1S has no process profiles of its own: Bambu uses the X1C ones for it.
        public static readonly BambuPrinter[] Printers = {
            new BambuPrinter { Key = "A1", MachineName = "Bambu Lab A1", ModelId = "N2S", Thumb = "n2s",
                ProcessFamilies = new[] { "A1" }, FilamentFamilies = new[] { "A1" }, BedX = 256, BedY = 256, BedZ = 256 },
            new BambuPrinter { Key = "A1M", MachineName = "Bambu Lab A1 mini", ModelId = "N1", Thumb = "n1",
                ProcessFamilies = new[] { "A1M" }, FilamentFamilies = new[] { "A1M" }, BedX = 180, BedY = 180, BedZ = 180 },
            new BambuPrinter { Key = "P1S", MachineName = "Bambu Lab P1S", ModelId = "C12", Thumb = "p1s",
                ProcessFamilies = new[] { "X1C", "P1P" }, FilamentFamilies = new[] { "P1S", "X1C", "P1P" }, BedX = 256, BedY = 256, BedZ = 250 },
        };

        /// The printer with this key (ignores case); unknown / empty = the A1.
        public static BambuPrinter FindPrinter(string key)
        {
            foreach (var p in Printers) if (string.Equals(p.Key, (key ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return p;
            return Printers[0];
        }

        /// Our key for a Bambu printer name ("Bambu Lab A1 mini", also "Bambu Lab A1 mini 0.4 nozzle") or model id ("N1");
        /// "" = a printer we don't support, null = empty input.
        public static string ModelKeyFromName(string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) return null;
            n = Regex.Replace(n, @"\s+\d+(\.\d+)?\s*nozzle$", "", RegexOptions.IgnoreCase).Trim();
            foreach (var p in Printers)
                if (string.Equals(p.MachineName, n, StringComparison.OrdinalIgnoreCase) || string.Equals(p.ModelId, n, StringComparison.OrdinalIgnoreCase))
                    return p.Key;
            return "";
        }

        /// Bambu's machine preset, e.g. ("A1M", "0.4") -> "Bambu Lab A1 mini 0.4 nozzle". Unknown model = A1.
        public static string MachinePreset(string model, string nozzleSize)
        {
            return FindPrinter(model).MachineName + " " + nozzleSize + " nozzle";
        }

        public static string MachinePreset(string nozzleSize)
        {
            return MachinePreset("A1", nozzleSize);
        }

        /// Process/filament preset suffix for a nozzle on the A1: 0.4 is "@BBL A1", others "@BBL A1 0.6 nozzle".
        public static string Suffix(string nozzleSize)
        {
            return FamilySuffix("A1", nozzleSize);
        }

        static string FamilySuffix(string family, string nozzleSize)
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
                var proc = Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(workDir, "process.json")));
                bedType = FirstString(proc, "curr_bed_type") ?? "";
                string key = PlateTempKey(bedType);
                var files = (filamentFiles ?? Path.Combine(workDir, "filament.json")).Split(';');
                foreach (var f in files)
                {
                    if (string.IsNullOrEmpty(f) || !File.Exists(f)) continue;
                    var d = Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
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

        public const string IssuesUrl ="https://github.com/gettic/PrintPrice/issues";

        /// The text for a sliced file made for a printer we don't support (name "" = "another printer").
        public static string UnsupportedPrinterText(string printerName)
        {
            string n = (printerName ?? "").Trim();
            if (n.Length == 0) n = L.S("another printer", "en anden printer");
            return L.F("This file was sliced for the {0}, which PrintPrice doesn't support yet. You can ask for it to be added: " +
                       "open an issue on GitHub at {1}",
                       "Filen er slicet til {0}, som PrintPrice ikke understøtter endnu. Du kan bede om at få den tilføjet: " +
                       "opret et issue på GitHub på {1}", n, IssuesUrl);
        }

        /// Which printer a project was made for: project_settings.config ("printer_model", then "printer_settings_id",
        /// nozzle from "nozzle_diameter"), then slice_info.config ("printer_model_id", "nozzle_diameters"; empty in CLI
        /// output). Null when neither names a printer.
        public static ProjectPrinter ParseProjectPrinter(string projectSettingsJson, string sliceInfoXml)
        {
            string name = "", nozzle = "";
            var model = ParseSlotStrings(projectSettingsJson, "printer_model");
            if (model.Count > 0) name = (model[0] ?? "").Trim();
            if (name.Length == 0)
            {
                var id = ParseSlotStrings(projectSettingsJson, "printer_settings_id");
                if (id.Count > 0) name = Regex.Replace((id[0] ?? "").Trim(), @"\s+\d+(\.\d+)?\s*nozzle$", "", RegexOptions.IgnoreCase).Trim();
            }
            var nz = ParseSlotStrings(projectSettingsJson, "nozzle_diameter");
            if (nz.Count > 0) nozzle = (nz[0] ?? "").Trim();
            if ((name.Length == 0 || nozzle.Length == 0) && !string.IsNullOrEmpty(sliceInfoXml))
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(sliceInfoXml);
                    if (name.Length == 0)
                    {
                        var n = doc.SelectSingleNode("/config/plate/metadata[@key='printer_model_id']");
                        if (n != null) name = AttrText(n, "value");
                    }
                    if (nozzle.Length == 0)
                    {
                        var n = doc.SelectSingleNode("/config/plate/metadata[@key='nozzle_diameters']");
                        if (n != null) nozzle = AttrText(n, "value").Split(',')[0].Trim();
                    }
                }
                catch { }
            }
            if (name.Length == 0) return null;
            var res = new ProjectPrinter { Key = ModelKeyFromName(name) ?? "", Name = name, Nozzle = nozzle };
            if (res.Key.Length > 0 && string.Equals(name, FindPrinter(res.Key).ModelId, StringComparison.OrdinalIgnoreCase))
                res.Name = FindPrinter(res.Key).MachineName;   // a model id ("N1") -> the printer's name
            return res;
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

        public static List<string> ListProcesses(string exe, string nozzleSize)
        {
            return ListProcesses(exe, "A1", nozzleSize);
        }

        /// Filaments Bambu lists as compatible with the printer + nozzle (0.6/0.8 include the standard ones, 0.2 not).
        public static List<string> ListFilaments(string exe, string model, string nozzleSize)
        {
            return ListShort(exe, "filament", model, nozzleSize);
        }

        public static List<string> ListFilaments(string exe, string nozzleSize)
        {
            return ListFilaments(exe, "A1", nozzleSize);
        }

        /// A preset name without its printer part: "0.20mm Standard @BBL X1C" / "Bambu PLA Basic @BBL A1M 0.2 nozzle" -> the
        /// part before " @BBL".
        public static string ShortName(string preset)
        {
            return Regex.Replace(preset ?? "", @"\s*@BBL\b.*$", "");
        }

        /// Reads filament type and whether Bambu marks it abrasive (required nozzle hardness ≥ 40 HRC).
        public static void FilamentInfo(string exe, string shortName, string nozzleSize, out string type, out bool abrasive)
        {
            FilamentInfo(exe, "A1", shortName, nozzleSize, out type, out abrasive);
        }

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
            PrepareProfiles(exe, "A1", nozzleSize, process, filament, bedType, workDir);
        }

        /// As above for a printer model ("A1", "A1M", "P1S"; unknown = A1). Also writes printer.txt (the model key) so
        /// SliceFile knows the printer for per-slot filaments and the bed-size error.
        public static void PrepareProfiles(string exe, string model, string nozzleSize, string process, string filament, string bedType, string workDir)
        {
            var roots = Roots(exe);
            if (roots.Count == 0) throw new SliceException(L.S("Could not find Bambu Studio's printer profiles.", "Kunne ikke finde Bambu Studios printerprofiler."));
            model = FindPrinter(model).Key;
            Directory.CreateDirectory(workDir);
            var json = Json();
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
            return SliceFile(exe, model, workDir, null, nozzleSize, defaultFilament, ct);
        }

        /// As above for a printer model ("A1", "A1M", "P1S"); null = the one PrepareProfiles used for workDir.
        /// A model too big for the printer (CLI -50) throws BedErrorText.
        public static SliceResult SliceFile(string exe, string model, string workDir, string printer, string nozzleSize, string defaultFilament, CancellationToken ct)
        {
            printer = string.IsNullOrEmpty(printer) ? WorkDirPrinter(workDir) : FindPrinter(printer).Key;
            string outDir = Path.Combine(workDir, "out_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(outDir);
            string filaments = null;
            if (!string.IsNullOrEmpty(nozzleSize) && !string.IsNullOrEmpty(defaultFilament))
                filaments = SlotFilaments(exe, model, printer, nozzleSize, defaultFilament, outDir);
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
            {
                string bed = "";
                var clash = IsPlateError(exit, error) ? ClashingFilaments(workDir, filaments, out bed) : null;
                throw new SliceException(SliceErrorText(exit, Path.GetFileName(model), error, printer, bed, clash));
            }

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
                res.StudioFile = path3mf;
                var profiles = ParseFilamentSlots(settings);
                foreach (var f in res.Filaments)
                    if (f.Slot >= 1 && f.Slot <= profiles.Count) f.Profile = profiles[f.Slot - 1];
                var pp = ParseProjectPrinter(settings, xml);
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
                var profiles = ParseFilamentSlots(settings);
                if (profiles.Count < 2) return null;
                var colours = ParseSlotStrings(settings, "filament_colour");
                var roots = Roots(exe);
                var plan = SlotPlan(profiles, ShortName(defaultFilament), n => ResolveOrNull(roots, "filament", printer, nozzleSize, n));
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
        /// A file sliced for a printer we don't support is refused: SliceException(UnsupportedPrinterText).
        public static SliceResult ReadSliceInfo(string path3mf, string defaultFilament)
        {
            var res = ReadSliceInfo(path3mf);
            if (res != null && res.UnsupportedPrinter) throw new SliceException(UnsupportedPrinterText(res.PrinterName));
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
