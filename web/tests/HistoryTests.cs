using System;
using System.Collections.Generic;
using PrintPrice;
using PrintPrice.Web;

// Stage 3a: the web's price history (WebHistory) on HistoryForm's 8 picture-mode sample quotes, saving, Open again /
// Repeat print values, stored text, and the linked QuoteRows file (compiled with WEB).
static class HistoryTests
{
    // = HistoryForm.SampleQuotes() (English), the quotes in the desktop's history picture
    internal static List<QuoteRecord> Samples()
    {
        var l = new List<QuoteRecord>();
        Action<string, string, string, int, int, double, double, double, int, string> add = (when, model, src, h, m, g, fr, cu, who, note) =>
            l.Add(new QuoteRecord
            {
                Id = "sample" + l.Count, SavedAt = when, Model = model, Source = src, Hours = h, Minutes = m, Grams = g,
                FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 3.65, PrinterWatts = 100, FriendsMarkupPct = 25, OthersMarkupPct = 75,
                FriendsPrice = fr, OthersPrice = cu, For = who, Note = note, Printed = true,
                MaterialCost = Math.Round(g * 0.2, 2), PowerCost = Math.Round((h + m / 60.0) * 0.365, 2), MachineRatePerHour = 1.71
            });
        int F = History.ForFriends, C = History.ForCustomer;
        add("2026-11-03 14:12:00", "3D BENCHY", "https://www.printables.com/model/3161-3d-benchy", 0, 38, 11.8, 4.32, 5.62, F, "Mads, red PLA");
        add("2026-11-02 19:40:00", "Four-Color Multicolor Test benchy", "https://www.printables.com/model/1648230-four-color-multicolor-test-benchy", 10, 43, 135.85, 57.18, 72.72, C, "Webshop #112, paid");
        add("2026-10-30 10:05:00", "Phone stand (adjustable)", @"C:\Models\phone_stand.3mf", 2, 5, 48.2, 16.56, 21.76, C, "Lene, black");
        add("2026-10-27 21:18:00", "Cable clips ×20", @"C:\Models\cable_clip.stl", 1, 12, 22.4, 8.20, 10.66, F, "Dad, garage");
        add("2026-10-24 16:51:00", "Planter pot 12 cm", @"C:\Models\planter.stl", 6, 30, 164, 55.08, 72.67, C, "Café Bønne, 2 pcs");
        add("2026-10-19 12:33:00", "Headphone hook", @"C:\Models\headphone_hook.stl", 1, 48, 38.6, 13.55, 17.74, C, "");
        add("2026-10-12 20:02:00", "Articulated dragon", "https://www.printables.com/model/1234-articulated-dragon", 4, 55, 92.3, 33.73, 43.85, F, "Emma's birthday");
        add("2026-10-05 09:47:00", "Lithophane lamp", @"C:\Models\lithophane.3mf", 9, 20, 120.5, 50.34, 64.10, C, "Not paid yet");
        l[5].Note = "Layer shift at 80 %";
        History.MarkFailed(l[5], true, new DateTime(2026, 10, 19, 15, 10, 0));
        l[2].Customer = "Lene Hansen"; l[2].InvoiceNo = "2026-K7Q4"; l[2].BilledAt = "2026-10-31 09:12:00";
        l[4].Customer = "Café Bønne"; l[4].InvoiceNo = "2026-3HWN"; l[4].BilledAt = "2026-10-25 10:30:00";
        l[7].Printed = false;
        l[7].Note = "Quote sent, waiting";
        return l;
    }

    public static void Run(Action<string, bool> True, Action<string, double, double> Eq)
    {
        bool wasDanish = L.Danish;
        L.Danish = false;
        var list = Samples();
        var oct = new DateTime(2026, 10, 1);

        // filters (HistoryForm.Matches)
        Func<WebHistory.Filter, int> count = f => WebHistory.Apply(list, f, null).Count;
        True("start month = newest (Nov 2026)", WebHistory.StartMonth(list) == new DateTime(2026, 11, 1));
        True("October: 6 quotes", count(new WebHistory.Filter { Month = oct }) == 6);
        True("all months, friends: 3", count(new WebHistory.Filter { Show = WebHistory.ShowFriends }) == 3);
        True("all months, customers: 5", count(new WebHistory.Filter { Show = WebHistory.ShowCustomers }) == 5);
        True("failed: 1", count(new WebHistory.Filter { Show = WebHistory.ShowFailed }) == 1);
        True("not printed: 1", count(new WebHistory.Filter { Show = WebHistory.ShowNotPrinted }) == 1);
        True("search customer name 'hansen': 1", count(new WebHistory.Filter { Query = "hansen" }) == 1);
        True("search invoice code: 1", count(new WebHistory.Filter { Query = "k7q4" }) == 1);
        True("search link: 1", count(new WebHistory.Filter { Query = "1648230" }) == 1);
        True("search in another month: 0", count(new WebHistory.Filter { Month = oct, Query = "benchy" }) == 0);
        True("count text '6 of 8 quotes'", WebHistory.CountText(6, 8) == "6 of 8 quotes" && WebHistory.CountText(1, 1) == "1 quote");

        // month cards for October (= the History mock-up's numbers)
        var cards = WebHistory.Cards(WebHistory.Totals(list, oct, null));
        Func<int, string> v = i => cards[i].Value + " | " + cards[i].Sub;
        True("card prints: " + v(0), v(0) == "4 | + 1 failed");
        True("card time: " + v(1), v(1) == "14 h 42 m | 327 g filament");
        True("card customers: " + v(2), v(2) == "94,43 kr. | 2 prints" && cards[2].Css == "red");
        True("card friends: " + v(3), v(3) == "41,93 kr. | 2 prints" && cards[3].Css == "green");
        var t = WebHistory.Totals(list, oct, null);
        True("card costs: " + v(4), v(4) == Num.Kr(t.Costs + t.Lost) + " | incl. " + Num.Kr(t.Lost) + " failed");
        True("card profit: " + v(5), cards[5].Value == Num.Kr(t.Profit) && cards[5].Sub.EndsWith("% of income"));
        Console.WriteLine("History cards (October 2026): " + v(0) + " / " + v(1) + " / " + v(2) + " / " + v(3) + " / " + v(4) + " / " + v(5));
        var all = WebHistory.Totals(list, DateTime.MinValue, null);
        double sumProfit = 0;
        foreach (var x in History.MonthSummaries(list)) sumProfit += x.Profit;
        Eq("all months profit = sum of months", sumProfit, all.Profit);
        True("one printer: no profit tooltip", WebHistory.ProfitTip(list, new AppSettings(), oct, false, null) == null);

        // list texts
        True("date text", WebHistory.DateText(list[2]) == "30-10-2026 10:05");
        True("short date", WebHistory.ShortDate(list[2]) == "30 Oct 10:05");
        True("time text", WebHistory.TimeText(list[2]) == "2 h 5 m");
        True("price follows For", WebHistory.PriceText(list[0]) == "4,32" && WebHistory.PriceText(list[1]) == "72,72");
        True("model text: file name of a path", WebHistory.ModelText(new QuoteRecord { Source = @"C:\Models\a b.3mf" }) == "a b.3mf");
        True("model text: no name", WebHistory.ModelText(new QuoteRecord()) == "(no name)");
        True("month text", WebHistory.MonthText(oct) == "October 2026");

        // saving (MainForm.SaveToHistory): same quote within 10 minutes replaces; a repeat is a new record
        var s = new AppSettings { PrinterWatts = 100, FriendsMarkupPct = 25, OthersMarkupPct = 75, MachineRatePerHour = 2, AutoWearRate = 0,
                                  FailedPrintPct = 10, MinimumPrice = 0, RoundTo = 0, FailedPayback = 0 };
        var input = new QuoteInput { Hours = 5, Minutes = 30, FilamentGrams = 120, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2.00 };
        var res = Pricing.Calculate(input, s);
        var hist = new List<QuoteRecord>();
        var mat = new List<HistoryFilament> { WebHistory.SingleMaterial(null, null, "PETG") };
        var a = WebHistory.Save(hist, WebHistory.MakeRecord(input, res, s, "Benchy.gcode.3mf", new DateTime(2026, 11, 10, 12, 0, 0), mat, -1, null, null));
        a.Customer = "Mette"; a.For = History.ForFriends;
        var b = WebHistory.Save(hist, WebHistory.MakeRecord(input, res, s, "Benchy.gcode.3mf", new DateTime(2026, 11, 10, 12, 5, 0), mat, -1, null, null));
        True("same quote again replaces it (Id, For, name kept)", hist.Count == 1 && b.Id == a.Id && b.For == History.ForFriends && b.Customer == "Mette");
        True("saved line: type from the page, no name", b.Filaments.Count == 1 && b.Filaments[0].Type == "PETG" && b.Filaments[0].Name == "");
        True("web record has no source path", b.Source == "" && b.Model == "Benchy.gcode.3mf");
        var rep = WebHistory.Save(hist, WebHistory.MakeRecord(input, res, s, "Benchy.gcode.3mf", new DateTime(2026, 11, 10, 12, 6, 0), mat, -1, null, b));
        True("repeat print = new record with RepeatOf and the same For", hist.Count == 2 && rep.RepeatOf == b.Id && rep.For == History.ForFriends && rep.Id != b.Id);
        var reo = WebHistory.MakeRecord(input, res, s, "x", new DateTime(2026, 11, 11, 9, 0, 0), mat, -1, b, null);
        True("re-saved reopened quote keeps For", reo.For == History.ForFriends && reo.RepeatOf == "");

        // multi-colour: colours, types, profiles and purge survive the history.json text (the desktop reads the same)
        var two = new QuoteInput { Hours = 1, Minutes = 0, FilamentGrams = 30, ElectricityDkkPerKwh = 2,
                                   Filaments = new List<FilamentLine> { new FilamentLine { Grams = 20, PricePerKg = 200 }, new FilamentLine { Grams = 10, PricePerKg = 250 } } };
        var mats = new List<HistoryFilament> { new HistoryFilament { Colour = "#FF0000", Type = "PLA", Name = "Bambu PLA Basic @BBL A1" },
                                               new HistoryFilament { Colour = "#00FF00", Type = "PETG", Name = "Bambu PETG HF @BBL A1" } };
        var mr = WebHistory.MakeRecord(two, Pricing.Calculate(two, s), s, "two.gcode.3mf", new DateTime(2026, 11, 10, 13, 0, 0), mats, 3.5, null, null);
        var back = History.ParseJson(History.ToJson(new List<QuoteRecord> { mr }));
        var matte = WebHistory.MakeRecord(input, res, s, "m.gcode.3mf", new DateTime(2026, 11, 10, 13, 30, 0),
                                          new List<HistoryFilament> { new HistoryFilament { Colour = "#808080", Type = "PLA", Name = "Bambu PLA Matte @BBL A1" } }, -1, null, null);
        True("saved filament name has no printer suffix", matte.Filaments.Count == 1 && matte.Filaments[0].Name == "Bambu PLA Matte");
        True("multi-colour record round trip", back != null && back[0].Filaments.Count == 2 && back[0].Filaments[1].Colour == "#00FF00"
             && back[0].Filaments[1].Name == "Bambu PETG HF" && back[0].PurgeGrams == 3.5 && back[0].Filaments[0].PricePerKg == 200);

        // Open again / Repeat print values (MainForm.FillFromRecord)
        var today = new AppSettings { LastFilamentPrice = 190 };
        today.RememberPrice("Bambu PLA Basic", 210);
        var open = WebHistory.FillFrom(back[0], false, today);
        True("open again: saved kr/kg per colour and saved electricity", open.Rows.Count == 2 && open.Rows[0].PerKg == "200" && open.Rows[1].PerKg == "250"
             && open.Rows[0].Name == "Bambu PLA Basic" && open.Elec == "2,00" && open.Hours == "1" && open.Grams == "30" && open.PurgeGrams == 3.5);
        var again = WebHistory.FillFrom(back[0], true, today);
        True("repeat: today's kr/kg (remembered, else last) and today's electricity", again.Rows[0].PerKg == "210" && again.Rows[1].PerKg == "190" && again.Elec == null);
        var oneOpen = WebHistory.FillFrom(b, false, today);
        True("one colour: saved kr/kg, type kept, no material name", oneOpen.Rows.Count == 0 && oneOpen.PerKg == "180" && oneOpen.Single != null
             && oneOpen.Single.Type == "PETG" && oneOpen.MaterialName == null);
        True("one colour repeat without a material: last kr/kg", WebHistory.FillFrom(b, true, today).PerKg == "190");
        var old = WebHistory.FillFrom(list[2], false, today);
        True("old record (no lines): its kr/kg, no material", old.PerKg == "200" && old.Single == null && old.Elec == "3,65");

        string main, muted;
        WebHistory.ReopenLines(list[2], null, out main, out muted);
        True("reopen line: " + main, main == "↻ Opened from history: \"Phone stand (adjustable)\", saved 30-10-2026 10:05." && muted.StartsWith("Saved price 16,56 / 21,76 kr. — now –"));
        WebHistory.RepeatLines(list[2], new QuoteResult { FriendsPrice = 17, OthersPrice = 22.10 }, out main, out muted);
        True("repeat lines: " + muted, main.StartsWith("↻ Repeat print of \"Phone stand (adjustable)\" (first quoted 30-10-2026)")
             && muted == "Was 21,76 kr. → now 22,10 kr. (+0,34 kr.), customer price");

        // a repeat's dropped file keeps the saved materials
        var slice = new SliceResult();
        slice.Filaments.Add(new FilamentUse { Slot = 1, Profile = "Generic PLA", Type = "PLA" });
        slice.Filaments.Add(new FilamentUse { Slot = 2, Profile = "Generic PLA", Type = "PLA" });
        WebHistory.KeepSavedMaterials(slice, back[0]);
        True("repeat file keeps saved materials", slice.Filaments[0].Profile == "Bambu PLA Basic" && slice.Filaments[1].Type == "PETG");
        var sm = WebHistory.SingleMaterial(new FilamentUse { Colour = "#123456", Profile = "", Type = "" }, "Bambu PLA Matte", "PLA");
        True("one colour from a file without a profile: Settings' filament", sm.Name == "Bambu PLA Matte" && sm.Colour == "#123456" && sm.Type == "PLA");

        // payback row: a failed print in the history adds a payback to the next quote
        var pay = new AppSettings { FailedPayback = 1, FailedPaybackPct = 10, FailedPaybackWho = 2, MinimumPrice = 0, RoundTo = 0 };
        var debtList = new List<QuoteRecord> { b };
        History.MarkFailed(b, true, new DateTime(2026, 11, 10, 14, 0, 0));
        var pin = new QuoteInput { Hours = 5, Minutes = 30, FilamentGrams = 120, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2, PaybackDebt = History.FailedDebt(debtList, null) };
        var pr = Pricing.Calculate(pin, pay);
        True("payback row shown with a failed print: " + WebHistory.PaybackCaption(pr), pr.PaybackCost > 0 && WebHistory.PaybackCaption(pr) == "Failed prints payback – customers only"
             && WebHistory.PaybackAmount(pr).Contains("left"));
        pin.PaybackDebt = 0;
        True("no payback row without a debt", WebHistory.PaybackCaption(Pricing.Calculate(pin, pay)) == null);

        // stored text
        string problem, bad;
        True("nothing stored = empty history", WebHistory.ReadStored(null, DateTime.Now, out problem, out bad).Count == 0 && problem == null && bad == null);
        var now = new DateTime(2026, 11, 10, 15, 4, 5);
        True("garbage kept aside", WebHistory.ReadStored("{oops", now, out problem, out bad).Count == 0 && bad == "history.bad-20261110-150405" && problem != null);
        True("newer version kept aside", WebHistory.ReadStored("{\"Version\":2,\"Quotes\":[]}", now, out problem, out bad).Count == 0 && bad != null);
        True("stored history read", WebHistory.ReadStored(History.ToJson(list), now, out problem, out bad).Count == 8 && bad == null);
        var orphans = WebHistory.OrphanPictures(new[] { "pic:sample1", "pic:gone", "history", "pic:" }, list);
        True("orphan pictures", orphans.Count == 2 && orphans.Contains("pic:gone") && orphans.Contains("pic:"));

        // Danish texts
        L.Danish = true;
        True("DA month", WebHistory.MonthText(oct) == "Oktober 2026");
        True("DA count", WebHistory.CountText(6, 8) == "6 af 8 tilbud");
        True("DA short date", WebHistory.ShortDate(list[2]) == "30. okt. 10:05");
        True("DA card prints sub", WebHistory.Cards(WebHistory.Totals(list, oct, null))[0].Sub == "+ 1 fejlede");
        L.Danish = false;

        // src\QuoteRows.cs compiles with WEB and gives the desktop's texts
        True("QuoteRows.FileName", QuoteRows.FileName(new DateTime(2026, 11, 4), "3D Benchy", "Mette: \"H\"") == "2026-11-04 3D Benchy - Mette_ _H_");
        True("QuoteRows.MaterialLine", QuoteRows.MaterialLine(new[] { "PLA", "PETG", "PLA" }, 4, null) == "PLA + PETG, 4 colours");
        True("QuoteRows.LongDate", QuoteRows.LongDate(new DateTime(2026, 11, 4)) == "4 November 2026");
        var qb = Pricing.Breakdown(input, s, false, null);
        var qrows = QuoteRows.Build(qb, null, -1, 5, 30, "");
        True("QuoteRows.Build rows", qrows.Count > 2 && qrows[0].Label == "Material");

        L.Danish = wasDanish;
    }
}
