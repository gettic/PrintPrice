using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Hidden picture mode: "PrintPrice.exe --screenshots <folder>" draws the app's windows off-screen
    /// into PNGs and exits (main_empty.png, main.png, main_payback.png, main_multi.png, quote_light.png, quote_dark.png, quote.pdf + quote_pdf.png, invoice_light.png, quote_window.png, main_invoice.png + invoice_window.png + settings_quote_invoice.png (Show invoice… setting),main_reopened.png, repeat.png, history.png, backup*.png / restore*.png (Backup window), ..., several printers: printers_one.png, printers.png, printers_focus.png, printers_many.png, settings_printers.png, settings_add_menu.png, history_printers.png, main_printer_changed.png; idea 21: main_parts.png, main_plates.png, main_plates_menu.png, main_makerworld.png, picker.png, picker_colour_menu.png; a model.png / model2.png in the folder is used as
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
                s.QuoteName = AppSettings.LegacyQuoteName;   // pictures keep "Gettic" (new users start with an empty name)
                var fake = FakeUpdate();

                using (var main = new MainForm(s, true))
                {
                    // sample pictures (assets\history-samples): model.png / model2.png fallbacks and saved records' pictures
                    main.SamplePictures = HistoryForm.SamplePictures(folder);
                    Place(main);
                    main.Show();
                    Snap(main, folder, "main_empty.png");   // fresh window: logo + "No model yet"
                    main.SampleSetupHint();   // idea 19: after "Set up later" (the next message removes it)
                    Snap(main, folder, "main_setup_hint.png");
                    main.SetSampleJob();   // Benchy example with a sample live price
                    // sample model picture: <folder>\model.png if it is there (e.g. a slicer plate_1.png), else the logo stays
                    string sample = Path.Combine(folder, "model.png");
                    main.SetSamplePicture(sample, "sample0");
                    Snap(main, folder, "main.png");
                    // failed prints payback on, with a made-up debt (nothing is saved): the extra Cost breakdown row
                    main.SetSamplePayback(33.70);
                    Snap(main, folder, "main_payback.png");
                    main.SetSamplePayback(0);   // back to the defaults (no debt) for the other pictures
                    // issue #8: the Scale window on the Benchy (as it opens at 100 %, then 150 % typed), the scaled job, and
                    // 500 % (too big for the A1: warning, Apply off)
                    using (var sw = main.SampleScaleWindow(null)) if (sw != null) SnapForm(sw, folder, "scale_100.png");
                    using (var sw = main.SampleScaleWindow("150")) if (sw != null) SnapForm(sw, folder, "scale.png");
                    using (var sw = main.SampleScaleWindow("500")) if (sw != null) SnapForm(sw, folder, "scale_toobig.png");
                    using (var sw = main.SampleScaleWindow("5")) if (sw != null) SnapForm(sw, folder, "scale_bad.png");
                    // too small: Benchy at 20 % (12 × 6,2 × 9,6 mm): the soft muted note under the boxes, Apply still on
                    using (var sw = main.SampleScaleWindow("20")) if (sw != null) SnapForm(sw, folder, "scale_small.png");
                    // step 2: X / Y / Z (the lock off, 150 × 100 × 50 %), a height typed in mm (36 mm = 75 %), a STEP job
                    // (the lock forced on, its note), a typed mm too big for 500 %
                    using (var sw = main.SampleScaleWindowXyz(new double[] { 150, 100, 50 }, -1, 0, false)) if (sw != null) SnapForm(sw, folder, "scale_xyz.png");
                    using (var sw = main.SampleScaleWindowXyz(new double[] { 100, 100, 101 }, 2, 36, false)) if (sw != null) SnapForm(sw, folder, "scale_xyz_mm.png");
                    using (var sw = main.SampleScaleWindowXyz(null, -1, 0, true)) if (sw != null) SnapForm(sw, folder, "scale_locked.png");
                    using (var sw = main.SampleScaleWindowXyz(new double[] { 100, 100, 101 }, 2, 900, false)) if (sw != null) SnapForm(sw, folder, "scale_xyz_mm_bad.png");
                    main.SetSampleScaledJob();
                    Snap(main, folder, "main_scaled.png");
                    main.SetSampleJob();   // back to 100 %
                    // 4-colour Benchy: one row per colour in the Cost breakdown; picture = <folder>\model2.png if there
                    main.SetSampleMultiJob();
                    main.SetSamplePicture(Path.Combine(folder, "model2.png"), "sample1");
                    Snap(main, folder, "main_multi.png");
                    // quote export: the customer quote page of the 4-colour example (A4 at 150 dpi): light without a
                    // customer name, dark with one; then the "Show quote…" window (made-up customer, nothing is saved)
                    foreach (bool dark in new[] { false, true })
                        using (var q = main.RenderSampleQuote(dark, dark ? "Mette Hansen" : ""))
                            if (q != null)
                            {
                                q.Save(Path.Combine(folder, dark ? "quote_dark.png" : "quote_light.png"), ImageFormat.Png);
                                if (!dark) SavePdfCheck(q, folder);   // quote.pdf + quote_pdf.png (issue #1)
                            }
                    // the invoice page of the same job (sample code, customer and payment ways)
                    using (var inv = main.RenderSampleInvoice())
                        if (inv != null) inv.Save(Path.Combine(folder, "invoice_light.png"), ImageFormat.Png);
                    using (var inv = main.RenderSampleInvoice(true))   // paid-tick: "Paid 12-11-2026 – thank you"
                        if (inv != null) inv.Save(Path.Combine(folder, "invoice_paid.png"), ImageFormat.Png);
                    using (var qf = main.SampleQuoteWindow("Mette Hansen"))
                        if (qf != null)
                        {
                            Place(qf);
                            qf.Show();
                            Snap(qf, folder, "quote_window.png");
                            qf.Hide();
                        }
                    // Settings > Quote & invoice "Show invoice… instead of Show quote…" on: the button and its window
                    main.SampleMainInvoice(true);
                    Snap(main, folder, "main_invoice.png");
                    using (var jf = main.SampleJobInvoiceWindow("Mette Hansen"))
                        if (jf != null)
                        {
                            Place(jf);
                            jf.Show();
                            Snap(jf, folder, "invoice_window.png");
                            jf.Hide();
                        }
                    var invSettings = s.Clone();   // the Quote & invoice tab with the box ticked (never saved)
                    invSettings.MainInvoice = 1;
                    using (var set = new SettingsForm(invSettings, 0, SettingsForm.TabQuote))
                        SnapForm(set, folder, "settings_quote_invoice.png");
                    main.SampleMainInvoice(false);   // back to the default for the other pictures
                    // issue #8 step 3: a two-colour job with its prime tower (note line + "Prime tower: on" link), its quote
                    // with the "Prime tower 1,70 g" note, and the Quote & invoice tab with that box ticked (never saved)
                    main.SetSampleTowerJob();
                    Snap(main, folder, "main_tower.png");
                    using (var q = main.RenderSampleTowerQuote())
                        if (q != null) q.Save(Path.Combine(folder, "quote_tower.png"), ImageFormat.Png);
                    var towerSettings = s.Clone();
                    towerSettings.QuoteShowTower = 1;
                    using (var set = new SettingsForm(towerSettings, 0, SettingsForm.TabQuote))
                        SnapForm(set, folder, "settings_quote_tower.png");
                    main.SetSampleMultiJob();   // back to the 4-colour job for the pictures below
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
                    main.SetSamplePicture(sample, "sample0");
                    Snap(main, folder, "main_reopened.png");
                    // "Repeat print" of the 4-colour quote: today's prices, no slicing in picture mode
                    main.SetSampleJob();
                    main.SampleRepeat(quotes[1]);
                    main.SetSamplePicture(Path.Combine(folder, "model2.png"), "sample1");
                    Snap(main, folder, "repeat.png");
                    // Invoice… on a made invoice (Lene Hansen's phone stand): the name it was made out to, read-only
                    using (var mf = main.SampleMadeInvoiceWindow(quotes[2]))
                        if (mf != null)
                        {
                            Place(mf);
                            mf.Show();
                            Snap(mf, folder, "invoice_window_made.png");
                            mf.Hide();
                        }
                    using (var hist = new HistoryForm(quotes, null, true))
                    {
                        hist.QuoteRequested = (owner, recs, isInvoice) => null;   // shows Quote… / Invoice… (never clicked here)
                        hist.BackupRequested = owner => false;                  // shows Backup… (never clicked here)
                        hist.SamplePicturesForMode = HistoryForm.SamplePictures(folder);   // real model pictures (assets\history-samples)
                        Place(hist);
                        hist.Show();
                        hist.SelectMonth(new DateTime(2026, 10, 1));   // October: billed, failed and not-printed quotes
                        Snap(hist, folder, "history.png");
                        // idea 22: all months, three selected, in each view; the delete question; the ✓ line with Undo
                        hist.SelectMonth(DateTime.MinValue);
                        hist.SampleSelect(new[] { 2, 4, 5 });
                        Snap(hist, folder, "history_selected.png");
                        hist.SampleView(HistoryForm.ViewMedium);
                        Snap(hist, folder, "history_medium.png");
                        hist.SampleView(HistoryForm.ViewLarge);
                        Snap(hist, folder, "history_large.png");
                        hist.SampleView(HistoryForm.ViewList);
                        using (var ask = hist.NewDeleteAsk(hist.SampleSelected()))
                            SnapForm(ask, folder, "history_delete_ask.png");
                        using (var ask = hist.NewClearAsk())
                            SnapForm(ask, folder, "history_clear_ask.png");
                        hist.DoDelete(hist.SampleSelected());   // in memory only (picture mode)
                        Snap(hist, folder, "history_deleted.png");
                        hist.UndoClicked();
                        Snap(hist, folder, "history_undone.png");
                        // issue #7: the Show list with the print statuses, one status filter, the Status cell's menu, the
                        // question before several Failed
                        Snap(hist, folder, "history_show_list.png", hist.SampleShowList);
                        hist.SampleShow(4);   // Not printed yet
                        Snap(hist, folder, "history_show_notprinted.png");
                        hist.SampleShow(0);
                        hist.SampleSelect(new int[0]);
                        Snap(hist, folder, "history_status_menu.png", (g, o) => hist.SampleStatusMenu(1, g, o));
                        using (var ask = HistoryForm.NewFailedAsk(3))
                            SnapForm(ask, folder, "history_failed_ask.png");
                        hist.Hide();
                    }
                    // issue #7: the Invoices window (made-up invoices: a combined Mixed one, one past its due date), a search,
                    // the Status cell's menu of the combined invoice
                    var invQuotes = HistoryForm.SampleInvoiceQuotes();
                    using (var hist = new HistoryForm(invQuotes, null, true))
                    {
                        hist.QuoteRequested = (owner, recs, isInvoice) => null;
                        hist.Settings = s;
                        Place(hist);
                        hist.Show();
                        using (var inv = hist.NewInvoicesForm())
                        {
                            Place(inv);
                            inv.Show(hist);
                            Snap(inv, folder, "invoices.png");
                            inv.TestSelect("2026-51907");
                            int row = inv.TestRows.FindIndex(r => r.No == "2026-51907");
                            Snap(inv, folder, "invoices_status_menu.png", (g, o) => inv.SampleStatusMenu(row, g, o));
                            inv.TestSearch("mads");
                            Snap(inv, folder, "invoices_search.png");
                            inv.TestSearch("");
                            inv.TestStatusFilter(2);   // Printing now: only the 4-colour Benchy's invoice
                            Snap(inv, folder, "invoices_filter.png");
                            // paid-tick: Payment = Unpaid (the overdue one in amber), the bottom "Unpaid: …" line
                            inv.TestStatusFilter(0);
                            inv.TestPayment(1);
                            Snap(inv, folder, "invoices_unpaid.png");
                            inv.TestPayment(0);
                            inv.TestSearch("zzz");   // qol 9: nothing matches
                            Snap(inv, folder, "invoices_empty.png");
                            inv.TestSearch("");
                            inv.Hide();
                        }
                        // fit-to-screen: as on a 1366 x 768 screen at 125 % (1093 x 582 px working area at 96 dpi, taskbar off)
                        HistoryForm.InvoicesForm.TestScreenWidth = 1093;
                        HistoryForm.InvoicesForm.TestScreenHeight = 582;
                        using (var inv = hist.NewInvoicesForm())
                        {
                            Place(inv);
                            inv.Show(hist);
                            Snap(inv, folder, "invoices_narrow.png");
                            inv.Hide();
                        }
                        HistoryForm.InvoicesForm.TestScreenWidth = 0;
                        HistoryForm.InvoicesForm.TestScreenHeight = 0;
                        hist.Hide();
                    }
                    // combined bill (Part A): three small prints for Lene Hansen on one invoice (made: one code, below the
                    // minimum: the top-up shared into the job lines and Work & handling, never shown), the same as a dark quote, the Invoice… window for them (two names: the warning line), and
                    // 12 prints as a PDF (2 A4 pages: invoice_group12.pdf + each page's JPEG as _p1.png / _p2.png)
                    var groupPictures = HistoryForm.SamplePictures(folder);
                    Image[] gp;
                    var madeJobs = main.SampleGroupJobs(3, true, groupPictures, out gp);
                    using (var page = QuotePage.Render(main.SampleGroupData(madeJobs, gp, true), false))
                        page.Save(Path.Combine(folder, "invoice_group.png"), ImageFormat.Png);
                    Image[] gq;
                    var openJobs = main.SampleGroupJobs(3, false, groupPictures, out gq);
                    using (var page = QuotePage.Render(main.SampleGroupData(openJobs, gq, false), true))
                        page.Save(Path.Combine(folder, "quote_group_dark.png"), ImageFormat.Png);
                    openJobs[1].Customer = "Lene H.";
                    using (var gf = main.SampleGroupWindow(openJobs, gq, true))
                        if (gf != null)
                        {
                            Place(gf);
                            gf.Show();
                            Snap(gf, folder, "invoice_group_window.png");
                            gf.Hide();
                        }
                    // issue #7: a combined quote whose prints differ: the one status box says "Mixed"
                    openJobs[0].Printed = false;
                    using (var gf = main.SampleGroupWindow(openJobs, gq, false))
                        if (gf != null)
                        {
                            Place(gf);
                            gf.Show();
                            Snap(gf, folder, "quote_group_mixed.png");
                            gf.Hide();
                        }
                    openJobs[0].Printed = true;
                    Image[] g4;
                    var jobs4 = main.SampleGroupJobs(4, true, groupPictures, out g4);
                    var pages4 = QuotePage.RenderPages(main.SampleGroupData(jobs4, g4, true), false);
                    try { SavePdfPages(pages4, folder, "invoice_group4"); }   // 4 cards: a row of 3, then 1 + the total
                    finally { foreach (var b in pages4) b.Dispose(); }
                    foreach (var im in g4) if (im != null) im.Dispose();
                    Image[] g12;
                    var jobs12 = main.SampleGroupJobs(12, true, groupPictures, out g12);
                    var data12 = main.SampleGroupData(jobs12, g12, true);
                    using (var tall = QuotePage.Render(data12, false))
                        tall.Save(Path.Combine(folder, "invoice_group12_tall.png"), ImageFormat.Png);
                    var pages12 = QuotePage.RenderPages(data12, false);
                    try { SavePdfPages(pages12, folder, "invoice_group12"); }
                    finally { foreach (var b in pages12) b.Dispose(); }
                    // the delete question for one job of the made invoice
                    using (var hist = new HistoryForm(madeJobs, null, true))
                    {
                        hist.QuoteRequested = (owner, recs, isInvoice) => null;
                        using (var ask = hist.NewDeleteAsk(new List<QuoteRecord> { madeJobs[1] }))
                            SnapForm(ask, folder, "history_group_delete_ask.png");
                    }
                    foreach (var arr in new[] { gp, gq, g12 }) foreach (var im in arr) if (im != null) im.Dispose();

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
                    // issue #2: a code remembered before the window is built (this used to crash while the window was built)
                    var sr = s.Clone();
                    BackupForm.SampleRemember(sr);
                    using (var bf = new BackupForm(quotes, sr, BackupForm.TabRestore, true))
                    {
                        Place(bf);
                        bf.Show();
                        bf.SampleRememberedRestore();
                        Snap(bf, folder, "restore_remembered.png");    // the remembered code filled in, "An online code"
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
                    main.SetSamplePicture(sample, "sample0");
                    // the single Benchy as a customer quote: below the minimum price, the top-up is inside Work & handling
                    // (the minimum is never shown on a bill)
                    using (var q = main.RenderSampleTopUpQuote())
                        if (q != null) q.Save(Path.Combine(folder, "quote_topup.png"), ImageFormat.Png);
                    // A1: settings.ini couldn't be read at start (made-up text, nothing read): the Warning info box
                    main.ShowSettingsProblem(L.F("The settings file was unreadable, kept as {0}. Default settings are used.",
                                                 "Indstillingsfilen kunne ikke læses og er gemt som {0}. Standardindstillingerne bruges.",
                                                 "settings.bad-2026-11-20-0930.ini"));   // AppSettings.Load's text for a cut-off file
                    Snap(main, folder, "main_settings_problem.png");
                    main.SetSampleJob();   // back to the normal ✓ line
                    main.SetSamplePicture(sample, "sample0");
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
                    // qol 3: OK with a bad number (-5 in Friends markup): the red box + the red bottom-row text, no Windows box
                    using (var set = new SettingsForm(s.Clone()))
                    {
                        Place(set);
                        set.Show();
                        set.SampleBadNumber();
                        Snap(set, folder, "settings_bad.png");
                        set.Hide();
                    }
                    // issue #7: the question before "Reset to defaults" (your own details are kept)
                    using (var ask = SettingsForm.NewResetAsk())
                        SnapForm(ask, folder, "settings_reset_ask.png");
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

                    // first-time setup (idea 19): each step of a first run (new install: no name on quotes), the printer step
                    // without Bambu Studio, the summary with two printers ticked; then the summary of a re-run from Settings
                    var fresh = s.Clone();
                    fresh.QuoteName = "";
                    fresh.SetupDone = 0;
                    using (var setup = new SetupForm(fresh, false))
                    {
                        Place(setup);
                        setup.Show();
                        string[] setupNames = { "setup_1.png", "setup_2.png", "setup_3.png", "setup_4.png" };
                        for (int i = 0; i < setupNames.Length; i++)
                        {
                            setup.SampleStep(i);
                            Snap(setup, folder, setupNames[i]);
                        }
                        setup.SampleStep(SetupForm.StepPrinter);
                        setup.SampleStudioMissing(true);
                        Snap(setup, folder, "setup_studio_missing.png");
                        setup.SampleStudioMissing(false);
                        setup.SampleAnswers();
                        setup.SampleStep(SetupForm.StepPrinter);
                        Snap(setup, folder, "setup_2_two.png");
                        setup.SampleStep(SetupForm.StepSummary);
                        Snap(setup, folder, "setup_5.png");
                        setup.Hide();
                    }
                    using (var setup = new SetupForm(s, true))
                    {
                        Place(setup);
                        setup.Show();
                        setup.SampleStep(SetupForm.StepPrinter);
                        Snap(setup, folder, "setup_again_printer.png");
                        setup.SampleStep(SetupForm.StepSummary);
                        Snap(setup, folder, "setup_again.png");
                        setup.Hide();
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
                        hist.QuoteRequested = (owner, recs, isInvoice) => null;
                        hist.BackupRequested = owner => false;   // shows Backup… like the real window
                        Place(hist);
                        hist.Show();
                        hist.SelectMonth(new DateTime(2026, 10, 1));
                        Snap(hist, folder, "history_printers.png");
                        hist.Hide();
                    }
                    main.SetSampleJob();
                    main.SetSamplePicture(sample, "sample0");
                    main.SampleSwitchPrinter(Wear.ModelA1Mini);   // card + warning stripe: sliced for the printer used before
                    Snap(main, folder, "main_printer_changed.png");
                    main.SampleSwitchPrinter(Wear.ModelA1);   // back to the A1 for the idea 21 pictures

                    // idea 21: a colour per file. The worst height: 4 colour rows + the "together" fallback note + plates link
                    main.SetSamplePartsJob();
                    Snap(main, folder, "main_parts.png");
                    // a sliced 3MF with 3 plates (one named): the link, then its menu drawn where it opens
                    main.SetSamplePlatesJob();
                    Snap(main, folder, "main_plates.png");
                    Snap(main, folder, "main_plates_menu.png", main.SamplePlatesMenu);
                    // a MakerWorld link: the sign-in message with "Open the page"
                    main.SampleMakerWorld();
                    Snap(main, folder, "main_makerworld.png");
                    // the "Choose files and colours" window: 5 files (one 3MF with its own colours), colours picked,
                    // "together on one plate" ticked with its note; then the same with row 2's colour menu drawn open
                    var pickNames = new List<string> { "body.stl", "wheel_front.stl", "wheel_rear.stl", "spoiler_with_a_rather_long_file_name_v2_final.stl", "body.3mf" };
                    using (var picker = new FilePickerForm("RC car wheel set", pickNames, FilePickerForm.DefaultTicks(pickNames), true, "Bambu PLA Basic", null))
                    {
                        Place(picker);
                        picker.Show();
                        picker.SampleChoices(new[] { "Bambu PLA Basic", "Bambu PLA Matte", "Bambu PLA Matte", "Bambu PETG HF", null },
                                             new[] { "#000000", "#C12E1F", "#C12E1F", "#F4EE2A", null }, null, true);
                        Snap(picker, folder, "picker.png");
                        Snap(picker, folder, "picker_colour_menu.png", (g, o) => picker.SampleColourMenu(0, g, o));
                        // batch & plates: copies per file (4 bodies, 8 wheels, ...)
                        picker.SampleCopies(new[] { 4, 8, 8, 1, 2 });
                        Snap(picker, folder, "picker_copies.png");
                        picker.SampleCopies(new[] { 400, 200, 1, 1, 1 });   // over 500 in all: red boxes + error line
                        Snap(picker, folder, "picker_copies_bad.png");
                        picker.Hide();
                    }

                    // batch & plates (issue #9 / #10): the Benchy x 40 on 2 plates (28 + 12), its plates menu, the Copies
                    // window (as it opens, 300 = the "several minutes" warning, 600 = out of range), its quote and invoice,
                    // a job typed by hand with copies, and a combined bill with copies
                    main.SetSampleJob();
                    main.SetSamplePicture(sample, "sample0");
                    main.SetSampleBatchJob();
                    Snap(main, folder, "main_batch.png");
                    Snap(main, folder, "main_batch_plates_menu.png", main.SamplePlatesMenu);
                    using (var cw = main.SampleCopiesWindow(0, false)) if (cw != null) SnapForm(cw, folder, "copies.png");
                    using (var cw = main.SampleCopiesWindow(300, false)) if (cw != null) SnapForm(cw, folder, "copies_big.png");
                    using (var cw = main.SampleCopiesWindow(600, false)) if (cw != null) SnapForm(cw, folder, "copies_bad.png");
                    using (var cw = main.SampleCopiesWindow(12, true)) if (cw != null) SnapForm(cw, folder, "copies_typed.png");
                    using (var q = main.RenderSampleQuote(false, "Mette Hansen"))
                        if (q != null) q.Save(Path.Combine(folder, "quote_batch.png"), ImageFormat.Png);
                    using (var inv = main.RenderSampleInvoice())
                        if (inv != null) inv.Save(Path.Combine(folder, "invoice_batch.png"), ImageFormat.Png);
                    main.SetSampleTypedCopies();
                    Snap(main, folder, "main_typed_copies.png");
                    Image[] gb;
                    var batchJobs = main.SampleGroupJobs(3, false, HistoryForm.SamplePictures(folder), out gb);
                    History.SetCopies(batchJobs[0], 4, 1, 4, false);
                    History.SetCopies(batchJobs[1], 40, 2, 28, false);
                    using (var page = QuotePage.Render(main.SampleGroupData(batchJobs, gb, true), false))
                        page.Save(Path.Combine(folder, "invoice_group_batch.png"), ImageFormat.Png);
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

        /// Writes the quote page as quote.pdf (QuotePage.Pdf, as Save as PDF does) and the JPEG inside it, decoded,
        /// as quote_pdf.png, so the PDF's real page content can be checked (it must fill the whole picture).
        static void SavePdfCheck(Bitmap page, string folder)
        {
            byte[] pdf = QuotePage.Pdf(page);
            File.WriteAllBytes(Path.Combine(folder, "quote.pdf"), pdf);
            int start = -1, end = -1;   // the JPEG: from its FF D8 start marker to the last FF D9 end marker
            for (int i = 0; i + 1 < pdf.Length; i++)
                if (pdf[i] == 0xFF && pdf[i + 1] == 0xD8) { start = i; break; }
            for (int i = pdf.Length - 2; i > start; i--)
                if (pdf[i] == 0xFF && pdf[i + 1] == 0xD9) { end = i + 2; break; }
            if (start < 0 || end < 0) return;
            using (var ms = new MemoryStream(pdf, start, end - start))
            using (var img = Image.FromStream(ms))
                img.Save(Path.Combine(folder, "quote_pdf.png"), ImageFormat.Png);
        }

        /// Writes a several-page PDF (QuotePage.Pdf, as Save as PDF does for a combined bill) as name.pdf and each page's
        /// JPEG inside it, decoded, as name_p1.png, name_p2.png… (the real page content of each A4 page).
        static void SavePdfPages(List<Bitmap> pages, string folder, string name)
        {
            byte[] pdf = QuotePage.Pdf(pages);
            File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), pdf);
            int at = 0, k = 0;
            while (true)
            {
                int start = -1, end = -1;
                for (int i = at; i + 1 < pdf.Length; i++)
                    if (pdf[i] == 0xFF && pdf[i + 1] == 0xD8) { start = i; break; }
                if (start < 0) break;
                // the JPEG ends right before "\nendstream"
                byte[] marker = System.Text.Encoding.ASCII.GetBytes("\nendstream");
                for (int i = start; i + marker.Length <= pdf.Length && end < 0; i++)
                {
                    bool hit = true;
                    for (int j = 0; j < marker.Length && hit; j++) hit = pdf[i + j] == marker[j];
                    if (hit) end = i;
                }
                if (end < 0) break;
                k++;
                using (var ms = new MemoryStream(pdf, start, end - start))
                using (var img = Image.FromStream(ms))
                    img.Save(Path.Combine(folder, name + "_p" + k + ".png"), ImageFormat.Png);
                at = end;
            }
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

        const int SettleMs = 600;   // shortest wait per picture (Snap)

        static void Pump()
        {
            for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(10); }   // short: Snap's capture loop is the real wait
        }

        internal static void Snap(Form f, string folder, string name)
        {
            Snap(f, folder, name, null);
        }

        /// draw: paints extra things (an open menu) over the picture; its Point is the picture's top left on screen.
        static void Snap(Form f, string folder, string name, Action<Graphics, Point> draw)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Pump();
            RedrawWindow(f.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0080 | 0x0100 | 0x0400);   // INVALIDATE|ALLCHILDREN|UPDATENOW|FRAME
            Pump();
            // PrintWindow's full-content copy comes from DWM and can lag behind a change made just before
            // (stale picture): wait for DWM to compose the new frame first
            try { DwmFlush(); } catch { }
            Pump();
            // ...and keep capturing until three captures in a row are the same (two matches), and at least SettleMs
            // have passed: a focused box's own Border-grey frame (Theme.BoxBorder) comes back only after a while, and
            // Windows' lighter frame can stay still that long (up to ~4 s in all)
            Bitmap bmp = Capture(f);
            int matches = 0;
            while ((matches < 2 || clock.ElapsedMilliseconds < SettleMs) && clock.ElapsedMilliseconds < 4000)
            {
                Pump();
                try { DwmFlush(); } catch { }
                var next = Capture(f);
                matches = SamePixels(bmp, next) ? matches + 1 : 0;
                bmp.Dispose();
                bmp = next;
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
