using System;
using System.Collections.Generic;
using System.Text;

namespace SMSModForge.Shared
{
    /// <summary>
    /// One language's text: a plain file a person can open in Notepad, and fix
    /// with Replace All.
    /// <para/>
    /// Every text is one line, <c>key = text</c>. A line starting with <c>#</c>
    /// is a note for whoever is reading, a line in <c>[brackets]</c> is a
    /// heading, and neither means anything to the program. The one note that
    /// does is <c># en:</c> just above a line: the English that line was
    /// translated from, which is how a later version can tell the English has
    /// changed since. <c># changed from:</c> marks a line whose English moved
    /// on and that nobody has looked at since.
    /// <para/>
    /// Inside a text, <c>\n</c> is a line break and <c>\\</c> a backslash; any
    /// other backslash is itself, so a Windows path reads as written. Nothing
    /// after the = means "not translated yet", and shows the English; a text
    /// that should show nothing at all in a language - half of "Over 3
    /// seconds" in one whose word order has no use for it - is written
    /// <c>""</c>. A key is found whatever its case, because a Replace All can
    /// change it.
    /// <para/>
    /// Compiled into the editor and the plugin from one file, so a pack's
    /// translation is read the same way by the thing that checks it and the
    /// thing that plays it. Written for the plugin's compiler as well.
    /// </summary>
    public sealed class TextFile
    {
        /// <summary>The note that records the English a line was translated from.</summary>
        public const string EnglishNote = "en:";

        /// <summary>The note that marks a line whose English has changed since.</summary>
        public const string ChangedNote = "changed from:";

        /// <summary>
        /// The note that marks a line meant to read exactly as the English does
        /// - a name, a word every language shares. Without it such a line cannot
        /// be told from one nobody has translated yet, and anything that offers
        /// to translate the rest offers it again, for ever. The machine writes it
        /// when it gives a line back unchanged; a person can write it too.
        /// </summary>
        public const string SameNote = "same in this language";

        /// <summary>What a text that is empty on purpose is written as.</summary>
        public const string EmptyOnPurpose = "\"\"";

        /// <summary>The last part of a key that makes it one form of a plural.</summary>
        public static readonly string[] PluralForms = { "zero", "one", "two", "few", "many", "other" };

        public sealed class Entry
        {
            public string Key;
            public string Text;

            /// <summary>Written as <c>""</c>: nothing, and meant.</summary>
            public bool Blank;

            /// <summary>What <c># en:</c> said, or null when there was no such note.</summary>
            public string English;

            /// <summary>What <c># changed from:</c> said, or null.</summary>
            public string ChangedFrom;

            /// <summary>Marked <c># same in this language</c>: meant to read as the
            /// English does, so translated rather than waiting.</summary>
            public bool Same;

            /// <summary>The other notes above the line, without their <c>#</c>.</summary>
            public List<string> Notes = new List<string>();

            /// <summary>The heading the line sits under, or "".</summary>
            public string Heading = "";

            /// <summary>Line number in the file, from 1.</summary>
            public int Line;
        }

        /// <summary>A line the reader could not make sense of.</summary>
        public sealed class BadLine
        {
            public int Line;
            public string Content;

            /// <summary>For a key that appears twice: the line it first appeared on.</summary>
            public int DuplicateOf;

            public string Key;
        }

        /// <summary>Everything the file says before its first heading or text.</summary>
        public readonly List<string> TopNotes = new List<string>();

        public readonly List<Entry> Entries = new List<Entry>();
        public readonly List<BadLine> BadLines = new List<BadLine>();

        private readonly Dictionary<string, Entry> _byKey =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public Entry Find(string key)
        {
            Entry e;
            return key != null && _byKey.TryGetValue(key, out e) ? e : null;
        }

        /// <summary>The text for <paramref name="key"/>, or null when the file has none.</summary>
        public string Get(string key)
        {
            var e = Find(key);
            return e == null ? null : e.Text;
        }

        /// <summary>
        /// The text a translation gives <paramref name="key"/>, or null when it
        /// gives none - left out, or left empty - so the English shows instead.
        /// A text written <c>""</c> is given, and is "".
        /// </summary>
        public string Translated(string key)
        {
            var e = Find(key);
            if (e == null) return null;
            if (e.Blank) return "";
            return e.Text.Length == 0 ? null : e.Text;
        }

        public bool Has(string key) => Find(key) != null;

        /// <summary>Take <paramref name="key"/>'s text out of the file. False
        /// when it had none.</summary>
        public bool Remove(string key)
        {
            Entry e;
            if (key == null || !_byKey.TryGetValue(key, out e)) return false;
            _byKey.Remove(key);
            Entries.Remove(e);
            return true;
        }

        public void Add(Entry entry)
        {
            Entry had;
            if (_byKey.TryGetValue(entry.Key, out had))
            {
                BadLines.Add(new BadLine { Line = entry.Line, Content = entry.Key + " = " + entry.Text, DuplicateOf = had.Line, Key = entry.Key });
                return;
            }
            _byKey[entry.Key] = entry;
            Entries.Add(entry);
        }

        // ── Reading ────────────────────────────────────────────────────

        public static TextFile Parse(string content)
        {
            var file = new TextFile();
            if (string.IsNullOrEmpty(content)) return file;
            if (content[0] == '﻿') content = content.Substring(1);

            var notes = new List<string>();
            string english = null, changedFrom = null, heading = "";
            bool same = false;
            bool anything = false;
            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    // A blank line ends what the notes above were about, so a
                    // note is only ever read as belonging to the text it sits on.
                    if (!anything && notes.Count > 0) { file.TopNotes.AddRange(notes); }
                    notes.Clear(); english = null; changedFrom = null; same = false;
                    continue;
                }

                if (line[0] == '#')
                {
                    string note = line.Substring(1).Trim();
                    if (StartsWith(note, EnglishNote)) english = Unescape(note.Substring(EnglishNote.Length).TrimStart());
                    else if (StartsWith(note, ChangedNote)) changedFrom = Unescape(note.Substring(ChangedNote.Length).TrimStart());
                    else if (string.Equals(note, SameNote, StringComparison.OrdinalIgnoreCase)) same = true;
                    else notes.Add(note);
                    continue;
                }

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    if (!anything) file.TopNotes.AddRange(notes);
                    anything = true;
                    heading = line.Substring(1, line.Length - 2).Trim();
                    notes.Clear(); english = null; changedFrom = null; same = false;
                    continue;
                }

                int eq = line.IndexOf('=');
                string key = eq > 0 ? line.Substring(0, eq).Trim() : null;
                if (key == null || !IsKey(key))
                {
                    file.BadLines.Add(new BadLine { Line = i + 1, Content = line });
                    notes.Clear(); english = null; changedFrom = null; same = false;
                    continue;
                }

                anything = true;
                string value = line.Substring(eq + 1).Trim();
                bool blank = value == EmptyOnPurpose;
                file.Add(new Entry
                {
                    Key = key,
                    Text = blank ? "" : Unescape(value),
                    Blank = blank,
                    English = english,
                    ChangedFrom = changedFrom,
                    Same = same,
                    Notes = new List<string>(notes),
                    Heading = heading,
                    Line = i + 1,
                });
                notes.Clear(); english = null; changedFrom = null; same = false;
            }
            return file;
        }

        private static bool StartsWith(string s, string prefix)
            => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Letters, digits, dots, dashes and underscores, and no space.</summary>
        public static bool IsKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (char c in key)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')) return false;
            return true;
        }

        /// <summary><c>\n</c> becomes a line break and <c>\\</c> a backslash.
        /// Every other backslash stays, so <c>BepInEx\plugins</c> reads as written.</summary>
        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    if (next == 'n') { sb.Append('\n'); i++; continue; }
                    if (next == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The other way: what goes on the line for <paramref name="s"/>.</summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r') continue;
                if (c == '\n') { sb.Append("\\n"); continue; }
                // Only a backslash that would otherwise be read as an escape
                // needs doubling: one followed by n or by another backslash.
                if (c == '\\' && i + 1 < s.Length && (s[i + 1] == 'n' || s[i + 1] == '\\')) { sb.Append("\\\\"); continue; }
                if (c == '\\' && i + 1 == s.Length) { sb.Append('\\'); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ── Plural keys ────────────────────────────────────────────────

        /// <summary>The form a key names (<c>one</c>, <c>other</c>...), or null for a plain key.</summary>
        public static string FormOf(string key)
        {
            int dot = key.LastIndexOf('.');
            if (dot < 0) return null;
            string last = key.Substring(dot + 1);
            foreach (string f in PluralForms)
                if (string.Equals(f, last, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        /// <summary>The key without its plural form.</summary>
        public static string BaseOf(string key)
        {
            string form = FormOf(key);
            return form == null ? key : key.Substring(0, key.Length - form.Length - 1);
        }
    }
}
