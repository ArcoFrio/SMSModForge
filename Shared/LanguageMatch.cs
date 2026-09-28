using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Which of the translations there are suits somebody.
    /// <para/>
    /// Files are named for their language (<c>es</c>, <c>pt-BR</c>,
    /// <c>zh-Hans</c>) and people are described by their system's settings
    /// (<c>es-MX</c>, <c>pt-PT</c>, <c>zh-CN</c>), which rarely say exactly the
    /// same thing. A Mexican reads the Spanish file; a Portuguese reader is
    /// better served by the Brazilian one than by English. Chinese is the one
    /// language where that is not so: the script is what matters, and a reader
    /// of one cannot be handed the other.
    /// </summary>
    public static class LanguageMatch
    {
        /// <summary>
        /// The code in <paramref name="available"/> that suits somebody asking
        /// for <paramref name="wanted"/>, or null when none does.
        /// </summary>
        public static string Best(string wanted, IEnumerable<string> available)
        {
            if (string.IsNullOrEmpty(wanted) || available == null) return null;
            var codes = available.Where(c => !string.IsNullOrEmpty(c)).ToList();

            string exact = codes.FirstOrDefault(c => string.Equals(c, wanted, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            string language = PluralRules.LanguageOf(wanted);
            if (language == "zh")
            {
                string script = ChineseScript(wanted);
                return codes.FirstOrDefault(c => PluralRules.LanguageOf(c) == "zh" && ChineseScript(c) == script);
            }

            // The language on its own first ("es" for "es-MX"), then any
            // regional one, the same one every time.
            var same = codes.Where(c => PluralRules.LanguageOf(c) == language).ToList();
            return same.FirstOrDefault(c => string.Equals(c, language, StringComparison.OrdinalIgnoreCase))
                   ?? same.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        /// <summary>Hans or Hant, from a code that may say either the script or
        /// the region: zh-TW, zh-HK and zh-MO write traditional characters.</summary>
        private static string ChineseScript(string code)
        {
            string c = code.ToLowerInvariant();
            if (c.Contains("hant") || c.EndsWith("-tw") || c.EndsWith("-hk") || c.EndsWith("-mo")
                || c.EndsWith("_tw") || c.EndsWith("_hk") || c.EndsWith("_mo"))
                return "Hant";
            return "Hans";
        }

        /// <summary>
        /// The code for a language as Unity names it
        /// (<c>Application.systemLanguage</c>), or null for one ModForge has no
        /// code for. Unity says only "Portuguese" and "Chinese" where Windows
        /// would say which kind; <see cref="Best"/> picks among the kinds there
        /// are.
        /// </summary>
        public static string CodeOfSystemLanguage(string name)
        {
            switch (name)
            {
                case "Afrikaans": return "af";
                case "Arabic": return "ar";
                case "Basque": return "eu";
                case "Belarusian": return "be";
                case "Bulgarian": return "bg";
                case "Catalan": return "ca";
                case "Chinese": return "zh-Hans";
                case "ChineseSimplified": return "zh-Hans";
                case "ChineseTraditional": return "zh-Hant";
                case "Czech": return "cs";
                case "Danish": return "da";
                case "Dutch": return "nl";
                case "English": return "en";
                case "Estonian": return "et";
                case "Faroese": return "fo";
                case "Finnish": return "fi";
                case "French": return "fr";
                case "German": return "de";
                case "Greek": return "el";
                case "Hebrew": return "he";
                case "Hindi": return "hi";
                case "Hungarian": return "hu";
                case "Hugarian": return "hu";   // Unity's own spelling of it, in older versions
                case "Icelandic": return "is";
                case "Indonesian": return "id";
                case "Italian": return "it";
                case "Japanese": return "ja";
                case "Korean": return "ko";
                case "Latvian": return "lv";
                case "Lithuanian": return "lt";
                case "Norwegian": return "nb";
                case "Polish": return "pl";
                case "Portuguese": return "pt";
                case "Romanian": return "ro";
                case "Russian": return "ru";
                case "SerboCroatian": return "hr";
                case "Slovak": return "sk";
                case "Slovenian": return "sl";
                case "Spanish": return "es";
                case "Swedish": return "sv";
                case "Thai": return "th";
                case "Turkish": return "tr";
                case "Ukrainian": return "uk";
                case "Vietnamese": return "vi";
                default: return null;
            }
        }
    }
}
