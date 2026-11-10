using System;
using System.Globalization;

namespace PrintPrice.Web
{
    /// The live electricity price on the website: the Worker's answer -> the price, with the desktop's own parsing
    /// (src\PowerPrice.cs: PickCurrent + TotalPrice). Pure (tests call it); the fetch itself is printPrice.fetchPower (JS).
    public static class LivePower
    {
        public const string WorkerUrl = "https://printprice.gtd-relay.workers.dev/power";
        public const string SiteHost = "gettic.github.io";
        public const int TimeoutMs = 10000;

        /// Status 0 = no answer (offline, blocked, timed out).
        public sealed class Answer
        {
            // properties (not fields): JS interop fills only properties
            public int Status { get; set; }
            public string Text { get; set; }
            public string Stale { get; set; }     // X-Price-Stale (ISO UTC) or null
            public bool TimedOut { get; set; }
        }

        public sealed class Result
        {
            public bool Ok;
            public PowerQuote Quote;
            public double Total;          // kr/kWh incl. fees (and VAT), rounded to 2 decimals as on the desktop
            public DateTime? SavedUtc;    // a saved copy (Worker's STALE): when it was fetched
            public string Why;            // not Ok: the reason, for "Couldn't get the live price ({0})…"
        }

        /// Where the page asks: the Worker on the real site; anywhere else a test answer next to the page (only the local
        /// test server has one), so local runs never hit the Worker (its CORS allows the real site only).
        public static string Url(string host, string area)
        {
            string a = area == "DK2" ? "DK2" : "DK1";
            return (string.Equals(host, SiteHost, StringComparison.OrdinalIgnoreCase) ? WorkerUrl : "_t/power.json") + "?area=" + a;
        }

        public static Result Interpret(Answer a, string area, DateTime nowUtc, double addOn, bool vat)
        {
            var r = new Result();
            if (a == null || a.Status == 0)
            {
                r.Why = a != null && a.TimedOut ? L.S("the price service didn't answer in time", "pristjenesten svarede ikke i tide")
                                                : L.S("could not reach the price service", "kunne ikke nå pristjenesten");
                return r;
            }
            if (a.Status == 429) { r.Why = L.S("the price service is busy right now", "pristjenesten har travlt lige nu"); return r; }
            if (a.Status != 200) { r.Why = L.S("the price service is unavailable", "pristjenesten er ikke tilgængelig"); return r; }
            DateTime saved = default(DateTime);
            bool stale = !string.IsNullOrEmpty(a.Stale) && DateTime.TryParse(a.Stale, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out saved);
            if (stale) r.SavedUtc = DateTime.SpecifyKind(saved, DateTimeKind.Utc);
            try
            {
                // a saved copy: the slot that was current when it was fetched
                r.Quote = PowerPrice.PickCurrent(a.Text ?? "", area == "DK2" ? "DK2" : "DK1", stale ? r.SavedUtc.Value : nowUtc);
            }
            catch (SliceException ex) { r.Why = ex.Message; return r; }
            catch (Exception) { r.Why = L.S("unexpected answer from Energi Data Service", "uventet svar fra Energi Data Service"); return r; }
            r.Total = Math.Round(PowerPrice.TotalPrice(r.Quote.SpotDkkPerKwh, addOn, vat), 2);
            r.Ok = true;
            return r;
        }

        /// The line under the electricity price (MainForm.FetchPowerPrice's texts). applied = the box got the price.
        /// toLocal turns a UTC time into the browser's local time.
        public static string Line(Result r, double addOn, bool vat, bool applied, Func<DateTime, DateTime> toLocal, DateTime localToday)
        {
            if (!r.Ok)
                return L.F("Couldn't get the live price ({0}). Using your last price.",
                           "Kunne ikke hente live-prisen ({0}). Bruger din sidste pris.", r.Why);
            string total = r.Total.ToString("0.00", Num.Dk);
            string when = r.Quote.Area + " " + toLocal(r.Quote.SlotStartUtc).ToString("HH:mm", CultureInfo.InvariantCulture);
            // times always with ":" (da-DK's own time separator here is ".", the desktop shows 17:45)
        if (r.SavedUtc.HasValue)
            {
                var t = toLocal(r.SavedUtc.Value);
                string at = t.Date == localToday.Date ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : t.ToString("d/M HH:mm", CultureInfo.InvariantCulture);
                when += L.F(" (price from {0}, the live service is busy)", " (pris fra {0}, live-tjenesten har travlt)", at);
            }
            if (!applied)
                return L.F("Live price {0} is {1} kr/kWh (not applied because you edited the field).",
                           "Live-pris {0} er {1} kr/kWh (ikke brugt, fordi du har rettet feltet).", when, total);
            string breakdown = L.F("spot {0} + fees {1}", "spot {0} + afg. {1}",
                                   r.Quote.SpotDkkPerKwh.ToString("0.00", Num.Dk), addOn.ToString("0.00", Num.Dk)) +
                               (vat ? L.S(" + 25 % VAT", " + moms") : "");
            // non-breaking spaces: "= 3,65 kr." wraps as one piece
            return L.S("Live ", "Live-pris ") + when + ": " + breakdown + "\u00a0=\u00a0" + total + "\u00a0kr.";
        }
    }
}
