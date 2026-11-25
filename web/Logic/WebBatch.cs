using System;
using System.Collections.Generic;
using System.Linq;

namespace PrintPrice.Web
{
    /// Batch & plates on the website (issue #9 / #10). The website can't slice, so there are three cases:
    /// - the file already holds the batch (copies arranged in the slicer): copies / plates read from it, exact (FileCopies);
    /// - one copy (one-plate Bambu file, G-code, typed): the user types Copies and Per plate (per plate estimated from the
    ///   file's footprint when it has one); totals = Pricing.ForCopies with the tower / colour-change parts per plate,
    ///   marked "≈" when more than one copy shares a plate;
    /// - a saved batch opened again: its totals, copies as saved.
    /// A file with 2+ plates that isn't a batch is printed N times (everything x N, exact).
    /// Pure (no browser calls): web\tests runs it.
    public static class WebBatch
    {
        /// The copies a sliced file already holds: objects grouped by CopyBaseName ("benchy.stl_3" -> "benchy.stl"); when
        /// every group has the same count n >= 2 the file is n copies of one set (perPlate = most sets on one plate).
        /// Anything else (one object, different models once each, uneven counts) = 1, perPlate 0.
        public static int FileCopies(SliceResult res, out int perPlate)
        {
            perPlate = 0;
            if (res == null || res.PlateList == null || res.PlateList.Count == 0) return 1;
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int total = 0;
            foreach (var p in res.PlateList)
            {
                if (p == null || p.Objects == null) continue;
                foreach (var o in p.Objects)
                {
                    string key = BambuSlicer.CopyBaseName((o ?? "").Trim());
                    int c;
                    counts.TryGetValue(key, out c);
                    counts[key] = c + 1;
                    total++;
                }
            }
            if (total < 2 || counts.Count == 0) return 1;
            int n = counts.Values.First();
            if (n < 2 || counts.Values.Any(c => c != n)) return 1;
            int sets = counts.Count;
            foreach (var p in res.PlateList)
                if (p != null && p.Objects != null)
                    perPlate = Math.Max(perPlate, BambuSlicer.CopiesPerPlate(p.Objects.Count, sets));
            return n;
        }

        /// The estimate of copies on one plate from a one-plate file's footprint (0 = no footprint / doesn't fit).
        /// printerKey = the file's printer (else Settings'); unknown = A1.
        public static int AutoPerPlate(PlateFootprint fp, string printerKey)
        {
            if (fp == null) return 0;
            return BambuSlicer.CopiesPerPlate(printerKey ?? "", fp.Width, fp.Depth, fp.TowerWidth, fp.TowerDepth);
        }

        /// A Copies / Per plate box: whole number 1..MaxCopies. Empty = Empty (the page uses 1).
        public static FieldState ReadCount(string text, out int value)
        {
            var st = WebCalc.ReadInt(text, BambuSlicer.MaxCopies, out value);
            if (st == FieldState.Ok && value < 1) { value = 0; return FieldState.Invalid; }
            return st;
        }

        /// The once-per-plate grams inside one copy's FilamentGrams: 2+ colours = the purge (flush + tower), else the tower.
        public static double PlateGrams(double purge, double tower)
        {
            if (purge > 0) return purge;
            return tower > 0 ? tower : 0;
        }

        /// The once-per-plate seconds inside one copy's time: colour-change time + tower time (each when known).
        public static double PlateSeconds(double changeHours, double towerSeconds)
        {
            return (changeHours > 0 ? changeHours * 3600.0 : 0) + (towerSeconds > 0 ? towerSeconds : 0);
        }

        /// One colour line's once-per-plate grams: Grams - ModelGrams when the model part is known, else its tower.
        public static double LinePlateGrams(FilamentUse f)
        {
            if (f == null) return 0;
            if (f.ModelGrams >= 0 && f.Grams >= f.ModelGrams) return f.Grams - f.ModelGrams;
            return f.TowerGrams > 0 ? Math.Min(f.TowerGrams, Math.Max(0, f.Grams)) : 0;
        }

        /// What the page prices.
        public sealed class Plan
        {
            public int Copies = 1, Plates = 1, PerPlate;
            public bool Estimated;        // 2+ copies share a plate in a one-copy estimate ("≈")
            public bool Multiplied;       // a multi-plate file printed N times (everything x N)
        }

        /// The one-copy case: copies on plates of perPlate (<= 0 = 1); a multi-plate file (filePlates >= 2) = the whole
        /// file N times (perPlate not used).
        public static Plan OneCopy(int copies, int perPlate, int filePlates)
        {
            if (copies < 1) copies = 1;
            var p = new Plan { Copies = copies };
            if (filePlates >= 2)
            {
                p.Plates = (int)Math.Min((long)filePlates * copies, History.MaxStoredCount);
                p.PerPlate = 0;
                p.Multiplied = copies > 1;
                return p;
            }
            if (perPlate < 1) perPlate = 1;
            if (perPlate > copies) perPlate = copies;
            p.PerPlate = perPlate;
            p.Plates = Pricing.PlatesNeeded(copies, perPlate);
            p.Estimated = copies > 1 && perPlate > 1;
            return p;
        }

        /// The QuoteInput of the plan from one copy's input (copies 1 = one unchanged). Multiplied: all parts x N.
        public static QuoteInput Totals(QuoteInput one, Plan plan, double plateGrams, double plateSeconds, IList<double> lineGrams)
        {
            if (one == null || plan == null || plan.Copies <= 1) return one;
            if (plan.Multiplied) return Pricing.ForCopies(one, plan.Copies, plan.Plates, 0, 0, null);
            return Pricing.ForCopies(one, plan.Copies, plan.Plates, plateGrams, plateSeconds, lineGrams);
        }

        /// A once-per-plate value of one copy as a total: x plates (Multiplied: x copies); unknown (< 0) stays.
        public static double PerPlateTotal(double one, Plan plan)
        {
            if (one < 0 || plan == null || plan.Copies <= 1) return one;
            return one * (plan.Multiplied ? plan.Copies : plan.Plates);
        }

        /// "In all: ≈ 18 h 0 m · 240 g on 3 plates" / "I alt: ≈ 18 t 0 m · 240 g på 3 plader" (the totals line under Copies).
        public static string TotalsText(QuoteInput total, bool estimated)
        {
            if (total == null) return "";
            string g = Math.Round(Math.Max(0, total.FilamentGrams), 1).ToString("0.#", Num.Dk) + " g";
            string s = L.S("In all: ", "I alt: ") + (estimated ? "≈ " : "") + QuoteRows.Time(total.Hours, total.Minutes) + " · " + g;
            string on = History.OnPlatesText(total.Plates);
            return on.Length > 0 ? s + " " + on : s;
        }

        /// "≈ 28 fit" / "plads til ≈ 28": the hint next to Per plate (estimated from the file).
        public static string AutoText(int auto)
        {
            return L.F("≈ {0} fit", "plads til ≈ {0}", auto);
        }

        /// "Per plate must be 1 to 500." / "Pr. plade skal være 1 til 500." (the Per plate box's own range text).
        public static string PerPlateRangeText()
        {
            return L.F("Per plate must be 1 to {0}.", "Pr. plade skal være 1 til {0}.", BambuSlicer.MaxCopies);
        }

        /// The read-only line of a file / saved record that already is a batch: "12 pcs on 3 plates (from the file)".
        public static string FixedText(int copies, int plates, bool fromFile, bool estimated)
        {
            string s = (estimated ? "≈ " : "") + History.PiecesText(copies);
            string on = History.OnPlatesText(plates);
            if (on.Length > 0) s += " " + on;
            return s + " " + (fromFile ? L.S("(from the file)", "(fra filen)") : L.S("(as saved)", "(som gemt)"));
        }

        /// The label suffix of the time / grams boxes while they hold one copy of 2+.
        public static string OneCopyLabel() { return L.S("(1 copy)", "(1 stk.)"); }

        /// The help line under the row for an estimate.
        public static string EstimateHint()
        {
            return L.S("Estimate. For exact numbers, arrange the copies in Bambu Studio and load the sliced file.",
                       "Et skøn. Præcise tal: placér kopierne i Bambu Studio og åbn den slicede fil.");
        }

        // ---------- batch-2: colours sharing plates (the website reads them from the file, it never builds them) ----------

        /// Plates of the file that print 2+ colours (BambuSlicer.IsMixedPlate: filaments of 0,5 g or more).
        public static int MixedPlates(IList<PlateInfo> plates)
        {
            int n = 0;
            if (plates != null) foreach (var p in plates) if (p != null && BambuSlicer.IsMixedPlate(p)) n++;
            return n;
        }

        /// A plate's colours for its dots (filaments of 0,5 g or more, slot order, as the desktop's plates menu).
        public static List<string> PlateColours(PlateInfo p)
        {
            var res = new List<string>();
            if (p == null || p.Filaments == null) return res;
            foreach (var f in p.Filaments.Where(x => x != null && x.Grams >= 0.5).OrderBy(x => x.Slot)) res.Add(f.Colour ?? "");
            return res;
        }

        /// The plates list's heading: "4 plates – the price is for all of them:"; with mixed-colour plates the shared
        /// History.MixedPlatesText first ("4 plates, 1 with mixed colours – the price is for all of them:"). ui-text #5: a dash in both.
        public static string PlatesHeading(int count, int mixed)
        {
            string first = mixed <= 0 ? L.F("{0} plates", "{0} plader", count) : History.MixedPlatesText(count, mixed);
            return first + " – " + (count == 2 ? L.S("the price is for both:", "prisen gælder dem begge:")
                                               : L.S("the price is for all of them:", "prisen gælder dem alle:"));
        }

        /// The hint for a multi-plate file printed N times.
        public static string MultipliedHint(int filePlates)
        {
            return L.F("The whole file ({0} plates) is printed once per copy.", "Hele filen ({0} plader) printes én gang pr. stk.", filePlates);
        }
    }
}
