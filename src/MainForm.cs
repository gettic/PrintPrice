using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintPrice
{
    public class MainForm : CaptionForm
    {
        AppSettings settings;
        TextBox txtUrl, txtHours, txtMinutes, txtGrams, txtElecPrice;
        Label lblPower, lblFailed, lblFailedCap, lblBase;
        ThemedGroupBox grpBreak;
        int breakBaseHeight, breakRowHeight;   // Cost breakdown's designed 3-row height, and one (fixed) breakdown row (scaled, set on Load)
        int plainRowHeight;                    // one plain label row (label + margins) as the designed height counts it
        TableLayoutPanel breakTable;           // the Cost breakdown's table (rows: 0 material rows, 1 purge, 2 Power, 3 Failed, 4 Base, 5 payback)
        Label lblFriends, lblOthers, lblFriendsInfo, lblOthersInfo, lblStatus;
        Button btnCopy, btnUpdate;
        UpdateInfo pendingUpdate;   // newer version found at startup (null = none)
        System.Windows.Forms.Timer updateTimer;   // quiet check every 10 min while running (null in picture mode)
        bool checkingUpdate, updateDialogOpen;
        QuoteResult last;
        QuoteInput lastInput;   // the input behind last (saved to the history)
        int lastHours, lastMinutes;
        bool statusIsSuccess;
        System.Windows.Forms.Timer statusTimer;   // clears a green ✓ status line after a few seconds

        // price history (history.json next to settings.ini; loaded on first use, never in picture mode)
        Button btnHistory, btnSave;
        List<QuoteRecord> history;
        string historyProblem;          // load problem, shown once in the history window
        string jobModel;                // model name of the current job (Printables name / file name), null = none
        double jobPurge = -1;           // purge grams of the current multi-colour job (-1 = unknown)
        QuoteRecord reopened;           // quote opened from the history (its line stays in the model info box)

        // failed prints payback (idea 9): the record this job saved is left out of the debt, so saving it again keeps the price
        string savedId;                 // Id of the history record the current job saved (null = none yet); cleared by a new job, ClearJob, Reopen
        double sampleDebt;              // picture mode only: made-up debt (history is empty there)
        Label lblPaybackCap, lblPayback;   // Cost breakdown row "Failed prints payback", shown only while a payback is added
        bool paybackRowOn;

        // auto-detect
        Button btnAnalyze, btnLoadFile;
        InfoBox lblAuto;
        bool autoIsError;
        CancellationTokenSource cts;

        // model picture under the "Model link" label (logo + "No model yet" while empty)
        PictureBox picModel;
        int picRequest;   // bumped for every new job / Clear, so a late picture of an old job is dropped
        int picBaseHeight;   // buttons' top to a 2-line info box's bottom (scaled, set on Load)
        bool jobLoaded;      // a model is loaded (analyzed or reopened): no picture = logo only, without "No model yet"

        // material rows in the Cost breakdown: swatch, material drop-down, grams, × kr/kg box, cost. One colour = one row
        // (its grams follow "Filament used"); 2+ colours = one row per colour + purge line, "Filament used" = read-only total
        const int MaxColourRows = 4;   // AMS lite has 4 slots; with more, row 4 holds all the rest ("+N more")
        List<FilamentUse> filaments;   // null = one filament (row 0 = `single`)
        FilamentUse single;            // the one-colour row's material (Profile = name, Colour, Type); null while 2+ colours
        int colourRows;                // rows in use (1 for one colour)
        readonly List<int>[] rowItems = new List<int>[MaxColourRows];   // indices into filaments per row (2+ colours)
        readonly Control[][] rowControls = new Control[MaxColourRows][];
        readonly Swatch[] rowSwatch = new Swatch[MaxColourRows];
        readonly ComboBox[] rowCombo = new ComboBox[MaxColourRows];
        readonly Label[] rowGrams = new Label[MaxColourRows], rowCost = new Label[MaxColourRows], rowUnit = new Label[MaxColourRows];
        readonly TextBox[] rowPrice = new TextBox[MaxColourRows];
        TableLayoutPanel colourGrid;
        Label lblPurge;
        bool fillingColours;   // no recalculation / remembered prices while the rows are being filled
        List<string> filamentNames;      // A1 filament profiles of Bambu Studio (short names, as in Settings), cached
        string filamentNamesKey;         // slicer exe + nozzle size the list was read for
        int filamentNamesWidth;          // widest name (px), for the drop-down list width

        // live electricity price
        Button btnPower;
        InfoBox lblPowerInfo;
        bool powerIsError, fetchingPower;

        // printer card (photo + "Bambu Lab A1 · 100 W"); later the place to pick another printer
        PrinterCard printerCard;
        ToolTip printerTip;
        string printerPhotoExe = "?";   // slicer exe the card's photo was looked up for ("?" = not yet)
        bool printerCardReady;          // set on Load: sizes are scaled and the column widths known

        // picture mode (--screenshots): never saves, never goes online
        readonly bool offline;

        public MainForm() : this(AppSettings.Load(), false) { }

        public MainForm(AppSettings startSettings, bool offline)
        {
            this.offline = offline;
            settings = startSettings;
            if (settings.AutoWearRate != 0) settings.MachineRatePerHour = AutoWearRate(settings);
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = AppInfo.Name + (AppSettings.TestMode && !offline ? " [TEST]" : "");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterScreen;
            // test mode: --hidden = off-screen from the first frame, no taskbar button; --minimized = starts minimized
            if (Program.Hidden && !offline) Screenshots.Place(this);
            else if (Program.Minimized && !offline) WindowState = FormWindowState.Minimized;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // GrowAndShrink: the window shrinks back when the status row hides again
            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14), Margin = Padding.Empty };
            Controls.Add(root);

            // ---------- Inputs ----------
            var inputs = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            // label column as wide as the longest label + Theme.LabelGap (~30 px gap to the boxes)
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // link box, buttons and info boxes end at the Cost breakdown's right edge (widths set on Load, see below)
            // all text in the link box is centred: typed / pasted / loaded links and file names, like the grey example and red hint
            // (a link longer than the box scrolls like a left-aligned one: set from code, the caret is at 0 so its start shows)
            txtUrl = new HintBox { Width = 394, Margin = new Padding(3, 3, 0, 3), TextAlign = HorizontalAlignment.Center };
            // grey example text while empty; re-applied whenever the handle is recreated (e.g. theme change)
            txtUrl.HandleCreated += delegate { ApplyUrlCue(); };
            // typing/pasting brings the grey example text back
            txtUrl.TextChanged += delegate { SetUrlHint(false); jobModel = null; };   // a new link = no model name yet
            AddRow(inputs, L.S("Model link", "Link til model"), txtUrl, null);
            inputs.SetColumnSpan(txtUrl, 2);

            // three equal buttons lined up exactly under the link box (widths set on Load)
            btnAnalyze = new Button { Text = AnalyzeText, Width = 128, Height = 32, Margin = new Padding(3, 6, 6, 4) };
            btnLoadFile = new Button { Text = L.S("Load file…", "Åbn fil…"), Width = 128, Height = 32, Margin = new Padding(0, 6, 6, 4) };
            btnHistory = new Button { Text = L.S("History…", "Historik…"), Width = 128, Height = 32, Margin = new Padding(0, 6, 0, 4) };
            btnAnalyze.Click += delegate { AnalyzeClicked(); };
            btnLoadFile.Click += delegate { BrowseForModel(); };
            btnHistory.Click += delegate { OpenHistory(); };
            // Enter in the link box = "Get time & weight" (not while a job runs, so it never cancels by accident)
            txtUrl.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None) return;
                e.SuppressKeyPress = true;   // no beep
                if (cts != null) return;
                if (txtUrl.Text.Trim().Trim('"').Length == 0)
                {
                    // empty box + Enter = try the example (the button still shows the red hint)
                    txtUrl.Text = ExampleLink;
                    txtUrl.SelectionStart = 0;   // caret at the start: a link longer than the box shows its beginning
                }
                AnalyzeClicked();
            };
            var autoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            autoRow.Controls.Add(btnAnalyze);
            autoRow.Controls.Add(btnLoadFile);
            autoRow.Controls.Add(btnHistory);
            int picRow = inputs.RowCount;
            AddRow(inputs, "", autoRow, null);
            inputs.SetColumnSpan(autoRow, 2);
            lblAuto = new InfoBox(394, 9.5f) { Margin = new Padding(3, 4, 0, 10) };
            AddRow(inputs, "", lblAuto, null);
            // model picture in the label column, beside the buttons and the info box (size measured on Load)
            picModel = new PictureBox
            {
                BackColor = Theme.Input, Size = new Size(10, 10),   // the model picture is drawn in PaintModelPicture (Zoom ignores Padding)
                Padding = new Padding(4), Margin = Padding.Empty, Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            picModel.Paint += (s, e) => PaintModelPicture(e.Graphics);
            inputs.Controls.Add(picModel, 0, picRow);
            inputs.SetRowSpan(picModel, 2);
            // the box stays put: buttons and info box keep to the top of their rows, the info box is at least 2 lines tall
            autoRow.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblAuto.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            inputs.SetColumnSpan(lblAuto, 2);

            txtHours = new TextBox { Width = 50, Text = "0", TextAlign = HorizontalAlignment.Right };
            txtMinutes = new TextBox { Width = 50, Text = "0", TextAlign = HorizontalAlignment.Right };
            var timeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            timeRow.Controls.Add(txtHours);
            timeRow.Controls.Add(Unit(L.S("h", "t")));
            timeRow.Controls.Add(txtMinutes);
            timeRow.Controls.Add(Unit("m"));
            int timeRowIndex = inputs.RowCount;
            AddRow(inputs, L.S("Print time", "Printtid"), timeRow, null);
            Load += delegate
            {
                // number boxes below end exactly where the m box ends (DPI-safe, measured)
                int right = txtMinutes.Parent.PointToScreen(new Point(txtMinutes.Right, 0)).X;
                foreach (var tb in new[] { txtGrams, txtElecPrice })
                    tb.Width = right - tb.Parent.PointToScreen(new Point(tb.Left, 0)).X;
            };

            txtGrams = NumBox("");
            AddRow(inputs, L.S("Filament used", "Filamentforbrug"), WithUnit(txtGrams, "g"), null);
            // the filament's kr/kg is in its material row in the Cost breakdown (no "Filament price" row here)
            txtElecPrice = NumBox(Num.Fmt(settings.LastElectricityPrice));
            var elecRow = WithUnit(txtElecPrice, "kr/kWh");
            AddRow(inputs, L.S("Electricity price", "Elpris"), elecRow, null);
            lblPowerInfo = new InfoBox(394, 9.5f) { Margin = new Padding(3, 2, 0, 8) };
            int powerInfoRow = inputs.RowCount;
            AddRow(inputs, "", lblPowerInfo, null);
            inputs.SetColumnSpan(lblPowerInfo, 2);
            // "Update" sits in the label column under "Electricity price", its top level with the live-price info box's top
            // (same top margin); height matched to the number boxes once the form has loaded (DPI-safe).
            // Slightly smaller font + no padding so the text fits inside a button only as tall as a text box.
            // The info box is always shown (empty = just the box with its stripe, one line tall), so the button and the
            // layout never move.
            lblPowerInfo.KeepWhenEmpty = true;
            btnPower = new Button
            {
                Text = L.S("Update", "Opdatér"), AutoSize = false, Width = 76, Margin = new Padding(0, 2, 0, 3),
                Padding = Padding.Empty, Font = new Font("Segoe UI", 9f), TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            Load += delegate
            {
                lblPowerInfo.KeepWhenEmpty = true;   // again after DPI scaling: the empty line's height is measured now
                // as tall as the one-line live-price box beside it, top and bottom level with it
                btnPower.Height = lblPowerInfo.OneLineHeight;
                btnPower.Margin = new Padding(0, lblPowerInfo.Margin.Top, 0, btnPower.Margin.Bottom);
            };
            btnPower.Click += delegate { FetchPowerPrice(); };
            inputs.Controls.Add(btnPower, 0, powerInfoRow);
            lblPowerInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            // printer card in the free space right of the Print time … Electricity rows (see "Printer card")
            printerCard = new PrinterCard { Size = new Size(161, 80), Margin = Padding.Empty, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            printerCard.Click += delegate { OpenSettings(); };
            printerTip = new ToolTip { OwnerDraw = true, InitialDelay = 400 };
            printerTip.Draw += DrawDarkTip;
            printerTip.SetToolTip(printerCard, L.S("Change in Settings", "Ret i Indstillinger"));
            printerCard.AccessibleDescription = L.S("Change in Settings", "Ret i Indstillinger");
            inputs.Controls.Add(printerCard, 2, timeRowIndex);
            inputs.SetRowSpan(printerCard, 3);   // Print time, Filament used, Electricity price
            root.Controls.Add(inputs);

            // ---------- Breakdown ----------
            grpBreak = new ThemedGroupBox { Text = L.S("Cost breakdown", "Omkostninger"), Width = 540, Height = 110, Margin = new Padding(0, 0, 0, 8) };
            var bt = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2) };
            breakTable = bt;
            for (int i = 0; i < 6; i++) bt.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // rows 2-5 fixed on Load (SetBreakRows)
            bt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            bt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            lblPower = BreakLabel(); lblFailed = BreakLabel(); lblBase = BreakLabel();
            // material rows (one per colour, at least one) + a muted purge line for 2+ colours (SetColours)
            colourGrid = BuildColourGrid();
            bt.Controls.Add(colourGrid, 0, 0); bt.SetColumnSpan(colourGrid, 2);
            lblPurge = new Theme.PlainLabel { AutoSize = true, Tag = "muted", Font = new Font("Segoe UI", 9f), UseMnemonic = false, Margin = new Padding(3, 0, 3, 4), Visible = false };
            bt.Controls.Add(lblPurge, 0, 1); bt.SetColumnSpan(lblPurge, 2);
            bt.Controls.Add(BreakCaption(L.S("Power", "Strøm")), 0, 2); bt.Controls.Add(lblPower, 1, 2);
            // failed prints: hidden (row collapses, box shrinks) when the setting is 0 %, see UpdateFailedRow
            lblFailedCap = BreakCaption("");
            bt.Controls.Add(lblFailedCap, 0, 3); bt.Controls.Add(lblFailed, 1, 3);
            var baseCap = BreakCaption(L.S("Base cost", "Kostpris")); baseCap.Font = new Font(Font, FontStyle.Bold);
            lblBase.Font = new Font(Font, FontStyle.Bold);
            bt.Controls.Add(baseCap, 0, 4); bt.Controls.Add(lblBase, 1, 4);
            // failed prints payback: added after markup (not part of the base cost), hidden while 0, see UpdatePaybackRow
            lblPaybackCap = BreakCaption(""); lblPayback = BreakLabel();
            lblPaybackCap.Visible = lblPayback.Visible = false;
            bt.Controls.Add(lblPaybackCap, 0, 5); bt.Controls.Add(lblPayback, 1, 5);
            grpBreak.Controls.Add(bt);
            root.Controls.Add(grpBreak);
            Load += delegate
            {
                // scaled sizes: the box's designed 3-row height and one plain row (label + margins)
                breakBaseHeight = grpBreak.Height;
                plainRowHeight = lblPower.Height + lblPower.Margin.Vertical;
                breakRowHeight = BreakRowHeight();
                // material drop-downs exactly as tall as the kr/kg boxes (owner-drawn: height = ItemHeight + frame, measured)
                foreach (var cb in rowCombo)
                {
                    int frame = cb.Height - cb.ItemHeight;
                    if (rowPrice[0].Height - frame > 0) cb.ItemHeight = rowPrice[0].Height - frame;
                }
                FitColourGrid();
                UpdateFailedRow();
                // link box, the two buttons and both info boxes end exactly at the Cost breakdown's right edge
                // (measured, so it stays lined up at any DPI and whatever the label column's width)
                int edge = grpBreak.Parent.PointToScreen(new Point(grpBreak.Right, 0)).X;
                int w = edge - txtUrl.Parent.PointToScreen(new Point(txtUrl.Left, 0)).X;
                txtUrl.Width = w;
                lblAuto.SetWidth(w);
                lblPowerInfo.SetWidth(w);
                int gap = btnAnalyze.Margin.Right;
                btnAnalyze.Width = (w - 2 * gap) / 3;
                btnLoadFile.Width = btnAnalyze.Width;
                btnHistory.Width = w - 2 * gap - 2 * btnAnalyze.Width;

                // model picture: from the label column's left edge to the longest label's right edge (keeps the label gap),
                // from the buttons' top to the bottom of a 2-line info box (the info box never gets shorter than that)
                int labelsRight = 0;
                foreach (Control c in inputs.Controls)
                    if (c is Label && inputs.GetColumn(c) == 0) labelsRight = Math.Max(labelsRight, c.Right);
                int twoLines;
                using (var probe = new Label { AutoSize = true, Font = new Font("Segoe UI", 9.5f), Text = "Ag\nAg" })
                    twoLines = probe.GetPreferredSize(Size.Empty).Height + lblAuto.Padding.Vertical;
                lblAuto.MinimumSize = new Size(lblAuto.MinimumSize.Width, twoLines);
                picModel.Margin = new Padding(0, btnAnalyze.Margin.Top, 0, 0);
                picBaseHeight = btnAnalyze.Height + btnAnalyze.Margin.Bottom + lblAuto.Margin.Top + twoLines;
                picModel.Size = new Size(labelsRight - picModel.Left, picBaseHeight);
                FitModelPicture();
            };
            // a 3+ line info box: the picture box grows with it, so both still end at the same line
            lblAuto.SizeChanged += delegate { FitModelPicture(); };
            lblAuto.LocationChanged += delegate { FitModelPicture(); };
            picModel.Resize += delegate { picModel.Invalidate(); };
            Load += delegate { printerCardReady = true; LayoutPrinterCard(); };   // after the widths above are set

            // ---------- Prices ----------
            var prices = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            prices.Controls.Add(PriceBox(L.S("Friends && family", "Venner og familie"), "friends", out lblFriends, out lblFriendsInfo), 0, 0);
            var custBox = PriceBox(L.S("Customers", "Kunder"), "others", out lblOthers, out lblOthersInfo);
            custBox.Margin = Padding.Empty;   // gap only between the boxes, not after the last one
            prices.Controls.Add(custBox, 1, 0);
            root.Controls.Add(prices);

            // ---------- Buttons + status ----------
            var bottom = new TableLayoutPanel { ColumnCount = 2, Width = 540, Height = 38, Margin = Padding.Empty };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            // status line: its own row under the buttons, hidden (row collapses) while empty
            lblStatus = new Label { AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(540, 0), Margin = new Padding(0, 2, 0, 0), Visible = false };
            // update button: from the left edge to 6 px before the buttons (width set on Load), only shown when GitHub has a newer version
            btnUpdate = new Button
            {
                Size = new Size(264, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 0, 3),
                Anchor = AnchorStyles.Left, Visible = false, Font = new Font(Font, FontStyle.Bold)
            };
            btnUpdate.Click += delegate { if (pendingUpdate != null) ShowUpdate(pendingUpdate); };
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            // four buttons, 340 px = 76 + 84 + 70 + 92 + 3 × 6 gap, ending at the Customers box's right edge;
            // the update button left of them takes the rest of the row
            var btnClear = new Button { Text = L.S("Clear", "Ryd"), Size = new Size(76, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 6, 3) };
            var btnSettings = new Button { Text = L.S("Settings…", "Indstillinger…"), Size = new Size(84, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 6, 3) };
            btnSave = new Button { Text = L.S("Save", "Gem"), Size = new Size(70, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 6, 3) };
            btnCopy = new Button { Text = L.S("Copy quote", "Kopiér tilbud"), Size = new Size(92, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 0, 3) };
            // same total width (scaled), split by the measured text widths so longer (Danish) texts fit
            Load += delegate
            {
                var row = new[] { btnClear, btnSettings, btnSave, btnCopy };
                SplitByText(row);
                int used = 0;
                foreach (var b in row) used += b.Width + b.Margin.Horizontal;
                btnUpdate.Width = Math.Max(btnUpdate.Height, bottom.Width - used - btnClear.Margin.Right);   // same 6 px gap
            };
            btnClear.Click += delegate { ClearJob(); };
            btnSettings.Click += delegate { OpenSettings(); };
            btnSave.Click += delegate { SaveClicked(); };
            btnCopy.Click += delegate { CopyQuote(); };
            buttons.Controls.Add(btnClear);
            buttons.Controls.Add(btnSettings);
            buttons.Controls.Add(btnSave);
            buttons.Controls.Add(btnCopy);
            bottom.Controls.Add(btnUpdate, 0, 0);
            bottom.Controls.Add(buttons, 1, 0);
            root.Controls.Add(bottom);
            root.Controls.Add(lblStatus);

            // live recalculation
            EventHandler recalc = delegate { Recalculate(); };
            foreach (var tb in new[] { txtHours, txtMinutes, txtGrams, txtElecPrice })
            {
                var box = tb;
                box.TextChanged += recalc;
                box.Enter += delegate { BeginInvoke((MethodInvoker)delegate { box.SelectAll(); }); };
            }

            FormClosing += delegate { if (cts != null) cts.Cancel(); SaveLastPrices(); };
            FormClosed += delegate
            {
                picRequest++; SetModelPicture(null);
                if (statusTimer != null) { statusTimer.Stop(); statusTimer.Dispose(); statusTimer = null; }
                if (updateTimer != null) { updateTimer.Stop(); updateTimer.Dispose(); updateTimer = null; }
                if (hintTimer != null) { hintTimer.Stop(); hintTimer.Dispose(); hintTimer = null; }
            };
            if (!offline && !AppSettings.TestMode)   // test mode: no update checks at all
            {
                updateTimer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
                updateTimer.Tick += delegate { PeriodicUpdateCheck(); };
                Shown += delegate { SyncUpdateTimer(); };
            }
            EnableDrop(this);
            SetColours(null);   // one material row: Settings' filament profile at its remembered kr/kg

            ApplyTheme();
            ResumeLayout(true);
            RefreshSettingsLabels();
            ShowAutoIdle();
            if (!offline && !AppSettings.TestMode) Shown += delegate { StartupUpdateCheck(); };
            if (settings.AutoPowerPrice != 0) { if (!offline) Shown += delegate { FetchPowerPrice(); }; }
            else SetPowerInfo(L.S("Live price is off (Settings). Click Update to fetch it once.",
                                  "Live-pris er slået fra (Indstillinger). Klik Opdatér for at hente den én gang."), false);
            Recalculate();
            ActiveControl = txtUrl;
        }

        /// Wear rate from the chosen nozzle, filament (abrasive? type?) and build plate.
        static double AutoWearRate(AppSettings s)
        {
            var nozzle = Wear.FindNozzle(s.Nozzle);
            string type = "";
            bool abrasive = false;
            try { BambuSlicer.FilamentInfo(BambuSlicer.FindExe(s.SlicerPath), s.FilamentPreset, nozzle.Size, out type, out abrasive); }
            catch { /* Bambu Studio missing: treat as normal filament */ }
            return Wear.Calculate(nozzle, abrasive, type, Wear.FindPlate(s.BedType)).Total;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override bool ShowWithoutActivation { get { return offline || Screenshots.Active || Program.Hidden; } }

        /// Picture mode: fills in a fixed example Benchy job, as if it had been sliced and the live
        /// price fetched (made-up texts, nothing goes online). The price labels update by themselves.
        internal void SetSampleJob()
        {
            if (!offline) return;
            var one = new SliceResult { Seconds = 38 * 60, Grams = 11.8, Plates = 1 };
            one.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#F07850", Type = "PLA", Profile = "Bambu PLA Matte @BBL A1", Grams = 11.8 });
            SetColours(one);   // one material row, orange like the sample Benchy
            txtUrl.Text = SingleBenchyLink;
            SetAuto(L.S("✓ \"3D BENCHY\": 0 h 38 m, 11,8 g (0.16mm Optimal · Bambu PLA Matte).",
                        "✓ \"3D BENCHY\": 0 t 38 m, 11,8 g (0.16mm Optimal · Bambu PLA Matte)."), false);
            txtHours.Text = "0";
            txtMinutes.Text = "38";
            txtGrams.Text = "11,8";
            SetSampleRowPrices("200");
            txtElecPrice.Text = "3,65";
            SetJobLoaded(true);
            SetPowerInfo(L.S("Live DK1 17:45: spot 2,37 + fees 0,55 + 25 % VAT = 3,65 kr.",
                             "Live-pris DK1 17:45: spot 2,37 + afg. 0,55 + moms = 3,65 kr."), false);
            Recalculate();
        }

        /// Picture mode: the 4-colour Benchy (Printables 1648230) as our CLI slices it (real numbers from the
        /// multi-colour test: 135,85 g of which 124 g purge, 8,7 h of colour changes). Call after SetSampleJob
        /// (keeps its live price line).
        internal void SetSampleMultiJob()
        {
            if (!offline) return;
            txtUrl.Text = ExampleLink;   // the 4-colour Benchy
            SetAuto(L.S("✓ \"Four-Color Multicolor Test benchy\": 10 h 43 m, 135,85 g (0.16mm Optimal · 4 colours).",
                        "✓ \"Four-Color Multicolor Test benchy\": 10 t 43 m, 135,85 g (0.16mm Optimal · 4 farver)."), false);
            var r = new SliceResult { Seconds = 38566, Grams = 135.85, Plates = 1, PurgeGrams = 124.0, ColourChangeHours = 31450 / 3600.0 };
            string[] colours = { "#118002", "#FFFF0A", "#074080", "#ED1C24" };
            string[] profiles = { "Bambu PLA Basic", "Bambu PLA Matte", "Bambu PLA Silk+", "Bambu PETG Translucent" };
            double[] grams = { 33.04, 55.99, 17.28, 29.54 }, model = { 5.04, 0.38, 5.87, 0.56 };
            for (int i = 0; i < 4; i++)
                r.Filaments.Add(new FilamentUse { Slot = i + 1, Colour = colours[i], Type = i == 3 ? "PETG" : "PLA",
                                                  Profile = profiles[i] + " @BBL A1", Grams = grams[i], ModelGrams = model[i] });
            txtHours.Text = "10";
            txtMinutes.Text = "43";
            txtElecPrice.Text = "3,65";
            SetColours(r);
            SetSampleRowPrices("200");
            txtGrams.Text = "135,85";
            SetJobLoaded(true);
            Recalculate();
        }

        /// Picture mode: the same kr/kg in every material row (not remembered per material).
        void SetSampleRowPrices(string price)
        {
            fillingColours = true;
            for (int i = 0; i < colourRows; i++) rowPrice[i].Text = price;
            fillingColours = false;
            Recalculate();
        }

        void ApplyTheme()
        {
            Theme.Apply(this);
            Theme.AccentButton(btnUpdate);
            Theme.TitleBar(this);
            SetStatusColor();
            SetAutoColor();
            SetPowerColor();
        }

        // ---------- Live electricity price ----------
        void SetPowerInfo(string text, bool error)
        {
            powerIsError = error;
            lblPowerInfo.Text = text;
            SetPowerColor();
        }

        void SetPowerColor()
        {
            if (lblPowerInfo != null) lblPowerInfo.Kind = powerIsError ? InfoKind.Error : InfoKind.Info;
        }

        async void FetchPowerPrice()
        {
            if (offline || fetchingPower) return;
            fetchingPower = true;
            btnPower.Enabled = false;
            string area = settings.PriceArea;
            double addOn = settings.PowerAddOn;
            bool vat = settings.PowerIncludeVat != 0;
            string before = txtElecPrice.Text;
            SetPowerInfo(L.F("Fetching the current electricity price for {0}…", "Henter den aktuelle elpris for {0}…", area), false);
            try
            {
                var q = await Task.Run(() => PowerPrice.GetCurrent(area));
                double total = Math.Round(PowerPrice.TotalPrice(q.SpotDkkPerKwh, addOn, vat), 2);
                // Danish kept short ("afg.", "moms" = always 25 %) so the line fits on one line in the box
                string breakdown = L.F("spot {0} + fees {1}", "spot {0} + afg. {1}",
                                       q.SpotDkkPerKwh.ToString("0.00", Num.Dk), addOn.ToString("0.00", Num.Dk)) +
                                   (vat ? L.S(" + 25 % VAT", " + moms") : "");
                string when = q.Area + " " + q.SlotStartUtc.ToLocalTime().ToString("HH:mm", Num.Dk);
                if (txtElecPrice.Text == before)
                {
                    txtElecPrice.Text = total.ToString("0.00", Num.Dk);
                    // non-breaking spaces: "= 3,65 kr." wraps as one piece, never a lone "kr." on line 2
                    SetPowerInfo(L.S("Live ", "Live-pris ") + when + ": " + breakdown + " = " + total.ToString("0.00", Num.Dk) + " kr.", false);
                }
                else
                {
                    SetPowerInfo(L.F("Live price {0} is {1} kr/kWh (not applied because you edited the field).",
                                     "Live-pris {0} er {1} kr/kWh (ikke brugt, fordi du har rettet feltet).",
                                     when, total.ToString("0.00", Num.Dk)), false);
                }
            }
            catch (Exception ex)
            {
                string why = ex is SliceException ? ex.Message : L.S("unexpected error", "uventet fejl");
                SetPowerInfo(L.F("Couldn't get the live price ({0}). Using your last price.",
                                 "Kunne ikke hente live-prisen ({0}). Bruger din sidste pris.", why), true);
            }
            finally
            {
                fetchingPower = false;
                btnPower.Enabled = true;
            }
        }

        // ---------- Auto-detect: link / file → Bambu Studio → time & grams ----------
        void SetAuto(string text, bool error)
        {
            reopened = null;   // any other message replaces the "Opened from history" lines
            autoIsError = error;
            lblAuto.Text = text;
            SetAutoColor();
        }

        void SetAutoColor()
        {
            // results keep the red info stripe too (the ✓ in the text already shows it worked)
            if (lblAuto != null) lblAuto.Kind = autoIsError ? InfoKind.Error : InfoKind.Info;
        }

        void ShowAutoIdle()
        {
            if (BambuSlicer.FindExe(settings.SlicerPath) == null)
                SetAuto(L.S("Bambu Studio was not found. Install it to detect time and weight automatically.",
                            "Bambu Studio blev ikke fundet. Installér det for at finde tid og vægt automatisk."), false);
            else
                SetAuto(L.F("Paste a Printables link, or drop a model file (STL/3MF) on this window. Slices with {0} mm nozzle · {1} · {2}.",
                            "Indsæt et Printables-link, eller træk en modelfil (STL/3MF) ind i vinduet. Slicer med {0} mm dyse · {1} · {2}.",
                            L.Danish ? Wear.FindNozzle(settings.Nozzle).Size.Replace('.', ',') : Wear.FindNozzle(settings.Nozzle).Size,
                            BambuSlicer.ShortName(settings.ProcessPreset), BambuSlicer.ShortName(settings.FilamentPreset)), false);
        }

        void EnableDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) =>
            {
                e.Effect = cts == null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            c.DragDrop += (s, e) =>
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0) return;
                var models = paths.Where(BambuSlicer.IsModelFile).ToList();
                if (models.Count == 0) { SetAuto(L.S("That isn't a model file. Use STL, 3MF, OBJ or STEP.", "Det er ikke en modelfil. Brug STL, 3MF, OBJ eller STEP."), true); return; }
                txtUrl.Text = models.Count == 1 ? models[0] : L.F("{0} files: ", "{0} filer: ", models.Count) + string.Join(", ", models.Select(Path.GetFileName));
                Analyze(null, models);
            };
            foreach (Control child in c.Controls) EnableDrop(child);
        }

        void BrowseForModel()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = L.S("Choose model file(s)", "Vælg modelfil(er)"), Multiselect = true,
                Filter = L.S("3D models", "3D-modeller") + " (*.stl;*.3mf;*.obj;*.step;*.stp)|*.stl;*.3mf;*.obj;*.step;*.stp;*.amf|" +
                         L.S("All files", "Alle filer") + " (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var files = dlg.FileNames.ToList();
                txtUrl.Text = files.Count == 1 ? files[0] : L.F("{0} files: ", "{0} filer: ", files.Count) + string.Join(", ", files.Select(Path.GetFileName));
                Analyze(null, files);
            }
        }

        void AnalyzeClicked()
        {
            if (cts != null) { cts.Cancel(); return; }
            string src = txtUrl.Text.Trim().Trim('"');
            if (src.Length == 0) { txtUrl.Text = ""; SetUrlHint(true); txtUrl.Focus(); return; }
            if (File.Exists(src)) { Analyze(null, new List<string> { src }); return; }

            string id = Printables.ParseId(src);
            if (id != null) { Analyze(id, null); return; }

            string lower = src.ToLowerInvariant();
            if (lower.Contains("makerworld.com") || lower.Contains("thingiverse.com") || lower.Contains("cults3d.com") || lower.Contains("myminifactory.com"))
                SetAuto(L.S("This site blocks automatic downloads. Download the model (STL/3MF) from the page and drop the file on this window.",
                            "Siden blokerer automatisk download. Hent modellen (STL/3MF) fra siden, og træk filen ind i vinduet."), true);
            else
                SetAuto(L.S("Paste a Printables link (printables.com/model/…) or load a model file.",
                            "Indsæt et Printables-link (printables.com/model/…) eller åbn en modelfil."), true);
        }

        static string AnalyzeText { get { return L.S("Get time && weight", "Hent tid og vægt"); } }

        void SetBusy(bool busy)
        {
            btnAnalyze.Text = busy ? L.S("Cancel", "Annullér") : AnalyzeText;
            btnLoadFile.Enabled = !busy;
            btnHistory.Enabled = !busy;   // "Open again" would clash with a running job
            txtUrl.ReadOnly = busy;
            UseWaitCursor = false;
        }

        async void Analyze(string printablesId, List<string> localFiles)
        {
            if (offline || cts != null) return;
            string exe = BambuSlicer.FindExe(settings.SlicerPath);
            bool presliced = localFiles != null && localFiles.Count == 1 && BambuSlicer.IsSlicedProject(localFiles[0]);
            if (exe == null && !presliced)
            {
                SetAuto(L.S("Bambu Studio was not found. Install it from bambulab.com, or set its location in Settings.",
                            "Bambu Studio blev ikke fundet. Installér det fra bambulab.com, eller angiv stien i Indstillinger."), true);
                return;
            }

            string process = settings.ProcessPreset, filament = settings.FilamentPreset, bed = settings.BedType;
            string nozzle = Wear.FindNozzle(settings.Nozzle).Size;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            SetBusy(true);
            IProgress<string> progress = new Progress<string>(s => SetAuto(s, false));
            string modelName = null;
            // model picture: a picked .3mf shows its own picture at once; Printables photo / slice render follow below
            SetJobLoaded(false);
            savedId = null;   // a new job: what the last one saved counts as paid back now
            ResetModelPicture();
            if (localFiles != null && localFiles.Count == 1 && localFiles[0].EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            {
                string project = localFiles[0];
                LoadModelPicture(() => BambuSlicer.FromProject(project), true);
            }
            try
            {
                List<RemoteFile> chosen = null;
                if (printablesId != null)
                {
                    progress.Report(L.S("Looking up the model on Printables…", "Finder modellen på Printables…"));
                    var model = await Task.Run(() => Printables.GetModel(printablesId));
                    token.ThrowIfCancellationRequested();
                    modelName = model.Name;
                    if (model.ImageUrl != null)
                    {
                        string imageUrl = model.ImageUrl;
                        LoadModelPicture(() => Printables.DownloadImage(imageUrl), true);
                    }
                    var sliceable = model.Files.Where(f => BambuSlicer.IsModelFile(f.Name)).ToList();
                    if (sliceable.Count == 0) throw new SliceException(L.F("\"{0}\" has no STL, 3MF or STEP files to slice.",
                                                                           "\"{0}\" har ingen STL-, 3MF- eller STEP-filer at slice.", model.Name));
                    if (sliceable.Count == 1) chosen = sliceable;
                    else
                    {
                        using (var picker = new FilePickerForm(model.Name, sliceable))
                        {
                            if (picker.ShowDialog(this) != DialogResult.OK) throw new OperationCanceledException();
                            chosen = picker.Selected;
                        }
                    }
                }

                var result = await Task.Run(() => RunJob(exe, printablesId, chosen, localFiles, nozzle, process, filament, bed, progress, token));
                if (result.Picture != null)
                {
                    byte[] render = result.Picture;
                    // the plate render always wins: replaces the Printables photo / 3MF thumbnail; picRequest++ drops a photo
                    // still downloading (it would land after the render). The shown picture stays if the render can't be read.
                    picRequest++;
                    LoadModelPicture(() => render, true);
                }

                int totalMinutes = (int)Math.Round(result.Seconds / 60.0);
                if (totalMinutes == 0 && result.Seconds > 0) totalMinutes = 1;
                txtHours.Text = (totalMinutes / 60).ToString();
                txtMinutes.Text = (totalMinutes % 60).ToString();
                SetColours(result);   // 2+ colours: one row per colour in the Cost breakdown, else today's layout
                bool multi = filaments != null;
                // several colours: grams with 2 decimals, like the colour rows
                string gramsText = multi ? Math.Round(result.Grams, 2).ToString("0.##", Num.Dk) : Math.Round(result.Grams, 1).ToString("0.#", Num.Dk);
                txtGrams.Text = gramsText;

                string what = modelName != null ? "\"" + modelName + "\"" :
                              localFiles.Count == 1 ? Path.GetFileName(localFiles[0]) : L.F("{0} files", "{0} filer", localFiles.Count);
                string summary = (totalMinutes / 60) + L.S(" h ", " t ") + (totalMinutes % 60) + " m, " + gramsText + " g";
                string colourCount = multi ? L.F("{0} colours", "{0} farver", filaments.Count) : null;
                string how = result.FromSlicedFile
                    ? L.S("read from the sliced file", "læst fra den slicede fil") + (multi ? " · " + colourCount : "")
                    : BambuSlicer.ShortName(process) + " · " + (multi ? colourCount : BambuSlicer.ShortName(filament));
                int parts = chosen != null ? chosen.Count : localFiles.Count;
                // name for the history (after the link box was filled, whose TextChanged clears it)
                jobModel = modelName ?? (localFiles.Count == 1 ? Path.GetFileName(localFiles[0]) : string.Join(", ", localFiles.Select(Path.GetFileName)));
                SetJobLoaded(true);
                SetAuto("✓ " + what + ": " + summary + " (" + how +
                        (parts > 1 ? L.F(", {0} files", ", {0} filer", parts) : "") +
                        (result.Plates > parts ? L.F(", {0} plates", ", {0} plader", result.Plates) : "") + ").", false);
            }
            catch (OperationCanceledException) { SetAuto(L.S("Cancelled.", "Annulleret."), false); }
            catch (SliceException ex) { SetAuto(ex.Message, true); }
            catch (Exception ex) { SetAuto(L.S("Something went wrong: ", "Noget gik galt: ") + ex.Message, true); }
            finally
            {
                cts.Dispose();
                cts = null;
                SetBusy(false);
            }
        }

        static SliceResult RunJob(string exe, string printId, List<RemoteFile> remote, List<string> local,
                                  string nozzle, string process, string filament, string bed, IProgress<string> progress, CancellationToken ct)
        {
            string work = Path.Combine(Path.GetTempPath(), "PrintPrice", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(work);
                var models = new List<KeyValuePair<string, string>>();   // display name → path
                if (remote != null)
                {
                    for (int i = 0; i < remote.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(L.S("Downloading ", "Henter ") + remote[i].Name + (remote.Count > 1 ? " (" + (i + 1) + "/" + remote.Count + ")" : "") + "…");
                        string target = Path.Combine(work, (i + 1) + "_" + Printables.SafeFileName(remote[i].Name));
                        Printables.Download(printId, remote[i], target);
                        models.Add(new KeyValuePair<string, string>(remote[i].Name, target));
                    }
                }
                else
                {
                    foreach (var f in local) models.Add(new KeyValuePair<string, string>(Path.GetFileName(f), f));
                }

                if (models.Count == 1 && BambuSlicer.IsSlicedProject(models[0].Value))
                {
                    var r = BambuSlicer.ReadSliceInfo(models[0].Value);
                    r.FromSlicedFile = true;
                    return r;
                }

                ct.ThrowIfCancellationRequested();
                progress.Report(L.S("Preparing Bambu Lab A1 profiles…", "Klargør Bambu Lab A1-profiler…"));
                BambuSlicer.PrepareProfiles(exe, nozzle, process, filament, bed, work);

                var total = new SliceResult();
                for (int i = 0; i < models.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report(L.S("Slicing ", "Slicer ") + models[i].Key + (models.Count > 1 ? " (" + (i + 1) + "/" + models.Count + ")" : "") +
                                    L.S(" in Bambu Studio…", " i Bambu Studio…"));
                    var r = BambuSlicer.SliceFile(exe, models[i].Value, work, ct);
                    total.Seconds += r.Seconds;
                    total.Grams += r.Grams;
                    total.Plates += r.Plates;
                    if (total.Picture == null) total.Picture = r.Picture;   // first model's plate picture
                    MergeFilaments(total, r);
                }
                total.Filaments.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                return total;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        /// Several model files: adds one slice's filaments to the total per slot, and sums the known purge / colour-change values.
        static void MergeFilaments(SliceResult total, SliceResult r)
        {
            foreach (var f in r.Filaments)
            {
                if (f == null) continue;
                var t = total.Filaments.FirstOrDefault(x => x.Slot == f.Slot);
                if (t == null)
                {
                    total.Filaments.Add(new FilamentUse { Slot = f.Slot, Colour = f.Colour ?? "", Type = f.Type ?? "", Profile = f.Profile ?? "",
                                                          Grams = f.Grams, ModelGrams = f.ModelGrams, Metres = f.Metres });
                    continue;
                }
                t.Grams += f.Grams;
                t.Metres += f.Metres;
                t.ModelGrams = t.ModelGrams >= 0 && f.ModelGrams >= 0 ? t.ModelGrams + f.ModelGrams : -1;
                if (t.Colour.Length == 0 && !string.IsNullOrEmpty(f.Colour)) t.Colour = f.Colour;
                if (t.Type.Length == 0 && !string.IsNullOrEmpty(f.Type)) t.Type = f.Type;
                if (t.Profile.Length == 0 && !string.IsNullOrEmpty(f.Profile)) t.Profile = f.Profile;
            }
            if (r.PurgeGrams >= 0) total.PurgeGrams = Math.Max(0, total.PurgeGrams) + r.PurgeGrams;
            if (r.ColourChangeHours >= 0) total.ColourChangeHours = Math.Max(0, total.ColourChangeHours) + r.ColourChangeHours;
        }

        /// Empty link box + Enter puts this in (the 4-colour Benchy: shows the multi-colour rows).
        const string ExampleLink = "https://www.printables.com/model/1648230-four-color-multicolor-test-benchy";
        /// The single-colour Benchy: picture mode's main example job (SetSampleJob) only.
        const string SingleBenchyLink = "https://www.printables.com/model/3161-3d-benchy";

        /// Link box that paints its own placeholder while empty, centred: the grey example (Cue) or the red Hint.
        /// (The native cue banner can't be centred or coloured.) Typed text is centred too (TextAlign = Center).
        sealed class HintBox : TextBox
        {
            public string Hint;   // null = no hint (red, wins over Cue)
            public string Cue;    // grey example text, null = none

            // Theme.Apply makes it (like every text box) a one-line multiline edit with its text line vertically centred
            // (EM_SETRECT, 4 px side margins); the hint is drawn on that same line (Theme.OneLineTop) = same baseline.
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == 0x0007 || m.Msg == 0x0008) { Invalidate(); return; }   // WM_SETFOCUS / WM_KILLFOCUS: redraw the placeholder
                string text = Hint ?? Cue;
                if (m.Msg != 0x000F || text == null || Text.Length > 0) return;   // WM_PAINT
                int textTop = Theme.OneLineTop(this);
                using (var g = Graphics.FromHwnd(Handle))
                    TextRenderer.DrawText(g, text, Font, new Rectangle(2, textTop, ClientSize.Width - 4, Math.Max(1, ClientSize.Height - textTop - 1)), Hint != null ? Theme.Error : SystemColors.GrayText,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter);   // centred; y = the text line's top = same baseline as typed text; no background fill, so the box's top border stays whole
            }
            protected override void OnTextChanged(EventArgs e)
            {
                // one line only: pasted / Ctrl+Enter line breaks become spaces
                if (Text.IndexOf('\r') >= 0 || Text.IndexOf('\n') >= 0)
                {
                    int caret = SelectionStart;
                    Text = Text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');   // raises TextChanged again, without breaks
                    SelectionStart = Math.Min(caret, Text.Length);
                    return;
                }
                base.OnTextChanged(e);
                Invalidate();   // placeholder appears/disappears with the first/last character
            }
        }

        System.Windows.Forms.Timer hintTimer;

        void ApplyUrlCue()
        {
            var box = (HintBox)txtUrl;
            // painted by HintBox, centred (grey like the native cue banner); ~330 / 355 px wide at 96 dpi, the box ~410 px
            box.Cue = L.S("Paste a Printables link, or press Enter for an example",
                          "Indsæt et Printables-link, eller tryk Enter for et eksempel");
            box.AccessibleDescription = box.Hint ?? box.Cue;
            if (box.IsHandleCreated) box.Invalidate();
        }

        /// true = red "Paste a link or load a file first" in the empty link box; false = grey example again.
        void SetUrlHint(bool on)
        {
            var box = (HintBox)txtUrl;
            if (hintTimer == null)
            {
                hintTimer = new System.Windows.Forms.Timer { Interval = 10000 };   // red hint goes away after 10 s
                hintTimer.Tick += delegate { SetUrlHint(false); };
            }
            hintTimer.Stop();
            if (on) hintTimer.Start();   // pressing the button again restarts the 10 s
            string hint = on ? L.S("Paste a link or load a file first", "Indsæt et link eller åbn en fil først") : null;
            if (box.Hint == hint) return;
            box.Hint = hint;
            ApplyUrlCue();
        }

        // ---------- Model picture ----------
        /// Shows a new picture (null = back to the logo) and disposes the old one.
        void SetModelPicture(Bitmap bmp)
        {
            var old = modelImage;
            modelImage = bmp;
            if (old != null) old.Dispose();
            picModel.Invalidate();
        }

        /// The model picture, drawn by PaintModelPicture (not PictureBox.Image: its Zoom mode ignores Padding,
        /// so the picture touched the walls and the border was drawn over its edges).
        Bitmap modelImage;

        /// Picture box height: at least the 2-line size, else down to the info box's bottom.
        void FitModelPicture()
        {
            if (picBaseHeight == 0) return;
            int h = Math.Max(picBaseHeight, lblAuto.Bottom - picModel.Top);
            if (picModel.Height != h) picModel.Height = h;
        }

        /// Starts a new picture "job": anything still loading for an older one is dropped. Back to the logo.
        void ResetModelPicture()
        {
            picRequest++;
            SetModelPicture(null);
        }

        /// A model is loaded (true) or not: decides whether the empty picture box says "No model yet" under the logo.
        void SetJobLoaded(bool on)
        {
            if (jobLoaded == on) return;
            jobLoaded = on;
            if (modelImage == null) picModel.Invalidate();
        }

        /// Gets the picture bytes and prepares the bitmap off the UI thread; failures are ignored quietly.
        /// replace = false: only used if nothing is showing yet for this job (the Printables photo beats the slice render).
        async void LoadModelPicture(Func<byte[]> source, bool replace)
        {
            if (offline) return;   // picture mode never downloads
            int request = picRequest;
            Bitmap bmp = null;
            try { bmp = await Task.Run(() => { var bytes = source(); return bytes == null ? null : PrepareModelPicture(bytes); }); }
            catch { }
            if (bmp == null) return;
            // back on the UI thread (await); drop it if the window closed, a newer job started or a better picture is there
            if (IsDisposed || request != picRequest || (!replace && modelImage != null)) { bmp.Dispose(); return; }
            SetModelPicture(bmp);
        }

        /// Picture mode: shows a sample picture (a file, never a download); no file = back to the logo.
        internal void SetSamplePicture(string file)
        {
            if (!File.Exists(file)) { ResetModelPicture(); return; }
            try { SetModelPicture(PrepareModelPicture(File.ReadAllBytes(file))); } catch { }
        }

        /// Decodes the picture, cuts off transparent margins (slicer renders have lots) and scales it down to at most 384 px.
        static Bitmap PrepareModelPicture(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
            using (var src = new Bitmap(ms))
            using (var full = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(full)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                Rectangle r = OpaqueBounds(full);
                if (r.Width <= 0 || r.Height <= 0) return null;
                double sc = Math.Min(1.0, 384.0 / Math.Max(r.Width, r.Height));
                int w = Math.Max(1, (int)Math.Round(r.Width * sc)), h = Math.Max(1, (int)Math.Round(r.Height * sc));
                var result = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(full, new Rectangle(0, 0, w, h), r, GraphicsUnit.Pixel);
                }
                return result;
            }
        }

        /// Smallest rectangle holding all pixels that aren't (almost) transparent.
        static Rectangle OpaqueBounds(Bitmap b)
        {
            var all = new Rectangle(0, 0, b.Width, b.Height);
            var data = b.LockBits(all, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var px = new int[b.Width * b.Height];
            try { System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length); }   // 32bpp: stride = width * 4
            finally { b.UnlockBits(data); }
            int minX = b.Width, minY = b.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                    if (((px[y * b.Width + x] >> 24) & 0xFF) > 16)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            return maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        /// Border, and while there is no picture: the dimmed logo, with "No model yet" under it unless a job is loaded
        /// (e.g. a quote reopened from a Printables link has no picture, but it isn't "no model").
        void PaintModelPicture(Graphics g)
        {
            var r = picModel.ClientRectangle;
            var pic = modelImage;
            if (pic != null)
            {
                // the whole picture, scaled to fit inside a margin of 5 % of the box (at least 4 px at 96 dpi), centred
                int m = Math.Max(4 * DeviceDpi / 96, (int)Math.Round(0.05 * Math.Min(r.Width, r.Height)));
                int aw = r.Width - 2 * m, ah = r.Height - 2 * m;
                if (aw > 0 && ah > 0)
                {
                    double sc = Math.Min((double)aw / pic.Width, (double)ah / pic.Height);
                    int w = Math.Max(1, (int)Math.Round(pic.Width * sc)), h = Math.Max(1, (int)Math.Round(pic.Height * sc));
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(pic, new Rectangle(m + (aw - w) / 2, m + (ah - h) / 2, w, h));
                }
            }
            else
            {
                string text = jobLoaded ? "" : L.S("No model yet", "Ingen model");
                var font = pictureFont;
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
                int pad = picModel.Padding.Left;
                Size ts = text.Length == 0 ? Size.Empty : TextRenderer.MeasureText(g, text, font, new Size(r.Width - 2 * pad, 0), flags);
                int gap = text.Length == 0 ? 0 : pad, topMin = pad * 3 / 2;   // at least 6 px (at 96 dpi) above the logo, 4 px under the text
                int logoSize = Math.Min(r.Width - 4 * pad, r.Height - topMin - pad - gap - ts.Height);
                var logo = Logo;
                if (logo == null) logoSize = 0;
                int top = Math.Max(topMin, (r.Height - (logoSize + (logoSize > 0 ? gap : 0) + ts.Height)) / 2);
                if (logoSize > 0)
                {
                    using (var ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.6f };   // dimmed: 60 % opacity
                        ia.SetColorMatrix(cm);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(logo, new Rectangle((r.Width - logoSize) / 2, top, logoSize, logoSize),
                                    0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, ia);
                    }
                    top += logoSize + gap;
                }
                TextRenderer.DrawText(g, text, font, new Rectangle(pad, top, r.Width - 2 * pad, ts.Height), Theme.Muted, flags);
            }
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        }

        static readonly Font pictureFont = new Font("Segoe UI", 9f);
        static Bitmap logo;
        static bool logoLoaded;

        /// The app logo: the biggest PNG frame of the program's own icon (app.ico has a 128 px one). Null if not found.
        static Bitmap Logo
        {
            get
            {
                if (logoLoaded) return logo;
                logoLoaded = true;
                try
                {
                    IntPtr module = GetModuleHandle(null);
                    int bestWidth = 0;
                    for (int id = 1; id <= 32; id++)
                    {
                        IntPtr res = FindResource(module, (IntPtr)id, (IntPtr)3);   // RT_ICON
                        if (res == IntPtr.Zero) continue;
                        int size = (int)SizeofResource(module, res);
                        IntPtr data = LockResource(LoadResource(module, res));
                        if (data == IntPtr.Zero || size < 8) continue;
                        var bytes = new byte[size];
                        System.Runtime.InteropServices.Marshal.Copy(data, bytes, 0, size);
                        if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47) continue;   // PNG frames only (keep alpha)
                        Bitmap bmp;
                        using (var ms = new MemoryStream(bytes))
                        using (var png = new Bitmap(ms)) bmp = new Bitmap(png);   // own copy, the stream can go
                        if (bmp.Width > bestWidth) { if (logo != null) logo.Dispose(); logo = bmp; bestWidth = bmp.Width; }
                        else bmp.Dispose();
                    }
                }
                catch { }
                return logo;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr LoadResource(IntPtr module, IntPtr res);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr LockResource(IntPtr data);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint SizeofResource(IntPtr module, IntPtr res);

        // ---------- Material rows (Cost breakdown) ----------
        /// Grid of MaxColourRows rows: swatch, material drop-down (190 px), grams, ×, kr/kg box, unit, cost (right, lined up
        /// with the other amounts). Spans both breakdown columns; its height is set by FitColourGrid (rows × 28 px at 96 dpi).
        TableLayoutPanel BuildColourGrid()
        {
            // fixed height = rows in use × row height, rows Absolute (FitColourGrid; unused rows 0), so the rows are always even
            var g = new TableLayoutPanel { ColumnCount = 7, AutoSize = false, Height = 10, Margin = Padding.Empty,
                                           Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            for (int c = 0; c < 6; c++) g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < MaxColourRows; i++)
            {
                g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                rowSwatch[i] = new Swatch { Size = new Size(14, 14), Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 8, 0) };
                // full filament name; choosing one = new kr/kg from that material's remembered price (no re-slice).
                // Rows are 28 px: the drop-down is as tall as the kr/kg box (ItemHeight set on Load) + 1 above, 2 below
                int row = i;
                rowCombo[i] = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, MaxDropDownItems = 12, Anchor = AnchorStyles.Top | AnchorStyles.Left,
                    Margin = new Padding(0, 1, 16, 0), AccessibleName = L.S("Material", "Materiale")   // top margin set by FitColourGrid
                };
                rowCombo[i].SelectedIndexChanged += delegate { if (!fillingColours) MaterialChosen(row); };
                rowCombo[i].Enter += delegate { EnsureMaterialList(row); };      // arrow keys / typing pick from the full list
                rowCombo[i].DropDown += delegate { EnsureMaterialList(row); };
                rowCombo[i].KeyDown += delegate { EnsureMaterialList(row); };   // still focused after a new job reset the list
                // fixed width (MinimumSize, FitColourGrid), text right-aligned in it
                rowGrams[i] = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 6, 0), TextAlign = ContentAlignment.TopRight };
                // grayscale text: ClearType made the small × look orange
                var times = new Theme.PlainLabel { Text = "×", AutoSize = true, Tag = "muted", Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
                var box = new TextBox { Width = 50, TextAlign = HorizontalAlignment.Right, Margin = Padding.Empty, Anchor = AnchorStyles.Top | AnchorStyles.Left };   // 25 px at 100 %; top margin by FitColourGrid
                // kr/kg on the row's text line without making the row taller: the label (23 px of GDI+ text) is stretched
                // to the row (it adds only 10 px itself) and its text starts 5 px down
                var unit = Unit("kr/kg");
                unit.AutoSize = false;
                using (var probe = new Theme.PlainLabel { AutoSize = true, Font = Font, Text = unit.Text })   // the font it gets from the form
                    unit.Size = new Size(probe.PreferredWidth, 10);
                unit.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
                unit.Margin = new Padding(2, 0, 10, 0);
                unit.Padding = new Padding(0, 5, 0, 0);   // row 28 px, box centred 1 px down: text 1 px lower than before
                unit.TextAlign = ContentAlignment.TopLeft;
                rowPrice[i] = box;
                rowCost[i] = BreakLabel();
                box.TextChanged += delegate { if (!fillingColours) RememberRowPrice(row); Recalculate(); };
                box.Enter += delegate { BeginInvoke((MethodInvoker)delegate { box.SelectAll(); }); };
                rowUnit[i] = unit;
                rowControls[i] = new Control[] { rowSwatch[i], rowCombo[i], rowGrams[i], times, box, unit, rowCost[i] };
                for (int c = 0; c < 7; c++) g.Controls.Add(rowControls[i][c], c, i);
            }
            return g;
        }

        /// Fills the material rows: r null (no job) = one row with Settings' filament profile and a neutral swatch;
        /// 0-1 filaments = one row (its grams follow "Filament used", which stays editable); 2+ = one row per colour.
        /// Each row's kr/kg = the remembered price of its material (AppSettings.PriceFor); with more than MaxColourRows
        /// colours the last row holds all the rest ("+N more", one shared kr/kg box, drop-down disabled).
        void SetColours(SliceResult r)
        {
            var list = r == null ? new List<FilamentUse>() : r.Filaments.Where(f => f != null && f.Grams > 0).OrderBy(f => f.Slot).ToList();
            bool multi = list.Count >= 2;
            filaments = multi ? list : null;
            jobPurge = multi ? r.PurgeGrams : -1;
            single = null;
            if (!multi)
            {
                var f = list.Count == 1 ? list[0] : null;
                string profile = f != null ? f.Profile ?? "" : "";
                // unknown profile: a sliced file's own type ("PETG"), else the profile we sliced with (Settings)
                if (profile.Length == 0)
                    profile = f != null && r.FromSlicedFile && !string.IsNullOrEmpty(f.Type) ? f.Type : settings.FilamentPreset;
                single = new FilamentUse { Slot = 1, Colour = f != null ? f.Colour ?? "" : "", Type = f != null ? f.Type ?? "" : "",
                                           Profile = profile, Grams = f != null ? f.Grams : 0, ModelGrams = -1 };
            }
            fillingColours = true;
            SuspendLayout();
            int rows;
            if (multi)
            {
                rows = Math.Min(list.Count, MaxColourRows);
                int own = list.Count > MaxColourRows ? MaxColourRows - 1 : rows;   // rows with one colour each
                for (int i = 0; i < rows; i++) rowItems[i] = new List<int>();
                for (int k = 0; k < list.Count; k++) rowItems[k < own ? k : own].Add(k);
                for (int i = 0; i < rows; i++)
                {
                    var items = rowItems[i];
                    rowSwatch[i].Colours = items.Select(k => ParseColour(list[k].Colour)).ToArray();
                    if (items.Count > 1) SetMaterialBox(i, L.F("+{0} more", "+{0} flere", items.Count), false);
                    else SetMaterialBox(i, MaterialName(list[items[0]]), true);
                    rowGrams[i].Text = items.Sum(k => list[k].Grams).ToString("0.00", Num.Dk) + " g";
                    rowPrice[i].Text = Num.Fmt(settings.PriceFor(MaterialName(list[items[0]])));
                    rowCost[i].Text = "–";
                }
            }
            else
            {
                rows = 1;
                rowItems[0] = null;
                rowSwatch[0].Colours = new[] { ParseColour(single.Colour) };   // unknown colour = empty box
                SetMaterialBox(0, MaterialName(single), true);
                rowPrice[0].Text = Num.Fmt(settings.PriceFor(MaterialName(single)));
                rowCost[0].Text = "–";   // grams: Recalculate, from "Filament used"
            }
            colourRows = rows;
            for (int i = 0; i < MaxColourRows; i++)
                foreach (var c in rowControls[i]) c.Visible = i < rows;
            FitColourGrid();
            string note = multi ? PurgeNote(r) : "";
            lblPurge.Text = note;
            lblPurge.Visible = note.Length > 0;
            txtGrams.ReadOnly = multi;
            txtGrams.TabStop = !multi;
            txtGrams.ForeColor = multi ? Theme.Muted : Theme.Text;
            ResumeLayout(true);
            fillingColours = false;
            LayoutPrinterCard();
            UpdateBreakHeight();
            Recalculate();
        }

        /// Rows in use get a fixed height (28 px at 96 dpi, at least the kr/kg box + 3 px), unused rows 0; the grid's height
        /// is exactly their sum (UpdateBreakHeight sizes the Cost breakdown from it). The drop-down and box are centred in the row; "kr/kg" sits on the box's text
        /// line (4 px below the box's top). Before Load the rows are AutoSize (sizes not scaled yet).
        void FitColourGrid()
        {
            if (breakBaseHeight == 0) return;
            int rowH = BreakRowHeight();
            // "kr/kg" (GDI+ PlainLabel, stretched to the row) starts where the centred grams label starts: same baseline
            // as the grams, the cost, the drop-down text and the box digits (all on the labels' text line)
            int labelTop = Math.Max(0, (rowH - rowGrams[0].PreferredHeight) / 2);
            // one grams column width for 1 and 2+ colours, so the × / kr/kg box / unit never move ("000,00 g" fits)
            int gramsW = TextRenderer.MeasureText("000,00 g", rowGrams[0].Font).Width + rowGrams[0].Padding.Horizontal;
            colourGrid.SuspendLayout();
            for (int i = 0; i < MaxColourRows; i++)
            {
                colourGrid.RowStyles[i].SizeType = SizeType.Absolute;
                colourGrid.RowStyles[i].Height = i < colourRows ? rowH : 0;
                rowUnit[i].Padding = new Padding(0, labelTop, 0, 0);
                rowGrams[i].MinimumSize = new Size(gramsW, 0);
                // drop-down and box Top-anchored at the same y, centred by their REAL heights (the table centred the
                // combo by a smaller height: 2 px low, and the box sat at the row's top: 1-3 px high)
                rowCombo[i].Margin = new Padding(0, Math.Max(0, (rowH - rowCombo[i].Height) / 2), 16, 0);
                rowPrice[i].Margin = new Padding(0, Math.Max(0, (rowH - rowPrice[i].Height) / 2), 0, 0);
            }
            colourGrid.Height = Math.Max(1, colourRows) * rowH;   // = the rows exactly: no left-over space to stretch the last row
            colourGrid.ResumeLayout(true);
            foreach (var b in rowPrice) b.Invalidate();   // a box moved into a newly sized row repaints (its themed frame too)
        }

        int ScalePx(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

        /// One row height for the whole Cost breakdown (material rows, Power, Failed, Base cost, payback): 28 px at 96 dpi,
        /// at least a kr/kg box + 3 px.
        int BreakRowHeight() { return Math.Max(ScalePx(28), rowPrice[0].Height + ScalePx(3)); }

        /// Breakdown table rows 2-5 (Power, Failed, Base cost, payback) get the same fixed height; a hidden Failed /
        /// payback row is 0 (Absolute rows don't collapse by themselves). Before Load: AutoSize rows.
        void SetBreakRows()
        {
            if (breakBaseHeight == 0) return;
            bool[] on = { true, settings.FailedPrintPct > 0, true, paybackRowOn };
            for (int i = 0; i < 4; i++)
            {
                var st = breakTable.RowStyles[2 + i];
                st.SizeType = SizeType.Absolute;
                st.Height = on[i] ? breakRowHeight : 0;
            }
        }

        /// Row i's drop-down shows `name`; the full list follows when the box is entered or opened (EnsureMaterialList),
        /// so startup and new jobs don't read Bambu Studio's profile folders. choosable false = fixed, disabled text ("+N more").
        void SetMaterialBox(int i, string name, bool choosable)
        {
            var cb = rowCombo[i];
            cb.BeginUpdate();
            cb.Items.Clear();
            cb.Items.Add(name);
            cb.SelectedIndex = 0;
            cb.Enabled = choosable;
            cb.EndUpdate();
            cb.Tag = null;   // list not filled yet
        }

        /// Fills row i's drop-down with the A1 filament list (once per row and job) and keeps its material selected
        /// (added in its sorted place when Bambu Studio doesn't know it, e.g. a sliced file's profile).
        void EnsureMaterialList(int i)
        {
            var cb = rowCombo[i];
            if (cb.Tag != null || !cb.Enabled || RowFilament(i) == null) return;
            cb.Tag = "full";
            bool was = fillingColours;
            fillingColours = true;
            FillMaterialBox(i, MaterialName(RowFilament(i)), FilamentNames());
            fillingColours = was;
        }

        void FillMaterialBox(int i, string name, List<string> names)
        {
            var cb = rowCombo[i];
            cb.BeginUpdate();
            cb.Items.Clear();
            var items = new List<string>(names);   // already sorted (Bambu Studio's names, case-insensitive)
            string match = items.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            int width = filamentNamesWidth;
            if (match == null)
            {
                match = name;
                int at = items.FindIndex(n => string.Compare(n, name, StringComparison.OrdinalIgnoreCase) > 0);
                items.Insert(at < 0 ? items.Count : at, name);
                width = Math.Max(width, TextRenderer.MeasureText(name, cb.Font).Width);
            }
            cb.Items.AddRange(items.ToArray());
            cb.SelectedIndex = cb.Items.IndexOf(match);
            cb.EndUpdate();
            // the open list shows the full names (the closed box ends in "…" when a name is longer than 190 px)
            cb.DropDownWidth = Math.Max(cb.Width, width + SystemInformation.VerticalScrollBarWidth + cb.Height / 2);
        }

        /// Bambu Studio's A1 filament profiles for the chosen nozzle, short names (as Settings lists them); read once per
        /// slicer + nozzle. Empty when Bambu Studio isn't found (the rows then list only their own name).
        List<string> FilamentNames()
        {
            string exe = BambuSlicer.FindExe(settings.SlicerPath);
            string size = Wear.FindNozzle(settings.Nozzle).Size;
            string key = (exe ?? "") + "|" + size;
            if (filamentNames != null && key == filamentNamesKey) return filamentNames;
            filamentNamesKey = key;
            try { filamentNames = exe == null ? new List<string>() : BambuSlicer.ListFilaments(exe, size); }
            catch { filamentNames = new List<string>(); }
            filamentNamesWidth = 0;
            foreach (var n in filamentNames) filamentNamesWidth = Math.Max(filamentNamesWidth, TextRenderer.MeasureText(n, rowCombo[0].Font).Width);
            return filamentNames;
        }

        /// The material behind row i (one colour: `single`; 2+: that colour); null for a "+N more" row.
        FilamentUse RowFilament(int i)
        {
            if (i >= colourRows) return null;
            if (filaments == null) return i == 0 ? single : null;
            var items = rowItems[i];
            return items != null && items.Count == 1 ? filaments[items[0]] : null;
        }

        /// A material picked in row i: the row's material changes (saved with the quote) and its kr/kg box fills with
        /// that material's remembered price. No re-slice; Settings' filament profile stays the default for new slices.
        void MaterialChosen(int i)
        {
            var name = rowCombo[i].SelectedItem as string;
            var f = RowFilament(i);
            if (name == null || f == null) return;
            f.Profile = name;
            try
            {
                string type; bool abrasive;
                BambuSlicer.FilamentInfo(BambuSlicer.FindExe(settings.SlicerPath), name, Wear.FindNozzle(settings.Nozzle).Size, out type, out abrasive);
                if (type.Length > 0) f.Type = type;   // "PETG" for the history; unknown name: keeps the old type
            }
            catch { }
            fillingColours = true;
            rowPrice[i].Text = Num.Fmt(settings.PriceFor(name));
            fillingColours = false;
            Recalculate();
        }

        /// Typing a kr/kg in a row remembers it for that row's material (saved with the settings); also the fallback
        /// price for materials without one. "+N more" rows (mixed materials) remember nothing.
        void RememberRowPrice(int i)
        {
            var f = RowFilament(i);
            double v;
            if (f == null || !Num.TryParse(rowPrice[i].Text, out v)) return;
            settings.RememberPrice(MaterialName(f), v);
            settings.LastFilamentPrice = v;
        }

        /// "incl. 124 g purge · 8,7 h colour changes"; only the parts that are known (purge only above 0,5 g). "" = no line.
        static string PurgeNote(SliceResult r)
        {
            var parts = new List<string>();
            if (r.PurgeGrams > 0.5)
                parts.Add(L.F("incl. {0} g purge", "heraf {0} g udskylning", r.PurgeGrams.ToString(r.PurgeGrams >= 10 ? "0" : "0.#", Num.Dk)));
            if (r.ColourChangeHours >= 0.05)
                parts.Add(L.F("{0} h colour changes", "{0} t farveskift", r.ColourChangeHours.ToString("0.#", Num.Dk)));
            return string.Join(" · ", parts);
        }

        /// Material name of a filament: the profile's short name ("Bambu PLA Matte", "@BBL A1 …" dropped), else its type
        /// ("PLA"), else "Colour 3". Also the key of its remembered kr/kg.
        static string MaterialName(FilamentUse f)
        {
            if (!string.IsNullOrEmpty(f.Profile)) return BambuSlicer.ShortName(f.Profile).Trim();
            if (!string.IsNullOrEmpty(f.Type)) return f.Type;
            return L.F("Colour {0}", "Farve {0}", f.Slot);
        }

        /// "#RRGGBB" (or "#RRGGBBAA") -> colour; Color.Empty if unknown.
        static Color ParseColour(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.Empty;
            string h = hex.Trim().TrimStart('#');
            int v;
            if (h.Length < 6 || !int.TryParse(h.Substring(0, 6), System.Globalization.NumberStyles.HexNumber, null, out v)) return Color.Empty;
            return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }

        // ---------- Printer card ----------
        /// Card size and place: from the Print time box's top to the Electricity price box's bottom, right edge at the
        /// live-price box's right edge (measured, DPI-safe). Anchor Top|Left: the margins place it in its 3-row cell.
        void LayoutPrinterCard()
        {
            if (!printerCardReady) return;
            var t = (TableLayoutPanel)printerCard.Parent;
            // small first, so an older, taller card doesn't stretch the rows we measure
            printerCard.Margin = Padding.Empty;
            printerCard.FitTo(1);
            t.PerformLayout();
            Func<Control, Rectangle> inTable = c => t.RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
            Rectangle hours = inTable(txtHours), elec = inTable(txtElecPrice);
            int right = inTable(txtUrl).Right;   // = the live-price box's, the buttons' and the Cost breakdown's right edge
            printerCard.FitTo(Math.Max(1, elec.Bottom - hours.Top));   // Print time box top to Electricity price box bottom
            int cellLeft = printerCard.Left, cellTop = printerCard.Top;   // margins are 0 here
            printerCard.Margin = new Padding(Math.Max(0, right - printerCard.Width - cellLeft), Math.Max(0, hours.Top - cellTop), 0, 0);
        }

        /// Bambu Studio's own A1 picture from its install folder (read at runtime, nothing of it is in our exe);
        /// null = the card draws a simple A1 itself.
        static Bitmap LoadPrinterPhoto(string exe)
        {
            try
            {
                if (exe == null) return null;
                string f = Path.Combine(Path.GetDirectoryName(exe), "resources", "images", "printer_thumbnail_n2s_png.png");
                return File.Exists(f) ? PrepareModelPicture(File.ReadAllBytes(f)) : null;
            }
            catch { return null; }
        }

        /// Dark tooltip (the standard one is light).
        static void DrawDarkTip(object sender, DrawToolTipEventArgs e)
        {
            using (var b = new SolidBrush(Theme.InfoBack)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        /// Clickable printer card: photo on top, "Bambu Lab A1 · 100 W" centred under it, framed like an info box
        /// (rounded; lighter fill, border and text on hover; hand cursor). No photo = a simple A1 drawn in the theme colours.
        sealed class PrinterCard : Control
        {
            static readonly Color HotBack = Color.FromArgb(52, 55, 60), HotBorder = Color.FromArgb(110, 110, 116);
            const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter;
            Bitmap photo;
            int photoHeight;   // device px, set by FitTo
            bool hot;

            public PrinterCard()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.PushButton;
            }

            public Bitmap Photo
            {
                get { return photo; }
                set { if (photo != null && photo != value) photo.Dispose(); photo = value; Invalidate(); }
            }

            int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }
            int PhotoWidth(int h) { return photo != null ? (int)Math.Round(h * (double)photo.Width / photo.Height) : h * 24 / 28; }

            /// Card exactly `height` px tall (at least photo 16 px + text): photo as big as fits (at most 88 px at 96 dpi),
            /// one text line under it.
            public void FitTo(int height)
            {
                Size ts = TextRenderer.MeasureText(Text.Length > 0 ? Text : "Ag", Font, Size.Empty, TextFlags);
                int rest = Px(5) + Px(3) + ts.Height + Px(5);
                photoHeight = Math.Max(Px(16), Math.Min(Px(88), height - rest));
                Size = new Size(Math.Max(PhotoWidth(photoHeight), ts.Width) + Px(20), Math.Max(height, photoHeight + rest));
                Invalidate();
            }

            protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
            protected override void OnClick(EventArgs e) { hot = false; Invalidate(); base.OnClick(e); }   // Settings opens over it: no stuck hover

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Back);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int rad = Px(6), w = Width - 1, h = Height - 1;
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddArc(0, 0, rad, rad, 180, 90);
                    path.AddArc(w - rad, 0, rad, rad, 270, 90);
                    path.AddArc(w - rad, h - rad, rad, rad, 0, 90);
                    path.AddArc(0, h - rad, rad, rad, 90, 90);
                    path.CloseFigure();
                    using (var b = new SolidBrush(hot ? HotBack : Theme.InfoBack)) g.FillPath(b, path);
                    using (var p = new Pen(hot ? HotBorder : Theme.Border)) g.DrawPath(p, path);
                }
                int ph = photoHeight > 0 ? photoHeight : Px(60), pw = PhotoWidth(ph);
                var pr = new Rectangle((Width - pw) / 2, Px(5), pw, ph);
                if (photo != null)
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(photo, pr);
                }
                else DrawA1(g, pr);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                int ty = pr.Bottom + Px(3);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(0, ty, Width, Math.Max(1, Height - ty)), hot ? Theme.Text : Theme.Muted, TextFlags);
            }

            /// Simple Bambu Lab A1 on a 24 × 28 grid: base + bed, frame, X gantry, red toolhead.
            static void DrawA1(Graphics g, Rectangle r)
            {
                float u = r.Height / 28f, x0 = r.X + (r.Width - 24 * u) / 2f, y0 = r.Y;
                Func<float, float, float, float, RectangleF> R = (x, y, rw, rh) => new RectangleF(x0 + x * u, y0 + y * u, rw * u, rh * u);
                using (var m = new SolidBrush(Theme.Muted))
                using (var d = new SolidBrush(Color.FromArgb(105, 105, 108)))
                using (var red = new SolidBrush(Theme.Others))
                {
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

        /// Small colour square with a thin border; several colours = equal vertical stripes; unknown colour = empty box.
        sealed class Swatch : Control
        {
            Color[] colours = new Color[0];
            public Swatch()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
            }
            public Color[] Colours { get { return colours; } set { colours = value ?? new Color[0]; Invalidate(); } }
            protected override void OnPaint(PaintEventArgs e)
            {
                var r = ClientRectangle;
                using (var b = new SolidBrush(Theme.Input)) e.Graphics.FillRectangle(b, r);
                var known = colours.Where(c => !c.IsEmpty).ToArray();
                for (int i = 0; i < known.Length; i++)
                {
                    int x0 = r.Width * i / known.Length, x1 = r.Width * (i + 1) / known.Length;
                    using (var b = new SolidBrush(known[i])) e.Graphics.FillRectangle(b, x0, 0, x1 - x0, r.Height);
                }
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        // ---------- UI helpers ----------
        /// Adds a row; returns its caption label (null for an empty caption).
        Label AddRow(TableLayoutPanel t, string caption, Control input, Control extra)
        {
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label cap = null;
            if (caption.Length > 0)   // no empty caption label, so rows holding a hidden info box collapse fully
            {
                cap = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, Theme.LabelGap, 6) };
                t.Controls.Add(cap, 0, row);
            }
            input.Anchor = AnchorStyles.Left;
            t.Controls.Add(input, 1, row);
            if (extra != null) { extra.Anchor = AnchorStyles.Left; t.Controls.Add(extra, 2, row); }
            return cap;
        }

        /// Keeps the buttons' total width (real px, after DPI scaling) and gives each one its measured text
        /// width plus an equal share of the spare room.
        static void SplitByText(Button[] buttons)
        {
            int total = 0, need = 0;
            var natural = new int[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                total += buttons[i].Width;
                natural[i] = TextRenderer.MeasureText(buttons[i].Text, buttons[i].Font).Width + buttons[i].Height / 2;
                need += natural[i];
            }
            int spare = total - need, used = 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                int w = i == buttons.Length - 1 ? total - used
                      : natural[i] + (spare >= 0 ? spare / buttons.Length : spare * natural[i] / need);
                buttons[i].Width = w;
                used += w;
            }
        }

        static Label Unit(string text)
        {
            return new Theme.PlainLabel { Text = text, AutoSize = true, Tag = "muted", Anchor = AnchorStyles.Left, Margin = new Padding(2, 6, 10, 0) };
        }

        static Control WithUnit(Control box, string unit)
        {
            var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            p.Controls.Add(box);
            p.Controls.Add(Unit(unit));
            return p;
        }

        static TextBox NumBox(string text)
        {
            return new TextBox { Width = 100, Text = text, TextAlign = HorizontalAlignment.Right };
        }

        static Label BreakCaption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 3, 3) };
        }

        static Label BreakLabel()
        {
            return new Label { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 3, 3, 3) };
        }

        Control PriceBox(string title, string colorTag, out Label price, out Label info)
        {
            var g = new ThemedGroupBox { Text = title, Width = 264, Height = 112, Margin = new Padding(0, 0, 12, 0) };
            price = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Tag = colorTag,
                Font = new Font("Segoe UI", 22f, FontStyle.Bold)
            };
            info = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleCenter, Tag = "muted" };
            g.Controls.Add(price);
            g.Controls.Add(info);
            return g;
        }

        void SetStatus(string text, bool success)
        {
            statusIsSuccess = success;
            lblStatus.Text = text;
            lblStatus.Visible = text.Length > 0;
            SetStatusColor();
        }

        void SetStatusColor()
        {
            if (lblStatus != null) lblStatus.ForeColor = statusIsSuccess ? Theme.Success : Theme.Error;
        }

        // ---------- Logic ----------
        static FieldState ReadInt(TextBox box, int max, out int value)
        {
            value = 0;
            string s = box.Text.Trim();
            FieldState st = s.Length == 0 ? FieldState.Empty
                          : (int.TryParse(s, out value) && value >= 0 && value <= max) ? FieldState.Ok
                          : FieldState.Invalid;
            if (st != FieldState.Ok) value = 0;
            box.BackColor = st == FieldState.Invalid ? Theme.InvalidBack : Theme.Input;
            return st;
        }

        void Recalculate()
        {
            if (fillingColours) return;
            double grams, filPrice = 0, elPrice;
            var s1 = Num.Read(txtGrams, out grams);
            FieldState s2;
            // material rows: one colour = row 0's kr/kg (its grams follow "Filament used");
            // 2+ colours = one line per colour with its own kr/kg (first row's price kept as the quote's filament price)
            List<FilamentLine> lines = null;
            if (filaments != null)
            {
                lines = new List<FilamentLine>(filaments.Count);
                for (int k = 0; k < filaments.Count; k++) lines.Add(null);
                bool invalid = false, empty = false;
                for (int i = 0; i < colourRows; i++)
                {
                    double p;
                    var st = Num.Read(rowPrice[i], out p);
                    if (st == FieldState.Invalid) invalid = true;
                    if (st == FieldState.Empty) empty = true;
                    if (i == 0) filPrice = p;
                    foreach (int k in rowItems[i]) lines[k] = new FilamentLine { Grams = filaments[k].Grams, PricePerKg = p };
                }
                s2 = invalid ? FieldState.Invalid : empty ? FieldState.Empty : FieldState.Ok;
            }
            else
            {
                s2 = Num.Read(rowPrice[0], out filPrice);
                rowGrams[0].Text = (s1 == FieldState.Ok ? Num.Fmt(grams) : "–") + " g";
            }
            var s3 = Num.Read(txtElecPrice, out elPrice);
            int hours, minutes;
            var sh = ReadInt(txtHours, 999, out hours);     // empty counts as 0
            var sm = ReadInt(txtMinutes, 59, out minutes);

            string problem = null;
            if (s1 == FieldState.Invalid || s2 == FieldState.Invalid || s3 == FieldState.Invalid)
                problem = L.S("Please enter valid, non-negative numbers.", "Indtast gyldige tal, ikke negative.");
            else if (sh == FieldState.Invalid || sm == FieldState.Invalid)
                problem = L.S("Print time: hours 0–999, minutes 0–59.", "Printtid: timer 0–999, minutter 0–59.");
            else if (s1 == FieldState.Empty || s2 == FieldState.Empty || s3 == FieldState.Empty)
                problem = "";   // incomplete, no error

            if (problem != null)
            {
                last = null;
                SetStatus(problem, false);
                lblPower.Text = lblFailed.Text = lblBase.Text = lblPayback.Text = "–";   // payback row keeps its place (no jump while typing)
                foreach (var c in rowCost) c.Text = "–";
                lblFriends.Text = lblOthers.Text = "–";
                btnCopy.Enabled = btnSave.Enabled = false;
                lastInput = null;
                UpdateReopenedLine();
                return;
            }

            var input = new QuoteInput
            {
                Hours = hours, Minutes = minutes, FilamentGrams = grams,
                FilamentDkkPerKg = filPrice, ElectricityDkkPerKwh = elPrice, Filaments = lines,
                PaybackDebt = PaybackDebt()
            };
            last = Pricing.Calculate(input, settings);
            lastInput = input;
            lastHours = hours; lastMinutes = minutes;

            if (!statusIsSuccess) SetStatus("", false);   // a green "✓ Saved…" stays until its timer clears it
            if (lines != null && last.FilamentCosts != null)
                for (int i = 0; i < colourRows; i++)
                    rowCost[i].Text = Num.Kr(rowItems[i].Sum(k => last.FilamentCosts[k]));
            else rowCost[0].Text = Num.Kr(last.MaterialCost);
            lblPower.Text = Num.Kr(last.PowerCost) + "  (" + last.PowerKwh.ToString("N2", Num.Dk) + " kWh)";
            lblFailed.Text = Num.Kr(last.FailedPrintCost);
            lblBase.Text = Num.Kr(last.BaseCost);
            UpdatePaybackRow();
            lblFriends.Text = Num.Kr(last.FriendsPrice);
            lblOthers.Text = Num.Kr(last.OthersPrice);
            btnCopy.Enabled = btnSave.Enabled = true;
            UpdateReopenedLine();
        }

        void RefreshSettingsLabels()
        {
            printerCard.Text = "Bambu Lab A1 · " + Num.Fmt(settings.PrinterWatts) + " W";
            printerCard.AccessibleName = printerCard.Text;
            string exe = BambuSlicer.FindExe(settings.SlicerPath);
            if (exe != printerPhotoExe)   // first time, or the slicer path changed in Settings
            {
                printerPhotoExe = exe;
                printerCard.Photo = LoadPrinterPhoto(exe);
            }
            LayoutPrinterCard();   // the text width may have changed
            string wear = settings.MachineRatePerHour > 0 ? L.F(", {0} kr/h wear", ", {0} kr/t slid", Num.Fmt(settings.MachineRatePerHour)) : "";
            lblFriendsInfo.Text = L.F("+{0} % markup", "+{0} % avance", Num.Fmt(settings.FriendsMarkupPct)) + wear;
            string o = L.F("+{0} % markup", "+{0} % avance", Num.Fmt(settings.OthersMarkupPct)) + wear;
            if (settings.MinimumPrice > 0) o += L.F(", min {0} kr.", ", min. {0} kr.", Num.Fmt(settings.MinimumPrice));
            lblOthersInfo.Text = o;
            UpdateFailedRow();
        }

        /// "Failed prints (+5 %)" line in the Cost breakdown: shown only when the setting is above 0 %;
        /// the box grows/shrinks by one row (the window follows, it is GrowAndShrink).
        void UpdateFailedRow()
        {
            bool on = settings.FailedPrintPct > 0;
            lblFailedCap.Text = L.F("Failed prints (+{0} %)", "Fejlprint (+{0} %)", Num.Fmt(settings.FailedPrintPct));
            lblFailedCap.Visible = lblFailed.Visible = on;
            UpdateBreakHeight();
        }

        /// Cost breakdown height: 3 rows, + the failed-prints and payback rows, with the material rows (+ purge line for
        /// 2+ colours) in place of one plain row. The window follows (GrowAndShrink).
        void UpdateBreakHeight()
        {
            if (breakBaseHeight == 0) return;
            SetBreakRows();
            // the designed height holds 3 plain rows; the real rows are: material rows (+ purge line), then Power, Base cost
            // (+ Failed, + payback) at one fixed row height each
            int rows = 2 + (settings.FailedPrintPct > 0 ? 1 : 0) + (paybackRowOn ? 1 : 0);
            int h = breakBaseHeight - 3 * plainRowHeight + rows * breakRowHeight + colourGrid.Height + colourGrid.Margin.Vertical;
            if (lblPurge.Text.Length > 0) h += lblPurge.PreferredHeight + lblPurge.Margin.Vertical;   // text "" = hidden
            grpBreak.Height = h;
        }

        /// Debt of failed prints for this quote: 0 while payback is off; the record this job saved is left out.
        /// Picture mode: the made-up sampleDebt (never the file).
        double PaybackDebt()
        {
            if (settings.FailedPayback == 0) return 0;
            if (offline) return sampleDebt;
            EnsureHistory();
            return History.FailedDebt(history, savedId);
        }

        /// "Failed prints payback   5,68 kr.  (28,03 kr. left)" under Base cost, only while a payback is added
        /// (caption + " – customers only" when the friends price has none). The box grows/shrinks by one row.
        void UpdatePaybackRow()
        {
            bool on = last != null && last.PaybackCost > 0;
            if (on)
            {
                lblPaybackCap.Text = last.PaybackBothPrices ? L.S("Failed prints payback", "Fejlprint tjenes ind")
                                                            : L.S("Failed prints payback – customers only", "Fejlprint tjenes ind – kun kunder");
                lblPayback.Text = L.F("{0}  ({1} left)", "{0}  ({1} tilbage)", Num.Kr(last.PaybackCost), Num.Kr(last.PaybackLeft));
            }
            if (on == paybackRowOn) return;
            paybackRowOn = on;
            lblPaybackCap.Visible = lblPayback.Visible = on;
            UpdateBreakHeight();
        }

        /// Picture mode: a made-up debt of failed prints with payback on (0 = back to the defaults: off, no debt).
        internal void SetSamplePayback(double debt)
        {
            if (!offline) return;
            sampleDebt = debt;
            settings.FailedPayback = debt > 0 ? 1 : 0;
            Recalculate();
        }

        void OpenSettings()
        {
            EnsureHistory();   // for "Not made back yet: x kr." (picture mode: empty list)
            using (var dlg = new SettingsForm(settings, offline ? sampleDebt : History.FailedDebt(history, null)))
            {
                if (!offline) dlg.UpdateFound = ShowUpdateButton;   // "check now" in Settings
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var old = settings;
                    settings = dlg.Result;
                    if (old.AutoUpdateCheck == 0 && settings.AutoUpdateCheck != 0 && pendingUpdate == null)
                        BeginInvoke((MethodInvoker)StartupUpdateCheck);   // switched back on: check now
                    SyncUpdateTimer();   // the 10-minute check follows the setting
                    bool powerChanged = old.PriceArea != settings.PriceArea || old.PowerAddOn != settings.PowerAddOn ||
                                        old.PowerIncludeVat != settings.PowerIncludeVat ||
                                        (old.AutoPowerPrice == 0 && settings.AutoPowerPrice != 0);
                    if (powerChanged && settings.AutoPowerPrice != 0) BeginInvoke((MethodInvoker)FetchPowerPrice);
                    SaveLastPrices();
                    RefreshSettingsLabels();
                    foreach (var cb in rowCombo) cb.Tag = null;   // nozzle / slicer may have changed: re-read the list when opened
                    if (!jobLoaded && cts == null) SetColours(null);   // no model yet: the row follows Settings' filament profile
                    Recalculate();
                    if (cts == null) ShowAutoIdle();
                    if (dlg.RestartRequested && !offline)
                    {
                        // new language: settings are already saved above; Restart closes this window (saves again) and starts anew
                        if (cts != null) cts.Cancel();
                        BeginInvoke((MethodInvoker)Program.Restart);   // same arguments (test mode stays in its folder)
                    }
                }
            }
        }

        // ---------- updates from GitHub ----------
        /// Removes leftovers of a previous update (local only), then, if the setting allows it,
        /// quietly looks for a new release. A newer version shows the red update button under the Friends & family box.
        async void StartupUpdateCheck()
        {
            if (offline || AppSettings.TestMode) return;
            bool check = settings.AutoUpdateCheck != 0;
            UpdateInfo found = null;
            try
            {
                found = await Task.Run(() =>
                {
                    Updater.CleanUp();
                    return check ? Updater.CheckForUpdate() : null;
                });
            }
            catch { /* offline or GitHub unreachable: try again next start */ }
            if (found == null || IsDisposed) return;
            ShowUpdateButton(found);
        }

        /// The 10-minute timer runs only while "Check for new versions automatically" is on.
        void SyncUpdateTimer()
        {
            if (updateTimer == null) return;
            updateTimer.Enabled = settings.AutoUpdateCheck != 0;
        }

        /// Every 10 minutes while running: a quiet check (ETag cache, never throws). Skipped while the setting is off,
        /// the update dialog is open (download/install), a slice job runs, the last check is still busy, or GitHub
        /// asked us to back off. Only ever shows (or raises) the red button, never hides it.
        async void PeriodicUpdateCheck()
        {
            if (offline || AppSettings.TestMode || IsDisposed || settings.AutoUpdateCheck == 0 || updateDialogOpen || cts != null || checkingUpdate) return;
            if (DateTime.UtcNow < Updater.NextAllowedUtc) return;
            checkingUpdate = true;
            UpdateInfo found = null;
            try { found = await Task.Run(() => Updater.CheckQuiet()); }
            catch { }
            finally { checkingUpdate = false; }
            if (found == null || IsDisposed || found.Version == null) return;
            if (pendingUpdate == null || pendingUpdate.Version == null || found.Version > pendingUpdate.Version) ShowUpdateButton(found);
        }

        internal void ShowUpdateButton(UpdateInfo found)
        {
            pendingUpdate = found;
            btnUpdate.Text = L.S("⬆  Update to v", "⬆  Opdatér til v") + Updater.Short(found.Version);
            btnUpdate.Visible = true;
        }

        /// What's new + Update now / Later. Later keeps the button.
        void ShowUpdate(UpdateInfo found)
        {
            if (offline) return;
            updateDialogOpen = true;   // no background checks while it may be downloading / installing
            try
            {
                using (var dlg = new UpdateForm(found, SaveLastPrices))
                    if (dlg.ShowDialog(this) == DialogResult.OK) Close();   // the new version is already starting
            }
            finally { updateDialogOpen = false; }
        }

        void SaveLastPrices()
        {
            if (offline) return;   // picture mode never writes settings.ini
            double v;   // filament kr/kg: remembered per material as it is typed (RememberRowPrice)
            if (Num.TryParse(txtElecPrice.Text, out v)) settings.LastElectricityPrice = v;
            settings.Save();
        }

        void CopyQuote()
        {
            if (last == null) return;
            string nl = Environment.NewLine;
            string text = L.S("3D print quote", "Tilbud på 3D-print") + nl;
            if (txtUrl.Text.Trim().Length > 0) text += "Model: " + txtUrl.Text.Trim() + nl;
            text += L.F("Print time: {0} h {1} m, filament: {2} g", "Printtid: {0} t {1} m, filament: {2} g",
                        lastHours, lastMinutes, txtGrams.Text.Trim()) +
                    (filaments != null ? L.F(" ({0} colours)", " ({0} farver)", filaments.Count) : "") + nl;
            text += L.S("Friends & family: ", "Venner og familie: ") + Num.Kr(last.FriendsPrice) + nl;
            text += L.S("Customer: ", "Kunde: ") + Num.Kr(last.OthersPrice);
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                // hidden test start: no message box (it would pop up on screen), red status line instead
                if (Program.Hidden) SetStatus(L.S("Clipboard: ", "Udklipsholder: ") + ex.Message, false);
                else MessageBox.Show(this, ex.Message, L.S("Clipboard", "Udklipsholder"));
                return;
            }
            // a copied quote is one that was sent: it goes into the price history too
            string err = SaveToHistory();
            if (err == null) FlashStatus(L.S("✓ Quote copied to clipboard and saved to history.", "✓ Tilbuddet er kopieret og gemt i historikken."));
            else SetStatus(L.S("Quote copied to clipboard. ", "Tilbuddet er kopieret. ") + err, false);
        }

        // ---------- price history ----------
        /// Green ✓ text in the status line that clears itself after 4 seconds.
        void FlashStatus(string msg)
        {
            SetStatus(msg, true);
            if (statusTimer == null)
            {
                statusTimer = new System.Windows.Forms.Timer { Interval = 4000 };
                statusTimer.Tick += delegate
                {
                    statusTimer.Stop();
                    if (statusIsSuccess) SetStatus("", false);
                };
            }
            statusTimer.Stop();
            statusTimer.Start();
        }

        void SaveClicked()
        {
            string err = SaveToHistory();
            if (err == null) FlashStatus(L.S("✓ Saved to history.", "✓ Gemt i historikken."));
            else SetStatus(err, false);
        }

        /// Loads history.json the first time it is needed (picture mode: an empty list in memory, never the file).
        void EnsureHistory()
        {
            if (history != null) return;
            if (offline) { history = new List<QuoteRecord>(); return; }
            history = History.Load(out historyProblem);
        }

        /// Adds the quote on screen to the history and writes the file. null = OK, else the error text.
        /// The same quote again within 10 minutes replaces the last entry (History.Add).
        string SaveToHistory()
        {
            if (last == null || lastInput == null) return L.S("Nothing to save yet.", "Der er ikke noget at gemme endnu.");
            EnsureHistory();
            var r = History.FromQuote(lastInput, last, settings, jobModel ?? "", txtUrl.Text, DateTime.Now);
            // colour, type and material of each row, so "Open again" can rebuild the material rows without slicing
            var used = filaments ?? (single != null ? new List<FilamentUse> { single } : null);
            if (r.Filaments != null && used != null && r.Filaments.Count == used.Count)
            {
                for (int k = 0; k < used.Count; k++)
                {
                    r.Filaments[k].Colour = used[k].Colour ?? "";
                    r.Filaments[k].Type = used[k].Type ?? "";
                    r.Filaments[k].Name = used[k].Profile ?? "";
                }
                if (filaments != null) r.PurgeGrams = jobPurge;
            }
            if (reopened != null) r.For = reopened.For;   // opened again and re-saved: same person as before
            savedId = History.Add(history, r).Id;   // left out of the payback debt from now on (same price when saved again)
            if (offline) return null;   // picture mode never writes
            return History.Save(history);
        }

        void OpenHistory()
        {
            if (cts != null) return;
            EnsureHistory();
            string problem = historyProblem;
            historyProblem = null;   // shown once
            using (var dlg = new HistoryForm(history, problem, offline))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Selected != null) Reopen(dlg.Selected);
                else Recalculate();   // Failed / For / Delete in the list change the payback debt
            }
        }

        /// "Open again": puts a saved quote's inputs back (time, grams, colour rows with their kr/kg, filament and
        /// electricity price, link/file, model name; no slicing) and recalculates with today's settings.
        /// The model info box shows where it came from and the saved price next to today's.
        internal void Reopen(QuoteRecord r)
        {
            if (r == null || cts != null) return;
            reopened = null;   // no half-updated lines of an earlier one while the boxes fill
            savedId = null;    // saving it again is a new sale: it pays back again
            SetUrlHint(false);
            txtUrl.Text = r.Source ?? "";
            jobModel = string.IsNullOrEmpty(r.Model) ? null : r.Model;   // after the link (its TextChanged clears it)
            ResetModelPicture();
            SetJobLoaded(true);   // no picture (Printables link, missing file) = logo without "No model yet"
            string src = (r.Source ?? "").Trim().Trim('"');
            if (!offline && src.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) && File.Exists(src))
                LoadModelPicture(() => BambuSlicer.FromProject(src), true);   // a local project still has its own picture

            SliceResult slice = null;
            var one = History.SingleLine(r);   // one colour saved with its material (null: old record or 2+ colours)
            if (one != null)
            {
                // Grams > 0 so SetColours keeps it; the row's grams follow "Filament used" anyway
                slice = new SliceResult { PurgeGrams = -1, ColourChangeHours = -1 };
                slice.Filaments.Add(new FilamentUse { Slot = 1, Colour = one.Colour ?? "", Type = one.Type ?? "", Profile = one.Name ?? "",
                                                      Grams = Math.Max(one.Grams, 0.01), ModelGrams = -1 });
            }
            else if (r.Filaments != null && r.Filaments.Count >= 2)
            {
                slice = new SliceResult { PurgeGrams = r.PurgeGrams, ColourChangeHours = -1 };
                for (int k = 0; k < r.Filaments.Count; k++)
                {
                    var f = r.Filaments[k];
                    slice.Filaments.Add(new FilamentUse { Slot = k + 1, Colour = f.Colour ?? "", Type = f.Type ?? "", Profile = f.Name ?? "",
                                                          Grams = f.Grams, ModelGrams = -1 });
                }
            }
            SetColours(slice);   // old record (no saved material): neutral swatch + Settings' filament profile
            fillingColours = true;   // the saved kr/kg, not the material's remembered one (and not remembered again)
            if (filaments != null)
                for (int i = 0; i < colourRows; i++)   // "+N more" row: one shared box, the first of its colours' price
                    rowPrice[i].Text = Num.Fmt(r.Filaments[filaments[rowItems[i][0]].Slot - 1].PricePerKg);
            else rowPrice[0].Text = Num.Fmt(one != null ? one.PricePerKg : r.FilamentDkkPerKg);
            fillingColours = false;
            txtGrams.Text = Num.Fmt(r.Grams);
            txtHours.Text = r.Hours.ToString();
            txtMinutes.Text = r.Minutes.ToString();
            txtElecPrice.Text = r.ElectricityDkkPerKwh.ToString("0.00##", Num.Dk);
            SetPowerInfo(L.F("Electricity price {0} kr/kWh from the saved quote. Click Update for today's.",
                             "Elpris {0} kr/kWh fra det gemte tilbud. Klik Opdatér for dagens.",
                             r.ElectricityDkkPerKwh.ToString("0.00", Num.Dk)), false);
            autoIsError = false;
            SetAutoColor();
            Recalculate();
            reopened = r;   // after Recalculate/SetAuto, which clear it
            UpdateReopenedLine();
        }

        /// "↻ Opened from history: …, saved …" + muted "Saved price a / b kr. — now c / d kr. (today's settings)".
        /// Follows every recalculation (typing, new settings) while the quote is open.
        void UpdateReopenedLine()
        {
            if (reopened == null) return;
            var r = reopened;
            DateTime t;
            string when = History.TryTime(r.SavedAt, out t) ? t.ToString("dd-MM-yyyy HH:mm", Num.Dk) : r.SavedAt;
            string name = !string.IsNullOrEmpty(r.Model) ? "\"" + r.Model + "\""
                        : (r.Source ?? "").Length > 0 ? Path.GetFileName((r.Source ?? "").Trim().Trim('"')) : L.S("a quote", "et tilbud");
            string main = L.F("↻ Opened from history: {0}, saved {1}.", "↻ Åbnet fra historikken: {0}, gemt {1}.", name, when);
            Func<double, string> n = v => v.ToString("0.00", Num.Dk);
            string now = last != null ? n(last.FriendsPrice) + " / " + n(last.OthersPrice) + " kr." : "–";
            string muted = L.F("Saved price {0} / {1} kr. — now {2} (today's settings)", "Gemt pris {0} / {1} kr. — nu {2} (dagens indstillinger)",
                               n(r.FriendsPrice), n(r.OthersPrice), now);
            if (lblAuto.Text != main || lblAuto.MutedLine != muted)
            {
                lblAuto.SetText(main, muted);
                SetAutoColor();
            }
        }

        void ClearJob()
        {
            reopened = null;
            savedId = null;
            if (lblAuto.MutedLine.Length > 0) ShowAutoIdle();   // the "Opened from history" lines go with the job
            txtUrl.Text = "";
            SetUrlHint(false);
            SetColours(null);   // back to the single-filament layout
            txtGrams.Text = "";
            txtHours.Text = "0";
            txtMinutes.Text = "0";
            ResetModelPicture();   // back to the logo
            SetJobLoaded(false);   // with "No model yet" again
            txtUrl.Focus();
        }
    }
}
