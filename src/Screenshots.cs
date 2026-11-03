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
    /// into PNGs and exits (main_empty.png, main.png, main_multi.png, ...; a model.png / model2.png in the folder is used as
    /// the sample model picture of the single / 4-colour example). Add "--lang da" for Danish pictures (English otherwise, whatever the Windows language). Uses default settings in memory (settings.ini is never read or written)
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
                    Snap(main, folder, "main_empty.png");   // fresh window: logo + "No model yet"
                    main.SetSampleJob();   // Benchy example with a sample live price
                    // sample model picture: <folder>\model.png if it is there (e.g. a slicer plate_1.png), else the logo stays
                    string sample = Path.Combine(folder, "model.png");
                    main.SetSamplePicture(sample);
                    Snap(main, folder, "main.png");
                    // 4-colour Benchy: one row per colour in the Cost breakdown; picture = <folder>\model2.png if there
                    main.SetSampleMultiJob();
                    main.SetSamplePicture(Path.Combine(folder, "model2.png"));
                    Snap(main, folder, "main_multi.png");
                    main.SetSampleJob();   // back to the single-colour example for the update picture
                    main.SetSamplePicture(sample);
                    main.ShowUpdateButton(fake);
                    Snap(main, folder, "main_update.png");

                    using (var set = new SettingsForm(s))
                    {
                        Place(set);
                        set.Show();
                        Snap(set, folder, "settings.png");
                        // longest "check now" result text, to check the bottom row still fits (no real check)
                        set.SetCheckText(SettingsForm.FoundText(fake), null, Theme.Text);
                        Snap(set, folder, "settings_found.png");
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
            RedrawWindow(f.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0080 | 0x0100 | 0x0400);   // INVALIDATE|ALLCHILDREN|UPDATENOW|FRAME
            Pump();
            // PrintWindow's full-content copy comes from DWM and can lag behind a change made just before
            // (stale picture): wait for DWM to compose the new frame first
            for (int i = 0; i < 3; i++) { try { DwmFlush(); } catch { } Pump(); }
            // ...and keep capturing until two captures in a row are the same (up to ~3 s)
            Bitmap bmp = Capture(f);
            for (int i = 0; i < 10; i++)
            {
                Pump();
                try { DwmFlush(); } catch { }
                var next = Capture(f);
                bool same = SamePixels(bmp, next);
                bmp.Dispose();
                bmp = next;
                if (same) break;
            }
            using (bmp) bmp.Save(Path.Combine(folder, name), ImageFormat.Png);
        }

        static bool SamePixels(Bitmap a, Bitmap b)
        {
            if (a.Size != b.Size) return false;
            var r = new Rectangle(Point.Empty, a.Size);
            var da = a.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var db = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int n = da.Stride * a.Height;
                var pa = new byte[n]; var pb = new byte[n];
                Marshal.Copy(da.Scan0, pa, 0, n);
                Marshal.Copy(db.Scan0, pb, 0, n);
                for (int i = 0; i < n; i++) if (pa[i] != pb[i]) return false;
                return true;
            }
            finally { a.UnlockBits(da); b.UnlockBits(db); }
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
        [DllImport("user32.dll")]
        static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
        [DllImport("dwmapi.dll")]
        static extern int DwmFlush();
        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
    }
}
