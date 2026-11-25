using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PrintPrice;
using PrintPrice.Web;

// batch-2 (issue #10 follow-up) on the website: quantity discount (Settings boxes, price line / tooltip, breakdown, quotes,
// Copy text, records), plates printed (status texts, +1 / –1, selection line, JSON / backup with the desktop), and plates
// that mix colours read from a file (FixSingleFilamentPlates, the plates list's heading and dots).
static class Batch2Tests
{
    static Action<string, bool> True;
    static Action<string, double, double> Eq;

    public static void Run(Action<string, bool> t, Action<string, double, double> eq)
    {
        True = t; Eq = eq;
        bool wasDanish = L.Danish;
        L.Danish = false;
        try { Boxes(); SettingsKeys(); PriceLine(); CopyText(); Quotes(); Records(); Progress(); MixedFile(); Danish(); }
        finally { L.Danish = wasDanish; }
    }

    static AppSettings Defaults()
    {
        bool up;
        var s = WebCalc.SettingsFromText(null, out up);
        WebCalc.ApplyAutoWear(s, "PLA");
        return s;
    }

    static AppSettings WithSteps()
    {
        var s = Defaults();
        s.QtyDiscountFrom1 = 10; s.QtyDiscountPct1 = 5;
        s.QtyDiscountFrom2 = 50; s.QtyDiscountPct2 = 10;
        return s;
    }

    static QuoteInput Copies(int copies, int perPlate)
    {
        var one = new QuoteInput { Hours = 1, Minutes = 0, FilamentGrams = 20, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2 };
        return WebBatch.Totals(one, WebBatch.OneCopy(copies, perPlate, 0), 0, 0, null);
    }

    static void Boxes()
    {
        double f, p; bool bf, bp;
        True("step: both empty = off", WebCalc.ReadQtyStep("", " ", out f, out p, out bf, out bp) && f == 0 && p == 0 && !bf && !bp);
        True("step: 0 and 0 = off", WebCalc.ReadQtyStep("0", "0", out f, out p, out bf, out bp) && f == 0 && p == 0);
        True("step: 10 pcs 5 %", WebCalc.ReadQtyStep("10", "5", out f, out p, out bf, out bp) && f == 10 && p == 5);
        True("step: 7,5 % kept", WebCalc.ReadQtyStep("20", "7,5", out f, out p, out bf, out bp) && p == 7.5);
        True("step: % alone = From red", !WebCalc.ReadQtyStep("", "5", out f, out p, out bf, out bp) && bf && !bp);
        True("step: From alone = % red", !WebCalc.ReadQtyStep("10", "", out f, out p, out bf, out bp) && !bf && bp);
        True("step: 1 pc / 2,5 pcs red", !WebCalc.ReadQtyStep("1", "5", out f, out p, out bf, out bp) && bf
             && !WebCalc.ReadQtyStep("2,5", "5", out f, out p, out bf, out bp) && bf);
        True("step: 91 % / -5 % / x red", !WebCalc.ReadQtyStep("10", "91", out f, out p, out bf, out bp) && bp
             && !WebCalc.ReadQtyStep("10", "-5", out f, out p, out bf, out bp) && bp
             && !WebCalc.ReadQtyStep("10", "x", out f, out p, out bf, out bp) && bp);
        True("step: 90 % is the top", WebCalc.ReadQtyStep("2", "90", out f, out p, out bf, out bp) && p == 90);
        True("box text: on step shows, half step empty", WebCalc.QtyBoxText(10, 5, true) == "10" && WebCalc.QtyBoxText(10, 5, false) == "5"
             && WebCalc.QtyBoxText(10, 0, true) == "" && WebCalc.QtyBoxText(0, 5, false) == "");
        True("bad text = desktop's", WebCalc.QtyBadText() == "Quantity discount: from 2 pcs, up to 90 %, or both empty.");
    }

    static void SettingsKeys()
    {
        var s = WithSteps();
        s.QtyDiscountWho = 1; s.ShareLastPlate = 1;
        bool up;
        var back = WebCalc.SettingsFromText(WebCalc.SettingsToText(s), out up);
        True("settings text keeps the steps, Who and the desktop's ShareLastPlate", back.QtyDiscountFrom1 == 10 && back.QtyDiscountPct1 == 5
             && back.QtyDiscountFrom2 == 50 && back.QtyDiscountPct2 == 10 && back.QtyDiscountFrom3 == 0 && back.QtyDiscountWho == 1 && back.ShareLastPlate == 1);
        var d = Defaults();
        True("defaults: all steps off, customers only", d.QtyDiscountFrom1 == 0 && d.QtyDiscountPct1 == 0 && d.QtyDiscountWho == 2 && d.ShareLastPlate == 0);
        var reset = WebStatus.Defaults(back);
        True("Reset to defaults: steps off, Who 2; ShareLastPlate (not on a web tab) stays", reset.QtyDiscountFrom1 == 0 && reset.QtyDiscountPct2 == 0
             && reset.QtyDiscountWho == 2 && reset.ShareLastPlate == 1);
        // off = every price exactly as before
        var tot = Copies(40, 4);
        var a = Pricing.Calculate(tot, Defaults());
        True("off: no discount", a.DiscountPct == 0 && a.OthersDiscount == 0 && a.FriendsDiscount == 0
             && !Pricing.Breakdown(tot, Defaults(), false, null).Lines.Any(l => l.Key == QuoteLine.Discount));
    }

    static void PriceLine()
    {
        var s = WithSteps();
        var tot = Copies(40, 4);
        var r = Pricing.Calculate(tot, s);
        True("40 pcs: the 5 % step, customers only", r.DiscountPct == 5 && r.DiscountFrom == 10 && r.OthersDiscount > 0 && r.FriendsDiscount == 0);
        string each = History.EachText(r.OthersPrice, 40);
        True("price line: '… each · –5 %'", WebCalc.DiscountMark(each, r, false) == each + " · –5 %");
        True("friends line unchanged", WebCalc.DiscountMark("x", r, true) == "x");
        string tip = WebCalc.DiscountTip(r, false);
        True("tooltip: before discount + the step (= MainForm.DiscountTip): " + tip.Replace("\n", " | "),
             tip == "\nBefore discount: " + Num.Kr(r.OthersPrice + r.OthersDiscount) + "\nQuantity discount 5 % (from 10 pcs): –" + Num.Kr(r.OthersDiscount));
        True("no tooltip lines for friends", WebCalc.DiscountTip(r, true) == "");
        var r60 = Pricing.Calculate(Copies(60, 4), s);
        True("60 pcs: the 10 % step", r60.DiscountPct == 10 && r60.DiscountFrom == 50);
        True("9 pcs: none", Pricing.Calculate(Copies(9, 3), s).DiscountPct == 0);
        // "How the price is made": the negative line, the lines still add up
        var b = Pricing.Breakdown(tot, s, false, null);
        var dl = b.Lines.FirstOrDefault(l => l.Key == QuoteLine.Discount);
        True("breakdown: discount line, name and detail", dl != null && dl.Amount < 0 && WebCalc.DiscountLineName(dl) == "Quantity discount 5 %"
             && WebCalc.DiscountLineDetail(dl) == "from 10 pcs" && WebCalc.Amount(dl.Amount).StartsWith("–") && WebCalc.Amount(12.5) == Num.Kr(12.5));
        long sum = 0;
        foreach (var l in b.Lines) sum += l.Ore;
        True("breakdown lines with the discount sum to the shown price", sum == b.TotalOre && b.TotalOre == Pricing.ShownOre(b.Price));
        s.QtyDiscountWho = 1;
        var both = Pricing.Calculate(tot, s);
        True("both prices: friends get it too", both.FriendsDiscount > 0 && WebCalc.DiscountMark("x", both, true) == "x · –5 %");
        // Copy text (shared JobText, 13 arguments)
        string jt = QuoteRows.JobText("", "Hook", tot.Hours, tot.Minutes, "800", 0, r.FriendsPrice, r.OthersPrice, 40, tot.Plates, true,
                                      r.FriendsDiscount > 0 ? r.DiscountPct : 0, r.OthersDiscount > 0 ? r.DiscountPct : 0);
        True("Copy text: customer each incl. 5 % quantity discount, friends without", jt.Contains("Customer: " + Num.Kr(r.OthersPrice) + " " + History.EachInclDiscountText(r.OthersPrice, 40, 5))
             && jt.Contains("incl. 5 % quantity discount") && !jt.Split('\n').First(x => x.StartsWith("Friends")).Contains("discount"));
    }

    static void CopyText()
    {
        var s = WithSteps();
        var tot = Copies(40, 4);
        var r = Pricing.Calculate(tot, s);
        string t = QuoteRows.JobText("", "Hook", tot.Hours, tot.Minutes, "800", 0, r, 40, tot.Plates, true);
        var lines = t.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        True("web Copy text: customer with the shared result-based suffix, friends plain (" + lines[lines.Length - 1] + ")",
             lines[lines.Length - 1] == "Customer: " + Num.Kr(r.OthersPrice) + " " + History.EachInclDiscountText(r.OthersPrice, 40, r, false)
             && lines[lines.Length - 2] == "Friends & family: " + Num.Kr(r.FriendsPrice) + QuoteRows.EachSuffix(r.FriendsPrice, 40, true)
             && t.StartsWith(QuoteRows.JobText("", "Hook", tot.Hours, tot.Minutes, "800", 0, r.FriendsPrice, r.OthersPrice, 40, tot.Plates, true).Split('\n')[0]));
        True("real cut: the % shown", Pricing.ShownDiscountPct(r, false) == 5 ? t.Contains("incl. 5 % quantity discount") : t.Contains("incl. quantity discount"));
        s.RoundTo = 5;
        var rr = Pricing.Calculate(tot, s);
        string tr = QuoteRows.JobText("", "Hook", tot.Hours, tot.Minutes, "800", 0, rr, 40, tot.Plates, true);
        True("rounding changed the cut: " + (Pricing.ShownDiscountPct(rr, false) == 0 ? "no %" : "% kept (rounding added nothing)"),
             Pricing.ShownDiscountPct(rr, false) == 0 ? tr.Contains("incl. quantity discount") && !tr.Contains("incl. 5 %") : tr.Contains("incl. 5 % quantity discount"));
        if (Pricing.ShownDiscountPct(rr, false) == 0)
            True("rounding changed the cut: no '–5 %' mark, tooltip 'Incl. quantity discount (from 10 pcs)'",
                 WebCalc.DiscountMark("x", rr, false) == "x" && WebCalc.DiscountTip(rr, false) == "\nIncl. quantity discount (from 10 pcs)");
        var none = Pricing.Calculate(tot, Defaults());
        string tn = QuoteRows.JobText("", "Hook", tot.Hours, tot.Minutes, "800", 0, none, 40, tot.Plates, true);
        True("no discount: = JobText", tn == QuoteRows.JobText("", "Hook", tot.Hours, tot.Minutes, "800", 0, none.FriendsPrice, none.OthersPrice, 40, tot.Plates, true));
    }

    static void Quotes()
    {
        var s = WithSteps();
        var tot = Copies(40, 4);
        var d = WebQuote.FromJob(new WebQuote.Job { Input = tot, Model = "Hook.gcode.3mf" }, s, false, DateTime.Now, "2026-1", "");
        var rows = WebQuote.Rows(d);
        var sub = rows.FirstOrDefault(r => r.Subtotal);
        var disc = rows.FirstOrDefault(r => r.Label.StartsWith("Quantity discount"));
        True("job quote: 'Price before discount' + 'Quantity discount 5 % (40 pcs)' with a minus amount",
             sub != null && sub.Label == "Price before discount" && disc != null && disc.Label == "Quantity discount 5 % (40 pcs)" && disc.Amount.StartsWith("–")
             && disc.Amount.EndsWith(" kr."));
        var lay = WebQuote.Draw(d, true);
        True("A4 page draws both rows, the minus amount as is", lay.Items.Exists(i => i.Text == "Price before discount")
             && lay.Items.Exists(i => i.Text == "Quantity discount 5 % (40 pcs)") && lay.Items.Exists(i => i.Text == disc.Amount));
        True("total = after discount; each line after discount", Math.Abs(d.Breakdown.Total - Pricing.Calculate(tot, s).OthersPrice) < 0.006
             && d.EachLine.StartsWith(History.EachText(d.Breakdown.Total, 40)));
        var fr = WebQuote.FromJob(new WebQuote.Job { Input = tot, Model = "Hook" }, s, true, DateTime.Now, "2026-1", "");
        True("friends quote (customers only): no discount rows", !WebQuote.Rows(fr).Any(r => r.Subtotal || r.Label.StartsWith("Quantity discount")));
        // a saved record: the discount it got (SavedSettings), on the quote and the combined bill
        var rec = WebHistory.MakeRecord(tot, Pricing.Calculate(tot, s), s, "Hook", DateTime.Now, null, -1, null, null);
        History.SetCopies(rec, 40, tot.Plates, 4, true);
        rec.Id = "q1";
        var none = Defaults();
        var rd = WebQuote.FromRecord(rec, none, false, "Lene", false, DateTime.Now, null);
        True("record quote keeps its discount when the steps are off today", WebQuote.Rows(rd).Any(r => r.Label == "Quantity discount 5 % (40 pcs)")
             && Math.Abs(rd.Breakdown.Total - rec.OthersPrice) < 0.006);
        var other = WebHistory.MakeRecord(Copies(3, 3), Pricing.Calculate(Copies(3, 3), s), s, "Clip", DateTime.Now.AddMinutes(-1), null, -1, null, null);
        History.SetCopies(other, 3, 1, 3, true);
        other.Id = "q2";
        var g = WebQuote.FromRecords(new List<QuoteRecord> { rec, other }, none, false, "Lene", false, DateTime.Now, DateTime.Now, null);
        var marks = new Dictionary<QuoteRow, string>();
        var grows = WebQuote.GroupRows(g, false, marks);
        var gd = grows.FirstOrDefault(r => r.Label.StartsWith("Quantity discount"));
        True("combined bill: one discount row with a minus amount", gd != null && gd.Amount.StartsWith("–") && grows.Any(r => r.Subtotal));
        var pages = WebQuote.DrawPages(g, true);
        True("combined bill pages draw it", pages.Any(pg => pg.Items.Exists(i => i.Text == gd.Amount)));
        string rt = QuoteRows.RecordText(rec, false, false, none, "");
        True("record Copy text: incl. 5 % quantity discount", rt.Contains("incl. 5 % quantity discount"));
    }

    static void Records()
    {
        var s = WithSteps();
        var tot = Copies(40, 4);
        var rec = WebHistory.MakeRecord(tot, Pricing.Calculate(tot, s), s, "Hook", DateTime.Now, null, -1, null, null);
        History.SetCopies(rec, 40, tot.Plates, 4, true);
        True("MakeRecord (FromQuote): the discount snapshot, plates not counted", rec.QtyDiscountPct == 5 && rec.QtyDiscountFrom == 10 && rec.QtyDiscountWho == 2
             && rec.PlatesDone == -1 && History.DiscountPctFor(rec, false) == 5 && History.DiscountPctFor(rec, true) == 0);
        History.SetPlatesDone(rec, 3, DateTime.Now);
        string json = History.ToJson(new List<QuoteRecord> { rec });
        True("JSON: desktop keys", json.Contains("\"QtyDiscountPct\":5") && json.Contains("\"QtyDiscountFrom\":10") && json.Contains("\"PlatesDone\":3"));
        string problem, badKey;
        var back = WebHistory.ReadStored(json, DateTime.Now, out problem, out badKey);
        True("stored round trip", problem == null && back.Count == 1 && back[0].PlatesDone == 3 && back[0].QtyDiscountPct == 5 && back[0].QtyDiscountFrom == 10
             && History.ToJson(back) == json);
        string old = json.Replace("\"PlatesDone\":3,", "").Replace("\"QtyDiscountPct\":5,", "").Replace("\"QtyDiscountFrom\":10,", "").Replace("\"QtyDiscountWho\":2,", "");
        True("test removed the keys", !old.Contains("PlatesDone") && !old.Contains("QtyDiscount"));
        var ob = WebHistory.ReadStored(old, DateTime.Now, out problem, out badKey);
        True("older record (no keys): not counted, no discount, same price", problem == null && ob[0].PlatesDone == -1 && ob[0].QtyDiscountPct == 0
             && ob[0].QtyDiscountWho == 2 && Math.Abs(ob[0].OthersPrice - rec.OthersPrice) < 1e-9);
        BackupData data;
        string backup = History.ToBackup(new List<QuoteRecord> { rec }, s, "web", DateTime.Now);
        True("backup (web -> desktop format) keeps plates printed, the discount and the settings' steps",
             History.ReadBackup(backup, out data) == null && data.Quotes[0].PlatesDone == 3 && data.Quotes[0].QtyDiscountPct == 5
             && data.Settings != null && data.Settings.Contains("QtyDiscountFrom2=50"));
        // a desktop record written by the desktop's serializer has the same keys: read here as is
        string desk = "{\"Version\":1,\"Quotes\":[{\"Id\":\"d1\",\"SavedAt\":\"2026-11-24 10:00:00\",\"Hours\":20,\"Grams\":400,\"OthersPrice\":690.84,\"FriendsPrice\":500," +
                      "\"Quantity\":40,\"Plates\":5,\"PerPlate\":8,\"PlatesDone\":2,\"QtyDiscountPct\":5,\"QtyDiscountFrom\":10,\"QtyDiscountWho\":2,\"Printed\":true}]}";
        var dl = History.ParseJson(desk);
        True("desktop history.json: 2 of 5 plates, 5 % discount", dl != null && dl[0].PlatesDone == 2 && History.PlatesOf(dl[0]) == 5
             && History.PrintStateOf(dl[0], DateTime.Now) == PrintState.Printing && History.DiscountPctFor(dl[0], false) == 5);
    }

    static QuoteRecord Job(string id, int plates, int copies)
    {
        var s = Defaults();
        var tot = Copies(copies, Math.Max(1, (copies + plates - 1) / plates));
        var r = WebHistory.MakeRecord(tot, Pricing.Calculate(tot, s), s, "Job " + id, DateTime.Now.AddMinutes(-10), null, -1, null, null);
        History.SetCopies(r, copies, plates, Math.Max(1, (copies + plates - 1) / plates), true);
        r.Id = id;
        History.SetPrintState(r, PrintState.NotPrinted, DateTime.Now);
        return r;
    }

    static void Progress()
    {
        DateTime now = DateTime.Now;
        var a = Job("a", 5, 20);
        var b = Job("b", 2, 4);
        var c = Job("c", 1, 1);
        True("not printed: plain status, no plates text", WebHistory.PlatesStatusText(a, now) == null && History.PlatesDoneOf(a, now) == 0);
        var jobs = WebHistory.PlateJobs(new List<QuoteRecord> { a, b, c });
        True("plate part: only jobs of 2+ plates", jobs.Count == 2 && jobs.Contains(a) && jobs.Contains(b));
        True("+1 on, –1 off at 0", WebHistory.CanStepPlates(new List<QuoteRecord> { a }, 1, now) && !WebHistory.CanStepPlates(new List<QuoteRecord> { a }, -1, now));
        string line = WebHistory.StepPlates(new List<QuoteRecord> { a }, 1, now);
        line = WebHistory.StepPlates(new List<QuoteRecord> { a }, 1, now);
        line = WebHistory.StepPlates(new List<QuoteRecord> { a }, 1, now);
        True("+1 x 3: '✓ Plates printed: 3 of 5.', Printing, cell '3 of 5 plates', tip", line == "✓ Plates printed: 3 of 5." && a.PlatesDone == 3
             && History.PrintStateOf(a, now) == PrintState.Printing && WebHistory.PlatesStatusText(a, now) == "3 of 5 plates"
             && WebHistory.StatusTip(a, now) == "Plates printed: 3 of 5");
        line = WebHistory.StepPlates(new List<QuoteRecord> { a }, -1, now);
        True("–1: 2 of 5", line == "✓ Plates printed: 2 of 5." && a.PlatesDone == 2);
        History.SetPrintState(a, PrintState.Printing, now);
        DateTime end;
        True("printing now: 'Printing plate 3 of 5, done about …' (one plate's time)", History.PrintingEnds(a, out end)
             && WebHistory.StatusTip(a, now) == "Printing plate 3 of 5, done about " + end.ToString("HH':'mm", System.Globalization.CultureInfo.InvariantCulture)
             || (end.Date != now.Date && WebHistory.StatusTip(a, now).StartsWith("Printing plate 3 of 5, done about ")));
        History.SetPrintState(a, PrintState.Printed, now);
        True("Printed: all 5, normal name again, +1 off", History.PlatesDoneOf(a, now) == 5 && WebHistory.PlatesStatusText(a, now) == null
             && !WebHistory.CanStepPlates(new List<QuoteRecord> { a }, 1, now));
        // the selection line: 2+ ticked with a multi-plate job
        History.SetPlatesDone(a, 3, now);
        History.SetPrintState(b, PrintState.Printed, now);
        var ticked = new HashSet<QuoteRecord> { a, b, c };
        True("selection: the shared text, 3 prints, 5 of 8 (" + WebHistory.SelectionProgress(ticked, now) + ")",
             WebHistory.SelectionProgress(ticked, now) == History.SelectedProgressText(3, 5, 8));
        True("selection: one ticked / only 1-plate jobs = no line", WebHistory.SelectionProgress(new HashSet<QuoteRecord> { a }, now) == null
             && WebHistory.SelectionProgress(new HashSet<QuoteRecord> { c, Job("d", 1, 1) }, now) == null);
        // two jobs stepped together (the selection bar's menu)
        var two = new List<QuoteRecord> { a, b };
        True("+1 on the bar: b is all done, a goes to 4", WebHistory.StepPlates(two, 1, now) == "✓ Plates printed: 6 of 7." && a.PlatesDone == 4 && History.PlatesDoneOf(b, now) == 2);
        History.SetPrintState(b, PrintState.Failed, now);
        True("failed job: left out of the plate part", WebHistory.PlateJobs(two).Count == 1);
        True("minus plate text (qol #7)", History.MinusPlateText() == "One plate fewer printed");
    }

    // online-names.md's sample: plate 2 is blue only, reported under slot 1 by Bambu Studio's CLI
    const string SliceXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<config>\n<plate><metadata key=\"index\" value=\"1\"/><metadata key=\"prediction\" value=\"2244\"/><metadata key=\"weight\" value=\"30.00\"/>" +
        "<object identify_id=\"1\" name=\"a.stl_1\" skipped=\"false\" /><filament id=\"1\" type=\"PLA\" color=\"#FF0000\" used_m=\"3.3\" used_g=\"10.00\" />" +
        "<filament id=\"2\" type=\"PLA\" color=\"#0000FF\" used_m=\"5.6\" used_g=\"20.00\" /></plate>\n<plate><metadata key=\"index\" value=\"2\"/><metadata key=\"prediction\" value=\"857\"/>" +
        "<metadata key=\"weight\" value=\"\"/><object identify_id=\"2\" name=\"b.stl_1\" skipped=\"false\" /><object identify_id=\"3\" name=\"b.stl_2\" skipped=\"false\" />" +
        "<filament id=\"1\" type=\"PLA\" color=\"#FF0000\" used_m=\"1.93\" used_g=\"5.86\" /></plate>\n</config>\n";
    const string ModelXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<config>\n" +
        "<object id=\"2\"><metadata key=\"name\" value=\"a.stl_1\"/><metadata key=\"extruder\" value=\"1\"/><part id=\"1\" subtype=\"normal_part\"><metadata key=\"name\" value=\"a.stl\"/></part></object>\n" +
        "<object id=\"3\"><metadata key=\"name\" value=\"c\"/><metadata key=\"extruder\" value=\"2\"/></object>\n" +
        "<object id=\"4\"><metadata key=\"name\" value=\"b.stl_1\"/><metadata key=\"extruder\" value=\"2\"/></object>\n" +
        "<object id=\"5\"><metadata key=\"name\" value=\"b.stl_2\"/><metadata key=\"extruder\" value=\"2\"/></object>\n" +
        "<plate><metadata key=\"plater_id\" value=\"1\"/><model_instance><metadata key=\"object_id\" value=\"2\"/></model_instance><model_instance><metadata key=\"object_id\" value=\"3\"/></model_instance></plate>\n" +
        "<plate><metadata key=\"plater_id\" value=\"2\"/><model_instance><metadata key=\"object_id\" value=\"4\"/></model_instance><model_instance><metadata key=\"object_id\" value=\"5\"/></model_instance></plate>\n</config>\n";
    const string ProjectJson = "{\"filament_density\":[\"1.26\",\"1.5\"],\"filament_colour\":[\"#FF0000\",\"#0000FF\"],\"filament_type\":[\"PLA\",\"PLA\"]," +
        "\"filament_settings_id\":[\"Bambu PLA Basic @BBL A1 #1\",\"Bambu PLA Basic @BBL A1 #2\"],\"printer_model\":\"Bambu Lab A1\"}";

    static FileJob ReadZip(Dictionary<string, string> entries)
    {
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var kv in entries)
                {
                    var e = zip.CreateEntry(kv.Key, CompressionLevel.Optimal);
                    using (var w = e.Open()) { var b = Encoding.UTF8.GetBytes(kv.Value); w.Write(b, 0, b.Length); }
                }
            return SlicedFile.Read(new BytesSource(ms.ToArray()), "Mixed.gcode.3mf", "Bambu PLA Basic", false).GetAwaiter().GetResult();
        }
    }

    static void MixedFile()
    {
        var job = ReadZip(new Dictionary<string, string> {
            { "Metadata/slice_info.config", SliceXml }, { "Metadata/plate_1.gcode", "G1 X1\n" }, { "Metadata/plate_2.gcode", "G1 X1\n" },
            { "Metadata/model_settings.config", ModelXml }, { "Metadata/project_settings.config", ProjectJson } });
        var res = job.Result;
        True("file read: 2 plates", res != null && job.Error == null && res.PlateList.Count == 2);
        if (res == null || res.PlateList.Count != 2) return;
        var p2 = res.PlateList[1];
        True("one-filament plate fixed (= desktop ReadSliceInfo): slot 2, 6,98 g", res.SingleFilamentFixes == 1 && p2.Filaments.Count == 1
             && p2.Filaments[0].Slot == 2 && Math.Abs(p2.Filaments[0].Grams - 6.98) < 0.01);
        var s2 = res.Filaments.FirstOrDefault(f => f.Slot == 2);
        True("file totals: slot 2 26,98 g, profile without the slot tag", s2 != null && Math.Abs(s2.Grams - 26.98) < 0.01 && s2.Profile == "Bambu PLA Basic @BBL A1");
        True("plate names still read (model_settings read once)", p2.Objects.Count == 2);
        True("1 plate with mixed colours", WebBatch.MixedPlates(res.PlateList) == 1 && BambuSlicer.IsMixedPlate(res.PlateList[0]) && !BambuSlicer.IsMixedPlate(p2));
        True("heading: '2 plates, 1 with mixed colours – the price is for both:'", WebBatch.PlatesHeading(2, 1) == "2 plates, 1 with mixed colours – the price is for both:");
        True("heading without mixed plates: a dash too (ui-text #5)", WebBatch.PlatesHeading(2, 0) == "2 plates – the price is for both:"
             && WebBatch.PlatesHeading(4, 0) == "4 plates – the price is for all of them:" && WebBatch.PlatesHeading(4, 2) == "4 plates, 2 with mixed colours – the price is for all of them:");
        var dots = WebBatch.PlateColours(res.PlateList[0]);
        True("plate dots: red, blue (slot order)", dots.Count == 2 && dots[0] == "#FF0000" && dots[1] == "#0000FF" && WebBatch.PlateColours(p2).SequenceEqual(new[] { "#0000FF" }));
        True("plain one-colour file: nothing mixed", WebBatch.MixedPlates(null) == 0 && WebBatch.PlateColours(null).Count == 0);
    }

    static void Danish()
    {
        L.Danish = true;
        try
        {
            var s = WithSteps();
            var tot = Copies(40, 4);
            var r = Pricing.Calculate(tot, s);
            True("DA tooltip: Før rabat / Mængderabat 5 % (fra 10 stk.)", WebCalc.DiscountTip(r, false).Contains("Før rabat: ")
                 && WebCalc.DiscountTip(r, false).Contains("Mængderabat 5 % (fra 10 stk.): –"));
            var d = WebQuote.FromJob(new WebQuote.Job { Input = tot, Model = "Krog" }, s, false, DateTime.Now, "2026-1", "");
            True("DA quote rows: Pris før rabat, Mængderabat 5 % (40 stk.)", WebQuote.Rows(d).Any(x => x.Label == "Pris før rabat")
                 && WebQuote.Rows(d).Any(x => x.Label == "Mængderabat 5 % (40 stk.)"));
            var a = Job("da", 5, 20);
            History.SetPlatesDone(a, 3, DateTime.Now);
            True("DA: 3 af 5 plader, Plader printet, Én plade færre printet", WebHistory.PlatesStatusText(a, DateTime.Now) == "3 af 5 plader"
                 && WebHistory.StatusTip(a, DateTime.Now) == "Plader printet: 3 af 5" && History.MinusPlateText() == "Én plade færre printet");
            True("DA heading", WebBatch.PlatesHeading(4, 1) == "4 plader, 1 med blandede farver – prisen gælder dem alle:"
                 && WebBatch.PlatesHeading(4, 0) == "4 plader – prisen gælder dem alle:");
            True("DA settings line", WebCalc.QtyBadText() == "Mængderabat: fra 2 stk., højst 90 %, eller begge tomme.");
        }
        finally { L.Danish = false; }
    }
}
