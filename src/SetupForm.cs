using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintPrice
{
    /// First-time setup (idea 19): a short guided start with 4 questions (language, printers + Bambu Studio, filament
    /// and electricity, markups + minimum + name on quotes) and a summary. Shown by Program before the main window on a
    /// new install (AppSettings.SetupDone 0), and again from Settings > App ("Run setup again…", via MainForm).
    /// Back / Skip / Next on every step; Skip puts the step's answers back to how they were when the window opened.
    /// First run: closing it (Set up later, ✕, Esc) keeps the steps finished with Next, the rest default; Result is never
    /// null then. Again: closing = Cancel, nothing changes. The window is as high as its tallest step (measured on Load).
    sealed partial class SetupForm : CaptionForm
    {
        public const int StepLanguage = 0, StepPrinter = 1, StepCosts = 2, StepPrices = 3, StepSummary = 4;
        const int Questions = 4;
        // layout (96 dpi units)
        const int ContentW = 532, Edge = 14, ButtonH = 32, ButtonMinW = 75, NumberBoxW = 70, UnitGap = 6, TileGap = 8,
                  TileH = 150, LogoSize = 84, Indent = 22;
        const string StudioDownload = "https://bambulab.com/en/download/studio";

        AppSettings start;      // the settings when the window opened (Skip goes back to these; a history restore updates it)
        readonly bool again;    // opened from Settings > App
        readonly bool offline;  // picture mode: no network, no dialogs
        readonly string startLang;   // "en" / "da" as the window opened

        /// The settings with the answers (SetupDone = 1); null after Cancel on a re-run. Not saved.
        public AppSettings Result { get; private set; }
        /// Re-run: the language changed, so the program should restart after saving.
        public bool RestartRequested { get; private set; }
        /// First run: a backup with settings was restored (saved on disk); the caller loads the settings again.
        public bool Restored { get; private set; }
        /// First run: closed without finishing (Set up later, ✕, Esc) and not by a restore: the main window shows a hint once.
        public bool SetUpLater { get; private set; }

        // ---------- the answers (AppSettings.cs SetupAnswers, shared with the website; number boxes as typed) ----------
        SetupAnswers answers, opening;   // opening = the answers the window opened with (Skip goes back to them)
        string lang;                     // the window's language choice; answers.Language is set from it in Apply
        readonly List<string> haveModels = new List<string>();   // re-run: models already added (locked)
        SetupPrinterMode Mode { get { return again ? SetupPrinterMode.Add : SetupPrinterMode.Replace; } }
        // short names for the answers (read on every use: CopyStep puts a new Models list in)
        List<string> models { get { return answers.Models; } }
        string slicerPath { get { return answers.SlicerPath; } set { answers.SlicerPath = value; } }
        string filamentText { get { return answers.FilamentText; } set { answers.FilamentText = value; } }
        string elecText { get { return answers.ElecText; } set { answers.ElecText = value; } }
        string friendsText { get { return answers.FriendsText; } set { answers.FriendsText = value; } }
        string othersText { get { return answers.OthersText; } set { answers.OthersText = value; } }
        string minText { get { return answers.MinText; } set { answers.MinText = value; } }
        string nameText { get { return answers.QuoteName; } set { answers.QuoteName = value; } }
        bool live { get { return answers.LivePower; } set { answers.LivePower = value; } }
        string area { get { return answers.Area; } set { answers.Area = value; } }
        readonly bool[] done = new bool[Questions], skipped = new bool[Questions];
        double livePrice = -1;   // fetched total kr/kWh for liveFor (-1 = none)
        string liveFor, liveLine;
        int step;
        bool finishing, filling;

        // ---------- controls (rebuilt when the language changes on a first run) ----------
        TableLayoutPanel root, headPanel, bottomPanel;
        int capColW, hostH;   // caption column width and step height (real px, set by Fit / FitHeight)
        Label lblTitle, lblStep, lblStudioPrompt;
        StepBar bar;
        Panel host;
        TableLayoutPanel[] pages;
        readonly List<Label> captions = new List<Label>();
        readonly List<InfoBox> fullBoxes = new List<InfoBox>();
        InfoBox infoWelcome, infoStudio;
        LogoView logo;
        QuoteForm.Choice choiceEn, choiceDa, choiceLive, choiceFixed;
        Label lblRestoreHead;
        InfoBox infoRestore;
        Button btnRestore, btnFindStudio, btnLookAgain;
        Tile[] tiles;
        FlowLayoutPanel tileFlow;
        TextBox txtFilament, txtElec, txtFriends, txtOthers, txtMin, txtName;
        Label lblElecCap, lblAreaCap, lblLiveNow;
        ComboBox cmbArea;
        ExampleView example;
        TableLayoutPanel sumTable;
        Label[] sumValue, sumChange;
        InfoBox infoSummary;
        Panel separator;
        Theme.LinkText lnkLater;
        Button btnBack, btnSkip, btnNext;
        ToolTip tip;

        public SetupForm(AppSettings current, bool again)
        {
            start = (current ?? new AppSettings()).Clone();
            this.again = again;
            offline = Screenshots.Active;
            startLang = again ? (AppSettings.IsDanish(start.Language) ? "da" : "en") : (L.Danish ? "da" : "en");
            lang = startLang;
            foreach (var p in start.Printers)
                if (!haveModels.Contains(p.Model)) haveModels.Add(p.Model);
            opening = SetupAnswers.From(start, Mode);
            answers = opening.Clone();

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = !again;   // first run: a window of its own (taskbar button)
            ShowInTaskbar = !again;
            StartPosition = again ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (Program.Hidden || offline) Screenshots.Place(this);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Build();
            Load += delegate { Fit(); ShowStep(0); };
            Shown += delegate { if (!offline && !Program.Hidden) FocusStep(); };
            HookTestDriver();   // --test-setup only (SetupFormTest.cs)
        }

        int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

        protected override bool ShowWithoutActivation { get { return Screenshots.Active || Program.Hidden; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        // ---------- answers ----------
        /// Puts one step's answers back to the values the window opened with.
        void ResetStep(int i)
        {
            if (i == StepLanguage) lang = startLang;   // the window's choice (answers.Language follows it in Apply)
            else answers.CopyStep(i, opening);
        }

        static bool ReadNumber(string text, out double v) { return SetupAnswers.ReadNumber(text, out v); }

        /// The settings with every answer in (skipped steps = the values the window opened with); AppSettings.ApplySetup:
        /// a first run replaces the default printer by the first one ticked, a re-run only adds.
        AppSettings Apply()
        {
            answers.Language = lang != startLang ? lang : null;
            return start.ApplySetup(answers, Mode);
        }

        // ---------- building ----------
        void Build()
        {
            SuspendLayout();
            captions.Clear();
            fullBoxes.Clear();
            hostH = 0;
            Text = L.S("Set up PrintPrice", "Opsætning af PrintPrice");
            if (tip == null)
            {
                tip = new ToolTip { OwnerDraw = true, ShowAlways = true };
                tip.Draw += MainForm.DrawDarkTip;
            }
            tip.RemoveAll();
            root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Edge, 12, Edge, Edge), Margin = Padding.Empty };

            // header: step title (bold 12) + "Step 2 of 4" (muted 9, right), then the progress bar
            var head = headPanel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblTitle = new Label { AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = Padding.Empty, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            lblStep = new Theme.PlainLabel { AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI", 9f), Tag = "muted", Margin = new Padding(0, 0, 0, 2), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            head.Controls.Add(lblTitle, 0, 0);
            head.Controls.Add(lblStep, 1, 0);
            root.Controls.Add(head);
            bar = new StepBar { Height = 3, Margin = new Padding(0, 6, 0, 12) };
            root.Controls.Add(bar);

            host = new Panel { Margin = Padding.Empty, Size = new Size(ContentW, 300) };
            root.Controls.Add(host);
            pages = new TableLayoutPanel[5];
            for (int i = 0; i < pages.Length; i++)
            {
                var p = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty, Location = Point.Empty };
                p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                pages[i] = p;
                host.Controls.Add(p);
            }
            BuildLanguage(pages[StepLanguage]);
            BuildPrinter(pages[StepPrinter]);
            BuildCosts(pages[StepCosts]);
            BuildPrices(pages[StepPrices]);
            BuildSummary(pages[StepSummary]);

            // bottom: 1 px line 9 px above the buttons; "Set up later" left, Back / Skip / Next right
            separator = new Panel { Height = 1, Margin = new Padding(0, 12, 0, 9) };
            root.Controls.Add(separator);
            var bottom = bottomPanel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lnkLater = new Theme.LinkText { AutoSize = true, UseMnemonic = false, MutedLink = true, Tag = "muted", Anchor = AnchorStyles.Left,
                                            Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 2),
                                            Text = L.S("Set up later", "Opsæt senere"), Visible = !again };
            lnkLater.Click += delegate { Close(); };
            tip.SetToolTip(lnkLater, L.S("Use the defaults for now.\nRun the setup again from Settings > App.",
                                         "Brug standardværdierne indtil videre.\nKør opsætningen igen fra Indstillinger > App."));
            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
            btnBack = Btn(L.S("Back", "Tilbage"));
            btnSkip = Btn(L.S("Skip", "Spring over"));
            btnNext = Btn(L.S("Next", "Næste"));
            btnNext.Margin = Padding.Empty;
            btnBack.Click += delegate { if (step > 0) ShowStep(step - 1); };
            btnSkip.Click += delegate { SkipStep(); };
            btnNext.Click += delegate { NextStep(); };
            flow.Controls.Add(btnBack);
            flow.Controls.Add(btnSkip);
            flow.Controls.Add(btnNext);
            bottom.Controls.Add(lnkLater, 0, 0);
            bottom.Controls.Add(flow, 1, 0);
            root.Controls.Add(bottom);
            btnBack.TabIndex = 0; btnSkip.TabIndex = 1; btnNext.TabIndex = 2;
            AcceptButton = btnNext;

            Controls.Add(root);
            Theme.Apply(this);
            Theme.AccentButton(btnNext);
            separator.BackColor = Theme.Border;   // after Apply, which paints panels in the background colour
            FillControls();
            ResumeLayout(true);
        }

        static Button Btn(string text)
        {
            return new Button { Text = text, Size = new Size(ButtonMinW, ButtonH), Margin = new Padding(0, 0, 6, 0), Padding = Padding.Empty, UseMnemonic = false };
        }

        Label Caption(TableLayoutPanel t, int row, string text, int indent)
        {
            var l = new Label { Text = text, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(indent, 6, Theme.LabelGap, 6) };
            t.Controls.Add(l, 0, row);
            captions.Add(l);
            return l;
        }

        TextBox NumberRow(TableLayoutPanel t, string caption, string unit, int indent, out Label cap)
        {
            int r = t.RowCount++;
            cap = Caption(t, r, caption, indent);
            var box = new TextBox { Width = NumberBoxW, TextAlign = HorizontalAlignment.Right, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 3) };
            box.Enter += delegate { box.BeginInvoke((MethodInvoker)delegate { box.SelectAll(); }); };
            box.AccessibleName = caption;
            t.Controls.Add(box, 1, r);
            t.Controls.Add(new SettingsForm.UnitLabel { Text = unit, AutoSize = true, Anchor = AnchorStyles.Left, Tag = "muted", Margin = new Padding(UnitGap, 0, 0, 0), UseMnemonic = false }, 2, r);
            return box;
        }

        Label Section(TableLayoutPanel t, string text, int top)
        {
            int r = t.RowCount++;
            var l = new Label { Text = text, AutoSize = true, UseMnemonic = false, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, top, 0, 4) };
            t.Controls.Add(l, 0, r);
            t.SetColumnSpan(l, 3);
            return l;
        }

        InfoBox Box(TableLayoutPanel t, string text, string muted, int bottom)
        {
            int r = t.RowCount++;
            var b = new InfoBox(ContentW, 9f) { Margin = new Padding(0, 2, 0, bottom) };
            b.SetText(text, muted);
            t.Controls.Add(b, 0, r);
            t.SetColumnSpan(b, 3);
            fullBoxes.Add(b);
            return b;
        }

        QuoteForm.Choice ChoiceRow(TableLayoutPanel t, string text, int indent)
        {
            int r = t.RowCount++;
            var c = new QuoteForm.Choice { Text = text, Margin = new Padding(indent, 0, 0, 0), Width = ContentW - indent };
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, 3);
            return c;
        }

        Label Note(TableLayoutPanel t, string text, int column, int left)
        {
            int r = t.RowCount++;
            var l = new Theme.PlainLabel { Text = text, AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI", 9f), Tag = "muted", Margin = new Padding(left, 0, 0, 4) };
            t.Controls.Add(l, column, r);
            t.SetColumnSpan(l, 3 - column);
            return l;
        }

        void BuildLanguage(TableLayoutPanel t)
        {
            // logo beside the welcome text
            var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            logo = new LogoView { Size = new Size(LogoSize, LogoSize), Margin = new Padding(0, 0, 12, 0), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            infoWelcome = new InfoBox(ContentW - LogoSize - 12, 9f) { Margin = Padding.Empty, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            infoWelcome.SetText(again
                ? L.S("Go through the main questions again. Your answers are filled in; Skip leaves a step as it is.",
                      "Gå de vigtigste spørgsmål igennem igen. Dine svar er udfyldt; Spring over lader et trin være, som det er.")
                : L.S("A few quick questions so your prices fit your printer, your filament and your electricity. It takes about a minute.",
                      "Et par hurtige spørgsmål, så dine priser passer til din printer, dit filament og din strøm. Det tager omkring et minut."),
                again ? L.S("Everything else stays in Settings.", "Alt andet ligger stadig i Indstillinger.")
                      : L.S("Skip anything you're unsure of: it keeps a sensible default, and everything can be changed later in Settings.",
                            "Spring over, hvis du er i tvivl: så bruges en fornuftig standard, og alt kan ændres senere i Indstillinger."));
            row.Controls.Add(logo, 0, 0);
            row.Controls.Add(infoWelcome, 1, 0);
            int r = t.RowCount++;
            t.Controls.Add(row, 0, r);
            t.SetColumnSpan(row, 3);

            Section(t, "Sprog / Language", 4);   // both languages, like Settings
            choiceEn = ChoiceRow(t, "English", 0);
            choiceDa = ChoiceRow(t, "Dansk", 0);
            choiceEn.CheckedChanged += delegate { if (choiceEn.Checked) LanguageChosen("en"); };
            choiceDa.CheckedChanged += delegate { if (choiceDa.Checked) LanguageChosen("da"); };
            Note(t, again ? L.S("A new language shows after a restart (PrintPrice restarts when you save).",
                                "Et nyt sprog vises efter en genstart (PrintPrice genstarter, når du gemmer).")
                          : L.S("Picked from Windows. The window switches language at once.",
                                "Valgt ud fra Windows. Vinduet skifter sprog med det samme."), 0, Indent);

            lblRestoreHead = Section(t, L.S("Used PrintPrice before?", "Har du brugt PrintPrice før?"), 12);
            infoRestore = Box(t, L.S("If you have a backup from another PC, restore it instead: your settings and price history come back and this setup ends.",
                                     "Har du en sikkerhedskopi fra en anden pc, så gendan den i stedet: dine indstillinger og din prishistorik kommer tilbage, og opsætningen slutter."), null, 4);
            r = t.RowCount++;
            btnRestore = new Button { Text = L.S("Restore a backup…", "Gendan en sikkerhedskopi…"), Size = new Size(120, 25), Padding = Padding.Empty,
                                      Margin = new Padding(0, 4, 0, 0), UseMnemonic = false, Font = new Font("Segoe UI", 9f), Anchor = AnchorStyles.Left };
            btnRestore.Click += delegate { RestoreBackup(); };
            t.Controls.Add(btnRestore, 0, r);
            t.SetColumnSpan(btnRestore, 3);
            lblRestoreHead.Visible = infoRestore.Visible = btnRestore.Visible = !again;
            if (again) fullBoxes.Remove(infoRestore);
        }

        void BuildPrinter(TableLayoutPanel t)
        {
            int r = t.RowCount++;
            lblStudioPrompt = new Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 8),
                                          Text = again ? L.S("Tick any printers you want to add.", "Sæt flueben ved de printere, du vil tilføje.")
                                                       : L.S("Which printers do you have? Tick each one.", "Hvilke printere har du? Sæt flueben ved hver af dem.") };
            t.Controls.Add(lblStudioPrompt, 0, r);
            t.SetColumnSpan(lblStudioPrompt, 3);
            r = t.RowCount++;
            tileFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8), Padding = Padding.Empty };
            string exe = BambuSlicer.FindExe(start.SlicerPath);
            tiles = new Tile[Wear.Models.Length];
            for (int i = 0; i < tiles.Length; i++)
            {
                var m = Wear.Models[i];
                var tile = new Tile
                {
                    Model = m.Key, Title = m.Label,
                    Sub = L.F("{0} W · {1} kr/h wear", "{0} W · {1} kr/t slid", Num.Fmt(m.DefaultWatts), Num.N2(Wear.DefaultRateFor(m.Key))),
                    Size = new Size(172, TileH), Margin = new Padding(0, 0, i < tiles.Length - 1 ? TileGap : 0, 0),
                    InUseText = L.S("In use", "I brug"), AddedText = L.S("Added", "Tilføjet"),
                    AccessibleName = m.Label
                };
                tile.SetPhoto(exe == null ? null : MainForm.LoadPrinterPhoto(exe, m.Key));
                string key = m.Key;
                tile.Click += delegate { ToggleModel(key); };
                tiles[i] = tile;
                tileFlow.Controls.Add(tile);
            }
            t.Controls.Add(tileFlow, 0, r);
            t.SetColumnSpan(tileFlow, 3);
            Box(t, again ? L.S("Printers you already have are kept; remove or rename them in Settings > Printer.",
                               "Printere, du allerede har, bliver beholdt; fjern eller omdøb dem i Indstillinger > Printer.")
                         : L.S("The first one ticked is in use. Two of the same model? Add the second in Settings.",
                               "Den første med flueben er i brug. To af samme model? Tilføj den anden i Indstillinger."), null, 4);

            Section(t, "Bambu Studio", 10);
            infoStudio = Box(t, "", null, 4);
            r = t.RowCount++;
            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0), Padding = Padding.Empty };
            btnFindStudio = new Button { Text = L.S("Find bambu-studio.exe…", "Find bambu-studio.exe…"), Size = new Size(120, 25), Padding = Padding.Empty,
                                         Margin = new Padding(0, 0, 6, 0), UseMnemonic = false, Font = new Font("Segoe UI", 9f) };
            btnLookAgain = new Button { Text = L.S("Look again", "Søg igen"), Size = new Size(80, 25), Padding = Padding.Empty,
                                        Margin = Padding.Empty, UseMnemonic = false, Font = new Font("Segoe UI", 9f) };
            btnFindStudio.Click += delegate { FindStudio(); };
            btnLookAgain.Click += delegate { UpdateStudio(); };
            flow.Controls.Add(btnFindStudio);
            flow.Controls.Add(btnLookAgain);
            t.Controls.Add(flow, 0, r);
            t.SetColumnSpan(flow, 3);
        }

        void BuildCosts(TableLayoutPanel t)
        {
            Label cap;
            Section(t, L.S("Filament", "Filament"), 0);
            txtFilament = NumberRow(t, L.S("Filament price", "Filamentpris"), "kr/kg", 0, out cap);
            txtFilament.TextChanged += delegate { if (!filling) { filamentText = txtFilament.Text; Valid(txtFilament); } };
            Box(t, L.F("What you pay per kg for your usual filament ({0}), shipping included. Other filaments remember their own price.",
                       "Hvad du betaler pr. kg for dit sædvanlige filament ({0}), med fragt. Andre filamenter husker deres egen pris.",
                       start.FilamentPreset), null, 4);

            Section(t, L.S("Electricity", "Strøm"), 10);
            choiceLive = ChoiceRow(t, L.S("Live price from Energinet (changes every hour)", "Aktuel pris fra Energinet (skifter hver time)"), 0);
            int r = t.RowCount++;
            lblAreaCap = Caption(t, r, L.S("Price area", "Prisområde"), Indent);
            cmbArea = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 0, 3) };
            cmbArea.Items.Add(L.S("DK1 – Jutland & Funen", "DK1 – Jylland og Fyn"));
            cmbArea.Items.Add(L.S("DK2 – Zealand, Lolland & Bornholm", "DK2 – Sjælland, Lolland og Bornholm"));
            cmbArea.AccessibleName = L.S("Price area", "Prisområde");
            cmbArea.SelectedIndexChanged += delegate
            {
                if (filling) return;
                area = cmbArea.SelectedIndex == 1 ? "DK2" : "DK1";
                FetchLive();
                UpdateExample();
            };
            t.Controls.Add(cmbArea, 1, r);
            t.SetColumnSpan(cmbArea, 2);
            lblLiveNow = Note(t, " ", 1, 0);
            choiceFixed = ChoiceRow(t, L.S("A fixed price", "En fast pris"), 0);
            txtElec = NumberRow(t, L.S("Electricity price", "Elpris"), "kr/kWh", Indent, out lblElecCap);
            txtElec.TextChanged += delegate { if (!filling) { elecText = txtElec.Text; Valid(txtElec); UpdateExample(); } };
            choiceLive.CheckedChanged += delegate { if (!filling && choiceLive.Checked) { live = true; UpdatePower(); FetchLive(); UpdateExample(); } };
            choiceFixed.CheckedChanged += delegate { if (!filling && choiceFixed.Checked) { live = false; UpdatePower(); UpdateExample(); } };
            Box(t, L.S("A print uses little power (an A1 about 0,1 kWh an hour), so this is a small part of the price.",
                       "Et print bruger lidt strøm (en A1 omkring 0,1 kWh i timen), så det er en lille del af prisen."),
                   L.S("Tariffs, VAT and the start-up fetch are in Settings > Electricity.",
                       "Afgifter, moms og hentning ved start ligger i Indstillinger > Elpris."), 0);
        }

        void BuildPrices(TableLayoutPanel t)
        {
            Box(t, L.S("Every print gets two prices: one for friends & family, one for customers.",
                       "Hvert print får to priser: en til venner og familie og en til kunder."),
                   L.S("Failed-print allowance, rounding and invoice details keep their defaults (Settings).",
                       "Fejlprint, afrunding og fakturaoplysninger beholder deres standard (Indstillinger)."), 6);
            Label cap;
            txtFriends = NumberRow(t, L.S("Friends & family markup", "Avance, venner og familie"), "%", 0, out cap);
            txtOthers = NumberRow(t, L.S("Customer markup", "Avance, kunder"), "%", 0, out cap);
            txtMin = NumberRow(t, L.S("Minimum price (customers)", "Minimumspris (kunder)"), L.S("kr. (0 = off)", "kr. (0 = fra)"), 0, out cap);
            txtFriends.TextChanged += delegate { if (!filling) { friendsText = txtFriends.Text; Valid(txtFriends); UpdateExample(); } };
            txtOthers.TextChanged += delegate { if (!filling) { othersText = txtOthers.Text; Valid(txtOthers); UpdateExample(); } };
            txtMin.TextChanged += delegate { if (!filling) { minText = txtMin.Text; Valid(txtMin); UpdateExample(); } };
            int r = t.RowCount++;
            Caption(t, r, L.S("Your name on quotes", "Dit navn på tilbud"), 0);
            txtName = new SettingsForm.CueBox
            {
                Cue = L.S("optional (you or your shop)", "valgfrit (dig eller din butik)"), Width = 120, MaxLength = 40,
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 0, 3), TextAlign = HorizontalAlignment.Left
            };
            txtName.AccessibleName = L.S("Your name on quotes", "Dit navn på tilbud");
            txtName.TextChanged += delegate { if (!filling) nameText = txtName.Text; };
            t.Controls.Add(txtName, 1, r);
            t.SetColumnSpan(txtName, 2);
            r = t.RowCount++;
            example = new ExampleView { Margin = new Padding(0, 10, 0, 0), Size = new Size(ContentW, 90) };
            t.Controls.Add(example, 0, r);
            t.SetColumnSpan(example, 3);
        }

        void BuildSummary(TableLayoutPanel t)
        {
            int r = t.RowCount++;
            sumTable = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 0, 0, 10), Padding = Padding.Empty };
            sumTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sumTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sumTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            string[] names =
            {
                L.S("Language", "Sprog"), L.S("Printers", "Printere"), "Bambu Studio", L.S("Filament price", "Filamentpris"),
                L.S("Electricity", "Strøm"), L.S("Markups", "Avance"), L.S("Minimum price", "Minimumspris"), L.S("Name on quotes", "Navn på tilbud")
            };
            int[] stepOf = { StepLanguage, StepPrinter, StepPrinter, StepCosts, StepCosts, StepPrices, StepPrices, StepPrices };
            sumValue = new Label[names.Length];
            sumChange = new Label[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                sumTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                var cap = new Label { Text = names[i], AutoSize = true, UseMnemonic = false, Tag = "muted", Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Theme.LabelGap, 0) };
                captions.Add(cap);
                var val = new Label { AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
                var change = new Theme.LinkText { Text = L.S("Change", "Ret"), AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Right, Margin = new Padding(8, 4, 0, 0) };   // centred: 2 px lower, on the values' baseline (GDI+ draws higher)
                change.Font = new Font(Font, FontStyle.Underline);
                change.AccessibleName = L.S("Change", "Ret") + " " + names[i];
                int target = stepOf[i];
                change.Click += delegate { ShowStep(target); };
                sumChange[i] = change;
                sumTable.Controls.Add(cap, 0, i);
                sumTable.Controls.Add(val, 1, i);
                sumTable.Controls.Add(change, 2, i);
                sumValue[i] = val;
            }
            sumTable.RowCount = names.Length;
            t.Controls.Add(sumTable, 0, r);
            t.SetColumnSpan(sumTable, 3);
            infoSummary = Box(t, "", null, 0);
            infoSummary.Kind = InfoKind.Success;
        }

        // ---------- filling the controls from the answers ----------
        void FillControls()
        {
            filling = true;
            try
            {
                choiceEn.Checked = lang != "da";
                choiceDa.Checked = lang == "da";
                foreach (var tile in tiles) UpdateTile(tile);
                txtFilament.Text = filamentText;
                txtElec.Text = elecText;
                choiceLive.Checked = live;
                choiceFixed.Checked = !live;
                cmbArea.SelectedIndex = area == "DK2" ? 1 : 0;
                txtFriends.Text = friendsText;
                txtOthers.Text = othersText;
                txtMin.Text = minText;
                txtName.Text = nameText;
            }
            finally { filling = false; }
            foreach (var b in new[] { txtFilament, txtElec, txtFriends, txtOthers, txtMin }) Valid(b);
            UpdatePower();
            UpdateStudio();
            UpdateExample();
        }

        void FillStep(int i)
        {
            filling = true;
            try
            {
                switch (i)
                {
                    case StepLanguage: choiceEn.Checked = lang != "da"; choiceDa.Checked = lang == "da"; break;
                    case StepPrinter: foreach (var tile in tiles) UpdateTile(tile); break;
                    case StepCosts:
                        txtFilament.Text = filamentText; txtElec.Text = elecText;
                        choiceLive.Checked = live; choiceFixed.Checked = !live;
                        cmbArea.SelectedIndex = area == "DK2" ? 1 : 0;
                        break;
                    case StepPrices:
                        txtFriends.Text = friendsText; txtOthers.Text = othersText; txtMin.Text = minText; txtName.Text = nameText;
                        break;
                }
            }
            finally { filling = false; }
            foreach (var b in new[] { txtFilament, txtElec, txtFriends, txtOthers, txtMin }) Valid(b);
            UpdatePower();
            UpdateStudio();
            UpdateExample();
        }

        /// Red box for a value that can't be read (empty or bad); the fixed price only counts when chosen.
        bool Valid(TextBox box)
        {
            double v;
            bool ok = ReadNumber(box.Text, out v) || (box == txtElec && live);
            box.BackColor = ok ? Theme.Input : Theme.InvalidBack;
            return ok;
        }

        void UpdateTile(Tile tile)
        {
            tile.Locked = again && haveModels.Contains(tile.Model);
            tile.Ticked = models.Contains(tile.Model);
            tile.InUse = !again && models.Count > 0 && models[0] == tile.Model;
            tile.Invalidate();
        }

        void ToggleModel(string key)
        {
            if (again && haveModels.Contains(key)) return;   // kept: removing is in Settings
            if (models.Contains(key))
            {
                if (models.Count == 1) return;   // at least one printer
                models.Remove(key);
            }
            else models.Add(key);
            foreach (var tile in tiles) UpdateTile(tile);
            UpdateExample();
        }

        void UpdatePower()
        {
            cmbArea.Enabled = live;
            txtElec.Enabled = !live;
            lblAreaCap.ForeColor = live ? Theme.Text : Theme.Muted;
            lblElecCap.ForeColor = live ? Theme.Muted : Theme.Text;
            lblLiveNow.Visible = true;
            lblLiveNow.Text = !live ? " " : liveLine != null && liveFor == area ? liveLine : " ";
            Valid(txtElec);
        }

        /// The live price for the chosen area (off the UI thread), shown under the drop-down; picture mode = a made-up one.
        void FetchLive()
        {
            if (!live || liveFor == area) { UpdatePower(); return; }
            string a = area;
            liveFor = a;
            livePrice = -1;
            if (offline) { SetLive(a, 2.87, true); return; }
            liveLine = L.S("Getting the price now…", "Henter prisen nu…");
            UpdatePower();
            var ui = TaskScheduler.FromCurrentSynchronizationContext();
            double addOn = start.PowerAddOn;
            bool vat = start.PowerIncludeVat != 0;
            // test mode: never the live server. --test-setup's stand-in (live fail / a spot price) when it set one, else
            // the fixed test price (SetupTest.StandInPower), like the main window
            Func<string, PowerQuote> fetch = !AppSettings.TestMode ? new Func<string, PowerQuote>(PowerPrice.GetCurrent)
                : TestFetch != null && SetupTest.Active ? TestFetch : x => SetupTest.StandInPower(x, 1.20);
            Task.Run(() => fetch(a)).ContinueWith(t =>
            {
                var failure = t.Exception;   // observed here, so a failed fetch never reaches errors.log
                if (IsDisposed || liveFor != a) return;
                if (t.IsFaulted || t.Result == null) SetLive(a, -1, false);
                else SetLive(a, PowerPrice.TotalPrice(t.Result.SpotDkkPerKwh, addOn, vat), true);
            }, ui);
        }

        void SetLive(string a, double total, bool ok)
        {
            livePrice = ok ? total : -1;
            bool vat = start.PowerIncludeVat != 0;
            liveLine = !ok ? L.S("Can't get the live price now; the app tries again when it starts.",
                                 "Kan ikke hente prisen nu; programmet prøver igen, når det starter.")
                     : vat ? L.F("Now {0} kr/kWh incl. {1} tariffs & fees and 25 % VAT", "Nu {0} kr/kWh inkl. {1} i afgifter og 25 % moms", Num.N2(total), Num.N2(start.PowerAddOn))
                           : L.F("Now {0} kr/kWh incl. {1} tariffs & fees", "Nu {0} kr/kWh inkl. {1} i afgifter", Num.N2(total), Num.N2(start.PowerAddOn));
            UpdatePower();
            UpdateExample();
        }

        void UpdateStudio()
        {
            if (infoStudio == null) return;
            string exe = BambuSlicer.FindExe(slicerPath);
            ShowStudio(exe);
        }

        void ShowStudio(string exe)
        {
            if (exe != null)
            {
                infoStudio.Kind = InfoKind.Success;
                infoStudio.SetText(L.F("✓ Found: {0}", "✓ Fundet: {0}", exe),
                                   L.S("PrintPrice uses it to work out print time and filament from a model.",
                                       "PrintPrice bruger det til at regne printtid og filament ud fra en model."));
                btnLookAgain.Visible = false;
            }
            else
            {
                infoStudio.Kind = InfoKind.Warning;
                infoStudio.Text = L.S("Bambu Studio was not found. PrintPrice needs it (free) to work out time and filament from a model; without it you type them in yourself.",
                                      "Bambu Studio blev ikke fundet. PrintPrice skal bruge det (gratis) til at regne tid og filament ud fra en model; uden det skriver du dem selv.");
                infoStudio.SetLink(L.S("Download Bambu Studio", "Hent Bambu Studio"), delegate
                {
                    if (offline || Program.Hidden) return;
                    try { Process.Start(StudioDownload); } catch { }
                });
                btnLookAgain.Visible = true;
            }
        }

        void FindStudio()
        {
            if (offline) return;
            using (var dlg = new OpenFileDialog
            {
                Title = L.S("Find bambu-studio.exe", "Find bambu-studio.exe"),
                Filter = "bambu-studio.exe|bambu-studio.exe|" + L.S("Programs", "Programmer") + " (*.exe)|*.exe",
                CheckFileExists = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                slicerPath = dlg.FileName;
            }
            UpdateStudio();
            FitHeight();
        }

        /// The example under the prices: an 8 h print with 150 g, with the answers so far (first printer's watts / wear).
        void UpdateExample()
        {
            if (example == null) return;
            var s = Apply();
            double kg, f, o, m, el;
            bool ok = ReadNumber(filamentText, out kg) & ReadNumber(friendsText, out f) & ReadNumber(othersText, out o) & ReadNumber(minText, out m);
            if (live) el = livePrice >= 0 && liveFor == area ? livePrice : start.LastElectricityPrice;
            else ok &= ReadNumber(elecText, out el);
            if (!ok)
            {
                example.Set(L.S("Example: an 8 h print with 150 g filament", "Eksempel: et print på 8 t med 150 g filament"),
                            L.S("Friends & family", "Venner og familie"), "–", L.S("Customers", "Kunder"), "–",
                            L.S("Fill in the boxes above to see the prices.", "Udfyld felterne ovenfor for at se priserne."));
                return;
            }
            if (!again)
            {
                var model = Wear.FindModel(models.Count > 0 ? models[0] : start.PrinterModel);
                s.PrinterWatts = model.DefaultWatts;
                s.MachineRatePerHour = Wear.DefaultRateFor(model.Key);
            }
            var res = Pricing.Calculate(new QuoteInput { Hours = 8, Minutes = 0, FilamentGrams = 150, FilamentDkkPerKg = kg, ElectricityDkkPerKwh = el }, s);
            example.Set(L.S("Example: an 8 h print with 150 g filament", "Eksempel: et print på 8 t med 150 g filament"),
                        L.S("Friends & family", "Venner og familie"), Num.Kr(res.FriendsPrice),
                        L.S("Customers", "Kunder"), Num.Kr(res.OthersPrice),
                        L.F("Material + power, plus your markup, plus printer wear ({0} kr/h).",
                            "Materiale + strøm, plus din avance, plus slid på printeren ({0} kr/t).", Num.N2(s.MachineRatePerHour)));
        }

        void UpdateSummary()
        {
            var s = Apply();
            string def = again ? L.S("unchanged", "uændret") : L.S("default", "standard");
            Action<int, string, int> set = (i, text, stepNo) =>
            {
                bool skip = skipped[stepNo] || (!done[stepNo] && !again);
                SetCut(sumValue[i], skip ? text + "  (" + def + ")" : text);
                sumValue[i].ForeColor = skip ? Theme.Muted : Theme.Text;
            };
            set(0, lang == "da" ? "Dansk" : "English", StepLanguage);
            var names = new List<string>();
            foreach (var p in s.Printers)
                names.Add(p.Id == s.ActivePrinterId && s.PrinterCount > 1 ? p.Name + " " + L.S("(in use)", "(i brug)") : p.Name);
            set(1, string.Join(", ", names.ToArray()), StepPrinter);
            string exe = BambuSlicer.FindExe(s.SlicerPath);
            SetCut(sumValue[2], exe != null ? L.S("✓ Found", "✓ Fundet") : L.S("Not found (type time and weight yourself)", "Ikke fundet (skriv selv tid og vægt)"));
            sumValue[2].ForeColor = exe != null ? Theme.Success : Theme.Warning;
            set(3, Num.Fmt(s.PriceFor(s.FilamentPreset)) + " kr/kg", StepCosts);
            set(4, s.AutoPowerPrice != 0 ? L.S("Live price, ", "Aktuel pris, ") + (string)cmbArea.Items[s.PriceArea == "DK2" ? 1 : 0]
                                         : L.F("Fixed, {0} kr/kWh", "Fast, {0} kr/kWh", Num.N2(s.LastElectricityPrice)), StepCosts);
            set(5, L.F("Friends & family {0} % · customers {1} %", "Venner og familie {0} % · kunder {1} %", Num.Fmt(s.FriendsMarkupPct), Num.Fmt(s.OthersMarkupPct)), StepPrices);
            set(6, s.MinimumPrice > 0 ? L.F("{0} kr. (customers)", "{0} kr. (kunder)", Num.Fmt(s.MinimumPrice)) : L.S("Off", "Fra"), StepPrices);
            set(7, s.QuoteName.Length > 0 ? s.QuoteName : L.S("none", "intet"), StepPrices);

            bool restart = again && AppSettings.IsDanish(lang) != L.Danish;
            if (again)
            {
                infoSummary.Kind = restart ? InfoKind.Warning : InfoKind.Success;
                infoSummary.SetText(restart ? L.S("PrintPrice restarts when you save, to switch the language.",
                                                  "PrintPrice genstarter, når du gemmer, for at skifte sprog.")
                                            : L.S("✓ Ready. Save to use these answers.", "✓ Klar. Gem for at bruge disse svar."),
                                    L.S("Everything else is in Settings.", "Alt andet ligger i Indstillinger."));
            }
            else
            {
                infoSummary.Kind = InfoKind.Success;
                infoSummary.SetText(L.S("✓ All set. Paste a Printables link or drop a model on the main window to get your first price.",
                                        "✓ Klar. Indsæt et Printables-link eller slip en model på hovedvinduet for at få din første pris."),
                                    L.S("Change any of this in Settings, or run this setup again from Settings > App.",
                                        "Ret det i Indstillinger, eller kør opsætningen igen fra Indstillinger > App."));
            }
        }

        /// Sets a summary value, cut with "…" to the column's width (the whole text in a dark tooltip when cut).
        void SetCut(Label l, string text)
        {
            int max = 0;
            foreach (Control c in sumTable.Controls)
                if (c is Theme.LinkText) { max = Px(ContentW) - capColW - c.PreferredSize.Width - c.Margin.Horizontal - 2; break; }
            string shown = text;
            if (max > 20 && TextRenderer.MeasureText(text, l.Font).Width > max)
            {
                int n = text.Length;
                while (n > 1 && TextRenderer.MeasureText(text.Substring(0, n).TrimEnd() + "…", l.Font).Width > max) n--;
                shown = text.Substring(0, n).TrimEnd() + "…";
            }
            l.Text = shown;
            tip.SetToolTip(l, shown != text ? text : null);
        }

        // ---------- steps ----------
        void ShowStep(int i)
        {
            step = Math.Max(0, Math.Min(StepSummary, i));
            for (int p = 0; p < pages.Length; p++) pages[p].Visible = p == step;
            string[] titles =
            {
                again ? L.S("Setup", "Opsætning") : L.S("Welcome to PrintPrice", "Velkommen til PrintPrice"),
                again ? L.S("Your printers", "Dine printere") : L.S("Your printer", "Din printer"),
                L.S("Filament and electricity", "Filament og strøm"),
                L.S("Your prices", "Dine priser"),
                again ? L.S("Check and save", "Tjek og gem") : L.S("Ready to go", "Klar til brug")
            };
            lblTitle.Text = titles[step];
            lblStep.Text = step == StepSummary ? L.S("Summary", "Oversigt") : L.F("Step {0} of {1}", "Trin {0} af {1}", step + 1, Questions);
            bar.Value = step;
            btnBack.Enabled = step > 0;
            btnSkip.Visible = step != StepSummary;
            btnNext.Text = step != StepSummary ? L.S("Next", "Næste")
                         : again ? L.S("Save", "Gem") : L.S("Start using PrintPrice", "Kom i gang");
            lnkLater.Visible = !again && step != StepSummary;
            if (step == StepCosts) FetchLive();
            if (step == StepSummary) UpdateSummary();
            FitButtons();
            if (IsHandleCreated && Visible && !offline && !Program.Hidden) FocusStep();
        }

        void FocusStep()
        {
            Control c = null;
            switch (step)
            {
                case StepLanguage: c = lang == "da" ? choiceDa : choiceEn; break;
                case StepPrinter: c = tiles[0]; break;
                case StepCosts: c = txtFilament; break;
                case StepPrices: c = txtFriends; break;
                default: c = btnNext; break;
            }
            if (c != null && c.CanFocus) c.Focus();
        }

        void SkipStep()
        {
            if (step >= Questions) return;
            ResetStep(step);
            FillStep(step);
            skipped[step] = true;
            done[step] = false;
            ShowStep(step + 1);
        }

        void NextStep()
        {
            if (step == StepSummary) { Finish(); return; }
            // a bad number stays on the step with the box red and focused
            TextBox[] boxes = step == StepCosts ? new[] { txtFilament, txtElec } : step == StepPrices ? new[] { txtFriends, txtOthers, txtMin } : new TextBox[0];
            foreach (var b in boxes)
                if (!Valid(b)) { b.Focus(); return; }
            done[step] = true;
            skipped[step] = false;
            ShowStep(step + 1);
        }

        void Finish()
        {
            Result = Apply();
            RestartRequested = again && AppSettings.IsDanish(lang) != L.Danish;
            finishing = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!finishing && !Restored)
            {
                if (again) { Result = null; DialogResult = DialogResult.Cancel; }
                else
                {
                    // Set up later / ✕ / Esc: the steps finished with Next stay, the others go back to the defaults
                    for (int i = 0; i < Questions; i++) if (!done[i]) ResetStep(i);
                    Result = Apply();
                    SetUpLater = true;
                    DialogResult = DialogResult.OK;
                }
            }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// First run: the language switches at once (the window is built again, answers kept).
        void LanguageChosen(string code)
        {
            if (filling || code == lang) return;
            lang = code;
            if (again) return;   // the running app keeps its language until the restart
            L.Danish = code == "da";
            BeginInvoke((MethodInvoker)RebuildForLanguage);   // not inside the choice's own event: it gets disposed
        }

        void RebuildForLanguage()
        {
            if (IsDisposed) return;
            var old = root;
            SuspendLayout();
            Controls.Remove(old);
            Build();
            old.Dispose();
            Fit();
            ShowStep(StepLanguage);
            ResumeLayout(true);
            if (!offline && !Program.Hidden) FocusStep();
        }

        /// First run: "Restore a backup…" = the Backup window on its Restore tab. A restore with settings ends the setup.
        void RestoreBackup()
        {
            if (offline) return;
            try
            {
                string problem;
                var history = History.Load(out problem) ?? new List<QuoteRecord>();
                using (var dlg = new BackupForm(history, start, BackupForm.TabRestore, false))
                {
                    RestoreResult got = null;
                    dlg.Restored = res => { got = res; };
                    dlg.ShowDialog(this);
                    if (got == null) return;
                    if (got.SettingsRestored)
                    {
                        Restored = true;
                        Result = got.Settings;
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }
                    // history only: the saved settings carry the merged invoice codes and printer ids now
                    if (got.Settings != null) { start = got.Settings.Clone(); opening = SetupAnswers.From(start, Mode); }
                    infoRestore.Kind = InfoKind.Success;
                    infoRestore.SetText(L.S("✓ Price history restored. Carry on with the setup.", "✓ Prishistorikken er gendannet. Fortsæt med opsætningen."), null);
                }
            }
            catch (Exception ex) { ErrorReport.Show(ex); }
        }

        // ---------- layout ----------
        void Fit()
        {
            int cw = Px(ContentW);
            int capW = 0;
            foreach (var c in captions)
                capW = Math.Max(capW, c.PreferredSize.Width + c.Margin.Horizontal);
            capColW = capW;
            foreach (var p in pages)
            {
                p.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, capW);
                p.MinimumSize = new Size(cw, 0);
                p.MaximumSize = new Size(cw, 0);
            }
            // the summary's captions line up with the boxes of the other steps
            sumTable.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, capW);
            sumTable.MinimumSize = new Size(cw, 0);
            sumTable.MaximumSize = new Size(cw, 0);
            foreach (RowStyle rs in sumTable.RowStyles) rs.Height = Px(28);   // Absolute rows aren't scaled by WinForms
            foreach (var b in fullBoxes) b.SetWidth(cw);
            infoWelcome.SetWidth(cw - logo.Width - logo.Margin.Right);
            int tileW = (cw - 2 * Px(TileGap)) / 3;
            foreach (var t in tiles) t.Size = new Size(tileW, Px(TileH));
            example.Size = new Size(cw, example.PreferredHeight());
            bar.Width = cw;
            separator.Width = cw;
            headPanel.MinimumSize = new Size(cw, 0);
            AlignStepBaseline();
            bottomPanel.MinimumSize = new Size(cw, 0);
            foreach (var b in new[] { btnRestore, btnFindStudio, btnLookAgain })
                b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + Px(24), txtFilament.Height);
            FitCombos();
            FitButtons();
            FitHeight();
        }

        /// "Step 2 of 4" (9 pt) on the title's (bold 12) baseline: both labels sit on the row's bottom, so the one whose
        /// baseline is lower gets the difference as bottom margin (baseline = label top + the font's ascent in px).
        void AlignStepBaseline()
        {
            lblTitle.Margin = Padding.Empty;
            lblStep.Margin = Padding.Empty;
            // > 0: the title's baseline is higher. The grey label draws with GDI+, 3 px higher than the metrics say
            // (measured in setup_1..5 at 100 %), so it goes 3 px (scaled) lower.
            int below = BelowBaseline(lblTitle) - BelowBaseline(lblStep) - Px(3);
            if (below > 0) lblStep.Margin = new Padding(0, 0, 0, below);
            else if (below < 0) lblTitle.Margin = new Padding(0, 0, 0, -below);
        }

        int BelowBaseline(Label l)
        {
            var f = l.Font;
            var fam = f.FontFamily;
            float ascent = f.SizeInPoints * DeviceDpi / 72f * fam.GetCellAscent(f.Style) / fam.GetEmHeight(f.Style);
            return l.PreferredSize.Height - l.Padding.Bottom - (int)Math.Round(l.Padding.Top + ascent);
        }

        void FitCombos()
        {
            int frame = cmbArea.Height - cmbArea.ItemHeight;
            if (cmbArea.DrawMode == DrawMode.OwnerDrawFixed && txtFilament.Height - frame > 0) cmbArea.ItemHeight = txtFilament.Height - frame;
        }

        /// Back / Skip / Next: one width (text + 24, at least 75); the summary's action button as wide as its text.
        void FitButtons()
        {
            int w = Px(ButtonMinW);
            foreach (var b in new[] { btnBack, btnSkip })
                w = Math.Max(w, TextRenderer.MeasureText(b.Text, b.Font).Width + Px(24));
            w = Math.Max(w, TextRenderer.MeasureText(L.S("Next", "Næste"), btnNext.Font).Width + Px(24));
            btnBack.Size = btnSkip.Size = new Size(w, Px(ButtonH));
            btnNext.Size = new Size(Math.Max(w, TextRenderer.MeasureText(btnNext.Text, btnNext.Font).Width + Px(24)), Px(ButtonH));
        }

        /// The host is as high as the tallest step (Bambu Studio's longer "not found" state counted), never lower.
        void FitHeight()
        {
            string exe = BambuSlicer.FindExe(slicerPath);
            int h = hostH;
            foreach (var p in pages) p.Visible = true;   // measured with every row taking part
            for (int pass = 0; pass < 2; pass++)
            {
                ShowStudio(pass == 0 ? null : exe);
                foreach (var p in pages)
                {
                    p.PerformLayout();
                    h = Math.Max(h, p.GetPreferredSize(new Size(Px(ContentW), 0)).Height);
                }
            }
            for (int i = 0; i < pages.Length; i++) { pages[i].Visible = i == step; pages[i].Location = Point.Empty; }
            hostH = h;
            host.Size = new Size(Px(ContentW), h);
        }

        /// Picture mode: answers for the summary picture (A1 + A1 mini ticked, every step finished with Next).
        internal void SampleAnswers()
        {
            if (!models.Contains(Wear.ModelA1Mini)) models.Add(Wear.ModelA1Mini);
            foreach (var tile in tiles) UpdateTile(tile);
            for (int i = 0; i < Questions; i++) done[i] = true;
            UpdateExample();
        }

        /// Picture mode: the printer step as it looks without Bambu Studio.
        internal void SampleStudioMissing(bool missing)
        {
            if (missing) ShowStudio(null); else UpdateStudio();
        }

        /// Picture mode / tests: shows a step.
        internal void SampleStep(int i) { ShowStep(i); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (tip != null) tip.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------- small controls ----------
        /// 4 equal parts, 6 px apart: done / current InfoAccent, coming Border; the summary = all four.
        sealed class StepBar : Control
        {
            int value;
            public int Value { get { return value; } set { this.value = value; Invalidate(); } }

            public StepBar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Back);
                int gap = (int)Math.Round(6 * DeviceDpi / 96f);
                int w = (Width - 3 * gap) / Questions;
                for (int i = 0; i < Questions; i++)
                {
                    int x = i * (w + gap), right = i == Questions - 1 ? Width : x + w;
                    using (var b = new SolidBrush(i <= value ? Theme.InfoAccent : Theme.Border))
                        g.FillRectangle(b, x, 0, right - x, Height);
                }
            }
        }

        /// The app's logo (the exe's largest icon), drawn whole and smooth.
        sealed class LogoView : Control
        {
            public LogoView()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Back);
                var img = MainForm.Logo;
                if (img == null) return;
                var st = g.Save();
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                int side = Math.Min(Width, Height);
                g.DrawImage(img, new Rectangle((Width - side) / 2, (Height - side) / 2, side, side));
                g.Restore(st);
            }
        }

        /// The example prices: looks like an InfoBox (InfoBack, Border frame, 4 px InfoAccent stripe), with the
        /// friends price in Friends green and the customer price in Others red, bold, right-aligned.
        sealed class ExampleView : Control
        {
            string head = "", friendsCap = "", friends = "", othersCap = "", others = "", note = "";
            Font font, bold;

            public ExampleView()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
                font = new Font("Segoe UI", 9f);
                bold = new Font("Segoe UI", 9f, FontStyle.Bold);
            }

            int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }
            int LineH { get { return TextRenderer.MeasureText("Ag", font).Height; } }

            // padding 7 + head + 4 + row + 2 + row + 4 + note + padding 7, plus the 1 px frame top and bottom
            /// "50,30 kr. | 64,95 kr." (--test-setup)
            public string Prices { get { return friends + " | " + others; } }

            public int PreferredHeight() { return 4 * LineH + Px(24) + 2; }

            public void Set(string head, string friendsCap, string friends, string othersCap, string others, string note)
            {
                this.head = head; this.friendsCap = friendsCap; this.friends = friends;
                this.othersCap = othersCap; this.others = others; this.note = note;
                AccessibleName = head + ". " + friendsCap + " " + friends + ", " + othersCap + " " + others + ". " + note;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.InfoBack);
                using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                using (var b = new SolidBrush(Theme.InfoAccent)) g.FillRectangle(b, 0, 0, 4, Height);
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                int x = Px(12) + 1, right = Width - Px(10) - 1, y = Px(7) + 1, lh = LineH;
                TextRenderer.DrawText(g, head, font, new Rectangle(x, y, right - x, lh), Theme.Text, flags);
                y += lh + Px(4);
                TextRenderer.DrawText(g, friendsCap, font, new Rectangle(x, y, right - x, lh), Theme.Text, flags);
                TextRenderer.DrawText(g, friends, bold, new Rectangle(x, y, right - x, lh), Theme.Friends, flags | TextFormatFlags.Right);
                y += lh + Px(2);
                TextRenderer.DrawText(g, othersCap, font, new Rectangle(x, y, right - x, lh), Theme.Text, flags);
                TextRenderer.DrawText(g, others, bold, new Rectangle(x, y, right - x, lh), Theme.Others, flags | TextFormatFlags.Right);
                y += lh + Px(4);
                TextRenderer.DrawText(g, note, font, new Rectangle(x, y, right - x, lh), Theme.Muted, flags);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { font.Dispose(); bold.Dispose(); }
                base.Dispose(disposing);
            }
        }

        /// A printer model to tick: InfoBack tile, Border frame (HotBack + HotBorder on hover), ticked = 2 px InfoAccent
        /// frame + tick; "In use" tag (InfoAccent) on the first ticked; re-run: models already added are locked ("Added").
        /// Tab stop: Space toggles, dotted focus rectangle 5 px inside. Picture = Bambu Studio's, else PrinterArt.
        sealed class Tile : Control
        {
            public string Model, Title = "", Sub = "", InUseText = "", AddedText = "";
            public bool Ticked, Locked, InUse;
            Bitmap photo;
            bool hot;

            public Tile()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.CheckButton;
            }

            public void SetPhoto(Bitmap b)
            {
                if (photo != null) photo.Dispose();
                photo = b;
                Invalidate();
            }

            int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
            protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space) { e.Handled = true; e.SuppressKeyPress = true; OnClick(EventArgs.Empty); return; }
                base.OnKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                bool hover = hot && !Locked;
                g.Clear(hover ? Theme.HotBack : Theme.InfoBack);
                if (Ticked)
                    using (var pen = new Pen(Theme.InfoAccent, Px(2)) { Alignment = PenAlignment.Inset })
                        g.DrawRectangle(pen, 0, 0, Width, Height);
                else
                    using (var pen = new Pen(hover || Focused ? Theme.HotBorder : Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

                // picture box at the top: the whole picture, centred
                var pic = new Rectangle(Px(10), Px(10), Width - Px(20), Px(84));
                var st = g.Save();
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (photo != null)
                {
                    float sc = Math.Min(pic.Width / (float)photo.Width, pic.Height / (float)photo.Height);
                    int w = (int)(photo.Width * sc), h = (int)(photo.Height * sc);
                    g.DrawImage(photo, new Rectangle(pic.Left + (pic.Width - w) / 2, pic.Top + (pic.Height - h) / 2, w, h));
                }
                else
                {
                    int side = pic.Height;
                    PrinterArt.Draw(g, new Rectangle(pic.Left + (pic.Width - side) / 2, pic.Top, side, side), Model);
                }
                g.Restore(st);

                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis;
                using (var bold = new Font(Font, FontStyle.Bold))
                    TextRenderer.DrawText(g, Title, bold, new Rectangle(Px(4), Px(100), Width - Px(8), Px(20)), Locked ? Theme.Muted : Theme.Text, flags);
                using (var small = new Font("Segoe UI", 9f))
                {
                    TextRenderer.DrawText(g, Sub, small, new Rectangle(Px(4), Px(121), Width - Px(8), Px(18)), Theme.Muted, flags);
                    // tick box top left (BackupForm.Tick look)
                    int d = Px(13), bx = Px(8), by = Px(8);
                    using (var b = new SolidBrush(Theme.Input)) g.FillRectangle(b, bx, by, d, d);
                    using (var p = new Pen(hover || Focused ? Theme.HotBorder : Theme.Border)) g.DrawRectangle(p, bx, by, d - 1, d - 1);
                    if (Ticked)
                    {
                        var s2 = g.Save();
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var p = new Pen(Locked ? Theme.Muted : Theme.Text, Math.Max(1.5f, d / 8f)))
                            g.DrawLines(p, new[] { new PointF(bx + d * 0.22f, by + d * 0.52f), new PointF(bx + d * 0.42f, by + d * 0.72f), new PointF(bx + d * 0.78f, by + d * 0.3f) });
                        g.Restore(s2);
                    }
                    // tag top right
                    string tag = Locked ? AddedText : InUse ? InUseText : null;
                    if (!string.IsNullOrEmpty(tag))
                    {
                        var size = TextRenderer.MeasureText(tag, small, Size.Empty, TextFormatFlags.NoPadding);
                        var r = new Rectangle(Width - size.Width - Px(14), 0, size.Width + Px(14), size.Height + Px(4));
                        using (var b = new SolidBrush(Locked ? Theme.ButtonBack : Theme.InfoAccent)) g.FillRectangle(b, r);
                        TextRenderer.DrawText(g, tag, small, r, Locked ? Theme.Muted : Color.White,
                                              TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                }
                if (Focused && ShowFocusCues)
                    using (var pen = new Pen(Theme.Text) { DashStyle = DashStyle.Dot })
                        g.DrawRectangle(pen, Px(5), Px(5), Width - 1 - Px(10), Height - 1 - Px(10));
                AccessibleDescription = (Ticked ? L.S("ticked", "valgt") : L.S("not ticked", "ikke valgt")) + (Locked ? ", " + AddedText : InUse ? ", " + InUseText : "");
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && photo != null) { photo.Dispose(); photo = null; }
                base.Dispose(disposing);
            }
        }
    }
}
