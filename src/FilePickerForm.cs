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
                var c = new HistoryPart { File = p.File ?? "", Filament = (p.Filament ?? "").Trim(), Colour = (p.Colour ?? "").Trim().ToUpperInvariant(), Slot = 1 };
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
            return plan;
        }
    }

    /// Lets the user tick which files to slice and pick a filament and colour per plain model file (idea 21), plus
    /// "Print together on one plate (AMS lite)". Printables models with 2+ files and 2+ dropped / chosen local files.
    public class FilePickerForm : CaptionForm
    {
        const int ContentW = 520, RowH = 28, MaxRows = 8, ComboW = 190;   // 96 dpi units

        /// Common filament colours for the swatch menu (Bambu Lab PLA Basic shades). Filament data, not UI colours.
        static readonly string[][] FilamentColours =
        {
            new[] { "#000000", "Black", "Sort" }, new[] { "#FFFFFF", "White", "Hvid" }, new[] { "#8E9089", "Grey", "Grå" },
            new[] { "#C12E1F", "Red", "Rød" }, new[] { "#FF6A13", "Orange", "Orange" }, new[] { "#F4EE2A", "Yellow", "Gul" },
            new[] { "#00AE42", "Green", "Grøn" }, new[] { "#0A2989", "Blue", "Blå" }, new[] { "#0086D6", "Light blue", "Lyseblå" },
            new[] { "#5E43B7", "Purple", "Lilla" }, new[] { "#F55A74", "Pink", "Pink" }, new[] { "#9D432C", "Brown", "Brun" },
            new[] { "#F7E6DE", "Beige", "Beige" }
        };

        sealed class Row
        {
            public string Name;
            public bool Own;          // .3mf / G-code: its own colours
            public BackupForm.Tick Tick;
            public Swatch Swatch;
            public ComboBox Combo;
            public string Filament;   // short preset name
            public string Colour = "";
        }

        readonly List<Row> rows = new List<Row>();
        readonly string defaultFilament;
        readonly Func<List<string>> filamentNames;
        readonly Panel rowsPanel;
        readonly TableLayoutPanel grid;
        readonly BackupForm.Tick together;
        readonly Label togetherNote;
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
            t.Controls.Add(new InfoBox(ContentW, 9.5f) { Text = help, Margin = new Padding(0, 2, 0, 10) });

            // rows: tick + file name | colour swatch | filament drop-down (28 px each; more than 8 scroll)
            grid = new TableLayoutPanel { ColumnCount = 3, RowCount = rows.Count, AutoSize = false, Margin = Padding.Empty, Location = Point.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
                grid.Controls.Add(r.Tick, 0, i);
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
                r.Swatch = new Swatch { Size = new Size(18, 18), Clickable = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 8, 0),
                                        AccessibleName = L.F("Colour of {0}", "Farve på {0}", r.Name) };
                r.Swatch.Click += delegate { ShowColourMenu(row); };
                grid.Controls.Add(r.Swatch, 1, i);
                r.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = ComboW, MaxDropDownItems = 12,
                                         Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = Padding.Empty,
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
            t.Controls.Add(togetherNote);

            // buttons 32 px high like the other windows, 6 px apart, Cancel's right edge = the drop-downs' right edge;
            // the top margin evens the gap under the note with the gaps above (info box -> rows, rows -> "Print together")
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 7, 0, 0) };
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
            ok.Enabled = rows.Any(r => r.Tick.Checked);
            var plain = rows.Where(r => r.Tick.Checked && !r.Own).ToList();
            int colours = plain.Select(Key).Distinct().Count();
            together.Enabled = plain.Count >= 2 && colours >= 2 && colours <= BambuSlicer.MaxTogetherColours;
            // greyed out = not used (Accept ignores it), so it doesn't keep showing a tick
            if (!together.Enabled && together.Checked) together.Checked = false;
        }

        static string Key(Row r) { return (r.Filament ?? "").ToLowerInvariant() + "|" + (r.Colour ?? "").ToUpperInvariant(); }

        void Accept()
        {
            SelectedIndexes = new List<int>();
            var parts = new List<HistoryPart>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].Tick.Checked) continue;
                SelectedIndexes.Add(i);
                parts.Add(new HistoryPart { File = rows[i].Name, Filament = rows[i].Own ? "" : rows[i].Filament, Colour = rows[i].Own ? "" : rows[i].Colour });
            }
            if (SelectedIndexes.Count == 0) return;
            Plan = ColourPlan.Build(parts, together.Visible && together.Enabled && together.Checked);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
