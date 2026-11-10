using System;
using System.Collections.Generic;
using System.Globalization;

namespace PrintPrice
{
    /// One filament (colour/slot) of a multi-colour print. Grams include purge.
    public class FilamentLine
    {
        public double Grams;
        public double PricePerKg;   // DKK/kg; the ui defaults it to the main filament price
    }

    public class QuoteInput
    {
        public int Hours;
        public int Minutes;
        public double FilamentGrams;
        public double FilamentDkkPerKg;
        public double ElectricityDkkPerKwh;
        // Optional: 2+ lines = material is the sum of the lines (FilamentGrams/FilamentDkkPerKg are then not used
        // for the cost). Null, empty or 1 line = today's single-filament formula.
        public List<FilamentLine> Filaments;
        // Failed prints not made back yet (History.FailedDebt), DKK. 0 = none. Used only when AppSettings.FailedPayback is on.
        public double PaybackDebt;
    }

    public class QuoteResult
    {
        public double PrintHours;
        public double MaterialCost;
        public double[] FilamentCosts;   // per line of QuoteInput.Filaments (unrounded); null for a single filament
        public double PowerKwh;
        public double PowerCost;
        public double FailedPrintCost;   // (material + power) × FailedPrintPct / 100
        public double BaseCost;          // material + power + failed prints
        public double FriendsPrice;
        public double OthersPrice;
        // Failed prints payback: min(debt, (material + power) × FailedPaybackPct / 100), added after markup (not marked up)
        // to the customer price, and to the friends price too when PaybackBothPrices. 0 = none.
        public double PaybackCost;
        public double PaybackLeft;       // debt still left after this quote (debt - PaybackCost); 0 when payback is off
        public bool PaybackBothPrices;   // true = also in FriendsPrice (FailedPaybackWho = 1)
    }

    /// One line of a quote breakdown (Pricing.QuoteBreakdown). Amount is whole øre; texts belong to the ui (by Key).
    public class QuoteLine
    {
        public const string Material = "material", Power = "power", Wear = "wear", Failed = "failed", Payback = "payback",
            Work = "work", Minimum = "minimum", Rounding = "rounding";
        // wear split (QuoteLine.Parts of the wear line)
        public const string WearPrinter = "wear.printer", WearParts = "wear.parts", WearHotend = "wear.hotend", WearPlate = "wear.plate";

        public string Key;
        public long Ore;                  // the shown amount in øre; Amount = Ore / 100
        public double Amount { get { return Ore / 100.0; } }
        public double Exact;              // unrounded kr.
        public int Index = -1;            // material: the colour's index in QuoteInput.Filaments (0 for one filament)
        // Detail values (0 when not used):
        //   material: Quantity = grams, UnitPrice = kr./kg      power: Quantity = kWh, UnitPrice = kr./kWh
        //   wear: Quantity = hours, UnitPrice = kr./h (MachineRatePerHour)      failed: Quantity = FailedPrintPct
        //   work: Quantity = markup %      payback: Quantity = FailedPaybackPct
        //   minimum: Quantity = MinimumPrice      rounding: Quantity = RoundTo
        public double Quantity;
        public double UnitPrice;
        public List<QuoteLine> Parts;     // wear only (AutoWearRate on + a Wear.Breakdown given): printer, parts, hotend,
                                          // plate, scaled to the rate used; their Ore sum = this line's Ore. Else null.
    }

    public class QuoteBreakdown
    {
        public bool Friends;              // true = friends & family price, false = customer price
        public List<QuoteLine> Lines;     // top-level lines in order; their Ore sum = TotalOre
        public long TotalOre;             // = the shown price (Num.Kr of Price) in øre
        public double Total { get { return TotalOre / 100.0; } }
        public double Price;              // the price from Pricing.Calculate (FriendsPrice or OthersPrice)
        public QuoteResult Result;        // the full Pricing.Calculate result
    }

    /// Estimated wear & tear per print hour, depending on the printer model, nozzle, filament and build plate.
    public static class Wear
    {
        // Bambu Lab A1 (Models[0]); the other models are in Models.
        public const double PrinterPriceDkk = 2099, PrinterLifeHours = 3000;   // printer paid off before it is worn out or outdated
        // parts and upkeep: PTFE tube, wiper, cutter, belts, extruder gears, rods/bearings, grease, IPA, glue stick, shipping
        public const double PartsPriceDkk = 600, PartsLifeHours = 1500;

        public const string ModelA1 = "A1", ModelA1Mini = "A1M", ModelP1S = "P1S";

        /// A printer model: its own printer and parts cost, hotend/plate price factors (x the Nozzles / Plates prices,
        /// which are the A1's) and typical power use (W) for a new printer of this model.
        public class Model
        {
            public string Key, Label;
            public double PrinterPriceDkk, PrinterLifeHours, PartsPriceDkk, PartsLifeHours;
            public double HotendFactor = 1, PlateFactor = 1;
            public double DefaultWatts;
        }

        public static readonly Model[] Models =
        {
            new Model { Key = ModelA1, Label = "Bambu Lab A1", PrinterPriceDkk = PrinterPriceDkk, PrinterLifeHours = PrinterLifeHours,
                        PartsPriceDkk = PartsPriceDkk, PartsLifeHours = PartsLifeHours, DefaultWatts = 100 },
            // smaller 180 mm plate (~70 % of the A1 plate's price), same hotend as the A1
            new Model { Key = ModelA1Mini, Label = "Bambu Lab A1 mini", PrinterPriceDkk = 1599, PrinterLifeHours = 3000,
                        PartsPriceDkk = 500, PartsLifeHours = 1500, PlateFactor = 0.7, DefaultWatts = 70 },
            // enclosed CoreXY: carbon rods, belts, chamber/aux fans and filters in the parts
            new Model { Key = ModelP1S, Label = "Bambu Lab P1S", PrinterPriceDkk = 4199, PrinterLifeHours = 3000,
                        PartsPriceDkk = 800, PartsLifeHours = 1500, DefaultWatts = 130 },
        };

        /// The model with this key; unknown or empty = A1.
        public static Model FindModel(string key)
        {
            foreach (var m in Models) if (m.Key == key) return m;
            return Models[0];
        }

        public class Nozzle
        {
            public string Key, Label, Size;
            public bool Hardened;
            public double PriceDkk;     // price of a complete A1 hotend with this nozzle
        }

        public static readonly Nozzle[] Nozzles =
        {
            new Nozzle { Key = "0.4 stainless", Label = "0.4 mm stainless steel (stock)", Size = "0.4", PriceDkk = 139 },
            new Nozzle { Key = "0.2 stainless", Label = "0.2 mm stainless steel", Size = "0.2", PriceDkk = 139 },
            new Nozzle { Key = "0.4 hardened", Label = "0.4 mm hardened steel", Size = "0.4", Hardened = true, PriceDkk = 185 },
            new Nozzle { Key = "0.6 hardened", Label = "0.6 mm hardened steel", Size = "0.6", Hardened = true, PriceDkk = 185 },
            new Nozzle { Key = "0.8 hardened", Label = "0.8 mm hardened steel", Size = "0.8", Hardened = true, PriceDkk = 185 },
        };

        public class Plate
        {
            public string Key, Label;   // Key = Bambu Studio's internal bed type name
            public double PriceDkk, LifeHours;
        }

        public static readonly Plate[] Plates =
        {
            new Plate { Key = "High Temp Plate", Label = "Smooth PEI / High Temp Plate", PriceDkk = 200, LifeHours = 600 },
            new Plate { Key = "Textured PEI Plate", Label = "Textured PEI Plate", PriceDkk = 150, LifeHours = 1000 },
            new Plate { Key = "Supertack Plate", Label = "Cool Plate (SuperTack)", PriceDkk = 260, LifeHours = 500 },
            new Plate { Key = "Cool Plate", Label = "Cool Plate", PriceDkk = 150, LifeHours = 500 },
            new Plate { Key = "Engineering Plate", Label = "Engineering Plate", PriceDkk = 180, LifeHours = 800 },
        };

        public static Nozzle FindNozzle(string key)
        {
            foreach (var n in Nozzles) if (n.Key == key) return n;
            return Nozzles[0];
        }

        public static Plate FindPlate(string key)
        {
            foreach (var p in Plates) if (p.Key == key) return p;
            return Plates[0];
        }

        /// Abrasive filaments (CF/GF, glow…) eat nozzles; stainless steel ones very quickly.
        public static double HotendLifeHours(bool hardened, bool abrasive)
        {
            // real use: clogs, heat creep, crashes, heater/thermistor faults
            return hardened ? (abrasive ? 300 : 750) : (abrasive ? 100 : 500);
        }

        /// PETG and TPU bond so hard to smooth PEI that they can tear the surface.
        public static bool HarshOnPlate(Plate p, string filamentType)
        {
            string t = (filamentType ?? "").ToUpperInvariant();
            return p.Key == "High Temp Plate" && (t.StartsWith("PETG") || t.StartsWith("TPU"));
        }

        public class Breakdown
        {
            public double Printer, Parts, Hotend, Plate, HotendLife, PlateLife;
            public bool Abrasive, AbrasiveOnStainless, PlateHalved;
            public double Total { get { return Math.Round(Printer + Parts + Hotend + Plate, 2); } }
        }

        /// Bambu Lab A1 (unchanged formula).
        public static Breakdown Calculate(Nozzle n, bool abrasive, string filamentType, Plate p)
        {
            return Calculate(Models[0], n, abrasive, filamentType, p);
        }

        /// Wear of this model (null = A1): printer + parts + hotend x HotendFactor + plate x PlateFactor, per hour.
        public static Breakdown Calculate(Model m, Nozzle n, bool abrasive, string filamentType, Plate p)
        {
            if (m == null) m = Models[0];
            var b = new Breakdown();
            b.Printer = m.PrinterPriceDkk / m.PrinterLifeHours;
            b.Parts = m.PartsPriceDkk / m.PartsLifeHours;
            b.HotendLife = HotendLifeHours(n.Hardened, abrasive);
            b.Hotend = n.PriceDkk * m.HotendFactor / b.HotendLife;
            b.PlateHalved = HarshOnPlate(p, filamentType);
            b.PlateLife = b.PlateHalved ? p.LifeHours / 2 : p.LifeHours;
            b.Plate = p.PriceDkk * m.PlateFactor / b.PlateLife;
            b.Abrasive = abrasive;
            b.AbrasiveOnStainless = abrasive && !n.Hardened;
            return b;
        }

        /// Stock setup: 0.4 stainless nozzle, PLA, smooth PEI plate (A1).
        public static double DefaultRate
        {
            get { return Calculate(Nozzles[0], false, "PLA", Plates[0]).Total; }
        }

        /// Stock setup (0.4 stainless, PLA, smooth PEI) for this model key (unknown = A1).
        public static double DefaultRateFor(string modelKey)
        {
            return Calculate(FindModel(modelKey), Nozzles[0], false, "PLA", Plates[0]).Total;
        }
    }

    public static class Pricing
    {
        public static QuoteResult Calculate(QuoteInput input, AppSettings s)
        {
            if (input.Hours < 0 || input.Minutes < 0 || input.Minutes > 59)
                throw new ArgumentException("Print time must be >= 0 and minutes 0-59.");
            if (input.FilamentGrams < 0 || input.FilamentDkkPerKg < 0 || input.ElectricityDkkPerKwh < 0)
                throw new ArgumentException("Values cannot be negative.");
            bool multi = input.Filaments != null && input.Filaments.Count >= 2;
            if (multi)
                foreach (var f in input.Filaments)
                    if (f == null || f.Grams < 0 || f.PricePerKg < 0)
                        throw new ArgumentException("Values cannot be negative.");

            var r = new QuoteResult();
            r.PrintHours = input.Hours + input.Minutes / 60.0;
            if (multi)
            {
                r.FilamentCosts = new double[input.Filaments.Count];
                for (int i = 0; i < r.FilamentCosts.Length; i++)
                {
                    var f = input.Filaments[i];
                    r.FilamentCosts[i] = (f.Grams / 1000.0) * f.PricePerKg;
                    r.MaterialCost += r.FilamentCosts[i];
                }
            }
            else
                r.MaterialCost = (input.FilamentGrams / 1000.0) * input.FilamentDkkPerKg;
            r.PowerKwh = r.PrintHours * (s.PrinterWatts / 1000.0);
            r.PowerCost = r.PowerKwh * input.ElectricityDkkPerKwh;
            // Failed prints / reprints: a share of material + power on top (not of the wear).
            r.FailedPrintCost = (r.MaterialCost + r.PowerCost) * (Math.Max(0, s.FailedPrintPct) / 100.0);
            r.BaseCost = r.MaterialCost + r.PowerCost + r.FailedPrintCost;

            // Both prices: markup + machine-time rate (wear, not marked up). Minimum price is customers only.
            r.FriendsPrice = r.BaseCost * (1 + s.FriendsMarkupPct / 100.0)
                             + r.PrintHours * s.MachineRatePerHour;

            // Customer price: markup + machine-time rate + optional minimum price.
            double others = r.BaseCost * (1 + s.OthersMarkupPct / 100.0)
                            + r.PrintHours * s.MachineRatePerHour;
            if (r.BaseCost > 0 && others < s.MinimumPrice) others = s.MinimumPrice;
            r.OthersPrice = others;

            // Failed prints payback: after markup, wear and minimum price, before rounding (rounding extra not counted).
            if (s.FailedPayback != 0 && input.PaybackDebt > 0)
            {
                double share = (r.MaterialCost + r.PowerCost) * (Math.Max(0, s.FailedPaybackPct) / 100.0);
                r.PaybackCost = Math.Min(input.PaybackDebt, share);
                r.PaybackLeft = input.PaybackDebt - r.PaybackCost;
                r.PaybackBothPrices = s.FailedPaybackWho == 1;
                r.OthersPrice += r.PaybackCost;
                if (r.PaybackBothPrices) r.FriendsPrice += r.PaybackCost;
            }

            r.FriendsPrice = RoundUp(r.FriendsPrice, s.RoundTo);
            r.OthersPrice = RoundUp(r.OthersPrice, s.RoundTo);
            return r;
        }

        public static double RoundUp(double value, double step)
        {
            if (step <= 0) return value;
            return Math.Ceiling(value / step - 1e-9) * step;
        }

        // ---------- quote breakdown (quote export) ----------

        /// The price shown in the app in øre: the same 2-decimal rounding as Num.Kr ("N2").
        public static long ShownOre(double price)
        {
            return (long)decimal.Round(decimal.Parse(price.ToString("F2", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture) * 100m);
        }

        /// Line-by-line breakdown of one price (friends = friends & family price, else the customer price). The lines'
        /// whole-øre amounts add up exactly to the shown price (largest remainder split). wear = the auto rate's split
        /// (Wear.Calculate for the chosen nozzle/filament/plate); null or AutoWearRate off = one wear line without Parts.
        /// Lines: material (one per colour), power, wear, failed (FailedPrintPct > 0), payback (when it is in this price),
        /// work (markup), minimum (customer top-up), rounding; lines that are 0 are left out, except material/power/work.
        public static QuoteBreakdown Breakdown(QuoteInput input, AppSettings s, bool friends, Wear.Breakdown wear)
        {
            var r = Calculate(input, s);
            var b = new QuoteBreakdown { Friends = friends, Result = r, Price = friends ? r.FriendsPrice : r.OthersPrice };
            var lines = new List<QuoteLine>();

            bool multi = r.FilamentCosts != null;
            if (multi)
                for (int i = 0; i < r.FilamentCosts.Length; i++)
                    lines.Add(new QuoteLine { Key = QuoteLine.Material, Index = i, Exact = r.FilamentCosts[i],
                        Quantity = input.Filaments[i].Grams, UnitPrice = input.Filaments[i].PricePerKg });
            else
                lines.Add(new QuoteLine { Key = QuoteLine.Material, Index = 0, Exact = r.MaterialCost,
                    Quantity = input.FilamentGrams, UnitPrice = input.FilamentDkkPerKg });
            lines.Add(new QuoteLine { Key = QuoteLine.Power, Exact = r.PowerCost, Quantity = r.PowerKwh,
                UnitPrice = input.ElectricityDkkPerKwh });

            double wearExact = r.PrintHours * s.MachineRatePerHour;
            var wearLine = new QuoteLine { Key = QuoteLine.Wear, Exact = wearExact, Quantity = r.PrintHours,
                UnitPrice = s.MachineRatePerHour };
            if (wearExact > 0) lines.Add(wearLine);
            if (s.FailedPrintPct > 0)
                lines.Add(new QuoteLine { Key = QuoteLine.Failed, Exact = r.FailedPrintCost, Quantity = s.FailedPrintPct });

            double pct = friends ? s.FriendsMarkupPct : s.OthersMarkupPct;
            double work = r.BaseCost * (pct / 100.0);
            double before = r.BaseCost + work + wearExact;   // the price before minimum, payback and rounding
            bool paid = r.PaybackCost > 0 && (!friends || r.PaybackBothPrices);
            if (paid)
                lines.Add(new QuoteLine { Key = QuoteLine.Payback, Exact = r.PaybackCost, Quantity = s.FailedPaybackPct });
            lines.Add(new QuoteLine { Key = QuoteLine.Work, Exact = work, Quantity = pct });
            if (!friends && r.BaseCost > 0 && before < s.MinimumPrice)
                lines.Add(new QuoteLine { Key = QuoteLine.Minimum, Exact = s.MinimumPrice - before, Quantity = s.MinimumPrice });
            if (s.RoundTo > 0)
            {
                double sum = 0;
                foreach (var l in lines) sum += l.Exact;
                lines.Add(new QuoteLine { Key = QuoteLine.Rounding, Exact = Math.Max(0, b.Price - sum), Quantity = s.RoundTo });
            }

            b.TotalOre = ShownOre(b.Price);
            SplitOre(lines, b.TotalOre);

            // optional lines that came out as 0,00 are left out (they add nothing)
            lines.RemoveAll(l => l.Ore == 0 && l.Key != QuoteLine.Material && l.Key != QuoteLine.Power && l.Key != QuoteLine.Work);

            if (s.AutoWearRate != 0 && wear != null && lines.Contains(wearLine))
            {
                double[] w = { wear.Printer, wear.Parts, wear.Hotend, wear.Plate };
                string[] k = { QuoteLine.WearPrinter, QuoteLine.WearParts, QuoteLine.WearHotend, QuoteLine.WearPlate };
                double wsum = w[0] + w[1] + w[2] + w[3];
                if (wsum > 0)
                {
                    wearLine.Parts = new List<QuoteLine>();
                    for (int i = 0; i < 4; i++)
                    {
                        double rate = s.MachineRatePerHour * w[i] / wsum;   // scaled to the rate actually used
                        wearLine.Parts.Add(new QuoteLine { Key = k[i], Exact = r.PrintHours * rate, Quantity = r.PrintHours, UnitPrice = rate });
                    }
                    SplitOre(wearLine.Parts, wearLine.Ore);
                }
            }
            b.Lines = lines;
            return b;
        }

        /// Gives each line whole øre so they sum to total: floor of each exact amount, then the øre left over go one by
        /// one to the lines with the largest remainders (ties: the earlier line); too many (only from float noise) are
        /// taken back from the smallest remainders.
        public static void SplitOre(List<QuoteLine> lines, long total)
        {
            int n = lines.Count;
            if (n == 0) return;
            var frac = new double[n];
            long sum = 0;
            for (int i = 0; i < n; i++)
            {
                double c = Math.Max(0, lines[i].Exact) * 100.0;
                double f = Math.Floor(c + 1e-7);
                lines[i].Ore = (long)f;
                frac[i] = Math.Max(0, c - f);
                sum += lines[i].Ore;
            }
            long left = total - sum;
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, c) => frac[a] != frac[c] ? frac[c].CompareTo(frac[a]) : a.CompareTo(c));   // largest first
            for (int j = 0; left > 0; j = (j + 1) % n) { lines[order[j]].Ore++; left--; }
            for (int guard = 0; left < 0 && guard < 100000; guard++)
            {
                bool took = false;
                for (int j = n - 1; j >= 0 && left < 0; j--)
                    if (lines[order[j]].Ore > 0) { lines[order[j]].Ore--; left++; took = true; }
                if (!took) break;
            }
        }

        /// Scales a recomputed breakdown to a record's saved price (when they differ: hand-edited file, older formula), so
        /// the page's lines and total agree with the saved price. Nothing changes when they already match (or b is null,
        /// has no lines, saved <= 0, or the lines sum to 0). Used by the desktop quote page and the website.
        public static void MatchSavedTotal(QuoteBreakdown b, double saved)
        {
            if (b == null || b.Lines == null || saved <= 0) return;
            long want = ShownOre(saved);
            if (want == b.TotalOre) return;
            double sum = 0;
            foreach (var l in b.Lines) sum += Math.Max(0, l.Exact);
            if (sum <= 0) return;
            double k = want / 100.0 / sum;
            foreach (var l in b.Lines) l.Exact = Math.Max(0, l.Exact) * k;
            b.Price = saved;
            b.TotalOre = want;
            SplitOre(b.Lines, want);
            foreach (var l in b.Lines)
                if (l.Parts != null)
                {
                    foreach (var part in l.Parts) part.Exact *= k;
                    SplitOre(l.Parts, l.Ore);
                }
        }
    }
}
