using System;
using System.Collections.Generic;
using System.Globalization;

namespace PrintPrice
{
    // The quote's texts without any drawing: breakdown rows, dates, material line, plain-text quotes and the file name.
    // No System.Drawing or WinForms here, so the website (WEB defined) can link this file as it is; QuotePage draws the rows.

    enum QuoteRowKind { Head, Sub, Note, Rule }

    /// One line of the quote's price breakdown.
    sealed class QuoteRow
    {
        public QuoteRowKind Kind;
        public string Label = "", Detail = "", Amount = "";
        public int ColourIndex = -1;   // material sub-line: QuoteLine.Index (colour dot from the page's colours); -1 = no dot
        public bool Alone;             // a head line without sub-lines: a little more room under it
    }

    static class QuoteRows
    {
        static QuoteRow Head(string label, string detail, long ore, bool alone)
        {
            return new QuoteRow { Kind = QuoteRowKind.Head, Label = label, Detail = detail ?? "", Amount = Num.Kr(ore / 100.0), Alone = alone };
        }

        static QuoteRow Sub(string label, string detail, long ore, int colourIndex)
        {
            return new QuoteRow { Kind = QuoteRowKind.Sub, Label = label, Detail = detail ?? "", Amount = Num.N2(ore / 100.0), ColourIndex = colourIndex };
        }

        public static string N2(double v) { return Num.N2(v); }
        public static string PerHour { get { return L.S(" kr/h", " kr/t"); } }   // units as in the windows: kr/kg, kr/kWh, kr/h
        public static string Time(int h, int m) { return L.F("{0} h {1} m", "{0} t {1} m", h, m); }

        /// The breakdown rows by QuoteLine.Key: material head (+ a sub-line per colour and the purge note), then a rule
        /// and a head line (wear: + its parts) for every other line. Names by QuoteLine.Index (empty = "Colour N").
        public static List<QuoteRow> Build(QuoteBreakdown b, IList<string> materialNames, double purgeGrams, int hours, int minutes, string plateName)
        {
            var rows = new List<QuoteRow>();
            var lines = b.Lines;
            var mats = lines.FindAll(l => l.Key == QuoteLine.Material);
            long matOre = 0;
            foreach (var m in mats) matOre += m.Ore;
            string material = L.S("Material", "Materiale");
            Func<QuoteLine, string> matDetail = m => Num.Fmt(m.Quantity) + " g  ×  " + Num.Fmt(m.UnitPrice) + " kr/kg";
            if (mats.Count == 1)
                rows.Add(Head(material, matDetail(mats[0]), matOre, true));
            else
            {
                rows.Add(Head(material, "", matOre, false));
                foreach (var m in mats)
                {
                    string name = materialNames != null && m.Index >= 0 && m.Index < materialNames.Count ? materialNames[m.Index] : "";
                    if (string.IsNullOrEmpty(name)) name = L.F("Colour {0}", "Farve {0}", m.Index + 1);
                    rows.Add(Sub(name, matDetail(m), m.Ore, m.Index));
                }
                if (purgeGrams > 0)
                    rows.Add(new QuoteRow { Kind = QuoteRowKind.Note, Label = L.F("incl. {0} g filament used for colour changes",
                        "inkl. {0} g filament brugt til farveskift", Num.Fmt(Math.Round(purgeGrams))) });
            }
            foreach (var l in lines)
            {
                if (l.Key == QuoteLine.Material) continue;
                rows.Add(new QuoteRow { Kind = QuoteRowKind.Rule });
                switch (l.Key)
                {
                    case QuoteLine.Power:
                        rows.Add(Head(L.S("Electricity", "Strøm"), N2(l.Quantity) + " kWh  ×  " + N2(l.UnitPrice) + " kr/kWh", l.Ore, true));
                        break;
                    case QuoteLine.Wear:
                        bool parts = l.Parts != null && l.Parts.Count > 0;
                        rows.Add(Head(L.S("Machine wear", "Maskinslid"), Time(hours, minutes) + "  ×  " + N2(l.UnitPrice) + PerHour.TrimEnd(), l.Ore, !parts));
                        if (parts)
                            foreach (var part in l.Parts)
                                rows.Add(Sub(PartName(part.Key, plateName), N2(part.UnitPrice) + PerHour, part.Ore, -1));
                        break;
                    case QuoteLine.Failed:
                        rows.Add(Head(L.S("Failed-print allowance", "Tillæg for fejlprint"),
                            L.F("{0} % of material + electricity", "{0} % af materiale + strøm", Num.Fmt(l.Quantity)), l.Ore, true));
                        break;
                    case QuoteLine.Payback:
                        rows.Add(Head(L.S("Earlier failed prints", "Tidligere fejlprint"), "", l.Ore, true));
                        break;
                    case QuoteLine.Work:
                        rows.Add(Head(L.S("Work & handling", "Arbejde & håndtering"), "", l.Ore, true));
                        break;
                    case QuoteLine.Minimum:
                        rows.Add(Head(L.S("Minimum price top-up", "Tillæg op til minimumspris"),
                            L.F("lowest price {0} kr.", "laveste pris {0} kr.", Num.Fmt(l.Quantity)), l.Ore, true));
                        break;
                    case QuoteLine.Rounding:
                        rows.Add(Head(L.S("Rounding", "Afrunding"),
                            L.F("up to nearest {0} kr.", "op til nærmeste {0} kr.", Num.Fmt(l.Quantity)), l.Ore, true));
                        break;
                    default:
                        rows.Add(Head(l.Key, "", l.Ore, true));
                        break;
                }
            }
            return rows;
        }

        /// The wear part's name (QuoteLine.WearPrinter / WearParts / WearHotend / WearPlate; plate = "Smooth PEI" or "").
        public static string PartName(string key, string plate)
        {
            switch (key)
            {
                case QuoteLine.WearPrinter: return L.S("Printer", "Printer");
                case QuoteLine.WearParts: return L.S("Spare parts & upkeep", "Reservedele & vedligehold");
                case QuoteLine.WearHotend: return L.S("Nozzle / hotend", "Dyse / hotend");
                case QuoteLine.WearPlate:
                    return string.IsNullOrEmpty(plate) ? L.S("Build plate", "Byggeplade")
                         : L.F("Build plate ({0})", "Byggeplade ({0})", plate);
            }
            return key;
        }

        /// "4 November 2026" / "4. november 2026".
        public static string LongDate(DateTime t)
        {
            return L.Danish ? t.ToString("d. MMMM yyyy", Num.Dk) : t.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"));
        }

        /// The details' Material line: 2+ colours = "PLA + PETG, 4 colours" (types in order, each once; no types = "4 colours"),
        /// else the one material's name.
        public static string MaterialLine(IEnumerable<string> types, int colours, string single)
        {
            if (colours < 2) return single ?? "";
            var seen = new List<string>();
            if (types != null)
                foreach (var t0 in types)
                {
                    string t = (t0 ?? "").Trim();
                    if (t.Length > 0 && !seen.Contains(t)) seen.Add(t);
                }
            string n = L.F("{0} colours", "{0} farver", colours);
            return seen.Count > 0 ? string.Join(" + ", seen) + ", " + n : n;
        }

        /// The model's name for the quote: the name, else "3D print".
        public static string ModelName(string model)
        {
            string m = (model ?? "").Trim();
            return m.Length > 0 ? m : L.S("3D print", "3D-print");
        }

        /// Short link for the quote: "printables.com/model/1648230" (printablesId = Printables.ParseId on the desktop);
        /// other web links without "https://www."; local files (paths) are never shown.
        public static string ShortLink(string url, Func<string, string> printablesId)
        {
            string u = (url ?? "").Trim();
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "";
            string id = printablesId != null ? printablesId(u) : null;
            if (id != null) return "printables.com/model/" + id;
            u = u.Substring(u.IndexOf("//", StringComparison.Ordinal) + 2);
            if (u.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) u = u.Substring(4);
            return u.Length > 60 ? u.Substring(0, 60) + "…" : u;
        }

        // the characters Windows forbids in file names (= .NET Framework's Path.GetInvalidFileNameChars, fixed so the
        // website gives the same names as the desktop)
        static readonly char[] BadFileChars = MakeBadFileChars();
        static char[] MakeBadFileChars()
        {
            var list = new List<char> { '"', '<', '>', '|', ':', '*', '?', '\\', '/' };
            for (int c = 0; c < 32; c++) list.Add((char)c);
            return list.ToArray();
        }

        /// Default file name (no extension): "2026-11-04 3D Benchy" (+ " - Mette Hansen" with a customer); model cut to
        /// 40 characters, customer to 30, characters Windows forbids -> "_".
        public static string FileName(DateTime date, string model, string customer)
        {
            Func<string, int, string> cut = (s, max) => { s = (s ?? "").Trim(); return s.Length > max ? s.Substring(0, max).TrimEnd() : s; };
            string name = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string m = cut(model, 40), c = cut(customer, 30);
            if (m.Length > 0) name += " " + m;
            if (c.Length > 0) name += " - " + c;
            foreach (char bad in BadFileChars) name = name.Replace(bad, '_');
            return name.Trim();
        }

        /// The job on screen as plain text (Copy text): "For:" line with a customer, the typed link or file, time and
        /// grams (+ " (N colours)" when colours > 0), both prices.
        public static string JobText(string customer, string source, int hours, int minutes, string grams, int colours,
                                     double friendsPrice, double customerPrice)
        {
            string nl = Environment.NewLine;
            string text = L.S("3D print quote", "Tilbud på 3D-print") + nl;
            if (!string.IsNullOrEmpty(customer)) text += L.S("For: ", "Til: ") + customer + nl;
            string src = (source ?? "").Trim();
            if (src.Length > 0) text += "Model: " + src + nl;
            text += L.F("Print time: {0} h {1} m, filament: {2} g", "Printtid: {0} t {1} m, filament: {2} g",
                        hours, minutes, (grams ?? "").Trim()) +
                    (colours > 0 ? L.F(" ({0} colours)", " ({0} farver)", colours) : "") + nl;
            text += L.S("Friends & family: ", "Venner og familie: ") + Num.Kr(friendsPrice) + nl;
            text += L.S("Customer: ", "Kunde: ") + Num.Kr(customerPrice);
            return text;
        }

        /// A saved record as plain text: title, name, model, link (already short), time and grams, the chosen price;
        /// invoice: number, dates (History.InvoiceInfo: the invoice's snapshot when it has one), payment.
        public static string RecordText(QuoteRecord r, bool friends, bool invoice, AppSettings settings, string link)
        {
            string nl = Environment.NewLine;
            string text = (invoice ? L.S("Invoice", "Faktura") + " " + r.InvoiceNo : L.S("3D print quote", "Tilbud på 3D-print") + " " + History.QuoteNumber(r)) + nl;
            if (!string.IsNullOrEmpty(r.Customer)) text += (invoice ? L.S("Bill to: ", "Kunde: ") : L.S("For: ", "Til: ")) + r.Customer + nl;
            text += "Model: " + ModelName(r.Model) + nl;
            if (!string.IsNullOrEmpty(link)) text += link + nl;
            text += L.F("Print time: {0} h {1} m, filament: {2} g", "Printtid: {0} t {1} m, filament: {2} g", r.Hours, r.Minutes, Num.Fmt(r.Grams)) + nl;
            text += (invoice ? L.S("Total: ", "I alt: ") : L.S("Price: ", "Pris: ")) + Num.Kr(friends ? r.FriendsPrice : r.OthersPrice);
            if (invoice)
            {
                DateTime billed;
                if (!History.TryTime(r.BilledAt, out billed)) billed = DateTime.Now;
                var inv = History.InvoiceInfo(r, settings, billed);
                text += nl + L.F("Invoice date {0}, please pay by {1}", "Fakturadato {0}, betales senest {1}",
                                 LongDate(billed), LongDate(inv.Due));
                string mp = (inv.MobilePay ?? "").Trim(), bank = (inv.Bank ?? "").Trim();
                if (mp.Length > 0) text += nl + "MobilePay: " + mp;
                if (bank.Length > 0) text += nl + L.S("Bank: ", "Bank: ") + bank;
                if (mp.Length > 0 || bank.Length > 0)
                    text += nl + L.F("Please write invoice no. {0} with the payment.", "Skriv fakturanr. {0} ved betalingen.", r.InvoiceNo);
            }
            return text;
        }
    }
}
