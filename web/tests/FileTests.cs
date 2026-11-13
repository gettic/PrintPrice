using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PrintPrice;
using PrintPrice.Web;

// Stage 2 tests: the web's ranged reading of sliced .gcode.3mf / G-code against the desktop's own parsing
// (src\SliceInfo.cs, the same steps as BambuSlicer.ReadSliceInfo). Samples: plans\website\stage2\samples (not in the repo;
// the sample tests are skipped with a note when the folder isn't there).
static class FileTests
{
    static Action<string, bool> True;
    static Action<string, double, double> Eq;

    public static void Run(Action<string, bool> t, Action<string, double, double> eq)
    {
        True = t; Eq = eq;
        JsonShapes();
        Synthetic();
        OtherSlicers();
        Plates();
        string dir = Samples();
        if (dir == null) { Console.WriteLine("NOTE sample files not found: sample tests skipped"); return; }
        Sample3mf(Path.Combine(dir, "test-cubes-3colour.gcode.3mf"));
        SampleGcode(Path.Combine(dir, "test-cubes-3colour.gcode"), Path.Combine(dir, "test-cubes-3colour.gcode.3mf"));
        OneColour(Path.Combine(dir, "test-cube-1colour.gcode"));
    }

    static string Samples()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "plans", "website", "stage2", "samples");
            if (Directory.Exists(p)) return p;
            p = Path.Combine(d.FullName, "plans", "_old", "website", "stage2", "samples");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    static FileJob Read(byte[] data, string name, out BytesSource src)
    {
        src = new BytesSource(data);
        return SlicedFile.Read(src, name, "Bambu PLA Matte").GetAwaiter().GetResult();
    }

    // the web Json gives JavaScriptSerializer's untyped shapes (ParseSlotStrings relies on them)
    static void JsonShapes()
    {
        var d = Json.Deserialize<Dictionary<string, object>>("{\"a\":[\"x\",1,0.40],\"b\":12,\"c\":\"s\",\"d\":{\"e\":true}}");
        True("Json untyped: array = ArrayList", d["a"] is ArrayList && ((ArrayList)d["a"]).Count == 3);
        True("Json untyped: int / decimal 0.40", ((ArrayList)d["a"])[1] is int && ((ArrayList)d["a"])[2] is decimal
             && Convert.ToString(((ArrayList)d["a"])[2], System.Globalization.CultureInfo.InvariantCulture) == "0.40");
        True("Json untyped: nested object, bool", d["d"] is Dictionary<string, object> && (bool)((Dictionary<string, object>)d["d"])["e"]);
        var slots = BambuSlicer.ParseSlotStrings("{\"filament_settings_id\":[\"A @BBL A1\",\"B\"],\"nozzle_diameter\":[\"0.4\"]}", "filament_settings_id");
        True("ParseSlotStrings via web Json", slots.Count == 2 && slots[0] == "A @BBL A1" && slots[1] == "B");
        True("ParseSlotStrings bad JSON -> empty", BambuSlicer.ParseSlotStrings("{bad", "x").Count == 0);
    }

    static void Synthetic()
    {
        Eq("Duration 1d 2h 3m 4s", 93784, BambuSlicer.Duration("1d 2h 3m 4s"));
        Eq("Duration 12m 18s", 738, BambuSlicer.Duration("12m 18s"));
        BytesSource s;
        // unsliced and wrong files
        var stl = Read(new byte[] { 1, 2, 3 }, "Benchy.stl", out s);
        True("STL -> unsliced, nothing read", stl.Unsliced && stl.Result == null && s.Reads == 0);
        var txt = Read(new byte[] { 1, 2, 3 }, "notes.txt", out s);
        True("txt -> error", txt.Error != null && !txt.Unsliced);
        var E = FieldState.Empty; var O = FieldState.Ok;
        True("Incomplete: grams empty", WebCalc.Incomplete(O, O, E));
        True("Incomplete: both time boxes empty", WebCalc.Incomplete(E, E, O));
        True("Complete: one time box empty is fine (empty = 0)", !WebCalc.Incomplete(E, O, O) && !WebCalc.Incomplete(O, E, O) && !WebCalc.Incomplete(O, O, O));
        var plain3mf = Read(MakeZip(new Dictionary<string, byte[]> { { "3D/3dmodel.model", Encoding.UTF8.GetBytes("<model/>") } }, 0), "Benchy.3mf", out s);
        True("unsliced 3mf -> unsliced", plain3mf.Unsliced);
        var broken = Read(Encoding.UTF8.GetBytes("not a zip at all, just text"), "x.gcode.3mf", out s);
        True("broken 3mf -> error", broken.Error != null && broken.Result == null);

        // G-code: printer lines, unsupported printer, thumbnail
        string head = "; HEADER_BLOCK_START\n; model printing time: 1h 0m 0s; total estimated time: 1h 2m 3s\n" +
                      "; total filament weight [g] : 10.5\n; filament: 1\n; HEADER_BLOCK_END\n";
        string cfg = "; CONFIG_BLOCK_START\n; filament_colour = #00ff00\n; filament_type = PETG\n" +
                     "; filament_settings_id = \"Bambu PETG HF @BBL A1M\"\n; nozzle_diameter = 0.4\n; printer_model = {0}\n; CONFIG_BLOCK_END\n";
        var mini = Read(Encoding.UTF8.GetBytes(head + string.Format(cfg, "Bambu Lab A1 mini")), "a.gcode", out s);
        True("G-code A1 mini: values + printer", mini.Result != null && mini.Result.Seconds == 3723 && mini.Result.Grams == 10.5
             && mini.Result.PrinterModel == "A1M" && mini.Result.Filaments.Count == 1 && mini.Result.Filaments[0].Colour == "#00FF00"
             && mini.Result.Filaments[0].Type == "PETG" && mini.Result.Filaments[0].Profile == "Bambu PETG HF @BBL A1M");
        var x1 = Read(Encoding.UTF8.GetBytes(head + string.Format(cfg, "Bambu Lab X1 Carbon")), "a.gcode", out s);
        True("G-code X1C -> unsupported text", x1.Result == null && x1.Error != null && x1.Error.Contains("X1 Carbon") && x1.Error.Contains(BambuSlicer.IssuesUrl));
        var none = Read(Encoding.UTF8.GetBytes("G1 X1\nG1 X2\n"), "other.gcode", out s);
        True("G-code without numbers -> error", none.Result == null && none.Error != null);

        byte[] png = Png();
        string b64 = Convert.ToBase64String(png);
        var sb = new StringBuilder("; THUMBNAIL_BLOCK_START\n; thumbnail begin 16x16 " + b64.Length + "\n");
        for (int i = 0; i < b64.Length; i += 78) sb.Append("; " + b64.Substring(i, Math.Min(78, b64.Length - i)) + "\n");
        sb.Append("; thumbnail end\n; THUMBNAIL_BLOCK_END\n");
        var thumb = Read(Encoding.UTF8.GetBytes(head + sb + string.Format(cfg, "Bambu Lab A1")), "t.gcode", out s);
        True("G-code thumbnail -> picture", thumb.Result != null && thumb.PictureType == "image/png" && thumb.Result.Picture.SequenceEqual(png));

        // a big G-code: only the first 1 MB is read when everything is there
        var big = new StringBuilder(head + string.Format(cfg, "Bambu Lab A1"));
        while (big.Length < 6 * 1048576) big.Append("G1 X10.123 Y20.456 E0.01234\n");
        var bigJob = Read(Encoding.UTF8.GetBytes(big.ToString()), "big.gcode", out s);
        True("big G-code: read " + s.BytesRead + " bytes of " + s.Length, bigJob.Result != null && s.BytesRead <= SlicedFile.GcodeHeadBytes);
        // numbers only at the end (other layout): head + tail
        var endOnly = new StringBuilder();
        while (endOnly.Length < 3 * 1048576) endOnly.Append("G1 X10.123 Y20.456 E0.01234\n");
        endOnly.Append(head + string.Format(cfg, "Bambu Lab A1"));
        var endJob = Read(Encoding.UTF8.GetBytes(endOnly.ToString()), "end.gcode", out s);
        True("G-code numbers at the end: found, read " + s.BytesRead, endJob.Result != null && endJob.Result.Seconds == 3723
             && s.BytesRead <= SlicedFile.GcodeHeadBytes + SlicedFile.GcodeTailBytes);
    }

    // ---------- plates list (idea 21): names from Metadata/model_settings.config via the shared ApplyPlateNames ----------

    static void Plates()
    {
        string slice = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<config>\n" +
            "<plate><metadata key=\"index\" value=\"1\"/><metadata key=\"prediction\" value=\"4500\"/><metadata key=\"weight\" value=\"39.2\"/>" +
            "<object identify_id=\"1\" name=\"Body\" skipped=\"false\"/>" +
            "<filament id=\"1\" type=\"PLA\" color=\"#C12E1F\" used_m=\"13\" used_g=\"39.2\"/></plate>\n" +
            "<plate><metadata key=\"index\" value=\"2\"/><metadata key=\"prediction\" value=\"4800\"/><metadata key=\"weight\" value=\"34\"/>" +
            "<object identify_id=\"2\" name=\"Wheel\" skipped=\"false\"/><object identify_id=\"3\" name=\"Wheel\" skipped=\"false\"/>" +
            "<object identify_id=\"4\" name=\"Wheel\" skipped=\"false\"/><object identify_id=\"5\" name=\"Spare\" skipped=\"true\"/>" +
            "<filament id=\"2\" type=\"PLA\" color=\"#F2F2F2\" used_m=\"11\" used_g=\"34\"/></plate>\n" +
            "<plate><metadata key=\"index\" value=\"3\"/><metadata key=\"prediction\" value=\"0\"/><metadata key=\"weight\" value=\"0\"/></plate>\n</config>\n";
        string model = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<config>\n" +
            "<plate><metadata key=\"plater_id\" value=\"1\"/><metadata key=\"plater_name\" value=\"\"/></plate>\n" +
            "<plate><metadata key=\"plater_id\" value=\"2\"/><metadata key=\"plater_name\" value=\"Wheels\"/></plate>\n</config>\n";
        Func<bool, byte[]> zip = withNames =>
        {
            var e = new Dictionary<string, byte[]>
            {
                { "Metadata/slice_info.config", Encoding.UTF8.GetBytes(slice) },
                { "Metadata/plate_1.gcode", Encoding.UTF8.GetBytes("G1 X1\n") },
                { "Metadata/plate_2.gcode", Encoding.UTF8.GetBytes("G1 X1\n") },
            };
            if (withNames) e["Metadata/model_settings.config"] = Encoding.UTF8.GetBytes(model);
            return MakeZip(e, 1);
        };
        BytesSource s;
        var job = Read(zip(true), "Car.gcode.3mf", out s);
        var pl = job.Result == null ? null : job.Result.PlateList;
        True("plates: 2 sliced plates (unsliced plate 3 left out), totals unchanged", pl != null && pl.Count == 2
             && job.Result.Seconds == 9300 && Math.Abs(job.Result.Grams - 73.2) < 1e-9);
        True("plates: name from model_settings.config", pl != null && pl.Count == 2 && pl[0].Name == "" && pl[1].Name == "Wheels");
        bool wasDanish = L.Danish;
        L.Danish = false;
        string en1 = BambuSlicer.PlateLine(pl[0], false), en2 = BambuSlicer.PlateLine(pl[1], false);
        True("plate line EN: " + en2, en1 == "Plate 1 – 1 h 15 m, 39,2 g, 1 object" && en2 == "Plate 2 'Wheels' – 1 h 20 m, 34 g, 3 objects");
        L.Danish = true;
        string da2 = BambuSlicer.PlateLine(pl[1], false);
        True("plate line DA: " + da2, da2 == "Plade 2 'Wheels' – 1 t 20 m, 34 g, 3 objekter");
        L.Danish = wasDanish;
        var noNames = Read(zip(false), "Car.gcode.3mf", out s);
        True("plates without model_settings.config: no names, no error", noNames.Result != null && noNames.Error == null
             && noNames.Result.PlateList.Count == 2 && noNames.Result.PlateList.All(p => p.Name == ""));
        var g = Read(Encoding.UTF8.GetBytes("; HEADER_BLOCK_START\n; total estimated time: 1h 2m 3s\n; total filament weight [g] : 10.5\n; HEADER_BLOCK_END\n"),
                     "one.gcode", out s);
        True("G-code: one plate (no list)", g.Result != null && g.Result.PlateList.Count == 1);
    }

    // ---------- G-code from other slicers (idea 14): small made-up samples in web\tests\gcode-samples ----------

    static bool Near(double a, double b) { return Math.Abs(a - b) < 0.015; }

    static void OtherSlicers()
    {
        string dir = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && dir == null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "gcode-samples"))) dir = Path.Combine(d.FullName, "gcode-samples");
        if (dir == null) { Console.WriteLine("NOTE web\\tests\\gcode-samples not found: other-slicer tests skipped"); return; }
        Func<string, FileJob> sample = f =>
        {
            BytesSource s;
            return Read(File.ReadAllBytes(Path.Combine(dir, f)), f, out s);
        };

        var j = sample("bambu_a1_2colour.gcode");
        True("bambu sample: 47m32s, 11,86 g, slots 1+3, A1, no note", j.Result != null && j.Result.Seconds == 2852 && Near(j.Result.Grams, 11.86)
             && j.Result.Filaments.Count == 2 && j.Result.Filaments[1].Slot == 3 && j.Result.PrinterModel == "A1"
             && BambuSlicer.OtherSlicerNote(j.Result) == null);

        j = sample("orca_a1.gcode");
        True("orca sample: 39m40s, 11,67 g, A1, note names OrcaSlicer", j.Result != null && j.Result.Seconds == 2380 && Near(j.Result.Grams, 11.67)
             && j.Result.PrinterModel == "A1" && !j.Result.GramsEstimated && BambuSlicer.OtherSlicerNote(j.Result).Contains("OrcaSlicer"));

        j = sample("orca_voron.gcode");
        True("orca Voron: name only, not refused", j.Result != null && j.Error == null && j.Result.PrinterModel == null && j.Result.PrinterName == "Voron 2.4 350");

        j = sample("prusa_mmu_2colour.gcode");
        True("prusa sample: 2 colours, purge, name only, PNG picture", j.Result != null && j.Result.Seconds == 3723 && j.Result.Filaments.Count == 2
             && Near(j.Result.PurgeGrams, 1.84) && j.Result.PrinterModel == null && j.PictureType == "image/png");

        var vor = sample("orca_voron.gcode").Result;
        True("voron: OtherPrinter + warning names it and the selected printer", vor.OtherPrinter
             && BambuSlicer.OtherPrinterWarning(vor, "A1").Contains("Voron 2.4 350") && BambuSlicer.OtherPrinterWarning(vor, "A1").Contains("sliced for"));
        True("no other-printer warning for an A1 file", BambuSlicer.OtherPrinterWarning(sample("orca_a1.gcode").Result, "A1") == null);

        j = sample("prusa_no_density.gcode");
        True("prusa without density: estimated grams + note", j.Result != null && j.Result.GramsEstimated && Near(j.Result.Grams, 11.67)
             && BambuSlicer.OtherSlicerNote(j.Result).Contains("estimated"));

        j = sample("cura_marlin.gcode");
        True("cura Marlin: 2269 s, estimated grams", j.Result != null && j.Result.Seconds == 2269 && j.Result.GramsEstimated && j.Result.SlicerName == BambuSlicer.SlicerCura);
        j = sample("cura_griffin.gcode");
        True("cura Griffin: 3725 s, estimated grams", j.Result != null && j.Result.Seconds == 3725 && j.Result.GramsEstimated);
        j = sample("cura_weight_placeholders.gcode");
        True("cura weight placeholder: 6,1 g, not estimated", j.Result != null && Near(j.Result.Grams, 6.1) && !j.Result.GramsEstimated);

        j = sample("odd_bom_prusa.gcode");
        True("BOM + PrusaSlicer: still detected as Prusa, 3723 s, 10,93 g", j.Result != null && j.Result.SlicerName == BambuSlicer.SlicerPrusa && j.Result.Seconds == 3723 && Near(j.Result.Grams, 10.93));
        j = sample("odd_decimal_comma.gcode");
        True("decimal comma (Prusa): 2 slots, 10,93 g, not 103", j.Result != null && j.Result.Filaments.Count == 2 && Near(j.Result.Grams, 10.93));
        j = sample("odd_cura_decimal_comma.gcode");
        True("decimal comma (Cura metres): about 11,7 g, estimated", j.Result != null && Near(j.Result.Grams, 11.67) && j.Result.GramsEstimated);
        j = sample("odd_impossible.gcode");
        True("impossible numbers -> error, no values, no price", j.Result == null && j.Error != null && j.Error.Contains("don't look right"));
        j = sample("partial_time_only.gcode");
        True("time only: 2400 s, grams flagged missing + text", j.Result != null && j.Result.Seconds == 2400 && j.Result.MissingGrams && !j.Result.MissingTime
             && BambuSlicer.MissingValueText(j.Result).Contains("weight"));
        j = sample("partial_grams_only.gcode");
        True("grams only: 11,67 g, time flagged missing + text", j.Result != null && Near(j.Result.Grams, 11.67) && j.Result.MissingTime && !j.Result.MissingGrams
             && BambuSlicer.MissingValueText(j.Result).Contains("print time"));
        True("complete file has no missing text", BambuSlicer.MissingValueText(sample("orca_a1.gcode").Result) == null);
        j = sample("bambu_x1c.gcode");
        True("X1C G-code still refused", j.Result == null && j.Error != null && j.Error.Contains("X1 Carbon"));

        j = sample("binary.bgcode");
        True("bgcode -> binary text", j.Result == null && j.Error == BambuSlicer.BinaryGcodeText());
        BytesSource s2;
        j = Read(Encoding.ASCII.GetBytes("GCDE\x01\x00\x00\x00"), "renamed.gcode", out s2);
        True("GCDE bytes in a .gcode -> binary text", j.Result == null && j.Error == BambuSlicer.BinaryGcodeText());
        j = Read(Encoding.UTF8.GetBytes("G28\nG1 X1\n"), "empty.gco", out s2);
        True(".gco without numbers -> missing text", j.Result == null && j.Error == BambuSlicer.GcodeMissingText());

        // Prusa numbers in the last 256 KB of a big file: head + tail only
        string prusa = File.ReadAllText(Path.Combine(dir, "prusa_mmu_2colour.gcode"));
        var sb = new StringBuilder(prusa.Split(new[] { "G28" }, StringSplitOptions.None)[0]);
        sb.Append("G28\n");
        while (sb.Length < 3 * 1024 * 1024) sb.Append("G1 X10.000 Y10.000 E0.01234\n");
        sb.Append(prusa.Split(new[] { "G1 X10 Y10 F3000" }, StringSplitOptions.None)[1]);
        j = Read(Encoding.UTF8.GetBytes(sb.ToString()), "big-prusa.gcode", out s2);
        True("big Prusa file: found, read " + s2.BytesRead, j.Result != null && j.Result.Seconds == 3723 && s2.BytesRead <= SlicedFile.GcodeHeadBytes + SlicedFile.GcodeTailBytes);

        L.Danish = true;
        True("Danish note", BambuSlicer.OtherSlicerNote(new SliceResult { SlicerName = "Cura", GramsEstimated = true }).Contains("anslået"));
        L.Danish = false;
    }

    // ---------- samples ----------

    // the desktop's steps (BambuSlicer.ReadSliceInfo) with ZipArchive on the whole file
    static SliceResult Desktop(string path, out byte[] picture)
    {
        using (var zip = ZipFile.OpenRead(path))
        {
            string xml = Text(zip, "Metadata/slice_info.config"), settings = Text(zip, "Metadata/project_settings.config");
            var res = BambuSlicer.ParseSliceInfo(xml);
            var profiles = BambuSlicer.ParseFilamentSlots(settings);
            foreach (var f in res.Filaments)
                if (f.Slot >= 1 && f.Slot <= profiles.Count) f.Profile = profiles[f.Slot - 1];
            var pp = BambuSlicer.ParseProjectPrinter(settings, xml);
            res.PrinterModel = pp.Key; res.PrinterName = pp.Name; res.PrinterNozzle = pp.Nozzle;
            using (var ms = new MemoryStream())
            {
                zip.GetEntry(BambuSlicer.PickPictureEntry(zip.Entries.Select(e => e.FullName))).Open().CopyTo(ms);
                picture = ms.ToArray();
            }
            return res;
        }
    }

    static string Text(ZipArchive zip, string name)
    {
        using (var sr = new StreamReader(zip.GetEntry(name).Open())) return sr.ReadToEnd();
    }

    static void Same(string what, SliceResult want, SliceResult got, bool profilesToo, double gramsTolerance = 0)
    {
        True(what + ": result", got != null);
        if (got == null) return;
        Eq(what + ": seconds", want.Seconds, got.Seconds);
        True(what + ": grams " + want.Grams + " / " + Math.Round(got.Grams, 2), Math.Abs(want.Grams - Math.Round(got.Grams, 2)) <= gramsTolerance + 1e-9);
        True(what + ": printer " + got.PrinterModel + " / " + got.PrinterName + " / " + got.PrinterNozzle,
             got.PrinterModel == want.PrinterModel && got.PrinterNozzle == want.PrinterNozzle);
        True(what + ": " + got.Filaments.Count + " colours", got.Filaments.Count == want.Filaments.Count);
        for (int i = 0; i < Math.Min(got.Filaments.Count, want.Filaments.Count); i++)
        {
            var a = want.Filaments[i]; var b = got.Filaments[i];
            True(what + ": slot " + a.Slot + " " + b.Colour + " " + b.Type + " " + b.Grams + " g " + b.Profile,
                 a.Slot == b.Slot && a.Colour == b.Colour && a.Type == b.Type && Math.Abs(a.Grams - b.Grams) < 1e-9
                 && (!profilesToo || a.Profile == b.Profile));
        }
    }

    static void Sample3mf(string path)
    {
        byte[] data = File.ReadAllBytes(path), pic;
        var desk = Desktop(path, out pic);
        True("sample 3mf desktop: 13382 s, 58,15 g, 3 slots, A1", desk.Seconds == 13382 && desk.Grams == 58.15 && desk.Filaments.Count == 3 && desk.PrinterModel == "A1");
        BytesSource s;
        var job = Read(data, Path.GetFileName(path), out s);
        Same("sample 3mf web", desk, job.Result, true);
        True("sample 3mf picture = plate_1.png", job.Result != null && job.PictureType == "image/png" && job.Result.Picture.SequenceEqual(pic));

        // every entry read by ranges = ZipArchive's bytes
        var z = ZipRanges.Open(new BytesSource(data)).GetAwaiter().GetResult();
        bool all = z != null;
        using (var zip = ZipFile.OpenRead(path))
            foreach (var e in zip.Entries)
            {
                using (var ms = new MemoryStream()) { e.Open().CopyTo(ms); all &= z.ReadEntry(e.FullName, long.MaxValue).GetAwaiter().GetResult().SequenceEqual(ms.ToArray()); }
            }
        True("ZipRanges = ZipArchive for all " + (z == null ? 0 : z.Entries.Count) + " entries", all);

        // the same project with a 20 MB plate G-code first: only the end, the list and three entries are read
        var entries = new Dictionary<string, byte[]>();
        var noise = new byte[20 * 1048576];
        new Random(1).NextBytes(noise);   // doesn't compress: the entry stays ~20 MB inside the zip
        entries["Metadata/plate_1.gcode"] = noise;
        using (var zip = ZipFile.OpenRead(path))
            foreach (var e in zip.Entries)
                if (e.FullName != "Metadata/plate_1.gcode")
                    using (var ms = new MemoryStream()) { e.Open().CopyTo(ms); entries[e.FullName] = ms.ToArray(); }
        byte[] bigZip = MakeZip(entries, 1);
        var bigJob = Read(bigZip, "big.gcode.3mf", out s);
        Same("big 3mf web", desk, bigJob.Result, true);
        Console.WriteLine("Sample 3mf: {0} s, {1} g, {2}; web = desktop. Same project with a 20 MB plate G-code: read {3:N0} of {4:N0} bytes ({5} reads)",
            desk.Seconds, desk.Grams, string.Join(" + ", desk.Filaments.Select(f => f.Grams + " g " + f.Colour + " " + BambuSlicer.FilamentBaseName(f.Profile))),
            s.BytesRead, bigZip.Length, s.Reads);
        True("big 3mf: read " + s.BytesRead + " of " + bigZip.Length + " bytes in " + s.Reads + " reads", s.BytesRead < 400 * 1024);
    }

    static void SampleGcode(string gcode, string threeMf)
    {
        byte[] pic;
        var desk = Desktop(threeMf, out pic);
        BytesSource s;
        var job = Read(File.ReadAllBytes(gcode), Path.GetFileName(gcode), out s);
        // the G-code lists grams per colour only (27,15 + 12,46 + 18,55 = 58,16); slice_info has the total 58,15
        Same("sample G-code vs the 3mf", desk, job.Result, true, 0.015);
        True("sample G-code (CLI): no thumbnail", job.Result != null && job.Result.Picture == null);
    }

    static void OneColour(string gcode)
    {
        BytesSource s;
        var job = Read(File.ReadAllBytes(gcode), Path.GetFileName(gcode), out s);
        True("1-colour G-code: 738 s, 3,75 g, 1 slot, A1", job.Result != null && job.Result.Seconds == 738 && job.Result.Grams == 3.75
             && job.Result.Filaments.Count == 1 && job.Result.PrinterModel == "A1");
        // priced like typing 0 h 12 m, 3,75 g on the desktop
        var input = new QuoteInput { Hours = 0, Minutes = 12, FilamentGrams = 3.75, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 2 };
        True("1-colour priced", Pricing.Calculate(input, new AppSettings()).MaterialCost == 0.75);
    }

    // ---------- helpers ----------

    static byte[] MakeZip(Dictionary<string, byte[]> entries, int level)
    {
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var kv in entries)
                {
                    var e = zip.CreateEntry(kv.Key, level == 0 ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    using (var w = e.Open()) w.Write(kv.Value, 0, kv.Value.Length);
                }
            return ms.ToArray();
        }
    }

    // a valid 1x1 PNG
    static byte[] Png()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    }
}

