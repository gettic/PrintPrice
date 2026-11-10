using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#if !WEB
using System.Web.Script.Serialization;
#endif

namespace PrintPrice
{
    /// One colour line of a saved quote (enough to rebuild the colour rows without re-slicing). A one-colour quote has
    /// one line (same Grams / PricePerKg as the record), a multi-colour quote 2+; quotes from before v1.6 have none (null).
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
        public List<HistoryFilament> Filaments;   // 1 line = one colour, 2+ = multi-colour, null = old one-colour quote
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

        // failed prints payback (idea 9): what this quote added to pay back failed prints, and the settings used
        public double PaybackCost;           // kr. in OthersPrice (and FriendsPrice when FailedPaybackWho = 1); 0 = none
        public double FailedPaybackPct;
        public double FailedPaybackWho;      // 1 = both prices, 2 = customers only (0 in old files = 2)

        // the user's own
        public int For = History.ForCustomer;   // 1 = friends & family, 2 = customer (old 0 = not set -> read as 2)
        public string Note = "";
        public string Customer = "";         // idea 21: who the quote is for ("" = not named)

        // idea 9: failed print, its cost (History.FailedCost) made back by a payback on later quotes
        public bool Failed;
        public string FailedAt = "";         // "" = not failed
        public double PaidBack;              // failed only: kr. paid by quotes Trim has dropped (so the debt stays the same)

        // idea 22: the user ticked "Printed" (the quote was really printed). New records false; a file saved before the
        // tick existed has no "Printed" key and reads as true (ParseJson, via printedRead).
        bool printed;
        internal bool printedRead;           // not in the JSON (not public): set when the deserializer sets Printed
        public bool Printed
        {
            get { return printed; }
            set { printed = value; printedRead = true; }
        }

        // idea 23: repeat print of an earlier record (its Id); "" = not a repeat. Never merged with the original (SameQuote).
        public string RepeatOf = "";

        // ideas 24 + 11: the printer the quote was made for (AppSettings.ActivePrinterId when saved) and a snapshot of
        // its name and model. "" in records saved before several printers = printer "1" (History.PrinterOf).
        public string PrinterId = "";
        public string PrinterName = "";
        public string PrinterModel = "";

        // invoice (History.MakeInvoice): number like "2026-006" ("" = no invoice yet) and the date it was made (TimeFormat)
        public string InvoiceNo = "";
        public string BilledAt = "";

        // invoice snapshot, made with a new number (MakeInvoice), so a re-issued invoice shows what the customer got.
        // "" in older records (History.InvoiceInfo then uses today's settings). InvoiceDue set = snapshot exists.
        public string InvoiceDue = "";             // "yyyy-MM-dd"
        public string InvoicePayMobilePay = "";
        public string InvoicePayBank = "";
        public string InvoiceFromName = "";        // AppSettings.QuoteName at the time
    }

    /// What an invoice shows besides the prices (History.InvoiceInfo): its saved snapshot, else today's settings.
    public class InvoiceDetails
    {
        public DateTime Due;                       // date only
        public string MobilePay = "";
        public string Bank = "";
        public string FromName = "";
        public bool FromSnapshot;                  // true = saved with the invoice; false = today's settings
    }

    /// One month of the price history (History.MonthSummary). kr. as saved; hours as decimal hours.
    public class MonthTotals
    {
        public int Year, Month;
        public int Prints;                   // Printed and not Failed
        public int FailedPrints;             // Failed (Printed or not)
        public double PrintHours;            // prints only
        public double Grams;                 // prints only
        public double CustomerIncome; public int CustomerCount;
        public double FriendsIncome; public int FriendsCount;
        public double Income;                // CustomerIncome + FriendsIncome (PriceFor: incl. failed-% share and payback)
        public double MaterialCost, PowerCost, WearCost;   // prints only; wear = hours x MachineRatePerHour
        public double Costs;                 // MaterialCost + PowerCost + WearCost
        public double Lost;                  // failed prints: sum of History.FailedCost
        public double Profit;                // Income - Costs - Lost
        public double ProfitPct;             // Profit / Income x 100; 0 when Income is 0
    }

    /// One printer's totals (History.PrinterTotals): MonthTotals (Prints = print count, Profit, ...) plus the printer.
    public class PrinterTotals : MonthTotals
    {
        public string Id = "";
        public string Name = "";
        public string Model = "";
        public bool Removed;                 // no longer in settings: id/name from its history records
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
        public const int MaxQuotes = 5000;
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
            else   // one colour: one line with the single-filament numbers Pricing used (ui fills its Name/Colour/Type)
                r.Filaments = new List<HistoryFilament> {
                    new HistoryFilament { Grams = input.FilamentGrams, PricePerKg = input.FilamentDkkPerKg } };
            r.ElectricityDkkPerKwh = input.ElectricityDkkPerKwh;

            r.PrinterId = AppSettings.ValidPrinterId(s.ActivePrinterId) ? s.ActivePrinterId : FirstPrinterId;
            r.PrinterName = AppSettings.CleanPrinterName(s.PrinterName);
            r.PrinterModel = Wear.FindModel(s.PrinterModel).Key;
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
            r.PaybackCost = result.PaybackCost;
            r.FailedPaybackPct = s.FailedPaybackPct;
            r.FailedPaybackWho = s.FailedPaybackWho == 1 ? 1 : 2;
            return r;
        }

        /// The inputs of a record, ready for Pricing.Calculate (with SavedSettings: the price as it was) or for filling the
        /// main window. PaybackDebt = the record's own PaybackCost, so the old payback is reproduced; a new quote must set
        /// its own PaybackDebt from FailedDebt. Filaments only for 2+ lines: a one-colour record (Filaments null or 1 line)
        /// prices from Grams / FilamentDkkPerKg exactly as before; read its name/colour from r.Filaments[0] (SingleLine).
        public static QuoteInput ToInput(QuoteRecord r)
        {
            var q = new QuoteInput
            {
                Hours = r.Hours, Minutes = r.Minutes, FilamentGrams = r.Grams,
                FilamentDkkPerKg = r.FilamentDkkPerKg, ElectricityDkkPerKwh = r.ElectricityDkkPerKwh,
                PaybackDebt = r.PaybackCost
            };
            if (r.Filaments != null && r.Filaments.Count >= 2)
            {
                q.Filaments = new List<FilamentLine>(r.Filaments.Count);
                foreach (var f in r.Filaments)
                    q.Filaments.Add(new FilamentLine { Grams = f.Grams, PricePerKg = f.PricePerKg });
            }
            return q;
        }

        /// The one colour line of a one-colour record (Name, Colour, Type), or null for old records (Filaments null)
        /// and multi-colour ones (2+ lines).
        public static HistoryFilament SingleLine(QuoteRecord r)
        {
            if (r == null || r.Filaments == null || r.Filaments.Count != 1) return null;
            return r.Filaments[0];
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
            s.FailedPayback = r.PaybackCost > 0 ? 1 : 0;
            s.FailedPaybackPct = r.FailedPaybackPct;
            s.FailedPaybackWho = r.FailedPaybackWho == 1 ? 1 : 2;
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
            // a repeat print is never the same as its original (or as a repeat of something else)
            string ra = a.RepeatOf ?? "", rb = b.RepeatOf ?? "";
            if (ra != rb) return false;
            if (ra.Length > 0 && (ra == (b.Id ?? "") || rb == (a.Id ?? ""))) return false;
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
        /// Id, For, Note, Failed mark and Printed tick (Printed if either was; r's own Customer is kept). A repeat print
        /// (RepeatOf) never replaces its original. Then trims to MaxQuotes. Returns the record now in the list.
        public static QuoteRecord Add(List<QuoteRecord> list, QuoteRecord r)
        {
            if (list.Count > 0 && SameQuote(list[0], r))
            {
                var old = list[0];
                if (r.Customer == null) r.Customer = "";
                r.Id = old.Id; r.For = old.For; r.Note = old.Note; r.Failed = old.Failed; r.FailedAt = old.FailedAt;
                r.PaidBack = old.PaidBack;
                if (!string.IsNullOrEmpty(old.InvoiceNo))
                {
                    r.InvoiceNo = old.InvoiceNo; r.BilledAt = old.BilledAt ?? "";
                    r.InvoiceDue = old.InvoiceDue ?? ""; r.InvoicePayMobilePay = old.InvoicePayMobilePay ?? "";
                    r.InvoicePayBank = old.InvoicePayBank ?? ""; r.InvoiceFromName = old.InvoiceFromName ?? "";
                }
                r.Printed = old.Printed || r.Printed;
                list[0] = r;
            }
            else list.Insert(0, r);
            Trim(list, MaxQuotes);
            return r;
        }

        /// Customer names used before: distinct (trimmed, case-insensitive; first spelling kept), newest first
        /// (list order, index 0 = newest), empty names skipped, at most max.
        public static List<string> RecentCustomers(List<QuoteRecord> list, int max)
        {
            var res = new List<string>();
            if (list == null) return res;
            foreach (var q in list)
            {
                if (res.Count >= max) break;
                if (q == null || q.Customer == null) continue;
                string n = q.Customer.Trim();
                if (n.Length == 0) continue;
                bool dup = false;
                foreach (var x in res)
                    if (string.Equals(x, n, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                if (!dup) res.Add(n);
            }
            return res;
        }

        /// Quote export: Add, then For = forWho (ForFriends / ForCustomer; anything else = ForCustomer), also when Add
        /// replaced a same newest quote (whose old For would otherwise be kept). Returns the record now in the list.
        public static QuoteRecord AddFor(List<QuoteRecord> list, QuoteRecord r, int forWho)
        {
            var kept = Add(list, r);
            kept.For = forWho == ForFriends ? ForFriends : ForCustomer;
            return kept;
        }

        /// Quote number from the saved time: "yyMMdd-HHmm" (2026-11-04 14:05:33 -> "261104-1405"); "" if SavedAt is bad.
        public static string QuoteNumber(QuoteRecord r)
        {
            DateTime t;
            if (r == null || !TryTime(r.SavedAt, out t)) return "";
            return QuoteNumber(t);
        }

        public static string QuoteNumber(DateTime t) { return t.ToString("yyMMdd-HHmm", Inv); }

        // ---------- invoices ----------

        /// Characters of an invoice code: upper case, no 0/O, 1/I/L or 5/S (easy to misread). 29 characters.
        public const string InvoiceAlphabet = "ABCDEFGHJKMNPQRTUVWXYZ2346789";
        public const int InvoiceCodeLength = 4;
        public const int MaxUsedInvoiceNos = 2000;

        static readonly System.Security.Cryptography.RandomNumberGenerator crypto = System.Security.Cryptography.RandomNumberGenerator.Create();

        /// Crypto-random int in [0, max) without modulo bias.
        static int CryptoIndex(int max)
        {
            var b = new byte[4];
            uint limit = uint.MaxValue - (uint.MaxValue % (uint)max);
            uint v;
            lock (crypto)
                do { crypto.GetBytes(b); v = BitConverter.ToUInt32(b, 0); } while (v >= limit);
            return (int)(v % (uint)max);
        }

        /// A new invoice number "<year>-<4 random characters>" (e.g. "2026-K7Q4"), different from every InvoiceNo in list
        /// and from every code in usedCsv (comma-separated, AppSettings.UsedInvoiceNos). Crypto-random.
        public static string NextInvoiceNo(List<QuoteRecord> list, int year, string usedCsv)
        {
            return NextInvoiceNo(list, year, usedCsv, null);
        }

        /// As above with an own random source: pick(n) returns an int in [0, n) (null = crypto). For tests.
        public static string NextInvoiceNo(List<QuoteRecord> list, int year, string usedCsv, Func<int, int> pick)
        {
            if (pick == null) pick = CryptoIndex;
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (list != null)
                foreach (var r in list)
                    if (r != null && !string.IsNullOrEmpty(r.InvoiceNo)) taken.Add(r.InvoiceNo.Trim());
            foreach (var u in (usedCsv ?? "").Split(','))
                if (u.Trim().Length > 0) taken.Add(u.Trim());
            string prefix = year.ToString("0000", Inv) + "-";
            int len = InvoiceCodeLength;
            for (int attempt = 1; ; attempt++)
            {
                if (attempt % 1000 == 0) len++;   // (practically never) a full code space: longer codes
                var sb = new StringBuilder(prefix);
                for (int i = 0; i < len; i++) sb.Append(InvoiceAlphabet[pick(InvoiceAlphabet.Length)]);
                string no = sb.ToString();
                if (!taken.Contains(no)) return no;
            }
        }

        /// Adds a code to a comma-separated used list, keeping the last MaxUsedInvoiceNos.
        public static string AddUsedInvoiceNo(string usedCsv, string no)
        {
            var parts = new List<string>();
            foreach (var u in (usedCsv ?? "").Split(','))
                if (u.Trim().Length > 0) parts.Add(u.Trim());
            if (!string.IsNullOrEmpty(no)) parts.Add(no.Trim());
            if (parts.Count > MaxUsedInvoiceNos) parts.RemoveRange(0, parts.Count - MaxUsedInvoiceNos);
            return string.Join(",", parts.ToArray());
        }

        /// Makes the invoice for r: with no InvoiceNo yet it gets a new random code (NextInvoiceNo, year of now),
        /// BilledAt = Stamp(now) and Printed = true, and the code is added to s.UsedInvoiceNos (s may be null; the caller
        /// saves the settings too). A new number also saves the snapshot from s: InvoiceDue (s.InvoiceDue(now),
        /// "yyyy-MM-dd"), InvoicePayMobilePay, InvoicePayBank, InvoiceFromName (QuoteName); s null = no snapshot.
        /// With a number it keeps the same number, date and snapshot. Returns the number. Does not save.
        public static string MakeInvoice(List<QuoteRecord> list, QuoteRecord r, DateTime now, AppSettings s)
        {
            return MakeInvoice(list, r, now, s, null);
        }

        public static string MakeInvoice(List<QuoteRecord> list, QuoteRecord r, DateTime now, AppSettings s, Func<int, int> pick)
        {
            if (r == null) throw new ArgumentNullException();
            if (string.IsNullOrEmpty(r.InvoiceNo))
            {
                r.InvoiceNo = NextInvoiceNo(list, now.Year, s == null ? "" : s.UsedInvoiceNos, pick);
                if (s != null) s.UsedInvoiceNos = AddUsedInvoiceNo(s.UsedInvoiceNos, r.InvoiceNo);
                r.BilledAt = Stamp(now);
                r.Printed = true;
                if (s != null)
                {
                    r.InvoiceDue = s.InvoiceDue(now).ToString(DueFormat, Inv);
                    r.InvoicePayMobilePay = s.PayMobilePay ?? "";
                    r.InvoicePayBank = s.PayBank ?? "";
                    r.InvoiceFromName = s.QuoteName ?? "";
                }
            }
            return r.InvoiceNo;
        }

        public const string DueFormat = "yyyy-MM-dd";

        /// Due date, MobilePay, bank and "from" name for r's invoice: the snapshot saved with it (r.InvoiceDue is a valid
        /// date) or, for an old invoice / one not made yet, today's settings s (null = defaults): due = s.InvoiceDue(BilledAt,
        /// or today when not billed). Never null.
        public static InvoiceDetails InvoiceInfo(QuoteRecord r, AppSettings s)
        {
            return InvoiceInfo(r, s, DateTime.Now);
        }

        /// As above with "today" given (tests).
        public static InvoiceDetails InvoiceInfo(QuoteRecord r, AppSettings s, DateTime today)
        {
            var d = new InvoiceDetails();
            DateTime due;
            if (r != null && !string.IsNullOrEmpty(r.InvoiceDue) &&
                DateTime.TryParseExact(r.InvoiceDue.Trim(), DueFormat, Inv, DateTimeStyles.None, out due))
            {
                d.Due = due.Date;
                d.MobilePay = r.InvoicePayMobilePay ?? "";
                d.Bank = r.InvoicePayBank ?? "";
                d.FromName = r.InvoiceFromName ?? "";
                d.FromSnapshot = true;
                return d;
            }
            if (s == null) s = new AppSettings();
            DateTime billed;
            if (r == null || string.IsNullOrEmpty(r.InvoiceNo) || !TryTime(r.BilledAt, out billed)) billed = today;
            d.Due = s.InvoiceDue(billed);
            d.MobilePay = s.PayMobilePay ?? "";
            d.Bank = s.PayBank ?? "";
            d.FromName = s.QuoteName ?? "";
            return d;
        }

        /// Keeps the newest max entries (list is newest first). Older failed ones not yet made back are kept too; what the
        /// dropped quotes paid towards them goes into their PaidBack, so FailedDebt is the same before and after.
        public static void Trim(List<QuoteRecord> list, int max)
        {
            if (max < 0) max = 0;
            if (list.Count <= max) return;
            var remaining = new double[list.Count];
            var byDropped = new double[list.Count];
            RunDebt(list, null, max, remaining, byDropped);
            var keep = new List<QuoteRecord>(max + 8);
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r == null) continue;
                if (r.Failed && byDropped[i] > 0) r.PaidBack = Math.Max(0, r.PaidBack) + byDropped[i];
                if (i < max) { keep.Add(r); continue; }
                // beyond max: only failed ones that dropped quotes haven't fully paid back
                if (r.Failed && FailedCost(r) - Math.Max(0, r.PaidBack) > 1e-9) keep.Add(r);
            }
            list.Clear();
            list.AddRange(keep);
        }

        // ---------- failed prints payback (idea 9) ----------

        /// What a failed print cost: material + power + wear for its print hours (no markup, no failed-% share).
        public static double FailedCost(QuoteRecord r)
        {
            if (r == null) return 0;
            return r.MaterialCost + r.PowerCost + WearCost(r);
        }

        /// Wear of a record: print hours x its saved MachineRatePerHour (0 for null).
        public static double WearCost(QuoteRecord r)
        {
            if (r == null) return 0;
            return (r.Hours + r.Minutes / 60.0) * r.MachineRatePerHour;
        }

        // ---------- month summary (idea 22) ----------

        /// The year and month a record was saved in; false if SavedAt is unreadable.
        static bool SavedMonth(QuoteRecord r, out int year, out int month)
        {
            DateTime t;
            year = 0; month = 0;
            if (r == null || !TryTime(r.SavedAt, out t)) return false;
            year = t.Year; month = t.Month;
            return true;
        }

        /// Totals of the records saved in that month (by SavedAt). Prints = Printed and not Failed: they give income
        /// (PriceFor by For) and costs (material + power + wear). Failed ones (ticked Printed or not) give Lost
        /// (FailedCost). Not printed, not failed quotes count for nothing. Empty month / null list = all 0.
        public static MonthTotals MonthSummary(List<QuoteRecord> list, int year, int month)
        {
            return MonthSummary(list, year, month, null);
        }

        /// MonthSummary for one printer (PrinterOf(r) == printerId; null = all printers). year 0 = all time (every
        /// record, month ignored).
        public static MonthTotals MonthSummary(List<QuoteRecord> list, int year, int month, string printerId)
        {
            var m = new MonthTotals { Year = year, Month = year == 0 ? 0 : month };
            Sum(m, list, year, month, printerId);
            return m;
        }

        static void Sum(MonthTotals m, List<QuoteRecord> list, int year, int month, string printerId)
        {
            if (list != null)
                foreach (var r in list)
                {
                    if (r == null) continue;
                    if (printerId != null && PrinterOf(r) != printerId) continue;
                    int y, mo;
                    if (year != 0 && (!SavedMonth(r, out y, out mo) || y != year || mo != month)) continue;
                    if (r.Failed) { m.FailedPrints++; m.Lost += FailedCost(r); continue; }
                    if (!r.Printed) continue;
                    m.Prints++;
                    m.PrintHours += r.Hours + r.Minutes / 60.0;
                    m.Grams += r.Grams;
                    double price = PriceFor(r);
                    if (r.For == ForFriends) { m.FriendsIncome += price; m.FriendsCount++; }
                    else { m.CustomerIncome += price; m.CustomerCount++; }
                    m.MaterialCost += r.MaterialCost;
                    m.PowerCost += r.PowerCost;
                    m.WearCost += WearCost(r);
                }
            m.Income = m.CustomerIncome + m.FriendsIncome;
            m.Costs = m.MaterialCost + m.PowerCost + m.WearCost;
            m.Profit = m.Income - m.Costs - m.Lost;
            m.ProfitPct = Math.Abs(m.Income) < 1e-9 ? 0 : m.Profit / m.Income * 100;
        }

        // ---------- several printers (ideas 24 + 11) ----------

        /// The id old records (saved before several printers) belong to: the first and only printer back then.
        public const string FirstPrinterId = "1";

        /// The printer id of a record; "" (old record) = FirstPrinterId.
        public static string PrinterOf(QuoteRecord r)
        {
            if (r == null || string.IsNullOrEmpty(r.PrinterId)) return FirstPrinterId;
            return r.PrinterId;
        }

        /// Reserves every printer id used in the history (AppSettings.ReservePrinterId), so a new printer never gets the
        /// id of a removed or lost one (e.g. after an older version rewrote settings.ini without the printer lines).
        /// True when settings.LastPrinterId went up. Doesn't save.
        public static bool ReservePrinterIds(List<QuoteRecord> list, AppSettings settings)
        {
            bool changed = false;
            if (list == null || settings == null) return false;
            foreach (var r in list)
                if (r != null && settings.ReservePrinterId(PrinterOf(r))) changed = true;
            return changed;
        }

        /// Totals per printer (same rules as MonthSummary; year 0 = all time). First every printer in settings, in
        /// order (also with no records: all 0), with today's name; then printers no longer in settings that have records
        /// in the period (Removed = true; name/model = the newest record's snapshot, else "Bambu Lab A1" / A1).
        /// The rows add up to MonthSummary(list, year, month). settings null = only printers found in the records.
        public static List<PrinterTotals> PrinterTotals(List<QuoteRecord> list, AppSettings settings, int year, int month)
        {
            var res = new List<PrinterTotals>();
            var seen = new Dictionary<string, PrinterTotals>(StringComparer.Ordinal);
            if (settings != null)
                foreach (var p in settings.Printers)
                {
                    var t = new PrinterTotals { Id = p.Id, Name = p.Name, Model = p.Model };
                    res.Add(t);
                    seen[p.Id] = t;
                }
            if (list != null)
                foreach (var r in list)   // newest first: the first record seen gives a removed printer's name
                {
                    if (r == null) continue;
                    int y, mo;
                    if (year != 0 && (!SavedMonth(r, out y, out mo) || y != year || mo != month)) continue;
                    string id = PrinterOf(r);
                    if (seen.ContainsKey(id)) continue;
                    var t = new PrinterTotals
                    {
                        Id = id, Removed = settings != null,
                        Name = AppSettings.CleanPrinterName(r.PrinterName),
                        Model = Wear.FindModel(r.PrinterModel).Key
                    };
                    res.Add(t);
                    seen[id] = t;
                }
            foreach (var t in res)
            {
                t.Year = year; t.Month = year == 0 ? 0 : month;
                Sum(t, list, year, month, t.Id);
            }
            return res;
        }

        /// The months that have records (any kind, readable SavedAt), as the 1st of each month, newest first, no doubles.
        public static List<DateTime> Months(List<QuoteRecord> list)
        {
            var res = new List<DateTime>();
            if (list == null) return res;
            foreach (var r in list)
            {
                int y, mo;
                if (!SavedMonth(r, out y, out mo)) continue;
                var d = new DateTime(y, mo, 1);
                if (!res.Contains(d)) res.Add(d);
            }
            res.Sort(delegate(DateTime a, DateTime b) { return b.CompareTo(a); });
            return res;
        }

        /// True if r's PaybackCost counts against the debt: not failed, and it was in both prices or the quote is for a customer.
        public static bool PaysBack(QuoteRecord r)
        {
            return r != null && !r.Failed && r.PaybackCost > 0 && (r.FailedPaybackWho == 1 || r.For != ForFriends);
        }

        /// Failed prints not made back yet (kr., >= 0). Timeline oldest first: a failure adds FailedCost - PaidBack at
        /// FailedAt (SavedAt if unreadable), a paying quote (PaysBack) subtracts its PaybackCost at SavedAt, paying the
        /// oldest open failure first; payback with nothing open is lost (no credit). excludeId (null/"" = none): that
        /// record's payback is left out (the quote the current job saved, so saving it again gives the same price).
        public static double FailedDebt(List<QuoteRecord> list, string excludeId)
        {
            if (list == null || list.Count == 0) return 0;
            return RunDebt(list, excludeId, int.MaxValue, new double[list.Count], new double[list.Count]);
        }

        class DebtEvent
        {
            public DateTime T;
            public int Kind;     // 0 = failure, 1 = payback (failures first at the same time)
            public int Index;    // in list (newest first)
            public double Amount;
        }

        static DateTime EventTime(string primary, string fallback)
        {
            DateTime t;
            if (TryTime(primary, out t)) return t;
            if (TryTime(fallback, out t)) return t;
            return DateTime.MinValue;
        }

        // remaining[i] = left of failure i; byDropped[i] = paid to failure i by records with index >= dropFrom. Returns the debt.
        static double RunDebt(List<QuoteRecord> list, string excludeId, int dropFrom, double[] remaining, double[] byDropped)
        {
            bool exclude = !string.IsNullOrEmpty(excludeId);
            var ev = new List<DebtEvent>();
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r == null) continue;
                if (r.Failed)
                {
                    double cost = FailedCost(r) - Math.Max(0, r.PaidBack);
                    if (cost > 0) ev.Add(new DebtEvent { T = EventTime(r.FailedAt, r.SavedAt), Kind = 0, Index = i, Amount = cost });
                }
                else if (PaysBack(r) && !(exclude && r.Id == excludeId))
                    ev.Add(new DebtEvent { T = EventTime(r.SavedAt, null), Kind = 1, Index = i, Amount = r.PaybackCost });
            }
            ev.Sort(delegate(DebtEvent a, DebtEvent b)
            {
                int c = a.T.CompareTo(b.T);
                if (c != 0) return c;
                if (a.Kind != b.Kind) return a.Kind.CompareTo(b.Kind);
                return b.Index.CompareTo(a.Index);   // older (higher index) first
            });
            var open = new List<int>();
            foreach (var e in ev)
            {
                if (e.Kind == 0) { remaining[e.Index] = e.Amount; open.Add(e.Index); continue; }
                double x = e.Amount;
                while (x > 1e-12 && open.Count > 0)
                {
                    int f = open[0];
                    double pay = Math.Min(x, remaining[f]);
                    remaining[f] -= pay;
                    if (e.Index >= dropFrom) byDropped[f] += pay;
                    x -= pay;
                    if (remaining[f] <= 1e-9) { remaining[f] = 0; open.RemoveAt(0); }
                }
            }
            double debt = 0;
            foreach (int f in open) debt += remaining[f];
            return debt;
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

        public static string ToJson(List<QuoteRecord> list)
        {
            return Json.Serialize(new HistoryFile { Version = Version, Quotes = list ?? new List<QuoteRecord>() });
        }

        /// The quotes, or null if the text isn't a history file of this version (garbage, empty, newer version).
        public static List<QuoteRecord> ParseJson(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Trim().Length == 0) return null;
            HistoryFile f;
            try { f = Json.Deserialize<HistoryFile>(json); }
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
                if (r.Customer == null) r.Customer = "";
                if (r.FailedAt == null) r.FailedAt = "";
                if (r.RepeatOf == null) r.RepeatOf = "";
                if (r.PrinterId == null) r.PrinterId = "";
                if (r.PrinterName == null) r.PrinterName = "";
                if (r.PrinterModel == null) r.PrinterModel = "";
                if (r.InvoiceNo == null) r.InvoiceNo = "";
                if (r.BilledAt == null) r.BilledAt = "";
                if (r.InvoiceDue == null) r.InvoiceDue = "";
                if (r.InvoicePayMobilePay == null) r.InvoicePayMobilePay = "";
                if (r.InvoicePayBank == null) r.InvoicePayBank = "";
                if (r.InvoiceFromName == null) r.InvoiceFromName = "";
                if (!r.printedRead) r.Printed = true;   // saved before the Printed tick existed: those were printed
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
                    if (r.Filaments.Count == 0) r.Filaments = null;   // 1 line = a one-colour quote (name/colour kept)
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

#if !WEB
    /// All JSON goes through here (History.ToJson / ParseJson). Desktop: JavaScriptSerializer (System.Web.Extensions).
    /// The website builds with WEB defined and has its own Json class (System.Text.Json) with the same two methods;
    /// it must read and write history.json exactly like this one (public fields and properties, same names).
    public static class Json
    {
        static JavaScriptSerializer Make()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        public static string Serialize(object value) { return Make().Serialize(value); }

        /// Throws on text that isn't valid JSON for T (callers catch).
        public static T Deserialize<T>(string json) { return Make().Deserialize<T>(json); }
    }
#endif
}
