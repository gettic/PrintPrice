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
    public class MainForm : Form
    {
        AppSettings settings;
        TextBox txtUrl, txtHours, txtMinutes, txtGrams, txtFilamentPrice, txtElecPrice;
        Label lblPrinter, lblMaterial, lblPower, lblFailed, lblFailedCap, lblBase;
        ThemedGroupBox grpBreak;
        int breakBaseHeight, breakRowHeight;   // Cost breakdown height without the failed-prints row, and one row (scaled, set on Load)
        Label lblFriends, lblOthers, lblFriendsInfo, lblOthersInfo, lblStatus;
        Button btnCopy, btnUpdate;
        UpdateInfo pendingUpdate;   // newer version found at startup (null = none)
        System.Windows.Forms.Timer updateTimer;   // quiet check every 5 min while running (null in picture mode)
        bool checkingUpdate, updateDialogOpen;
        QuoteResult last;
        int lastHours, lastMinutes;
        bool statusIsSuccess;

        // auto-detect
        Button btnAnalyze, btnLoadFile;
        InfoBox lblAuto;
        bool autoIsError;
        CancellationTokenSource cts;

        // model picture under the "Model link" label (logo + "No model yet" while empty)
        PictureBox picModel;
        int picRequest;   // bumped for every new job / Clear, so a late picture of an old job is dropped
        int picBaseHeight;   // buttons' top to a 2-line info box's bottom (scaled, set on Load)

        // multi-colour (2+ filaments): one row per colour in the Cost breakdown instead of the Material line,
        // the "Filament price" row hides and "Filament used" shows the read-only total
        const int MaxColourRows = 4;   // AMS lite has 4 slots; with more, row 4 holds all the rest ("+N more")
        List<FilamentUse> filaments;   // null = single filament (today's layout)
        int colourRows;                // rows in use (0 = single layout)
        readonly List<int>[] rowItems = new List<int>[MaxColourRows];   // indices into filaments per row
        readonly Control[][] rowControls = new Control[MaxColourRows][];
        readonly Swatch[] rowSwatch = new Swatch[MaxColourRows];
        readonly Label[] rowType = new Label[MaxColourRows], rowGrams = new Label[MaxColourRows], rowCost = new Label[MaxColourRows];
        readonly TextBox[] rowPrice = new TextBox[MaxColourRows];
        TableLayoutPanel colourGrid;
        Label lblMaterialCap, lblPurge;
        Control filPriceCap, filPriceRow;
        bool fillingColours;   // no recalculation while the rows are being filled

        // live electricity price
        Button btnPower;
        InfoBox lblPowerInfo;
        bool powerIsError, fetchingPower;

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
            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterScreen;
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
            txtUrl = new HintBox { Width = 394, Margin = new Padding(3, 3, 0, 3) };
            // grey example text while empty; re-applied whenever the handle is recreated (e.g. theme change)
            txtUrl.HandleCreated += delegate { ApplyUrlCue(); };
            // typing/pasting brings the grey example text back
            txtUrl.TextChanged += delegate { SetUrlHint(false); };
            AddRow(inputs, L.S("Model link", "Link til model"), txtUrl, null);
            inputs.SetColumnSpan(txtUrl, 2);

            // two equal buttons lined up exactly under the link box (widths set on Load)
            btnAnalyze = new Button { Text = AnalyzeText, Width = 194, Height = 32, Margin = new Padding(3, 6, 6, 4) };
            btnLoadFile = new Button { Text = L.S("Load file…", "Åbn fil…"), Width = 194, Height = 32, Margin = new Padding(0, 6, 0, 4) };
            btnAnalyze.Click += delegate { AnalyzeClicked(); };
            btnLoadFile.Click += delegate { BrowseForModel(); };
            // Enter in the link box = "Get time & weight" (not while a job runs, so it never cancels by accident)
            txtUrl.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None) return;
                e.SuppressKeyPress = true;   // no beep
                if (cts == null) AnalyzeClicked();   // empty box: shows the red hint (AnalyzeClicked)
            };
            var autoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            autoRow.Controls.Add(btnAnalyze);
            autoRow.Controls.Add(btnLoadFile);
            int picRow = inputs.RowCount;
            AddRow(inputs, "", autoRow, null);
            inputs.SetColumnSpan(autoRow, 2);
            lblAuto = new InfoBox(394, 9.5f) { Margin = new Padding(3, 4, 0, 10) };
            AddRow(inputs, "", lblAuto, null);
            // model picture in the label column, beside the buttons and the info box (size measured on Load)
            picModel = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Input, Size = new Size(10, 10),
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
            AddRow(inputs, L.S("Print time", "Printtid"), timeRow, null);
            Load += delegate
            {
                // number boxes below end exactly where the m box ends (DPI-safe, measured)
                int right = txtMinutes.Parent.PointToScreen(new Point(txtMinutes.Right, 0)).X;
                foreach (var tb in new[] { txtGrams, txtFilamentPrice, txtElecPrice })
                    tb.Width = right - tb.Parent.PointToScreen(new Point(tb.Left, 0)).X;
            };

            txtGrams = NumBox("");
            AddRow(inputs, L.S("Filament used", "Filamentforbrug"), WithUnit(txtGrams, "g"), null);
            txtFilamentPrice = NumBox(Num.Fmt(settings.LastFilamentPrice));
            filPriceRow = WithUnit(txtFilamentPrice, "kr./kg");
            filPriceCap = AddRow(inputs, L.S("Filament price", "Filamentpris"), filPriceRow, null);   // row hides for 2+ colours
            txtElecPrice = NumBox(Num.Fmt(settings.LastElectricityPrice));
            var elecRow = (FlowLayoutPanel)WithUnit(txtElecPrice, "kr./kWh");
            // same top margin as the text box; height is matched to the box once the form has loaded (DPI-safe)
            // slightly smaller font + no padding so the text fits inside a button only as tall as the text box
            btnPower = new Button
            {
                Text = L.S("Update", "Opdatér"), AutoSize = false, Width = 76, Margin = new Padding(4, 3, 0, 3),
                Padding = Padding.Empty, Font = new Font("Segoe UI", 9f), TextAlign = ContentAlignment.MiddleCenter
            };
            Load += delegate
            {
                btnPower.Height = txtElecPrice.Height;
                btnPower.Margin = new Padding(btnPower.Margin.Left, txtElecPrice.Margin.Top, 0, txtElecPrice.Margin.Bottom);
            };
            btnPower.Click += delegate { FetchPowerPrice(); };
            elecRow.Controls.Add(btnPower);
            AddRow(inputs, L.S("Electricity price", "Elpris"), elecRow, null);
            lblPowerInfo = new InfoBox(394, 9.5f) { Margin = new Padding(3, 2, 0, 8) };
            AddRow(inputs, "", lblPowerInfo, null);
            inputs.SetColumnSpan(lblPowerInfo, 2);
            lblPrinter = new Label { AutoSize = true, Tag = "muted", Margin = new Padding(3, 4, 3, 4) };
            AddRow(inputs, L.S("Printer", "Printer"), lblPrinter, null);
            root.Controls.Add(inputs);

            // ---------- Breakdown ----------
            grpBreak = new ThemedGroupBox { Text = L.S("Cost breakdown", "Omkostninger"), Width = 540, Height = 110, Margin = new Padding(0, 0, 0, 8) };
            var bt = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2) };
            bt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            bt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            lblMaterial = BreakLabel(); lblPower = BreakLabel(); lblFailed = BreakLabel(); lblBase = BreakLabel();
            lblMaterialCap = BreakCaption(L.S("Material", "Materiale"));
            bt.Controls.Add(lblMaterialCap, 0, 0); bt.Controls.Add(lblMaterial, 1, 0);
            // 2+ colours: the Material row hides, one row per colour + a muted purge line show instead (SetColours)
            colourGrid = BuildColourGrid();
            bt.Controls.Add(colourGrid, 0, 1); bt.SetColumnSpan(colourGrid, 2);
            lblPurge = new Theme.PlainLabel { AutoSize = true, Tag = "muted", Font = new Font("Segoe UI", 9f), UseMnemonic = false, Margin = new Padding(3, 0, 3, 4), Visible = false };
            bt.Controls.Add(lblPurge, 0, 2); bt.SetColumnSpan(lblPurge, 2);
            bt.Controls.Add(BreakCaption(L.S("Power", "Strøm")), 0, 3); bt.Controls.Add(lblPower, 1, 3);
            // failed prints: hidden (row collapses, box shrinks) when the setting is 0 %, see UpdateFailedRow
            lblFailedCap = BreakCaption("");
            bt.Controls.Add(lblFailedCap, 0, 4); bt.Controls.Add(lblFailed, 1, 4);
            var baseCap = BreakCaption(L.S("Base cost", "Kostpris")); baseCap.Font = new Font(Font, FontStyle.Bold);
            lblBase.Font = new Font(Font, FontStyle.Bold);
            bt.Controls.Add(baseCap, 0, 5); bt.Controls.Add(lblBase, 1, 5);
            grpBreak.Controls.Add(bt);
            root.Controls.Add(grpBreak);
            Load += delegate
            {
                // scaled sizes: the box's 3-row height, plus one row (label + margins) for the failed-prints line
                breakBaseHeight = grpBreak.Height;
                breakRowHeight = lblPower.Height + lblPower.Margin.Vertical;
                UpdateFailedRow();
                // link box, the two buttons and both info boxes end exactly at the Cost breakdown's right edge
                // (measured, so it stays lined up at any DPI and whatever the label column's width)
                int edge = grpBreak.Parent.PointToScreen(new Point(grpBreak.Right, 0)).X;
                int w = edge - txtUrl.Parent.PointToScreen(new Point(txtUrl.Left, 0)).X;
                txtUrl.Width = w;
                lblAuto.SetWidth(w);
                lblPowerInfo.SetWidth(w);
                int gap = btnAnalyze.Margin.Right;
                btnAnalyze.Width = (w - gap) / 2;
                btnLoadFile.Width = w - gap - btnAnalyze.Width;

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
            // update button: same width as the Friends & family box above it, only shown when GitHub has a newer version
            btnUpdate = new Button
            {
                Size = new Size(264, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 0, 3),
                Anchor = AnchorStyles.Left, Visible = false, Font = new Font(Font, FontStyle.Bold)
            };
            btnUpdate.Click += delegate { if (pendingUpdate != null) ShowUpdate(pendingUpdate); };
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            // three buttons spanning exactly the Customers box (264 px = 76 + 84 + 92 + 2 × 6 gap) above them
            var btnClear = new Button { Text = L.S("Clear", "Ryd"), Size = new Size(76, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 6, 3) };
            var btnSettings = new Button { Text = L.S("Settings…", "Indstillinger…"), Size = new Size(84, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 6, 3) };
            btnCopy = new Button { Text = L.S("Copy quote", "Kopiér tilbud"), Size = new Size(92, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 0, 3) };
            // same total width (scaled), split by the measured text widths so longer (Danish) texts fit
            Load += delegate { SplitByText(new[] { btnClear, btnSettings, btnCopy }); };
            btnClear.Click += delegate { ClearJob(); };
            btnSettings.Click += delegate { OpenSettings(); };
            btnCopy.Click += delegate { CopyQuote(); };
            buttons.Controls.Add(btnClear);
            buttons.Controls.Add(btnSettings);
            buttons.Controls.Add(btnCopy);
            bottom.Controls.Add(btnUpdate, 0, 0);
            bottom.Controls.Add(buttons, 1, 0);
            root.Controls.Add(bottom);
            root.Controls.Add(lblStatus);

            // live recalculation
            EventHandler recalc = delegate { Recalculate(); };
            foreach (var tb in new[] { txtHours, txtMinutes, txtGrams, txtFilamentPrice, txtElecPrice })
            {
                var box = tb;
                box.TextChanged += recalc;
                box.Enter += delegate { BeginInvoke((MethodInvoker)delegate { box.SelectAll(); }); };
            }

            FormClosing += delegate { if (cts != null) cts.Cancel(); SaveLastPrices(); };
            FormClosed += delegate
            {
                picRequest++; SetModelPicture(null);
                if (updateTimer != null) { updateTimer.Stop(); updateTimer.Dispose(); updateTimer = null; }
            };
            if (!offline)
            {
                updateTimer = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };
                updateTimer.Tick += delegate { PeriodicUpdateCheck(); };
                Shown += delegate { SyncUpdateTimer(); };
            }
            EnableDrop(this);

            ApplyTheme();
            ResumeLayout(true);
            RefreshSettingsLabels();
            ShowAutoIdle();
            if (!offline) Shown += delegate { StartupUpdateCheck(); };
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

        protected override bool ShowWithoutActivation { get { return offline || Screenshots.Active; } }

        /// Picture mode: fills in a fixed example Benchy job, as if it had been sliced and the live
        /// price fetched (made-up texts, nothing goes online). The price labels update by themselves.
        internal void SetSampleJob()
        {
            if (!offline) return;
            SetColours(null);
            txtUrl.Text = "https://www.printables.com/model/3161-3d-benchy";
            SetAuto(L.S("✓ \"3D BENCHY\": 0 h 38 m, 11,8 g (0.20mm Standard · Bambu PLA Basic).",
                        "✓ \"3D BENCHY\": 0 t 38 m, 11,8 g (0.20mm Standard · Bambu PLA Basic)."), false);
            txtHours.Text = "0";
            txtMinutes.Text = "38";
            txtGrams.Text = "11,8";
            txtFilamentPrice.Text = "200";
            txtElecPrice.Text = "3,65";
            SetPowerInfo(L.S("Live DK1 17:45: spot 2,37 + fees 0,55 + 25 % VAT = 3,65 kr.",
                             "Live-pris DK1 17:45: spot 2,37 + afgifter 0,55 + 25 % moms = 3,65 kr."), false);
            Recalculate();
        }

        /// Picture mode: the 4-colour Benchy (Printables 1648230) as our CLI slices it (real numbers from the
        /// multi-colour test: 135,85 g of which 124 g purge, 8,7 h of colour changes). Call after SetSampleJob
        /// (keeps its live price line).
        internal void SetSampleMultiJob()
        {
            if (!offline) return;
            txtUrl.Text = "https://www.printables.com/model/1648230-four-color-multicolor-test-benchy";
            SetAuto(L.S("✓ \"Four-Color Multicolor Test benchy\": 10 h 43 m, 135,85 g (0.20mm Standard · 4 colours).",
                        "✓ \"Four-Color Multicolor Test benchy\": 10 t 43 m, 135,85 g (0.20mm Standard · 4 farver)."), false);
            var r = new SliceResult { Seconds = 38566, Grams = 135.85, Plates = 1, PurgeGrams = 124.0, ColourChangeHours = 31450 / 3600.0 };
            string[] colours = { "#118002", "#FFFF0A", "#074080", "#ED1C24" };
            double[] grams = { 33.04, 55.99, 17.28, 29.54 }, model = { 5.04, 0.38, 5.87, 0.56 };
            for (int i = 0; i < 4; i++)
                r.Filaments.Add(new FilamentUse { Slot = i + 1, Colour = colours[i], Type = "PLA", Grams = grams[i], ModelGrams = model[i] });
            txtHours.Text = "10";
            txtMinutes.Text = "43";
            txtFilamentPrice.Text = "200";
            txtElecPrice.Text = "3,65";
            SetColours(r);
            txtGrams.Text = "135,85";
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
                string breakdown = L.F("spot {0} + fees {1}", "spot {0} + afgifter {1}",
                                       q.SpotDkkPerKwh.ToString("0.00", Num.Dk), addOn.ToString("0.00", Num.Dk)) +
                                   (vat ? L.S(" + 25 % VAT", " + 25 % moms") : "");
                string when = q.Area + " " + q.SlotStartUtc.ToLocalTime().ToString("HH:mm", Num.Dk);
                if (txtElecPrice.Text == before)
                {
                    txtElecPrice.Text = total.ToString("0.00", Num.Dk);
                    // non-breaking spaces: "= 3,65 kr." wraps as one piece, never a lone "kr." on line 2
                    SetPowerInfo(L.S("Live ", "Live-pris ") + when + ": " + breakdown + " = " + total.ToString("0.00", Num.Dk) + " kr.", false);
                }
                else
                {
                    SetPowerInfo(L.F("Live price {0} is {1} kr./kWh (not applied because you edited the field).",
                                     "Live-pris {0} er {1} kr./kWh (ikke brugt, fordi du har rettet feltet).",
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
                    LoadModelPicture(() => render, false);   // only if no Printables photo / 3MF picture is showing
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

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// Placeholder text for a TextBox (.NET Framework has no PlaceholderText property).
        static void SetCueBanner(TextBox box, string text)
        {
            const int EM_SETCUEBANNER = 0x1501;
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);   // 1 = keep showing while focused
        }

        /// Link box that can show a red hint (the native cue banner is always grey).
        sealed class HintBox : TextBox
        {
            public string Hint;   // null = no hint
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != 0x000F || Hint == null || Text.Length > 0) return;   // WM_PAINT
                using (var g = Graphics.FromHwnd(Handle))
                    TextRenderer.DrawText(g, Hint, Font, new Rectangle(1, 0, ClientSize.Width - 2, ClientSize.Height), Theme.Error, Theme.Input,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter);   // centred; 1 px above the first version
            }
        }

        void ApplyUrlCue()
        {
            var box = (HintBox)txtUrl;
            if (!box.IsHandleCreated) return;
            SetCueBanner(box, box.Hint != null ? "" : L.S("e.g. ", "f.eks. ") + "https://www.printables.com/model/3161-3d-benchy");
            box.Invalidate();
        }

        /// true = red "Paste a link or load a file first" in the empty link box; false = grey example again.
        void SetUrlHint(bool on)
        {
            var box = (HintBox)txtUrl;
            string hint = on ? L.S("Paste a link or load a file first", "Indsæt et link eller åbn en fil først") : null;
            if (box.Hint == hint) return;
            box.Hint = hint;
            box.AccessibleDescription = hint;
            ApplyUrlCue();
        }

        // ---------- Model picture ----------
        /// Shows a new picture (null = back to the logo) and disposes the old one.
        void SetModelPicture(Bitmap bmp)
        {
            var old = picModel.Image;
            picModel.Image = bmp;
            if (old != null) old.Dispose();
            picModel.Invalidate();
        }

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
            if (IsDisposed || request != picRequest || (!replace && picModel.Image != null)) { bmp.Dispose(); return; }
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

        /// Border, and while there is no picture: the dimmed logo with "No model yet" under it.
        void PaintModelPicture(Graphics g)
        {
            var r = picModel.ClientRectangle;
            if (picModel.Image == null)
            {
                string text = L.S("No model yet", "Ingen model");
                var font = pictureFont;
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
                int pad = picModel.Padding.Left;
                Size ts = TextRenderer.MeasureText(g, text, font, new Size(r.Width - 2 * pad, 0), flags);
                int gap = pad, topMin = pad * 3 / 2;   // at least 6 px (at 96 dpi) above the logo, 4 px under the text
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

        // ---------- Multi-colour rows (Cost breakdown) ----------
        /// Grid of MaxColourRows rows: swatch, type, grams, ×, kr./kg box, unit, cost (right, lined up with the other amounts).
        /// Spans both breakdown columns; its height is set by SetColours (rows × box row height).
        TableLayoutPanel BuildColourGrid()
        {
            var g = new TableLayoutPanel { ColumnCount = 7, AutoSize = false, Height = 10, Margin = Padding.Empty, Visible = false,
                                           Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            for (int c = 0; c < 6; c++) g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < MaxColourRows; i++)
            {
                g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                rowSwatch[i] = new Swatch { Size = new Size(14, 14), Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 8, 0) };
                rowType[i] = new Label { AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 14, 0) };
                rowGrams[i] = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 6, 0) };
                // grayscale text: ClearType made the small × look orange
                var times = new Theme.PlainLabel { Text = "×", AutoSize = true, Tag = "muted", Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
                var box = new TextBox { Width = 50, TextAlign = HorizontalAlignment.Right, Margin = Padding.Empty };   // rows = box height (25 px at 100 %)
                var unit = Unit("kr./kg");
                unit.Margin = new Padding(2, 0, 10, 0);
                rowPrice[i] = box;
                rowCost[i] = BreakLabel();
                box.TextChanged += delegate { Recalculate(); };
                box.Enter += delegate { BeginInvoke((MethodInvoker)delegate { box.SelectAll(); }); };
                rowControls[i] = new Control[] { rowSwatch[i], rowType[i], rowGrams[i], times, box, unit, rowCost[i] };
                for (int c = 0; c < 7; c++) g.Controls.Add(rowControls[i][c], c, i);
            }
            return g;
        }

        /// Switches between today's single-filament layout (r null or 1 filament) and one row per colour (2+).
        /// Each colour's kr./kg starts at the main filament price; with more than MaxColourRows colours the last
        /// row holds all the rest ("+N more", one shared kr./kg box).
        void SetColours(SliceResult r)
        {
            var list = r == null ? null : r.Filaments.Where(f => f != null && f.Grams > 0).OrderBy(f => f.Slot).ToList();
            bool multi = list != null && list.Count >= 2;
            filaments = multi ? list : null;
            fillingColours = true;
            SuspendLayout();
            int rows = 0;
            if (multi)
            {
                rows = Math.Min(list.Count, MaxColourRows);
                int own = list.Count > MaxColourRows ? MaxColourRows - 1 : rows;   // rows with one colour each
                for (int i = 0; i < rows; i++) rowItems[i] = new List<int>();
                for (int k = 0; k < list.Count; k++) rowItems[k < own ? k : own].Add(k);
                string price = txtFilamentPrice.Text.Trim();
                for (int i = 0; i < rows; i++)
                {
                    var items = rowItems[i];
                    rowSwatch[i].Colours = items.Select(k => ParseColour(list[k].Colour)).ToArray();
                    rowType[i].Text = items.Count > 1 ? L.F("+{0} more", "+{0} flere", items.Count) : FilamentName(list[items[0]]);
                    rowGrams[i].Text = items.Sum(k => list[k].Grams).ToString("0.00", Num.Dk) + " g";
                    rowPrice[i].Text = price;
                    rowCost[i].Text = "–";
                }
            }
            colourRows = rows;
            for (int i = 0; i < MaxColourRows; i++)
                foreach (var c in rowControls[i]) c.Visible = i < rows;
            colourGrid.Height = Math.Max(1, rows * (rowPrice[0].Height + rowPrice[0].Margin.Vertical));
            colourGrid.Visible = multi;
            lblMaterialCap.Visible = lblMaterial.Visible = !multi;
            string note = multi ? PurgeNote(r) : "";
            lblPurge.Text = note;
            lblPurge.Visible = note.Length > 0;
            // the hidden "Filament price" box keeps its value (saved as the last price, default for the next job)
            filPriceCap.Visible = filPriceRow.Visible = !multi;
            txtGrams.ReadOnly = multi;
            txtGrams.TabStop = !multi;
            txtGrams.ForeColor = multi ? Theme.Muted : Theme.Text;
            ResumeLayout(true);
            fillingColours = false;
            UpdateBreakHeight();
            Recalculate();
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

        /// Row text for a filament: its type ("PLA"), else the profile's short name, else "Colour 3".
        static string FilamentName(FilamentUse f)
        {
            if (!string.IsNullOrEmpty(f.Type)) return f.Type;
            if (!string.IsNullOrEmpty(f.Profile)) return BambuSlicer.ShortName(f.Profile);
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
            double grams, filPrice, elPrice;
            var s1 = Num.Read(txtGrams, out grams);
            var s2 = Num.Read(txtFilamentPrice, out filPrice);
            // 2+ colours: one line per colour with its own kr./kg; these boxes replace the hidden filament price box
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
                    foreach (int k in rowItems[i]) lines[k] = new FilamentLine { Grams = filaments[k].Grams, PricePerKg = p };
                }
                s2 = invalid ? FieldState.Invalid : empty ? FieldState.Empty : FieldState.Ok;
                if (filPrice < 0) filPrice = 0;
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
                lblMaterial.Text = lblPower.Text = lblFailed.Text = lblBase.Text = "–";
                foreach (var c in rowCost) c.Text = "–";
                lblFriends.Text = lblOthers.Text = "–";
                btnCopy.Enabled = false;
                return;
            }

            var input = new QuoteInput
            {
                Hours = hours, Minutes = minutes, FilamentGrams = grams,
                FilamentDkkPerKg = filPrice, ElectricityDkkPerKwh = elPrice, Filaments = lines
            };
            last = Pricing.Calculate(input, settings);
            lastHours = hours; lastMinutes = minutes;

            SetStatus("", false);
            lblMaterial.Text = Num.Kr(last.MaterialCost);
            if (lines != null && last.FilamentCosts != null)
                for (int i = 0; i < colourRows; i++)
                    rowCost[i].Text = Num.Kr(rowItems[i].Sum(k => last.FilamentCosts[k]));
            lblPower.Text = Num.Kr(last.PowerCost) + "  (" + last.PowerKwh.ToString("N2", Num.Dk) + " kWh)";
            lblFailed.Text = Num.Kr(last.FailedPrintCost);
            lblBase.Text = Num.Kr(last.BaseCost);
            lblFriends.Text = Num.Kr(last.FriendsPrice);
            lblOthers.Text = Num.Kr(last.OthersPrice);
            btnCopy.Enabled = true;
        }

        void RefreshSettingsLabels()
        {
            lblPrinter.Text = "Bambu Lab A1 · " + Num.Fmt(settings.PrinterWatts) + L.S(" W  (change in Settings)", " W  (ret i Indstillinger)");
            string wear = settings.MachineRatePerHour > 0 ? L.F(", {0} kr./h wear", ", {0} kr./t slid", Num.Fmt(settings.MachineRatePerHour)) : "";
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

        /// Cost breakdown height: 3 rows, + the failed-prints row, + the colour rows and purge line instead of
        /// the Material row (2+ colours). The window follows (GrowAndShrink).
        void UpdateBreakHeight()
        {
            if (breakBaseHeight == 0) return;
            int h = breakBaseHeight + (settings.FailedPrintPct > 0 ? breakRowHeight : 0);
            if (filaments != null)
            {
                h += colourGrid.Height + colourGrid.Margin.Vertical - breakRowHeight;
                if (lblPurge.Text.Length > 0) h += lblPurge.PreferredHeight + lblPurge.Margin.Vertical;
            }
            grpBreak.Height = h;
        }

        void OpenSettings()
        {
            using (var dlg = new SettingsForm(settings))
            {
                if (!offline) dlg.UpdateFound = ShowUpdateButton;   // "check now" in Settings
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var old = settings;
                    settings = dlg.Result;
                    if (old.AutoUpdateCheck == 0 && settings.AutoUpdateCheck != 0 && pendingUpdate == null)
                        BeginInvoke((MethodInvoker)StartupUpdateCheck);   // switched back on: check now
                    SyncUpdateTimer();   // the 5-minute check follows the setting
                    bool powerChanged = old.PriceArea != settings.PriceArea || old.PowerAddOn != settings.PowerAddOn ||
                                        old.PowerIncludeVat != settings.PowerIncludeVat ||
                                        (old.AutoPowerPrice == 0 && settings.AutoPowerPrice != 0);
                    if (powerChanged && settings.AutoPowerPrice != 0) BeginInvoke((MethodInvoker)FetchPowerPrice);
                    SaveLastPrices();
                    RefreshSettingsLabels();
                    Recalculate();
                    if (cts == null) ShowAutoIdle();
                    if (dlg.RestartRequested && !offline)
                    {
                        // new language: settings are already saved above; Restart closes this window (saves again) and starts anew
                        if (cts != null) cts.Cancel();
                        BeginInvoke((MethodInvoker)Application.Restart);
                    }
                }
            }
        }

        // ---------- updates from GitHub ----------
        /// Removes leftovers of a previous update (local only), then, if the setting allows it,
        /// quietly looks for a new release. A newer version shows the red update button under the Friends & family box.
        async void StartupUpdateCheck()
        {
            if (offline) return;
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

        /// The 5-minute timer runs only while "Check for a new version at startup" is on.
        void SyncUpdateTimer()
        {
            if (updateTimer == null) return;
            updateTimer.Enabled = settings.AutoUpdateCheck != 0;
        }

        /// Every 5 minutes while running: a quiet check (ETag cache, never throws). Skipped while the setting is off,
        /// the update dialog is open (download/install), a slice job runs, the last check is still busy, or GitHub
        /// asked us to back off. Only ever shows (or raises) the red button, never hides it.
        async void PeriodicUpdateCheck()
        {
            if (offline || IsDisposed || settings.AutoUpdateCheck == 0 || updateDialogOpen || cts != null || checkingUpdate) return;
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
            double v;
            if (Num.TryParse(txtFilamentPrice.Text, out v)) settings.LastFilamentPrice = v;
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
                string msg = L.S("Quote copied to clipboard.", "Tilbuddet er kopieret til udklipsholderen.");
                SetStatus(msg, true);
                var t = new System.Windows.Forms.Timer { Interval = 2500 };
                t.Tick += delegate { t.Stop(); t.Dispose(); if (lblStatus.Text == msg) SetStatus("", false); };
                t.Start();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, L.S("Clipboard", "Udklipsholder")); }
        }

        void ClearJob()
        {
            txtUrl.Text = "";
            SetUrlHint(false);
            SetColours(null);   // back to the single-filament layout
            txtGrams.Text = "";
            txtHours.Text = "0";
            txtMinutes.Text = "0";
            ResetModelPicture();   // back to the logo
            txtUrl.Focus();
        }
    }
}
