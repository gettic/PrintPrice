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

        // STALE copy (old Worker: only X-Price-Stale) fetched 11:20 UTC, asked at 12:14: still the slot of NOW, with the copy's time
        var stale = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json, Stale = "2026-11-10T11:20:00.000Z" },
                                        "DK1", at, 0.55, true);
        True("stale: ok, from 11:20", stale.Ok && stale.Stale && stale.SavedUtc == new DateTime(2026, 11, 10, 11, 20, 0, DateTimeKind.Utc));
        True("stale: slot of now (12:00)", stale.Quote.SlotStartUtc == new DateTime(2026, 11, 10, 12, 0, 0));
        string sl = LivePower.Line(stale, 0.55, true, true, utc, at);
        True("stale line: " + sl, sl.StartsWith("Live DK1 12:00 (price from 11:20, the live service is busy): spot 2,40"));
        string sl2 = LivePower.Line(stale, 0.55, true, true, utc, at.AddDays(1));
        True("stale from yesterday shows the date: " + sl2, sl2.Contains("(price from 10/11 11:20,"));
        // a STALE copy too old for a current slot (the Worker's 10-day-old copy): not used, the user's price stays, its date said
        var tooOld = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json, Stale = "2026-11-10T11:20:00.000Z" },
                                         "DK1", at.AddDays(10), 0.55, true);
        string tl = LivePower.Line(tooOld, 0.55, true, true, utc, at.AddDays(10));
        True("too old: not used, says from when: " + tl, !tooOld.Ok && tooOld.NoCurrent
             && tl == "Couldn't get the live price (the newest prices are from 10/11 11:20). Using your last price.");
        // the new Worker's headers: source, fetch time, cache state
        var eli = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json, Source = "elprisenligenu.dk", Fetched = "2026-11-10T12:05:00Z", Cache = "MISS" },
                                      "DK1", at, 0.55, true);
        True("new Worker fresh: ok, no 'price from', credit", eli.Ok && !eli.Stale && eli.SavedUtc == null && eli.Source == "elprisenligenu.dk"
             && LivePower.Credit(eli) == "Prices: " && LivePower.Line(eli, 0.55, true, true, utc, at).StartsWith("Live DK1 12:00: spot"));
        var eds = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json, Source = "Energi Data Service", Fetched = "2026-11-10T12:05:00Z", Cache = "HIT" },
                                      "DK1", at, 0.55, true);
        True("Energi Data Service: no credit line", eds.Ok && LivePower.Credit(eds) == null);
        var kept = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json, Source = "elprisenligenu.dk", Fetched = "2026-11-10T11:30:00Z",
                                                              Cache = "STALE", Stale = "2026-11-10T11:30:00Z" }, "DK1", at, 0.55, true);
        True("new Worker STALE: price from its fetch time: " + LivePower.Line(kept, 0.55, true, true, utc, at),
             kept.Ok && kept.Stale && LivePower.Line(kept, 0.55, true, true, utc, at).StartsWith("Live DK1 12:00 (price from 11:30, the live service is busy)"));
        // fetched over a day ago (no STALE said): "price from" with the date, no "busy"
        string day = PowerTests.Records(at.AddHours(-26), 160);
        var aged = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = day, Fetched = "2026-11-09T10:00:00Z", Cache = "KEPT" }, "DK1", at, 0.55, true);
        string al = LivePower.Line(aged, 0.55, true, true, utc, at);
        True("fetched yesterday: price from with the date: " + al, aged.Ok && al.Contains("(price from 9/11 10:00):") && !al.Contains("busy"));
        var noHead = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json }, "DK1", at, 0.55, true);
        True("old Worker, no headers: as before, no credit", noHead.Ok && noHead.Source == "" && LivePower.Credit(noHead) == null && noHead.SavedUtc == null);
        var noCurNoDate = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json }, "DK1", at.AddHours(5), 0.55, true);
        True("no current slot, no fetch time: the old reason", !noCurNoDate.Ok && !noCurNoDate.NoCurrent && noCurNoDate.Why.Contains("no current price"));

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
        tooOld = LivePower.Interpret(new LivePower.Answer { Status = 200, Text = json, Stale = "2026-11-10T11:20:00.000Z" }, "DK1", at.AddDays(10), 0.55, true);
        True("Danish too old", LivePower.Line(tooOld, 0.55, true, true, utc, at.AddDays(10)) ==
             "Kunne ikke hente live-prisen (de nyeste priser er fra 10/11 11:20). Bruger din sidste pris.");
        True("Danish credit + price from", LivePower.Credit(eli) == "Priser: " && LivePower.Line(aged, 0.55, true, true, utc, at).Contains("(pris fra 9/11 10:00):"));
        L.Danish = danish;
    }
}
