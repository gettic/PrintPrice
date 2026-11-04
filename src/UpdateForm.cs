using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintPrice
{
    /// "A new version is available" dialog: shows the release notes, downloads, verifies and installs the update.
    /// DialogResult.OK means the new version has been started and the program should exit now.
    public class UpdateForm : CaptionForm
    {
        readonly UpdateInfo update;
        readonly Action beforeRestart;
        readonly InfoBox status;
        readonly Button btnUpdate, btnLater, btnPage;
        readonly NotesBox notesBox;
        CancellationTokenSource cts;

        public UpdateForm(UpdateInfo update, Action beforeRestart)
        {
            this.update = update;
            this.beforeRestart = beforeRestart;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = L.S("Update available", "Opdatering klar");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(16, 12, 16, 14) };
            Controls.Add(t);
            t.Controls.Add(new Label
            {
                Text = L.F("Version {0} is available", "Version {0} er klar", Updater.Short(update.Version)),
                AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2)
            });
            t.Controls.Add(new Label { Text = L.F("You have version {0}.", "Du har version {0}.", Updater.CurrentText), AutoSize = true, Tag = "muted", Margin = new Padding(0, 0, 0, 10) });

            if ((update.Notes ?? "").Trim().Length > 0)
            {
                t.Controls.Add(new Label { Text = L.S("What's new", "Nyheder"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) });
                notesBox = new NotesBox(440, update.Notes) { Margin = new Padding(0, 2, 0, 8) };
                t.Controls.Add(notesBox);
            }

            status = new InfoBox(440, 9.5f) { Margin = new Padding(0, 2, 0, 8) };
            t.Controls.Add(status);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            // 32 px high like every button; 6 px apart, the right one ending on the notes box's right edge (no right margin)
            btnLater = new Button { Text = L.S("Later", "Senere"), AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(75, 32), Margin = new Padding(6, 0, 0, 0) };
            btnUpdate = new Button { Text = L.S("Update now", "Opdatér nu"), AutoSize = true, MinimumSize = new Size(75, 32), Margin = new Padding(6, 0, 0, 0) };
            btnPage = new Button { Text = L.S("Open download page", "Åbn download-siden"), AutoSize = true, Visible = false, MinimumSize = new Size(75, 32), Margin = new Padding(6, 0, 0, 0) };
            btnUpdate.Click += delegate { UpdateNow(); };
            btnPage.Click += delegate { OpenPage(); };
            buttons.Controls.Add(btnLater);
            buttons.Controls.Add(btnUpdate);
            buttons.Controls.Add(btnPage);
            t.Controls.Add(buttons);

            AcceptButton = btnUpdate;
            CancelButton = btnLater;
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (cts != null && DialogResult != DialogResult.OK) cts.Cancel();
            };
            Theme.Apply(this);
            if (notesBox != null) notesBox.ApplyTheme();   // Apply paints panels in the window colour
            ResumeLayout(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            if (notesBox != null) notesBox.FitHeight(220);   // before base.OnLoad centres the window
            base.OnLoad(e);
        }

        protected override bool ShowWithoutActivation { get { return Screenshots.Active; } }

        /// Picture mode: scrolls the release notes to the top or the end.
        internal void ScrollNotes(bool end)
        {
            if (notesBox != null) notesBox.ScrollTo(end);
        }

        void SetStatus(string text, InfoKind kind)
        {
            status.Kind = kind;
            status.Text = text;
        }

        async void UpdateNow()
        {
            if (Screenshots.Active) return;   // picture mode never downloads
            btnUpdate.Enabled = false;
            btnPage.Visible = false;
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            var progress = new Progress<int>(p => SetStatus(L.S("Downloading… ", "Henter… ") + p + " %", InfoKind.Info));
            SetStatus(L.S("Downloading…", "Henter…"), InfoKind.Info);
            try
            {
                string file = await Task.Run(() => Updater.Download(update, progress, ct));
                SetStatus(L.S("✓ Download verified. Restarting…", "✓ Download kontrolleret. Genstarter…"), InfoKind.Info);   // red stripe like every info box
                if (beforeRestart != null) beforeRestart();
                Updater.InstallAndRestart(file);
                DialogResult = DialogResult.OK;   // caller exits the program
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                string msg = ex is UpdateException ? ex.Message : L.F("The update failed ({0}).", "Opdateringen mislykkedes ({0}).", ex.Message);
                SetStatus(msg + L.S("\nYour current version is unchanged.", "\nDin nuværende version er uændret."), InfoKind.Error);
                btnUpdate.Text = L.S("Try again", "Prøv igen");
                btnUpdate.Enabled = true;
                btnPage.Visible = true;
            }
        }

        void OpenPage()
        {
            try { Process.Start(update.PageUrl ?? Updater.ReleasesPage); } catch { }
        }
    }

    /// Release notes in an InfoBox-style frame (shaded, border, red stripe). "## v1.2.3" lines become bold
    /// headings with their bullets under them. Grows with the text up to a maximum height, then scrolls
    /// with a slim red scroll bar (the text box's own bar is pushed outside a clipping panel).
    public class NotesBox : Panel
    {
        readonly NotesRichText rtb;
        readonly Panel view;          // clips off the RichTextBox's native scroll bar
        readonly RedScrollBar bar;
        readonly string rtf;
        int contentHeight;

        public NotesBox(int width, string markdown)
        {
            Padding = new Padding(12, 6, 2, 6);
            Size = new Size(width, 60);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            rtb = new NotesRichText
            {
                BorderStyle = BorderStyle.None, ReadOnly = true, TabStop = false,
                ScrollBars = RichTextBoxScrollBars.ForcedVertical, DetectUrls = false, ShortcutsEnabled = true,
                Cursor = Cursors.Default, Font = new Font("Segoe UI", 9f), ForeColor = Theme.Text
            };
            rtf = ToRtf(markdown);
            rtb.ContentsResized += delegate(object s, ContentsResizedEventArgs e) { contentHeight = rtb.ContentHeight = e.NewRectangle.Height; };
            rtb.GotFocus += delegate { HideCaret(rtb.Handle); };
            rtb.MouseUp += delegate { HideCaret(rtb.Handle); };
            view = new Panel();
            view.Controls.Add(rtb);
            bar = new RedScrollBar(rtb) { Visible = false };
            rtb.Scrolled += delegate { bar.Invalidate(); };
            Controls.Add(view);
            Controls.Add(bar);
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            BackColor = view.BackColor = bar.BackColor = Theme.InfoBack;
            rtb.BackColor = Theme.InfoBack;   // set explicitly, or a read-only box turns grey
            Invalidate();
        }

        int BarWidth { get { return (int)Math.Round(10 * DeviceDpi / 96f); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (view == null || bar == null) return;   // called while the constructor sets Padding/Size
            int bw = BarWidth;
            view.Bounds = new Rectangle(Padding.Left, Padding.Top,
                Math.Max(10, Width - Padding.Left - Padding.Right - bw - 4), Math.Max(10, Height - Padding.Vertical));
            bar.Bounds = new Rectangle(Width - 2 - bw - 1, 3, bw, Math.Max(10, Height - 6));
            int nativeBar = rtb.Width - rtb.ClientSize.Width;   // width of the hidden native bar
            rtb.Bounds = new Rectangle(0, 0, view.Width + nativeBar, view.Height);
        }

        /// Sizes the box to its text, at most maxHeight (at 96 dpi); longer text gets the red scroll bar.
        public void FitHeight(int maxHeight)
        {
            float scale = DeviceDpi / 96f;
            PerformLayout();
            rtb.Rtf = rtf;   // set late: font changes from the form re-format all text and drop the bold
            contentHeight = 0;
            SendMessage(rtb.Handle, 0x0441, IntPtr.Zero, IntPtr.Zero);   // EM_REQUESTRESIZE -> ContentsResized
            if (contentHeight <= 0) return;
            Height = Math.Min(contentHeight + Padding.Vertical + 2, (int)(maxHeight * scale));
            PerformLayout();
            bar.Visible = contentHeight > view.Height;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            using (var accent = new SolidBrush(Theme.InfoAccent)) g.FillRectangle(accent, 0, 0, 4, Height);
        }

        /// Scrolls the text to the top or the end (used by picture mode).
        public void ScrollTo(bool end)
        {
            if (!rtb.IsHandleCreated) return;
            rtb.ScrollPos = end ? rtb.MaxScroll : 0;
            bar.Invalidate();
            rtb.Update();
            bar.Update();
        }

        static readonly Regex Heading = new Regex(@"^\s*#+\s*(.*)$");

        /// Markdown-ish notes -> RTF. Body text is cleaned by Updater.NotesText (bullets, bold, links).
        static string ToRtf(string markdown)
        {
            var sb = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}");
            sb.Append(@"{\colortbl;").Append(Rgb(Color.White)).Append(Rgb(Theme.Text)).Append(@"}\f0").Append('\n');

            bool first = true;
            var body = new StringBuilder();
            foreach (string raw in (markdown ?? "").Replace("\r", "").Split('\n'))
            {
                Match m = Heading.Match(raw);
                if (!m.Success) { body.Append(raw).Append('\n'); continue; }
                AddBody(sb, body.ToString(), ref first);
                body.Length = 0;
                string title = m.Groups[1].Value.Trim();
                bool version = Regex.IsMatch(title, @"^v?\d");
                sb.Append(@"\pard\sb").Append(first ? 0 : (version ? 200 : 100)).Append(@"\sa60")
                  .Append(version ? @"\fs21\b\cf1 " : @"\fs18\b\cf2 ").Append(Esc(title)).Append(@"\b0\par").Append('\n');
                first = false;
            }
            AddBody(sb, body.ToString(), ref first);
            sb.Append('}');
            return sb.ToString();
        }

        static void AddBody(StringBuilder sb, string text, ref bool first)
        {
            string s = Updater.NotesText(new UpdateInfo { Notes = text }, int.MaxValue);
            foreach (string line in s.Split('\n'))
            {
                string l = line.Trim();
                if (l.Length == 0) continue;
                if (l.StartsWith("• "))
                    sb.Append(@"\pard\li260\fi-260\tx260\sa30\fs18\cf2 ").Append(Esc("•")).Append(@"\tab ").Append(Esc(l.Substring(2)));
                else
                    sb.Append(@"\pard\sa60\fs18\cf2 ").Append(Esc(l));
                sb.Append(@"\par").Append('\n');
                first = false;
            }
        }

        static string Rgb(Color c) { return @"\red" + c.R + @"\green" + c.G + @"\blue" + c.B + ";"; }

        static string Esc(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (ch == '\\' || ch == '{' || ch == '}') sb.Append('\\').Append(ch);
                else if (ch > 127) sb.Append(@"\u").Append((int)(short)ch).Append('?');
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        [DllImport("user32.dll")]
        static extern bool HideCaret(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    }

    /// RichTextBox that tells when its text has scrolled (wheel, keys, selection drag, code), and never
    /// scrolls past the last line: RichEdit's own scroll range lets the last line go up to the top of the box.
    class NotesRichText : RichTextBox
    {
        public event EventHandler Scrolled;
        public int ContentHeight;   // text height in pixels (from ContentsResized)
        bool clamping;

        public int MaxScroll { get { return Math.Max(0, ContentHeight - ClientSize.Height); } }

        public int ScrollPos
        {
            get { var p = new POINT(); SendMessage(Handle, 0x04DD, IntPtr.Zero, ref p); return p.y; }   // EM_GETSCROLLPOS
            set { var p = new POINT { y = Math.Max(0, Math.Min(MaxScroll, value)) }; SendMessage(Handle, 0x04DE, IntPtr.Zero, ref p); }
        }

        double wheelPending;   // pixels not scrolled yet (high-resolution wheels and touchpads send small deltas)

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A && IsHandleCreated)   // WM_MOUSEWHEEL: scroll ourselves; RichEdit's smooth scroll overshoots the end
            {
                int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                int lines = SystemInformation.MouseWheelScrollLines;   // -1 = one page per notch
                int step = lines < 0 ? ClientSize.Height : Math.Max(1, lines) * Font.Height;
                wheelPending += delta * step / 120.0;
                int px = (int)wheelPending;
                wheelPending -= px;
                if (px != 0) ScrollPos -= px;
                if (ScrollPos == 0 || ScrollPos >= MaxScroll) wheelPending = 0;
                m.Result = IntPtr.Zero;
                if (Scrolled != null) Scrolled(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
            switch (m.Msg)
            {
                case 0x0100: case 0x0113: case 0x0115: case 0x04DE:   // KEYDOWN, TIMER, VSCROLL, EM_SETSCROLLPOS
                    if (!clamping && ContentHeight > 0 && ScrollPos > MaxScroll)
                    {
                        clamping = true;
                        ScrollPos = MaxScroll;
                        clamping = false;
                    }
                    if (Scrolled != null) Scrolled(this, EventArgs.Empty);
                    break;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, ref POINT l);
    }

    /// Slim custom-drawn vertical scroll bar in the app's red, driving a RichTextBox whose own bar is hidden.
    class RedScrollBar : Control
    {
        static readonly Color Track = Color.FromArgb(34, 36, 41);
        static readonly Color ThumbHot = Color.FromArgb(205, 70, 75);
        readonly NotesRichText target;
        bool hover, dragging;
        int dragOffset;

        public RedScrollBar(NotesRichText target)
        {
            this.target = target;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        float Dpi { get { return DeviceDpi / 96f; } }
        int Pad { get { return (int)Math.Round(2 * Dpi); } }

        /// Text height, visible height and scroll position (pixels) of the target.
        bool Info(out int total, out int page, out int pos)
        {
            total = page = pos = 0;
            if (!target.IsHandleCreated) return false;
            total = target.ContentHeight;   // not GetScrollInfo: its range runs a page past the end
            page = target.ClientSize.Height;
            pos = Math.Min(target.ScrollPos, Math.Max(0, total - page));
            return page > 0 && total > page;
        }

        Rectangle ThumbRect()
        {
            int total, page, pos;
            if (!Info(out total, out page, out pos)) return Rectangle.Empty;
            int track = Height - 2 * Pad;
            int len = Math.Min(track, Math.Max((int)(24 * Dpi), track * page / total));
            int y = Pad + (int)((long)(track - len) * pos / (total - page));
            return new Rectangle(0, y, Width, len);
        }

        void ScrollTo(int pos)
        {
            int total, page, cur;
            if (!Info(out total, out page, out cur)) return;
            target.ScrollPos = pos;   // clamped to the last line
            Invalidate();
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

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = (hover || dragging ? 8 : 6) * Dpi;
            float x = (Width - w) / 2f;
            using (var path = Pill(new RectangleF(x, Pad, w, Height - 2 * Pad)))
            using (var b = new SolidBrush(Track)) g.FillPath(b, path);
            Rectangle t = ThumbRect();
            if (t.IsEmpty) return;
            using (var path = Pill(new RectangleF(x, t.Y, w, t.Height)))
            using (var b = new SolidBrush(hover || dragging ? ThumbHot : Theme.InfoAccent)) g.FillPath(b, path);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int total, page, pos;
            if (!Info(out total, out page, out pos)) return;
            Rectangle t = ThumbRect();
            if (t.Contains(e.Location)) { dragging = true; dragOffset = e.Y - t.Y; Capture = true; Invalidate(); }
            else ScrollTo(e.Y < t.Y ? pos - page : pos + page);   // click on the track: one page
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                int total, page, pos;
                if (!Info(out total, out page, out pos)) return;
                Rectangle t = ThumbRect();
                int free = Height - 2 * Pad - t.Height;
                if (free > 0) ScrollTo((int)((long)(e.Y - dragOffset - Pad) * (total - page) / free));
                return;
            }
            bool h = ThumbRect().Contains(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            Capture = false;
            hover = ClientRectangle.Contains(e.Location) && ThumbRect().Contains(e.Location);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover && !dragging) { hover = false; Invalidate(); }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A && target.IsHandleCreated)   // wheel over the bar scrolls the text
            {
                SendMessage(target.Handle, m.Msg, m.WParam, m.LParam);
                return;
            }
            base.WndProc(ref m);
        }

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    }
}
