using System;
using System.Collections.Generic;

namespace PrintPrice.Tests
{
    static class PricingTests
    {
        static int failures = 0;

        static void Eq(string name, double expected, double actual)
        {
            bool ok = Math.Abs(expected - actual) < 0.005;
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + ": expected " + expected.ToString("0.00") + ", got " + actual.ToString("0.00"));
            if (!ok) failures++;
        }

        static void True(string name, bool cond)
        {
            Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
            if (!cond) failures++;
        }

        static QuoteInput Example()
        {
            return new QuoteInput { Hours = 5, Minutes = 30, FilamentGrams = 120, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2.00 };
        }

        // slice_info.config pieces (trimmed from the real 4-colour Benchy slices, scratchpad multi-colour_test\fixtures)
        static string Fil(int id, string colour, string metres, string grams)
        {
            return "<filament id=\"" + id + "\" tray_info_idx=\"GFL99\" type=\"PLA\" color=\"" + colour + "\" used_m=\"" + metres +
                "\" used_g=\"" + grams + "\" total_load_time=\"2225.00\" total_unload_time=\"2581.00\"/>";
        }

        static string PlateXml(string prediction, string weight, params string[] filaments)
        {
            return "<plate><metadata key=\"index\" value=\"1\"/><metadata key=\"prediction\" value=\"" + prediction + "\"/>" +
                "<metadata key=\"weight\" value=\"" + weight + "\"/>" + string.Join("", filaments) + "</plate>";
        }

        static string SliceInfo(params string[] plates)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><config>" + string.Join("", plates) + "</config>";
        }

        // result.json (trimmed from result_a_one.json: one plate, 4 filaments)
        const string ResultAOne = "{\"error_string\":\"Success.\",\"return_code\":0,\"sliced_plates\":[{" +
            "\"feature_type_times\":{\"Flush\":31431.537109375},\"filament_change_times\":435,\"filaments\":[" +
            "{\"filament_id\":\"GFA00\",\"id\":1,\"main_used_g\":5.042617321014404,\"total_used_g\":33.04133987426758}," +
            "{\"filament_id\":\"GFL99\",\"id\":2,\"main_used_g\":0.38217446208000183,\"total_used_g\":55.98664855957031}," +
            "{\"filament_id\":\"GFL99\",\"id\":3,\"main_used_g\":5.874551296234131,\"total_used_g\":17.282550811767578}," +
            "{\"filament_id\":\"GFL99\",\"id\":4,\"main_used_g\":0.5582333207130432,\"total_used_g\":29.542543411254883}]," +
            "\"id\":1,\"main_predication\":7115.779296875,\"total_predication\":38566.390625}]}";

        static double Sum(System.Collections.Generic.IEnumerable<double> xs)
        {
            double t = 0;
            foreach (var x in xs) t += x;
            return t;
        }

        static int Main()
        {
            // Wear: printer 2099/3000 = 0.6997 + parts 600/1500 = 0.40 + hotend + plate
            // default: + 139/500 = 0.278 + 200/600 = 0.3333 = 1.711
            Eq("wear default (0.4 SS, PLA, smooth)", 1.71, Wear.DefaultRate);
            // 0.2 stainless: same hotend price and life = 1.711
            Eq("wear 0.2 stainless", 1.71, Wear.Calculate(Wear.FindNozzle("0.2 stainless"), false, "PLA", Wear.FindPlate("High Temp Plate")).Total);
            // + 0.278 + 150/1000 = 0.15 -> 1.5277
            Eq("wear textured plate", 1.53, Wear.Calculate(Wear.FindNozzle("0.4 stainless"), false, "PLA", Wear.FindPlate("Textured PEI Plate")).Total);
            // + 0.278 + 200/300 = 0.6667 -> 2.0443
            Eq("wear PETG on smooth (plate life halved)", 2.04, Wear.Calculate(Wear.FindNozzle("0.4 stainless"), false, "PETG", Wear.FindPlate("High Temp Plate")).Total);
            // + 185/750 = 0.2467 + 0.3333 -> 1.6797
            Eq("wear PLA on hardened", 1.68, Wear.Calculate(Wear.FindNozzle("0.4 hardened"), false, "PLA", Wear.FindPlate("High Temp Plate")).Total);
            // + 185/300 = 0.6167 + 0.3333 -> 2.0497
            Eq("wear CF on hardened", 2.05, Wear.Calculate(Wear.FindNozzle("0.4 hardened"), true, "PLA-CF", Wear.FindPlate("High Temp Plate")).Total);
            var cfSS = Wear.Calculate(Wear.FindNozzle("0.4 stainless"), true, "PLA-CF", Wear.FindPlate("High Temp Plate"));
            True("CF on stainless flagged", cfSS.AbrasiveOnStainless);
            // + 139/100 = 1.39 + 0.3333 -> 2.823
            Eq("wear CF on stainless (100 h hotend)", 2.82, cfSS.Total);
            True("short preset name", BambuSlicer.ShortName("Bambu PLA Basic @BBL A1 0.2 nozzle") == "Bambu PLA Basic");
            True("suffix 0.6", BambuSlicer.Suffix("0.6") == " @BBL A1 0.6 nozzle" && BambuSlicer.Suffix("0.4") == " @BBL A1");
            Eq("AutoUpdateCheck default is 1", 1, new AppSettings().AutoUpdateCheck);
            var orig0 = new AppSettings { AutoUpdateCheck = 0 };
            Eq("Clone keeps AutoUpdateCheck 0", 0, orig0.Clone().AutoUpdateCheck);
            True("Language default is empty", new AppSettings().Language == "");
            True("IsDanish(da) true", AppSettings.IsDanish("da"));
            True("IsDanish(en) false", !AppSettings.IsDanish("en"));
            True("IsDanish(unknown) = IsDanish(auto)", AppSettings.IsDanish("xx") == AppSettings.IsDanish(""));
            True("Clone keeps Language da", new AppSettings { Language = "da" }.Clone().Language == "da");
            Eq("FailedPrintPct default is 0 (off)", 0, new AppSettings().FailedPrintPct);
            Eq("Clone keeps FailedPrintPct", 12, new AppSettings { FailedPrintPct = 12 }.Clone().FailedPrintPct);
            var s = new AppSettings { MachineRatePerHour = 0, FailedPrintPct = 0 }; // 100 W, 25 %, 75 %, no wear, no fails (formula check)
            var r = Pricing.Calculate(Example(), s);
            Eq("material", 21.60, r.MaterialCost);
            Eq("power kWh", 0.55, r.PowerKwh);
            Eq("power cost", 1.10, r.PowerCost);
            Eq("failed prints off", 0, r.FailedPrintCost);
            Eq("base", 22.70, r.BaseCost);
            Eq("friends", 28.375, r.FriendsPrice);
            Eq("others", 39.725, r.OthersPrice);

            // Failed prints 5 %: 22.70 × 0.05 = 1.135; base 23.835; × 1.25 = 29.79375; × 1.75 = 41.71125
            var rf = Pricing.Calculate(Example(), new AppSettings { MachineRatePerHour = 0, FailedPrintPct = 5 });
            Eq("failed prints 5 %", 1.135, rf.FailedPrintCost);
            Eq("base with failed prints", 23.835, rf.BaseCost);
            Eq("friends with failed prints", 29.79375, rf.FriendsPrice);
            Eq("others with failed prints", 41.71125, rf.OthersPrice);
            Eq("negative failed % counts as 0", 22.70, Pricing.Calculate(Example(), new AppSettings { MachineRatePerHour = 0, FailedPrintPct = -5 }).BaseCost);

            var zero = Pricing.Calculate(new QuoteInput(), s);
            Eq("zero job", 0, zero.OthersPrice);

            var s2 = new AppSettings { MachineRatePerHour = 5, MinimumPrice = 50, RoundTo = 5, FailedPrintPct = 0 };
            var r2 = Pricing.Calculate(Example(), s2);
            // 39.725 + 5.5h*5 = 67.225 -> round up to 70
            Eq("others w/ rate + rounding", 70, r2.OthersPrice);
            // 28.375 + 5.5h*5 = 55.875 -> round up to 60
            Eq("friends w/ rate + rounding", 60, r2.FriendsPrice);

            var s4 = new AppSettings();   // defaults: failed prints off, A1 wear 5.5 h × 1.71 = 9.405
            Eq("others with defaults", 49.13, Pricing.Calculate(Example(), s4).OthersPrice);   // 39.725 + 9.405
            Eq("friends with defaults", 37.78, Pricing.Calculate(Example(), s4).FriendsPrice); // 28.375 + 9.405

            // Benchy: 0 h 38 m, 11.8 g, 200 DKK/kg, 3.65 DKK/kWh, defaults.
            // base = 2.36 + 0.6333 × 0.1 × 3.65 = 2.5912; wear = 0.6333 × 1.71 = 1.0830
            var benchy = Pricing.Calculate(new QuoteInput { Hours = 0, Minutes = 38, FilamentGrams = 11.8, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 3.65 }, new AppSettings());
            Eq("benchy friends", 4.32, benchy.FriendsPrice);   // 2.5912 × 1.25 + 1.0830 = 4.3220
            Eq("benchy others", 5.62, benchy.OthersPrice);     // 2.5912 × 1.75 + 1.0830 = 5.6175

            // ---- multi-colour (per-filament material) ----
            // 1 line = today's formula exactly (the line is ignored, top-level grams/price used)
            var one = Example();
            one.Filaments = new List<FilamentLine> { new FilamentLine { Grams = 120, PricePerKg = 180 } };
            var rOne = Pricing.Calculate(one, s);
            var rOld = Pricing.Calculate(Example(), s);
            True("1 line = old result exactly", rOne.MaterialCost == rOld.MaterialCost && rOne.BaseCost == rOld.BaseCost &&
                rOne.FriendsPrice == rOld.FriendsPrice && rOne.OthersPrice == rOld.OthersPrice);
            True("1 line: no FilamentCosts", rOne.FilamentCosts == null && rOld.FilamentCosts == null);
            var empty = Example();
            empty.Filaments = new List<FilamentLine>();
            Eq("empty list = old material", 21.60, Pricing.Calculate(empty, s).MaterialCost);

            // 4-colour Benchy, 10 h 43 m, 200 kr./kg, 3,65 kr./kWh, defaults:
            // grams 33,04 + 55,99 + 17,28 + 29,54 = 135,85; × 0,2 = 27,17
            // hours 10,716667; power 10,716667 × 0,1 × 3,65 = 3,9116; base 31,0816; wear 10,716667 × 1,71 = 18,3255
            // friends 31,0816 × 1,25 + 18,3255 = 57,1775; customers 31,0816 × 1,75 + 18,3255 = 72,7183
            Func<double, List<FilamentLine>> four = p2 => new List<FilamentLine> {
                new FilamentLine { Grams = 33.04, PricePerKg = 200 }, new FilamentLine { Grams = 55.99, PricePerKg = p2 },
                new FilamentLine { Grams = 17.28, PricePerKg = 200 }, new FilamentLine { Grams = 29.54, PricePerKg = 200 } };
            var mc = Pricing.Calculate(new QuoteInput { Hours = 10, Minutes = 43, FilamentGrams = 135.85, FilamentDkkPerKg = 200,
                ElectricityDkkPerKwh = 3.65, Filaments = four(200) }, new AppSettings());
            True("4 colours: 4 line costs", mc.FilamentCosts != null && mc.FilamentCosts.Length == 4);
            Eq("4 colours line 1 (33,04 g × 200)", 6.608, mc.FilamentCosts[0]);
            Eq("4 colours line 2 (55,99 g × 200)", 11.198, mc.FilamentCosts[1]);
            Eq("4 colours line 3 (17,28 g × 200)", 3.456, mc.FilamentCosts[2]);
            Eq("4 colours line 4 (29,54 g × 200)", 5.908, mc.FilamentCosts[3]);
            Eq("4 colours material", 27.17, mc.MaterialCost);
            Eq("4 colours power", 3.91, mc.PowerCost);
            Eq("4 colours base", 31.08, mc.BaseCost);
            Eq("4 colours friends", 57.18, mc.FriendsPrice);
            Eq("4 colours customers", 72.72, mc.OthersPrice);

            // Mixed: line 2 at 300 kr./kg -> 79,86 g × 0,2 = 15,972 + 55,99 × 0,3 = 16,797 -> 32,769
            // (top-level FilamentGrams/FilamentDkkPerKg are not used with 2+ lines)
            var s0 = new AppSettings { MachineRatePerHour = 0, FailedPrintPct = 0 };
            var mx = Pricing.Calculate(new QuoteInput { Hours = 1, FilamentGrams = 999, FilamentDkkPerKg = 999,
                ElectricityDkkPerKwh = 2.00, Filaments = four(300) }, s0);
            Eq("mixed line 2 (55,99 g × 300)", 16.797, mx.FilamentCosts[1]);
            Eq("mixed material", 32.769, mx.MaterialCost);
            // power 1 h × 0,1 × 2 = 0,20; base 32,969; × 1,25 = 41,21125; × 1,75 = 57,69575
            Eq("mixed base", 32.969, mx.BaseCost);
            Eq("mixed friends", 41.21125, mx.FriendsPrice);
            Eq("mixed customers", 57.69575, mx.OthersPrice);
            // failed prints 10 % on the summed material: (32,769 + 0,20) × 0,1 = 3,2969; base 36,2659
            var mf = Pricing.Calculate(new QuoteInput { Hours = 1, ElectricityDkkPerKwh = 2.00, Filaments = four(300) },
                new AppSettings { MachineRatePerHour = 0, FailedPrintPct = 10 });
            Eq("mixed failed prints 10 %", 3.2969, mf.FailedPrintCost);
            Eq("mixed base with failed prints", 36.2659, mf.BaseCost);
            bool negThrew = false;
            var neg = four(200); neg[2].Grams = -1;
            try { Pricing.Calculate(new QuoteInput { Filaments = neg }, s0); } catch (ArgumentException) { negThrew = true; }
            True("rejects negative line grams", negThrew);

            var s3 = new AppSettings { MinimumPrice = 50, MachineRatePerHour = 0, FailedPrintPct = 0 };
            Eq("minimum price", 50, Pricing.Calculate(Example(), s3).OthersPrice);
            Eq("minimum not for friends", 28.375, Pricing.Calculate(Example(), s3).FriendsPrice);

            Eq("round exact stays", 25, Pricing.RoundUp(25, 5));

            bool threw = false;
            try { Pricing.Calculate(new QuoteInput { Minutes = 60 }, s); } catch (ArgumentException) { threw = true; }
            True("rejects 60 minutes", threw);
            threw = false;
            try { Pricing.Calculate(new QuoteInput { FilamentGrams = -1 }, s); } catch (ArgumentException) { threw = true; }
            True("rejects negative grams", threw);

            double v;
            True("parse 12,5", Num.TryParse("12,5", out v) && v == 12.5);
            True("parse 12.5", Num.TryParse("12.5", out v) && v == 12.5);
            True("parse 1.234,5", Num.TryParse("1.234,5", out v) && v == 1234.5);
            True("parse 1,234.5", Num.TryParse("1,234.5", out v) && v == 1234.5);
            True("reject abc", !Num.TryParse("abc", out v));
            True("reject -3", !Num.TryParse("-3", out v));

            // Printables link parsing
            True("printables id", Printables.ParseId("https://www.printables.com/model/3161-3d-benchy") == "3161");
            True("printables id (lang)", Printables.ParseId("printables.com/de/model/1050000-strap") == "1050000");
            True("printables id (other site)", Printables.ParseId("https://makerworld.com/en/models/123") == null);

            // Bambu slice_info parsing (2 plates; second plate has no 'weight' → sum used_g)
            string xml = "<?xml version=\"1.0\"?><config>" +
                "<plate><metadata key=\"prediction\" value=\"2270\"/><metadata key=\"weight\" value=\"11.76\"/></plate>" +
                "<plate><metadata key=\"prediction\" value=\"600\"/><metadata key=\"weight\" value=\"\"/>" +
                "<filament id=\"1\" used_g=\"2.5\"/><filament id=\"2\" used_g=\"1.0\"/></plate>" +
                "<plate><metadata key=\"prediction\" value=\"0\"/></plate></config>";
            var si = BambuSlicer.ParseSliceInfo(xml);
            Eq("slice seconds", 2870, si.Seconds);
            Eq("slice grams", 15.26, si.Grams);
            Eq("slice plates", 2, si.Plates);
            True("unsliced → null", BambuSlicer.ParseSliceInfo("<config><plate><metadata key=\"index\" value=\"1\"/></plate></config>") == null);
            True("model ext", BambuSlicer.IsModelFile("a.STL") && BambuSlicer.IsModelFile("b.3mf") && !BambuSlicer.IsModelFile("c.f3d"));

            // ---- multi-colour slice parsing (4-colour Benchy, fixtures trimmed) ----
            string[] cols = { "#118002", "#FFFF0A", "#074080", "#ED1C24" };
            var a1 = BambuSlicer.ParseSliceInfo(SliceInfo(PlateXml("38566", "135.85",
                Fil(1, cols[0], "10.90", "33.04"), Fil(2, cols[1], "18.77", "55.99"),
                Fil(3, cols[2], "5.79", "17.28"), Fil(4, cols[3], "9.91", "29.54"))));
            Eq("a_one seconds", 38566, a1.Seconds);
            Eq("a_one grams", 135.85, a1.Grams);
            True("a_one 4 filaments", a1.Filaments != null && a1.Filaments.Count == 4);
            double[] aG = { 33.04, 55.99, 17.28, 29.54 }, aM = { 10.90, 18.77, 5.79, 9.91 };
            bool slotsOk = a1.Filaments.Count == 4;
            for (int i = 0; slotsOk && i < 4; i++)
            {
                var f = a1.Filaments[i];
                slotsOk = f.Slot == i + 1 && f.Colour == cols[i] && f.Type == "PLA" && Math.Abs(f.Grams - aG[i]) < 0.005 &&
                          Math.Abs(f.Metres - aM[i]) < 0.005 && f.ModelGrams == -1;
            }
            True("a_one slots 1-4: colour, PLA, grams, metres, ModelGrams -1", slotsOk);
            True("a_one purge/change unknown", a1.PurgeGrams == -1 && a1.ColourChangeHours == -1);

            var b4 = BambuSlicer.ParseSliceInfo(SliceInfo(PlateXml("39807", "137.51",
                Fil(1, cols[0], "10.90", "33.04"), Fil(2, cols[1], "18.77", "56.89"),
                Fil(3, cols[2], "5.79", "17.56"), Fil(4, cols[3], "9.91", "30.02"))));
            Eq("b_four seconds", 39807, b4.Seconds);
            Eq("b_four grams", 137.51, b4.Grams);
            True("b_four grams per slot", b4.Filaments.Count == 4 && Math.Abs(b4.Filaments[0].Grams - 33.04) < 0.005 &&
                Math.Abs(b4.Filaments[1].Grams - 56.89) < 0.005 && Math.Abs(b4.Filaments[2].Grams - 17.56) < 0.005 &&
                Math.Abs(b4.Filaments[3].Grams - 30.02) < 0.005);

            // 2 plates, same slot summed (2,5 + 1,5 = 4,0 g; 0,8 + 0,5 = 1,3 m); used_g="0" left out; weight "" -> sum used_g
            var two = BambuSlicer.ParseSliceInfo(SliceInfo(
                PlateXml("600", "", Fil(1, "#00ae42", "0.8", "2.5"), Fil(2, "#FFFFFF", "0", "0")),
                PlateXml("300", "", Fil(1, "#00ae42", "0.5", "1.5"))));
            Eq("2 plates seconds", 900, two.Seconds);
            Eq("2 plates grams", 4.0, two.Grams);
            True("2 plates: one slot, zero left out", two.Filaments.Count == 1 && two.Filaments[0].Slot == 1);
            Eq("2 plates slot 1 grams summed", 4.0, two.Filaments[0].Grams);
            Eq("2 plates slot 1 metres summed", 1.3, two.Filaments[0].Metres);
            True("colour normalised to upper case", two.Filaments[0].Colour == "#00AE42");
            True("old single-colour XML has Filaments list", si.Filaments != null && si.Filaments.Count == 2);

            // result.json: model grams 5,04 + 0,38 + 5,87 + 0,56 = 11,858; total 135,853
            var st = BambuSlicer.ParseSliceResult(ResultAOne);
            True("result parsed", st != null);
            Eq("result main g slot 1", 5.04, st.MainGrams[1]);
            Eq("result main g slot 2", 0.38, st.MainGrams[2]);
            Eq("result main g slot 3", 5.87, st.MainGrams[3]);
            Eq("result main g slot 4", 0.56, st.MainGrams[4]);
            Eq("result main g sum", 11.858, Sum(st.MainGrams.Values));
            Eq("result total g sum", 135.853, Sum(st.TotalGrams.Values));
            Eq("result main seconds", 7115.78, st.MainSeconds);
            Eq("result total seconds", 38566.39, st.TotalSeconds);
            True("result colour changes 435", st.ColourChanges == 435);
            True("result null/{}/x -> null", BambuSlicer.ParseSliceResult(null) == null &&
                BambuSlicer.ParseSliceResult("{}") == null && BambuSlicer.ParseSliceResult("x") == null);

            // Apply: purge = 135,853 - 11,858 = 123,996 g; change time (38566,39 - 7115,78) / 3600 = 8,7363 h
            BambuSlicer.ApplySliceStats(a1, st);
            Eq("apply purge grams", 123.996, a1.PurgeGrams);
            Eq("apply colour change hours", 8.7363, a1.ColourChangeHours);
            Eq("apply slot 1 model grams", 5.04, a1.Filaments[0].ModelGrams);
            Eq("apply keeps grams", 135.85, a1.Grams);
            Eq("apply keeps seconds", 38566, a1.Seconds);
            BambuSlicer.ApplySliceStats(b4, null);
            True("apply null stats leaves -1", b4.PurgeGrams == -1 && b4.ColourChangeHours == -1 && b4.Filaments[0].ModelGrams == -1);

            // project_settings.config filament_settings_id
            True("slots 4", BambuSlicer.ParseFilamentSlots("{\"filament_settings_id\":[\"Bambu PLA Basic @BBL A1\",\"PLA\",\"PLA\",\"PLA\"]}").Count == 4);
            True("slots first name", BambuSlicer.ParseFilamentSlots("{\"filament_settings_id\":[\"Bambu PLA Basic @BBL A1\",\"PLA\"]}")[0] == "Bambu PLA Basic @BBL A1");
            True("slots plain string -> 1", BambuSlicer.ParseFilamentSlots("{\"filament_settings_id\":\"Bambu PLA Basic @BBL A1\"}").Count == 1);
            True("slots null/empty/bad -> empty", BambuSlicer.ParseFilamentSlots(null).Count == 0 &&
                BambuSlicer.ParseFilamentSlots("").Count == 0 && BambuSlicer.ParseFilamentSlots("{bad").Count == 0);

            // Live electricity price
            Eq("power total incl. VAT", 2.7625, PowerPrice.TotalPrice(1.66, 0.55, true));
            Eq("power total excl. VAT", 2.21, PowerPrice.TotalPrice(1.66, 0.55, false));
            string pj = "{\"records\":[" +
                "{\"TimeUTC\":\"2026-11-03T14:00:00\",\"DayAheadPriceDKK\":1000.0}," +
                "{\"TimeUTC\":\"2026-11-03T14:15:00\",\"DayAheadPriceDKK\":1200.5}," +
                "{\"TimeUTC\":\"2026-11-03T14:30:00\",\"DayAheadPriceDKK\":1500.0}]}";
            var now = new DateTime(2026, 11, 3, 14, 20, 0, DateTimeKind.Utc);
            var pq = PowerPrice.PickCurrent(pj, "DK2", now);
            Eq("current slot price", 1.2005, pq.SpotDkkPerKwh);
            True("current slot time", pq.SlotStartUtc == new DateTime(2026, 11, 3, 14, 15, 0));
            threw = false;
            try { PowerPrice.PickCurrent(pj, "DK2", now.AddHours(5)); } catch (SliceException) { threw = true; }
            True("stale data rejected", threw);

            // ---- updater ----
            True("tag v1.0.1", Updater.ParseTag("v1.0.1") == new Version(1, 0, 1, 0));
            True("tag 2.3", Updater.ParseTag("2.3") == new Version(2, 3, 0, 0));
            True("tag junk", Updater.ParseTag("latest") == null && Updater.ParseTag("v1.0.1-beta") == null);
            True("1.0.1 newer than 1.0.0", Updater.ParseTag("v1.0.1") > Updater.ParseTag("v1.0.0"));
            True("1.0.0 = 1.0.0.0", Updater.ParseTag("v1.0.0") == new Version(1, 0, 0, 0));
            True("short version", Updater.Short(new Version(1, 2, 3, 0)) == "1.2.3");
            string rel = "{\"tag_name\":\"v1.2.0\",\"name\":\"v1.2.0\",\"draft\":false,\"prerelease\":false," +
                "\"html_url\":\"https://github.com/gettic/PrintPrice/releases/tag/v1.2.0\",\"body\":\"## New\\n- **Faster** slicing\\n- See [README](https://x)\"," +
                "\"assets\":[{\"name\":\"other.zip\",\"size\":5,\"browser_download_url\":\"https://github.com/gettic/PrintPrice/releases/download/v1.2.0/other.zip\"}," +
                "{\"name\":\"PrintPrice.exe\",\"size\":201728,\"digest\":\"sha256:9E4D00BF7355CD8A364686FAEBD887511851169E5075428FDA7AB07787E4643B\"," +
                "\"browser_download_url\":\"https://github.com/gettic/PrintPrice/releases/download/v1.2.0/PrintPrice.exe\"}]}";
            var ui = Updater.ParseRelease(rel);
            True("release parsed", ui != null && ui.Version == new Version(1, 2, 0, 0) && ui.Size == 201728);
            True("release exe asset", ui != null && ui.DownloadUrl.EndsWith("/v1.2.0/PrintPrice.exe"));
            True("release digest", ui != null && ui.Sha256 == "9e4d00bf7355cd8a364686faebd887511851169e5075428fda7ab07787e4643b");
            True("release notes text", ui != null && Updater.NotesText(ui, 500) == "New\n• Faster slicing\n• See README");
            True("prerelease ignored", Updater.ParseRelease(rel.Replace("\"prerelease\":false", "\"prerelease\":true")) == null);
            True("no exe asset", Updater.ParseRelease(rel.Replace("PrintPrice.exe", "Other.exe")) == null);
            True("bad tag ignored", Updater.ParseRelease(rel.Replace("\"v1.2.0\",\"name\"", "\"nightly\",\"name\"")) == null);
            // releases list -> target + combined notes
            Func<string, string, bool, bool, bool, string> mk = (tag, body, draft, pre, exe) =>
                "{\"tag_name\":\"" + tag + "\",\"draft\":" + (draft ? "true" : "false") + ",\"prerelease\":" + (pre ? "true" : "false") +
                ",\"body\":\"" + body + "\",\"assets\":[{\"name\":\"" + (exe ? "PrintPrice.exe" : "other.zip") + "\",\"size\":100," +
                "\"digest\":\"sha256:9E4D00BF7355CD8A364686FAEBD887511851169E5075428FDA7AB07787E4643B\"," +
                "\"browser_download_url\":\"https://github.com/gettic/PrintPrice/releases/download/" + tag + "/" + (exe ? "PrintPrice.exe" : "other.zip") + "\"}]}";
            string rBeta = mk("v1.1.0-beta", "BETA-NOTE", false, true, true);
            string rDraft = mk("v1.0.4", "DRAFT-NOTE", true, false, true);
            string rNightly = mk("nightly", "NIGHTLY-NOTE", false, false, true);
            string rv103 = mk("v1.0.3", "## Fixed\\n- **Faster** start", false, false, true);
            string rv102 = mk("v1.0.2", "", false, false, true);
            string rv101 = mk("v1.0.1", "V101-NOTE", false, false, true);
            string rv100 = mk("v1.0.0", "V100-NOTE", false, false, true);
            string list = "[" + string.Join(",", new string[] { rBeta, rDraft, rv103, rNightly, rv102, rv101, rv100 }) + "]";
            var cur = new Version(1, 0, 1, 0);
            var tu = Updater.ParseReleases(list, cur);
            True("list target 1.0.3", tu != null && tu.Version == new Version(1, 0, 3, 0) && tu.DownloadUrl.EndsWith("/v1.0.3/PrintPrice.exe"));
            True("list notes", tu != null && Updater.NotesText(tu, 0) == "## v1.0.3\nFixed\n• Faster start\n\n## v1.0.2\nNo notes");
            True("list notes exclude others", tu != null && !tu.Notes.Contains("BETA") && !tu.Notes.Contains("DRAFT") &&
                !tu.Notes.Contains("NIGHTLY") && !tu.Notes.Contains("V101") && !tu.Notes.Contains("V100"));
            True("list up to date", Updater.ParseReleases(list, new Version(1, 0, 3, 0)) == null);
            True("list empty", Updater.ParseReleases("[]", cur) == null);
            var tu2 = Updater.ParseReleases("[" + string.Join(",", new string[] { rBeta, rDraft, mk("v1.0.3", "X", false, false, false), rNightly, rv102, rv101, rv100 }) + "]", cur);
            True("list no-asset skipped", tu2 != null && tu2.Version == new Version(1, 0, 2, 0) && Updater.NotesText(tu2, 0) == "## v1.0.2\nNo notes");
            var tu3 = Updater.ParseReleases("[" + string.Join(",", new string[] { rv100, rv102, rNightly, rv101, rv103, rDraft, rBeta }) + "]", cur);
            True("list scrambled order", tu3 != null && Updater.NotesText(tu3, 0) == "## v1.0.3\nFixed\n• Faster start\n\n## v1.0.2\nNo notes");
            True("notes max 0 no cut", tu != null && Updater.NotesText(tu, 0).Length > 20);
            True("notes max cuts", tu != null && Updater.NotesText(tu, 10).EndsWith("…") && Updater.NotesText(tu, 10) == "## v1.0.3…");
            // Model picture helpers
            string benchyDir = "media/prints/3161/images/20206_70fde6a0-6da1-4522-ba46-25f1bece7199/";
            string benchyThumb = "https://media.printables.com/" + benchyDir + "thumbs/inside/320x240/jpg/benchy.jpg";
            True("ThumbUrl benchy jpg", Printables.ThumbUrl(benchyDir + "benchy.jpg") == benchyThumb);
            True("ThumbUrl png -> jpg", Printables.ThumbUrl(benchyDir + "benchy.png") == benchyThumb);
            True("ThumbUrl webp -> jpg", Printables.ThumbUrl(benchyDir + "benchy.webp") == benchyThumb);
            True("ThumbUrl leading slash", Printables.ThumbUrl("/" + benchyDir + "benchy.jpg") == benchyThumb);
            True("ThumbUrl null", Printables.ThumbUrl(null) == null);
            True("ThumbUrl empty", Printables.ThumbUrl("") == null);
            True("ThumbUrl ../x", Printables.ThumbUrl("../x") == null);
            True("ThumbUrl full http url", Printables.ThumbUrl("http://evil/x.jpg") == null);
            True("ThumbUrl not under media/", Printables.ThumbUrl("other/prints/1/x.jpg") == null);
            True("ImagePath from image.filePath", Printables.ImagePathFromReply("{\"data\":{\"print\":{\"image\":{\"filePath\":\"media/a/x.jpg\"},\"images\":[{\"filePath\":\"media/b/y.jpg\"}]}}}") == "media/a/x.jpg");
            True("ImagePath image null -> first of images", Printables.ImagePathFromReply("{\"data\":{\"print\":{\"image\":null,\"images\":[{\"filePath\":\"media/b/y.jpg\"},{\"filePath\":\"media/c/z.jpg\"}]}}}") == "media/b/y.jpg");
            True("ImagePath print null", Printables.ImagePathFromReply("{\"data\":{\"print\":null}}") == null);
            True("ImagePath bad JSON", Printables.ImagePathFromReply("not json {") == null);
            True("PickPicture prefers plate_1", BambuSlicer.PickPictureEntry(new[] { "Metadata/pick_1.png", "Metadata/top_1.png", "Metadata/plate_no_light_1.png", "Metadata/plate_1_small.png", "Metadata/plate_1.png" }) == "Metadata/plate_1.png");
            True("PickPicture thumbnail", BambuSlicer.PickPictureEntry(new[] { "Metadata/thumbnail.png", "Metadata/top_1.png" }) == "Metadata/thumbnail.png");
            True("PickPicture plate_2 over small", BambuSlicer.PickPictureEntry(new[] { "Metadata/plate_2_small.png", "Metadata/plate_2.png" }) == "Metadata/plate_2.png");
            True("PickPicture only pick -> null", BambuSlicer.PickPictureEntry(new[] { "Metadata/pick_1.png" }) == null);
            True("PickPicture ignores subfolders and 3D", BambuSlicer.PickPictureEntry(new[] { "Metadata/sub/x.png", "3D/x.png" }) == null);
            True("IsImage PNG", Net.IsImage(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
            True("IsImage JPEG", Net.IsImage(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46 }));
            True("IsImage null", !Net.IsImage(null));
            True("IsImage short", !Net.IsImage(new byte[] { 0x89, 0x50, 0x4E }));
            True("IsImage GIF", !Net.IsImage(System.Text.Encoding.ASCII.GetBytes("GIF89a..")));
            Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
