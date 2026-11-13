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
            txtUrl.TextChanged += delegate { SetUrlHint(false); jobModel = null; jobParts = null; fromExample = false; SetStudioFile(null); repeatOf = null; };
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
            for (int i = 0; i < 6; i++) bt.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // rows 2-5 fixed on Load (SetBreakRows)
            // empty last row takes the box's spare height (~4 px at the bottom), so the last visible row is 28 px like the
            // others (a table stretches its last row: Base cost / payback sat 4 px low)
            bt.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bt.RowCount = 7;
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
            btnCopy = new Button { Text = L.S("Show quote…", "Vis tilbud…"), Size = new Size(128, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 0, 3), AutoEllipsis = true };
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
                int pair = custBox.Width - btnSave.Margin.Right;
                if (btnSave.Width <= pair / 2 && btnCopy.Width <= pair - pair / 2) btnSave.Width = pair / 2;
                else btnSave.Width = (int)Math.Round((double)pair * btnSave.Width / (btnSave.Width + btnCopy.Width));
                btnCopy.Width = pair - btnSave.Width;
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

            FormClosing += delegate { if (cts != null) cts.Cancel(); SaveLastPrices(); };
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
            // leftover record pictures (issue #4): history.json read in the background, cleaned once it read fine
            if (!offline) Shown += delegate { StartupPictureClean(); };
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
            // shows the "Open in Studio" button (with Studio's icon if it's installed); never clicked in picture mode
            SetStudioFile(Path.Combine(BambuSlicer.StudioRoot, "sample", "Multicolor_Benchy.3mf"));
            jobModel = "Four-Color Multicolor Test Benchy";   // the quote picture's model name
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
            if (offline || cts != null) return false;
            bool ok = false;
            SetStudioFile(null);   // a new job: back to "Get time & weight" until it has sliced
            string exe = BambuSlicer.FindExe(settings.SlicerPath);
            bool presliced = localFiles != null && localFiles.Count == 1 && BambuSlicer.IsSlicedProject(localFiles[0]);
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
            ResetModelPicture();
            if (localFiles != null && localFiles.Count == 1 && localFiles[0].EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            {
                string project = localFiles[0];
                LoadModelPicture(() => BambuSlicer.FromProject(project), true);
            }
            try
            {
                List<RemoteFile> chosen = null;
                ColourPlan plan = null;   // a colour per file (idea 21); null = one file, or files that keep their own colours
                jobParts = null;
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
                    if (savedParts != null)
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
                var result = await Task.Run(() => RunJob(exe, printablesId, chosen, files, printer, nozzle, process, filament, bed, colourPlan, progress, token));
                if (plan != null)
                {
                    jobParts = plan.Parts;   // saved with the quote (History.SetParts), so Repeat print slices it the same way
                    jobTogether = plan.Together;
                }
                if (result.Picture != null)
                {
                    byte[] render = result.Picture;
                    // the plate render always wins: replaces the Printables photo / 3MF thumbnail; picRequest++ drops a photo
                    // still downloading (it would land after the render). The shown picture stays if the render can't be read.
                    picRequest++;
                    LoadModelPicture(() => render, true);
                }

                int parts = chosen != null ? chosen.Count : localFiles.Count;
                ShowJobResult(result, modelName, localFiles, parts, process, filament, beforeFill);
                ok = true;
            }
            catch (OperationCanceledException) { SetAuto(L.S("Cancelled.", "Annulleret."), false); }
            // a failed job clears the last one's numbers (Repeat print keeps them: it shows the saved quote instead)
            catch (SliceException ex) { if (beforeFill == null) ClearJobNumbers(); SetAutoError(ex.Message); }
            catch (Exception ex)
            {
                if (beforeFill == null) ClearJobNumbers();
                SetAuto(L.S("Something went wrong: ", "Noget gik galt: ") + ex.Message, true);
            }
            finally
            {
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
                                                         : L.F(", {0} files", ", {0} filer", parts)) : "") +
                           (result.Plates > parts && !platesLink ? L.F(", {0} plates", ", {0} plader", result.Plates) : "");
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
            ShowPlatesLink(result.PlateList);
            platesProblem = fellBack ? result.TogetherProblem : null;   // a Repeat print shows them in the link's tooltip
            platesStudioOf = studioFirst ? parts : 0;
            SetAutoColor();
            if (noTime) txtHours.Focus();
            else if (noGrams && !txtGrams.ReadOnly) txtGrams.Focus();
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
            if (plates == null || plates.Count < 2) return;
            var list = plates.Where(p => p != null).ToList();
            shownPlates = list;
            lblAuto.SetLink(PlatesLinkText(list.Count, false), () => ShowPlatesMenu(list));
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

        /// tip (may be null): shows a plate's object names beside its line while the mouse is on it.
        ContextMenuStrip PlatesMenu(List<PlateInfo> plates, ToolTip tip)
        {
            bool showFile = plates.Select(p => p.File ?? "").Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            var menu = new ContextMenuStrip { ShowImageMargin = false, Renderer = new QuoteMenuRenderer(), ShowItemToolTips = false,
                                              BackColor = Theme.Input, ForeColor = Theme.Text };
            menu.Items.Add(new ToolStripMenuItem(L.S("Plates in this job:", "Plader i dette job:")) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            foreach (var p in plates)
            {
                var mi = new ToolStripMenuItem(BambuSlicer.PlateLine(p, showFile).Replace("&", "&&")) { ForeColor = Theme.Text };
                var objects = p.Objects ?? new List<string>();
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
        string platesProblem;          // the last slice's TogetherProblem when "together" fell back (null = didn't)
        int platesStudioOf;            // the last slice's file count when Studio opens only the first file (0 = not)

        /// Forgets the last slice's plates (a reopened / repeated / cleared job must not show an earlier job's plates).
        void ForgetPlates()
        {
            shownPlates = null;
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
                    var objs = shownPlates[k].Objects ?? new List<string>();
                    if (objs.Count == 0) return;
                    string text = string.Join("\n", objs);
                    var font = SystemFonts.StatusFont;
                    var ts = TextRenderer.MeasureText(text, font);
                    var box = new Rectangle(x + item.Bounds.Right + 4, y + item.Bounds.Top, ts.Width + 12, ts.Height + 8);
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
                                  IProgress<string> progress, CancellationToken ct)
        {
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
                    var r = BambuSlicer.ReadSliceInfo(models[0].Value, filament);
                    r.FromSlicedFile = true;
                    return r;
                }

                ct.ThrowIfCancellationRequested();
                string label = Wear.FindModel(printer).Label;   // "Bambu Lab A1 mini"
                progress.Report(L.F("Preparing {0} profiles…", "Klargør {0}-profiler…", label));
                BambuSlicer.PrepareProfiles(exe, printer, nozzle, process, filament, bed, work);

                SliceResult total;
                if (plan != null)
                {
                    total = BambuSlicer.SliceColours(exe, models, plan.Slots, plan.Colours, plan.Together, work, printer, nozzle,
                                                     BambuSlicer.ShortName(filament), progress, ct);
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
                        var r = BambuSlicer.SliceFile(exe, models[i].Value, work, printer, nozzle, filament, ct);
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
                problem = L.S("Please enter valid, non-negative numbers.", "Indtast gyldige tal, ikke negative.");
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
            lblFriendsInfo.Text = L.F("+{0} % markup", "+{0} % avance", Num.Fmt(settings.FriendsMarkupPct)) + wear;
            // with a minimum price the line drops "markup" so "min. 50 kr." fits: "+75 %, 1,71 kr/h wear, min. 50 kr."
            string o;
            if (settings.MinimumPrice > 0)
                o = L.F("+{0} %", "+{0} %", Num.Fmt(settings.OthersMarkupPct)) + wear + L.F(", min. {0} kr.", ", min. {0} kr.", Num.Fmt(settings.MinimumPrice));
            else o = L.F("+{0} % markup", "+{0} % avance", Num.Fmt(settings.OthersMarkupPct)) + wear;
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
                        // other slicing settings make the slice out of date: the button slices again instead of opening it
                        bool slicingChanged = SlicingChanged(old, settings);
                        if (slicingChanged || old.FilamentPreset != settings.FilamentPreset || old.SlicerPath != settings.SlicerPath)
                            SetStudioFile(null);
                        SaveLastPrices();
                        RefreshSettingsLabels();
                        foreach (var cb in rowCombo) cb.Tag = null;   // nozzle / slicer may have changed: re-read the list when opened
                        if (!jobLoaded && cts == null) SetColours(null);   // no model yet: the row follows Settings' filament profile
                        Recalculate();
                        if (cts == null)
                        {
                            if (jobLoaded && slicingChanged && old.ActivePrinterId != settings.ActivePrinterId) ShowPrinterChanged();
                            else ShowAutoIdle();
                        }
                        if (dlg.RestartRequested && !offline)
                        {
                            // new language: settings are already saved above; Restart closes this window (saves again) and starts anew
                            if (cts != null) cts.Cancel();
                            BeginInvoke((MethodInvoker)Program.Restart);   // same arguments (test mode stays in its folder)
                        }
                    }
                }
            }
            finally { restore(); }   // also if building the window failed
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
            foreach (string t in new[] { full, L.S("⬆  Update v", "⬆  Opdatér v") + ver, L.S("⬆  v" + ver, "⬆  Opdatér") })   // Danish keeps the word, not the version
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
            return QuoteRows.JobText(customer, LinkSource(),lastHours, lastMinutes, txtGrams.Text,
                                     filaments != null ? filaments.Count : 0, last.FriendsPrice, last.OthersPrice);
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
            miQuoteInfo.Text = L.S("No name · ", "Uden navn · ")
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

        /// "Show quote…": the quote window (preview, customer name, price, look, copy / save). Nothing is saved by opening it.
        void ShowQuoteWindow()
        {
            if (last == null || lastInput == null || cts != null) return;
            using (var f = NewQuoteForm("")) f.ShowDialog(this);
        }

        QuoteForm NewQuoteForm(string customer)
        {
            EnsureHistory();
            quoteWhen = DateTime.Now;   // the preview's number/date until an export saves the quote (then the record's)
            return new QuoteForm(QuoteModelName(), customer, settings.QuoteFriends, settings.QuoteDark != 0,
                new List<string>(History.RecentCustomers(history, 10)), last.OthersPrice, last.FriendsPrice,
                (friends, name) => BuildQuote(friends, quoteWhen, History.QuoteNumber(quoteWhen), name),
                ExportQuote,
                (friends, dark) =>
                {
                    settings.QuotePrice = friends ? AppSettings.QuotePriceFriends : AppSettings.QuotePriceCustomer;
                    settings.QuoteDark = dark ? 1 : 0;
                    if (!offline) settings.Save();
                });
        }

        /// One export (window or ▾ menu): Copy picture / Save picture / Save as PDF ask for the file first (null = cancelled),
        /// then the quote goes into the history with this price and customer (History.AddFor), then the page is drawn
        /// (PDF always light) and copied / written. Returns the status text; failed = it is an error text.
        string ExportQuote(IWin32Window owner, QuoteExport kind, bool friends, bool dark, string customer, out bool failed)
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
                string e = SaveToHistory(friends ? History.ForFriends : History.ForCustomer, customer, out saved);
                if (saved != null) { DateTime w; if (History.TryTime(saved.SavedAt, out w)) quoteWhen = w; }
                if (e != null) { failed = true; return e; }
                return L.S("✓ Saved to history.", "✓ Gemt i historikken.");
            }
            string path = null;
            if (kind == QuoteExport.Png || kind == QuoteExport.Pdf)
            {
                bool pdf = kind == QuoteExport.Pdf;
                using (var dlg = new SaveFileDialog
                {
                    Title = pdf ? L.S("Save quote as PDF", "Gem tilbud som PDF") : L.S("Save quote picture", "Gem tilbud som billede"),
                    Filter = pdf ? "PDF (*.pdf)|*.pdf" : L.S("PNG picture", "PNG-billede") + " (*.png)|*.png",
                    FileName = QuoteFileName(DateTime.Now, customer) + (pdf ? ".pdf" : ".png"),
                    AddExtension = true, OverwritePrompt = true
                })
                {
                    if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
                    path = dlg.FileName;
                }
            }
            QuoteRecord rec;
            string err = SaveToHistory(friends ? History.ForFriends : History.ForCustomer, customer, out rec);
            DateTime when;
            if (rec == null || !History.TryTime(rec.SavedAt, out when)) when = DateTime.Now;
            quoteWhen = when;
            string done;
            try
            {
                if (kind == QuoteExport.Text)
                {
                    Clipboard.SetText(QuoteText(customer));
                    done = L.S("✓ Quote text copied", "✓ Tilbuddet er kopieret som tekst");
                }
                else
                {
                    var data = BuildQuote(friends, when, History.QuoteNumber(when), customer);
                    using (var bmp = QuotePage.Render(data, dark && kind != QuoteExport.Pdf))   // a printed PDF is always light
                    {
                        if (kind == QuoteExport.Copy) QuotePage.CopyToClipboard(bmp);
                        else if (kind == QuoteExport.Png) bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        else QuotePage.SavePdf(bmp, path);
                    }
                    done = kind == QuoteExport.Copy ? L.S("✓ Quote picture copied", "✓ Tilbuddet er kopieret som billede")
                         : L.F("✓ Saved {0}", "✓ Gemt {0}", Path.GetFileName(path));
                }
            }
            catch (Exception ex)
            {
                failed = true;
                return (kind == QuoteExport.Copy || kind == QuoteExport.Text ? L.S("Clipboard: ", "Udklipsholder: ") : "") + ex.Message;
            }
            if (err != null) { failed = true; return done + ". " + err; }
            return done + L.S(" and saved to history.", " og gemt i historikken.");
        }

        /// Default file name of a quote: "2026-11-04 3D Benchy" (+ " - Mette Hansen" with a customer).
        string QuoteFileName(DateTime date, string customer)
        {
            return QuoteRows.FileName(date, QuoteModelName(), customer);
        }

        /// The model's name for the quote: the job's model name, else "3D print".
        string QuoteModelName()
        {
            return QuoteRows.ModelName(jobModel);
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
                PurgeGrams = filaments != null ? jobPurge : -1,
                Breakdown = Pricing.Breakdown(lastInput, settings, friends, wear)
            };
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
            return d;
        }

        /// The build plate's short name for the wear line: "Smooth PEI / High Temp Plate" -> "Smooth PEI" ("Glat PEI").
        string QuotePlate()
        {
            string plate = Wear.FindPlate(settings.BedType).Label;
            int cut = plate.IndexOf(" / ", StringComparison.Ordinal);
            if (cut > 0) plate = plate.Substring(0, cut);
            if (L.Danish && plate == "Smooth PEI") plate = "Glat PEI";
            return plate;
        }

        string PrinterText()
        {
            return PrinterText(null);
        }

        /// "<printer name>, 0.4 mm nozzle": the active printer, or a saved quote's printer (its saved name; the nozzle of
        /// that printer today, if it still exists).
        string PrinterText(QuoteRecord r)
        {
            var p = r != null ? settings.FindPrinter(History.PrinterOf(r)) : null;
            string name = r != null && !string.IsNullOrEmpty(r.PrinterName) ? r.PrinterName : p != null ? p.Name : settings.PrinterName;
            var nozzle = Wear.FindNozzle(p != null ? p.Nozzle : settings.Nozzle);
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
            string help = invoice
                ? L.S("Making the invoice (copy or save) gives it a number and ticks Printed in the history. Esc closes.",
                      "Når fakturaen laves (kopi eller gem), får den et nummer, og Printet bliver sat i historikken. Esc lukker.")
                : L.S("Copying or saving puts the name and price on this quote in the history. Esc closes.",
                      "Når du kopierer eller gemmer, kommer navn og pris på dette tilbud i historikken. Esc lukker.");
            var f = new QuoteForm(RecordModelName(r), r.Customer ?? "", r.For == History.ForFriends, settings.QuoteDark != 0,
                new List<string>(History.RecentCustomers(history, 10)), r.OthersPrice, r.FriendsPrice,
                (friends, name) => BuildRecordQuote(r, friends, name, invoice, pic),
                (IWin32Window o, QuoteExport kind, bool friends, bool dark, string customer, out bool failed) =>
                    ExportRecord(o, r, invoice, kind, friends, dark, customer, pic, out failed),
                (friends, dark) =>
                {
                    settings.QuoteDark = dark ? 1 : 0;   // the price choice belongs to the record (For), not to Settings
                    if (!offline) settings.Save();
                },
                invoice, help);
            if (owned && pic != null) f.FormClosed += delegate { pic.Dispose(); };
            return f;
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
            return QuoteRows.ModelName(r.Model);
        }

        /// The page of a saved record: its saved inputs and prices (History.ToInput + SavedSettings), quote number and
        /// date from SavedAt; invoice: its number and date (or the next number and today while it isn't made yet),
        /// due date and the payment ways set in Settings.
        QuoteData BuildRecordQuote(QuoteRecord r, bool friends, string customer, bool invoice, Image pic)
        {
            DateTime saved;
            if (!History.TryTime(r.SavedAt, out saved)) saved = DateTime.Now;
            var d = new QuoteData
            {
                Name = (settings.QuoteName ?? "").Trim(),
                Customer = (customer ?? "").Trim(),
                Model = RecordModelName(r),
                Link = ShortLink(r.Source),
                Picture = pic,
                Logo = Logo,
                Hours = r.Hours, Minutes = r.Minutes,
                Weight = Num.Fmt(r.Grams) + " g",
                Printer = PrinterText(r),
                Number = History.QuoteNumber(r), Date = saved, ValidUntil = settings.QuoteValidUntil(saved),
                PlateName = QuotePlate(),
                PurgeGrams = r.Filaments != null && r.Filaments.Count >= 2 ? r.PurgeGrams : -1,
                Breakdown = Pricing.Breakdown(History.ToInput(r), History.SavedSettings(r, settings), friends, null)
            };
            // N4: the page shows the saved price (as Copy text and the History list do), even if the inputs no longer give it
            Pricing.MatchSavedTotal(d.Breakdown, friends ? r.FriendsPrice : r.OthersPrice);
            if (r.Filaments != null)
                for (int k = 0; k < r.Filaments.Count; k++)
                {
                    var f = r.Filaments[k];
                    d.MaterialNames.Add(MaterialName(new FilamentUse { Slot = k + 1, Profile = f.Name ?? "", Type = f.Type ?? "", Colour = f.Colour ?? "" }));
                    d.MaterialColours.Add(ParseColour(f.Colour));
                }
            else d.MaterialNames.Add(BambuSlicer.ShortName(settings.FilamentPreset).Trim());
            d.Material = QuoteRows.MaterialLine(r.Filaments != null ? r.Filaments.Select(f => f.Type) : null,
                                                r.Filaments != null ? r.Filaments.Count : 0, d.MaterialNames[0]);
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
            }
            return d;
        }

        /// One export of a record's quote / invoice: file dialog first (null = cancelled), then the record gets the name
        /// and For (and the invoice number), the history is saved, the page is drawn (PDF always light) and copied / written.
        string ExportRecord(IWin32Window owner, QuoteRecord r, bool invoice, QuoteExport kind, bool friends, bool dark,
                            string customer, Image pic, out bool failed)
        {
            failed = false;
            customer = (customer ?? "").Trim();
            if (kind == QuoteExport.History)
            {
                // "Save to history" of a saved record: only its name and price (For); an invoice is not made by this
                r.Customer = customer;
                r.For = friends ? History.ForFriends : History.ForCustomer;
                string e = offline ? null : History.Save(history);
                if (e != null) { failed = true; return e; }
                return L.S("✓ Saved to history.", "✓ Gemt i historikken.");
            }
            if (invoice && customer.Length == 0)
            {
                failed = true;
                return L.S("Type the customer's name first: it goes on the invoice.", "Skriv kundens navn først: det kommer på fakturaen.");
            }
            DateTime now = DateTime.Now;
            string path = null;
            if (kind == QuoteExport.Png || kind == QuoteExport.Pdf)
            {
                bool pdf = kind == QuoteExport.Pdf;
                string title = invoice ? (pdf ? L.S("Save invoice as PDF", "Gem faktura som PDF") : L.S("Save invoice picture", "Gem faktura som billede"))
                                       : (pdf ? L.S("Save quote as PDF", "Gem tilbud som PDF") : L.S("Save quote picture", "Gem tilbud som billede"));
                // "2026-11-02 Model - Name": the quote's date, for an invoice its date (today while it isn't made yet)
                DateTime qd;
                if (invoice) { if (string.IsNullOrEmpty(r.InvoiceNo) || !History.TryTime(r.BilledAt, out qd)) qd = now; }
                else if (!History.TryTime(r.SavedAt, out qd)) qd = DateTime.Now;
                string name = QuoteRows.FileName(qd, RecordModelName(r), customer);
                using (var dlg = new SaveFileDialog
                {
                    Title = title,
                    Filter = pdf ? "PDF (*.pdf)|*.pdf" : L.S("PNG picture", "PNG-billede") + " (*.png)|*.png",
                    FileName = name + (pdf ? ".pdf" : ".png"), AddExtension = true, OverwritePrompt = true
                })
                {
                    if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
                    path = dlg.FileName;
                }
            }
            r.Customer = customer;
            r.For = friends ? History.ForFriends : History.ForCustomer;
            string err = null;
            if (invoice)
            {
                History.MakeInvoice(history, r, now, settings);   // random code (never given twice: settings.UsedInvoiceNos), date, Printed
                if (!offline) settings.Save();
            }
            if (!offline) err = History.Save(history);
            string done;
            try
            {
                if (kind == QuoteExport.Text)
                {
                    Clipboard.SetText(RecordText(r, friends, invoice));
                    done = invoice ? L.S("✓ Invoice text copied", "✓ Fakturaen er kopieret som tekst") : L.S("✓ Quote text copied", "✓ Tilbuddet er kopieret som tekst");
                }
                else
                {
                    var data = BuildRecordQuote(r, friends, customer, invoice, pic);
                    using (var bmp = QuotePage.Render(data, dark && kind != QuoteExport.Pdf))   // a printed PDF is always light
                    {
                        if (kind == QuoteExport.Copy) QuotePage.CopyToClipboard(bmp);
                        else if (kind == QuoteExport.Png) bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        else QuotePage.SavePdf(bmp, path);
                    }
                    done = kind == QuoteExport.Copy
                        ? (invoice ? L.S("✓ Invoice picture copied", "✓ Fakturaen er kopieret som billede") : L.S("✓ Quote picture copied", "✓ Tilbuddet er kopieret som billede"))
                        : L.F("✓ Saved {0}", "✓ Gemt {0}", Path.GetFileName(path));
                }
            }
            catch (Exception ex)
            {
                failed = true;
                return (kind == QuoteExport.Copy || kind == QuoteExport.Text ? L.S("Clipboard: ", "Udklipsholder: ") : "") + ex.Message;
            }
            if (err != null) { failed = true; return done + ". " + err; }
            return done + (invoice ? L.F(". Invoice no. {0} is in the history.", ". Faktura nr. {0} er gemt i historikken.", r.InvoiceNo)
                                   : L.S(" and saved to history.", " og gemt i historikken."));
        }

        /// A record as plain text: title, name, model, time and grams, the chosen price; invoice: number, dates, payment.
        string RecordText(QuoteRecord r, bool friends, bool invoice)
        {
            return QuoteRows.RecordText(r, friends, invoice, settings, ShortLink(r.Source));
        }

        /// Picture mode: the invoice page of the job on screen (sample number, date, customer and payment ways; nothing saved).
        internal Bitmap RenderSampleInvoice()
        {
            if (!offline || last == null || lastInput == null) return null;
            var when = new DateTime(2026, 11, 4, 17, 45, 0);
            var d = BuildQuote(false, when, History.QuoteNumber(when), "Lene Hansen");
            d.Invoice = true;
            d.InvoiceNo = "2026-K7Q4";
            d.Due = settings.InvoiceDue(when);
            d.PayMobilePay = "12 34 56 78";
            d.PayBank = L.S("reg. 1234, account 0012345678", "reg.nr. 1234, konto 0012345678");
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
            History.ReservePrinterIds(history, settings);   // a new printer never reuses an id from the history
            if (historyProblem == null && !picturesCleaned) { picturesCleaned = true; History.CleanPictures(history); }   // once per run
        }

        bool picturesCleaned;   // History.CleanPictures ran this run (StartupPictureClean or EnsureHistory)

        /// At start (Shown): reads and parses history.json off the UI thread (no Load: that renames bad files and blocks
        /// saving), then cleans leftover pictures on the UI thread, only when the file read fine and the history hasn't been
        /// loaded meanwhile (a save loads it first, so a just-saved picture is never in danger; EnsureHistory cleans then).
        async void StartupPictureClean()
        {
            if (offline || picturesCleaned) return;
            List<QuoteRecord> list = null;
            try
            {
                list = await Task.Run(() =>
                {
                    string path = History.FilePath;
                    if (!File.Exists(path)) return new List<QuoteRecord>();
                    return History.ParseJson(File.ReadAllText(path, System.Text.Encoding.UTF8));   // null = not readable: skip
                });
            }
            catch { return; }   // locked / unreadable: EnsureHistory decides later
            if (list == null || IsDisposed || history != null || picturesCleaned) return;
            picturesCleaned = true;
            try { History.CleanPictures(list); } catch { }
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
            saved = null;
            if (last == null || lastInput == null) return L.S("Nothing to save yet.", "Der er ikke noget at gemme endnu.");
            EnsureHistory();
            var r = History.FromQuote(lastInput, last, settings, jobModel ?? "", LinkSource(), DateTime.Now);
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
                }
                if (filaments != null) r.PurgeGrams = jobPurge;
            }
            if (jobParts != null) History.SetParts(r, jobParts, jobTogether);   // idea 21: the filament / colour per file
            else if (repeatOf != null && History.PartsOf(repeatOf) != null)    // a repeat print: new records don't copy them
                History.SetParts(r, History.PartsOf(repeatOf), History.PartsTogether(repeatOf));
            if (reopened != null) r.For = reopened.For;   // opened again and re-saved: same person as before
            if (repeatOf != null) { r.For = repeatOf.For; r.RepeatOf = repeatOf.Id ?? ""; }   // a repeat print: new record, same person
            if (forWho != 0)
            {
                saved = History.AddFor(history, r, forWho);
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
            if (offline || r == null || string.IsNullOrEmpty(r.Id)) return null;
            var img = History.PictureImage(History.LoadPicture(r.Id));
            if (img == null) return null;
            var bmp = img as Bitmap;
            if (bmp != null) return bmp;
            using (img) return new Bitmap(img);
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
            Environment.Exit(ok ? 0 : 4);
        }

        // ---------- test hooks for idea 21 (test mode + --hidden only; plans\idea21-multipart\test-hook.md) ----------
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
            try
            {
                var parts = new List<HistoryPart>();
                bool together = false;
                string baseDir = Path.GetDirectoryName(specFile);
                foreach (var raw in File.ReadAllLines(specFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("//")) continue;
                    if (line.StartsWith("together=", StringComparison.OrdinalIgnoreCase)) { together = line.Substring(9).Trim() == "1"; continue; }
                    var f = line.Split('|');
                    string file = f[0].Trim().Trim('"');
                    if (!Path.IsPathRooted(file)) file = Path.GetFullPath(Path.Combine(baseDir, file));
                    parts.Add(new HistoryPart { File = file, Filament = f.Length > 1 ? f[1].Trim() : "", Colour = f.Length > 2 ? f[2].Trim() : "" });
                }
                log.Add("spec: " + parts.Count + " file(s), together=" + (together ? 1 : 0));
                check(parts.Count > 0 && parts.All(p => File.Exists(p.File)), "spec files exist");
                if (ok)
                {
                    var files = parts.Select(p => p.File).ToList();
                    txtUrl.Text = files.Count == 1 ? files[0] : L.F("{0} files: ", "{0} filer: ", files.Count) + string.Join(", ", files.Select(Path.GetFileName));
                    lastJobResult = null;
                    bool done = await AnalyzeAsync(null, files, null, parts, together);
                    check(done && lastJobResult != null, "sliced");
                    TestJobLines(log, lastJobResult);
                    QuoteRecord rec = null;
                    string err = done ? SaveToHistory(0, null, out rec) : "not sliced";
                    if (err != null) rec = null;
                    check(err == null && rec != null, "saved to history" + (err != null ? ": " + err : ""));
                    TestRecordLines(log, rec);
                    var savedParts = rec != null ? History.PartsOf(rec) : null;
                    check(savedParts != null && savedParts.Count == parts.Count, "record keeps the " + parts.Count + " part(s)");
                }
                Screenshots.Snap(this, dir, "test_slice.png");
            }
            catch (Exception ex) { check(false, "exception: " + ex); }
            try { File.WriteAllLines(Path.Combine(dir, "test_slice.txt"), log); } catch { }
            Environment.Exit(ok ? 0 : 4);
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
                    await RepeatAsync(r);
                    check(repeatWhy == null && lastJobResult != null, "sliced again" + (repeatWhy != null ? " (not: " + repeatWhy + ")" : ""));
                    TestJobLines(log, lastJobResult);
                    QuoteRecord rec;
                    string err = SaveToHistory(0, null, out rec);
                    if (err != null) rec = null;
                    check(err == null && rec != null, "saved to history" + (err != null ? ": " + err : ""));
                    check(rec != null && rec.RepeatOf == (r.Id ?? ""), "saved as a repeat of the original");
                    TestRecordLines(log, rec);
                    var a = History.PartsOf(r);
                    var b = rec != null ? History.PartsOf(rec) : null;
                    check((a == null) == (b == null) && (a == null || a.Count == b.Count), "same parts as the original");
                }
                Screenshots.Snap(this, dir, "test_repeat.png");
            }
            catch (Exception ex) { check(false, "exception: " + ex); }
            try { File.WriteAllLines(Path.Combine(dir, "test_repeat.txt"), log); } catch { }
            Environment.Exit(ok ? 0 : 4);
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
                            f.Grams.ToString("0.##", Num.Dk) + " g");
                log.Add("together: " + res.Together + ", together problem: " + (res.TogetherProblem ?? "(none)"));
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
                    (string.IsNullOrEmpty(r.RepeatOf) ? "-" : r.RepeatOf));
            var parts = History.PartsOf(r);
            if (parts == null) log.Add("record parts: (none)");
            else
            {
                log.Add("record parts together: " + History.PartsTogether(r));
                foreach (var p in parts) log.Add("record part: " + p.File + " | " + p.Filament + " | " + p.Colour + " | slot " + p.Slot);
            }
            if (r.Filaments != null)
                foreach (var f in r.Filaments)
                    log.Add("record colour: " + (f.Colour ?? "") + " " + (f.Type ?? "") + " \"" + (f.Name ?? "") + "\" " + f.Grams.ToString("0.##", Num.Dk) + " g");
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
                dlg.QuoteRequested = (owner, rec, invoice) => ShowRecordQuote(owner, rec, invoice);
                dlg.Settings = settings;   // printer names (Printer column / filter with 2+ printers)
                // Backup…: the Restore tab first while the history is empty (a new PC)
                dlg.BackupRequested = owner =>
                {
                    bool restored = OpenBackup(owner, history.Count == 0 ? BackupForm.TabRestore : BackupForm.TabBackup);
                    if (restored) dlg.Settings = settings;
                    return restored;
                };
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Selected != null)
                {
                    if (dlg.RepeatSelected) Repeat(dlg.Selected);
                    else Reopen(dlg.Selected);
                }
                else Recalculate();   // Failed / For / Delete in the list change the payback debt
            }
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
                bool done = await AnalyzeAsync(id, paths, res => KeepSavedMaterials(res, r), parts, History.PartsTogether(r));
                if (done) { repeatOf = r; ShowRepeatLine(r, null); return; }
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
            bool ok = await AnalyzeAsync(id, local ? new List<string> { src } : null, res => KeepSavedMaterials(res, r));
            if (ok) { repeatOf = r; ShowRepeatLine(r, null); return; }
            string why = (lblAuto.Text ?? "").Trim().TrimEnd('.');
            FillFromRecord(r, true);   // back to the saved time, grams and colours (Analyze reset the job)
            repeatOf = r;
            ShowRepeatLine(r, why.Length > 0 ? why : L.S("slicing failed", "slicing mislykkedes"));
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
            jobParts = History.PartsOf(r);   // a colour per file (idea 21): saving it again / a failed repeat keeps them
            jobTogether = History.PartsTogether(r);
            ResetModelPicture();
            SetJobLoaded(true);   // no picture (Printables link, missing file) = logo without "No model yet"
            string src = (r.Source ?? "").Trim().Trim('"');
            var stored = StoredPicture(r);   // the picture saved with the record (issue #4) comes first
            if (stored != null) SetModelPicture(stored);
            else if (!offline && src.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) && File.Exists(src))
                LoadModelPicture(() => BambuSlicer.FromProject(src), true);   // a local project still has its own picture
            else if (!offline && BambuSlicer.IsGcodeFile(src) && File.Exists(src))
                LoadModelPicture(() => BambuSlicer.ReadGcodeFile(src).Picture, true);   // a G-code's thumbnail (errors = none)

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
            string main = plates ? OneLineRepeatText(r, when)
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
            lblAuto.SetLink(PlatesLinkText(list.Count, fellBack), () => ShowPlatesMenu(list));
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
