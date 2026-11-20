using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintPrice
{
    /// "Show quote…" window (idea 5 + 21): live preview of the quote page, the customer's name (optional, recent names
    /// from the history), which price, light or dark, and Copy picture / Save picture… / Save as PDF… / Copy text.
    /// Opening it saves nothing; every export goes into the price history (MainForm.ExportQuote, History.AddFor).
    internal class QuoteForm : CaptionForm
    {
        readonly Func<bool, string, QuoteData> build;   // (friends, customer) -> page data
        readonly QuoteExportHandler export;
        readonly Action<bool, bool> remember;            // (friends, dark) -> settings
        readonly List<string> recent;
        readonly QuotePreview preview;
        readonly TableLayoutPanel root, right;
        readonly ToolTip zoomTip;   // dark (owner-drawn) "Click to zoom…" on the preview
        readonly TextBox txtCustomer;
        readonly Label lblCustomer;   // "Customer (required)" / "(optional)" / a made invoice's "Bill to (kept)"
        readonly Label lblPrice;      // "Price on the quote" / "Price on the invoice" (+ " (locked)" on a made invoice)
        readonly Label lblStatus;     // "Print status" (hidden in the invoice window)
        readonly ComboBox cmbStatus;  // Not printed yet / Printing now / Printed / Failed (= PrintState)
        readonly Button btnRecent, btnCopyPic, btnSavePic, btnPdf, btnText, btnHistory, btnClose;
        readonly Choice rbCustomers, rbFriends, rbDark, rbLight;
        readonly InfoBox info;
        readonly Timer typing = new Timer { Interval = 300 };
        ContextMenuStrip recentMenu;
        bool ready;   // after Load: choice changes redraw and are remembered
        readonly bool invoice;   // invoice mode (History's Invoice…): the customer's name is required before any export

        /// invoice = the invoice window (title, "Customer (required)", exports off until a name is typed); info = the
        /// InfoBox's help text (null = the quote window's "Copying or saving also puts the quote in the price history").
        public QuoteForm(string model, string customer, bool friends, bool dark, List<string> recent,
                         double customerPrice, double friendsPrice, Func<bool, string, QuoteData> build,
                         QuoteExportHandler export, Action<bool, bool> remember, bool invoice = false, string helpText = null)
        {
            this.build = build;
            this.export = export;
            this.remember = remember;
            this.invoice = invoice;
            this.recent = recent ?? new List<string>();
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = invoice ? L.F("Invoice – {0}", "Faktura – {0}", model) : L.F("Quote – {0}", "Tilbud – {0}", model);
            // resizable by its edges (no maximise: the own title strip isn't made for it); Load sizes it to ~85 % of the
            // screen's working-area height and the preview takes all the room the right column leaves
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;   // Load centres it on the owner once its size is known
            if (Program.Hidden || Screenshots.Active) Screenshots.Place(this);

            root = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Padding = new Padding(14), Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);
            preview = new QuotePreview { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0) };
            root.Controls.Add(preview, 0, 0);

            right = new TableLayoutPanel { ColumnCount = 1, Margin = Padding.Empty, Dock = DockStyle.Fill };
            root.Controls.Add(right, 1, 0);
            Func<string, int, Label> Heading = (text, top) => new Label
            {
                Text = text, AutoSize = true, UseMnemonic = false, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, top, 0, 6)
            };

            // customer: text box + ▾ for recent names (dark menu)
            lblCustomer = Heading(invoice ? L.S("Customer (required)", "Kunde (skal udfyldes)") : L.S("Customer (optional)", "Kunde (valgfrit)"), 0);
            right.Controls.Add(lblCustomer);
            var custRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            txtCustomer = new TextBox { Width = 214, Margin = Padding.Empty, MaxLength = History.MaxCustomer, Text = customer ?? "" };
            btnRecent = new Button { Size = new Size(26, 25), Margin = Padding.Empty, Text = "", TabStop = true,
                                     AccessibleName = L.S("Recent customers", "Seneste kunder") };
            btnRecent.Enabled = this.recent.Count > 0;
            btnRecent.Paint += (s, e) => PaintArrow(e.Graphics, btnRecent);
            btnRecent.Click += delegate { ShowRecent(); };
            custRow.Controls.Add(txtCustomer, 0, 0);
            custRow.Controls.Add(btnRecent, 1, 0);
            right.Controls.Add(custRow);
            txtCustomer.TextChanged += delegate { typing.Stop(); typing.Start(); UpdateActions(); };
            typing.Tick += delegate { typing.Stop(); Redraw(); };

            // which price
            lblPrice = Heading(invoice ? L.S("Price on the invoice", "Pris på fakturaen") : L.S("Price on the quote", "Pris på tilbuddet"), 14);
            right.Controls.Add(lblPrice);
            var grpPrice = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            rbCustomers = new Choice { Text = L.S("Customers", "Kunder"), RightText = Num.Kr(customerPrice), RightColour = Theme.Others, Checked = !friends };
            rbFriends = new Choice { Text = L.S("Friends & family", "Venner og familie"), RightText = Num.Kr(friendsPrice), RightColour = Theme.Friends, Checked = friends };
            grpPrice.Controls.Add(rbCustomers);
            grpPrice.Controls.Add(rbFriends);
            right.Controls.Add(grpPrice);

            // light or dark (the PDF is always light)
            right.Controls.Add(Heading(L.S("Look of the picture", "Billedets udseende"), 12));
            var grpLook = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            rbDark = new Choice { Text = L.S("Dark", "Mørk"), Checked = dark };
            rbLight = new Choice { Text = L.S("Light", "Lys"), Checked = !dark };
            grpLook.Controls.Add(rbDark);
            grpLook.Controls.Add(rbLight);
            right.Controls.Add(grpLook);
            right.Controls.Add(new Theme.PlainLabel
            {
                Text = L.S("The PDF is always light (for printing).", "PDF'en er altid lys (til print)."), AutoSize = true, UseMnemonic = false,
                Tag = "muted", Font = new Font("Segoe UI", 9f), Margin = new Padding(22, 0, 0, 12)
            });
            // print status (quotes only): saved with the quote on every export (History.AddFor / SetPrintState)
            lblStatus = Heading(L.S("Print status", "Printstatus"), 0);
            right.Controls.Add(lblStatus);
            cmbStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, Margin = new Padding(0, 0, 0, 12),
                                       AccessibleName = L.S("Print status", "Printstatus") };
            // the order = PrintState's values
            cmbStatus.Items.AddRange(new object[]
            {
                L.S("Not printed yet", "Ikke printet endnu"), L.S("Printing now", "Printer nu"),
                L.S("Printed", "Printet"), L.S("Failed", "Fejlet")
            });
            cmbStatus.SelectedIndex = (int)PrintState.NotPrinted;
            right.Controls.Add(cmbStatus);
            // issue #7: shown on invoices too; a user's pick raises StatusPicked (saved records save it at once)
            cmbStatus.SelectionChangeCommitted += delegate { OnStatusPicked(); };
            // the zoom hint as a dark tooltip on the page (no extra line: the right column must fit a 768 px laptop screen)
            zoomTip = new ToolTip { OwnerDraw = true, InitialDelay = 600 };
            zoomTip.Draw += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.InfoBack)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
            zoomTip.SetToolTip(preview, L.S("Click to zoom in or out, drag to move the page", "Klik for at zoome ind eller ud, træk for at flytte siden"));            foreach (var c in new[] { rbCustomers, rbFriends, rbDark, rbLight })
                c.CheckedChanged += (s, e) => { if (((Choice)s).Checked) ChoiceChanged(); };

            // actions: 2 x 2, Copy picture = the accent (default) button
            var grid = new TableLayoutPanel { ColumnCount = 2, RowCount = 3, AutoSize = true, Margin = Padding.Empty };
            btnCopyPic = ActionButton(L.S("Copy picture", "Kopiér billede"), QuoteExport.Copy, new Padding(0, 0, 6, 6));
            btnSavePic = ActionButton(L.S("Save picture…", "Gem billede…"), QuoteExport.Png, new Padding(0, 0, 0, 6));
            btnPdf = ActionButton(L.S("Save as PDF…", "Gem som PDF…"), QuoteExport.Pdf, new Padding(0, 0, 6, 6));
            btnText = ActionButton(L.S("Copy text", "Kopiér tekst"), QuoteExport.Text, new Padding(0, 0, 0, 6));
            // full-width third row: saves the quote (price + name) to the history without making a picture, file or text
            btnHistory = ActionButton(L.S("Save to history", "Gem i historik"), QuoteExport.History, Padding.Empty);
            btnHistory.Width = 117 * 2 + 6;
            if (invoice) zoomTip.SetToolTip(btnHistory, L.S("Saves it as a quote; no invoice number.", "Gemmes som tilbud uden fakturanummer."));
            grid.Controls.Add(btnCopyPic, 0, 0);
            grid.Controls.Add(btnSavePic, 1, 0);
            grid.Controls.Add(btnPdf, 0, 1);
            grid.Controls.Add(btnText, 1, 1);
            grid.Controls.Add(btnHistory, 0, 2);
            grid.SetColumnSpan(btnHistory, 2);
            right.Controls.Add(grid);

            info = new InfoBox(240, 9.5f)
            {
                Margin = new Padding(0, 12, 0, 0),
                Text = helpText ?? L.S("Copying or saving also puts the quote in the price history.",
                                       "Når du kopierer eller gemmer, kommer tilbuddet også i prishistorikken.")
            };
            right.Controls.Add(info);

            // Close sits at the bottom of the right column, level with the preview's bottom (last row takes the rest)
            btnClose = new Button { Text = L.S("Close", "Luk"), Size = new Size(90, 32), Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                                    Margin = Padding.Empty, DialogResult = DialogResult.Cancel };
            right.Controls.Add(btnClose);
            // every control in its own fixed row: auto-placement skips hidden controls (the invoice window's print status),
            // which moved Close up into an AutoSize row under the info box and left the 100 % row empty (qol #1)
            for (int i = 0; i < right.Controls.Count; i++) right.SetCellPosition(right.Controls[i], new TableLayoutPanelCellPosition(0, i));
            right.RowCount = right.Controls.Count;
            while (right.RowStyles.Count < right.RowCount) right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles[right.RowCount - 1] = new RowStyle(SizeType.Percent, 100);

            AcceptButton = btnCopyPic;   // Enter (also in the name box) = Copy picture
            CancelButton = btnClose;     // Esc closes
            Theme.Apply(this);
            Theme.AccentButton(btnCopyPic);
            UpdateActions();
            ResumeLayout(true);

            Load += delegate
            {
                btnRecent.Size = new Size(Px(26), txtCustomer.Height);
                int frame = cmbStatus.Height - cmbStatus.ItemHeight;   // the status box as tall as the name box
                if (txtCustomer.Height - frame > 0) cmbStatus.ItemHeight = txtCustomer.Height - frame;
                FitWindow();
                ready = true;
                Redraw();
            };
            Resize += delegate { FitRoot(); };
            // type the name first; Enter copies the picture (picture mode / hidden: no focus games)
            Shown += delegate { if (!Program.Hidden && !Screenshots.Active) txtCustomer.Focus(); };
            FormClosed += delegate { typing.Dispose(); zoomTip.Dispose(); if (recentMenu != null) recentMenu.Dispose(); };
        }

        int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

        /// Window size (real px, measured): about 85 % of the working area's height; the preview is A4-shaped at that
        /// height (narrower if the screen is narrow), the right column keeps its own width. Minimum = the right column's
        /// full height and a 300 px preview. Centred on the owner (picture mode / hidden: stays where Place put it).
        void FitWindow()
        {
            int cap = CaptionHeight;
            Size pref = right.GetPreferredSize(Size.Empty);
            root.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, pref.Width);
            bool hidden = Program.Hidden || Screenshots.Active;
            Rectangle wa = !hidden && Owner != null ? Screen.FromControl(Owner).WorkingArea
                         : !hidden ? Screen.FromPoint(Cursor.Position).WorkingArea : Screen.PrimaryScreen.WorkingArea;
            int frameW = Width - ClientSize.Width, frameH = Height - ClientSize.Height;
            var pad = root.Padding;
            int minClientH = cap + pad.Vertical + pref.Height;
            int clientH = Math.Max(minClientH, Math.Min((int)(wa.Height * 0.85), wa.Height - frameH) - frameH);
            int pageH = clientH - cap - pad.Vertical;
            int previewW = (int)Math.Round((pageH - 2 * Px(8) - 2) * 210.0 / 297.0) + Px(10) + 2 * Px(8) + 2;
            int sideW = pad.Horizontal + preview.Margin.Right + pref.Width;
            int clientW = Math.Min(sideW + previewW, wa.Width - frameW);
            MinimumSize = new Size(sideW + Px(300) + frameW, minClientH + frameH);
            ClientSize = new Size(Math.Max(clientW, sideW + Px(300)), clientH);
            FitRoot();
            if (!hidden)
            {
                var o = Owner != null ? Owner.Bounds : wa;
                int x = o.Left + (o.Width - Width) / 2, y = o.Top + (o.Height - Height) / 2;
                x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
                y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height));
                Location = new Point(x, y);
            }
        }

        /// The content fills the window under the title strip (the preview grows with the window).
        void FitRoot()
        {
            if (root == null) return;
            root.SetBounds(0, CaptionHeight, ClientSize.Width, Math.Max(0, ClientSize.Height - CaptionHeight));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override bool ShowWithoutActivation { get { return Program.Hidden || Screenshots.Active; } }

        bool Friends { get { return rbFriends.Checked; } }
        bool Dark { get { return rbDark.Checked; } }
        string Customer { get { return txtCustomer.Text.Trim(); } }

        // --test-invoice (MainForm.TestInvoiceFlow): press an action as the button does, read the result
        /// Runs the action button's own Export(kind) (the window needn't be shown); returns the info box text after it.
        internal string TestExport(QuoteExport kind) { Export(kind); return info.Text; }
        /// The info box's kind after the last action (Error = it failed).
        internal InfoKind TestInfoKind { get { return info.Kind; } }
        /// The name box is read-only (LockCustomer: a made invoice).
        internal bool TestCustomerLocked { get { return txtCustomer.ReadOnly; } }
        /// The four export buttons are on (an invoice: only with a name).
        internal bool TestActionsOn { get { return btnPdf.Enabled; } }
        // --test-bill (MainForm.TestBillFlow)
        /// The page data the preview shows now (the build callback with the window's price choice and name).
        internal QuoteData TestData() { return build(Friends, Customer); }
        internal string TestInfoText { get { return info.Text ?? ""; } }
        internal string TestCustomer { get { return Customer; } }
        /// Types a name into the name box (no effect when it is locked).
        internal void TestSetCustomer(string name) { if (!txtCustomer.ReadOnly) txtCustomer.Text = name; }
        // the box's own Visible flag (Visible reads false until the window is shown, so the tests ask this)
        internal bool TestStatusShown { get { return statusShown; } }
        bool statusShown = true;
        /// Picks the Customers / Friends & family price as a click would.
        internal void TestFriends(bool friends)
        {
            var c = friends ? rbFriends : rbCustomers;
            if (c.Enabled) c.Checked = true;   // a greyed-out (locked) choice can't be clicked
        }

        Button ActionButton(string text, QuoteExport kind, Padding margin)
        {
            var b = new Button { Text = text, Size = new Size(117, 32), Margin = margin, Padding = Padding.Empty, UseMnemonic = false };
            b.Click += delegate { Export(kind); };
            return b;
        }

        /// Invoice: the four export buttons only with a customer's name (it goes on the invoice); a quote: always on.
        void UpdateActions()
        {
            bool on = !invoice || Customer.Length > 0;
            foreach (var b in new[] { btnCopyPic, btnSavePic, btnPdf, btnText })
                if (b.Enabled != on) b.Enabled = on;
        }

        /// The print status chosen in the box ("Mixed" on a combined window reads as NotPrinted: check StatusMixed).
        /// Setting it never raises StatusPicked and removes a "Mixed" line.
        public PrintState PrintStatus
        {
            get { int i = cmbStatus.SelectedIndex; return i >= 0 && i <= (int)PrintState.Failed ? (PrintState)i : PrintState.NotPrinted; }
            set
            {
                cmbStatus.SelectedIndex = (int)value;
                if (cmbStatus.Items.Count > 4) cmbStatus.Items.RemoveAt(4);
            }
        }

        /// A combined window whose jobs have different states: the box shows "Mixed" / "Blandet".
        public bool StatusMixed { get { return cmbStatus.SelectedIndex == 4; } }

        /// The user picked a print status in the box (issue #7). The handler saves it (saved records) or keeps it for the
        /// first export (a new job); it may set PrintStatus back (e.g. a "Failed" for several jobs that was not confirmed).
        public event Action StatusPicked;

        void OnStatusPicked()
        {
            if (StatusMixed) return;
            if (cmbStatus.Items.Count > 4) { var s = PrintStatus; PrintStatus = s; }   // a real state chosen: "Mixed" goes
            if (StatusPicked != null) StatusPicked();
        }

        /// A combined window (issue #7): heading "Print status (all 3 prints)"; shared = their one state, null = "Mixed".
        public void SetGroupStatus(int n, PrintState? shared)
        {
            lblStatus.Text = L.F("Print status (all {0} prints)", "Printstatus (alle {0} print)", n);
            if (shared.HasValue) PrintStatus = shared.Value;
            else
            {
                if (cmbStatus.Items.Count == 4) cmbStatus.Items.Add(L.S("Mixed", "Blandet"));
                cmbStatus.SelectedIndex = 4;
            }
        }

        /// --test-invoice / --test-bill: picks a status as the user would (raises StatusPicked).
        internal void TestPickStatus(PrintState state)
        {
            cmbStatus.SelectedIndex = (int)state;
            OnStatusPicked();
        }

        internal string TestStatusHeading { get { return lblStatus.Text; } }
        internal bool TestStatusMixed { get { return StatusMixed; } }

        /// A line in the info box under the actions (e.g. the invoice of a failed print: Warning).
        public void ShowNote(string text, InfoKind kind)
        {
            ShowInfo(text, kind);
        }

        /// One more line under the info box's text (e.g. "1 failed print is left off the bill."); a Warning stays a
        /// Warning, an Info box becomes kind.
        public void AddNote(string text, InfoKind kind)
        {
            if (string.IsNullOrEmpty(text)) return;
            string now = info.Text ?? "";
            ShowInfo(now.Length > 0 ? now + "\n" + text : text, info.Kind == InfoKind.Info ? kind : info.Kind);
        }

        /// A combined quote (several History jobs): no print status box (it would set one state on all of them).
        public void HideStatus()
        {
            lblStatus.Visible = cmbStatus.Visible = statusShown = false;
        }

        /// A made invoice (History's Invoice… on a billed quote): the name it was made out to stays; the box is read-only
        /// (Muted text, still selectable / copyable), ▾ greyed out, heading "Bill to (kept)". Call before the window shows.
        public void LockCustomer()
        {
            lblCustomer.Text = L.S("Bill to (locked)", "Kunde (låst)");
            txtCustomer.ReadOnly = true;
            txtCustomer.BackColor = Theme.Input;   // ReadOnly would turn it Windows grey
            txtCustomer.ForeColor = Theme.Muted;
            btnRecent.Enabled = false;
            btnRecent.TabStop = false;
        }

        /// A made invoice (A4): the name and the billed price stay. friends = the price it was made at (the records' For);
        /// that choice is chosen and the other one greyed out, so the same number never shows another price.
        public void LockInvoice(bool friends)
        {
            LockCustomer();
            bool was = ready;
            ready = false;   // no remember / redraw for the forced choice; Redraw below
            if (friends) rbFriends.Checked = true; else rbCustomers.Checked = true;
            ready = was;
            rbCustomers.Enabled = !friends;
            rbFriends.Enabled = friends;
            lblPrice.Text = L.S("Price on the invoice (locked)", "Pris på fakturaen (låst)");
            if (ready) Redraw();
        }

        /// --test-invoice / --test-bill: the price choice is locked (LockInvoice).
        internal bool TestPriceLocked { get { return !rbCustomers.Enabled || !rbFriends.Enabled; } }
        /// --test-invoice / --test-bill: the chosen price (true = friends & family).
        internal bool TestFriendsChosen { get { return Friends; } }

        void ChoiceChanged()
        {
            if (!ready) return;
            remember(Friends, Dark);
            Redraw();
        }

        /// Draws the page again (price, look, customer); errors go to the info box.
        public void Redraw()
        {
            typing.Stop();
            try { preview.SetPage(QuotePage.Render(build(Friends, Customer), Dark)); }
            catch (Exception ex) { ShowInfo(ex.Message, InfoKind.Error); }
        }

        void Export(QuoteExport kind)
        {
            bool failed;
            string msg = export(this, kind, Friends, Dark, Customer, out failed);
            if (msg == null) return;   // file dialog cancelled
            ShowInfo(msg, failed ? InfoKind.Error : InfoKind.Success);
            if (!failed) Redraw();     // the page now shows the saved quote's number and date
        }

        void ShowInfo(string text, InfoKind kind)
        {
            info.Kind = kind;
            info.Text = text;
        }

        void ShowRecent()
        {
            if (recent.Count == 0) return;
            if (recentMenu == null)
            {
                recentMenu = new ContextMenuStrip { ShowImageMargin = false, Renderer = new QuoteMenuRenderer(), BackColor = Theme.Input, ForeColor = Theme.Text };
                foreach (var name in recent)
                {
                    string n = name;
                    var mi = new ToolStripMenuItem(n.Replace("&", "&&")) { ForeColor = Theme.Text };
                    mi.Click += delegate { txtCustomer.Text = n; txtCustomer.SelectionStart = n.Length; Redraw(); };
                    recentMenu.Items.Add(mi);
                }
            }
            recentMenu.MinimumSize = new Size(txtCustomer.Width + btnRecent.Width, 0);
            recentMenu.Show(txtCustomer, new Point(0, txtCustomer.Height));
        }

        /// The ▾ of the recent-names button (Muted when there are no names yet).
        void PaintArrow(Graphics g, Button b)
        {
            float cx = b.Width / 2f, cy = b.Height / 2f, w = Px(4.5f);
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(b.Enabled ? Theme.Text : Theme.Muted))
                g.FillPolygon(br, new[] { new PointF(cx - w, cy - w / 2), new PointF(cx + w, cy - w / 2), new PointF(cx, cy + w / 2) });
            g.Restore(st);
        }

        /// Round choice (radio) button in the dark look: Input circle with a Border ring (110,110,116 on hover / focus),
        /// Text dot when chosen, text; optional bold right-hand text in its own colour (the prices). Keeps RadioButton's
        /// behaviour (groups by container, arrow keys, Space).
        internal sealed class Choice : RadioButton
        {
            public string RightText = "";
            public Color RightColour = Theme.Text;
            bool hot;

            public Choice()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                AutoSize = false;
                UseMnemonic = false;
                Size = new Size(240, 28);
                Margin = Padding.Empty;
            }

            int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

            // screen readers: the text as shown (Windows would take "&" in "Friends & family" as a mnemonic and drop it)
            protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; base.OnTextChanged(e); }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Back);
                int d = Px(14), y = (Height - d) / 2;
                var st = g.Save();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(Theme.Input)) g.FillEllipse(br, 0, y, d, d);
                using (var pen = new Pen(hot || Focused ? Color.FromArgb(110, 110, 116) : Theme.Border)) g.DrawEllipse(pen, 0, y, d - 1, d - 1);
                if (Checked)
                {
                    int dot = Px(6);
                    using (var br = new SolidBrush(Enabled ? Theme.Text : Theme.Muted))
                        g.FillEllipse(br, (d - 1 - dot) / 2f, y + (d - 1 - dot) / 2f, dot, dot);
                }
                g.Restore(st);
                int tx = d + Px(8);
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
                Color fore = Enabled ? Theme.Text : Theme.Muted;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, Width - tx, Height), fore, flags);
                if (!string.IsNullOrEmpty(RightText))
                    using (var bold = new Font(Font, FontStyle.Bold))
                        TextRenderer.DrawText(g, RightText, bold, new Rectangle(0, 0, Width, Height), Enabled ? RightColour : Theme.Muted, flags | TextFormatFlags.Right);
                if (Focused && ShowFocusCues)
                {
                    var size = TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags);
                    using (var pen = new Pen(Theme.Muted) { DashStyle = DashStyle.Dot })
                        g.DrawRectangle(pen, tx - 2, (Height - size.Height) / 2 - 1, size.Width + 3, size.Height + 1);
                }
            }
        }

        /// The quote page fitted to the width, in a 1 px Border frame on Input, with a slim red scroll bar (like
        /// UpdateForm's): wheel, drag the thumb, click the track. Owns the page bitmap.
        internal sealed class QuotePreview : Control
        {
            Bitmap page, scaled;
            int scroll, scrollX, dragOffset = -1;
            bool hotThumb;
            bool zoom;                  // true = the page at 100 % (its own pixels), else fitted to the width
            bool pressed, panning;      // left press on the page: a click toggles the zoom, a drag pans
            Point downPt;
            int downScroll, downScrollX;

            public QuotePreview()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Input;
                Cursor = Cursors.Hand;
            }

            int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }
            int Bar { get { return Px(10); } }
            int Pad { get { return Px(8); } }
            Rectangle View { get { return new Rectangle(1 + Pad, 1 + Pad, Math.Max(1, Width - 2 - Bar - 2 * Pad), Math.Max(1, Height - 2 - 2 * Pad)); } }
            Rectangle Track { get { return new Rectangle(Width - 1 - Bar, 1, Bar, Height - 2); } }
            int ContentHeight { get { return page == null ? 0 : zoom ? page.Height : (int)Math.Round(page.Height * (double)View.Width / page.Width); } }
            int ContentWidth { get { return page == null ? 0 : zoom ? page.Width : View.Width; } }
            int MaxScroll { get { return Math.Max(0, ContentHeight - View.Height); } }
            int MaxScrollX { get { return Math.Max(0, ContentWidth - View.Width); } }

            /// Fit width ↔ 100 %, keeping the page point under the mouse where it is.
            void ToggleZoom(Point p)
            {
                if (page == null) return;
                var v = View;
                double k = page.Width / (double)v.Width;   // page px per fitted px
                int ox = p.X - v.X, oy = p.Y - v.Y;
                if (!zoom)
                {
                    double px = ox * k, py = (oy + scroll) * k;
                    zoom = true;
                    scrollX = Math.Max(0, Math.Min(MaxScrollX, (int)Math.Round(px - ox)));
                    scroll = Math.Max(0, Math.Min(MaxScroll, (int)Math.Round(py - oy)));
                }
                else
                {
                    double py = (oy + scroll) / k;
                    zoom = false;
                    scrollX = 0;
                    scroll = Math.Max(0, Math.Min(MaxScroll, (int)Math.Round(py - oy)));
                }
                Invalidate();
            }

            Rectangle Thumb
            {
                get
                {
                    var t = Track;
                    int ch = ContentHeight;
                    if (ch <= View.Height || ch == 0) return Rectangle.Empty;
                    int h = Math.Max(Px(24), t.Height * View.Height / ch);
                    int y = t.Y + (int)((t.Height - h) * (long)scroll / Math.Max(1, MaxScroll));
                    return new Rectangle(t.X + Px(2), y + Px(2), t.Width - Px(4), h - Px(4));
                }
            }

            public void SetPage(Bitmap b)
            {
                var old = page;
                page = b;
                if (old != null) old.Dispose();
                DropScaled();
                scroll = Math.Min(scroll, MaxScroll);
                scrollX = Math.Min(scrollX, MaxScrollX);
                Invalidate();
            }

            void DropScaled() { if (scaled != null) { scaled.Dispose(); scaled = null; } }

            protected override void OnResize(EventArgs e)
            {
                DropScaled();
                scroll = Math.Min(scroll, MaxScroll);
                scrollX = Math.Min(scrollX, MaxScrollX);
                base.OnResize(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { DropScaled(); if (page != null) { page.Dispose(); page = null; } }
                base.Dispose(disposing);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Input);
                var v = View;
                if (page != null && zoom)
                {
                    // 100 %: the page's own pixels (centred when narrower than the view), panned by scrollX / scroll
                    var clip = g.Clip;
                    g.SetClip(v);
                    int x = page.Width < v.Width ? v.X + (v.Width - page.Width) / 2 : v.X - scrollX;
                    g.DrawImage(page, new Rectangle(x, v.Y - scroll, page.Width, page.Height));   // pixel size, not 150-dpi physical size
                    g.Clip = clip;
                }
                else if (page != null)
                {
                    if (scaled == null)
                    {
                        scaled = new Bitmap(v.Width, Math.Max(1, ContentHeight));
                        using (var sg = Graphics.FromImage(scaled))
                        {
                            sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            sg.DrawImage(page, new Rectangle(0, 0, scaled.Width, scaled.Height));
                        }
                    }
                    var clip = g.Clip;
                    g.SetClip(v);
                    g.DrawImageUnscaled(scaled, v.X, v.Y - scroll);
                    g.Clip = clip;
                }
                var thumb = Thumb;
                if (thumb != Rectangle.Empty)
                {
                    using (var br = new SolidBrush(Color.FromArgb(34, 36, 41))) g.FillRectangle(br, Track);
                    using (var br = new SolidBrush(hotThumb || dragOffset >= 0 ? Theme.Others : Theme.InfoAccent)) g.FillRectangle(br, thumb);
                }
                using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            void ScrollTo(int y)
            {
                int s = Math.Max(0, Math.Min(MaxScroll, y));
                if (s != scroll) { scroll = s; Invalidate(); }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                int step = e.Delta * Px(60) / 120;
                if (zoom && (ModifierKeys & Keys.Shift) != 0) ScrollXTo(scrollX - step);   // Shift + wheel = sideways at 100 %
                else ScrollTo(scroll - step);
                base.OnMouseWheel(e);
            }

            void ScrollXTo(int x)
            {
                int s = Math.Max(0, Math.Min(MaxScrollX, x));
                if (s != scrollX) { scrollX = s; Invalidate(); }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                var thumb = Thumb;
                if (thumb != Rectangle.Empty && thumb.Contains(e.Location)) { dragOffset = e.Y - thumb.Y; Capture = true; Invalidate(); return; }
                if (Track.Contains(e.Location))
                {
                    if (thumb != Rectangle.Empty) ScrollTo(scroll + (e.Y < thumb.Y ? -View.Height : View.Height));
                    return;
                }
                if (page == null) return;
                // on the page: click = zoom, drag = pan
                pressed = true;
                panning = false;
                downPt = e.Location;
                downScroll = scroll;
                downScrollX = scrollX;
                Capture = true;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (pressed)
                {
                    int dx = e.X - downPt.X, dy = e.Y - downPt.Y;
                    if (!panning && Math.Abs(dx) + Math.Abs(dy) > Px(4)) { panning = true; Cursor = Cursors.SizeAll; }
                    if (panning)
                    {
                        ScrollTo(downScroll - dy);
                        if (zoom) ScrollXTo(downScrollX - dx);
                    }
                    return;
                }
                var thumb = Thumb;
                if (dragOffset >= 0)
                {
                    var t = Track;
                    int range = t.Height - (thumb.Height + Px(4));
                    if (range > 0) ScrollTo((int)((long)(e.Y - dragOffset - Px(2) - t.Y) * MaxScroll / range));
                    return;
                }
                bool hot = thumb != Rectangle.Empty && thumb.Contains(e.Location);
                if (hot != hotThumb) { hotThumb = hot; Invalidate(); }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (dragOffset >= 0) { dragOffset = -1; Capture = false; Invalidate(); }
                if (pressed)
                {
                    pressed = false;
                    Capture = false;
                    if (!panning) ToggleZoom(downPt);
                    panning = false;
                    Cursor = Cursors.Hand;
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (hotThumb) { hotThumb = false; Invalidate(); }
            }
        }
    }
}
