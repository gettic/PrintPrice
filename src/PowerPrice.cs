using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace PrintPrice
{
    public class PowerQuote
    {
        public string Area;
        public DateTime SlotStartUtc;
        public double SpotDkkPerKwh;
    }

    /// Live Danish day-ahead (spot) prices from Energinet's free Energi Data Service API.
    /// Dataset "DayAheadPrices": 15-minute prices in DKK/MWh for DK1 (west) and DK2 (east).
    public static class PowerPrice
    {
        public const double VatRate = 0.25;

        /// What you actually pay per kWh: (spot + tariffs/taxes/supplier fee) + VAT.
        public static double TotalPrice(double spotDkkPerKwh, double addOnDkkPerKwh, bool includeVat)
        {
            double net = spotDkkPerKwh + addOnDkkPerKwh;
            return includeVat ? net * (1 + VatRate) : net;
        }

        public static PowerQuote GetCurrent(string area)
        {
            if (area != "DK1" && area != "DK2") area = "DK1";
            DateTime nowUtc = DateTime.UtcNow;
            string url = "https://api.energidataservice.dk/dataset/DayAheadPrices" +
                         "?start=now-PT3H&end=now%2BPT1H" +
                         "&filter=" + Uri.EscapeDataString("{\"PriceArea\":[\"" + area + "\"]}") +
                         "&columns=TimeUTC,DayAheadPriceDKK&sort=TimeUTC%20asc&limit=1000";

            string body;
            using (var wc = Net.Client(Net.DefaultUserAgent))
            {
                try { body = wc.DownloadString(url); }
                catch (WebException ex)
                {
                    var resp = ex.Response as HttpWebResponse;
                    if (resp != null && (int)resp.StatusCode == 429)
                        throw new SliceException(L.S("the price service is busy right now", "pristjenesten har travlt lige nu"));
                    throw new SliceException(L.S("could not reach Energi Data Service", "kunne ikke nå Energi Data Service"));
                }
            }
            return PickCurrent(body, area, nowUtc);
        }

        /// Picks the latest price slot that has started at or before nowUtc.
        public static PowerQuote PickCurrent(string json, string area, DateTime nowUtc)
        {
            var d = Json.Deserialize<Dictionary<string, object>>(json);
            object recs;
            if (!d.TryGetValue("records", out recs) || !(recs is IEnumerable))
                throw new SliceException(L.S("unexpected answer from Energi Data Service", "uventet svar fra Energi Data Service"));

            PowerQuote best = null;
            foreach (var o in (IEnumerable)recs)
            {
                var r = o as Dictionary<string, object>;
                if (r == null || r["TimeUTC"] == null || r["DayAheadPriceDKK"] == null) continue;
                DateTime t;
                if (!DateTime.TryParse(Convert.ToString(r["TimeUTC"], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                                       DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) continue;
                if (t > nowUtc) continue;
                if (best == null || t > best.SlotStartUtc)
                {
                    best = new PowerQuote
                    {
                        Area = area, SlotStartUtc = t,
                        SpotDkkPerKwh = Convert.ToDouble(r["DayAheadPriceDKK"], CultureInfo.InvariantCulture) / 1000.0
                    };
                }
            }
            if (best == null || nowUtc - best.SlotStartUtc > TimeSpan.FromHours(2))
                throw new SliceException(L.F("no current price published for {0}", "ingen aktuel pris offentliggjort for {0}", area));
            return best;
        }
    }
}
