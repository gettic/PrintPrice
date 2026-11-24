using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Batch & plates (issue #9 / #10): "Copies" for the shown job, built like the Scale window. How many (1..MaxCopies)
    /// and, for a job typed by hand, how many build plates. Modes: Slice = Bambu Studio places as many as fit on each
    /// plate and slices again; Multiply = a sliced file / G-code printed N times (no slicing); Typed = the main window's
    /// time and weight are one copy's (Pricing.ForCopies), the plates are typed. "Back to 1" left, "Apply (slices again)"
    /// (accent only when it can be pressed) + Cancel right. Apply above WarnCopies (Slice mode) asks first. Owner: ui.
    internal sealed class CopiesForm : CaptionForm
    {
        internal enum Mode { Slice, Multiply, Typed }

        const int ContentW = 380;
        readonly TextBox txtCopies, txtPlates;
        readonly InfoBox info;
        readonly Button btnApply, btnReset, btnCancel;
        readonly Mode mode;
        readonly int currentCopies, currentPlates, files;

        /// The chosen copies (valid after OK).
        public int Copies { get; private set; }
        /// Typed mode: the chosen build plates (valid after OK); else 0.
        public int Plates { get; private set; }
        /// Set by tests: the "may take several minutes" question's answer (null = ask with the real window).
        internal bool? TestBigAnswer;
        /// The question was asked on the last Apply (tests).
        internal bool TestBigAsked { get; private set; }

        /// copies / plates = the shown job's (plates 0 = unknown); files = files in the job (2+: every file gets this many).
        public CopiesForm(Mode mode, int copies, int plates, int files)
        {
            this.mode = mode;
            currentCopies = Math.Max(1, copies);
            currentPlates = Math.Max(1, plates);
            this.files = Math.Max(1, files);
            Copies = currentCopies;
            Plates = mode == Mode.Typed ? currentPlates : 0;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = L.S("Copies", "Antal");
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

            //  How many:      [  40 ]  copies
            //  Build plates:  [   2 ]           (typed by hand only)
            var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                              Margin = new Padding(0, 0, 0, 8), Padding = Padding.Empty };
            for (int c = 0; c < 3; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            txtCopies = new TextBox { Width = 70, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 0, 6, 6), MaxLength = 4,
                                      Text = currentCopies.ToString() };
            txtCopies.AccessibleName = L.S("How many copies", "Hvor mange stk.");
            grid.Controls.Add(Caption(files > 1 ? L.S("Of each file:", "Af hver fil:") : L.S("How many:", "Hvor mange:")), 0, 0);
            grid.Controls.Add(txtCopies, 1, 0);
            grid.Controls.Add(Unit(L.S("copies", "stk.")), 2, 0);
            if (mode == Mode.Typed)
            {
                txtPlates = new TextBox { Width = 70, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 0, 6, 6), MaxLength = 4,
                                          Text = currentPlates.ToString() };
                txtPlates.AccessibleName = L.S("Build plates", "Byggeplader");
                grid.Controls.Add(Caption(L.S("Build plates:", "Byggeplader:")), 0, 1);
                grid.Controls.Add(txtPlates, 1, 1);
                grid.Controls.Add(Unit(""), 2, 1);
                txtPlates.TextChanged += delegate { UpdateState(); };
                txtPlates.Enter += delegate { if (!Screenshots.Active) BeginInvoke((MethodInvoker)txtPlates.SelectAll); };
            }
            txtCopies.TextChanged += delegate { UpdateState(); };
            txtCopies.Enter += delegate { if (!Screenshots.Active) BeginInvoke((MethodInvoker)txtCopies.SelectAll); };

            info = new InfoBox(ContentW, 9.5f) { Margin = new Padding(0, 0, 0, 14) };

            var bottom = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                Margin = Padding.Empty, Padding = Padding.Empty, Dock = DockStyle.Fill };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnReset = new Button { Text = L.S("Back to 1", "Tilbage til 1"), Height = 32, Margin = Padding.Empty, Padding = Padding.Empty,
                                    Anchor = AnchorStyles.Left, UseMnemonic = false };
            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
            btnApply = new Button { Text = mode == Mode.Slice ? L.S("Apply (slices again)", "Anvend (slicer igen)") : L.S("Apply", "Anvend"),
                                    Height = 32, Margin = new Padding(0, 0, 6, 0), Padding = Padding.Empty, UseMnemonic = false };
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

            btnReset.Click += delegate
            {
                txtCopies.Text = "1";
                if (txtPlates != null) txtPlates.Text = "1";
                txtCopies.Focus();
            };
            btnApply.Click += delegate { Apply(); };

            Theme.Apply(this);
            ResumeLayout(true);
            UpdateState();

            Load += delegate
            {
                int w = Px(ContentW);
                info.SetWidth(w);
                // the box is never shorter than the help text, so a warning / error line doesn't move the buttons
                string shown = info.Text;
                var kind = info.Kind;
                info.Text = HelpText(Math.Max(2, currentCopies));
                int helpH = info.GetPreferredSize(new Size(w, 0)).Height;
                info.Text = shown;
                info.Kind = kind;
                info.MinimumSize = new Size(w, helpH);
                root.MinimumSize = new Size(w + root.Padding.Horizontal, 0);
                bottom.MinimumSize = new Size(w, 0);
                foreach (var b in new[] { btnReset, btnApply, btnCancel })
                    b.Width = Math.Max(Px(90), TextRenderer.MeasureText(b.Text, b.Font).Width + Px(24));
                btnCancel.Height = btnApply.Height = btnReset.Height = Px(32);
            };
            Shown += delegate { if (!Program.Hidden && !Screenshots.Active) { txtCopies.Focus(); txtCopies.SelectAll(); } };
        }

        int Px(int v) { return (int)Math.Round(v * DeviceDpi / 96f); }

        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Theme.LabelGap + 4, 6) };   // qol #9: ~30 px to the box, as in the Scale window
        }

        static Label Unit(string text)
        {
            return new SettingsForm.UnitLabel { Text = text, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 6) };
        }

        /// The help line for n copies (by mode).
        string HelpText(int n)
        {
            switch (mode)
            {
                case Mode.Multiply:
                    // ui-text #3
                    return L.S("A sliced file is printed this many times, one after the other. Time, weight and price are for all copies.",
                               "En slicet fil printes så mange gange efter hinanden. Tid, vægt og pris gælder alle stk.");
                case Mode.Typed:
                    // ui-text #2 / qol #6
                    return L.S("Time and weight in the main window are for one copy. The price is for all copies.",
                               "Tid og vægt i hovedvinduet er for ét stk. Prisen gælder alle stk.");
                default:
                    string help = L.S("Bambu Studio puts as many copies as fit on each build plate and slices again. Time, weight and price are for all copies.",
                                      "Bambu Studio lægger så mange stk. som muligt på hver byggeplade og slicer igen. Tid, vægt og pris gælder alle stk.");
                    if (files > 1)
                        help += "\n" + L.S("For a different number per file, load the files again.",   // ui-text #11
                                           "Forskelligt antal pr. fil: indlæs filerne igen.");
                    return help;
            }
        }

        bool ReadCopies(out int n)
        {
            n = 0;
            string t = (txtCopies.Text ?? "").Trim();
            return int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n)
                   && BambuSlicer.CopiesInRange(n) && (long)n * files <= BambuSlicer.MaxCopies;
        }

        bool ReadPlates(int copies, out int p)
        {
            p = 1;
            if (txtPlates == null) return true;
            string t = (txtPlates.Text ?? "").Trim();
            return int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out p)
                   && p >= 1 && p <= Math.Max(1, copies);
        }

        /// Reads the boxes: the help / warning / error line and Apply follow them.
        void UpdateState()
        {
            int n, p = 1;
            bool okN = ReadCopies(out n);
            bool okP = !okN || ReadPlates(n, out p);
            txtCopies.BackColor = okN ? Theme.Input : Theme.InvalidBack;
            if (txtPlates != null) txtPlates.BackColor = okP ? Theme.Input : Theme.InvalidBack;
            if (!okN)
            {
                info.Text = files > 1   // ui-text #6
                    ? L.F("All {1} files together: 1 to {0} copies.", "Alle {1} filer tilsammen: 1 til {0} stk.", BambuSlicer.MaxCopies, files)
                    : BambuSlicer.CopiesRangeText();
                info.Kind = InfoKind.Error;
                SetApply(false);
                return;
            }
            if (!okP)
            {
                info.Text = L.F("Build plates: 1 to {0} (at least one copy on each).", "Byggeplader: 1 til {0} (mindst ét stk. på hver).", n);   // ui-text #7
                info.Kind = InfoKind.Error;
                SetApply(false);
                return;
            }
            if (mode == Mode.Slice && (long)n * files > BambuSlicer.WarnCopies)
            {
                info.Text = BigText((int)Math.Min(int.MaxValue, (long)n * files));
                info.Kind = InfoKind.Warning;
            }
            else
            {
                info.Text = HelpText(n);
                info.Kind = InfoKind.Info;
            }
            SetApply(n != currentCopies || (mode == Mode.Typed && p != currentPlates));
        }

        /// "Slicing 300 copies can take several minutes." (Slice mode above WarnCopies)
        static string BigText(int n)
        {
            return L.F("Slicing {0} copies can take several minutes.", "Det kan tage flere minutter at slice {0} stk.", n);
        }

        /// Apply on = accent (red), off = the normal greyed button.
        void SetApply(bool on)
        {
            btnApply.Enabled = on;
            Theme.Apply(btnApply);
            if (on) Theme.AccentButton(btnApply);
        }

        void Apply()
        {
            int n, p = 1;
            if (!btnApply.Enabled || !ReadCopies(out n) || !ReadPlates(n, out p)) return;
            TestBigAsked = false;
            if (mode == Mode.Slice && (long)n * files > BambuSlicer.WarnCopies)
            {
                // big batches: ask first (the decision: "may take several minutes, go on?")
                TestBigAsked = true;
                bool go;
                if (TestBigAnswer.HasValue) go = TestBigAnswer.Value;
                else
                    using (var ask = NewBigAsk((int)((long)n * files)))
                        go = ask.ShowDialog(this) == DialogResult.Yes;
                if (!go) return;
            }
            Copies = n;
            Plates = mode == Mode.Typed ? p : 0;
            DialogResult = DialogResult.OK;
        }

        /// "Slice 300 copies?" (AskForm look: Slice / Cancel; Cancel default and Esc). Also the file picker's question.
        internal static BackupForm.AskForm NewBigAsk(int n)
        {
            return new BackupForm.AskForm(L.S("Copies", "Antal"),
                L.F("Slice {0} copies?", "Slice {0} stk.?", n),
                L.S("Bambu Studio may take several minutes. You can cancel while it slices.",
                    "Bambu Studio kan bruge flere minutter. Du kan annullere undervejs."),
                L.S("Slice", "Slice"), L.S("Cancel", "Annullér"));
        }

        // ---------- picture mode / tests ----------
        // picture mode: nothing marked (Windows' blue selection); the caret after the text, as when typing
        internal void SampleType(int copies) { txtCopies.Text = copies.ToString(); txtCopies.Select(txtCopies.Text.Length, 0); }
        internal void SampleTypePlates(int plates) { if (txtPlates != null) { txtPlates.Text = plates.ToString(); txtPlates.Select(txtPlates.Text.Length, 0); } }
        internal string TestInfoText { get { return info.Text ?? ""; } }
        internal InfoKind TestInfoKind { get { return info.Kind; } }
        internal bool TestApplyOn { get { return btnApply.Enabled; } }
        internal bool TestHasPlates { get { return txtPlates != null; } }
        /// The real Apply button (shown first: a hidden button can't be clicked); true = the window ended with OK.
        internal bool TestPressApply(IWin32Window owner)
        {
            if (!Visible) Show(owner);
            Application.DoEvents();
            // PerformClick does nothing until the button can be selected (seen once: a run right after a slice); wait up to 1 s
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!btnApply.CanSelect && btnApply.Enabled && sw.ElapsedMilliseconds < 1000) { System.Threading.Thread.Sleep(10); Application.DoEvents(); }
            btnApply.PerformClick();
            Application.DoEvents();
            return DialogResult == DialogResult.OK;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }
    }
}
