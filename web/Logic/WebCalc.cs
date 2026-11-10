using System;
using System.Collections.Generic;
using System.Globalization;

namespace PrintPrice.Web
{
    /// The web page's glue around the desktop price code (no browser calls here, so web\tests can run it).
    public static class WebCalc
    {
        /// Filament types for the wear rate (the desktop reads the type from the Bambu Studio profile; the web asks).
        /// Types ending in -CF / -GF are abrasive.
        public static readonly string[] FilamentTypes = { "PLA", "PETG", "ABS", "ASA", "TPU", "PC", "PA", "PLA-CF", "PETG-CF", "PA-CF", "PA-GF" };

        public static bool IsAbrasive(string type)
        {
            string t = (type ?? "").ToUpperInvariant();
            return t.EndsWith("-CF") || t.EndsWith("-GF");
        }

        public static string CleanType(string type)
        {
            return Array.IndexOf(FilamentTypes, type) >= 0 ? type : "PLA";
        }

        /// The wear split behind the auto rate: model, nozzle, filament type and plate (same as MainForm.WearBreakdown).
        public static Wear.Breakdown WearBreakdown(AppSettings s, string filamentType)
        {
            string type = CleanType(filamentType);
            return Wear.Calculate(Wear.FindModel(s.PrinterModel), Wear.FindNozzle(s.Nozzle), IsAbrasive(type), type,
                Wear.FindPlate(s.BedType));
        }

        /// Auto wear on: MachineRatePerHour = the calculated rate (as MainForm does at startup and after Settings).
        public static void ApplyAutoWear(AppSettings s, string filamentType)
        {
            if (s.AutoWearRate != 0) s.MachineRatePerHour = WearBreakdown(s, filamentType).Total;
        }

        /// A number box: empty, invalid (not a number or negative) or OK.
        public static FieldState Read(string text, out double value)
        {
            value = 0;
            if ((text ?? "").Trim().Length == 0) return FieldState.Empty;
            return Num.TryParse(text, out value) && !double.IsInfinity(value) ? FieldState.Ok : FieldState.Invalid;
        }

        /// A whole-number box (hours 0-999, minutes 0-59); empty = 0.
        public static FieldState ReadInt(string text, int max, out int value)
        {
            value = 0;
            string t = (text ?? "").Trim();
            if (t.Length == 0) return FieldState.Empty;
            int v;
            if (!int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out v) || v > max) return FieldState.Invalid;
            value = v;
            return FieldState.Ok;
        }

        // ---------- settings in the browser ----------
        // The desktop settings.ini text (AppSettings.ToIni / FromIni), so the same keys and checks apply.

        public static string SettingsToText(AppSettings s)
        {
            return string.Join("\n", s.ToIni());
        }

        /// null / empty = defaults. upgraded = an old settings text got the new defaults: store it again once.
        public static AppSettings SettingsFromText(string text, out bool upgraded)
        {
            return AppSettings.FromIni(string.IsNullOrEmpty(text) ? null : text.Replace("\r", "").Split('\n'), out upgraded);
        }

        // ---------- web-only values (not in the desktop INI) ----------

        /// Web-only values: filament type and the last job's time and grams. Saved as key=value lines.
        public class Prefs
        {
            public string FilamentType = "PLA";
            public string Hours = "", Minutes = "", Grams = "";

            public string ToText()
            {
                return "FilamentType=" + Line(FilamentType) + "\nHours=" + Line(Hours) + "\nMinutes=" + Line(Minutes) + "\nGrams=" + Line(Grams);
            }

            public static Prefs FromText(string text)
            {
                var p = new Prefs();
                foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                    if (v.Length > 20) v = v.Substring(0, 20);
                    switch (k)
                    {
                        case "FilamentType": p.FilamentType = CleanType(v); break;
                        case "Hours": p.Hours = v; break;
                        case "Minutes": p.Minutes = v; break;
                        case "Grams": p.Grams = v; break;
                    }
                }
                return p;
            }

            static string Line(string v) { return (v ?? "").Replace("\r", "").Replace("\n", ""); }
        }
    }
}
