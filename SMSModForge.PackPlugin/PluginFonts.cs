using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using SMSModForge.Shared;
using TMPro;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Letters the game's own fonts do not have.
    /// <para/>
    /// The game is English and its fonts only draw Latin letters, so a line
    /// in Chinese, Japanese, Korean or Russian would show as empty boxes. Text
    /// Mesh Pro looks for a letter a font lacks in a list of fallback fonts,
    /// so Windows' own fonts for the player's language go on that list: the
    /// game's fonts still draw everything they can, and only the letters they
    /// cannot are drawn from Windows'.
    /// </summary>
    internal static class PluginFonts
    {
        private const string Tag = "[SMSModForge.PackPlugin] Fonts: ";

        /// <summary>Windows' fonts for a language's script, best first; the
        /// first one installed is used.</summary>
        private static string[] ForScript(string code)
        {
            string c = (code ?? "").ToLowerInvariant();
            switch (PluralRules.LanguageOf(code))
            {
                case "zh":
                    return c.Contains("hant") || c.EndsWith("-tw") || c.EndsWith("-hk") || c.EndsWith("-mo")
                        ? new[] { "Microsoft JhengHei", "MingLiU", "PMingLiU" }
                        : new[] { "Microsoft YaHei", "SimHei", "SimSun" };
                case "ja": return new[] { "Yu Gothic", "Meiryo", "MS Gothic" };
                case "ko": return new[] { "Malgun Gothic", "Gulim" };
                case "th": return new[] { "Leelawadee UI", "Tahoma" };
                default: return new string[0];
            }
        }

        /// <summary>For everything else: accented Latin, Cyrillic, Greek.</summary>
        private static readonly string[] General = { "Segoe UI", "Arial" };

        /// <summary>
        /// Put Windows' fonts for <paramref name="code"/> behind the game's.
        /// Nothing for English, which the game's fonts already draw. Every
        /// step is logged, because whether it worked is only visible in the
        /// game, and a font that fails leaves the game's fonts as they were.
        /// </summary>
        public static void AddFallbacks(string code, ManualLogSource log)
        {
            if (PluralRules.LanguageOf(code) == "en") return;

            var wanted = new List<string>();
            Want(wanted, ForScript(code));
            Want(wanted, General);
            if (wanted.Count == 0) return;

            Install(wanted, code, log);
        }

        /// <summary>
        /// Put Windows' fonts behind the game's for the scripts that actually
        /// appear in <paramref name="text"/>.
        /// <para/>
        /// The player's language says what ModForge's own words need. It says
        /// nothing about what a PACK needs: a Portuguese player can install a
        /// pack written in Chinese, or one with a Japanese name in an English
        /// line, and the language setting has no idea. Asked of the text, this
        /// does — and a pack with nothing but Latin letters adds nothing, which
        /// is every pack today.
        /// <para/>
        /// Called with a manifest once it has been read and translated, so what
        /// is scanned is the words the player will actually see.
        /// </summary>
        public static void AddForText(string text, string what, ManualLogSource log)
        {
            var scripts = TextScripts.Of(text);
            if (scripts.Count == 0) return;

            var wanted = new List<string>();
            foreach (var script in scripts) Want(wanted, TextScripts.Fonts(script));
            if (wanted.Count == 0) return;

            log?.LogInfo(Tag + what + " is written in " + string.Join(", ", Names(scripts))
                         + ", which the game's fonts cannot draw.");
            Install(wanted, what, log);
            Prepare(text, what, log);
        }

        /// <summary>
        /// Draw every letter <paramref name="text"/> needs from Windows' fonts
        /// now, while the pack is loading, rather than when a line first shows
        /// it.
        /// <para/>
        /// A fallback font makes a letter the first time something asks for it:
        /// it opens the Windows font file - Microsoft YaHei is fifteen
        /// megabytes - and renders the letter into its texture, inside the
        /// frame that asked. A line of Chinese asks for thirty letters at once,
        /// so the game stopped for a moment every time a line with letters it
        /// had not shown yet came up. Here the same work happens behind the
        /// loading screen, once, and a line only reads letters already made.
        /// <para/>
        /// Offered to the fonts in the order Text Mesh Pro searches them, each
        /// taking what it has and passing on what it lacks - so each letter is
        /// made in the font that will actually draw it.
        /// </summary>
        private static void Prepare(string text, string what, ManualLogSource log)
        {
            var letters = new HashSet<char>();
            foreach (char c in text)
                if (c >= 0x0E00 && !char.IsSurrogate(c) && !char.IsWhiteSpace(c) && !char.IsControl(c)) letters.Add(c);
            if (letters.Count == 0) return;

            List<TMP_FontAsset> fallbacks;
            try { fallbacks = TMP_Settings.fallbackFontAssets; }
            catch (Exception) { return; }
            if (fallbacks == null) return;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            string remaining = new string(letters.ToArray());
            int made = 0;
            foreach (var asset in fallbacks)
            {
                if (remaining.Length == 0) break;
                if (asset == null || !asset.name.StartsWith(NamePrefix, StringComparison.Ordinal)) continue;
                try
                {
                    string missing;
                    asset.TryAddCharacters(remaining, out missing);
                    missing = missing ?? "";
                    made += remaining.Length - missing.Length;
                    remaining = missing;
                }
                catch (Exception ex)
                {
                    log?.LogWarning(Tag + asset.name + " could not make letters ahead of time (" + ex.Message
                                    + "); they will be made as lines show them.");
                }
            }
            clock.Stop();
            log?.LogInfo(Tag + made + " letter(s) " + what + " uses made ready in " + clock.ElapsedMilliseconds + " ms"
                         + (remaining.Length > 0
                                ? "; " + remaining.Length + " none of the fonts has, which will show as boxes"
                                : "") + ".");
        }

        private static string[] Names(System.Collections.Generic.IList<TextScripts.Script> scripts)
        {
            var names = new string[scripts.Count];
            for (int i = 0; i < scripts.Count; i++) names[i] = scripts[i].ToString();
            return names;
        }

        /// <summary>The first of <paramref name="families"/> that is installed.</summary>
        private static void Want(List<string> wanted, string[] families)
        {
            HashSet<string> installed = Installed();
            foreach (string family in families)
            {
                if (installed != null && !installed.Contains(family)) continue;
                if (!wanted.Contains(family)) wanted.Add(family);
                return;
            }
            if (families.Length > 0)
                wanted.Add(families[0]);   // not installed by name; Create still tries
        }

        private static HashSet<string> Installed()
        {
            try { return new HashSet<string>(Font.GetOSInstalledFontNames() ?? new string[0], StringComparer.OrdinalIgnoreCase); }
            catch (Exception) { return null; }
        }

        /// <summary>Add each family to Text Mesh Pro's fallback list, once.</summary>
        private static void Install(List<string> families, string why, ManualLogSource log)
        {
            List<TMP_FontAsset> fallbacks;
            try { fallbacks = TMP_Settings.fallbackFontAssets; }
            catch (Exception ex)
            {
                log?.LogWarning(Tag + "Text Mesh Pro's settings could not be read (" + ex.Message
                                + "); letters the game's fonts lack will show as boxes.");
                return;
            }
            if (fallbacks == null)
            {
                log?.LogWarning(Tag + "Text Mesh Pro has no fallback list; letters the game's fonts lack will show as boxes.");
                return;
            }

            foreach (string family in families)
            {
                if (fallbacks.Any(a => a != null && a.name == Name(family))) continue;
                var asset = Create(family, log);
                if (asset == null) continue;
                asset.name = Name(family);
                // One texture holds a limited number of letters at this size,
                // and a Chinese pack uses thousands: without more textures, the
                // letters past what the first one holds would never be made.
                asset.isMultiAtlasTexturesEnabled = true;
                fallbacks.Add(asset);
                log?.LogInfo(Tag + family + " added behind the game's fonts, for " + why + ".");
            }
        }

        private const string NamePrefix = "SMSModForge fallback - ";

        private static string Name(string family) => NamePrefix + family;

        /// <summary>A font asset drawing from a Windows font, its letters made
        /// as they are first needed.</summary>
        private static TMP_FontAsset Create(string family, ManualLogSource log)
        {
            try
            {
                var asset = TMP_FontAsset.CreateFontAsset(family, "Regular", 90);
                if (asset != null) return asset;
            }
            catch (Exception ex)
            {
                log?.LogInfo(Tag + family + " by name: " + ex.Message);
            }

            try
            {
                var font = Font.CreateDynamicFontFromOSFont(family, 90);
                var asset = font == null ? null : TMP_FontAsset.CreateFontAsset(font);
                if (asset != null) return asset;
            }
            catch (Exception ex)
            {
                log?.LogInfo(Tag + family + " from the system font: " + ex.Message);
            }

            log?.LogWarning(Tag + family + " could not be loaded as a fallback font.");
            return null;
        }
    }
}
