using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PrintPrice;
using PrintPrice.Web;

// batch-plates (issue #9 / #10) on the website: a file that already holds copies (exact), the footprint estimate of a
// one-plate file, typed copies / per plate (tower and colour changes once per plate), History fields, texts, settings.
// Real samples: plans\batch-plates\online\run_*\out.3mf (skipped when missing).
static class BatchTests
{
    static Action<string, bool> True;
    static Action<string, double, double> Eq;

    public static void Run(Action<string, bool> t, Action<string, double, double> eq)
    {
        True = t; Eq = eq;
        bool wasDanish = L.Danish;
        L.Danish = false;
        try { InFile(); Footprint(); Boxes(); Totals(); Records(); Texts(); SettingsKeys(); Quotes(); Samples(); }
        finally { L.Danish = wasDanish; }
    }

    static SliceResult Res(params string[][] plates)
    {
        var r = new SliceResult();
        int i = 1;
        foreach (var p in plates) r.PlateList.Add(new PlateInfo { Index = i++, Objects = p.ToList() });
        return r;
    }

    static string[] Named(string name, int from, int n)
    {
        return Enumerable.Range(from, n).Select(k => name + "_" + k).ToArray();
    }

    static void InFile()
    {
        int per;
        int c = WebBatch.FileCopies(Res(Named("3dbenchy.stl", 1, 4), Named("3dbenchy.stl", 5, 4), Named("3dbenchy.stl", 9, 4)), out per);
        True("file holds 12 Benchys on 3 plates: 12, 4 per plate", c == 12 && per == 4);
        c = WebBatch.FileCopies(Res(new[] { "a.stl", "b.stl", "a.stl_2", "b.stl_2" }), out per);
        True("a set of two parts x 2 on one plate: 2 copies, 2 per plate", c == 2 && per == 2);
        c = WebBatch.FileCopies(Res(new[] { "Body" }, new[] { "Wheel", "Wheel", "Wheel" }), out per);
        True("different models, uneven counts: 1", c == 1 && per == 0);
        c = WebBatch.FileCopies(Res(new[] { "Benchy" }), out per);
        True("one object: 1", c == 1 && per == 0);
        c = WebBatch.FileCopies(Res(new[] { "Benchy", "Benchy", "Benchy" }), out per);
        True("same name 3 times (GUI copies): 3, 3 per plate", c == 3 && per == 3);
        True("null / empty result: 1", WebBatch.FileCopies(null, out per) == 1 && WebBatch.FileCopies(new SliceResult(), out per) == 1);
    }

    // the cubes sample's plate_1.json (plans\_old\website\stage2\samples\test-cubes-3colour.gcode.3mf)
    const string CubesJson = "{\"bbox_all\":[11.79,114.5,146.5,253.26],\"bbox_objects\":[{\"area\":400.0,\"bbox\":[126.49998,121.49998,146.49998,141.49998],\"id\":50,\"name\":\"Cube 1\"}," +
        "{\"area\":225.0,\"bbox\":[109.50001,126.49998,124.50001,141.49998],\"id\":51,\"name\":\"Cube 2\"},{\"area\":100.0,\"bbox\":[114.50001,114.50002,124.50001,124.50002],\"id\":52,\"name\":\"Cube 3\"}," +
        "{\"area\":0.0,\"bbox\":[11.79219,216.77461,48.27495,253.25737],\"id\":1000,\"name\":\"wipe_tower\"}],\"version\":2}";

    static string SliceXml(int plates)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<config>\n");
        for (int i = 1; i <= plates; i++)
            sb.Append("<plate><metadata key=\"index\" value=\"" + i + "\"/><metadata key=\"prediction\" value=\"3600\"/><metadata key=\"weight\" value=\"20\"/>" +
                      "<object identify_id=\"" + i + "\" name=\"Cube " + i + "\" skipped=\"false\"/>" +
                      "<filament id=\"1\" type=\"PLA\" color=\"#C12E1F\" used_m=\"6\" used_g=\"20\"/></plate>\n");
        return sb.Append("</config>\n").ToString();
    }

    static FileJob ReadZip(Dictionary<string, string> entries, string name)
    {
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var kv in entries)
                {
                    var e = zip.CreateEntry(kv.Key, CompressionLevel.Optimal);
                    using (var w = e.Open()) { var b = Encoding.UTF8.GetBytes(kv.Value); w.Write(b, 0, b.Length); }
                }
            return SlicedFile.Read(new BytesSource(ms.ToArray()), name, "Bambu PLA Basic", false).GetAwaiter().GetResult();
        }
    }

    static void Footprint()
    {
        var one = ReadZip(new Dictionary<string, string> { { "Metadata/slice_info.config", SliceXml(1) }, { "Metadata/plate_1.gcode", "G1 X1\n" },
                                                           { "Metadata/plate_1.json", CubesJson } }, "Cubes.gcode.3mf");
        True("one plate: footprint read (37 x 27, tower 36,48)", one.Result != null && one.Footprint != null && Math.Abs(one.Footprint.Width - 37) < 0.01
             && Math.Abs(one.Footprint.Depth - 27) < 0.01 && Math.Abs(one.Footprint.TowerWidth - 36.483) < 0.01);
        int auto = WebBatch.AutoPerPlate(one.Footprint, "A1");
        True("auto per plate on the A1 = the shared estimate (" + auto + ")", auto > 1 && auto == BambuSlicer.CopiesPerPlate("A1", 37, 27, 36.483, 36.483));
        True("A1 mini fits fewer", WebBatch.AutoPerPlate(one.Footprint, "A1M") < auto);
        var two = ReadZip(new Dictionary<string, string> { { "Metadata/slice_info.config", SliceXml(2) }, { "Metadata/plate_1.gcode", "G1 X1\n" },
                                                           { "Metadata/plate_2.gcode", "G1 X1\n" }, { "Metadata/plate_1.json", CubesJson } }, "Two.gcode.3mf");
        True("two plates: no footprint (whole file x copies)", two.Result != null && two.Footprint == null);
        var bad = ReadZip(new Dictionary<string, string> { { "Metadata/slice_info.config", SliceXml(1) }, { "Metadata/plate_1.gcode", "G1 X1\n" },
                                                           { "Metadata/plate_1.json", "not json" } }, "Bad.gcode.3mf");
        True("bad plate json: no footprint, file still read", bad.Result != null && bad.Error == null && bad.Footprint == null);
        True("no footprint: 0", WebBatch.AutoPerPlate(null, "A1") == 0);

        // the G-code's footprint: model features only, tower apart, start code / skirt / filament change left out
        var g = new GcodeFootprint();
        foreach (var l in new[] {
            "M83", "G1 X5 Y5 E2 ; purge line before any feature", "; FEATURE: Skirt", "G1 X1 Y1", "G1 X200 Y200 E1",
            "; FEATURE: Outer wall", "G1 X100 Y100", "G1 X160 Y100 E.5", "G1 X160 Y131 E.5", "G1 E-.8 ; retract",
            "G3 X100 Y131 I1 J1 E.3", "M620 S1A", "G1 X-38 Y20 E15", "M621 S1A", "; FEATURE: Prime tower", "G1 X10 Y200", "G1 X45 Y200 E1", "G1 X45 Y235 E1",
            "; FEATURE: Inner wall", "G1 X100 Y100", "G1 X100 Y110 E.2", "G10", "G1 X0 Y0" })
            g.Feed(l);
        var gr = g.Result();
        True("G-code footprint: 60 x 31, tower 35 x 35", gr != null && gr.Width == 60 && gr.Depth == 31 && gr.TowerWidth == 35 && gr.TowerDepth == 35);
        var ga = new GcodeFootprint();
        foreach (var l in new[] { "M82", ";TYPE:External perimeter", "G92 E0", "G1 X10 Y10", "G1 X30 Y10 E2", "G1 X30 Y25 E1.5 ; E went back: no", "G1 X10 Y40 E3" })
            ga.Feed(l);
        var gar = ga.Result();
        True("G-code footprint, absolute E (Prusa): 20 x 30", gar != null && gar.Width == 20 && gar.Depth == 30);
        True("no model extrusion: null", new GcodeFootprint().Result() == null);
        var comb = GcodeFootprint.Combine(new PlateFootprint { Width = 39, Depth = 16, TowerWidth = 36, TowerDepth = 36, Objects = 1 }, gr);
        True("combine: the bigger box, the JSON's tower", comb.Width == 60 && comb.Depth == 31 && comb.TowerWidth == 36 && comb.Objects == 1);
    }

    static void Boxes()
    {
        int n;
        True("Copies 12 ok", WebBatch.ReadCount("12", out n) == FieldState.Ok && n == 12);
        True("Copies 500 ok, 501 / 0 / -1 / 2,5 / abc refused", WebBatch.ReadCount("500", out n) == FieldState.Ok
             && new[] { "501", "0", "-1", "2,5", "abc" }.All(x => WebBatch.ReadCount(x, out n) == FieldState.Invalid));
        True("Copies empty = Empty", WebBatch.ReadCount(" ", out n) == FieldState.Empty);

        var p = WebBatch.OneCopy(12, 4, 0);
        True("12 copies, 4 per plate: 3 plates, estimate", p.Plates == 3 && p.PerPlate == 4 && p.Estimated && !p.Multiplied);
        p = WebBatch.OneCopy(12, 1, 0);
        True("1 per plate: 12 plates, exact (the file x 12)", p.Plates == 12 && !p.Estimated);
        p = WebBatch.OneCopy(3, 28, 0);
        True("3 copies, 28 fit: 1 plate, per plate 3", p.Plates == 1 && p.PerPlate == 3 && p.Estimated);
        p = WebBatch.OneCopy(3, 4, 2);
        True("2-plate file x 3: 6 plates, multiplied, exact", p.Plates == 6 && p.Multiplied && !p.Estimated && p.PerPlate == 0);
        p = WebBatch.OneCopy(1, 4, 0);
        True("1 copy: 1 plate", p.Copies == 1 && p.Plates == 1 && !p.Estimated);
    }

    static AppSettings Defaults()
    {
        bool up;
        var s = WebCalc.SettingsFromText(null, out up);
        WebCalc.ApplyAutoWear(s, "PLA");
        return s;
    }

    static void Totals()
    {
        var s = Defaults();
        // pricing-report's worked example: one copy 20 g / 1 h, 12 copies 4 per plate (no tower) = 240 g / 12 h -> 100,32 / 77,52
        var one = new QuoteInput { Hours = 1, Minutes = 0, FilamentGrams = 20, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2 };
        var plan = WebBatch.OneCopy(12, 4, 0);
        var tot = WebBatch.Totals(one, plan, WebBatch.PlateGrams(-1, 0), WebBatch.PlateSeconds(-1, -1), null);
        True("12 copies: 240 g, 12 h 0 m, quantity 12, plates 3", Math.Abs(tot.FilamentGrams - 240) < 1e-9 && tot.Hours == 12 && tot.Minutes == 0
             && tot.Quantity == 12 && tot.Plates == 3);
        var r = Pricing.Calculate(tot, s);
        Eq("12 copies customer 100,32", 100.32, Math.Round(r.OthersPrice, 2));
        Eq("12 copies friends 77,52", 77.52, Math.Round(r.FriendsPrice, 2));
        s.PlateChangeFee = 5;
        Eq("plate fee 5 x 2 extra plates: 110,32", 110.32, Math.Round(Pricing.Calculate(tot, s).OthersPrice, 2));
        s.PlateChangeFee = 0;
        True("1 copy: the input unchanged", WebBatch.Totals(one, WebBatch.OneCopy(1, 1, 0), 0, 0, null) == one);

        // tower once per plate: 20 g incl. 2 g tower, 1 h 30 m incl. 300 s tower; 12 copies, 4 per plate
        var withTower = new QuoteInput { Hours = 1, Minutes = 30, FilamentGrams = 20, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2 };
        tot = WebBatch.Totals(withTower, plan, WebBatch.PlateGrams(-1, 2), WebBatch.PlateSeconds(-1, 300), null);
        True("tower per plate: (20-2) x 12 + 2 x 3 = 222 g; time (5400-300) x 12 + 300 x 3 s = 17 h 15 m",
             Math.Abs(tot.FilamentGrams - 222) < 1e-9 && tot.Hours == 17 && tot.Minutes == 15);
        // 2+ colours: the purge (flush + tower) and the colour-change time per plate, per colour line apart
        var lines = new List<FilamentUse> { new FilamentUse { Slot = 1, Grams = 12, ModelGrams = 10 }, new FilamentUse { Slot = 2, Grams = 8, ModelGrams = 5 } };
        var two = new QuoteInput { Hours = 2, Minutes = 0, FilamentGrams = 20, ElectricityDkkPerKwh = 2,
                                   Filaments = new List<FilamentLine> { new FilamentLine { Grams = 12, PricePerKg = 200 }, new FilamentLine { Grams = 8, PricePerKg = 250 } } };
        tot = WebBatch.Totals(two, plan, WebBatch.PlateGrams(5, 1), WebBatch.PlateSeconds(0.25, -1), lines.Select(WebBatch.LinePlateGrams).ToList());
        True("colours: grams (20-5) x 12 + 5 x 3 = 195; lines 10 x 12 + 2 x 3 = 126 and 5 x 12 + 3 x 3 = 69; time (7200-900) x 12 + 900 x 3 = 21 h 45 m",
             Math.Abs(tot.FilamentGrams - 195) < 1e-9 && Math.Abs(tot.Filaments[0].Grams - 126) < 1e-9 && Math.Abs(tot.Filaments[1].Grams - 69) < 1e-9
             && tot.Hours == 21 && tot.Minutes == 45);
        True("line plate grams: model part unknown = its tower", WebBatch.LinePlateGrams(new FilamentUse { Grams = 8, ModelGrams = -1, TowerGrams = 1.5 }) == 1.5
             && WebBatch.LinePlateGrams(new FilamentUse { Grams = 8, ModelGrams = -1 }) == 0);
        // a 2-plate file x 3: everything x 3, tower too
        var multi = WebBatch.OneCopy(3, 4, 2);
        tot = WebBatch.Totals(withTower, multi, 2, 300, null);
        True("2-plate file x 3: 60 g, 4 h 30 m, 6 plates", Math.Abs(tot.FilamentGrams - 60) < 1e-9 && tot.Hours == 4 && tot.Minutes == 30 && tot.Plates == 6);
        Eq("per-plate total: x plates", 6, WebBatch.PerPlateTotal(2, plan));
        Eq("per-plate total: multiplied x copies", 6, WebBatch.PerPlateTotal(2, multi));
        Eq("per-plate total: unknown stays", -1, WebBatch.PerPlateTotal(-1, plan));
    }

    static void Records()
    {
        var s = Defaults();
        var one = new QuoteInput { Hours = 1, Minutes = 30, FilamentGrams = 20, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2 };
        var plan = WebBatch.OneCopy(12, 4, 0);
        var tot = WebBatch.Totals(one, plan, 2, 300, null);
        var res = Pricing.Calculate(tot, s);
        var mats = new List<HistoryFilament> { new HistoryFilament { Name = "Bambu PLA Basic", Type = "PLA", TowerGrams = WebBatch.PerPlateTotal(2, plan) } };
        var rec = WebHistory.MakeRecord(tot, res, s, "Benchy", DateTime.Now, mats, -1, null, null, null, WebBatch.PerPlateTotal(2, plan), WebBatch.PerPlateTotal(300, plan));
        History.SetCopies(rec, plan.Copies, tot.Plates, plan.PerPlate, plan.Estimated);
        True("record: totals + copies 12, plates 3, 4 per plate, estimated, tower x 3", rec.Quantity == 12 && rec.Plates == 3 && rec.PerPlate == 4
             && rec.CopiesEstimated == 1 && Math.Abs(rec.Grams - 222) < 1e-9 && rec.Hours == 17 && rec.Minutes == 15 && Math.Abs(rec.TowerGrams - 6) < 1e-9
             && Math.Abs(rec.OthersPrice - res.OthersPrice) < 1e-9);
        // the web's JSON (history in IndexedDB) and the shared backup keep the keys; the desktop names
        string json = History.ToJson(new List<QuoteRecord> { rec });
        True("JSON has the desktop keys", json.Contains("\"Quantity\":12") && json.Contains("\"Plates\":3") && json.Contains("\"PerPlate\":4")
             && json.Contains("\"CopiesEstimated\":1") && json.Contains("\"OrderId\":\"\""));
        string problem, badKey;
        var back = WebHistory.ReadStored(json, DateTime.Now, out problem, out badKey);
        True("stored text round trip", problem == null && back.Count == 1 && back[0].Quantity == 12 && back[0].Plates == 3 && back[0].PerPlate == 4 && back[0].CopiesEstimated == 1);
        // an older record (desktop or web, no keys) = 1 copy, plates unknown
        string old = json.Replace("\"Quantity\":12,", "").Replace("\"Plates\":3,", "").Replace("\"PerPlate\":4,", "").Replace("\"CopiesEstimated\":1,", "");
        var oldBack = WebHistory.ReadStored(old, DateTime.Now, out problem, out badKey);
        True("old record without the keys: 1 copy, PlatesOf 1, same price", problem == null && oldBack.Count == 1 && History.QuantityOf(oldBack[0]) == 1
             && History.PlatesOf(oldBack[0]) == 1 && Math.Abs(oldBack[0].OthersPrice - rec.OthersPrice) < 1e-9);
        BackupData data;
        string backup = History.ToBackup(new List<QuoteRecord> { rec }, s, "web", DateTime.Now);
        True("backup keeps the batch", History.ReadBackup(backup, out data) == null && data.Quotes[0].Quantity == 12 && data.Quotes[0].Plates == 3);
        // open again: the saved totals (FillFrom), copies from the record
        var f = WebHistory.FillFrom(rec, false, s);
        True("open again: the boxes are the totals", f.Hours == "17" && f.Minutes == "15" && f.Grams == "222");
        // History list name
        True("list name 12 × Benchy", WebHistory.ModelText(rec) == "12 × Benchy" && WebHistory.CopiesPart(rec) == "12 × " && WebHistory.BaseModelText(rec) == "Benchy");
        History.SetScale(rec, 150, 150, 150);
        True("copies + scale: " + WebHistory.ModelText(rec), WebHistory.ModelText(rec) == "12 × Benchy (150 %)");
        var single = WebHistory.MakeRecord(one, Pricing.Calculate(one, s), s, "Vase", DateTime.Now, null, -1, null, null);
        True("1 copy: no prefix", WebHistory.CopiesPart(single) == "" && WebHistory.ModelText(single) == "Vase");
        // a desktop job of two files with 30 copies each: each file counted in the name, no prefix
        var two = WebHistory.MakeRecord(one, Pricing.Calculate(one, s), s, "red_block.stl, blue_block.stl", DateTime.Now, null, -1, null, null);
        History.SetParts(two, new List<HistoryPart> { new HistoryPart { File = @"C:\m\red_block.stl", Copies = 30 }, new HistoryPart { File = @"C:\m\blue_block.stl", Copies = 30 } }, false);
        History.SetCopies(two, 60, 3, 0, false);
        True("two files x 30: " + WebHistory.ModelText(two), WebHistory.ModelText(two) == "30 × red_block.stl, 30 × blue_block.stl"
             && WebHistory.CopiesPart(two) == "" && WebHistory.NameText(two) == "30 × red_block.stl, 30 × blue_block.stl");
        True("plate fee row without a rate: 2 changes", QuoteRows.PlatesRowDetail(new QuoteLine { Key = QuoteLine.Plates, Quantity = 2 }, false) == "2 changes"
             && QuoteRows.PlatesRowDetail(new QuoteLine { Key = QuoteLine.Plates, Quantity = 1 }, false) == "1 change");
    }

    static void Texts()
    {
        var total = new QuoteInput { Hours = 18, Minutes = 0, FilamentGrams = 240, Plates = 3 };
        True("totals line EN: " + WebBatch.TotalsText(total, true), WebBatch.TotalsText(total, true) == "In all: ≈ 18 h 0 m · 240 g on 3 plates");
        True("per plate hint EN / range text", WebBatch.AutoText(28) == "≈ 28 fit" && WebBatch.PerPlateRangeText() == "Per plate must be 1 to 500.");
        True("totals line, exact, 1 plate", WebBatch.TotalsText(new QuoteInput { Hours = 2, Minutes = 5, FilamentGrams = 40.26, Plates = 1 }, false) == "In all: 2 h 5 m · 40,3 g");
        True("price box sub-line: 12 pcs · 9,19 kr. each", History.EachText(110.32, 12) == "12 pcs · 9,19 kr. each");
        True("fixed line from the file", WebBatch.FixedText(100, 4, true, false) == "100 pcs on 4 plates (from the file)");
        True("fixed line as saved, estimate", WebBatch.FixedText(12, 3, false, true) == "≈ 12 pcs on 3 plates (as saved)");
        L.Danish = true;
        try
        {
            True("totals line DA: " + WebBatch.TotalsText(total, true), WebBatch.TotalsText(total, true) == "I alt: ≈ 18 t 0 m · 240 g på 3 plader");
            True("price box DA", History.EachText(110.32, 12) == "12 stk. · 9,19 kr. pr. stk.");
            True("fixed line DA", WebBatch.FixedText(100, 4, true, false) == "100 stk. på 4 plader (fra filen)");
            True("one copy label DA", WebBatch.OneCopyLabel() == "(1 stk.)" && WebBatch.AutoText(28) == "plads til ≈ 28"
                 && WebBatch.PerPlateRangeText() == "Pr. plade skal være 1 til 500.");
            True("copies range DA", BambuSlicer.CopiesRangeText() == "Antal skal være 1 til 500.");
        }
        finally { L.Danish = false; }
    }

    static void SettingsKeys()
    {
        bool up;
        var s = Defaults();
        True("defaults: plate fee 0, unit price on", s.PlateChangeFee == 0 && s.QuoteShowUnitPrice == 1);
        s.PlateChangeFee = 5; s.QuoteShowUnitPrice = 0;
        string text = WebCalc.SettingsToText(s);
        var back = WebCalc.SettingsFromText(text, out up);
        True("settings text: PlateChangeFee / QuoteShowUnitPrice round trip", back.PlateChangeFee == 5 && back.QuoteShowUnitPrice == 0
             && text.Contains("PlateChangeFee=5") && text.Contains("QuoteShowUnitPrice=0"));
        var d = WebStatus.Defaults(back);
        True("Reset to defaults: fee 0, unit price on", d.PlateChangeFee == 0 && d.QuoteShowUnitPrice == 1);
    }

    static QuoteRecord Batch(string id, string model, int copies, int perPlate, int minsAgo)
    {
        var s = Defaults();
        var one = new QuoteInput { Hours = 1, Minutes = 0, FilamentGrams = 20, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2 };
        var plan = WebBatch.OneCopy(copies, perPlate, 0);
        var tot = WebBatch.Totals(one, plan, 0, 0, null);
        var r = WebHistory.MakeRecord(tot, Pricing.Calculate(tot, s), s, model, DateTime.Now.AddMinutes(-minsAgo), null, -1, null, null);
        History.SetCopies(r, plan.Copies, tot.Plates, plan.PerPlate, plan.Estimated);
        r.Id = id;
        return r;
    }

    static void Quotes()
    {
        var s = Defaults();
        var r = Batch("b1", "Benchy", 12, 4, 5);
        var d = WebQuote.FromRecord(r, s, false, "Lene", false, DateTime.Now, null);
        var det = WebQuote.Details(d);
        True("quote: title 12 × Benchy, Quantity first, time on 3 plates", d.Model == "12 × Benchy" && det[0][0] == "Quantity" && det[0][1] == "12 pcs"
             && det[1][1] == "12 h 0 m on 3 plates" && d.EachLine == "12 pcs · 8,36 kr. each");
        True("quote: total box small line with the price per piece: " + WebQuote.TotalSmallLine(d), WebQuote.TotalSmallLine(d) == "12 pcs · 8,36 kr. each · shipping not included");
        var inv = WebQuote.FromRecord(r, s, false, "Lene", true, DateTime.Now, null);
        var di = WebQuote.Details(inv);
        True("invoice: Bill to, then Quantity, then time on plates", di[0][0] == "Bill to" && di[1][0] == "Quantity" && di[2][1].EndsWith("on 3 plates"));
        s.QuoteShowUnitPrice = 0;
        var off = WebQuote.FromRecord(r, s, false, "", false, DateTime.Now, null);
        True("price per piece off: no each line, small line as before", off.EachLine == "" && WebQuote.TotalSmallLine(off) == "Shipping not included"
             && WebQuote.Details(off)[0][1] == "12 pcs");
        s.QuoteShowUnitPrice = 1;
        var plain = Batch("p1", "Vase", 1, 1, 4);
        var dp = WebQuote.FromRecord(plain, s, false, "", false, DateTime.Now, null);
        True("1 copy: the page as before", dp.Model == "Vase" && WebQuote.Details(dp).Count == 4 && WebQuote.Details(dp)[0][1] == "1 h 0 m" && WebQuote.TotalSmallLine(dp) == "Shipping not included");
        var lay = WebQuote.Draw(d, true);
        True("drawn page has the Quantity row and the each line", lay.Items.Exists(i => i.Text == "Quantity") && lay.Items.Exists(i => i.Text == "12 pcs · 8,36 kr. each · shipping not included"));
        // combined bill: a card row "Quantity", "Plates" in the summary, Copy text
        var r2 = Batch("b2", "Clip", 2, 1, 3);
        var jobs = new List<QuoteRecord> { r, r2 };
        var g = WebQuote.FromRecords(jobs, s, false, "Lene", false, DateTime.Now, DateTime.Now, null);
        True("combined bill: cards 12 × Benchy / 2 × Clip, copies rows, Plates 5", g.Jobs[0].Name == "12 × Benchy" && g.Jobs[0].Copies == "12 pcs · 3 plates"
             && g.Jobs[1].Copies == "2 pcs · 2 plates" && g.GroupPlates == "5");
        var pages = WebQuote.DrawPages(g, true);
        True("combined bill drawn: Plates row and a Quantity card row", pages[0].Items.Exists(i => i.Text == "Plates") && pages.Exists(p => p.Items.Exists(i => i.Text == "Quantity")));
        var sum = WebQuote.SummaryRows(g);
        True("summary rows = QuotePage: Prints 2 models, 14 pcs | Plates 5 | In all …: " + string.Join(" | ", sum.Select(x => x[0] + " " + x[1])),
             sum.Count == 3 && sum[0][0] == "Prints" && sum[0][1] == "2 models, 14 pcs" && sum[1][0] == "Plates" && sum[1][1] == "5"
             && sum[2][0] == "In all" && sum[2][1].Contains(" g") && pages[0].Items.Exists(i => i.Text == "In all"));
        string text = WebQuote.GroupText(jobs, g, false, s);
        True("combined Copy text: 12 × Benchy, Plates: 5", text.Contains("12 × Benchy") && text.Contains("Plates: 5"));
        True("no batch on the bill: no Plates row", WebQuote.FromRecords(new List<QuoteRecord> { plain, Batch("p2", "Cup", 1, 1, 2) }, s, false, "", false, DateTime.Now, DateTime.Now, null).GroupPlates == "");
        // the job on screen
        var one = new QuoteInput { Hours = 1, Minutes = 0, FilamentGrams = 20, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2 };
        var tot = WebBatch.Totals(one, WebBatch.OneCopy(12, 4, 0), 0, 0, null);
        var job = WebQuote.FromJob(new WebQuote.Job { Input = tot, Model = "Benchy.gcode.3mf" }, s, false, DateTime.Now, "2026-1", "");
        True("job quote: 12 × name, quantity 12, plates 3", job.Model.StartsWith("12 × ") && job.Quantity == 12 && job.Plates == 3 && job.EachLine.Length > 0);
        string jt = QuoteRows.JobText("", "Benchy", tot.Hours, tot.Minutes, "240", 0, 77.52, 100.32, 12, 3, true);
        True("job Copy text: Quantity line, on 3 plates, each", jt.Contains("Quantity: 12 pcs") && jt.Contains("on 3 plates") && jt.Contains("(8,36 kr. each)"));
        L.Danish = true;
        try
        {
            var da = WebQuote.FromRecord(r, s, false, "", false, DateTime.Now, null);
            True("DA: Antal 12 stk., på 3 plader, pr. stk.", WebQuote.Details(da)[0][0] == "Antal" && WebQuote.Details(da)[1][1] == "12 t 0 m på 3 plader"
                 && WebQuote.TotalSmallLine(da) == "12 stk. · 8,36 kr. pr. stk. · fragt er ikke inkluderet");
        }
        finally { L.Danish = false; }
    }

    // ---------- real sliced files from online's trial runs ----------

    static string Online()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "plans", "batch-plates", "online");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    static FileJob ReadFile(string path)
    {
        return SlicedFile.Read(new BytesSource(File.ReadAllBytes(path)), Path.GetFileName(path), "Bambu PLA Basic", true).GetAwaiter().GetResult();
    }

    static void Samples()
    {
        string dir = Online();
        if (dir == null) { Console.WriteLine("NOTE plans\\batch-plates\\online not found: real batch samples skipped"); return; }
        int per;
        string f = Path.Combine(dir, "run_benchy100", "out.3mf");
        if (File.Exists(f))
        {
            var j = ReadFile(f);
            int c = WebBatch.FileCopies(j.Result, out per);
            True("benchy100: 100 copies on 4 plates, 28 per plate (" + c + ", " + per + ")", j.Result != null && c == 100 && per == 28 && j.Result.PlateList.Count == 4 && j.Footprint == null);
        }
        f = Path.Combine(dir, "run_single", "out.3mf");
        if (File.Exists(f))
        {
            var j = ReadFile(f);
            int c = WebBatch.FileCopies(j.Result, out per);
            int auto = WebBatch.AutoPerPlate(j.Footprint, j.Result == null ? "A1" : j.Result.PrinterModel);
            string size = j.Footprint == null ? "none" : j.Footprint.Width + " x " + j.Footprint.Depth;
            True("single Benchy: 1 copy, footprint " + size + " (whole height, not the first layer's 39 x 16), estimate 28 = the real slice (" + auto + ")",
                 j.Result != null && c == 1 && auto == 28);
        }
        f = Path.Combine(dir, "run_tc1", "out.3mf");
        if (File.Exists(f))
        {
            var j = ReadFile(f);
            string size = j.Footprint == null ? "none" : j.Footprint.Width + " x " + j.Footprint.Depth + ", tower " + j.Footprint.TowerWidth + " x " + j.Footprint.TowerDepth;
            True("two-colour set: footprint read (" + size + ")", j.Footprint != null && j.Footprint.Width >= 16 && j.Footprint.Depth >= 22);
        }
        f = Path.Combine(dir, "run_clip1", "out.3mf");
        string f200 = Path.Combine(dir, "run_clip200", "out.3mf");
        if (File.Exists(f) && File.Exists(f200))
        {
            var j = ReadFile(f);
            var all = ReadFile(f200);
            int realPer;
            WebBatch.FileCopies(all.Result, out realPer);
            int auto = WebBatch.AutoPerPlate(j.Footprint, j.Result == null ? "A1" : j.Result.PrinterModel);
            Console.WriteLine("Cable clip: footprint " + (j.Footprint == null ? "none" : j.Footprint.Width + " x " + j.Footprint.Depth) + ", estimate " + auto + " per plate, Bambu Studio placed " + realPer);
            True("cable clip: the estimate never puts more on a plate than Bambu Studio did (" + auto + " <= " + realPer + ")", auto >= 1 && auto <= realPer);
        }
        f = Path.Combine(dir, "run_tc_rep10", "out.3mf");
        if (File.Exists(f))
        {
            var j = ReadFile(f);
            int c = WebBatch.FileCopies(j.Result, out per);
            True("two-part set x 10 on one plate: 10 copies, 10 per plate (" + c + ", " + per + ")", c == 10 && per == 10);
        }
        f = Path.Combine(dir, "run_mixed2", "out.3mf");
        if (File.Exists(f))
        {
            var j = ReadFile(f);
            int c = WebBatch.FileCopies(j.Result, out per);
            True("mixed models (4 Benchys + clips): one job, 1 copy (" + c + ")", c == 1);
        }
    }
}
