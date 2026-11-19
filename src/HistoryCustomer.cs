using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintPrice
{
    /// History's Customer column (combined-bill part B): the name typed in place like Note, a ▾ in the cell for the
    /// earlier names (dark menu), "Customer name…" and "Select all quotes for …" in the row menu, and the dark tooltip
    /// when a made invoice keeps another name (QuoteRecord.BilledTo).
    public partial class HistoryForm
    {
        const int CustomerArrowW = 22;           // the ▾ zone at the cell's right (96 dpi)
        int hotCustomerRow = -1;                 // the row whose Customer cell the mouse is over (-1 = none)
        bool hotArrow;                           // ... and over its ▾
        Point rowMenuPoint;                      // where the row menu opened (Customer name… opens there too)
        ContextMenuStrip nameMenu;               // the names menu (built each time it opens)
        List<QuoteRecord> typedTargets;          // "Type a new name…" for several quotes: the name goes on all of them

        int ArrowZone() { return Px(CustomerArrowW); }

        void HookCustomerColumn()
        {
            grid.CellPainting += PaintCustomerArrow;
            grid.CellMouseEnter += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == colCustomer.Index) SetHotCustomer(e.RowIndex, false); };
            grid.CellMouseMove += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != colCustomer.Index) return;
                SetHotCustomer(e.RowIndex, e.X >= colCustomer.Width - ArrowZone());
            };
            grid.CellMouseLeave += (s, e) => { if (e.ColumnIndex == colCustomer.Index) SetHotCustomer(-1, false); };
            grid.CurrentCellChanged += delegate { grid.InvalidateColumn(colCustomer.Index); };
            grid.Enter += delegate { grid.InvalidateColumn(colCustomer.Index); };
            grid.Leave += delegate { grid.InvalidateColumn(colCustomer.Index); };
            grid.CellEndEdit += (s, e) => { if (e.ColumnIndex == colCustomer.Index) typedTargets = null; };
            // Alt+Down / F4 on the Customer cell (also while typing in it): the names menu
            grid.KeyDown += (s, e) =>
            {
                if (!IsNameMenuKey(e) || grid.IsCurrentCellInEditMode || grid.CurrentCell == null || grid.CurrentCell.OwningColumn != colCustomer) return;
                e.Handled = true;
                ShowCustomerMenuAt(grid.CurrentCell.RowIndex);
            };
            grid.EditingControlShowing += (s, e) =>
            {
                var tb = e.Control as TextBox;
                if (tb == null) return;
                tb.AutoCompleteMode = AutoCompleteMode.None;   // never Windows' light AutoComplete list
                tb.KeyDown -= NameEditorKeyDown;
                tb.KeyDown += NameEditorKeyDown;
            };
            FormClosed += delegate { if (nameMenu != null) nameMenu.Dispose(); };
        }

        static bool IsNameMenuKey(KeyEventArgs e)
        {
            return (e.Alt && e.KeyCode == Keys.Down) || (e.KeyCode == Keys.F4 && !e.Alt);
        }

        void NameEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsNameMenuKey(e) || grid.CurrentCell == null || grid.CurrentCell.OwningColumn != colCustomer) return;
            e.Handled = e.SuppressKeyPress = true;
            int row = grid.CurrentCell.RowIndex;
            var targets = typedTargets;
            grid.CancelEdit();   // the half-typed text is dropped: a name from the menu replaces it
            grid.EndEdit();
            ShowCustomerMenu(targets ?? TargetsFor(row), CellMenuPoint(row), CellWidth(row));
        }

        void SetHotCustomer(int row, bool arrow)
        {
            if (row == hotCustomerRow && arrow == hotArrow) return;
            int old = hotCustomerRow;
            hotCustomerRow = row;
            hotArrow = arrow;
            if (old >= 0 && old < grid.Rows.Count) grid.InvalidateCell(colCustomer.Index, old);
            if (row >= 0 && row < grid.Rows.Count) grid.InvalidateCell(colCustomer.Index, row);
        }

        bool OverCustomerArrow(int row) { return hotArrow && hotCustomerRow == row; }

        /// The ▾ at the Customer cell's right on the hovered row and on the current cell (focused list): Muted, Text when
        /// the mouse is over it; on a selected (red) row Text / white.
        void PaintCustomerArrow(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != colCustomer.Index) return;
            bool current = grid.CurrentCell != null && grid.CurrentCell.RowIndex == e.RowIndex && grid.CurrentCell.ColumnIndex == e.ColumnIndex
                           && grid.ContainsFocus;
            if (e.RowIndex != hotCustomerRow && !current) return;
            if (current && grid.IsCurrentCellInEditMode) return;
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            bool over = OverCustomerArrow(e.RowIndex);
            Color c = selected ? (over ? Color.White : Theme.Text) : (over ? Theme.Text : Theme.Muted);
            int zone = ArrowZone();
            float cx = e.CellBounds.Right - zone / 2f - Px(2), cy = e.CellBounds.Top + e.CellBounds.Height / 2f, w = Px(4) + 0.5f;
            var st = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(c))
                e.Graphics.FillPolygon(br, new[] { new PointF(cx - w, cy - w / 2), new PointF(cx + w, cy - w / 2), new PointF(cx, cy + w / 2) });
            e.Graphics.Restore(st);
            e.Handled = true;
        }

        /// The quotes a ▾ / Alt+Down on this row acts on: all selected ones when the row is one of 2+ selected, else the row.
        List<QuoteRecord> TargetsFor(int row)
        {
            var res = new List<QuoteRecord>();
            if (row < 0 || row >= grid.Rows.Count) return res;
            var r = grid.Rows[row].Tag as QuoteRecord;
            if (r == null) return res;
            var sel = SelectedRecords();
            if (grid.Rows[row].Selected && sel.Count > 1) return sel;
            res.Add(r);
            return res;
        }

        Point CellMenuPoint(int row)
        {
            var rc = grid.GetCellDisplayRectangle(colCustomer.Index, row, false);
            return grid.PointToScreen(new Point(rc.Left, rc.Bottom));
        }

        int CellWidth(int row) { return grid.GetCellDisplayRectangle(colCustomer.Index, row, false).Width; }

        void ShowCustomerMenuAt(int row)
        {
            ShowCustomerMenu(TargetsFor(row), CellMenuPoint(row), CellWidth(row));
        }

        void ShowCustomerMenu(List<QuoteRecord> targets, Point screen)
        {
            ShowCustomerMenu(targets, screen, 0);
        }

        /// The names menu (QuoteMenuRenderer): muted heading, the 10 newest names (round dot = the name on all of them),
        /// "Type a new name…" (list view), "No name". A choice is saved at once.
        void ShowCustomerMenu(List<QuoteRecord> targets, Point screen, int minWidth)
        {
            if (targets == null || targets.Count == 0) return;
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            if (nameMenu != null) nameMenu.Dispose();
            nameMenu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Renderer = new QuoteMenuRenderer(),
                                              BackColor = Theme.Input, ForeColor = Theme.Text };
            Func<string, Action, ToolStripMenuItem> Item = (text, act) =>
            {
                var mi = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
                if (act != null) mi.Click += delegate { act(); };
                return mi;
            };
            nameMenu.Items.Add(new ToolStripMenuItem(targets.Count == 1 ? L.S("Name on this quote:", "Navn på dette tilbud:")
                                                                        : L.F("Name on {0} quotes:", "Navn på {0} tilbud:", targets.Count)) { Enabled = false });
            nameMenu.Items.Add(new ToolStripSeparator());
            string shared = SharedName(targets);
            var names = History.RecentCustomers(list, 10);
            foreach (var name in names)
            {
                string n = name;
                var mi = Item(MenuName(n, 40), () => ApplyName(targets, n));
                mi.Checked = shared != null && string.Equals(shared, n.Trim(), StringComparison.OrdinalIgnoreCase);
                nameMenu.Items.Add(mi);
            }
            if (names.Count == 0)
                nameMenu.Items.Add(new ToolStripMenuItem(L.S("No earlier names yet", "Ingen tidligere navne endnu")) { Enabled = false });
            nameMenu.Items.Add(new ToolStripSeparator());
            if (view == ViewList)
                nameMenu.Items.Add(Item(L.S("Type a new name…", "Skriv et nyt navn…"), () => StartTyping(targets)));
            bool anyName = false;
            foreach (var r in targets) anyName |= (r.Customer ?? "").Trim().Length > 0;
            var none = Item(L.S("No name", "Intet navn"), () => ApplyName(targets, ""));
            none.Enabled = anyName;
            nameMenu.Items.Add(none);
            if (minWidth > 0) nameMenu.MinimumSize = new Size(minWidth, 0);
            nameMenu.Show(screen);
        }

        /// The name on these quotes (History.SetCustomer: trimmed, max 80; BilledTo untouched), saved at once; they stay selected.
        void ApplyName(List<QuoteRecord> targets, string name)
        {
            int n = History.SetCustomer(targets, name);
            if (n > 0) SaveList();
            Fill(new HashSet<QuoteRecord>(targets), targets.Contains(CurrentRecord()) ? CurrentRecord() : targets[0]);
            FocusList();
        }

        /// "Type a new name…": edits the Customer cell of the current (or first) target; with several, the typed name goes on all.
        void StartTyping(List<QuoteRecord> targets)
        {
            if (view != ViewList || targets.Count == 0) return;
            var cur = CurrentRecord();
            var first = targets.Contains(cur) ? cur : targets[0];
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                if (grid.Rows[i].Tag != first) continue;
                grid.CurrentCell = grid.Rows[i].Cells[colCustomer.Index];
                if (targets.Count > 1)
                {
                    // select them again in one go (no SelectionChanged work per row), then the buttons / count once
                    var set = new HashSet<QuoteRecord>(targets);
                    filling = true;
                    try { foreach (DataGridViewRow row in grid.Rows) row.Selected = set.Contains(row.Tag as QuoteRecord); }
                    finally { filling = false; }
                    UpdateButtons();
                    UpdateCount();
                }
                typedTargets = targets.Count > 1 ? new List<QuoteRecord>(targets) : null;
                grid.Focus();
                grid.BeginEdit(true);
                return;
            }
        }

        /// A name typed in a Customer cell (CellValueChanged): on that quote, or on all of "Type a new name…"'s quotes.
        void CustomerTyped(DataGridViewRow row, QuoteRecord r)
        {
            object v = row.Cells[colCustomer.Index].Value;
            string name = v == null ? "" : v.ToString();
            var targets = typedTargets != null && typedTargets.Contains(r) ? typedTargets : new List<QuoteRecord> { r };
            typedTargets = null;
            int n = History.SetCustomer(targets, name);
            filling = true;
            row.Cells[colCustomer.Index].Value = r.Customer ?? "";   // trimmed / cut as saved
            filling = false;
            if (n == 0) return;
            SaveList();
            if (targets.Count > 1)
                BeginInvoke((MethodInvoker)delegate { Fill(new HashSet<QuoteRecord>(targets), r); });   // after the edit has ended
        }

        /// The one name all of these quotes have (trimmed, case-insensitive; first spelling), or null (none / different).
        static string SharedName(List<QuoteRecord> sel)
        {
            if (sel == null || sel.Count == 0) return null;
            string name = (sel[0].Customer ?? "").Trim();
            if (name.Length == 0) return null;
            foreach (var r in sel)
                if (!string.Equals((r.Customer ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)) return null;
            return name;
        }

        /// A name for a menu: cut with "…" after max characters, & doubled.
        static string MenuName(string name, int max)
        {
            string n = (name ?? "").Trim();
            if (n.Length > max) n = n.Substring(0, max - 1).TrimEnd() + "…";
            return n.Replace("&", "&&");
        }

        /// Row menu "Select all quotes for <name>": every shown quote (month / filters / search) with that name.
        void SelectSameName()
        {
            string name = SharedName(SelectedRecords());
            if (name == null) return;
            var keep = new HashSet<QuoteRecord>();
            foreach (var r in shown)
                if (string.Equals((r.Customer ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)) keep.Add(r);
            var cur = CurrentRecord();
            Fill(keep, keep.Contains(cur) ? cur : null);
            FocusList();
        }

        /// The tooltip line for a made invoice whose stored name (BilledTo) differs from the Customer now; else null.
        static string BilledToNote(QuoteRecord r)
        {
            if (r == null || string.IsNullOrEmpty(r.InvoiceNo)) return null;
            string bt = (r.BilledTo ?? "").Trim();
            if (bt.Length == 0 || string.Equals(bt, (r.Customer ?? "").Trim(), StringComparison.Ordinal)) return null;
            return L.F("Invoice {0} is made out to {1}", "Faktura {0} er lavet til {1}", r.InvoiceNo, bt);
        }
    }
}
