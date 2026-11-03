using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintPrice
{
    public class SettingsForm : Form
    {
        public AppSettings Result;
        readonly AppSettings source;
        readonly string exe;
        TextBox txtWatts, txtFriends, txtOthers, txtRate, txtMin, txtRound, txtAddOn, txtSlicer;
        CheckBox chkAutoPower, chkVat, chkAutoWear, chkAutoUpdate;
        Label lblCheck, lnkCheck;   // "v1.1.0 –" / check result, and the clickable "check now"
        /// Called when "check now" finds a newer version (MainForm shows its red update button).
        public Action<UpdateInfo> UpdateFound;
        ComboBox cmbNozzle, cmbProcess, cmbFilament, cmbBed, cmbArea;
        InfoBox lblWear;
        bool filling;   // suppress change events while filling the form

        public SettingsForm(AppSettings current)
        {
            source = current;
            exe = BambuSlicer.FindExe(current.SlicerPath);
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // two columns: left = pricing, right = printer setup & live price; buttons underneath
            var outer = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(14, 2, 14, 14), Margin = Padding.Empty };
            Controls.Add(outer);
            var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 0, 28, 0) };
            var t2 = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = Padding.Empty };
            outer.Controls.Add(t, 0, 0);
            outer.Controls.Add(t2, 1, 0);

            // ----- left: pricing -----
            Section(t, "Pricing");
            txtWatts = Row(t, "Printer power (avg.)", "W", "Bambu Lab A1 averages ~80–110 W when printing PLA.");
            txtFriends = Row(t, "Friends && family markup", "%", null);
            txtOthers = Row(t, "Customer markup", "%", null);

            Section(t, "Wear && tear – both prices");
            txtRate = Row(t, "Machine-time rate", "kr./h", null);
            chkAutoWear = Check(t, "Calculate from nozzle, filament && plate");
            lblWear = Hint(t, "");

            Section(t, "Optional – customer price only");
            txtMin = Row(t, "Minimum price", "kr.  (0 = off)", null);

            Section(t, "Optional – both prices");
            txtRound = Row(t, "Round up to nearest", "kr.  (0 = off)", null);

            // ----- right: printer setup -----
            Section(t2, "Printer setup (used for slicing && wear)");
            cmbNozzle = Combo(t2, "Nozzle");
            foreach (var n in Wear.Nozzles) cmbNozzle.Items.Add(n.Label);
            cmbProcess = Combo(t2, "Print profile");
            cmbFilament = Combo(t2, "Filament profile");
            cmbBed = Combo(t2, "Build plate");
            foreach (var p in Wear.Plates) cmbBed.Items.Add(p.Label);
            txtSlicer = Row(t2, "Bambu Studio path", "", exe == null
                ? "Bambu Studio not found. Install it, or enter the path to bambu-studio.exe."
                : "Found: " + exe + ". Leave empty to auto-detect.");
            // stretch to the right edge of the info boxes below (the column is as wide as they are)
            txtSlicer.Width = 200;
            txtSlicer.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtSlicer.Margin = new Padding(3, 3, 0, 3);
            txtSlicer.TextAlign = HorizontalAlignment.Left;
            t2.SetColumnSpan(txtSlicer, 2);

            // ----- right: electricity -----
            Section(t2, "Electricity price (live from Energinet)");
            chkAutoPower = Check(t2, "Fetch the current price when the program starts");
            cmbArea = Combo(t2, "Price area");
            cmbArea.Items.Add("DK1 – West Denmark");
            cmbArea.Items.Add("DK2 – East Denmark");
            Hint(t2, "DK1 = Jutland & Funen. DK2 = Zealand, Copenhagen, Lolland-Falster & Bornholm.");
            txtAddOn = Row(t2, "Tariffs, taxes && fees", "kr./kWh",
                "Added to the spot price (excl. VAT): your grid company's tariff, Energinet (~0,12), " +
                "elafgift (0,008 in 2026) and your supplier's fee. Check your electricity bill.");
            chkVat = Check(t2, "Add 25 % VAT");

            // bottom row: update-check switch on the left, version link + buttons on the right
            var bottom = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            chkAutoUpdate = new CheckBox { Text = "Check for a new version at startup", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 12, 4) };
            bottom.Controls.Add(chkAutoUpdate, 0, 0);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = Padding.Empty, WrapContents = false };
            bottom.Controls.Add(buttons, 1, 0);
            var ok = new Button { Text = "OK", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var reset = new Button { Text = "Defaults", AutoSize = true };
            ok.Click += delegate { Accept(); };
            reset.Click += delegate { Fill(new AppSettings()); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(reset);
            // installed version + "check now" link, left of Defaults (a found update shows the red button in the main window)
            // (flow is right-to-left: the link is added first so it sits right of the text)
            lnkCheck = new Theme.PlainLabel { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand };
            lnkCheck.Font = new Font(Font, FontStyle.Underline);
            lnkCheck.Click += delegate { CheckNow(); };
            lblCheck = new Theme.PlainLabel { AutoSize = true, Tag = "muted", Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 0, 0) };
            buttons.Controls.Add(lnkCheck);
            buttons.Controls.Add(lblCheck);
            SetCheckText("v" + Updater.CurrentText + " –", "check now", null);
            outer.Controls.Add(bottom, 0, 1);
            outer.SetColumnSpan(bottom, 2);

            // live updates
            cmbNozzle.SelectedIndexChanged += delegate { if (!filling) { RefillPresets(); UpdateWear(); } };
            cmbFilament.SelectedIndexChanged += delegate { if (!filling) UpdateWear(); };
            cmbBed.SelectedIndexChanged += delegate { if (!filling) UpdateWear(); };
            chkAutoWear.CheckedChanged += delegate { if (!filling) UpdateWear(); };

            AcceptButton = ok;
            CancelButton = cancel;
            Theme.Apply(this);
            ResumeLayout(true);
            Fill(current);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override bool ShowWithoutActivation { get { return Screenshots.Active; } }

        // ---------- layout helpers ----------
        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6) };
        }

        TextBox Row(TableLayoutPanel t, string caption, string unit, string hint)
        {
            int r = t.RowCount++;
            t.Controls.Add(Caption(caption), 0, r);
            var box = new TextBox { Width = 90, TextAlign = HorizontalAlignment.Right, Anchor = AnchorStyles.Left };
            t.Controls.Add(box, 1, r);
            if (unit.Length > 0)
                t.Controls.Add(new Label { Text = unit, AutoSize = true, Anchor = AnchorStyles.Left, Tag = "muted" }, 2, r);
            if (hint != null) Hint(t, hint);
            return box;
        }

        static ComboBox Combo(TableLayoutPanel t, string caption)
        {
            int r = t.RowCount++;
            t.Controls.Add(Caption(caption), 0, r);
            // stretches to the right edge of the info boxes (Width is only the minimum)
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3, 3, 0, 3), MaxDropDownItems = 20 };
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
            var box = new InfoBox(380, 9f) { Margin = new Padding(0, 2, 0, 8), Text = text };
            t.Controls.Add(box, 0, h);
            t.SetColumnSpan(box, 3);
            return box;
        }

        void Section(TableLayoutPanel t, string text)
        {
            int r = t.RowCount++;
            var lbl = new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 12, 0, 4) };
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
            SetItems(cmbProcess, BambuSlicer.ListProcesses(exe, size), process, "Standard");
            SetItems(cmbFilament, BambuSlicer.ListFilaments(exe, size), filament, "Bambu PLA Basic");
            filling = was;
        }

        // ---------- wear rate ----------
        static string Kr(double v) { return v.ToString("0.00", Num.Dk); }
        static string Hours(double h) { return h.ToString("#,0", Num.Dk) + " h"; }

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
                new[] { "Printer (depreciation)", Kr(b.Printer) },
                new[] { "Belts, PTFE tube, wiper…", Kr(b.Parts) },
                new[] { "Hotend  (" + nozzle.PriceDkk + " kr. / " + Hours(b.HotendLife) + ")", Kr(b.Hotend) },
                new[] { "Plate  (" + plate.PriceDkk + " kr. / " + Hours(b.PlateLife) + ")", Kr(b.Plate) },
                new[] { "= Wear per print hour", Kr(b.Total) + " kr." },
            };
            var notes = new List<string>();
            if (!auto) notes.Add("Not used: tick the box above to use this rate.");
            if (b.AbrasiveOnStainless)
                notes.Add("⚠ " + filament + " is abrasive and needs a hardened steel nozzle. A stainless nozzle wears out in ~100 h.");
            else if (b.Abrasive)
                notes.Add(filament + " is abrasive: the hardened nozzle lasts ~" + Hours(b.HotendLife) + ".");
            if (b.PlateHalved)
                notes.Add(type + " can tear the smooth PEI surface (use glue stick), so the plate's life is halved.");
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
            cmbNozzle.SelectedIndex = Array.IndexOf(Wear.Nozzles, Wear.FindNozzle(s.Nozzle));
            RefillPresets(BambuSlicer.ShortName(s.ProcessPreset), BambuSlicer.ShortName(s.FilamentPreset));
            cmbBed.SelectedIndex = Array.IndexOf(Wear.Plates, Wear.FindPlate(s.BedType));
            txtSlicer.Text = s.SlicerPath;
            chkAutoPower.Checked = s.AutoPowerPrice != 0;
            cmbArea.SelectedIndex = s.PriceArea == "DK1" ? 0 : 1;
            txtAddOn.Text = Num.Fmt(s.PowerAddOn);
            chkVat.Checked = s.PowerIncludeVat != 0;
            chkAutoUpdate.Checked = s.AutoUpdateCheck != 0;
            filling = false;
            UpdateWear();
        }

        void Accept()
        {
            double w, f, o, rate, min, round, addOn;
            bool ok = Num.Read(txtWatts, out w) == FieldState.Ok;
            ok &= Num.Read(txtFriends, out f) == FieldState.Ok;
            ok &= Num.Read(txtOthers, out o) == FieldState.Ok;
            ok &= Num.Read(txtRate, out rate) == FieldState.Ok;
            ok &= Num.Read(txtMin, out min) == FieldState.Ok;
            ok &= Num.Read(txtRound, out round) == FieldState.Ok;
            ok &= Num.Read(txtAddOn, out addOn) == FieldState.Ok;
            if (!ok)
            {
                MessageBox.Show(this, "Please enter valid, non-negative numbers in the highlighted fields.", "Settings");
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

        async void CheckNow()
        {
            if (Screenshots.Active) return;
            SetCheckText("Checking for a new version…", null, null);
            UpdateInfo found;
            try { found = await Task.Run(() => Updater.CheckForUpdate()); }
            catch
            {
                if (!IsDisposed) SetCheckText("Check failed –", "try again", Theme.Error);
                return;
            }
            if (IsDisposed) return;
            if (found == null)
            {
                SetCheckText("✓ v" + Updater.CurrentText + " is the newest", null, null);   // muted
                return;
            }
            SetCheckText("v" + Updater.Short(found.Version) + " is out – see main window", null, Theme.Text);
            if (UpdateFound != null) UpdateFound(found);
        }
    }
}
