using System;
using System.Collections.Generic;
using PrintPrice;
using PrintPrice.Web;

// Combined bills on the website, step 1 (WebQuoteGroup.cs): which jobs go on the bill, the page's data and layout, the
// export (one invoice number on all jobs), texts in both languages. The browser side is in plans\web-followups\test\run.mjs.
static class GroupTests
{
    static AppSettings Bs()
    {
        var s = new AppSettings();
        s.PrinterWatts = 0; s.FriendsMarkupPct = 0; s.OthersMarkupPct = 0; s.MachineRatePerHour = 0; s.AutoWearRate = 0;
        s.FailedPrintPct = 0; s.FailedPayback = 0; s.MinimumPrice = 50; s.RoundTo = 1; s.QuoteName = "Test 3D";
        s.InvoiceDueDays = 14; s.PayMobilePay = "12 34 56 78";
        return s;
    }

    static QuoteRecord Job(double grams, DateTime t, string customer)
    {
        var s = Bs();
        var input = new QuoteInput { Hours = 1, Minutes = 0, FilamentGrams = grams, FilamentDkkPerKg = 1000, ElectricityDkkPerKwh = 0 };
        var r = History.FromQuote(input, Pricing.Calculate(input, s), s, "Job" + grams, "", t);
        r.Customer = customer;
        return r;
    }

    public static void Run(Action<string, bool> True, Action<string, double, double> Eq)
    {
        bool wasDanish = L.Danish;
        L.Danish = false;
        var s = Bs();
        var t = new DateTime(2026, 11, 19, 10, 0, 0);
        var a = Job(18, t.AddHours(-3), "Lene Hansen");
        var b = Job(14, t.AddHours(-2), "Lene Hansen");
        var c = Job(20, t.AddHours(-1), "Bo");
        var list = new List<QuoteRecord> { c, b, a };   // newest first, as History keeps it

        // opening: oldest first, names warned, one job = the single window
        var o = WebQuote.OpenRecords(list, new List<QuoteRecord> { c, a, b }, true, s);
        True("open: 3 jobs oldest first", o.Problem == null && o.Jobs != null && o.Jobs.Count == 3 && o.Jobs[0] == a && o.Jobs[2] == c);
        True("open: most used name first", o.StartName == "Lene Hansen");
        True("open: different names warned", o.Warn && o.Notes.Count == 1 && o.Notes[0].Contains("Lene Hansen, Bo"));
        True("open: one = single", WebQuote.OpenRecords(list, new List<QuoteRecord> { a }, true, s).Single == a);
        c.Failed = true;
        o = WebQuote.OpenRecords(list, new List<QuoteRecord> { a, b, c }, false, s);
        True("open: failed left off", o.Jobs.Count == 2 && o.Notes.Contains(WebQuote.FailedLeftOutText(1)));
        o = WebQuote.OpenRecords(list, new List<QuoteRecord> { a, c }, false, s);
        True("open: failed + 1 left = single", o.Single == a);
        o = WebQuote.OpenRecords(list, new List<QuoteRecord> { c }, false, s);
        True("open: one failed alone = single", o.Single == c);
        c.Failed = false;

        // the page: cards add up to the total; 18 + 14 + 20 = 52 >= 50, no top-up
        var sheet = WebQuote.FromRecords(new List<QuoteRecord> { a, b, c }, s, false, "Lene", true, t, t, null);
        True("sheet: group", WebQuote.IsGroup(sheet) && sheet.Jobs.Count == 3);
        long sum = 0; foreach (var j in sheet.Jobs) sum += j.Ore;
        True("sheet: cards sum to the total", sum == 5200 && Math.Abs(sheet.GroupTotal - 52) < 1e-9);
        True("sheet: new invoice", sheet.Invoice && sheet.InvoiceNo == "(new)");
        True("sheet: time summed", sheet.Hours == 3 && sheet.Minutes == 0);
        var two = WebQuote.FromRecords(new List<QuoteRecord> { a, b }, s, false, "", false, t, t, null);
        long sum2 = 0; foreach (var j in two.Jobs) sum2 += j.Ore;
        True("sheet: top-up shared over the cards (18 + 14 -> 50)", sum2 == 5000 && two.TopUpAmount != null && two.Jobs[0].Ore > 1800);
        var lay = WebQuote.Draw(sheet, true);
        True("layout: one A4 page for 3", lay.Height == WebQuote.A4H);
        bool frame = false, cube = false;
        foreach (var i in lay.Items) { if (i.Kind == "frame") frame = true; if (i.Kind == "cube") cube = true; }
        True("layout: card frames and cubes", frame && cube);
        var four = WebQuote.FromRecords(new List<QuoteRecord> { a, b, c, Job(9, t, "") }, s, false, "", false, t, t, null);
        WebQuote.Draw(four, true);
        True("layout: 4 prints = one row of 4", four.GroupLevel >= 2);
        var many = new List<QuoteRecord>();
        for (int i = 0; i < 9; i++) many.Add(Job(10 + i, t.AddMinutes(i), ""));
        var nine = WebQuote.FromRecords(many, s, false, "", false, t, t, null);
        var pages9 = WebQuote.DrawPages(nine, true);
        True("layout: 9 prints = A4 pages, each A4 tall", pages9.Count >= 2 && pages9.TrueForAll(p => p.Height == WebQuote.A4H));
        var twelve = new List<QuoteRecord>();
        for (int i = 0; i < 12; i++) twelve.Add(Job(10 + i, t.AddMinutes(i), ""));
        var inv12 = WebQuote.FromRecords(twelve, s, false, "Test Kunde", true, t, t, null);
        True("12 prints: 3 rows + note, then the last row + total (as the desktop): " + WebQuote.PageLayout(inv12), WebQuote.PageLayout(inv12) == "0-9+note | 9-12+total");
        var p12 = WebQuote.DrawPages(inv12, true);
        string p1 = string.Join("|", p12[0].Items.ConvertAll(i => i.Text).ToArray()), p2 = string.Join("|", p12[1].Items.ConvertAll(i => i.Text).ToArray());
        True("12: page 1 note + Page 1 of 2, no total", p1.Contains("Breakdown and total: next page →") && p1.Contains("Page 1 of 2") && !p1.Contains("Total · 12 prints") && p1.Contains("Bill to"));
        True("12: page 2 top line, total, Page 2 of 2", p2.Contains("Invoice no. (new)  ·  Test Kunde  ·  19 November 2026") && p2.Contains("Total · 12 prints") && p2.Contains("Page 2 of 2") && !p2.Contains("Bill to"));
        for (int n = 2; n <= 6; n++)
        {
            var sub = WebQuote.FromRecords(twelve.GetRange(0, n), s, false, "X", n % 2 == 0, t, t, null);
            True(n + " prints: one page: " + WebQuote.PageLayout(sub), WebQuote.PageLayout(sub) == "0-" + n + "+total");
        }
        True("single: one page", WebQuote.PageLayout(WebQuote.FromRecords(new List<QuoteRecord> { a }, s, false, "", false, t, t, null)) == "single");

        // export: an invoice for all at once
        bool friends = false;
        var jobs = new List<QuoteRecord> { a, b, c };
        True("export: name required", WebQuote.ApplyGroupExport(list, jobs, true, false, ref friends, " ", t, s) == WebQuote.NeedName);
        True("export: made", WebQuote.ApplyGroupExport(list, jobs, true, false, ref friends, "Lene", t, s) == null);
        string code = a.InvoiceNo;
        True("export: one number on all", !string.IsNullOrEmpty(code) && b.InvoiceNo == code && c.InvoiceNo == code && WebQuote.AllMade(jobs));
        True("export: name on all", a.Customer == "Lene" && c.Customer == "Lene");
        True("export: printed", History.PrintStateOf(a, t) == PrintState.Printed);
        var made = WebQuote.FromRecords(jobs, s, false, "Lene", true, t, t, null);
        True("made: its number", made.InvoiceNo == code);
        True("made: file name", WebQuote.GroupFileName(made) == QuoteRows.InvoiceFileName(code, "Lene"));
        True("made: copy text has the number", WebQuote.GroupText(jobs, made, false, s).Contains(code));
        friends = true;
        True("re-issue: same number, price kept", WebQuote.ApplyGroupExport(list, jobs, true, false, ref friends, "Other", t, s) == null
             && a.InvoiceNo == code && !friends && a.Customer == "Lene");
        o = WebQuote.OpenRecords(list, new List<QuoteRecord> { b }, true, s);
        True("single invoice on a combined invoice: the whole bill", o.Jobs != null && o.Jobs.Count == 3 && o.Made && o.Notes.Count == 1);
        True("single quote of it: its own window", WebQuote.OpenRecords(list, new List<QuoteRecord> { b }, false, s).Single == b);
        var d = Job(30, t, "X");
        list.Insert(0, d);
        o = WebQuote.OpenRecords(list, new List<QuoteRecord> { a, d }, true, s);
        True("mix of billed and unbilled refused", o.Problem != null && o.Problem.Contains(code));
        o = WebQuote.OpenRecords(list, new List<QuoteRecord> { a, b }, true, s);
        True("part of an invoice: the whole invoice", o.Jobs.Count == 3 && o.Notes.Count == 1);
        True("group help made", WebQuote.GroupHelpText(3, true, true, code).StartsWith("Invoice " + code + " (3 prints) is already made"));
        True("done text", WebQuote.GroupDoneText(false, true, code, 3, true) == "✓ Print window opened. Invoice no. " + code + " is in the history (3 prints).");

        // quote file name, colour names
        var q = WebQuote.FromRecords(new List<QuoteRecord> { a, b }, s, false, "Lene", false, t, t, null);
        True("quote file name", WebQuote.GroupFileName(q) == QuoteRows.GroupFileName(t, 2, "Lene"));
        True("colour name", WebQuote.ColourName("#c0301f") == "Red" && WebQuote.ColourName("") == "");
        var names = new List<string> { "PLA", "PLA", "PETG" };
        WebQuote.NameByColour(names, new List<string> { "#000000", "#ffffff", "#ff0000" });
        True("same names get colours", names[0] == "PLA (Black)" && names[1] == "PLA (White)" && names[2] == "PETG");

        var di = WebQuote.Details(WebQuote.FromRecord(a, s, false, "Lene", true, t, null));
        var dq = WebQuote.Details(WebQuote.FromRecord(a, s, false, "Lene", false, t, null));
        True("single invoice: Bill to on top, then Print time, Weight, Material", di.Count == 4 && di[0][0] == "Bill to" && di[0][1] == "Lene" && di[1][0] == "Print time" && di[2][0] == "Weight" && di[3][0] == "Material");
        True("single quote: Print time, Weight, Printer, Material", dq[0][0] == "Print time" && dq[2][0] == "Printer" && dq[3][0] == "Material");
        L.Danish = true;
        True("DA failed left out", WebQuote.FailedLeftOutText(2) == "2 fejlede print er udeladt af regningen.");
        True("DA saved", WebQuote.GroupSavedText(3).StartsWith("✓ Gemt i historikken"));
        L.Danish = wasDanish;
    }
}
