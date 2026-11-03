using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Hidden picture mode: "PrintPrice.exe --screenshots <folder>" draws the app's windows off-screen
    /// into PNGs and exits. Uses default settings in memory (settings.ini is never read or written)
    /// and never goes online (no power price, no update check). The windows are never activated.
    static class Screenshots
    {
        /// True while picture mode runs: forms show without taking focus, nothing downloads.
        public static bool Active;

        public static int Run(string folder)
        {
            Active = true;
            try
            {
                Directory.CreateDirectory(folder);
                var s = new AppSettings();   // defaults only; never Load() or Save()
                var fake = FakeUpdate();

                using (var main = new MainForm(s, true))
                {
                    Place(main);
                    main.Show();
                    main.SetSampleJob();   // Benchy example with a sample live price
                    Snap(main, folder, "main.png");
                    main.ShowUpdateButton(fake);
                    Snap(main, folder, "main_update.png");

                    using (var set = new SettingsForm(s))
                    {
                        Place(set);
                        set.Show();
                        Snap(set, folder, "settings.png");
                        set.Hide();
                    }

                    using (var up = new UpdateForm(fake, null))
                    {
                        Place(up);
                        up.Show();
                        Pump();
                        up.ScrollNotes(false);
                        Snap(up, folder, "update_top.png");
                        up.ScrollNotes(true);
                        Snap(up, folder, "update_end.png");
                        up.Hide();
                    }
                    main.Hide();
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString()); } catch { }
                return 1;
            }
        }

        /// A made-up update three versions ahead, with notes long enough to scroll.
        static UpdateInfo FakeUpdate()
        {
            var c = Updater.Current;
            Func<int, string> v = n => c.Major + "." + c.Minor + "." + (Math.Max(0, c.Build) + n);
            string notes =
                "## v" + v(3) + "\n- Example note: the wear rate is shown under both prices\n- Example note: clearer Settings sections\n- Example note: faster start\n\n" +
                "## v" + v(2) + "\n- Example note: new build plates in the list\n- Example note: better slicer detection\n- Example note: small layout fixes\n\n" +
                "## v" + v(1) + "\n- Example note: live electricity price retries once\n- Example note: copy quote includes the model link\n- Example note: last line of the notes";
            return new UpdateInfo
            {
                Version = new Version(c.Major, c.Minor, Math.Max(0, c.Build) + 3),
                Tag = "v" + v(3), Title = "v" + v(3), Notes = notes
            };
        }

        /// Left of every monitor, so nothing ever appears on screen, and not in the taskbar.
        static void Place(Form f)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.ShowInTaskbar = false;
            Rectangle all = SystemInformation.VirtualScreen;
            f.Location = new Point(all.Left - 4000, all.Top);
        }

        static void Pump()
        {
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(40); }
        }

        static void Snap(Form f, string folder, string name)
        {
            Pump();
            f.Refresh();
            Pump();
            using (var bmp = Capture(f)) bmp.Save(Path.Combine(folder, name), ImageFormat.Png);
        }

        /// PrintWindow with PW_RENDERFULLCONTENT (also draws the RichTextBox and the dark title bar),
        /// cropped to the visible frame (without the invisible resize borders). Falls back to DrawToBitmap.
        static Bitmap Capture(Form f)
        {
            RECT wr;
            GetWindowRect(f.Handle, out wr);
            RECT fr;
            if (DwmGetWindowAttribute(f.Handle, 9, out fr, Marshal.SizeOf(typeof(RECT))) != 0) fr = wr;   // DWMWA_EXTENDED_FRAME_BOUNDS
            int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
            var full = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bool ok;
            using (var g = Graphics.FromImage(full))
            {
                IntPtr hdc = g.GetHdc();
                ok = PrintWindow(f.Handle, hdc, 2);
                g.ReleaseHdc(hdc);
            }
            if (!ok)
            {
                full.Dispose();
                var b = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                return b;
            }
            var crop = new Rectangle(fr.Left - wr.Left, fr.Top - wr.Top, fr.Right - fr.Left, fr.Bottom - fr.Top);
            crop.Intersect(new Rectangle(0, 0, w, h));
            if (crop.Width <= 0 || crop.Height <= 0) return full;
            var cut = full.Clone(crop, PixelFormat.Format32bppArgb);
            full.Dispose();
            return cut;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
    }
}
