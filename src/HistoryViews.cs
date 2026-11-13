using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Idea 22 (issue #5): the History window's pictures (lazy, cached), the List / Medium / Large switch and the
    /// hand-drawn picture cards. The window itself is in HistoryForm.cs.
    public partial class HistoryForm
    {
        public const int ViewList = AppSettings.HistoryViewList, ViewMedium = AppSettings.HistoryViewMedium, ViewLarge = AppSettings.HistoryViewLarge;

        static string TimeText(QuoteRecord r) { return r.Hours + L.S(" h ", " t ") + r.Minutes + " m"; }

        static string ShortDate(QuoteRecord r)
        {
            DateTime t;
            return History.TryTime(r.SavedAt, out t) ? t.ToString("dd-MM-yyyy", Num.Dk) : (r.SavedAt ?? "");
        }

        // ---------- pictures: loaded only when shown, scaled once, kept in a capped cache ----------
        /// The record pictures (History pictures folder) at the sizes the window needs. Get never waits: the first call
        /// queues a load on one background task and returns null (placeholder); Loaded(id) fires on the UI thread when
        /// it is there. Least recently used pictures go once ~48 MB of scaled pictures are kept. Picture mode (offline):
        /// made-up pictures from memory, scaled at once, the user's folder is never read.
        internal sealed class PictureCache : IDisposable
        {
            sealed class Entry { public string Key, Id; public Image Img; public long Bytes; }

            const long MaxBytes = 48L << 20;
            readonly Control owner;
            readonly Dictionary<string, byte[]> samples;   // offline only
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            readonly Dictionary<string, LinkedListNode<Entry>> map = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
            readonly LinkedList<Entry> lru = new LinkedList<Entry>();
            long bytes;
            readonly object gate = new object();
            readonly List<string> queue = new List<string>();
            readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
            bool running, disposed;

            /// Fires on the UI thread when a picture for this record id has loaded (or turned out unreadable).
            public event Action<string> Loaded;

            public PictureCache(Control owner, Dictionary<string, byte[]> samples)
            {
                this.owner = owner;
                this.samples = samples;
                Reload();
            }

            bool Offline { get { return samples != null; } }

            /// Reads which records have a picture file again (one folder scan).
            public void Reload()
            {
                if (Offline) ids = new HashSet<string>(samples.Keys, StringComparer.Ordinal);
                else { try { ids = History.PictureIdSet(); } catch { ids = new HashSet<string>(StringComparer.Ordinal); } }
            }

            public bool Has(QuoteRecord r) { return r != null && !string.IsNullOrEmpty(r.Id) && ids.Contains(r.Id); }

            /// The record's picture, longest side px (never enlarged), or null (none, or not loaded yet).
            public Image Get(QuoteRecord r, int px)
            {
                if (disposed || !Has(r) || px < 1) return null;
                string key = r.Id + "|" + px;
                LinkedListNode<Entry> n;
                if (map.TryGetValue(key, out n)) { lru.Remove(n); lru.AddFirst(n); return n.Value.Img; }
                if (Offline)
                {
                    byte[] b;
                    Image img = samples.TryGetValue(r.Id, out b) ? History.PictureThumbnail(b, px) : null;
                    if (img == null) { ids.Remove(r.Id); return null; }
                    Add(key, r.Id, img);
                    return img;
                }
                Request(key);
                return null;
            }

            void Request(string key)
            {
                lock (gate)
                {
                    if (!pending.Add(key)) return;
                    queue.Add(key);
                    if (queue.Count > 300) { pending.Remove(queue[0]); queue.RemoveAt(0); }   // scrolled past long ago
                    if (running) return;
                    running = true;
                }
                Task.Run(new Action(Work));
            }

            /// One background task: newest request first (what is on screen now), read + decode + scale, back to the UI thread.
            void Work()
            {
                while (true)
                {
                    string key;
                    lock (gate)
                    {
                        if (disposed || queue.Count == 0) { running = false; return; }
                        key = queue[queue.Count - 1];
                        queue.RemoveAt(queue.Count - 1);
                    }
                    int bar = key.LastIndexOf('|');
                    string id = key.Substring(0, bar);
                    int px = int.Parse(key.Substring(bar + 1), System.Globalization.CultureInfo.InvariantCulture);
                    Image img = null;
                    try { img = History.PictureThumbnail(History.LoadPicture(id), px); }
                    catch { img = null; }
                    try { owner.BeginInvoke((MethodInvoker)delegate { Done(key, id, img); }); }
                    catch
                    {
                        if (img != null) img.Dispose();   // the window is gone
                        lock (gate) { running = false; }
                        return;
                    }
                }
            }

            void Done(string key, string id, Image img)
            {
                lock (gate) pending.Remove(key);
                if (disposed) { if (img != null) img.Dispose(); return; }
                if (img == null) ids.Remove(id);   // unreadable (e.g. a website WebP): the placeholder from now on
                else if (!map.ContainsKey(key)) Add(key, id, img);
                else img.Dispose();
                if (Loaded != null) Loaded(id);
            }

            void Add(string key, string id, Image img)
            {
                var e = new Entry { Key = key, Id = id, Img = img, Bytes = 4L * img.Width * img.Height };
                map[key] = lru.AddFirst(e);
                bytes += e.Bytes;
                while (bytes > MaxBytes && lru.Count > 1) Drop(lru.Last);
            }

            void Drop(LinkedListNode<Entry> n)
            {
                lru.Remove(n);
                map.Remove(n.Value.Key);
                bytes -= n.Value.Bytes;
                n.Value.Img.Dispose();
            }

            /// Deleted records: their pictures leave the cache (and count as none).
            public void Forget(IEnumerable<string> gone)
            {
                var set = new HashSet<string>(gone, StringComparer.Ordinal);
                ids.ExceptWith(set);
                var node = lru.First;
                while (node != null)
                {
                    var next = node.Next;
                    if (set.Contains(node.Value.Id)) Drop(node);
                    node = next;
                }
            }

            public void Clear()
            {
                while (lru.Count > 0) Drop(lru.Last);
                ids.Clear();
            }

            public void Dispose()
            {
                lock (gate) { disposed = true; queue.Clear(); pending.Clear(); }
                while (lru.Count > 0) Drop(lru.Last);
            }
        }

        /// A picture well: Back fill (+ 1 px Border frame when framed), the whole picture centred inside a 5 % margin
        /// (at least 2 px) at its own size (already scaled), or a muted cube outline when there is none.
        internal static void DrawWell(Graphics g, Rectangle well, Image img, bool framed, float sc)
        {
            using (var b = new SolidBrush(Theme.Back)) g.FillRectangle(b, well);
            if (framed) using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, well.X, well.Y, well.Width - 1, well.Height - 1);
            if (img != null)
            {
                var inner = WellInner(well, framed, sc);
                double k = Math.Min(1.0, Math.Min(inner.Width / (double)img.Width, inner.Height / (double)img.Height));
                int w = Math.Max(1, (int)Math.Round(img.Width * k)), h = Math.Max(1, (int)Math.Round(img.Height * k));
                var state = g.Save();
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(img, new Rectangle(inner.X + (inner.Width - w) / 2, inner.Y + (inner.Height - h) / 2, w, h));
                g.Restore(state);
                return;
            }
            // placeholder: an isometric cube outline, half the well's short side
            float side = Math.Min(well.Width, well.Height) * 0.5f, r = side / 2f;
            float cx = well.X + well.Width / 2f, cy = well.Y + well.Height / 2f;
            float dx = r * 0.866f, dy = r * 0.5f;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Theme.Muted, Math.Max(1f, 1.4f * sc)) { LineJoin = LineJoin.Round })
            {
                var top = new PointF(cx, cy - r);
                var rt = new PointF(cx + dx, cy - dy);
                var rb = new PointF(cx + dx, cy + dy);
                var bottom = new PointF(cx, cy + r);
                var lb = new PointF(cx - dx, cy + dy);
                var lt = new PointF(cx - dx, cy - dy);
                var mid = new PointF(cx, cy);
                g.DrawPolygon(p, new[] { top, rt, rb, bottom, lb, lt });
                g.DrawLines(p, new[] { lt, mid, rt });
                g.DrawLine(p, mid, bottom);
            }
            g.Restore(st);
        }

        /// The area a picture may use inside a well (5 % margin, at least 2 px, inside the frame).
        internal static Rectangle WellInner(Rectangle well, bool framed, float sc)
        {
            int m = Math.Max((int)Math.Round(2 * sc), (int)Math.Round(Math.Min(well.Width, well.Height) * 0.05)) + (framed ? 1 : 0);
            return new Rectangle(well.X + m, well.Y + m, Math.Max(1, well.Width - 2 * m), Math.Max(1, well.Height - 2 * m));
        }

        // ---------- the List / Medium / Large switch ----------
        /// Three 30 px cells with drawn glyphs (list, 2x2, one big picture) in one Border frame on ButtonBack; the chosen
        /// one InfoAccent with a white glyph, hover ButtonHover + Text glyph, others Muted. One Tab stop: Left/Right/Home/End
        /// switch; dotted Text focus rectangle round the chosen cell with keyboard cues.
        internal sealed class ViewSwitch : Control
        {
            int value, hot = -1;
            public event Action Changed;
            /// The hovered cell changed (-1 = none): the window updates the dark tooltip.
            public event Action<int> HotChanged;

            public ViewSwitch()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                AccessibleRole = AccessibleRole.PageTabList;
                Size = new Size(94, 25);
            }

            public static string ViewName(int v)
            {
                return v == ViewMedium ? L.S("Medium pictures", "Mellemstore billeder")
                     : v == ViewLarge ? L.S("Large pictures", "Store billeder") : L.S("List", "Liste");
            }

            public int Value
            {
                get { return value; }
                set
                {
                    int v = Math.Max(0, Math.Min(2, value));
                    if (v == this.value) return;
                    this.value = v;
                    AccessibleName = L.S("View: ", "Visning: ") + ViewName(v);
                    Invalidate();
                }
            }

            void Choose(int v)
            {
                v = Math.Max(0, Math.Min(2, v));
                if (v == value) return;
                Value = v;
                if (Changed != null) Changed();
            }

            Rectangle Cell(int i)
            {
                int cw = (Width - 4) / 3;   // frame 1 + 2 separators + frame 1
                int x = 1 + i * (cw + 1);
                return new Rectangle(x, 1, i == 2 ? Width - 1 - x : cw, Height - 2);
            }

            int HitCell(Point p)
            {
                for (int i = 0; i < 3; i++) if (Cell(i).Contains(p)) return i;
                return -1;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                float sc = DeviceDpi / 96f;
                g.Clear(Theme.ButtonBack);
                using (var sep = new Pen(Theme.Border))
                {
                    g.DrawRectangle(sep, 0, 0, Width - 1, Height - 1);
                    for (int i = 0; i < 2; i++) { var c = Cell(i); g.DrawLine(sep, c.Right, 1, c.Right, Height - 2); }
                }
                for (int i = 0; i < 3; i++)
                {
                    var c = Cell(i);
                    Color glyph = Theme.Muted;
                    if (i == value) { using (var b = new SolidBrush(Theme.InfoAccent)) g.FillRectangle(b, c); glyph = Color.White; }
                    else if (i == hot && Enabled) { using (var b = new SolidBrush(Theme.ButtonHover)) g.FillRectangle(b, c); glyph = Theme.Text; }
                    DrawGlyph(g, i, c, glyph, sc);
                }
                if (Focused && ShowFocusCues)
                {
                    var c = Cell(value);
                    using (var p = new Pen(Theme.Text) { DashStyle = DashStyle.Dot })
                        g.DrawRectangle(p, c.X + 2, c.Y + 2, c.Width - 5, c.Height - 5);
                }
            }

            static void DrawGlyph(Graphics g, int kind, Rectangle cell, Color colour, float sc)
            {
                float s = 16 * sc, u = s / 16f;
                float x = cell.X + (cell.Width - s) / 2f, y = cell.Y + (cell.Height - s) / 2f;
                Func<float, float, float, float, RectangleF> R = (a, b, w, h) => new RectangleF(x + a * u, y + b * u, w * u, h * u);
                RectangleF[] parts;
                if (kind == 0) parts = new[] { R(1, 2, 3, 3), R(6, 3, 9, 1.5f), R(1, 6.5f, 3, 3), R(6, 7.25f, 9, 1.5f), R(1, 11, 3, 3), R(6, 11.75f, 9, 1.5f) };
                else if (kind == 1) parts = new[] { R(1.5f, 1.5f, 5.5f, 5.5f), R(9, 1.5f, 5.5f, 5.5f), R(1.5f, 9, 5.5f, 5.5f), R(9, 9, 5.5f, 5.5f) };
                else parts = new[] { R(1.5f, 1.5f, 13, 9.5f), R(1.5f, 12.5f, 9, 1.75f) };
                using (var b = new SolidBrush(colour)) g.FillRectangles(b, parts);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int h = HitCell(e.Location);
                if (h != hot) { hot = h; Invalidate(); if (HotChanged != null) HotChanged(h); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (hot != -1) { hot = -1; Invalidate(); if (HotChanged != null) HotChanged(-1); }
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { int h = HitCell(e.Location); if (h >= 0) Choose(h); }
                base.OnMouseDown(e);
            }

            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Left: case Keys.Right: case Keys.Home: case Keys.End: return (keyData & (Keys.Control | Keys.Alt)) == 0;
                }
                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.Modifiers == Keys.None)
                {
                    if (e.KeyCode == Keys.Left) { Choose(value - 1); e.Handled = true; }
                    else if (e.KeyCode == Keys.Right) { Choose(value + 1); e.Handled = true; }
                    else if (e.KeyCode == Keys.Home) { Choose(0); e.Handled = true; }
                    else if (e.KeyCode == Keys.End) { Choose(2); e.Handled = true; }
                }
                base.OnKeyDown(e);
            }

            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        }

        // ---------- the picture cards (Medium / Large) ----------
        /// Hand-drawn cards for the shown quotes; draws only the visible ones. Medium: 96 px pictures, Large: 160 px; as
        /// many per row as fit (at least 168 / 204 px each). InfoBack card with a Border frame, hover HotBack + HotBorder,
        /// selected InfoAccent + AccentBorder with white text; the picture well stays Back. Slim red scroll bar in a 6 + 8 px
        /// gutter (only when it scrolls), wheel 60 px. Several can be selected (Ctrl / Shift + click or keys, Ctrl+A).
        internal sealed class TileView : Control
        {
            List<QuoteRecord> items = new List<QuoteRecord>();
            readonly HashSet<QuoteRecord> sel = new HashSet<QuoteRecord>();
            int current = -1, anchor = -1, hot = -1, scroll, mode = ViewMedium;
            bool barHot, dragging;
            int dragY, dragScroll;
            public PictureCache Pictures;
            readonly Font small = new Font("Segoe UI", 9f), nameM = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                          nameL = new Font("Segoe UI", 10f, FontStyle.Bold);

            public event Action SelectionChanged, OpenRequested, DeleteRequested;
            public event Action<Point> MenuRequested;   // screen point
            public event Action<int> ViewWheel;         // Ctrl + wheel: +1 = bigger, -1 = smaller
            /// A cut name / line is under the mouse (text, client point for the tooltip); null = none (hide).
            public event Action<string, Point> TipChanged;
            string shownTip;

            public TileView()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                AccessibleRole = AccessibleRole.List;
                AccessibleName = L.S("Saved quotes", "Gemte tilbud");
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { small.Dispose(); nameM.Dispose(); nameL.Dispose(); }
                base.Dispose(disposing);
            }

            int P(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }
            float Sc { get { return DeviceDpi / 96f; } }
            Font NameFont { get { return mode == ViewLarge ? nameL : nameM; } }
            int Pad { get { return P(8); } }
            int Gap { get { return P(8); } }
            // right room: 8 px like the left without a scroll bar; with one, 8 px card -> bar (6) -> 8 px edge = 22
            int RightRoom { get { int c = ColsFor(P(8)); return ContentHFor(c) > Height ? P(22) : P(8); } }
            int BarW { get { return P(6); } }
            int BarX { get { return Width - P(8) - BarW; } }
            int MinCardW { get { return mode == ViewLarge ? P(204) : P(168); } }
            int WellH { get { return mode == ViewLarge ? P(160) : P(96); } }
            int NameH { get { return TextRenderer.MeasureText("Ag", NameFont).Height + P(1); } }
            int LineH { get { return Math.Max(TextRenderer.MeasureText("Ag", small).Height, TextRenderer.MeasureText("Ag", NameFont).Height) + P(1); } }
            int Lines { get { return mode == ViewLarge ? 3 : 2; } }
            int CardH { get { return P(7) + WellH + P(6) + NameH + Lines * LineH + P(6); } }
            int ColsFor(int right) { return Math.Max(1, (Width - Pad - right + Gap) / (MinCardW + Gap)); }
            int ContentHFor(int cols) { return items.Count == 0 ? 0 : Pad + ((items.Count + cols - 1) / cols) * (CardH + Gap) - Gap + Pad; }
            int Cols { get { return ColsFor(RightRoom); } }
            int CardW { get { int c = Cols; return Math.Max(P(60), (Width - Pad - RightRoom - (c - 1) * Gap) / c); } }
            int Rows { get { return (items.Count + Cols - 1) / Cols; } }
            int ContentH { get { return ContentHFor(Cols); } }
            int MaxScroll { get { return Math.Max(0, ContentH - Height); } }

            Rectangle CardRect(int i)
            {
                int c = Cols;
                return new Rectangle(Pad + (i % c) * (CardW + Gap), Pad + (i / c) * (CardH + Gap) - scroll, CardW, CardH);
            }

            public int Mode
            {
                get { return mode; }
                set { mode = value == ViewLarge ? ViewLarge : ViewMedium; SetScroll(scroll); if (current >= 0) ScrollIntoView(current); Invalidate(); }
            }

            public int Count { get { return items.Count; } }
            public int SelectedCount { get { return sel.Count; } }
            public QuoteRecord Current { get { return current >= 0 && current < items.Count ? items[current] : null; } }

            /// The selected records in list order.
            public List<QuoteRecord> SelectedRecords()
            {
                var res = new List<QuoteRecord>();
                if (sel.Count == 0) return res;
                foreach (var r in items) if (sel.Contains(r)) res.Add(r);
                return res;
            }

            /// New items (Fill); keeps the given selection and current one where they are still shown, else the first item.
            public void SetItems(List<QuoteRecord> list, ICollection<QuoteRecord> keep, QuoteRecord cur)
            {
                items = new List<QuoteRecord>(list);
                sel.Clear();
                var pos = new Dictionary<QuoteRecord, int>();
                for (int i = 0; i < items.Count; i++) pos[items[i]] = i;
                if (keep != null) foreach (var r in keep) if (r != null && pos.ContainsKey(r)) sel.Add(r);
                int ci;
                current = cur != null && pos.TryGetValue(cur, out ci) ? ci : -1;
                if (current < 0) foreach (var r in sel) { int k = pos[r]; if (current < 0 || k < current) current = k; }
                if (current < 0 && items.Count > 0) current = 0;
                if (sel.Count == 0 && current >= 0) sel.Add(items[current]);
                anchor = current;
                hot = -1;
                SetScroll(scroll);
                if (current >= 0) ScrollIntoView(current);
                UpdateAccessible();
                Invalidate();
            }

            /// Selects these (and makes the first current), e.g. after an undo.
            public void Select(ICollection<QuoteRecord> records)
            {
                SetItems(items, records, null);
                if (SelectionChanged != null) SelectionChanged();
            }

            public void SelectAll()
            {
                sel.Clear();
                foreach (var r in items) sel.Add(r);
                Invalidate();
                if (SelectionChanged != null) SelectionChanged();
            }

            void UpdateAccessible()
            {
                var r = Current;
                AccessibleDescription = r == null ? null : ModelText(r) + ", " + Num.Kr(History.PriceFor(r)) + ", " + ShortDate(r);
            }

            void SetScroll(int v)
            {
                v = Math.Max(0, Math.Min(MaxScroll, v));
                if (v == scroll) return;
                scroll = v;
                Invalidate();
            }

            public void ScrollIntoView(int i)
            {
                if (i < 0 || i >= items.Count) return;
                var rc = CardRect(i);
                if (rc.Top < Pad) SetScroll(scroll + rc.Top - Pad);
                else if (rc.Bottom > Height - Pad) SetScroll(scroll + rc.Bottom - Height + Pad);
            }

            int HitTest(Point p)
            {
                if (items.Count == 0 || p.X >= Width - RightRoom + P(2)) return -1;
                int c = Cols, cw = CardW, ch = CardH;
                int col = (p.X - Pad) / (cw + Gap), row = (p.Y + scroll - Pad) / (ch + Gap);
                if (p.X < Pad || p.Y + scroll < Pad || col >= c) return -1;
                int i = row * c + col;
                if (i < 0 || i >= items.Count || !CardRect(i).Contains(p)) return -1;
                return i;
            }

            Rectangle ThumbRect()
            {
                int max = MaxScroll;
                if (max <= 0) return Rectangle.Empty;
                int h = Math.Max(P(24), (int)((long)Height * Height / Math.Max(1, ContentH)));
                int y = (int)((long)(Height - h) * scroll / max);
                return new Rectangle(BarX, y, BarW, h);
            }

            // ---- painting ----
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Input);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;   // grayscale, no ClearType fringes
                if (items.Count == 0)
                {
                    TextRenderer.DrawText(g, L.S("No quotes to show.", "Ingen tilbud at vise."), small, ClientRectangle, Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    return;
                }
                int c = Cols, ch = CardH;
                int firstRow = Math.Max(0, (scroll - Pad) / (ch + Gap));
                for (int i = firstRow * c; i < items.Count; i++)
                {
                    var rc = CardRect(i);
                    if (rc.Top >= Height) break;
                    if (rc.Bottom < 0 || !rc.IntersectsWith(e.ClipRectangle)) continue;
                    DrawCard(g, i, rc);
                }
                var th = ThumbRect();
                if (!th.IsEmpty)
                {
                    using (var b = new SolidBrush(Theme.ScrollTrack)) g.FillRectangle(b, BarX, 0, BarW, Height);
                    using (var b = new SolidBrush(barHot || dragging ? Theme.Others : Theme.InfoAccent)) g.FillRectangle(b, th);
                }
            }

            // Card text is drawn with GDI+ grayscale anti-aliasing (like Theme.PlainLabel): ClearType's colour fringes
            // showed round the letters on the red selected cards. Cut texts are remembered per card for the tooltip.
            readonly Dictionary<QuoteRecord, List<KeyValuePair<Rectangle, string>>> cutTexts =
                new Dictionary<QuoteRecord, List<KeyValuePair<Rectangle, string>>>();
            static readonly StringFormat measureFormat = MakeFormat(false, false);

            static StringFormat MakeFormat(bool right, bool ellipsis)
            {
                var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
                sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.LineLimit | StringFormatFlags.MeasureTrailingSpaces;
                sf.Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None;
                sf.Alignment = right ? StringAlignment.Far : StringAlignment.Near;
                sf.HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None;
                return sf;
            }

            static int TextW(Graphics g, string s, Font f)
            {
                return string.IsNullOrEmpty(s) ? 0 : (int)Math.Ceiling(g.MeasureString(s, f, PointF.Empty, measureFormat).Width);
            }

            /// One line, cut with "…" when too long (then remembered for the dark tooltip, in content coordinates).
            void DrawText(Graphics g, QuoteRecord r, string s, Font f, Rectangle rc, Color c, bool right)
            {
                if (string.IsNullOrEmpty(s) || rc.Width <= 0) return;
                using (var sf = MakeFormat(right, true))
                using (var b = new SolidBrush(c))
                    g.DrawString(s, f, b, new RectangleF(rc.X, rc.Y, rc.Width, rc.Height), sf);
                if (r != null && TextW(g, s, f) > rc.Width)
                {
                    List<KeyValuePair<Rectangle, string>> cuts;
                    if (!cutTexts.TryGetValue(r, out cuts)) cutTexts[r] = cuts = new List<KeyValuePair<Rectangle, string>>();
                    cuts.Add(new KeyValuePair<Rectangle, string>(new Rectangle(rc.X, rc.Y + scroll, rc.Width, rc.Height), s));
                }
            }

            void DrawCard(Graphics g, int i, Rectangle rc)
            {
                var r = items[i];
                cutTexts.Remove(r);
                bool s = sel.Contains(r), h = i == hot && !s;
                using (var b = new SolidBrush(s ? Theme.InfoAccent : h ? Theme.HotBack : Theme.InfoBack)) g.FillRectangle(b, rc);
                using (var p = new Pen(s ? Theme.AccentBorder : h ? Theme.HotBorder : Theme.Border)) g.DrawRectangle(p, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);
                var well = new Rectangle(rc.X + P(7), rc.Y + P(7), rc.Width - P(14), WellH);
                var inner = WellInner(well, false, Sc);
                Image img = Pictures == null ? null : Pictures.Get(r, Math.Min(inner.Width, inner.Height));
                DrawWell(g, well, img, false, Sc);

                bool friends = r.For == History.ForFriends;
                Color main = s ? Color.White : Theme.Text, muted = s ? Theme.Text : Theme.Muted;
                int x = rc.X + P(8), w = rc.Width - P(16), y = well.Bottom + P(6);
                DrawText(g, r, ModelText(r), NameFont, new Rectangle(x, y, w, NameH), main, false);
                y += NameH;
                // price (Friends / Others, bold) + date (muted, right)
                string date = ShortDate(r);
                int dw = TextW(g, date, small);
                DrawText(g, null, date, small, new Rectangle(x + w - dw, y + P(1), dw + 1, LineH), muted, true);
                DrawText(g, null, Num.Kr(History.PriceFor(r)), NameFont, new Rectangle(x, y, w - dw - P(6), LineH),
                     s ? Color.White : friends ? Theme.Friends : Theme.Others, false);
                y += LineH;
                // time · grams (muted) + status (medium) / colour dots (large)
                string facts = TimeText(r) + " · " + Num.Fmt(r.Grams) + " g";
                int rightW = mode == ViewLarge ? DrawDots(g, r, x + w, y + LineH / 2) : DrawStatus(g, r, false, x + w, y, s);
                DrawText(g, r, facts, small, new Rectangle(x, y + P(1), w - rightW - (rightW > 0 ? P(6) : 0), LineH), muted, false);
                if (mode == ViewLarge)
                {
                    y += LineH;
                    int sw = DrawStatus(g, r, true, x + w, y, s);
                    string note = !string.IsNullOrEmpty(r.Note) ? r.Note
                                : friends ? L.S("Friends & family", "Venner og familie") : L.S("Customer", "Kunde");
                    DrawText(g, r, note, small, new Rectangle(x, y + P(1), w - sw - (sw > 0 ? P(6) : 0), LineH), muted, false);
                }
                if (i == current && Focused)
                    using (var p = new Pen(s ? Color.White : Theme.Text) { DashStyle = DashStyle.Dot })
                        g.DrawRectangle(p, rc.X + P(4), rc.Y + P(4), rc.Width - 1 - P(8), rc.Height - 1 - P(8));
            }

            /// Right-aligned status ending at right: ✓ printed (Success; white on a selected card), ✗ failed (Error; white
            /// on a selected card); long = with words and the invoice code (Large). Returns the width used.
            int DrawStatus(Graphics g, QuoteRecord r, bool longForm, int right, int y, bool selected)
            {
                string text; Color colour;
                if (r.Failed) { text = longForm ? L.S("✗ Failed", "✗ Fejlet") : "✗"; colour = selected ? Color.White : Theme.Error; }
                else if (r.Printed) { text = "✓"; colour = selected ? Color.White : Theme.Success; }
                else if (longForm) { text = L.S("Not printed", "Ikke printet"); colour = selected ? Theme.Text : Theme.Muted; }
                else return 0;
                int tw = TextW(g, text, small);
                DrawText(g, null, text, small, new Rectangle(right - tw, y + P(1), tw + 1, LineH), colour, true);
                int used = tw;
                if (longForm && !string.IsNullOrEmpty(r.InvoiceNo))
                {
                    string inv = r.InvoiceNo;
                    int iw = TextW(g, inv, small);
                    DrawText(g, null, inv, small, new Rectangle(right - tw - P(6) - iw, y + P(1), iw + 1, LineH), selected ? Theme.Text : Theme.Muted, true);
                    used += P(6) + iw;
                }
                return used;
            }

            /// The cut text under the mouse (name, facts or note of a card), or null.
            string CutTextAt(Point p)
            {
                int i = HitTest(p);
                if (i < 0) return null;
                List<KeyValuePair<Rectangle, string>> cuts;
                if (!cutTexts.TryGetValue(items[i], out cuts)) return null;
                var at = new Point(p.X, p.Y + scroll);
                foreach (var c in cuts) if (c.Key.Contains(at)) return c.Value;
                return null;
            }

            /// The colours (up to 8 dots, 10 px, Muted ring so black shows) ending at right, centred on cy. Returns the width.
            int DrawDots(Graphics g, QuoteRecord r, int right, int cy)
            {
                if (r.Filaments == null) return 0;
                var cols = new List<Color>();
                foreach (var f in r.Filaments)
                {
                    if (f == null || string.IsNullOrEmpty(f.Colour)) continue;
                    try { cols.Add(ColorTranslator.FromHtml(f.Colour.Length == 9 ? "#" + f.Colour.Substring(1, 6) : f.Colour)); }
                    catch { }
                    if (cols.Count == 8) break;
                }
                if (cols.Count == 0) return 0;
                int d = P(10), gap = P(3), wAll = cols.Count * d + (cols.Count - 1) * gap;
                var st = g.Save();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int k = 0; k < cols.Count; k++)
                {
                    var rc = new Rectangle(right - wAll + k * (d + gap), cy - d / 2, d, d);
                    using (var b = new SolidBrush(cols[k])) g.FillEllipse(b, rc);
                    using (var p = new Pen(Theme.Muted)) g.DrawEllipse(p, rc);
                }
                g.Restore(st);
                return wAll;
            }

            // ---- mouse ----
            protected override void OnMouseDown(MouseEventArgs e)
            {
                Focus();
                var th = ThumbRect();
                if (e.Button == MouseButtons.Left && !th.IsEmpty && e.X >= BarX - P(4))
                {
                    if (th.Contains(new Point(BarX, e.Y))) { dragging = true; dragY = e.Y; dragScroll = scroll; Capture = true; Invalidate(); }
                    else SetScroll(scroll + (e.Y < th.Top ? -Height : Height) + (e.Y < th.Top ? P(40) : -P(40)));
                    return;
                }
                int i = HitTest(e.Location);
                if (e.Button == MouseButtons.Left && i >= 0)
                {
                    var mods = ModifierKeys;
                    if ((mods & Keys.Shift) != 0 && anchor >= 0) SelectRange(anchor, i, (mods & Keys.Control) != 0);
                    else if ((mods & Keys.Control) != 0) { if (!sel.Remove(items[i])) sel.Add(items[i]); anchor = i; }
                    else { sel.Clear(); sel.Add(items[i]); anchor = i; }
                    current = i;
                    Changed();
                }
                else if (e.Button == MouseButtons.Right && i >= 0)
                {
                    if (!sel.Contains(items[i])) { sel.Clear(); sel.Add(items[i]); anchor = i; }
                    current = i;
                    Changed();
                    if (MenuRequested != null) MenuRequested(PointToScreen(e.Location));
                }
                base.OnMouseDown(e);
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && HitTest(e.Location) >= 0 && OpenRequested != null) OpenRequested();
                base.OnMouseDoubleClick(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (dragging)
                {
                    var th = ThumbRect();
                    int room = Height - th.Height;
                    if (room > 0) SetScroll(dragScroll + (int)((long)(e.Y - dragY) * MaxScroll / room));
                    return;
                }
                bool bh = !ThumbRect().IsEmpty && e.X >= BarX - P(4);
                int h = bh ? -1 : HitTest(e.Location);
                if (h != hot || bh != barHot) { hot = h; barHot = bh; Invalidate(); }
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                string tip = bh ? null : CutTextAt(e.Location);
                if (tip != shownTip) { shownTip = tip; if (TipChanged != null) TipChanged(tip, new Point(e.X, e.Y + P(20))); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (dragging) { dragging = false; Capture = false; Invalidate(); }
                base.OnMouseUp(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (hot != -1 || barHot) { hot = -1; barHot = false; Invalidate(); }
                if (shownTip != null) { shownTip = null; if (TipChanged != null) TipChanged(null, Point.Empty); }
                base.OnMouseLeave(e);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if ((ModifierKeys & Keys.Control) != 0) { if (ViewWheel != null) ViewWheel(e.Delta > 0 ? 1 : -1); }
                else SetScroll(scroll - e.Delta * P(60) / 120);
                var he = e as HandledMouseEventArgs;
                if (he != null) he.Handled = true;
                base.OnMouseWheel(e);
            }

            // ---- keys ----
            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                    case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown: return true;
                }
                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.Handled || items.Count == 0) return;
                int c = Cols, n = items.Count, at = current < 0 ? 0 : current, to = -1;
                int page = Math.Max(1, Height / (CardH + Gap)) * c;
                switch (e.KeyCode)
                {
                    case Keys.Left: to = at - 1; break;
                    case Keys.Right: to = at + 1; break;
                    case Keys.Up: to = at - c; break;
                    case Keys.Down: to = at + c; break;
                    case Keys.Home: to = 0; break;
                    case Keys.End: to = n - 1; break;
                    case Keys.PageUp: to = at - page; break;
                    case Keys.PageDown: to = at + page; break;
                    case Keys.Delete:
                        if (e.Modifiers == Keys.None && DeleteRequested != null) { e.Handled = true; DeleteRequested(); }
                        return;
                    case Keys.Space:
                        e.Handled = true;
                        if (e.Control) { if (!sel.Remove(items[at])) sel.Add(items[at]); }
                        else { sel.Clear(); sel.Add(items[at]); }
                        anchor = current = at;
                        Changed();
                        return;
                    case Keys.A:
                        if (e.Modifiers == Keys.Control) { e.Handled = true; SelectAll(); }
                        return;
                    case Keys.Apps:
                    case Keys.F10:
                        if (e.KeyCode == Keys.F10 && !e.Shift) return;
                        e.Handled = true;
                        if (current >= 0 && MenuRequested != null)
                        {
                            if (!sel.Contains(items[current])) { sel.Clear(); sel.Add(items[current]); Changed(); }
                            var rc = CardRect(current);
                            MenuRequested(PointToScreen(new Point(rc.Left + rc.Width / 3, Math.Min(Height - 1, rc.Bottom))));
                        }
                        return;
                    default: return;
                }
                to = Math.Max(0, Math.Min(n - 1, to));
                e.Handled = true;
                if (e.Shift) SelectRange(anchor < 0 ? at : anchor, to, false);
                else if (!e.Control) { sel.Clear(); sel.Add(items[to]); anchor = to; }
                current = to;
                ScrollIntoView(to);
                Changed();
            }

            void SelectRange(int a, int b, bool add)
            {
                if (!add) sel.Clear();
                for (int k = Math.Min(a, b); k <= Math.Max(a, b); k++) sel.Add(items[k]);
            }

            void Changed()
            {
                UpdateAccessible();
                Invalidate();
                if (SelectionChanged != null) SelectionChanged();
            }

            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
            protected override void OnResize(EventArgs e) { SetScroll(scroll); base.OnResize(e); }
        }

        // ---------- picture mode ----------
        /// Selects these shown quotes (indexes in the shown list; the first one current).
        internal void SampleSelect(int[] indexes)
        {
            var keep = new List<QuoteRecord>();
            foreach (int i in indexes) if (i >= 0 && i < shown.Count) keep.Add(shown[i]);
            Fill(keep, keep.Count > 0 ? keep[0] : null);
        }

        internal List<QuoteRecord> SampleSelected() { return SelectedRecords(); }

        /// Made-up 256 px pictures for SampleQuotes (PNG bytes by record id; "sample5", the failed hook, has none).
        internal static Dictionary<string, byte[]> SamplePictures()
        {
            var res = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            for (int i = 0; i < 8; i++)
            {
                if (i == 5) continue;
                using (var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        DrawSample(g, i);
                    }
                    using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); res["sample" + i] = ms.ToArray(); }
                }
            }
            return res;
        }

        static void DrawSample(Graphics g, int i)
        {
            Func<int, int, int, Brush> B = (r, gg, b) => new SolidBrush(Color.FromArgb(r, gg, b));
            switch (i)
            {
                case 0:   // a boat, orange
                case 1:   // the same in four colours
                    using (var hull = i == 0 ? B(224, 84, 42) : B(42, 123, 214)) g.FillPolygon(hull, new[] { new Point(30, 160), new Point(226, 160), new Point(200, 212), new Point(56, 212) });
                    using (var cab = i == 0 ? B(232, 101, 58) : B(232, 195, 58)) g.FillRectangle(cab, 92, 102, 78, 58);
                    using (var win = i == 0 ? B(163, 58, 28) : B(214, 58, 58)) { g.FillRectangle(win, 106, 118, 20, 20); g.FillRectangle(win, 138, 118, 20, 20); }
                    using (var st = i == 0 ? B(201, 74, 36) : B(238, 238, 238)) g.FillRectangle(st, 148, 70, 20, 32);
                    break;
                case 2:   // phone stand
                    using (var a = B(58, 58, 60)) g.FillPolygon(a, new[] { new Point(38, 204), new Point(218, 204), new Point(218, 184), new Point(102, 56), new Point(76, 56), new Point(76, 184), new Point(38, 184) });
                    using (var b = B(85, 85, 88)) g.FillPolygon(b, new[] { new Point(76, 56), new Point(102, 56), new Point(218, 184), new Point(192, 184) });
                    using (var c = B(38, 38, 40)) g.FillRectangle(c, 38, 204, 180, 14);
                    break;
                case 3:   // cable clips
                    using (var p = new Pen(Color.FromArgb(240, 240, 240), 18))
                        for (int k = 0; k < 3; k++) g.DrawArc(p, 40 + k * 64, 104, 48, 48, 150, 240);
                    using (var b = B(207, 207, 207)) g.FillRectangle(b, 36, 168, 184, 12);
                    break;
                case 4:   // planter
                    using (var leaf = B(58, 138, 74))
                    {
                        g.FillEllipse(leaf, 60, 40, 40, 90); g.FillEllipse(leaf, 110, 24, 36, 100); g.FillEllipse(leaf, 156, 44, 40, 86);
                    }
                    using (var pot = B(192, 96, 58)) g.FillPolygon(pot, new[] { new Point(60, 118), new Point(196, 118), new Point(176, 222), new Point(80, 222) });
                    using (var rim = B(216, 116, 76)) g.FillRectangle(rim, 54, 112, 148, 20);
                    break;
                case 6:   // dragon
                    using (var p = new Pen(Color.FromArgb(42, 123, 214), 30) { StartCap = LineCap.Round, EndCap = LineCap.Round, DashPattern = new[] { 0.6f, 0.15f } })
                        g.DrawBezier(p, 36, 180, 90, 90, 150, 220, 196, 110);
                    using (var head = B(232, 195, 58)) g.FillEllipse(head, 182, 78, 50, 50);
                    using (var eye = B(214, 58, 58)) g.FillEllipse(eye, 208, 92, 10, 10);
                    break;
                case 7:   // lithophane lamp
                    using (var a = B(244, 241, 232)) g.FillRectangle(a, 78, 40, 100, 160);
                    using (var b = B(217, 210, 191)) g.FillRectangle(b, 88, 60, 80, 100);
                    using (var c = B(200, 191, 168)) g.FillEllipse(c, 104, 84, 48, 48);
                    using (var d = B(155, 148, 136)) g.FillRectangle(d, 66, 200, 124, 20);
                    break;
            }
        }
    }
}
