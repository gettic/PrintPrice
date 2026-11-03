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
        public string ProcessPreset = "0.20mm Standard"; // short names; "@BBL A1 …" is added per nozzle
        public string FilamentPreset = "Bambu PLA Basic";
        public string BedType = "High Temp Plate";      // = Smooth PEI plate (Bambu's internal name)

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PrintPrice", "settings.ini");
            }
        }

        Dictionary<string, double> Numbers()
        {
            var d = new Dictionary<string, double>();
            d["PrinterWatts"] = PrinterWatts;
            d["FriendsMarkupPct"] = FriendsMarkupPct;
            d["OthersMarkupPct"] = OthersMarkupPct;
            d["MachineRatePerHour"] = MachineRatePerHour;
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
}
