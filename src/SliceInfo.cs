using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

// Text-only parsing of sliced Bambu files (slice_info.config, project_settings.config) and the printer table.
// No WinForms, Process, files or network here: the website (WEB defined) compiles this file as it is.
// The file and slicer parts of BambuSlicer are in Slicer.cs.
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

    public class SliceException : Exception
    {
        public SliceException(string message) : base(message) { }
    }

    /// Text-only part of BambuSlicer (printer table, slice_info / project_settings parsing). The rest is in Slicer.cs.
    public static partial class BambuSlicer
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

        // " 0.4 nozzle" at the end of a printer name (ModelKeyFromName, ParseProjectPrinter)
        static readonly Regex NozzleSuffixRx = new Regex(@"\s+\d+(\.\d+)?\s*nozzle$", RegexOptions.IgnoreCase);

        /// Our key for a Bambu printer name ("Bambu Lab A1 mini", also "Bambu Lab A1 mini 0.4 nozzle") or model id ("N1");
        /// "" = a printer we don't support, null = empty input.
        public static string ModelKeyFromName(string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) return null;
            n = NozzleSuffixRx.Replace(n, "").Trim();
            foreach (var p in Printers)
                if (string.Equals(p.MachineName, n, StringComparison.OrdinalIgnoreCase) || string.Equals(p.ModelId, n, StringComparison.OrdinalIgnoreCase))
                    return p.Key;
            return "";
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
            return ProjectPrinterFrom(ParseSettings(projectSettingsJson), sliceInfoXml);
        }

        /// ParseProjectPrinter with project_settings already parsed (ParseSettings; null = none or bad).
        internal static ProjectPrinter ProjectPrinterFrom(Dictionary<string, object> projectSettings, string sliceInfoXml)
        {
            string name = "", nozzle = "";
            var model = SlotStrings(projectSettings, "printer_model");
            if (model.Count > 0) name = (model[0] ?? "").Trim();
            if (name.Length == 0)
            {
                var id = SlotStrings(projectSettings, "printer_settings_id");
                if (id.Count > 0) name = NozzleSuffixRx.Replace((id[0] ?? "").Trim(), "").Trim();
            }
            var nz = SlotStrings(projectSettings, "nozzle_diameter");
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

        // ---------- project_settings.config ----------

        /// Filament profile names per slot from Metadata/project_settings.config (JSON, "filament_settings_id").
        /// Index 0 = slot 1. Empty list on missing or bad input, never null.
        public static List<string> ParseFilamentSlots(string projectSettingsJson)
        {
            return ParseSlotStrings(projectSettingsJson, "filament_settings_id");
        }

        /// One string per slot from a project_settings.config array (e.g. "filament_colour"). Empty list on missing or bad input.
        public static List<string> ParseSlotStrings(string projectSettingsJson, string key)
        {
            return SlotStrings(ParseSettings(projectSettingsJson), key);
        }

        /// project_settings.config parsed once for several SlotStrings / ParseProjectPrinter reads; null on missing or bad input.
        internal static Dictionary<string, object> ParseSettings(string projectSettingsJson)
        {
            if (string.IsNullOrEmpty(projectSettingsJson)) return null;
            try { return Json.Deserialize<Dictionary<string, object>>(projectSettingsJson); }
            catch { return null; }
        }

        /// ParseSlotStrings on parsed settings (null = empty list).
        internal static List<string> SlotStrings(Dictionary<string, object> d, string key)
        {
            var list = new List<string>();
            try
            {
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

        // ---------- picture and slice_info.config ----------

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
