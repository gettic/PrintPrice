using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Backup window (idea 27), opened from History (Backup…) and Settings > App (Back up… / Restore…). Two tabs:
    /// Back up = the price history + settings to a .printprice-backup file (History.WriteBackupFile), or online with a
    /// code (coming soon: shown, greyed out); Restore = from a file (or a code: coming soon; the typed code is checked
    /// with its check character already), Merge or Replace, "Also restore the settings". History.Restore makes the
    /// safety copies and saves; Restored hands the result to MainForm. The result replaces the Restore tab's content.
    internal class BackupForm : CaptionForm
    {
        public const int TabBackup = 0, TabRestore = 1;
        /// Called right after a restore saved the files: MainForm takes the new history (in place) and settings.
        public Action<RestoreResult> Restored;
        /// True when the window closed with "Restart now" (restored settings in another language).
        public bool RestartRequested;

        // the online backup service isn't there yet: its parts show "coming soon" and their buttons are greyed out
        static readonly bool OnlineReady = false;

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
        readonly Button btnAction, btnClose, btnBrowse, btnGet, btnCopy;
        readonly QuoteForm.Choice rbFile, rbOnline, rbFromFile, rbFromCode, rbMerge, rbReplace;
        readonly InfoBox infoContents, infoMode, infoResult, infoSummary, infoNote;
        readonly InfoBox infoCodeOk, infoCodeWarn, infoCodeNext;
        readonly InfoBox doneMain, doneClash, doneMoved, doneSafety, doneLang;
        readonly TextBox txtFile;
        readonly SettingsForm.CueBox txtCode;
        readonly Tick chkSettings;
        readonly Label lblSettingsNote, lblCode;
        BackupData fileData;     // the backup file chosen on the Restore tab (null = none yet)
        string fileProblem;      // why the chosen file can't be restored (null = none)
        string shownCode = "";   // the code on the code page
        bool codeShown, doneShown, fitted, measured;
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
            rbOnline = new QuoteForm.Choice { Text = L.S("Online, with a code", "Online, med en kode") + Soon, Enabled = OnlineReady };
            grpTo.Controls.Add(rbFile);
            grpTo.Controls.Add(rbOnline);
            pageBackup.Controls.Add(grpTo);
            infoContents = Info(8);
            infoMode = Info(8);
            infoResult = Info(8);
            pageBackup.Controls.Add(infoContents);
            pageBackup.Controls.Add(infoMode);
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
            rbFromCode = new QuoteForm.Choice { Text = L.S("An online code", "En online-kode") + Soon, Enabled = OnlineReady };
            rowCode = Row();
            rowCode.Margin = new Padding(Indent, 2, 0, 0);
            txtCode = new SettingsForm.CueBox { Cue = "XXXXX-XXXXX-XXXXX-XXXXX", Width = 300, Margin = Padding.Empty, MaxLength = 40,
                                                CharacterCasing = CharacterCasing.Upper, AccessibleName = L.S("Backup code", "Kode til kopien") };
            txtCode.TextChanged += delegate { UpdateRestore(); };
            txtCode.Leave += delegate { string n = Backup.Normalize(txtCode.Text); if (n != null) txtCode.Text = Backup.Format(n); };
            btnGet = SmallButton(L.S("Get backup", "Hent kopi"));
            rowCode.Controls.Add(txtCode, 0, 0);
            rowCode.Controls.Add(btnGet, 1, 0);
            grpFrom.Controls.Add(rbFromFile);
            grpFrom.Controls.Add(rowFile);
            grpFrom.Controls.Add(rbFromCode);
            grpFrom.Controls.Add(rowCode);
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
            int sw = Math.Max(TextRenderer.MeasureText(btnBrowse.Text, btnBrowse.Font).Width, TextRenderer.MeasureText(btnGet.Text, btnGet.Font).Width) + Px(24);
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
            foreach (var s in new[] { FileText, OnlineText, RestoreText })
                aw = Math.Max(aw, TextRenderer.MeasureText(s, btnAction.Font).Width + Px(28));
            btnAction.Size = new Size(aw, Px(ButtonH));
            btnClose.Size = new Size(Math.Max(Px(90), TextRenderer.MeasureText(btnClose.Text, btnClose.Font).Width + Px(28)), Px(ButtonH));
            fitted = true;
            foreach (var p in pages) p.Visible = true;   // measured while shown, then ShowPage keeps one
            // the Restore tab at its tallest (Replace's warning is longer than the safety note): no jump when Replace is chosen
            if (list.Count > 0) { infoNote.Kind = InfoKind.Warning; infoNote.Text = ReplaceNote(); }
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
                if (child is Tick) { child.Size = new Size(cw, Px(26)); continue; }
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
        static string Soon { get { return L.S(" (coming soon)", " (kommer snart)"); } }
        static string CopiedText { get { return L.S("✓ Copied", "✓ Kopieret"); } }

        /// The accent button: Save file… / Back up online / Restore; hidden on the code and result pages (Close is the
        /// default there). Greyed out = a normal button's look (no red behind muted text).
        void UpdateAction()
        {
            bool done = tab == TabBackup ? codeShown : doneShown;
            btnAction.Visible = !done;
            string text = tab == TabRestore ? RestoreText : rbOnline.Checked ? OnlineText : FileText;
            bool on = tab == TabRestore ? CurrentData != null : rbFile.Checked || OnlineReady;
            if (btnAction.Text != text) btnAction.Text = text;
            btnAction.Enabled = on;
            Theme.Apply(btnAction);
            if (on) Theme.AccentButton(btnAction);
            AcceptButton = done ? btnClose : on ? btnAction : null;
        }

        // ---------- Back up ----------
        void UpdateBackup()
        {
            infoContents.Text = list.Count == 0
                ? L.S("The backup holds your settings (prices, printers, invoice details; no quotes yet), so it includes payment details.",
                      "Kopien indeholder dine indstillinger (priser, printere, fakturaoplysninger; ingen tilbud endnu), så betalingsoplysninger er med.")
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
                infoMode.SetText(L.S("Online backup with a code is coming soon. Until then, save a backup file.",
                                     "Online-kopi med en kode kommer snart. Indtil da kan du gemme en kopi-fil."),
                                 L.S("The backup will be locked on this PC with a 20-character code before it is sent, and kept 400 days after the last backup.",
                                     "Kopien bliver låst på denne pc med en kode på 20 tegn, før den sendes, og gemmes 400 dage efter seneste kopi."));
            }
            UpdateAction();
        }

        void Action()
        {
            if (!btnAction.Enabled) return;
            if (tab == TabRestore) DoRestore();
            else if (rbFile.Checked) SaveFile();
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
            string err = History.WriteBackupFile(path, list, settings, Updater.CurrentText, now);
            if (err != null) { infoResult.Kind = InfoKind.Error; infoResult.Text = err; return; }
            infoResult.Kind = InfoKind.Success;
            infoResult.SetText(L.F("✓ Saved: {0}", "✓ Gemt: {0}", Path.GetFileName(path)), Path.GetDirectoryName(path));
            infoResult.SetLink(L.S("Open folder", "Åbn mappe"), () => ShowInFolder(path));
        }

        // ---------- the code page (after an online backup) ----------
        /// Shows the code, Copy, the keep-it-safe warning and when it expires (online backup done).
        void ShowCode(string code, DateTime when, DateTime keptUntil)
        {
            shownCode = code ?? "";
            lblCode.Text = shownCode;
            btnCopy.Text = L.S("Copy", "Kopiér");
            infoCodeOk.Text = L.F("✓ Backed up online: {0} and your settings, {1}.", "✓ Kopi gemt online: {0} og dine indstillinger, {1}.",
                                  Quotes(list.Count), when.ToString("dd-MM-yyyy HH:mm", Num.Dk));
            infoCodeWarn.Text = L.S("Keep this code safe, for example in a password manager or written on paper. It is the only way to get the backup back: it can't be reset or sent to you.\nAnyone who has the code can read the backup, so don't share it.",
                                    "Gem koden et sikkert sted, fx i en adgangskode-app eller på papir. Den er den eneste vej til kopien: den kan ikke nulstilles eller sendes til dig.\nAlle med koden kan læse kopien, så del den ikke.");
            infoCodeNext.SetText(L.S("Next time, \"Back up online\" updates this same backup and keeps the code.",
                                     "Næste gang opdaterer \"Tag kopi online\" den samme kopi og beholder koden."),
                                 L.F("Kept until {0} (400 days after the last backup).", "Gemmes til {0} (400 dage efter seneste kopi).",
                                     keptUntil.ToString("dd-MM-yyyy", Num.Dk)));
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
        BackupData CurrentData { get { return rbFromFile.Checked && fileProblem == null ? fileData : null; } }

        void Browse()
        {
            if (offline) return;
            using (var dlg = new OpenFileDialog
            {
                Title = L.S("Restore backup", "Gendan sikkerhedskopi"), CheckFileExists = true,
                Filter = L.S("PrintPrice backup", "PrintPrice-sikkerhedskopi") + " (*" + History.BackupExtension + ", *.json)|*" + History.BackupExtension + ";*.json|" +
                         L.S("All files", "Alle filer") + " (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                BackupData d;
                fileProblem = History.ReadBackupFile(dlg.FileName, out d);
                fileData = d;
                txtFile.Text = Path.GetFileName(dlg.FileName);
            }
            UpdateRestore();
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
            txtCode.Enabled = !fromFile && OnlineReady;   // coming soon: greyed out, can't be typed in
            bool typo = !fromFile && CodeTypo(txtCode.Text);
            txtCode.BackColor = typo ? Theme.InvalidBack : Theme.Input;
            btnGet.Enabled = !fromFile && OnlineReady && !typo && Backup.Normalize(txtCode.Text) != null;
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
            else
            {
                infoSummary.Kind = InfoKind.Info;
                infoSummary.Text = L.S("Restoring from an online code is coming soon. Until then, restore from a backup file.",
                                       "Gendannelse fra en online-kode kommer snart. Indtil da kan du gendanne fra en kopi-fil.");
            }

            bool loaded = data != null;
            rbMerge.Text = list.Count == 0 ? L.S("Merge: add the backup's quotes", "Flet: tilføj kopiens tilbud")
                         : L.F("Merge: add the backup's quotes to the {0} on this PC", "Flet: læg kopiens tilbud til de {0} på denne pc",
                               list.Count.ToString("N0", Num.Dk));
            rbMerge.Enabled = rbReplace.Enabled = loaded;
            chkSettings.Enabled = loaded && data.Settings != null;
            lblSettingsNote.Text = loaded && data.Settings == null
                ? L.S("This backup has no settings (a history.json).", "Kopien har ingen indstillinger (en history.json).")
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
            ShowDone(res, problem, SafetyCopy(now));
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


        internal void SampleCode()
        {
            ShowCode("K7QF2-9XMRT-B3HDW-P5ZNC", new DateTime(2026, 11, 10, 14, 5, 0), new DateTime(2027, 12, 15));
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