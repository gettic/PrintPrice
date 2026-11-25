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
            readonly ComboBox cmbStatus, cmbPeriod, cmbPayment;
            readonly Label lblCount, lblUnpaid, lblOverdue;   // paid-tick: "Unpaid: 3 invoices · 125,64 kr." + amber " · 1 overdue"
            readonly HistoryGrid grid;
            readonly Panel gridFrame;
            readonly InfoBox info;
            readonly Button btnOpen, btnShow, btnClose;
            readonly DataGridViewTextBoxColumn colNo, colTo, colPrints, colDate, colAge, colDue, colTotal, colPaid, colStatus;
            readonly ContextMenuStrip rowMenu, statusMenu;
            readonly ToolTip tip;
            readonly Timer ageTimer;
            List<InvoiceRow> rows = new List<InvoiceRow>(), shown = new List<InvoiceRow>();
            bool doneShown;
            bool narrow;   // fit-to-screen made the top row smaller
            string narrowCue;   // the search hint then (null = the normal one)
            internal string TestCue { get { return narrowCue ?? L.S("Number, name or model", "Nummer, navn eller model"); } }
            const int RowsShown = 12;
            int rowsShown = RowsShown;   // qol 6: fewer (min 5) when the window would be taller than the screen
            /// Test / picture mode: the screen's working height (px) for the fit check (0 = the real screen).
            internal static int TestScreenHeight;

            /// Status filter items -> History.InvoiceMatches values.
            static readonly int[] StatusValues =
            {
                History.InvoiceStatusAll, (int)PrintState.NotPrinted, (int)PrintState.Printing, (int)PrintState.Printed,
                (int)PrintState.Failed, History.InvoiceStatusMixed
            };
            static readonly int[] PeriodValues = { History.PeriodAll, History.PeriodMonth, History.PeriodLast30, History.PeriodYear };
            static readonly int[] PaymentValues = { History.PaymentAll, History.PaymentUnpaid, History.PaymentOverdue, History.PaymentPaid };
            /// Test / picture mode: the screen's working width (px) for the fit-to-screen check (0 = the real screen).
            internal static int TestScreenWidth;

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
                var top = new TableLayoutPanel { ColumnCount = 10, RowCount = 1, AutoSize = true, Margin = new Padding(0, 0, 0, 8), Dock = DockStyle.Fill };
                for (int i = 0; i < 8; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                Func<string, Label> cap = t => new Label { Text = t, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
                txtSearch = new TextBox { Width = 220, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 3, 24, 3), Tag = "single" };
                txtSearch.HandleCreated += delegate
                {
                    SendMessage(txtSearch.Handle, 0x1501, (IntPtr)1, TestCue);   // EM_SETCUEBANNER (shorter when narrow)
                };
                // optimizer 3b: the list follows 200 ms after the last key (Enter, Esc and leaving the box: at once)
                searchTimer = new Timer { Interval = 200 };
                searchTimer.Tick += delegate { searchTimer.Stop(); Fill(); };
                txtSearch.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };
                // leaving the box runs a waiting search at once; when that leave came from a click in the list, the click is
                // swallowed too (Windows moves the focus before the list sees the mouse-down)
                txtSearch.Leave += delegate { if (ApplyPendingSearch()) leaveFlushTick = Environment.TickCount; };
                txtSearch.KeyPress += (ks, ke) => { if (ke.KeyChar == '\r') { ke.Handled = true; FlushSearch(); } };
                cmbStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 3, 24, 3) };
                cmbStatus.Items.AddRange(new object[]
                {
                    L.S("All invoices", "Alle fakturaer"), QuoteRows.StateName(PrintState.NotPrinted), QuoteRows.StateName(PrintState.Printing),
                    QuoteRows.StateName(PrintState.Printed), QuoteRows.StateName(PrintState.Failed), L.S("Mixed", "Blandet")
                });
                cmbStatus.SelectedIndex = 0;
                cmbStatus.SelectedIndexChanged += delegate { Fill(); };
                cmbPeriod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 3, 24, 3) };
                cmbPeriod.Items.AddRange(new object[]
                {
                    L.S("All time", "Hele perioden"), L.S("This month", "Denne måned"), L.S("Last 30 days", "Sidste 30 dage"), L.S("This year", "I år")
                });
                cmbPeriod.SelectedIndex = 0;
                cmbPeriod.SelectedIndexChanged += delegate { Fill(); };
                // paid-tick: All / Unpaid (overdue too) / Overdue / Paid
                cmbPayment = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 3, 0, 3) };
                cmbPayment.Items.AddRange(new object[] { L.S("All", "Alle"), L.S("Unpaid", "Ubetalte"), L.S("Overdue", "Forfaldne"), L.S("Paid", "Betalte") });
                cmbPayment.SelectedIndex = 0;
                cmbPayment.SelectedIndexChanged += delegate { Fill(); };
                lblCount = new Label { AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty, ForeColor = Theme.Muted };
                top.Controls.Add(cap(L.S("Search", "Søg")), 0, 0);
                top.Controls.Add(txtSearch, 1, 0);
                top.Controls.Add(cap(L.S("Status", "Status")), 2, 0);
                top.Controls.Add(cmbStatus, 3, 0);
                top.Controls.Add(cap(L.S("Period", "Periode")), 4, 0);
                top.Controls.Add(cmbPeriod, 5, 0);
                top.Controls.Add(cap(L.S("Payment", "Betaling")), 6, 0);
                top.Controls.Add(cmbPayment, 7, 0);
                top.Controls.Add(lblCount, 9, 0);

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
                colNo = TextColumn(L.S("Invoice no.", "Fakturanr."), false);
                colTo = TextColumn(L.S("Customer", "Kunde"), false);
                colPrints = TextColumn(L.S("Prints", "Print"), false);
                colPrints.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                colDate = TextColumn(L.S("Invoice date", "Fakturadato"), false);
                colAge = TextColumn(L.S("Created", "Oprettet"), false);
                colAge.DefaultCellStyle.ForeColor = Theme.Muted;
                colDue = TextColumn(L.S("Due", "Forfald"), false);
                colTotal = TextColumn(L.S("Total", "I alt"), true);
                colTotal.DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
                colPaid = TextColumn(L.S("Paid", "Betalt"), false);   // paid-tick: ✓ Paid / Unpaid / Overdue, click toggles
                colStatus = TextColumn(L.S("Status", "Status"), false);
                grid.Columns.AddRange(new DataGridViewColumn[] { colNo, colTo, colPrints, colDate, colAge, colDue, colTotal, colPaid, colStatus });
                grid.SearchPending = SearchWasPending;   // release-test-6: a click / Space during the 200 ms search delay is swallowed
                grid.CellFormatting += CellFormatting;
                grid.CellPainting += PaintStatus;
                grid.CellPainting += PaintPaid;
                grid.OpenRequested = OpenSelected;
                grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != colStatus.Index && e.ColumnIndex != colPaid.Index) OpenSelected(); };
                grid.CellClick += (s, e) =>
                {
                    if (e.RowIndex < 0) return;
                    if (e.ColumnIndex == colStatus.Index) ShowStatusMenu(e.RowIndex);
                    else if (e.ColumnIndex == colPaid.Index && e.RowIndex < shown.Count) TogglePaid(shown[e.RowIndex]);
                };
                grid.KeyDown += (s, e) =>
                {
                    // Space: paid / unpaid (the Status menu: click the Status cell or the row menu)
                    if (e.KeyCode == Keys.Space && grid.CurrentRow != null) { e.Handled = true; TogglePaid(SelectedRow()); }
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
                // qol 9: nothing matches the search / filters -> a grey line in the empty list
                grid.Paint += (s, e) =>
                {
                    if (shown == null || shown.Count > 0 || rows == null || rows.Count == 0) return;
                    var area = grid.ClientRectangle;
                    area.Y += grid.ColumnHeadersHeight + Px(18);
                    area.Height = grid.RowTemplate.Height;
                    TextRenderer.DrawText(e.Graphics, EmptyText, grid.Font, area, Theme.Muted,
                                          TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                };
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
                var bottom = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = Padding.Empty, Dock = DockStyle.Fill };
                bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                // paid-tick: "Unpaid: 3 invoices · 125,64 kr." (+ amber " · 1 overdue") next to Show in History
                lblUnpaid = new Label { AutoSize = true, UseMnemonic = false, Margin = Padding.Empty, ForeColor = Theme.Text };
                lblOverdue = new Label { AutoSize = true, UseMnemonic = false, Margin = Padding.Empty, ForeColor = Theme.Warning };
                // a plain panel: "· 1 overdue" is placed by hand 6 px into the first label's right-hand slack (qol 7)
                var unpaidFlow = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(16, 0, 0, 0), Anchor = AnchorStyles.Left };
                unpaidFlow.Controls.Add(lblUnpaid);
                unpaidFlow.Controls.Add(lblOverdue);
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
                bottom.Controls.Add(unpaidFlow, 1, 0);
                bottom.Controls.Add(right, 2, 0);
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
                var miPaid = item(L.S("Mark as paid", "Markér som betalt"), () => TogglePaid(SelectedRow()));
                rowMenu.Items.Add(miPaid);
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
                    miPaid.Text = row.Paid ? L.S("Mark as unpaid", "Markér som ubetalt") : L.S("Mark as paid", "Markér som betalt");
                };
                statusMenu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Renderer = new QuoteMenuRenderer(),
                                                    BackColor = Theme.Input, ForeColor = Theme.Text };

                // the age text stays current: redrawn every 10 minutes (crosses midnight within 10 minutes)
                ageTimer = new Timer { Interval = 600000 };
                ageTimer.Tick += delegate { RefreshAges(); };
                FormClosed += delegate { ageTimer.Stop(); ageTimer.Dispose(); searchTimer.Stop(); searchTimer.Dispose(); tip.Dispose(); rowMenu.Dispose(); statusMenu.Dispose(); };

                Theme.Apply(this);
                lblOverdue.ForeColor = Theme.Warning;   // after Apply (it sets label colours)
                gridFrame.BackColor = Theme.Border;     // qol 1: Apply resets it (the 1 px list frame)
                ResumeLayout(true);
                Load += delegate { FitLayout(); Build(null); ShowHelp(); FitHeight(); ageTimer.Start(); };
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
                colNo.Width = Math.Max(widest(head, new[] { "Invoice no.", "Fakturanr." }), widest(grid.Font, new[] { "2026-00000", "2026-WWWW" })) + pad;
                colTo.Width = Px(160);
                colDate.Width = Math.Max(widest(head, new[] { "Invoice date", "Fakturadato" }), widest(grid.Font, new[] { "00-00-0000" })) + pad;
                // qol 3: this language only; it may shrink to the ages really shown (before Prints gives way)
                colAge.Width = Math.Max(widest(head, new[] { L.S("Created", "Oprettet") }),
                                        widest(grid.Font, new[] { L.S("30 days ago", "for 30 dage siden"), L.S("11 months ago", "for 11 måneder siden") })) + pad;
                var nowRows = History.Invoices(list, DateTime.Now, settings);
                int ageMin = Math.Max(widest(head, new[] { L.S("Created", "Oprettet") }),
                                      nowRows.Count == 0 ? 0 : nowRows.Max(r => TextRenderer.MeasureText(History.AgeText(r.BilledAt, DateTime.Now), grid.Font).Width)) + pad;
                colDue.Width = Math.Max(widest(head, new[] { "Due", "Forfald" }), widest(grid.Font, new[] { "00-00-0000" })) + pad;
                colTotal.Width = Math.Max(widest(head, new[] { "Total", "I alt" }), widest(bold, new[] { "00.000,00 kr." })) + pad;
                colStatus.Width = Math.Max(widest(head, new[] { "Status" }),
                                           widest(grid.Font, new[] { "Not printed", "Printing", "Printed", "Failed", "Ikke printet", "Printer nu", "Printet", "Fejlet", "Mixed", "Blandet" })) + Px(14 + 6 + 14 + 12);   // batch-2 qol #9: +12 for "1 of 5 plates"
                colPaid.Width = Math.Max(widest(head, new[] { "Paid", "Betalt" }),
                                         widest(grid.Font, new[] { "Paid", "Unpaid", "Overdue", "Betalt", "Ubetalt", "Forfalden" })) + Px(14 + 6 + 14);
                foreach (var cb in new[] { cmbStatus, cmbPeriod, cmbPayment })
                {
                    int frame = cb.Height - cb.ItemHeight;
                    if (txtSearch.Height - frame > 0) cb.ItemHeight = txtSearch.Height - frame;
                }
                int textRowH = TextRenderer.MeasureText("Ag", grid.Font).Height + Px(8);
                int rowH = Math.Max(textRowH, Px(30));
                grid.RowTemplate.Height = rowH;
                grid.ColumnHeadersHeight = textRowH + Px(4);
                // fit to the screen (paid-tick: e.g. 125 % on a 1366 px screen): the Prints column, then Customer, the search
                // box and the filters give way; the window never gets wider than the working area
                int printsW = Px(230);
                int screenW = TestScreenWidth > 0 ? TestScreenWidth : Screen.FromControl(this).WorkingArea.Width;
                int chrome = Width - ClientSize.Width + 2 * Px(14) + SystemInformation.VerticalScrollBarWidth + 2;
                Func<int> colsW = () => colNo.Width + colTo.Width + printsW + colDate.Width + colAge.Width + colDue.Width + colTotal.Width + colPaid.Width + colStatus.Width;
                int over = colsW() + chrome - screenW;
                if (over > 0) { int cut = Math.Max(0, Math.Min(over, colAge.Width - ageMin)); colAge.Width -= cut; over -= cut; }
                if (over > 0) { int cut = Math.Min(over, printsW - Px(110)); printsW -= cut; over -= cut; }
                if (over > 0) { int cut = Math.Min(over, colTo.Width - Px(110)); colTo.Width -= cut; over -= cut; }
                narrow = colsW() + chrome + Px(40) > screenW;
                if (narrow)
                {
                    // the top row: smaller boxes and gaps so it isn't wider than the list
                    txtSearch.Width = Px(150);
                    narrowCue = L.S("No., name, model", "Nr., navn, model");   // the shorter hint fits 150 px
                    if (txtSearch.IsHandleCreated) SendMessage(txtSearch.Handle, 0x1501, (IntPtr)1, narrowCue);
                    txtSearch.Margin = new Padding(0, txtSearch.Margin.Top, Px(12), txtSearch.Margin.Bottom);
                    cmbStatus.Width = Px(140); cmbStatus.Margin = new Padding(0, cmbStatus.Margin.Top, Px(12), cmbStatus.Margin.Bottom);
                    cmbPeriod.Width = Px(125); cmbPeriod.Margin = new Padding(0, cmbPeriod.Margin.Top, Px(12), cmbPeriod.Margin.Bottom);
                    cmbPayment.Width = Px(110);
                }
                int cols = colsW();
                rowsShown = RowsShown;
                gridFrame.Size = new Size(cols + SystemInformation.VerticalScrollBarWidth + 2, grid.ColumnHeadersHeight + rowsShown * rowH + 2);
                info.SetWidth(gridFrame.Width);
                // the real width after layout (margins the estimate missed): still too wide -> the Prints column gives the rest
                PerformLayout();
                int extra = Width - screenW;
                if (extra > 0 && printsW - extra >= Px(80))
                {
                    gridFrame.Width -= extra;
                    info.SetWidth(gridFrame.Width);
                    PerformLayout();
                }
            }

            /// qol 6: too tall for the screen (e.g. 1366 x 768 at 125 %) -> fewer rows, at least 5. After the help text is in
            /// (its lines set the info box height).
            void FitHeight()
            {
                PerformLayout();
                int rowH = grid.RowTemplate.Height;
                int screenH = TestScreenHeight > 0 ? TestScreenHeight : Screen.FromControl(this).WorkingArea.Height;
                int tooTall = Height - screenH;
                if (tooTall > 0)
                {
                    int less = Math.Min(RowsShown - 5, (tooTall + rowH - 1) / rowH);
                    rowsShown = RowsShown - less;
                    gridFrame.Height = grid.ColumnHeadersHeight + rowsShown * rowH + 2;
                    PerformLayout();
                }
            }

            static string EmptyText { get { return L.S("No invoices match the search or filters.", "Ingen fakturaer passer til søgningen eller filtrene."); } }

            readonly Timer searchTimer;

            /// A search still waiting for its 200 ms runs now (Enter, Esc, leaving the box, the test steps).
            void FlushSearch() { ApplyPendingSearch(); }

            int leaveFlushTick;   // when leaving the search box ran a waiting search (0 = not just now)

            /// The grid asks on a mouse-down / Space: a waiting search runs now, or one just ran because the search box lost
            /// the focus to this click (within 300 ms): true = swallow the click.
            bool SearchWasPending()
            {
                if (ApplyPendingSearch()) return true;
                bool justLeft = leaveFlushTick != 0 && unchecked(Environment.TickCount - leaveFlushTick) < 300;
                leaveFlushTick = 0;
                return justLeft;
            }

            /// Runs a waiting search now; true = there was one (the grid then swallows the click / Space that asked).
            bool ApplyPendingSearch()
            {
                if (!searchTimer.Enabled) return false;
                searchTimer.Stop();
                Fill();
                return true;
            }

            /// qol 4: Esc in a search box with text clears it first (the next Esc closes).
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData == Keys.Escape && (txtSearch.Focused || ActiveControl == txtSearch) && txtSearch.TextLength > 0) { txtSearch.Clear(); FlushSearch(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
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
                searchTimer.Stop();   // this fill uses the search box as it is now
                DateTime now = DateTime.Now, from, to;
                History.InvoicePeriod(PeriodValues[Math.Max(0, cmbPeriod.SelectedIndex)], now, out from, out to);
                int status = StatusValues[Math.Max(0, cmbStatus.SelectedIndex)];
                int payment = PaymentValues[Math.Max(0, cmbPayment.SelectedIndex)];
                shown = rows.Where(r => History.InvoiceMatches(r, txtSearch.Text, status, from, to, payment, now)).ToList();
                grid.SuspendLayout();
                grid.Rows.Clear();
                int keepAt = -1;
                foreach (var r in shown)
                {
                    int i = grid.Rows.Add(r.No, r.BilledTo.Length > 0 ? r.BilledTo : "–", PrintsText(r), DateText(r.BilledAt),
                                          History.AgeText(r.BilledAt, now), r.Due == DateTime.MinValue ? "" : r.Due.ToString("dd-MM-yyyy", Num.Dk),
                                          KrNb(r.Total), PaidWord(r, now), "");
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
                // paid-tick: the unpaid line over all invoices (not only the filtered ones)
                var u = History.UnpaidSummary(rows, now);
                if (rows.Count == 0) { lblUnpaid.Text = ""; lblOverdue.Text = ""; }
                else if (u.Count == 0) { lblUnpaid.Text = L.S("Everything is paid.", "Alt er betalt."); lblUnpaid.ForeColor = Theme.Muted; lblOverdue.Text = ""; }
                else
                {
                    lblUnpaid.ForeColor = Theme.Text;
                    lblUnpaid.Text = L.S("Unpaid: ", "Ubetalt: ") + (u.Count == 1 ? L.S("1 invoice", "1 faktura") : L.F("{0} invoices", "{0} fakturaer", u.Count)) +
                                     " · " + KrNb(u.Total);
                    lblOverdue.Text = u.Overdue > 0 ? "· " + L.F("{0} overdue", "{0} forfaldne", u.Overdue).Replace("1 forfaldne", "1 forfalden") : "";
                }
                // qol 7: the gap before "· 1 overdue" as a space: placed 6 px into the first label's right-hand slack
                lblUnpaid.Location = Point.Empty;
                lblOverdue.Location = new Point(Math.Max(0, lblUnpaid.Right - Px(6)), 0);
                lblOverdue.BringToFront();   // over the first label's empty end
                grid.Invalidate();   // the "no invoices match" line
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
                    return L.S("Overdue: past its due date.", "Forfalden: forfaldsdatoen er overskredet.");
                if (column == colPaid.Index)
                {
                    if (r.Paid)
                        return (r.PaidAt != DateTime.MinValue ? L.F("Paid on {0}", "Betalt {0}", r.PaidAt.ToString("dd-MM-yyyy", Num.Dk)) + "\n" : "") +
                               L.S("Click to mark it as unpaid", "Klik for at markere den som ubetalt");
                    return (r.Overdue(DateTime.Now) ? L.S("Overdue: past its due date.", "Forfalden: forfaldsdatoen er overskredet.") + "\n" : "") +
                           L.S("Click to mark it as paid", "Klik for at markere den som betalt");
                }
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
                else if (e.ColumnIndex == colDue.Index && r.Overdue(DateTime.Now)) e.CellStyle.ForeColor = Theme.Warning;   // only while unpaid
                else if (e.ColumnIndex == colTo.Index && r.BilledTo.Length == 0) e.CellStyle.ForeColor = Theme.Muted;
            }

            void PaintStatus(object sender, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != colStatus.Index || e.RowIndex >= shown.Count) return;
                bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
                e.PaintBackground(e.CellBounds, selected);
                // batch-2: some of the invoice's plates printed, not all: amber clock + "7 of 12 plates" (also when the jobs differ)
                var ir = shown[e.RowIndex];
                bool partly = ir.Plates >= 2 && ir.PlatesDone > 0 && ir.PlatesDone < ir.Plates && ir.State != PrintState.Failed;
                PaintStatusCell(e.Graphics, e.CellBounds, partly ? PrintState.Printing : ir.State, selected, CurrentAutoScaleDimensions.Height / 96f, grid.Font,
                                partly ? History.PlatesProgressText(ir.PlatesDone, ir.Plates) : null);
                e.Handled = true;
            }

            /// paid-tick: the Paid cell's word (painted by PaintPaid; also the cell value for tests / search).
            static string PaidWord(InvoiceRow r, DateTime now)
            {
                if (r.Paid) return L.S("Paid", "Betalt");
                return r.Overdue(now) ? L.S("Overdue", "Forfalden") : L.S("Unpaid", "Ubetalt");
            }

            /// The Paid cell: green ✓ + "Paid", grey hollow square + "Unpaid", amber hollow square + "Overdue" (white text when
            /// the row is selected; the marks keep their colour like the Status column's).
            void PaintPaid(object sender, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != colPaid.Index || e.RowIndex >= shown.Count) return;
                bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
                e.PaintBackground(e.CellBounds, selected);
                var r = shown[e.RowIndex];
                DateTime now = DateTime.Now;
                bool overdue = r.Overdue(now);
                float sc = CurrentAutoScaleDimensions.Height / 96f;
                int size = (int)Math.Round(14 * sc), padL = (int)Math.Round(7 * sc), gap = (int)Math.Round(6 * sc);
                var cell = e.CellBounds;
                var box = new Rectangle(cell.X + padL, cell.Y + (cell.Height - size) / 2, size - 1, size - 1);
                var g = e.Graphics;
                var old = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (r.Paid)
                {
                    int m = Math.Max(2, (int)Math.Round(2f * sc));
                    using (var pen = new Pen(selected ? Color.White : Theme.Success, Math.Max(2f, 2f * sc)))
                        g.DrawLines(pen, new[]
                        {
                            new PointF(box.Left + m, box.Top + box.Height / 2f), new PointF(box.Left + box.Width * 0.42f, box.Bottom - m),
                            new PointF(box.Right - m + 0.5f, box.Top + m)
                        });
                }
                else
                {
                    g.SmoothingMode = old;
                    int inset = Math.Max(1, (int)Math.Round(1.5f * sc));
                    using (var pen = new Pen(overdue ? Theme.Warning : Theme.Muted, Math.Max(1f, 1.2f * sc)))
                        g.DrawRectangle(pen, box.Left + inset, box.Top + inset, box.Width - 2 * inset, box.Height - 2 * inset);
                }
                g.SmoothingMode = old;
                Color fore = selected ? Color.White : r.Paid ? Theme.Success : overdue ? Theme.Warning : Theme.Muted;
                var rt = new Rectangle(box.Right + gap, cell.Y, Math.Max(1, cell.Right - box.Right - gap - (int)Math.Round(4 * sc)), cell.Height);
                TextRenderer.DrawText(g, PaidWord(r, now), grid.Font, rt, fore,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
                    info.Text = L.S("No invoices yet. Make one with Invoice… in History.", "Ingen fakturaer endnu. Lav en med Faktura… i historikken.");
                    return;
                }
                info.Text = L.S("Double-click or Enter opens an invoice. Click Status to change the print status of all its prints (saved at once).\n" +
                                "Click Paid (or Space) to mark it paid or unpaid. Overdue invoices are in amber.",
                                "Dobbeltklik eller Enter åbner en faktura. Klik på Status for at ændre printstatus for alle dens print (gemmes med det samme).\n" +
                                "Klik på Betalt (eller mellemrumstasten) for at markere den betalt eller ubetalt. Forfaldne fakturaer står med gult.");
            }

            void ShowDone(string text, InfoKind kind)
            {
                doneShown = true;
                info.Text = text;
                info.Kind = kind;
            }

            // ---------- actions ----------
            /// paid-tick: ticks / unticks the whole invoice (all its jobs), saved at once; the ✓ line says it (and when the
            /// Payment filter now hides it).
            internal void TogglePaid(InvoiceRow row)
            {
                if (row == null) return;
                bool paid = !row.Paid;
                DateTime now = DateTime.Now;
                // qol 5: Undo puts each job's paid date back as it was
                var ids = new HashSet<string>(row.Jobs.Select(j => j.Id));
                var before = list.Where(j => ids.Contains(j.Id)).Select(j => new KeyValuePair<QuoteRecord, string>(j, j.PaidAt ?? "")).ToList();
                string no = row.No;
                History.SetPaid(list, row.No, paid, now);
                if (save != null) save();
                Build(row.No);
                bool gone = !shown.Any(r => string.Equals(r.No, row.No, StringComparison.OrdinalIgnoreCase));
                string text = paid ? L.F("✓ Invoice {0} marked as paid ({1}).", "✓ Faktura {0} markeret som betalt ({1}).", row.No, now.ToString("dd-MM-yyyy", Num.Dk))
                                   : L.F("✓ Invoice {0} marked as unpaid.", "✓ Faktura {0} markeret som ubetalt.", row.No);
                if (gone) text += " " + L.S("It no longer shows with this filter.", "Den vises ikke længere med dette filter.");
                ShowDone(text, InfoKind.Success);
                undoPaid = () =>
                {
                    undoPaid = null;
                    foreach (var kv in before) kv.Key.PaidAt = kv.Value;
                    if (save != null) save();
                    Build(no);
                    var back = rows.FirstOrDefault(r => string.Equals(r.No, no, StringComparison.OrdinalIgnoreCase));
                    ShowDone(back != null && back.Paid ? L.F("✓ Undone: invoice {0} is paid again.", "✓ Fortrudt: faktura {0} er betalt igen.", no)
                                                       : L.F("✓ Undone: invoice {0} is unpaid again.", "✓ Fortrudt: faktura {0} er ubetalt igen.", no), InfoKind.Success);
                    grid.Focus();
                };
                info.SetLink(L.S("Undo", "Fortryd"), () => { if (undoPaid != null) undoPaid(); });
                grid.Focus();
            }

            Action undoPaid;   // the last paid / unpaid tick's Undo (null = none)

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
            internal void TestSearch(string text) { txtSearch.Text = text; FlushSearch(); }   // no 200 ms wait in tests
            internal void TestStatusFilter(int index) { cmbStatus.SelectedIndex = index; }
            internal void TestPeriod(int index) { cmbPeriod.SelectedIndex = index; }
            internal void TestPayment(int index) { cmbPayment.SelectedIndex = index; }
            internal void TestTogglePaid() { TogglePaid(SelectedRow()); }
            // release-test-6: the real input paths (window messages through the same handlers a user's keys and clicks use)
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
            static IntPtr XY(Point p) { return (IntPtr)((p.Y << 16) | (p.X & 0xFFFF)); }
            static void TestClickAt(Control c, Point p)
            {
                SendMessage(c.Handle, 0x201, (IntPtr)1, XY(p));   // WM_LBUTTONDOWN, MK_LBUTTON
                SendMessage(c.Handle, 0x202, IntPtr.Zero, XY(p));  // WM_LBUTTONUP
                Application.DoEvents();
            }
            /// The "Undo" link on the ✓ line, clicked like a user would; false = no Undo link on screen.
            internal bool TestUndoPaid()
            {
                var lc = info.LinkControl;
                if (lc == null || !lc.Visible || !info.Visible || lc.Text != L.S("Undo", "Fortryd") || lc.Width <= 0) return false;
                TestClickAt(lc, new Point(lc.Width / 2, lc.Height / 2));
                return true;
            }
            /// Esc in the search box through the form's key handling (ProcessCmdKey, then Cancel = Close): true = handled.
            internal bool TestEscape()
            {
                ActiveControl = txtSearch;
                var m = Message.Create(txtSearch.Handle, 0x100, (IntPtr)Keys.Escape, IntPtr.Zero);   // WM_KEYDOWN
                return txtSearch.PreProcessMessage(ref m);
            }
            internal bool TestClosedByEscape { get { return DialogResult == DialogResult.Cancel || IsDisposed || !Visible; } }
            /// Space in the list (WM_KEYDOWN to the grid: OnKeyDown -> the KeyDown handler).
            internal void TestSpace()
            {
                ActiveControl = grid;
                SendMessage(grid.Handle, 0x100, (IntPtr)Keys.Space, IntPtr.Zero);
                SendMessage(grid.Handle, 0x101, (IntPtr)Keys.Space, IntPtr.Zero);
                Application.DoEvents();
            }
            /// A mouse click on the Paid cell of the row now showing invoice no (WM_LBUTTONDOWN / UP at the cell's middle).
            internal bool TestClickPaid(string no)
            {
                int i = shown.FindIndex(r => string.Equals(r.No, no, StringComparison.OrdinalIgnoreCase));
                if (i < 0) return false;
                var rc = grid.GetCellDisplayRectangle(colPaid.Index, i, false);
                if (rc.Width <= 0) return false;
                TestClickAt(grid, new Point(rc.Left + rc.Width / 2, rc.Top + rc.Height / 2));
                return true;
            }
            /// Types into the search box without running the waiting search (the 200 ms delay test).
            internal void TestType(string text) { ActiveControl = txtSearch; txtSearch.Text = text; }   // typed with the focus in the box
            internal bool TestSearchWaiting { get { return searchTimer.Enabled; } }
            internal string TestSearchText { get { return txtSearch.Text; } }
            internal bool TestLastSwallowed { get { return grid.LastClickSwallowed; } }
            internal string TestEmptyText { get { return shown.Count == 0 && rows.Count > 0 ? EmptyText : ""; } }
            internal int TestRowsShown { get { return rowsShown; } }
            internal int TestHeight { get { return Height; } }
            internal string TestUnpaidLine { get { return lblUnpaid.Text + (lblOverdue.Text.Length > 0 ? " " + lblOverdue.Text : ""); } }
            internal int TestWidth { get { return Width; } }
            internal string TestPaidTip(int rowIndex) { return rowIndex >= 0 && rowIndex < shown.Count ? TipFor(rowIndex, colPaid.Index) : null; }
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
                       (shown.Count > 6 ? " …" : "") + "), filter \"" + cmbStatus.Text + "\", period \"" + cmbPeriod.Text + "\", payment \"" + cmbPayment.Text + "\", search \"" +
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
            // paid-tick: two paid (the café and Emma), the rest unpaid (Jonas's overdue)
            l[4].PaidAt = "2026-11-02";
            l[6].PaidAt = "2026-10-20";
            return l;
        }
    }
}
