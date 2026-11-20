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
            // the newer Worker (plans\worker-power\web-notes.md); null from the old one
            public string Source { get; set; }    // X-Price-Source: "Energi Data Service" or "elprisenligenu.dk"
            public string Fetched { get; set; }   // X-Price-Fetched: when the prices were fetched (ISO UTC)
            public string Cache { get; set; }     // X-Cache: MISS / HIT / KEPT / STALE
            public bool TimedOut { get; set; }
        }

        public sealed class Result
        {
            public bool Ok;
            public PowerQuote Quote;
            public double Total;          // kr/kWh incl. fees (and VAT), rounded to 2 decimals as on the desktop
            public DateTime? SavedUtc;    // shown as "price from …": a saved copy (Worker's STALE) or prices fetched over a day ago
            public DateTime? FetchedUtc;  // when the prices were fetched (X-Price-Fetched, else X-Price-Stale); null = not said
            public bool Stale;            // the Worker could reach no source and sent its saved copy
            public bool NoCurrent;        // the prices have no slot for now (too old): "the newest prices are from …"
            public string Source = "";    // X-Price-Source ("" from the old Worker)
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
            r.Source = (a.Source ?? "").Trim();
            r.Stale = string.Equals((a.Cache ?? "").Trim(), "STALE", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(a.Stale);
            r.FetchedUtc = Utc(a.Fetched) ?? Utc(a.Stale);
            try
            {
                // always the slot of NOW: a saved copy covers 36 h; one without a current slot is too old to use (the
                // Worker's copy was 10 days old once: the page then keeps the user's own price)
                r.Quote = PowerPrice.PickCurrent(a.Text ?? "", area == "DK2" ? "DK2" : "DK1", nowUtc);
            }
            catch (SliceException ex)
            {
                r.Why = ex.Message;
                // PowerPrice.PickCurrent's "no current price" (not a broken answer): say how old the prices are
                string a2 = area == "DK2" ? "DK2" : "DK1";
                r.NoCurrent = r.FetchedUtc.HasValue && ex.Message == L.F("no current price published for {0}", "ingen aktuel pris offentliggjort for {0}", a2);
                return r;
            }
            catch (Exception) { r.Why = L.S("unexpected answer from Energi Data Service", "uventet svar fra Energi Data Service"); return r; }
            r.Total = Math.Round(PowerPrice.TotalPrice(r.Quote.SpotDkkPerKwh, addOn, vat), 2);
            r.Ok = true;
            // "price from …": a saved copy, or prices fetched about a day ago or more
            if (r.FetchedUtc.HasValue && (r.Stale || nowUtc - r.FetchedUtc.Value > OldAfter)) r.SavedUtc = r.FetchedUtc;
            return r;
        }

        /// Prices fetched longer ago than this get "price from <date>" even when the Worker didn't say STALE.
        public static readonly TimeSpan OldAfter = TimeSpan.FromHours(20);

        static DateTime? Utc(string iso)
        {
            DateTime t;
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return null;
            return DateTime.SpecifyKind(t, DateTimeKind.Utc);
        }

        /// The source credit under the price line: elprisenligenu.dk asks for it when their prices are shown; null = none.
        public static string Credit(Result r)
        {
            if (r == null || !r.Ok || !string.Equals(r.Source, CreditSource, StringComparison.OrdinalIgnoreCase)) return null;
            return L.S("Prices: ", "Priser: ");
        }

        public const string CreditSource = "elprisenligenu.dk", CreditUrl = "https://www.elprisenligenu.dk";

        /// A local time for the line: "14:20" today, else "10/11 14:20" (times always with ":").
        static string At(DateTime local, DateTime localToday)
        {
            return local.Date == localToday.Date ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("d/M HH:mm", CultureInfo.InvariantCulture);
        }

        /// The line under the electricity price (MainForm.FetchPowerPrice's texts). applied = the box got the price.
        /// toLocal turns a UTC time into the browser's local time.
        public static string Line(Result r, double addOn, bool vat, bool applied, Func<DateTime, DateTime> toLocal, DateTime localToday)
        {
            if (!r.Ok && r.NoCurrent)
                return L.F("Couldn't get the live price (the newest prices are from {0}). Using your last price.",
                           "Kunne ikke hente live-prisen (de nyeste priser er fra {0}). Bruger din sidste pris.", At(toLocal(r.FetchedUtc.Value), localToday));
            if (!r.Ok)
                return L.F("Couldn't get the live price ({0}). Using your last price.",
                           "Kunne ikke hente live-prisen ({0}). Bruger din sidste pris.", r.Why);
            string total = r.Total.ToString("0.00", Num.Dk);
            string when = r.Quote.Area + " " + toLocal(r.Quote.SlotStartUtc).ToString("HH:mm", CultureInfo.InvariantCulture);
            // times always with ":" (da-DK's own time separator here is ".", the desktop shows 17:45)
            if (r.SavedUtc.HasValue)
            {
                string at = At(toLocal(r.SavedUtc.Value), localToday);
                when += r.Stale ? L.F(" (price from {0}, the live service is busy)", " (pris fra {0}, live-tjenesten har travlt)", at)
                                : L.F(" (price from {0})", " (pris fra {0})", at);
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
