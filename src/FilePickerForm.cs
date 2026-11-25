using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Idea 21: the files picked for a job with the filament and colour of each, and the "together on one plate" choice.
    /// Parts[i] = the i-th picked file (File, Filament, Colour, Slot; Slot 0 = a .3mf / G-code with its own colours),
    /// Slots[i] = its colour (1-based index into Colours;
    /// 1 for a file with its own colours, which BambuSlicer.SliceColours ignores).
    public sealed class ColourPlan
    {
        public List<HistoryPart> Parts = new List<HistoryPart>();
        public List<int> Slots = new List<int>();
        public List<ColourChoice> Colours = new List<ColourChoice>();
        public bool Together;
        /// Batch & plates: copies per file (same order as Parts; null = 1 each), from the picker's Copies boxes.
        public List<int> Copies;
        /// batch-2: share the colours' part-full last plates on one plate (the picker's "Share the last plate" tick; Repeat print /
        /// Open again: History.ShareLastPlateFor). Only used for a batch (copies) with 2+ colours.
        public bool Share;

        /// A .3mf project or a G-code keeps its own colours (no colour of its own in the picker).
        public static bool OwnColours(string file)
        {
            string f = file ?? "";
            return f.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) || BambuSlicer.IsGcodeFile(f);
        }

        /// One colour per different (filament, colour) of the plain files, in the order they first appear; also used to
        /// slice a saved job again (History.PartsOf).
        public static ColourPlan Build(IList<HistoryPart> parts, bool together)
        {
            var plan = new ColourPlan { Together = together };
            var keys = new List<string>();
            foreach (var p in parts)
            {
                if (p == null) continue;
                // batch & plates: the copies of each file go with it (saved in History, used by Repeat / Open again)
                var c = new HistoryPart { File = p.File ?? "", Filament = (p.Filament ?? "").Trim(), Colour = (p.Colour ?? "").Trim().ToUpperInvariant(), Slot = 1,
                                          Copies = Math.Max(1, p.Copies) };
                if (OwnColours(c.File)) { c.Filament = ""; c.Colour = ""; c.Slot = 0; }   // saved as slot 0 = the file's own colours
                else
                {
                    string key = c.Filament.ToLowerInvariant() + "|" + c.Colour;
                    int i = keys.IndexOf(key);
                    if (i < 0)
                    {
                        keys.Add(key);
                        plan.Colours.Add(new ColourChoice { Filament = c.Filament, Colour = c.Colour });
                        i = keys.Count - 1;
                    }
                    c.Slot = i + 1;
                }
                plan.Slots.Add(Math.Max(1, c.Slot));   // the slicer still gets 1 for an own-colours file (SliceColours ignores it)
                plan.Parts.Add(c);
            }
            if (plan.Parts.Any(x => x.Copies > 1)) plan.Copies = plan.Parts.Select(x => x.Copies).ToList();
            return plan;
        }
    }

    /// Lets the user tick which files to slice and pick a filament and colour per plain model file (idea 21), plus
    /// "Print together on one plate (AMS lite)". Printables models with 2+ files and 2+ dropped / chosen local files.
    public class FilePickerForm : CaptionForm
    {
        const int ContentW = 520, RowH = 28, MaxRows = 8, ComboW = 190;   // 96 dpi units

        /// Common filament colours for the swatch menu (Bambu Lab PLA Basic shades). Filament data, not UI colours.
        internal static readonly string[][] FilamentColours =
        {
            new[] { "#000000", "Black", "Sort" }, new[] { "#FFFFFF", "White", "Hvid" }, new[] { "#8E9089", "Grey", "Grå" },
            new[] { "#C12E1F", "Red", "Rød" }, new[] { "#FF6A13", "Orange", "Orange" }, new[] { "#F4EE2A", "Yellow", "Gul" },
            new[] { "#00AE42", "Green", "Grøn" }, new[] { "#0A2989", "Blue", "Blå" }, new[] { "#0086D6", "Light blue", "Lyseblå" },
            new[] { "#5E43B7", "Purple", "Lilla" }, new[] { "#F55A74", "Pink", "Pink" }, new[] { "#9D432C", "Brown", "Brun" },
            new[] { "#F7E6DE", "Beige", "Beige" }
        };

        /// The name (app language) of the FilamentColours entry nearest to c ("Red", "Light blue"); "" for an empty colour.
        /// Used to tell material rows with the same profile apart (qol 8: "Bambu PLA Basic (Red)").
        internal static string ColourName(Color c)
        {
            if (c == Color.Empty || c.A == 0) return "";
            string best = "";
            double bestD = double.MaxValue;
            foreach (var e in FilamentColours)
            {
                Color k = ColorTranslator.FromHtml(e[0]);
                double dr = c.R - k.R, dg = c.G - k.G, db = c.B - k.B;
                double dist = 2 * dr * dr + 4 * dg * dg + 3 * db * db;   // rough eye weighting
                if (dist < bestD) { bestD = dist; best = L.S(e[1], e[2]); }
            }
            return best;
        }

        sealed class Row
        {
            public string Name;
            public bool Own;          // .3mf / G-code: its own colours
            public BackupForm.Tick Tick;
            public Swatch Swatch;
            public ComboBox Combo;
            public string Filament;   // short preset name
            public string Colour = "";
            public TextBox Copies;    // batch & plates: copies of this file (1..MaxCopies)
        }

        const int CopiesW = 44;   // the copies box (96 dpi units)
        InfoBox helpBox;
        string helpText;

        readonly List<Row> rows = new List<Row>();
        readonly string defaultFilament;
        readonly Func<List<string>> filamentNames;
        readonly Panel rowsPanel;
        readonly TableLayoutPanel grid;
        readonly BackupForm.Tick together;
        readonly Label togetherNote;
        readonly BackupForm.Tick share;   // batch-2: "Share the last plate between the colours" (copies in 2+ colours)
        readonly Label shareNote;
        bool shareWanted;                  // the tick as the user (or Settings) wants it; shown only while it can be used
        readonly ToolTip tip;
        Button ok;

        /// The picked rows (indexes into the names given), in order.
        public List<int> SelectedIndexes = new List<int>();
        /// The picked files' filament / colour (Parts[i].File = the name given; the caller puts the path in).
        public ColourPlan Plan;

        /// names = the files (display names); ticked = which start ticked (null = all); filamentNames = Bambu Studio's
        /// filament list (read when a drop-down opens).
        public FilePickerForm(string title, IList<string> names, IList<bool> ticked, bool printables, string defaultFilament,
                              Func<List<string>> filamentNames)
        {
            this.defaultFilament = string.IsNullOrEmpty(defaultFilament) ? "" : defaultFilament;
            this.filamentNames = filamentNames;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = L.S("Choose files and colours", "Vælg filer og farver");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            if (Program.Hidden) Screenshots.Place(this);   // CenterParent would clamp an off-screen dialog back onto the screen
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            tip = new ToolTip { OwnerDraw = true, InitialDelay = 400 };
            tip.Draw += MainForm.DrawDarkTip;

            for (int i = 0; i < names.Count; i++)
                rows.Add(new Row { Name = names[i] ?? "", Own = ColourPlan.OwnColours(names[i]), Filament = this.defaultFilament });
            int plain = rows.Count(r => !r.Own);

            // no Margin: the default 3 px made the window's right edge 17 px from the content instead of 14
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(14), Margin = Padding.Empty };
            Controls.Add(t);
            t.Controls.Add(new Label
            {
                Text = title, AutoSize = true, MaximumSize = new Size(ContentW, 0), UseMnemonic = false,
                Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4)
            });
            string help = plain > 0
                ? L.S("Tick the files you will print and pick a filament and colour for each. Files with the same filament and colour are added up as one colour.",
                      "Sæt flueben ved de filer, du vil printe, og vælg filament og farve til hver. Filer med samme filament og farve lægges sammen som én farve.")
                : L.S("Tick the parts you will print. Each file is sliced on its own and the times and weights are added up.",
                      "Sæt flueben ved de dele, du vil printe. Hver fil slices for sig, og tider og vægt lægges sammen.");
            if (printables)
                help += L.S("\n\nModels often include the same part as both STL and 3MF, so pick just one of those.",
                            "\n\nModeller har tit den samme del som både STL og 3MF, så vælg kun én af dem.");
            help += L.S("\n× = copies of each file (as many as fit go on each plate).",   // ui-text #14
                        "\n× = stk. af hver fil (så mange som muligt på hver plade).");
            helpText = help;
            helpBox = new InfoBox(ContentW, 9.5f) { Text = help, Margin = new Padding(0, 2, 0, 10) };
            t.Controls.Add(helpBox);

            // rows: tick + file name | colour swatch | filament drop-down | × copies (28 px each; more than 8 scroll)
            grid = new TableLayoutPanel { ColumnCount = 5, RowCount = rows.Count, AutoSize = false, Margin = Padding.Empty, Location = Point.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, RowH));
                r.Tick = new BackupForm.Tick { Text = r.Name, Dock = DockStyle.Fill, Margin = Padding.Empty,
                                               Checked = ticked == null || (i < ticked.Count && ticked[i]) };
                r.Tick.AccessibleName = r.Name;
                r.Tick.CheckedChanged += delegate { UpdateState(); };
                r.Tick.TabIndex = i * 4;   // qol #10: Tab goes tick -> colour -> filament -> copies, row by row
                grid.Controls.Add(r.Tick, 0, i);
                // batch & plates: "× [1]" copies of this file (muted ×, a 44 px number box; dark tooltip says what it is);
                // qol #7: grayscale text (no ClearType colour fringes on the thin ×)
                var times = new Theme.PlainLabel { Text = "×", AutoSize = true, Tag = "muted", Anchor = AnchorStyles.Left, UseMnemonic = false,
                                                   Margin = new Padding(10, 0, 4, 0) };
                grid.Controls.Add(times, 3, i);
                r.Copies = new TextBox { Width = CopiesW, Text = "1", TextAlign = HorizontalAlignment.Right, MaxLength = 3,
                                         Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = Padding.Empty, TabIndex = i * 4 + 3,
                                         AccessibleName = L.F("Copies of {0}", "Antal af {0}", r.Name) };
                var box = r.Copies;
                r.Copies.TextChanged += delegate { UpdateState(); };
                r.Copies.Enter += delegate { BeginInvoke((MethodInvoker)box.SelectAll); };
                grid.Controls.Add(r.Copies, 4, i);
                tip.SetToolTip(r.Copies, L.S("How many copies of this file", "Hvor mange stk. af denne fil"));
                if (r.Own)
                {
                    var own = new Label { Text = L.S("colours from the file", "farver fra filen"), AutoSize = true, Tag = "muted",
                                          // left 5: the text's first pixel lines up with the swatches' left edge (8 px margin)
                                          Anchor = AnchorStyles.Left, Margin = new Padding(5, 0, 0, 0), UseMnemonic = false };
                    grid.Controls.Add(own, 1, i);
                    grid.SetColumnSpan(own, 2);
                    continue;
                }
                var row = r;
                r.Swatch = new Swatch { Size = new Size(18, 18), Clickable = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 8, 0), TabIndex = i * 4 + 1,
                                        AccessibleName = L.F("Colour of {0}", "Farve på {0}", r.Name) };
                r.Swatch.Click += delegate { ShowColourMenu(row); };
                grid.Controls.Add(r.Swatch, 1, i);
                r.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = ComboW, MaxDropDownItems = 12,
                                         Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = Padding.Empty, TabIndex = i * 4 + 2,
                                         AccessibleName = L.F("Filament of {0}", "Filament til {0}", r.Name) };
                r.Combo.Items.Add(r.Filament);
                r.Combo.SelectedIndex = 0;
                r.Combo.Enter += delegate { FillList(row); };
                r.Combo.DropDown += delegate { FillList(row); };
                r.Combo.SelectedIndexChanged += delegate
                {
                    var name = row.Combo.SelectedItem as string;
                    if (name != null) row.Filament = name;
                    UpdateState();
                };
                grid.Controls.Add(r.Combo, 2, i);
                tip.SetToolTip(r.Swatch, L.S("Click to pick the colour", "Klik for at vælge farven"));
            }
            rowsPanel = new Panel { Margin = Padding.Empty, AutoScroll = rows.Count > MaxRows, Width = ContentW, Height = Math.Min(rows.Count, MaxRows) * RowH };
            rowsPanel.Controls.Add(grid);
            rowsPanel.HandleCreated += delegate { try { SetWindowTheme(rowsPanel.Handle, "DarkMode_Explorer", null); } catch { } };
            t.Controls.Add(rowsPanel);

            together = new BackupForm.Tick
            {
                Text = L.S("Print together on one plate (AMS lite)", "Print sammen på én plade (AMS lite)"),
                Size = new Size(ContentW, 26), Margin = new Padding(0, 10, 0, 0), Visible = plain >= 2
            };
            together.CheckedChanged += delegate { UpdateState(); };
            t.Controls.Add(together);
            togetherNote = new Label
            {
                Text = L.F("Much slower and uses more filament (colour changes): in tests about 4× the time and 3.7× the filament. Needs 2–{0} different colours.",
                           "Meget langsommere og bruger mere filament (farveskift): i test ca. 4× tiden og 3,7× filamentet. Kræver 2–{0} forskellige farver.",
                           BambuSlicer.MaxTogetherColours),
                AutoSize = true, Tag = "muted", Font = new Font("Segoe UI", 9f), UseMnemonic = false,
                MaximumSize = new Size(ContentW - BackupForm.Tick.TextX, 0), Margin = new Padding(BackupForm.Tick.TextX, 0, 0, 0), Visible = plain >= 2
            };
            togetherText = togetherNote.Text;
            t.Controls.Add(togetherNote);
            // batch-2: copies in 2+ colours: full plates keep one colour, the colours' part-full last plates share one plate
            share = new BackupForm.Tick
            {
                Text = L.S("Share the last plate between the colours (AMS lite)", "Del den sidste plade mellem farverne (AMS lite)"),
                Size = new Size(ContentW, 26), Margin = new Padding(0, 6, 0, 0), Visible = plain >= 2
            };
            share.CheckedChanged += delegate { if (share.Enabled && !updating) shareWanted = share.Checked; };
            t.Controls.Add(share);
            shareNote = new Label
            {
                Text = L.F("Full plates keep one colour; each colour's last plate goes on one shared plate. Fewer plates, but slower (colour changes). STL and OBJ, 2–{0} colours.",
                           "Fulde plader har én farve; hver farves sidste plade kommer på én fælles plade. Færre plader, men langsommere (farveskift). STL og OBJ, 2–{0} farver.",
                           BambuSlicer.MaxTogetherColours),
                AutoSize = true, Tag = "muted", Font = new Font("Segoe UI", 9f), UseMnemonic = false,
                MaximumSize = new Size(ContentW - BackupForm.Tick.TextX, 0), Margin = new Padding(BackupForm.Tick.TextX, 0, 0, 0), Visible = plain >= 2
            };
            t.Controls.Add(shareNote);

            // buttons 32 px high like the other windows, 6 px apart, Cancel's right edge = the drop-downs' right edge;
            // the top margin evens the gap under the note with the gaps above (info box -> rows, rows -> "Print together");
            // batch-2 qol #3: at least 16 px clear under the share note's last line
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, plain >= 2 ? 16 : 7, 0, 0) };
            Func<string, Button> button = text => new Button { Text = text, AutoSize = true, MinimumSize = new Size(75, 32), Margin = new Padding(6, 0, 0, 0) };
            ok = button(L.S("Slice selected", "Slice valgte"));
            var cancel = button(L.S("Cancel", "Annullér"));
            cancel.DialogResult = DialogResult.Cancel;
            var all = button(L.S("All", "Alle"));
            var none = button(L.S("None", "Ingen"));
            // added right to left, but Tab goes in reading order: All, None, Slice selected, Cancel
            all.TabIndex = 0; none.TabIndex = 1; ok.TabIndex = 2; cancel.TabIndex = 3;
            ok.Click += delegate { Accept(); };
            all.Click += delegate { SetAll(true); };
            none.Click += delegate { SetAll(false); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(none);
            buttons.Controls.Add(all);
            t.Controls.Add(buttons);

            AcceptButton = ok;
            CancelButton = cancel;
            Theme.Apply(this);
            ResumeLayout(true);
            UpdateState();
            Disposed += delegate { tip.Dispose(); };
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        int Px(float v) { return (int)Math.Round(v * DeviceDpi / 96f); }

        protected override void OnLoad(EventArgs e)
        {
            // real px: rows 28, the drop-downs centred by their real height, the panel 8 rows at most (then it scrolls),
            // file names cut with "…" (full name in a dark tooltip)
            int rowH = Math.Max(Px(RowH), rows.Where(r => r.Combo != null).Select(r => r.Combo.Height + Px(3)).DefaultIfEmpty(0).Max());
            int scroll = rows.Count > MaxRows ? SystemInformation.VerticalScrollBarWidth : 0;
            rowsPanel.SuspendLayout();
            grid.SuspendLayout();
            for (int i = 0; i < rows.Count; i++)
            {
                grid.RowStyles[i].SizeType = SizeType.Absolute;
                grid.RowStyles[i].Height = rowH;
                if (rows[i].Combo != null) rows[i].Combo.Margin = new Padding(0, Math.Max(0, (rowH - rows[i].Combo.Height) / 2), 0, 0);
                // the copies box like the drop-down: Top-anchored, centred by its real height (drop-down height when there is one)
                int boxH = rows[i].Combo != null ? rows[i].Combo.Height : rows[i].Copies.Height;
                rows[i].Copies.Margin = new Padding(0, Math.Max(0, (rowH - boxH) / 2), 0, 0);
            }
            grid.Size = new Size(rowsPanel.Width - scroll, rowH * rows.Count);
            rowsPanel.Height = rowH * Math.Min(rows.Count, MaxRows);
            grid.ResumeLayout(true);
            rowsPanel.ResumeLayout(true);
            foreach (var r in rows)
            {
                int room = r.Tick.Width - Px(BackupForm.Tick.TextX) - Px(4);
                string cut = Cut(r.Name, r.Tick.Font, room);
                r.Tick.Text = cut;
                r.Tick.AccessibleName = r.Name;
                if (cut != r.Name) tip.SetToolTip(r.Tick, r.Name);
            }
            // the help box never gets lower or narrower than its help text (a short error line doesn't move the rows;
            // qol #1: it kept its height but shrank to the error's width)
            helpBox.MinimumSize = new Size(helpBox.Width, helpBox.Height);
            // qol #5: the note under "together" also fits its "Not with copies above 1." version, so nothing moves
            if (togetherNote.Visible)
            {
                string shown = togetherNote.Text;
                togetherNote.Text = NoTogetherText + " " + togetherText;
                int h = togetherNote.GetPreferredSize(new Size(togetherNote.MaximumSize.Width, 0)).Height;
                togetherNote.Text = shown;
                togetherNote.MinimumSize = new Size(0, h);
            }
            base.OnLoad(e);
        }

        /// text cut to whole characters + "…" so it fits `width` px.
        static string Cut(string text, Font font, int width)
        {
            const TextFormatFlags f = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            if (width <= 0 || TextRenderer.MeasureText(text, font, Size.Empty, f).Width <= width) return text;
            int n = text.Length;
            while (n > 1 && TextRenderer.MeasureText(text.Substring(0, n).TrimEnd() + "…", font, Size.Empty, f).Width > width) n--;
            return text.Substring(0, n).TrimEnd() + "…";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override bool ShowWithoutActivation { get { return Program.Hidden || Screenshots.Active; } }

        /// Printables: prefer all STL files; if none, all 3MF files; otherwise everything.
        public static List<bool> DefaultTicks(IList<string> names)
        {
            Func<string, bool> stl = n => Path.GetExtension(n ?? "").ToLowerInvariant() == ".stl";
            Func<string, bool> mf = n => Path.GetExtension(n ?? "").ToLowerInvariant() == ".3mf";
            Func<string, bool> pick = names.Any(stl) ? stl : names.Any(mf) ? mf : (n => true);
            return names.Select(pick).ToList();
        }

        /// Fills a row's drop-down with Bambu Studio's filament list once, keeping its filament selected.
        void FillList(Row r)
        {
            if (r.Combo.Tag != null) return;
            r.Combo.Tag = "full";
            List<string> names;
            try { names = filamentNames != null ? filamentNames() : null; } catch { names = null; }
            var items = new List<string>(names ?? new List<string>());
            string match = items.FirstOrDefault(n => string.Equals(n, r.Filament, StringComparison.OrdinalIgnoreCase));
            if (match == null && r.Filament.Length > 0)
            {
                match = r.Filament;
                int at = items.FindIndex(n => string.Compare(n, r.Filament, StringComparison.OrdinalIgnoreCase) > 0);
                items.Insert(at < 0 ? items.Count : at, r.Filament);
            }
            int width = 0;
            foreach (var n in items) width = Math.Max(width, TextRenderer.MeasureText(n, r.Combo.Font).Width);
            r.Combo.BeginUpdate();
            r.Combo.Items.Clear();
            r.Combo.Items.AddRange(items.ToArray());
            r.Combo.SelectedIndex = match != null ? r.Combo.Items.IndexOf(match) : -1;
            r.Combo.EndUpdate();
            r.Combo.DropDownWidth = Math.Max(r.Combo.Width, width + SystemInformation.VerticalScrollBarWidth + r.Combo.Height / 2);
        }

        /// Dark menu of filament colours under the swatch (never Windows' light colour dialog).
        void ShowColourMenu(Row r)
        {
            var images = new List<Image>();
            var menu = ColourMenu(r, images);
            menu.Closed += delegate
            {
                BeginInvoke((MethodInvoker)delegate { menu.Dispose(); foreach (var i in images) i.Dispose(); });
            };
            menu.Show(r.Swatch, new Point(0, r.Swatch.Height));
        }

        /// Picture mode: filaments, colours and ticks for the sample files; together = tick "together on one plate".
        internal void SampleChoices(string[] filaments, string[] colours, bool[] ticks, bool together)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (ticks != null && i < ticks.Length) r.Tick.Checked = ticks[i];
                if (r.Own) continue;
                if (filaments != null && i < filaments.Length && !string.IsNullOrEmpty(filaments[i]))
                {
                    r.Filament = filaments[i];
                    r.Combo.Items.Clear();
                    r.Combo.Items.Add(r.Filament);
                    r.Combo.SelectedIndex = 0;
                }
                if (colours != null && i < colours.Length) SetColour(r, colours[i]);
            }
            this.together.Checked = together;
            UpdateState();
        }

        /// Picture mode: draws row `index`'s colour menu (never shown on screen there) under its swatch.
        internal void SampleColourMenu(int index, Graphics g, Point origin)
        {
            var r = rows[index];
            if (r.Swatch == null) return;
            var images = new List<Image>();
            using (var menu = ColourMenu(r, images))
            {
                menu.PerformLayout();
                var size = menu.GetPreferredSize(Size.Empty);
                menu.Size = size;
                using (var bmp = new Bitmap(size.Width, size.Height))
                {
                    menu.DrawToBitmap(bmp, new Rectangle(Point.Empty, size));
                    var p = r.Swatch.PointToScreen(new Point(0, r.Swatch.Height));
                    g.DrawImage(bmp, p.X - origin.X, p.Y - origin.Y);
                }
            }
            foreach (var i in images) i.Dispose();
        }

        ContextMenuStrip ColourMenu(Row r, List<Image> images)
        {
            int d = Px(14);
            var menu = new ContextMenuStrip { Renderer = new QuoteMenuRenderer(), ShowItemToolTips = false, BackColor = Theme.Input,
                                              ForeColor = Theme.Text, ImageScalingSize = new Size(d, d) };
            Action<string, string> add = (hex, name) =>
            {
                var bmp = Dot(d, MainForm.ParseColour(hex));
                images.Add(bmp);
                var mi = new ToolStripMenuItem(name, bmp) { ForeColor = Theme.Text, ImageScaling = ToolStripItemImageScaling.None };
                if (string.Equals(hex, r.Colour, StringComparison.OrdinalIgnoreCase)) mi.Font = new Font(menu.Font, FontStyle.Bold);
                string chosen = hex;
                mi.Click += delegate { SetColour(r, chosen); };
                menu.Items.Add(mi);
            };
            add("", L.S("The filament's own colour", "Filamentets egen farve"));
            menu.Items.Add(new ToolStripSeparator());
            foreach (var c in FilamentColours) add(c[0], L.S(c[1], c[2]));
            return menu;
        }

        /// A round colour dot with a Muted ring (dark filaments show on the dark menu); empty colour = ring only.
        static Bitmap Dot(int d, Color c)
        {
            var bmp = new Bitmap(d, d);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (!c.IsEmpty) using (var b = new SolidBrush(c)) g.FillEllipse(b, 1, 1, d - 3, d - 3);
                using (var p = new Pen(Theme.Muted)) g.DrawEllipse(p, 1, 1, d - 3, d - 3);
            }
            return bmp;
        }

        void SetColour(Row r, string hex)
        {
            r.Colour = hex ?? "";
            r.Swatch.Colours = new[] { MainForm.ParseColour(r.Colour) };
            UpdateState();
        }

        void SetAll(bool value)
        {
            foreach (var r in rows) r.Tick.Checked = value;
            UpdateState();
        }

        /// Slice selected: at least one file; "together": 2+ ticked plain files with 2..MaxTogetherColours colours.
        void UpdateState()
        {
            if (ok == null) return;
            // batch & plates: every ticked file 1..MaxCopies copies, all together at most MaxCopies (bad = red box + error)
            bool copiesOk = true, anyBatch = false;
            long sum = 0;
            foreach (var r in rows)
            {
                int n;
                bool good = TryCopies(r, out n);
                r.Copies.BackColor = good || !r.Tick.Checked ? Theme.Input : Theme.InvalidBack;
                if (!r.Tick.Checked) continue;
                if (!good) copiesOk = false;
                else { sum += n; if (n > 1) anyBatch = true; }
            }
            bool tooMany = sum > BambuSlicer.MaxCopies;
            if (tooMany)
            {
                copiesOk = false;
                // qol #3: over the total, the ticked boxes above 1 turn red (each alone is fine, together too many)
                foreach (var r in rows)
                {
                    int n;
                    if (r.Tick.Checked && TryCopies(r, out n) && n > 1) r.Copies.BackColor = Theme.InvalidBack;
                }
            }
            if (helpBox != null)
            {
                // ui-text #6 (+ qol #3: the total now)
                int ticked = rows.Count(r => r.Tick.Checked);
                string text = copiesOk ? helpText
                    : tooMany ? L.F("All {1} files together: 1 to {0} copies (now {2}).", "Alle {1} filer tilsammen: 1 til {0} stk. (nu {2}).", BambuSlicer.MaxCopies, ticked, sum)
                    : BambuSlicer.CopiesRangeText();
                if (helpBox.Text != text) { helpBox.Text = text; helpBox.Kind = copiesOk ? InfoKind.Info : InfoKind.Error; }
            }
            // qol #11: an unticked file's copies box is greyed out (not used)
            foreach (var r in rows) r.Copies.Enabled = r.Tick.Checked;
            ok.Enabled = rows.Any(r => r.Tick.Checked) && copiesOk;
            var plain = rows.Where(r => r.Tick.Checked && !r.Own).ToList();
            int colours = plain.Select(Key).Distinct().Count();
            // copies are placed by colour on their own plates: "together on one plate" is for one of each
            together.Enabled = plain.Count >= 2 && colours >= 2 && colours <= BambuSlicer.MaxTogetherColours && !anyBatch;
            // greyed out = not used (Accept ignores it), so it doesn't keep showing a tick
            if (!together.Enabled && together.Checked) together.Checked = false;
            // batch-2: sharing the last plate is for copies in 2+ colours (greyed out = unticked, the wish is kept)
            int shareColours = plain.Where(r => BambuSlicer.CanShareFile(r.Name)).Select(Key).Distinct().Count();
            bool canShare = anyBatch && shareColours >= 2;
            updating = true;
            share.Enabled = canShare;
            share.Checked = canShare && shareWanted;
            updating = false;
            // qol #5: say why it is greyed out when copies are the reason
            if (togetherNote != null && togetherText != null)
            {
                string note = anyBatch ? NoTogetherText + " " + togetherText : togetherText;
                if (togetherNote.Text != note) togetherNote.Text = note;
            }
        }

        string togetherText;   // the note under "together" as built
        bool updating;         // UpdateState sets the share tick (not the user's wish)

        /// batch-2: the share tick's start value (Settings' ShareLastPlate); shown ticked once it can be used.
        internal bool ShareDefault
        {
            get { return shareWanted; }
            set { shareWanted = value; UpdateState(); }
        }
        static string NoTogetherText { get { return L.S("Not with copies above 1.", "Ikke med mere end 1 stk."); } }

        static string Key(Row r) { return (r.Filament ?? "").ToLowerInvariant() + "|" + (r.Colour ?? "").ToUpperInvariant(); }

        /// The row's copies box: a whole number 1..MaxCopies.
        static bool TryCopies(Row r, out int n)
        {
            n = 1;
            if (r.Copies == null) return true;
            return int.TryParse((r.Copies.Text ?? "").Trim(), System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out n) && BambuSlicer.CopiesInRange(n);
        }

        /// Set by tests: the "may take several minutes" answer (null = the real question window).
        internal bool? TestBigAnswer;

        void Accept()
        {
            SelectedIndexes = new List<int>();
            var parts = new List<HistoryPart>();
            var copies = new List<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].Tick.Checked) continue;
                int n;
                if (!TryCopies(rows[i], out n)) return;
                SelectedIndexes.Add(i);
                copies.Add(n);
                parts.Add(new HistoryPart { File = rows[i].Name, Filament = rows[i].Own ? "" : rows[i].Filament, Colour = rows[i].Own ? "" : rows[i].Colour, Copies = n });
            }
            if (SelectedIndexes.Count == 0) return;
            int total = copies.Sum();
            if (total > BambuSlicer.MaxCopies) return;
            if (copies.Any(c => c > 1) && total > BambuSlicer.WarnCopies)
            {
                // big batches: ask first ("may take several minutes, go on?")
                bool go;
                if (TestBigAnswer.HasValue) go = TestBigAnswer.Value;
                else using (var ask = CopiesForm.NewBigAsk(total)) go = ask.ShowDialog(this) == DialogResult.Yes;
                if (!go) return;
            }
            Plan = ColourPlan.Build(parts, together.Visible && together.Enabled && together.Checked);
            Plan.Copies = copies.Any(c => c > 1) ? copies : null;
            Plan.Share = share.Visible && share.Enabled && share.Checked;   // batch-2
            DialogResult = DialogResult.OK;
            Close();
        }

        /// Tests: the real "Slice selected" button (shown first); true = the window ended with OK and Plan.
        internal bool TestPressOk(IWin32Window owner)
        {
            if (!Visible) Show(owner);
            Application.DoEvents();
            ok.PerformClick();
            Application.DoEvents();
            return DialogResult == DialogResult.OK;
        }
        internal bool TestOkOn { get { return ok.Enabled; } }
        internal bool TestTogetherOn { get { return together.Enabled; } }
        internal string TestInfoText { get { return helpBox != null ? helpBox.Text ?? "" : ""; } }
        internal void TestTickTogether() { together.Checked = true; }
        internal bool TestShareOn { get { return share.Enabled; } }
        internal bool TestShareTicked { get { return share.Checked; } }
        internal void TestTickShare(bool on) { share.Checked = on; }

        /// Picture mode / tests: the copies typed into each row's box (null entries = left as they are).
        internal void SampleCopies(int[] copies)
        {
            for (int i = 0; i < rows.Count && copies != null && i < copies.Length; i++)
                if (copies[i] > 0) rows[i].Copies.Text = copies[i].ToString();
            UpdateState();
        }
    }
}
