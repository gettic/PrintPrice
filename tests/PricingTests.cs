using System;

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

        static int Main()
        {
            // Wear: printer 0.42 + parts 0.15 + hotend + plate
            Eq("wear default (0.4 SS, PLA, smooth)", 0.91, Wear.DefaultRate);
            Eq("wear textured plate", 0.86, Wear.Calculate(Wear.FindNozzle("0.4 stainless"), false, "PLA", Wear.FindPlate("Textured PEI Plate")).Total);
            Eq("wear PETG on smooth (plate life halved)", 1.11, Wear.Calculate(Wear.FindNozzle("0.4 stainless"), false, "PETG", Wear.FindPlate("High Temp Plate")).Total);
            Eq("wear CF on hardened", 1.14, Wear.Calculate(Wear.FindNozzle("0.4 hardened"), true, "PLA-CF", Wear.FindPlate("High Temp Plate")).Total);
            var cfSS = Wear.Calculate(Wear.FindNozzle("0.4 stainless"), true, "PLA-CF", Wear.FindPlate("High Temp Plate"));
            True("CF on stainless flagged", cfSS.AbrasiveOnStainless);
            Eq("wear CF on stainless (100 h hotend)", 2.16, cfSS.Total);
            True("short preset name", BambuSlicer.ShortName("Bambu PLA Basic @BBL A1 0.2 nozzle") == "Bambu PLA Basic");
            True("suffix 0.6", BambuSlicer.Suffix("0.6") == " @BBL A1 0.6 nozzle" && BambuSlicer.Suffix("0.4") == " @BBL A1");
            var s = new AppSettings { MachineRatePerHour = 0 }; // 100 W, 25 %, 75 %, no wear (formula check)
            var r = Pricing.Calculate(Example(), s);
            Eq("material", 21.60, r.MaterialCost);
            Eq("power kWh", 0.55, r.PowerKwh);
            Eq("power cost", 1.10, r.PowerCost);
            Eq("base", 22.70, r.BaseCost);
            Eq("friends", 28.375, r.FriendsPrice);
            Eq("others", 39.725, r.OthersPrice);

            var zero = Pricing.Calculate(new QuoteInput(), s);
            Eq("zero job", 0, zero.OthersPrice);

            var s2 = new AppSettings { MachineRatePerHour = 5, MinimumPrice = 50, RoundTo = 5 };
            var r2 = Pricing.Calculate(Example(), s2);
            // 39.725 + 5.5h*5 = 67.225 -> round up to 70
            Eq("others w/ rate + rounding", 70, r2.OthersPrice);
            Eq("friends rounded", 30, r2.FriendsPrice);

            var s4 = new AppSettings();   // defaults include A1 wear: 39.725 + 5.5 h × 0.91 = 44.73
            Eq("others with default wear", 44.73, Pricing.Calculate(Example(), s4).OthersPrice);
            Eq("friends unaffected by wear", 28.375, Pricing.Calculate(Example(), s4).FriendsPrice);

            var s3 = new AppSettings { MinimumPrice = 50, MachineRatePerHour = 0 };
            Eq("minimum price", 50, Pricing.Calculate(Example(), s3).OthersPrice);

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
            Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
