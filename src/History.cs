using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace PrintPrice
{
    /// One colour line of a saved multi-colour quote (enough to rebuild the colour rows without re-slicing).
    public class HistoryFilament
    {
        public double Grams;
        public double PricePerKg;
        public string Colour = "";   // "#RRGGBB" or ""
        public string Type = "";     // e.g. "PLA"
        public string Name = "";     // filament profile / display name
    }

    /// One saved quote: inputs, the settings used, the result as shown, and the user's own notes.
    public class QuoteRecord
    {
        public string Id = "";
        public string SavedAt = "";          // local time "yyyy-MM-dd HH:mm:ss"
        public string Model = "";            // Printables name or file name
        public string Source = "";           // link or file path as typed in the link box

        // inputs
        public int Hours;
        public int Minutes;
        public double Grams;                 // total, incl. purge
        public double FilamentDkkPerKg;
        public List<HistoryFilament> Filaments;   // null = one filament
        public double PurgeGrams = -1;       // -1 = unknown
        public double ElectricityDkkPerKwh;

        // settings used (snapshot)
        public double PrinterWatts;
        public double MachineRatePerHour;
        public double FailedPrintPct;
        public double FriendsMarkupPct;
        public double OthersMarkupPct;
        public double MinimumPrice;
        public double RoundTo;

        // result as shown
        public double MaterialCost;
        public double PowerCost;
        public double FailedPrintCost;
        public double BaseCost;
        public double FriendsPrice;
        public double OthersPrice;

        // the user's own
        public int For = History.ForCustomer;   // 1 = friends & family, 2 = customer (old 0 = not set -> read as 2)
        public string Note = "";

        // idea 9 (later): failed print, its cost made back by a surcharge
        public bool Failed;
        public string FailedAt = "";         // "" = not failed
    }

    /// The file's top level: {"Version":1,"Quotes":[...]} (newest first).
    public class HistoryFile
    {
        public int Version;
        public List<QuoteRecord> Quotes;
    }

    /// Price history: saved quotes in history.json next to settings.ini. Local only, never sent anywhere.
    public static class History
    {
        public const int Version = 1;
        public const int MaxQuotes = 500;
        public const int ForFriends = 1;
        public const int ForCustomer = 2;
        public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Random rnd = new Random();
        static bool saveBlocked;   // the file exists but couldn't be read: never overwrite it

        public static string FilePath
        {
            get { return Path.Combine(AppSettings.Folder, "history.json"); }
        }

        // ---------- pure ----------

        public static string Stamp(DateTime t) { return t.ToString(TimeFormat, Inv); }

        public static bool TryTime(string s, out DateTime t)
        {
            return DateTime.TryParseExact(s ?? "", TimeFormat, Inv, DateTimeStyles.None, out t);
        }

        /// Copies a calculated quote into a record. ui fills Colour/Type/Name of the colour lines and PurgeGrams.
        public static QuoteRecord FromQuote(QuoteInput input, QuoteResult result, AppSettings s,
                                            string model, string source, DateTime now)
        {
            if (input == null || result == null || s == null) throw new ArgumentNullException();
            var r = new QuoteRecord();
            lock (rnd) r.Id = now.ToString("yyyyMMddHHmmssfff", Inv) + rnd.Next(0x10000).ToString("x4", Inv);
            r.SavedAt = Stamp(now);
            r.Model = (model ?? "").Trim();
            r.Source = (source ?? "").Trim();
            r.Hours = input.Hours;
            r.Minutes = input.Minutes;
            r.Grams = input.FilamentGrams;
            r.FilamentDkkPerKg = input.FilamentDkkPerKg;
            if (input.Filaments != null && input.Filaments.Count >= 2)
            {
                r.Filaments = new List<HistoryFilament>(input.Filaments.Count);
                foreach (var f in input.Filaments)
                    r.Filaments.Add(new HistoryFilament { Grams = f == null ? 0 : f.Grams, PricePerKg = f == null ? 0 : f.PricePerKg });
            }
            r.ElectricityDkkPerKwh = input.ElectricityDkkPerKwh;

            r.PrinterWatts = s.PrinterWatts;
            r.MachineRatePerHour = s.MachineRatePerHour;
            r.FailedPrintPct = s.FailedPrintPct;
            r.FriendsMarkupPct = s.FriendsMarkupPct;
            r.OthersMarkupPct = s.OthersMarkupPct;
            r.MinimumPrice = s.MinimumPrice;
            r.RoundTo = s.RoundTo;

            r.MaterialCost = result.MaterialCost;
            r.PowerCost = result.PowerCost;
            r.FailedPrintCost = result.FailedPrintCost;
            r.BaseCost = result.BaseCost;
            r.FriendsPrice = result.FriendsPrice;
            r.OthersPrice = result.OthersPrice;
            return r;
        }

        /// The inputs of a record, ready for Pricing.Calculate or for filling the main window.
        public static QuoteInput ToInput(QuoteRecord r)
        {
            var q = new QuoteInput
            {
                Hours = r.Hours, Minutes = r.Minutes, FilamentGrams = r.Grams,
                FilamentDkkPerKg = r.FilamentDkkPerKg, ElectricityDkkPerKwh = r.ElectricityDkkPerKwh
            };
            if (r.Filaments != null && r.Filaments.Count >= 2)
            {
                q.Filaments = new List<FilamentLine>(r.Filaments.Count);
                foreach (var f in r.Filaments)
                    q.Filaments.Add(new FilamentLine { Grams = f.Grams, PricePerKg = f.PricePerKg });
            }
            return q;
        }

        /// A copy of current (or defaults) with the record's settings snapshot: Pricing.Calculate(ToInput(r), this)
        /// gives the price as it was.
        public static AppSettings SavedSettings(QuoteRecord r, AppSettings current)
        {
            var s = current != null ? current.Clone() : new AppSettings();
            s.PrinterWatts = r.PrinterWatts;
            s.MachineRatePerHour = r.MachineRatePerHour;
            s.AutoWearRate = 0;
            s.FailedPrintPct = r.FailedPrintPct;
            s.FriendsMarkupPct = r.FriendsMarkupPct;
            s.OthersMarkupPct = r.OthersMarkupPct;
            s.MinimumPrice = r.MinimumPrice;
            s.RoundTo = r.RoundTo;
            return s;
        }

        /// The one price the history list shows: friends & family price for For = 1, else the customer price.
        public static double PriceFor(QuoteRecord r)
        {
            return r.For == ForFriends ? r.FriendsPrice : r.OthersPrice;
        }

        static bool Same(double a, double b) { return Math.Abs(a - b) < 1e-9; }

        /// Same inputs, model/link and prices, saved at most 10 minutes apart.
        public static bool SameQuote(QuoteRecord a, QuoteRecord b)
        {
            if (a == null || b == null) return false;
            DateTime ta, tb;
            if (!TryTime(a.SavedAt, out ta) || !TryTime(b.SavedAt, out tb)) return false;
            if (Math.Abs((ta - tb).TotalMinutes) > 10) return false;
            if ((a.Model ?? "") != (b.Model ?? "") || (a.Source ?? "") != (b.Source ?? "")) return false;
            if (a.Hours != b.Hours || a.Minutes != b.Minutes || !Same(a.Grams, b.Grams)
                || !Same(a.FilamentDkkPerKg, b.FilamentDkkPerKg) || !Same(a.ElectricityDkkPerKwh, b.ElectricityDkkPerKwh)
                || !Same(a.FriendsPrice, b.FriendsPrice) || !Same(a.OthersPrice, b.OthersPrice)) return false;
            int na = a.Filaments == null ? 0 : a.Filaments.Count, nb = b.Filaments == null ? 0 : b.Filaments.Count;
            if (na != nb) return false;
            for (int i = 0; i < na; i++)
                if (!Same(a.Filaments[i].Grams, b.Filaments[i].Grams) || !Same(a.Filaments[i].PricePerKg, b.Filaments[i].PricePerKg))
                    return false;
            return true;
        }

        /// Adds r as the newest (index 0). The same quote as the newest one within 10 min replaces it, keeping its
        /// Id, For, Note and Failed mark. Then trims to MaxQuotes. Returns the record now in the list.
        public static QuoteRecord Add(List<QuoteRecord> list, QuoteRecord r)
        {
            if (list.Count > 0 && SameQuote(list[0], r))
            {
                var old = list[0];
                r.Id = old.Id; r.For = old.For; r.Note = old.Note; r.Failed = old.Failed; r.FailedAt = old.FailedAt;
                list[0] = r;
            }
            else list.Insert(0, r);
            Trim(list, MaxQuotes);
            return r;
        }

        /// Keeps the newest max entries (list is newest first); older failed ones are kept too (idea 9 needs them).
        public static void Trim(List<QuoteRecord> list, int max)
        {
            if (list.Count <= max) return;
            var keep = new List<QuoteRecord>(max + 8);
            for (int i = 0; i < list.Count; i++)
                if (i < max || (list[i] != null && list[i].Failed)) keep.Add(list[i]);
            list.Clear();
            list.AddRange(keep);
        }

        public static bool Remove(List<QuoteRecord> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id == id) { list.RemoveAt(i); return true; }
            return false;
        }

        public static void MarkFailed(QuoteRecord r, bool failed, DateTime now)
        {
            r.Failed = failed;
            r.FailedAt = failed ? Stamp(now) : "";
        }

        static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        public static string ToJson(List<QuoteRecord> list)
        {
            return Json().Serialize(new HistoryFile { Version = Version, Quotes = list ?? new List<QuoteRecord>() });
        }

        /// The quotes, or null if the text isn't a history file of this version (garbage, empty, newer version).
        public static List<QuoteRecord> ParseJson(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Trim().Length == 0) return null;
            HistoryFile f;
            try { f = Json().Deserialize<HistoryFile>(json); }
            catch { return null; }
            if (f == null || f.Version != Version || f.Quotes == null) return null;
            var list = new List<QuoteRecord>(f.Quotes.Count);
            foreach (var r in f.Quotes)
            {
                if (r == null) continue;
                if (r.Id == null) r.Id = "";
                if (r.SavedAt == null) r.SavedAt = "";
                if (r.Model == null) r.Model = "";
                if (r.Source == null) r.Source = "";
                if (r.Note == null) r.Note = "";
                if (r.FailedAt == null) r.FailedAt = "";
                if (r.For != ForFriends) r.For = ForCustomer;   // old 0 (not set) or unknown -> customer
                if (r.Filaments != null)
                {
                    r.Filaments.RemoveAll(x => x == null);
                    foreach (var x in r.Filaments)
                    {
                        if (x.Colour == null) x.Colour = "";
                        if (x.Type == null) x.Type = "";
                        if (x.Name == null) x.Name = "";
                    }
                    if (r.Filaments.Count < 2) r.Filaments = null;
                }
                list.Add(r);
            }
            return list;
        }

        // ---------- file ----------

        /// Reads history.json. Missing -> empty list, problem null. Unreadable content or a newer version -> the file
        /// is renamed to history.bad-yyyyMMdd-HHmmss.json (kept), empty list, problem = text for the ui.
        /// Never throws; never touches settings.ini.
        public static List<QuoteRecord> Load(out string problem)
        {
            problem = null;
            saveBlocked = false;
            string path = FilePath;
            string text;
            try
            {
                if (!File.Exists(path)) return new List<QuoteRecord>();
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                saveBlocked = true;   // maybe locked: don't overwrite a file we couldn't read
                problem = L.S("Price history could not be read: ", "Prishistorikken kunne ikke læses: ") + ex.Message;
                return new List<QuoteRecord>();
            }
            var list = ParseJson(text);
            if (list != null) return list;

            try
            {
                string bad = BackupName(path, DateTime.Now);
                File.Move(path, bad);
                problem = L.F("Old price history was unreadable, kept as {0}.", "Den gamle prishistorik kunne ikke læses, gemt som {0}.",
                              Path.GetFileName(bad));
            }
            catch (Exception ex)
            {
                saveBlocked = true;
                problem = L.S("Price history could not be read: ", "Prishistorikken kunne ikke læses: ") + ex.Message;
            }
            return new List<QuoteRecord>();
        }

        /// history.bad-yyyyMMdd-HHmmss.json in the same folder, with -2, -3... if taken.
        public static string BackupName(string path, DateTime now)
        {
            string dir = Path.GetDirectoryName(path);
            string stem = "history.bad-" + now.ToString("yyyyMMdd-HHmmss", Inv);
            string name = Path.Combine(dir, stem + ".json");
            for (int n = 2; File.Exists(name); n++) name = Path.Combine(dir, stem + "-" + n.ToString(Inv) + ".json");
            return name;
        }

        /// Writes the list (via history.json.tmp, then replace). null = OK, else an error text. Never throws.
        public static string Save(List<QuoteRecord> list)
        {
            if (saveBlocked)
                return L.S("Price history not saved: the file could not be read.", "Prishistorikken blev ikke gemt: filen kunne ikke læses.");
            string path = FilePath, tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, ToJson(list), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return null;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return L.S("Price history not saved: ", "Prishistorikken blev ikke gemt: ") + ex.Message;
            }
        }

        /// Tests only.
        internal static void ResetBlock() { saveBlocked = false; }
    }
}
