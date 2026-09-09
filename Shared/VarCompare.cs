using System;
using System.Globalization;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Comparing what a game variable actually holds against what a pack asked
    /// for.
    /// <para/>
    /// Reported: gifts keyed to the game's own booleans never appeared, even
    /// with those booleans true. The check was
    /// <c>g.ToString() == "true"</c> - and a boxed <see cref="bool"/> prints
    /// <c>"True"</c>, capital T, so an ordinal comparison against the authored
    /// <c>"true"</c> was false whatever the variable held. Every vanilla boolean
    /// condition in every pack failed the same way, silently.
    /// <para/>
    /// Numbers had the same shape of bug waiting: <c>ToString()</c> follows the
    /// machine's culture, so a variable holding 1.5 prints "1,5" wherever the
    /// decimal separator is a comma and never matches an authored "1.5".
    /// <para/>
    /// So nothing is compared as text that is not text. The pack's own store has
    /// always compared by declared type; this is the same idea for variables
    /// whose type has to be read off the value.
    /// <para/>
    /// Compiled into both the editor and the runtime from one file - see
    /// <see cref="VarText"/> for why.
    /// </summary>
    public static class VarCompare
    {
        /// <summary>
        /// Whether <paramref name="actual"/>, straight out of the game, is what
        /// <paramref name="expected"/> says it should be.
        /// <para/>
        /// A variable that does not exist matches nothing, including the empty
        /// string: "there is no such variable" is a different answer from "it is
        /// empty", and only the second should match "".
        /// </summary>
        public static bool Matches(object actual, string expected)
        {
            if (actual == null) return false;
            string wanted = expected == null ? "" : expected.Trim();

            if (actual is bool flag)
            {
                bool asked;
                // "True", "true" and "TRUE" all read the same, which is what an
                // author means every time.
                return bool.TryParse(wanted, out asked) && flag == asked;
            }

            if (IsNumber(actual))
            {
                double asked;
                if (!double.TryParse(wanted, NumberStyles.Float, CultureInfo.InvariantCulture, out asked))
                    return false;
                return Convert.ToDouble(actual, CultureInfo.InvariantCulture) == asked;
            }

            return string.Equals(Text(actual), expected ?? "", StringComparison.Ordinal);
        }

        /// <summary>
        /// What a value looks like written down - for logs, and for the places
        /// that genuinely do want text. Lower-case for booleans and
        /// culture-independent for numbers, so it reads the way a pack is
        /// authored rather than the way the machine happens to be set.
        /// </summary>
        public static string Text(object value)
        {
            if (value == null) return "";
            if (value is bool flag) return flag ? "true" : "false";
            if (IsNumber(value))
                return Convert.ToDouble(value, CultureInfo.InvariantCulture)
                              .ToString("R", CultureInfo.InvariantCulture);

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        private static bool IsNumber(object value)
            => value is double || value is float || value is int || value is long
            || value is short || value is byte || value is decimal
            || value is uint || value is ulong || value is ushort || value is sbyte;
    }
}
