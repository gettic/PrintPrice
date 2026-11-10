using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PrintPrice
{
    public class AppSettings
    {
        // Bambu Lab A1 averages roughly 80-110 W while printing PLA.
        public double PrinterWatts = 100;
        public double FriendsMarkupPct = 25;
        public double OthersMarkupPct = 75;
        public double MachineRatePerHour = Wear.DefaultRate;   // DKK/h added to both prices (A1 wear & tear)
        public double AutoWearRate = 1;                        // 1 = calculate from nozzle, filament & plate
        public double FailedPrintPct = 0;       // % added to material + power for failed prints / reprints (0 = off)
        public double FailedPayback = 1;        // 1 = add a payback to new quotes until failed prints are made back (0 = off)
        public double FailedPaybackPct = 10;    // payback per quote: % of material + power (never more than what's left)
        public double FailedPaybackWho = 2;     // 1 = both prices, 2 = customers only
        public double MinimumPrice = 50;        // DKK, customer price floor
        public double RoundTo = 0;              // round prices up to nearest N kr. (0 = off)
        public double LastFilamentPrice = 180;  // DKK/kg
        public double LastElectricityPrice = 2.00; // DKK/kWh

        // Settings file version. 2 = v1.8.0 defaults (MinimumPrice 50, FailedPayback on, FailedPaybackPct 10).
        // A file without the key (or below 2) gets UpgradeOldDefaults once on Load; new files are 2 straight away.
        public const int CurrentSettingsVersion = 2;
        public double SettingsVersion = CurrentSettingsVersion;

        /// One-time step for a settings file older than version 2: a value still at the OLD default counts as "never
        /// changed" and gets the new default (MinimumPrice 0 -> 50, FailedPayback 0 -> 1, FailedPaybackPct 25 -> 10).
        /// Any other value is kept. Sets SettingsVersion = 2. Doesn't save.
        internal void UpgradeOldDefaults()
        {
            if (SettingsVersion < 2)
            {
                if (MinimumPrice == 0) MinimumPrice = 50;
                if (FailedPayback == 0) FailedPayback = 1;
                if (FailedPaybackPct == 25) FailedPaybackPct = 10;
            }
            SettingsVersion = CurrentSettingsVersion;
        }

        // Live electricity price (Energi Data Service)
        public double AutoPowerPrice = 1;       // 1 = fetch the current spot price at startup
        public double AutoUpdateCheck = 1;      // 1 = look for a new version on GitHub at startup
        public double PowerAddOn = 0.55;       // DKK/kWh excl. VAT: grid tariff + Energinet + elafgift + supplier fee
        public double PowerIncludeVat = 1;      // 1 = add 25 % VAT
        public string PriceArea = "DK1";        // DK1 = Jutland/Funen, DK2 = Zealand/Copenhagen

        // Auto-detect (Bambu Studio)
        public string SlicerPath = "";          // empty = auto-detect
        public string Nozzle = "0.4 stainless";         // key in Wear.Nozzles
        public string ProcessPreset = "0.16mm Optimal"; // short names; "@BBL A1 …" is added per nozzle
        public string FilamentPreset = "Bambu PLA Matte";
        public string BedType = "High Temp Plate";      // = Smooth PEI plate (Bambu's internal name)

        public const string DefaultPrinterName = "Bambu Lab A1";
        public string PrinterName = DefaultPrinterName;   // shown by "Power (avg.)"; trimmed, max 40, empty = default

        public static string CleanPrinterName(string v)
        {
            v = (v ?? "").Trim();
            if (v.Length > 40) v = v.Substring(0, 40).Trim();
            return v.Length == 0 ? DefaultPrinterName : v;
        }

        // ---------- several printers (ideas 7 + 24 + 11) ----------
        // The flat fields PrinterName, PrinterModel, PrinterWatts, AutoWearRate, MachineRatePerHour, Nozzle, BedType and
        // ProcessPreset are the ACTIVE printer's working copy (Pricing and the slicer read them). The list keeps every
        // printer; SelectPrinter swaps the fields. settings.ini: the flat keys always hold the FIRST printer (+ "PrinterId",
        // missing = "1"), so an old file is printer 1 unchanged; other printers are "Printer.<id>.<key>=..." lines.
        public string PrinterModel = Wear.ModelA1;     // Wear.Models key of the active printer
        public string ActivePrinterId = "1";
        public double LastPrinterId = 1;               // highest id ever given out: ids are never reused
        List<PrinterProfile> printers;                 // null = one printer made from the fields

        const string PrinterIdKey = "PrinterId", PrinterLinePrefix = "Printer.";

        PrinterProfile FromFields(string id)
        {
            return new PrinterProfile
            {
                Id = id, Name = CleanPrinterName(PrinterName), Model = Wear.FindModel(PrinterModel).Key,
                Watts = PrinterWatts, AutoWearRate = AutoWearRate, MachineRatePerHour = MachineRatePerHour,
                Nozzle = Nozzle, BedType = BedType, ProcessPreset = ProcessPreset
            };
        }

        void ToFields(PrinterProfile p)
        {
            PrinterName = p.Name; PrinterModel = p.Model; PrinterWatts = p.Watts; AutoWearRate = p.AutoWearRate;
            MachineRatePerHour = p.MachineRatePerHour; Nozzle = p.Nozzle; BedType = p.BedType; ProcessPreset = p.ProcessPreset;
        }

        int PrinterIndex(string id)
        {
            if (printers == null || id == null) return -1;
            for (int i = 0; i < printers.Count; i++) if (printers[i].Id == id) return i;
            return -1;
        }

        // Makes sure the list exists and holds the active printer, and copies the fields into the active entry.
        void SyncPrinters()
        {
            if (printers == null) printers = new List<PrinterProfile>();
            if (string.IsNullOrEmpty(ActivePrinterId)) ActivePrinterId = printers.Count > 0 ? printers[0].Id : "1";
            int i = PrinterIndex(ActivePrinterId);
            if (i < 0) printers.Add(FromFields(ActivePrinterId));
            else printers[i] = FromFields(ActivePrinterId);
            NoteId(ActivePrinterId);
        }

        /// A printer id is a whole number 1-999999999 written without signs or leading zeros ("1", "2", ...).
        public static bool ValidPrinterId(string id)
        {
            int n;
            return !string.IsNullOrEmpty(id) && id.Length <= 9 && id[0] != '0'
                && int.TryParse(id, NumberStyles.None, Inv, out n) && n >= 1;
        }

        /// Marks a printer id as used (e.g. one found in the history): AddPrinter then never hands it out.
        /// Invalid ids are ignored. True when LastPrinterId went up. Doesn't save.
        public bool ReservePrinterId(string id)
        {
            double before = LastPrinterId;
            if (ValidPrinterId(id)) NoteId(id);
            return LastPrinterId > before;
        }

        void NoteId(string id)
        {
            int n;
            if (int.TryParse(id, NumberStyles.None, Inv, out n) && n > LastPrinterId) LastPrinterId = n;
        }

        /// Copies of all printers, in order (never empty; the active one has the current field values).
        public List<PrinterProfile> Printers
        {
            get
            {
                SyncPrinters();
                var res = new List<PrinterProfile>(printers.Count);
                foreach (var p in printers) res.Add(p.Clone());
                return res;
            }
        }

        public int PrinterCount { get { SyncPrinters(); return printers.Count; } }

        /// A copy of the active printer.
        public PrinterProfile ActivePrinter { get { return FindPrinter(ActivePrinterId); } }

        /// A copy of the printer with this id, or null.
        public PrinterProfile FindPrinter(string id)
        {
            SyncPrinters();
            int i = PrinterIndex(id);
            return i < 0 ? null : printers[i].Clone();
        }

        /// Makes this printer the active one (its values go into the fields). False (nothing changes) if not found.
        public bool SelectPrinter(string id)
        {
            SyncPrinters();
            int i = PrinterIndex(id);
            if (i < 0) return false;
            ActivePrinterId = id;
            ToFields(printers[i]);
            return true;
        }

        /// Adds a printer of this model (unknown = A1) with the model's power use and stock wear rate (auto wear on,
        /// 0.4 stainless, smooth PEI, today's print profile). Name empty = the model's label. Doesn't select it or save.
        public PrinterProfile AddPrinter(string name, string modelKey)
        {
            SyncPrinters();
            var m = Wear.FindModel(modelKey);
            LastPrinterId = Math.Floor(LastPrinterId) + 1;
            var p = new PrinterProfile
            {
                Id = ((long)LastPrinterId).ToString(Inv), Model = m.Key,
                Name = CleanPrinterName((name ?? "").Trim().Length == 0 ? m.Label : name),
                Watts = m.DefaultWatts, AutoWearRate = 1, MachineRatePerHour = Wear.DefaultRateFor(m.Key),
                ProcessPreset = ProcessPreset
            };
            printers.Add(p);
            return p.Clone();
        }

        /// Replaces the printer with p's Id by a cleaned copy of p (the fields too when it is the active one).
        /// False if no printer has that Id.
        public bool SetPrinter(PrinterProfile p)
        {
            if (p == null) return false;
            SyncPrinters();
            int i = PrinterIndex(p.Id);
            if (i < 0) return false;
            var c = p.Clone();
            c.Clean();
            printers[i] = c;
            if (c.Id == ActivePrinterId) ToFields(c);
            return true;
        }

        /// Removes a printer (its history keeps its id and name). The last printer can't be removed. Removing the
        /// active one selects the first one left.
        public bool RemovePrinter(string id)
        {
            SyncPrinters();
            int i = PrinterIndex(id);
            if (i < 0 || printers.Count < 2) return false;
            printers.RemoveAt(i);
            if (id == ActivePrinterId) { ActivePrinterId = printers[0].Id; ToFields(printers[0]); }
            return true;
        }

        // printer keys as written in the file (flat for the first printer, "Printer.<id>." prefix for the others)
        static void PutPrinter(Dictionary<string, double> nums, Dictionary<string, string> strs, PrinterProfile p)
        {
            nums["PrinterWatts"] = p.Watts;
            nums["MachineRatePerHour"] = p.MachineRatePerHour;
            nums["AutoWearRate"] = p.AutoWearRate;
            strs["PrinterName"] = CleanPrinterName(p.Name);
            strs["PrinterModel"] = p.Model;
            strs["Nozzle"] = p.Nozzle;
            strs["BedType"] = p.BedType;
            strs["ProcessPreset"] = p.ProcessPreset;
        }

        static readonly string[] PrinterNumberKeys = { "PrinterWatts", "MachineRatePerHour", "AutoWearRate" };
        static readonly string[] PrinterStringKeys = { "PrinterName", "PrinterModel", "Nozzle", "BedType", "ProcessPreset" };

        public string Language = "";            // "" = auto (Danish if Windows is Danish), "en", "da"

        // Quote export (picture / PDF)
        public const string QuotePriceCustomer = "customer", QuotePriceFriends = "friends";
        public string QuoteName = "Gettic";     // name on the quote (may be empty)
        public double QuoteValidDays = 14;      // valid until = quote date + N days (whole days, min 1)
        public string QuotePrice = QuotePriceCustomer;   // which price the quote shows: "customer" or "friends"
        public double QuoteDark = 1;            // 1 = quote picture in the dark look (default), 0 = light; saved as "QuoteLook" (the old "QuoteDark" key is ignored so the dark default reaches everyone once)

        // Invoice: days to pay (1-365), and how to pay (shown on the invoice only when not empty)
        public double InvoiceDueDays = 14;
        public string PayMobilePay = "";
        public string PayBank = "";
        public string UsedInvoiceNos = "";   // invoice codes ever given out, comma-separated, last 2000 (History.AddUsedInvoiceNo): a code is never given twice

        /// Payment due date: the billed date (time dropped) + InvoiceDueDays (whole, 1-365; bad values -> 14).
        public DateTime InvoiceDue(DateTime billed)
        {
            double d = InvoiceDueDays;
            int days = double.IsNaN(d) || d < 1 || d > 365 ? 14 : (int)Math.Round(d);
            return billed.Date.AddDays(days);
        }

        /// True when the quote shows the friends & family price.
        public bool QuoteFriends { get { return QuotePrice == QuotePriceFriends; } }

        /// The History.For value of the chosen quote price (History.ForFriends / ForCustomer).
        public int QuoteFor { get { return QuoteFriends ? History.ForFriends : History.ForCustomer; } }

        /// Last day the quote is valid: the quote's date (time dropped) + QuoteValidDays (at least 1).
        public DateTime QuoteValidUntil(DateTime quoteDate)
        {
            return ValidUntil(quoteDate, QuoteValidDays);
        }

        public static DateTime ValidUntil(DateTime quoteDate, double days)
        {
            int d = double.IsNaN(days) || days < 1 ? 1 : (days > 3650 ? 3650 : (int)Math.Round(days));
            return quoteDate.Date.AddDays(d);
        }

        // Remembered kr./kg per filament short name ("Bambu PLA Matte" -> 200). Saved as one line per name:
        // FilamentPrice=200|Bambu PLA Matte (number first, so any name works; older versions ignore the line).
        public Dictionary<string, double> FilamentPrices = new Dictionary<string, double>(StringComparer.Ordinal);

        const string FilamentPriceKey = "FilamentPrice";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string CleanName(string name)
        {
            if (name == null) return "";
            return name.Replace("\r", "").Replace("\n", "").Trim();
        }

        static bool ValidPrice(double kr)
        {
            return kr >= 0 && !double.IsNaN(kr) && !double.IsInfinity(kr);
        }

        /// The remembered kr./kg for this filament, else LastFilamentPrice.
        public double PriceFor(string name)
        {
            double v;
            string n = CleanName(name);
            if (n.Length > 0 && FilamentPrices != null && FilamentPrices.TryGetValue(n, out v)) return v;
            return LastFilamentPrice;
        }

        /// Remembers kr./kg for this filament (replaces an older value). Empty name or a negative/NaN price is ignored.
        /// Doesn't touch LastFilamentPrice and doesn't save: call Save().
        public void RememberPrice(string name, double kr)
        {
            string n = CleanName(name);
            if (n.Length == 0 || !ValidPrice(kr)) return;
            if (FilamentPrices == null) FilamentPrices = new Dictionary<string, double>(StringComparer.Ordinal);
            FilamentPrices[n] = kr;
        }

        // "200|Bambu PLA Matte" -> remembered (bad lines are skipped)
        void ReadFilamentPrice(string val)
        {
            int bar = val.IndexOf('|');
            if (bar <= 0) return;
            double v;
            if (!double.TryParse(val.Substring(0, bar).Trim(), NumberStyles.Float, Inv, out v)) return;
            RememberPrice(val.Substring(bar + 1), v);
        }

        // "da" -> true, "en" -> false, anything else (incl. "") -> follow Windows' UI language
        public static bool IsDanish(string language)
        {
            if (language == "da") return true;
            if (language == "en") return false;
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "da";
        }

        // ---------- test mode: PrintPrice.exe --settings "<folder>" ----------
        // Once a test folder is set, FilePath only ever points into it (never falls back to %APPDATA%).
        static string testFolder;

        public static bool TestMode { get { return testFolder != null; } }

        /// The user's own settings folder: %APPDATA%\PrintPrice
        public static string RealFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintPrice"); }
        }

        /// The folder settings.ini (and history.json) live in: the test folder in test mode, else RealFolder.
        public static string Folder
        {
            get { return TestMode ? testFolder : RealFolder; }
        }

        public static string FilePath
        {
            get { return Path.Combine(Folder, "settings.ini"); }
        }

        /// Switches to a separate settings folder (test mode). Refuses (returns false, nothing changes) an empty or
        /// relative path, or the real %APPDATA%\PrintPrice folder. Doesn't create the folder.
        public static bool UseFolder(string folder)
        {
            string full = FullFolder(folder);
            if (full == null) return false;
            string real = FullFolder(RealFolder);
            if (real != null && string.Equals(full, real, StringComparison.OrdinalIgnoreCase)) return false;
            testFolder = full;
            return true;
        }

        /// Tests only: back to the real settings file.
        internal static void ClearTestFolder() { testFolder = null; }

        // full path without trailing separators, or null if empty / relative / invalid
        static string FullFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            folder = folder.Trim();
            bool drive = folder.Length >= 3 && char.IsLetter(folder[0]) && folder[1] == ':' && (folder[2] == '\\' || folder[2] == '/');
            bool unc = folder.StartsWith(@"\\");
            if (!drive && !unc) return null;
            try
            {
                string full = Path.GetFullPath(folder).TrimEnd('\\', '/');
                if (full.Length == 2 && full[1] == ':') full += "\\";   // keep "C:\" a root
                return full;
            }
            catch { return null; }
        }

        Dictionary<string, double> Numbers()
        {
            var d = new Dictionary<string, double>();
            d["PrinterWatts"] = PrinterWatts;
            d["FriendsMarkupPct"] = FriendsMarkupPct;
            d["OthersMarkupPct"] = OthersMarkupPct;
            d["MachineRatePerHour"] = MachineRatePerHour;
            d["FailedPrintPct"] = FailedPrintPct;
            d["FailedPayback"] = FailedPayback;
            d["FailedPaybackPct"] = FailedPaybackPct;
            d["FailedPaybackWho"] = FailedPaybackWho;
            d["MinimumPrice"] = MinimumPrice;
            d["RoundTo"] = RoundTo;
            d["LastFilamentPrice"] = LastFilamentPrice;
            d["LastElectricityPrice"] = LastElectricityPrice;
            d["AutoWearRate"] = AutoWearRate;
            d["AutoPowerPrice"] = AutoPowerPrice;
            d["AutoUpdateCheck"] = AutoUpdateCheck;
            d["PowerAddOn"] = PowerAddOn;
            d["PowerIncludeVat"] = PowerIncludeVat;
            d["QuoteValidDays"] = QuoteValidDays;
            d["QuoteLook"] = QuoteDark;
            d["InvoiceDueDays"] = InvoiceDueDays;
            d["SettingsVersion"] = SettingsVersion;
            d["LastPrinterId"] = LastPrinterId;
            return d;
        }

        Dictionary<string, string> Strings()
        {
            var d = new Dictionary<string, string>();
            d["SlicerPath"] = SlicerPath;
            d["ProcessPreset"] = ProcessPreset;
            d["FilamentPreset"] = FilamentPreset;
            d["Nozzle"] = Nozzle;
            d["BedType"] = BedType;
            d["PriceArea"] = PriceArea;
            d["Language"] = Language;
            d["PrinterName"] = CleanPrinterName(PrinterName);
            d["PrinterModel"] = PrinterModel;
            d["ActivePrinter"] = ActivePrinterId;
            d["QuoteName"] = QuoteName;
            d["QuotePrice"] = QuotePrice;
            d["PayMobilePay"] = PayMobilePay;
            d["PayBank"] = PayBank;
            d["UsedInvoiceNos"] = UsedInvoiceNos;
            return d;
        }

        void SetNumber(string key, double v)
        {
            switch (key)
            {
                case "PrinterWatts": PrinterWatts = v; break;
                case "FriendsMarkupPct": FriendsMarkupPct = v; break;
                case "OthersMarkupPct": OthersMarkupPct = v; break;
                case "MachineRatePerHour": MachineRatePerHour = v; break;
                case "FailedPrintPct": FailedPrintPct = v; break;
                case "FailedPayback": FailedPayback = v != 0 ? 1 : 0; break;
                case "FailedPaybackPct": if (v >= 0) FailedPaybackPct = v; break;
                case "FailedPaybackWho": if (v == 1 || v == 2) FailedPaybackWho = v; break;
                case "MinimumPrice": MinimumPrice = v; break;
                case "RoundTo": RoundTo = v; break;
                case "LastFilamentPrice": LastFilamentPrice = v; break;
                case "LastElectricityPrice": LastElectricityPrice = v; break;
                case "AutoWearRate": AutoWearRate = v; break;
                case "AutoPowerPrice": AutoPowerPrice = v; break;
                case "AutoUpdateCheck": AutoUpdateCheck = v; break;
                case "PowerAddOn":PowerAddOn = v; break;
                case "PowerIncludeVat": PowerIncludeVat = v; break;
                case "QuoteValidDays": if (v >= 1 && v <= 3650) QuoteValidDays = Math.Round(v); break;
                case "QuoteLook": QuoteDark = v != 0 ? 1 : 0; break;
                case "InvoiceDueDays": if (v >= 1 && v <= 365) InvoiceDueDays = Math.Round(v); break;
                case "SettingsVersion": SettingsVersion = Math.Floor(v); break;
                case "LastPrinterId": if (v >= 1 && v < 1e9) LastPrinterId = Math.Floor(v); break;
            }
        }

        void SetString(string key, string v)
        {
            switch (key)
            {
                case "SlicerPath": SlicerPath = v; break;
                // older versions stored full names like "0.20mm Standard @BBL A1"
                case "ProcessPreset": if (v.Length > 0) ProcessPreset = BambuSlicer.ShortName(v); break;
                case "FilamentPreset": if (v.Length > 0) FilamentPreset = BambuSlicer.ShortName(v); break;
                case "Nozzle": if (Wear.FindNozzle(v).Key == v) Nozzle = v; break;
                case "BedType": if (v.Length > 0) BedType = v; break;
                case "PriceArea": if (v == "DK1" || v == "DK2") PriceArea = v; break;
                case "Language": if (v == "" || v == "en" || v == "da") Language = v; break;
                case "PrinterName": PrinterName = CleanPrinterName(v); break;
                case "PrinterModel": PrinterModel = Wear.FindModel(v).Key; break;
                case "ActivePrinter": if (ValidPrinterId(v)) ActivePrinterId = v; break;
                case "QuoteName": QuoteName = v; break;
                case "QuotePrice": if (v == QuotePriceCustomer || v == QuotePriceFriends) QuotePrice = v; break;
                case "PayMobilePay": PayMobilePay = v; break;
                case "PayBank": PayBank = v; break;
                case "UsedInvoiceNos": UsedInvoiceNos = v; break;
            }
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            bool read = false;
            try
            {
                if (!File.Exists(FilePath)) return s;   // new install: version 2 defaults
                s.SettingsVersion = 0;                  // a file without the key is from before version 2
                s.ActivePrinterId = "";                 // not in the file = the first printer
                string firstId = "1";
                var stringKeys = s.Strings();
                var extraIds = new List<string>();      // other printers, in file order
                var extras = new Dictionary<string, AppSettings>(StringComparer.Ordinal);
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                    if (key == FilamentPriceKey) { s.ReadFilamentPrice(val); continue; }
                    if (key == PrinterIdKey) { if (ValidPrinterId(val)) firstId = val; continue; }
                    AppSettings target = s;
                    if (key.StartsWith(PrinterLinePrefix, StringComparison.Ordinal))   // Printer.<id>.<key>
                    {
                        int dot = key.IndexOf('.', PrinterLinePrefix.Length);
                        if (dot < 0) continue;
                        string id = key.Substring(PrinterLinePrefix.Length, dot - PrinterLinePrefix.Length);
                        key = key.Substring(dot + 1);
                        if (!ValidPrinterId(id) || (Array.IndexOf(PrinterNumberKeys, key) < 0
                            && Array.IndexOf(PrinterStringKeys, key) < 0)) continue;
                        if (!extras.TryGetValue(id, out target))
                        {
                            target = new AppSettings();
                            extras[id] = target;
                            extraIds.Add(id);
                        }
                    }
                    if (stringKeys.ContainsKey(key)) { target.SetString(key, val); continue; }
                    double v;
                    if (double.TryParse(val, NumberStyles.Float, Inv, out v) && v >= 0) target.SetNumber(key, v);
                }
                // printer list: the flat keys = the first printer, then the others (a doubled id is skipped)
                s.printers = new List<PrinterProfile> { s.FromFields(firstId) };
                s.NoteId(firstId);
                foreach (var id in extraIds)
                {
                    if (s.PrinterIndex(id) >= 0) continue;
                    s.printers.Add(extras[id].FromFields(id));
                    s.NoteId(id);
                }
                int active = s.PrinterIndex(s.ActivePrinterId);
                if (active < 0) s.ActivePrinterId = firstId;
                else if (active > 0) s.ToFields(s.printers[active]);
                read = true;
            }
            catch { /* fall back to defaults */ }
            if (read && s.SettingsVersion < CurrentSettingsVersion)   // unreadable file: no step, nothing saved
            {
                s.UpgradeOldDefaults();
                s.Save();   // writes SettingsVersion=2, so the step runs only once
            }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var lines = new List<string>();
                SyncPrinters();
                var nums = Numbers();
                var strs = Strings();
                PutPrinter(nums, strs, printers[0]);   // the flat keys always hold the first printer
                foreach (var kv in nums)
                    lines.Add(kv.Key + "=" + kv.Value.ToString("R", Inv));
                foreach (var kv in strs)
                    lines.Add(kv.Key + "=" + Line(kv.Value));
                lines.Add(PrinterIdKey + "=" + printers[0].Id);
                for (int i = 1; i < printers.Count; i++)
                {
                    var pn = new Dictionary<string, double>();
                    var ps = new Dictionary<string, string>();
                    PutPrinter(pn, ps, printers[i]);
                    string pre = PrinterLinePrefix + printers[i].Id + ".";
                    foreach (var kv in pn) lines.Add(pre + kv.Key + "=" + kv.Value.ToString("R", Inv));
                    foreach (var kv in ps) lines.Add(pre + kv.Key + "=" + Line(kv.Value));
                }
                if (FilamentPrices != null)
                {
                    var names = new List<string>(FilamentPrices.Keys);
                    names.Sort(StringComparer.Ordinal);
                    foreach (var n in names)
                    {
                        string clean = CleanName(n);
                        double v = FilamentPrices[n];
                        if (clean.Length == 0 || !ValidPrice(v)) continue;
                        lines.Add(FilamentPriceKey + "=" + v.ToString("R", Inv) + "|" + clean);
                    }
                }
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch { /* non-fatal */ }
        }

        static string Line(string v) { return (v ?? "").Replace("\r", "").Replace("\n", ""); }

        public AppSettings Clone()
        {
            var c = (AppSettings)MemberwiseClone();
            c.FilamentPrices = FilamentPrices != null
                ? new Dictionary<string, double>(FilamentPrices, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal);
            if (printers != null)
            {
                c.printers = new List<PrinterProfile>(printers.Count);
                foreach (var p in printers) c.printers.Add(p.Clone());
            }
            return c;
        }
    }

    /// One printer (AppSettings.Printers). Id never changes and is never reused; History saves it with each quote.
    public class PrinterProfile
    {
        public string Id = "1";
        public string Name = AppSettings.DefaultPrinterName;   // trimmed, max 40, empty = "Bambu Lab A1"
        public string Model = Wear.ModelA1;                    // Wear.Models key
        public double Watts = 100;
        public double AutoWearRate = 1;                        // 1 = wear rate from model, nozzle, filament & plate
        public double MachineRatePerHour = Wear.DefaultRate;   // kr/h
        public string Nozzle = "0.4 stainless";
        public string BedType = "High Temp Plate";
        public string ProcessPreset = "0.16mm Optimal";

        public PrinterProfile Clone() { return (PrinterProfile)MemberwiseClone(); }

        /// Fixes bad values in place: name (CleanPrinterName), unknown model -> A1, unknown nozzle -> stock,
        /// empty bed type / profile -> defaults, negative or NaN numbers -> defaults.
        public void Clean()
        {
            Name = AppSettings.CleanPrinterName(Name);
            Model = Wear.FindModel(Model).Key;
            if (Wear.FindNozzle(Nozzle).Key != Nozzle) Nozzle = Wear.Nozzles[0].Key;
            if (string.IsNullOrEmpty(BedType)) BedType = Wear.Plates[0].Key;
            if (string.IsNullOrEmpty(ProcessPreset)) ProcessPreset = "0.16mm Optimal";
            if (!(Watts >= 0) || double.IsInfinity(Watts)) Watts = Wear.FindModel(Model).DefaultWatts;
            if (!(MachineRatePerHour >= 0) || double.IsInfinity(MachineRatePerHour)) MachineRatePerHour = Wear.DefaultRateFor(Model);
            AutoWearRate = AutoWearRate != 0 ? 1 : 0;
        }
    }

    /// Command-line options for a normal start (picture mode "--screenshots" is handled before this in Program).
    /// --settings "<folder>"  separate settings folder (test mode)
    /// --hidden               off-screen, no taskbar button (needs --settings)
    /// --minimized            starts minimized (needs --settings; not together with --hidden)
    /// Any other argument starting with "--" is an error, so a typo never silently runs on the real settings.
    public class StartOptions
    {
        public string SettingsFolder;   // null = normal start on the real settings
        public bool Hidden;
        public bool Minimized;
        public string Error;            // null = OK

        public static StartOptions Parse(string[] args)
        {
            var o = new StartOptions();
            if (args == null) return o;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? "";
                if (a == "--settings")
                {
                    if (o.SettingsFolder != null) return Fail(o, "--settings given twice");
                    if (i + 1 >= args.Length || string.IsNullOrEmpty(args[i + 1]) || args[i + 1].StartsWith("--"))
                        return Fail(o, "--settings needs a folder");
                    o.SettingsFolder = args[++i];
                }
                else if (a == "--hidden") o.Hidden = true;
                else if (a == "--minimized") o.Minimized = true;
                else if (a.StartsWith("--")) return Fail(o, "unknown option " + a);
                // plain arguments (e.g. a file dropped on the exe) are ignored, as before
            }
            if ((o.Hidden || o.Minimized) && o.SettingsFolder == null) return Fail(o, "--hidden / --minimized need --settings");
            if (o.Hidden && o.Minimized) return Fail(o, "use --hidden or --minimized, not both");
            return o;
        }

        static StartOptions Fail(StartOptions o, string error)
        {
            o.Error = error;
            return o;
        }
    }
}
