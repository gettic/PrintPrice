using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Lets the user tick which of a model's files to slice.
    public class FilePickerForm : Form
    {
        readonly CheckedListBox list;
        readonly List<RemoteFile> files;
        Button ok;
        public List<RemoteFile> Selected = new List<RemoteFile>();

        public FilePickerForm(string modelName, List<RemoteFile> files)
        {
            this.files = files;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = "Choose files to print";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(14) };
            Controls.Add(t);
            t.Controls.Add(new Label
            {
                Text = modelName, AutoSize = true, MaximumSize = new Size(460, 0),
                Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4)
            });
            t.Controls.Add(new InfoBox(460, 9.5f)
            {
                Text = "Tick the parts you will print. Each file is sliced on its own and the times and weights are added up.\n\n" +
                       "Models often include the same part as both STL and 3MF, so pick just one of those.",
                Margin = new Padding(0, 2, 0, 10)
            });

            list = new CheckedListBox
            {
                CheckOnClick = true, Width = 460, IntegralHeight = false,
                Height = Math.Min(Math.Max(files.Count, 3), 12) * 22 + 8
            };
            var defaults = DefaultSelection(files);
            foreach (var f in files) list.Items.Add(f, defaults.Contains(f));
            list.ItemCheck += delegate { BeginInvoke((MethodInvoker)UpdateOk); };
            t.Controls.Add(list);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            ok = new Button { Text = "Slice selected", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var all = new Button { Text = "All", AutoSize = true };
            var none = new Button { Text = "None", AutoSize = true };
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
            UpdateOk();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        /// Prefer all STL files; if none, all 3MF files; otherwise everything.
        static HashSet<RemoteFile> DefaultSelection(List<RemoteFile> files)
        {
            Func<string, List<RemoteFile>> byExt = ext => files.Where(f => Path.GetExtension(f.Name).ToLowerInvariant() == ext).ToList();
            var pick = byExt(".stl");
            if (pick.Count == 0) pick = byExt(".3mf");
            if (pick.Count == 0) pick = files;
            return new HashSet<RemoteFile>(pick);
        }

        void SetAll(bool value)
        {
            for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, value);
            UpdateOk();
        }

        void UpdateOk()
        {
            ok.Enabled = list.CheckedItems.Count > 0;
        }

        void Accept()
        {
            Selected = list.CheckedItems.Cast<RemoteFile>().ToList();
            if (Selected.Count == 0) return;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
