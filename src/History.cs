using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#if !WEB
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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

        internal object MemberwiseCopy() { return MemberwiseClone(); }   // History.CopyQuotes
    }

    /// Idea 21: one model file of a job sliced from several plain model files, with the filament and colour the user
    /// picked for it (the file picker's ColourChoice), so Open again / Repeat print can slice it the same way.
    public class HistoryPart
    {
        public string File = "";       // the file as given to the slicer (path or name)
        public string Filament = "";   // short preset name as in Settings, "" = Settings' filament
        public string Colour = "";     // "#RRGGBB", "" = the profile's own colour
        public int Slot = 1;           // filament slot (1-based); files with the same slot share one colour line;
                                       // 0 = a .3mf project that keeps the file's own colours (no colour line of its own)
    }

    /// Idea 21: the files of a several-files job (QuoteRecord.Parts; null = a normal job, old records).
    public class HistoryParts
    {
        public bool Together;                  // "together on one plate" was ticked
        public List<HistoryPart> Files;        // in the picker's order; never empty after History.CleanQuotes

        internal HistoryParts Copy()
        {
            var c = new HistoryParts { Together = Together };
            if (Files != null)
            {
                c.Files = new List<HistoryPart>(Files.Count);
                foreach (var f in Files)
                    c.Files.Add(f == null ? null : new HistoryPart { File = f.File, Filament = f.Filament, Colour = f.Colour, Slot = f.Slot });
            }
            return c;
        }
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

        // idea 21: several model files with a filament / colour each (History.SetParts / PartsOf); null = none (old records,
        // one file, links, typed jobs). Older app versions ignore the key.
        public HistoryParts Parts;

        internal object MemberwiseCopy() { return MemberwiseClone(); }   // History.CopyQuotes
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

    /// A backup file's top level (idea 27, History.ToBackup): {"Format":"PrintPriceBackup","Version":1,"CreatedAt":...,
    /// "App":"1.10.0","History":{HistoryFile},"Settings":[settings.ini lines]}. Settings null = a history-only backup.
    public class BackupFile
    {
        public string Format;
        public int Version;
        public string CreatedAt;
        public string App;
        public HistoryFile History;
        public List<string> Settings;
    }

    /// Reading only (History.ReadBackup): a backup file (BackupFile) or a plain history.json (HistoryFile: Version + Quotes)
    /// in one parse. Never written.
    public class BackupFileRead : BackupFile
    {
        public List<QuoteRecord> Quotes;
        // no Pictures here on purpose: a broken "Pictures" part must never fail the records (History.ReadPictures reads it
        // loosely in a second pass)
    }

    /// A backup with record pictures (issue #4): BackupFile + "Pictures":[{"Id":..,"Data":"data:image/png;base64,.."}].
    /// Only written when there are pictures, so a backup without pictures is the same text as before. Older apps ignore
    /// the extra member (Version stays 1).
    public class BackupFileWithPictures : BackupFile
    {
        public List<BackupPicture> Pictures;
    }

    /// One record's small picture in a backup: the record's Id and a data URL (the string the website keeps under "pic:<Id>").
    public class BackupPicture
    {
        public string Id;
        public string Data;
    }

    /// A read backup (History.ReadBackup): the records (cleaned like Load does) and the settings.ini lines (null = none:
    /// a plain history.json or a history-only backup).
    public class BackupData
    {
        public List<QuoteRecord> Quotes = new List<QuoteRecord>();
        public string[] Settings;
        public string CreatedAt = "";
        public string App = "";
        /// Record Id -> picture bytes (PNG / JPEG / WebP, checked); empty = none (old backup, plain history.json).
        public Dictionary<string, byte[]> Pictures = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    }

    /// What a restore gives (History.Combine / Restore): the new history and settings, and counts for the message.
    public class RestoreResult
    {
        public List<QuoteRecord> Quotes = new List<QuoteRecord>();
        public AppSettings Settings;
        public bool SettingsRestored;     // the backup's settings were used (asked for and in the backup)
        public int Added;                 // records from the backup that weren't here (Replace: all of them)
        public int Updated;               // same record here, filled in from the backup (invoice, Printed, Failed, Note, Customer)
        public int Same;                  // same record here, nothing new
        public int PrintersMoved;         // printer ids moved to a new id (same id, different model)
        public List<string> InvoiceClashes = new List<string>();   // invoice codes now on 2+ different records (kept as they are)
        /// Pictures to write (record Id -> bytes): replace = the backup's; merge = added records' and records that have none here.
        public Dictionary<string, byte[]> Pictures = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    }

    /// Price history: saved quotes in history.json next to settings.ini. Local only, never sent anywhere.
    public static partial class History
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

        // ---------- several model files with a colour each (idea 21) ----------

        /// Highest filament slot a part may have (larger ones are read as this).
        public const int MaxPartSlot = 64;

        /// Stores the files of a several-files job on r (own copies, cleaned like a read file: blank file names dropped,
        /// null texts "", Colour "#RRGGBB" upper case or "", Slot 0..MaxPartSlot). null / no files left = r.Parts null.
        public static void SetParts(QuoteRecord r, IList<HistoryPart> files, bool together)
        {
            if (r == null) return;
            if (files == null) { r.Parts = null; return; }
            var p = new HistoryParts { Together = together, Files = new List<HistoryPart>(files.Count) };
            foreach (var f in files)
                if (f != null) p.Files.Add(new HistoryPart { File = f.File, Filament = f.Filament, Colour = f.Colour, Slot = f.Slot });
            r.Parts = CleanParts(p);
        }

        /// The files of a several-files job (copies, picker order), or null for a normal job (old records too).
        public static List<HistoryPart> PartsOf(QuoteRecord r)
        {
            if (r == null || r.Parts == null || r.Parts.Files == null || r.Parts.Files.Count == 0) return null;
            return r.Parts.Copy().Files;
        }

        /// True when the job's files were sliced together on one plate (false for a normal job).
        public static bool PartsTogether(QuoteRecord r)
        {
            return PartsOf(r) != null && r.Parts.Together;
        }

        /// The colour choice per slot: index i = slot i+1 (Filament / Colour of the first file with that slot, File "";
        /// a slot no file uses = "" / ""), up to the highest slot used. null for a normal job. ui maps it to ColourChoice.
        /// Files in slot 0 (a .3mf project keeping its own colours) have no colour line and are skipped.
        public static List<HistoryPart> PartSlots(QuoteRecord r)
        {
            var files = PartsOf(r);
            if (files == null) return null;
            int max = 1;
            foreach (var f in files) if (f.Slot > max) max = f.Slot;
            var res = new List<HistoryPart>(max);
            var seen = new bool[max];
            for (int i = 0; i < max; i++) res.Add(new HistoryPart { Slot = i + 1 });
            foreach (var f in files)
            {
                if (f.Slot < 1) continue;   // slot 0: the project's own colours
                int i = f.Slot - 1;
                if (seen[i]) continue;
                seen[i] = true;
                res[i].Filament = f.Filament; res[i].Colour = f.Colour;
            }
            return res;
        }

        // a read or set Parts: null, no Files or no file with a name left -> null
        static HistoryParts CleanParts(HistoryParts p)
        {
            if (p == null || p.Files == null) return null;
            var files = new List<HistoryPart>(p.Files.Count);
            foreach (var f in p.Files)
            {
                if (f == null || f.File == null || f.File.Trim().Length == 0) continue;
                f.Filament = f.Filament == null ? "" : f.Filament.Trim();
                f.Colour = CleanPartColour(f.Colour);
                // Slot 0 = a .3mf project part that keeps the file's own colours (idea 21): kept as 0.
                // Negative = damaged -> 1; a stored part without "Slot" reads as the field's default 1.
                if (f.Slot < 0) f.Slot = 1;
                if (f.Slot > MaxPartSlot) f.Slot = MaxPartSlot;
                files.Add(f);
            }
            if (files.Count == 0) return null;
            p.Files = files;
            return p;
        }

        // "#RRGGBB" (any case, spaces around) -> upper case; anything else -> ""
        static string CleanPartColour(string c)
        {
            if (c == null) return "";
            c = c.Trim();
            if (c.Length != 7 || c[0] != '#') return "";
            for (int i = 1; i < 7; i++)
                if (!Uri.IsHexDigit(c[i])) return "";
            return c.ToUpperInvariant();
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

        /// Every month with records (as Months(list), newest first), each equal to MonthSummary(list, year, month), in one
        /// pass over the list (each SavedAt read once). HistoryForm's "All months" cards add these up.
        public static List<MonthTotals> MonthSummaries(List<QuoteRecord> list)
        {
            var res = new List<MonthTotals>();
            if (list == null) return res;
            var byMonth = new Dictionary<int, MonthTotals>();
            foreach (var r in list)
            {
                int y, mo;
                if (!SavedMonth(r, out y, out mo)) continue;
                MonthTotals m;
                if (!byMonth.TryGetValue(y * 12 + mo, out m))
                {
                    m = new MonthTotals { Year = y, Month = mo };
                    byMonth[y * 12 + mo] = m;
                    res.Add(m);
                }
                AddRecord(m, r);
            }
            foreach (var m in res) Finish(m);
            res.Sort(delegate(MonthTotals a, MonthTotals b) { return (b.Year * 12 + b.Month).CompareTo(a.Year * 12 + a.Month); });
            return res;
        }

        /// The "All months" cards (HistoryForm, website): the sum of every month's MonthSummaries, Profit % from the sums.
        public static MonthTotals AllMonthsTotals(List<QuoteRecord> list)
        {
            var t = new MonthTotals();
            foreach (var x in MonthSummaries(list))
            {
                t.Prints += x.Prints; t.FailedPrints += x.FailedPrints; t.PrintHours += x.PrintHours; t.Grams += x.Grams;
                t.CustomerIncome += x.CustomerIncome; t.CustomerCount += x.CustomerCount;
                t.FriendsIncome += x.FriendsIncome; t.FriendsCount += x.FriendsCount;
                t.Income += x.Income; t.Costs += x.Costs; t.Lost += x.Lost; t.Profit += x.Profit;
            }
            t.ProfitPct = Math.Abs(t.Income) < 1e-9 ? 0 : t.Profit / t.Income * 100;
            return t;
        }

        // ---------- the History list's name and filter (HistoryForm and the website's History page) ----------

        /// "Show" choices: all quotes, friends & family, customers, failed, not printed.
        public const int ShowAll = 0, ShowFriends = 1, ShowCustomers = 2, ShowFailed = 3, ShowNotPrinted = 4;

        /// The model's name, else the file name of the link/file (after the last \ or / for a file path, the whole link
        /// for a web address), else "(no name)".
        public static string ModelText(QuoteRecord r)
        {
            if (!string.IsNullOrEmpty(r.Model)) return r.Model;
            string s = (r.Source ?? "").Trim().Trim('"');
            if (s.Length == 0) return L.S("(no name)", "(uden navn)");
            if (s.IndexOf('\\') >= 0 || s.IndexOf('/') < 0)
            {
                int cut = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
                return cut >= 0 ? s.Substring(cut + 1) : s;
            }
            return s;
        }

        /// True when the record passes the list's filters: month (1st of it; MinValue = all months), printer id (null =
        /// all), Show (ShowAll…ShowNotPrinted) and the search text (model, link, note, name, customer, invoice number and,
        /// with printerText not null, the printer's name; case-insensitive).
        public static bool Matches(QuoteRecord r, DateTime month, string printerId, int show, string query, Func<QuoteRecord, string> printerText)
        {
            if (month != DateTime.MinValue)
            {
                DateTime t;
                if (!TryTime(r.SavedAt, out t) || t.Year != month.Year || t.Month != month.Month) return false;
            }
            if (printerId != null && PrinterOf(r) != printerId) return false;
            switch (show)
            {
                case ShowFriends: if (r.For != ForFriends) return false; break;
                case ShowCustomers: if (r.For == ForFriends) return false; break;
                case ShowFailed: if (!r.Failed) return false; break;
                case ShowNotPrinted: if (r.Printed) return false; break;
            }
            string q = (query ?? "").Trim();
            if (q.Length == 0) return true;
            foreach (var s in new[] { r.Model, r.Source, r.Note, ModelText(r), r.Customer, r.InvoiceNo, printerText != null ? printerText(r) : null })
                if (!string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            return false;
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
                    AddRecord(m, r);
                }
            Finish(m);
        }

        // one record into the totals (MonthSummary's rules)
        static void AddRecord(MonthTotals m, QuoteRecord r)
        {
            if (r.Failed) { m.FailedPrints++; m.Lost += FailedCost(r); return; }
            if (!r.Printed) return;
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

        // the derived totals, after the last AddRecord
        static void Finish(MonthTotals m)
        {
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
            return CleanQuotes(f.Quotes);
        }

        // the records of a read file: nulls dropped, missing strings "", old-file rules (Printed, For, empty Filaments)
        static List<QuoteRecord> CleanQuotes(List<QuoteRecord> quotes)
        {
            var list = new List<QuoteRecord>(quotes.Count);
            foreach (var r in quotes)
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
                r.Parts = CleanParts(r.Parts);
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
            return BackupName(path, "history.bad-", ".json", now);
        }

        /// <prefix>yyyyMMdd-HHmmss<ext> in path's folder, with -2, -3... if taken.
        public static string BackupName(string path, string prefix, string ext, DateTime now)
        {
            string dir = Path.GetDirectoryName(path);
            string stem = prefix + now.ToString("yyyyMMdd-HHmmss", Inv);
            string name = Path.Combine(dir, stem + ext);
            for (int n = 2; File.Exists(name); n++) name = Path.Combine(dir, stem + "-" + n.ToString(Inv) + ext);
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

        /// Tests only: as if history.json couldn't be read.
        internal static void SetBlock() { saveBlocked = true; }

        // ---------- record pictures (issue #4) ----------
        // One small picture per record in <Folder>\pictures\<Id>.png / .jpg (.webp only from a website backup), never in
        // history.json. The website keeps the same pictures as data URLs under "pic:<Id>" (IndexedDB).

        public const string PictureFolder = "pictures";
        public const int PictureSize = 256;                         // px, longest side (= WebHistory.PictureSize)
        public const int SmallPictureSize = 192;                    // second try when 256 px is over MaxPictureBytes
        public const int MaxPictureBytes = 96 * 1024;               // per picture, on write and on read
        public const long FilePictureBudget = 40L * 1024 * 1024;    // data URL characters in one backup file (web reads <= 64 MB)
        static readonly string[] PictureExtensions = { ".png", ".jpg", ".webp" };

        /// <Folder>\pictures (test mode: the test folder's).
        public static string PictureDir
        {
            get { return Path.Combine(AppSettings.Folder, PictureFolder); }
        }

        /// An Id that can be a file name: 1-64 characters, only 0-9 A-Z a-z _ - (History.FromQuote Ids are 21 of them).
        public static bool SafePictureId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id)
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || c == '-')) return false;
            return true;
        }

        /// "image/png", "image/jpeg", "image/webp" by the first bytes; null = none of them.
        public static string PictureMime(byte[] b)
        {
            if (b == null || b.Length < 12) return null;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
                return "image/webp";
            return null;
        }

        /// A usable picture: a known type and at most MaxPictureBytes.
        public static bool ValidPicture(byte[] b)
        {
            return b != null && b.Length <= MaxPictureBytes && PictureMime(b) != null;
        }

        /// "data:image/png;base64,..." (the website's format); null for no / invalid picture.
        public static string PictureDataUrl(byte[] b)
        {
            if (!ValidPicture(b)) return null;
            return "data:" + PictureMime(b) + ";base64," + Convert.ToBase64String(b);
        }

        /// The bytes of a data URL from PictureDataUrl or the website; null on anything wrong (other type, bad base64,
        /// type and bytes don't agree, too big). Never throws.
        public static byte[] PictureFromDataUrl(string url)
        {
            if (url == null || !url.StartsWith("data:", StringComparison.Ordinal)) return null;
            int comma = url.IndexOf(',');
            if (comma < 0) return null;
            string head = url.Substring(5, comma - 5);
            if (!head.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return null;
            string mime = head.Substring(0, head.Length - 7).ToLowerInvariant();
            if (mime != "image/png" && mime != "image/jpeg" && mime != "image/webp") return null;
            if (url.Length - comma - 1 > (MaxPictureBytes / 3 + 1) * 4) return null;
            byte[] b;
            try { b = Convert.FromBase64String(url.Substring(comma + 1)); }
            catch { return null; }
            if (!ValidPicture(b) || PictureMime(b) != mime) return null;
            return b;
        }

        static string PictureExtension(string mime)
        {
            return mime == "image/png" ? ".png" : mime == "image/jpeg" ? ".jpg" : mime == "image/webp" ? ".webp" : null;
        }

        /// Pictures for a backup, newest records first (by SavedAt), while the data URLs' total length stays within
        /// budget (a picture that doesn't fit is skipped, smaller later ones may still go in). load(id) = bytes or null.
        public static List<BackupPicture> PicturesForBackup(List<QuoteRecord> list, Func<string, byte[]> load, long budget)
        {
            var res = new List<BackupPicture>();
            if (list == null || load == null || budget <= 0) return res;
            var order = new List<QuoteRecord>();
            foreach (var r in list) if (r != null && SafePictureId(r.Id)) order.Add(r);
            var pos = new Dictionary<QuoteRecord, int>();
            for (int i = 0; i < order.Count; i++) pos[order[i]] = i;
            order.Sort(delegate(QuoteRecord a, QuoteRecord b)
            {
                DateTime ta, tb;
                if (!TryTime(a.SavedAt, out ta)) ta = DateTime.MinValue;
                if (!TryTime(b.SavedAt, out tb)) tb = DateTime.MinValue;
                int c = tb.CompareTo(ta);
                return c != 0 ? c : pos[a].CompareTo(pos[b]);
            });
            var seen = new HashSet<string>(StringComparer.Ordinal);
            long used = 0;
            foreach (var r in order)
            {
                if (!seen.Add(r.Id)) continue;
                byte[] b;
                try { b = load(r.Id); }
                catch { b = null; }
                string url = PictureDataUrl(b);
                if (url == null || used + url.Length > budget) continue;
                used += url.Length;
                res.Add(new BackupPicture { Id = r.Id, Data = url });
            }
            return res;
        }

        /// Picture ids that no record in list (or in keepAlso) has: the files to remove.
        public static List<string> OrphanPictures(IEnumerable<string> ids, List<QuoteRecord> list, IEnumerable<QuoteRecord> keepAlso)
        {
            var keep = new HashSet<string>(StringComparer.Ordinal);
            if (list != null) foreach (var r in list) if (r != null && !string.IsNullOrEmpty(r.Id)) keep.Add(r.Id);
            if (keepAlso != null) foreach (var r in keepAlso) if (r != null && !string.IsNullOrEmpty(r.Id)) keep.Add(r.Id);
            var res = new List<string>();
            if (ids != null) foreach (var id in ids) if (id != null && !keep.Contains(id)) res.Add(id);
            return res;
        }

        // files

        /// Keeps a record's picture (replaces an old one of any type). bytes null / invalid = removes it (a re-save without
        /// a picture, as the website). true = done. Never throws.
        public static bool SavePicture(string id, byte[] bytes)
        {
            if (!SafePictureId(id)) return false;
            if (!ValidPicture(bytes)) return DeletePicture(id);
            string dir = PictureDir, ext = PictureExtension(PictureMime(bytes));
            string path = Path.Combine(dir, id + ext), tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                foreach (var e in PictureExtensions)
                    if (e != ext) { string other = Path.Combine(dir, id + e); if (File.Exists(other)) File.Delete(other); }
                return true;
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }

        /// The record's picture bytes (checked: known type, <= MaxPictureBytes), null = none. Never throws.
        public static byte[] LoadPicture(string id)
        {
            if (!SafePictureId(id)) return null;
            try
            {
                foreach (var e in PictureExtensions)
                {
                    var f = new FileInfo(Path.Combine(PictureDir, id + e));
                    if (!f.Exists) continue;
                    if (f.Length > MaxPictureBytes) return null;
                    byte[] b = File.ReadAllBytes(f.FullName);
                    return ValidPicture(b) ? b : null;
                }
            }
            catch { }
            return null;
        }

        public static bool HasPicture(string id)
        {
            return LoadPicture(id) != null;
        }

        /// Removes the record's picture (all types). true = none left. Never throws.
        public static bool DeletePicture(string id)
        {
            if (!SafePictureId(id)) return false;
            bool ok = true;
            foreach (var e in PictureExtensions)
            {
                try { string p = Path.Combine(PictureDir, id + e); if (File.Exists(p)) File.Delete(p); }
                catch { ok = false; }
            }
            return ok;
        }

        /// Repeat print: the original's picture for the new record. false = the original has none (or it failed).
        public static bool CopyPicture(string fromId, string toId)
        {
            byte[] b = LoadPicture(fromId);
            return b != null && SavePicture(toId, b);
        }

        /// Ids that have a picture file.
        public static List<string> PictureIds()
        {
            var res = new List<string>();
            try
            {
                if (!Directory.Exists(PictureDir)) return res;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var f in Directory.GetFiles(PictureDir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    string id = Path.GetFileNameWithoutExtension(f);
                    if (Array.IndexOf(PictureExtensions, ext) >= 0 && SafePictureId(id) && seen.Add(id)) res.Add(id);
                }
            }
            catch { }
            return res;
        }

        /// Removes pictures no record has (deleted, trimmed, replaced), and leftover .tmp files. Pictures of the records in
        /// the newest history.before-restore-*.json stay, so undoing a Replace restore keeps them. Returns how many
        /// pictures were removed. Never throws. Call once per run after loading the history, not after every change.
        /// Does nothing for an empty list or while history.json couldn't be read (a failed load must never cost pictures).
        public static int CleanPictures(List<QuoteRecord> list)
        {
            if (list == null || list.Count == 0 || saveBlocked) return 0;
            List<QuoteRecord> safety = null;
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (Directory.Exists(dir))
                {
                    string newest = null;
                    foreach (var f in Directory.GetFiles(dir, "history.before-restore-*.json"))
                        if (newest == null || File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(newest)) newest = f;
                    if (newest != null) safety = ParseJson(File.ReadAllText(newest, Encoding.UTF8));
                }
            }
            catch { safety = null; }
            int n = 0;
            foreach (var id in OrphanPictures(PictureIds(), list, safety))
                if (DeletePicture(id)) n++;
            try
            {
                if (Directory.Exists(PictureDir))
                    foreach (var f in Directory.GetFiles(PictureDir, "*.tmp")) File.Delete(f);
            }
            catch { }
            return n;
        }

        // ---------- backup and restore (idea 27) ----------

        public const string BackupFormat = "PrintPriceBackup";
        public const int BackupVersion = 1;
        public const string BackupExtension = ".printprice-backup";

        /// settings.ini keys a backup never holds and a restore never brings back (they belong to the PC).
        public static readonly string[] PcOnlyKeys = { "SlicerPath", "BackupCode", "LastOnlineBackup" };

        /// "PrintPrice backup 2026-11-10.printprice-backup"
        public static string BackupFileName(DateTime now)
        {
            return "PrintPrice backup " + now.ToString("yyyy-MM-dd", Inv) + BackupExtension;
        }

        /// The backup text (BackupFile JSON): all records, and the settings.ini lines without PcOnlyKeys (s null = history only).
        public static string ToBackup(List<QuoteRecord> list, AppSettings s, string app, DateTime now)
        {
            return ToBackup(list, s, app, now, null);
        }

        /// ToBackup with record pictures (PicturesForBackup). Only pictures of records in list, each Id once, valid data
        /// URLs; none left = exactly the text without pictures. Version stays 1 (older apps ignore "Pictures").
        public static string ToBackup(List<QuoteRecord> list, AppSettings s, string app, DateTime now, List<BackupPicture> pictures)
        {
            List<string> lines = null;
            if (s != null)
            {
                lines = new List<string>();
                foreach (var l in s.ToIni()) if (!IsPcOnly(l)) lines.Add(l);
            }
            if (list == null) list = new List<QuoteRecord>();
            var pics = new List<BackupPicture>();
            if (pictures != null && pictures.Count > 0)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in list) if (r != null && SafePictureId(r.Id)) ids.Add(r.Id);
                var done = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in pictures)
                    if (p != null && p.Id != null && ids.Contains(p.Id) && PictureFromDataUrl(p.Data) != null && done.Add(p.Id))
                        pics.Add(new BackupPicture { Id = p.Id, Data = p.Data });
            }
            var hf = new HistoryFile { Version = Version, Quotes = list };
            if (pics.Count == 0)
                return Json.Serialize(new BackupFile
                {
                    Format = BackupFormat, Version = BackupVersion, CreatedAt = Stamp(now), App = app ?? "", History = hf, Settings = lines
                });
            return Json.Serialize(new BackupFileWithPictures
            {
                Format = BackupFormat, Version = BackupVersion, CreatedAt = Stamp(now), App = app ?? "", History = hf, Settings = lines,
                Pictures = pics
            });
        }

        /// "This is not a PrintPrice backup." (also Backup.Open)
        internal static string NotBackupText()
        {
            return L.S("This is not a PrintPrice backup.", "Det er ikke en PrintPrice-sikkerhedskopi.");
        }

        static bool IsPcOnly(string line)
        {
            if (line == null) return true;
            int eq = line.IndexOf('=');
            string key = (eq < 0 ? line : line.Substring(0, eq)).Trim();
            return Array.IndexOf(PcOnlyKeys, key) >= 0;
        }

        /// Reads a backup's text, or a plain history.json (no settings). null = OK (data set); else the problem text for
        /// the ui (data null): not a backup, or made by a newer version (backup Version or history Version).
        public static string ReadBackup(string text, out BackupData data)
        {
            data = null;
            string notBackup = NotBackupText();
            string newer = L.S("This backup was made by a newer PrintPrice. Update the app first.",
                               "Sikkerhedskopien er lavet af en nyere PrintPrice. Opdater programmet først.");
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return notBackup;
            BackupFileRead b;
            try { b = Json.Deserialize<BackupFileRead>(text); }
            catch { return notBackup; }
            if (b == null) return notBackup;
            if (b.Format == BackupFormat)
            {
                if (b.Version > BackupVersion) return newer;
                if (b.Version < 1 || b.History == null) return notBackup;
                if (b.History.Version > Version) return newer;
                if (b.History.Version != Version || b.History.Quotes == null) return notBackup;
                data = new BackupData { Quotes = CleanQuotes(b.History.Quotes), CreatedAt = b.CreatedAt ?? "", App = b.App ?? "" };
                if (b.Settings != null)
                {
                    var l = new List<string>();
                    foreach (var x in b.Settings) if (!IsPcOnly(x)) l.Add(x);
                    data.Settings = l.ToArray();
                }
                ReadPictures(text, data);
                return null;
            }
            if (b.Format != null) return notBackup;
            // a plain history.json: {"Version":1,"Quotes":[...]}
            if (b.Quotes == null) return notBackup;
            if (b.Version > Version) return newer;
            if (b.Version != Version) return notBackup;
            data = new BackupData { Quotes = CleanQuotes(b.Quotes) };
            return null;
        }

        // a backup's pictures into data.Pictures, read loosely in a second, untyped parse (Dictionary / ArrayList / string
        // shapes on both the desktop and the website Json): only for its records (safe Id), first one per Id, valid data
        // URLs; any other shape ("Pictures":"x", [1,2], "Data":{..}) is dropped quietly, the records always restore
        static void ReadPictures(string text, BackupData data)
        {
            if (text == null || text.IndexOf("\"Pictures\"", StringComparison.OrdinalIgnoreCase) < 0) return;
            object list;
            try
            {
                var top = Json.Deserialize<Dictionary<string, object>>(text);
                list = Member(top, "Pictures");
            }
            catch { return; }
            var items = list as System.Collections.IEnumerable;
            if (items == null || list is string) return;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in data.Quotes) if (r != null && SafePictureId(r.Id)) ids.Add(r.Id);
            foreach (var item in items)
            {
                var d = item as IDictionary<string, object>;
                if (d == null) continue;
                string id = Member(d, "Id") as string, url = Member(d, "Data") as string;
                if (id == null || url == null || !ids.Contains(id) || data.Pictures.ContainsKey(id)) continue;
                byte[] b = PictureFromDataUrl(url);
                if (b != null) data.Pictures[id] = b;
            }
        }

        // a JSON object's member by name (exact, else ignoring case); null when missing or not an object
        static object Member(IDictionary<string, object> d, string name)
        {
            if (d == null) return null;
            object v;
            if (d.TryGetValue(name, out v)) return v;
            foreach (var kv in d) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        /// Matches the same record on both sides: its Id; records without one by SavedAt, model, source, time, grams, price.
        public static string RecordKey(QuoteRecord r)
        {
            if (!string.IsNullOrEmpty(r.Id)) return "id:" + r.Id;
            return "k:" + r.SavedAt + "|" + r.Model + "|" + r.Source + "|" + r.Hours.ToString(Inv) + "|" + r.Minutes.ToString(Inv)
                + "|" + r.Grams.ToString("R", Inv) + "|" + r.OthersPrice.ToString("R", Inv);
        }

        // deep copies, so a restore never changes the caller's records; the same as a JSON round trip (ParseJson(ToJson)):
        // Printed set again (a written file always has the key), then CleanQuotes
        internal static List<QuoteRecord> CopyQuotes(List<QuoteRecord> list)
        {
            if (list == null || list.Count == 0) return new List<QuoteRecord>();
            var copy = new List<QuoteRecord>(list.Count);
            foreach (var r in list) copy.Add(r == null ? null : CopyRecord(r));
            return CleanQuotes(copy);
        }

        static QuoteRecord CopyRecord(QuoteRecord r)
        {
            var c = (QuoteRecord)r.MemberwiseCopy();
            c.Printed = r.Printed;
            if (r.Filaments != null)
            {
                c.Filaments = new List<HistoryFilament>(r.Filaments.Count);
                foreach (var f in r.Filaments)
                    c.Filaments.Add(f == null ? null : (HistoryFilament)f.MemberwiseCopy());
            }
            c.Parts = r.Parts == null ? null : r.Parts.Copy();
            return c;
        }

        // fills keep (this PC's) from other (the same record in the backup); true when something changed
        static bool FillFrom(QuoteRecord keep, QuoteRecord other)
        {
            bool changed = false;
            if (string.IsNullOrEmpty(keep.InvoiceNo) && !string.IsNullOrEmpty(other.InvoiceNo))   // an issued code is never lost
            {
                keep.InvoiceNo = other.InvoiceNo; keep.BilledAt = other.BilledAt; keep.InvoiceDue = other.InvoiceDue;
                keep.InvoicePayMobilePay = other.InvoicePayMobilePay; keep.InvoicePayBank = other.InvoicePayBank;
                keep.InvoiceFromName = other.InvoiceFromName;
                changed = true;
            }
            if (other.Printed && !keep.Printed) { keep.Printed = true; changed = true; }
            if (other.Failed && !keep.Failed)
            {
                keep.Failed = true; keep.FailedAt = other.FailedAt; keep.PaidBack = other.PaidBack;
                changed = true;
            }
            if (keep.Note.Trim().Length == 0 && other.Note.Trim().Length > 0) { keep.Note = other.Note; changed = true; }
            if (keep.Customer.Trim().Length == 0 && other.Customer.Trim().Length > 0) { keep.Customer = other.Customer; changed = true; }
            if (keep.Parts == null && other.Parts != null) { keep.Parts = other.Parts.Copy(); changed = true; }   // idea 21
            return changed;
        }

        // Records whose printer id is one of kept's printers but with a different model than their own printer (from
        // theirs, else the record's snapshot) move to a new id, reserved in kept. One decision per id. Returns ids moved.
        static int MovePrinters(List<QuoteRecord> records, AppSettings theirs, AppSettings kept)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            int moved = 0;
            foreach (var r in records)
            {
                string id = PrinterOf(r), to;
                if (!map.TryGetValue(id, out to))
                {
                    to = id;
                    var here = kept.FindPrinter(id);
                    string own = null;
                    if (theirs != null)
                    {
                        var p = theirs.FindPrinter(id);
                        if (p != null) own = p.Model;
                    }
                    if (own == null && !string.IsNullOrEmpty(r.PrinterModel)) own = r.PrinterModel;
                    if (here != null && own != null && Wear.FindModel(own).Key != Wear.FindModel(here.Model).Key)
                    {
                        to = ((long)kept.LastPrinterId + 1).ToString(Inv);
                        kept.ReservePrinterId(to);
                        moved++;
                    }
                    map[id] = to;
                }
                if (to != id) r.PrinterId = to;
            }
            return moved;
        }

        static void AddCodes(List<string> codes, HashSet<string> seen, IEnumerable<string> more)
        {
            foreach (var c in more)
            {
                string t = (c ?? "").Trim();
                if (t.Length > 0 && seen.Add(t)) codes.Add(t);
            }
        }

        static IEnumerable<string> CodesOf(List<QuoteRecord> list)
        {
            var res = new List<string>();
            if (list != null)
                foreach (var r in list) if (r != null && !string.IsNullOrEmpty(r.InvoiceNo)) res.Add(r.InvoiceNo);
            return res;
        }

        /// The restore without files (pure). Never changes current, currentSettings or backup.
        /// - replace: the history becomes the backup's; else merge: this PC's records stay, the backup's new ones are added,
        ///   the same record (RecordKey) is filled in from the backup (FillFrom: invoice if none here, Printed, Failed, empty
        ///   Note / Customer). Then newest first by SavedAt and Trim(MaxQuotes).
        /// - withSettings (and the backup has settings): settings = the backup's, but all PcOnlyKeys stay this PC's; else this PC's.
        /// - printers: an id both sides use for different models moves the records of the side whose settings are NOT kept to a
        ///   new id (MovePrinters); every printer id in either history or settings is reserved.
        /// - UsedInvoiceNos = both sides' lists + every invoice code in both histories (also replaced ones): never given again.
        ///   Codes on 2+ different records are kept and listed in InvoiceClashes.
        public static RestoreResult Combine(List<QuoteRecord> current, AppSettings currentSettings, BackupData backup,
                                            bool replace, bool withSettings)
        {
            return Combine(current, currentSettings, backup, replace, withSettings, null);
        }

        /// Combine with pictures: RestoreResult.Pictures = the backup's pictures of records in the result; replace: all of
        /// them; merge: added records', and a same record's only when hasPicture(id) says this PC has none (null = none here).
        public static RestoreResult Combine(List<QuoteRecord> current, AppSettings currentSettings, BackupData backup,
                                            bool replace, bool withSettings, Func<string, bool> hasPicture)
        {
            if (backup == null) throw new ArgumentNullException("backup");
            if (currentSettings == null) currentSettings = new AppSettings();
            var res = new RestoreResult();
            var cur = replace ? new List<QuoteRecord>() : CopyQuotes(current);
            var inc = CopyQuotes(backup.Quotes);
            AppSettings bs = backup.Settings != null ? AppSettings.FromIni(backup.Settings) : null;
            AppSettings rs;
            if (withSettings && bs != null)
            {
                rs = bs.Clone();
                rs.CopyKeys(currentSettings, PcOnlyKeys);   // SlicerPath, BackupCode, LastOnlineBackup stay this PC's
                res.SettingsRestored = true;
            }
            else rs = currentSettings.Clone();

            // printer ids: never hand out one used on either side, then move clashing ones
            rs.ReservePrinterId(((long)currentSettings.LastPrinterId).ToString(Inv));
            if (bs != null) rs.ReservePrinterId(((long)bs.LastPrinterId).ToString(Inv));
            ReservePrinterIds(current, rs);
            ReservePrinterIds(inc, rs);
            res.PrintersMoved = res.SettingsRestored ? MovePrinters(cur, currentSettings, rs) : MovePrinters(inc, bs, rs);

            // merge
            var index = new Dictionary<string, QuoteRecord>(StringComparer.Ordinal);
            var addedIds = new HashSet<string>(StringComparer.Ordinal);
            var all = new List<QuoteRecord>(cur.Count + inc.Count);
            foreach (var r in cur)
            {
                string k = RecordKey(r);
                if (!index.ContainsKey(k)) index[k] = r;
                all.Add(r);
            }
            foreach (var r in inc)
            {
                string k = RecordKey(r);
                QuoteRecord here;
                if (index.TryGetValue(k, out here))
                {
                    if (FillFrom(here, r)) res.Updated++; else res.Same++;
                    continue;
                }
                index[k] = r;
                all.Add(r);
                res.Added++;
                if (!string.IsNullOrEmpty(r.Id)) addedIds.Add(r.Id);
            }
            var order = new Dictionary<QuoteRecord, int>();
            for (int i = 0; i < all.Count; i++) order[all[i]] = i;
            all.Sort(delegate(QuoteRecord a, QuoteRecord b)
            {
                DateTime ta, tb;
                if (!TryTime(a.SavedAt, out ta)) ta = DateTime.MinValue;
                if (!TryTime(b.SavedAt, out tb)) tb = DateTime.MinValue;
                int c = tb.CompareTo(ta);   // newest first, unreadable times last
                return c != 0 ? c : order[a].CompareTo(order[b]);
            });
            Trim(all, MaxQuotes);

            // invoice codes
            var codes = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCodes(codes, seen, (currentSettings.UsedInvoiceNos ?? "").Split(','));
            if (bs != null) AddCodes(codes, seen, (bs.UsedInvoiceNos ?? "").Split(','));
            AddCodes(codes, seen, CodesOf(current));
            AddCodes(codes, seen, CodesOf(all));
            if (codes.Count > MaxUsedInvoiceNos) codes.RemoveRange(0, codes.Count - MaxUsedInvoiceNos);
            rs.UsedInvoiceNos = string.Join(",", codes.ToArray());
            var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in all)
            {
                if (string.IsNullOrEmpty(r.InvoiceNo)) continue;
                string c = r.InvoiceNo.Trim();
                int n;
                count.TryGetValue(c, out n);
                count[c] = n + 1;
                if (n + 1 == 2) res.InvoiceClashes.Add(c);
            }

            // pictures
            if (backup.Pictures != null && backup.Pictures.Count > 0)
            {
                var kept = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in all) if (!string.IsNullOrEmpty(r.Id)) kept.Add(r.Id);
                foreach (var p in backup.Pictures)
                {
                    if (!kept.Contains(p.Key) || !SafePictureId(p.Key) || !ValidPicture(p.Value)) continue;
                    bool here = false;
                    if (!replace && !addedIds.Contains(p.Key) && hasPicture != null)
                    {
                        try { here = hasPicture(p.Key); } catch { here = false; }
                    }
                    if (!here) res.Pictures[p.Key] = p.Value;
                }
            }

            res.Quotes = all;
            res.Settings = rs;
            return res;
        }

        /// Restores a backup: Combine, then safety copies of history.json and settings.ini (when they exist) as
        /// history.before-restore-yyyyMMdd-HHmmss.json / settings.before-restore-yyyyMMdd-HHmmss.ini (the restore stops if a
        /// copy fails), then saves the history and the settings. null = OK. Refused while history.json couldn't be read.
        /// result is set once the history is saved (also when only the settings failed): the caller uses result.Quotes and
        /// result.Settings from then on. Never throws.
        public static string Restore(List<QuoteRecord> current, AppSettings settings, BackupData backup, bool replace,
                                     bool withSettings, DateTime now, out RestoreResult result)
        {
            result = null;
            if (saveBlocked)
                return L.S("Backup not restored: the price history file could not be read.",
                           "Sikkerhedskopien blev ikke gendannet: prishistorik-filen kunne ikke læses.");
            RestoreResult r;
            try { r = Combine(current, settings, backup, replace, withSettings, HasPicture); }
            catch (Exception ex) { return L.S("Backup not restored: ", "Sikkerhedskopien blev ikke gendannet: ") + ex.Message; }
            string hist = FilePath, ini = AppSettings.FilePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(hist));
                if (File.Exists(hist)) File.Copy(hist, BackupName(hist, "history.before-restore-", ".json", now));
                if (File.Exists(ini)) File.Copy(ini, BackupName(ini, "settings.before-restore-", ".ini", now));
            }
            catch (Exception ex)
            {
                return L.S("Backup not restored, the safety copy failed: ", "Sikkerhedskopien blev ikke gendannet, sikkerhedskopien af det nuværende fejlede: ")
                    + ex.Message;
            }
            string problem = Save(r.Quotes);
            if (problem != null) return problem;
            result = r;
            foreach (var p in r.Pictures) SavePicture(p.Key, p.Value);   // quiet: a missing picture never fails a restore
            try { File.WriteAllLines(ini, r.Settings.ToIni()); }
            catch (Exception ex)
            {
                return L.S("Price history restored, but the settings could not be saved: ",
                           "Prishistorikken er gendannet, men indstillingerne kunne ikke gemmes: ") + ex.Message;
            }
            return null;
        }

        /// Writes a backup file (ToBackup). null = OK, else the problem text. Never throws.
        public static string WriteBackupFile(string path, List<QuoteRecord> list, AppSettings s, string app, DateTime now)
        {
            return WriteBackupFile(path, list, s, app, now, null);
        }

        /// WriteBackupFile with pictures (e.g. BackupPictures(list)).
        public static string WriteBackupFile(string path, List<QuoteRecord> list, AppSettings s, string app, DateTime now,
                                             List<BackupPicture> pictures)
        {
            try
            {
                File.WriteAllText(path, ToBackup(list, s, app, now, pictures), new UTF8Encoding(false));
                return null;
            }
            catch (Exception ex)
            {
                return L.S("Backup not saved: ", "Sikkerhedskopien blev ikke gemt: ") + ex.Message;
            }
        }

        /// Reads a backup file or a plain history.json (ReadBackup). null = OK. Never throws.
        public static string ReadBackupFile(string path, out BackupData data)
        {
            data = null;
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex) { return L.S("Backup could not be read: ", "Sikkerhedskopien kunne ikke læses: ") + ex.Message; }
            return ReadBackup(text, out data);
        }

        /// This PC's pictures for a backup file: newest first within FilePictureBudget (= PicturesForBackup with LoadPicture).
        public static List<BackupPicture> BackupPictures(List<QuoteRecord> list)
        {
            return PicturesForBackup(list, LoadPicture, FilePictureBudget);
        }
    }

#if !WEB
    public static partial class History
    {
        /// The small picture kept with a record (issue #4): longest side PictureSize (256 px), PNG when it has transparent
        /// pixels (Bambu plate pictures), else JPEG quality 85 (photos); over MaxPictureBytes -> again at 192 px; still
        /// over -> null. null image -> null. Never throws. Doesn't change or dispose img.
        public static byte[] EncodePicture(Image img)
        {
            if (img == null) return null;
            try
            {
                foreach (int size in new[] { PictureSize, SmallPictureSize })
                {
                    byte[] b = EncodePictureAt(img, size);
                    if (b != null && b.Length <= MaxPictureBytes) return b;
                }
            }
            catch { }
            return null;
        }

        internal static byte[] EncodePictureAt(Image img, int max)
        {
            int iw = img.Width, ih = img.Height;
            if (iw <= 0 || ih <= 0) return null;
            double k = Math.Min(1.0, max / (double)Math.Max(iw, ih));
            int w = Math.Max(1, (int)Math.Round(iw * k)), h = Math.Max(1, (int)Math.Round(ih * k));
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var ia = new ImageAttributes())
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    ia.SetWrapMode(WrapMode.TileFlipXY);   // no half-transparent edge on opaque pictures
                    g.DrawImage(img, new Rectangle(0, 0, w, h), 0, 0, iw, ih, GraphicsUnit.Pixel, ia);
                }
                using (var ms = new MemoryStream())
                {
                    if (HasTransparency(bmp)) bmp.Save(ms, ImageFormat.Png);
                    else
                    {
                        ImageCodecInfo jpeg = null;
                        foreach (var c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") jpeg = c;
                        using (var ep = new EncoderParameters(1))
                        {
                            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                            bmp.Save(ms, jpeg, ep);
                        }
                    }
                    return ms.ToArray();
                }
            }
        }

        static bool HasTransparency(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData d = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[bmp.Width * 4];
                for (int y = 0; y < bmp.Height; y++)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                    for (int i = 3; i < row.Length; i += 4) if (row[i] != 255) return true;
                }
                return false;
            }
            finally { bmp.UnlockBits(d); }
        }

        /// A stored picture as an Image (caller disposes), null = none or not readable here (WebP: GDI+ can't). Never throws.
        public static Image PictureImage(byte[] bytes)
        {
            if (!ValidPicture(bytes) || PictureMime(bytes) == "image/webp") return null;
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);   // a copy that doesn't need the stream
            }
            catch { return null; }
        }
    }
#endif

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
