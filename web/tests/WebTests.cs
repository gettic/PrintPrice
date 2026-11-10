using System;
using System.Collections.Generic;
using PrintPrice;
using PrintPrice.Web;

// Web tests: the desktop price code as linked into the website (WEB defined) + the web's Json and WebCalc.
// Expected prices are the ones tests\PricingTests.cs asserts for the desktop build.
static class WebTests
{
    static int failed, passed;

    static void True(string name, bool ok)
    {
        if (ok) passed++; else { failed++; Console.WriteLine("FAIL " + name); }
    }

    static void Eq(string name, double want, double got)
    {
        True(name + " (got " + got.ToString("R") + ")", Math.Abs(want - got) < 1e-6);
    }

    static int Main()
    {
        // 1. the desktop test suite's sample (PricingTests.HistSettings + Example)
        var s = new AppSettings();
        s.PrinterWatts = 100; s.FriendsMarkupPct = 25; s.OthersMarkupPct = 75; s.MachineRatePerHour = 2;
        s.FailedPrintPct = 10; s.MinimumPrice = 0; s.RoundTo = 0; s.FailedPayback = 0;
        var input = new QuoteInput { Hours = 5, Minutes = 30, FilamentGrams = 120, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2.00 };
        var r = Pricing.Calculate(input, s);
        Eq("sample material 21,60", 21.60, Math.Round(r.MaterialCost, 2));
        Eq("sample power 1,10", 1.10, Math.Round(r.PowerCost, 2));
        Eq("sample failed 2,27", 2.27, Math.Round(r.FailedPrintCost, 2));
        Eq("sample base 24,97", 24.97, Math.Round(r.BaseCost, 2));
        Eq("sample friends 42,2125", 42.2125, r.FriendsPrice);
        Eq("sample customer 54,6975", 54.6975, r.OthersPrice);
        Console.WriteLine("Sample (desktop test settings): material {0}, power {1}, failed {2}, base {3}, friends {4}, customers {5}",
            Num.Kr(r.MaterialCost), Num.Kr(r.PowerCost), Num.Kr(r.FailedPrintCost), Num.Kr(r.BaseCost), Num.Kr(r.FriendsPrice), Num.Kr(r.OthersPrice));

        // 2. a new visitor: default settings, auto wear for an A1 with PLA (= Wear.DefaultRate 1,71)
        bool up;
        var d = WebCalc.SettingsFromText(null, out up);
        WebCalc.ApplyAutoWear(d, "PLA");
        True("defaults not 'upgraded'", !up);
        Eq("default wear 1,71", 1.71, d.MachineRatePerHour);
        var rd = Pricing.Calculate(input, d);
        Eq("defaults friends 37,78", 37.78, Math.Round(rd.FriendsPrice, 2));
        Eq("defaults customer = minimum 50", 50, rd.OthersPrice);
        foreach (bool friends in new[] { true, false })
        {
            var b = Pricing.Breakdown(input, d, friends, WebCalc.WearBreakdown(d, "PLA"));
            long sum = 0;
            foreach (var l in b.Lines) sum += l.Ore;
            True("breakdown lines sum to the shown price (" + friends + ")", sum == b.TotalOre && b.TotalOre == Pricing.ShownOre(b.Price));
        }
        Console.WriteLine("Sample (defaults, A1, PLA): wear {0}/h, friends {1}, customers {2}",
            Num.Kr(d.MachineRatePerHour), Num.Kr(rd.FriendsPrice), Num.Kr(rd.OthersPrice));
        True("P1S PLA-CF on stainless is abrasive", WebCalc.WearBreakdown(new AppSettings { PrinterModel = "P1S" }, "PLA-CF").AbrasiveOnStainless);

        // 3. settings text round trip (browser storage) and old-file upgrade
        var t = new AppSettings { FriendsMarkupPct = 33, Language = "da", LastElectricityPrice = 3.65 };
        var back = WebCalc.SettingsFromText(WebCalc.SettingsToText(t), out up);
        True("settings round trip", back.FriendsMarkupPct == 33 && back.Language == "da" && back.LastElectricityPrice == 3.65 && !up);
        var old = WebCalc.SettingsFromText("MinimumPrice=0\nFriendsMarkupPct=20", out up);
        True("old settings text upgraded (min 0 -> 50)", up && old.MinimumPrice == 50 && old.FriendsMarkupPct == 20);

        // 4. Json: same text as the desktop's JavaScriptSerializer
        string line = Json.Serialize(new HistoryFilament { Grams = 12.5, PricePerKg = 200, Colour = "#FF0000", Type = "PLA", Name = "Rød \"PLA\"" });
        True("filament line JSON exact: " + line, line == "{\"Grams\":12.5,\"PricePerKg\":200,\"Colour\":\"#FF0000\",\"Type\":\"PLA\",\"Name\":\"Rød \\\"PLA\\\"\"}");
        bool threw = false;
        try { Json.Deserialize<HistoryFile>("{not json"); } catch { threw = true; }
        True("Json throws on bad text", threw);

        // 5. history round trip + a desktop-style file (no Printed key -> printed; old For 0 -> customer)
        var rec = History.FromQuote(input, r, s, "Benchy", "https://www.printables.com/model/1", new DateTime(2026, 11, 10, 12, 0, 0));
        rec.Note = "Til Anne, rød";
        var list = History.ParseJson(History.ToJson(new List<QuoteRecord> { rec }));
        True("history round trip", list != null && list.Count == 1 && list[0].Note == "Til Anne, rød" && list[0].OthersPrice == rec.OthersPrice
             && list[0].Printed == rec.Printed && list[0].Hours == 5 && list[0].Filaments != null && list[0].Filaments.Count == 1);
        var desk = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"SavedAt\":\"2026-01-02 10:00:00\",\"Hours\":2,\"Grams\":10.5,\"For\":0,\"Note\":\"æøå\"}]}");
        True("desktop file: Printed missing -> true, For 0 -> 2", desk != null && desk.Count == 1 && desk[0].Printed && desk[0].For == 2
             && desk[0].Grams == 10.5 && desk[0].Note == "æøå");
        var printedFalse = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"b\",\"Printed\":false}]}");
        True("Printed false kept", printedFalse != null && !printedFalse[0].Printed);
        True("newer version -> null", History.ParseJson("{\"Version\":2,\"Quotes\":[]}") == null);

        // 6. Danish formatting works (culture data present)
        True("Num.Kr Danish: " + Num.Kr(1234.5), Num.Kr(1234.5) == "1.234,50 kr.");

        // 7. stage 2: sliced files
        FileTests.Run(True, Eq);

        Console.WriteLine(failed == 0 ? "All " + passed + " web tests passed." : failed + " FAILED, " + passed + " passed.");
        return failed == 0 ? 0 : 1;
    }
}
