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
        Label lblPrinter, lblMaterial, lblPower, lblBase;
        Label lblFriends, lblOthers, lblFriendsInfo, lblOthersInfo, lblStatus;
        Button btnCopy, btnUpdate;
        UpdateInfo pendingUpdate;   // newer version found at startup (null = none)
        QuoteResult last;
        int lastHours, lastMinutes;
        bool statusIsSuccess;

        // auto-detect
        Button btnAnalyze, btnLoadFile;
        InfoBox lblAuto;
        bool autoIsError;
        CancellationTokenSource cts;

        // live electricity price
        Button btnPower;
        InfoBox lblPowerInfo;
        bool powerIsError, fetchingPower;

        public MainForm()
        {
            settings = AppSettings.Load();
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
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // link box spans to the right edge of the panels below (540 px wide content)
            txtUrl = new TextBox { Width = 394 };
            // grey example text while empty; re-applied whenever the handle is recreated (e.g. theme change)
            txtUrl.HandleCreated += delegate { SetCueBanner(txtUrl, "e.g. https://www.printables.com/model/3161-3d-benchy"); };
            AddRow(inputs, "Model link", txtUrl, null);
            inputs.SetColumnSpan(txtUrl, 2);

            // two equal buttons lined up exactly under the link box
            btnAnalyze = new Button { Text = "Get time && weight", Width = 194, Height = 32, Margin = new Padding(3, 6, 6, 4) };
            btnLoadFile = new Button { Text = "Load file…", Width = 194, Height = 32, Margin = new Padding(0, 6, 0, 4) };
            btnAnalyze.Click += delegate { AnalyzeClicked(); };
            btnLoadFile.Click += delegate { BrowseForModel(); };
            var autoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            autoRow.Controls.Add(btnAnalyze);
            autoRow.Controls.Add(btnLoadFile);
            AddRow(inputs, "", autoRow, null);
            inputs.SetColumnSpan(autoRow, 2);
            lblAuto = new InfoBox(394, 9.5f) { Margin = new Padding(3, 4, 3, 10) };
            AddRow(inputs, "", lblAuto, null);
            inputs.SetColumnSpan(lblAuto, 2);

            txtHours = new TextBox { Width = 50, Text = "0", TextAlign = HorizontalAlignment.Right };
            txtMinutes = new TextBox { Width = 50, Text = "0", TextAlign = HorizontalAlignment.Right };
            var timeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            timeRow.Controls.Add(txtHours);
            timeRow.Controls.Add(Unit("h"));
            timeRow.Controls.Add(txtMinutes);
            timeRow.Controls.Add(Unit("m"));
            AddRow(inputs, "Print time", timeRow, null);

            txtGrams = NumBox("");
            AddRow(inputs, "Filament used", WithUnit(txtGrams, "g"), null);
            txtFilamentPrice = NumBox(Num.Fmt(settings.LastFilamentPrice));
            AddRow(inputs, "Filament price", WithUnit(txtFilamentPrice, "DKK/kg"), null);
            txtElecPrice = NumBox(Num.Fmt(settings.LastElectricityPrice));
            var elecRow = (FlowLayoutPanel)WithUnit(txtElecPrice, "DKK/kWh");
            // same top margin as the text box; height is matched to the box once the form has loaded (DPI-safe)
            // slightly smaller font + no padding so the text fits inside a button only as tall as the text box
            btnPower = new Button
            {
                Text = "Update", AutoSize = false, Width = 76, Margin = new Padding(4, 3, 0, 3),
                Padding = Padding.Empty, Font = new Font("Segoe UI", 9f), TextAlign = ContentAlignment.MiddleCenter
            };
            Load += delegate
            {
                btnPower.Height = txtElecPrice.Height;
                btnPower.Margin = new Padding(btnPower.Margin.Left, txtElecPrice.Margin.Top, 0, txtElecPrice.Margin.Bottom);
            };
            btnPower.Click += delegate { FetchPowerPrice(); };
            elecRow.Controls.Add(btnPower);
            AddRow(inputs, "Electricity price", elecRow, null);
            lblPowerInfo = new InfoBox(394, 9.5f) { Margin = new Padding(3, 2, 3, 8) };
            AddRow(inputs, "", lblPowerInfo, null);
            inputs.SetColumnSpan(lblPowerInfo, 2);
            lblPrinter = new Label { AutoSize = true, Tag = "muted", Margin = new Padding(3, 4, 3, 4) };
            AddRow(inputs, "Printer", lblPrinter, null);
            root.Controls.Add(inputs);

            // ---------- Breakdown ----------
            var grpBreak = new ThemedGroupBox { Text = "Cost breakdown", Width = 540, Height = 110, Margin = new Padding(0, 0, 0, 8) };
            var bt = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2) };
            bt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            bt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            lblMaterial = BreakLabel(); lblPower = BreakLabel(); lblBase = BreakLabel();
            bt.Controls.Add(BreakCaption("Material"), 0, 0); bt.Controls.Add(lblMaterial, 1, 0);
            bt.Controls.Add(BreakCaption("Power"), 0, 1); bt.Controls.Add(lblPower, 1, 1);
            var baseCap = BreakCaption("Base cost"); baseCap.Font = new Font(Font, FontStyle.Bold);
            lblBase.Font = new Font(Font, FontStyle.Bold);
            bt.Controls.Add(baseCap, 0, 2); bt.Controls.Add(lblBase, 1, 2);
            grpBreak.Controls.Add(bt);
            root.Controls.Add(grpBreak);

            // ---------- Prices ----------
            var prices = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            prices.Controls.Add(PriceBox("Friends && family", "friends", out lblFriends, out lblFriendsInfo), 0, 0);
            var custBox = PriceBox("Customers", "others", out lblOthers, out lblOthersInfo);
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
            var btnClear = new Button { Text = "Clear", Size = new Size(76, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 6, 3) };
            var btnSettings = new Button { Text = "Settings…", Size = new Size(84, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 6, 3) };
            btnCopy = new Button { Text = "Copy quote", Size = new Size(92, 32), Padding = Padding.Empty, Margin = new Padding(0, 3, 0, 3) };
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
            EnableDrop(this);

            ApplyTheme();
            ResumeLayout(true);
            RefreshSettingsLabels();
            ShowAutoIdle();
            Shown += delegate { StartupUpdateCheck(); };
            if (settings.AutoPowerPrice != 0) Shown += delegate { FetchPowerPrice(); };
            else SetPowerInfo("Live price is off (Settings). Click Update to fetch it once.", false);
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
            if (fetchingPower) return;
            fetchingPower = true;
            btnPower.Enabled = false;
            string area = settings.PriceArea;
            double addOn = settings.PowerAddOn;
            bool vat = settings.PowerIncludeVat != 0;
            string before = txtElecPrice.Text;
            SetPowerInfo("Fetching the current electricity price for " + area + "…", false);
            try
            {
                var q = await Task.Run(() => PowerPrice.GetCurrent(area));
                double total = Math.Round(PowerPrice.TotalPrice(q.SpotDkkPerKwh, addOn, vat), 2);
                string breakdown = "spot " + q.SpotDkkPerKwh.ToString("0.00", Num.Dk) +
                                   " + fees " + addOn.ToString("0.00", Num.Dk) + (vat ? " + 25 % VAT" : "");
                string when = q.Area + " " + q.SlotStartUtc.ToLocalTime().ToString("HH:mm", Num.Dk);
                if (txtElecPrice.Text == before)
                {
                    txtElecPrice.Text = total.ToString("0.00", Num.Dk);
                    SetPowerInfo("Live " + when + ": " + breakdown + " = " + total.ToString("0.00", Num.Dk) + " kr.", false);
                }
                else
                {
                    SetPowerInfo("Live price " + when + " is " + total.ToString("0.00", Num.Dk) +
                                 " kr./kWh (not applied because you edited the field).", false);
                }
            }
            catch (Exception ex)
            {
                string why = ex is SliceException ? ex.Message : "unexpected error";
                SetPowerInfo("Couldn't get the live price (" + why + "). Using your last price.", true);
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
            if (lblAuto != null)
                lblAuto.Kind = autoIsError ? InfoKind.Error
                             : lblAuto.Text.StartsWith("✓") ? InfoKind.Success
                             : InfoKind.Info;
        }

        void ShowAutoIdle()
        {
            if (BambuSlicer.FindExe(settings.SlicerPath) == null)
                SetAuto("Bambu Studio was not found. Install it to detect time and weight automatically.", false);
            else
                SetAuto("Paste a Printables link, or drop a model file (STL/3MF) on this window. Slices with " +
                        Wear.FindNozzle(settings.Nozzle).Size + " mm nozzle · " +
                        BambuSlicer.ShortName(settings.ProcessPreset) + " · " + BambuSlicer.ShortName(settings.FilamentPreset) + ".", false);
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
                if (models.Count == 0) { SetAuto("That isn't a model file. Use STL, 3MF, OBJ or STEP.", true); return; }
                txtUrl.Text = models.Count == 1 ? models[0] : models.Count + " files: " + string.Join(", ", models.Select(Path.GetFileName));
                Analyze(null, models);
            };
            foreach (Control child in c.Controls) EnableDrop(child);
        }

        void BrowseForModel()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Choose model file(s)", Multiselect = true,
                Filter = "3D models (*.stl;*.3mf;*.obj;*.step;*.stp)|*.stl;*.3mf;*.obj;*.step;*.stp;*.amf|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var files = dlg.FileNames.ToList();
                txtUrl.Text = files.Count == 1 ? files[0] : files.Count + " files: " + string.Join(", ", files.Select(Path.GetFileName));
                Analyze(null, files);
            }
        }

        void AnalyzeClicked()
        {
            if (cts != null) { cts.Cancel(); return; }
            string src = txtUrl.Text.Trim().Trim('"');
            if (src.Length == 0) { BrowseForModel(); return; }
            if (File.Exists(src)) { Analyze(null, new List<string> { src }); return; }

            string id = Printables.ParseId(src);
            if (id != null) { Analyze(id, null); return; }

            string lower = src.ToLowerInvariant();
            if (lower.Contains("makerworld.com") || lower.Contains("thingiverse.com") || lower.Contains("cults3d.com") || lower.Contains("myminifactory.com"))
                SetAuto("This site blocks automatic downloads. Download the model (STL/3MF) from the page and drop the file on this window.", true);
            else
                SetAuto("Paste a Printables link (printables.com/model/…) or load a model file.", true);
        }

        void SetBusy(bool busy)
        {
            btnAnalyze.Text = busy ? "Cancel" : "Get time && weight";
            btnLoadFile.Enabled = !busy;
            txtUrl.ReadOnly = busy;
            UseWaitCursor = false;
        }

        async void Analyze(string printablesId, List<string> localFiles)
        {
            if (cts != null) return;
            string exe = BambuSlicer.FindExe(settings.SlicerPath);
            bool presliced = localFiles != null && localFiles.Count == 1 && BambuSlicer.IsSlicedProject(localFiles[0]);
            if (exe == null && !presliced)
            {
                SetAuto("Bambu Studio was not found. Install it from bambulab.com, or set its location in Settings.", true);
                return;
            }

            string process = settings.ProcessPreset, filament = settings.FilamentPreset, bed = settings.BedType;
            string nozzle = Wear.FindNozzle(settings.Nozzle).Size;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            SetBusy(true);
            IProgress<string> progress = new Progress<string>(s => SetAuto(s, false));
            string modelName = null;
            try
            {
                List<RemoteFile> chosen = null;
                if (printablesId != null)
                {
                    progress.Report("Looking up the model on Printables…");
                    var model = await Task.Run(() => Printables.GetModel(printablesId));
                    token.ThrowIfCancellationRequested();
                    modelName = model.Name;
                    var sliceable = model.Files.Where(f => BambuSlicer.IsModelFile(f.Name)).ToList();
                    if (sliceable.Count == 0) throw new SliceException("\"" + model.Name + "\" has no STL, 3MF or STEP files to slice.");
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

                int totalMinutes = (int)Math.Round(result.Seconds / 60.0);
                if (totalMinutes == 0 && result.Seconds > 0) totalMinutes = 1;
                txtHours.Text = (totalMinutes / 60).ToString();
                txtMinutes.Text = (totalMinutes % 60).ToString();
                txtGrams.Text = Num.Fmt(Math.Round(result.Grams, 1));

                string what = modelName != null ? "\"" + modelName + "\"" :
                              localFiles.Count == 1 ? Path.GetFileName(localFiles[0]) : localFiles.Count + " files";
                string summary = (totalMinutes / 60) + " h " + (totalMinutes % 60) + " m, " +
                                 Math.Round(result.Grams, 1).ToString("0.#", Num.Dk) + " g";
                string how = result.FromSlicedFile
                    ? "read from the sliced file"
                    : BambuSlicer.ShortName(process) + " · " + BambuSlicer.ShortName(filament);
                int parts = chosen != null ? chosen.Count : localFiles.Count;
                SetAuto("✓ " + what + ": " + summary + " (" + how +
                        (parts > 1 ? ", " + parts + " files" : "") +
                        (result.Plates > parts ? ", " + result.Plates + " plates" : "") + ").", false);
            }
            catch (OperationCanceledException) { SetAuto("Cancelled.", false); }
            catch (SliceException ex) { SetAuto(ex.Message, true); }
            catch (Exception ex) { SetAuto("Something went wrong: " + ex.Message, true); }
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
                        progress.Report("Downloading " + remote[i].Name + (remote.Count > 1 ? " (" + (i + 1) + "/" + remote.Count + ")" : "") + "…");
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
                progress.Report("Preparing Bambu Lab A1 profiles…");
                BambuSlicer.PrepareProfiles(exe, nozzle, process, filament, bed, work);

                var total = new SliceResult();
                for (int i = 0; i < models.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report("Slicing " + models[i].Key + (models.Count > 1 ? " (" + (i + 1) + "/" + models.Count + ")" : "") +
                                    " in Bambu Studio…");
                    var r = BambuSlicer.SliceFile(exe, models[i].Value, work, ct);
                    total.Seconds += r.Seconds;
                    total.Grams += r.Grams;
                    total.Plates += r.Plates;
                }
                return total;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// Placeholder text for a TextBox (.NET Framework has no PlaceholderText property).
        static void SetCueBanner(TextBox box, string text)
        {
            const int EM_SETCUEBANNER = 0x1501;
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);   // 1 = keep showing while focused
        }

        // ---------- UI helpers ----------
        void AddRow(TableLayoutPanel t, string caption, Control input, Control extra)
        {
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (caption.Length > 0)   // no empty caption label, so rows holding a hidden info box collapse fully
                t.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, row);
            input.Anchor = AnchorStyles.Left;
            t.Controls.Add(input, 1, row);
            if (extra != null) { extra.Anchor = AnchorStyles.Left; t.Controls.Add(extra, 2, row); }
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
            double grams, filPrice, elPrice;
            var s1 = Num.Read(txtGrams, out grams);
            var s2 = Num.Read(txtFilamentPrice, out filPrice);
            var s3 = Num.Read(txtElecPrice, out elPrice);
            int hours, minutes;
            var sh = ReadInt(txtHours, 999, out hours);     // empty counts as 0
            var sm = ReadInt(txtMinutes, 59, out minutes);

            string problem = null;
            if (s1 == FieldState.Invalid || s2 == FieldState.Invalid || s3 == FieldState.Invalid)
                problem = "Please enter valid, non-negative numbers.";
            else if (sh == FieldState.Invalid || sm == FieldState.Invalid)
                problem = "Print time: hours 0–999, minutes 0–59.";
            else if (s1 == FieldState.Empty || s2 == FieldState.Empty || s3 == FieldState.Empty)
                problem = "";   // incomplete, no error

            if (problem != null)
            {
                last = null;
                SetStatus(problem, false);
                lblMaterial.Text = lblPower.Text = lblBase.Text = "–";
                lblFriends.Text = lblOthers.Text = "–";
                btnCopy.Enabled = false;
                return;
            }

            var input = new QuoteInput
            {
                Hours = hours, Minutes = minutes, FilamentGrams = grams,
                FilamentDkkPerKg = filPrice, ElectricityDkkPerKwh = elPrice
            };
            last = Pricing.Calculate(input, settings);
            lastHours = hours; lastMinutes = minutes;

            SetStatus("", false);
            lblMaterial.Text = Num.Kr(last.MaterialCost);
            lblPower.Text = Num.Kr(last.PowerCost) + "  (" + last.PowerKwh.ToString("N2", Num.Dk) + " kWh)";
            lblBase.Text = Num.Kr(last.BaseCost);
            lblFriends.Text = Num.Kr(last.FriendsPrice);
            lblOthers.Text = Num.Kr(last.OthersPrice);
            btnCopy.Enabled = true;
        }

        void RefreshSettingsLabels()
        {
            lblPrinter.Text = "Bambu Lab A1 · " + Num.Fmt(settings.PrinterWatts) + " W  (change in Settings)";
            lblFriendsInfo.Text = "+" + Num.Fmt(settings.FriendsMarkupPct) + " % markup";
            string o = "+" + Num.Fmt(settings.OthersMarkupPct) + " % markup";
            if (settings.MachineRatePerHour > 0) o += ", " + Num.Fmt(settings.MachineRatePerHour) + " kr./h";
            if (settings.MinimumPrice > 0) o += ", min " + Num.Fmt(settings.MinimumPrice) + " kr.";
            lblOthersInfo.Text = o;
        }

        void OpenSettings()
        {
            using (var dlg = new SettingsForm(settings))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var old = settings;
                    settings = dlg.Result;
                    bool powerChanged = old.PriceArea != settings.PriceArea || old.PowerAddOn != settings.PowerAddOn ||
                                        old.PowerIncludeVat != settings.PowerIncludeVat ||
                                        (old.AutoPowerPrice == 0 && settings.AutoPowerPrice != 0);
                    if (powerChanged && settings.AutoPowerPrice != 0) BeginInvoke((MethodInvoker)FetchPowerPrice);
                    SaveLastPrices();
                    RefreshSettingsLabels();
                    Recalculate();
                    if (cts == null) ShowAutoIdle();
                }
            }
        }

        // ---------- updates from GitHub ----------
        /// Removes leftovers of a previous update, then quietly looks for a new release.
        /// A newer version shows the red update button under the Friends & family box.
        async void StartupUpdateCheck()
        {
            UpdateInfo found = null;
            try
            {
                found = await Task.Run(() =>
                {
                    Updater.CleanUp();
                    return Updater.CheckForUpdate();
                });
            }
            catch { /* offline or GitHub unreachable: try again next start */ }
            if (found == null || IsDisposed) return;
            ShowUpdateButton(found);
        }

        void ShowUpdateButton(UpdateInfo found)
        {
            pendingUpdate = found;
            btnUpdate.Text = "⬆  Update to v" + Updater.Short(found.Version);
            btnUpdate.Visible = true;
        }

        /// What's new + Update now / Later. Later keeps the button.
        void ShowUpdate(UpdateInfo found)
        {
            using (var dlg = new UpdateForm(found, SaveLastPrices))
                if (dlg.ShowDialog(this) == DialogResult.OK) Close();   // the new version is already starting
        }

        void SaveLastPrices()
        {
            double v;
            if (Num.TryParse(txtFilamentPrice.Text, out v)) settings.LastFilamentPrice = v;
            if (Num.TryParse(txtElecPrice.Text, out v)) settings.LastElectricityPrice = v;
            settings.Save();
        }

        void CopyQuote()
        {
            if (last == null) return;
            string nl = Environment.NewLine;
            string text = "3D print quote" + nl;
            if (txtUrl.Text.Trim().Length > 0) text += "Model: " + txtUrl.Text.Trim() + nl;
            text += "Print time: " + lastHours + " h " + lastMinutes + " m, filament: " + txtGrams.Text.Trim() + " g" + nl;
            text += "Friends & family: " + Num.Kr(last.FriendsPrice) + nl;
            text += "Customer: " + Num.Kr(last.OthersPrice);
            try
            {
                Clipboard.SetText(text);
                const string msg = "Quote copied to clipboard.";
                SetStatus(msg, true);
                var t = new System.Windows.Forms.Timer { Interval = 2500 };
                t.Tick += delegate { t.Stop(); t.Dispose(); if (lblStatus.Text == msg) SetStatus("", false); };
                t.Start();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Clipboard"); }
        }

        void ClearJob()
        {
            txtUrl.Text = "";
            txtGrams.Text = "";
            txtHours.Text = "0";
            txtMinutes.Text = "0";
            txtUrl.Focus();
        }
    }
}
