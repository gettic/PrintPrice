using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Dark colour palette + helpers to theme a WinForms control tree. The app is dark mode only.
    public static class Theme
    {
        public static readonly Color Back = Color.FromArgb(32, 32, 32);
        public static readonly Color Input = Color.FromArgb(45, 45, 48);
        public static readonly Color Text = Color.FromArgb(232, 232, 232);
        public static readonly Color Muted = Color.FromArgb(155, 155, 155);
        public static readonly Color Border = Color.FromArgb(78, 78, 82);
        public static readonly Color ButtonBack = Color.FromArgb(55, 55, 58);
        public static readonly Color ButtonHover = Color.FromArgb(70, 70, 74);
        public static readonly Color Friends = Color.FromArgb(90, 210, 150);
        public static readonly Color Others = Color.FromArgb(205, 70, 75);       // customer price: lighter InfoAccent red, readable as big text
        public static readonly Color Error = Color.FromArgb(255, 120, 120);
        public static readonly Color Success = Color.FromArgb(110, 215, 150);
        public static readonly Color InvalidBack = Color.FromArgb(95, 40, 40);
        public static readonly Color InfoBack = Color.FromArgb(42, 45, 50);
        public static readonly Color InfoAccent = Color.FromArgb(150, 30, 35);    // dark red
        public static readonly Color Warning = Color.FromArgb(240, 180, 70);

        /// Right margin after a row caption (96 dpi units): ~30 px clear gap to the box after the longest label,
        /// in the main window and Settings alike (label columns are AutoSize, so this follows the real text width).
        public const int LabelGap = 21;

        /// Label drawn with grayscale anti-aliasing: ClearType colour fringes make thin grey text look tinted.
        public class PlainLabel : Label
        {
            public PlainLabel() { UseCompatibleTextRendering = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                base.OnPaint(e);
            }
        }

        /// Underlined text link that also works from the keyboard: reachable with Tab, Enter or Space clicks it,
        /// a dotted focus rectangle shows when it has keyboard focus. Looks like a PlainLabel otherwise.
        public class LinkText : PlainLabel
        {
            public LinkText()
            {
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
            }

            protected override bool IsInputKey(Keys keyData)
            {
                // Enter/Space go to this control instead of the form's OK button
                return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; e.SuppressKeyPress = true; OnClick(EventArgs.Empty); return; }
                base.OnKeyDown(e);
            }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, ForeColor, BackColor);
            }
        }

        /// Recursively colours a control tree. Labels can carry Tag = "muted" | "friends" | "others".
        public static void Apply(Control c)
        {
            if (c is InfoBox) { ((InfoBox)c).ApplyTheme(); return; }
            if (c is Form || c is Panel || c is GroupBox || c is TableLayoutPanel || c is FlowLayoutPanel)
            {
                c.BackColor = Back;
                c.ForeColor = Text;
            }
            else if (c is TextBox)
            {
                var tb = (TextBox)c;
                tb.BackColor = Input;
                tb.ForeColor = Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is Button)
            {
                var b = (Button)c;
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = ButtonBack;
                b.ForeColor = Text;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = ButtonHover;
                b.FlatAppearance.MouseDownBackColor = Border;
            }
            else if (c is ComboBox)
            {
                var cb = (ComboBox)c;
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Input;
                cb.ForeColor = Text;
                // owner-draw so the closed box and the list both use the theme colours
                if (cb.DrawMode != DrawMode.OwnerDrawFixed)
                {
                    cb.DrawMode = DrawMode.OwnerDrawFixed;
                    cb.DrawItem += DrawComboItem;
                }
            }
            else if (c is ListBox)
            {
                var lb = (ListBox)c;
                lb.BackColor = Input;
                lb.ForeColor = Text;
                lb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is CheckBox)
            {
                c.ForeColor = Text;
                ((CheckBox)c).FlatStyle = FlatStyle.Flat;
            }
            else if (c is Label)
            {
                string tag = c.Tag as string;
                c.ForeColor = tag == "muted" ? Muted
                            : tag == "friends" ? Friends
                            : tag == "others" ? Others
                            : Text;
            }
            foreach (Control child in c.Controls) Apply(child);
            c.Invalidate();
        }

        /// Dark red call-to-action button (e.g. "Update to vX"). Call after Apply, which resets button colours.
        public static void AccentButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = InfoAccent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Color.FromArgb(190, 55, 60);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(175, 40, 45);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(120, 22, 27);
        }

        static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            var cb = (ComboBox)sender;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            Color back = selected ? InfoAccent : Input;
            Color fore = selected ? Color.White : Text;
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
            {
                TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, e.Bounds, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// Dark title bar (Windows 10 20H1+ / Windows 11).
        public static void TitleBar(Form f)
        {
            if (!f.IsHandleCreated) return;
            int v = 1;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
            }
            catch { }
        }
    }

    public enum InfoKind { Info, Success, Warning, Error }

    /// A shaded box with a coloured stripe for help and status text. Hidden while it has no text.
    public class InfoBox : TableLayoutPanel
    {
        readonly Label label;
        readonly float fontSize;
        TableLayoutPanel grid;   // optional two-column table (label … value)
        Label note;              // optional text under the table
        InfoKind kind;

        public InfoBox(int width, float fontSize)
        {
            this.fontSize = fontSize;
            ColumnCount = 1;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12, 7, 10, 7);
            Margin = new Padding(3, 2, 3, 8);
            MinimumSize = new Size(width, 0);
            MaximumSize = new Size(width, 0);
            label = NewLabel();
            Controls.Add(label, 0, 0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Visible = false;
            ApplyTheme();
        }

        /// Usable text width; read at runtime so it follows DPI scaling of the box.
        int InnerWidth { get { return Math.Max(50, MaximumSize.Width - Padding.Horizontal - 2); } }

        /// New width in real pixels (e.g. measured on Load); the text rewraps to it.
        public void SetWidth(int width)
        {
            MaximumSize = new Size(width, 0);
            MinimumSize = new Size(width, 0);
            label.MaximumSize = new Size(InnerWidth, 0);
            if (grid != null) grid.MinimumSize = new Size(InnerWidth, 0);
            if (note != null) note.MaximumSize = new Size(InnerWidth, 0);
        }

        Label NewLabel()
        {
            return new Label
            {
                AutoSize = true, Margin = Padding.Empty, UseMnemonic = false,
                MaximumSize = new Size(InnerWidth, 0), Font = SharedFont(fontSize, FontStyle.Regular)
            };
        }

        // One Font per size/style, shared by all labels and never disposed (lives for the whole run).
        static readonly System.Collections.Generic.Dictionary<string, Font> fontCache =
            new System.Collections.Generic.Dictionary<string, Font>();

        static Font SharedFont(float size, FontStyle style)
        {
            string key = size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + (int)style;
            Font f;
            if (!fontCache.TryGetValue(key, out f)) { f = new Font("Segoe UI", size, style); fontCache[key] = f; }
            return f;
        }

        void ClearTable()
        {
            if (grid != null) { Controls.Remove(grid); grid.Dispose(); grid = null; }
            if (note != null) { Controls.Remove(note); note.Dispose(); note = null; }
            noteMuted = false;
        }

        bool noteMuted;   // the note is a muted second line (SetText), not a table note

        /// Text plus a muted (grey) line under it, e.g. "↻ Opened from history…" + "Saved price … — now …".
        /// Setting Text (or SetTable) removes the muted line again.
        public void SetText(string text, string mutedLine)
        {
            if (label == null) return;
            SuspendLayout();
            ClearTable();
            label.Text = text ?? "";
            label.Visible = true;
            if (!string.IsNullOrEmpty(mutedLine))
            {
                note = NewLabel();
                note.Text = mutedLine;
                note.Margin = new Padding(0, 2, 0, 0);
                Controls.Add(note, 0, 2);
                noteMuted = true;
            }
            Visible = label.Text.Length > 0 || note != null;
            ApplyTheme();
            ResumeLayout(true);
        }

        /// The muted line of SetText ("" if none).
        public string MutedLine { get { return noteMuted && note != null ? note.Text : ""; } }

        /// Plain text (use \n for line breaks). Empty text hides the box.
        public override string Text
        {
            get { return label == null ? "" : label.Text; }
            set
            {
                if (label == null) return;
                ClearTable();
                label.Text = value ?? "";
                label.Visible = true;
                Visible = label.Text.Length > 0;
            }
        }

        /// Shows rows of "description … amount" with the amounts right-aligned.
        /// A row whose description starts with "=" is drawn bold (a total). Optional text goes underneath.
        public void SetTable(string[][] rows, string after)
        {
            SuspendLayout();
            ClearTable();
            label.Text = "";
            label.Visible = false;
            grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty, MinimumSize = new Size(InnerWidth, 0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (var r in rows)
            {
                bool total = r[0].StartsWith("=");
                var left = NewLabel();
                var right = NewLabel();
                left.Text = total ? r[0].TrimStart('=', ' ') : r[0];
                right.Text = r[1];
                right.Anchor = AnchorStyles.Right;
                left.Margin = right.Margin = new Padding(0, total ? 4 : 1, 0, 1);
                if (total) left.Font = right.Font = SharedFont(fontSize, FontStyle.Bold);
                int row = grid.RowCount++;
                grid.Controls.Add(left, 0, row);
                grid.Controls.Add(right, 1, row);
            }
            Controls.Add(grid, 0, 1);
            if (!string.IsNullOrEmpty(after))
            {
                note = NewLabel();
                note.Text = after;
                note.Margin = new Padding(0, 8, 0, 0);
                Controls.Add(note, 0, 2);
            }
            Visible = true;
            ApplyTheme();
            ResumeLayout(true);
        }

        public InfoKind Kind
        {
            get { return kind; }
            set { kind = value; ApplyTheme(); }
        }

        Color Accent
        {
            get
            {
                switch (kind)
                {
                    case InfoKind.Success: return Theme.Success;
                    case InfoKind.Warning: return Theme.Warning;
                    case InfoKind.Error: return Theme.Error;
                    default: return Theme.InfoAccent;
                }
            }
        }

        public void ApplyTheme()
        {
            BackColor = Theme.InfoBack;
            // plain error text is red; in a table only the note underneath is coloured, so the numbers stay readable
            Color fore = kind == InfoKind.Error && grid == null ? Theme.Error : Theme.Text;
            ColorTree(this, fore);
            if (note != null && noteMuted) note.ForeColor = Theme.Muted;
            else if (note != null && kind != InfoKind.Info) note.ForeColor = Accent;   // warnings stand out under the table
            Invalidate();
        }

        static void ColorTree(Control parent, Color fore)
        {
            foreach (Control c in parent.Controls)
            {
                c.BackColor = Theme.InfoBack;
                c.ForeColor = fore;
                ColorTree(c, fore);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            using (var accent = new SolidBrush(Accent)) g.FillRectangle(accent, 0, 0, 4, Height);
        }
    }

    /// GroupBox whose border and caption follow the theme (the stock one ignores dark colours).
    public class ThemedGroupBox : GroupBox
    {
        public ThemedGroupBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var flags = TextFormatFlags.Left | TextFormatFlags.SingleLine;
            Size sz = TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags);
            int top = sz.Height / 2;
            using (var pen = new Pen(Theme.Border))
                g.DrawRectangle(pen, 0, top, Width - 1, Height - top - 1);
            var r = new Rectangle(8, 0, sz.Width + 2, sz.Height);
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, r);
            TextRenderer.DrawText(g, Text, Font, r, ForeColor, flags);
        }
    }

    /// A window with our own title bar, so the logo and title sit further in from Windows 11's rounded corner.
    /// Windows keeps the frame (rounded corners, shadow, border); only its caption is removed (WM_NCCALCSIZE) and
    /// drawn here in the top CaptionHeight pixels of the client area: icon at x 10, title at x 33, minimise/close
    /// buttons 46 x 32 (96 dpi units). The form's single content control is kept just below the strip.
    public class CaptionForm : Form
    {
        const int CapH = 32, BtnW = 46, IconX = 10, TextX = 33;   // 96 dpi units
        static Icon smallIcon;
        static Font titleFont;
        readonly CaptionButton closeBtn, minBtn;
        bool active, placing;

        public CaptionForm()
        {
            closeBtn = new CaptionButton(true) { AccessibleName = L.S("Close", "Luk") };
            minBtn = new CaptionButton(false) { AccessibleName = L.S("Minimise", "Minimér"), Visible = false };
            closeBtn.Click += delegate { Close(); };
            minBtn.Click += delegate { WindowState = FormWindowState.Minimized; };
            Controls.Add(closeBtn);
            Controls.Add(minBtn);
        }

        int Px(int v) { return (int)Math.Round(v * CurrentAutoScaleDimensions.Height / 96f); }

        /// Height of the title strip in real pixels.
        public int CaptionHeight { get { return Px(CapH); } }

        /// Dimmed look while another window is active. Windows shown without activation (picture mode, hidden test
        /// windows: each form's ShowWithoutActivation) never activate, so they always look active.
        bool LooksActive { get { return active || ShowWithoutActivation; } }

        // ---------- frame ----------
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        protected override void WndProc(ref Message m)
        {
            const int WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084, HTCLIENT = 1, HTCAPTION = 2;
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero && !IsIconic(m.HWnd))
            {
                // let Windows work out the side and bottom borders, then give the whole top (caption and top border)
                // to the client area. Nothing of Windows' caption is left, so it can't peek through; the top edge
                // comes from DWM (OnHandleCreated extends the frame 1 px into the client area).
                var before = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                base.WndProc(ref m);
                var after = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                after.Top = before.Top;
                Marshal.StructureToPtr(after, m.LParam, false);
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT)
            {
                // the strip drags the window; right-click there opens the window menu (the buttons are child windows,
                // so they get their own clicks). Fixed windows don't maximise, so double-click does nothing.
                int lp = unchecked((int)(long)m.LParam);
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                if (p.Y < CaptionHeight) m.Result = (IntPtr)HTCAPTION;
            }
        }

        /// Window size for a client size: side and bottom borders only (no caption, no top border, see WM_NCCALCSIZE).
        /// The form's auto size uses this, so the window is exactly content + strip.
        protected override Size SizeFromClientSize(Size clientSize)
        {
            Size s = base.SizeFromClientSize(clientSize);
            return new Size(s.Width, clientSize.Height + (s.Width - clientSize.Width) / 2);   // bottom border = one side border
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            minBtn.Visible = MinimizeBox;
            // 1 px of DWM frame at the top keeps the window's shadow and top edge without a caption
            try { var mg = new MARGINS { Top = 1 }; DwmExtendFrameIntoClientArea(Handle, ref mg); } catch { }
            // make Windows ask WM_NCCALCSIZE again with the handle hooked up, then size the window to match
            const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            PerformLayout();
        }

        // ---------- layout ----------
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (placing) { base.OnLayout(e); return; }
            placing = true;
            try
            {
                int cap = CaptionHeight, bw = Px(BtnW);
                foreach (Control c in Controls)
                    if (!(c is CaptionButton) && c.Top != cap) c.Top = cap;   // content right under the strip
                // park the buttons at the left while the window sizes itself to the content (so they never hold it wide)
                closeBtn.SetBounds(0, 0, bw, cap);
                minBtn.SetBounds(0, 0, bw, cap);
                base.OnLayout(e);
                PlaceButtons();
            }
            finally { placing = false; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (placing) return;
            placing = true;
            try { PlaceButtons(); } finally { placing = false; }
            Invalidate(new Rectangle(0, 0, ClientSize.Width, CaptionHeight));
        }

        void PlaceButtons()
        {
            int cap = CaptionHeight, bw = Px(BtnW);
            closeBtn.SetBounds(ClientSize.Width - bw, 0, bw, cap);
            minBtn.SetBounds(ClientSize.Width - 2 * bw, 0, bw, cap);
            if (IsHandleCreated) Invalidate(new Rectangle(0, 0, ClientSize.Width, cap));   // title width follows the window
        }

        // ---------- drag from empty space, selectable label text ----------
        // Every child is hooked once when it is added (also later ones, via ControlAdded on containers):
        // - labels (not links): text can be marked with the mouse (double-click = word), Ctrl+C / right-click Copy;
        //   a press beside the text (empty part of the label) drags the window instead
        // - the form, panels, group boxes, picture boxes: a left press drags the window (standard WM_NCLBUTTONDOWN)
        // - everything else (inputs, buttons, links, lists, scroll bars): untouched, a press there only ends a text selection
        MouseEventHandler hPanelDown, hOtherDown, hLabelDown, hLabelMove, hLabelUp;
        EventHandler hEnter, hLabelText;
        PaintEventHandler hLabelPaint;
        ControlEventHandler hAdded;
        Label selLabel, menuLabel;
        int selAnchor, selCaret;
        bool selecting;
        TextLayout selLayout, hoverLayout;
        ContextMenuStrip textMenu;
        static readonly Color SelectionColour = Color.FromArgb(110, 51, 153, 255);   // see-through blue, the text keeps its colour

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            Hook(e.Control);
        }

        static bool IsLink(Control c) { return c is Theme.LinkText || c is LinkLabel; }

        void Hook(Control c)
        {
            if (c == null || c is CaptionButton) return;
            if (hPanelDown == null)
            {
                hPanelDown = PanelDown; hOtherDown = OtherDown; hLabelDown = LabelDown; hLabelMove = LabelMove; hLabelUp = LabelUp;
                hEnter = delegate { ClearSelection(); };
                hLabelText = delegate(object s, EventArgs e) { if (s == selLabel) ClearSelection(); };
                hLabelPaint = LabelPaint;
                hAdded = delegate(object s, ControlEventArgs e) { Hook(e.Control); };
            }
            if (c is Label && !IsLink(c))
            {
                c.MouseDown -= hLabelDown; c.MouseDown += hLabelDown;
                c.MouseMove -= hLabelMove; c.MouseMove += hLabelMove;
                c.MouseUp -= hLabelUp; c.MouseUp += hLabelUp;
                c.Paint -= hLabelPaint; c.Paint += hLabelPaint;
                c.TextChanged -= hLabelText; c.TextChanged += hLabelText;
                return;
            }
            if (c is Panel || c is GroupBox || c is PictureBox)
            {
                c.MouseDown -= hPanelDown; c.MouseDown += hPanelDown;
                c.ControlAdded -= hAdded; c.ControlAdded += hAdded;
                foreach (Control child in c.Controls) Hook(child);
                return;
            }
            // inputs, buttons, links, lists (their own children too, e.g. a grid's editing box) stay as they are
            c.MouseDown -= hOtherDown; c.MouseDown += hOtherDown;
            c.Enter -= hEnter; c.Enter += hEnter;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            PanelDown(this, e);
        }

        void PanelDown(object sender, MouseEventArgs e)
        {
            ClearSelection();
            if (e.Button == MouseButtons.Left && e.Clicks == 1) StartDrag();
        }

        void OtherDown(object sender, MouseEventArgs e) { ClearSelection(); }

        /// Moves the window like a press on the title strip.
        void StartDrag()
        {
            if (WindowState != FormWindowState.Normal || !IsHandleCreated) return;
            ReleaseCapture();
            SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);   // WM_NCLBUTTONDOWN, HTCAPTION
        }

        TextLayout LayoutFor(Label l)
        {
            if (l == selLabel && selLayout != null && selLayout.Fits(l)) return selLayout;
            if (hoverLayout == null || hoverLayout.Owner != l || !hoverLayout.Fits(l)) hoverLayout = new TextLayout(l);
            return hoverLayout;
        }

        void LabelDown(object sender, MouseEventArgs e)
        {
            var l = (Label)sender;
            if (e.Button == MouseButtons.Right) { if (l != selLabel) ClearSelection(); return; }   // menu on MouseUp
            if (e.Button != MouseButtons.Left) return;
            var lay = LayoutFor(l);
            if (!lay.InText(e.Location))
            {
                ClearSelection();
                if (e.Clicks == 1) StartDrag();   // empty part of the label = empty space
                return;
            }
            if (l != selLabel) { ClearSelection(); selLabel = l; }
            selLayout = lay;
            int i = lay.Hit(e.Location);
            if (e.Clicks >= 2) { lay.Word(i, out selAnchor, out selCaret); selecting = false; }
            else { selAnchor = selCaret = i; selecting = true; l.Capture = true; }
            l.Invalidate();
        }

        void LabelMove(object sender, MouseEventArgs e)
        {
            var l = (Label)sender;
            if (selecting && l == selLabel && (e.Button & MouseButtons.Left) != 0)
            {
                int i = selLayout.Hit(e.Location);
                if (i != selCaret) { selCaret = i; l.Invalidate(); }
                return;
            }
            if (e.Button != MouseButtons.None) return;
            var want = LayoutFor(l).InText(e.Location) ? Cursors.IBeam : Cursors.Default;
            if (l.Cursor != want) l.Cursor = want;
        }

        void LabelUp(object sender, MouseEventArgs e)
        {
            var l = (Label)sender;
            if (e.Button == MouseButtons.Left) { selecting = false; l.Capture = false; }
            else if (e.Button == MouseButtons.Right && l.Text.Length > 0) ShowTextMenu(l, e.Location);
        }

        void LabelPaint(object sender, PaintEventArgs e)
        {
            if (sender != selLabel || selAnchor == selCaret) return;
            var l = (Label)sender;
            if (selLayout == null || !selLayout.Fits(l)) { selLayout = null; selLabel = null; return; }   // resized: drop it
            using (var b = new SolidBrush(SelectionColour))
                foreach (var r in selLayout.Rects(Math.Min(selAnchor, selCaret), Math.Max(selAnchor, selCaret)))
                    e.Graphics.FillRectangle(b, r);
        }

        void ClearSelection()
        {
            selecting = false;
            if (selLabel == null) return;
            var l = selLabel;
            selLabel = null;
            selLayout = null;
            if (!l.IsDisposed) l.Invalidate();
        }

        /// The marked label text ("" = none).
        string SelectedText()
        {
            if (selLabel == null || selLayout == null || selAnchor == selCaret) return "";
            int a = Math.Min(selAnchor, selCaret), b = Math.Max(selAnchor, selCaret);
            return selLayout.Text.Substring(a, Math.Min(b, selLayout.Text.Length) - a);
        }

        static void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine)); } catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C) || keyData == (Keys.Control | Keys.Insert))
            {
                string t = SelectedText();
                if (t.Length > 0) { CopyText(t); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void ShowTextMenu(Label l, Point at)
        {
            if (textMenu == null)
            {
                textMenu = new ContextMenuStrip { ShowImageMargin = false, Renderer = new ToolStripProfessionalRenderer(new DarkMenuColours()),
                                                  BackColor = Theme.Input, ForeColor = Theme.Text };
                var copy = new ToolStripMenuItem(L.S("Copy", "Kopiér")) { ShortcutKeyDisplayString = "Ctrl+C", ForeColor = Theme.Text };
                var all = new ToolStripMenuItem(L.S("Select all", "Markér alt")) { ForeColor = Theme.Text };
                copy.Click += delegate
                {
                    if (menuLabel == null) return;
                    string t = menuLabel == selLabel ? SelectedText() : "";
                    CopyText(t.Length > 0 ? t : new TextLayout(menuLabel).Text);   // nothing marked: the whole text
                };
                all.Click += delegate
                {
                    if (menuLabel == null || menuLabel.IsDisposed) return;
                    ClearSelection();
                    selLabel = menuLabel;
                    selLayout = new TextLayout(menuLabel);
                    selAnchor = 0;
                    selCaret = selLayout.Text.Length;
                    selLabel.Invalidate();
                };
                textMenu.Items.Add(copy);
                textMenu.Items.Add(all);
                Disposed += delegate { textMenu.Dispose(); };
            }
            menuLabel = l;
            textMenu.Show(l, at);
        }

        sealed class DarkMenuColours : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Input; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Theme.InfoAccent; } }
            public override Color MenuItemSelected { get { return Theme.InfoAccent; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.InfoAccent; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.InfoAccent; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Input; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Input; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Input; } }
        }

        /// Where a label's characters are on screen: the same word wrap, alignment and padding as the label's own
        /// drawing (GDI text), so the selection lines up with the text. Measured once per selection / hover label.
        sealed class TextLayout
        {
            public readonly Label Owner;
            public readonly string Text;   // as shown (mnemonic && -> &)
            readonly string raw;
            readonly Font font;
            readonly Size size;
            readonly Padding pad;
            readonly List<Line> lines = new List<Line>();
            const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

            sealed class Line { public int Start, Len, X, Y, W, H; public int[] Xs; }

            public TextLayout(Label l)
            {
                Owner = l; raw = l.Text ?? ""; font = l.Font; size = l.ClientSize; pad = l.Padding;
                Text = l.UseMnemonic ? Shown(raw) : raw;
                if (Text.Length == 0) return;
                var face = new Rectangle(pad.Left, pad.Top, size.Width - pad.Horizontal, size.Height - pad.Vertical);
                int side = l.UseCompatibleTextRendering ? 0
                         : Math.Max(0, TextRenderer.MeasureText("x", font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width - W("x")) / 2;
                int avail = Math.Max(1, face.Width - 2 * side);
                int lineH = TextRenderer.MeasureText("Ag", font, new Size(int.MaxValue, int.MaxValue), Flags).Height;

                // greedy word wrap on spaces (only where the label can wrap), hard breaks on \n
                bool wraps = !(l.AutoSize && l.MaximumSize.Width == 0);
                int pos = 0;
                foreach (string para in Text.Split('\n'))
                {
                    int ps = pos, end = pos + para.Length;
                    int lineStart = ps, i = ps;
                    while (i < end)
                    {
                        int next = i;
                        while (next < end && Text[next] != ' ') next++;
                        while (next < end && Text[next] == ' ') next++;   // word + its spaces
                        string candidate = Text.Substring(lineStart, next - lineStart).TrimEnd(' ', '\r');
                        if (wraps && i > lineStart && W(candidate) > avail + 2)
                        {
                            AddLine(lineStart, i - lineStart);
                            lineStart = i;
                        }
                        i = next;
                    }
                    AddLine(lineStart, end - lineStart);
                    pos = end + 1;
                }

                int total = lines.Count * lineH;
                var a = l.TextAlign;
                bool middle = a == ContentAlignment.MiddleLeft || a == ContentAlignment.MiddleCenter || a == ContentAlignment.MiddleRight;
                bool bottom = a == ContentAlignment.BottomLeft || a == ContentAlignment.BottomCenter || a == ContentAlignment.BottomRight;
                bool center = a == ContentAlignment.TopCenter || a == ContentAlignment.MiddleCenter || a == ContentAlignment.BottomCenter;
                bool right = a == ContentAlignment.TopRight || a == ContentAlignment.MiddleRight || a == ContentAlignment.BottomRight;
                int y = middle ? face.Top + (face.Height - total) / 2 : bottom ? face.Bottom - total : face.Top;
                foreach (var ln in lines)
                {
                    ln.W = W(Text.Substring(ln.Start, ln.Len).TrimEnd(' ', '\r'));
                    ln.X = center ? face.Left + (face.Width - ln.W) / 2 : right ? face.Right - side - ln.W : face.Left + side;
                    ln.Y = y; ln.H = lineH;
                    y += lineH;
                }
            }

            void AddLine(int start, int len) { lines.Add(new Line { Start = start, Len = Math.Max(0, len) }); }

            int W(string s)
            {
                return s.Length == 0 ? 0 : TextRenderer.MeasureText(s, font, new Size(int.MaxValue, int.MaxValue), Flags).Width;
            }

            static string Shown(string s)
            {
                var sb = new System.Text.StringBuilder(s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] == '&') { if (i + 1 < s.Length && s[i + 1] == '&') { sb.Append('&'); i++; } continue; }
                    sb.Append(s[i]);
                }
                return sb.ToString();
            }

            public bool Fits(Label l) { return l.Text == raw && l.Font == font && l.ClientSize == size && l.Padding == pad; }

            public bool InText(Point p)
            {
                foreach (var ln in lines)
                    if (p.Y >= ln.Y - 2 && p.Y < ln.Y + ln.H + 2 && p.X >= ln.X - 4 && p.X <= ln.X + ln.W + 4) return true;
                return false;
            }

            int[] Xs(Line ln)
            {
                if (ln.Xs != null) return ln.Xs;
                ln.Xs = new int[ln.Len + 1];
                for (int k = 1; k <= ln.Len; k++) ln.Xs[k] = W(Text.Substring(ln.Start, k).TrimEnd('\r'));
                return ln.Xs;
            }

            /// Character boundary nearest to the point (0..Text.Length).
            public int Hit(Point p)
            {
                if (lines.Count == 0) return 0;
                Line best = lines[0];
                foreach (var ln in lines) if (p.Y >= ln.Y) best = ln;
                if (p.Y < lines[0].Y) return 0;
                if (p.Y >= lines[lines.Count - 1].Y + lines[lines.Count - 1].H) return Text.Length;
                var xs = Xs(best);
                int x = p.X - best.X, k = 0;
                while (k < best.Len && x > (xs[k] + xs[k + 1]) / 2) k++;
                return best.Start + k;
            }

            /// The word (letters/digits and , . - inside numbers) around a boundary.
            public void Word(int i, out int a, out int b)
            {
                Func<int, bool> inWord = k => k >= 0 && k < Text.Length && (char.IsLetterOrDigit(Text[k]) || Text[k] == '_' ||
                    ((Text[k] == ',' || Text[k] == '.') && k > 0 && k + 1 < Text.Length && char.IsDigit(Text[k - 1]) && char.IsDigit(Text[k + 1])));
                a = i; b = i;
                if (!inWord(i) && inWord(i - 1)) { a = i - 1; b = i - 1; }
                if (!inWord(a)) { b = Math.Min(Text.Length, i + 1); a = Math.Min(i, b); return; }
                while (inWord(a - 1)) a--;
                while (inWord(b)) b++;
            }

            /// Highlight boxes for the characters a..b.
            public IEnumerable<Rectangle> Rects(int a, int b)
            {
                foreach (var ln in lines)
                {
                    int s = Math.Max(a, ln.Start), e = Math.Min(b, ln.Start + ln.Len);
                    if (e <= s) continue;
                    var xs = Xs(ln);
                    int x0 = ln.X + xs[s - ln.Start], x1 = ln.X + xs[e - ln.Start];
                    if (x1 > x0) yield return new Rectangle(x0, ln.Y, x1 - x0, ln.H);
                }
            }
        }

        // ---------- painting ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int cap = CaptionHeight;
            if (e.ClipRectangle.Top >= cap) return;
            var g = e.Graphics;
            Icon ic = SmallIcon();
            if (ic != null)
            {
                int size = Px(16);
                g.DrawIcon(ic, new Rectangle(Px(IconX), (cap - size) / 2, size, size));
            }
            if (titleFont == null) titleFont = new Font("Segoe UI", 9f);
            int x = Px(TextX), right = (minBtn.Visible ? minBtn.Left : closeBtn.Left) - Px(8);
            TextRenderer.DrawText(g, Text, titleFont, new Rectangle(x, 0, Math.Max(0, right - x), cap),
                CaptionButton.Glyph(LooksActive),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        /// The exe's small icon (the size Windows used in its own caption), loaded once.
        static Icon SmallIcon()
        {
            if (smallIcon != null) return smallIcon;
            try
            {
                var small = new IntPtr[1];
                if (ExtractIconEx(Application.ExecutablePath, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                {
                    using (var tmp = Icon.FromHandle(small[0])) smallIcon = (Icon)tmp.Clone();
                    DestroyIcon(small[0]);
                }
            }
            catch { }
            return smallIcon;
        }

        void Repaint()
        {
            Invalidate(new Rectangle(0, 0, ClientSize.Width, CaptionHeight));
            closeBtn.Invalidate();
            minBtn.Invalidate();
        }

        protected override void OnActivated(EventArgs e) { base.OnActivated(e); active = true; Repaint(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); active = false; Repaint(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Repaint(); }

        /// Minimise / close button of the title strip: Windows 11 look (close turns red), never takes keyboard focus,
        /// screen readers see a push button with its name (Alt+Space and Alt+F4 work as usual too).
        sealed class CaptionButton : Control
        {
            readonly bool isClose;
            bool hot, down;

            public CaptionButton(bool isClose)
            {
                this.isClose = isClose;
                SetStyle(ControlStyles.Selectable, false);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                TabStop = false;
                Margin = Padding.Empty;   // a margin would make the auto-sized window grow by it on every layout
                AccessibleRole = AccessibleRole.PushButton;
            }

            internal void Press() { OnClick(EventArgs.Empty); }

            internal static Color Glyph(bool active) { return active ? Color.White : Color.FromArgb(140, 140, 140); }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; down = false; Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var form = FindForm() as CaptionForm;
                bool active = form == null || form.LooksActive;
                Color back = Theme.Back, fore = Glyph(active);
                if (isClose && (hot || down)) { back = down ? Color.FromArgb(148, 32, 20) : Color.FromArgb(196, 43, 28); fore = Color.White; }
                else if (hot) back = down ? Theme.ButtonHover : Theme.ButtonBack;
                using (var b = new SolidBrush(back)) g.FillRectangle(b, ClientRectangle);
                float scale = Height / (float)CapH;
                float s = 10 * scale, cx = Width / 2f, cy = Height / 2f;
                using (var pen = new Pen(fore, Math.Max(1f, (float)Math.Floor(scale))))
                {
                    if (isClose)
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.DrawLine(pen, cx - s / 2, cy - s / 2, cx + s / 2, cy + s / 2);
                        g.DrawLine(pen, cx + s / 2, cy - s / 2, cx - s / 2, cy + s / 2);
                    }
                    else
                    {
                        int y = (int)cy;
                        g.DrawLine(pen, (int)(cx - s / 2), y, (int)(cx + s / 2), y);
                    }
                }
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new Acc(this); }

            sealed class Acc : ControlAccessibleObject
            {
                public Acc(CaptionButton owner) : base(owner) { }
                public override string DefaultAction { get { return L.S("Press", "Tryk"); } }
                public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
                public override void DoDefaultAction() { ((CaptionButton)Owner).Press(); }
            }
        }
    }
}
