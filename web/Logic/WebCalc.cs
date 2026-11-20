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

        /// No price yet (as the desktop's MainForm): the grams box is empty, or both time boxes are empty.
        /// Save to history and Show quote are off then.
        public static bool Incomplete(FieldState hours, FieldState minutes, FieldState grams)
        {
            return grams == FieldState.Empty || (hours == FieldState.Empty && minutes == FieldState.Empty);
        }

        // ---------- the note under the colour rows (MainForm.PurgeNote / TowerText, issue #8 step 3) ----------

        /// The parts of "incl. 6 g for colour changes · prime tower 1,7 g · 5 min" (2+ colours): the colour-change part
        /// (flush only = purge − tower; grams above 0,5 g, hours from 0,05 h) and the tower part (grams > 0; the time only
        /// when known, which the website normally isn't). Tower unknown (-1) or 0 = no tower part; a part alone starts with
        /// a capital. The page joins them with " · " (each part kept on one line). Empty list = no note.
        public static List<string> PurgeNoteParts(double purge, double changeHours, double tower, double towerSeconds)
        {
            var parts = new List<string>();
            double t = tower > 0.005 ? tower : 0;
            double flush = purge > 0 ? Math.Max(0, purge - t) : purge;
            bool g = flush > 0.5, h = changeHours >= 0.05;
            string grams = g ? flush.ToString(flush >= 10 ? "0" : "0.#", Num.Dk) : "";
            string hours = h ? changeHours.ToString("0.#", Num.Dk) : "";
            if (g && h) parts.Add(L.F("incl. {0} g and {1} h for colour changes", "heraf {0} g og {1} t til farveskift", grams, hours));
            else if (g) parts.Add(L.F("incl. {0} g for colour changes", "heraf {0} g til farveskift", grams));
            else if (h) parts.Add(L.F("{0} h for colour changes", "{0} t til farveskift", hours));
            string tt = TowerText(t, towerSeconds, true);
            if (tt.Length > 0) parts.Add(parts.Count == 0 ? char.ToUpper(tt[0]) + tt.Substring(1) : tt);
            return parts;
        }

        /// "prime tower 1,7 g · 5 min" (DA "primetårn …"); withTime false / seconds unknown = grams only; grams <= 0 = ""
        /// (= MainForm.TowerText).
        public static string TowerText(double grams, double seconds, bool withTime)
        {
            if (!(grams > 0.005)) return "";
            string s = L.F("prime tower {0} g", "primetårn {0} g", grams.ToString("0.0", Num.Dk));   // one decimal (as the desktop)
            if (withTime && seconds >= 30)
            {
                int min = (int)Math.Round(seconds / 60);
                s += " · " + (min >= 60 ? (min / 60) + L.S(" h ", " t ") + (min % 60) + " min" : min + " min");
            }
            return s;
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

        /// Before this tab writes its settings: what another open tab stored meanwhile and must never be lost is taken
        /// in (A8): invoice codes given out there (UsedInvoiceNos, both lists, the stored ones first, cap kept) and the
        /// higher LastPrinterId. Everything else stays this tab's. Unreadable / empty stored text = nothing changes.
        /// True when mine changed.
        public static bool MergeStored(AppSettings mine, string storedText)
        {
            if (mine == null || string.IsNullOrEmpty(storedText)) return false;
            AppSettings stored;
            try { bool up; stored = SettingsFromText(storedText, out up); }
            catch (Exception) { return false; }
            if (stored == null) return false;
            bool changed = false;
            var have = new HashSet<string>(StringComparer.Ordinal);
            foreach (var u in (mine.UsedInvoiceNos ?? "").Split(',')) if (u.Trim().Length > 0) have.Add(u.Trim());
            string merged = null;
            foreach (var u in (stored.UsedInvoiceNos ?? "").Split(','))
            {
                string code = u.Trim();
                if (code.Length == 0 || have.Contains(code)) continue;
                have.Add(code);
                merged = History.AddUsedInvoiceNo(merged ?? "", code);
            }
            if (merged != null)
            {
                // theirs first (older), then mine: the newest stay when the cap cuts
                string all = merged;
                foreach (var u in (mine.UsedInvoiceNos ?? "").Split(',')) if (u.Trim().Length > 0) all = History.AddUsedInvoiceNo(all, u.Trim());
                mine.UsedInvoiceNos = all;
                changed = true;
            }
            if (stored.LastPrinterId > mine.LastPrinterId && mine.ReservePrinterId(((long)stored.LastPrinterId).ToString(CultureInfo.InvariantCulture)))
                changed = true;
            return changed;
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
