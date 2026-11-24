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
            s.FailedPrintPct = 10; s.MinimumPrice = 0; s.RoundTo = 0; s.FailedPayback = 0;
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
            // Json helper (website swaps in its own): same text both ways, exact format, bad text throws
            True("Json: History.ToJson(ParseJson(x)) == x (re-save is byte-identical)", back != null && History.ToJson(back) == json);
            var hfj = Json.Deserialize<HistoryFile>(json);
            True("Json round trip HistoryFile: same text", hfj != null && hfj.Version == 1 && hfj.Quotes.Count == 2 && Json.Serialize(hfj) == json);
            string lineJson = Json.Serialize(new HistoryFilament { Grams = 12.5, PricePerKg = 200, Colour = "#FF0000", Type = "PLA", Name = "Rød \"PLA\"" });
            True("Json exact format: field order, 12.5, escaped quote", lineJson ==
                 "{\"Grams\":12.5,\"PricePerKg\":200,\"Colour\":\"#FF0000\",\"Type\":\"PLA\",\"Name\":\"Rød \\\"PLA\\\"\"}");
            var lineBack = Json.Deserialize<HistoryFilament>(lineJson);
            True("Json round trip HistoryFilament", lineBack.Grams == 12.5 && lineBack.PricePerKg == 200 && lineBack.Name == "Rød \"PLA\"");
            // issue #8 step 3: TowerGrams written only when known (1,1 yes, -1 no); missing reads as -1
            string towerJson = Json.Serialize(new HistoryFilament { Grams = 12.5, PricePerKg = 200, Colour = "", Type = "PLA", Name = "x", TowerGrams = 1.1 });
            True("Json HistoryFilament: TowerGrams 1,1 written last, -1 left out, missing -> -1", towerJson ==
                 "{\"Grams\":12.5,\"PricePerKg\":200,\"Colour\":\"\",\"Type\":\"PLA\",\"Name\":\"x\",\"TowerGrams\":1.1}"
                 && Json.Deserialize<HistoryFilament>(towerJson).TowerGrams == 1.1 && !lineJson.Contains("TowerGrams") && lineBack.TowerGrams == -1);
            bool threw = false;
            try { Json.Deserialize<HistoryFile>("garbage{"); } catch { threw = true; }
            True("Json.Deserialize throws on garbage", threw);

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

            // invoiced record is never reused: same job within 10 min after an invoice adds a new record without a number
            var invList = new List<QuoteRecord>();
            History.Add(invList, HistRecord(t0));
            invList[0].InvoiceNo = "2026-001";
            string invId = invList[0].Id;
            History.Add(invList, HistRecord(t0.AddMinutes(3)));
            True("History Add same job after invoice -> new record", invList.Count == 2);
            True("History new record has no invoice number and a new Id", (invList[0].InvoiceNo ?? "") == "" && invList[0].Id != invId);
            True("History invoiced record untouched", invList[1].InvoiceNo == "2026-001" && invList[1].Id == invId);
            True("History SameQuote invoiced -> no", !History.SameQuote(invList[1], HistRecord(t0.AddMinutes(1))));
            History.Add(invList, HistRecord(t0.AddMinutes(5)));
            True("History same job without invoice still reused", invList.Count == 2);

            // idea 21: customer name
            True("Customer default empty", new QuoteRecord().Customer == "");
            var cl = new List<QuoteRecord>();
            var c1 = HistRecord(t0); c1.Customer = "Anne";
            History.Add(cl, c1);
            var c2 = HistRecord(t0.AddMinutes(5)); c2.Customer = "Bo";
            History.Add(cl, c2);
            True("History replace keeps the newer Customer", cl.Count == 1 && cl[0].Customer == "Bo");
            var c3 = HistRecord(t0.AddMinutes(6)); c3.Customer = "";
            History.Add(cl, c3);
            True("History replace with empty newer Customer -> empty", cl.Count == 1 && cl[0].Customer == "");
            var cj = History.ParseJson(History.ToJson(new List<QuoteRecord> { c1 }));
            True("Customer JSON round trip", cj != null && cj.Count == 1 && cj[0].Customer == "Anne");
            var cOld = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Hours\":1},{\"Customer\":null},{\"Customer\":\"Åse\"}]}");
            True("Customer old file / null -> empty, name kept", cOld != null && cOld.Count == 3 && cOld[0].Customer == ""
                 && cOld[1].Customer == "" && cOld[2].Customer == "Åse");
            var rl = new List<QuoteRecord>();
            foreach (var nm in new string[] { "Anne", " bo ", "", "ANNE", "  ", null, "Carl", "Bo", "Dan" })
                rl.Add(new QuoteRecord { Customer = nm });
            var rc = History.RecentCustomers(rl, 10);
            True("RecentCustomers distinct, trimmed, case-insensitive, newest first, no empties",
                 rc.Count == 4 && rc[0] == "Anne" && rc[1] == "bo" && rc[2] == "Carl" && rc[3] == "Dan");
            var rc2 = History.RecentCustomers(rl, 2);
            True("RecentCustomers max 2", rc2.Count == 2 && rc2[0] == "Anne" && rc2[1] == "bo");
            True("RecentCustomers null/empty list/max 0", History.RecentCustomers(null, 5).Count == 0
                 && History.RecentCustomers(new List<QuoteRecord>(), 5).Count == 0 && History.RecentCustomers(rl, 0).Count == 0);
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

            // idea 22: limit 5000 (Add trims to MaxQuotes)
            True("History MaxQuotes 5000", History.MaxQuotes == 5000);
            var lim = new List<QuoteRecord>();
            for (int i = 0; i < 5000; i++) { var q = new QuoteRecord(); q.Id = "L" + i; lim.Add(q); }
            History.Add(lim, HistRecord(t0));
            True("History Add 5001st -> 5000, oldest dropped", lim.Count == 5000 && lim[4999].Id == "L4998"
                 && !lim.Exists(x => x.Id == "L4999"));
            HistoryMonthTests();

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
            HistoryReadAheadTests();
            OldestFirstTests();
        }

        // ---------- optimizer v1.23: History read once (ReadAhead + LoadFrom); files only in bin\test-history-read ----------
        static void HistoryReadAheadTests()
        {
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-history-read");
            string file = System.IO.Path.Combine(dir, "history.json");
            try
            {
                True("ReadAhead UseFolder test-history-read", AppSettings.UseFolder(dir));
                True("ReadAhead FilePath in test folder", string.Equals(History.FilePath, file, StringComparison.OrdinalIgnoreCase));
                System.IO.Directory.CreateDirectory(dir);
                foreach (var f in System.IO.Directory.GetFiles(dir, "history*.json*")) System.IO.File.Delete(f);
                History.ResetBlock();
                string problem;
                var t0 = new DateTime(2026, 3, 1, 10, 0, 0);
                var a1 = HistRecord(t0); a1.Id = "RA1";
                var a2 = HistRecord(t0.AddHours(1)); a2.Id = "RA2";
                string json = History.ToJson(new List<QuoteRecord> { a2, a1 });

                // 1. missing file -> Existed false, empty list; LoadFrom hands it out, no read
                History.FileReads = 0;
                var miss = History.ReadAhead();
                True("ReadAhead missing -> empty list, no read", miss != null && miss.List != null && miss.List.Count == 0
                     && !miss.Existed && History.FileReads == 0);
                History.SetBlock();
                var missList = History.LoadFrom(miss, out problem);
                True("LoadFrom missing -> empty, no problem, no read, unblocked", missList.Count == 0 && problem == null
                     && History.FileReads == 0 && History.Save(new List<QuoteRecord>()) == null);
                System.IO.File.Delete(file);

                // 2. unchanged file -> the read-ahead list, no second read; saveBlocked cleared like a good Load
                System.IO.File.WriteAllText(file, json);
                History.FileReads = 0;
                var ahead = History.ReadAhead();
                True("ReadAhead existing -> list of 2, one read", ahead != null && ahead.List != null && ahead.List.Count == 2
                     && History.FileReads == 1);
                var aheadList = ahead.List;
                History.FileReads = 0;
                History.SetBlock();
                var got = History.LoadFrom(ahead, out problem);
                True("LoadFrom unchanged -> same list, no read, no problem", object.ReferenceEquals(got, aheadList)
                     && History.FileReads == 0 && problem == null);
                True("LoadFrom unchanged -> same content as a fresh parse",
                     History.ToJson(got) == History.ToJson(History.ParseJson(System.IO.File.ReadAllText(file))));
                True("LoadFrom unchanged -> List handed out once (now null)", ahead.List == null);
                True("LoadFrom unchanged -> Save works (unblocked)", History.Save(got) == null);
                // handed out already -> a second LoadFrom reads normally
                History.FileReads = 0;
                var again = History.LoadFrom(ahead, out problem);
                True("LoadFrom used read-ahead -> reads again", History.FileReads == 1 && again.Count == 2 && problem == null);

                // 3. touched file (new write time, same size) -> reads again and gets the new content
                System.IO.File.WriteAllText(file, json);
                var ahead3 = History.ReadAhead();
                string json3 = json.Replace("\"RA1\"", "\"RB1\"");   // same length, one id changed
                True("touched test: same size", json3.Length == json.Length && json3 != json);
                System.IO.File.WriteAllText(file, json3);
                System.IO.File.SetLastWriteTimeUtc(file, System.IO.File.GetLastWriteTimeUtc(file).AddSeconds(5));
                History.FileReads = 0;
                var got3 = History.LoadFrom(ahead3, out problem);
                True("LoadFrom touched -> reads again, new content", History.FileReads == 1 && problem == null
                     && got3.Count == 2 && got3.Exists(r => r.Id == "RB1") && !got3.Exists(r => r.Id == "RA1"));

                // 4. file gone after the read-ahead -> Load: empty list, no problem
                var ahead4 = History.ReadAhead();
                System.IO.File.Delete(file);
                var got4 = History.LoadFrom(ahead4, out problem);
                True("LoadFrom file gone -> empty, no problem", got4.Count == 0 && problem == null);
                // file appeared after a 'missing' read-ahead -> reads it
                var ahead4b = History.ReadAhead();
                System.IO.File.WriteAllText(file, json);
                History.FileReads = 0;
                var got4b = History.LoadFrom(ahead4b, out problem);
                True("LoadFrom file appeared -> reads it", History.FileReads == 1 && got4b.Count == 2 && problem == null);

                // 5. bad file -> ReadAhead null, never renames; LoadFrom(null) = Load: renamed + problem
                System.IO.File.WriteAllText(file, "garbage{");
                History.ResetBlock();
                var bad = History.ReadAhead();
                True("ReadAhead bad -> null, file kept, nothing renamed", bad == null && System.IO.File.Exists(file)
                     && System.IO.Directory.GetFiles(dir, "history.bad-*.json").Length == 0);
                True("ReadAhead bad -> Save not blocked", History.Save(new List<QuoteRecord> { a1 }) == null);
                System.IO.File.WriteAllText(file, "garbage{");
                var gotBad = History.LoadFrom(bad, out problem);
                var bads = System.IO.Directory.GetFiles(dir, "history.bad-*.json");
                True("LoadFrom bad -> empty + problem + renamed like Load", gotBad.Count == 0 && problem != null
                     && bads.Length == 1 && System.IO.File.ReadAllText(bads[0]) == "garbage{" && !System.IO.File.Exists(file));
                // newer version -> ReadAhead null too
                System.IO.File.WriteAllText(file, "{\"Version\":2,\"Quotes\":[]}");
                True("ReadAhead Version 2 -> null, kept", History.ReadAhead() == null && System.IO.File.Exists(file)
                     && System.IO.Directory.GetFiles(dir, "history.bad-*.json").Length == 1);
                History.LoadFrom(null, out problem);
                True("LoadFrom(null) Version 2 -> problem + renamed", problem != null
                     && System.IO.Directory.GetFiles(dir, "history.bad-*.json").Length == 2);
            }
            finally
            {
                History.ResetBlock();
                AppSettings.ClearTestFolder();
            }
        }

        // ---------- History.OldestFirst (shared by InvoiceGroup and the main window) ----------
        static void OldestFirstTests()
        {
            var t0 = new DateTime(2026, 4, 1, 9, 0, 0);
            var a = HistRecord(t0.AddHours(2)); a.Id = "A";   // newest
            var b = HistRecord(t0); b.Id = "B";                 // same time as C
            var c = HistRecord(t0); c.Id = "C";
            var d = HistRecord(t0.AddHours(1)); d.Id = "D";
            var x = HistRecord(t0); x.Id = "X"; x.SavedAt = "not a time";
            var y = HistRecord(t0); y.Id = "Y"; y.SavedAt = "";
            // list newest first: positions A0 X1 B2 D3 C4 Y5, plus a null and a repeat of B
            var jobs = new List<QuoteRecord> { a, x, b, null, d, c, b, y };
            var o = History.OldestFirst(jobs);
            string ids = string.Join(",", o.ConvertAll(r => r.Id).ToArray());
            // valid first by time: C/B at 9:00 (C later in the list -> older -> first), D 10:00, A 11:00;
            // then unreadable times, later position first: Y, X
            True("OldestFirst order C,B,D,A,Y,X (got " + ids + ")", ids == "C,B,D,A,Y,X");
            True("OldestFirst leaves the input as is", jobs.Count == 8 && jobs[0] == a);
            True("OldestFirst null -> empty", History.OldestFirst(null).Count == 0);
            // InvoiceGroup: filter by code then the same order
            a.InvoiceNo = "2026-12345"; b.InvoiceNo = " 2026-12345 "; c.InvoiceNo = "2026-12345"; d.InvoiceNo = "2026-99999";
            var g = History.InvoiceGroup(new List<QuoteRecord> { a, x, b, d, c, y }, "2026-12345");
            string gids = string.Join(",", g.ConvertAll(r => r.Id).ToArray());
            True("InvoiceGroup = filter + OldestFirst C,B,A (got " + gids + ")", gids == "C,B,A");
        }

        // ---------- backup and restore (idea 27): History backup/merge/restore, Backup code + blob ----------
        static QuoteRecord BRec(string id, string savedAt)
        {
            var r = HistRecord(DateTime.ParseExact(savedAt, History.TimeFormat, System.Globalization.CultureInfo.InvariantCulture));
            r.Id = id;
            return r;
        }

        // ---------- idea 21: several model files with a colour each (QuoteRecord.Parts) ----------
        // an older app's record and file: no Parts key (set only by the JSON reader, hence the initial values)
        public class OldQuoteRecord { public string Id = ""; public int Hours = 0; }
        public class OldHistoryFile { public int Version = 0; public List<OldQuoteRecord> Quotes = null; }

        static List<HistoryPart> ThreeParts()
        {
            return new List<HistoryPart> {
                new HistoryPart { File = @"C:\Models\body.stl", Filament = "Bambu PLA Basic", Colour = "#ff0000", Slot = 2 },
                new HistoryPart { File = @"C:\Models\wheel.stl", Filament = "", Colour = "", Slot = 1 },
                new HistoryPart { File = @"C:\Models\lid.stl", Filament = "Bambu PLA Basic", Colour = "#FF0000", Slot = 2 } };
        }

        static string PartsText(QuoteRecord r)
        {
            var l = History.PartsOf(r);
            if (l == null) return "NULL";
            var s = new List<string>();
            foreach (var f in l) s.Add(f.File + "," + f.Filament + "," + f.Colour + "," + f.Slot);
            return (History.PartsTogether(r) ? "T:" : "S:") + J(s);
        }

        static void HistoryPartsTests()
        {
            const string Three = @"T:C:\Models\body.stl,Bambu PLA Basic,#FF0000,2|C:\Models\wheel.stl,,,1|C:\Models\lid.stl,Bambu PLA Basic,#FF0000,2";
            var t = new DateTime(2026, 11, 12, 12, 0, 0);

            // set: own copies, colour upper case
            var r = BRec("P1", "2026-11-12 10:00:00");
            True("Parts: new record has none", r.Parts == null && History.PartsOf(r) == null && !History.PartsTogether(r) && History.PartSlots(r) == null);
            var input = ThreeParts();
            History.SetParts(r, input, true);
            input[0].File = "changed"; input.Add(new HistoryPart { File = "x.stl" });
            True("Parts: set + read back (own copy)", PartsText(r) == Three);
            History.PartsOf(r)[0].Colour = "#000000";
            True("Parts: PartsOf gives a copy", r.Parts.Files[0].Colour == "#FF0000");

            // slots: index i = slot i+1, the first file with the slot gives its choice
            var slots = History.PartSlots(r);
            True("Parts: 2 slots, slot 1 = Settings' filament, slot 2 = red PLA Basic", slots.Count == 2
                 && slots[0].Filament == "" && slots[0].Colour == "" && slots[0].Slot == 1
                 && slots[1].Filament == "Bambu PLA Basic" && slots[1].Colour == "#FF0000" && slots[1].Slot == 2 && slots[1].File == "");
            var gap = BRec("P2", "2026-11-12 10:00:00");
            History.SetParts(gap, new List<HistoryPart> { new HistoryPart { File = "a.stl", Colour = "#00FF00", Slot = 3 } }, false);
            slots = History.PartSlots(gap);
            True("Parts: unused slots 1-2 empty, slot 3 green", slots.Count == 3 && slots[0].Colour == "" && slots[1].Filament == ""
                 && slots[2].Colour == "#00FF00" && !History.PartsTogether(gap));

            // cleaning (SetParts and reading): null entries and blank file names dropped, bad colour "", slot 0..64 (negative -> 1)
            var odd = BRec("P3", "2026-11-12 10:00:00");
            History.SetParts(odd, new List<HistoryPart> { null, new HistoryPart { File = "  " },
                new HistoryPart { File = "a.stl", Filament = null, Colour = "red", Slot = 0 },
                new HistoryPart { File = "b.stl", Filament = " Bambu PETG HF ", Colour = " #12abEF ", Slot = 99 },
                new HistoryPart { File = "c.stl", Colour = "#12345G", Slot = -5 } }, false);
            True("Parts: cleaned", PartsText(odd) == "S:a.stl,,,0|b.stl,Bambu PETG HF,#12ABEF,64|c.stl,,,1");

            // slot 0 = a .3mf project keeping its own colours: kept through JSON, no colour line, the STL after it keeps slot 1
            var proj = BRec("P4", "2026-11-12 10:00:00");
            History.SetParts(proj, new List<HistoryPart> {
                new HistoryPart { File = "robot.3mf", Filament = "Bambu PETG HF", Colour = "#0000FF", Slot = 0 },
                new HistoryPart { File = "stand.stl", Filament = "Bambu PLA Basic", Colour = "#ff0000", Slot = 1 } }, true);
            const string Proj = "T:robot.3mf,Bambu PETG HF,#0000FF,0|stand.stl,Bambu PLA Basic,#FF0000,1";
            True("Parts: slot 0 kept", PartsText(proj) == Proj);
            var projBack = History.ParseJson(History.ToJson(new List<QuoteRecord> { proj }));
            True("Parts: slot 0 JSON round trip", projBack != null && projBack.Count == 1 && PartsText(projBack[0]) == Proj);
            slots = History.PartSlots(projBack[0]);
            True("Parts: PartSlots skips slot 0, STL in slot 1 not hidden", slots.Count == 1 && slots[0].Slot == 1
                 && slots[0].Filament == "Bambu PLA Basic" && slots[0].Colour == "#FF0000");
            var onlyProj = BRec("P5", "2026-11-12 10:00:00");
            History.SetParts(onlyProj, new List<HistoryPart> { new HistoryPart { File = "robot.3mf", Colour = "#0000FF", Slot = 0 } }, false);
            slots = History.PartSlots(onlyProj);
            True("Parts: only a slot-0 project -> one empty slot 1", slots.Count == 1 && slots[0].Slot == 1 && slots[0].Filament == "" && slots[0].Colour == "");
            var neg = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"n\",\"Parts\":{\"Files\":[{\"File\":\"p.3mf\",\"Slot\":0},{\"File\":\"q.stl\",\"Slot\":-2},{\"File\":\"r.stl\"}]}}]}");
            True("Parts: read slot 0 kept, negative -> 1, no Slot key -> 1", neg != null && neg.Count == 1 && PartsText(neg[0]) == "S:p.3mf,,,0|q.stl,,,1|r.stl,,,1");
            History.SetParts(odd, new List<HistoryPart> { null, new HistoryPart { File = "" } }, true);
            True("Parts: no files left -> none", odd.Parts == null && !History.PartsTogether(odd));
            History.SetParts(r, ThreeParts(), true);
            History.SetParts(odd, ThreeParts(), true); History.SetParts(odd, null, true);
            True("Parts: SetParts null clears", odd.Parts == null);

            // history.json round trip (save -> load)
            var plain = BRec("P0", "2026-11-11 10:00:00");
            var back = History.ParseJson(History.ToJson(new List<QuoteRecord> { r, plain }));
            True("Parts: JSON round trip keeps files + together", back != null && back.Count == 2 && PartsText(back[0]) == Three);
            True("Parts: JSON round trip, normal record stays none", back[1].Parts == null && History.ToJson(back) == History.ToJson(new List<QuoteRecord> { r, plain }));

            // old file without the key; odd stored shapes
            var old = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"old\",\"SavedAt\":\"2025-05-01 10:00:00\",\"Hours\":2,\"Grams\":50}]}");
            True("Parts: old history.json loads, no parts", old != null && old.Count == 1 && old[0].Parts == null && History.PartsOf(old[0]) == null
                 && History.PartSlots(old[0]) == null && old[0].Hours == 2);
            var shapes = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"Parts\":null},{\"Id\":\"b\",\"Parts\":{\"Together\":true}},"
                + "{\"Id\":\"c\",\"Parts\":{\"Together\":true,\"Files\":[]}},{\"Id\":\"d\",\"Parts\":{\"Files\":[null,{\"File\":\"x.stl\",\"Colour\":\"#abcdef\"}]}}]}");
            True("Parts: null / no Files / empty Files -> none", shapes != null && shapes.Count == 4 && shapes[0].Parts == null
                 && shapes[1].Parts == null && shapes[2].Parts == null);
            True("Parts: read file cleaned (null dropped, missing texts, slot 1)", PartsText(shapes[3]) == "S:x.stl,,#ABCDEF,1");

            // an older app version reads a new file: the extra key is ignored
            var older = Json.Deserialize<OldHistoryFile>(History.ToJson(new List<QuoteRecord> { r }));
            True("Parts: older app reads the new file", older != null && older.Version == 1 && older.Quotes.Count == 1 && older.Quotes[0].Id == "P1");

            // deep copy (Combine's CopyQuotes) = the JSON round trip
            var copied = History.CopyQuotes(new List<QuoteRecord> { r });
            True("Parts: CopyQuotes = JSON round trip", History.ToJson(copied) == History.ToJson(History.ParseJson(History.ToJson(new List<QuoteRecord> { r }))));
            True("Parts: CopyQuotes own objects", !ReferenceEquals(copied[0].Parts, r.Parts) && !ReferenceEquals(copied[0].Parts.Files[0], r.Parts.Files[0]));

            // backup file round trip
            BackupData data;
            True("Parts: backup round trip", History.ReadBackup(History.ToBackup(new List<QuoteRecord> { r, plain }, null, "1.18.0", t), out data) == null
                 && data.Quotes.Count == 2 && PartsText(data.Quotes[0]) == Three && data.Quotes[1].Parts == null);

            // merge: a new record comes in with its parts; the same record without parts here gets them; parts here are kept
            var res = History.Combine(new List<QuoteRecord> { plain }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { r } }, false, false);
            True("Parts: merge adds record with parts", res.Added == 1 && res.Quotes.Count == 2 && PartsText(res.Quotes[0]) == Three);
            var mine = BRec("P1", "2026-11-12 10:00:00");
            res = History.Combine(new List<QuoteRecord> { mine }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { r } }, false, false);
            True("Parts: merge fills a record without parts", res.Updated == 1 && res.Quotes.Count == 1 && PartsText(res.Quotes[0]) == Three
                 && mine.Parts == null && !ReferenceEquals(res.Quotes[0].Parts, r.Parts));
            var theirs = BRec("P1", "2026-11-12 10:00:00");
            History.SetParts(theirs, new List<HistoryPart> { new HistoryPart { File = "other.stl", Slot = 1 } }, false);
            res = History.Combine(new List<QuoteRecord> { r }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("Parts: merge keeps this PC's parts", res.Same == 1 && res.Updated == 0 && PartsText(res.Quotes[0]) == Three);
            res = History.Combine(new List<QuoteRecord> { mine }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, true, false);
            True("Parts: replace takes the backup's", res.Quotes.Count == 1 && PartsText(res.Quotes[0]) == @"S:other.stl,,,1");
            res = History.Combine(new List<QuoteRecord> { r }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { mine } }, false, false);
            True("Parts: merge from an old backup (no parts) keeps them", res.Same == 1 && PartsText(res.Quotes[0]) == Three);
        }

        static string Hex(byte[] b)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        static List<string> Ids(List<QuoteRecord> l)
        {
            var res = new List<string>();
            foreach (var r in l) res.Add(r.Id);
            return res;
        }

        static void BackupTests()
        {
            var t = new DateTime(2026, 11, 10, 12, 0, 0);
            var A = BRec("A", "2026-11-05 10:00:00");
            var B = BRec("B", "2026-11-01 10:00:00");
            var C = BRec("C", "2026-11-07 10:00:00");
            var s = new AppSettings();
            s.SlicerPath = @"C:\Bambu\bambu-studio.exe"; s.FriendsMarkupPct = 33; s.UsedInvoiceNos = "2026-AAAA";

            // backup text round trip
            string text = History.ToBackup(new List<QuoteRecord> { A, B, C }, s, "1.10.0", t);
            BackupData data;
            True("Backup round trip reads", History.ReadBackup(text, out data) == null && data != null);
            True("Backup round trip records", J(Ids(data.Quotes)) == "A|B|C" && data.CreatedAt == "2026-11-10 12:00:00" && data.App == "1.10.0");
            True("Backup has settings without SlicerPath", data.Settings != null
                 && !Array.Exists(data.Settings, x => x.StartsWith("SlicerPath")) && text.IndexOf("bambu-studio", StringComparison.Ordinal) < 0);
            True("Backup settings read back", AppSettings.FromIni(data.Settings).FriendsMarkupPct == 33
                 && AppSettings.FromIni(data.Settings).UsedInvoiceNos == "2026-AAAA");
            True("Backup Format/Version in text", text.Contains("\"Format\":\"PrintPriceBackup\"") && text.Contains("\"Version\":1"));
            True("Backup file name", History.BackupFileName(t) == "PrintPrice backup 2026-11-10.printprice-backup");
            True("Backup history only (null settings)", History.ReadBackup(History.ToBackup(new List<QuoteRecord> { A }, null, "x", t), out data) == null
                 && data.Settings == null && data.Quotes.Count == 1);
            // plain history.json
            True("Backup reads plain history.json", History.ReadBackup(History.ToJson(new List<QuoteRecord> { A, B, C }), out data) == null
                 && data.Settings == null && J(Ids(data.Quotes)) == "A|B|C");
            // refused
            string p;
            p = History.ReadBackup(Json.Serialize(new BackupFile { Format = History.BackupFormat, Version = 2,
                History = new HistoryFile { Version = 1, Quotes = new List<QuoteRecord>() } }), out data);
            True("Backup Version 2 -> newer", p != null && p.Contains("newer") && data == null);
            p = History.ReadBackup(Json.Serialize(new BackupFile { Format = History.BackupFormat, Version = 1,
                History = new HistoryFile { Version = 2, Quotes = new List<QuoteRecord>() } }), out data);
            True("Backup History Version 2 -> newer", p != null && p.Contains("newer") && data == null);
            p = History.ReadBackup("{\"Version\":2,\"Quotes\":[]}", out data);
            True("Plain history Version 2 -> newer", p != null && p.Contains("newer") && data == null);
            True("Backup garbage refused", History.ReadBackup("garbage{", out data) != null && data == null);
            True("Backup empty refused", History.ReadBackup("", out data) != null && History.ReadBackup(null, out data) != null);
            True("Backup other Format refused", History.ReadBackup("{\"Format\":\"Other\",\"Version\":1}", out data) != null);
            True("Backup [] refused", History.ReadBackup("[1,2]", out data) != null);

            // merge: new ones in, newest first; caller's list untouched
            var cur = new List<QuoteRecord> { A, B };
            var res = History.Combine(cur, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { BRec("B", "2026-11-01 10:00:00"), C } }, false, false);
            True("Merge order C|A|B", J(Ids(res.Quotes)) == "C|A|B");
            True("Merge counts added 1 same 1", res.Added == 1 && res.Same == 1 && res.Updated == 0 && res.PrintersMoved == 0);
            True("Merge leaves current list alone", cur.Count == 2 && !object.ReferenceEquals(res.Quotes[1], A));

            // merge: same Id filled in from the backup
            var B2 = BRec("B", "2026-11-01 10:00:00");
            B2.InvoiceNo = "2026-K7Q4"; B2.BilledAt = "2026-11-02 09:00:00"; B2.InvoiceDue = "2026-11-16"; B2.InvoicePayBank = "1234 5678";
            B2.Printed = true; B2.Note = "theirs"; B2.Customer = "Anna";
            var Bh = BRec("B", "2026-11-01 10:00:00"); Bh.Note = "mine";
            res = History.Combine(new List<QuoteRecord> { Bh }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { B2 } }, false, false);
            var mb = res.Quotes[0];
            True("Merge same Id: invoice taken", res.Updated == 1 && mb.InvoiceNo == "2026-K7Q4" && mb.BilledAt == "2026-11-02 09:00:00"
                 && mb.InvoiceDue == "2026-11-16" && mb.InvoicePayBank == "1234 5678");
            True("Merge same Id: Printed, own Note kept, Customer filled", mb.Printed && mb.Note == "mine" && mb.Customer == "Anna");
            True("Merge same Id: caller's record unchanged", Bh.InvoiceNo == "" && !Bh.Printed && Bh.Customer == "");
            res = History.Combine(new List<QuoteRecord> { B2 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { Bh } }, false, false);
            True("Merge same Id: this PC's invoice kept, nothing new = Same", res.Same == 1 && res.Updated == 0 && res.Quotes[0].InvoiceNo == "2026-K7Q4" && res.Quotes[0].Note == "theirs");
            // BilledTo (combined-bill Part B): taken with the invoice; same invoice here without a name -> filled; own name kept
            var Bt = BRec("B", "2026-11-01 10:00:00"); Bt.InvoiceNo = "2026-K7Q4"; Bt.BilledAt = "2026-11-02 09:00:00";
            Bt.Customer = "Anna Jensen"; Bt.BilledTo = "Anna";
            var Bn = BRec("B", "2026-11-01 10:00:00"); Bn.Customer = "Ole";
            res = History.Combine(new List<QuoteRecord> { Bn }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { Bt } }, false, false);
            True("Merge: BilledTo taken with the invoice, own Customer kept", res.Updated == 1 && res.Quotes[0].BilledTo == "Anna" && res.Quotes[0].Customer == "Ole");
            var Bs = BRec("B", "2026-11-01 10:00:00"); Bs.InvoiceNo = "2026-K7Q4"; Bs.BilledAt = "2026-11-02 09:00:00";
            res = History.Combine(new List<QuoteRecord> { Bs }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { Bt } }, false, false);
            True("Merge: same invoice without BilledTo here -> filled", res.Updated == 1 && res.Quotes[0].BilledTo == "Anna" && res.Quotes[0].Customer == "Anna Jensen");
            var Bo = BRec("B", "2026-11-01 10:00:00"); Bo.InvoiceNo = "2026-K7Q4"; Bo.BilledAt = "2026-11-02 09:00:00"; Bo.Customer = "Anna Jensen"; Bo.BilledTo = "A. Jensen";
            res = History.Combine(new List<QuoteRecord> { Bo }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { Bt } }, false, false);
            True("Merge: this PC's BilledTo kept", res.Same == 1 && res.Quotes[0].BilledTo == "A. Jensen");
            var Bx = BRec("B", "2026-11-01 10:00:00"); Bx.InvoiceNo = "2026-ZZZZ"; Bx.BilledAt = "2026-11-02 09:00:00"; Bx.Customer = "Anna Jensen";
            res = History.Combine(new List<QuoteRecord> { Bx }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { Bt } }, false, false);
            True("Merge: other invoice code -> BilledTo not taken", res.Quotes[0].BilledTo == "" && res.Quotes[0].InvoiceNo == "2026-ZZZZ");
            // backup round trip keeps BilledTo; a backup without the key reads ""
            True("BilledTo: backup round trip", History.ReadBackup(History.ToBackup(new List<QuoteRecord> { Bt }, null, "1.22.0", t), out data) == null
                 && data.Quotes[0].BilledTo == "Anna" && data.Quotes[0].Customer == "Anna Jensen");
            True("BilledTo: backup without the key / null -> \"\"", History.ReadBackup("{\"Version\":1,\"Quotes\":[{\"Id\":\"x\",\"InvoiceNo\":\"2026-K7Q4\",\"Customer\":\"Ole\"},{\"Id\":\"y\",\"BilledTo\":null}]}", out data) == null
                 && data.Quotes[0].BilledTo == "" && data.Quotes[1].BilledTo == "");
            // failed in the backup only -> failed
            var Fb = BRec("A", "2026-11-05 10:00:00"); History.MarkFailed(Fb, true, new DateTime(2026, 11, 6, 8, 0, 0));
            res = History.Combine(new List<QuoteRecord> { A }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { Fb } }, false, false);
            True("Merge same Id: Failed taken", res.Quotes[0].Failed && res.Quotes[0].FailedAt == "2026-11-06 08:00:00" && res.Updated == 1);
            // no Id: matched by content
            var N1 = BRec("", "2026-11-03 10:00:00"); var N2 = BRec("", "2026-11-03 10:00:00");
            res = History.Combine(new List<QuoteRecord> { N1 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { N2 } }, false, false);
            True("Merge no Id: same content = same record", res.Quotes.Count == 1 && res.Same == 1);

            // invoice codes: clash kept, used list = both sides + both histories
            var Ai = BRec("A", "2026-11-05 10:00:00"); Ai.InvoiceNo = "2026-K7Q4";
            var Ci = BRec("C", "2026-11-07 10:00:00"); Ci.InvoiceNo = "2026-K7Q4";
            var bs = new AppSettings(); bs.UsedInvoiceNos = "2026-BBBB";
            var cs = new AppSettings(); cs.UsedInvoiceNos = "2026-AAAA";
            res = History.Combine(new List<QuoteRecord> { Ai }, cs, new BackupData { Quotes = new List<QuoteRecord> { Ci }, Settings = bs.ToIni() }, false, false);
            True("Invoice clash: both kept, codes unchanged", res.Quotes.Count == 2 && res.Quotes[0].InvoiceNo == "2026-K7Q4"
                 && res.Quotes[1].InvoiceNo == "2026-K7Q4" && J(res.InvoiceClashes) == "2026-K7Q4");
            True("Invoice used list = AAAA, BBBB, K7Q4", res.Settings.UsedInvoiceNos == "2026-AAAA,2026-BBBB,2026-K7Q4");
            True("Settings not restored -> this PC's", !res.SettingsRestored && res.Settings.UsedInvoiceNos != cs.UsedInvoiceNos
                 && cs.UsedInvoiceNos == "2026-AAAA");

            // replace: history = the backup's, replaced codes still never given again
            var Az = BRec("A", "2026-11-05 10:00:00"); Az.InvoiceNo = "2026-ZZZZ";
            res = History.Combine(new List<QuoteRecord> { Az }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { C } }, true, false);
            True("Replace: only the backup's", J(Ids(res.Quotes)) == "C" && res.Added == 1);
            True("Replace: replaced code stays used", res.Settings.UsedInvoiceNos == "2026-ZZZZ");

            // printers: same id, different model -> moved to a new id
            cs = new AppSettings(); cs.AddPrinter("Mini", Wear.ModelA1Mini);              // here: 1 = A1, 2 = A1 mini
            bs = new AppSettings(); bs.AddPrinter("Big", Wear.ModelP1S);                  // backup: 1 = A1, 2 = P1S
            True("Printers set up", cs.FindPrinter("2") != null && bs.FindPrinter("2") != null && cs.LastPrinterId == 2);
            var R1 = BRec("R1", "2026-11-04 10:00:00"); R1.PrinterId = "1";
            var R2 = BRec("R2", "2026-11-04 11:00:00"); R2.PrinterId = "2"; R2.PrinterModel = Wear.ModelP1S;
            var R5 = BRec("R5", "2026-11-04 12:00:00"); R5.PrinterId = "5"; R5.PrinterModel = Wear.ModelP1S;
            var bdata = new BackupData { Quotes = new List<QuoteRecord> { R5, R2, R1 }, Settings = bs.ToIni() };
            res = History.Combine(new List<QuoteRecord>(), cs, bdata, false, false);
            var byId = new Dictionary<string, QuoteRecord>();
            foreach (var r in res.Quotes) byId[r.Id] = r;
            // ids reserved first: here 2, backup 2, history 5 -> LastPrinterId 5, so the moved printer gets 6
            True("Printer clash moved to 6", byId["R2"].PrinterId == "6" && res.PrintersMoved == 1);
            True("Printer same model kept, unknown id kept", byId["R1"].PrinterId == "1" && byId["R5"].PrinterId == "5");
            True("Printer ids reserved (LastPrinterId 6)", res.Settings.LastPrinterId == 6 && R2.PrinterId == "2");
            var bs2 = new AppSettings(); bs2.AddPrinter("Other name", Wear.ModelA1Mini);
            var R2b = BRec("R2", "2026-11-04 11:00:00"); R2b.PrinterId = "2";
            res = History.Combine(new List<QuoteRecord>(), cs, new BackupData { Quotes = new List<QuoteRecord> { R2b }, Settings = bs2.ToIni() }, false, false);
            True("Printer renamed only -> not moved", res.Quotes[0].PrinterId == "2" && res.PrintersMoved == 0);
            // with the backup's settings: this PC's records move instead, SlicerPath stays this PC's
            cs.SlicerPath = @"D:\Studio\bambu-studio.exe";
            var H2 = BRec("H2", "2026-11-03 10:00:00"); H2.PrinterId = "2"; H2.PrinterModel = Wear.ModelA1Mini;
            res = History.Combine(new List<QuoteRecord> { H2 }, cs, new BackupData { Quotes = new List<QuoteRecord> { R2 }, Settings = bs.ToIni() }, false, true);
            byId.Clear();
            foreach (var r in res.Quotes) byId[r.Id] = r;
            True("With settings: backup's printers", res.SettingsRestored && res.Settings.FindPrinter("2").Model == Wear.ModelP1S
                 && res.Settings.SlicerPath == @"D:\Studio\bambu-studio.exe");
            True("With settings: this PC's clashing records moved", byId["H2"].PrinterId == "3" && byId["R2"].PrinterId == "2" && res.PrintersMoved == 1);
            res = History.Combine(new List<QuoteRecord>(), cs, new BackupData { Quotes = new List<QuoteRecord> { C } }, false, true);
            True("With settings but none in backup -> this PC's", !res.SettingsRestored && res.Settings.FindPrinter("2").Model == Wear.ModelA1Mini);

            // failed-print debt is not doubled: FailedCost = 21,60 + 1,10 + 5,5 h x 2 = 33,70; paid 12,00 -> 21,70
            var F = BRec("F", "2026-11-02 10:00:00"); History.MarkFailed(F, true, new DateTime(2026, 11, 2, 12, 0, 0));
            var P = BRec("P", "2026-11-04 10:00:00"); P.PaybackCost = 12; P.FailedPaybackWho = 2; P.For = History.ForCustomer;
            var both = new List<QuoteRecord> { P, F };
            Eq("Debt before merge 33,70 - 12,00", 21.70, History.FailedDebt(both, null));
            res = History.Combine(both, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { BRec("P", "2026-11-04 10:00:00"), F } }, false, false);
            Eq("Debt after merge 21,70", 21.70, History.FailedDebt(res.Quotes, null));

            // trimmed to MaxQuotes
            var many = new List<QuoteRecord>();
            for (int i = 0; i < 4990; i++) { var q = new QuoteRecord(); q.Id = "L" + i; many.Add(q); }
            var more = new List<QuoteRecord>();
            for (int i = 0; i < 20; i++) { var q = new QuoteRecord(); q.Id = "N" + i; more.Add(q); }
            res = History.Combine(many, new AppSettings(), new BackupData { Quotes = more }, false, false);
            True("Merge 4990 + 20 -> 5000", res.Quotes.Count == 5000 && res.Added == 20);

            // CopyQuotes (Combine's deep copy) = the JSON round trip it replaced: same text, same Printed rule, own objects
            var odd = new QuoteRecord();   // fresh: Printed false, never set (printedRead false)
            odd.Id = null; odd.Note = null; odd.Customer = null; odd.InvoiceNo = null; odd.PrinterName = null; odd.For = 0;
            odd.Grams = 0.1 + 0.2; odd.OthersPrice = 1.0 / 3; odd.SavedAt = "2026-11-03 09:00:00";
            var lines = BRec("M", "2026-11-04 10:00:00");
            lines.Filaments = new List<HistoryFilament> { new HistoryFilament { Grams = 12.345678901234, PricePerKg = 199.99, Colour = "#FF0000", Name = "ÆØÅ \"q\" \\ x" },
                                                          null, new HistoryFilament { Colour = null, Type = null, Name = null } };
            var emptyLines = BRec("E", "2026-11-04 11:00:00"); emptyLines.Filaments = new List<HistoryFilament> { null };
            var failed = BRec("F2", "2026-11-04 12:00:00"); History.MarkFailed(failed, true, t); failed.PaidBack = 2.5; failed.Printed = false;
            var mix = new List<QuoteRecord> { A, odd, null, lines, emptyLines, failed };
            var viaJson = History.ParseJson(History.ToJson(mix));
            var copied = History.CopyQuotes(mix);
            True("CopyQuotes = JSON round trip (text)", History.ToJson(copied) == History.ToJson(viaJson) && copied.Count == 5);
            bool sameRead = copied.Count == viaJson.Count;
            for (int i = 0; sameRead && i < copied.Count; i++) sameRead = copied[i].printedRead == viaJson[i].printedRead && copied[i].Printed == viaJson[i].Printed;
            True("CopyQuotes Printed as the round trip (fresh record stays not printed)", sameRead && !copied[1].Printed);
            True("CopyQuotes own objects", !ReferenceEquals(copied[0], A) && !ReferenceEquals(copied[2].Filaments, lines.Filaments)
                 && !ReferenceEquals(copied[2].Filaments[0], lines.Filaments[0]));
            copied[2].Filaments[0].Grams = 99; copied[0].Note = "changed";
            True("CopyQuotes leaves the originals alone", lines.Filaments[0].Grams == 12.345678901234 && A.Note != "changed" && odd.Id == null);
            True("CopyQuotes null / empty", History.CopyQuotes(null).Count == 0 && History.CopyQuotes(new List<QuoteRecord>()).Count == 0);

            BackupCodeTests();
            BackupFileTests();
            BackupSettingsTests();
        }

        // online backup settings: BackupCode (DPAPI) and LastOnlineBackup, both PC-only
        static void BackupSettingsTests()
        {
            const string Code = "K7QF2-9XMRT-B3HDW-P5ZNC";
            var s = new AppSettings();
            True("BackupCode default none", s.BackupCode == "" && s.GetBackupCode() == "" && !s.HasBackupCode && s.LastOnlineBackup == "");
            s.SetBackupCode(" " + Code + " ");
            True("BackupCode stored encrypted", s.BackupCode.StartsWith("dpapi1:") && s.BackupCode.IndexOf("K7QF2", StringComparison.Ordinal) < 0);
            True("BackupCode DPAPI round trip", s.GetBackupCode() == Code && s.HasBackupCode);
            True("UnprotectCode garbled -> none", AppSettings.UnprotectCode("dpapi1:AAAA") == "" && AppSettings.UnprotectCode("dpapi1:not base64!") == ""
                 && AppSettings.UnprotectCode(Code) == "" && AppSettings.UnprotectCode(null) == "" && AppSettings.UnprotectCode("dpapi1:") == "");
            int mid = "dpapi1:".Length + 40;
            string flipped = s.BackupCode.Substring(0, mid) + (s.BackupCode[mid] == 'A' ? 'B' : 'A') + s.BackupCode.Substring(mid + 1);
            var g = new AppSettings(); g.BackupCode = flipped;
            True("BackupCode flipped character -> no code", g.GetBackupCode() == "" && !g.HasBackupCode);
            g.SetBackupCode("  ");
            True("SetBackupCode blank -> empty", g.BackupCode == "");

            s.SetLastOnlineBackup(new DateTime(2026, 11, 10, 14, 5, 33));
            DateTime last;
            True("LastOnlineBackup format", s.LastOnlineBackup == "2026-11-10 14:05" && s.TryLastOnlineBackup(out last)
                 && last == new DateTime(2026, 11, 10, 14, 5, 0));
            True("TryLastOnlineBackup never -> false", !new AppSettings().TryLastOnlineBackup(out last));

            // ini round trip
            var r = AppSettings.FromIni(s.ToIni());
            True("BackupCode ini round trip", r.BackupCode == s.BackupCode && r.GetBackupCode() == Code);
            True("LastOnlineBackup ini round trip", r.LastOnlineBackup == "2026-11-10 14:05");
            True("LastOnlineBackup bad value ignored", AppSettings.FromIni(new[] { "LastOnlineBackup=yesterday" }).LastOnlineBackup == "");
            True("BackupCode garbled ini -> no code", !AppSettings.FromIni(new[] { "BackupCode=dpapi1:xyz" }).HasBackupCode);

            // never in a backup
            s.SlicerPath = @"C:\Bambu\bambu-studio.exe";
            string text = History.ToBackup(new List<QuoteRecord>(), s, "1.12.0", DateTime.Now);
            True("ToBackup has no BackupCode / LastOnlineBackup", text.IndexOf("BackupCode", StringComparison.Ordinal) < 0
                 && text.IndexOf("LastOnlineBackup", StringComparison.Ordinal) < 0 && text.IndexOf("dpapi1", StringComparison.Ordinal) < 0);
            True("PcOnlyKeys has all three", J(new List<string>(History.PcOnlyKeys)) == "SlicerPath|BackupCode|LastOnlineBackup");

            // Combine with "also settings": this PC's keys stay, even when the backup's lines hold another PC's
            var other = new AppSettings(); other.FriendsMarkupPct = 55; other.SetBackupCode("ZZZZZ-ZZZZZ-ZZZZZ-ZZZZZ");
            other.LastOnlineBackup = "2025-01-01 00:00"; other.SlicerPath = @"E:\Other\bambu-studio.exe";
            var res = History.Combine(new List<QuoteRecord>(), s, new BackupData { Quotes = new List<QuoteRecord>(), Settings = other.ToIni() }, true, true);
            True("Combine with settings: backup's settings", res.SettingsRestored && res.Settings.FriendsMarkupPct == 55);
            True("Combine with settings: this PC's keys kept", res.Settings.GetBackupCode() == Code && res.Settings.LastOnlineBackup == "2026-11-10 14:05"
                 && res.Settings.SlicerPath == @"C:\Bambu\bambu-studio.exe");
            res = History.Combine(new List<QuoteRecord>(), s, new BackupData { Quotes = new List<QuoteRecord>(), Settings = other.ToIni() }, false, false);
            True("Combine without settings: this PC's keys kept", res.Settings.GetBackupCode() == Code && res.Settings.FriendsMarkupPct == s.FriendsMarkupPct);

            // first-time setup (idea 19): a restore with settings always ends set up (SetupDone 1), even when this PC
            // (in the setup window) and the backup's own lines say 0; without settings this PC's value stays
            var newPc = new AppSettings(); newPc.SetupDone = 0;
            var other0 = other.Clone(); other0.SetupDone = 0;
            True("backup lines carry SetupDone=0", Array.IndexOf(other0.ToIni(), "SetupDone=0") >= 0);
            res = History.Combine(new List<QuoteRecord>(), newPc, new BackupData { Quotes = new List<QuoteRecord>(), Settings = other0.ToIni() }, true, true);
            True("Combine with settings: SetupDone forced 1", res.SettingsRestored && res.Settings.SetupDone == 1 && res.Settings.FriendsMarkupPct == 55);
            res = History.Combine(new List<QuoteRecord>(), newPc, new BackupData { Quotes = new List<QuoteRecord>(), Settings = other.ToIni() }, false, true);
            True("Combine merge + settings: SetupDone 1", res.SettingsRestored && res.Settings.SetupDone == 1);
            res = History.Combine(new List<QuoteRecord>(), newPc, new BackupData { Quotes = new List<QuoteRecord>(), Settings = other.ToIni() }, true, false);
            True("Combine without settings: this PC's SetupDone 0 kept", !res.SettingsRestored && res.Settings.SetupDone == 0);
            res = History.Combine(new List<QuoteRecord>(), newPc, new BackupData { Quotes = new List<QuoteRecord>(), Settings = null }, true, true);
            True("Combine 'with settings' but backup has none: SetupDone 0 kept", !res.SettingsRestored && res.Settings.SetupDone == 0);
            True("Combine doesn't change the PC's own settings object", newPc.SetupDone == 0);

            // Restore (files, test folder only)
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-backup-keys");
            try
            {
                True("UseFolder test-backup-keys", AppSettings.UseFolder(dir));
                System.IO.Directory.CreateDirectory(dir);
                foreach (var f in System.IO.Directory.GetFiles(dir)) System.IO.File.Delete(f);
                History.ResetBlock();
                s.SetupDone = 0;   // restoring from the setup window
                s.Save();
                BackupData data;
                History.ReadBackup(History.ToBackup(new List<QuoteRecord> { BRec("C", "2026-11-07 10:00:00") }, other, "1.12.0", DateTime.Now), out data);
                RestoreResult rr;
                True("Restore with settings OK", History.Restore(new List<QuoteRecord>(), s, data, true, true, DateTime.Now, out rr) == null);
                var ls = AppSettings.Load();
                True("Restore with settings: saved file keeps this PC's code, time, slicer", ls.FriendsMarkupPct == 55 && ls.GetBackupCode() == Code
                     && ls.LastOnlineBackup == "2026-11-10 14:05" && ls.SlicerPath == @"C:\Bambu\bambu-studio.exe");
                Eq("Restore with settings: saved file has SetupDone 1 (was 0)", 1, ls.SetupDone);
            }
            finally
            {
                History.ResetBlock();
                AppSettings.ClearTestFolder();
            }
        }

        static void BackupCodeTests()
        {
            // the code
            string code = Backup.NewCode();
            True("Code format XXXXX-XXXXX-XXXXX-XXXXX", System.Text.RegularExpressions.Regex.IsMatch(code,
                 "^[0-9A-HJKMNP-TV-Z]{5}(-[0-9A-HJKMNP-TV-Z]{5}){3}$"));
            True("Code valid", Backup.Normalize(code) == code.Replace("-", ""));
            True("Two codes differ", Backup.NewCode() != code);
            // vector code: first 19 = alphabet 0..18, check = Alphabet[SHA-256("0123456789ABCDEFGHJ")[0] & 31] = Alphabet[15] = 'F'
            const string V = "0123456789ABCDEFGHJF";
            True("Code from pick(i) = i", Backup.NewCode(i => i) == "01234-56789-ABCDE-FGHJF");
            True("Normalize lower case, spaces", Backup.Normalize(" 01234 56789-abcde-fghjf ") == V);
            True("Normalize O -> 0", Backup.Normalize("O1234-56789-ABCDE-FGHJF") == V);
            True("Normalize I/L -> 1", Backup.Normalize("0I234-56789-ABCDE-FGHJF") == V && Backup.Normalize("0l234-56789-ABCDE-FGHJF") == V);
            True("Normalize wrong check char -> null", Backup.Normalize("01234-56789-ABCDE-FGHJG") == null);
            True("Normalize U -> null", Backup.Normalize("U1234-56789-ABCDE-FGHJF") == null);
            True("Normalize 19 chars -> null", Backup.Normalize("01234-56789-ABCDE-FGHJ") == null && Backup.Normalize(null) == null);
            True("Clean any length, mapped", Backup.Clean(" o1-il ") == "0111" && Backup.Clean("") == "" && Backup.Clean(null) == null
                 && Backup.Clean("01U") == null && Backup.Clean("01234-56789-ABCDE-FGHJF-0") == "01234567 89ABCDEFGHJF0".Replace(" ", ""));

            // fixed test vector (also in plans\history-backup\test vector.md; HMAC values checked with Python)
            True("Vector StorageId", Backup.StorageId(V) == "5c22a78a2694ed52be5c1efa1565dd0d");
            True("Vector StorageId from typed code", Backup.StorageId("01234-56789-abcde-fghjf") == "5c22a78a2694ed52be5c1efa1565dd0d");
            True("Vector EncKey", Hex(Backup.EncKey(V)) == "5a6ec9831e244ec017f8235240eb60a6260fea40d69b7ace51bf638f09d94b12");
            True("Vector MacKey", Hex(Backup.MacKey(V)) == "058bf9d37c15b495e88de8ca61700934426ed365204c826abb053d58420b7711");
            var iv = new byte[16];
            for (int i = 0; i < 16; i++) iv[i] = (byte)i;
            var plain = System.Text.Encoding.ASCII.GetBytes("PrintPrice backup test vector");
            const string VBlob = "50504231000102030405060708090a0b0c0d0e0fe7b16e3df4b922480ef68fcee419f9fd65ae5df3cd3a4487289b836f84fa9e4375a63f8abc7eca18d55324858906c9521c6e1edc1309753ef28412b8936718db";
            var sealedBlob = Backup.Seal(plain, V, iv);
            True("Vector Seal bytes (84)", Hex(sealedBlob) == VBlob && sealedBlob.Length == 84);
            string p;
            var opened = Backup.Open(sealedBlob, V, out p);
            True("Vector Open", opened != null && p == null && System.Text.Encoding.ASCII.GetString(opened) == "PrintPrice backup test vector");
            const string VPack = "UFBCMRAREhMUFRYXGBkaGxwdHh91dDP3/rCpAGMKMZKP+W33pKtcLHi+MX5czFGyEmQLlbB2qKl/xtLBE8uIs8Gtf6hjkYX67mJuicMydWhoob9EzyZdDVbGlw7sFKasuVyFImsPtdAngkO+V/ft3GO5o01Go3kxBZfYS4/F+hhalEFWRSR/6xO8+9Apfz2s+XGOhlLNOcdelYMPXTcgnZmyDNYjbVf5Is+TtMI+o6n8wMuI5TKC7F/roCM/j8EmKHcOmQ==";
            const string VJson = "{\"Format\":\"PrintPriceBackup\",\"Version\":1,\"CreatedAt\":\"2026-11-10 12:00:00\",\"App\":\"test\",\"History\":{\"Version\":1,\"Quotes\":[]},\"Settings\":null}";
            True("Vector Unpack (gzip blob)", Backup.Unpack(Convert.FromBase64String(VPack), V) == VJson);
            BackupData vd;
            True("Vector JSON is an empty backup", History.ReadBackup(VJson, out vd) == null && vd.Quotes.Count == 0 && vd.Settings == null);

            // Pack / Unpack
            string json = History.ToBackup(new List<QuoteRecord> { BRec("A", "2026-11-05 10:00:00") }, new AppSettings(), "1.10.0", DateTime.Now);
            byte[] blob = Backup.Pack(json, code);
            True("Pack starts with PPB1", Backup.IsBlob(blob) && blob[0] == (byte)'P' && blob[3] == (byte)'1');
            True("Pack/Unpack round trip", Backup.Unpack(blob, code, out p) == json && p == null);
            True("Unpack typed code (lower case, spaces)", Backup.Unpack(blob, code.ToLowerInvariant().Replace("-", " ")) == json);
            string other = Backup.NewCode();
            True("Unpack wrong code -> null + problem", Backup.Unpack(blob, other, out p) == null && p != null);
            True("Unpack invalid code -> null", Backup.Unpack(blob, "nonsense", out p) == null && p != null);
            var b1 = (byte[])blob.Clone(); b1[30] ^= 1;
            True("Unpack flipped ciphertext byte -> null", Backup.Unpack(b1, code, out p) == null && p != null);
            var b2 = (byte[])blob.Clone(); b2[5] ^= 1;
            True("Unpack flipped IV byte -> null", Backup.Unpack(b2, code) == null);
            var b3 = (byte[])blob.Clone(); b3[0] = (byte)'X';
            True("Unpack wrong magic -> null", Backup.Unpack(b3, code, out p) == null && p != null);
            var b4 = (byte[])blob.Clone(); b4[b4.Length - 1] ^= 1;
            True("Unpack flipped HMAC byte -> null", Backup.Unpack(b4, code) == null);
            True("Unpack short / null -> null", Backup.Unpack(new byte[10], code) == null && Backup.Unpack(null, code) == null);
            True("Two Packs differ (random IV)", Hex(Backup.Pack(json, code)) != Hex(blob));
            bool threw = false;
            try { Backup.Pack(json, "bad"); } catch (ArgumentException) { threw = true; }
            True("Pack invalid code throws", threw);
            var big = new List<QuoteRecord>();
            for (int i = 0; i < 5000; i++) big.Add(BRec("Q" + i, "2026-11-05 10:00:00"));
            string bigJson = History.ToBackup(big, new AppSettings(), "1.10.0", DateTime.Now);
            byte[] bigBlob = Backup.Pack(bigJson, code);
            Console.WriteLine("INFO 5000 quotes: JSON " + bigJson.Length + " chars, blob " + bigBlob.Length + " bytes");
            True("5000 quotes blob under 512 KB (Worker limit)", bigBlob.Length < 512 * 1024 && Backup.Unpack(bigBlob, code) == bigJson);
        }

        static void BackupFileTests()
        {
            // files: only in bin\test-backup (test mode), never %APPDATA%
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-backup");
            try
            {
                True("Backup UseFolder test-backup", AppSettings.UseFolder(dir));
                System.IO.Directory.CreateDirectory(dir);
                foreach (var f in System.IO.Directory.GetFiles(dir)) System.IO.File.Delete(f);
                History.ResetBlock();
                var A = BRec("A", "2026-11-05 10:00:00"); A.InvoiceNo = "2026-ZZZZ";
                var C = BRec("C", "2026-11-07 10:00:00");
                var s = new AppSettings(); s.FriendsMarkupPct = 41; s.Save();
                True("Backup test history saved", History.Save(new List<QuoteRecord> { A }) == null);
                string histBefore = System.IO.File.ReadAllText(History.FilePath);
                string iniBefore = System.IO.File.ReadAllText(AppSettings.FilePath);

                string bf = System.IO.Path.Combine(dir, History.BackupFileName(new DateTime(2026, 11, 10)));
                var bset = new AppSettings(); bset.FriendsMarkupPct = 55;
                True("WriteBackupFile OK", History.WriteBackupFile(bf, new List<QuoteRecord> { C }, bset, "1.10.0", DateTime.Now) == null);
                BackupData data;
                True("ReadBackupFile OK", History.ReadBackupFile(bf, out data) == null && data.Quotes.Count == 1 && data.Settings != null);
                True("ReadBackupFile missing -> problem", History.ReadBackupFile(System.IO.Path.Combine(dir, "nope.printprice-backup"), out data) != null);
                History.ReadBackupFile(bf, out data);

                // blocked (history.json unreadable): refused, nothing written
                History.SetBlock();
                RestoreResult rr;
                True("Restore refused while blocked", History.Restore(new List<QuoteRecord> { A }, s, data, true, true, DateTime.Now, out rr) != null
                     && rr == null && System.IO.File.ReadAllText(History.FilePath) == histBefore);
                History.ResetBlock();

                var now = new DateTime(2026, 11, 10, 12, 0, 0);
                True("Restore replace + settings OK", History.Restore(new List<QuoteRecord> { A }, s, data, true, true, now, out rr) == null && rr != null);
                string[] hs = System.IO.Directory.GetFiles(dir, "history.before-restore-20261110-120000*.json");
                string[] ss = System.IO.Directory.GetFiles(dir, "settings.before-restore-20261110-120000*.ini");
                True("Restore safety copies made", hs.Length == 1 && ss.Length == 1
                     && System.IO.File.ReadAllText(hs[0]) == histBefore && System.IO.File.ReadAllText(ss[0]) == iniBefore);
                string problem;
                var loaded = History.Load(out problem);
                True("Restore saved the backup's history", problem == null && J(Ids(loaded)) == "C");
                var ls = AppSettings.Load();
                True("Restore saved the backup's settings + used code", ls.FriendsMarkupPct == 55 && ls.UsedInvoiceNos.Contains("2026-ZZZZ"));
                // a second restore in the same second: -2 names
                True("Restore again OK", History.Restore(loaded, ls, data, false, false, now, out rr) == null && rr.Same == 1);
                True("Restore safety copy -2", System.IO.Directory.GetFiles(dir, "history.before-restore-20261110-120000-2.json").Length == 1);
            }
            finally
            {
                History.ResetBlock();
                AppSettings.ClearTestFolder();
            }
        }

        // ---------- quote export (idea 5): breakdown, settings helpers, history helpers ----------
        static string Line(QuoteBreakdown b, string key, int index)
        {
            foreach (var l in b.Lines) if (l.Key == key && (index < 0 || l.Index == index)) return l.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return "none";
        }

        static string Parts(QuoteLine l)
        {
            if (l == null || l.Parts == null) return "NULL";
            var p = new List<string>();
            foreach (var x in l.Parts) p.Add(x.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join("|", p.ToArray());
        }

        static QuoteLine Find(QuoteBreakdown b, string key)
        {
            foreach (var l in b.Lines) if (l.Key == key) return l;
            return null;
        }

        // the øre add up (lines -> total, wear parts -> wear line), the total is the shown price, each line within 1 øre
        static void Adds(string name, QuoteBreakdown b)
        {
            long sum = 0;
            bool near = true;
            foreach (var l in b.Lines)
            {
                sum += l.Ore;
                if (Math.Abs(l.Ore - l.Exact * 100) > 1.0001) near = false;
                if (l.Parts != null)
                {
                    long ps = 0;
                    foreach (var p in l.Parts) { ps += p.Ore; if (Math.Abs(p.Ore - p.Exact * 100) > 1.0001) near = false; }
                    True(name + ": wear parts sum to the wear line", ps == l.Ore);
                }
            }
            double price = b.Friends ? b.Result.FriendsPrice : b.Result.OthersPrice;
            True(name + ": lines sum to total " + b.Total.ToString("0.00"), sum == b.TotalOre);
            True(name + ": total = shown price " + Num.Kr(price), Num.Kr(b.Total) == Num.Kr(price));
            True(name + ": each line within 1 øre of exact", near);
        }

        // ---------- Pricing.MatchSavedTotal (quote page shows the saved price) ----------
        static QuoteLine ML(string key, double exact)
        {
            return new QuoteLine { Key = key, Exact = exact, Ore = (long)Math.Round(exact * 100) };
        }

        static QuoteBreakdown MB(long totalOre, params QuoteLine[] lines)
        {
            return new QuoteBreakdown { Lines = new List<QuoteLine>(lines), TotalOre = totalOre, Price = totalOre / 100.0 };
        }

        static void MatchSavedTotalTests()
        {
            // 10 + 2 + 8 = 20 kr., saved 30 -> k = 30 / 20 = 1,5: 15 / 3 / 12 kr.
            var b = MB(2000, ML("material", 10), ML("power", 2), ML("work", 8));
            Pricing.MatchSavedTotal(b, 30);
            True("MatchSavedTotal 20 -> 30: 1500/300/1200 øre, total 3000, Price 30",
                 b.Lines[0].Ore == 1500 && b.Lines[1].Ore == 300 && b.Lines[2].Ore == 1200 && b.TotalOre == 3000 && b.Price == 30);
            Eq("MatchSavedTotal 20 -> 30: material Exact 10 × 1,5", 15, b.Lines[0].Exact);

            // wear parts scale too: 6 + 4 (3 + 1) = 10 kr., saved 12,50 -> k = 1,25: 7,50 / 5,00 (3,75 + 1,25)
            var wl = ML("wear", 4);
            wl.Parts = new List<QuoteLine> { ML("wear.printer", 3), ML("wear.plate", 1) };
            b = MB(1000, ML("material", 6), wl);
            Pricing.MatchSavedTotal(b, 12.5);
            True("MatchSavedTotal wear parts: 750/500, parts 375/125",
                 b.Lines[0].Ore == 750 && wl.Ore == 500 && wl.Parts[0].Ore == 375 && wl.Parts[1].Ore == 125 && b.TotalOre == 1250);

            // 1 + 1 + 1 = 3 kr., saved 10 -> 3,3333 each: 333 × 3 = 999, the extra øre goes to the first line (tie)
            b = MB(300, ML("material", 1), ML("power", 1), ML("work", 1));
            Pricing.MatchSavedTotal(b, 10);
            True("MatchSavedTotal 3 -> 10: 334/333/333", b.Lines[0].Ore == 334 && b.Lines[1].Ore == 333 && b.Lines[2].Ore == 333 && b.TotalOre == 1000);

            // a negative line counts as 0: 4 + (-1) -> sum 4, saved 8 -> k = 2: 8 / 0
            b = MB(400, ML("material", 4), ML("rounding", -1));
            Pricing.MatchSavedTotal(b, 8);
            True("MatchSavedTotal negative line -> 0: 800/0", b.Lines[0].Ore == 800 && b.Lines[1].Ore == 0 && b.Lines[1].Exact == 0 && b.TotalOre == 800);

            // nothing changes: same shown price (20,004 -> 2000 øre), saved 0 / -5, lines summing to 0, null breakdown / lines
            b = MB(2000, ML("material", 10), ML("power", 2), ML("work", 8));
            Pricing.MatchSavedTotal(b, 20.004);
            Pricing.MatchSavedTotal(b, 0);
            Pricing.MatchSavedTotal(b, -5);
            True("MatchSavedTotal same price / 0 / negative: unchanged",
                 b.TotalOre == 2000 && b.Price == 20 && b.Lines[0].Ore == 1000 && b.Lines[0].Exact == 10 && b.Lines[2].Ore == 800);
            b = MB(0, ML("material", 0), ML("power", 0));
            Pricing.MatchSavedTotal(b, 50);
            True("MatchSavedTotal lines sum 0: unchanged", b.TotalOre == 0 && b.Lines[0].Ore == 0);
            bool threw = false;
            try { Pricing.MatchSavedTotal(null, 10); Pricing.MatchSavedTotal(new QuoteBreakdown(), 10); }
            catch (Exception) { threw = true; }
            True("MatchSavedTotal null breakdown / null lines: no error", !threw);

            // a real breakdown (Example(), default settings, customer, with the wear split) moved to its price + 7,77
            var s = new AppSettings();
            var real = Pricing.Breakdown(Example(), s, false, Wear.Calculate(Wear.Nozzles[0], false, "PLA", Wear.Plates[0]));
            True("MatchSavedTotal real breakdown has a wear split", Find(real, "wear") != null && Find(real, "wear").Parts != null);
            long before = real.TotalOre;
            Pricing.MatchSavedTotal(real, real.Price);
            True("MatchSavedTotal real breakdown, own price: unchanged", real.TotalOre == before);
            double saved = before / 100.0 + 7.77;
            Pricing.MatchSavedTotal(real, saved);
            long sum = 0;
            bool parts = true;
            foreach (var l in real.Lines)
            {
                sum += l.Ore;
                if (l.Parts != null) { long ps = 0; foreach (var p in l.Parts) ps += p.Ore; if (ps != l.Ore) parts = false; }
            }
            True("MatchSavedTotal real breakdown + 7,77: total = " + Num.Kr(saved) + ", lines and wear parts add up",
                 real.TotalOre == before + 777 && sum == real.TotalOre && parts && real.Price == saved);
        }

        static void QuoteTests()
        {
            var defWear = Wear.Calculate(Wear.Nozzles[0], false, "PLA", Wear.Plates[0]);
            // 4-colour Benchy (see Main): customer 6,608 + 11,198 + 3,456 + 5,908 + power 3,91158 + wear 18,3255 +
            // work 31,08158 x 0,75 = 23,31119 = 72,71827 -> 72,72. Floors 660+1119+345+590+391+1832+2331 = 7268 øre,
            // 4 øre left -> remainders ,8 ,8 ,8 (lines 1, 2, 4) then ,6 (line 3): 6,61 11,20 3,46 5,91 | 3,91 18,32 23,31
            var four = new List<FilamentLine> {
                new FilamentLine { Grams = 33.04, PricePerKg = 200 }, new FilamentLine { Grams = 55.99, PricePerKg = 200 },
                new FilamentLine { Grams = 17.28, PricePerKg = 200 }, new FilamentLine { Grams = 29.54, PricePerKg = 200 } };
            var bin = new QuoteInput { Hours = 10, Minutes = 43, FilamentGrams = 135.85, FilamentDkkPerKg = 200,
                ElectricityDkkPerKwh = 3.65, Filaments = four };
            var bc = Pricing.Breakdown(bin, new AppSettings(), false, defWear);
            Adds("Benchy customer", bc);
            Eq("Benchy customer total 72,72", 72.72, bc.Total);
            True("Benchy customer colours 6,61 11,20 3,46 5,91", Line(bc, "material", 0) == "6.61" && Line(bc, "material", 1) == "11.20"
                 && Line(bc, "material", 2) == "3.46" && Line(bc, "material", 3) == "5.91");
            True("Benchy customer power 3,91, wear 18,32, work 23,31", Line(bc, "power", -1) == "3.91" && Line(bc, "wear", -1) == "18.32"
                 && Line(bc, "work", -1) == "23.31");
            True("Benchy: no failed/payback/minimum/rounding lines", Line(bc, "failed", -1) == "none" && Line(bc, "payback", -1) == "none"
                 && Line(bc, "minimum", -1) == "none" && Line(bc, "rounding", -1) == "none");
            var bw = Find(bc, "wear");
            // split 0,69967 / 0,4 / 0,278 / 0,33333 of 1,711, scaled to 1,71: x 10,716667 h -> 7,49372 4,28416 2,97749 3,57013
            // floors 749+428+297+357 = 1831, 1 øre left -> largest remainder ,749 (hotend): 7,49 4,28 2,98 3,57 = 18,32
            True("Benchy wear parts 7,49 4,28 2,98 3,57", Parts(bw) == "7.49|4.28|2.98|3.57");
            True("Benchy wear parts keys", bw.Parts[0].Key == "wear.printer" && bw.Parts[1].Key == "wear.parts" &&
                 bw.Parts[2].Key == "wear.hotend" && bw.Parts[3].Key == "wear.plate");
            Eq("Benchy wear hours 10,7167", 10.7167, bw.Quantity);
            Eq("Benchy wear rate 1,71", 1.71, bw.UnitPrice);
            Eq("Benchy wear parts rates sum to 1,71", 1.71, bw.Parts[0].UnitPrice + bw.Parts[1].UnitPrice + bw.Parts[2].UnitPrice + bw.Parts[3].UnitPrice);
            var m0 = bc.Lines[0];
            True("material line details: index 0, 33,04 g, 200 kr./kg", m0.Key == "material" && m0.Index == 0 &&
                 Math.Abs(m0.Quantity - 33.04) < 1e-9 && m0.UnitPrice == 200);
            var pw = Find(bc, "power");
            Eq("power line kWh 1,0717", 1.0717, pw.Quantity);
            Eq("power line kr./kWh 3,65", 3.65, pw.UnitPrice);
            Eq("work line markup 75 %", 75, Find(bc, "work").Quantity);
            // friends: work 31,08158 x 0,25 = 7,770395; 57,17748 -> 57,18; floors 5714 + 4 øre (same ,8 ,8 ,8 ,6): work 7,77
            var bf = Pricing.Breakdown(bin, new AppSettings(), true, defWear);
            Adds("Benchy friends", bf);
            Eq("Benchy friends total 57,18", 57.18, bf.Total);
            True("Benchy friends work 7,77, wear 18,32", Line(bf, "work", -1) == "7.77" && Line(bf, "wear", -1) == "18.32");
            Eq("Benchy friends markup 25 %", 25, Find(bf, "work").Quantity);

            // AutoWearRate off: one wear line, no parts (even with a breakdown given)
            var manual = new AppSettings { AutoWearRate = 0, MachineRatePerHour = 2.5 };
            var bm = Pricing.Breakdown(bin, manual, false, defWear);
            Adds("AutoWearRate off", bm);
            True("AutoWearRate off: no wear parts", Find(bm, "wear") != null && Find(bm, "wear").Parts == null);
            True("null wear breakdown: no parts", Find(Pricing.Breakdown(bin, new AppSettings(), false, null), "wear").Parts == null);
            True("no wear rate: no wear line", Find(Pricing.Breakdown(bin, new AppSettings { MachineRatePerHour = 0 }, false, defWear), "wear") == null);

            // one filament (Example: 120 g x 180, 5 h 30 m, 2,00 kr./kWh), minimum 50, no wear:
            // material 21,60 + power 1,10 + work 22,70 x 0,75 = 17,025 = 39,725 < 50 -> minimum top-up 10,275; total 50,00
            var smin = new AppSettings { MinimumPrice = 50, MachineRatePerHour = 0, FailedPrintPct = 0 };
            var bmin = Pricing.Breakdown(Example(), smin, false, defWear);
            Adds("minimum price", bmin);
            Eq("minimum total 50,00", 50, bmin.Total);
            True("minimum: one material line 21,60 (120 g, 180 kr./kg), power 1,10", Line(bmin, "material", 0) == "21.60" &&
                 bmin.Lines[0].Quantity == 120 && bmin.Lines[0].UnitPrice == 180 && Line(bmin, "power", -1) == "1.10");
            Eq("minimum: work + top-up = 50 - 22,70 = 27,30", 27.30, Find(bmin, "work").Amount + Find(bmin, "minimum").Amount);
            Eq("minimum line keeps the minimum price", 50, Find(bmin, "minimum").Quantity);
            var bminF = Pricing.Breakdown(Example(), smin, true, defWear);
            Adds("minimum price, friends", bminF);
            True("minimum: no top-up for friends (28,38)", Find(bminF, "minimum") == null && Math.Abs(bminF.Total - 28.38) < 1e-9);

            // rounding: wear 5 kr./h (manual), minimum 50, round to 5: customer 21,60 + 1,10 + 27,50 + 17,025 = 67,225 -> 70
            var sr = new AppSettings { AutoWearRate = 0, MachineRatePerHour = 5, MinimumPrice = 50, RoundTo = 5, FailedPrintPct = 0 };
            var br = Pricing.Breakdown(Example(), sr, false, defWear);
            Adds("rounding customer", br);
            Eq("rounding total 70", 70, br.Total);
            True("rounding: wear 27,50 (5,5 h x 5), no minimum line", Line(br, "wear", -1) == "27.50" && Find(br, "minimum") == null);
            Eq("rounding: work + rounding = 70 - 50,20 = 19,80", 19.80, Find(br, "work").Amount + Find(br, "rounding").Amount);
            var brf = Pricing.Breakdown(Example(), sr, true, defWear);
            Adds("rounding friends", brf);
            // friends 22,70 x 1,25 = 28,375 + 27,50 = 55,875 -> 60; rounding 4,125
            Eq("rounding friends total 60", 60, brf.Total);
            Eq("rounding friends: work + rounding = 60 - 50,20 = 9,80", 9.80, Find(brf, "work").Amount + Find(brf, "rounding").Amount);
            var exact5 = Pricing.Breakdown(new QuoteInput { Hours = 1, FilamentGrams = 100, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 0 },
                new AppSettings { AutoWearRate = 0, MachineRatePerHour = 0, OthersMarkupPct = 0, RoundTo = 5, MinimumPrice = 0 }, false, null);
            // 100 g x 200 = 20,00 exactly on a 5 kr. step: no rounding line
            True("rounding 0 left out (20,00)", Find(exact5, "rounding") == null && exact5.Total == 20);

            // failed prints 10 % + payback (customers only) on the Example with HistSettings (wear 2 kr./h manual):
            // 21,60 + 1,10 + failed 2,27 + wear 11,00 + payback 5,675 + work 24,97 x 0,75 = 18,7275 = 60,3725 -> 60,37
            // floors 2160+110+227+1100+567+1872 = 6036, 1 øre -> largest remainder ,75 (work): payback 5,67, work 18,73
            var ps = PaySettings(2); ps.AutoWearRate = 0;
            var bp = Pricing.Breakdown(Owing(33.70), ps, false, defWear);
            Adds("payback customer", bp);
            Eq("payback customer total 60,37", 60.37, bp.Total);
            True("payback customer: failed 2,27, wear 11,00, payback 5,67, work 18,73", Line(bp, "failed", -1) == "2.27" &&
                 Line(bp, "wear", -1) == "11.00" && Line(bp, "payback", -1) == "5.67" && Line(bp, "work", -1) == "18.73");
            Eq("failed line keeps 10 %", 10, Find(bp, "failed").Quantity);
            Eq("payback line keeps 25 %", 25, Find(bp, "payback").Quantity);
            // friends, customers-only payback: 42,2125 -> 42,21, no payback line, work 24,97 x 0,25 = 6,2425 -> 6,24
            var bpf = Pricing.Breakdown(Owing(33.70), ps, true, defWear);
            Adds("payback customers only, friends", bpf);
            True("friends: no payback line, work 6,24, total 42,21", Find(bpf, "payback") == null && Line(bpf, "work", -1) == "6.24"
                 && Math.Abs(bpf.Total - 42.21) < 1e-9);
            var pb = PaySettings(1); pb.AutoWearRate = 0;
            var bpb = Pricing.Breakdown(Owing(33.70), pb, true, defWear);
            Adds("payback both, friends", bpb);
            // 42,2125 + 5,675 = 47,8875 -> 47,89
            True("payback both: friends has payback, total 47,89", Find(bpb, "payback") != null && Math.Abs(bpb.Total - 47.89) < 1e-9);

            // awkward: 7 colours, odd grams/prices, odd hours, failed 16 %, payback both, minimum + rounding 0,5, auto wear
            var seven = new List<FilamentLine>();
            double[] g7 = { 12.345, 0.333, 7.777, 101.01, 3.3, 0.005, 44.444 }, p7 = { 199.95, 250, 180, 219.5, 333.33, 180, 123.45 };
            for (int i = 0; i < 7; i++) seven.Add(new FilamentLine { Grams = g7[i], PricePerKg = p7[i] });
            var awkIn = new QuoteInput { Hours = 3, Minutes = 7, ElectricityDkkPerKwh = 2.71, Filaments = seven, PaybackDebt = 19.99 };
            var wearCf = Wear.Calculate(Wear.FindNozzle("0.4 hardened"), true, "PLA-CF", Wear.FindPlate("Textured PEI Plate"));
            foreach (double round in new double[] { 0, 0.5, 1, 10 })
                foreach (double min in new double[] { 0, 200, 999 })
                    foreach (bool fr in new bool[] { false, true })
                    {
                        var sa = new AppSettings { FailedPrintPct = 16, FailedPayback = 1, FailedPaybackPct = 33, FailedPaybackWho = 1,
                            MinimumPrice = min, RoundTo = round, MachineRatePerHour = wearCf.Total, FriendsMarkupPct = 17.5, OthersMarkupPct = 83.3 };
                        string nm = "awkward round " + round + " min " + min + (fr ? " friends" : " customer");
                        var ba = Pricing.Breakdown(awkIn, sa, fr, wearCf);
                        Adds(nm, ba);
                        int mats = 0;
                        foreach (var l in ba.Lines) if (l.Key == "material") mats++;
                        True(nm + ": 7 material lines, wear split, payback", mats == 7 && Find(ba, "wear").Parts != null && Find(ba, "payback") != null);
                        True(nm + ": minimum line only when hit", (Find(ba, "minimum") != null) == (!fr && min >= 200));
                    }
            // many tiny amounts: 9 colours of 0,004 g -> all 0,00 but total still exact
            var tiny = new List<FilamentLine>();
            for (int i = 0; i < 9; i++) tiny.Add(new FilamentLine { Grams = 0.4, PricePerKg = 1.37 });
            Adds("9 tiny colours", Pricing.Breakdown(new QuoteInput { Minutes = 1, ElectricityDkkPerKwh = 1.11, Filaments = tiny },
                new AppSettings(), false, defWear));
            var bz = Pricing.Breakdown(new QuoteInput(), new AppSettings(), false, defWear);
            Adds("zero job", bz);
            True("zero job: total 0, material/power/work lines", bz.TotalOre == 0 && Find(bz, "material") != null && Find(bz, "power") != null
                 && Find(bz, "work") != null && Find(bz, "wear") == null);
            True("ShownOre 57,175 -> 5718 (like N2)", Pricing.ShownOre(57.175) == 5718 && Pricing.ShownOre(0.004) == 0 && Pricing.ShownOre(72.71827) == 7272);
            MatchSavedTotalTests();

            // settings defaults, Clone, valid-until
            var d = new AppSettings();
            True("quote defaults: empty name, 14 days, customer, dark", d.QuoteName == "" && AppSettings.DefaultQuoteName == ""
                 && AppSettings.LegacyQuoteName == "Gettic" && d.QuoteValidDays == 14 &&
                 d.QuotePrice == "customer" && !d.QuoteFriends && d.QuoteFor == History.ForCustomer && d.QuoteDark == 1);
            var dc = new AppSettings { QuoteName = "X", QuoteValidDays = 3, QuotePrice = "friends", QuoteDark = 0 }.Clone();
            True("Clone keeps quote settings", dc.QuoteName == "X" && dc.QuoteValidDays == 3 && dc.QuotePrice == "friends" && dc.QuoteDark == 0);
            var made = new DateTime(2026, 11, 4, 23, 59, 0);
            True("valid until 4/11 23:59 + 14 = 18/11", d.QuoteValidUntil(made) == new DateTime(2026, 11, 18));
            True("valid until over month end: 20/12 + 14 = 3/1", AppSettings.ValidUntil(new DateTime(2026, 12, 20, 9, 0, 0), 14) == new DateTime(2027, 1, 3));
            True("valid until 0 / -3 / NaN days -> 1 day", AppSettings.ValidUntil(made, 0) == new DateTime(2026, 11, 5) &&
                 AppSettings.ValidUntil(made, -3) == new DateTime(2026, 11, 5) && AppSettings.ValidUntil(made, double.NaN) == new DateTime(2026, 11, 5));

            // history: quote number and For
            var t = new DateTime(2026, 11, 4, 14, 5, 33);
            var qr = HistRecord(t);
            True("QuoteNumber 261104-1405", History.QuoteNumber(qr) == "261104-1405" && History.QuoteNumber(t) == "261104-1405");
            True("QuoteNumber bad SavedAt -> empty", History.QuoteNumber(new QuoteRecord { SavedAt = "x" }) == "" && History.QuoteNumber((QuoteRecord)null) == "");
            var hl = new List<QuoteRecord>();
            var a1 = History.AddFor(hl, HistRecord(t), History.ForFriends);
            True("AddFor friends", hl.Count == 1 && a1.For == History.ForFriends);
            var a2 = History.AddFor(hl, HistRecord(t.AddMinutes(2)), History.ForCustomer);
            True("AddFor replacing a same quote sets the new For", hl.Count == 1 && a2.Id == a1.Id && hl[0].For == History.ForCustomer);
            True("AddFor odd value -> customer", History.AddFor(new List<QuoteRecord>(), HistRecord(t), 7).For == History.ForCustomer);
        }

        // ---------- invoices ----------
        // fake random source: returns the given indices one after the other (alphabet index: A=0 B=1 C=2 D=3 E=4 F=5 G=6 H=7
        // J=8 K=9 M=10 N=11 P=12 Q=13 R=14 ... '2'=22 '3'=23 '4'=24 '6'=25 '7'=26 '8'=27 '9'=28)
        static Func<int, int> Seq(params int[] v)
        {
            int i = 0;
            return delegate(int n) { return v[i++ % v.Length] % n; };
        }

        /// A fake random source that makes NextInvoiceNo draw these 5-digit codes in turn ("48213" -> pick(9) = 3, then 8, 2, 1, 3).
        static Func<int, int> Digits(params string[] codes)
        {
            var v = new List<int>();
            foreach (var c in codes)
                for (int i = 0; i < c.Length; i++) v.Add(c[i] - '0' - (i == 0 ? 1 : 0));
            return Seq(v.ToArray());
        }

        static bool CodeOk(string no, int year)
        {
            if (no == null || no.Length != 10 || !no.StartsWith(year.ToString() + "-")) return false;
            for (int i = 5; i < 10; i++) if (no[i] < '0' || no[i] > '9') return false;
            return no[5] != '0';
        }

        static void InvoiceTests()
        {
            var t = new DateTime(2026, 11, 4, 14, 5, 33);
            True("invoice code: 5 digits", History.InvoiceCodeLength == 5);
            // first digit = 1 + pick(9), others pick(10): Seq(0) -> "10000", Seq(8, 9, 9, 9, 9) -> "99999" (no leading 0)
            True("fake source lowest / highest: 2026-10000 / 2026-99999", History.NextInvoiceNo(null, 2026, "", Seq(0)) == "2026-10000"
                 && History.NextInvoiceNo(null, 2026, "", Seq(8, 9, 9, 9, 9)) == "2026-99999");
            // format with the real crypto source
            bool fmt = true; var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                string no = History.NextInvoiceNo(null, 2026, "");
                fmt = fmt && CodeOk(no, 2026);
                seen.Add(no);
            }
            True("200 crypto codes all look like 2026-NNNNN (first digit 1-9)", fmt);
            True("200 crypto codes are (nearly) all different", seen.Count >= 195);
            True("year in the code: 2027-", History.NextInvoiceNo(null, 2027, "").StartsWith("2027-"));
            True("fake source: 11234", History.NextInvoiceNo(null, 2026, "", Digits("11234")) == "2026-11234");
            True("fake source: 48213", History.NextInvoiceNo(null, 2026, "", Seq(3, 8, 2, 1, 3)) == "2026-48213");

            // unique against history and against the used list: the fake returns two taken codes first
            var hist = new List<QuoteRecord> { new QuoteRecord { InvoiceNo = "2026-11234" }, null, new QuoteRecord { InvoiceNo = "" } };
            string got = History.NextInvoiceNo(hist, 2026, "2026-56789,2025-XXXX", Digits("11234", "56789", "48213"));
            True("collision with history (11234) and used list (56789) -> 48213", got == "2026-48213");
            True("used list is trimmed", History.NextInvoiceNo(null, 2026, " 2026-11234 , ", Digits("11234", "56789")) == "2026-56789");
            // same code in another year is a different invoice number
            True("same digits in another year is free", History.NextInvoiceNo(hist, 2027, "", Digits("11234")) == "2027-11234");
            // old letter codes stay taken but never clash with digit codes
            True("old letter code in the used list: digit code still given", History.NextInvoiceNo(null, 2026, "2026-K7Q4", Digits("48213")) == "2026-48213");

            // MakeInvoice: new code, BilledAt, Printed, used list
            var list = new List<QuoteRecord>();
            var a = HistRecord(t);
            list.Add(a);
            var st = new AppSettings();
            True("UsedInvoiceNos default empty", st.UsedInvoiceNos == "");
            True("record without invoice: no number", a.InvoiceNo == "" && a.BilledAt == "" && !a.Printed);
            string n1 = History.MakeInvoice(list, a, t, st, Digits("11234"));
            True("MakeInvoice: 2026-11234, BilledAt, Printed", n1 == "2026-11234" && a.InvoiceNo == n1 &&
                 a.BilledAt == "2026-11-04 14:05:33" && a.Printed);
            True("code added to the used list", st.UsedInvoiceNos == "2026-11234");
            // a second invoice on the same record keeps code and date, the used list stays
            string again = History.MakeInvoice(list, a, t.AddDays(30), st, Digits("56789"));
            True("second invoice on the same record: same code and date", again == "2026-11234" && a.BilledAt == "2026-11-04 14:05:33" &&
                 st.UsedInvoiceNos == "2026-11234");
            // never reused: the record is deleted, its code still can't come back
            var b = HistRecord(t.AddHours(1)); b.Model = "Other";
            list.Clear(); list.Add(b);
            string n2 = History.MakeInvoice(list, b, t.AddDays(1), st, Digits("11234", "56789"));
            True("deleted record's code 11234 not given again -> 56789", n2 == "2026-56789" && st.UsedInvoiceNos == "2026-11234,2026-56789");
            // null settings: works, nothing remembered
            var c = HistRecord(t); c.Model = "C";
            True("MakeInvoice with null settings", History.MakeInvoice(list, c, t, null, Digits("48213")) == "2026-48213" && c.Printed);
            // a year change uses the new year
            var d2 = HistRecord(t); d2.Model = "D";
            True("MakeInvoice in 2027 -> 2027-...", History.MakeInvoice(list, d2, new DateTime(2027, 1, 2, 9, 0, 0), st, Digits("11234")) == "2027-11234");

            // issue #6: a wanted code (picked before the save dialog) is used while still free, else a fresh one
            var wl = new List<QuoteRecord> { new QuoteRecord { InvoiceNo = "2026-22222" } };
            var wst = new AppSettings { UsedInvoiceNos = "2026-33333" };
            True("InvoiceFree: free / on a record / in the used list / blank / comma", History.InvoiceFree(wl, wst.UsedInvoiceNos, " 2026-44444 ")
                 && !History.InvoiceFree(wl, wst.UsedInvoiceNos, "2026-22222") && !History.InvoiceFree(wl, wst.UsedInvoiceNos, "2026-33333")
                 && !History.InvoiceFree(wl, "", "  ") && !History.InvoiceFree(null, null, null) && !History.InvoiceFree(null, "", "2026-1,2"));
            var wa = HistRecord(t); wa.Customer = "Ole"; wl.Add(wa);
            True("wanted free code 2026-44444 used", History.MakeInvoice(wl, wa, t, wst, " 2026-44444 ") == "2026-44444" && wa.InvoiceNo == "2026-44444"
                 && wa.Printed && wa.BilledAt == "2026-11-04 14:05:33" && wa.BilledTo == "Ole" && wst.UsedInvoiceNos == "2026-33333,2026-44444");
            True("re-issue ignores the wanted code", History.MakeInvoice(wl, wa, t, wst, "2026-55555") == "2026-44444" && wst.UsedInvoiceNos == "2026-33333,2026-44444");
            var wb = HistRecord(t); wb.Model = "WB"; wl.Add(wb);
            string wbNo = History.MakeInvoice(wl, wb, t, wst, "2026-22222");   // taken by a record
            True("wanted code taken by a record -> a fresh one", CodeOk(wbNo, 2026) && wbNo != "2026-22222" && wbNo != "2026-33333" && wbNo != "2026-44444"
                 && wst.UsedInvoiceNos == "2026-33333,2026-44444," + wbNo);
            var wc = HistRecord(t); wc.Model = "WC"; wl.Add(wc);
            string wcNo = History.MakeInvoice(wl, wc, t, wst, "2026-33333");   // only in the used list (record deleted)
            True("wanted code in the used list -> a fresh one", CodeOk(wcNo, 2026) && wcNo != "2026-33333" && wcNo != wbNo);
            var wd = HistRecord(t); wd.Model = "WD";
            True("wanted null -> a fresh one", CodeOk(History.MakeInvoice(wl, wd, t, null, (string)null), 2026));
            // the group version: one wanted code on all jobs; taken -> fresh; one job left -> single path with the code
            var g1 = BillJob(18, t.AddHours(-2)); var g2 = BillJob(14, t.AddHours(-1));
            var gs = new AppSettings { MinimumPrice = 50, RoundTo = 1, UsedInvoiceNos = "2026-33333" };
            True("group wanted free code used on both", History.MakeInvoice(wl, new[] { g1, g2 }, t, gs, false, "2026-66666") == "2026-66666"
                 && g1.InvoiceNo == "2026-66666" && g2.InvoiceNo == "2026-66666" && gs.UsedInvoiceNos == "2026-33333,2026-66666"
                 && Pricing.ShownOre(g1.OthersPrice) + Pricing.ShownOre(g2.OthersPrice) == 5000);
            var g3 = BillJob(18, t.AddHours(-2)); var g4 = BillJob(14, t.AddHours(-1));
            string gNo = History.MakeInvoice(wl, new[] { g3, g4 }, t, gs, false, "2026-33333");
            True("group wanted code taken -> a fresh one on both", CodeOk(gNo, 2026) && gNo != "2026-33333" && g3.InvoiceNo == gNo && g4.InvoiceNo == gNo);
            var g5 = BillJob(18, t); var g6 = BillJob(14, t); History.MarkFailed(g6, true, t);
            True("group with one job left: single invoice with the wanted code", History.MakeInvoice(wl, new[] { g5, g6 }, t, gs, false, "2026-77777") == "2026-77777"
                 && g5.OthersPrice == 50 && g5.InvoiceTopUp == 0 && g6.InvoiceNo == "");

            // used list: last 2000 kept
            string used = "";
            for (int i = 0; i < 2005; i++) used = History.AddUsedInvoiceNo(used, "2026-N" + i);
            string[] up = used.Split(',');
            True("used list keeps the last 2000", up.Length == 2000 && up[0] == "2026-N5" && up[1999] == "2026-N2004");
            True("AddUsedInvoiceNo ignores empty", History.AddUsedInvoiceNo("a", "") == "a" && History.AddUsedInvoiceNo(null, "x") == "x");

            // Add (same-quote replace) keeps code and date
            var al = new List<QuoteRecord>();
            var first = History.Add(al, HistRecord(t));
            History.MakeInvoice(al, first, t, null, Digits("11234"));
            var second = History.Add(al, HistRecord(t.AddMinutes(2)));
            True("Add after an invoice: invoiced record untouched, new one has no invoice", al.Count == 2 && al[0] == second && second != first &&
                 (second.InvoiceNo ?? "") == "" && (second.BilledAt ?? "") == "" && !second.Printed &&
                 first.InvoiceNo == "2026-11234" && first.BilledAt == "2026-11-04 14:05:33" && first.Printed);

            // JSON round trip, and old files without the keys read ""
            var back = History.ParseJson(History.ToJson(new List<QuoteRecord> { first }));
            True("JSON round trip keeps invoice fields", back[0].InvoiceNo == "2026-11234" && back[0].BilledAt == "2026-11-04 14:05:33" && back[0].Printed);
            var old = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"x\",\"Model\":\"m\"}]}");
            True("old file: InvoiceNo and BilledAt are empty", old[0].InvoiceNo == "" && old[0].BilledAt == "");

            // invoice snapshot: a new number saves due date, MobilePay, bank and "from" name
            var ss = new AppSettings { InvoiceDueDays = 14, PayMobilePay = "12 34 56 78", PayBank = "Reg 1234 konto 0012345678", QuoteName = "Gettic 3D" };
            var sl = new List<QuoteRecord>();
            var sa = HistRecord(t); sl.Add(sa);
            True("not invoiced: no snapshot", sa.InvoiceDue == "" && sa.InvoicePayMobilePay == "" && sa.InvoicePayBank == "" && sa.InvoiceFromName == "");
            History.MakeInvoice(sl, sa, t, ss, Digits("11234"));
            // 4/11/2026 14:05 -> date 4/11 + 14 days = 18/11/2026
            True("snapshot: due 2026-11-18, MobilePay, bank, from name", sa.InvoiceDue == "2026-11-18" &&
                 sa.InvoicePayMobilePay == "12 34 56 78" && sa.InvoicePayBank == "Reg 1234 konto 0012345678" && sa.InvoiceFromName == "Gettic 3D");
            // settings change later: re-invoice keeps the snapshot, InvoiceInfo shows it
            ss.InvoiceDueDays = 30; ss.PayMobilePay = ""; ss.PayBank = "Other bank"; ss.QuoteName = "New name";
            History.MakeInvoice(sl, sa, t.AddDays(10), ss, Digits("56789"));
            True("re-invoice keeps the snapshot", sa.InvoiceNo == "2026-11234" && sa.InvoiceDue == "2026-11-18" &&
                 sa.InvoicePayMobilePay == "12 34 56 78" && sa.InvoicePayBank == "Reg 1234 konto 0012345678" && sa.InvoiceFromName == "Gettic 3D");
            var ia = History.InvoiceInfo(sa, ss, t.AddDays(10));
            True("InvoiceInfo uses the snapshot, not today's settings", ia.FromSnapshot && ia.Due == new DateTime(2026, 11, 18) &&
                 ia.MobilePay == "12 34 56 78" && ia.Bank == "Reg 1234 konto 0012345678" && ia.FromName == "Gettic 3D");
            // a snapshot with empty payment fields stays empty (not filled from today's settings)
            var se = new AppSettings { PayMobilePay = "", PayBank = "", QuoteName = "" };
            var sb2 = HistRecord(t.AddHours(2)); sb2.Model = "E"; sl.Add(sb2);
            History.MakeInvoice(sl, sb2, t, se, Digits("48213"));
            var ib = History.InvoiceInfo(sb2, ss, t);
            True("empty snapshot fields stay empty", ib.FromSnapshot && ib.MobilePay == "" && ib.Bank == "" && ib.FromName == "" &&
                 ib.Due == new DateTime(2026, 11, 18));
            // old invoice without a snapshot: today's settings; due = BilledAt 4/11 + 30 days = 4/12/2026
            var so = HistRecord(t); so.InvoiceNo = "2026-OLD1"; so.BilledAt = "2026-11-04 14:05:33";
            var io = History.InvoiceInfo(so, ss, t.AddDays(40));
            True("old invoice: today's settings, due from BilledAt", !io.FromSnapshot && io.Due == new DateTime(2026, 12, 4) &&
                 io.MobilePay == "" && io.Bank == "Other bank" && io.FromName == "New name");
            // not invoiced yet (preview): due from today 10/11 + 30 = 10/12/2026
            var inew = History.InvoiceInfo(HistRecord(t), ss, new DateTime(2026, 11, 10, 9, 0, 0));
            True("not invoiced: due from today", !inew.FromSnapshot && inew.Due == new DateTime(2026, 12, 10));
            // bad snapshot date -> today's settings; null record / settings never crash (defaults: 14 days, no name)
            var sbad = HistRecord(t); sbad.InvoiceNo = "2026-BAD1"; sbad.BilledAt = "2026-11-04 14:05:33"; sbad.InvoiceDue = "18/11/2026";
            True("bad snapshot date -> settings", !History.InvoiceInfo(sbad, ss, t).FromSnapshot);
            var inull = History.InvoiceInfo(null, null, t);
            True("null record + settings -> defaults", inull != null && inull.Due == new DateTime(2026, 11, 18) && inull.FromName == "");
            // null settings in MakeInvoice: no snapshot
            var sn = HistRecord(t); sn.Model = "N";
            History.MakeInvoice(sl, sn, t, null, Digits("11234", "56789", "48213"));
            True("MakeInvoice with null settings: no snapshot", sn.InvoiceNo != "" && sn.InvoiceDue == "");
            // Add (same-quote replace) keeps the snapshot
            var al2 = new List<QuoteRecord>();
            var f2 = History.Add(al2, HistRecord(t));
            History.MakeInvoice(al2, f2, t, new AppSettings { PayMobilePay = "MP", PayBank = "BK", QuoteName = "Q" }, Digits("11234"));
            var s2b = History.Add(al2, HistRecord(t.AddMinutes(2)));
            True("Add after an invoice: snapshot stays on the invoiced record, new one has none", al2.Count == 2 && al2[0] == s2b &&
                 s2b.InvoiceDue == "" && s2b.InvoicePayMobilePay == "" && s2b.InvoicePayBank == "" && s2b.InvoiceFromName == "" &&
                 f2.InvoiceDue == "2026-11-18" && f2.InvoicePayMobilePay == "MP" && f2.InvoicePayBank == "BK" && f2.InvoiceFromName == "Q");
            // JSON round trip; old file without the keys reads ""
            var sj = History.ParseJson(History.ToJson(new List<QuoteRecord> { f2 }));
            True("JSON round trip keeps the snapshot", sj[0].InvoiceDue == "2026-11-18" && sj[0].InvoicePayMobilePay == "MP" &&
                 sj[0].InvoicePayBank == "BK" && sj[0].InvoiceFromName == "Q");
            True("old file: snapshot fields empty", old[0].InvoiceDue == "" && old[0].InvoicePayMobilePay == "" &&
                 old[0].InvoicePayBank == "" && old[0].InvoiceFromName == "");
            var jn = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"x\",\"InvoiceDue\":null,\"InvoicePayBank\":null}]}");
            True("null snapshot keys read as empty", jn[0].InvoiceDue == "" && jn[0].InvoicePayBank == "");

            // BilledTo (combined-bill Part B): the name the invoice was made out to
            var bl = new List<QuoteRecord>();
            var ba = HistRecord(t); bl.Add(ba);
            True("new record: BilledTo empty", ba.BilledTo == "" && ba.Customer == "");
            True("not invoiced: InvoiceInfo.BilledTo = Customer", History.InvoiceInfo(ba, ss, t).BilledTo == "");
            ba.Customer = "  Lene Hansen ";
            True("not invoiced: InvoiceInfo.BilledTo = trimmed Customer", History.InvoiceInfo(ba, ss, t).BilledTo == "Lene Hansen" && ba.BilledTo == "");
            History.MakeInvoice(bl, ba, t, new AppSettings(), Digits("11234"));
            True("new invoice: BilledTo = trimmed Customer", ba.InvoiceNo == "2026-11234" && ba.BilledTo == "Lene Hansen");
            History.SetCustomer(new[] { ba }, "Lene H. Jensen");
            History.MakeInvoice(bl, ba, t.AddDays(5), ss, Digits("56789"));
            True("re-issue keeps BilledTo after Customer changed", ba.InvoiceNo == "2026-11234" && ba.BilledTo == "Lene Hansen" && ba.Customer == "Lene H. Jensen");
            var bi = History.InvoiceInfo(ba, ss, t);
            True("InvoiceInfo.BilledTo = stored name, not Customer", bi.BilledTo == "Lene Hansen");
            // new invoice with no name: BilledTo "" stays ""
            var bz = HistRecord(t); bz.Model = "Z"; bl.Add(bz);
            History.MakeInvoice(bl, bz, t, null, Digits("48213"));
            True("new invoice without a name: BilledTo empty", bz.InvoiceNo == "2026-48213" && bz.BilledTo == "" && History.InvoiceInfo(bz, null, t).BilledTo == "");
            // old invoice (no BilledTo): InvoiceInfo falls back to Customer; first re-issue freezes it, later Customer changes don't
            var bo = HistRecord(t); bo.InvoiceNo = "2026-OLD1"; bo.BilledAt = "2026-11-04 14:05:33"; bo.Customer = "Ole";
            True("old invoice: InvoiceInfo.BilledTo = Customer", History.InvoiceInfo(bo, ss, t).BilledTo == "Ole" && bo.BilledTo == "");
            True("old invoice re-issued: same code", History.MakeInvoice(bl, bo, t.AddDays(3), ss, Digits("11234")) == "2026-OLD1");
            True("old invoice re-issued: BilledTo filled once", bo.BilledTo == "Ole" && bo.BilledAt == "2026-11-04 14:05:33");
            bo.Customer = "Ole Berg";
            History.MakeInvoice(bl, bo, t.AddDays(4), ss, Digits("11234"));
            True("old invoice: second re-issue keeps the frozen name", bo.BilledTo == "Ole" && History.InvoiceInfo(bo, ss, t).BilledTo == "Ole");
            True("InvoiceInfo null record: BilledTo empty", History.InvoiceInfo(null, null, t).BilledTo == "");
            // Add (same-quote replace) keeps BilledTo with the invoice
            var bal = new List<QuoteRecord>();
            var bf = History.Add(bal, HistRecord(t)); bf.Customer = "Anna";
            History.MakeInvoice(bal, bf, t, null, Digits("11234"));
            var bs2 = History.Add(bal, HistRecord(t.AddMinutes(2)));
            True("Add after an invoice: BilledTo stays on the invoiced record, new one has none", bal.Count == 2 && bal[0] == bs2 &&
                 (bs2.InvoiceNo ?? "") == "" && bs2.BilledTo == "" && bf.InvoiceNo == "2026-11234" && bf.BilledTo == "Anna");
            // JSON round trip; old file without the key; null key
            var bj = History.ParseJson(History.ToJson(new List<QuoteRecord> { ba, bo }));
            True("JSON round trip keeps BilledTo and Customer", bj[0].BilledTo == "Lene Hansen" && bj[0].Customer == "Lene H. Jensen" &&
                 bj[1].BilledTo == "Ole" && bj[1].Customer == "Ole Berg");
            True("JSON has the BilledTo key", History.ToJson(new List<QuoteRecord> { ba }).Contains("\"BilledTo\":\"Lene Hansen\""));
            True("old file: BilledTo empty", old[0].BilledTo == "");
            var bnull = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"x\",\"InvoiceNo\":\"2026-K7Q4\",\"Customer\":\"Ole\",\"BilledTo\":null}]}");
            True("null BilledTo reads as empty, InvoiceInfo uses Customer", bnull[0].BilledTo == "" && History.InvoiceInfo(bnull[0], null, t).BilledTo == "Ole");

            // SetCustomer: several records, trimmed, max 80, "" allowed, never touches BilledTo, returns changed count
            var c1 = HistRecord(t); var c2 = HistRecord(t); c2.Customer = "Mia"; var c3 = HistRecord(t);
            c3.InvoiceNo = "2026-C3C3"; c3.Customer = "Old"; c3.BilledTo = "Old";
            True("SetCustomer: 2 changed (null skipped, same name not counted), trimmed", History.SetCustomer(new List<QuoteRecord> { c1, null, c2, c3 }, "  Mia  ") == 2 &&
                 c1.Customer == "Mia" && c2.Customer == "Mia" && c3.Customer == "Mia");
            True("SetCustomer never touches BilledTo", c3.BilledTo == "Old" && c1.BilledTo == "" && History.InvoiceInfo(c3, null, t).BilledTo == "Old");
            True("SetCustomer same name again: 0 changed", History.SetCustomer(new[] { c1, c2 }, "Mia") == 0);
            True("SetCustomer null / blank -> \"\"", History.SetCustomer(new[] { c1 }, null) == 1 && c1.Customer == "" &&
                 History.SetCustomer(new[] { c2 }, "   ") == 1 && c2.Customer == "");
            True("SetCustomer null list -> 0", History.SetCustomer(null, "x") == 0);
            History.SetCustomer(new[] { c1 }, new string('a', 79) + " bcd");
            True("SetCustomer cuts to 80 (then trims the end)", c1.Customer == new string('a', 79) && History.MaxCustomer == 80);
            History.SetCustomer(new[] { c1 }, new string('b', 85));
            True("SetCustomer 85 chars -> 80", c1.Customer.Length == 80);

            // due date: 14 days default, time dropped; month and year ends
            var s = new AppSettings();
            True("due 4/11 14:05 + 14 days = 18/11", s.InvoiceDue(t) == new DateTime(2026, 11, 18));
            True("due 20/12 + 14 = 3/1", s.InvoiceDue(new DateTime(2026, 12, 20, 23, 0, 0)) == new DateTime(2027, 1, 3));
            s.InvoiceDueDays = 30;
            True("due 30 days: 4/11 -> 4/12", s.InvoiceDue(t) == new DateTime(2026, 12, 4));
            s.InvoiceDueDays = 0;
            True("bad days 0 -> 14", s.InvoiceDue(t) == new DateTime(2026, 11, 18));
            s.InvoiceDueDays = 400;
            True("bad days 400 -> 14", s.InvoiceDue(t) == new DateTime(2026, 11, 18));
            var sc = new AppSettings { InvoiceDueDays = 7, PayMobilePay = "1", PayBank = "2", UsedInvoiceNos = "2026-ABCD,2026-EFGH" }.Clone();
            True("Clone keeps invoice settings", sc.InvoiceDueDays == 7 && sc.PayMobilePay == "1" && sc.PayBank == "2" &&
                 sc.UsedInvoiceNos == "2026-ABCD,2026-EFGH");
        }

        // ---------- Printed tick, month summary, repeat prints (ideas 22, 23) ----------
        static void HistoryMonthTests()
        {
            var t = new DateTime(2026, 11, 3, 10, 0, 0);

            // Printed: new records false, old file without the key -> true, the key kept both ways
            True("Printed: FromQuote -> false", !HistRecord(t).Printed && !new QuoteRecord().Printed);
            True("RepeatOf default empty", new QuoteRecord().RepeatOf == "" && HistRecord(t).RepeatOf == "");
            var po = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\"},{\"Id\":\"b\",\"Printed\":false},"
                + "{\"Id\":\"c\",\"Printed\":true},{\"Id\":\"d\",\"RepeatOf\":null}]}");
            True("Printed old file: missing -> true, false/true kept", po != null && po.Count == 4 && po[0].Printed
                 && !po[1].Printed && po[2].Printed && po[3].Printed);
            True("RepeatOf old file / null -> empty", po != null && po[0].RepeatOf == "" && po[3].RepeatOf == "");
            var pr1 = HistRecord(t); var pr2 = HistRecord(t.AddHours(1)); pr2.Printed = true; pr2.RepeatOf = pr1.Id;
            string pj = History.ToJson(new List<QuoteRecord> { pr2, pr1 });
            var pb = History.ParseJson(pj);
            True("Printed/RepeatOf JSON round trip", pj.Contains("\"Printed\":true") && pj.Contains("\"Printed\":false")
                 && !pj.Contains("printedRead") && pb != null && pb.Count == 2 && pb[0].Printed && !pb[1].Printed
                 && pb[0].RepeatOf == pr1.Id && pb[1].RepeatOf == "");
            // Add replacing a same quote keeps the Printed tick
            var pl = new List<QuoteRecord>();
            History.Add(pl, HistRecord(t)); pl[0].Printed = true;
            History.Add(pl, HistRecord(t.AddMinutes(3)));
            True("Add replace keeps Printed", pl.Count == 1 && pl[0].Printed);

            // repeat print: never merged with its original, even 1 minute later with the same numbers
            var orig = HistRecord(t);
            var rep = HistRecord(t.AddMinutes(1)); rep.RepeatOf = orig.Id;
            True("SameQuote repeat vs original -> no", !History.SameQuote(orig, rep) && !History.SameQuote(rep, orig));
            var rl = new List<QuoteRecord>();
            History.Add(rl, orig);
            var kept = History.Add(rl, rep);
            True("Add repeat -> own entry, own Id", rl.Count == 2 && rl[0] == rep && kept.Id != orig.Id && rl[1].Id == orig.Id);
            var other = HistRecord(t.AddMinutes(2)); other.RepeatOf = "someone-else";
            True("SameQuote repeats of different records -> no", !History.SameQuote(rep, other));
            var again = HistRecord(t.AddMinutes(2)); again.RepeatOf = orig.Id;
            True("SameQuote same repeat saved again (copy + export) -> same", History.SameQuote(rep, again));

            // month summary. Example record (HistoryTests): material 21,60, power 1,10, wear 5,5 h x 2 = 11,
            // friends 42,2125, customer 54,6975; FailedCost 33,70. With payback (customers only) customer 60,3725.
            Eq("WearCost 5,5 h x 2 = 11", 11, History.WearCost(HistRecord(t)));
            Eq("WearCost 1 h 15 min x 4 = 5", 5, History.WearCost(new QuoteRecord { Hours = 1, Minutes = 15, MachineRatePerHour = 4 }));
            Eq("WearCost null 0", 0, History.WearCost(null));
            var r1 = HistRecord(t); r1.Printed = true;                                          // customer 54,6975
            var r2 = HistRecord(t.AddDays(1)); r2.Printed = true; r2.For = History.ForFriends;  // friends 42,2125
            var r3 = HistRecord(t.AddDays(2));                                                  // not printed: nothing
            var r4 = HistRecord(t.AddDays(3)); r4.Printed = true; History.MarkFailed(r4, true, t.AddDays(3));   // lost 33,70
            var ps = PaySettings(2);
            var r5 = History.FromQuote(Owing(33.70), Pricing.Calculate(Owing(33.70), ps), ps, "Benchy", "", t.AddDays(4));
            r5.Printed = true;                                                                  // customer 60,3725
            var r6 = HistRecord(new DateTime(2026, 12, 1, 0, 0, 0)); r6.Printed = true;         // December
            var r7 = HistRecord(new DateTime(2026, 10, 31, 23, 59, 59)); r7.Printed = true;     // October
            var r8 = HistRecord(t); r8.Printed = true; r8.SavedAt = "bad";                      // unreadable: no month
            var ml = new List<QuoteRecord> { r6, r5, r4, r3, r2, r1, r7, r8 };
            var nov = History.MonthSummary(ml, 2026, 11);
            True("Month Nov: 3 prints, 1 failed, 2 customer, 1 friends", nov.Prints == 3 && nov.FailedPrints == 1
                 && nov.CustomerCount == 2 && nov.FriendsCount == 1 && nov.Year == 2026 && nov.Month == 11);
            Eq("Month Nov hours 3 x 5,5 = 16,5", 16.5, nov.PrintHours);
            Eq("Month Nov grams 3 x 120 = 360", 360, nov.Grams);
            Eq("Month Nov customer 54,6975 + 60,3725 = 115,07", 115.07, nov.CustomerIncome);
            Eq("Month Nov friends 42,2125", 42.2125, nov.FriendsIncome);
            Eq("Month Nov income 115,07 + 42,2125 = 157,2825", 157.2825, nov.Income);
            Eq("Month Nov material 3 x 21,60 = 64,80", 64.80, nov.MaterialCost);
            Eq("Month Nov power 3 x 1,10 = 3,30", 3.30, nov.PowerCost);
            Eq("Month Nov wear 3 x 11 = 33", 33, nov.WearCost);
            Eq("Month Nov costs 64,80 + 3,30 + 33 = 101,10", 101.10, nov.Costs);
            Eq("Month Nov lost 33,70", 33.70, nov.Lost);
            Eq("Month Nov profit 157,2825 - 101,10 - 33,70 = 22,4825", 22.4825, nov.Profit);
            Eq("Month Nov profit % 22,4825 / 157,2825 = 14,2943", 14.2943, nov.ProfitPct);
            var dec = History.MonthSummary(ml, 2026, 12);
            True("Month Dec: 1 customer print", dec.Prints == 1 && dec.CustomerCount == 1 && dec.FailedPrints == 0);
            // 54,6975 - 33,70 = 20,9975; 20,9975 / 54,6975 = 38,3884 %
            Eq("Month Dec profit 54,6975 - 33,70 = 20,9975", 20.9975, dec.Profit);
            Eq("Month Dec profit % 38,3884", 38.3884, dec.ProfitPct);
            var empty = History.MonthSummary(ml, 2026, 9);
            True("Month empty: all 0", empty.Prints == 0 && empty.FailedPrints == 0 && empty.Income == 0 && empty.Costs == 0
                 && empty.Lost == 0 && empty.Profit == 0 && empty.ProfitPct == 0 && empty.PrintHours == 0 && empty.Grams == 0);
            True("Month null list: all 0", History.MonthSummary(null, 2026, 11).Prints == 0
                 && History.MonthSummary(null, 2026, 11).Profit == 0);
            // MonthSummaries (one pass, HistoryForm's All months cards) = MonthSummary of each month in Months order, exactly
            var sep = HistRecord(new DateTime(2026, 9, 2, 8, 0, 0));                            // only a not-printed quote
            var oct2 = HistRecord(new DateTime(2026, 10, 5, 8, 0, 0)); oct2.Printed = true; oct2.For = History.ForFriends;
            oct2.MaterialCost = 0.1; oct2.PowerCost = 0.2; oct2.FriendsPrice = 1.0 / 3; oct2.Minutes = 7;
            var ml2 = new List<QuoteRecord>(ml) { null, sep, oct2 };
            var monthList = History.Months(ml2);
            var sums = History.MonthSummaries(ml2);
            bool same = sums.Count == monthList.Count && sums.Count == 4;
            for (int i = 0; same && i < sums.Count; i++)
            {
                var a = sums[i];
                var b = History.MonthSummary(ml2, monthList[i].Year, monthList[i].Month);
                same = a.Year == b.Year && a.Month == b.Month && a.Prints == b.Prints && a.FailedPrints == b.FailedPrints
                    && a.PrintHours == b.PrintHours && a.Grams == b.Grams && a.CustomerIncome == b.CustomerIncome
                    && a.CustomerCount == b.CustomerCount && a.FriendsIncome == b.FriendsIncome && a.FriendsCount == b.FriendsCount
                    && a.Income == b.Income && a.MaterialCost == b.MaterialCost && a.PowerCost == b.PowerCost && a.WearCost == b.WearCost
                    && a.Costs == b.Costs && a.Lost == b.Lost && a.Profit == b.Profit && a.ProfitPct == b.ProfitPct;
            }
            True("MonthSummaries = MonthSummary per month (exact, newest first)", same);
            True("MonthSummaries null / empty", History.MonthSummaries(null).Count == 0 && History.MonthSummaries(new List<QuoteRecord>()).Count == 0);
            // only a failed print (not ticked Printed): lost 33,70, no income -> profit -33,70, % 0
            var f = HistRecord(t); History.MarkFailed(f, true, t);
            var fo = History.MonthSummary(new List<QuoteRecord> { f }, 2026, 11);
            True("Month only failed: 0 prints, 1 failed", fo.Prints == 0 && fo.FailedPrints == 1 && fo.Income == 0);
            Eq("Month only failed: profit -33,70", -33.70, fo.Profit);
            Eq("Month only failed: profit % 0 (no income)", 0, fo.ProfitPct);
            // payback for friends in both prices: friends 42,2125 + 5,675 = 47,8875 is income
            var pb2 = PaySettings(1);
            var rf = History.FromQuote(Owing(33.70), Pricing.Calculate(Owing(33.70), pb2), pb2, "Benchy", "", t);
            rf.Printed = true; rf.For = History.ForFriends;
            var fp = History.MonthSummary(new List<QuoteRecord> { rf }, 2026, 11);
            Eq("Month friends with payback: income 47,8875", 47.8875, fp.FriendsIncome);
            Eq("Month friends with payback: profit 47,8875 - 33,70 = 14,1875", 14.1875, fp.Profit);

            var months = History.Months(ml);
            True("Months: Dec, Nov, Oct (newest first, no doubles, bad skipped)", months.Count == 3
                 && months[0] == new DateTime(2026, 12, 1) && months[1] == new DateTime(2026, 11, 1)
                 && months[2] == new DateTime(2026, 10, 1));
            True("Months null/empty -> none", History.Months(null).Count == 0 && History.Months(new List<QuoteRecord>()).Count == 0);
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
            Eq("FailedPayback default 1 (on)", 1, d.FailedPayback);
            Eq("FailedPaybackPct default 10", 10, d.FailedPaybackPct);
            Eq("MinimumPrice default 50", 50, d.MinimumPrice);
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

        // ---------- several printers (ideas 7 + 24 + 11) ----------
        static QuoteRecord PrinterRec(string id, string saved, int h, int m, double material, double power, double rate,
                                      int forWho, double price, bool printed, bool failed)
        {
            return new QuoteRecord
            {
                Id = id + saved, PrinterId = id == null ? "" : id, SavedAt = saved, Hours = h, Minutes = m,
                MaterialCost = material, PowerCost = power, MachineRatePerHour = rate, For = forWho,
                FriendsPrice = price, OthersPrice = price, Printed = printed, Failed = failed
            };
        }

        static void PrinterTests()
        {
            // wear per model (stock 0.4 stainless, PLA, smooth PEI):
            // A1   2099/3000 = 0,6997 + 600/1500 = 0,40 + 139/500 = 0,278 + 200/600 = 0,3333 = 1,711 -> 1,71
            // A1M  1599/3000 = 0,533 + 500/1500 = 0,3333 + 0,278 + 200 x 0,7 / 600 = 0,2333 = 1,3777 -> 1,38
            // P1S  4199/3000 = 1,3997 + 800/1500 = 0,5333 + 0,278 + 0,3333 = 2,5443 -> 2,54
            Eq("DefaultRateFor A1", 1.71, Wear.DefaultRateFor("A1"));
            Eq("DefaultRateFor A1 mini", 1.38, Wear.DefaultRateFor("A1M"));
            Eq("DefaultRateFor P1S", 2.54, Wear.DefaultRateFor("P1S"));
            Eq("DefaultRateFor unknown = A1", 1.71, Wear.DefaultRateFor("X9"));
            // A1M textured: 0,533 + 0,3333 + 0,278 + 150 x 0,7 / 1000 = 0,105 -> 1,2497 -> 1,25
            Eq("wear A1 mini textured plate", 1.25, Wear.Calculate(Wear.FindModel("A1M"), Wear.FindNozzle("0.4 stainless"), false,
                "PLA", Wear.FindPlate("Textured PEI Plate")).Total);
            True("FindModel unknown/null -> A1", Wear.FindModel(null).Key == "A1" && Wear.FindModel("x").Key == "A1");

            // settings in memory
            var s = new AppSettings();
            True("new settings: one printer '1' = the fields", s.PrinterCount == 1 && s.ActivePrinter.Id == "1"
                 && s.ActivePrinter.Name == "Bambu Lab A1" && s.ActivePrinter.Model == "A1" && s.ActivePrinter.Watts == 100);
            var mini = s.AddPrinter("", "A1M");
            True("AddPrinter: id 2, model label, model watts", mini.Id == "2" && mini.Name == "Bambu Lab A1 mini"
                 && mini.Watts == 70 && mini.AutoWearRate == 1 && s.PrinterCount == 2 && s.ActivePrinterId == "1");
            Eq("AddPrinter A1 mini wear rate 1,38", 1.38, mini.MachineRatePerHour);
            s.PrinterWatts = 95;                          // edit printer 1 through the fields
            True("SelectPrinter 2", s.SelectPrinter("2") && s.ActivePrinterId == "2" && s.PrinterWatts == 70
                 && s.PrinterModel == "A1M" && s.PrinterName == "Bambu Lab A1 mini");
            True("printer 1 kept its edited watts", s.FindPrinter("1").Watts == 95);
            True("SelectPrinter unknown -> false, unchanged", !s.SelectPrinter("9") && s.ActivePrinterId == "2");
            var p2 = s.FindPrinter("2"); p2.Name = "  Mini  "; p2.Watts = 65;
            True("SetPrinter active -> fields", s.SetPrinter(p2) && s.PrinterName == "Mini" && s.PrinterWatts == 65);
            True("Printers are copies", s.Printers[1].Name == "Mini" && (s.Printers[1].Name = "x") == "x" && s.FindPrinter("2").Name == "Mini");
            var c = s.Clone(); c.AddPrinter("P", "P1S"); c.SelectPrinter("1");
            True("Clone: own printer list", c.PrinterCount == 3 && s.PrinterCount == 2 && s.ActivePrinterId == "2");
            var p1s = s.AddPrinter("Big one", "P1S");
            True("AddPrinter P1S id 3", p1s.Id == "3" && p1s.Watts == 130 && p1s.Name == "Big one");
            True("RemovePrinter 3", s.RemovePrinter("3") && s.PrinterCount == 2);
            True("ids never reused", s.AddPrinter("", "A1").Id == "4");
            s.RemovePrinter("4");
            True("RemovePrinter active 2 -> printer 1 active", s.RemovePrinter("2") && s.ActivePrinterId == "1" && s.PrinterWatts == 95);
            True("RemovePrinter last -> false", !s.RemovePrinter("1") && s.PrinterCount == 1);

            // settings file (bin\test-printers, never %APPDATA%)
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-printers");
            string file = System.IO.Path.Combine(dir, "settings.ini");
            try
            {
                True("UseFolder test-printers", AppSettings.UseFolder(dir));
                System.IO.Directory.CreateDirectory(dir);
                // an old file (v1.8.0 keys only) -> one printer "1" with exactly its values
                System.IO.File.WriteAllText(file, "SettingsVersion=2\r\nPrinterWatts=88\r\nMachineRatePerHour=1.5\r\nAutoWearRate=0\r\n"
                    + "Nozzle=0.4 hardened\r\nBedType=Textured PEI Plate\r\nProcessPreset=0.20mm Standard\r\nPrinterName=My A1\r\n");
                var old = AppSettings.Load();
                var op = old.ActivePrinter;
                True("old file -> one printer '1', active", old.PrinterCount == 1 && old.ActivePrinterId == "1" && op.Id == "1");
                True("old file -> values kept", op.Name == "My A1" && op.Model == "A1" && op.Watts == 88 && op.MachineRatePerHour == 1.5
                     && op.AutoWearRate == 0 && op.Nozzle == "0.4 hardened" && op.BedType == "Textured PEI Plate"
                     && op.ProcessPreset == "0.20mm Standard" && old.PrinterWatts == 88);
                // round trip: 2 printers, the second active
                var mp = old.AddPrinter("Mini", "A1M");
                mp.Watts = 66; mp.Nozzle = "0.2 stainless"; old.SetPrinter(mp);
                old.SelectPrinter(mp.Id);
                old.Save();
                string text = System.IO.File.ReadAllText(file);
                True("file: flat keys = printer 1, Printer.2.* lines", text.Contains("PrinterWatts=88\r\n") && text.Contains("PrinterName=My A1\r\n")
                     && text.Contains("PrinterId=1\r\n") && text.Contains("Printer.2.PrinterWatts=66\r\n")
                     && text.Contains("Printer.2.PrinterModel=A1M\r\n") && text.Contains("ActivePrinter=2\r\n"));
                var back = AppSettings.Load();
                True("round trip: 2 printers, active 2 in the fields", back.PrinterCount == 2 && back.ActivePrinterId == "2"
                     && back.PrinterWatts == 66 && back.PrinterModel == "A1M" && back.Nozzle == "0.2 stainless" && back.PrinterName == "Mini");
                True("round trip: printer 1 unchanged", back.FindPrinter("1").Watts == 88 && back.FindPrinter("1").Name == "My A1");
                True("round trip: next id 3", back.AddPrinter("", "P1S").Id == "3");
                // unknown active id -> first printer; bad Printer.x lines skipped
                System.IO.File.WriteAllText(file, "PrinterWatts=90\r\nActivePrinter=7\r\nPrinter.x.PrinterWatts=1\r\nPrinter.5.Foo=1\r\n"
                    + "Printer.5.PrinterWatts=120\r\nPrinter.5.PrinterModel=P1S\r\nLastPrinterId=6\r\n");
                var odd = AppSettings.Load();
                True("unknown active -> first; Printer.5 read; bad lines skipped", odd.ActivePrinterId == "1" && odd.PrinterWatts == 90
                     && odd.PrinterCount == 2 && odd.FindPrinter("5").Watts == 120 && odd.FindPrinter("5").Model == "P1S");
                True("LastPrinterId kept -> next id 7", odd.AddPrinter("", "A1").Id == "7");
                // v1.8.0 run once rewrote the file without Printer.N / ActivePrinter / LastPrinterId (tester A):
                // the history still has quotes of printers 2 and 5 -> the next new printer is 6, not 2
                System.IO.File.WriteAllText(file, "SettingsVersion=2\r\nPrinterWatts=100\r\nPrinterName=Bambu Lab A1\r\n");
                var lost = AppSettings.Load();
                var lostHist = new List<QuoteRecord> { new QuoteRecord { PrinterId = "2" }, new QuoteRecord { PrinterId = "5" },
                    new QuoteRecord(), new QuoteRecord { PrinterId = "x" }, null };
                True("ReservePrinterIds: 2 and 5 in history -> raised", History.ReservePrinterIds(lostHist, lost) && lost.LastPrinterId == 5);
                True("ReservePrinterIds again -> no change", !History.ReservePrinterIds(lostHist, lost));
                True("after ReservePrinterIds: next id 6 (old quotes stay on their printers)", lost.AddPrinter("", "A1M").Id == "6");
                True("ReservePrinterIds null list / settings -> false", !History.ReservePrinterIds(null, lost)
                     && !History.ReservePrinterIds(lostHist, null));
                True("ReservePrinterId lower or invalid -> no change", !lost.ReservePrinterId("3") && !lost.ReservePrinterId("0")
                     && !lost.ReservePrinterId(null) && lost.LastPrinterId == 6);
            }
            finally
            {
                AppSettings.ClearTestFolder();
            }

            // history: FromQuote snapshots the active printer; old JSON -> "" = printer "1"
            var hs = HistSettings(); hs.AddPrinter("Mini", "A1M"); hs.SelectPrinter("2");
            var hr = History.FromQuote(Example(), Pricing.Calculate(Example(), hs), hs, "Benchy", "", new DateTime(2026, 11, 10, 9, 0, 0));
            True("FromQuote: printer 2 snapshot", hr.PrinterId == "2" && hr.PrinterName == "Mini" && hr.PrinterModel == "A1M");
            var hj = History.ParseJson(History.ToJson(new List<QuoteRecord> { hr }));
            True("printer JSON round trip", hj != null && hj[0].PrinterId == "2" && hj[0].PrinterName == "Mini" && hj[0].PrinterModel == "A1M");
            var ho = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\"},{\"Id\":\"b\",\"PrinterId\":null}]}");
            True("old JSON: printer fields empty, PrinterOf = 1", ho != null && ho[0].PrinterId == "" && ho[0].PrinterName == ""
                 && History.PrinterOf(ho[0]) == "1" && History.PrinterOf(ho[1]) == "1");

            // profit per printer, November 2026:
            // A printer 1, 2 h, 30 + 0,40, rate 2,50, customer 80 -> 80 - 30 - 0,40 - 5 = 44,60
            // B printer 2, 1 h 30, 12 + 0,15, rate 1,80, friends 25 -> wear 2,70; 25 - 12 - 0,15 - 2,70 = 10,15
            // C printer 2 failed, 1 h, 10 + 0,10 + 1,80 -> lost 11,90
            // D old record (no id), 0 h 30, 8 + 0,05, rate 2,50, customer 50 -> wear 1,25; 50 - 8 - 0,05 - 1,25 = 40,70
            // E printer 1 not printed -> nothing; F printer 3 (removed) in December: 1 h, 5 + 0,10, rate 1, customer 20 -> 13,90
            // printer 1 = 44,60 + 40,70 = 85,30; printer 2 = 10,15 - 11,90 = -1,75; sum 83,55
            var list = new List<QuoteRecord>
            {
                PrinterRec("3", "2026-12-02 10:00:00", 1, 0, 5, 0.10, 1, History.ForCustomer, 20, true, false),
                PrinterRec("1", "2026-11-09 10:00:00", 3, 0, 40, 0.50, 2.5, History.ForCustomer, 99, false, false),
                PrinterRec("2", "2026-11-08 10:00:00", 1, 0, 10, 0.10, 1.8, History.ForCustomer, 30, false, true),
                PrinterRec("2", "2026-11-07 10:00:00", 1, 30, 12, 0.15, 1.8, History.ForFriends, 25, true, false),
                PrinterRec("1", "2026-11-06 10:00:00", 2, 0, 30, 0.40, 2.5, History.ForCustomer, 80, true, false),
                PrinterRec(null, "2026-11-01 10:00:00", 0, 30, 8, 0.05, 2.5, History.ForCustomer, 50, true, false),
            };
            list[0].PrinterName = "Old P1S"; list[0].PrinterModel = "P1S";
            var ps = new AppSettings(); ps.AddPrinter("Mini", "A1M");
            var nov = History.PrinterTotals(list, ps, 2026, 11);
            True("PrinterTotals Nov: rows 1, 2 (no removed printer 3 this month)", nov.Count == 2 && nov[0].Id == "1" && nov[1].Id == "2"
                 && nov[1].Name == "Mini" && !nov[0].Removed);
            Eq("printer 1 profit 85,30", 85.30, nov[0].Profit);
            Eq("printer 1 prints 2 (not printed one ignored)", 2, nov[0].Prints);
            Eq("printer 1 wear 5 + 1,25", 6.25, nov[0].WearCost);
            Eq("printer 2 profit -1,75", -1.75, nov[1].Profit);
            Eq("printer 2 lost 11,90", 11.90, nov[1].Lost);
            Eq("printer 2 prints 1, failed 1", 11, nov[1].Prints * 10 + nov[1].FailedPrints);
            Eq("sum = MonthSummary 83,55", History.MonthSummary(list, 2026, 11).Profit, nov[0].Profit + nov[1].Profit);
            Eq("MonthSummary printer 2 = row", -1.75, History.MonthSummary(list, 2026, 11, "2").Profit);
            var all = History.PrinterTotals(list, ps, 0, 0);
            True("PrinterTotals all time: removed printer 3 last, snapshot name", all.Count == 3 && all[2].Id == "3" && all[2].Removed
                 && all[2].Name == "Old P1S" && all[2].Model == "P1S" && all[2].Month == 0);
            Eq("printer 3 all time 20 - 5 - 0,10 - 1 = 13,90", 13.90, all[2].Profit);
            Eq("all time sum 83,55 + 13,90 = 97,45", 97.45, all[0].Profit + all[1].Profit + all[2].Profit);
            Eq("MonthSummary year 0 = all time 97,45", 97.45, History.MonthSummary(list, 0, 0).Profit);
            var dec = History.PrinterTotals(list, ps, 2026, 12);
            True("PrinterTotals Dec: printers 1 and 2 with 0, printer 3 removed", dec.Count == 3 && dec[0].Prints == 0
                 && dec[1].Prints == 0 && dec[2].Prints == 1);
            var noSet = History.PrinterTotals(list, null, 2026, 11);
            True("PrinterTotals without settings: ids from records, not Removed", noSet.Count == 2 && noSet[0].Id == "1"
                 && noSet[1].Id == "2" && !noSet[1].Removed);
            True("PrinterTotals null list -> settings printers, 0", History.PrinterTotals(null, ps, 2026, 11).Count == 2);
        }

        // ---------- several printers: online's pure slicer functions (plans\several-printers\online tests.md) ----------
        static bool Has(string text, params string[] parts)
        {
            if (text == null) return false;
            foreach (var p in parts) if (text.IndexOf(p, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        // a small .3mf in a temp folder: entry name -> text
        static string Make3mf(string dir, string name, Dictionary<string, string> entries)
        {
            string path = System.IO.Path.Combine(dir, name);
            using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
                foreach (var kv in entries)
                    using (var w = new System.IO.StreamWriter(zip.CreateEntry(kv.Key).Open())) w.Write(kv.Value);
            return path;
        }

        /// IsSlicedProject (light) gives the same true/false as ReadSliceInfo(path) != null; ReadSliceInfo reads the
        /// project_settings once for the slot profiles and the printer (same results as the string parsers).
        static void SlicedProjectTests()
        {
            L.Danish = false;
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pricing_sliced_test_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                string xml = SliceInfo(PlateXml("3600", "10", "<filament id=\"1\" type=\"PLA\" color=\"#FF0000\" used_m=\"1\" used_g=\"6\"/>",
                                                             "<filament id=\"2\" type=\"PLA\" color=\"#00FF00\" used_m=\"1\" used_g=\"4\"/>"));
                string settings = "{\"filament_settings_id\":[\"Bambu PLA Basic @BBL A1\",\"Bambu PLA Matte @BBL A1\"]," +
                                  "\"printer_model\":\"Bambu Lab A1 mini\",\"nozzle_diameter\":[\"0.4\"]}";
                var good = Make3mf(dir, "good.gcode.3mf", new Dictionary<string, string> {
                    { "Metadata/plate_1.gcode", "G1" }, { "Metadata/slice_info.config", xml }, { "Metadata/project_settings.config", settings } });
                var noInfo = Make3mf(dir, "noinfo.gcode.3mf", new Dictionary<string, string> { { "Metadata/plate_1.gcode", "G1" } });
                var badXml = Make3mf(dir, "bad.gcode.3mf", new Dictionary<string, string> {
                    { "Metadata/plate_1.gcode", "G1" }, { "Metadata/slice_info.config", "<config><plate>" } });
                var noPlate = Make3mf(dir, "noplate.gcode.3mf", new Dictionary<string, string> {
                    { "Metadata/plate_1.gcode", "G1" }, { "Metadata/slice_info.config", SliceInfo(PlateXml("0", "0")) } });
                var noGcode = Make3mf(dir, "model.3mf", new Dictionary<string, string> { { "Metadata/slice_info.config", xml } });
                Func<string, bool> full = p => { try { return BambuSlicer.ReadSliceInfo(p) != null; } catch { return false; } };
                foreach (var p in new[] { good, noInfo, badXml, noPlate })
                    True("IsSlicedProject = ReadSliceInfo != null: " + System.IO.Path.GetFileName(p), BambuSlicer.IsSlicedProject(p) == full(p));
                True("IsSlicedProject good / no gcode / missing", BambuSlicer.IsSlicedProject(good) && !BambuSlicer.IsSlicedProject(noGcode)
                     && !BambuSlicer.IsSlicedProject(System.IO.Path.Combine(dir, "gone.gcode.3mf")) && !BambuSlicer.IsSlicedProject(noInfo));
                var r = BambuSlicer.ReadSliceInfo(good);
                var slots = BambuSlicer.ParseFilamentSlots(settings);
                var pp = BambuSlicer.ParseProjectPrinter(settings, xml);
                True("ReadSliceInfo slot profiles = ParseFilamentSlots", r.Filaments.Count == 2 && r.Filaments[0].Profile == slots[0] && r.Filaments[1].Profile == slots[1]);
                True("ReadSliceInfo printer = ParseProjectPrinter", r.PrinterModel == pp.Key && pp.Key == "A1M" && r.PrinterName == pp.Name && r.PrinterNozzle == "0.4");
                True("ParseProjectPrinter strips the nozzle from printer_settings_id", BambuSlicer.ParseProjectPrinter(
                     "{\"printer_settings_id\":\"Bambu Lab P1S 0.4 nozzle\"}", null).Key == "P1S");
                True("ParseSlotStrings bad / empty json -> empty", BambuSlicer.ParseSlotStrings("{bad", "x").Count == 0
                     && BambuSlicer.ParseSlotStrings("", "x").Count == 0 && BambuSlicer.ParseSlotStrings("{\"x\":\"a\"}", "x")[0] == "a");
            }
            finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
        }

        static void SlicerPrinterTests()
        {
            L.Danish = false;
            // 1. ModelKeyFromName
            True("ModelKey A1", BambuSlicer.ModelKeyFromName("Bambu Lab A1") == "A1");
            True("ModelKey A1 mini", BambuSlicer.ModelKeyFromName("Bambu Lab A1 mini") == "A1M");
            True("ModelKey a1 MINI (case)", BambuSlicer.ModelKeyFromName("bambu lab a1 MINI") == "A1M");
            True("ModelKey P1S", BambuSlicer.ModelKeyFromName("Bambu Lab P1S") == "P1S");
            True("ModelKey A1 mini 0.4 nozzle", BambuSlicer.ModelKeyFromName("Bambu Lab A1 mini 0.4 nozzle") == "A1M");
            True("ModelKey P1S 0.6 nozzle", BambuSlicer.ModelKeyFromName("Bambu Lab P1S 0.6 nozzle") == "P1S");
            True("ModelKey ids N1/N2S/C12", BambuSlicer.ModelKeyFromName("N1") == "A1M" && BambuSlicer.ModelKeyFromName("N2S") == "A1"
                 && BambuSlicer.ModelKeyFromName("C12") == "P1S");
            True("ModelKey X1 Carbon / P1P -> \"\"", BambuSlicer.ModelKeyFromName("Bambu Lab X1 Carbon") == ""
                 && BambuSlicer.ModelKeyFromName("Bambu Lab P1P") == "");
            True("ModelKey empty/null -> null", BambuSlicer.ModelKeyFromName("") == null && BambuSlicer.ModelKeyFromName(null) == null);

            // 2. FindPrinter / MachinePreset
            True("MachinePreset A1M 0.4", BambuSlicer.MachinePreset("A1M", "0.4") == "Bambu Lab A1 mini 0.4 nozzle");
            True("MachinePreset P1S 0.2", BambuSlicer.MachinePreset("P1S", "0.2") == "Bambu Lab P1S 0.2 nozzle");
            True("MachinePreset \"\" 0.6 -> A1", BambuSlicer.MachinePreset("", "0.6") == "Bambu Lab A1 0.6 nozzle");
            True("MachinePreset X9 -> A1", BambuSlicer.MachinePreset("X9", "0.4") == "Bambu Lab A1 0.4 nozzle");
            True("MachinePreset A1 0.4", BambuSlicer.MachinePreset("A1", "0.4") == "Bambu Lab A1 0.4 nozzle");
            True("FindPrinter a1m / null", BambuSlicer.FindPrinter("a1m").Key == "A1M" && BambuSlicer.FindPrinter(null).Key == "A1");
            True("FindPrinter A1M bed 180 x 180", BambuSlicer.FindPrinter("A1M").BedX == 180 && BambuSlicer.FindPrinter("A1M").BedZ == 180);
            True("FindPrinter P1S bed Z 250", BambuSlicer.FindPrinter("P1S").BedZ == 250);

            // 3. ShortName
            True("ShortName @BBL X1C", BambuSlicer.ShortName("0.20mm Standard @BBL X1C") == "0.20mm Standard");
            True("ShortName @BBL A1M 0.2 nozzle", BambuSlicer.ShortName("0.20mm Standard @BBL A1M 0.2 nozzle") == "0.20mm Standard");
            True("ShortName @BBL P1S 0.4 nozzle", BambuSlicer.ShortName("Bambu PLA Matte @BBL P1S 0.4 nozzle") == "Bambu PLA Matte");
            True("ShortName @BBL A1", BambuSlicer.ShortName("Bambu PLA Basic @BBL A1") == "Bambu PLA Basic");
            True("ShortName no suffix unchanged", BambuSlicer.ShortName("Bambu PLA Basic") == "Bambu PLA Basic");

            // 4. ParseCompatiblePrinters
            var cp = BambuSlicer.ParseCompatiblePrinters("{\"name\":\"x\",\"compatible_printers\":[\"Bambu Lab A1 0.4 nozzle\",\"Bambu Lab A1 0.6 nozzle\"]}");
            True("ParseCompatiblePrinters 2 items", cp != null && cp.Count == 2 && cp[0] == "Bambu Lab A1 0.4 nozzle");
            var cp2 = BambuSlicer.ParseCompatiblePrinters("{\"compatible_printers\": [\n \"Bambu Lab P1S 0.4 nozzle\"\n ]}");
            True("ParseCompatiblePrinters multi-line 1 item", cp2 != null && cp2.Count == 1);
            bool cpEmpty = true;
            foreach (var j in new[] { "{\"compatible_printers\": []}", "{\"name\":\"x\"}", null, "not json" })
            {
                var l = BambuSlicer.ParseCompatiblePrinters(j);
                if (l == null || l.Count != 0) cpEmpty = false;
            }
            True("ParseCompatiblePrinters empty/missing/null/bad -> 0, never null", cpEmpty);

            // 5. PickProfile
            True("PickProfile P1S filament", BambuSlicer.PickProfile(new[] { "Bambu PLA Matte @BBL X1C", "Bambu PLA Matte @BBL P1S 0.4 nozzle",
                "Bambu PLA Basic @BBL X1C" }, "Bambu PLA Matte", "P1S", "0.4", false) == "Bambu PLA Matte @BBL P1S 0.4 nozzle");
            True("PickProfile P1S filament fallback X1C", BambuSlicer.PickProfile(new[] { "Bambu PLA Basic @BBL P1P", "Bambu PLA Basic @BBL X1C" },
                "Bambu PLA Basic", "P1S", "0.4", false) == "Bambu PLA Basic @BBL X1C");
            True("PickProfile P1S process", BambuSlicer.PickProfile(new[] { "0.20mm Standard @BBL X1C", "0.20mm Strength @BBL X1C" },
                "0.20mm Standard", "P1S", "0.4", true) == "0.20mm Standard @BBL X1C");
            True("PickProfile A1 nozzle-specific wins", BambuSlicer.PickProfile(new[] { "Bambu PLA Palette @BBL A1", "Bambu PLA Palette @BBL A1 0.4 nozzle" },
                "Bambu PLA Palette", "A1", "0.4", false) == "Bambu PLA Palette @BBL A1 0.4 nozzle");
            True("PickProfile A1 0.6 -> plain A1", BambuSlicer.PickProfile(new[] { "Bambu PLA Matte @BBL A1" },
                "Bambu PLA Matte", "A1", "0.6", false) == "Bambu PLA Matte @BBL A1");
            True("PickProfile short is a full name", BambuSlicer.PickProfile(new[] { "Bambu PLA Matte @BBL A1M" },
                "Bambu PLA Matte @BBL A1", "A1M", "0.4", false) == "Bambu PLA Matte @BBL A1M");
            True("PickProfile no match -> null", BambuSlicer.PickProfile(new[] { "Bambu PETG HF @BBL A1" },
                "Bambu PLA Matte", "A1", "0.4", false) == null);
            True("PickProfile null list -> null", BambuSlicer.PickProfile(null, "Bambu PLA Matte", "A1", "0.4", false) == null);

            // 6. ParseProjectPrinter
            var pp = BambuSlicer.ParseProjectPrinter("{\"printer_model\":\"Bambu Lab A1 mini\",\"printer_settings_id\":\"Bambu Lab A1 mini 0.4 nozzle\","
                + "\"nozzle_diameter\":[\"0.4\"]}", "<config><plate><metadata key=\"printer_model_id\" value=\"\"/></plate></config>");
            True("ParseProjectPrinter CLI output -> A1M", pp != null && pp.Key == "A1M" && pp.Name == "Bambu Lab A1 mini" && pp.Nozzle == "0.4");
            pp = BambuSlicer.ParseProjectPrinter("{\"printer_settings_id\":\"Bambu Lab P1S 0.6 nozzle\"}", null);
            True("ParseProjectPrinter only settings id -> P1S", pp != null && pp.Key == "P1S" && pp.Name == "Bambu Lab P1S" && pp.Nozzle == "");
            pp = BambuSlicer.ParseProjectPrinter("{\"printer_model\":\"Bambu Lab X1 Carbon\",\"nozzle_diameter\":[\"0.4\"]}", null);
            True("ParseProjectPrinter unsupported X1 Carbon", pp != null && pp.Key == "" && pp.Name == "Bambu Lab X1 Carbon");
            pp = BambuSlicer.ParseProjectPrinter(null, "<config><plate><metadata key=\"printer_model_id\" value=\"C12\"/><metadata "
                + "key=\"nozzle_diameters\" value=\"0.4\"/></plate></config>");
            True("ParseProjectPrinter GUI file C12 -> P1S", pp != null && pp.Key == "P1S" && pp.Name == "Bambu Lab P1S" && pp.Nozzle == "0.4");
            True("ParseProjectPrinter nothing -> null", BambuSlicer.ParseProjectPrinter("{}", "<config><plate/></config>") == null
                 && BambuSlicer.ParseProjectPrinter(null, null) == null);
            bool ppThrew = false;
            try { BambuSlicer.ParseProjectPrinter(null, "<config><plate"); } catch { ppThrew = true; }
            True("ParseProjectPrinter bad XML -> no throw", !ppThrew);

            // 7. error texts
            True("BedErrorText A1M", Has(BambuSlicer.BedErrorText("A1M"), "Bambu Lab A1 mini", "180 × 180 × 180 mm", "plates has nothing on it"));
            True("BedErrorText P1S", Has(BambuSlicer.BedErrorText("P1S"), "256 × 256 × 250 mm"));
            True("BedErrorText A1 points to Scale…", Has(BambuSlicer.BedErrorText("A1"), "Scale…"));
            True("SliceException.IsBedError: -50 yes; -100 / 0 / message only no", new SliceException("m", -50, null).IsBedError
                 && !new SliceException("m", -100, null).IsBedError && !new SliceException("m", 0, null).IsBedError && !new SliceException("m").IsBedError);
            True("BedErrorText \"\" -> A1", Has(BambuSlicer.BedErrorText(""), "Bambu Lab A1", "256 × 256 × 256 mm"));
            True("SliceErrorText -50 = BedErrorText", BambuSlicer.SliceErrorText(-50, "a.stl", "One of the plate is empty…", "A1M")
                 == BambuSlicer.BedErrorText("A1M"));
            True("SliceErrorText -17 no message", BambuSlicer.SliceErrorText(-17, "a.stl", "", "A1") == "Bambu Studio could not slice a.stl: it stopped with error -17");
            True("SliceErrorText 1 with message", BambuSlicer.SliceErrorText(1, "a.stl", "boom", "A1") == "Bambu Studio could not slice a.stl: boom");
            True("UnsupportedPrinterText X1 Carbon", Has(BambuSlicer.UnsupportedPrinterText("Bambu Lab X1 Carbon"), "Bambu Lab X1 Carbon",
                "doesn't support yet", "https://github.com/gettic/PrintPrice/issues"));
            True("UnsupportedPrinterText \"\"", Has(BambuSlicer.UnsupportedPrinterText(""), "another printer"));
            L.Danish = true;
            try
            {
                True("BedErrorText A1M Danish", Has(BambuSlicer.BedErrorText("A1M"), "byggepladen"));
                True("BedErrorText A1 Danish points to Skalér…", Has(BambuSlicer.BedErrorText("A1"), "Skalér…"));
                True("UnsupportedPrinterText Danish", Has(BambuSlicer.UnsupportedPrinterText("X"), "https://github.com/gettic/PrintPrice/issues", "endnu"));
            }
            finally { L.Danish = false; }

            // 7b. plate / filament clash (Bambu Studio 2.x CLI exit -61)
            True("IsPlateError -61 + clash text", BambuSlicer.IsPlateError(-61, "Filaments are not compatible with the plate type. "
                + "Please verify the slicing of all plates in Bambu Studio before uploading."));
            True("IsPlateError -61 empty -> true", BambuSlicer.IsPlateError(-61, ""));
            True("IsPlateError -61 other text -> false", !BambuSlicer.IsPlateError(-61, "something else"));
            True("IsPlateError -50 -> false", !BambuSlicer.IsPlateError(-50, "One of the plate is empty…"));
            string engPla = "Bambu PLA Matte can't be printed on the Engineering Plate (Bambu Studio doesn't allow it). "
                + "Pick another build plate in Settings > Printer, or another filament.";
            True("PlateErrorText Engineering + PLA Matte", BambuSlicer.PlateErrorText("Engineering Plate", new[] { "Bambu PLA Matte" }) == engPla);
            var pe2 = BambuSlicer.PlateErrorText("High Temp Plate", new[] { "Bambu PLA Matte @BBL A1", "Bambu PETG HF", "Bambu PLA Matte" });
            True("PlateErrorText short names, no duplicates, plate label", pe2 != null
                 && pe2.StartsWith("Bambu PLA Matte, Bambu PETG HF can't be printed on the Smooth PEI / High Temp Plate", StringComparison.Ordinal));
            var pe3 = BambuSlicer.PlateErrorText("", null);
            True("PlateErrorText no plate/filaments", pe3 != null && pe3.StartsWith("The filament can't be printed on this build plate", StringComparison.Ordinal));
            True("PlateErrorText unknown plate shown as is", Has(BambuSlicer.PlateErrorText("Odd Plate", new[] { "X" }), "on the Odd Plate"));
            True("SliceErrorText -61 = PlateErrorText", BambuSlicer.SliceErrorText(-61, "a.stl", "Filaments are not compatible with the plate type. …",
                "A1", "Engineering Plate", new[] { "Bambu PLA Matte" }) == BambuSlicer.PlateErrorText("Engineering Plate", new[] { "Bambu PLA Matte" }));
            True("SliceErrorText 6 args -50 = BedErrorText", BambuSlicer.SliceErrorText(-50, "a.stl", "x", "A1M", "Engineering Plate", null)
                 == BambuSlicer.BedErrorText("A1M"));
            True("PlateTempKey all plates + unknown", BambuSlicer.PlateTempKey("Engineering Plate") == "eng_plate_temp"
                 && BambuSlicer.PlateTempKey("High Temp Plate") == "hot_plate_temp"
                 && BambuSlicer.PlateTempKey("Textured PEI Plate") == "textured_plate_temp"
                 && BambuSlicer.PlateTempKey("Cool Plate") == "cool_plate_temp"
                 && BambuSlicer.PlateTempKey("Supertack Plate") == "supertack_plate_temp"
                 && BambuSlicer.PlateTempKey("x") == "");
            L.Danish = true;
            try
            {
                True("PlateErrorText Danish", BambuSlicer.PlateErrorText("Engineering Plate", new[] { "Bambu PLA Matte" })
                     == "Bambu PLA Matte kan ikke printes på Engineering Plate (Bambu Studio tillader det ikke). "
                     + "Vælg en anden byggeplade i Indstillinger > Printer, eller et andet filament.");
            }
            finally { L.Danish = false; }

            // 8. printer table
            True("Thumb n2s / n1 / p1s", BambuSlicer.FindPrinter("A1").Thumb == "n2s" && BambuSlicer.FindPrinter("A1M").Thumb == "n1"
                 && BambuSlicer.FindPrinter("P1S").Thumb == "p1s");
            bool picThrew = false; string pic1 = "x", pic2 = "x";
            try
            {
                pic1 = BambuSlicer.PrinterPicture(null, "A1");
                pic2 = BambuSlicer.PrinterPicture(@"C:\nowhere\bambu-studio.exe", "P1S");
            }
            catch { picThrew = true; }
            True("PrinterPicture null exe / missing exe -> null, no throw", !picThrew && pic1 == null && pic2 == null);
            bool tableOk = true;
            var keys = new HashSet<string>();
            foreach (var p in BambuSlicer.Printers)
                if (!keys.Add(p.Key) || BambuSlicer.FindPrinter(p.Key) != p) tableOk = false;
            True("Printers: distinct keys, FindPrinter finds each", tableOk);
        }

        // Num.Kr / Num.N2 / Num.Round2: half-øre values round like .NET Framework's "N2" on every runtime.
        // Part 1 (fixed strings) holds on .NET Framework and .NET 10 alike; part 2 compares with this runtime's
        // own "N2", so it proves "desktop unchanged" only here on .NET Framework (on .NET 10 it would differ).
        static void MoneyRoundingTests()
        {
            var dk = Num.Dk;
            // 25.005 is stored as 25.00499999999999900524...; 15 digits give 25.0050000000000 -> half up -> 25,01
            True("Kr 25.005 = 25,01 kr.", Num.Kr(25.005) == "25,01 kr.");
            True("Kr 0.005 = 0,01 kr.", Num.Kr(0.005) == "0,01 kr.");          // 0.005000000000000000104 anyway
            True("Kr 1.115 = 1,12 kr.", Num.Kr(1.115) == "1,12 kr.");          // stored 1.11499999999999999112
            True("Kr 2.675 = 2,68 kr.", Num.Kr(2.675) == "2,68 kr.");          // stored 2.67499999999999982236
            True("Kr 1.005 = 1,01 kr.", Num.Kr(1.005) == "1,01 kr.");          // stored 1.00499999999999989342
            True("Kr -25.005 = -25,01 kr.", Num.Kr(-25.005) == "-25,01 kr.");  // away from zero
            True("Kr -2.675 = -2,68 kr.", Num.Kr(-2.675) == "-2,68 kr.");
            True("Kr -0.001 = 0,00 kr. (no minus zero)", Num.Kr(-0.001) == "0,00 kr.");
            True("Kr 0 = 0,00 kr.", Num.Kr(0) == "0,00 kr.");
            True("Kr 1234567.895 = 1.234.567,90 kr.", Num.Kr(1234567.895) == "1.234.567,90 kr.");
            True("Kr 1000000000.125 = 1.000.000.000,13 kr.", Num.Kr(1000000000.125) == "1.000.000.000,13 kr.");   // exact .125, half up
            True("Kr 31.2125 = 31,21 kr.", Num.Kr(31.2125) == "31,21 kr.");   // normal values unchanged
            True("Kr 54.6975 = 54,70 kr.", Num.Kr(54.6975) == "54,70 kr.");
            True("Kr 50 = 50,00 kr.", Num.Kr(50) == "50,00 kr.");
            True("Kr 0.004 = 0,00 kr.", Num.Kr(0.004) == "0,00 kr.");
            True("Kr 0.1 + 0.2 = 0,30 kr.", Num.Kr(0.1 + 0.2) == "0,30 kr.");
            // 25.0049 x: a real non-midpoint below half stays down
            True("Kr 25.0049 = 25,00 kr.", Num.Kr(25.0049) == "25,00 kr.");
            True("Round2 25.005 = 25.01m", Num.Round2(25.005) == 25.01m);
            True("Round2 -1.115 = -1.12m", Num.Round2(-1.115) == -1.12m);
            True("N2 NaN falls back", Num.N2(double.NaN) == double.NaN.ToString("N2", dk));

            // part 2 (desktop only): same text as .NET Framework's own "N2" for many values
            int diffs = 0; string first = null;
            var rnd = new Random(12345);
            var vals = new List<double>();
            for (int c = -200000; c <= 200000; c++) vals.Add(c / 1000.0);          // every 0,001 kr. in ±200 kr.
            for (int c = 0; c < 20000; c++) vals.Add(c + 0.005);                   // half-øre values up to 20.000 kr.
            for (int c = 0; c < 20000; c++) vals.Add(-(c * 7 + 0.005));
            for (int c = 0; c < 50000; c++) vals.Add((rnd.NextDouble() - 0.5) * Math.Pow(10, rnd.Next(-4, 16)));
            vals.Add(1e20); vals.Add(-123456789012.345); vals.Add(1e-7); vals.Add(-1e-7); vals.Add(0.0049999999999);
            foreach (double v in vals)
            {
                string want = v.ToString("N2", dk) + " kr.";
                if (Num.Kr(v) != want) { diffs++; if (first == null) first = v.ToString("R") + ": " + Num.Kr(v) + " vs " + want; }
            }
            True("Kr = .NET Framework N2 for " + vals.Count + " values" + (first != null ? " (first diff " + first + ")" : ""), diffs == 0);

            // ShownOre: fixed values (both runtimes), then the old F2 formula on this runtime (desktop: unchanged)
            True("ShownOre 25.005 = 2501", Pricing.ShownOre(25.005) == 2501);
            True("ShownOre 2.675 = 268", Pricing.ShownOre(2.675) == 268);
            True("ShownOre 1.115 = 112", Pricing.ShownOre(1.115) == 112);
            True("ShownOre -25.005 = -2501", Pricing.ShownOre(-25.005) == -2501);
            True("ShownOre 54.6975 = 5470", Pricing.ShownOre(54.6975) == 5470);
            True("ShownOre 0 = 0", Pricing.ShownOre(0) == 0);
            int oreDiffs = 0; string firstOre = null;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (double v in vals)
            {
                if (Math.Abs(v) >= 1e16) continue;   // beyond long øre
                long old = (long)decimal.Round(decimal.Parse(v.ToString("F2", inv), inv) * 100m);
                if (Pricing.ShownOre(v) != old) { oreDiffs++; if (firstOre == null) firstOre = v.ToString("R") + ": " + Pricing.ShownOre(v) + " vs " + old; }
            }
            True("ShownOre = old F2 formula for the same values" + (firstOre != null ? " (first diff " + firstOre + ")" : ""), oreDiffs == 0);
        }

        // ---------- slice_info.config reader (SliceXml, no System.Xml) against the old XmlDocument + XPath code ----------

        // the old ParseSliceInfo (v1.16.0), kept here as the reference
        static SliceResult OldParseSliceInfo(string xml)
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);
            var res = new SliceResult();
            var slots = new SortedDictionary<int, FilamentUse>();
            foreach (System.Xml.XmlNode plate in doc.SelectNodes("/config/plate"))
            {
                double seconds = OldMeta(plate, "prediction");
                if (seconds <= 0) continue;
                double grams = OldMeta(plate, "weight");
                bool sumFilaments = grams <= 0;
                foreach (System.Xml.XmlNode f in plate.SelectNodes("filament"))
                {
                    double g = OldAttr(f, "used_g");
                    if (sumFilaments) grams += g;
                    int slot = (int)OldAttr(f, "id");
                    if (g <= 0 || slot <= 0) continue;
                    FilamentUse u;
                    if (!slots.TryGetValue(slot, out u))
                    {
                        u = new FilamentUse { Slot = slot, Colour = OldNormColour(OldAttrText(f, "color")), Type = OldAttrText(f, "type") };
                        slots[slot] = u;
                    }
                    u.Grams += g;
                    u.Metres += OldAttr(f, "used_m");
                }
                res.Seconds += seconds;
                res.Grams += grams;
                res.Plates++;
            }
            res.Filaments = new List<FilamentUse>(slots.Values);
            return res.Plates > 0 ? res : null;
        }

        // the old XML part of ParseProjectPrinter (no project_settings), then the same naming rules
        static ProjectPrinter OldPrinterFromXml(string xml)
        {
            string name = "", nozzle = "";
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);
                var n = doc.SelectSingleNode("/config/plate/metadata[@key='printer_model_id']");
                if (n != null) name = OldAttrText(n, "value");
                n = doc.SelectSingleNode("/config/plate/metadata[@key='nozzle_diameters']");
                if (n != null) nozzle = OldAttrText(n, "value").Split(',')[0].Trim();
            }
            catch { }
            if (name.Length == 0) return null;
            var res = new ProjectPrinter { Key = BambuSlicer.ModelKeyFromName(name) ?? "", Name = name, Nozzle = nozzle };
            if (res.Key.Length > 0 && string.Equals(name, BambuSlicer.FindPrinter(res.Key).ModelId, StringComparison.OrdinalIgnoreCase))
                res.Name = BambuSlicer.FindPrinter(res.Key).MachineName;
            return res;
        }

        static string OldAttrText(System.Xml.XmlNode n, string name)
        {
            var a = n.Attributes == null ? null : n.Attributes[name];
            return a == null ? "" : (a.Value ?? "").Trim();
        }

        static double OldMeta(System.Xml.XmlNode plate, string key)
        {
            var n = plate.SelectSingleNode("metadata[@key='" + key + "']");
            return n == null ? 0 : OldAttr(n, "value");
        }

        static double OldAttr(System.Xml.XmlNode n, string name)
        {
            var a = n.Attributes == null ? null : n.Attributes[name];
            double v;
            if (a != null && double.TryParse(a.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0) return v;
            return 0;
        }

        static string OldNormColour(string c)
        {
            if (c == null) return "";
            c = c.Trim();
            if (!c.StartsWith("#")) c = "#" + c;
            if (c.Length < 7 || !System.Text.RegularExpressions.Regex.IsMatch(c.Substring(1, 6), "^[0-9A-Fa-f]{6}$")) return "";
            return c.Substring(0, 7).ToUpperInvariant();
        }

        static string DescribeSlice(SliceResult r)
        {
            if (r == null) return "null";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append(r.Seconds.ToString("R", inv)).Append('|').Append(r.Grams.ToString("R", inv)).Append('|').Append(r.Plates);
            foreach (var f in r.Filaments)
                sb.Append(";").Append(f.Slot).Append(',').Append(f.Colour).Append(',').Append(f.Type).Append(',')
                  .Append(f.Grams.ToString("R", inv)).Append(',').Append(f.Metres.ToString("R", inv));
            return sb.ToString();
        }

        static string DescribePrinter(ProjectPrinter p) { return p == null ? "null" : p.Key + "|" + p.Name + "|" + p.Nozzle; }

        // the real Bambu Studio slice_info.config files we have (plans\ isn't in the repo: none found = skipped)
        static List<KeyValuePair<string, string>> RealSliceInfos()
        {
            var list = new List<KeyValuePair<string, string>>();
            var d = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (d != null && !System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "plans"))) d = d.Parent;
            if (d == null) return list;
            foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(d.FullName, "plans"), "*.3mf", System.IO.SearchOption.AllDirectories))
            {
                try
                {
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(f))
                    {
                        var e = zip.GetEntry("Metadata/slice_info.config");
                        if (e == null) continue;
                        using (var sr = new System.IO.StreamReader(e.Open())) list.Add(new KeyValuePair<string, string>(System.IO.Path.GetFileName(f), sr.ReadToEnd()));
                    }
                }
                catch (Exception) { }
            }
            return list;
        }

        static void SliceXmlTests()
        {
            string fil1 = Fil(1, "#FF0000", "1.5", "4.50"), fil2 = Fil(3, "00ae42ff", "2.25", "6.75");
            string plate = PlateXml("3600", "11.25", fil1, fil2);
            var cases = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (n, x) => cases.Add(new KeyValuePair<string, string>(n, x));
            // the committed real Bambu Studio 2.09 files (tests\multipart-samples): always there, so a missing one fails
            int committed = 0;
            var known = new Dictionary<string, string> {
                // t4: 5316 s, weight 29,65 g, 1 plate; slot 1 #C12E1F PLA 8,03 g 2,65 m; slot 3 #F2F2F2 PLA 21,62 g 6,81 m
                { "t4_three.slice_info.config", "5316|29.65|1;1,#C12E1F,PLA,8.03,2.65;3,#F2F2F2,PLA,21.62,6.81" },
                // t5: 5207 s, weight 26,04 g, 1 plate; slots 1-3: 0,28 g 0,09 m / 7,72 g 2,55 m / 18,32 g 5,77 m
                { "t5_edited.slice_info.config", "5207|26.04|1;1,#C12E1F,PLA,0.28,0.09;2,#1F5FC1,PLA,7.72,2.55;3,#F2F2F2,PLA,18.32,5.77" } };
            foreach (var k in known)
            {
                string xml = null;
                try { xml = MultipartSample(k.Key); } catch (Exception) { }
                if (xml == null) continue;
                committed++;
                add("real " + k.Key, xml);
                string got;
                try { got = DescribeSlice(BambuSlicer.ParseSliceInfo(xml)); } catch (Exception ex) { got = ex.GetType().Name; }
                True("SliceXml known values: " + k.Key + (got == k.Value ? "" : " (got " + got + ")"), got == k.Value);
            }
            True("SliceXml: committed real samples found (" + committed + " of 2)", committed == 2);
            // extra local samples: any .3mf under plans\ (not in the repo, so none on a fresh clone)
            int extra = 0;
            foreach (var real in RealSliceInfos()) { add("real " + real.Key, real.Value); extra++; }
            if (extra == 0) Console.WriteLine("SKIP: no extra .3mf under plans\\ (local-only real-file parity)");
            add("builder", SliceInfo(plate));
            add("two plates, one unsliced", SliceInfo(plate, "<plate><metadata key=\"index\" value=\"2\"/></plate>", PlateXml("60", "0", Fil(3, "#00FF00", "1", "2"))));
            add("CRLF, indent, comment, PI", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<!-- made by hand -->\r\n<config>\r\n  <?pi data?>\r\n  <plate>\r\n    <metadata key=\"prediction\" value=\"100\" />\r\n    <metadata key=\"weight\" value=\"2.5\"/>\r\n    " + fil1 + "\r\n  </plate>\r\n</config>\r\n<!-- end -->\r\n");
            add("single quotes", "<config><plate><metadata key='prediction' value='77'/><metadata key='weight' value='1'/><filament id='2' color='#123456' type='PETG' used_g='1' used_m='0.3'/></plate></config>");
            add("entities", "<config><plate><metadata key=\"prediction\" value=\"&#49;&#x30;0\"/><filament id=\"1\" type=\"P&amp;LA &lt;x&gt; &quot;q&quot; &apos;\" color=\"&#x23;aBcDeF\" used_g=\"3\"/></plate></config>");
            add("white space in values", "<config><plate><metadata key=\"prediction\" value=\" 120 \"/><metadata key=\"weight\" value=\"1\n2\"/><filament id=\"1\" type=\"\tPLA\r\nBasic \" color=\"#00FF00\" used_g=\"2\"/><filament id=\"2\" type=\"A&#10;B\" used_g=\"1\"/></plate></config>");
            add("default namespace", "<config xmlns=\"urn:x\">" + plate + "</config>");
            add("namespace reset on plate", "<config xmlns=\"urn:x\"><plate xmlns=\"\"><metadata key=\"prediction\" value=\"5\"/></plate></config>");
            add("namespace reset on config", "<config xmlns=\"\">" + plate + "</config>");
            add("prefixed root", "<b:config xmlns:b=\"u\">" + plate + "</b:config>");
            add("prefixed attribute", "<config xmlns:b=\"u\"><plate><metadata b:key=\"prediction\" value=\"9\"/><metadata key=\"prediction\" b:value=\"9\"/></plate></config>");
            add("duplicate metadata (first wins)", "<config><plate><metadata key=\"prediction\" value=\"0\"/><metadata key=\"prediction\" value=\"50\"/><metadata key=\"weight\" value=\"7\"/><metadata key=\"weight\" value=\"8\"/></plate></config>");
            add("nested filament ignored", "<config><plate><metadata key=\"prediction\" value=\"10\"/><x>" + fil1 + "</x>" + fil2 + "</plate><plate2>" + plate + "</plate2></config>");
            add("other root", "<cfg>" + plate + "</cfg>");
            add("DOCTYPE with an entity", "<?xml version=\"1.0\"?><!DOCTYPE config [<!ENTITY t \"PETG\">]><config><plate><metadata key=\"prediction\" value=\"8\"/><filament id=\"1\" type=\"&t;\" used_g=\"1\"/></plate></config>");
            add("CDATA and text", "<config>text<plate>more <![CDATA[ <not> & markup ]]><metadata key=\"prediction\" value=\"4\"/></plate></config>");
            add("printer in plate 2", "<config><plate><metadata key=\"prediction\" value=\"1\"/></plate><plate><metadata key=\"printer_model_id\" value=\" N1 \"/><metadata key=\"nozzle_diameters\" value=\"0.4,0.6\"/></plate></config>");
            add("other printer", "<config><plate><metadata key=\"printer_model_id\" value=\"BL-P001\"/><metadata key=\"nozzle_diameters\" value=\"\"/></plate></config>");
            add("empty elements, spaces in tags", "<config ><plate ><metadata key = \"prediction\"   value=\"30\" ></metadata ><filament id=\"4\" used_g=\"0.5\" ></filament></plate ></config >");
            // odd or not well-formed: both must refuse (or both accept: LoadXml takes any &#n; reference)
            foreach (var bad in new[] { "\uFEFF<config/>", "  <?xml version=\"1.0\"?><config/>", "<config/>x", "<config a=\"1\" a=\"2\"/>",
                                        "<config a=\"&foo;\"/>", "<a:config/>", "<config a=\"x<y\"/>", "<config>&#xZZ;</config>", "<1config/>",
                                        "<config/ >", "<config a=\"1\"b=\"2\"/>", "", "   ", "<config><plate></config>", "<config></Config>",
                                        "<config>]]></config>", "<!-- a -- b --><config/>", "<config>\u0001</config>", "<config a='&#0;'/>",
                                        "<config/><config/>", "<?xml version=\"1.0\"?><?xml version=\"1.0\"?><config/>", "<config>&amp</config>",
                                        "<config a=1/>", "<config", "<config><!DOCTYPE x></config>", "<config b:a=\"1\"/>", "<?xml?><config/>",
                                        "<?xml version=\"2\"?><config/>", "<config>&#xD800;</config>", "<config xmlns:b=\"\"/>", "<:config/>",
                                        "<config>&#x110000;</config>", "<config>&#;</config>", "<config a=\"&#x41;&#65;&#x1F600;\"/>",
                                        "<config\r\n a=\"1\"\t/>", "<config>æøå &#230;</config>" })
                add("odd " + bad.Replace("\uFEFF", "BOM").Replace("\u0001", "^A"), bad);
            foreach (var r in new[] { "&#x110000;", "&#xFFFFFF;", "&#x7FFFFFC9;", "&#x7FFFFFCA;", "&#x7fffffc9;", "&#2147483599;", "&#2147483600;",
                                      "&#x10FFFF;", "&#x0000041;", "&#0;", "&#xD800;&#xDC00;" })
                add("reference " + r, "<config><plate><metadata key=\"prediction\" value=\"1\"/><filament id=\"1\" used_g=\"1\" type=\"" + r + "\"/></plate></config>");

            foreach (var c in cases)
            {
                string oldR, newR, oldP = DescribePrinter(OldPrinterFromXml(c.Value)), newP = DescribePrinter(BambuSlicer.ParseProjectPrinter(null, c.Value));
                try { oldR = DescribeSlice(OldParseSliceInfo(c.Value)); } catch (System.Xml.XmlException) { oldR = "refused"; }
                try { SliceXml.Parse(c.Value); newR = DescribeSlice(BambuSlicer.ParseSliceInfo(c.Value)); } catch (SliceXmlException) { newR = "refused"; }
                bool desktopSame = true;
                if (oldR == "refused")   // the desktop's ParseSliceInfo still throws XmlDocument's XmlException
                    try { BambuSlicer.ParseSliceInfo(c.Value); desktopSame = false; } catch (System.Xml.XmlException) { } catch (Exception) { desktopSame = false; }
                True("SliceXml = XmlDocument: " + c.Key + (oldR == newR ? "" : " (old " + oldR + ", new " + newR + ")"), oldR == newR && desktopSame);
                True("SliceXml printer = XmlDocument: " + c.Key + (oldP == newP ? "" : " (old " + oldP + ", new " + newP + ")"), oldP == newP);
            }
        }

        // ---------- several files with a colour each (idea 21, SliceInfo.cs / Slicer.cs): samples in tests\multipart-samples ----------
        static string MultipartSample(string name)
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            string[] tries = { System.IO.Path.Combine(b, @"..\tests\multipart-samples"), System.IO.Path.Combine(b, @"tests\multipart-samples"), @"tests\multipart-samples" };
            string dir = tries[0];
            foreach (var t in tries) if (System.IO.Directory.Exists(t)) { dir = t; break; }
            return System.IO.File.ReadAllText(System.IO.Path.Combine(dir, name));
        }

        static string Objs(PlateInfo p) { return J(p.Objects); }

        static string Slots(List<FilamentUse> l)
        {
            var s = new List<string>();
            foreach (var f in l) s.Add(f.Slot.ToString() + ":" + f.Grams.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            return J(s);
        }

        static void MultipartTests()
        {
            bool wasDanish = L.Danish;
            L.Danish = false;
            try
            {
                // PlateList from the samples: t4 = 3 files on one plate, slots 1 and 3, prediction 5316 s, weight 29,65 g
                var t4 = BambuSlicer.ParseSliceInfo(MultipartSample("t4_three.slice_info.config"));
                True("Plates t4: 1 plate", t4 != null && t4.Plates == 1 && t4.PlateList.Count == 1);
                var p4 = t4.PlateList[0];
                True("Plates t4: index 1, 5316 s, 29,65 g, no name/file", p4.Index == 1 && p4.Seconds == 5316 && Math.Abs(p4.Grams - 29.65) < 1e-9
                     && p4.Name == "" && p4.File == "");
                True("Plates t4: 3 objects in file order", Objs(p4) == "partB.stl|partA.stl|test-cube.stl");
                True("Plates t4: plate slots 1 (8,03 g) and 3 (21,62 g)", Slots(p4.Filaments) == "1:8.03|3:21.62"
                     && p4.Filaments[0].Colour == "#C12E1F" && p4.Filaments[1].Type == "PLA");
                True("Plates t4: totals unchanged", t4.Seconds == 5316 && Math.Abs(t4.Grams - 29.65) < 1e-9 && Slots(t4.Filaments) == "1:8.03|3:21.62");
                var t5 = BambuSlicer.ParseSliceInfo(MultipartSample("t5_edited.slice_info.config"));
                True("Plates t5: 1 plate, 1 object 'Assembly', slots 1-3", t5 != null && t5.PlateList.Count == 1 && Objs(t5.PlateList[0]) == "Assembly"
                     && Slots(t5.PlateList[0].Filaments) == "1:0.28|2:7.72|3:18.32" && t5.PlateList[0].Seconds == 5207);

                // several plates: one without a time skipped, skipped objects left out, no weight = sum of its filaments (2 + 3 = 5)
                const string Three = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><config>"
                    + "<plate><metadata key=\"index\" value=\"1\"/><metadata key=\"prediction\" value=\"3600\"/><metadata key=\"weight\" value=\"10.5\"/>"
                    + "<object identify_id=\"1\" name=\"Wheel\" skipped=\"false\"/><object identify_id=\"2\" name=\"Gone\" skipped=\"true\"/>"
                    + "<filament id=\"2\" type=\"PETG\" color=\"#00FF00\" used_m=\"3.5\" used_g=\"10.5\"/></plate>"
                    + "<plate><metadata key=\"index\" value=\"2\"/><metadata key=\"prediction\" value=\"0\"/><metadata key=\"weight\" value=\"9\"/></plate>"
                    + "<plate><metadata key=\"index\" value=\"3\"/><metadata key=\"prediction\" value=\"1200\"/><metadata key=\"weight\" value=\"0\"/>"
                    + "<filament id=\"1\" type=\"PLA\" color=\"#FF0000\" used_m=\"0.7\" used_g=\"2\"/><filament id=\"2\" type=\"PETG\" color=\"#00FF00\" used_m=\"1\" used_g=\"3\"/></plate>"
                    + "</config>";
                var mp = BambuSlicer.ParseSliceInfo(Three);
                True("Plates: 2 of 3 counted (index 1 and 3)", mp != null && mp.Plates == 2 && mp.PlateList.Count == 2
                     && mp.PlateList[0].Index == 1 && mp.PlateList[1].Index == 3);
                True("Plates: skipped object left out", Objs(mp.PlateList[0]) == "Wheel" && mp.PlateList[1].Objects.Count == 0);
                True("Plates: no weight = 2 + 3 = 5 g; totals 4800 s, 10,5 + 5 = 15,5 g", mp.PlateList[1].Grams == 5 && mp.PlateList[0].Grams == 10.5
                     && mp.Seconds == 4800 && mp.Grams == 15.5);
                True("Plates: per-plate slots; total slot 2 = 10,5 + 3 = 13,5", Slots(mp.PlateList[1].Filaments) == "1:2.00|2:3.00"
                     && Slots(mp.Filaments) == "1:2.00|2:13.50");

                // ApplyPlateNames: plate 1 named, plate 3 empty name, plate 2 named but not in the list
                const string Names = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><config><object id=\"2\"><metadata key=\"name\" value=\"x\"/></object>"
                    + "<plate><metadata key=\"plater_id\" value=\"1\"/><metadata key=\"plater_name\" value=\"Wheels\"/></plate>"
                    + "<plate><metadata key=\"plater_id\" value=\"2\"/><metadata key=\"plater_name\" value=\"Ghost\"/></plate>"
                    + "<plate><metadata key=\"plater_id\" value=\"3\"/><metadata key=\"plater_name\" value=\"\"/></plate></config>";
                BambuSlicer.ApplyPlateNames(mp, Names);
                True("Plate names: named plate", mp.PlateList[0].Name == "Wheels");
                True("Plate names: empty name stays \"\", unknown plate ignored", mp.PlateList[1].Name == "" && mp.PlateList.Count == 2);
                BambuSlicer.ApplyPlateNames(t4, MultipartSample("t4_three.model_settings.config"));
                True("Plate names: t4 sample has an empty name", t4.PlateList[0].Name == "");
                var keep = BambuSlicer.ParseSliceInfo(Three);
                keep.PlateList[0].Name = "Mine";
                bool threw = false;
                try
                {
                    BambuSlicer.ApplyPlateNames(keep, null);
                    BambuSlicer.ApplyPlateNames(keep, "");
                    BambuSlicer.ApplyPlateNames(keep, "<config><plate><metadata key=\"plater_id\" value=\"1\"/>");   // broken: never closed
                    BambuSlicer.ApplyPlateNames(keep, "not xml at all <<<");
                    BambuSlicer.ApplyPlateNames(null, Names);
                    BambuSlicer.ApplyPlateNames(new SliceResult(), Names);
                }
                catch (Exception) { threw = true; }
                True("Plate names: missing / broken xml never throw, names unchanged", !threw && keep.PlateList[0].Name == "Mine" && keep.PlateList[1].Name == "");

                // AddResult: first file, no purge / colour-change info (-1 stays -1), plate gets the file name
                var total = new SliceResult();
                var r1 = new SliceResult { Seconds = 3600, Grams = 10, Plates = 1, Picture = new byte[] { 1 }, StudioFile = "a.3mf" };
                r1.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#FF0000", Type = "PLA", Grams = 10, ModelGrams = 9, Metres = 3 });
                var r1p = new PlateInfo { Index = 1, Seconds = 3600, Grams = 10 };
                r1p.Objects.Add("a"); r1p.Filaments.Add(new FilamentUse { Slot = 1, Grams = 10 });
                r1.PlateList.Add(r1p);
                BambuSlicer.AddResult(total, r1, "a.stl");
                True("AddResult 1st: totals", total.Seconds == 3600 && total.Grams == 10 && total.Plates == 1 && Slots(total.Filaments) == "1:10.00");
                True("AddResult 1st: purge / colour change unknown stay -1", total.PurgeGrams == -1 && total.ColourChangeHours == -1);
                True("AddResult 1st: plate copied with File", total.PlateList.Count == 1 && total.PlateList[0].File == "a.stl"
                     && Objs(total.PlateList[0]) == "a" && !ReferenceEquals(total.PlateList[0], r1p) && r1p.File == "");

                // second file with slotOffset 1: its slots 1, 2 -> 2, 3 (never merged into slot 1); purge 1,5 g and 0,25 h known
                var r2 = new SliceResult { Seconds = 1800, Grams = 6, Plates = 1, Picture = new byte[] { 2 }, StudioFile = "b.3mf", PurgeGrams = 1.5, ColourChangeHours = 0.25 };
                r2.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#00FF00", Type = "PETG", Grams = 4, Metres = 1 });
                r2.Filaments.Add(new FilamentUse { Slot = 2, Colour = "#0000FF", Type = "PETG", Grams = 2, ModelGrams = 2 });
                var r2p = new PlateInfo { Index = 1, Name = "Top", Seconds = 1800, Grams = 6 };
                r2p.Filaments.Add(new FilamentUse { Slot = 1, Grams = 4 }); r2p.Filaments.Add(new FilamentUse { Slot = 2, Grams = 2 });
                r2.PlateList.Add(r2p);
                BambuSlicer.AddResult(total, r2, "b.stl", 1);
                True("AddResult offset 1: slots kept apart 1 | 2 | 3", Slots(total.Filaments) == "1:10.00|2:4.00|3:2.00"
                     && total.Filaments[0].Colour == "#FF0000" && total.Filaments[1].Colour == "#00FF00" && total.Filaments[2].Colour == "#0000FF");
                True("AddResult offset 1: 3600 + 1800 = 5400 s, 10 + 6 = 16 g, 2 plates", total.Seconds == 5400 && total.Grams == 16 && total.Plates == 2);
                True("AddResult offset 1: -1 + 1,5 -> 1,5 g purge, -1 + 0,25 -> 0,25 h", total.PurgeGrams == 1.5 && total.ColourChangeHours == 0.25);
                True("AddResult offset 1: plate 2 = b.stl 'Top', slots 2 and 3", total.PlateList.Count == 2 && total.PlateList[1].File == "b.stl"
                     && total.PlateList[1].Name == "Top" && Slots(total.PlateList[1].Filaments) == "2:4.00|3:2.00");
                True("AddResult: r not changed", r2.Filaments[0].Slot == 1 && r2p.Filaments[0].Slot == 1 && r2p.File == "");
                True("AddResult: first picture and Studio file kept", total.Picture[0] == 1 && total.StudioFile == "a.3mf");
                True("AddResult: model grams 9 kept, unknown (-1) copied as -1", total.Filaments[0].ModelGrams == 9 && total.Filaments[1].ModelGrams == -1
                     && total.Filaments[2].ModelGrams == 2);

                // third file without offset: summed into slot 1 (10 + 5 = 15 g, model 9 + 4 = 13 g); unknown purge keeps the sum
                var r3 = new SliceResult { Seconds = 600, Grams = 5, Plates = 1 };
                r3.Filaments.Add(new FilamentUse { Slot = 1, Colour = "#FFFFFF", Grams = 5, ModelGrams = 4 });
                r3.PlateList.Add(new PlateInfo { Index = 1, File = "own.stl", Seconds = 600, Grams = 5 });
                BambuSlicer.AddResult(total, r3, "c.stl");
                True("AddResult no offset: slot 1 = 10 + 5 = 15 g, model 9 + 4 = 13, first colour kept", Slots(total.Filaments) == "1:15.00|2:4.00|3:2.00"
                     && total.Filaments[0].ModelGrams == 13 && total.Filaments[0].Colour == "#FF0000");
                True("AddResult no offset: purge 1,5 and 0,25 h stay (r3 unknown)", total.PurgeGrams == 1.5 && total.ColourChangeHours == 0.25);
                True("AddResult: a plate's own File is kept", total.PlateList.Count == 3 && total.PlateList[2].File == "own.stl");
                var r4 = new SliceResult { Seconds = 60, Grams = 1, Plates = 1, PurgeGrams = 0.5 };
                r4.Filaments.Add(new FilamentUse { Slot = 3, Grams = 1 });   // model grams unknown (-1)
                BambuSlicer.AddResult(total, r4, "d.stl", 0);
                True("AddResult: purge 1,5 + 0,5 = 2,0; slot 3 model 2 + unknown = -1", total.PurgeGrams == 2.0 && total.Filaments[2].ModelGrams == -1
                     && Slots(total.Filaments) == "1:15.00|2:4.00|3:3.00");
                threw = false;
                try { BambuSlicer.AddResult(null, r1, "x"); BambuSlicer.AddResult(total, null, "x"); }
                catch (Exception) { threw = true; }
                True("AddResult: null total / r = nothing", !threw && total.PlateList.Count == 3 && total.Seconds == 6060);
                // FilePlates: a file with 1 plate -> 1, a file with 2 plates -> 2 on both; a value already set is kept
                True("AddResult: FilePlates 1 for one-plate files", total.PlateList[0].FilePlates == 1 && total.PlateList[1].FilePlates == 1);
                var r5 = new SliceResult { Seconds = 120, Grams = 2, Plates = 2 };
                r5.PlateList.Add(new PlateInfo { Index = 1, Seconds = 60, Grams = 1 });
                r5.PlateList.Add(new PlateInfo { Index = 2, Name = "Wheels", Seconds = 60, Grams = 1 });
                var t5total = new SliceResult();
                BambuSlicer.AddResult(t5total, r5, "multi.3mf", 1);
                True("AddResult: FilePlates 2 on both plates of a 2-plate file", t5total.PlateList.Count == 2 && t5total.PlateList[0].FilePlates == 2
                     && t5total.PlateList[1].FilePlates == 2 && t5total.PlateList[1].File == "multi.3mf" && r5.PlateList[0].FilePlates == 0);
                var again = new SliceResult();
                BambuSlicer.AddResult(again, t5total, "other");
                True("AddResult: FilePlates and File already set are kept", again.PlateList.Count == 2 && again.PlateList[0].FilePlates == 2
                     && again.PlateList[0].File == "multi.3mf");

                // TogetherArgs: ids in file order, mix-temp only when the types differ
                True("TogetherArgs: same type", J(BambuSlicer.TogetherArgs(new List<int> { 2, 1, 2 }, new List<string> { "PLA", "pla " }))
                     == "--load-filament-ids|2,1,2|--allow-multicolor-oneplate");
                True("TogetherArgs: PLA + PETG -> --allow-mix-temp", J(BambuSlicer.TogetherArgs(new List<int> { 1, 2 }, new List<string> { "PLA", "PETG" }))
                     == "--load-filament-ids|1,2|--allow-multicolor-oneplate|--allow-mix-temp");
                True("TogetherArgs: empty type ignored, null types", J(BambuSlicer.TogetherArgs(new List<int> { 3, 1 }, new List<string> { "PLA", "" }))
                     == "--load-filament-ids|3,1|--allow-multicolor-oneplate"
                     && J(BambuSlicer.TogetherArgs(new List<int> { 1, 2 }, null)) == "--load-filament-ids|1,2|--allow-multicolor-oneplate");

                // plate texts: 4800 s = 80 min = 1 h 20 min
                var pt = new PlateInfo { Index = 2, Name = "Wheels", File = "partA.stl", Seconds = 4800, Grams = 34.12 };
                pt.Objects.AddRange(new[] { "a", "b", "c" });
                // time = QuoteRows.Time ("1 h 20 m", "0 h 25 m"; at least 1 m when > 0 s); grams Math.Round(g, 1) as "0.#"
                // (34,12 -> 34,1; 5 -> 5; 0 -> 0); line = title + " – " + numbers. showFile: the file's only plate = the file name,
                // one of several (FilePlates 2+) = "file, plate N 'Name'"
                True("PlateNumbers 1 h 20 m, 34,12 -> 34,1 g, 3 objects", BambuSlicer.PlateNumbers(pt) == "1 h 20 m, 34,1 g, 3 objects");
                True("PlateLine without file", BambuSlicer.PlateLine(pt, false) == "Plate 2 'Wheels' – 1 h 20 m, 34,1 g, 3 objects");
                True("PlateTitle showFile, only plate of its file = file name", BambuSlicer.PlateTitle(pt, true) == "partA.stl");
                pt.File = "multi.3mf"; pt.FilePlates = 2;
                True("PlateLine showFile, one of several plates", BambuSlicer.PlateLine(pt, true) == "multi.3mf, plate 2 'Wheels' – 1 h 20 m, 34,1 g, 3 objects"
                     && BambuSlicer.PlateLine(pt, false) == "Plate 2 'Wheels' – 1 h 20 m, 34,1 g, 3 objects");
                var p0 = new PlateInfo { Index = 0, File = "body.stl", Seconds = 1500, Grams = 5 };   // 25 min, no objects, no name
                True("PlateNumbers under an hour, 0 objects", BambuSlicer.PlateNumbers(p0) == "0 h 25 m, 5 g");
                True("PlateLine showFile, file's only plate", BambuSlicer.PlateLine(p0, true) == "body.stl – 0 h 25 m, 5 g");
                True("PlateTitle index 0 -> 1, no name", BambuSlicer.PlateTitle(p0, false) == "Plate 1");
                var p1 = new PlateInfo { Index = 1, Seconds = 89, Grams = 0 };   // 1,48 min -> 1 m
                p1.Objects.Add("x");
                True("PlateLine 1 object, 89 s = 0 h 1 m, 0 g", BambuSlicer.PlateLine(p1, true) == "Plate 1 – 0 h 1 m, 0 g, 1 object");
                p1.Seconds = 20;   // 0,33 min rounds to 0, but > 0 s -> 1 m
                True("PlateNumbers 20 s -> at least 1 m", BambuSlicer.PlateNumbers(p1).StartsWith("0 h 1 m,"));
                p1.Seconds = 0;
                True("PlateNumbers 0 s -> 0 h 0 m", BambuSlicer.PlateNumbers(p1).StartsWith("0 h 0 m,"));
                p1.Seconds = 3600 - 29;   // 59,52 min -> 60 = 1 h 0 m
                True("PlateNumbers 3571 s -> 1 h 0 m", BambuSlicer.PlateNumbers(p1).StartsWith("1 h 0 m,"));
                p1.Seconds = 89;
                True("Plate texts null", BambuSlicer.PlateTitle(null, true) == "" && BambuSlicer.PlateNumbers(null) == "" && BambuSlicer.PlateLine(null, true) == "");
                L.Danish = true;
                True("Plate texts Danish", BambuSlicer.PlateLine(pt, false) == "Plade 2 'Wheels' – 1 t 20 m, 34,1 g, 3 objekter"
                     && BambuSlicer.PlateLine(pt, true) == "multi.3mf, plade 2 'Wheels' – 1 t 20 m, 34,1 g, 3 objekter"
                     && BambuSlicer.PlateLine(p0, true) == "body.stl – 0 t 25 m, 5 g"
                     && BambuSlicer.PlateLine(p1, false) == "Plade 1 – 0 t 1 m, 0 g, 1 objekt");
                L.Danish = false;

                // CutName: 40 characters stay, 41 -> 39 + "…" (40), trimmed, null = ""
                string forty = new string('a', 40), fortyOne = new string('b', 41);
                True("CutName 40 stays", BambuSlicer.CutName(forty, 40) == forty);
                True("CutName 41 -> 39 + …", BambuSlicer.CutName(fortyOne, 40) == new string('b', 39) + "…" && BambuSlicer.CutName(fortyOne, 40).Length == 40);
                True("CutName trims, also before …", BambuSlicer.CutName("  Wheels  ", 40) == "Wheels"
                     && BambuSlicer.CutName(new string('c', 38) + "  defg", 40) == new string('c', 38) + "…" && BambuSlicer.CutName(null, 40) == "");
                var longName = new PlateInfo { Index = 1, Name = new string('n', 45), File = new string('f', 45) + ".stl" };
                True("PlateTitle cuts name at 40", BambuSlicer.PlateTitle(longName, false) == "Plate 1 '" + new string('n', 39) + "…'");
                True("PlateTitle cuts file at 40 (only plate)", BambuSlicer.PlateTitle(longName, true) == new string('f', 39) + "…");
                longName.FilePlates = 3;
                True("PlateTitle cuts file and name at 40 (one of several)", BambuSlicer.PlateTitle(longName, true)
                     == new string('f', 39) + "…, plate 1 '" + new string('n', 39) + "…'");
            }
            finally { L.Danish = wasDanish; }
        }

        // ---------- G-code from other slicers (idea 14, SliceInfo.cs): samples in tests\gcode-samples ----------
        static string GcodeDir()
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            string[] tries = { System.IO.Path.Combine(b, @"..\tests\gcode-samples"), System.IO.Path.Combine(b, @"tests\gcode-samples"), @"tests\gcode-samples" };
            foreach (var t in tries) if (System.IO.Directory.Exists(t)) return t;
            return tries[0];
        }

        static string GcodeText(string file)
        {
            return System.IO.File.ReadAllText(System.IO.Path.Combine(GcodeDir(), file), System.Text.Encoding.UTF8);
        }

        static SliceResult GcodeRead(string file)
        {
            return BambuSlicer.ParseGcode(GcodeText(file)).ToResult();
        }

        static bool Near2(double a, double b) { return Math.Abs(a - b) < 0.015; }

        static void GcodeTests()
        {
            True("gcode samples folder found", System.IO.File.Exists(System.IO.Path.Combine(GcodeDir(), "bambu_a1_2colour.gcode")));
            var r = GcodeRead("bambu_a1_2colour.gcode");
            True("bambu time", r.Seconds == 47 * 60 + 32);
            True("bambu grams", Near2(r.Grams, 11.86) && !r.GramsEstimated && r.SlicerName == BambuSlicer.SlicerBambu);
            True("bambu slots 1,3", r.Filaments.Count == 2 && r.Filaments[0].Slot == 1 && r.Filaments[1].Slot == 3 && r.Filaments[1].Colour == "#C12E1F"
                 && r.Filaments[1].Profile == "Bambu PLA Matte @BBL A1" && Near2(r.Filaments[1].Grams, 8.22));
            True("bambu printer A1", r.PrinterModel == "A1" && r.PrinterNozzle == "0.4" && !r.UnsupportedPrinter);
            True("bambu no note", BambuSlicer.OtherSlicerNote(r) == null);

            r = GcodeRead("orca_a1.gcode");
            True("orca time (total estimated)", r.Seconds == 39 * 60 + 40);
            True("orca grams", Near2(r.Grams, 11.67) && !r.GramsEstimated && r.SlicerName == BambuSlicer.SlicerOrca);
            True("orca A1", r.PrinterModel == "A1" && r.Filaments.Count == 1 && r.Filaments[0].Colour == "#F2754E" && r.Filaments[0].Type == "PLA"
                 && r.Filaments[0].Profile == "Generic PLA @System" && BambuSlicer.ShortName(r.Filaments[0].Profile) == "Generic PLA");
            True("ShortName drops a final @System only", BambuSlicer.ShortName("Generic PETG @System") == "Generic PETG"
                 && BambuSlicer.ShortName("My @Systemic PLA") == "My @Systemic PLA");
            True("orca note: one sentence", BambuSlicer.OtherSlicerNote(r) == "Time is OrcaSlicer's estimate and may differ in Bambu Studio.");
            True("orca A1: no other-printer warning", !r.OtherPrinter && BambuSlicer.OtherPrinterWarning(r, "A1") == null);

            r = GcodeRead("orca_voron.gcode");
            True("orca voron time normal mode", r.Seconds == 28 * 60 + 5);
            True("orca voron not refused, name kept", r.PrinterModel == null && !r.UnsupportedPrinter && r.PrinterName == "Voron 2.4 350");
            True("orca voron ABS", r.Filaments.Count == 1 && r.Filaments[0].Type == "ABS" && r.Filaments[0].Colour == "#2850E0");
            True("orca voron: OtherPrinter + warning with the selected printer", r.OtherPrinter
                 && BambuSlicer.OtherPrinterWarning(r, "A1M") == "The file was sliced for Voron 2.4 350, not the Bambu Lab A1 mini: its time is for that printer.");
            True("orca voron: note has no printer line", !BambuSlicer.OtherSlicerNote(r).Contains("Voron"));
            True("orca voron: Generic ABS kept", r.Filaments[0].KeepProfile && r.Filaments[0].Profile == "Generic ABS @System");
            True("supported Bambu printer: no warning", BambuSlicer.OtherPrinterWarning(new SliceResult { SlicerName = BambuSlicer.SlicerOrca, PrinterModel = "A1M",
                 PrinterName = "Bambu Lab A1 mini" }, "A1") == null);
            True("no printer name: no warning", !GcodeRead("cura_weight_placeholders.gcode").OtherPrinter);
            True("null result: no note, no warning", BambuSlicer.OtherSlicerNote(null) == null && BambuSlicer.OtherPrinterWarning(null, "A1") == null);
            // idea 17's default filament: other slicers keep a named "Generic PLA", Bambu files still get the Settings filament
            var two = BambuSlicer.ParseGcode("; generated by PrusaSlicer 2.8.1 on 2026-11-10 at 10:00:00 UTC\n; estimated printing time (normal mode) = 10m\n" +
                                             "; filament used [g] = 2, 3\n; filament_settings_id = \"Generic PLA\";\"\"\n; filament_type = PLA;PLA\n").ToResult();
            BambuSlicer.ApplyDefaultFilament(two.Filaments, "Bambu PLA Matte");
            True("prusa Generic PLA kept, empty slot gets the default", two.Filaments[0].Profile == "Generic PLA" && two.Filaments[1].Profile == "Bambu PLA Matte");
            var bb = BambuSlicer.ParseGcode("; BambuStudio 02.03.00.70\n; total estimated time: 10m\n; total filament weight [g] : 2.00,3.00\n" +
                                            "; filament_settings_id = \"Generic PLA @BBL A1\";\"Generic PLA @BBL A1\"\n; filament_type = PLA;PLA\n").ToResult();
            BambuSlicer.ApplyDefaultFilament(bb.Filaments, "Bambu PLA Matte");
            True("bambu Generic PLA still -> default", bb.Filaments.Count == 2 && bb.Filaments[0].Profile == "Bambu PLA Matte" && bb.Filaments[1].Profile == "Bambu PLA Matte");

            r = GcodeRead("prusa_mmu_2colour.gcode");
            True("prusa time normal not silent", r.Seconds == 3723);
            True("prusa grams per slot", Near2(r.Grams, 10.93) && r.Filaments.Count == 2 && Near2(r.Filaments[0].Grams, 6.38) && Near2(r.Filaments[1].Grams, 4.55));
            True("prusa colours (extruder fallback)", r.Filaments[0].Colour == "#FF8000" && r.Filaments[1].Colour == "#FFE000");
            True("prusa types/profiles", r.Filaments[0].Type == "PETG" && r.Filaments[1].Profile == "Prusament PLA");
            True("prusa purge", Near2(r.PurgeGrams, 1.84));
            True("prusa printer name only", r.PrinterModel == null && !r.UnsupportedPrinter && r.PrinterName == "Original Prusa MK4 MMU3" && r.PrinterNozzle == "0.4");
            True("prusa PNG thumbnail (QOI skipped)", r.Picture != null && BambuSlicer.ImageType(r.Picture) == "image/png");
            True("prusa slicer", r.SlicerName == BambuSlicer.SlicerPrusa);

            r = GcodeRead("prusa_no_density.gcode");
            True("prusa 0 g -> from length, estimated", Near2(r.Grams, 11.67) && r.GramsEstimated);
            True("estimated note, 2 sentences", BambuSlicer.OtherSlicerNote(r) ==
                 "Time is PrusaSlicer's estimate and may differ in Bambu Studio. Weight is estimated from the filament length.");

            r = GcodeRead("cura_marlin.gcode");
            True("cura time", r.Seconds == 2269 && r.SlicerName == BambuSlicer.SlicerCura);
            True("cura grams from metres", Near2(r.Grams, 11.67) && r.GramsEstimated && r.Filaments.Count == 1 && r.Filaments[0].Colour == "");
            True("cura printer name only", r.PrinterModel == null && r.PrinterName == "Creality Ender-3 V3");
            string cura = GcodeText("cura_marlin.gcode");
            var head = BambuSlicer.ParseGcode(cura.Split(new[] { ";End of Gcode" }, StringSplitOptions.None)[0]);
            True("cura head only: not complete (diameter at the end)", head.HasValues && !head.Complete);
            True("cura full: complete", BambuSlicer.ParseGcode(cura).Complete);

            r = GcodeRead("cura_griffin.gcode");
            True("griffin time", r.Seconds == 3725);
            True("griffin grams from volume", Near2(r.Grams, 11.67) && r.GramsEstimated && r.Filaments.Count == 1);
            True("griffin printer", r.PrinterName == "Ultimaker S5" && r.PrinterModel == null && r.PrinterNozzle == "0.4");

            r = GcodeRead("cura_weight_placeholders.gcode");
            True("cura weight placeholder", Near2(r.Grams, 6.1) && !r.GramsEstimated && r.Filaments[0].Type == "PETG" && r.Filaments[0].Profile == "Generic PETG");
            True("cura nozzle placeholder", r.PrinterNozzle == "0.6" || r.PrinterNozzle == "");

            r = GcodeRead("bambu_x1c.gcode");
            True("bambu X1C refused", r.UnsupportedPrinter && r.PrinterModel == "" && r.PrinterName == "Bambu Lab X1 Carbon");
            var o = BambuSlicer.ParseGcode("; generated by OrcaSlicer 2.2.0 on 2026-11-10 at 10:00:00\n; estimated printing time (normal mode) = 10m\n; filament used [g] = 2\n; printer_model = Bambu Lab X1 Carbon\n").ToResult();
            True("orca X1C refused", o.UnsupportedPrinter);
            o = BambuSlicer.ParseGcode("; total estimated time: 10m\n; total filament weight [g] : 2\n; printer_model = MK4\n").ToResult();
            True("unknown slicer keeps old rule", o.UnsupportedPrinter);

            True("bgcode by name", BambuSlicer.IsBinaryGcode("x.bgcode", null));
            True("bgcode by bytes", BambuSlicer.IsBinaryGcode("x.gcode", System.Text.Encoding.ASCII.GetBytes("GCDE\x01\x00")));
            True("plain not binary", !BambuSlicer.IsBinaryGcode("x.gcode", System.Text.Encoding.ASCII.GetBytes("; gen")));
            True("IsGcodeFile", BambuSlicer.IsGcodeFile("A.GCODE") && BambuSlicer.IsGcodeFile("a.gco") && BambuSlicer.IsGcodeFile("a.bgcode")
                 && !BambuSlicer.IsGcodeFile("a.gcode.3mf") && !BambuSlicer.IsGcodeFile("a.stl") && !BambuSlicer.IsGcodeFile(null));
            True("Duration", BambuSlicer.Duration("1d 2h 3m 4s") == 93784 && BambuSlicer.Duration("1h2m") == 3720 && BambuSlicer.Duration("12m 18s") == 738);
            True("nothing -> no values", !BambuSlicer.ParseGcode("G28\nG1 X0\n").HasValues && !BambuSlicer.ParseGcode(null).HasValues);
            True("TIME_ELAPSED ignored", BambuSlicer.ParseGcode(";TIME_ELAPSED:99\n").Seconds == 0);

            // big file: numbers only in the tail (as the readers do: head 1 MB + tail 256 KB)
            string prusa = GcodeText("prusa_mmu_2colour.gcode");
            var sb = new System.Text.StringBuilder(prusa.Split(new[] { "G28" }, StringSplitOptions.None)[0]);
            sb.Append("G28\n");
            while (sb.Length < 3 * 1024 * 1024) sb.Append("G1 X10.000 Y10.000 E0.01234\n");
            sb.Append(prusa.Split(new[] { "G1 X10 Y10 F3000" }, StringSplitOptions.None)[1]);
            string big = sb.ToString();
            var hi = BambuSlicer.ParseGcode(big.Substring(0, BambuSlicer.GcodeHeadBytes));
            True("big: head alone incomplete", !hi.Complete);
            var full = BambuSlicer.ParseGcode(big.Substring(0, BambuSlicer.GcodeHeadBytes) + "\n" + big.Substring(big.Length - BambuSlicer.GcodeTailBytes)).ToResult();
            True("big: head + tail", full.Seconds == 3723 && Near2(full.Grams, 10.93) && full.Filaments.Count == 2);

            // odd files (tester, v1.16.0): BOM, decimal comma, impossible numbers, only one of time / weight
            string bomPath = System.IO.Path.Combine(GcodeDir(), "odd_bom_prusa.gcode");
            var bomText = System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(bomPath));
            True("BOM sample really starts with U+FEFF", bomText.Length > 0 && bomText[0] == '﻿');
            r = BambuSlicer.ParseGcode(bomText).ToResult();
            True("BOM PrusaSlicer: slicer found, not refused", r.SlicerName == BambuSlicer.SlicerPrusa && !r.UnsupportedPrinter && r.OtherPrinter && Near2(r.Grams, 10.93));
            r = BambuSlicer.ReadGcodeFile(bomPath);
            True("BOM PrusaSlicer via ReadGcodeFile", r.SlicerName == BambuSlicer.SlicerPrusa && !r.UnsupportedPrinter && r.FromSlicedFile && r.Seconds == 3723);
            r = GcodeRead("odd_decimal_comma.gcode");
            True("decimal comma list '6,38, 4,55' -> 2 colours", r.Filaments.Count == 2 && Near2(r.Filaments[0].Grams, 6.38) && Near2(r.Filaments[1].Grams, 4.55));
            r = GcodeRead("odd_cura_decimal_comma.gcode");
            True("Cura '3,91282m' -> 11,67 g", Near2(r.Grams, 11.67) && r.GramsEstimated);
            True("decimal comma ';' list and '1,2,3' list", BambuSlicer.NumberList("6,38;4,55").Count == 2 && Near2(BambuSlicer.NumberList("6,38;4,55")[1], 4.55)
                 && BambuSlicer.NumberList("1,2,3").Count == 3 && BambuSlicer.NumberList("3.64,8.22").Count == 2 && Near2(BambuSlicer.NumberList("11,76")[0], 11.76));
            bool refused = false; string why = null;
            try { GcodeRead("odd_impossible.gcode"); } catch (SliceException ex) { refused = true; why = ex.Message; }
            True("272 kg refused with the impossible text", refused && why == BambuSlicer.ImpossibleValuesText());
            refused = false;
            try { BambuSlicer.ParseGcode("; estimated printing time (normal mode) = 31d 0h\n; filament used [g] = 10\n").ToResult(); } catch (SliceException) { refused = true; }
            True("31 days refused", refused);
            refused = false;
            try { BambuSlicer.ParseGcode("; estimated printing time (normal mode) = 1h\n; filament used [g] = 9000, 9000, 9000\n").ToResult(); } catch (SliceException) { refused = true; }
            True("27 kg total refused", refused);
            True("29 days, 9 kg + 9 kg allowed", !BambuSlicer.ImpossibleValues(BambuSlicer.ParseGcode("; estimated printing time (normal mode) = 29d\n; filament used [g] = 9000, 9000\n")));
            var tOnly = BambuSlicer.ParseGcode(GcodeText("partial_time_only.gcode"));
            r = tOnly.ToResult();
            True("time only: kept, grams missing", tOnly.HasValues && !tOnly.Complete && r.Seconds == 2400 && r.Grams == 0 && r.MissingGrams && !r.MissingTime
                 && BambuSlicer.MissingValueText(r) == "Couldn't find the weight in the file: type it in.");
            r = GcodeRead("partial_grams_only.gcode");
            True("grams only: kept, time missing", Near2(r.Grams, 11.67) && r.Seconds == 0 && r.MissingTime && !r.MissingGrams
                 && BambuSlicer.MissingValueText(r) == "Couldn't find the print time in the file: type it in.");
            True("grams only: no time sentence, no note", BambuSlicer.OtherSlicerNote(r) == null);
            True("grams only: warning without the time part", BambuSlicer.OtherPrinterWarning(new SliceResult { OtherPrinter = true, MissingTime = true,
                 PrinterName = "Voron 2.4 350" }, "A1") == "The file was sliced for Voron 2.4 350, not the Bambu Lab A1.");
            True("no time + estimated grams: only the weight sentence", BambuSlicer.OtherSlicerNote(new SliceResult { SlicerName = BambuSlicer.SlicerCura,
                 MissingTime = true, GramsEstimated = true }) == "Weight is estimated from the filament length.");
            True("both found: no missing text", BambuSlicer.MissingValueText(GcodeRead("orca_a1.gcode")) == null);
            True("time only via ReadGcodeFile", BambuSlicer.ReadGcodeFile(System.IO.Path.Combine(GcodeDir(), "partial_time_only.gcode")).MissingGrams);
            refused = false;
            try { BambuSlicer.ReadGcodeFile(System.IO.Path.Combine(GcodeDir(), "binary.bgcode")); } catch (SliceException) { refused = true; }
            True("neither: ParseGcode HasValues false", !BambuSlicer.ParseGcode("G28\n").HasValues && refused);

            bool wasDanish = L.Danish;
            L.Danish = true;
            True("Danish missing weight / time", BambuSlicer.MissingValueText(new SliceResult { MissingGrams = true }) == "Kunne ikke finde vægten i filen: skriv den ind."
                 && BambuSlicer.MissingValueText(new SliceResult { MissingTime = true }) == "Kunne ikke finde printtiden i filen: skriv den ind.");
            True("Danish no-time warning and note", BambuSlicer.OtherPrinterWarning(new SliceResult { OtherPrinter = true, MissingTime = true,
                 PrinterName = "Voron 2.4 350" }, "A1") == "Filen er slicet til Voron 2.4 350, ikke Bambu Lab A1."
                 && BambuSlicer.OtherSlicerNote(new SliceResult { SlicerName = "Cura", MissingTime = true, GramsEstimated = true }) == "Vægten er anslået ud fra filamentlængden.");
            True("Danish impossible", BambuSlicer.ImpossibleValuesText().StartsWith("Tallene i filen ser forkerte ud"));
            True("Danish note", BambuSlicer.OtherSlicerNote(new SliceResult { SlicerName = "Cura", GramsEstimated = true })
                 == "Tiden er Curas skøn og kan afvige i Bambu Studio. Vægten er anslået ud fra filamentlængden.");
            True("Danish genitive after s", BambuSlicer.OtherSlicerNote(new SliceResult { SlicerName = "Simplify3D Plus" }).StartsWith("Tiden er Simplify3D Plus' skøn"));
            True("Danish bgcode", BambuSlicer.BinaryGcodeText().StartsWith("Filen er en binær G-code"));
            True("Danish missing", BambuSlicer.GcodeMissingText().EndsWith("Du kan selv skrive tid og vægt ind."));
            True("Danish other-printer warning", BambuSlicer.OtherPrinterWarning(new SliceResult { OtherPrinter = true, PrinterName = "Voron 2.4 350" }, "A1")
                 == "Filen er slicet til Voron 2.4 350, ikke Bambu Lab A1: tiden gælder den printer.");
            L.Danish = wasDanish;
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
            True("suffix 0.6", BambuSlicer.FamilySuffix("A1", "0.6") == " @BBL A1 0.6 nozzle" && BambuSlicer.FamilySuffix("A1", "0.4") == " @BBL A1");
            Eq("AutoUpdateCheck default is 1", 1, new AppSettings().AutoUpdateCheck);
            var orig0 = new AppSettings { AutoUpdateCheck = 0 };
            Eq("Clone keeps AutoUpdateCheck 0", 0, orig0.Clone().AutoUpdateCheck);
            True("Language default is empty", new AppSettings().Language == "");
            True("IsDanish(da) true", AppSettings.IsDanish("da"));
            True("IsDanish(en) false", !AppSettings.IsDanish("en"));
            True("IsDanish(unknown) = IsDanish(auto)", AppSettings.IsDanish("xx") == AppSettings.IsDanish(""));
            True("PrinterName default is Bambu Lab A1", new AppSettings().PrinterName == "Bambu Lab A1");
            True("Clone keeps PrinterName", new AppSettings { PrinterName = "Prusa MK4" }.Clone().PrinterName == "Prusa MK4");
            True("CleanPrinterName empty/spaces/null -> default", AppSettings.CleanPrinterName("") == "Bambu Lab A1"
                 && AppSettings.CleanPrinterName("   ") == "Bambu Lab A1" && AppSettings.CleanPrinterName(null) == "Bambu Lab A1");
            True("CleanPrinterName trims and caps at 40", AppSettings.CleanPrinterName("  X1C  ") == "X1C"
                 && AppSettings.CleanPrinterName(new string('a', 50)) == new string('a', 40));
            True("Clone keeps Language da",new AppSettings { Language = "da" }.Clone().Language == "da");
            Eq("FailedPrintPct default is 0 (off)", 0, new AppSettings().FailedPrintPct);
            Eq("Clone keeps FailedPrintPct", 12, new AppSettings { FailedPrintPct = 12 }.Clone().FailedPrintPct);
            var s = new AppSettings { MachineRatePerHour = 0, FailedPrintPct = 0, MinimumPrice = 0 }; // 100 W, 25 %, 75 %, no wear, no fails (formula check)
            var r = Pricing.Calculate(Example(), s);
            Eq("material", 21.60, r.MaterialCost);
            Eq("power kWh", 0.55, r.PowerKwh);
            Eq("power cost", 1.10, r.PowerCost);
            Eq("failed prints off", 0, r.FailedPrintCost);
            Eq("base", 22.70, r.BaseCost);
            Eq("friends", 28.375, r.FriendsPrice);
            Eq("others", 39.725, r.OthersPrice);

            // Failed prints 5 %: 22.70 × 0.05 = 1.135; base 23.835; × 1.25 = 29.79375; × 1.75 = 41.71125
            var rf = Pricing.Calculate(Example(), new AppSettings { MachineRatePerHour = 0, FailedPrintPct = 5, MinimumPrice = 0 });
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
            // 39.725 + 9.405 = 49.13 is under the default 50 kr. minimum -> customers pay 50
            Eq("others with defaults (minimum 50)", 50, Pricing.Calculate(Example(), s4).OthersPrice);
            Eq("friends with defaults", 37.78, Pricing.Calculate(Example(), s4).FriendsPrice); // 28.375 + 9.405

            // Benchy: 0 h 38 m, 11.8 g, 200 DKK/kg, 3.65 DKK/kWh, defaults.
            // base = 2.36 + 0.6333 × 0.1 × 3.65 = 2.5912; wear = 0.6333 × 1.71 = 1.0830
            var benchy = Pricing.Calculate(new QuoteInput { Hours = 0, Minutes = 38, FilamentGrams = 11.8, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 3.65 }, new AppSettings { MinimumPrice = 0 });
            Eq("benchy others with the default 50 kr. minimum", 50, Pricing.Calculate(new QuoteInput { Hours = 0, Minutes = 38, FilamentGrams = 11.8, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 3.65 }, new AppSettings()).OthersPrice);
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
            // Bambu Studio hand-over (pure parts + refusals only; nothing is ever started)
            var stAll = new List<string> { @"C:\t\a", @"C:\t\B\", @"C:\t\c" };
            True("StudioFolders keeps own folder", J(BambuSlicer.StudioFoldersToDelete(stAll, @"C:\t\a\x.3mf")) == @"C:\t\B\|C:\t\c");
            True("StudioFolders case + trailing slash", J(BambuSlicer.StudioFoldersToDelete(stAll, @"c:\T\b\x.3mf")) == @"C:\t\a|C:\t\c");
            True("StudioFolders keep listed with slash", J(BambuSlicer.StudioFoldersToDelete(new List<string> { @"C:\t\a\" }, @"C:\t\A\x.3mf")) == "");
            True("StudioFolders null keep -> all", J(BambuSlicer.StudioFoldersToDelete(stAll, null)) == @"C:\t\a|C:\t\B\|C:\t\c");
            True("StudioFolders null list -> empty", BambuSlicer.StudioFoldersToDelete(null, @"C:\t\a\x.3mf").Count == 0);
            True("StudioArgs spaces quoted", BambuSlicer.StudioArguments(@"C:\my dir\a b.3mf") == "\"C:\\my dir\\a b.3mf\"");
            True("StudioArgs æøå quoted", BambuSlicer.StudioArguments(@"C:\blå\æøå.3mf") == "\"C:\\blå\\æøå.3mf\"");
            string stRel = BambuSlicer.StudioArguments(@"rel\a.3mf");
            True("StudioArgs relative -> full path", stRel.StartsWith("\"") && stRel.EndsWith("\"") && stRel.IndexOf(@":\") == 2 && stRel.EndsWith(@"\rel\a.3mf""") );
            string stTmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pricing_studio_test_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(stTmp);
            try
            {
                string fakeExe = System.Reflection.Assembly.GetExecutingAssembly().Location;   // the test program itself; refused before any start
                string txt = System.IO.Path.Combine(stTmp, "a.txt");
                System.IO.File.WriteAllText(txt, "x");
                string missing3mf = System.IO.Path.Combine(stTmp, "gone.3mf");
                string[][] stCases = new string[][] {
                    new string[] { null, missing3mf }, new string[] { System.IO.Path.Combine(stTmp, "nope.exe"), missing3mf },
                    new string[] { fakeExe, txt }, new string[] { fakeExe, missing3mf }, new string[] { fakeExe, null } };
                string[] stNames = { "null exe", "missing exe", ".txt file", "missing file", "null file" };
                for (int i = 0; i < stCases.Length; i++)
                {
                    bool right = false;
                    try { BambuSlicer.OpenInStudio(stCases[i][0], stCases[i][1]); }
                    catch (SliceException) { right = true; }
                    catch (Exception) { }
                    True("OpenInStudio refuses: " + stNames[i], right);
                }
            }
            finally { try { System.IO.Directory.Delete(stTmp, true); } catch { } }
            True("IsImage PNG",Net.IsImage(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
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
                // 4-inv. MainInvoice (quote/invoice toggle): default 0, 1 survives save/load, anything else reads as 0
                Eq("MainInvoice default 0", 0, new AppSettings().MainInvoice);
                var mi = new AppSettings(); mi.MainInvoice = 1;
                Eq("MainInvoice 1 survives ToIni/FromIni", 1, AppSettings.FromIni(mi.ToIni()).MainInvoice);
                True("MainInvoice line in ini", Array.IndexOf(mi.ToIni(), "MainInvoice=1") >= 0);
                Eq("MainInvoice=7 reads as 0", 0, AppSettings.FromIni(new string[] { "MainInvoice=7" }).MainInvoice);
                Eq("MainInvoice=-1 reads as 0", 0, AppSettings.FromIni(new string[] { "MainInvoice=-1" }).MainInvoice);
                Eq("MainInvoice missing reads as 0", 0, AppSettings.FromIni(new string[] { "SetupDone=1" }).MainInvoice);
                // issue #6: InvoiceCopies (copy of every invoice PDF): default 1, 0 survives save/load, anything else reads as 1
                Eq("InvoiceCopies default 1", 1, new AppSettings().InvoiceCopies);
                Eq("InvoiceCopies new install (FromIni null) 1", 1, AppSettings.FromIni(null).InvoiceCopies);
                var ic = new AppSettings(); ic.InvoiceCopies = 0;
                Eq("InvoiceCopies 0 survives ToIni/FromIni", 0, AppSettings.FromIni(ic.ToIni()).InvoiceCopies);
                True("InvoiceCopies line in ini", Array.IndexOf(ic.ToIni(), "InvoiceCopies=0") >= 0);
                Eq("InvoiceCopies 1 survives ToIni/FromIni", 1, AppSettings.FromIni(new AppSettings().ToIni()).InvoiceCopies);
                Eq("InvoiceCopies=7 reads as 1", 1, AppSettings.FromIni(new string[] { "InvoiceCopies=7" }).InvoiceCopies);
                Eq("InvoiceCopies=-1 reads as 1", 1, AppSettings.FromIni(new string[] { "InvoiceCopies=-1" }).InvoiceCopies);
                Eq("InvoiceCopies=abc keeps 1", 1, AppSettings.FromIni(new string[] { "InvoiceCopies=abc" }).InvoiceCopies);
                Eq("InvoiceCopies missing reads as 1", 1, AppSettings.FromIni(new string[] { "SetupDone=1" }).InvoiceCopies);
                Eq("InvoiceCopies Clone keeps 0", 0, ic.Clone().InvoiceCopies);
                // 4a. first-time setup (idea 19): no settings.ini and no history.json = a new user -> setup window, empty quote name
                Eq("new install: SetupDone 0", 0, fresh.SetupDone);
                True("new install: QuoteName empty", fresh.QuoteName == "");
                True("new AppSettings() (tests, picture mode, Defaults button): SetupDone 1, QuoteName empty",
                     defaults.SetupDone == 1 && defaults.QuoteName == "");
                // history.json but no settings.ini: not a new user -> no setup window, the old default name kept
                string histOnlyDir = System.IO.Path.Combine(testDir, "history-only");
                True("UseFolder history-only", AppSettings.UseFolder(histOnlyDir));
                System.IO.Directory.CreateDirectory(histOnlyDir);
                if (System.IO.File.Exists(AppSettings.FilePath)) System.IO.File.Delete(AppSettings.FilePath);
                System.IO.File.WriteAllText(History.FilePath, "{}");   // only its existence counts
                var ho = AppSettings.Load();
                True("history only: SetupDone 1, QuoteName Gettic", ho.SetupDone == 1 && ho.QuoteName == "Gettic");
                True("history only: Load wrote no settings.ini", !System.IO.File.Exists(AppSettings.FilePath));
                // ForModel: a printer at its model's defaults (Settings' Defaults button / setup window)
                var fm = PrinterProfile.ForModel("3", "", Wear.ModelP1S);
                True("ForModel P1S: id 3, label name, 130 W, auto wear, P1S rate, stock nozzle/plate/profile", fm.Id == "3"
                     && fm.Name == "Bambu Lab P1S" && fm.Model == "P1S" && fm.Watts == 130 && fm.AutoWearRate == 1
                     && fm.MachineRatePerHour == Wear.DefaultRateFor("P1S") && fm.Nozzle == "0.4 stainless"
                     && fm.BedType == "High Temp Plate" && fm.ProcessPreset == "0.16mm Optimal");
                var fm2 = PrinterProfile.ForModel("x", "  Garage  ", "nope");
                True("ForModel bad id -> 1, unknown model -> A1 (100 W), name trimmed", fm2.Id == "1" && fm2.Model == "A1"
                     && fm2.Watts == 100 && fm2.Name == "Garage" && fm2.MachineRatePerHour == Wear.DefaultRateFor("A1"));
                Eq("ForModel A1 mini: 70 W", 70, PrinterProfile.ForModel("2", null, Wear.ModelA1Mini).Watts);
                // 5. save/load round trip in the test folder
                True("UseFolder test dir again", AppSettings.UseFolder(testDir));
                if (System.IO.Directory.Exists(testDir))   // the empty-file tests below keep settings.bad-*.ini copies
                    foreach (var f in System.IO.Directory.GetFiles(testDir, "settings.bad-*.ini")) System.IO.File.Delete(f);
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
                new AppSettings { PrinterName = "  Prusa MK4  " }.Save();
                True("round trip PrinterName (trimmed)", AppSettings.Load().PrinterName == "Prusa MK4");
                System.IO.File.WriteAllText(testFile, "PrinterName=\r\n");
                True("empty PrinterName in file -> default", AppSettings.Load().PrinterName == "Bambu Lab A1");
                System.IO.File.WriteAllText(testFile, "PrinterName=    \r\n");
                True("spaces-only PrinterName in file -> default", AppSettings.Load().PrinterName == "Bambu Lab A1");
                System.IO.File.WriteAllText(testFile, "PrinterName=" + new string('b', 60) + "\r\n");
                Eq("long PrinterName in file capped at 40", 40, AppSettings.Load().PrinterName.Length);
                // 5a. quote export settings
                True("quote defaults in file (empty name written as QuoteName=)", r5.QuoteName == "" && r5.QuoteValidDays == 14 &&
                     r5.QuotePrice == "customer" && r5.QuoteDark == 1);
                var sq = new AppSettings { QuoteName = "Gettic 3D = Print", QuoteValidDays = 30, QuotePrice = "friends", QuoteDark = 0 };
                sq.Save();
                var rq = AppSettings.Load();
                True("round trip QuoteName", rq.QuoteName == "Gettic 3D = Print");
                Eq("round trip QuoteValidDays", 30, rq.QuoteValidDays);
                True("round trip QuotePrice friends", rq.QuotePrice == "friends" && rq.QuoteFriends && rq.QuoteFor == History.ForFriends);
                Eq("round trip QuoteDark 0 (light)", 0, rq.QuoteDark);
                True("settings file uses key QuoteLook, not QuoteDark", System.IO.File.ReadAllText(testFile).Contains("QuoteLook=0")
                     && !System.IO.File.ReadAllText(testFile).Contains("QuoteDark"));
                System.IO.File.WriteAllText(testFile, "QuoteDark=0\r\n");
                Eq("old QuoteDark=0 key ignored -> dark default", 1, AppSettings.Load().QuoteDark);
                new AppSettings { QuoteName = "" }.Save();
                True("round trip empty QuoteName stays empty", AppSettings.Load().QuoteName == "");
                // SetupDone (idea 19): key round trip; an existing file without the key (old app) = 1, and an old
                // file without QuoteName keeps "Gettic" (only a brand-new install starts empty)
                new AppSettings { SetupDone = 0 }.Save();
                True("file has SetupDone=0", System.IO.File.ReadAllText(testFile).Contains("SetupDone=0\r\n"));
                Eq("round trip SetupDone 0", 0, AppSettings.Load().SetupDone);
                new AppSettings().Save();
                True("file has SetupDone=1", System.IO.File.ReadAllText(testFile).Contains("SetupDone=1\r\n"));
                Eq("round trip SetupDone 1", 1, AppSettings.Load().SetupDone);
                System.IO.File.WriteAllText(testFile, "SettingsVersion=2\r\nFriendsMarkupPct=30\r\n");
                var noKey = AppSettings.Load();
                True("old file without SetupDone / QuoteName: SetupDone 1, QuoteName Gettic", noKey.SetupDone == 1 && noKey.QuoteName == "Gettic");
                System.IO.File.WriteAllText(testFile, "");
                Eq("empty settings.ini (exists) = not new: SetupDone 1", 1, AppSettings.Load().SetupDone);
                System.IO.File.WriteAllText(testFile, "SetupDone=7\r\n");
                Eq("SetupDone 7 -> 1", 1, AppSettings.Load().SetupDone);
                System.IO.File.WriteAllText(testFile, "SetupDone=-1\r\n");
                Eq("SetupDone -1 ignored -> 1", 1, AppSettings.Load().SetupDone);
                System.IO.File.WriteAllText(testFile, "SetupDone=0\r\nQuoteName=\r\n");
                var still = AppSettings.Load();
                True("file with SetupDone=0 (skipped nothing yet) keeps 0 and empty name", still.SetupDone == 0 && still.QuoteName == "");
                Eq("FromIni(null) (website new install): SetupDone 1", 1, AppSettings.FromIni(null).SetupDone);
                Eq("FromIni old lines without the key: SetupDone 1", 1, AppSettings.FromIni(new[] { "MinimumPrice=33" }).SetupDone);
                // QuoteName: a new website visitor gets the empty default; old INI text without the key keeps "Gettic";
                // an empty key stays empty; "QuoteName=" in the file and a missing key are told apart
                True("FromIni(null) (website new visitor): QuoteName empty", AppSettings.FromIni(null).QuoteName == "");
                True("FromIni(empty list): QuoteName Gettic (text exists)", AppSettings.FromIni(new string[0]).QuoteName == "Gettic");
                True("FromIni old lines without QuoteName: Gettic", AppSettings.FromIni(new[] { "MinimumPrice=33" }).QuoteName == "Gettic");
                True("FromIni QuoteName= stays empty", AppSettings.FromIni(new[] { "QuoteName=" }).QuoteName == "");
                True("FromIni QuoteName=Lene kept", AppSettings.FromIni(new[] { "QuoteName=Lene" }).QuoteName == "Lene");
                True("FromIni(new AppSettings().ToIni()): empty name round trip",
                     AppSettings.FromIni(new AppSettings().ToIni()).QuoteName == "");
                System.IO.File.WriteAllText(testFile, "");
                True("empty settings.ini (exists): QuoteName Gettic", AppSettings.Load().QuoteName == "Gettic");
                // a backup's settings without the key (History.Combine reads them with FromIni) also keep "Gettic"
                var oldBackup = History.Combine(new List<QuoteRecord>(), new AppSettings(),
                    new BackupData { Quotes = new List<QuoteRecord>(), Settings = new[] { "FriendsMarkupPct=30" } }, false, true);
                True("restored old backup settings without QuoteName: Gettic", oldBackup.Settings != null && oldBackup.Settings.QuoteName == "Gettic");
                System.IO.File.WriteAllText(testFile, "QuoteValidDays=0\r\nQuotePrice=boss\r\nQuoteLook=7\r\n");
                var bq = AppSettings.Load();
                True("bad quote values -> defaults (days 0 -> 14, price -> customer), QuoteLook 7 -> 1",
                     bq.QuoteValidDays == 14 && bq.QuotePrice == "customer" && bq.QuoteDark == 1);
                System.IO.File.WriteAllText(testFile, "QuoteValidDays=7.6\r\n");
                Eq("QuoteValidDays 7,6 -> 8 whole days", 8, AppSettings.Load().QuoteValidDays);
                // 5a2. invoice settings
                new AppSettings().Save();
                var ri0 = AppSettings.Load();
                True("invoice defaults in file: 14 days, no MobilePay, no bank", ri0.InvoiceDueDays == 14 && ri0.PayMobilePay == "" && ri0.PayBank == "");
                new AppSettings { InvoiceDueDays = 30, PayMobilePay = "12 34 56 78", PayBank = "Reg 1234 konto 0012345678", UsedInvoiceNos = "2026-ABCD,2026-K7Q4" }.Save();
                var ri1 = AppSettings.Load();
                True("round trip invoice settings", ri1.InvoiceDueDays == 30 && ri1.PayMobilePay == "12 34 56 78" &&
                     ri1.PayBank == "Reg 1234 konto 0012345678" && ri1.UsedInvoiceNos == "2026-ABCD,2026-K7Q4");
                True("UsedInvoiceNos default empty in file", ri0.UsedInvoiceNos == "");
                System.IO.File.WriteAllText(testFile, "InvoiceDueDays=0\r\n");
                Eq("InvoiceDueDays 0 -> 14", 14, AppSettings.Load().InvoiceDueDays);
                System.IO.File.WriteAllText(testFile, "InvoiceDueDays=366\r\n");
                Eq("InvoiceDueDays 366 -> 14", 14, AppSettings.Load().InvoiceDueDays);
                System.IO.File.WriteAllText(testFile, "InvoiceDueDays=365\r\n");
                Eq("InvoiceDueDays 365 kept", 365, AppSettings.Load().InvoiceDueDays);
                // 5a3. SettingsVersion 2: the v1.8.0 defaults reach users who never changed the old ones
                Eq("new install: SettingsVersion 2", 2, fresh.SettingsVersion);
                Eq("new settings object: SettingsVersion 2", 2, new AppSettings().SettingsVersion);
                // old file (no key) at the old defaults 0 / 0 / 25 -> 50 / 1 / 10; other values kept; version 2 written
                System.IO.File.WriteAllText(testFile, "MinimumPrice=0\r\nFailedPayback=0\r\nFailedPaybackPct=25\r\nFriendsMarkupPct=30\r\n");
                var v1 = AppSettings.Load();
                True("old file, old defaults -> min 50, payback on, 10 %", v1.MinimumPrice == 50 && v1.FailedPayback == 1 && v1.FailedPaybackPct == 10);
                True("old file: other values kept, version 2", v1.FriendsMarkupPct == 30 && v1.SettingsVersion == 2);
                string iniV = System.IO.File.ReadAllText(testFile);
                True("upgrade wrote SettingsVersion=2 and the new values", iniV.Contains("SettingsVersion=2") &&
                     iniV.Contains("MinimumPrice=50") && iniV.Contains("FailedPaybackPct=10") && iniV.Contains("FriendsMarkupPct=30"));
                // old file with custom values: kept (FailedPayback 0 is the old default, so it still turns on)
                System.IO.File.WriteAllText(testFile, "SettingsVersion=1\r\nMinimumPrice=30\r\nFailedPayback=0\r\nFailedPaybackPct=15\r\n");
                var v1c = AppSettings.Load();
                True("old file, custom min 30 / pct 15 kept", v1c.MinimumPrice == 30 && v1c.FailedPaybackPct == 15 && v1c.SettingsVersion == 2);
                Eq("old file: FailedPayback 0 (old default) -> 1", 1, v1c.FailedPayback);
                System.IO.File.WriteAllText(testFile, "MinimumPrice=75\r\nFailedPayback=1\r\nFailedPaybackPct=40\r\n");
                var v1d = AppSettings.Load();
                True("old file, custom 75 / on / 40 kept", v1d.MinimumPrice == 75 && v1d.FailedPayback == 1 && v1d.FailedPaybackPct == 40);
                // version 2 file: 0 / 0 / 25 are the user's own choices after the update -> kept, file not rewritten
                System.IO.File.WriteAllText(testFile, "SettingsVersion=2\r\nMinimumPrice=0\r\nFailedPayback=0\r\nFailedPaybackPct=25\r\n");
                var v2 = AppSettings.Load();
                True("version 2 file: min 0, payback off, 25 % kept", v2.MinimumPrice == 0 && v2.FailedPayback == 0 && v2.FailedPaybackPct == 25);
                True("version 2 file not rewritten by Load", System.IO.File.ReadAllText(testFile) ==
                     "SettingsVersion=2\r\nMinimumPrice=0\r\nFailedPayback=0\r\nFailedPaybackPct=25\r\n");
                // the upgrade runs once: loading the upgraded file again, then the user sets min 0 -> stays 0
                System.IO.File.WriteAllText(testFile, "MinimumPrice=0\r\n");
                var once = AppSettings.Load();
                once.MinimumPrice = 0;
                once.Save();
                Eq("after the upgrade the user's min 0 stays 0", 0, AppSettings.Load().MinimumPrice);
                // round trip
                new AppSettings { MinimumPrice = 20, FailedPayback = 0, FailedPaybackPct = 25 }.Save();
                var rv = AppSettings.Load();
                True("round trip: version 2, 20 / off / 25 kept", rv.SettingsVersion == 2 && rv.MinimumPrice == 20 &&
                     rv.FailedPayback == 0 && rv.FailedPaybackPct == 25);
                // 5a4. ToIni / FromIni (website): Save writes exactly ToIni, FromIni reads what Load reads
                var ini1 = new AppSettings { FriendsMarkupPct = 37, MinimumPrice = 20, QuoteName = "Gettic 3D = Print", Language = "da" };
                ini1.RememberPrice("Bambu PLA Matte", 200);
                ini1.AddPrinter("Garage P1S", Wear.ModelP1S);
                ini1.Save();
                string[] iniLines = ini1.ToIni();
                True("Save writes exactly ToIni()", string.Join("\n", System.IO.File.ReadAllLines(testFile)) == string.Join("\n", iniLines));
                bool upg;
                var fi = AppSettings.FromIni(iniLines, out upg);
                True("FromIni(ToIni) round trip: same lines, not upgraded", !upg && string.Join("\n", fi.ToIni()) == string.Join("\n", iniLines));
                True("FromIni = Load", string.Join("\n", AppSettings.Load().ToIni()) == string.Join("\n", iniLines));
                True("FromIni values: 37 %, min 20, name, da, 2 printers, PLA Matte 200", fi.FriendsMarkupPct == 37 && fi.MinimumPrice == 20
                     && fi.QuoteName == "Gettic 3D = Print" && fi.Language == "da" && fi.PrinterCount == 2 && fi.PriceFor("Bambu PLA Matte") == 200);
                // text split on \n keeps \r at the line ends (browser storage): still read
                var fr = AppSettings.FromIni(string.Join("\r\n", iniLines).Split('\n'));
                True("FromIni with \\r line ends", string.Join("\n", fr.ToIni()) == string.Join("\n", iniLines));
                var fnull = AppSettings.FromIni(null, out upg);
                True("FromIni(null) = new install defaults, version 2, not upgraded", !upg && fnull.SettingsVersion == 2
                     && string.Join("\n", fnull.ToIni()) == string.Join("\n", new AppSettings().ToIni()));
                var fo = AppSettings.FromIni(new[] { "MinimumPrice=0", "FailedPayback=0", "FailedPaybackPct=25" }, out upg);
                True("FromIni old file -> upgraded (min 50, payback on, 10 %)", upg && fo.SettingsVersion == 2 && fo.MinimumPrice == 50
                     && fo.FailedPayback == 1 && fo.FailedPaybackPct == 10);
                var f2 = AppSettings.FromIni(new[] { "SettingsVersion=2", "MinimumPrice=0" }, out upg);
                True("FromIni version 2 file: not upgraded, min 0 kept", !upg && f2.MinimumPrice == 0);
                var fz = AppSettings.FromIni(new[] { "FriendsMarkupPct=12", null, "MinimumPrice=33" });
                True("FromIni with a null line: skipped, rest read", fz.FriendsMarkupPct == 12 && fz.MinimumPrice == 33 && fz.PrinterCount == 1);
                // preset names: AppSettings' own copy of BambuSlicer.ShortName gives the same short names
                foreach (var pn in new[] { "0.20mm Standard @BBL A1", "Bambu PLA Basic @BBL A1M 0.2 nozzle", "0.20mm Standard @BBL X1C",
                                           "Bambu PLA Matte", "My @BBLx", "  Spaced  @BBL P1S" })
                {
                    var fp = AppSettings.FromIni(new[] { "ProcessPreset=" + pn, "FilamentPreset=" + pn });
                    string want = BambuSlicer.ShortName(pn.Trim());
                    True("preset short name same as BambuSlicer.ShortName: " + pn, fp.ProcessPreset == want && fp.FilamentPreset == want);
                }
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
            QuoteTests();
            InvoiceTests();
            PrinterTests();
            SlicerPrinterTests();
            SlicedProjectTests();
            BackupTests();
            HistoryPartsTests();
            OnlineBackupTests();
            MoneyRoundingTests();
            GcodeTests();
            MultipartTests();
            SliceXmlTests();
            PictureTests();
            HistoryDeleteTests();
            SetupApplyTests();
            RecordBillTests();
            PrintStateTests();
            StatusFilterTests();
            ScaleTests();
            ScaleXyzTests();
            ScaleSlicerTests();
            ScaleFileTests();
            TowerHistoryTests();
            TowerSlicerTests();
            GcodeThumbnailTests();
            InvoiceListTests();
            PaidTests();
            CombinedBillTests();
            ReviewFixTests();
            SettingsSafeTests();
            OnlineHelperTests();
            BatchTests();

            Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        // ---------- whole-app review fixes (2026-11-20): numbers, øre split, payback fold, Trim, texts ----------

        static QuoteLine Ln(string key, long ore)
        {
            return new QuoteLine { Key = key, Ore = ore, Exact = ore / 100.0 };
        }

        static string Keys(List<QuoteLine> lines)
        {
            var k = new List<string>();
            foreach (var l in lines) k.Add(l.Key + ":" + l.Ore);
            return string.Join(",", k.ToArray());
        }

        static void ReviewFixTests()
        {
            double v;
            // A2: Num.TryParse refuses NaN, infinity and 1e7 or more
            True("TryParse 9999999,99 ok", Num.TryParse("9999999,99", out v) && Math.Abs(v - 9999999.99) < 1e-6);
            True("TryParse 10000000 / 1e7 / 1e20 refused", !Num.TryParse("10000000", out v) && !Num.TryParse("1e7", out v) && !Num.TryParse("1e20", out v));
            True("TryParse Infinity / NaN refused", !Num.TryParse("Infinity", out v) && !Num.TryParse("NaN", out v) && !Num.TryParse("-Infinity", out v));
            // a lone dot is a decimal point: "1.200" = 1,2 (not 1200); with a comma after it the dot is a thousands dot
            True("TryParse \"1.200\" -> 1,2", Num.TryParse("1.200", out v) && Math.Abs(v - 1.2) < 1e-12);
            True("TryParse \"1.234,5\" -> 1234,5", Num.TryParse("1.234,5", out v) && Math.Abs(v - 1234.5) < 1e-9);
            // Pricing.Calculate refuses non-finite and too large inputs
            var cs = new AppSettings();
            Func<QuoteInput, bool> throws = inp => { try { Pricing.Calculate(inp, cs); return false; } catch (ArgumentException) { return true; } };
            True("Calculate: 1e7 g refused", throws(new QuoteInput { Hours = 1, FilamentGrams = 1e7, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 2 }));
            True("Calculate: NaN kr/kg refused", throws(new QuoteInput { Hours = 1, FilamentGrams = 10, FilamentDkkPerKg = double.NaN, ElectricityDkkPerKwh = 2 }));
            True("Calculate: infinite kr/kWh refused", throws(new QuoteInput { Hours = 1, FilamentGrams = 10, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = double.PositiveInfinity }));
            True("Calculate: a filament line of 1e20 g refused", throws(new QuoteInput { Hours = 1, ElectricityDkkPerKwh = 2, Filaments = new List<FilamentLine> {
                new FilamentLine { Grams = 10, PricePerKg = 200 }, new FilamentLine { Grams = 1e20, PricePerKg = 200 } } }));
            True("Calculate: NaN debt refused", throws(new QuoteInput { Hours = 1, FilamentGrams = 10, FilamentDkkPerKg = 200, PaybackDebt = double.NaN }));
            True("Calculate: 9.999.999 g still works", !throws(new QuoteInput { Hours = 1, FilamentGrams = 9999999, FilamentDkkPerKg = 1, ElectricityDkkPerKwh = 0 }));
            // ReadIni skips an infinite number: the default 50 stays
            Eq("FromIni MinimumPrice=Infinity -> 50", 50, AppSettings.FromIni(new[] { "SettingsVersion=2", "MinimumPrice=Infinity" }).MinimumPrice);
            Eq("FromIni MinimumPrice=NaN -> 50", 50, AppSettings.FromIni(new[] { "SettingsVersion=2", "MinimumPrice=NaN" }).MinimumPrice);
            bool upg;
            AppSettings.FromIni(new string[0], out upg);
            True("FromIni with no key lines: not upgraded (nothing to store)", !upg);
            AppSettings.FromIni(new[] { "junk", "=x", "\u0001\u0002" }, out upg);
            True("FromIni junk lines: not upgraded", !upg);

            // ShownOre limits (pinned): 9.999.999,99 -> 999.999.999; -0,004 -> 0; NaN and 1e17 (1e19 øre > long) throw
            True("ShownOre 9.999.999,99 = 999.999.999", Pricing.ShownOre(9999999.99) == 999999999L && Pricing.ShownOre(-0.004) == 0);
            Func<double, bool> overflow = x => { try { Pricing.ShownOre(x); return false; } catch (OverflowException) { return true; } };
            True("ShownOre NaN / 1e17 throw OverflowException", overflow(double.NaN) && overflow(1e17));

            // SplitOre: big leftover {0,01; 0,02} to 10 øre: floors 1 + 2 = 3, 7 left round-robin from the earlier tie
            // (+4 / +3) -> 5 + 5
            var sp = new List<QuoteLine> { new QuoteLine { Exact = 0.01 }, new QuoteLine { Exact = 0.02 } };
            Pricing.SplitOre(sp, 10);
            True("SplitOre big leftover -> 5 + 5", sp[0].Ore == 5 && sp[1].Ore == 5);
            // over the total {0,50; 0,25} to 70 øre: 75 - 70 = 5 taken from the end in turn: 24, 49, 23, 48, 22 -> 48 + 22
            sp = new List<QuoteLine> { new QuoteLine { Exact = 0.5 }, new QuoteLine { Exact = 0.25 } };
            Pricing.SplitOre(sp, 70);
            True("SplitOre over the total -> 48 + 22", sp[0].Ore == 48 && sp[1].Ore == 22);
            sp = new List<QuoteLine> { new QuoteLine { Exact = -1 }, new QuoteLine { Exact = 0.3 } };
            Pricing.SplitOre(sp, 30);
            True("SplitOre negative exact -> 0 øre", sp[0].Ore == 0 && sp[1].Ore == 30);

            // A3: ShareOre by the SHOWN prices: 10,0041 and 10,0049 both show 10,00 -> equal weights 0,5 øre each, floors
            // 0 + 0, 1 left, tie -> the earlier job (exact prices gave it to the second)
            var sh = Pricing.ShareOre(new[] { 10.0041, 10.0049 }, 1);
            True("ShareOre by shown price: {1, 0}", sh[0] == 1 && sh[1] == 0);
            True("ShareOre never throws on huge / NaN prices", Pricing.ShareOre(new[] { 1e20, double.NaN, 5 }, 100).Length == 3);

            // A3: an older invoice keeps its stored top-up shares: 25,01 - 15,01 = 10,00 and 24,99 - 14,99 = 10,00;
            // total 1000 + 1000 + 1501 + 1499 = 5000; JobOre = {1000 + 1501, 1000 + 1499} = {2501, 2499}
            // (re-splitting 30,00 over 10 / 10 would give 2500 / 2500)
            var o1 = new QuoteRecord { Id = "o1", SavedAt = "2026-11-01 10:00:00", InvoiceNo = "2026-22222", BilledAt = "2026-11-02 10:00:00",
                                       OthersPrice = 25.01, FriendsPrice = 10, InvoiceTopUp = 15.01 };
            var o2 = new QuoteRecord { Id = "o2", SavedAt = "2026-11-01 11:00:00", InvoiceNo = "2026-22222", BilledAt = "2026-11-02 10:00:00",
                                       OthersPrice = 24.99, FriendsPrice = 10, InvoiceTopUp = 14.99 };
            var og = History.GroupBill(new[] { o1, o2 }, new AppSettings(), false);
            var ojo = QuoteRows.JobOre(og);
            True("old invoice: Invoiced, total 50,00", og.Invoiced && Pricing.ShownOre(og.Total) == 5000);
            True("old invoice: JobOre = stored 2501 / 2499", ojo[0] == 2501 && ojo[1] == 2499);
            True("live bill: not Invoiced", !History.GroupBill(new[] { BillJob(18, new DateTime(2026, 11, 1)) }, new AppSettings(), false).Invoiced);

            // A3 round trip: 3 jobs saved without rounding, 12,3349 / 12,3441 / 10,0049 kr. (shown 12,33 / 12,34 / 10,00 = 34,67);
            // minimum 50, round 1: 50 - 34,67 = 15,33 -> 16,00 top-up; shares of 1600 by 1233 / 1234 / 1000 (Σ 3467):
            // 569,02 / 569,48 / 461,49 -> floors 1599, 1 left -> ,49 (third) -> 569 / 569 / 462;
            // saved 1233 + 569 = 18,02, 1234 + 569 = 18,03, 1000 + 462 = 14,62 (Σ 50,67)
            var s0 = BillSettings(); s0.RoundTo = 0;
            var rt = new List<QuoteRecord>();
            double[] grams = { 12.3349, 12.3441, 10.0049 };
            for (int i = 0; i < 3; i++)
            {
                var inp = new QuoteInput { Hours = 1, FilamentGrams = grams[i], FilamentDkkPerKg = 1000, ElectricityDkkPerKwh = 0 };
                rt.Add(History.FromQuote(inp, Pricing.Calculate(inp, s0), s0, "RT" + i, "", new DateTime(2026, 11, 10, 9, i, 0)));
            }
            var rs = new AppSettings { MinimumPrice = 50, RoundTo = 1 };
            var pre = QuoteRows.JobOre(History.GroupBill(rt, rs, false));
            True("round trip preview 1802 / 1803 / 1462", pre[0] == 1802 && pre[1] == 1803 && pre[2] == 1462);
            History.MakeInvoice(new List<QuoteRecord>(rt), rt, new DateTime(2026, 11, 10, 12, 0, 0), rs, false, Digits("33333"));
            var after = History.GroupBill(rt, rs, false);
            var post = QuoteRows.JobOre(after);
            bool same = true;
            for (int i = 0; i < 3; i++) same &= post[i] == pre[i] && post[i] == Pricing.ShownOre(rt[i].OthersPrice);
            True("round trip: saved = preview = re-opened (18,02 / 18,03 / 14,62)", same && after.Invoiced);
            True("round trip: jobs add up to the total 50,67", post[0] + post[1] + post[2] == 5067 && Pricing.ShownOre(after.Total) == 5067);

            // 4: payback and minimum fold into Work & handling: {mat 10, pay 3, work 5, min 2} -> work 5 + 3 + 2 = 10
            var f1 = QuoteRows.FoldMinimum(new List<QuoteLine> { Ln(QuoteLine.Material, 1000), Ln(QuoteLine.Payback, 300), Ln(QuoteLine.Work, 500), Ln(QuoteLine.Minimum, 200) });
            True("fold payback + minimum into work: " + Keys(f1), Keys(f1) == "material:1000,work:1000");
            // no work line: one in the first folded line's place, 0,30 + 0,50 = 0,80
            var f2 = QuoteRows.FoldMinimum(new List<QuoteLine> { Ln(QuoteLine.Material, 100), Ln(QuoteLine.Payback, 30), Ln(QuoteLine.Minimum, 50), Ln(QuoteLine.Rounding, 20) });
            True("fold without a work line: " + Keys(f2), Keys(f2) == "material:100,work:80,rounding:20");
            var f3 = QuoteRows.FoldMinimum(new List<QuoteLine> { Ln(QuoteLine.Material, 100), Ln(QuoteLine.Minimum, 50) });
            True("fold minimum only, no work line: " + Keys(f3), Keys(f3) == "material:100,work:50");
            // the payback example (60,37): payback 5,67 + work 18,73 = 24,40 on one Work & handling row, no payback row
            var ps = PaySettings(2); ps.AutoWearRate = 0;
            var bp = Pricing.Breakdown(Owing(33.70), ps, false, null);
            var prow = QuoteRows.Build(bp, null, 0, 5, 30, "");
            QuoteRow workRow = null;
            bool payRow = false;
            long heads = 0;
            foreach (var r in prow)
            {
                if (r.Kind != QuoteRowKind.Head) continue;
                if (r.Label == "Work & handling") workRow = r;
                if (r.Label.IndexOf("failed prints", StringComparison.OrdinalIgnoreCase) >= 0) payRow = true;
                double a;
                Num.TryParse(r.Amount.Replace(" kr.", "").Replace(".", ""), out a);
                heads += (long)Math.Round(a * 100);
            }
            True("bill: no payback row, Work & handling 24,40 kr.", !payRow && workRow != null && workRow.Amount == Num.Kr(24.40));
            True("bill: rows add up to 60,37", heads == 6037);
            // combined bill rows: payback folded too; 8 colours with at most 6 rows -> "3 more colours" / "3 flere farver"
            var gl = new List<QuoteLine>();
            for (int i = 0; i < 8; i++) gl.Add(new QuoteLine { Key = QuoteLine.Material, Index = i, Ore = 100, Exact = 1, Quantity = 10 });
            gl.Add(Ln(QuoteLine.Power, 50)); gl.Add(Ln(QuoteLine.Payback, 70)); gl.Add(Ln(QuoteLine.Work, 200)); gl.Add(Ln(QuoteLine.Other, 30));
            var gb = new QuoteBreakdown { Lines = gl, TotalOre = 1150 };
            var grows = QuoteRows.BuildGroup(gb, null, 2, 0, "", true, 6);
            bool gPay = false, gMore = false, gOther = false; string gWork = "";
            foreach (var r in grows)
            {
                if (r.Label.IndexOf("failed prints", StringComparison.OrdinalIgnoreCase) >= 0) gPay = true;
                if (r.Label == "3 more colours") gMore = true;
                if (r.Label == "Other") gOther = true;
                if (r.Label == "Work & handling") gWork = r.Amount;
            }
            True("combined rows: no payback row, work 2,00 + 0,70 = 2,70, \"Other\", \"3 more colours\"", !gPay && gWork == Num.Kr(2.70) && gOther && gMore);
            L_Danish(true);
            try
            {
                bool da = false;
                foreach (var r in QuoteRows.BuildGroup(gb, null, 2, 0, "", true, 6)) if (r.Label == "3 flere farver") da = true;
                True("Danish: \"3 flere farver\"", da);
                True("Danish backup code text", Backup.InvalidCodeText() == "Det er ikke en gyldig kode til kopien.");
            }
            finally { L_Danish(false); }

            // 5: Trim keeps invoiced records beyond max (their numbers must stay taken): 4 records, max 2, the oldest
            // but one invoiced -> kept; the oldest (plain) dropped
            var tl = new List<QuoteRecord>();
            for (int i = 0; i < 4; i++) tl.Add(HistRecord(new DateTime(2026, 11, 10, 12, 0, 0).AddHours(-i)));
            tl[2].InvoiceNo = "2026-44444";
            History.Trim(tl, 2);
            True("Trim keeps the invoiced record beyond max", tl.Count == 3 && tl[2].InvoiceNo == "2026-44444");

            // 5: NextInvoiceNo with every 5-digit try taken (pick always 0 -> 10000, used): tries 1-999 at 5 digits,
            // try 1000 at 6 digits = 100000
            True("NextInvoiceNo full: 2026-100000", History.NextInvoiceNo(null, 2026, "2026-10000", n => 0) == "2026-100000");
        }

        static void L_Danish(bool on) { PrintPrice.L.Danish = on; }

        // A1: settings.ini is written safely and never overwritten with defaults after a failed read
        static void SettingsSafeTests()
        {
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-settings-safe");
            string file = System.IO.Path.Combine(dir, "settings.ini");
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                foreach (var f in System.IO.Directory.GetFiles(dir)) System.IO.File.Delete(f);
                True("UseFolder test-settings-safe", AppSettings.UseFolder(dir));

                // Save via .tmp: file written, no .tmp left; a second Save replaces it
                new AppSettings { FriendsMarkupPct = 33 }.Save();
                new AppSettings { FriendsMarkupPct = 34 }.Save();
                True("Save: file there, no .tmp left", System.IO.File.Exists(file) && !System.IO.File.Exists(file + ".tmp"));
                var l1 = AppSettings.Load();
                True("Save/Load round trip 34, no problem, not blocked", l1.FriendsMarkupPct == 34 && AppSettings.LoadProblem == null && !AppSettings.SaveBlocked);

                // locked file: defaults in memory, saving blocked, the file keeps its bytes
                System.IO.File.WriteAllText(file, "SettingsVersion=2\r\nFriendsMarkupPct=41\r\nMinimumPrice=0\r\n");
                byte[] orig = System.IO.File.ReadAllBytes(file);
                AppSettings locked;
                using (new System.IO.FileStream(file, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                {
                    locked = AppSettings.Load();
                }
                True("locked: defaults, SaveBlocked, a problem text", locked.FriendsMarkupPct == 25 && AppSettings.SaveBlocked && AppSettings.LoadProblem != null);
                string lp = AppSettings.LoadProblem ?? "";
                True("locked: short 'in use' text, no path or .NET message", lp.StartsWith("settings.ini ") && lp.IndexOf('\\') < 0
                     && lp.IndexOf(dir, StringComparison.OrdinalIgnoreCase) < 0 && lp == AppSettings.OpenProblemText(new System.IO.IOException("x")));
                string noAccess = AppSettings.OpenProblemText(new UnauthorizedAccessException(@"Access to the path 'C:\Users\Someone\settings.ini' is denied."));
                True("no access: plain text without the path", noAccess.StartsWith("settings.ini ") && noAccess.IndexOf('\\') < 0 && noAccess != lp);
                locked.MinimumPrice = 50;
                locked.Save();
                True("locked: Save after the read failed leaves the file alone", Same(orig, System.IO.File.ReadAllBytes(file)));
                var back = AppSettings.Load();
                True("next Load reads it again (41, min 0), not blocked", back.FriendsMarkupPct == 41 && back.MinimumPrice == 0 && !AppSettings.SaveBlocked);

                // empty file / junk: kept as settings.bad-*.ini (same bytes), defaults, no upgrade save, then Save works
                foreach (string junk in new[] { "", "junk without keys\r\nÿþ=" })
                {
                    foreach (var f in System.IO.Directory.GetFiles(dir, "settings.bad-*.ini")) System.IO.File.Delete(f);
                    System.IO.File.WriteAllText(file, junk);
                    byte[] jb = System.IO.File.ReadAllBytes(file);
                    var e = AppSettings.Load();
                    var bads = System.IO.Directory.GetFiles(dir, "settings.bad-*.ini");
                    string nm = junk.Length == 0 ? "empty file" : "junk file";
                    True(nm + ": kept as settings.bad-*.ini, no settings.ini, not blocked", bads.Length == 1 && Same(jb, System.IO.File.ReadAllBytes(bads[0]))
                         && !System.IO.File.Exists(file) && !AppSettings.SaveBlocked && AppSettings.LoadProblem != null);
                    True(nm + ": defaults, existing user (SetupDone 1, Gettic), version 2", e.SetupDone == 1 && e.QuoteName == "Gettic"
                         && e.MinimumPrice == 50 && e.SettingsVersion == 2);
                    e.MinimumPrice = 0;
                    e.Save();
                    Eq(nm + ": the user's min 0 after Save stays 0", 0, AppSettings.Load().MinimumPrice);
                }
                // a file with one key is a settings file (read as usual)
                System.IO.File.WriteAllText(file, "SettingsVersion=2\r\n");
                AppSettings.Load();
                True("one key line: read, no bad copy", System.IO.File.Exists(file) && AppSettings.LoadProblem == null);

                // Restore while settings.ini couldn't be read: history only -> settings.ini untouched + a message
                System.IO.File.WriteAllText(file, "SettingsVersion=2\r\nFriendsMarkupPct=41\r\n");
                orig = System.IO.File.ReadAllBytes(file);
                History.ResetBlock();
                AppSettings.SetSettingsBlock();
                RestoreResult rr;
                var bd = new BackupData { Quotes = new List<QuoteRecord>() };
                string msg = History.Restore(new List<QuoteRecord>(), new AppSettings(), bd, false, false, DateTime.Now, out rr);
                True("Restore history only while blocked: message, settings.ini unchanged", msg != null && rr != null && Same(orig, System.IO.File.ReadAllBytes(file)));
                AppSettings.ResetSettingsBlock();
            }
            finally
            {
                AppSettings.ResetSettingsBlock();
                AppSettings.ClearTestFolder();
                try { System.IO.Directory.Delete(dir, true); } catch { }
            }
        }

        // ---------- online's pure helpers (review 2026-11-20): job folder sweep, quoting, update swap, safe links, replies,
        // downloads (a local 127.0.0.1 server only, never a real one) ----------

        static bool Throws<T>(Action a) where T : Exception
        {
            try { a(); return false; }
            catch (T) { return true; }
            catch (Exception) { return false; }
        }

        static Dictionary<string, object> JsonObj(string json)
        {
            return Json.Deserialize<Dictionary<string, object>>(json);
        }

        static void OnlineHelperTests()
        {
            // job folder sweep: 32 hex digits (any case), at least minAge old; future-dated, young and other names kept
            var now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
            string hex = "0123456789abcdef0123456789abcdef";
            Func<string, double, KeyValuePair<string, DateTime>> F = (n, hoursAgo) =>
                new KeyValuePair<string, DateTime>(@"C:\t\" + n, now.AddHours(-hoursAgo));
            var folders = new List<KeyValuePair<string, DateTime>>
            {
                F(hex, 48), F(hex.ToUpperInvariant(), 48), F("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1), F("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 24),
                F("cccccccccccccccccccccccccccccccc", -5), F(hex.Substring(1), 48), F(hex + "0", 48), F("studio", 48),
                F("gggggggggggggggggggggggggggggggg", 48), new KeyValuePair<string, DateTime>(@"C:\t\dddddddddddddddddddddddddddddddd\", now.AddDays(-3))
            };
            var old = BambuSlicer.OldJobFolders(folders, now, TimeSpan.FromDays(1));
            True("OldJobFolders: 2-day hex (both cases), exactly 24 h, trailing slash; not young / future / 31 / 33 / studio / non-hex",
                 old.Count == 4 && old.Contains(@"C:\t\" + hex) && old.Contains(@"C:\t\" + hex.ToUpperInvariant())
                 && old.Contains(@"C:\t\bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb") && old.Contains(@"C:\t\dddddddddddddddddddddddddddddddd\"));
            True("OldJobFolders null -> empty", BambuSlicer.OldJobFolders(null, now, TimeSpan.FromDays(1)).Count == 0);

            // CleanJobFolders on a test folder: only the old job folder goes
            string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-jobfolders");
            try
            {
                if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
                string oldJob = System.IO.Path.Combine(root, hex), young = System.IO.Path.Combine(root, "ffffffffffffffffffffffffffffffff"),
                       studio = System.IO.Path.Combine(root, "studio");
                foreach (var d in new[] { oldJob, young, studio })
                {
                    System.IO.Directory.CreateDirectory(d);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(d, "x.txt"), "x");
                }
                foreach (var d in new[] { oldJob, studio })
                {
                    System.IO.Directory.SetCreationTimeUtc(d, DateTime.UtcNow.AddDays(-3));
                    System.IO.Directory.SetLastWriteTimeUtc(d, DateTime.UtcNow.AddDays(-3));
                }
                int gone = BambuSlicer.CleanJobFolders(root);
                True("CleanJobFolders: 1 old job folder gone, young + studio kept", gone == 1 && !System.IO.Directory.Exists(oldJob)
                     && System.IO.Directory.Exists(young) && System.IO.Directory.Exists(studio));
                True("CleanJobFolders missing / null root -> 0", BambuSlicer.CleanJobFolders(System.IO.Path.Combine(root, "none")) == 0
                     && BambuSlicer.CleanJobFolders(null) == 0);
            }
            finally { try { System.IO.Directory.Delete(root, true); } catch { } }

            // QuoteArg (Windows' CommandLineToArgvW rules)
            True("QuoteArg plain path", BambuSlicer.QuoteArg(@"C:\a b\x.stl") == "\"C:\\a b\\x.stl\"");
            True("QuoteArg trailing backslash doubled", BambuSlicer.QuoteArg(@"C:\a b\") == "\"C:\\a b\\\\\"");
            True("QuoteArg quote -> \\\"", BambuSlicer.QuoteArg("say \"hi\"") == "\"say \\\"hi\\\"\"");
            True("QuoteArg backslash before a quote -> 3 + quote", BambuSlicer.QuoteArg("a\\\"b") == "\"a\\\\\\\"b\"");
            True("QuoteArg null -> \"\"", BambuSlicer.QuoteArg(null) == "\"\"");

            // update swap on plain text files (never an exe)
            string sw = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-swap");
            try
            {
                if (System.IO.Directory.Exists(sw)) System.IO.Directory.Delete(sw, true);
                System.IO.Directory.CreateDirectory(sw);
                string exe = System.IO.Path.Combine(sw, "app.txt"), dl = System.IO.Path.Combine(sw, "app.txt.download");
                Action reset = () =>
                {
                    foreach (var f in System.IO.Directory.GetFiles(sw)) System.IO.File.Delete(f);
                    System.IO.File.WriteAllText(exe, "old");
                    System.IO.File.WriteAllText(dl, "new");
                };
                Func<string, string> read = p => System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : "(none)";

                reset();
                string started = null;
                Updater.Swap(exe, dl, p => started = p);
                True("Swap ok: new in place, old as .old, started", read(exe) == "new" && read(exe + ".old") == "old" && !System.IO.File.Exists(dl) && started == exe);

                reset();
                UpdateException ue = null;
                try { Updater.Swap(exe, dl, p => { throw new InvalidOperationException("no start"); }); }
                catch (UpdateException ex) { ue = ex; }
                True("Swap start fails: old program back, not Installed, download removed", ue != null && !ue.Installed && read(exe) == "old"
                     && !System.IO.File.Exists(exe + ".old") && !System.IO.File.Exists(dl));

                reset();
                ue = null;
                try { Updater.Swap(exe, dl, p => { System.IO.File.Delete(p); throw new InvalidOperationException("removed"); }); }
                catch (UpdateException ex) { ue = ex; }
                True("Swap new file removed (antivirus): old program back, not Installed", ue != null && !ue.Installed && read(exe) == "old");

                reset();
                ue = null;
                System.IO.FileStream hold = null;
                try
                {
                    // the .old file is held open, so the old program can't be put back: the new one stays, Installed
                    try { Updater.Swap(exe, dl, p => { hold = new System.IO.FileStream(p + ".old", System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None); throw new InvalidOperationException("no start"); }); }
                    catch (UpdateException ex) { ue = ex; }
                }
                finally { if (hold != null) hold.Dispose(); }
                True("Swap can't roll back: Installed, new program in place", ue != null && ue.Installed && read(exe) == "new");
            }
            finally { try { System.IO.Directory.Delete(sw, true); } catch { } }

            // safe release page links
            string rp = Updater.ReleasesPage;
            True("SafePageUrl: our https release page kept", Updater.SafePageUrl("https://github.com/gettic/PrintPrice/releases/tag/v1.23.0") == "https://github.com/gettic/PrintPrice/releases/tag/v1.23.0");
            True("SafePageUrl: http / other host / other repo / user info / port / javascript / null -> releases page",
                 Updater.SafePageUrl("http://github.com/gettic/PrintPrice/releases") == rp && Updater.SafePageUrl("https://evil.example/gettic/PrintPrice/x") == rp
                 && Updater.SafePageUrl("https://github.com/other/repo/releases") == rp && Updater.SafePageUrl("https://u@github.com/gettic/PrintPrice/x") == rp
                 && Updater.SafePageUrl("https://github.com:8443/gettic/PrintPrice/x") == rp && Updater.SafePageUrl("javascript:alert(1)") == rp
                 && Updater.SafePageUrl(null) == rp && Updater.SafePageUrl("https://github.com/gettic/PrintPriceX/x") == rp);
            var rel = Updater.ParseRelease("{\"tag_name\":\"v9.9.9\",\"name\":\"x\",\"html_url\":\"file:///C:/evil\",\"assets\":[{\"name\":\"PrintPrice.exe\",\"browser_download_url\":\"https://github.com/gettic/PrintPrice/releases/download/v9.9.9/PrintPrice.exe\",\"size\":10}]}");
            True("ParseRelease odd html_url -> releases page", rel != null && rel.PageUrl == rp);

            // Printables replies
            True("ParseReply HTML -> SliceException", Throws<SliceException>(() => Printables.ParseReply("<html>502 Bad Gateway</html>")));
            True("ParseReply null / empty -> SliceException", Throws<SliceException>(() => Printables.ParseReply(null)) && Throws<SliceException>(() => Printables.ParseReply("")));
            True("ParseReply errors without data -> SliceException", Throws<SliceException>(() => Printables.ParseReply("{\"errors\":[{\"message\":\"x\"}]}"))
                 && Throws<SliceException>(() => Printables.ParseReply("{\"data\":null}")) && Throws<SliceException>(() => Printables.ParseReply("[1,2]")));
            var pd = Printables.ParseReply("{\"data\":{\"print\":{\"id\":\"5\",\"name\":\"Benchy\",\"stls\":[{\"id\":\"11\",\"name\":\"a.stl\",\"fileSize\":\"123\"},{\"id\":\"\",\"name\":\"b.stl\"},{\"id\":\"12\"},\"odd\",{\"id\":\"13\",\"name\":\"c.stl\",\"fileSize\":\"x\"}]}}}");
            var pm = Printables.ModelFromData("5", pd);
            True("ModelFromData: name, 2 files (no id / no name / odd skipped), size 123 and 0", pm.Name == "Benchy" && pm.Files.Count == 2
                 && pm.Files[0].Id == "11" && pm.Files[0].Size == 123 && pm.Files[1].Id == "13" && pm.Files[1].Size == 0);
            True("ModelFromData print null / missing / null data -> SliceException",
                 Throws<SliceException>(() => Printables.ModelFromData("5", Printables.ParseReply("{\"data\":{\"print\":null}}")))
                 && Throws<SliceException>(() => Printables.ModelFromData("5", JsonObj("{\"x\":1}"))) && Throws<SliceException>(() => Printables.ModelFromData("5", null)));
            True("ModelFromData stls not a list -> no files", Printables.ModelFromData("5", JsonObj("{\"print\":{\"name\":\"n\",\"stls\":\"text\"}}")).Files.Count == 0);
            Func<string, string> link = l => Printables.LinkFromData(JsonObj("{\"getDownloadLink\":{\"ok\":true,\"output\":" + l + "}}"));
            True("LinkFromData https kept", link("{\"link\":\"https://files.printables.com/x.stl\"}") == "https://files.printables.com/x.stl");
            True("LinkFromData http / file / relative / output null / no link -> null", link("{\"link\":\"http://files.printables.com/x.stl\"}") == null
                 && link("{\"link\":\"file:///C:/x\"}") == null && link("{\"link\":\"/x.stl\"}") == null && link("null") == null && link("{}") == null
                 && Printables.LinkFromData(null) == null);

            // PowerPrice.PickCurrent: HTML -> SliceException; records without a time, with a null or text price skipped
            var pn = new DateTime(2026, 11, 20, 10, 20, 0, DateTimeKind.Utc);
            True("PickCurrent HTML / null / no records -> SliceException", Throws<SliceException>(() => PowerPrice.PickCurrent("<html></html>", "DK1", pn))
                 && Throws<SliceException>(() => PowerPrice.PickCurrent(null, "DK1", pn)) && Throws<SliceException>(() => PowerPrice.PickCurrent("{\"x\":1}", "DK1", pn))
                 && Throws<SliceException>(() => PowerPrice.PickCurrent("{\"records\":5}", "DK1", pn)));
            var pq = PowerPrice.PickCurrent("{\"records\":[{\"TimeUTC\":\"2026-11-20T10:00:00\",\"DayAheadPriceDKK\":800.5}," +
                "{\"TimeUTC\":\"2026-11-20T10:15:00\",\"DayAheadPriceDKK\":null},{\"DayAheadPriceDKK\":999},{\"TimeUTC\":null,\"DayAheadPriceDKK\":999}," +
                "{\"TimeUTC\":\"2026-11-20T10:16:00\",\"DayAheadPriceDKK\":\"abc\"},{\"TimeUTC\":\"2026-11-20T10:30:00\",\"DayAheadPriceDKK\":5000},5,null]}", "DK1", pn);
            // the 10:15 / 10:16 slots have no usable price and 10:30 hasn't started: the 10:00 slot, 800,5 kr/MWh = 0,8005 kr/kWh
            True("PickCurrent skips bad records: 10:00 slot, 0,8005 kr/kWh", pq != null && pq.SlotStartUtc == new DateTime(2026, 11, 20, 10, 0, 0)
                 && Math.Abs(pq.SpotDkkPerKwh - 0.8005) < 1e-12 && pq.Area == "DK1");
            True("PickCurrent all bad / only future / older than 2 h -> SliceException",
                 Throws<SliceException>(() => PowerPrice.PickCurrent("{\"records\":[{\"TimeUTC\":\"2026-11-20T10:00:00\",\"DayAheadPriceDKK\":null}]}", "DK1", pn))
                 && Throws<SliceException>(() => PowerPrice.PickCurrent("{\"records\":[{\"TimeUTC\":\"2026-11-20T11:00:00\",\"DayAheadPriceDKK\":1}]}", "DK1", pn))
                 && Throws<SliceException>(() => PowerPrice.PickCurrent("{\"records\":[{\"TimeUTC\":\"2026-11-20T08:00:00\",\"DayAheadPriceDKK\":1}]}", "DK1", pn)));

            // online backup: a timed-out upload has its own text; other failures the general one
            string slow = OnlineBackup.FailureText(System.Net.WebExceptionStatus.Timeout, true);
            True("FailureText: timed-out PUT = upload text; GET timeout / other PUT failure = general",
                 slow.Contains("upload") && OnlineBackup.FailureText(System.Net.WebExceptionStatus.Timeout, false) != slow
                 && OnlineBackup.FailureText(System.Net.WebExceptionStatus.ConnectFailure, true) != slow);

            NetRunTests();
        }

        // Net.Run / ReadCapped / AllowedAddress against a local 127.0.0.1 server (TinyServer): quick, no real servers
        static void NetRunTests()
        {
            bool was = Net.AllowLoopbackHttp;
            try
            {
                Net.AllowLoopbackHttp = false;
                True("AllowedAddress: https yes; loopback http only when allowed; file / other http no",
                     Net.AllowedAddress(new Uri("https://example.com/x")) && !Net.AllowedAddress(new Uri("http://127.0.0.1:5/x"))
                     && !Net.AllowedAddress(new Uri("file:///C:/x")) && !Net.AllowedAddress(null));
                Net.AllowLoopbackHttp = true;
                True("AllowedAddress with AllowLoopbackHttp: 127.0.0.1 http yes, localhost / other http no",
                     Net.AllowedAddress(new Uri("http://127.0.0.1:5/x")) && !Net.AllowedAddress(new Uri("http://localhost:5/x"))
                     && !Net.AllowedAddress(new Uri("http://example.com/x")));

                // refused before any connection is made
                using (var wc = Net.Client("test"))
                {
                    True("Net.Client refuses file://", Throws<System.Net.WebException>(() => wc.DownloadData("file:///C:/Windows/win.ini")));
                    True("Net.Client refuses non-loopback http", Throws<System.Net.WebException>(() => wc.DownloadData("http://192.0.2.1/x")));
                }

                using (var srv = new TinyServer())
                {
                    Func<string, long, CancellationTokenSourceBox, int, byte[]> get = (path, cap, cts, stall) =>
                    {
                        using (var wc = Net.Client("test"))
                        {
                            wc.Proxy = null;
                            System.Threading.Tasks.Task<byte[]> call = null;
                            Net.Run(wc, () => call = wc.DownloadDataTaskAsync(new Uri(srv.Url + path)),
                                    cts != null ? cts.Source.Token : System.Threading.CancellationToken.None, stall, cap);
                            return call.Result;
                        }
                    };
                    byte[] ok = get("/small", 1000, null, 5000);
                    True("Net.Run small reply read", ok != null && System.Text.Encoding.ASCII.GetString(ok) == "hello");
                    True("Net.Run over the cap -> InvalidDataException", Throws<System.IO.InvalidDataException>(() => get("/big", 1000, null, 5000)));
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    True("Net.Run no data -> TimeoutException", Throws<TimeoutException>(() => get("/stall", 1000, null, 400)));
                    True("Net.Run stall noticed quickly (< 3 s)", sw.ElapsedMilliseconds < 3000);
                    var box = new CancellationTokenSourceBox();
                    box.Source.CancelAfter(300);
                    True("Net.Run cancel -> OperationCanceledException", Throws<OperationCanceledException>(() => get("/stall", 1000, box, 10000)));
                }

                // ReadCapped: text read (BOM dropped), over the cap -> InvalidDataException
                var bom = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };
                True("ReadCapped reads text, drops the BOM", Net.ReadCapped(new System.IO.MemoryStream(bom), 10) == "hi");
                True("ReadCapped over the cap -> InvalidDataException", Throws<System.IO.InvalidDataException>(() => Net.ReadCapped(new System.IO.MemoryStream(new byte[100]), 50)));
            }
            finally { Net.AllowLoopbackHttp = was; }
        }

        sealed class CancellationTokenSourceBox
        {
            public readonly System.Threading.CancellationTokenSource Source = new System.Threading.CancellationTokenSource();
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ---------- combined bill: several History jobs on one invoice, minimum price once on the whole bill ----------

        // simple settings: price = material only (1000 kr./kg, so grams = kr.), minimum 50, rounded up to whole kr.
        static AppSettings BillSettings()
        {
            var s = new AppSettings();
            s.PrinterWatts = 0; s.FriendsMarkupPct = 0; s.OthersMarkupPct = 0; s.MachineRatePerHour = 0; s.AutoWearRate = 0;
            s.FailedPrintPct = 0; s.FailedPayback = 0; s.MinimumPrice = 50; s.RoundTo = 1;
            return s;
        }

        static QuoteRecord BillJob(double grams, DateTime t)
        {
            var s = BillSettings();
            var input = new QuoteInput { Hours = 1, Minutes = 0, FilamentGrams = grams, FilamentDkkPerKg = 1000, ElectricityDkkPerKwh = 0 };
            return History.FromQuote(input, Pricing.Calculate(input, s), s, "Job" + grams, "", t);
        }

        static void CombinedBillTests()
        {
            double top;
            // BillTotal: 30 + 25 + 20 = 75 >= 50 -> no top-up, total 75 (per job today: 50 + 50 + 50 = 150)
            Eq("BillTotal 30+25+20 = 75", 75, Pricing.BillTotal(new[] { 30.0, 25, 20 }, 50, 1, false, out top));
            Eq("BillTotal 30+25+20: no top-up", 0, top);
            // 18 + 14 = 32 < 50 -> top-up 50 - 32 = 18 -> total 50 (today 100)
            Eq("BillTotal 18+14 = 50", 50, Pricing.BillTotal(new[] { 18.0, 14 }, 50, 1, false, out top));
            Eq("BillTotal 18+14: top-up 18", 18, top);
            Eq("BillTotal friends 18+14 = 32", 32, Pricing.BillTotal(new[] { 18.0, 14 }, 50, 1, true, out top));
            Eq("BillTotal friends: no top-up", 0, top);
            Eq("BillTotal at the minimum 30+20 = 50", 50, Pricing.BillTotal(new[] { 30.0, 20 }, 50, 1, false, out top));
            Eq("BillTotal at the minimum: no top-up", 0, top);
            // rounding: 18,50 + 14 = 32,50; 50 - 32,50 = 17,50 rounded UP to 5 = 20 -> total 52,50
            Eq("BillTotal round to 5: total 52,50", 52.50, Pricing.BillTotal(new[] { 18.5, 14 }, 50, 5, false, out top));
            Eq("BillTotal round to 5: top-up 20", 20, top);
            // no rounding: 17,50 -> total 50,00
            Eq("BillTotal no rounding: total 50", 50, Pricing.BillTotal(new[] { 18.5, 14 }, 50, 0, false, out top));
            Eq("BillTotal no rounding: top-up 17,50", 17.50, top);
            // minimum 49,995, no rounding: 49,995 - 32 = 17,995 -> up to whole øre 18,00
            Pricing.BillTotal(new[] { 18.0, 14 }, 49.995, 0, false, out top);
            True("BillTotal top-up rounded up to whole øre: 18,00", Pricing.ShownOre(top) == 1800);
            Eq("BillTotal minimum 0: plain sum 32", 32, Pricing.BillTotal(new[] { 18.0, 14 }, 0, 1, false, out top));
            True("BillTotal minimum 0: no top-up", top == 0);
            True("BillTotal empty / null / all 0: 0, no top-up", Pricing.BillTotal(new double[0], 50, 1, false, out top) == 0 && top == 0
                 && Pricing.BillTotal(null, 50, 1, false, out top) == 0 && top == 0
                 && Pricing.BillTotal(new[] { 0.0, 0 }, 50, 1, false, out top) == 0 && top == 0);
            True("BillTotal never negative (negative / NaN prices ignored)", Pricing.BillTotal(new[] { -10.0, double.NaN, 60 }, 50, 1, false, out top) == 60 && top == 0);

            // ShareOre: 18 kr. over 18 / 14: 18/32 x 18 = 10,125 (1012,5 øre), 14/32 x 18 = 7,875 (787,5); floors 1012 + 787 = 1799,
            // 1 øre left, tie -> the earlier job: 1013 + 787 = 1800
            var sh = Pricing.ShareOre(new[] { 18.0, 14 }, 1800);
            True("ShareOre 18/14 of 18 kr. = 10,13 + 7,87", sh.Length == 2 && sh[0] == 1013 && sh[1] == 787);
            // 20 kr. over 3 x 10: 666,67 each -> 666 x 3 = 1998, 2 left -> 667, 667, 666
            sh = Pricing.ShareOre(new[] { 10.0, 10, 10 }, 2000);
            True("ShareOre 3 equal jobs: 667 + 667 + 666 = 2000", sh[0] == 667 && sh[1] == 667 && sh[2] == 666);
            sh = Pricing.ShareOre(new[] { 0.0, 0 }, 101);
            True("ShareOre all prices 0: equal parts 51 + 50", sh[0] == 51 && sh[1] == 50);
            sh = Pricing.ShareOre(new[] { 7.0, 13, 29, 31 }, 1999);
            True("ShareOre 4 jobs sums exactly to 1999", sh[0] + sh[1] + sh[2] + sh[3] == 1999);
            True("ShareOre nothing to share / no jobs", Pricing.ShareOre(new[] { 1.0 }, 0)[0] == 0 && Pricing.ShareOre(null, 100).Length == 0);

            // PriceWithoutMinimum: 30 g x 1000 kr./kg = 30 kr.; with the minimum 50
            var bs = BillSettings();
            var in30 = new QuoteInput { Hours = 1, Minutes = 0, FilamentGrams = 30, FilamentDkkPerKg = 1000, ElectricityDkkPerKwh = 0 };
            Eq("PriceWithoutMinimum 30 g = 30", 30, Pricing.PriceWithoutMinimum(in30, bs));
            Eq("Calculate 30 g with minimum = 50", 50, Pricing.Calculate(in30, bs).OthersPrice);
            True("PriceWithoutMinimum doesn't change the settings", bs.MinimumPrice == 50);

            var t = new DateTime(2026, 11, 19, 10, 0, 0);
            var ss = new AppSettings { MinimumPrice = 50, RoundTo = 1, InvoiceDueDays = 14, PayMobilePay = "12 34 56 78", PayBank = "BK", QuoteName = "Gettic 3D" };

            // BillPrice: own price without the minimum; the job's saved price is 50
            var j18 = BillJob(18, t.AddHours(-2)); var j14 = BillJob(14, t.AddHours(-1));
            Eq("job 18 g saved customer price 50 (own minimum)", 50, j18.OthersPrice);
            Eq("BillPrice 18 g = 18", 18, History.BillPrice(j18, ss, false));
            Eq("BillPrice friends = FriendsPrice 18", 18, History.BillPrice(j18, ss, true));

            // live bill (quote, nothing saved): 18 + 14 = 32 -> top-up 18 -> 50
            var g = History.GroupBill(new[] { j18, j14 }, ss, false);
            True("GroupBill live: 18 + 14, top-up 18, total 50, minimum 50", g.Prices.Length == 2 && Math.Abs(g.Prices[0] - 18) < 1e-9
                 && Math.Abs(g.Prices[1] - 14) < 1e-9 && Math.Abs(g.Sum - 32) < 1e-9 && Math.Abs(g.TopUp - 18) < 1e-9
                 && Math.Abs(g.Total - 50) < 1e-9 && g.Minimum == 50);
            True("GroupBill live changes nothing", j18.OthersPrice == 50 && j18.InvoiceNo == "" && j18.InvoiceTopUp == 0);
            var gf = History.GroupBill(new[] { j18, j14 }, ss, true);
            True("GroupBill friends: 32, no top-up", Math.Abs(gf.Total - 32) < 1e-9 && gf.TopUp == 0);

            // MakeInvoice for the group: one code, same date / snapshot / name / Printed; prices 18 + 10,13 and 14 + 7,87
            var list = new List<QuoteRecord> { j14, j18 };
            j14.Customer = "  Lene Hansen ";
            string code = History.MakeInvoice(list, new[] { j18, j14 }, t, ss, false, Digits("11234"));
            True("group invoice: one code on both", code == "2026-11234" && j18.InvoiceNo == code && j14.InvoiceNo == code);
            True("group invoice: same BilledAt, Printed", j18.BilledAt == "2026-11-19 10:00:00" && j14.BilledAt == j18.BilledAt && j18.Printed && j14.Printed);
            True("group invoice: same snapshot (due 19/11 + 14 = 3/12)", j18.InvoiceDue == "2026-12-03" && j14.InvoiceDue == "2026-12-03"
                 && j18.InvoicePayMobilePay == "12 34 56 78" && j14.InvoicePayBank == "BK" && j18.InvoiceFromName == "Gettic 3D");
            True("group invoice: BilledTo = first name given, on all", j18.BilledTo == "Lene Hansen" && j14.BilledTo == "Lene Hansen");
            True("group invoice: code in the used list once", ss.UsedInvoiceNos == "2026-11234");
            Eq("group invoice: 18 g job = 18 + 10,13 = 28,13", 28.13, j18.OthersPrice);
            Eq("group invoice: 14 g job = 14 + 7,87 = 21,87", 21.87, j14.OthersPrice);
            True("group invoice: shares kept (10,13 / 7,87)", Pricing.ShownOre(j18.InvoiceTopUp) == 1013 && Pricing.ShownOre(j14.InvoiceTopUp) == 787);
            True("group invoice: saved prices sum to the total 50,00 exactly", Pricing.ShownOre(j18.OthersPrice) + Pricing.ShownOre(j14.OthersPrice) == 5000);
            True("group invoice: friends price unchanged", Math.Abs(j18.FriendsPrice - 18) < 1e-9);

            // re-open after the settings change: same numbers
            ss.MinimumPrice = 100; ss.RoundTo = 5;
            var grp = History.InvoiceGroup(list, " 2026-11234 ");
            True("InvoiceGroup: both, oldest first", grp.Count == 2 && grp[0] == j18 && grp[1] == j14);
            var chk = History.BillProblem(grp);
            True("BillProblem of the made invoice: Ok, Code", chk.Ok && chk.Code == "2026-11234" && chk.Clash == null);
            var g2 = History.GroupBill(grp, ss, false);
            True("re-open: lines 18 + 14, minimum line 18 (50), total 50", Math.Abs(g2.Prices[0] - 18) < 1e-9 && Math.Abs(g2.Prices[1] - 14) < 1e-9
                 && Math.Abs(g2.TopUp - 18) < 1e-9 && Math.Abs(g2.Total - 50) < 1e-9 && g2.Minimum == 50);
            // re-issue: same code, nothing recomputed
            True("re-issue group: same code, prices kept", History.MakeInvoice(list, grp, t.AddDays(3), ss, false, Digits("56789")) == "2026-11234"
                 && Pricing.ShownOre(j18.OthersPrice) == 2813 && j18.BilledAt == "2026-11-19 10:00:00" && ss.UsedInvoiceNos == "2026-11234");
            ss.MinimumPrice = 50; ss.RoundTo = 1;

            // 3 jobs above the minimum together: 30 + 25 + 20 = 75, no top-up (today 150)
            var a30 = BillJob(30, t.AddHours(-3)); var a25 = BillJob(25, t.AddHours(-2)); var a20 = BillJob(20, t.AddHours(-1));
            var l3 = new List<QuoteRecord> { a20, a25, a30 };
            History.MakeInvoice(l3, new[] { a30, a25, a20 }, t, ss, false, Digits("48213"));
            True("3 jobs 75: prices 30 / 25 / 20, no shares", Pricing.ShownOre(a30.OthersPrice) == 3000 && Pricing.ShownOre(a25.OthersPrice) == 2500
                 && Pricing.ShownOre(a20.OthersPrice) == 2000 && a30.InvoiceTopUp == 0 && a20.InvoiceTopUp == 0 && a30.BilledTo == "");
            var g3 = History.GroupBill(History.InvoiceGroup(l3, "2026-48213"), ss, false);
            True("3 jobs re-open: total 75, no minimum line", Math.Abs(g3.Total - 75) < 1e-9 && g3.TopUp == 0);

            // friends bill: no top-up, prices unchanged
            var f18 = BillJob(18, t.AddHours(-2)); var f14 = BillJob(14, t.AddHours(-1));
            History.MakeInvoice(new List<QuoteRecord>(), new[] { f18, f14 }, t, ss, true, Digits("22222"));
            True("friends group: one code, prices unchanged, no shares", f18.InvoiceNo == "2026-22222" && f14.InvoiceNo == "2026-22222"
                 && f18.OthersPrice == 50 && f14.OthersPrice == 50 && f18.InvoiceTopUp == 0 && f14.InvoiceTopUp == 0);
            var gfr = History.GroupBill(new[] { f18, f14 }, ss, true);
            True("friends group re-open: 18 + 14 = 32", Math.Abs(gfr.Total - 32) < 1e-9 && gfr.TopUp == 0);

            // one job = today's single invoice (own minimum, no share)
            var o18 = BillJob(18, t); o18.Customer = "Ole";
            History.MakeInvoice(new List<QuoteRecord>(), new[] { o18 }, t, ss, false, Digits("33333"));
            True("one job: single invoice, own minimum 50 kept, InvoiceTopUp 0, BilledTo", o18.InvoiceNo == "2026-33333" && o18.OthersPrice == 50
                 && o18.InvoiceTopUp == 0 && o18.BilledTo == "Ole");
            Eq("one job re-open: line = 50", 50, History.BillPrice(o18, ss, false));

            // failed jobs left off
            var p18 = BillJob(18, t.AddHours(-3)); var pf = BillJob(14, t.AddHours(-2)); var p30 = BillJob(30, t.AddHours(-1));
            History.MarkFailed(pf, true, t.AddHours(-1));
            var pc = History.BillProblem(new[] { p18, pf, null, p30 });
            True("BillProblem: failed left out", pc.Ok && pc.Jobs.Count == 2 && pc.FailedLeftOut == 1 && pc.Code == "");
            History.MakeInvoice(new List<QuoteRecord>(), new[] { p18, pf, p30 }, t, ss, false, Digits("44444"));
            True("failed job not invoiced, the others are (18 + 30 = 48 -> top-up 2)", pf.InvoiceNo == "" && p18.InvoiceNo == "2026-44444"
                 && p30.InvoiceNo == "2026-44444" && Pricing.ShownOre(p18.OthersPrice) + Pricing.ShownOre(p30.OthersPrice) == 5000);
            // 2 kr. over 18 / 30: 18/48 x 2 = 0,75; 30/48 x 2 = 1,25
            True("failed case shares 0,75 / 1,25", Pricing.ShownOre(p18.InvoiceTopUp) == 75 && Pricing.ShownOre(p30.InvoiceTopUp) == 125);
            var allFailed = History.BillProblem(new[] { pf });
            True("BillProblem all failed: not Ok", !allFailed.Ok && allFailed.Jobs.Count == 0 && allFailed.FailedLeftOut == 1);
            True("BillProblem null: not Ok", !History.BillProblem(null).Ok);

            // mixed: one already billed (o18, single) + one not -> clash names the billed one; two codes -> clash
            var n1 = BillJob(20, t);
            var mix = History.BillProblem(new[] { n1, o18 });
            True("BillProblem mix: clash = the billed job", !mix.Ok && mix.Clash == o18 && mix.ClashCode == "2026-33333");
            var two = History.BillProblem(new[] { j18, o18 });
            True("BillProblem two codes: clash", !two.Ok && two.Clash == j18 && two.ClashCode == "2026-11234");
            bool threw = false;
            try { History.MakeInvoice(list, new[] { n1, o18 }, t, ss, false); } catch (InvalidOperationException) { threw = true; }
            True("MakeInvoice on a mix throws, nothing changed", threw && n1.InvoiceNo == "" && o18.InvoiceNo == "2026-33333");

            // old record whose saved price isn't the calculated one: its saved price as is
            var odd = BillJob(30, t.AddHours(-1)); odd.OthersPrice = 44;
            var ok14 = BillJob(14, t);
            Eq("BillPrice old record: saved 44 as is", 44, History.BillPrice(odd, ss, false));
            History.MakeInvoice(new List<QuoteRecord>(), new[] { odd, ok14 }, t, ss, false, Digits("55555"));
            True("old record + 14: 44 + 14 = 58 >= 50, no top-up", Pricing.ShownOre(odd.OthersPrice) == 4400 && Pricing.ShownOre(ok14.OthersPrice) == 1400
                 && odd.InvoiceTopUp == 0);
            var bad = BillJob(30, t); bad.Hours = -1;
            Eq("BillPrice impossible inputs: saved price", 50, History.BillPrice(bad, ss, false));

            // delete one job of a combined invoice: the summary says so, the rest keep their prices and shares
            var dsum = History.SummarizeDelete(list, new[] { j18.Id });
            True("SummarizeDelete: on combined invoice 2026-11234, 1 gone, 1 left", dsum.OnCombined == 1 && dsum.Combined.Count == 1
                 && dsum.Combined[0].Code == "2026-11234" && dsum.Combined[0].Gone == 1 && dsum.Combined[0].Left == 1 && dsum.Invoices == 1);
            True("SummarizeDelete single invoice: not combined", History.SummarizeDelete(new List<QuoteRecord> { o18 }, new[] { o18.Id }).OnCombined == 0);
            History.RemoveMany(list, new[] { j18.Id });
            var gd = History.GroupBill(History.InvoiceGroup(list, "2026-11234"), ss, false);
            True("after delete: 14 + minimum 7,87 = 21,87", gd.Jobs.Count == 1 && Math.Abs(gd.Prices[0] - 14) < 1e-9
                 && Pricing.ShownOre(gd.TopUp) == 787 && Pricing.ShownOre(gd.Total) == 2187);

            // JSON: key round trip, missing -> 0, bad -> 0
            var jr = History.ParseJson(History.ToJson(new List<QuoteRecord> { j14 }));
            True("JSON round trip keeps InvoiceTopUp", Pricing.ShownOre(jr[0].InvoiceTopUp) == 787 && Pricing.ShownOre(jr[0].OthersPrice) == 2187);
            var jo = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"x\",\"InvoiceNo\":\"2026-K7Q4\",\"OthersPrice\":50},{\"Id\":\"y\",\"InvoiceTopUp\":-5}]}");
            True("JSON without the key -> 0; negative -> 0", jo[0].InvoiceTopUp == 0 && jo[1].InvoiceTopUp == 0);
            Eq("old invoice without the key: line = saved price 50", 50, History.BillPrice(jo[0], ss, false));

            // Combine: same code + same BilledAt = a combined invoice, not a clash; different BilledAt = a clash
            var c1 = BillJob(18, t.AddHours(-2)); var c2 = BillJob(14, t.AddHours(-1));
            History.MakeInvoice(new List<QuoteRecord>(), new[] { c1, c2 }, t, new AppSettings { MinimumPrice = 50, RoundTo = 1 }, false, Digits("66666"));
            var res = History.Combine(new List<QuoteRecord> { c2 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { c1 } }, false, false);
            True("Combine: combined invoice is not a clash", res.Quotes.Count == 2 && res.InvoiceClashes.Count == 0);
            var c3 = BillJob(20, t); c3.InvoiceNo = "2026-66666"; c3.BilledAt = "2026-11-20 09:00:00";
            res = History.Combine(new List<QuoteRecord> { c2, c3 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { c1 } }, false, false);
            True("Combine: same code, other BilledAt = a clash (listed once)", res.Quotes.Count == 3 && J(res.InvoiceClashes) == "2026-66666");
            var e1 = BillJob(18, t); e1.InvoiceNo = "2026-E1E1"; var e2 = BillJob(14, t.AddHours(1)); e2.InvoiceNo = "2026-E1E1";
            res = History.Combine(new List<QuoteRecord> { e1 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { e2 } }, false, false);
            True("Combine: same code without BilledAt = a clash", J(res.InvoiceClashes) == "2026-E1E1");
            // merge: this PC's copy has no invoice yet, the backup's is on the combined invoice -> invoice, price and share taken
            var here = History.CopyQuotes(new List<QuoteRecord> { BillJob(18, t.AddHours(-2)) })[0];
            here.Id = c1.Id;
            res = History.Combine(new List<QuoteRecord> { here }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { c1 } }, false, false);
            var hr = res.Quotes[0];
            True("Merge: combined invoice filled in with its price and share", res.Updated == 1 && hr.InvoiceNo == "2026-66666"
                 && Pricing.ShownOre(hr.OthersPrice) == 2813 && Pricing.ShownOre(hr.InvoiceTopUp) == 1013);
            // backup round trip keeps the key
            BackupData bd;
            True("InvoiceTopUp: backup round trip", History.ReadBackup(History.ToBackup(new List<QuoteRecord> { c1 }, null, "1.23.0", t), out bd) == null
                 && Pricing.ShownOre(bd.Quotes[0].InvoiceTopUp) == 1013);

            GroupBreakdownTests();
        }

        static string GbText(GroupBreakdownInfo b)
        {
            var parts = new List<string>();
            foreach (var l in b.Lines)
            {
                string p = l.Key + "=" + l.Ore;
                if (l.Parts != null) foreach (var x in l.Parts) p += "[" + x.Key + "=" + x.Ore + "]";
                parts.Add(p);
            }
            return string.Join(" ", parts.ToArray());
        }

        static long GbSum(GroupBreakdownInfo b)
        {
            long s = 0;
            foreach (var l in b.Lines) s += l.Ore;
            return s;
        }

        static void GroupBreakdownTests()
        {
            var t = new DateTime(2026, 11, 20, 10, 0, 0);
            var ss = new AppSettings { MinimumPrice = 50, RoundTo = 1 };

            // live 18 + 14 (same filament): material merged 18 + 14 = 32 g = 32,00; power / work 0 kept; minimum top-up 18,00
            var j18 = BillJob(18, t.AddHours(-2)); var j14 = BillJob(14, t.AddHours(-1));
            var g = History.GroupBill(new[] { j18, j14 }, ss, false);
            var b = History.GroupBreakdown(g, ss);
            True("GroupBreakdown live 18+14: material 3200, power 0, work 0, minimum 1800", GbText(b) == "material=3200 power=0 work=0 minimum=1800");
            True("GroupBreakdown live: total = bill total 50,00 and = Σ rows", b.TotalOre == 5000 && GbSum(b) == 5000 && Pricing.ShownOre(g.Total) == 5000);
            True("GroupBreakdown live: one merged material 32 g at 1000 kr./kg, same rates", b.Materials.Count == 1 && Math.Abs(b.Materials[0].Grams - 32) < 1e-9
                 && b.Lines[0].Index == 0 && Math.Abs(b.Lines[0].Quantity - 32) < 1e-9 && b.Lines[0].UnitPrice == 1000 && b.SameRates);
            True("GroupBreakdown minimum row: Quantity = minimum 50", b.Lines[3].Key == QuoteLine.Minimum && b.Lines[3].Quantity == 50);

            // invoiced (18 + 10,13 and 14 + 7,87), then the minimum setting changes: re-opened rows are the same
            History.MakeInvoice(new List<QuoteRecord>(), new[] { j18, j14 }, t, ss, false, Digits("12345"));
            ss.MinimumPrice = 100;
            var gi = History.GroupBill(History.InvoiceGroup(new List<QuoteRecord> { j14, j18 }, "2026-12345"), ss, false);
            var bi = History.GroupBreakdown(gi, ss);
            True("GroupBreakdown re-opened after the minimum changed: same rows", GbText(bi) == "material=3200 power=0 work=0 minimum=1800"
                 && bi.TotalOre == 5000 && Pricing.ShownOre(gi.Total) == 5000);
            ss.MinimumPrice = 50;

            // friends: 18 + 14 = 32, no minimum row
            var f18 = BillJob(18, t.AddHours(-2)); var f14 = BillJob(14, t.AddHours(-1));
            var bf = History.GroupBreakdown(History.GroupBill(new[] { f18, f14 }, ss, true), ss);
            True("GroupBreakdown friends: material 3200, no minimum", GbText(bf) == "material=3200 power=0 work=0" && bf.TotalOre == 3200);

            // other colour = its own material row; another kr./kg merges grams but the rate is mixed
            var c18 = BillJob(18, t.AddHours(-2)); c18.Filaments[0].Colour = "#ff0000";
            var c14 = BillJob(14, t.AddHours(-1));
            var bc = History.GroupBreakdown(History.GroupBill(new[] { c18, c14 }, ss, false), ss);
            True("GroupBreakdown other colour: 2 material rows (18 g red, 14 g)", bc.Materials.Count == 2 && bc.Materials[0].Colour == "#FF0000"
                 && Math.Abs(bc.Materials[0].Grams - 18) < 1e-9 && Math.Abs(bc.Materials[1].Grams - 14) < 1e-9
                 && GbText(bc) == "material=1800 material=1400 power=0 work=0 minimum=1800" && bc.TotalOre == 5000);
            // 20 g x 500 kr./kg = 10,00: same name / colour as the 1000 kr./kg jobs -> merged 18 + 20 = 38 g, rate 0 (mixed)
            var s500 = BillSettings();
            var in500 = new QuoteInput { Hours = 1, Minutes = 0, FilamentGrams = 20, FilamentDkkPerKg = 500, ElectricityDkkPerKwh = 0 };
            var r500 = History.FromQuote(in500, Pricing.Calculate(in500, s500), s500, "Cheap", "", t);
            var bm = History.GroupBreakdown(History.GroupBill(new[] { BillJob(18, t.AddHours(-1)), r500 }, ss, false), ss);
            // 18 + 10 = 28 -> top-up 22 -> 50
            True("GroupBreakdown merged grams 38, mixed rate -> UnitPrice 0, SameRates false", bm.Materials.Count == 1
                 && Math.Abs(bm.Lines[0].Quantity - 38) < 1e-9 && bm.Lines[0].UnitPrice == 0 && !bm.SameRates
                 && GbText(bm) == "material=2800 power=0 work=0 minimum=2200" && bm.TotalOre == 5000);

            // old record whose saved price (44) isn't the calculated one (30): its rows scaled to 44; + 14 = 58, no top-up
            var odd = BillJob(30, t.AddHours(-1)); odd.OthersPrice = 44;
            var bo = History.GroupBreakdown(History.GroupBill(new[] { odd, BillJob(14, t) }, ss, false), ss);
            True("GroupBreakdown old mismatched record: scaled 44 + 14 = 58, rate not shown", GbText(bo) == "material=5800 power=0 work=0"
                 && bo.TotalOre == 5800 && Math.Abs(bo.Lines[0].Quantity - 44) < 1e-9 && bo.Lines[0].UnitPrice == 0 && !bo.SameRates);
            // no usable inputs: one "other" row with its saved price 50 (+ 14 = 64)
            var bad = BillJob(30, t); bad.Hours = -1;
            var bb = History.GroupBreakdown(History.GroupBill(new[] { bad, BillJob(14, t) }, ss, false), ss);
            True("GroupBreakdown no usable inputs: other row 50,00", GbText(bb) == "material=1400 power=0 work=0 other=5000" && bb.TotalOre == 6400);

            // full formula (HistSettings: power, wear, failed 10 %, markups, no rounding): 54,6975 -> 54,70 each, + 18 g job
            // with minimum 50: 54,70 + 54,70 + 18,00 = 127,40 >= 50 -> no top-up; rows must still add up to 127,40 exactly
            var h1 = HistRecord(t.AddHours(-3)); var h2 = HistRecord(t.AddHours(-2));
            var gh = History.GroupBill(new[] { h1, h2, BillJob(18, t) }, ss, false);
            var bh = History.GroupBreakdown(gh, ss);
            True("GroupBreakdown full formula: total 127,40 = bill total = Σ rows", bh.TotalOre == 12740 && Pricing.ShownOre(gh.Total) == 12740 && GbSum(bh) == 12740);
            // material 21,60 + 21,60 + 18,00 = 61,20; power 1,10 x 2 = 2,20; wear 11,00 x 2 = 22,00; failed 2,27 x 2 = 4,54;
            // work 24,97 x 0,75 = 18,7275 x 2 -> 18,73 + 18,73 (øre split per job) = 37,46: 61,20 + 2,20 + 22,00 + 4,54 + 37,46 = 127,40
            // wear split estimated for prints saved without one (A1, 0.4 stainless, PLA, High Temp Plate): printer 2099 / 3000 =
            // 0,69967, parts 600 / 1500 = 0,4, hotend 139 / 500 = 0,278, plate 200 / 600 = 0,33333 kr/h; sum 1,711.
            // 11,00 kr. per print: 4,4981 / 2,5716 / 1,7873 / 2,1430 -> floors 449 + 257 + 178 + 214 = 1098, the 2 øre left go
            // to the largest remainders (printer ,81, hotend ,73): 450 / 257 / 179 / 214 = 1100; two prints: 900 / 514 / 358 / 428
            True("GroupBreakdown full formula rows (wear always split)", GbText(bh) ==
                 "material=6120 power=220 wear=2200[wear.printer=900][wear.parts=514][wear.hotend=358][wear.plate=428] failed=454 work=3746");
            True("GroupBreakdown estimated split flagged, one plate", bh.EstimatedSplit && bh.PlateKey == "High Temp Plate");

            // a mix: h3 has a saved split 1 / 0,5 / 0,3 / 0,2 kr/h x 5,5 h = 5,50 / 2,75 / 1,65 / 1,10 (= 11,00), h4 estimated
            // 4,50 / 2,57 / 1,79 / 2,14: parts 10,00 / 5,32 / 3,44 / 3,24 = 22,00 = the wear row
            var h3 = HistRecord(t.AddHours(-3)); h3.WearPrinter = 1; h3.WearParts = 0.5; h3.WearHotend = 0.3; h3.WearPlate = 0.2;
            var h4 = HistRecord(t.AddHours(-2));
            var gx = History.GroupBill(new[] { h3, h4 }, ss, false);
            var bx = History.GroupBreakdown(gx, ss);
            True("GroupBreakdown mix saved + estimated split: parts 1000 / 532 / 344 / 324", bx.Lines[2].Key == QuoteLine.Wear && bx.Lines[2].Ore == 2200
                 && bx.Lines[2].Parts != null && bx.Lines[2].Parts.Count == 4 && bx.Lines[2].Parts[0].Ore == 1000 && bx.Lines[2].Parts[1].Ore == 532
                 && bx.Lines[2].Parts[2].Ore == 344 && bx.Lines[2].Parts[3].Ore == 324 && bx.EstimatedSplit);
            True("GroupBreakdown mix: total = bill total = Σ rows (109,40)", bx.TotalOre == 10940 && Pricing.ShownOre(gx.Total) == 10940 && GbSum(bx) == 10940);
            // all prints with a saved split: not estimated
            var h5 = HistRecord(t.AddHours(-1)); h5.WearPrinter = 1; h5.WearParts = 0.5; h5.WearHotend = 0.3; h5.WearPlate = 0.2;
            var ba = History.GroupBreakdown(History.GroupBill(new[] { h3, h5 }, ss, false), ss);
            True("GroupBreakdown all saved splits: 1100 / 550 / 330 / 220, not estimated", !ba.EstimatedSplit && ba.Lines[2].Parts[0].Ore == 1100
                 && ba.Lines[2].Parts[1].Ore == 550 && ba.Lines[2].Parts[2].Ore == 330 && ba.Lines[2].Parts[3].Ore == 220);
            // different plates: PlateKey "" (general "Build plate"); the parts still add up to the wear row
            var h6 = HistRecord(t.AddHours(-1)); h6.BedType = "Textured PEI Plate";
            var bp = History.GroupBreakdown(History.GroupBill(new[] { h4, h6 }, ss, false), ss);
            long ps = 0; foreach (var p in bp.Lines[2].Parts) ps += p.Ore;
            True("GroupBreakdown different plates: PlateKey empty, parts = wear row 2200", bp.PlateKey == "" && ps == 2200 && bp.Lines[2].Ore == 2200
                 && GbSum(bp) == bp.TotalOre);
            // an old print without saved plate / nozzle / model: today's settings (s) give the ratios
            var h7 = HistRecord(t.AddHours(-1)); h7.BedType = ""; h7.Nozzle = ""; h7.PrinterModel = "";
            var st = new AppSettings { MinimumPrice = 50, RoundTo = 1, BedType = "Textured PEI Plate" };
            var b7 = History.GroupBreakdown(History.GroupBill(new[] { h7, h6 }, st, false), st);
            True("GroupBreakdown old print: today's plate -> same plate as h6", b7.PlateKey == "Textured PEI Plate" && b7.Lines[2].Parts.Count == 4);
            True("GroupBreakdown full formula: power 1,1 kWh at 2 kr./kWh, wear 11 h at 2 kr./h", Math.Abs(bh.Lines[1].Quantity - 1.1) < 1e-9
                 && bh.Lines[1].UnitPrice == 2 && Math.Abs(bh.Lines[2].Quantity - 11) < 1e-9 && bh.Lines[2].UnitPrice == 2);
            True("GroupBreakdown null bill: empty", History.GroupBreakdown(null, ss).Lines.Count == 0);
        }

        // ---------- a bill made again from History = the bill made after slicing (RecordBreakdown / RecordQuoteInfo) ----------

        static string BillText(QuoteBreakdown b)
        {
            var parts = new List<string>();
            foreach (var l in b.Lines)
            {
                string p = l.Key + "=" + l.Ore;
                if (l.Parts != null) foreach (var x in l.Parts) p += "[" + x.Key + "=" + x.Ore + "]";
                parts.Add(p);
            }
            return string.Join("|", parts.ToArray()) + " total=" + b.TotalOre;
        }

        static void RecordBillTests()
        {
            var t = new DateTime(2026, 11, 3, 14, 5, 0);
            var s = HistSettings();
            s.AutoWearRate = 1; s.MachineRatePerHour = 1.71; s.BedType = "High Temp Plate"; s.Nozzle = "0.4 stainless";
            s.QuoteValidDays = 14; s.QuoteName = " Gettic "; s.FilamentPreset = "Bambu PLA Basic";
            var input = new QuoteInput { Hours = 2, Minutes = 0, FilamentGrams = 100, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 2 };
            var wear = new Wear.Breakdown { Printer = 0.80, Parts = 0.40, Hotend = 0.31, Plate = 0.20 };   // sum 1,71
            // 0,100 x 200 = 20,00 material; 2 h x 0,1 kW = 0,2 kWh x 2 = 0,40 power; 20,40 x 10 % = 2,04 failed; base 22,44
            // customer 22,44 x 1,75 = 39,27 + 2 x 1,71 = 3,42 -> 42,69; friends 22,44 x 1,25 = 28,05 + 3,42 = 31,47
            var fresh = Pricing.Breakdown(input, s, false, wear);
            var freshF = Pricing.Breakdown(input, s, true, wear);
            var rec = History.FromQuote(input, Pricing.Calculate(input, s), s, "Box", "", t, wear);
            True("Bill: snapshot saved (wear 0,80/0,40/0,31/0,20, plate, nozzle, material, 14 days, name)",
                 rec.WearPrinter == 0.80 && rec.WearParts == 0.40 && rec.WearHotend == 0.31 && rec.WearPlate == 0.20
                 && rec.BedType == "High Temp Plate" && rec.Nozzle == "0.4 stainless" && rec.MaterialName == "Bambu PLA Basic"
                 && rec.QuoteValidDays == 14 && rec.QuoteFromName == "Gettic");
            Eq("Bill: customer total 42,69", 42.69, fresh.Total);
            var fw = Find(fresh, "wear");
            // wear 2 x 1,71 = 3,42: parts 2 x 0,80 = 1,60, 2 x 0,40 = 0,80, 2 x 0,31 = 0,62, 2 x 0,20 = 0,40
            True("Bill: fresh wear 342 = 160 + 80 + 62 + 40", fw != null && fw.Ore == 342 && fw.Parts != null
                 && fw.Parts[0].Ore == 160 && fw.Parts[1].Ore == 80 && fw.Parts[2].Ore == 62 && fw.Parts[3].Ore == 40);

            // today's settings changed: Auto wear off, other plate, rate 2,54, 30 days, other name -> the record's bill is unchanged
            var today = s.Clone();
            today.AutoWearRate = 0; today.BedType = "Textured PEI Plate"; today.MachineRatePerHour = 2.54;
            today.QuoteValidDays = 30; today.QuoteName = "Other"; today.Nozzle = "0.2 stainless";
            today.FriendsMarkupPct = 50; today.OthersMarkupPct = 100; today.FailedPrintPct = 0;
            var hist = History.RecordBreakdown(rec, today, false);
            True("Bill: History customer bill = fresh (" + BillText(fresh) + ")", BillText(hist) == BillText(fresh));
            True("Bill: History friends bill = fresh (" + BillText(freshF) + ")", BillText(History.RecordBreakdown(rec, today, true)) == BillText(freshF));
            var hw = Find(hist, "wear");
            Eq("Bill: History wear part printer kr/h 0,80", 0.80, hw != null && hw.Parts != null ? hw.Parts[0].UnitPrice : -1);

            // JSON round trip and a deep copy keep the snapshot
            var back = History.ParseJson(History.ToJson(new List<QuoteRecord> { rec }));
            True("Bill: JSON round trip keeps the snapshot", back != null && BillText(History.RecordBreakdown(back[0], today, false)) == BillText(fresh)
                 && back[0].BedType == "High Temp Plate" && back[0].Nozzle == "0.4 stainless" && back[0].MaterialName == "Bambu PLA Basic"
                 && back[0].QuoteValidDays == 14 && back[0].QuoteFromName == "Gettic");
            True("Bill: CopyQuotes = JSON round trip", History.ToJson(History.CopyQuotes(new List<QuoteRecord> { rec }))
                 == History.ToJson(back));

            // quote details: snapshot (3 Nov + 14 = 17 Nov, "Gettic") vs an old record (today's: 3 Nov + 30 = 3 Dec, "Other")
            var qi = History.RecordQuoteInfo(rec, today);
            True("Bill: quote info from snapshot: 2026-11-17, Gettic", qi.FromSnapshot && qi.ValidUntil == new DateTime(2026, 11, 17) && qi.FromName == "Gettic");

            // an old record (no keys): one wear line 2 x 1,71 = 3,42 without sub-rows, today's valid days and name
            string oldJson = History.ToJson(new List<QuoteRecord> { rec });
            foreach (var k in new[] { "WearPrinter", "WearParts", "WearHotend", "WearPlate", "QuoteValidDays" })
                oldJson = System.Text.RegularExpressions.Regex.Replace(oldJson, ",\"" + k + "\":[0-9.]+", "");
            foreach (var k in new[] { "BedType", "Nozzle", "MaterialName", "QuoteFromName" })
                oldJson = System.Text.RegularExpressions.Regex.Replace(oldJson, ",\"" + k + "\":\"[^\"]*\"", "");
            var old = History.ParseJson(oldJson);
            True("Bill: old record reads 0 / \"\"", old != null && old.Count == 1 && !oldJson.Contains("WearPrinter") && !oldJson.Contains("MaterialName")
                 && old[0].WearPrinter == 0 && old[0].QuoteValidDays == 0 && old[0].BedType == "" && old[0].Nozzle == ""
                 && old[0].MaterialName == "" && old[0].QuoteFromName == "" && History.SavedWear(old[0]) == null);
            var ob = History.RecordBreakdown(old[0], today, false);
            var ow = Find(ob, "wear");
            True("Bill: old record = one wear line 342 without parts, same total", ow != null && ow.Ore == 342 && ow.Parts == null && ob.TotalOre == 4269);
            qi = History.RecordQuoteInfo(old[0], today);
            True("Bill: old record quote info = today's: 2026-12-03, Other", !qi.FromSnapshot && qi.ValidUntil == new DateTime(2026, 12, 3) && qi.FromName == "Other");

            // Auto wear off when saved: no split stored even if one is given
            var off = s.Clone();
            off.AutoWearRate = 0;
            var recOff = History.FromQuote(input, Pricing.Calculate(input, off), off, "Box", "", t, wear);
            True("Bill: Auto wear off -> no split saved, one wear line", History.SavedWear(recOff) == null && recOff.WearPrinter == 0
                 && Find(History.RecordBreakdown(recOff, today, false), "wear").Parts == null);
            True("Bill: 6-argument FromQuote = no split", History.SavedWear(History.FromQuote(input, Pricing.Calculate(input, s), s, "Box", "", t)) == null);

            // a rate (2,00) other than the split's sum (1,71): parts scaled to the rate, as Pricing.Breakdown does.
            // printer 2,00 x 0,80 / 1,71 = 0,9357 kr/h -> 1,8713 kr; parts 0,9357; hotend 0,7251; plate 0,4678 (sum 4,00)
            // øre: floors 187 + 93 + 72 + 46 = 398, the 2 øre left go to plate (,78) and parts (,57) -> 187 / 94 / 72 / 47
            var two = s.Clone();
            two.MachineRatePerHour = 2.00;
            var rec2 = History.FromQuote(input, Pricing.Calculate(input, two), two, "Box", "", t, wear);
            var w2 = Find(History.RecordBreakdown(rec2, today, false), "wear");
            bool w2ok = w2 != null && w2.Parts != null && w2.Parts.Count == 4;
            True("Bill: rate 2,00 vs split 1,71: wear 400 = 187 + 94 + 72 + 47", w2ok && w2.Ore == 400
                 && w2.Parts[0].Ore == 187 && w2.Parts[1].Ore == 94 && w2.Parts[2].Ore == 72 && w2.Parts[3].Ore == 47);
            Eq("Bill: rate 2,00 printer part 0,9357 kr/h", 0.9357, w2ok ? w2.Parts[0].UnitPrice : -1);

            // CleanQuotes: a bad split value drops the whole split, bad days -> 0
            var bad = History.CopyQuotes(new List<QuoteRecord> { rec });
            bad[0].WearPlate = -1;
            var cleaned = History.ParseJson(History.ToJson(bad));
            True("Bill: negative wear part -> no split", cleaned != null && History.SavedWear(cleaned[0]) == null && cleaned[0].WearPrinter == 0);
            var bad2 = History.CopyQuotes(new List<QuoteRecord> { rec });
            bad2[0].QuoteValidDays = -3;
            True("Bill: negative valid days -> today's", !History.RecordQuoteInfo(History.ParseJson(History.ToJson(bad2))[0], today).FromSnapshot);

            // SameQuote ignores the snapshot (a record without it is still the same quote)
            True("Bill: SameQuote ignores the snapshot", History.SameQuote(rec, old[0]));

            // merge: a copy here without the snapshot (resaved by an older app) gets it from the backup; one with it stays
            var here = History.ParseJson(oldJson);
            var res = History.Combine(here, new AppSettings(), new BackupData { Quotes = History.CopyQuotes(new List<QuoteRecord> { rec }) }, false, false);
            True("Bill: merge fills the snapshot", res.Updated == 1 && res.Quotes.Count == 1 && History.SavedWear(res.Quotes[0]) != null
                 && res.Quotes[0].BedType == "High Temp Plate" && res.Quotes[0].Nozzle == "0.4 stainless" && res.Quotes[0].MaterialName == "Bambu PLA Basic"
                 && res.Quotes[0].QuoteValidDays == 14 && res.Quotes[0].QuoteFromName == "Gettic"
                 && BillText(History.RecordBreakdown(res.Quotes[0], today, false)) == BillText(fresh));
            res = History.Combine(History.CopyQuotes(new List<QuoteRecord> { rec }), new AppSettings(), new BackupData { Quotes = History.ParseJson(oldJson) }, false, false);
            True("Bill: merge from an old backup keeps the snapshot", res.Same == 1 && History.SavedWear(res.Quotes[0]) != null && res.Quotes[0].QuoteValidDays == 14);
        }

        // ---------- print status from the quote window (SetPrintState, IsPrinting, AddFor with a state) ----------

        static void PrintStateTests()
        {
            var t = new DateTime(2026, 11, 19, 10, 0, 0);
            const string T = "2026-11-19 10:00:00";

            // old file (no key), null and unreadable values = not printing
            var old = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"Hours\":5},{\"Id\":\"b\",\"PrintingSince\":null},{\"Id\":\"c\",\"Printed\":true,\"Hours\":5,\"PrintingSince\":\"soon\"}]}");
            True("PrintState: old / null / bad PrintingSince -> \"\"", old != null && old[0].PrintingSince == "" && old[1].PrintingSince == "" && old[2].PrintingSince == ""
                 && !History.IsPrinting(old[2], t) && History.PrintStateOf(old[0], t) == PrintState.Printed);   // old file = printed

            // Printing now: Printed, start = now; Example() is 5 h 30 m -> done 15:30
            var r = HistRecord(t.AddMinutes(-5));
            True("PrintState: new record = Not printed", History.PrintStateOf(r, t) == PrintState.NotPrinted && r.PrintingSince == "");
            History.SetPrintState(r, PrintState.Printing, t);
            DateTime end;
            True("PrintState: Printing -> Printed + start " + T, r.Printed && !r.Failed && r.PrintingSince == T
                 && History.PrintingEnds(r, out end) && end == new DateTime(2026, 11, 19, 15, 30, 0));
            True("PrintState: printing at 15:29, done at 15:30", History.IsPrinting(r, t.AddMinutes(329)) && !History.IsPrinting(r, t.AddMinutes(330)));
            True("PrintState: state at 12:00 Printing, at 16:00 Printed", History.PrintStateOf(r, t.AddHours(2)) == PrintState.Printing
                 && History.PrintStateOf(r, t.AddHours(6)) == PrintState.Printed);
            History.SetPrintState(r, PrintState.Printing, t.AddMinutes(3));
            True("PrintState: Printing again keeps the start", r.PrintingSince == T);
            var back = History.ParseJson(History.ToJson(new List<QuoteRecord> { r }));
            True("PrintState: JSON round trip keeps PrintingSince", back[0].PrintingSince == T && History.IsPrinting(back[0], t.AddHours(1)));
            True("PrintState: CopyQuotes keeps it", History.CopyQuotes(new List<QuoteRecord> { r })[0].PrintingSince == T);

            // Printed / Not printed
            History.SetPrintState(r, PrintState.Printed, t);
            True("PrintState: Printed -> Printed, not printing", r.Printed && !r.Failed && r.PrintingSince == "" && History.PrintStateOf(r, t) == PrintState.Printed);
            History.SetPrintState(r, PrintState.NotPrinted, t);
            True("PrintState: Not printed -> Printed off", !r.Printed && !r.Failed && r.PrintingSince == "" && History.PrintStateOf(r, t) == PrintState.NotPrinted);

            // Failed = ticking Failed in History: FailedAt now, Printed off, its cost 21,60 + 1,10 + 5,5 x 2 = 33,70 becomes debt
            History.SetPrintState(r, PrintState.Printing, t);
            History.SetPrintState(r, PrintState.Failed, t.AddHours(1));
            True("PrintState: Failed -> Failed, FailedAt 11:00, Printed off, not printing", r.Failed && r.FailedAt == "2026-11-19 11:00:00" && !r.Printed
                 && r.PrintingSince == "" && History.PrintStateOf(r, t.AddHours(2)) == PrintState.Failed);
            Eq("PrintState: Failed adds 33,70 debt", 33.70, History.FailedDebt(new List<QuoteRecord> { r }, null));
            History.SetPrintState(r, PrintState.Failed, t.AddHours(3));
            True("PrintState: Failed again keeps FailedAt", r.FailedAt == "2026-11-19 11:00:00");
            History.SetPrintState(r, PrintState.Printed, t.AddHours(3));
            True("PrintState: Printed after Failed -> Failed off, FailedAt \"\"", !r.Failed && r.FailedAt == "" && r.Printed);
            Eq("PrintState: no debt after unfailing", 0, History.FailedDebt(new List<QuoteRecord> { r }, null));
            // MarkFailed (History's tick) also ends printing; 0 h 0 m is never printing; null record no error
            History.SetPrintState(r, PrintState.Printing, t);
            History.MarkFailed(r, true, t);
            True("PrintState: MarkFailed clears PrintingSince", r.PrintingSince == "" && !History.IsPrinting(r, t));
            var z = HistRecord(t); z.Hours = 0; z.Minutes = 0;
            History.SetPrintState(z, PrintState.Printing, t);
            True("PrintState: 0 h 0 m is never printing", z.Printed && !History.IsPrinting(z, t) && History.PrintStateOf(z, t) == PrintState.Printed);
            bool threw = false;
            try { History.SetPrintState(null, PrintState.Failed, t); History.PrintStateOf(null, t); History.IsPrinting(null, t); }
            catch (Exception) { threw = true; }
            True("PrintState: null record no error", !threw);

            // same quote saved again: the window's state wins over the old keep-Printed / keep-Failed rule
            var list = new List<QuoteRecord>();
            var a = History.AddFor(list, HistRecord(t), History.ForCustomer, PrintState.Printed, t);
            True("AddFor state: Printed", list.Count == 1 && a.Printed);
            var plain = History.Add(new List<QuoteRecord> { a }, HistRecord(t.AddMinutes(1)));
            True("Add without a state keeps Printed (old rule)", plain.Printed);
            var b = History.AddFor(list, HistRecord(t.AddMinutes(2)), History.ForFriends, PrintState.NotPrinted, t.AddMinutes(2));
            True("AddFor state: Not printed wins over the old Printed", list.Count == 1 && b.Id == a.Id && !b.Printed && b.For == History.ForFriends);
            var c = History.AddFor(list, HistRecord(t.AddMinutes(3)), History.ForCustomer, PrintState.Failed, t.AddMinutes(3));
            True("AddFor state: Failed, FailedAt 10:03", list.Count == 1 && c.Failed && c.FailedAt == "2026-11-19 10:03:00" && !c.Printed);
            var keepF = History.Add(list, HistRecord(t.AddMinutes(4)));
            True("Add without a state keeps Failed (old rule)", keepF.Failed && keepF.FailedAt == "2026-11-19 10:03:00");
            var d = History.AddFor(list, HistRecord(t.AddMinutes(5)), History.ForCustomer, PrintState.Failed, t.AddMinutes(5));
            True("AddFor state: Failed again keeps FailedAt 10:03", d.Failed && d.FailedAt == "2026-11-19 10:03:00");
            var e = History.AddFor(list, HistRecord(t.AddMinutes(6)), History.ForCustomer, PrintState.Printed, t.AddMinutes(6));
            True("AddFor state: Printed wins over the old Failed", list.Count == 1 && !e.Failed && e.FailedAt == "" && e.Printed);
            var f = History.AddFor(list, HistRecord(t.AddMinutes(7)), History.ForCustomer, PrintState.Printing, t.AddMinutes(7));
            var g = History.AddFor(list, HistRecord(t.AddMinutes(8)), History.ForCustomer, PrintState.Printing, t.AddMinutes(8));
            True("AddFor state: Printing again keeps the start 10:07", g.PrintingSince == "2026-11-19 10:07:00" && History.IsPrinting(g, t.AddMinutes(8)));
            var h = History.Add(list, HistRecord(t.AddMinutes(9)));
            True("Add without a state keeps printing", h.Printed && h.PrintingSince == "2026-11-19 10:07:00" && list.Count == 1);
            var other = History.AddFor(list, HistRecord(t.AddMinutes(30)), History.ForCustomer, PrintState.Printing, t.AddMinutes(30));
            True("AddFor state: a new quote is added", list.Count == 2 && other.PrintingSince == "2026-11-19 10:30:00");

            // a finished print set Printing again gets a new start: 10-min job started 10:00 (done 10:10), again at 10:11
            var ten = HistRecord(t); ten.Hours = 0; ten.Minutes = 10;
            History.SetPrintState(ten, PrintState.Printing, t);
            History.SetPrintState(ten, PrintState.Printing, t.AddMinutes(11));
            True("PrintState: finished print set Printing again -> new start 10:11, printing", ten.PrintingSince == "2026-11-19 10:11:00"
                 && History.IsPrinting(ten, t.AddMinutes(12)));
            // same through AddFor: an old ended start is not carried over (10-min jobs, 10:00 then 10:11 -> start 10:11)
            var tl = new List<QuoteRecord>();
            var j1 = HistRecord(t); j1.Hours = 0; j1.Minutes = 10;
            History.AddFor(tl, j1, History.ForCustomer, PrintState.Printing, t);
            var j2 = HistRecord(t.AddMinutes(9)); j2.Hours = 0; j2.Minutes = 10;   // saved within 10 min = the same quote
            var j2k = History.AddFor(tl, j2, History.ForCustomer, PrintState.Printing, t.AddMinutes(11));
            True("AddFor state: ended start not carried over -> 10:11, printing", tl.Count == 1 && j2k.PrintingSince == "2026-11-19 10:11:00"
                 && History.IsPrinting(j2k, t.AddMinutes(12)));
            // the History Printed tick: unticking ends printing, ticking keeps nothing else
            History.SetPrinted(j2k, false);
            True("SetPrinted false: not printed, not printing", !j2k.Printed && j2k.PrintingSince == "");
            History.SetPrinted(j2k, true);
            True("SetPrinted true: printed, not printing", j2k.Printed && !History.IsPrinting(j2k, t.AddMinutes(12)));
            History.SetPrinted(null, true);

            // merge: the backup's printing start comes in (later one wins); never onto a failed record
            var mine = BRec("M", "2026-11-19 10:00:00"); mine.Printed = true;
            var theirs = BRec("M", "2026-11-19 10:00:00"); History.SetPrintState(theirs, PrintState.Printing, t);
            var res = History.Combine(new List<QuoteRecord> { mine }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("PrintState merge: printing start filled", res.Updated == 1 && res.Quotes[0].PrintingSince == T);
            var mineF = BRec("M", "2026-11-19 10:00:00"); History.MarkFailed(mineF, true, t);
            res = History.Combine(new List<QuoteRecord> { mineF }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("PrintState merge: failed here stays failed, not printing", res.Quotes[0].Failed && res.Quotes[0].PrintingSince == "");
            var theirsF = BRec("M", "2026-11-19 10:00:00"); History.MarkFailed(theirsF, true, t);
            var mineP = BRec("M", "2026-11-19 10:00:00"); History.SetPrintState(mineP, PrintState.Printing, t);
            res = History.Combine(new List<QuoteRecord> { mineP }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirsF } }, false, false);
            True("PrintState merge: failed in the backup ends printing", res.Quotes[0].Failed && res.Quotes[0].PrintingSince == "");
        }

        // ---------- issue #7: History filter by the 4 print statuses + Settings "Reset to defaults" keeping personal details ----------

        static void StatusFilterTests()
        {
            var t = new DateTime(2026, 11, 19, 10, 0, 0);
            var none = HistRecord(t);                                                  // new record = Not printed
            var printing = HistRecord(t); History.SetPrintState(printing, PrintState.Printing, t);           // 5 h 30 m, done 15:30
            var done = HistRecord(t); History.SetPrintState(done, PrintState.Printed, t);
            var ended = HistRecord(t); History.SetPrintState(ended, PrintState.Printing, t.AddHours(-6));   // 04:00 + 5 h 30 m = 09:30 < 10:00
            var failed = HistRecord(t); History.SetPrintState(failed, PrintState.Failed, t);
            var all = new[] { none, printing, done, ended, failed };
            int[] states = { History.ShowStateNotPrinted, History.ShowStatePrinting, History.ShowStatePrinted, History.ShowStateFailed };
            // expected per record (rows) and state filter (columns)
            bool[][] want = {
                new[] { true,  false, false, false },
                new[] { false, true,  false, false },
                new[] { false, false, true,  false },
                new[] { false, false, true,  false },    // time run out = Printed
                new[] { false, false, false, true  } };
            bool ok = true;
            for (int i = 0; i < all.Length; i++)
                for (int k = 0; k < states.Length; k++)
                    if (History.Matches(all[i], DateTime.MinValue, null, states[k], "", null, t) != want[i][k]) ok = false;
            True("Status filter: each record in exactly its own status (finished print = Printed)", ok);
            True("Status filter: at 16:00 the printing one is Printed", History.Matches(printing, DateTime.MinValue, null, History.ShowStatePrinted, "", null, t.AddHours(6))
                 && !History.Matches(printing, DateTime.MinValue, null, History.ShowStatePrinting, "", null, t.AddHours(6)));
            // old values unchanged (website): ShowNotPrinted = Printed tick off, so it also shows the failed one; ShowFailed = Failed
            True("Status filter: old ShowNotPrinted = none + failed", History.Matches(none, DateTime.MinValue, null, History.ShowNotPrinted, "", null, t)
                 && History.Matches(failed, DateTime.MinValue, null, History.ShowNotPrinted, "", null, t)
                 && !History.Matches(printing, DateTime.MinValue, null, History.ShowNotPrinted, "", null, t));
            True("Status filter: old ShowFailed = failed only", History.Matches(failed, DateTime.MinValue, null, History.ShowFailed, "", null)
                 && !History.Matches(none, DateTime.MinValue, null, History.ShowFailed, "", null));
            // combined with month, search and the 6-argument overload (DateTime.Now; done has no start, so Printed at any time)
            True("Status filter: + month 2026-11 + search \"bench\"", History.Matches(done, new DateTime(2026, 11, 1), null, History.ShowStatePrinted, "bench", null));
            True("Status filter: other month / other search -> no", !History.Matches(done, new DateTime(2026, 10, 1), null, History.ShowStatePrinted, "", null)
                 && !History.Matches(done, DateTime.MinValue, null, History.ShowStatePrinted, "xyz", null));
            True("Status filter: ShowAll shows every status", History.Matches(failed, DateTime.MinValue, null, History.ShowAll, "", null, t)
                 && History.Matches(printing, DateTime.MinValue, null, History.ShowAll, "", null, t));
            True("Status filter: null record -> false, no error", !History.Matches(null, DateTime.MinValue, null, History.ShowStateFailed, "", null, t));
            // ShowFor / StateOfShow round trip; old values are not states
            bool trip = true;
            foreach (PrintState s in new[] { PrintState.NotPrinted, PrintState.Printing, PrintState.Printed, PrintState.Failed })
            {
                PrintState back;
                if (!History.StateOfShow(History.ShowFor(s), out back) || back != s) trip = false;
            }
            PrintState dummy;
            True("Status filter: ShowFor / StateOfShow round trip, 5..8", trip && History.ShowFor(PrintState.NotPrinted) == 5 && History.ShowFor(PrintState.Failed) == 8
                 && !History.StateOfShow(History.ShowAll, out dummy) && !History.StateOfShow(History.ShowNotPrinted, out dummy)
                 && !History.StateOfShow(History.ShowFailed, out dummy) && !History.StateOfShow(99, out dummy));

            // Settings "Reset to defaults": personal details kept, everything else default, the original untouched
            var mine = new AppSettings();
            mine.QuoteName = "Test Name"; mine.PayMobilePay = "12345678"; mine.PayBank = "1234 5678901234";
            mine.SlicerPath = @"C:\Test\bambu-studio.exe"; mine.Language = "da";
            mine.FriendsMarkupPct = 99; mine.MinimumPrice = 7; mine.InvoiceDueDays = 30;
            var fresh = new AppSettings();
            var d = mine.DefaultsKeepingPersonal();
            True("Reset keeps name, MobilePay, bank, slicer path, language", !ReferenceEquals(d, mine) && d.QuoteName == "Test Name"
                 && d.PayMobilePay == "12345678" && d.PayBank == "1234 5678901234" && d.SlicerPath == @"C:\Test\bambu-studio.exe" && d.Language == "da");
            True("Reset: other settings back to defaults (minimum 50)", d.FriendsMarkupPct == fresh.FriendsMarkupPct && d.MinimumPrice == 50
                 && d.InvoiceDueDays == fresh.InvoiceDueDays);
            True("Reset: the original is unchanged", mine.FriendsMarkupPct == 99 && mine.MinimumPrice == 7 && mine.InvoiceDueDays == 30);
            var blank = new AppSettings(); blank.QuoteName = null; blank.PayMobilePay = null; blank.PayBank = null; blank.SlicerPath = null; blank.Language = null;
            var db = blank.DefaultsKeepingPersonal();
            True("Reset: null details -> \"\"", db.QuoteName == "" && db.PayMobilePay == "" && db.PayBank == "" && db.SlicerPath == "" && db.Language == "");
        }

        // ---------- issue #8 step 1: same-size scale saved per job (QuoteRecord.Scale, percent) ----------

        static void ScaleTests()
        {
            var t = new DateTime(2026, 11, 20, 12, 0, 0);
            // new record and old file without the key = 100 %
            var r = HistRecord(t);
            True("Scale: new record 100 %, factor 1", r.Scale == 100 && History.ScaleOf(r) == 100 && History.ScaleFactor(r) == 1);
            var old = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"Hours\":1}]}");
            True("Scale: old file without the key -> 100", old != null && old[0].Scale == 100 && History.ScaleText(old[0]) == "");
            // bad values read as 100: 0, negative, 0,5 (< 1), 1001 (> 1000); 1 and 1000 kept
            var bad = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"Scale\":0},{\"Id\":\"b\",\"Scale\":-50},{\"Id\":\"c\",\"Scale\":0.5},"
                                        + "{\"Id\":\"d\",\"Scale\":1001},{\"Id\":\"e\",\"Scale\":1},{\"Id\":\"f\",\"Scale\":1000}]}");
            True("Scale: 0 / -50 / 0,5 / 1001 -> 100", bad != null && bad[0].Scale == 100 && bad[1].Scale == 100 && bad[2].Scale == 100 && bad[3].Scale == 100);
            True("Scale: 1 and 1000 kept", bad[4].Scale == 1 && bad[5].Scale == 1000);
            True("Scale: CleanScale NaN / infinity -> 100", History.CleanScale(double.NaN) == 100 && History.CleanScale(double.PositiveInfinity) == 100
                 && History.CleanScale(double.NegativeInfinity) == 100 && History.CleanScale(150) == 150);
            // SetScale + JSON round trip; factor 150 / 100 = 1,5
            History.SetScale(r, 150);
            var back = History.ParseJson(History.ToJson(new List<QuoteRecord> { r }));
            True("Scale: 150 % round trip, factor 1,5", back[0].Scale == 150 && History.ScaleFactor(back[0]) == 1.5);
            History.SetScale(back[0], 5000);
            True("Scale: SetScale 5000 -> 100; null record no error", back[0].Scale == 100);
            History.SetScale(null, 150);
            // CopyQuotes (backup copies) keeps it
            True("Scale: CopyQuotes keeps 150", History.CopyQuotes(new List<QuoteRecord> { r })[0].Scale == 150);
            // texts: 150 -> "150 %", 12,5 -> "12,5 %", 33,333 -> "33,3 %", 100 / 100,04 / bad -> ""
            True("ScaleText: 150 %, 12,5 %, 33,3 %", History.ScaleText(150) == "150 %" && History.ScaleText(12.5) == "12,5 %" && History.ScaleText(33.333) == "33,3 %");
            True("ScaleText: 100 / 100,04 / 0 -> \"\"", History.ScaleText(100) == "" && History.ScaleText(100.04) == "" && History.ScaleText(0) == "");
            True("WithScale: \"3D Benchy (150 %)\", unscaled unchanged", History.WithScale("3D Benchy", r) == "3D Benchy (150 %)"
                 && History.WithScale("3D Benchy", HistRecord(t)) == "3D Benchy" && History.WithScale(null, null) == "");
            // the same job at another size is never "the same quote" (both numbers equal, 2 min apart)
            var a = HistRecord(t); var b = HistRecord(t.AddMinutes(2));
            True("Scale: same numbers same scale = same quote", History.SameQuote(a, b));
            History.SetScale(b, 150);
            True("Scale: other scale = not the same quote", !History.SameQuote(a, b));
            var list = new List<QuoteRecord>();
            History.Add(list, a); History.Add(list, b);
            True("Scale: Add keeps both sizes", list.Count == 2 && list[0].Scale == 150 && list[1].Scale == 100);
            // backup merge: this PC's copy lost the scale (100), the backup's has 150 -> filled; a scaled one here stays
            var mine = BRec("S", "2026-11-20 12:00:00");
            var theirs = BRec("S", "2026-11-20 12:00:00"); History.SetScale(theirs, 150);
            var res = History.Combine(new List<QuoteRecord> { mine }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("Scale merge: 100 here, 150 in the backup -> 150", res.Updated == 1 && res.Quotes[0].Scale == 150);
            var mine2 = BRec("S", "2026-11-20 12:00:00"); History.SetScale(mine2, 80);
            res = History.Combine(new List<QuoteRecord> { mine2 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("Scale merge: 80 here stays 80", res.Quotes[0].Scale == 80);
            // replace restore takes the backup's record as it is
            res = History.Combine(new List<QuoteRecord> { mine }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, true, false);
            True("Scale restore (replace): 150", res.Quotes.Count == 1 && res.Quotes[0].Scale == 150);
        }

        // ---------- issue #8 step 2: separate X/Y/Z scale on History records (QuoteRecord.ScaleX/Y/Z) ----------

        static string Xyz(QuoteRecord r) { var a = History.ScaleXyzOf(r); return a[0] + "/" + a[1] + "/" + a[2]; }

        static void ScaleXyzTests()
        {
            var t = new DateTime(2026, 11, 20, 12, 0, 0);
            // old records: no axes = the uniform Scale on every axis
            var old = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\"},{\"Id\":\"b\",\"Scale\":150}]}");
            True("ScaleXyz: old records uniform 100/100/100 and 150/150/150", History.IsUniformScale(old[0]) && Xyz(old[0]) == "100/100/100"
                 && History.IsUniformScale(old[1]) && Xyz(old[1]) == "150/150/150" && old[1].ScaleX == 0);
            // X/Y/Z: axes stored, Scale = X for older versions, text "150 × 100 × 50 %"
            var r = HistRecord(t);
            History.SetScale(r, 150, 100, 50);
            True("ScaleXyz: 150/100/50 stored, not uniform, Scale = X = 150", Xyz(r) == "150/100/50" && !History.IsUniformScale(r)
                 && r.Scale == 150 && History.ScaleOf(r) == 150);
            True("ScaleXyz: text \"150 × 100 × 50 %\", name \"3D Benchy (150 × 100 × 50 %)\"", History.ScaleText(r) == "150 × 100 × 50 %"
                 && History.WithScale("3D Benchy", r) == "3D Benchy (150 × 100 × 50 %)");
            // JSON round trip; the file still has "Scale":150 for older versions; CopyQuotes keeps the axes
            string json = History.ToJson(new List<QuoteRecord> { r });
            var back = History.ParseJson(json);
            True("ScaleXyz: JSON round trip + \"Scale\":150 written", Xyz(back[0]) == "150/100/50" && json.Contains("\"Scale\":150"));
            True("ScaleXyz: CopyQuotes keeps 150/100/50", Xyz(History.CopyQuotes(new List<QuoteRecord> { r })[0]) == "150/100/50");
            // equal axes fold into the uniform scale; all 100 = not scaled; uniform SetScale clears the axes
            var u = HistRecord(t); History.SetScale(u, 120, 120, 120);
            True("ScaleXyz: 120/120/120 -> uniform 120 %, axes 0", History.IsUniformScale(u) && u.Scale == 120 && u.ScaleX == 0 && u.ScaleZ == 0
                 && History.ScaleText(u) == "120 %");
            var n = HistRecord(t); History.SetScale(n, 100, 100, 100);
            True("ScaleXyz: 100/100/100 -> \"\"", n.Scale == 100 && n.ScaleX == 0 && History.ScaleText(n) == "" && History.WithScale("Vase", n) == "Vase");
            var c = HistRecord(t); History.SetScale(c, 150, 100, 50); History.SetScale(c, 80);
            True("ScaleXyz: uniform SetScale 80 clears the axes", Xyz(c) == "80/80/80" && c.ScaleX == 0 && c.ScaleY == 0 && c.ScaleZ == 0);
            // bad axes read as 100: 0 / 5000 -> 100 (X kept 150); all bad -> uniform 100
            var bad = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"Scale\":150,\"ScaleX\":150,\"ScaleY\":0,\"ScaleZ\":5000},"
                                        + "{\"Id\":\"b\",\"ScaleX\":-1,\"ScaleY\":2000,\"ScaleZ\":0.5}]}");
            True("ScaleXyz: 150/0/5000 -> 150/100/100, \"150 × 100 × 100 %\"", Xyz(bad[0]) == "150/100/100" && History.ScaleText(bad[0]) == "150 × 100 × 100 %");
            True("ScaleXyz: -1/2000/0,5 -> uniform 100", History.IsUniformScale(bad[1]) && bad[1].Scale == 100 && bad[1].ScaleX == 0 && History.ScaleText(bad[1]) == "");
            // texts: one decimal at most; rounding to all 100 = ""
            True("ScaleText xyz: 12,5 × 100 × 33,3 %; 100,04/100/99,96 -> \"\"; 150/150,04/150 -> 150 %", History.ScaleText(12.5, 100, 33.333) == "12,5 × 100 × 33,3 %"
                 && History.ScaleText(100.04, 100, 99.96) == "" && History.ScaleText(150, 150.04, 150) == "150 %");
            // a different X/Y/Z is a different quote (same numbers, 2 min apart)
            var a1 = HistRecord(t); var a2 = HistRecord(t.AddMinutes(2));
            History.SetScale(a1, 150, 100, 50); History.SetScale(a2, 150, 100, 50);
            bool same = History.SameQuote(a1, a2);
            History.SetScale(a2, 150, 100, 60);
            bool otherZ = History.SameQuote(a1, a2);
            History.SetScale(a2, 150);
            True("ScaleXyz: same X/Y/Z = same quote; other Z or uniform 150 = not", same && !otherZ && !History.SameQuote(a1, a2));
            // merge: an older version kept only Scale = 150 here -> the backup's axes come back; none here -> filled; own axes stay
            var theirs = BRec("X", "2026-11-20 12:00:00"); History.SetScale(theirs, 150, 100, 50);
            var mine = BRec("X", "2026-11-20 12:00:00"); History.SetScale(mine, 150);
            var res = History.Combine(new List<QuoteRecord> { mine }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("ScaleXyz merge: X only here (older version) -> 150/100/50", res.Updated == 1 && Xyz(res.Quotes[0]) == "150/100/50");
            var plain = BRec("X", "2026-11-20 12:00:00");
            res = History.Combine(new List<QuoteRecord> { plain }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("ScaleXyz merge: none here -> 150/100/50", Xyz(res.Quotes[0]) == "150/100/50");
            var own = BRec("X", "2026-11-20 12:00:00"); History.SetScale(own, 80, 90, 100);
            res = History.Combine(new List<QuoteRecord> { own }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("ScaleXyz merge: own 80/90/100 stays", Xyz(res.Quotes[0]) == "80/90/100");
        }

        // ---------- issue #8 step 3: prime tower setting + History fields ----------

        // ---------- batch & plates (issue #9/#10): copies, plates, plate change fee, orders, texts ----------
        static AppSettings BatchSettings()
        {
            var s = new AppSettings();
            s.PrinterWatts = 100; s.FriendsMarkupPct = 25; s.OthersMarkupPct = 75; s.MachineRatePerHour = 1.71; s.AutoWearRate = 0;
            s.FailedPrintPct = 0; s.FailedPayback = 0; s.MinimumPrice = 50; s.RoundTo = 0; s.PlateChangeFee = 0;
            return s;
        }

        // 12 copies, 4 per plate = 3 plates, 240 g, 12 h 00 m in total (180 kr./kg, 2,00 kr./kWh)
        static QuoteInput BatchInput(int plates)
        {
            return new QuoteInput { Hours = 12, Minutes = 0, FilamentGrams = 240, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2.00,
                                    Quantity = 12, Plates = plates };
        }

        static QuoteRecord BatchRec(QuoteInput q, AppSettings s, DateTime t, string name)
        {
            return History.FromQuote(q, Pricing.Calculate(q, s), s, name, "", t);
        }

        static List<HistoryPart> PartsOf3(int a, int b, int c)
        {
            return new List<HistoryPart> { new HistoryPart { File = "stand.stl", Slot = 1, Copies = a },
                new HistoryPart { File = "hook.stl", Slot = 2, Copies = b }, new HistoryPart { File = "clip.stl", Slot = 2, Copies = c } };
        }

        static void BatchTests()
        {
            var s = BatchSettings();
            // totals: material 0,240 x 180 = 43,20; power 12 x 0,1 x 2,00 = 2,40; base 45,60
            // customer 45,60 x 1,75 = 79,80 + 12 x 1,71 = 20,52 -> 100,32; friends 45,60 x 1,25 = 57,00 + 20,52 = 77,52
            var r0 = Pricing.Calculate(BatchInput(3), s);
            Eq("Batch: 12 copies / 3 plates, fee 0: customer 100,32", 100.32, r0.OthersPrice);
            Eq("Batch: fee 0: friends 77,52", 77.52, r0.FriendsPrice);
            Eq("Batch: fee 0 -> PlateFee 0", 0, r0.PlateFee);
            var noPlates = BatchInput(0); noPlates.Quantity = 0;
            True("Batch: Quantity / Plates 0 = the same price as before (no new inputs)",
                 Pricing.Calculate(noPlates, s).OthersPrice == r0.OthersPrice && Pricing.Calculate(Example(), s).OthersPrice == Pricing.Calculate(Example(), BatchSettings()).OthersPrice);
            // fee 5 kr. per extra plate: (3 - 1) x 5 = 10 -> 110,32 / 87,52
            s.PlateChangeFee = 5;
            var r5 = Pricing.Calculate(BatchInput(3), s);
            Eq("Batch: fee 5 x 2 extra plates = 10", 10, r5.PlateFee);
            Eq("Batch: fee 5: customer 100,32 + 10 = 110,32", 110.32, r5.OthersPrice);
            Eq("Batch: fee 5: friends 77,52 + 10 = 87,52", 87.52, r5.FriendsPrice);
            Eq("Batch: fee with 1 plate = none: 100,32", 100.32, Pricing.Calculate(BatchInput(1), s).OthersPrice);
            Eq("Batch: fee with plates 0 (unknown) = none: 100,32", 100.32, Pricing.Calculate(BatchInput(0), s).OthersPrice);
            Eq("PlateFee(3, 5) = 10", 10, Pricing.PlateFee(3, 5));
            True("PlateFee: 1 / 0 plates, fee -5 / NaN / inf / 1e7 -> 0", Pricing.PlateFee(1, 5) == 0 && Pricing.PlateFee(0, 5) == 0
                 && Pricing.PlateFee(3, -5) == 0 && Pricing.PlateFee(3, double.NaN) == 0 && Pricing.PlateFee(3, double.PositiveInfinity) == 0
                 && Pricing.PlateFee(3, 1e7) == 0);
            // the fee is below the minimum: 1 copy 20 g 1 h 30 m on 2 plates: 3,90 x 1,75 = 6,825 + 2,565 + 5 = 14,39 -> 50,00
            var small = new QuoteInput { Hours = 1, Minutes = 30, FilamentGrams = 20, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2.00, Plates = 2 };
            Eq("Batch: small job + fee 5 still lifted to the minimum 50,00", 50, Pricing.Calculate(small, s).OthersPrice);
            var sb = Pricing.Breakdown(small, s, false, null);
            True("Batch breakdown: small job: plates 5,00 + minimum 50 - 14,39 = 35,61", Line(sb, QuoteLine.Plates, -1) == "5.00"
                 && Line(sb, QuoteLine.Minimum, -1) == "35.61" && sb.TotalOre == 5000);

            // breakdown: material 43,20 + power 2,40 + wear 20,52 + plates 10,00 + work 45,60 x 0,75 = 34,20 = 110,32
            var b = Pricing.Breakdown(BatchInput(3), s, false, null);
            var pl = Find(b, QuoteLine.Plates);
            True("Batch breakdown: plates line 10,00 (2 extra x 5,00), after wear", pl != null && pl.Ore == 1000 && pl.Quantity == 2 && pl.UnitPrice == 5
                 && b.Lines.IndexOf(pl) == b.Lines.IndexOf(Find(b, QuoteLine.Wear)) + 1);
            True("Batch breakdown: 43,20 / 2,40 / 20,52 / 34,20, total 110,32", Line(b, QuoteLine.Material, 0) == "43.20" && Line(b, QuoteLine.Power, -1) == "2.40"
                 && Line(b, QuoteLine.Wear, -1) == "20.52" && Line(b, QuoteLine.Work, -1) == "34.20" && b.TotalOre == 11032);
            Adds("Batch breakdown adds up", b);
            var bf = Pricing.Breakdown(BatchInput(3), s, true, null);
            True("Batch breakdown friends: plates 10,00, work 45,60 x 0,25 = 11,40, total 87,52", Line(bf, QuoteLine.Plates, -1) == "10.00"
                 && Line(bf, QuoteLine.Work, -1) == "11.40" && bf.TotalOre == 8752);
            True("Batch breakdown: fee 0 -> no plates line", Find(Pricing.Breakdown(BatchInput(3), BatchSettings(), false, null), QuoteLine.Plates) == null);
            // round to 5 kr.: 110,32 -> 115,00, rounding line 4,68
            var s5 = s.Clone(); s5.RoundTo = 5;
            var br = Pricing.Breakdown(BatchInput(3), s5, false, null);
            True("Batch: RoundTo 5: 110,32 -> 115,00, rounding 4,68", br.TotalOre == 11500 && Line(br, QuoteLine.Rounding, -1) == "4.68");
            Adds("Batch breakdown with rounding adds up", br);

            // unit price = shown total / N, not rounded up: 110,32 / 12 = 9,1933 -> "9,19 kr."; 72,72 / 4 = 18,18
            Eq("UnitPrice 110,32 / 12 = 9,1933", 9.19333, Pricing.UnitPrice(110.32, 12));
            True("UnitPrice shown 9,19 kr.", Num.Kr(Pricing.UnitPrice(110.32, 12)) == "9,19 kr.");
            True("UnitPrice: quantity 0 = 1, total NaN / -1 = 0", Pricing.UnitPrice(50, 0) == 50 && Pricing.UnitPrice(double.NaN, 3) == 0 && Pricing.UnitPrice(-1, 3) == 0);

            // plates needed
            True("PlatesNeeded: 40 / 28 = 2, 28 / 28 = 1, 5 / 0 = 5, 0 / 4 = 1, 12 / 4 = 3", Pricing.PlatesNeeded(40, 28) == 2 && Pricing.PlatesNeeded(28, 28) == 1
                 && Pricing.PlatesNeeded(5, 0) == 5 && Pricing.PlatesNeeded(0, 4) == 1 && Pricing.PlatesNeeded(12, 4) == 3);
            True("PlatesNeeded agrees with online's BatchPlates (40/28, 56/28, 12/4, 1/1)", Pricing.PlatesNeeded(40, 28) == BambuSlicer.BatchPlates(40, 28)
                 && Pricing.PlatesNeeded(56, 28) == BambuSlicer.BatchPlates(56, 28) && Pricing.PlatesNeeded(12, 4) == BambuSlicer.BatchPlates(12, 4)
                 && Pricing.PlatesNeeded(1, 1) == BambuSlicer.BatchPlates(1, 1));
            BatchSlicerTests();

            // per plate, not per copy: one two-colour copy 8,99 g of which 5,00 g tower + flush, 10 copies on 1 plate:
            // (8,99 - 5,00) x 10 + 5,00 x 1 = 39,90 + 5,00 = 44,90 (x 10 would be 89,90)
            Eq("CopiesTotal: tower + flush once per plate: 44,90", 44.90, Pricing.CopiesTotal(8.99, 5.00, 10, 1));
            Eq("CopiesTotal: same on 2 plates: 39,90 + 10,00 = 49,90", 49.90, Pricing.CopiesTotal(8.99, 5.00, 10, 2));
            Eq("CopiesTotal: per plate above one copy is held to it: 2 x 1 = 2", 2, Pricing.CopiesTotal(2, 5, 10, 1));
            True("CopiesTotal: NaN / negative -> 0; copies / plates 0 -> 1", Pricing.CopiesTotal(double.NaN, 1, 3, 1) == 0 && Pricing.CopiesTotal(-4, 0, 3, 1) == 0
                 && Pricing.CopiesTotal(7, double.NaN, 3, 1) == 21 && Pricing.CopiesTotal(7, 2, 0, 0) == 7);
            // ForCopies: 20 g / 1 h 30 m x 12 on 3 plates (nothing per plate) = 240 g / 18 h 00 m
            var one = new QuoteInput { Hours = 1, Minutes = 30, FilamentGrams = 20, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2.00, PaybackDebt = 3 };
            var fc = Pricing.ForCopies(one, 12, 3, 0, 0, null);
            True("ForCopies: 12 x 20 g / 1 h 30 m = 240 g / 18 h 00 m, prices copied, Quantity 12 / Plates 3", fc.FilamentGrams == 240 && fc.Hours == 18 && fc.Minutes == 0
                 && fc.FilamentDkkPerKg == 180 && fc.ElectricityDkkPerKwh == 2.00 && fc.PaybackDebt == 3 && fc.Quantity == 12 && fc.Plates == 3);
            True("ForCopies: one copy not changed", one.FilamentGrams == 20 && one.Hours == 1 && one.Minutes == 30 && one.Quantity == 0);
            var m45 = Pricing.ForCopies(new QuoteInput { Minutes = 45 }, 3, 1, 0, 0, null);
            True("ForCopies: 3 x 45 m = 2 h 15 m (minute carry)", m45.Hours == 2 && m45.Minutes == 15);
            // time per plate: one copy 45 m = 2700 s with 900 s tower / colour changes, 10 on 1 plate:
            // (2700 - 900) x 10 + 900 = 18900 s = 315 m = 5 h 15 m
            var tp = Pricing.ForCopies(new QuoteInput { Minutes = 45, FilamentGrams = 8.99 }, 10, 1, 5.00, 900, null);
            True("ForCopies: tower time once per plate: 5 h 15 m, 44,90 g", tp.Hours == 5 && tp.Minutes == 15 && Math.Abs(tp.FilamentGrams - 44.90) < 1e-9);
            True("ForCopies: 90 s -> 2 m (half up), 75 s -> 1 m", Pricing.ForCopies(new QuoteInput { Minutes = 1 }, 2, 1, 0, 30, null).Minutes == 2
                 && Pricing.ForCopies(new QuoteInput { Minutes = 1 }, 2, 1, 0, 45, null).Minutes == 1);
            // colour lines: A 5,00 g (2,00 per plate), B 3,99 g (3,00 per plate), 10 copies on 2 plates:
            // A 3,00 x 10 + 2,00 x 2 = 34,00; B 0,99 x 10 + 3,00 x 2 = 15,90
            var mc = new QuoteInput { Minutes = 30, FilamentGrams = 8.99, Filaments = new List<FilamentLine> {
                new FilamentLine { Grams = 5.00, PricePerKg = 200 }, new FilamentLine { Grams = 3.99, PricePerKg = 250 } } };
            var mcb = Pricing.ForCopies(mc, 10, 2, 5.00, 0, new List<double> { 2.00, 3.00 });
            True("ForCopies: colour lines per plate: 34,00 g / 15,90 g, prices kept", Math.Abs(mcb.Filaments[0].Grams - 34) < 1e-9
                 && Math.Abs(mcb.Filaments[1].Grams - 15.9) < 1e-9 && mcb.Filaments[1].PricePerKg == 250 && !ReferenceEquals(mcb.Filaments[0], mc.Filaments[0]));
            True("ForCopies: missing line per-plate grams = 0 (x copies)", Math.Abs(Pricing.ForCopies(mc, 10, 2, 0, 0, new List<double> { 2.00 }).Filaments[1].Grams - 39.9) < 1e-9);

            // History: FromQuote takes Quantity / Plates / the fee snapshot; SetCopies; JSON round trip; RecordBreakdown
            var t = new DateTime(2026, 11, 24, 12, 0, 0);
            var rec = BatchRec(BatchInput(3), s, t, "Benchy");
            True("Batch record: Quantity 12, Plates 3, fee 5 snapshot, totals 240 g / 12 h / 110,32", rec.Quantity == 12 && rec.Plates == 3 && rec.PlateChangeFee == 5
                 && rec.Grams == 240 && rec.Hours == 12 && Math.Abs(rec.OthersPrice - 110.32) < 1e-9);
            History.SetCopies(rec, 12, 3, 4, true);
            var back = History.ParseJson(History.ToJson(new List<QuoteRecord> { rec }))[0];
            True("Batch record JSON round trip: 12 / 3 / 4 / estimated / fee 5", back.Quantity == 12 && back.Plates == 3 && back.PerPlate == 4
                 && back.CopiesEstimated == 1 && back.PlateChangeFee == 5 && back.OrderId == "");
            var cp = History.CopyQuotes(new List<QuoteRecord> { rec })[0];
            True("Batch record: CopyQuotes keeps it", cp.Quantity == 12 && cp.Plates == 3 && cp.PerPlate == 4 && cp.CopiesEstimated == 1);
            var rb = History.RecordBreakdown(back, new AppSettings(), false);
            True("Batch record: RecordBreakdown has the plates line 10,00 and total 110,32 (fee from the record, not today's 0)",
                 rb.TotalOre == 11032 && Line(rb, QuoteLine.Plates, -1) == "10.00");
            True("Batch record: ToInput gives Quantity 12 / Plates 3", History.ToInput(back).Quantity == 12 && History.ToInput(back).Plates == 3);
            // old record: 1 copy, plates unknown (PlatesOf = 1), no fee
            var old = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"Hours\":1,\"OthersPrice\":50}]}")[0];
            True("Batch: old record -> Quantity 1, Plates 0 (PlatesOf 1), PerPlate 0, not estimated, OrderId \"\", fee 0",
                 old.Quantity == 1 && old.Plates == 0 && History.PlatesOf(old) == 1 && old.PerPlate == 0 && old.CopiesEstimated == 0 && old.OrderId == ""
                 && old.PlateChangeFee == 0 && History.QuantityOf(old) == 1);
            var bad = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"Quantity\":0,\"Plates\":-2,\"PerPlate\":200000,\"CopiesEstimated\":5,"
                                        + "\"OrderId\":\"  o1  \",\"PlateChangeFee\":-3},{\"Id\":\"b\",\"Quantity\":-3},{\"Id\":\"c\",\"Quantity\":200000,"
                                        + "\"OrderId\":\"" + new string('x', 60) + "\"}]}");
            True("Batch: bad values cleaned (0 / -3 / 200000 copies -> 1, plates -2 -> 0, per plate 200000 -> 0, flag 5 -> 0, fee -3 -> 0)",
                 bad[0].Quantity == 1 && bad[0].Plates == 0 && bad[0].PerPlate == 0 && bad[0].CopiesEstimated == 0 && bad[0].PlateChangeFee == 0
                 && bad[1].Quantity == 1 && bad[2].Quantity == 1);
            True("Batch: OrderId trimmed, cut to 40", bad[0].OrderId == "o1" && bad[2].OrderId.Length == 40);
            History.SetCopies(old, 600, -1, 7, false);
            True("SetCopies cleans: 600 kept (reading range), plates -1 -> 0, per plate 7", old.Quantity == 600 && old.Plates == 0 && old.PerPlate == 7);
            // an older app version re-saves the file without the keys: the total price stays (totals stored)
            string json = History.ToJson(new List<QuoteRecord> { rec });
            string stripped = System.Text.RegularExpressions.Regex.Replace(json, "\"(Quantity|Plates|PerPlate|CopiesEstimated|OrderId|PlateChangeFee)\":(\"[^\"]*\"|[^,}]*),?", "");
            var older = History.ParseJson(stripped);
            True("Batch: older version's re-save: still 110,32 / 240 g / 12 h, copies 1", older != null && older.Count == 1
                 && Math.Abs(older[0].OthersPrice - 110.32) < 1e-9 && older[0].Grams == 240 && older[0].Hours == 12 && older[0].Quantity == 1);
            True("Batch: older version's re-save: bill total still 110,32 (scaled)", History.RecordBreakdown(older[0], new AppSettings(), false).TotalOre == 11032);
            // merge: lost here -> back from the backup copy; OrderId too
            var theirs = BRec("B", "2026-11-24 12:00:00"); History.SetCopies(theirs, 12, 3, 4, false); theirs.PlateChangeFee = 5; theirs.OrderId = "o7";
            var mine = BRec("B", "2026-11-24 12:00:00");
            var res = History.Combine(new List<QuoteRecord> { mine }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("Batch merge: copies / plates / per plate / fee / order filled from the backup", res.Updated == 1 && res.Quotes[0].Quantity == 12
                 && res.Quotes[0].Plates == 3 && res.Quotes[0].PerPlate == 4 && res.Quotes[0].PlateChangeFee == 5 && res.Quotes[0].OrderId == "o7");
            var mine2 = BRec("B", "2026-11-24 12:00:00"); History.SetCopies(mine2, 2, 1, 2, false);
            res = History.Combine(new List<QuoteRecord> { mine2 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("Batch merge: known copies here stay", res.Quotes[0].Quantity == 2 && res.Quotes[0].Plates == 1);
            // other copies / plates / order = another quote
            var q1 = HistRecord(t); var q2 = HistRecord(t.AddMinutes(2));
            bool same = History.SameQuote(q1, q2);
            q2.Quantity = 2; bool sameQty = History.SameQuote(q1, q2); q2.Quantity = 1;
            q2.Plates = 2; bool samePlates = History.SameQuote(q1, q2); q2.Plates = 0;
            q2.OrderId = "o1"; bool sameOrder = History.SameQuote(q1, q2);
            True("Batch: same = same quote; other copies / plates / order = not", same && !sameQty && !samePlates && !sameOrder);

            // texts EN + DA
            bool wasDanish = L.Danish;
            try
            {
                L.Danish = false;
                True("Text: 4 × Name, 1 copy = name", History.CopiesName("Benchy", 4) == "4 × Benchy" && History.CopiesName("Benchy", 1) == "Benchy"
                     && History.CopiesName(null, 1) == "");
                var named = HistRecord(t); History.SetCopies(named, 4, 2, 2, false); History.SetScale(named, 150);
                True("Text: WithCopies(WithScale) = 4 × Benchy (150 %)", History.WithCopies(History.WithScale(History.ModelText(named), named), named) == "4 × Benchy (150 %)");
                True("Text EN: 4 pcs · 18,18 kr. each", History.EachText(72.72, 4) == "4 pcs · 18,18 kr. each" && History.EachText(72.72, 1) == "");
                True("Text EN: 12 pcs · 9,19 kr. each", History.EachText(110.32, 12) == "12 pcs · 9,19 kr. each");
                True("Text EN: 18,18 kr. each", History.EachPriceText(72.72, 4) == "18,18 kr. each");
                True("Text EN: on 3 plates / 1 plate = \"\"", History.OnPlatesText(3) == "on 3 plates" && History.OnPlatesText(1) == "");
                True("Text EN: Quantity: 4 pcs", History.QuantityLabel() == "Quantity" && History.QuantityText(4) == "Quantity: 4 pcs");
                True("Text EN: Prints: 3 models, 9 pcs / 1 model", History.PrintsText(3, 9) == "Prints: 3 models, 9 pcs" && History.PrintsText(1, 4) == "Prints: 1 model, 4 pcs");
                True("Text EN: Plates: 5", History.PlatesCountText(5) == "Plates: 5");
                L.Danish = true;
                True("Text DA: 4 stk. · 18,18 kr. pr. stk.", History.EachText(72.72, 4) == "4 stk. · 18,18 kr. pr. stk.");
                True("Text DA: på 3 plader", History.OnPlatesText(3) == "på 3 plader");
                True("Text DA: Antal: 4 stk.", History.QuantityLabel() == "Antal" && History.QuantityText(4) == "Antal: 4 stk.");
                True("Text DA: Print: 3 modeller, 9 stk. / 1 model", History.PrintsText(3, 9) == "Print: 3 modeller, 9 stk." && History.PrintsText(1, 4) == "Print: 1 model, 4 stk.");
                True("Text DA: Plader: 5", History.PlatesCountText(5) == "Plader: 5");
            }
            finally { L.Danish = wasDanish; }

            // orders: minimum once per order (= combined bill), counts, order id
            var so = BatchSettings();
            var a = BatchRec(BatchInput(3), so, t, "Benchy");                     // 100,32
            var oneIn = new QuoteInput { Hours = 1, Minutes = 30, FilamentGrams = 20, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2.00 };
            var bRec = BatchRec(oneIn, so, t.AddMinutes(1), "Clip");               // 9,39 -> 50 alone
            var cIn = new QuoteInput { Hours = 1, FilamentGrams = 10, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2.00 };
            var cRec = BatchRec(cIn, so, t.AddMinutes(2), "Hook");                 // 2,00 x 1,75 = 3,50 + 1,71 = 5,21 -> 50 alone
            Eq("Order: Clip alone = 50 (minimum)", 50, bRec.OthersPrice);
            var list = new List<QuoteRecord> { cRec, bRec, a };                   // newest first
            string oid = History.SetOrder(new List<QuoteRecord> { bRec, a });
            True("SetOrder: id = the oldest record's Id, on both", oid == a.Id && a.OrderId == oid && bRec.OrderId == oid && cRec.OrderId == "");
            var jobs = History.OrderJobs(list, " " + oid.ToUpperInvariant() + " ");
            True("OrderJobs: 2 jobs oldest first (trim, any case)", jobs.Count == 2 && jobs[0] == a && jobs[1] == bRec);
            True("OrderJobs: empty id = none", History.OrderJobs(list, "  ").Count == 0 && History.OrderJobs(null, oid).Count == 0);
            // 100,32 + 9,39 = 109,71 >= 50: no top-up (per model it would be 100,32 + 50 = 150,32)
            var ob = History.OrderBill(jobs, so, false);
            True("OrderBill: 100,32 + 9,39 = 109,71, no minimum top-up", Math.Abs(ob.Total - 109.71) < 1e-9 && ob.TopUp == 0);
            // two small lines: 9,39 + 5,21 = 14,60 -> top-up 35,40 -> 50,00 once
            var ob2 = History.OrderBill(new List<QuoteRecord> { bRec, cRec }, so, false);
            True("OrderBill: 9,39 + 5,21 = 14,60, top-up 35,40 -> 50,00", Math.Abs(ob2.Sum - 14.60) < 1e-9 && Math.Abs(ob2.TopUp - 35.40) < 1e-9
                 && Math.Abs(ob2.Total - 50) < 1e-9);
            var pc = History.CountPrints(jobs);
            True("CountPrints: 2 models, 12 + 1 = 13 pcs, 3 + 1 = 4 plates", pc.Models == 2 && pc.Pieces == 13 && pc.Plates == 4);
            True("CountPrints: null = 0", History.CountPrints(null).Models == 0);
            // the order's invoice (group MakeInvoice) has the quote's total
            var inv = so.Clone();
            History.MakeInvoice(list, jobs, t.AddHours(1), inv, false);
            Eq("Order invoice total = order quote total 109,71", 109.71, a.OthersPrice + bRec.OthersPrice);
            True("ClearOrder: 2 changed, then none", History.ClearOrder(jobs) == 2 && a.OrderId == "" && History.ClearOrder(jobs) == 0);
            True("SetOrder: none = \"\"", History.SetOrder(null) == "" && History.SetOrder(new List<QuoteRecord>()) == "");
            // combined bill breakdown sums the plates lines: 12 copies on 3 plates + on 2 plates, fee 5: 10 + 5 = 15,00 (3 extra plates x 5)
            var g1 = BatchRec(BatchInput(3), s, t, "A"); var g2 = BatchRec(BatchInput(2), s, t.AddMinutes(1), "B");
            var gb = History.GroupBreakdown(History.GroupBill(new List<QuoteRecord> { g1, g2 }, s, false), s);
            QuoteLine gpl = null; foreach (var l in gb.Lines) if (l.Key == QuoteLine.Plates) gpl = l;
            True("GroupBreakdown: plates 15,00, 3 extra plates x 5,00; total 110,32 + 105,32 = 215,64", gpl != null && gpl.Ore == 1500 && gpl.Quantity == 3
                 && gpl.UnitPrice == 5 && gb.TotalOre == 21564);

            // copies per file of a several-files job (HistoryPart.Copies)
            var pr = HistRecord(t);
            History.SetParts(pr, new List<HistoryPart> {
                new HistoryPart { File = @"C:\m\stand.stl", Slot = 1, Copies = 4 },
                new HistoryPart { File = "hook.stl", Slot = 2, Copies = 0 },
                new HistoryPart { File = "clip.stl", Slot = 2, Copies = 30 } }, false);
            var pcs = History.PartCopies(pr);
            True("PartCopies: 4, 0 -> 1, 30", pcs != null && pcs.Count == 3 && pcs[0] == 4 && pcs[1] == 1 && pcs[2] == 30);
            var items = History.BatchItems(pr);
            True("BatchItems: path, file name, copies, slot", items != null && items.Count == 3 && items[0].Path == @"C:\m\stand.stl" && items[0].Name == "stand.stl"
                 && items[0].Copies == 4 && items[0].Slot == 1 && items[2].Name == "clip.stl" && items[2].Copies == 30 && items[2].Slot == 2);
            True("PartCopies / BatchItems: normal job = null", History.PartCopies(HistRecord(t)) == null && History.BatchItems(HistRecord(t)) == null
                 && History.PartCopies(null) == null);
            var prBack = History.ParseJson(History.ToJson(new List<QuoteRecord> { pr }))[0];
            True("Part copies: JSON round trip 4 / 1 / 30; CopyQuotes keeps them", History.PartCopies(prBack)[0] == 4 && History.PartCopies(prBack)[2] == 30
                 && History.CopyQuotes(new List<QuoteRecord> { pr })[0].Parts.Files[2].Copies == 30);
            var oldParts = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"Parts\":{\"Together\":false,\"Files\":[{\"File\":\"a.stl\",\"Slot\":1},"
                                             + "{\"File\":\"b.stl\",\"Slot\":2,\"Copies\":0},{\"File\":\"c.stl\",\"Slot\":2,\"Copies\":-4}]}}]}")[0];
            var oc = History.PartCopies(oldParts);
            True("Part copies: missing / 0 / -4 -> 1", oc[0] == 1 && oc[1] == 1 && oc[2] == 1);
            // an older version dropped the copies (all 1): the backup's copy brings them back; known copies here stay
            var pTheirs = BRec("P", "2026-11-24 12:00:00"); History.SetParts(pTheirs, PartsOf3(4, 1, 30), false);
            var pMine = BRec("P", "2026-11-24 12:00:00"); History.SetParts(pMine, PartsOf3(1, 1, 1), false);
            res = History.Combine(new List<QuoteRecord> { pMine }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { pTheirs } }, false, false);
            var mc3 = History.PartCopies(res.Quotes[0]);
            True("Part copies merge: lost here -> 4 / 1 / 30 from the backup", mc3[0] == 4 && mc3[1] == 1 && mc3[2] == 30);
            var pMine2 = BRec("P", "2026-11-24 12:00:00"); History.SetParts(pMine2, PartsOf3(2, 1, 1), false);
            res = History.Combine(new List<QuoteRecord> { pMine2 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { pTheirs } }, false, false);
            True("Part copies merge: copies known here stay (2 / 1 / 1)", History.PartCopies(res.Quotes[0])[0] == 2 && History.PartCopies(res.Quotes[0])[2] == 1);
            var pOther = BRec("P", "2026-11-24 12:00:00"); History.SetParts(pOther, new List<HistoryPart> { new HistoryPart { File = "x.stl" } }, false);
            res = History.Combine(new List<QuoteRecord> { pOther }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { pTheirs } }, false, false);
            True("Part copies merge: other files = not touched", res.Quotes[0].Parts.Files.Count == 1 && res.Quotes[0].Parts.Files[0].File == "x.stl");

            // names of several-files jobs with copies (tester T2): per file, not "60 × a, b"
            bool wasDa2 = L.Danish;
            try
            {
                L.Danish = false;
                var two = HistRecord(t); two.Model = "red_block.stl, blue_block.stl";
                History.SetParts(two, new List<HistoryPart> { new HistoryPart { File = @"C:\m\red_block.stl", Copies = 30 },
                    new HistoryPart { File = "blue_block.stl", Slot = 2, Copies = 30 } }, false);
                History.SetCopies(two, 60, 2, 30, false);
                True("WithCopies 2 files x 30: 30 × red_block.stl, 30 × blue_block.stl",
                     History.WithCopies(History.ModelText(two), two) == "30 × red_block.stl, 30 × blue_block.stl");
                History.SetScale(two, 150);
                True("WithCopies 2 files x 30 + scale: (150 %) kept at the end",
                     History.WithCopies(History.WithScale(History.ModelText(two), two), two) == "30 × red_block.stl, 30 × blue_block.stl (150 %)");
                History.SetScale(two, 100);
                History.SetParts(two, new List<HistoryPart> { new HistoryPart { File = "red_block.stl", Copies = 4 },
                    new HistoryPart { File = "blue_block.stl", Slot = 2, Copies = 1 } }, false);
                History.SetCopies(two, 5, 1, 5, false);
                True("WithCopies 4 + 1: 4 × red_block.stl, blue_block.stl (no 1 ×)", History.WithCopies(History.ModelText(two), two) == "4 × red_block.stl, blue_block.stl");
                True("WithCopies: another name with per-file copies -> Name (5 pcs)", History.WithCopies("Desk set", two) == "Desk set (5 pcs)");
                History.SetParts(two, new List<HistoryPart> { new HistoryPart { File = "red_block.stl" }, new HistoryPart { File = "blue_block.stl", Slot = 2 } }, false);
                History.SetCopies(two, 2, 1, 2, false);
                True("WithCopies: 2 files, 1 each = name unchanged", History.WithCopies(History.ModelText(two), two) == "red_block.stl, blue_block.stl");
                History.SetCopies(two, 60, 2, 30, false);   // an older version dropped the per-file copies
                True("WithCopies: per-file copies lost, 60 pcs -> red_block.stl, blue_block.stl (60 pcs)",
                     History.WithCopies(History.ModelText(two), two) == "red_block.stl, blue_block.stl (60 pcs)");
                var single = HistRecord(t); History.SetCopies(single, 40, 2, 28, false);
                True("WithCopies single file unchanged: 40 × Benchy; null name = \"\"", History.WithCopies("Benchy", single) == "40 × Benchy"
                     && History.WithCopies(null, HistRecord(t)) == "");
                L.Danish = true;
                History.SetParts(two, new List<HistoryPart> { new HistoryPart { File = "red_block.stl", Copies = 30 },
                    new HistoryPart { File = "blue_block.stl", Slot = 2, Copies = 30 } }, false);
                True("WithCopies DA: another name -> Navn (60 stk.)", History.WithCopies("Navn", two) == "Navn (60 stk.)");
            }
            finally { L.Danish = wasDa2; }

            // settings: PlateChangeFee default 0, QuoteShowUnitPrice default 1
            True("PlateChangeFee 0 / QuoteShowUnitPrice 1 by default (new / new install)", new AppSettings().PlateChangeFee == 0 && AppSettings.FromIni(null).PlateChangeFee == 0
                 && new AppSettings().QuoteShowUnitPrice == 1 && AppSettings.FromIni(null).QuoteShowUnitPrice == 1);
            var ps = new AppSettings(); ps.PlateChangeFee = 7.5; ps.QuoteShowUnitPrice = 0;
            var psBack = AppSettings.FromIni(ps.ToIni());
            True("PlateChangeFee 7,5 / QuoteShowUnitPrice 0 survive the ini", psBack.PlateChangeFee == 7.5 && psBack.QuoteShowUnitPrice == 0);
            var psBad = AppSettings.FromIni(new[] { "PlateChangeFee=-3", "QuoteShowUnitPrice=7" });
            True("PlateChangeFee -3 -> 0, QuoteShowUnitPrice 7 -> 1, missing -> defaults", psBad.PlateChangeFee == 0 && psBad.QuoteShowUnitPrice == 1
                 && AppSettings.FromIni(new[] { "SetupDone=1" }).PlateChangeFee == 0 && AppSettings.FromIni(new[] { "SetupDone=1" }).QuoteShowUnitPrice == 1);
            True("PlateChangeFee: Clone keeps, Reset to defaults -> 0 / 1", ps.Clone().PlateChangeFee == 7.5 && ps.DefaultsKeepingPersonal().PlateChangeFee == 0
                 && ps.DefaultsKeepingPersonal().QuoteShowUnitPrice == 1);
        }

        // online's pure batch helpers (SliceInfo.cs, batch-plates)
        static void BatchSlicerTests()
        {
            True("Copy limits: MaxCopies 500, WarnCopies 200, 1..500 in range", BambuSlicer.MaxCopies == 500 && BambuSlicer.WarnCopies == 200
                 && BambuSlicer.CopiesInRange(1) && BambuSlicer.CopiesInRange(500) && !BambuSlicer.CopiesInRange(0) && !BambuSlicer.CopiesInRange(501));
            var ca = BambuSlicer.CloneArgs(new List<int> { 4, 30 });
            True("CloneArgs 4, 30 -> --clone-objects 4,30", ca.Count == 2 && ca[0] == "--clone-objects" && ca[1] == "4,30");
            bool t0 = false, tSum = false, tNull = false;
            try { BambuSlicer.CloneArgs(new List<int> { 0 }); } catch (SliceException) { t0 = true; }
            try { BambuSlicer.CloneArgs(new List<int> { 300, 201 }); } catch (SliceException) { tSum = true; }
            try { BambuSlicer.CloneArgs(null); } catch (ArgumentException) { tNull = true; }
            True("CloneArgs: 0 / sum 501 -> SliceException, null -> ArgumentException", t0 && tSum && tNull);
            var ra = BambuSlicer.RepetitionArgs(40);
            bool r1 = false, r501 = false;
            try { BambuSlicer.RepetitionArgs(1); } catch (SliceException) { r1 = true; }
            try { BambuSlicer.RepetitionArgs(501); } catch (SliceException) { r501 = true; }
            True("RepetitionArgs 40 -> --repetitions 40; 1 / 501 refused", ra.Count == 2 && ra[0] == "--repetitions" && ra[1] == "40" && r1 && r501);
            int full, last;
            int p40 = BambuSlicer.BatchPlates(40, 28, out full, out last);
            True("BatchPlates 40 / 28 = 2 plates (1 full + 12)", p40 == 2 && full == 1 && last == 12);
            int p56 = BambuSlicer.BatchPlates(56, 28, out full, out last);
            True("BatchPlates 56 / 28 = 2 plates (2 full + 0); 0 copies = 0", p56 == 2 && full == 2 && last == 0 && BambuSlicer.BatchPlates(0, 28) == 0);
            bool perZero = false;
            try { BambuSlicer.BatchPlates(5, 0); } catch (ArgumentOutOfRangeException) { perZero = true; }
            True("BatchPlates per plate 0 -> ArgumentOutOfRangeException", perZero);
            True("CopiesPerPlate objects: 294 / 2 = 147, 5 / 0 = 5, 0 = 0, 1 / 2 = 1", BambuSlicer.CopiesPerPlate(294, 2) == 147
                 && BambuSlicer.CopiesPerPlate(5, 0) == 5 && BambuSlicer.CopiesPerPlate(0, 3) == 0 && BambuSlicer.CopiesPerPlate(1, 2) == 1);
            // footprint grid, gap 2 on 256: 60 x 31 -> floor(258/62) = 4 x floor(258/33) = 7 = 28 (turned: 7 x 4 = 28)
            True("CopiesPerPlate footprint: Benchy 60 x 31 on 256 = 28", BambuSlicer.CopiesPerPlate(60, 31, 256, 256, 2, 0, 0) == 28);
            // tower 30 x 30 takes ceil(32/62) = 1 x ceil(32/33) = 1 cell: 27
            True("CopiesPerPlate footprint: with a 30 x 30 tower = 27", BambuSlicer.CopiesPerPlate(60, 31, 256, 256, 2, 30, 30) == 27);
            True("CopiesPerPlate footprint: 300 x 10 doesn't fit = 0; 256 x 256 = 1; bad size = 0", BambuSlicer.CopiesPerPlate(300, 10, 256, 256, 2, 0, 0) == 0
                 && BambuSlicer.CopiesPerPlate(256, 256, 256, 256, 2, 0, 0) == 1 && BambuSlicer.CopiesPerPlate(double.NaN, 31, 256, 256, 2, 0, 0) == 0);
            // A1 mini 180: floor(182/62) = 2 x floor(182/33) = 5 = 10; unknown printer = A1 = 28
            True("CopiesPerPlate by printer: A1 mini 10, A1 28, unknown = A1", BambuSlicer.CopiesPerPlate("A1M", 60, 31, 0, 0) == 10
                 && BambuSlicer.CopiesPerPlate("A1", 60, 31, 0, 0) == 28 && BambuSlicer.CopiesPerPlate("??", 60, 31, 0, 0) == 28);
            True("CopyBaseName: 3dbenchy.stl_12 -> 3dbenchy.stl; a_b kept; null = \"\"", BambuSlicer.CopyBaseName("3dbenchy.stl_12") == "3dbenchy.stl"
                 && BambuSlicer.CopyBaseName("a_b") == "a_b" && BambuSlicer.CopyBaseName(null) == "");
            // 2 objects 10..140 x 20..51 = 130 x 31, tower 200..230 x 200..235 = 30 x 35
            var fp = BambuSlicer.ParsePlateFootprint("{\"bbox_objects\":[{\"bbox\":[10,20,70,51],\"name\":\"3dbenchy.stl_1\"},"
                + "{\"bbox\":[80,20,140,51],\"name\":\"3dbenchy.stl_2\"},{\"bbox\":[200,200,230,235],\"name\":\"wipe_tower\"}]}");
            True("ParsePlateFootprint: 130 x 31, 2 objects, tower 30 x 35", fp != null && fp.Width == 130 && fp.Depth == 31 && fp.Objects == 2
                 && fp.TowerWidth == 30 && fp.TowerDepth == 35);
            True("ParsePlateFootprint: bad / empty / only a tower -> null", BambuSlicer.ParsePlateFootprint("x") == null && BambuSlicer.ParsePlateFootprint(null) == null
                 && BambuSlicer.ParsePlateFootprint("{\"bbox_objects\":[{\"bbox\":[1,1,2,2],\"name\":\"wipe_tower\"}]}") == null);
            var po = BambuSlicer.ProjectPlateObjects("<config><plate><model_instance/><model_instance/></plate><plate><model_instance/></plate></config>");
            True("ProjectPlateObjects: 2 plates with 2 and 1 objects; bad xml = none", po.Count == 2 && po[0] == 2 && po[1] == 1
                 && BambuSlicer.ProjectPlateObjects("<config").Count == 0 && BambuSlicer.ProjectPlateObjects(null).Count == 0);
            var nb = new SliceResult();
            foreach (var f in new[] { "a", "a", "b", "a" }) nb.PlateList.Add(new PlateInfo { File = f });
            BambuSlicer.NumberBatchPlates(nb);
            True("NumberBatchPlates: a 1, a 2, b 1, a 3; FilePlates 3, 3, 1, 3", nb.PlateList[0].Index == 1 && nb.PlateList[1].Index == 2 && nb.PlateList[2].Index == 1
                 && nb.PlateList[3].Index == 3 && nb.PlateList[0].FilePlates == 3 && nb.PlateList[2].FilePlates == 1 && nb.PlateList[3].FilePlates == 3);
            BambuSlicer.NumberBatchPlates(null);

            // more cases from plans\batch-plates\online-names.md
            bool wasDanish = L.Danish;
            try
            {
                L.Danish = false; string en = BambuSlicer.CopiesRangeText();
                L.Danish = true; string da = BambuSlicer.CopiesRangeText();
                True("CopiesRangeText EN / DA", en == "Copies must be 1 to 500." && da == "Antal skal være 1 til 500.");
            }
            finally { L.Danish = wasDanish; }
            True("CopiesInRange -3 false", !BambuSlicer.CopiesInRange(-3));
            bool c501 = false, cEmpty = false, r0 = false;
            try { BambuSlicer.CloneArgs(new List<int> { 501 }); } catch (SliceException) { c501 = true; }
            try { BambuSlicer.CloneArgs(new List<int>()); } catch (ArgumentException) { cEmpty = true; }
            try { BambuSlicer.RepetitionArgs(0); } catch (SliceException) { r0 = true; }
            True("CloneArgs {1} -> 1; {501} refused; empty -> ArgumentException; RepetitionArgs 0 refused",
                 BambuSlicer.CloneArgs(new List<int> { 1 })[1] == "1" && c501 && cEmpty && r0);
            int p100 = BambuSlicer.BatchPlates(100, 28, out full, out last);
            int int5Full, int5Last;
            int p5 = BambuSlicer.BatchPlates(5, 28, out int5Full, out int5Last);
            True("BatchPlates 100 / 28 = 4 (3 full + 16); 5 / 28 = 1 (0 + 5)", p100 == 4 && full == 3 && last == 16 && p5 == 1 && int5Full == 0 && int5Last == 5);
            True("CopiesPerPlate objects: 28 / 1 = 28, 28 / 0 = 28", BambuSlicer.CopiesPerPlate(28, 1) == 28 && BambuSlicer.CopiesPerPlate(28, 0) == 28);
            // clip 41,22 x 25,24: floor(258/43,22) = 5 x floor(258/27,24) = 9 = 45; 250 x 250 = 1;
            // 10 x 10 with a 35 x 35 tower: 21 x 21 = 441 - ceil(37/12) = 4 x 4 = 16 -> 425; negative size = 0
            True("CopiesPerPlate footprint: clip 45, 250 x 250 = 1, 10 x 10 + tower 35 = 425, negative = 0",
                 BambuSlicer.CopiesPerPlate(41.22, 25.24, 256, 256, 2, 0, 0) == 45 && BambuSlicer.CopiesPerPlate(250, 250, 256, 256, 2, 0, 0) == 1
                 && BambuSlicer.CopiesPerPlate(10, 10, 256, 256, 2, 35, 35) == 425 && BambuSlicer.CopiesPerPlate(-5, 10, 256, 256, 2, 0, 0) == 0
                 && BambuSlicer.CopiesPerPlate(0, 10, 256, 256, 2, 0, 0) == 0);
            True("CopyBaseName: c4.stl / part_a kept", BambuSlicer.CopyBaseName("c4.stl") == "c4.stl" && BambuSlicer.CopyBaseName("part_a") == "part_a");
            True("ParsePlateFootprint: \"\" / {} / no bbox_objects -> null", BambuSlicer.ParsePlateFootprint("") == null && BambuSlicer.ParsePlateFootprint("{}") == null
                 && BambuSlicer.ParsePlateFootprint("{\"other\":1}") == null && BambuSlicer.ParsePlateFootprint("not json") == null);
            True("ProjectPlateObjects \"\" = none", BambuSlicer.ProjectPlateObjects("").Count == 0);

            // RepeatResult: one plate 2270 s / 11,76 g / tower 1,7 x 3 = 6810 s, 35,28 g, 3 plates, tower 5,1
            var one = new SliceResult { Seconds = 2270, Grams = 11.76, Plates = 1, TowerGrams = 1.7 };
            one.PlateList.Add(new PlateInfo { Index = 1, Seconds = 2270, Grams = 11.76 });
            var rep = BambuSlicer.RepeatResult(one, 3);
            True("RepeatResult x 3: 6810 s, 3 plates (1, 2, 3, 1 copy each), Copies 3, PerPlate 1, multiplied", rep.Seconds == 6810 && rep.Plates == 3
                 && rep.PlateList.Count == 3 && rep.PlateList[0].Index == 1 && rep.PlateList[2].Index == 3 && rep.PlateList[1].Copies == 1
                 && rep.Copies == 3 && rep.PerPlate == 1 && rep.CopiesMultiplied);
            Eq("RepeatResult x 3: 11,76 x 3 = 35,28 g", 35.28, rep.Grams);
            Eq("RepeatResult x 3: tower 1,7 x 3 = 5,1 g", 5.1, rep.TowerGrams);
            True("RepeatResult: one unchanged; x 1 not multiplied; null -> null", one.PlateList.Count == 1 && one.Seconds == 2270
                 && !BambuSlicer.RepeatResult(one, 1).CopiesMultiplied && BambuSlicer.RepeatResult(null, 3) == null);
            var two = new SliceResult { Seconds = 100, Grams = 2, Plates = 2 };
            two.PlateList.Add(new PlateInfo { Index = 1 }); two.PlateList.Add(new PlateInfo { Index = 2 });
            var rep2 = BambuSlicer.RepeatResult(two, 2);
            True("RepeatResult: two-plate file x 2 = 4 plates, plate Copies 0, PerPlate 0", rep2.Plates == 4 && rep2.PlateList.Count == 4
                 && rep2.PlateList[0].Copies == 0 && rep2.PerPlate == 0);
            bool rr0 = false, rr501 = false;
            try { BambuSlicer.RepeatResult(one, 0); } catch (SliceException) { rr0 = true; }
            try { BambuSlicer.RepeatResult(one, 501); } catch (SliceException) { rr501 = true; }
            True("RepeatResult: 0 / 501 times refused", rr0 && rr501);
            // AddResult: 28 + 12 copies = 40, PerPlate max 28
            var tot = new SliceResult();
            BambuSlicer.AddResult(tot, new SliceResult { Copies = 28, PerPlate = 28, Plates = 1 }, "a");
            BambuSlicer.AddResult(tot, new SliceResult { Copies = 12, PerPlate = 12, Plates = 1 }, "a");
            True("AddResult: Copies 28 + 12 = 40, PerPlate 28", tot.Copies == 40 && tot.PerPlate == 28);
            // ApplyPlateTowers: plate 2 gets 1,25 + 0,5 = 1,75; plate 1 (not in the map) stays unknown
            var at = new SliceResult();
            at.PlateList.Add(new PlateInfo { Index = 1 }); at.PlateList.Add(new PlateInfo { Index = 2 });
            double before1 = at.PlateList[0].TowerGrams;
            var tw = new GcodeTower(); tw.Grams.Add(1.25); tw.Grams.Add(0.5);
            BambuSlicer.ApplyPlateTowers(at, new Dictionary<int, GcodeTower> { { 2, tw } });
            True("ApplyPlateTowers: plate 2 = 1,75 g, plate 1 unchanged (-1)", Math.Abs(at.PlateList[1].TowerGrams - 1.75) < 1e-9
                 && at.PlateList[0].TowerGrams == before1 && before1 == -1);
            BambuSlicer.ApplyPlateTowers(null, null);
        }

        static void TowerHistoryTests()
        {
            // AppSettings.PrimeTower: default 1, 0 survives, others -> 1; QuoteShowTower: default 0, 1 survives, others -> 0
            Eq("PrimeTower default 1 (new / new install)", 2, new AppSettings().PrimeTower + AppSettings.FromIni(null).PrimeTower);
            var pt = new AppSettings(); pt.PrimeTower = 0; pt.QuoteShowTower = 1;
            True("PrimeTower 0 / QuoteShowTower 1 in ini + survive FromIni", Array.IndexOf(pt.ToIni(), "PrimeTower=0") >= 0 && Array.IndexOf(pt.ToIni(), "QuoteShowTower=1") >= 0
                 && AppSettings.FromIni(pt.ToIni()).PrimeTower == 0 && AppSettings.FromIni(pt.ToIni()).QuoteShowTower == 1);
            True("PrimeTower=7 / missing -> 1; QuoteShowTower=7 / missing -> 0", AppSettings.FromIni(new[] { "PrimeTower=7", "QuoteShowTower=7" }).PrimeTower == 1
                 && AppSettings.FromIni(new[] { "PrimeTower=7", "QuoteShowTower=7" }).QuoteShowTower == 0
                 && AppSettings.FromIni(new[] { "SetupDone=1" }).PrimeTower == 1 && AppSettings.FromIni(new[] { "SetupDone=1" }).QuoteShowTower == 0);
            True("PrimeTower: Clone keeps 0, Reset to defaults -> 1 / 0", pt.Clone().PrimeTower == 0 && pt.DefaultsKeepingPersonal().PrimeTower == 1
                 && pt.DefaultsKeepingPersonal().QuoteShowTower == 0);

            // old records: everything unknown (-1), no line
            var old = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"Filaments\":[{\"Grams\":5,\"PricePerKg\":200}]}]}");
            True("Tower: old record -> PrimeTower / TowerGrams / TowerSeconds / line grams -1, no line", old[0].PrimeTower == -1 && old[0].TowerGrams == -1
                 && old[0].TowerSeconds == -1 && old[0].Filaments[0].TowerGrams == -1 && !History.ShowsTower(old[0]));
            // SetTower + per-colour grams, JSON round trip and CopyQuotes
            var t = new DateTime(2026, 11, 20, 12, 0, 0);
            var r = HistRecord(t);
            History.SetTower(r, true, 1.70, 314);
            r.Filaments[0].TowerGrams = 1.10;
            var back = History.ParseJson(History.ToJson(new List<QuoteRecord> { r }))[0];
            True("Tower: on, 1,70 g, 314 s, line 1,10 g round trip", back.PrimeTower == 1 && back.TowerGrams == 1.70 && back.TowerSeconds == 314
                 && back.Filaments[0].TowerGrams == 1.10 && History.ShowsTower(back));
            var cp = History.CopyQuotes(new List<QuoteRecord> { r })[0];
            True("Tower: CopyQuotes keeps it (own filament copy)", cp.TowerGrams == 1.70 && cp.Filaments[0].TowerGrams == 1.10 && !ReferenceEquals(cp.Filaments[0], r.Filaments[0]));
            // bad values -> unknown; 0 = no tower (kept, no line); SetTower cleans
            var bad = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"PrimeTower\":5,\"TowerGrams\":-3,\"TowerSeconds\":-0.5,"
                                        + "\"Filaments\":[{\"Grams\":5,\"TowerGrams\":-2}]},{\"Id\":\"b\",\"PrimeTower\":0,\"TowerGrams\":0,\"TowerSeconds\":0}]}");
            True("Tower: PrimeTower 5 / grams -3 / seconds -0,5 / line -2 -> -1", bad[0].PrimeTower == -1 && bad[0].TowerGrams == -1 && bad[0].TowerSeconds == -1
                 && bad[0].Filaments[0].TowerGrams == -1);
            True("Tower: off with 0 g kept, no line", bad[1].PrimeTower == 0 && bad[1].TowerGrams == 0 && !History.ShowsTower(bad[1]));
            var sc = HistRecord(t); History.SetTower(sc, false, double.NaN, -5);
            True("SetTower: off, NaN / -5 -> -1", sc.PrimeTower == 0 && sc.TowerGrams == -1 && sc.TowerSeconds == -1);
            // the switch for reopen / repeat: own when known, else Settings'
            var off = new AppSettings(); off.PrimeTower = 0;
            True("PrimeTowerFor: own off beats Settings on; own on beats Settings off; unknown = Settings; null = on",
                 !History.PrimeTowerFor(sc, new AppSettings()) && History.PrimeTowerFor(r, off) && !History.PrimeTowerFor(old[0], off)
                 && History.PrimeTowerFor(old[0], new AppSettings()) && History.PrimeTowerFor(old[0], null) && History.PrimeTowerFor(null, null));
            // tower on / off is another quote (same numbers, 2 min apart)
            var a1 = HistRecord(t); var a2 = HistRecord(t.AddMinutes(2));
            History.SetTower(a1, true, 1.7, 300); History.SetTower(a2, true, 1.7, 300);
            bool same = History.SameQuote(a1, a2);
            History.SetTower(a2, false, 0, 0);
            True("Tower: same switch = same quote, on vs off = not", same && !History.SameQuote(a1, a2));
            // merge: unknown here -> filled from the backup (with the colour line); known here stays
            var theirs = BRec("T", "2026-11-20 12:00:00"); History.SetTower(theirs, true, 1.7, 300); theirs.Filaments[0].TowerGrams = 1.7;
            var mine = BRec("T", "2026-11-20 12:00:00");
            var res = History.Combine(new List<QuoteRecord> { mine }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("Tower merge: unknown here -> on, 1,70 g, 300 s, line 1,70", res.Updated == 1 && res.Quotes[0].PrimeTower == 1 && res.Quotes[0].TowerGrams == 1.7
                 && res.Quotes[0].TowerSeconds == 300 && res.Quotes[0].Filaments[0].TowerGrams == 1.7);
            var mine2 = BRec("T", "2026-11-20 12:00:00"); History.SetTower(mine2, false, 0, 0);
            res = History.Combine(new List<QuoteRecord> { mine2 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("Tower merge: off / 0 g here stays", res.Quotes[0].PrimeTower == 0 && res.Quotes[0].TowerGrams == 0);
            // combined bill: 1,70 + unknown + 0,60 = 2,30; all unknown = -1
            Eq("TowerGramsSum: 1,70 + unknown + 0,60 = 2,30", 2.30, History.TowerGramsSum(new[] { a1, old[0], new QuoteRecord { TowerGrams = 0.6 } }));
            True("TowerGramsSum: all unknown / null -> -1", History.TowerGramsSum(new[] { old[0] }) == -1 && History.TowerGramsSum(null) == -1);
        }

        // ---------- issue #8 step 3: online's tower helpers (SliceInfo.cs / Slicer.cs); samples in tests\gcode-samples\tower ----------
        // (trimmed copies of plans\issue8\step3\runs\*\plate_1.gcode and the website's test-cubes-3colour.gcode: header filament lines,
        // T / M82 / M83 / G92, feature markers, and each tower block's printing moves summed into one G1 per tool run)

        static GcodeTower TowerOf(string text)
        {
            return BambuSlicer.TowerFromGcode(text.Replace("\r", "").Split('\n'));
        }

        static GcodeTower TowerFile(string name)
        {
            return BambuSlicer.TowerFromGcode(System.IO.File.ReadLines(System.IO.Path.Combine(GcodeDir(), "tower", name)));
        }

        static bool G(GcodeTower t, int slot, double grams) { return t.Grams.Count >= slot && Math.Abs(t.Grams[slot - 1] - grams) <= 0.01; }

        // a real A1 slice with a thumbnail block (tests\gcode-samples\thumbnail\red_block_thumbnail.gcode, made by online)
        static void GcodeThumbnailTests()
        {
            string path = System.IO.Path.Combine(GcodeDir(), "thumbnail", "red_block_thumbnail.gcode");
            var pic = BambuSlicer.ReadGcodePicture(path);
            True("ReadGcodePicture: PNG (89 50 4E 47 0D 0A 1A 0A)", pic != null && pic.Length > 8 && pic[0] == 0x89 && pic[1] == 0x50 && pic[2] == 0x4E
                 && pic[3] == 0x47 && pic[4] == 0x0D && pic[5] == 0x0A && pic[6] == 0x1A && pic[7] == 0x0A);
            var res = BambuSlicer.ReadGcodeFile(path);
            True("ReadGcodeFile: picture, FromSlicedFile, about 2,21 g", res != null && res.Picture != null && res.FromSlicedFile && Math.Abs(res.Grams - 2.21) < 0.01);
        }

        static void TowerSlicerTests()
        {
            // 1-2. real slices: tower on vs off (plan: counted 1,098 / 0,606 g = on - off 1,10 / 0,60)
            var l = TowerFile("L_on_names.gcode");
            True("TowerFromGcode: L (on) 1,098 / 0,606 g, blocks > 0", G(l, 1, 1.098) && G(l, 2, 0.606) && l.Blocks > 0);
            var m = TowerFile("M_off_names.gcode");
            True("TowerFromGcode: M (off) 0 / 0, no blocks", m.Grams.Count == 2 && m.Grams[0] == 0 && m.Grams[1] == 0 && m.Blocks == 0);
            var g3 = TowerFile("G3_on.gcode");
            True("TowerFromGcode: G3 (together, on) 1,655 / 1,029 g", G(g3, 1, 1.655) && G(g3, 2, 1.029));
            var cubes = TowerFile("cubes_3colour.gcode");
            True("TowerFromGcode: website 3-colour sample 3,888 / 1,799 / 1,582 g", G(cubes, 1, 3.888) && G(cubes, 2, 1.799) && G(cubes, 3, 1.582));

            // 3. only printing moves count: 200 + 300 = 500 mm (flush E3000 and E0,8 without X/Y, E-0,8 / E-1 left out);
            //    500 × π × 0,875² × 1,24 / 1000 = 1,4913 g (defaults 1,75 mm / 1,24 g/cm³)
            const string rel = "M83\nT0\n; FEATURE: Outer wall\nG1 X10 Y10 E5\n; FEATURE: Prime tower\nG1 X20 Y20 E200\nG1 E-0.8\nG1 E0.8\n"
                + "; FilamentChangeGCodeStart\nG1 E3000 F300\n; FilamentChangeGCodeEnd\nG1 X21 Y20 E300\nG1 X22 Y20 E-1\n; FEATURE: Inner wall\nG1 X30 Y30 E7\n";
            var tr = TowerOf(rel);
            True("TowerFromGcode: flush / retract left out -> 1,491 g, 1 block", tr.Grams.Count == 1 && tr.Grams[0] == 1.491 && tr.Blocks == 1);
            // 4. the same moves in absolute E with G92 resets inside and outside the block
            const string abs = "M82\nT0\nG92 E0\n; FEATURE: Outer wall\nG1 X10 Y10 E5\n; FEATURE: Prime tower\nG1 X20 Y20 E205\nG1 E204.2\nG1 E205\n"
                + "G92 E0\nG1 E3000 F300\nG92 E0\nG1 X21 Y20 E300\nG1 X22 Y20 E299\n; FEATURE: Inner wall\nG1 X30 Y30 E306\n";
            var ta = TowerOf(abs);
            True("TowerFromGcode: M82 + G92 E0 = the M83 version (1,491 g)", ta.Grams.Count == 1 && ta.Grams[0] == tr.Grams[0]);
            // 5. T255 / T1000 skipped, T1 = slot 2; "density: 1,2" and "diameter = 2,1.75"; diameter_map not taken
            //    slot 1: 100 × π × 1² × 1,0 / 1000 = 0,314; slot 2: 100 × π × 0,875² × 2,0 / 1000 = 0,481
            const string tools = "; filament_diameter_map = 9,9\n; filament_density: 1.0,2.0\n; filament_diameter = 2.0,1.75\nM83\nT255\nT1000\n"
                + "; FEATURE: Prime tower\nG1 X1 Y1 E100\nT1\nG1 X1 Y1 E100\n";
            var tt = TowerOf(tools);
            True("TowerFromGcode: T255/T1000 ignored, T1 -> slot 2; header both forms -> 0,314 / 0,481", tt.Grams.Count == 2 && tt.Grams[0] == 0.314 && tt.Grams[1] == 0.481);
            // no header: 100 × π × 0,875² × 1,24 / 1000 = 0,298
            var td = TowerOf("M83\n; FEATURE: Prime tower\nG1 X1 Y1 E100\n");
            True("TowerFromGcode: no header -> 1,75 mm / 1,24 -> 0,298 g", td.Grams.Count == 1 && td.Grams[0] == 0.298);
            // TowerCounter fed in two pieces with a Result() in between = TowerFromGcode on the whole file
            var cubeLines = new List<string>(System.IO.File.ReadLines(System.IO.Path.Combine(GcodeDir(), "tower", "cubes_3colour.gcode")));
            var counter = new BambuSlicer.TowerCounter();
            int half = cubeLines.Count / 2;
            for (int i = 0; i < half; i++) counter.Feed(cubeLines[i]);
            var mid = counter.Result();
            for (int i = half; i < cubeLines.Count; i++) counter.Feed(cubeLines[i]);
            var whole = counter.Result();
            bool sameGrams = whole.Grams.Count == cubes.Grams.Count;
            for (int i = 0; sameGrams && i < cubes.Grams.Count; i++) if (whole.Grams[i] != cubes.Grams[i]) sameGrams = false;
            True("TowerCounter: two pieces + Result() in between = TowerFromGcode (grams, blocks)", mid != null && sameGrams && whole.Blocks == cubes.Blocks
                 && whole.Blocks > mid.Blocks);
            True("IsMoveLine: G1 X1 E.1 / g0 / G01<tab>X1 / G3;c yes", BambuSlicer.IsMoveLine("G1 X1 E.1") && BambuSlicer.IsMoveLine("g0")
                 && BambuSlicer.IsMoveLine("G01\tX1") && BambuSlicer.IsMoveLine("G3;c"));
            True("IsMoveLine: G10 / G28 / G92 E0 / G010 / G1X10 / M83 / G / null no", !BambuSlicer.IsMoveLine("G10") && !BambuSlicer.IsMoveLine("G28")
                 && !BambuSlicer.IsMoveLine("G92 E0") && !BambuSlicer.IsMoveLine("G010") && !BambuSlicer.IsMoveLine("G1X10") && !BambuSlicer.IsMoveLine("M83")
                 && !BambuSlicer.IsMoveLine("G") && !BambuSlicer.IsMoveLine(null));
            True("TowerFromGcode: null -> empty, no blocks", BambuSlicer.TowerFromGcode(null).Grams.Count == 0 && BambuSlicer.TowerFromGcode(null).Blocks == 0);

            // 6. ApplyTower
            var none = new GcodeTower(); none.Grams.Add(0);
            var bambu = new SliceResult();
            bambu.Filaments.Add(new FilamentUse { Slot = 1 }); bambu.Filaments.Add(new FilamentUse { Slot = 2 });
            BambuSlicer.ApplyTower(bambu, none);
            True("ApplyTower: Bambu, no blocks -> 0, slots 0 (also a slot without grams)", bambu.TowerGrams == 0 && bambu.Filaments[0].TowerGrams == 0 && bambu.Filaments[1].TowerGrams == 0);
            var cura = new SliceResult { SlicerName = BambuSlicer.SlicerCura };
            cura.Filaments.Add(new FilamentUse { Slot = 1 });
            BambuSlicer.ApplyTower(cura, new GcodeTower());
            True("ApplyTower: Cura, no blocks -> unknown -1", cura.TowerGrams == -1 && cura.Filaments[0].TowerGrams == -1);
            var prusa = new SliceResult { SlicerName = BambuSlicer.SlicerPrusa, PurgeGrams = 1.84 };
            prusa.Filaments.Add(new FilamentUse { Slot = 1 });
            BambuSlicer.ApplyTower(prusa, new GcodeTower());
            True("ApplyTower: Prusa, no blocks, wipe tower total 1,84 -> 1,84, slots unknown", prusa.TowerGrams == 1.84 && prusa.Filaments[0].TowerGrams == -1);
            var withL = new SliceResult();
            withL.Filaments.Add(new FilamentUse { Slot = 1 }); withL.Filaments.Add(new FilamentUse { Slot = 2 });
            BambuSlicer.ApplyTower(withL, l);
            True("ApplyTower: L -> 1,10 + 0,61 = 1,70 g total, per slot", Math.Abs(withL.TowerGrams - 1.704) < 0.01 && Math.Abs(withL.Filaments[0].TowerGrams - 1.098) < 0.01
                 && Math.Abs(withL.Filaments[1].TowerGrams - 0.606) < 0.01);

            // 7. slot names
            True("StripSlotTag / SlotNameTag", BambuSlicer.StripSlotTag("Bambu PLA Basic @BBL A1 #2") == "Bambu PLA Basic @BBL A1"
                 && BambuSlicer.StripSlotTag("PLA #2") == "PLA #2" && BambuSlicer.StripSlotTag(null) == "" && BambuSlicer.SlotNameTag(3) == " #3");
            True("TaggedName: \"… @BBL A1 #2\", \"Acme PLA @#2\", stripped back", BambuSlicer.TaggedName("Bambu PLA Basic @BBL A1", 2) == "Bambu PLA Basic @BBL A1 #2"
                 && BambuSlicer.TaggedName("Acme PLA", 2) == "Acme PLA @#2" && BambuSlicer.StripSlotTag("Acme PLA @#2") == "Acme PLA"
                 && BambuSlicer.StripSlotTag(BambuSlicer.TaggedName("Bambu PLA Basic @BBL A1", 2)) == "Bambu PLA Basic @BBL A1");
            // OwnSlotProfile: slot index 1 takes the project's per-slot density 1,32; name / from / type are not the project's
            var template = new Dictionary<string, object> { { "name", "Bambu PLA Basic @BBL A1" }, { "from", "system" },
                                                           { "filament_density", new[] { "1.24" } }, { "filament_type", new[] { "PLA" } } };
            var project = new Dictionary<string, object> { { "filament_density", new System.Collections.ArrayList { "1.26", "1.32" } }, { "from", "x" } };
            var own = BambuSlicer.OwnSlotProfile(template, project, new List<string> { "Bambu PLA Basic @BBL A1", "Bambu PLA Matte @BBL A1" }, 1);
            var dens = own["filament_density"] as string[];
            var typ = own["filament_type"] as string[];
            True("OwnSlotProfile: density [1.32], from = template's, name tagged #2, type unchanged, template untouched",
                 dens != null && dens.Length == 1 && dens[0] == "1.32" && (string)own["from"] == "system" && (string)own["name"] == "Bambu PLA Matte @BBL A1 #2"
                 && typ != null && typ.Length == 1 && typ[0] == "PLA" && ((string[])template["filament_density"])[0] == "1.24" && (string)template["name"] == "Bambu PLA Basic @BBL A1");

            // 8. AddResult: 1,70 (1,10 + 0,60, 271 s) + 0,50 at offset 2 + unknown + 0,40 on slot 1 -> 2,60 g; slots 1,50 / 0,60 / 0,50
            var total = new SliceResult();
            var r1 = new SliceResult { TowerGrams = 1.7, TowerSeconds = 271 };
            r1.Filaments.Add(new FilamentUse { Slot = 1, TowerGrams = 1.1 }); r1.Filaments.Add(new FilamentUse { Slot = 2, TowerGrams = 0.6 });
            var r2 = new SliceResult { TowerGrams = 0.5 };
            r2.Filaments.Add(new FilamentUse { Slot = 1, TowerGrams = 0.5 });
            var r3 = new SliceResult();
            r3.Filaments.Add(new FilamentUse { Slot = 2 });   // unknown: never lowers slot 2
            var r4 = new SliceResult { TowerGrams = 0.4 };
            r4.Filaments.Add(new FilamentUse { Slot = 1, TowerGrams = 0.4 });
            BambuSlicer.AddResult(total, r1, "a"); BambuSlicer.AddResult(total, r2, "b", 2); BambuSlicer.AddResult(total, r3, "c"); BambuSlicer.AddResult(total, r4, "d");
            Func<int, double> slotT = k => { foreach (var f in total.Filaments) if (f != null && f.Slot == k) return f.TowerGrams; return -9; };
            True("AddResult tower: total 2,60 g, 271 s; slots 1,50 / 0,60 / 0,50", Math.Abs(total.TowerGrams - 2.6) < 1e-9 && total.TowerSeconds == 271
                 && Math.Abs(slotT(1) - 1.5) < 1e-9 && Math.Abs(slotT(2) - 0.6) < 1e-9 && Math.Abs(slotT(3) - 0.5) < 1e-9);
            var tot = new GcodeTower(); tot.Grams.Add(1.0); tot.Blocks = 2;
            var pl = new GcodeTower(); pl.Grams.Add(0.5); pl.Grams.Add(0.3); pl.Blocks = 3;
            BambuSlicer.AddTower(tot, pl);
            True("AddTower: [1,0] + [0,5, 0,3] = [1,5, 0,3], 5 blocks, total 1,8", tot.Grams.Count == 2 && tot.Grams[0] == 1.5 && tot.Grams[1] == 0.3 && tot.Blocks == 5
                 && Math.Abs(tot.Total - 1.8) < 1e-9);

            // 9. tower seconds from result.json feature_type_times "Prime tower" (L: 270,94 s; M: none)
            var sl = BambuSlicer.ParseSliceResult(System.IO.File.ReadAllText(System.IO.Path.Combine(GcodeDir(), "tower", "L_on_names_result.json")));
            var sm = BambuSlicer.ParseSliceResult(System.IO.File.ReadAllText(System.IO.Path.Combine(GcodeDir(), "tower", "M_off_names_result.json")));
            True("ParseSliceResult: tower seconds L 271, M 0", sl != null && Math.Abs(sl.TowerSeconds - 271) < 1 && sm != null && sm.TowerSeconds == 0);
        }

        // ---------- issue #8 step 2, online's X/Y/Z helpers (ScaleXyz in SliceInfo.cs, file scaling in Slicer.cs) ----------

        // a stream that says it is longer than it is (a binary STL cut off after its size was checked)
        class LongerStream : System.IO.MemoryStream
        {
            readonly long fake;
            public LongerStream(byte[] b, long fakeLength) : base(b) { fake = fakeLength; }
            public override long Length { get { return fake; } }
        }

        static byte[] StlTriangle(float[] n, float[] v, byte a0, byte a1)
        {
            var b = new byte[50];
            for (int i = 0; i < 3; i++) Buffer.BlockCopy(BitConverter.GetBytes(n[i]), 0, b, i * 4, 4);
            for (int i = 0; i < 9; i++) Buffer.BlockCopy(BitConverter.GetBytes(v[i]), 0, b, 12 + i * 4, 4);
            b[48] = a0; b[49] = a1;
            return b;
        }

        static float F32(byte[] b, int at) { return BitConverter.ToSingle(b, at); }

        static void ScaleFileTests()
        {
            ScaleXyz s;
            // ---- ScaleXyz.TryParse
            True("ScaleXyz.TryParse: \"150\" / \"150 %\" -> uniform 150", ScaleXyz.TryParse("150", out s) && s.IsUniform && s.X == 150
                 && ScaleXyz.TryParse("150 %", out s) && s.IsUniform && s.X == 150);
            True("ScaleXyz.TryParse: \"87,5\" -> uniform 87,5", ScaleXyz.TryParse("87,5", out s) && s.IsUniform && s.X == 87.5);
            bool allXyz = true;
            foreach (var txt in new[] { "150,100,50", "150 × 100 × 50", "150x100x50", "150;100;50", "150 100 50" })
                if (!ScaleXyz.TryParse(txt, out s) || s.X != 150 || s.Y != 100 || s.Z != 50) allXyz = false;
            True("ScaleXyz.TryParse: 150,100,50 / × / x / ; / spaces -> 150 100 50", allXyz);
            True("ScaleXyz.TryParse: 9 / 501 / abc / \"\" / 150,100,600 rejected", !ScaleXyz.TryParse("9", out s) && !ScaleXyz.TryParse("501", out s)
                 && !ScaleXyz.TryParse("abc", out s) && !ScaleXyz.TryParse("", out s) && !ScaleXyz.TryParse("150,100,600", out s));
            True("ScaleXyz.TryParse: \"150,100\" refused (two numbers, not 150,1 %)", !ScaleXyz.TryParse("150,100", out s));
            True("ScaleXyz.TryParse: \"87,25\" -> uniform 87,3 (1 decimal)", ScaleXyz.TryParse("87,25", out s) && s.IsUniform && Math.Abs(s.X - 87.3) < 1e-9);
            // ---- ScaleXyz
            var z = new ScaleXyz(0, -5, double.NaN);
            True("ScaleXyz: 0 / -5 / NaN -> 100/100/100, not scaled", z.X == 100 && z.Y == 100 && z.Z == 100 && !z.IsScaled && z.IsUniform);
            True("ScaleXyz: Uniform(150) uniform, text \"150 %\"", ScaleXyz.Uniform(150).IsUniform && ScaleXyz.Uniform(150).Text == "150 %");
            True("ScaleXyz: texts \"150 × 100 × 50 %\", \"87,5 × 100 × 100 %\"", new ScaleXyz(150, 100, 50).Text == "150 × 100 × 50 %"
                 && new ScaleXyz(87.5, 100, 100).Text == "87,5 × 100 × 100 %" && !new ScaleXyz(150, 100, 50).IsUniform);
            True("ScaleXyz: InRange false for 600, true for 150/100/50", !new ScaleXyz(600, 100, 100).InRange && new ScaleXyz(150, 100, 50).InRange);
            // ---- ScaleArgs(ScaleXyz)
            True("ScaleArgs xyz: None -> none; Uniform(150) -> --scale 1.5 --ensure-on-bed; 150/100/50 -> --ensure-on-bed",
                 BambuSlicer.ScaleArgs(ScaleXyz.None).Count == 0 && string.Join(" ", BambuSlicer.ScaleArgs(ScaleXyz.Uniform(150))) == "--scale 1.5 --ensure-on-bed"
                 && string.Join(" ", BambuSlicer.ScaleArgs(new ScaleXyz(150, 100, 50))) == "--ensure-on-bed");
            bool threw = false;
            try { BambuSlicer.ScaleArgs(new ScaleXyz(600, 100, 100)); } catch (SliceException) { threw = true; }
            True("ScaleArgs xyz: 600/100/100 throws SliceException", threw);
            // ---- CanScaleXyz
            True("CanScaleXyz: .stl .OBJ .3mf .gcode.3mf yes", BambuSlicer.CanScaleXyz(@"C:\x\a.stl") && BambuSlicer.CanScaleXyz(@"C:\x\a.OBJ")
                 && BambuSlicer.CanScaleXyz(@"C:\x\a.3mf") && BambuSlicer.CanScaleXyz(@"C:\x\x.gcode.3mf"));
            True("CanScaleXyz: .step .stp .amf .gcode no", !BambuSlicer.CanScaleXyz(@"C:\x\a.step") && !BambuSlicer.CanScaleXyz(@"C:\x\a.stp")
                 && !BambuSlicer.CanScaleXyz(@"C:\x\a.amf") && !BambuSlicer.CanScaleXyz(@"C:\x\a.gcode"));
            // ---- PrepareScaled: uniform / none = same path, nothing written; X/Y/Z on STEP refused before writing
            string work = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-scale-work-not-made");
            string step = @"C:\x\part.step";
            True("PrepareScaled: Uniform(150) and None = same path, no folder", BambuSlicer.PrepareScaled(step, ScaleXyz.Uniform(150), work) == step
                 && BambuSlicer.PrepareScaled(step, ScaleXyz.None, work) == step && !System.IO.Directory.Exists(work));
            threw = false;
            try { BambuSlicer.PrepareScaled(step, new ScaleXyz(150, 100, 50), work); } catch (SliceException) { threw = true; }
            True("PrepareScaled: 150/100/50 on .step throws SliceException, no folder", threw && !System.IO.Directory.Exists(work));
            // ---- ScaleNormal: (0,0,1) at 100/100/50 -> (0,0,2) -> (0,0,1); (1,1,0) at 2/1/1 -> (0,5; 1; 0) / √1,25 = (0,4472; 0,8944; 0)
            var n1 = BambuSlicer.ScaleNormal(0, 0, 1, 1, 1, 0.5);
            var n2 = BambuSlicer.ScaleNormal(1, 1, 0, 2, 1, 1);
            var n0 = BambuSlicer.ScaleNormal(0, 0, 0, 2, 1, 1);
            True("ScaleNormal: (0,0,1) stays; (1,1,0) at X 2 -> (0,4472; 0,8944; 0); zero stays zero", n1[0] == 0 && n1[1] == 0 && n1[2] == 1
                 && Math.Abs(n2[0] - 0.4472136) < 1e-6 && Math.Abs(n2[1] - 0.8944272) < 1e-6 && n2[2] == 0 && n0[0] == 0 && n0[1] == 0 && n0[2] == 0);
            // ---- ScaleStlLine / ScaleObjLine at 200/100/50: (1,2,3) -> (2,2,1,5)
            var s2 = new ScaleXyz(200, 100, 50);
            True("ScaleStlLine: \"  vertex 1 2 3\" -> \"  vertex 2 2 1.5\"", BambuSlicer.ScaleStlLine("  vertex 1 2 3", s2) == "  vertex 2 2 1.5");
            True("ScaleStlLine: normal 0 0 1 / outer loop / solid x / vertex a b c unchanged", BambuSlicer.ScaleStlLine("facet normal 0 0 1", s2) == "facet normal 0 0 1"
                 && BambuSlicer.ScaleStlLine("outer loop", s2) == "outer loop" && BambuSlicer.ScaleStlLine("solid x", s2) == "solid x"
                 && BambuSlicer.ScaleStlLine("vertex a b c", s2) == "vertex a b c");
            True("ScaleObjLine: v 1 2 3 -> v 2 2 1.5; colour tail kept; vn 1 0 0 stays", BambuSlicer.ScaleObjLine("v 1 2 3", s2) == "v 2 2 1.5"
                 && BambuSlicer.ScaleObjLine("v 1 2 3 0.5 0.5 0.5", s2) == "v 2 2 1.5 0.5 0.5 0.5" && BambuSlicer.ScaleObjLine("vn 1 0 0", s2) == "vn 1 0 0");
            True("ScaleObjLine: vt / f / comment unchanged", BambuSlicer.ScaleObjLine("vt 0.5 0.5", s2) == "vt 0.5 0.5" && BambuSlicer.ScaleObjLine("f 1 2 3", s2) == "f 1 2 3"
                 && BambuSlicer.ScaleObjLine("# c", s2) == "# c");
            // ---- ScaleStl binary: 2 triangles, header "H" x 80, attribute bytes AB CD; at 200/100/50: (1,2,3) -> (2,2,1,5), (7,8,9) -> (14,8,4,5)
            var head = new byte[84];
            for (int i = 0; i < 80; i++) head[i] = (byte)'H';
            Buffer.BlockCopy(BitConverter.GetBytes((uint)2), 0, head, 80, 4);
            var t1 = StlTriangle(new float[] { 0, 0, 1 }, new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 0xAB, 0xCD);
            var stl = new byte[184];
            Buffer.BlockCopy(head, 0, stl, 0, 84); Buffer.BlockCopy(t1, 0, stl, 84, 50); Buffer.BlockCopy(t1, 0, stl, 134, 50);
            True("IsBinaryStl: 84 + 50 × 2 = 184 with count 2 -> binary; 144 bytes -> not", BambuSlicer.IsBinaryStl(184, head) && !BambuSlicer.IsBinaryStl(144, head));
            var outB = new System.IO.MemoryStream();
            BambuSlicer.ScaleStl(new System.IO.MemoryStream(stl), outB, s2);
            var o = outB.ToArray();
            bool headSame = o.Length == 184;
            for (int i = 0; headSame && i < 84; i++) if (o[i] != stl[i]) headSame = false;
            True("ScaleStl binary: same length, header + count kept, attribute bytes kept", headSame && o[84 + 48] == 0xAB && o[84 + 49] == 0xCD && o[134 + 49] == 0xCD);
            True("ScaleStl binary: vertices (2,2,1,5) (8,5,3) (14,8,4,5), normal (0,0,1)", F32(o, 84 + 12) == 2 && F32(o, 84 + 16) == 2 && F32(o, 84 + 20) == 1.5f
                 && F32(o, 84 + 24) == 8 && F32(o, 84 + 28) == 5 && F32(o, 84 + 32) == 3 && F32(o, 134 + 36) == 14 && F32(o, 134 + 40) == 8 && F32(o, 134 + 44) == 4.5f
                 && F32(o, 84) == 0 && F32(o, 92) == 1);
            // a binary STL whose triangles end early (the stream claims 184 bytes, holds 144)
            var cut = new byte[144];
            Buffer.BlockCopy(stl, 0, cut, 0, 144);
            threw = false;
            try { BambuSlicer.ScaleStl(new LongerStream(cut, 184), new System.IO.MemoryStream(), s2); }
            catch (System.IO.InvalidDataException) { threw = true; }
            True("ScaleStl binary: cut off -> InvalidDataException", threw);
            // not "solid" text, count 5 but 150 bytes = 84 + 66 (66 is no whole number of 50-byte triangles) -> broken
            var broken = new byte[150];
            Buffer.BlockCopy(head, 0, broken, 0, 80);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)5), 0, broken, 80, 4);
            threw = false;
            try { BambuSlicer.ScaleStl(new System.IO.MemoryStream(broken), new System.IO.MemoryStream(), s2); }
            catch (System.IO.InvalidDataException) { threw = true; }
            True("ScaleStl: binary-looking, count 5, 150 bytes -> InvalidDataException", threw);
            // count field 0 but 184 bytes = 84 + 2 × 50: read as binary with count 2 (written as 2), vertices scaled
            var zero = (byte[])stl.Clone();
            Buffer.BlockCopy(BitConverter.GetBytes((uint)0), 0, zero, 80, 4);
            var outZ = new System.IO.MemoryStream();
            BambuSlicer.ScaleStl(new System.IO.MemoryStream(zero), outZ, s2);
            var oz = outZ.ToArray();
            True("ScaleStl: count 0 + 2 whole triangles -> binary, count 2, (1,2,3) -> (2,2,1,5)", oz.Length == 184 && BitConverter.ToUInt32(oz, 80) == 2
                 && oz[0] == (byte)'H' && F32(oz, 84 + 12) == 2 && F32(oz, 84 + 20) == 1.5f && oz[134 + 49] == 0xCD);
            // ASCII
            string ascii = "solid t\nfacet normal 0 0 1\n outer loop\n  vertex 1 2 3\n  vertex 4 5 6\n  vertex 7 8 9\n endloop\nendfacet\nendsolid t\n";
            var ab = System.Text.Encoding.ASCII.GetBytes(ascii);
            var h84 = new byte[84]; Buffer.BlockCopy(ab, 0, h84, 0, 84);
            True("IsBinaryStl: ASCII \"solid\" file -> false", !BambuSlicer.IsBinaryStl(ab.Length, h84));
            var outA = new System.IO.MemoryStream();
            BambuSlicer.ScaleStl(new System.IO.MemoryStream(ab), outA, s2);
            string at = System.Text.Encoding.UTF8.GetString(outA.ToArray());
            True("ScaleStl ASCII: all vertices scaled, rest kept", at.Contains("  vertex 2 2 1.5") && at.Contains("  vertex 8 5 3") && at.Contains("  vertex 14 8 4.5")
                 && at.Contains("facet normal 0 0 1") && at.StartsWith("solid t") && at.Contains("endsolid t") && !at.Contains("vertex 1 2 3"));
            // ---- ScaleTransform at 150/100/50: column j × axis j, translation kept
            var s3 = new ScaleXyz(150, 100, 50);
            True("ScaleTransform: none -> 1.5 0 0 0 1 0 0 0 0.5 0 0 0", BambuSlicer.ScaleTransform(null, s3) == "1.5 0 0 0 1 0 0 0 0.5 0 0 0");
            True("ScaleTransform: Benchy item, translation kept", BambuSlicer.ScaleTransform("1 0 0 0 1 0 0 0 1 127.17549 128 0", s3)
                 == "1.5 0 0 0 1 0 0 0 0.5 127.17549 128 0");
            True("ScaleTransform: 90° about Z at X 200 -> world X longer (-1 -> -2)", BambuSlicer.ScaleTransform("0 1 0 -1 0 0 0 0 1 10 20 0", new ScaleXyz(200, 100, 100))
                 == "0 1 0 -2 0 0 0 0 1 10 20 0");
            True("ScaleTransform: 11 numbers / words -> null", BambuSlicer.ScaleTransform("1 0 0 0 1 0 0 0 1 0 0", s3) == null
                 && BambuSlicer.ScaleTransform("a b c d e f g h i j k l", s3) == null);
            // ---- ScaleBuildItems (trimmed 3D/3dmodel.model of plans\issue8\files\benchy_project.3mf)
            const string model = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\" "
                + "xmlns:p=\"http://schemas.microsoft.com/3dmanufacturing/production/2015/06\" requiredextensions=\"p\">\n <resources>\n"
                + "  <object id=\"2\" type=\"model\">\n   <components>\n    <component p:path=\"/3D/Objects/object_1.model\" objectid=\"1\" "
                + "transform=\"1 0 0 0 1 0 0 0 1 0.824500084 0 24\"/>\n   </components>\n  </object>\n </resources>\n"
                + " <build p:UUID=\"2c7c17d8\">\n  <item objectid=\"2\" p:UUID=\"00000002\" transform=\"1 0 0 0 1 0 0 0 1 127.17549 128 0\" printable=\"1\"/>\n </build>\n</model>";
            string sb = BambuSlicer.ScaleBuildItems(model, s3);
            True("ScaleBuildItems: item scaled, component untouched", sb.Contains("transform=\"1.5 0 0 0 1 0 0 0 0.5 127.17549 128 0\" printable=\"1\"/>")
                 && sb.Contains("transform=\"1 0 0 0 1 0 0 0 1 0.824500084 0 24\"") && !sb.Contains("\"1 0 0 0 1 0 0 0 1 127.17549 128 0\""));
            string two = "<model><build><item objectid=\"2\"/><item objectid=\"3\" transform=\"1 0 0 0 1 0 0 0 1 5 5 0\"/></build></model>";
            string tb = BambuSlicer.ScaleBuildItems(two, s3);
            True("ScaleBuildItems: item without transform gets one; two items both scaled", tb.Contains("<item objectid=\"2\" transform=\"1.5 0 0 0 1 0 0 0 0.5 0 0 0\"/>")
                 && tb.Contains("<item objectid=\"3\" transform=\"1.5 0 0 0 1 0 0 0 0.5 5 5 0\"/>"));
            string pre = "<m:model><m:build><m:item objectid=\"1\" transform=\"1 0 0 0 1 0 0 0 1 5 5 0\"/></m:build></m:model>";
            True("ScaleBuildItems: prefixed <m:build> / <m:item>", BambuSlicer.ScaleBuildItems(pre, s3).Contains("transform=\"1.5 0 0 0 1 0 0 0 0.5 5 5 0\""));
            threw = false;
            try { BambuSlicer.ScaleBuildItems("<model><resources/></model>", s3); } catch (SliceException) { threw = true; }
            True("ScaleBuildItems: no <build> -> SliceException", threw);
            // ---- RootModelPath
            string rels = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n"
                + " <Relationship Target=\"/3D/3dmodel.model\" Id=\"rel-1\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/>\n</Relationships>";
            True("RootModelPath: Bambu rels -> 3D/3dmodel.model; /3D/model.model -> 3D/model.model; null -> default",
                 BambuSlicer.RootModelPath(rels) == "3D/3dmodel.model" && BambuSlicer.RootModelPath(rels.Replace("/3D/3dmodel.model", "/3D/model.model")) == "3D/model.model"
                 && BambuSlicer.RootModelPath(null) == "3D/3dmodel.model");
        }

        // ---------- issue #8 step 1, online's pure helpers in Slicer.cs / SliceInfo.cs ----------

        static void ScaleSlicerTests()
        {
            Func<List<string>, string> join = l => l == null ? "NULL" : string.Join(" ", l);
            True("ScaleArgs: 100 and 0 -> none", BambuSlicer.ScaleArgs(100).Count == 0 && BambuSlicer.ScaleArgs(0).Count == 0);
            True("ScaleArgs: 150 -> --scale 1.5 --ensure-on-bed", join(BambuSlicer.ScaleArgs(150)) == "--scale 1.5 --ensure-on-bed");
            var oldCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("da-DK");
                True("ScaleArgs: Danish Windows still \"1.5\"", join(BambuSlicer.ScaleArgs(150)) == "--scale 1.5 --ensure-on-bed");
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = oldCulture; }
            True("ScaleArgs: 33,3 -> 0.333", join(BambuSlicer.ScaleArgs(33.3)) == "--scale 0.333 --ensure-on-bed");
            foreach (double bad in new[] { 501.0, 5.0 })
            {
                bool threw = false;
                try { BambuSlicer.ScaleArgs(bad); } catch (SliceException) { threw = true; }
                True("ScaleArgs: " + bad + " throws SliceException", threw);
            }

            double p;
            True("TryParseScale: \"150 %\" -> 150", BambuSlicer.TryParseScale("150 %", out p) && p == 150);
            True("TryParseScale: \"87,5\" -> 87,5", BambuSlicer.TryParseScale("87,5", out p) && p == 87.5);
            True("TryParseScale: \"87.5\" -> 87,5", BambuSlicer.TryParseScale("87.5", out p) && p == 87.5);
            True("TryParseScale: abc / 9,9 / 500,1 / empty rejected", !BambuSlicer.TryParseScale("abc", out p) && !BambuSlicer.TryParseScale("9,9", out p)
                 && !BambuSlicer.TryParseScale("500,1", out p) && !BambuSlicer.TryParseScale("", out p));

            // result.json bbox (trimmed from plans\issue8\runs\b_scale150_eob: Benchy at 150 %)
            const string sample = "{\"sliced_plates\":[{\"main_predication\":4645.6,\"total_predication\":4664.7,\"filament_change_times\":0,"
                + "\"filaments\":[{\"id\":1,\"main_used_g\":30.57,\"total_used_g\":30.57}],"
                + "\"objects\":[{\"bbox\":{\"depth\":46.50599670410156,\"height\":72.0,\"width\":90.00150299072266,\"x\":83,\"y\":104.7,\"z\":0},\"id\":5,\"name\":\"3dbenchy.stl\"}]}]}";
            var st = BambuSlicer.ParseSliceResult(sample);
            True("ParseSliceResult: one object box", st != null && st.ObjectSizes.Count == 1);
            var res = new SliceResult();
            BambuSlicer.ApplySliceStats(res, st);
            True("ParseSliceResult: Benchy 150 % = 90,0 × 46,5 × 72,0 mm", BambuSlicer.SizeText(res.SizeX, res.SizeY, res.SizeZ) == "90,0 × 46,5 × 72,0 mm"
                 && res.SizeObjects == 1);
            // biggest by volume: 10×10×50 (5000) vs 20×20×20 (8000) vs 30×30×5 (4500) -> 20×20×20, 3 objects
            var two = new SliceResult();
            BambuSlicer.SetSize(two, new List<double[]> { new[] { 10.0, 10, 50 }, new[] { 20.0, 20, 20 }, new[] { 30.0, 30, 5 } });
            True("SetSize: biggest box 20×20×20 of 3", two.SizeX == 20 && two.SizeY == 20 && two.SizeZ == 20 && two.SizeObjects == 3);
            // AddResult: total keeps the bigger box (Benchy 90 × 46,5 × 72 ≈ 301 000 mm³ > 20 × 20 × 20 = 8000) and sums objects
            var total = new SliceResult();
            BambuSlicer.AddResult(total, two, "a.stl");
            BambuSlicer.AddResult(total, res, "b.stl");
            True("AddResult: Benchy's box kept (bigger), 3 + 1 = 4 objects", total.SizeZ == 72 && Math.Abs(total.SizeX - 90.0015) < 0.001 && total.SizeObjects == 4);
            var total2 = new SliceResult();
            BambuSlicer.AddResult(total2, res, "b.stl");
            BambuSlicer.AddResult(total2, two, "a.stl");
            True("AddResult: other order, same biggest box", total2.SizeZ == 72 && total2.SizeObjects == 4);
            True("SizeAt: 48 mm at 100 % -> 72 at 150 %; bad % unchanged", BambuSlicer.SizeAt(48, 100, 150) == 72 && BambuSlicer.SizeAt(48, 0, 150) == 48);
            True("FitsBed: A1 257 mm high no, 256 yes", !BambuSlicer.FitsBed("A1", 100, 100, 257) && BambuSlicer.FitsBed("A1", 256, 256, 256));
            True("FitsBed: A1 mini 180×100×50 yes, 181 no; X/Y swap", BambuSlicer.FitsBed("A1M", 180, 100, 50) && BambuSlicer.FitsBed("A1M", 100, 180, 50)
                 && !BambuSlicer.FitsBed("A1M", 181, 100, 50));
            True("FitsBed: unknown size = fits", BambuSlicer.FitsBed("A1M", 0, 500, 500));
            True("CanScale: .gcode no, .stl / .3mf yes, empty no", !BambuSlicer.CanScale(@"C:\x\a.gcode") && BambuSlicer.CanScale(@"C:\x\a.stl")
                 && BambuSlicer.CanScale(@"C:\x\a.3mf") && !BambuSlicer.CanScale(""));
            True("ReadAsSliced: .gcode read at 100 %, not at 150 % (SliceFile refuses the scale); missing .stl not", BambuSlicer.ReadAsSliced(@"C:\x\a.gcode", 100)
                 && !BambuSlicer.ReadAsSliced(@"C:\x\a.gcode", 150) && !BambuSlicer.ReadAsSliced(@"C:\x\no-such-file.stl", 150));
            TooSmallTests();
            SlicedOffBedTests();
        }

        // ---------- paid-tick (plans\paid-tick part A): QuoteRecord.PaidAt, SetPaid, InvoiceRow.Paid, Payment filter, UnpaidSummary ----------

        static void PaidTests()
        {
            var now = new DateTime(2026, 11, 20, 12, 0, 0);
            // JSON: PaidAt always written ("" = unpaid); both save back byte-identical; old records unpaid
            var u = Inv(now.AddDays(-5), "2026-11111", "2026-11-18 10:00:00", "2026-12-02", 72.72);
            var p = Inv(now.AddDays(-5), "2026-44444", "2026-10-18 10:00:00", "2026-11-01", 40.00); p.PaidAt = "2026-11-05";
            string ju = History.ToJson(new List<QuoteRecord> { u }), jp = History.ToJson(new List<QuoteRecord> { p });
            True("Paid JSON: unpaid \"PaidAt\":\"\", paid \"PaidAt\":\"2026-11-05\"", ju.Contains("\"PaidAt\":\"\"") && jp.Contains("\"PaidAt\":\"2026-11-05\""));
            // an unpaid record's text differs from the paid one only in the PaidAt value (nothing else moves)
            var u2 = History.ParseJson(ju)[0]; u2.PaidAt = "2026-11-05";
            True("Paid JSON: ticking changes only the PaidAt value", History.ToJson(new List<QuoteRecord> { u2 }) == ju.Replace("\"PaidAt\":\"\"", "\"PaidAt\":\"2026-11-05\""));
            var bu = History.ParseJson(ju); var bp = History.ParseJson(jp);
            True("Paid JSON: round trip byte-identical, unpaid / paid 05-11-2026", History.ToJson(bu) == ju && History.ToJson(bp) == jp
                 && !History.IsPaid(bu[0]) && bu[0].PaidAt == "" && History.IsPaid(bp[0]) && bp[0].PaidAt == "2026-11-05");
            var old = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"InvoiceNo\":\"2026-K7Q4\"}]}");
            True("Paid: old invoice without the key = unpaid, saved back with \"PaidAt\":\"\"", old[0].PaidAt == "" && !History.IsPaid(old[0])
                 && History.ToJson(old).Contains("\"PaidAt\":\"\""));
            // clean-up: unreadable / no invoice number -> unpaid; spaces trimmed
            var cl = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"a\",\"InvoiceNo\":\"2026-1\",\"PaidAt\":\"soon\"},"
                                       + "{\"Id\":\"b\",\"PaidAt\":\"2026-11-05\"},{\"Id\":\"c\",\"InvoiceNo\":\"2026-2\",\"PaidAt\":\" 2026-11-05 \"},{\"Id\":\"d\",\"InvoiceNo\":\"2026-3\",\"PaidAt\":null}]}");
            True("Paid clean-up: bad date / no invoice / null -> \"\", \" 2026-11-05 \" trimmed", cl[0].PaidAt == "" && cl[1].PaidAt == "" && cl[2].PaidAt == "2026-11-05"
                 && cl[3].PaidAt == "");
            True("Paid: CopyQuotes keeps it", History.CopyQuotes(new List<QuoteRecord> { p })[0].PaidAt == "2026-11-05");
            DateTime pd;
            True("PaidOn: 05-11-2026; unpaid / null false", History.PaidOn(p, out pd) && pd == new DateTime(2026, 11, 5) && !History.PaidOn(u, out pd) && !History.PaidOn(null, out pd));

            // SetPaid on a combined invoice (3 jobs, one with spaces around the number); a job already paid keeps its date
            var b1 = Inv(now.AddDays(-4), "2026-22222", "2026-11-19 09:00:00", "2026-11-19", 10.10);
            var b2 = Inv(now.AddDays(-3), "2026-22222", "2026-11-19 09:00:00", "2026-11-19", 20.20);
            var b3 = Inv(now.AddDays(-2), " 2026-22222 ", "2026-11-19 09:00:00", "2026-11-19", 30.30);
            var other = HistRecord(now);
            var list = new List<QuoteRecord> { b3, other, u, b1, p, b2 };
            b2.PaidAt = "2026-11-19";
            True("SetPaid: 2 of 3 jobs change (one already paid keeps 19-11), others untouched", History.SetPaid(list, "2026-22222", true, now) == 2
                 && b1.PaidAt == "2026-11-20" && b3.PaidAt == "2026-11-20" && b2.PaidAt == "2026-11-19" && u.PaidAt == "" && other.PaidAt == "");
            True("SetPaid: again = 0; unknown / empty number = 0", History.SetPaid(list, "2026-22222", true, now) == 0
                 && History.SetPaid(list, "2026-99999", true, now) == 0 && History.SetPaid(list, "", true, now) == 0 && History.SetPaid(null, "x", true, now) == 0);
            var rows = History.Invoices(list, now);
            var B = rows.Find(x => x.No == "2026-22222");
            True("Invoices: combined all paid -> Paid, PaidAt = latest 20-11, not overdue though past due", B.Paid && B.PaidAt == new DateTime(2026, 11, 20)
                 && !B.Overdue(now));
            True("SetPaid off: 3 jobs unpaid (case / spaces ignored)", History.SetPaid(list, " 2026-22222", false, now) == 3 && b1.PaidAt == "" && b2.PaidAt == "" && b3.PaidAt == "");
            b1.PaidAt = "2026-11-20";   // only one of three paid
            rows = History.Invoices(list, now);
            B = rows.Find(x => x.No == "2026-22222");
            var A = rows.Find(x => x.No == "2026-11111");
            var P = rows.Find(x => x.No == "2026-44444");
            True("Invoices: 1 of 3 paid = unpaid, overdue (due 19-11); A unpaid not due; P paid 05-11 not overdue", !B.Paid && B.PaidAt == DateTime.MinValue && B.Overdue(now)
                 && !A.Paid && !A.Overdue(now) && P.Paid && P.PaidAt == new DateTime(2026, 11, 5) && !P.Overdue(now));
            // Payment filter (rows newest first: B 19-11, A 18-11, P 18-10)
            Func<int, string> pay = delegate(int payment)
            {
                var hits = new List<string>();
                foreach (var r in rows)
                    if (History.InvoiceMatches(r, "", History.InvoiceStatusAll, DateTime.MinValue, DateTime.MaxValue, payment, now)) hits.Add(r.No);
                return string.Join(",", hits);
            };
            True("Payment filter: all / unpaid / overdue / paid", pay(History.PaymentAll) == "2026-22222,2026-11111,2026-44444"
                 && pay(History.PaymentUnpaid) == "2026-22222,2026-11111" && pay(History.PaymentOverdue) == "2026-22222" && pay(History.PaymentPaid) == "2026-44444");
            True("Payment filter: search still applies", !History.InvoiceMatches(B, "nothing", History.InvoiceStatusAll, DateTime.MinValue, DateTime.MaxValue, History.PaymentAll, now));
            // unpaid: B 10,10 + 20,20 + 30,30 = 60,60 and A 72,72 = 133,32 kr., 2 invoices, 1 overdue
            var us = History.UnpaidSummary(rows, now);
            True("UnpaidSummary: 2 invoices, 133,32 kr., 1 overdue", us.Count == 2 && Math.Abs(us.Total - 133.32) < 1e-9 && us.Overdue == 1);
            var none = History.UnpaidSummary(null, now);
            True("UnpaidSummary: null -> 0", none.Count == 0 && none.Total == 0 && none.Overdue == 0);

            // re-issuing an invoice keeps its paid date
            var ri = HistRecord(now);
            var rl = new List<QuoteRecord> { ri };
            string no = History.MakeInvoice(rl, ri, now, new AppSettings());
            History.SetPaid(rl, no, true, now);
            string no2 = History.MakeInvoice(rl, ri, now.AddDays(1), new AppSettings());
            True("Re-issue keeps the number and PaidAt 20-11", no2 == no && ri.PaidAt == "2026-11-20");

            // backup: merge fills only when unpaid here; replace takes the backup's
            var theirs = BRec("P", "2026-11-20 12:00:00"); theirs.InvoiceNo = "2026-55555"; theirs.BilledAt = "2026-11-20 12:00:00"; theirs.PaidAt = "2026-11-10";
            var mine = BRec("P", "2026-11-20 12:00:00"); mine.InvoiceNo = "2026-55555"; mine.BilledAt = "2026-11-20 12:00:00";
            var res = History.Combine(new List<QuoteRecord> { mine }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("Paid merge: unpaid here -> 10-11 from the backup", res.Quotes[0].PaidAt == "2026-11-10");
            var mine2 = BRec("P", "2026-11-20 12:00:00"); mine2.InvoiceNo = "2026-55555"; mine2.BilledAt = "2026-11-20 12:00:00"; mine2.PaidAt = "2026-11-01";
            res = History.Combine(new List<QuoteRecord> { mine2 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { theirs } }, false, false);
            True("Paid merge: paid 01-11 here stays", res.Quotes[0].PaidAt == "2026-11-01");
            var unpaidBackup = BRec("P", "2026-11-20 12:00:00"); unpaidBackup.InvoiceNo = "2026-55555"; unpaidBackup.BilledAt = "2026-11-20 12:00:00";
            res = History.Combine(new List<QuoteRecord> { mine2 }, new AppSettings(), new BackupData { Quotes = new List<QuoteRecord> { unpaidBackup } }, true, false);
            True("Paid restore (replace): the backup's unpaid wins", res.Quotes.Count == 1 && res.Quotes[0].PaidAt == "");

            // the PDF line
            bool wasDanish = L.Danish;
            try
            {
                L.Danish = false;
                True("PaidText EN (long date like the invoice header)", History.PaidText(new DateTime(2026, 11, 12)) == "Paid 12 November 2026 – thank you"
                     && History.PaidText(new DateTime(2026, 11, 5)) == "Paid 5 November 2026 – thank you"
                     && History.PaidText(new DateTime(2026, 11, 12)).Contains(QuoteRows.LongDate(new DateTime(2026, 11, 12))));
                L.Danish = true;
                True("PaidText DA (long date like the invoice header)", History.PaidText(new DateTime(2026, 11, 12)) == "Betalt 12. november 2026 – tak"
                     && History.PaidText(new DateTime(2026, 11, 12)).Contains(QuoteRows.LongDate(new DateTime(2026, 11, 12))));
            }
            finally { L.Danish = wasDanish; }
        }

        // ---------- off the plate after slicing (plans\too-small): BambuSlicer.SlicedOffBed; samples tests\gcode-samples\results ----------

        static void SlicedOffBedTests()
        {
            double nan = double.NaN;
            True("SlicedOffBed: A1 321,75 × 321,75 × 240 (Benchy 500 %) yes", BambuSlicer.SlicedOffBed("A1", nan, nan, 321.75, 321.75, 240));
            // 2,0 + 252,0 = 254,0 <= 256; 62,9 + 130,2 = 193,1
            True("SlicedOffBed: A1 at 2,0 / 62,9, 252,0 × 130,2 × 201,6 no", !BambuSlicer.SlicedOffBed("A1", 2.0, 62.9, 252.0, 130.2, 201.6));
            True("SlicedOffBed: A1 from x -18,5 yes; -0,4 no (0,5 mm slack)", BambuSlicer.SlicedOffBed("A1", -18.5, 50, 100, 100, 100)
                 && !BambuSlicer.SlicedOffBed("A1", -0.4, 0, 100, 100, 10));
            // 200 + 60 = 260 > 256 + 0,5
            True("SlicedOffBed: A1 at x 200, 60 wide yes", BambuSlicer.SlicedOffBed("A1", 200, 10, 60, 50, 10));
            // A1 mini 180: 10 + 175 = 185 > 180,5 yes; 2 + 175 = 177 no
            True("SlicedOffBed: A1 mini 10 + 175 yes, 2 + 175 no", BambuSlicer.SlicedOffBed("A1M", 10, 10, 175, 50, 10) && !BambuSlicer.SlicedOffBed("A1M", 2, 10, 175, 50, 10));
            True("SlicedOffBed: P1S 251 mm high (max 250) yes", BambuSlicer.SlicedOffBed("P1S", nan, nan, 100, 100, 251));
            True("SlicedOffBed: unknown size (0) no", !BambuSlicer.SlicedOffBed("A1", 0, 0, 0, 10, 10));

            // result.json: two plates of 150 × 150 × 3 (plate 2 at x 360,2 = the plate grid, size check only) -> fits
            string dir = System.IO.Path.Combine(GcodeDir(), "results");
            string two = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "two_plates_result.json"));
            True("SlicedOffBed(stats): two plates 150 × 150 fit (plate 2's x 360 not a position)", !BambuSlicer.SlicedOffBed("A1", BambuSlicer.ParseSliceResult(two)));
            string wide = two.Replace("\"width\": 149.99998474121094", "\"width\": 300");
            True("SlicedOffBed(stats): plate 2 300 wide -> yes (sample edited)", wide != two && BambuSlicer.SlicedOffBed("A1", BambuSlicer.ParseSliceResult(wide)));
            string big = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "benchy_500_result.json"));
            True("SlicedOffBed(stats): Benchy 500 % (321,75 mm from x -18,5) yes", BambuSlicer.SlicedOffBed("A1", BambuSlicer.ParseSliceResult(big)));
            True("SlicedOffBed(stats): null no", !BambuSlicer.SlicedOffBed("A1", (SliceStats)null));
        }

        // ---------- too small to print (plans\too-small): -100 "Failed slicing the model" on a scaled-down job ----------

        static void TooSmallTests()
        {
            const string failed = "Failed slicing the model. Please verify the slicing of all plates on Orca Slicer before uploading.";
            True("IsFailedSlice: -100 / 0 + \"Failed slicing the model\" yes", BambuSlicer.IsFailedSlice(-100, null) && BambuSlicer.IsFailedSlice(0, failed));
            True("IsFailedSlice: 0 / 0 boom / -50 with the text / -17 no", !BambuSlicer.IsFailedSlice(0, null) && !BambuSlicer.IsFailedSlice(0, "boom")
                 && !BambuSlicer.IsFailedSlice(-50, failed) && !BambuSlicer.IsFailedSlice(-17, null));
            True("IsTooSmallFailure: -100 at 20 % and at 100 × 100 × 50 % yes", BambuSlicer.IsTooSmallFailure(-100, "x", ScaleXyz.Uniform(20))
                 && BambuSlicer.IsTooSmallFailure(-100, "x", new ScaleXyz(100, 100, 50)));
            True("IsTooSmallFailure: -100 at 100 % / 150 % / None / null no", !BambuSlicer.IsTooSmallFailure(-100, "x", ScaleXyz.Uniform(100))
                 && !BambuSlicer.IsTooSmallFailure(-100, "x", ScaleXyz.Uniform(150)) && !BambuSlicer.IsTooSmallFailure(-100, "x", ScaleXyz.None)
                 && !BambuSlicer.IsTooSmallFailure(-100, "x", null));
            True("IsTooSmallFailure: -50 / -61 / -6 at 20 % no", !BambuSlicer.IsTooSmallFailure(-50, "x", ScaleXyz.Uniform(20))
                 && !BambuSlicer.IsTooSmallFailure(-61, "x", ScaleXyz.Uniform(20)) && !BambuSlicer.IsTooSmallFailure(-6, "x", ScaleXyz.Uniform(20)));
            True("IsTooSmallFailure(exception): null no, (m, -100, x) at 20 % yes, plain message no",
                 !BambuSlicer.IsTooSmallFailure((SliceException)null, ScaleXyz.Uniform(20))
                 && BambuSlicer.IsTooSmallFailure(new SliceException("m", -100, "x"), ScaleXyz.Uniform(20))
                 && !BambuSlicer.IsTooSmallFailure(new SliceException("m"), ScaleXyz.Uniform(20)));
            True("IsSmallSize: 9,9 / 9,94 yes; 9,96 / 10 / unknown / NaN no", BambuSlicer.IsSmallSize(20, 20, 9.9) && BambuSlicer.IsSmallSize(20, 20, 9.94)
                 && !BambuSlicer.IsSmallSize(20, 20, 9.96) && !BambuSlicer.IsSmallSize(20, 20, 10) && !BambuSlicer.IsSmallSize(0, 5, 5)
                 && !BambuSlicer.IsSmallSize(double.NaN, 5, 5));
            bool wasDanish = L.Danish;
            try
            {
                // 60 × 31 × 48 mm at 20 % = 12,0 × 6,2 × 9,6 mm
                L.Danish = false;
                True("TooSmallText EN with size", BambuSlicer.TooSmallText(ScaleXyz.Uniform(20), 60, 31, 48) ==
                     "Too small at 20 % (12,0 × 6,2 × 9,6 mm): the thinnest parts disappear when sliced. Try a bigger size.");
                const string noSizeEn = "Too small at 20 %: the thinnest parts disappear when sliced. Try a bigger size.";
                True("TooSmallText EN unknown size (0 / NaN)", BambuSlicer.TooSmallText(ScaleXyz.Uniform(20), 0, 31, 48) == noSizeEn
                     && BambuSlicer.TooSmallText(ScaleXyz.Uniform(20), double.NaN, 31, 48) == noSizeEn);
                // 20 × 20 × 10 at 100 × 100 × 50 % = 20,0 × 20,0 × 5,0 mm
                string xyz = BambuSlicer.TooSmallText(new ScaleXyz(100, 100, 50), 20, 20, 10);
                True("TooSmallText X/Y/Z: \"100 × 100 × 50 %\", \"20,0 × 20,0 × 5,0 mm\"", xyz.Contains("100 × 100 × 50 %") && xyz.Contains("20,0 × 20,0 × 5,0 mm"));
                True("SliceErrorText -100 EN = plain too-thin text", BambuSlicer.SliceErrorText(-100, "a.stl", failed, "A1")
                     == "Bambu Studio could not slice a.stl: parts of the model are too thin to print, or the model is damaged.");
                L.Danish = true;
                True("TooSmallText DA with size", BambuSlicer.TooSmallText(ScaleXyz.Uniform(20), 60, 31, 48) ==
                     "For lille ved 20 % (12,0 × 6,2 × 9,6 mm): de tyndeste dele forsvinder ved slicing. Prøv en større størrelse.");
                const string noSizeDa = "For lille ved 20 %: de tyndeste dele forsvinder ved slicing. Prøv en større størrelse.";
                True("TooSmallText DA unknown size (0 / NaN)", BambuSlicer.TooSmallText(ScaleXyz.Uniform(20), 0, 31, 48) == noSizeDa
                     && BambuSlicer.TooSmallText(ScaleXyz.Uniform(20), double.NaN, 31, 48) == noSizeDa);
                True("SliceErrorText -100 DA = plain too-thin text", BambuSlicer.SliceErrorText(-100, "a.stl", failed, "A1")
                     == "Bambu Studio kunne ikke slice a.stl: dele af modellen er for tynde til at printe, eller modellen er beskadiget.");
            }
            finally { L.Danish = wasDanish; }
        }

        // ---------- issue #7 part 2: Invoices window helpers (History.Invoices / InvoiceMatches / InvoicePeriod / AgeText) ----------

        static QuoteRecord Inv(DateTime saved, string no, string billedAt, string due, double price)
        {
            var r = HistRecord(saved);
            r.InvoiceNo = no; r.BilledAt = billedAt; r.InvoiceDue = due; r.OthersPrice = price;
            return r;
        }

        static void InvoiceListTests()
        {
            var now = new DateTime(2026, 11, 20, 12, 0, 0);
            var a = Inv(now.AddDays(-5), "2026-11111", "2026-11-18 10:00:00", "2026-12-02", 72.72);
            a.Customer = "Lene Hansen"; History.SetPrintState(a, PrintState.Printed, now);
            var b1 = Inv(now.AddDays(-4), "2026-22222", "2026-11-19 09:00:00", "2026-11-19", 10.10);
            var b2 = Inv(now.AddDays(-3), "2026-22222", "2026-11-19 09:00:00", "2026-11-19", 20.20); b2.BilledTo = "Firma ApS";
            var b3 = Inv(now.AddDays(-2), " 2026-22222 ", "2026-11-19 09:00:00", "2026-11-19", 30.30); b3.Model = "Vase";
            History.SetPrintState(b1, PrintState.Printed, now);   // b2, b3 not printed -> mixed
            var c = Inv(new DateTime(2025, 6, 1, 7, 0, 0), "2025-K7Q4", "2025-06-01 08:00:00", "", 0);   // old letter code, no due snapshot
            c.For = History.ForFriends; c.FriendsPrice = 5.5; History.SetPrintState(c, PrintState.Failed, now);
            var d = HistRecord(now);                                                       // no invoice: skipped
            var e = Inv(now.AddDays(-1), "2026-33333", "bad", "", 1);                      // unreadable date: last
            var list = new List<QuoteRecord> { b3, d, a, e, b1, c, b2 };
            var rows = History.Invoices(list, now);
            True("Invoices: 4 rows, newest first B, A, C, E", rows.Count == 4 && rows[0].No == "2026-22222" && rows[1].No == "2026-11111"
                 && rows[2].No == "2025-K7Q4" && rows[3].No == "2026-33333");
            var B = rows[0];
            True("Invoices: combined = 3 jobs oldest first", B.Jobs.Count == 3 && B.Jobs[0] == b1 && B.Jobs[1] == b2 && B.Jobs[2] == b3);
            Eq("Invoices: combined total 10,10 + 20,20 + 30,30 = 60,60", 60.60, B.Total);
            True("Invoices: combined state mixed, billed to Firma ApS, due 19-11, overdue on 20-11", B.State == null && B.BilledTo == "Firma ApS"
                 && B.Due == new DateTime(2026, 11, 19) && B.Overdue(now) && !B.Overdue(new DateTime(2026, 11, 19, 23, 0, 0))
                 && B.BilledAt == new DateTime(2026, 11, 19, 9, 0, 0));
            var A = rows[1];
            True("Invoices: single 72,72, Printed, Lene Hansen, due 02-12, not overdue", Math.Abs(A.Total - 72.72) < 1e-9 && A.State == PrintState.Printed
                 && A.BilledTo == "Lene Hansen" && A.Due == new DateTime(2026, 12, 2) && !A.Overdue(now) && !A.Friends);
            var C = rows[2];
            True("Invoices: old code, friends 5,50, Failed, due 01-06 + 14 days = 15-06-2025", C.Friends && Math.Abs(C.Total - 5.5) < 1e-9
                 && C.State == PrintState.Failed && C.Due == new DateTime(2025, 6, 15));
            True("Invoices: unreadable date -> MinValue, age \"\"", rows[3].BilledAt == DateTime.MinValue && History.AgeText(rows[3].BilledAt, now) == "");
            True("Invoices: null / empty list -> no rows", History.Invoices(null, now).Count == 0 && History.Invoices(new List<QuoteRecord> { d }, now).Count == 0);

            Func<string, int, int, string> pick = delegate(string q, int status, int period)
            {
                DateTime f, t;
                History.InvoicePeriod(period, now, out f, out t);
                var hits = new List<string>();
                foreach (var r in rows) if (History.InvoiceMatches(r, q, status, f, t)) hits.Add(r.No);
                return string.Join(",", hits);
            };
            const int all = History.InvoiceStatusAll;
            True("InvoiceMatches: all", pick("", all, History.PeriodAll) == "2026-22222,2026-11111,2025-K7Q4,2026-33333");
            True("InvoiceMatches: search number part / BilledTo / customer / model / letter code", pick("22222", all, 0) == "2026-22222"
                 && pick("firma", all, 0) == "2026-22222" && pick("lene", all, 0) == "2026-11111" && pick("VASE", all, 0) == "2026-22222"
                 && pick("k7q4", all, 0) == "2025-K7Q4" && pick("nothing", all, 0) == "");
            True("InvoiceMatches: status Printed / Failed / Not printed / Mixed", pick("", (int)PrintState.Printed, 0) == "2026-11111"
                 && pick("", (int)PrintState.Failed, 0) == "2025-K7Q4" && pick("", (int)PrintState.NotPrinted, 0) == "2026-33333"
                 && pick("", History.InvoiceStatusMixed, 0) == "2026-22222" && pick("", (int)PrintState.Printing, 0) == "");
            True("InvoiceMatches: this month / last 30 days / this year = B, A (no old, no unreadable)", pick("", all, History.PeriodMonth) == "2026-22222,2026-11111"
                 && pick("", all, History.PeriodLast30) == "2026-22222,2026-11111" && pick("", all, History.PeriodYear) == "2026-22222,2026-11111");
            DateTime pf, pt;
            History.InvoicePeriod(History.PeriodMonth, now, out pf, out pt);
            bool month = pf == new DateTime(2026, 11, 1) && pt == new DateTime(2026, 12, 1);
            History.InvoicePeriod(History.PeriodLast30, now, out pf, out pt);
            bool last30 = pf == new DateTime(2026, 10, 21) && pt == DateTime.MaxValue;
            History.InvoicePeriod(History.PeriodYear, now, out pf, out pt);
            True("InvoicePeriod: month 01-11..01-12, 30 days from 21-10, year 2026", month && last30 && pf == new DateTime(2026, 1, 1) && pt == new DateTime(2027, 1, 1));
            True("InvoiceMatches: null row false", !History.InvoiceMatches(null, "", all, DateTime.MinValue, DateTime.MaxValue));

            // age by calendar days
            bool wasDanish = L.Danish;
            try
            {
                L.Danish = false;
                True("AgeText: same day / later date = today", History.AgeText(now.AddHours(-11), now) == "today" && History.AgeText(now.AddDays(2), now) == "today");
                True("AgeText: 23:59 yesterday at 00:01 = yesterday", History.AgeText(new DateTime(2026, 11, 19, 23, 59, 0), new DateTime(2026, 11, 20, 0, 1, 0)) == "yesterday");
                True("AgeText: 3 / 30 days ago", History.AgeText(now.AddDays(-3), now) == "3 days ago" && History.AgeText(now.AddDays(-30), now) == "30 days ago");
                True("AgeText: 20-10 -> 20-11 = 31 days = 1 month ago", History.AgeText(new DateTime(2026, 10, 20), now) == "1 month ago");
                // month ends: 31-01 -> 02-03 = 28 + 2 = 30 days; -> 03-03 = 31 days, 1 month (3rd < 31st); -> 01-04 = 2 months
                True("AgeText: 31-01 -> 02-03 = 30 days ago", History.AgeText(new DateTime(2026, 1, 31), new DateTime(2026, 3, 2)) == "30 days ago");
                True("AgeText: 31-01 -> 03-03 = 1 month ago", History.AgeText(new DateTime(2026, 1, 31), new DateTime(2026, 3, 3)) == "1 month ago");
                True("AgeText: 31-01 -> 01-04 = 2 months ago", History.AgeText(new DateTime(2026, 1, 31), new DateTime(2026, 4, 1)) == "2 months ago");
                True("AgeText: 1 year (12 months; 23 months) / 2 years", History.AgeText(new DateTime(2025, 11, 20), now) == "1 year ago"
                     && History.AgeText(new DateTime(2024, 11, 21), now) == "1 year ago" && History.AgeText(new DateTime(2024, 11, 20), now) == "2 years ago");
                L.Danish = true;
                True("AgeText DA: i dag / i går / for 3 dage siden", History.AgeText(now, now) == "i dag" && History.AgeText(now.AddDays(-1), now) == "i går"
                     && History.AgeText(now.AddDays(-3), now) == "for 3 dage siden");
                True("AgeText DA: for 1 måned siden / for 2 måneder siden / for 1 år siden / for 2 år siden",
                     History.AgeText(new DateTime(2026, 10, 20), now) == "for 1 måned siden" && History.AgeText(new DateTime(2026, 9, 20), now) == "for 2 måneder siden"
                     && History.AgeText(new DateTime(2025, 11, 20), now) == "for 1 år siden" && History.AgeText(new DateTime(2024, 11, 20), now) == "for 2 år siden");
            }
            finally { L.Danish = wasDanish; }
        }

        // ---------- first-time setup answers (idea 19): SetupAnswers + AppSettings.ApplySetup, shared by desktop and web ----------

        static string Ini(AppSettings s) { return string.Join("\n", s.ToIni()); }

        static SetupAnswers Answers(params string[] models)
        {
            var a = new SetupAnswers();
            a.Models.AddRange(models);
            return a;
        }

        static void SetupApplyTests()
        {
            // 1. Replace [A1M, P1S] on new settings: printer 1 -> A1 mini at its defaults, P1S added as "2"
            var s0 = new AppSettings();
            s0.SetupDone = 0;
            var r = s0.ApplySetup(Answers(Wear.ModelA1Mini, Wear.ModelP1S), SetupPrinterMode.Replace);
            var ps = r.Printers;
            True("Setup 1 Replace two printers", ps.Count == 2 && r.ActivePrinterId == "1");
            True("Setup 1 printer 1 = A1 mini", ps[0].Id == "1" && ps[0].Model == Wear.ModelA1Mini && ps[0].Name == "Bambu Lab A1 mini" && ps[0].AutoWearRate == 1);
            Eq("Setup 1 A1 mini watts 70", 70, ps[0].Watts);
            Eq("Setup 1 A1 mini rate 1,38", 1.38, ps[0].MachineRatePerHour);
            True("Setup 1 printer 2 = P1S", ps[1].Id == "2" && ps[1].Model == Wear.ModelP1S);
            Eq("Setup 1 P1S watts 130", 130, ps[1].Watts);
            Eq("Setup 1 P1S rate 2,54", 2.54, ps[1].MachineRatePerHour);
            True("Setup 1 flat fields = A1 mini", r.PrinterModel == Wear.ModelA1Mini && r.PrinterWatts == 70);
            Eq("Setup 1 SetupDone 1", 1, r.SetupDone);

            // 2. Replace on a custom printer 1: stock printer of the new model (first run, as today)
            var s2 = new AppSettings();
            s2.PrinterWatts = 95; s2.PrinterName = "Workshop"; s2.AutoWearRate = 0; s2.MachineRatePerHour = 3;
            r = s2.ApplySetup(Answers(Wear.ModelP1S), SetupPrinterMode.Replace);
            True("Setup 2 Replace -> stock P1S", r.PrinterModel == Wear.ModelP1S && r.PrinterName == "Bambu Lab P1S" && r.AutoWearRate == 1 && r.PrinterCount == 1);
            Eq("Setup 2 watts 130", 130, r.PrinterWatts);
            Eq("Setup 2 rate 2,54", 2.54, r.MachineRatePerHour);

            // 3. Add: A1 + P1S there, answers A1, P1S, A1M, A1M -> only one A1 mini added, id "3"
            var s3 = new AppSettings();
            s3.AddPrinter("", Wear.ModelP1S);
            string before1 = s3.Printers[0].Name + "|" + s3.Printers[0].Watts;
            r = s3.ApplySetup(Answers(Wear.ModelA1, Wear.ModelP1S, Wear.ModelA1Mini, Wear.ModelA1Mini), SetupPrinterMode.Add);
            ps = r.Printers;
            True("Setup 3 Add only the A1 mini", ps.Count == 3 && ps[2].Id == "3" && ps[2].Model == Wear.ModelA1Mini);
            True("Setup 3 active still 1, printer 1 unchanged", r.ActivePrinterId == "1" && ps[0].Name + "|" + ps[0].Watts == before1);

            // 4. ChangeModel A1 -> P1S, watts/name still the A1's, auto wear on: all follow
            var s4 = new AppSettings();
            r = s4.ApplySetup(Answers(Wear.ModelP1S), SetupPrinterMode.ChangeModel);
            True("Setup 4 ChangeModel name follows", r.PrinterModel == Wear.ModelP1S && r.PrinterName == "Bambu Lab P1S" && r.PrinterCount == 1);
            Eq("Setup 4 watts 100 -> 130", 130, r.PrinterWatts);
            Eq("Setup 4 auto rate -> 2,54", 2.54, r.MachineRatePerHour);

            // 5. ChangeModel A1 -> A1M with own watts, name, rate (auto off): only the model changes
            var s5 = new AppSettings();
            s5.PrinterWatts = 95; s5.PrinterName = "Workshop"; s5.AutoWearRate = 0; s5.MachineRatePerHour = 3;
            s5.Nozzle = "0.6 hardened"; s5.BedType = "Textured PEI Plate";
            r = s5.ApplySetup(Answers(Wear.ModelA1Mini), SetupPrinterMode.ChangeModel);
            True("Setup 5 ChangeModel keeps own values", r.PrinterModel == Wear.ModelA1Mini && r.PrinterName == "Workshop" && r.AutoWearRate == 0);
            Eq("Setup 5 watts 95 kept", 95, r.PrinterWatts);
            Eq("Setup 5 rate 3 kept", 3, r.MachineRatePerHour);
            True("Setup 5 nozzle and plate kept", r.Nozzle == s5.Nozzle && r.BedType == s5.BedType);

            // 6. ChangeModel to the same model: nothing changes
            r = s5.ApplySetup(Answers(Wear.ModelA1), SetupPrinterMode.ChangeModel);
            True("Setup 6 same model unchanged", r.PrinterModel == Wear.ModelA1 && r.PrinterName == "Workshop" && r.PrinterWatts == 95 && r.MachineRatePerHour == 3);

            // 7. no models: printers untouched in every mode
            foreach (SetupPrinterMode m in new[] { SetupPrinterMode.Replace, SetupPrinterMode.Add, SetupPrinterMode.ChangeModel })
            {
                r = s3.ApplySetup(Answers(), m);
                True("Setup 7 no models, " + m + ": printers untouched", r.PrinterCount == 2 && r.PrinterModel == Wear.ModelA1 && r.Printers[1].Model == Wear.ModelP1S);
            }

            // 8. filament price: 250 -> remembered; the same as now -> no new entry
            var a = Answers();
            a.FilamentText = "250";
            r = new AppSettings().ApplySetup(a, SetupPrinterMode.Replace);
            Eq("Setup 8 LastFilamentPrice 250", 250, r.LastFilamentPrice);
            Eq("Setup 8 PriceFor preset 250", 250, r.PriceFor(r.FilamentPreset));
            a.FilamentText = "180";   // = the default LastFilamentPrice, nothing remembered yet
            r = new AppSettings().ApplySetup(a, SetupPrinterMode.Replace);
            True("Setup 8 same price -> no entry", r.FilamentPrices.Count == 0 && r.LastFilamentPrice == 180);

            // 9. power: fixed 1,5; live keeps the fixed price; area DK2 / unknown -> DK1
            a = Answers();
            a.LivePower = false; a.ElecText = "1,5"; a.Area = "DK2";
            r = new AppSettings().ApplySetup(a, SetupPrinterMode.Replace);
            True("Setup 9 fixed: AutoPowerPrice 0, DK2", r.AutoPowerPrice == 0 && r.PriceArea == "DK2");
            Eq("Setup 9 fixed 1,5", 1.5, r.LastElectricityPrice);
            a.LivePower = true; a.ElecText = "9"; a.Area = "XX";
            r = new AppSettings().ApplySetup(a, SetupPrinterMode.Replace);
            True("Setup 9 live: AutoPowerPrice 1, XX -> DK1", r.AutoPowerPrice == 1 && r.PriceArea == "DK1");
            Eq("Setup 9 live keeps 2,00", 2.00, r.LastElectricityPrice);

            // 10. markups and minimum; bad numbers keep the value
            a = Answers();
            a.FriendsText = "30"; a.OthersText = "80,5"; a.MinText = "75";
            r = new AppSettings().ApplySetup(a, SetupPrinterMode.Replace);
            Eq("Setup 10 friends 30", 30, r.FriendsMarkupPct);
            Eq("Setup 10 others 80,5", 80.5, r.OthersMarkupPct);
            Eq("Setup 10 minimum 75", 75, r.MinimumPrice);
            foreach (string bad in new[] { "-5", "abc", "", null, "10000000" })
            {
                a.MinText = bad;
                r = new AppSettings().ApplySetup(a, SetupPrinterMode.Replace);
                Eq("Setup 10 minimum '" + (bad ?? "null") + "' keeps 50", 50, r.MinimumPrice);
            }
            a.MinText = "9999999";
            r = new AppSettings().ApplySetup(a, SetupPrinterMode.Replace);
            True("Setup 10 minimum 9999999 taken", r.MinimumPrice == 9999999);
            double v;
            True("Setup 10 ReadNumber rule", SetupAnswers.ReadNumber("1.234,5", out v) && v == 1234.5 && !SetupAnswers.ReadNumber("1e7", out v));

            // 11. name on quotes trimmed; "" and null -> ""
            a = Answers();
            a.QuoteName = "  Lene Hansen  ";
            True("Setup 11 name trimmed", new AppSettings().ApplySetup(a, SetupPrinterMode.Replace).QuoteName == "Lene Hansen");
            a.QuoteName = "";
            True("Setup 11 empty name", new AppSettings { QuoteName = "Gettic" }.ApplySetup(a, SetupPrinterMode.Replace).QuoteName == "");
            a.QuoteName = null;
            True("Setup 11 null name", new AppSettings().ApplySetup(a, SetupPrinterMode.Replace).QuoteName == "");

            // 12. language: "da" set; null keeps "" (auto)
            a = Answers();
            a.Language = "da";
            True("Setup 12 language da", new AppSettings().ApplySetup(a, SetupPrinterMode.Replace).Language == "da");
            a.Language = null;
            True("Setup 12 language null keeps auto", new AppSettings().ApplySetup(a, SetupPrinterMode.Replace).Language == "");

            // 13. slicer path: null keeps, "" clears
            a = Answers();
            True("Setup 13 slicer null keeps", new AppSettings { SlicerPath = @"C:\Bambu\bambu-studio.exe" }.ApplySetup(a, SetupPrinterMode.Replace).SlicerPath == @"C:\Bambu\bambu-studio.exe");
            a.SlicerPath = "";
            True("Setup 13 slicer '' clears", new AppSettings { SlicerPath = @"C:\Bambu\bambu-studio.exe" }.ApplySetup(a, SetupPrinterMode.Replace).SlicerPath == "");

            // 14. the start object is not changed; SetupDone 0 -> 1
            var s14 = new AppSettings();
            s14.SetupDone = 0;
            string ini14 = Ini(s14);
            a = SetupAnswers.From(s14, SetupPrinterMode.Replace);
            a.FilamentText = "300"; a.FriendsText = "40"; a.Language = "en"; a.Models = new List<string> { Wear.ModelP1S, Wear.ModelA1 };
            r = s14.ApplySetup(a, SetupPrinterMode.Replace);
            True("Setup 14 start unchanged", Ini(s14) == ini14 && s14.PrinterCount == 1);
            True("Setup 14 SetupDone 0 -> 1", s14.SetupDone == 0 && r.SetupDone == 1);

            // 15. From / CopyStep
            var f = SetupAnswers.From(new AppSettings(), SetupPrinterMode.Replace);
            True("Setup 15 From models [A1]", J(f.Models) == Wear.ModelA1);
            True("Setup 15 From texts", f.FilamentText == "180" && f.ElecText == "2" && f.FriendsText == "25" && f.OthersText == "75" && f.MinText == "50"
                                        && f.LivePower && f.Area == "DK1" && f.QuoteName == "" && f.Language == "" && f.SlicerPath == "");
            var s15 = new AppSettings();
            s15.AddPrinter("", Wear.ModelP1S);
            s15.AddPrinter("Second A1", Wear.ModelA1);
            True("Setup 15 From Add = models in order, once", J(SetupAnswers.From(s15, SetupPrinterMode.Add).Models) == Wear.ModelA1 + "|" + Wear.ModelP1S);
            True("Setup 15 From ChangeModel = active model", J(SetupAnswers.From(s15, SetupPrinterMode.ChangeModel).Models) == Wear.ModelA1);
            var c = f.Clone();
            c.FilamentText = "1"; c.ElecText = "1"; c.LivePower = false; c.Area = "DK2"; c.FriendsText = "1"; c.Language = "da";
            c.Models.Add(Wear.ModelP1S);
            True("Setup 15 Clone has its own Models", f.Models.Count == 1);
            c.CopyStep(SetupAnswers.StepCosts, f);
            True("Setup 15 CopyStep costs", c.FilamentText == "180" && c.ElecText == "2" && c.LivePower && c.Area == "DK1");
            True("Setup 15 CopyStep leaves other steps", c.FriendsText == "1" && c.Language == "da" && c.Models.Count == 2);
            c.CopyStep(SetupAnswers.StepPrinter, f);
            c.CopyStep(SetupAnswers.StepLanguage, f);
            c.CopyStep(SetupAnswers.StepPrices, f);
            True("Setup 15 CopyStep printer/language/prices", J(c.Models) == Wear.ModelA1 && c.Language == "" && c.FriendsText == "25");

            // 16. applying the opening answers changes nothing (every step skipped)
            var s16 = new AppSettings();
            s16.FriendsMarkupPct = 33; s16.OthersMarkupPct = 90.5; s16.MinimumPrice = 0; s16.LastElectricityPrice = 2.75;
            s16.AutoPowerPrice = 0; s16.PriceArea = "DK2"; s16.QuoteName = "Lene Hansen"; s16.Language = "da";
            s16.RememberPrice(s16.FilamentPreset, 215.5); s16.LastFilamentPrice = 215.5;
            s16.SlicerPath = @"D:\Bambu\bambu-studio.exe";
            s16.AddPrinter("", Wear.ModelA1Mini);
            string ini16 = Ini(s16);
            foreach (SetupPrinterMode m in new[] { SetupPrinterMode.Replace, SetupPrinterMode.Add, SetupPrinterMode.ChangeModel })
                True("Setup 16 skip all, " + m + ": unchanged", Ini(s16.ApplySetup(SetupAnswers.From(s16, m), m)) == ini16);
            var s16b = s16.Clone();
            s16b.PrinterWatts = 95; s16b.PrinterName = "Workshop"; s16b.AutoWearRate = 0; s16b.MachineRatePerHour = 3;
            string ini16b = Ini(s16b);
            True("Setup 16 skip all, Add/ChangeModel keep a custom printer",
                 Ini(s16b.ApplySetup(SetupAnswers.From(s16b, SetupPrinterMode.Add), SetupPrinterMode.Add)) == ini16b
                 && Ini(s16b.ApplySetup(SetupAnswers.From(s16b, SetupPrinterMode.ChangeModel), SetupPrinterMode.ChangeModel)) == ini16b);

            // 17. example price: P1S, 200 kr/kg, fixed 2 kr/kWh, 25 % / 75 %, minimum 0, 8 h / 150 g, wear 2,54 kr/h
            // material 0,150 x 200 = 30,00; power 0,130 x 8 x 2 = 2,08; base 32,08
            // friends 32,08 x 1,25 = 40,10 + 8 x 2,54 = 20,32 -> 60,42; customers 32,08 x 1,75 = 56,14 + 20,32 -> 76,46
            a = Answers(Wear.ModelP1S);
            a.FilamentText = "200"; a.LivePower = false; a.ElecText = "2"; a.FriendsText = "25"; a.OthersText = "75"; a.MinText = "0";
            r = new AppSettings().ApplySetup(a, SetupPrinterMode.Replace);
            var res = Pricing.Calculate(new QuoteInput { Hours = 8, Minutes = 0, FilamentGrams = 150, FilamentDkkPerKg = r.LastFilamentPrice, ElectricityDkkPerKwh = r.LastElectricityPrice }, r);
            Eq("Setup 17 friends 60,42", 60.42, res.FriendsPrice);
            Eq("Setup 17 customers 76,46", 76.46, res.OthersPrice);
        }

        // ---------- record pictures (issue #4): History picture helpers, files, backups, Backup.FitForUpload ----------

        // "picture" bytes for the size / backup tests: PNG magic + random rest (incompressible, like a real PNG's data)
        static byte[] FakePng(int length, int seed)
        {
            var b = new byte[length];
            new Random(seed).NextBytes(b);
            byte[] magic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Buffer.BlockCopy(magic, 0, b, 0, magic.Length);
            return b;
        }

        static System.Drawing.Bitmap TestBitmap(int w, int h, bool transparent, bool noise)
        {
            var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rnd = new Random(7);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    System.Drawing.Color c;
                    if (noise) c = System.Drawing.Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256), rnd.Next(256));
                    else if (transparent && (x < w / 4 || x >= w * 3 / 4)) c = System.Drawing.Color.Transparent;
                    else c = System.Drawing.Color.FromArgb(255, 40 + x % 50, 120, 200 - y % 60);
                    bmp.SetPixel(x, y, c);
                }
            return bmp;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static void PictureTests()
        {
            // 1. ids
            True("SafePictureId FromQuote Id", History.SafePictureId(HistRecord(new DateTime(2026, 11, 12, 9, 0, 0)).Id));
            True("SafePictureId a-Z_09-", History.SafePictureId("aZ_09-x"));
            True("SafePictureId 64 chars ok, 65 refused", History.SafePictureId(new string('a', 64)) && !History.SafePictureId(new string('a', 65)));
            True("SafePictureId refuses empty / null / ..\\x / a/b / a b / a.b",
                 !History.SafePictureId("") && !History.SafePictureId(null) && !History.SafePictureId(@"..\x")
                 && !History.SafePictureId("a/b") && !History.SafePictureId("a b") && !History.SafePictureId("a.b"));

            // 2. types and data URLs
            byte[] png = FakePng(2000, 1);
            byte[] jpg = FakePng(1500, 2); jpg[0] = 0xFF; jpg[1] = 0xD8; jpg[2] = 0xFF; jpg[3] = 0xE0;
            byte[] webp = FakePng(1200, 3);
            byte[] riff = System.Text.Encoding.ASCII.GetBytes("RIFF....WEBP");
            Buffer.BlockCopy(riff, 0, webp, 0, riff.Length);
            True("PictureMime png / jpeg / webp", History.PictureMime(png) == "image/png" && History.PictureMime(jpg) == "image/jpeg"
                 && History.PictureMime(webp) == "image/webp");
            True("PictureMime other / short / null -> null", History.PictureMime(new byte[20]) == null
                 && History.PictureMime(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) == null && History.PictureMime(null) == null);
            string url = History.PictureDataUrl(png);
            True("PictureDataUrl png prefix", url.StartsWith("data:image/png;base64,"));
            True("data URL round trip png / jpeg / webp", SameBytes(History.PictureFromDataUrl(url), png)
                 && SameBytes(History.PictureFromDataUrl(History.PictureDataUrl(jpg)), jpg)
                 && SameBytes(History.PictureFromDataUrl(History.PictureDataUrl(webp)), webp));
            True("PictureFromDataUrl bad prefix / not base64 / bad base64 / gif -> null",
                 History.PictureFromDataUrl("image/png;base64," + Convert.ToBase64String(png)) == null
                 && History.PictureFromDataUrl("data:image/png," + Convert.ToBase64String(png)) == null
                 && History.PictureFromDataUrl("data:image/png;base64,@@@") == null
                 && History.PictureFromDataUrl("data:image/gif;base64," + Convert.ToBase64String(png)) == null
                 && History.PictureFromDataUrl(null) == null);
            True("PictureFromDataUrl type and bytes must agree", History.PictureFromDataUrl("data:image/jpeg;base64," + Convert.ToBase64String(png)) == null);
            byte[] tooBig = FakePng(History.MaxPictureBytes + 1, 4);
            True("MaxPictureBytes 96 KB = 98304", History.MaxPictureBytes == 98304);
            True("picture of exactly 96 KB ok, 1 byte more refused", History.PictureDataUrl(FakePng(History.MaxPictureBytes, 5)) != null
                 && History.PictureDataUrl(tooBig) == null
                 && History.PictureFromDataUrl("data:image/png;base64," + Convert.ToBase64String(tooBig)) == null);

            // 3. EncodePicture: 256 px, PNG with transparency, JPEG without, too big -> null
            using (var t = TestBitmap(512, 512, true, false))
            {
                byte[] e = History.EncodePicture(t);
                True("EncodePicture transparent -> PNG <= 96 KB", e != null && History.PictureMime(e) == "image/png" && e.Length <= History.MaxPictureBytes);
                using (var back = History.PictureImage(e))
                    True("EncodePicture 512x512 -> 256x256", back != null && back.Width == 256 && back.Height == 256);
            }
            using (var o = TestBitmap(400, 300, false, false))
            {
                byte[] e = History.EncodePicture(o);
                True("EncodePicture opaque -> JPEG", e != null && History.PictureMime(e) == "image/jpeg");
                // 400 x 300: k = 256 / 400 = 0,64 -> 256 x 192
                using (var back = History.PictureImage(e))
                    True("EncodePicture 400x300 -> 256x192", back != null && back.Width == 256 && back.Height == 192);
            }
            using (var small = TestBitmap(100, 50, false, false))
            using (var back = History.PictureImage(History.EncodePicture(small)))
                True("EncodePicture small picture not enlarged (100x50)", back != null && back.Width == 100 && back.Height == 50);
            using (var n = TestBitmap(256, 256, false, true))
            {
                byte[] at256 = History.EncodePictureAt(n, 256);
                Console.WriteLine("INFO noise 256 px PNG " + (at256 == null ? -1 : at256.Length) + " bytes");
                True("noise at 256 px is over 96 KB", at256 != null && at256.Length > History.MaxPictureBytes);
                True("EncodePicture noise (over at 256 and 192) -> null", History.EncodePicture(n) == null);
            }
            True("EncodePicture null -> null", History.EncodePicture(null) == null);
            True("PictureImage webp / junk / null -> null", History.PictureImage(webp) == null && History.PictureImage(png) == null
                 && History.PictureImage(null) == null);

            // 4. PicturesForBackup: newest first (by SavedAt, not list order), budget, skip what doesn't fit
            var a = BRec("A", "2026-11-01 10:00:00"); var b = BRec("B", "2026-11-03 10:00:00");
            var c = BRec("C", "2026-11-02 10:00:00"); var d = BRec("D", "2026-10-01 10:00:00");
            var bad = BRec("x/y", "2026-11-04 10:00:00");
            var pics = new Dictionary<string, byte[]> { { "A", FakePng(3000, 11) }, { "B", FakePng(3000, 12) }, { "C", FakePng(9000, 13) },
                                                        { "D", FakePng(3000, 14) }, { "x/y", FakePng(3000, 15) } };
            Func<string, byte[]> load = delegate(string id) { byte[] v; return pics.TryGetValue(id, out v) ? v : null; };
            var list = new List<QuoteRecord> { a, b, c, d, bad };
            // data URL of 3000 bytes = 22 + 4000 = 4022 chars, 9000 bytes = 22 + 12000 = 12022
            var all = History.PicturesForBackup(list, load, long.MaxValue);
            True("PicturesForBackup newest first B C A D, unsafe id left out", all.Count == 4 && all[0].Id == "B" && all[1].Id == "C"
                 && all[2].Id == "A" && all[3].Id == "D");
            True("data URL length 3000 bytes = 4022 chars", all[0].Data.Length == 4022 && all[1].Data.Length == 12022);
            // budget 10 000: B 4022, C 12022 too big -> skipped, A 4022 (8044), D 4022 would be 12066 -> skipped
            var some = History.PicturesForBackup(list, load, 10000);
            True("PicturesForBackup budget 10 000 -> B, A", J(some.ConvertAll(p => p.Id)) == "B|A");
            True("PicturesForBackup no loader / budget 0 -> none", History.PicturesForBackup(list, null, 1000).Count == 0
                 && History.PicturesForBackup(list, load, 0).Count == 0);

            // 5. backup text with pictures, both ways
            var when = new DateTime(2026, 11, 12, 10, 0, 0);
            var recs = new List<QuoteRecord> { b, c, a };
            string plain = History.ToBackup(recs, new AppSettings(), "1.16.1", when);
            True("ToBackup with no / only invalid pictures = the old text", History.ToBackup(recs, new AppSettings(), "1.16.1", when, null) == plain
                 && History.ToBackup(recs, new AppSettings(), "1.16.1", when, new List<BackupPicture>()) == plain
                 && History.ToBackup(recs, new AppSettings(), "1.16.1", when,
                        new List<BackupPicture> { new BackupPicture { Id = "A", Data = "data:image/png;base64,@@" }, new BackupPicture { Id = "Z", Data = url } }) == plain);
            True("old text has no Pictures", !plain.Contains("\"Pictures\""));
            var given = new List<BackupPicture> {
                new BackupPicture { Id = "B", Data = History.PictureDataUrl(pics["B"]) },
                new BackupPicture { Id = "B", Data = History.PictureDataUrl(pics["A"]) },     // second for B: dropped
                new BackupPicture { Id = "Z", Data = History.PictureDataUrl(pics["A"]) },     // no such record: dropped
                new BackupPicture { Id = "C", Data = "data:image/png;base64,@@" },             // bad: dropped
                new BackupPicture { Id = "A", Data = History.PictureDataUrl(webp) },          // website WebP: kept
                null };
            string withPics = History.ToBackup(recs, new AppSettings(), "1.16.1", when, given);
            True("ToBackup with pictures: Pictures + Version 1", withPics.Contains("\"Pictures\":[") && withPics.Contains("\"Format\":\"PrintPriceBackup\",\"Version\":1,"));
            BackupData bd;
            True("ReadBackup with pictures OK", History.ReadBackup(withPics, out bd) == null && bd.Quotes.Count == 3);
            True("ReadBackup pictures = B, A (bytes as written)", bd.Pictures.Count == 2 && SameBytes(bd.Pictures["B"], pics["B"])
                 && SameBytes(bd.Pictures["A"], webp));
            True("ReadBackup old backup -> 0 pictures", History.ReadBackup(plain, out bd) == null && bd.Pictures.Count == 0 && bd.Quotes.Count == 3);
            var oldReader = Json.Deserialize<BackupFile>(withPics);   // = an older app's class without Pictures
            True("old app reads a backup with pictures (all quotes)", oldReader != null && oldReader.Version == 1 && oldReader.History.Quotes.Count == 3);
            True("plain history.json -> 0 pictures", History.ReadBackup(History.ToJson(recs), out bd) == null && bd.Pictures.Count == 0);
            // hand-made backup: picture for a record not in it, bad data, unsafe id -> all dropped, restore still fine
            string odd = withPics.Replace("\"Id\":\"A\",\"Data\"", "\"Id\":\"Q\",\"Data\"");
            True("picture of an unknown record dropped", History.ReadBackup(odd, out bd) == null && bd.Pictures.Count == 1 && bd.Pictures.ContainsKey("B"));
            // broken "Pictures" parts (tester): the records still restore, only the pictures are dropped
            True("backup text without pictures ends with }", plain.EndsWith("}"));
            string head = plain.Substring(0, plain.Length - 1);   // the same 3 records, then our own "Pictures" part
            string goodB = "{\"Id\":\"B\",\"Data\":\"" + History.PictureDataUrl(pics["B"]) + "\"}";
            string[,] shapes = {
                { "Pictures \"x\"", "\"x\"" },
                { "Pictures [1,2]", "[1,2]" },
                { "Pictures {}", "{\"Id\":\"B\"}" },
                { "Pictures null", "null" },
                { "Data {...}", "[{\"Id\":\"B\",\"Data\":{\"a\":1}}]" },
                { "Data [...]", "[{\"Id\":\"B\",\"Data\":[1,2]}]" },
                { "Id 5", "[{\"Id\":5,\"Data\":\"x\"}]" },
                { "list of strings / null / arrays", "[\"x\",null,[1],true]" } };
            for (int i = 0; i < shapes.GetLength(0); i++)
            {
                string t = head + ",\"Pictures\":" + shapes[i, 1] + "}";
                True("broken " + shapes[i, 0] + ": 3 quotes restore, 0 pictures", History.ReadBackup(t, out bd) == null
                     && bd.Quotes.Count == 3 && bd.Pictures.Count == 0);
            }
            string mixed = head + ",\"Pictures\":[1,{\"Id\":\"A\",\"Data\":{}},\"s\"," + goodB + ",{\"Id\":\"C\",\"Data\":[1]}]}";
            True("mixed Pictures list: the good B kept, the rest dropped", History.ReadBackup(mixed, out bd) == null
                 && bd.Quotes.Count == 3 && bd.Pictures.Count == 1 && SameBytes(bd.Pictures["B"], pics["B"]));

            // 6. Combine with pictures
            var cur = new List<QuoteRecord> { BRec("A", "2026-11-01 10:00:00"), BRec("B", "2026-11-03 10:00:00") };
            var inc = new BackupData { Quotes = new List<QuoteRecord> { BRec("A", "2026-11-01 10:00:00"), BRec("E", "2026-11-05 10:00:00") } };
            inc.Pictures["A"] = pics["A"]; inc.Pictures["E"] = pics["D"];
            Func<string, bool> hereA = delegate(string id) { return id == "A"; };
            var m = History.Combine(cur, new AppSettings(), inc, false, false, hereA);
            True("Combine merge: added E brings its picture, A keeps ours", m.Pictures.Count == 1 && m.Pictures.ContainsKey("E"));
            m = History.Combine(cur, new AppSettings(), inc, false, false, delegate(string id) { return false; });
            True("Combine merge: A has none here -> backup's A too", m.Pictures.Count == 2 && SameBytes(m.Pictures["A"], pics["A"]));
            m = History.Combine(cur, new AppSettings(), inc, true, false, hereA);
            True("Combine replace: the backup's pictures (A and E)", m.Pictures.Count == 2 && m.Pictures.ContainsKey("A"));
            True("Combine old overload: no pictures here", History.Combine(cur, new AppSettings(), inc, false, false).Pictures.Count == 2);
            True("Combine backup without pictures -> none", History.Combine(cur, new AppSettings(), new BackupData { Quotes = inc.Quotes }, false, false).Pictures.Count == 0);

            // 7. FitForUpload: 60 jobs x 20 KB incompressible pictures, limit 512 KB
            var many = new List<QuoteRecord>();
            var manyPics = new Dictionary<string, byte[]>();
            for (int i = 0; i < 60; i++)
            {
                var r = BRec("P" + i.ToString("00"), new DateTime(2026, 1, 1, 8, 0, 0).AddHours(i).ToString(History.TimeFormat, System.Globalization.CultureInfo.InvariantCulture));
                many.Insert(0, r);   // newest first, as the history
                manyPics[r.Id] = FakePng(20 * 1024, 100 + i);
            }
            var cands = History.PicturesForBackup(many, delegate(string id) { return manyPics[id]; }, History.FilePictureBudget);
            True("60 pictures as candidates, newest P59 first", cands.Count == 60 && cands[0].Id == "P59");
            string code = Backup.NewCode();
            string fit = Backup.FitForUpload(many, new AppSettings(), "1.16.1", when, cands, OnlineBackup.MaxBlob);
            byte[] blob = Backup.Pack(fit, code);
            True("PackedSize = Pack length", Backup.PackedSize(fit) == blob.Length
                 && Backup.PackedSize(plain) == Backup.Pack(plain, code).Length);
            True("FitForUpload under 512 KB", blob.Length <= OnlineBackup.MaxBlob);
            BackupData fd;
            True("FitForUpload reads back", History.ReadBackup(Backup.Unpack(blob, code), out fd) == null && fd.Quotes.Count == 60);
            bool newest = fd.Pictures.Count > 0;
            for (int i = 0; i < fd.Pictures.Count; i++) if (!fd.Pictures.ContainsKey("P" + (59 - i).ToString("00"))) newest = false;
            Console.WriteLine("INFO FitForUpload: " + fd.Pictures.Count + " of 60 pictures, blob " + blob.Length + " bytes");
            // room ~ 512 - ~10 KB (60 records) - 16 = ~486 KB -> ~23 pictures of 20 KB
            True("FitForUpload keeps the newest pictures (20-25 of 60)", newest && fd.Pictures.Count >= 20 && fd.Pictures.Count <= 25);
            True("FitForUpload no pictures = ToBackup", Backup.FitForUpload(many, null, "1", when, null, OnlineBackup.MaxBlob) == History.ToBackup(many, null, "1", when));
            True("FitForUpload: too small a limit -> backup without pictures", Backup.FitForUpload(many, null, "1", when, cands, 1000) == History.ToBackup(many, null, "1", when));

            // 8. files: only in bin\test-pictures (test mode), never %APPDATA%
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-pictures");
            try
            {
                True("Pictures UseFolder test-pictures", AppSettings.UseFolder(dir));
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
                System.IO.Directory.CreateDirectory(dir);
                History.ResetBlock();
                True("PictureDir in the test folder", string.Equals(History.PictureDir, System.IO.Path.Combine(dir, "pictures"), StringComparison.OrdinalIgnoreCase));
                True("LoadPicture none -> null", History.LoadPicture("A") == null && !History.HasPicture("A"));
                True("SavePicture jpeg", History.SavePicture("A", jpg) && SameBytes(History.LoadPicture("A"), jpg)
                     && System.IO.File.Exists(System.IO.Path.Combine(History.PictureDir, "A.jpg")));
                True("SavePicture png replaces the jpeg", History.SavePicture("A", png) && SameBytes(History.LoadPicture("A"), png)
                     && !System.IO.File.Exists(System.IO.Path.Combine(History.PictureDir, "A.jpg")));
                True("SavePicture unsafe id -> false, nothing written", !History.SavePicture(@"..\evil", png)
                     && !System.IO.File.Exists(System.IO.Path.Combine(dir, "evil.png")));
                True("SavePicture too big -> removed (= no picture)", History.SavePicture("A", tooBig) && History.LoadPicture("A") == null);
                History.SavePicture("A", png);
                True("SavePicture null deletes", History.SavePicture("A", null) && !History.HasPicture("A"));
                History.SavePicture("A", png); History.SavePicture("W", webp);
                True("webp kept as .webp", SameBytes(History.LoadPicture("W"), webp) && System.IO.File.Exists(System.IO.Path.Combine(History.PictureDir, "W.webp")));
                True("CopyPicture A -> R", History.CopyPicture("A", "R") && SameBytes(History.LoadPicture("R"), png));
                True("CopyPicture from none -> false", !History.CopyPicture("none", "R2") && !History.HasPicture("R2"));
                True("DeletePicture", History.DeletePicture("R") && !History.HasPicture("R"));
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(History.PictureDir, "big.png"), tooBig);
                True("LoadPicture over 96 KB file -> null", History.LoadPicture("big") == null);
                True("PictureIds", J(SortedIds(History.PictureIds())) == "A|W|big");
                // CleanPictures: keeps the list's and the newest safety copy's, removes the rest; never on empty list / blocked
                History.SavePicture("S", png); History.SavePicture("Gone", png);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "history.before-restore-20261112-100000.json"),
                    History.ToJson(new List<QuoteRecord> { BRec("S", "2026-11-01 10:00:00") }));
                True("CleanPictures empty list -> 0 removed", History.CleanPictures(new List<QuoteRecord>()) == 0 && History.HasPicture("Gone"));
                History.SetBlock();
                True("CleanPictures while blocked -> 0", History.CleanPictures(new List<QuoteRecord> { BRec("A", "2026-11-01 10:00:00") }) == 0);
                History.ResetBlock();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(History.PictureDir, "X.png.tmp"), png);
                int removed = History.CleanPictures(new List<QuoteRecord> { BRec("A", "2026-11-01 10:00:00") });
                True("CleanPictures removed W, big, Gone; kept A and S (safety copy); .tmp gone", removed == 3
                     && J(SortedIds(History.PictureIds())) == "A|S" && !System.IO.File.Exists(System.IO.Path.Combine(History.PictureDir, "X.png.tmp")));
                True("OrphanPictures", J(History.OrphanPictures(new[] { "A", "B", null, "C" }, new List<QuoteRecord> { BRec("A", "2026-11-01 10:00:00") },
                     new[] { BRec("C", "2026-11-01 10:00:00") })) == "B");

                // Restore writes the pictures (merge: ours kept)
                var here = new List<QuoteRecord> { BRec("A", "2026-11-01 10:00:00") };
                True("Pictures test history saved", History.Save(here) == null);
                var rb = new BackupData { Quotes = new List<QuoteRecord> { BRec("A", "2026-11-01 10:00:00"), BRec("E", "2026-11-05 10:00:00") } };
                rb.Pictures["A"] = jpg; rb.Pictures["E"] = webp;
                RestoreResult rr;
                True("Restore with pictures OK", History.Restore(here, new AppSettings(), rb, false, false, when, out rr) == null);
                True("Restore merge: E written, A kept ours", SameBytes(History.LoadPicture("E"), webp) && SameBytes(History.LoadPicture("A"), png));
                True("BackupPictures = this PC's, newest first (E, A)", J(History.BackupPictures(rr.Quotes).ConvertAll(p => p.Id)) == "E|A");
                // History.Add of the same quote again keeps its Id, so its picture stays matched
                var l2 = new List<QuoteRecord>();
                var first = HistRecord(new DateTime(2026, 11, 12, 9, 0, 0));
                History.Add(l2, first);
                History.Add(l2, HistRecord(new DateTime(2026, 11, 12, 9, 3, 0)));
                True("Add same quote keeps the Id (picture key)", l2.Count == 1 && l2[0].Id == first.Id);
            }
            finally
            {
                History.ResetBlock();
                AppSettings.ClearTestFolder();
            }
        }

        // ---------- idea 22 (issue #5): delete several, undo, Clear all, list pictures, HistoryView ----------

        static List<string> IdsOf(List<QuoteRecord> l)
        {
            var res = new List<string>();
            foreach (var r in l) res.Add(r == null ? "~" : r.Id);
            return res;
        }

        static void HistoryDeleteTests()
        {
            // 1. RemoveMany / PutBack (pure)
            var l = new List<QuoteRecord> { BRec("A", "2026-11-05 10:00:00"), BRec("B", "2026-11-04 10:00:00"), BRec("C", "2026-11-03 10:00:00"),
                                            BRec("D", "2026-11-02 10:00:00"), BRec("E", "2026-11-01 10:00:00") };
            var rm = History.RemoveMany(l, new[] { "B", "D", "X", null, "", "B" });
            True("RemoveMany: B, D gone, rest in order", J(IdsOf(l)) == "A|C|E");
            True("RemoveMany: removed B, D with indexes 1, 3", J(rm.Ids()) == "B|D" && rm.Indexes[0] == 1 && rm.Indexes[1] == 3 && rm.Count == 2);
            True("PutBack: old order back", History.PutBack(l, rm) == 2 && J(IdsOf(l)) == "A|B|C|D|E");
            True("PutBack again: already there -> 0", History.PutBack(l, rm) == 0 && l.Count == 5);
            var rmAE = History.RemoveMany(l, new[] { "E", "A" });
            True("RemoveMany first + last", J(IdsOf(l)) == "B|C|D" && J(rmAE.Ids()) == "A|E");
            l.Insert(0, BRec("A", "2026-11-05 10:00:00"));   // an A came back some other way
            True("PutBack skips an Id already there, appends past the end", History.PutBack(l, rmAE) == 1 && J(IdsOf(l)) == "A|B|C|D|E");
            True("RemoveMany null ids / no match / null list", History.RemoveMany(l, null).Count == 0
                 && History.RemoveMany(l, new[] { "Q" }).Count == 0 && l.Count == 5 && History.RemoveMany(null, new[] { "A" }).Count == 0);
            True("PutBack null -> 0", History.PutBack(l, null) == 0 && History.PutBack(null, rm) == 0);

            // 2. debt and the summary. Failed F (2026-11-01): material 20 + power 10, no wear (0 h) -> FailedCost 30.
            // Paying quote P (2026-11-05, customer, PaybackCost 10) -> debt 30 - 10 = 20.
            Func<List<QuoteRecord>> debtList = delegate
            {
                var f = BRec("F", "2026-11-01 10:00:00");
                f.Hours = 0; f.Minutes = 0; f.MaterialCost = 20; f.PowerCost = 10; f.Failed = true; f.FailedAt = "2026-11-01 12:00:00"; f.PaidBack = 0;
                var p = BRec("P", "2026-11-05 10:00:00");
                p.Failed = false; p.FailedAt = ""; p.PaybackCost = 10; p.For = History.ForCustomer; p.FailedPaybackWho = 2; p.InvoiceNo = "2026-11234";
                var o = BRec("O", "2026-11-06 10:00:00");
                o.Failed = false; o.PaybackCost = 0;
                return new List<QuoteRecord> { o, p, f };
            };
            var dl = debtList();
            True("debt 30 - 10 = 20", Math.Abs(History.FailedDebt(dl, null) - 20) < 1e-9);
            var sp = History.SummarizeDelete(dl, new[] { "P", "nope" });
            True("summary delete P: 1, invoice 1, paying 1, debt 20 -> 30", sp.Count == 1 && sp.Invoices == 1 && sp.PayingBack == 1
                 && sp.Failed == 0 && sp.FailedOpen == 0 && Math.Abs(sp.DebtBefore - 20) < 1e-9 && Math.Abs(sp.DebtAfter - 30) < 1e-9);
            var sf = History.SummarizeDelete(dl, new[] { "F" });
            True("summary delete F: failed open 1 (20 left), debt 20 -> 0", sf.Count == 1 && sf.Failed == 1 && sf.FailedOpen == 1
                 && Math.Abs(sf.FailedOpenAmount - 20) < 1e-9 && Math.Abs(sf.DebtAfter) < 1e-9);
            True("FailedNotEarnedBack(F, P, O) = 1", History.FailedNotEarnedBack(dl, new[] { "F", "P", "O" }) == 1);
            True("SummarizeClear: 3, debt after 0", History.SummarizeClear(dl).Count == 3 && History.SummarizeClear(dl).DebtAfter == 0
                 && History.SummarizeClear(dl).FailedOpen == 1);
            True("summary of nothing / null", History.SummarizeDelete(dl, new string[0]).Count == 0
                 && Math.Abs(History.SummarizeDelete(dl, new string[0]).DebtAfter - 20) < 1e-9 && History.SummarizeDelete(null, null).Count == 0);
            // RemoveMany = Remove one by one (no PaidBack moved): deleting P brings the debt back to 30
            var one = debtList(); History.Remove(one, "P");
            var many = debtList(); History.RemoveMany(many, new[] { "P" });
            True("RemoveMany = Remove: debt back to 30", J(IdsOf(one)) == J(IdsOf(many)) && Math.Abs(History.FailedDebt(many, null) - 30) < 1e-9
                 && many[1].PaidBack == 0);
            var fl = debtList();
            var srm = History.RemoveMany(fl, new[] { "F" });
            True("delete F: debt 0, PutBack: 20 again", Math.Abs(History.FailedDebt(fl, null)) < 1e-9
                 && History.PutBack(fl, srm) == 1 && Math.Abs(History.FailedDebt(fl, null) - 20) < 1e-9);

            // 3. HistoryView setting (INI "HistoryView", default 0 list; 1 medium, 2 large; other values -> 0)
            var hs = new AppSettings();
            True("HistoryView default 0 (list)", hs.HistoryView == AppSettings.HistoryViewList && AppSettings.HistoryViewList == 0
                 && AppSettings.HistoryViewMedium == 1 && AppSettings.HistoryViewLarge == 2);
            hs.HistoryView = 2;
            True("HistoryView 2 saved and read back", AppSettings.FromIni(hs.ToIni()).HistoryView == 2
                 && Array.IndexOf(hs.ToIni(), "HistoryView=2") >= 0);
            True("HistoryView 1 read", AppSettings.FromIni(new[] { "HistoryView=1" }).HistoryView == 1);
            True("HistoryView 5 / -1 / 1,5 -> 0", AppSettings.FromIni(new[] { "HistoryView=5" }).HistoryView == 0
                 && AppSettings.FromIni(new[] { "HistoryView=-1" }).HistoryView == 0 && AppSettings.FromIni(new[] { "HistoryView=1.5" }).HistoryView == 0);

            // 4. PictureThumbnail (desktop)
            byte[] real;
            using (var bmp = new System.Drawing.Bitmap(256, 128))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp)) g.Clear(System.Drawing.Color.SteelBlue);
                real = History.EncodePicture(bmp);
            }
            using (var th = History.PictureThumbnail(real, 48))
                True("PictureThumbnail 256x128 -> 48x24", th != null && th.Width == 48 && th.Height == 24);
            using (var th = History.PictureThumbnail(real, 1000))
                True("PictureThumbnail never enlarges", th != null && th.Width == 256 && th.Height == 128);
            byte[] webp = FakePng(1200, 3);
            byte[] riff = System.Text.Encoding.ASCII.GetBytes("RIFF....WEBP");
            Buffer.BlockCopy(riff, 0, webp, 0, riff.Length);
            True("PictureThumbnail webp / junk / null / size 0 -> null", History.PictureThumbnail(webp, 48) == null
                 && History.PictureThumbnail(FakePng(500, 9), 48) == null && History.PictureThumbnail(null, 48) == null
                 && History.PictureThumbnail(real, 0) == null);

            // 5. files: only in bin\test-delete (test mode), never %APPDATA%
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-delete");
            byte[] png = FakePng(2000, 11), png2 = FakePng(1800, 12);
            DateTime when = new DateTime(2026, 11, 13, 10, 0, 0);
            try
            {
                True("Delete UseFolder test-delete", AppSettings.UseFolder(dir));
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
                System.IO.Directory.CreateDirectory(dir);
                History.ResetBlock();
                var list = new List<QuoteRecord> { BRec("A", "2026-11-04 10:00:00"), BRec("B", "2026-11-03 10:00:00"),
                                                   BRec("C", "2026-11-02 10:00:00"), BRec("D", "2026-11-01 10:00:00") };
                True("delete test history saved", History.Save(list) == null);
                foreach (var id in new[] { "A", "B", "C" }) History.SavePicture(id, png);
                History.SavePicture("D", real);
                True("PictureIdSet = the 4 ids", History.PictureIdSet().Count == 4 && History.PictureIdSet().Contains("D"));
                True("PicturePath: file / none / unsafe", History.PicturePath("D") == System.IO.Path.Combine(History.PictureDir, "D.jpg")
                     && History.PicturePath("Z") == null && History.PicturePath(@"..\x") == null);

                RemovedRecords removed;
                string problem;
                True("DeleteRecords B, C", History.DeleteRecords(list, new[] { "B", "C" }, out removed) == null && removed.Count == 2
                     && J(IdsOf(list)) == "A|D");
                True("one save: file has A, D, no .tmp", J(IdsOf(History.Load(out problem))) == "A|D" && problem == null
                     && !System.IO.File.Exists(History.FilePath + ".tmp"));
                True("pictures kept for the undo", History.HasPicture("B") && History.HasPicture("C"));
                True("UndoDelete: A, B, C, D in list and file", History.UndoDelete(list, removed) == null && J(IdsOf(list)) == "A|B|C|D"
                     && J(IdsOf(History.Load(out problem))) == "A|B|C|D");
                True("DropRemoved after undo: nothing deleted", History.DropRemoved(list, removed) == 0 && History.HasPicture("B") && History.HasPicture("C"));
                True("DeleteRecords B, C again", History.DeleteRecords(list, new[] { "C", "B" }, out removed) == null && J(IdsOf(list)) == "A|D");
                True("DropRemoved: B, C pictures gone, A, D kept", History.DropRemoved(list, removed) == 2
                     && !History.HasPicture("B") && !History.HasPicture("C") && History.HasPicture("A") && History.HasPicture("D"));
                True("DeleteRecords no match: null, nothing removed", History.DeleteRecords(list, new[] { "Q" }, out removed) == null
                     && removed.Count == 0 && list.Count == 2);
                History.SetBlock();
                True("DeleteRecords while blocked: refused, list unchanged", History.DeleteRecords(list, new[] { "A" }, out removed) != null
                     && removed.Count == 0 && J(IdsOf(list)) == "A|D");
                string cp;
                True("ClearAll while blocked: refused", History.ClearAll(list, "test", when, out cp) != null && cp == null && list.Count == 2);
                History.ResetBlock();
                using (new System.IO.FileStream(History.FilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
                    True("DeleteRecords when the save fails: list as before", History.DeleteRecords(list, new[] { "A" }, out removed) != null
                         && removed.Count == 0 && J(IdsOf(list)) == "A|D" && History.HasPicture("A"));

                // Clear all
                string empty;
                True("ClearAll empty list: nothing", History.ClearAll(new List<QuoteRecord>(), "test", when, out empty) == null && empty == null
                     && History.NewestClearCopy() == null);
                True("ClearAll OK", History.ClearAll(list, "test", when, out cp) == null && cp != null && System.IO.File.Exists(cp)
                     && System.IO.Path.GetFileName(cp) == "history.before-clear-20261113-100000.printprice-backup" && list.Count == 0);
                True("after ClearAll: file empty, no pictures", History.Load(out problem).Count == 0 && History.PictureIds().Count == 0);
                True("NewestClearCopy = the copy", string.Equals(History.NewestClearCopy(), cp, StringComparison.OrdinalIgnoreCase));
                BackupData bd;
                True("safety copy has A, D and their pictures", History.ReadBackupFile(cp, out bd) == null && J(SortedIds(IdsOf(bd.Quotes))) == "A|D"
                     && SameBytes(bd.Pictures["A"], png) && SameBytes(bd.Pictures["D"], real) && (bd.Settings == null || bd.Settings.Length == 0));
                RestoreResult rr;
                var now = new List<QuoteRecord>();
                True("get it back: Restore replace", History.Restore(now, new AppSettings(), bd, true, false, when, out rr) == null
                     && J(SortedIds(IdsOf(rr.Quotes))) == "A|D" && SameBytes(History.LoadPicture("A"), png) && SameBytes(History.LoadPicture("D"), real));
                // CleanPictures with an empty list (test report finding 1): cleans only when history.json reads fine as empty
                History.SavePicture("K", png);
                System.IO.File.Delete(History.FilePath);
                True("CleanPictures empty list, no history.json -> keeps", History.CleanPictures(new List<QuoteRecord>()) == 0 && History.HasPicture("K"));
                System.IO.File.WriteAllText(History.FilePath, "not json");
                True("CleanPictures empty list, unreadable history.json -> keeps", History.CleanPictures(new List<QuoteRecord>()) == 0 && History.HasPicture("K"));
                True("Save the restored history", History.Save(rr.Quotes) == null);
                True("CleanPictures empty list, history.json not empty -> keeps", History.CleanPictures(new List<QuoteRecord>()) == 0 && History.HasPicture("K"));
                True("Save empty history", History.Save(new List<QuoteRecord>()) == null);
                History.SetBlock();
                True("CleanPictures empty list while blocked -> keeps", History.CleanPictures(new List<QuoteRecord>()) == 0 && History.HasPicture("K"));
                History.ResetBlock();
                int cleaned = History.CleanPictures(new List<QuoteRecord>());
                True("CleanPictures empty list, history.json empty -> removes all (K, A, D)", cleaned == 3 && History.PictureIds().Count == 0);
                True("Save the restored history again", History.Save(rr.Quotes) == null);
                var again = History.Load(out problem);
                string cp2;
                True("second ClearAll same second: -2 copy, both kept", History.ClearAll(again, "test", when, out cp2) == null
                     && System.IO.Path.GetFileName(cp2) == "history.before-clear-20261113-100000-2.printprice-backup" && System.IO.File.Exists(cp));
            }
            finally
            {
                History.ResetBlock();
                AppSettings.ClearTestFolder();
            }
        }

        static List<string> SortedIds(List<string> l)
        {
            var c = new List<string>(l);
            c.Sort(StringComparer.Ordinal);
            return c;
        }

        // ---------- online backup (OnlineBackup.cs, online): texts, size check, headers, fake Worker ----------
        static void OnlineBackupTests()
        {
            bool wasDanish = L.Danish;
            L.Danish = false;
            try
            {
                True("online: no connection text", OnlineBackup.ErrorText(0, false).Contains("internet"));
                True("online: 404 on get = no backup", OnlineBackup.ErrorText(404, true).StartsWith("No online backup") && OnlineBackup.ErrorText(404, true).Contains("400 days"));
                True("online: 404 not get = generic", OnlineBackup.ErrorText(404, false).Contains("error 404"));
                True("online: 413 too big", OnlineBackup.ErrorText(413, false).Contains("512 KB"));
                True("online: 429 = 503 busy", OnlineBackup.ErrorText(429, false) == OnlineBackup.ErrorText(503, false) && OnlineBackup.ErrorText(503, false).Contains("busy"));
                True("online: 400 update", OnlineBackup.ErrorText(400, false).Contains("Update PrintPrice"));
                True("online: 500 generic", OnlineBackup.ErrorText(500, true).Contains("error 500"));
                True("online: 512 KB allowed", OnlineBackup.SizeProblem(512 * 1024) == null);
                True("online: 512 KB + 1 refused", OnlineBackup.SizeProblem(512 * 1024 + 1) == OnlineBackup.ErrorText(413, false));
                L.Danish = true;
                True("online: Danish 404", OnlineBackup.ErrorText(404, true).StartsWith("Der blev ikke fundet"));
                True("online: Danish daily limit", OnlineBackup.DailyLimitText(null, DateTime.UtcNow).StartsWith("Dagens grænse er nået (5"));
                True("online: Danish wait", OnlineBackup.WaitText(TimeSpan.FromMinutes(200)) == "3 t. 20 min.");
                {
                    var n0 = new DateTime(2026, 11, 10, 20, 40, 0, DateTimeKind.Utc);
                    bool noDouble = true;
                    foreach (bool da in new[] { true, false })
                    {
                        L.Danish = da;
                        foreach (int m in new[] { 0, 21, 60, 201 })
                        {
                            string t = OnlineBackup.DailyLimitText(n0.AddMinutes(m), n0);
                            if (t.Contains("..") || !t.EndsWith(".")) noDouble = false;
                        }
                        if (OnlineBackup.DailyLimitText(null, n0).Contains("..")) noDouble = false;
                    }
                    L.Danish = true;
                    True("online: daily text no '..' (EN + DA)", noDouble);
                    True("online: Danish daily text", OnlineBackup.DailyLimitText(n0.AddMinutes(201), n0) ==
                         "Dagens grænse er nået (5 online-kopier om dagen). Du kan tage en kopi igen om 3 t. 21 min.");
                }
                L.Danish = false;

                // headers
                var now = new DateTime(2026, 11, 10, 20, 40, 0, DateTimeKind.Utc);
                var h = OnlineBackup.ParseLimitHeaders("3", "2026-11-11T00:00:00Z", null, now);
                True("online: headers left 3", h.UploadsLeft == 3);
                True("online: headers reset", h.ResetUtc == new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc) && h.ResetUtc.Value.Kind == DateTimeKind.Utc);
                h = OnlineBackup.ParseLimitHeaders(null, null, "3600", now);
                True("online: Retry-After only", h.UploadsLeft == -1 && h.ResetUtc == now.AddHours(1));
                h = OnlineBackup.ParseLimitHeaders("x", "junk", "-5", now);
                True("online: junk headers", h.UploadsLeft == -1 && h.ResetUtc == null);
                True("online: wait 3 h 20 min", OnlineBackup.WaitText(TimeSpan.FromMinutes(199.2)) == "3 h 20 min");
                True("online: wait 2 h", OnlineBackup.WaitText(TimeSpan.FromHours(2)) == "2 h");
                True("online: wait 0 -> 1 min", OnlineBackup.WaitText(TimeSpan.Zero) == "1 min");
                True("online: daily text", OnlineBackup.DailyLimitText(now.AddMinutes(200), now) ==
                     "Daily limit reached (5 online backups a day). You can back up again in 3 h 20 min.");
                True("online: daily text no time", OnlineBackup.DailyLimitText(null, now).EndsWith("again tomorrow."));

                // URL and invalid code
                string code = Backup.NewCode(delegate(int i) { return (i * 7 + 3) & 31; });
                string other = Backup.NewCode(delegate(int i) { return (i * 5 + 1) & 31; });
                True("online: url", OnlineBackup.Url("https://x.example/", code) == "https://x.example/b/" + Backup.StorageId(code));
                int st = 99;
                try { OnlineBackup.Url("https://x.example", "ABC"); } catch (OnlineBackupException ex) { st = ex.Status; }
                True("online: invalid code = -1", st == -1);

                // too big: refused before any network (port 1 would give status 0)
                var rnd = new Random(1);
                var big = new System.Text.StringBuilder("{\"x\":\"");
                for (int i = 0; i < 1200000; i++) big.Append((char)('a' + rnd.Next(26)));   // random letters: ~700 KB after gzip
                big.Append("\"}");
                st = 99;
                try { OnlineBackup.Upload("http://127.0.0.1:1", code, big.ToString()); } catch (OnlineBackupException ex) { st = ex.Status; }
                True("online: too big refused locally", st == -1);

                // fake Worker on 127.0.0.1
                using (var fake = new FakeWorker())
                {
                    string json = "{\"Format\":\"PrintPriceBackup\",\"Version\":1}";
                    var up = OnlineBackup.Upload(fake.Url, code, json);
                    True("online fake: upload stored", fake.Count == 1 && Backup.IsBlob(fake.Get(Backup.StorageId(code))));
                    True("online fake: upload left 4 + reset", up.UploadsLeft == 4 && up.ResetUtc == new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc));
                    True("online fake: download", OnlineBackup.Download(fake.Url, code) == json);

                    st = 99;
                    try { OnlineBackup.Download(fake.Url, other); } catch (OnlineBackupException ex) { st = ex.Status; }
                    True("online fake: other code 404", st == 404);

                    fake.Get(Backup.StorageId(code))[30] ^= 1;
                    st = 99;
                    try { OnlineBackup.Download(fake.Url, code); } catch (OnlineBackupException ex) { st = ex.Status; }
                    True("online fake: damaged = -1", st == -1);

                    OnlineBackup.Delete(fake.Url, code);
                    True("online fake: deleted", fake.Count == 0);
                    bool threw = false;
                    try { OnlineBackup.Delete(fake.Url, code); } catch (OnlineBackupException) { threw = true; }
                    True("online fake: delete missing is OK", !threw);

                    foreach (int s in new[] { 413, 429, 500, 503 })
                    {
                        fake.Force = s;
                        OnlineBackupException e = null;
                        try { OnlineBackup.Upload(fake.Url, code, json); } catch (OnlineBackupException ex) { e = ex; }
                        True("online fake: forced " + s, e != null && e.Status == s && !e.DailyLimit && e.Message == OnlineBackup.ErrorText(s, false));
                    }

                    fake.Force = 429;
                    fake.ForceHeaders = "Retry-After: 3600\r\nX-Uploads-Left: 0\r\nX-Uploads-Reset: 2030-01-01T00:00:00Z\r\n";
                    OnlineBackupException d = null;
                    try { OnlineBackup.Upload(fake.Url, code, json); } catch (OnlineBackupException ex) { d = ex; }
                    True("online fake: daily limit", d != null && d.Status == 429 && d.DailyLimit &&
                         d.ResetUtc == new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc) && d.Message.StartsWith("Daily limit reached (5"));
                    fake.ForceHeaders = "Retry-After: 60\r\n";
                    d = null;
                    try { OnlineBackup.Upload(fake.Url, code, json); } catch (OnlineBackupException ex) { d = ex; }
                    True("online fake: minute limit = busy", d != null && d.Status == 429 && !d.DailyLimit);
                    fake.Force = 0;
                    fake.ForceHeaders = "";

                    string deadUrl = fake.Url;
                    fake.Dispose();
                    st = 99;
                    try { OnlineBackup.Download(deadUrl, code); } catch (OnlineBackupException ex) { st = ex.Status; }
                    True("online fake: server gone = 0", st == 0);
                }
            }
            finally { L.Danish = wasDanish; }
        }
    }

    /// A tiny HTTP/1.1 server on 127.0.0.1 (random port; loopback only, so no admin rights and no firewall prompt) that
    /// acts like the Worker's /b/<id> route. Force > 0 = answer every request with that status (plus ForceHeaders).
    class FakeWorker : IDisposable
    {
        readonly System.Net.Sockets.TcpListener listener;
        readonly Dictionary<string, byte[]> store = new Dictionary<string, byte[]>();
        public volatile int Force;
        public volatile string ForceHeaders = "";
        int left = 5;
        public string Url { get; private set; }

        public FakeWorker()
        {
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            Url = "http://127.0.0.1:" + ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            new System.Threading.Thread(Loop) { IsBackground = true }.Start();
        }

        public int Count { get { lock (store) return store.Count; } }
        public byte[] Get(string id) { lock (store) return store[id]; }

        void Loop()
        {
            while (true)
            {
                System.Net.Sockets.TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch { return; }
                try { using (c) Handle(c.GetStream()); } catch { }
            }
        }

        static string ReadLine(System.IO.Stream s)
        {
            var sb = new System.Text.StringBuilder();
            int b;
            while ((b = s.ReadByte()) >= 0 && b != '\n') if (b != '\r') sb.Append((char)b);
            return sb.ToString();
        }

        void Handle(System.Net.Sockets.NetworkStream s)
        {
            string[] first = ReadLine(s).Split(' ');
            string method = first[0], path = first.Length > 1 ? first[1] : "/";
            int len = 0;
            string line;
            while ((line = ReadLine(s)).Length > 0)
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) len = int.Parse(line.Substring(15).Trim());
            var body = new byte[len];
            for (int got = 0; got < len; ) { int n = s.Read(body, got, len - got); if (n <= 0) break; got += n; }

            int status; byte[] outBody = new byte[0]; string headers = "";
            string id = path.StartsWith("/b/") ? path.Substring(3) : null;
            if (Force > 0) { status = Force; headers = ForceHeaders; }
            else if (id == null) status = 404;
            else if (method == "PUT")
            {
                lock (store) store[id] = body;
                left--;
                status = 204;
                headers = "X-Uploads-Left: " + left + "\r\nX-Uploads-Reset: 2026-11-11T00:00:00Z\r\n";
            }
            else if (method == "GET")
            {
                byte[] b;
                lock (store) if (!store.TryGetValue(id, out b)) b = null;
                if (b == null) status = 404; else { status = 200; outBody = b; }
            }
            else if (method == "DELETE")
            {
                bool had;
                lock (store) had = store.Remove(id);
                status = had ? 204 : 404;
            }
            else status = 405;

            string head = "HTTP/1.1 " + status + " X\r\n" + headers + "Content-Length: " + outBody.Length + "\r\nConnection: close\r\n\r\n";
            byte[] hb = System.Text.Encoding.ASCII.GetBytes(head);
            s.Write(hb, 0, hb.Length);
            s.Write(outBody, 0, outBody.Length);
            s.Flush();
        }

        public void Dispose() { try { listener.Stop(); } catch { } }
    }

    /// A tiny HTTP server on 127.0.0.1 (random port, loopback only) for Net.Run: GET /small = "hello", /big = 5000
    /// bytes, /stall = reads the request and sends nothing for 3 s. One thread per connection.
    class TinyServer : IDisposable
    {
        readonly System.Net.Sockets.TcpListener listener;
        public string Url { get; private set; }

        public TinyServer()
        {
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            Url = "http://127.0.0.1:" + ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            new System.Threading.Thread(Loop) { IsBackground = true }.Start();
        }

        void Loop()
        {
            while (true)
            {
                System.Net.Sockets.TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch { return; }
                new System.Threading.Thread(() => { try { using (c) Handle(c.GetStream()); } catch { } }) { IsBackground = true }.Start();
            }
        }

        static string ReadLine(System.IO.Stream s)
        {
            var sb = new System.Text.StringBuilder();
            int b;
            while ((b = s.ReadByte()) >= 0 && b != '\n') if (b != '\r') sb.Append((char)b);
            return sb.ToString();
        }

        static void Handle(System.Net.Sockets.NetworkStream s)
        {
            string[] first = ReadLine(s).Split(' ');
            string path = first.Length > 1 ? first[1] : "/";
            while (ReadLine(s).Length > 0) { }
            if (path == "/stall") { System.Threading.Thread.Sleep(3000); return; }
            byte[] body = path == "/big" ? new byte[5000] : System.Text.Encoding.ASCII.GetBytes("hello");
            byte[] hb = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            s.Write(hb, 0, hb.Length);
            s.Write(body, 0, body.Length);
            s.Flush();
        }

        public void Dispose() { try { listener.Stop(); } catch { } }
    }
}
