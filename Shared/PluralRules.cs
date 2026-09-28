using System;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Which form of a word goes with a count, per language.
    /// <para/>
    /// English has two ("1 task", "2 tasks"), and that is the exception rather
    /// than the rule: Chinese, Japanese and Korean have one, Russian and Polish
    /// three for whole numbers, and French counts 0 with 1. These follow the
    /// Unicode CLDR rules for the counts ModForge shows: whole numbers, well
    /// under a million. A language not listed gets English's, and the file
    /// check says which forms it expects either way.
    /// </summary>
    public static class PluralRules
    {
        private static readonly string[] OneOther = { "one", "other" };
        private static readonly string[] OtherOnly = { "other" };
        private static readonly string[] OneFewMany = { "one", "few", "many" };
        private static readonly string[] OneFewOther = { "one", "few", "other" };

        /// <summary>The language part of a code: <c>pt</c> for <c>pt-BR</c>.</summary>
        public static string LanguageOf(string code)
        {
            if (string.IsNullOrEmpty(code)) return "en";
            int dash = code.IndexOfAny(new[] { '-', '_' });
            return (dash < 0 ? code : code.Substring(0, dash)).ToLowerInvariant();
        }

        /// <summary>The forms a translation into <paramref name="code"/> needs, in order.</summary>
        public static string[] FormsFor(string code)
        {
            switch (LanguageOf(code))
            {
                case "ja": case "zh": case "ko": case "vi": case "th": case "id": case "ms":
                    return OtherOnly;
                case "ru": case "uk": case "be": case "pl":
                    return OneFewMany;
                case "cs": case "sk":
                    return OneFewOther;
                default:
                    return OneOther;
            }
        }

        /// <summary>The form <paramref name="n"/> takes in <paramref name="code"/>.</summary>
        public static string FormOf(string code, long n)
        {
            long a = Math.Abs(n), mod10 = a % 10, mod100 = a % 100;
            switch (LanguageOf(code))
            {
                case "ja": case "zh": case "ko": case "vi": case "th": case "id": case "ms":
                    return "other";

                case "fr":
                    return a <= 1 ? "one" : "other";

                // Brazilian Portuguese counts 0 with 1, as French does; CLDR's
                // pt rule is exactly that for whole numbers.
                case "pt":
                    return a <= 1 ? "one" : "other";

                case "ru": case "uk": case "be":
                    if (mod10 == 1 && mod100 != 11) return "one";
                    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "few";
                    return "many";

                case "pl":
                    if (a == 1) return "one";
                    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "few";
                    return "many";

                case "cs": case "sk":
                    if (a == 1) return "one";
                    if (a >= 2 && a <= 4) return "few";
                    return "other";

                default:
                    return a == 1 ? "one" : "other";
            }
        }
    }
}
