using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintPrice
{
    /// "Choose printer" (the main window's printer card): one tile per printer with its picture, name, model, nozzle,
    /// plate, wear rate, power and this month's prints / profit, then an "Add a printer…" tile. Up to 3 tiles a row;
    /// more rows than fit (2, or 1 on a low screen) scroll (slim red bar, wheel, keys). Click / Enter picks a printer.
    public class PrinterPickerForm : CaptionForm
    {
        /// The printer picked (DialogResult.OK, not Add / Settings).
        public string ChosenId;
        /// Closed with the "Add a printer…" tile: open Settings > Printer with its Add menu.
        public bool AddRequested;
        /// Closed with "Printer settings…": open Settings > Printer showing SettingsPrinterId.
        public bool SettingsRequested;
        public string SettingsPrinterId;

        readonly TileView view;
        readonly InfoBox info;
        readonly Button btnAdd, btnSettings, btnClose;
        readonly TableLayoutPanel bottom;
        readonly List<PrinterProfile> printers;

        public PrinterPickerForm(AppSettings settings, List<QuoteRecord> history, DateTime now)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = L.S("Choose printer", "Vælg printer");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            if (Program.Hidden || Screenshots.Active) Screenshots.Place(this);   // CenterParent would pull it back on screen
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            printers = settings.Printers;
            string exe = BambuSlicer.FindExe(settings.SlicerPath);
            var totals = new Dictionary<string, PrinterTotals>();
            foreach (var t in History.PrinterTotals(history, settings, now.Year, now.Month)) totals[t.Id] = t;

            var tiles = new List<Tile>();
            foreach (var p in printers)
            {
                PrinterTotals t;
                totals.TryGetValue(p.Id, out t);
                tiles.Add(MakeTile(p, p.Id == settings.ActivePrinterId, t, exe));
            }
            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14), Margin = Padding.Empty };
            Controls.Add(root);
            // centred when the bottom row is wider than the tiles (one printer)
            view = new TileView(tiles) { Margin = new Padding(0, 0, 0, 10), TabIndex = 0, Anchor = AnchorStyles.Top };
            view.Picked += Picked;
            root.Controls.Add(view);

            info = new InfoBox(400, 9.5f) { Margin = new Padding(0, 0, 0, 10) };
            root.Controls.Add(info);

            // bottom row: Add printer… + Printer settings… (left), Close (right)
            bottom = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = Padding.Empty, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnAdd = new Button { Text = L.S("Add printer…", "Tilføj printer…"), Size = new Size(110, 32), Padding = Padding.Empty,
                                  Margin = new Padding(0, 0, 6, 0), Anchor = AnchorStyles.Left, UseMnemonic = false, TabIndex = 1 };
            btnAdd.Click += delegate { AddRequested = true; DialogResult = DialogResult.OK; };
            btnSettings = new Button { Text = L.S("Printer settings…", "Printerindstillinger…"), Size = new Size(140, 32), Padding = Padding.Empty,
                                       Margin = Padding.Empty, Anchor = AnchorStyles.Left, UseMnemonic = false, TabIndex = 2 };
            btnSettings.Click += delegate
            {
                var t = view.FocusedTile;
                SettingsPrinterId = t != null ? t.Id : settings.ActivePrinterId;
                SettingsRequested = true;
                DialogResult = DialogResult.OK;
            };
            btnClose = new Button { Text = L.S("Close", "Luk"), Size = new Size(90, 32), Padding = Padding.Empty, Margin = new Padding(12, 0, 0, 0),
                                    Anchor = AnchorStyles.Right, DialogResult = DialogResult.Cancel, TabIndex = 3 };
            bottom.Controls.Add(btnAdd, 0, 0);
            bottom.Controls.Add(btnSettings, 1, 0);
            bottom.Controls.Add(btnClose, 2, 0);
            root.Controls.Add(bottom);

            CancelButton = btnClose;
            Theme.Apply(this);
            ResumeLayout(true);
            Load += delegate { FitLayout(); };
            ActiveControl = view;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override bool ShowWithoutActivation { get { return Screenshots.Active || Program.Hidden; } }

        int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

        /// Load: tile area (rows that fit the screen: 2, or 1 when a 2-row window would be too tall), the help text,
        /// buttons sized by their text, everything as wide as the tiles.
        void FitLayout()
        {
            int screenH = Screen.FromControl(this).WorkingArea.Height;
            // the window without tiles: title strip + padding + info box (2 lines) + gaps + buttons
            int rest = CaptionHeight + Px(14) * 2 + Px(10) * 2 + Px(52) + Px(32) + Px(16);
            int rows = rest + view.AreaHeight(2) <= screenH - Px(20) ? 2 : 1;
            view.Fit(rows);
            btnAdd.Width = TextRenderer.MeasureText(btnAdd.Text, btnAdd.Font).Width + Px(24);
            btnSettings.Width = TextRenderer.MeasureText(btnSettings.Text, btnSettings.Font).Width + Px(24);
            btnClose.Width = Math.Max(Px(90), TextRenderer.MeasureText(btnClose.Text, btnClose.Font).Width + Px(24));
            // content width: the tiles, but at least two tiles and the bottom row's buttons
            int buttons = btnAdd.Width + btnAdd.Margin.Horizontal + btnSettings.Width + btnClose.Width + btnClose.Margin.Horizontal;
            int w = Math.Max(view.Width, Math.Max(view.TwoTilesWidth, buttons));
            info.SetWidth(w);
            info.Text = printers.Count < 2
                ? L.S("Add a second printer (A1, A1 mini or P1S) to choose between them here.",
                      "Tilføj en printer mere (A1, A1 mini eller P1S) for at vælge mellem dem her.")
                : view.Scrolls
                    ? L.S("Click a printer to use it for this quote and the next ones. Arrow keys, Enter and the mouse wheel work too.",
                          "Klik på en printer for at bruge den fra nu af. Piletaster, Enter og musehjulet virker også.")
                    : L.S("Click a printer to use it for this quote and the next ones. Arrow keys + Enter work too.",
                          "Klik på en printer for at bruge den til dette tilbud og de næste. Piletaster og Enter virker også.");
            bottom.MinimumSize = new Size(w, 0);
            view.ShowActive();
            if (Owner != null && !Screenshots.Active && !Program.Hidden)
            {
                // centred on the main window, kept on its screen
                var wa = Screen.FromControl(Owner).WorkingArea;
                int x = Owner.Left + (Owner.Width - Width) / 2, y = Owner.Top + (Owner.Height - Height) / 2;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(Math.Max(wa.Left, Math.Min(x, wa.Right - Width)), Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height)));
            }
        }

        /// Picture mode: scrolls the tiles down by y px (96 dpi).
        internal void SampleScroll(int y)
        {
            view.SampleScroll(y);
        }

        /// Picture mode: draws the keyboard focus on tile i (the window is never active there).
        internal void SampleFocus(int i)
        {
            view.SampleFocus(i);
        }

        void Picked(Tile t)
        {
            if (t == null) return;
            ChosenId = t.Id;
            DialogResult = DialogResult.OK;
        }

        // ---------- tile texts ----------
        static Tile MakeTile(PrinterProfile p, bool active, PrinterTotals t, string exe)
        {
            var model = Wear.FindModel(p.Model);
            var nozzle = Wear.FindNozzle(p.Nozzle);
            string size = L.Danish ? nozzle.Size.Replace('.', ',') : nozzle.Size;
            string noz = nozzle.Hardened ? L.F("{0} mm hardened", "{0} mm hærdet", size) : L.F("{0} mm nozzle", "{0} mm dyse", size);
            int prints = t != null ? t.Prints : 0;
            string printsText = prints == 0 ? L.S("No prints this month", "Ingen print denne måned")
                              : prints == 1 ? L.S("1 print this month", "1 print denne måned")
                              : L.F("{0} prints this month", "{0} print denne måned", prints);
            double profit = t != null ? t.Profit : 0;
            bool showProfit = t != null && (t.Prints > 0 || t.FailedPrints > 0);
            return new Tile
            {
                Id = p.Id, Active = active, Model = model.Key,
                Name = AppSettings.CleanPrinterName(p.Name),
                ModelText = AppSettings.CleanPrinterName(p.Name) == model.Label ? "" : model.Label,
                Line1 = noz + " · " + PlateShort(p.BedType),
                Line2 = L.F("{0} kr/h wear · {1} W", "{0} kr/t slid · {1} W", p.MachineRatePerHour.ToString("0.00", Num.Dk), Num.Fmt(p.Watts)),
                Prints = printsText,
                Profit = showProfit ? L.F("{0} profit", "{0} overskud", Num.Kr(profit)) : "",
                ProfitColour = profit < 0 ? Theme.Error : Theme.Success,
                Picture = Picture(exe, model.Key)
            };
        }

        static string PlateShort(string bed)
        {
            switch (Wear.FindPlate(bed).Key)
            {
                case "Textured PEI Plate": return L.S("Textured PEI", "Tekstur-PEI");
                case "Supertack Plate": return "SuperTack";
                case "Cool Plate": return "Cool Plate";
                case "Engineering Plate": return "Engineering";
                default: return L.S("Smooth PEI", "Glat PEI");
            }
        }

        // Bambu Studio's printer pictures, read once per run (kept for the whole run, like the fonts)
        static readonly Dictionary<string, Bitmap> pictures = new Dictionary<string, Bitmap>();

        static Bitmap Picture(string exe, string model)
        {
            string key = (exe ?? "") + "|" + model;
            Bitmap b;
            if (!pictures.TryGetValue(key, out b)) { b = MainForm.LoadPrinterPhoto(exe, model); pictures[key] = b; }
            return b;
        }

        // ---------- tiles ----------
        internal sealed class Tile
        {
            public string Id, Model, Name, ModelText, Line1, Line2, Prints, Profit;
            public Color ProfitColour;
            public bool Active;
            public Bitmap Picture;   // null = drawn (PrinterArt); shared, not owned
        }

        /// The tiles in rows of up to 3, scrolled by hand (no Windows scroll bar): slim red bar in a gutter on the right.
        sealed class TileView : Control
        {
            const int TileW = 196, TileH = 262, Gap = 8, Cols = 3, BarGap = 6, BarW = 8, WheelStep = 60;
            static readonly Color HotBack = Theme.HotBack, HotBorder = Theme.HotBorder, Track = Theme.ScrollTrack, ThumbHot = Theme.Others;
            readonly List<Tile> tiles;
            readonly Font small = new Font("Segoe UI", 9f), bold = new Font("Segoe UI", 10f, FontStyle.Bold);
            int focus, hover = -1, scroll, visibleRows = 2;
            bool barHot, dragging, sampleFocus;
            readonly ToolTip nameTip = new ToolTip { OwnerDraw = true, InitialDelay = 400 };   // full name of a cut tile name
            int dragOffset;
            public event Action<Tile> Picked;

            public TileView(List<Tile> tiles)
            {
                this.tiles = tiles;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                AccessibleRole = AccessibleRole.List;
                AccessibleName = L.S("Printers", "Printere");
                Size = new Size(TileW, TileH);   // real size on Load (Fit)
                nameTip.Draw += DrawTip;
            }

            static void DrawTip(object sender, DrawToolTipEventArgs e)
            {
                using (var b = new SolidBrush(Theme.InfoBack)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            /// Hover on a tile whose name is cut with …: a dark tooltip with the full name.
            void UpdateNameTip()
            {
                string tip = null;
                if (hover >= 0 && hover < tiles.Count)
                {
                    var t = tiles[hover];
                    if (TextRenderer.MeasureText(t.Name, bold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width > Px(TileW) - 2 * Px(12))
                        tip = t.Name;
                }
                if (nameTip.GetToolTip(this) != (tip ?? "")) nameTip.SetToolTip(this, tip);
            }

            public void SampleFocus(int i)
            {
                if (i < 0 || i >= tiles.Count) return;
                focus = i;
                sampleFocus = true;
                ScrollIntoView(i);
                Invalidate();
            }

            int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }
            int ColCount { get { return Math.Min(Cols, tiles.Count); } }
            int RowCount { get { return (tiles.Count + Cols - 1) / Cols; } }
            int RowStep { get { return Px(TileH) + Px(Gap); } }
            int ContentHeight { get { return RowCount * Px(TileH) + (RowCount - 1) * Px(Gap); } }
            int TilesWidth { get { return ColCount * Px(TileW) + (ColCount - 1) * Px(Gap); } }
            /// Width of two tiles side by side (the window is never narrower).
            public int TwoTilesWidth { get { return 2 * Px(TileW) + Px(Gap); } }
            public bool Scrolls { get { return RowCount > visibleRows; } }
            int MaxScroll { get { return Math.Max(0, ContentHeight - Height); } }

            /// Height of n rows of tiles (device px).
            public int AreaHeight(int rows)
            {
                rows = Math.Max(1, Math.Min(rows, RowCount));
                return rows * Px(TileH) + (rows - 1) * Px(Gap);
            }

            /// Sizes the area for at most `rows` visible rows; a gutter for the scroll bar only when it scrolls.
            public void Fit(int rows)
            {
                visibleRows = Math.Max(1, rows);
                Size = new Size(TilesWidth + (Scrolls ? Px(BarGap) + Px(BarW) : 0), AreaHeight(visibleRows));
                scroll = Math.Min(scroll, MaxScroll);
                Invalidate();
            }

            public void SampleScroll(int y) { ScrollTo(scroll + Px(y)); }

            public Tile FocusedTile { get { return focus >= 0 && focus < tiles.Count ? tiles[focus] : null; } }

            /// Focus on the active printer's tile, scrolled into view.
            public void ShowActive()
            {
                int i = tiles.FindIndex(t => t.Active);
                focus = Math.Max(0, i);
                ScrollIntoView(focus);
                Invalidate();
            }

            Rectangle TileRect(int i)
            {
                int col = i % Cols, row = i / Cols;
                return new Rectangle(col * (Px(TileW) + Px(Gap)), row * RowStep - scroll, Px(TileW), Px(TileH));
            }

            int HitTile(Point p)
            {
                if (p.X >= TilesWidth) return -1;
                for (int i = 0; i < tiles.Count; i++) if (TileRect(i).Contains(p)) return i;
                return -1;
            }

            void ScrollTo(int y)
            {
                y = Math.Max(0, Math.Min(MaxScroll, y));
                if (y == scroll) return;
                scroll = y;
                hover = HitTile(PointToClient(Cursor.Position));
                Invalidate();
            }

            void ScrollIntoView(int i)
            {
                var r = TileRect(i);
                if (r.Top < 0) ScrollTo(scroll + r.Top);
                else if (r.Bottom > Height) ScrollTo(scroll + r.Bottom - Height);
            }

            // ----- scroll bar (gutter right of the tiles) -----
            Rectangle BarRect { get { return new Rectangle(TilesWidth + Px(BarGap), 0, Px(BarW), Height); } }

            Rectangle ThumbRect()
            {
                if (!Scrolls) return Rectangle.Empty;
                int track = Height, total = ContentHeight;
                int len = Math.Min(track, Math.Max(Px(24), track * Height / total));
                int y = MaxScroll == 0 ? 0 : (int)((long)(track - len) * scroll / MaxScroll);
                return new Rectangle(BarRect.X, y, BarRect.Width, len);
            }

            static GraphicsPath Pill(RectangleF r)
            {
                var p = new GraphicsPath();
                float d = r.Width;
                if (r.Height <= d) { p.AddEllipse(r); return p; }
                p.AddArc(r.X, r.Y, d, d, 180, 180);
                p.AddArc(r.X, r.Bottom - d, d, d, 0, 180);
                p.CloseFigure();
                return p;
            }

            // ----- painting -----
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Back);
                for (int i = 0; i < tiles.Count; i++)
                {
                    var r = TileRect(i);
                    if (r.Bottom < 0 || r.Top > Height) continue;
                    PaintTile(g, tiles[i], r, i == hover, i == focus && ((Focused && ShowFocusCues) || sampleFocus));
                }
                if (Scrolls)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var bar = BarRect;
                    float w = (barHot || dragging ? 8 : 6) * DeviceDpi / 96f, x = bar.X + (bar.Width - w) / 2f;
                    using (var path = Pill(new RectangleF(x, 0, w, Height)))
                    using (var b = new SolidBrush(Track)) g.FillPath(b, path);
                    var t = ThumbRect();
                    using (var path = Pill(new RectangleF(x, t.Y, w, t.Height)))
                    using (var b = new SolidBrush(barHot || dragging ? ThumbHot : Theme.InfoAccent)) g.FillPath(b, path);
                    g.SmoothingMode = SmoothingMode.None;
                }
            }

            void PaintTile(Graphics g, Tile t, Rectangle r, bool hot, bool focused)
            {
                const TextFormatFlags centre = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter |
                                               TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                int pad = Px(12), w = r.Width - 2 * pad;
                {
                    using (var b = new SolidBrush(hot ? HotBack : Theme.InfoBack)) g.FillRectangle(b, r);
                    if (t.Active)
                        using (var pen = new Pen(Theme.InfoAccent, Px(2)) { Alignment = PenAlignment.Inset })
                            g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                    else
                        using (var pen = new Pen(hot ? HotBorder : Theme.Border)) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                    if (t.Active)
                    {
                        // "In use" tag in the top right corner
                        string tag = L.S("In use", "I brug");
                        var ts = TextRenderer.MeasureText(tag, small, Size.Empty, TextFormatFlags.NoPadding);
                        var tr = new Rectangle(r.Right - Px(2) - ts.Width - Px(10), r.Y + Px(2), ts.Width + Px(10), Px(20));
                        using (var b = new SolidBrush(Theme.InfoAccent)) g.FillRectangle(b, tr);
                        TextRenderer.DrawText(g, tag, small, tr, Color.White, centre);
                    }
                    var pr = new Rectangle(r.X + (r.Width - Px(112)) / 2, r.Y + Px(10), Px(112), Px(112));
                    if (t.Picture != null)
                    {
                        // the whole picture, scaled to fit the 112 px box, centred
                        double sc = Math.Min(pr.Width / (double)t.Picture.Width, pr.Height / (double)t.Picture.Height);
                        int pw = (int)Math.Round(t.Picture.Width * sc), ph = (int)Math.Round(t.Picture.Height * sc);
                        var st = g.Save();
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(t.Picture, new Rectangle(pr.X + (pr.Width - pw) / 2, pr.Y + (pr.Height - ph) / 2, pw, ph));
                        g.Restore(st);
                    }
                    else
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        PrinterArt.Draw(g, Rectangle.Inflate(pr, -Px(14), -Px(14)), t.Model);
                        g.SmoothingMode = SmoothingMode.None;
                    }
                    // texts from y 130; the last line (profit) ends 10 px above the tile's bottom edge / the red frame
                    int y = r.Y + Px(130);
                    TextRenderer.DrawText(g, t.Name, bold, new Rectangle(r.X + pad, y, w, Px(20)), Theme.Text, centre);
                    if (t.ModelText.Length > 0)
                        TextRenderer.DrawText(g, t.ModelText, small, new Rectangle(r.X + pad, y + Px(20), w, Px(18)), Theme.Muted, centre);
                    using (var b = new SolidBrush(Theme.Border)) g.FillRectangle(b, r.X + pad, y + Px(44), w, Math.Max(1, Px(1)));
                    TextRenderer.DrawText(g, t.Line1, small, new Rectangle(r.X + pad, y + Px(50), w, Px(18)), Theme.Text, centre);
                    TextRenderer.DrawText(g, t.Line2, small, new Rectangle(r.X + pad, y + Px(68), w, Px(18)), Theme.Text, centre);
                    TextRenderer.DrawText(g, t.Prints, small, new Rectangle(r.X + pad, y + Px(86), w, Px(18)), Theme.Muted, centre);
                    if (t.Profit.Length > 0)
                        TextRenderer.DrawText(g, t.Profit, small, new Rectangle(r.X + pad, y + Px(104), w, Px(18)), t.ProfitColour, centre);
                }
                if (focused)
                    ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -Px(5), -Px(5)), Theme.Text, hot ? HotBack : Theme.InfoBack);
            }

            // ----- mouse -----
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (dragging)
                {
                    var t = ThumbRect();
                    int free = Height - t.Height;
                    if (free > 0) ScrollTo((int)((long)(e.Y - dragOffset) * MaxScroll / free));
                    return;
                }
                int h = HitTile(e.Location);
                bool bh = Scrolls && BarRect.Contains(e.Location);
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                if (h != hover || bh != barHot) { hover = h; barHot = bh; Invalidate(); UpdateNameTip(); }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (!dragging && (hover != -1 || barHot)) { hover = -1; barHot = false; Invalidate(); }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                Focus();
                if (Scrolls && BarRect.Contains(e.Location))
                {
                    var t = ThumbRect();
                    if (t.Contains(e.Location)) { dragging = true; dragOffset = e.Y - t.Y; Capture = true; Invalidate(); }
                    else ScrollTo(e.Y < t.Y ? scroll - Height : scroll + Height);   // a page
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (dragging) { dragging = false; Capture = false; Invalidate(); return; }
                if (e.Button != MouseButtons.Left) return;
                int h = HitTile(e.Location);
                if (h >= 0 && Picked != null) { focus = h; Picked(tiles[h]); }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (Scrolls) ScrollTo(scroll - e.Delta * Px(WheelStep) / 120);
            }

            // ----- keys -----
            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData)
                {
                    case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End:
                    case Keys.PageUp: case Keys.PageDown: case Keys.Enter: case Keys.Space:
                        return true;
                }
                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                int to = focus;
                switch (e.KeyCode)
                {
                    case Keys.Left: to = focus - 1; break;
                    case Keys.Right: to = focus + 1; break;
                    case Keys.Up: to = focus - Cols; break;
                    case Keys.Down: to = focus + Cols < tiles.Count ? focus + Cols : (focus / Cols < RowCount - 1 ? tiles.Count - 1 : focus); break;
                    case Keys.PageUp: to = Math.Max(focus % Cols, focus - 2 * Cols); break;
                    case Keys.PageDown: to = Math.Min(tiles.Count - 1, focus + 2 * Cols); break;
                    case Keys.Home: to = 0; break;
                    case Keys.End: to = tiles.Count - 1; break;
                    case Keys.Enter:
                    case Keys.Space:
                        e.Handled = e.SuppressKeyPress = true;
                        if (Picked != null && FocusedTile != null) Picked(FocusedTile);
                        return;
                    default: base.OnKeyDown(e); return;
                }
                e.Handled = true;
                if (to < 0 || to >= tiles.Count || to == focus) return;
                focus = to;
                ScrollIntoView(focus);
                Invalidate();
            }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { small.Dispose(); bold.Dispose(); nameTip.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }

    /// Simple printer drawings in the theme colours, used when Bambu Studio's own pictures aren't there
    /// (main window's printer card, Choose printer tiles).
    static class PrinterArt
    {
        /// Draws the printer model (Wear.Models key; unknown = A1) inside r, on a 24 × 28 grid.
        public static void Draw(Graphics g, Rectangle r, string model)
        {
            float u = Math.Min(r.Height / 28f, r.Width / 24f), x0 = r.X + (r.Width - 24 * u) / 2f, y0 = r.Y + (r.Height - 28 * u) / 2f;
            Func<float, float, float, float, RectangleF> R = (x, y, rw, rh) => new RectangleF(x0 + x * u, y0 + y * u, rw * u, rh * u);
            string key = Wear.FindModel(model).Key;
            using (var m = new SolidBrush(Theme.Muted))
            using (var d = new SolidBrush(Theme.ArtDark))
            using (var red = new SolidBrush(Theme.Others))
            {
                if (key == Wear.ModelP1S)
                {
                    // closed box: frame, dark glass door, top lid, red toolhead inside
                    g.FillRectangle(m, R(1, 1, 22, 26));
                    g.FillRectangle(d, R(3, 4, 18, 19));        // glass door
                    g.FillRectangle(red, R(9, 7, 6, 5));        // toolhead behind the glass
                    g.FillRectangle(m, R(4, 6, 16, 1.5f));      // gantry
                    g.FillRectangle(m, R(6, 19, 12, 1.5f));     // bed
                    g.FillRectangle(d, R(16, 24.5f, 4, 1.5f));  // screen
                }
                else if (key == Wear.ModelA1Mini)
                {
                    // one upright on the left, arm over a small bed
                    g.FillRectangle(m, R(2, 23, 20, 4));        // base
                    g.FillRectangle(d, R(7, 21, 12, 1.5f));     // bed
                    g.FillRectangle(m, R(3, 3, 2.5f, 20));      // upright
                    g.FillRectangle(m, R(3, 9, 17, 2));         // X arm
                    g.FillRectangle(red, R(11, 7, 5, 6));       // toolhead
                    g.FillRectangle(red, R(12.75f, 13, 1.5f, 1.5f)); // nozzle
                    g.FillRectangle(d, R(17, 24, 4, 2));        // screen
                }
                else
                {
                    // Bambu Lab A1: base + bed, frame, X gantry, red toolhead
                    g.FillRectangle(m, R(1, 23, 22, 4));      // base
                    g.FillRectangle(d, R(4, 21, 15, 1.5f));   // bed
                    g.FillRectangle(m, R(3, 2, 2, 21));       // left upright
                    g.FillRectangle(m, R(19, 2, 2, 21));      // right upright
                    g.FillRectangle(m, R(3, 1, 18, 2));       // top bar
                    g.FillRectangle(m, R(1, 10, 22, 2));      // X gantry
                    g.FillRectangle(red, R(9, 8, 6, 7));      // toolhead
                    g.FillRectangle(red, R(11, 15, 2, 1.5f)); // nozzle
                    g.FillRectangle(d, R(16, 24, 4, 2));      // screen
                }
            }
        }
    }
}
