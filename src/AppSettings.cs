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
        public double MinimumPrice = 0;         // DKK, customer price floor
        public double RoundTo = 0;              // round prices up to nearest N kr. (0 = off)
        public double LastFilamentPrice = 180;  // DKK/kg
        public double LastElectricityPrice = 2.00; // DKK/kWh

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

        public string Language = "";            // "" = auto (Danish if Windows is Danish), "en", "da"

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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
            d["MinimumPrice"] = MinimumPrice;
            d["RoundTo"] = RoundTo;
            d["LastFilamentPrice"] = LastFilamentPrice;
            d["LastElectricityPrice"] = LastElectricityPrice;
            d["AutoWearRate"] = AutoWearRate;
            d["AutoPowerPrice"] = AutoPowerPrice;
            d["AutoUpdateCheck"] = AutoUpdateCheck;
            d["PowerAddOn"] = PowerAddOn;
            d["PowerIncludeVat"] = PowerIncludeVat;
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
                case "MinimumPrice": MinimumPrice = v; break;
                case "RoundTo": RoundTo = v; break;
                case "LastFilamentPrice": LastFilamentPrice = v; break;
                case "LastElectricityPrice": LastElectricityPrice = v; break;
                case "AutoWearRate": AutoWearRate = v; break;
                case "AutoPowerPrice": AutoPowerPrice = v; break;
                case "AutoUpdateCheck": AutoUpdateCheck = v; break;
                case "PowerAddOn":PowerAddOn = v; break;
                case "PowerIncludeVat": PowerIncludeVat = v; break;
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
            }
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var stringKeys = s.Strings();
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                    if (stringKeys.ContainsKey(key)) { s.SetString(key, val); continue; }
                    double v;
                    if (double.TryParse(val, NumberStyles.Float, Inv, out v) && v >= 0) s.SetNumber(key, v);
                }
            }
            catch { /* fall back to defaults */ }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var lines = new List<string>();
                foreach (var kv in Numbers())
                    lines.Add(kv.Key + "=" + kv.Value.ToString("R", Inv));
                foreach (var kv in Strings())
                    lines.Add(kv.Key + "=" + (kv.Value ?? "").Replace("\r", "").Replace("\n", ""));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch { /* non-fatal */ }
        }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
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
