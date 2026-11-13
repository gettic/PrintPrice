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
    public partial class HistoryForm : CaptionForm
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
        ToolStripMenuItem miPrinted, miFailed, miDelete, miOpen, miRepeat, miQuote, miInvoice, miFriends, miCustomer;
        bool filling, refillingMonths;
        string saveError;
        string listError;   // building the list failed (Load / after a restore): shown in the info box, details in the error window

        // ---------- idea 22: several selected, Clear all, undo, pictures, views ----------
        readonly Button btnClear;
        readonly DataGridViewTextBoxColumn colPic;   // the picture (drawn in GridCellPainting)
        readonly ViewSwitch viewSwitch;
        readonly TileView tiles;
        readonly ToolTip picTip;                     // hover picture (160 px) over the list's picture cell
        PictureCache pics;
        int view = ViewList;
        readonly List<QuoteRecord> shown = new List<QuoteRecord>();   // what the search / month / filters show, list order
        RemovedRecords undoDelete;                   // the last delete, until undone / dropped (pictures kept meanwhile)
        string undoClearCopy;                        // Clear all's safety copy, for Undo while the window is open
        List<QuoteRecord> undoClearMemory;           // picture mode: Clear all keeps the list in memory instead
        bool doneShown;                              // the info box shows a ✓ / error line, not the help
        int infoMinH;                                // the help's height (real px): the box never gets lower (no jumping)
        QuoteRecord picTipRecord;

        /// Sample pictures by record id (picture mode only; set before the window shows). The user's folder is never read then.
        internal Dictionary<string, byte[]> SamplePicturesForMode;
        /// Undo of Clear all restored the safety copy (History.Restore): MainForm takes over the records and settings
        /// (AdoptRestore). null = the window puts the records in its list itself.
        public Action<RestoreResult> Restored;

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
        int rowsShown = VisibleRows;   // fewer when the window must fit a small screen (FitToScreen)
        int widthCut;                  // px taken from Note / Model when the window must fit a narrow screen

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
            top = new TableLayoutPanel { ColumnCount = 12, AutoSize = true, Margin = new Padding(0, 0, 0, 8), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            for (int i = 0; i < 10; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
            // List / Medium / Large (idea 22): right edge on the list's right edge, the count 16 px before it
            viewSwitch = new ViewSwitch { Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(11, 3, 0, 3) };   // + the label's own padding = 16 px visible
            viewSwitch.Value = ViewList;
            viewSwitch.AccessibleName = L.S("View: ", "Visning: ") + ViewSwitch.ViewName(ViewList);
            viewSwitch.Changed += delegate { SetView(viewSwitch.Value); };
            top.Controls.Add(viewSwitch, 11, 0);
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
                MultiSelect = true, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, CellBorderStyle = DataGridViewCellBorderStyle.None,
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
            colPic = TextColumn("", false);   // the job's picture (idea 22), drawn by GridCellPainting
            grid.Columns.AddRange(new DataGridViewColumn[] { colPic, colDate, colModel, colTime, colGrams, colPrice, colFor, colPrinter, colNote, colPrinted, colFailed, colInvoice });

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
            // view switch: the hovered cell's name and shortcut ("Medium pictures (Ctrl+2)")
            viewSwitch.HotChanged += cell =>
            {
                if (cell < 0) { modelTip.Hide(viewSwitch); return; }
                modelTip.Show(ViewSwitch.ViewName(cell) + " (Ctrl+" + (cell + 1) + ")", viewSwitch, 0, viewSwitch.Height + Px(2), 4000);
            };
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
                if ((ModifierKeys & (Keys.Control | Keys.Shift)) != 0) return;   // Ctrl / Shift + click only selects
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
                if ((e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift)) && !grid.IsCurrentCellInEditMode && grid.CurrentRow != null
                    && SelectedRecords().Count > 0)
                {
                    e.Handled = true;
                    var rc = grid.GetRowDisplayRectangle(grid.CurrentRow.Index, false);
                    ShowRowMenu(grid.PointToScreen(new Point(rc.Left + rc.Width / 3, rc.Bottom)));
                }
            };
            // right-click a row: an unselected row becomes the only selected one; a selected row keeps the selection
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                if (grid.IsCurrentCellInEditMode) grid.EndEdit();
                if (!grid.Rows[e.RowIndex].Selected)
                {
                    grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(1, e.ColumnIndex)];
                    grid.ClearSelection();
                    grid.Rows[e.RowIndex].Selected = true;
                }
                ShowRowMenu(Cursor.Position);
            };
            grid.CellPainting += PaintTicks;
            grid.CellPainting += PaintPicture;
            grid.SelectionChanged += delegate { if (!filling) { UpdateButtons(); UpdateCount(); } };
            // Ctrl + mouse wheel: bigger / smaller view (like Explorer)
            grid.MouseWheel += (s, e) =>
            {
                if ((ModifierKeys & Keys.Control) == 0) return;
                var he = e as HandledMouseEventArgs;
                if (he != null) he.Handled = true;
                StepView(e.Delta > 0 ? 1 : -1);
            };
            // the picture at 160 px in a dark tooltip while the mouse is over a row's picture
            picTip = new ToolTip { OwnerDraw = true, InitialDelay = 300, ReshowDelay = 100 };
            picTip.Popup += (s, e) => { int side = Px(160) + 2 * Px(6) + 2; e.ToolTipSize = new Size(side, side); };
            picTip.Draw += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.InfoBack)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                var well = new Rectangle(Px(6) + 1, Px(6) + 1, Px(160), Px(160));
                var img = picTipRecord != null && pics != null ? pics.Get(picTipRecord, WellInner(well, false, Scale96).Width) : null;
                DrawWell(e.Graphics, well, img, false, Scale96);
            };
            grid.CellMouseEnter += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != colPic.Index) return;
                var r = grid.Rows[e.RowIndex].Tag as QuoteRecord;
                if (r == null || pics == null || !pics.Has(r)) return;
                picTipRecord = r;
                var rc = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                picTip.Show(" ", grid, rc.Right + Px(4), rc.Top, 10000);
            };
            grid.CellMouseLeave += (s, e) =>
            {
                if (e.ColumnIndex != colPic.Index || picTipRecord == null) return;
                picTipRecord = null;
                picTip.Hide(grid);
            };
            FormClosed += delegate { picTip.Dispose(); };
            grid.HandleCreated += delegate { DarkScrollBars(grid); };

            // 1 px border around the list in the theme's border colour
            gridFrame = new Panel { Padding = new Padding(1), BackColor = Theme.Border, Margin = new Padding(0, 0, 0, 10), Size = new Size(900, 280) };
            gridFrame.Controls.Add(grid);
            // Medium / Large: the picture cards in the same frame (the window keeps its size in every view)
            tiles = new TileView { Dock = DockStyle.Fill, Visible = false };
            tiles.SelectionChanged += delegate { UpdateButtons(); UpdateCount(); };
            tiles.OpenRequested += OpenAgain;
            tiles.DeleteRequested += DeleteClicked;
            tiles.MenuRequested += p => ShowRowMenu(p);
            tiles.ViewWheel += StepView;
            // a cut card name / line: the whole text in the dark tooltip (like the grid's cut cells)
            tiles.TipChanged += (text, p) =>
            {
                if (text == null) modelTip.Hide(tiles);
                else modelTip.Show(text, tiles, p.X, p.Y, 8000);
            };
            gridFrame.Controls.Add(tiles);
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
            miOpen = Item(L.S("Open again", "Åbn igen"), OpenAgain);
            miOpen.Font = new Font(rowMenu.Font, FontStyle.Bold);
            rowMenu.Items.Add(miOpen);
            miRepeat = Item(L.S("Repeat print", "Print igen"), RepeatPrint);
            rowMenu.Items.Add(miRepeat);
            rowMenu.Items.Add(new ToolStripSeparator());
            miQuote = Item(L.S("Quote…", "Tilbud…"), () => ShowQuote(false));
            miInvoice = Item(L.S("Invoice…", "Faktura…"), () => ShowQuote(true));
            rowMenu.Items.Add(miQuote);
            rowMenu.Items.Add(miInvoice);
            rowMenu.Items.Add(new ToolStripSeparator());
            // For / Printed / Failed act on every selected quote (round dot = all of them)
            miFriends = Item(L.S("For friends && family", "Til venner og familie"), () => SetFor(History.ForFriends));
            miCustomer = Item(L.S("For a customer", "Til en kunde"), () => SetFor(History.ForCustomer));
            rowMenu.Items.Add(miFriends);
            rowMenu.Items.Add(miCustomer);
            rowMenu.Items.Add(new ToolStripSeparator());
            miPrinted = Item(L.S("Printed", "Printet"), () => SetMarks(true));
            miFailed = Item(L.S("Failed", "Fejlet"), () => SetMarks(false));
            rowMenu.Items.Add(miPrinted);
            rowMenu.Items.Add(miFailed);
            rowMenu.Items.Add(new ToolStripSeparator());
            miDelete = Item(L.S("Delete…", "Slet…"), DeleteClicked);
            rowMenu.Items.Add(miDelete);
            rowMenu.Opening += delegate
            {
                var sel = SelectedRecords();
                bool one = sel.Count == 1;
                bool allPrinted = sel.Count > 0, allFailed = sel.Count > 0, allFriends = sel.Count > 0, allCustomers = sel.Count > 0;
                foreach (var r in sel)
                {
                    allPrinted &= r.Printed; allFailed &= r.Failed;
                    allFriends &= r.For == History.ForFriends; allCustomers &= r.For != History.ForFriends;
                }
                miPrinted.Checked = allPrinted;
                miFailed.Checked = allFailed;
                miFriends.Checked = allFriends;
                miCustomer.Checked = allCustomers;
                miOpen.Enabled = miRepeat.Enabled = miQuote.Enabled = miInvoice.Enabled = one;
                miQuote.Visible = miInvoice.Visible = QuoteRequested != null;
                miDelete.Text = one ? L.S("Delete…", "Slet…") : L.F("Delete {0} quotes…", "Slet {0} tilbud…", sel.Count);
            };
            FormClosed += delegate { rowMenu.Dispose(); };
            // left: Delete + Backup… (6 px apart)
            var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            btnBackup = MakeButton(L.S("Backup…", "Sikkerhedskopi…"), 0);
            btnBackup.Margin = new Padding(6, 0, 0, 0);
            btnBackup.Click += delegate { BackupClicked(); };
            // Clear all… (idea 22): every quote, whatever the filters show; asks first, safety copy, Undo while open
            btnClear = MakeButton(L.S("Clear all…", "Ryd alt…"), 0);
            btnClear.Margin = new Padding(6, 0, 0, 0);
            btnClear.Click += delegate { ClearAllClicked(); };
            left.Controls.Add(btnDelete);
            left.Controls.Add(btnClear);
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
                pics = new PictureCache(this, offline ? (SamplePicturesForMode ?? new Dictionary<string, byte[]>()) : null);
                pics.Loaded += PictureLoaded;
                tiles.Pictures = pics;
                int v = Settings != null ? (int)Settings.HistoryView : ViewList;
                view = v == ViewMedium || v == ViewLarge ? v : ViewList;
                viewSwitch.Value = view;
                ShowViewControl();
                BuildList(false);
                FitToScreen();
            };
            // the undo ends with the window: deleted records' pictures go now
            FormClosed += delegate
            {
                DropUndo();
                if (pics != null) pics.Dispose();
            };
            ActiveControl = grid;
        }

        /// Load (after BuildList): a window wider or taller than the screen's work area (e.g. 150 % with 2+ printers) gets
        /// narrower Note / Model columns (up to 45 + 40 px) and fewer list rows (at least 5), then is kept on that screen.
        void FitToScreen()
        {
            if (Screenshots.Active || Program.Hidden) return;   // off-screen picture runs keep the 100 % layout
            Rectangle wa;
            try { wa = (Owner != null ? Screen.FromControl(Owner) : Screen.FromPoint(Cursor.Position)).WorkingArea; }
            catch { return; }
            PerformLayout();
            int overW = Width - wa.Width, overH = Height - wa.Height;
            if (overW <= 0 && overH <= 0) return;
            if (overW > 0) widthCut = Math.Min(Px(85), overW);
            if (overH > 0)
            {
                int rowH = Math.Max(1, grid.RowTemplate.Height);
                rowsShown = Math.Max(5, VisibleRows - (overH + rowH - 1) / rowH);
            }
            FitLayout();
            KeepInfoHeight(!doneShown);
            PerformLayout();
            Shown += delegate
            {
                var b = Bounds;
                int x = Math.Max(wa.Left, Math.Min(b.Left, wa.Right - b.Width));
                int y = Math.Max(wa.Top, Math.Min(b.Top, wa.Bottom - b.Height));
                if (x != b.Left || y != b.Top) Location = new Point(x, y);
            };
        }

        float Scale96 { get { return DeviceDpi / 96f; } }
        int Px(int v) { return (int)Math.Round(v * Scale96); }

        /// Picture mode: shows that view (List / Medium / Large) without saving it.
        internal void SampleView(int v)
        {
            viewSwitch.Value = v;
            SetView(v);
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
            colPic.Width = px(44);   // a 30 px picture well, 7 px each side
            colDate.Width = width(colDate, "00-00-0000 00:00", 130);
            colModel.Width = px(172) - Math.Max(0, widthCut - px(45));   // "Phone stand (adjustable)" (160) + cell padding 12 fits; longer names get the dark tooltip (ModelTip)
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

            viewSwitch.Size = new Size(px(94), txtSearch.Height);
            // rows 34 high for the 30 px picture (2 px above and below); the header keeps its old height
            int textRowH = TextRenderer.MeasureText("Ag", grid.Font).Height + px(8);
            int rowH = Math.Max(textRowH, px(34));
            grid.RowTemplate.Height = rowH;
            grid.ColumnHeadersHeight = textRowH + px(4);
            int cols = colPic.Width + colDate.Width + colModel.Width + colTime.Width + colGrams.Width + colPrice.Width + colFor.Width
                     + colPrinted.Width + colFailed.Width + colInvoice.Width + px(155) - Math.Min(widthCut, px(45))    // Note gets the rest (~155 px + no-scroll-bar room; +40 from Model / Invoice)
                     + (printerMode ? colPrinter.Width : 0);                              // 2+ printers: the window is wider by the Printer column
            gridFrame.Size = new Size(cols + SystemInformation.VerticalScrollBarWidth + 2,
                                      grid.ColumnHeadersHeight + rowsShown * rowH + 2);
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
            top.MinimumSize = new Size(gridFrame.Width, 0);      // the view switch ends at the list's right edge
            bottom.MinimumSize = new Size(gridFrame.Width, 0);   // Close ends there too

            // the top row must fit the list's width with the widest count text ("120 of 340 quotes" / "3 of 12 selected"):
            // with 2+ printers the search box gives way (220 -> at least 170)
            txtSearch.Width = px(220);
            int n = Math.Max(10, list.Count);
            int countW = Math.Max(TextRenderer.MeasureText(L.F("{0} of {1} quotes", "{0} af {1} tilbud", n, n), lblCount.Font).Width,
                                  TextRenderer.MeasureText(L.F("{0} of {1} selected", "{0} af {1} valgt", n, n), lblCount.Font).Width);
            int topNeed = countW + lblCount.Margin.Horizontal;
            foreach (Control c in top.Controls)
                if (c != lblCount && (printerMode || (c != lblPrinterCap && c != cmbPrinter))) topNeed += c.Width + c.Margin.Horizontal;
            if (topNeed > gridFrame.Width) txtSearch.Width = Math.Max(px(170), txtSearch.Width - (topNeed - gridFrame.Width));
        }

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

        /// Rebuilds the shown quotes from the list (search + filters) in the active view, keeping the selected quotes
        /// (and the current one) that are still shown.
        void Fill()
        {
            Fill(null, null);
        }

        void Fill(ICollection<QuoteRecord> keep, QuoteRecord keepCurrent)
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            if (keep == null) { keep = new HashSet<QuoteRecord>(SelectedRecords()); keepCurrent = CurrentRecord(); }
            shown.Clear();
            foreach (var r in list) if (Matches(r)) shown.Add(r);
            if (view == ViewList) FillGrid(keep, keepCurrent);
            else
            {
                if (grid.Rows.Count > 0) { filling = true; grid.Rows.Clear(); filling = false; }   // rebuilt when the list shows again
                tiles.SetItems(shown, keep, keepCurrent);
            }
            UpdateCount();
            UpdateButtons();
            UpdateCards();
        }

        void FillGrid(ICollection<QuoteRecord> keep, QuoteRecord keepCurrent)
        {
            filling = true;
            try
            {
                grid.SuspendLayout();
                grid.Rows.Clear();
                var keepSet = keep as HashSet<QuoteRecord> ?? new HashSet<QuoteRecord>(keep);
                int cur = -1;
                var others = new List<int>();
                foreach (var r in shown)
                {
                    int i = grid.Rows.Add("", DateText(r), ModelText(r), TimeText(r),
                                          Num.Fmt(r.Grams), History.PriceFor(r).ToString("0.00", Num.Dk),
                                          r.For == History.ForFriends ? History.ForFriends : History.ForCustomer,
                                          printerMode ? PrinterText(r) : "", r.Note ?? "", r.Printed, r.Failed, r.InvoiceNo ?? "");
                    grid.Rows[i].Tag = r;
                    grid.Rows[i].Height = grid.RowTemplate.Height;
                    if (r == keepCurrent) cur = i;
                    else if (keepSet.Contains(r)) others.Add(i);
                }
                if (cur < 0 && others.Count > 0) { cur = others[0]; others.RemoveAt(0); }
                if (cur < 0 && grid.Rows.Count > 0) cur = 0;
                if (cur >= 0)
                {
                    grid.CurrentCell = grid.Rows[cur].Cells[1];   // selects only this row
                    grid.ClearSelection();
                    if (keepSet.Contains(shown[cur]) || others.Count == 0) grid.Rows[cur].Selected = true;
                    foreach (int i in others) grid.Rows[i].Selected = true;
                }
                grid.ResumeLayout();
            }
            finally { filling = false; }
        }

        /// "6 of 8 quotes" (0–1 selected) or "3 of 12 selected" (of the shown ones, 2+ selected).
        void UpdateCount()
        {
            int n = SelectedCount();
            lblCount.Text = n >= 2 ? L.F("{0} of {1} selected", "{0} af {1} valgt", n, shown.Count)
                          : shown.Count == list.Count ? L.F(list.Count == 1 ? "{0} quote" : "{0} quotes", "{0} tilbud", list.Count)
                          : L.F("{0} of {1} quotes", "{0} af {1} tilbud", shown.Count, list.Count);
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
                t.FailedPrints > 0 ? L.F("+ {0} failed", t.FailedPrints == 1 ? "+ {0} fejlet" : "+ {0} fejlede", t.FailedPrints) : L.S("no failed prints", "ingen fejlprint"), Theme.Text);
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

        /// The one selected quote (null when none or several are selected): Open again, Repeat print, Quote…, Invoice….
        QuoteRecord SelectedRecord()
        {
            var sel = SelectedRecords();
            return sel.Count == 1 ? sel[0] : null;
        }

        /// The selected quotes in list order (list view: the grid's selected rows; Medium / Large: the cards).
        List<QuoteRecord> SelectedRecords()
        {
            var res = new List<QuoteRecord>();
            if (view != ViewList) return tiles.SelectedRecords();
            var rows = grid.SelectedRows;
            if (rows.Count == 0) return res;
            var idx = new List<int>(rows.Count);
            foreach (DataGridViewRow row in rows) idx.Add(row.Index);
            idx.Sort();
            foreach (int i in idx) { var r = grid.Rows[i].Tag as QuoteRecord; if (r != null) res.Add(r); }
            return res;
        }

        int SelectedCount() { return view == ViewList ? grid.SelectedRows.Count : tiles.SelectedCount; }

        /// The quote with the keyboard focus (list: the current row; cards: the current card).
        QuoteRecord CurrentRecord()
        {
            if (view != ViewList) return tiles.Current;
            return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as QuoteRecord;
        }

        /// The list's picture cell: a 30 px Back well with a Border frame, the picture whole inside (5 % margin), or the
        /// muted cube when the quote has none. Pictures load in the background (PictureLoaded repaints the row).
        void PaintPicture(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != colPic.Index) return;
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            e.PaintBackground(e.CellBounds, selected);
            int side = Px(30);
            var well = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - side) / 2, e.CellBounds.Y + (e.CellBounds.Height - side) / 2, side, side);
            var r = grid.Rows[e.RowIndex].Tag as QuoteRecord;
            Image img = r != null && pics != null ? pics.Get(r, WellInner(well, true, Scale96).Width) : null;
            DrawWell(e.Graphics, well, img, true, Scale96);
            e.Handled = true;
        }

        /// A picture finished loading: repaint where it shows (row / cards / the hover tooltip).
        void PictureLoaded(string id)
        {
            if (IsDisposed) return;
            if (view == ViewList)
            {
                int first = grid.FirstDisplayedScrollingRowIndex;
                if (first >= 0)
                    for (int i = first; i < grid.Rows.Count && i < first + grid.DisplayedRowCount(true) + 1; i++)
                    {
                        var r = grid.Rows[i].Tag as QuoteRecord;
                        if (r != null && r.Id == id) grid.InvalidateCell(colPic.Index, i);
                    }
            }
            else tiles.Invalidate();
            if (picTipRecord != null && picTipRecord.Id == id && picTip.Active)
            {
                var row = -1;
                for (int i = 0; i < grid.Rows.Count; i++) if (grid.Rows[i].Tag == picTipRecord) { row = i; break; }
                if (row >= 0)
                {
                    var rc = grid.GetCellDisplayRectangle(colPic.Index, row, false);
                    picTip.Show(" ", grid, rc.Right + Px(4), rc.Top, 10000);
                }
            }
        }

        // ---------- views (idea 22) ----------
        /// List / Medium / Large: the same quotes, filters and selection; remembered in the settings.
        void SetView(int v)
        {
            v = v == ViewMedium || v == ViewLarge ? v : ViewList;
            if (v == view) return;
            bool hadFocus = grid.ContainsFocus || tiles.Focused;
            var keep = new HashSet<QuoteRecord>(SelectedRecords());
            var cur = CurrentRecord();
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            view = v;
            if (viewSwitch.Value != v) viewSwitch.Value = v;
            ShowViewControl();
            Fill(keep, cur);
            if (hadFocus) { if (view == ViewList) grid.Focus(); else tiles.Focus(); }
            if (!doneShown) ShowInfo();
            if (Settings != null && !Screenshots.Active)
            {
                Settings.HistoryView = v;
                if (!offline) Settings.Save();
            }
        }

        void ShowViewControl()
        {
            if (view == ViewList) { tiles.Visible = false; grid.Visible = true; }
            else { tiles.Mode = view; tiles.Visible = true; grid.Visible = false; }
            if (ActiveControl == grid || ActiveControl == tiles || ActiveControl == null) ActiveControl = view == ViewList ? (Control)grid : tiles;
        }

        /// Ctrl + wheel: +1 = bigger pictures, -1 = smaller (stops at List / Large).
        void StepView(int step)
        {
            int v = Math.Max(ViewList, Math.Min(ViewLarge, view + step));
            if (v != view) SetView(v);
        }

        /// Ctrl+1 / 2 / 3 switch the view from anywhere in the window.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.D1) || keyData == (Keys.Control | Keys.NumPad1)) { SetView(ViewList); return true; }
            if (keyData == (Keys.Control | Keys.D2) || keyData == (Keys.Control | Keys.NumPad2)) { SetView(ViewMedium); return true; }
            if (keyData == (Keys.Control | Keys.D3) || keyData == (Keys.Control | Keys.NumPad3)) { SetView(ViewLarge); return true; }
            // Esc in the search box with text: clears the search first (the next Esc closes the window)
            if (keyData == Keys.Escape && txtSearch.Focused && txtSearch.TextLength > 0) { txtSearch.Clear(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
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

        /// The help for the view (or the load problem / a save error). Resets a ✓ / error line from an action.
        void ShowInfo()
        {
            doneShown = false;
            string help = view == ViewList
                ? L.S("Ctrl+click or Shift+click selects several quotes (Ctrl+A all), Delete deletes them. Quote… makes a quote page, Invoice… a bill " +
                      "(it ticks Printed). Repeat print slices it with today's prices; Open again (or double-click) loads it as saved. Right-click for all of these.\n" +
                      "Click For, Note, Printed or Failed to change them, changes are saved at once. Ctrl+1, 2, 3 or Ctrl+wheel: list, medium or large pictures.",
                      "Ctrl+klik eller Shift+klik vælger flere tilbud (Ctrl+A alle), Slet sletter dem. Tilbud… laver et tilbud, Faktura… en regning " +
                      "(den sætter Printet). Print igen slicer det med dagens priser; Åbn igen (eller dobbeltklik) henter det som gemt. Højreklik for det hele.\n" +
                      "Klik på Til, Note, Printet eller Fejlet for at ændre dem, ændringer gemmes med det samme. Ctrl+1, 2, 3 eller Ctrl+hjul: liste, mellemstore eller store billeder.")
                : L.S("Ctrl+click or Shift+click selects several quotes (Ctrl+A all), Delete deletes them. Double-click or Enter opens a quote again; " +
                      "right-click for more.\n" +
                      "Notes are changed in the list (Ctrl+1). Ctrl+1, 2, 3 or Ctrl+wheel: list, medium or large pictures.",
                      "Ctrl+klik eller Shift+klik vælger flere tilbud (Ctrl+A alle), Slet sletter dem. Dobbeltklik eller Enter åbner et tilbud igen; " +
                      "højreklik for mere.\n" +
                      "Noter ændres i listen (Ctrl+1). Ctrl+1, 2, 3 eller Ctrl+hjul: liste, mellemstore eller store billeder.");
            if (saveError != null) { info.Text = saveError; info.Kind = InfoKind.Error; }
            else if (listError != null) { info.Text = listError; info.Kind = InfoKind.Error; }
            else if (!string.IsNullOrEmpty(problem)) { info.Text = problem + "\n" + help; info.Kind = InfoKind.Warning; }
            else { info.Text = help; info.Kind = InfoKind.Info; }
            KeepInfoHeight(true);
        }

        /// A ✓ / error line after an action (delete, Clear all, undo), with optional links ("Undo", "Open folder").
        void ShowDone(string text, InfoKind kind, string[] links, Action[] clicks)
        {
            doneShown = true;
            info.Text = text;
            info.Kind = kind;
            if (links != null) info.SetLinks(links, clicks);
            KeepInfoHeight(false);
        }

        /// The info box never gets lower than the help was (measured), so the window doesn't jump for a short ✓ line.
        void KeepInfoHeight(bool measureHelp)
        {
            int w = info.MaximumSize.Width;
            if (measureHelp)
            {
                info.MinimumSize = new Size(w, 0);
                info.PerformLayout();
                infoMinH = Math.Max(infoMinH, info.GetPreferredSize(new Size(w, 0)).Height);
            }
            info.MinimumSize = new Size(w, infoMinH);
        }

        /// Backup…: the Backup window (Restore tab first when the history is empty); after a restore the months,
        /// printers, cards and list are built again from the new list.
        void BackupClicked()
        {
            if (BackupRequested == null) return;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            DropUndo(true);   // a restore and an older undo don't mix
            if (!BackupRequested(this)) return;
            problem = null;
            saveError = null;
            pics.Reload();
            Rebuild(true, null, null);
        }

        /// The month list again from the records. keep = the month to keep chosen when it still has quotes (else All
        /// months); null = the newest month (after a restore).
        void RefillMonths(DateTime? keep)
        {
            refillingMonths = true;   // one Fill afterwards, not one per change of the month list
            try
            {
                cmbMonth.Items.Clear();
                cmbMonth.Items.Add(new MonthItem { Month = DateTime.MinValue, Text = L.S("All months", "Alle måneder") });
                foreach (var m in History.Months(list)) cmbMonth.Items.Add(new MonthItem { Month = m, Text = MonthText(m) });
                int index = keep == null ? (cmbMonth.Items.Count > 1 ? 1 : 0) : 0;
                if (keep != null)
                    for (int i = 0; i < cmbMonth.Items.Count; i++)
                        if (((MonthItem)cmbMonth.Items[i]).Month == keep.Value) { index = i; break; }
                cmbMonth.SelectedIndex = index;
            }
            finally { refillingMonths = false; }
        }

        /// After a delete, Clear all, undo or restore: months, printers, layout, list, cards and info built again.
        /// afterRestore = newest month and all printers (a new list); else the chosen month / printer stay when they can.
        void Rebuild(bool afterRestore, ICollection<QuoteRecord> keep, QuoteRecord current)
        {
            string printer = SelectedPrinter;
            RefillMonths(afterRestore ? (DateTime?)null : SelectedMonth);
            BuildList(afterRestore, keep, current, afterRestore ? null : printer);
        }

        /// Printers, layout, list, info and ‹ › (Load, and again after a restore). An unexpected error (e.g. odd old data)
        /// doesn't leave a half-built window: the info box says so, the error window (Copy details, errors.log) follows.
        void BuildList(bool afterRestore)
        {
            BuildList(afterRestore, null, null, null);
        }

        void BuildList(bool afterRestore, ICollection<QuoteRecord> keep, QuoteRecord current, string printer)
        {
            listError = null;
            try
            {
                SetupPrinters();
                if (printer != null && printerMode)
                {
                    filling = true;
                    for (int i = 0; i < cmbPrinter.Items.Count; i++)
                        if (((PrinterChoice)cmbPrinter.Items[i]).Id == printer) { cmbPrinter.SelectedIndex = i; break; }
                    filling = false;
                }
                FitLayout();
                Fill(keep ?? new HashSet<QuoteRecord>(), current);
                UpdateMonthButtons();
            }
            catch (Exception ex)
            {
                filling = false;
                try { grid.ResumeLayout(); } catch { }   // Fill may have stopped between Suspend and Resume
                listError = afterRestore
                    ? L.S("The backup was restored, but the list could not be shown. Close this window and open History again.",
                          "Kopien er gendannet, men listen kunne ikke vises. Luk vinduet, og åbn Historik igen.")
                    : L.S("Part of the price history could not be shown. Your saved quotes are not changed.",
                          "En del af prishistorikken kunne ikke vises. Dine gemte tilbud er ikke ændret.");
                BeginInvoke((MethodInvoker)delegate { ErrorReport.Show(ex); });   // after the window shows, over it
            }
            ShowInfo();
        }

        /// One quote selected: Open again, Repeat print, Quote…, Invoice…; one or more: Delete; any quotes: Clear all….
        void UpdateButtons()
        {
            int n = SelectedCount();
            bool one = n == 1;
            btnOpen.Enabled = one;
            btnRepeat.Enabled = one;
            btnQuote.Enabled = one;
            btnInvoice.Enabled = one;
            btnDelete.Enabled = n > 0;
            btnClear.Enabled = list.Count > 0;
        }

        void FocusList()
        {
            if (view == ViewList) grid.Focus(); else tiles.Focus();
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
            FocusList();
        }

        void ShowRowMenu(Point screen)
        {
            if (SelectedCount() == 0) return;
            rowMenu.Show(screen);
        }

        /// Row menu Printed / Failed for every selected quote: ticked on all of them -> off, else on for all. Saved at once.
        void SetMarks(bool printed)
        {
            var sel = SelectedRecords();
            if (sel.Count == 0) return;
            bool all = true;
            foreach (var r in sel) all &= printed ? r.Printed : r.Failed;
            var now = DateTime.Now;
            foreach (var r in sel)
            {
                if (printed) r.Printed = !all;
                else if (r.Failed == all) History.MarkFailed(r, !all, now);
            }
            SaveList();
            Fill();
        }

        /// Row menu For: friends / customer for every selected quote (the price shown follows). Saved at once.
        void SetFor(int forWho)
        {
            var sel = SelectedRecords();
            if (sel.Count == 0) return;
            foreach (var r in sel) r.For = forWho;
            SaveList();
            Fill();
        }

        /// kr. amount with a no-break space ("11,46 kr." never splits over two lines).
        static string KrNb(double v) { return Num.Kr(v).Replace(' ', '\u00A0'); }

        static List<string> IdsOf(List<QuoteRecord> records)
        {
            var ids = new List<string>();
            foreach (var r in records) if (r != null && !string.IsNullOrEmpty(r.Id)) ids.Add(r.Id);
            return ids;
        }

        /// Delete (button, Delete key, row menu): every selected quote, after a dark yes / no question (Cancel = default).
        void DeleteClicked()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            var sel = SelectedRecords();
            if (sel.Count == 0) return;
            using (var ask = NewDeleteAsk(sel))
                if (ask.ShowDialog(this) != DialogResult.Yes) { FocusList(); return; }
            DoDelete(sel);
        }

        /// The question before a delete: the name (one quote) or the count, what goes, the undo, and what it means for
        /// failed prints (not earned back yet = no longer earned back; a quote that earned some back = owed again).
        internal BackupForm.AskForm NewDeleteAsk(List<QuoteRecord> sel)
        {
            var sum = History.SummarizeDelete(list, IdsOf(sel));
            bool one = sel.Count == 1;
            string name = one ? ModelText(sel[0]) : "";
            if (name.Length > 80) name = name.Substring(0, 79) + "…";
            string q = one ? L.F("Delete \"{0}\"?", "Slet \"{0}\"?", name) : L.F("Delete {0} quotes?", "Slet {0} tilbud?", sel.Count);
            var lines = new List<string>
            {
                one ? L.S("The quote and its picture are deleted. Undo brings it back while this window is open.",
                          "Tilbuddet og dets billede slettes. Fortryd henter det tilbage, mens vinduet er åbent.")
                    : L.S("The selected quotes and their pictures are deleted. Undo brings them back while this window is open.",
                          "De valgte tilbud og deres billeder slettes. Fortryd henter dem tilbage, mens vinduet er åbent.")
            };
            if (sum.FailedOpen > 0)
                lines.Add(one ? L.F("It is a failed print not earned back yet ({0}): new quotes stop earning it back.",
                                    "Det er et fejlet print, der ikke er tjent ind endnu ({0}): nye tilbud holder op med at tjene det ind.",
                                    KrNb(sum.FailedOpenAmount))
                        : sum.FailedOpen == 1
                              ? L.F("1 of them is a failed print not earned back yet ({0}): new quotes stop earning it back.",
                                    "1 af dem er et fejlet print, der ikke er tjent ind endnu ({0}): nye tilbud holder op med at tjene det ind.",
                                    KrNb(sum.FailedOpenAmount))
                              : L.F("{0} of them are failed prints not earned back yet ({1}): new quotes stop earning that back.",
                                    "{0} af dem er fejlede print, der ikke er tjent ind endnu ({1}): nye tilbud holder op med at tjene det ind.",
                                    sum.FailedOpen, KrNb(sum.FailedOpenAmount)));
            if (sum.PayingBack > 0)
                lines.Add(one ? L.S("It earned back part of a failed print: new quotes earn that back again.",
                                    "Det tjente en del af et fejlet print ind: nye tilbud tjener det ind igen.")
                              : L.F("{0} of them earned back failed prints: new quotes earn that back again.",
                                    "{0} af dem tjente fejlede print ind: nye tilbud tjener det ind igen.", sum.PayingBack));
            return new BackupForm.AskForm(L.S("Delete", "Slet"), q, string.Join("\n", lines), L.S("Delete", "Slet"), L.S("Cancel", "Annullér"));
        }

        /// Deletes (History.DeleteRecords: saved at once, pictures kept for Undo), then the ✓ line with "Undo"; the
        /// quote after the deleted ones (else before) gets the selection.
        internal void DoDelete(List<QuoteRecord> sel)
        {
            var ids = IdsOf(sel);
            if (ids.Count == 0) return;
            var gone = new HashSet<QuoteRecord>(sel);
            int pos = shown.IndexOf(sel[0]);
            QuoteRecord next = null;
            for (int i = Math.Max(0, pos); i < shown.Count && next == null; i++) if (!gone.Contains(shown[i])) next = shown[i];
            for (int i = Math.Min(pos, shown.Count - 1); i >= 0 && next == null; i--) if (!gone.Contains(shown[i])) next = shown[i];
            DropUndo(false);   // one undo at a time: the older one's pictures go now
            RemovedRecords removed;
            string err = null;
            if (offline) removed = History.RemoveMany(list, ids);   // picture mode: memory only
            else err = History.DeleteRecords(list, ids, out removed);
            if (err != null) { ShowDone(err, InfoKind.Error, null, null); return; }
            if (removed.Count == 0) return;
            undoDelete = removed;
            Rebuild(false, next == null ? null : new List<QuoteRecord> { next }, next);
            string text = removed.Count == 1
                ? L.F("✓ \"{0}\" deleted.", "✓ \"{0}\" slettet.", ModelText(removed.Records[0]))
                : L.F("✓ {0} quotes deleted.", "✓ {0} tilbud slettet.", removed.Count);
            ShowDone(text, InfoKind.Success, new[] { L.S("Undo", "Fortryd") }, new Action[] { UndoClicked });
            FocusList();
        }

        /// Clear all…: every quote (whatever the filters show) after the question; History.ClearAll saves a safety copy
        /// with the pictures first. The ✓ line has "Undo" (restores the copy) and "Open folder" (shows the copy).
        void ClearAllClicked()
        {
            if (list.Count == 0) return;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            using (var ask = NewClearAsk())
                if (ask.ShowDialog(this) != DialogResult.Yes) { FocusList(); return; }
            int n = list.Count;
            DropUndo(false);   // ClearAll removes every picture: the older undo can't come back anyway
            string path = null;
            if (offline) { undoClearMemory = new List<QuoteRecord>(list); list.Clear(); }
            else
            {
                string err = History.ClearAll(list, Updater.CurrentText, DateTime.Now, out path);
                if (err != null) { ShowDone(err, InfoKind.Error, null, null); return; }
                if (path == null) return;
                undoClearCopy = path;
            }
            pics.Clear();
            Rebuild(false, null, null);
            string copy = path;
            ShowDone(n == 1 ? L.S("✓ The price history is cleared: 1 quote deleted. A safety copy was saved first.",
                                  "✓ Prishistorikken er ryddet: 1 tilbud slettet. Der blev gemt en sikkerhedskopi først.")
                            : L.F("✓ The price history is cleared: {0} quotes deleted. A safety copy was saved first.",
                                  "✓ Prishistorikken er ryddet: {0} tilbud slettet. Der blev gemt en sikkerhedskopi først.", n),
                     InfoKind.Success, new[] { L.S("Undo", "Fortryd"), L.S("Open folder", "Åbn mappe") },
                     new Action[] { UndoClicked, () => ShowInFolder(copy) });
            FocusList();
        }

        internal BackupForm.AskForm NewClearAsk()
        {
            int n = list.Count;
            var sum = History.SummarizeClear(list);
            string details = L.S("The whole price history and its pictures are deleted, including quotes the month, search or Show filter hide. " +
                                 "A safety copy is saved first: Undo brings it back while this window is open, later Backup… › Restore › Browse… (it opens at the copy).",
                                 "Hele prishistorikken og dens billeder slettes, også tilbud, som måned, søgning eller Vis skjuler. " +
                                 "Der gemmes først en sikkerhedskopi: Fortryd henter den tilbage, mens vinduet er åbent, senere Sikkerhedskopi… › Gendan › Gennemse… (den åbner ved kopien).");
            if (sum.FailedOpen > 0)
                details += "\n" + L.F("Failed prints not earned back yet ({0}) are no longer earned back.",
                                      "Fejlede print, der ikke er tjent ind endnu ({0}), bliver ikke længere tjent ind.", KrNb(sum.FailedOpenAmount));
            string q = n == 1 ? L.S("Delete the 1 quote?", "Slet det ene tilbud?") : L.F("Delete all {0} quotes?", "Slet alle {0} tilbud?", n);
            return new BackupForm.AskForm(L.S("Clear all", "Ryd alt"), q, details, L.S("Delete all", "Slet alle"), L.S("Cancel", "Annullér"));
        }

        /// Undo of the last delete (History.UndoDelete: back in their places, saved) or of Clear all (its safety copy
        /// restored with History.Restore, replace, without settings; MainForm takes the result over via Restored).
        internal void UndoClicked()
        {
            if (undoDelete != null)
            {
                var back = undoDelete;
                string err = null;
                if (offline) History.PutBack(list, back);
                else err = History.UndoDelete(list, back);
                if (err != null) { ShowDone(err, InfoKind.Error, null, null); return; }
                undoDelete = null;
                Rebuild(false, back.Records, back.Records.Count > 0 ? back.Records[0] : null);
                ShowDone(back.Count == 1 ? L.F("✓ \"{0}\" back.", "✓ \"{0}\" er tilbage.", ModelText(back.Records[0]))
                                         : L.F("✓ {0} quotes back.", "✓ {0} tilbud er tilbage.", back.Count),
                         InfoKind.Success, null, null);
            }
            else if (undoClearMemory != null)
            {
                list.AddRange(undoClearMemory);
                int n = undoClearMemory.Count;
                undoClearMemory = null;
                pics.Reload();
                Rebuild(true, null, null);
                ShowDone(L.F("✓ {0} quotes back.", "✓ {0} tilbud er tilbage.", n), InfoKind.Success, null, null);
            }
            else if (undoClearCopy != null)
            {
                BackupData data;
                RestoreResult res = null;
                string err = History.ReadBackupFile(undoClearCopy, out data);
                if (err == null && Settings == null)
                    err = L.S("Not undone. Backup… › Restore can bring the safety copy back.", "Ikke fortrudt. Sikkerhedskopi… › Gendan kan hente sikkerhedskopien tilbage.");
                if (err == null) err = History.Restore(list, Settings, data, true, false, DateTime.Now, out res);
                if (res == null) { ShowDone(err ?? L.S("Not undone.", "Ikke fortrudt."), InfoKind.Error, null, null); return; }
                if (Restored != null) Restored(res);
                else { list.Clear(); list.AddRange(res.Quotes); }
                undoClearCopy = null;
                pics.Reload();
                Rebuild(true, null, null);
                if (err != null) ShowDone(err, InfoKind.Warning, null, null);   // the history is back, the settings file wasn't written
                else ShowDone(L.F("✓ {0} quotes back.", "✓ {0} tilbud er tilbage.", list.Count), InfoKind.Success, null, null);
            }
            FocusList();
        }

        /// The undo is gone (window closing, a new delete, Clear all, Backup…): deleted records' pictures go now.
        void DropUndo(bool refreshInfo)
        {
            bool had = undoDelete != null || undoClearCopy != null || undoClearMemory != null;
            if (undoDelete != null)
            {
                if (!offline) History.DropRemoved(list, undoDelete);
                var stillHere = new HashSet<string>(IdsOf(list));
                var goneIds = new List<string>();
                foreach (var id in undoDelete.Ids()) if (!stillHere.Contains(id)) goneIds.Add(id);
                if (pics != null) pics.Forget(goneIds);
                undoDelete = null;
            }
            undoClearCopy = null;
            undoClearMemory = null;
            if (had && refreshInfo && doneShown && !IsDisposed) ShowInfo();   // no "Undo" link left that does nothing
        }

        void DropUndo() { DropUndo(false); }

        void ShowInFolder(string path)
        {
            if (offline || string.IsNullOrEmpty(path)) return;
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); }
            catch { }
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
