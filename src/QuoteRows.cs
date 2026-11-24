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
            var lines = FoldMinimum(b.Lines);   // the customer never sees a minimum-price top-up: it is part of Work & handling
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
                    case QuoteLine.Plates:   // batch & plates: the plate change fee
                        rows.Add(Head(PlatesRowLabel(), PlatesRowDetail(l, true), l.Ore, true));
                        break;
                    case QuoteLine.Failed:
                        rows.Add(Head(L.S("Failed-print allowance", "Tillæg for fejlprint"),
                            L.F("{0} % of material + electricity", "{0} % af materiale + strøm", Num.Fmt(l.Quantity)), l.Ore, true));
                        break;
                    case QuoteLine.Work:   // also holds any minimum top-up and failed-print payback (FoldMinimum)
                        rows.Add(Head(L.S("Work & handling", "Arbejde & håndtering"), "", l.Ore, true));
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

        /// Issue #8 step 3: the "incl. 1,7 g for the prime tower" note (no amount) at the end of the material part (before the first
        /// rule), on quotes / invoices when AppSettings.QuoteShowTower is on. grams <= 0 = no row. Returns rows.
        public static List<QuoteRow> AddTowerNote(List<QuoteRow> rows, double grams)
        {
            if (rows == null || !(grams > 0.005)) return rows;
            int at = rows.FindIndex(r => r.Kind == QuoteRowKind.Rule);
            if (at < 0) at = rows.Count;
            if (at == 1 && rows[0].Kind == QuoteRowKind.Head) rows[0].Alone = false;   // a single-material head gets its note right under it
            rows.Insert(at, new QuoteRow { Kind = QuoteRowKind.Note, Label = TowerNote(grams) });
            return rows;
        }

        /// "incl. 1,7 g for the prime tower" / "inkl. 1,7 g til primetårnet" (one decimal; worded like the colour-change note).
        public static string TowerNote(double grams)
        {
            return L.F("incl. {0} g for the prime tower", "inkl. {0} g til primetårnet", grams.ToString("0.0", Num.Dk));
        }

        /// The rows of a combined bill's breakdown (History.GroupBreakdown as b.Lines; names by its Materials index, hours /
        /// minutes = the jobs' summed time). As Build, but the details "g × kr/kg", "kWh × kr/kWh", "time × kr/h" only when
        /// sameRates and the rate is known (else the summed grams / kWh / time); the top-up folded into Work & handling
        /// (FoldMinimum); "%" and "nearest" only when
        /// known; the "other" row (older quotes without a split); at most maxColours filament sub-rows, the rest summed in
        /// one "N more colours" row.
        public static List<QuoteRow> BuildGroup(QuoteBreakdown b, IList<string> materialNames, int hours, int minutes, string plateName,
                                                bool sameRates, int maxColours)
        {
            return BuildGroup(b, materialNames, hours, minutes, plateName, sameRates, maxColours, false);
        }

        /// As above; shortWear = the wear parts folded into one Note row under the wear head ("Printer 1,20 · Parts 0,40 ·
        /// Nozzle 0,10 · Plate 0,05"), so a combined bill fits one A4 page (QuotePage GroupLevel 1+). Same øre either way.
        public static List<QuoteRow> BuildGroup(QuoteBreakdown b, IList<string> materialNames, int hours, int minutes, string plateName,
                                                bool sameRates, int maxColours, bool shortWear)
        {
            var rows = new List<QuoteRow>();
            var lines = FoldMinimum(b.Lines);   // the bill's top-up is shown inside Work & handling, never as its own row
            var mats = lines.FindAll(l => l.Key == QuoteLine.Material);
            long matOre = 0;
            foreach (var m in mats) matOre += m.Ore;
            Func<QuoteLine, string> matDetail = m => sameRates && m.UnitPrice > 0
                ? Num.Fmt(m.Quantity) + " g  ×  " + Num.Fmt(m.UnitPrice) + " kr/kg" : Num.Fmt(Math.Round(m.Quantity, 2)) + " g";
            Func<QuoteLine, string> matName = m =>
            {
                string name = materialNames != null && m.Index >= 0 && m.Index < materialNames.Count ? materialNames[m.Index] : "";
                return string.IsNullOrEmpty(name) ? L.F("Colour {0}", "Farve {0}", m.Index + 1) : name;
            };
            string material = L.S("Material", "Materiale");
            if (mats.Count == 1) rows.Add(Head(material, matDetail(mats[0]), matOre, true));
            else if (mats.Count > 1)
            {
                rows.Add(Head(material, "", matOre, false));
                int shown = mats.Count > maxColours ? Math.Max(1, maxColours - 1) : mats.Count;
                for (int i = 0; i < shown; i++) rows.Add(Sub(matName(mats[i]), matDetail(mats[i]), mats[i].Ore, mats[i].Index));
                if (shown < mats.Count)
                {
                    long rest = 0;
                    double grams = 0;
                    for (int i = shown; i < mats.Count; i++) { rest += mats[i].Ore; grams += mats[i].Quantity; }
                    rows.Add(Sub(L.F("{0} more colours", "{0} flere farver", mats.Count - shown), Num.Fmt(Math.Round(grams, 2)) + " g", rest, -1));
                }
            }
            foreach (var l in lines)
            {
                if (l.Key == QuoteLine.Material) continue;
                if (rows.Count > 0) rows.Add(new QuoteRow { Kind = QuoteRowKind.Rule });
                bool rate = sameRates && l.UnitPrice > 0;
                switch (l.Key)
                {
                    case QuoteLine.Power:
                        rows.Add(Head(L.S("Electricity", "Strøm"), rate ? N2(l.Quantity) + " kWh  ×  " + N2(l.UnitPrice) + " kr/kWh" : N2(l.Quantity) + " kWh", l.Ore, true));
                        break;
                    case QuoteLine.Wear:
                        bool parts = l.Parts != null && l.Parts.Count > 0;
                        rows.Add(Head(L.S("Machine wear", "Maskinslid"), rate ? Time(hours, minutes) + "  ×  " + N2(l.UnitPrice) + PerHour.TrimEnd() : Time(hours, minutes),
                                      l.Ore, !parts));
                        if (parts && shortWear)
                        {
                            var bits = new List<string>();
                            foreach (var part in l.Parts) bits.Add(ShortPartName(part.Key) + " " + Num.N2(part.Ore / 100.0));
                            rows.Add(new QuoteRow { Kind = QuoteRowKind.Note, Label = string.Join("  ·  ", bits), Detail = "", Amount = "", ColourIndex = -1 });
                        }
                        else if (parts)
                            foreach (var part in l.Parts)
                                rows.Add(Sub(PartName(part.Key, plateName), sameRates && part.UnitPrice > 0 ? N2(part.UnitPrice) + PerHour : "", part.Ore, -1));
                        break;
                    case QuoteLine.Plates:   // batch & plates: the plate change fee (the rate only when every job used the same)
                        rows.Add(Head(PlatesRowLabel(), PlatesRowDetail(l, rate), l.Ore, true));
                        break;
                    case QuoteLine.Failed:
                        rows.Add(Head(L.S("Failed-print allowance", "Tillæg for fejlprint"), l.Quantity > 0
                            ? L.F("{0} % of material + electricity", "{0} % af materiale + strøm", Num.Fmt(l.Quantity)) : "", l.Ore, true));
                        break;
                    case QuoteLine.Work:   // also holds the bill's top-up and any failed-print payback (FoldMinimum)
                        rows.Add(Head(L.S("Work & handling", "Arbejde & håndtering"), "", l.Ore, true));
                        break;
                    case QuoteLine.Other:
                        rows.Add(Head(L.S("Other", "Andet"), "", l.Ore, true));
                        break;
                    case QuoteLine.Rounding:
                        rows.Add(Head(L.S("Rounding", "Afrunding"), l.Quantity > 0
                            ? L.F("up to nearest {0} kr.", "op til nærmeste {0} kr.", Num.Fmt(l.Quantity)) : "", l.Ore, true));
                        break;
                    default:
                        rows.Add(Head(l.Key, "", l.Ore, true));
                        break;
                }
            }
            return rows;
        }

        /// The breakdown lines as the customer sees them (the user's rules: never show the minimum-price top-up, and never
        /// show the failed-print payback as its own line): every "minimum" and "payback" line's øre is added to the Work &
        /// handling line (copied, with no % detail left on it), or becomes a Work & handling line in the first folded line's
        /// place when there is none. The øre sum is unchanged. The saved data (Pricing.Breakdown, History.GroupBreakdown,
        /// InvoiceTopUp) keeps its own minimum / payback lines; only the display folds them.
        public static List<QuoteLine> FoldMinimum(List<QuoteLine> lines)
        {
            var res = new List<QuoteLine>();
            if (lines == null) return res;
            long minOre = 0;
            int minAt = -1;
            foreach (var l in lines)
                if (l.Key == QuoteLine.Minimum || l.Key == QuoteLine.Payback) { minOre += l.Ore; if (minAt < 0) minAt = res.Count; }
                else res.Add(l);
            if (minAt < 0) return res;
            int work = res.FindIndex(l => l.Key == QuoteLine.Work);
            if (work >= 0)
            {
                var w = res[work];
                res[work] = new QuoteLine { Key = QuoteLine.Work, Ore = w.Ore + minOre, Exact = w.Exact + minOre / 100.0, Index = w.Index };
            }
            else if (minOre != 0) res.Insert(minAt, new QuoteLine { Key = QuoteLine.Work, Ore = minOre, Exact = minOre / 100.0 });
            return res;
        }

        /// Each job's amount on a combined bill as the customer sees it, in øre: its line (GroupBillInfo.Prices) plus its
        /// share of the minimum top-up (Pricing.ShareOre by price, as History.MakeInvoice saves it), so the job amounts add up
        /// to the total and no minimum line is needed. Friends / no top-up: the lines as they are. An invoice already made
        /// (bill.Invoiced): each job's stored InvoiceTopUp, never split again, so the amounts are the saved ones.
        public static long[] JobOre(GroupBillInfo bill)
        {
            int n = bill.Prices.Length;
            var res = new long[n];
            bool stored = bill.Invoiced && !bill.Friends;
            long[] share = !stored && bill.TopUp > 0 && !bill.Friends ? Pricing.ShareOre(bill.Prices, Pricing.ShownOre(bill.TopUp)) : null;
            for (int i = 0; i < n; i++)
            {
                double p = bill.Prices[i];
                long ore = !double.IsNaN(p) && !double.IsInfinity(p) && p > 0 ? Pricing.ShownOre(p) : 0;   // as Pricing.BillTotal sums them
                if (stored)
                {
                    double t = i < bill.Jobs.Count && bill.Jobs[i] != null ? bill.Jobs[i].InvoiceTopUp : 0;
                    res[i] = ore + (!double.IsNaN(t) && !double.IsInfinity(t) && t > 0 ? Pricing.ShownOre(t) : 0);   // as History.GroupBill sums them
                }
                else res[i] = ore + (share != null && i < share.Length ? share[i] : 0);
            }
            return res;
        }

        /// The wear part's name (QuoteLine.WearPrinter / WearParts / WearHotend / WearPlate; plate = "Smooth PEI" or "").
        /// The four print statuses as the status boxes and History show them (issue #7).
        public static string StateName(PrintState s)
        {
            switch (s)
            {
                case PrintState.Printing: return L.S("Printing now", "Printer nu");
                case PrintState.Printed: return L.S("Printed", "Printet");
                case PrintState.Failed: return L.S("Failed", "Fejlet");
                default: return L.S("Not printed yet", "Ikke printet endnu");
            }
        }

        /// The short wear part names of BuildGroup's one-line wear note.
        public static string ShortPartName(string key)
        {
            switch (key)
            {
                case QuoteLine.WearPrinter: return L.S("Printer", "Printer");
                case QuoteLine.WearParts: return L.S("Parts", "Dele");
                case QuoteLine.WearHotend: return L.S("Nozzle", "Dyse");
                case QuoteLine.WearPlate: return L.S("Build plate", "Byggeplade");
            }
            return key;
        }

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

        /// An invoice's file name (no extension, issue #6): "2026-48213 - Mette Hansen" (the invoice number, then the
        /// name on the invoice cut to 30 characters); characters Windows forbids -> "_". Single and combined invoices.
        public static string InvoiceFileName(string code, string customer)
        {
            string name = (code ?? "").Trim();
            string c = (customer ?? "").Trim();
            if (c.Length > 30) c = c.Substring(0, 30).TrimEnd();
            if (c.Length > 0) name += (name.Length > 0 ? " - " : "") + c;
            foreach (char bad in BadFileChars) name = name.Replace(bad, '_');
            return name.Trim();
        }

        /// The job on screen as plain text (Copy text): "For:" line with a customer, the typed link or file, time and
        /// grams (+ " (N colours)" when colours > 0), both prices.
        public static string JobText(string customer, string source, int hours, int minutes, string grams, int colours,
                                     double friendsPrice, double customerPrice)
        {
            return JobText(customer, source, hours, minutes, grams, colours, friendsPrice, customerPrice, 1, 1, false);
        }

        /// Batch & plates: as above for quantity copies on plates plates. 2+ copies: a "Quantity: 4 pcs" line after the
        /// model, "Print time: 9 h 12 m on 2 plates, …" and, with showEach (AppSettings.QuoteShowUnitPrice), each price
        /// followed by " (18,18 kr. each)". 1 copy = exactly the old text.
        public static string JobText(string customer, string source, int hours, int minutes, string grams, int colours,
                                     double friendsPrice, double customerPrice, int quantity, int plates, bool showEach)
        {
            string nl = Environment.NewLine;
            bool batch = quantity > 1;
            string text = L.S("3D print quote", "Tilbud på 3D-print") + nl;
            if (!string.IsNullOrEmpty(customer)) text += L.S("For: ", "Til: ") + customer + nl;
            string src = (source ?? "").Trim();
            if (src.Length > 0) text += "Model: " + src + nl;
            if (batch) text += History.QuantityText(quantity) + nl;
            text += PrintTimeText(hours, minutes, batch ? plates : 1) + L.F(", filament: {0} g", ", filament: {0} g", (grams ?? "").Trim()) +
                    (colours > 0 ? L.F(" ({0} colours)", " ({0} farver)", colours) : "") + nl;
            text += L.S("Friends & family: ", "Venner og familie: ") + Num.Kr(friendsPrice) + EachSuffix(friendsPrice, quantity, showEach) + nl;
            text += L.S("Customer: ", "Kunde: ") + Num.Kr(customerPrice) + EachSuffix(customerPrice, quantity, showEach);
            return text;
        }

        // ---------- batch & plates (issue #9 / #10): shared texts (desktop and website) ----------

        /// "Print time: 9 h 12 m" + " on 2 plates" for 2+ plates (DA "Printtid: 9 t 12 m på 2 plader").
        public static string PrintTimeText(int hours, int minutes, int plates)
        {
            return L.F("Print time: {0} h {1} m", "Printtid: {0} t {1} m", hours, minutes) + OnPlates(plates);
        }

        /// " on 2 plates" (leading space) for 2+ plates, else "".
        public static string OnPlates(int plates)
        {
            string s = History.OnPlatesText(plates);
            return s.Length > 0 ? " " + s : "";
        }

        /// "9 h 12 m on 2 plates" (QuoteRows.Time + OnPlates): the quote page's Print time value of a batch.
        public static string TimeOnPlates(int hours, int minutes, int plates)
        {
            return Time(hours, minutes) + OnPlates(plates);
        }

        /// " (18,18 kr. each)" after a price for 2+ copies when showEach, else "".
        public static string EachSuffix(double total, int quantity, bool showEach)
        {
            if (!showEach || quantity <= 1) return "";
            return " (" + History.EachPriceText(total, quantity) + ")";
        }

        /// The breakdown row of the plate change fee: "Plate changes" / "Pladeskift".
        public static string PlatesRowLabel() { return L.S("Plate changes", "Pladeskift"); }

        /// Its detail: "2 × 5,00 kr." (plate changes × fee; withRate false or no rate = "2 changes" / "2 skift", ui-text #1:
        /// it counts the changes, not the plates).
        public static string PlatesRowDetail(QuoteLine l, bool withRate)
        {
            if (l == null) return "";
            int n = (int)Math.Round(l.Quantity);
            if (withRate && l.UnitPrice > 0) return n + "  ×  " + Num.Kr(l.UnitPrice);
            return n == 1 ? L.S("1 change", "1 skift") : L.F("{0} changes", "{0} skift", n);
        }

        /// The quote page / Copy text's total small line with the price per piece: "4 pcs · 18,18 kr. each" ("" for 1
        /// copy or when showEach is off).
        public static string EachLine(double total, int quantity, bool showEach)
        {
            return showEach ? History.EachText(total, quantity) : "";
        }

        /// A saved record as plain text: title, name, model, link (already short), time and grams, the chosen price;
        /// invoice: "Bill to" name, number, dates (History.InvoiceInfo: the invoice's snapshot when it has one), payment.
        public static string RecordText(QuoteRecord r, bool friends, bool invoice, AppSettings settings, string link)
        {
            string nl = Environment.NewLine;
            string text = (invoice ? L.S("Invoice", "Faktura") + " " + r.InvoiceNo : L.S("3D print quote", "Tilbud på 3D-print") + " " + History.QuoteNumber(r)) + nl;
            // invoice: the name it was made out to (History.InvoiceInfo: stored BilledTo, else Customer)
            string who = invoice ? History.InvoiceInfo(r, settings).BilledTo : (r.Customer ?? "").Trim();
            if (!string.IsNullOrEmpty(who)) text += (invoice ? L.S("Bill to: ", "Kunde: ") : L.S("For: ", "Til: ")) + who + nl;
            // batch & plates: "Model: 4 × Benchy", "Print time: … on 2 plates", "Price: … (18,18 kr. each)"; 1 copy as before
            int qty = History.QuantityOf(r);
            bool showEach = settings == null || settings.QuoteShowUnitPrice != 0;
            text += "Model: " + History.CopiesName(ModelName(r.Model), qty) + nl;
            if (!string.IsNullOrEmpty(link)) text += link + nl;
            text += PrintTimeText(r.Hours, r.Minutes, qty > 1 ? History.PlatesOf(r) : 1) +
                    L.F(", filament: {0} g", ", filament: {0} g", Num.Fmt(r.Grams)) + nl;
            double price = friends ? r.FriendsPrice : r.OthersPrice;
            text += (invoice ? L.S("Total: ", "I alt: ") : L.S("Price: ", "Pris: ")) + Num.Kr(price) + EachSuffix(price, qty, showEach);
            if (invoice)
            {
                DateTime billed;
                if (!History.TryTime(r.BilledAt, out billed)) billed = DateTime.Now;
                var inv = History.InvoiceInfo(r, settings, billed);
                string paid = string.IsNullOrEmpty(r.InvoiceNo) ? "" : PaidLine(new[] { r });   // paid-tick
                text += nl + (paid.Length > 0 ? L.F("Invoice date {0}.", "Fakturadato {0}.", LongDate(billed)) + " " + paid
                                              : L.F("Invoice date {0}, please pay by {1}", "Fakturadato {0}, betales senest {1}", LongDate(billed), LongDate(inv.Due)));
                string mp = paid.Length > 0 ? "" : (inv.MobilePay ?? "").Trim(), bank = paid.Length > 0 ? "" : (inv.Bank ?? "").Trim();   // paid: no payment details
                if (mp.Length > 0) text += nl + "MobilePay: " + mp;
                if (bank.Length > 0) text += nl + L.S("Bank: ", "Bank: ") + bank;
                if (mp.Length > 0 || bank.Length > 0)
                    text += nl + L.F("Please write invoice no. {0} with the payment.", "Skriv fakturanr. {0} ved betalingen.", r.InvoiceNo);
            }
            return text;
        }

        // ---------- colour-change / prime tower note (desktop's material table note; shared with the website) ----------

        /// "prime tower 1,7 g · 5 min" (DA "primetårn …"); withTime false / seconds &lt; 30 = grams only; grams &lt;= 0 = "".
        public static string TowerText(double grams, double seconds, bool withTime)
        {
            if (!(grams > 0.005)) return "";
            string s = L.F("prime tower {0} g", "primetårn {0} g", grams.ToString("0.0", Num.Dk));   // one decimal (qol)
            if (withTime && seconds >= 30)
            {
                int min = (int)Math.Round(seconds / 60);
                s += " · " + (min >= 60 ? (min / 60) + L.S(" h ", " t ") + (min % 60) + " min" : min + " min");
            }
            return s;
        }

        /// The note's two parts: note = "incl. 124 g and 8,7 h for colour changes" (the flush only: purge less the tower; grams
        /// only above 0,5 g, hours from 0,05 h; "" = none), tower = TowerText(…, true) lower-case ("" = no tower).
        public static void PurgeNoteParts(double purgeGrams, double colourChangeHours, double towerGrams, double towerSeconds,
                                          out string note, out string tower)
        {
            double tw = towerGrams > 0.005 ? towerGrams : 0;
            double flush = purgeGrams > 0 ? Math.Max(0, purgeGrams - tw) : purgeGrams;
            bool g = flush > 0.5, h = colourChangeHours >= 0.05;
            string grams = g ? flush.ToString(flush >= 10 ? "0" : "0.#", Num.Dk) : "";
            string hours = h ? colourChangeHours.ToString("0.#", Num.Dk) : "";
            note = "";
            if (g && h) note = L.F("incl. {0} g and {1} h for colour changes", "heraf {0} g og {1} t til farveskift", grams, hours);
            else if (g) note = L.F("incl. {0} g for colour changes", "heraf {0} g til farveskift", grams);
            else if (h) note = L.F("{0} h for colour changes", "{0} t til farveskift", hours);
            tower = TowerText(tw, towerSeconds, true);
        }

        /// The parts as one text: "note · tower" (twoLines: "note\nTower …"); only the tower = "Prime tower …" (capital).
        public static string PurgeNoteJoin(string note, string tower, bool twoLines)
        {
            note = note ?? ""; tower = tower ?? "";
            if (tower.Length == 0) return note;
            string up = char.ToUpper(tower[0]) + tower.Substring(1);
            if (note.Length == 0) return up;
            return twoLines ? note + "\n" + up : note + " · " + tower;
        }

        /// paid-tick: "Paid 05-11-2026 – thank you" (History.PaidText, the latest paid date) when every job is paid, else "".
        public static string PaidLine(IList<QuoteRecord> jobs)
        {
            if (jobs == null || jobs.Count == 0) return "";
            DateTime latest = DateTime.MinValue;
            foreach (var j in jobs)
            {
                DateTime day;
                if (j == null || !History.PaidOn(j, out day)) return "";
                if (day > latest) latest = day;
            }
            return History.PaidText(latest);
        }

        // ---------- combined bill (several History jobs on one quote / invoice) ----------

        /// "3 prints" / "3 print" ("1 print").
        public static string PrintsText(int n)
        {
            return n == 1 ? L.S("1 print", "1 print") : L.F("{0} prints", "{0} print", n);
        }

        /// A job's muted line on a combined bill: "3 h 10 m · 86 g · PLA Matte" (material left out when empty).
        public static string JobDetail(QuoteRecord r, string material)
        {
            string m = (material ?? "").Trim();
            // batch & plates: "9 h 12 m on 2 plates · 182 g · PLA" for 2+ copies
            string time = History.QuantityOf(r) > 1 ? TimeOnPlates(r.Hours, r.Minutes, History.PlatesOf(r)) : Time(r.Hours, r.Minutes);
            return time + " · " + Num.Fmt(r.Grams) + " g" + (m.Length > 0 ? " · " + m : "");
        }

        /// A job's faint line on a combined bill: "Quote no. 261103-1412 · 3 November 2026".
        public static string JobSub(QuoteRecord r)
        {
            DateTime t;
            if (!History.TryTime(r.SavedAt, out t)) return L.F("Quote no. {0}", "Tilbud nr. {0}", History.QuoteNumber(r));
            return L.F("Quote no. {0} · {1}", "Tilbud nr. {0} · {1}", History.QuoteNumber(r), LongDate(t));
        }

        /// "3 prints · 7 h 40 m · 182 g" (time and grams summed).
        public static string GroupSummary(IList<QuoteRecord> jobs)
        {
            int minutes = 0;
            double grams = 0;
            int n = 0;
            if (jobs != null)
                foreach (var r in jobs)
                {
                    if (r == null) continue;
                    n++;
                    minutes += Math.Max(0, r.Hours) * 60 + Math.Max(0, r.Minutes);
                    grams += Math.Max(0, r.Grams);
                }
            // batch & plates: "3 models, 9 pcs · …" when any job has 2+ copies
            var count = History.CountPrints(jobs);
            string what = count.Pieces > count.Models
                ? (count.Models == 1 ? L.S("1 model", "1 model") : L.F("{0} models", "{0} modeller", count.Models)) + ", " + History.PiecesText(count.Pieces)
                : PrintsText(n);
            return what + " · " + Time(minutes / 60, minutes % 60) + " · " + Num.Fmt(Math.Round(grams, 2)) + " g";
        }

        /// Batch & plates: the combined bill's "Plates" summary value ("5"), "" when no job has 2+ copies (no row then).
        public static string GroupPlates(IList<QuoteRecord> jobs)
        {
            var count = History.CountPrints(jobs);
            return count.Pieces > count.Models ? count.Plates.ToString(CultureInfo.InvariantCulture) : "";
        }

        /// The muted line before the total when a customer bill is below the minimum price: "Minimum price (50,00 kr.)".
        public static string MinimumLabel(double minimum)
        {
            return L.F("Minimum price ({0})", "Minimumspris ({0})", Num.Kr(minimum));
        }

        /// Default file name of a combined bill: "2026-11-19 3 prints - Lene Hansen".
        public static string GroupFileName(DateTime date, int jobs, string customer)
        {
            return FileName(date, PrintsText(jobs), customer);
        }

        /// A combined bill as plain text (Copy text): title + number, name, one line per job (name, time, grams, price),
        /// each price with its share of any top-up (JobOre; the minimum is never shown), the total; invoice: dates and payment (inv = InvoiceInfo of
        /// the first job: its snapshot when made). number = invoice code / quote number.
        public static string GroupText(GroupBillInfo bill, bool invoice, string number, string customer, DateTime date, InvoiceDetails inv)
        {
            return GroupText(bill, invoice, number, customer, date, inv, true);
        }

        /// As above. Batch & plates: a job of 2+ copies reads "- 4 × Benchy: 9 h 12 m on 2 plates, 182 g: 72,72 kr. (18,18 kr.
        /// each)" (each only with showEach), and a bill with any of them gets "Prints: 3 models, 9 pcs · Plates: 5" before the
        /// total. Bills without copies: exactly the old text.
        public static string GroupText(GroupBillInfo bill, bool invoice, string number, string customer, DateTime date, InvoiceDetails inv,
                                       bool showEach)
        {
            string nl = Environment.NewLine;
            string text = (invoice ? L.S("Invoice", "Faktura") : L.S("3D print quote", "Tilbud på 3D-print")) + " " + number + nl;
            string who = (customer ?? "").Trim();
            if (who.Length > 0) text += (invoice ? L.S("Bill to: ", "Kunde: ") : L.S("For: ", "Til: ")) + who + nl;
            text += PrintsText(bill.Jobs.Count) + ":" + nl;
            long[] jobOre = JobOre(bill);   // each job with its share of any top-up: no minimum line, the lines add up to the total
            for (int i = 0; i < bill.Jobs.Count; i++)
            {
                var r = bill.Jobs[i];
                int qty = History.QuantityOf(r);
                double amount = i < jobOre.Length ? jobOre[i] / 100.0 : 0;
                text += "- " + History.CopiesName(ModelName(r.Model), qty) + ": " +
                        L.F("{0} h {1} m", "{0} t {1} m", r.Hours, r.Minutes) + (qty > 1 ? OnPlates(History.PlatesOf(r)) : "") +
                        L.F(", {0} g", ", {0} g", Num.Fmt(r.Grams)) + ": " + Num.Kr(amount) + EachSuffix(amount, qty, showEach) + nl;
            }
            var count = History.CountPrints(bill.Jobs);
            if (count.Pieces > count.Models)
                text += History.PrintsText(count.Models, count.Pieces) + " · " + History.PlatesCountText(count.Plates) + nl;
            text += (invoice ? L.S("Total: ", "I alt: ") : L.S("Price: ", "Pris: ")) + Num.Kr(bill.Total);
            if (invoice && inv != null)
            {
                string paid = bill.Jobs.TrueForAll(j => j != null && !string.IsNullOrEmpty(j.InvoiceNo)) ? PaidLine(bill.Jobs) : "";   // paid-tick
                text += nl + (paid.Length > 0 ? L.F("Invoice date {0}.", "Fakturadato {0}.", LongDate(date)) + " " + paid
                                              : L.F("Invoice date {0}, please pay by {1}", "Fakturadato {0}, betales senest {1}", LongDate(date), LongDate(inv.Due)));
                string mp = paid.Length > 0 ? "" : (inv.MobilePay ?? "").Trim(), bank = paid.Length > 0 ? "" : (inv.Bank ?? "").Trim();   // paid: no payment details
                if (mp.Length > 0) text += nl + "MobilePay: " + mp;
                if (bank.Length > 0) text += nl + L.S("Bank: ", "Bank: ") + bank;
                if (mp.Length > 0 || bank.Length > 0)
                    text += nl + L.F("Please write invoice no. {0} with the payment.", "Skriv fakturanr. {0} ved betalingen.", number);
            }
            return text;
        }
    }
}
