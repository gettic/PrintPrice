using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Issue #8: "Scale model" for the shown job (one scale for the whole job). Step 2: a % box per axis (X width, Y depth,
    /// Z height; 10-500 %, comma or dot, bad = red box + red line) with "Same in every direction" (on = typing one % sets
    /// all three, as in step 1; forced on with a note when a file can only be scaled uniformly, e.g. STEP), and an mm box
    /// per axis worked out live from the last slice; typing mm sets that axis's % (with the lock on: all three). A warning
    /// when it gets too big for the printer, "Reset to 100 %", "Apply (slices again)" and "Cancel". Apply =
    /// DialogResult.OK with Scale; MainForm slices the job again at that size. Owner: ui.
    internal sealed class ScaleForm : CaptionForm
    {
        const int ContentW = 380;
        readonly TextBox[] pctBox = new TextBox[3], mmBox = new TextBox[3];
        readonly CheckBox chkSame;
        readonly InfoBox info;
        readonly Button btnApply, btnReset, btnCancel;
        readonly double[] size = new double[3], current = new double[3];
        readonly int objects;
        readonly string printer, uniformOnly;
        bool syncing;        // boxes being filled by the window itself (no TextChanged follow-ups)
        int mmTyped = -1;    // the axis whose mm box was typed in last (its text is kept as typed)
        string mmError;      // that mm box's problem (red box + red line), null = fine

        /// The chosen scale (valid after OK).
        public ScaleXyz Scale { get; private set; }
        /// Step 1's single value: X (= all axes when uniform).
        public double Pct { get { return Scale.X; } }

        /// currentScale = the scale the job was sliced at; x / y / z = its measured size at that scale (0 = unknown, e.g. a
        /// sliced file read without slicing); objects = how many were measured (2+ = the size is the biggest one's);
        /// uniformOnly = null when every file can get its own X / Y / Z, else the note why not (the lock stays on).
        public ScaleForm(ScaleXyz currentScale, double x, double y, double z, int objects, string printer, string uniformOnly)
        {
            var cur = currentScale ?? ScaleXyz.None;
            current[0] = cur.X; current[1] = cur.Y; current[2] = cur.Z;
            size[0] = x; size[1] = y; size[2] = z;
            this.objects = objects;
            this.printer = printer;
            this.uniformOnly = uniformOnly;
            Scale = cur;

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

            // [x] Same in every direction
            //  X (width):   [150] %   [ 90,0] mm
            //  Y (depth):   [150] %   [ 46,5] mm
            //  Z (height):  [150] %   [ 72,0] mm
            chkSame = new CheckBox { Text = L.S("Same in every direction", "Ens i alle retninger"), AutoSize = true,
                                     Margin = new Padding(0, 0, 0, uniformOnly != null ? 2 : 8), Checked = uniformOnly != null || cur.IsUniform,
                                     AutoCheck = uniformOnly == null, UseMnemonic = false };
            var grid = new TableLayoutPanel { ColumnCount = 5, RowCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                              Margin = new Padding(0, 0, 0, 2), Padding = Padding.Empty };
            // too small: the muted "Small prints can lose thin parts…" line under the boxes; its line is always kept (empty
            // when not small), so nothing moves when it shows
            smallNote = new Theme.PlainLabel { AutoSize = false, Tag = "muted", UseMnemonic = false, AutoEllipsis = true,
                                               Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 0, 0, 8),
                                               Size = new Size(ContentW, 18) };
            for (int c = 0; c < 5; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            string[] caps = { L.S("X (width):", "X (bredde):"), L.S("Y (depth):", "Y (dybde):"), L.S("Z (height):", "Z (højde):") };
            string[] names = { L.S("width", "bredde"), L.S("depth", "dybde"), L.S("height", "højde") };
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                pctBox[i] = new TextBox { Width = 70, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 0, 6, 6), MaxLength = 8,
                                          Text = Num.Fmt(Math.Round(current[i], 1)) };
                pctBox[i].AccessibleName = L.F("{0} in percent", "{0} i procent", names[i]);
                mmBox[i] = new TextBox { Width = 76, TextAlign = HorizontalAlignment.Right, Margin = new Padding(18, 0, 6, 6), MaxLength = 9 };
                mmBox[i].AccessibleName = L.F("{0} in mm", "{0} i mm", names[i]);
                grid.Controls.Add(Caption(caps[i]), 0, i);
                grid.Controls.Add(pctBox[i], 1, i);
                grid.Controls.Add(Unit("%"), 2, i);
                grid.Controls.Add(mmBox[i], 3, i);
                grid.Controls.Add(Unit("mm"), 4, i);
                pctBox[i].TextChanged += delegate { PctTyped(axis); };
                mmBox[i].TextChanged += delegate { MmTyped(axis); };
                var tb = pctBox[i];
                tb.Enter += delegate { BeginInvoke((MethodInvoker)tb.SelectAll); };
                var mb = mmBox[i];
                mb.Enter += delegate { if (!mb.ReadOnly) BeginInvoke((MethodInvoker)mb.SelectAll); };
                mb.Leave += delegate { if (mmTyped == axis && mmError == null) { mmTyped = -1; UpdateState(); } };   // tidy "90" to "90,0"
            }

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

            root.Controls.Add(chkSame);
            // qol: a file that can only be scaled evenly: its note on its own muted line under the (muted, locked) tick
            if (uniformOnly != null)
            {
                formatNote = new Theme.PlainLabel { Text = uniformOnly, AutoSize = true, Tag = "muted", UseMnemonic = false,
                                                    Font = new Font("Segoe UI", 9f), Margin = new Padding(NoteIndent, 0, 0, 10) };
                root.Controls.Add(formatNote);
            }
            root.Controls.Add(grid);
            root.Controls.Add(smallNote);
            root.Controls.Add(info);
            root.Controls.Add(bottom);
            AcceptButton = btnApply;
            CancelButton = btnCancel;

            chkSame.CheckedChanged += delegate
            {
                // on again: all axes take X's value
                if (chkSame.Checked) SetAll(pctBox[0].Text);
                mmTyped = -1; mmError = null;
                UpdateState();
            };
            btnReset.Click += delegate { mmTyped = -1; mmError = null; SetAll("100"); UpdateState(); pctBox[0].Focus(); };
            btnApply.Click += delegate { Apply(); };

            Theme.Apply(this);
            if (uniformOnly != null)
            {
                chkSame.ForeColor = Theme.Muted;   // locked: it can't be changed (after Apply, which sets Text)
                formatNote.ForeColor = Theme.Muted;
                tip = new ToolTip { OwnerDraw = true, InitialDelay = 300, AutoPopDelay = 15000 };
                tip.Draw += MainForm.DrawDarkTip;
                tip.SetToolTip(chkSame, L.S("Only STL, OBJ and 3MF can be scaled per direction", "Kun STL, OBJ og 3MF kan skaleres pr. retning"));
                Disposed += delegate { tip.Dispose(); };
            }
            ResumeLayout(true);
            UpdateState();

            Load += delegate
            {
                int w = (int)Math.Round(ContentW * DeviceDpi / 96f);
                info.SetWidth(w);
                smallNote.Size = new Size(w, TextRenderer.MeasureText("Ag", smallNote.Font).Height + Px(2));
                if (formatNote != null) formatNote.MaximumSize = new Size(Math.Max(1, w - Px(NoteIndent)), 0);
                // qol: the box is never shorter than the help text, so an error line doesn't move the buttons
                string shown = info.Text;
                var kind = info.Kind;
                info.Text = HelpText();
                int helpH = info.GetPreferredSize(new Size(w, 0)).Height;
                info.Text = shown;
                info.Kind = kind;
                info.MinimumSize = new Size(w, helpH);
                root.MinimumSize = new Size(w + root.Padding.Horizontal, 0);
                bottom.MinimumSize = new Size(w, 0);
                // buttons as wide as their text (+24), at least 90
                foreach (var b in new[] { btnReset, btnApply })
                    b.Width = Math.Max(Px(90), TextRenderer.MeasureText(b.Text, b.Font).Width + Px(24));
                btnCancel.Width = Math.Max(Px(90), TextRenderer.MeasureText(btnCancel.Text, btnCancel.Font).Width + Px(24));
                btnCancel.Height = btnApply.Height = btnReset.Height = Px(32);
            };
            Shown += delegate { if (!Program.Hidden && !Screenshots.Active) { pctBox[0].Focus(); pctBox[0].SelectAll(); } };
        }

        int Px(int v) { return (int)Math.Round(v * DeviceDpi / 96f); }

        /// The InfoBox help (the file-format note is on its own muted line under the tick, not in here).
        string HelpText()
        {
            string help = L.S("The whole job gets this size and is sliced again; time, weight and prices follow.",
                              "Hele jobbet får denne størrelse og slices igen; tid, vægt og priser følger med.");
            if (!SizeKnown)
                help += "\n" + L.S("The size in mm is shown after slicing.", "Størrelsen i mm vises efter slicing.");
            else if (objects >= 2)
                help += "\n" + L.S("Several objects: the size is the biggest one's.", "Flere objekter: størrelsen er for det største.");
            return help;
        }

        const int NoteIndent = 18;   // the tick's text start (box 13 + gap), so the note lines up under "Same in every direction"
        Label formatNote;            // the uniformOnly note (null = none)
        Label smallNote;             // too small: BambuSlicer.SmallScaleNote() when IsSmallSize, else "" (its line always kept)
        ToolTip tip;                 // the locked tick's dark tooltip

        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Theme.LabelGap, 6) };
        }

        static Label Unit(string text)
        {
            // qol: drawn without GDI's side padding, so the unit's text starts 6 px after the box frame (the box's right margin)
            return new SettingsForm.UnitLabel { Text = text, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 6) };
        }

        /// The size at pct fits the printer's bed (size = mm at the current % per axis; any size unknown = fits). Shared by the
        /// window's Apply and --test-slice's bed check, so the two can't drift.
        internal static bool FitsAt(string printer, double[] size, double[] current, double[] pct)
        {
            if (size == null || size.Length < 3 || !(size[0] > 0) || !(size[1] > 0) || !(size[2] > 0)) return true;
            return BambuSlicer.FitsBed(printer, BambuSlicer.SizeAt(size[0], current[0], pct[0]), BambuSlicer.SizeAt(size[1], current[1], pct[1]),
                                       BambuSlicer.SizeAt(size[2], current[2], pct[2]));
        }

        bool SizeKnown { get { return size[0] > 0 && size[1] > 0 && size[2] > 0; } }
        bool Locked { get { return chkSame.Checked; } }

        /// The axis's size at 100 % (mm).
        double Size100(int i) { return BambuSlicer.SizeAt(size[i], current[i], 100); }

        void SetAll(string pctText)
        {
            syncing = true;
            foreach (var b in pctBox) b.Text = pctText;
            syncing = false;
        }

        void PctTyped(int axis)
        {
            if (syncing) return;
            mmTyped = -1; mmError = null;
            if (Locked)
            {
                syncing = true;
                for (int i = 0; i < 3; i++) if (i != axis) pctBox[i].Text = pctBox[axis].Text;
                syncing = false;
            }
            UpdateState();
        }

        /// Typed mm: that axis's % = mm / its size at 100 % (lock on: every axis); outside 10-500 % = red box + line.
        void MmTyped(int axis)
        {
            if (syncing || !SizeKnown) return;
            mmTyped = axis; mmError = null;
            double mm;
            double s100 = Size100(axis);
            double lo = BambuSlicer.SizeAt(s100, 100, ScaleXyz.Min), hi = BambuSlicer.SizeAt(s100, 100, ScaleXyz.Max);
            if (!Num.TryParse(mmBox[axis].Text, out mm) || mm <= 0 || s100 <= 0)
                mmError = L.S("Type the size in mm as a number, e.g. 90 or 90,5.", "Skriv størrelsen i mm som et tal, fx 90 eller 90,5.");
            else
            {
                double p = Math.Round(mm / s100 * 100, 1, MidpointRounding.AwayFromZero);
                if (p < ScaleXyz.Min || p > ScaleXyz.Max)
                {
                    // the % box shows what this mm would need (red too), so the two boxes never disagree
                    string[] axisEn = { "Width", "Depth", "Height" }, axisDa = { "Bredden", "Dybden", "Højden" };
                    mmError = L.F("{0} must be {1}–{2} mm (10–500 %).", "{0} skal være {1}–{2} mm (10–500 %).",
                                  L.S(axisEn[axis], axisDa[axis]), Num.Fmt(Math.Ceiling(lo * 10) / 10), Num.Fmt(Math.Floor(hi * 10) / 10));
                    syncing = true;
                    for (int i = 0; i < 3; i++) if (i == axis || Locked) pctBox[i].Text = Num.Fmt(Math.Round(p));
                    syncing = false;
                }
                else
                {
                    syncing = true;
                    for (int i = 0; i < 3; i++) if (i == axis || Locked) pctBox[i].Text = Num.Fmt(p);
                    syncing = false;
                }
            }
            UpdateState();
        }

        /// Reads the boxes: the mm boxes, the help / warning / error line and Apply follow them.
        void UpdateState()
        {
            var pct = new double[3];
            bool allOk = true;
            for (int i = 0; i < 3; i++)
            {
                bool ok = BambuSlicer.TryParseScale(pctBox[i].Text, out pct[i]);
                pctBox[i].BackColor = ok && !(mmError != null && (i == mmTyped || Locked)) ? Theme.Input : Theme.InvalidBack;
                allOk &= ok;
            }
            syncing = true;
            for (int i = 0; i < 3; i++)
            {
                var mb = mmBox[i];
                mb.ReadOnly = !SizeKnown;
                mb.TabStop = SizeKnown;
                mb.BackColor = i == mmTyped && mmError != null ? Theme.InvalidBack : Theme.Input;
                if (!SizeKnown) { mb.Text = "–"; mb.ForeColor = Theme.Muted; continue; }
                mb.ForeColor = Theme.Text;
                if (i == mmTyped) continue;   // kept as typed
                double p;
                bool ok = BambuSlicer.TryParseScale(pctBox[i].Text, out p);
                mb.Text = ok ? Num.Fmt(Math.Round(BambuSlicer.SizeAt(size[i], current[i], p), 1)) : "–";
            }
            syncing = false;
            // too small (plans\too-small): a soft muted note when the smallest side would be under 10 mm (never blocks)
            bool small = SizeKnown && allOk && mmError == null &&
                         BambuSlicer.IsSmallSize(BambuSlicer.SizeAt(size[0], current[0], pct[0]), BambuSlicer.SizeAt(size[1], current[1], pct[1]),
                                                 BambuSlicer.SizeAt(size[2], current[2], pct[2]));
            smallNote.Text = small ? BambuSlicer.SmallScaleNote() : "";

            if (mmError != null)
            {
                info.Text = mmError;
                info.Kind = InfoKind.Error;
                SetApply(false);
                return;
            }
            if (!allOk)
            {
                info.Text = BambuSlicer.ScaleRangeText();
                info.Kind = InfoKind.Error;
                SetApply(false);
                return;
            }
            bool fits = FitsAt(printer, size, current, pct);
            if (!fits)
            {
                info.Text = BambuSlicer.TooBigText(printer);
                info.Kind = InfoKind.Warning;
            }
            else
            {
                info.Text = HelpText();
                info.Kind = InfoKind.Info;
            }
            bool changed = false;
            for (int i = 0; i < 3; i++) changed |= Math.Abs(pct[i] - current[i]) >= 0.05;
            SetApply(fits && changed);
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
            if (!btnApply.Enabled) return;
            var p = new double[3];
            for (int i = 0; i < 3; i++) if (!BambuSlicer.TryParseScale(pctBox[i].Text, out p[i])) return;
            Scale = Locked ? ScaleXyz.Uniform(p[0]) : new ScaleXyz(p[0], p[1], p[2]);
            DialogResult = DialogResult.OK;
        }

        // ---------- picture mode / tests ----------
        /// Typed into X (with the lock on, as step 1: all three follow).
        internal void SampleType(string text) { pctBox[0].Text = text; }
        /// A whole scale: uniform = the lock on and X typed; else the lock off (when allowed) and each axis typed.
        internal void SampleType(ScaleXyz s)
        {
            if (s == null) return;
            if (s.IsUniform || uniformOnly != null) { chkSame.Checked = true; pctBox[0].Text = Num.Fmt(Math.Round(s.X, 1)); return; }
            chkSame.Checked = false;
            pctBox[0].Text = Num.Fmt(Math.Round(s.X, 1));
            pctBox[1].Text = Num.Fmt(Math.Round(s.Y, 1));
            pctBox[2].Text = Num.Fmt(Math.Round(s.Z, 1));
        }
        /// mm typed into one axis's box (0 = X, 1 = Y, 2 = Z).
        internal void SampleTypeMm(int axis, double mm) { mmBox[axis].Text = Num.Fmt(Math.Round(mm, 1)); }
        internal string TestSizeText
        {
            get { return SizeKnown ? mmBox[0].Text + " × " + mmBox[1].Text + " × " + mmBox[2].Text + " mm" : "–"; }
        }
        internal string TestPctText { get { return pctBox[0].Text + " / " + pctBox[1].Text + " / " + pctBox[2].Text; } }
        internal string TestInfoText { get { return info.Text ?? ""; } }
        internal string TestSmallNote { get { return smallNote.Text ?? ""; } }
        internal InfoKind TestInfoKind { get { return info.Kind; } }
        internal bool TestApplyOn { get { return btnApply.Enabled; } }
        internal bool TestLockOn { get { return chkSame.Checked; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }
    }
}
