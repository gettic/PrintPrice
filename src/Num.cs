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

        public static string Kr(double v) { return v.ToString("N2", Dk) + " kr."; }
        public static string Fmt(double v) { return v.ToString("0.##", Dk); }
    }
}
