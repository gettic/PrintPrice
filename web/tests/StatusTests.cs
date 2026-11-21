using System;
using System.Collections.Generic;
using PrintPrice;
using PrintPrice.Web;

// Issue #7 on the website (WebStatus.cs): the History filter's four statuses, the Mixed state, the question before
// several Failed, the texts (EN/DA) and Settings' "Reset to defaults". The pages are checked by
// plans\web-followups\test\run.mjs.
static class StatusTests
{
    public static void Run(Action<string, bool> True, Action<string, double, double> Eq)
    {
        bool wasDanish = L.Danish;
        L.Danish = false;
        var now = new DateTime(2026, 11, 20, 12, 0, 0);
        Func<string, QuoteRecord> rec = id => new QuoteRecord { Id = id, SavedAt = "2026-11-20 08:00:00", Model = id };
        var none = rec("none");
        var printing = rec("printing"); History.SetPrintState(printing, PrintState.Printing, now.AddMinutes(-30)); printing.Hours = 2;
        var over = rec("over"); History.SetPrintState(over, PrintState.Printing, now.AddHours(-5)); over.Hours = 1;
        var printed = rec("printed"); History.SetPrintState(printed, PrintState.Printed, now);
        var failed = rec("failed"); History.SetPrintState(failed, PrintState.Failed, now);
        var all = new List<QuoteRecord> { none, printing, over, printed, failed };

        // filter: every quote under exactly one status; a print whose time ran out counts as Printed
        foreach (var r in all)
        {
            int hits = 0;
            foreach (var st in WebStatus.States)
                if (History.Matches(r, DateTime.MinValue, null, History.ShowFor(st), "", null, now)) hits++;
            True("filter: " + r.Id + " under exactly one status", hits == 1);
        }
        True("filter: printing now", History.Matches(printing, DateTime.MinValue, null, History.ShowStatePrinting, "", null, now));
        True("filter: time ran out = printed", History.Matches(over, DateTime.MinValue, null, History.ShowStatePrinted, "", null, now)
             && !History.Matches(over, DateTime.MinValue, null, History.ShowStatePrinting, "", null, now));
        True("filter: state shows 5-8 only", WebStatus.IsStateShow(5) && WebStatus.IsStateShow(8) && !WebStatus.IsStateShow(3) && !WebStatus.IsStateShow(0));
        var wf = new WebHistory.Filter { Show = History.ShowStateFailed };
        True("web filter: failed only", WebHistory.Apply(all, wf, null).Count == 1 && WebHistory.Apply(all, wf, null)[0] == failed);

        // Mixed / shared
        True("shared: one status", WebStatus.Shared(new List<QuoteRecord> { printed, over }, now) == PrintState.Printed);
        True("shared: mixed = null", WebStatus.Shared(new List<QuoteRecord> { printed, none }, now) == null);

        // the question: only 2+ not-yet-failed to Failed
        True("ask: 2 to failed", WebStatus.FailedToAsk(new List<QuoteRecord> { none, printed }, PrintState.Failed) == 2);
        True("ask: 1 new + 1 already failed = no", WebStatus.FailedToAsk(new List<QuoteRecord> { none, failed }, PrintState.Failed) == 0);
        True("ask: not for Printed", WebStatus.FailedToAsk(new List<QuoteRecord> { none, printing }, PrintState.Printed) == 0);
        var two = new List<QuoteRecord> { rec("a"), rec("b") };
        WebStatus.Apply(two, PrintState.Failed, now);
        True("apply: both failed", two.TrueForAll(r => r.Failed && History.PrintStateOf(r, now) == PrintState.Failed));

        // texts
        True("saved text", WebStatus.SavedText(PrintState.Printed) == "✓ Print status saved: Printed.");
        True("list text 1", WebStatus.ListText(1, PrintState.Failed, false) == "✓ Print status: Failed.");
        True("list text 3 moved", WebStatus.ListText(3, PrintState.Printed, true) == "✓ 3 quotes set to Printed. They are now under their new status in Show.");
        True("group heading", WebStatus.GroupHeading(3) == "Print status (all 3 prints)" && WebStatus.Mixed == "Mixed");
        True("question", WebStatus.FailedQuestion(3) == "Mark 3 prints as failed?");
        True("reset texts", WebStatus.ResetButton == "Reset to defaults" && WebStatus.ResetQuestion == "Reset to defaults?" && WebStatus.ResetText.Contains("are kept"));
        L.Danish = true;
        True("DA saved", WebStatus.SavedText(PrintState.Failed) == "✓ Printstatus gemt: Fejlet.");
        True("DA list", WebStatus.ListText(2, PrintState.NotPrinted, true) == "✓ 2 tilbud sat til Ikke printet endnu. De ligger nu under deres nye status i Vis.");
        True("DA mixed / heading", WebStatus.Mixed == "Blandet" && WebStatus.GroupHeading(2) == "Printstatus (begge print)" && WebStatus.GroupHeading(3) == "Printstatus (alle 3 print)" && WebStatus.FailedQuestion(2) == "Markér 2 print som fejlet?");
        True("DA reset", WebStatus.ResetButton == "Nulstil til standard" && WebStatus.ResetYes == "Nulstil");
        L.Danish = false;

        // Reset to defaults: own details kept, everything not on a tab kept, the rest default
        var s = new AppSettings
        {
            QuoteName = "Test 3D", PayMobilePay = "12 34 56 78", PayBank = "1234 5678", Language = "da", SlicerPath = "C:\\x",
            FriendsMarkupPct = 40, OthersMarkupPct = 120, MinimumPrice = 80, RoundTo = 5, FailedPrintPct = 15, MainInvoice = 1,
            InvoiceDueDays = 30, QuoteValidDays = 60, PowerAddOn = 1.2, PriceArea = "DK2", UsedInvoiceNos = "2026-11111", SetupDone = 1,
            LastFilamentPrice = 250, QuoteDark = 0
        };
        s.PrinterName = "Workshop"; s.PrinterModel = Wear.FindModel(Wear.Models[1].Key).Key; s.PrinterWatts = 999; s.AutoWearRate = 0; s.MachineRatePerHour = 9;
        s.AddPrinter("Second", Wear.ModelA1);
        int printersBefore = s.Printers.Count;
        var d = WebStatus.Defaults(s);
        var def = new AppSettings();
        True("reset: own details kept", d.QuoteName == "Test 3D" && d.PayMobilePay == "12 34 56 78" && d.PayBank == "1234 5678" && d.Language == "da" && d.SlicerPath == "C:\\x");
        True("reset: tab values default", d.FriendsMarkupPct == def.FriendsMarkupPct && d.OthersMarkupPct == def.OthersMarkupPct && d.MinimumPrice == def.MinimumPrice
             && d.RoundTo == def.RoundTo && d.FailedPrintPct == def.FailedPrintPct && d.MainInvoice == 0 && d.InvoiceDueDays == def.InvoiceDueDays
             && d.QuoteValidDays == def.QuoteValidDays && d.PowerAddOn == def.PowerAddOn && d.PriceArea == def.PriceArea && d.LastFilamentPrice == def.LastFilamentPrice);
        True("reset: printer keeps name and model, gets the model's defaults", d.PrinterName == "Workshop" && d.PrinterModel == s.PrinterModel
             && d.PrinterWatts == Wear.FindModel(s.PrinterModel).DefaultWatts && d.AutoWearRate == 1 && d.MachineRatePerHour == Wear.DefaultRateFor(s.PrinterModel));
        True("reset: other printers, used numbers, setup and look kept", d.Printers.Count == printersBefore && d.UsedInvoiceNos == "2026-11111" && d.SetupDone == 1 && d.QuoteDark == 0);
        True("reset: the settings it came from unchanged", s.FriendsMarkupPct == 40 && s.QuoteName == "Test 3D");
        L.Danish = wasDanish;
    }
}
