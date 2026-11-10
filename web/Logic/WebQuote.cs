using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PrintPrice.Web
{
    /// The quote / invoice page on the website (stage 4), without browser calls (web\tests runs it). What the page shows
    /// = the desktop's QuoteData (MainForm.BuildQuote / BuildRecordQuote); the rows, dates, material line, file name and
    /// plain texts come from the linked src\QuoteRows.cs, the prices from Pricing.Breakdown / MatchSavedTotal, a saved
    /// quote's inputs from History.ToInput / SavedSettings. Pages\QuoteSheet.razor draws it in the same 1000-unit layout
    /// as QuotePage.
    public static class WebQuote
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// Everything the page shows (= QuoteData; colours as CSS "#rrggbb", "" = no dot; Picture = a data URL or null).
        public sealed class Sheet
        {
            public string Name = "", Customer = "", Model = "", Link = "", Picture;
            public int Hours, Minutes;
            public string Weight = "", Printer = "", Material = "", Number = "", PlateName = "";
            public DateTime Date, ValidUntil, Due;
            public double PurgeGrams = -1;
            public List<string> MaterialNames = new List<string>();
            public List<string> MaterialColours = new List<string>();
            public QuoteBreakdown Breakdown;
            public bool Invoice;
            public string InvoiceNo = "", PayMobilePay = "", PayBank = "";
        }

        /// The job on the calculator (MainForm.BuildQuote's inputs on the web).
        public sealed class Job
        {
            public QuoteInput Input;
            public string Model, Source, Picture;   // model name (file / record), typed link or file name, plate picture
            public List<FilamentUse> Colours;       // 2+ colours (the rows), else null
            public FilamentUse Single;              // one colour from a file / record (may be null)
            public string SingleName;               // the one material's name the page uses (null = Settings' filament)
            public double PurgeGrams = -1;
            public string FilamentType = "PLA";     // for the wear split (WebCalc.WearBreakdown)
        }

        // ---------- the page's data ----------

        /// The quote of the job on screen (MainForm.BuildQuote): today's settings, the wear split when Auto wear is on.
        public static Sheet FromJob(Job j, AppSettings s, bool friends, DateTime date, string number, string customer)
        {
            var wear = s.AutoWearRate != 0 ? WebCalc.WearBreakdown(s, j.FilamentType) : null;
            var d = new Sheet
            {
                Name = (s.QuoteName ?? "").Trim(),
                Customer = (customer ?? "").Trim(),
                Model = QuoteRows.ModelName(j.Model),
                Link = ShortLink(j.Source),
                Picture = j.Picture,
                Hours = j.Input.Hours, Minutes = j.Input.Minutes,
                Weight = Num.Fmt(j.Input.FilamentGrams) + " g",
                Printer = PrinterText(s, null),
                Number = number ?? "", Date = date, ValidUntil = s.QuoteValidUntil(date),
                PlateName = PlateName(s),
                PurgeGrams = j.Colours != null ? j.PurgeGrams : -1,
                Breakdown = Pricing.Breakdown(j.Input, s, friends, wear)
            };
            var used = j.Colours ?? (j.Single != null ? new List<FilamentUse> { j.Single } : null);
            if (used != null)
                foreach (var f in used)
                {
                    d.MaterialNames.Add(j.Colours == null && !string.IsNullOrEmpty(j.SingleName) ? j.SingleName : WebHistory.MaterialName(f));
                    d.MaterialColours.Add(ColourCss(f.Colour));
                }
            else d.MaterialNames.Add(!string.IsNullOrEmpty(j.SingleName) ? j.SingleName : DefaultMaterial(s));
            var types = new List<string>();
            if (j.Colours != null) foreach (var f in j.Colours) types.Add(f.Type);
            d.Material = QuoteRows.MaterialLine(j.Colours != null ? types : null, j.Colours != null ? j.Colours.Count : 0, d.MaterialNames[0]);
            return d;
        }

        /// A saved quote (MainForm.BuildRecordQuote): its saved inputs and prices, shown at the saved price
        /// (MatchSavedTotal); invoice: its number and dates, or "(new)" and today while it isn't made yet, and the invoice's
        /// snapshot of the due date, payment ways and name (History.InvoiceInfo).
        public static Sheet FromRecord(QuoteRecord r, AppSettings s, bool friends, string customer, bool invoice, DateTime now, string picture)
        {
            DateTime saved;
            if (!History.TryTime(r.SavedAt, out saved)) saved = now;
            var d = new Sheet
            {
                Name = (s.QuoteName ?? "").Trim(),
                Customer = (customer ?? "").Trim(),
                Model = QuoteRows.ModelName(r.Model),
                Link = ShortLink(r.Source),
                Picture = picture,
                Hours = r.Hours, Minutes = r.Minutes,
                Weight = Num.Fmt(r.Grams) + " g",
                Printer = PrinterText(s, r),
                Number = History.QuoteNumber(r), Date = saved, ValidUntil = s.QuoteValidUntil(saved),
                PlateName = PlateName(s),
                PurgeGrams = r.Filaments != null && r.Filaments.Count >= 2 ? r.PurgeGrams : -1,
                Breakdown = Pricing.Breakdown(History.ToInput(r), History.SavedSettings(r, s), friends, null)
            };
            Pricing.MatchSavedTotal(d.Breakdown, friends ? r.FriendsPrice : r.OthersPrice);
            var types = new List<string>();
            if (r.Filaments != null)
                for (int k = 0; k < r.Filaments.Count; k++)
                {
                    var f = r.Filaments[k];
                    d.MaterialNames.Add(WebHistory.MaterialName(new FilamentUse { Slot = k + 1, Profile = f.Name ?? "", Type = f.Type ?? "", Colour = f.Colour ?? "" }));
                    d.MaterialColours.Add(ColourCss(f.Colour));
                    types.Add(f.Type);
                }
            else d.MaterialNames.Add(DefaultMaterial(s));
            d.Material = QuoteRows.MaterialLine(r.Filaments != null ? types : null, r.Filaments != null ? r.Filaments.Count : 0, d.MaterialNames[0]);
            if (invoice)
            {
                DateTime billed = DateTime.MinValue;
                bool made = !string.IsNullOrEmpty(r.InvoiceNo) && History.TryTime(r.BilledAt, out billed);
                if (!made) billed = now;
                d.Invoice = true;
                d.InvoiceNo = made ? r.InvoiceNo : L.S("(new)", "(ny)");   // the random code is drawn when the invoice is made
                d.Date = billed;
                var inv = History.InvoiceInfo(r, s, billed);
                d.Due = inv.Due;
                d.PayMobilePay = (inv.MobilePay ?? "").Trim();
                d.PayBank = (inv.Bank ?? "").Trim();
                d.Name = (inv.FromName ?? "").Trim();
            }
            return d;
        }

        static string DefaultMaterial(AppSettings s) { return BambuSlicer.FilamentBaseName(s.FilamentPreset); }

        /// "<printer name>, 0.4 mm nozzle" (MainForm.PrinterText): the active printer, or a saved quote's printer (its
        /// saved name; the nozzle of that printer today, if it still exists).
        public static string PrinterText(AppSettings s, QuoteRecord r)
        {
            var p = r != null ? s.FindPrinter(History.PrinterOf(r)) : null;
            string name = r != null && !string.IsNullOrEmpty(r.PrinterName) ? r.PrinterName : p != null ? p.Name : s.PrinterName;
            var nozzle = Wear.FindNozzle(p != null ? p.Nozzle : s.Nozzle);
            return L.F("{0}, {1} mm nozzle", "{0}, {1} mm dyse", AppSettings.CleanPrinterName(name),
                       L.Danish ? nozzle.Size.Replace('.', ',') : nozzle.Size);
        }

        /// The build plate's short name for the wear line (MainForm.QuotePlate): "Smooth PEI" ("Glat PEI").
        public static string PlateName(AppSettings s)
        {
            string plate = Wear.FindPlate(s.BedType).Label;
            int cut = plate.IndexOf(" / ", StringComparison.Ordinal);
            if (cut > 0) plate = plate.Substring(0, cut);
            if (L.Danish && plate == "Smooth PEI") plate = "Glat PEI";
            return plate;
        }

        /// "#RRGGBB" (or "#RRGGBBAA") -> "#rrggbb" for the colour dot; "" = unknown, no dot (MainForm.ParseColour).
        public static string ColourCss(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return "";
            string h = hex.Trim().TrimStart('#');
            int v;
            if (h.Length < 6 || !int.TryParse(h.Substring(0, 6), NumberStyles.HexNumber, Inv, out v)) return "";
            return "#" + h.Substring(0, 6).ToLowerInvariant();
        }

        // Printables.ParseId's pattern (src\Printables.cs uses WebClient, so it isn't linked; tests check the same answers)
        static readonly Regex PrintablesId = new Regex(@"printables\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?model/(\d+)", RegexOptions.IgnoreCase);

        public static string ParsePrintablesId(string url)
        {
            var m = PrintablesId.Match(url ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// Short link for the page (QuoteRows.ShortLink): "printables.com/model/1648230"; file names are never shown.
        public static string ShortLink(string url) { return QuoteRows.ShortLink(url, ParsePrintablesId); }

        // ---------- the rows and texts the sheet draws (QuotePage.Draw's) ----------

        internal static List<QuoteRow> Rows(Sheet d)
        {
            return QuoteRows.Build(d.Breakdown, d.MaterialNames, d.PurgeGrams, d.Hours, d.Minutes, d.PlateName);
        }

        public static string Title(bool invoice) { return invoice ? L.S("Invoice", "Faktura") : L.S("3D print quote", "Tilbud på 3D-print"); }

        public static string NameLine(Sheet d) { return d.Name.Length > 0 ? d.Name + L.S(" · 3D printing", " · 3D-print") : ""; }

        public static string ForLine(Sheet d) { return d.Invoice || d.Customer.Length == 0 ? "" : L.S("For: ", "Til: ") + d.Customer; }

        /// The "For:" line's size (QuotePage: bold 20 px down to 15 px to fit from 30 units after the title to the right
        /// edge; the browser cuts what still doesn't fit with …). Widths estimated from Segoe UI Bold's average letter.
        public static int ForSize(Sheet d, bool logo)
        {
            string text = ForLine(d);
            if (text.Length == 0) return 20;
            double title = Title(false).Length * 40 * 0.53;
            double room = 960 - ((logo ? 182 : 40) + title + 30);
            int size = 20;
            while (size > 15 && text.Length * size * 0.56 > room) size--;
            return size;
        }

        /// The header's right block (top to bottom): invoice no., invoice date, due date / quote no., date.
        public static List<string> HeaderLines(Sheet d)
        {
            var l = new List<string>();
            if (d.Invoice)
            {
                l.Add(L.F("Invoice no. {0}", "Faktura nr. {0}", d.InvoiceNo));
                l.Add(L.F("Invoice date {0}", "Fakturadato {0}", QuoteRows.LongDate(d.Date)));
                l.Add(L.F("Due {0}", "Forfald {0}", QuoteRows.LongDate(d.Due)));
            }
            else
            {
                if (d.Number.Length > 0) l.Add(L.F("Quote no. {0}", "Tilbud nr. {0}", d.Number));
                l.Add(QuoteRows.LongDate(d.Date));
            }
            return l;
        }

        /// The details table: Print time, Weight, Printer (invoice: Bill to), Material.
        public static List<string[]> Details(Sheet d)
        {
            return new List<string[]>
            {
                new[] { L.S("Print time", "Printtid"), QuoteRows.Time(d.Hours, d.Minutes) },
                new[] { L.S("Weight", "Vægt"), d.Weight },
                d.Invoice ? new[] { L.S("Bill to", "Kunde"), d.Customer ?? "" } : new[] { L.S("Printer", "Printer"), d.Printer },
                new[] { L.S("Material", "Materiale"), d.Material },
            };
        }

        public static string TotalNote(Sheet d)
        {
            return d.Invoice ? L.F("Please pay by {0}", "Betales senest {0}", QuoteRows.LongDate(d.Due))
                             : L.S("Shipping not included", "Fragt er ikke inkluderet");
        }

        /// Invoice: the payment lines that are set (none = no Payment block).
        public static List<string> PayLines(Sheet d)
        {
            var pay = new List<string>();
            if (!string.IsNullOrEmpty(d.PayMobilePay)) pay.Add("MobilePay: " + d.PayMobilePay);
            if (!string.IsNullOrEmpty(d.PayBank)) pay.Add(L.S("Bank: ", "Bank: ") + d.PayBank);
            return pay;
        }

        public static string PayNote(Sheet d) { return L.F("Please write invoice no. {0} with the payment.", "Skriv fakturanr. {0} ved betalingen.", d.InvoiceNo); }

        public static string ValidLine(Sheet d) { return L.F("Valid until {0}", "Gyldigt til {0}", QuoteRows.LongDate(d.ValidUntil)); }

        public static string PictureNote { get { return L.S("Colours and finish may differ slightly from the picture.", "Farver og overflade kan afvige lidt fra billedet."); } }

        public static string Footer { get { return L.S("Made with Gettic's 3D Print Price Calculator", "Lavet med Gettic's 3D Print Price Calculator"); } }

        // ---------- the layout (QuotePage.Draw, step for step, in its 1000-unit page) ----------

        public const double PageW = 1000, A4H = 1000 * 297.0 / 210.0;   // 1414

        /// One thing on the page. Kind: "text", "wrap" (model name: up to 2 lines), "line" (H = thickness), "rect",
        /// "round" (rounded box, R = radius, Role2 = border colour or null), "dot", "logo", "picture".
        /// Role = a colour of the palette (CSS variable --q-<role>); Align "l" / "r" / "c" within W (W 0 = no width).
        public sealed class Item
        {
            public string Kind = "text", Text = "", Role = "ink", Role2, Align = "l", Colour;
            public double X, Y, W, H, Size, R;
            public bool Bold;
        }

        public sealed class Layout
        {
            public List<Item> Items = new List<Item>();
            public double Height;   // the page: A4 (1414) or taller when the content needs it
        }

        /// Model name lines at 28 px bold in width w (estimated from Segoe UI Bold's average letter; 1 or 2).
        static int NameLines(string name, double w)
        {
            return (name ?? "").Length * 28 * 0.56 > w ? 2 : 1;
        }

        public static Layout Draw(Sheet d, bool logo)
        {
            var lay = new Layout();
            var it = lay.Items;
            Action<string, double, bool, string, double, double, double, string> T = (text, size, bold, role, x, y, w, align) =>
            {
                if (string.IsNullOrEmpty(text)) return;
                it.Add(new Item { Text = text, Size = size, Bold = bold, Role = role, X = x, Y = y, W = w, Align = align });
            };
            Action<double, double, double, string, double> HLine = (ax, bx, ly, role, width) =>
                it.Add(new Item { Kind = "line", X = ax, Y = ly - width / 2, W = bx - ax, H = width, Role = role });
            const double L0 = 40, R0 = 960;

            // header band: logo, title, name; number and dates on the right
            it.Add(new Item { Kind = "rect", X = 0, Y = 0, W = PageW, H = 170, Role = "header" });
            it.Add(new Item { Kind = "rect", X = 0, Y = 170, W = PageW, H = 8, Role = "stripe" });
            if (logo) it.Add(new Item { Kind = "logo", X = L0, Y = 25, W = 120, H = 120 });
            double tx = logo ? 182 : L0;
            T(Title(d.Invoice), 40, true, "headtext", tx, 42, 0, "l");
            T(NameLine(d), 18, false, "headmuted", tx + 3, 98, 0, "l");
            if (d.Invoice)
            {
                var h = HeaderLines(d);
                T(h[0], 17, false, "headtext", 560, 46, 400, "r");
                T(h[1], 17, false, "headmuted", 560, 74, 400, "r");
                T(h[2], 17, false, "headmuted", 560, 102, 400, "r");
            }
            else
            {
                string forText = ForLine(d);
                bool forLine = forText.Length > 0;
                if (forLine)
                {
                    double title = Title(false).Length * 40 * 0.53;
                    double forX = tx + title + 30;
                    int size = ForSize(d, logo);
                    T(forText, size, true, "headtext", forX, 38 + (20 - size) / 2.0, R0 - forX, "r");
                }
                if (d.Number.Length > 0)
                    T(L.F("Quote no. {0}", "Tilbud nr. {0}", d.Number), 17, false, "headtext", 560, forLine ? 74 : 52, 400, "r");
                T(QuoteRows.LongDate(d.Date), 17, false, "headmuted", 560, forLine ? 100 : 80, 400, "r");
            }

            // model: picture box (if any) + name, link and details
            double y0 = 214, pic = 300, x = L0;
            if (d.Picture != null)
            {
                it.Add(new Item { Kind = "round", X = L0, Y = y0, W = pic, H = pic, R = 14, Role = "picback", Role2 = "line" });
                it.Add(new Item { Kind = "picture", X = L0, Y = y0, W = pic, H = pic, Text = d.Picture });
                x = L0 + pic + 40;
            }
            double nameW = R0 - x;
            double nameH = NameLines(d.Model, nameW) * 28 * 1.33;
            it.Add(new Item { Kind = "wrap", Text = d.Model, Size = 28, Bold = true, Role = "ink", X = x, Y = y0 - 4, W = nameW, H = nameH + 2 });
            double y = y0 - 4 + nameH + 8;
            if (d.Link.Length > 0) { T(d.Link, 16, false, "muted", x + 3, y, nameW - 3, "l"); y += 28; }
            y = Math.Max(y + 10, y0 + 110);
            double valueX = x + (L.Danish ? 140 : 130);
            foreach (var r in Details(d))
            {
                HLine(x, R0, y, "line", 1);
                T(r[0], 18, false, "muted", x + 3, y + 10, 0, "l");
                T(r[1], 18, true, "ink", valueX, y + 10, R0 - valueX, "l");
                y += 46;
            }
            HLine(x, R0, y, "line", 1);
            y = Math.Max(y + 58, y0 + pic + 52);

            // price breakdown
            T(L.S("Price breakdown", "Prisoversigt"), 24, true, "ink", L0, y, 0, "l");
            y += 44;
            HLine(L0, R0, y, "line", 2);
            y += 14;
            QuoteRowKind prev = QuoteRowKind.Rule;
            foreach (var r in Rows(d))
            {
                switch (r.Kind)
                {
                    case QuoteRowKind.Rule:
                        if (prev == QuoteRowKind.Sub) y += 10;
                        HLine(L0, R0, y, "line", 1);
                        y += 14;
                        break;
                    case QuoteRowKind.Head:
                        T(r.Label, 19, true, "ink", L0 + 3, y, 420, "l");
                        T(r.Detail, 16, false, "muted", 470, y + 2, 240, "l");
                        T(r.Amount, 19, true, "ink", 700, y, R0 - 700, "r");
                        y += r.Alone ? 44 : 36;
                        break;
                    case QuoteRowKind.Sub:
                        double lx = 70;
                        string dot = r.ColourIndex >= 0 && r.ColourIndex < d.MaterialColours.Count ? d.MaterialColours[r.ColourIndex] : "";
                        if (dot.Length > 0)
                        {
                            it.Add(new Item { Kind = "dot", X = 70, Y = y + 5, W = 15, H = 15, Colour = dot, Role = "dotring" });
                            lx = 94;
                        }
                        T(r.Label, 17, false, "ink", lx, y, 465 - lx, "l");
                        T(r.Detail, 16, false, "muted", 470, y + 1, 240, "l");
                        T(r.Amount, 17, false, "ink", 700, y, R0 - 30 - 700, "r");
                        y += 32;
                        break;
                    default:
                        T(r.Label, 15, false, "faint", 70, y, R0 - 70, "l");
                        y += 34;
                        break;
                }
                prev = r.Kind;
            }
            if (prev == QuoteRowKind.Sub) y += 10;
            y += 4;
            HLine(L0, R0, y, "line", 2);
            y += 22;

            // total box
            it.Add(new Item { Kind = "round", X = L0, Y = y, W = R0 - L0, H = 110, R = 14, Role = "totalback", Role2 = "totalline" });
            it.Add(new Item { Kind = "rect", X = L0, Y = y + 12, W = 8, H = 86, Role = "stripe" });
            T(L.S("Total", "I alt"), 26, true, "ink", 80, y + 22, 0, "l");
            T(TotalNote(d), 16, false, "muted", 80, y + 62, 0, "l");
            T(Num.Kr(d.Breakdown.Total), 56, true, "total", 420, y + 14, 510, "r");
            y += 140;

            if (d.Invoice)
            {
                var pay = PayLines(d);
                if (pay.Count > 0)
                {
                    T(L.S("Payment", "Betaling"), 19, true, "ink", L0 + 3, y, 0, "l");
                    y += 32;
                    foreach (var line in pay) { T(line, 18, false, "ink", L0 + 3, y, R0 - L0 - 3, "l"); y += 28; }
                    T(PayNote(d), 16, false, "muted", L0 + 3, y + 2, 0, "l");
                    y += 32;
                }
            }
            else
            {
                T(ValidLine(d), 18, false, "ink", L0 + 3, y, 0, "l");
                if (d.Picture != null)
                {
                    T(PictureNote, 16, false, "muted", L0 + 3, y + 30, 0, "l");
                    y += 60;
                }
                else y += 30;
            }
            double pageH = Math.Max(A4H, y + 90);
            // "made with" line at the bottom of the page
            HLine(L0, R0, pageH - 62, "line", 1);
            T(Footer, 14, false, "muted", L0, pageH - 44, R0 - L0, "c");
            lay.Height = pageH;
            return lay;
        }

        // ---------- the quote window (QuoteForm) ----------

        public static string WindowTitle(bool invoice, string model)
        {
            return invoice ? L.F("Invoice – {0}", "Faktura – {0}", model) : L.F("Quote – {0}", "Tilbud – {0}", model);
        }

        /// The help box: the desktop's text, with "Print / PDF" for the web's one way out.
        public static string HelpText(bool record, bool invoice)
        {
            if (invoice)
                return L.S("Making the invoice (Print / PDF or Copy text) gives it a number and ticks Printed in the history. Print / PDF opens the browser's print window: choose \"Save as PDF\" for a file. The page is A4 and always prints light.",
                           "Når fakturaen laves (Print / PDF eller Kopiér tekst), får den et nummer, og Printet bliver sat i historikken. Print / PDF åbner browserens printvindue: vælg \"Gem som PDF\" for en fil. Siden er A4 og printes altid lys.");
            if (record)
                return L.S("Print / PDF or Copy text puts the name and price on this quote in the history. Print / PDF opens the browser's print window: choose \"Save as PDF\" for a file. The page is A4 and always prints light.",
                           "Print / PDF eller Kopiér tekst sætter navn og pris på dette tilbud i historikken. Print / PDF åbner browserens printvindue: vælg \"Gem som PDF\" for en fil. Siden er A4 og printes altid lys.");
            return L.S("Print / PDF or Copy text also puts the quote in the price history. Print / PDF opens the browser's print window: choose \"Save as PDF\" for a file. The page is A4 and always prints light.",
                       "Print / PDF eller Kopiér tekst lægger også tilbuddet i prishistorikken. Print / PDF åbner browserens printvindue: vælg \"Gem som PDF\" for en fil. Siden er A4 og printes altid lys.");
        }

        /// A #quote/<id> or #invoice/<id> address whose quote isn't here (deleted, or saved in another browser).
        public static string NotFound { get { return L.S("Quote not found: it isn't in this browser's price history (deleted, or saved in another browser).", "Tilbuddet blev ikke fundet: det er ikke i denne browsers prishistorik (slettet, eller gemt i en anden browser)."); } }

        /// The record id in a "#quote/<id>" / "#invoice/<id>" address; null = not such an address.
        public static string AddressId(string hash, out bool invoice)
        {
            hash = hash ?? "";
            invoice = hash.StartsWith("#invoice/", StringComparison.Ordinal);
            if (!invoice && !hash.StartsWith("#quote/", StringComparison.Ordinal)) return null;
            return hash.Substring(hash.IndexOf('/') + 1);
        }

        public static string NeedName { get { return L.S("Type the customer's name first: it goes on the invoice.", "Skriv kundens navn først: det kommer på fakturaen."); } }

        /// A quote export of the job: Add, then For and the typed name (MainForm.SaveToHistory(forWho, customer)).
        public static QuoteRecord SaveExport(List<QuoteRecord> list, QuoteRecord r, bool friends, string customer)
        {
            var saved = History.AddFor(list, r, friends ? History.ForFriends : History.ForCustomer);
            saved.Customer = (customer ?? "").Trim();   // the name typed now (a same-quote replace takes it too)
            return saved;
        }

        /// A record's export (MainForm.ExportRecord): the typed name and the chosen price go on it; an invoice gets its
        /// number, date and snapshot (History.MakeInvoice, settings.UsedInvoiceNos). The caller saves both.
        public static void ApplyRecordExport(List<QuoteRecord> list, QuoteRecord r, bool friends, string customer, bool invoice, DateTime now, AppSettings s)
        {
            r.Customer = (customer ?? "").Trim();
            r.For = friends ? History.ForFriends : History.ForCustomer;
            if (invoice) History.MakeInvoice(list, r, now, s);
        }

        /// The PDF's suggested name (the page title while printing): "2026-11-04 3D Benchy - Mette Hansen"; a record: the
        /// quote's date, an invoice its date (today while it isn't made yet).
        public static string FileName(Sheet d) { return QuoteRows.FileName(d.Date, d.Model, d.Customer); }

        public static string PrintedText(bool invoice, string invoiceNo, bool saved)
        {
            if (invoice) return L.F("✓ Print window opened. Invoice no. {0} is in the history.", "✓ Printvinduet er åbnet. Faktura nr. {0} er gemt i historikken.", invoiceNo);
            return saved ? L.S("✓ Print window opened, and the quote is saved to history.", "✓ Printvinduet er åbnet, og tilbuddet er gemt i historikken.")
                         : L.S("✓ Print window opened.", "✓ Printvinduet er åbnet.");
        }

        public static string CopiedText(bool invoice, string invoiceNo, bool saved)
        {
            if (invoice) return L.F("✓ Invoice text copied. Invoice no. {0} is in the history.", "✓ Fakturaen er kopieret som tekst. Faktura nr. {0} er gemt i historikken.", invoiceNo);
            return saved ? L.S("✓ Quote text copied and saved to history.", "✓ Tilbuddet er kopieret som tekst og gemt i historikken.")
                         : L.S("✓ Quote text copied", "✓ Tilbuddet er kopieret som tekst");
        }

        /// What the page needs for the print / copy after an export (null Problem = OK).
        public sealed class Done
        {
            public string Problem, Status, Text, FileName;
        }
    }
}
