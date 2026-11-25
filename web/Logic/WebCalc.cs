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

        // ---------- batch-2: quantity discount (Settings boxes, price box line / tooltip) ----------

        /// One quantity-discount step's two boxes (= SettingsForm.ReadQtyStep): both empty (or 0) = off (0, 0, true). Else
        /// From must be whole pieces 2..History.MaxStoredCount and % above 0 up to AppSettings.MaxQtyDiscountPct; badFrom /
        /// badPct say which box is red. Returns false when one is bad.
        public static bool ReadQtyStep(string fromText, string pctText, out double from, out double pct, out bool badFrom, out bool badPct)
        {
            from = 0; pct = 0; badFrom = false; badPct = false;
            string a = (fromText ?? "").Trim(), b = (pctText ?? "").Trim();
            double fv = 0, pv = 0;
            bool fEmpty = a.Length == 0 || (Num.TryParse(a, out fv) && fv == 0);
            bool pEmpty = b.Length == 0 || (Num.TryParse(b, out pv) && pv == 0);
            if (fEmpty && pEmpty) return true;
            badFrom = fEmpty || !Num.TryParse(a, out fv) || AppSettings.CleanQtyFrom(fv) <= 0;
            badPct = pEmpty || !Num.TryParse(b, out pv) || AppSettings.CleanQtyPct(pv) <= 0;
            if (badFrom || badPct) return false;
            from = fv; pct = pv;
            return true;
        }

        /// A step's box text in Settings: the number when the step is on (both set), else "" (= SettingsForm.FillGeneral).
        public static string QtyBoxText(double from, double pct, bool fromBox)
        {
            bool on = AppSettings.CleanQtyFrom(from) > 0 && AppSettings.CleanQtyPct(pct) > 0;
            return on ? Num.Fmt(fromBox ? from : pct) : "";
        }

        /// The Settings line when only a discount step is bad (= SettingsForm.QtyBadText).
        public static string QtyBadText()
        {
            return L.F("Quantity discount: from 2 pcs, up to {0} %, or both empty.", "Mængderabat: fra 2 stk., højst {0} %, eller begge tomme.",
                       AppSettings.MaxQtyDiscountPct);
        }

        /// The price box's grey line with a discount: "40 pcs · 5,31 kr. each · –5 %" (= MainForm.WithDiscountMark; the web
        /// line wraps, so the mark is always added). discount = kr. taken off this price (0 = none).
        /// The mark only when the cut really is the step's % (Pricing.ShownDiscountPct: rounding / the minimum / the cost floor
        /// changed nothing), as the desktop.
        public static string DiscountMark(string line, QuoteResult r, bool friends)
        {
            double pct = Pricing.ShownDiscountPct(r, friends);
            return pct > 0 ? line + " · " + History.DiscountShortText(pct) : line;
        }

        /// The price box tooltip's discount lines (= MainForm.DiscountTip): "\nBefore discount: 727,20 kr.\nQuantity discount
        /// 5 % (from 10 pcs): –36,36 kr."; a cut that isn't the % (ShownDiscountPct 0): only "\nIncl. quantity discount (from
        /// 10 pcs)"; "" without a discount.
        public static string DiscountTip(QuoteResult r, bool friends)
        {
            if (!Pricing.HasDiscount(r, friends)) return "";
            double pct = Pricing.ShownDiscountPct(r, friends);
            double price = friends ? r.FriendsPrice : r.OthersPrice, discount = friends ? r.FriendsDiscount : r.OthersDiscount;
            if (!(pct > 0))
                return "\n" + L.F("Incl. quantity discount (from {0})", "Inkl. mængderabat (fra {0})", History.PiecesText(r.DiscountFrom));
            return "\n" + L.F("Before discount: {0}", "Før rabat: {0}", Num.Kr(price + discount)) +
                   "\n" + L.F("Quantity discount {0} (from {1}): {2}", "Mængderabat {0} (fra {1}): {2}", History.PctText(pct),
                              History.PiecesText(r.DiscountFrom), "–" + Num.Kr(discount));
        }

        /// "How the price is made": the discount line's name and detail ("Quantity discount 5 %", "from 10 pcs").
        public static string DiscountLineName(QuoteLine l)
        {
            return l != null && l.Quantity > 0 ? History.DiscountText(l.Quantity, 0) : L.S("Quantity discount", "Mængderabat");
        }

        public static string DiscountLineDetail(QuoteLine l)
        {
            int from = l == null ? 0 : (int)Math.Round(l.UnitPrice);
            return from > 0 ? L.F("from {0}", "fra {0}", History.PiecesText(from)) : "";
        }

        /// A breakdown amount: "–36,36 kr." for a negative one (en dash, as on the quote), else Num.Kr.
        public static string Amount(double v)
        {
            return v < 0 ? "–" + Num.Kr(-v) : Num.Kr(v);
        }

        /// No price yet (as the desktop's MainForm): the grams box is empty, or both time boxes are empty.
        /// Save to history and Show quote are off then.
        public static bool Incomplete(FieldState hours, FieldState minutes, FieldState grams)
        {
            return grams == FieldState.Empty || (hours == FieldState.Empty && minutes == FieldState.Empty);
        }

        // ---------- the note under the colour rows (shared QuoteRows.PurgeNoteParts / PurgeNoteJoin / TowerText) ----------

        /// The parts of "incl. 6 g for colour changes · prime tower 1,7 g · 5 min" (2+ colours) from the shared
        /// QuoteRows.PurgeNoteParts; a part alone starts with a capital (QuoteRows.PurgeNoteJoin). The page joins them with
        /// " · " (each part kept on one line). Empty list = no note.
        public static List<string> PurgeNoteParts(double purge, double changeHours, double tower, double towerSeconds)
        {
            string note, tw;
            QuoteRows.PurgeNoteParts(purge, changeHours, tower, towerSeconds, out note, out tw);
            var parts = new List<string>();
            if (note.Length > 0) parts.Add(note);
            if (tw.Length > 0) parts.Add(note.Length == 0 ? QuoteRows.PurgeNoteJoin("", tw, false) : tw);
            return parts;
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
