using System;
using System.Collections.Generic;
using System.Linq;
using PrintPrice;
using PrintPrice.Web;

// paid-tick on the website (plans\paid-tick): History's Invoice cell tick (History.SetPaid on the whole number), the
// "Unpaid invoices" filter, the invoice page's "Paid 05-11-2026 – thank you" line (single + combined), JSON + backup.
static class PaidTests
{
    static Action<string, bool> True;

    public static void Run(Action<string, bool> t, Action<string, double, double> eq)
    {
        True = t;
        bool wasDanish = L.Danish;
        L.Danish = false;
        try { Tick(); Pages(); JsonText(); }
        finally { L.Danish = wasDanish; }
    }

    static QuoteRecord Rec(string id, string model, int minsAgo, string invoiceNo = "", string paidAt = "")
    {
        var s = new AppSettings();
        var input = new QuoteInput { Hours = 1, FilamentGrams = 30, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 2 };
        var r = History.FromQuote(input, Pricing.Calculate(input, s), s, model, "", DateTime.Now.AddMinutes(-minsAgo));
        r.Id = id;
        if (invoiceNo.Length > 0) { r.InvoiceNo = invoiceNo; r.BilledAt = History.Stamp(DateTime.Now.AddMinutes(-minsAgo)); r.Printed = true; }
        r.PaidAt = paidAt;
        return r;
    }

    static List<QuoteRecord> List()
    {
        return new List<QuoteRecord>
        {
            Rec("a", "Benchy", 10, "2026-48213"), Rec("b", "Phone stand", 20, "2026-48213"),   // one combined invoice
            Rec("c", "Hook", 30, "2026-11111", "2026-11-05"),                                 // paid
            Rec("d", "Vase", 40),                                                             // a quote
        };
    }

    static void Tick()
    {
        var list = List();
        var f = new WebHistory.Filter { Show = WebHistory.ShowUnpaidInvoices };
        True("Unpaid invoices filter: the unpaid invoice's jobs only", string.Join(",", WebHistory.Apply(list, f, null).Select(r => r.Id)) == "a,b");
        True("IsUnpaidInvoice: a quote isn't", !WebHistory.IsUnpaidInvoice(list[3]) && !WebHistory.IsUnpaidInvoice(list[2]) && WebHistory.IsUnpaidInvoice(list[0]));
        True("cell texts", WebHistory.PaidMark(list[0]) == "Unpaid" && WebHistory.PaidMark(list[2]) == "✓ Paid"
             && WebHistory.PaidTip(list[2]) == "Paid 05-11-2026. Click to mark invoice 2026-11111 as unpaid.");
        var now = new DateTime(2026, 11, 20, 15, 0, 0);
        bool paid = WebHistory.TogglePaid(list, list[1], now);
        True("tick: the whole number paid (both jobs), today's date", paid && list[0].PaidAt == "2026-11-20" && list[1].PaidAt == "2026-11-20" && list[3].PaidAt == "");
        True("filter after the tick: none unpaid", WebHistory.Apply(list, f, null).Count == 0);
        True("done line: " + WebHistory.PaidDoneText("2026-48213", true, 2), WebHistory.PaidDoneText("2026-48213", true, 2) == "✓ Invoice 2026-48213 marked as paid (2 prints).");
        paid = WebHistory.TogglePaid(list, list[0], now);
        True("untick: both unpaid again", !paid && list[0].PaidAt == "" && list[1].PaidAt == "");
        True("sheet button", WebHistory.PaidButton(list[0]) == "Mark as paid" && WebHistory.PaidButton(list[2]) == "Mark as unpaid");
        L.Danish = true;
        True("DA texts", WebHistory.PaidMark(list[2]) == "✓ Betalt" && WebHistory.PaidMark(list[0]) == "Ubetalt" && WebHistory.ShowUnpaidText == "Ubetalte fakturaer"
             && WebHistory.PaidDoneText("2026-11111", false, 1) == "✓ Faktura 2026-11111 markeret som ubetalt." && WebHistory.PaidButton(list[0]) == "Markér som betalt");
        L.Danish = false;
        // other Show values still work as before
        True("Show all unchanged", WebHistory.Apply(list, new WebHistory.Filter(), null).Count == 4);
    }

    static void Pages()
    {
        var s = new AppSettings();
        var list = List();
        var c = list[2];
        var d = WebQuote.FromRecord(c, s, false, "", true, DateTime.Now, null);
        True("paid invoice: \"Paid 05-11-2026 – thank you\" instead of Please pay by", WebQuote.TotalNote(d) == "Paid 05-11-2026 – thank you");
        s.PayMobilePay = "12 34 56 78";
        True("paid invoice: no Payment block", WebQuote.PayLines(WebQuote.FromRecord(c, s, false, "", true, DateTime.Now, null)).Count == 0
             && WebQuote.PayLines(WebQuote.FromRecord(list[0], s, false, "", true, DateTime.Now, null)).Count == 1);
        L.Danish = true;
        True("DA: Betalt 05-11-2026 – tak", WebQuote.TotalNote(WebQuote.FromRecord(c, s, false, "", true, DateTime.Now, null)) == "Betalt 05-11-2026 – tak");
        L.Danish = false;
        True("unpaid invoice: Please pay by", WebQuote.TotalNote(WebQuote.FromRecord(list[0], s, false, "", true, DateTime.Now, null)).StartsWith("Please pay by"));
        True("quote of a paid record: Shipping line", WebQuote.TotalNote(WebQuote.FromRecord(c, s, false, "", false, DateTime.Now, null)) == "Shipping not included");
        var jobs = new List<QuoteRecord> { list[1], list[0] };
        True("combined, unpaid: Please pay by", WebQuote.TotalNote(WebQuote.FromRecords(jobs, s, false, "", true, DateTime.Now, DateTime.Now, null)).StartsWith("Please pay by"));
        list[0].PaidAt = "2026-11-03"; list[1].PaidAt = "2026-11-06";
        var g = WebQuote.FromRecords(jobs, s, false, "", true, DateTime.Now, DateTime.Now, null);
        True("combined, all paid: the latest date", WebQuote.TotalNote(g) == "Paid 06-11-2026 – thank you");
        True("combined, all paid: no Payment block", WebQuote.PayLines(g).Count == 0);
        list[0].PaidAt = "";
        True("combined, one unpaid: Please pay by", WebQuote.TotalNote(WebQuote.FromRecords(jobs, s, false, "", true, DateTime.Now, DateTime.Now, null)).StartsWith("Please pay by"));
        True("combined, one unpaid: Payment block kept", WebQuote.PayLines(WebQuote.FromRecords(jobs, s, false, "", true, DateTime.Now, DateTime.Now, null)).Count == 1);
        True("Copy text (shared QuoteRows) of a paid invoice: paid, no payment lines", QuoteRows.RecordText(c, false, true, s, "").Contains("Paid 05-11-2026")
             && !QuoteRows.RecordText(c, false, true, s, "").Contains("MobilePay"));
        // the drawn page has the line
        list[0].PaidAt = "2026-11-03";
        bool drawn = WebQuote.DrawPages(WebQuote.FromRecords(jobs, s, false, "", true, DateTime.Now, DateTime.Now, null), false)
                             .Any(p => p.Items.Any(i => (i.Text ?? "").ToLowerInvariant().Contains("paid 06-11-2026 – thank you")));
        True("combined page draws the paid line", drawn);
    }

    static void JsonText()
    {
        var list = List();
        string json = History.ToJson(list);
        True("JSON: PaidAt written on every record (\"\" unpaid), as the desktop", json.Contains("\"PaidAt\":\"2026-11-05\"") && json.Contains("\"PaidAt\":\"\""));
        var back = History.ParseJson(json);
        True("JSON round trip keeps PaidAt", back != null && back[2].PaidAt == "2026-11-05" && back[0].PaidAt == "" && History.ToJson(back) == json);
        var old = History.ParseJson(json.Replace(",\"PaidAt\":\"2026-11-05\"", "").Replace(",\"PaidAt\":\"\"", ""));
        True("old file without PaidAt: unpaid", old != null && old.All(r => !History.IsPaid(r)));
        BackupData data;
        string problem = History.ReadBackup(History.ToBackup(list, null, "web", DateTime.Now), out data);
        True("backup keeps PaidAt", problem == null && data.Quotes.Single(r => r.Id == "c").PaidAt == "2026-11-05");
    }
}
