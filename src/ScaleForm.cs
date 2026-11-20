using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Issue #8 step 1: "Scale model" for the shown job (one size for the whole job). A % box (10-500 %, comma or dot,
    /// bad = red box + red line), the size W × D × H in mm worked out live from the last slice, a warning when it gets too
    /// big for the printer, "Reset to 100 %", "Apply (slices again)" and "Cancel". Apply = DialogResult.OK with Pct;
    /// MainForm slices the job again at that size. Owner: ui.
    internal sealed class ScaleForm : CaptionForm
    {
        const int ContentW = 360;
        readonly TextBox box;
        readonly Label sizeValue;
        readonly InfoBox info;
        readonly Button btnApply, btnReset, btnCancel;
        readonly double currentPct, sizeX, sizeY, sizeZ;
        readonly int objects;
        readonly string printer;

        /// The chosen size in percent (valid after OK).
        public double Pct { get; private set; }

        /// currentPct = the size the job was sliced at; x / y / z = its measured size at that scale (0 = unknown, e.g. a
        /// sliced file read without slicing); objects = how many were measured (2+ = the size is the biggest one's).
        public ScaleForm(double currentPct, double x, double y, double z, int objects, string printer)
        {
            this.currentPct = currentPct > 0 ? currentPct : History.DefaultScale;
            sizeX = x; sizeY = y; sizeZ = z;
            this.objects = objects;
            this.printer = printer;
            Pct = this.currentPct;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = L.S("Scale model", "Skalér model");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            if (Program.Hidden || Screenshots.Active) Screenshots.Place(this);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                              Padding = new Padding(14), Margin = Padding.Empty };
            Controls.Add(root);

            // "Size:" [150] %   /   W × D × H:  72,0 × 46,5 × 72,0 mm  (captions in one column, values lined up)
            var grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                              Margin = new Padding(0, 0, 0, 10), Padding = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var capSize = Caption(L.S("Size:", "Størrelse:"));
            box = new TextBox { Width = 70, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 0, 6, 6), MaxLength = 8,
                                Text = Num.Fmt(Math.Round(this.currentPct, 1)) };
            box.AccessibleName = L.S("Size in percent", "Størrelse i procent");
            var unit = new Label { Text = "%", AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 6) };
            var capMm = Caption(L.S("W × D × H:", "B × D × H:"));
            sizeValue = new Label { AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 0) };
            grid.Controls.Add(capSize, 0, 0);
            grid.Controls.Add(box, 1, 0);
            grid.Controls.Add(unit, 2, 0);
            grid.Controls.Add(capMm, 0, 1);
            grid.Controls.Add(sizeValue, 1, 1);
            grid.SetColumnSpan(sizeValue, 2);

            info = new InfoBox(ContentW, 9.5f) { Margin = new Padding(0, 0, 0, 14) };

            // Reset to 100 % on the left; Apply (accent) + Cancel on the right
            var bottom = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                Margin = Padding.Empty, Padding = Padding.Empty, Dock = DockStyle.Fill };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnReset = new Button { Text = L.S("Reset to 100 %", "Nulstil til 100 %"), Height = 32, Margin = Padding.Empty, Padding = Padding.Empty,
                                    Anchor = AnchorStyles.Left, UseMnemonic = false };
            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
            btnApply = new Button { Text = L.S("Apply (slices again)", "Anvend (slicer igen)"), Height = 32, Margin = new Padding(0, 0, 6, 0),
                                    Padding = Padding.Empty, UseMnemonic = false };
            btnCancel = new Button { Text = L.S("Cancel", "Annullér"), Size = new Size(90, 32), Margin = Padding.Empty, Padding = Padding.Empty,
                                     DialogResult = DialogResult.Cancel };
            right.Controls.Add(btnApply);
            right.Controls.Add(btnCancel);
            bottom.Controls.Add(btnReset, 0, 0);
            bottom.Controls.Add(right, 1, 0);

            root.Controls.Add(grid);
            root.Controls.Add(info);
            root.Controls.Add(bottom);
            AcceptButton = btnApply;
            CancelButton = btnCancel;

            box.TextChanged += delegate { UpdateState(); };
            box.Enter += delegate { BeginInvoke((MethodInvoker)box.SelectAll); };
            btnReset.Click += delegate { box.Text = "100"; box.Focus(); };
            btnApply.Click += delegate { Apply(); };

            Theme.Apply(this);
            ResumeLayout(true);
            UpdateState();

            Load += delegate
            {
                int w = (int)Math.Round(ContentW * DeviceDpi / 96f);
                info.SetWidth(w);
                root.MinimumSize = new Size(w + root.Padding.Horizontal, 0);
                bottom.MinimumSize = new Size(w, 0);
                // buttons as wide as their text (+24), at least 90; the size column wide enough for the biggest size text
                foreach (var b in new[] { btnReset, btnApply })
                    b.Width = Math.Max(Px(90), TextRenderer.MeasureText(b.Text, b.Font).Width + Px(24));
                btnCancel.Width = Math.Max(Px(90), TextRenderer.MeasureText(btnCancel.Text, btnCancel.Font).Width + Px(24));
                btnCancel.Height = btnApply.Height = btnReset.Height = Px(32);
                // the % box's text lines up with the size text below it (both start at the value column)
                sizeValue.Margin = new Padding(box.Margin.Left, 0, 0, 0);
            };
            Shown += delegate { if (!Program.Hidden && !Screenshots.Active) { box.Focus(); box.SelectAll(); } };
        }

        int Px(int v) { return (int)Math.Round(v * DeviceDpi / 96f); }

        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 6) };
        }

        bool SizeKnown { get { return sizeX > 0 && sizeY > 0 && sizeZ > 0; } }

        /// Reads the box: the size text, the help / warning / error line and Apply follow it.
        void UpdateState()
        {
            double pct;
            bool ok = BambuSlicer.TryParseScale(box.Text, out pct);
            box.BackColor = ok ? Theme.Input : Theme.InvalidBack;
            if (!ok)
            {
                sizeValue.Text = SizeKnown ? "–" : L.S("shown after slicing", "vises efter slicing");
                sizeValue.ForeColor = Theme.Muted;
                info.Text = BambuSlicer.ScaleRangeText();
                info.Kind = InfoKind.Error;
                SetApply(false);
                return;
            }
            bool fits = true;
            if (SizeKnown)
            {
                double x = BambuSlicer.SizeAt(sizeX, currentPct, pct), y = BambuSlicer.SizeAt(sizeY, currentPct, pct),
                       z = BambuSlicer.SizeAt(sizeZ, currentPct, pct);
                sizeValue.Text = BambuSlicer.SizeText(x, y, z);
                sizeValue.ForeColor = Theme.Text;
                fits = BambuSlicer.FitsBed(printer, x, y, z);
            }
            else
            {
                sizeValue.Text = L.S("shown after slicing", "vises efter slicing");
                sizeValue.ForeColor = Theme.Muted;
            }
            if (!fits)
            {
                info.Text = BambuSlicer.TooBigText(printer);
                info.Kind = InfoKind.Warning;
            }
            else
            {
                string help = L.S("The whole job gets this size and is sliced again; time, weight and prices follow.",
                                  "Hele jobbet får denne størrelse og slices igen; tid, vægt og priser følger med.");
                if (objects >= 2)
                    help += "\n" + L.S("Several objects: the size is the biggest one's.", "Flere objekter: størrelsen er for det største.");
                info.Text = help;
                info.Kind = InfoKind.Info;
            }
            SetApply(fits && Math.Abs(pct - currentPct) >= 0.05);
        }

        /// Apply on = accent (red), off = the normal greyed button (accent only when it can be pressed, as Backup's).
        void SetApply(bool on)
        {
            btnApply.Enabled = on;
            Theme.Apply(btnApply);
            if (on) Theme.AccentButton(btnApply);
        }

        void Apply()
        {
            double pct;
            if (!BambuSlicer.TryParseScale(box.Text, out pct) || !btnApply.Enabled) return;
            Pct = pct;
            DialogResult = DialogResult.OK;
        }

        // ---------- picture mode / tests ----------
        internal void SampleType(string text) { box.Text = text; }
        internal string TestSizeText { get { return sizeValue.Text; } }
        internal string TestInfoText { get { return info.Text ?? ""; } }
        internal InfoKind TestInfoKind { get { return info.Kind; } }
        internal bool TestApplyOn { get { return btnApply.Enabled; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }
    }
}
