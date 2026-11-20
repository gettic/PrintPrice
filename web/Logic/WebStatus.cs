using System;
using System.Collections.Generic;

namespace PrintPrice.Web
{
    /// Issue #7 on the website: print statuses (History.PrintStateOf / SetPrintState, QuoteRows.StateName), the History
    /// filter's status values (History.ShowState*), the question before several prints are set to Failed, and Settings'
    /// "Reset to defaults" (AppSettings.DefaultsKeepingPersonal). Texts = the desktop's (HistoryForm, MainForm, SettingsForm).
    public static class WebStatus
    {
        public static readonly PrintState[] States = { PrintState.NotPrinted, PrintState.Printing, PrintState.Printed, PrintState.Failed };

        /// The mark before a status name: hollow circle, clock (drawn by the page), ✓, ✗.
        public static string Mark(PrintState s)
        {
            switch (s)
            {
                case PrintState.Printed: return "✓";
                case PrintState.Failed: return "✗";
                case PrintState.Printing: return "";   // the page draws its clock
                default: return "○";
            }
        }

        /// The CSS class of a status (colour of its mark): none / printing (amber) / printed (green) / failed (red).
        public static string Css(PrintState s)
        {
            switch (s)
            {
                case PrintState.Printing: return "st-printing";
                case PrintState.Printed: return "st-printed";
                case PrintState.Failed: return "st-failed";
                default: return "st-none";
            }
        }

        public static string Heading { get { return L.S("Print status", "Printstatus"); } }

        /// A combined window's heading: "Print status (all 3 prints)".
        public static string GroupHeading(int n) { return L.F("Print status (all {0} prints)", "Printstatus (alle {0} print)", n); }

        public static string Mixed { get { return L.S("Mixed", "Blandet"); } }

        /// The jobs' one status, or null when they differ ("Mixed").
        public static PrintState? Shared(IList<QuoteRecord> jobs, DateTime now)
        {
            PrintState? one = null;
            if (jobs == null) return null;
            foreach (var r in jobs)
            {
                var s = History.PrintStateOf(r, now);
                if (one == null) one = s;
                else if (one.Value != s) return null;
            }
            return one;
        }

        /// The question before setting these to Failed: only when 2+ of them aren't failed yet (the user's choice).
        public static int FailedToAsk(IEnumerable<QuoteRecord> recs, PrintState state)
        {
            if (state != PrintState.Failed || recs == null) return 0;
            int n = 0;
            foreach (var r in recs) if (r != null && !r.Failed) n++;
            return n > 1 ? n : 0;
        }

        public static string FailedQuestion(int n) { return L.F("Mark {0} prints as failed?", "Markér {0} print som fejlet?", n); }

        public static string FailedText
        {
            get
            {
                return L.S("Failed prints count as lost in the History totals, and with \"Earn back failed prints\" on, new quotes earn their cost back. You can change the status back at any time.",
                           "Fejlede print tæller som tab i historikkens tal, og med \"Tjen fejlprint ind igen\" slået til tjener nye tilbud deres pris ind. Du kan altid ændre status tilbage.");
            }
        }

        public static string Yes { get { return L.S("Yes", "Ja"); } }
        public static string No { get { return L.S("No", "Nej"); } }

        /// Sets the status on every record (one time for all).
        public static void Apply(IEnumerable<QuoteRecord> recs, PrintState state, DateTime now)
        {
            foreach (var r in recs) if (r != null) History.SetPrintState(r, state, now);
        }

        /// The quote window after a status was saved at once.
        public static string SavedText(PrintState s) { return L.F("✓ Print status saved: {0}.", "✓ Printstatus gemt: {0}.", QuoteRows.StateName(s)); }

        /// History's line after a status change (moved = a status filter is on and they left the list).
        public static string ListText(int n, PrintState s, bool moved)
        {
            string name = QuoteRows.StateName(s);
            string text = n == 1 ? L.F("✓ Print status: {0}.", "✓ Printstatus: {0}.", name)
                                 : L.F("✓ {0} quotes set to {1}.", "✓ {0} tilbud sat til {1}.", n, name);
            if (moved)
                text += " " + (n == 1 ? L.S("It is now under its new status in Show.", "Det ligger nu under sin nye status i Vis.")
                                      : L.S("They are now under their new status in Show.", "De ligger nu under deres nye status i Vis."));
            return text;
        }

        /// The History filter's Show value is one of the four statuses.
        public static bool IsStateShow(int show) { PrintState s; return History.StateOfShow(show, out s); }

        // ---------- Settings: Reset to defaults ----------

        public static string ResetButton { get { return L.S("Reset to defaults", "Nulstil til standard"); } }
        public static string ResetQuestion { get { return L.S("Reset to defaults?", "Nulstil til standard?"); } }

        public static string ResetText
        {
            get
            {
                return L.S("All settings on every tab go back to their defaults. Your name, payment details, Bambu Studio path and language " +
                           "are kept, and your price history, invoices and customers are not touched. Nothing is saved until you press Save; " +
                           "Cancel undoes it.",
                           "Alle indstillinger på alle faner sættes tilbage til standard. Dit navn, dine betalingsoplysninger, Bambu Studio-stien " +
                           "og sproget beholdes, og din prishistorik, dine fakturaer og kunder røres ikke. Intet gemmes, før du trykker Gem; " +
                           "Annullér fortryder det.");
            }
        }

        public static string ResetYes { get { return L.S("Reset", "Nulstil"); } }
        public static string Cancel { get { return L.S("Cancel", "Annullér"); } }
        public static string ResetDone { get { return L.S("Defaults filled in. Save stores them.", "Standarder udfyldt. Gem gemmer dem."); } }

        /// Settings' "Reset to defaults" (= SettingsForm.Defaults): every tab's values back to default, your own details
        /// kept (AppSettings.DefaultsKeepingPersonal); the printer shown keeps its name and model and gets that model's
        /// defaults; everything not on a tab stays (other printers and their ids, used invoice numbers, setup done,
        /// the quote window's choices). work is not changed.
        public static AppSettings Defaults(AppSettings work)
        {
            var d = work.DefaultsKeepingPersonal();
            var s = work.Clone();
            s.QuoteName = d.QuoteName; s.PayMobilePay = d.PayMobilePay; s.PayBank = d.PayBank; s.SlicerPath = d.SlicerPath; s.Language = d.Language;
            s.FriendsMarkupPct = d.FriendsMarkupPct; s.OthersMarkupPct = d.OthersMarkupPct;
            s.MinimumPrice = d.MinimumPrice; s.RoundTo = d.RoundTo; s.FailedPrintPct = d.FailedPrintPct;
            s.FailedPayback = d.FailedPayback; s.FailedPaybackPct = d.FailedPaybackPct; s.FailedPaybackWho = d.FailedPaybackWho;
            s.LastFilamentPrice = d.LastFilamentPrice; s.LastElectricityPrice = d.LastElectricityPrice;
            s.AutoPowerPrice = d.AutoPowerPrice; s.PowerAddOn = d.PowerAddOn; s.PowerIncludeVat = d.PowerIncludeVat; s.PriceArea = d.PriceArea;
            s.QuoteValidDays = d.QuoteValidDays; s.InvoiceDueDays = d.InvoiceDueDays; s.MainInvoice = d.MainInvoice;
            s.FilamentPreset = d.FilamentPreset;
            // the active printer: its name and model stay, the rest = that model's defaults
            var m = Wear.FindModel(s.PrinterModel);
            s.Nozzle = d.Nozzle; s.BedType = d.BedType; s.ProcessPreset = d.ProcessPreset;
            s.PrinterWatts = m.DefaultWatts; s.AutoWearRate = 1; s.MachineRatePerHour = Wear.DefaultRateFor(m.Key);
            return s;
        }
    }
}
