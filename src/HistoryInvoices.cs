using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PrintPrice
{
    // ---------- issue #7 part 2: the Invoices window (opened from History: Invoices… / Ctrl+I) ----------
    public partial class HistoryForm
    {
        List<QuoteRecord> invoicesShowInHistory;   // "Show in History" from the Invoices window: selected after it closes

        /// Invoices…: the invoice list over the same history list; afterwards History refills (statuses may have changed)
        /// and selects the jobs of "Show in History".
        void InvoicesClicked()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            invoicesShowInHistory = null;
            using (var f = NewInvoicesForm()) f.ShowDialog(this);
            AfterInvoices();
        }

        internal InvoicesForm NewInvoicesForm()
        {
            return new InvoicesForm(list, Settings, QuoteRequested, SaveList, recs => invoicesShowInHistory = recs);
        }

        /// After the Invoices window: the list and cards again; "Show in History" = all months, all quotes, no search,
        /// those jobs selected in the list.
        internal void AfterInvoices()
        {
            var show = invoicesShowInHistory;
            invoicesShowInHistory = null;
            if (show != null && show.Count > 0)
            {
                if (txtSearch.TextLength > 0) txtSearch.Clear();
                if (cmbShow.SelectedIndex != 0) cmbShow.SelectedIndex = 0;
                if (cmbMonth.SelectedIndex != 0) cmbMonth.SelectedIndex = 0;   // All months
                if (cmbPrinter != null && cmbPrinter.Visible && cmbPrinter.SelectedIndex != 0) cmbPrinter.SelectedIndex = 0;
                Fill(show, show[0]);
                string no = (show[0].InvoiceNo ?? "").Trim();
                ShowDone(show.Count == 1 ? L.F("Invoice {0}: its print is selected.", "Faktura {0}: dens print er valgt.", no)
                                         : L.F("Invoice {0}: its {1} prints are selected.", "Faktura {0}: dens {1} print er valgt.", no, show.Count),
                         InfoKind.Success, null, null);
            }
            else Fill();
            FocusList();
        }

        /// The Invoices window: one row per invoice number (History.Invoices; a combined invoice = one row) with number,
        /// the name it was made out to, its prints, invoice date, age ("3 days ago", kept current), due date (amber when
        /// passed: a hint, payments aren't tracked), total and print status. Search (number, name, model), Status and
        /// Period filters, count + sum. Double-click / Enter / Open invoice = the invoice window (History's Invoice…);
        /// the Status cell / right-click set the status of all its prints (several to Failed asks first), saved at once.
        internal sealed class InvoicesForm : CaptionForm
        {
            readonly List<QuoteRecord> list;
            readonly AppSettings settings;
            readonly Func<IWin32Window, List<QuoteRecord>, bool, string> open;
            readonly Action save;
            readonly Action<List<QuoteRecord>> showInHistory;
            readonly TextBox txtSearch;
            readonly ComboBox cmbStatus, cmbPeriod;
            readonly Label lblCount;
            readonly HistoryGrid grid;
            readonly Panel gridFrame;
            readonly InfoBox info;
            readonly Button btnOpen, btnShow, btnClose;
            readonly DataGridViewTextBoxColumn colNo, colTo, colPrints, colDate, colAge, colDue, colTotal, colStatus;
            readonly ContextMenuStrip rowMenu, statusMenu;
            readonly ToolTip tip;
            readonly Timer ageTimer;
            List<InvoiceRow> rows = new List<InvoiceRow>(), shown = new List<InvoiceRow>();
            bool doneShown;
            const int RowsShown = 12;

            /// Status filter items -> History.InvoiceMatches values.
            static readonly int[] StatusValues =
            {
                History.InvoiceStatusAll, (int)PrintState.NotPrinted, (int)PrintState.Printing, (int)PrintState.Printed,
                (int)PrintState.Failed, History.InvoiceStatusMixed
            };
            static readonly int[] PeriodValues = { History.PeriodAll, History.PeriodMonth, History.PeriodLast30, History.PeriodYear };

            public InvoicesForm(List<QuoteRecord> quotes, AppSettings settings, Func<IWin32Window, List<QuoteRecord>, bool, string> open,
                                Action save, Action<List<QuoteRecord>> showInHistory)
            {
                list = quotes ?? new List<QuoteRecord>();
                this.settings = settings;
                this.open = open;
                this.save = save;
                this.showInHistory = showInHistory;
                SuspendLayout();
                AutoScaleDimensions = new SizeF(96F, 96F);
                AutoScaleMode = AutoScaleMode.Dpi;
                Font = new Font("Segoe UI", 10f);
                Text = L.S("Invoices", "Fakturaer");
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent;
                if (Program.Hidden || Screenshots.Active) Screenshots.Place(this);
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;

                var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14), Margin = Padding.Empty };
                Controls.Add(root);

                // ---------- search + status + period + count ----------
                var top = new TableLayoutPanel { ColumnCount = 8, RowCount = 1, AutoSize = true, Margin = new Padding(0, 0, 0, 8), Dock = DockStyle.Fill };
                for (int i = 0; i < 6; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                Func<string, Label> cap = t => new Label { Text = t, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
                txtSearch = new TextBox { Width = 220, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 24, 3), Tag = "single" };
                txtSearch.HandleCreated += delegate
                {
                    SendMessage(txtSearch.Handle, 0x1501, (IntPtr)1, L.S("Number, name or model", "Nummer, navn eller model"));   // EM_SETCUEBANNER
                };
                txtSearch.TextChanged += delegate { Fill(); };
                cmbStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 24, 3) };
                cmbStatus.Items.AddRange(new object[]
                {
                    L.S("All invoices", "Alle fakturaer"), QuoteRows.StateName(PrintState.NotPrinted), QuoteRows.StateName(PrintState.Printing),
                    QuoteRows.StateName(PrintState.Printed), QuoteRows.StateName(PrintState.Failed), L.S("Mixed", "Blandet")
                });
                cmbStatus.SelectedIndex = 0;
                cmbStatus.SelectedIndexChanged += delegate { Fill(); };
                cmbPeriod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 3) };
                cmbPeriod.Items.AddRange(new object[]
                {
                    L.S("All time", "Al tid"), L.S("This month", "Denne måned"), L.S("Last 30 days", "Sidste 30 dage"), L.S("This year", "I år")
                });
                cmbPeriod.SelectedIndex = 0;
                cmbPeriod.SelectedIndexChanged += delegate { Fill(); };
                lblCount = new Label { AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty, ForeColor = Theme.Muted };
                top.Controls.Add(cap(L.S("Search", "Søg")), 0, 0);
                top.Controls.Add(txtSearch, 1, 0);
                top.Controls.Add(cap(L.S("Status", "Status")), 2, 0);
                top.Controls.Add(cmbStatus, 3, 0);
                top.Controls.Add(cap(L.S("Period", "Periode")), 4, 0);
                top.Controls.Add(cmbPeriod, 5, 0);
                top.Controls.Add(lblCount, 7, 0);

                // ---------- the list ----------
                grid = new HistoryGrid
                {
                    Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackgroundColor = Theme.Input, GridColor = Theme.Border,
                    AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AllowUserToResizeColumns = false,
                    AllowUserToOrderColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    MultiSelect = false, EditMode = DataGridViewEditMode.EditProgrammatically, CellBorderStyle = DataGridViewCellBorderStyle.None,
                    ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single, EnableHeadersVisualStyles = false,
                    ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                    ScrollBars = ScrollBars.Vertical, StandardTab = true, ShowCellToolTips = false
                };
                grid.DefaultCellStyle.BackColor = Theme.Input;
                grid.DefaultCellStyle.ForeColor = Theme.Text;
                grid.DefaultCellStyle.SelectionBackColor = Theme.InfoAccent;
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
                grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.ButtonBack;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.ButtonBack;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Text;
                grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
                grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
                grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;   // "Invoice date" on one line
                colNo = TextColumn(L.S("Invoice no.", "Faktura nr."), false);
                colTo = TextColumn(L.S("Customer", "Kunde"), false);
                colPrints = TextColumn(L.S("Prints", "Print"), false);
                colPrints.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                colDate = TextColumn(L.S("Invoice date", "Fakturadato"), false);
                colAge = TextColumn(L.S("Created", "Oprettet"), false);
                colAge.DefaultCellStyle.ForeColor = Theme.Muted;
                colDue = TextColumn(L.S("Due", "Forfald"), false);
                colTotal = TextColumn(L.S("Total", "I alt"), true);
                colTotal.DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
                colStatus = TextColumn(L.S("Status", "Status"), false);
                grid.Columns.AddRange(new DataGridViewColumn[] { colNo, colTo, colPrints, colDate, colAge, colDue, colTotal, colStatus });
                grid.CellFormatting += CellFormatting;
                grid.CellPainting += PaintStatus;
                grid.OpenRequested = OpenSelected;
                grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != colStatus.Index) OpenSelected(); };
                grid.CellClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == colStatus.Index) ShowStatusMenu(e.RowIndex); };
                grid.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Space && grid.CurrentRow != null) { e.Handled = true; ShowStatusMenu(grid.CurrentRow.Index); }
                    if ((e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift)) && grid.CurrentRow != null)
                    {
                        e.Handled = true;
                        var rc = grid.GetRowDisplayRectangle(grid.CurrentRow.Index, false);
                        rowMenu.Show(grid, new Point(rc.Left + rc.Width / 3, rc.Bottom));
                    }
                };
                grid.CellMouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                    grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
                    rowMenu.Show(Cursor.Position);
                };
                grid.SelectionChanged += delegate { UpdateButtons(); };
                grid.HandleCreated += delegate { DarkScrollBars(grid); };
                // dark tooltips: the prints of a combined invoice, a cut-off name, the due hint
                tip = new ToolTip { OwnerDraw = true, InitialDelay = 400 };
                tip.Draw += (s, e) =>
                {
                    using (var b = new SolidBrush(Theme.InfoBack)) e.Graphics.FillRectangle(b, e.Bounds);
                    using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                    const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                    TextRenderer.DrawText(e.Graphics, e.ToolTipText ?? "", e.Font, e.Bounds, Theme.Text, flags);
                };
                grid.CellMouseEnter += (s, e) =>
                {
                    if (e.RowIndex < 0 || e.RowIndex >= shown.Count) return;
                    string t = TipFor(e.RowIndex, e.ColumnIndex);
                    if (t == null) return;
                    var r = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                    tip.Show(t, grid, r.Left, r.Bottom + 2, 8000);
                };
                grid.CellMouseLeave += delegate { tip.Hide(grid); };
                gridFrame = new Panel { Padding = new Padding(1), BackColor = Theme.Border, Margin = new Padding(0, 0, 0, 10), Size = new Size(900, 300) };
                gridFrame.Controls.Add(grid);

                info = new InfoBox(900, 9.5f) { Margin = new Padding(0, 0, 0, 10) };

                // ---------- buttons: Show in History | Open invoice, Close ----------
                var bottom = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty, Dock = DockStyle.Fill };
                bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                Func<string, Button> button = t => new Button
                {
                    Text = t, AutoSize = true, MinimumSize = new Size(92, 32), Padding = new Padding(8, 0, 8, 0), Margin = new Padding(0, 0, 6, 0), UseMnemonic = false
                };
                btnShow = button(L.S("Show in History", "Vis i historik"));
                btnShow.Anchor = AnchorStyles.Left;
                btnShow.Margin = Padding.Empty;
                btnShow.Click += delegate { ShowInHistoryClicked(); };
                btnOpen = button(L.S("Open invoice", "Åbn faktura"));
                btnOpen.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
                btnOpen.Click += delegate { OpenSelected(); };
                btnClose = button(L.S("Close", "Luk"));
                btnClose.Margin = Padding.Empty;
                btnClose.DialogResult = DialogResult.Cancel;
                var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
                right.Controls.Add(btnOpen);
                right.Controls.Add(btnClose);
                bottom.Controls.Add(btnShow, 0, 0);
                bottom.Controls.Add(right, 1, 0);
                CancelButton = btnClose;

                root.Controls.Add(top);
                root.Controls.Add(gridFrame);
                root.Controls.Add(info);
                root.Controls.Add(bottom);

                // ---------- menus ----------
                rowMenu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Renderer = new QuoteMenuRenderer(),
                                                 BackColor = Theme.Input, ForeColor = Theme.Text };
                Func<string, Action, ToolStripMenuItem> item = (text, act) =>
                {
                    var mi = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
                    mi.Click += delegate { act(); };
                    return mi;
                };
                var miOpen = item(L.S("Open invoice", "Åbn faktura"), OpenSelected);
                miOpen.Font = new Font(miOpen.Font, FontStyle.Bold);
                rowMenu.Items.Add(miOpen);
                rowMenu.Items.Add(item(L.S("Copy invoice number", "Kopiér fakturanummer"), CopyNumber));
                rowMenu.Items.Add(new ToolStripSeparator());
                rowMenu.Items.Add(new ToolStripMenuItem(L.S("Print status", "Printstatus")) { Enabled = false });
                var states = new ToolStripMenuItem[4];
                for (int k = 0; k < 4; k++)
                {
                    var st = (PrintState)k;
                    states[k] = item(QuoteRows.StateName(st), () => { var row = SelectedRow(); if (row != null) SetStatus(row, st); });
                    rowMenu.Items.Add(states[k]);
                }
                rowMenu.Items.Add(new ToolStripSeparator());
                rowMenu.Items.Add(item(L.S("Show in History", "Vis i historik"), ShowInHistoryClicked));
                rowMenu.Opening += (s, e) =>
                {
                    var row = SelectedRow();
                    if (row == null) { e.Cancel = true; return; }
                    for (int k = 0; k < 4; k++) states[k].Checked = row.State.HasValue && (int)row.State.Value == k;
                };
                statusMenu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Renderer = new QuoteMenuRenderer(),
                                                    BackColor = Theme.Input, ForeColor = Theme.Text };

                // the age text stays current: redrawn every 10 minutes (crosses midnight within 10 minutes)
                ageTimer = new Timer { Interval = 600000 };
                ageTimer.Tick += delegate { RefreshAges(); };
                FormClosed += delegate { ageTimer.Stop(); ageTimer.Dispose(); tip.Dispose(); rowMenu.Dispose(); statusMenu.Dispose(); };

                Theme.Apply(this);
                ResumeLayout(true);
                Load += delegate { FitLayout(); Build(null); ShowHelp(); ageTimer.Start(); };
                Shown += delegate { if (!Program.Hidden && !Screenshots.Active) grid.Focus(); };
            }

            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                Theme.TitleBar(this);
            }

            int Px(int v) { return (int)Math.Round(v * DeviceDpi / 96f); }

            /// Column widths measured in both languages, 12 rows, the info box as wide as the list.
            void FitLayout()
            {
                var head = grid.ColumnHeadersDefaultCellStyle.Font;
                var bold = colTotal.DefaultCellStyle.Font;
                Func<Font, string[], int> widest = (font, texts) => texts.Max(t => TextRenderer.MeasureText(t, font).Width);
                int pad = Px(20);   // cell padding 6 + 6, plus room so bold headers never wrap
                colNo.Width = Math.Max(widest(head, new[] { "Invoice no.", "Faktura nr." }), widest(grid.Font, new[] { "2026-00000", "2026-WWWW" })) + pad;
                colTo.Width = Px(160);
                colDate.Width = Math.Max(widest(head, new[] { "Invoice date", "Fakturadato" }), widest(grid.Font, new[] { "00-00-0000" })) + pad;
                colAge.Width = Math.Max(widest(head, new[] { "Created", "Oprettet" }),
                                        widest(grid.Font, new[] { "30 days ago", "11 months ago", "for 30 dage siden", "for 11 måneder siden" })) + pad;
                colDue.Width = Math.Max(widest(head, new[] { "Due", "Forfald" }), widest(grid.Font, new[] { "00-00-0000" })) + pad;
                colTotal.Width = Math.Max(widest(head, new[] { "Total", "I alt" }), widest(bold, new[] { "00.000,00 kr." })) + pad;
                colStatus.Width = Math.Max(widest(head, new[] { "Status" }),
                                           widest(grid.Font, new[] { "Not printed", "Printing", "Printed", "Failed", "Ikke printet", "Printer nu", "Printet", "Fejlet", "Mixed", "Blandet" })) + Px(14 + 6 + 14);
                foreach (var cb in new[] { cmbStatus, cmbPeriod })
                {
                    int frame = cb.Height - cb.ItemHeight;
                    if (txtSearch.Height - frame > 0) cb.ItemHeight = txtSearch.Height - frame;
                }
                int textRowH = TextRenderer.MeasureText("Ag", grid.Font).Height + Px(8);
                int rowH = Math.Max(textRowH, Px(30));
                grid.RowTemplate.Height = rowH;
                grid.ColumnHeadersHeight = textRowH + Px(4);
                int cols = colNo.Width + colTo.Width + Px(230) + colDate.Width + colAge.Width + colDue.Width + colTotal.Width + colStatus.Width;
                gridFrame.Size = new Size(cols + SystemInformation.VerticalScrollBarWidth + 2, grid.ColumnHeadersHeight + RowsShown * rowH + 2);
                info.SetWidth(gridFrame.Width);
            }

            /// Rows from the list again (statuses, names and totals may have changed), keeping the selected invoice.
            void Build(string keepNo)
            {
                if (keepNo == null) { var sel = SelectedRow(); if (sel != null) keepNo = sel.No; }
                rows = History.Invoices(list, DateTime.Now, settings);
                Fill(keepNo);
            }

            void Fill() { var sel = SelectedRow(); Fill(sel != null ? sel.No : null); }

            void Fill(string keepNo)
            {
                DateTime now = DateTime.Now, from, to;
                History.InvoicePeriod(PeriodValues[Math.Max(0, cmbPeriod.SelectedIndex)], now, out from, out to);
                int status = StatusValues[Math.Max(0, cmbStatus.SelectedIndex)];
                shown = rows.Where(r => History.InvoiceMatches(r, txtSearch.Text, status, from, to)).ToList();
                grid.SuspendLayout();
                grid.Rows.Clear();
                int keepAt = -1;
                foreach (var r in shown)
                {
                    int i = grid.Rows.Add(r.No, r.BilledTo.Length > 0 ? r.BilledTo : "–", PrintsText(r), DateText(r.BilledAt),
                                          History.AgeText(r.BilledAt, now), r.Due == DateTime.MinValue ? "" : r.Due.ToString("dd-MM-yyyy", Num.Dk),
                                          KrNb(r.Total), "");
                    grid.Rows[i].Tag = r;
                    if (keepNo != null && string.Equals(r.No, keepNo, StringComparison.OrdinalIgnoreCase)) keepAt = i;
                }
                grid.ResumeLayout();
                grid.ClearSelection();
                if (grid.Rows.Count > 0)
                {
                    int at = keepAt >= 0 ? keepAt : 0;
                    grid.CurrentCell = grid.Rows[at].Cells[0];
                    grid.Rows[at].Selected = true;
                }
                double sum = shown.Sum(r => r.Total);
                lblCount.Text = (shown.Count == rows.Count
                                    ? (rows.Count == 1 ? L.S("1 invoice", "1 faktura") : L.F("{0} invoices", "{0} fakturaer", rows.Count))
                                    : L.F("{0} of {1} invoices", "{0} af {1} fakturaer", shown.Count, rows.Count))
                                + " · " + KrNb(sum);
                UpdateButtons();
                if (!doneShown) ShowHelp();
            }

            static string DateText(DateTime t) { return t == DateTime.MinValue ? "?" : t.ToString("dd-MM-yyyy", Num.Dk); }

            /// A single invoice: its model name; a combined one: "3 prints: Moon lamp, Phone stand, …".
            static string PrintsText(InvoiceRow r)
            {
                if (r.Jobs.Count == 1) return ModelText(r.Jobs[0]);
                return L.F("{0} prints: ", "{0} print: ", r.Jobs.Count) + string.Join(", ", r.Jobs.Select(ModelText));
            }

            string TipFor(int rowIndex, int column)
            {
                var r = shown[rowIndex];
                if (column == colPrints.Index && r.Jobs.Count > 1)
                    return string.Join("\n", r.Jobs.Select(j => ModelText(j) + "   " + KrNb(History.PriceFor(j))));
                if (column == colDue.Index && r.Overdue(DateTime.Now))
                    return L.S("The due date has passed.\nPayments aren't tracked in the app.", "Forfaldsdatoen er overskredet.\nBetalinger registreres ikke i appen.");
                if (column == colTo.Index || column == colPrints.Index)
                {
                    string text = Convert.ToString(grid.Rows[rowIndex].Cells[column].FormattedValue);
                    var rect = grid.GetCellDisplayRectangle(column, rowIndex, false);
                    if (!string.IsNullOrEmpty(text) && TextRenderer.MeasureText(text, grid.Font).Width > rect.Width - Px(16)) return text;
                }
                return null;
            }

            void CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex >= shown.Count) return;
                var r = shown[e.RowIndex];
                if (e.ColumnIndex == colTotal.Index) e.CellStyle.ForeColor = r.Friends ? Theme.Friends : Theme.Others;
                else if (e.ColumnIndex == colDue.Index && r.Overdue(DateTime.Now)) e.CellStyle.ForeColor = Theme.Warning;
                else if (e.ColumnIndex == colTo.Index && r.BilledTo.Length == 0) e.CellStyle.ForeColor = Theme.Muted;
            }

            void PaintStatus(object sender, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != colStatus.Index || e.RowIndex >= shown.Count) return;
                bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
                e.PaintBackground(e.CellBounds, selected);
                PaintStatusCell(e.Graphics, e.CellBounds, shown[e.RowIndex].State, selected, CurrentAutoScaleDimensions.Height / 96f, grid.Font);
                e.Handled = true;
            }

            /// The age column again (the date moved on); nothing else changes.
            void RefreshAges()
            {
                DateTime now = DateTime.Now;
                for (int i = 0; i < grid.Rows.Count && i < shown.Count; i++)
                {
                    grid.Rows[i].Cells[colAge.Index].Value = History.AgeText(shown[i].BilledAt, now);
                    grid.InvalidateCell(colDue.Index, i);
                }
            }

            InvoiceRow SelectedRow()
            {
                if (grid.CurrentRow == null || grid.SelectedRows.Count == 0) return null;
                return grid.CurrentRow.Tag as InvoiceRow;
            }

            void UpdateButtons()
            {
                bool one = SelectedRow() != null;
                btnOpen.Enabled = one && open != null;
                btnShow.Enabled = one && showInHistory != null;
            }

            void ShowHelp()
            {
                doneShown = false;
                info.Kind = InfoKind.Info;
                if (rows.Count == 0)
                {
                    info.Text = L.S("No invoices yet. An invoice is made with Invoice… in History or Show invoice… in the main window.",
                                    "Ingen fakturaer endnu. En faktura laves med Faktura… i historikken eller Vis faktura… i hovedvinduet.");
                    return;
                }
                info.Text = L.S("Double-click or Enter opens an invoice. Click Status to change the print status of all its prints (saved at once).\n" +
                                "Due dates in amber have passed (payments aren't tracked in the app).",
                                "Dobbeltklik eller Enter åbner en faktura. Klik på Status for at ændre printstatus for alle dens print (gemmes med det samme).\n" +
                                "Forfaldsdatoer i orange er overskredet (betalinger registreres ikke i appen).");
            }

            void ShowDone(string text, InfoKind kind)
            {
                doneShown = true;
                info.Text = text;
                info.Kind = kind;
            }

            // ---------- actions ----------
            void OpenSelected()
            {
                var row = SelectedRow();
                if (row == null || open == null) return;
                string problem = open(this, new List<QuoteRecord>(row.Jobs), true);   // History's Invoice… (re-opens the made invoice)
                doneShown = false;
                Build(row.No);
                if (!string.IsNullOrEmpty(problem)) ShowDone(problem, InfoKind.Warning);
                grid.Focus();
            }

            void CopyNumber()
            {
                var row = SelectedRow();
                if (row == null) return;
                try
                {
                    if (testClipboard != null) testClipboard(row.No); else Clipboard.SetText(row.No);
                    ShowDone(L.F("✓ Invoice number {0} copied.", "✓ Fakturanummer {0} kopieret.", row.No), InfoKind.Success);
                }
                catch (Exception ex) { ShowDone(L.S("Couldn't copy: ", "Kunne ikke kopiere: ") + ex.Message, InfoKind.Error); }
            }

            void ShowInHistoryClicked()
            {
                var row = SelectedRow();
                if (row == null || showInHistory == null) return;
                showInHistory(new List<QuoteRecord>(row.Jobs));
                DialogResult = DialogResult.OK;
                Close();
            }

            /// The Status cell's menu under the cell: the four statuses (round dot = the invoice's, none when mixed).
            void ShowStatusMenu(int rowIndex)
            {
                if (rowIndex < 0 || rowIndex >= shown.Count) return;
                FillStatusMenu(shown[rowIndex]);
                var rc = grid.GetCellDisplayRectangle(colStatus.Index, rowIndex, false);
                statusMenu.Show(grid, new Point(rc.Left, rc.Bottom));
            }

            void FillStatusMenu(InvoiceRow row)
            {
                statusMenu.Items.Clear();
                if (row.Jobs.Count > 1)
                    statusMenu.Items.Add(new ToolStripMenuItem(L.F("All {0} prints:", "Alle {0} print:", row.Jobs.Count)) { Enabled = false });
                for (int k = 0; k < 4; k++)
                {
                    var st = (PrintState)k;
                    var mi = new ToolStripMenuItem(QuoteRows.StateName(st)) { ForeColor = Theme.Text, Checked = row.State.HasValue && row.State.Value == st };
                    mi.Click += delegate { SetStatus(row, st); };
                    statusMenu.Items.Add(mi);
                }
            }

            /// Every print of the invoice gets state, saved at once; 2+ prints to Failed asks first. The rows are built
            /// again (with a status filter the invoice may leave the list) and the ✓ line says what changed.
            internal void SetStatus(InvoiceRow row, PrintState state)
            {
                if (row == null) return;
                int newlyFailed = row.Jobs.Count(j => !j.Failed);
                if (state == PrintState.Failed && newlyFailed > 1)
                    using (var ask = NewFailedAsk(newlyFailed))
                        if (ask.ShowDialog(this) != DialogResult.Yes) { grid.Focus(); return; }
                DateTime now = DateTime.Now;
                foreach (var j in row.Jobs) History.SetPrintState(j, state, now);
                if (save != null) save();
                Build(row.No);
                bool gone = !shown.Any(r => string.Equals(r.No, row.No, StringComparison.OrdinalIgnoreCase));
                string text = L.F("✓ Invoice {0}: {1}.", "✓ Faktura {0}: {1}.", row.No, QuoteRows.StateName(state));
                if (gone) text += " " + L.S("It no longer shows with this filter.", "Den vises ikke længere med dette filter.");
                ShowDone(text, InfoKind.Success);
                grid.Focus();
            }

            // ---------- tests (--test-bill section 11) and picture mode ----------
            internal static Action<string> testClipboard;
            internal int TestRowCount { get { return shown.Count; } }
            internal List<InvoiceRow> TestRows { get { return shown; } }
            internal string TestCount { get { return lblCount.Text; } }
            internal string TestInfoText { get { return (info.Text ?? "").Replace("\n", " / "); } }
            internal InfoKind TestInfoKind { get { return info.Kind; } }
            internal string TestCell(int row, int col) { return row >= 0 && row < grid.Rows.Count ? Convert.ToString(grid.Rows[row].Cells[col].FormattedValue) : ""; }
            internal void TestSearch(string text) { txtSearch.Text = text; }
            internal void TestStatusFilter(int index) { cmbStatus.SelectedIndex = index; }
            internal void TestPeriod(int index) { cmbPeriod.SelectedIndex = index; }
            internal void TestSelect(string no)
            {
                for (int i = 0; i < shown.Count; i++)
                    if (string.Equals(shown[i].No, no, StringComparison.OrdinalIgnoreCase))
                    {
                        grid.ClearSelection();
                        grid.CurrentCell = grid.Rows[i].Cells[0];
                        grid.Rows[i].Selected = true;
                    }
            }
            internal InvoiceRow TestSelected { get { return SelectedRow(); } }
            internal string TestStateLine()
            {
                var sel = SelectedRow();
                return "invoices: rows " + shown.Count + " of " + rows.Count + " (" + string.Join(" ", shown.Take(6).Select(r => r.No)) +
                       (shown.Count > 6 ? " …" : "") + "), filter \"" + cmbStatus.Text + "\", period \"" + cmbPeriod.Text + "\", search \"" +
                       txtSearch.Text + "\", selected " + (sel != null ? sel.No : "none") + "\n  invoices info: " + TestInfoText;
            }
            internal void TestOpen() { OpenSelected(); }
            internal void TestCopy() { CopyNumber(); }
            internal void TestShowInHistory() { ShowInHistoryClicked(); }
            internal int TestStatusMenu(int rowIndex, out int dot)
            {
                dot = -1;
                if (rowIndex < 0 || rowIndex >= shown.Count) return 0;
                FillStatusMenu(shown[rowIndex]);
                int n = 0;
                foreach (ToolStripItem it in statusMenu.Items)
                {
                    var mi = it as ToolStripMenuItem;
                    if (mi == null || !mi.Enabled) continue;
                    if (mi.Checked) dot = n;
                    n++;
                }
                return n;
            }
            internal static int ColAge { get { return 4; } }

            /// Picture mode: the Status cell's menu of a row, drawn where it opens.
            internal void SampleStatusMenu(int rowIndex, Graphics g, Point origin)
            {
                if (rowIndex < 0 || rowIndex >= shown.Count) return;
                FillStatusMenu(shown[rowIndex]);
                statusMenu.PerformLayout();
                var size = statusMenu.GetPreferredSize(Size.Empty);
                statusMenu.Size = size;
                using (var bmp = new Bitmap(size.Width, size.Height))
                {
                    statusMenu.DrawToBitmap(bmp, new Rectangle(Point.Empty, size));
                    var rc = grid.GetCellDisplayRectangle(colStatus.Index, rowIndex, false);
                    var p = grid.PointToScreen(new Point(rc.Left, rc.Bottom));
                    // the Status column is the last one: in the picture the menu is kept inside the window (a real menu
                    // may reach past the window's edge; Windows only keeps it on the screen)
                    int x = Math.Max(0, Math.Min(p.X - origin.X, Width - size.Width - Px(8)));
                    g.DrawImage(bmp, x, p.Y - origin.Y);
                }
            }
        }

        /// Picture mode / --test-bill section 11: SampleQuotes with more invoices: the two billed ones, the 4-colour Benchy
        /// (printing now), Benchy + cable clips on one combined friends invoice (clips not printed yet: "Mixed"), the Nessie
        /// and the moon lamp (14 days to pay, so past its due date). Never saved.
        internal static List<QuoteRecord> SampleInvoiceQuotes()
        {
            var l = SampleQuotes();
            Action<QuoteRecord, string, string, string, int> bill = (r, no, at, to, days) =>
            {
                r.InvoiceNo = no; r.BilledAt = at; r.BilledTo = to;
                DateTime t;
                if (History.TryTime(at, out t)) r.InvoiceDue = t.AddDays(days).ToString("yyyy-MM-dd");
            };
            bill(l[2], "2026-48213", "2026-10-31 09:12:00", "Lene Hansen", 30);
            bill(l[4], "2026-73096", "2026-10-25 10:30:00", "Café Bønne", 30);
            bill(l[1], "2026-20481", "2026-11-03 08:15:00", l[1].Customer, 30);
            bill(l[0], "2026-51907", "2026-11-04 17:45:00", "Mads", 30);   // Benchy + cable clips: one combined friends invoice
            bill(l[3], "2026-51907", "2026-11-04 17:45:00", "Mads", 30);
            l[3].Customer = "Mads";
            l[3].Printed = false;   // the cable clips not printed yet: the combined invoice shows "Mixed"
            bill(l[6], "2026-66120", "2026-10-13 12:00:00", "Emma", 30);
            bill(l[7], "2026-39774", "2026-10-06 10:00:00", "Jonas Berg", 14);   // due passed: the amber hint
            l[7].Customer = "Jonas Berg";
            return l;
        }
    }
}
