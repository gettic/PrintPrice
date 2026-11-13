using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Backup window (idea 27), opened from History (Backup…) and Settings > App (Back up… / Restore…). Two tabs:
    /// Back up = the price history + settings to a .printprice-backup file (History.WriteBackupFile), or online with a
    /// code (OnlineBackup.Upload; the code is remembered in the settings, DPAPI); Restore = from a file or an online code
    /// (OnlineBackup.Download), Merge or Replace, "Also restore the settings". History.Restore makes the safety copies
    /// and saves; Restored hands the result to MainForm. The result replaces the Restore tab's content.
    /// Online calls run with await Task.Run; the 5-a-day upload cap shows a countdown in place of "Back up online".
    internal class BackupForm : CaptionForm
    {
        public const int TabBackup = 0, TabRestore = 1;
        /// Called right after a restore saved the files: MainForm takes the new history (in place) and settings.
        public Action<RestoreResult> Restored;
        /// True when the window closed with "Restart now" (restored settings in another language).
        public bool RestartRequested;

        // the online parts are on (false = greyed out, e.g. if the service ever has to be switched off)
        static readonly bool OnlineReady = true;
        // the daily upload cap was reached: no upload until then (UTC; MinValue = none). For this run only.
        static DateTime limitUntilUtc = DateTime.MinValue;

        // layout (96 dpi units)
        const int WindowW = 520, MinWindowH = 480, Edge = 14, StripTop = 6, StripH = 28, PageTop = 44, SepGap = 9,
                  ButtonH = 32, Indent = 22, CodeH = 44;

        readonly List<QuoteRecord> list;   // MainForm's history (a restore replaces its contents in place)
        AppSettings settings;               // MainForm's settings (the restored ones after a restore)
        readonly bool offline;              // picture mode: never writes, never opens dialogs
        readonly Panel root, separator;
        readonly SettingsForm.TabStrip strip;
        readonly TableLayoutPanel pageBackup, pageCode, pageRestore, pageDone, rowFile, rowCode, rowShowCode;
        readonly TableLayoutPanel[] pages;
        readonly Button btnAction, btnClose, btnBrowse, btnGet, btnCopy, btnShowCode, btnNewCode, btnDeleteOnline;
        readonly FlowLayoutPanel rowCodeButtons;   // Show code / New code… / Delete online backup… (a code is remembered)
        readonly QuoteForm.Choice rbFile, rbOnline, rbFromFile, rbFromCode, rbMerge, rbReplace;
        readonly InfoBox infoContents, infoMode, infoResult, infoSummary, infoNote;
        readonly InfoBox infoCodeOk, infoCodeLeft, infoCodeWarn, infoCodeNext;
        readonly InfoBox doneMain, doneClash, doneMoved, doneSafety, doneLang;
        readonly TextBox txtFile;
        readonly SettingsForm.CueBox txtCode;
        readonly Tick chkSettings, chkKeep;
        readonly Label lblSettingsNote, lblCode;
        readonly Timer clock = new Timer { Interval = 1000 };   // the daily-cap countdown
        BackupData fileData;     // the backup file chosen on the Restore tab (null = none yet)
        string fileProblem;      // why the chosen file can't be restored (null = none)
        BackupData codeData;     // the online backup fetched with Get backup (null = none yet)
        string codeProblem;      // why it couldn't be fetched / read (null = none)
        string codeLoaded = "";  // the normalised code codeData came from
        string shownCode = "";   // the code on the code page
        bool codeShown, doneShown, fitted, measured, busy, keptCode;
        string busyText;         // the action button's text while busy ("Backing up…")
        TableLayoutPanel current;   // the page that shows (ShowPage)
        readonly int[] pageH = new int[4];   // each page's height when it last showed (FitHeight)
        int tab;

        public BackupForm(List<QuoteRecord> history, AppSettings current, int startTab, bool offline)
        {
            list = history ?? new List<QuoteRecord>();
            settings = current ?? new AppSettings();
            this.offline = offline;
            tab = startTab == TabRestore ? TabRestore : TabBackup;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = L.S("Backup", "Sikkerhedskopi");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            if (Program.Hidden || Screenshots.Active) Screenshots.Place(this);   // CenterParent would pull it back on screen
            ClientSize = new Size(WindowW - 2, MinWindowH);   // real size set on Load (measured)

            root = new Panel { Margin = Padding.Empty };
            Controls.Add(root);
            strip = new SettingsForm.TabStrip(new[] { L.S("Back up", "Tag kopi"), L.S("Restore", "Gendan") }) { TabIndex = 0 };
            root.Controls.Add(strip);

            // ----- Back up -----
            pageBackup = Page(1);
            pageBackup.Controls.Add(Heading(L.S("Back up the price history to", "Tag kopi af prishistorikken til"), 0));
            var grpTo = Group();
            rbFile = new QuoteForm.Choice { Text = L.S("A file (.printprice-backup)", "En fil (.printprice-backup)"), Checked = true };
            rbOnline = new QuoteForm.Choice { Text = L.S("Online, with a code", "Online, med en kode"), Enabled = OnlineReady };
            grpTo.Controls.Add(rbFile);
            grpTo.Controls.Add(rbOnline);
            pageBackup.Controls.Add(grpTo);
            infoContents = Info(8);
            infoMode = Info(8);
            infoResult = Info(8);
            pageBackup.Controls.Add(infoContents);
            pageBackup.Controls.Add(infoMode);
            // a code is remembered: Show code (no network) / New code… / Delete online backup…
            rowCodeButtons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                                                   Margin = new Padding(0, 8, 0, 0), Padding = Padding.Empty };
            btnShowCode = SmallButton(L.S("Show code", "Vis kode"));
            btnNewCode = SmallButton(L.S("New code…", "Ny kode…"));
            btnDeleteOnline = SmallButton(L.S("Delete online backup…", "Slet online-kopi…"));
            btnShowCode.Margin = Padding.Empty;
            btnShowCode.Click += delegate { ShowRememberedCode(); };
            btnNewCode.Click += delegate { NewCodeClicked(); };
            btnDeleteOnline.Click += delegate { DeleteOnlineClicked(); };
            rowCodeButtons.Controls.Add(btnShowCode);
            rowCodeButtons.Controls.Add(btnNewCode);
            rowCodeButtons.Controls.Add(btnDeleteOnline);
            pageBackup.Controls.Add(rowCodeButtons);
            pageBackup.Controls.Add(infoResult);
            rbFile.CheckedChanged += delegate { infoResult.Text = ""; UpdateBackup(); };

            // ----- the code after an online backup (reached once the backup service is there; picture mode shows it) -----
            pageCode = Page(1);
            infoCodeOk = Info(0);
            infoCodeOk.Kind = InfoKind.Success;
            pageCode.Controls.Add(infoCodeOk);
            pageCode.Controls.Add(Heading(L.S("Your backup code", "Din kode til kopien"), 14));
            rowShowCode = Row();
            lblCode = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false, Margin = Padding.Empty,
                                  Font = new Font("Segoe UI", 17f, FontStyle.Bold), Size = new Size(380, CodeH) };
            lblCode.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, lblCode.Width - 1, lblCode.Height - 1); };
            btnCopy = new Button { Text = L.S("Copy", "Kopiér"), Size = new Size(90, CodeH), Margin = new Padding(6, 0, 0, 0), Padding = Padding.Empty, UseMnemonic = false };
            btnCopy.Click += delegate { CopyCode(); };
            rowShowCode.Controls.Add(lblCode, 0, 0);
            rowShowCode.Controls.Add(btnCopy, 1, 0);
            pageCode.Controls.Add(rowShowCode);
            infoCodeLeft = Info(10);
            infoCodeLeft.Kind = InfoKind.Warning;
            pageCode.Controls.Add(infoCodeLeft);
            infoCodeWarn = Info(10);
            infoCodeWarn.Kind = InfoKind.Warning;
            infoCodeNext = Info(8);
            pageCode.Controls.Add(infoCodeWarn);
            pageCode.Controls.Add(infoCodeNext);

            // ----- Restore -----
            pageRestore = Page(2);
            pageRestore.Controls.Add(Heading(L.S("Restore from", "Gendan fra"), 0));
            var grpFrom = Group();
            rbFromFile = new QuoteForm.Choice { Text = L.S("A file", "En fil"), Checked = true };
            rowFile = Row();
            rowFile.Margin = new Padding(Indent, 2, 0, 3);
            txtFile = new TextBox { ReadOnly = true, Width = 300, Margin = Padding.Empty, TabStop = false,
                                    AccessibleName = L.S("Backup file", "Kopi-fil") };
            btnBrowse = SmallButton(L.S("Browse…", "Gennemse…"));
            btnBrowse.Click += delegate { Browse(); };
            rowFile.Controls.Add(txtFile, 0, 0);
            rowFile.Controls.Add(btnBrowse, 1, 0);
            rbFromCode = new QuoteForm.Choice { Text = L.S("An online code", "En online-kode"), Enabled = OnlineReady };
            rowCode = Row();
            rowCode.Margin = new Padding(Indent, 2, 0, 0);
            txtCode = new SettingsForm.CueBox { Cue = "XXXXX-XXXXX-XXXXX-XXXXX", Width = 300, Margin = Padding.Empty, MaxLength = 40,
                                                CharacterCasing = CharacterCasing.Upper, AccessibleName = L.S("Backup code", "Kode til kopien") };
            txtCode.TextChanged += delegate { UpdateRestore(); };
            txtCode.Enter += delegate { UpdateAccept(); };
            txtCode.Leave += delegate { UpdateAccept(); };
            txtCode.Leave += delegate { string n = Backup.Normalize(txtCode.Text); if (n != null) txtCode.Text = Backup.Format(n); };
            btnGet = SmallButton(L.S("Get backup", "Hent kopi"));
            btnGet.Click += delegate { GetBackup(); };
            rowCode.Controls.Add(txtCode, 0, 0);
            rowCode.Controls.Add(btnGet, 1, 0);
            // after a restore from a code: back up to that code from now on (sets the remembered code)
            chkKeep = new Tick { Text = L.S("Keep backing up to this code", "Bliv ved med at tage kopi til denne kode"), Checked = true,
                                 Margin = new Padding(Indent, 2, 0, 0) };
            string remembered = settings.GetBackupCode();
            if (remembered.Length > 0) txtCode.Text = Backup.Format(remembered);
            grpFrom.Controls.Add(rbFromFile);
            grpFrom.Controls.Add(rowFile);
            grpFrom.Controls.Add(rbFromCode);
            grpFrom.Controls.Add(rowCode);
            grpFrom.Controls.Add(chkKeep);
            pageRestore.Controls.Add(grpFrom);
            infoSummary = Info(10);
            pageRestore.Controls.Add(infoSummary);
            pageRestore.Controls.Add(Heading(L.S("How", "Sådan"), 6));
            var grpHow = Group();
            rbMerge = new QuoteForm.Choice { Checked = list.Count > 0 };
            rbReplace = new QuoteForm.Choice { Text = L.S("Replace this PC's history with the backup", "Erstat denne pc's historik med kopien"), Checked = list.Count == 0 };
            grpHow.Controls.Add(rbMerge);
            grpHow.Controls.Add(rbReplace);
            pageRestore.Controls.Add(grpHow);
            chkSettings = new Tick { Text = L.S("Also restore the settings", "Gendan også indstillingerne"),
                                         Checked = list.Count == 0, Margin = new Padding(0, 6, 0, 0) };
            pageRestore.Controls.Add(chkSettings);
            lblSettingsNote = new Theme.PlainLabel { AutoSize = true, Tag = "muted", UseMnemonic = false, Font = new Font("Segoe UI", 9f),
                                                     Margin = new Padding(Tick.TextX, 0, 0, 0) };
            pageRestore.Controls.Add(lblSettingsNote);
            infoNote = Info(10);
            pageRestore.Controls.Add(infoNote);
            rbFromFile.CheckedChanged += delegate { UpdateRestore(); };
            rbReplace.CheckedChanged += delegate { UpdateRestore(); };
            chkSettings.CheckedChanged += delegate { UpdateRestore(); };

            // ----- after a restore -----
            pageDone = Page(2);
            doneMain = Info(0);
            doneClash = Info(8);
            doneMoved = Info(8);
            doneSafety = Info(8);
            doneLang = Info(8);
            foreach (var b in new[] { doneMain, doneClash, doneMoved, doneSafety, doneLang }) pageDone.Controls.Add(b);

            pages = new[] { pageBackup, pageCode, pageRestore, pageDone };
            foreach (var p in pages) root.Controls.Add(p);

            // ----- bottom: line, then the action (accent) + Close -----
            separator = new Panel { Height = 1 };
            root.Controls.Add(separator);
            btnAction = new Button { Text = "", TabIndex = 3, Padding = Padding.Empty, UseMnemonic = false };
            btnAction.Click += delegate { Action(); };
            btnClose = new Button { Text = L.S("Close", "Luk"), DialogResult = DialogResult.Cancel, TabIndex = 4, Padding = Padding.Empty };
            root.Controls.Add(btnAction);
            root.Controls.Add(btnClose);
            CancelButton = btnClose;
            strip.SelectedChanged += delegate { tab = strip.Selected; if (fitted) ShowPage(); };

            Theme.Apply(this);
            separator.BackColor = Theme.Border;   // after Apply, which paints panels in the background colour
            lblCode.BackColor = Theme.InfoBack;
            foreach (var p in pages) p.ResumeLayout(true);
            ResumeLayout(true);
            UpdateBackup();
            UpdateRestore();
            strip.Selected = tab;
            Load += delegate { FitLayout(); };
            clock.Tick += delegate { ClockTick(); };
            clock.Start();
            FormClosed += delegate { clock.Stop(); clock.Dispose(); };
        }

        int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override bool ShowWithoutActivation { get { return Screenshots.Active || Program.Hidden; } }

        /// The tab that shows (TabBackup / TabRestore); also before the window is shown.
        public int SelectedTab
        {
            get { return tab; }
            set { if (value == TabBackup || value == TabRestore) { tab = value; strip.Selected = value; if (fitted) ShowPage(); } }
        }

        // ---------- building blocks ----------
        static TableLayoutPanel Page(int tabIndex)
        {
            var p = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty,
                                           Padding = Padding.Empty, TabIndex = tabIndex, Visible = false, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
            p.SuspendLayout();
            return p;
        }

        Label Heading(string text, int top)
        {
            return new Label { Text = text, AutoSize = true, UseMnemonic = false, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, top, 0, 4) };
        }

        static FlowLayoutPanel Group()
        {
            return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                         WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        }

        /// Box (or code) + button beside it, 6 px apart.
        static TableLayoutPanel Row()
        {
            var t = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        /// Box-high button beside a text box (Segoe UI 9 like Settings' Add… / Remove: fits with its descenders).
        static Button SmallButton(string text)
        {
            return new Button { Text = text, Size = new Size(90, 25), Margin = new Padding(6, 0, 0, 0), Padding = Padding.Empty, UseMnemonic = false,
                                Font = new Font("Segoe UI", 9f) };
        }

        static InfoBox Info(int top)
        {
            return new InfoBox(400, 9.5f) { Margin = new Padding(0, top, 0, 0) };
        }

        static string Quotes(int n)
        {
            string c = n.ToString("N0", Num.Dk);
            return n == 1 ? L.S("1 quote", "1 tilbud") : L.F("{0} quotes", "{0} tilbud", c);
        }

        // ---------- layout ----------
        /// Load: 520 wide; pages as wide as the content; boxes and buttons measured; height from the tallest page.
        void FitLayout()
        {
            Width = Px(WindowW);
            int cw = ClientSize.Width - 2 * Px(Edge);
            // box-high buttons: one width for both (text + 24 px, like Settings' Add… / Remove)
            int sw = Px(24);
            foreach (var s in new[] { btnBrowse.Text, btnGet.Text, GettingText, ReadingText })
                sw = Math.Max(sw, TextRenderer.MeasureText(s, btnGet.Font).Width + Px(24));
            foreach (var b in new[] { btnShowCode, btnNewCode, btnDeleteOnline })
                b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + Px(24), txtFile.Height);
            foreach (var b in new[] { btnBrowse, btnGet }) b.Size = new Size(sw, txtFile.Height);
            int rowW = cw - Px(Indent);
            txtFile.Width = txtCode.Width = rowW - Px(6) - sw;
            // Copy keeps its width when it says "✓ Copied"
            int cpw = Math.Max(Px(90), Math.Max(TextRenderer.MeasureText(btnCopy.Text, btnCopy.Font).Width, TextRenderer.MeasureText(CopiedText, btnCopy.Font).Width) + Px(28));
            btnCopy.Size = new Size(cpw, Px(CodeH));
            lblCode.Size = new Size(cw - Px(6) - cpw, Px(CodeH));
            foreach (var p in pages)
            {
                p.SuspendLayout();
                p.MinimumSize = new Size(cw, 0);
                p.MaximumSize = new Size(cw, 0);
                FitChildren(p, cw);
                p.ResumeLayout(true);
                p.SizeChanged += delegate { if (fitted) FitHeight(); };   // longer texts: grow, never cut off
            }
            // the action button fits all its texts (no jump between file / online / restore)
            int aw = Px(130);
            foreach (var s in new[] { FileText, OnlineText, RestoreText, BackText, BackingUpText, DeletingText, CountdownText(new TimeSpan(23, 59, 59)) })
                aw = Math.Max(aw, TextRenderer.MeasureText(s, btnAction.Font).Width + Px(28));
            btnAction.Size = new Size(aw, Px(ButtonH));
            btnClose.Size = new Size(Math.Max(Px(90), TextRenderer.MeasureText(btnClose.Text, btnClose.Font).Width + Px(28)), Px(ButtonH));
            fitted = true;
            foreach (var p in pages) p.Visible = true;   // measured while shown, then ShowPage keeps one
            // the Restore tab at its tallest (Replace's warning is longer than the safety note): no jump when Replace is chosen
            if (list.Count > 0) { infoNote.Kind = InfoKind.Warning; infoNote.Text = ReplaceNote(); }
            chkKeep.Visible = true;   // (from a code) also at its tallest
            FitHeight();
            UpdateRestore();
            ShowPage();
        }

        void FitChildren(Control c, int cw)
        {
            foreach (Control child in c.Controls)
            {
                var info = child as InfoBox;
                if (info != null) { info.SetWidth(cw - info.Margin.Horizontal); continue; }
                var choice = child as QuoteForm.Choice;
                if (choice != null) { choice.Size = new Size(cw, Px(28)); continue; }
                if (child is Tick) { child.Size = new Size(cw - child.Margin.Left, Px(26)); continue; }
                if (child is FlowLayoutPanel) FitChildren(child, cw);
            }
        }

        /// Window height = the tallest page + strip + bottom row; only grows (no jump between tabs). A page is measured
        /// while it shows (on Load all of them show for a moment); hidden pages keep their last height.
        void FitHeight()
        {
            int cw = ClientSize.Width - 2 * Px(Edge), tallest = 0;
            for (int i = 0; i < pages.Length; i++)
            {
                if (!measured || pages[i] == current) pageH[i] = pages[i].GetPreferredSize(new Size(cw, 0)).Height;
                tallest = Math.Max(tallest, pageH[i]);
            }
            int need = CaptionHeight + Px(PageTop) + tallest + Px(16) + 1 + Px(SepGap) + Px(ButtonH) + Px(Edge);
            int h = SizeFromClientSize(new Size(ClientSize.Width, need)).Height;
            if (h > Height || !measured) Height = h;
            measured = true;
            PlaceAll();
        }

        void PlaceAll()
        {
            int m = Px(Edge), w = ClientSize.Width, cw = w - 2 * m;
            root.SetBounds(0, CaptionHeight, w, ClientSize.Height - CaptionHeight);
            strip.SetBounds(m, Px(StripTop), cw, Px(StripH));
            foreach (var p in pages) p.Location = new Point(m, Px(PageTop));
            int by = root.Height - m - btnClose.Height, x = w - m - btnClose.Width;
            btnClose.Location = new Point(x, by);
            btnAction.Location = new Point(x - Px(6) - btnAction.Width, by);
            separator.SetBounds(m, by - Px(SepGap) - 1, cw, 1);
        }

        /// Shows the page for the tab and its state; the action button follows it.
        void ShowPage()
        {
            var show = tab == TabBackup ? (codeShown ? pageCode : pageBackup) : (doneShown ? pageDone : pageRestore);
            bool focusLost = false;
            foreach (var p in pages) if (p != show && p.ContainsFocus) focusLost = true;
            show.Visible = true;
            current = show;
            foreach (var p in pages) if (p != show) p.Visible = false;
            if (focusLost) strip.Focus();
            UpdateAction();
            if (measured) FitHeight();   // a page that changed while hidden
        }

        string FileText { get { return L.S("Save file…", "Gem fil…"); } }
        string OnlineText { get { return L.S("Back up online", "Tag kopi online"); } }
        string RestoreText { get { return L.S("Restore", "Gendan"); } }
        static string BackText { get { return L.S("Back", "Tilbage"); } }
        static string BackingUpText { get { return L.S("Backing up…", "Tager kopi…"); } }
        static string DeletingText { get { return L.S("Deleting…", "Sletter…"); } }
        static string GettingText { get { return L.S("Getting…", "Henter…"); } }
        static string ReadingText { get { return L.S("Reading…", "Læser…"); } }
        static string CopiedText { get { return L.S("✓ Copied", "✓ Kopieret"); } }

        /// "Next online backup in 3:20:15" (the countdown in place of "Back up online" while the daily cap is reached).
        static string CountdownText(TimeSpan left)
        {
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            string t = ((int)left.TotalHours).ToString(Num.Dk) + ":" + left.Minutes.ToString("00") + ":" + left.Seconds.ToString("00");
            return L.F("Next online backup in {0}", "Næste online-kopi om {0}", t);
        }

        /// The daily-limit box: no time and no "tomorrow" (the reset may be hours away); the button counts down.
        static string LimitBoxText()
        {
            return L.F("Daily limit reached ({0} online backups a day).", "Dagens grænse er nået ({0} online-kopier om dagen).",
                       OnlineBackup.DailyUploads);
        }

        static bool LimitActive { get { return limitUntilUtc > DateTime.UtcNow; } }

        static DateTime NextUtcMidnight() { return DateTime.UtcNow.Date.AddDays(1); }

        /// The bottom button: Save file… / Back up online (or the countdown) / Restore (accent), Back on the code page
        /// (normal look), hidden on the result page (Close is the default there). Busy = its busy text, greyed out.
        /// Greyed out = a normal button's look (no red behind muted text).
        void UpdateAction()
        {
            bool code = tab == TabBackup && codeShown, done = tab == TabRestore && doneShown;
            btnAction.Visible = !done;
            string text;
            bool on, accent = true;
            if (busy) { text = busyText ?? BackingUpText; on = false; }
            else if (code) { text = BackText; on = true; accent = false; }
            else if (tab == TabRestore) { text = RestoreText; on = CurrentData != null; }
            else if (rbOnline.Checked && LimitActive) { text = CountdownText(limitUntilUtc - DateTime.UtcNow); on = false; }
            else { text = rbOnline.Checked ? OnlineText : FileText; on = rbFile.Checked || OnlineReady; }
            if (btnAction.Text != text) btnAction.Text = text;
            if (btnAction.Enabled != on) btnAction.Enabled = on;
            Theme.Apply(btnAction);
            if (on && accent) Theme.AccentButton(btnAction);
            UpdateAccept();
        }

        /// Enter: Close on the result page, Copy on the code page, Get backup while the code box has the focus
        /// (and can be pressed), else the bottom button when it is on.
        void UpdateAccept()
        {
            if (btnAction == null || btnGet == null) return;
            bool code = tab == TabBackup && codeShown, done = tab == TabRestore && doneShown;
            if (done) AcceptButton = btnClose;
            else if (code) AcceptButton = btnCopy;
            else if (tab == TabRestore && txtCode.ContainsFocus && btnGet.Enabled) AcceptButton = btnGet;
            else AcceptButton = btnAction.Enabled ? btnAction : null;
        }

        /// Every second: the countdown on the button; at the reset time it turns back into "Back up online".
        void ClockTick()
        {
            if (limitUntilUtc == DateTime.MinValue) return;
            if (!LimitActive)
            {
                limitUntilUtc = DateTime.MinValue;
                if (infoResult.Kind == InfoKind.Error && rbOnline.Checked) infoResult.Text = "";   // the limit text
                UpdateBackup();
                return;
            }
            if (tab == TabBackup && !codeShown && rbOnline.Checked && !busy) UpdateAction();
        }

        /// While an online call runs: the window's inputs are greyed out, the button (or Get backup) says what is going on.
        void SetBusy(bool on, string text)
        {
            busy = on;
            busyText = text;
            foreach (Control c in new Control[] { strip, rbFile, rbOnline, btnShowCode, btnNewCode, btnDeleteOnline, rbFromFile, rbFromCode,
                                                  btnBrowse, txtCode, btnGet, rbMerge, rbReplace, chkSettings, chkKeep })
                c.Enabled = !on;
            UseWaitCursor = on;
            if (!on) { UpdateBackup(); UpdateRestore(); }
            else UpdateAction();
        }

        // ---------- Back up ----------
        void UpdateBackup()
        {
            infoContents.Text = list.Count == 0
                ? L.S("The backup holds your settings (prices, printers, invoice details; no quotes yet), so it includes payment details.",
                      "Kopien indeholder dine indstillinger (priser, printere, fakturaoplysninger; ingen tilbud endnu), så betalingsoplysninger er med.")
                : list.Count == 1
                ? L.S("The backup holds your 1 quote and your settings (prices, printers, invoice details), so it includes customer names and payment details.",
                      "Kopien indeholder dit ene tilbud og dine indstillinger (priser, printere, fakturaoplysninger), så kundenavne og betalingsoplysninger er med.")
                : L.F("The backup holds all {0} and your settings (prices, printers, invoice details), so it includes customer names and payment details.",
                                    "Kopien indeholder alle {0} og dine indstillinger (priser, printere, fakturaoplysninger), så kundenavne og betalingsoplysninger er med.",
                                    Quotes(list.Count));
            if (rbFile.Checked)
            {
                infoMode.Kind = InfoKind.Warning;
                infoMode.Text = L.S("A backup file is not locked with a code: keep it somewhere private, like a USB stick or your own cloud folder.",
                                    "En kopi-fil er ikke låst med en kode: gem den et privat sted, fx på en USB-nøgle eller i din egen sky-mappe.");
            }
            else
            {
                infoMode.Kind = InfoKind.Info;
                infoMode.SetText(L.F("The backup is locked on this PC with a 20-character code before it is sent: only the code opens it. It is kept {0} days after the last upload. The newest backup replaces the older one, even if the older one was made on another PC with the same code.",
                                     "Kopien låses på denne pc med en kode på 20 tegn, før den sendes: kun koden åbner den. Den gemmes {0} dage efter seneste kopi. Den nyeste kopi erstatter den ældre, også en, der er sendt fra en anden pc med samme kode.",
                                     OnlineBackup.KeptDays),
                                 LastBackupLine());
            }
            bool hasCode = settings.HasBackupCode;
            rowCodeButtons.Visible = rbOnline.Checked && hasCode;
            if (!busy) btnNewCode.Enabled = !LimitActive;
            UpdateAction();
        }

        /// "Last online backup: 10-11-2026 14:05 · kept until 15-12-2027" / "Last online backup: none yet".
        string LastBackupLine()
        {
            DateTime t;
            if (!settings.TryLastOnlineBackup(out t)) return L.S("Last online backup: none yet", "Seneste online-kopi: ingen endnu");
            return L.F("Last online backup: {0} · kept until {1}", "Seneste online-kopi: {0} · gemmes til {1}",
                       t.ToString("dd-MM-yyyy HH:mm", Num.Dk), KeptUntil(t));
        }

        void Action()
        {
            if (!btnAction.Enabled || busy) return;
            if (tab == TabBackup && codeShown) { codeShown = false; UpdateBackup(); ShowPage(); return; }   // Back
            if (tab == TabRestore) DoRestore();
            else if (rbFile.Checked) SaveFile();
            else BackupOnline(false);
        }

        // ---------- online backup ----------
        /// Back up online: the remembered code (or a new one; newCode = always a new one, then the old online copy is
        /// deleted) -> ToBackup -> Upload (off the UI thread). Success: the code and time are saved, the code page shows.
        async void BackupOnline(bool newCode)
        {
            if (offline || busy) return;
            string old = settings.GetBackupCode();
            string code = newCode || old.Length == 0 ? Backup.NewCode() : Backup.Format(old);
            DateTime made = DateTime.Now;
            infoResult.Text = "";
            SetBusy(true, BackingUpText);
            UploadResult r = null;
            string error = null, deleteProblem = null;
            // with the newest pictures that fit under the service's size limit (issue #4; the rest only in file backups),
            // packed off the UI thread (pictures are read and the backup may be packed a few times)
            try { r = await Task.Run(() => OnlineBackup.Upload(code, Backup.FitForUpload(list, settings, Updater.CurrentText, made,
                                                                                         History.BackupPictures(list), OnlineBackup.MaxBlob))); }
            catch (OnlineBackupException ex)
            {
                if (ex.DailyLimit)
                {
                    limitUntilUtc = ex.ResetUtc ?? NextUtcMidnight();
                    error = LimitBoxText();
                }
                else error = ex.Message;
            }
            if (r != null)
            {
                DateTime now = DateTime.Now;
                bool stored = TryStoreCode(code);
                settings.SetLastOnlineBackup(now);
                settings.Save();
                if (r.UploadsLeft == 0) limitUntilUtc = r.ResetUtc ?? NextUtcMidnight();
                if (newCode && old.Length > 0 && Backup.Normalize(old) != Backup.Normalize(code))
                {
                    try { await Task.Run(() => OnlineBackup.Delete(old)); }
                    catch (OnlineBackupException ex) { deleteProblem = ex.Message; }
                }
                if (IsDisposed) return;
                SetBusy(false, null);
                ShowCode(code, now, true, r.UploadsLeft, deleteProblem);
                if (!stored)
                {
                    infoCodeLeft.Text = (infoCodeLeft.Text.Length > 0 ? infoCodeLeft.Text + "\n" : "") + CodeNotStoredText();
                    if (fitted) ShowPage();
                }
                return;
            }
            if (IsDisposed) return;
            SetBusy(false, null);
            infoResult.Kind = InfoKind.Error;
            infoResult.Text = error;
        }

        /// Remembers the code (encrypted by Windows, AppSettings.SetBackupCode). Windows can refuse (CryptographicException,
        /// e.g. a damaged user profile): false, logged to errors.log, the code is not remembered.
        bool TryStoreCode(string code)
        {
            try { settings.SetBackupCode(code); return true; }
            catch (Exception ex) { ErrorReport.Log(ex); return false; }
        }

        static string CodeNotStoredText()
        {
            return L.S("Windows could not store the code on this PC, so it isn't remembered here: write it down now.",
                       "Windows kunne ikke gemme koden på denne pc, så den huskes ikke her: skriv den ned nu.");
        }

        /// The date an online backup made at t is kept until ("?" for an impossible date near year 9999).
        static string KeptUntil(DateTime t)
        {
            return t.Year < 9000 ? t.AddDays(OnlineBackup.KeptDays).ToString("dd-MM-yyyy", Num.Dk) : "?";
        }

        /// Show code: the remembered code on the code page (no network).
        void ShowRememberedCode()
        {
            string code = settings.GetBackupCode();
            if (code.Length == 0) return;
            DateTime t;
            ShowCode(Backup.Format(code), settings.TryLastOnlineBackup(out t) ? t : DateTime.MinValue, false, -1, null);
        }

        /// New code…: asks, then uploads under a new code and deletes the online backup under the old one.
        void NewCodeClicked()
        {
            if (busy || offline) return;
            using (var ask = new AskForm(L.S("New code", "Ny kode"), L.S("Make a new code?", "Lav en ny kode?"),
                                         L.S("The backup is sent again under a new code, and the online backup under the old code is deleted. The old code stops working, so keep the new one safe.",
                                             "Kopien sendes igen med en ny kode, og online-kopien under den gamle kode slettes. Den gamle kode holder op med at virke, så gem den nye godt."),
                                         L.S("Yes", "Ja"), L.S("No", "Nej")))
                if (ask.ShowDialog(this) != DialogResult.Yes) return;
            BackupOnline(true);
        }

        /// Delete online backup…: asks, deletes it on the service, then this PC forgets the code and the time.
        async void DeleteOnlineClicked()
        {
            if (busy || offline) return;
            string code = settings.GetBackupCode();
            if (code.Length == 0) return;
            using (var ask = new AskForm(L.S("Delete online backup", "Slet online-kopi"), L.S("Delete the online backup?", "Slet online-kopien?"),
                                         L.S("It is deleted from the backup service, and this PC forgets the code. Backup files you saved stay as they are.",
                                             "Den slettes fra kopi-tjenesten, og denne pc glemmer koden. Kopi-filer, du har gemt, bliver, som de er."),
                                         L.S("Yes", "Ja"), L.S("No", "Nej")))
                if (ask.ShowDialog(this) != DialogResult.Yes) return;
            infoResult.Text = "";
            SetBusy(true, DeletingText);
            string error = null;
            try { await Task.Run(() => OnlineBackup.Delete(code)); }
            catch (OnlineBackupException ex) { error = ex.Message; }
            if (error == null)
            {
                settings.SetBackupCode("");
                settings.LastOnlineBackup = "";
                settings.Save();
            }
            if (IsDisposed) return;
            SetBusy(false, null);
            infoResult.Kind = error == null ? InfoKind.Success : InfoKind.Error;
            infoResult.Text = error ?? L.S("✓ The online backup is deleted, and this PC no longer has a code.",
                                           "✓ Online-kopien er slettet, og denne pc har ikke længere en kode.");
        }

        void SaveFile()
        {
            if (offline) return;
            DateTime now = DateTime.Now;
            string path;
            using (var dlg = new SaveFileDialog
            {
                Title = L.S("Save backup", "Gem sikkerhedskopi"), FileName = History.BackupFileName(now), DefaultExt = History.BackupExtension.TrimStart('.'),
                Filter = L.S("PrintPrice backup", "PrintPrice-sikkerhedskopi") + " (*" + History.BackupExtension + ")|*" + History.BackupExtension,
                OverwritePrompt = true, AddExtension = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            string err = History.WriteBackupFile(path, list, settings, Updater.CurrentText, now, History.BackupPictures(list));   // with the pictures
            if (err != null) { infoResult.Kind = InfoKind.Error; infoResult.Text = err; return; }
            infoResult.Kind = InfoKind.Success;
            infoResult.SetText(L.F("✓ Saved: {0}", "✓ Gemt: {0}", Path.GetFileName(path)), Path.GetDirectoryName(path));
            infoResult.SetLink(L.S("Open folder", "Åbn mappe"), () => ShowInFolder(path));
        }

        // ---------- the code page (after an online backup) ----------
        /// Shows the code, Copy, the keep-it-safe warning and when it expires. justBackedUp = after an upload (✓ line,
        /// uploads left today: 1-2 = Warning, 0 = the countdown note); else Show code (when = the last upload, MinValue = unknown).
        /// deleteProblem = New code couldn't delete the old online backup (shown as a warning).
        void ShowCode(string code, DateTime when, bool justBackedUp, int left, string deleteProblem)
        {
            shownCode = code ?? "";
            lblCode.Text = shownCode;
            btnCopy.Text = L.S("Copy", "Kopiér");
            if (justBackedUp)
            {
                infoCodeOk.Kind = InfoKind.Success;
                infoCodeOk.Text = L.F("✓ Backed up online: {0} and your settings, {1}.", "✓ Kopi gemt online: {0} og dine indstillinger, {1}.",
                                      Quotes(list.Count), when.ToString("dd-MM-yyyy HH:mm", Num.Dk));
            }
            else
            {
                infoCodeOk.Kind = InfoKind.Info;
                infoCodeOk.Text = L.S("The code of this PC's online backup.", "Koden til denne pc's online-kopi.");
            }
            string leftText = "";
            if (justBackedUp && left == 0)
                leftText = L.F("That was today's last online backup ({0} a day). You can back up again in {1}.",
                               "Det var dagens sidste online-kopi ({0} om dagen). Du kan tage en kopi igen om {1}.",
                               OnlineBackup.DailyUploads, OnlineBackup.WaitText(limitUntilUtc - DateTime.UtcNow));
            else if (justBackedUp && left == 1)
                leftText = L.S("1 online backup left today.", "1 online-kopi tilbage i dag.");
            else if (justBackedUp && left == 2)
                leftText = L.S("2 online backups left today.", "2 online-kopier tilbage i dag.");
            if (deleteProblem != null)
                leftText = (leftText.Length > 0 ? leftText + "\n" : "") +
                           L.S("The online backup under the old code could not be deleted: ", "Online-kopien under den gamle kode kunne ikke slettes: ") + deleteProblem;
            infoCodeLeft.Text = leftText;
            infoCodeWarn.Text = L.S("Keep this code safe, for example in a password manager or written on paper. It is the only way to get the backup back: it can't be reset or sent to you.\nAnyone who has the code can read the backup, so don't share it.",
                                    "Gem koden et sikkert sted, fx i en adgangskode-app eller på papir. Den er den eneste vej til kopien: den kan ikke nulstilles eller sendes til dig.\nAlle med koden kan læse kopien, så del den ikke.");
            infoCodeNext.SetText(L.S("Next time, \"Back up online\" replaces this backup and keeps the code. The newest backup always wins, even if the older one was made on another PC with this code.",
                                     "Næste gang erstatter \"Tag kopi online\" denne kopi og beholder koden. Den nyeste kopi vinder altid, også hvis den ældre er lavet på en anden pc med denne kode."),
                                 when == DateTime.MinValue ? null
                                 : L.F("Last online backup: {0} · kept until {1}", "Seneste online-kopi: {0} · gemmes til {1}",
                                       when.ToString("dd-MM-yyyy HH:mm", Num.Dk), KeptUntil(when)));
            codeShown = true;
            if (fitted) ShowPage();
        }

        void CopyCode()
        {
            if (shownCode.Length == 0 || offline) return;
            try { Clipboard.SetText(shownCode); btnCopy.Text = CopiedText; }
            catch { }
        }

        // ---------- Restore ----------
        BackupData CurrentData
        {
            get
            {
                if (rbFromFile.Checked) return fileProblem == null ? fileData : null;
                return codeProblem == null && codeLoaded.Length > 0 && codeLoaded == Backup.Normalize(txtCode.Text) ? codeData : null;
            }
        }

        /// Get backup: downloads the backup for the typed code (off the UI thread) and reads it like a file.
        async void GetBackup()
        {
            string code = Backup.Normalize(txtCode.Text);
            if (code == null || busy || offline) return;
            codeData = null;
            codeProblem = null;
            codeLoaded = "";
            SetBusy(true, null);
            btnGet.Text = GettingText;
            string text = null, problem = null;
            try { text = await Task.Run(() => OnlineBackup.Download(code)); }
            catch (OnlineBackupException ex) { problem = ex.Message; }
            if (IsDisposed) return;
            BackupData d = null;
            if (problem == null) problem = History.ReadBackup(text, out d);
            codeData = d;
            codeProblem = problem;
            codeLoaded = code;
            btnGet.Text = L.S("Get backup", "Hent kopi");
            SetBusy(false, null);
        }

        /// Browse…: the file is read and parsed off the UI thread (a big backup with pictures can take a while); the inputs
        /// are greyed out and the button says "Reading…" meanwhile, like Get backup.
        async void Browse()
        {
            if (offline || busy) return;
            string path;
            using (var dlg = new OpenFileDialog
            {
                Title = L.S("Restore backup", "Gendan sikkerhedskopi"), CheckFileExists = true,
                Filter = L.S("PrintPrice backup", "PrintPrice-sikkerhedskopi") + " (*" + History.BackupExtension + ", *.json)|*" + History.BackupExtension + ";*.json|" +
                         L.S("All files", "Alle filer") + " (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            fileData = null;
            fileProblem = null;
            txtFile.Text = Path.GetFileName(path);
            string browseText = btnBrowse.Text;
            SetBusy(true, null);
            btnBrowse.Text = ReadingText;
            string problem;
            BackupData d = null;
            try
            {
                var read = await Task.Run(() => { BackupData x; string p = History.ReadBackupFile(path, out x); return Tuple.Create(p, x); });
                problem = read.Item1;
                d = read.Item2;
            }
            catch (Exception ex) { problem = L.S("Backup could not be read: ", "Sikkerhedskopien kunne ikke læses: ") + ex.Message; }
            if (IsDisposed) return;
            fileProblem = problem;
            fileData = problem == null ? d : null;
            btnBrowse.Text = browseText;
            SetBusy(false, null);   // UpdateRestore: the summary of the file
        }

        /// A typed code is wrong once it has a character that is never in a code, more than 20 characters, or 20 with
        /// the wrong check character (Backup.Normalize).
        static bool CodeTypo(string typed)
        {
            string c = Backup.Clean(typed ?? "");
            if (c == null) return true;
            return c.Length > Backup.CodeLength || (c.Length == Backup.CodeLength && Backup.Normalize(typed) == null);
        }

        /// Restore tab state: which source row is on, the summary (or error), the How choices, the note under them.
        void UpdateRestore()
        {
            bool fromFile = rbFromFile.Checked;
            txtFile.Enabled = btnBrowse.Enabled = fromFile;
            txtCode.Enabled = !fromFile && OnlineReady;
            bool typo = !fromFile && CodeTypo(txtCode.Text);
            txtCode.BackColor = typo ? Theme.InvalidBack : Theme.Input;
            string typed = Backup.Normalize(txtCode.Text);
            btnGet.Enabled = !fromFile && OnlineReady && !typo && typed != null;
            UpdateAccept();
            chkKeep.Visible = true;   // always there (greyed for a file) so the tab keeps its height
            chkKeep.Enabled = !fromFile && OnlineReady;
            bool fetched = codeLoaded.Length > 0 && codeLoaded == typed;   // Get backup ran for the code in the box
            var data = CurrentData;
            if (fromFile)
            {
                if (fileProblem != null) { infoSummary.Kind = InfoKind.Error; infoSummary.Text = fileProblem; }
                else if (data != null) { infoSummary.Kind = InfoKind.Info; infoSummary.Text = Summary(data); }
                else
                {
                    infoSummary.Kind = InfoKind.Info;
                    infoSummary.Text = L.S("Choose a backup file (.printprice-backup) or a history.json with Browse…",
                                           "Vælg en kopi-fil (.printprice-backup) eller en history.json med Gennemse…");
                }
            }
            else if (typo)
            {
                infoSummary.Kind = InfoKind.Error;
                infoSummary.Text = L.S("This code has a typo: check it letter by letter.", "Koden har en tastefejl: tjek den tegn for tegn.");
            }
            else if (fetched && codeProblem != null) { infoSummary.Kind = InfoKind.Error; infoSummary.Text = codeProblem; }
            else if (data != null) { infoSummary.Kind = InfoKind.Info; infoSummary.Text = Summary(data); }
            else
            {
                infoSummary.Kind = InfoKind.Info;
                infoSummary.Text = L.S("Type the 20-character code, then click Get backup.", "Skriv koden på 20 tegn, og klik på Hent kopi.");
            }

            bool loaded = data != null;
            rbMerge.Text = list.Count == 0 ? L.S("Merge: add the backup's quotes", "Flet: tilføj kopiens tilbud")
                         : L.F("Merge: add the backup's quotes to the {0} on this PC", "Flet: læg kopiens tilbud til de {0} på denne pc",
                               list.Count.ToString("N0", Num.Dk));
            rbMerge.Enabled = rbReplace.Enabled = loaded;
            chkSettings.Enabled = loaded && data.Settings != null;
            lblSettingsNote.Text = loaded && data.Settings == null
                ? (string.IsNullOrEmpty(data.CreatedAt) && string.IsNullOrEmpty(data.App)
                    ? L.S("This file has no settings (a history.json).", "Filen har ingen indstillinger (en history.json).")
                    : L.S("This backup was made without the settings.", "Kopien blev lavet uden indstillingerne."))
                : L.S("Prices, printers and invoice details. The Bambu Studio path stays as it is.",
                      "Priser, printere og fakturaoplysninger. Stien til Bambu Studio bliver, som den er.");
            if (rbReplace.Checked && list.Count > 0)
            {
                infoNote.Kind = InfoKind.Warning;
                infoNote.Text = ReplaceNote();

            }
            else
            {
                infoNote.Kind = InfoKind.Info;
                infoNote.Text = L.S("A safety copy of this PC's history and settings is saved first, next to them.",
                                    "En sikkerhedskopi af denne pc's historik og indstillinger gemmes først, ved siden af dem.");
            }
            UpdateAction();
        }

        string ReplaceNote()
        {
            return L.F("Replace takes the {0} on this PC out of the list. A safety copy of them is saved first.",
                       "Erstat fjerner de {0} på denne pc fra listen. En sikkerhedskopi af dem gemmes først.", Quotes(list.Count));
        }

        static string Summary(BackupData d)
        {
            DateTime t;
            string when = History.TryTime(d.CreatedAt, out t) ? t.ToString("dd-MM-yyyy HH:mm", Num.Dk) : "";
            string set = d.Settings != null ? L.S("with settings", "med indstillinger") : L.S("no settings", "uden indstillinger");
            if (when.Length == 0 && string.IsNullOrEmpty(d.App))
                return L.F("A price history file: {0}, {1}.", "En prishistorik-fil: {0}, {1}.", Quotes(d.Quotes.Count), set);
            string app = string.IsNullOrEmpty(d.App) ? "" : " (PrintPrice v" + d.App + ")";
            return L.F("Backup from {0}{1}: {2}, {3}.", "Kopi fra {0}{1}: {2}, {3}.", when.Length > 0 ? when : "?", app, Quotes(d.Quotes.Count), set);
        }

        void DoRestore()
        {
            var data = CurrentData;
            if (data == null || offline) return;
            bool replace = rbReplace.Checked, withSettings = chkSettings.Enabled && chkSettings.Checked;
            if (replace && list.Count > 0)
                using (var ask = NewReplaceAsk(list.Count))
                    if (ask.ShowDialog(this) != DialogResult.Yes) return;
            DateTime now = DateTime.Now;
            RestoreResult res;
            string problem;
            var old = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try { problem = History.Restore(list, settings, data, replace, withSettings, now, out res); }
            finally { Cursor.Current = old; }
            if (res == null)
            {
                infoSummary.Kind = InfoKind.Error;
                infoSummary.Text = problem ?? L.S("Backup not restored.", "Sikkerhedskopien blev ikke gendannet.");
                return;
            }
            if (Restored != null) Restored(res);   // MainForm: the history list (in place) and the settings
            if (res.Settings != null) settings = res.Settings;
            // restored from a code with "Keep backing up to this code": online backups go to that code from now on
            if (!rbFromFile.Checked && chkKeep.Checked && codeLoaded.Length > 0)
            {
                if (TryStoreCode(codeLoaded))
                {
                    settings.Save();
                    keptCode = true;
                }
                else problem = (problem != null ? problem + "\n" : "") + CodeNotStoredText();
            }
            ShowDone(res, problem, SafetyCopy(now));
            UpdateBackup();   // the remembered code / Last online backup line
        }

        /// The newest history.before-restore-*.json next to history.json (made by History.Restore just now), or null.
        static string SafetyCopy(DateTime since)
        {
            try
            {
                string dir = Path.GetDirectoryName(History.FilePath), best = null;
                DateTime bestTime = DateTime.MinValue;
                foreach (var f in Directory.GetFiles(dir, "history.before-restore-*.json"))
                {
                    DateTime c = File.GetCreationTime(f);
                    if (c >= bestTime) { bestTime = c; best = f; }
                }
                return best != null && bestTime >= since.AddMinutes(-1) ? best : null;
            }
            catch { return null; }
        }

        /// The result in place of the Restore tab: what was added, invoice codes now on two quotes, printers moved,
        /// the safety copy (Open folder), and "Restart now" when the restored settings use another language.
        void ShowDone(RestoreResult r, string problem, string safety)
        {
            int here = r.Same + r.Updated;
            string main = L.F("✓ Restored. {0} added", "✓ Gendannet. {0} tilføjet", Quotes(r.Added));
            if (here > 0)
                main += r.Updated > 0
                    ? L.F(", {0} were already here ({1} of them got details from the backup)", ", {0} var her allerede ({1} af dem fik oplysninger fra kopien)",
                          here.ToString("N0", Num.Dk), r.Updated.ToString("N0", Num.Dk))
                    : L.F(", {0} were already here", ", {0} var her allerede", here.ToString("N0", Num.Dk));
            main += ".";
            if (r.SettingsRestored) main += L.S(" Settings restored.", " Indstillinger gendannet.");
            if (keptCode) main += L.S(" Online backups now go to this code.", " Online-kopier går nu til denne kode.");
            if (problem != null) main += "\n" + problem;
            doneMain.Kind = problem != null ? InfoKind.Warning : InfoKind.Success;
            doneMain.SetText(main, L.F("Now {0} in the history.", "Nu {0} i historikken.", Quotes(r.Quotes.Count)));

            doneClash.Kind = InfoKind.Warning;
            int nc = r.InvoiceClashes.Count;
            doneClash.Text = nc == 0 ? "" : nc == 1
                ? L.F("Invoice code {0} is on two different quotes. Both are kept, because the customer may already have it. Search for it in the history to check them.",
                      "Fakturanummer {0} står på to forskellige tilbud. Begge beholdes, fordi kunden måske allerede har det. Søg efter det i historikken for at tjekke dem.",
                      r.InvoiceClashes[0])
                : L.F("Invoice codes {0} are each on two different quotes. All are kept, because the customers may already have them. Search for them in the history to check them.",
                      "Fakturanumrene {0} står hver på to forskellige tilbud. Alle beholdes, fordi kunderne måske allerede har dem. Søg efter dem i historikken for at tjekke dem.",
                      string.Join(", ", r.InvoiceClashes.ToArray()));

            doneMoved.Kind = InfoKind.Warning;
            doneMoved.Text = r.PrintersMoved == 0 ? "" : r.PrintersMoved == 1
                ? L.S("A printer in the backup had the same number as a different printer here. Its quotes are kept under its own name, shown as \"(removed)\", so the totals stay right.",
                      "En printer i kopien havde samme nummer som en anden printer her. Dens tilbud beholdes under dens eget navn, vist som \"(fjernet)\", så tallene passer.")
                : L.F("{0} printers in the backup had the same number as a different printer here. Their quotes are kept under their own names, shown as \"(removed)\", so the totals stay right.",
                      "{0} printere i kopien havde samme nummer som en anden printer her. Deres tilbud beholdes under deres egne navne, vist som \"(fjernet)\", så tallene passer.",
                      r.PrintersMoved.ToString(Num.Dk));

            doneSafety.Kind = InfoKind.Info;
            if (safety != null)
            {
                doneSafety.SetText(L.S("Safety copy of the old history:", "Sikkerhedskopi af den gamle historik:"), Path.GetFileName(safety));
                string path = safety;
                doneSafety.SetLink(L.S("Open folder", "Åbn mappe"), () => ShowInFolder(path));
            }
            else doneSafety.Text = "";

            doneLang.Kind = InfoKind.Info;
            if (r.SettingsRestored && r.Settings != null && AppSettings.IsDanish(r.Settings.Language) != L.Danish)
            {
                doneLang.Text = L.S("The restored settings use another language.", "De gendannede indstillinger bruger et andet sprog.");
                doneLang.SetLink(L.S("Restart now", "Genstart nu"), () => { RestartRequested = true; DialogResult = DialogResult.OK; Close(); });
            }
            else doneLang.Text = "";

            doneShown = true;
            if (fitted) ShowPage();
        }

        void ShowInFolder(string path)
        {
            if (offline) return;
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); }
            catch { }
        }

        /// Ctrl+Tab / Ctrl+PgDn and Ctrl+Shift+Tab / Ctrl+PgUp switch tabs, like Settings.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.PageDown) ||
                keyData == (Keys.Control | Keys.Shift | Keys.Tab) || keyData == (Keys.Control | Keys.PageUp))
            {
                SelectedTab = tab == TabBackup ? TabRestore : TabBackup;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- picture mode (Screenshots): made-up states, nothing is read, written or sent ----------


        static readonly string SampleCodeText = Backup.NewCode(i => (i * 7 + 3) & 31);   // a valid made-up code
        static readonly DateTime SampleTime = new DateTime(2026, 11, 10, 14, 5, 0);

        /// Back up tab, online chosen; remembered = a code and a last backup are known (Show code / New code / Delete).
        internal void SampleOnline(bool remembered)
        {
            if (remembered) { settings.SetBackupCode(SampleCodeText); settings.SetLastOnlineBackup(SampleTime); }
            codeShown = false;
            rbOnline.Checked = true;
            UpdateBackup();
            ShowPage();
        }

        /// The code page right after an upload, with "2 online backups left today".
        internal void SampleCode()
        {
            ShowCode(SampleCodeText, SampleTime, true, 2, null);
        }

        /// The daily cap reached: the countdown in place of "Back up online" and the limit text.
        internal void SampleCountdown()
        {
            codeShown = false;
            limitUntilUtc = DateTime.UtcNow.AddHours(3).AddMinutes(20).AddSeconds(15);
            infoResult.Kind = InfoKind.Error;
            infoResult.Text = LimitBoxText();
            UpdateBackup();
            ShowPage();
        }

        /// The Restore tab after Get backup (this window's quotes + settings as the downloaded backup), "Keep backing up" ticked.
        internal void SampleCodeRestore()
        {
            limitUntilUtc = DateTime.MinValue;
            rbFromCode.Checked = true;
            txtCode.Text = SampleCodeText;
            BackupData d;
            codeProblem = History.ReadBackup(History.ToBackup(list, settings, Updater.CurrentText, SampleTime), out d);
            codeData = d;
            codeLoaded = Backup.Normalize(SampleCodeText) ?? "";
            rbMerge.Checked = true;
            UpdateRestore();
        }

        /// The Restore tab with this window's quotes + settings as a loaded backup file (through ToBackup / ReadBackup).
        internal void SampleLoad(bool replace)
        {
            rbFromFile.Checked = true;
            BackupData d;
            fileProblem = History.ReadBackup(History.ToBackup(list, settings, Updater.CurrentText, new DateTime(2026, 11, 2, 21, 40, 0)), out d);
            fileData = d;
            txtFile.Text = History.BackupFileName(new DateTime(2026, 11, 2));
            if (replace) rbReplace.Checked = true; else rbMerge.Checked = true;
            chkSettings.Checked = !replace;
            UpdateRestore();
        }



        internal void SampleDone()
        {
            var r = new RestoreResult { Added = 12, Same = 1234, Updated = 2, PrintersMoved = 1, SettingsRestored = true, Settings = settings };
            r.InvoiceClashes.Add("2026-K7Q4");
            for (int i = 0; i < 1260; i++) r.Quotes.Add(null);   // only the count is shown
            ShowDone(r, null, Path.Combine(Path.GetTempPath(), "history.before-restore-20261110-1405.json"));
        }

        /// "Replace N quotes?" before a Replace restore (Yes / No; No is the default and Esc).
        internal static AskForm NewReplaceAsk(int quotes)
        {
            return new AskForm(L.S("Restore", "Gendan"), L.F("Replace {0}?", "Erstat {0}?", Quotes(quotes)),
                               L.S("This PC's history becomes the backup's. A safety copy of the current history is saved first.",
                                   "Denne pc's historik bliver kopiens. En sikkerhedskopi af den nuværende historik gemmes først."),
                               L.S("Yes", "Ja"), L.S("No", "Nej"));
        }

        /// Small dark yes / no question (instead of Windows' light MessageBox): bold question, a Warning InfoBox with
        /// the details, Yes / No right-aligned (No = default and Esc). Content 352 wide.
        internal sealed class AskForm : CaptionForm
        {
            const int ContentW = 352;
            readonly Label question;
            readonly InfoBox info;

            public AskForm(string title, string text, string details, string yes, string no)
            {
                SuspendLayout();
                AutoScaleDimensions = new SizeF(96F, 96F);
                AutoScaleMode = AutoScaleMode.Dpi;
                Font = new Font("Segoe UI", 10f);
                Text = title;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent;
                if (Program.Hidden || Screenshots.Active) Screenshots.Place(this);
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14), Margin = Padding.Empty };
                Controls.Add(root);
                question = new Label { Text = text, AutoSize = true, UseMnemonic = false, Font = new Font(Font, FontStyle.Bold),
                                       MaximumSize = new Size(ContentW, 0), Margin = new Padding(0, 0, 0, 8) };
                info = new InfoBox(ContentW, 9.5f) { Margin = new Padding(0, 0, 0, 14), Text = details };
                info.Kind = InfoKind.Warning;
                var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
                var btnYes = new Button { Text = yes, Size = new Size(90, 32), Margin = new Padding(0, 0, 6, 0), Padding = Padding.Empty, DialogResult = DialogResult.Yes };
                var btnNo = new Button { Text = no, Size = new Size(90, 32), Margin = Padding.Empty, Padding = Padding.Empty, DialogResult = DialogResult.No };
                buttons.Controls.Add(btnYes);
                buttons.Controls.Add(btnNo);
                root.Controls.Add(question);
                root.Controls.Add(info);
                root.Controls.Add(buttons);
                AcceptButton = btnNo;   // Enter never replaces by accident
                CancelButton = btnNo;
                Theme.Apply(this);
                ResumeLayout(true);
                Load += delegate
                {
                    int w = (int)Math.Round(ContentW * DeviceDpi / 96f);
                    question.MaximumSize = new Size(w, 0);
                    info.SetWidth(w);
                    root.MinimumSize = new Size(w + root.Padding.Horizontal, 0);
                };
                Shown += delegate { if (!Program.Hidden && !Screenshots.Active) btnNo.Focus(); };
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                Theme.TitleBar(this);
            }

            protected override bool ShowWithoutActivation { get { return Screenshots.Active || Program.Hidden; } }
        }

        /// Check box in the dark look, painted by hand (WinForms' disabled flat one draws its text almost black):
        /// 13 px Input box, Border frame (110,110,116 on hover / focus), Text tick, text 6 px after the box (Text, Muted
        /// when disabled), dotted Muted focus rectangle with keyboard cues. Keeps CheckBox's behaviour (Space, click).
        internal sealed class Tick : CheckBox
        {
            public const int Box = 13, TextX = 19;   // 96 dpi units
            bool hot;

            public Tick()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                AutoSize = false;
                UseMnemonic = false;
                Size = new Size(300, 26);
            }

            int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

            protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; base.OnTextChanged(e); }
            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
            protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Back);
                int d = Px(Box), y = (Height - d) / 2;
                using (var b = new SolidBrush(Theme.Input)) g.FillRectangle(b, 0, y, d, d);
                using (var p = new Pen((hot || Focused) && Enabled ? Theme.HotBorder : Theme.Border)) g.DrawRectangle(p, 0, y, d - 1, d - 1);
                if (Checked)
                {
                    var st = g.Save();
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var p = new Pen(Enabled ? Theme.Text : Theme.Muted, Math.Max(1.5f, d / 8f)))
                        g.DrawLines(p, new[] { new PointF(d * 0.22f, y + d * 0.52f), new PointF(d * 0.42f, y + d * 0.72f), new PointF(d * 0.78f, y + d * 0.3f) });
                    g.Restore(st);
                }
                int tx = Px(TextX);
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, Width - tx, Height), Enabled ? Theme.Text : Theme.Muted, flags);
                if (Focused && ShowFocusCues)
                {
                    var size = TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags);
                    using (var pen = new Pen(Theme.Muted) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                        g.DrawRectangle(pen, tx - 2, (Height - size.Height) / 2 - 1, size.Width + 3, size.Height + 1);
                }
            }
        }
    }
}