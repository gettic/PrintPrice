using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Price history: the saved quotes, newest first. Search, Show filter, one Price column chosen by the row's For,
    /// For (drop-down), Note, Printed and Failed editable in the list and saved to history.json at once; a month picker
    /// with six cards (History.MonthSummary); Delete / Quote… / Invoice… / Repeat print / Open again / Close, and the same
    /// actions in a right-click row menu.
    /// Picture mode (offline): sample list in memory, never saves.
    public class HistoryForm : CaptionForm
    {
        readonly List<QuoteRecord> list;
        readonly bool offline;
        string problem;
        readonly TextBox txtSearch;
        readonly ComboBox cmbShow;
        readonly Label lblCount;
        readonly HistoryGrid grid;
        readonly Panel gridFrame;
        readonly InfoBox info;
        readonly Button btnDelete, btnOpen, btnRepeat, btnQuote, btnInvoice, btnOlder, btnNewer, btnBackup;
        readonly TableLayoutPanel top, bottom, cards;
        readonly ComboBox cmbMonth;
        readonly StatCard cardPrints, cardTime, cardCustomers, cardFriends, cardCosts, cardProfit;
        readonly DataGridViewTextBoxColumn colDate, colModel, colTime, colGrams, colPrice, colNote, colInvoice, colPrinter;
        readonly ComboBox cmbPrinter;    // Printer filter (only with 2+ printers): All printers / one printer
        readonly Label lblPrinterCap;
        bool printerMode;                // 2+ printers (in Settings or in the records): Printer column + filter + profit tooltip
        Dictionary<string, string> printerNames = new Dictionary<string, string>();   // id -> name now (removed ones: the records' name)

        /// The settings (printer names and order); set before the window shows. null = names from the records only.
        public AppSettings Settings;

        sealed class PrinterChoice
        {
            public string Id;   // null = all printers
            public string Text;
            public override string ToString() { return Text; }
        }
        readonly DataGridViewComboBoxColumn colFor;
        readonly ToolTip modelTip;   // dark tooltip with the full model name when it is cut off
        readonly DataGridViewCheckBoxColumn colPrinted, colFailed;   // read-only cells, toggled by click / Space, drawn in the theme (PaintTicks)
        readonly ContextMenuStrip rowMenu;
        ToolStripMenuItem miPrinted, miFailed;
        System.Windows.Forms.Timer deleteTimer;
        bool deleteArmed, filling, refillingMonths;
        string saveError;

        /// The quote to open again (set when the window closes with OK).
        public QuoteRecord Selected;
        /// True when the window closed with "Repeat print" (Selected = the quote to repeat), false for "Open again".
        public bool RepeatSelected;
        /// Quote… / Invoice…: (owner, record, invoice) opens the quote window for the record (MainForm.ShowRecordQuote);
        /// null = the two buttons are hidden.
        public Action<IWin32Window, QuoteRecord, bool> QuoteRequested;
        /// Backup…: (owner) opens the Backup window (MainForm.OpenBackup); true = a restore replaced the list (in place)
        /// and the settings, so the window reloads. null = the button is hidden.
        public Func<IWin32Window, bool> BackupRequested;

        sealed class MonthItem
        {
            public DateTime Month;   // 1st of the month; MinValue = all months
            public string Text;
            public override string ToString() { return Text; }
        }

        const int VisibleRows = 10;

        public sealed class ForChoice
        {
            public int Value { get; set; }
            public string Text { get; set; }
        }

        public HistoryForm(List<QuoteRecord> quotes, string loadProblem, bool offline)
        {
            list = quotes ?? new List<QuoteRecord>();
            problem = loadProblem;
            this.offline = offline;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = L.S("Price history", "Prishistorik");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            if (Program.Hidden || Screenshots.Active) Screenshots.Place(this);   // CenterParent would pull it back on screen
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14), Margin = Padding.Empty };
            Controls.Add(root);

            // ---------- search + month + filter + count ----------
            top = new TableLayoutPanel { ColumnCount = 11, AutoSize = true, Margin = new Padding(0, 0, 0, 8), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            for (int i = 0; i < 10; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // search box and Show combo: same top (Top anchor, same margin) and same height (FitLayout), captions centred beside them
            // Tag "single": stays a single-line edit (its cue banner needs that), see Theme.TextMargins
            txtSearch = new TextBox { Width = 260, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 3, 24, 3), Tag = "single" };
            txtSearch.HandleCreated += delegate
            {
                SendMessage(txtSearch.Handle, 0x1501, (IntPtr)1, L.S("Model, link or note", "Model, link eller note"));   // EM_SETCUEBANNER
            };
            txtSearch.TextChanged += delegate { Fill(); };
            txtSearch.Width = 220;
            // month: "All months" or one month (newest first); ‹ = older, › = newer; the cards show that month
            cmbMonth = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 3, 4, 3) };
            cmbMonth.Items.Add(new MonthItem { Month = DateTime.MinValue, Text = L.S("All months", "Alle måneder") });
            foreach (var m in History.Months(list)) cmbMonth.Items.Add(new MonthItem { Month = m, Text = MonthText(m) });
            cmbMonth.SelectedIndex = cmbMonth.Items.Count > 1 ? 1 : 0;   // the newest month
            cmbMonth.SelectedIndexChanged += delegate { if (!refillingMonths) { Fill(); UpdateMonthButtons(); } };
            btnOlder = new Button { Text = "‹", Size = new Size(28, 25), Margin = new Padding(0, 3, 4, 3), Anchor = AnchorStyles.Left | AnchorStyles.Top,
                                    Padding = Padding.Empty, AccessibleName = L.S("Older month", "Ældre måned") };
            btnNewer = new Button { Text = "›", Size = new Size(28, 25), Margin = new Padding(0, 3, 24, 3), Anchor = AnchorStyles.Left | AnchorStyles.Top,
                                    Padding = Padding.Empty, AccessibleName = L.S("Newer month", "Nyere måned") };
            btnOlder.Click += delegate { if (cmbMonth.SelectedIndex < cmbMonth.Items.Count - 1) cmbMonth.SelectedIndex++; };
            btnNewer.Click += delegate { if (cmbMonth.SelectedIndex > 0) cmbMonth.SelectedIndex--; };
            cmbShow = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 3, 0, 3) };
            cmbShow.Items.AddRange(new object[]
            {
                L.S("All quotes", "Alle tilbud"), L.S("Friends & family", "Venner og familie"),
                L.S("Customers", "Kunder"), L.S("Failed", "Fejlede"), L.S("Not printed", "Ikke printet")
            });
            cmbShow.SelectedIndex = 0;
            cmbShow.SelectedIndexChanged += delegate { Fill(); };
            // Printer filter (shown on Load only with 2+ printers): the cards and the list follow it
            cmbPrinter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 3, 0, 3), Visible = false };
            cmbPrinter.SelectedIndexChanged += delegate { if (!filling) Fill(); };
            lblPrinterCap = Caption("Printer");
            lblPrinterCap.Visible = false;
            lblCount = new Label { AutoSize = true, Tag = "muted", Anchor = AnchorStyles.Right, Margin = new Padding(8, 3, 0, 3) };
            top.Controls.Add(Caption(L.S("Search", "Søg")), 0, 0);
            top.Controls.Add(txtSearch, 1, 0);
            top.Controls.Add(Caption(L.S("Month", "Måned")), 2, 0);
            top.Controls.Add(cmbMonth, 3, 0);
            top.Controls.Add(btnOlder, 4, 0);
            top.Controls.Add(btnNewer, 5, 0);
            top.Controls.Add(Caption(L.S("Show", "Vis")), 6, 0);
            top.Controls.Add(cmbShow, 7, 0);
            top.Controls.Add(lblPrinterCap, 8, 0);
            top.Controls.Add(cmbPrinter, 9, 0);
            top.Controls.Add(lblCount, 10, 0);
            root.Controls.Add(top);

            // ---------- the month in six cards (History.MonthSummary) ----------
            cards = new TableLayoutPanel { ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 0, 0, 10), Size = new Size(900, 90) };
            for (int i = 0; i < 6; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
            cardPrints = new StatCard(L.S("Prints", "Print"), Theme.Text, false);
            cardTime = new StatCard(L.S("Print time", "Printtid"), Theme.Text, false);
            cardCustomers = new StatCard(L.S("Customers", "Kunder"), Theme.Others, false);
            cardFriends = new StatCard(L.S("Friends & family", "Venner og familie"), Theme.Friends, false);
            cardCosts = new StatCard(L.S("Costs", "Omkostninger"), Theme.Text, false);
            cardProfit = new StatCard(L.S("Profit", "Overskud"), Theme.Success, true);
            var all = new[] { cardPrints, cardTime, cardCustomers, cardFriends, cardCosts, cardProfit };
            for (int i = 0; i < all.Length; i++)
            {
                all[i].Dock = DockStyle.Fill;
                all[i].Margin = new Padding(0, 0, i < all.Length - 1 ? 6 : 0, 0);
                cards.Controls.Add(all[i], i, 0);
            }
            root.Controls.Add(cards);

            // ---------- the list ----------
            grid = new HistoryGrid
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackgroundColor = Theme.Input, GridColor = Theme.Border,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AllowUserToResizeColumns = false,
                AllowUserToOrderColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, CellBorderStyle = DataGridViewCellBorderStyle.None,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single, EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ScrollBars = ScrollBars.Vertical, StandardTab = true
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

            colDate = TextColumn(L.S("Date", "Dato"), false);
            colModel = TextColumn(L.S("Model", "Model"), false);
            colTime = TextColumn(L.S("Time", "Tid"), true);
            colGrams = TextColumn(L.S("Grams", "Gram"), true);
            colPrice = TextColumn(L.S("Price", "Pris"), true);
            colPrice.DefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            colFor = new DataGridViewComboBoxColumn
            {
                HeaderText = L.S("For", "Til"), DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing, FlatStyle = FlatStyle.Flat,
                DataSource = new List<ForChoice>
                {
                    new ForChoice { Value = History.ForFriends, Text = L.S("Friends", "Venner") },
                    new ForChoice { Value = History.ForCustomer, Text = L.S("Customer", "Kunde") }
                },
                ValueMember = "Value", DisplayMember = "Text", ValueType = typeof(int), SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colPrinter = TextColumn("Printer", false);   // the quote's printer (only with 2+ printers)
            colPrinter.Visible = false;
            colNote = TextColumn(L.S("Note", "Note"), false);
            colNote.ReadOnly = false;
            colNote.MaxInputLength = 200;
            colNote.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;   // takes what is left (no scroll bar = a bit wider)
            // Failed: ticking it marks the print as failed (its cost is earned back by new quotes when that is on in Settings)
            colFailed = new DataGridViewCheckBoxColumn { HeaderText = L.S("Failed", "Fejlet"), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable };
            colFailed.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            // Printed: the quote was really printed (counts in the month's cards); an invoice ticks it too
            colPrinted = new DataGridViewCheckBoxColumn { HeaderText = L.S("Printed", "Printet"), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable };
            colPrinted.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colInvoice = TextColumn(L.S("Invoice", "Faktura"), false);   // invoice no. (muted), empty = no invoice yet
            grid.Columns.AddRange(new DataGridViewColumn[] { colDate, colModel, colTime, colGrams, colPrice, colFor, colPrinter, colNote, colPrinted, colFailed, colInvoice });

            grid.CellFormatting += GridCellFormatting;
            grid.CurrentCellDirtyStateChanged += delegate
            {
                // the For drop-down applies at once (no need to leave the cell)
                if (grid.IsCurrentCellDirty && grid.CurrentCell != null && grid.CurrentCell.OwningColumn == colFor)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += GridCellValueChanged;
            grid.DataError += (s, e) => { e.ThrowException = false; };
            // the grid's own cell tooltip is Windows' light one: off; a cut-off model name shows in a dark one instead
            grid.ShowCellToolTips = false;
            modelTip = new ToolTip { OwnerDraw = true, InitialDelay = 400 };
            modelTip.SetToolTip(btnOlder, btnOlder.AccessibleName);   // dark tooltips on ‹ ›
            modelTip.SetToolTip(btnNewer, btnNewer.AccessibleName);
            modelTip.Draw += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.InfoBack)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                string text = e.ToolTipText ?? "";
                if (text.IndexOf('\n') < 0)
                {
                    TextRenderer.DrawText(e.Graphics, text, e.Font, e.Bounds, Theme.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    return;
                }
                // several lines (the Profit card's "by printer"): the measured block centred, each line centred
                const TextFormatFlags multi = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix;
                Size sz = TextRenderer.MeasureText(e.Graphics, text, e.Font, e.Bounds.Size, multi);
                TextRenderer.DrawText(e.Graphics, text, e.Font,
                    new Rectangle(e.Bounds.X, e.Bounds.Y + (e.Bounds.Height - sz.Height) / 2, e.Bounds.Width, sz.Height), Theme.Text, multi);
            };
            grid.CellMouseEnter += (s, e) =>
            {
                if (e.RowIndex < 0 || (e.ColumnIndex != colModel.Index && e.ColumnIndex != colNote.Index && e.ColumnIndex != colInvoice.Index &&
                                       e.ColumnIndex != colPrinter.Index)) return;   // cut-off model, printer, note or invoice code
                var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                string text = Convert.ToString(cell.FormattedValue);
                var r = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                var pad = cell.InheritedStyle.Padding;
                if (string.IsNullOrEmpty(text) || TextRenderer.MeasureText(text, grid.Font).Width <= r.Width - pad.Horizontal - 4) return;
                modelTip.Show(text, grid, r.Left, r.Bottom + 2, 8000);
            };
            grid.CellMouseLeave += delegate { modelTip.Hide(grid); };
            FormClosed += delegate { modelTip.Dispose(); };
            grid.EditingControlShowing += (s, e) =>
            {
                e.CellStyle.BackColor = Theme.Input;
                e.CellStyle.ForeColor = Theme.Text;
                e.CellStyle.SelectionBackColor = Theme.InfoAccent;
                e.CellStyle.SelectionForeColor = Color.White;
                if (e.Control is ComboBox || e.Control is TextBox) Theme.Apply(e.Control);
            };
            grid.CellClick += (s, e) =>
            {
                // one click on For or Note edits it (For opens its list)
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var col = grid.Columns[e.ColumnIndex];
                if (col == colFailed) { ToggleFailed(e.RowIndex); return; }
                if (col == colPrinted) { TogglePrinted(e.RowIndex); return; }
                if (col != colFor && col != colNote) return;
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (!grid.BeginEdit(col == colNote)) return;
                var cb = grid.EditingControl as ComboBox;
                if (cb != null) cb.DroppedDown = true;
            };
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var col = grid.Columns[e.ColumnIndex];
                if (col == colFor || col == colNote || col == colFailed || col == colPrinted) return;   // those edit / toggle
                OpenAgain();
            };
            grid.OpenRequested = OpenAgain;
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && !grid.IsCurrentCellInEditMode) { e.Handled = true; DeleteClicked(); }
                // Space on the Failed / Printed cell ticks / unticks it
                if (e.KeyCode == Keys.Space && !grid.IsCurrentCellInEditMode && grid.CurrentCell != null)
                {
                    if (grid.CurrentCell.OwningColumn == colFailed) { e.Handled = true; ToggleFailed(grid.CurrentCell.RowIndex); }
                    else if (grid.CurrentCell.OwningColumn == colPrinted) { e.Handled = true; TogglePrinted(grid.CurrentCell.RowIndex); }
                }
                // menu key / Shift+F10: the row menu at the current row
                if ((e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift)) && !grid.IsCurrentCellInEditMode && SelectedRecord() != null)
                {
                    e.Handled = true;
                    var rc = grid.GetRowDisplayRectangle(grid.CurrentRow.Index, false);
                    ShowRowMenu(grid.PointToScreen(new Point(rc.Left + rc.Width / 3, rc.Bottom)));
                }
            };
            // right-click a row: select it, then the row menu
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                if (grid.IsCurrentCellInEditMode) grid.EndEdit();
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
                grid.Rows[e.RowIndex].Selected = true;
                ShowRowMenu(Cursor.Position);
            };
            grid.CellPainting += PaintTicks;
            grid.SelectionChanged += delegate { DisarmDelete(); UpdateButtons(); };
            grid.HandleCreated += delegate { DarkScrollBars(grid); };

            // 1 px border around the list in the theme's border colour
            gridFrame = new Panel { Padding = new Padding(1), BackColor = Theme.Border, Margin = new Padding(0, 0, 0, 10), Size = new Size(900, 280) };
            gridFrame.Controls.Add(grid);
            root.Controls.Add(gridFrame);

            // ---------- help / status ----------
            info = new InfoBox(900, 9.5f) { Margin = new Padding(0, 0, 0, 10) };
            root.Controls.Add(info);

            // ---------- buttons ----------
            bottom = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnDelete = new Button { Text = L.S("Delete", "Slet"), Size = new Size(84, 32), Padding = Padding.Empty, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            btnDelete.Click += delegate { DeleteClicked(); };
            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
            Func<string, int, Button> MakeButton = (text, rightGap) => new Button
            {
                Text = text, AutoSize = true, MinimumSize = new Size(92, 32), Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(0, 0, rightGap, 0), UseMnemonic = false
            };
            btnQuote = MakeButton(L.S("Quote…", "Tilbud…"), 6);
            btnInvoice = MakeButton(L.S("Invoice…", "Faktura…"), 14);   // a wider gap: the page buttons, then the main-window ones
            btnRepeat = MakeButton(L.S("Repeat print", "Print igen"), 6);
            btnQuote.Click += delegate { ShowQuote(false); };
            btnInvoice.Click += delegate { ShowQuote(true); };
            btnRepeat.Click += delegate { RepeatPrint(); };
            btnOpen = new Button
            {
                Text = L.S("Open again", "Åbn igen"), AutoSize = true, MinimumSize = new Size(110, 32), Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(0, 0, 6, 0), Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            btnOpen.Click += delegate { OpenAgain(); };
            var close = new Button { Text = L.S("Close", "Luk"), Size = new Size(84, 32), Padding = Padding.Empty, Margin = Padding.Empty, DialogResult = DialogResult.Cancel };
            right.Controls.Add(btnQuote);
            right.Controls.Add(btnInvoice);
            right.Controls.Add(btnRepeat);
            right.Controls.Add(btnOpen);
            right.Controls.Add(close);

            // the row menu (right-click / menu key): every action for one quote
            rowMenu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Renderer = new QuoteMenuRenderer(),
                                             BackColor = Theme.Input, ForeColor = Theme.Text };
            Func<string, Action, ToolStripMenuItem> Item = (text, act) =>
            {
                var mi = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
                mi.Click += delegate { act(); };
                return mi;
            };
            var miOpen = Item(L.S("Open again", "Åbn igen"), OpenAgain);
            miOpen.Font = new Font(rowMenu.Font, FontStyle.Bold);
            rowMenu.Items.Add(miOpen);
            rowMenu.Items.Add(Item(L.S("Repeat print", "Print igen"), RepeatPrint));
            rowMenu.Items.Add(new ToolStripSeparator());
            var miQuote = Item(L.S("Quote…", "Tilbud…"), () => ShowQuote(false));
            var miInvoice = Item(L.S("Invoice…", "Faktura…"), () => ShowQuote(true));
            rowMenu.Items.Add(miQuote);
            rowMenu.Items.Add(miInvoice);
            rowMenu.Items.Add(new ToolStripSeparator());
            miPrinted = Item(L.S("Printed", "Printet"), () => { if (grid.CurrentRow != null) TogglePrinted(grid.CurrentRow.Index); });
            miFailed = Item(L.S("Failed", "Fejlet"), () => { if (grid.CurrentRow != null) ToggleFailed(grid.CurrentRow.Index); });
            rowMenu.Items.Add(miPrinted);
            rowMenu.Items.Add(miFailed);
            rowMenu.Items.Add(new ToolStripSeparator());
            rowMenu.Items.Add(Item(L.S("Delete", "Slet"), DeleteClicked));
            rowMenu.Opening += delegate
            {
                var r = SelectedRecord();
                miPrinted.Checked = r != null && r.Printed;
                miFailed.Checked = r != null && r.Failed;
                miQuote.Visible = miInvoice.Visible = QuoteRequested != null;
            };
            FormClosed += delegate { rowMenu.Dispose(); };
            // left: Delete + Backup… (6 px apart)
            var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            btnBackup = MakeButton(L.S("Backup…", "Sikkerhedskopi…"), 0);
            btnBackup.Margin = new Padding(6, 0, 0, 0);
            btnBackup.Click += delegate { BackupClicked(); };
            left.Controls.Add(btnDelete);
            left.Controls.Add(btnBackup);
            bottom.Controls.Add(left, 0, 0);
            bottom.Controls.Add(right, 1, 0);
            root.Controls.Add(bottom);

            AcceptButton = btnOpen;
            // Enter in the search box only searches (it filters as you type): no Open again from there
            txtSearch.Enter += delegate { AcceptButton = null; };
            txtSearch.Leave += delegate { AcceptButton = btnOpen; };
            txtSearch.KeyPress += (ks, ke) => { if (ke.KeyChar == '\r') ke.Handled = true; };   // no ding
            CancelButton = close;
            Theme.Apply(this);
            // Apply resets these
            gridFrame.BackColor = Theme.Border;
            grid.BackgroundColor = Theme.Input;
            ResumeLayout(true);

            Load += delegate
            {
                btnQuote.Visible = btnInvoice.Visible = QuoteRequested != null;
                btnBackup.Visible = BackupRequested != null;
                SetupPrinters();
                FitLayout(); Fill(); ShowInfo(); UpdateMonthButtons();
            };
            FormClosed += delegate { if (deleteTimer != null) { deleteTimer.Stop(); deleteTimer.Dispose(); deleteTimer = null; } };
            ActiveControl = grid;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override bool ShowWithoutActivation { get { return Screenshots.Active || Program.Hidden; } }

        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 10, 3) };
        }

        static DataGridViewTextBoxColumn TextColumn(string header, bool rightAligned)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = header, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (rightAligned)
            {
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            return c;
        }

        /// Column widths, row height and the list's size in real pixels (after DPI scaling): widths are 96-dpi values
        /// scaled by the window's DPI, at least the measured header/sample text; the list shows VisibleRows rows.
        void FitLayout()
        {
            float sc = CurrentAutoScaleDimensions.Height / 96f;
            Func<int, int> px = v => (int)Math.Round(v * sc);
            var headFont = grid.ColumnHeadersDefaultCellStyle.Font;
            Func<DataGridViewColumn, string, int, int> width = (col, sample, w) =>
            {
                int need = Math.Max(TextRenderer.MeasureText(col.HeaderText, headFont).Width,
                                    sample == null ? 0 : TextRenderer.MeasureText(sample, col == colPrice ? colPrice.DefaultCellStyle.Font : grid.Font).Width) + px(16);
                return Math.Max(px(w), need);
            };
            colDate.Width = width(colDate, "00-00-0000 00:00", 130);
            colModel.Width = px(172);   // "Phone stand (adjustable)" (160) + cell padding 12 fits; longer names get the dark tooltip (ModelTip)
            colTime.Width = width(colTime, L.S("000 h 00 m", "000 t 00 m"), 92);
            colGrams.Width = width(colGrams, "0000,00", 70);
            colPrice.Width = width(colPrice, "0000,00", 74);
            colFor.Width = width(colFor, L.S("Customer", "Venner"), 96);
            colPrinter.Width = width(colPrinter, null, 140);   // "Bambu Lab A1 mini" fits; longer names get the dark tooltip
            colNote.MinimumWidth = px(110);
            colPrinted.Width = width(colPrinted, null, 72);
            colFailed.Width = width(colFailed, null, 64);
            colInvoice.Width = width(colInvoice, "2026-WWWW", 80);   // the widest possible code (W = widest letter), measured: never cut

            // Month / Show combos exactly as tall as the search box (owner-drawn: height = ItemHeight + its frame, measured),
            // the ‹ › buttons too
            foreach (var cb in new[] { cmbShow, cmbMonth, cmbPrinter })
            {
                int frame = cb.Height - cb.ItemHeight;
                if (txtSearch.Height - frame > 0) cb.ItemHeight = txtSearch.Height - frame;
            }
            btnOlder.Size = btnNewer.Size = new Size(px(28), txtSearch.Height);
            // the top row exactly as tall as the search box + its margins (the Printer caption / filter made it 1 px taller)
            top.RowStyles.Clear();
            top.RowStyles.Add(new RowStyle(SizeType.Absolute, txtSearch.Height + txtSearch.Margin.Vertical));

            int rowH = TextRenderer.MeasureText("Ag", grid.Font).Height + px(8);
            grid.RowTemplate.Height = rowH;
            grid.ColumnHeadersHeight = rowH + px(4);
            int cols = colDate.Width + colModel.Width + colTime.Width + colGrams.Width + colPrice.Width + colFor.Width
                     + colPrinted.Width + colFailed.Width + colInvoice.Width + px(155)    // Note gets the rest (~155 px + no-scroll-bar room; +40 from Model / Invoice)
                     + (printerMode ? colPrinter.Width : 0);                              // 2+ printers: the window is wider by the Printer column
            gridFrame.Size = new Size(cols + SystemInformation.VerticalScrollBarWidth + 2,
                                      grid.ColumnHeadersHeight + VisibleRows * rowH + 2);
            info.SetWidth(gridFrame.Width);
            cards.Size = new Size(gridFrame.Width, px(90));
            // six equal cards (the gaps are the first five cards' right margins; spare px 1 each from the left), not equal
            // columns: those made the last card 6 px wider
            int gapW = px(6), cardW = (gridFrame.Width - 5 * gapW) / 6, spare = gridFrame.Width - 5 * gapW - 6 * cardW;
            for (int i = 0; i < 6; i++)
            {
                var cs = cards.ColumnStyles[i];
                cs.SizeType = SizeType.Absolute;
                cs.Width = cardW + (i < spare ? 1 : 0) + (i < 5 ? gapW : 0);
                cards.GetControlFromPosition(i, 0).Margin = new Padding(0, 0, i < 5 ? gapW : 0, 0);
            }
            top.MinimumSize = new Size(gridFrame.Width, 0);      // the count ends at the list's right edge
            bottom.MinimumSize = new Size(gridFrame.Width, 0);   // Close ends there too

            // Delete is wide enough for its "Delete?" second step too (no jump)
            btnDelete.Width = Math.Max(btnDelete.Width, TextRenderer.MeasureText(ConfirmText, btnDelete.Font).Width + px(24));
        }

        static string ConfirmText { get { return L.S("Really delete?", "Slet virkelig?"); } }

        // ---------- several printers ----------
        /// Load: with 2+ printers (in Settings, or removed ones still in the records) the Printer column, the Printer
        /// filter and the Profit card's "by printer" tooltip show; with one printer nothing changes.
        void SetupPrinters()
        {
            var all = History.PrinterTotals(list, Settings, 0, 0);
            printerNames = new Dictionary<string, string>();
            foreach (var t in all) printerNames[t.Id] = AppSettings.CleanPrinterName(t.Name);
            printerMode = all.Count >= 2;
            colPrinter.Visible = cmbPrinter.Visible = lblPrinterCap.Visible = printerMode;
            cmbShow.Margin = new Padding(0, 3, 0, 3);   // again after a restore that left one printer
            if (!printerMode) return;
            cmbShow.Margin = new Padding(0, 3, 24, 3);
            filling = true;
            cmbPrinter.Items.Clear();
            cmbPrinter.Items.Add(new PrinterChoice { Id = null, Text = L.S("All printers", "Alle printere") });
            foreach (var t in all)
                cmbPrinter.Items.Add(new PrinterChoice { Id = t.Id, Text = printerNames[t.Id] + (t.Removed ? L.S(" (removed)", " (fjernet)") : "") });
            cmbPrinter.SelectedIndex = 0;
            filling = false;
        }

        /// The printer chosen in the filter (null = all printers).
        string SelectedPrinter
        {
            get { var c = printerMode ? cmbPrinter.SelectedItem as PrinterChoice : null; return c == null ? null : c.Id; }
        }

        string PrinterText(QuoteRecord r)
        {
            string name;
            if (printerNames.TryGetValue(History.PrinterOf(r), out name)) return name;
            return AppSettings.CleanPrinterName(r.PrinterName);
        }

        // ---------- list ----------
        static string ModelText(QuoteRecord r) { return History.ModelText(r); }

        static string DateText(QuoteRecord r)
        {
            DateTime t;
            return History.TryTime(r.SavedAt, out t) ? t.ToString("dd-MM-yyyy HH:mm", Num.Dk) : r.SavedAt;
        }

        /// "October 2026" / "Oktober 2026".
        static string MonthText(DateTime m)
        {
            string s = L.Danish ? m.ToString("MMMM yyyy", Num.Dk) : m.ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
            return s.Length > 0 ? char.ToUpper(s[0], Num.Dk) + s.Substring(1) : s;
        }

        /// The chosen month (1st of it), or MinValue for all months.
        DateTime SelectedMonth
        {
            get { var m = cmbMonth.SelectedItem as MonthItem; return m == null ? DateTime.MinValue : m.Month; }
        }

        /// Picture mode: shows that month (if it has quotes).
        internal void SelectMonth(DateTime month)
        {
            for (int i = 0; i < cmbMonth.Items.Count; i++)
                if (((MonthItem)cmbMonth.Items[i]).Month == new DateTime(month.Year, month.Month, 1)) { cmbMonth.SelectedIndex = i; return; }
        }

        void UpdateMonthButtons()
        {
            btnOlder.Enabled = cmbMonth.SelectedIndex < cmbMonth.Items.Count - 1;
            btnNewer.Enabled = cmbMonth.SelectedIndex > 0;
        }

        bool Matches(QuoteRecord r)
        {
            return History.Matches(r, SelectedMonth, SelectedPrinter, cmbShow.SelectedIndex, txtSearch.Text,
                                   printerMode ? (Func<QuoteRecord, string>)PrinterText : null);
        }

        /// Rebuilds the rows from the list (search + filter), keeping the selected quote if it is still shown.
        void Fill()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            var keep = SelectedRecord();
            filling = true;
            grid.SuspendLayout();
            grid.Rows.Clear();
            int shown = 0;
            foreach (var r in list)
            {
                if (!Matches(r)) continue;
                int i = grid.Rows.Add(DateText(r), ModelText(r),
                                      r.Hours + L.S(" h ", " t ") + r.Minutes + " m",
                                      Num.Fmt(r.Grams), History.PriceFor(r).ToString("0.00", Num.Dk),
                                      r.For == History.ForFriends ? History.ForFriends : History.ForCustomer,
                                      printerMode ? PrinterText(r) : "", r.Note ?? "", r.Printed, r.Failed, r.InvoiceNo ?? "");
                grid.Rows[i].Tag = r;
                grid.Rows[i].Height = grid.RowTemplate.Height;
                if (r == keep) { grid.CurrentCell = grid.Rows[i].Cells[0]; }
                shown++;
            }
            if (grid.Rows.Count > 0 && (grid.CurrentRow == null || !grid.CurrentRow.Selected))
            {
                grid.CurrentCell = grid.Rows[0].Cells[0];
                grid.Rows[0].Selected = true;
            }
            grid.ResumeLayout();
            filling = false;
            lblCount.Text = shown == list.Count
                ? L.F(list.Count == 1 ? "{0} quote" : "{0} quotes", "{0} tilbud", list.Count)
                : L.F("{0} of {1} quotes", "{0} af {1} tilbud", shown, list.Count);
            UpdateButtons();
            UpdateCards();
        }

        // ---------- month cards ----------
        /// The six cards for the chosen month (all months = the sum of every month). Search and Show don't change them.
        void UpdateCards()
        {
            MonthTotals t;
            var m = SelectedMonth;
            string printer = SelectedPrinter;
            UpdateProfitTip(m);
            if (m != DateTime.MinValue) t = History.MonthSummary(list, m.Year, m.Month, printer);
            else if (printer != null) t = History.MonthSummary(list, 0, 0, printer);   // one printer, all time
            else t = History.AllMonthsTotals(list);   // = MonthSummary of each month, one pass
            Func<int, string> prints = n => n == 1 ? L.S("1 print", "1 print") : L.F("{0} prints", "{0} print", n);
            cardPrints.Set(t.Prints.ToString(Num.Dk),
                t.FailedPrints > 0 ? L.F("+ {0} failed", "+ {0} fejlede", t.FailedPrints) : L.S("no failed prints", "ingen fejlprint"), Theme.Text);
            int minutes = (int)Math.Round(t.PrintHours * 60);
            cardTime.Set(L.F("{0} h {1:00} m", "{0} t {1:00} m", minutes / 60, minutes % 60),
                (t.Grams >= 1000 ? (t.Grams / 1000).ToString("0.00", Num.Dk) + " kg" : Math.Round(t.Grams).ToString("0", Num.Dk) + " g")
                + L.S(" filament", " filament"), Theme.Text);
            cardCustomers.Set(Num.Kr(t.CustomerIncome), prints(t.CustomerCount), Theme.Others);
            cardFriends.Set(Num.Kr(t.FriendsIncome), prints(t.FriendsCount), Theme.Friends);
            cardCosts.Set(Num.Kr(t.Costs + t.Lost),
                t.Lost > 0 ? L.F("incl. {0} failed", "inkl. {0} fejlprint", Num.Kr(t.Lost)) : L.S("material, power, wear", "materiale, strøm, slid"), Theme.Text);
            cardProfit.Set(Num.Kr(t.Profit),
                Math.Abs(t.Income) < 1e-9 ? L.S("no income yet", "ingen indtægt endnu")
                                          : L.F("{0} % of income", "{0} % af indtægten", Math.Round(t.ProfitPct).ToString("0", Num.Dk)),
                t.Profit < 0 ? Theme.Error : Theme.Success);
        }

        /// With 2+ printers and "All printers": the Profit card's dark tooltip lists each printer's profit for the month
        /// (all months = all time).
        void UpdateProfitTip(DateTime m)
        {
            if (!printerMode || SelectedPrinter != null) { modelTip.SetToolTip(cardProfit, null); return; }
            var lines = new List<string> { L.S("Profit by printer", "Overskud pr. printer") };
            foreach (var t in History.PrinterTotals(list, Settings, m == DateTime.MinValue ? 0 : m.Year, m.Month))
                lines.Add(AppSettings.CleanPrinterName(t.Name) + (t.Removed ? L.S(" (removed)", " (fjernet)") : "") + ":  " + Num.Kr(t.Profit));
            modelTip.SetToolTip(cardProfit, string.Join("\n", lines));
        }

        /// One card: muted caption, big bold value in its colour, muted second line; InfoBack fill, Border frame, the
        /// Profit card with the InfoAccent stripe (like an InfoBox).
        sealed class StatCard : Control
        {
            readonly string caption;
            readonly bool stripe;
            string value = "", sub = "";
            Color colour;
            readonly Font small = new Font("Segoe UI", 9f), big = new Font("Segoe UI", 14f, FontStyle.Bold);

            public StatCard(string caption, Color colour, bool stripe)
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                this.caption = caption;
                this.colour = colour;
                this.stripe = stripe;
                AccessibleRole = AccessibleRole.StaticText;
            }

            public void Set(string value, string sub, Color colour)
            {
                if (this.value == value && this.sub == sub && this.colour == colour) return;
                this.value = value; this.sub = sub; this.colour = colour;
                AccessibleName = caption + ": " + value + ", " + sub;
                Invalidate();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { small.Dispose(); big.Dispose(); }
                base.Dispose(disposing);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.InfoBack);
                float sc = DeviceDpi / 96f;
                Func<int, int> px = v => (int)Math.Round(v * sc);
                using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                if (stripe) using (var br = new SolidBrush(Theme.InfoAccent)) g.FillRectangle(br, 0, 0, px(4), Height);
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                int x = px(12), w = Width - x - px(8);
                TextRenderer.DrawText(g, caption, small, new Rectangle(x, px(8), w, px(18)), Theme.Muted, flags);
                TextRenderer.DrawText(g, value, big, new Rectangle(x, px(28), w, px(30)), colour, flags);
                TextRenderer.DrawText(g, sub, small, new Rectangle(x, Height - px(26), w, px(18)), Theme.Muted, flags);
            }
        }

        QuoteRecord SelectedRecord()
        {
            var row = grid.CurrentRow;
            return row == null || !row.Selected ? null : row.Tag as QuoteRecord;
        }

        void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var r = grid.Rows[e.RowIndex].Tag as QuoteRecord;
            if (r == null) return;
            var col = grid.Columns[e.ColumnIndex];
            bool friends = r.For == History.ForFriends;
            if (col == colPrice) e.CellStyle.ForeColor = friends ? Theme.Friends : Theme.Others;
            else if (col == colFor) e.CellStyle.ForeColor = friends ? Theme.Friends : Theme.Others;   // same colours as the prices
            else if (col == colInvoice) e.CellStyle.ForeColor = Theme.Muted;
        }

        /// Printed / Failed cells: a themed box (border colour, input background) with a green ✓ (Printed) or a red ✗
        /// (Failed) when ticked; the box frame is Text on the selected row (no pure white, style).
        void PaintTicks(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || (e.ColumnIndex != colFailed.Index && e.ColumnIndex != colPrinted.Index)) return;
            bool printed = e.ColumnIndex == colPrinted.Index;
            e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            bool on = e.Value is bool && (bool)e.Value;
            float sc = CurrentAutoScaleDimensions.Height / 96f;
            int size = (int)Math.Round(14 * sc);
            var box = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - size) / 2, e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size - 1, size - 1);
            using (var back = new SolidBrush(Theme.Input)) e.Graphics.FillRectangle(back, box);
            using (var pen = new Pen(selected ? Theme.Text : Theme.Muted)) e.Graphics.DrawRectangle(pen, box);
            if (on)
            {
                var old = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int m = Math.Max(3, (int)Math.Round(3f * sc));   // mark 7 px of the 13 px box at 96 dpi
                if (printed)
                    using (var pen = new Pen(Theme.Success, Math.Max(2f, 2f * sc)))
                        e.Graphics.DrawLines(pen, new[]
                        {
                            new PointF(box.Left + m, box.Top + box.Height / 2f), new PointF(box.Left + box.Width * 0.42f, box.Bottom - m),
                            new PointF(box.Right - m + 0.5f, box.Top + m)
                        });
                else
                    using (var pen = new Pen(Theme.Error, Math.Max(2f, 2f * sc)))
                    {
                        e.Graphics.DrawLine(pen, box.Left + m, box.Top + m, box.Right - m, box.Bottom - m);
                        e.Graphics.DrawLine(pen, box.Right - m, box.Top + m, box.Left + m, box.Bottom - m);
                    }
                e.Graphics.SmoothingMode = old;
            }
            e.Handled = true;
        }

        /// Ticks / unticks Printed for the row's quote and saves at once (the cards follow).
        void TogglePrinted(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            var row = grid.Rows[rowIndex];
            var r = row.Tag as QuoteRecord;
            if (r == null) return;
            r.Printed = !r.Printed;
            filling = true;
            row.Cells[colPrinted.Index].Value = r.Printed;
            filling = false;
            grid.InvalidateCell(colPrinted.Index, rowIndex);
            SaveList();
            UpdateCards();
        }

        /// Ticks / unticks Failed for the row's quote (History.MarkFailed) and saves at once.
        void ToggleFailed(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            var row = grid.Rows[rowIndex];
            var r = row.Tag as QuoteRecord;
            if (r == null) return;
            History.MarkFailed(r, !r.Failed, DateTime.Now);
            filling = true;
            row.Cells[colFailed.Index].Value = r.Failed;
            filling = false;
            grid.InvalidateCell(colFailed.Index, rowIndex);
            SaveList();
            UpdateCards();
        }

        void GridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (filling || e.RowIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            var r = row.Tag as QuoteRecord;
            if (r == null) return;
            var col = grid.Columns[e.ColumnIndex];
            if (col == colFor)
            {
                object v = row.Cells[e.ColumnIndex].Value;
                int f = v is int && (int)v == History.ForFriends ? History.ForFriends : History.ForCustomer;
                if (f == r.For) return;
                r.For = f;
                filling = true;
                row.Cells[colPrice.Index].Value = History.PriceFor(r).ToString("0.00", Num.Dk);   // the price follows For
                filling = false;
                grid.InvalidateRow(e.RowIndex);
                SaveList();
                UpdateCards();
            }
            else if (col == colNote)
            {
                object v = row.Cells[e.ColumnIndex].Value;
                string note = v == null ? "" : v.ToString().Trim();
                if (note == (r.Note ?? "")) return;
                r.Note = note;
                SaveList();
            }
        }

        /// Every change is written to history.json right away (never in picture mode).
        void SaveList()
        {
            if (offline) return;
            string err = History.Save(list);
            if (err == saveError) return;
            saveError = err;
            ShowInfo();
        }

        void ShowInfo()
        {
            string help = L.S("Quote… makes a quote page for the selected print, Invoice… a bill (it ticks Printed). Repeat print slices it " +
                              "with today's prices; Open again (or double-click) loads it as saved. Right-click for all of these.\n" +
                              "Click For, Note, Printed or Failed to change them, changes are saved at once. Backup… saves or restores the history and settings.",
                              "Tilbud… laver et tilbud på det valgte print, Faktura… en regning (den sætter Printet). Print igen slicer det " +
                              "med dagens priser; Åbn igen (eller dobbeltklik) henter det som gemt. Højreklik for det hele.\n" +
                              "Klik på Til, Note, Printet eller Fejlet for at ændre dem, ændringer gemmes med det samme. Sikkerhedskopi… gemmer eller gendanner historikken og indstillingerne.");
            if (saveError != null) { info.Text = saveError; info.Kind = InfoKind.Error; }
            else if (!string.IsNullOrEmpty(problem)) { info.Text = problem + "\n" + help; info.Kind = InfoKind.Warning; }
            else { info.Text = help; info.Kind = InfoKind.Info; }
        }

        /// Backup…: the Backup window (Restore tab first when the history is empty); after a restore the months,
        /// printers, cards and list are built again from the new list.
        void BackupClicked()
        {
            if (BackupRequested == null) return;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            if (!BackupRequested(this)) return;
            problem = null;
            saveError = null;
            refillingMonths = true;   // one Fill below, not one per change of the month list
            try
            {
                cmbMonth.Items.Clear();
                cmbMonth.Items.Add(new MonthItem { Month = DateTime.MinValue, Text = L.S("All months", "Alle måneder") });
                foreach (var m in History.Months(list)) cmbMonth.Items.Add(new MonthItem { Month = m, Text = MonthText(m) });
                cmbMonth.SelectedIndex = cmbMonth.Items.Count > 1 ? 1 : 0;
            }
            finally { refillingMonths = false; }
            SetupPrinters();
            FitLayout(); Fill(); ShowInfo(); UpdateMonthButtons();
        }

        void UpdateButtons()
        {
            bool any = SelectedRecord() != null;
            btnOpen.Enabled = any;
            btnDelete.Enabled = any;
            btnRepeat.Enabled = any;
            btnQuote.Enabled = any;
            btnInvoice.Enabled = any;
        }

        // ---------- actions ----------
        void OpenAgain()
        {
            var r = SelectedRecord();
            if (r == null) return;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            Selected = r;
            RepeatSelected = false;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// Closes with the quote to price again with today's prices (MainForm.Repeat slices it again).
        void RepeatPrint()
        {
            var r = SelectedRecord();
            if (r == null) return;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            Selected = r;
            RepeatSelected = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// Quote… / Invoice…: the quote window for the selected record over this window; afterwards the list shows the
        /// name / price / Printed / invoice no. it may have set.
        void ShowQuote(bool invoice)
        {
            var r = SelectedRecord();
            if (r == null || QuoteRequested == null) return;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            QuoteRequested(this, r, invoice);
            Fill();
            grid.Focus();
        }

        void ShowRowMenu(Point screen)
        {
            if (SelectedRecord() == null) return;
            rowMenu.Show(screen);
        }

        /// Two clicks: the first turns the button into a red "Really delete?" for 4 seconds, the second deletes.
        void DeleteClicked()
        {
            var r = SelectedRecord();
            if (r == null) return;
            if (!deleteArmed)
            {
                deleteArmed = true;
                btnDelete.Text = ConfirmText;
                Theme.AccentButton(btnDelete);
                if (deleteTimer == null)
                {
                    deleteTimer = new System.Windows.Forms.Timer { Interval = 4000 };
                    deleteTimer.Tick += delegate { DisarmDelete(); };
                }
                deleteTimer.Stop();
                deleteTimer.Start();
                return;
            }
            DisarmDelete();
            int index = grid.CurrentRow != null ? grid.CurrentRow.Index : 0;
            History.Remove(list, r.Id);
            SaveList();
            Fill();
            if (grid.Rows.Count > 0)
            {
                int i = Math.Min(index, grid.Rows.Count - 1);
                grid.CurrentCell = grid.Rows[i].Cells[0];
                grid.Rows[i].Selected = true;
            }
            UpdateButtons();
            grid.Focus();
        }

        void DisarmDelete()
        {
            if (!deleteArmed) return;
            deleteArmed = false;
            if (deleteTimer != null) deleteTimer.Stop();
            btnDelete.Text = L.S("Delete", "Slet");
            Theme.Apply(btnDelete);
        }

        // ---------- dark scroll bar ----------
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, string lp);

        /// The grid's own scroll bars in Windows' dark style (Windows 10 1809+; older: the normal light bar).
        static void DarkScrollBars(Control c)
        {
            foreach (Control child in c.Controls)
                if (child is ScrollBar)
                    try { SetWindowTheme(child.Handle, "DarkMode_Explorer", null); } catch { }
        }

        /// Enter (when not editing a cell) = Open again, instead of moving to the next row.
        sealed class HistoryGrid : DataGridView
        {
            public Action OpenRequested;
            public HistoryGrid() { DoubleBuffered = true; }
            protected override bool ProcessDialogKey(Keys keyData)
            {
                if (keyData == Keys.Enter && !IsCurrentCellInEditMode && OpenRequested != null) { OpenRequested(); return true; }
                return base.ProcessDialogKey(keyData);
            }
            protected override bool ProcessDataGridViewKey(KeyEventArgs e)
            {
                if (e.KeyData == Keys.Enter && !IsCurrentCellInEditMode && OpenRequested != null) { OpenRequested(); return true; }
                return base.ProcessDataGridViewKey(e);
            }
        }

        // ---------- picture mode ----------
        /// Made-up quotes for the history picture (never saved).
        internal static List<QuoteRecord> SampleQuotes()
        {
            var l = new List<QuoteRecord>();
            Action<string, string, string, int, int, double, double, double, int, string> add = (when, model, src, h, m, g, fr, cu, who, note) =>
                l.Add(new QuoteRecord
                {
                    Id = "sample" + l.Count, SavedAt = when, Model = model, Source = src, Hours = h, Minutes = m, Grams = g,
                    FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 3.65, PrinterWatts = 100, FriendsMarkupPct = 25, OthersMarkupPct = 75,
                    FriendsPrice = fr, OthersPrice = cu, For = who, Note = note, Printed = true,
                    // costs for the month cards: 200 kr./kg, 100 W at 3,65 kr./kWh, 1,71 kr./h wear
                    MaterialCost = Math.Round(g * 0.2, 2), PowerCost = Math.Round((h + m / 60.0) * 0.365, 2), MachineRatePerHour = 1.71
                });
            int F = History.ForFriends, C = History.ForCustomer;
            add("2026-11-03 14:12:00", "3D BENCHY", "https://www.printables.com/model/3161-3d-benchy", 0, 38, 11.8, 4.32, 5.62, F, L.S("Mads, red PLA", "Mads, rød PLA"));
            add("2026-11-02 19:40:00", "Four-Color Multicolor Test benchy", "https://www.printables.com/model/1648230-four-color-multicolor-test-benchy", 10, 43, 135.85, 57.18, 72.72, C, L.S("Webshop #112, paid", "Webshop #112, betalt"));
            add("2026-10-30 10:05:00", L.S("Phone stand (adjustable)", "Telefonholder (justerbar)"), @"C:\Models\phone_stand.3mf", 2, 5, 48.2, 16.56, 21.76, C, L.S("Lene, black", "Lene, sort"));
            add("2026-10-27 21:18:00", L.S("Cable clips ×20", "Kabelclips ×20"), @"C:\Models\cable_clip.stl", 1, 12, 22.4, 8.20, 10.66, F, L.S("Dad, garage", "Far, garagen"));
            add("2026-10-24 16:51:00", L.S("Planter pot 12 cm", "Urtepotte 12 cm"), @"C:\Models\planter.stl", 6, 30, 164, 55.08, 72.67, C, L.S("Café Bønne, 2 pcs", "Café Bønne, 2 stk."));
            add("2026-10-19 12:33:00", L.S("Headphone hook", "Hovedtelefonkrog"), @"C:\Models\headphone_hook.stl", 1, 48, 38.6, 13.55, 17.74, C, "");
            add("2026-10-12 20:02:00", L.S("Articulated dragon", "Bevægelig drage"), "https://www.printables.com/model/1234-articulated-dragon", 4, 55, 92.3, 33.73, 43.85, F, L.S("Emma's birthday", "Emmas fødselsdag"));
            add("2026-10-05 09:47:00", L.S("Lithophane lamp", "Litofanlampe"), @"C:\Models\lithophane.3mf", 9, 20, 120.5, 50.34, 64.10, C, L.S("Not paid yet", "Ikke betalt endnu"));
            // one failed print, so the picture shows a ticked Failed box
            l[5].Note = L.S("Layer shift at 80 %", "Lagforskydning ved 80 %");
            History.MarkFailed(l[5], true, new DateTime(2026, 10, 19, 15, 10, 0));
            // two billed customer prints and one quote not printed yet (Printed unticked)
            l[2].Customer = "Lene Hansen"; l[2].InvoiceNo = "2026-K7Q4"; l[2].BilledAt = "2026-10-31 09:12:00";
            l[4].Customer = "Café Bønne"; l[4].InvoiceNo = "2026-3HWN"; l[4].BilledAt = "2026-10-25 10:30:00";
            l[7].Printed = false;
            l[7].Note = L.S("Quote sent, waiting", "Tilbud sendt, venter");
            return l;
        }
    }
}
