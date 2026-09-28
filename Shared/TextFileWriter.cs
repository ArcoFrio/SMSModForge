using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Writes a translation file: a new one to start from, or an existing one
    /// brought up to date with the English.
    /// <para/>
    /// The result is always in the English file's order, under its headings and
    /// with its notes, so any two translations line up and a translator reads
    /// the texts in the order they appear on screen. Each text carries the
    /// English it stands for in a <c># en:</c> note. A text nobody has
    /// translated yet is the English itself, so the file works the moment it
    /// exists and fills in as somebody works through it.
    /// <para/>
    /// Nothing a translator wrote is thrown away. A line whose English changed
    /// keeps its translation and gains <c># changed from:</c> with the English
    /// it was made from, until somebody looks at it and deletes the note. A
    /// line English no longer has goes to the end, under its own heading.
    /// </summary>
    public static class TextFileWriter
    {
        public const string UnusedHeading = "Not used by this version";

        /// <param name="english">The English file, which decides what exists and in what order.</param>
        /// <param name="existing">The translation so far, or null to start one.</param>
        /// <param name="code">The language it is in.</param>
        /// <param name="topNotes">What goes at the top of a new file; an existing
        /// file keeps its own.</param>
        public static string Build(TextFile english, TextFile existing, string code, IList<string> topNotes)
        {
            var sb = new StringBuilder();
            var notes = existing != null && existing.TopNotes.Count > 0 ? (IList<string>)existing.TopNotes : topNotes;
            if (notes != null)
                foreach (string n in notes) sb.Append("# ").Append(n).Append("\r\n");

            string[] forms = PluralRules.FormsFor(code);
            string heading = null;
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in english.Entries)
            {
                string form = TextFile.FormOf(e.Key);
                string baseKey = TextFile.BaseOf(e.Key);
                if (form != null && written.Contains(baseKey)) continue;

                if (e.Heading != heading)
                {
                    heading = e.Heading;
                    if (!string.IsNullOrEmpty(heading)) sb.Append("\r\n[").Append(heading).Append("]\r\n");
                }
                sb.Append("\r\n");
                foreach (string n in e.Notes) sb.Append("# ").Append(n).Append("\r\n");

                if (form == null)
                {
                    WriteText(sb, e.Key, e.Text, existing);
                    written.Add(e.Key);
                    continue;
                }

                // A plural: the forms THIS language needs, each from the English
                // form that says the same, or English's "other" where English has
                // no such form (Russian's "few").
                written.Add(baseKey);
                var englishForms = english.Entries.Where(x => string.Equals(TextFile.BaseOf(x.Key), baseKey, StringComparison.OrdinalIgnoreCase)
                                                              && TextFile.FormOf(x.Key) != null).ToList();
                if (forms.Length > 1) sb.Append("# ").Append(FormGuide(code, forms)).Append("\r\n");
                foreach (string f in forms)
                {
                    var source = englishForms.FirstOrDefault(x => TextFile.FormOf(x.Key) == f)
                                 ?? englishForms.FirstOrDefault(x => TextFile.FormOf(x.Key) == "other")
                                 ?? englishForms[englishForms.Count - 1];
                    WriteText(sb, baseKey + "." + f, source.Text, existing);
                }
                foreach (var x in englishForms) written.Add(x.Key);
            }

            // What the translator wrote that English no longer asks for.
            if (existing != null)
            {
                var unused = existing.Entries.Where(x => !english.Has(x.Key)
                    && !(TextFile.FormOf(x.Key) != null && written.Contains(TextFile.BaseOf(x.Key)))).ToList();
                if (unused.Count > 0)
                {
                    sb.Append("\r\n[").Append(UnusedHeading).Append("]\r\n");
                    foreach (var x in unused)
                    {
                        sb.Append("\r\n");
                        if (x.English != null) sb.Append("# ").Append(TextFile.EnglishNote).Append(' ').Append(TextFile.Escape(x.English)).Append("\r\n");
                        sb.Append(x.Key).Append(" = ").Append(TextFile.Escape(x.Text)).Append("\r\n");
                    }
                }
            }
            return sb.ToString();
        }

        private static void WriteText(StringBuilder sb, string key, string englishNow, TextFile existing)
        {
            var mine = existing == null ? null : existing.Find(key);
            string text = englishNow;
            string changedFrom = null;
            if (mine != null)
            {
                bool untouched = mine.English != null && mine.Text == mine.English && !mine.Blank;
                if (untouched || (mine.Text.Length == 0 && !mine.Blank))
                {
                    // Never translated: it follows the English.
                    text = englishNow;
                }
                else
                {
                    text = mine.Text;
                    changedFrom = mine.ChangedFrom
                                  ?? (mine.English != null && mine.English != englishNow ? mine.English : null);
                }
            }

            sb.Append("# ").Append(TextFile.EnglishNote).Append(' ').Append(TextFile.Escape(englishNow)).Append("\r\n");
            if (changedFrom != null)
                sb.Append("# ").Append(TextFile.ChangedNote).Append(' ').Append(TextFile.Escape(changedFrom)).Append("\r\n");
            // Kept only while it is still true: the line says what the English
            // says NOW. Once the English moves on, "the same" was about words
            // nobody says any more, and the line is waiting again.
            else if (mine != null && mine.Same && text == englishNow
                     && (mine.English == null || mine.English == englishNow))
                sb.Append("# ").Append(TextFile.SameNote).Append("\r\n");
            bool blank = mine != null && mine.Blank && text.Length == 0;
            sb.Append(key).Append(" = ").Append(blank ? TextFile.EmptyOnPurpose : TextFile.Escape(text)).Append("\r\n");
        }

        /// <summary>
        /// Which numbers take which form, from the rule itself: "one: 1, 21,
        /// 31 · few: 2, 3, 4, 22 · many: 0, 5, 6, 7". A translator knows their
        /// grammar; what they cannot know is which name ModForge gave each case.
        /// </summary>
        public static string FormGuide(string code, string[] forms)
        {
            var examples = forms.ToDictionary(f => f, f => new List<long>());
            for (long n = 0; n <= 200 && examples.Values.Any(l => l.Count < 4); n++)
            {
                string f = PluralRules.FormOf(code, n);
                List<long> list;
                if (examples.TryGetValue(f, out list) && list.Count < 4) list.Add(n);
            }
            return string.Join(" · ", forms.Select(f => f + ": " + string.Join(", ", examples[f]) + (examples[f].Count >= 4 ? "…" : "")));
        }
    }
}
