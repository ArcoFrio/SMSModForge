using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SMSModForge.Shared
{
    /// <summary>
    /// The words on screen, in the language somebody picked.
    /// <para/>
    /// English is always underneath: a line the translation lacks, or left
    /// empty, shows in English rather than as a key or a blank, so a half-done
    /// translation is still usable. A key English lacks too is a mistake in
    /// ModForge itself; it shows as the key and <see cref="Missing"/> hears
    /// about it, which is how the tests catch one.
    /// <para/>
    /// Texts fill named gaps: <c>{pack}</c> in "'{pack}' changes..." takes the
    /// value given for <c>pack</c>, so a translation can put it wherever its
    /// own word order wants it. A count picks the form of the sentence its
    /// language uses for that number (<see cref="PluralRules"/>).
    /// </summary>
    public sealed class Texts
    {
        public string Code { get; private set; }
        public TextFile English { get; private set; }

        /// <summary>Null when the language is English itself.</summary>
        public TextFile Translation { get; private set; }

        public bool IsPseudo { get { return Code == PseudoText.Code; } }

        /// <summary>Told about a key that is not in the English file.</summary>
        public Action<string> Missing;

        /// <summary>Told about a gap a text has that nothing was given for:
        /// the key and the gap's name.</summary>
        public Action<string, string> Unfilled;

        private readonly CultureInfo _culture;

        public Texts(TextFile english, TextFile translation, string code)
        {
            English = english ?? new TextFile();
            Translation = translation;
            Code = string.IsNullOrEmpty(code) ? "en" : code;
            _culture = CultureFor(Code);
        }

        private static CultureInfo CultureFor(string code)
        {
            if (code == PseudoText.Code) return CultureInfo.InvariantCulture;
            try { return CultureInfo.GetCultureInfo(code); }
            catch (Exception) { return CultureInfo.InvariantCulture; }
        }

        /// <summary>The text for <paramref name="key"/>.</summary>
        public string T(string key)
        {
            string s = Lookup(key);
            if (s != null) return s;
            if (Missing != null) Missing(key);
            return key;
        }

        /// <summary>The text for <paramref name="key"/> with its gaps filled:
        /// name, value, name, value...</summary>
        public string F(string key, params object[] namesAndValues)
            => Fill(key, T(key), namesAndValues, null);

        /// <summary>
        /// The form of <paramref name="key"/> that goes with <paramref name="count"/>,
        /// with <c>{count}</c> and any other gaps filled. The key is written
        /// without its form: <c>P("quests.changed", n)</c> reads
        /// <c>quests.changed.one</c>, <c>quests.changed.other</c> and so on.
        /// </summary>
        public string P(string key, long count, params object[] namesAndValues)
        {
            string s = LookupPlural(key, count);
            if (s == null)
            {
                if (Missing != null) Missing(key);
                s = key;
            }
            return Fill(key, s, namesAndValues, count);
        }

        public bool Has(string key)
            => English.Has(key) || English.Has(key + ".other");

        /// <summary>"A, B and C", the way the language joins a list.</summary>
        public string JoinAnd(IList<string> items) { return Join(items, "common.listAnd"); }

        /// <summary>"A, B or C", the way the language joins a list.</summary>
        public string JoinOr(IList<string> items) { return Join(items, "common.listOr"); }

        /// <summary>"A, B, C": a list with no "and", such as the counts beside a row.</summary>
        public string JoinList(IList<string> items) { return Join(items, "common.listComma"); }

        /// <summary>
        /// A list read as a sentence. Built from two patterns rather than a
        /// separator, because the words between items are not always words with
        /// spaces round them: Japanese joins with a particle and no space at all,
        /// and a separator in the file would lose its spaces to the trim.
        /// </summary>
        private string Join(IList<string> items, string lastKey)
        {
            if (items == null || items.Count == 0) return "";
            string text = items[items.Count - 1];
            if (items.Count == 1) return text;
            text = F(lastKey, "first", items[items.Count - 2], "second", text);
            for (int i = items.Count - 3; i >= 0; i--)
                text = F("common.listComma", "first", items[i], "second", text);
            return text;
        }

        private string Lookup(string key)
        {
            string english = English.Get(key);
            if (IsPseudo) return english == null ? null : PseudoText.Of(english);
            if (Translation != null)
            {
                string mine = Translation.Translated(key);
                // A key only a translation has would be text nothing asks for;
                // English decides what exists.
                if (mine != null && english != null) return mine;
            }
            return english;
        }

        private string LookupPlural(string key, long count)
        {
            string englishForm = PluralRules.FormOf("en", count);
            string english = English.Get(key + "." + englishForm) ?? English.Get(key + ".other");
            if (english == null) return null;
            if (IsPseudo) return PseudoText.Of(english);
            if (Translation != null)
            {
                string mine = Translation.Translated(key + "." + PluralRules.FormOf(Code, count))
                              ?? Translation.Translated(key + ".other");
                if (mine != null) return mine;
            }
            return english;
        }

        private string Fill(string key, string text, object[] namesAndValues, long? count)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (count.HasValue) values["count"] = count.Value.ToString("N0", _culture);
            if (namesAndValues != null)
            {
                for (int i = 0; i + 1 < namesAndValues.Length; i += 2)
                {
                    string name = Convert.ToString(namesAndValues[i], CultureInfo.InvariantCulture);
                    object v = namesAndValues[i + 1];
                    values[name] = v is IFormattable
                        ? ((IFormattable)v).ToString(null, _culture)
                        : Convert.ToString(v, _culture) ?? "";
                }
            }
            return TextCodes.Fill(text, values, gap =>
            {
                if (Unfilled != null) Unfilled(key, gap);
            });
        }
    }

    /// <summary>
    /// The parts of a text that are not words: gaps like <c>{pack}</c>, tags
    /// like <c>&lt;b&gt;</c>, tokens like <c>[PV:name]</c>. A translation has to
    /// keep every one of them exactly as it is, or what fills it in, or what
    /// reads it, breaks.
    /// </summary>
    public static class TextCodes
    {
        /// <summary>
        /// <paramref name="text"/> with each <c>{name}</c> that has a value
        /// replaced by it. A gap with no value is left as written, so literal
        /// braces in a text about braces survive; <paramref name="unfilled"/>
        /// hears about each one.
        /// </summary>
        public static string Fill(string text, IDictionary<string, string> values, Action<string> unfilled)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? "";
            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) { sb.Append(text, i, text.Length - i); break; }
                int close = text.IndexOf('}', open + 1);
                if (close < 0) { sb.Append(text, i, text.Length - i); break; }
                string name = text.Substring(open + 1, close - open - 1);
                sb.Append(text, i, open - i);
                string value;
                if (IsGapName(name) && values != null && values.TryGetValue(name, out value))
                    sb.Append(value);
                else
                {
                    if (IsGapName(name) && unfilled != null) unfilled(name);
                    sb.Append(text, open, close - open + 1);
                }
                i = close + 1;
            }
            return sb.ToString();
        }

        public static bool IsGapName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }

        /// <summary>Every code in <paramref name="text"/>, in order.</summary>
        public static List<string> Of(string text)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int end = -1;
                if (c == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > 0 && IsGapName(text.Substring(i + 1, close - i - 1))) end = close;
                }
                else if (c == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    // A tag starts with a letter, a slash or a hash - "a < b"
                    // and "<-" are words, not tags.
                    if (close > i + 1 && close - i < 80)
                    {
                        char first = text[i + 1];
                        if ((char.IsLetter(first) || first == '/' || first == '#') && text.IndexOf('<', i + 1, close - i - 1) < 0)
                            end = close;
                    }
                }
                else if (c == '[')
                {
                    // [PV:name] - capitals, a colon, and no space anywhere:
                    // "a [PV: that never closes" is words about a token.
                    int close = text.IndexOf(']', i + 1);
                    int colon = close > 0 ? text.IndexOf(':', i + 1, close - i - 1) : -1;
                    if (colon > i + 1 && IsUpperWord(text.Substring(i + 1, colon - i - 1))
                        && text.IndexOfAny(new[] { ' ', '\t', '\n' }, i, close - i) < 0)
                        end = close;
                }
                if (end > 0)
                {
                    found.Add(text.Substring(i, end - i + 1));
                    i = end;
                }
            }
            return found;
        }

        private static bool IsUpperWord(string s)
        {
            foreach (char c in s) if (!(c >= 'A' && c <= 'Z')) return false;
            return s.Length > 0;
        }

        /// <summary>Whether <paramref name="text"/> has an access key: an
        /// underscore before the letter Alt reaches it by.</summary>
        public static bool HasAccessKey(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i + 1 < text.Length; i++)
            {
                if (text[i] != '_') continue;
                if (text[i + 1] == '_') { i++; continue; }
                if (char.IsLetterOrDigit(text[i + 1])) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// A language made of English, for finding what has not been handed to
    /// the translation yet.
    /// <para/>
    /// Every text comes out accented, a third longer, and between
    /// <see cref="Open"/> and <see cref="Close"/>. Anything on screen without
    /// them did not come through the translation, and anything cut off will
    /// be cut off in German too. Microsoft calls the technique
    /// pseudo-localisation; the code is theirs for it.
    /// </summary>
    public static class PseudoText
    {
        public const string Code = "qps-ploc";
        public const char Open = '⟦';
        public const char Close = '⟧';
        public const char Pad = '·';

        private const string Plain = "aAcCeEiInNoOuUyYsSzZgGdD";
        private const string Fancy = "áÅçÇéÉíÍñÑöÖüÜýÝšŠžŽğĞđĐ";

        public static string Of(string english)
        {
            if (english == null) return null;
            var sb = new StringBuilder(english.Length * 2 + 4);
            sb.Append(Open);
            int letters = 0;
            int depth = 0;
            for (int i = 0; i < english.Length; i++)
            {
                char c = english[i];
                // Gaps and tags keep their names, or they could not be filled.
                // Only a brace or bracket that opens one counts: "a < b" is words.
                char next = i + 1 < english.Length ? english[i + 1] : ' ';
                if ((c == '{' || c == '<') && (char.IsLetterOrDigit(next) || next == '/' || next == '#' || next == '_')) depth++;
                else if ((c == '}' || c == '>') && depth > 0) { depth--; sb.Append(c); continue; }
                if (depth > 0) { sb.Append(c); continue; }

                int at = Plain.IndexOf(c);
                sb.Append(at >= 0 ? Fancy[at] : c);
                if (char.IsLetter(c)) letters++;
            }
            int pad = Math.Max(2, (letters + 2) / 3);
            sb.Append(Pad, pad);
            sb.Append(Close);
            return sb.ToString();
        }

        /// <summary>Whether <paramref name="shown"/> came through the translation.</summary>
        public static bool Marks(string shown)
            => shown != null && shown.IndexOf(Open) >= 0;
    }
}
