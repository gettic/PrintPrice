using System;
using System.Collections.Generic;

namespace PrintPrice.Web
{
    /// First-time setup on the website (idea 19, web part). The answers and the way they become settings are shared with
    /// the desktop (src\AppSettings.cs: SetupAnswers + AppSettings.ApplySetup); this file only has the web's rules and texts.
    /// Pure: web\tests runs it.
    public static class WebSetup
    {
        public const int Summary = SetupAnswers.Questions;   // step index of the summary page (after the 4 questions)
        public const double ExampleHours = 8, ExampleGrams = 150;

        /// A brand-new visitor: nothing of PrintPrice in this browser (no settings, no page prefs, no backup note, an empty
        /// history read without a problem) and the browser keeps what the site stores (else the setup would come back on
        /// every reload). Existing visitors always have a stored settings text.
        public static bool IsNewVisitor(string settingsText, string prefsText, string lastBackup, int quotes,
                                        string historyProblem, bool storageAvailable)
        {
            return storageAvailable && string.IsNullOrEmpty(settingsText) && string.IsNullOrEmpty(prefsText)
                && string.IsNullOrEmpty(lastBackup) && quotes == 0 && historyProblem == null;
        }

        /// The settings the page starts with: a new visitor gets SetupDone 0 (in memory only, nothing saved until the setup
        /// closes). A stored text keeps its own value (no key = 1, AppSettings' rule).
        public static void MarkNewVisitor(AppSettings s, bool newVisitor)
        {
            if (newVisitor) s.SetupDone = 0;
        }

        public static bool ShouldShow(AppSettings s) { return s != null && s.SetupDone == 0; }

        public static SetupPrinterMode Mode(bool again) { return again ? SetupPrinterMode.ChangeModel : SetupPrinterMode.Replace; }

        /// The settings the answers give (= what Finish saves). Auto wear worked out with the page's filament type.
        public static AppSettings Result(AppSettings start, SetupAnswers a, bool again, string filamentType)
        {
            var s = start.ApplySetup(a, Mode(again));
            WebCalc.ApplyAutoWear(s, filamentType);
            return s;
        }

        /// "Set up later" / ✕ / Esc on a first run: the steps not finished with Next go back to their opening answers.
        public static SetupAnswers Later(SetupAnswers answers, SetupAnswers opening, bool[] done)
        {
            var a = answers.Clone();
            for (int i = 0; i < SetupAnswers.Questions; i++)
                if (done == null || i >= done.Length || !done[i]) a.CopyStep(i, opening);
            return a;
        }

        /// The answer boxes that would be refused (red), for a step: names "filament", "elec", "friends", "others", "min".
        public static List<string> BadBoxes(SetupAnswers a, int step)
        {
            var bad = new List<string>();
            double v;
            if (step == SetupAnswers.StepCosts)
            {
                if (!SetupAnswers.ReadNumber(a.FilamentText, out v)) bad.Add("filament");
                if (!a.LivePower && !SetupAnswers.ReadNumber(a.ElecText, out v)) bad.Add("elec");
            }
            else if (step == SetupAnswers.StepPrices)
            {
                if (!SetupAnswers.ReadNumber(a.FriendsText, out v)) bad.Add("friends");
                if (!SetupAnswers.ReadNumber(a.OthersText, out v)) bad.Add("others");
                if (!SetupAnswers.ReadNumber(a.MinText, out v)) bad.Add("min");
            }
            return bad;
        }

        /// The example on step 4 (desktop UpdateExample): an 8 h print with 150 g, with the answers so far. Live power =
        /// the price fetched on step 3 for that area (livePrice < 0 = none: the last price). null = a box isn't a number.
        public static QuoteResult Example(AppSettings start, SetupAnswers a, bool again, string filamentType, double livePrice)
        {
            double kg, el, x;
            if (!SetupAnswers.ReadNumber(a.FilamentText, out kg) || !SetupAnswers.ReadNumber(a.FriendsText, out x)
                || !SetupAnswers.ReadNumber(a.OthersText, out x) || !SetupAnswers.ReadNumber(a.MinText, out x)) return null;
            if (a.LivePower) el = livePrice >= 0 ? livePrice : start.LastElectricityPrice;
            else if (!SetupAnswers.ReadNumber(a.ElecText, out el)) return null;
            var s = Result(start, a, again, filamentType);
            return Pricing.Calculate(new QuoteInput { Hours = (int)ExampleHours, Minutes = 0, FilamentGrams = ExampleGrams,
                                                      FilamentDkkPerKg = kg, ElectricityDkkPerKwh = el }, s);
        }

        // ---------- texts ----------

        public static string StepTitle(int step, bool again)
        {
            switch (step)
            {
                case SetupAnswers.StepLanguage: return again ? L.S("Setup", "Opsætning") : L.S("Welcome to PrintPrice", "Velkommen til PrintPrice");
                case SetupAnswers.StepPrinter: return L.S("Your printer", "Din printer");
                case SetupAnswers.StepCosts: return L.S("Filament and electricity", "Filament og strøm");
                case SetupAnswers.StepPrices: return L.S("Your prices", "Dine priser");
                default: return again ? L.S("Check and save", "Tjek og gem") : L.S("Ready to go", "Klar til brug");
            }
        }

        public static string StepCount(int step)
        {
            return step >= Summary ? L.S("Summary", "Oversigt") : L.F("Step {0} of {1}", "Trin {0} af {1}", step + 1, SetupAnswers.Questions);
        }

        /// A printer tile's two small lines: "100 W" and "1,71 kr/h wear" (two lines on purpose: a phone never leaves "wear" alone).
        public static string ModelWatts(Wear.Model m) { return Num.Fmt(m.DefaultWatts) + " W"; }

        public static string ModelRate(Wear.Model m)
        {
            return L.F("{0} kr/h wear", "{0} kr/t slid", Num.N2(Wear.DefaultRateFor(m.Key)));
        }

        /// The live price line on step 3 (desktop SetupForm.SetLive; the web says "the page" instead of "the app").
        public static string LiveText(LivePower.Result r, double addOn, bool vat)
        {
            if (r == null) return L.S("Getting the price now…", "Henter prisen nu…");
            if (!r.Ok) return L.S("Can't get the live price now. PrintPrice tries again next time you open the page.",
                                  "Kan ikke hente prisen nu. PrintPrice prøver igen, næste gang du åbner siden.");
            return vat ? L.F("Now {0} kr/kWh incl. {1} tariffs & fees and 25 % VAT", "Nu {0} kr/kWh inkl. {1} i afgifter og 25 % moms", Num.N2(r.Total), Num.N2(addOn))
                       : L.F("Now {0} kr/kWh incl. {1} tariffs & fees", "Nu {0} kr/kWh inkl. {1} i afgifter", Num.N2(r.Total), Num.N2(addOn));
        }

        public static string AreaName(string area)
        {
            return area == "DK2" ? L.S("DK2 – Zealand, Lolland & Bornholm", "DK2 – Sjælland, Lolland og Bornholm")
                                 : L.S("DK1 – Jutland & Funen", "DK1 – Jylland og Fyn");
        }

        public static string HintText { get { return L.S("Setup skipped", "Opsætning sprunget over"); } }
        public static string HintLink { get { return L.S("run it now", "kør den nu"); } }

        public sealed class Row
        {
            public string Caption, Value;
            public int Step;
            public bool Muted;   // skipped: the default / unchanged value
        }

        /// The summary page (desktop UpdateSummary, without the Bambu Studio row). s = Result(...); danish = the language
        /// chosen on step 1; skipped[i] = step i was skipped (or, first run, never finished with Next).
        public static List<Row> SummaryRows(AppSettings s, bool danish, bool[] skipped, bool again)
        {
            string def = again ? L.S("unchanged", "uændret") : L.S("default", "standard");
            var rows = new List<Row>();
            Action<string, string, int> add = (cap, text, step) =>
            {
                bool skip = skipped != null && step < skipped.Length && skipped[step];
                rows.Add(new Row { Caption = cap, Value = skip ? text + "  (" + def + ")" : text, Step = step, Muted = skip });
            };
            add(L.S("Language", "Sprog"), danish ? "Dansk" : "English", SetupAnswers.StepLanguage);
            add(L.S("Printer", "Printer"), AppSettings.CleanPrinterName(s.PrinterName) + " · " + Num.Fmt(s.PrinterWatts) + " W · "
                + L.F("{0} kr/h wear", "{0} kr/t slid", Num.N2(s.MachineRatePerHour)), SetupAnswers.StepPrinter);
            add(L.S("Filament price", "Filamentpris"), Num.Fmt(s.PriceFor(s.FilamentPreset)) + " kr/kg", SetupAnswers.StepCosts);
            add(L.S("Electricity", "Strøm"), s.AutoPowerPrice != 0 ? L.S("Live price, ", "Aktuel pris, ") + AreaName(s.PriceArea)
                                                                  : L.F("Fixed, {0} kr/kWh", "Fast, {0} kr/kWh", Num.N2(s.LastElectricityPrice)), SetupAnswers.StepCosts);
            add(L.S("Markups", "Avance"), L.F("Friends & family {0} % · customers {1} %", "Venner og familie {0} % · kunder {1} %",
                                             Num.Fmt(s.FriendsMarkupPct), Num.Fmt(s.OthersMarkupPct)), SetupAnswers.StepPrices);
            add(L.S("Minimum price", "Minimumspris"), s.MinimumPrice > 0 ? L.F("{0} kr. (customers)", "{0} kr. (kunder)", Num.Fmt(s.MinimumPrice))
                                                                       : L.S("Off", "Fra"), SetupAnswers.StepPrices);
            add(L.S("Name on quotes", "Navn på tilbud"), (s.QuoteName ?? "").Length > 0 ? s.QuoteName : L.S("none", "intet"), SetupAnswers.StepPrices);
            return rows;
        }
    }
}
