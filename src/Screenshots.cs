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
    /// into PNGs and exits (main_empty.png, main.png, main_payback.png, main_multi.png, quote_light.png, quote_dark.png, invoice_light.png, quote_window.png, main_reopened.png, repeat.png, history.png, backup*.png / restore*.png (Backup window), ..., several printers: printers_one.png, printers.png, printers_focus.png, printers_many.png, settings_printers.png, settings_add_menu.png, history_printers.png, main_printer_changed.png; a model.png / model2.png in the folder is used as
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
                    // failed prints payback on, with a made-up debt (nothing is saved): the extra Cost breakdown row
                    main.SetSamplePayback(33.70);
                    Snap(main, folder, "main_payback.png");
                    main.SetSamplePayback(0);   // back to the defaults (no debt) for the other pictures
                    // 4-colour Benchy: one row per colour in the Cost breakdown; picture = <folder>\model2.png if there
                    main.SetSampleMultiJob();
                    main.SetSamplePicture(Path.Combine(folder, "model2.png"));
                    Snap(main, folder, "main_multi.png");
                    // quote export: the customer quote page of the 4-colour example (A4 at 150 dpi): light without a
                    // customer name, dark with one; then the "Show quote…" window (made-up customer, nothing is saved)
                    foreach (bool dark in new[] { false, true })
                        using (var q = main.RenderSampleQuote(dark, dark ? "Mette Hansen" : ""))
                            if (q != null) q.Save(Path.Combine(folder, dark ? "quote_dark.png" : "quote_light.png"), ImageFormat.Png);
                    // the invoice page of the same job (sample code, customer and payment ways)
                    using (var inv = main.RenderSampleInvoice())
                        if (inv != null) inv.Save(Path.Combine(folder, "invoice_light.png"), ImageFormat.Png);
                    using (var qf = main.SampleQuoteWindow("Mette Hansen"))
                        if (qf != null)
                        {
                            Place(qf);
                            qf.Show();
                            Snap(qf, folder, "quote_window.png");
                            qf.Hide();
                        }
                    // sliced G-code files put in the folder (idea 14): main_gcode_<name>.png each, read as a dropped file would be
                    foreach (string g in Directory.GetFiles(folder, "*.gcode"))
                    {
                        string shot = "main_gcode_" + Path.GetFileNameWithoutExtension(g) + ".png";
                        try { main.SampleSlicedFile(g); }
                        catch (Exception ex) { main.SampleError(ex.Message); }   // e.g. no time and weight: the error box
                        Snap(main, folder, shot);
                    }
                    // price history: made-up quotes in memory (history.json is never read or written here)
                    var quotes = HistoryForm.SampleQuotes();
                    main.SetSampleJob();
                    main.Reopen(quotes[0]);   // "↻ Opened from history" + saved price next to today's
                    main.SetSamplePicture(sample);
                    Snap(main, folder, "main_reopened.png");
                    // "Repeat print" of the 4-colour quote: today's prices, no slicing in picture mode
                    main.SetSampleJob();
                    main.SampleRepeat(quotes[1]);
                    main.SetSamplePicture(Path.Combine(folder, "model2.png"));
                    Snap(main, folder, "repeat.png");
                    using (var hist = new HistoryForm(quotes, null, true))
                    {
                        hist.QuoteRequested = (owner, rec, isInvoice) => { };   // shows Quote… / Invoice… (never clicked here)
                        hist.BackupRequested = owner => false;                  // shows Backup… (never clicked here)
                        Place(hist);
                        hist.Show();
                        hist.SelectMonth(new DateTime(2026, 10, 1));   // October: billed, failed and not-printed quotes
                        Snap(hist, folder, "history.png");
                        hist.Hide();
                    }
                    // the Backup window (idea 27) with made-up states: nothing is read, written or sent
                    // (a copy of the settings: the sample code is set on it; nothing goes online, nothing is saved)
                    using (var bf = new BackupForm(quotes, s.Clone(), BackupForm.TabBackup, true))
                    {
                        Place(bf);
                        bf.Show();
                        Snap(bf, folder, "backup.png");                // to a file (warning: not locked)
                        bf.SampleOnline(false);
                        Snap(bf, folder, "backup_online.png");         // online, no code yet ("none yet")
                        bf.SampleOnline(true);
                        Snap(bf, folder, "backup_online_code.png");    // a remembered code: Show code / New code… / Delete online backup…
                        bf.SampleCode();
                        Snap(bf, folder, "backup_code.png");           // the code page after an upload, "2 online backups left today"
                        bf.SampleCountdown();
                        Snap(bf, folder, "backup_countdown.png");      // daily cap reached: countdown in place of the button
                        bf.SelectedTab = BackupForm.TabRestore;
                        Snap(bf, folder, "restore_empty.png");         // nothing chosen yet
                        bf.SampleLoad(false);
                        Snap(bf, folder, "restore.png");               // a file loaded, Merge + settings
                        bf.SampleLoad(true);
                        Snap(bf, folder, "restore_replace.png");       // Replace: warning
                        bf.SampleCodeRestore();
                        Snap(bf, folder, "restore_code.png");          // an online backup fetched with its code, "Keep backing up" ticked
                        bf.SampleDone();
                        Snap(bf, folder, "restore_done.png");          // the result
                        bf.Hide();
                    }
                    using (var ask = BackupForm.NewReplaceAsk(312))
                        SnapForm(ask, folder, "restore_ask.png");     // the question before Replace
                    // the dark error window (issue #2) with a made-up error: first time, keeps happening
                    var sampleError = ErrorReport.SampleError();
                    using (var err = new ErrorForm(sampleError, ErrorReport.Details(sampleError, false), false, false))
                        SnapForm(err, folder, "error_window.png");
                    using (var err = new ErrorForm(sampleError, ErrorReport.Details(sampleError, false), false, true))
                        SnapForm(err, folder, "error_repeated.png");
                    main.SetSampleJob();   // back to the single-colour example for the update picture
                    main.SetSamplePicture(sample);
                    main.ShowUpdateButton(fake);
                    Snap(main, folder, "main_update.png");

                    using (var set = new SettingsForm(s))
                    {
                        Place(set);
                        set.Show();
                        Snap(set, folder, "settings.png");
                        // the other tabs (settings.png = Prices)
                        set.SelectedTab = SettingsForm.TabPrinter;
                        Snap(set, folder, "settings_printer.png");
                        set.SelectedTab = SettingsForm.TabElectricity;
                        Snap(set, folder, "settings_electricity.png");
                        set.SelectedTab = SettingsForm.TabQuote;
                        Snap(set, folder, "settings_quote.png");
                        set.SelectedTab = SettingsForm.TabApp;
                        Snap(set, folder, "settings_app.png");
                        set.SelectedTab = SettingsForm.TabPrices;   // settings_found.png: the "check now" text, on the first tab
                        // longest "check now" result text, to check the bottom row still fits (no real check)
                        set.SetCheckText(SettingsForm.FoundText(fake), null, Theme.Text);
                        Snap(set, folder, "settings_found.png");
                        set.Hide();
                    }
                    // Settings with "Earn back failed prints" ticked and a made-up debt (copy of the defaults, never saved)
                    var payback = s.Clone();
                    payback.FailedPayback = 1;
                    using (var set = new SettingsForm(payback, 33.05))
                    {
                        Place(set);
                        set.Show();
                        Snap(set, folder, "settings_payback.png");
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

                    // several printers (copies in memory, never saved): Choose printer with 1, 3 and 7 printers, the
                    // Printer tab, History with the Printer column, then the main window after switching printer
                    var month = new DateTime(2026, 10, 20);   // the sample quotes' month: tiles show their prints / profit
                    var one = HistoryForm.SampleQuotes();
                    using (var pick = new PrinterPickerForm(s, one, month))
                        SnapForm(pick, folder, "printers_one.png");
                    var multi = s.Clone();
                    var mini = multi.AddPrinter("Kitchen mini", Wear.ModelA1Mini);
                    var p1s = multi.AddPrinter("", Wear.ModelP1S);
                    var pq = HistoryForm.SampleQuotes();
                    for (int i = 0; i < pq.Count; i++)
                    {
                        var p = i % 3 == 1 ? mini : i % 3 == 2 ? p1s : multi.FindPrinter("1");
                        pq[i].PrinterId = p.Id;
                        pq[i].PrinterName = p.Name;
                        pq[i].PrinterModel = p.Model;
                    }
                    using (var pick = new PrinterPickerForm(multi, pq, month))
                    {
                        SnapForm(pick, folder, "printers.png");
                        pick.Show();
                        pick.SampleFocus(1);   // keyboard focus on the second tile
                        Snap(pick, folder, "printers_focus.png");
                        pick.Hide();
                    }
                    var many = multi.Clone();
                    many.AddPrinter("A1 #2 (garage)", Wear.ModelA1);
                    many.AddPrinter("Mini for PETG", Wear.ModelA1Mini);
                    many.AddPrinter("P1S #2", Wear.ModelP1S);
                    many.AddPrinter("Spare A1", Wear.ModelA1);
                    using (var pick = new PrinterPickerForm(many, pq, month))
                    {
                        Place(pick);
                        pick.Show();
                        pick.SampleScroll(150);   // partway down
                        Snap(pick, folder, "printers_many.png");
                        pick.Hide();
                    }
                    var setCopy = multi.Clone();
                    var longName = setCopy.AddPrinter("", Wear.ModelA1Mini);   // "Bambu Lab A1 mini": the longest model name
                    using (var set = new SettingsForm(setCopy))
                    {
                        set.ShowPrinter(longName.Id, false);
                        SnapForm(set, folder, "settings_printers.png");
                        // the Add… menu drawn over the window where it opens (a real menu would pop up on screen)
                        set.Show();
                        Snap(set, folder, "settings_add_menu.png", set.SampleAddMenu);
                        set.Hide();
                    }
                    using (var hist = new HistoryForm(pq, null, true))
                    {
                        hist.Settings = multi;
                        hist.QuoteRequested = (owner, rec, isInvoice) => { };
                        hist.BackupRequested = owner => false;   // shows Backup… like the real window
                        Place(hist);
                        hist.Show();
                        hist.SelectMonth(new DateTime(2026, 10, 1));
                        Snap(hist, folder, "history_printers.png");
                        hist.Hide();
                    }
                    main.SetSampleJob();
                    main.SetSamplePicture(sample);
                    main.SampleSwitchPrinter(Wear.ModelA1Mini);   // card + warning stripe: sliced for the printer used before
                    Snap(main, folder, "main_printer_changed.png");
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
        internal static void Place(Form f)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.ShowInTaskbar = false;
            Rectangle all = SystemInformation.VirtualScreen;
            f.Location = new Point(all.Left - 4000, all.Top);
        }

        /// Shows a form off-screen, takes its picture and hides it again (also ErrorReport's --hidden test runs).
        internal static void SnapForm(Form f, string folder, string name)
        {
            Place(f);
            f.Show();
            Snap(f, folder, name);
            f.Hide();
        }

        static void Pump()
        {
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(40); }
        }

        internal static void Snap(Form f, string folder, string name)
        {
            Snap(f, folder, name, null);
        }

        /// draw: paints extra things (an open menu) over the picture; its Point is the picture's top left on screen.
        static void Snap(Form f, string folder, string name, Action<Graphics, Point> draw)
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
            if (draw != null)
            {
                RECT wr, fr;
                GetWindowRect(f.Handle, out wr);
                if (DwmGetWindowAttribute(f.Handle, 9, out fr, Marshal.SizeOf(typeof(RECT))) != 0) fr = wr;
                using (var g = Graphics.FromImage(bmp)) draw(g, new Point(fr.Left, fr.Top));
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
