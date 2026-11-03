namespace PrintPrice
{
    /// Two languages (English / Danish). Each text is written as an inline pair at the place it is used:
    /// L.S("Clear", "Ryd"). L.Danish is set once at startup (Program.Main) and never changes while running.
    internal static class L
    {
        public static bool Danish = false;

        /// The text in the current language.
        public static string S(string en, string da) { return Danish && da != null ? da : en; }

        /// string.Format with the text in the current language; numbers are formatted the Danish way.
        public static string F(string en, string da, params object[] args)
        {
            return string.Format(Num.Dk, S(en, da), args);
        }
    }
}
