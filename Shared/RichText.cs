using System;
using System.Collections.Generic;

namespace SMSModForge.Shared
{
    /// <summary>
    /// What the game's text engine (Text Mesh Pro) makes of a text with markup
    /// in it - <c>&lt;b&gt;</c>, <c>&lt;color=#FF0000&gt;</c> and the rest.
    /// </summary>
    public static class RichText
    {
        /// <summary>The tags the text engine reads as markup. Anything else
        /// between angle brackets is shown as it is, and so is counted.</summary>
        private static readonly HashSet<string> Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "action", "align", "allcaps", "alpha", "b", "br", "color", "cspace", "font", "font-weight",
            "gradient", "i", "indent", "line-height", "line-indent", "link", "lowercase", "margin", "margin-left",
            "margin-right", "mark", "material", "mspace", "nobr", "noparse", "page", "pos", "rotate", "s", "size",
            "smallcaps", "space", "sprite", "strikethrough", "style", "sub", "sup", "u", "uppercase", "voffset",
            "width",
        };

        /// <summary>The longest tag the text engine reads; a longer one is text.</summary>
        private const int LongestTag = 128;

        /// <summary>
        /// How many characters the text engine lays out for
        /// <paramref name="text"/> - the count a line is revealed by, one
        /// character at a time. Markup takes none, except a line break
        /// (<c>&lt;br&gt;</c>) and a picture (<c>&lt;sprite&gt;</c>), which take
        /// one each; so do the escapes <c>\n</c>, <c>\r</c> and <c>\t</c>, and a
        /// character outside the basic plane (two UTF-16 units, one letter).
        /// Inside <c>&lt;noparse&gt;</c> everything is text.
        /// </summary>
        public static int VisibleLength(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            bool noparse = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<')
                {
                    int end = text.IndexOf('>', i + 1);
                    if (end > i && end - i - 1 <= LongestTag)
                    {
                        string inside = text.Substring(i + 1, end - i - 1);
                        bool closing;
                        string name = NameOf(inside, out closing);
                        if (noparse)
                        {
                            if (closing && name == "noparse")
                            {
                                noparse = false;
                                i = end;
                                continue;
                            }
                        }
                        else if (name != null && inside.IndexOf('<') < 0 && IsTag(name, closing))
                        {
                            if (!closing && name == "noparse") noparse = true;
                            else if (!closing && (name == "br" || name == "sprite")) count++;
                            i = end;
                            continue;
                        }
                    }
                }
                else if (c == '\\' && !noparse && i + 1 < text.Length
                         && (text[i + 1] == 'n' || text[i + 1] == 'r' || text[i + 1] == 't'))
                {
                    count++;
                    i++;
                    continue;
                }

                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                count++;
            }
            return count;
        }

        /// <summary>Whether <paramref name="text"/> has anything that could be
        /// markup - the quick test before counting.</summary>
        public static bool MayHaveMarkup(string text)
        {
            return !string.IsNullOrEmpty(text) && (text.IndexOf('<') >= 0 || text.IndexOf('\\') >= 0);
        }

        /// <summary>The tag's name, lower case: <c>color</c> for
        /// <c>color=#FFF</c>, <c>#ff0000</c> for the short colour form.</summary>
        private static string NameOf(string inside, out bool closing)
        {
            closing = inside.StartsWith("/", StringComparison.Ordinal);
            string rest = closing ? inside.Substring(1) : inside;
            if (rest.Length == 0) return null;
            int stop = rest.IndexOfAny(new[] { '=', ' ' });
            return (stop < 0 ? rest : rest.Substring(0, stop)).ToLowerInvariant();
        }

        private static bool IsTag(string name, bool closing)
        {
            if (Tags.Contains(name)) return true;
            // <#FF0000>: a colour on its own, three, four, six or eight digits.
            if (closing || name.Length < 2 || name[0] != '#') return false;
            int digits = name.Length - 1;
            if (digits != 3 && digits != 4 && digits != 6 && digits != 8) return false;
            for (int i = 1; i < name.Length; i++)
                if (!Uri.IsHexDigit(name[i])) return false;
            return true;
        }
    }
}
