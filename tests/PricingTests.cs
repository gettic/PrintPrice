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
        }

        // ---------- backup and restore (idea 27): History backup/merge/restore, Backup code + blob ----------
        static QuoteRecord BRec(string id, string savedAt)
        {
            var r = HistRecord(DateTime.ParseExact(savedAt, History.TimeFormat, System.Globalization.CultureInfo.InvariantCulture));
            r.Id = id;
            return r;
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

            // settings defaults, Clone, valid-until
            var d = new AppSettings();
            True("quote defaults: Gettic, 14 days, customer, dark", d.QuoteName == "Gettic" && d.QuoteValidDays == 14 &&
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

        static bool CodeOk(string no, int year)
        {
            if (no == null || no.Length != 9 || !no.StartsWith(year.ToString() + "-")) return false;
            for (int i = 5; i < 9; i++) if (History.InvoiceAlphabet.IndexOf(no[i]) < 0) return false;
            return true;
        }

        static void InvoiceTests()
        {
            var t = new DateTime(2026, 11, 4, 14, 5, 33);
            True("alphabet: 29 upper-case characters, no 0 O 1 I L 5 S", History.InvoiceAlphabet.Length == 29 &&
                 History.InvoiceAlphabet == History.InvoiceAlphabet.ToUpperInvariant() &&
                 History.InvoiceAlphabet.IndexOfAny("01ILO5S".ToCharArray()) < 0);
            // format with the real crypto source
            bool fmt = true; var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                string no = History.NextInvoiceNo(null, 2026, "");
                fmt = fmt && CodeOk(no, 2026);
                seen.Add(no);
            }
            True("200 crypto codes all look like 2026-XXXX", fmt);
            True("200 crypto codes are (nearly) all different", seen.Count >= 195);
            True("year in the code: 2027-", History.NextInvoiceNo(null, 2027, "").StartsWith("2027-"));
            True("fake source: ABCD", History.NextInvoiceNo(null, 2026, "", Seq(0, 1, 2, 3)) == "2026-ABCD");
            True("fake source: K7Q4", History.NextInvoiceNo(null, 2026, "", Seq(9, 26, 13, 24)) == "2026-K7Q4");

            // unique against history and against the used list: the fake returns two taken codes first
            var hist = new List<QuoteRecord> { new QuoteRecord { InvoiceNo = "2026-ABCD" }, null, new QuoteRecord { InvoiceNo = "" } };
            string got = History.NextInvoiceNo(hist, 2026, "2026-EFGH,2025-XXXX", Seq(0, 1, 2, 3, 4, 5, 6, 7, 9, 26, 13, 24));
            True("collision with history (ABCD) and used list (EFGH) -> K7Q4", got == "2026-K7Q4");
            True("used list is trimmed and case-insensitive", History.NextInvoiceNo(null, 2026, " 2026-abcd , ", Seq(0, 1, 2, 3, 4, 5, 6, 7)) == "2026-EFGH");
            // same code in another year is a different invoice number
            True("same letters in another year is free", History.NextInvoiceNo(hist, 2027, "", Seq(0, 1, 2, 3)) == "2027-ABCD");

            // MakeInvoice: new code, BilledAt, Printed, used list
            var list = new List<QuoteRecord>();
            var a = HistRecord(t);
            list.Add(a);
            var st = new AppSettings();
            True("UsedInvoiceNos default empty", st.UsedInvoiceNos == "");
            True("record without invoice: no number", a.InvoiceNo == "" && a.BilledAt == "" && !a.Printed);
            string n1 = History.MakeInvoice(list, a, t, st, Seq(0, 1, 2, 3));
            True("MakeInvoice: 2026-ABCD, BilledAt, Printed", n1 == "2026-ABCD" && a.InvoiceNo == n1 &&
                 a.BilledAt == "2026-11-04 14:05:33" && a.Printed);
            True("code added to the used list", st.UsedInvoiceNos == "2026-ABCD");
            // a second invoice on the same record keeps code and date, the used list stays
            string again = History.MakeInvoice(list, a, t.AddDays(30), st, Seq(4, 5, 6, 7));
            True("second invoice on the same record: same code and date", again == "2026-ABCD" && a.BilledAt == "2026-11-04 14:05:33" &&
                 st.UsedInvoiceNos == "2026-ABCD");
            // never reused: the record is deleted, its code still can't come back
            var b = HistRecord(t.AddHours(1)); b.Model = "Other";
            list.Clear(); list.Add(b);
            string n2 = History.MakeInvoice(list, b, t.AddDays(1), st, Seq(0, 1, 2, 3, 4, 5, 6, 7));
            True("deleted record's code ABCD not given again -> EFGH", n2 == "2026-EFGH" && st.UsedInvoiceNos == "2026-ABCD,2026-EFGH");
            // null settings: works, nothing remembered
            var c = HistRecord(t); c.Model = "C";
            True("MakeInvoice with null settings", History.MakeInvoice(list, c, t, null, Seq(9, 26, 13, 24)) == "2026-K7Q4" && c.Printed);
            // a year change uses the new year
            var d2 = HistRecord(t); d2.Model = "D";
            True("MakeInvoice in 2027 -> 2027-...", History.MakeInvoice(list, d2, new DateTime(2027, 1, 2, 9, 0, 0), st, Seq(0, 1, 2, 3)) == "2027-ABCD");

            // used list: last 2000 kept
            string used = "";
            for (int i = 0; i < 2005; i++) used = History.AddUsedInvoiceNo(used, "2026-N" + i);
            string[] up = used.Split(',');
            True("used list keeps the last 2000", up.Length == 2000 && up[0] == "2026-N5" && up[1999] == "2026-N2004");
            True("AddUsedInvoiceNo ignores empty", History.AddUsedInvoiceNo("a", "") == "a" && History.AddUsedInvoiceNo(null, "x") == "x");

            // Add (same-quote replace) keeps code and date
            var al = new List<QuoteRecord>();
            var first = History.Add(al, HistRecord(t));
            History.MakeInvoice(al, first, t, null, Seq(0, 1, 2, 3));
            var second = History.Add(al, HistRecord(t.AddMinutes(2)));
            True("Add replace keeps InvoiceNo, BilledAt, Printed", al.Count == 1 && second.InvoiceNo == "2026-ABCD" &&
                 second.BilledAt == "2026-11-04 14:05:33" && second.Printed);

            // JSON round trip, and old files without the keys read ""
            var back = History.ParseJson(History.ToJson(al));
            True("JSON round trip keeps invoice fields", back[0].InvoiceNo == "2026-ABCD" && back[0].BilledAt == second.BilledAt);
            var old = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"x\",\"Model\":\"m\"}]}");
            True("old file: InvoiceNo and BilledAt are empty", old[0].InvoiceNo == "" && old[0].BilledAt == "");

            // invoice snapshot: a new number saves due date, MobilePay, bank and "from" name
            var ss = new AppSettings { InvoiceDueDays = 14, PayMobilePay = "12 34 56 78", PayBank = "Reg 1234 konto 0012345678", QuoteName = "Gettic 3D" };
            var sl = new List<QuoteRecord>();
            var sa = HistRecord(t); sl.Add(sa);
            True("not invoiced: no snapshot", sa.InvoiceDue == "" && sa.InvoicePayMobilePay == "" && sa.InvoicePayBank == "" && sa.InvoiceFromName == "");
            History.MakeInvoice(sl, sa, t, ss, Seq(0, 1, 2, 3));
            // 4/11/2026 14:05 -> date 4/11 + 14 days = 18/11/2026
            True("snapshot: due 2026-11-18, MobilePay, bank, from name", sa.InvoiceDue == "2026-11-18" &&
                 sa.InvoicePayMobilePay == "12 34 56 78" && sa.InvoicePayBank == "Reg 1234 konto 0012345678" && sa.InvoiceFromName == "Gettic 3D");
            // settings change later: re-invoice keeps the snapshot, InvoiceInfo shows it
            ss.InvoiceDueDays = 30; ss.PayMobilePay = ""; ss.PayBank = "Other bank"; ss.QuoteName = "New name";
            History.MakeInvoice(sl, sa, t.AddDays(10), ss, Seq(4, 5, 6, 7));
            True("re-invoice keeps the snapshot", sa.InvoiceNo == "2026-ABCD" && sa.InvoiceDue == "2026-11-18" &&
                 sa.InvoicePayMobilePay == "12 34 56 78" && sa.InvoicePayBank == "Reg 1234 konto 0012345678" && sa.InvoiceFromName == "Gettic 3D");
            var ia = History.InvoiceInfo(sa, ss, t.AddDays(10));
            True("InvoiceInfo uses the snapshot, not today's settings", ia.FromSnapshot && ia.Due == new DateTime(2026, 11, 18) &&
                 ia.MobilePay == "12 34 56 78" && ia.Bank == "Reg 1234 konto 0012345678" && ia.FromName == "Gettic 3D");
            // a snapshot with empty payment fields stays empty (not filled from today's settings)
            var se = new AppSettings { PayMobilePay = "", PayBank = "", QuoteName = "" };
            var sb2 = HistRecord(t.AddHours(2)); sb2.Model = "E"; sl.Add(sb2);
            History.MakeInvoice(sl, sb2, t, se, Seq(9, 26, 13, 24));
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
            // bad snapshot date -> today's settings; null record / settings never crash (defaults: 14 days, "Gettic")
            var sbad = HistRecord(t); sbad.InvoiceNo = "2026-BAD1"; sbad.BilledAt = "2026-11-04 14:05:33"; sbad.InvoiceDue = "18/11/2026";
            True("bad snapshot date -> settings", !History.InvoiceInfo(sbad, ss, t).FromSnapshot);
            var inull = History.InvoiceInfo(null, null, t);
            True("null record + settings -> defaults", inull != null && inull.Due == new DateTime(2026, 11, 18) && inull.FromName == "Gettic");
            // null settings in MakeInvoice: no snapshot
            var sn = HistRecord(t); sn.Model = "N";
            History.MakeInvoice(sl, sn, t, null, Seq(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11));
            True("MakeInvoice with null settings: no snapshot", sn.InvoiceNo != "" && sn.InvoiceDue == "");
            // Add (same-quote replace) keeps the snapshot
            var al2 = new List<QuoteRecord>();
            var f2 = History.Add(al2, HistRecord(t));
            History.MakeInvoice(al2, f2, t, new AppSettings { PayMobilePay = "MP", PayBank = "BK", QuoteName = "Q" }, Seq(0, 1, 2, 3));
            var s2b = History.Add(al2, HistRecord(t.AddMinutes(2)));
            True("Add replace keeps the snapshot", al2.Count == 1 && s2b.InvoiceDue == "2026-11-18" && s2b.InvoicePayMobilePay == "MP" &&
                 s2b.InvoicePayBank == "BK" && s2b.InvoiceFromName == "Q");
            // JSON round trip; old file without the keys reads ""
            var sj = History.ParseJson(History.ToJson(al2));
            True("JSON round trip keeps the snapshot", sj[0].InvoiceDue == "2026-11-18" && sj[0].InvoicePayMobilePay == "MP" &&
                 sj[0].InvoicePayBank == "BK" && sj[0].InvoiceFromName == "Q");
            True("old file: snapshot fields empty", old[0].InvoiceDue == "" && old[0].InvoicePayMobilePay == "" &&
                 old[0].InvoicePayBank == "" && old[0].InvoiceFromName == "");
            var jn = History.ParseJson("{\"Version\":1,\"Quotes\":[{\"Id\":\"x\",\"InvoiceDue\":null,\"InvoicePayBank\":null}]}");
            True("null snapshot keys read as empty", jn[0].InvoiceDue == "" && jn[0].InvoicePayBank == "");

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
            True("MachinePreset old (nozzle only)", BambuSlicer.MachinePreset("0.4") == "Bambu Lab A1 0.4 nozzle");
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
            True("BedErrorText A1M", Has(BambuSlicer.BedErrorText("A1M"), "Bambu Lab A1 mini", "180 × 180 × 180 mm", "plate in the file is empty"));
            True("BedErrorText P1S", Has(BambuSlicer.BedErrorText("P1S"), "256 × 256 × 250 mm"));
            True("BedErrorText \"\" -> A1", Has(BambuSlicer.BedErrorText(""), "Bambu Lab A1", "256 × 256 × 256 mm"));
            True("SliceErrorText -50 = BedErrorText", BambuSlicer.SliceErrorText(-50, "a.stl", "One of the plate is empty…", "A1M")
                 == BambuSlicer.BedErrorText("A1M"));
            True("SliceErrorText -17 no message", BambuSlicer.SliceErrorText(-17, "a.stl", "", "A1") == "Bambu Studio could not slice a.stl: exit code -17");
            True("SliceErrorText 1 with message", BambuSlicer.SliceErrorText(1, "a.stl", "boom", "A1") == "Bambu Studio could not slice a.stl: boom");
            True("UnsupportedPrinterText X1 Carbon", Has(BambuSlicer.UnsupportedPrinterText("Bambu Lab X1 Carbon"), "Bambu Lab X1 Carbon",
                "doesn't support yet", "https://github.com/gettic/PrintPrice/issues"));
            True("UnsupportedPrinterText \"\"", Has(BambuSlicer.UnsupportedPrinterText(""), "another printer"));
            L.Danish = true;
            try
            {
                True("BedErrorText A1M Danish", Has(BambuSlicer.BedErrorText("A1M"), "byggepladen"));
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
                new AppSettings { PrinterName = "  Prusa MK4  " }.Save();
                True("round trip PrinterName (trimmed)", AppSettings.Load().PrinterName == "Prusa MK4");
                System.IO.File.WriteAllText(testFile, "PrinterName=\r\n");
                True("empty PrinterName in file -> default", AppSettings.Load().PrinterName == "Bambu Lab A1");
                System.IO.File.WriteAllText(testFile, "PrinterName=    \r\n");
                True("spaces-only PrinterName in file -> default", AppSettings.Load().PrinterName == "Bambu Lab A1");
                System.IO.File.WriteAllText(testFile, "PrinterName=" + new string('b', 60) + "\r\n");
                Eq("long PrinterName in file capped at 40", 40, AppSettings.Load().PrinterName.Length);
                // 5a. quote export settings
                True("quote defaults in file", r5.QuoteName == "Gettic" && r5.QuoteValidDays == 14 &&
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

            Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
