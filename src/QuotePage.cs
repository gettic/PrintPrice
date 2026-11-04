using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PrintPrice
{
    /// What an export does (quote window buttons and the ▾ menu of "Show quote…").
    enum QuoteExport { Copy, Png, Pdf, Text, History }   // History = only "Save to history" (no picture, file or text)

    /// One export: returns the status text (null = the file dialog was cancelled); failed = it is an error text.
    delegate string QuoteExportHandler(IWin32Window owner, QuoteExport kind, bool friends, bool dark, string customer, out bool failed);

    /// Everything the customer quote page shows (filled by MainForm.BuildQuote). Texts are already in the app language.
    class QuoteData
    {
        public string Name = "";          // AppSettings.QuoteName (may be empty)
        public string Customer = "";      // customer's name (idea 21; empty = no "For:" line)
        public string Model = "";         // model name
        public string Link = "";          // short link ("printables.com/model/1648230") or ""
        public Image Picture;             // model picture (not owned) or null = no picture box
        public Image Logo;                // app logo (not owned) or null
        public int Hours, Minutes;
        public string Weight = "";        // "135,85 g"
        public string Printer = "";       // "Bambu Lab A1, 0.4 mm nozzle"
        public string Material = "";      // "Bambu PLA Matte" / "PLA, 4 colours"
        public string Number = "";        // History.QuoteNumber
        public DateTime Date, ValidUntil;
        public string PlateName = "";     // build plate for the wear line ("Smooth PEI")
        public double PurgeGrams = -1;    // multi-colour purge grams (<= 0 = no note)
        public List<string> MaterialNames = new List<string>();   // by QuoteLine.Index
        public List<Color> MaterialColours = new List<Color>();   // by QuoteLine.Index (Color.Empty = no dot)
        public QuoteBreakdown Breakdown;
        // invoice (History's Invoice…): title "Invoice", number / invoice date / due date in the header, "Bill to" row
        // instead of Printer, "Please pay by" under the total and a Payment block (only the lines that are set)
        public bool Invoice;
        public string InvoiceNo = "";     // History.MakeInvoice / NextInvoiceNo ("2026-006"); Date = the invoice date
        public DateTime Due;              // AppSettings.InvoiceDue(Date)
        public string PayMobilePay = "";  // AppSettings.PayMobilePay ("" = no line)
        public string PayBank = "";       // AppSettings.PayBank ("" = no line)
    }

    /// The customer quote page (idea 5): one A4 page with logo, model, details and the full price breakdown.
    /// Light (printing, mail, always the PDF) or dark (the app's own colours). Layout in 1000 units wide, drawn at
    /// PageWidth px (A4 at 150 dpi); taller than A4 only when the breakdown needs it.
    static class QuotePage
    {
        public const int PageWidth = 1240;
        const float W = 1000f;                   // layout width in page units
        const float A4Height = W * 297f / 210f;  // 1414

        sealed class Pal
        {
            public Color Page, Header, HeaderText, HeaderMuted, Ink, Muted, Faint, Line, PicBack, TotalBack, TotalLine, Stripe, Total, DotRing;
        }

        static Pal Palette(bool dark, bool friends)
        {
            if (dark)
                return new Pal
                {
                    Page = Theme.Back, Header = Theme.InfoBack, HeaderText = Theme.Text, HeaderMuted = Theme.Muted,
                    Ink = Theme.Text, Muted = Theme.Muted, Faint = Theme.Muted, Line = Theme.Border, PicBack = Theme.InfoBack,
                    TotalBack = Theme.InfoBack, TotalLine = Theme.Border, Stripe = Theme.InfoAccent,
                    Total = friends ? Theme.Friends : Theme.Others, DotRing = Theme.Muted
                };
            return new Pal
            {
                Page = Theme.QuoteLight.Page, Header = Theme.QuoteLight.Header, HeaderText = Theme.QuoteLight.HeaderText,
                HeaderMuted = Theme.QuoteLight.HeaderMuted, Ink = Theme.QuoteLight.Ink, Muted = Theme.QuoteLight.Muted,
                Faint = Theme.QuoteLight.Faint, Line = Theme.QuoteLight.Line, PicBack = Theme.QuoteLight.PictureBack,
                TotalBack = Theme.QuoteLight.TotalBack, TotalLine = Color.Empty, Stripe = Theme.InfoAccent,
                Total = friends ? Theme.QuoteLight.Friends : Theme.QuoteLight.Customer, DotRing = Theme.QuoteLight.DotRing
            };
        }

        public static Bitmap Render(QuoteData d, bool dark)
        {
            var p = Palette(dark, d.Breakdown.Friends);
            float need;
            using (var tmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(tmp))
            {
                Prep(g);
                need = Draw(g, p, d, A4Height, false);
            }
            float pageH = Math.Max(A4Height, need);
            float k = PageWidth / W;
            var bmp = new Bitmap(PageWidth, (int)Math.Ceiling(pageH * k), PixelFormat.Format32bppArgb);
            bmp.SetResolution(150, 150);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(p.Page);
                g.ScaleTransform(k, k);
                Prep(g);
                Draw(g, p, d, pageH, true);
            }
            return bmp;
        }

        static void Prep(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        // ---------- breakdown rows ----------
        enum Kind { Head, Sub, Note, Rule }

        sealed class Row
        {
            public Kind Kind;
            public string Label = "", Detail = "", Amount = "";
            public Color Dot = Color.Empty;
            public bool Alone;   // a head line without sub-lines: a little more room under it
        }

        static Row Head(string label, string detail, long ore, bool alone)
        {
            return new Row { Kind = Kind.Head, Label = label, Detail = detail ?? "", Amount = Num.Kr(ore / 100.0), Alone = alone };
        }

        static Row Sub(string label, string detail, long ore, Color dot)
        {
            return new Row { Kind = Kind.Sub, Label = label, Detail = detail ?? "", Amount = (ore / 100.0).ToString("N2", Num.Dk), Dot = dot };
        }

        static string N2(double v) { return v.ToString("N2", Num.Dk); }
        static string PerHour { get { return L.S(" kr/h", " kr/t"); } }   // units as in the windows: kr/kg, kr/kWh, kr/h
        static string Time(int h, int m) { return L.F("{0} h {1} m", "{0} t {1} m", h, m); }

        /// text cut to fit width (whole characters + "…"), measured as DrawString draws it; never stretched.
        static string FitText(Graphics g, string text, Font font, float width)
        {
            using (var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None })
            {
                Func<string, float> W = t => g.MeasureString(t, font, new SizeF(100000, 1000), sf).Width;
                if (W(text) <= width) return text;
                int lo = 0, hi = text.Length;
                while (lo < hi)   // longest start that fits with "…"
                {
                    int mid = (lo + hi + 1) / 2;
                    if (W(text.Substring(0, mid).TrimEnd() + "…") <= width) lo = mid; else hi = mid - 1;
                }
                return text.Substring(0, lo).TrimEnd() + "…";
            }
        }

        static List<Row> Rows(QuoteData d)
        {
            var rows = new List<Row>();
            var lines = d.Breakdown.Lines;
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
                    string name = m.Index >= 0 && m.Index < d.MaterialNames.Count ? d.MaterialNames[m.Index] : "";
                    if (name.Length == 0) name = L.F("Colour {0}", "Farve {0}", m.Index + 1);
                    Color dot = m.Index >= 0 && m.Index < d.MaterialColours.Count ? d.MaterialColours[m.Index] : Color.Empty;
                    rows.Add(Sub(name, matDetail(m), m.Ore, dot));
                }
                if (d.PurgeGrams > 0)
                    rows.Add(new Row { Kind = Kind.Note, Label = L.F("incl. {0} g filament used for colour changes",
                        "inkl. {0} g filament brugt til farveskift", Num.Fmt(Math.Round(d.PurgeGrams))) });
            }
            foreach (var l in lines)
            {
                if (l.Key == QuoteLine.Material) continue;
                rows.Add(new Row { Kind = Kind.Rule });
                switch (l.Key)
                {
                    case QuoteLine.Power:
                        rows.Add(Head(L.S("Electricity", "Strøm"), N2(l.Quantity) + " kWh  ×  " + N2(l.UnitPrice) + " kr/kWh", l.Ore, true));
                        break;
                    case QuoteLine.Wear:
                        bool parts = l.Parts != null && l.Parts.Count > 0;
                        rows.Add(Head(L.S("Machine wear", "Maskinslid"), Time(d.Hours, d.Minutes) + "  ×  " + N2(l.UnitPrice) + PerHour.TrimEnd(), l.Ore, !parts));
                        if (parts)
                            foreach (var part in l.Parts)
                                rows.Add(Sub(PartName(part.Key, d.PlateName), N2(part.UnitPrice) + PerHour, part.Ore, Color.Empty));
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

        static string PartName(string key, string plate)
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

        // ---------- drawing ----------
        /// Draws the page (draw = false only measures) and returns the height it needs.
        static float Draw(Graphics g, Pal p, QuoteData d, float pageH, bool draw)
        {
            var fonts = new Dictionary<string, Font>();
            Func<float, bool, Font> F = (size, bold) =>
            {
                string key = size + (bold ? "b" : "");
                Font f;
                if (!fonts.TryGetValue(key, out f))
                {
                    f = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                    fonts[key] = f;
                }
                return f;
            };
            // text at (px, py); pwid > 0 = one line in that width (cut with … by FitText, normal word gaps), aligned in it
            Action<string, Font, Color, float, float, float, StringAlignment> T = (text, font, col, px, py, pwid, align) =>
            {
                if (!draw || string.IsNullOrEmpty(text)) return;
                using (var tb = new SolidBrush(col))
                using (var tf = new StringFormat { Alignment = align, Trimming = StringTrimming.None })
                {
                    if (pwid > 0)
                    {
                        tf.FormatFlags = StringFormatFlags.NoWrap;
                        g.DrawString(FitText(g, text, font, pwid), font, tb, new RectangleF(px, py, pwid, font.Size * 1.6f), tf);
                    }
                    else g.DrawString(text, font, tb, px, py, tf);
                }
            };
            Action<float, float, float, Color, float> HLine = (ax, bx, ly, col, width) =>
            {
                if (!draw) return;
                using (var lp = new Pen(col, width)) g.DrawLine(lp, ax, ly, bx, ly);
            };
            try
            {
                const float L0 = 40, R0 = 960;
                var near = StringAlignment.Near;
                var far = StringAlignment.Far;

                // header band: logo, title, name; quote number and date on the right
                if (draw)
                {
                    using (var br = new SolidBrush(p.Header)) g.FillRectangle(br, 0, 0, W, 170);
                    using (var br = new SolidBrush(p.Stripe)) g.FillRectangle(br, 0, 170, W, 8);
                    if (d.Logo != null)
                    {
                        var st = g.Save();
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(d.Logo, new RectangleF(L0, 25, 120, 120));
                        g.Restore(st);
                    }
                }
                float tx = d.Logo != null ? 182 : L0;
                T(d.Invoice ? L.S("Invoice", "Faktura") : L.S("3D print quote", "Tilbud på 3D-print"), F(40, true), p.HeaderText, tx, 42, 0, near);
                if (!string.IsNullOrEmpty(d.Name))
                    T(d.Name + L.S(" · 3D printing", " · 3D-print"), F(18, false), p.HeaderMuted, tx + 3, 98, 0, near);
                if (d.Invoice)
                {
                    // right block: invoice no., invoice date, due date (the customer is in the "Bill to" row)
                    T(L.F("Invoice no. {0}", "Faktura nr. {0}", d.InvoiceNo), F(17, false), p.HeaderText, 560, 46, 400, far);
                    T(L.F("Invoice date {0}", "Fakturadato {0}", LongDate(d.Date)), F(17, false), p.HeaderMuted, 560, 74, 400, far);
                    T(L.F("Due {0}", "Forfald {0}", LongDate(d.Due)), F(17, false), p.HeaderMuted, 560, 102, 400, far);
                }
                else
                {
                    // right block: "For: <customer>" (bold, only with a name), quote no., date
                    bool forLine = !string.IsNullOrEmpty(d.Customer);
                    if (forLine)
                    {
                        // from 30 px after the title to the right edge (C2: long company names); 20 px bold, down to 15 px
                        // before it is cut with …
                        float forX = tx + g.MeasureString(d.Invoice ? "" : L.S("3D print quote", "Tilbud på 3D-print"), F(40, true)).Width + 30;
                        string forText = L.S("For: ", "Til: ") + d.Customer;
                        float size = 20;
                        while (size > 15 && g.MeasureString(forText, F(size, true)).Width > 960 - forX) size -= 1;
                        T(forText, F(size, true), p.HeaderText, forX, 38 + (20 - size) / 2, 960 - forX, far);
                    }
                    if (d.Number.Length > 0)
                        T(L.F("Quote no. {0}", "Tilbud nr. {0}", d.Number), F(17, false), p.HeaderText, 560, forLine ? 74 : 52, 400, far);
                    T(LongDate(d.Date), F(17, false), p.HeaderMuted, 560, forLine ? 100 : 80, 400, far);
                }

                // model: picture box (if any) + name, link and details
                float y0 = 214, pic = 300;
                float x = L0;
                if (d.Picture != null)
                {
                    if (draw)
                    {
                        using (var path = RoundRect(L0, y0, pic, pic, 14))
                        {
                            using (var br = new SolidBrush(p.PicBack)) g.FillPath(br, path);
                            using (var pen = new Pen(p.Line, 2)) g.DrawPath(pen, path);
                        }
                        DrawPicture(g, d.Picture, L0, y0, pic, pic);
                    }
                    x = L0 + pic + 40;
                }
                float nameW = R0 - x;
                var nameFont = F(28, true);
                float nameH;
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                {
                    float line = nameFont.GetHeight(g);
                    nameH = Math.Min(g.MeasureString(d.Model, nameFont, (int)nameW, sf).Height, line * 2);
                    if (draw)
                        using (var br = new SolidBrush(p.Ink))
                            g.DrawString(d.Model, nameFont, br, new RectangleF(x, y0 - 4, nameW, nameH + 2), sf);
                }
                float y = y0 - 4 + nameH + 8;
                if (d.Link.Length > 0) { T(d.Link, F(16, false), p.Muted, x + 3, y, nameW - 3, near); y += 28; }
                y = Math.Max(y + 10, y0 + 110);
                var details = new[]
                {
                    new[] { L.S("Print time", "Printtid"), Time(d.Hours, d.Minutes) },
                    new[] { L.S("Weight", "Vægt"), d.Weight },
                    d.Invoice ? new[] { L.S("Bill to", "Kunde"), d.Customer ?? "" } : new[] { L.S("Printer", "Printer"), d.Printer },
                    new[] { L.S("Material", "Materiale"), d.Material },
                };
                float valueX = x + (L.Danish ? 140 : 130);
                foreach (var r in details)
                {
                    HLine(x, R0, y, p.Line, 1);
                    T(r[0], F(18, false), p.Muted, x + 3, y + 10, 0, near);
                    T(r[1], F(18, true), p.Ink, valueX, y + 10, R0 - valueX, near);
                    y += 46;
                }
                HLine(x, R0, y, p.Line, 1);
                y = Math.Max(y + 58, y0 + pic + 52);

                // price breakdown
                T(L.S("Price breakdown", "Prisoversigt"), F(24, true), p.Ink, L0, y, 0, near);
                y += 44;
                HLine(L0, R0, y, p.Line, 2);
                y += 14;
                Kind prev = Kind.Rule;
                foreach (var r in Rows(d))
                {
                    switch (r.Kind)
                    {
                        case Kind.Rule:
                            if (prev == Kind.Sub) y += 10;
                            HLine(L0, R0, y, p.Line, 1);
                            y += 14;
                            break;
                        case Kind.Head:
                            T(r.Label, F(19, true), p.Ink, L0 + 3, y, 420, near);
                            T(r.Detail, F(16, false), p.Muted, 470, y + 2, 240, near);
                            T(r.Amount, F(19, true), p.Ink, 700, y, R0 - 700, far);
                            y += r.Alone ? 44 : 36;
                            break;
                        case Kind.Sub:
                            float lx = 70;
                            if (r.Dot != Color.Empty)
                            {
                                if (draw)
                                {
                                    using (var br = new SolidBrush(r.Dot)) g.FillEllipse(br, 70, y + 5, 15, 15);
                                    using (var pen = new Pen(p.DotRing, 1.5f)) g.DrawEllipse(pen, 70, y + 5, 15, 15);
                                }
                                lx = 94;
                            }
                            T(r.Label, F(17, false), p.Ink, lx, y, 465 - lx, near);
                            T(r.Detail, F(16, false), p.Muted, 470, y + 1, 240, near);
                            T(r.Amount, F(17, false), p.Ink, 700, y, R0 - 30 - 700, far);
                            y += 32;
                            break;
                        default:
                            T(r.Label, F(15, false), p.Faint, 70, y, R0 - 70, near);
                            y += 34;
                            break;
                    }
                    prev = r.Kind;
                }
                if (prev == Kind.Sub) y += 10;
                y += 4;
                HLine(L0, R0, y, p.Line, 2);
                y += 22;

                // total box
                if (draw)
                {
                    using (var path = RoundRect(L0, y, R0 - L0, 110, 14))
                    {
                        using (var br = new SolidBrush(p.TotalBack)) g.FillPath(br, path);
                        if (p.TotalLine != Color.Empty) using (var pen = new Pen(p.TotalLine, 1)) g.DrawPath(pen, path);
                    }
                    using (var br = new SolidBrush(p.Stripe)) g.FillRectangle(br, L0, y + 12, 8, 86);
                }
                T(L.S("Total", "I alt"), F(26, true), p.Ink, 80, y + 22, 0, near);
                T(d.Invoice ? L.F("Please pay by {0}", "Betales senest {0}", LongDate(d.Due)) : L.S("Shipping not included", "Fragt er ikke inkluderet"),
                  F(16, false), p.Muted, 80, y + 62, 0, near);
                T(Num.Kr(d.Breakdown.Total), F(56, true), p.Total, 420, y + 14, 510, far);
                y += 140;

                if (d.Invoice)
                {
                    // payment: only the ways that are set in Settings; none set = no block
                    var pay = new List<string>();
                    if (!string.IsNullOrEmpty(d.PayMobilePay)) pay.Add("MobilePay: " + d.PayMobilePay);
                    if (!string.IsNullOrEmpty(d.PayBank)) pay.Add(L.S("Bank: ", "Bank: ") + d.PayBank);
                    if (pay.Count > 0)
                    {
                        T(L.S("Payment", "Betaling"), F(19, true), p.Ink, L0 + 3, y, 0, near);
                        y += 32;
                        foreach (var line in pay) { T(line, F(18, false), p.Ink, L0 + 3, y, R0 - L0 - 3, near); y += 28; }
                        T(L.F("Please write invoice no. {0} with the payment.", "Skriv fakturanr. {0} ved betalingen.", d.InvoiceNo),
                          F(16, false), p.Muted, L0 + 3, y + 2, 0, near);
                        y += 32;
                    }
                }
                else
                {
                    // validity + small print
                    T(L.F("Valid until {0}", "Gyldigt til {0}", LongDate(d.ValidUntil)), F(18, false), p.Ink, L0 + 3, y, 0, near);
                    if (d.Picture != null)   // C3: only with a picture on the page
                    {
                        T(L.S("Colours and finish may differ slightly from the picture.", "Farver og overflade kan afvige lidt fra billedet."),
                          F(16, false), p.Muted, L0 + 3, y + 30, 0, near);
                        y += 60;
                    }
                    else y += 30;
                }
                // "made with" line at the bottom of the page
                HLine(L0, R0, pageH - 62, p.Line, 1);
                T(L.S("Made with Gettic's 3D Print Price Calculator", "Lavet med Gettic's 3D Print Price Calculator"),
                  F(14, false), p.Muted, L0, pageH - 44, R0 - L0, StringAlignment.Center);
                return y + 90;
            }
            finally
            {
                foreach (var f in fonts.Values) f.Dispose();
            }
        }

        /// "4 November 2026" / "4. november 2026".
        public static string LongDate(DateTime t)
        {
            return L.Danish ? t.ToString("d. MMMM yyyy", Num.Dk) : t.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"));
        }

        static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            float d = 2 * r;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// The whole picture, scaled to fit inside an 8 % margin, centred (the model picture is already trimmed).
        static void DrawPicture(Graphics g, Image img, float x, float y, float w, float h)
        {
            float mar = Math.Max(4, Math.Min(w, h) * 0.08f);
            float aw = w - 2 * mar, ah = h - 2 * mar;
            float s = Math.Min(aw / img.Width, ah / img.Height);
            float dw = img.Width * s, dh = img.Height * s;
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(img, new RectangleF(x + (w - dw) / 2, y + (h - dh) / 2, dw, dh));
            g.Restore(st);
        }

        // ---------- clipboard / files ----------
        /// Puts the picture on the clipboard as a bitmap and as PNG data (browsers and Messenger paste the PNG).
        public static void CopyToClipboard(Bitmap bmp)
        {
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, true, bmp);
            var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            data.SetData("PNG", false, ms);
            Clipboard.SetDataObject(data, true);
        }

        /// Writes a one-page A4 PDF with the picture (JPEG inside, scaled to fit the page). No PDF library: the file is
        /// written by hand (catalog, pages, page, image XObject, content stream, xref).
        public static void SavePdf(Bitmap bmp, string path)
        {
            File.WriteAllBytes(path, Pdf(bmp));
        }

        internal static byte[] Pdf(Bitmap bmp)
        {
            byte[] jpg;
            using (var ms = new MemoryStream())
            {
                ImageCodecInfo codec = null;
                foreach (var c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") codec = c;
                using (var ps = new EncoderParameters(1))
                {
                    ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                    // JPEG has no alpha: flatten onto white first (the page is opaque anyway)
                    using (var rgb = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb))
                    {
                        using (var g = Graphics.FromImage(rgb)) { g.Clear(Color.White); g.DrawImageUnscaled(bmp, 0, 0); }
                        rgb.Save(ms, codec, ps);
                    }
                }
                jpg = ms.ToArray();
            }
            var inv = CultureInfo.InvariantCulture;
            const double pw = 595.28, ph = 841.89;   // A4 in points
            double s = Math.Min(pw / bmp.Width, ph / bmp.Height);
            double dw = bmp.Width * s, dh = bmp.Height * s;
            double dx = (pw - dw) / 2, dy = ph - dh;   // top of the page; a taller-than-A4 picture is scaled to fit
            string content = string.Format(inv, "q {0:0.###} 0 0 {1:0.###} {2:0.###} {3:0.###} cm /Im0 Do Q", dw, dh, dx, dy);

            var outp = new MemoryStream();
            var offsets = new List<long>();
            Action<string> Put = str => { var b = Encoding.ASCII.GetBytes(str); outp.Write(b, 0, b.Length); };
            Put("%PDF-1.4\n");
            outp.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 6);   // binary marker comment
            offsets.Add(outp.Position); Put("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            offsets.Add(outp.Position); Put("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
            offsets.Add(outp.Position);
            Put(string.Format(inv, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.##} {1:0.##}] " +
                "/Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n", pw, ph));
            offsets.Add(outp.Position);
            Put(string.Format(inv, "4 0 obj\n<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB " +
                "/BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n", bmp.Width, bmp.Height, jpg.Length));
            outp.Write(jpg, 0, jpg.Length);
            Put("\nendstream\nendobj\n");
            offsets.Add(outp.Position);
            Put(string.Format(inv, "5 0 obj\n<< /Length {0} >>\nstream\n{1}\nendstream\nendobj\n", Encoding.ASCII.GetByteCount(content), content));
            long xref = outp.Position;
            Put("xref\n0 6\n0000000000 65535 f \n");
            foreach (long o in offsets) Put(o.ToString("0000000000", inv) + " 00000 n \n");
            Put(string.Format(inv, "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{0}\n%%EOF\n", xref));
            return outp.ToArray();
        }
    }

    /// Dark menu renderer for the "Copy quote ▾" menu: CaptionForm's dark colours, plus a right-hand text (prices,
    /// "PDF stays light") in the item's Tag colour, Muted disabled text and a drawn round check mark.
    sealed class QuoteMenuRenderer : ToolStripProfessionalRenderer
    {
        public QuoteMenuRenderer() : base(new CaptionForm.DarkMenuColours()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var mi = e.Item as ToolStripMenuItem;
            if (!e.Item.Enabled) e.TextColor = Theme.Muted;
            else if (mi != null && mi.Tag is Color && e.Text == mi.ShortcutKeyDisplayString && !mi.Selected) e.TextColor = (Color)mi.Tag;
            else e.TextColor = mi != null && mi.Selected ? Color.White : Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            int d = Math.Max(6, r.Height / 2);
            var g = e.Graphics;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(e.Item.Selected ? Color.White : Theme.Text))
                g.FillEllipse(br, r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d);
            g.Restore(st);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }
    }
}
