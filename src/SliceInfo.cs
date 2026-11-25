using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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
        // the slicer a G-code came from (BambuSlicer.SlicerBambu / SlicerOrca / SlicerPrusa / SlicerCura or another name);
        // "" = Bambu Studio or unknown (.gcode.3mf, our own slice). See BambuSlicer.OtherSlicerNote.
        public string SlicerName = "";
        // grams worked out from the filament length with a guessed density (PLA 1,24 g/cm³), e.g. Cura: show as an estimate
        public bool GramsEstimated;
        // sliced for a printer that isn't a Bambu Lab one (G-code from another slicer; PrinterModel null, PrinterName set):
        // show BambuSlicer.OtherPrinterWarning with the warning stripe
        public bool OtherPrinter;
        // a G-code that gave only one of the two (Seconds or Grams = 0): show BambuSlicer.MissingValueText so the user types it
        public bool MissingTime;
        public bool MissingGrams;
        // idea 21: every plate (ParseSliceInfo, GcodeResult = 1; AddResult appends a file's plates with its name). Never null.
        // Show the list when it has 2+ entries (BambuSlicer.PlateTitle / PlateNumbers / PlateLine).
        public List<PlateInfo> PlateList = new List<PlateInfo>();
        // idea 21 stage B (BambuSlicer.SliceColours): true = all files were sliced together on one plate with several colours
        public bool Together;
        // idea 21: null = fine / not asked; else a short text why "together" fell back to separate plates
        public string TogetherProblem;
        // issue #8: size in mm of the biggest object (by its box volume) as sliced, i.e. already scaled; 0 = unknown (a
        // pre-sliced file / G-code has no result.json). SizeObjects = how many objects were measured (2+ = "the biggest one").
        public double SizeX, SizeY, SizeZ;
        public int SizeObjects;
        // issue #8 step 3: prime tower grams (already inside Grams and PurgeGrams; the flush alone = PurgeGrams - TowerGrams)
        // and its print time (inside Seconds; only from our own slicing's result.json). -1 = unknown, 0 = no tower.
        public double TowerGrams = -1;
        public double TowerSeconds = -1;
        // batch-plates (issue #9/#10): Copies = pieces in this result (0 = not a batch slice = 1 piece), PerPlate = most
        // copies on one plate (0 = unknown). CopiesMultiplied = a pre-sliced file / G-code (or a multi-plate project) that
        // can't be re-arranged: its numbers are the file's x Copies (BambuSlicer.RepeatResult), exact as printed.
        public int Copies;
        public int PerPlate;
        public bool CopiesMultiplied;
        // batch-2 (issue #10): "share the last plate" (SliceBatch with shareLastPlate). null = not asked or nothing to share
        // (fewer than 2 colours with a part-full last plate); see ShareInfo.
        public ShareInfo Share;
        // batch-2: plates BambuSlicer.FixSingleFilamentPlates corrected (the CLI's slot-1 report of a one-filament plate);
        // > 0 = those slots' ModelGrams are unknown (-1)
        public int SingleFilamentFixes;
    }

    /// Batch-2: what "share the last plate between colours" did (SliceResult.Share). Applied = the colours' part-full last
    /// plates were replaced by one shared plate; else Problem says why not (own plates kept, numbers as without sharing).
    /// The trade-off against own plates (only when Applied): PlatesSaved (>= 1), ExtraSeconds / ExtraGrams (shared plate
    /// minus the own last plates it replaced; normally > 0: colour changes + tower), ColourChanges on the shared plate.
    public sealed class ShareInfo
    {
        public bool Applied;
        public string Problem;        // null when Applied
        public int Colours;           // colours on the shared plate (Applied) or with a part-full last plate (not applied)
        public int PlatesSaved;
        public double ExtraSeconds;
        public double ExtraGrams;
        public int ColourChanges = -1;   // -1 unknown
    }

    /// Batch-2: one plate of a Bambu Studio assemble list (BambuSlicer.AssembleListJson).
    public sealed class AssemblePlate
    {
        public string Name = "";
        public List<AssembleObject> Objects = new List<AssembleObject>();
    }

    /// Batch-2: Count copies of the model file at Path (STL / OBJ only), all on filament Slot (1-based, in the run's
    /// --load-filaments order).
    public sealed class AssembleObject
    {
        public string Path = "";
        public int Count = 1;
        public int Slot = 1;
    }

    /// Batch-plates: one model file of a batch (BambuSlicer.SliceBatch). Slot = 1-based colour (index into the colours
    /// list; plain files only, ignored for .3mf / G-code). Copies 1..BambuSlicer.MaxCopies.
    public sealed class BatchItem
    {
        public string Name = "";   // display name
        public string Path = "";   // file on disk
        public int Copies = 1;
        public int Slot = 1;
    }

    /// Batch-plates: one copy's footprint on a plate: the box around every object (the wipe tower left out) and the
    /// tower's box (0 = no tower). From BambuSlicer.ParsePlateFootprint = FIRST LAYER only (see there) or the website's
    /// GcodeFootprint (whole height).
    public sealed class PlateFootprint
    {
        public double Width, Depth;             // mm, X / Y of all objects together
        public double TowerWidth, TowerDepth;   // mm, 0 = none
        public int Objects;                     // objects counted (tower left out)
    }

    /// Issue #8 step 2: one job's scale per axis, in percent (100 = as the file is). Uniform (X = Y = Z) slices with
    /// Bambu Studio's --scale; different axes are written into a scaled copy of the model (BambuSlicer.PrepareScaled:
    /// STL, OBJ, 3MF only). Pure; the website compiles it too.
    public sealed class ScaleXyz
    {
        /// The Scale window's limits per axis (percent); same as BambuSlicer.MinScalePct / MaxScalePct.
        public const double Min = 10, Max = 500;
        public readonly double X, Y, Z;

        /// Axes <= 0 / NaN / infinity count as 100 (like a record without a scale).
        public ScaleXyz(double x, double y, double z)
        {
            X = Clean(x); Y = Clean(y); Z = Clean(z);
        }

        static double Clean(double v) { return double.IsNaN(v) || double.IsInfinity(v) || v <= 0 ? 100 : v; }

        public static ScaleXyz Uniform(double pct) { return new ScaleXyz(pct, pct, pct); }
        public static readonly ScaleXyz None = new ScaleXyz(100, 100, 100);

        static bool Same(double a, double b) { return Math.Abs(a - b) < 0.005; }
        /// All three axes the same (then Pct is the step-1 single scale).
        public bool IsUniform { get { return Same(X, Y) && Same(Y, Z); } }
        /// Any axis not 100.
        public bool IsScaled { get { return !Same(X, 100) || !Same(Y, 100) || !Same(Z, 100); } }
        /// The uniform percent (X when the axes differ).
        public double Pct { get { return X; } }
        /// Every axis within Min..Max.
        public bool InRange { get { return In(X) && In(Y) && In(Z); } }
        static bool In(double v) { return v >= Min - 0.005 && v <= Max + 0.005; }

        /// "150 %" (uniform), "150 × 100 × 50 %" (X × Y × Z), "100 %" when not scaled. Danish comma, up to 1 decimal.
        public string Text
        {
            get
            {
                if (IsUniform) return F(X) + " %";
                return F(X) + " × " + F(Y) + " × " + F(Z) + " %";
            }
        }
        static string F(double v) { return Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("0.#", Num.Dk); }

        /// Pure: "150", "150 %", "87,5" / "87.5" = uniform; "150 × 100 × 50", "150x100x50", "150; 100; 50", "150/100/50",
        /// "150 100 50" or "150,100,50" (exactly three comma parts, so "87,5" stays one number) = X/Y/Z; a trailing "%"
        /// is fine. Each axis must be a number in Min..Max (rounded to 1 decimal); else false.
        public static bool TryParse(string text, out ScaleXyz scale)
        {
            scale = null;
            string s = (text ?? "").Trim().Replace("%", " ").Trim();
            if (s.Length == 0) return false;
            string[] parts = Regex.Split(s, @"\s*[×xX*;/]\s*|\s+");
            if (parts.Length == 1 && s.Split(',').Length == 3) parts = s.Split(',');
            // "150,100" is two numbers typed with a comma, not 150,1 %: a decimal comma takes at most 2 digits here
            if (parts.Length == 1) { var c = s.Split(','); if (c.Length == 2 && c[1].Trim().Length >= 3) return false; }
            parts = parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            if (parts.Length != 1 && parts.Length != 3) return false;
            var v = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!Num.TryParse(parts[i], out v[i])) return false;
                v[i] = Math.Round(v[i], 1, MidpointRounding.AwayFromZero);
                if (!In(v[i])) return false;
            }
            scale = parts.Length == 1 ? Uniform(v[0]) : new ScaleXyz(v[0], v[1], v[2]);
            return true;
        }
    }

    /// One plate of a sliced print (idea 21): from slice_info.config (+ names from model_settings.config); G-code = one plate.
    public sealed class PlateInfo
    {
        public int Index;                 // slice_info "index" (1-based); G-code / unknown = 1
        public string Name = "";          // model_settings.config plater_name, "" = not named
        public string File = "";          // display name of the model file it came from ("" = the one file)
        public int FilePlates;            // plates that file had (AddResult); 0-1 = its only plate (PlateTitle shows just the file)
        public double Seconds;            // plate "prediction"
        public double Grams;              // plate "weight" (else the sum of its filaments)
        public List<string> Objects = new List<string>();   // <object name=…> in file order, skipped="true" left out
        public List<FilamentUse> Filaments = new List<FilamentUse>();   // this plate's slots (Slot, Colour, Type, Grams, Metres)
        // batch-plates: copies of the batch on this plate (0 = not a batch); this plate's prime tower (inside Grams /
        // Seconds; -1 = unknown, 0 = none; from plate_N.gcode / result.json of our own slices)
        public int Copies;
        public double TowerGrams = -1;
        public double TowerSeconds = -1;
        // batch-2: colour changes on this plate and their time (inside Seconds), from our own slice's result.json; -1 =
        // unknown (pre-sliced / G-code). Shared = the colours' shared last plate (SliceBatch with shareLastPlate).
        // Colours on the plate: Filaments (BambuSlicer.PlateColourCount / IsMixedPlate).
        public int ColourChanges = -1;
        public double ColourChangeSeconds = -1;
        public bool Shared;
    }

    /// The user's choice for one colour (idea 21; item i of the list = filament slot i+1).
    public sealed class ColourChoice
    {
        public string Filament = "";   // short preset name as in Settings ("Bambu PLA Basic"); "" = Settings' filament
        public string Colour = "";     // "#RRGGBB"; "" = the profile's own colour
    }

    /// What a sliced G-code file says (BambuSlicer.ParseGcode): Bambu Studio, OrcaSlicer, PrusaSlicer (and SuperSlicer),
    /// Cura (Marlin and Griffin headers). Pure; the desktop (Slicer.cs ReadGcodeFile) and the website use it.
    public sealed class SlicedGcode
    {
        public string Slicer = "";       // BambuSlicer.SlicerBambu etc., another "; generated by" name, "" = unknown
        public double Seconds;
        public List<double> Grams = new List<double>();   // per slot: index i = slot i+1, or UsedSlots[i]
        public bool GramsEstimated;      // from the length with the default density (no density in the file)
        public List<int> UsedSlots = new List<int>();     // Bambu "; filament: 1,3" (1-based), empty = 1..n
        public List<string> Colours = new List<string>(), Types = new List<string>(), Profiles = new List<string>();
        public string PrinterModel = "", PrinterSettingsId = "", Nozzle = "";
        public double PurgeGrams = -1;   // PrusaSlicer "total filament used for wipe tower [g]" (> 0 only), -1 unknown
        public byte[] Picture;           // biggest PNG/JPEG thumbnail, null = none
        internal bool NeedsTail;         // grams from length but the diameter wasn't found yet (Cura keeps it at the end)

        public bool HasTime { get { return Seconds > 0; } }
        public bool HasGrams { get { return Grams.Count > 0 && Grams.Sum() > 0; } }

        /// Time or grams (or both) were found: the readers refuse the file only when this is false (GcodeMissingText).
        /// A missing one is flagged on the result (SliceResult.MissingTime / MissingGrams, BambuSlicer.MissingValueText).
        public bool HasValues { get { return HasTime || HasGrams; } }

        /// Nothing more to look for (the reader then skips the file's last 256 KB).
        public bool Complete
        {
            get
            {
                if (!HasTime || !HasGrams || NeedsTail) return false;
                if (Slicer == BambuSlicer.SlicerCura) return true;   // Cura writes no colours / printer settings
                return Colours.Count > 0 && (PrinterModel.Length > 0 || PrinterSettingsId.Length > 0);
            }
        }

        public SliceResult ToResult() { return BambuSlicer.GcodeResult(this); }
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
        public double TowerGrams = -1;   // issue #8 step 3: this slot's prime tower (inside Grams); -1 = unknown
        public double Metres;
        // the name is the user's own choice in another slicer (G-code from Orca/Prusa/Cura): ApplyDefaultFilament leaves it
        public bool KeepProfile;
    }

    /// Issue #8 step 3: prime tower use counted in one G-code (BambuSlicer.TowerFromGcode). Grams[i] = slot i+1's tower
    /// grams (0 = that slot printed no tower); Blocks = tower sections found (0 = no tower, or a slicer whose marker we
    /// don't know: BambuSlicer.ApplyTower decides).
    public sealed class GcodeTower
    {
        public List<double> Grams = new List<double>();
        public int Blocks;
        public double Total { get { return Grams.Sum(); } }
    }

    public class SliceException : Exception
    {
        public SliceException(string message) : base(message) { }

        /// A failed Bambu Studio CLI run: its exit code and result.json error_string (null = none).
        public SliceException(string message, int exitCode, string cliError) : base(message)
        {
            ExitCode = exitCode;
            CliError = cliError;
        }

        /// The CLI's exit code (0 = not a CLI failure / unknown), e.g. -100 = "Failed slicing the model", -50 = plate.
        public int ExitCode { get; private set; }
        /// The CLI's error_string from result.json (null = none).
        public string CliError { get; private set; }
        /// True for a bed refusal (BedErrorText): the CLI's -50 (doesn't fit / empty plate) and our own SlicedOffBed
        /// check after a slice (thrown with the same code).
        public bool IsBedError { get { return ExitCode == -50; } }
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
                    var root = SliceXml.Parse(sliceInfoXml);
                    if (name.Length == 0)
                    {
                        var n = FirstPlateMeta(root, "printer_model_id");
                        if (n != null) name = AttrText(n, "value");
                    }
                    if (nozzle.Length == 0)
                    {
                        var n = FirstPlateMeta(root, "nozzle_diameters");
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
                if (f == null || f.KeepProfile || !IsUnstatedFilament(f.Profile)) continue;
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

        /// slice_info.config -> time, grams and filaments summed over the sliced plates; null when no plate has a time.
        /// Throws on text that isn't well-formed XML (desktop: the same XmlException as XmlDocument.LoadXml; website:
        /// SliceXmlException).
        public static SliceResult ParseSliceInfo(string xml)
        {
            SliceXml.Element root;
            try { root = SliceXml.Parse(xml); }
            catch (SliceXmlException)
            {
#if !WEB
                new System.Xml.XmlDocument().LoadXml(xml);   // the desktop's old error text (throws XmlException)
#endif
                throw;
            }
            var res = new SliceResult();
            var slots = new SortedDictionary<int, FilamentUse>();
            foreach (var plate in Plates(root))
            {
                double seconds = Meta(plate, "prediction");
                if (seconds <= 0) continue;
                double grams = Meta(plate, "weight");
                bool sumFilaments = grams <= 0;
                var info = new PlateInfo { Index = (int)Meta(plate, "index"), Seconds = seconds };
                if (info.Index <= 0) info.Index = res.Plates + 1;
                foreach (var f in plate.Children("filament"))
                {
                    double g = Attr(f, "used_g");
                    if (sumFilaments) grams += g;
                    int slot = (int)Attr(f, "id");
                    if (g <= 0 || slot <= 0) continue;
                    FilamentUse u;
                    string colour = NormColour(AttrText(f, "color")), type = AttrText(f, "type");
                    if (!slots.TryGetValue(slot, out u))
                    {
                        u = new FilamentUse { Slot = slot, Colour = colour, Type = type };
                        slots[slot] = u;
                    }
                    u.Grams += g;
                    u.Metres += Attr(f, "used_m");
                    info.Filaments.Add(new FilamentUse { Slot = slot, Colour = colour, Type = type, Grams = g, Metres = Attr(f, "used_m") });
                }
                foreach (var o in plate.Children("object"))
                {
                    if (string.Equals(AttrText(o, "skipped"), "true", StringComparison.OrdinalIgnoreCase)) continue;
                    info.Objects.Add(AttrText(o, "name"));
                }
                info.Grams = grams;
                res.PlateList.Add(info);
                res.Seconds += seconds;
                res.Grams += grams;
                res.Plates++;
            }
            res.Filaments = slots.Values.ToList();
            return res.Plates > 0 ? res : null;
        }

        // ---------- plates list (idea 21) ----------

        /// Plate names from Metadata/model_settings.config (`<plate>` with plater_id / plater_name) into res.PlateList
        /// (matched by Index). Null/empty/bad xml or no PlateList = nothing happens; never throws.
        public static void ApplyPlateNames(SliceResult res, string modelSettingsXml)
        {
            if (res == null || res.PlateList == null || res.PlateList.Count == 0 || string.IsNullOrEmpty(modelSettingsXml)) return;
            try
            {
                var root = SliceXml.Parse(modelSettingsXml);
                foreach (var plate in Plates(root))
                {
                    var idNode = PlateMeta(plate, "plater_id");
                    var nameNode = PlateMeta(plate, "plater_name");
                    if (idNode == null || nameNode == null) continue;
                    int id = (int)Attr(idNode, "value");
                    string name = AttrText(nameNode, "value");
                    if (id <= 0 || name.Length == 0) continue;
                    foreach (var p in res.PlateList)
                        if (p != null && p.Index == id) p.Name = name;
                }
            }
            catch (Exception) { }
        }

        /// Adds one slice to a running total (several model files): time, grams, plates, per-slot filaments (summed by
        /// slot, `slotOffset` added to r's slots first, so a project's colour 1 can go after the user's colours), known
        /// purge / colour-change values, the first picture and Studio file, and r's plates (copies) with File = fileName
        /// when they have none. r isn't changed. Null total or r = nothing.
        public static void AddResult(SliceResult total, SliceResult r, string fileName, int slotOffset)
        {
            if (total == null || r == null) return;
            if (slotOffset < 0) slotOffset = 0;
            total.Seconds += r.Seconds;
            total.Grams += r.Grams;
            total.Plates += r.Plates;
            if (total.Picture == null) total.Picture = r.Picture;
            if (total.StudioFile == null) total.StudioFile = r.StudioFile;
            if (r.SizeObjects > 0)
            {
                if (r.SizeX * r.SizeY * r.SizeZ > total.SizeX * total.SizeY * total.SizeZ)
                { total.SizeX = r.SizeX; total.SizeY = r.SizeY; total.SizeZ = r.SizeZ; }
                total.SizeObjects += r.SizeObjects;
            }
            foreach (var f in r.Filaments)
            {
                if (f == null) continue;
                int slot = f.Slot + slotOffset;
                var t = total.Filaments.FirstOrDefault(x => x.Slot == slot);
                if (t == null)
                {
                    total.Filaments.Add(new FilamentUse { Slot = slot, Colour = f.Colour ?? "", Type = f.Type ?? "", Profile = f.Profile ?? "",
                                                          Grams = f.Grams, ModelGrams = f.ModelGrams, TowerGrams = f.TowerGrams,
                                                          Metres = f.Metres, KeepProfile = f.KeepProfile });
                    continue;
                }
                t.Grams += f.Grams;
                t.Metres += f.Metres;
                t.ModelGrams = t.ModelGrams >= 0 && f.ModelGrams >= 0 ? t.ModelGrams + f.ModelGrams : -1;
                if (f.TowerGrams >= 0) t.TowerGrams = Math.Max(0, t.TowerGrams) + f.TowerGrams;
                if (string.IsNullOrEmpty(t.Colour)) t.Colour = f.Colour ?? "";
                if (string.IsNullOrEmpty(t.Type)) t.Type = f.Type ?? "";
                if (string.IsNullOrEmpty(t.Profile)) t.Profile = f.Profile ?? "";
            }
            total.Filaments.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            if (r.PurgeGrams >= 0) total.PurgeGrams = Math.Max(0, total.PurgeGrams) + r.PurgeGrams;
            if (r.ColourChangeHours >= 0) total.ColourChangeHours = Math.Max(0, total.ColourChangeHours) + r.ColourChangeHours;
            if (r.TowerGrams >= 0) total.TowerGrams = Math.Max(0, total.TowerGrams) + r.TowerGrams;
            if (r.TowerSeconds >= 0) total.TowerSeconds = Math.Max(0, total.TowerSeconds) + r.TowerSeconds;
            // batch-plates: pieces add up (SliceBatch sets Copies on every part), most per plate kept
            if (r.Copies > 0) total.Copies += r.Copies;
            if (r.PerPlate > total.PerPlate) total.PerPlate = r.PerPlate;
            if (r.CopiesMultiplied) total.CopiesMultiplied = true;
            if (r.PlateList == null) return;
            foreach (var p in r.PlateList)
            {
                if (p == null) continue;
                var c = new PlateInfo { Index = p.Index, Name = p.Name ?? "", File = string.IsNullOrEmpty(p.File) ? (fileName ?? "") : p.File,
                                        FilePlates = p.FilePlates > 0 ? p.FilePlates : r.PlateList.Count,
                                        Seconds = p.Seconds, Grams = p.Grams, Objects = new List<string>(p.Objects ?? new List<string>()),
                                        Copies = p.Copies, TowerGrams = p.TowerGrams, TowerSeconds = p.TowerSeconds,
                                        ColourChanges = p.ColourChanges, ColourChangeSeconds = p.ColourChangeSeconds, Shared = p.Shared };
                if (p.Filaments != null)
                    foreach (var f in p.Filaments)
                        if (f != null)
                            c.Filaments.Add(new FilamentUse { Slot = f.Slot + slotOffset, Colour = f.Colour ?? "", Type = f.Type ?? "",
                                                              Profile = f.Profile ?? "", Grams = f.Grams, ModelGrams = f.ModelGrams,
                                                              TowerGrams = f.TowerGrams, Metres = f.Metres, KeepProfile = f.KeepProfile });
                total.PlateList.Add(c);
            }
        }

        /// AddResult without a slot offset.
        public static void AddResult(SliceResult total, SliceResult r, string fileName)
        {
            AddResult(total, r, fileName, 0);
        }

        // ---------- batch plates (issue #9 / #10, plans\batch-plates) ----------
        // Bambu Studio's CLI: plain files take "--clone-objects n1,n2" (total copies per file; with --arrange 1 they
        // spill onto extra plates by themselves); a .3mf can't be cloned ("can not load 3mf when set ... clone_objects")
        // but takes "--repetitions N --slice <plate>", which fills that ONE plate with up to N copies of the plate's
        // contents. Copies are named "<name>_1", "<name>_2"... in slice_info / plate_N.json. Arrange leaves ~2 mm between
        // objects (measured: 28 Benchys = the 4 x 7 grid below on the A1).

        /// Most copies of one model (and of one SliceBatch call, all files together).
        public const int MaxCopies = 500;
        /// Above this many copies the window asks first (slicing takes minutes: ~1 s per Benchy-sized copy).
        public const int WarnCopies = 200;
        /// Gap Bambu Studio's arrange leaves between objects, mm (used by the footprint estimate).
        public const double ArrangeGapMm = 2;

        /// 1..MaxCopies.
        public static bool CopiesInRange(int copies)
        {
            return copies >= 1 && copies <= MaxCopies;
        }

        /// "Copies must be 1 to 500." (DA "Antal skal være 1 til 500.")
        public static string CopiesRangeText()
        {
            return L.F("Copies must be 1 to {0}.", "Antal skal være 1 til {0}.", MaxCopies);
        }

        /// Pure: "--clone-objects", "4,30" (one total count per plain model file, invariant digits). Any count outside
        /// 1..MaxCopies, or their sum over MaxCopies -> SliceException(CopiesRangeText); null / empty -> ArgumentException.
        internal static List<string> CloneArgs(IList<int> counts)
        {
            if (counts == null || counts.Count == 0) throw new ArgumentException("counts");
            long sum = 0;
            foreach (int c in counts)
            {
                if (!CopiesInRange(c)) throw new SliceException(CopiesRangeText());
                sum += c;
            }
            if (sum > MaxCopies) throw new SliceException(CopiesRangeText());
            return new List<string> { "--clone-objects", string.Join(",", counts.Select(x => x.ToString(CultureInfo.InvariantCulture))) };
        }

        /// Pure: "--repetitions", "N" for a project's plate (the run also needs "--slice <plate>", not 0). N outside
        /// 2..MaxCopies -> SliceException(CopiesRangeText) (1 copy is a normal slice).
        internal static List<string> RepetitionArgs(int copies)
        {
            if (copies < 2 || copies > MaxCopies) throw new SliceException(CopiesRangeText());
            return new List<string> { "--repetitions", copies.ToString(CultureInfo.InvariantCulture) };
        }

        /// Pure: plates for `copies` when `perPlate` fit on one: returns the plate count; fullPlates = plates holding
        /// perPlate, lastPlate = copies on the last part-full plate (0 = every plate is full). copies <= 0 -> 0 plates.
        /// perPlate <= 0 -> ArgumentOutOfRangeException. (40, 28) -> 2 (1 full + 12); (56, 28) -> 2 (2 + 0).
        public static int BatchPlates(int copies, int perPlate, out int fullPlates, out int lastPlate)
        {
            if (perPlate <= 0) throw new ArgumentOutOfRangeException("perPlate");
            fullPlates = 0; lastPlate = 0;
            if (copies <= 0) return 0;
            fullPlates = copies / perPlate;
            lastPlate = copies % perPlate;
            return fullPlates + (lastPlate > 0 ? 1 : 0);
        }

        /// BatchPlates without the split.
        public static int BatchPlates(int copies, int perPlate)
        {
            int full, last;
            return BatchPlates(copies, perPlate, out full, out last);
        }

        /// Pure: copies on a sliced plate from its object count and the objects one copy has (a project's plate can hold
        /// several parts that are repeated as a set): 294 objects / 2 = 147. objectsPerCopy <= 0 counts as 1; 0 objects = 0.
        public static int CopiesPerPlate(int objectsOnPlate, int objectsPerCopy)
        {
            if (objectsOnPlate <= 0) return 0;
            return Math.Max(1, objectsOnPlate / Math.Max(1, objectsPerCopy));
        }

        /// Pure estimate (no slicer: website, G-code): how many copies of a width x depth footprint fit on a bedX x bedY
        /// plate in a straight grid with `gap` mm between them, the better of the two turns (0° / 90°). A prime tower
        /// (towerWidth x towerDepth, 0 = none) takes the grid cells it covers in one corner. 0 = doesn't fit at all; else
        /// at least 1. Under-counts odd shapes (Bambu's arrange nests and turns them: clips 45 here vs 53 sliced), right for
        /// boxes and the Benchy (28 = sliced). width x depth must be the whole-height box (result.json bbox 60 x 31 for a
        /// Benchy), not plate_N.json's first-layer box (39 x 16 -> 84, 3x too many).
        public static int CopiesPerPlate(double width, double depth, double bedX, double bedY, double gap, double towerWidth, double towerDepth)
        {
            if (!(width > 0) || !(depth > 0) || !(bedX > 0) || !(bedY > 0) || double.IsInfinity(width) || double.IsInfinity(depth)) return 0;
            if (!(gap >= 0) || double.IsInfinity(gap)) gap = 0;
            if (!(towerWidth > 0) || !(towerDepth > 0) || double.IsInfinity(towerWidth) || double.IsInfinity(towerDepth)) { towerWidth = 0; towerDepth = 0; }
            int best = Math.Max(Grid(width, depth, bedX, bedY, gap, towerWidth, towerDepth), Grid(depth, width, bedX, bedY, gap, towerWidth, towerDepth));
            bool fits = (width <= bedX + 1e-6 && depth <= bedY + 1e-6) || (depth <= bedX + 1e-6 && width <= bedY + 1e-6);
            return fits ? Math.Max(1, best) : 0;
        }

        static int Grid(double w, double d, double bedX, double bedY, double gap, double towerW, double towerD)
        {
            long nx = (long)Math.Floor((bedX + gap) / (w + gap) + 1e-9), ny = (long)Math.Floor((bedY + gap) / (d + gap) + 1e-9);
            if (nx <= 0 || ny <= 0) return 0;
            long n = nx * ny;
            if (towerW > 0 && towerD > 0)
            {
                long bx = Math.Min(nx, (long)Math.Ceiling((towerW + gap) / (w + gap) - 1e-9));
                long by = Math.Min(ny, (long)Math.Ceiling((towerD + gap) / (d + gap) - 1e-9));
                n -= bx * by;
            }
            return (int)Math.Max(0, Math.Min(n, int.MaxValue));
        }

        /// The estimate on a printer's bed (FindPrinter(printerKey): A1 / P1S 256 x 256, A1 mini 180 x 180; unknown = A1)
        /// with ArrangeGapMm.
        public static int CopiesPerPlate(string printerKey, double width, double depth, double towerWidth, double towerDepth)
        {
            var p = FindPrinter(printerKey);
            return CopiesPerPlate(width, depth, p.BedX, p.BedY, ArrangeGapMm, towerWidth, towerDepth);
        }

        /// Pure: what a sliced file's Metadata/plate_N.json says ("bbox_objects": [{bbox: [minX, minY, maxX, maxY], name}],
        /// the tower is the object named "wipe_tower"): the box around all other objects + the tower's box + the object
        /// count. CAUTION: Bambu writes each object's FIRST-LAYER box there (a Benchy: 39 x 16 mm, its real footprint is
        /// 60 x 31 = result.json's bbox), so Width / Depth alone over-count copies about 3x. Use it for the tower box and
        /// the object count, and widen Width / Depth with the whole-height box (website: GcodeFootprint.Combine; our own
        /// slices: SliceResult.SizeX / SizeY) before CopiesPerPlate. Null on bad / empty JSON or no object with a box.
        /// Never throws.
        public static PlateFootprint ParsePlateFootprint(string plateJson)
        {
            if (string.IsNullOrEmpty(plateJson)) return null;
            try
            {
                var d = Json.Deserialize<Dictionary<string, object>>(plateJson);
                object list;
                if (d == null || !d.TryGetValue("bbox_objects", out list) || list == null || list is string) return null;
                var items = list as System.Collections.IEnumerable;
                if (items == null) return null;
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                var fp = new PlateFootprint();
                foreach (var o in items)
                {
                    var ob = o as Dictionary<string, object>;
                    object bo, no;
                    if (ob == null || !ob.TryGetValue("bbox", out bo) || bo == null || bo is string) continue;
                    var box = new List<double>();
                    var bl = bo as System.Collections.IEnumerable;
                    if (bl == null) continue;
                    foreach (var v in bl) box.Add(JsonNumber(v));
                    if (box.Count < 4 || box.Any(x => double.IsNaN(x)) || box[2] < box[0] || box[3] < box[1]) continue;
                    string name = ob.TryGetValue("name", out no) && no != null ? no.ToString() : "";
                    if (string.Equals(name.Trim(), "wipe_tower", StringComparison.OrdinalIgnoreCase))
                    {
                        fp.TowerWidth = box[2] - box[0];
                        fp.TowerDepth = box[3] - box[1];
                        continue;
                    }
                    minX = Math.Min(minX, box[0]); minY = Math.Min(minY, box[1]);
                    maxX = Math.Max(maxX, box[2]); maxY = Math.Max(maxY, box[3]);
                    fp.Objects++;
                }
                if (fp.Objects == 0) return null;
                fp.Width = Math.Round(maxX - minX, 3);
                fp.Depth = Math.Round(maxY - minY, 3);
                fp.TowerWidth = Math.Round(fp.TowerWidth, 3);
                fp.TowerDepth = Math.Round(fp.TowerDepth, 3);
                return fp;
            }
            catch (Exception) { return null; }
        }

        // a JSON number (int / long / decimal / double / numeric string, invariant); NaN when it isn't one
        static double JsonNumber(object v)
        {
            if (v == null) return double.NaN;
            try
            {
                double x = v is string ? double.Parse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture)
                                       : Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return double.IsInfinity(x) ? double.NaN : x;
            }
            catch (Exception) { return double.NaN; }
        }

        /// Pure: the object name without the copy number Bambu adds ("3dbenchy.stl_12" -> "3dbenchy.stl"); null = "".
        public static string CopyBaseName(string objectName)
        {
            return Regex.Replace(objectName ?? "", @"_\d+$", "");
        }

        /// Pure: objects per plate of a project from Metadata/model_settings.config (`<plate>` -> `<model_instance>`
        /// count), in file order. The count = how many plates the project has. Bad / empty xml = empty list; never throws.
        public static List<int> ProjectPlateObjects(string modelSettingsXml)
        {
            var list = new List<int>();
            if (string.IsNullOrEmpty(modelSettingsXml)) return list;
            try
            {
                var root = SliceXml.Parse(modelSettingsXml);
                foreach (var plate in Plates(root)) list.Add(plate.Children("model_instance").Count());
            }
            catch (Exception) { list.Clear(); }
            return list;
        }

        /// Pure: a sliced file printed `times` times (pre-sliced .3mf / G-code, multi-plate project): every number x times,
        /// its plates repeated, Copies = times, CopiesMultiplied (times > 1). A one-plate file: each plate Copies 1, PerPlate 1;
        /// several plates: Copies 0 per plate (one copy = all of them), PerPlate 0. one isn't changed; null -> null;
        /// times outside 1..MaxCopies -> SliceException(CopiesRangeText).
        public static SliceResult RepeatResult(SliceResult one, int times)
        {
            if (one == null) return null;
            if (!CopiesInRange(times)) throw new SliceException(CopiesRangeText());
            var total = new SliceResult();
            var plain = CopyWithoutCopies(one);
            for (int i = 0; i < times; i++) AddResult(total, plain, "");
            bool onePlate = one.PlateList != null && one.PlateList.Count == 1;
            foreach (var p in total.PlateList) p.Copies = onePlate ? 1 : 0;
            total.Copies = times;
            total.PerPlate = onePlate ? 1 : 0;
            total.CopiesMultiplied = times > 1;
            total.FromSlicedFile = one.FromSlicedFile;
            total.PrinterModel = one.PrinterModel; total.PrinterName = one.PrinterName ?? ""; total.PrinterNozzle = one.PrinterNozzle ?? "";
            total.UnsupportedPrinter = one.UnsupportedPrinter;
            total.SlicerName = one.SlicerName ?? ""; total.GramsEstimated = one.GramsEstimated; total.OtherPrinter = one.OtherPrinter;
            total.MissingTime = one.MissingTime; total.MissingGrams = one.MissingGrams;
            total.SizeObjects = one.SizeObjects;
            if (one.PurgeGrams < 0) total.PurgeGrams = -1;
            if (one.ColourChangeHours < 0) total.ColourChangeHours = -1;
            NumberBatchPlates(total);
            return total;
        }

        // a shallow copy of r's totals and plates without Copies / PerPlate (so AddResult doesn't count them)
        static SliceResult CopyWithoutCopies(SliceResult r)
        {
            var c = new SliceResult { Seconds = r.Seconds, Grams = r.Grams, Plates = r.Plates, Picture = r.Picture, StudioFile = r.StudioFile,
                                      Filaments = r.Filaments ?? new List<FilamentUse>(), PurgeGrams = r.PurgeGrams,
                                      ColourChangeHours = r.ColourChangeHours, PlateList = r.PlateList ?? new List<PlateInfo>(),
                                      SizeX = r.SizeX, SizeY = r.SizeY, SizeZ = r.SizeZ, SizeObjects = r.SizeObjects,
                                      TowerGrams = r.TowerGrams, TowerSeconds = r.TowerSeconds };
            return c;
        }

        /// Pure: numbers a batch's plates 1, 2, 3... per File (in list order) and sets FilePlates = that file's count, so
        /// repeated plates read "Plate 1..4" instead of four "Plate 1". Null = nothing.
        public static void NumberBatchPlates(SliceResult res)
        {
            if (res == null || res.PlateList == null) return;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in res.PlateList)
            {
                if (p == null) continue;
                string f = p.File ?? "";
                int n;
                counts.TryGetValue(f, out n);
                counts[f] = n + 1;
                p.Index = n + 1;
            }
            foreach (var p in res.PlateList)
                if (p != null) p.FilePlates = counts[p.File ?? ""];
        }

        /// Pure: per-plate prime tower grams (plate index -> that plate's G-code count) into res.PlateList (TowerGrams and
        /// each plate filament's TowerGrams; plates not in the map keep -1). Null = nothing.
        public static void ApplyPlateTowers(SliceResult res, IDictionary<int, GcodeTower> perPlate)
        {
            if (res == null || res.PlateList == null || perPlate == null) return;
            foreach (var p in res.PlateList)
            {
                GcodeTower t;
                if (p == null || !perPlate.TryGetValue(p.Index, out t) || t == null) continue;
                p.TowerGrams = Math.Round(t.Total, 3);
                foreach (var f in p.Filaments)
                    if (f != null) f.TowerGrams = f.Slot >= 1 && f.Slot <= t.Grams.Count ? t.Grams[f.Slot - 1] : 0;
            }
        }

        // ---------- share the last plate (batch-2, issue #10, plans\batch-2) ----------
        // Full plates stay one colour (one clone run per colour); the colours' part-full last plates can go on ONE shared
        // plate with Bambu Studio's "--load-assemble-list <json>" (plates exactly as listed, each object on its own
        // filament, arranged per plate; STL / OBJ only; an over-full plate fails with -21 instead of spilling; needs
        // "--ensure-on-bed" or models sit half under the bed and the grams are halved). CLI bug (any multi-filament run):
        // a plate that uses ONE filament other than 1 is reported as filament 1 with filament 1's density (the G-code and
        // time are right): FixSingleFilamentPlates.

        /// A filament under this many grams on a plate doesn't count as a colour there (a plate without filament 1 still
        /// lists ~0,3 g of it: the start purge).
        public const double MinColourGrams = 0.5;

        /// Colours (slots) on a plate with at least MinColourGrams. Null = 0.
        public static int PlateColourCount(PlateInfo p)
        {
            if (p == null || p.Filaments == null) return 0;
            return p.Filaments.Where(f => f != null && f.Grams >= MinColourGrams).Select(f => f.Slot).Distinct().Count();
        }

        /// The plate prints 2+ colours (PlateColourCount >= 2): the plates menu's / website's "· 2 colours" mark.
        public static bool IsMixedPlate(PlateInfo p)
        {
            return PlateColourCount(p) >= 2;
        }

        /// An assemble list can load it: .stl / .obj (not STEP / AMF / 3MF / G-code).
        public static bool CanShareFile(string path)
        {
            string p = (path ?? "").Trim();
            return p.EndsWith(".stl", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".obj", StringComparison.OrdinalIgnoreCase);
        }

        /// Pure: Bambu Studio's assemble list for `plates` (keys from BambuStudio.hpp: plates / plate_name / need_arrange /
        /// plate_params / objects / path / count / filaments / assemble_index). Paths with forward slashes, JSON-escaped;
        /// every plate need_arrange true. Null / empty plates, a plate without objects or an object with an empty path ->
        /// ArgumentException; Count outside 1..MaxCopies or Slot < 1 -> ArgumentOutOfRangeException.
        public static string AssembleListJson(IList<AssemblePlate> plates)
        {
            if (plates == null || plates.Count == 0) throw new ArgumentException("plates");
            var sb = new System.Text.StringBuilder("{\"plates\":[");
            for (int i = 0; i < plates.Count; i++)
            {
                var pl = plates[i];
                if (pl == null || pl.Objects == null || pl.Objects.Count == 0) throw new ArgumentException("plates");
                if (i > 0) sb.Append(',');
                sb.Append("{\"plate_name\":").Append(JsonString(pl.Name ?? "")).Append(",\"need_arrange\":true,\"plate_params\":{},\"objects\":[");
                for (int j = 0; j < pl.Objects.Count; j++)
                {
                    var o = pl.Objects[j];
                    if (o == null || string.IsNullOrEmpty(o.Path)) throw new ArgumentException("plates");
                    if (!CopiesInRange(o.Count)) throw new ArgumentOutOfRangeException("plates", "count");
                    if (o.Slot < 1) throw new ArgumentOutOfRangeException("plates", "slot");
                    if (j > 0) sb.Append(',');
                    sb.Append("{\"path\":").Append(JsonString(o.Path.Replace('\\', '/')))
                      .Append(",\"count\":").Append(o.Count.ToString(CultureInfo.InvariantCulture))
                      .Append(",\"filaments\":[").Append(o.Slot.ToString(CultureInfo.InvariantCulture))
                      .Append("],\"assemble_index\":[0]}");
                }
                sb.Append("]}");
            }
            return sb.Append("]}").ToString();
        }

        // a JSON string literal ("…" with \" \\ and control characters escaped)
        static string JsonString(string s)
        {
            var sb = new System.Text.StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// Pure: the CLI options for an assemble run: "--load-assemble-list", listPath, then scaleArgs (ScaleArgs: "--scale
        /// f" works with it), and "--ensure-on-bed" exactly once (always: without it the grams are halved).
        internal static List<string> AssembleArgs(string listPath, IList<string> scaleArgs)
        {
            var list = new List<string> { "--load-assemble-list", listPath ?? "" };
            if (scaleArgs != null) list.AddRange(scaleArgs.Where(a => a != "--ensure-on-bed"));
            list.Add("--ensure-on-bed");
            return list;
        }

        /// Pure: copies per file on a clone run's plate. objects = the plate's object names ("cable_hook.stl_3": the file
        /// name + Bambu's copy number), fileNames = the run's file names (no folder). Result[i] = copies of fileNames[i].
        /// Null when an object matches no file or two files have the same name (can't tell them apart).
        public static int[] LastPlateCounts(IList<string> objects, IList<string> fileNames)
        {
            if (objects == null || fileNames == null || fileNames.Count == 0) return null;
            var names = fileNames.Select(n => (n ?? "").Trim()).ToList();
            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) return null;
            var counts = new int[names.Count];
            foreach (var o in objects)
            {
                string b = CopyBaseName((o ?? "").Trim());
                int k = names.FindIndex(n => string.Equals(n, b, StringComparison.OrdinalIgnoreCase));
                if (k < 0) return null;
                counts[k]++;
            }
            return counts;
        }

        /// Pure: an assemble run's object name ("C:/jobs/cable_hook_2" = path without extension + copy number) as a clone
        /// run writes it ("cable_hook.stl_2"), matched against the run's paths (either slash). No match = unchanged.
        public static string AssembleObjectName(string objectName, IList<string> paths)
        {
            string name = objectName ?? "";
            if (paths == null) return name;
            string b = CopyBaseName(name), suffix = name.Substring(b.Length);
            foreach (var path in paths)
            {
                string q = (path ?? "").Replace('\\', '/');
                int slash = q.LastIndexOf('/'), dot = q.LastIndexOf('.');
                string stem = dot > slash ? q.Substring(0, dot) : q;
                if (q.Length > 0 && string.Equals(stem, b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    return q.Substring(slash + 1) + suffix;
            }
            return name;
        }

        /// Pure: index of the biggest leftover group (largest area; ties = the first), -1 for null / empty.
        public static int BiggestGroup(IList<double> areas)
        {
            if (areas == null || areas.Count == 0) return -1;
            int best = 0;
            for (int i = 1; i < areas.Count; i++) if (areas[i] > areas[best]) best = i;
            return best;
        }

        /// Pure: r without its last plate (the plate the shared plate takes over): Seconds / Grams / Plates and the
        /// per-slot filaments (Grams, Metres, known ModelGrams / TowerGrams) less that plate's, Copies less its copies,
        /// PerPlate = most on the plates left. Purge / colour-change / tower totals less the plate's known values.
        /// r isn't changed; null or no plate -> a copy as it is.
        public static SliceResult WithoutLastPlate(SliceResult r)
        {
            if (r == null) return null;
            var c = new SliceResult();
            AddResult(c, r, "");
            c.Copies = r.Copies; c.PerPlate = r.PerPlate; c.CopiesMultiplied = r.CopiesMultiplied;
            c.PurgeGrams = r.PurgeGrams; c.ColourChangeHours = r.ColourChangeHours; c.TowerGrams = r.TowerGrams; c.TowerSeconds = r.TowerSeconds;
            c.SizeX = r.SizeX; c.SizeY = r.SizeY; c.SizeZ = r.SizeZ; c.SizeObjects = r.SizeObjects;
            if (c.PlateList.Count == 0) return c;
            var last = c.PlateList[c.PlateList.Count - 1];
            c.PlateList.RemoveAt(c.PlateList.Count - 1);
            foreach (var p in c.PlateList) p.FilePlates = c.PlateList.Count;
            c.Seconds = Math.Max(0, c.Seconds - last.Seconds);
            c.Grams = Math.Max(0, c.Grams - last.Grams);
            c.Plates = Math.Max(0, c.Plates - 1);
            c.Copies = Math.Max(0, c.Copies - last.Copies);
            c.PerPlate = c.PlateList.Count == 0 ? 0 : c.PlateList.Max(x => x.Copies);
            foreach (var f in last.Filaments)
            {
                var t = c.Filaments.FirstOrDefault(x => x.Slot == f.Slot);
                if (t == null) continue;
                t.Grams = Math.Max(0, t.Grams - f.Grams);
                t.Metres = Math.Max(0, t.Metres - f.Metres);
                // a one-colour plate has no purge: its model grams = its grams
                if (t.ModelGrams >= 0) t.ModelGrams = Math.Max(0, t.ModelGrams - (f.ModelGrams >= 0 ? f.ModelGrams : f.Grams));
                if (t.TowerGrams >= 0 && f.TowerGrams > 0) t.TowerGrams = Math.Max(0, t.TowerGrams - f.TowerGrams);
            }
            c.Filaments.RemoveAll(f => f.Grams <= 0.0005 && f.Metres <= 0.0005);
            if (c.TowerGrams >= 0 && last.TowerGrams > 0) c.TowerGrams = Math.Max(0, c.TowerGrams - last.TowerGrams);
            if (c.TowerSeconds >= 0 && last.TowerSeconds > 0) c.TowerSeconds = Math.Max(0, c.TowerSeconds - last.TowerSeconds);
            if (c.ColourChangeHours >= 0 && last.ColourChangeSeconds > 0) c.ColourChangeHours = Math.Max(0, c.ColourChangeHours - last.ColourChangeSeconds / 3600.0);
            return c;
        }

        /// Pure: an assemble run loaded only some colours: its slot j (1-based) is realSlots[j-1]. Renumbers r's
        /// filaments and every plate's (slots outside the list stay). Null = nothing.
        public static void RemapSlots(SliceResult r, IList<int> realSlots)
        {
            if (r == null || realSlots == null) return;
            Func<int, int> map = s => s >= 1 && s <= realSlots.Count && realSlots[s - 1] >= 1 ? realSlots[s - 1] : s;
            foreach (var f in r.Filaments) if (f != null) f.Slot = map(f.Slot);
            r.Filaments.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            foreach (var p in r.PlateList)
                if (p != null)
                    foreach (var f in p.Filaments) if (f != null) f.Slot = map(f.Slot);
        }

        /// Pure: the extruders (filaments, 1-based) each plate's objects use, from Metadata/model_settings.config:
        /// plater_id -> one entry per model_instance = its object's "extruder" (a part with another extruder > 0 -> 0 =
        /// mixed / unknown; no extruder key = 1). Bad / empty xml = empty; never throws.
        public static Dictionary<int, List<int>> ObjectExtruders(string modelSettingsXml)
        {
            var res = new Dictionary<int, List<int>>();
            if (string.IsNullOrEmpty(modelSettingsXml)) return res;
            try
            {
                var root = SliceXml.Parse(modelSettingsXml);
                if (!root.Is("config")) return res;
                var byId = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var ob in root.Children("object"))
                {
                    string id = AttrText(ob, "id");
                    var em = ob.Children("metadata").FirstOrDefault(m => m.Attr("key") == "extruder");
                    int e = em == null ? 1 : (int)Attr(em, "value");
                    if (e <= 0) e = 1;
                    foreach (var part in ob.Children("part"))
                    {
                        var pm = part.Children("metadata").FirstOrDefault(m => m.Attr("key") == "extruder");
                        int pe = pm == null ? 0 : (int)Attr(pm, "value");
                        if (pe > 0 && pe != e) e = 0;
                    }
                    if (id.Length > 0) byId[id] = e;
                }
                foreach (var plate in Plates(root))
                {
                    var idNode = PlateMeta(plate, "plater_id");
                    int pid = idNode == null ? 0 : (int)Attr(idNode, "value");
                    if (pid <= 0) continue;
                    var list = new List<int>();
                    foreach (var inst in plate.Children("model_instance"))
                    {
                        var om = inst.Children("metadata").FirstOrDefault(m => m.Attr("key") == "object_id");
                        int e;
                        list.Add(om != null && byId.TryGetValue(AttrText(om, "value"), out e) ? e : 0);
                    }
                    res[pid] = list;
                }
            }
            catch (Exception) { res.Clear(); }
            return res;
        }

        /// Pure: numbers from per-slot strings (invariant, "1.26"); unreadable / missing = 0.
        public static List<double> SlotNumbers(IList<string> values)
        {
            var list = new List<double>();
            if (values == null) return list;
            foreach (var v in values)
            {
                double d;
                list.Add(double.TryParse((v ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0 && !double.IsInfinity(d) ? d : 0);
            }
            return list;
        }

        /// Pure: the CLI bug fix for one plate. When every object on it uses the same filament n (extruders, from
        /// ObjectExtruders) and n != 1, but the plate lists only filament 1: that one filament becomes slot n, its grams
        /// (and known ModelGrams) x densities[n-1] / densities[0] (unknown density = x 1), colour / type / profile from
        /// slot n's entries (null lists or missing = "" / unchanged profile), plate Grams adjusted. True when changed.
        /// Example: 2 blue clips reported as slot 1, 5,86 g, densities 1,26 / 1,5 -> slot 2, 6,98 g.
        public static bool FixSingleFilamentPlate(PlateInfo p, IList<int> extruders, IList<double> densities,
                                                  IList<string> colours, IList<string> types, IList<string> profiles)
        {
            if (p == null || p.Filaments == null || extruders == null || extruders.Count == 0) return false;
            if (extruders.Any(e => e <= 0)) return false;
            var distinct = extruders.Distinct().ToList();
            if (distinct.Count != 1 || distinct[0] == 1) return false;
            int n = distinct[0];
            var used = p.Filaments.Where(f => f != null && f.Grams > 0).ToList();
            if (used.Count != 1 || used[0].Slot != 1) return false;
            var f1 = used[0];
            double d1 = densities != null && densities.Count >= 1 ? densities[0] : 0;
            double dn = densities != null && densities.Count >= n ? densities[n - 1] : 0;
            double factor = d1 > 0 && dn > 0 ? dn / d1 : 1;
            double old = f1.Grams;
            f1.Grams = Math.Round(old * factor, 2);
            if (f1.ModelGrams >= 0) f1.ModelGrams = Math.Round(f1.ModelGrams * factor, 2);
            f1.Slot = n;
            f1.Colour = colours != null && colours.Count >= n ? NormColour(colours[n - 1]) : "";
            f1.Type = types != null && types.Count >= n ? (types[n - 1] ?? "").Trim() : "";
            if (profiles != null && profiles.Count >= n) f1.Profile = StripSlotTag(profiles[n - 1] ?? "");
            p.Grams = Math.Round(Math.Max(0, p.Grams - old + f1.Grams), 2);
            return true;
        }

        /// The fix on every plate of a sliced .3mf (desktop ReadSliceInfo and the website's sliced-file reader): plates
        /// matched by Index = plater_id; res.Filaments / Grams moved and re-weighed to match (ModelGrams of the moved
        /// slots become -1 = unknown). Densities, colours, types and profiles from project_settings.config. Returns the
        /// plates fixed (0 = nothing; also on missing / bad xml or json). Sets res.SingleFilamentFixes.
        public static int FixSingleFilamentPlates(SliceResult res, string modelSettingsXml, string projectSettingsJson)
        {
            if (res == null || res.PlateList == null || res.PlateList.Count == 0) return 0;
            var extr = ObjectExtruders(modelSettingsXml);
            if (extr.Count == 0) return 0;
            var settings = ParseSettings(projectSettingsJson);
            var densities = SlotNumbers(SlotStrings(settings, "filament_density"));
            var colours = SlotStrings(settings, "filament_colour");
            var types = SlotStrings(settings, "filament_type");
            var profiles = SlotStrings(settings, "filament_settings_id");
            int fixes = 0;
            foreach (var p in res.PlateList)
            {
                List<int> e;
                if (p == null || !extr.TryGetValue(p.Index, out e)) continue;
                var before = p.Filaments.FirstOrDefault(f => f != null && f.Grams > 0);
                double oldGrams = before == null ? 0 : before.Grams, metres = before == null ? 0 : before.Metres;
                if (!FixSingleFilamentPlate(p, e, densities, colours, types, profiles)) continue;
                fixes++;
                res.Grams = Math.Max(0, res.Grams - oldGrams + before.Grams);
                var t1 = res.Filaments.FirstOrDefault(x => x.Slot == 1);
                if (t1 != null)
                {
                    t1.Grams = Math.Max(0, t1.Grams - oldGrams);
                    t1.Metres = Math.Max(0, t1.Metres - metres);
                    t1.ModelGrams = -1;
                }
                var tn = res.Filaments.FirstOrDefault(x => x.Slot == before.Slot);
                if (tn == null)
                {
                    tn = new FilamentUse { Slot = before.Slot, Colour = before.Colour, Type = before.Type, Profile = before.Profile };
                    res.Filaments.Add(tn);
                }
                tn.Grams += before.Grams;
                tn.Metres += metres;
                tn.ModelGrams = -1;
            }
            if (fixes == 0) return 0;
            res.Filaments.RemoveAll(f => f.Grams <= 0.0005 && f.Metres <= 0.0005);
            res.Filaments.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            res.SingleFilamentFixes += fixes;
            return fixes;
        }

        /// Pure: the trade-off of a shared plate against the own last plates it replaced (ShareInfo, Applied):
        /// PlatesSaved = replaced - 1, ExtraSeconds / ExtraGrams = shared minus the replaced plates' sums (rounded to 1 s /
        /// 0,01 g), Colours = PlateColourCount(shared), ColourChanges = the shared plate's. Null shared / < 2 replaced -> null.
        public static ShareInfo ShareTradeOff(IList<PlateInfo> replaced, PlateInfo shared)
        {
            if (shared == null || replaced == null || replaced.Count(x => x != null) < 2) return null;
            var own = replaced.Where(x => x != null).ToList();
            return new ShareInfo
            {
                Applied = true,
                Colours = PlateColourCount(shared),
                PlatesSaved = own.Count - 1,
                ExtraSeconds = Math.Round(shared.Seconds - own.Sum(x => x.Seconds), 0),
                ExtraGrams = Math.Round(shared.Grams - own.Sum(x => x.Grams), 2),
                ColourChanges = shared.ColourChanges
            };
        }

        public const int MaxPlateNameChars = 40;

        /// text cut to `max` characters with "…" at the end (trimmed; null = "").
        public static string CutName(string text, int max)
        {
            string t = (text ?? "").Trim();
            if (max < 2 || t.Length <= max) return t;
            return t.Substring(0, max - 1).TrimEnd() + "…";
        }

        /// "Plate 2 'Wheels'" (DA "Plade 2 'Wheels'"). showFile and the plate has a file name: the file's only plate =
        /// "body.stl"; one of several = "multi.3mf, plate 2 'Wheels'" (DA "plade"). Names are cut at MaxPlateNameChars.
        public static string PlateTitle(PlateInfo p, bool showFile)
        {
            if (p == null) return "";
            string file = showFile ? CutName(p.File, MaxPlateNameChars) : "";
            if (file.Length > 0 && p.FilePlates <= 1) return file;
            string s = file.Length > 0 ? file + ", " + L.F("plate {0}", "plade {0}", p.Index > 0 ? p.Index : 1)
                                       : L.F("Plate {0}", "Plade {0}", p.Index > 0 ? p.Index : 1);
            string name = CutName(p.Name, MaxPlateNameChars);
            if (name.Length > 0) s += " '" + name + "'";
            return s;
        }

        /// "1 h 20 m, 34,1 g, 3 objects" (DA "1 t 20 m, 34,1 g, 3 objekter"; time as the window's ✓ line = QuoteRows.Time,
        /// whole minutes, at least 1 m when > 0 s); objects left out when none.
        public static string PlateNumbers(PlateInfo p)
        {
            if (p == null) return "";
            long minutes = (long)Math.Round(Math.Max(0, p.Seconds) / 60.0);
            if (minutes == 0 && p.Seconds > 0) minutes = 1;
            string time = QuoteRows.Time((int)(minutes / 60), (int)(minutes % 60));
            // grams like the window's one-colour ✓ line (MainForm: 1 decimal, "0.#": 34 g, 81,4 g)
            string s = time + ", " + Math.Round(Math.Max(0, p.Grams), 1).ToString("0.#", Num.Dk) + " g";
            int n = p.Objects == null ? 0 : p.Objects.Count;
            if (n == 1) s += L.S(", 1 object", ", 1 objekt");
            else if (n > 1) s += L.F(", {0} objects", ", {0} objekter", n);
            return s;
        }

        /// PlateTitle + " – " + PlateNumbers ("Plate 2 'Wheels' – 1 h 20 m, 34 g, 3 objects").
        public static string PlateLine(PlateInfo p, bool showFile)
        {
            if (p == null) return "";
            return PlateTitle(p, showFile) + " – " + PlateNumbers(p);
        }

        // XPath "/config/plate" (root config, its plate children)
        static IEnumerable<SliceXml.Element> Plates(SliceXml.Element root)
        {
            return root.Is("config") ? root.Children("plate") : Enumerable.Empty<SliceXml.Element>();
        }

        // XPath "/config/plate/metadata[@key='…']": the first one in document order, or null
        static SliceXml.Element FirstPlateMeta(SliceXml.Element root, string key)
        {
            foreach (var plate in Plates(root))
            {
                var n = PlateMeta(plate, key);
                if (n != null) return n;
            }
            return null;
        }

        // XPath "metadata[@key='…']" under a plate: the first one, or null
        static SliceXml.Element PlateMeta(SliceXml.Element plate, string key)
        {
            return plate.Children("metadata").FirstOrDefault(m => m.Attr("key") == key);
        }

        static string AttrText(SliceXml.Element n, string name)
        {
            return (n.Attr(name) ?? "").Trim();
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

        static double Meta(SliceXml.Element plate, string key)
        {
            var n = PlateMeta(plate, key);
            return n == null ? 0 : Attr(n, "value");
        }

        static double Attr(SliceXml.Element n, string name)
        {
            string a = n.Attr(name);
            double v;
            if (a != null && double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0) return v;
            return 0;
        }

        // ---------- sliced G-code from Bambu Studio, OrcaSlicer, PrusaSlicer and Cura (idea 14) ----------

        public const string SlicerBambu = "Bambu Studio", SlicerOrca = "OrcaSlicer", SlicerPrusa = "PrusaSlicer", SlicerCura = "Cura";
        public const int GcodeHeadBytes = 1024 * 1024;   // header, thumbnails, Bambu/Orca config block
        public const int GcodeTailBytes = 256 * 1024;    // PrusaSlicer/Orca statistics + config, Cura settings
        public const int MaxGcodePictureBytes = 4 * 1048576;
        public const double DefaultDensity = 1.24;       // PLA, g/cm³, when the file gives none
        public const double DefaultDiameter = 1.75;      // mm

        static readonly string[] GcodeExtensions = { ".gcode", ".gco", ".gc", ".bgcode" };

        /// A G-code file by its name (.gcode, .gco, .gc, also PrusaSlicer's binary .bgcode).
        public static bool IsGcodeFile(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            return GcodeExtensions.Any(e => n.EndsWith(e, StringComparison.Ordinal));
        }

        /// PrusaSlicer's binary G-code: a .bgcode name, or bytes starting with "GCDE".
        public static bool IsBinaryGcode(string name, byte[] head)
        {
            if ((name ?? "").Trim().EndsWith(".bgcode", StringComparison.OrdinalIgnoreCase)) return true;
            return head != null && head.Length >= 4 && head[0] == (byte)'G' && head[1] == (byte)'C' && head[2] == (byte)'D' && head[3] == (byte)'E';
        }

        public static string BinaryGcodeText()
        {
            return L.S("This is a binary G-code (.bgcode), which PrintPrice can't read. In PrusaSlicer, untick \"Supports binary G-code\" " +
                       "(Printer Settings > General > Firmware), export again and use the .gcode file.",
                       "Filen er en binær G-code (.bgcode), som PrintPrice ikke kan læse. I PrusaSlicer: fjern fluebenet i \"Supports binary G-code\" " +
                       "(Printer Settings > General > Firmware), eksportér igen og brug .gcode-filen.");
        }

        public static string GcodeMissingText()
        {
            return L.S("Couldn't find the print time and weight in this G-code. Bambu Studio, OrcaSlicer, PrusaSlicer and Cura files can be read. " +
                       "You can type the time and weight in yourself.",
                       "Kunne ikke finde printtid og vægt i denne G-code. Filer fra Bambu Studio, OrcaSlicer, PrusaSlicer og Cura kan læses. " +
                       "Du kan selv skrive tid og vægt ind.");
        }

        public const double MaxGcodeSeconds = 30 * 86400;   // 30 days
        public const double MaxGcodeGrams = 20000;          // 20 kg in total
        public const double MaxSlotGrams = 10000;           // 10 kg for one colour

        /// True when a G-code's numbers can't be a real print (over 30 days, 20 kg, 10 kg per colour, 64+ colours, or
        /// not a number): GcodeResult refuses them with ImpossibleValuesText instead of showing them.
        public static bool ImpossibleValues(SlicedGcode g)
        {
            if (g == null) return false;
            if (double.IsNaN(g.Seconds) || g.Seconds > MaxGcodeSeconds) return true;
            if (g.Grams.Count > 64 || g.Grams.Any(x => double.IsNaN(x) || x > MaxSlotGrams)) return true;
            return g.Grams.Sum() > MaxGcodeGrams;
        }

        public static string ImpossibleValuesText()
        {
            return L.S("The numbers in this file don't look right (over 30 days or 20 kg), so they weren't used. Type the time and weight in yourself.",
                       "Tallene i filen ser forkerte ud (over 30 dage eller 20 kg), så de blev ikke brugt. Skriv selv tid og vægt ind.");
        }

        /// What to tell the user when a G-code gave only one of time and weight (null when both or neither are missing).
        public static string MissingValueText(SliceResult r)
        {
            if (r == null || r.MissingTime == r.MissingGrams) return null;
            return r.MissingTime
                ? L.S("Couldn't find the print time in the file: type it in.", "Kunne ikke finde printtiden i filen: skriv den ind.")
                : L.S("Couldn't find the weight in the file: type it in.", "Kunne ikke finde vægten i filen: skriv den ind.");
        }

        /// A short muted note (one sentence, two with GramsEstimated) for a file from another slicer than Bambu Studio;
        /// null for Bambu Studio / unknown. The printer warning is separate: OtherPrinterWarning.
        public static string OtherSlicerNote(SliceResult r)
        {
            if (r == null || string.IsNullOrEmpty(r.SlicerName) || r.SlicerName == SlicerBambu) return null;
            string name = r.SlicerName.Trim();
            var parts = new List<string>();
            if (!r.MissingTime)   // no time in the file: nothing to say about it
                parts.Add(L.F("Time is {0}'s estimate and may differ in Bambu Studio.",
                              "Tiden er {1} skøn og kan afvige i Bambu Studio.", name, DanishGenitive(name)));
            if (r.GramsEstimated)
                parts.Add(L.S("Weight is estimated from the filament length.", "Vægten er anslået ud fra filamentlængden."));
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }

        /// The warning for a G-code sliced for a printer that isn't a Bambu Lab one (SliceResult.OtherPrinter), in the
        /// words of the other-Bambu-printer warning; selectedModelKey = the active printer (AppSettings.PrinterModel).
        /// Null when the result has no such printer.
        public static string OtherPrinterWarning(SliceResult r, string selectedModelKey)
        {
            if (r == null || !r.OtherPrinter) return null;
            if (r.MissingTime)
                return L.F("The file was sliced for {0}, not the {1}.", "Filen er slicet til {0}, ikke {1}.",
                           r.PrinterName.Trim(), Wear.FindModel(selectedModelKey).Label);
            return L.F("The file was sliced for {0}, not the {1}: its time is for that printer.",
                       "Filen er slicet til {0}, ikke {1}: tiden gælder den printer.",
                       r.PrinterName.Trim(), Wear.FindModel(selectedModelKey).Label);
        }

        // Danish genitive: "Curas", "OrcaSlicers"; a name ending in s, x or z gets an apostrophe ("Xs'")
        static string DanishGenitive(string name)
        {
            return Regex.IsMatch(name, "[sxzSXZ]$") ? name + "'" : name + "s";
        }

        static readonly Regex BambuIdRx = new Regex(@"^;\s*BambuStudio\b", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex GeneratedByRx = new Regex(@"^;\s*generated by\s+([A-Za-z][A-Za-z0-9_\-]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex CuraIdRx = new Regex(@"^;\s*(?:Generated with\s+|GENERATOR\.NAME:\s*)Cura", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex TotalTimeRx = new Regex(@"total estimated time\s*[:=]\s*([0-9dhms ]+)", RegexOptions.IgnoreCase);
        static readonly Regex NormalTimeRx = new Regex(@"^;\s*estimated printing time \(normal mode\)\s*[:=]\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex AnyTimeRx = new Regex(@"^;\s*estimated printing time(?: \([a-z]+ mode\))?\s*[:=]\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex CuraTimeRx = new Regex(@"^;(?:PRINT\.)?TIME:\s*(\d+(?:\.\d+)?)\s*$", RegexOptions.Multiline);
        static readonly Regex WeightRx = new Regex(@"^;\s*total filament weight \[g\]\s*[:=]\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex UsedGRx = new Regex(@"^;\s*filament used \[g\]\s*[:=]\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex TotalUsedGRx = new Regex(@"^;\s*total filament used \[g\]\s*[:=]\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex CuraWeightRx = new Regex(@"^;\s*Filament weight\s*[:=]\s*\[?([^\]\n]*)\]?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex LengthMmRx = new Regex(@"^;\s*(?:total filament length|filament used) \[mm\]\s*[:=]\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex CuraMetresRx = new Regex(@"^;\s*Filament used:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex GriffinVolumeRx = new Regex(@"^;EXTRUDER_TRAIN\.(\d+)\.MATERIAL\.VOLUME_USED:\s*([0-9.]+)\s*$", RegexOptions.Multiline);
        static readonly Regex WipeTowerRx = new Regex(@"^;\s*total filament used for wipe tower \[g\]\s*[:=]\s*([0-9.]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex SlotsRx = new Regex(@"^;\s*filament:\s*([0-9, ]+)$", RegexOptions.Multiline);
        static readonly Regex GcodeThumbRx = new Regex(@"^;\s*thumbnail(?:_PNG|_JPG|_QOI)?\s+begin[^\n]*\n((?:;[^\n]*\n)+?);\s*thumbnail(?:_PNG|_JPG|_QOI)?\s+end",
                                                       RegexOptions.Multiline | RegexOptions.IgnoreCase);

        /// Which slicer wrote this G-code: SlicerBambu, SlicerOrca, SlicerPrusa, SlicerCura, another "; generated by" name, or "".
        public static string GcodeSlicer(string text)
        {
            text = text ?? "";
            if (BambuIdRx.IsMatch(text)) return SlicerBambu;
            if (CuraIdRx.IsMatch(text)) return SlicerCura;
            var m = GeneratedByRx.Match(text);
            if (!m.Success) return "";
            string n = m.Groups[1].Value;
            if (n.StartsWith("BambuStudio", StringComparison.OrdinalIgnoreCase)) return SlicerBambu;
            if (n.StartsWith("OrcaSlicer", StringComparison.OrdinalIgnoreCase)) return SlicerOrca;
            if (n.StartsWith("PrusaSlicer", StringComparison.OrdinalIgnoreCase)) return SlicerPrusa;
            return n;
        }

        /// Reads print time, grams per slot, colours, filament types/profiles, printer and thumbnail from G-code text
        /// (the file's head, plus its tail when Complete is false). Missing values stay empty; never throws on bad text.
        /// Time: "total estimated time" (Bambu, Orca on Bambu printers) > "estimated printing time (normal mode)" (Prusa,
        /// Orca) > any "estimated printing time" > Cura ";TIME:" (largest) / ";PRINT.TIME:". Grams: "total filament weight [g]"
        /// (per slot) > "filament used [g]" (per slot) > "total filament used [g]" > Cura ";Filament weight =" > from the length
        /// ("filament used [mm]", Cura ";Filament used: 1.2m", Griffin VOLUME_USED) with the file's density (else 1,24 =
        /// GramsEstimated) and diameter (else 1,75).
        public static SlicedGcode ParseGcode(string text)
        {
            var g = new SlicedGcode();
            text = (text ?? "").Replace("\r", "").TrimStart('﻿');   // a UTF-8 BOM would hide the first line ("; generated by …")
            g.Slicer = GcodeSlicer(text);

            // time
            var m = TotalTimeRx.Match(text);
            if (m.Success) g.Seconds = Duration(m.Groups[1].Value);
            if (g.Seconds <= 0) { m = NormalTimeRx.Match(text); if (m.Success) g.Seconds = Duration(m.Groups[1].Value); }
            if (g.Seconds <= 0) { m = AnyTimeRx.Match(text); if (m.Success) g.Seconds = Duration(m.Groups[1].Value); }
            if (g.Seconds <= 0)
                foreach (Match t in CuraTimeRx.Matches(text))
                {
                    double v;
                    if (double.TryParse(t.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > g.Seconds) g.Seconds = v;
                }

            // grams as written
            foreach (var rx in new[] { WeightRx, UsedGRx, TotalUsedGRx, CuraWeightRx })
            {
                m = rx.Match(text);
                if (!m.Success) continue;
                var l = NumberList(m.Groups[1].Value);
                if (l.Count > 0 && l.Sum() > 0) { g.Grams = l; break; }
            }
            if (g.Grams.Count == 0) GramsFromLength(text, g);

            m = WipeTowerRx.Match(text);
            double purge;
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out purge) && purge > 0) g.PurgeGrams = purge;

            m = SlotsRx.Match(text);
            if (m.Success)
                foreach (var s in m.Groups[1].Value.Split(','))
                {
                    int n;
                    if (int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n > 0) g.UsedSlots.Add(n);
                }

            // colours (filament_colour, else that slot's extruder_colour), types, profiles
            var fc = GcodeSetting(text, "filament_colour");
            var ec = GcodeSetting(text, "extruder_colour");
            for (int i = 0; i < Math.Max(fc.Count, ec.Count); i++)
            {
                string c = i < fc.Count ? NormColour(fc[i]) : "";
                if (c.Length == 0 && i < ec.Count) c = NormColour(ec[i]);
                g.Colours.Add(c);
            }
            if (g.Colours.All(c => c.Length == 0)) g.Colours.Clear();
            g.Types = GcodeSetting(text, "filament_type");
            g.Profiles = GcodeSetting(text, "filament_settings_id");
            if (g.Types.Count == 0) g.Types = CommaList(GcodeSetting(text, "Filament type"));   // Cura start G-code placeholders
            if (g.Profiles.Count == 0) g.Profiles = CommaList(GcodeSetting(text, "Filament name"));

            // printer
            g.PrinterModel = FirstOf(GcodeSetting(text, "printer_model"));
            g.PrinterSettingsId = FirstOf(GcodeSetting(text, "printer_settings_id"));
            g.Nozzle = FirstOf(GcodeSetting(text, "nozzle_diameter"));
            if (g.PrinterModel.Length == 0) g.PrinterModel = HeaderValue(text, "TARGET_MACHINE.NAME");   // Cura Griffin
            if (g.Nozzle.Length == 0) g.Nozzle = HeaderValue(text, "EXTRUDER_TRAIN.0.NOZZLE.DIAMETER");
            if (g.Nozzle.Length == 0) g.Nozzle = FirstOf(GcodeSetting(text, "Nozzle diameter"));

            g.Picture = GcodeThumbnail(text);
            return g;
        }

        // grams from the length per slot: mm (Bambu/Orca/Prusa), Cura metres, Griffin mm³
        static void GramsFromLength(string text, SlicedGcode g)
        {
            var volumes = new List<double>();   // cm³ per slot
            bool diameterKnown = true;
            var m = LengthMmRx.Match(text);
            List<double> mm = m.Success ? NumberList(m.Groups[1].Value) : new List<double>();
            if (mm.Count == 0)
            {
                m = CuraMetresRx.Match(text);
                if (m.Success) mm = NumberList(Regex.Replace(m.Groups[1].Value, @"(?<=[0-9])\s*m\b", "")).Select(v => v * 1000).ToList();
            }
            if (mm.Count > 0)
            {
                var dia = NumberList(string.Join(",", GcodeSetting(text, "filament_diameter")));
                if (dia.Count == 0)
                {
                    double d = CuraSetting(text, "material_diameter");
                    if (d > 0) dia.Add(d);
                }
                diameterKnown = dia.Count > 0;
                for (int i = 0; i < mm.Count; i++)
                {
                    double d = dia.Count == 0 ? DefaultDiameter : dia[Math.Min(i, dia.Count - 1)];
                    if (d <= 0) d = DefaultDiameter;
                    volumes.Add(mm[i] * Math.PI * d * d / 4 / 1000);
                }
            }
            else
            {
                var byTrain = new SortedDictionary<int, double>();
                foreach (Match v in GriffinVolumeRx.Matches(text))
                {
                    int t; double mm3;
                    if (int.TryParse(v.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out t) && t < 64 &&
                        double.TryParse(v.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out mm3) && mm3 >= 0)
                        byTrain[t] = mm3 / 1000;
                }
                if (byTrain.Count > 0)
                    for (int i = 0; i <= byTrain.Keys.Max(); i++) volumes.Add(byTrain.ContainsKey(i) ? byTrain[i] : 0);
            }
            if (volumes.Count == 0 || volumes.Sum() <= 0) return;
            var dens = NumberList(string.Join(",", GcodeSetting(text, "filament_density")));
            for (int i = 0; i < volumes.Count; i++)
            {
                double rho = dens.Count == 0 ? 0 : dens[Math.Min(i, dens.Count - 1)];
                if (rho <= 0) { rho = DefaultDensity; if (volumes[i] > 0) g.GramsEstimated = true; }
                g.Grams.Add(Math.Round(volumes[i] * rho, 2));
            }
            g.NeedsTail = !diameterKnown && g.Slicer == SlicerCura;
        }

        /// The SliceResult of a parsed G-code: one plate, filaments by slot (grams > 0), printer via the
        /// project_settings keys (ParseProjectPrinter's rules). From another slicer than Bambu Studio, a printer that
        /// isn't a Bambu Lab one is kept by name only (PrinterModel null, not refused); a Bambu Lab one we don't support is
        /// refused as always (UnsupportedPrinter).
        public static SliceResult GcodeResult(SlicedGcode g)
        {
            if (ImpossibleValues(g)) throw new SliceException(ImpossibleValuesText());
            var res = new SliceResult { Seconds = g.Seconds, Grams = g.Grams.Sum(), Plates = 1, SlicerName = g.Slicer ?? "",
                                        GramsEstimated = g.GramsEstimated, PurgeGrams = g.PurgeGrams, Picture = g.Picture,
                                        MissingTime = !g.HasTime, MissingGrams = !g.HasGrams };
            for (int i = 0; i < g.Grams.Count; i++)
            {
                if (g.Grams[i] <= 0) continue;
                int slot = g.UsedSlots.Count == g.Grams.Count ? g.UsedSlots[i] : i + 1;
                res.Filaments.Add(new FilamentUse
                {
                    Slot = slot, Grams = g.Grams[i],
                    Colour = NormColour(SlotAt(g.Colours, slot)), Type = SlotAt(g.Types, slot),
                    Profile = StripSlotTag(SlotAt(g.Profiles, slot))   // a G-code exported from our out.3mf has our " #2" (issue #8 step 3)
                });
            }
            res.Filaments.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            var plate = new PlateInfo { Index = 1, Seconds = res.Seconds, Grams = res.Grams };
            foreach (var f in res.Filaments)
                plate.Filaments.Add(new FilamentUse { Slot = f.Slot, Colour = f.Colour, Type = f.Type, Profile = f.Profile, Grams = f.Grams });
            res.PlateList.Add(plate);
            bool other = res.SlicerName.Length > 0 && res.SlicerName != SlicerBambu;
            // idea 17's "Generic PLA = no filament chosen" rule is about Bambu Studio projects; in other slicers a named
            // profile is the user's pick, so it stays (an empty name still gets the Settings filament)
            if (other) foreach (var f in res.Filaments) f.KeepProfile = f.Profile.Trim().Length > 0;
            var d = new Dictionary<string, object>();
            if (g.PrinterModel.Length > 0) d["printer_model"] = g.PrinterModel;
            if (g.PrinterSettingsId.Length > 0) d["printer_settings_id"] = g.PrinterSettingsId;
            if (g.Nozzle.Length > 0) d["nozzle_diameter"] = g.Nozzle;
            var pp = ProjectPrinterFrom(d, null);
            if (pp != null)
            {
                bool bambu = pp.Name.StartsWith("Bambu Lab", StringComparison.OrdinalIgnoreCase);
                res.PrinterName = pp.Name;
                res.PrinterNozzle = pp.Nozzle;
                if (pp.Key.Length == 0 && other && !bambu)
                {
                    // a Prusa, Voron… : name only, the profile's name reads better ("Original Prusa MK4" over "MK4")
                    res.PrinterModel = null;
                    if (g.PrinterSettingsId.Length > 0) res.PrinterName = NozzleSuffixRx.Replace(g.PrinterSettingsId, "").Trim();
                    res.OtherPrinter = res.PrinterName.Length > 0;
                }
                else
                {
                    res.PrinterModel = pp.Key;
                    res.UnsupportedPrinter = pp.Key.Length == 0;
                }
            }
            return res;
        }

        static string SlotAt(List<string> list, int slot) { return slot >= 1 && slot <= list.Count ? (list[slot - 1] ?? "") : ""; }

        static string FirstOf(List<string> l)
        {
            return l.Count > 0 ? (l[0] ?? "").Split(',')[0].Trim() : "";
        }

        static List<string> CommaList(List<string> l)
        {
            var r = new List<string>();
            foreach (var s in l) foreach (var p in (s ?? "").Split(',')) r.Add(p.Trim().Trim('"'));
            return r;
        }

        /// "1d 2h 3m 4s", "3h 43m 2s", "12m 18s" -> seconds.
        public static double Duration(string s)
        {
            double total = 0;
            foreach (Match p in Regex.Matches(s ?? "", @"(\d+(?:\.\d+)?)\s*([dhms])"))
            {
                double v = double.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture);
                switch (p.Groups[2].Value) { case "d": total += v * 86400; break; case "h": total += v * 3600; break; case "m": total += v * 60; break; default: total += v; break; }
            }
            return total;
        }

        /// "1.5, 2,0.25" or "1.5;2" -> numbers; empty when any part isn't a number >= 0. Without any '.', a comma is read
        /// as a decimal comma where that's clear (the slicers we know always write '.'): "6,38; 4,55" / "6,38, 4,55" (list
        /// split on ';' or ", ") and a lone "3,91282" -> decimals; "1,2,3" (no spaces) stays a list.
        internal static List<double> NumberList(string s)
        {
            var l = new List<double>();
            s = s ?? "";
            string[] parts;
            if (s.IndexOf('.') < 0 && s.IndexOf(',') >= 0)
            {
                if (s.IndexOf(';') >= 0) parts = s.Split(';');
                else if (Regex.IsMatch(s, @",\s")) parts = Regex.Split(s, @",\s+");
                else if (s.IndexOf(',') == s.LastIndexOf(',')) parts = new[] { s };
                else parts = s.Split(',');
                for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Replace(',', '.');
            }
            else parts = s.Split(new[] { ',', ';' });
            foreach (var part in parts)
            {
                string p = part.Trim().Trim('"');
                if (p.Length == 0) continue;
                double v;
                if (double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0 && !double.IsInfinity(v)) l.Add(v);
                else return new List<double>();
            }
            return l;
        }

        // ---------- prime tower (issue #8 step 3) ----------
        // Tower grams aren't in any report (result.json / slice_info only give model and total grams), so we count the
        // extrusion inside the G-code's tower sections: "; FEATURE: Prime tower" (Bambu Studio, OrcaSlicer) up to the next
        // feature line; PrusaSlicer ";TYPE:Wipe tower" and Cura ";TYPE:PRIME-TOWER" best effort. Only printing moves count
        // (G0-G3 with E > 0 and X or Y): that leaves out the filament-change flush into the chute that Bambu writes inside
        // the tower section (no X/Y, and it happens with the tower off too) and retract / unretract pairs. Checked against
        // real tower-on minus tower-off slices within 0,01 g per slot (plans\issue8\step3-plan.md).

        /// Pure: counts the prime tower per slot in a G-code's lines (any slicer; slot = last "T<n>" + 1, n < 16, so
        /// Bambu's T255 / T1000 are skipped). E: M83 relative, M82 absolute (default), "G92 E<v>" resets. Diameter /
        /// density per slot from "; filament_diameter = a,b" / "; filament_density: a,b" (missing = DefaultDiameter /
        /// DefaultDensity). Grams has an entry for every slot seen (tower or header), rounded to 0,001 g. Never null, never throws.
        public static GcodeTower TowerFromGcode(IEnumerable<string> lines)
        {
            var c = new TowerCounter();
            if (lines == null) return c.Result();
            foreach (var raw in lines) c.Feed(raw);
            return c.Result();
        }

        /// TowerFromGcode in steps (optimizer v1.28: the website reads a G-code in pieces): Feed every line in order, then
        /// Result(). Same rules and results as TowerFromGcode (which uses it). Result() can be called any time and doesn't
        /// change the state. Never throws (null lines are skipped).
        public sealed class TowerCounter
        {
            static readonly char[] Spaces = { ' ', '\t' };
            readonly List<double> mm = new List<double>();
            List<double> dia, rho;
            bool relative, inTower;
            double lastE;
            int tool, blocks;

            public void Feed(string raw)
            {
                if (raw == null) return;
                string line = raw.Trim();
                if (line.Length == 0) return;
                if (line[0] == ';')
                {
                    int kind = TowerMarker(line);
                    if (kind == 1) { if (!inTower) blocks++; inTower = true; }
                    else if (kind == 0) inTower = false;
                    else if (dia == null || rho == null)
                    {
                        if (dia == null) dia = HeaderNumbers(line, "filament_diameter");
                        if (rho == null) rho = HeaderNumbers(line, "filament_density");
                    }
                    return;
                }
                // fast path (optimizer v1.28): relative E outside a tower = a move can't change anything (no lastE to
                // keep, nothing counted), so skip it before the Substring / Split; same results, most lines are moves
                if (relative && !inTower && IsMoveLine(line)) return;
                int semi = line.IndexOf(';');
                if (semi >= 0) line = line.Substring(0, semi);
                var parts = line.Split(Spaces, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return;
                string cmd = parts[0].ToUpperInvariant();
                if (cmd == "M83") { relative = true; return; }
                if (cmd == "M82") { relative = false; return; }
                if (cmd.Length >= 2 && cmd[0] == 'T')
                {
                    int n;
                    if (int.TryParse(cmd.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n < 16) tool = n;
                    return;
                }
                if (cmd == "G92")
                {
                    double v;
                    if (parts.Length == 1) lastE = 0;   // plain G92 = all axes 0
                    else if (Axis(parts, 'E', out v)) lastE = v;
                    return;
                }
                if (cmd != "G0" && cmd != "G1" && cmd != "G2" && cmd != "G3" && cmd != "G00" && cmd != "G01" && cmd != "G02" && cmd != "G03") return;
                double e;
                if (!Axis(parts, 'E', out e)) return;
                double delta = relative ? e : e - lastE;
                if (!relative) lastE = e;
                if (!inTower || delta <= 0) return;
                double dummy;
                if (!Axis(parts, 'X', out dummy) && !Axis(parts, 'Y', out dummy)) return;
                while (mm.Count <= tool) mm.Add(0);
                mm[tool] += delta;
            }

            public GcodeTower Result()
            {
                var t = new GcodeTower();
                t.Blocks = blocks;
                int slots = Math.Max(mm.Count, Math.Max(dia == null ? 0 : dia.Count, rho == null ? 0 : rho.Count));
                for (int i = 0; i < slots; i++)
                {
                    double length = i < mm.Count ? mm[i] : 0;
                    double d = dia == null || dia.Count == 0 ? DefaultDiameter : dia[Math.Min(i, dia.Count - 1)];
                    double r = rho == null || rho.Count == 0 ? DefaultDensity : rho[Math.Min(i, rho.Count - 1)];
                    if (d <= 0) d = DefaultDiameter;
                    if (r <= 0) r = DefaultDensity;
                    double g = length * Math.PI * (d / 2) * (d / 2) * r / 1000.0;
                    t.Grams.Add(Math.Round(g, 3));
                }
                return t;
            }
        }

        /// True when a trimmed, non-comment line's command is G0-G3 / G00-G03 (any case), followed by a space, tab, ';' or
        /// the end: the same commands TowerFromGcode's Split path treats as moves ("G10", "G28", "G92", "G010" = false).
        internal static bool IsMoveLine(string line)
        {
            if (line == null || line.Length < 2 || (line[0] != 'G' && line[0] != 'g')) return false;
            int i = 1;
            if (line[i] == '0' && i + 1 < line.Length && line[i + 1] >= '0' && line[i + 1] <= '9') i++;
            char c = line[i];
            if (c < '0' || c > '3') return false;
            i++;
            return i == line.Length || line[i] == ' ' || line[i] == '\t' || line[i] == ';';
        }

        /// 1 = a prime / wipe tower section starts, 0 = another feature starts, -1 = not a feature line.
        static int TowerMarker(string line)
        {
            string s = line.TrimStart(';').Trim();
            string value;
            if (s.StartsWith("FEATURE:", StringComparison.OrdinalIgnoreCase)) value = s.Substring(8).Trim();
            else if (s.StartsWith("TYPE:", StringComparison.OrdinalIgnoreCase)) value = s.Substring(5).Trim();
            else return -1;
            return value.Equals("Prime tower", StringComparison.OrdinalIgnoreCase) || value.Equals("Wipe tower", StringComparison.OrdinalIgnoreCase)
                || value.Equals("PRIME-TOWER", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        /// "; key = 1.75,1.75" / "; key: 1.75;1.75" -> the numbers; null when the line isn't that key or has no numbers.
        static List<double> HeaderNumbers(string commentLine, string key)
        {
            string s = commentLine.TrimStart(';').TrimStart();
            if (!s.StartsWith(key, StringComparison.Ordinal)) return null;
            s = s.Substring(key.Length).TrimStart();
            if (s.Length == 0 || (s[0] != '=' && s[0] != ':')) return null;
            var l = NumberList(s.Substring(1));
            return l.Count > 0 ? l : null;
        }

        /// The number after an axis letter among a G-code line's words ("E0.8171", "X.5"); false when missing / not a number.
        static bool Axis(string[] parts, char axis, out double v)
        {
            v = 0;
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length < 2 || char.ToUpperInvariant(p[0]) != axis) continue;
                return double.TryParse(p.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                       && !double.IsNaN(v) && !double.IsInfinity(v);
            }
            return false;
        }

        /// Adds one plate's tower count into a total (slot by slot). Null = nothing.
        public static void AddTower(GcodeTower total, GcodeTower plate)
        {
            if (total == null || plate == null) return;
            for (int i = 0; i < plate.Grams.Count; i++)
            {
                if (i < total.Grams.Count) total.Grams[i] += plate.Grams[i];
                else total.Grams.Add(plate.Grams[i]);
            }
            total.Blocks += plate.Blocks;
        }

        /// Puts a tower count into a result: TowerGrams (sum) and each FilamentUse.TowerGrams (slot's grams, 0 when none).
        /// Unknown (all stay -1) when the count is null, or found no tower in a G-code from a slicer whose marker we don't
        /// know (Cura, other). A PrusaSlicer file whose blocks weren't found but whose header gave the wipe tower total
        /// (PurgeGrams) gets that total, slots unknown. Bambu Studio / OrcaSlicer / PrusaSlicer without a tower = 0.
        public static void ApplyTower(SliceResult res, GcodeTower tower)
        {
            if (res == null || tower == null) return;
            string slicer = res.SlicerName ?? "";
            bool knownMarker = slicer.Length == 0 || slicer == SlicerBambu || slicer == SlicerOrca || slicer == SlicerPrusa;
            if (tower.Blocks == 0 && slicer == SlicerPrusa && res.PurgeGrams > 0)
            {
                res.TowerGrams = res.PurgeGrams;
                return;
            }
            if (tower.Blocks == 0 && !knownMarker) return;
            res.TowerGrams = Math.Round(tower.Total, 3);
            foreach (var f in res.Filaments)
                if (f != null) f.TowerGrams = f.Slot >= 1 && f.Slot <= tower.Grams.Count ? tower.Grams[f.Slot - 1] : 0;
            // per plate only when there's one plate (a count over several plates' G-code can't be split back)
            if (res.PlateList.Count == 1 && res.PlateList[0] != null)
                foreach (var f in res.PlateList[0].Filaments)
                    if (f != null) f.TowerGrams = f.Slot >= 1 && f.Slot <= tower.Grams.Count ? tower.Grams[f.Slot - 1] : 0;
        }

        /// Issue #8 step 3: the CLI turns the prime tower off for a Bambu .3mf when every loaded filament profile has the
        /// same name (BambuStudio.cpp disable_wipe_tower_after_mapping), e.g. one PLA in two colours. So each slot file
        /// we write gets its own name: the preset + " #<slot>" (after "@BBL", so ShortName / FilamentBaseName drop it).
        public static string SlotNameTag(int slot)
        {
            return " #" + slot.ToString(CultureInfo.InvariantCulture);
        }

        /// The name we give a slot file: name + SlotNameTag when it has an "@" part ("Bambu PLA Basic @BBL A1 #2"), else
        /// name + " @#<slot>" ("Acme PLA @#2"), so ShortName / FilamentBaseName drop it either way. null = "".
        public static string TaggedName(string name, int slot)
        {
            name = name ?? "";
            return name.IndexOf('@') >= 0 ? name + SlotNameTag(slot) : name + " @" + SlotNameTag(slot).TrimStart();
        }

        /// Pure: removes our tag again ("Bambu PLA Basic @BBL A1 #2" -> "Bambu PLA Basic @BBL A1", "Acme PLA @#2" ->
        /// "Acme PLA"). Only a " #<1-2 digits>" ending after an "@" part, or a " @#<1-2 digits>" ending; anything else
        /// unchanged (null = "").
        public static string StripSlotTag(string profile)
        {
            string s = profile ?? "";
            if (Regex.IsMatch(s, @"\s@#\d{1,2}$")) return Regex.Replace(s, @"\s@#\d{1,2}$", "");
            return Regex.Replace(s, @"(@[^#]*?)\s#\d{1,2}$", "$1");
        }

        /// "; key = a;b;c", "; key = "a";"b"" or "; key: a,b" (Bambu header) -> per-slot strings (quotes removed, trimmed). Empty when missing.
        public static List<string> GcodeSetting(string text, string key)
        {
            var l = new List<string>();
            var m = Regex.Match(text ?? "", @"^;\s*" + Regex.Escape(key) + @"\s*[:=]\s*(.*)$", RegexOptions.Multiline);
            if (!m.Success) return l;
            string v = m.Groups[1].Value.Trim();
            if (v.Length == 0) return l;
            if (v.StartsWith("\""))
                foreach (Match q in Regex.Matches(v, "\"((?:[^\"\\\\]|\\\\.)*)\"")) l.Add(q.Groups[1].Value.Replace("\\\"", "\"").Trim());
            else
                foreach (var part in v.Split(';')) l.Add(part.Trim());
            return l;
        }

        // Cura Griffin header ";KEY:value"
        static string HeaderValue(string text, string key)
        {
            var m = Regex.Match(text, @"^;" + Regex.Escape(key) + @":\s*(.*?)\s*$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : "";
        }

        // a number from Cura's ";SETTING_3 …" lines at the end (one JSON text cut into lines), 0 = not found
        static double CuraSetting(string text, string key)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Match l in Regex.Matches(text, @"^;SETTING_3 (.*)$", RegexOptions.Multiline)) sb.Append(l.Groups[1].Value);
            var m = Regex.Match(sb.ToString(), @"(?:^|\\n)" + Regex.Escape(key) + @"\s*=\s*([0-9.]+)");
            double v;
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        /// The biggest PNG / JPEG thumbnail in a "; thumbnail begin" … "; thumbnail end" block (QOI skipped), or null.
        public static byte[] GcodeThumbnail(string text)
        {
            byte[] best = null;
            foreach (Match m in GcodeThumbRx.Matches(text ?? ""))
            {
                var sb = new System.Text.StringBuilder();
                foreach (var line in m.Groups[1].Value.Split('\n')) sb.Append(line.TrimStart(';').Trim());
                try
                {
                    var b = Convert.FromBase64String(sb.ToString());
                    if (ImageType(b) != null && (best == null || b.Length > best.Length) && b.Length <= MaxGcodePictureBytes) best = b;
                }
                catch (FormatException) { }
            }
            return best;
        }

        /// "image/png" / "image/jpeg" by the first bytes, else null.
        public static string ImageType(byte[] b)
        {
            if (b == null || b.Length < 8) return null;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            return null;
        }
    }

    /// slice_info.config text that isn't well-formed XML (SliceXml.Parse).
    public class SliceXmlException : FormatException
    {
        public SliceXmlException(string message) : base(message) { }
    }

    /// A small, strict XML reader for slice_info.config (elements and attributes only), so the website needs no
    /// System.Xml (~160 KB smaller download). Follows XmlDocument.LoadXml: the same documents are refused (BOM character,
    /// late XML declaration, bad names / entities / characters, duplicate attributes, undeclared prefixes, mismatched or
    /// missing tags, text outside the root), attribute values decoded the same way (entities; white space kept as written), and
    /// Element.Is / Children match like XPath (no prefix, no default namespace). DOCTYPE: skipped, its internal text
    /// entities are expanded (attribute defaults from a DTD are not added; Bambu files have no DTD).
    /// Checked against XmlDocument by tests\PricingTests.cs SliceXmlTests.
    public static class SliceXml
    {
        public sealed class Element
        {
            public string Name;
            internal bool NoNamespace;   // no prefix and no default namespace in scope
            internal readonly List<KeyValuePair<string, string>> Attrs = new List<KeyValuePair<string, string>>();
            internal readonly List<Element> Kids = new List<Element>();

            /// The attribute's value by its full name, null when missing (= XmlElement.Attributes[name]).
            public string Attr(string name)
            {
                foreach (var a in Attrs) if (a.Key == name) return a.Value;
                return null;
            }

            /// XPath name test: an element with this name and no namespace.
            public bool Is(string name) { return NoNamespace && Name == name; }

            /// The child elements matching XPath "name", in document order.
            public IEnumerable<Element> Children(string name) { return Kids.Where(k => k.Is(name)); }
        }

        /// The root element; SliceXmlException when the text isn't well-formed.
        public static Element Parse(string xml)
        {
            return new Reader(xml ?? "").Document();
        }

        sealed class Reader
        {
            readonly string s;
            int p;
            readonly Dictionary<string, string> entities = new Dictionary<string, string>();
            readonly List<Dictionary<string, string>> scopes = new List<Dictionary<string, string>>();   // prefix -> uri ("" = default)

            static readonly Regex XmlDecl = new Regex(
                "\\A<\\?xml[ \\t\\r\\n]+version[ \\t\\r\\n]*=[ \\t\\r\\n]*([\"'])1\\.[0-9]+\\1" +
                "(?:[ \\t\\r\\n]+encoding[ \\t\\r\\n]*=[ \\t\\r\\n]*([\"'])[A-Za-z][A-Za-z0-9._\\-]*\\2)?" +
                "(?:[ \\t\\r\\n]+standalone[ \\t\\r\\n]*=[ \\t\\r\\n]*([\"'])(?:yes|no)\\3)?[ \\t\\r\\n]*\\?>");
            static readonly Regex EntityDecl = new Regex("<!ENTITY[ \\t\\r\\n]+([A-Za-z_][A-Za-z0-9._\\-]*)[ \\t\\r\\n]+([\"'])([^\"'<&%]*)\\2[ \\t\\r\\n]*>");

            public Reader(string text) { s = text; }

            static SliceXmlException Bad(string what) { return new SliceXmlException("Not valid XML: " + what + "."); }

            bool At(string t) { return string.CompareOrdinal(s, p, t, 0, t.Length) == 0; }
            static bool Space(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }
            void SkipSpace() { while (p < s.Length && Space(s[p])) p++; }

            static bool NameStart(char c) { return char.IsLetter(c) || c == '_' || c == ':'; }
            static bool NameChar(char c)
            {
                return NameStart(c) || char.IsDigit(c) || c == '-' || c == '.' || c == '·'
                    || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark
                    || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpacingCombiningMark
                    || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.LetterNumber;
            }

            static bool XmlChar(char c)
            {
                if (c < 0x20) return c == '\t' || c == '\n' || c == '\r';
                return c != '￾' && c != '￿';
            }

            string Name()
            {
                if (p >= s.Length || !NameStart(s[p])) throw Bad("a name is expected at " + p);
                int start = p;
                while (p < s.Length && NameChar(s[p])) p++;
                string n = s.Substring(start, p - start);
                int colon = n.IndexOf(':');
                if (colon == 0 || colon == n.Length - 1 || (colon > 0 && n.IndexOf(':', colon + 1) >= 0)) throw Bad("bad name " + n);
                return n;
            }

            public Element Document()
            {
                if (s.Length > 0 && s[0] == '﻿') throw Bad("data at the root level");
                if (At("<?xml") && s.Length > 5 && (Space(s[5]) || s[5] == '?'))
                {
                    var m = XmlDecl.Match(s);
                    if (!m.Success) throw Bad("XML declaration");
                    p = m.Length;
                }
                Element root = null;
                bool doctype = false;
                while (true)
                {
                    SkipSpace();
                    if (p >= s.Length) break;
                    if (At("<!--")) Comment();
                    else if (At("<?")) Pi();
                    else if (At("<!DOCTYPE"))
                    {
                        if (doctype || root != null) throw Bad("DOCTYPE");
                        doctype = true;
                        Doctype();
                    }
                    else if (s[p] == '<' && root == null) root = ElementAt();
                    else throw Bad("data at the root level at " + p);
                }
                if (root == null) throw Bad("root element is missing");
                return root;
            }

            void Comment()
            {
                p += 4;
                int end = s.IndexOf("--", p, StringComparison.Ordinal);
                if (end < 0) throw Bad("unclosed comment");
                if (end + 2 >= s.Length || s[end + 2] != '>') throw Bad("'--' in a comment");
                for (int i = p; i < end; i++) if (!XmlChar(s[i])) throw Bad("invalid character");
                p = end + 3;
            }

            void Pi()
            {
                p += 2;
                string target = Name();
                if (string.Equals(target, "xml", StringComparison.OrdinalIgnoreCase)) throw Bad("unexpected XML declaration");
                if (target.IndexOf(':') >= 0) throw Bad("colon in a processing instruction");
                int end = s.IndexOf("?>", p, StringComparison.Ordinal);
                if (end < 0) throw Bad("unclosed processing instruction");
                if (end > p && !Space(s[p])) throw Bad("processing instruction");
                p = end + 2;
            }

            void Doctype()
            {
                p += 9;
                if (p >= s.Length || !Space(s[p])) throw Bad("DOCTYPE");
                char quote = '\0';
                while (p < s.Length)
                {
                    char c = s[p];
                    if (quote != '\0') { if (c == quote) quote = '\0'; p++; continue; }
                    if (c == '"' || c == '\'') { quote = c; p++; continue; }
                    if (c == '[')
                    {
                        int end = s.IndexOf("]", p, StringComparison.Ordinal);
                        if (end < 0) throw Bad("unclosed DOCTYPE");
                        foreach (Match m in EntityDecl.Matches(s.Substring(p + 1, end - p - 1)))
                            if (!entities.ContainsKey(m.Groups[1].Value)) entities[m.Groups[1].Value] = m.Groups[3].Value;
                        p = end + 1;
                        continue;
                    }
                    if (c == '>') { p++; return; }
                    p++;
                }
                throw Bad("unclosed DOCTYPE");
            }

            string Lookup(string prefix)
            {
                if (prefix == "xml") return "http://www.w3.org/XML/1998/namespace";
                if (prefix == "xmlns") return "http://www.w3.org/2000/xmlns/";
                for (int i = scopes.Count - 1; i >= 0; i--)
                {
                    string uri;
                    if (scopes[i].TryGetValue(prefix, out uri)) return uri;
                }
                return null;
            }

            Element ElementAt()
            {
                p++;   // '<'
                var e = new Element { Name = Name() };
                var scope = new Dictionary<string, string>();
                while (true)
                {
                    int before = p;
                    SkipSpace();
                    if (p >= s.Length) throw Bad("unexpected end of file");
                    if (s[p] == '/' || s[p] == '>') break;
                    if (p == before) throw Bad("white space expected at " + p);
                    string an = Name();
                    SkipSpace();
                    if (p >= s.Length || s[p] != '=') throw Bad("'=' expected at " + p);
                    p++;
                    SkipSpace();
                    string v = AttrValue();
                    if (e.Attr(an) != null) throw Bad("duplicate attribute " + an);
                    e.Attrs.Add(new KeyValuePair<string, string>(an, v));
                    if (an == "xmlns") scope[""] = v;
                    else if (an.StartsWith("xmlns:", StringComparison.Ordinal))
                    {
                        if (v.Length == 0) throw Bad("empty namespace for a prefix");
                        scope[an.Substring(6)] = v;
                    }
                }
                scopes.Add(scope);
                int colon = e.Name.IndexOf(':');
                if (colon > 0 && Lookup(e.Name.Substring(0, colon)) == null) throw Bad("undeclared prefix in " + e.Name);
                foreach (var a in e.Attrs)
                {
                    int c = a.Key.IndexOf(':');
                    if (c > 0 && a.Key.Substring(0, c) != "xmlns" && Lookup(a.Key.Substring(0, c)) == null) throw Bad("undeclared prefix in " + a.Key);
                }
                e.NoNamespace = colon < 0 && string.IsNullOrEmpty(Lookup(""));
                if (s[p] == '/')
                {
                    p++;
                    if (p >= s.Length || s[p] != '>') throw Bad("'>' expected at " + p);
                    p++;
                    scopes.RemoveAt(scopes.Count - 1);
                    return e;
                }
                p++;   // '>'
                Content(e);
                scopes.RemoveAt(scopes.Count - 1);
                return e;
            }

            void Content(Element e)
            {
                while (true)
                {
                    if (p >= s.Length) throw Bad("unexpected end of file, " + e.Name + " not closed");
                    char c = s[p];
                    if (c == '<')
                    {
                        if (At("</"))
                        {
                            p += 2;
                            string n = Name();
                            SkipSpace();
                            if (n != e.Name) throw Bad("end tag " + n + " doesn't match " + e.Name);
                            if (p >= s.Length || s[p] != '>') throw Bad("'>' expected at " + p);
                            p++;
                            return;
                        }
                        if (At("<!--")) { Comment(); continue; }
                        if (At("<![CDATA["))
                        {
                            int end = s.IndexOf("]]>", p + 9, StringComparison.Ordinal);
                            if (end < 0) throw Bad("unclosed CDATA");
                            for (int i = p + 9; i < end; i++) if (!XmlChar(s[i])) throw Bad("invalid character");
                            p = end + 3;
                            continue;
                        }
                        if (At("<?")) { Pi(); continue; }
                        if (At("<!")) throw Bad("unexpected markup at " + p);
                        e.Kids.Add(ElementAt());
                        continue;
                    }
                    if (c == '&') { Entity(); continue; }
                    if (c == ']' && At("]]>")) throw Bad("']]>' in text");
                    if (!XmlChar(c)) throw Bad("invalid character at " + p);
                    p++;
                }
            }

            string AttrValue()
            {
                if (p >= s.Length || (s[p] != '"' && s[p] != '\'')) throw Bad("quoted value expected at " + p);
                char q = s[p++];
                var sb = new System.Text.StringBuilder();
                while (true)
                {
                    if (p >= s.Length) throw Bad("unclosed attribute value");
                    char c = s[p];
                    if (c == q) { p++; return sb.ToString(); }
                    if (c == '<') throw Bad("'<' in an attribute value");
                    if (c == '&') { sb.Append(Entity()); continue; }
                    if (!XmlChar(c)) throw Bad("invalid character at " + p);
                    sb.Append(c);   // as written: LoadXml doesn't normalize white space or line ends here
                    p++;
                }
            }

            // &name; or &#n; / &#xh; -> its text
            string Entity()
            {
                int end = s.IndexOf(';', p);
                if (end < 0) throw Bad("unclosed entity reference");
                string body = s.Substring(p + 1, end - p - 1);
                p = end + 1;
                if (body.StartsWith("#", StringComparison.Ordinal))
                {
                    bool hex = body.StartsWith("#x", StringComparison.Ordinal);
                    string digits = body.Substring(hex ? 2 : 1);
                    long code = 0;
                    if (digits.Length == 0) throw Bad("bad character reference");
                    foreach (char d in digits)
                    {
                        int offset = d >= '0' && d <= '9' ? '0' : hex && d >= 'a' && d <= 'f' ? 'a' - 10 : hex && d >= 'A' && d <= 'F' ? 'A' - 10 : -1;
                        if (offset < 0) throw Bad("bad character reference");
                        long t = code * (hex ? 16 : 10) + d;   // LoadXml's own overflow check, character code included
                        if (t > int.MaxValue) throw Bad("invalid character reference");
                        code = t - offset;
                    }
                    // like LoadXml, a reference may name any number (&#0;, a lone surrogate, above U+10FFFF): only literal
                    // characters are checked; above U+FFFF it becomes the two halves LoadXml makes
                    if (code <= 0xFFFF) return ((char)code).ToString();
                    long v = code - 0x10000;
                    return new string(new[] { unchecked((char)(0xD800 + (v >> 10))), unchecked((char)(0xDC00 + (v & 0x3FF))) });
                }
                switch (body)
                {
                    case "lt": return "<";
                    case "gt": return ">";
                    case "amp": return "&";
                    case "quot": return "\"";
                    case "apos": return "'";
                }
                string text;
                if (body.Length > 0 && entities.TryGetValue(body, out text)) return text;
                throw Bad("undeclared entity " + body);
            }
        }
    }
}
