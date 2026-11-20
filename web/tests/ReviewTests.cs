using System;
using System.Collections.Generic;
using PrintPrice;
using PrintPrice.Web;

// Whole-app review fixes on the web (2026-11-20): A8 settings merge between tabs, B storage full, D invoice file names /
// Customer column / Printing now, C texts. The browser side (rollback after a failed save, two tabs, clipboard, accept,
// 16 px) is checked by plans\ui-review\web-test\run.mjs.
static class ReviewTests
{
    public static void Run(Action<string, bool> True, Action<string, double, double> Eq)
    {
        bool wasDanish = L.Danish;
        L.Danish = false;
        bool up;

        // ---- A8: what another tab stored is kept when this tab writes ----
        var other = new AppSettings { UsedInvoiceNos = "2026-11111,2026-22222", LastPrinterId = 5 };
        var mine = new AppSettings { UsedInvoiceNos = "2026-22222,2026-33333", LastPrinterId = 3, FriendsMarkupPct = 40 };
        True("merge: changed", WebCalc.MergeStored(mine, WebCalc.SettingsToText(other)));
        var codes = new HashSet<string>(mine.UsedInvoiceNos.Split(','));
        True("merge: every code from both tabs once (" + mine.UsedInvoiceNos + ")",
             codes.Count == 3 && codes.Contains("2026-11111") && codes.Contains("2026-22222") && codes.Contains("2026-33333")
             && mine.UsedInvoiceNos.Split(',').Length == 3);
        True("merge: mine last (newest kept by the cap)", mine.UsedInvoiceNos.EndsWith("2026-33333", StringComparison.Ordinal));
        Eq("merge: higher printer id", 5, mine.LastPrinterId);
        Eq("merge: my own values stay", 40, mine.FriendsMarkupPct);
        True("merge again: nothing new", !WebCalc.MergeStored(mine, WebCalc.SettingsToText(other)));
        var lower = new AppSettings { LastPrinterId = 9, UsedInvoiceNos = "" };
        WebCalc.MergeStored(lower, WebCalc.SettingsToText(new AppSettings { LastPrinterId = 2 }));
        Eq("merge: a lower stored id never lowers mine", 9, lower.LastPrinterId);
        var plain = new AppSettings { UsedInvoiceNos = "2026-1" };
        True("merge: empty / null stored text = no change", !WebCalc.MergeStored(plain, "") && !WebCalc.MergeStored(plain, null) && plain.UsedInvoiceNos == "2026-1");
        True("merge: unreadable stored text = no change", !WebCalc.MergeStored(plain, "\u0000garbage=\nUsedInvoiceNos") || plain.UsedInvoiceNos.Contains("2026-1"));
        var many = new List<string>();
        for (int i = 0; i < 2100; i++) many.Add("2026-" + (10000 + i));
        var big = new AppSettings { UsedInvoiceNos = string.Join(",", many.GetRange(0, 1500).ToArray()) };
        var bigOther = new AppSettings { UsedInvoiceNos = string.Join(",", many.GetRange(1000, 1100).ToArray()) };
        WebCalc.MergeStored(big, WebCalc.SettingsToText(bigOther));
        True("merge: cap " + History.MaxUsedInvoiceNos + " kept", big.UsedInvoiceNos.Split(',').Length == History.MaxUsedInvoiceNos);
        var round = WebCalc.SettingsFromText(WebCalc.SettingsToText(mine), out up);
        True("merged settings round trip", round.UsedInvoiceNos == mine.UsedInvoiceNos && round.LastPrinterId == 5);

        // ---- B: storage full ----
        True("quota names: Chrome/Edge/Safari/Firefox", WebHistory.IsQuotaError("QuotaExceededError") && WebHistory.IsQuotaError("NS_ERROR_DOM_QUOTA_REACHED")
             && WebHistory.IsQuotaError("The quota has been exceeded.") && !WebHistory.IsQuotaError("UnknownError") && !WebHistory.IsQuotaError(null));
        True("save problem says full (Firefox name too)", WebHistory.SaveProblem("NS_ERROR_DOM_QUOTA_REACHED").EndsWith("the browser's storage is full.", StringComparison.Ordinal));
        True("settings problem", WebHistory.SettingsProblem("QuotaExceededError") == "Your settings couldn't be saved in this browser: the browser's storage is full."
             && WebHistory.SettingsProblem("SecurityError") == "Your settings couldn't be saved in this browser: SecurityError.");
        var sizes = WebBackup.ParseSizes("before-restore-20261120-101010\t20000000\nbefore-clear-20261120-111111\t3000000\nhistory\t99\npic:a\t5\nbad line");
        True("sizes: only safety copies", sizes.Count == 2 && sizes[0].Key == "before-restore-20261120-101010" && sizes[1].Value == 3000000);
        True("delete text EN: " + WebBackup.DeleteSafetyText(sizes), WebBackup.DeleteSafetyText(sizes) == "Delete safety copies (2, about 22 MB)");
        True("delete text: none", WebBackup.DeleteSafetyText(WebBackup.ParseSizes("")) == null && WebBackup.DeleteSafetyText(null) == null);
        True("small copy = about 1 MB", WebBackup.DeleteSafetyText(WebBackup.ParseSizes("before-clear-x\t10")) == "Delete safety copies (1, about 1 MB)");
        True("deleted text", WebBackup.DeletedSafetyText(1) == "✓ 1 safety copy deleted. Try again now." && WebBackup.DeletedSafetyText(3) == "✓ 3 safety copies deleted. Try again now.");
        True("full box text", WebHistory.StorageFullText.Contains("Download a backup first"));

        // ---- D: Printing now (= HistoryViews.PrintingText) ----
        var now = new DateTime(2026, 11, 20, 14, 0, 0);
        var r = new QuoteRecord { Id = "p1", SavedAt = "2026-11-20 11:00:00", Hours = 4, Minutes = 40, Printed = true };
        History.SetPrintState(r, PrintState.Printing, now.AddHours(-2));   // started 12:00, done 16:40
        True("printing long: " + WebHistory.PrintingText(r, now, false), WebHistory.PrintingText(r, now, false) == "Printing, done about 16:40");
        True("printing short", WebHistory.PrintingText(r, now, true) == "Printing · 16:40");
        True("printing ends another day: date", WebHistory.PrintingText(r, new DateTime(2026, 11, 19, 23, 0, 0), true) == null
             || WebHistory.PrintingText(r, new DateTime(2026, 11, 20, 13, 0, 0), true) == "Printing · 16:40");
        var night = new QuoteRecord { Id = "p2", SavedAt = "2026-11-20 20:00:00", Hours = 10, Minutes = 0, Printed = true };
        History.SetPrintState(night, PrintState.Printing, new DateTime(2026, 11, 20, 22, 0, 0));
        True("over midnight: " + WebHistory.PrintingText(night, new DateTime(2026, 11, 20, 23, 0, 0), true),
             WebHistory.PrintingText(night, new DateTime(2026, 11, 20, 23, 0, 0), true) == "Printing · 21-11 08:00");
        True("finished = no clock", WebHistory.PrintingText(r, now.AddHours(3), false) == null);
        var plainPrinted = new QuoteRecord { Id = "p3", Printed = true };
        True("printed without a start = no clock", WebHistory.PrintingText(plainPrinted, now, false) == null);
        True("next end = the soonest running one", WebHistory.NextPrintingEnd(new List<QuoteRecord> { night, r, plainPrinted }, now) == new DateTime(2026, 11, 20, 16, 40, 0));
        True("next end: none", WebHistory.NextPrintingEnd(new List<QuoteRecord> { plainPrinted }, now) == null && WebHistory.NextPrintingEnd(null, now) == null);
        L.Danish = true;
        True("printing DA: " + WebHistory.PrintingText(r, now, false), WebHistory.PrintingText(r, now, false) == "Printer, færdig ca. 16:40"
             && WebHistory.PrintingText(r, now, true) == "Printer · 16:40");
        L.Danish = false;
        History.SetPrinted(r, false);
        True("unticked = no clock", WebHistory.PrintingText(r, now, false) == null);

        // ---- D: Customer column ----
        var c = new QuoteRecord { Customer = "  Mette Hansen " };
        True("customer text trimmed", WebHistory.CustomerText(c) == "Mette Hansen" && WebHistory.CustomerTip(c) == "Mette Hansen");
        True("no customer: empty, no tip", WebHistory.CustomerText(new QuoteRecord()) == "" && WebHistory.CustomerTip(new QuoteRecord()) == null && WebHistory.CustomerText(null) == "");
        var billed = new QuoteRecord { Customer = "Mette", InvoiceNo = "2026-48213", BilledTo = "Mette Hansen ApS" };
        True("made invoice to another name: tip says so", WebHistory.CustomerText(billed) == "Mette"
             && WebHistory.CustomerTip(billed) == "Mette\nInvoice 2026-48213 is made out to Mette Hansen ApS");
        billed.BilledTo = "Mette";
        True("same name: no note", WebHistory.CustomerTip(billed) == "Mette");
        L.Danish = true;
        billed.BilledTo = "Lene";
        True("tip DA", WebHistory.CustomerTip(billed) == "Mette\nFaktura 2026-48213 er lavet til Lene");
        L.Danish = false;

        // ---- D: invoice PDF names (issue #6) ----
        var sh = new WebQuote.Sheet { Date = new DateTime(2026, 11, 4), Model = "3D Benchy", Customer = "Mette Hansen", Invoice = true, InvoiceNo = "2026-48213" };
        True("invoice file name: " + WebQuote.FileName(sh), WebQuote.FileName(sh) == "2026-48213 - Mette Hansen");
        True("= QuoteRows.InvoiceFileName", WebQuote.FileName(sh) == QuoteRows.InvoiceFileName("2026-48213", "Mette Hansen"));
        sh.Customer = "A/S: \"Test\"";
        True("forbidden characters: " + WebQuote.FileName(sh), WebQuote.FileName(sh) == QuoteRows.InvoiceFileName("2026-48213", "A/S: \"Test\"") && WebQuote.FileName(sh).IndexOf('/') < 0);
        sh.InvoiceNo = "(new)";
        sh.Customer = "Mette Hansen";
        True("invoice not made yet: the quote name", WebQuote.FileName(sh) == "2026-11-04 3D Benchy - Mette Hansen");
        sh.Invoice = false; sh.InvoiceNo = "";
        True("quote: unchanged", WebQuote.FileName(sh) == "2026-11-04 3D Benchy - Mette Hansen");

        // ---- A9 texts: a failed save never says "is in the history" ----
        True("printed, invoice not saved", WebQuote.PrintedText(true, null, false) == "Not printed: the invoice couldn't be saved.");
        True("copied, invoice not saved", WebQuote.CopiedText(true, null, false) == "Not copied: the invoice couldn't be saved.");
        True("quote not saved: no 'saved'", !WebQuote.PrintedText(false, null, false).Contains("saved") && !WebQuote.CopiedText(false, null, false).Contains("saved"));
        True("invoice saved", WebQuote.PrintedText(true, "2026-1", true).Contains("2026-1 is in the history"));

        // ---- C texts ----
        True("help: marks it as printed", WebQuote.HelpText(true, true).Contains("marks it as printed") && !WebQuote.HelpText(true, true).Contains("ticks"));
        L.Danish = true;
        True("help DA", WebQuote.HelpText(true, true).Contains("markeres som printet"));
        var names = new List<QuoteRecord>();
        for (int i = 0; i < 5; i++) names.Add(new QuoteRecord { Model = "M" + i });
        True("DA names: flere", WebHistory.NamesText(names) == "M0, M1, M2 og 2 flere");
        True("DA delete text", WebBackup.DeleteSafetyText(sizes) == "Slet sikkerhedskopier (2, ca. 22 MB)");
        L.Danish = false;

        L.Danish = wasDanish;
    }
}
