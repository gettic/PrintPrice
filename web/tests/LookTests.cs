using System;
using System.Collections.Generic;
using System.Linq;
using PrintPrice;
using PrintPrice.Web;

// web-look (plans\web-look\report.md, qol 2026-11-21): a scaled job's name everywhere (History.WithScale), the combined
// bill's materials grouped per job with each job's own tower note, the "Unpaid invoices" line, no empty room under a
// single invoice's name.
static class LookTests
{
    static Action<string, bool> True;

    public static void Run(Action<string, bool> t, Action<string, double, double> eq)
    {
        True = t;
        bool wasDanish = L.Danish;
        L.Danish = false;
        try { Scale(); Grouped(); Unpaid(); Gap(); }
        finally { L.Danish = wasDanish; }
    }

    static QuoteRecord Rec(string id, string model, int minsAgo, string invoiceNo = "", string paidAt = "")
    {
        var s = new AppSettings();
        var input = new QuoteInput { Hours = 1, FilamentGrams = 30, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 2 };
        var r = History.FromQuote(input, Pricing.Calculate(input, s), s, model, "", DateTime.Now.AddMinutes(-minsAgo));
        r.Id = id;
        if (invoiceNo.Length > 0) { r.InvoiceNo = invoiceNo; r.BilledAt = History.Stamp(DateTime.Now.AddMinutes(-minsAgo)); r.Printed = true; }
        r.PaidAt = paidAt;
        return r;
    }

    static void Scale()
    {
        var r = Rec("s", "3D Benchy", 5, "2026-33333");
        History.SetScale(r, 150, 100, 50);
        var s = new AppSettings();
        True("History list name keeps the scale: " + WebHistory.ModelText(r), WebHistory.ModelText(r) == "3D Benchy (150 × 100 × 50 %)"
             && WebHistory.BaseModelText(r) == "3D Benchy" && WebHistory.ScalePart(r) == " (150 × 100 × 50 %)");
        True("invoice / quote page name has the scale", WebQuote.FromRecord(r, s, false, "", true, DateTime.Now, null).Model == "3D Benchy (150 × 100 × 50 %)"
             && WebQuote.RecordModelName(r) == "3D Benchy (150 × 100 × 50 %)");
        var plain = Rec("p", "Vase", 6);
        True("not scaled: name unchanged", WebHistory.ModelText(plain) == "Vase" && WebHistory.ScalePart(plain) == "" && WebQuote.RecordModelName(plain) == "Vase");
        var other = Rec("o", "Hook", 7, "2026-33333");
        var g = WebQuote.FromRecords(new List<QuoteRecord> { r, other }, s, false, "", true, DateTime.Now, DateTime.Now, null);
        True("combined bill card has the scale", g.Jobs.Any(j => j.Name == "3D Benchy (150 × 100 × 50 %)"));
        var open = WebQuote.OpenRecords(new List<QuoteRecord> { r, other }, new List<QuoteRecord> { r }, true, s);
        True("\"is on combined invoice\" note has the scale", open.Notes.Count == 1 && open.Notes[0].StartsWith("\"3D Benchy (150 × 100 × 50 %)\" is on combined invoice 2026-33333"));
    }

    static void Grouped()
    {
        var s = new AppSettings { QuoteShowTower = 1 };
        var a = Rec("a", "Cable clip", 30, "2026-52001");
        a.Filaments = new List<HistoryFilament> { new HistoryFilament { Grams = 30, PricePerKg = 200, Name = "Bambu PLA Matte", Colour = "", Type = "PLA" } };
        var b = Rec("b", "Two colour cubes", 20, "2026-52001");
        b.Filaments = new List<HistoryFilament>
        {
            new HistoryFilament { Grams = 18, PricePerKg = 200, Name = "Bambu PLA Basic", Colour = "#FFFFFF", Type = "PLA", TowerGrams = 1.098 },
            new HistoryFilament { Grams = 12, PricePerKg = 200, Name = "Bambu PLA Basic", Colour = "#ED1C24", Type = "PLA", TowerGrams = 0.606 },
        };
        b.TowerGrams = 1.704; b.PrimeTower = 1;
        var jobs = new List<QuoteRecord> { a, b };
        var d = WebQuote.FromRecords(jobs, s, false, "", true, DateTime.Now, DateTime.Now, null);
        True("material owners: one job each", d.MaterialJob != null && string.Join(",", d.MaterialJob) == "0,1,1");
        var marks = new Dictionary<QuoteRow, string>();
        var rows = WebQuote.GroupRows(d, false, marks);
        string Lab(QuoteRow r) { string m; return (marks.TryGetValue(r, out m) ? m + ":" : r.Kind + ":") + r.Label; }
        int rule = rows.FindIndex(r => r.Kind == QuoteRowKind.Rule);
        string mat = string.Join(" | ", rows.Take(rule).Select(Lab));
        True("grouped: job name, its lines, its own tower note after its lines: " + mat,
             mat == "Head:Material | label:Cable clip | Sub:Bambu PLA Matte | label:Two colour cubes | Sub:Bambu PLA Basic (White) | Sub:Bambu PLA Basic (Red) | note:incl. 1,7 g for the prime tower");
        True("grouped: same øre as the flat list", rows.Where(r => r.Kind == QuoteRowKind.Head).Count() == QuoteRows.BuildGroup(d.Breakdown, d.MaterialNames, d.Hours, d.Minutes, d.PlateName, d.GroupSameRates, 6).Count(r => r.Kind == QuoteRowKind.Head));
        // a material both jobs use: one list as before (summed tower note at the end)
        var c = Rec("c", "Hook", 10, "2026-52001");
        c.Filaments = new List<HistoryFilament> { new HistoryFilament { Grams = 30, PricePerKg = 200, Name = "Bambu PLA Matte", Colour = "", Type = "PLA" } };
        var d2 = WebQuote.FromRecords(new List<QuoteRecord> { a, b, c }, s, false, "", true, DateTime.Now, DateTime.Now, null);
        var marks2 = new Dictionary<QuoteRow, string>();
        var rows2 = WebQuote.GroupRows(d2, false, marks2);
        True("shared material: not grouped, tower note summed", marks2.Count == 0 && rows2.Any(r => r.Kind == QuoteRowKind.Note && r.Label == "incl. 1,7 g for the prime tower"));
        // setting off: no tower notes, still grouped
        var off = WebQuote.FromRecords(jobs, new AppSettings(), false, "", true, DateTime.Now, DateTime.Now, null);
        var marks3 = new Dictionary<QuoteRow, string>();
        var rows3 = WebQuote.GroupRows(off, false, marks3);
        True("tower setting off: grouped, no tower note", marks3.Values.Count(v => v == "label") == 2 && !marks3.Values.Contains("note") && !rows3.Any(r => r.Label.Contains("prime tower")));
        // the drawn page has the labels
        bool drawn = WebQuote.DrawPages(d, false).Any(p => p.Items.Any(i => i.Text == "Two colour cubes" && i.Size == 14));
        True("page draws the job labels", drawn);
        L.Danish = true;
        var da = WebQuote.FromRecords(jobs, s, false, "", true, DateTime.Now, DateTime.Now, null);
        var marksDa = new Dictionary<QuoteRow, string>();
        True("DA: tower note under its job", WebQuote.GroupRows(da, false, marksDa).Any(r => marksDa.TryGetValue(r, out var m) && m == "note" && r.Label == "inkl. 1,7 g til primetårnet"));
        L.Danish = false;
    }

    static void Unpaid()
    {
        var now = DateTime.Now;
        var list = new List<QuoteRecord> { Rec("a", "Benchy", 10, "2026-48213"), Rec("b", "Phone stand", 20, "2026-48213"), Rec("c", "Hook", 30, "2026-11111", "2026-11-05"), Rec("d", "Vase", 40) };
        string overdue;
        string line = WebHistory.UnpaidLine(list, new AppSettings(), now, out overdue);
        True("unpaid line: " + line, line == "Unpaid: 1 invoice · " + Num.Kr(History.PriceFor(list[0]) + History.PriceFor(list[1])) && overdue == "");
        list[0].PaidAt = list[1].PaidAt = "2026-11-06";
        True("everything paid", WebHistory.UnpaidLine(list, new AppSettings(), now, out overdue) == "Everything is paid.");
        True("no invoices: no line", WebHistory.UnpaidLine(new List<QuoteRecord> { Rec("x", "Vase", 3) }, new AppSettings(), now, out overdue) == null);
        list[0].PaidAt = list[1].PaidAt = "";
        list[2].PaidAt = "";
        list[2].BilledAt = History.Stamp(now.AddDays(-60));
        L.Danish = true;
        line = WebHistory.UnpaidLine(list, new AppSettings(), now, out overdue);
        True("DA: 2 fakturaer, 1 forfalden: " + line + " " + overdue, line.StartsWith("Ubetalt: 2 fakturaer · ") && overdue == "· 1 forfalden");
        L.Danish = false;
    }

    static void Gap()
    {
        var r = Rec("g", "Hook", 5, "2026-11111");
        var lay = WebQuote.Draw(WebQuote.FromRecord(r, new AppSettings(), false, "Bo", true, DateTime.Now, null), false);
        var name = lay.Items.First(i => i.Kind == "wrap");
        var bill = lay.Items.First(i => i.Text == "Bill to");
        True("single invoice: details 26 units under the name (was ~65): " + (bill.Y - (name.Y + name.H)), bill.Y - (name.Y + name.H) < 45);
    }
}
