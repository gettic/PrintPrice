using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintPrice
{
    public class SettingsForm : CaptionForm
    {
        public AppSettings Result;
        readonly AppSettings source;
        readonly string exe;
        TextBox txtWatts, txtFriends, txtOthers, txtRate, txtMin, txtRound, txtFailed, txtPayback, txtAddOn, txtSlicer;
        TextBox txtQuoteName, txtQuoteDays;   // quote export: name on the quote, valid for N days
        TextBox txtDueDays, txtMobilePay, txtBank, txtPrinterName;   // invoice: pay within N days, ways to pay; printer name
        CheckBox chkAutoPower, chkVat, chkAutoWear, chkAutoUpdate, chkPayback;
        ComboBox cmbPaybackWho;   // 0 = customers only (FailedPaybackWho 2), 1 = both prices (1)
        Label lblCheck, lnkCheck;   // "v1.1.0 –" / check result, and the clickable "check now"
        /// Called when "check now" finds a newer version (MainForm shows its red update button).
        public Action<UpdateInfo> UpdateFound;
        ComboBox cmbNozzle, cmbProcess, cmbFilament, cmbBed, cmbArea, cmbLang;
        Label lnkRestart;   // "Restart now" under the language (only when the choice differs from the running language)
        /// True when the dialog was closed with "Restart now" (MainForm saves and restarts the program).
        public bool RestartRequested;
        InfoBox lblWear;
        Button btnBackupTo, btnRestoreFrom;   // App tab: the Backup window (idea 27)
        /// Back up… / Restore…: (owner, BackupForm tab) opens the Backup window (MainForm.OpenBackup); true = a restore
        /// saved new settings, so this window closes without saving.
        public Func<IWin32Window, int, bool> BackupRequested;

        // several printers: the Printer tab edits one printer of `work` (a copy of the settings) at a time
        AppSettings work;
        string shownId;          // the printer the Printer tab shows
        ComboBox cmbPrinter;
        Button btnAddPrinter, btnRemovePrinter;
        Label lblWattsUnit;
        ContextMenuStrip addMenu;
        bool useAdded;        // opened from Choose printer's "Add printer…": the printer added is put in use
        string printerNote;   // a line under the wear table after Remove / a Remove that can't be done (null = none)

        sealed class PrinterItem
        {
            public string Id, Name;
            public bool InUse;   // the printer quotes use now: "(in use)" after its name
            public override string ToString() { return InUse ? Name + "  " + L.S("(in use)", "(i brug)") : Name; }
        }

        /// "" = automatic (Windows language), "en", "da".
        string SelectedLanguage { get { return cmbLang.SelectedIndex == 1 ? "en" : cmbLang.SelectedIndex == 2 ? "da" : ""; } }
        bool filling;   // suppress change events while filling the form

        // ---------- tabs ----------
        /// Tab numbers for the constructor / SelectedTab (e.g. MainForm's printer card -> TabPrinter).
        public const int TabPrices = 0, TabPrinter = 1, TabElectricity = 2, TabQuote = 3, TabApp = 4;
        static int lastTab;   // the tab Settings showed last, for this run only (not saved)
        readonly Panel root;   // the one content control under the title strip
        readonly TabStrip strip;
        readonly TableLayoutPanel[] pages = new TableLayoutPanel[5];
        readonly Panel separator;   // 1 px Border line above the bottom row
        readonly FlowLayoutPanel checkFlow;   // "v1.7.0 –" + "check now", bottom left
        readonly Button ok, cancel, reset;
        int selected = -1;
        bool fitted;   // Load has sized the window

        // layout (96 dpi units)
        // 560 wide (was 520): room for the Printer row (drop-down with "Bambu Lab A1 mini" + Add… + Remove)
        const int WindowW = 560, MinWindowH = 534, Edge = 14, StripTop = 6, StripH = 28, PageTop = 44,
                  PageGap = 16, SepGap = 9, ButtonH = 32, ButtonMinW = 75, NumberBoxW = 70, UnitGap = 6;

        public SettingsForm(AppSettings current) : this(current, 0, -1) { }

        /// failedDebt = History.FailedDebt(history, null): shown as "Not made back yet: x kr." (also while payback is off).
        public SettingsForm(AppSettings current, double failedDebt) : this(current, failedDebt, -1) { }

        /// tab = TabPrices … TabApp to open on that tab; -1 = the tab shown last in this run (Prices in picture mode).
        public SettingsForm(AppSettings current, double failedDebt, int tab)
        {
            source = current;
            exe = BambuSlicer.FindExe(current.SlicerPath);
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = L.S("Settings", "Indstillinger");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            if (Program.Hidden) Screenshots.Place(this);   // CenterParent would clamp an off-screen dialog back onto the screen
            ClientSize = new Size(WindowW - 2, MinWindowH);   // real size set on Load (measured)

            root = new Panel { Margin = Padding.Empty };
            Controls.Add(root);
            strip = new TabStrip(new[]
            {
                L.S("Prices", "Priser"), "Printer", L.S("Electricity", "Elpris"),
                L.S("Quote & invoice", "Tilbud og faktura"), L.S("App", "Program")
            }) { TabIndex = 0 };
            root.Controls.Add(strip);
            for (int i = 0; i < pages.Length; i++)
            {
                // all pages visible until Load (their controls get handles, combos can be measured), then only the chosen one
                var p = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty, TabIndex = 1 + i };
                p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));   // caption column: same on every tab (Load, measured)
                p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                p.SuspendLayout();
                pages[i] = p;
                root.Controls.Add(p);
            }

            // ----- Prices -----
            var t = pages[TabPrices];
            Section(t, L.S("Markup", "Avance"));
            txtFriends = Row(t, L.S("Friends && family markup", "Avance, venner og familie"), "%");
            txtOthers = Row(t, L.S("Customer markup", "Avance, kunder"), "%");

            Section(t, L.S("Price rules", "Prisregler"));
            txtMin = Row(t, L.S("Minimum price (customers)", "Minimumspris (kunder)"), L.S("kr. (0 = off)", "kr. (0 = fra)"));
            txtRound = Row(t, L.S("Round up to nearest", "Rund op til nærmeste"), L.S("kr. (0 = off)", "kr. (0 = fra)"));

            Section(t, L.S("Failed prints", "Fejlprint"));
            txtFailed = Row(t, L.S("Allowance", "Tillæg"), L.S("% (0 = off)", "% (0 = fra)"));   // not "Failed prints" twice (heading)
            // the hint's grey second line = failed prints marked in the price history that new quotes haven't made back yet
            Hint(t, "").SetText(L.S("Added to material + power for prints that fail.", "Lægges oven i materiale + strøm for print, der fejler."),
                                L.F("Not made back yet: {0} kr.", "Ikke tjent ind endnu: {0} kr.", failedDebt.ToString("N2", Num.Dk)));
            // earn back failed prints: the checkbox is the caption of its % row; % and "paid by" only work while ticked
            int pr = t.RowCount++;
            chkPayback = new CheckBox { Text = L.S("Earn back failed prints", "Tjen fejlprint ind igen"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, Theme.LabelGap, 5) };   // row 31 like the box rows (6 gave 33)
            t.Controls.Add(chkPayback, 0, pr);
            txtPayback = NumberBox();
            t.Controls.Add(txtPayback, 1, pr);
            t.Controls.Add(Unit(L.S("% per quote", "% pr. tilbud")), 2, pr);
            cmbPaybackWho = Combo(t, L.S("Paid by", "Betales af"));
            cmbPaybackWho.Items.Add(L.S("Customers only", "Kun kunder"));
            cmbPaybackWho.Items.Add(L.S("Both prices", "Begge priser"));
            chkPayback.CheckedChanged += delegate { txtPayback.Enabled = cmbPaybackWho.Enabled = chkPayback.Checked; };
            Hint(t, L.S("Adds this % of material + power to new quotes until failed prints are made back.",
                        "Lægger denne % af materiale + strøm på nye tilbud, til fejlprint er tjent ind."));

            // ----- Printer -----
            t = pages[TabPrinter];
            // which printer the tab shows (no heading: this row heads the tab) + Add… (menu of models) + Remove
            int pk = t.RowCount++;
            t.Controls.Add(Caption("Printer"), 0, pk);
            var pick = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty,
                                              Padding = Padding.Empty, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cmbPrinter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                                        Margin = new Padding(0, 3, 0, 3), MaxDropDownItems = 20, AccessibleName = "Printer" };
            btnAddPrinter = new Button { Text = L.S("Add…", "Tilføj…"), Size = new Size(64, 25), Padding = Padding.Empty, Margin = new Padding(6, 3, 0, 3),
                                         Anchor = AnchorStyles.Top | AnchorStyles.Left, UseMnemonic = false, Font = new Font("Segoe UI", 9f) };   // 9 pt like btnPower: fits a box-high button (descenders)
            btnRemovePrinter = new Button { Text = L.S("Remove", "Fjern"), Size = new Size(64, 25), Padding = Padding.Empty, Margin = new Padding(6, 3, 0, 3),
                                            Anchor = AnchorStyles.Top | AnchorStyles.Left, UseMnemonic = false, Font = new Font("Segoe UI", 9f) };   // 9 pt like btnPower: fits a box-high button (descenders)
            pick.Controls.Add(cmbPrinter, 0, 0);
            pick.Controls.Add(btnAddPrinter, 1, 0);
            pick.Controls.Add(btnRemovePrinter, 2, 0);
            t.Controls.Add(pick, 1, pk);
            t.SetColumnSpan(pick, 2);
            // the printer's name; empty = its model's name (grey placeholder)
            txtPrinterName = TextRow(t, L.S("Name", "Navn"), AppSettings.DefaultPrinterName, 40);
            cmbNozzle = Combo(t, L.S("Nozzle", "Dyse"));
            foreach (var n in Wear.Nozzles) cmbNozzle.Items.Add(NozzleLabel(n));
            cmbProcess = Combo(t, L.S("Print profile", "Printprofil"));
            cmbFilament = Combo(t, L.S("Filament profile", "Filamentprofil"));
            cmbBed = Combo(t, L.S("Build plate", "Byggeplade"));
            foreach (var p in Wear.Plates) cmbBed.Items.Add(L.Danish && p.Label == "Smooth PEI / High Temp Plate" ? "Glat PEI / High Temp" : p.Label);   // short: fits the combo ("Byggeplade" already says plate)
            txtWatts = Row(t, L.S("Power (avg.)", "Effekt (gns.)"), "W (A1: ~100)");
            lblWattsUnit = (Label)t.GetControlFromPosition(2, t.RowCount - 1);   // the note follows the printer's model

            Section(t, L.S("Wear && tear  (both prices)", "Slid  (begge priser)"));
            txtRate = Row(t, L.S("Machine-time rate", "Maskintimepris"), L.S("kr/h", "kr/t"));
            chkAutoWear = Check(t, L.S("Calculate from the printer above", "Beregn ud fra printeren ovenfor"));
            lblWear = Hint(t, "");

            // ----- Electricity -----
            t = pages[TabElectricity];
            Section(t, L.S("Electricity price  (live from Energinet)", "Elpris  (live fra Energinet)"));
            cmbArea = Combo(t, L.S("Price area", "Prisområde"));
            cmbArea.Items.Add(L.S("DK1 – Jutland & Funen", "DK1 – Jylland og Fyn"));   // owner-drawn items use NoPrefix: one "&"
            cmbArea.Items.Add(L.S("DK2 – Zealand, Lolland & Bornholm", "DK2 – Sjælland, Lolland og Bornholm"));
            txtAddOn = Row(t, L.S("Tariffs && fees", "Tariffer og afgifter"), "kr/kWh");
            Hint(t, L.S("Added to the spot price, excl. VAT: grid tariff, Energinet (~0,12), elafgift (0,008 in 2026) " +
                        "and your supplier's fee. See your bill.",
                        "Lægges oven i spotprisen, ekskl. moms: nettarif, Energinet (~0,12), elafgift (0,008 i 2026) " +
                        "og dit elselskabs gebyr. Se din elregning."));
            chkVat = Check(t, L.S("Add 25 % VAT", "Læg 25 % moms til"));
            chkAutoPower = Check(t, L.S("Fetch the live price when the program starts", "Hent den aktuelle pris, når programmet starter"));
            // power use (W) and the printer's name are per printer: on the Printer tab

            // ----- Quote & invoice -----
            t = pages[TabQuote];
            Section(t, L.S("Quote", "Tilbud"));
            txtQuoteName = TextRow(t, L.S("Name on the quote", "Navn på tilbuddet"), null, 40);
            txtQuoteDays = Row(t, L.S("Valid for", "Gyldigt i"), L.S("days", "dage"));   // valid until = quote date + days
            Section(t, L.S("Invoice", "Faktura"));
            txtDueDays = Row(t, L.S("Pay within", "Betalingsfrist"), L.S("days", "dage"));
            txtMobilePay = TextRow(t, L.S("MobilePay number", "MobilePay-nummer"), L.S("optional", "valgfrit"), 40);
            txtBank = TextRow(t, L.S("Bank account", "Bankkonto"), L.S("optional (reg. no. + account no.)", "valgfrit (reg.nr. + kontonr.)"), 60);
            Hint(t, L.S("MobilePay and bank account are shown on invoices only when filled in.",
                        "MobilePay og bankkonto vises kun på fakturaer, når de er udfyldt."));

            // ----- App -----
            t = pages[TabApp];
            Section(t, "Sprog / Language");   // in both languages so it can be found whichever one is showing
            cmbLang = Combo(t, L.S("Language", "Sprog"));
            cmbLang.Items.Add(L.S("Automatic (Windows)", "Automatisk (Windows)"));
            cmbLang.Items.Add("English");
            cmbLang.Items.Add("Dansk");
            // "Restart now" (under the drop-down) only shows when the choice differs from the language that is showing now
            lnkRestart = new Theme.LinkText { Text = L.S("Restart now", "Genstart nu"), AutoSize = true, Visible = false, Margin = new Padding(0, 0, 0, 4) };
            lnkRestart.Font = new Font(Font, FontStyle.Underline);
            lnkRestart.Click += delegate { RestartRequested = true; Accept(); };
            int lr = t.RowCount++;
            t.Controls.Add(lnkRestart, 1, lr);
            t.SetColumnSpan(lnkRestart, 2);
            cmbLang.SelectedIndexChanged += delegate { lnkRestart.Visible = AppSettings.IsDanish(SelectedLanguage) != L.Danish; };
            Section(t, L.S("Updates", "Opdateringer"));
            chkAutoUpdate = Check(t, L.S("Check for new versions automatically", "Søg automatisk efter nye versioner"));
            // App tab headings: the same visible gap (14 px at 100 %, measured) above each, whatever is above it
            // (a box row, the check box with its 4 px margin, a hint box)
            Section(t, "Bambu Studio").Margin = new Padding(0, 2, 0, 4);
            txtSlicer = TextRow(t, L.S("Bambu Studio path", "Sti til Bambu Studio"), null, 32767);
            Hint(t, exe == null
                ? L.S("Bambu Studio not found. Install it, or enter the path to bambu-studio.exe.",
                      "Bambu Studio blev ikke fundet. Installér det, eller angiv stien til bambu-studio.exe.")
                : L.F("Found: {0}.\nLeave empty to find it automatically.", "Fundet: {0}.\nLad feltet være tomt for at finde det automatisk.", exe));
            // backup (idea 27): the Backup window on its Back up / Restore tab; a restore closes Settings (its fields are out of date)
            Section(t, L.S("Backup", "Sikkerhedskopi")).Margin = new Padding(0, 5, 0, 4);
            int br = t.RowCount++;
            t.Controls.Add(Caption(L.S("Price history", "Prishistorik")), 0, br);
            var backupFlow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            btnBackupTo = new Button { Text = L.S("Back up…", "Tag kopi…"), Size = new Size(80, 25), Padding = Padding.Empty, Margin = new Padding(0, 3, 0, 3),
                                       UseMnemonic = false, Font = new Font("Segoe UI", 9f) };   // 9 pt like Add… / Remove (box-high button)
            btnRestoreFrom = new Button { Text = L.S("Restore…", "Gendan…"), Size = new Size(80, 25), Padding = Padding.Empty, Margin = new Padding(6, 3, 0, 3),
                                          UseMnemonic = false, Font = new Font("Segoe UI", 9f) };
            btnBackupTo.Click += delegate { OpenBackup(BackupForm.TabBackup); };
            btnRestoreFrom.Click += delegate { OpenBackup(BackupForm.TabRestore); };
            backupFlow.Controls.Add(btnBackupTo);
            backupFlow.Controls.Add(btnRestoreFrom);
            t.Controls.Add(backupFlow, 1, br);
            t.SetColumnSpan(backupFlow, 2);

            // ----- below the tabs: line, version + "check now" (left), Defaults / OK / Cancel (right) -----
            separator = new Panel { Height = 1 };
            root.Controls.Add(separator);
            checkFlow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, TabIndex = 6 };
            lblCheck = new Theme.PlainLabel { AutoSize = true, Tag = "muted", Margin = Padding.Empty };
            lnkCheck = new Theme.LinkText { AutoSize = true, Margin = Padding.Empty };
            lnkCheck.Font = new Font(Font, FontStyle.Underline);
            lnkCheck.Click += delegate { CheckNow(); };
            checkFlow.Controls.Add(lblCheck);
            checkFlow.Controls.Add(lnkCheck);
            root.Controls.Add(checkFlow);
            if (AppSettings.TestMode)   // test mode never looks for updates: muted text, no link
                SetCheckText("v" + Updater.CurrentText + L.S(" – check off in test mode", " – tjek slået fra i testtilstand"), null, null);
            else
                SetCheckText("v" + Updater.CurrentText + " –", L.S("check now", "tjek nu"), null);
            reset = new Button { Text = L.S("Defaults", "Standard"), TabIndex = 7 };
            ok = new Button { Text = "OK", TabIndex = 8 };
            cancel = new Button { Text = L.S("Cancel", "Annullér"), DialogResult = DialogResult.Cancel, TabIndex = 9 };
            ok.Click += delegate { Accept(); };
            reset.Click += delegate { Defaults(); };   // every tab (the shown printer: its model's defaults)
            root.Controls.Add(reset);
            root.Controls.Add(ok);
            root.Controls.Add(cancel);

            // live updates
            cmbNozzle.SelectedIndexChanged += delegate { if (!filling) { RefillPresets(); UpdateWear(); } };
            cmbFilament.SelectedIndexChanged += delegate { if (!filling) UpdateWear(); };
            cmbBed.SelectedIndexChanged += delegate { if (!filling) UpdateWear(); };
            chkAutoWear.CheckedChanged += delegate { if (!filling) UpdateWear(); };
            cmbPrinter.SelectedIndexChanged += delegate { if (!filling) PrinterChosen(); };
            txtPrinterName.TextChanged += delegate { if (!filling) NameTyped(); };
            btnAddPrinter.Click += delegate { addMenu.Show(btnAddPrinter, new Point(0, btnAddPrinter.Height)); };
            btnRemovePrinter.Click += delegate { RemoveShownPrinter(); };
            BuildAddMenu();
            // before Load only remember it: every page stays visible until Load has measured them all
            strip.SelectedChanged += delegate { if (fitted) SelectTab(strip.Selected); else selected = strip.Selected; };

            AcceptButton = ok;
            CancelButton = cancel;
            Theme.Apply(this);
            separator.BackColor = Theme.Border;   // after Apply, which paints panels in the background colour
            Fill(current);   // while layout is paused: the texts and lists don't each trigger a layout pass
            foreach (var p in pages) p.ResumeLayout(true);
            ResumeLayout(true);

            if (tab < 0 || tab >= pages.Length) tab = Screenshots.Active ? TabPrices : lastTab;
            selected = tab;
            strip.Selected = tab;
            if (current.AutoUpdateCheck != 0 && !AppSettings.TestMode && !Screenshots.Active)
                Shown += delegate { QuietCheckOnOpen(); };   // auto update on: look when Settings opens
            // in Load: a drop-down's real height (ItemHeight + frame) is only known once its window exists
            Load += delegate { FitLayout(); };
        }

        /// The tab that shows (TabPrices … TabApp). Set it to switch tabs (also before the window is shown).
        public int SelectedTab
        {
            get { return selected; }
            set { if (value >= 0 && value < pages.Length) { if (fitted) SelectTab(value); else { selected = value; strip.Selected = value; } } }   // before Load: shown by FitLayout
        }

        int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

        /// Load: window 560 wide, the caption column measured over all tabs, every page as wide as the content, drop-downs as
        /// tall as the boxes, the height from the tallest tab (at least 534), then only the chosen tab stays visible.
        void FitLayout()
        {
            Width = Px(WindowW);
            int cw = ClientSize.Width - 2 * Px(Edge);
            // one caption column for every tab: the longest caption (or the payback checkbox) + its right margin (LabelGap)
            int col = 0;
            foreach (var p in pages)
                foreach (Control c in p.Controls)
                    if (p.GetColumn(c) == 0 && p.GetColumnSpan(c) == 1)
                        col = Math.Max(col, c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal);
            foreach (var p in pages)
            {
                p.SuspendLayout();
                p.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, col);
                p.MinimumSize = new Size(cw, 0);
                p.MaximumSize = new Size(cw, 0);
                foreach (Control c in p.Controls)
                {
                    var info = c as InfoBox;
                    if (info != null) info.SetWidth(cw - info.Margin.Horizontal);
                }
                p.ResumeLayout(true);
                p.SizeChanged += delegate { if (fitted) FitHeight(); };   // e.g. wear warnings: grow, never cut off
            }
            FitCombos(this);
            int bw = Px(ButtonMinW);
            foreach (var b in new[] { reset, ok, cancel })
                bw = Math.Max(bw, TextRenderer.MeasureText(b.Text, b.Font).Width + Px(20));
            foreach (var b in new[] { reset, ok, cancel }) b.Size = new Size(bw, Px(ButtonH));
            // Add… / Remove: box height, each its text + 24 px (flat buttons draw with inner padding), so the printer
            // drop-down keeps room for "Bambu Lab A1 mini"; its open list is as wide as the longest name
            foreach (var b in new[] { btnAddPrinter, btnRemovePrinter, btnBackupTo, btnRestoreFrom })
                b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + Px(24), txtWatts.Height);
            FitHeight();
            FitPrinterList();
            fitted = true;
            SelectTab(selected);
        }

        /// Window height = the tallest tab + strip + bottom row (at least 534 at 100 %), the same on every tab; only grows.
        void FitHeight()
        {
            int cw = ClientSize.Width - 2 * Px(Edge), tallest = 0;
            foreach (var p in pages) tallest = Math.Max(tallest, p.GetPreferredSize(new Size(cw, 0)).Height);
            int need = CaptionHeight + Px(PageTop) + tallest + Px(PageGap) + 1 + Px(SepGap) + Px(ButtonH) + Px(Edge);
            int h = Math.Max(Px(MinWindowH), SizeFromClientSize(new Size(ClientSize.Width, need)).Height);
            if (!fitted || h > Height) Height = h;
            PlaceAll();
        }

        void PlaceAll()
        {
            int m = Px(Edge), w = ClientSize.Width, cw = w - 2 * m;
            root.SetBounds(0, CaptionHeight, w, ClientSize.Height - CaptionHeight);
            strip.SetBounds(m, Px(StripTop), cw, Px(StripH));
            foreach (var p in pages) p.Location = new Point(m, Px(PageTop));
            int by = root.Height - m - cancel.Height, x = w - m;
            foreach (var b in new[] { cancel, ok, reset }) { x -= b.Width; b.Location = new Point(x, by); x -= Px(6); }
            separator.SetBounds(m, by - Px(SepGap) - 1, cw, 1);
            checkFlow.MaximumSize = new Size(Math.Max(1, reset.Left - Px(8) - m), 0);
            checkFlow.Location = new Point(m, by + (cancel.Height - checkFlow.Height) / 2);
        }

        /// Back up… / Restore… on the App tab. After a restore the settings on disk are new: close without saving.
        void OpenBackup(int backupTab)
        {
            if (BackupRequested == null || !BackupRequested(this, backupTab)) return;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// Shows tab i; focus inside the tab that hides moves to the tab strip.
        void SelectTab(int i)
        {
            if (i < 0 || i >= pages.Length) return;
            bool focusLost = false;
            for (int k = 0; k < pages.Length; k++)
                if (k != i && pages[k].ContainsFocus) focusLost = true;
            pages[i].Visible = true;
            for (int k = 0; k < pages.Length; k++)
                if (k != i) pages[k].Visible = false;
            selected = i;
            lastTab = i;
            if (strip.Selected != i) strip.Selected = i;
            if (focusLost) strip.Focus();
        }

        /// Ctrl+Tab / Ctrl+PgDn = next tab, Ctrl+Shift+Tab / Ctrl+PgUp = previous (wrapping), from anywhere in the window.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            int step = 0;
            if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.PageDown)) step = 1;
            else if (keyData == (Keys.Control | Keys.Shift | Keys.Tab) || keyData == (Keys.Control | Keys.PageUp)) step = -1;
            if (step != 0)
            {
                SelectTab((selected + step + pages.Length) % pages.Length);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// Every drop-down exactly as tall as the number boxes (owner-drawn: height = ItemHeight + its frame, measured),
        /// so its text sits on the captions' line like the boxes' digits.
        void FitCombos(Control c)
        {
            foreach (Control child in c.Controls)
            {
                var cb = child as ComboBox;
                if (cb != null && cb.DrawMode == DrawMode.OwnerDrawFixed)
                {
                    int frame = cb.Height - cb.ItemHeight;
                    if (txtWatts.Height - frame > 0 && cb.ItemHeight != txtWatts.Height - frame) cb.ItemHeight = txtWatts.Height - frame;
                }
                FitCombos(child);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override bool ShowWithoutActivation { get { return Screenshots.Active || Program.Hidden; } }

        // ---------- layout helpers ----------
        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, Theme.LabelGap, 6) };
        }

        /// Number box: 70 wide, right-aligned, selects its text when tabbed or clicked into.
        static TextBox NumberBox()
        {
            var box = new TextBox { Width = NumberBoxW, TextAlign = HorizontalAlignment.Right, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 3) };
            box.Enter += delegate { box.BeginInvoke((MethodInvoker)delegate { box.SelectAll(); }); };
            return box;
        }

        /// Unit / note right of a number box: its text starts 6 px after the box's frame (UnitLabel draws without padding).
        static Label Unit(string text)
        {
            return new UnitLabel { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Tag = "muted", Margin = new Padding(UnitGap, 0, 0, 0) };
        }

        TextBox Row(TableLayoutPanel t, string caption, string unit)
        {
            int r = t.RowCount++;
            t.Controls.Add(Caption(caption), 0, r);
            var box = NumberBox();
            t.Controls.Add(box, 1, r);
            t.Controls.Add(Unit(unit), 2, r);
            return box;
        }

        /// Text box from the caption column to the right edge; cue = grey placeholder while empty (null = none).
        TextBox TextRow(TableLayoutPanel t, string caption, string cue, int maxLength)
        {
            int r = t.RowCount++;
            t.Controls.Add(Caption(caption), 0, r);
            var box = new CueBox
            {
                Cue = cue, Width = 120, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 0, 3),
                TextAlign = HorizontalAlignment.Left, MaxLength = maxLength, AccessibleDescription = cue
            };
            t.Controls.Add(box, 1, r);
            t.SetColumnSpan(box, 2);
            return box;
        }

        static ComboBox Combo(TableLayoutPanel t, string caption)
        {
            int r = t.RowCount++;
            t.Controls.Add(Caption(caption), 0, r);
            // stretches to the right edge (Width is only the minimum)
            // Top-anchored like a centred number box (row 31 = box 25 + 3 + 3): the table centres a combo by a height
            // smaller than the one it really gets, which put it 2 px low; its height = a box's (FitCombos, on Load)
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 0, 3), MaxDropDownItems = 20 };
            t.Controls.Add(cb, 1, r);
            t.SetColumnSpan(cb, 2);
            return cb;
        }

        static CheckBox Check(TableLayoutPanel t, string text)
        {
            var cb = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
            int r = t.RowCount++;
            t.Controls.Add(cb, 0, r);
            t.SetColumnSpan(cb, 3);
            return cb;
        }

        /// Help text in its own shaded box under the setting it explains (full width, set on Load).
        static InfoBox Hint(TableLayoutPanel t, string text)
        {
            int h = t.RowCount++;
            var box = new InfoBox(380, 9f) { Margin = new Padding(0, 2, 0, 4), Text = text };
            t.Controls.Add(box, 0, h);
            t.SetColumnSpan(box, 3);
            return box;
        }

        Label Section(TableLayoutPanel t, string text)
        {
            int r = t.RowCount++;
            var lbl = new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 6, 0, 4) };
            t.Controls.Add(lbl, 0, r);
            t.SetColumnSpan(lbl, 3);
            return lbl;
        }

        /// Unit label drawn without GDI's side padding, so "6 px after the box" is 6 px to the text.
        sealed class UnitLabel : Label
        {
            const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                                          TextFormatFlags.Left | TextFormatFlags.Top;

            public override Size GetPreferredSize(Size proposedSize)
            {
                var s = TextRenderer.MeasureText(Text, Font, Size.Empty, Flags);
                return new Size(s.Width + Padding.Horizontal, base.GetPreferredSize(proposedSize).Height);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var r = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, ClientSize.Width - Padding.Horizontal), Math.Max(1, ClientSize.Height - Padding.Vertical));
                TextRenderer.DrawText(e.Graphics, Text, Font, r, Enabled ? ForeColor : Theme.Muted, Flags);
            }
        }

        /// Text box that paints grey placeholder text (Theme.Muted) on its text line while empty, where typed text starts.
        internal sealed class CueBox : TextBox
        {
            public string Cue;   // null = none

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == 0x0007 || m.Msg == 0x0008) { Invalidate(); return; }   // WM_SETFOCUS / WM_KILLFOCUS
                if (m.Msg != 0x000F || Cue == null || Text.Length > 0) return;      // WM_PAINT
                int margin = (int)Math.Round(4 * DeviceDpi / 96f), top = Theme.OneLineTop(this);
                // x = the edit rect's left (Theme.ApplyLineRect: 1 + 4 px), y = the text line's top: same place as typed text
                using (var g = Graphics.FromHwnd(Handle))
                    TextRenderer.DrawText(g, Cue, Font, new Rectangle(1 + margin, top, Math.Max(1, ClientSize.Width - 2 - 2 * margin), Math.Max(1, ClientSize.Height - top - 1)),
                        Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                // the empty box's paint brings back Windows' light frame: draw the theme's 1 px Border frame over it again
                IntPtr dc = GetWindowDC(Handle);
                if (dc == IntPtr.Zero) return;
                try
                {
                    using (var g = Graphics.FromHdc(dc))
                    using (var pen = new Pen(Theme.Border))
                        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
                finally { ReleaseDC(Handle, dc); }
            }

            [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hwnd);
            [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

            protected override void OnTextChanged(EventArgs e)
            {
                base.OnTextChanged(e);
                Invalidate();   // placeholder appears / disappears with the first / last character
            }
        }

        /// Hand-drawn tab strip (no stock TabControl): other tabs Muted (Text on hover), the chosen one bold Text with a
        /// 2 px InfoAccent underline. A Tab stop: Left/Right/Home/End switch tabs, focus rectangle round the chosen tab's text.
        internal sealed class TabStrip : Control
        {
            readonly string[] names;
            Rectangle[] cells;
            int sel, hover = -1;
            Font bold;
            public event EventHandler SelectedChanged;

            public TabStrip(string[] names)
            {
                this.names = names;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                AccessibleRole = AccessibleRole.PageTabList;
                AccessibleName = names[0];
            }

            int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

            public int Selected
            {
                get { return sel; }
                set
                {
                    if (value < 0 || value >= names.Length || value == sel) return;
                    sel = value;
                    AccessibleName = names[sel];
                    Invalidate();
                    if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
                }
            }

            const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                                          TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

            Font Bold
            {
                get
                {
                    if (bold == null || bold.Size != Font.Size) { if (bold != null) bold.Dispose(); bold = new Font(Font, FontStyle.Bold); }
                    return bold;
                }
            }

            /// Each tab = its bold text (so nothing moves when it is chosen) + the same padding on each side; the padding is
            /// the strip's free width shared evenly (at most 20 px a side), so the tabs touch and fill the strip: the gaps
            /// between the texts are all equal and the last tab ends on the content's right edge (within rounding, < 10 px).
            void Measure()
            {
                cells = new Rectangle[names.Length];
                var widths = new int[names.Length];
                int sum = 0;
                for (int i = 0; i < names.Length; i++)
                {
                    widths[i] = TextRenderer.MeasureText(names[i], Bold, Size.Empty, Flags).Width;
                    sum += widths[i];
                }
                int pad = Math.Max(Px(4), Math.Min(Px(20), (Width - sum) / (2 * names.Length))), x = 0;
                for (int i = 0; i < names.Length; i++)
                {
                    int w = widths[i] + 2 * pad;
                    cells[i] = new Rectangle(x, 0, w, Height);
                    x += w;
                }
            }

            protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); cells = null; }
            protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); cells = null; Invalidate(); }

            int HitTest(Point p)
            {
                if (cells == null) Measure();
                for (int i = 0; i < cells.Length; i++) if (cells[i].Contains(p)) return i;
                return -1;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (cells == null) Measure();
                var g = e.Graphics;
                g.Clear(Theme.Back);
                int line = Px(2);
                for (int i = 0; i < names.Length; i++)
                {
                    var text = new Rectangle(cells[i].X, 0, cells[i].Width, Height - line - Px(2));
                    bool chosen = i == sel;
                    TextRenderer.DrawText(g, names[i], chosen ? Bold : Font, text, chosen || i == hover ? Theme.Text : Theme.Muted, Flags);
                    if (chosen)
                    {
                        using (var b = new SolidBrush(Theme.InfoAccent)) g.FillRectangle(b, cells[i].X, Height - line, cells[i].Width, line);
                        if (Focused && ShowFocusCues)
                            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(text, -Px(6), -Px(2)), Theme.Text, Theme.Back);
                    }
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = HitTest(e.Location);
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                if (h != hover) { hover = h; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover != -1) { hover = -1; Invalidate(); } }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                int h = HitTest(e.Location);
                if (h >= 0) { Focus(); Selected = h; }
            }

            protected override bool IsInputKey(Keys keyData)
            {
                return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                int to = e.KeyCode == Keys.Left ? sel - 1 : e.KeyCode == Keys.Right ? sel + 1
                       : e.KeyCode == Keys.Home ? 0 : e.KeyCode == Keys.End ? names.Length - 1 : -2;
                if (to != -2)
                {
                    e.Handled = true;
                    if (to >= 0 && to < names.Length) Selected = to;
                    return;
                }
                base.OnKeyDown(e);
            }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override void Dispose(bool disposing)
            {
                if (disposing && bold != null) { bold.Dispose(); bold = null; }
                base.Dispose(disposing);
            }
        }

        // ---------- presets depend on the nozzle ----------
        Wear.Nozzle SelectedNozzle
        {
            get { return Wear.Nozzles[Math.Max(0, cmbNozzle.SelectedIndex)]; }
        }

        static void SetItems(ComboBox cb, List<string> items, string wanted, string fallbackContains)
        {
            cb.BeginUpdate();
            cb.Items.Clear();
            foreach (var i in items) cb.Items.Add(i);
            string pick = null;
            if (!string.IsNullOrEmpty(wanted) && items.Contains(wanted)) pick = wanted;
            if (pick == null) pick = items.Find(i => i.IndexOf(fallbackContains, StringComparison.OrdinalIgnoreCase) >= 0);
            if (pick == null && items.Count > 0) pick = items[0];
            if (pick == null && !string.IsNullOrEmpty(wanted)) { cb.Items.Add(wanted); pick = wanted; }   // no Bambu Studio
            cb.SelectedItem = pick;
            cb.EndUpdate();
        }

        void RefillPresets()
        {
            RefillPresets((string)cmbProcess.SelectedItem, (string)cmbFilament.SelectedItem);
        }

        void RefillPresets(string process, string filament)
        {
            string size = SelectedNozzle.Size;
            bool was = filling;
            filling = true;
            SetItems(cmbProcess, BambuSlicer.ListProcesses(exe, ShownModel, size), process, "Optimal");
            SetItems(cmbFilament, BambuSlicer.ListFilaments(exe, ShownModel, size), filament, "Bambu PLA Matte");
            filling = was;
        }

        // ---------- wear rate ----------
        static string Kr(double v) { return v.ToString("0.00", Num.Dk); }
        static string Hours(double h) { return h.ToString("#,0", Num.Dk) + L.S(" h", " t"); }

        /// Nozzle name in the current language (Pricing.cs keeps the English label).
        static string NozzleLabel(Wear.Nozzle n)
        {
            if (!L.Danish) return n.Label;
            return n.Size.Replace('.', ',') + " mm " + (n.Hardened ? "hærdet stål" : "rustfrit stål") +
                   (n == Wear.Nozzles[0] ? " (std.)" : "");   // short: the full word doesn't fit the combo
        }

        void UpdateWear()
        {
            var nozzle = SelectedNozzle;
            var plate = Wear.Plates[Math.Max(0, cmbBed.SelectedIndex)];
            string filament = (string)cmbFilament.SelectedItem ?? "";
            string type = "";
            bool abrasive = false;
            try { BambuSlicer.FilamentInfo(exe, ShownModel, filament, nozzle.Size, out type, out abrasive); }
            catch { /* no Bambu Studio / profile: treat as normal filament */ }

            var model = Wear.FindModel(ShownModel);
            var b = Wear.Calculate(model, nozzle, abrasive, type, plate);
            bool auto = chkAutoWear.Checked;
            txtRate.ReadOnly = auto;
            if (auto) txtRate.Text = Num.Fmt(b.Total);

            // one line per part so the sum is easy to follow
            var rows = new[]
            {
                new[] { L.S("Printer (depreciation)", "Printer (afskrivning)"), Kr(b.Printer) },
                new[] { L.S("Belts, PTFE tube, wiper…", "Remme, PTFE-rør, visker…"), Kr(b.Parts) },
                new[] { "Hotend  (" + Math.Round(nozzle.PriceDkk * model.HotendFactor) + " kr. / " + Hours(b.HotendLife) + ")", Kr(b.Hotend) },
                new[] { L.S("Plate", "Plade") + "  (" + Math.Round(plate.PriceDkk * model.PlateFactor) + " kr. / " + Hours(b.PlateLife) + ")", Kr(b.Plate) },
                new[] { L.S("= Wear per print hour", "= Slid pr. printtime"), Kr(b.Total) + " kr." },
            };
            var notes = new List<string>();
            if (printerNote != null) notes.Add(printerNote);
            if (!auto) notes.Add(L.S("Not used: tick the box above to use this rate.", "Bruges ikke: sæt flueben ovenfor for at bruge denne sats."));
            if (b.AbrasiveOnStainless)
                notes.Add(L.F("⚠ {0} is abrasive and needs a hardened steel nozzle. A stainless nozzle wears out in ~100 h.",
                              "⚠ {0} er slibende og kræver en dyse af hærdet stål. En dyse af rustfrit stål slides op på ~100 t.", filament));
            else if (b.Abrasive)
                notes.Add(L.F("{0} is abrasive: the hardened nozzle lasts ~{1}.", "{0} er slibende: den hærdede dyse holder ~{1}.",
                              filament, Hours(b.HotendLife)));
            if (b.PlateHalved)
                notes.Add(L.F("{0} can tear the smooth PEI surface (use glue stick), so the plate's life is halved.",
                              "{0} kan rive den glatte PEI-overflade i stykker (brug limstift), så pladens levetid halveres.", type));
            lblWear.SetTable(rows, string.Join("\n\n", notes));
            lblWear.Kind = b.AbrasiveOnStainless ? InfoKind.Error
                         : (b.Abrasive || b.PlateHalved) ? InfoKind.Warning
                         : InfoKind.Info;
        }

        // ---------- fill / accept ----------
        IEnumerable<TextBox> AllBoxes()
        {
            foreach (var p in pages)
                foreach (Control c in p.Controls)
                    if (c is TextBox) yield return (TextBox)c;
        }

        /// Fills every tab from s; the Printer tab edits a copy (work) and shows the active printer.
        void Fill(AppSettings s)
        {
            work = s.Clone();
            FillGeneral(s);
            FillPrinterList();
            ShowPrinterNow(work.ActivePrinterId);
        }

        /// Defaults: every tab's defaults; the printer the Printer tab shows gets its model's defaults (name, other
        /// printers and the list stay).
        void Defaults()
        {
            var d = new AppSettings();
            FillGeneral(d);
            var p = work.FindPrinter(shownId);
            if (p != null)
            {
                var m = Wear.FindModel(p.Model);
                work.SetPrinter(new PrinterProfile { Id = p.Id, Name = p.Name, Model = m.Key, Watts = m.DefaultWatts,
                                                     MachineRatePerHour = Wear.DefaultRateFor(m.Key) });
            }
            work.FilamentPreset = d.FilamentPreset;
            ShowPrinterNow(shownId);
        }

        void FillGeneral(AppSettings s)
        {
            filling = true;
            foreach (var box in AllBoxes()) box.BackColor = Theme.Input;   // Defaults: no red boxes left over
            txtFriends.Text = Num.Fmt(s.FriendsMarkupPct);
            txtOthers.Text = Num.Fmt(s.OthersMarkupPct);
            txtMin.Text = Num.Fmt(s.MinimumPrice);
            txtRound.Text = Num.Fmt(s.RoundTo);
            txtFailed.Text = Num.Fmt(s.FailedPrintPct);
            txtPayback.Text = Num.Fmt(s.FailedPaybackPct);
            cmbPaybackWho.SelectedIndex = s.FailedPaybackWho == 1 ? 1 : 0;
            chkPayback.Checked = s.FailedPayback != 0;
            txtPayback.Enabled = cmbPaybackWho.Enabled = chkPayback.Checked;   // CheckedChanged doesn't fire when it stays the same
            txtSlicer.Text = s.SlicerPath;
            chkAutoPower.Checked = s.AutoPowerPrice != 0;
            cmbArea.SelectedIndex = s.PriceArea == "DK1" ? 0 : 1;
            txtAddOn.Text = Num.Fmt(s.PowerAddOn);
            chkVat.Checked = s.PowerIncludeVat != 0;
            chkAutoUpdate.Checked = s.AutoUpdateCheck != 0;
            cmbLang.SelectedIndex = s.Language == "en" ? 1 : s.Language == "da" ? 2 : 0;
            txtQuoteName.Text = s.QuoteName ?? "";
            txtQuoteDays.Text = Num.Fmt(s.QuoteValidDays);
            txtDueDays.Text = Num.Fmt(s.InvoiceDueDays);
            txtMobilePay.Text = s.PayMobilePay ?? "";
            txtBank.Text = s.PayBank ?? "";
            filling = false;
        }

        // ---------- several printers (Printer tab) ----------
        /// Model key of the printer the Printer tab shows (its Bambu Studio lists and wear).
        string ShownModel
        {
            get { var p = work != null ? work.FindPrinter(shownId) : null; return p != null ? p.Model : Wear.ModelA1; }
        }

        void FillPrinterList()
        {
            bool was = filling;
            filling = true;
            cmbPrinter.BeginUpdate();
            cmbPrinter.Items.Clear();
            string active = work.ActivePrinterId;
            foreach (var p in work.Printers)
                cmbPrinter.Items.Add(new PrinterItem { Id = p.Id, Name = AppSettings.CleanPrinterName(p.Name), InUse = p.Id == active });
            cmbPrinter.EndUpdate();
            filling = was;
            if (fitted) FitPrinterList();
        }

        /// The printer drop-down's open list: as wide as the longest name (the closed box may cut it with …).
        void FitPrinterList()
        {
            int longest = 0;
            foreach (var o in cmbPrinter.Items) longest = Math.Max(longest, TextRenderer.MeasureText(o.ToString(), cmbPrinter.Font).Width);
            cmbPrinter.DropDownWidth = Math.Max(cmbPrinter.Width, longest + Px(30));
        }

        int PrinterIndex(string id)
        {
            for (int i = 0; i < cmbPrinter.Items.Count; i++) if (((PrinterItem)cmbPrinter.Items[i]).Id == id) return i;
            return -1;
        }

        /// Shows printer `id` on the Printer tab (no saving of the one shown before: see StorePrinter).
        void ShowPrinterNow(string id)
        {
            var p = work.FindPrinter(id) ?? work.ActivePrinter;
            shownId = p.Id;
            var model = Wear.FindModel(p.Model);
            filling = true;
            cmbPrinter.SelectedIndex = PrinterIndex(p.Id);
            foreach (var box in new[] { txtWatts, txtRate, txtPrinterName }) box.BackColor = Theme.Input;
            ((CueBox)txtPrinterName).Cue = model.Label;
            txtPrinterName.AccessibleDescription = model.Label;
            string name = AppSettings.CleanPrinterName(p.Name);
            txtPrinterName.Text = name == model.Label ? "" : name;   // empty = the model's name (grey)
            txtPrinterName.Invalidate();
            txtWatts.Text = Num.Fmt(p.Watts);
            lblWattsUnit.Text = "W (" + model.Label.Replace("Bambu Lab ", "") + ": ~" + Num.Fmt(model.DefaultWatts) + ")";
            txtRate.Text = Num.Fmt(p.MachineRatePerHour);
            chkAutoWear.Checked = p.AutoWearRate != 0;
            cmbNozzle.SelectedIndex = Array.IndexOf(Wear.Nozzles, Wear.FindNozzle(p.Nozzle));
            RefillPresets(BambuSlicer.ShortName(p.ProcessPreset), BambuSlicer.ShortName(work.FilamentPreset));
            cmbBed.SelectedIndex = Array.IndexOf(Wear.Plates, Wear.FindPlate(p.BedType));
            printerNote = null;
            filling = false;
            UpdateWear();
        }

        /// Puts the Printer tab's boxes into the shown printer (in `work`). False (boxes red, focus on the first) when
        /// power or wear rate isn't a valid number.
        bool StorePrinter()
        {
            double w, rate;
            bool ok = ReadBox(txtWatts, out w);
            ok &= ReadBox(txtRate, out rate);
            if (!ok)
            {
                SelectTab(TabPrinter);
                (txtWatts.BackColor == Theme.InvalidBack ? txtWatts : txtRate).Focus();
                return false;
            }
            var p = work.FindPrinter(shownId);
            if (p == null) return true;
            string name = txtPrinterName.Text.Trim();
            p.Name = name.Length == 0 ? Wear.FindModel(p.Model).Label : name;
            p.Watts = w;
            p.MachineRatePerHour = rate;
            p.AutoWearRate = chkAutoWear.Checked ? 1 : 0;
            p.Nozzle = SelectedNozzle.Key;
            p.ProcessPreset = (string)cmbProcess.SelectedItem ?? p.ProcessPreset;
            p.BedType = Wear.Plates[Math.Max(0, cmbBed.SelectedIndex)].Key;
            work.SetPrinter(p);
            work.FilamentPreset = (string)cmbFilament.SelectedItem ?? work.FilamentPreset;   // shared by all printers
            return true;
        }

        /// Another printer picked in the drop-down: keep the shown one's values, then show the new one (a bad number
        /// keeps the old printer shown).
        void PrinterChosen()
        {
            var item = cmbPrinter.SelectedItem as PrinterItem;
            if (item == null || item.Id == shownId) return;
            if (!StorePrinter())
            {
                filling = true;
                cmbPrinter.SelectedIndex = PrinterIndex(shownId);
                filling = false;
                return;
            }
            ShowPrinterNow(item.Id);
        }

        /// Typing a name renames the printer in the drop-down at once.
        void NameTyped()
        {
            int i = PrinterIndex(shownId);
            if (i < 0) return;
            var item = (PrinterItem)cmbPrinter.Items[i];
            string name = txtPrinterName.Text.Trim();
            item.Name = AppSettings.CleanPrinterName(name.Length == 0 ? Wear.FindModel(ShownModel).Label : name);
            filling = true;
            cmbPrinter.Items[i] = item;   // redraws its text
            cmbPrinter.SelectedIndex = i;
            filling = false;
            cmbPrinter.Invalidate();
        }

        void BuildAddMenu()
        {
            addMenu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Renderer = new QuoteMenuRenderer(),
                                             BackColor = Theme.Input, ForeColor = Theme.Text };
            addMenu.Items.Add(new ToolStripMenuItem(L.S("Add which printer?", "Hvilken printer?")) { Enabled = false });
            foreach (var m in Wear.Models)
            {
                var model = m;
                var mi = new ToolStripMenuItem(model.Label) { ForeColor = Theme.Text };
                mi.Click += delegate { AddPrinter(model.Key); };
                addMenu.Items.Add(mi);
            }
            FormClosed += delegate { addMenu.Dispose(); };
        }

        /// Adds a printer of this model (the model's power, wear and stock nozzle / plate), shows it, focus to its name.
        /// Its name = the model's, with " 2", " 3"… when that is taken.
        void AddPrinter(string modelKey)
        {
            if (!StorePrinter()) return;
            string label = Wear.FindModel(modelKey).Label, name = label;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in work.Printers) names.Add(AppSettings.CleanPrinterName(p.Name));
            for (int n = 2; names.Contains(name); n++) name = label + " " + n;
            var added = work.AddPrinter(name, modelKey);
            if (useAdded) { work.SelectPrinter(added.Id); useAdded = false; }   // from Choose printer: use it after OK
            FillPrinterList();
            ShowPrinterNow(added.Id);
            SelectTab(TabPrinter);
            txtPrinterName.Focus();
            txtPrinterName.SelectAll();
        }

        /// Removes the shown printer (only when there are 2+; Cancel brings it back). Its quotes keep its name in History.
        /// A note under the wear table says so (or why the only printer can't be removed).
        void RemoveShownPrinter()
        {
            if (work.PrinterCount < 2)
            {
                printerNote = L.S("One printer is always needed: add another one before removing this one.",
                                  "Der skal altid være én printer: tilføj en til, før du fjerner denne.");
                UpdateWear();
                return;
            }
            var gone = work.FindPrinter(shownId);
            string name = gone != null ? AppSettings.CleanPrinterName(gone.Name) : "";
            if (!work.RemovePrinter(shownId)) return;
            FillPrinterList();
            ShowPrinterNow(work.ActivePrinterId);
            printerNote = L.F("Removed \"{0}\". Cancel brings it back.", "Fjernede \"{0}\". Annullér fortryder det.", name);
            UpdateWear();
            cmbPrinter.Focus();
        }

        /// Before the window shows (MainForm's Choose printer): show printer `id` (null = the active one) on the Printer
        /// tab; addMenu = open the Add… menu once the window is up.
        public void ShowPrinter(string id, bool openAddMenu)
        {
            if (id != null && work.FindPrinter(id) != null) ShowPrinterNow(id);
            SelectedTab = TabPrinter;
            useAdded = openAddMenu;
            if (openAddMenu && !Screenshots.Active && !Program.Hidden)
                Shown += delegate { BeginInvoke((MethodInvoker)delegate { btnAddPrinter.Focus(); addMenu.Show(btnAddPrinter, new Point(0, btnAddPrinter.Height)); }); };
        }

        /// Picture mode: draws the Add… menu (never shown on screen there) under its button, as it opens.
        /// origin = the picture's top left on screen.
        internal void SampleAddMenu(Graphics g, Point origin)
        {
            addMenu.PerformLayout();
            var size = addMenu.GetPreferredSize(Size.Empty);
            addMenu.Size = size;
            using (var bmp = new Bitmap(size.Width, size.Height))
            {
                addMenu.DrawToBitmap(bmp, new Rectangle(Point.Empty, size));
                var at = btnAddPrinter.PointToScreen(new Point(0, btnAddPrinter.Height));
                g.DrawImage(bmp, at.X - origin.X, at.Y - origin.Y);
            }
        }

        /// A number box's value; anything but a valid number (also empty) turns the box red.
        static bool ReadBox(TextBox box, out double value)
        {
            bool good = Num.Read(box, out value) == FieldState.Ok;
            if (!good) box.BackColor = Theme.InvalidBack;
            return good;
        }

        /// Whole days in [min, max]; else the box turns red.
        static bool ReadDays(TextBox box, double max, out double days)
        {
            bool good = ReadBox(box, out days) && days >= 1 && days <= max && days == Math.Floor(days);
            if (!good) box.BackColor = Theme.InvalidBack;
            return good;
        }

        void Accept()
        {
            double w, f, o, rate, min, round, failed, addOn, days, dueDays;
            bool ok = ReadBox(txtWatts, out w);
            ok &= ReadBox(txtFriends, out f);
            ok &= ReadBox(txtOthers, out o);
            ok &= ReadBox(txtRate, out rate);
            ok &= ReadBox(txtMin, out min);
            ok &= ReadBox(txtRound, out round);
            ok &= ReadBox(txtFailed, out failed);
            ok &= ReadBox(txtAddOn, out addOn);
            ok &= ReadDays(txtQuoteDays, 3650, out days);   // quote validity: whole days, 1–3650
            ok &= ReadDays(txtDueDays, 365, out dueDays);   // invoice: whole days, 1–365
            // payback %: checked only while it is used (a greyed-out box can't be fixed); off = a valid number is kept, else the old one
            double payback = source.FailedPaybackPct;
            if (chkPayback.Checked) ok &= ReadBox(txtPayback, out payback);
            else
            {
                double v;
                if (Num.TryParse(txtPayback.Text, out v)) payback = v;
                txtPayback.BackColor = Theme.Input;
            }
            if (!ok)
            {
                RestartRequested = false;
                // show the first tab with a red box, and put the cursor in that box
                for (int i = 0; i < pages.Length; i++)
                {
                    TextBox bad = null;
                    foreach (Control c in pages[i].Controls)
                        if (c is TextBox && c.BackColor == Theme.InvalidBack) { bad = (TextBox)c; break; }
                    if (bad == null) continue;
                    SelectTab(i);
                    bad.Focus();
                    break;
                }
                string msg = L.S("Please enter valid, non-negative numbers in the highlighted fields.",
                                 "Indtast gyldige tal, ikke negative, i de markerede felter.");
                // hidden test start: no message box (it would pop up on screen), red text in the bottom row instead
                if (Program.Hidden) SetCheckText(msg, null, Theme.Error);
                else MessageBox.Show(this, msg, Text);
                return;
            }
            StorePrinter();   // the shown printer's boxes are valid (checked above)
            Result = work.Clone();   // printers (the active one = the flat fields) + everything not shown here
            Result.FriendsMarkupPct = f;
            Result.OthersMarkupPct = o;
            Result.MinimumPrice = min;
            Result.RoundTo = round;
            Result.FailedPrintPct = failed;
            Result.FailedPayback = chkPayback.Checked ? 1 : 0;
            Result.FailedPaybackPct = payback;
            Result.FailedPaybackWho = cmbPaybackWho.SelectedIndex == 1 ? 1 : 2;
            Result.SlicerPath = txtSlicer.Text.Trim().Trim('"');
            Result.AutoPowerPrice = chkAutoPower.Checked ? 1 : 0;
            Result.PriceArea = cmbArea.SelectedIndex == 0 ? "DK1" : "DK2";
            Result.PowerAddOn = addOn;
            Result.PowerIncludeVat = chkVat.Checked ? 1 : 0;
            Result.AutoUpdateCheck = chkAutoUpdate.Checked ? 1 : 0;
            Result.Language = SelectedLanguage;
            Result.QuoteName = txtQuoteName.Text.Trim();
            Result.QuoteValidDays = days;
            Result.InvoiceDueDays = dueDays;
            Result.PayMobilePay = txtMobilePay.Text.Trim();
            Result.PayBank = txtBank.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        }

        // ---------- "check now" (only when clicked; never in picture mode) ----------
        /// Text bottom left, then an optional clickable part ("check now" / "try again"); colour null = muted.
        internal void SetCheckText(string text, string link, Color? colour)
        {
            lblCheck.Text = text;
            lblCheck.ForeColor = colour ?? Theme.Muted;
            lnkCheck.Text = link ?? "";
            lnkCheck.Visible = link != null;
        }

        /// "vX is out – see main window" (also used by picture mode as the longest result text).
        internal static string FoundText(UpdateInfo found)
        {
            return L.F("v{0} is out – see main window", "v{0} er ude – se hovedvinduet", Updater.Short(found.Version));
        }

        /// Runs once when Settings opens (auto update on): quiet check, ETag-cached. Failure = line stays as it is.
        async void QuietCheckOnOpen()
        {
            if (UpdateFound == null) return;   // offline: no checks
            string before = lblCheck.Text;
            UpdateInfo found = null;
            bool ok = false;
            // LastQuietOk is read in the same task, right after CheckQuiet (the main window's timer may check too)
            try { await Task.Run(() => { found = Updater.CheckQuiet(); ok = Updater.LastQuietOk; }); }
            catch { return; }
            if (IsDisposed || lblCheck.Text != before) return;   // closed, or "check now" was clicked meanwhile
            if (found == null)
            {
                if (!ok) return;   // check failed: the line stays as it is

                SetCheckText(L.F("✓ v{0} is the newest", "✓ v{0} er den nyeste", Updater.CurrentText), null, null);
                return;
            }
            SetCheckText(FoundText(found), null, Theme.Text);
            UpdateFound(found);
        }

        async void CheckNow()
        {
            if (Screenshots.Active || AppSettings.TestMode) return;
            SetCheckText(L.S("Checking for a new version…", "Søger efter ny version…"), null, null);
            UpdateInfo found;
            try { found = await Task.Run(() => Updater.CheckForUpdate()); }
            catch
            {
                if (!IsDisposed) SetCheckText(L.S("Check failed –", "Tjek mislykkedes –"), L.S("try again", "prøv igen"), Theme.Error);
                return;
            }
            if (IsDisposed) return;
            if (found == null)
            {
                SetCheckText(L.F("✓ v{0} is the newest", "✓ v{0} er den nyeste", Updater.CurrentText), null, null);   // muted
                return;
            }
            SetCheckText(FoundText(found), null, Theme.Text);
            if (UpdateFound != null) UpdateFound(found);
        }
    }
}
