using System.Collections.Generic;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Which writing systems a piece of text is written in.
    /// <para/>
    /// The game's fonts draw Latin, Cyrillic and Greek — the last two through
    /// Windows' own Segoe UI, put behind them by the plugin. What they cannot
    /// draw at all is Chinese, Japanese, Korean and Thai, and those need a
    /// Windows font of their own loaded before a line using them is shown.
    /// <para/>
    /// Asked of the TEXT rather than of the player's language setting, because
    /// the two have nothing to do with each other: a Portuguese player can
    /// install a pack written in Chinese, or an English pack with one Japanese
    /// name in it, and the setting says Portuguese in both cases. Guessing from
    /// the setting is exactly the bug this replaced — Cyrillic and Greek drew
    /// fine while every CJK line showed as boxes, because no CJK font had been
    /// asked for.
    /// <para/>
    /// Every script found is asked for, never the most likely one. A manifest
    /// is a whole pack at once and can be written in more than one; picking a
    /// single answer for all of it is the second bug this carries a test for.
    /// <para/>
    /// Here rather than in the plugin so it can be checked: the plugin loads
    /// into a game this repository cannot start, and a script test that never
    /// runs is a guess. Both bugs above were found in a running game, by
    /// reading which fonts the plugin said it had loaded - and both were
    /// invisible in a test until the rule was written down here.
    /// </summary>
    public static class TextScripts
    {
        /// <summary>A writing system the game's own fonts cannot draw.</summary>
        public enum Script
        {
            /// <summary>Hiragana or katakana. Listed before Chinese so a
            /// Japanese font is searched first and Japanese text is shaped the
            /// way a Japanese reader expects.</summary>
            Japanese,
            /// <summary>Han characters, of which the simplified Chinese forms
            /// are the ones a Japanese font does not have.</summary>
            Chinese,
            Korean,
            Thai,
        }

        /// <summary>
        /// The scripts in <paramref name="text"/> that need a font of their own.
        /// Empty for anything the game can already draw, which is every pack
        /// written in a European language.
        /// </summary>
        public static IList<Script> Of(string text)
        {
            var found = new List<Script>();
            if (string.IsNullOrEmpty(text)) return found;

            bool han = false, kana = false, hangul = false, thai = false;

            foreach (char c in text)
            {
                // Everything below this is Latin, Cyrillic or Greek, which the
                // game draws already. Worth a fast path: this runs over a whole
                // manifest, which is megabytes on a large pack.
                if (c < 0x0E00) continue;

                if (c >= 0x3040 && c <= 0x30FF) kana = true;            // hiragana, katakana
                else if (c >= 0xAC00 && c <= 0xD7A3) hangul = true;     // hangul syllables
                else if (c >= 0x1100 && c <= 0x11FF) hangul = true;     // hangul jamo
                else if (c >= 0x4E00 && c <= 0x9FFF) han = true;        // CJK unified
                else if (c >= 0x3400 && c <= 0x4DBF) han = true;        // CJK extension A
                else if (c >= 0xF900 && c <= 0xFAFF) han = true;        // compatibility ideographs
                else if (c >= 0x0E00 && c <= 0x0E7F) thai = true;

                if (kana && hangul && han && thai) break;
            }

            // Both, when both are there, and that is not belt and braces: it
            // is the bug this replaced.
            //
            // Kana used to settle it - kana meant Japanese, Han on its own
            // meant Chinese - which is right for ONE LINE and wrong for a
            // manifest. A manifest is the whole pack at once, so a pack with a
            // Japanese line anywhere in it read as Japanese entirely, and no
            // Chinese font was ever asked for. What made that hard to see is
            // that it half worked: a Japanese font has Han in it, so most of
            // the Chinese drew, and only the simplified-only forms - 这, 测,
            // 试, 组, 应, 该, 显, 标 - came out as empty boxes, scattered
            // through lines that were otherwise fine.
            //
            // So: any Han at all asks for a Chinese font. Kana asks for a
            // Japanese one as well, and Japanese is first in the enum, so a
            // Japanese font is searched first and shapes the kanji its way
            // while the Chinese font behind it catches what it lacks. A
            // Japanese pack loading a Chinese font it barely touches costs one
            // font asset whose letters are only made when something asks.
            if (kana) found.Add(Script.Japanese);
            if (han) found.Add(Script.Chinese);
            if (hangul) found.Add(Script.Korean);
            if (thai) found.Add(Script.Thai);
            return found;
        }

        /// <summary>
        /// Windows' fonts for a script, best first. The first one installed is
        /// the one used; a machine with none of them shows boxes, and says so.
        /// </summary>
        public static string[] Fonts(Script script)
        {
            switch (script)
            {
                case Script.Chinese: return new[] { "Microsoft YaHei", "SimHei", "SimSun" };
                case Script.Japanese: return new[] { "Yu Gothic", "Meiryo", "MS Gothic" };
                case Script.Korean: return new[] { "Malgun Gothic", "Gulim" };
                case Script.Thai: return new[] { "Leelawadee UI", "Tahoma" };
                default: return new string[0];
            }
        }
    }
}
