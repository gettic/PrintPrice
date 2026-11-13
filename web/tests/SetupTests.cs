using System;
using System.Collections.Generic;
using PrintPrice;
using PrintPrice.Web;

// Website first-time setup (idea 19): who sees it, what the answers save (through the shared AppSettings.ApplySetup), Set up
// later, re-run, restore, the example and the texts. The apply rules themselves are tested once, in tests\PricingTests.cs.
static class SetupTests
{
    static Action<string, bool> True;
    static Action<string, double, double> Eq;

    public static void Run(Action<string, bool> t, Action<string, double, double> e)
    {
        True = t; Eq = e;
        bool danish = L.Danish;
        try
        {
            L.Danish = false;
            NewVisitor();
            StoredFlag();
            FirstRun();
            ReRun();
            Later();
            Boxes();
            Example();
            Restore();
            Texts();
        }
        finally { L.Danish = danish; }
    }

    static void NewVisitor()
    {
        True("setup: nothing stored = new visitor", WebSetup.IsNewVisitor(null, null, null, 0, null, true));
        True("setup: empty strings = new visitor", WebSetup.IsNewVisitor("", "", "", 0, null, true));
        True("setup: stored settings = not new", !WebSetup.IsNewVisitor("Language=", null, null, 0, null, true));
        True("setup: stored page prefs = not new", !WebSetup.IsNewVisitor(null, "x", null, 0, null, true));
        True("setup: a backup note = not new", !WebSetup.IsNewVisitor(null, null, "2026-11-13 10:00|file", 0, null, true));
        True("setup: 1 quote = not new", !WebSetup.IsNewVisitor(null, null, null, 1, null, true));
        True("setup: history problem = not new", !WebSetup.IsNewVisitor(null, null, null, 0, "unreadable", true));
        True("setup: no browser storage = no setup", !WebSetup.IsNewVisitor(null, null, null, 0, null, false));

        bool up;
        var s = WebCalc.SettingsFromText(null, out up);
        True("setup: FromIni(null) alone = SetupDone 1", !WebSetup.ShouldShow(s));
        True("setup: new install quote name empty", s.QuoteName == "");
        WebSetup.MarkNewVisitor(s, true);
        True("setup: new visitor -> shows", WebSetup.ShouldShow(s));
        var s2 = WebCalc.SettingsFromText(null, out up);
        WebSetup.MarkNewVisitor(s2, false);
        True("setup: not new -> doesn't show", !WebSetup.ShouldShow(s2));
        True("setup: ShouldShow(null) false", !WebSetup.ShouldShow(null));
    }

    static void StoredFlag()
    {
        bool up;
        var old = WebCalc.SettingsFromText("FriendsMarkupPct=30\nSettingsVersion=2", out up);
        True("setup: old stored text without the key = 1 (existing visitors never see it)", old.SetupDone == 1 && !WebSetup.ShouldShow(old));
        var s = new AppSettings();
        s.SetupDone = 0;
        var back = WebCalc.SettingsFromText(WebCalc.SettingsToText(s), out up);
        True("setup: SetupDone=0 round trip shows again", WebSetup.ShouldShow(back));
        s.SetupDone = 1;
        back = WebCalc.SettingsFromText(WebCalc.SettingsToText(s), out up);
        True("setup: SetupDone=1 round trip", !WebSetup.ShouldShow(back));
        var desk = WebCalc.SettingsFromText("SetupDone=1\nQuoteName=Anna", out up);
        True("setup: desktop text SetupDone=1", desk.SetupDone == 1 && desk.QuoteName == "Anna");
        True("setup: modes", WebSetup.Mode(false) == SetupPrinterMode.Replace && WebSetup.Mode(true) == SetupPrinterMode.ChangeModel);
    }

    static AppSettings NewVisitorSettings()
    {
        bool up;
        var s = WebCalc.SettingsFromText(null, out up);
        WebSetup.MarkNewVisitor(s, true);
        return s;
    }

    static void FirstRun()
    {
        var start = NewVisitorSettings();
        string before = WebCalc.SettingsToText(start);
        var a = SetupAnswers.From(start, WebSetup.Mode(false));
        var r = WebSetup.Result(start, a, false, "PLA");
        True("setup: all skipped = defaults, SetupDone 1", r.SetupDone == 1 && r.FriendsMarkupPct == start.FriendsMarkupPct
             && r.PrinterModel == start.PrinterModel && r.QuoteName == "");
        True("setup: start settings not changed", WebCalc.SettingsToText(start) == before);

        a.Language = "da";
        a.Models = new List<string> { Wear.ModelP1S };
        a.FilamentText = "250";
        a.LivePower = false; a.ElecText = "1,5"; a.Area = "DK2";
        a.FriendsText = "30"; a.OthersText = "80,5"; a.MinText = "0"; a.QuoteName = "  Lene's Prints  ";
        r = WebSetup.Result(start, a, false, "PLA");
        True("setup: language", r.Language == "da");
        True("setup: printer P1S", r.PrinterModel == Wear.ModelP1S && r.PrinterCount == 1);
        Eq("setup: P1S watts", 130, r.PrinterWatts);
        True("setup: P1S auto wear", r.AutoWearRate != 0);
        Eq("setup: P1S wear = web auto rate", WebCalc.WearBreakdown(r, "PLA").Total, r.MachineRatePerHour);
        Eq("setup: filament", 250, r.LastFilamentPrice);
        Eq("setup: filament remembered", 250, r.PriceFor(r.FilamentPreset));
        True("setup: fixed power", r.AutoPowerPrice == 0 && r.PriceArea == "DK2");
        Eq("setup: electricity", 1.5, r.LastElectricityPrice);
        Eq("setup: friends", 30, r.FriendsMarkupPct);
        Eq("setup: customers", 80.5, r.OthersMarkupPct);
        Eq("setup: minimum off", 0, r.MinimumPrice);
        True("setup: name trimmed", r.QuoteName == "Lene's Prints");
        True("setup: saved text keeps SetupDone=1", WebCalc.SettingsToText(r).Contains("SetupDone=1"));

        a.LivePower = true; a.ElecText = "9";
        r = WebSetup.Result(start, a, false, "PLA");
        True("setup: live keeps the last price", r.AutoPowerPrice == 1 && r.LastElectricityPrice == start.LastElectricityPrice);
    }

    static void ReRun()
    {
        var start = new AppSettings();
        var p = start.ActivePrinter;
        p.Name = "Workshop"; p.Watts = 95;
        start.SetPrinter(p);
        start.AddPrinter("", Wear.ModelP1S);
        var opening = SetupAnswers.From(start, WebSetup.Mode(true));
        True("setup re-run: opening model = active", opening.Models.Count == 1 && opening.Models[0] == Wear.ModelA1);
        var r = WebSetup.Result(start, opening, true, "PLA");
        True("setup re-run: Save unchanged keeps printers", r.PrinterCount == 2 && r.PrinterName == "Workshop" && r.PrinterWatts == 95);

        var a = opening.Clone();
        a.Models = new List<string> { Wear.ModelA1Mini };
        r = WebSetup.Result(start, a, true, "PLA");
        True("setup re-run: model changed in place", r.PrinterModel == Wear.ModelA1Mini && r.PrinterCount == 2 && r.ActivePrinterId == start.ActivePrinterId);
        True("setup re-run: own name and watts kept", r.PrinterName == "Workshop" && r.PrinterWatts == 95);

        var plain = new AppSettings();
        a = SetupAnswers.From(plain, WebSetup.Mode(true));
        a.Models = new List<string> { Wear.ModelP1S };
        r = WebSetup.Result(plain, a, true, "PLA");
        True("setup re-run: stock printer follows the model (Settings' Model box)", r.PrinterName == "Bambu Lab P1S" && r.PrinterWatts == 130);
    }

    static void Later()
    {
        var start = NewVisitorSettings();
        var opening = SetupAnswers.From(start, WebSetup.Mode(false));
        var a = opening.Clone();
        a.Language = "da";
        a.Models = new List<string> { Wear.ModelA1Mini };
        a.FriendsText = "99";
        var done = new bool[SetupAnswers.Questions];
        done[SetupAnswers.StepLanguage] = true;
        done[SetupAnswers.StepPrinter] = true;   // the prices step not finished with Next
        var later = WebSetup.Later(a, opening, done);
        var r = WebSetup.Result(start, later, false, "PLA");
        True("setup later: finished steps kept", r.Language == "da" && r.PrinterModel == Wear.ModelA1Mini);
        Eq("setup later: unfinished step = default", start.FriendsMarkupPct, r.FriendsMarkupPct);
        True("setup later: SetupDone 1 (no nagging)", r.SetupDone == 1);
        True("setup later: answers not changed", a.FriendsText == "99");
        r = WebSetup.Result(start, WebSetup.Later(a, opening, null), false, "PLA");
        True("setup later: nothing finished = all defaults", r.Language == "" && r.PrinterModel == start.PrinterModel);
    }

    static void Boxes()
    {
        var a = SetupAnswers.From(new AppSettings(), SetupPrinterMode.Replace);
        True("setup boxes: opening OK", WebSetup.BadBoxes(a, SetupAnswers.StepCosts).Count == 0 && WebSetup.BadBoxes(a, SetupAnswers.StepPrices).Count == 0);
        a.FilamentText = "abc"; a.LivePower = false; a.ElecText = "-1";
        var bad = WebSetup.BadBoxes(a, SetupAnswers.StepCosts);
        True("setup boxes: filament + elec red", bad.Contains("filament") && bad.Contains("elec"));
        a.LivePower = true;
        True("setup boxes: live = elec box not checked", !WebSetup.BadBoxes(a, SetupAnswers.StepCosts).Contains("elec"));
        a.FriendsText = ""; a.OthersText = "10000000"; a.MinText = "5,5";
        bad = WebSetup.BadBoxes(a, SetupAnswers.StepPrices);
        True("setup boxes: empty / too big red, 5,5 fine", bad.Contains("friends") && bad.Contains("others") && !bad.Contains("min"));
        True("setup boxes: other steps none", WebSetup.BadBoxes(a, SetupAnswers.StepLanguage).Count == 0);
    }

    static void Example()
    {
        var start = NewVisitorSettings();
        var a = SetupAnswers.From(start, SetupPrinterMode.Replace);
        a.Models = new List<string> { Wear.ModelP1S };
        a.FilamentText = "200"; a.LivePower = false; a.ElecText = "2";
        a.FriendsText = "25"; a.OthersText = "75"; a.MinText = "0";
        var ex = WebSetup.Example(start, a, false, "PLA", -1);
        var s = WebSetup.Result(start, a, false, "PLA");
        var want = Pricing.Calculate(new QuoteInput { Hours = 8, FilamentGrams = 150, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 2 }, s);
        True("setup example: computed", ex != null);
        Eq("setup example: friends = Pricing.Calculate", want.FriendsPrice, ex.FriendsPrice);
        Eq("setup example: customers = Pricing.Calculate", want.OthersPrice, ex.OthersPrice);
        Eq("setup example: material 30", 30, ex.MaterialCost);
        Eq("setup example: power 0,13 kW x 8 h x 2", 2.08, Math.Round(ex.PowerCost, 2));

        a.LivePower = true;
        var live = WebSetup.Example(start, a, false, "PLA", 3);
        Eq("setup example: live price used", Math.Round(0.13 * 8 * 3, 2), Math.Round(live.PowerCost, 2));
        var noLive = WebSetup.Example(start, a, false, "PLA", -1);
        Eq("setup example: no live price = last price", Math.Round(0.13 * 8 * start.LastElectricityPrice, 2), Math.Round(noLive.PowerCost, 2));
        a.FriendsText = "x";
        True("setup example: bad box = none", WebSetup.Example(start, a, false, "PLA", 3) == null);

        foreach (var m in Wear.Models)
        {
            var b = SetupAnswers.From(start, SetupPrinterMode.Replace);
            b.Models = new List<string> { m.Key };
            var r = WebSetup.Result(start, b, false, "PLA");
            Eq("setup example: " + m.Key + " watts", m.DefaultWatts, r.PrinterWatts);
        }
    }

    static void Restore()
    {
        // a backup made by a set-up visitor, restored in a new visitor's setup
        var other = new AppSettings();
        other.FriendsMarkupPct = 33;
        var quotes = new List<QuoteRecord>();
        string text = WebBackup.FileText(quotes, other, new DateTime(2026, 11, 13, 10, 0, 0));
        BackupData data;
        True("setup restore: backup read", WebBackup.ReadText(text, out data) == null);
        var here = NewVisitorSettings();
        string problem;
        var res = WebBackup.Combine(new List<QuoteRecord>(), here, data, false, true, out problem);
        True("setup restore: with settings = done, setup closes", problem == null && res.SettingsRestored && !WebSetup.ShouldShow(res.Settings));
        Eq("setup restore: restored value", 33, res.Settings.FriendsMarkupPct);
        res = WebBackup.Combine(new List<QuoteRecord>(), here, data, false, false, out problem);
        True("setup restore: without settings = setup stays", problem == null && !res.SettingsRestored && WebSetup.ShouldShow(res.Settings));
    }

    static void Texts()
    {
        var s = new AppSettings();
        var skipped = new bool[SetupAnswers.Questions];
        skipped[SetupAnswers.StepPrices] = true;
        foreach (bool da in new[] { false, true })
        {
            L.Danish = da;
            string lang = da ? "DA " : "EN ";
            for (int i = 0; i <= WebSetup.Summary; i++)
            {
                True("setup text " + lang + "title " + i, WebSetup.StepTitle(i, false).Length > 0 && WebSetup.StepTitle(i, true).Length > 0);
                True("setup text " + lang + "count " + i, WebSetup.StepCount(i).Length > 0);
            }
            var rows = WebSetup.SummaryRows(s, da, skipped, false);
            True("setup text " + lang + "7 summary rows", rows.Count == 7);
            True("setup text " + lang + "skipped rows muted", rows[4].Muted && rows[5].Muted && rows[6].Muted && !rows[0].Muted);
            True("setup text " + lang + "default marker", rows[4].Value.EndsWith(da ? "(standard)" : "(default)"));
            True("setup text " + lang + "re-run marker", WebSetup.SummaryRows(s, da, skipped, true)[4].Value.EndsWith(da ? "(uændret)" : "(unchanged)"));
            True("setup text " + lang + "language row", rows[0].Value == (da ? "Dansk" : "English"));
            True("setup text " + lang + "live price row", rows[3].Value.Contains("DK1"));
            True("setup text " + lang + "empty name", rows[6].Value.StartsWith(da ? "intet" : "none"));
            foreach (var m in Wear.Models) True("setup text " + lang + "tile " + m.Key, WebSetup.ModelWatts(m) == Num.Fmt(m.DefaultWatts) + " W" && WebSetup.ModelRate(m).EndsWith(da ? "kr/t slid" : "kr/h wear"));
            True("setup text " + lang + "live asking", WebSetup.LiveText(null, 0.55, true) == (da ? "Henter prisen nu…" : "Getting the price now…"));
            True("setup text " + lang + "live failed", WebSetup.LiveText(new LivePower.Result(), 0.55, true).Contains(da ? "næste gang du åbner siden" : "next time you open the page"));
            var ok = new LivePower.Result { Ok = true, Total = 2.87 };
            True("setup text " + lang + "live ok: " + WebSetup.LiveText(ok, 0.55, true), WebSetup.LiveText(ok, 0.55, true).Contains("2,87") && WebSetup.LiveText(ok, 0.55, true).Contains("0,55"));
            True("setup text " + lang + "area names", WebSetup.AreaName("DK2").StartsWith("DK2") && WebSetup.AreaName("x").StartsWith("DK1"));
        }
        L.Danish = false;
        Eq("setup text: summary step index", 4, WebSetup.Summary);
    }
}
