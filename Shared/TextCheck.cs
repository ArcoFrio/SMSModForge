using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// What is wrong with a translation, measured against the English it
    /// translates.
    /// <para/>
    /// Nothing here stops a translation from being used: a line it lacks shows
    /// in English, and a line nothing asks for is never shown. What the check is
    /// for is the translator, who otherwise finds out by reading every screen.
    /// The findings carry what they found rather than a sentence, so whoever
    /// shows them says it in the reader's own language.
    /// </summary>
    public static class TextCheck
    {
        /// <summary>Keys that describe the file rather than hold a text.</summary>
        public const string LanguageKeys = "language.";

        public enum Kind
        {
            /// <summary>A line the reader could not read as a note, a heading or a text.</summary>
            BadLine,

            /// <summary>The same key twice. The first one is used.</summary>
            Duplicate,

            /// <summary>A key English does not have - often one a Replace All
            /// changed. <see cref="Finding.Suggestion"/> is the key it most likely was.</summary>
            Unknown,

            /// <summary>English has it and the translation does not.</summary>
            Missing,

            /// <summary>The translation has the key, with nothing after the =.</summary>
            Empty,

            /// <summary>A count needs a form of the sentence the translation lacks.</summary>
            MissingForm,

            /// <summary>A gap, tag or token was lost, added or changed.</summary>
            Codes,

            /// <summary>English has an access key (an underscore) and the translation does not.</summary>
            AccessKey,

            /// <summary>The English changed since this line was translated.</summary>
            EnglishChanged,

            /// <summary>Marked as changed and not looked at since.</summary>
            NeedsReview,

            /// <summary>Still the same as the English.</summary>
            Untranslated,
        }

        public sealed class Finding
        {
            public Kind Kind;
            public string Key;
            public int Line;

            /// <summary>The English now, where it matters to what was found.</summary>
            public string English;

            /// <summary>What the translation has.</summary>
            public string Found;

            /// <summary>For <see cref="Kind.Unknown"/>: the key it probably was.
            /// For <see cref="Kind.MissingForm"/>: the forms that are missing.</summary>
            public string Suggestion;

            /// <summary>For <see cref="Kind.Codes"/>: what English has that the translation lost.</summary>
            public List<string> Lost = new List<string>();

            /// <summary>For <see cref="Kind.Codes"/>: what the translation has that English does not.</summary>
            public List<string> Added = new List<string>();

            /// <summary>For <see cref="Kind.EnglishChanged"/>: the English it was translated from.</summary>
            public string Was;

            /// <summary>Whether the translation works as it is: everything but
            /// bad lines, lost codes and duplicates is only unfinished.</summary>
            public bool IsError => Kind == Kind.BadLine || Kind == Kind.Codes || Kind == Kind.Duplicate || Kind == Kind.Unknown;
        }

        public sealed class Result
        {
            public readonly List<Finding> Findings = new List<Finding>();

            /// <summary>How many of English's texts there are, a plural counting once.</summary>
            public int Total;

            /// <summary>How many of them the translation has, filled in.</summary>
            public int Translated;

            public int Of(Kind kind) => Findings.Count(f => f.Kind == kind);
        }

        /// <summary>
        /// Everything wrong with <paramref name="translation"/> as a translation
        /// of <paramref name="english"/> into <paramref name="code"/>.
        /// </summary>
        /// <param name="sourceCode">The language <paramref name="english"/> is
        /// actually in: ModForge's own file is English, a pack's own words are
        /// whatever the pack says it is written in.</param>
        public static Result Run(TextFile english, TextFile translation, string code, string sourceCode = "en")
        {
            var result = new Result();
            string[] forms = PluralRules.FormsFor(code);

            foreach (var bad in translation.BadLines)
            {
                result.Findings.Add(new Finding
                {
                    Kind = bad.DuplicateOf > 0 ? Kind.Duplicate : Kind.BadLine,
                    Line = bad.Line,
                    Key = bad.Key,
                    Found = bad.Content,
                    Suggestion = bad.DuplicateOf > 0 ? bad.DuplicateOf.ToString() : null,
                });
            }

            // What English has, a plural being one text with several forms.
            var plurals = new Dictionary<string, List<TextFile.Entry>>(StringComparer.OrdinalIgnoreCase);
            var plain = new List<TextFile.Entry>();
            foreach (var e in english.Entries)
            {
                string form = TextFile.FormOf(e.Key);
                if (form == null) { plain.Add(e); continue; }
                string b = TextFile.BaseOf(e.Key);
                List<TextFile.Entry> list;
                if (!plurals.TryGetValue(b, out list)) plurals[b] = list = new List<TextFile.Entry>();
                list.Add(e);
            }

            bool sameLanguage = PluralRules.LanguageOf(code) == PluralRules.LanguageOf(sourceCode ?? "en");

            foreach (var e in plain)
            {
                result.Total++;
                var mine = translation.Find(e.Key);
                if (mine == null)
                {
                    result.Findings.Add(new Finding { Kind = Kind.Missing, Key = e.Key, English = e.Text });
                    continue;
                }

                // A text with no words of the source's own - typed only in this
                // translation. There is nothing to hold it to: no codes it must
                // keep, no words it could still be equal to.
                if (e.Text.Length == 0 && mine.Text.Length > 0)
                {
                    result.Translated++;
                    continue;
                }
                if (mine.Text.Length == 0 && !mine.Blank && e.Text.Length > 0)
                {
                    result.Findings.Add(new Finding { Kind = Kind.Empty, Key = e.Key, Line = mine.Line, English = e.Text });
                    continue;
                }
                result.Translated++;
                CheckLine(result, e.Text, CodesIn(e.Text), CodesIn(e.Text), mine, sameLanguage);
            }

            foreach (var pair in plurals)
            {
                result.Total++;
                var englishForms = pair.Value;
                // Each translated form keeps the codes of the English form it
                // stands for - except that "one" may spell its number out ("a
                // quest") and so leave {count} behind. Nothing may appear that
                // no English form has.
                var anywhere = englishForms.SelectMany(f => CodesIn(f.Text)).Distinct().ToList();

                var missingForms = new List<string>();
                bool any = false;
                foreach (string form in forms)
                {
                    var mine = translation.Find(pair.Key + "." + form);
                    var source = englishForms.FirstOrDefault(f => string.Equals(TextFile.FormOf(f.Key), form, StringComparison.OrdinalIgnoreCase))
                                 ?? englishForms.FirstOrDefault(f => TextFile.FormOf(f.Key) == "other")
                                 ?? englishForms[englishForms.Count - 1];
                    if (mine == null || (mine.Text.Length == 0 && !mine.Blank)) { missingForms.Add(form); continue; }
                    any = true;
                    var required = CodesIn(source.Text);
                    if (form == "one") required.Remove("{count}");
                    CheckLine(result, source.Text, required, anywhere, mine, sameLanguage);
                }
                if (any) result.Translated++;
                if (missingForms.Count == forms.Length)
                {
                    result.Findings.Add(new Finding { Kind = Kind.Missing, Key = pair.Key, English = englishForms[englishForms.Count - 1].Text });
                }
                else if (missingForms.Count > 0)
                {
                    result.Findings.Add(new Finding
                    {
                        Kind = Kind.MissingForm,
                        Key = pair.Key,
                        Suggestion = string.Join(", ", missingForms),
                    });
                }
            }

            foreach (var pair in Unknowns(english, translation))
            {
                // Set aside on purpose by an update, so nothing written is
                // lost: not a mistake, and not a key to be put back.
                if (pair.Key.Heading == TextFileWriter.UnusedHeading) continue;
                result.Findings.Add(new Finding
                {
                    Kind = Kind.Unknown,
                    Key = pair.Key.Key,
                    Line = pair.Key.Line,
                    Found = pair.Key.Text,
                    Suggestion = pair.Value,
                });
            }

            result.Findings.Sort((a, b) =>
            {
                int byError = b.IsError.CompareTo(a.IsError);
                if (byError != 0) return byError;
                return (a.Line == 0 ? int.MaxValue : a.Line).CompareTo(b.Line == 0 ? int.MaxValue : b.Line);
            });
            return result;
        }

        private static void CheckLine(Result result, string english, List<string> required, List<string> allowed,
                                      TextFile.Entry mine, bool sameLanguage)
        {
            var found = CodesIn(mine.Text);
            var lost = required.Where(c => !found.Contains(c)).ToList();
            var added = found.Where(c => !allowed.Contains(c)).Distinct().ToList();
            if (lost.Count > 0 || added.Count > 0)
            {
                var f = new Finding { Kind = Kind.Codes, Key = mine.Key, Line = mine.Line, English = english, Found = mine.Text };
                f.Lost.AddRange(lost);
                f.Added.AddRange(added);
                result.Findings.Add(f);
            }

            if (TextCodes.HasAccessKey(english) && !TextCodes.HasAccessKey(mine.Text))
                result.Findings.Add(new Finding { Kind = Kind.AccessKey, Key = mine.Key, Line = mine.Line, English = english, Found = mine.Text });

            // A "changed from" that differs only by the space the file's trim
            // took off its end is no change: files written before 2026-09-28
            // have them.
            if (mine.ChangedFrom != null && PackTexts.Normal(mine.ChangedFrom) != PackTexts.Normal(english))
                result.Findings.Add(new Finding { Kind = Kind.NeedsReview, Key = mine.Key, Line = mine.Line, English = english, Found = mine.Text, Was = mine.ChangedFrom });
            // Told apart as the file can tell them: it trims every line it
            // reads, so a space at either end of the English never survives
            // into the note, and a line ending in one was out of date for ever.
            else if (mine.English != null && PackTexts.Normal(mine.English) != PackTexts.Normal(english))
                result.Findings.Add(new Finding { Kind = Kind.EnglishChanged, Key = mine.Key, Line = mine.Line, English = english, Found = mine.Text, Was = mine.English });

            // The file's own details (its language's name, who made it) are not
            // texts anybody translates.
            // Nor is a line marked as meant to read the same.
            if (!sameLanguage && PackTexts.Normal(mine.Text) == PackTexts.Normal(english)
                && HasWords(WithoutCodes(english)) && !mine.Same
                && !mine.Key.StartsWith(LanguageKeys, StringComparison.OrdinalIgnoreCase))
                result.Findings.Add(new Finding { Kind = Kind.Untranslated, Key = mine.Key, Line = mine.Line, English = english });
        }

        /// <summary>
        /// Every key English does not have, with the key it most likely was.
        /// <para/>
        /// A Replace All that turned "quest" into another word turned it in the
        /// keys too, and not in one of them but in all of a section. What it
        /// cannot move is where each line sits: the file is in English's order,
        /// so a run of unknown keys between two intact ones stands for the
        /// English keys between those two that the file lacks. When the counts
        /// agree they pair up in order; when they do not, each takes the
        /// likeliest-looking one from its own stretch.
        /// </summary>
        public static List<KeyValuePair<TextFile.Entry, string>> Unknowns(
            TextFile english, TextFile translation)
        {
            var found = new List<KeyValuePair<TextFile.Entry, string>>();
            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < english.Entries.Count; i++) order[english.Entries[i].Key] = i;

            Func<string, bool> known = key =>
                english.Has(key) || (TextFile.FormOf(key) != null && english.Entries.Any(e =>
                    TextFile.FormOf(e.Key) != null
                    && string.Equals(TextFile.BaseOf(e.Key), TextFile.BaseOf(key), StringComparison.OrdinalIgnoreCase)));

            // What English has that the translation lacks, a plural it has any
            // form of counting as present.
            Func<TextFile.Entry, bool> lacking = e =>
            {
                if (translation.Has(e.Key)) return false;
                if (TextFile.FormOf(e.Key) == null) return true;
                string b = TextFile.BaseOf(e.Key);
                return !translation.Entries.Any(x => TextFile.FormOf(x.Key) != null
                    && string.Equals(TextFile.BaseOf(x.Key), b, StringComparison.OrdinalIgnoreCase));
            };

            var entries = translation.Entries;
            int at = 0;
            int before = -1;
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (at < entries.Count)
            {
                if (known(entries[at].Key))
                {
                    int idx;
                    if (order.TryGetValue(entries[at].Key, out idx)) before = idx;
                    at++;
                    continue;
                }

                var run = new List<TextFile.Entry>();
                while (at < entries.Count && !known(entries[at].Key)) run.Add(entries[at++]);
                int after = english.Entries.Count;
                for (int j = at; j < entries.Count; j++)
                {
                    int idx;
                    if (order.TryGetValue(entries[j].Key, out idx)) { after = idx; break; }
                }

                var stretch = new List<string>();
                for (int i = before + 1; i < after && i < english.Entries.Count; i++)
                    if (i >= 0 && lacking(english.Entries[i]) && !claimed.Contains(english.Entries[i].Key))
                        stretch.Add(english.Entries[i].Key);

                if (stretch.Count == run.Count)
                {
                    for (int k = 0; k < run.Count; k++)
                    {
                        claimed.Add(stretch[k]);
                        found.Add(new KeyValuePair<TextFile.Entry, string>(run[k], stretch[k]));
                    }
                    continue;
                }
                foreach (var e in run)
                {
                    string guess = stretch.Count > 0
                        ? stretch.Where(k => !claimed.Contains(k)).OrderBy(k => Distance(e.Key.ToLowerInvariant(), k.ToLowerInvariant())).FirstOrDefault()
                        : Nearest(e.Key, english.Entries.Where(lacking).Select(x => x.Key).ToList());
                    if (guess != null) claimed.Add(guess);
                    found.Add(new KeyValuePair<TextFile.Entry, string>(e, guess));
                }
            }
            return found;
        }

        private static List<string> CodesIn(string text) => TextCodes.Of(text).Distinct().ToList();

        /// <summary>
        /// <paramref name="s"/> with its codes taken out: "&lt;size=60%&gt;..."
        /// is three dots, and "{PC}..." the same. The letters of a tag or a
        /// token are not words anybody translates, and a line of nothing else
        /// read as untranslated in every language, for ever.
        /// </summary>
        private static string WithoutCodes(string s)
        {
            foreach (string code in TextCodes.Of(s)) s = s.Replace(code, " ");
            return s;
        }

        private static bool HasWords(string s)
        {
            int run = 0;
            foreach (char c in s)
            {
                run = char.IsLetter(c) ? run + 1 : 0;
                if (run >= 2) return true;
            }
            return false;
        }

        /// <summary>The key in <paramref name="keys"/> closest to <paramref name="key"/>,
        /// if any is close enough to be a likely meaning.</summary>
        internal static string Nearest(string key, IList<string> keys)
        {
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (string k in keys)
            {
                int d = Distance(key.ToLowerInvariant(), k.ToLowerInvariant());
                if (d < bestDistance) { bestDistance = d; best = k; }
            }
            // Close enough is under half the longer key: a word changed, not a
            // different key altogether.
            if (best == null || bestDistance * 2 > Math.Max(key.Length, best.Length)) return null;
            return best;
        }

        private static int Distance(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                var t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }
    }
}
