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
        CheckBox chkAutoPower, chkVat, chkAutoWear, chkAutoUpdate, chkPayback;
        ComboBox cmbPaybackWho;   // 0 = customers only (FailedPaybackWho 2), 1 = both prices (1)
        Label lblCheck, lnkCheck;   // "v1.1.0 –" / check result, and the clickable "check now"
        /// Called when "check now" finds a newer version (MainForm shows its red update button).
        public Action<UpdateInfo> UpdateFound;
        ComboBox cmbNozzle, cmbProcess, cmbFilament, cmbBed, cmbArea, cmbLang;
        Label lnkRestart;   // "Restart now" under the language hint (only when the choice differs from the running language)
        /// True when the dialog was closed with "Restart now" (MainForm saves and restarts the program).
        public bool RestartRequested;
        InfoBox lblWear;

        /// "" = automatic (Windows language), "en", "da".
        string SelectedLanguage { get { return cmbLang.SelectedIndex == 1 ? "en" : cmbLang.SelectedIndex == 2 ? "da" : ""; } }
        Panel divider;   // red line between the two columns
        bool filling;   // suppress change events while filling the form

        public SettingsForm(AppSettings current) : this(current, 0) { }

        /// failedDebt = History.FailedDebt(history, null): shown as "Not made back yet: x kr." (also while payback is off).
        public SettingsForm(AppSettings current, double failedDebt)
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
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // two columns: left = pricing, right = printer setup & live price; buttons underneath
            // no top padding, so the red divider starts right under the title bar; the columns keep their 2 px top margin
            var outer = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Padding = new Padding(14, 0, 14, 14), Margin = Padding.Empty };
            Controls.Add(outer);
            var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            var t2 = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            // red divider centred in the 28 px gap (13 + 2 + 13), from the top down to just above the bottom row
            divider = new Panel { Width = 2, Anchor = AnchorStyles.Top | AnchorStyles.Bottom, Margin = new Padding(13, 0, 13, 0) };
            outer.Controls.Add(t, 0, 0);
            outer.Controls.Add(divider, 1, 0);
            outer.Controls.Add(t2, 2, 0);

            // ----- left: pricing -----
            Section(t, L.S("Pricing", "Priser"));
            txtWatts = Row(t, L.S("Printer power (avg.)", "Printerens effekt (gns.)"), "W",
                L.S("Bambu Lab A1 averages ~80–110 W when printing PLA.", "Bambu Lab A1 bruger i snit ~80–110 W ved print i PLA."));
            txtFriends = Row(t, L.S("Friends && family markup", "Avance, venner og familie"), "%", null);
            txtOthers = Row(t, L.S("Customer markup", "Avance, kunder"), "%", null);

            Section(t, L.S("Wear && tear – both prices", "Slid – begge priser"));
            txtRate = Row(t, L.S("Machine-time rate", "Maskintimepris"), L.S("kr/h", "kr/t"), null);
            chkAutoWear = Check(t, L.S("Calculate from nozzle, filament && plate", "Beregn ud fra dyse, filament og plade"));
            lblWear = Hint(t, "");

            Section(t, L.S("Optional – customer price only", "Valgfrit – kun kundepris"));
            txtMin = Row(t, L.S("Minimum price", "Minimumspris"), L.S("kr.  (0 = off)", "kr.  (0 = fra)"), null);

            Section(t, L.S("Optional – both prices", "Valgfrit – begge priser"));
            txtFailed = Row(t, L.S("Failed prints", "Fejlprint"), L.S("%  (0 = off)", "%  (0 = fra)"), null);
            // the hint's grey second line = failed prints marked in the price history that new quotes haven't made back yet
            Hint(t, "").SetText(L.S("Added to material + power for prints that fail.", "Lægges oven i materiale + strøm for print, der fejler."),
                                L.F("Not made back yet: {0} kr.", "Ikke tjent ind endnu: {0} kr.", failedDebt.ToString("N2", Num.Dk)));
            // earn back failed prints: the checkbox is the caption of its % row; % and "paid by" only work while ticked
            int pr = t.RowCount++;
            chkPayback = new CheckBox { Text = L.S("Earn back failed prints", "Tjen fejlprint ind igen"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, Theme.LabelGap, 6) };
            t.Controls.Add(chkPayback, 0, pr);
            txtPayback = new TextBox { Width = 90, TextAlign = HorizontalAlignment.Right, Anchor = AnchorStyles.Left };
            txtPayback.Enter += delegate { BeginInvoke((MethodInvoker)delegate { txtPayback.SelectAll(); }); };
            t.Controls.Add(txtPayback, 1, pr);
            t.Controls.Add(new Label { Text = L.S("% per quote", "% pr. tilbud"), AutoSize = true, Anchor = AnchorStyles.Left, Tag = "muted" }, 2, pr);
            cmbPaybackWho = Combo(t, L.S("Paid by", "Betales af"));
            cmbPaybackWho.Items.Add(L.S("Customers only", "Kun kunder"));
            cmbPaybackWho.Items.Add(L.S("Both prices", "Begge priser"));
            chkPayback.CheckedChanged += delegate { txtPayback.Enabled = cmbPaybackWho.Enabled = chkPayback.Checked; };
            txtRound = Row(t, L.S("Round up to nearest", "Rund op til nærmeste"), L.S("kr.  (0 = off)", "kr.  (0 = fra)"), null);

            // ----- right: printer setup -----
            Section(t2, L.S("Printer setup (used for slicing && wear)", "Printeropsætning (til slicing og slid)"));
            cmbNozzle = Combo(t2, L.S("Nozzle", "Dyse"));
            foreach (var n in Wear.Nozzles) cmbNozzle.Items.Add(NozzleLabel(n));
            cmbProcess = Combo(t2, L.S("Print profile", "Printprofil"));
            cmbFilament = Combo(t2, L.S("Filament profile", "Filamentprofil"));
            cmbBed = Combo(t2, L.S("Build plate", "Byggeplade"));
            foreach (var p in Wear.Plates) cmbBed.Items.Add(L.Danish && p.Label == "Smooth PEI / High Temp Plate" ? "Glat PEI / High Temp" : p.Label);   // short: fits the combo ("Byggeplade" already says plate)
            txtSlicer = Row(t2, L.S("Bambu Studio path", "Sti til Bambu Studio"), "", exe == null
                ? L.S("Bambu Studio not found. Install it, or enter the path to bambu-studio.exe.",
                      "Bambu Studio blev ikke fundet. Installér det, eller angiv stien til bambu-studio.exe.")
                : L.F("Found: {0}. Leave empty to auto-detect.", "Fundet: {0}. Lad feltet være tomt for at finde det automatisk.", exe));
            // stretch to the right edge of the info boxes below (the column is as wide as they are)
            txtSlicer.Width = 120;   // only the minimum: must stay narrower than the info boxes so they set the column width
            txtSlicer.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtSlicer.Margin = new Padding(3, 3, 0, 3);
            txtSlicer.TextAlign = HorizontalAlignment.Left;
            t2.SetColumnSpan(txtSlicer, 2);

            // ----- right: electricity -----
            Section(t2, L.S("Electricity price (live from Energinet)", "Elpris (live fra Energinet)"));
            chkAutoPower = Check(t2, L.S("Fetch the current price when the program starts", "Hent den aktuelle pris, når programmet starter"));
            cmbArea = Combo(t2, L.S("Price area", "Prisområde"));
            cmbArea.Items.Add(L.S("DK1 – West Denmark", "DK1 – Vestdanmark"));
            cmbArea.Items.Add(L.S("DK2 – East Denmark", "DK2 – Østdanmark"));
            Hint(t2, L.S("DK1 = Jutland & Funen. DK2 = Zealand, Copenhagen, Lolland-Falster & Bornholm.",
                         "DK1 = Jylland og Fyn. DK2 = Sjælland, København, Lolland-Falster og Bornholm."));
            txtAddOn = Row(t2, L.S("Tariffs, taxes && fees", "Tariffer, afgifter og gebyrer"), "kr/kWh", L.S(
                "Added to the spot price (excl. VAT): your grid company's tariff, Energinet (~0,12), " +
                "elafgift (0,008 in 2026) and your supplier's fee. Check your electricity bill.",
                "Lægges oven i spotprisen (ekskl. moms): dit netselskabs tarif, Energinet (~0,12), " +
                "elafgift (0,008 i 2026) og dit elselskabs gebyr. Se din elregning."));
            chkVat = Check(t2, L.S("Add 25 % VAT", "Læg 25 % moms til"));

            // ----- right: language (written in both languages so it can be found whichever one is showing) -----
            Section(t2, "Sprog / Language");
            cmbLang = Combo(t2, L.S("Language", "Sprog"));
            cmbLang.Items.Add(L.S("Automatic (Windows)", "Automatisk (Windows)"));
            cmbLang.Items.Add("English");
            cmbLang.Items.Add("Dansk");
            // "Restart now" only shows when the choice differs from the language that is showing now
            lnkRestart = new Theme.LinkText { Text = L.S("Restart now", "Genstart nu"), AutoSize = true, Visible = false, Margin = new Padding(0, 0, 0, 4) };
            lnkRestart.Font = new Font(Font, FontStyle.Underline);
            lnkRestart.Click += delegate { RestartRequested = true; Accept(); };
            int lr = t2.RowCount++;
            t2.Controls.Add(lnkRestart, 0, lr);
            t2.SetColumnSpan(lnkRestart, 3);
            cmbLang.SelectedIndexChanged += delegate { lnkRestart.Visible = AppSettings.IsDanish(SelectedLanguage) != L.Danish; };

            // bottom row: update-check switch on the left, version link + buttons on the right
            var bottom = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            chkAutoUpdate = new CheckBox { Text = L.S("Check for new versions automatically", "Søg automatisk efter nye versioner"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 12, 4) };
            bottom.Controls.Add(chkAutoUpdate, 0, 0);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = Padding.Empty, WrapContents = false };
            bottom.Controls.Add(buttons, 1, 0);
            var ok = new Button { Text = "OK", AutoSize = true };
            var cancel = new Button { Text = L.S("Cancel", "Annullér"), AutoSize = true, DialogResult = DialogResult.Cancel };
            var reset = new Button { Text = L.S("Defaults", "Standard"), AutoSize = true };
            ok.Click += delegate { Accept(); };
            reset.Click += delegate { Fill(new AppSettings()); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(reset);
            // installed version + "check now" link, left of Defaults (a found update shows the red button in the main window)
            // (flow is right-to-left: the link is added first so it sits right of the text)
            lnkCheck = new Theme.LinkText { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
            lnkCheck.Font = new Font(Font, FontStyle.Underline);
            lnkCheck.Click += delegate { CheckNow(); };
            lblCheck = new Theme.PlainLabel { AutoSize = true, Tag = "muted", Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 0, 0) };
            buttons.Controls.Add(lnkCheck);
            buttons.Controls.Add(lblCheck);
            if (AppSettings.TestMode)   // test mode never looks for updates: muted text, no link
                SetCheckText("v" + Updater.CurrentText + L.S(" – check off in test mode", " – tjek slået fra i testtilstand"), null, null);
            else
                SetCheckText("v" + Updater.CurrentText + " –", L.S("check now", "tjek nu"), null);
            outer.Controls.Add(bottom, 0, 1);
            outer.SetColumnSpan(bottom, 3);

            // live updates
            cmbNozzle.SelectedIndexChanged += delegate { if (!filling) { RefillPresets(); UpdateWear(); } };
            cmbFilament.SelectedIndexChanged += delegate { if (!filling) UpdateWear(); };
            cmbBed.SelectedIndexChanged += delegate { if (!filling) UpdateWear(); };
            chkAutoWear.CheckedChanged += delegate { if (!filling) UpdateWear(); };

            AcceptButton = ok;
            CancelButton = cancel;
            Theme.Apply(this);
            divider.BackColor = Theme.InfoAccent;   // after Apply, which paints panels in the background colour
            ResumeLayout(true);
            Fill(current);
            if (current.AutoUpdateCheck != 0 && !AppSettings.TestMode && !Screenshots.Active)
                Shown += delegate { QuietCheckOnOpen(); };   // auto update on: look when Settings opens
            Load += delegate { FitCombos(this); };
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

        TextBox Row(TableLayoutPanel t, string caption, string unit, string hint)
        {
            int r = t.RowCount++;
            t.Controls.Add(Caption(caption), 0, r);
            var box = new TextBox { Width = 90, TextAlign = HorizontalAlignment.Right, Anchor = AnchorStyles.Left };
            t.Controls.Add(box, 1, r);
            // number boxes (all have a unit; the slicer path has none) select their text when tabbed or clicked into
            if (unit.Length > 0) box.Enter += delegate { BeginInvoke((MethodInvoker)delegate { box.SelectAll(); }); };
            if (unit.Length > 0)
                t.Controls.Add(new Label { Text = unit, AutoSize = true, Anchor = AnchorStyles.Left, Tag = "muted" }, 2, r);
            if (hint != null) Hint(t, hint);
            return box;
        }

        static ComboBox Combo(TableLayoutPanel t, string caption)
        {
            int r = t.RowCount++;
            t.Controls.Add(Caption(caption), 0, r);
            // stretches to the right edge of the info boxes (Width is only the minimum, kept small so the
            // info boxes, not caption + combo, decide the column width)
            // Top-anchored like a centred number box (row 31 = box 25 + 3 + 3): the table centres a combo by a height
            // smaller than the one it really gets, which put it 2 px low; its height = a box's (FitCombos, on Load)
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3, 3, 0, 3), MaxDropDownItems = 20 };
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

        /// Help text in its own shaded box under the setting it explains.
        static InfoBox Hint(TableLayoutPanel t, string text)
        {
            int h = t.RowCount++;
            var box = new InfoBox(380, 9f) { Margin = new Padding(0, 2, 0, 4), Text = text };
            t.Controls.Add(box, 0, h);
            t.SetColumnSpan(box, 3);
            return box;
        }

        void Section(TableLayoutPanel t, string text)
        {
            int r = t.RowCount++;
            var lbl = new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 6, 0, 4) };
            t.Controls.Add(lbl, 0, r);
            t.SetColumnSpan(lbl, 3);
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
            SetItems(cmbProcess, BambuSlicer.ListProcesses(exe, size), process, "Optimal");
            SetItems(cmbFilament, BambuSlicer.ListFilaments(exe, size), filament, "Bambu PLA Matte");
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
            try { BambuSlicer.FilamentInfo(exe, filament, nozzle.Size, out type, out abrasive); }
            catch { /* no Bambu Studio / profile: treat as normal filament */ }

            var b = Wear.Calculate(nozzle, abrasive, type, plate);
            bool auto = chkAutoWear.Checked;
            txtRate.ReadOnly = auto;
            if (auto) txtRate.Text = Num.Fmt(b.Total);

            // one line per part so the sum is easy to follow
            var rows = new[]
            {
                new[] { L.S("Printer (depreciation)", "Printer (afskrivning)"), Kr(b.Printer) },
                new[] { L.S("Belts, PTFE tube, wiper…", "Remme, PTFE-rør, wiper…"), Kr(b.Parts) },
                new[] { "Hotend  (" + nozzle.PriceDkk + " kr. / " + Hours(b.HotendLife) + ")", Kr(b.Hotend) },
                new[] { L.S("Plate", "Plade") + "  (" + plate.PriceDkk + " kr. / " + Hours(b.PlateLife) + ")", Kr(b.Plate) },
                new[] { L.S("= Wear per print hour", "= Slid pr. printtime"), Kr(b.Total) + " kr." },
            };
            var notes = new List<string>();
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
        void Fill(AppSettings s)
        {
            filling = true;
            txtWatts.Text = Num.Fmt(s.PrinterWatts);
            txtFriends.Text = Num.Fmt(s.FriendsMarkupPct);
            txtOthers.Text = Num.Fmt(s.OthersMarkupPct);
            txtRate.Text = Num.Fmt(s.MachineRatePerHour);
            chkAutoWear.Checked = s.AutoWearRate != 0;
            txtMin.Text = Num.Fmt(s.MinimumPrice);
            txtRound.Text = Num.Fmt(s.RoundTo);
            txtFailed.Text = Num.Fmt(s.FailedPrintPct);
            txtPayback.Text = Num.Fmt(s.FailedPaybackPct);
            cmbPaybackWho.SelectedIndex = s.FailedPaybackWho == 1 ? 1 : 0;
            chkPayback.Checked = s.FailedPayback != 0;
            txtPayback.Enabled = cmbPaybackWho.Enabled = chkPayback.Checked;   // CheckedChanged doesn't fire when it stays the same
            cmbNozzle.SelectedIndex = Array.IndexOf(Wear.Nozzles, Wear.FindNozzle(s.Nozzle));
            RefillPresets(BambuSlicer.ShortName(s.ProcessPreset), BambuSlicer.ShortName(s.FilamentPreset));
            cmbBed.SelectedIndex = Array.IndexOf(Wear.Plates, Wear.FindPlate(s.BedType));
            txtSlicer.Text = s.SlicerPath;
            chkAutoPower.Checked = s.AutoPowerPrice != 0;
            cmbArea.SelectedIndex = s.PriceArea == "DK1" ? 0 : 1;
            txtAddOn.Text = Num.Fmt(s.PowerAddOn);
            chkVat.Checked = s.PowerIncludeVat != 0;
            chkAutoUpdate.Checked = s.AutoUpdateCheck != 0;
            cmbLang.SelectedIndex = s.Language == "en" ? 1 : s.Language == "da" ? 2 : 0;
            filling = false;
            UpdateWear();
        }

        void Accept()
        {
            double w, f, o, rate, min, round, failed, addOn;
            bool ok = Num.Read(txtWatts, out w) == FieldState.Ok;
            ok &= Num.Read(txtFriends, out f) == FieldState.Ok;
            ok &= Num.Read(txtOthers, out o) == FieldState.Ok;
            ok &= Num.Read(txtRate, out rate) == FieldState.Ok;
            ok &= Num.Read(txtMin, out min) == FieldState.Ok;
            ok &= Num.Read(txtRound, out round) == FieldState.Ok;
            ok &= Num.Read(txtFailed, out failed) == FieldState.Ok;
            ok &= Num.Read(txtAddOn, out addOn) == FieldState.Ok;
            // payback %: checked only while it is used (a greyed-out box can't be fixed); off = a valid number is kept, else the old one
            double payback = source.FailedPaybackPct;
            if (chkPayback.Checked) ok &= Num.Read(txtPayback, out payback) == FieldState.Ok;
            else
            {
                double v;
                if (Num.TryParse(txtPayback.Text, out v)) payback = v;
                txtPayback.BackColor = Theme.Input;
            }
            if (!ok)
            {
                RestartRequested = false;
                string msg = L.S("Please enter valid, non-negative numbers in the highlighted fields.",
                                 "Indtast gyldige tal, ikke negative, i de markerede felter.");
                // hidden test start: no message box (it would pop up on screen), red text in the bottom row instead
                if (Program.Hidden) SetCheckText(msg, null, Theme.Error);
                else MessageBox.Show(this, msg, Text);
                return;
            }
            Result = source.Clone();
            Result.PrinterWatts = w;
            Result.FriendsMarkupPct = f;
            Result.OthersMarkupPct = o;
            Result.MachineRatePerHour = rate;
            Result.AutoWearRate = chkAutoWear.Checked ? 1 : 0;
            Result.MinimumPrice = min;
            Result.RoundTo = round;
            Result.FailedPrintPct = failed;
            Result.FailedPayback = chkPayback.Checked ? 1 : 0;
            Result.FailedPaybackPct = payback;
            Result.FailedPaybackWho = cmbPaybackWho.SelectedIndex == 1 ? 1 : 2;
            Result.Nozzle = SelectedNozzle.Key;
            Result.ProcessPreset = (string)cmbProcess.SelectedItem ?? source.ProcessPreset;
            Result.FilamentPreset = (string)cmbFilament.SelectedItem ?? source.FilamentPreset;
            Result.BedType = Wear.Plates[Math.Max(0, cmbBed.SelectedIndex)].Key;
            Result.SlicerPath = txtSlicer.Text.Trim().Trim('"');
            Result.AutoPowerPrice = chkAutoPower.Checked ? 1 : 0;
            Result.PriceArea = cmbArea.SelectedIndex == 0 ? "DK1" : "DK2";
            Result.PowerAddOn = addOn;
            Result.PowerIncludeVat = chkVat.Checked ? 1 : 0;
            Result.AutoUpdateCheck = chkAutoUpdate.Checked ? 1 : 0;
            Result.Language = SelectedLanguage;
            DialogResult = DialogResult.OK;
            Close();
        }

        // ---------- "check now" (only when clicked; never in picture mode) ----------
        /// Text left of Defaults, then an optional clickable part ("check now" / "try again"); colour null = muted.
        internal void SetCheckText(string text, string link, Color? colour)
        {
            lblCheck.Text = text;
            lblCheck.ForeColor = colour ?? Theme.Muted;
            lnkCheck.Text = link ?? "";
            lnkCheck.Visible = link != null;
            lblCheck.Margin = new Padding(3, 0, link != null ? 0 : 8, 0);
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
