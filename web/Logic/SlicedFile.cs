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

    /// Reads a sliced .gcode.3mf or G-code (Bambu Studio, OrcaSlicer, PrusaSlicer, Cura) in the browser, by ranges only. The parsing itself is the desktop's
    /// (src\SliceInfo.cs: ParseSliceInfo, ParseSettings + SlotStrings + ProjectPrinterFrom, PickPictureEntry, ApplyDefaultFilament).
    public static class SlicedFile
    {
        public const long MaxSettingsBytes = 64L * 1048576;   // as Slicer.ProjectSettings
        public const long MaxPictureBytes = 4L * 1048576;     // as Slicer.FromProject
        public const int GcodeHeadBytes = BambuSlicer.GcodeHeadBytes;   // header + thumbnails + config block
        public const int GcodeTailBytes = BambuSlicer.GcodeTailBytes;   // other layouts put the numbers at the end

        static readonly string[] ModelExtensions = { ".stl", ".3mf", ".obj", ".step", ".stp", ".amf" };

        public static async Task<FileJob> Read(IRangeSource src, string fileName, string defaultFilament)
        {
            var job = new FileJob { FileName = fileName ?? "" };
            string lower = job.FileName.ToLowerInvariant();
            try
            {
                if (BambuSlicer.IsGcodeFile(lower))
                {
                    job.IsGcode = true;
                    await ReadGcode(src, job);
                }
                else if (lower.EndsWith(".3mf")) await Read3mf(src, job);
                else if (ModelExtensions.Any(lower.EndsWith)) job.Unsliced = true;
                else job.Error = L.S("That isn't a sliced file. Use a .gcode.3mf or a G-code file (Bambu Studio, OrcaSlicer, PrusaSlicer or Cura).",
                                     "Det er ikke en slicet fil. Brug en .gcode.3mf eller en G-code-fil (Bambu Studio, OrcaSlicer, PrusaSlicer eller Cura).");
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
                try { res = BambuSlicer.ParseSliceInfo(xml); } catch (SliceXmlException) { res = null; }
            }
            if (res == null) { job.Unsliced = true; return; }
            string settings = await zip.ReadText("Metadata/project_settings.config", MaxSettingsBytes);
            var parsed = BambuSlicer.ParseSettings(settings);   // once for the slots and the printer (as ReadSliceInfo)
            var profiles = BambuSlicer.SlotStrings(parsed, "filament_settings_id");   // = ParseFilamentSlots
            foreach (var f in res.Filaments)
                if (f.Slot >= 1 && f.Slot <= profiles.Count) f.Profile = profiles[f.Slot - 1];
            SetPrinter(res, BambuSlicer.ProjectPrinterFrom(parsed, xml));
            string pic = BambuSlicer.PickPictureEntry(zip.Names);
            if (pic != null)
            {
                var bytes = await zip.ReadEntry(pic, MaxPictureBytes);
                job.PictureType = BambuSlicer.ImageType(bytes);
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

        // ---------- G-code (Bambu Studio, OrcaSlicer, PrusaSlicer, Cura): first 1 MB and last 256 KB only ----------

        static async Task ReadGcode(IRangeSource src, FileJob job)
        {
            long len = src.Length;
            byte[] head = await src.Read(0, (int)Math.Min(len, BambuSlicer.GcodeHeadBytes));
            if (BambuSlicer.IsBinaryGcode(job.FileName, head)) { job.Error = BambuSlicer.BinaryGcodeText(); return; }
            string text = Encoding.UTF8.GetString(head);
            var info = BambuSlicer.ParseGcode(text);
            if (!info.Complete && len > head.Length)
            {
                long start = Math.Max(head.Length, len - BambuSlicer.GcodeTailBytes);
                byte[] tail = await src.Read(start, (int)(len - start));
                info = BambuSlicer.ParseGcode(text + "\n" + Encoding.UTF8.GetString(tail));
            }
            if (!info.HasValues) { job.Error = BambuSlicer.GcodeMissingText(); return; }
            job.Result = info.ToResult();
            if (info.Picture != null) { job.PictureType = BambuSlicer.ImageType(info.Picture); if (job.PictureType == null) job.Result.Picture = null; }
        }
    }
}
