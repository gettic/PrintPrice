using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintPrice
{
    /// "A new version is available" dialog: shows the release notes, downloads, verifies and installs the update.
    /// DialogResult.OK means the new version has been started and the program should exit now.
    public class UpdateForm : Form
    {
        readonly UpdateInfo update;
        readonly Action beforeRestart;
        readonly InfoBox status;
        readonly Button btnUpdate, btnLater, btnPage;
        CancellationTokenSource cts;

        public UpdateForm(UpdateInfo update, Action beforeRestart)
        {
            this.update = update;
            this.beforeRestart = beforeRestart;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = "Update available";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(16, 12, 16, 14) };
            Controls.Add(t);
            t.Controls.Add(new Label
            {
                Text = "Version " + Updater.Short(update.Version) + " is available",
                AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2)
            });
            t.Controls.Add(new Label { Text = "You have version " + Updater.CurrentText + ".", AutoSize = true, Tag = "muted", Margin = new Padding(0, 0, 0, 10) });

            string notes = Updater.NotesText(update, 900);
            if (notes.Length > 0)
            {
                t.Controls.Add(new Label { Text = "What's new", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) });
                t.Controls.Add(new InfoBox(440, 9f) { Text = notes, Margin = new Padding(0, 2, 0, 8) });
            }

            status = new InfoBox(440, 9.5f) { Margin = new Padding(0, 2, 0, 8) };
            t.Controls.Add(status);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            btnLater = new Button { Text = "Later", AutoSize = true, DialogResult = DialogResult.Cancel };
            btnUpdate = new Button { Text = "Update now", AutoSize = true };
            btnPage = new Button { Text = "Open download page", AutoSize = true, Visible = false };
            btnUpdate.Click += delegate { UpdateNow(); };
            btnPage.Click += delegate { OpenPage(); };
            buttons.Controls.Add(btnLater);
            buttons.Controls.Add(btnUpdate);
            buttons.Controls.Add(btnPage);
            t.Controls.Add(buttons);

            AcceptButton = btnUpdate;
            CancelButton = btnLater;
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (cts != null && DialogResult != DialogResult.OK) cts.Cancel();
            };
            Theme.Apply(this);
            ResumeLayout(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        void SetStatus(string text, InfoKind kind)
        {
            status.Kind = kind;
            status.Text = text;
        }

        async void UpdateNow()
        {
            btnUpdate.Enabled = false;
            btnPage.Visible = false;
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            var progress = new Progress<int>(p => SetStatus("Downloading… " + p + " %", InfoKind.Info));
            SetStatus("Downloading…", InfoKind.Info);
            try
            {
                string file = await Task.Run(() => Updater.Download(update, progress, ct));
                SetStatus("✓ Download verified. Restarting…", InfoKind.Success);
                if (beforeRestart != null) beforeRestart();
                Updater.InstallAndRestart(file);
                DialogResult = DialogResult.OK;   // caller exits the program
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                string msg = ex is UpdateException ? ex.Message : "The update failed (" + ex.Message + ").";
                SetStatus(msg + "\nYour current version is unchanged.", InfoKind.Error);
                btnUpdate.Text = "Try again";
                btnUpdate.Enabled = true;
                btnPage.Visible = true;
            }
        }

        void OpenPage()
        {
            try { Process.Start(update.PageUrl ?? Updater.ReleasesPage); } catch { }
        }
    }
}
