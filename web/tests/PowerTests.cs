using System;
using System.Globalization;
using System.Text;
using PrintPrice;
using PrintPrice.Web;

// Live electricity price on the website: the Worker's answers -> LivePower.Interpret (the desktop's PickCurrent + TotalPrice)
// and the line texts. No network.
static class PowerTests
{
    static Action<string, bool> True;
    static Action<string, double, double> Eq;

    /// An Energi Data Service answer with 15-minute slots from `from` (UTC), `count` of them, price = 2000 + 100 * i DKK/MWh.
    public static string Records(DateTime from, int count)
    {
        var sb = new StringBuilder("{\"total\":" + count + ",\"dataset\":\"DayAheadPrices\",\"records\":[");
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"TimeUTC\":\"" + from.AddMinutes(15 * i).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
                      + "\",\"DayAheadPriceDKK\":" + (2000 + 100 * i).ToString(CultureInfo.InvariantCulture) + ".5}");
        }
        return sb.Append("]}").ToString();
    }

    public static void Run(Action<string, bool> t, Action<string, double, double> eq)
    {
        True = t; Eq = eq;
        bool danish = L.Danish;
        L.Danish = false;
        var from = new DateTime(2026, 11, 10, 11, 0, 0, DateTimeKind.Utc);
        string json = Records(from, 16);   // 11:00 .. 14:45 UTC
        var at = new DateTime(2026, 11, 10, 12, 14, 59, DateTimeKind.Utc);

        // fresh answer: the slot that has started (12:00 = record 4 -> 2400,5 DKK/MWh)
        var r = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json }, "DK1", at, 0.55, true);
        True("fresh: ok", r.Ok && r.SavedUtc == null);
        Eq("fresh: slot 12:00 spot 2,4005", 2.4005, r.Quote.SpotDkkPerKwh);
        Eq("fresh: total (2,4005 + 0,55) x 1,25 = 3,69", 3.69, r.Total);
        Eq("same as desktop TotalPrice", Math.Round(PowerPrice.TotalPrice(2.4005, 0.55, true), 2), r.Total);
        var r15 = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json }, "DK1", at.AddSeconds(1), 0.55, false);
        Eq("12:15:00 -> next slot, no VAT: 2,5005 + 0,55 = 3,05", 3.05, r15.Total);

        Func<DateTime, DateTime> utc = x => x;   // "local" = UTC in the tests
        string line = LivePower.Line(r, 0.55, true, true, utc, at);
        True("line: " + line, line == "Live DK1 12:00: spot 2,40 + fees 0,55 + 25 % VAT\u00a0=\u00a03,69\u00a0kr.");
        string edited = LivePower.Line(r, 0.55, true, false, utc, at);
        True("edited field: " + edited, edited == "Live price DK1 12:00 is 3,69 kr/kWh (not applied because you edited the field).");

        // STALE copy from 13:20 UTC, asked 5 hours later: the slot current at 13:20, with the copy's time
        var stale = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json, Stale = "2026-11-10T13:20:00.000Z" },
                                        "DK1", at.AddHours(5), 0.55, true);
        True("stale: ok, saved 13:20", stale.Ok && stale.SavedUtc == new DateTime(2026, 11, 10, 13, 20, 0, DateTimeKind.Utc));
        True("stale: slot 13:15", stale.Quote.SlotStartUtc == new DateTime(2026, 11, 10, 13, 15, 0));
        string sl = LivePower.Line(stale, 0.55, true, true, utc, at);
        True("stale line: " + sl, sl.StartsWith("Live DK1 13:15 (price from 13:20, the live service is busy): spot 2,90"));
        string sl2 = LivePower.Line(stale, 0.55, true, true, utc, at.AddDays(1));
        True("stale from yesterday shows the date: " + sl2, sl2.Contains("(price from 10/11 13:20,"));

        // failures -> the fallback line (the typed / last price stays)
        var busy = LivePower.Interpret(new LivePower.Answer { Status = 429, Text = "price service busy" }, "DK1", at, 0.55, true);
        string bl = LivePower.Line(busy, 0.55, true, true, utc, at);
        True("429: " + bl, !busy.Ok && bl == "Couldn't get the live price (the price service is busy right now). Using your last price.");
        var down = LivePower.Interpret(new LivePower.Answer { Status = 502, Text = "price service unavailable" }, "DK1", at, 0.55, true);
        True("502", !down.Ok && down.Why == "the price service is unavailable");
        var none = LivePower.Interpret(new LivePower.Answer { Status = 0 }, "DK1", at, 0.55, true);
        True("no answer (offline / blocked)", !none.Ok && none.Why == "could not reach the price service");
        var slow = LivePower.Interpret(new LivePower.Answer { Status = 0, TimedOut = true }, "DK1", at, 0.55, true);
        True("timeout", !slow.Ok && slow.Why == "the price service didn't answer in time");
        var old = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json }, "DK1", at.AddHours(5), 0.55, true);
        True("old prices (fresh answer, > 2 h) -> no current price: " + old.Why, !old.Ok && old.Why.Contains("no current price"));
        var bad = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = "<html>" }, "DK1", at, 0.55, true);
        True("bad JSON -> not ok", !bad.Ok && bad.Why.Length > 0);
        var empty = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = "{\"records\":[]}" }, "DK2", at, 0.55, true);
        True("no records -> not ok", !empty.Ok);

        // address: Worker only on the real site, area only DK1/DK2
        True("url on the site", LivePower.Url("gettic.github.io", "DK2") == "https://printprice.gtd-relay.workers.dev/power?area=DK2");
        True("url elsewhere = test answer", LivePower.Url("localhost", "DK1") == "_t/power.json?area=DK1");
        True("url odd area -> DK1", LivePower.Url("gettic.github.io", "x&y=1") == "https://printprice.gtd-relay.workers.dev/power?area=DK1");

        L.Danish = true;
        busy = LivePower.Interpret(new LivePower.Answer { Status = 429 }, "DK1", at, 0.55, true);
        True("Danish fallback text", LivePower.Line(busy, 0.55, true, true, utc, at) ==
             "Kunne ikke hente live-prisen (pristjenesten har travlt lige nu). Bruger din sidste pris.");
        L.Danish = danish;
    }
}
