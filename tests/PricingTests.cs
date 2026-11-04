using System;
using System.Collections.Generic;

namespace PrintPrice.Tests
{
    static class PricingTests
    {
        static int failures = 0;

        static void Eq(string name, double expected, double actual)
        {
            bool ok = Math.Abs(expected - actual) < 0.005;
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + ": expected " + expected.ToString("0.00") + ", got " + actual.ToString("0.00"));
            if (!ok) failures++;
        }

        /// List joined with "|", null entries shown as "~", null list as "NULL".
        static string J(List<string> l)
        {
            if (l == null) return "NULL";
            var parts = new List<string>();
            foreach (var s in l) parts.Add(s == null ? "~" : s);
            return string.Join("|", parts.ToArray());
        }

        static void True(string name, bool cond)
        {
            Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
            if (!cond) failures++;
        }

        static QuoteInput Example()
        {
            return new QuoteInput { Hours = 5, Minutes = 30, FilamentGrams = 120, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2.00 };
        }

        // ---------- price history (History.cs) ----------
        static AppSettings HistSettings()
        {
            var s = new AppSettings();
            s.PrinterWatts = 100; s.FriendsMarkupPct = 25; s.OthersMarkupPct = 75; s.MachineRatePerHour = 2;
            s.FailedPrintPct = 10; s.MinimumPrice = 0; s.RoundTo = 0;
            return s;
        }

        static QuoteRecord HistRecord(DateTime t)
        {
            var s = HistSettings();
            return History.FromQuote(Example(), Pricing.Calculate(Example(), s), s, "Benchy", "https://www.printables.com/model/1", t);
        }

        static void HistoryTests()
        {
            var t0 = new DateTime(2026, 11, 3, 14, 5, 0);
            var s = HistSettings();
            var rec = HistRecord(t0);
            // Example: 0,120 x 180 = 21,60 material; 5,5 h x 0,1 kW = 0,55 kWh x 2,00 = 1,10 power; 22,70 x 10 % = 2,27 failed
            // base 24,97; friends 24,97 x 1,25 = 31,2125 + 5,5 x 2 = 42,2125; customer 24,97 x 1,75 = 43,6975 + 11 = 54,6975
            True("History SavedAt", rec.SavedAt == "2026-11-03 14:05:00");
            True("History Id starts with time", rec.Id.StartsWith("20261103140500000") && rec.Id.Length == 21);
            True("History Model/Source", rec.Model == "Benchy" && rec.Source == "https://www.printables.com/model/1");
            True("History time", rec.Hours == 5 && rec.Minutes == 30);
            Eq("History Grams", 120, rec.Grams);
            Eq("History FilamentDkkPerKg", 180, rec.FilamentDkkPerKg);
            Eq("History Electricity", 2.00, rec.ElectricityDkkPerKwh);
            // one colour: one line with the record's own numbers (120 g, 180 kr./kg); ui fills Name/Colour/Type
            True("History single filament -> 1 line", rec.Filaments != null && rec.Filaments.Count == 1);
            Eq("History single line grams 120", 120, rec.Filaments[0].Grams);
            Eq("History single line price 180", 180, rec.Filaments[0].PricePerKg);
            True("History single line name empty until ui fills it", rec.Filaments[0].Name == "" && rec.Filaments[0].Colour == "");
            True("History SingleLine = that line", History.SingleLine(rec) == rec.Filaments[0]);
            True("History ToInput one line -> single formula (Filaments null)", History.ToInput(rec).Filaments == null);
            // an input with a 1-line Filaments list is saved from FilamentGrams/FilamentDkkPerKg too (what Pricing used)
            var oneIn = Example();
            oneIn.Filaments = new List<FilamentLine> { new FilamentLine { Grams = 120, PricePerKg = 180 } };
            var oneRec = History.FromQuote(oneIn, Pricing.Calculate(oneIn, s), s, "Benchy", "", t0);
            True("History 1-line input -> 1 line 120 g x 180", oneRec.Filaments.Count == 1 && oneRec.Filaments[0].Grams == 120
                 && oneRec.Filaments[0].PricePerKg == 180);
            Eq("History 1-line input same customer price 54,6975", 54.6975, oneRec.OthersPrice);
            Eq("History MaterialCost 21,60", 21.60, rec.MaterialCost);
            Eq("History PowerCost 1,10", 1.10, rec.PowerCost);
            Eq("History FailedPrintCost 2,27", 2.27, rec.FailedPrintCost);
            Eq("History BaseCost 24,97", 24.97, rec.BaseCost);
            Eq("History FriendsPrice 42,2125", 42.2125, rec.FriendsPrice);
            Eq("History OthersPrice 54,6975", 54.6975, rec.OthersPrice);
            Eq("History snapshot FailedPrintPct", 10, rec.FailedPrintPct);
            Eq("History snapshot MachineRate", 2, rec.MachineRatePerHour);
            True("History new: For customer, Note empty, not failed", rec.For == History.ForCustomer && rec.Note == "" && !rec.Failed && rec.FailedAt == "");
            Eq("History PriceFor customer = 54,6975", 54.6975, History.PriceFor(rec));
            rec.For = History.ForFriends;
            Eq("History PriceFor friends = 42,2125", 42.2125, History.PriceFor(rec));
            rec.For = History.ForCustomer;

            // price as it was, even after today's markup changes; today's price with today's settings
            var today = HistSettings();
            today.FriendsMarkupPct = 50;
            var asWas = Pricing.Calculate(History.ToInput(rec), History.SavedSettings(rec, today));
            Eq("History as it was: friends 42,2125", 42.2125, asWas.FriendsPrice);
            Eq("History as it was: customer 54,6975", 54.6975, asWas.OthersPrice);
            True("History SavedSettings doesn't change current", today.FriendsMarkupPct == 50);
            // 24,97 x 1,50 = 37,455 + 11 = 48,455
            Eq("History today: friends 48,455", 48.455, Pricing.Calculate(History.ToInput(rec), today).FriendsPrice);
            // old one-colour record (before v1.6: Filaments null) prices exactly as the new 1-line one
            var oldRec = HistRecord(t0);
            oldRec.Filaments = null;
            True("History SingleLine old record -> null", History.SingleLine(oldRec) == null);
            var oldWas = Pricing.Calculate(History.ToInput(oldRec), History.SavedSettings(oldRec, today));
            Eq("History old record as it was: material 21,60", 21.60, oldWas.MaterialCost);
            Eq("History old record as it was: friends 42,2125", 42.2125, oldWas.FriendsPrice);
            Eq("History old record as it was: customer 54,6975", 54.6975, oldWas.OthersPrice);
            Eq("History old record today: friends 48,455", 48.455, Pricing.Calculate(History.ToInput(oldRec), today).FriendsPrice);

            // multi-colour: 50 g x 200 = 10,00 + 30 g x 300 = 9,00 -> 19,00 material
            var mi = new QuoteInput { Hours = 1, Minutes = 0, FilamentGrams = 80, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2.00,
                Filaments = new List<FilamentLine> { new FilamentLine { Grams = 50, PricePerKg = 200 }, new FilamentLine { Grams = 30, PricePerKg = 300 } } };
            var mrec = History.FromQuote(mi, Pricing.Calculate(mi, s), s, "Multi", "", t0.AddHours(-1));
            True("History multi: 2 lines", mrec.Filaments != null && mrec.Filaments.Count == 2);
            mrec.Filaments[0].Colour = "#FF0000"; mrec.Filaments[0].Type = "PLA"; mrec.Filaments[0].Name = "Bambu PLA Basic";
            mrec.PurgeGrams = 4.5;
            Eq("History multi MaterialCost 19,00", 19.00, mrec.MaterialCost);

            // JSON round trip under da-DK (decimal comma must not leak into the file)
            var oldCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            List<QuoteRecord> back;
            string json;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("da-DK");
                var two = new List<QuoteRecord> { rec, mrec };
                rec.Note = "Til Anne, rød"; rec.For = 2; rec.ElectricityDkkPerKwh = 2.27;
                rec.Filaments[0].Name = "Bambu PLA Matte"; rec.Filaments[0].Colour = "#FFFFFF"; rec.Filaments[0].Type = "PLA";
                json = History.ToJson(two);
                back = History.ParseJson(json);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = oldCulture; }
            True("History JSON has Version 1", json.Contains("\"Version\":1"));
            True("History JSON uses decimal point", json.Contains("2.27") && !json.Contains("2,27"));
            True("History JSON round trip: 2 quotes", back != null && back.Count == 2);
            if (back != null && back.Count == 2)
            {
                Eq("History round trip 2,27 kr./kWh", 2.27, back[0].ElectricityDkkPerKwh);
                True("History round trip Note/For/Id", back[0].Note == "Til Anne, rød" && back[0].For == 2 && back[0].Id == rec.Id);
                Eq("History round trip FriendsPrice", 42.2125, back[0].FriendsPrice);
                True("History round trip single -> 1 line with name/colour", back[0].Filaments != null && back[0].Filaments.Count == 1
                     && back[0].Filaments[0].Name == "Bambu PLA Matte" && back[0].Filaments[0].Colour == "#FFFFFF"
                     && back[0].Filaments[0].Type == "PLA");
                Eq("History round trip single line price 180", 180, back[0].Filaments[0].PricePerKg);
                // 2,27 kr./kWh: power 0,55 x 2,27 = 1,2485; base (21,60 + 1,2485) x 1,10 = 25,13335; x 1,75 = 43,98336 + 11 = 54,98336
                Eq("History round trip single as it was 54,9834",
                   54.98336, Pricing.Calculate(History.ToInput(back[0]), History.SavedSettings(back[0], null)).OthersPrice);
                True("History round trip multi lines", back[1].Filaments != null && back[1].Filaments.Count == 2
                     && back[1].Filaments[0].Colour == "#FF0000" && back[1].Filaments[0].Type == "PLA"
                     && back[1].Filaments[0].Name == "Bambu PLA Basic");
                Eq("History round trip line 2 price", 300, back[1].Filaments[1].PricePerKg);
                Eq("History round trip PurgeGrams", 4.5, back[1].PurgeGrams);
                Eq("History round trip multi material 19,00",
                   19.00, Pricing.Calculate(History.ToInput(back[1]), History.SavedSettings(back[1], null)).MaterialCost);
            }
            rec.Note = ""; rec.For = History.ForCustomer; rec.ElectricityDkkPerKwh = 2.00;
            rec.Filaments[0].Name = ""; rec.Filaments[0].Colour = ""; rec.Filaments[0].Type = "";

            // an old file's one-colour quote (no Filaments) still loads and prices as before (numbers as Example + HistSettings)
            var oldJson = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Hours\":5,\"Minutes\":30,\"Grams\":120,\"FilamentDkkPerKg\":180,"
                + "\"ElectricityDkkPerKwh\":2,\"PrinterWatts\":100,\"MachineRatePerHour\":2,\"FailedPrintPct\":10,"
                + "\"FriendsMarkupPct\":25,\"OthersMarkupPct\":75},{\"Grams\":5,\"Filaments\":[]}]}");
            True("History old JSON: 2 quotes, no lines", oldJson != null && oldJson.Count == 2 && oldJson[0].Filaments == null
                 && oldJson[1].Filaments == null);
            if (oldJson != null && oldJson.Count == 2)
            {
                var ow = Pricing.Calculate(History.ToInput(oldJson[0]), History.SavedSettings(oldJson[0], null));
                Eq("History old JSON as it was: friends 42,2125", 42.2125, ow.FriendsPrice);
                Eq("History old JSON as it was: customer 54,6975", 54.6975, ow.OthersPrice);
            }

            // ParseJson: not a history file -> null; missing fields -> defaults
            True("History ParseJson empty -> null", History.ParseJson("") == null);
            True("History ParseJson null -> null", History.ParseJson(null) == null);
            True("History ParseJson garbage -> null", History.ParseJson("garbage{") == null);
            True("History ParseJson array -> null", History.ParseJson("[1,2]") == null);
            True("History ParseJson Version 2 -> null", History.ParseJson("{\"Version\":2,\"Quotes\":[]}") == null);
            True("History ParseJson no Version -> null", History.ParseJson("{\"Quotes\":[]}") == null);
            var empty = History.ParseJson("{\"Version\":1,\"Quotes\":[]}");
            True("History ParseJson empty list", empty != null && empty.Count == 0);
            var partial = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Hours\":3,\"Note\":null},null]}");
            True("History ParseJson partial: 1 quote, defaults", partial != null && partial.Count == 1 && partial[0].Hours == 3
                 && partial[0].Note == "" && partial[0].Model == "" && partial[0].Filaments == null && partial[0].PurgeGrams == -1
                 && partial[0].For == History.ForCustomer);
            var forOld = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"For\":0},{\"For\":1},{\"For\":7}]}");
            True("History ParseJson For: old 0 -> customer, 1 kept, unknown -> customer", forOld != null && forOld.Count == 3
                 && forOld[0].For == 2 && forOld[1].For == 1 && forOld[2].For == 2);

            // SameQuote / Add: within 10 min replaces (keeps Id, For, Note), later or different adds
            var list = new List<QuoteRecord>();
            History.Add(list, HistRecord(t0));
            list[0].Note = "keep me"; list[0].For = 1;
            string firstId = list[0].Id;
            History.Add(list, HistRecord(t0.AddMinutes(9)));
            True("History Add same within 9 min -> replaced", list.Count == 1);
            True("History replace keeps Id/Note/For", list[0].Id == firstId && list[0].Note == "keep me" && list[0].For == 1);
            True("History replace has new time", list[0].SavedAt == "2026-11-03 14:14:00");
            True("History SameQuote exactly 10 min", History.SameQuote(HistRecord(t0), HistRecord(t0.AddMinutes(10))));
            True("History SameQuote 11 min -> no", !History.SameQuote(HistRecord(t0), HistRecord(t0.AddMinutes(11))));
            History.Add(list, HistRecord(t0.AddMinutes(9 + 11)));
            True("History Add 11 min later -> new entry", list.Count == 2 && list[0].SavedAt == "2026-11-03 14:25:00");
            var other = HistRecord(t0.AddMinutes(21));
            other.Grams = 121;
            True("History SameQuote other grams -> no", !History.SameQuote(list[0], other));
            History.Add(list, other);
            True("History Add different -> new entry, newest first", list.Count == 3 && list[0].Grams == 121);
            True("History SameQuote multi vs single -> no", !History.SameQuote(mrec, rec));

            // Trim: 502 -> 500; an old failed one is kept
            var big = new List<QuoteRecord>();
            for (int i = 0; i < 502; i++) { var q = new QuoteRecord(); q.Id = "q" + i; big.Add(q); }
            History.Trim(big, 500);
            True("History Trim 502 -> 500", big.Count == 500 && big[499].Id == "q499");
            big.Clear();
            for (int i = 0; i < 502; i++) { var q = new QuoteRecord(); q.Id = "q" + i; big.Add(q); }
            History.MarkFailed(big[501], true, t0);
            big[501].MaterialCost = 10;   // a failed print that cost something (a 0 kr. one counts as made back)
            History.Trim(big, 500);
            True("History Trim keeps old failed", big.Count == 501 && big[500].Id == "q501" && !big.Exists(x => x.Id == "q500"));
            True("History MarkFailed sets date", big[500].Failed && big[500].FailedAt == "2026-11-03 14:05:00");
            History.MarkFailed(big[500], false, t0);
            True("History MarkFailed off clears date", !big[500].Failed && big[500].FailedAt == "");
            True("History Remove by Id", History.Remove(big, "q7") && big.Count == 500 && !big.Exists(x => x.Id == "q7"));   // 501 - 1
            True("History Remove unknown Id", !History.Remove(big, "nope") && big.Count == 500);

            // files: only in bin\test-history (test mode), never %APPDATA%
            string histDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-history");
            string histFile = System.IO.Path.Combine(histDir, "history.json");
            try
            {
                True("History UseFolder test-history", AppSettings.UseFolder(histDir));
                True("History FilePath in test folder", string.Equals(History.FilePath, histFile, StringComparison.OrdinalIgnoreCase));
                True("AppSettings.Folder = test folder", string.Equals(AppSettings.Folder, histDir, StringComparison.OrdinalIgnoreCase));
                System.IO.Directory.CreateDirectory(histDir);
                // clean leftovers of earlier runs (this test's own folder only)
                foreach (var f in System.IO.Directory.GetFiles(histDir, "history*.json*")) System.IO.File.Delete(f);
                History.ResetBlock();
                string problem;
                var none = History.Load(out problem);
                True("History Load missing file -> empty, no problem", none.Count == 0 && problem == null);

                var saved = new List<QuoteRecord> { rec, mrec };
                True("History Save OK", History.Save(saved) == null);
                True("History Save left no .tmp", System.IO.File.Exists(histFile) && !System.IO.File.Exists(histFile + ".tmp"));
                True("History Save again (replace) OK", History.Save(saved) == null);
                var loaded = History.Load(out problem);
                True("History Load round trip", problem == null && loaded.Count == 2 && loaded[0].Id == rec.Id && loaded[1].Filaments.Count == 2);

                // corrupt file: backed up, empty list, settings.ini untouched
                var st = new AppSettings(); st.FriendsMarkupPct = 37; st.Save();
                string iniBefore = System.IO.File.ReadAllText(AppSettings.FilePath);
                System.IO.File.WriteAllText(histFile, "garbage{");
                var afterBad = History.Load(out problem);
                var bads = System.IO.Directory.GetFiles(histDir, "history.bad-*.json");
                True("History corrupt -> empty list + problem", afterBad.Count == 0 && problem != null);
                True("History corrupt -> backed up", bads.Length == 1 && System.IO.File.ReadAllText(bads[0]) == "garbage{" && !System.IO.File.Exists(histFile));
                True("History corrupt -> settings.ini unchanged", System.IO.File.ReadAllText(AppSettings.FilePath) == iniBefore);
                True("History Save after backup OK", History.Save(new List<QuoteRecord> { rec }) == null && History.Load(out problem).Count == 1);
                // newer version: backed up too, never overwritten unread
                System.IO.File.WriteAllText(histFile, "{\"Version\":2,\"Quotes\":[]}");
                History.Load(out problem);
                True("History Version 2 -> backed up", problem != null && System.IO.Directory.GetFiles(histDir, "history.bad-*.json").Length == 2);
                True("History BackupName unique", !System.IO.File.Exists(History.BackupName(histFile, DateTime.Now)));
            }
            finally
            {
                History.ResetBlock();
                AppSettings.ClearTestFolder();
            }
            True("History FilePath back to %APPDATA%", string.Equals(History.FilePath,
                 System.IO.Path.Combine(AppSettings.RealFolder, "history.json"), StringComparison.OrdinalIgnoreCase));
        }

        // ---------- failed prints payback (idea 9) ----------
        static AppSettings PaySettings(double who)
        {
            var s = HistSettings();
            s.FailedPayback = 1; s.FailedPaybackPct = 25; s.FailedPaybackWho = who;
            return s;
        }

        static QuoteInput Owing(double debt)
        {
            var q = Example();
            q.PaybackDebt = debt;
            return q;
        }

        // a saved quote that paid back `payback` kr.
        static QuoteRecord Payer(DateTime t, double payback, int forWho, double who)
        {
            var r = HistRecord(t);
            r.PaybackCost = payback; r.For = forWho; r.FailedPaybackWho = who;
            return r;
        }

        static void PaybackTests()
        {
            // Example with HistSettings: material 21,60 + power 1,10 = 22,70; failed 10 % = 2,27; base 24,97; wear 5,5 h x 2 = 11
            // friends 42,2125, customer 54,6975 (see HistoryTests). Payback share 22,70 x 25 % = 5,675.
            // Failed cost of the Example record = 21,60 + 1,10 + 11 = 33,70.
            var d = new AppSettings();
            Eq("FailedPayback default 0 (off)", 0, d.FailedPayback);
            Eq("FailedPaybackPct default 25", 25, d.FailedPaybackPct);
            Eq("FailedPaybackWho default 2 (customers only)", 2, d.FailedPaybackWho);
            Eq("Clone keeps FailedPaybackWho", 1, new AppSettings { FailedPaybackWho = 1 }.Clone().FailedPaybackWho);

            var off = Pricing.Calculate(Owing(33.70), HistSettings());
            Eq("Payback off: payback 0", 0, off.PaybackCost);
            Eq("Payback off: left 0", 0, off.PaybackLeft);
            Eq("Payback off: friends 42,2125", 42.2125, off.FriendsPrice);
            Eq("Payback off: customer 54,6975", 54.6975, off.OthersPrice);

            var both = Pricing.Calculate(Owing(33.70), PaySettings(1));
            Eq("Payback both: 22,70 x 25 % = 5,675", 5.675, both.PaybackCost);
            Eq("Payback both: left 33,70 - 5,675 = 28,025", 28.025, both.PaybackLeft);
            True("Payback both: PaybackBothPrices", both.PaybackBothPrices);
            Eq("Payback both: friends 42,2125 + 5,675 = 47,8875", 47.8875, both.FriendsPrice);
            Eq("Payback both: customer 54,6975 + 5,675 = 60,3725", 60.3725, both.OthersPrice);
            Eq("Payback not in base cost (24,97)", 24.97, both.BaseCost);

            var cust = Pricing.Calculate(Owing(33.70), PaySettings(2));
            Eq("Payback customers only: payback 5,675", 5.675, cust.PaybackCost);
            True("Payback customers only: not both", !cust.PaybackBothPrices);
            Eq("Payback customers only: friends unchanged 42,2125", 42.2125, cust.FriendsPrice);
            Eq("Payback customers only: customer 60,3725", 60.3725, cust.OthersPrice);

            var little = Pricing.Calculate(Owing(3.00), PaySettings(1));
            Eq("Payback capped at debt: min(3,00, 5,675) = 3,00", 3.00, little.PaybackCost);
            Eq("Payback capped: left 0", 0, little.PaybackLeft);
            Eq("Payback capped: friends 42,2125 + 3 = 45,2125", 45.2125, little.FriendsPrice);
            Eq("Payback capped: customer 54,6975 + 3 = 57,6975", 57.6975, little.OthersPrice);
            Eq("Payback no debt: 0", 0, Pricing.Calculate(Owing(0), PaySettings(1)).PaybackCost);

            var smin = PaySettings(2); smin.MinimumPrice = 100;
            Eq("Payback after minimum: max(54,6975, 100) + 5,675 = 105,675", 105.675, Pricing.Calculate(Owing(33.70), smin).OthersPrice);
            var sround = PaySettings(1); sround.RoundTo = 5;
            var rounded = Pricing.Calculate(Owing(33.70), sround);
            Eq("Payback then rounding: friends 47,8875 -> 50", 50, rounded.FriendsPrice);
            Eq("Payback then rounding: customer 60,3725 -> 65", 65, rounded.OthersPrice);
            Eq("Payback rounding extra not counted (5,675)", 5.675, rounded.PaybackCost);

            // record keeps the payback; as it was, even after today's settings change
            var t = new DateTime(2026, 11, 3, 10, 0, 0);
            var ps = PaySettings(1);
            var prec = History.FromQuote(Owing(33.70), Pricing.Calculate(Owing(33.70), ps), ps, "Benchy", "", t);
            Eq("Record PaybackCost 5,675", 5.675, prec.PaybackCost);
            Eq("Record FailedPaybackPct 25", 25, prec.FailedPaybackPct);
            Eq("Record FailedPaybackWho 1", 1, prec.FailedPaybackWho);
            var today = PaySettings(2); today.FailedPaybackPct = 50; today.FailedPayback = 0;
            var asWas = Pricing.Calculate(History.ToInput(prec), History.SavedSettings(prec, today));
            Eq("Payback as it was: friends 47,8875", 47.8875, asWas.FriendsPrice);
            Eq("Payback as it was: customer 60,3725", 60.3725, asWas.OthersPrice);
            var crec = History.FromQuote(Owing(33.70), cust, PaySettings(2), "Benchy", "", t);
            var asWas2 = Pricing.Calculate(History.ToInput(crec), History.SavedSettings(crec, today));
            Eq("Payback as it was, customers only: friends 42,2125", 42.2125, asWas2.FriendsPrice);
            Eq("Payback as it was, customers only: customer 60,3725", 60.3725, asWas2.OthersPrice);

            // debt timeline. A = failed Example (saved 10:00, failed 10:30); B, C pay 5,675 at 11:00, 12:00 (customer)
            var A = HistRecord(t);
            History.MarkFailed(A, true, t.AddMinutes(30));
            Eq("FailedCost 21,60 + 1,10 + 5,5 x 2 = 33,70", 33.70, History.FailedCost(A));
            var B = Payer(t.AddHours(1), 5.675, History.ForCustomer, 2);
            var C = Payer(t.AddHours(2), 5.675, History.ForCustomer, 2);
            Eq("Debt: A only 33,70", 33.70, History.FailedDebt(new List<QuoteRecord> { A }, null));
            Eq("Debt: empty list 0", 0, History.FailedDebt(new List<QuoteRecord>(), null));
            Eq("Debt: no failed, only payers 0", 0, History.FailedDebt(new List<QuoteRecord> { C, B }, null));
            var abc = new List<QuoteRecord> { C, B, A };
            Eq("Debt: A+B+C 33,70 - 11,35 = 22,35", 22.35, History.FailedDebt(abc, null));
            Eq("Debt: exclude C 33,70 - 5,675 = 28,025", 28.025, History.FailedDebt(abc, C.Id));
            Eq("Debt: exclude \"\" = none, 22,35", 22.35, History.FailedDebt(abc, ""));
            // customers only: a Friends quote doesn't pay back; with both prices it does
            B.For = History.ForFriends;
            Eq("Debt: B for friends (customers only) doesn't count, 28,025", 28.025, History.FailedDebt(abc, null));
            B.FailedPaybackWho = 1;
            Eq("Debt: B for friends but in both prices counts, 22,35", 22.35, History.FailedDebt(abc, null));
            B.For = History.ForCustomer; B.FailedPaybackWho = 2;
            // a failed record's own payback is ignored
            A.PaybackCost = 2;
            Eq("Debt: A's own PaybackCost ignored, 33,70", 33.70, History.FailedDebt(new List<QuoteRecord> { A }, null));
            A.PaybackCost = 0;
            // a payer saved before the failure doesn't count for it
            var early = Payer(t.AddMinutes(15), 5, History.ForCustomer, 2);
            Eq("Debt: payback before the failure (10:15 < 10:30) no count, 33,70", 33.70,
               History.FailedDebt(new List<QuoteRecord> { early, A }, null));
            // delete a payer -> debt comes back
            var ac = new List<QuoteRecord> { C, B, A };
            History.Remove(ac, B.Id);
            Eq("Debt: B deleted 33,70 - 5,675 = 28,025", 28.025, History.FailedDebt(ac, null));
            // unreadable FailedAt -> SavedAt (10:00) is used
            A.FailedAt = "x";
            Eq("Debt: unreadable FailedAt uses SavedAt, 22,35", 22.35, History.FailedDebt(abc, null));
            History.MarkFailed(A, true, t.AddMinutes(30));
            // PaidBack counts
            A.PaidBack = 10;
            Eq("Debt: PaidBack 10 -> 33,70 - 10 = 23,70", 23.70, History.FailedDebt(new List<QuoteRecord> { A }, null));
            A.PaidBack = 0;
            // two failures: 2 x 33,70 - 11,35 = 56,05
            var E = HistRecord(t.AddMinutes(75));
            History.MarkFailed(E, true, t.AddMinutes(90));
            Eq("Debt: two failures 67,40 - 11,35 = 56,05", 56.05, History.FailedDebt(new List<QuoteRecord> { C, E, B, A }, null));
            // unmark A: B and C paid for nothing -> 0, and no credit for a later failure D (saved 12:30, failed 13:00)
            History.MarkFailed(A, false, t);
            Eq("Debt: A unmarked 0", 0, History.FailedDebt(abc, null));
            var D = HistRecord(t.AddMinutes(150));
            History.MarkFailed(D, true, t.AddHours(3));
            Eq("Debt: new failure after unmark 33,70 (no credit)", 33.70, History.FailedDebt(new List<QuoteRecord> { D, C, B, A }, null));

            // Trim keeps the debt: q0 (kept, newest) pays 5,675 at 12:00; P (index 500, dropped) pays 5,675 at 11:00;
            // A (index 501) failed 10:30. Debt 33,70 - 11,35 = 22,35 before and after; A keeps PaidBack 5,675.
            History.MarkFailed(A, true, t.AddMinutes(30));
            var big = TrimList(A, 5.675, 5.675);
            Eq("Trim payback: debt before 22,35", 22.35, History.FailedDebt(big, null));
            History.Trim(big, 500);
            True("Trim payback: P dropped, A kept (501)", big.Count == 501 && big[500] == A && !big.Exists(x => x.Id == "P"));
            Eq("Trim payback: A.PaidBack 5,675", 5.675, A.PaidBack);
            Eq("Trim payback: debt after 22,35", 22.35, History.FailedDebt(big, null));
            // dropped P (40) pays A in full -> A dropped too
            A.PaidBack = 0;
            big = TrimList(A, 0, 40);
            History.Trim(big, 500);
            True("Trim payback: fully paid A dropped (500)", big.Count == 500 && !big.Contains(A));
            Eq("Trim payback: debt 0 after", 0, History.FailedDebt(big, null));
            // paid in full only with the kept q0 (30): A kept (PaidBack 5,675), debt stays 0
            A.PaidBack = 0;
            big = TrimList(A, 30, 5.675);
            Eq("Trim payback: debt before 0 (33,70 - 35,675)", 0, History.FailedDebt(big, null));
            History.Trim(big, 500);
            True("Trim payback: A kept while kept quotes still pay it", big.Count == 501 && big.Contains(A));
            Eq("Trim payback: debt after still 0", 0, History.FailedDebt(big, null));

            // JSON keeps the new fields (da-DK); an old file without them reads 0
            var oldCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            List<QuoteRecord> back;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("da-DK");
                A.PaidBack = 2.5;
                back = History.ParseJson(History.ToJson(new List<QuoteRecord> { prec, A }));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = oldCulture; }
            True("Payback JSON round trip", back != null && back.Count == 2);
            Eq("Payback JSON PaybackCost 5,675", 5.675, back[0].PaybackCost);
            Eq("Payback JSON FailedPaybackPct 25", 25, back[0].FailedPaybackPct);
            Eq("Payback JSON FailedPaybackWho 1", 1, back[0].FailedPaybackWho);
            Eq("Payback JSON PaidBack 2,5", 2.5, back[1].PaidBack);
            var old = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"x\",\"Failed\":true}]}");
            True("Payback old file: new fields 0", old != null && old.Count == 1 && old[0].PaybackCost == 0
                 && old[0].PaidBack == 0 && old[0].FailedPaybackWho == 0);
        }

        // 502 records newest first: q0 pays q0Pay at 12:00 (kept), q1..q499 plain, P (index 500) pays pPay at 11:00, A at index 501
        static List<QuoteRecord> TrimList(QuoteRecord a, double q0Pay, double pPay)
        {
            var t = new DateTime(2026, 11, 3, 10, 0, 0);
            var big = new List<QuoteRecord>();
            for (int i = 0; i < 500; i++) { var q = new QuoteRecord(); q.Id = "q" + i; big.Add(q); }
            big[0] = Payer(t.AddHours(2), q0Pay, History.ForCustomer, 2); big[0].Id = "q0";
            var p = Payer(t.AddHours(1), pPay, History.ForCustomer, 2); p.Id = "P";
            big.Add(p);
            big.Add(a);
            return big;
        }

        // slice_info.config pieces (trimmed from the real 4-colour Benchy slices, scratchpad multi-colour_test\fixtures)
        static string Fil(int id, string colour, string metres, string grams)
        {
            return "<filament id=\"" + id + "\" tray_info_idx=\"GFL99\" type=\"PLA\" color=\"" + colour + "\" used_m=\"" + metres +
                "\" used_g=\"" + grams + "\" total_load_time=\"2225.00\" total_unload_time=\"2581.00\"/>";
        }

        static string PlateXml(string prediction, string weight, params string[] filaments)
        {
            return "<plate><metadata key=\"index\" value=\"1\"/><metadata key=\"prediction\" value=\"" + prediction + "\"/>" +
                "<metadata key=\"weight\" value=\"" + weight + "\"/>" + string.Join("", filaments) + "</plate>";
        }

        static string SliceInfo(params string[] plates)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><config>" + string.Join("", plates) + "</config>";
        }

        // result.json (trimmed from result_a_one.json: one plate, 4 filaments)
        const string ResultAOne = "{\"error_string\":\"Success.\",\"return_code\":0,\"sliced_plates\":[{" +
            "\"feature_type_times\":{\"Flush\":31431.537109375},\"filament_change_times\":435,\"filaments\":[" +
            "{\"filament_id\":\"GFA00\",\"id\":1,\"main_used_g\":5.042617321014404,\"total_used_g\":33.04133987426758}," +
            "{\"filament_id\":\"GFL99\",\"id\":2,\"main_used_g\":0.38217446208000183,\"total_used_g\":55.98664855957031}," +
            "{\"filament_id\":\"GFL99\",\"id\":3,\"main_used_g\":5.874551296234131,\"total_used_g\":17.282550811767578}," +
            "{\"filament_id\":\"GFL99\",\"id\":4,\"main_used_g\":0.5582333207130432,\"total_used_g\":29.542543411254883}]," +
            "\"id\":1,\"main_predication\":7115.779296875,\"total_predication\":38566.390625}]}";

        static double Sum(System.Collections.Generic.IEnumerable<double> xs)
        {
            double t = 0;
            foreach (var x in xs) t += x;
            return t;
        }

        static int Main()
        {
            // Wear: printer 2099/3000 = 0.6997 + parts 600/1500 = 0.40 + hotend + plate
            // default: + 139/500 = 0.278 + 200/600 = 0.3333 = 1.711
            Eq("wear default (0.4 SS, PLA, smooth)", 1.71, Wear.DefaultRate);
            // 0.2 stainless: same hotend price and life = 1.711
            Eq("wear 0.2 stainless", 1.71, Wear.Calculate(Wear.FindNozzle("0.2 stainless"), false, "PLA", Wear.FindPlate("High Temp Plate")).Total);
            // + 0.278 + 150/1000 = 0.15 -> 1.5277
            Eq("wear textured plate", 1.53, Wear.Calculate(Wear.FindNozzle("0.4 stainless"), false, "PLA", Wear.FindPlate("Textured PEI Plate")).Total);
            // + 0.278 + 200/300 = 0.6667 -> 2.0443
            Eq("wear PETG on smooth (plate life halved)", 2.04, Wear.Calculate(Wear.FindNozzle("0.4 stainless"), false, "PETG", Wear.FindPlate("High Temp Plate")).Total);
            // + 185/750 = 0.2467 + 0.3333 -> 1.6797
            Eq("wear PLA on hardened", 1.68, Wear.Calculate(Wear.FindNozzle("0.4 hardened"), false, "PLA", Wear.FindPlate("High Temp Plate")).Total);
            // + 185/300 = 0.6167 + 0.3333 -> 2.0497
            Eq("wear CF on hardened", 2.05, Wear.Calculate(Wear.FindNozzle("0.4 hardened"), true, "PLA-CF", Wear.FindPlate("High Temp Plate")).Total);
            var cfSS = Wear.Calculate(Wear.FindNozzle("0.4 stainless"), true, "PLA-CF", Wear.FindPlate("High Temp Plate"));
            True("CF on stainless flagged", cfSS.AbrasiveOnStainless);
            // + 139/100 = 1.39 + 0.3333 -> 2.823
            Eq("wear CF on stainless (100 h hotend)", 2.82, cfSS.Total);
            True("short preset name", BambuSlicer.ShortName("Bambu PLA Basic @BBL A1 0.2 nozzle") == "Bambu PLA Basic");
            True("suffix 0.6", BambuSlicer.Suffix("0.6") == " @BBL A1 0.6 nozzle" && BambuSlicer.Suffix("0.4") == " @BBL A1");
            Eq("AutoUpdateCheck default is 1", 1, new AppSettings().AutoUpdateCheck);
            var orig0 = new AppSettings { AutoUpdateCheck = 0 };
            Eq("Clone keeps AutoUpdateCheck 0", 0, orig0.Clone().AutoUpdateCheck);
            True("Language default is empty", new AppSettings().Language == "");
            True("IsDanish(da) true", AppSettings.IsDanish("da"));
            True("IsDanish(en) false", !AppSettings.IsDanish("en"));
            True("IsDanish(unknown) = IsDanish(auto)", AppSettings.IsDanish("xx") == AppSettings.IsDanish(""));
            True("Clone keeps Language da", new AppSettings { Language = "da" }.Clone().Language == "da");
            Eq("FailedPrintPct default is 0 (off)", 0, new AppSettings().FailedPrintPct);
            Eq("Clone keeps FailedPrintPct", 12, new AppSettings { FailedPrintPct = 12 }.Clone().FailedPrintPct);
            var s = new AppSettings { MachineRatePerHour = 0, FailedPrintPct = 0 }; // 100 W, 25 %, 75 %, no wear, no fails (formula check)
            var r = Pricing.Calculate(Example(), s);
            Eq("material", 21.60, r.MaterialCost);
            Eq("power kWh", 0.55, r.PowerKwh);
            Eq("power cost", 1.10, r.PowerCost);
            Eq("failed prints off", 0, r.FailedPrintCost);
            Eq("base", 22.70, r.BaseCost);
            Eq("friends", 28.375, r.FriendsPrice);
            Eq("others", 39.725, r.OthersPrice);

            // Failed prints 5 %: 22.70 × 0.05 = 1.135; base 23.835; × 1.25 = 29.79375; × 1.75 = 41.71125
            var rf = Pricing.Calculate(Example(), new AppSettings { MachineRatePerHour = 0, FailedPrintPct = 5 });
            Eq("failed prints 5 %", 1.135, rf.FailedPrintCost);
            Eq("base with failed prints", 23.835, rf.BaseCost);
            Eq("friends with failed prints", 29.79375, rf.FriendsPrice);
            Eq("others with failed prints", 41.71125, rf.OthersPrice);
            Eq("negative failed % counts as 0", 22.70, Pricing.Calculate(Example(), new AppSettings { MachineRatePerHour = 0, FailedPrintPct = -5 }).BaseCost);

            var zero = Pricing.Calculate(new QuoteInput(), s);
            Eq("zero job", 0, zero.OthersPrice);

            var s2 = new AppSettings { MachineRatePerHour = 5, MinimumPrice = 50, RoundTo = 5, FailedPrintPct = 0 };
            var r2 = Pricing.Calculate(Example(), s2);
            // 39.725 + 5.5h*5 = 67.225 -> round up to 70
            Eq("others w/ rate + rounding", 70, r2.OthersPrice);
            // 28.375 + 5.5h*5 = 55.875 -> round up to 60
            Eq("friends w/ rate + rounding", 60, r2.FriendsPrice);

            var s4 = new AppSettings();   // defaults: failed prints off, A1 wear 5.5 h × 1.71 = 9.405
            Eq("others with defaults", 49.13, Pricing.Calculate(Example(), s4).OthersPrice);   // 39.725 + 9.405
            Eq("friends with defaults", 37.78, Pricing.Calculate(Example(), s4).FriendsPrice); // 28.375 + 9.405

            // Benchy: 0 h 38 m, 11.8 g, 200 DKK/kg, 3.65 DKK/kWh, defaults.
            // base = 2.36 + 0.6333 × 0.1 × 3.65 = 2.5912; wear = 0.6333 × 1.71 = 1.0830
            var benchy = Pricing.Calculate(new QuoteInput { Hours = 0, Minutes = 38, FilamentGrams = 11.8, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 3.65 }, new AppSettings());
            Eq("benchy friends", 4.32, benchy.FriendsPrice);   // 2.5912 × 1.25 + 1.0830 = 4.3220
            Eq("benchy others", 5.62, benchy.OthersPrice);     // 2.5912 × 1.75 + 1.0830 = 5.6175

            // ---- multi-colour (per-filament material) ----
            // 1 line = today's formula exactly (the line is ignored, top-level grams/price used)
            var one = Example();
            one.Filaments = new List<FilamentLine> { new FilamentLine { Grams = 120, PricePerKg = 180 } };
            var rOne = Pricing.Calculate(one, s);
            var rOld = Pricing.Calculate(Example(), s);
            True("1 line = old result exactly", rOne.MaterialCost == rOld.MaterialCost && rOne.BaseCost == rOld.BaseCost &&
                rOne.FriendsPrice == rOld.FriendsPrice && rOne.OthersPrice == rOld.OthersPrice);
            True("1 line: no FilamentCosts", rOne.FilamentCosts == null && rOld.FilamentCosts == null);
            var empty = Example();
            empty.Filaments = new List<FilamentLine>();
            Eq("empty list = old material", 21.60, Pricing.Calculate(empty, s).MaterialCost);

            // 4-colour Benchy, 10 h 43 m, 200 kr./kg, 3,65 kr./kWh, defaults:
            // grams 33,04 + 55,99 + 17,28 + 29,54 = 135,85; × 0,2 = 27,17
            // hours 10,716667; power 10,716667 × 0,1 × 3,65 = 3,9116; base 31,0816; wear 10,716667 × 1,71 = 18,3255
            // friends 31,0816 × 1,25 + 18,3255 = 57,1775; customers 31,0816 × 1,75 + 18,3255 = 72,7183
            Func<double, List<FilamentLine>> four = p2 => new List<FilamentLine> {
                new FilamentLine { Grams = 33.04, PricePerKg = 200 }, new FilamentLine { Grams = 55.99, PricePerKg = p2 },
                new FilamentLine { Grams = 17.28, PricePerKg = 200 }, new FilamentLine { Grams = 29.54, PricePerKg = 200 } };
            var mc = Pricing.Calculate(new QuoteInput { Hours = 10, Minutes = 43, FilamentGrams = 135.85, FilamentDkkPerKg = 200,
                ElectricityDkkPerKwh = 3.65, Filaments = four(200) }, new AppSettings());
            True("4 colours: 4 line costs", mc.FilamentCosts != null && mc.FilamentCosts.Length == 4);
            Eq("4 colours line 1 (33,04 g × 200)", 6.608, mc.FilamentCosts[0]);
            Eq("4 colours line 2 (55,99 g × 200)", 11.198, mc.FilamentCosts[1]);
            Eq("4 colours line 3 (17,28 g × 200)", 3.456, mc.FilamentCosts[2]);
            Eq("4 colours line 4 (29,54 g × 200)", 5.908, mc.FilamentCosts[3]);
            Eq("4 colours material", 27.17, mc.MaterialCost);
            Eq("4 colours power", 3.91, mc.PowerCost);
            Eq("4 colours base", 31.08, mc.BaseCost);
            Eq("4 colours friends", 57.18, mc.FriendsPrice);
            Eq("4 colours customers", 72.72, mc.OthersPrice);

            // Mixed: line 2 at 300 kr./kg -> 79,86 g × 0,2 = 15,972 + 55,99 × 0,3 = 16,797 -> 32,769
            // (top-level FilamentGrams/FilamentDkkPerKg are not used with 2+ lines)
            var s0 = new AppSettings { MachineRatePerHour = 0, FailedPrintPct = 0 };
            var mx = Pricing.Calculate(new QuoteInput { Hours = 1, FilamentGrams = 999, FilamentDkkPerKg = 999,
                ElectricityDkkPerKwh = 2.00, Filaments = four(300) }, s0);
            Eq("mixed line 2 (55,99 g × 300)", 16.797, mx.FilamentCosts[1]);
            Eq("mixed material", 32.769, mx.MaterialCost);
            // power 1 h × 0,1 × 2 = 0,20; base 32,969; × 1,25 = 41,21125; × 1,75 = 57,69575
            Eq("mixed base", 32.969, mx.BaseCost);
            Eq("mixed friends", 41.21125, mx.FriendsPrice);
            Eq("mixed customers", 57.69575, mx.OthersPrice);
            // failed prints 10 % on the summed material: (32,769 + 0,20) × 0,1 = 3,2969; base 36,2659
            var mf = Pricing.Calculate(new QuoteInput { Hours = 1, ElectricityDkkPerKwh = 2.00, Filaments = four(300) },
                new AppSettings { MachineRatePerHour = 0, FailedPrintPct = 10 });
            Eq("mixed failed prints 10 %", 3.2969, mf.FailedPrintCost);
            Eq("mixed base with failed prints", 36.2659, mf.BaseCost);
            bool negThrew = false;
            var neg = four(200); neg[2].Grams = -1;
            try { Pricing.Calculate(new QuoteInput { Filaments = neg }, s0); } catch (ArgumentException) { negThrew = true; }
            True("rejects negative line grams", negThrew);

            var s3 = new AppSettings { MinimumPrice = 50, MachineRatePerHour = 0, FailedPrintPct = 0 };
            Eq("minimum price", 50, Pricing.Calculate(Example(), s3).OthersPrice);
            Eq("minimum not for friends", 28.375, Pricing.Calculate(Example(), s3).FriendsPrice);

            Eq("round exact stays", 25, Pricing.RoundUp(25, 5));

            bool threw = false;
            try { Pricing.Calculate(new QuoteInput { Minutes = 60 }, s); } catch (ArgumentException) { threw = true; }
            True("rejects 60 minutes", threw);
            threw = false;
            try { Pricing.Calculate(new QuoteInput { FilamentGrams = -1 }, s); } catch (ArgumentException) { threw = true; }
            True("rejects negative grams", threw);

            double v;
            True("parse 12,5", Num.TryParse("12,5", out v) && v == 12.5);
            True("parse 12.5", Num.TryParse("12.5", out v) && v == 12.5);
            True("parse 1.234,5", Num.TryParse("1.234,5", out v) && v == 1234.5);
            True("parse 1,234.5", Num.TryParse("1,234.5", out v) && v == 1234.5);
            True("reject abc", !Num.TryParse("abc", out v));
            True("reject -3", !Num.TryParse("-3", out v));

            // Printables link parsing
            True("printables id", Printables.ParseId("https://www.printables.com/model/3161-3d-benchy") == "3161");
            True("printables id (lang)", Printables.ParseId("printables.com/de/model/1050000-strap") == "1050000");
            True("printables id (other site)", Printables.ParseId("https://makerworld.com/en/models/123") == null);

            // Bambu slice_info parsing (2 plates; second plate has no 'weight' → sum used_g)
            string xml = "<?xml version=\"1.0\"?><config>" +
                "<plate><metadata key=\"prediction\" value=\"2270\"/><metadata key=\"weight\" value=\"11.76\"/></plate>" +
                "<plate><metadata key=\"prediction\" value=\"600\"/><metadata key=\"weight\" value=\"\"/>" +
                "<filament id=\"1\" used_g=\"2.5\"/><filament id=\"2\" used_g=\"1.0\"/></plate>" +
                "<plate><metadata key=\"prediction\" value=\"0\"/></plate></config>";
            var si = BambuSlicer.ParseSliceInfo(xml);
            Eq("slice seconds", 2870, si.Seconds);
            Eq("slice grams", 15.26, si.Grams);
            Eq("slice plates", 2, si.Plates);
            True("unsliced → null", BambuSlicer.ParseSliceInfo("<config><plate><metadata key=\"index\" value=\"1\"/></plate></config>") == null);
            True("model ext", BambuSlicer.IsModelFile("a.STL") && BambuSlicer.IsModelFile("b.3mf") && !BambuSlicer.IsModelFile("c.f3d"));

            // ---- multi-colour slice parsing (4-colour Benchy, fixtures trimmed) ----
            string[] cols = { "#118002", "#FFFF0A", "#074080", "#ED1C24" };
            var a1 = BambuSlicer.ParseSliceInfo(SliceInfo(PlateXml("38566", "135.85",
                Fil(1, cols[0], "10.90", "33.04"), Fil(2, cols[1], "18.77", "55.99"),
                Fil(3, cols[2], "5.79", "17.28"), Fil(4, cols[3], "9.91", "29.54"))));
            Eq("a_one seconds", 38566, a1.Seconds);
            Eq("a_one grams", 135.85, a1.Grams);
            True("a_one 4 filaments", a1.Filaments != null && a1.Filaments.Count == 4);
            double[] aG = { 33.04, 55.99, 17.28, 29.54 }, aM = { 10.90, 18.77, 5.79, 9.91 };
            bool slotsOk = a1.Filaments.Count == 4;
            for (int i = 0; slotsOk && i < 4; i++)
            {
                var f = a1.Filaments[i];
                slotsOk = f.Slot == i + 1 && f.Colour == cols[i] && f.Type == "PLA" && Math.Abs(f.Grams - aG[i]) < 0.005 &&
                          Math.Abs(f.Metres - aM[i]) < 0.005 && f.ModelGrams == -1;
            }
            True("a_one slots 1-4: colour, PLA, grams, metres, ModelGrams -1", slotsOk);
            True("a_one purge/change unknown", a1.PurgeGrams == -1 && a1.ColourChangeHours == -1);

            var b4 = BambuSlicer.ParseSliceInfo(SliceInfo(PlateXml("39807", "137.51",
                Fil(1, cols[0], "10.90", "33.04"), Fil(2, cols[1], "18.77", "56.89"),
                Fil(3, cols[2], "5.79", "17.56"), Fil(4, cols[3], "9.91", "30.02"))));
            Eq("b_four seconds", 39807, b4.Seconds);
            Eq("b_four grams", 137.51, b4.Grams);
            True("b_four grams per slot", b4.Filaments.Count == 4 && Math.Abs(b4.Filaments[0].Grams - 33.04) < 0.005 &&
                Math.Abs(b4.Filaments[1].Grams - 56.89) < 0.005 && Math.Abs(b4.Filaments[2].Grams - 17.56) < 0.005 &&
                Math.Abs(b4.Filaments[3].Grams - 30.02) < 0.005);

            // 2 plates, same slot summed (2,5 + 1,5 = 4,0 g; 0,8 + 0,5 = 1,3 m); used_g="0" left out; weight "" -> sum used_g
            var two = BambuSlicer.ParseSliceInfo(SliceInfo(
                PlateXml("600", "", Fil(1, "#00ae42", "0.8", "2.5"), Fil(2, "#FFFFFF", "0", "0")),
                PlateXml("300", "", Fil(1, "#00ae42", "0.5", "1.5"))));
            Eq("2 plates seconds", 900, two.Seconds);
            Eq("2 plates grams", 4.0, two.Grams);
            True("2 plates: one slot, zero left out", two.Filaments.Count == 1 && two.Filaments[0].Slot == 1);
            Eq("2 plates slot 1 grams summed", 4.0, two.Filaments[0].Grams);
            Eq("2 plates slot 1 metres summed", 1.3, two.Filaments[0].Metres);
            True("colour normalised to upper case", two.Filaments[0].Colour == "#00AE42");
            True("old single-colour XML has Filaments list", si.Filaments != null && si.Filaments.Count == 2);

            // result.json: model grams 5,04 + 0,38 + 5,87 + 0,56 = 11,858; total 135,853
            var st = BambuSlicer.ParseSliceResult(ResultAOne);
            True("result parsed", st != null);
            Eq("result main g slot 1", 5.04, st.MainGrams[1]);
            Eq("result main g slot 2", 0.38, st.MainGrams[2]);
            Eq("result main g slot 3", 5.87, st.MainGrams[3]);
            Eq("result main g slot 4", 0.56, st.MainGrams[4]);
            Eq("result main g sum", 11.858, Sum(st.MainGrams.Values));
            Eq("result total g sum", 135.853, Sum(st.TotalGrams.Values));
            Eq("result main seconds", 7115.78, st.MainSeconds);
            Eq("result total seconds", 38566.39, st.TotalSeconds);
            True("result colour changes 435", st.ColourChanges == 435);
            True("result null/{}/x -> null", BambuSlicer.ParseSliceResult(null) == null &&
                BambuSlicer.ParseSliceResult("{}") == null && BambuSlicer.ParseSliceResult("x") == null);

            // Apply: purge = 135,853 - 11,858 = 123,996 g; change time (38566,39 - 7115,78) / 3600 = 8,7363 h
            BambuSlicer.ApplySliceStats(a1, st);
            Eq("apply purge grams", 123.996, a1.PurgeGrams);
            Eq("apply colour change hours", 8.7363, a1.ColourChangeHours);
            Eq("apply slot 1 model grams", 5.04, a1.Filaments[0].ModelGrams);
            Eq("apply keeps grams", 135.85, a1.Grams);
            Eq("apply keeps seconds", 38566, a1.Seconds);
            BambuSlicer.ApplySliceStats(b4, null);
            True("apply null stats leaves -1", b4.PurgeGrams == -1 && b4.ColourChangeHours == -1 && b4.Filaments[0].ModelGrams == -1);

            // project_settings.config filament_settings_id
            True("slots 4", BambuSlicer.ParseFilamentSlots("{\"filament_settings_id\":[\"Bambu PLA Basic @BBL A1\",\"PLA\",\"PLA\",\"PLA\"]}").Count == 4);
            True("slots first name", BambuSlicer.ParseFilamentSlots("{\"filament_settings_id\":[\"Bambu PLA Basic @BBL A1\",\"PLA\"]}")[0] == "Bambu PLA Basic @BBL A1");
            True("slots plain string -> 1", BambuSlicer.ParseFilamentSlots("{\"filament_settings_id\":\"Bambu PLA Basic @BBL A1\"}").Count == 1);
            True("slots null/empty/bad -> empty", BambuSlicer.ParseFilamentSlots(null).Count == 0 &&
                BambuSlicer.ParseFilamentSlots("").Count == 0 && BambuSlicer.ParseFilamentSlots("{bad").Count == 0);

            // Live electricity price
            Eq("power total incl. VAT", 2.7625, PowerPrice.TotalPrice(1.66, 0.55, true));
            Eq("power total excl. VAT", 2.21, PowerPrice.TotalPrice(1.66, 0.55, false));
            string pj = "{\"records\":[" +
                "{\"TimeUTC\":\"2026-11-03T14:00:00\",\"DayAheadPriceDKK\":1000.0}," +
                "{\"TimeUTC\":\"2026-11-03T14:15:00\",\"DayAheadPriceDKK\":1200.5}," +
                "{\"TimeUTC\":\"2026-11-03T14:30:00\",\"DayAheadPriceDKK\":1500.0}]}";
            var now = new DateTime(2026, 11, 3, 14, 20, 0, DateTimeKind.Utc);
            var pq = PowerPrice.PickCurrent(pj, "DK2", now);
            Eq("current slot price", 1.2005, pq.SpotDkkPerKwh);
            True("current slot time", pq.SlotStartUtc == new DateTime(2026, 11, 3, 14, 15, 0));
            threw = false;
            try { PowerPrice.PickCurrent(pj, "DK2", now.AddHours(5)); } catch (SliceException) { threw = true; }
            True("stale data rejected", threw);

            // ---- updater ----
            True("tag v1.0.1", Updater.ParseTag("v1.0.1") == new Version(1, 0, 1, 0));
            True("tag 2.3", Updater.ParseTag("2.3") == new Version(2, 3, 0, 0));
            True("tag junk", Updater.ParseTag("latest") == null && Updater.ParseTag("v1.0.1-beta") == null);
            True("1.0.1 newer than 1.0.0", Updater.ParseTag("v1.0.1") > Updater.ParseTag("v1.0.0"));
            True("1.0.0 = 1.0.0.0", Updater.ParseTag("v1.0.0") == new Version(1, 0, 0, 0));
            True("short version", Updater.Short(new Version(1, 2, 3, 0)) == "1.2.3");
            string rel = "{\"tag_name\":\"v1.2.0\",\"name\":\"v1.2.0\",\"draft\":false,\"prerelease\":false," +
                "\"html_url\":\"https://github.com/gettic/PrintPrice/releases/tag/v1.2.0\",\"body\":\"## New\\n- **Faster** slicing\\n- See [README](https://x)\"," +
                "\"assets\":[{\"name\":\"other.zip\",\"size\":5,\"browser_download_url\":\"https://github.com/gettic/PrintPrice/releases/download/v1.2.0/other.zip\"}," +
                "{\"name\":\"PrintPrice.exe\",\"size\":201728,\"digest\":\"sha256:9E4D00BF7355CD8A364686FAEBD887511851169E5075428FDA7AB07787E4643B\"," +
                "\"browser_download_url\":\"https://github.com/gettic/PrintPrice/releases/download/v1.2.0/PrintPrice.exe\"}]}";
            var ui = Updater.ParseRelease(rel);
            True("release parsed", ui != null && ui.Version == new Version(1, 2, 0, 0) && ui.Size == 201728);
            True("release exe asset", ui != null && ui.DownloadUrl.EndsWith("/v1.2.0/PrintPrice.exe"));
            True("release digest", ui != null && ui.Sha256 == "9e4d00bf7355cd8a364686faebd887511851169e5075428fda7ab07787e4643b");
            True("release notes text", ui != null && Updater.NotesText(ui, 500) == "New\n• Faster slicing\n• See README");
            True("prerelease ignored", Updater.ParseRelease(rel.Replace("\"prerelease\":false", "\"prerelease\":true")) == null);
            True("no exe asset", Updater.ParseRelease(rel.Replace("PrintPrice.exe", "Other.exe")) == null);
            True("bad tag ignored", Updater.ParseRelease(rel.Replace("\"v1.2.0\",\"name\"", "\"nightly\",\"name\"")) == null);
            // releases list -> target + combined notes
            Func<string, string, bool, bool, bool, string> mk = (tag, body, draft, pre, exe) =>
                "{\"tag_name\":\"" + tag + "\",\"draft\":" + (draft ? "true" : "false") + ",\"prerelease\":" + (pre ? "true" : "false") +
                ",\"body\":\"" + body + "\",\"assets\":[{\"name\":\"" + (exe ? "PrintPrice.exe" : "other.zip") + "\",\"size\":100," +
                "\"digest\":\"sha256:9E4D00BF7355CD8A364686FAEBD887511851169E5075428FDA7AB07787E4643B\"," +
                "\"browser_download_url\":\"https://github.com/gettic/PrintPrice/releases/download/" + tag + "/" + (exe ? "PrintPrice.exe" : "other.zip") + "\"}]}";
            string rBeta = mk("v1.1.0-beta", "BETA-NOTE", false, true, true);
            string rDraft = mk("v1.0.4", "DRAFT-NOTE", true, false, true);
            string rNightly = mk("nightly", "NIGHTLY-NOTE", false, false, true);
            string rv103 = mk("v1.0.3", "## Fixed\\n- **Faster** start", false, false, true);
            string rv102 = mk("v1.0.2", "", false, false, true);
            string rv101 = mk("v1.0.1", "V101-NOTE", false, false, true);
            string rv100 = mk("v1.0.0", "V100-NOTE", false, false, true);
            string list = "[" + string.Join(",", new string[] { rBeta, rDraft, rv103, rNightly, rv102, rv101, rv100 }) + "]";
            var cur = new Version(1, 0, 1, 0);
            var tu = Updater.ParseReleases(list, cur);
            True("list target 1.0.3", tu != null && tu.Version == new Version(1, 0, 3, 0) && tu.DownloadUrl.EndsWith("/v1.0.3/PrintPrice.exe"));
            True("list notes", tu != null && Updater.NotesText(tu, 0) == "## v1.0.3\nFixed\n• Faster start\n\n## v1.0.2\nNo notes");
            True("list notes exclude others", tu != null && !tu.Notes.Contains("BETA") && !tu.Notes.Contains("DRAFT") &&
                !tu.Notes.Contains("NIGHTLY") && !tu.Notes.Contains("V101") && !tu.Notes.Contains("V100"));
            True("list up to date", Updater.ParseReleases(list, new Version(1, 0, 3, 0)) == null);
            True("list empty", Updater.ParseReleases("[]", cur) == null);
            var tu2 = Updater.ParseReleases("[" + string.Join(",", new string[] { rBeta, rDraft, mk("v1.0.3", "X", false, false, false), rNightly, rv102, rv101, rv100 }) + "]", cur);
            True("list no-asset skipped", tu2 != null && tu2.Version == new Version(1, 0, 2, 0) && Updater.NotesText(tu2, 0) == "## v1.0.2\nNo notes");
            var tu3 = Updater.ParseReleases("[" + string.Join(",", new string[] { rv100, rv102, rNightly, rv101, rv103, rDraft, rBeta }) + "]", cur);
            True("list scrambled order", tu3 != null && Updater.NotesText(tu3, 0) == "## v1.0.3\nFixed\n• Faster start\n\n## v1.0.2\nNo notes");
            True("notes max 0 no cut", tu != null && Updater.NotesText(tu, 0).Length > 20);
            True("notes max cuts", tu != null && Updater.NotesText(tu, 10).EndsWith("…") && Updater.NotesText(tu, 10) == "## v1.0.3…");
            // Model picture helpers
            string benchyDir = "media/prints/3161/images/20206_70fde6a0-6da1-4522-ba46-25f1bece7199/";
            string benchyThumb = "https://media.printables.com/" + benchyDir + "thumbs/inside/320x240/jpg/benchy.jpg";
            True("ThumbUrl benchy jpg", Printables.ThumbUrl(benchyDir + "benchy.jpg") == benchyThumb);
            string thumbBase = "https://media.printables.com/" + benchyDir + "thumbs/inside/320x240/";
            True("ThumbUrl png folder, file .jpg", Printables.ThumbUrl(benchyDir + "benchy.png") == thumbBase + "png/benchy.jpg");
            True("ThumbUrl webp folder, file .jpg", Printables.ThumbUrl(benchyDir + "benchy.webp") == thumbBase + "webp/benchy.jpg");
            True("ThumbUrl jpeg folder, file .jpg", Printables.ThumbUrl(benchyDir + "x.jpeg") == thumbBase + "jpeg/x.jpg");
            True("ThumbUrl no extension -> jpg folder", Printables.ThumbUrl(benchyDir + "benchy") == thumbBase + "jpg/benchy.jpg");
            True("ThumbUrl odd extension -> jpg folder", Printables.ThumbUrl(benchyDir + "x.p-g") == thumbBase + "jpg/x.jpg");
            // BambuSlicer default filament per colour (idea 17)
            True("Unstated: empty", BambuSlicer.IsUnstatedFilament("") && BambuSlicer.IsUnstatedFilament(null));
            foreach (var u in new[] { "PLA", "Generic PLA", "Generic PLA @BBL A1", "Creality Generic PLA", "PLA - Copy" })
                True("Unstated: " + u, BambuSlicer.IsUnstatedFilament(u));
            foreach (var stSt in new[] { "Bambu PLA Matte @BBL A1", "Bambu PLA Basic @BBL X1C", "Generic PETG", "PETG", "PLA+", "Generic PLA Silk", "Prusament PLA" })
                True("Stated: " + stSt, !BambuSlicer.IsUnstatedFilament(stSt));
            True("BaseName strips printer", BambuSlicer.FilamentBaseName("Bambu PLA Basic @BBL X1C") == "Bambu PLA Basic");
            True("BaseName strips (Copy)", BambuSlicer.FilamentBaseName("PLA (Copy)") == "PLA");
            Func<string, string> toA1 = n => n + " @BBL A1";
            True("SlotPlan 4x PLA -> 4x Matte A1",
                J(BambuSlicer.SlotPlan(new List<string> { "PLA", "PLA", "PLA", "PLA" }, "Bambu PLA Matte", toA1))
                == "Bambu PLA Matte @BBL A1|Bambu PLA Matte @BBL A1|Bambu PLA Matte @BBL A1|Bambu PLA Matte @BBL A1");
            Func<string, string> known = n => (n == "Bambu PLA Silk+" || n == "Bambu PLA Matte") ? n + " @BBL A1" : null;
            True("SlotPlan mixed: named kept, unknown null, unstated default",
                J(BambuSlicer.SlotPlan(new List<string> { "Bambu PLA Silk+ @BBL X1C", "PLA", "Prusament PLA", "" }, "Bambu PLA Matte", known))
                == "Bambu PLA Silk+ @BBL A1|Bambu PLA Matte @BBL A1|~|Bambu PLA Matte @BBL A1");
            True("SlotPlan null -> empty", BambuSlicer.SlotPlan(null, "Bambu PLA Matte", toA1).Count == 0);
            True("SlotPlan default Basic for unstated",
                J(BambuSlicer.SlotPlan(new List<string> { "PLA", "Generic PLA" }, "Bambu PLA Basic", toA1))
                == "Bambu PLA Basic @BBL A1|Bambu PLA Basic @BBL A1");
            var fu = new List<FilamentUse> {
                new FilamentUse { Slot = 1, Profile = "PLA", Type = "PLA" },
                new FilamentUse { Slot = 2, Profile = "Bambu PETG HF @BBL A1", Type = "PETG" },
                new FilamentUse { Slot = 3, Profile = "", Type = "" } };
            BambuSlicer.ApplyDefaultFilament(fu, "Bambu PLA Matte");
            True("ApplyDefault: unstated PLA and empty get default, named PETG kept",
                fu[0].Profile == "Bambu PLA Matte" && fu[1].Profile == "Bambu PETG HF @BBL A1" && fu[2].Profile == "Bambu PLA Matte");
            var fe = new List<FilamentUse> {
                new FilamentUse { Slot = 1, Profile = "PLA", Type = "PLA" },
                new FilamentUse { Slot = 2, Profile = "", Type = "PETG" } };
            BambuSlicer.ApplyDefaultFilament(fe, "Bambu PLA Matte");
            True("ApplyDefault: empty profile with PETG type kept", fe[1].Profile == "" && fe[0].Profile == "Bambu PLA Matte");
            var f1 = new List<FilamentUse> { new FilamentUse { Slot = 1, Profile = "PLA", Type = "PLA" } };
            BambuSlicer.ApplyDefaultFilament(f1, "Bambu PLA Matte");
            True("ApplyDefault: single filament unchanged", f1[0].Profile == "PLA");
            var fn = new List<FilamentUse> {
                new FilamentUse { Slot = 1, Profile = "PLA", Type = "PLA" }, new FilamentUse { Slot = 2, Profile = "", Type = "" } };
            BambuSlicer.ApplyDefaultFilament(fn, null);
            True("ApplyDefault: null default changes nothing", fn[0].Profile == "PLA" && fn[1].Profile == "");
            var colArr = BambuSlicer.ParseSlotStrings("{\"filament_colour\":[\"#FF0000\",\"#00FF00\"]}", "filament_colour");
            True("ParseSlotStrings array", J(colArr) == "#FF0000|#00FF00");
            True("ParseSlotStrings missing key -> empty", BambuSlicer.ParseSlotStrings("{\"a\":[\"x\"]}", "filament_colour").Count == 0);
            True("ThumbUrl leading slash", Printables.ThumbUrl("/" + benchyDir + "benchy.jpg") == benchyThumb);
            True("ThumbUrl null", Printables.ThumbUrl(null) == null);
            True("ThumbUrl empty", Printables.ThumbUrl("") == null);
            True("ThumbUrl ../x", Printables.ThumbUrl("../x") == null);
            True("ThumbUrl full http url", Printables.ThumbUrl("http://evil/x.jpg") == null);
            True("ThumbUrl not under media/", Printables.ThumbUrl("other/prints/1/x.jpg") == null);
            True("ImagePath from image.filePath", Printables.ImagePathFromReply("{\"data\":{\"print\":{\"image\":{\"filePath\":\"media/a/x.jpg\"},\"images\":[{\"filePath\":\"media/b/y.jpg\"}]}}}") == "media/a/x.jpg");
            True("ImagePath image null -> first of images", Printables.ImagePathFromReply("{\"data\":{\"print\":{\"image\":null,\"images\":[{\"filePath\":\"media/b/y.jpg\"},{\"filePath\":\"media/c/z.jpg\"}]}}}") == "media/b/y.jpg");
            True("ImagePath print null", Printables.ImagePathFromReply("{\"data\":{\"print\":null}}") == null);
            True("ImagePath bad JSON", Printables.ImagePathFromReply("not json {") == null);
            True("PickPicture prefers plate_1", BambuSlicer.PickPictureEntry(new[] { "Metadata/pick_1.png", "Metadata/top_1.png", "Metadata/plate_no_light_1.png", "Metadata/plate_1_small.png", "Metadata/plate_1.png" }) == "Metadata/plate_1.png");
            True("PickPicture thumbnail", BambuSlicer.PickPictureEntry(new[] { "Metadata/thumbnail.png", "Metadata/top_1.png" }) == "Metadata/thumbnail.png");
            True("PickPicture plate_2 over small", BambuSlicer.PickPictureEntry(new[] { "Metadata/plate_2_small.png", "Metadata/plate_2.png" }) == "Metadata/plate_2.png");
            True("PickPicture only pick -> null", BambuSlicer.PickPictureEntry(new[] { "Metadata/pick_1.png" }) == null);
            True("PickPicture ignores subfolders and 3D", BambuSlicer.PickPictureEntry(new[] { "Metadata/sub/x.png", "3D/x.png" }) == null);
            True("IsImage PNG", Net.IsImage(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
            True("IsImage JPEG", Net.IsImage(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46 }));
            True("IsImage null", !Net.IsImage(null));
            True("IsImage short", !Net.IsImage(new byte[] { 0x89, 0x50, 0x4E }));
            True("IsImage GIF", !Net.IsImage(System.Text.Encoding.ASCII.GetBytes("GIF89a..")));

            // ---------- test mode (--settings "<folder>"): never reads or writes the real %APPDATA% file ----------
            string realFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintPrice", "settings.ini");
            string testDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-settings");
            string testFile = System.IO.Path.Combine(testDir, "settings.ini");
            try
            {
                // 1. no override
                AppSettings.ClearTestFolder();
                True("TestMode off by default", !AppSettings.TestMode);
                True("FilePath = %APPDATA%\\PrintPrice\\settings.ini", string.Equals(AppSettings.FilePath, realFile, StringComparison.OrdinalIgnoreCase));
                // 2. test folder
                True("UseFolder test dir", AppSettings.UseFolder(testDir));
                True("TestMode on", AppSettings.TestMode);
                True("FilePath in test dir", string.Equals(AppSettings.FilePath, testFile, StringComparison.OrdinalIgnoreCase));
                // 3. refused folders leave FilePath unchanged
                string realDir = AppSettings.RealFolder;
                True("UseFolder refuses empty", !AppSettings.UseFolder(""));
                True("UseFolder refuses null", !AppSettings.UseFolder(null));
                True("UseFolder refuses relative", !AppSettings.UseFolder(@"relative\x"));
                True("UseFolder refuses \\x (no drive)", !AppSettings.UseFolder(@"\x"));
                True("UseFolder refuses real folder", !AppSettings.UseFolder(realDir));
                True("UseFolder refuses real folder + \\", !AppSettings.UseFolder(realDir + "\\"));
                True("UseFolder refuses real folder upper-case", !AppSettings.UseFolder(realDir.ToUpperInvariant()));
                True("UseFolder refuses real folder via ..", !AppSettings.UseFolder(System.IO.Path.Combine(realDir, "sub", "..")));
                True("FilePath unchanged after refusals", string.Equals(AppSettings.FilePath, testFile, StringComparison.OrdinalIgnoreCase));
                // 4. missing folder -> defaults, folder not created by Load
                string neverDir = System.IO.Path.Combine(testDir, "never-made");
                True("UseFolder never-made", AppSettings.UseFolder(neverDir));
                var fresh = AppSettings.Load();
                var defaults = new AppSettings();
                Eq("missing folder: FriendsMarkupPct default", defaults.FriendsMarkupPct, fresh.FriendsMarkupPct);
                Eq("missing folder: LastFilamentPrice default", defaults.LastFilamentPrice, fresh.LastFilamentPrice);
                True("missing folder: Language default", fresh.Language == defaults.Language);
                True("Load doesn't create the folder", !System.IO.Directory.Exists(neverDir));
                // 5. save/load round trip in the test folder
                True("UseFolder test dir again", AppSettings.UseFolder(testDir));
                var s5 = new AppSettings();
                s5.FriendsMarkupPct = 37;
                s5.Language = "da";
                s5.LastFilamentPrice = 189.5;
                s5.FailedPayback = 1; s5.FailedPaybackPct = 40; s5.FailedPaybackWho = 1;
                s5.Save();
                True("Save wrote the test file", System.IO.File.Exists(testFile));
                var r5 = AppSettings.Load();
                Eq("round trip FriendsMarkupPct", 37, r5.FriendsMarkupPct);
                Eq("round trip FailedPayback", 1, r5.FailedPayback);
                Eq("round trip FailedPaybackPct", 40, r5.FailedPaybackPct);
                Eq("round trip FailedPaybackWho", 1, r5.FailedPaybackWho);
                Eq("round trip LastFilamentPrice", 189.5, r5.LastFilamentPrice);
                True("round trip Language", r5.Language == "da");
                // 5b. remembered price per filament: round trip under da-DK, names with spaces, +, -, =, |
                var s6 = new AppSettings();
                s6.LastFilamentPrice = 189.5;
                Eq("PriceFor nothing remembered -> LastFilamentPrice 189,5", 189.5, s6.PriceFor("Bambu PLA Matte"));
                s6.RememberPrice("Bambu PLA Matte", 200);
                s6.RememberPrice("Elegoo PLA+ Silk - Rainbow = 2|x", 249.5);
                s6.RememberPrice(" Bambu PETG HF ", 150);
                s6.RememberPrice("Bambu PETG HF", 165.25);        // replaces (names trimmed)
                s6.RememberPrice("", 99);                          // ignored
                s6.RememberPrice("Bad", -1);                       // ignored
                s6.RememberPrice("NaN", double.NaN);               // ignored
                Eq("RememberPrice: 3 names", 3, s6.FilamentPrices.Count);
                Eq("RememberPrice doesn't touch LastFilamentPrice", 189.5, s6.LastFilamentPrice);
                var c6 = s6.Clone();
                c6.RememberPrice("Bambu PLA Matte", 300);
                Eq("Clone has its own price list", 200, s6.PriceFor("Bambu PLA Matte"));
                Eq("Clone keeps the prices", 300, c6.PriceFor("Bambu PLA Matte"));
                var cult6 = System.Threading.Thread.CurrentThread.CurrentCulture;
                AppSettings r6;
                string ini6;
                try
                {
                    System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("da-DK");
                    s6.Save();
                    ini6 = System.IO.File.ReadAllText(testFile);
                    System.IO.File.AppendAllText(testFile, "FilamentPrice=abc|Broken\r\nFilamentPrice=12\r\nFilamentPrice=-5|Neg\r\n");
                    r6 = AppSettings.Load();
                }
                finally { System.Threading.Thread.CurrentThread.CurrentCulture = cult6; }
                True("ini: decimal point, number first", ini6.Contains("FilamentPrice=249.5|Elegoo PLA+ Silk - Rainbow = 2|x")
                     && ini6.Contains("FilamentPrice=165.25|Bambu PETG HF") && !ini6.Contains("249,5"));
                Eq("round trip PriceFor Bambu PLA Matte 200", 200, r6.PriceFor("Bambu PLA Matte"));
                Eq("round trip PriceFor odd name 249,5", 249.5, r6.PriceFor("Elegoo PLA+ Silk - Rainbow = 2|x"));
                Eq("round trip PriceFor Bambu PETG HF 165,25", 165.25, r6.PriceFor("Bambu PETG HF"));
                Eq("round trip: bad lines skipped (3 names)", 3, r6.FilamentPrices.Count);
                Eq("unknown name -> LastFilamentPrice 189,5", 189.5, r6.PriceFor("Unknown filament"));
                Eq("empty / null name -> LastFilamentPrice", 189.5, r6.PriceFor(null));
                Eq("other settings still load (LastFilamentPrice)", 189.5, r6.LastFilamentPrice);
                // 6. command line
                var o = StartOptions.Parse(new[] { "--settings", @"C:\t", "--hidden" });
                True("Parse --settings --hidden", o.Error == null && o.SettingsFolder == @"C:\t" && o.Hidden && !o.Minimized);
                o = StartOptions.Parse(new[] { "--settings", @"C:\t", "--minimized" });
                True("Parse --settings --minimized", o.Error == null && o.SettingsFolder == @"C:\t" && o.Minimized && !o.Hidden);
                True("Parse --settings without value", StartOptions.Parse(new[] { "--settings" }).Error != null);
                True("Parse --settings followed by option", StartOptions.Parse(new[] { "--settings", "--hidden" }).Error != null);
                True("Parse --hidden alone", StartOptions.Parse(new[] { "--hidden" }).Error != null);
                True("Parse --minimized alone", StartOptions.Parse(new[] { "--minimized" }).Error != null);
                True("Parse --hidden and --minimized", StartOptions.Parse(new[] { "--settings", @"C:\t", "--hidden", "--minimized" }).Error != null);
                True("Parse --settings twice", StartOptions.Parse(new[] { "--settings", @"C:\a", "--settings", @"C:\b" }).Error != null);
                True("Parse unknown --option (typo)", StartOptions.Parse(new[] { "--setting", @"C:\t" }).Error != null);
                o = StartOptions.Parse(new string[0]);
                True("Parse no args", o.Error == null && o.SettingsFolder == null && !o.Hidden && !o.Minimized);
                o = StartOptions.Parse(new[] { @"C:\model.3mf" });
                True("Parse plain arg ignored", o.Error == null && o.SettingsFolder == null);
                True("Parse null", StartOptions.Parse(null).Error == null);
            }
            finally
            {
                // 7. back to normal
                AppSettings.ClearTestFolder();
            }
            True("ClearTestFolder -> real path", !AppSettings.TestMode && string.Equals(AppSettings.FilePath, realFile, StringComparison.OrdinalIgnoreCase));

            HistoryTests();
            PaybackTests();

            Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
