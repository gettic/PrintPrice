using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PrintPrice.Web
{
    /// What reading a dropped / chosen file gave.
    public sealed class FileJob
    {
        public string FileName = "";
        public SliceResult Result;     // null = no values (see Error / Unsliced)
        public string Error;           // text for the user (unsupported printer, not a model file, couldn't read)
        public bool Unsliced;          // a model file that still needs slicing in Bambu Studio
        public string PictureType;     // "image/png" / "image/jpeg", null = no picture
        public bool IsGcode;
    }

    /// Reads a sliced Bambu .gcode.3mf or Bambu G-code in the browser, by ranges only. The parsing itself is the desktop's
    /// (src\SliceInfo.cs: ParseSliceInfo, ParseFilamentSlots, ParseProjectPrinter, PickPictureEntry, ApplyDefaultFilament).
    public static class SlicedFile
    {
        public const long MaxSettingsBytes = 64L * 1048576;   // as Slicer.ProjectSettings
        public const long MaxPictureBytes = 4L * 1048576;     // as Slicer.FromProject
        public const int GcodeHeadBytes = 1024 * 1024;        // header + thumbnails + config block (~30 KB without pictures)
        public const int GcodeTailBytes = 256 * 1024;         // older / other layouts put the numbers at the end

        static readonly string[] ModelExtensions = { ".stl", ".3mf", ".obj", ".step", ".stp", ".amf" };

        public static async Task<FileJob> Read(IRangeSource src, string fileName, string defaultFilament)
        {
            var job = new FileJob { FileName = fileName ?? "" };
            string lower = job.FileName.ToLowerInvariant();
            try
            {
                if (lower.EndsWith(".gcode") || lower.EndsWith(".gco") || lower.EndsWith(".gc"))
                {
                    job.IsGcode = true;
                    await ReadGcode(src, job);
                }
                else if (lower.EndsWith(".3mf")) await Read3mf(src, job);
                else if (ModelExtensions.Any(lower.EndsWith)) job.Unsliced = true;
                else job.Error = L.S("That isn't a sliced file. Use a .gcode.3mf or a Bambu G-code file.",
                                     "Det er ikke en slicet fil. Brug en .gcode.3mf eller en Bambu G-code-fil.");
            }
            catch (SliceException ex) { job.Result = null; job.Error = ex.Message; }
            catch (Exception) { job.Result = null; job.Error = L.S("The file couldn't be read.", "Filen kunne ikke læses."); }
            if (job.Result != null)
            {
                job.Result.FromSlicedFile = true;
                if (job.Result.UnsupportedPrinter)
                {
                    job.Error = BambuSlicer.UnsupportedPrinterText(job.Result.PrinterName);
                    job.Result = null;
                }
                else BambuSlicer.ApplyDefaultFilament(job.Result.Filaments, defaultFilament);
            }
            return job;
        }

        // ---------- .gcode.3mf (same steps as BambuSlicer.ReadSliceInfo) ----------

        static async Task Read3mf(IRangeSource src, FileJob job)
        {
            var zip = await ZipRanges.Open(src);
            if (zip == null) throw new FormatException("not a zip");
            string xml = zip.Find("Metadata/slice_info.config") == null ? null : await zip.ReadText("Metadata/slice_info.config", MaxSettingsBytes);
            bool hasGcode = zip.Entries.Any(e => e.Name.StartsWith("Metadata/plate_", StringComparison.OrdinalIgnoreCase)
                                              && e.Name.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase));
            SliceResult res = null;
            if (xml != null && hasGcode)
            {
                try { res = BambuSlicer.ParseSliceInfo(xml); } catch (System.Xml.XmlException) { res = null; }
            }
            if (res == null) { job.Unsliced = true; return; }
            string settings = await zip.ReadText("Metadata/project_settings.config", MaxSettingsBytes);
            var profiles = BambuSlicer.ParseFilamentSlots(settings);
            foreach (var f in res.Filaments)
                if (f.Slot >= 1 && f.Slot <= profiles.Count) f.Profile = profiles[f.Slot - 1];
            SetPrinter(res, BambuSlicer.ParseProjectPrinter(settings, xml));
            string pic = BambuSlicer.PickPictureEntry(zip.Names);
            if (pic != null)
            {
                var bytes = await zip.ReadEntry(pic, MaxPictureBytes);
                job.PictureType = ImageType(bytes);
                if (job.PictureType != null) res.Picture = bytes;
            }
            job.Result = res;
        }

        static void SetPrinter(SliceResult res, ProjectPrinter pp)
        {
            if (pp == null) return;
            res.PrinterModel = pp.Key;
            res.PrinterName = pp.Name;
            res.PrinterNozzle = pp.Nozzle;
            res.UnsupportedPrinter = pp.Key.Length == 0;
        }

        // ---------- Bambu G-code: first 1 MB and last 256 KB only ----------

        static async Task ReadGcode(IRangeSource src, FileJob job)
        {
            long len = src.Length;
            byte[] head = await src.Read(0, (int)Math.Min(len, GcodeHeadBytes));
            string text = Encoding.UTF8.GetString(head);
            var info = ParseGcode(text);
            if (!info.Complete && len > head.Length)
            {
                long start = Math.Max(head.Length, len - GcodeTailBytes);
                byte[] tail = await src.Read(start, (int)(len - start));
                info = ParseGcode(text + "\n" + Encoding.UTF8.GetString(tail));
            }
            if (info.Seconds <= 0 || info.Grams.Count == 0 || info.Grams.Sum() <= 0)
            {
                job.Error = L.S("Couldn't find the print time and weight in this G-code. Is it from Bambu Studio? You can type them below.",
                                "Kunne ikke finde printtid og vægt i denne G-code. Er den fra Bambu Studio? Du kan skrive dem nedenfor.");
                return;
            }
            job.Result = info.ToResult();
            if (info.Picture != null) { job.Result.Picture = info.Picture; job.PictureType = ImageType(info.Picture); }
        }

        /// What the G-code text says (pure; tests call it).
        public sealed class GcodeInfo
        {
            public double Seconds;
            public List<double> Grams = new List<double>();       // as listed in "total filament weight [g]"
            public List<int> UsedSlots = new List<int>();         // "; filament: 1,2,3" (1-based), empty = 1..n
            public List<string> Colours = new List<string>(), Types = new List<string>(), Profiles = new List<string>();
            public string PrinterModel = "", PrinterSettingsId = "", Nozzle = "";
            public byte[] Picture;
            public bool Complete { get { return Seconds > 0 && Grams.Count > 0 && Colours.Count > 0 && (PrinterModel.Length > 0 || PrinterSettingsId.Length > 0); } }

            public SliceResult ToResult()
            {
                var res = new SliceResult { Seconds = Seconds, Grams = Grams.Sum(), Plates = 1 };
                for (int i = 0; i < Grams.Count; i++)
                {
                    if (Grams[i] <= 0) continue;
                    int slot = UsedSlots.Count == Grams.Count ? UsedSlots[i] : i + 1;
                    res.Filaments.Add(new FilamentUse
                    {
                        Slot = slot, Grams = Grams[i],
                        Colour = Norm(At(Colours, slot)), Type = At(Types, slot), Profile = At(Profiles, slot)
                    });
                }
                res.Filaments.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                // the printer: the desktop's ParseProjectPrinter on the same keys as project_settings.config
                var d = new Dictionary<string, object>();
                if (PrinterModel.Length > 0) d["printer_model"] = PrinterModel;
                if (PrinterSettingsId.Length > 0) d["printer_settings_id"] = PrinterSettingsId;
                if (Nozzle.Length > 0) d["nozzle_diameter"] = new[] { Nozzle };
                SetPrinter(res, BambuSlicer.ParseProjectPrinter(Json.Serialize(d), null));
                return res;
            }

            static string At(List<string> list, int slot) { return slot >= 1 && slot <= list.Count ? list[slot - 1] : ""; }
        }

        static readonly Regex TimeRx = new Regex(@"total estimated time\s*[:=]\s*([0-9dhms ]+)", RegexOptions.IgnoreCase);
        static readonly Regex WeightRx = new Regex(@"^;\s*total filament weight \[g\]\s*[:=]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex UsedRx = new Regex(@"^;\s*filament used \[g\]\s*=\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        static readonly Regex SlotsRx = new Regex(@"^;\s*filament:\s*([0-9, ]+)$", RegexOptions.Multiline);

        public static GcodeInfo ParseGcode(string text)
        {
            var g = new GcodeInfo();
            text = (text ?? "").Replace("\r", "");
            var m = TimeRx.Match(text);
            if (m.Success) g.Seconds = Duration(m.Groups[1].Value);
            m = WeightRx.Match(text);
            if (!m.Success) m = UsedRx.Match(text);
            if (m.Success) g.Grams = Numbers(m.Groups[1].Value);
            m = SlotsRx.Match(text);
            if (m.Success)
                foreach (var s in m.Groups[1].Value.Split(','))
                {
                    int n;
                    if (int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n > 0) g.UsedSlots.Add(n);
                }
            g.Colours = Setting(text, "filament_colour");
            g.Types = Setting(text, "filament_type");
            g.Profiles = Setting(text, "filament_settings_id");
            g.PrinterModel = First(Setting(text, "printer_model"));
            g.PrinterSettingsId = First(Setting(text, "printer_settings_id"));
            g.Nozzle = First(Setting(text, "nozzle_diameter"));
            g.Picture = Thumbnail(text);
            return g;
        }

        static string First(List<string> l) { return l.Count > 0 ? l[0] : ""; }

        /// "1d 2h 3m 4s", "3h 43m 2s", "12m 18s" -> seconds.
        public static double Duration(string s)
        {
            double total = 0;
            foreach (Match p in Regex.Matches(s ?? "", @"(\d+)\s*([dhms])"))
            {
                double v = double.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture);
                switch (p.Groups[2].Value) { case "d": total += v * 86400; break; case "h": total += v * 3600; break; case "m": total += v * 60; break; default: total += v; break; }
            }
            return total;
        }

        static List<double> Numbers(string s)
        {
            var l = new List<double>();
            foreach (var part in s.Split(','))
            {
                double v;
                if (double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0) l.Add(v);
                else return new List<double>();
            }
            return l;
        }

        /// "; key = a;b;c" or "; key = "a";"b"" -> per-slot strings (quotes removed). Empty when missing.
        public static List<string> Setting(string text, string key)
        {
            var l = new List<string>();
            var m = Regex.Match(text, @"^;\s*" + Regex.Escape(key) + @"\s*=\s*(.*)$", RegexOptions.Multiline);
            if (!m.Success) return l;
            string v = m.Groups[1].Value.Trim();
            if (v.Length == 0) return l;
            if (v.StartsWith("\""))
                foreach (Match q in Regex.Matches(v, "\"((?:[^\"\\\\]|\\\\.)*)\"")) l.Add(q.Groups[1].Value.Replace("\\\"", "\"").Trim());
            else
                foreach (var part in v.Split(';')) l.Add(part.Trim());
            return l;
        }

        /// The biggest PNG / JPEG thumbnail in a "; thumbnail begin" … "; thumbnail end" block, or null.
        public static byte[] Thumbnail(string text)
        {
            byte[] best = null;
            foreach (Match m in Regex.Matches(text, @"^;\s*thumbnail(?:_PNG|_JPG|_QOI)?\s+begin[^\n]*\n((?:;[^\n]*\n)+?);\s*thumbnail(?:_PNG|_JPG|_QOI)?\s+end",
                                              RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                var sb = new StringBuilder();
                foreach (var line in m.Groups[1].Value.Split('\n')) sb.Append(line.TrimStart(';').Trim());
                try
                {
                    var b = Convert.FromBase64String(sb.ToString());
                    if (ImageType(b) != null && (best == null || b.Length > best.Length) && b.Length <= MaxPictureBytes) best = b;
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

        // "#rrggbb" / "#RRGGBBAA" -> "#RRGGBB" (as SliceInfo's NormColour), else ""
        static string Norm(string c)
        {
            c = (c ?? "").Trim();
            if (!c.StartsWith("#")) c = "#" + c;
            if (c.Length < 7 || !Regex.IsMatch(c.Substring(1, 6), "^[0-9A-Fa-f]{6}$")) return "";
            return c.Substring(0, 7).ToUpperInvariant();
        }
    }
}
