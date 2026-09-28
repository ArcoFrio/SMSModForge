using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// The language a player picks on the game's main menu: which languages
    /// are offered, what each is called, and which packs a choice leaves
    /// mostly untranslated.
    /// <para/>
    /// The plugin draws the menu; what it offers and what it warns about are
    /// decided here, where the editor's tests can check them.
    /// </summary>
    public static class LanguageChoice
    {
        /// <summary>
        /// Under this share of a pack translated, choosing the language warns
        /// about the pack: the player would read more of it in the language it
        /// was written in than in the one they picked.
        /// </summary>
        public const double Enough = 0.5;

        /// <summary>
        /// Each language ModForge comes with or is being translated into, as
        /// its own speakers write it - the names Windows gives them, which are
        /// what the editor's Language menu shows. Written out rather than asked
        /// of the game's runtime, whose list of languages is its own and older.
        /// </summary>
        private static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "en", "English" },
                { "es", "Español" },
                { "pt-BR", "Português (Brasil)" },
                { "zh-Hans", "中文（简体）" },
                { "de", "Deutsch" },
                { "fr", "Français" },
                { "ja", "日本語" },
                { "ko", "한국어" },
                { "ru", "Русский" },
            };

        /// <summary>The languages with a flag on the main menu, from the bottom
        /// up. Each has a picture built into the plugin under its code.</summary>
        public static readonly string[] Flagged = { "en", "es", "pt-BR", "zh-Hans", "de", "fr", "ja", "ko", "ru" };

        /// <summary>A language's name in itself, or null when it is not one of
        /// <see cref="Names"/>.</summary>
        public static string NativeName(string code)
        {
            string name;
            return code != null && Names.TryGetValue(code, out name) ? name : null;
        }

        /// <summary>The code as <see cref="Flagged"/> spells it, or null when the
        /// language has no flag.</summary>
        public static string FlagOf(string code)
        {
            foreach (string flagged in Flagged)
                if (string.Equals(flagged, code, StringComparison.OrdinalIgnoreCase)) return flagged;
            return null;
        }

        /// <summary>
        /// Every language worth offering: the ones ModForge has words for, and
        /// every one an installed pack is written in or translated into - a
        /// pack translated into Italian can be played in Italian even though
        /// ModForge's own words would stay in English.
        /// <para/>
        /// English first, the rest by name, each once.
        /// </summary>
        /// <param name="nameOf">The name a language is listed under; codes
        /// without one are listed by code.</param>
        public static List<string> Offered(IEnumerable<string> forge, IEnumerable<string> packs,
                                           Func<string, string> nameOf = null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var codes = new List<string>();
            foreach (string code in (forge ?? Enumerable.Empty<string>()).Concat(packs ?? Enumerable.Empty<string>()))
            {
                string c = (code ?? "").Trim();
                if (c.Length == 0 || !TextFile.IsKey(c) || !seen.Add(c)) continue;
                codes.Add(FlagOf(c) ?? c);
            }
            if (seen.Add(PackTexts.DefaultLanguage)) codes.Add(PackTexts.DefaultLanguage);

            Func<string, string> name = nameOf ?? NativeName;
            return codes
                .OrderBy(c => string.Equals(c, PackTexts.DefaultLanguage, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(c => name(c) ?? c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ── How much of a pack is in the chosen language ────────────────

        /// <summary>How much of one pack a player reads in the language they chose.</summary>
        public sealed class Coverage
        {
            public string Pack = "";

            /// <summary>The pack is written in that language, so all of it is.</summary>
            public bool Own;

            /// <summary>The pack has a translation it will be played in.</summary>
            public bool HasTranslation;

            public int Translated;
            public int Total;

            public double Share
            {
                get { return Own || Total == 0 ? 1.0 : (double)Translated / Total; }
            }

            public bool IsEnough
            {
                get { return Share >= Enough; }
            }

            /// <summary>Rounded down, so a pack short of half never reads as 50%.</summary>
            public int Percent
            {
                get { return (int)Math.Floor(Share * 100); }
            }
        }

        /// <summary>
        /// How much of <paramref name="manifest"/> a player choosing
        /// <paramref name="code"/> reads in it - by the same rule the plugin
        /// plays the pack by (<see cref="PackTexts.Choose"/>), and counted the
        /// way the plugin's log counts it (<see cref="PackTexts.Count"/>).
        /// </summary>
        /// <param name="translations">The pack's translation files by code;
        /// only the one chosen is read.</param>
        public static Coverage Of(string pack, JObject manifest, string code,
                                  IDictionary<string, Func<TextFile>> translations)
        {
            var result = new Coverage { Pack = pack ?? "" };
            string own = PackTexts.LanguageOf(manifest);
            var files = translations ?? new Dictionary<string, Func<TextFile>>();

            string chosen = PackTexts.Choose(code, own, files.Keys);
            if (chosen == null && LanguageMatch.Best(code, new[] { own }) != null)
            {
                result.Own = true;
                return result;
            }

            TextFile file = chosen != null ? files[chosen]() : null;
            result.HasTranslation = file != null;
            var counted = PackTexts.Count(manifest, file);
            result.Translated = counted.Translated;
            result.Total = counted.Total;
            return result;
        }

        /// <summary>
        /// What the player is told after choosing <paramref name="language"/>
        /// (its name), when some packs are less than half in it; null when
        /// none are. Laid out like the warning before a save, which the window
        /// that shows both expects.
        /// </summary>
        public static SaveWarningText Warning(string language, IList<Coverage> packs)
        {
            var short_ = (packs ?? new List<Coverage>()).Where(p => p != null && !p.IsEnough).ToList();
            if (short_.Count == 0) return null;

            var text = new SaveWarningText
            {
                Title = GameTexts.F("game.language.coverage.title", "language", language),
            };
            text.Paragraphs.Add(GameTexts.P("game.language.coverage.body", short_.Count, "language", language));
            text.Details.Add(new SaveWarningSection(
                GameTexts.F("game.language.coverage.heading", "language", language),
                short_.Select(p => p.HasTranslation && p.Translated > 0
                    ? GameTexts.F("game.language.coverage.share", "pack", p.Pack, "percent", p.Percent)
                    : GameTexts.F("game.language.coverage.none", "pack", p.Pack))));
            return text;
        }
    }
}
