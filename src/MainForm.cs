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
        // quote export (idea 5 + 21): "Show quote… ▾" split button (btnCopy) + its menu (section "quote export" after QuoteText)
        ContextMenuStrip quoteMenu;
        ToolStripMenuItem miQuoteInfo;   // muted first line of the menu: "No name · customer price · dark"
        DateTime quoteWhen;              // date/number the quote window shows (now, or the saved record's time)
        int copyArrowWidth;          // the ▾ part of btnCopy (real px, set on Load)
        int saveCopyPair;            // Save to history + Show quote… ▾ together (the Customers box width minus the gap; real px, Load)
        // --test-invoice only (TestInvoiceFlow): the file dialog's answer (kind, suggested name -> path, null = cancelled) and
        // the clipboard (text / "[picture]"), so a test run never opens a dialog or touches the user's clipboard
        static Func<QuoteExport, string, string> testExportPath;
        Action<string> testClipboard;
        // --test-bill only (TestBillFlow): History's Quote… / Invoice… window is handed to this instead of ShowDialog
        Action<QuoteForm> testRecordsForm;
        bool copyArrowPressed;       // the mouse press was on ▾: the Click that follows opens the menu instead of copying
        Panel leftCell;                   // left of the bottom buttons: update button or "Made by Gettic · GitHub"
        int autoMinHeight;                // the info box's normal minimum height (2 lines); raised to its height while a job runs (idea 26)
        FlowLayoutPanel bottomButtons;
        Label lblMadeBy;
        Theme.LinkText lnkGitHub;
        UpdateInfo pendingUpdate;   // newer version found at startup (null = none)
        System.Windows.Forms.Timer updateTimer;   // quiet check every 10 min while running (null in picture mode)
        bool checkingUpdate, updateDialogOpen;
        QuoteResult last;
        QuoteInput lastInput;   // the input behind last (saved to the history)
        int lastHours, lastMinutes;
        bool statusIsSuccess;
        System.Windows.Forms.Timer statusTimer;   // clears a green ✓ status line after a few seconds

        // price history (history.json next to settings.ini; loaded on first use, never in picture mode)
        Button btnHistory, btnSave, btnSettings;
        List<QuoteRecord> history;
        string historyProblem;          // load problem, shown once in the history window
        string jobModel;                // model name of the current job (Printables name / file name), null = none
        double jobPurge = -1;           // purge grams of the current multi-colour job (-1 = unknown)
        double jobTowerGrams = -1, jobTowerSeconds = -1;   // issue #8 step 3: its prime tower (-1 = unknown, 0 = none)
        QuoteRecord reopened;           // quote opened from the history (its line stays in the model info box)
        QuoteRecord repeatOf;           // "Repeat print": the original (saving sets RepeatOf + its For); cleared by a new link, Clear, Open again
        QuoteRecord repeatLine;         // the repeat's "Was → now" lines in the model info box (cleared by SetAuto)
        string repeatWhy;               // null = sliced again; else why not (warning stripe, saved time and weight kept)
        string elecToday;               // the electricity box before "Open again" put a saved price in (Repeat print goes back to it)
        bool elecSaved;                 // the electricity box holds a saved quote's price

        // failed prints payback (idea 9): the record this job saved is left out of the debt, so saving it again keeps the price
        string savedId;                 // Id of the history record the current job saved (null = none yet); cleared by a new job, ClearJob, Reopen
        double sampleDebt;              // picture mode only: made-up debt (history is empty there)
        Label lblPaybackCap, lblPayback;   // Cost breakdown row "Failed prints payback", shown only while a payback is added
        bool paybackRowOn;

        // auto-detect
        Button btnAnalyze, btnLoadFile;
        InfoBox lblAuto;
        bool autoIsError;
        bool autoWarning;   // warning stripe: printer changed after slicing, or a file sliced for another printer (SetAuto clears it)
        CancellationTokenSource cts;

        // "Open in Studio" (idea 13): after a successful slice the first top button opens the sliced file in Bambu Studio
        string studioFile;              // file the button opens (null = the button is "Get time & weight")
        Bitmap studioIcon, studioIconGrey;   // Bambu Studio's icon at 16 px (scaled) + grey copy for disabled; null = text only
        string studioIconExe = "?";     // slicer exe the icon was read from ("?" = not yet)
        int studioClickTick;            // Environment.TickCount of the last Open click (2 s guard against double launches)
        bool studioClicked;

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
            // a new link = no model name yet, and the button slices again (no "Open in Studio" for the old file)
            txtUrl.TextChanged += delegate
            {
                if (linkFile != null && !LinkShowsFile()) { linkFile = null; if (printerTip != null) printerTip.SetToolTip(txtUrl, null); }
            };
            txtUrl.TextChanged += delegate
            {
                SetUrlHint(false); jobModel = null; jobParts = null; fromExample = false; SetStudioFile(null); repeatOf = null;
                jobScale = ScaleXyz.None; lastJob = null; jobTower = SettingsTower; jobTowerKnown = true;   // issue #8: a new link / file starts at 100 %
            };
            AddRow(inputs, L.S("Model link", "Link til model"), txtUrl, null);
            inputs.SetColumnSpan(txtUrl, 2);

            // three equal buttons lined up exactly under the link box (widths set on Load)
            btnAnalyze = new Button { Text = AnalyzeText, Width = 128, Height = 32, Margin = new Padding(3, 6, 6, 4) };
            btnLoadFile = new Button { Text = L.S("Load file…", "Åbn fil…"), Width = 128, Height = 32, Margin = new Padding(0, 6, 6, 4) };
            btnHistory = new Button { Text = L.S("History…", "Historik…"), Width = 128, Height = 32, Margin = new Padding(0, 6, 0, 4) };
            btnAnalyze.Click += delegate { AnalyzeClicked(); };
            btnAnalyze.Paint += (s, e) => PaintStudioIcon(e.Graphics);
            btnLoadFile.Click += delegate { BrowseForModel(); };
            btnHistory.Click += delegate { OpenHistory(); };
            // Enter in the link box = what the first button shows: "Get time & weight", or "Open in Studio" on an unchanged
            // sliced link (any edit switches back to slicing); not while a job runs, so it never cancels by accident
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
                    fromExample = true;   // after setting the text (TextChanged clears it): slice the 4-colour Benchy without the file picker
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
            lblAuto = new InfoBox(394, 9.5f) { Margin = new Padding(3, 4, 0, 8) };   // bottom 8: 11 px above the printer card (measured)
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
            lblMinutesUnit = Unit("m");
            timeRow.Controls.Add(lblMinutesUnit);
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
            var gramsRow = WithUnit(txtGrams, "g");
            lblGramsUnit = (Label)gramsRow.Controls[1];
            AddRow(inputs, L.S("Filament used", "Filamentforbrug"), gramsRow, null);
            // the filament's kr/kg is in its material row in the Cost breakdown (no "Filament price" row here)
            txtElecPrice = NumBox(Num.Fmt(settings.LastElectricityPrice));
            var elecRow = WithUnit(txtElecPrice, "kr/kWh");
            AddRow(inputs, L.S("Electricity price", "Elpris"), elecRow, null);
            lblPowerInfo = new InfoBox(394, 9.5f) { Margin = new Padding(3, 8, 0, 8) };   // top 8: 11 px under the printer card, as above it (measured)
            int powerInfoRow = inputs.RowCount;
            AddRow(inputs, "", lblPowerInfo, null);
            inputs.SetColumnSpan(lblPowerInfo, 2);
            // "Update" sits in the label column under "Electricity price", its top level with the live-price info box's top
            // (same top margin); height matched to the number boxes once the form has loaded (DPI-safe).
            // Slightly smaller font + no padding so the text fits inside a button only as tall as a text box.
            // The info box is always shown (empty = just the box with its stripe, one line tall), so the button and the
            // layout never move.
            lblPowerInfo.KeepWhenEmpty = true;
            lblPowerInfo.EmptyText = L.S("Click Update for today's price.", "Klik Opdatér for dagens pris.");   // qol #15: never a bare empty box
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
            printerCard.Click += delegate { ChoosePrinter(); };
            printerTip = new ToolTip { OwnerDraw = true, InitialDelay = 400 };
            printerTip.Draw += DrawDarkTip;
            printerTip.SetToolTip(printerCard, L.S("Choose printer", "Vælg printer"));
            printerCard.AccessibleDescription = L.S("Choose printer", "Vælg printer");
            inputs.Controls.Add(printerCard, 2, timeRowIndex);
            inputs.SetRowSpan(printerCard, 3);   // Print time, Filament used, Electricity price
            root.Controls.Add(inputs);

            // ---------- Breakdown ----------
            grpBreak = new ThemedGroupBox { Text = L.S("Cost breakdown", "Omkostninger"), Width = 540, Height = 110, Margin = new Padding(0, 0, 0, 8) };
            var bt = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2) };
            breakTable = bt;
            for (int i = 0; i < 7; i++) bt.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // rows 2-6 fixed on Load (SetBreakRows)
            // empty last row takes the box's spare height (~4 px at the bottom), so the last visible row is 28 px like the
            // others (a table stretches its last row: Base cost / payback sat 4 px low)
            bt.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bt.RowCount = 8;
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
            // batch-2 compact mode: the Power row folds into this one ("Base cost  incl. power 3,91 kr."), see SetCompact
            lblBasePower = new Label { AutoSize = true, Tag = "muted", UseMnemonic = false, Visible = false, Margin = new Padding(0, 3, 3, 3), Anchor = AnchorStyles.Left };
            var baseFlow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                                                 Margin = Padding.Empty, Padding = Padding.Empty, Anchor = AnchorStyles.Left };
            baseFlow.Controls.Add(baseCap);
            baseFlow.Controls.Add(lblBasePower);
            powerCap = (Label)bt.GetControlFromPosition(0, 2);
            bt.Controls.Add(baseFlow, 0, 4); bt.Controls.Add(lblBase, 1, 4);
            // failed prints payback: added after markup (not part of the base cost), hidden while 0, see UpdatePaybackRow
            lblPaybackCap = BreakCaption(""); lblPayback = BreakLabel();
            lblPaybackCap.Visible = lblPayback.Visible = false;
            bt.Controls.Add(lblPaybackCap, 0, 5); bt.Controls.Add(lblPayback, 1, 5);
            // batch & plates (qol #12): the plate change fee, hidden while 0, see UpdatePlateFeeRow
            lblPlateFeeCap = BreakCaption(""); lblPlateFee = BreakLabel();
            lblPlateFeeCap.Visible = lblPlateFee.Visible = false;
            bt.Controls.Add(lblPlateFeeCap, 0, 6); bt.Controls.Add(lblPlateFee, 1, 6);
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
                autoMinHeight = twoLines;
                lblAuto.MinimumSize = new Size(lblAuto.MinimumSize.Width, twoLines);
                picModel.Margin = new Padding(0, btnAnalyze.Margin.Top, 0, 0);
                picBaseHeight = btnAnalyze.Height + btnAnalyze.Margin.Bottom + lblAuto.Margin.Top + twoLines;
                picModel.Size = new Size(labelsRight - picModel.Left, picBaseHeight);
                // idea 25: the electricity "Update" button exactly as wide as the model picture (same column, both margin left 0)
                btnPower.Width = picModel.Width;
                FitModelPicture();
            };
            // a 3+ line info box: the picture box grows with it, so both still end at the same line
            lblAuto.SizeChanged += delegate { FitModelPicture(); };
            lblAuto.LocationChanged += delegate { FitModelPicture(); };
            picModel.Resize += delegate { picModel.Invalidate(); };
            Load += delegate { printerCardReady = true; LayoutPrinterCard(); };   // after the widths above are set
            // batch-2: too tall for the screen (e.g. 4 colours on 1080p at 150 %): compact mode (CheckFit)
            Shown += delegate { CheckFit(); };
            SizeChanged += delegate { CheckFit(); };
            Move += delegate { CheckFit(); };   // dragged onto a smaller screen

            // ---------- Prices ----------
            var prices = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            var friendsPriceBox = PriceBox(L.S("Friends && family", "Venner og familie"), "friends", out lblFriends, out lblFriendsInfo);
            prices.Controls.Add(friendsPriceBox, 0, 0);
            var custBox = PriceBox(L.S("Customers", "Kunder"), "others", out lblOthers, out lblOthersInfo);
            compactParts = new Control[] { inputs, grpBreak, prices, friendsPriceBox, custBox, root };   // batch-2 compact mode (SetCompact)
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
                Anchor = AnchorStyles.Left, Visible = false, Font = new Font(Font, FontStyle.Bold), AutoEllipsis = true
            };
            btnUpdate.Click += delegate { if (pendingUpdate != null) ShowUpdate(pendingUpdate); };
            var buttons = bottomButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            // four buttons lined up with the price boxes above: Save to history + Show quote… ▾ exactly under the Customers box
            // (50/50, 6 px apart; split by text if a text needs more than half), Clear + Settings (text + 14 px each, 6 apart)
            // ending at the Friends & family box's right edge. "Made by Gettic · GitHub" left of them only if it fits with
            // 12 px to spare (else hidden); the update button takes the rest of the row. AutoEllipsis = one line, never wrapped.
            var btnClear = new Button { Text = L.S("Clear", "Ryd"), Size = new Size(56, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 6, 3), AutoEllipsis = true };
            btnSettings = new Button { Text = L.S("Settings", "Indstillinger"), Size = new Size(76, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 6, 3), AutoEllipsis = true };
            btnSave = new Button { Text = L.S("Save to history", "Gem i historik"), Size = new Size(114, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 6, 3), AutoEllipsis = true };
            btnCopy = new Button { Text = QuoteButtonText(), Size = new Size(128, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 0, 3), AutoEllipsis = true };
            BuildQuoteMenu();   // split button: main part = quote window, ▾ part = quick exports without a name
            Load += delegate
            {
                copyArrowWidth = ScalePx(22);
                btnCopy.Padding = new Padding(0, 0, copyArrowWidth, 0);   // text centred in the main part; SizeByText counts it
                var row = new[] { btnClear, btnSettings, btnSave, btnCopy };
                SizeByText(row, ScalePx(14));
                // the left pair a little tighter (text + 10 still fits on one line) so the credit fits before Clear
                SizeByText(new[] { btnClear, btnSettings }, ScalePx(10));
                // the price boxes' measured edges (same 540 px content width, so the row's right edge = Customers' right edge)
                Control friendsBox = prices.GetControlFromPosition(0, 0);
                int boxGap = friendsBox.Margin.Right;   // the 12 px between the boxes (scaled)
                saveCopyPair = custBox.Width - btnSave.Margin.Right;
                SplitSaveCopy();
                btnSettings.Margin = new Padding(0, 3, boxGap, 3);   // Settings ends where Friends & family ends
                int used = 0;
                foreach (var b in row) used += b.Width + b.Margin.Horizontal;
                btnUpdate.Width = Math.Max(btnUpdate.Height, bottom.Width - used - btnClear.Margin.Right);   // same 6 px gap (ellipsis if short)
            };
            btnClear.Click += delegate { ClearJob(); };
            btnSettings.Click += delegate { OpenSettings(-1); };
            btnSave.Click += delegate { SaveClicked(); };
            btnCopy.Click += delegate
            {
                if (copyArrowPressed) { copyArrowPressed = false; ShowQuoteMenu(); }   // click on the ▾ part
                else ShowQuoteWindow();
            };
            buttons.Controls.Add(btnClear);
            buttons.Controls.Add(btnSettings);
            buttons.Controls.Add(btnSave);
            buttons.Controls.Add(btnCopy);
            // left of the buttons: "Made by Gettic · GitHub" (muted, "GitHub" = muted link); the update button takes its place when shown
            leftCell = new Panel { Size = new Size(100, 38), Dock = DockStyle.Fill, Margin = Padding.Empty };   // 38 = the row's height (button 32 + 3 + 3)
            btnUpdate.Location = new Point(0, 3);
            // two lines (PlaceMadeBy), Segoe UI 10: widest line "Made by Gettic" 103 px, ends ~34 px (DA ~26) before Clear
            lblMadeBy = new Theme.PlainLabel { Text = L.S("Made by Gettic", "Lavet af Gettic"), AutoSize = true, Tag = "muted", UseMnemonic = false,
                Font = new Font("Segoe UI", 10f), Margin = Padding.Empty, Padding = Padding.Empty };
            lnkGitHub = new Theme.LinkText { Text = "GitHub", AutoSize = true, Tag = "muted", MutedLink = true,
                Font = new Font("Segoe UI", 10f), Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 2) };   // room for the hover underline
            lnkGitHub.Click += delegate { OpenProjectPage(); };
            printerTip.SetToolTip(lnkGitHub, "github.com/gettic/PrintPrice");
            leftCell.Controls.Add(btnUpdate);
            leftCell.Controls.Add(lblMadeBy);
            leftCell.Controls.Add(lnkGitHub);
            btnUpdate.TabIndex = 0;
            lnkGitHub.TabIndex = 1;
            Load += delegate
            {
                PlaceMadeBy(btnClear);
                // the credit only where it fits: its right end at least 12 px before Clear (measured), else hidden
                bottom.PerformLayout();
                int creditRight = Math.Max(lblMadeBy.PointToScreen(new Point(lblMadeBy.Width, 0)).X, lnkGitHub.PointToScreen(new Point(lnkGitHub.Width, 0)).X);
                bool fits = creditRight + ScalePx(12) <= btnClear.PointToScreen(Point.Empty).X;
                if (!fits) { lblMadeBy.Visible = false; lnkGitHub.Visible = false; }
            };
            bottom.Controls.Add(leftCell, 0, 0);
            bottom.Controls.Add(buttons, 1, 0);
            // tab order: the link last (after Copy quote); the update button (shown instead of the line) stays first
            buttons.TabIndex = 0;
            leftCell.TabIndex = 1;
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

            // closing mid-slice: cancel the job and stop Bambu Studio (A5), so it doesn't keep running hidden
            FormClosing += delegate { if (cts != null) cts.Cancel(); BambuSlicer.StopAll(); SaveLastPrices(); };
            FormClosed += delegate
            {
                picRequest++; SetModelPicture(null);
                SetStudioIcon(null);
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
            // sliced files kept for "Open in Studio" by an earlier run (off the UI thread; files Studio still has open stay)
            // (not in test mode: it uses its own temp folder, see JobTempRoot, and never touches the user's)
            if (!offline && !AppSettings.TestMode) Shown += delegate { Task.Run(() => BambuSlicer.CleanStudioFiles(null)); };
            // job folders (Printables downloads, sliced copies) a closed run left behind, older than a day (A7); test mode
            // too: there JobTempRoot is the test folder's own temp
            if (!offline) Shown += delegate { string jobRoot = JobTempRoot(); Task.Run(() => BambuSlicer.CleanJobFolders(jobRoot)); };
            // leftover record pictures (issue #4): history.json read in the background, cleaned once it read fine
            if (!offline) Shown += delegate { StartupPictureClean(); };
            // settings.ini couldn't be read (A1): say so once in the info box (Warning stripe), as History shows its own problem
            if (!offline) Shown += delegate { ShowSettingsProblem(AppSettings.LoadProblem); };
            if (!offline) Shown += delegate { ApplySetupHint(); };   // idea 19: after "Set up later" (Program.ShowSetupHint)
            // read Bambu Studio's profile index once in the background (Settings' lists and the first slice are quicker)
            if (!offline) Shown += delegate
            {
                string slicer = BambuSlicer.FindExe(settings.SlicerPath);
                if (slicer != null) Task.Run(() => { try { BambuSlicer.WarmProfiles(slicer); } catch { } });
            };
            if (settings.AutoPowerPrice != 0) { if (!offline) Shown += delegate { FetchPowerPrice(); }; }
            else SetPowerInfo(L.S("Live price is off (Settings). Click Update to fetch it once.",
                                  "Live-pris er slået fra (Indstillinger). Klik Opdatér for at hente den én gang."), false);
            Recalculate();
            ActiveControl = txtUrl;
        }

        /// Wear rate from the chosen nozzle, filament (abrasive? type?) and build plate.
        static double AutoWearRate(AppSettings s)
        {
            return WearBreakdown(s).Total;
        }

        /// The whole wear split behind the auto rate (printer, parts, hotend, plate per hour); also the quote's wear lines.
        static Wear.Breakdown WearBreakdown(AppSettings s)
        {
            var nozzle = Wear.FindNozzle(s.Nozzle);
            string type = "";
            bool abrasive = false;
            try { BambuSlicer.FilamentInfo(BambuSlicer.FindExe(s.SlicerPath), s.PrinterModel, s.FilamentPreset, nozzle.Size, out type, out abrasive); }
            catch { /* Bambu Studio missing: treat as normal filament */ }
            return Wear.Calculate(Wear.FindModel(s.PrinterModel), nozzle, abrasive, type, Wear.FindPlate(s.BedType));
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
            jobModel = "3D Benchy";   // after the link (its TextChanged clears it); the quote picture's model name
            jobScale = ScaleXyz.None; jobTower = true; jobTowerKnown = true;   // issue #8: not scaled (the same link doesn't fire TextChanged)
            ResetCopies();   // batch & plates: one copy
            lastJobResult = new SliceResult { Seconds = 38 * 60, Grams = 11.8, Plates = 1, SizeX = 60, SizeY = 31, SizeZ = 48, SizeObjects = 1 };
            SetResultLinks(null, null, null);   // "Scale…" under the line, as after a real slice
            Recalculate();
        }

        /// Picture mode (issue #8): the sample Benchy scaled to 150 % (numbers from the CLI test: about 30,6 g and 1 h 18 m;
        /// 60 × 31 × 48 mm at 100 %), with the ✓ line naming the scale and the "Scale…" link. Call after SetSampleJob.
        internal void SetSampleScaledJob()
        {
            if (!offline) return;
            jobScale = ScaleXyz.Uniform(150);
            lastJobResult = new SliceResult { Seconds = 78 * 60, Grams = 30.57, Plates = 1, SizeX = 90, SizeY = 46.5, SizeZ = 72, SizeObjects = 1 };
            txtHours.Text = "1";
            txtMinutes.Text = "18";
            txtGrams.Text = "30,6";
            SetAuto(L.S("✓ \"3D BENCHY\" (150 %): 1 h 18 m, 30,6 g (0.16mm Optimal · Bambu PLA Matte).",
                        "✓ \"3D BENCHY\" (150 %): 1 t 18 m, 30,6 g (0.16mm Optimal · Bambu PLA Matte)."), false);
            SetResultLinks(null, null, null);
            Recalculate();
        }

        /// Picture mode: the Scale window of the shown sample job, with typed (null = as it opens).
        internal ScaleForm SampleScaleWindow(string typed)
        {
            if (!offline) return null;
            var f = NewScaleForm();
            if (typed != null) f.SampleType(typed);
            return f;
        }

        /// Picture mode (step 2): the Scale window with "Same in every direction" off and X / Y / Z typed (pcts), or
        /// mm typed into one axis (mmAxis 0-2, mm); stepFile = a STEP job: the lock forced on with its note.
        internal ScaleForm SampleScaleWindowXyz(double[] pcts, int mmAxis, double mm, bool stepFile)
        {
            if (!offline) return null;
            var r = lastJobResult;
            var f = new ScaleForm(jobScale, r != null ? r.SizeX : 0, r != null ? r.SizeY : 0, r != null ? r.SizeZ : 0,
                                  r != null ? r.SizeObjects : 0, settings.PrinterModel,
                                  stepFile ? BambuSlicer.ScaleXyzFormatText("bracket.step") : null);
            if (pcts != null) f.SampleType(new ScaleXyz(pcts[0], pcts[1], pcts[2]));
            if (mmAxis >= 0) f.SampleTypeMm(mmAxis, mm);
            return f;
        }

        /// Picture mode: the 4-colour Benchy (Printables 1648230) as our CLI slices it (real numbers from the
        /// multi-colour test: 135,85 g of which 124 g purge, 8,7 h of colour changes). Call after SetSampleJob
        /// (keeps its live price line).
        internal void SetSampleMultiJob()
        {
            if (!offline) return;
            txtUrl.Text = ExampleLink;   // the 4-colour Benchy
            SetAuto(L.S("✓ \"Four-Color Multicolor Test benchy\": 10 h 43 m, 135,85 g (0.16mm Optimal · 4\u00A0colours).",
                        "✓ \"Four-Color Multicolor Test benchy\": 10 t 43 m, 135,85 g (0.16mm Optimal · 4\u00A0farver)."), false);
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
            // shows the "Open in Studio" button (with Studio's icon if it's installed); never clicked in picture mode
            SetStudioFile(Path.Combine(BambuSlicer.StudioRoot, "sample", "Multicolor_Benchy.3mf"));
            jobModel = "Four-Color Multicolor Test Benchy";   // the quote picture's model name
            SetResultLinks(null, null, null);   // "Scale…" under the line, as after a real slice (issue #8)
            Recalculate();
        }

        /// Picture mode (issue #8 step 3): a two-colour project as our CLI sliced it with the prime tower on (plan run L:
        /// 5,84 g, 49 m 48 s; tower 1,10 + 0,60 g, 314 s), with the note line and the "Prime tower: on" link. Call after
        /// SetSampleJob (keeps its live price line and picture).
        internal void SetSampleTowerJob()
        {
            if (!offline) return;
            txtUrl.Text = "two_colour_project.3mf";
            SetAuto(L.S("✓ \"two_colour_project\": 0 h 50 m, 5,84 g (0.20mm Standard · 2\u00A0colours).",
                        "✓ \"two_colour_project\": 0 t 50 m, 5,84 g (0.20mm Standard · 2\u00A0farver)."), false);
            var r = new SliceResult { Seconds = 2988, Grams = 5.84, Plates = 1, PurgeGrams = 2.6, ColourChangeHours = -1,
                                      TowerGrams = 1.70, TowerSeconds = 314 };
            r.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#FFFFFF", Type = "PLA", Profile = "Bambu PLA Basic @BBL A1", Grams = 4.85, ModelGrams = 3.0, TowerGrams = 1.10 });
            r.Filaments.Add(new FilamentUse { Slot = 2, Colour = "#ED1C24", Type = "PLA", Profile = "Bambu PLA Basic @BBL A1", Grams = 0.99, ModelGrams = 0.2, TowerGrams = 0.60 });
            txtHours.Text = "0";
            txtMinutes.Text = "50";
            txtElecPrice.Text = "3,65";
            jobTower = true; jobTowerKnown = true;
            SetColours(r);
            SetSampleRowPrices("200");
            txtGrams.Text = "5,84";
            SetJobLoaded(true);
            jobModel = "Two-colour project";
            jobScale = ScaleXyz.None;
            lastJobResult = r;
            SetResultLinks(null, null, null);   // "Scale…" and "Prime tower: on" under the line
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
                // test mode (--settings, every --test- hook): never Energi Data Service; a fixed stand-in price instead,
                // marked "test" in the line, so test runs stay offline and give the same numbers every time
                bool test = AppSettings.TestMode;
                var q = test ? SetupTest.StandInPower(area, 1.20) : await Task.Run(() => PowerPrice.GetCurrent(area));
                double total = Math.Round(PowerPrice.TotalPrice(q.SpotDkkPerKwh, addOn, vat), 2);
                // Danish kept short ("afg.", "moms" = always 25 %) so the line fits on one line in the box
                string breakdown = L.F("spot {0} + fees {1}", "spot {0} + afg. {1}",
                                       q.SpotDkkPerKwh.ToString("0.00", Num.Dk), addOn.ToString("0.00", Num.Dk)) +
                                   (vat ? L.S(" + 25 % VAT", " + moms") : "");
                string when = q.Area + " " + (test ? "test" : q.SlotStartUtc.ToLocalTime().ToString("HH:mm", Num.Dk));
                if (txtElecPrice.Text == before)
                {
                    txtElecPrice.Text = total.ToString("0.00", Num.Dk);
                    elecSaved = false;   // the box holds today's price again
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
            reopened = null;   // any other message replaces the "Opened from history" / "Repeat print" lines
            repeatLine = null;
            autoIdle = false;          // batch & plates: ShowAutoIdle sets it again after this
            typedLineShown = false;    // UpdateTypedLine sets it again after this
            autoIsError = error;
            autoWarning = false;
            lblAuto.Text = text;
            SetAutoColor();
        }

        /// An error in the model info box; a web address in it (e.g. "open an issue on GitHub at https://…") becomes a
        /// clickable line of its own under the text.
        void SetAutoError(string text)
        {
            text = text ?? "";
            int at = text.IndexOf("https://", StringComparison.Ordinal);
            if (at < 0) { SetAuto(text, true); return; }
            int end = at;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            string url = text.Substring(at, end - at).TrimEnd('.', ',', ')');
            // only our own issues page is clickable: the text can come from a downloaded file or Bambu Studio
            if (url != BambuSlicer.IssuesUrl && !url.StartsWith("https://github.com/gettic/PrintPrice/", StringComparison.Ordinal))
            { SetAuto(text, true); return; }
            SetAuto((text.Substring(0, at).TrimEnd() + " " + text.Substring(at + url.Length).Trim()).Trim(), true);
            lblAuto.SetLink(url, () => { try { Process.Start(url); } catch { } });
        }

        void SetAutoColor()
        {
            // results keep the red info stripe too (the ✓ in the text already shows it worked); a repeat print that
            // couldn't slice again, a printer change after slicing and a file sliced for another printer get the warning stripe
            if (lblAuto != null)
                lblAuto.Kind = autoIsError ? InfoKind.Error
                             : autoWarning || (repeatLine != null && repeatWhy != null) ? InfoKind.Warning : InfoKind.Info;
        }

        void ShowAutoIdle()
        {
            if (BambuSlicer.FindExe(settings.SlicerPath) == null)
                SetAuto(L.S("Bambu Studio was not found. Install it to detect time and weight automatically.",
                            "Bambu Studio blev ikke fundet. Installér det for at finde tid og vægt automatisk."), false);
            else
                // 2 lines with the default profiles (idea 26: 3 lines made the box shrink when a job started)
                SetAuto(L.F("Paste a Printables link or drop an STL/3MF file here. Slices with {0} mm nozzle · {1} · {2}.",
                            "Indsæt et Printables-link eller træk en STL/3MF-fil hertil. Slicer med {0} mm dyse · {1} · {2}.",
                            L.Danish ? Wear.FindNozzle(settings.Nozzle).Size.Replace('.', ',') : Wear.FindNozzle(settings.Nozzle).Size,
                            BambuSlicer.ShortName(settings.ProcessPreset), BambuSlicer.ShortName(settings.FilamentPreset)), false);
            autoIdle = true;   // batch & plates: typing a job by hand may replace it with the typed line (UpdateTypedLine)
        }

        /// The info box shows the start text (ShowAutoIdle) and nothing since.
        bool autoIdle;

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
                var models = paths.Where(p => BambuSlicer.IsModelFile(p) || BambuSlicer.IsGcodeFile(p)).ToList();
                if (models.Count == 0) { SetAuto(L.S("That isn't a model file. Use STL, 3MF, OBJ, STEP or G-code.", "Det er ikke en modelfil. Brug STL, 3MF, OBJ, STEP eller G-code."), true); return; }
                if (!GcodeAlone(models)) return;
                if (models.Count == 1) ShowLinkFile(models[0]);
                else txtUrl.Text = L.F("{0} files: ", "{0} filer: ", models.Count) + string.Join(", ", models.Select(Path.GetFileName));
                Analyze(null, models);
            };
            foreach (Control child in c.Controls) EnableDrop(child);
        }

        /// A sliced G-code is read, not sliced, so it can't be combined with other files; false = shown why.
        bool GcodeAlone(List<string> files)
        {
            if (files.Count < 2 || !files.Any(BambuSlicer.IsGcodeFile)) return true;
            SetAuto(L.S("Use one G-code file at a time.", "Brug én G-code-fil ad gangen."), true);
            return false;
        }

        // a local file in the link box is shown by its name only (the centred box cut long paths before the name);
        // linkFile keeps the full path, also in the box's dark tooltip. Links and typed text are shown as they are.
        string linkFile;

        void ShowLinkFile(string path)
        {
            linkFile = path;   // before Text: TextChanged keeps it while the box shows its name
            txtUrl.Text = Path.GetFileName(path);
            if (printerTip != null) printerTip.SetToolTip(txtUrl, path);
        }

        /// What the link box stands for: the full path of a file shown by its name, else the box's text (a link, a typed path).
        string LinkSource()
        {
            return LinkShowsFile() ? linkFile : txtUrl.Text;
        }

        bool LinkShowsFile()
        {
            return linkFile != null && string.Equals(txtUrl.Text.Trim().Trim('"'), Path.GetFileName(linkFile), StringComparison.OrdinalIgnoreCase);
        }

        /// A saved source: a local path (drive letter or \\server) by its file name, anything else (links) as it is.
        void ShowLinkSource(string src)
        {
            src = src ?? "";
            string t = src.Trim().Trim('"');
            bool path = t.Length > 2 && ((char.IsLetter(t[0]) && t[1] == ':' && (t[2] == '\\' || t[2] == '/')) || t.StartsWith(@"\\"));
            if (path && t.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.GetFileName(t).Length > 0) ShowLinkFile(t);
            else txtUrl.Text = src;
        }

        /// A failed read or slice: the previous job's time, grams and colour rows go, so its prices don't stay under the
        /// error (the link box and the error message stay).
        void ClearJobNumbers()
        {
            ResetCopies();
            SetStudioFile(null);
            SetColours(null);
            txtGrams.Text = "";
            txtHours.Text = "0";
            txtMinutes.Text = "0";
            SetJobLoaded(false);
        }

        void BrowseForModel()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = L.S("Choose model or G-code file(s)", "Vælg model- eller G-code-fil(er)"), Multiselect = true,
                Filter = L.S("3D models and G-code", "3D-modeller og G-code") + " (*.stl;*.3mf;*.obj;*.step;*.gcode…)|" +
                         "*.stl;*.3mf;*.obj;*.step;*.stp;*.amf;*.gcode;*.gco;*.gc;*.bgcode|" +
                         L.S("All files", "Alle filer") + " (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var files = dlg.FileNames.ToList();
                if (!GcodeAlone(files)) return;
                if (files.Count == 1) ShowLinkFile(files[0]);
                else txtUrl.Text = L.F("{0} files: ", "{0} filer: ", files.Count) + string.Join(", ", files.Select(Path.GetFileName));
                Analyze(null, files);
            }
        }

        void AnalyzeClicked()
        {
            if (cts != null) { cts.Cancel(); return; }
            if (studioFile != null) { OpenInStudio(); return; }
            string src = LinkSource().Trim().Trim('"');
            if (src.Length == 0) { txtUrl.Text = ""; SetUrlHint(true); txtUrl.Focus(); return; }
            if (File.Exists(src)) { Analyze(null, new List<string> { src }); return; }

            string id = Printables.ParseId(src);
            if (id != null) { Analyze(id, null); return; }

            string lower = src.ToLowerInvariant();
            string makerWorld = MakerWorldPage(src);
            if (makerWorld != null) ShowMakerWorld(makerWorld);
            else if (lower.Contains("makerworld.com") || lower.Contains("thingiverse.com") || lower.Contains("cults3d.com") || lower.Contains("myminifactory.com"))
                SetAuto(L.S("This site blocks automatic downloads. Download the model (STL/3MF) from the page and drop the file on this window.",
                            "Siden blokerer automatisk download. Hent modellen (STL/3MF) fra siden, og træk filen ind i vinduet."), true);
            else
                SetAuto(L.S("Paste a Printables link (printables.com/model/…) or load a model file.",
                            "Indsæt et Printables-link (printables.com/model/…) eller åbn en modelfil."), true);
        }

        /// A makerworld.com page as an https:// address to open (a missing "https://" is added), else null (other sites,
        /// other schemes, odd text: never handed to the shell).
        internal static string MakerWorldPage(string text)
        {
            string t = (text ?? "").Trim().Trim('"');
            if (t.Length == 0 || t.IndexOfAny(new[] { ' ', '\t', '"', '<', '>' }) >= 0) return null;
            if (!t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                t = "https://" + t;
            Uri u;
            if (!Uri.TryCreate(t, UriKind.Absolute, out u)) return null;
            string host = u.Host.ToLowerInvariant();
            if (host != "makerworld.com" && !host.EndsWith(".makerworld.com")) return null;
            return "https://" + u.Host + u.PathAndQuery;
        }

        static string AnalyzeText { get { return L.S("Get time && weight", "Hent tid og vægt"); } }

        // ---------- "Open in Studio" (the first top button after a successful slice) ----------
        /// The file "Open in Studio" opens (null = back to "Get time & weight"). Switched back by: a link edit (TextChanged),
        /// a new job (Analyze start: Load file / drop / Get time), errors and Cancel, Clear, Reopen and slicing settings changes.
        void SetStudioFile(string file)
        {
            studioFile = string.IsNullOrEmpty(file) ? null : file;
            if (studioFile != null) SetStudioIcon(BambuSlicer.FindExe(settings.SlicerPath));
            UpdateAnalyzeButton();
        }

        /// Text, icon room and tooltip of the first top button: Cancel (job running), Open in Studio, or Get time & weight.
        void UpdateAnalyzeButton()
        {
            if (btnAnalyze == null) return;
            bool open = cts == null && studioFile != null;
            // with the icon, PaintStudioIcon draws icon + text itself (the button's own text placement can't be lined up
            // exactly): the button's Text is empty then and the name goes to AccessibleName; no icon = normal button text
            bool own = open && studioIcon != null;
            btnAnalyze.Text = own ? "" : cts != null ? L.S("Cancel", "Annullér") : open ? StudioText : AnalyzeText;
            btnAnalyze.AccessibleName = own ? StudioText : null;
            if (printerTip != null)
                printerTip.SetToolTip(btnAnalyze, open
                    ? L.S("Opens the sliced file in Bambu Studio.\nClick \"Slice plate\" there before printing.\nChange the link to slice again.",
                          "Åbner den slicede fil i Bambu Studio.\nKlik \"Slice plate\" der, før du printer.\nRet linket for at slice igen.")
                    : null);
            btnAnalyze.Invalidate();
        }

        /// Reads Bambu Studio's icon from its exe once per exe (16 px at 96 dpi, scaled) + a grey copy for the disabled button.
        void SetStudioIcon(string exe)
        {
            if (exe == studioIconExe) return;
            studioIconExe = exe;
            if (studioIcon != null) { studioIcon.Dispose(); studioIcon = null; }
            if (studioIconGrey != null) { studioIconGrey.Dispose(); studioIconGrey = null; }
            if (exe == null || !File.Exists(exe)) return;
            try
            {
                int size = ScalePx(16);
                using (var exeIcon = Icon.ExtractAssociatedIcon(exe))
                using (var src = exeIcon.ToBitmap())
                {
                    studioIcon = new Bitmap(size, size);
                    using (var g = Graphics.FromImage(studioIcon))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(src, new Rectangle(0, 0, size, size));
                    }
                    // grey copy: luminance only, dimmed to about Theme.Muted on the button back
                    studioIconGrey = new Bitmap(size, size);
                    var m = new System.Drawing.Imaging.ColorMatrix(new[]
                    {
                        new[] { 0.3f, 0.3f, 0.3f, 0f, 0f }, new[] { 0.59f, 0.59f, 0.59f, 0f, 0f }, new[] { 0.11f, 0.11f, 0.11f, 0f, 0f },
                        new[] { 0f, 0f, 0f, 0.5f, 0f }, new[] { 0f, 0f, 0f, 0f, 1f }
                    });
                    using (var attr = new System.Drawing.Imaging.ImageAttributes())
                    using (var g = Graphics.FromImage(studioIconGrey))
                    {
                        attr.SetColorMatrix(m);
                        g.DrawImage(studioIcon, new Rectangle(0, 0, size, size), 0, 0, size, size, GraphicsUnit.Pixel, attr);
                    }
                }
            }
            catch
            {
                if (studioIcon != null) { studioIcon.Dispose(); studioIcon = null; }
                if (studioIconGrey != null) { studioIconGrey.Dispose(); studioIconGrey = null; }
            }
        }

        static string StudioText { get { return L.S("Open in Studio", "Åbn i Studio"); } }

        const TextFormatFlags StudioTextFlags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                                                TextFormatFlags.VerticalCenter;
        string studioInkKey;          // text + font the ink columns below were measured for
        int studioInkLeft, studioInkWidth;

        /// Measures where the text's ink really is (first lit column, width) by drawing it once into a small bitmap,
        /// so icon, 4 px gap and text can be centred to the pixel (TextRenderer's widths include side bearings).
        void MeasureStudioInk(string text, Font font)
        {
            string key = text + "|" + font.Name + "|" + font.Size + "|" + DeviceDpi;
            if (key == studioInkKey) return;
            Size sz = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), StudioTextFlags);
            studioInkLeft = 0; studioInkWidth = sz.Width;
            using (var bmp = new Bitmap(Math.Max(1, sz.Width + 4), Math.Max(1, sz.Height)))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Theme.ButtonBack);
                    TextRenderer.DrawText(g, text, font, new Rectangle(0, 0, bmp.Width, bmp.Height), Theme.Text, Theme.ButtonBack, StudioTextFlags);
                }
                int bg = Theme.ButtonBack.ToArgb(), first = -1, last = -1;
                for (int x = 0; x < bmp.Width; x++)
                    for (int y = 0; y < bmp.Height; y++)
                        if (bmp.GetPixel(x, y).ToArgb() != bg) { if (first < 0) first = x; last = x; break; }
                if (first >= 0) { studioInkLeft = first; studioInkWidth = last - first + 1; }
            }
            studioInkKey = key;
        }

        /// "Open in Studio" with the icon: draws icon + 4 px + text as one group, centred by the ink (the button's Text is empty).
        /// Text colour like the other buttons: Text, Muted when disabled; the button itself still paints back, hover and focus.
        void PaintStudioIcon(Graphics g)
        {
            if (cts != null || studioFile == null || studioIcon == null) return;
            string text = StudioText;
            Font font = btnAnalyze.Font;
            MeasureStudioInk(text, font);
            int gap = ScalePx(4);
            int groupW = studioIcon.Width + gap + studioInkWidth;
            int x = (btnAnalyze.Width - groupW + 1) / 2;   // odd leftover: the extra px goes to the left side
            int y = (btnAnalyze.Height - studioIcon.Height) / 2;
            g.DrawImageUnscaled(btnAnalyze.Enabled ? studioIcon : studioIconGrey, x, y);
            int textX = x + studioIcon.Width + gap - studioInkLeft;
            TextRenderer.DrawText(g, text, font, new Rectangle(textX, 0, btnAnalyze.Width - textX, btnAnalyze.Height),
                                  btnAnalyze.Enabled ? Theme.Text : Theme.Muted, StudioTextFlags);
        }

        /// The click: starts Bambu Studio with the sliced file (2 s guard against double launches). Errors go in the info box;
        /// a file that is gone switches the button back to slicing.
        void OpenInStudio()
        {
            if (studioClicked && unchecked(Environment.TickCount - studioClickTick) < 2000) return;
            studioClicked = true;
            studioClickTick = Environment.TickCount;
            string file = studioFile;
            try { BambuSlicer.OpenInStudio(BambuSlicer.FindExe(settings.SlicerPath), file); }
            catch (SliceException ex)
            {
                SetAuto(ex.Message, true);
                if (!File.Exists(file)) SetStudioFile(null);
            }
        }

        void SetBusy(bool busy)
        {
            UpdateAnalyzeButton();   // Cancel while busy (cts is set first), else Get time / Open in Studio
            btnLoadFile.Enabled = !busy;
            btnHistory.Enabled = !busy;   // "Open again" would clash with a running job
            printerCard.Enabled = !busy;  // the job uses the active printer's profiles: dimmed, no hand, no click
            txtUrl.ReadOnly = busy;
            UseWaitCursor = false;
        }

        void Analyze(string printablesId, List<string> localFiles)
        {
            var job = AnalyzeAsync(printablesId, localFiles, null);   // errors are shown by AnalyzeAsync itself
        }

        /// Gets time and weight (Printables model or local files); true when it worked. beforeFill (may be null) can
        /// adjust the slice before the colour rows are filled (Repeat print keeps the saved materials).
        async Task<bool> AnalyzeAsync(string printablesId, List<string> localFiles, Action<SliceResult> beforeFill)
        {
            return await AnalyzeAsync(printablesId, localFiles, beforeFill, null, false);
        }

        /// savedParts (Repeat print of a job with a colour per file, idea 21): the files and their filament / colour, so
        /// the file picker is skipped; Printables files are found by name, local ones are the saved paths (= localFiles).
        async Task<bool> AnalyzeAsync(string printablesId, List<string> localFiles, Action<SliceResult> beforeFill,
                                      List<HistoryPart> savedParts, bool savedTogether)
        {
            return await AnalyzeAsync(printablesId, localFiles, beforeFill, savedParts, savedTogether, ScaleXyz.None, null);
        }

        // ---------- issue #8: same-size scale of the whole job ----------
        /// The shown job's scale in percent (100 = not scaled). Set by a finished job (the scale it was sliced at), by Open
        /// again / Repeat print (the record's); back to 100 by a new link / file, Clear and a new job without a scale.
        ScaleXyz jobScale = ScaleXyz.None;   // step 2: per axis (X = Y = Z = the step-1 uniform scale)
        // ---------- issue #8 step 3: prime tower ----------
        /// The shown job's tower switch (what it was sliced with; Open again = the record's, History.PrimeTowerFor).
        bool jobTower = true;
        /// false = the shown job is an old record opened again whose tower is unknown (PrimeTower -1): saving keeps -1
        /// instead of Settings' value, until it is sliced again.
        bool jobTowerKnown = true;
        /// The next slice's tower switch (set by the job's tower link and Repeat print); null = Settings.
        bool? towerForNextSlice;
        /// The tower switch of the slice that is starting (towerForNextSlice once, else Settings; set in AnalyzeAsync).
        bool sliceTower = true;
        bool SettingsTower { get { return settings.PrimeTower != 0; } }
        /// "150 %" / "150 × 100 × 50 %", "" when not scaled (History's text, so names match the saved record's).
        static string ScaleLabel(ScaleXyz s) { return s == null ? "" : History.ScaleText(s.X, s.Y, s.Z); }
        /// A record's scale per axis (old records: their uniform Scale on every axis).
        static ScaleXyz RecordScale(QuoteRecord r) { var a = History.ScaleXyzOf(r); return new ScaleXyz(a[0], a[1], a[2]); }
        static bool SameScale(ScaleXyz a, ScaleXyz b)
        {
            a = a ?? ScaleXyz.None; b = b ?? ScaleXyz.None;
            return Math.Abs(a.X - b.X) < 0.05 && Math.Abs(a.Y - b.Y) < 0.05 && Math.Abs(a.Z - b.Z) < 0.05;
        }
        /// What the last finished job was sliced from, so Scale… can slice the same files again without the file picker.
        sealed class JobInputs
        {
            public string PrintablesId;
            public List<string> LocalFiles;
            public List<RemoteFile> Chosen;
            public ColourPlan Plan;
            public string ModelName;
            public bool Scalable;   // no G-code among them (BambuSlicer.CanScale)
        }
        JobInputs lastJob;

        // ---------- batch & plates (issue #9 / #10) ----------
        /// Copies of the shown job (1 = a normal job). Sliced / read jobs: the boxes hold the TOTALS of all copies; a job
        /// typed by hand (jobCopiesTyped): the boxes hold ONE copy and Recalculate prices Pricing.ForCopies.
        int jobCopies = 1;
        /// Build plates of the shown job (0 = unknown = 1; sliced: SliceResult.Plates; typed: the Copies window's).
        int jobPlates;
        /// Most copies on one plate (0 = unknown).
        int jobPerPlate;
        /// The copies are a sliced file / G-code printed N times (SliceResult.CopiesMultiplied; no re-arranging).
        bool jobCopiesMultiplied;
        /// A job typed by hand with copies: the boxes are one copy's (saved with CopiesEstimated).
        bool jobCopiesTyped;
        /// Copies per file of the shown job (the file picker's / the Copies window's; null = 1 each).
        List<int> jobFileCopies;
        /// The next slice's copies per file (set by the Copies window and Repeat print, used once by AnalyzeAsync).
        List<int> copiesForNextSlice;
        /// batch-2: the shown job's "share the colours' last plate" choice (as sliced; Open again = the record's). Saved with
        /// the job's parts (History.SetShareLastPlate).
        bool jobShare;
        /// The next slice's share choice (the Copies window, Repeat print; used once by AnalyzeAsync); null = the picker's
        /// tick for a new job, the shown job's for Scale… / tower / Copies, else Settings'.
        bool? shareForNextSlice;
        bool SettingsShare { get { return settings.ShareLastPlate == 1; } }

        /// scalePct = the size to slice at (100 = as the file is); rescale = the shown job again at another size (Scale…):
        /// the same files and colour plan (no picker), the picture stays, and a failed slice keeps the shown numbers.
        async Task<bool> AnalyzeAsync(string printablesId, List<string> localFiles, Action<SliceResult> beforeFill,
                                      List<HistoryPart> savedParts, bool savedTogether, ScaleXyz scalePct, JobInputs rescale)
        {
            if (offline || cts != null) return false;
            // issue #8 step 3: the tower switch for this slice (the job's tower link / Repeat print once, else Settings)
            sliceTower = towerForNextSlice ?? SettingsTower;
            towerForNextSlice = null;
            // batch & plates: the copies per file for this slice (the Copies window / Repeat print once; Scale… and the tower
            // link keep the shown job's; a new job: the file picker's, else 1 each)
            var nextCopies = copiesForNextSlice;
            copiesForNextSlice = null;
            var nextShare = shareForNextSlice;   // batch-2
            shareForNextSlice = null;
            // issue #8: G-code can't be scaled; say so first (not "Bambu Studio was not found" when it's missing)
            if (BambuSlicer.IsScaled(scalePct) && localFiles != null && localFiles.Any(BambuSlicer.IsGcodeFile))
            {
                SetAuto(BambuSlicer.ScaleGcodeText(), true);
                return false;
            }
            bool ok = false;
            string studioBefore = studioFile;
            SetStudioFile(null);   // a new job: back to "Get time & weight" until it has sliced
            string exe = BambuSlicer.FindExe(settings.SlicerPath);
            bool presliced = localFiles != null && localFiles.Count == 1 && BambuSlicer.ReadAsSliced(localFiles[0], scalePct);
            if (exe == null && !presliced)
            {
                SetAuto(L.S("Bambu Studio was not found. Install it from bambulab.com, or set its location in Settings.",
                            "Bambu Studio blev ikke fundet. Installér det fra bambulab.com, eller angiv stien i Indstillinger."), true);
                return false;
            }

            string process = settings.ProcessPreset, filament = settings.FilamentPreset, bed = settings.BedType;
            string nozzle = Wear.FindNozzle(settings.Nozzle).Size;
            string printer = settings.PrinterModel;   // the active printer's model: its Bambu Studio profiles
            cts = new CancellationTokenSource();
            var token = cts.Token;
            SetBusy(true);
            // idea 26: the info box keeps its height while the job runs ("Slicing…" is 1 line, the start hint 2-3), so the
            // picture and the window don't change size until the result is in (released in finally)
            lblAuto.MinimumSize = new Size(lblAuto.MinimumSize.Width, Math.Max(autoMinHeight, lblAuto.Height));
            IProgress<string> progress = new Progress<string>(s => SetAuto(s, false));
            string modelName = null;
            // model picture: a picked .3mf shows its own picture at once; Printables photo / slice render follow below
            SetJobLoaded(false);
            savedId = null;   // a new job: what the last one saved counts as paid back now
            if (rescale == null) ResetModelPicture();   // Scale…: the picture stays until the new render
            if (rescale == null && localFiles != null && localFiles.Count == 1 && localFiles[0].EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            {
                string project = localFiles[0];
                var projectPicture = LoadModelPicture(() => BambuSlicer.FromProject(project), true);   // not awaited: shows when ready
            }
            try
            {
                List<RemoteFile> chosen = null;
                ColourPlan plan = null;   // a colour per file (idea 21); null = one file, or files that keep their own colours
                jobParts = null;
                if (printablesId != null)
                {
                    progress.Report(L.S("Looking up the model on Printables…", "Finder modellen på Printables…"));
                    var model = await Task.Run(() => Printables.GetModel(printablesId, token));
                    token.ThrowIfCancellationRequested();
                    modelName = model.Name;
                    if (model.ImageUrl != null)
                    {
                        string imageUrl = model.ImageUrl;
                        var photo = LoadModelPicture(() => Printables.DownloadImage(imageUrl), true);   // not awaited: the job goes on
                    }
                    var sliceable = model.Files.Where(f => BambuSlicer.IsModelFile(f.Name)).ToList();
                    if (sliceable.Count == 0) throw new SliceException(L.F("\"{0}\" has no STL, 3MF or STEP files to slice.",
                                                                           "\"{0}\" har ingen STL-, 3MF- eller STEP-filer at slice.", model.Name));
                    if (rescale != null && rescale.Chosen != null)
                    {
                        // Scale…: the files sliced last time (no picker), their colour plan
                        chosen = rescale.Chosen;
                        plan = rescale.Plan;
                    }
                    else if (savedParts != null)
                    {
                        // Repeat print: the same files by name, with their saved filament and colour (no picker)
                        chosen = new List<RemoteFile>();
                        foreach (var p in savedParts)
                        {
                            var f = sliceable.FirstOrDefault(x => string.Equals(x.Name, p.File, StringComparison.OrdinalIgnoreCase));
                            if (f == null) throw new SliceException(L.F("\"{0}\" isn't in the model on Printables any more.",
                                                                        "\"{0}\" findes ikke længere i modellen på Printables.", p.File));
                            chosen.Add(f);
                        }
                        plan = ColourPlan.Build(savedParts, savedTogether);
                    }
                    else if (sliceable.Count == 1) chosen = sliceable;
                    else if (fromExample && ExampleFile(sliceable) != null) chosen = new List<RemoteFile> { ExampleFile(sliceable) };
                    else
                    {
                        var names = sliceable.Select(f => f.Name).ToList();
                        var picked = PickFiles(model.Name, names, FilePickerForm.DefaultTicks(names), true);
                        if (picked == null) throw new OperationCanceledException();
                        chosen = picked.SelectedIndexes.Select(i => sliceable[i]).ToList();
                        plan = picked.Plan;
                    }
                }
                else if (rescale != null) plan = rescale.Plan;   // Scale…: the same files (localFiles) and colour plan
                else if (localFiles.Count >= 2 || savedParts != null)
                {
                    if (savedParts != null) plan = ColourPlan.Build(savedParts, savedTogether);   // localFiles = the saved paths
                    else if (localFiles.Any(f => !ColourPlan.OwnColours(f)))
                    {
                        // several local files, at least one plain model: a filament and colour per file (idea 21)
                        var picked = PickFiles(L.F("{0} files", "{0} filer", localFiles.Count), localFiles.Select(Path.GetFileName).ToList(), null, false);
                        if (picked == null) throw new OperationCanceledException();
                        var paths = picked.SelectedIndexes.Select(i => localFiles[i]).ToList();
                        plan = picked.Plan;
                        for (int k = 0; k < paths.Count; k++) plan.Parts[k].File = paths[k];   // the full path, for Repeat print
                        if (paths.Count != localFiles.Count)
                        {
                            if (paths.Count == 1) ShowLinkFile(paths[0]);
                            else txtUrl.Text = L.F("{0} files: ", "{0} filer: ", paths.Count) + string.Join(", ", paths.Select(Path.GetFileName));
                        }
                        localFiles = paths;
                    }
                }

                var files = localFiles;
                var colourPlan = plan;
                var scale = scalePct;
                bool tower = sliceTower;   // issue #8 step 3: this slice's tower switch
                int fileCount = chosen != null ? chosen.Count : localFiles.Count;
                var fileCopies = NormalizeCopies(nextCopies ?? (rescale != null ? jobFileCopies : plan != null ? plan.Copies : null), fileCount);
                // batch-2: share the colours' last plate (the Copies window / Repeat once, Scale… keeps the job's, a new job: the
                // picker's tick, else Settings')
                bool share = nextShare ?? (rescale != null ? jobShare : plan != null && savedParts == null && rescale == null ? plan.Share : SettingsShare);
                var result = await Task.Run(() => RunJob(exe, printablesId, chosen, files, printer, nozzle, process, filament, bed, colourPlan, progress, token, scale, tower, fileCopies, share));
                jobShare = share;
                // batch & plates: the shown job's copies and plates (the boxes below hold the totals of all copies)
                jobFileCopies = fileCopies;
                jobCopies = result.Copies > 0 ? result.Copies : fileCopies != null ? fileCopies.Sum() : 1;
                jobPlates = Math.Max(result.Plates, result.PlateList != null ? result.PlateList.Count : 0);
                jobPerPlate = result.PerPlate;
                jobCopiesMultiplied = result.CopiesMultiplied;
                jobCopiesTyped = false;
                jobTower = tower;
                jobTowerKnown = true;   // sliced: the switch it used is known
                if (plan != null)
                {
                    jobParts = plan.Parts;   // saved with the quote (History.SetParts), so Repeat print slices it the same way
                    jobTogether = plan.Together;
                    // batch & plates: each file's copies as this slice used them (the Copies window may have changed them)
                    if (jobParts != null)
                        jobParts = jobParts.Select((p, i) => new HistoryPart { File = p.File, Filament = p.Filament, Colour = p.Colour, Slot = p.Slot,
                            Copies = fileCopies != null && fileCopies.Count == jobParts.Count ? fileCopies[i] : 1 }).ToList();
                }
                jobScale = BambuSlicer.IsScaled(scalePct) ? scalePct : ScaleXyz.None;
                lastJob = new JobInputs
                {
                    PrintablesId = printablesId, LocalFiles = localFiles, Chosen = chosen, Plan = plan, ModelName = modelName,
                    Scalable = localFiles == null || localFiles.All(BambuSlicer.CanScale)   // Printables: model files only
                };
                if (result.Picture != null)
                {
                    byte[] render = result.Picture;
                    // the plate render always wins: replaces the Printables photo / 3MF thumbnail; picRequest++ drops a photo
                    // still downloading (it would land after the render). The shown picture stays if the render can't be read.
                    picRequest++;
                    // awaited (idea 29): Save to history comes back on after this job and saves the picture with the quote
                    await LoadModelPicture(() => render, true);
                }

                int parts = chosen != null ? chosen.Count : localFiles.Count;
                ShowJobResult(result, modelName, localFiles, parts, process, filament, beforeFill);
                ok = true;
            }
            catch (OperationCanceledException) { SetAuto(L.S("Cancelled.", "Annulleret."), false); }
            // a failed job clears the last one's numbers (Repeat print keeps them: it shows the saved quote instead; Scale…
            // keeps the shown job at its old size)
            catch (SliceException ex)
            {
                lastSliceBedError = ex.IsBedError;   // Repeat print: refused, not the saved numbers (release-test-5)
                if (beforeFill == null && rescale == null) ClearJobNumbers();
                // too small (plans\too-small): a slice that failed on a job scaled down says so, with the size it would have
                // been (from the shown job's size at 100 %, known after Scale…; a repeat / first slice: no mm)
                var sc = scalePct ?? ScaleXyz.None;
                SetAutoError(BambuSlicer.IsTooSmallFailure(ex, sc) ? TooSmallMessage(sc, rescale != null) : ex.Message);
            }
            catch (Exception ex)
            {
                if (beforeFill == null && rescale == null) ClearJobNumbers();
                SetAuto(L.S("Something went wrong: ", "Noget gik galt: ") + ex.Message, true);
            }
            finally
            {
                if (!ok && rescale != null)
                {
                    // Scale… failed or was cancelled: the job stays as it was (numbers, size, Open in Studio, Save)
                    SetJobLoaded(true);
                    SetStudioFile(studioBefore);
                    lastJob = rescale;
                }
                cts.Dispose();
                cts = null;
                lblAuto.MinimumSize = new Size(lblAuto.MinimumSize.Width, autoMinHeight);   // one size change, with the result
                SetBusy(false);
            }
            return ok;
        }

        /// Fills time, grams and the material rows from a finished job and writes the "✓ …" line, with the muted notes under
        /// it: another slicer's file (idea 14), Studio opening only the first file, a file sliced for another printer.
        void ShowJobResult(SliceResult result, string modelName, List<string> localFiles, int parts, string process, string filament,
                           Action<SliceResult> beforeFill)
        {
            lastJobResult = result;   // read by the test hooks (TestSliceFlow / TestRepeatFlow)
            int totalMinutes = (int)Math.Round(result.Seconds / 60.0);
            if (totalMinutes == 0 && result.Seconds > 0) totalMinutes = 1;
            // a G-code with only one of the two (idea 14): the missing box stays empty (not 0), gets the focus and a warning note
            bool noTime = result.MissingTime && !result.MissingGrams, noGrams = result.MissingGrams && !result.MissingTime;
            txtHours.Text = noTime ? "" : (totalMinutes / 60).ToString();
            txtMinutes.Text = noTime ? "" : (totalMinutes % 60).ToString();
            if (beforeFill != null) beforeFill(result);
            SetColours(result);   // 2+ colours: one row per colour in the Cost breakdown, else today's layout
            bool multi = filaments != null;
            // several colours: grams with 2 decimals, like the colour rows
            string gramsText = multi ? Math.Round(result.Grams, 2).ToString("0.##", Num.Dk) : Math.Round(result.Grams, 1).ToString("0.#", Num.Dk);
            txtGrams.Text = noGrams ? "" : gramsText;

            string what = modelName != null ? "\"" + modelName + "\"" :
                          localFiles.Count == 1 ? Path.GetFileName(localFiles[0]) : L.F("{0} files", "{0} filer", localFiles.Count);
            if (BambuSlicer.IsScaled(jobScale)) what += " (" + ScaleLabel(jobScale) + ")";   // issue #8: "3D Benchy" (150 %)
            // batch-2 qol #2: several files with copies: "50 pcs (2 files)" (jobCopies = all pieces; "50 × 2 files" read as 100)
            bool filesInWhat = modelName == null && localFiles.Count > 1 && jobCopies > 1 && !BambuSlicer.IsScaled(jobScale);
            if (filesInWhat) what = History.PiecesText(jobCopies) + " (" + L.F("{0} files", "{0} filer", localFiles.Count) + ")";
            else what = History.CopiesName(what, jobCopies);   // batch & plates: 4 × "3D Benchy"
            // grams worked out from the filament length (e.g. Cura): shown as an estimate; a missing value is left out
            var said = new List<string>();
            if (!noTime) said.Add((totalMinutes / 60) + L.S(" h ", " t ") + (totalMinutes % 60) + " m");
            if (!noGrams) said.Add((result.GramsEstimated ? "≈ " : "") + gramsText + " g");
            string summary = string.Join(", ", said);
            // a no-break space keeps a number on the same line as its word ("4 colours" never splits over two lines)
            string colourCount = multi ? L.F("{0} colours", "{0} farver", filaments.Count) : null;
            // another slicer's file: its muted note (or the printer warning) already says where the numbers come from,
            // so the first line keeps short (the window must stay <= ~760 px with 4 colours and every note)
            string slicerNote = BambuSlicer.OtherSlicerNote(result);   // null for Bambu Studio's own files
            string printerWarning = BambuSlicer.OtherPrinterWarning(result, settings.PrinterModel);   // non-Bambu printer
            string how = slicerNote != null || printerWarning != null ? colourCount
                : result.FromSlicedFile
                ? L.S("from the sliced file", "fra den slicede fil") + (multi ? " · " + colourCount : "")
                : BambuSlicer.ShortName(process) + " · " + (multi ? colourCount : BambuSlicer.ShortName(filament));
            // ", N plates" only without the plates link (2+ plates: the link under the line says it)
            bool platesLink = result.PlateList != null && result.PlateList.Count >= 2;
            string extra = (parts > 1 ? (result.Together ? L.F(", {0} files on one plate", ", {0} filer på én plade", parts)
                                                         : filesInWhat ? "" : L.F(", {0} files", ", {0} filer", parts)) : "") +
                           (result.Plates > parts && !platesLink ? L.F(", {0} plates", ", {0} plader", result.Plates) : "");
            // batch-2: a shared last plate: ", 4 plates, 1 with mixed colours" (also with the plates link: it says which one);
            // numbers joined to their words by no-break spaces
            int mixedPlates = SharedMixedPlates(result);
            if (mixedPlates > 0)
                extra += ", " + SharedPlatesText(mixedPlates).Replace(' ', ' ');   // qol #2: ", 1 shared plate" (the plates link says how many)
            if (string.IsNullOrEmpty(how) && extra.Length > 0) extra = extra.Substring(2);
            string tail = (how ?? "") + extra;
            // name for the history (after the link box was filled, whose TextChanged clears it)
            jobModel = modelName ?? (localFiles.Count == 1 ? Path.GetFileName(localFiles[0]) : string.Join(", ", localFiles.Select(Path.GetFileName)));
            SetJobLoaded(true);
            SetAuto("✓ " + what + ": " + summary + (tail.Length > 0 ? " (" + tail + ")" : "") + ".", false);
            SetStudioFile(result.StudioFile);   // null (nothing kept) = stays "Get time & weight"

            // muted lines under the result (ClearJob takes them away again), one per note
            var notes = new List<string>();
            string missing = BambuSlicer.MissingValueText(result);   // "Couldn't find the weight in the file: type it in."
            if (missing != null)
            {
                notes.Add(missing);
                autoWarning = true;
            }
            if (printerWarning != null)
            {
                // a printer that isn't a Bambu Lab one (warning stripe): the warning already says the time is that
                // printer's, so the slicer note is left out (keeps the box at 3 lines; "≈" still marks estimated grams)
                notes.Add(printerWarning);
                autoWarning = true;
            }
            else if (slicerNote != null) notes.Add(slicerNote);
            bool fellBack = !string.IsNullOrEmpty(result.TogetherProblem);   // "together on one plate" didn't work
            bool studioFirst = studioFile != null && parts > 1 && !result.Together;   // several files: Studio gets the first one only
            if (fellBack)
            {
                // one short line (the window must stay <= ~760 px); the reason is in the line's dark tooltip
                notes.Add(studioFirst
                    ? L.F("Sliced one by one instead (Studio opens file 1 of {0}).", "Slicet hver for sig i stedet (Studio åbner fil 1 af {0}).", parts)
                    : L.S("Sliced one by one instead.", "Slicet hver for sig i stedet."));
                autoWarning = true;
            }
            else if (studioFirst)
                notes.Add(L.F("\"Open in Studio\" opens only the first file (1 of {0}).",
                              "\"Åbn i Studio\" åbner kun den første fil (1 af {0}).", parts));
            // batch-2: what sharing the last plate did: the trade-off ("Sharing: 1 plate less, +2 h 11 m, +37 g"), or one short
            // Warning line when it fell back (the reason in the line's dark tooltip, like "together")
            bool shareFellBack = result.Share != null && !result.Share.Applied && !string.IsNullOrEmpty(result.Share.Problem);
            // with the plates link the trade-off is the plates menu's last line (the box stays at 3 lines); one plate: a note
            shareMenuNote = result.Share != null && result.Share.Applied ? ShareNoteText(result.Share) : null;
            if (shareMenuNote != null && !platesLink) notes.Add(shareMenuNote);
            else if (shareFellBack && !fellBack)
            {
                notes.Add(L.S("Each colour kept its own plates.", "Hver farve beholdt sine egne plader."));
                autoWarning = true;
            }
            // a sliced file made for another of our printers: its time and weight are that printer's (warning stripe)
            if (result.FromSlicedFile && !string.IsNullOrEmpty(result.PrinterModel) &&
                Wear.FindModel(result.PrinterModel).Key != Wear.FindModel(settings.PrinterModel).Key)
            {
                notes.Add(L.F("The file was sliced for the {0}, not the {1}: its time and weight are for that printer.",
                              "Filen er slicet til {0}, ikke {1}: tid og vægt gælder den printer.",
                              Wear.FindModel(result.PrinterModel).Label, Wear.FindModel(settings.PrinterModel).Label));
                autoWarning = true;
            }
            if (notes.Count > 0) lblAuto.SetText(lblAuto.Text, string.Join("\n", notes));
            if (fellBack && lblAuto.NoteControl != null && printerTip != null)
                printerTip.SetToolTip(lblAuto.NoteControl, WrapTip(result.TogetherProblem, 45));
            else if (shareFellBack && lblAuto.NoteControl != null && printerTip != null)
                printerTip.SetToolTip(lblAuto.NoteControl, WrapTip(result.Share.Problem, 45));
            ShowPlatesLink(result.PlateList);
            platesProblem = fellBack ? result.TogetherProblem : null;   // a Repeat print shows them in the link's tooltip
            platesStudioOf = studioFirst ? parts : 0;
            SetAutoColor();
            if (noTime) txtHours.Focus();
            else if (noGrams && !txtGrams.ReadOnly) txtGrams.Focus();
        }

        // ---------- batch-2: share the colours' last plate ----------

        /// Mixed-colour plates of a slice whose last plate was shared (SliceResult.Share applied); 0 = none / not shared.
        static int SharedMixedPlates(SliceResult r)
        {
            if (r == null || r.Share == null || !r.Share.Applied || r.PlateList == null) return 0;
            return Math.Max(1, r.PlateList.Count(p => p != null && (p.Shared || BambuSlicer.IsMixedPlate(p))));
        }

        /// "1 shared plate" / "1 fælles plade" ("2 shared plates" / "2 fælles plader"): the ✓ line's part (qol #2).
        internal static string SharedPlatesText(int n)
        {
            return n == 1 ? L.S("1 shared plate", "1 fælles plade") : L.F("{0} shared plates", "{0} fælles plader", n);
        }

        /// "Sharing: 1 plate fewer, +2 h 11 m, +37 g" / "Deling: 1 plade færre, +2 t 11 m, +37 g" (what the shared plate cost
        /// against each colour's own last plate); a fallback = its Problem text; null = "".
        internal static string ShareNoteText(ShareInfo s)
        {
            if (s == null) return "";
            if (!s.Applied) return s.Problem ?? "";
            string plates = s.PlatesSaved == 1 ? L.S("1 plate fewer", "1 plade færre") : L.F("{0} plates fewer", "{0} plader færre", s.PlatesSaved);   // ui-text #3
            int min = (int)Math.Round(Math.Abs(s.ExtraSeconds) / 60.0);
            string time = (s.ExtraSeconds < 0 ? "–" : "+") + (min >= 60 ? L.F("{0} h {1} m", "{0} t {1} m", min / 60, min % 60) : min + " m");
            double g = Math.Round(Math.Abs(s.ExtraGrams));
            string grams = (s.ExtraGrams < 0 ? "–" : "+") + Num.Fmt(g) + " g";
            return L.S("Sharing: ", "Deling: ") + plates + ", " + time + ", " + grams;
        }

        // ---------- a colour per file and plates (idea 21) ----------
        /// The files of the job with the filament / colour picked for each (null = a normal job); saved with the quote
        /// (History.SetParts), set again by Open again / Repeat print, cleared by a new job, a link edit and Clear.
        List<HistoryPart> jobParts;
        bool jobTogether;

        /// Text broken into lines of about `width` characters at spaces (dark tooltips keep lines short).
        static string WrapTip(string text, int width)
        {
            var lines = new List<string>();
            string line = "";
            foreach (var w in (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + w.Length > width) { lines.Add(line); line = w; }
                else line = line.Length == 0 ? w : line + " " + w;
            }
            if (line.Length > 0) lines.Add(line);
            return string.Join("\n", lines);
        }

        /// 2+ plates (also one per separately sliced file): a link line under the result that says the price is for all of
        /// them ("The price is for all 3 plates – show them"); it opens a dark menu with one line per plate
        /// (BambuSlicer.PlateLine), the object names in each line's dark tooltip.
        void ShowPlatesLink(List<PlateInfo> plates)
        {
            shownPlates = null;
            if (plates == null || plates.Count < 2) { SetResultLinks(null, null, null); return; }   // only "Scale…" (if any)
            var list = plates.Where(p => p != null).ToList();
            shownPlates = list;
            SetResultLinks(PlatesLinkText(list.Count, false), () => ShowPlatesMenu(list), L.F("{0} plates – show them", "{0} plader – vis dem", list.Count));
        }

        /// The links under a finished job's line (issue #8): the plates link (when any) and "Scale…" (a job that can be scaled:
        /// no G-code) on one line; when both don't fit the box's width the plates link gets its short text.
        void SetResultLinks(string platesText, Action platesClick, string platesShort)
        {
            // the links on one line: plates (2+), "Scale…", and for 2+ colours "Prime tower: on" (issue #8 step 3)
            var texts = new List<string>();
            var clicks = new List<Action>();
            if (platesText != null) { texts.Add(platesText); clicks.Add(platesClick); }
            if (CanCopiesJob) { texts.Add(CopiesLinkText); clicks.Add(() => ShowCopiesWindow()); }   // batch & plates
            if (CanScaleJob) { texts.Add(ScaleLinkText); clicks.Add(() => ShowScaleWindow()); }
            if (CanTowerJob) { texts.Add(TowerLinkText); clicks.Add(() => { var _ = ToggleTowerAsync(); }); }
            if (texts.Count == 0) return;
            if (texts.Count == 1) { lblAuto.SetLink(texts[0], clicks[0]); SetTowerTip(); return; }
            Label lbl = lblAuto.Controls.Count > 0 ? lblAuto.Controls[0] as Label : null;
            int width = lbl != null ? lbl.MaximumSize.Width : 0;
            if (platesText != null && lbl != null && width > 0 && platesShort != null)
            {
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
                int need = 0;
                foreach (var t in texts) need += TextRenderer.MeasureText(t, lbl.Font, new Size(int.MaxValue, int.MaxValue), flags).Width + ScalePx(16);
                if (need - ScalePx(16) + 4 > width) texts[0] = platesShort;
            }
            lblAuto.SetLinks(texts.ToArray(), clicks.ToArray());
            SetTowerTip();
        }

        /// The tower link's dark tooltip: what a click does (qol: the switch reads like a state, the tip says the action).
        void SetTowerTip()
        {
            if (printerTip == null || !CanTowerJob) return;
            string text = TowerLinkText;
            var stack = new Stack<Control>();
            stack.Push(lblAuto);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                if (c is Theme.LinkText && c.Text == text)
                {
                    printerTip.SetToolTip(c, jobTower
                        ? L.S("Click to slice again without the prime tower\n(saves filament, may leave colour smudges)",
                              "Klik for at slice igen uden primetårn\n(sparer filament, kan give farvepletter)")
                        : L.S("Click to slice again with the prime tower\n(cleaner colour changes, uses a little more filament)",
                              "Klik for at slice igen med primetårn\n(renere farveskift, bruger lidt mere filament)"));
                    return;
                }
                foreach (Control k in c.Controls) stack.Push(k);
            }
        }

        /// "Prime tower: on" / "Primetårn: til" (the shown job's switch; a click slices it again with the other one).
        string TowerLinkText { get { return jobTower ? L.S("Prime tower: on", "Primetårn: til") : L.S("Prime tower: off", "Primetårn: fra"); } }

        /// The tower link is offered for a sliced job with 2+ colours that can be sliced again (not G-code / a sliced file
        /// read as it is); picture mode: the sample jobs with 2+ colours.
        bool CanTowerJob
        {
            get
            {
                if (!jobLoaded || filaments == null) return false;
                if (offline) return true;
                return lastJob != null && lastJob.Scalable && lastJobResult != null && !lastJobResult.FromSlicedFile;
            }
        }

        /// The tower link: the same files, size and colours sliced again with the tower switched (a failure keeps the job).
        async Task<bool> ToggleTowerAsync()
        {
            if (!CanTowerJob || cts != null || offline) return false;
            towerForNextSlice = !jobTower;
            bool ok = await RescaleAsync(jobScale);
            towerForNextSlice = null;
            return ok;
        }

        static string ScaleLinkText { get { return L.S("Scale…", "Skalér…"); } }

        /// The too-small text for a failed slice at scale (BambuSlicer.TooSmallText). sameJob = Scale… on the shown job: its
        /// 100 % size = the last slice's size at jobScale (still the old one: it changes only after a slice); else unknown.
        string TooSmallMessage(ScaleXyz scale, bool sameJob)
        {
            var r = sameJob ? lastJobResult : null;
            if (r == null || !(r.SizeX > 0) || !(r.SizeY > 0) || !(r.SizeZ > 0)) return BambuSlicer.TooSmallText(scale, 0, 0, 0);
            return BambuSlicer.TooSmallText(scale, BambuSlicer.SizeAt(r.SizeX, jobScale.X, 100), BambuSlicer.SizeAt(r.SizeY, jobScale.Y, 100),
                                            BambuSlicer.SizeAt(r.SizeZ, jobScale.Z, 100));
        }

        /// The Scale window for the shown job (issue #8); Apply slices the same files again at the new size.
        void ShowScaleWindow()
        {
            if (!CanScaleJob || cts != null) return;
            ScaleForm f = NewScaleForm();
            ScaleXyz pct;
            using (f)
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                pct = f.Result;
            }
            var _ = RescaleAsync(pct);
        }

        ScaleForm NewScaleForm()
        {
            var r = lastJobResult;
            return new ScaleForm(jobScale, r != null ? r.SizeX : 0, r != null ? r.SizeY : 0, r != null ? r.SizeZ : 0,
                                 r != null ? r.SizeObjects : 0, settings.PrinterModel, UniformOnlyReason());
        }

        /// Step 2: null when every file of the shown job can get its own X / Y / Z (STL, OBJ, 3MF); else the note for the
        /// first one that can't (STEP / AMF: the Scale window keeps the same % on every axis).
        string UniformOnlyReason()
        {
            var job = lastJob;
            if (job == null) return null;
            var names = new List<string>();
            if (job.LocalFiles != null) names.AddRange(job.LocalFiles);
            if (job.Chosen != null) names.AddRange(job.Chosen.Where(c => c != null).Select(c => c.Name ?? ""));
            foreach (var n in names)
                if (!BambuSlicer.CanScaleXyz(n)) return BambuSlicer.ScaleXyzFormatText(Path.GetFileName(n));
            return null;
        }

        /// Slices the shown job again at pct (same files, colour plan and model name; a failed slice keeps the job as it was).
        async Task<bool> RescaleAsync(ScaleXyz pct)
        {
            var job = lastJob;
            if (job == null || !job.Scalable) return false;
            var keepRepeat = repeatOf;   // a repeat stays a repeat (its record is saved as one)
            bool ok = await AnalyzeAsync(job.PrintablesId, job.LocalFiles, null, null, false, pct, job);
            if (ok) repeatOf = keepRepeat;
            return ok;
        }

        /// A job is shown that Scale… can slice again: it sliced or was read from files that aren't G-code (picture mode:
        /// the sample job).
        bool CanScaleJob { get { return jobLoaded && (offline || (lastJob != null && lastJob.Scalable)); } }

        // ---------- batch & plates (issue #9 / #10): the Copies link and window ----------

        /// "Copies: 4…" / "Antal: 4…" (the shown job's copies; a click opens the Copies window).
        // qol #13: "40 copies…" / "40 stk.…" ("1 copy…")
        string CopiesLinkText
        {
            get
            {
                int n = Math.Max(1, jobCopies);
                return n == 1 ? L.S("1 copy…", "1 stk.…") : L.F("{0} copies…", "{0} stk.…", n);
            }
        }

        /// copies per file cleaned to n files: null / all 1 = null; one value for 2+ files = that value for each.
        static List<int> NormalizeCopies(IList<int> copies, int n)
        {
            if (copies == null || copies.Count == 0 || n < 1) return null;
            var list = new List<int>(n);
            for (int i = 0; i < n; i++) list.Add(Math.Max(1, copies.Count == 1 ? copies[0] : i < copies.Count ? copies[i] : 1));
            return list.Any(c => c > 1) ? list : null;
        }

        /// Back to one copy (a new job, Clear, a failed slice).
        void ResetCopies()
        {
            jobCopies = 1;
            jobPlates = 0;
            jobPerPlate = 0;
            jobCopiesMultiplied = false;
            jobCopiesTyped = false;
            jobFileCopies = null;
        }

        /// The Copies link is offered for a finished job that can be sliced again or multiplied (not while a job runs;
        /// picture mode: the sample jobs). Repeat print / Open again records without their files: not offered.
        bool CanCopiesJob
        {
            get
            {
                if (!jobLoaded) return false;
                if (offline) return true;
                return lastJob != null && lastJobResult != null;
            }
        }

        /// The window's mode for the shown job: a sliced file / G-code read as it is = Multiply, else Slice.
        CopiesForm.Mode CopiesMode
        {
            get
            {
                if (jobCopiesTyped || !jobLoaded) return CopiesForm.Mode.Typed;
                var r = lastJobResult;
                return r != null && (r.FromSlicedFile || r.CopiesMultiplied) ? CopiesForm.Mode.Multiply : CopiesForm.Mode.Slice;
            }
        }

        /// Files in the shown job (the Copies window's number applies to each).
        int JobFileCount
        {
            get
            {
                var job = lastJob;
                if (job == null) return 1;
                if (job.Chosen != null) return Math.Max(1, job.Chosen.Count);
                return job.LocalFiles != null ? Math.Max(1, job.LocalFiles.Count) : 1;
            }
        }

        CopiesForm NewCopiesForm()
        {
            var mode = CopiesMode;
            int files = mode == CopiesForm.Mode.Typed ? 1 : JobFileCount;
            // several files: the window's number is per file (shown: the same number of every file, else the first one's)
            int each = files > 1 && jobFileCopies != null && jobFileCopies.Count > 0 ? jobFileCopies[0] : files > 1 ? 1 : jobCopies;
            // batch-2: the share tick for a job of 2+ colours from STL / OBJ files (the last slice's sharing line under the help)
            bool offer = mode == CopiesForm.Mode.Slice && CanShareJob;
            var r = lastJobResult;
            string note = offer && r != null && r.Share != null ? ShareNoteText(r.Share) : null;
            return new CopiesForm(mode, each, jobPlates, files, offer ? (bool?)jobShare : null, note);
        }

        /// batch-2: the shown job has 2+ colours among its STL / OBJ files (a colour plan), so its last plates could be shared.
        bool CanShareJob
        {
            get
            {
                var job = lastJob;
                if (job == null || job.Plan == null || job.Plan.Parts == null) return false;
                var keys = new HashSet<int>();
                foreach (var p in job.Plan.Parts)
                    if (p != null && p.Slot > 0 && BambuSlicer.CanShareFile(p.File)) keys.Add(p.Slot);
                return keys.Count >= 2;
            }
        }

        /// The Copies window for the shown job; Apply slices it again with the new copies (Slice / Multiply) or prices the
        /// typed copy N times (Typed).
        void ShowCopiesWindow()
        {
            if (cts != null) return;
            if (!CanCopiesJob && !TypedCopiesOffered) return;
            var mode = CopiesMode;
            int copies, plates;
            bool share;
            using (var f = NewCopiesForm())
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                copies = f.Copies;
                plates = f.Plates;
                share = f.Share;
            }
            if (mode == CopiesForm.Mode.Typed) { SetTypedCopies(copies, plates); return; }
            var _ = RecopyAsync(copies, CanShareJob ? share : jobShare);
        }

        /// Slices the shown job again with copies of each file (same files, colours, size and tower; a failed slice keeps the
        /// job as it was).
        async Task<bool> RecopyAsync(int copies)
        {
            return await RecopyAsync(copies, jobShare);
        }

        /// batch-2: as above with the share choice (the Copies window's tick).
        async Task<bool> RecopyAsync(int copies, bool share)
        {
            var job = lastJob;
            if (job == null) return false;
            var keepRepeat = repeatOf;
            copiesForNextSlice = new List<int> { Math.Max(1, copies) };
            towerForNextSlice = jobTower;
            shareForNextSlice = share;
            bool ok = await AnalyzeAsync(job.PrintablesId, job.LocalFiles, null, null, false, jobScale, job);
            copiesForNextSlice = null;
            towerForNextSlice = null;
            shareForNextSlice = null;
            if (ok) repeatOf = keepRepeat;
            return ok;
        }

        // ---------- typed by hand (no slicing): the boxes are ONE copy, priced with Pricing.ForCopies ----------

        /// The info box's line for a job typed by hand (2 lines like the start text, so nothing moves): what the boxes mean
        /// and the Copies link. Shown once the typed numbers give a price and no job is loaded.
        bool typedLineShown;

        /// A job typed by hand can get copies: no job loaded, nothing running, the boxes give a price.
        bool TypedCopiesOffered { get { return !jobLoaded && cts == null && (last != null || jobCopiesTyped); } }

        /// Sets the typed job's copies and plates (1 copy = back to a plain typed job) and prices it again.
        void SetTypedCopies(int copies, int plates)
        {
            jobCopies = Math.Max(1, copies);
            jobPlates = jobCopies > 1 ? Math.Max(1, Math.Min(plates, jobCopies)) : 0;
            jobPerPlate = 0;
            jobCopiesTyped = jobCopies > 1;
            jobCopiesMultiplied = false;
            jobFileCopies = null;
            Recalculate();
        }

        /// The typed-job line: "Typed by hand: time and weight of one copy." + "Copies: 4…" (4 copies: "… 4 copies on 2 plates.").
        void UpdateTypedLine()
        {
            if (jobLoaded || cts != null || reopened != null || repeatLine != null) { typedLineShown = false; return; }
            if (offline && !jobCopiesTyped) return;   // picture mode: only its typed-copies sample (SetSampleTypedCopies)
            bool idle = autoIdle || typedLineShown;
            if (!idle) return;
            if (last == null && !jobCopiesTyped)
            {
                if (typedLineShown) { typedLineShown = false; ShowAutoIdle(); }
                return;
            }
            string text = L.S("Typed by hand: time and weight are for one copy.", "Indtastet: tid og vægt gælder ét stk.");
            // the link says the copies and plates ("Copies: 4 on 2 plates…"), so the box stays 2 lines
            string link = jobCopiesTyped && jobPlates > 1
                ? L.F("{0} copies on {1} plates…", "{0} stk. på {1} plader…", jobCopies, jobPlates)
                : CopiesLinkText;
            if (typedLineShown && lblAuto.Text == text && lblAuto.LinkControl != null && lblAuto.LinkControl.Text == link) return;
            SetAuto(text, false);
            typedLineShown = true;
            lblAuto.SetLink(link, ShowCopiesWindow);
        }

        /// "The price is for both plates – show them" (2) / "… all N plates …" (3+); fellBack = "together" didn't work
        /// (Repeat print folds the "Sliced one by one" note into the link to keep the box at 3 lines).
        static string PlatesLinkText(int count, bool fellBack)
        {
            if (fellBack)
                return count == 2 ? L.S("Sliced one by one: the price is for both plates – show them", "Slicet hver for sig: prisen gælder begge plader – vis dem")
                                  : L.F("Sliced one by one: the price is for all {0} plates – show them", "Slicet hver for sig: prisen gælder alle {0} plader – vis dem", count);
            return count == 2 ? L.S("The price is for both plates – show them", "Prisen gælder begge plader – vis dem")
                              : L.F("The price is for all {0} plates – show them", "Prisen gælder alle {0} plader – vis dem", count);
        }

        void ShowPlatesMenu(List<PlateInfo> plates)
        {
            var at = lblAuto.LinkControl;
            if (at == null) return;
            var tip = new ToolTip { OwnerDraw = true, InitialDelay = 300 };
            tip.Draw += DrawDarkTip;
            var menu = PlatesMenu(plates, tip);
            menu.Closed += delegate { tip.Hide(menu); BeginInvoke((MethodInvoker)delegate { menu.Dispose(); tip.Dispose(); }); };
            menu.Show(at, new Point(0, at.Height));
        }

        /// A plates-menu line: BambuSlicer.PlateLine, or for a batch plate (PlateInfo.Copies > 0) "Plate 1 – 6 h 54 m, 136,8 g,
        /// 28 copies" (DA "28 stk.") instead of the object count.
        static string PlateMenuLine(PlateInfo p, bool showFile)
        {
            if (p == null || p.Copies <= 0) return BambuSlicer.PlateLine(p, showFile) + ColourChangesPart(p);
            string numbers = BambuSlicer.PlateNumbers(new PlateInfo { Seconds = p.Seconds, Grams = p.Grams });
            // batch-2: the shared last plate says so ("Plate 3 (shared)"), not Bambu Studio's internal plate name
            string title = p.Shared ? L.S("Shared plate", "Fælles plade") : BambuSlicer.PlateTitle(p, showFile);
            return title + " – " + numbers + ", " +
                   (p.Copies == 1 ? L.S("1 copy", "1 stk.") : L.F("{0} copies", "{0} stk.", p.Copies)) + ColourChangesPart(p);
        }

        /// batch-2: " · 212 colour changes" / " · 212 farveskift" for a plate with 2+ colours whose changes are known; else "".
        static string ColourChangesPart(PlateInfo p)
        {
            if (p == null || p.ColourChanges <= 0 || !BambuSlicer.IsMixedPlate(p)) return "";
            return " · " + (p.ColourChanges == 1 ? L.S("1 colour change", "1 farveskift") : L.F("{0} colour changes", "{0} farveskift", p.ColourChanges));
        }

        /// batch-2: the colours on a plate (filaments of 0,5 g or more, as BambuSlicer.PlateColourCount counts them), in slot
        /// order; the plates menu draws one dot each.
        static List<Color> PlateColours(PlateInfo p)
        {
            var list = new List<Color>();
            if (p == null || p.Filaments == null) return list;
            foreach (var f in p.Filaments.Where(x => x != null && x.Grams >= 0.5).OrderBy(x => x.Slot))
                list.Add(ParseColour(f.Colour));
            return list;
        }

        /// A row of round colour dots (d px each, 3 px apart, Muted ring so black shows) on a transparent strip `slots` dots wide.
        static Bitmap ColourDots(List<Color> colours, int slots, int d)
        {
            var bmp = new Bitmap(Math.Max(1, slots * (d + 3)), d);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                for (int i = 0; i < colours.Count && i < slots; i++)
                {
                    var r = new Rectangle(i * (d + 3) + 1, 1, d - 3, d - 3);
                    if (!colours[i].IsEmpty) using (var b = new SolidBrush(colours[i])) g.FillEllipse(b, r);
                    using (var pen = new Pen(Theme.Muted)) g.DrawEllipse(pen, r);
                }
            }
            return bmp;
        }

        /// The names in a plate's tooltip. Batch plates (qol #4): Bambu Studio's copy names ("3DBenchy.stl_29", …) grouped
        /// by model, in order: "3DBenchy × 12" (one copy: "3DBenchy"); other plates: the object names as they are.
        static List<string> PlateObjectNames(PlateInfo p)
        {
            var objects = p != null && p.Objects != null ? p.Objects : new List<string>();
            if (p == null || p.Copies <= 0) return objects;
            var order = new List<string>();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in objects)
            {
                string name = System.Text.RegularExpressions.Regex.Replace(o ?? "", @"_\d+$", "");
                name = System.Text.RegularExpressions.Regex.Replace(name, @"\.(stl|obj|3mf|step|stp|amf)$", "",
                                                                   System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (name.Length == 0) continue;
                int n;
                if (counts.TryGetValue(name, out n)) counts[name] = n + 1;
                else { counts[name] = 1; order.Add(name); }
            }
            return order.Select(n => counts[n] > 1 ? n + " × " + counts[n] : n).ToList();
        }

        /// tip (may be null): shows a plate's object names beside its line while the mouse is on it.
        ContextMenuStrip PlatesMenu(List<PlateInfo> plates, ToolTip tip)
        {
            bool showFile = plates.Select(p => p.File ?? "").Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            // batch-2: a job with 2+ colours on its plates gets a colour dot per colour on each plate (image margin)
            var colours = plates.Select(PlateColours).ToList();
            int slots = colours.Count == 0 ? 0 : colours.Max(c => c.Count);
            bool dots = slots >= 1 && colours.SelectMany(c => c).Select(c => c.ToArgb()).Distinct().Count() >= 2;
            int d = ScalePx(12);
            var menu = new ContextMenuStrip { ShowImageMargin = dots, Renderer = new QuoteMenuRenderer(), ShowItemToolTips = false,
                                              BackColor = Theme.Input, ForeColor = Theme.Text };
            if (dots) menu.ImageScalingSize = new Size(slots * (d + 3), d);
            var images = new List<Image>();
            menu.Disposed += delegate { foreach (var i in images) i.Dispose(); };
            menu.Items.Add(new ToolStripMenuItem(L.S("Plates in this job:", "Plader i dette job:")) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            for (int pi = 0; pi < plates.Count; pi++)
            {
                var p = plates[pi];
                var mi = new ToolStripMenuItem(PlateMenuLine(p, showFile).Replace("&", "&&")) { ForeColor = Theme.Text };
                if (dots)
                {
                    var img = ColourDots(colours[pi], slots, d);
                    images.Add(img);
                    mi.Image = img;
                    mi.ImageScaling = ToolStripItemImageScaling.None;
                }
                var objects = PlateObjectNames(p);
                if (objects.Count > 0 && tip != null)
                {
                    const int MaxShown = 12;
                    var names = objects.Take(objects.Count > MaxShown ? MaxShown - 1 : MaxShown)
                                       .Select(o => BambuSlicer.CutName(o, BambuSlicer.MaxPlateNameChars)).ToList();
                    if (objects.Count > MaxShown) names.Add(L.F("+{0} more", "+{0} flere", objects.Count - (MaxShown - 1)));
                    string text = string.Join("\n", names);
                    var item = mi;
                    mi.Tag = text;   // the object names (KeyUp below; the renderer only uses a Color Tag)
                    mi.MouseEnter += delegate
                    {
                        var b = item.Bounds;
                        tip.Show(text, menu, b.Right + 4, b.Top);
                    };
                    mi.MouseLeave += delegate { tip.Hide(menu); };
                }
                menu.Items.Add(mi);
            }
            if (!string.IsNullOrEmpty(shareMenuNote) && ReferenceEquals(plates, shownPlates))
            {
                // batch-2: what sharing the last plate cost / saved, muted under the plates
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem(shareMenuNote) { Enabled = false });
            }
            if (tip != null)
                menu.KeyUp += delegate   // keyboard (arrow keys): the highlighted plate's object names too
                {
                    var sel = menu.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.Selected);
                    var names = sel != null ? sel.Tag as string : null;
                    if (names == null) { tip.Hide(menu); return; }
                    tip.Show(names, menu, sel.Bounds.Right + 4, sel.Bounds.Top);
                };
            return menu;
        }

        List<PlateInfo> shownPlates;   // the plates behind the link (picture mode draws their menu)
        string shareMenuNote;          // batch-2: "Sharing: 1 plate less, +1 h 41 m, +23 g" as the plates menu's last line (null = none)
        string platesProblem;          // the last slice's TogetherProblem when "together" fell back (null = didn't)
        int platesStudioOf;            // the last slice's file count when Studio opens only the first file (0 = not)

        /// Forgets the last slice's plates (a reopened / repeated / cleared job must not show an earlier job's plates).
        void ForgetPlates()
        {
            shownPlates = null;
            shareMenuNote = null;
            platesProblem = null;
            platesStudioOf = 0;
        }

        /// Picture mode: the worst-height result (idea 21): 4 colour rows, a "together" fallback note and the plates link.
        internal void SetSamplePartsJob()
        {
            if (!offline) return;
            txtUrl.Text = L.F("{0} files: ", "{0} filer: ", 4) + "body.stl, wheel_front.stl, wheel_rear.stl, spoiler.stl";
            var r = new SliceResult { Seconds = 4 * 3600 + 25 * 60, Grams = 128.4, Plates = 4, PurgeGrams = -1, ColourChangeHours = -1 };
            string[] colours = { "#000000", "#C12E1F", "#F4EE2A", "#0086D6" };
            string[] profiles = { "Bambu PLA Basic", "Bambu PLA Matte", "Bambu PETG HF", "Bambu PLA Basic" };
            double[] grams = { 62.1, 30.2, 21.6, 14.5 };
            for (int i = 0; i < 4; i++)
                r.Filaments.Add(new FilamentUse { Slot = i + 1, Colour = colours[i], Type = i == 2 ? "PETG" : "PLA", Profile = profiles[i],
                                                  Grams = grams[i], ModelGrams = -1 });
            string[] files = { "body.stl", "wheel_front.stl", "wheel_rear.stl", "spoiler.stl" };
            double[] secs = { 7200, 2700, 2400, 3600 + 600 };
            for (int i = 0; i < 4; i++)
                r.PlateList.Add(new PlateInfo { Index = 1, File = files[i], Name = i == 0 ? "Chassis" : "", Seconds = secs[i], Grams = grams[i],
                                                Objects = i == 0 ? new List<string> { "body", "battery lid", "bumper" } : new List<string> { files[i].Replace(".stl", "") } });
            r.TogetherProblem = L.S("The files don't fit on one plate together, so they were sliced separately.",
                                    "Filerne kan ikke være på én plade sammen, så de blev slicet hver for sig.");   // (sample text)
            txtElecPrice.Text = "3,65";
            // a kept slice, so the "Studio opens file 1 of 4" part shows too (the real fallback has it); never opened here
            r.StudioFile = Path.Combine(BambuSlicer.StudioRoot, "sample", "body.3mf");
            ShowJobResult(r, "RC car wheel set", new List<string>(), 4, settings.ProcessPreset, settings.FilamentPreset, null);
            SetSampleRowPrices("200");
            Recalculate();
        }

        /// Picture mode: a sliced 3MF with 3 plates (one named): the "Show the 3 plates" link.
        internal void SetSamplePlatesJob()
        {
            if (!offline) return;
            ShowLinkFile(@"C:\Models\Desk organiser.3mf");
            var r = new SliceResult { Seconds = 5 * 3600 + 2 * 60, Grams = 151.3, Plates = 3, PurgeGrams = -1, ColourChangeHours = -1, FromSlicedFile = true };
            r.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#8E9089", Type = "PLA", Profile = "Bambu PLA Basic", Grams = 151.3, ModelGrams = -1 });
            r.PlateList.Add(new PlateInfo { Index = 1, Name = "", Seconds = 2 * 3600 + 5 * 60, Grams = 64.2, Objects = new List<string> { "Tray", "Pen cup" } });
            r.PlateList.Add(new PlateInfo { Index = 2, Name = "Wheels", Seconds = 3600 + 20 * 60, Grams = 34.0, Objects = new List<string> { "Wheel A", "Wheel B", "Axle" } });
            r.PlateList.Add(new PlateInfo { Index = 3, Name = "", Seconds = 3600 + 37 * 60, Grams = 53.1, Objects = new List<string> { "Drawer" } });
            ShowJobResult(r, null, new List<string> { @"C:\Models\Desk organiser.3mf" }, 1, settings.ProcessPreset, settings.FilamentPreset, null);
            SetSampleRowPrices("200");
            Recalculate();
        }

        /// Picture mode (batch & plates): the single Benchy x 40 as the CLI sliced it (plans\batch-plates\online\run_clone40:
        /// 28 copies 52 509 s 326,84 g + 12 copies 22 565 s 140,11 g), with the plates and Copies links and "40 pcs · … each".
        /// Call after SetSampleJob (keeps its prices and link).
        internal void SetSampleBatchJob()
        {
            if (!offline) return;
            var r = new SliceResult { Seconds = 52509 + 22565, Grams = 326.84 + 140.11, Plates = 2, PurgeGrams = -1, ColourChangeHours = -1,
                                      Copies = 40, PerPlate = 28, SizeX = 60, SizeY = 31, SizeZ = 48, SizeObjects = 1 };
            r.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#9D432C", Type = "PLA", Profile = "Bambu PLA Matte", Grams = 466.95, ModelGrams = -1 });
            var p1 = new PlateInfo { Index = 1, Seconds = 52509, Grams = 326.84, Copies = 28 };
            var p2 = new PlateInfo { Index = 2, Seconds = 22565, Grams = 140.11, Copies = 12 };
            for (int i = 1; i <= 28; i++) p1.Objects.Add("3DBenchy.stl_" + i);
            for (int i = 29; i <= 40; i++) p2.Objects.Add("3DBenchy.stl_" + i);
            r.PlateList.Add(p1);
            r.PlateList.Add(p2);
            jobCopies = 40; jobPlates = 2; jobPerPlate = 28; jobCopiesMultiplied = false; jobCopiesTyped = false;
            jobFileCopies = new List<int> { 40 };
            ShowJobResult(r, "3D BENCHY", new List<string>(), 1, settings.ProcessPreset, "Bambu PLA Matte", null);
            jobModel = "3D Benchy";
            SetSampleRowPrices("200");
            Recalculate();
        }

        /// Picture mode: the Copies window of the shown job with typed copies (0 = as it opens); typedMode = the window of
        /// a job typed by hand (copies + plates boxes).
        internal CopiesForm SampleCopiesWindow(int typed, bool typedMode)
        {
            if (!offline) return null;
            var f = typedMode ? new CopiesForm(CopiesForm.Mode.Typed, Math.Max(1, jobCopies), Math.Max(1, jobPlates), 1) : NewCopiesForm();
            f.SampleType(typed > 0 ? typed : Math.Max(1, jobCopies));   // also "as it opens": nothing marked in the picture
            if (typedMode && typed > 0) f.SampleTypePlates(Math.Max(1, (typed + 9) / 10));
            return f;
        }

        /// batch-2 picture mode: the quantity discount on (from 10 pcs 5 %, from 50 pcs 10 %, customers only) or off again, then
        /// the shown job is priced again (main_discount.png / quote_discount.png).
        internal void SampleDiscount(bool on)
        {
            if (!offline) return;
            settings.QtyDiscountFrom1 = on ? 10 : 0; settings.QtyDiscountPct1 = on ? 5 : 0;
            settings.QtyDiscountFrom2 = on ? 50 : 0; settings.QtyDiscountPct2 = on ? 10 : 0;
            settings.QtyDiscountWho = 2;
            Recalculate();
        }

        /// batch-2 picture mode: 30 red + 20 blue clips with "share the last plate": one full red plate (24), one full blue
        /// plate (16), and the shared plate (6 red + 4 blue, 2 colours, 61 colour changes); numbers in the style of online's
        /// runs. The ✓ line, the sharing note, the plates link (its menu: main_shared_plates_menu.png).
        internal void SetSampleSharedJob()
        {
            if (!offline) return;
            ClearJob();
            txtUrl.Text = L.F("{0} files: ", "{0} filer: ", 2) + "clip_red.stl, clip_blue.stl";
            var r = new SliceResult { Seconds = 4 * 3600 + 12 * 60 + 3 * 3600 + 5 * 60 + 2 * 3600 + 43 * 60, Grams = 69.4 + 46.3 + 53.6, Plates = 3,
                                      PurgeGrams = 22.6, ColourChangeHours = 1.68, Copies = 50, PerPlate = 24 };
            r.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#C12E1F", Type = "PLA", Profile = "Bambu PLA Basic", Grams = 69.4 + 30.4, ModelGrams = -1 });
            r.Filaments.Add(new FilamentUse { Slot = 2, Colour = "#0A2989", Type = "PLA", Profile = "Bambu PLA Basic", Grams = 46.3 + 23.2, ModelGrams = -1 });
            var p1 = new PlateInfo { Index = 1, File = "clip_red.stl", Seconds = 4 * 3600 + 12 * 60, Grams = 69.4, Copies = 24 };
            p1.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#C12E1F", Grams = 69.4 });
            var p2 = new PlateInfo { Index = 1, File = "clip_blue.stl", Seconds = 3 * 3600 + 5 * 60, Grams = 46.3, Copies = 16 };
            p2.Filaments.Add(new FilamentUse { Slot = 2, Colour = "#0A2989", Grams = 46.3 });
            var p3 = new PlateInfo { Index = 1, File = "clip_red.stl + clip_blue.stl", Seconds = 2 * 3600 + 43 * 60, Grams = 53.6, Copies = 10,
                                     Shared = true, ColourChanges = 61 };
            p3.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#C12E1F", Grams = 30.4 });
            p3.Filaments.Add(new FilamentUse { Slot = 2, Colour = "#0A2989", Grams = 23.2 });
            for (int i = 1; i <= 24; i++) p1.Objects.Add("clip_red.stl_" + i);
            for (int i = 1; i <= 16; i++) p2.Objects.Add("clip_blue.stl_" + i);
            for (int i = 25; i <= 30; i++) p3.Objects.Add("clip_red.stl_" + i);
            for (int i = 17; i <= 20; i++) p3.Objects.Add("clip_blue.stl_" + i);
            r.PlateList.Add(p1); r.PlateList.Add(p2); r.PlateList.Add(p3);
            r.Share = new ShareInfo { Applied = true, Colours = 2, PlatesSaved = 1, ExtraSeconds = 6042, ExtraGrams = 22.6, ColourChanges = 61 };
            txtElecPrice.Text = "3,65";
            jobCopies = 50; jobPlates = 3; jobPerPlate = 24; jobCopiesMultiplied = false; jobCopiesTyped = false;
            jobFileCopies = new List<int> { 30, 20 };
            jobShare = true;
            ShowJobResult(r, null, new List<string> { @"C:\Models\clip_red.stl", @"C:\Models\clip_blue.stl" }, 2, settings.ProcessPreset, "Bambu PLA Basic", null);
            jobModel = "Cable clip";
            SetSampleRowPrices("200");
            Recalculate();
        }

        /// batch-2 picture mode: the Copies window of a two-colour job with the share tick (on) and the last slice's sharing line.
        internal CopiesForm SampleShareCopiesWindow()
        {
            if (!offline) return null;
            var f = new CopiesForm(CopiesForm.Mode.Slice, 30, 3, 2, true, lastJobResult != null && lastJobResult.Share != null ? ShareNoteText(lastJobResult.Share) : null);
            f.SampleType(30);
            return f;
        }

        /// Picture mode: a job typed by hand (no link, no slicing): one copy's 0 h 38 m / 11,8 g, 12 copies on 2 plates.
        internal void SetSampleTypedCopies()
        {
            if (!offline) return;
            ClearJob();
            txtHours.Text = "0";
            txtMinutes.Text = "38";
            txtGrams.Text = "11,8";
            SetSampleRowPrices("200");
            txtElecPrice.Text = "3,65";
            ShowAutoIdle();
            SetTypedCopies(12, 2);
        }

        /// "--test-batch" (test mode + --hidden): batch & plates without Bambu Studio, through the real code: a job typed by
        /// hand (1 copy = the old texts; the Copies window's typed mode; totals, price lines, Copy text, quote page), the
        /// plate change fee, the unit-price setting, saving / Open again, a combined bill with copies, the Copies window's
        /// checks (range, "several minutes" question, sliced file, several files), the file picker's Copies boxes, and a
        /// G-code printed 3 times (AnalyzeAsync + the Copies link's path, no slicer needed). test_batch.txt, exit 0 / 4.
        internal async void TestBatchFlow()
        {
            if (!AppSettings.TestMode || !Program.Hidden) return;
            string dir = AppSettings.Folder;
            var log = new TestLog();
            log.Add(SetupTest.StandInNote);
            testClipboard = t => { };
            var when = new DateTime(2026, 11, 24, 12, 0, 0);
            Action typeJob = () =>
            {
                ClearJob();
                txtHours.Text = "0"; txtMinutes.Text = "38"; txtGrams.Text = "11,8";
                rowPrice[0].Text = "200"; txtElecPrice.Text = "3,65";
                Recalculate();
            };
            try
            {
                double keepFee = settings.PlateChangeFee, keepUnit = settings.QuoteShowUnitPrice;
                settings.PlateChangeFee = 0; settings.QuoteShowUnitPrice = 1;

                // 1. one copy typed by hand: the texts are exactly the old ones; the typed line offers "Copies: 1…"
                typeJob();
                log.Check(last != null && lastInput != null, "1a typed job priced: " + (last != null ? Num.Kr(last.OthersPrice) : "none"));
                string oldText = QuoteRows.JobText(null, LinkSource(), 0, 38, txtGrams.Text, 0, last.FriendsPrice, last.OthersPrice);
                log.Check(QuoteText(null) == oldText, "1b 1 copy: Copy text = the old text");
                var d1 = BuildQuote(false, when, "1", "");
                log.Check(d1.Quantity == 1 && d1.EachLine == "" && !d1.Model.Contains("×"), "1c 1 copy: no Quantity row, no each line, name \"" + d1.Model + "\"");
                log.Check(lblFriendsInfo.Text == friendsMarkupLine && lblOthersInfo.Text == othersMarkupLine,
                          "1d 1 copy: the markup lines under the prices (\"" + lblOthersInfo.Text + "\")");
                log.Check(lastInput.Quantity == 1 && last.PlateFee == 0, "1e 1 copy: Quantity 1, no plate fee");
                log.Check(typedLineShown && lblAuto.LinkControl != null && lblAuto.LinkControl.Text == CopiesLinkText,
                          "1f typed line: \"" + lblAuto.Text + "\" + link \"" + (lblAuto.LinkControl != null ? lblAuto.LinkControl.Text : "none") + "\"");
                double oneOthers = last.OthersPrice;

                // 2. the Copies window (typed mode): checks, then 12 copies on 2 plates
                using (var f = NewCopiesForm())
                {
                    log.Check(f.TestHasPlates, "2a typed: the window has a Build plates box");
                    f.SampleType(0);
                    log.Check(!f.TestApplyOn && f.TestInfoKind == InfoKind.Error, "2b 0 copies: Apply off, " + f.TestInfoText);
                    f.SampleType(501);
                    log.Check(!f.TestApplyOn && f.TestInfoKind == InfoKind.Error, "2c 501 copies: Apply off, " + f.TestInfoText);
                    f.SampleType(12); f.SampleTypePlates(13);
                    log.Check(!f.TestApplyOn && f.TestInfoKind == InfoKind.Error, "2d 13 plates for 12 copies: Apply off, " + f.TestInfoText);
                    f.SampleTypePlates(2);
                    log.Check(f.TestApplyOn && f.TestInfoKind == InfoKind.Info, "2e 12 on 2 plates: Apply on");
                    bool okA = f.TestPressApply(this);
                    log.Check(okA && f.Copies == 12 && f.Plates == 2, "2f Apply: " + f.Copies + " copies on " + f.Plates + " plates");
                    if (okA) SetTypedCopies(f.Copies, f.Plates);
                }
                log.Check(lastInput.Quantity == 12 && lastInput.Plates == 2 && lastInput.Hours * 60 + lastInput.Minutes == 12 * 38 &&
                          Math.Abs(lastInput.FilamentGrams - 12 * 11.8) < 0.001,
                          "2g typed x 12: " + lastInput.Hours + " h " + lastInput.Minutes + " m, " + Num.Fmt(lastInput.FilamentGrams) + " g (one copy in the boxes)");
                log.Check(lblOthersInfo.Text == History.EachText(last.OthersPrice, 12) && lblFriendsInfo.Text == History.EachText(last.FriendsPrice, 12),
                          "2h price lines: \"" + lblFriendsInfo.Text + "\" / \"" + lblOthersInfo.Text + "\"");
                // 1 copy is below the minimum (50 kr.), so 12 copies cost more but not 12 times as much
                log.Check(lblGramsUnit.Text == L.S("g each", "g/stk.") && lblMinutesUnit.Text == L.S("m each", "m/stk."),
                          "2m typed units: " + lblMinutesUnit.Text + " / " + lblGramsUnit.Text);
                log.Check(last.OthersPrice > oneOthers, "2i the price is for 12: " + Num.Kr(last.OthersPrice) + " (1 copy " + Num.Kr(oneOthers) + ")");
                log.Check(lblAuto.LinkControl != null && lblAuto.LinkControl.Text.Contains("12") && lblAuto.LinkControl.Text.Contains("2"),
                          "2j typed link: \"" + (lblAuto.LinkControl != null ? lblAuto.LinkControl.Text : "none") + "\"");
                string t12 = QuoteText(null);
                log.Check(t12.Contains(History.QuantityText(12)) && t12.Contains(History.OnPlatesText(2)) &&
                          t12.Contains(History.EachPriceText(last.OthersPrice, 12)) && t12.Contains(QuoteRows.Time(7, 36).Replace(" m", "")),
                          "2k Copy text: " + t12.Replace(Environment.NewLine, " | "));
                var d12 = BuildQuote(false, when, "12", "Batch Kunde");
                log.Check(d12.Quantity == 12 && d12.Plates == 2 && d12.EachLine == History.EachText(d12.Breakdown.Total, 12) && d12.Model.StartsWith("12 × "),
                          "2l quote page: \"" + d12.Model + "\", Quantity 12, Plates 2, each \"" + d12.EachLine + "\"");
                using (var page = QuotePage.Render(d12, false)) page.Save(Path.Combine(dir, "test_batch_quote.png"), System.Drawing.Imaging.ImageFormat.Png);
                Screenshots.Snap(this, dir, "test_batch_typed.png");

                // 3. plate change fee 5 kr.: one extra plate = 5 kr. in both prices, a "Plate changes" row
                double before = last.FriendsPrice;
                settings.PlateChangeFee = 5;
                Recalculate();
                log.Check(Math.Abs(last.PlateFee - 5) < 1e-9, "3a plate fee " + Num.Kr(last.PlateFee) + " (1 extra plate x 5,00)");
                var rows = QuoteRows.Build(Pricing.Breakdown(lastInput, settings, false, null), null, -1, lastInput.Hours, lastInput.Minutes, "");
                var feeRow = rows.FirstOrDefault(r => r.Label == QuoteRows.PlatesRowLabel());
                log.Check(feeRow != null && feeRow.Detail == "1  ×  " + Num.Kr(5) && feeRow.Amount == Num.Kr(5),
                          "3b breakdown row: " + (feeRow != null ? feeRow.Label + " | " + feeRow.Detail + " | " + feeRow.Amount : "none"));
                log.Check(printerTip.GetToolTip(lblOthersInfo).Contains(Num.Kr(5)), "3c the fee is in the price line's tooltip: " + printerTip.GetToolTip(lblOthersInfo).Replace("\n", " / "));
                log.Add("INFO friends " + Num.Kr(before) + " -> " + Num.Kr(last.FriendsPrice));
                log.Check(plateFeeRowOn && lblPlateFee.Text == Num.Kr(5) && lblPlateFeeCap.Text.Contains("1 × " + Num.Kr(5)),
                          "3d Cost breakdown row: " + lblPlateFeeCap.Text + " | " + lblPlateFee.Text);
                settings.PlateChangeFee = 0;
                Recalculate();
                log.Check(!plateFeeRowOn && !lblPlateFeeCap.Visible, "3e fee 0: no plate change row");

                // 4. the unit price setting off: no "each" on the quote / Copy text (the app's price lines keep it)
                settings.QuoteShowUnitPrice = 0;
                string tOff = QuoteText(null);
                log.Check(!tOff.Contains(History.EachPriceText(last.OthersPrice, 12)) && BuildQuote(false, when, "12", "").EachLine == "" &&
                          lblOthersInfo.Text == History.EachText(last.OthersPrice, 12), "4a unit price off: no each on the quote / Copy text");
                settings.QuoteShowUnitPrice = 1;

                // 5. saved with its copies, named "12 × …", Open again keeps the copies and the price
                EnsureHistory();
                QuoteRecord rec;
                jobModel = "Clip";
                string err = SaveToHistory(History.ForCustomer, "Batch Kunde", out rec);
                log.Check(err == null && rec != null && rec.Quantity == 12 && rec.Plates == 2 && rec.CopiesEstimated == 1,
                          "5a saved: " + (rec != null ? "Quantity " + rec.Quantity + ", Plates " + rec.Plates + ", estimated " + rec.CopiesEstimated : err));
                string rt = rec != null ? RecordText(rec, false, false) : "";
                log.Check(RecordModelName(rec).StartsWith("12 × ") && rt.Contains("Model: 12 × ") && rt.Contains(History.OnPlatesText(2)) &&
                          rt.Contains(History.EachPriceText(rec.OthersPrice, 12)), "5b record name / text: " + rt.Replace(Environment.NewLine, " | "));
                Reopen(rec);
                log.Check(jobCopies == 12 && jobPlates == 2 && !jobCopiesTyped && Math.Abs(last.OthersPrice - rec.OthersPrice) < 0.005,
                          "5c Open again: 12 copies on 2 plates, " + Num.Kr(last.OthersPrice) + " = saved " + Num.Kr(rec.OthersPrice));

                // 6. back to one copy: Clear = the old texts again
                typeJob();
                log.Check(jobCopies == 1 && !jobCopiesTyped && QuoteText(null) == QuoteRows.JobText(null, LinkSource(), 0, 38, txtGrams.Text, 0, last.FriendsPrice, last.OthersPrice),
                          "6a Clear + typed again: 1 copy, the old Copy text");
                QuoteRecord one;
                jobModel = "Hook";
                SaveToHistory(History.ForCustomer, "Batch Kunde", out one);

                // 7. combined bill with copies: "Prints: 2 models, 13 pcs · Plates: 3", card "12 pcs · 2 plates"
                var jobs = new List<QuoteRecord> { rec, one };
                var bill = History.GroupBill(jobs, settings, false);
                string gt = QuoteRows.GroupText(bill, false, "1", "Batch Kunde", when, null, true);
                log.Check(gt.Contains(History.PrintsText(2, 13)) && gt.Contains(History.PlatesCountText(3)) && gt.Contains("- 12 × Clip") && gt.Contains("- Hook"),
                          "7a combined Copy text: " + gt.Replace(Environment.NewLine, " | "));
                var gd = BuildGroupQuote(jobs, false, "Batch Kunde", false, null, when);
                log.Check(gd.GroupPlates == "3" && gd.Jobs[0].Copies.StartsWith(History.PiecesText(12)) && gd.Jobs[1].Copies == "" &&
                          gd.Jobs[0].Name.StartsWith("12 × ") && gd.Summary.StartsWith(L.S("2 models, 13 pcs", "2 modeller, 13 stk.")),
                          "7b combined page: plates " + gd.GroupPlates + ", card \"" + gd.Jobs[0].Copies + "\", summary \"" + gd.Summary + "\"");
                using (var page = QuotePage.Render(gd, false)) page.Save(Path.Combine(dir, "test_batch_group.png"), System.Drawing.Imaging.ImageFormat.Png);
                var plain = new List<QuoteRecord> { one, one };
                string gPlain = QuoteRows.GroupText(History.GroupBill(plain, settings, false), false, "1", "", when, null, true);
                log.Check(gPlain == QuoteRows.GroupText(History.GroupBill(plain, settings, false), false, "1", "", when, null) &&
                          !gPlain.Contains(History.PlatesCountText(2)) && QuoteRows.GroupPlates(plain) == "", "7c a bill without copies: no Plates line / row");

                // 8. the Copies window of a sliced job: no plates box; 300 = the "several minutes" question; a sliced file =
                // printed N times; 2 files x 300 = over 500
                typeJob();
                lastJob = new JobInputs { LocalFiles = new List<string> { @"C:\x\a.stl" }, Scalable = true };
                lastJobResult = new SliceResult { Seconds = 2280, Grams = 11.8, Plates = 1 };
                SetJobLoaded(true);
                using (var f = NewCopiesForm())
                {
                    log.Check(!f.TestHasPlates && f.TestInfoKind == InfoKind.Info, "8a sliced job: no plates box, " + f.TestInfoText.Replace("\n", " / "));
                    f.SampleType(300);
                    log.Check(f.TestApplyOn && f.TestInfoKind == InfoKind.Warning, "8b 300: warning " + f.TestInfoText);
                    f.TestBigAnswer = false;
                    bool okNo = f.TestPressApply(this);
                    log.Check(!okNo && f.TestBigAsked, "8c 300 asked first; No = the window stays");
                    f.TestBigAnswer = true;
                    bool okYes = f.TestPressApply(this);
                    log.Check(okYes && f.Copies == 300, "8d Yes = 300 copies");
                }
                lastJobResult.FromSlicedFile = true;
                using (var f = NewCopiesForm())
                {
                    f.SampleType(300);
                    log.Check(f.TestInfoKind == InfoKind.Info && f.TestInfoText.Contains(L.S("one after the other", "efter hinanden")),
                              "8e sliced file: printed N times, no question: " + f.TestInfoText);
                }
                lastJobResult.FromSlicedFile = false;
                lastJob.LocalFiles.Add(@"C:\x\b.stl");
                using (var f = NewCopiesForm())
                {
                    f.SampleType(300);
                    log.Check(!f.TestApplyOn && f.TestInfoKind == InfoKind.Error, "8f 2 files x 300 = 600: Apply off, " + f.TestInfoText);
                    f.SampleType(4);
                    log.Check(f.TestApplyOn, "8g 2 files x 4: Apply on");
                }
                ClearJob();

                // 9. the file picker's Copies boxes
                var names = new List<string> { "a.stl", "b.stl" };
                using (var p = new FilePickerForm("t", names, null, false, "Bambu PLA Basic", null))
                {
                    p.SampleChoices(new[] { "Bambu PLA Basic", "Bambu PLA Basic" }, new[] { "#000000", "#FFFFFF" }, null, false);
                    log.Check(p.TestTogetherOn, "9a 1 of each, 2 colours: \"together\" offered");
                    p.SampleCopies(new[] { 4, 2 });
                    log.Check(!p.TestTogetherOn && p.TestOkOn, "9b copies 4 + 2: \"together\" off, Slice selected on");
                    bool okP = p.TestPressOk(this);
                    log.Check(okP && p.Plan != null && p.Plan.Copies != null && p.Plan.Copies.SequenceEqual(new[] { 4, 2 }),
                              "9c Plan.Copies = " + (p.Plan != null && p.Plan.Copies != null ? string.Join(",", p.Plan.Copies) : "none"));
                }
                using (var p = new FilePickerForm("t", names, null, false, "Bambu PLA Basic", null))
                {
                    p.SampleCopies(new[] { 400, 200 });
                    log.Check(!p.TestOkOn && p.TestInfoText.Contains("500"), "9d 400 + 200: Slice selected off, " + p.TestInfoText);
                    p.SampleCopies(new[] { 150, 100 });
                    p.TestBigAnswer = false;
                    log.Check(!p.TestPressOk(this), "9e 250 in all: asked, No = not sliced");
                }
                using (var p = new FilePickerForm("t", names, null, false, "Bambu PLA Basic", null))
                {
                    p.SampleCopies(new[] { 1, 1 });
                    bool okP = p.TestPressOk(this);
                    log.Check(okP && p.Plan != null && p.Plan.Copies == null, "9f 1 of each: no copies in the plan");
                }

                // 10. a G-code printed 3 times: read (no slicer), then the Copies link's path (RecopyAsync, Multiply)
                string gcode = Path.GetFullPath(Path.Combine(Application.StartupPath, "..", "tests", "gcode-samples", "orca_a1.gcode"));
                if (!File.Exists(gcode)) log.Check(false, "10 sample G-code missing: " + gcode);
                else
                {
                    ClearJob();
                    ShowLinkFile(gcode);
                    bool done = await AnalyzeAsync(null, new List<string> { gcode }, null);
                    var one1 = lastJobResult;
                    log.Check(done && one1 != null && jobCopies == 1 && CanCopiesJob, "10a G-code read: 1 copy, Copies link offered");
                    log.Check(CopiesMode == CopiesForm.Mode.Multiply, "10b G-code: the window multiplies");
                    double s1 = one1 != null ? one1.Seconds : 0, g1 = one1 != null ? one1.Grams : 0;
                    done = await RecopyAsync(3);
                    var r3 = lastJobResult;
                    log.Check(done && r3 != null && jobCopies == 3 && jobPlates == 3 && jobCopiesMultiplied && Math.Abs(r3.Seconds - 3 * s1) < 1 && Math.Abs(r3.Grams - 3 * g1) < 0.01,
                              "10c x 3: " + jobCopies + " copies, " + jobPlates + " plates, " + (r3 != null ? Num.Fmt(r3.Grams) + " g" : "none") + " (1 copy " + Num.Fmt(g1) + " g)");
                    log.Check((lblAuto.Text ?? "").StartsWith("✓ 3 × "), "10d result line: " + (lblAuto.Text ?? "").Replace("\n", " / "));
                    log.Check(lblOthersInfo.Text == History.EachText(last.OthersPrice, 3), "10e price line: " + lblOthersInfo.Text);
                    Screenshots.Snap(this, dir, "test_batch_gcode.png");
                    done = await RecopyAsync(1);
                    log.Check(done && jobCopies == 1 && lblOthersInfo.Text == othersMarkupLine && !(lblAuto.Text ?? "").Contains("×"),
                              "10f back to 1: the markup line and the plain result line again");
                }
                // 11. two files with their own copies (4 + 10), sliced by Bambu Studio (online's tiny test blocks): saved per
                // file, then Repeat print slices each file with its own count again, and the plan rebuilt from the record has them
                string onlineFiles = BatchTestFiles();
                var f2 = new List<string> { Path.Combine(onlineFiles, "red_block.stl"), Path.Combine(onlineFiles, "blue_block.stl") };
                if (!f2.All(File.Exists)) log.Check(false, "11 test blocks missing in " + onlineFiles);
                else
                {
                    ClearJob();
                    var parts2 = new List<HistoryPart>
                    {
                        new HistoryPart { File = f2[0], Filament = "Bambu PLA Basic", Colour = "#C12E1F", Copies = 4 },
                        new HistoryPart { File = f2[1], Filament = "Bambu PLA Basic", Colour = "#0A2989", Copies = 10 },
                    };
                    txtUrl.Text = L.F("{0} files: ", "{0} filer: ", 2) + string.Join(", ", f2.Select(Path.GetFileName));
                    bool done = await AnalyzeAsync(null, f2, null, parts2, false);
                    log.Check(done && jobCopies == 14 && jobFileCopies != null && jobFileCopies.SequenceEqual(new[] { 4, 10 }),
                              "11a 2 files sliced with 4 + 10: " + jobCopies + " copies (" + (jobFileCopies != null ? string.Join(" + ", jobFileCopies) : "none") + "), " +
                              jobPlates + " plate(s)" + (done ? "" : ": " + (lblAuto.Text ?? "").Replace("\n", " / ")));
                    QuoteRecord rb = null;
                    if (done)
                    {
                        jobModel = "Blocks";
                        string e2 = SaveToHistory(History.ForCustomer, "Batch Kunde", out rb);
                        var pc = rb != null ? History.PartCopies(rb) : null;
                        var items = rb != null ? History.BatchItems(rb) : null;
                        log.Check(e2 == null && rb != null && rb.Quantity == 14 && pc != null && pc.SequenceEqual(new[] { 4, 10 }) &&
                                  items != null && items.Select(i => i.Copies).SequenceEqual(new[] { 4, 10 }),
                                  "11b saved: Quantity " + (rb != null ? rb.Quantity : 0) + ", per file " + (pc != null ? string.Join(" + ", pc) : "none") +
                                  ", BatchItems " + (items != null ? string.Join(" + ", items.Select(i => i.Name + " x" + i.Copies)) : "none"));
                        var rebuilt = ColourPlan.Build(History.PartsOf(rb), false);
                        log.Check(rebuilt.Copies != null && rebuilt.Copies.SequenceEqual(new[] { 4, 10 }), "11c plan from the record: copies " +
                                  (rebuilt.Copies != null ? string.Join(" + ", rebuilt.Copies) : "none"));
                    }
                    if (rb != null)
                    {
                        ClearJob();
                        Reopen(rb);
                        log.Check(jobCopies == 14 && jobFileCopies != null && jobFileCopies.SequenceEqual(new[] { 4, 10 }),
                                  "11d Open again: " + jobCopies + " copies (" + (jobFileCopies != null ? string.Join(" + ", jobFileCopies) : "none") + ")");
                        ClearJob();
                        await RepeatAsync(rb);
                        log.Check(lastJobResult != null && jobCopies == 14 && jobFileCopies != null && jobFileCopies.SequenceEqual(new[] { 4, 10 }) && repeatOf == rb,
                                  "11e Repeat print: sliced again with " + jobCopies + " copies (" + (jobFileCopies != null ? string.Join(" + ", jobFileCopies) : "none") + "), " +
                                  jobPlates + " plate(s): " + (lblAuto.Text ?? "").Replace("\n", " / "));
                        QuoteRecord rr;
                        string e3 = SaveToHistory(History.ForCustomer, "Batch Kunde", out rr);
                        var pc2 = rr != null ? History.PartCopies(rr) : null;
                        log.Check(e3 == null && rr != null && rr.Quantity == 14 && pc2 != null && pc2.SequenceEqual(new[] { 4, 10 }),
                                  "11f the repeat saved: Quantity " + (rr != null ? rr.Quantity : 0) + ", per file " + (pc2 != null ? string.Join(" + ", pc2) : "none"));
                        Screenshots.Snap(this, dir, "test_batch_two_files.png");
                    }
                }
                await TestBatch2(log, dir, when, typeJob);
                settings.PlateChangeFee = keepFee; settings.QuoteShowUnitPrice = keepUnit;
            }
            catch (Exception ex) { log.Check(false, "exception: " + ex); }
            try { File.WriteAllLines(Path.Combine(dir, "test_batch.txt"), log.Lines); } catch { }
            Program.EndTestRun(log.Ok ? 0 : 4);
        }

        /// The tests' model files (online's tiny blocks, cube.step): plans\batch-plates\online\files, or its plans\_old copy.
        static string BatchTestFiles()
        {
            string root = Path.GetFullPath(Path.Combine(Application.StartupPath, "..", "plans"));
            string a = Path.Combine(root, "batch-plates", "online", "files"), b = Path.Combine(root, "_old", "batch-plates", "online", "files");
            return Directory.Exists(a) ? a : b;
        }

        /// batch-2 part of --test-batch (sections 12-15): the quantity discount (price lines, tooltip, quote rows, Copy text,
        /// saved record, off = as before), plates printed in History (status menu +1 / –1 saved, "3 of 5 plates", "Printing
        /// plate 4 of 5", the selection line, the Invoices row's totals), a REAL two-colour batch with sharing the last plate
        /// (Bambu Studio: 1 shared plate, the ✓ line, notes, plates menu dots, saved choice, Copies window) and its fallback.
        async Task TestBatch2(TestLog log, string dir, DateTime when, Action typeJob)
        {
            double[] keep = { settings.QtyDiscountFrom1, settings.QtyDiscountPct1, settings.QtyDiscountFrom2, settings.QtyDiscountPct2,
                              settings.QtyDiscountFrom3, settings.QtyDiscountPct3, settings.QtyDiscountWho, settings.ShareLastPlate };
            try
            {
                // 12. quantity discount: from 10 pcs 5 %, from 50 pcs 10 %, customers only
                typeJob();
                settings.QtyDiscountFrom1 = 10; settings.QtyDiscountPct1 = 5;
                settings.QtyDiscountFrom2 = 50; settings.QtyDiscountPct2 = 10;
                settings.QtyDiscountFrom3 = 0; settings.QtyDiscountPct3 = 0;
                settings.QtyDiscountWho = 2;
                SetTypedCopies(40, 2);
                log.Check(last.DiscountPct == 5 && last.DiscountFrom == 10 && last.OthersDiscount > 0 && last.FriendsDiscount == 0,
                          "12a 40 pcs: " + Num.Fmt(last.DiscountPct) + " % from " + last.DiscountFrom + ", customer -" + Num.Kr(last.OthersDiscount) +
                          ", friends -" + Num.Kr(last.FriendsDiscount));
                log.Check(lblOthersInfo.Text == History.EachText(last.OthersPrice, 40) + " · " + History.DiscountShortText(5) &&
                          lblFriendsInfo.Text == History.EachText(last.FriendsPrice, 40),
                          "12b price lines: \"" + lblFriendsInfo.Text + "\" / \"" + lblOthersInfo.Text + "\"");
                string tip = printerTip.GetToolTip(lblOthersInfo) ?? "";
                log.Check(tip.Contains(L.F("Before discount: {0}", "Før rabat: {0}", Num.Kr(last.OthersPrice + last.OthersDiscount))) &&
                          tip.Contains("–" + Num.Kr(last.OthersDiscount)) && !(printerTip.GetToolTip(lblFriendsInfo) ?? "").Contains("–"),
                          "12c tooltip: " + tip.Replace("\n", " / "));
                var b = Pricing.Breakdown(lastInput, settings, false, null);
                var rows = QuoteRows.Build(b, null, -1, lastInput.Hours, lastInput.Minutes, "", 40);
                var sub = rows.FirstOrDefault(r => r.Subtotal);
                var disc = rows.FirstOrDefault(r => r.Label == History.DiscountText(5, 40));
                var dl = b.Lines.First(l => l.Key == QuoteLine.Discount);
                long beforeOre = 0;
                foreach (var l in QuoteRows.FoldMinimum(b.Lines)) { if (l.Key == QuoteLine.Discount) break; beforeOre += l.Ore; }
                log.Check(sub != null && sub.Label == History.PriceBeforeDiscountText() && sub.Amount == Num.Kr(beforeOre / 100.0) &&
                          disc != null && disc.Amount == "–" + Num.Kr(-dl.Ore / 100.0) && rows.IndexOf(disc) == rows.IndexOf(sub) + 2,
                          "12d quote rows: " + (sub != null ? sub.Label + " " + sub.Amount : "no subtotal") + " | " + (disc != null ? disc.Label + " " + disc.Amount : "no discount row"));
                string ct = QuoteText(null);
                log.Check(ct.Contains(Num.Kr(last.OthersPrice) + " " + History.EachInclDiscountText(last.OthersPrice, 40, last, false)) &&
                          ct.Contains(Num.Kr(last.FriendsPrice) + " (" + History.EachPriceText(last.FriendsPrice, 40) + ")"),
                          "12e Copy text: " + ct.Replace(Environment.NewLine, " | "));
                var dq = BuildQuote(false, when, "40", "Rabat Kunde");
                log.Check(dq.EachLine == History.EachText(dq.Breakdown.Total, 40), "12f quote page each line after the discount: " + dq.EachLine);
                using (var page = QuotePage.Render(dq, false)) page.Save(Path.Combine(dir, "test_batch2_discount_quote.png"), System.Drawing.Imaging.ImageFormat.Png);
                QuoteRecord dr;
                jobModel = "Rabatklips";
                string e1 = SaveToHistory(History.ForCustomer, "Rabat Kunde", out dr);
                string drt = dr != null ? RecordText(dr, false, false) : "";
                log.Check(e1 == null && dr != null && dr.QtyDiscountPct == 5 && dr.QtyDiscountFrom == 10 && drt.Contains(L.S("quantity discount", "mængderabat")),
                          "12g saved with 5 %: " + drt.Replace(Environment.NewLine, " | "));
                SetTypedCopies(60, 2);
                log.Check(last.DiscountPct == 10 && lblOthersInfo.Text.EndsWith(History.DiscountShortText(10)), "12h 60 pcs: the 10 % step, " + lblOthersInfo.Text);
                settings.QtyDiscountWho = 1;
                Recalculate();
                log.Check(last.FriendsDiscount > 0 && lblFriendsInfo.Text.EndsWith(History.DiscountShortText(10)), "12i both prices: friends -" + Num.Kr(last.FriendsDiscount));
                // off again: no discount anywhere, the texts as before
                settings.QtyDiscountFrom1 = settings.QtyDiscountPct1 = settings.QtyDiscountFrom2 = settings.QtyDiscountPct2 = 0;
                settings.QtyDiscountWho = 2;
                Recalculate();
                var b0 = Pricing.Breakdown(lastInput, settings, false, null);
                log.Check(last.DiscountPct == 0 && lblOthersInfo.Text == History.EachText(last.OthersPrice, 60) &&
                          !b0.Lines.Any(l => l.Key == QuoteLine.Discount) &&
                          !QuoteRows.Build(b0, null, -1, lastInput.Hours, lastInput.Minutes, "", 60).Any(r => r.Subtotal) &&
                          QuoteText(null) == QuoteRows.JobText(null, LinkSource(), lastInput.Hours, lastInput.Minutes, Num.Fmt(lastInput.FilamentGrams), 0,
                                                               last.FriendsPrice, last.OthersPrice, 60, 2, true),
                          "12j discount off: no row, no mark, the old Copy text");
                Screenshots.Snap(this, dir, "test_batch2_discount.png");

                // 13. plates printed: a job of 10 copies on 5 plates in History; the real status menu +1 / –1, saved at once
                typeJob();
                SetTypedCopies(10, 5);
                QuoteRecord p5;
                jobModel = "Plader5";
                SaveToHistory(History.ForCustomer, "Plade Kunde", out p5);
                typeJob();
                SetTypedCopies(4, 2);
                QuoteRecord p2;
                jobModel = "Plader2";
                SaveToHistory(History.ForCustomer, "Plade Kunde", out p2);
                if (p5 == null || p2 == null) log.Check(false, "13 saving the test jobs failed");
                else
                {
                    var hist = new HistoryForm(history, null, false) { Settings = settings };
                    try
                    {
                        hist.Show();
                        Application.DoEvents();
                        hist.TestSelect(new List<QuoteRecord> { p5 });
                        string menu = hist.TestStatusMenuTexts(p5);
                        log.Check(menu.Contains("-" + History.PlatesPrintedText(0, 5)) && menu.Contains("+" + History.PlusPlateText()) &&
                                  menu.Contains("-" + History.MinusPlateText()), "13a status menu: " + menu);
                        bool c1 = hist.TestStatusMenuClick(p5, History.PlusPlateText()), c2 = hist.TestStatusMenuClick(p5, History.PlusPlateText()),
                             c3 = hist.TestStatusMenuClick(p5, History.PlusPlateText());
                        DateTime now = DateTime.Now;
                        log.Check(c1 && c2 && c3 && p5.PlatesDone == 3 && History.PrintStateOf(p5, now) == PrintState.Printing &&
                                  HistoryForm.PlatesStatusText(p5, now) == History.PlatesProgressText(3, 5) &&
                                  hist.TestInfo == "✓ " + History.PlatesPrintedText(3, 5) + ".",
                                  "13b +1 x 3: " + p5.PlatesDone + " done, cell \"" + HistoryForm.PlatesStatusText(p5, now) + "\", line \"" + hist.TestInfo + "\"");
                        string problem;
                        var disk = History.Load(out problem);
                        var p5d = disk != null ? disk.FirstOrDefault(x => x.Id == p5.Id) : null;
                        log.Check(p5d != null && p5d.PlatesDone == 3, "13c saved at once: history.json has " + (p5d != null ? p5d.PlatesDone.ToString() : "no record"));
                        bool c4 = hist.TestStatusMenuClick(p5, History.MinusPlateText());
                        disk = History.Load(out problem);
                        p5d = disk != null ? disk.FirstOrDefault(x => x.Id == p5.Id) : null;
                        log.Check(c4 && p5.PlatesDone == 2 && p5d != null && p5d.PlatesDone == 2, "13d –1: " + p5.PlatesDone + " (saved " + (p5d != null ? p5d.PlatesDone : -9) + ")");
                        hist.SetStatus(new List<QuoteRecord> { p5 }, PrintState.Printing);
                        string stip = HistoryForm.StatusTip(p5) ?? "";
                        log.Check(History.IsPrinting(p5, DateTime.Now) && stip.StartsWith(History.PrintingPlateText(3, 5)), "13e printing now: \"" + stip + "\"");
                        hist.TestSelect(new List<QuoteRecord> { p5, p2 });
                        var prog = History.Progress(new[] { p5, p2 }, DateTime.Now);
                        log.Check(hist.TestInfo == History.SelectedProgressText(2, prog.PlatesDone, 7), "13f 2 selected: \"" + hist.TestInfo + "\"");
                        hist.SetStatus(new List<QuoteRecord> { p5 }, PrintState.Printed);
                        log.Check(p5.PlatesDone == 5 && History.PrintStateOf(p5, DateTime.Now) == PrintState.Printed && HistoryForm.PlatesStatusText(p5, DateTime.Now) == null,
                                  "13g Printed = all 5 plates, the cell says Printed");
                        hist.TestSelect(new List<QuoteRecord> { p5 });
                        log.Check(!hist.TestStatusMenuTexts(p5).Contains("+" + History.PlusPlateText()), "13h all printed: +1 is off");
                        Screenshots.Snap(hist, dir, "test_batch2_history.png");
                    }
                    finally { hist.Close(); hist.Dispose(); }
                }

                // 14. a REAL two-colour batch with "share the last plate" on (Bambu Studio): red block x 4 + blue block x 10 each
                // fit one plate, so the two part-full last plates become ONE shared plate with both colours
                string files = BatchTestFiles();
                var blocks = new List<string> { Path.Combine(files, "red_block.stl"), Path.Combine(files, "blue_block.stl") };
                if (!blocks.All(File.Exists)) log.Check(false, "14 test blocks missing in " + files);
                else
                {
                    ClearJob();
                    var parts = new List<HistoryPart>
                    {
                        new HistoryPart { File = blocks[0], Filament = "Bambu PLA Basic", Colour = "#C12E1F", Copies = 4 },
                        new HistoryPart { File = blocks[1], Filament = "Bambu PLA Basic", Colour = "#0A2989", Copies = 10 },
                    };
                    txtUrl.Text = L.F("{0} files: ", "{0} filer: ", 2) + string.Join(", ", blocks.Select(Path.GetFileName));
                    shareForNextSlice = true;
                    bool done = await AnalyzeAsync(null, blocks, null, parts, false);
                    var res = lastJobResult;
                    var shared = res != null ? res.PlateList.FirstOrDefault(p => p.Shared) : null;
                    log.Add("INFO 14 result: " + (lblAuto.Text ?? "").Replace("\n", " / ") + " | plates " + (res != null ? res.PlateList.Count : 0) +
                            (res != null && res.Share != null ? " | share applied " + res.Share.Applied + ", saved " + res.Share.PlatesSaved + ", +" +
                             Math.Round(res.Share.ExtraSeconds) + " s, +" + Num.Fmt(Math.Round(res.Share.ExtraGrams, 2)) + " g, changes " + res.Share.ColourChanges +
                             ", problem " + (res.Share.Problem ?? "-") : " | no share info"));
                    log.Check(done && res != null && res.Share != null && res.Share.Applied && res.Share.PlatesSaved == 1 && jobCopies == 14 && jobShare,
                              "14a sliced with sharing: applied, 1 plate saved, 14 copies");
                    log.Check(shared != null && res.PlateList.Count == 1 && BambuSlicer.IsMixedPlate(shared) && BambuSlicer.PlateColourCount(shared) == 2 &&
                              shared.Copies == 14, "14b one shared plate with both colours and 14 copies: " +
                              (shared != null ? PlateMenuLine(shared, false) + ", colours " + BambuSlicer.PlateColourCount(shared) : "none"));
                    string line = lblAuto.Text ?? "";
                    log.Check(line.Contains(SharedPlatesText(1).Replace(' ', ' ')) &&
                              res != null && res.Share != null && lblAuto.MutedLine.Contains(ShareNoteText(res.Share)) && !autoWarning,
                              "14c ✓ line + sharing note: " + line.Replace("\n", " / ") + " / " + lblAuto.MutedLine);
                    if (res != null && res.PlateList.Count > 0)
                    {
                        using (var m = PlatesMenu(res.PlateList, null))
                        {
                            var item = m.Items.OfType<ToolStripMenuItem>().LastOrDefault();
                            log.Check(m.ShowImageMargin && item != null && item.Image != null &&
                                      (shared == null || shared.ColourChanges <= 0 || item.Text.Contains(L.S("colour change", "farveskift"))),
                                      "14d plates menu: colour dots" + (item != null ? ", \"" + item.Text + "\"" : ""));
                        }
                    }
                    if (done)
                    {
                        QuoteRecord sr;
                        jobModel = "Delte klodser";
                        string e5 = SaveToHistory(History.ForCustomer, "Plade Kunde", out sr);
                        log.Check(e5 == null && sr != null && sr.Parts != null && sr.Parts.ShareLastPlate == 1 && History.ShareLastPlateFor(sr, settings),
                                  "14e saved with the share choice: " + (sr != null && sr.Parts != null ? sr.Parts.ShareLastPlate.ToString() : "no parts"));
                        using (var f = NewCopiesForm())
                            log.Check(f.TestHasShare && f.TestInfoMuted == ShareNoteText(res.Share), "14f Copies window: share tick, note \"" + f.TestInfoMuted + "\"");
                        Screenshots.Snap(this, dir, "test_batch2_shared.png");
                        if (sr != null)
                        {
                            ClearJob();
                            Reopen(sr);
                            log.Check(jobShare, "14g Open again keeps the share choice");
                        }
                    }
                    // 15. the fallback: two 150 x 150 mm plates (big1.stl, one per plate) in two colours: their last plates don't fit
                    // on one plate together, so each colour keeps its own plates (warning note, the reason in the tooltip). The
                    // second colour is a copy of big1.stl in this test's own folder.
                    string big = Path.Combine(files, "big1.stl"), bigBlue = Path.Combine(dir, "big1_blue.stl");
                    if (!File.Exists(big)) log.Check(false, "15 big1.stl missing in " + files);
                    else
                    {
                        File.Copy(big, bigBlue, true);
                        ClearJob();
                        var two = new List<string> { big, bigBlue };
                        var parts2 = new List<HistoryPart>
                        {
                            new HistoryPart { File = two[0], Filament = "Bambu PLA Basic", Colour = "#C12E1F", Copies = 2 },
                            new HistoryPart { File = two[1], Filament = "Bambu PLA Basic", Colour = "#0A2989", Copies = 2 },
                        };
                        txtUrl.Text = L.F("{0} files: ", "{0} filer: ", 2) + string.Join(", ", two.Select(Path.GetFileName));
                        shareForNextSlice = true;
                        bool done2 = await AnalyzeAsync(null, two, null, parts2, false);
                        var r2 = lastJobResult;
                        log.Add("INFO 15 result: " + (lblAuto.Text ?? "").Replace("\n", " / ") + " | plates " + (r2 != null ? r2.PlateList.Count : 0) +
                                " | problem " + (r2 != null && r2.Share != null ? r2.Share.Problem ?? "-" : "no share info"));
                        log.Check(done2 && r2 != null && r2.Share != null && !r2.Share.Applied && !string.IsNullOrEmpty(r2.Share.Problem) &&
                                  r2.PlateList.Count == 4 && autoWarning &&
                                  lblAuto.MutedLine.Contains(L.S("Each colour kept its own plates.", "Hver farve beholdt sine egne plader.")),
                                  "15a fell back: own plates, warning note");
                        string ntip = lblAuto.NoteControl != null ? printerTip.GetToolTip(lblAuto.NoteControl) ?? "" : "";
                        log.Check(r2 != null && r2.Share != null && ntip.Replace("\n", " ") == WrapTip(r2.Share.Problem, 45).Replace("\n", " "),
                                  "15b the reason in the note's tooltip: " + ntip.Replace("\n", " "));
                        Screenshots.Snap(this, dir, "test_batch2_fallback.png");
                    }
                }
                // 16. compact mode: a made-up working area 40 px lower than the window -> compact, smaller, nothing cut off;
                // back off = the same height as before
                int before = Height;
                string normalFit = FitReport("16 normal");
                log.Add("INFO " + normalFit);   // the credits panel's "GitHub" link reports 1-2 px over its panel in both modes (not visible)
                Func<string, string> cutOf = s => s.Contains("CUT:") ? s.Substring(s.IndexOf("CUT:")) : "";
                TestWorkHeight = (int)Math.Round((Height - ScalePx(40)) * 96f / DeviceDpi);
                SampleCheckFit();
                string fit = FitReport("16 compact");
                log.Check(compact && Height < before && Height <= ScalePx(TestWorkHeight) && cutOf(fit) == cutOf(normalFit) && lblBasePower.Visible && !lblPower.Visible,
                          fit + " (was " + before + " px)");
                Screenshots.Snap(this, dir, "test_batch2_compact.png");
                TestWorkHeight = 0;
                SetCompact(false);
                log.Check(!compact && Height == before, "16b compact off again: " + Height + " px");
            }
            catch (Exception ex) { log.Check(false, "batch-2 exception: " + ex); }
            finally
            {
                shareForNextSlice = null;
                settings.QtyDiscountFrom1 = keep[0]; settings.QtyDiscountPct1 = keep[1]; settings.QtyDiscountFrom2 = keep[2];
                settings.QtyDiscountPct2 = keep[3]; settings.QtyDiscountFrom3 = keep[4]; settings.QtyDiscountPct3 = keep[5];
                settings.QtyDiscountWho = keep[6]; settings.ShareLastPlate = keep[7];
            }
        }

        /// Picture mode: draws the plates menu (never shown on screen there) under its link, as it opens.
        internal void SamplePlatesMenu(Graphics g, Point origin)
        {
            var at = lblAuto.LinkControl;
            if (at == null || shownPlates == null) return;
            using (var menu = PlatesMenu(shownPlates, null))
            {
                menu.PerformLayout();
                var size = menu.GetPreferredSize(Size.Empty);
                menu.Size = size;
                using (var bmp = new Bitmap(size.Width, size.Height))
                {
                    menu.DrawToBitmap(bmp, new Rectangle(Point.Empty, size));
                    var p = at.PointToScreen(new Point(0, at.Height));
                    int x = p.X - origin.X, y = p.Y - origin.Y;
                    g.DrawImage(bmp, x, y);
                    // the dark object-names tooltip of the second plate, where it shows beside its line
                    int k = shownPlates.Count > 1 ? 1 : 0;
                    var item = menu.Items[2 + k];
                    var objs = PlateObjectNames(shownPlates[k]);
                    if (objs.Count == 0) return;
                    string text = string.Join("\n", objs);
                    var font = SystemFonts.StatusFont;
                    var ts = TextRenderer.MeasureText(text, font);
                    var box = new Rectangle(x + item.Bounds.Right + 4, y + item.Bounds.Top, ts.Width + 12, ts.Height + 8);
                    // batch-2 qol #11: a wide menu leaves no room beside it in the picture: no cut-off tooltip then
                    if (box.Right > g.VisibleClipBounds.Right) return;
                    using (var b = new SolidBrush(Theme.InfoBack)) g.FillRectangle(b, box);
                    using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
                    TextRenderer.DrawText(g, text, font, box, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }

        /// Picture mode: the MakerWorld message for a made-up link.
        internal void SampleMakerWorld()
        {
            if (!offline) return;
            ClearJobNumbers();
            ResetModelPicture();
            txtUrl.Text = "https://makerworld.com/en/models/123456-sample-model";
            ShowMakerWorld(MakerWorldPage(txtUrl.Text));
            Recalculate();
        }

        void ShowMakerWorld(string page)
        {
            // MakerWorld needs a sign-in to download (idea 21): say how, with a link that opens the page
            SetAuto(L.S("MakerWorld needs you to sign in to download. Open the page, click Download (3MF), then drop the file here or use Load file… – colours and plates are read automatically.",
                        "MakerWorld kræver, at du logger ind for at hente. Åbn siden, klik Download (3MF), og træk filen herind eller brug Åbn fil… – farver og plader læses automatisk."), false);
            lblAuto.SetLink(L.S("Open the page", "Åbn siden"), () => { if (!offline) try { Process.Start(page); } catch { } });
        }

        /// Picture mode: reads a sliced file (e.g. a sample .gcode) as a dropped one would be, without slicing or saving.
        internal void SampleSlicedFile(string path)
        {
            if (!offline) return;
            var r = BambuSlicer.ReadSliceInfo(path, settings.FilamentPreset);
            r.FromSlicedFile = true;
            ShowLinkFile(path);
            ResetModelPicture();
            if (r.Picture != null) try { SetModelPicture(PrepareModelPicture(r.Picture)); } catch { }
            ShowJobResult(r, null, new List<string> { path }, 1, settings.ProcessPreset, settings.FilamentPreset, null);
            Recalculate();
        }

        /// Picture mode: shows a reading error as AnalyzeAsync would.
        internal void SampleError(string text)
        {
            if (!offline) return;
            ClearJobNumbers();
            ResetModelPicture();
            SetAutoError(text);
            Recalculate();
        }

        /// Where slicing jobs work: %TEMP%\PrintPrice, or in test mode `<test folder>\temp` (never the user's temp files).
        static string JobTempRoot()
        {
            return AppSettings.TestMode ? Path.Combine(AppSettings.Folder, "temp") : Path.Combine(Path.GetTempPath(), "PrintPrice");
        }

        /// Test mode's KeepForStudio: copies the slice to `<test folder>\temp\studio\<guid>\<name>.3mf` (BambuSlicer.StudioRoot
        /// and its clean-up are the user's %TEMP%); null on failure.
        static string KeepForStudioTest(string sliced, string modelName)
        {
            try
            {
                if (string.IsNullOrEmpty(sliced) || !File.Exists(sliced)) return null;
                string dir = Path.Combine(JobTempRoot(), "studio", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string stem = Path.GetFileNameWithoutExtension(Printables.SafeFileName(modelName ?? "model"));
                string target = Path.Combine(dir, (stem.Length > 0 ? stem : "model") + ".3mf");
                File.Copy(sliced, target, true);
                return target;
            }
            catch { return null; }
        }

        /// The file picker (idea 21: a filament and colour per file); null = cancelled.
        FilePickerForm PickFiles(string title, List<string> names, List<bool> ticked, bool printables)
        {
            var picker = new FilePickerForm(title, names, ticked, printables, BambuSlicer.ShortName(settings.FilamentPreset), FilamentNames);
            picker.ShareDefault = SettingsShare;   // batch-2: the share tick starts as Settings say
            using (picker)
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return null;
                return picker;   // read after Dispose: only its plain fields (SelectedIndexes, Plan)
            }
        }

        /// plan (idea 21) = several files with a filament / colour each (BambuSlicer.SliceColours, also "together on one
        /// plate"); null = one file, or files that keep their own colours (sliced one by one, summed).
        static SliceResult RunJob(string exe, string printId, List<RemoteFile> remote, List<string> local, string printer,
                                  string nozzle, string process, string filament, string bed, ColourPlan plan,
                                  IProgress<string> progress, CancellationToken ct, ScaleXyz scalePct = null, bool primeTower = true,
                                  IList<int> copies = null, bool shareLastPlate = false)
        {
            if (scalePct == null) scalePct = ScaleXyz.None;
            // batch & plates: copies per file (null = 1 each); any 2+ = BambuSlicer.SliceBatch
            bool batch = copies != null && copies.Any(c => c > 1);
            string work = Path.Combine(JobTempRoot(), Guid.NewGuid().ToString("N"));
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
                        Printables.Download(printId, remote[i], target, ct);
                        models.Add(new KeyValuePair<string, string>(remote[i].Name, target));
                    }
                }
                else
                {
                    foreach (var f in local) models.Add(new KeyValuePair<string, string>(Path.GetFileName(f), f));
                }

                if (models.Count == 1 && BambuSlicer.ReadAsSliced(models[0].Value, scalePct))   // issue #8: a scaled sliced .3mf slices again
                {
                    var r = BambuSlicer.ReadSliceInfo(models[0].Value, filament);
                    r.FromSlicedFile = true;
                    if (batch)
                    {
                        // batch & plates: a sliced file / G-code can't be re-arranged: the file printed N times
                        r = BambuSlicer.RepeatResult(r, copies[0]);
                        r.FromSlicedFile = true;
                    }
                    return r;
                }

                ct.ThrowIfCancellationRequested();
                string label = Wear.FindModel(printer).Label;   // "Bambu Lab A1 mini"
                progress.Report(L.F("Preparing {0} profiles…", "Klargør {0}-profiler…", label));
                BambuSlicer.PrepareProfiles(exe, printer, nozzle, process, filament, bed, work, primeTower);   // issue #8 step 3

                SliceResult total;
                if (batch)
                {
                    // batch & plates: every file with its copies; as many as fit on each plate (each colour its own plates)
                    var items = new List<BatchItem>();
                    for (int i = 0; i < models.Count; i++)
                        items.Add(new BatchItem { Name = models[i].Key, Path = models[i].Value, Copies = i < copies.Count ? Math.Max(1, copies[i]) : 1,
                                                  Slot = plan != null && i < plan.Slots.Count ? Math.Max(1, plan.Slots[i]) : 1 });
                    total = BambuSlicer.SliceBatch(exe, items, plan != null ? plan.Colours : null, work, printer, nozzle,
                                                   BambuSlicer.ShortName(filament), progress, ct, scalePct, shareLastPlate);   // batch-2
                    if (plan != null)
                    {
                        foreach (var f in total.Filaments)
                        {
                            if (f == null || f.Slot < 1 || f.Slot > plan.Colours.Count) continue;
                            var c = plan.Colours[f.Slot - 1];
                            f.Profile = string.IsNullOrEmpty(c.Filament) ? BambuSlicer.ShortName(filament) : c.Filament;
                            if (!string.IsNullOrEmpty(c.Colour)) f.Colour = c.Colour;
                        }
                        // batch-2: the plates' colour dots in the user's colours too (the plates menu)
                        foreach (var p in total.PlateList)
                            if (p != null && p.Filaments != null)
                                foreach (var f in p.Filaments)
                                    if (f != null && f.Slot >= 1 && f.Slot <= plan.Colours.Count && !string.IsNullOrEmpty(plan.Colours[f.Slot - 1].Colour))
                                        f.Colour = plan.Colours[f.Slot - 1].Colour;
                    }
                }
                else if (plan != null)
                {
                    total = BambuSlicer.SliceColours(exe, models, plan.Slots, plan.Colours, plan.Together, work, printer, nozzle,
                                                     BambuSlicer.ShortName(filament), progress, ct, scalePct);
                    // the user's colours: the material and colour they picked (a file with its own colours keeps them)
                    foreach (var f in total.Filaments)
                    {
                        if (f == null || f.Slot < 1 || f.Slot > plan.Colours.Count) continue;
                        var c = plan.Colours[f.Slot - 1];
                        f.Profile = string.IsNullOrEmpty(c.Filament) ? BambuSlicer.ShortName(filament) : c.Filament;
                        if (!string.IsNullOrEmpty(c.Colour)) f.Colour = c.Colour;
                    }
                }
                else
                {
                    total = new SliceResult();
                    for (int i = 0; i < models.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(L.S("Slicing ", "Slicer ") + models[i].Key + (models.Count > 1 ? " (" + (i + 1) + "/" + models.Count + ")" : "") +
                                        L.S(" in Bambu Studio…", " i Bambu Studio…"));
                        var r = BambuSlicer.SliceFile(exe, models[i].Value, work, printer, nozzle, filament, ct, scalePct);
                        // time, grams, plates (with the file's name), filaments per slot, the first picture and Studio file
                        BambuSlicer.AddResult(total, r, models.Count > 1 ? models[i].Key : "");
                    }
                }
                // copy the slice out of the work folder before the finally below deletes it (null = nothing to open)
                total.StudioFile = AppSettings.TestMode ? KeepForStudioTest(total.StudioFile, models[0].Key)
                                                        : BambuSlicer.KeepForStudio(total.StudioFile, models[0].Key);
                return total;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        /// Empty link box + Enter puts this in (the 4-colour Benchy: shows the multi-colour rows).
        const string ExampleLink = "https://www.printables.com/model/1648230-four-color-multicolor-test-benchy";
        /// true while the link box holds the example put there by Enter (cleared when the text changes).
        bool fromExample;

        /// The example's 4-colour Benchy file (Printables file id 6930756), so the example skips the file picker; null = not found (picker as usual).
        static RemoteFile ExampleFile(List<RemoteFile> files)
        {
            return files.FirstOrDefault(f => f.Id == "6930756") ??
                   files.FirstOrDefault(f => string.Equals(f.Name, "Multicolor_Benchy.3mf", StringComparison.OrdinalIgnoreCase));
        }
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
        /// The Task ends when the picture is shown (or dropped); AnalyzeAsync awaits the slice render so the quote saved right
        /// after the job has its picture (idea 29); the other callers don't wait (never throws).
        async Task LoadModelPicture(Func<byte[]> source, bool replace)
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
            SetSamplePicture(file, null);
        }

        /// Picture mode: the sample record pictures (HistoryForm.SamplePictures: assets\history-samples), by record Id. Used when
        /// a sample picture file is missing and by StoredPicture (saved records' pictures; the disk is never read in picture mode).
        internal Dictionary<string, byte[]> SamplePictures;

        /// As above; no file = the sample picture fallbackId (SamplePictures), else back to the logo.
        internal void SetSamplePicture(string file, string fallbackId)
        {
            byte[] bytes = null;
            try { if (File.Exists(file)) bytes = File.ReadAllBytes(file); } catch { }
            if (bytes == null && fallbackId != null && SamplePictures != null) SamplePictures.TryGetValue(fallbackId, out bytes);
            if (bytes == null) { ResetModelPicture(); return; }
            try { SetModelPicture(PrepareModelPicture(bytes)); } catch { }
        }

        /// Decodes the picture, cuts off transparent margins (slicer renders have lots) and scales it down to at most 384 px.
        internal static Bitmap PrepareModelPicture(byte[] bytes)
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
                    var state = g.Save();   // the half-pixel offset must not reach the border below (it moved it 1 px up-left)
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    using (var ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);   // no faint seam at the picture's edges
                        g.DrawImage(pic, new Rectangle(m + (aw - w) / 2, m + (ah - h) / 2, w, h), 0, 0, pic.Width, pic.Height, GraphicsUnit.Pixel, ia);
                    }
                    g.Restore(state);
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
        internal static Bitmap Logo   // also the setup window's welcome picture
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
                unit.Margin = new Padding(3, 0, 10, 0);   // text 6 px after the kr/kg box (measured: 2 gave 5)
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
            jobTowerGrams = r != null ? r.TowerGrams : -1;       // issue #8 step 3 (one colour: 0 = sliced, no tower)
            jobTowerSeconds = r != null ? r.TowerSeconds : -1;
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
            string note = multi ? PurgeNote(r, PurgeLineWidth()) : "";
            lblPurge.Text = note;
            lblPurge.Visible = note.Length > 0 && !compact;
            UpdatePurgeTip();
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
        int BreakRowHeight()
        {
            return compact ? Math.Max(ScalePx(24), rowPrice[0].Height + ScalePx(1))   // compact: the boxes just fit
                           : Math.Max(ScalePx(28), rowPrice[0].Height + ScalePx(3));
        }

        // ---------- batch-2: compact mode (only when the window doesn't fit the screen) ----------

        bool compact;                 // on: rows 24 px, Power folded into Base cost, colour-change note in a tooltip, lower price boxes, smaller gaps
        bool fitting;                 // CheckFit running (its size change must not call it again)
        Label powerCap, lblBasePower; // the Power row's caption; "incl. power 3,91 kr." after "Base cost" (compact only)
        Control[] compactParts;       // inputs table, Cost breakdown, prices table, the two price boxes
        /// Tests / picture mode: a made-up working-area height (96-dpi px) instead of the screen's; 0 = the real screen
        /// (picture mode and hidden test runs never look at the real screen).
        internal static int TestWorkHeight;

        /// Turns compact mode on when the window is taller than the screen's working area (never back off by itself:
        /// no jumping between two looks). Big screens never see it.
        void CheckFit()
        {
            if (compact || fitting || breakBaseHeight == 0 || WindowState == FormWindowState.Minimized) return;
            int avail;
            if (TestWorkHeight > 0) avail = ScalePx(TestWorkHeight);
            else if (offline || Program.Hidden || Screenshots.Active) return;
            else avail = Screen.FromControl(this).WorkingArea.Height;
            if (Height <= avail) return;
            fitting = true;
            try { SetCompact(true); }
            finally { fitting = false; }
        }

        /// Compact mode on / off (picture mode and tests switch it back off). Saves ~98 px at 96 dpi with 4 colours.
        internal void SetCompact(bool on)
        {
            if (compactParts == null || compact == on) return;
            SuspendLayout();
            compact = on;
            int gap = ScalePx(on ? 4 : 8);
            compactParts[0].Margin = new Padding(0, 0, 0, gap);
            compactParts[1].Margin = new Padding(0, 0, 0, gap);
            compactParts[2].Margin = new Padding(0, 0, 0, gap);
            compactParts[3].Height = compactParts[4].Height = ScalePx(on ? 84 : 112);
            foreach (var big in new[] { lblFriends, lblOthers })   // 22 -> 19 pt: the price keeps clear of its grey line
            {
                var old = big.Font;
                big.Font = new Font("Segoe UI", on ? 19f : 22f, FontStyle.Bold);
                old.Dispose();
            }
            int edge = ScalePx(14), edgeV = ScalePx(on ? 10 : 14);
            compactParts[5].Padding = new Padding(edge, edgeV, edge, edgeV);   // the window's top / bottom edge
            int infoV = ScalePx(on ? 4 : 8);   // the live-price box (and its Update button, lined up with it)
            lblPowerInfo.Margin = new Padding(lblPowerInfo.Margin.Left, infoV, lblPowerInfo.Margin.Right, infoV);
            btnPower.Margin = new Padding(btnPower.Margin.Left, infoV, btnPower.Margin.Right, btnPower.Margin.Bottom);
            powerCap.Visible = lblPower.Visible = !on;
            lblBasePower.Visible = on;
            breakRowHeight = BreakRowHeight();
            FitColourGrid();
            lblPurge.Visible = !on && lblPurge.Text.Length > 0;
            UpdatePurgeTip();
            UpdateBreakHeight();
            ResumeLayout(true);
            if (!fitting) Recalculate();   // the Base cost line's power part
        }

        /// Compact: the colour-change note lives in the tooltip of the material rows' grams and of Base cost.
        void UpdatePurgeTip()
        {
            if (printerTip == null) return;
            string tip = compact && lblPurge.Text.Length > 0 ? lblPurge.Text : null;
            foreach (var g in rowGrams) printerTip.SetToolTip(g, tip);
        }

        internal bool TestCompact { get { return compact; } }

        /// Picture mode / tests: "name: window 662 px (fits 672), compact, all inside" - every visible control lies inside
        /// its parent's client area (nothing cut off) and the window fits the made-up working area.
        internal string FitReport(string name)
        {
            var cut = new List<string>();
            Action<Control> walk = null;
            walk = c =>
            {
                foreach (Control k in c.Controls)
                {
                    if (!k.Visible) continue;
                    if (!(c is Form) && (k.Right > c.ClientSize.Width + 1 || k.Bottom > c.ClientSize.Height + 1) && !(c is ScrollableControl && ((ScrollableControl)c).AutoScroll))
                        cut.Add((k.Name.Length > 0 ? k.Name : k.GetType().Name) + " \"" + (k.Text ?? "").Replace("\n", " ") + "\" in " + c.GetType().Name);
                    walk(k);
                }
            };
            walk(this);
            int limit = TestWorkHeight > 0 ? ScalePx(TestWorkHeight) : 0;
            return name + ": window " + Height + " px" + (limit > 0 ? (Height <= limit ? " (fits " : " (TOO TALL for ") + limit + ")" : "") +
                   (compact ? ", compact" : ", normal") + (cut.Count == 0 ? ", all inside" : ", CUT: " + string.Join("; ", cut));
        }

        /// Picture mode: checks the fit now (TestWorkHeight set by Screenshots).
        internal void SampleCheckFit()
        {
            if (!offline && !Program.Hidden) return;
            CheckFit();
            if (compact) Recalculate();   // the Base cost line's power part
        }

        /// Breakdown table rows 2-5 (Power, Failed, Base cost, payback) get the same fixed height; a hidden Failed /
        /// payback row is 0 (Absolute rows don't collapse by themselves). Before Load: AutoSize rows.
        void SetBreakRows()
        {
            if (breakBaseHeight == 0) return;
            bool[] on = { !compact, settings.FailedPrintPct > 0, true, paybackRowOn, plateFeeRowOn };   // compact: Power folds into Base cost
            for (int i = 0; i < on.Length; i++)
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

        /// Bambu Studio's filament profiles for the active printer and nozzle, short names (as Settings lists them); read once per
        /// slicer + nozzle. Empty when Bambu Studio isn't found (the rows then list only their own name).
        List<string> FilamentNames()
        {
            string exe = BambuSlicer.FindExe(settings.SlicerPath);
            string size = Wear.FindNozzle(settings.Nozzle).Size;
            string model = settings.PrinterModel;
            string key = (exe ?? "") + "|" + model + "|" + size;
            if (filamentNames != null && key == filamentNamesKey) return filamentNames;
            filamentNamesKey = key;
            try { filamentNames = exe == null ? new List<string>() : BambuSlicer.ListFilaments(exe, model, size); }
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
                BambuSlicer.FilamentInfo(BambuSlicer.FindExe(settings.SlicerPath), settings.PrinterModel, name, Wear.FindNozzle(settings.Nozzle).Size, out type, out abrasive);
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

        /// "incl. 124 g and 8,7 h for colour changes" (ui-text: no "purge" jargon); only the parts that are known (purge only
        /// above 0,5 g). "" = no line.
        /// Issue #8 step 3: the prime tower is counted apart ("· prime tower 1,70 g · 5 min"); the colour-change grams are then
        /// the flush only (PurgeGrams - TowerGrams). Tower unknown (-1) or 0 = no tower part.
        static string PurgeNote(SliceResult r)
        {
            return PurgeNote(r, int.MaxValue);
        }

        /// The room for the note line (the material table's width less the label's margins); 0 = not laid out yet.
        int PurgeLineWidth()
        {
            var p = lblPurge.Parent;
            return p == null ? 0 : Math.Max(0, p.ClientSize.Width - lblPurge.Margin.Horizontal);
        }

        /// As above; the tower part goes on a second line when both don't fit width px on one (width <= 0 = always one).
        static string PurgeNote(SliceResult r, int width)
        {
            // the wording is shared with the website (QuoteRows.PurgeNoteParts / PurgeNoteJoin); only the fit is measured here
            string note, t;
            QuoteRows.PurgeNoteParts(r.PurgeGrams, r.ColourChangeHours, r.TowerGrams, r.TowerSeconds, out note, out t);
            string one = QuoteRows.PurgeNoteJoin(note, t, false);
            if (note.Length == 0 || t.Length == 0 || width <= 0 || width == int.MaxValue) return one;
            using (var f = new Font("Segoe UI", 9f))   // lblPurge's font
                if (TextRenderer.MeasureText(one, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width <= width)
                    return one;
            return QuoteRows.PurgeNoteJoin(note, t, true);
        }

        /// The quote's "incl. N g for colour changes": the flush only when the tower note is on the page (QuoteShowTower),
        /// else the whole purge (tower included, as before). purge <= 0 stays as it is (no note).
        double QuotePurge(double purge, double tower)
        {
            if (purge <= 0 || settings.QuoteShowTower != 1 || !(tower > 0.005)) return purge;
            return Math.Max(0, purge - tower);
        }

        /// "prime tower 1,70 g · 5 min" (DA "primetårn …"); withTime false / seconds unknown = grams only; grams <= 0 = "".
        internal static string TowerText(double grams, double seconds, bool withTime) { return QuoteRows.TowerText(grams, seconds, withTime); }

        /// Material name of a filament: the profile's short name ("Bambu PLA Matte", "@BBL A1 …" dropped), else its type
        /// ("PLA"), else "Colour 3". Also the key of its remembered kr/kg.
        static string MaterialName(FilamentUse f)
        {
            if (!string.IsNullOrEmpty(f.Profile)) return BambuSlicer.ShortName(f.Profile).Trim();
            if (!string.IsNullOrEmpty(f.Type)) return f.Type;
            return L.F("Colour {0}", "Farve {0}", f.Slot);
        }

        /// "#RRGGBB" (or "#RRGGBBAA") -> colour; Color.Empty if unknown.
        internal static Color ParseColour(string hex)
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
            printerCard.MaxWidth = 0;
            printerCard.FitTo(1);
            t.PerformLayout();
            Func<Control, Rectangle> inTable = c => t.RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
            Rectangle hours = inTable(txtHours), elec = inTable(txtElecPrice);
            int right = inTable(txtUrl).Right;   // = the live-price box's, the buttons' and the Cost breakdown's right edge
            // never over the units: at most its whole cell (column 2 starts after the Print time / Filament / Electricity rows'
            // units; measured with the card at its smallest), so the default name always shows in full
            printerCard.MaxWidth = Math.Max(0, right - printerCard.Left);
            printerCard.FitTo(Math.Max(1, elec.Bottom - hours.Top));   // Print time box top to Electricity price box bottom
            int cellLeft = printerCard.Left, cellTop = printerCard.Top;   // margins are 0 here
            printerCard.Margin = new Padding(Math.Max(0, right - printerCard.Width - cellLeft), Math.Max(0, hours.Top - cellTop), 0, 0);
        }

        /// Bambu Studio's own picture of this printer model from its install folder (read at runtime, nothing of it is in
        /// our exe); null = the card draws a simple printer itself (PrinterArt).
        internal static Bitmap LoadPrinterPhoto(string exe, string model)
        {
            try
            {
                string f = BambuSlicer.PrinterPicture(exe, model);
                return f != null ? PrepareModelPicture(File.ReadAllBytes(f)) : null;
            }
            catch { return null; }
        }

        /// Dark tooltip (the standard one is light).
        internal static void DrawDarkTip(object sender, DrawToolTipEventArgs e)
        {
            using (var b = new SolidBrush(Theme.InfoBack)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            string text = e.ToolTipText ?? "";
            if (text.IndexOf('\n') < 0)
            {
                TextRenderer.DrawText(e.Graphics, text, e.Font, e.Bounds, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            // several lines (VerticalCenter only works on one line): centre the measured block, each line centred
            const TextFormatFlags multi = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix;
            Size sz = TextRenderer.MeasureText(e.Graphics, text, e.Font, e.Bounds.Size, multi);
            var r = new Rectangle(e.Bounds.X, e.Bounds.Y + (e.Bounds.Height - sz.Height) / 2, e.Bounds.Width, sz.Height);
            TextRenderer.DrawText(e.Graphics, text, e.Font, r, Theme.Text, multi);
        }

        /// Clickable printer card: photo on top, "Bambu Lab A1 · 100 W" centred under it, framed like an info box
        /// (rounded; lighter fill, border and text on hover; hand cursor). No photo = a simple A1 drawn in the theme colours.
        sealed class PrinterCard : Control
        {
            static readonly Color HotBack = Theme.HotBack, HotBorder = Theme.HotBorder;
            const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis;
            /// The card's width (device px, 0 = fit the text): a long printer name is cut with … (LayoutPrinterCard).
            public int MaxWidth;
            Bitmap photo;
            int photoHeight;   // device px, set by FitTo
            bool hot;

            public PrinterCard()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, true);   // Tab stop: Enter / Space open the Choose printer window
                TabStop = true;
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.PushButton;
            }

            /// Wear.Models key: which printer the fallback drawing shows when there is no photo.
            public string Model = Wear.ModelA1;

            protected override bool IsInputKey(Keys keyData)
            {
                return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = e.SuppressKeyPress = true; OnClick(EventArgs.Empty); return; }
                base.OnKeyDown(e);
            }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

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
                // measured without EndEllipsis: with it (and no width given) the text measured as cut, so the card shrank to "Bambu…"
                Size ts = TextRenderer.MeasureText(Text.Length > 0 ? Text : "Ag", Font, Size.Empty, TextFlags & ~TextFormatFlags.EndEllipsis);
                int rest = Px(5) + Px(3) + ts.Height + Px(5);
                photoHeight = Math.Max(Px(16), Math.Min(Px(88), height - rest));
                int w = Math.Max(PhotoWidth(photoHeight), ts.Width) + Px(20);
                // fixed width (the whole cell): the card doesn't jump when another printer's name is longer or shorter
                if (MaxWidth > 0) w = Math.Max(PhotoWidth(photoHeight) + Px(20), MaxWidth);
                Size = new Size(w, Math.Max(height, photoHeight + rest));
                Invalidate();
            }

            protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
            protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); hot = false; Invalidate(); }
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
                else PrinterArt.Draw(g, pr, Model);
                if (!Enabled)   // while slicing: the picture dimmed (text stays muted)
                    using (var b = new SolidBrush(Color.FromArgb(140, Theme.InfoBack))) g.FillRectangle(b, pr);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                int ty = pr.Bottom + Px(3);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(Px(6), ty, Width - Px(12), Math.Max(1, Height - ty)), hot || Focused ? Theme.Text : Theme.Muted, TextFlags);
                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -Px(3), -Px(3)), Theme.Text, hot ? HotBack : Theme.InfoBack);
            }
        }

        // Swatch (the colour square of the material rows) is in Theme.cs: the file picker uses it too (idea 21)

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

        /// Each button exactly its measured text (TextRenderer, as the button draws it, real px) + room (both sides
        /// together) + its Padding (e.g. the ▾ part of "Show quote…"). The pictures showed text + 8 wraps, text + 10 fits.
        static void SizeByText(Button[] buttons, int room)
        {
            foreach (var b in buttons)
                b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + room + b.Padding.Horizontal;
        }

        static Label Unit(string text)
        {
            return new Theme.PlainLabel { Text = text, AutoSize = true, Tag = "muted", Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 0) };   // text 6 px after the box (measured)
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
            // grey line under the price: PlainLabel (grayscale text, no ClearType colour fringes), one line
            info = new Theme.PlainLabel { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleCenter, Tag = "muted", UseMnemonic = false };
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
                problem = L.S("Use numbers of 0 or more in the red boxes.", "Brug tal på 0 eller mere i de røde felter.");
            else if (sh == FieldState.Invalid || sm == FieldState.Invalid)
                problem = L.S("Print time: hours 0–999, minutes 0–59.", "Printtid: timer 0–999, minutter 0–59.");
            else if (s1 == FieldState.Empty || s2 == FieldState.Empty || s3 == FieldState.Empty ||
                     (sh == FieldState.Empty && sm == FieldState.Empty))   // both time boxes empty (e.g. a G-code without time)
                problem = "";   // incomplete, no error

            if (problem != null)
            {
                last = null;
                SetStatus(problem, false);
                lblPower.Text = lblFailed.Text = lblBase.Text = lblPayback.Text = "–";   // payback row keeps its place (no jump while typing)
                lblBasePower.Text = "";
                foreach (var c in rowCost) c.Text = "–";
                lblFriends.Text = lblOthers.Text = "–";
                btnCopy.Enabled = btnSave.Enabled = false;
                lastInput = null;
                UpdatePriceLines();
                UpdateReopenedLine();
                UpdateTypedLine();
                return;
            }

            var input = new QuoteInput
            {
                Hours = hours, Minutes = minutes, FilamentGrams = grams,
                FilamentDkkPerKg = filPrice, ElectricityDkkPerKwh = elPrice, Filaments = lines,
                PaybackDebt = PaybackDebt()
            };
            // batch & plates: a sliced / read job's boxes are already the totals (copies and plates are information + the
            // plate fee); a job typed by hand with copies has ONE copy in the boxes: N copies on the typed plates
            if (jobCopiesTyped && jobCopies > 1)
            {
                input = Pricing.ForCopies(input, jobCopies, Math.Max(1, jobPlates), 0, 0, null);
                if (filaments == null) rowGrams[0].Text = Num.Fmt(input.FilamentGrams) + " g";
            }
            else
            {
                input.Quantity = Math.Max(1, jobCopies);
                input.Plates = jobLoaded || jobCopies > 1 ? Math.Max(0, jobPlates) : 0;
            }
            last = Pricing.Calculate(input, settings);
            lastInput = input;
            lastHours = hours; lastMinutes = minutes;

            if (!statusIsSuccess) SetStatus("", false);   // a green "✓ Saved…" stays until its timer clears it
            if (lines != null && last.FilamentCosts != null)
                for (int i = 0; i < colourRows; i++)
                    rowCost[i].Text = Num.Kr(rowItems[i].Sum(k => last.FilamentCosts[k]));
            else rowCost[0].Text = Num.Kr(last.MaterialCost);
            lblPower.Text = Num.Kr(last.PowerCost) + "  (" + last.PowerKwh.ToString("N2", Num.Dk) + " kWh)";
            lblBasePower.Text = L.F("incl. power {0}", "inkl. strøm {0}", Num.Kr(last.PowerCost));   // compact mode only
            if (printerTip != null) printerTip.SetToolTip(lblBasePower, compact ? L.S("Power: ", "Strøm: ") + lblPower.Text : null);
            lblFailed.Text = Num.Kr(last.FailedPrintCost);
            lblBase.Text = Num.Kr(last.BaseCost);
            UpdatePaybackRow();
            lblFriends.Text = Num.Kr(last.FriendsPrice);
            lblOthers.Text = Num.Kr(last.OthersPrice);
            btnCopy.Enabled = btnSave.Enabled = true;
            UpdatePriceLines();
            UpdateReopenedLine();
            UpdateTypedLine();
        }

        // markup lines under the prices (RefreshSettingsLabels); batch & plates puts "4 pcs · 18,18 kr. each" there instead
        string friendsMarkupLine = "", othersMarkupLine = "";

        /// The grey lines under the two prices: the markup text, or for 2+ copies "4 pcs · 18,18 kr. each" with the markup
        /// text (and a plate fee) moved into the line's dark tooltip.
        void UpdatePriceLines()
        {
            bool batch = jobCopies > 1 && last != null;
            // batch-2: a quantity discount adds " · –5 %" to the line (left out when the line would not fit its box)
            lblFriendsInfo.Text = batch ? WithDiscountMark(History.EachText(last.FriendsPrice, jobCopies), true, lblFriendsInfo) : friendsMarkupLine;
            lblOthersInfo.Text = batch ? WithDiscountMark(History.EachText(last.OthersPrice, jobCopies), false, lblOthersInfo) : othersMarkupLine;
            if (printerTip == null) return;
            string fee = last != null && last.PlateFee > 0
                ? "\n" + L.F("incl. {0} for plate changes", "inkl. {0} til pladeskift", Num.Kr(last.PlateFee)) : "";
            string fTip = DiscountTip(true), oTip = DiscountTip(false);
            printerTip.SetToolTip(lblFriendsInfo, batch || fee.Length > 0 || fTip.Length > 0 ? friendsMarkupLine + fee + fTip : null);
            printerTip.SetToolTip(lblOthersInfo, batch || fee.Length > 0 || oTip.Length > 0 ? othersMarkupLine + fee + oTip : null);
            // qol #8: a job typed by hand with copies: the boxes hold ONE copy's time and grams, the units say so
            bool each = jobCopiesTyped && jobCopies > 1;
            if (lblMinutesUnit != null) lblMinutesUnit.Text = each ? L.S("m each", "m/stk.") : "m";
            if (lblGramsUnit != null) lblGramsUnit.Text = each ? L.S("g each", "g/stk.") : "g";
            UpdatePlateFeeRow();
        }

        /// batch-2: "40 pcs · 5,31 kr. each" + " · –5 %" when the price's cut really is the step's % (Pricing.ShownDiscountPct:
        /// rounding / the minimum / the cost floor changed nothing) and the line still fits its label (measured; else the
        /// line as it was, the discount is in the tooltip).
        string WithDiscountMark(string line, bool friends, Label lbl)
        {
            double pct = Pricing.ShownDiscountPct(last, friends);
            if (!(pct > 0)) return line;
            string with = line + " · " + History.DiscountShortText(pct);
            int room = lbl.Width - lbl.Padding.Horizontal - 10;   // GDI+ draws a little wider than TextRenderer measures
            return room <= 0 || TextRenderer.MeasureText(with, lbl.Font).Width <= room ? with : line;
        }

        /// batch-2: the price tooltip's discount lines: "\nBefore discount: 727,20 kr.\nQuantity discount 5 % (from 10 pcs):
        /// –36,36 kr."; a cut that isn't the % (ShownDiscountPct 0): only "\nIncl. quantity discount (from 10 pcs)"; "" without.
        string DiscountTip(bool friends)
        {
            if (last == null || !Pricing.HasDiscount(last, friends)) return "";
            double pct = Pricing.ShownDiscountPct(last, friends);
            double price = friends ? last.FriendsPrice : last.OthersPrice, discount = friends ? last.FriendsDiscount : last.OthersDiscount;
            if (!(pct > 0))
                return "\n" + L.F("Incl. quantity discount (from {0})", "Inkl. mængderabat (fra {0})", History.PiecesText(last.DiscountFrom));
            return "\n" + L.F("Before discount: {0}", "Før rabat: {0}", Num.Kr(price + discount)) +
                   "\n" + L.F("Quantity discount {0} (from {1}): {2}", "Mængderabat {0} (fra {1}): {2}", History.PctText(pct),
                              History.PiecesText(last.DiscountFrom), "–" + Num.Kr(discount));
        }

        Label lblMinutesUnit, lblGramsUnit;   // the m / g units after the time and grams boxes (qol #8)

        void RefreshSettingsLabels()
        {
            // Settings > Electricity > Printer name (N2); a long name is cut with … on the card, full text in the tooltip
            printerCard.Text = AppSettings.CleanPrinterName(settings.PrinterName) + " · " + Num.Fmt(settings.PrinterWatts) + " W";
            printerCard.AccessibleName = printerCard.Text;
            printerTip.SetToolTip(printerCard, printerCard.Text + "\n" + L.S("Click to choose the printer", "Klik for at vælge printer"));
            string exe = BambuSlicer.FindExe(settings.SlicerPath);
            string photoKey = (exe ?? "") + "|" + Wear.FindModel(settings.PrinterModel).Key;
            if (photoKey != printerPhotoExe)   // first time, the slicer path changed in Settings, or another printer model
            {
                printerPhotoExe = photoKey;
                printerCard.Model = settings.PrinterModel;
                printerCard.Photo = LoadPrinterPhoto(exe, settings.PrinterModel);
            }
            LayoutPrinterCard();   // the text width may have changed
            string wear = settings.MachineRatePerHour > 0 ? L.F(", {0} kr/h wear", ", {0} kr/t slid", Num.Fmt(settings.MachineRatePerHour)) : "";
            // both lines say "markup" when the customer line fits its box with it (measured); otherwise both drop it, so the
            // two sides always read the same way (qol #16): "+75 %, 1,71 kr/h wear, min. 50 kr."
            string min = settings.MinimumPrice > 0 ? L.F(", min. {0} kr.", ", min. {0} kr.", Num.Fmt(settings.MinimumPrice)) : "";
            string fLong = L.F("+{0} % markup", "+{0} % avance", Num.Fmt(settings.FriendsMarkupPct)) + wear;
            string oLong = L.F("+{0} % markup", "+{0} % avance", Num.Fmt(settings.OthersMarkupPct)) + wear + min;
            // a few px spare: the grey line is drawn by GDI+ (PlainLabel), a little wider than TextRenderer measures
            int room = Math.Min(lblOthersInfo.Width, lblFriendsInfo.Width) - lblOthersInfo.Padding.Horizontal - 10;
            bool fits = room <= 0 || (TextRenderer.MeasureText(oLong, lblOthersInfo.Font).Width <= room
                                      && TextRenderer.MeasureText(fLong, lblFriendsInfo.Font).Width <= room);
            friendsMarkupLine = fits ? fLong : "+" + Num.Fmt(settings.FriendsMarkupPct) + " %" + wear;
            othersMarkupLine = fits ? oLong : "+" + Num.Fmt(settings.OthersMarkupPct) + " %" + wear + min;
            UpdatePriceLines();
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
            int rows = (compact ? 1 : 2) + (settings.FailedPrintPct > 0 ? 1 : 0) + (paybackRowOn ? 1 : 0) + (plateFeeRowOn ? 1 : 0);
            int h = breakBaseHeight - 3 * plainRowHeight + rows * breakRowHeight + colourGrid.Height + colourGrid.Margin.Vertical;
            if (lblPurge.Text.Length > 0 && !compact) h += lblPurge.PreferredHeight + lblPurge.Margin.Vertical;   // text "" = hidden; compact: a tooltip
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
                lblPaybackCap.Text = last.PaybackBothPrices ? L.S("Earning back failed prints", "Fejlprint tjenes ind")
                                                            : L.S("Earning back failed prints – customers only", "Fejlprint tjenes ind – kun kunder");
                lblPayback.Text = L.F("{0}  ({1} left)", "{0}  ({1} tilbage)", Num.Kr(last.PaybackCost), Num.Kr(last.PaybackLeft));
            }
            if (on == paybackRowOn) return;
            paybackRowOn = on;
            lblPaybackCap.Visible = lblPayback.Visible = on;
            UpdateBreakHeight();
        }

        /// qol #12: "Plate changes (1 × 5,00 kr.)   5,00 kr." under Base cost (and payback) while a plate change fee is
        /// added (both prices, not marked up). The box grows/shrinks by one row.
        void UpdatePlateFeeRow()
        {
            if (lblPlateFeeCap == null) return;
            bool on = last != null && last.PlateFee > 0;
            if (on)
            {
                int changes = lastInput != null ? Math.Max(1, lastInput.Plates - 1) : 1;
                double rate = last.PlateFee / changes;
                lblPlateFeeCap.Text = QuoteRows.PlatesRowLabel() + " (" + changes + " × " + Num.Kr(rate) + ")";
                lblPlateFee.Text = Num.Kr(last.PlateFee);
            }
            if (on == plateFeeRowOn) return;
            plateFeeRowOn = on;
            lblPlateFeeCap.Visible = lblPlateFee.Visible = on;
            UpdateBreakHeight();
        }

        Label lblPlateFeeCap, lblPlateFee;
        bool plateFeeRowOn;

        /// Picture mode: a made-up debt of failed prints with payback on (0 = back to the defaults: no debt).
        internal void SetSamplePayback(double debt)
        {
            if (!offline) return;
            sampleDebt = debt;
            settings.FailedPayback = debt > 0 ? 1 : new AppSettings().FailedPayback;   // 0 = back to the default (on since v1.8.0; no debt = no row)
            Recalculate();
        }

        /// tab = SettingsForm.TabPrices … TabApp, or -1 = the tab used last.
        void OpenSettings(int tab)
        {
            OpenSettings(tab, null);
        }

        /// prepare: called with the new Settings window before it shows (e.g. which printer the Printer tab shows).
        void OpenSettings(int tab, Action<SettingsForm> prepare)
        {
            // building Settings takes a moment: say so on the button (also when opened from the printer card)
            string btnText = btnSettings.Text;
            var oldCursor = Cursor.Current;
            btnSettings.Text = L.S("Wait…", "Åbner…");   // short: the button is only "Settings" wide (no ellipsis)
            Cursor.Current = Cursors.WaitCursor;
            btnSettings.Update();
            MethodInvoker restore = delegate { btnSettings.Text = btnText; Cursor.Current = oldCursor; };
            try
            {
                EnsureHistory();   // for "Not made back yet: x kr." (picture mode: empty list)
                using (var dlg = new SettingsForm(settings, offline ? sampleDebt : History.FailedDebt(history, null), tab))
                {
                    if (!offline) dlg.UpdateFound = ShowUpdateButton;   // "check now" in Settings
                    dlg.BackupRequested = (owner, t) => OpenBackup(owner, t);   // App tab: Back up… / Restore…
                    if (prepare != null) prepare(dlg);
                    dlg.Shown += delegate { restore(); };   // back to normal once the window is up
                    var answer = dlg.ShowDialog(this);
                    if (dlg.SetupRequested && !offline) BeginInvoke((MethodInvoker)RunSetupAgain);   // App tab: "Run setup again…"
                    if (answer == DialogResult.OK) TakeNewSettings(dlg.Result, dlg.RestartRequested);
                }
            }
            finally { restore(); }   // also if building the window failed
        }

        /// After Settings' OK or the setup window (idea 19): the new settings and their follow-ups (update check, live
        /// price, Studio file, labels, colour rows, prices); restart = the language changed (saved first, then restart).
        void TakeNewSettings(AppSettings newSettings, bool restart)
        {
            var old = settings;
            settings = newSettings;
            if (old.AutoUpdateCheck == 0 && settings.AutoUpdateCheck != 0 && pendingUpdate == null)
                BeginInvoke((MethodInvoker)StartupUpdateCheck);   // switched back on: check now
            SyncUpdateTimer();   // the 10-minute check follows the setting
            bool powerChanged = old.PriceArea != settings.PriceArea || old.PowerAddOn != settings.PowerAddOn ||
                                old.PowerIncludeVat != settings.PowerIncludeVat ||
                                (old.AutoPowerPrice == 0 && settings.AutoPowerPrice != 0);
            if (powerChanged && settings.AutoPowerPrice != 0) BeginInvoke((MethodInvoker)FetchPowerPrice);
            // other slicing settings make the slice out of date: the button slices again instead of opening it
            bool slicingChanged = SlicingChanged(old, settings);
            if (slicingChanged || old.FilamentPreset != settings.FilamentPreset || old.SlicerPath != settings.SlicerPath)
                SetStudioFile(null);
            SaveLastPrices();
            RefreshSettingsLabels();
            UpdateQuoteButton();   // Show quote… / Show invoice… (MainInvoice)
            foreach (var cb in rowCombo) cb.Tag = null;   // nozzle / slicer may have changed: re-read the list when opened
            if (!jobLoaded && cts == null) SetColours(null);   // no model yet: the row follows Settings' filament profile
            Recalculate();
            if (cts == null)
            {
                if (jobLoaded && slicingChanged && old.ActivePrinterId != settings.ActivePrinterId) ShowPrinterChanged();
                else ShowAutoIdle();
            }
            if (restart && !offline)
            {
                // new language: settings are already saved above; Restart closes this window (saves again) and starts anew
                if (cts != null) cts.Cancel();
                BeginInvoke((MethodInvoker)Program.Restart);   // same arguments (test mode stays in its folder)
            }
        }

        // ---------- first-time setup (idea 19): hint after "Set up later", test hooks ----------
        bool setupHintPending;

        static string SetupHintText { get { return L.S("Setup skipped – run it now", "Opsætning sprunget over – kør den nu"); } }

        /// Program, after the setup window was closed with Set up later / ✕ / Esc: once the window shows, the info box gets
        /// a "Setup skipped – run it now" line (the normal help text stays); the next message replaces it.
        internal void ShowSetupHint()
        {
            setupHintPending = true;
        }

        void ApplySetupHint()
        {
            if (!setupHintPending || cts != null) return;
            setupHintPending = false;
            lblAuto.SetLink(SetupHintText, () => { if (!offline) RunSetupAgain(); });
        }

        /// A settings.ini read problem (AppSettings.LoadProblem) in the info box with the Warning stripe; null = nothing.
        /// The next message replaces it (it is said once per start).
        internal void ShowSettingsProblem(string problem)
        {
            if (string.IsNullOrEmpty(problem) || cts != null) return;
            SetAuto(problem, false);
            autoWarning = true;
            SetAutoColor();
        }

        /// Picture mode: the hint on the fresh window (main_setup_hint.png).
        internal void SampleSetupHint()
        {
            setupHintPending = true;
            ApplySetupHint();
        }

        /// --test-setup: the settings in use and whether the hint shows.
        internal AppSettings TestSettings { get { return settings; } }
        internal string TestPowerLine { get { return lblPowerInfo.Text; } }
        internal bool TestHintShown { get { return lblAuto.LinkControl != null && lblAuto.LinkControl.Text == SetupHintText; } }

        /// --test-setup: Settings > App > "Run setup again…" through the real button (Settings closes, RunSetupAgain follows).
        internal void TestSetupAgain()
        {
            if (!AppSettings.TestMode || !Program.Hidden) return;
            OpenSettings(SettingsForm.TabApp, s => s.Shown += delegate { s.BeginInvoke((MethodInvoker)s.TestClickSetupAgain); });
        }

        /// --test-setup: the lines after "main" (SetupTest.PlayMain).
        internal void TestSetupFlow(SetupTest t)
        {
            if (!AppSettings.TestMode || !Program.Hidden || t == null) return;
            t.PlayMain(this);
        }

        /// Settings > App > "Run setup again…" (idea 19): the setup window with today's answers filled in; Save takes them
        /// over like Settings' OK (a fixed electricity price goes into the main window's box first).
        void RunSetupAgain()
        {
            if (offline || IsDisposed) return;
            try
            {
                SaveLastPrices();   // the electricity box's price is the setup's starting value
                using (var dlg = new SetupForm(settings, true))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
                    var res = dlg.Result;
                    if (res.AutoPowerPrice == 0) txtElecPrice.Text = Num.Fmt(res.LastElectricityPrice);   // SaveLastPrices reads it back
                    TakeNewSettings(res, dlg.RestartRequested);
                }
            }
            catch (Exception ex) { ErrorReport.Show(ex); }
        }

        // ---------- several printers (ideas 7 + 24 + 11) ----------
        /// The printer card: the Choose printer window (a tile per printer). A pick makes that printer the active one;
        /// "Printer settings…" / the Add tile open Settings > Printer.
        void ChoosePrinter()
        {
            if (cts != null) return;   // not while slicing: the job uses the active printer's profiles
            EnsureHistory();
            using (var dlg = new PrinterPickerForm(settings, history, DateTime.Now))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (dlg.AddRequested)
                    OpenSettings(SettingsForm.TabPrinter, s => s.ShowPrinter(null, true));
                else if (dlg.SettingsRequested)
                {
                    string id = dlg.SettingsPrinterId;
                    OpenSettings(SettingsForm.TabPrinter, s => s.ShowPrinter(id, false));
                }
                else UsePrinter(dlg.ChosenId);
            }
        }

        /// Makes printer `id` the active one: its wear (auto rate recalculated), power, nozzle, plate and profiles are
        /// used from now on (saved). A model already sliced with other printer settings gets the warning line.
        void UsePrinter(string id)
        {
            if (string.IsNullOrEmpty(id) || id == settings.ActivePrinterId) return;
            var old = settings.Clone();
            if (!settings.SelectPrinter(id)) return;
            if (settings.AutoWearRate != 0) settings.MachineRatePerHour = AutoWearRate(settings);
            if (!offline) settings.Save();
            bool slicingChanged = SlicingChanged(old, settings);
            if (slicingChanged) SetStudioFile(null);   // the slice was made for the other printer
            foreach (var cb in rowCombo) cb.Tag = null;   // another printer's filament list
            RefreshSettingsLabels();
            if (!jobLoaded && cts == null) SetColours(null);
            Recalculate();
            if (cts != null) return;
            if (jobLoaded && slicingChanged && reopened == null) ShowPrinterChanged();
            else if (!jobLoaded) ShowAutoIdle();
        }

        /// True when the printer settings that change a slice differ (model, nozzle, print profile, build plate).
        static bool SlicingChanged(AppSettings a, AppSettings b)
        {
            return Wear.FindModel(a.PrinterModel).Key != Wear.FindModel(b.PrinterModel).Key || a.Nozzle != b.Nozzle ||
                   a.ProcessPreset != b.ProcessPreset || a.BedType != b.BedType;
        }

        /// Warning stripe in the model info box: the time and weight shown were sliced for the printer used before.
        void ShowPrinterChanged()
        {
            // two lines at most (a third line would make the window taller)
            SetAuto(L.F("Printer changed to {0}: click \"{1}\" to slice for it again.",
                        "Printer skiftet til {0}: klik \"{1}\" for at beregne den igen.",
                        AppSettings.CleanPrinterName(settings.PrinterName), AnalyzeText.Replace("&&", "&")), false);
            autoWarning = true;
            SetAutoColor();
        }

        /// Picture mode: adds a printer of this model (in memory) and switches to it, as a pick in Choose printer does.
        internal void SampleSwitchPrinter(string modelKey)
        {
            if (!offline) return;
            UsePrinter(settings.AddPrinter("", modelKey).Id);
        }

        /// A saved quote's printer becomes the active one again if it still exists (Open again / Repeat print).
        void UseRecordPrinter(QuoteRecord r)
        {
            string id = History.PrinterOf(r);
            if (id == settings.ActivePrinterId || settings.FindPrinter(id) == null) return;
            if (!settings.SelectPrinter(id)) return;
            if (settings.AutoWearRate != 0) settings.MachineRatePerHour = AutoWearRate(settings);
            if (!offline) settings.Save();
            foreach (var cb in rowCombo) cb.Tag = null;
            RefreshSettingsLabels();
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
            // the longest text that fits on the button without "…" (full text in its tooltip when shortened)
            string ver = Updater.Short(found.Version);
            string full = L.S("⬆  Update to v", "⬆  Opdatér til v") + ver;
            btnUpdate.Text = full;
            foreach (string t in new[] { full, L.S("⬆  Update v", "⬆  Opdatér v") + ver, "⬆  v" + ver })   // shortest: the version only (DA "Opdatér" would repeat the power price's Update button, qol #10)
            {
                btnUpdate.Text = t;
                if (TextRenderer.MeasureText(t, btnUpdate.Font).Width + ScalePx(10) <= btnUpdate.Width) break;
            }
            printerTip.SetToolTip(btnUpdate, btnUpdate.Text == full ? null : full.Replace("⬆  ", ""));
            btnUpdate.Visible = true;
            // the update button wins the place left of the buttons: hide "Made by Gettic · GitHub", Update first in tab order
            lblMadeBy.Visible = false;
            lnkGitHub.Visible = false;
            leftCell.TabIndex = 0;
            bottomButtons.TabIndex = 1;
        }

        /// The credit on the button row, two lines: "Made by Gettic" over the "GitHub" link, the block centred on the row
        /// (button + its margins), both lines' first glyphs at the same x (measured glyph boxes, both labels draw with GDI+).
        void PlaceMadeBy(Button reference)
        {
            int rowTop = leftCell.PointToClient(reference.PointToScreen(Point.Empty)).Y - reference.Margin.Top;
            int rowHeight = reference.Height + reference.Margin.Vertical;
            int line = lblMadeBy.Font.Height;   // normal line gap
            int top = rowTop + (rowHeight - 2 * line) / 2;
            lblMadeBy.Location = new Point(0, top);
            RectangleF first = GlyphBox(lblMadeBy, 0, 1), link = GlyphBox(lnkGitHub, 0, 1);
            lnkGitHub.Location = new Point((int)Math.Round(lblMadeBy.Left + first.Left - link.Left), top + line);
            lnkGitHub.BringToFront();
        }
        /// Where the glyphs of text[start, start+length) are drawn inside a GDI+ (UseCompatibleTextRendering) label.
        static RectangleF GlyphBox(Label l, int start, int length)
        {
            using (var g = l.CreateGraphics())
            using (var sf = new StringFormat(StringFormat.GenericDefault))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                sf.SetMeasurableCharacterRanges(new[] { new CharacterRange(start, length) });
                var area = new RectangleF(l.Padding.Left, l.Padding.Top, 10000, Math.Max(1, l.ClientSize.Height));
                var regions = g.MeasureCharacterRanges(l.Text, l.Font, area, sf);
                RectangleF r = regions.Length > 0 ? regions[0].GetBounds(g) : RectangleF.Empty;
                foreach (var rg in regions) rg.Dispose();
                return r;
            }
        }

        int Ascent(Font f)
        {
            var fam = f.FontFamily;
            return (int)Math.Round(f.SizeInPoints * DeviceDpi / 72f * fam.GetCellAscent(f.Style) / fam.GetEmHeight(f.Style));
        }

        /// Opens the project page in the browser (fixed address; nothing is sent before the click).
        void OpenProjectPage()
        {
            try { Process.Start("https://github.com/gettic/PrintPrice"); }
            catch { }
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

        /// The quote as plain text (Copy text): model, time and grams, both prices; "For: <name>" when a customer is given.
        string QuoteText(string customer)
        {
            // batch & plates: "Quantity: 4 pcs", "… on 2 plates", "(18,18 kr. each)"; a typed copy's totals come from lastInput
            bool typed = jobCopiesTyped && jobCopies > 1 && lastInput != null;
            string text = QuoteRows.JobText(customer, LinkSource(), typed ? lastInput.Hours : lastHours, typed ? lastInput.Minutes : lastMinutes,
                                            typed ? Num.Fmt(lastInput.FilamentGrams) : txtGrams.Text,
                                            filaments != null ? filaments.Count : 0, last,
                                            jobCopies, Math.Max(1, jobPlates), settings.QuoteShowUnitPrice != 0);   // batch-2 (T2: the % only when it is the real cut)
            return WithTowerLine(text, filaments != null && settings.QuoteShowTower == 1 ? jobTowerGrams : -1);   // issue #8 step 3
        }

        /// Issue #8 step 3: Copy text gets "Prime tower 1,70 g" under the print-time line (as the quote's note row);
        /// grams <= 0 = the text as it is.
        static string WithTowerLine(string text, double grams)
        {
            if (!(grams > 0.005) || string.IsNullOrEmpty(text)) return text;
            string nl = Environment.NewLine;
            string[] lines = text.Split(new[] { nl }, StringSplitOptions.None);
            int at = Array.FindIndex(lines, s => s.StartsWith("Print time", StringComparison.Ordinal) || s.StartsWith("Printtid", StringComparison.Ordinal));
            var list = new List<string>(lines);
            list.Insert(at >= 0 ? at + 1 : list.Count, QuoteRows.TowerNote(grams));
            return string.Join(nl, list);
        }

        // ---------- quote export (idea 5 + 21): "Show quote… ▾" → QuoteForm, or quick exports from the ▾ menu ----------
        /// The ▾ menu of "Show quote…": quick exports without a customer name, with the remembered price and look
        /// (both are chosen in the quote window); a muted first line says which.
        void BuildQuoteMenu()
        {
            quoteMenu = new ContextMenuStrip
            {
                ShowImageMargin = false, Renderer = new QuoteMenuRenderer(),
                BackColor = Theme.Input, ForeColor = Theme.Text
            };
            Func<string, QuoteExport, ToolStripMenuItem> Item = (text, kind) =>
            {
                var mi = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
                mi.Click += delegate { MenuExport(kind); };
                return mi;
            };
            miQuoteInfo = new ToolStripMenuItem("") { Enabled = false };
            quoteMenu.Items.Add(miQuoteInfo);
            quoteMenu.Items.Add(new ToolStripSeparator());
            quoteMenu.Items.Add(Item(L.S("Copy picture", "Kopiér billede"), QuoteExport.Copy));
            quoteMenu.Items.Add(Item(L.S("Save picture…", "Gem billede…"), QuoteExport.Png));
            quoteMenu.Items.Add(Item(L.S("Save as PDF…", "Gem som PDF…"), QuoteExport.Pdf));
            quoteMenu.Items.Add(Item(L.S("Copy text", "Kopiér tekst"), QuoteExport.Text));
            quoteMenu.Opening += delegate { UpdateQuoteMenu(); };
            Disposed += delegate { quoteMenu.Dispose(); };

            // split button: a press on the ▾ part opens the menu (on its Click); keyboard: Down / menu key
            btnCopy.MouseDown += (s, e) =>
            {
                copyArrowPressed = e.Button == MouseButtons.Left && copyArrowWidth > 0 && e.X >= btnCopy.Width - copyArrowWidth;
            };
            btnCopy.PreviewKeyDown += (s, e) => { if (e.KeyCode == Keys.Down) e.IsInputKey = true; };
            btnCopy.KeyDown += (s, e) =>
            {
                copyArrowPressed = false;
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Apps) { ShowQuoteMenu(); e.Handled = true; }
            };
            btnCopy.Paint += (s, e) => PaintCopyArrow(e.Graphics);
            btnCopy.AccessibleDescription = L.S("Down arrow: quick copy or save without a name", "Pil ned: hurtig kopi eller gem uden navn");
        }

        void ShowQuoteMenu()
        {
            if (!btnCopy.Enabled) return;
            quoteMenu.Show(btnCopy, new Point(btnCopy.Width, 0), ToolStripDropDownDirection.AboveLeft);
        }

        /// The menu's muted first line: "No name · customer price · dark".
        void UpdateQuoteMenu()
        {
            // with Show invoice… the quick menu still makes quotes (an invoice needs a name): the line says so
            miQuoteInfo.Text = (MainInvoice ? L.S("Quote · no name · ", "Tilbud · uden navn · ") : L.S("No name · ", "Uden navn · "))
                + (settings.QuoteFriends ? L.S("friends && family price", "venne- og familiepris") : L.S("customer price", "kundepris"))
                + " · " + (settings.QuoteDark != 0 ? L.S("dark", "mørk") : L.S("light", "lys"));
        }

        void MenuExport(QuoteExport kind)
        {
            bool failed;
            string msg = ExportQuote(this, kind, settings.QuoteFriends, settings.QuoteDark != 0, "", out failed);
            if (msg == null) return;   // file dialog cancelled
            if (failed) SetStatus(msg, false);
            else FlashStatus(msg);
        }

        /// The ▾ part of the split button: a 1 px Border divider and a small arrow (Muted when disabled).
        void PaintCopyArrow(Graphics g)
        {
            if (copyArrowWidth <= 0) return;
            int x = btnCopy.Width - copyArrowWidth, inset = ScalePx(6);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, x, inset, x, btnCopy.Height - 1 - inset);
            float cx = x + copyArrowWidth / 2f, cy = btnCopy.Height / 2f, w = ScalePx(4.5f);
            var st = g.Save();
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(btnCopy.Enabled ? Theme.Text : Theme.Muted))
                g.FillPolygon(br, new[] { new PointF(cx - w, cy - w / 2), new PointF(cx + w, cy - w / 2), new PointF(cx, cy + w / 2) });
            g.Restore(st);
        }

        /// Settings > Quote & invoice "Main window: Show invoice… instead of Show quote…" (AppSettings.MainInvoice).
        bool MainInvoice { get { return settings.MainInvoice == 1; } }

        /// The split button's text: "Show quote…" or, with MainInvoice on, "Show invoice…".
        string QuoteButtonText()
        {
            return MainInvoice ? L.S("Show invoice…", "Vis faktura…") : L.S("Show quote…", "Vis tilbud…");
        }

        /// Save to history + Show quote… ▾ fill saveCopyPair (the Customers box): 50/50, or split by their texts when one
        /// needs more than half. Load, and after Settings changes the button's text.
        void SplitSaveCopy()
        {
            if (saveCopyPair <= 0) return;
            SizeByText(new[] { btnSave, btnCopy }, ScalePx(14));
            int pair = saveCopyPair;
            if (btnSave.Width <= pair / 2 && btnCopy.Width <= pair - pair / 2) btnSave.Width = pair / 2;
            else btnSave.Width = (int)Math.Round((double)pair * btnSave.Width / (btnSave.Width + btnCopy.Width));
            btnCopy.Width = pair - btnSave.Width;
        }

        /// After Settings: the button follows MainInvoice (text and the pair's split).
        void UpdateQuoteButton()
        {
            string text = QuoteButtonText();
            if (btnCopy.Text == text) return;
            btnCopy.Text = text;
            SplitSaveCopy();
            btnCopy.Invalidate();
        }

        /// "Show quote…": the quote window (preview, customer name, price, look, copy / save). Nothing is saved by opening it.
        /// With MainInvoice on ("Show invoice…") the same window in invoice mode (NewJobInvoiceForm).
        void ShowQuoteWindow()
        {
            if (last == null || lastInput == null || cts != null) return;
            using (var f = MainInvoice ? NewJobInvoiceForm("") : NewQuoteForm("")) f.ShowDialog(this);
        }

        /// "Show invoice…" (MainInvoice): the invoice window for the job on screen. Opening it saves nothing and uses no
        /// number; the first Copy / Save / PDF / Copy text saves the job (History.AddFor), makes the invoice
        /// (History.MakeInvoice: number, date, Printed) and from then on the window shows that record: later exports in the
        /// same window re-issue the same invoice (ExportRecord), the name is locked. Save to history = a plain quote, no number.
        QuoteForm NewJobInvoiceForm(string customer)
        {
            EnsureHistory();
            quoteWhen = DateTime.Now;
            var made = new QuoteRecord[1];   // the invoice's record once made (null before)
            string help = L.S("Making the invoice (copy or save) gives it a number, saves it in the history and marks it as printed.",
                              "Når fakturaen laves (kopi eller gem), får den et nummer, gemmes i historikken og markeres som printet.");
            QuoteForm f = null;
            f = new QuoteForm(QuoteModelName(), customer, settings.QuoteFriends, settings.QuoteDark != 0,
                History.RecentCustomers(history, 10), last.OthersPrice, last.FriendsPrice,
                (friends, name) => made[0] != null ? BuildRecordQuote(made[0], friends, name, true, modelImage)
                                                   : BuildJobInvoice(friends, name),
                (IWin32Window o, QuoteExport kind, bool friends, bool dark, string name, out bool failed) =>
                {
                    bool before = made[0] != null;
                    string msg = ExportJobInvoice(o, kind, friends, dark, name, made, f.PrintStatus, out failed);
                    if (!before && made[0] != null)
                    {
                        f.LockInvoice(BilledFriends(made[0]));   // made now: its name and price stay
                        f.PrintStatus = History.PrintStateOf(made[0], DateTime.Now);
                    }
                    return msg;
                },
                (friends, dark) =>
                {
                    settings.QuotePrice = friends ? AppSettings.QuotePriceFriends : AppSettings.QuotePriceCustomer;
                    settings.QuoteDark = dark ? 1 : 0;
                    if (!offline) settings.Save();
                },
                true, help);
            // issue #7: a new invoice starts at "Printed" (making it ticks Printed, as before); the pick is stored when it
            // is made, and saved at once after that
            f.PrintStatus = PrintState.Printed;
            f.StatusPicked += delegate { if (made[0] != null) SaveStatusNow(f, new List<QuoteRecord> { made[0] }); };
            return f;
        }

        /// The invoice page of the job on screen before it is made: number "(new)", today's date, due date and the
        /// payment ways from Settings (History.InvoiceInfo without a record).
        QuoteData BuildJobInvoice(bool friends, string customer)
        {
            var d = BuildQuote(friends, quoteWhen, History.QuoteNumber(quoteWhen), customer);
            var inv = History.InvoiceInfo(null, settings, quoteWhen);
            d.Invoice = true;
            d.InvoiceNo = L.S("(new)", "(ny)");
            d.Due = inv.Due;
            d.PayMobilePay = (inv.MobilePay ?? "").Trim();
            d.PayBank = (inv.Bank ?? "").Trim();
            d.Name = (inv.FromName ?? "").Trim();
            return d;
        }

        /// One export of the job's invoice window. made[0] = the record once the invoice is made (then ExportRecord: same
        /// number). Save to history = the quote only (no number). Otherwise: name required, file dialog first (cancel =
        /// nothing saved, no number used), then the job is saved, the invoice made and the page copied / written.
        string ExportJobInvoice(IWin32Window owner, QuoteExport kind, bool friends, bool dark, string customer,
                                QuoteRecord[] made, PrintState state, out bool failed)
        {
            failed = false;
            if (made[0] != null)
                return ExportRecord(owner, made[0], true, kind, friends, dark, customer, modelImage, null, out failed);
            if (kind == QuoteExport.History) return ExportQuote(owner, kind, friends, dark, customer, null, out failed);
            if (last == null || lastInput == null)
            {
                failed = true;
                return L.S("Nothing to send yet.", "Der er ikke noget at sende endnu.");
            }
            customer = (customer ?? "").Trim();
            if (customer.Length == 0)
            {
                failed = true;
                return L.S("Type the customer's name first: it goes on the invoice.", "Skriv kundens navn først: det kommer på fakturaen.");
            }
            DateTime now = DateTime.Now;
            string path = null, wanted = null;
            if (kind == QuoteExport.Png || kind == QuoteExport.Pdf)
            {
                // "2026-48213 - Name": the number is picked now but only used once the file is saved (issue #6)
                wanted = InvoiceCodeFor(null, now);
                path = AskExportPath(owner, kind, true, QuoteRows.InvoiceFileName(wanted, customer));
                if (path == null) return null;
            }
            QuoteRecord rec;
            string err = SaveToHistory(friends ? History.ForFriends : History.ForCustomer, customer, state, out rec);
            if (rec == null) { failed = true; return err ?? L.S("Nothing to save yet.", "Der er ikke noget at gemme endnu."); }
            made[0] = rec;
            return ExportRecordTo(rec, true, kind, friends, dark, customer, modelImage, state, path, wanted, now, out failed);
        }

        /// Quote / invoice text to the clipboard (--test-invoice: to the test's own catcher instead).
        void CopyText(string text)
        {
            if (testClipboard != null) testClipboard(text);
            else Clipboard.SetText(text);
        }

        /// Quote / invoice picture to the clipboard (--test-invoice: only noted, as "[picture]").
        void CopyPicture(Bitmap bmp)
        {
            if (testClipboard != null) testClipboard("[picture]");
            else QuotePage.CopyToClipboard(bmp);
        }

        /// The Save picture / Save as PDF dialog of a quote or invoice export; null = cancelled (or not a file export).
        static string AskExportPath(IWin32Window owner, QuoteExport kind, bool invoice, string fileName)
        {
            if (kind != QuoteExport.Png && kind != QuoteExport.Pdf) return null;
            if (testExportPath != null) return testExportPath(kind, fileName);   // --test-invoice: no dialog (null = "cancelled")
            bool pdf = kind == QuoteExport.Pdf;
            string title = invoice ? (pdf ? L.S("Save invoice as PDF", "Gem faktura som PDF") : L.S("Save invoice picture", "Gem faktura som billede"))
                                   : (pdf ? L.S("Save quote as PDF", "Gem tilbud som PDF") : L.S("Save quote picture", "Gem tilbud som billede"));
            using (var dlg = new SaveFileDialog
            {
                Title = title,
                Filter = pdf ? "PDF (*.pdf)|*.pdf" : L.S("PNG picture", "PNG-billede") + " (*.png)|*.png",
                FileName = fileName + (pdf ? ".pdf" : ".png"), AddExtension = true, OverwritePrompt = true
            })
                return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.FileName : null;
        }

        QuoteForm NewQuoteForm(string customer)
        {
            EnsureHistory();
            quoteWhen = DateTime.Now;   // the preview's number/date until an export saves the quote (then the record's)
            QuoteForm f = null;
            f = new QuoteForm(QuoteModelName(), customer, settings.QuoteFriends, settings.QuoteDark != 0,
                History.RecentCustomers(history, 10), last.OthersPrice, last.FriendsPrice,
                (friends, name) => BuildQuote(friends, quoteWhen, History.QuoteNumber(quoteWhen), name),
                // every export saves the quote with the print status chosen in the window
                (IWin32Window o, QuoteExport kind, bool friends, bool dark, string name, out bool failed) =>
                    ExportQuote(o, kind, friends, dark, name, f.PrintStatus, out failed),
                (friends, dark) =>
                {
                    settings.QuotePrice = friends ? AppSettings.QuotePriceFriends : AppSettings.QuotePriceCustomer;
                    settings.QuoteDark = dark ? 1 : 0;
                    if (!offline) settings.Save();
                });
            f.PrintStatus = PrintState.NotPrinted;   // a new job: always "Not printed yet"
            return f;
        }

        /// One export (window or ▾ menu): Copy picture / Save picture / Save as PDF ask for the file first (null = cancelled),
        /// then the quote goes into the history with this price and customer (History.AddFor), then the page is drawn
        /// (PDF always light) and copied / written. Returns the status text; failed = it is an error text.
        string ExportQuote(IWin32Window owner, QuoteExport kind, bool friends, bool dark, string customer, out bool failed)
        {
            return ExportQuote(owner, kind, friends, dark, customer, null, out failed);
        }

        /// state = the quote window's print status (put on the saved quote, History.AddFor with a state); null = the ▾ quick
        /// menu (no status: today's rule).
        string ExportQuote(IWin32Window owner, QuoteExport kind, bool friends, bool dark, string customer, PrintState? state, out bool failed)
        {
            failed = false;
            if (last == null || lastInput == null)
            {
                failed = true;
                return L.S("Nothing to send yet.", "Der er ikke noget at sende endnu.");
            }
            customer = (customer ?? "").Trim();
            if (kind == QuoteExport.History)
            {
                // "Save to history": the quote with this price and name, nothing else
                QuoteRecord saved;
                string e = SaveToHistory(friends ? History.ForFriends : History.ForCustomer, customer, state, out saved);
                if (saved != null) { DateTime w; if (History.TryTime(saved.SavedAt, out w)) quoteWhen = w; }
                if (e != null) { failed = true; return e; }
                return L.S("✓ Saved to history.", "✓ Gemt i historikken.") + FailedSavedNote(saved);
            }
            string path = null;
            if (kind == QuoteExport.Png || kind == QuoteExport.Pdf)
            {
                path = AskExportPath(owner, kind, false, QuoteFileName(DateTime.Now, customer));
                if (path == null) return null;
            }
            QuoteRecord rec;
            string err = SaveToHistory(friends ? History.ForFriends : History.ForCustomer, customer, state, out rec);
            DateTime when;
            if (rec == null || !History.TryTime(rec.SavedAt, out when)) when = DateTime.Now;
            quoteWhen = when;
            string copyNote;
            string done = WriteExport(kind, false, dark, path, () => QuoteText(customer),
                                      d => QuotePage.Render(BuildQuote(friends, when, History.QuoteNumber(when), customer), d),
                                      null, null, out copyNote, out failed);
            if (failed) return done;
            if (err != null) { failed = true; return done + ". " + err; }
            return done + L.S(" and saved to history.", " og gemt i historikken.") + FailedSavedNote(rec);
        }

        /// The shared end of every export (ExportQuote, ExportRecordTo, ExportGroup): copies the text (Text) or the
        /// picture (Copy), or writes the PNG / PDF. A printed PDF is always light: pdfPages (A4 pages), null = render(false)
        /// as one page. An invoice PDF also gets its copy in the Invoices folder (copyName != null: SaveInvoiceCopy).
        /// Returns the "✓ …" text; on an error failed = true and the error text ("Clipboard: " first for Copy / Text).
        string WriteExport(QuoteExport kind, bool invoice, bool dark, string path, Func<string> text, Func<bool, Bitmap> render,
                           Func<List<Bitmap>> pdfPages, Func<string> copyName, out string copyNote, out bool failed)
        {
            failed = false;
            copyNote = null;
            try
            {
                if (kind == QuoteExport.Text)
                {
                    CopyText(text());
                    return invoice ? L.S("✓ Invoice text copied", "✓ Fakturaen er kopieret som tekst") : L.S("✓ Quote text copied", "✓ Tilbuddet er kopieret som tekst");
                }
                if (kind == QuoteExport.Pdf)
                {
                    var pages = pdfPages != null ? pdfPages() : new List<Bitmap> { render(false) };
                    try
                    {
                        byte[] pdf = QuotePage.Pdf(pages);
                        File.WriteAllBytes(path, pdf);
                        if (copyName != null) copyNote = SaveInvoiceCopy(pdf, copyName());
                    }
                    finally { foreach (var b in pages) b.Dispose(); }
                    return pages.Count > 1 ? L.F("✓ Saved {0} ({1} pages)", "✓ Gemt {0} ({1} sider)", Path.GetFileName(path), pages.Count)
                                           : L.F("✓ Saved {0}", "✓ Gemt {0}", Path.GetFileName(path));
                }
                using (var bmp = render(dark))
                {
                    if (kind == QuoteExport.Copy) CopyPicture(bmp);
                    else bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                return kind == QuoteExport.Copy
                    ? (invoice ? L.S("✓ Invoice picture copied", "✓ Fakturaen er kopieret som billede") : L.S("✓ Quote picture copied", "✓ Tilbuddet er kopieret som billede"))
                    : L.F("✓ Saved {0}", "✓ Gemt {0}", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                failed = true;
                return (kind == QuoteExport.Copy || kind == QuoteExport.Text ? L.S("Clipboard: ", "Udklipsholder: ") : "") + ex.Message;
            }
        }

        /// An invoice export without a customer name.
        static string NeedCustomerText()
        {
            return L.S("Type the customer's name first: it goes on the invoice.", "Skriv kundens navn først: det kommer på fakturaen.");
        }

        /// After saving a quote marked Failed: a second line saying what that means for new quotes ("" otherwise).
        string FailedSavedNote(QuoteRecord r)
        {
            if (r == null || !r.Failed) return "";
            if (settings.FailedPayback == 0)
                return "\n" + L.S("Marked as failed in the history.", "Markeret som fejlet i historikken.");
            // Num.Kr already ends in "kr.": no extra full stop after it
            return "\n" + L.F("Marked as failed: new quotes earn back {0}", "Markeret som fejlet: nye tilbud tjener {0} ind.",
                              Num.Kr(History.FailedCost(r)).Replace(' ', ' '));
        }

        /// Default file name of a quote: "2026-11-04 3D Benchy" (+ " - Mette Hansen" with a customer).
        string QuoteFileName(DateTime date, string customer)
        {
            return QuoteRows.FileName(date, QuoteModelName(), customer);
        }

        /// The model's name for the quote: the job's model name, else "3D print".
        string QuoteModelName()
        {
            // issue #8: "3D Benchy (150 %)" when the job is scaled (quote / invoice name, title, file name)
            string name = QuoteRows.ModelName(jobModel);
            string scale = ScaleLabel(jobScale);
            // batch & plates: "4 × 3D Benchy (150 %)" for 2+ copies
            return History.CopiesName(scale.Length == 0 ? name : name + " (" + scale + ")", jobCopies);
        }

        /// Short link for the quote (QuoteRows.ShortLink with Printables' link parser).
        static string ShortLink(string url)
        {
            return QuoteRows.ShortLink(url, Printables.ParseId);
        }

        /// Everything the quote page shows, from the quote on screen (lastInput / last) and the settings.
        QuoteData BuildQuote(bool friends, DateTime date, string number, string customer)
        {
            var wear = settings.AutoWearRate != 0 ? WearBreakdown(settings) : null;
            string plate = QuotePlate();
            var d = new QuoteData
            {
                Name = (settings.QuoteName ?? "").Trim(),
                Customer = (customer ?? "").Trim(),
                Model = QuoteModelName(),
                Link = ShortLink(LinkSource()),
                Picture = modelImage,
                Logo = Logo,
                Hours = lastInput.Hours, Minutes = lastInput.Minutes,
                Weight = Num.Fmt(lastInput.FilamentGrams) + " g",
                Printer = PrinterText(),
                Number = number, Date = date, ValidUntil = settings.QuoteValidUntil(date),
                PlateName = plate,
                PurgeGrams = filaments != null ? QuotePurge(jobPurge, jobTowerGrams) : -1,
                TowerGrams = filaments != null && settings.QuoteShowTower == 1 ? jobTowerGrams : -1,   // issue #8 step 3
                Breakdown = Pricing.Breakdown(lastInput, settings, friends, wear)
            };
            // batch & plates: Quantity row, "… on 2 plates", the price per piece on the total box
            d.Quantity = Math.Max(1, jobCopies);
            d.Plates = Math.Max(1, jobPlates);
            d.EachLine = QuoteRows.EachLine(d.Breakdown.Total, d.Quantity, settings.QuoteShowUnitPrice != 0);
            var used = filaments ?? (single != null ? new List<FilamentUse> { single } : null);
            if (used != null)
                foreach (var f in used)
                {
                    d.MaterialNames.Add(MaterialName(f));
                    d.MaterialColours.Add(ParseColour(f.Colour));
                }
            else d.MaterialNames.Add(BambuSlicer.ShortName(settings.FilamentPreset).Trim());
            d.Material = QuoteRows.MaterialLine(filaments != null ? filaments.Select(f => f.Type) : null,
                                                filaments != null ? filaments.Count : 0, d.MaterialNames[0]);
            NameByColour(d.MaterialNames, d.MaterialColours);
            return d;
        }

        /// The build plate's short name for the wear line: "Smooth PEI / High Temp Plate" -> "Smooth PEI" ("Glat PEI").
        string QuotePlate()
        {
            return QuotePlate(settings.BedType);
        }

        /// The same for a given bed type (a saved record's BedType; "" = today's Settings).
        string QuotePlate(string bedType)
        {
            if (string.IsNullOrWhiteSpace(bedType)) bedType = settings.BedType;
            string plate = Wear.FindPlate(bedType).Label;
            int cut = plate.IndexOf(" / ", StringComparison.Ordinal);
            if (cut > 0) plate = plate.Substring(0, cut);
            if (L.Danish && plate == "Smooth PEI") plate = "Glat PEI";
            return plate;
        }

        string PrinterText()
        {
            return PrinterText(null);
        }

        /// "<printer name>, 0.4 mm nozzle": the active printer, or a saved quote's printer (its saved name; the nozzle saved
        /// with it, else (older records) the nozzle of that printer today, if it still exists).
        string PrinterText(QuoteRecord r)
        {
            var p = r != null ? settings.FindPrinter(History.PrinterOf(r)) : null;
            string name = r != null && !string.IsNullOrEmpty(r.PrinterName) ? r.PrinterName : p != null ? p.Name : settings.PrinterName;
            string savedNozzle = r != null ? (r.Nozzle ?? "").Trim() : "";
            var nozzle = Wear.FindNozzle(savedNozzle.Length > 0 ? savedNozzle : p != null ? p.Nozzle : settings.Nozzle);
            return L.F("{0}, {1} mm nozzle", "{0}, {1} mm dyse", AppSettings.CleanPrinterName(name),
                       L.Danish ? nozzle.Size.Replace('.', ',') : nozzle.Size);
        }

        // ---------- quote / invoice of a saved record (History's Quote… / Invoice…) ----------

        /// The quote window for a history record, with its saved prices. Exports put the typed name and the chosen
        /// price (For) on the record and save the history; Invoice also makes the invoice (History.MakeInvoice: number,
        /// date, Printed). Nothing new is added to the history.
        internal void ShowRecordQuote(IWin32Window owner, QuoteRecord r, bool invoice)
        {
            if (r == null) return;
            using (var f = NewRecordQuoteForm(r, invoice)) f.ShowDialog(owner);
        }

        QuoteForm NewRecordQuoteForm(QuoteRecord r, bool invoice)
        {
            EnsureHistory();
            bool owned;
            Image pic = RecordPicture(r, out owned);
            // a made invoice keeps the name it was made out to (BilledTo, else Customer for older invoices)
            bool made = invoice && InvoiceMade(r);
            string help = made
                ? L.F("Invoice {0} is already made and stays as it is.",
                      "Faktura {0} er allerede lavet og forbliver uændret.", r.InvoiceNo)
                : invoice
                ? L.S("Making the invoice (copy or save) gives it a number and marks it as printed in the history.",
                      "Når fakturaen laves (kopi eller gem), får den et nummer og markeres som printet i historikken.")
                : L.S("Copying or saving puts the name and price on this quote in the history.",
                      "Når du kopierer eller gemmer, kommer navn og pris på dette tilbud i historikken.");
            string billTo = made ? History.InvoiceInfo(r, settings).BilledTo : r.Customer ?? "";
            QuoteForm f = null;
            f = new QuoteForm(RecordModelName(r), billTo, r.For == History.ForFriends, settings.QuoteDark != 0,
                History.RecentCustomers(history, 10), r.OthersPrice, r.FriendsPrice,
                (friends, name) => BuildRecordQuote(r, friends, name, invoice, pic),
                (IWin32Window o, QuoteExport kind, bool friends, bool dark, string customer, out bool failed) =>
                {
                    bool before = InvoiceMade(r);
                    string msg = ExportRecord(o, r, invoice, kind, friends, dark, customer, pic, invoice ? (PrintState?)null : f.PrintStatus, out failed);
                    // made now: the name it was made out to stays (as the main window's Show invoice…); the status box
                    // shows MakeInvoice's Printed tick
                    if (invoice && !before && InvoiceMade(r)) { f.LockInvoice(BilledFriends(r)); f.PrintStatus = History.PrintStateOf(r, DateTime.Now); }
                    return msg;
                },
                (friends, dark) =>
                {
                    settings.QuoteDark = dark ? 1 : 0;   // the price choice belongs to the record (For), not to Settings
                    if (!offline) settings.Save();
                },
                invoice, help);
            if (made) f.LockInvoice(BilledFriends(r));
            // the record's status in History (quote and invoice, issue #7); a pick is saved at once
            f.PrintStatus = History.PrintStateOf(r, DateTime.Now);
            f.StatusPicked += delegate { SaveStatusNow(f, new List<QuoteRecord> { r }); };
            if (invoice && r.Failed)
                f.ShowNote(help + "\n" + L.S("This print is marked as failed in the history.", "Dette print er markeret som fejlet i historikken."),
                           InfoKind.Warning);
            if (owned && pic != null) f.FormClosed += delegate { pic.Dispose(); };
            return f;
        }

        /// The record has an invoice number (Invoice… re-issues it: same number, date, snapshot and name).
        static bool InvoiceMade(QuoteRecord r)
        {
            return r != null && !string.IsNullOrEmpty(r.InvoiceNo);
        }

        /// The price a made invoice was billed at (A4): the record's For (set when it was made; History keeps it locked).
        static bool BilledFriends(QuoteRecord r)
        {
            return r != null && r.For == History.ForFriends;
        }

        /// The same for a combined invoice: friends only when every job was billed at the friends price.
        static bool BilledFriends(List<QuoteRecord> jobs)
        {
            return jobs != null && jobs.Count > 0 && jobs.All(r => BilledFriends(r));
        }

        /// The record's picture: the one saved with it (issue #4), else the model on screen when it is the same link/file,
        /// else a local .3mf's own picture.
        Image RecordPicture(QuoteRecord r, out bool owned)
        {
            owned = false;
            var stored = StoredPicture(r);
            if (stored != null) { owned = true; return stored; }
            string src = (r.Source ?? "").Trim();
            if (src.Length > 0 && jobLoaded && modelImage != null && string.Equals(src, LinkSource().Trim(), StringComparison.OrdinalIgnoreCase))
                return modelImage;
            string file = src.Trim('"');
            if (offline || !file.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                if (!File.Exists(file)) return null;
                var bytes = BambuSlicer.FromProject(file);
                if (bytes == null) return null;
                owned = true;
                return PrepareModelPicture(bytes);
            }
            catch { owned = false; return null; }
        }

        static string RecordModelName(QuoteRecord r)
        {
            // issue #8: "3D Benchy (150 %)"; batch & plates: "4 × 3D Benchy (150 %)"
            return History.WithCopies(History.WithScale(QuoteRows.ModelName(r.Model), r), r);
        }

        /// The page of a saved record, as it was made: its saved inputs, prices and wear split (History.RecordBreakdown),
        /// plate, nozzle, material name, valid-until and from-name saved with it (older records: today's settings for
        /// those, as before); quote number and date from SavedAt; invoice: its number and date (or the next number and
        /// today while it isn't made yet), due date and the payment ways (its snapshot, else Settings).
        QuoteData BuildRecordQuote(QuoteRecord r, bool friends, string customer, bool invoice, Image pic)
        {
            DateTime saved;
            if (!History.TryTime(r.SavedAt, out saved)) saved = DateTime.Now;
            var quote = History.RecordQuoteInfo(r, settings, saved);
            var d = new QuoteData
            {
                Name = quote.FromName,
                Customer = (customer ?? "").Trim(),
                Model = RecordModelName(r),
                Link = ShortLink(r.Source),
                Picture = pic,
                Logo = Logo,
                Hours = r.Hours, Minutes = r.Minutes,
                Weight = Num.Fmt(r.Grams) + " g",
                Printer = PrinterText(r),
                Number = History.QuoteNumber(r), Date = saved, ValidUntil = quote.ValidUntil,
                PlateName = QuotePlate(r.BedType),
                PurgeGrams = r.Filaments != null && r.Filaments.Count >= 2 ? QuotePurge(r.PurgeGrams, r.TowerGrams) : -1,
                TowerGrams = settings.QuoteShowTower == 1 && History.ShowsTower(r) ? r.TowerGrams : -1,   // issue #8 step 3
                // N4: the saved price (as Copy text and the History list), with the saved wear split as sub-rows
                Breakdown = History.RecordBreakdown(r, settings, friends)
            };
            FillRecordMaterial(d, r);
            // batch & plates: the record's copies and plates
            d.Quantity = History.QuantityOf(r);
            d.Plates = History.PlatesOf(r);
            d.EachLine = QuoteRows.EachLine(d.Breakdown.Total, d.Quantity, settings.QuoteShowUnitPrice != 0);
            if (invoice)
            {
                DateTime billed = DateTime.MinValue;
                bool made = !string.IsNullOrEmpty(r.InvoiceNo) && History.TryTime(r.BilledAt, out billed);
                if (!made) billed = DateTime.Now;
                d.Invoice = true;
                d.InvoiceNo = made ? r.InvoiceNo : L.S("(new)", "(ny)");   // the random code is drawn when the invoice is made
                d.Date = billed;
                // N3: a made invoice keeps what it said then (its snapshot); older ones and new ones use today's settings
                var inv = History.InvoiceInfo(r, settings, billed);
                d.Due = inv.Due;
                d.PayMobilePay = (inv.MobilePay ?? "").Trim();
                d.PayBank = (inv.Bank ?? "").Trim();
                d.Name = (inv.FromName ?? "").Trim();
                DateTime paidOn;   // paid-tick: "Paid 05-11-2026 – thank you" instead of "Please pay by …"
                if (made && History.PaidOn(r, out paidOn)) d.PaidLine = History.PaidText(paidOn);
            }
            return d;
        }

        /// paid-tick: a combined invoice is paid when every job is; the latest paid date ("" = not paid).
        static string GroupPaidLine(List<QuoteRecord> jobs) { return QuoteRows.PaidLine(jobs); }

        /// The record's material names / colours (by filament line) and the details' Material line ("PLA, 4 colours").
        void FillRecordMaterial(QuoteData d, QuoteRecord r)
        {
            // a typed job (no slice): its one line has no name; the filament preset saved with it, as the fresh quote showed
            string savedMaterial = BambuSlicer.ShortName(r.MaterialName ?? "").Trim();
            bool typedLine = r.Filaments != null && r.Filaments.Count == 1 && string.IsNullOrEmpty(r.Filaments[0].Name)
                             && string.IsNullOrEmpty(r.Filaments[0].Type);
            if (r.Filaments != null)
                for (int k = 0; k < r.Filaments.Count; k++)
                {
                    var f = r.Filaments[k];
                    d.MaterialNames.Add(typedLine && savedMaterial.Length > 0 ? savedMaterial
                        : MaterialName(new FilamentUse { Slot = k + 1, Profile = f.Name ?? "", Type = f.Type ?? "", Colour = f.Colour ?? "" }));
                    d.MaterialColours.Add(ParseColour(f.Colour));
                }
            else d.MaterialNames.Add(savedMaterial.Length > 0 ? savedMaterial : BambuSlicer.ShortName(settings.FilamentPreset).Trim());
            d.Material = QuoteRows.MaterialLine(r.Filaments != null ? r.Filaments.Select(f => f.Type) : null,
                                                r.Filaments != null ? r.Filaments.Count : 0, d.MaterialNames[0]);
            NameByColour(d.MaterialNames, d.MaterialColours);
        }

        /// One export of a record's quote / invoice: file dialog first (null = cancelled), then the record gets the name
        /// and For (and the invoice number), the history is saved, the page is drawn (PDF always light) and copied / written.
        string ExportRecord(IWin32Window owner, QuoteRecord r, bool invoice, QuoteExport kind, bool friends, bool dark,
                            string customer, Image pic, PrintState? state, out bool failed)
        {
            failed = false;
            customer = (customer ?? "").Trim();
            if (kind == QuoteExport.History)
            {
                if (state.HasValue) History.SetPrintState(r, state.Value, DateTime.Now);   // the quote window's print status
                // "Save to history" of a saved record: only its name and price (For); an invoice is not made by this.
                // A made invoice's box shows its kept name (read-only): the record's Customer stays as it is.
                if (!(invoice && InvoiceMade(r))) r.Customer = customer;
                if (!InvoiceMade(r)) r.For = friends ? History.ForFriends : History.ForCustomer;   // A4: an invoice keeps its price
                string e = offline ? null : History.Save(history);
                if (e != null) { failed = true; return e; }
                return L.S("✓ Saved to history.", "✓ Gemt i historikken.") + FailedSavedNote(r);
            }
            if (invoice && customer.Length == 0)
            {
                failed = true;
                return NeedCustomerText();
            }
            DateTime now = DateTime.Now;
            string path = null, wanted = null;
            if (kind == QuoteExport.Png || kind == QuoteExport.Pdf)
            {
                if (invoice)
                {
                    // "2026-48213 - Name" (issue #6): a made invoice's number and kept name; a new one's number is picked
                    // now and only used once the file is saved (a cancelled dialog uses none)
                    wanted = InvoiceCodeFor(r.InvoiceNo, now);
                    path = AskExportPath(owner, kind, true, QuoteRows.InvoiceFileName(wanted, InvoiceName(r, customer)));
                }
                else
                {
                    // "2026-11-02 Model - Name": the quote's date
                    DateTime qd;
                    if (!History.TryTime(r.SavedAt, out qd)) qd = DateTime.Now;
                    path = AskExportPath(owner, kind, false, QuoteRows.FileName(qd, RecordModelName(r), customer));
                }
                if (path == null) return null;
            }
            return ExportRecordTo(r, invoice, kind, friends, dark, customer, pic, state, path, wanted, now, out failed);
        }

        /// The rest of ExportRecord once the file (path) is chosen: name, For, print state, invoice number (wantedNo = the
        /// number in the file name, null = any new one), save, then the page copied / written (an invoice PDF also gets its
        /// copy in the Invoices folder). Also the first export of the main window's "Show invoice…" (ExportJobInvoice).
        string ExportRecordTo(QuoteRecord r, bool invoice, QuoteExport kind, bool friends, bool dark, string customer,
                              Image pic, PrintState? state, string path, string wantedNo, DateTime now, out bool failed)
        {
            failed = false;
            if (!(invoice && InvoiceMade(r))) r.Customer = customer;   // MakeInvoice stores a new invoice's name from it (BilledTo)
            // A4: a made invoice keeps the price it was billed at (same number, same amount), whatever the window says
            if (invoice && InvoiceMade(r)) friends = BilledFriends(r);
            if (!InvoiceMade(r)) r.For = friends ? History.ForFriends : History.ForCustomer;
            if (state.HasValue && !invoice) History.SetPrintState(r, state.Value, now);   // the quote window's print status
            string err = null, copyNote = null;
            if (invoice)
            {
                History.MakeInvoice(history, r, now, settings, wantedNo);   // random code (never given twice: settings.UsedInvoiceNos), date, Printed
                // issue #7: the status chosen in the job's invoice window (state given only for its first export; a saved
                // record's window saves its status at once and keeps MakeInvoice's Printed tick)
                if (state.HasValue) History.SetPrintState(r, state.Value, now);
                if (!offline) settings.Save();
            }
            if (!offline) err = History.Save(history);
            string done = WriteExport(kind, invoice, dark, path, () => RecordText(r, friends, invoice),
                                      d => QuotePage.Render(BuildRecordQuote(r, friends, customer, invoice, pic), d), null,
                                      invoice ? (Func<string>)(() => QuoteRows.InvoiceFileName(r.InvoiceNo, InvoiceName(r, customer))) : null,
                                      out copyNote, out failed);
            if (failed) return done;
            if (err != null) { failed = true; return done + ". " + err; }
            return done + (invoice ? L.F(". Invoice no. {0} is in the history.", ". Faktura nr. {0} er gemt i historikken.", r.InvoiceNo)
                                   : L.S(" and saved to history.", " og gemt i historikken."))
                        + (copyNote != null ? "\n" + copyNote : "");
        }

        // ---------- invoice file names and copies (issue #6) ----------

        /// The folder every saved invoice PDF is copied to: <settings folder>\Invoices (the test folder in test mode).
        internal static string InvoicesFolder
        {
            get { return Path.Combine(AppSettings.Folder, "Invoices"); }
        }

        /// The number an invoice file is named after, picked before the save dialog: a made invoice's own (madeNo), else
        /// a free new code (History.NextInvoiceNo). Kept only in memory: a cancelled dialog uses no number; after a save
        /// it goes to History.MakeInvoice(…, wantedNo), which takes it while it is still free.
        string InvoiceCodeFor(string madeNo, DateTime now)
        {
            if (!string.IsNullOrEmpty(madeNo)) return madeNo.Trim();
            EnsureHistory();
            return History.NextInvoiceNo(history, now.Year, settings.UsedInvoiceNos);
        }

        /// The name on an invoice: a made one's kept name (BilledTo), else the typed name.
        static string InvoiceName(QuoteRecord r, string customer)
        {
            return r != null && !string.IsNullOrWhiteSpace(r.BilledTo) ? r.BilledTo.Trim() : (customer ?? "").Trim();
        }

        /// Keeps a copy of a saved invoice PDF in InvoicesFolder as "<name>.pdf" (Settings "Keep a copy of every invoice
        /// PDF", AppSettings.InvoiceCopies; never in picture mode). Never overwrites: a file with the same bytes is already
        /// the copy (nothing written), a different one -> "<name> (2).pdf", "(3)", …. Returns null, or the line for the
        /// status text when the copy failed (the real save has worked: not an error).
        string SaveInvoiceCopy(byte[] pdf, string name)
        {
            if (offline || pdf == null || settings.InvoiceCopies == 0) return null;
            try
            {
                string dir = InvoicesFolder;
                Directory.CreateDirectory(dir);
                for (int n = 1; n <= 999; n++)
                {
                    string target = Path.Combine(dir, name + (n == 1 ? "" : " (" + n + ")") + ".pdf");
                    if (File.Exists(target))
                    {
                        if (SameBytes(target, pdf)) return null;   // already kept
                        continue;
                    }
                    using (var fs = new FileStream(target, FileMode.CreateNew, FileAccess.Write))   // CreateNew: never over another file
                        fs.Write(pdf, 0, pdf.Length);
                    return null;
                }
                throw new IOException(L.S("too many copies with this name", "for mange kopier med dette navn"));
            }
            catch (Exception ex)
            {
                return L.F("The copy in the Invoices folder couldn't be saved: {0}",
                           "Kopien i mappen Invoices kunne ikke gemmes: {0}", ex.Message);
            }
        }

        static bool SameBytes(string path, byte[] data)
        {
            var info = new FileInfo(path);
            if (info.Length != data.Length) return false;
            byte[] have = File.ReadAllBytes(path);
            for (int i = 0; i < have.Length; i++) if (have[i] != data[i]) return false;
            return true;
        }

        /// A record as plain text: title, name, model, time and grams, the chosen price; invoice: number, dates, payment.
        string RecordText(QuoteRecord r, bool friends, bool invoice)
        {
            // paid-tick: the paid line ("Paid 05-11-2026 – thank you") comes from the shared QuoteRows.RecordText
            return WithTowerLine(QuoteRows.RecordText(r, friends, invoice, settings, ShortLink(r.Source)),
                                 settings.QuoteShowTower == 1 && History.ShowsTower(r) ? r.TowerGrams : -1);   // issue #8 step 3
        }

        // ---------- combined bill: several History jobs on one quote / invoice (plans\combined-bill, Part A) ----------

        /// History's Quote… / Invoice… for the selected records. One record = the one-job window as before (Invoice… on
        /// a job of a combined invoice opens the whole invoice). Several: failed ones are left off; for an invoice a mix of
        /// billed and unbilled jobs (or two invoices) is refused; all on one invoice = that invoice opened again. Returns
        /// null when a window was shown (or there was nothing to show), else the refusal line for History's info box.
        internal string ShowRecordsQuote(IWin32Window owner, List<QuoteRecord> records, bool invoice)
        {
            QuoteForm f;
            string problem = NewRecordsQuoteForm(records, invoice, out f);
            if (f == null) return problem;
            using (f)
            {
                if (testRecordsForm != null) testRecordsForm(f);   // --test-bill: driven by the test, never shown
                else f.ShowDialog(owner);
            }
            return null;
        }

        /// The window for ShowRecordsQuote (null + the refusal text when it can't open).
        string NewRecordsQuoteForm(List<QuoteRecord> records, bool invoice, out QuoteForm f)
        {
            f = null;
            var sel = records == null ? new List<QuoteRecord>() : records.Where(r => r != null).ToList();
            if (sel.Count == 0) return null;
            EnsureHistory();
            var notes = new List<string>();
            bool warn = false;
            List<QuoteRecord> jobs;
            if (sel.Count == 1)
            {
                var r = sel[0];
                var group = invoice && InvoiceMade(r) ? History.InvoiceGroup(history, r.InvoiceNo) : null;
                if (group == null || group.Count < 2) { f = NewRecordQuoteForm(r, invoice); return null; }
                jobs = group;
                notes.Add(group.Count == 2
                    ? L.F("\"{0}\" is on combined invoice {1} with 1 other print: the whole invoice is shown.",
                          "\"{0}\" er på samlet faktura {1} med 1 andet print: hele fakturaen vises.", RecordModelName(r), r.InvoiceNo.Trim())
                    : L.F("\"{0}\" is on combined invoice {1} with {2} other prints: the whole invoice is shown.",
                          "\"{0}\" er på samlet faktura {1} med {2} andre print: hele fakturaen vises.", RecordModelName(r), r.InvoiceNo.Trim(), group.Count - 1));
            }
            else
            {
                var check = History.BillProblem(sel);
                if (invoice && check.Clash != null)
                    return L.F("\"{0}\" is already on invoice {1}. Pick only unbilled prints, or only that invoice's prints.",
                               "\"{0}\" er allerede på faktura {1}. Vælg kun print uden faktura, eller kun den fakturas print.",
                               RecordModelName(check.Clash), check.ClashCode);
                if (check.Jobs.Count == 0)
                    return L.S("Failed prints can't go on a bill, and all the selected ones failed.",
                               "Fejlede print kan ikke faktureres, og alle de valgte er fejlet.");
                jobs = History.OldestFirst(check.Jobs);
                if (check.FailedLeftOut > 0) { warn = true; notes.Add(FailedLeftOutText(check.FailedLeftOut)); }
                if (invoice && check.Code.Length > 0)
                {
                    var group = History.InvoiceGroup(history, check.Code);
                    if (group.Count > jobs.Count)
                        notes.Add(L.F("Invoice {0} has {1} prints: the whole invoice is shown.", "Faktura {0} har {1} print: hele fakturaen vises.",
                                      check.Code, group.Count));
                    if (group.Count >= jobs.Count) jobs = group;
                }
                if (jobs.Count == 1)
                {
                    f = NewRecordQuoteForm(jobs[0], invoice);
                    foreach (var n in notes) f.AddNote(n, warn ? InfoKind.Warning : InfoKind.Info);
                    return null;
                }
            }
            try { f = NewGroupQuoteForm(jobs, invoice, notes, warn, null); }
            catch (Exception ex)
            {
                // a record with an absurd number (e.g. a hand-edited history.json): the bill can't be worked out
                if (!(ex is OverflowException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                f = null;
                return L.S("These prints can't go on one bill: one of them has a price or amount too large to work with. Check their time and grams.",
                           "Disse print kan ikke komme på én regning: et af dem har en pris eller mængde, der er for stor. Tjek deres tid og gram.");
            }
            return null;
        }

        static string FailedLeftOutText(int n)
        {
            return n == 1 ? L.S("1 failed print is left off the bill.", "1 fejlet print er udeladt af regningen.")
                          : L.F("{0} failed prints are left off the bill.", "{0} fejlede print er udeladt af regningen.", n);
        }


        /// The jobs' names (trimmed, not empty), the most used first (same count: the first seen first).
        static List<string> JobNames(List<QuoteRecord> jobs)
        {
            var count = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            var order = new List<string>();
            foreach (var r in jobs)
            {
                string c = (r.Customer ?? "").Trim();
                if (c.Length == 0) continue;
                int k;
                if (count.TryGetValue(c, out k)) count[c] = k + 1;
                else { count[c] = 1; order.Add(c); }
            }
            return order.OrderByDescending(c => count[c]).ToList();   // OrderBy is stable: ties keep the first-seen order
        }

        /// The combined window: title "Invoice – 3 prints", the most used name (a made invoice: its kept name, read-only),
        /// both totals on the price choices, notes (different names, failed left out, whole invoice) under the help.
        /// samplePics: picture mode's pictures (null = each job's own, RecordPicture).
        QuoteForm NewGroupQuoteForm(List<QuoteRecord> jobs, bool invoice, List<string> notes, bool warn, Image[] samplePics)
        {
            int n = jobs.Count;
            bool made = invoice && jobs.All(r => InvoiceMade(r));
            var pics = new Image[n];
            var owned = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (samplePics != null) pics[i] = i < samplePics.Length ? samplePics[i] : null;
                else pics[i] = RecordPicture(jobs[i], out owned[i]);
            }
            DateTime when = Screenshots.Active ? new DateTime(2026, 11, 19, 10, 30, 0) : DateTime.Now;   // a combined quote's number and date
            string code = made ? (jobs[0].InvoiceNo ?? "").Trim() : "";
            string help = made
                ? L.F("Invoice {0} ({1} prints) is already made and stays as it is.",
                      "Faktura {0} ({1} print) er allerede lavet og forbliver uændret.", code, n)
                : invoice
                ? L.F("Making the invoice (copy or save) gives all {0} prints one number and marks them as printed in the history.",
                      "Når fakturaen laves (kopi eller gem), får alle {0} print ét nummer og markeres som printet i historikken.", n)
                : L.F("Copying or saving puts the name and price on these {0} quotes in the history.",
                      "Når du kopierer eller gemmer, kommer navn og pris på disse {0} tilbud i historikken.", n);
            var names = JobNames(jobs);
            string billTo = made ? History.InvoiceInfo(jobs[0], settings).BilledTo : names.Count > 0 ? names[0] : "";
            if (!made && names.Count > 1)
            {
                warn = true;
                string list = string.Join(", ", names.Take(3)) + (names.Count > 3 ? ", …" : "");
                notes.Add(L.F("Different names on these prints ({0}): the name you type goes on all of them.",
                              "Forskellige navne på disse print ({0}): navnet, du skriver, kommer på dem alle.", list));
            }
            bool friends = jobs.All(r => r.For == History.ForFriends);
            double customerTotal = History.GroupBill(jobs, settings, false).Total;
            double friendsTotal = History.GroupBill(jobs, settings, true).Total;
            QuoteForm f = null;
            f = new QuoteForm(QuoteRows.PrintsText(n), billTo, friends, settings.QuoteDark != 0,
                History.RecentCustomers(history, 10), customerTotal, friendsTotal,
                (fr, name) => BuildGroupQuote(jobs, fr, name, invoice, pics, when),
                (IWin32Window o, QuoteExport kind, bool fr, bool dark, string customer, out bool failed) =>
                {
                    bool before = jobs.All(r => InvoiceMade(r));
                    string msg = ExportGroup(o, jobs, invoice, kind, fr, dark, customer, pics, when, out failed);
                    // made now: the name it was made out to stays (as the main window's Show invoice…); the status box
                    // shows MakeInvoice's Printed ticks
                    if (invoice && !before && jobs.All(r => InvoiceMade(r))) { f.LockInvoice(BilledFriends(jobs)); ShowGroupStatus(f, jobs); }
                    return msg;
                },
                (fr, dark) =>
                {
                    settings.QuoteDark = dark ? 1 : 0;   // the price choice belongs to the records (For), not to Settings
                    if (!offline) settings.Save();
                },
                invoice, help);
            if (made) f.LockInvoice(BilledFriends(jobs));
            // issue #7: one status box for all of them ("Mixed" when they differ); a pick is saved on every job at once
            ShowGroupStatus(f, jobs);
            f.StatusPicked += delegate { SaveStatusNow(f, jobs); };
            foreach (var note in notes) f.AddNote(note, warn ? InfoKind.Warning : InfoKind.Info);
            f.Disposed += delegate { for (int i = 0; i < n; i++) if (owned[i] && pics[i] != null) pics[i].Dispose(); };
            return f;
        }

        /// A combined window's status box: their one state, or "Mixed" when they differ.
        static void ShowGroupStatus(QuoteForm f, List<QuoteRecord> jobs)
        {
            DateTime now = DateTime.Now;
            var states = jobs.Select(r => History.PrintStateOf(r, now)).Distinct().ToList();
            f.SetGroupStatus(jobs.Count, states.Count == 1 ? (PrintState?)states[0] : null);
        }

        /// Issue #7: a status picked in a saved record's window (quote or invoice, one job or a combined window) is saved
        /// at once on every record (History.SetPrintState, one Save). Several jobs to "Failed" asks first (the user's
        /// choice); No = the box goes back. The ✓ / error line shows in the window's info box.
        void SaveStatusNow(QuoteForm f, List<QuoteRecord> recs)
        {
            if (recs == null || recs.Count == 0) return;
            var state = f.PrintStatus;
            DateTime now = DateTime.Now;
            Action back = () => { if (recs.Count > 1) ShowGroupStatus(f, recs); else f.PrintStatus = History.PrintStateOf(recs[0], now); };
            if (state == PrintState.Failed && recs.Count(r => !r.Failed) > 1)
                using (var ask = HistoryForm.NewFailedAsk(recs.Count(r => !r.Failed)))
                    if (ask.ShowDialog(f) != DialogResult.Yes) { back(); return; }
            foreach (var r in recs) History.SetPrintState(r, state, now);
            string e = offline ? null : History.Save(history);
            if (e != null) { f.ShowNote(e, InfoKind.Error); return; }
            back();   // "Printing now" that is already over reads as Printed
            string text = L.F("✓ Print status saved: {0}.", "✓ Printstatus gemt: {0}.", QuoteRows.StateName(state));
            if (state == PrintState.Failed) text += FailedSavedNote(recs[0]);
            f.ShowNote(text, InfoKind.Success);
        }

        /// Names and colour dots of GroupBreakdownInfo.Materials (by the material lines' Index).
        void FillGroupMaterials(List<string> names, List<Color> colours, List<HistoryFilament> materials)
        {
            foreach (var m in materials)
            {
                bool unnamed = string.IsNullOrEmpty(m.Name) && string.IsNullOrEmpty(m.Type);
                // older quotes saved without a filament name: Settings' filament, as their single page shows
                names.Add(unnamed ? BambuSlicer.ShortName(settings.FilamentPreset ?? "").Trim() : MaterialName(new FilamentUse { Profile = m.Name ?? "", Type = m.Type ?? "", Colour = m.Colour ?? "" }));
                colours.Add(ParseColour(m.Colour));
            }
            NameByColour(names, colours);
        }

        /// qol 8: material rows with the same name get their colour's name ("Bambu PLA Basic (Red)", FilePickerForm.
        /// ColourName), so 4 rows of one profile can be told apart. Unique names and rows without a colour stay as they are.
        static void NameByColour(List<string> names, List<Color> colours)
        {
            var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in names) { int c; count.TryGetValue(n ?? "", out c); count[n ?? ""] = c + 1; }
            for (int i = 0; i < names.Count && i < colours.Count; i++)
            {
                if (count[names[i] ?? ""] < 2) continue;
                string colour = FilePickerForm.ColourName(colours[i]);
                if (colour.Length > 0) names[i] = (names[i] ?? "") + " (" + colour + ")";
            }
        }

        /// The page of a combined bill: one card per job (QuoteJob: picture, name, quote no. / date, print time, weight, its
        /// print price = History.GroupBill's line + its share of any top-up, QuoteRows.JobOre, so the cards add up to the
        /// total), the printer / plate when shared, the whole bill's breakdown (History.GroupBreakdown; its minimum line folded
        /// into Work & handling when drawn: the minimum is never shown), the total. TopUpLabel / TopUpAmount are data for the
        /// checks only. Quote: number / date / valid-until from when, today's name; invoice: its code, date and
        /// snapshot once made (else "(new)" and today's settings).
        QuoteData BuildGroupQuote(List<QuoteRecord> jobs, bool friends, string customer, bool invoice, Image[] pics, DateTime when)
        {
            var bill = History.GroupBill(jobs, settings, friends);
            var d = new QuoteData
            {
                Name = (settings.QuoteName ?? "").Trim(),
                Customer = (customer ?? "").Trim(),
                Logo = Logo,
                Number = History.QuoteNumber(when), Date = when, ValidUntil = settings.QuoteValidUntil(when),
                Summary = QuoteRows.GroupSummary(jobs),
                GroupPlates = QuoteRows.GroupPlates(jobs),   // batch & plates: "Plates  5" when any job has 2+ copies
                GroupFriends = friends,
                GroupTotal = bill.Total,
                Jobs = new List<QuoteJob>()
            };
            string printer = null;
            bool samePrinter = true;
            long[] jobOre = QuoteRows.JobOre(bill);   // with each job's share of any top-up: the minimum is never shown
            for (int i = 0; i < jobs.Count; i++)
            {
                var r = jobs[i];
                var md = new QuoteData();
                FillRecordMaterial(md, r);
                long ore = i < jobOre.Length ? jobOre[i] : 0;
                var job = new QuoteJob
                {
                    Name = RecordModelName(r),
                    Detail = QuoteRows.JobDetail(r, md.Material),
                    Sub = QuoteRows.JobSub(r),
                    Amount = Num.Kr(ore / 100.0),
                    Ore = ore,
                    Picture = pics != null && i < pics.Length ? pics[i] : null,
                    Hours = r.Hours, Minutes = r.Minutes,
                    Weight = Num.Fmt(r.Grams) + " g",
                    // batch & plates: "4 pcs · 2 plates" (2+ copies only)
                    Copies = History.QuantityOf(r) > 1
                        ? History.PiecesText(History.QuantityOf(r)) + (History.PlatesOf(r) > 1 ? " · " + L.F("{0} plates", "{0} plader", History.PlatesOf(r)) : "")
                        : ""
                };
                d.Jobs.Add(job);
                string pt = PrinterText(r);
                if (printer == null) printer = pt;
                else if (pt != printer) samePrinter = false;
            }
            d.Printer = samePrinter ? printer ?? "" : "";
            // the whole bill's price breakdown (lines sum exactly to the bill's total; its minimum line is folded into
            // Work & handling when drawn, QuoteRows.FoldMinimum)
            var gb = History.GroupBreakdown(bill, settings);
            d.Breakdown = new QuoteBreakdown { Friends = friends, Lines = gb.Lines, TotalOre = gb.TotalOre, Price = gb.Total };
            d.GroupSameRates = gb.SameRates;
            d.TowerGrams = settings.QuoteShowTower == 1 ? History.TowerGramsSum(jobs) : -1;   // issue #8 step 3: the jobs' towers
            FillGroupMaterials(d.MaterialNames, d.MaterialColours, gb.Materials);
            int minutes = 0;
            string bed = null;
            bool sameBed = true;
            foreach (var r in jobs)
            {
                minutes += Math.Max(0, r.Hours) * 60 + Math.Max(0, r.Minutes);
                // compared by the plate the page shows (an empty saved BedType = Settings' plate, as on its single page)
                string bt = QuotePlate(r.BedType ?? "");
                if (bed == null) bed = bt; else if (!string.Equals(bed, bt, StringComparison.OrdinalIgnoreCase)) sameBed = false;
            }
            d.Hours = minutes / 60;
            d.Minutes = minutes % 60;
            d.PlateName = sameBed && !string.IsNullOrEmpty(bed) ? bed : "";
            if (bill.TopUp > 0)
            {
                // the minimum the top-up reaches (older records saved without it: the total, which is that minimum)
                d.TopUpLabel = QuoteRows.MinimumLabel(bill.Minimum > 0 ? bill.Minimum : bill.Total);
                d.TopUpAmount = Num.Kr(bill.TopUp);
            }
            if (invoice)
            {
                var first = jobs[0];
                DateTime billed = DateTime.MinValue;
                bool made = jobs.All(r => InvoiceMade(r)) && History.TryTime(first.BilledAt, out billed);
                if (!made) billed = Screenshots.Active ? when : DateTime.Now;
                d.Invoice = true;
                d.InvoiceNo = made ? first.InvoiceNo.Trim() : L.S("(new)", "(ny)");
                d.Date = billed;
                var inv = History.InvoiceInfo(first, settings, billed);
                d.Due = inv.Due;
                if (made) d.PaidLine = GroupPaidLine(jobs);   // paid-tick: all jobs paid -> "Paid … – thank you"
                d.PayMobilePay = (inv.MobilePay ?? "").Trim();
                d.PayBank = (inv.Bank ?? "").Trim();
                d.Name = (inv.FromName ?? "").Trim();
            }
            return d;
        }

        /// One export of a combined bill: file dialog first ("2026-11-19 3 prints - Name"; null = cancelled), then every
        /// job gets the name (not on a made invoice: it keeps its name) and For; an invoice is made for all of them at
        /// once (History.MakeInvoice: one code, Printed, prices with the minimum's shares); one Save; then the page(s).
        string ExportGroup(IWin32Window owner, List<QuoteRecord> jobs, bool invoice, QuoteExport kind, bool friends, bool dark,
                           string customer, Image[] pics, DateTime when, out bool failed)
        {
            failed = false;
            customer = (customer ?? "").Trim();
            bool made = invoice && jobs.All(r => InvoiceMade(r));
            if (made) friends = BilledFriends(jobs);   // A4: a made invoice keeps the price it was billed at
            int forWho = friends ? History.ForFriends : History.ForCustomer;
            string prints = QuoteRows.PrintsText(jobs.Count);
            if (kind == QuoteExport.History)
            {
                // only the name and price (For) on each job; never makes an invoice; invoiced jobs keep their For (A4)
                foreach (var r in jobs) { if (!made) r.Customer = customer; if (!InvoiceMade(r)) r.For = forWho; }
                string e = offline ? null : History.Save(history);
                if (e != null) { failed = true; return e; }
                return L.F("✓ Saved to history ({0}).", "✓ Gemt i historikken ({0}).", prints);
            }
            if (invoice && customer.Length == 0)
            {
                failed = true;
                return NeedCustomerText();
            }
            DateTime now = DateTime.Now;
            string path = null, wanted = null;
            if (kind == QuoteExport.Png || kind == QuoteExport.Pdf)
            {
                if (invoice)
                {
                    // "2026-48213 - Name" (issue #6): the number is picked now, used only once the file is saved
                    wanted = InvoiceCodeFor(made ? jobs[0].InvoiceNo : null, now);
                    path = AskExportPath(owner, kind, true, QuoteRows.InvoiceFileName(wanted, made ? InvoiceName(jobs[0], customer) : customer));
                }
                else path = AskExportPath(owner, kind, false, QuoteRows.GroupFileName(when, jobs.Count, customer));
                if (path == null) return null;
            }
            if (invoice && !made)
            {
                var check = History.BillProblem(jobs);
                if (!check.Ok)
                {
                    failed = true;
                    return check.Clash != null
                        ? L.F("\"{0}\" is already on invoice {1}: no invoice was made.", "\"{0}\" er allerede på faktura {1}: der blev ikke lavet en faktura.",
                              RecordModelName(check.Clash), check.ClashCode)
                        : L.S("No prints to invoice.", "Ingen print at fakturere.");
                }
            }
            foreach (var r in jobs) { if (!made) r.Customer = customer; if (!InvoiceMade(r)) r.For = forWho; }   // MakeInvoice takes the name from Customer
            if (!made) History.SetOrder(jobs);   // batch & plates: the jobs of this bill are one order (OrderId)
            string err = null, copyNote = null;
            if (invoice)
            {
                History.MakeInvoice(history, jobs, now, settings, friends, wanted);   // one code for all (the file's), Printed, the minimum's shares
                if (!offline) settings.Save();
            }
            if (!offline) err = History.Save(history);
            QuoteData data = null;   // built once, inside WriteExport (an error there is its error text)
            Func<QuoteData> getData = () => data ?? (data = BuildGroupQuote(jobs, friends, customer, invoice, pics, when));
            string done = WriteExport(kind, invoice, dark, path,
                () =>
                {
                    var bill = History.GroupBill(jobs, settings, friends);
                    var gd = getData();
                    return QuoteRows.GroupText(bill, invoice, invoice ? gd.InvoiceNo : gd.Number, customer, gd.Date,
                                               invoice ? History.InvoiceInfo(jobs[0], settings, gd.Date) : null, settings.QuoteShowUnitPrice != 0);
                },
                d => QuotePage.Render(getData(), d),
                () => QuotePage.RenderPages(getData(), false),   // A4 pages
                invoice ? (Func<string>)(() => QuoteRows.InvoiceFileName(jobs[0].InvoiceNo, InvoiceName(jobs[0], customer))) : null,
                out copyNote, out failed);
            if (failed) return done;
            if (err != null) { failed = true; return done + ". " + err; }
            return done + (invoice ? L.F(". Invoice no. {0} is in the history ({1}).", ". Faktura nr. {0} er gemt i historikken ({1}).", jobs[0].InvoiceNo, prints)
                                   : L.F(" and saved to history ({0}).", " og gemt i historikken ({0}).", prints))
                        + (copyNote != null ? "\n" + copyNote : "");
        }

        /// Picture mode: made-up jobs for a combined bill (copies of HistoryForm.SampleQuotes, never saved): three small
        /// customer prints for Lene Hansen (40 kr. together: a "Minimum price" line), or count jobs for the page-break
        /// check; made = invoiced together (fixed code 2026-51907, 19 Nov 2026).
        internal List<QuoteRecord> SampleGroupJobs(int count, bool made, Dictionary<string, byte[]> pictures, out Image[] pics)
        {
            var all = HistoryForm.SampleQuotes();
            int[] order = { 7, 2, 0, 1, 3, 4, 6 };   // moon lamp, phone stand, benchy first; never the failed one (5)
            var jobs = new List<QuoteRecord>();
            var srcId = new Dictionary<QuoteRecord, string>();
            for (int i = 0; i < count; i++)
            {
                var src = all[order[i % order.Length]];
                var r = History.CopyQuotes(new List<QuoteRecord> { src })[0];
                srcId[r] = src.Id;
                r.Id = "group" + i;
                r.InvoiceNo = r.BilledAt = r.BilledTo = r.InvoiceDue = "";
                r.InvoiceTopUp = 0;
                r.For = History.ForCustomer;
                r.Customer = "Lene Hansen";
                r.Printed = true;
                r.PrintingSince = "";
                DateTime t;
                if (History.TryTime(src.SavedAt, out t)) r.SavedAt = History.Stamp(t.AddDays(16 + i / order.Length));
                jobs.Add(r);
            }
            jobs = History.OldestFirst(jobs);
            pics = new Image[jobs.Count];
            for (int i = 0; i < jobs.Count; i++)
            {
                byte[] b;
                if (pictures != null && pictures.TryGetValue(srcId[jobs[i]], out b)) pics[i] = History.PictureImage(b);
            }
            if (made)
            {
                var s = settings.Clone();
                s.PayMobilePay = "12 34 56 78";
                s.PayBank = L.S("reg. 1234, account 0012345678", "reg.nr. 1234, konto 0012345678");
                var digits = new Queue<int>(new[] { 4, 1, 9, 0, 7 });   // 2026-51907
                History.MakeInvoice(new List<QuoteRecord>(jobs), jobs, new DateTime(2026, 11, 19, 10, 30, 0), s, false,
                                    k => digits.Count > 0 ? digits.Dequeue() : 0);
            }
            return jobs;
        }

        /// Picture mode: the page data of a combined bill of jobs (Lene Hansen, customer prices).
        internal QuoteData SampleGroupData(List<QuoteRecord> jobs, Image[] pics, bool invoice)
        {
            return BuildGroupQuote(jobs, false, "Lene Hansen", invoice, pics, new DateTime(2026, 11, 19, 10, 30, 0));
        }

        /// Picture mode: History's Invoice… window for several jobs (nothing is saved).
        internal QuoteForm SampleGroupWindow(List<QuoteRecord> jobs, Image[] pics, bool invoice)
        {
            if (!offline) return null;
            EnsureHistory();
            var f = NewGroupQuoteForm(jobs, invoice, new List<string>(), false, pics);
            return f;
        }

        /// Picture mode: the invoice page of the job on screen (sample number, date, customer and payment ways; nothing saved).
        internal Bitmap RenderSampleInvoice(bool samplePaid = false)
        {
            if (!offline || last == null || lastInput == null) return null;
            var when = new DateTime(2026, 11, 4, 17, 45, 0);
            var d = BuildQuote(false, when, History.QuoteNumber(when), "Lene Hansen");
            d.Invoice = true;
            d.InvoiceNo = "2026-48213";
            d.Due = settings.InvoiceDue(when);
            d.PayMobilePay = "12 34 56 78";
            d.PayBank = L.S("reg. 1234, account 0012345678", "reg.nr. 1234, konto 0012345678");
            if (samplePaid) d.PaidLine = History.PaidText(new DateTime(2026, 11, 12));   // paid-tick picture
            return QuotePage.Render(d, false);
        }

        /// Picture mode: the main window after "Repeat print" of a record (no slicing offline).
        internal void SampleRepeat(QuoteRecord r) { Repeat(r); }

        /// Picture mode: the quote page of the job on screen (fixed date 4 Nov 2026 17:45, nothing saved).
        internal Bitmap RenderSampleQuote(bool dark, string customer)
        {
            if (!offline || last == null || lastInput == null) return null;
            var when = new DateTime(2026, 11, 4, 17, 45, 0);
            return QuotePage.Render(BuildQuote(settings.QuoteFriends, when, History.QuoteNumber(when), customer), dark);
        }

        /// Picture mode (issue #8 step 3): the job on screen as a light quote with the "Prime tower 1,70 g" note (as with
        /// Settings' "Show prime tower grams on quotes and invoices" ticked; the in-memory settings are put back after).
        internal Bitmap RenderSampleTowerQuote()
        {
            if (!offline || last == null || lastInput == null) return null;
            double keep = settings.QuoteShowTower;
            settings.QuoteShowTower = 1;
            try
            {
                var when = new DateTime(2026, 11, 4, 17, 45, 0);
                return QuotePage.Render(BuildQuote(false, when, History.QuoteNumber(when), "Mette Hansen"), false);
            }
            finally { settings.QuoteShowTower = keep; }
        }

        /// Picture mode: the job on screen as a light customer quote (not friends) for Mette Hansen, fixed date: the single
        /// Benchy is below the minimum price, so its top-up shows inside Work & handling (quote_topup.png).
        internal Bitmap RenderSampleTopUpQuote()
        {
            if (!offline || last == null || lastInput == null) return null;
            var when = new DateTime(2026, 11, 4, 17, 45, 0);
            return QuotePage.Render(BuildQuote(false, when, History.QuoteNumber(when), "Mette Hansen"), false);
        }

        /// Picture mode: the quote window for the job on screen, with a sample customer (fixed date; never saves:
        /// exports in picture mode only write the in-memory history, and the window is never clicked).
        internal QuoteForm SampleQuoteWindow(string customer)
        {
            if (!offline || last == null || lastInput == null) return null;
            var f = NewQuoteForm(customer);
            quoteWhen = new DateTime(2026, 11, 4, 17, 45, 0);
            f.Redraw();
            return f;
        }

        /// Picture mode: Settings' "Show invoice… instead of Show quote…" on / off (in memory, never saved).
        internal void SampleMainInvoice(bool on)
        {
            if (!offline) return;
            settings.MainInvoice = on ? 1 : 0;
            UpdateQuoteButton();
        }

        /// Picture mode: the main window's "Show invoice…" window for the job on screen (nothing is saved).
        internal QuoteForm SampleJobInvoiceWindow(string customer)
        {
            if (!offline || last == null || lastInput == null) return null;
            var f = NewJobInvoiceForm(customer);
            quoteWhen = new DateTime(2026, 11, 4, 17, 45, 0);
            f.Redraw();
            return f;
        }

        /// Picture mode: Invoice… on a made (sample) invoice: the kept name in a read-only box.
        internal QuoteForm SampleMadeInvoiceWindow(QuoteRecord r)
        {
            if (!offline || r == null) return null;
            return NewRecordQuoteForm(r, true);
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
            // the start-up read (StartupPictureClean) when history.json hasn't changed since, else a normal Load
            history = History.LoadFrom(historyAhead, out historyProblem);
            historyAhead = null;
            History.ReservePrinterIds(history, settings);   // a new printer never reuses an id from the history
            if (historyProblem == null && !picturesCleaned) { picturesCleaned = true; History.CleanPictures(history); }   // once per run
        }

        bool picturesCleaned;   // History.CleanPictures ran this run (StartupPictureClean or EnsureHistory)
        History.HistoryReadAhead historyAhead;   // StartupPictureClean's read, used by EnsureHistory if the file is unchanged
        Task startupRead;                        // StartupPictureClean's run (--test-history-read waits for it)

        /// At start (Shown): reads and parses history.json off the UI thread (History.ReadAhead, no Load: that renames bad
        /// files and blocks saving) and keeps it for EnsureHistory, then cleans leftover pictures on the UI thread, only
        /// when the file read fine and the history hasn't been loaded meanwhile (a save loads it first, so a just-saved
        /// picture is never in danger; EnsureHistory cleans then).
        void StartupPictureClean()
        {
            if (offline || picturesCleaned) return;
            startupRead = StartupPictureCleanAsync();
        }

        async Task StartupPictureCleanAsync()
        {
            History.HistoryReadAhead ahead;
            try { ahead = await Task.Run(() => History.ReadAhead()); }
            catch { return; }   // locked / unreadable: EnsureHistory decides later
            if (ahead == null || ahead.List == null || IsDisposed || history != null) return;   // null = not readable: Load later
            historyAhead = ahead;
            if (picturesCleaned) return;
            picturesCleaned = true;
            try { History.CleanPictures(ahead.List); } catch { }
        }

        /// Adds the quote on screen to the history and writes the file. null = OK, else the error text.
        /// The same quote again within 10 minutes replaces the last entry (History.Add).
        string SaveToHistory()
        {
            QuoteRecord saved;
            return SaveToHistory(0, null, out saved);
        }

        /// forWho = History.ForFriends / ForCustomer: a quote export, saved with that For (History.AddFor) and Customer
        /// (empty = none); 0 = the Save button (a reopened quote keeps its For; the same quote saved again keeps its
        /// Customer). saved = the record now in the list (null if there was nothing to save).
        string SaveToHistory(int forWho, string customer, out QuoteRecord saved)
        {
            return SaveToHistory(forWho, customer, null, out saved);
        }

        /// state = the quote window's print status (History.AddFor with a state: it wins over a replaced same quote's);
        /// null = no status (Save button, ▾ quick menu: today's rule).
        string SaveToHistory(int forWho, string customer, PrintState? state, out QuoteRecord saved)
        {
            saved = null;
            if (last == null || lastInput == null) return L.S("Nothing to save yet.", "Der er ikke noget at gemme endnu.");
            EnsureHistory();
            // with the wear split shown on the quote (Auto wear), so History's Quote… / Invoice… show the same rows later
            var r = History.FromQuote(lastInput, last, settings, jobModel ?? "", LinkSource(), DateTime.Now,
                                      settings.AutoWearRate != 0 ? WearBreakdown(settings) : null);
            // issue #8: the size it was sliced at (a repeat print / Open again: the record's, set when it was filled)
            History.SetScale(r, jobScale.X, jobScale.Y, jobScale.Z);   // step 2: per axis (equal axes = uniform)
            // batch & plates: copies, plates, most per plate; typed by hand = an estimate (one copy x N, Pricing.ForCopies)
            History.SetCopies(r, Math.Max(1, lastInput.Quantity), lastInput.Plates, jobPerPlate, jobCopiesTyped && jobCopies > 1);
            // colour, type and material of each row, so "Open again" can rebuild the material rows without slicing
            var used = filaments ?? (single != null ? new List<FilamentUse> { single } : null);
            if (r.Filaments != null && used != null && r.Filaments.Count == used.Count)
            {
                for (int k = 0; k < used.Count; k++)
                {
                    r.Filaments[k].Colour = used[k].Colour ?? "";
                    r.Filaments[k].Type = used[k].Type ?? "";
                    // short preset name ("Bambu PLA Matte", not "… @BBL A1") = MaterialName's key, so prices keep matching
                    r.Filaments[k].Name = string.IsNullOrEmpty(used[k].Profile) ? "" : BambuSlicer.ShortName(used[k].Profile).Trim();
                    r.Filaments[k].TowerGrams = used[k].TowerGrams;   // issue #8 step 3 (-1 = unknown)
                }
                if (filaments != null) r.PurgeGrams = jobPurge;
            }
            // issue #8 step 3: the tower switch it was sliced with and the tower's grams / time (2+ colours only; -1 = unknown)
            History.SetTower(r, jobTower, jobTowerGrams, jobTowerSeconds);   // one colour: the slice's 0 when known
            if (!jobTowerKnown) r.PrimeTower = -1;   // an old record opened again (not sliced): its switch stays unknown
            if (jobParts != null) History.SetParts(r, jobParts, jobTogether);   // idea 21: the filament / colour per file
            else if (repeatOf != null && History.PartsOf(repeatOf) != null)    // a repeat print: new records don't copy them
                History.SetParts(r, History.PartsOf(repeatOf), History.PartsTogether(repeatOf));
            if (jobCopies > 1 && CanShareJob || reopened != null && reopened.Parts != null && reopened.Parts.ShareLastPlate >= 0)
                History.SetShareLastPlate(r, jobShare);   // batch-2: the job's sharing choice (a several-files job only)
            if (reopened != null) r.For = reopened.For;   // opened again and re-saved: same person as before
            if (repeatOf != null) { r.For = repeatOf.For; r.RepeatOf = repeatOf.Id ?? ""; }   // a repeat print: new record, same person
            if (forWho != 0)
            {
                saved = state.HasValue ? History.AddFor(history, r, forWho, state.Value, DateTime.Now) : History.AddFor(history, r, forWho);
                saved.Customer = (customer ?? "").Trim();   // the name typed now (a same-quote replace takes it too)
            }
            else
            {
                if (history.Count > 0 && History.SameQuote(history[0], r)) r.Customer = history[0].Customer ?? "";
                saved = History.Add(history, r);
            }
            savedId = saved.Id;   // left out of the payback debt from now on (same price when saved again)
            if (offline) return null;   // picture mode never writes
            string err = History.Save(history);
            if (err == null) SaveRecordPicture(saved);
            return err;
        }

        /// Issue #4: the picture on screen goes with the record (pictures\<Id>.png/.jpg, 256 px). No picture on screen but the
        /// job came from a record that has one (Open again / Repeat print; e.g. a website WebP the app can't show): that one
        /// is copied (same Id = kept). Otherwise nothing = the record's old picture is deleted.
        void SaveRecordPicture(QuoteRecord rec)
        {
            if (offline || rec == null || string.IsNullOrEmpty(rec.Id)) return;
            byte[] bytes = jobLoaded && modelImage != null ? History.EncodePicture(modelImage) : null;
            var from = repeatOf ?? reopened;
            if (bytes == null && from != null && !string.IsNullOrEmpty(from.Id) && History.HasPicture(from.Id))
            {
                if (from.Id != rec.Id) History.CopyPicture(from.Id, rec.Id);
                return;
            }
            History.SavePicture(rec.Id, bytes);
        }

        /// The picture saved with a record (History.LoadPicture) as a Bitmap; null = none, not decodable (WebP) or picture mode.
        Bitmap StoredPicture(QuoteRecord r)
        {
            if (r == null || string.IsNullOrEmpty(r.Id)) return null;
            byte[] bytes;
            if (offline)
            {
                // picture mode: the sample pictures only (never the user's pictures folder)
                if (SamplePictures == null || !SamplePictures.TryGetValue(r.Id, out bytes)) return null;
            }
            else bytes = History.LoadPicture(r.Id);
            var img = History.PictureImage(bytes);
            if (img == null) return null;
            var bmp = img as Bitmap;
            if (bmp != null) return bmp;
            using (img) return new Bitmap(img);
        }

        /// Test run "--settings <folder> --hidden --test-invoice" (plans\invoice-toggle), only in test mode: Settings'
        /// "Show invoice…" (MainInvoice = 1) with the real window and export code (NewJobInvoiceForm, QuoteForm's own
        /// Export, ExportJobInvoice / ExportRecord / ExportQuote, MenuExport); file dialogs answered by testExportPath (files
        /// in the folder), the clipboard caught by testClipboard. Results in <folder>\test_invoice.txt; exit 0 / 4.
        internal void TestInvoiceFlow()
        {
            if (!AppSettings.TestMode || !Program.Hidden) return;
            string dir = AppSettings.Folder;
            var log = new TestLog();
            log.Add(SetupTest.StandInNote);
            string clip = null, askedName = null;
            bool cancel = false;
            testClipboard = t => clip = t;
            testExportPath = (kind, name) => { askedName = name; return cancel ? null : Path.Combine(dir, name + (kind == QuoteExport.Pdf ? ".pdf" : ".png")); };
            string invDir = InvoicesFolder;
            // the invoice copies (issue #6): the files in <test folder>\Invoices
            Func<string[]> copies = () => { try { return Directory.Exists(invDir) ? Directory.GetFiles(invDir) : new string[0]; } catch { return new string[0]; } };
            Func<string, string, bool> sameFile = (p1, p2) => { try { return File.ReadAllBytes(p1).SequenceEqual(File.ReadAllBytes(p2)); } catch { return false; } };
            QuoteForm f = null;
            try
            {
                // a typed one-colour job (no slicing)
                Func<int, bool> setJob = minutes =>
                {
                    var one = new SliceResult { Seconds = minutes * 60, Grams = 11.8, Plates = 1 };
                    one.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#F07850", Type = "PLA", Profile = "Bambu PLA Matte @BBL A1", Grams = 11.8 });
                    SetColours(one);
                    txtUrl.Text = SingleBenchyLink;
                    txtHours.Text = "0";
                    txtMinutes.Text = minutes.ToString();
                    txtGrams.Text = "11,8";
                    rowPrice[0].Text = "200";
                    txtElecPrice.Text = "3,65";
                    jobModel = "3D Benchy";
                    SetJobLoaded(true);
                    Recalculate();
                    return last != null && lastInput != null;
                };
                log.Check(setJob(38), "job on screen (0 h 38 m, 11,8 g)");
                EnsureHistory();
                settings.MainInvoice = 1;
                settings.Save();
                UpdateQuoteButton();
                log.Check(btnCopy.Text == L.S("Show invoice…", "Vis faktura…"), "main button reads \"" + btnCopy.Text + "\"");

                Func<int> invoices = () => history.Count(r => !string.IsNullOrEmpty(r.InvoiceNo));
                Func<string> used = () => settings.UsedInvoiceNos ?? "";
                int count0 = history.Count, inv0 = invoices();
                string used0 = used();

                // 1. opening the window and a cancelled file dialog use no number and save nothing
                f = NewJobInvoiceForm("Test Kunde");
                log.Check(history.Count == count0 && invoices() == inv0 && used() == used0, "1a window opened: nothing saved, no number used");
                log.Check(f.TestActionsOn, "1b export buttons on (name given)");
                int copies0 = copies().Length;
                cancel = true;
                askedName = null;
                string msg = f.TestExport(QuoteExport.Pdf);
                cancel = false;
                log.Check(history.Count == count0 && invoices() == inv0 && used() == used0, "1c PDF dialog cancelled: nothing saved, no number used");
                log.Check(askedName != null && System.Text.RegularExpressions.Regex.IsMatch(askedName, @"^\d{4}-[1-9]\d{4} - Test Kunde$"),
                          "1f the dialog suggested \"" + askedName + "\" (number - name)");
                log.Check(copies().Length == copies0, "1g cancelled: no invoice copy (" + copies().Length + " in Invoices)");
                log.Check(!f.TestCustomerLocked, "1d name not locked before the invoice is made");
                {
                    // the minimum is never shown: its top-up is inside Work & handling (no "%"), the rows add up to the total
                    var qd = f.TestData();
                    var lines = qd != null && qd.Breakdown != null ? qd.Breakdown.Lines : null;
                    if (lines == null) log.Check(false, "1e invoice page breakdown: none");
                    else
                    {
                        var minLine = lines.FirstOrDefault(l => l.Key == QuoteLine.Minimum);
                        // payback is folded into Work & handling too (pricing, whole-app review)
                        long workOre = lines.Where(l => l.Key == QuoteLine.Work || l.Key == QuoteLine.Payback).Sum(l => l.Ore) + (minLine != null ? minLine.Ore : 0);
                        var shown = QuoteRows.FoldMinimum(lines);
                        var rows = QuoteRows.AddTowerNote(QuoteRows.Build(qd.Breakdown, qd.MaterialNames, qd.PurgeGrams, qd.Hours, qd.Minutes, qd.PlateName, qd.Quantity), qd.TowerGrams);
                        var workRow = rows.FirstOrDefault(r => r.Kind == QuoteRowKind.Head && r.Label == L.S("Work & handling", "Arbejde & håndtering"));
                        bool minDrawn = rows.Any(r => (r.Label + " " + r.Detail).IndexOf("inimum", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                      (r.Label + " " + r.Detail).IndexOf(L.S("lowest price", "laveste pris"), StringComparison.OrdinalIgnoreCase) >= 0);
                        log.Add("INFO job price " + Num.Kr(qd.Breakdown.TotalOre / 100.0) + (minLine != null ? ", top-up " + Num.Kr(minLine.Amount) : ", no top-up"));
                        log.Check(!minDrawn && !shown.Any(l => l.Key == QuoteLine.Minimum) && shown.Sum(l => l.Ore) == qd.Breakdown.TotalOre &&
                                  (workOre == 0 || (workRow != null && workRow.Amount == Num.Kr(workOre / 100.0) && workRow.Detail.Length == 0)),
                                  "1e page rows: " + (minDrawn ? "a minimum row" : "no minimum row") + ", work & handling " +
                                  (workRow != null ? workRow.Amount : "none") + " = work + top-up " + Num.Kr(workOre / 100.0) +
                                  ", rows sum " + Num.Kr(shown.Sum(l => l.Ore) / 100.0) + " = total " + Num.Kr(qd.Breakdown.TotalOre / 100.0));
                    }
                }

                // 4. Save to history in the invoice window: a plain quote, no number
                msg = f.TestExport(QuoteExport.History);
                var plain = history.Count > 0 ? history[0] : null;
                log.Check(f.TestInfoKind != InfoKind.Error && history.Count == count0 + 1, "4a Save to history saved one record: " + msg);
                log.Check(plain != null && string.IsNullOrEmpty(plain.InvoiceNo) && invoices() == inv0 && used() == used0,
                          "4b it is a plain quote: no invoice number, none used");
                log.Check(plain != null && plain.Customer == "Test Kunde", "4c quote has the typed name");
                log.Add("INFO the invoice below is the same job within 10 min, so it takes over this quote's record (History.Add)");

                // 2. first export: PDF to the folder, then text
                msg = f.TestExport(QuoteExport.Pdf);
                log.Check(f.TestInfoKind == InfoKind.Success, "2a PDF export: " + msg.Replace("\n", " / "));
                var made = history.FirstOrDefault(r => !string.IsNullOrEmpty(r.InvoiceNo));
                string no = made != null ? made.InvoiceNo : "";
                log.Check(invoices() == inv0 + 1, "2b exactly one invoice made (" + invoices() + " in history)");
                log.Check(System.Text.RegularExpressions.Regex.IsMatch(no, @"^\d{4}-[1-9]\d{4}$") && no.StartsWith(DateTime.Now.Year + "-"),
                          "2c number \"" + no + "\" is this year + 5 digits");
                log.Check(no != "2026-48213", "2d not the picture-mode sample number");
                log.Check(made != null && made.Printed, "2e record ticked Printed");
                log.Check(made != null && made.BilledTo == "Test Kunde", "2f BilledTo = \"" + (made != null ? made.BilledTo : "") + "\"");
                DateTime billedAt;
                log.Check(made != null && History.TryTime(made.BilledAt, out billedAt) && !string.IsNullOrEmpty(made.InvoiceDue), "2g billed date and due date saved");
                log.Check(used().Split(',').Contains(no) && used().Split(',').Length == (used0.Length == 0 ? 0 : used0.Split(',').Length) + 1,
                          "2h UsedInvoiceNos got the number (only it)");
                string pdf = Path.Combine(dir, no + " - Test Kunde.pdf");
                bool pdfOk = false;
                try { pdfOk = File.Exists(pdf) && System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 5) == "%PDF-"; } catch { }
                log.Check(pdfOk && askedName == no + " - Test Kunde", "2i PDF written, named after the number: " + Path.GetFileName(pdf) + " (suggested \"" + askedName + "\")");
                string copy1 = Path.Combine(invDir, no + " - Test Kunde.pdf");
                log.Check(File.Exists(copy1) && sameFile(copy1, pdf) && copies().Length == copies0 + 1,
                          "2o copy kept: Invoices\\" + Path.GetFileName(copy1) + " = the saved file (" + copies().Length + " in Invoices)");
                string ini = "", json = "";
                try { ini = File.ReadAllText(AppSettings.FilePath); } catch { }
                try { json = File.ReadAllText(History.FilePath); } catch { }
                log.Check(ini.Contains("MainInvoice=1"), "2j settings.ini has MainInvoice=1");
                log.Check(no.Length > 0 && ini.Contains(no), "2k settings.ini's used numbers have " + no);
                log.Check(no.Length > 0 && json.Contains(no), "2l history.json has the invoice " + no);
                log.Check(f.TestCustomerLocked, "3a name locked after the invoice is made");
                clip = null;
                msg = f.TestExport(QuoteExport.Text);
                log.Check(f.TestInfoKind == InfoKind.Success && clip != null && no.Length > 0 && clip.Contains(no),
                          "2m Copy text: invoice text with " + no + " (" + msg + ")");
                log.Check(clip != null && clip.IndexOf("inimum", StringComparison.OrdinalIgnoreCase) < 0, "2n Copy text names no minimum price");
                {
                    // A4: the made invoice keeps the price it was billed at; Friends can't be picked, nothing changes
                    int forBefore = made != null ? made.For : 0;
                    double othersBefore = made != null ? made.OthersPrice : 0;
                    log.Check(f.TestPriceLocked && !f.TestFriendsChosen, "2p price choice locked at Customers after the invoice is made");
                    f.TestFriends(true);
                    log.Check(!f.TestFriendsChosen, "2q a Friends click is ignored (still Customers)");
                    clip = null;
                    msg = f.TestExport(QuoteExport.Text);
                    log.Check(made != null && made.For == forBefore && made.OthersPrice == othersBefore && made.InvoiceNo == no,
                              "2r record unchanged: For " + (made != null ? made.For : 0) + ", customer price " + Num.Kr(othersBefore));
                    log.Check(clip != null && clip.Contains(Num.N2(othersBefore)), "2s Copy text holds the customer amount " + Num.Kr(othersBefore));
                }

                // 3. another export from the same window: same number, nothing new
                msg = f.TestExport(QuoteExport.Pdf);
                log.Check(f.TestInfoKind == InfoKind.Success && made != null && made.InvoiceNo == no && invoices() == inv0 + 1,
                          "3b second PDF keeps " + no + ", still one invoice (" + msg.Replace("\n", " / ") + ")");
                log.Check(used() .Split(',').Count(x => x == no) == 1 && used().Split(',').Length == (used0.Length == 0 ? 0 : used0.Split(',').Length) + 1,
                          "3c no other number used");
                {
                    // the same invoice again: the same name; its copy is not written twice (same bytes), never overwritten
                    var mine = copies().Where(c => Path.GetFileName(c).StartsWith(no + " - Test Kunde", StringComparison.OrdinalIgnoreCase)).ToList();
                    bool distinct = true;
                    for (int i = 0; i < mine.Count; i++) for (int j = i + 1; j < mine.Count; j++) if (sameFile(mine[i], mine[j])) distinct = false;
                    log.Check(askedName == no + " - Test Kunde" && mine.Count >= 1 && mine.Count <= 2 && distinct && mine.Any(c => sameFile(c, pdf)),
                              "3d second PDF: same name, " + mine.Count + " copy file(s), none twice, one = the saved file");
                }
                f.Dispose(); f = null;

                // the SAME unchanged job again within 10 minutes (History.SameQuote: an invoiced record is never the same
                // quote): nothing may take over the invoice's record or number
                Func<int> usedCount = () => used().Length == 0 ? 0 : used().Split(',').Length;
                int used1 = usedCount();
                Func<string> inv1Text = () => made == null ? "none" : made.InvoiceNo + " / " + made.Customer + " / " + made.BilledTo;
                Func<bool> inv1Kept = () => made != null && made.InvoiceNo == no && made.Customer == "Test Kunde" && made.BilledTo == "Test Kunde"
                                            && history.Contains(made);

                // 5. ▾ quick export: a new quote record, no number
                int count5 = history.Count;
                clip = null;
                MenuExport(QuoteExport.Text);
                var quick = history.Count > 0 ? history[0] : null;
                log.Check(history.Count == count5 + 1 && quick != null && !ReferenceEquals(quick, made) && string.IsNullOrEmpty(quick.InvoiceNo),
                          "5a same job: quick Copy text added a new quote without a number");
                log.Check(clip != null && clip.Length > 0 && !clip.Contains(no), "5b quote text copied (no invoice number in it)");
                log.Check(invoices() == inv0 + 1 && usedCount() == used1, "5c no number used by the quick export");
                log.Check(inv1Kept(), "5d first invoice unchanged (number / Customer / BilledTo = " + inv1Text() + ")");
                log.Check(btnCopy.Text == L.S("Show invoice…", "Vis faktura…"), "5e main button still \"" + btnCopy.Text + "\"");

                // 6. a second Show invoice… of the same job for another customer: a NEW number
                f = NewJobInvoiceForm("Anden Kunde");
                msg = f.TestExport(QuoteExport.Pdf);
                var made2 = history.FirstOrDefault(r => !string.IsNullOrEmpty(r.InvoiceNo) && !ReferenceEquals(r, made));
                string no2 = made2 != null ? made2.InvoiceNo : "";
                log.Check(f.TestInfoKind == InfoKind.Success, "6a second invoice PDF: " + msg.Replace("\n", " / "));
                log.Check(no2.Length > 0 && no2 != no && invoices() == inv0 + 2, "6b new number " + no2 + " (first " + no + "), " + invoices() + " invoices");
                log.Check(made2 != null && made2.BilledTo == "Anden Kunde", "6c second invoice billed to \"" + (made2 != null ? made2.BilledTo : "") + "\"");
                log.Check(inv1Kept(), "6d first invoice kept its number, Customer and BilledTo (" + inv1Text() + ")");
                log.Check(usedCount() == used1 + 1 && used().Split(',').Contains(no2), "6e exactly one more number used");
                f.Dispose(); f = null;

                // 7. Save to history in a new invoice window (same job, an invoice on top): adds a plain quote
                int count7 = history.Count;
                f = NewJobInvoiceForm("Tredje Kunde");
                msg = f.TestExport(QuoteExport.History);
                var plain7 = history.Count > 0 ? history[0] : null;
                log.Check(f.TestInfoKind != InfoKind.Error && history.Count == count7 + 1 && plain7 != null
                          && !ReferenceEquals(plain7, made) && !ReferenceEquals(plain7, made2) && string.IsNullOrEmpty(plain7.InvoiceNo),
                          "7a Save to history added a plain quote without a number: " + msg);
                log.Check(plain7 != null && plain7.Customer == "Tredje Kunde", "7b plain quote has the typed name");
                log.Check(inv1Kept() && made2 != null && made2.InvoiceNo == no2 && made2.Customer == "Anden Kunde" && made2.BilledTo == "Anden Kunde",
                          "7c invoices not renamed (" + inv1Text() + "; " + (made2 != null ? made2.InvoiceNo + " / " + made2.Customer + " / " + made2.BilledTo : "none") + ")");
                log.Check(invoices() == inv0 + 2 && usedCount() == used1 + 1, "7d no number used");
                f.Dispose(); f = null;

                // 8. invoice copies (issue #6)
                {
                    string copy2 = Path.Combine(invDir, no2 + " - Anden Kunde.pdf");
                    log.Check(File.Exists(copy2), "8a second invoice's copy: Invoices\\" + Path.GetFileName(copy2));
                    // same bytes again: nothing new
                    byte[] saved = File.ReadAllBytes(copy1);
                    int n0 = copies().Length;
                    string note = SaveInvoiceCopy(saved, no + " - Test Kunde");
                    log.Check(note == null && copies().Length == n0, "8b same PDF again: no new copy (" + copies().Length + " files)");
                    // the kept copy changed: the next one goes beside it, the changed file stays as it is
                    byte[] changed = saved.Concat(new byte[] { 10 }).ToArray();
                    File.WriteAllBytes(copy1, changed);
                    note = SaveInvoiceCopy(saved, no + " - Test Kunde");
                    var beside = copies().Where(c => Path.GetFileName(c).StartsWith(no + " - Test Kunde (", StringComparison.OrdinalIgnoreCase)).ToList();
                    log.Check(note == null && copies().Length == n0 + 1 && beside.Any(c => File.ReadAllBytes(c).SequenceEqual(saved)) &&
                              File.ReadAllBytes(copy1).SequenceEqual(changed),
                              "8c a different older file: copy saved beside it as " + string.Join(", ", beside.Select(Path.GetFileName)) + ", the old file untouched");
                    // switched off: no copy
                    settings.InvoiceCopies = 0;
                    note = SaveInvoiceCopy(saved, "Off test");
                    settings.InvoiceCopies = 1;
                    log.Check(note == null && !File.Exists(Path.Combine(invDir, "Off test.pdf")), "8d setting off: no copy");

                    // an invoice picture: the number name, no copy
                    int n1 = copies().Length;
                    f = NewJobInvoiceForm("Fjerde Kunde");
                    msg = f.TestExport(QuoteExport.Png);
                    var made4 = history.FirstOrDefault(r => r.BilledTo == "Fjerde Kunde");
                    string no4 = made4 != null ? made4.InvoiceNo ?? "" : "";
                    log.Check(f.TestInfoKind == InfoKind.Success && no4.Length > 0 && askedName == no4 + " - Fjerde Kunde" &&
                              File.Exists(Path.Combine(dir, no4 + " - Fjerde Kunde.png")) && copies().Length == n1,
                              "8e invoice picture \"" + askedName + ".png\": no copy (" + msg.Replace("\n", " / ") + ")");
                    f.Dispose(); f = null;

                    // a quote PDF (▾ quick menu): the date name, no copy
                    MenuExport(QuoteExport.Pdf);
                    log.Check(askedName != null && askedName.StartsWith(DateTime.Now.ToString("yyyy-MM-dd")) && copies().Length == n1,
                              "8f quote PDF \"" + askedName + "\": date name, no copy");

                    // the copy can't be made (a file where the folder should be): the PDF is still saved, the text says so
                    string moved = invDir + "_kept";
                    Directory.Move(invDir, moved);
                    try
                    {
                        File.WriteAllText(invDir, "not a folder");
                        f = NewJobInvoiceForm("Femte Kunde");
                        msg = f.TestExport(QuoteExport.Pdf);
                        var made5 = history.FirstOrDefault(r => r.BilledTo == "Femte Kunde");
                        string no5 = made5 != null ? made5.InvoiceNo ?? "" : "";
                        log.Check(f.TestInfoKind == InfoKind.Success && no5.Length > 0 && File.Exists(Path.Combine(dir, no5 + " - Femte Kunde.pdf")) &&
                                  msg.Contains(L.S("The copy in the Invoices folder couldn't be saved", "Kopien i mappen Invoices kunne ikke gemmes")),
                                  "8g copy failed: PDF still saved, not an error, with the note (" + msg.Replace("\n", " / ") + ")");
                        f.Dispose(); f = null;
                    }
                    finally
                    {
                        try { File.Delete(invDir); } catch { }
                        try { Directory.Move(moved, invDir); } catch { }
                    }
                    log.Check(Directory.Exists(invDir) && File.Exists(copy1), "8h Invoices folder back as it was");
                }

                // 9. the other export kinds through the shared WriteExport (optimizer finding 4)
                {
                    bool failed9;
                    clip = null;
                    msg = ExportQuote(this, QuoteExport.Copy, true, true, "", out failed9);
                    log.Check(!failed9 && clip == "[picture]" && msg.StartsWith(L.S("✓ Quote picture copied", "✓ Tilbuddet er kopieret som billede")),
                              "9a quick Copy picture: " + msg.Replace("\n", " / "));
                    msg = ExportQuote(this, QuoteExport.Png, true, true, "", out failed9);
                    log.Check(!failed9 && askedName != null && File.Exists(Path.Combine(dir, askedName + ".png")),
                              "9b quick Save picture \"" + askedName + ".png\": " + msg.Replace("\n", " / "));
                    clip = null;
                    f = NewJobInvoiceForm("Sjette Kunde");
                    msg = f.TestExport(QuoteExport.Copy);
                    var made6 = history.FirstOrDefault(r => r.BilledTo == "Sjette Kunde");
                    log.Check(f.TestInfoKind == InfoKind.Success && clip == "[picture]" && made6 != null && !string.IsNullOrEmpty(made6.InvoiceNo) &&
                              msg.StartsWith(L.S("✓ Invoice picture copied", "✓ Fakturaen er kopieret som billede")),
                              "9c invoice Copy picture: " + msg.Replace("\n", " / "));
                    f.Dispose(); f = null;
                }

                // 10. issue #7: the invoice window's print status box
                {
                    BackupForm.AskForm.TestAnswers = new Queue<DialogResult>();
                    BackupForm.AskForm.TestLog = log;
                    Func<QuoteRecord, string> savedState = r =>
                    {
                        string p;
                        var back = History.Load(out p);
                        var s = back == null || r == null ? null : back.FirstOrDefault(x => x.Id == r.Id);
                        return s == null ? "missing" : History.PrintStateOf(s, DateTime.Now).ToString();
                    };
                    // a new job invoice: the box is there, starts at Printed; a pick before it is made saves nothing
                    int c10 = history.Count;
                    f = NewJobInvoiceForm("Syvende Kunde");
                    log.Check(f.TestStatusShown && f.PrintStatus == PrintState.Printed, "10a new invoice window: status box shown, starts at " + QuoteRows.StateName(f.PrintStatus));
                    f.TestPickStatus(PrintState.Printing);
                    log.Check(history.Count == c10, "10b picking Printing now before the invoice is made saves nothing");
                    msg = f.TestExport(QuoteExport.Text);
                    var made7 = history.FirstOrDefault(r => r.BilledTo == "Syvende Kunde");
                    log.Check(made7 != null && !string.IsNullOrEmpty(made7.InvoiceNo) && History.PrintStateOf(made7, DateTime.Now) == PrintState.Printing &&
                              savedState(made7) == "Printing" && f.PrintStatus == PrintState.Printing,
                              "10c made with the chosen status: " + (made7 != null ? History.PrintStateOf(made7, DateTime.Now).ToString() : "none") +
                              ", saved " + savedState(made7));
                    // once made: a pick saves at once (one job: no question, even for Failed)
                    f.TestPickStatus(PrintState.Failed);
                    log.Check(made7 != null && made7.Failed && savedState(made7) == "Failed" && BackupForm.AskForm.TestAnswers.Count == 0 &&
                              f.TestInfoText.StartsWith(L.S("✓ Print status saved: Failed", "✓ Printstatus gemt: Fejlet")),
                              "10d Failed saved at once, no question: " + f.TestInfoText.Replace("\n", " / "));
                    f.Dispose(); f = null;
                    // the saved invoice opened from History: the record's status, a pick saves at once
                    f = NewRecordQuoteForm(made7, true);
                    log.Check(f.TestStatusShown && f.PrintStatus == PrintState.Failed, "10e record invoice window shows the saved status " + QuoteRows.StateName(f.PrintStatus));
                    f.TestPickStatus(PrintState.Printed);
                    log.Check(made7.Printed && !made7.Failed && savedState(made7) == "Printed", "10f Printed picked: saved " + savedState(made7));
                    f.Dispose(); f = null;
                    // a saved quote: the same
                    QuoteRecord q10;
                    SaveToHistory(History.ForCustomer, "Otte Kunde", out q10);   // the invoices above took over the plain quotes
                    if (q10 != null && string.IsNullOrEmpty(q10.InvoiceNo))
                    {
                        History.SetPrintState(q10, PrintState.NotPrinted, DateTime.Now);
                        f = NewRecordQuoteForm(q10, false);
                        f.TestPickStatus(PrintState.Printed);
                        log.Check(savedState(q10) == "Printed", "10g record quote window: Printed saved at once (" + savedState(q10) + ")");
                        f.Dispose(); f = null;
                    }
                    else log.Check(false, "10g no plain saved quote to open");

                    // Settings "Reset to defaults": asks first; No changes nothing; Yes keeps your own details, resets the rest
                    var s10 = settings.Clone();
                    s10.QuoteName = "Test Navn";
                    s10.PayMobilePay = "12 34 56 78";
                    s10.PayBank = "reg. 1234";
                    s10.OthersMarkupPct = s10.OthersMarkupPct + 17;
                    s10.PrimeTower = 0;       // issue #8 step 3: Reset brings both tower settings back (1 / 0)
                    s10.QuoteShowTower = 1;
                    var def = new AppSettings();
                    using (var sf = new SettingsForm(s10))   // OK closes the window: one window per answer
                    {
                        BackupForm.AskForm.TestAnswers.Enqueue(DialogResult.No);
                        sf.TestReset();
                        var r1 = sf.TestAccept();
                        log.Check(r1 != null && r1.OthersMarkupPct == s10.OthersMarkupPct && r1.QuoteName == "Test Navn",
                                  "10h Reset to defaults, No: nothing changed (customer markup " + (r1 != null ? Num.Fmt(r1.OthersMarkupPct) : "-") + ")");
                    }
                    using (var sf = new SettingsForm(s10))
                    {
                        BackupForm.AskForm.TestAnswers.Enqueue(DialogResult.Yes);
                        sf.TestReset();
                        var r2 = sf.TestAccept();
                        log.Check(r2 != null && r2.OthersMarkupPct == def.OthersMarkupPct && r2.QuoteName == "Test Navn" &&
                                  r2.PayMobilePay == "12 34 56 78" && r2.PayBank == "reg. 1234",
                                  "10i Reset, Yes: markup back to " + (r2 != null ? Num.Fmt(r2.OthersMarkupPct) : "-") + " (default " + Num.Fmt(def.OthersMarkupPct) +
                                  "), name / MobilePay / bank kept (" + (r2 != null ? r2.QuoteName + " / " + r2.PayMobilePay + " / " + r2.PayBank : "-") + ")");
                        log.Check(r2 != null && r2.PrimeTower == 1 && r2.QuoteShowTower == 0,
                                  "10k Reset, Yes: prime tower back to on, tower grams on quotes back to off (" +
                                  (r2 != null ? r2.PrimeTower + " / " + r2.QuoteShowTower : "-") + ")");
                        log.Check(BackupForm.AskForm.TestAnswers.Count == 0, "10j the reset asked both times");
                    }
                    BackupForm.AskForm.TestAnswers = null;
                    BackupForm.AskForm.TestLog = null;
                }

                // 11. issue #8: a scaled job keeps "(150 %)" in its quote / invoice name, saved and re-opened; Clear = 100 %
                {
                    string tag = "(" + History.ScaleText(150) + ")";
                    log.Check(setJob(38), "11 job on screen again");
                    jobScale = ScaleXyz.Uniform(150);   // as a finished Scale… at 150 % leaves it
                    log.Check(QuoteModelName().EndsWith(tag), "11a the job's quote name \"" + QuoteModelName() + "\" ends with " + tag);
                    if (f != null) { f.Dispose(); f = null; }
                    f = NewJobInvoiceForm("Skala Kunde");
                    string m1 = f.TestData() != null ? f.TestData().Model : "";
                    log.Check(m1.EndsWith(tag), "11b the job's invoice window names it \"" + m1 + "\"");
                    f.Dispose(); f = null;
                    QuoteRecord s11;
                    SaveToHistory(History.ForCustomer, "Skala Kunde", out s11);
                    string saved = HistoryTest.FileRecords() != null ? HistoryTest.FileRecords().Where(x => s11 != null && x.Id == s11.Id).Select(x => History.ScaleText(x)).FirstOrDefault() : null;
                    log.Check(s11 != null && History.ScaleOf(s11) == 150 && saved == History.ScaleText(150),
                              "11c saved with scale " + (s11 != null ? History.ScaleText(s11) : "-") + " (history.json: " + (saved ?? "-") + ")");
                    if (s11 != null)
                    {
                        log.Check(RecordModelName(s11).EndsWith(tag), "11d the saved record's name \"" + RecordModelName(s11) + "\"");
                        foreach (bool inv in new[] { false, true })
                        {
                            f = NewRecordQuoteForm(s11, inv);
                            string m = f.TestData() != null ? f.TestData().Model : "";
                            log.Check(m.EndsWith(tag), "11" + (inv ? "f" : "e") + " the saved " + (inv ? "invoice" : "quote") + " window names it \"" + m + "\"");
                            f.Dispose(); f = null;
                        }
                    }
                    ClearJob();   // what the Clear button runs
                    log.Check(!jobScale.IsScaled && !QuoteModelName().Contains(tag),
                              "11g Clear: the job scale is back to " + jobScale.Text + ", name \"" + QuoteModelName() + "\"");
                }

                // 12. issue #8 step 3: the prime tower. A two-colour job sliced with the tower (1,10 + 0,60 g, 314 s; purge 2,6 g):
                // the app's note line, the quote's "Prime tower 1,70 g" row only with QuoteShowTower, the flush-only purge
                // note then, Copy text, the saved record, Open again, and a combined bill's summed tower
                double keepShow = settings.QuoteShowTower;
                try
                {
                    var two = new SliceResult { Seconds = 2988, Grams = 5.84, Plates = 1, PurgeGrams = 2.6, ColourChangeHours = -1, TowerGrams = 1.70, TowerSeconds = 314 };
                    two.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#FFFFFF", Type = "PLA", Profile = "Bambu PLA Basic @BBL A1", Grams = 4.85, TowerGrams = 1.10 });
                    two.Filaments.Add(new FilamentUse { Slot = 2, Colour = "#ED1C24", Type = "PLA", Profile = "Bambu PLA Basic @BBL A1", Grams = 0.99, TowerGrams = 0.60 });
                    txtUrl.Text = SingleBenchyLink;   // before the job (its TextChanged resets the job's state)
                    SetColours(two);
                    jobTower = true; jobTowerKnown = true;
                    txtHours.Text = "0";
                    txtMinutes.Text = "50";
                    for (int i = 0; i < colourRows; i++) rowPrice[i].Text = "200";
                    txtGrams.Text = "5,84";
                    txtElecPrice.Text = "3,65";
                    jobModel = "Two-colour project";
                    SetJobLoaded(true);
                    Recalculate();
                    log.Check(last != null && filaments != null && filaments.Count == 2, "12 two-colour job on screen");
                    string towerApp = TowerText(1.70, 314, true);
                    log.Check(lblPurge.Visible && lblPurge.Text.Replace("\n", " ").ToLowerInvariant().Contains(towerApp.ToLowerInvariant()),
                              "12a the app's note shows \"" + towerApp + "\": \"" + lblPurge.Text.Replace("\n", " / ") + "\"");
                    string note = QuoteRows.TowerNote(1.70);
                    Func<QuoteData, List<QuoteRow>> rowsOf = d => QuoteRows.AddTowerNote(QuoteRows.Build(d.Breakdown, d.MaterialNames, d.PurgeGrams, d.Hours, d.Minutes, d.PlateName, d.Quantity), d.TowerGrams);
                    Func<List<QuoteRow>, int> noteAt = rows => rows.FindIndex(x => x.Kind == QuoteRowKind.Note && x.Label == note);
                    var when12 = new DateTime(2026, 11, 20, 12, 0, 0);
                    settings.QuoteShowTower = 0;
                    var q0 = BuildQuote(false, when12, "T-1", "Tårn Kunde");
                    log.Check(noteAt(rowsOf(q0)) < 0 && Math.Abs(q0.PurgeGrams - 2.6) < 0.001,
                              "12b box off: no tower row on the quote, purge note keeps " + q0.PurgeGrams.ToString("0.##", Num.Dk) + " g");
                    log.Check(!QuoteText("Tårn Kunde").Contains(note), "12c box off: Copy text has no tower line");
                    settings.QuoteShowTower = 1;
                    var q1 = BuildQuote(false, when12, "T-1", "Tårn Kunde");
                    var rows1 = rowsOf(q1);
                    int at = noteAt(rows1), rule = rows1.FindIndex(x => x.Kind == QuoteRowKind.Rule);
                    log.Check(at > 0 && (rule < 0 || at < rule) && rows1[at].Amount == "",
                              "12d box on: \"" + note + "\" row at " + at + " (first rule " + rule + "), no amount");
                    log.Check(Math.Abs(q1.PurgeGrams - 0.9) < 0.001, "12e box on: the purge note is the flush only (" + q1.PurgeGrams.ToString("0.##", Num.Dk) + " g)");
                    string copy = QuoteText("Tårn Kunde");
                    log.Check(copy.Contains(note), "12f box on: Copy text has the tower line");
                    QuoteRecord s12;
                    SaveToHistory(History.ForCustomer, "Tårn Kunde", out s12);
                    log.Check(s12 != null && s12.PrimeTower == 1 && Math.Abs(s12.TowerGrams - 1.70) < 0.001 && Math.Abs(s12.TowerSeconds - 314) < 0.5 &&
                              s12.Filaments != null && s12.Filaments.Count == 2 && Math.Abs(s12.Filaments[0].TowerGrams - 1.10) < 0.001 &&
                              Math.Abs(s12.Filaments[1].TowerGrams - 0.60) < 0.001 && History.ShowsTower(s12),
                              "12g saved: switch " + (s12 != null ? s12.PrimeTower.ToString() : "-") + ", tower " +
                              (s12 != null ? s12.TowerGrams.ToString("0.##", Num.Dk) + " g, " + s12.TowerSeconds.ToString("0", Num.Dk) + " s" : "-"));
                    if (s12 != null)
                    {
                        var rq = BuildRecordQuote(s12, false, "Tårn Kunde", true, null);
                        log.Check(noteAt(rowsOf(rq)) > 0, "12h the saved job's invoice has the tower row");
                        settings.QuoteShowTower = 0;
                        rq = BuildRecordQuote(s12, false, "Tårn Kunde", false, null);
                        log.Check(noteAt(rowsOf(rq)) < 0 && !RecordText(s12, false, false).Contains(note), "12i box off: the saved job's quote and Copy text have none");
                        settings.QuoteShowTower = 1;
                        log.Check(RecordText(s12, false, false).Contains(note), "12j box on: the saved job's Copy text has the tower line");
                        // a combined bill: the summed tower of its jobs (one without a tower adds nothing)
                        var other = history.FirstOrDefault(x => x != null && x.Id != s12.Id && !History.ShowsTower(x));
                        if (other != null)
                        {
                            var jobs = new List<QuoteRecord> { s12, other };
                            var gd = BuildGroupQuote(jobs, false, "Tårn Kunde", false, null, when12);
                            var grows = QuoteRows.AddTowerNote(QuoteRows.BuildGroup(gd.Breakdown, gd.MaterialNames, gd.Hours, gd.Minutes, gd.PlateName, gd.GroupSameRates, 6), gd.TowerGrams);
                            log.Check(Math.Abs(gd.TowerGrams - History.TowerGramsSum(jobs)) < 0.001 && noteAt(grows) > 0,
                                      "12k combined bill: tower " + gd.TowerGrams.ToString("0.##", Num.Dk) + " g, row " + noteAt(grows));
                        }
                        else log.Check(false, "12k a saved job without a tower for the combined bill");
                        // Open again: the record's switch and tower, also when Settings says off
                        double keepTower = settings.PrimeTower;
                        settings.PrimeTower = 0;
                        ClearJob();
                        Reopen(s12);
                        log.Check(jobTower && lblPurge.Text.Replace("\n", " ").ToLowerInvariant().Contains(towerApp.ToLowerInvariant()),
                                  "12l Open again: tower " + (jobTower ? "on" : "off") + " (Settings off), note \"" + lblPurge.Text.Replace("\n", " / ") + "\"");
                        var old = new QuoteRecord { PrimeTower = -1 };
                        log.Check(!History.PrimeTowerFor(old, settings) && History.PrimeTowerFor(s12, settings), "12m an old record follows Settings (off), a saved one keeps its own (on)");
                        settings.PrimeTower = keepTower;
                    }
                }
                finally { settings.QuoteShowTower = keepShow; }
                ClearJob();

                // 13. paid-tick: a made invoice marked paid says "Paid 05-11-2026 – thank you" instead of "Please pay by …" on
                // its page and in Copy text; unticked it is back
                {
                    var madeInv = history.FirstOrDefault(x => x != null && !string.IsNullOrEmpty(x.InvoiceNo) &&
                                                              history.Count(y => y != null && string.Equals((y.InvoiceNo ?? "").Trim(), x.InvoiceNo.Trim(), StringComparison.OrdinalIgnoreCase)) == 1);
                    if (madeInv == null) log.Check(false, "13 a made single invoice to mark paid");
                    else
                    {
                        DateTime now13 = DateTime.Now;
                        string paidLine = History.PaidText(now13.Date);
                        History.SetPaid(history, madeInv.InvoiceNo, true, now13);
                        var pd = BuildRecordQuote(madeInv, madeInv.For == History.ForFriends, madeInv.Customer, true, null);
                        string pt = RecordText(madeInv, madeInv.For == History.ForFriends, true);
                        log.Check(pd.PaidLine == paidLine && pt.Contains(paidLine) && !pt.Contains(L.S("please pay by", "betales senest")),
                                  "13a paid: page line \"" + pd.PaidLine + "\", Copy text has it and no \"please pay by\"");
                        using (var bmp = QuotePage.Render(pd, false)) bmp.Save(Path.Combine(dir, "test_invoice_paid.png"), System.Drawing.Imaging.ImageFormat.Png);
                        History.SetPaid(history, madeInv.InvoiceNo, false, now13);
                        pd = BuildRecordQuote(madeInv, madeInv.For == History.ForFriends, madeInv.Customer, true, null);
                        pt = RecordText(madeInv, madeInv.For == History.ForFriends, true);
                        log.Check(pd.PaidLine == "" && pt.Contains(L.S("please pay by", "betales senest")), "13b unpaid again: \"Please pay by …\" is back");
                        History.Save(history);
                    }
                }
            }
            catch (Exception ex) { log.Check(false, "exception: " + ex); }
            finally
            {
                if (f != null) f.Dispose();
                testClipboard = null;
                testExportPath = null;
            }
            try { File.WriteAllLines(Path.Combine(dir, "test_invoice.txt"), log.Lines); } catch { }
            Program.EndTestRun(log.Ok ? 0 : 4);
        }

        /// Test run "--settings <folder> --hidden --test-bill" (combined bill, plans\combined-bill), only in test mode: jobs
        /// are saved from the main window into the test folder's history, then the real History window (HistoryForm.Show
        /// off-screen, its own selection + Quote… / Invoice… code -> ShowRecordsQuote) opens the combined windows, which are
        /// handed to the test instead of ShowDialog (testRecordsForm) and exported with their own Export. File dialogs
        /// answer with files in the folder, the clipboard is caught. Results in <folder>\test_bill.txt; exit 0 / 4.
        internal void TestBillFlow()
        {
            if (!AppSettings.TestMode || !Program.Hidden) return;
            string dir = AppSettings.Folder;
            var log = new TestLog();
            log.Add(SetupTest.StandInNote);
            string clip = null, askedName = null;
            bool cancel = false;
            testClipboard = t => clip = t;
            testExportPath = (kind, name) => { askedName = name; return cancel ? null : Path.Combine(dir, name + (kind == QuoteExport.Pdf ? ".pdf" : ".png")); };
            string invDir = InvoicesFolder;
            Func<string, string, bool> sameFile = (p1, p2) => { try { return File.ReadAllBytes(p1).SequenceEqual(File.ReadAllBytes(p2)); } catch { return false; } };
            Action<QuoteForm> onWindow = null;
            testRecordsForm = f => { if (onWindow != null) onWindow(f); };
            HistoryForm hist = null;
            Func<double, long> ore = Pricing.ShownOre;
            Func<string, int> pdfPages = path =>
            {
                try
                {
                    string t = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path));
                    return System.Text.RegularExpressions.Regex.Matches(t, @"/Type /Page /").Count;
                }
                catch { return -1; }
            };
            // the combined breakdown of a window's page: rows sum exactly to the total, one minimum row = the top-up (none
            // without), material grams = the jobs' grams
            Func<QuoteData, string> bdText = d => d.Breakdown == null || d.Breakdown.Lines == null ? "none"
                : string.Join(";", d.Breakdown.Lines.Select(l => l.Key + "=" + l.Ore));
            Action<string, QuoteData, List<QuoteRecord>> checkBd = (step, d, recs) =>
            {
                var lines = d.Breakdown != null ? d.Breakdown.Lines : null;
                if (lines == null || lines.Count == 0) { log.Check(false, step + " breakdown: none"); return; }
                long sum = lines.Sum(l => l.Ore);
                log.Check(sum == ore(d.GroupTotal) && d.Breakdown.TotalOre == sum,
                          step + " breakdown rows sum " + Num.Kr(sum / 100.0) + " = total " + Num.Kr(d.GroupTotal) + " (" + bdText(d) + ")");
                // saved data: one minimum line = the top-up (none without); drawn: never a minimum row, the top-up is inside
                // Work & handling (no "%" on it then), the shown lines still sum to the total
                var minLine = lines.FirstOrDefault(l => l.Key == QuoteLine.Minimum);
                int mins = lines.Count(l => l.Key == QuoteLine.Minimum);
                bool topUp = d.TopUpAmount.Length > 0;
                log.Check(topUp ? mins == 1 && Num.Kr(minLine.Amount) == d.TopUpAmount : mins == 0,
                          step + " saved minimum line " + (minLine != null ? Num.Kr(minLine.Amount) : "none") + ", top-up " + (topUp ? d.TopUpAmount : "none"));
                var shown = QuoteRows.FoldMinimum(lines);
                long workOre = lines.Where(l => l.Key == QuoteLine.Work || l.Key == QuoteLine.Payback).Sum(l => l.Ore) + (minLine != null ? minLine.Ore : 0);
                var workShown = shown.FirstOrDefault(l => l.Key == QuoteLine.Work);
                log.Check(!shown.Any(l => l.Key == QuoteLine.Minimum) && shown.Sum(l => l.Ore) == sum &&
                          (workOre == 0 ? workShown == null || workShown.Ore == 0 : workShown != null && workShown.Ore == workOre),
                          step + " shown lines: no minimum, work & handling " + (workShown != null ? Num.Kr(workShown.Amount) : "none") +
                          " = work + top-up " + Num.Kr(workOre / 100.0) + ", sum " + Num.Kr(shown.Sum(l => l.Ore) / 100.0));
                var rows = QuoteRows.AddTowerNote(QuoteRows.BuildGroup(d.Breakdown, d.MaterialNames, d.Hours, d.Minutes, d.PlateName, d.GroupSameRates, 6), d.TowerGrams);
                var workRow = rows.FirstOrDefault(r => r.Kind == QuoteRowKind.Head && r.Label == L.S("Work & handling", "Arbejde & håndtering"));
                bool minDrawn = rows.Any(r => (r.Label + " " + r.Detail).IndexOf(L.S("inimum", "inimum"), StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              (r.Label + " " + r.Detail).IndexOf(L.S("lowest price", "laveste pris"), StringComparison.OrdinalIgnoreCase) >= 0);
                log.Check(!minDrawn && (workOre == 0 || (workRow != null && workRow.Amount == Num.Kr(workOre / 100.0) && workRow.Detail.Length == 0)),
                          step + " drawn rows: " + (minDrawn ? "a minimum row" : "no minimum row") + ", work & handling " +
                          (workRow != null ? workRow.Amount + " \"" + workRow.Detail + "\"" : "none"));
                // the job lines carry their share of the top-up: they add up to the total
                var jobOre = QuoteRows.JobOre(History.GroupBill(recs, settings, d.GroupFriends));
                var want1 = jobOre.Select(o => Num.Kr(o / 100.0)).OrderBy(x => x).ToList();
                var have1 = d.Jobs.Select(j => j.Amount).OrderBy(x => x).ToList();
                log.Check(jobOre.Sum() == ore(d.GroupTotal) && want1.SequenceEqual(have1),
                          step + " job lines " + string.Join(" + ", have1) + " = total " + Num.Kr(d.GroupTotal));
                // machine wear: always its full split (History.GroupBreakdown fills Parts), the parts add up to the wear row, drawn under it
                var wearLine = lines.FirstOrDefault(l => l.Key == QuoteLine.Wear);
                if (wearLine != null && wearLine.Ore > 0)
                {
                    long partSum = wearLine.Parts != null ? wearLine.Parts.Sum(pt => pt.Ore) : -1;
                    int wearAt = rows.FindIndex(r => r.Kind == QuoteRowKind.Head && r.Label == L.S("Machine wear", "Maskinslid"));
                    int subs = 0;
                    for (int k = wearAt + 1; wearAt >= 0 && k < rows.Count && rows[k].Kind == QuoteRowKind.Sub; k++) subs++;
                    log.Check(wearLine.Parts != null && wearLine.Parts.Count > 0 && partSum == wearLine.Ore && subs == wearLine.Parts.Count,
                              step + " machine wear " + Num.Kr(wearLine.Amount) + ": " + (wearLine.Parts != null ? wearLine.Parts.Count : 0) +
                              " parts summing " + Num.Kr(partSum / 100.0) + ", " + subs + " drawn under it");
                }
                // the cards (side by side): print time, weight and a print price each (its top-up share inside); they add up
                var bad = new List<string>();
                foreach (var j in d.Jobs)
                    if (j.Amount != Num.Kr(j.Ore / 100.0) || string.IsNullOrEmpty(j.Weight) || j.Ore < 0)
                        bad.Add(j.Name + ": price " + j.Amount + " / " + Num.Kr(j.Ore / 100.0) + ", weight \"" + j.Weight + "\"");
                log.Check(bad.Count == 0 && d.Jobs.Sum(j => j.Ore) == ore(d.GroupTotal),
                          step + " cards: print prices sum " + Num.Kr(d.Jobs.Sum(j => j.Ore) / 100.0) + " = total " + Num.Kr(d.GroupTotal) +
                          (bad.Count > 0 ? " (" + string.Join("; ", bad) + ")" : ""));
                double grams = lines.Where(l => l.Key == QuoteLine.Material).Sum(l => l.Quantity), want = recs.Sum(r => r.Grams);
                log.Check(Math.Abs(grams - want) < 0.05, step + " material grams " + Num.Fmt(Math.Round(grams, 2)) + " = the jobs' " + Num.Fmt(Math.Round(want, 2)));
            };
            try
            {
                EnsureHistory();
                double minimum = settings.MinimumPrice;
                log.Add("INFO settings: minimum price " + Num.Kr(minimum) + ", round to " + Num.Fmt(settings.RoundTo) + ", " + history.Count + " records before");
                log.Check(minimum > 0, "minimum price is on (" + Num.Kr(minimum) + ")");

                // seed: real records saved from the main window (a typed one-colour job each; different names = never "the same quote")
                Func<string, int, double, QuoteRecord> save = (model, minutes, grams) =>
                {
                    var one = new SliceResult { Seconds = minutes * 60, Grams = grams, Plates = 1 };
                    one.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#F07850", Type = "PLA", Profile = "Bambu PLA Matte @BBL A1", Grams = grams });
                    SetColours(one);
                    txtUrl.Text = SingleBenchyLink;
                    txtHours.Text = (minutes / 60).ToString();
                    txtMinutes.Text = (minutes % 60).ToString();
                    txtGrams.Text = Num.Fmt(grams);
                    rowPrice[0].Text = "200";
                    txtElecPrice.Text = "3,65";
                    jobModel = model;
                    SetJobLoaded(true);
                    Recalculate();
                    QuoteRecord rec;
                    string err = SaveToHistory(History.ForCustomer, null, out rec);
                    if (err != null) log.Add("INFO save " + model + ": " + err);
                    return err == null ? rec : null;
                };
                var a = save("Bill A small", 12, 3);
                var b = save("Bill B small", 14, 4);
                var e1 = save("Bill E1", 10, 2.5);
                var e2 = save("Bill E2", 11, 2.6);
                var f1 = save("Bill F1", 15, 3.1);
                var f2 = save("Bill F2", 16, 3.2);
                var f3 = save("Bill F3", 17, 3.3);
                var g1 = save("Bill G1", 40, 12);
                var g2 = save("Bill G2", 45, 14);
                var x1 = save("Bill failed 1", 20, 5);
                var x2 = save("Bill failed 2", 21, 5.5);
                var single = save("Bill single", 50, 15);
                var pages = new List<QuoteRecord>();
                for (int i = 1; i <= 12; i++) pages.Add(save("Bill page " + i, 20 + i, 4 + i));
                var seeded = new List<QuoteRecord> { a, b, e1, e2, f1, f2, f3, g1, g2, x1, x2, single };
                seeded.AddRange(pages);
                log.Check(seeded.All(r => r != null), "seeded " + seeded.Count(r => r != null) + " records from the main window");
                // the plan's examples: hand-set saved prices (not the calculated ones, so the bill uses them as they are)
                e1.OthersPrice = 18; e2.OthersPrice = 14;
                f1.OthersPrice = 30; f2.OthersPrice = 25; f3.OthersPrice = 20;
                a.Customer = "Lene Hansen"; b.Customer = "Lene H.";
                DateTime now0 = DateTime.Now;
                History.MarkFailed(x1, true, now0);
                History.MarkFailed(x2, true, now0);
                log.Check(History.Save(history) == null, "history saved with the seeded records");
                double singlePrice = single.OthersPrice;

                hist = new HistoryForm(history, null, false);
                hist.Settings = settings;
                var asked = new List<QuoteRecord>();
                hist.QuoteRequested = (o, recs, inv) => { asked = recs; return ShowRecordsQuote(o, recs, inv); };
                hist.Show();
                Application.DoEvents();
                var h = hist;
                // runs Quote… / Invoice… on these records in the History window; act gets the window (false = none opened)
                Func<List<QuoteRecord>, bool, Action<QuoteForm>, bool> run = (recs, inv, act) =>
                {
                    bool got = false;
                    onWindow = f => { got = true; try { act(f); } catch (Exception ex) { log.Check(false, "exception in the window: " + ex.Message); } };
                    h.TestSelect(recs);
                    h.TestQuote(inv);
                    onWindow = null;
                    return got;
                };
                Func<List<QuoteRecord>, string> codes = recs => string.Join(",", recs.Select(r => string.IsNullOrEmpty(r.InvoiceNo) ? "-" : r.InvoiceNo));

                // 0. the buttons follow the selection
                h.TestSelect(new List<QuoteRecord> { e1, e2 });
                log.Check(h.SampleSelected().Count == 2 && h.TestQuoteButtonsOn, "0 two selected: Quote… and Invoice… are on");

                // 1. the plan's example 18 + 14 = 32 -> "Minimum price" 18 -> total 50 (with today's minimum / rounding)
                double top1;
                double total1 = Pricing.BillTotal(new[] { 18.0, 14.0 }, minimum, settings.RoundTo, false, out top1);
                log.Add("INFO expected 18 + 14: top-up " + Num.Kr(top1) + ", total " + Num.Kr(total1));
                string code1 = "";
                bool ok = run(new List<QuoteRecord> { e1, e2 }, true, f =>
                {
                    var d = f.TestData();
                    log.Check(f.Text == L.F("Invoice – {0}", "Faktura – {0}", QuoteRows.PrintsText(2)), "1a title \"" + f.Text + "\"");
                    log.Check(QuotePage.IsGroup(d) && d.Jobs.Count == 2, "1b two job lines");
                    log.Check(ore(d.GroupTotal) == ore(total1) && ore(total1) >= ore(minimum) && d.TopUpAmount == Num.Kr(top1) &&
                              d.TopUpLabel == QuoteRows.MinimumLabel(minimum),
                              "1c 18 + 14: \"" + d.TopUpLabel + "\" " + d.TopUpAmount + ", total " + Num.Kr(d.GroupTotal));
                    // the lines carry their share of the top-up (the minimum is never shown): 18 / 14 + ShareOre, sum = total
                    var sh1 = Pricing.ShareOre(new double[] { 18, 14 }, ore(top1));
                    log.Check(d.Jobs[0].Amount == Num.Kr((1800 + sh1[0]) / 100.0) && d.Jobs[1].Amount == Num.Kr((1400 + sh1[1]) / 100.0) &&
                              1800 + sh1[0] + 1400 + sh1[1] == ore(d.GroupTotal),
                              "1d lines 18 / 14 + their top-up share: " + d.Jobs[0].Amount + " / " + d.Jobs[1].Amount + " = total " + Num.Kr(d.GroupTotal));
                    checkBd("1n", d, new List<QuoteRecord> { e1, e2 });
                    log.Check(!f.TestActionsOn, "1e no name yet: exports off");
                    f.TestSetCustomer("Test Kunde");
                    // issue #6: a cancelled dialog uses no number; the suggested name is "<number> - <name>"
                    string usedBefore = settings.UsedInvoiceNos ?? "";
                    cancel = true;
                    askedName = null;
                    f.TestExport(QuoteExport.Pdf);
                    cancel = false;
                    string cancelledName = askedName;
                    log.Check(string.IsNullOrEmpty(e1.InvoiceNo) && string.IsNullOrEmpty(e2.InvoiceNo) && (settings.UsedInvoiceNos ?? "") == usedBefore,
                              "1o combined PDF dialog cancelled: no number used");
                    log.Check(cancelledName != null && System.Text.RegularExpressions.Regex.IsMatch(cancelledName, @"^\d{4}-[1-9]\d{4} - Test Kunde$"),
                              "1p the dialog suggested \"" + cancelledName + "\" (number - name)");
                    string msg = f.TestExport(QuoteExport.Pdf);
                    log.Check(f.TestInfoKind == InfoKind.Success, "1f PDF: " + msg.Replace("\n", " / "));
                    code1 = e1.InvoiceNo ?? "";
                    log.Check(askedName == code1 + " - Test Kunde", "1q saved as \"" + askedName + "\" = the invoice's number " + code1);
                });
                log.Check(ok, "1 the combined invoice window opened");
                log.Check(code1.Length > 0 && e2.InvoiceNo == code1, "1g one shared number: " + codes(new List<QuoteRecord> { e1, e2 }));
                log.Check(e1.BilledAt == e2.BilledAt && e1.Printed && e2.Printed, "1h same invoice date, Printed on both");
                log.Check(e1.BilledTo == "Test Kunde" && e2.BilledTo == "Test Kunde" && e1.Customer == "Test Kunde" && e2.Customer == "Test Kunde",
                          "1i name on both (Customer + BilledTo)");
                log.Check(ore(e1.OthersPrice) + ore(e2.OthersPrice) == ore(total1), "1j saved prices " + Num.Kr(e1.OthersPrice) + " + " +
                          Num.Kr(e2.OthersPrice) + " = the total " + Num.Kr(total1));
                log.Check(ore(e1.InvoiceTopUp) + ore(e2.InvoiceTopUp) == ore(top1), "1k top-up shares " + Num.Kr(e1.InvoiceTopUp) + " + " + Num.Kr(e2.InvoiceTopUp));
                string pdf1 = Path.Combine(dir, code1 + " - Test Kunde.pdf");
                log.Check(pdfPages(pdf1) == 1, "1l " + Path.GetFileName(pdf1) + ": " + pdfPages(pdf1) + " page");
                string copyB = Path.Combine(invDir, code1 + " - Test Kunde.pdf");
                log.Check(File.Exists(copyB) && sameFile(copyB, pdf1), "1r combined invoice copy: Invoices\\" + Path.GetFileName(copyB) + " = the saved file");
                string json = "";
                try { json = File.ReadAllText(History.FilePath); } catch { }
                log.Check(code1.Length > 0 && json.Contains(code1) && json.Contains("InvoiceTopUp"), "1m history.json has the invoice and InvoiceTopUp");

                // 2. 30 + 25 + 20 = 75: no top-up; Copy text lists every job
                ok = run(new List<QuoteRecord> { f1, f2, f3 }, true, f =>
                {
                    var d = f.TestData();
                    log.Check(ore(d.GroupTotal) == 7500 && d.TopUpLabel.Length == 0, "2a 30 + 25 + 20: total " + Num.Kr(d.GroupTotal) + ", no minimum line");
                    checkBd("2e", d, new List<QuoteRecord> { f1, f2, f3 });
                    f.TestSetCustomer("Test Kunde");
                    clip = null;
                    string msg = f.TestExport(QuoteExport.Text);
                    log.Check(f.TestInfoKind == InfoKind.Success && clip != null && clip.Contains("Bill F1") && clip.Contains("Bill F3") &&
                              clip.Contains(Num.Kr(75)) && clip.Contains(f1.InvoiceNo ?? "?"), "2b Copy text: every job, total, number (" + msg + ")");
                });
                log.Check(ok && f1.InvoiceNo == f2.InvoiceNo && f2.InvoiceNo == f3.InvoiceNo && !string.IsNullOrEmpty(f1.InvoiceNo),
                          "2c one number " + codes(new List<QuoteRecord> { f1, f2, f3 }));
                log.Check(ore(f1.OthersPrice) == 3000 && ore(f2.OthersPrice) == 2500 && ore(f3.OthersPrice) == 2000 &&
                          f1.InvoiceTopUp == 0 && f2.InvoiceTopUp == 0 && f3.InvoiceTopUp == 0, "2d prices unchanged, no shares");

                // 3. live jobs (calculated prices): a quote first (different names, no status box, nothing invoiced), then the invoice
                double liveTotal = 0;
                string liveBd = "";
                ok = run(new List<QuoteRecord> { a, b }, false, f =>
                {
                    log.Check(f.TestInfoText.Contains("Lene Hansen") && f.TestInfoText.Contains("Lene H."), "3a different names warned: " + f.TestInfoText.Replace("\n", " / "));
                    log.Check(f.TestCustomer == "Lene Hansen", "3b name box starts with \"" + f.TestCustomer + "\"");
                    // issue #7: one status box for both (they share "Not printed yet")
                    log.Check(f.TestStatusShown && !f.TestStatusMixed && f.PrintStatus == PrintState.NotPrinted &&
                              f.TestStatusHeading == L.F("Print status (all {0} prints)", "Printstatus (alle {0} print)", 2),
                              "3c combined quote: status box \"" + f.TestStatusHeading + "\" = " + QuoteRows.StateName(f.PrintStatus));
                    var d = f.TestData();
                    double pa = Pricing.PriceWithoutMinimum(History.ToInput(a), History.SavedSettings(a, settings));
                    double pb = Pricing.PriceWithoutMinimum(History.ToInput(b), History.SavedSettings(b, settings));
                    // without their own minimum, plus their share of the bill's one top-up (never shown), adding up to the total
                    long topOre = Math.Max(0, ore(d.GroupTotal) - ore(pa) - ore(pb));
                    var sh3 = Pricing.ShareOre(new[] { pa, pb }, topOre);
                    log.Check(d.Jobs[0].Amount == Num.Kr((ore(pa) + sh3[0]) / 100.0) && d.Jobs[1].Amount == Num.Kr((ore(pb) + sh3[1]) / 100.0) &&
                              ore(pa) + sh3[0] + ore(pb) + sh3[1] == ore(d.GroupTotal),
                              "3d lines without their own minimum " + Num.Kr(pa) + " / " + Num.Kr(pb) + " + top-up share: " +
                              d.Jobs[0].Amount + " / " + d.Jobs[1].Amount + " = total " + Num.Kr(d.GroupTotal) +
                              " (saved " + Num.Kr(a.OthersPrice) + " / " + Num.Kr(b.OthersPrice) + ")");
                    log.Check(d.Number == History.QuoteNumber(DateTime.Now) || d.Number.Length == 11, "3e quote number " + d.Number);
                    log.Check(ore(pa) + ore(pb) < ore(minimum) ? d.TopUpLabel.Length > 0 && ore(d.GroupTotal) >= ore(minimum) : d.TopUpLabel.Length == 0,
                              "3f top-up " + d.TopUpAmount + ", total " + Num.Kr(d.GroupTotal));
                    checkBd("3m", d, new List<QuoteRecord> { a, b });
                    f.TestFriends(true);   // friends price: its own breakdown, no minimum row
                    var df = f.TestData();
                    log.Check(df.GroupFriends && df.TopUpAmount.Length == 0, "3n friends: no top-up, total " + Num.Kr(df.GroupTotal));
                    checkBd("3n", df, new List<QuoteRecord> { a, b });
                    f.TestFriends(false);
                    string msg = f.TestExport(QuoteExport.History);
                    log.Check(f.TestInfoKind == InfoKind.Success, "3g Save to history: " + msg);
                    askedName = null;
                    msg = f.TestExport(QuoteExport.Png);   // the shared WriteExport (optimizer finding 4)
                    log.Check(f.TestInfoKind == InfoKind.Success && askedName != null && File.Exists(Path.Combine(dir, askedName + ".png")),
                              "3g2 quote Save picture \"" + askedName + ".png\": " + msg.Replace("\n", " / "));
                });
                log.Check(ok && a.Customer == "Lene Hansen" && b.Customer == "Lene Hansen" && string.IsNullOrEmpty(a.InvoiceNo) && string.IsNullOrEmpty(b.InvoiceNo),
                          "3h quote: the name on both, no invoice");
                ok = run(new List<QuoteRecord> { a, b }, true, f =>
                {
                    var d = f.TestData();
                    liveTotal = d.GroupTotal;
                    liveBd = bdText(d);
                    string msg = f.TestExport(QuoteExport.Copy);
                    log.Check(f.TestInfoKind == InfoKind.Success, "3i invoice Copy picture: " + msg.Replace("\n", " / "));
                    log.Check(f.TestCustomerLocked, "3j name locked once made (box read-only: " + f.TestCustomerLocked + ", name \"" + f.TestCustomer +
                              "\", invoice " + (a.InvoiceNo ?? "") + " / " + (b.InvoiceNo ?? "") + ", BilledTo \"" + a.BilledTo + "\")");
                });
                log.Check(ok && !string.IsNullOrEmpty(a.InvoiceNo) && a.InvoiceNo == b.InvoiceNo, "3k one number " + codes(new List<QuoteRecord> { a, b }));
                log.Check(ore(a.OthersPrice) + ore(b.OthersPrice) == ore(liveTotal), "3l saved prices " + Num.Kr(a.OthersPrice) + " + " + Num.Kr(b.OthersPrice) +
                          " = the bill's total " + Num.Kr(liveTotal));

                // 4. opening again: one member opens the whole invoice, same numbers even after the minimum price changed
                settings.MinimumPrice = minimum + 30;
                ok = run(new List<QuoteRecord> { a }, true, f =>
                {
                    var d = f.TestData();
                    log.Check(f.Text == L.F("Invoice – {0}", "Faktura – {0}", QuoteRows.PrintsText(2)), "4a one job of it -> \"" + f.Text + "\"");
                    log.Check(f.TestInfoText.Contains(a.InvoiceNo), "4b says the whole invoice is shown: " + f.TestInfoText.Replace("\n", " / "));
                    log.Check(f.TestCustomerLocked && f.TestCustomer == "Lene Hansen", "4c kept name, read-only");
                    log.Check(ore(d.GroupTotal) == ore(liveTotal) && d.InvoiceNo == a.InvoiceNo, "4d same total " + Num.Kr(d.GroupTotal) + " and number " + d.InvoiceNo);
                    log.Check(bdText(d) == liveBd, "4g same breakdown rows as when it was made (" + bdText(d) + " / then " + liveBd + ")");
                    checkBd("4h", d, new List<QuoteRecord> { a, b });
                    // A4: the made combined invoice keeps its billed price; Friends can't be picked, nothing changes
                    int forA = a.For, forB = b.For;
                    double oa = a.OthersPrice, ob = b.OthersPrice;
                    log.Check(f.TestPriceLocked && !f.TestFriendsChosen, "4j price choice locked at Customers");
                    f.TestFriends(true);
                    log.Check(!f.TestFriendsChosen && ore(f.TestData().GroupTotal) == ore(liveTotal), "4k a Friends click is ignored, total still " + Num.Kr(liveTotal));
                    clip = null;
                    string msg4 = f.TestExport(QuoteExport.Text);
                    log.Check(a.For == forA && b.For == forB && a.OthersPrice == oa && b.OthersPrice == ob && a.InvoiceNo == d.InvoiceNo,
                              "4l records unchanged after Copy text (For " + a.For + "/" + b.For + ", " + Num.Kr(oa) + " + " + Num.Kr(ob) + ")");
                    log.Check(clip != null && clip.Contains(Num.Kr(liveTotal)), "4m Copy text holds the customer total " + Num.Kr(liveTotal) + " (" + msg4 + ")");
                });
                log.Check(ok, "4 reopened");
                ok = run(new List<QuoteRecord> { e1, e2 }, true, f =>
                {
                    var d = f.TestData();
                    log.Check(ore(d.GroupTotal) == ore(total1) && d.TopUpAmount == Num.Kr(top1) && d.TopUpLabel == QuoteRows.MinimumLabel(minimum),
                              "4e 18 + 14 again: " + d.TopUpLabel + " " + d.TopUpAmount + ", total " + Num.Kr(d.GroupTotal) + " (minimum now " + Num.Kr(settings.MinimumPrice) + ")");
                    int before = history.Count(r => !string.IsNullOrEmpty(r.InvoiceNo));
                    string msg = f.TestExport(QuoteExport.Text);
                    log.Check(f.TestInfoKind == InfoKind.Success && e1.InvoiceNo == code1 && history.Count(r => !string.IsNullOrEmpty(r.InvoiceNo)) == before,
                              "4f export again keeps " + code1 + " (" + msg + ")");
                    // the minimum is never shown: the Copy text has no minimum line, each job line = its saved price (top-up share inside)
                    var have = d.Jobs.Select(j => j.Amount).OrderBy(x => x).ToList();
                    var saved = new[] { e1, e2 }.Select(r => Num.Kr(r.OthersPrice)).OrderBy(x => x).ToList();
                    log.Check(clip != null && clip.IndexOf("inimum", StringComparison.OrdinalIgnoreCase) < 0 && clip.Contains(Num.Kr(d.GroupTotal)) &&
                              saved.All(x => clip.Contains(x)) && have.SequenceEqual(saved),
                              "4i no minimum shown: job lines " + string.Join(" + ", have) + " = saved prices " + string.Join(" + ", saved) +
                              ", Copy text without a minimum line");
                });
                settings.MinimumPrice = minimum;
                settings.Save();

                // 5. refused: billed + unbilled, two invoices; a quote of a mix is fine
                int inv5 = history.Count(r => !string.IsNullOrEmpty(r.InvoiceNo));
                ok = run(new List<QuoteRecord> { e1, single }, true, f => { });
                log.Check(!ok && h.TestInfoKind == InfoKind.Warning && h.TestInfo.Contains(code1) && string.IsNullOrEmpty(single.InvoiceNo),
                          "5a billed + unbilled refused: " + h.TestInfo.Replace("\n", " / "));
                ok = run(new List<QuoteRecord> { e1, f1 }, true, f => { });
                log.Check(!ok && h.TestInfoKind == InfoKind.Warning, "5b two invoices refused: " + h.TestInfo.Replace("\n", " / "));
                ok = run(new List<QuoteRecord> { e1, single }, false, f => { });
                log.Check(ok && history.Count(r => !string.IsNullOrEmpty(r.InvoiceNo)) == inv5, "5c a quote of the same mix opens, nothing invoiced");

                // 6. failed prints: left off the bill; all failed = nothing opens
                ok = run(new List<QuoteRecord> { g1, x1, g2 }, true, f =>
                {
                    var d = f.TestData();
                    log.Check(d.Jobs.Count == 2 && f.TestInfoText.Contains(L.S("1 failed print is left off the bill.", "1 fejlet print er udeladt af regningen.")),
                              "6a failed one left out: " + d.Jobs.Count + " lines, " + f.TestInfoText.Replace("\n", " / "));
                    checkBd("6d", d, new List<QuoteRecord> { g1, g2 });
                    f.TestSetCustomer("Test Kunde");
                    f.TestExport(QuoteExport.Text);
                });
                log.Check(ok && !string.IsNullOrEmpty(g1.InvoiceNo) && g1.InvoiceNo == g2.InvoiceNo && string.IsNullOrEmpty(x1.InvoiceNo),
                          "6b invoice on the two good ones only: " + codes(new List<QuoteRecord> { g1, g2, x1 }));
                ok = run(new List<QuoteRecord> { x1, x2 }, true, f => { });
                log.Check(!ok && h.TestInfoKind == InfoKind.Warning, "6c all failed: nothing opens (" + h.TestInfo.Replace("\n", " / ") + ")");

                // 7. delete one job of the 3-job invoice: the others keep their number and prices; reopening shows 2
                string codeF = f1.InvoiceNo;
                var sum = History.SummarizeDelete(history, new List<string> { f1.Id });
                log.Check(sum.OnCombined == 1 && sum.Combined.Count == 1 && sum.Combined[0].Left == 2, "7a delete question knows the combined invoice (" +
                          sum.OnCombined + " on it, " + (sum.Combined.Count > 0 ? sum.Combined[0].Left : -1) + " left)");
                h.DoDelete(new List<QuoteRecord> { f1 });
                log.Check(!history.Contains(f1) && f2.InvoiceNo == codeF && f3.InvoiceNo == codeF && ore(f2.OthersPrice) == 2500 && ore(f3.OthersPrice) == 2000,
                          "7b others kept " + codeF + " and their prices");
                ok = run(new List<QuoteRecord> { f2 }, true, f =>
                {
                    var d = f.TestData();
                    log.Check(QuotePage.IsGroup(d) && d.Jobs.Count == 2 && ore(d.GroupTotal) == 4500, "7c reopened: " + (d.Jobs != null ? d.Jobs.Count : 0) +
                              " prints, total " + Num.Kr(d.GroupTotal));
                    checkBd("7d", d, new List<QuoteRecord> { f2, f3 });
                });
                log.Check(ok, "7 reopened after the delete");

                // 8. job cards side by side (3 a row, as many rows on a page as fit), then one breakdown + total + payment kept
                // together after the last row, or on the next page with a note under the cards ("…on the next page →") on
                // every page before it; 3 prints = 1 page when it fits; 4 = all 4 cards on page 1 + the breakdown on page 2
                Func<string, bool> notesRight = lay =>
                {
                    var parts = lay.Split(new[] { " | " }, StringSplitOptions.None);
                    for (int k = 0; k < parts.Length; k++)
                    {
                        bool last = k == parts.Length - 1, note = parts[k].Contains("+note"), tot = parts[k].EndsWith("+total");
                        if (last ? (!tot || note) : (tot || !note)) return false;
                    }
                    return true;
                };
                foreach (int cnt in new[] { 3, 4 })
                {
                    var some = pages.Take(cnt).ToList();
                    foreach (bool inv in new[] { false, true })
                    {
                        var dq = BuildGroupQuote(some, false, "Test Kunde", inv, null, DateTime.Now);
                        bool fitsN;
                        string layN = QuotePage.PageLayout(dq, out fitsN);
                        // 3: one page, or (a long breakdown) the cards + note, then the breakdown page
                        // qol 6: 3 and 4 prints fit ONE page (GroupLevel: shorter breakdown, 4 cards in a row)
                        bool okN = fitsN && notesRight(layN) && layN == "0-" + cnt + "+total" &&
                                   (cnt != 4 || dq.GroupLevel >= 2);   // 4 prints: always 4 cards in one row (user's choice)
                        log.Check(okN, "8g " + cnt + " prints (" + (inv ? "invoice" : "quote") + ") = " + QuotePage.PageCount(dq) +
                                  " A4 page(s): " + layN + " (layout step " + dq.GroupLevel + ")");
                    }
                }
                {
                    // qol 5: the 4-colour single invoice (with a picture) fits A4, so the PDF page isn't shrunk
                    var r4 = new SliceResult { Seconds = 38566, Grams = 135.85, Plates = 1, PurgeGrams = 124.0, ColourChangeHours = 31450 / 3600.0 };
                    string[] cols4 = { "#118002", "#FFFF0A", "#074080", "#ED1C24" };
                    double[] g4 = { 33.04, 55.99, 17.28, 29.54 }, m4 = { 5.04, 0.38, 5.87, 0.56 };
                    for (int i = 0; i < 4; i++)
                        r4.Filaments.Add(new FilamentUse { Slot = i + 1, Colour = cols4[i], Type = "PLA", Profile = "Bambu PLA Basic @BBL A1", Grams = g4[i], ModelGrams = m4[i] });
                    txtUrl.Text = SingleBenchyLink;
                    txtHours.Text = "10";
                    txtMinutes.Text = "43";
                    txtElecPrice.Text = "3,65";
                    SetColours(r4);
                    for (int i = 0; i < colourRows; i++) rowPrice[i].Text = "200";
                    txtGrams.Text = "135,85";
                    jobModel = "Four-Color Multicolor Test Benchy (0.4 nozzle)";
                    SetJobLoaded(true);
                    Recalculate();
                    var d4 = BuildQuote(false, DateTime.Now, History.QuoteNumber(DateTime.Now), "Test Kunde");
                    d4.Picture = Logo;
                    d4.Invoice = true;
                    d4.InvoiceNo = "2026-12345";
                    d4.Due = settings.InvoiceDue(DateTime.Now);
                    d4.PayMobilePay = "12 34 56 78";
                    d4.PayBank = "reg. 1234, account 0012345678";
                    bool compact4;
                    float h4;
                    bool fits4 = QuotePage.SingleFits(d4, out compact4, out h4);
                    log.Check(fits4, "8h 4-colour single invoice with a picture fits A4: " + Num.Fmt(Math.Round(h4)) + " of " + Num.Fmt(QuotePage.A4Height) +
                              (compact4 ? " (compact form)" : ""));
                    // qol 8: four rows of one profile are told apart by colour name
                    log.Check(d4.MaterialNames.Count == 4 && d4.MaterialNames.Distinct().Count() == 4 && d4.MaterialNames[0].Contains("("),
                              "8i colour names on same-profile rows: " + string.Join(" | ", d4.MaterialNames));
                }
                ok = run(pages, false, f =>
                {
                    var d = f.TestData();
                    log.Check(QuotePage.PageCount(d) == 2, "8a 12 prints = " + QuotePage.PageCount(d) + " A4 pages");
                    checkBd("8e", d, pages);
                    bool fits;
                    string lay = QuotePage.PageLayout(d, out fits);
                    log.Check(fits && notesRight(lay) && lay == "0-9+note | 9-12+total",
                              "8f 3 rows of cards + the note on page 1, the last row + breakdown + total + payment on page 2: " + lay);
                    f.TestSetCustomer("Test Kunde");
                    string msg = f.TestExport(QuoteExport.Pdf);
                    log.Check(f.TestInfoKind == InfoKind.Success, "8b PDF: " + msg.Replace("\n", " / "));
                });
                string pdf12 = Path.Combine(dir, QuoteRows.GroupFileName(DateTime.Now, 12, "Test Kunde") + ".pdf");
                int n12 = pdfPages(pdf12);
                string pdfText = "";
                try { pdfText = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf12)); } catch { }
                log.Check(ok && n12 == 2 && pdfText.Contains("/Count 2"), "8c " + Path.GetFileName(pdf12) + ": " + n12 + " pages");
                log.Check(pages.All(r => string.IsNullOrEmpty(r.InvoiceNo) && r.Customer == "Test Kunde"), "8d quote: names on all 12, no invoice");
                log.Check(!File.Exists(Path.Combine(invDir, Path.GetFileName(pdf12))) &&
                          (!Directory.Exists(invDir) || !Directory.GetFiles(invDir).Any(p => Path.GetFileName(p).Contains(QuoteRows.PrintsText(12)))),
                          "8g a combined quote PDF keeps the date name and gets no invoice copy");

                // 9. one job = the single-job invoice as before (own minimum, no share)
                ok = run(new List<QuoteRecord> { single }, true, f =>
                {
                    var d = f.TestData();
                    log.Check(f.Text == L.F("Invoice – {0}", "Faktura – {0}", "Bill single") && !QuotePage.IsGroup(d), "9a single window \"" + f.Text + "\"");
                    log.Check(d.Breakdown != null && ore(d.Breakdown.Total) == ore(singlePrice), "9b its saved price " + Num.Kr(singlePrice));
                    bool fits1;
                    log.Check(QuotePage.PageCount(d) == 1 && QuotePage.PageLayout(d, out fits1) == "single" && d.Breakdown.Lines.Sum(l => l.Ore) == ore(singlePrice),
                              "9h single page with its own breakdown (rows sum to its price)");
                    f.TestSetCustomer("Test Kunde");
                    string msg = f.TestExport(QuoteExport.Pdf);
                    log.Check(f.TestInfoKind == InfoKind.Success, "9c PDF: " + msg.Replace("\n", " / "));
                    log.Check(f.TestCustomerLocked, "9g single: name locked once made (read-only: " + f.TestCustomerLocked + ", name \"" + f.TestCustomer + "\")");
                });
                string pdfS = Path.Combine(dir, (single.InvoiceNo ?? "") + " - Test Kunde.pdf");
                log.Check(ok && !string.IsNullOrEmpty(single.InvoiceNo) && single.InvoiceTopUp == 0 && ore(single.OthersPrice) == ore(singlePrice) &&
                          pdfPages(pdfS) == 1, "9d single invoice " + single.InvoiceNo + ": price unchanged, no share, " + pdfPages(pdfS) + " page");
                string copyS = Path.Combine(invDir, (single.InvoiceNo ?? "") + " - Test Kunde.pdf");
                log.Check(File.Exists(copyS) && sameFile(copyS, pdfS), "9i single invoice copy: Invoices\\" + Path.GetFileName(copyS));                log.Check(History.InvoiceGroup(history, single.InvoiceNo).Count == 1, "9e its number is on that job only");
                log.Check(asked.Count == 1 && ReferenceEquals(asked[0], single), "9f History handed over the selected record");

                // 10. issue #7: one status box for a combined window, saved on every job at once; several to Failed asks
                {
                    BackupForm.AskForm.TestAnswers = new Queue<DialogResult>();
                    BackupForm.AskForm.TestLog = log;
                    var three = pages.Take(3).ToList();
                    DateTime n10 = DateTime.Now;
                    foreach (var r in three) History.SetPrintState(r, PrintState.NotPrinted, n10);
                    History.SetPrintState(three[0], PrintState.Printed, n10);
                    History.Save(history);
                    Func<string> states = () => string.Join(",", three.Select(r => History.PrintStateOf(r, DateTime.Now).ToString()));
                    Func<string> savedStates = () =>
                    {
                        string p;
                        var back = History.Load(out p);
                        if (back == null) return "unreadable";
                        return string.Join(",", three.Select(r => { var s = back.FirstOrDefault(x => x.Id == r.Id); return s == null ? "missing" : History.PrintStateOf(s, DateTime.Now).ToString(); }));
                    };
                    ok = run(three, false, f =>
                    {
                        log.Check(f.TestStatusShown && f.TestStatusMixed, "10a 1 printed + 2 not: the box says Mixed (" + f.TestStatusHeading + ")");
                        f.TestPickStatus(PrintState.Printing);
                        log.Check(states() == "Printing,Printing,Printing" && savedStates() == states() && !f.TestStatusMixed &&
                                  f.TestInfoText.StartsWith(L.S("✓ Print status saved", "✓ Printstatus gemt")),
                                  "10b Printing now: on all 3 and saved at once (" + savedStates() + "; " + f.TestInfoText + ")");
                        BackupForm.AskForm.TestAnswers.Enqueue(DialogResult.No);
                        f.TestPickStatus(PrintState.Failed);
                        log.Check(states() == "Printing,Printing,Printing" && f.PrintStatus == PrintState.Printing &&
                                  BackupForm.AskForm.TestAnswers.Count == 0,
                                  "10c Failed for 3 asked, No: nothing changed, the box back at " + QuoteRows.StateName(f.PrintStatus));
                        BackupForm.AskForm.TestAnswers.Enqueue(DialogResult.Yes);
                        f.TestPickStatus(PrintState.Failed);
                        log.Check(states() == "Failed,Failed,Failed" && savedStates() == states() && f.PrintStatus == PrintState.Failed,
                                  "10d Failed for 3 asked, Yes: all failed and saved (" + savedStates() + ")");
                        f.TestPickStatus(PrintState.NotPrinted);
                        log.Check(states() == "NotPrinted,NotPrinted,NotPrinted" && savedStates() == states() && BackupForm.AskForm.TestAnswers.Count == 0,
                                  "10e back to Not printed yet: no question, saved (" + savedStates() + ")");
                    });
                    log.Check(ok, "10 the combined quote window opened");
                    // a combined invoice: the box is there and shows the Printed ticks once made
                    var two = pages.Skip(3).Take(2).ToList();
                    ok = run(two, true, f =>
                    {
                        log.Check(f.TestStatusShown && f.PrintStatus == PrintState.NotPrinted, "10f combined invoice: status box shown (" + QuoteRows.StateName(f.PrintStatus) + ")");
                        f.TestSetCustomer("Test Kunde");
                        f.TestExport(QuoteExport.Text);
                        log.Check(!f.TestStatusMixed && f.PrintStatus == PrintState.Printed && two.All(r => r.Printed),
                                  "10g made: both Printed, the box shows " + QuoteRows.StateName(f.PrintStatus));
                    });
                    log.Check(ok, "10 the combined invoice window opened");
                    BackupForm.AskForm.TestAnswers = null;
                    BackupForm.AskForm.TestLog = null;
                }

                // 11. issue #7: the Invoices window (History's Invoices… / Ctrl+I)
                {
                    log.Add("11 the Invoices window");
                    BackupForm.AskForm.TestAnswers = new Queue<DialogResult>();
                    BackupForm.AskForm.TestLog = log;
                    DateTime now11 = DateTime.Now;
                    // a. the made-up invoices (picture-mode list; offline History, so nothing is saved)
                    var sample = HistoryForm.SampleInvoiceQuotes();
                    using (var sh = new HistoryForm(sample, null, true))
                    {
                        sh.Settings = settings;
                        sh.Show();
                        Application.DoEvents();
                        var f = sh.NewInvoicesForm();
                        f.Show(sh);
                        Application.DoEvents();
                        var want = History.Invoices(sample, now11, settings);
                        log.Check(f.TestRowCount == 6 && want.Count == 6 && f.TestCount.StartsWith(L.S("6 invoices", "6 fakturaer")),
                                  "11a 6 invoices (7 billed prints), one row each (" + f.TestCount + ")");
                        var comb = f.TestRows.FirstOrDefault(r => r.No == "2026-51907");
                        log.Check(comb != null && comb.Jobs.Count == 2 && comb.State == null && comb.Friends,
                                  "11b the combined friends invoice is one row of 2 prints, Mixed");
                        int ci = f.TestRows.IndexOf(comb);
                        log.Check(ci >= 0 && f.TestCell(ci, 2).StartsWith(L.S("2 prints: ", "2 print: ")) &&
                                  f.TestCell(ci, HistoryForm.InvoicesForm.ColAge) == History.AgeText(comb.BilledAt, now11),
                                  "11c its Prints / Created cells: \"" + (ci >= 0 ? f.TestCell(ci, 2) + "\", \"" + f.TestCell(ci, HistoryForm.InvoicesForm.ColAge) : "") + "\"");
                        var jonas = f.TestRows.FirstOrDefault(r => r.No == "2026-39774");
                        log.Check(jonas != null && jonas.Overdue(now11), "11d Jonas Berg's invoice (due 14 days after 06-10-2026) is past its due date");
                        f.TestSearch("mads");
                        log.Check(f.TestRowCount == 1 && f.TestRows[0].No == "2026-51907", "11e search \"mads\": 1 row (" + f.TestCount + ")");
                        f.TestSearch("2026-4");
                        log.Check(f.TestRowCount == 1 && f.TestRows[0].No == "2026-48213", "11f search by number: 1 row");
                        f.TestSearch("");
                        f.TestStatusFilter(5);
                        log.Check(f.TestRowCount == 1 && f.TestRows[0].No == "2026-51907", "11g Status Mixed: 1 row (" + f.TestCount + ")");
                        f.TestStatusFilter(4);
                        log.Check(f.TestRowCount == 0, "11h Status Failed: 0 rows (" + f.TestCount + ")");
                        f.TestStatusFilter(0);
                        DateTime from, to;
                        History.InvoicePeriod(History.PeriodLast30, now11, out from, out to);
                        int want30 = want.Count(r => History.InvoiceMatches(r, "", History.InvoiceStatusAll, from, to));
                        f.TestPeriod(2);
                        log.Check(f.TestRowCount == want30, "11i Period last 30 days: " + f.TestRowCount + " rows (expected " + want30 + ")");
                        f.TestPeriod(0);
                        int dot;
                        int items = f.TestStatusMenu(f.TestRows.IndexOf(f.TestRows.First(r => r.No == "2026-51907")), out dot);
                        log.Check(items == 4 && dot == -1, "11j Status menu of the Mixed invoice: 4 choices, none marked");
                        comb = f.TestRows.First(r => r.No == "2026-51907");
                        f.SetStatus(comb, PrintState.Printed);
                        var combJobs = sample.Where(r => r.InvoiceNo == "2026-51907").ToList();
                        log.Check(combJobs.All(r => History.PrintStateOf(r, DateTime.Now) == PrintState.Printed) &&
                                  f.TestRows.First(r => r.No == "2026-51907").State == PrintState.Printed && f.TestInfoKind == InfoKind.Success,
                                  "11k Printed: both prints printed (" + f.TestInfoText + ")");
                        BackupForm.AskForm.TestAnswers.Enqueue(DialogResult.No);
                        f.SetStatus(f.TestRows.First(r => r.No == "2026-51907"), PrintState.Failed);
                        log.Check(combJobs.All(r => History.PrintStateOf(r, DateTime.Now) == PrintState.Printed) && BackupForm.AskForm.TestAnswers.Count == 0,
                                  "11l Failed for 2 asked, No: unchanged");
                        BackupForm.AskForm.TestAnswers.Enqueue(DialogResult.Yes);
                        f.SetStatus(f.TestRows.First(r => r.No == "2026-51907"), PrintState.Failed);
                        log.Check(combJobs.All(r => r.Failed) && BackupForm.AskForm.TestAnswers.Count == 0, "11m Failed for 2 asked, Yes: both failed");
                        f.TestStatusFilter(4);
                        f.TestSelect("2026-51907");
                        f.SetStatus(f.TestSelected, PrintState.NotPrinted);
                        log.Check(f.TestRowCount == 0 && combJobs.All(r => !r.Failed && !r.Printed) &&
                                  f.TestInfoText.Contains(L.S("no longer shows", "vises ikke længere")),
                                  "11n under Status Failed, set Not printed: it leaves the list (" + f.TestInfoText + ")");
                        f.TestStatusFilter(0);
                        string clip11 = null;
                        HistoryForm.InvoicesForm.testClipboard = t => clip11 = t;
                        f.TestSelect("2026-73096");
                        f.TestCopy();
                        HistoryForm.InvoicesForm.testClipboard = null;
                        log.Check(clip11 == "2026-73096", "11o Copy invoice number: " + clip11);
                        f.TestSelect("2026-51907");
                        f.TestShowInHistory();
                        Application.DoEvents();
                        sh.AfterInvoices();
                        var sel = sh.SampleSelected();
                        log.Check(f.IsDisposed || !f.Visible, "11p Show in History closes the window");
                        log.Check(sel.Count == 2 && sel.All(r => r.InvoiceNo == "2026-51907") && sh.TestInfo.Contains("2026-51907"),
                                  "11q History selects its 2 prints (" + sh.TestInfo + ")");
                        if (!f.IsDisposed) f.Dispose();
                        // paid-tick (release-test-5): the combined invoice partly paid = unpaid (Please pay by + Payment block);
                        // all paid = "Paid … – thank you", no Payment details on the page or in Copy text
                        string keepMp = settings.PayMobilePay;
                        try
                        {
                            settings.PayMobilePay = "12 34 56 78";
                            var cj = sample.Where(r => r.InvoiceNo == "2026-51907").ToList();
                            foreach (var j in cj) { j.PaidAt = ""; j.InvoicePayMobilePay = "12 34 56 78"; }   // the made invoice's snapshot
                            cj[0].PaidAt = now11.ToString("yyyy-MM-dd");
                            Func<string> gtext = () => QuoteRows.GroupText(History.GroupBill(cj, settings, cj[0].For == History.ForFriends), true, "2026-51907",
                                                                           "Mads", now11, History.InvoiceInfo(cj[0], settings, now11));
                            var gq = BuildGroupQuote(cj, cj[0].For == History.ForFriends, "Mads", true, null, now11);
                            string gt = gtext();
                            log.Check(gq.PaidLine == "" && gt.Contains("MobilePay") && gt.Contains(L.S("please pay by", "betales senest")),
                                      "11t combined, 1 of 2 paid: still unpaid (Please pay by + MobilePay): line \"" + gq.PaidLine + "\", text \"" + gt.Replace(Environment.NewLine, " / ") + "\"");
                            var rows = History.Invoices(sample, now11, settings);
                            var pr = rows.FirstOrDefault(r => r.No == "2026-51907");
                            log.Check(pr != null && !pr.Paid, "11u the Invoices row of a partly paid combined invoice is Unpaid");
                            cj[1].PaidAt = now11.AddDays(-1).ToString("yyyy-MM-dd");
                            gq = BuildGroupQuote(cj, cj[0].For == History.ForFriends, "Mads", true, null, now11);
                            gt = gtext();
                            string pl = History.PaidText(now11.Date);
                            log.Check(gq.PaidLine == pl && gt.Contains(pl) && !gt.Contains("MobilePay") && !gt.Contains(L.S("please pay by", "betales senest")),
                                      "11v combined, all paid: \"" + gq.PaidLine + "\" (latest date), no MobilePay / Please pay by in Copy text");
                            using (var bmp = QuotePage.Render(gq, false))
                                bmp.Save(Path.Combine(AppSettings.Folder, "test_bill_paid_group.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        finally { settings.PayMobilePay = keepMp; }
                    }
                    // r. the real (test) list: Open invoice opens the invoice window of that number
                    {
                        var f = h.NewInvoicesForm();
                        f.Show(h);
                        Application.DoEvents();
                        int want = History.Invoices(history, DateTime.Now, settings).Count;
                        log.Check(f.TestRowCount == want && want > 0, "11r the test list: " + f.TestRowCount + " invoice rows (expected " + want + ")");
                        if (f.TestRowCount > 0)
                        {
                            var first = f.TestRows[0];
                            string gotNo = null;
                            onWindow = w => { gotNo = w.TestData().InvoiceNo; };
                            f.TestSelect(first.No);
                            f.TestOpen();
                            onWindow = null;
                            log.Check(gotNo != null && string.Equals(gotNo.Trim(), first.No, StringComparison.OrdinalIgnoreCase),
                                      "11s Open invoice " + first.No + ": the invoice window opened with " + (gotNo ?? "none"));
                        }
                        f.Close();
                        f.Dispose();
                    }
                    BackupForm.AskForm.TestAnswers = null;
                    BackupForm.AskForm.TestLog = null;
                }
            }
            catch (Exception ex) { log.Check(false, "exception: " + ex); }
            finally
            {
                testRecordsForm = null;
                testClipboard = null;
                testExportPath = null;
                if (hist != null) { try { hist.Close(); hist.Dispose(); } catch { } }
            }
            try { File.WriteAllLines(Path.Combine(dir, "test_bill.txt"), log.Lines); } catch { }
            Program.EndTestRun(log.Ok ? 0 : 4);
        }

        /// --test-history-read (test mode + --hidden only, alone; optimizer finding 2): history.json is read once per run.
        /// Each case writes made-up quotes (HistoryForm.SampleQuotes) to the test folder's history.json, runs the start-up
        /// read (StartupPictureCleanAsync), maybe changes the file, then EnsureHistory, and checks History.FileReads and
        /// the content. Results in <folder>\test_history_read.txt; exit 0 / 4.
        internal async void TestHistoryReadFlow()
        {
            if (!AppSettings.TestMode || !Program.Hidden) return;
            string dir = AppSettings.Folder;
            string path = History.FilePath;
            var log = new TestLog();
            try
            {
                if (startupRead != null) await startupRead;   // the Shown run on the empty test folder
                var sample = HistoryForm.SampleQuotes();
                string sampleJson = History.ToJson(sample);
                int moved = 0;
                // fresh start for a case: history.json = text (null = no file; an old one is moved aside, never deleted)
                Func<string, Task> start = async text =>
                {
                    history = null; historyProblem = null; historyAhead = null;
                    if (File.Exists(path)) File.Move(path, Path.Combine(dir, "history.moved-" + (++moved) + ".json"));
                    if (text != null) File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
                    await StartupPictureCleanAsync();
                };
                Func<string> fileJson = () => History.ToJson(History.ParseJson(File.ReadAllText(path, System.Text.Encoding.UTF8)));
                int reads;

                // 1. unchanged file: the start-up read is used, no second read, same content, saving allowed
                await start(sampleJson);
                log.Check(historyAhead != null && historyAhead.List != null, "1a start-up read kept (" + sample.Count + " quotes)");
                reads = History.FileReads;
                EnsureHistory();
                log.Check(History.FileReads == reads, "1b EnsureHistory read the file " + (History.FileReads - reads) + " more times (want 0)");
                log.Check(historyProblem == null, "1c no problem" + (historyProblem != null ? " (" + historyProblem + ")" : ""));
                log.Check(history != null && History.ToJson(history) == fileJson(), "1d same content as the file (" + (history != null ? history.Count : -1) + " quotes)");
                log.Check(historyAhead == null, "1e start-up read handed out once");
                log.Check(History.Save(history) == null, "1f saving works afterwards");

                // 2. file touched after the start-up read (new write time): read again
                await start(sampleJson);
                File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
                reads = History.FileReads;
                EnsureHistory();
                log.Check(History.FileReads == reads + 1, "2a touched file read again (" + (History.FileReads - reads) + " reads)");
                log.Check(historyProblem == null && history != null && History.ToJson(history) == fileJson(), "2b same content, no problem");

                // 3. same size, one letter changed (new write time): read again, the change shows
                await start(sampleJson);
                var changed = HistoryForm.SampleQuotes();
                int ci = changed.FindIndex(r => !string.IsNullOrEmpty(r.Customer) && r.Customer[0] < 128);
                string name = ci >= 0 ? changed[ci].Customer : "";
                if (ci >= 0) changed[ci].Customer = (name[0] == 'X' ? "Y" : "X") + name.Substring(1);
                string changedJson = History.ToJson(changed);
                File.WriteAllText(path, changedJson, new System.Text.UTF8Encoding(false));
                File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
                log.Check(ci >= 0 && changedJson.Length == sampleJson.Length,
                          "3a changed file has the same size (customer \"" + (ci >= 0 ? changed[ci].Customer : "none") + "\")");
                reads = History.FileReads;
                EnsureHistory();
                log.Check(History.FileReads == reads + 1, "3b changed file read again (" + (History.FileReads - reads) + " reads)");
                log.Check(ci >= 0 && history != null && history.Count > ci && history[ci].Customer == changed[ci].Customer, "3c the change shows");

                // 4. bad file: no start-up read; Load renames it and gives the problem text, as before
                await start("{ not a history");
                log.Check(historyAhead == null, "4a bad file: start-up read not kept");
                EnsureHistory();
                bool kept = Directory.GetFiles(dir, "history.bad-*.json").Length > 0;
                log.Check(historyProblem != null && kept && history != null && history.Count == 0,
                          "4b renamed to history.bad-* with a problem text (" + (historyProblem ?? "none") + ")");

                // 5. no file: an empty list, no problem, no read
                await start(null);
                reads = History.FileReads;
                EnsureHistory();
                log.Check(History.FileReads == reads && historyProblem == null && history != null && history.Count == 0,
                          "5 no file: empty list, no problem, " + (History.FileReads - reads) + " reads");
            }
            catch (Exception ex) { log.Check(false, "error: " + ex.Message); }
            try { File.WriteAllLines(Path.Combine(dir, "test_history_read.txt"), log.Lines); } catch { }
            Program.EndTestRun(log.Ok ? 0 : 4);
        }

        /// Test run "--settings <folder> --hidden --test-pictures" (issue #4), only in test mode: a job with a picture
        /// (<folder>\model.png, else a drawn one) is saved, cleared, opened again, its quote and invoice drawn, backed up
        /// to a file, its picture deleted and the backup restored (Merge), then opened again. Results in
        /// <folder>\test_pictures.txt (PASS / FAIL lines) and test_*.png; exit code 0 = all passed, 4 = a FAIL.
        internal void TestPictureFlow()
        {
            if (!AppSettings.TestMode || !Program.Hidden) return;
            string dir = AppSettings.Folder;
            var log = new List<string>();
            bool ok = true;
            Action<bool, string> check = (pass, what) => { log.Add((pass ? "PASS " : "FAIL ") + what); if (!pass) ok = false; };
            log.Add(SetupTest.StandInNote);
            try
            {
                var one = new SliceResult { Seconds = 38 * 60, Grams = 11.8, Plates = 1 };
                one.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#F07850", Type = "PLA", Profile = "Bambu PLA Matte @BBL A1", Grams = 11.8 });
                SetColours(one);
                txtUrl.Text = SingleBenchyLink;
                txtHours.Text = "0";
                txtMinutes.Text = "38";
                txtGrams.Text = "11,8";
                rowPrice[0].Text = "200";
                txtElecPrice.Text = "3,65";
                jobModel = "3D Benchy";
                SetJobLoaded(true);
                string png = Path.Combine(dir, "model.png");
                SetModelPicture(File.Exists(png) ? PrepareModelPicture(File.ReadAllBytes(png)) : TestPicture());
                Recalculate();
                QuoteRecord rec;
                string err = SaveToHistory(0, null, out rec);
                check(err == null && rec != null, "saved to history" + (err != null ? ": " + err : ""));
                check(rec != null && History.HasPicture(rec.Id), "picture saved with the record (pictures\\<Id>)");
                Screenshots.Snap(this, dir, "test_1_saved.png");

                ClearJob();
                check(modelImage == null, "cleared: no picture on screen");
                Reopen(rec);
                check(modelImage != null, "Open again shows the saved picture");
                Screenshots.Snap(this, dir, "test_2_reopened.png");

                bool owned;
                var pic = RecordPicture(rec, out owned);
                check(pic != null && owned, "quote / invoice picture comes from the saved picture");
                foreach (bool invoice in new[] { false, true })
                    using (var page = QuotePage.Render(BuildRecordQuote(rec, false, "Test Customer", invoice, pic), false))
                        page.Save(Path.Combine(dir, invoice ? "test_4_invoice.png" : "test_3_quote.png"), System.Drawing.Imaging.ImageFormat.Png);
                if (owned && pic != null) pic.Dispose();

                DateTime now = DateTime.Now;
                var pics = History.BackupPictures(history);
                check(pics.Count == 1, "BackupPictures: " + pics.Count + " picture(s)");
                string file = Path.Combine(dir, "test.printprice-backup");
                err = History.WriteBackupFile(file, history, settings, Updater.CurrentText, now, pics);
                check(err == null && File.ReadAllText(file).Contains("\"Pictures\""), "file backup written with its pictures" + (err != null ? ": " + err : ""));
                string online = Backup.FitForUpload(history, settings, Updater.CurrentText, now, pics, OnlineBackup.MaxBlob);
                check(online != null && online.Contains("\"Pictures\""), "online backup text keeps the picture (" + (online ?? "").Length + " chars)");

                History.DeletePicture(rec.Id);
                check(!History.HasPicture(rec.Id), "picture deleted before the restore");
                BackupData data;
                string problem = History.ReadBackupFile(file, out data);
                check(problem == null && data != null && data.Pictures.Count == 1, "backup read: " + (problem ?? (data.Pictures.Count + " picture(s)")));
                RestoreResult res = null;
                if (data != null) problem = History.Restore(history, settings, data, false, false, now, out res);
                check(res != null, "restored (Merge)" + (problem != null ? ": " + problem : ""));
                if (res != null) AdoptRestore(res);
                check(History.HasPicture(rec.Id), "restore wrote the picture back");
                ClearJob();
                Reopen(history.Find(x => x.Id == rec.Id));
                check(modelImage != null, "Open again after the restore shows the picture");
                Screenshots.Snap(this, dir, "test_5_restored_reopened.png");
                check(History.CleanPictures(history) == 0, "CleanPictures keeps the record's picture");
                check(History.HasPicture(rec.Id), "picture still there after CleanPictures");
            }
            catch (Exception ex) { check(false, "exception: " + ex); }
            try { File.WriteAllLines(Path.Combine(dir, "test_pictures.txt"), log); } catch { }
            Program.EndTestRun(ok ? 0 : 4);
        }

        // ---------- test hooks for idea 21 (test mode + --hidden only) ----------
        /// The last finished job's slice (set by ShowJobResult), for the test hooks.
        SliceResult lastJobResult;

        /// "--test-slice <spec>": slices the spec's files with a filament / colour each (no picker), like the picker's
        /// result, then saves the job. Spec lines: "file|filament|#RRGGBB" (filament / colour may be empty; a relative
        /// file is next to the spec), "together=1"; blank lines and "//" lines skipped. Writes <folder>\test_slice.txt and
        /// test_slice.png; exit 0 = sliced and saved with its parts, 4 = not.
        internal async void TestSliceFlow(string specFile)
        {
            if (!AppSettings.TestMode || !Program.Hidden) return;
            string dir = AppSettings.Folder;
            var log = new List<string>();
            bool ok = true;
            Action<bool, string> check = (pass, what) => { log.Add((pass ? "PASS " : "FAIL ") + what); if (!pass) ok = false; };
            log.Add(SetupTest.StandInNote);
            try
            {
                var parts = new List<HistoryPart>();
                bool together = false;
                double closeAfter = 0;   // "close-after=<s>": close the window mid-slice (A5: Bambu Studio must stop too)
                ScaleXyz specScale = ScaleXyz.None, specRescale = null;
                bool? specTower = null;     // issue #8 step 3: "tower=on|off" = the first slice's switch (else Settings)
                bool toggleTower = false;   // "toggle-tower=1": then the tower link (slices again with the other switch)
                bool expectTooSmall = false, expectTooBig = false;   // expect=too-big: refused by the bed check / Scale window, nothing saved   // "expect-too-small=1": scale= (or rescale=) fails with BambuSlicer.TooSmallText (plans\too-small)
                int specCopies = 0;          // batch & plates: "copies=<n>"
                string expectPlates = null;  // "expect-plates=28,12"
                string baseDir = Path.GetDirectoryName(specFile);
                foreach (var raw in File.ReadAllLines(specFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("//")) continue;
                    if (line.StartsWith("together=", StringComparison.OrdinalIgnoreCase)) { together = line.Substring(9).Trim() == "1"; continue; }
                    // issue #8: "scale=<pct>" slices at that size; "rescale=<pct>" slices at 100 % and then again through
                    // the Scale window's path (RescaleAsync)
                    if (line.StartsWith("scale=", StringComparison.OrdinalIgnoreCase))
                    {
                        // step 2: "150" = uniform, "150,100,50" = X / Y / Z (ScaleXyz.TryParse)
                        if (!ScaleXyz.TryParse(line.Substring(6), out specScale)) { check(false, "bad spec line: " + line); specScale = ScaleXyz.None; }
                        continue;
                    }
                    if (line.StartsWith("rescale=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!ScaleXyz.TryParse(line.Substring(8), out specRescale)) { check(false, "bad spec line: " + line); specRescale = null; }
                        continue;
                    }
                    if (line.StartsWith("tower=", StringComparison.OrdinalIgnoreCase))
                    {
                        string v = line.Substring(6).Trim().ToLowerInvariant();
                        if (v == "on" || v == "1") specTower = true;
                        else if (v == "off" || v == "0") specTower = false;
                        else check(false, "bad spec line: " + line);
                        continue;
                    }
                    if (line.StartsWith("toggle-tower=", StringComparison.OrdinalIgnoreCase)) { toggleTower = line.Substring(13).Trim() == "1"; continue; }
                    // batch & plates: "copies=<n>" = after the slice, the Copies window's Apply (slices again with n copies);
                    // "expect-plates=28,12" = the copies on each plate then
                    if (line.StartsWith("copies=", StringComparison.OrdinalIgnoreCase)) { int.TryParse(line.Substring(7).Trim(), out specCopies); continue; }
                    if (line.StartsWith("expect-plates=", StringComparison.OrdinalIgnoreCase)) { expectPlates = line.Substring(14).Trim(); continue; }
                    if (line.Equals("expect=too-big", StringComparison.OrdinalIgnoreCase)) { expectTooBig = true; continue; }
                    if (line.StartsWith("expect-too-small=", StringComparison.OrdinalIgnoreCase)) { expectTooSmall = line.Substring(17).Trim() == "1"; continue; }
                    if (line.StartsWith("close-after=", StringComparison.OrdinalIgnoreCase))
                    {
                        double.TryParse(line.Substring(12).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out closeAfter);
                        continue;
                    }
                    var f = line.Split('|');
                    string file = f[0].Trim().Trim('"');
                    if (!Path.IsPathRooted(file)) file = Path.GetFullPath(Path.Combine(baseDir, file));
                    parts.Add(new HistoryPart { File = file, Filament = f.Length > 1 ? f[1].Trim() : "", Colour = f.Length > 2 ? f[2].Trim() : "" });
                }
                log.Add("spec: " + parts.Count + " file(s), together=" + (together ? 1 : 0));
                check(parts.Count > 0 && parts.All(p => File.Exists(p.File)), "spec files exist");
                if (ok && closeAfter > 0)
                {
                    // start the job without waiting, close the window through the real close path after N s
                    var files = parts.Select(p => p.File).ToList();
                    txtUrl.Text = files.Count == 1 ? files[0] : L.F("{0} files: ", "{0} filer: ", files.Count) + string.Join(", ", files.Select(Path.GetFileName));
                    var job = AnalyzeAsync(null, files, null, parts, together);
                    await Task.Delay(TimeSpan.FromSeconds(closeAfter));
                    var ids = BambuSlicer.StartedIds;
                    log.Add("close-after=" + closeAfter.ToString(System.Globalization.CultureInfo.InvariantCulture) + " s; job finished before close: " + job.IsCompleted);
                    log.Add("started ids: " + string.Join(",", ids));
                    log.Add("running before close: " + ids.Count(RunningProcess));
                    Close();   // FormClosing: cts.Cancel + BambuSlicer.StopAll + SaveLastPrices
                    int left = ids.Count(RunningProcess);
                    log.Add("running after close: " + left);
                    check(ids.Count > 0, "Bambu Studio was started");
                    check(left == 0, "no Bambu Studio left running after close");
                    try { File.WriteAllLines(Path.Combine(dir, "test_slice.txt"), log); } catch { }
                    Program.EndTestRun(ok ? 0 : 4);
                    return;
                }
                if (ok)
                {
                    var files = parts.Select(p => p.File).ToList();
                    txtUrl.Text = files.Count == 1 ? files[0] : L.F("{0} files: ", "{0} filer: ", files.Count) + string.Join(", ", files.Select(Path.GetFileName));
                    lastJobResult = null;
                    EnsureHistory();
                    int historyBefore = history != null ? history.Count : 0;
                    if (specScale.IsScaled || specRescale != null)
                        log.Add("spec: scale: " + specScale.Text + (specRescale != null ? ", rescale: " + specRescale.Text : ""));
                    if (specTower.HasValue) { towerForNextSlice = specTower.Value; log.Add("spec: tower " + (specTower.Value ? "on" : "off")); }
                    bool done = await AnalyzeAsync(null, files, null, parts, together, specScale, null);
                    if (!done) log.Add("failed: " + (lblAuto.Text ?? "").Replace("\n", " / "));   // too small: BambuSlicer.TooSmallText
                    if (expectTooSmall && specRescale == null)
                    {
                        // "expect-too-small=1" with scale=: the first slice fails with the too-small text (no mm: size unknown)
                        string want = TooSmallMessage(specScale, false);
                        check(!done && (lblAuto.Text ?? "").Contains(want), "too small at " + specScale.Text + ": \"" + want + "\"");
                        try { File.WriteAllLines(Path.Combine(dir, "test_slice.txt"), log); } catch { }
                        Screenshots.Snap(this, dir, "test_slice.png");
                        Program.EndTestRun(ok ? 0 : 4);
                        return;
                    }
                    bool skipSave = false;   // a too-big refusal / an expected failure: nothing is saved
                    if (expectTooBig && specRescale == null && !done)
                    {
                        // too big for the slicer itself: fine for expect=too-big (nothing to save)
                        check(true, "not sliced (too big): " + (lblAuto.Text ?? "").Replace("\n", " / "));
                        skipSave = true;
                    }
                    else check(done && lastJobResult != null, "sliced");
                    check(!done || jobTower == (specTower ?? SettingsTower), "sliced with tower " + ((specTower ?? SettingsTower) ? "on" : "off"));
                    TestJobLines(log, lastJobResult);
                    // bed check (plans\paid-tick part B): scale= sliced at a size that doesn't fit the printer is refused, as the
                    // Scale window would refuse it (the same ScaleForm.FitsAt); expect=too-big wants that refusal
                    if (done && specRescale == null && lastJobResult != null)
                    {
                        var r0 = lastJobResult;
                        var one = new double[] { 100, 100, 100 };
                        bool fits = ScaleForm.FitsAt(settings.PrinterModel, new[] { r0.SizeX, r0.SizeY, r0.SizeZ }, one, one);
                        if (!fits)
                        {
                            log.Add("refused: " + BambuSlicer.TooBigText(settings.PrinterModel) + " (sliced size " + BambuSlicer.SizeText(r0.SizeX, r0.SizeY, r0.SizeZ) + ")");
                            skipSave = true;
                        }
                        if (expectTooBig) check(!fits, "too big for the bed: refused, nothing saved");
                        else check(fits, "fits the bed" + (r0.SizeX > 0 ? " (" + BambuSlicer.SizeText(r0.SizeX, r0.SizeY, r0.SizeZ) + ")" : " (size unknown)"));
                    }
                    if (done && toggleTower)
                    {
                        // the job's "Prime tower: on/off" link: the same files, size and colours again with the other switch
                        bool before = jobTower;
                        var firstRes = lastJobResult;   // kept: CanTowerJob reads it, as on a real click
                        double gramsBefore = firstRes != null ? firstRes.Grams : 0;
                        check(CanTowerJob, "tower link offered after the slice (2+ colours)");
                        done = await ToggleTowerAsync();
                        check(done && lastJobResult != null && !ReferenceEquals(lastJobResult, firstRes) && jobTower == !before,
                              "sliced again with tower " + (!before ? "on" : "off"));
                        TestJobLines(log, lastJobResult);
                        if (lastJobResult != null)
                            log.Add("grams " + gramsBefore.ToString("0.##", Num.Dk) + " -> " + lastJobResult.Grams.ToString("0.##", Num.Dk));
                    }
                    if (done && specCopies > 0)
                    {
                        // batch & plates: the Copies link -> the real window -> Apply = sliced again with N copies
                        var first = lastJobResult;
                        check(CanCopiesJob && lblAuto.LinkControl != null, "Copies… offered after the slice");
                        int applied = 0;
                        using (var cf = NewCopiesForm())
                        {
                            cf.SampleType(specCopies);
                            cf.TestBigAnswer = true;
                            log.Add("copies window: " + cf.TestInfoText.Replace("\n", " / ") + " | Apply " + (cf.TestApplyOn ? "on" : "off"));
                            if (cf.TestPressApply(this)) applied = cf.Copies;
                        }
                        check(applied == specCopies, "Apply pressed: " + applied + " copies");
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        done = applied > 0 && await RecopyAsync(applied);
                        log.Add("sliced again in " + (sw.ElapsedMilliseconds / 1000) + " s");
                        var rb = lastJobResult;
                        // several files: the window's number is per file, jobCopies = all pieces (release-test-7: stale check)
                        int nFiles = Math.Max(1, JobFileCount);
                        int pieces = specCopies * nFiles;
                        check(done && rb != null && !ReferenceEquals(rb, first) && jobCopies == pieces,
                              "sliced again: " + jobCopies + " copies (" + specCopies + " × " + nFiles + " files), " + jobPlates + " plate(s), " + jobPerPlate + " per plate");
                        string perPlate = rb != null ? string.Join(",", rb.PlateList.Select(p => p.Copies)) : "";
                        log.Add("copies per plate: " + perPlate);
                        if (expectPlates != null) check(perPlate == expectPlates, "copies per plate " + perPlate + " = expected " + expectPlates);
                        check((lblAuto.Text ?? "").StartsWith("✓ " + History.PiecesText(pieces) + " (") || (lblAuto.Text ?? "").StartsWith("✓ " + pieces + " × "),
                              "result line: " + (lblAuto.Text ?? "").Replace("\n", " / "));
                        if (rb != null)
                            foreach (var p in rb.PlateList) log.Add("plate menu: " + PlateMenuLine(p, false));
                        // the line may end in the quantity discount's " · –5 %" (Settings' steps)
                        check(lblOthersInfo.Text == WithDiscountMark(History.EachText(last.OthersPrice, pieces), false, lblOthersInfo),
                              "price line: " + lblOthersInfo.Text);
                        log.Add("Copy text: " + QuoteText(null).Replace(Environment.NewLine, " | "));
                        if (first != null && rb != null)
                            log.Add("1 copy " + first.Seconds + " s / " + first.Grams.ToString("0.##", Num.Dk) + " g -> " + specCopies + " copies " +
                                    rb.Seconds + " s / " + rb.Grams.ToString("0.##", Num.Dk) + " g");
                        TestJobLines(log, rb);
                        Screenshots.Snap(this, dir, "test_slice_copies.png");
                    }
                    if (done && specRescale != null)
                    {
                        // the Scale window's Apply: the same files again at the new size, the ✓ line says it
                        var first = lastJobResult;
                        check(CanScaleJob, "Scale… offered after the slice");
                        string refused = null;   // the real Scale window's Apply off: it stops here, as a user couldn't press it
                        ScaleXyz applied = null;  // release-test-6: the real Apply button pressed; its Result is what gets sliced
                        using (var sf = NewScaleForm())
                        {
                            sf.SampleType(specRescale);
                            log.Add("scale window: size " + sf.TestSizeText + " | " + sf.TestInfoText.Replace("\n", " / ") + " | Apply " + (sf.TestApplyOn ? "on" : "off") +
                                    " | small note: " + (sf.TestSmallNote.Length > 0 ? sf.TestSmallNote : "(none)"));
                            if (!sf.TestApplyOn) refused = sf.TestInfoText.Replace("\n", " / ");
                            else
                            {
                                bool okApply = sf.TestPressApply(this);
                                applied = sf.Result;
                                check(okApply && applied != null && SameScale(applied, specRescale),
                                      "Apply pressed: Result " + (applied != null ? applied.Text : "none") + " (typed " + specRescale.Text + ")");
                            }
                        }
                        if (refused != null)
                        {
                            log.Add("refused by the Scale window: " + refused);
                            check(expectTooBig, "Scale window refused " + specRescale.Text + (expectTooBig ? " (expected too big)" : ""));
                            skipSave = true;
                        }
                        else
                        {
                        // lastJobResult kept (as on a real Apply: a too-small failure takes the 100 % size from it)
                        string tooSmallWant = TooSmallMessage(specRescale, true);
                        done = await RescaleAsync(applied ?? specRescale);   // the Scale window's Result, as Scale… does
                        if (expectTooSmall)
                        {
                            string got = lblAuto.Text ?? "";
                            log.Add("failed: " + got.Replace("\n", " / "));
                            check(!done && got.Contains(tooSmallWant), "too small at " + specRescale.Text + ": \"" + tooSmallWant + "\"");
                        }
                        else
                        {
                            check(done && lastJobResult != null && !ReferenceEquals(lastJobResult, first) && SameScale(jobScale, specRescale), "sliced again at " + specRescale.Text);
                            check((lblAuto.Text ?? "").Contains("(" + ScaleLabel(specRescale) + ")"), "result line names the scale: " + lblAuto.Text);
                        }
                        TestJobLines(log, lastJobResult);
                        if (first != null && lastJobResult != null && first.SizeZ > 0 && lastJobResult.SizeZ > 0)
                            log.Add("size " + BambuSlicer.SizeText(first.SizeX, first.SizeY, first.SizeZ) + " -> " +
                                    BambuSlicer.SizeText(lastJobResult.SizeX, lastJobResult.SizeY, lastJobResult.SizeZ) + " (expected " +
                                    BambuSlicer.SizeText(BambuSlicer.SizeAt(first.SizeX, specScale.X, specRescale.X), BambuSlicer.SizeAt(first.SizeY, specScale.Y, specRescale.Y),
                                                         BambuSlicer.SizeAt(first.SizeZ, specScale.Z, specRescale.Z)) + ")");
                        if (expectTooSmall) skipSave = true;   // the failed Apply keeps the old job: nothing new to save
                        if (expectTooBig) check(false, "expected the Scale window to refuse " + specRescale.Text);
                        }
                    }
                    if (skipSave)
                    {
                        EnsureHistory();
                        log.Add("not saved (history " + (history != null ? history.Count : 0) + " quotes)");
                        check(history == null || history.Count == historyBefore, "nothing saved");
                        Screenshots.Snap(this, dir, "test_slice.png");
                        try { File.WriteAllLines(Path.Combine(dir, "test_slice.txt"), log); } catch { }
                        Program.EndTestRun(ok ? 0 : 4);
                        return;
                    }
                    QuoteRecord rec = null;
                    bool hadPicture = modelImage != null;   // a G-code without a thumbnail shows the logo: nothing to save
                    string err = done ? SaveToHistory(0, null, out rec) : "not sliced";
                    if (err != null) rec = null;
                    check(err == null && rec != null, "saved to history" + (err != null ? ": " + err : ""));
                    // idea 29: the slice render is shown before the job ends, so the quote saved right after has it
                    if (hadPicture || !done) check(rec != null && History.HasPicture(rec.Id), "record saved with its plate picture");
                    else check(rec != null && !History.HasPicture(rec.Id), "no picture to save (the file has none)");
                    // a G-code job with a thumbnail: Open again shows the saved picture, and the picture-only reader that an old
                    // record without a saved picture uses (FillFromRecord -> BambuSlicer.ReadGcodePicture) finds it too
                    string gsrc = rec != null ? (rec.Source ?? "").Trim().Trim('"') : "";
                    if (rec != null && hadPicture && BambuSlicer.IsGcodeFile(gsrc) && File.Exists(gsrc))
                    {
                        byte[] thumb = BambuSlicer.ReadGcodePicture(gsrc);
                        check(thumb != null && thumb.Length > 0, "ReadGcodePicture finds the thumbnail (" + (thumb != null ? thumb.Length + " bytes" : "none") + ")");
                        ClearJob();
                        await LoadModelPicture(() => BambuSlicer.ReadGcodePicture(gsrc), true);
                        check(modelImage != null, "the reader's picture shows (as an old record opened again)");
                        ClearJob();
                        Reopen(rec);
                        check(modelImage != null, "Open again shows the saved picture");
                        Screenshots.Snap(this, dir, "test_slice_reopened.png");
                    }
                    TestRecordLines(log, rec);
                    if (specCopies > 0 && rec != null)
                        check(rec.Quantity == specCopies * Math.Max(1, JobFileCount) && rec.Plates == jobPlates && rec.PerPlate == jobPerPlate && rec.CopiesEstimated == 0,
                              "record: Quantity " + rec.Quantity + ", Plates " + rec.Plates + ", per plate " + rec.PerPlate + ", name \"" + RecordModelName(rec) + "\"");
                    var savedParts = rec != null ? History.PartsOf(rec) : null;
                    check(savedParts != null && savedParts.Count == parts.Count, "record keeps the " + parts.Count + " part(s)");
                }
                Screenshots.Snap(this, dir, "test_slice.png");
            }
            catch (Exception ex) { check(false, "exception: " + ex); }
            try { File.WriteAllLines(Path.Combine(dir, "test_slice.txt"), log); } catch { }
            Program.EndTestRun(ok ? 0 : 4);
        }

        /// A process id that is still running (gone / exited / not ours to read = false). Test hooks only.
        static bool RunningProcess(int id)
        {
            try { using (var p = Process.GetProcessById(id)) return !p.HasExited; }
            catch { return false; }
        }

        /// "--test-repeat": Repeat print of the newest history record (the saved parts slice again without the picker),
        /// then saves it (a new record, RepeatOf = the original). Writes <folder>\test_repeat.txt and test_repeat.png;
        /// exit 0 = sliced again and saved, 4 = not.
        internal async void TestRepeatFlow()
        {
            if (!AppSettings.TestMode || !Program.Hidden) return;
            string dir = AppSettings.Folder;
            var log = new List<string>();
            bool ok = true;
            Action<bool, string> check = (pass, what) => { log.Add((pass ? "PASS " : "FAIL ") + what); if (!pass) ok = false; };
            log.Add(SetupTest.StandInNote);
            try
            {
                EnsureHistory();
                var r = history != null && history.Count > 0 ? history[0] : null;
                check(r != null, "history has a record" + (historyProblem != null ? " (" + historyProblem + ")" : ""));
                if (r != null)
                {
                    log.Add("original: " + r.Id + " " + RecordName(r) + ", saved " + r.SavedAt);
                    TestRecordLines(log, r);
                    lastJobResult = null;
                    int before = history.Count;
                    await RepeatAsync(r);
                    // release-test-5: "test_repeat_expect.txt" = "too-big" in the settings folder: the repeat must be refused (bed),
                    // with Save off and nothing saved
                    string expect = "";
                    try { string ef = Path.Combine(dir, "test_repeat_expect.txt"); if (File.Exists(ef)) expect = File.ReadAllText(ef).Trim(); } catch { }
                    if (expect == "too-big")
                    {
                        check(lastSliceBedError && repeatLine == null, "refused (bed): " + lblAuto.Text);
                        check(!btnSave.Enabled && !btnCopy.Enabled, "Save to history and Copy are off");
                        check(txtGrams.Text == "", "the saved time and weight aren't used (grams box \"" + txtGrams.Text + "\")");
                        check(history.Count == before, "nothing saved (history " + history.Count + " quotes)");
                        Screenshots.Snap(this, dir, "test_repeat.png");
                        try { File.WriteAllLines(Path.Combine(dir, "test_repeat.txt"), log); } catch { }
                        Program.EndTestRun(ok ? 0 : 4);
                        return;
                    }
                    check(repeatWhy == null && lastJobResult != null, "sliced again" + (repeatWhy != null ? " (not: " + repeatWhy + ")" : ""));
                    TestJobLines(log, lastJobResult);
                    QuoteRecord rec;
                    string err = SaveToHistory(0, null, out rec);
                    if (err != null) rec = null;
                    check(err == null && rec != null, "saved to history" + (err != null ? ": " + err : ""));
                    check(rec != null && rec.RepeatOf == (r.Id ?? ""), "saved as a repeat of the original");
                    if (modelImage != null) check(rec != null && History.HasPicture(rec.Id), "record saved with its plate picture");   // idea 29
                    else check(rec != null && !History.HasPicture(rec.Id), "no picture to save (the file has none)");
                    TestRecordLines(log, rec);
                    var a = History.PartsOf(r);
                    var b = rec != null ? History.PartsOf(rec) : null;
                    check((a == null) == (b == null) && (a == null || a.Count == b.Count), "same parts as the original");
                    // issue #8: the repeat slices and saves at the original's size
                    log.Add("scale: original " + RecordScale(r).Text + " / repeated " +
                            (rec != null ? RecordScale(rec).Text : "-"));
                    check(rec != null && SameScale(RecordScale(rec), RecordScale(r)), "same scale (X / Y / Z) as the original");
                    // issue #8 step 3: the repeat slices with the original's tower switch (unknown = Settings)
                    bool want = History.PrimeTowerFor(r, settings);
                    check(rec != null && rec.PrimeTower == (want ? 1 : 0), "same tower switch as the original (" + (want ? "on" : "off") + ")");
                }
                Screenshots.Snap(this, dir, "test_repeat.png");
            }
            catch (Exception ex) { check(false, "exception: " + ex); }
            try { File.WriteAllLines(Path.Combine(dir, "test_repeat.txt"), log); } catch { }
            Program.EndTestRun(ok ? 0 : 4);
        }

        /// "--test-history <spec>" (test mode + --hidden only; plans\history-test-hook\plan.md): plays the spec's lines
        /// with the app's own code. Outside the window: seed (sample quotes + pictures into an empty history), reopen
        /// <id> (Open again), save, expect …; "open" … "close" runs in the History window opened by ShowHistory (the
        /// History… button's method), played by HistoryForm.TestPlay. Writes <folder>\test_history.txt; exit 0 = all
        /// passed, 4 = a FAIL (also after 120 s, so a stuck window never hangs a run).
        internal void TestHistoryFlow(string specFile)
        {
            if (!AppSettings.TestMode || !Program.Hidden) return;
            string dir = AppSettings.Folder, specDir = Path.GetDirectoryName(specFile);
            var log = new TestLog();
            Action write = () => { try { File.WriteAllLines(Path.Combine(dir, "test_history.txt"), log.Lines); } catch { } };
            var guard = new System.Windows.Forms.Timer { Interval = 120000 };
            guard.Tick += delegate { log.Check(false, "timeout: the run took over 120 s"); write(); Environment.Exit(4); };
            guard.Start();
            BackupForm.AskForm.TestAnswers = new Queue<DialogResult>();
            BackupForm.AskForm.TestLog = log;
            try
            {
                var lines = File.ReadAllLines(specFile).Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("//")).ToList();
                EnsureHistory();
                log.Add(SetupTest.StandInNote);
                log.Add("start: " + MainTestState());
                for (int i = 0; i < lines.Count; i++)
                {
                    string cmd, arg;
                    HistoryTest.Split(lines[i], out cmd, out arg);
                    if (cmd == "open")
                    {
                        var block = new List<string>();
                        while (++i < lines.Count)
                        {
                            block.Add(lines[i]);
                            if (HistoryTest.Command(lines[i]) == "close") break;
                        }
                        log.Add("> open");
                        bool played = false;
                        HistoryForm.TestDriver = async f =>
                        {
                            played = true;
                            try { await f.TestPlay(block, log, dir, specDir); }
                            catch (Exception ex) { log.Check(false, "exception in the History window: " + ex); f.Close(); }
                        };
                        ShowHistory();
                        HistoryForm.TestDriver = null;
                        log.Check(played, "History window opened");
                        log.Add("  after the window: " + MainTestState());
                        continue;
                    }
                    log.Add("> " + lines[i]);
                    switch (cmd)
                    {
                        case "seed":
                        {
                            if (history.Count > 0) { log.Check(false, "seed needs an empty history"); break; }
                            // "seed invoices": the same quotes with 6 invoices (one combined, Mixed; one past due), issue #7
                            history.AddRange(arg == "invoices" ? HistoryForm.SampleInvoiceQuotes() : HistoryForm.SampleQuotes());
                            string err = History.Save(history);
                            int saved = 0;
                            foreach (var kv in HistoryForm.SamplePictures()) if (History.SavePicture(kv.Key, kv.Value)) saved++;
                            log.Check(err == null, "seeded " + history.Count + " quotes" + (err != null ? ": " + err : ""));
                            log.Check(saved == HistoryForm.SamplePictures().Count, "seeded " + saved + " pictures");
                            break;
                        }
                        case "quote":
                        case "invoice":
                        {
                            // quote <id> / invoice <id>: the saved record's page as History's Quote… / Invoice… builds it
                            // (BuildRecordQuote, read-only: nothing is made or saved), drawn to test_history_<cmd>_<id>.png;
                            // its model name is logged and kept for "expect model"
                            var r = history.Find(x => x.Id == arg);
                            log.Check(r != null, "quote " + arg + " in the history");
                            if (r == null) break;
                            var d = BuildRecordQuote(r, r.For == History.ForFriends, r.Customer, cmd == "invoice", null);
                            string png = Path.Combine(dir, "test_history_" + cmd + "_" + arg + ".png");
                            using (var bmp = QuotePage.Render(d, false)) bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                            HistoryTest.PageModels[cmd + " " + arg] = d.Model ?? "";
                            bool towerRow = TestHasTowerRow(QuoteRows.AddTowerNote(QuoteRows.Build(d.Breakdown, d.MaterialNames, d.PurgeGrams, d.Hours, d.Minutes, d.PlateName, d.Quantity), d.TowerGrams), d.TowerGrams);
                            HistoryTest.PageTowers[cmd + " " + arg] = new KeyValuePair<double, bool>(d.TowerGrams, towerRow);
                            log.Add("  " + cmd + " page " + Path.GetFileName(png) + ": model \"" + d.Model + "\"" +
                                    (d.Invoice ? ", invoice " + d.InvoiceNo : ", quote " + d.Number) +
                                    ", tower grams " + d.TowerGrams.ToString("0.###", Num.Dk) + ", tower row " + towerRow);
                            break;
                        }
                        case "bill":
                        {
                            // bill <id> <id> […] [invoice]: the combined page of saved records (BuildGroupQuote, read-only: nothing
                            // is made or saved), drawn to test_history_bill_<n>.png; its tower grams + row kept for "expect tower"
                            var words = HistoryTest.Words(arg).ToList();
                            bool inv = words.Remove("invoice");
                            var jobs = words.Select(id => history.Find(x => x.Id == id)).ToList();
                            log.Check(jobs.Count >= 2 && jobs.All(x => x != null), "bill: " + words.Count + " saved records found");
                            if (jobs.Count < 2 || jobs.Any(x => x == null)) break;
                            var d = BuildGroupQuote(jobs, jobs[0].For == History.ForFriends, jobs[0].Customer, inv, null, new DateTime(2026, 11, 20, 12, 0, 0));
                            string png = Path.Combine(dir, "test_history_bill_" + (++testBillPages) + ".png");
                            using (var bmp = QuotePage.Render(d, false)) bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                            var rows = QuoteRows.AddTowerNote(QuoteRows.BuildGroup(d.Breakdown, d.MaterialNames, d.Hours, d.Minutes, d.PlateName, d.GroupSameRates, 6), d.TowerGrams);
                            bool towerRow = TestHasTowerRow(rows, d.TowerGrams);
                            HistoryTest.PageTowers["bill " + string.Join(" ", words)] = new KeyValuePair<double, bool>(d.TowerGrams, towerRow);
                            log.Add("  bill page " + Path.GetFileName(png) + ": " + jobs.Count + " jobs, tower grams " + d.TowerGrams.ToString("0.###", Num.Dk) +
                                    " (sum " + History.TowerGramsSum(jobs).ToString("0.###", Num.Dk) + ", box " + settings.QuoteShowTower + "), tower row " + towerRow);
                            break;
                        }
                        case "reopen":
                        {
                            var r = history.Find(x => x.Id == arg);
                            log.Check(r != null, "quote " + arg + " in the history");
                            if (r != null) { Reopen(r); log.Check(reopened == r, "opened again: " + RecordName(r)); }
                            break;
                        }
                        case "save":
                        {
                            QuoteRecord rec;
                            string err = SaveToHistory(0, null, out rec);
                            if (err != null) rec = null;
                            log.Check(rec != null, "saved to history" + (err != null ? ": " + err : ""));
                            if (rec != null)
                            {
                                bool linked = !string.IsNullOrEmpty(rec.RepeatOf) && !history.Any(x => x.Id == rec.RepeatOf);
                                log.Add("  saved " + rec.Id + ", RepeatOf \"" + rec.RepeatOf + "\", For " + rec.For + ", tower switch " + rec.PrimeTower +
                                        ", tower grams " + rec.TowerGrams.ToString("0.###", Num.Dk));
                                log.Check(!linked, "the new quote doesn't point at a deleted one");
                                // "save tower=<1|0|-1>": the saved tower switch (issue #8 step 3: an old record opened again keeps -1)
                                int wantTower;
                                if (arg.StartsWith("tower=") && int.TryParse(arg.Substring(6).Trim(), out wantTower))
                                    log.Check(rec.PrimeTower == wantTower, "saved tower switch " + rec.PrimeTower + " (expected " + wantTower + ")");
                            }
                            break;
                        }
                        case "expect":
                            HistoryTest.Expect(arg, log, history, null, () => reopened == null ? "none" : reopened.Id);
                            break;
                        default:
                            log.Check(false, "unknown step: " + lines[i]);
                            break;
                    }
                    if (cmd != "expect") log.Add("  " + MainTestState());
                }
            }
            catch (Exception ex) { log.Check(false, "exception: " + ex); }
            guard.Stop();
            write();
            Program.EndTestRun(log.Ok ? 0 : 4);
        }

        int testBillPages;   // test_history_bill_<n>.png

        /// The breakdown has the tower note row (QuoteRows.TowerNote of these grams) before its first rule.
        static bool TestHasTowerRow(List<QuoteRow> rows, double grams)
        {
            if (rows == null || !(grams > 0.005)) return false;
            string note = QuoteRows.TowerNote(grams);
            int at = rows.FindIndex(x => x.Kind == QuoteRowKind.Note && x.Label == note), rule = rows.FindIndex(x => x.Kind == QuoteRowKind.Rule);
            return at > 0 && (rule < 0 || at < rule) && rows[at].Amount == "";
        }

        string MainTestState()
        {
            return "main: history " + (history == null ? "(none)" : history.Count.ToString()) + ", open quote " +
                   (reopened == null ? "none" : reopened.Id) + ", repeat of " + (repeatOf == null ? "none" : repeatOf.Id) + ", " + HistoryTest.FileState();
        }

        /// The test hooks' lines about the job on screen: the slice (slots, time, plates, together) and what the window shows.
        void TestJobLines(List<string> log, SliceResult res)
        {
            if (res != null)
            {
                log.Add("seconds: " + res.Seconds.ToString("0", Num.Dk) + ", grams: " + res.Grams.ToString("0.##", Num.Dk) +
                        ", plates: " + res.Plates + ", purge g: " + res.PurgeGrams.ToString("0.##", Num.Dk));
                foreach (var f in res.Filaments)
                    log.Add("slot " + f.Slot + ": " + (f.Colour ?? "") + " " + (f.Type ?? "") + " \"" + (f.Profile ?? "") + "\" " +
                            f.Grams.ToString("0.##", Num.Dk) + " g, tower " + f.TowerGrams.ToString("0.###", Num.Dk) + " g");
                // issue #8 step 3: the switch it was sliced with, the tower's grams / seconds (-1 unknown) and the breakdown note
                log.Add("tower: switch " + (jobTower ? "on" : "off") + ", grams " + res.TowerGrams.ToString("0.###", Num.Dk) + ", seconds " +
                        res.TowerSeconds.ToString("0", Num.Dk) + ", link offered " + CanTowerJob + " (" + TowerLinkText + ")");
                log.Add("purge note: " + (lblPurge.Text ?? "").Replace("\n", " / "));
                log.Add("together: " + res.Together + ", together problem: " + (res.TogetherProblem ?? "(none)"));
                // issue #8: the job's scale and the measured size (0 = unknown)
                log.Add("scale: " + jobScale.Text + ", size: " +
                        (res.SizeX > 0 ? BambuSlicer.SizeText(res.SizeX, res.SizeY, res.SizeZ) : "(unknown)") + ", objects: " + res.SizeObjects);
                var plates = res.PlateList ?? new List<PlateInfo>();
                bool showFile = plates.Select(p => p.File ?? "").Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                foreach (var p in plates)
                    log.Add("plate line: " + BambuSlicer.PlateLine(p, showFile) + " [" + string.Join(", ", p.Objects ?? new List<string>()) + "]");
            }
            else log.Add("slice: (none)");
            log.Add("info line: " + lblAuto.Text);
            log.Add("muted line: " + lblAuto.MutedLine);
            log.Add("link: " + (lblAuto.LinkControl != null ? lblAuto.LinkControl.Text : ""));
            log.Add("time box: " + txtHours.Text + " h " + txtMinutes.Text + " m, grams box: " + txtGrams.Text);
            for (int i = 0; i < colourRows; i++)
                log.Add("row " + (i + 1) + ": " + (rowCombo[i] != null ? rowCombo[i].Text : "") + ", " + (rowGrams[i] != null ? rowGrams[i].Text : "") +
                        ", " + (rowPrice[i] != null ? rowPrice[i].Text : "") + " kr/kg");
            if (last != null) log.Add("prices: friends " + Num.Kr(last.FriendsPrice) + ", customers " + Num.Kr(last.OthersPrice));
        }

        /// The test hooks' lines about a saved record: its parts (file, filament, colour, slot) and colours.
        static void TestRecordLines(List<string> log, QuoteRecord r)
        {
            if (r == null) { log.Add("record: (none)"); return; }
            log.Add("record " + r.Id + ": " + r.Hours + " h " + r.Minutes + " m, " + r.Grams.ToString("0.##", Num.Dk) + " g, repeat of: " +
                    (string.IsNullOrEmpty(r.RepeatOf) ? "-" : r.RepeatOf) + ", scale: " + RecordScale(r).Text);
            // issue #8 step 3: the saved tower switch (1 / 0 / -1 unknown), grams and seconds (-1 unknown)
            log.Add("record tower: switch " + r.PrimeTower + ", grams " + r.TowerGrams.ToString("0.###", Num.Dk) + ", seconds " +
                    r.TowerSeconds.ToString("0", Num.Dk) + ", shown " + History.ShowsTower(r));
            var parts = History.PartsOf(r);
            if (parts == null) log.Add("record parts: (none)");
            else
            {
                log.Add("record parts together: " + History.PartsTogether(r));
                foreach (var p in parts) log.Add("record part: " + p.File + " | " + p.Filament + " | " + p.Colour + " | slot " + p.Slot);
            }
            if (r.Filaments != null)
                foreach (var f in r.Filaments)
                    log.Add("record colour: " + (f.Colour ?? "") + " " + (f.Type ?? "") + " \"" + (f.Name ?? "") + "\" " + f.Grams.ToString("0.##", Num.Dk) + " g" +
                            ", tower " + f.TowerGrams.ToString("0.###", Num.Dk) + " g");
        }

        // a made-up model picture for TestPictureFlow (transparent, orange shape)
        static Bitmap TestPicture()
        {
            var b = new Bitmap(320, 240, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(Color.FromArgb(240, 120, 80))) g.FillEllipse(br, 40, 60, 240, 140);
                using (var br = new SolidBrush(Color.FromArgb(200, 90, 60))) g.FillRectangle(br, 140, 20, 50, 80);
            }
            return b;
        }

        void OpenHistory()
        {
            if (cts != null) return;
            // an unexpected error building the window: the dark error window, the main window carries on
            try { ShowHistory(); }
            catch (Exception ex) { ErrorReport.Show(ex); }
        }

        void ShowHistory()
        {
            EnsureHistory();
            string problem = historyProblem;
            historyProblem = null;   // shown once
            using (var dlg = new HistoryForm(history, problem, offline))
            {
                dlg.QuoteRequested = (owner, recs, invoice) => ShowRecordsQuote(owner, recs, invoice);
                dlg.Settings = settings;   // printer names (Printer column / filter with 2+ printers)
                // Backup…: the Restore tab first while the history is empty (a new PC)
                dlg.BackupRequested = owner =>
                {
                    bool restored = OpenBackup(owner, history.Count == 0 ? BackupForm.TabRestore : BackupForm.TabBackup);
                    if (restored) dlg.Settings = settings;
                    return restored;
                };
                // Undo of Clear all restores its safety copy: taken over like a Backup restore
                dlg.Restored = res => { AdoptRestore(res); dlg.Settings = settings; };
                var result = dlg.ShowDialog(this);
                ForgetDeletedRecords();   // idea 22: deleted (or cleared) quotes may have been open here
                if (result == DialogResult.OK && dlg.Selected != null)
                {
                    if (dlg.RepeatSelected) Repeat(dlg.Selected);
                    else Reopen(dlg.Selected);
                }
                else Recalculate();   // Failed / For / Delete in the list change the payback debt
            }
        }

        /// After the History window (idea 22: delete several, Clear all, undo): the opened / repeated quote points at the
        /// record now in the history with the same Id (an undone Clear all brings back copies), or at nothing when it
        /// was deleted, so saving never links to a deleted quote (RepeatOf) or copies its For.
        void ForgetDeletedRecords()
        {
            reopened = InHistory(reopened);
            repeatOf = InHistory(repeatOf);
            repeatLine = InHistory(repeatLine);
        }

        QuoteRecord InHistory(QuoteRecord r)
        {
            if (r == null || history == null) return null;
            if (history.Contains(r)) return r;
            if (string.IsNullOrEmpty(r.Id)) return null;
            foreach (var h in history) if (h != null && h.Id == r.Id) return h;
            return null;
        }

        /// The Backup window (idea 27) from History or Settings > App, on tab BackupForm.TabBackup / TabRestore.
        /// true = a restore saved a new history and settings (already taken over here by AdoptRestore).
        bool OpenBackup(IWin32Window owner, int tab)
        {
            bool restored = false;
            try
            {
                EnsureHistory();
                using (var dlg = new BackupForm(history, settings, tab, offline))
                {
                    dlg.Restored = res => { AdoptRestore(res); restored = true; };
                    dlg.ShowDialog(owner);
                    if (dlg.RestartRequested && !offline)
                    {
                        // restored settings in another language: they are saved; Restart closes the windows and starts anew
                        if (cts != null) cts.Cancel();
                        BeginInvoke((MethodInvoker)Program.Restart);
                    }
                }
            }
            catch (Exception ex) { ErrorReport.Show(ex); }   // the History / Settings window that asked stays open
            return restored;
        }

        /// After a restore (History.Restore saved both files): the same history list gets the new records (History
        /// and Settings windows hold this list), the settings become the restored ones (also when only the history was
        /// restored: they carry the merged invoice codes and printer ids), then the same follow-ups as Settings' OK.
        void AdoptRestore(RestoreResult res)
        {
            history.Clear();
            history.AddRange(res.Quotes);
            historyProblem = null;
            var old = settings;
            if (res.Settings != null) settings = res.Settings;
            // the restore is saved and taken over by now: an error in the follow-ups below must not look like a failed restore
            try
            {
                History.ReservePrinterIds(history, settings);
                SyncUpdateTimer();
                if (SlicingChanged(old, settings) || old.FilamentPreset != settings.FilamentPreset || old.SlicerPath != settings.SlicerPath)
                    SetStudioFile(null);
                RefreshSettingsLabels();
                foreach (var cb in rowCombo) cb.Tag = null;
                if (!jobLoaded && cts == null) SetColours(null);
                Recalculate();
                if (cts == null) ShowAutoIdle();
            }
            catch (Exception ex) { ErrorReport.Show(ex); }
        }

        /// "Open again": puts a saved quote's inputs back (time, grams, colour rows with their kr/kg, filament and
        /// electricity price, link/file, model name; no slicing) and recalculates with today's settings.
        /// The model info box shows where it came from and the saved price next to today's.
        internal void Reopen(QuoteRecord r)
        {
            if (r == null || cts != null) return;
            FillFromRecord(r, false);
            reopened = r;   // after Recalculate/SetAuto, which clear it
            UpdateReopenedLine();
        }

        /// "Repeat print" (History): the same print priced again with today's prices. Fills the saved time, grams and
        /// colours at once (today's kr/kg per material and today's electricity price), then slices the saved link or
        /// file again (the file picker opens again for multi-file Printables models). Slicing not possible (link or file
        /// gone, no Bambu Studio, error, Cancel) = the saved time and weight stay, with a note. Saving it makes a new
        /// record with RepeatOf = the original's Id and the same For.
        internal async void Repeat(QuoteRecord r)
        {
            await RepeatAsync(r);
        }

        /// Repeat's work, awaitable (TestRepeatFlow waits for it).
        async Task RepeatAsync(QuoteRecord r)
        {
            if (r == null || cts != null) return;
            FillFromRecord(r, true);
            repeatOf = r;
            string src = (r.Source ?? "").Trim().Trim('"');
            string id = src.Length > 0 ? Printables.ParseId(src) : null;
            bool local = id == null && src.Length > 0 && File.Exists(src) && (BambuSlicer.IsModelFile(src) || BambuSlicer.IsGcodeFile(src));
            if (offline) { ShowRepeatLine(r, null); return; }   // picture mode never slices
            var parts = History.PartsOf(r);   // idea 21: several files with a filament / colour each: sliced the same way
            if (parts != null)
            {
                List<string> paths = null;
                if (id == null)
                {
                    paths = parts.Select(p => (p.File ?? "").Trim()).ToList();
                    if (paths.Any(p => p.Length == 0 || !File.Exists(p)))
                    {
                        ShowRepeatLine(r, L.S("a saved file isn't there any more", "en gemt fil findes ikke længere"));
                        return;
                    }
                }
                towerForNextSlice = History.PrimeTowerFor(r, settings);   // issue #8 step 3: the saved tower switch
                copiesForNextSlice = RepeatCopies(r, parts.Count);        // batch & plates: the saved copies
                shareForNextSlice = History.ShareLastPlateFor(r, settings);   // batch-2: the saved sharing choice
                lastSliceBedError = false;
                bool done = await AnalyzeAsync(id, paths, res => KeepSavedMaterials(res, r), parts, History.PartsTogether(r),
                                               RecordScale(r), null);   // issue #8: at the saved size
                copiesForNextSlice = null;
                shareForNextSlice = null;
                if (done) { repeatOf = r; ShowRepeatLine(r, null); return; }
                if (lastSliceBedError) { RefuseRepeat(r); return; }
                string failed = (lblAuto.Text ?? "").Trim().TrimEnd('.');
                FillFromRecord(r, true);
                repeatOf = r;
                ShowRepeatLine(r, failed.Length > 0 ? failed : L.S("slicing failed", "slicing mislykkedes"));
                return;
            }
            if (id == null && !local)
            {
                ShowRepeatLine(r, src.Length == 0 ? L.S("no link or file was saved", "der blev ikke gemt et link eller en fil")
                                                  : L.S("the link or file isn't there any more", "linket eller filen findes ikke længere"));
                return;
            }
            towerForNextSlice = History.PrimeTowerFor(r, settings);   // issue #8 step 3: the saved tower switch
            copiesForNextSlice = RepeatCopies(r, 1);                  // batch & plates: the saved copies
            lastSliceBedError = false;
            bool ok = await AnalyzeAsync(id, local ? new List<string> { src } : null, res => KeepSavedMaterials(res, r), null, false,
                                         RecordScale(r), null);   // issue #8: at the saved size
            copiesForNextSlice = null;
            if (ok) { repeatOf = r; ShowRepeatLine(r, null); return; }
            if (lastSliceBedError) { RefuseRepeat(r); return; }
            string why = (lblAuto.Text ?? "").Trim().TrimEnd('.');
            FillFromRecord(r, true);   // back to the saved time, grams and colours (Analyze reset the job)
            repeatOf = r;
            ShowRepeatLine(r, why.Length > 0 ? why : L.S("slicing failed", "slicing mislykkedes"));
        }

        /// Batch & plates: the copies per file for a Repeat print / Open again of r (null = 1 each). One file: its Quantity.
        /// Several files: each file's saved count (History.PartCopies); records saved before that kept only the total, so an
        /// even split ("6 over 2 files" = 3 each), else one of each.
        static List<int> RepeatCopies(QuoteRecord r, int files)
        {
            var saved = History.PartCopies(r);
            if (files > 1 && saved != null && saved.Count == files && saved.Any(c => c > 1)) return saved;
            int q = History.QuantityOf(r);
            if (q <= 1 || files < 1) return null;
            if (files == 1) return new List<int> { q };
            return q % files == 0 ? Enumerable.Repeat(q / files, files).ToList() : null;
        }

        /// The last slice failed because the model doesn't fit the bed (SliceException.IsBedError); RepeatAsync reads it.
        bool lastSliceBedError;

        /// Repeat print refused by the bed (release-test-5, user: "Refuse, no save"): the saved time and weight are NOT used
        /// (they'd price a print the printer can't make); the boxes are cleared so Save to history / Copy stay off, and the
        /// error says why (BedErrorText already in lblAuto, with "Repeat print of …:" in front).
        void RefuseRepeat(QuoteRecord r)
        {
            string why = (lblAuto.Text ?? "").Trim();
            repeatOf = null;
            repeatLine = null;
            repeatWhy = null;
            ClearJobNumbers();
            SetAutoError(L.F("↻ Repeat print of {0}: doesn't fit this printer. {1}", "↻ Print igen af {0}: passer ikke til denne printer. {1}",
                             RecordName(r), why));
        }

        /// A repeat's new slice keeps the saved material per colour (name and type) when the colour count is the same,
        /// so the kr/kg is today's price of the same material.
        static void KeepSavedMaterials(SliceResult res, QuoteRecord r)
        {
            if (res == null || r.Filaments == null || res.Filaments.Count != r.Filaments.Count) return;
            for (int k = 0; k < res.Filaments.Count; k++)
            {
                var saved = r.Filaments[k];
                if (!string.IsNullOrEmpty(saved.Name)) res.Filaments[k].Profile = saved.Name;
                if (!string.IsNullOrEmpty(saved.Type)) res.Filaments[k].Type = saved.Type;
            }
        }

        /// Shows the repeat lines in the model info box (why = not sliced again: warning stripe + the reason).
        void ShowRepeatLine(QuoteRecord r, string why)
        {
            autoIsError = false;
            repeatLine = r;
            repeatWhy = why;
            UpdateReopenedLine();
            SetAutoColor();
        }

        /// The boxes from a saved record. repeat = false ("Open again"): its saved kr/kg and electricity price (today's
        /// box value is kept to come back to); repeat = true: today's kr/kg per material and today's electricity price.
        void FillFromRecord(QuoteRecord r, bool repeat)
        {
            reopened = null;   // no half-updated lines of an earlier one while the boxes fill
            repeatLine = null;
            repeatOf = null;
            savedId = null;    // saving it again is a new sale: it pays back again
            ForgetPlates();    // a saved record has no plates list (Repeat print gets a new one when it slices again)
            SetUrlHint(false);
            UseRecordPrinter(r);   // the quote's printer (if it still exists): its wear and power, and Repeat print slices for it
            ShowLinkSource(r.Source);
            SetStudioFile(null);   // no slice from this session (also when the link was the same, so TextChanged didn't fire)
            jobModel = string.IsNullOrEmpty(r.Model) ? null : r.Model;   // after the link (its TextChanged clears it)
            jobScale = RecordScale(r); jobTower = History.PrimeTowerFor(r, settings); jobTowerKnown = r.PrimeTower == 0 || r.PrimeTower == 1;   // issue #8: the saved size (also after the link: TextChanged resets it)
            lastJob = null;                  // nothing sliced this session: Scale… comes after a slice / Repeat print
            // batch & plates: the saved copies and plates (the saved time / grams are the totals; the price stays the same)
            ResetCopies();
            jobCopies = History.QuantityOf(r);
            jobPlates = History.CleanPlates(r.Plates);
            jobPerPlate = History.CleanPlates(r.PerPlate);
            jobParts = History.PartsOf(r);   // a colour per file (idea 21): saving it again / a failed repeat keeps them
            jobFileCopies = RepeatCopies(r, jobParts != null ? jobParts.Count : 1);   // per file (several files: History.PartCopies)
            jobTogether = History.PartsTogether(r);
            jobShare = History.ShareLastPlateFor(r, settings);   // batch-2
            ResetModelPicture();
            SetJobLoaded(true);   // no picture (Printables link, missing file) = logo without "No model yet"
            string src = (r.Source ?? "").Trim().Trim('"');
            var stored = StoredPicture(r);   // the picture saved with the record (issue #4) comes first
            Task picture = null;   // not awaited: the picture shows when ready
            if (stored != null) SetModelPicture(stored);
            else if (!offline && src.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) && File.Exists(src))
                picture = LoadModelPicture(() => BambuSlicer.FromProject(src), true);   // a local project still has its own picture
            else if (!offline && BambuSlicer.IsGcodeFile(src) && File.Exists(src))
                picture = LoadModelPicture(() => BambuSlicer.ReadGcodePicture(src), true);   // picture-only read (optimizer 4)   // a G-code's thumbnail (errors = none)

            SliceResult slice = null;
            var one = History.SingleLine(r);   // one colour saved with its material (null: old record or 2+ colours)
            if (one != null)
            {
                // Grams > 0 so SetColours keeps it; the row's grams follow "Filament used" anyway
                slice = new SliceResult { PurgeGrams = -1, ColourChangeHours = -1, TowerGrams = r.TowerGrams, TowerSeconds = r.TowerSeconds };
                slice.Filaments.Add(new FilamentUse { Slot = 1, Colour = one.Colour ?? "", Type = one.Type ?? "", Profile = one.Name ?? "",
                                                      Grams = Math.Max(one.Grams, 0.01), ModelGrams = -1, TowerGrams = one.TowerGrams });
            }
            else if (r.Filaments != null && r.Filaments.Count >= 2)
            {
                // issue #8 step 3: the saved tower (-1 = unknown: no line)
                slice = new SliceResult { PurgeGrams = r.PurgeGrams, ColourChangeHours = -1, TowerGrams = r.TowerGrams, TowerSeconds = r.TowerSeconds };
                for (int k = 0; k < r.Filaments.Count; k++)
                {
                    var f = r.Filaments[k];
                    slice.Filaments.Add(new FilamentUse { Slot = k + 1, Colour = f.Colour ?? "", Type = f.Type ?? "", Profile = f.Name ?? "",
                                                          Grams = f.Grams, ModelGrams = -1, TowerGrams = f.TowerGrams });
                }
            }
            SetColours(slice);   // old record (no saved material): neutral swatch + Settings' filament profile
            if (!repeat)   // a repeat keeps SetColours' kr/kg = today's remembered price of each material
            {
                fillingColours = true;   // the saved kr/kg, not the material's remembered one (and not remembered again)
                if (filaments != null)
                    for (int i = 0; i < colourRows; i++)   // "+N more" row: one shared box, the first of its colours' price
                        rowPrice[i].Text = Num.Fmt(r.Filaments[filaments[rowItems[i][0]].Slot - 1].PricePerKg);
                else rowPrice[0].Text = Num.Fmt(one != null ? one.PricePerKg : r.FilamentDkkPerKg);
                fillingColours = false;
            }
            txtGrams.Text = Num.Fmt(r.Grams);
            txtHours.Text = r.Hours.ToString();
            txtMinutes.Text = r.Minutes.ToString();
            string savedElec = r.ElectricityDkkPerKwh.ToString("0.00", Num.Dk);
            if (!repeat)
            {
                if (!elecSaved) elecToday = txtElecPrice.Text;   // today's value, for a later Repeat print
                elecSaved = true;
                txtElecPrice.Text = r.ElectricityDkkPerKwh.ToString("0.00##", Num.Dk);
                SetPowerInfo(L.F("Electricity price {0} kr/kWh from the saved quote. Click Update for today's.",
                                 "Elpris {0} kr/kWh fra det gemte tilbud. Klik Opdatér for dagens.", savedElec), false);
            }
            else
            {
                if (elecSaved && elecToday != null) txtElecPrice.Text = elecToday;   // an earlier "Open again" put an old price in
                elecSaved = false;
                SetPowerInfo(L.F("Today's electricity price; the saved quote used {0} kr/kWh. Click Update for the live price.",
                                 "Dagens elpris; det gemte tilbud brugte {0} kr/kWh. Klik Opdatér for live-prisen.", savedElec), false);
                if (!offline && settings.AutoPowerPrice != 0) FetchPowerPrice();
            }
            autoIsError = false;
            SetAutoColor();
            Recalculate();
        }

        /// The record's name for the info box lines: "model name", else the file name, else "a quote".
        static string RecordName(QuoteRecord r)
        {
            return !string.IsNullOrEmpty(r.Model) ? "\"" + r.Model + "\""
                 : (r.Source ?? "").Length > 0 ? Path.GetFileName((r.Source ?? "").Trim().Trim('"')) : L.S("a quote", "et tilbud");
        }

        /// "↻ Opened from history: …, saved …" + muted "Saved price a / b kr. — now c / d kr. (today's settings)".
        /// Follows every recalculation (typing, new settings) while the quote is open.
        void UpdateReopenedLine()
        {
            if (repeatLine != null) { UpdateRepeatLine(); return; }
            if (reopened == null) return;
            var r = reopened;
            DateTime t;
            string when = History.TryTime(r.SavedAt, out t) ? t.ToString("dd-MM-yyyy HH:mm", Num.Dk) : r.SavedAt;
            string name = RecordName(r);
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

        /// "↻ Repeat print of "X" (first quoted dd-MM-yyyy) with today's prices." (or "…: not sliced again (why), so the
        /// saved time and weight are used.") + muted "Was a kr. → now b kr. (+c kr.)" for the record's own price (For).
        void UpdateRepeatLine()
        {
            var r = repeatLine;
            DateTime t;
            string when = History.TryTime(r.SavedAt, out t) ? t.ToString("dd-MM-yyyy", Num.Dk) : r.SavedAt;
            // sliced again into 2+ plates: the plates link comes back under the lines, so the main line is kept to one line
            // (the box stays at 3 lines: window <= ~760 px with 4 colour rows)
            bool plates = repeatWhy == null && shownPlates != null && shownPlates.Count >= 2;
            bool scaleLink = repeatWhy == null && (CanScaleJob || CanTowerJob);   // issue #8: "Scale…" (and the tower link) under the lines too
            string main = plates || scaleLink ? OneLineRepeatText(r, when)
                : repeatWhy == null
                ? L.F("↻ Repeat print of {0} (first quoted {1}) with today's prices.", "↻ Print igen af {0} (første tilbud {1}) med dagens priser.",
                      RecordName(r), when)
                : L.F("↻ Repeat print of {0}: not sliced again ({1}), so the saved time and weight are used.",
                      "↻ Print igen af {0}: ikke slicet igen ({1}), så den gemte tid og vægt bruges.", RecordName(r), repeatWhy);
            bool friends = r.For == History.ForFriends;
            double was = History.PriceFor(r);
            string muted;
            if (last == null) muted = L.F("Was {0}", "Før {0}", Num.Kr(was));
            else
            {
                double now = friends ? last.FriendsPrice : last.OthersPrice;
                double diff = Math.Round(now - was, 2);
                string change = diff > 0 ? "+" + Num.Kr(diff) : diff < 0 ? "−" + Num.Kr(-diff) : L.S("same price", "samme pris");
                muted = L.F("Was {0} → now {1} ({2}), {3} price", "Før {0} → nu {1} ({2}), {3}", Num.Kr(was), Num.Kr(now), change,
                            friends ? L.S("friends & family", "venne- og familiepris") : L.S("customer", "kundepris"));
            }
            if (lblAuto.Text != main || lblAuto.MutedLine != muted)
            {
                lblAuto.SetText(main, muted);   // removes the link line: put it back
                if (plates) ShowRepeatPlatesLink();
                else if (scaleLink) SetResultLinks(null, null, null);
                SetAutoColor();
            }
        }

        /// "↻ Repeat print of "X", first quoted dd-MM-yyyy." on one line of the box: the name is cut with "…" until it
        /// fits the box's text width (measured with the label's own font, real px).
        string OneLineRepeatText(QuoteRecord r, string when)
        {
            string name = RecordName(r);
            bool quoted = name.Length >= 2 && name[0] == '"' && name[name.Length - 1] == '"';
            string raw = quoted ? name.Substring(1, name.Length - 2) : name;
            Label lbl = lblAuto.Controls.Count > 0 ? lblAuto.Controls[0] as Label : null;
            int width = lbl != null ? lbl.MaximumSize.Width : 0;
            string text = "";
            for (int max = raw.Length; max >= 6; max--)
            {
                string n = BambuSlicer.CutName(raw, max);
                if (quoted) n = "\"" + n + "\"";
                text = L.F("↻ Repeat print of {0}, first quoted {1}.", "↻ Print igen af {0}, første tilbud {1}.", n, when);
                if (lbl == null || width <= 0) break;
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
                if (TextRenderer.MeasureText(text, lbl.Font, new Size(int.MaxValue, int.MaxValue), flags).Width <= width - 2) break;
            }
            return text;
        }

        /// Repeat print of a 2+ plates job: the plates link again; when "together" fell back its text says so ("Sliced one
        /// by one: …") and its dark tooltip gives the reason (+ "Studio opens file 1 of N").
        void ShowRepeatPlatesLink()
        {
            var list = shownPlates;
            if (list == null || list.Count < 2) return;
            bool fellBack = !string.IsNullOrEmpty(platesProblem);
            SetResultLinks(PlatesLinkText(list.Count, fellBack), () => ShowPlatesMenu(list), L.F("{0} plates – show them", "{0} plader – vis dem", list.Count));
            if (lblAuto.LinkControl == null || printerTip == null) return;
            var tip = new List<string>();
            if (fellBack) tip.Add(WrapTip(platesProblem, 45));
            if (platesStudioOf > 1)
                tip.Add(WrapTip(L.F("\"Open in Studio\" opens only the first file (1 of {0}).",
                                    "\"Åbn i Studio\" åbner kun den første fil (1 af {0}).", platesStudioOf), 45));
            if (tip.Count > 0) printerTip.SetToolTip(lblAuto.LinkControl, string.Join("\n\n", tip));
        }

        void ClearJob()
        {
            reopened = null;
            repeatLine = null;
            repeatOf = null;
            savedId = null;
            ForgetPlates();
            if (lblAuto.MutedLine.Length > 0) ShowAutoIdle();   // the "Opened from history" lines go with the job
            txtUrl.Text = "";
            jobParts = null;
            jobScale = ScaleXyz.None; jobTower = SettingsTower; jobTowerKnown = true;   // issue #8
            lastJob = null;
            ResetCopies();   // batch & plates: one copy again
            SetStudioFile(null);   // back to "Get time & weight" (also if the box was already empty)
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
