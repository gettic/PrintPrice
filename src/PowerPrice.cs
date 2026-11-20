using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
#if !WEB
using System.Net;
#endif
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

#if !WEB
        const long MaxReplyBytes = 2 * 1048576;   // a normal reply is ~2 KB

        /// Desktop only (WebClient); the website gets the same answer through the Worker's /power route (worker\).
        public static PowerQuote GetCurrent(string area)
        {
            if (area != "DK1" && area != "DK2") area = "DK1";
            DateTime nowUtc = DateTime.UtcNow;
            string url = "https://api.energidataservice.dk/dataset/DayAheadPrices" +
                         "?start=now-PT3H&end=now%2BPT1H" +
                         "&filter=" + Uri.EscapeDataString("{\"PriceArea\":[\"" + area + "\"]}") +
                         "&columns=TimeUTC,DayAheadPriceDKK&sort=TimeUTC%20asc&limit=1000";

            string body = null;
            using (var wc = Net.Client(Net.DefaultUserAgent))
            {
                try
                {
                    System.Threading.Tasks.Task<byte[]> call = null;
                    Net.Run(wc, () => call = wc.DownloadDataTaskAsync(new Uri(url)), System.Threading.CancellationToken.None, 20000, MaxReplyBytes);
                    body = System.Text.Encoding.UTF8.GetString(call.Result).TrimStart('﻿');
                }
                catch (WebException ex)
                {
                    var resp = ex.Response as HttpWebResponse;
                    if (resp != null && (int)resp.StatusCode == 429)
                        throw new SliceException(L.S("the price service is busy right now", "pristjenesten har travlt lige nu"));
                    throw new SliceException(L.S("could not reach Energi Data Service", "kunne ikke nå Energi Data Service"));
                }
                catch (System.IO.InvalidDataException)
                {
                    throw new SliceException(L.S("unexpected answer from Energi Data Service", "uventet svar fra Energi Data Service"));
                }
                catch (Exception)
                {
                    throw new SliceException(L.S("could not reach Energi Data Service", "kunne ikke nå Energi Data Service"));
                }
            }
            return PickCurrent(body, area, nowUtc);
        }
#endif

        /// Picks the latest price slot that has started at or before nowUtc.
        public static PowerQuote PickCurrent(string json, string area, DateTime nowUtc)
        {
            Dictionary<string, object> d = null;
            try { d = Json.Deserialize<Dictionary<string, object>>(json); }
            catch (Exception) { d = null; }   // HTML error page, cut-off text...
            object recs;
            if (d == null || !d.TryGetValue("records", out recs) || !(recs is IEnumerable))
                throw new SliceException(L.S("unexpected answer from Energi Data Service", "uventet svar fra Energi Data Service"));

            PowerQuote best = null;
            foreach (var o in (IEnumerable)recs)
            {
                var r = o as Dictionary<string, object>;
                object time, price;
                if (r == null || !r.TryGetValue("TimeUTC", out time) || time == null ||
                    !r.TryGetValue("DayAheadPriceDKK", out price) || price == null) continue;
                DateTime t;
                if (!DateTime.TryParse(Convert.ToString(time, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                                       DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) continue;
                if (t > nowUtc) continue;
                double mwh;
                try { mwh = Convert.ToDouble(price, CultureInfo.InvariantCulture); }
                catch (Exception) { continue; }   // not a number
                if (double.IsNaN(mwh) || double.IsInfinity(mwh)) continue;
                if (best == null || t > best.SlotStartUtc)
                    best = new PowerQuote { Area = area, SlotStartUtc = t, SpotDkkPerKwh = mwh / 1000.0 };
            }
            if (best == null || nowUtc - best.SlotStartUtc > TimeSpan.FromHours(2))
                throw new SliceException(L.F("no current price published for {0}", "ingen aktuel pris offentliggjort for {0}", area));
            return best;
        }
    }
}
