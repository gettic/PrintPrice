using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Price history: the saved quotes, newest first. Search, Show filter, one Price column chosen by the row's For,
    /// For (drop-down) and Note editable in the list and saved to history.json at once; Delete / Open again / Close.
    /// Picture mode (offline): sample list in memory, never saves.
    public class HistoryForm : CaptionForm
    {
        readonly List<QuoteRecord> list;
        readonly bool offline;
        readonly string problem;
        readonly TextBox txtSearch;
        readonly ComboBox cmbShow;
        readonly Label lblCount;
        readonly HistoryGrid grid;
        readonly Panel gridFrame;
        readonly InfoBox info;
        readonly Button btnDelete, btnOpen;
        readonly TableLayoutPanel top, bottom;
        readonly DataGridViewTextBoxColumn colDate, colModel, colTime, colGrams, colPrice, colNote;
        readonly DataGridViewComboBoxColumn colFor;
        readonly ToolTip modelTip;   // dark tooltip with the full model name when it is cut off
        readonly DataGridViewCheckBoxColumn colFailed;   // read-only cell, toggled by click / Space (ToggleFailed), drawn in the theme (PaintFailed)
        System.Windows.Forms.Timer deleteTimer;
        bool deleteArmed, filling;
        string saveError;

        /// The quote to open again (set when the window closes with OK).
        public QuoteRecord Selected;

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

            // ---------- search + filter + count ----------
            top = new TableLayoutPanel { ColumnCount = 5, AutoSize = true, Margin = new Padding(0, 0, 0, 8), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            for (int i = 0; i < 4; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // search box and Show combo: same top (Top anchor, same margin) and same height (FitLayout), captions centred beside them
            // Tag "single": stays a single-line edit (its cue banner needs that), see Theme.TextMargins
            txtSearch = new TextBox { Width = 260, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 3, 24, 3), Tag = "single" };
            txtSearch.HandleCreated += delegate
            {
                SendMessage(txtSearch.Handle, 0x1501, (IntPtr)1, L.S("Model, link or note", "Model, link eller note"));   // EM_SETCUEBANNER
            };
            txtSearch.TextChanged += delegate { Fill(); };
            cmbShow = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 3, 0, 3) };
            cmbShow.Items.AddRange(new object[]
            {
                L.S("All quotes", "Alle tilbud"), L.S("Friends & family", "Venner og familie"),
                L.S("Customers", "Kunder"), L.S("Failed", "Fejlede")
            });
            cmbShow.SelectedIndex = 0;
            cmbShow.SelectedIndexChanged += delegate { Fill(); };
            lblCount = new Label { AutoSize = true, Tag = "muted", Anchor = AnchorStyles.Right, Margin = new Padding(8, 3, 0, 3) };
            top.Controls.Add(Caption(L.S("Search", "Søg")), 0, 0);
            top.Controls.Add(txtSearch, 1, 0);
            top.Controls.Add(Caption(L.S("Show", "Vis")), 2, 0);
            top.Controls.Add(cmbShow, 3, 0);
            top.Controls.Add(lblCount, 4, 0);
            root.Controls.Add(top);

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
            colNote = TextColumn(L.S("Note", "Note"), false);
            colNote.ReadOnly = false;
            colNote.MaxInputLength = 200;
            colNote.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;   // takes what is left (no scroll bar = a bit wider)
            // Failed: ticking it marks the print as failed (its cost is earned back by new quotes when that is on in Settings)
            colFailed = new DataGridViewCheckBoxColumn { HeaderText = L.S("Failed", "Fejlet"), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable };
            colFailed.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.AddRange(new DataGridViewColumn[] { colDate, colModel, colTime, colGrams, colPrice, colFor, colNote, colFailed });

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
            modelTip.Draw += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.InfoBack)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
            grid.CellMouseEnter += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != colModel.Index) return;
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
                if (col == colFor || col == colNote || col == colFailed) return;   // those edit / toggle
                OpenAgain();
            };
            grid.OpenRequested = OpenAgain;
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && !grid.IsCurrentCellInEditMode) { e.Handled = true; DeleteClicked(); }
                // Space on the Failed cell ticks / unticks it
                if (e.KeyCode == Keys.Space && !grid.IsCurrentCellInEditMode && grid.CurrentCell != null && grid.CurrentCell.OwningColumn == colFailed)
                { e.Handled = true; ToggleFailed(grid.CurrentCell.RowIndex); }
            };
            grid.CellPainting += PaintFailed;
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
            btnOpen = new Button
            {
                Text = L.S("Open again", "Åbn igen"), AutoSize = true, MinimumSize = new Size(110, 32), Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(0, 0, 6, 0), Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            btnOpen.Click += delegate { OpenAgain(); };
            var close = new Button { Text = L.S("Close", "Luk"), Size = new Size(84, 32), Padding = Padding.Empty, Margin = Padding.Empty, DialogResult = DialogResult.Cancel };
            right.Controls.Add(btnOpen);
            right.Controls.Add(close);
            bottom.Controls.Add(btnDelete, 0, 0);
            bottom.Controls.Add(right, 1, 0);
            root.Controls.Add(bottom);

            AcceptButton = btnOpen;
            CancelButton = close;
            Theme.Apply(this);
            // Apply resets these
            gridFrame.BackColor = Theme.Border;
            grid.BackgroundColor = Theme.Input;
            ResumeLayout(true);

            Load += delegate { FitLayout(); Fill(); ShowInfo(); };
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
            colModel.Width = px(240);   // most names fit; longer ones get the dark tooltip (ModelTip)
            colTime.Width = width(colTime, L.S("000 h 00 m", "000 t 00 m"), 92);
            colGrams.Width = width(colGrams, "0000,00", 70);
            colPrice.Width = width(colPrice, "0000,00", 74);
            colFor.Width = width(colFor, L.S("Customer", "Venner"), 96);
            colNote.MinimumWidth = px(200);
            colFailed.Width = width(colFailed, null, 64);

            // Show combo exactly as tall as the search box (owner-drawn: height = ItemHeight + its frame, measured)
            int frame = cmbShow.Height - cmbShow.ItemHeight;
            if (txtSearch.Height - frame > 0) cmbShow.ItemHeight = txtSearch.Height - frame;

            int rowH = TextRenderer.MeasureText("Ag", grid.Font).Height + px(8);
            grid.RowTemplate.Height = rowH;
            grid.ColumnHeadersHeight = rowH + px(4);
            int cols = colDate.Width + colModel.Width + colTime.Width + colGrams.Width + colPrice.Width + colFor.Width + colFailed.Width + px(230);
            gridFrame.Size = new Size(cols + SystemInformation.VerticalScrollBarWidth + 2,
                                      grid.ColumnHeadersHeight + VisibleRows * rowH + 2);
            info.SetWidth(gridFrame.Width);
            top.MinimumSize = new Size(gridFrame.Width, 0);      // the count ends at the list's right edge
            bottom.MinimumSize = new Size(gridFrame.Width, 0);   // Close ends there too

            // Delete is wide enough for its "Delete?" second step too (no jump)
            btnDelete.Width = Math.Max(btnDelete.Width, TextRenderer.MeasureText(ConfirmText, btnDelete.Font).Width + px(24));
        }

        static string ConfirmText { get { return L.S("Really delete?", "Slet virkelig?"); } }

        // ---------- list ----------
        static string ModelText(QuoteRecord r)
        {
            if (!string.IsNullOrEmpty(r.Model)) return r.Model;
            string s = (r.Source ?? "").Trim().Trim('"');
            if (s.Length == 0) return L.S("(no name)", "(uden navn)");
            try { if (s.IndexOf('\\') >= 0 || s.IndexOf('/') < 0) return Path.GetFileName(s); } catch { }
            return s;
        }

        static string DateText(QuoteRecord r)
        {
            DateTime t;
            return History.TryTime(r.SavedAt, out t) ? t.ToString("dd-MM-yyyy HH:mm", Num.Dk) : r.SavedAt;
        }

        bool Matches(QuoteRecord r)
        {
            switch (cmbShow.SelectedIndex)
            {
                case 1: if (r.For != History.ForFriends) return false; break;
                case 2: if (r.For == History.ForFriends) return false; break;
                case 3: if (!r.Failed) return false; break;
            }
            string q = txtSearch.Text.Trim();
            if (q.Length == 0) return true;
            foreach (var s in new[] { r.Model, r.Source, r.Note, ModelText(r) })
                if (!string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            return false;
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
                                      r.Note ?? "", r.Failed);
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
        }

        /// Failed cell: a themed box (border colour, input background) with a red ✗ when ticked (white on the selected row).
        void PaintFailed(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != colFailed.Index) return;
            e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            bool on = e.Value is bool && (bool)e.Value;
            float sc = CurrentAutoScaleDimensions.Height / 96f;
            int size = (int)Math.Round(14 * sc);
            var box = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - size) / 2, e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size - 1, size - 1);
            using (var back = new SolidBrush(Theme.Input)) e.Graphics.FillRectangle(back, box);
            using (var pen = new Pen(selected ? Theme.Text : Theme.Muted)) e.Graphics.DrawRectangle(pen, box);   // no pure white (style)
            if (on)
            {
                var old = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int m = Math.Max(3, (int)Math.Round(3f * sc));   // ✗ 7 px of the 13 px box at 96 dpi (was 5)
                using (var pen = new Pen(Theme.Error, Math.Max(2f, 2f * sc)))
                {
                    e.Graphics.DrawLine(pen, box.Left + m, box.Top + m, box.Right - m, box.Bottom - m);
                    e.Graphics.DrawLine(pen, box.Right - m, box.Top + m, box.Left + m, box.Bottom - m);
                }
                e.Graphics.SmoothingMode = old;
            }
            e.Handled = true;
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
            string help = L.S("Double-click a quote (or Open again) to load it with today's settings; the saved price is shown next to the new one.\n" +
                              "Click For, Note or Failed to change them, changes are saved at once. The history is kept only on this PC and never sent anywhere.",
                              "Dobbeltklik på et tilbud (eller Åbn igen) for at hente det med dagens indstillinger; den gemte pris vises ved siden af den nye.\n" +
                              "Klik på Til, Note eller Fejlet for at ændre dem, ændringer gemmes med det samme. Historikken ligger kun på denne pc og sendes aldrig nogen steder.");
            if (saveError != null) { info.Text = saveError; info.Kind = InfoKind.Error; }
            else if (!string.IsNullOrEmpty(problem)) { info.Text = problem + "\n" + help; info.Kind = InfoKind.Warning; }
            else { info.Text = help; info.Kind = InfoKind.Info; }
        }

        void UpdateButtons()
        {
            bool any = SelectedRecord() != null;
            btnOpen.Enabled = any;
            btnDelete.Enabled = any;
        }

        // ---------- actions ----------
        void OpenAgain()
        {
            var r = SelectedRecord();
            if (r == null) return;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            Selected = r;
            DialogResult = DialogResult.OK;
            Close();
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
                    FriendsPrice = fr, OthersPrice = cu, For = who, Note = note
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
            return l;
        }
    }
}
