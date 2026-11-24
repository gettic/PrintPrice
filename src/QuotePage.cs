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
        public double TowerGrams = -1;    // issue #8 step 3: "Prime tower 1,70 g" note (<= 0 = no note; only with QuoteShowTower)
        public List<string> MaterialNames = new List<string>();   // by QuoteLine.Index
        public List<Color> MaterialColours = new List<Color>();   // by QuoteLine.Index (Color.Empty = no dot)
        public QuoteBreakdown Breakdown;
        // batch & plates (issue #9/#10): Quantity 2+ = a "Quantity" details row ("4 pcs"), Print time "… on 2 plates"
        // (Plates), and EachLine ("4 pcs · 18,18 kr. each", "" = off) before the total box's small line. 1 = as before.
        public int Quantity = 1;
        public int Plates = 1;
        public string EachLine = "";
        // invoice (History's Invoice…): title "Invoice", number / invoice date / due date in the header, "Bill to" row
        // instead of Printer, "Please pay by" under the total and a Payment block (only the lines that are set)
        public bool Invoice;
        public string InvoiceNo = "";     // History.MakeInvoice / NextInvoiceNo ("2026-48213": year + 5 random digits); Date = the invoice date
        public DateTime Due;              // AppSettings.InvoiceDue(Date)
        public string PaidLine = "";      // paid-tick: History.PaidText ("Paid 05-11-2026 – thank you") instead of "Please pay by …"; "" = unpaid
        public string PayMobilePay = "";  // AppSettings.PayMobilePay ("" = no line)
        public string PayBank = "";       // AppSettings.PayBank ("" = no line)
        // combined bill (History: 2+ jobs on one quote / invoice; MainForm.BuildGroupQuote): Jobs with 2+ entries replaces the
        // picture, details and breakdown by summary rows ("Bill to", "Prints", "Printer" / "Plate" when shared) and one
        // card per job, side by side (up to 3 per row); Breakdown = the whole bill's breakdown (History.GroupBreakdown;
        // MaterialNames / MaterialColours by its Materials, Hours / Minutes summed), drawn under the cards by
        // QuoteRows.BuildGroup; GroupFriends / GroupTotal for colour and total
        public List<QuoteJob> Jobs;
        public string Summary = "";       // "3 prints · 7 h 40 m · 182 g" (QuoteRows.GroupSummary)
        public string GroupPlates = "";   // batch & plates: "5" = a "Plates" summary row (QuoteRows.GroupPlates; "" = none)
        public bool GroupSameRates;       // GroupBreakdownInfo.SameRates: "g × kr/kg" etc. in the details
        public string TopUpLabel = "";    // "Minimum price (50,00 kr.)" ("" = none; data for the checks only, never drawn)
        public string TopUpAmount = "";   // its amount ("18,00 kr.")
        public bool GroupFriends;
        public double GroupTotal;
        // the combined bill's layout step, chosen by QuotePage.ChooseGroupLevel so the whole bill fits ONE A4 page when it
        // can (qol #6): 0 = normal, 1 = compact breakdown (wear parts on one line, at most 4 colour rows), 2 = exactly 4
        // prints as 4 narrower cards in one row (always for 4), 3 = 4 in a row + the compact breakdown
        internal int GroupLevel;
    }

    /// One job's card on a combined bill (picture, name, quote no. / date, print time, weight, print price; MainForm.BuildGroupQuote).
    class QuoteJob
    {
        public string Name = "";          // model name (2 lines, cut with … on the card)
        public string Detail = "";        // "3 h 10 m · 86 g · PLA Matte" (QuoteRows.JobDetail; data, not drawn)
        public string Sub = "";           // "Quote no. 261103-1412 · 3 November 2026" (QuoteRows.JobSub; one line per part on the card)
        public string Amount = "";        // "178,00 kr." = its print price with its share of any top-up (QuoteRows.JobOre)
        public long Ore;                  // the same in øre
        public Image Picture;             // not owned; null = a drawn cube
        public int Hours, Minutes;
        public string Weight = "";        // "30 g"
        public string Copies = "";        // batch & plates: "4 pcs · 2 plates" = a third card row "Quantity" ("" = none)
    }

    /// The customer quote page (idea 5): one A4 page with logo, model, details and the full price breakdown.
    /// Light (printing, mail, always the PDF) or dark (the app's own colours). Layout in 1000 units wide, drawn at
    /// PageWidth px (A4 at 150 dpi); taller than A4 only when the breakdown needs it.
    static class QuotePage
    {
        public const int PageWidth = 1240;
        const float W = 1000f;                   // layout width in page units
        internal const float A4Height = W * 297f / 210f;  // 1414

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

        /// A combined bill (2+ job lines).
        public static bool IsGroup(QuoteData d) { return d != null && d.Jobs != null && d.Jobs.Count >= 2; }

        static bool FriendsOf(QuoteData d) { return IsGroup(d) ? d.GroupFriends : d.Breakdown.Friends; }

        public static Bitmap Render(QuoteData d, bool dark)
        {
            var p = Palette(dark, FriendsOf(d));
            if (IsGroup(d)) ChooseGroupLevel(d, p);   // the preview shows the same layout as the PDF
            float need;
            bool compact;
            using (var tmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(tmp))
            {
                Prep(g);
                need = MeasureSingle(g, p, d, out compact);
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
                Draw(g, p, d, pageH, true, compact);
            }
            return bmp;
        }

        /// The height the page needs. A single page that would pass A4 is measured again in its compact form (tighter
        /// breakdown rows and gaps, qol #5); compact = that form is used (it fits, or is at least shorter). A page that
        /// fits A4 today is never changed.
        static float MeasureSingle(Graphics g, Pal p, QuoteData d, out bool compact)
        {
            compact = false;
            float need = Draw(g, p, d, A4Height, false);
            if (IsGroup(d) || need <= A4Height) return need;
            float tight = Draw(g, p, d, A4Height, false, true);
            if (tight < need) { compact = true; return tight; }
            return need;
        }

        /// --test-bill: a single page fits A4 (no shrinking onto the PDF page); compact = it needed the compact form.
        internal static bool SingleFits(QuoteData d, out bool compact, out float height)
        {
            using (var tmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(tmp))
            {
                Prep(g);
                height = MeasureSingle(g, Palette(false, FriendsOf(d)), d, out compact);
            }
            return height <= A4Height + 0.5f;
        }

        /// Picks the combined bill's layout (QuoteData.GroupLevel): the first step whose PDF plan is ONE page; none = 0
        /// (today's layout, broken over pages).
        static void ChooseGroupLevel(QuoteData d, Pal p)
        {
            // levels: 0 = as drawn before, 1 = shorter breakdown, 2 = 4 cards in one row, 3 = 4 in a row + shorter breakdown.
            // Exactly 4 prints: always 4 in a row (the user's choice, 2026-11-20): 2, then 3, else 2 over two pages.
            int[] order = d.Jobs.Count == 4 ? new[] { 2, 3 } : new[] { 0, 1 };
            foreach (int level in order)
            {
                d.GroupLevel = level;
                if (PagePlan(d, p).Count == 1) return;
            }
            d.GroupLevel = order[0];
        }

        // combined bill on paper: job cards side by side, at most CardCols per row (2 jobs: 2 wider cards), CardGap
        // between them and RowGap under each row; a page's content ends by PageLimit (the "Made with" rule is at
        // A4Height - 62); later pages start at ContinueTop. The whole bill's breakdown shows at most MaxGroupColours
        // filament sub-rows (the rest summed in one "N more colours" row).
        // NoteH: the "Price breakdown and total on the next page →" line under the cards of a page without them
        const float ContinueTop = 60, PageLimit = A4Height - 80, CardGap = 24, RowGap = 24, NoteH = 46;
        const int CardCols = 3, MaxGroupColours = 6;

        static int Cols(QuoteData d)
        {
            if (d.GroupLevel >= 2 && d.Jobs.Count == 4) return 4;   // 4 prints: always one row of 4
            return Math.Max(1, Math.Min(CardCols, d.Jobs.Count));
        }

        /// The A4 pages of a PDF: a single job = the one Render page (exactly as before; a taller one is scaled onto A4
        /// by Pdf); a combined bill = A4 pages broken between job lines, the header only on page 1, the total and payment
        /// after the last job, "Page 2 of 2" in the footer when there are 2+. The caller disposes the bitmaps.
        public static List<Bitmap> RenderPages(QuoteData d, bool dark)
        {
            var res = new List<Bitmap>();
            if (!IsGroup(d)) { res.Add(Render(d, dark)); return res; }
            var p = Palette(dark, FriendsOf(d));
            ChooseGroupLevel(d, p);
            var plan = PagePlan(d, p);
            float k = PageWidth / W;
            for (int i = 0; i < plan.Count; i++)
            {
                var bmp = new Bitmap(PageWidth, (int)Math.Ceiling(A4Height * k), PixelFormat.Format32bppArgb);
                bmp.SetResolution(150, 150);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(p.Page);
                    g.ScaleTransform(k, k);
                    Prep(g);
                    Draw(g, p, d, A4Height, true, plan[i][0], plan[i][1], i == 0, plan[i][2] == 1,
                         plan.Count > 1 ? L.F("Page {0} of {1}", "Side {0} af {1}", i + 1, plan.Count) : null, NoteText(plan, i));
                }
                res.Add(bmp);
            }
            return res;
        }

        /// The note under the cards of plan page i when the breakdown / total part is on a later page (the user's wish:
        /// easy to read): "Price breakdown and total on the next page →" (or "on page N →"); null = none.
        static string NoteText(List<int[]> plan, int i)
        {
            if (plan[i][2] == 1 || plan[i][1] <= plan[i][0]) return null;
            int tailPage = plan.Count;   // the breakdown part is always on the last page
            return tailPage == i + 2
                ? L.S("Breakdown and total: next page →", "Prisoversigt og total: næste side →")
                : L.F("Breakdown and total: page {0} →", "Prisoversigt og total: side {0} →", tailPage);
        }

        /// --test-bill: the PDF's pages as "from-to" job ranges, "+note" on a page with the "next page" note, "+total" on
        /// the page with the breakdown / total / payment ("0-9+note | 9-12+total"); fits = that part is on exactly one page, the last, after the last job, and is no taller
        /// than a page.
        internal static string PageLayout(QuoteData d, out bool fits)
        {
            fits = true;
            if (!IsGroup(d)) return "single";
            var p = Palette(false, FriendsOf(d));
            ChooseGroupLevel(d, p);
            var plan = PagePlan(d, p);
            var parts = new List<string>();
            int tails = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                parts.Add(plan[i][0] + "-" + plan[i][1] + (NoteText(plan, i) != null ? "+note" : "") + (plan[i][2] == 1 ? "+total" : ""));
                if (plan[i][2] == 1) { tails++; if (i != plan.Count - 1 || plan[i][1] != d.Jobs.Count) fits = false; }
            }
            if (tails != 1) fits = false;
            using (var tmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(tmp))
            {
                Prep(g);
                float tailH = Draw(g, p, d, A4Height, false, 0, 0, false, true, null) - 90 - ContinueTop;
                if (ContinueTop + tailH > PageLimit) fits = false;
            }
            return string.Join(" | ", parts);
        }

        /// How many A4 pages the PDF of d has (1 for a single job).
        internal static int PageCount(QuoteData d)
        {
            if (!IsGroup(d)) return 1;
            var p = Palette(false, FriendsOf(d));
            ChooseGroupLevel(d, p);
            return PagePlan(d, p).Count;
        }

        /// Pages of a combined bill: {from job, to job (exclusive), 1 = the breakdown / total / payment part is on it}.
        /// Whole rows of cards, as many per page as fit (at least one; room kept under them for the note); the breakdown
        /// part comes after the last row when it fits there, else on the next page of its own (every page before it then
        /// says where it is: RenderPages' note). Always the last page.
        static List<int[]> PagePlan(QuoteData d, Pal p)
        {
            int n = d.Jobs.Count, cols = Cols(d);
            float headEnd, tailH;
            var rowH = new List<float>();
            using (var tmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(tmp))
            {
                Prep(g);
                headEnd = Draw(g, p, d, A4Height, false, 0, 0, true, false, null) - 90;
                tailH = Draw(g, p, d, A4Height, false, 0, 0, false, true, null) - 90 - ContinueTop;
                for (int a = 0; a < n; a += cols)
                    rowH.Add(Draw(g, p, d, A4Height, false, a, Math.Min(n, a + cols), false, false, null) - 90 - ContinueTop);
            }
            var plan = new List<int[]>();
            int pageFrom = 0, rowsOnPage = 0;
            float top = headEnd;
            for (int k = 0; k < rowH.Count; k++)
            {
                if (rowsOnPage > 0 && top + rowH[k] - RowGap + NoteH > PageLimit)   // room kept for the "next page" note
                {
                    plan.Add(new[] { pageFrom, k * cols, 0 });
                    pageFrom = k * cols; rowsOnPage = 0; top = ContinueTop;
                }
                top += rowH[k];
                rowsOnPage++;
            }
            if (top + tailH <= PageLimit) plan.Add(new[] { pageFrom, n, 1 });
            else
            {
                plan.Add(new[] { pageFrom, n, 0 });
                plan.Add(new[] { n, n, 1 });
            }
            return plan;
        }

        static void Prep(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        // breakdown rows, dates and other texts: QuoteRows (src\QuoteRows.cs, no drawing, shared with the website)

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

        /// A name in up to maxLines lines of width: word wrap, but the spaces inside brackets don't break ("Benchy (0.4
        /// nozzle)" stays together when it can); a word wider than a line is broken by characters; text left after the last
        /// line is cut with … (FitText).
        static List<string> WrapName(Graphics g, string text, Font font, float width, int maxLines)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            using (var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None })
            {
                Func<string, float> Wd = t => g.MeasureString(t, font, new SizeF(100000, 1000), sf).Width;
                // words: spaces inside ( ) / [ ] kept as part of the word
                var words = new List<string>();
                var cur = new StringBuilder();
                int depth = 0;
                foreach (char c in text.Trim())
                {
                    if (c == '(' || c == '[') depth++;
                    if ((c == ')' || c == ']') && depth > 0) depth--;
                    if (c == ' ' && depth == 0) { if (cur.Length > 0) { words.Add(cur.ToString()); cur.Clear(); } continue; }
                    cur.Append(c);
                }
                if (cur.Length > 0) words.Add(cur.ToString());
                // a bracket group wider than a line: back to its own words
                var parts = new List<string>();
                foreach (var w in words)
                {
                    if (w.IndexOf(' ') >= 0 && Wd(w) > width) parts.AddRange(w.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                    else parts.Add(w);
                }
                string line = "";
                int i = 0;
                while (i < parts.Count)
                {
                    string next = line.Length == 0 ? parts[i] : line + " " + parts[i];
                    if (Wd(next) <= width) { line = next; i++; continue; }
                    if (lines.Count == maxLines - 1) break;   // last line: the rest is cut with … below
                    if (line.Length == 0)
                    {
                        // one word wider than the line: as many characters as fit, with a hyphen
                        string w = parts[i];
                        int n = 1;
                        while (n < w.Length - 1 && Wd(w.Substring(0, n + 1) + "-") <= width) n++;
                        line = w.Substring(0, n) + (char.IsLetterOrDigit(w[n - 1]) ? "-" : "");
                        parts[i] = w.Substring(n);
                    }
                    lines.Add(line);
                    line = "";
                }
                if (i < parts.Count)
                {
                    string rest = line;
                    for (int k = i; k < parts.Count; k++) rest = rest.Length == 0 ? parts[k] : rest + " " + parts[k];
                    lines.Add(FitText(g, rest, font, width));
                }
                else if (line.Length > 0) lines.Add(line);
            }
            return lines;
        }

        // ---------- drawing ----------
        /// Draws the page (draw = false only measures) and returns the height it needs.
        static float Draw(Graphics g, Pal p, QuoteData d, float pageH, bool draw)
        {
            return Draw(g, p, d, pageH, draw, 0, d.Jobs != null ? d.Jobs.Count : 0, true, true, null);
        }

        /// As above; compact = a single page in its compact form (MeasureSingle: tighter breakdown and gaps).
        static float Draw(Graphics g, Pal p, QuoteData d, float pageH, bool draw, bool compact)
        {
            return Draw(g, p, d, pageH, draw, 0, d.Jobs != null ? d.Jobs.Count : 0, true, true, null, null, compact);
        }

        /// As above for one page of a combined bill (RenderPages): job lines from..to (exclusive), head = the header and
        /// summary (page 1), tail = the total and payment part, pageText = "Page 1 of 2" in the footer (null = none).
        /// A single job always draws everything (from / to / tail unused).
        static float Draw(Graphics g, Pal p, QuoteData d, float pageH, bool draw, int from, int to, bool head, bool tail, string pageText,
                          string noteText = null, bool compact = false)
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
                if (draw && head)
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
                if (head)
                {
                    T(d.Invoice ? L.S("Invoice", "Faktura") : L.S("3D print quote", "Tilbud på 3D-print"), F(40, true), p.HeaderText, tx, 42, 0, near);
                    if (!string.IsNullOrEmpty(d.Name))
                        T(d.Name + L.S(" · 3D printing", " · 3D-print"), F(18, false), p.HeaderMuted, tx + 3, 98, 0, near);
                    if (d.Invoice)
                    {
                        // right block: invoice no., invoice date, due date (the customer is in the "Bill to" row)
                        T(L.F("Invoice no. {0}", "Faktura nr. {0}", d.InvoiceNo), F(17, false), p.HeaderText, 560, 46, 400, far);
                        T(L.F("Invoice date {0}", "Fakturadato {0}", LongDate(d.Date)), F(17, false), p.HeaderMuted, 560, 74, 400, far);
                        if (string.IsNullOrEmpty(d.PaidLine))   // paid-tick (qol 8): a paid invoice has no due date in the header
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
                }

                if (IsGroup(d)) return DrawGroup(g, p, d, pageH, draw, from, to, head, tail, pageText, noteText, F, T, HLine);

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
                // the name: own 2-line wrap (brackets kept together, the rest cut with …)
                var nameLines = WrapName(g, d.Model, nameFont, nameW, 2);
                float lineH = nameFont.GetHeight(g);
                float nameH = Math.Max(1, nameLines.Count) * lineH;
                for (int i = 0; i < nameLines.Count; i++) T(nameLines[i], nameFont, p.Ink, x, y0 - 4 + i * lineH, 0, near);
                float y = y0 - 4 + nameH + 8;
                if (d.Link.Length > 0) { T(d.Link, F(16, false), p.Muted, x + 3, y, nameW - 3, near); y += 28; }
                y = Math.Max(y + 10, y0 + 110);
                // invoice: "Bill to" is the top line, above the print time (the user's wish); a quote keeps its Printer row
                var details = d.Invoice
                    ? new[]
                    {
                        new[] { L.S("Bill to", "Kunde"), d.Customer ?? "" },
                        new[] { L.S("Print time", "Printtid"), QuoteRows.Time(d.Hours, d.Minutes) },
                        new[] { L.S("Weight", "Vægt"), d.Weight },
                        new[] { L.S("Material", "Materiale"), d.Material },
                    }
                    : new[]
                    {
                        new[] { L.S("Print time", "Printtid"), QuoteRows.Time(d.Hours, d.Minutes) },
                        new[] { L.S("Weight", "Vægt"), d.Weight },
                        new[] { L.S("Printer", "Printer"), d.Printer },
                        new[] { L.S("Material", "Materiale"), d.Material },
                    };
                // batch & plates: the price per piece goes before the total box's small line ("4 pcs · 18,18 kr. each ·
                // shipping not included") when that fits left of the total; else it is the Quantity row's value
                string eachTotal = null;
                if (!string.IsNullOrEmpty(d.EachLine))
                {
                    string small0 = d.Invoice ? PayLine(d) : L.S("Shipping not included", "Fragt er ikke inkluderet");
                    bool lower0 = small0.Length > 0 && string.IsNullOrEmpty(d.PaidLine);
                    string withEach = d.EachLine + (small0.Length > 0 ? " · " + (lower0 ? char.ToLower(small0[0]) + small0.Substring(1) : small0) : "");
                    float room = 900 - 80 - 30 - g.MeasureString(Num.Kr(d.Breakdown.Total), F(56, true)).Width;
                    if (g.MeasureString(withEach, F(16, false)).Width <= room) eachTotal = withEach;
                }
                if (d.Quantity > 1)
                {
                    // batch & plates: "Quantity  4 pcs" first (after "Bill to" on an invoice), Print time "… on 2 plates"
                    var list = new List<string[]>(details);
                    int timeAt = d.Invoice ? 1 : 0;
                    list[timeAt] = new[] { list[timeAt][0], QuoteRows.TimeOnPlates(d.Hours, d.Minutes, d.Plates) };
                    string qty = eachTotal == null && !string.IsNullOrEmpty(d.EachLine) ? d.EachLine : History.PiecesText(d.Quantity);
                    list.Insert(timeAt, new[] { History.QuantityLabel(), qty });
                    details = list.ToArray();
                }
                float valueX = x + (L.Danish ? 140 : 130);
                foreach (var r in details)
                {
                    HLine(x, R0, y, p.Line, 1);
                    T(r[0], F(18, false), p.Muted, x + 3, y + 10, 0, near);
                    T(r[1], F(18, true), p.Ink, valueX, y + 10, R0 - valueX, near);
                    y += 46;
                }
                HLine(x, R0, y, p.Line, 1);
                y = compact ? Math.Max(y + 40, y0 + pic + 36) : Math.Max(y + 58, y0 + pic + 52);

                // price breakdown
                T(L.S("Price breakdown", "Prisoversigt"), F(24, true), p.Ink, L0, y, 0, near);
                y += 44;
                HLine(L0, R0, y, p.Line, 2);
                y += 14;
                y = DrawRows(g, p, d, QuoteRows.AddTowerNote(QuoteRows.Build(d.Breakdown, d.MaterialNames, d.PurgeGrams, d.Hours, d.Minutes, d.PlateName), d.TowerGrams),
                             y, draw, F, T, compact);
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
                T(eachTotal ?? (d.Invoice ? PayLine(d) : L.S("Shipping not included", "Fragt er ikke inkluderet")),
                  F(16, false), p.Muted, 80, y + 62, 0, near);
                T(Num.Kr(d.Breakdown.Total), F(56, true), p.Total, 420, y + 14, 510, far);
                y += compact ? 128 : 140;

                if (d.Invoice)
                {
                    // payment: only the ways that are set in Settings; none set = no block
                    var pay = new List<string>();
                    if (!string.IsNullOrEmpty(d.PayMobilePay)) pay.Add("MobilePay: " + d.PayMobilePay);
                    if (!string.IsNullOrEmpty(d.PayBank)) pay.Add(L.S("Bank: ", "Bank: ") + d.PayBank);
                    if (!string.IsNullOrEmpty(d.PaidLine)) pay.Clear();   // paid-tick: a paid invoice has no Payment block
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

        /// The body of a combined bill (2+ jobs) under the header: summary rows two to a line ("Bill to" / "Prints" /
        /// "Printer" / "Plate"), then the job cards side by side (up to CardCols per row, DrawCard; the cards of a row are as
        /// tall as the tallest), the whole bill's "Price breakdown" (as a single page's), the total box ("Total · 3 prints",
        /// the job prices "a + b + c" on its small line when they fit) and the payment block / valid-until.
        /// head / tail / from / to: see Draw (from / to are whole rows).
        static float DrawGroup(Graphics g, Pal p, QuoteData d, float pageH, bool draw, int from, int to, bool head, bool tail,
                               string pageText, string noteText, Func<float, bool, Font> F,
                               Action<string, Font, Color, float, float, float, StringAlignment> T, Action<float, float, float, Color, float> HLine)
        {
            const float L0 = 40, R0 = 960;
            var near = StringAlignment.Near;
            var far = StringAlignment.Far;
            float y;
            if (head)
            {
                // summary rows, two to a line (label at 43 / 503, value at 170 / 630; DA 180 / 640): Bill to (invoice) and
                // Prints; the shared printer / build plate are beside the "Price breakdown" heading (the user's wish)
                var rows = new List<string[]>();
                if (d.Invoice) rows.Add(new[] { L.S("Bill to", "Kunde"), d.Customer ?? "" });
                string summary = d.Summary ?? "";
                int cut = summary.IndexOf(" · ", StringComparison.Ordinal);
                if (!string.IsNullOrEmpty(d.GroupPlates) && cut > 0)
                {
                    // batch & plates (qol #2): "3 models, 45 pcs" | "Plates 4" | "In all 6 h 10 m · 52,6 g" - the one long
                    // Prints line was cut off in Danish and "Plates" sat alone on its row
                    rows.Add(new[] { L.S("Prints", "Print"), summary.Substring(0, cut) });
                    rows.Add(new[] { L.S("Plates", "Plader"), d.GroupPlates });
                    rows.Add(new[] { L.S("In all", "I alt"), summary.Substring(cut + 3) });
                }
                else
                {
                    rows.Add(new[] { L.S("Prints", "Print"), summary });
                    if (!string.IsNullOrEmpty(d.GroupPlates)) rows.Add(new[] { L.S("Plates", "Plader"), d.GroupPlates });
                }
                float vx = L.Danish ? 140 : 130, mid = 500;
                y = 214;
                for (int k = 0; k < rows.Count; k += 2)
                {
                    HLine(L0, R0, y, p.Line, 1);
                    bool pair = k + 1 < rows.Count;
                    T(rows[k][0], F(18, false), p.Muted, L0 + 3, y + 10, 0, near);
                    T(rows[k][1], F(18, true), p.Ink, L0 + vx, y + 10, (pair ? mid - 10 : R0) - L0 - vx, near);
                    if (pair)
                    {
                        T(rows[k + 1][0], F(18, false), p.Muted, mid + 3, y + 10, 0, near);
                        T(rows[k + 1][1], F(18, true), p.Ink, mid + vx, y + 10, R0 - mid - vx, near);
                    }
                    y += 46;
                }
                HLine(L0, R0, y, p.Line, 1);
                y += 26;
            }
            else
            {
                // a later page: which bill it belongs to (qol 7), Muted 14 at the top, a rule under it
                var bits = new List<string>();
                if (d.Invoice) bits.Add(L.F("Invoice no. {0}", "Faktura nr. {0}", d.InvoiceNo));
                else
                {
                    bits.Add(L.S("3D print quote", "Tilbud på 3D-print"));
                    if (!string.IsNullOrEmpty(d.Number)) bits.Add(L.F("Quote no. {0}", "Tilbud nr. {0}", d.Number));
                }
                if (!string.IsNullOrEmpty(d.Customer)) bits.Add(d.Customer);
                bits.Add(LongDate(d.Date));
                T(string.Join("  ·  ", bits), F(14, false), p.Muted, L0 + 3, 20, R0 - L0 - 3, near);
                HLine(L0, R0, 44, p.Line, 1);
                y = ContinueTop;
            }

            // the cards, row by row
            int cols = Cols(d);
            float cardW = (R0 - L0 - CardGap * (cols - 1)) / cols;
            bool anyPicture = false;
            foreach (var j in d.Jobs) if (j.Picture != null) anyPicture = true;
            for (int a = from; a < to && a < d.Jobs.Count; a += cols)
            {
                int b = Math.Min(Math.Min(to, d.Jobs.Count), a + cols);
                float rowH = 0;
                for (int i = a; i < b; i++)
                    rowH = Math.Max(rowH, DrawCard(g, p, d.Jobs[i], L0 + (i - a) * (cardW + CardGap), y, cardW, 0, false, F, T, HLine));
                if (draw)
                    for (int i = a; i < b; i++)
                        DrawCard(g, p, d.Jobs[i], L0 + (i - a) * (cardW + CardGap), y, cardW, rowH, true, F, T, HLine);
                y += rowH + RowGap;
            }
            // the breakdown is on a later page: say so under the cards (bold 20 Ink, larger than the body text)
            if (!string.IsNullOrEmpty(noteText))
            {
                T(noteText, F(20, true), p.Ink, L0 + 3, y, R0 - L0 - 3, near);
                y += NoteH;
            }

            if (tail)
            {
                // the whole bill's price breakdown (History.GroupBreakdown; its minimum line is folded into Work & handling
                // by QuoteRows.BuildGroup: the minimum is never shown), kept on one page with the total and payment
                // the shared printer and build plate (only when every job shares them): Muted 16 right of the heading, or on
                // its own line under it when it doesn't fit there
                var setup = new List<string>();
                if (!string.IsNullOrEmpty(d.Printer)) setup.Add(d.Printer);
                if (!string.IsNullOrEmpty(d.PlateName)) setup.Add(L.F("{0} build plate", "Byggeplade {0}", d.PlateName));
                string setupText = string.Join("  ·  ", setup);
                if (d.Breakdown != null && d.Breakdown.Lines != null && d.Breakdown.Lines.Count > 0)
                {
                    string heading = L.S("Price breakdown", "Prisoversigt");
                    T(heading, F(24, true), p.Ink, L0, y, 0, near);
                    if (setupText.Length > 0)
                    {
                        float sx = L0 + g.MeasureString(heading, F(24, true)).Width + 30;
                        if (g.MeasureString(setupText, F(16, false)).Width <= R0 - sx)
                            T(setupText, F(16, false), p.Muted, sx, y + 7, R0 - sx, far);
                        else
                        {
                            T(setupText, F(16, false), p.Muted, L0 + 3, y + 38, R0 - L0 - 3, near);
                            y += 28;
                        }
                    }
                    y += 40;
                    HLine(L0, R0, y, p.Line, 2);
                    y += 12;
                    // GroupLevel 1 / 3: shorter breakdown (wear parts on one note line, at most 4 colour rows) so it fits one page
                    bool shortRows = d.GroupLevel == 1 || d.GroupLevel == 3;
                    y = DrawRows(g, p, d, QuoteRows.AddTowerNote(QuoteRows.BuildGroup(d.Breakdown, d.MaterialNames, d.Hours, d.Minutes, d.PlateName, d.GroupSameRates,
                                                               shortRows ? 4 : MaxGroupColours, shortRows), d.TowerGrams), y, draw, F, T, true);
                    y += 4;
                    HLine(L0, R0, y, p.Line, 2);
                    y += 20;
                }
                else if (setupText.Length > 0)
                {
                    T(setupText, F(16, false), p.Muted, L0 + 3, y, R0 - L0 - 3, near);
                    y += 32;
                }
                // total box (as a single page, the label with the number of prints)
                if (draw)
                {
                    using (var path = RoundRect(L0, y, R0 - L0, 110, 14))
                    {
                        using (var br = new SolidBrush(p.TotalBack)) g.FillPath(br, path);
                        if (p.TotalLine != Color.Empty) using (var pen = new Pen(p.TotalLine, 1)) g.DrawPath(pen, path);
                    }
                    using (var br = new SolidBrush(p.Stripe)) g.FillRectangle(br, L0, y + 12, 8, 86);
                }
                T(L.S("Total", "I alt") + " · " + QuoteRows.PrintsText(d.Jobs.Count), F(26, true), p.Ink, 80, y + 22, 340, near);
                // small line: the job prices "34,08 + 8,89 + 7,03 · please pay by …" when that fits left of the total
                string small = d.Invoice ? PayLine(d) : L.S("Shipping not included", "Fragt er ikke inkluderet");
                string total = Num.Kr(d.GroupTotal);
                float smallW = 900 - 80 - 30 - g.MeasureString(total, F(56, true)).Width;
                var sums = new List<string>();
                foreach (var j in d.Jobs) sums.Add(Num.N2(j.Ore / 100.0));
                // "· please pay by …" lower-case; the paid line keeps its capital ("· Paid 12 November 2026 – thank you", qol 8)
                bool lower = small.Length > 0 && string.IsNullOrEmpty(d.PaidLine);
                string withSums = string.Join(" + ", sums) + " · " + (lower ? char.ToLower(small[0]) + small.Substring(1) : small);
                if (g.MeasureString(withSums, F(16, false)).Width <= smallW) small = withSums;
                T(small, F(16, false), p.Muted, 80, y + 62, 0, near);
                T(total, F(56, true), p.Total, 420, y + 14, 510, far);
                y += 140;

                if (d.Invoice)
                {
                    var pay = new List<string>();
                    if (!string.IsNullOrEmpty(d.PayMobilePay)) pay.Add("MobilePay: " + d.PayMobilePay);
                    if (!string.IsNullOrEmpty(d.PayBank)) pay.Add(L.S("Bank: ", "Bank: ") + d.PayBank);
                    if (!string.IsNullOrEmpty(d.PaidLine)) pay.Clear();   // paid-tick: a paid invoice has no Payment block
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
                    T(L.F("Valid until {0}", "Gyldigt til {0}", LongDate(d.ValidUntil)), F(18, false), p.Ink, L0 + 3, y, 0, near);
                    if (anyPicture)
                    {
                        T(L.S("Colours and finish may differ slightly from the pictures.", "Farver og overflade kan afvige lidt fra billederne."),
                          F(16, false), p.Muted, L0 + 3, y + 30, 0, near);
                        y += 60;
                    }
                    else y += 30;
                }
            }
            // "made with" line at the bottom of the page (+ "Page 1 of 2" on a PDF page of several)
            HLine(L0, R0, pageH - 62, p.Line, 1);
            T(L.S("Made with Gettic's 3D Print Price Calculator", "Lavet med Gettic's 3D Print Price Calculator"),
              F(14, false), p.Muted, L0, pageH - 44, R0 - L0, StringAlignment.Center);
            if (!string.IsNullOrEmpty(pageText)) T(pageText, F(14, false), p.Muted, R0 - 200, pageH - 44, 200, far);
            return y + 90;
        }

        /// One job's card at (x, y), w wide (h = the row's height: the print price box sits at its bottom; 0 = measure):
        /// rounded 1.5 px Line border; 76 px picture well (or a cube); right of it the name bold 18 (2 lines, …) and Faint 12
        /// lines "Quote no. …" and the date; Print time and Weight rows (Muted 14 label, bold 14 value, 1 px Line rules);
        /// the "Print price" box (TotalBack, stripe, bold 22 in the total colour). Returns the height it needs.
        static float DrawCard(Graphics g, Pal p, QuoteJob job, float x, float y, float w, float h, bool draw,
                              Func<float, bool, Font> F, Action<string, Font, Color, float, float, float, StringAlignment> T,
                              Action<float, float, float, Color, float> HLine)
        {
            const float Pad = 12, BoxH = 50;
            bool narrow = w < 250;   // 4 cards in a row (GroupLevel 2): smaller picture, short price label
            float Pic = narrow ? 56 : 76;
            var near = StringAlignment.Near;
            var far = StringAlignment.Far;
            Action<string, Font, Color, float, float, float, StringAlignment> t = (s, f, c, px, py, pw, al) => { if (draw) T(s, f, c, px, py, pw, al); };
            Action<float, float, float, Color, float> hl = (ax, bx, ly, c, lw) => { if (draw) HLine(ax, bx, ly, c, lw); };
            float top = y, R = x + w;
            if (draw && h > 0)
                using (var path = RoundRect(x, top, w, h, 10))
                    using (var pen = new Pen(p.Line, 1.5f)) g.DrawPath(pen, path);

            // picture, name (up to 2 lines, cut with … at a word), quote no. / date
            if (draw)
            {
                using (var path = RoundRect(x + Pad, top + Pad, Pic, Pic, 10))
                {
                    using (var br = new SolidBrush(p.PicBack)) g.FillPath(br, path);
                    using (var pen = new Pen(p.Line, 2)) g.DrawPath(pen, path);
                }
                if (job.Picture != null) DrawPicture(g, job.Picture, x + Pad, top + Pad, Pic, Pic);
                else DrawCube(g, p.Faint, x + Pad + Pic / 2, top + Pad + Pic / 2, Pic * 0.26f);
            }
            float tx = x + Pad + Pic + 12, tw = R - Pad - tx;
            float ny = top + Pad - 3;
            var nf = F(18, true);
            // the details start at the same height on every card: room for a 2-line name and the two faint lines
            float factsTop = Math.Max(top + Pad + Pic, ny + nf.GetHeight(g) * 2 + 4 + 2 * 17) + 10;
            if (!string.IsNullOrEmpty(job.Name))
            {
                // own 2-line wrap (brackets kept together, the rest cut with …)
                var nameLines = WrapName(g, job.Name, nf, tw, 2);
                float lh = nf.GetHeight(g);
                for (int i = 0; i < nameLines.Count; i++) t(nameLines[i], nf, p.Ink, tx, ny + i * lh, 0, near);
                ny += Math.Max(1, nameLines.Count) * lh + 4;
            }
            foreach (var part in (job.Sub ?? "").Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries))
            {
                t(part, F(12, false), p.Faint, tx, ny, tw, near);
                ny += 17;
            }
            y = Math.Max(factsTop, ny + 10);

            // print time and weight, as before
            var facts = new List<string[]>
            {
                new[] { L.S("Print time", "Printtid"), QuoteRows.Time(job.Hours, job.Minutes) },
                new[] { L.S("Weight", "Vægt"), job.Weight },
            };
            float valueX = x + Pad + (L.Danish ? 82 : 78);
            if (!string.IsNullOrEmpty(job.Copies))
            {
                // batch & plates: "Quantity  4 pcs · 2 plates" ("4 pcs" alone when the card is too narrow for both)
                string c = job.Copies;
                int cut = c.IndexOf(" · ", StringComparison.Ordinal);
                if (cut > 0 && g.MeasureString(c, F(14, true)).Width > R - Pad - valueX) c = c.Substring(0, cut);
                facts.Add(new[] { History.QuantityLabel(), c });
            }
            foreach (var r in facts)
            {
                hl(x + 10, R - 10, y, p.Line, 1);
                t(r[0], F(14, false), p.Muted, x + Pad + 2, y + 6, 0, near);
                t(r[1], F(14, true), p.Ink, valueX, y + 6, R - Pad - valueX, near);
                y += 28;
            }
            hl(x + 10, R - 10, y, p.Line, 1);
            y += 10;

            // the print price box: at the bottom of the row's height
            float boxY = h > 0 ? Math.Max(y, top + h - Pad - BoxH) : y;
            if (draw)
            {
                using (var path = RoundRect(x + 10, boxY, w - 20, BoxH, 8))
                {
                    using (var br = new SolidBrush(p.TotalBack)) g.FillPath(br, path);
                    if (p.TotalLine != Color.Empty) using (var pen = new Pen(p.TotalLine, 1)) g.DrawPath(pen, path);
                }
                using (var br = new SolidBrush(p.Stripe)) g.FillRectangle(br, x + 10, boxY + 8, 5, BoxH - 16);
            }
            float priceW = g.MeasureString(job.Amount ?? "", F(22, true)).Width;
            t(narrow ? L.S("Price", "Pris") : L.S("Print price", "Printpris"), F(16, true), p.Ink, x + 26, boxY + 13, R - 24 - priceW - 8 - (x + 26), near);
            t(job.Amount, F(22, true), p.Total, R - 24 - priceW - 10, boxY + 8, priceW + 10, far);
            return boxY + BoxH + Pad - top;
        }

        /// The breakdown rows (QuoteRows.Build / BuildGroup) from y: heads bold 19, sub-lines 17 with a colour dot by
        /// d.MaterialColours, notes faint 15, rules 1 px. Returns y under the last row (a sub-line block: + 10). compact (the combined
        /// bill, so 3 prints fit one A4 page): the same sizes with tighter steps (heads 38 / 32, sub-lines 27, rules 12, + 6).
        static float DrawRows(Graphics g, Pal p, QuoteData d, List<QuoteRow> rows, float y, bool draw,
                              Func<float, bool, Font> F, Action<string, Font, Color, float, float, float, StringAlignment> T,
                              bool compact = false)
        {
            const float L0 = 40, R0 = 960;
            float subEnd = compact ? 6 : 10;
            var near = StringAlignment.Near;
            var far = StringAlignment.Far;
            QuoteRowKind prev = QuoteRowKind.Rule;
            foreach (var r in rows)
            {
                switch (r.Kind)
                {
                    case QuoteRowKind.Rule:
                        if (prev == QuoteRowKind.Sub) y += subEnd;
                        if (draw) using (var lp = new Pen(p.Line, 1)) g.DrawLine(lp, L0, y, R0, y);
                        y += compact ? 12 : 14;
                        break;
                    case QuoteRowKind.Head:
                        T(r.Label, F(19, true), p.Ink, L0 + 3, y, 420, near);
                        T(r.Detail, F(16, false), p.Muted, 470, y + 2, 240, near);
                        T(r.Amount, F(19, true), p.Ink, 700, y, R0 - 700, far);
                        y += compact ? (r.Alone ? 38 : 32) : (r.Alone ? 44 : 36);
                        break;
                    case QuoteRowKind.Sub:
                        float lx = 70;
                        Color dot = r.ColourIndex >= 0 && r.ColourIndex < d.MaterialColours.Count ? d.MaterialColours[r.ColourIndex] : Color.Empty;
                        if (dot != Color.Empty)
                        {
                            if (draw)
                            {
                                using (var br = new SolidBrush(dot)) g.FillEllipse(br, 70, y + 5, 15, 15);
                                using (var pen = new Pen(p.DotRing, 1.5f)) g.DrawEllipse(pen, 70, y + 5, 15, 15);
                            }
                            lx = 94;
                        }
                        T(r.Label, F(17, false), p.Ink, lx, y, 465 - lx, near);
                        T(r.Detail, F(16, false), p.Muted, 470, y + 1, 240, near);
                        T(r.Amount, F(17, false), p.Ink, 700, y, R0 - 30 - 700, far);
                        y += compact ? 27 : 32;
                        break;
                    default:
                        if (prev == QuoteRowKind.Note) y -= 10;   // a second note (prime tower) sits as close as text lines
                        T(r.Label, F(15, false), p.Faint, 70, y, R0 - 70, near);
                        y += 34;
                        break;
                }
                prev = r.Kind;
            }
            if (prev == QuoteRowKind.Sub) y += subEnd;
            return y;
        }

        /// A cube outline (no picture for a job): hexagon + the three inner edges, 2 px.
        static void DrawCube(Graphics g, Color col, float cx, float cy, float s)
        {
            float w = s * 0.87f;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(col, 2) { LineJoin = LineJoin.Round })
            {
                g.DrawPolygon(pen, new[]
                {
                    new PointF(cx, cy - s), new PointF(cx + w, cy - s / 2), new PointF(cx + w, cy + s / 2),
                    new PointF(cx, cy + s), new PointF(cx - w, cy + s / 2), new PointF(cx - w, cy - s / 2)
                });
                g.DrawLine(pen, cx, cy, cx - w, cy - s / 2);
                g.DrawLine(pen, cx, cy, cx + w, cy - s / 2);
                g.DrawLine(pen, cx, cy, cx, cy + s);
            }
            g.Restore(st);
        }

        static string LongDate(DateTime t) { return QuoteRows.LongDate(t); }

        /// The invoice's line under the total: "Paid 05-11-2026 – thank you" once marked paid (paid-tick), else "Please pay by …".
        static string PayLine(QuoteData d)
        {
            if (!string.IsNullOrEmpty(d.PaidLine)) return d.PaidLine;
            return L.F("Please pay by {0}", "Betales senest {0}", LongDate(d.Due));
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

        /// A PDF with one A4 page per picture (RenderPages: a combined bill of many jobs).
        public static void SavePdf(IList<Bitmap> pages, string path)
        {
            File.WriteAllBytes(path, Pdf(pages));
        }

        internal static byte[] Pdf(Bitmap bmp)
        {
            return Pdf(new List<Bitmap> { bmp });
        }

        /// The PDF bytes: objects 1 catalog, 2 pages, then per page k (from 0) 3+3k page, 4+3k image, 5+3k content
        /// (one page = exactly the old one-page file).
        internal static byte[] Pdf(IList<Bitmap> pages)
        {
            var inv = CultureInfo.InvariantCulture;
            const double pw = 595.28, ph = 841.89;   // A4 in points
            var outp = new MemoryStream();
            var offsets = new List<long>();
            Action<string> Put = str => { var b = Encoding.ASCII.GetBytes(str); outp.Write(b, 0, b.Length); };
            Put("%PDF-1.4\n");
            outp.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 6);   // binary marker comment
            offsets.Add(outp.Position); Put("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            var kids = new StringBuilder();
            for (int k = 0; k < pages.Count; k++) kids.Append(k == 0 ? "" : " ").Append(3 + 3 * k).Append(" 0 R");
            offsets.Add(outp.Position); Put("2 0 obj\n<< /Type /Pages /Kids [" + kids + "] /Count " + pages.Count.ToString(inv) + " >>\nendobj\n");
            for (int k = 0; k < pages.Count; k++)
            {
                var bmp = pages[k];
                byte[] jpg = Jpeg(bmp);
                int pageObj = 3 + 3 * k, imgObj = pageObj + 1, contentObj = pageObj + 2;
                double s = Math.Min(pw / bmp.Width, ph / bmp.Height);
                double dw = bmp.Width * s, dh = bmp.Height * s;
                double dx = (pw - dw) / 2, dy = ph - dh;   // top of the page; a taller-than-A4 picture is scaled to fit
                string content = string.Format(inv, "q {0:0.###} 0 0 {1:0.###} {2:0.###} {3:0.###} cm /Im0 Do Q", dw, dh, dx, dy);
                offsets.Add(outp.Position);
                Put(string.Format(inv, "{2} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.##} {1:0.##}] " +
                    "/Resources << /XObject << /Im0 {3} 0 R >> >> /Contents {4} 0 R >>\nendobj\n", pw, ph, pageObj, imgObj, contentObj));
                offsets.Add(outp.Position);
                Put(string.Format(inv, "{3} 0 obj\n<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB " +
                    "/BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n", bmp.Width, bmp.Height, jpg.Length, imgObj));
                outp.Write(jpg, 0, jpg.Length);
                Put("\nendstream\nendobj\n");
                offsets.Add(outp.Position);
                Put(string.Format(inv, "{2} 0 obj\n<< /Length {0} >>\nstream\n{1}\nendstream\nendobj\n", Encoding.ASCII.GetByteCount(content), content, contentObj));
            }
            long xref = outp.Position;
            int size = offsets.Count + 1;
            Put(string.Format(inv, "xref\n0 {0}\n0000000000 65535 f \n", size));
            foreach (long o in offsets) Put(o.ToString("0000000000", inv) + " 00000 n \n");
            Put(string.Format(inv, "trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n", size, xref));
            return outp.ToArray();
        }

        /// The page as JPEG 92 % (flattened onto white: JPEG has no alpha).
        static byte[] Jpeg(Bitmap bmp)
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
                        // explicit pixel rectangle: DrawImageUnscaled would draw at physical size (150-dpi page onto a
                        // 96-dpi bitmap = 64 %, issue #1)
                        rgb.SetResolution(bmp.HorizontalResolution, bmp.VerticalResolution);
                        using (var g = Graphics.FromImage(rgb)) { g.Clear(Color.White); g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height)); }
                        rgb.Save(ms, codec, ps);
                    }
                }
                jpg = ms.ToArray();
            }
            return jpg;
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
