using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PrintPrice;
using PrintPrice.Web;

// Issue #8 step 3 on the website: the prime tower of a read file (the desktop's BambuSlicer.TowerFromGcode / ApplyTower on
// the web's ranged reads), the note under the colour rows, the quote / invoice note row (Settings' QuoteShowTower), the
// combined bill's sum, Copy text, and the History JSON (HistoryFilament.TowerGrams only when known, as the desktop writes it).
// Samples: tests\gcode-samples\tower (the desktop's trimmed tower samples).
static class TowerTests
{
    static Action<string, bool> True;
    static Action<string, double, double> Eq;

    public static void Run(Action<string, bool> t, Action<string, double, double> eq)
    {
        True = t; Eq = eq;
        bool wasDanish = L.Danish;
        L.Danish = false;
        try
        {
            Files();
            Note();
            Quotes();
            JsonText();
        }
        finally { L.Danish = wasDanish; }
    }

    static string Dir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "tests", "gcode-samples", "tower");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    static bool Near(double a, double b) { return Math.Abs(a - b) <= 0.01; }

    // a Bambu Studio header with two PLA colours (slots 1 and 2), as the tower samples use T0 / T1
    const string Head = "; HEADER_BLOCK_START\n; BambuStudio 02.03.00.70\n; model printing time: 41m 10s; total estimated time: 47m 32s\n" +
                        "; total filament weight [g] : 4.85,0.99\n; filament: 1,2\n; HEADER_BLOCK_END\n" +
                        "; CONFIG_BLOCK_START\n; filament_colour = #FFFFFF;#ED1C24\n" +
                        "; filament_settings_id = \"Bambu PLA Basic @BBL A1\";\"Bambu PLA Basic @BBL A1\"\n" +
                        "; filament_type = PLA;PLA\n; printer_model = Bambu Lab A1\n; CONFIG_BLOCK_END\n";

    public static string Gcode(string sample)
    {
        string dir = Dir();
        return dir == null ? null : Head + File.ReadAllText(Path.Combine(dir, sample)).Replace("\r", "");
    }

    static FileJob Read(byte[] data, string name)
    {
        return SlicedFile.Read(new BytesSource(data), name, "Bambu PLA Matte").GetAwaiter().GetResult();
    }

    static string Slots(SliceResult r)
    {
        return r == null ? "-" : r.TowerGrams.ToString("0.###") + " g = " + string.Join(" + ", r.Filaments.Select(f => f.TowerGrams.ToString("0.###")));
    }

    static void Files()
    {
        string dir = Dir();
        if (dir == null) { Console.WriteLine("NOTE tests\\gcode-samples\\tower not found: tower file tests skipped"); return; }

        // G-code with the tower on (L): 1,098 + 0,606 g, the same numbers the desktop counts (TowerSlicerTests)
        var on = Read(Encoding.UTF8.GetBytes(Gcode("L_on_names.gcode")), "L_on.gcode");
        var r = on.Result;
        True("tower G-code (on): 1,704 g = 1,098 + 0,606: " + Slots(r), r != null && Near(r.TowerGrams, 1.704) && r.Filaments.Count == 2
             && Near(r.Filaments[0].TowerGrams, 1.098) && Near(r.Filaments[1].TowerGrams, 0.606));
        True("tower G-code: time unknown on the web (no result.json)", r != null && r.TowerSeconds == -1);
        // tower off (M): 0, a known "no tower"
        var off = Read(Encoding.UTF8.GetBytes(Gcode("M_off_names.gcode")), "M_off.gcode");
        True("tower G-code (off): 0 / 0: " + Slots(off.Result), off.Result != null && off.Result.TowerGrams == 0 && off.Result.Filaments.All(f => f.TowerGrams == 0));
        // a slicer whose tower marker we don't know (Cura) and no blocks: unknown
        var cura = Read(Encoding.UTF8.GetBytes(";FLAVOR:Marlin\n;TIME:3723\n;Filament used: 1.2m\n;Generated with Cura_SteamEngine 5.7.1\nM83\nG1 X1 Y1 E5\n"), "cura.gcode");
        True("Cura G-code without a known marker: tower unknown (-1)", cura.Result != null && cura.Result.TowerGrams == -1);
        // without the count (tower: false) nothing is known
        var notCounted = SlicedFile.Read(new BytesSource(Encoding.UTF8.GetBytes(Gcode("L_on_names.gcode"))), "L.gcode", "x", false).GetAwaiter().GetResult();
        True("tower: false = not counted (-1)", notCounted.Result != null && notCounted.Result.TowerGrams == -1);

        // .gcode.3mf: two plates (L + G3), deflated; our own " #2" slot names are taken off (= ReadSliceInfo)
        string slice = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<config>\n" +
            "<plate><metadata key=\"index\" value=\"1\"/><metadata key=\"prediction\" value=\"2852\"/><metadata key=\"weight\" value=\"5.84\"/>" +
            "<filament id=\"1\" type=\"PLA\" color=\"#FFFFFF\" used_m=\"1\" used_g=\"4.85\"/><filament id=\"2\" type=\"PLA\" color=\"#ED1C24\" used_m=\"1\" used_g=\"0.99\"/></plate>\n" +
            "<plate><metadata key=\"index\" value=\"2\"/><metadata key=\"prediction\" value=\"3000\"/><metadata key=\"weight\" value=\"8\"/>" +
            "<filament id=\"1\" type=\"PLA\" color=\"#FFFFFF\" used_m=\"1\" used_g=\"5\"/><filament id=\"2\" type=\"PLA\" color=\"#ED1C24\" used_m=\"1\" used_g=\"3\"/></plate>\n</config>\n";
        string settings = "{\"filament_settings_id\":[\"Bambu PLA Basic @BBL A1 #1\",\"Bambu PLA Basic @BBL A1 #2\"],\"printer_model\":\"Bambu Lab A1\",\"nozzle_diameter\":[\"0.4\"]}";
        var zip = Zip(new Dictionary<string, byte[]>
        {
            { "Metadata/slice_info.config", Encoding.UTF8.GetBytes(slice) },
            { "Metadata/project_settings.config", Encoding.UTF8.GetBytes(settings) },
            { "Metadata/plate_1.gcode", File.ReadAllBytes(Path.Combine(dir, "L_on_names.gcode")) },
            { "Metadata/plate_2.gcode", File.ReadAllBytes(Path.Combine(dir, "G3_on.gcode")) },
        });
        var z = Read(zip, "two.gcode.3mf").Result;
        True("tower .gcode.3mf, 2 plates: 1,098 + 1,655 / 0,606 + 1,029 g: " + Slots(z), z != null && Near(z.TowerGrams, 1.098 + 1.655 + 0.606 + 1.029)
             && Near(z.Filaments[0].TowerGrams, 2.753) && Near(z.Filaments[1].TowerGrams, 1.635));
        True("tower .gcode.3mf: slot names without our \" #2\"", z != null && z.Filaments[0].Profile == "Bambu PLA Basic @BBL A1" && z.Filaments[1].Profile == "Bambu PLA Basic @BBL A1");
        // one plate: the plate's own colours get it too (= ApplyTower)
        var one = Read(Zip(new Dictionary<string, byte[]>
        {
            { "Metadata/slice_info.config", Encoding.UTF8.GetBytes(slice.Substring(0, slice.IndexOf("<plate>", slice.IndexOf("</plate>"), StringComparison.Ordinal)) + "</config>\n") },
            { "Metadata/plate_1.gcode", File.ReadAllBytes(Path.Combine(dir, "M_off_names.gcode")) },
        }), "off.gcode.3mf").Result;
        True("tower .gcode.3mf off: 0", one != null && one.TowerGrams == 0 && one.Filaments.All(f => f.TowerGrams == 0));
    }

    static byte[] Zip(Dictionary<string, byte[]> entries)
    {
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var kv in entries)
                    using (var w = zip.CreateEntry(kv.Key, CompressionLevel.Optimal).Open()) w.Write(kv.Value, 0, kv.Value.Length);
            return ms.ToArray();
        }
    }

    // the note under the colour rows = MainForm.PurgeNote (one line: the page wraps at " · ")
    static void Note()
    {
        Func<double, double, double, double, string> note = (p, h, t, s) => string.Join(" · ", WebCalc.PurgeNoteParts(p, h, t, s));
        True("note: flush only + tower: " + note(9.37, -1, 1.70, -1), note(9.37, -1, 1.70, -1) == "incl. 7,7 g for colour changes · prime tower 1,7 g");
        True("note: with a known tower time", note(9.37, -1, 1.70, 314) == "incl. 7,7 g for colour changes · prime tower 1,7 g · 5 min");
        True("note: tower unknown = the whole purge, no tower part", note(9.37, -1, -1, -1) == "incl. 9,4 g for colour changes");
        True("note: tower 0 = no tower part", note(9.37, 0.2, 0, -1) == "incl. 9,4 g and 0,2 h for colour changes");
        True("note: tower alone starts with a capital", note(-1, -1, 1.70, -1) == "Prime tower 1,7 g" && note(1.9, -1, 1.70, -1) == "Prime tower 1,7 g");
        True("note: nothing = no note", WebCalc.PurgeNoteParts(-1, -1, -1, -1).Count == 0);
        L.Danish = true;
        True("note DA: " + note(9.37, -1, 1.70, 3700), note(9.37, -1, 1.70, 3700) == "heraf 7,7 g til farveskift · primetårn 1,7 g · 1 t 2 min");
        L.Danish = false;
        True("TowerText = the desktop's", WebCalc.TowerText(1.7, 314, false) == "prime tower 1,7 g" && WebCalc.TowerText(0, 314, true) == "");
    }

    static QuoteRecord Two(double tower, double purge = 9.37)
    {
        var s = new AppSettings();
        var input = new QuoteInput { Hours = 0, Minutes = 48, FilamentGrams = 5.84, ElectricityDkkPerKwh = 2,
                                     Filaments = new List<FilamentLine> { new FilamentLine { Grams = 4.85, PricePerKg = 200 }, new FilamentLine { Grams = 0.99, PricePerKg = 200 } } };
        var mats = new List<HistoryFilament>
        {
            new HistoryFilament { Colour = "#FFFFFF", Type = "PLA", Name = "Bambu PLA Basic @BBL A1", TowerGrams = tower < 0 ? -1 : 1.098 },
            new HistoryFilament { Colour = "#ED1C24", Type = "PLA", Name = "Bambu PLA Basic @BBL A1", TowerGrams = tower < 0 ? -1 : 0.606 },
        };
        return WebHistory.MakeRecord(input, Pricing.Calculate(input, s), s, "Cubes", DateTime.Now, mats, purge, null, null, null, tower, -1);
    }

    static void Quotes()
    {
        var r = Two(1.704);
        True("MakeRecord: tower grams, per colour, switch unknown (the website doesn't slice)", Near(r.TowerGrams, 1.704) && r.TowerSeconds == -1 && r.PrimeTower == -1
             && Near(r.Filaments[0].TowerGrams, 1.098) && Near(r.Filaments[1].TowerGrams, 0.606) && r.PurgeGrams == 9.37);
        var reopened = WebHistory.MakeRecord(new QuoteInput { Hours = 1, FilamentGrams = 5, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 2 },
                                             Pricing.Calculate(new QuoteInput { Hours = 1, FilamentGrams = 5, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 2 }, new AppSettings()),
                                             new AppSettings(), "x", DateTime.Now, null, -1, null, null, null, 1.2, 300, 1);
        True("MakeRecord: a saved record's switch kept (Open again)", reopened.PrimeTower == 1 && reopened.TowerGrams == 1.2 && reopened.TowerSeconds == 300);
        var fill = WebHistory.FillFrom(r, false, new AppSettings());
        True("FillFrom: the saved tower back on the rows", Near(fill.TowerGrams, 1.704) && fill.TowerSeconds == -1 && fill.PrimeTower == -1
             && Near(fill.Rows[0].Use.TowerGrams, 1.098) && Near(fill.Rows[1].Use.TowerGrams, 0.606));

        var s = new AppSettings();
        // setting off (default): no row, the purge note keeps the whole purge (as before)
        var dOff = WebQuote.FromRecord(r, s, false, "", false, DateTime.Now, null);
        var rowsOff = WebQuote.Rows(dOff);
        True("quote, setting off: no tower row, whole purge", !rowsOff.Any(x => x.Label.Contains("prime tower")) && dOff.PurgeGrams == 9.37);
        s.QuoteShowTower = 1;
        var dOn = WebQuote.FromRecord(r, s, false, "", true, DateTime.Now, null);
        var rowsOn = WebQuote.Rows(dOn);
        int at = rowsOn.FindIndex(x => x.Label == "incl. 1,7 g for the prime tower");
        True("invoice, setting on: \"incl. 1,7 g for the prime tower\" note row before the first rule, no amount", at > 0 && rowsOn[at].Kind == QuoteRowKind.Note
             && (rowsOn[at].Amount ?? "") == "" && rowsOn.FindIndex(x => x.Kind == QuoteRowKind.Rule) == at + 1);
        True("invoice, setting on: colour changes = flush only (7,67 g)", Near(dOn.PurgeGrams, 7.666) && rowsOn.Any(x => x.Label.Contains("incl. 8 g") || x.Label.Contains("7,7")));
        L.Danish = true;
        True("DA: inkl. 1,7 g til primetårnet", WebQuote.Rows(WebQuote.FromRecord(r, s, false, "", false, DateTime.Now, null)).Any(x => x.Label == "inkl. 1,7 g til primetårnet"));
        L.Danish = false;
        // old record (unknown) and a 0 tower: never a row
        var old = Two(-1);
        True("old record (tower unknown): no row, whole purge", !WebQuote.Rows(WebQuote.FromRecord(old, s, false, "", false, DateTime.Now, null)).Any(x => x.Kind == QuoteRowKind.Note && x.Label.Contains("prime tower"))
             && WebQuote.FromRecord(old, s, false, "", false, DateTime.Now, null).PurgeGrams == 9.37);
        True("tower 0: no row", WebQuote.FromRecord(Two(0), s, false, "", false, DateTime.Now, null).TowerGrams == -1);
        // the job on the calculator (2+ colours)
        var job = new WebQuote.Job { Input = new QuoteInput { Hours = 0, Minutes = 48, FilamentGrams = 5.84, ElectricityDkkPerKwh = 2,
                                         Filaments = new List<FilamentLine> { new FilamentLine { Grams = 4.85, PricePerKg = 200 }, new FilamentLine { Grams = 0.99, PricePerKg = 200 } } },
                                     Model = "Cubes", Colours = new List<FilamentUse> { new FilamentUse { Slot = 1, Colour = "#FFFFFF", Type = "PLA" }, new FilamentUse { Slot = 2, Colour = "#ED1C24", Type = "PLA" } },
                                     PurgeGrams = 9.37, TowerGrams = 1.704 };
        var jd = WebQuote.FromJob(job, s, false, DateTime.Now, "1", "");
        True("job quote: tower row + flush", WebQuote.Rows(jd).Any(x => x.Label == "incl. 1,7 g for the prime tower") && Near(jd.PurgeGrams, 7.666));
        // combined bill: the jobs' sum (unknown ones count 0)
        var a = Two(1.704); a.Id = "a"; a.SavedAt = History.Stamp(DateTime.Now.AddMinutes(-30));
        var b = Two(-1); b.Id = "b"; b.SavedAt = History.Stamp(DateTime.Now.AddMinutes(-20));
        var c = Two(1.704); c.Id = "c"; c.SavedAt = History.Stamp(DateTime.Now.AddMinutes(-10));
        var g = WebQuote.FromRecords(new List<QuoteRecord> { a, b, c }, s, false, "", true, DateTime.Now, DateTime.Now, null);
        True("combined bill: sum 3,41 g", Near(g.TowerGrams, 3.408));
        s.QuoteShowTower = 0;
        True("combined bill, setting off: none", WebQuote.FromRecords(new List<QuoteRecord> { a, c }, s, false, "", true, DateTime.Now, DateTime.Now, null).TowerGrams == -1);
        s.QuoteShowTower = 1;
        // Copy text: the line under Print time
        string text = WebQuote.WithTowerLine(QuoteRows.RecordText(r, false, false, s, ""), WebQuote.RecordTower(s, r));
        var lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
        int pt = lines.FindIndex(x => x.StartsWith("Print time"));
        True("Copy text: the tower line under Print time", pt >= 0 && pt + 1 < lines.Count && lines[pt + 1] == "incl. 1,7 g for the prime tower");
        True("Copy text: none when unknown", WebQuote.RecordTower(s, old) == -1 && WebQuote.WithTowerLine("x", -1) == "x");
        // settings: the web's tick box is the desktop's key
        bool up;
        var back = WebCalc.SettingsFromText(WebCalc.SettingsToText(s), out up);
        True("settings text: QuoteShowTower=1 round trip, off by default", back.QuoteShowTower == 1 && new AppSettings().QuoteShowTower == 0
             && WebCalc.SettingsToText(s).Contains("QuoteShowTower=1"));
        True("Reset to defaults: QuoteShowTower off again", WebStatus.Defaults(s).QuoteShowTower == 0);
    }

    // history.json: the desktop's text (FilamentConverter: TowerGrams only when known; QuoteRecord tower fields always)
    static void JsonText()
    {
        var known = new HistoryFilament { Grams = 4.85, PricePerKg = 200, Colour = "#FFFFFF", Type = "PLA", Name = "A", TowerGrams = 1.098 };
        var unknown = new HistoryFilament { Grams = 0.99, PricePerKg = 200, Colour = "#ED1C24", Type = "PLA", Name = "B" };
        True("Json: a known tower line = the desktop's text", Json.Serialize(known) == "{\"Grams\":4.85,\"PricePerKg\":200,\"Colour\":\"#FFFFFF\",\"Type\":\"PLA\",\"Name\":\"A\",\"TowerGrams\":1.098}");
        True("Json: an unknown tower line = the old text (no TowerGrams)", Json.Serialize(unknown) == "{\"Grams\":0.99,\"PricePerKg\":200,\"Colour\":\"#ED1C24\",\"Type\":\"PLA\",\"Name\":\"B\"}");
        True("Json: reading a line without TowerGrams = -1", Json.Deserialize<HistoryFilament>("{\"Grams\":1,\"Name\":\"x\"}").TowerGrams == -1);

        // an old history.json (no tower keys anywhere) saves back unchanged where the desktop would
        var r = Two(-1);
        r.Id = "old1";
        string first = History.ToJson(new List<QuoteRecord> { r });
        var parsed = History.ParseJson(first);
        True("old record: no TowerGrams on its lines", !first.Contains("\"Name\":\"Bambu PLA Basic\",\"TowerGrams\"") && parsed != null && parsed[0].Filaments.All(f => f.TowerGrams == -1));
        True("old record: round trip unchanged", History.ToJson(parsed) == first);
        True("QuoteRecord tower fields written as the desktop does (-1 unknown)", first.Contains("\"PrimeTower\":-1") && first.Contains("\"TowerGrams\":-1") && first.Contains("\"TowerSeconds\":-1"));
        // a record with a tower: kept through JSON and through a backup (History.ToBackup / ReadBackup)
        var t = Two(1.704);
        t.Id = "t1";
        var back = History.ParseJson(History.ToJson(new List<QuoteRecord> { t }));
        True("tower record: JSON round trip keeps grams and per-colour grams", back != null && Near(back[0].TowerGrams, 1.704) && Near(back[0].Filaments[0].TowerGrams, 1.098)
             && Near(back[0].Filaments[1].TowerGrams, 0.606) && back[0].PrimeTower == -1);
        BackupData data;
        string backup = History.ToBackup(new List<QuoteRecord> { t, r }, new AppSettings { QuoteShowTower = 1 }, "web", DateTime.Now);
        string problem = History.ReadBackup(backup, out data);
        True("backup: tower fields and QuoteShowTower kept", problem == null && data != null && Near(data.Quotes[0].TowerGrams, 1.704)
             && Near(data.Quotes[0].Filaments[1].TowerGrams, 0.606) && data.Quotes[1].Filaments.All(f => f.TowerGrams == -1) && data.Settings != null && data.Settings.Contains("QuoteShowTower=1"));
        // a desktop record (switch on, time known) read on the web and written back = the same text
        string desk = "{\"Version\":1,\"Quotes\":[" + first.Substring(first.IndexOf('[') + 1, first.LastIndexOf(']') - first.IndexOf('[') - 1)
                      .Replace("\"PrimeTower\":-1", "\"PrimeTower\":1").Replace("\"TowerGrams\":-1", "\"TowerGrams\":1.7").Replace("\"TowerSeconds\":-1", "\"TowerSeconds\":314") + "]}";
        var dr = History.ParseJson(desk);
        True("desktop record (on, 1,7 g, 314 s) read on the web", dr != null && dr[0].PrimeTower == 1 && dr[0].TowerGrams == 1.7 && dr[0].TowerSeconds == 314);
    }
}
