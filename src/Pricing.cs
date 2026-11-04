using System;
using System.Collections.Generic;

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

    /// Estimated wear & tear for a Bambu Lab A1 per print hour, depending on nozzle, filament and build plate.
    public static class Wear
    {
        public const double PrinterPriceDkk = 2099, PrinterLifeHours = 3000;   // printer paid off before it is worn out or outdated
        // parts and upkeep: PTFE tube, wiper, cutter, belts, extruder gears, rods/bearings, grease, IPA, glue stick, shipping
        public const double PartsPriceDkk = 600, PartsLifeHours = 1500;

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

        public static Breakdown Calculate(Nozzle n, bool abrasive, string filamentType, Plate p)
        {
            var b = new Breakdown();
            b.Printer = PrinterPriceDkk / PrinterLifeHours;
            b.Parts = PartsPriceDkk / PartsLifeHours;
            b.HotendLife = HotendLifeHours(n.Hardened, abrasive);
            b.Hotend = n.PriceDkk / b.HotendLife;
            b.PlateHalved = HarshOnPlate(p, filamentType);
            b.PlateLife = b.PlateHalved ? p.LifeHours / 2 : p.LifeHours;
            b.Plate = p.PriceDkk / b.PlateLife;
            b.Abrasive = abrasive;
            b.AbrasiveOnStainless = abrasive && !n.Hardened;
            return b;
        }

        /// Stock setup: 0.4 stainless nozzle, PLA, smooth PEI plate.
        public static double DefaultRate
        {
            get { return Calculate(Nozzles[0], false, "PLA", Plates[0]).Total; }
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
    }
}
