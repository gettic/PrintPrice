using System;
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
        }

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
            if (note != null && kind != InfoKind.Info) note.ForeColor = Accent;   // warnings stand out under the table
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
}
