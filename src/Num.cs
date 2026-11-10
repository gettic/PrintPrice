using System.Globalization;
#if !WEB
using System.Windows.Forms;
using System.Drawing;
#endif

namespace PrintPrice
{
    public enum FieldState { Ok, Empty, Invalid }

    public static class Num
    {
        public static readonly CultureInfo Dk = new CultureInfo("da-DK");

        /// Accepts "12,5", "12.5", "1.234,5", "1,234.5". Rejects negatives.
        public static bool TryParse(string text, out double value)
        {
            value = 0;
            string s = (text ?? "").Trim().Replace(" ", "");
            if (s.Length == 0) return false;
            int lastComma = s.LastIndexOf(','), lastDot = s.LastIndexOf('.');
            if (lastComma >= 0 && lastDot >= 0)
            {
                // whichever separator comes last is the decimal one
                if (lastComma > lastDot) s = s.Replace(".", "").Replace(',', '.');
                else s = s.Replace(",", "");
            }
            else s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

#if !WEB
        // desktop only (WinForms + Theme); the website version builds with WEB defined
        public static FieldState Read(TextBox box, out double value)
        {
            value = 0;
            FieldState st;
            if (box.Text.Trim().Length == 0) st = FieldState.Empty;
            else st = TryParse(box.Text, out value) ? FieldState.Ok : FieldState.Invalid;
            box.BackColor = st == FieldState.Invalid ? Theme.InvalidBack : Theme.Input;
            return st;
        }
#endif

        /// Money with 2 decimals ("1.234,57 kr."), rounded like .NET Framework's "N2" on every runtime (see Round2).
        public static string Kr(double v) { return N2(v) + " kr."; }

        /// "N2" in da-DK, the same on .NET Framework (desktop) and .NET Core/10 (website). See Round2.
        public static string N2(double v)
        {
            decimal d;
            if (!TryRound2(v, out d)) return v.ToString("N2", Dk);   // NaN, infinity, beyond decimal: as before
            return d.ToString("N2", Dk);
        }

        /// v rounded to 2 decimals the way .NET Framework's "N2"/"F2" does: first to 15 significant digits, then half
        /// away from zero (25.005 -> 25.01, though the double is 25.00499...). .NET Core 3.0+ rounds the exact double
        /// instead (25.00), so we do it by hand: "E14" gives the same 15 digits on both runtimes, the rest is decimal.
        public static decimal Round2(double v)
        {
            decimal d;
            if (!TryRound2(v, out d)) throw new System.OverflowException("Value can't be rounded to øre: " + v.ToString(CultureInfo.InvariantCulture));
            return d;
        }

        static bool TryRound2(double v, out decimal d)
        {
            d = 0m;
            if (double.IsNaN(v) || double.IsInfinity(v) || System.Math.Abs(v) >= 1e27) return false;
            if (System.Math.Abs(v) < 1e-6) return true;   // 0,00 (and no tiny exponents for decimal.Parse)
            decimal e = decimal.Parse(v.ToString("E14", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            d = decimal.Round(e, 2, System.MidpointRounding.AwayFromZero);
            if (d == 0m) d = 0m;   // no "-0,00" (Framework drops the sign when it rounds to zero)
            return true;
        }
        public static string Fmt(double v) { return v.ToString("0.##", Dk); }
    }
}
