using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// A line's sounds (1.8.0): every auto-trigger pattern in it, in reading
    /// order, and how each plays - its volume, when it starts, whether its
    /// words show, whether it waits for the typewriter. The editor and the
    /// game read lines with this one class, so the row an author sets and the
    /// sound a player hears are the same match.
    /// <para/>
    /// WHEN A SOUND STARTS. The first starts its delay after the line begins;
    /// each one after starts its delay after the one before it ENDS - zero is
    /// "right after", a negative delay overlaps them (the author: "consecutive
    /// SFXs should always have their default starting point after the previous
    /// finishes, and the delay amounts can be negative for overlapping"). A
    /// sound set to wait for the typewriter starts its delay after the
    /// typewriter has shown its first letter instead, whatever came before.
    /// <para/>
    /// Stored sparse on the node (<c>cues</c>): a sound with every setting at
    /// its default has no entry. An entry names its sound by its place in
    /// reading order AND its pattern, so a line edited until another pattern
    /// stands in that place does not take over a setting meant for another.
    /// </summary>
    public static class SoundCues
    {
        /// <summary>One pattern found in a line.</summary>
        public sealed class Match
        {
            /// <summary>Its place among the line's sounds, from 1.</summary>
            public int Order;
            public int Index;
            public int Length;
            /// <summary>The pattern as the sound has it (<c>*clink*</c>).</summary>
            public string Pattern;
            /// <summary>The sound it plays, by key.</summary>
            public string Sound;
            /// <summary>Its place among the line's matches of the same pattern, from 1.</summary>
            public int OfItsPattern;
        }

        /// <summary>How one sound in a line plays, when not by default.</summary>
        public sealed class Setting
        {
            public int At;
            public string Pattern = "";
            /// <summary>Of the sound's own volume: 1 is as the sound is set.</summary>
            public float? Volume;
            /// <summary>Seconds; see the class's WHEN A SOUND STARTS.</summary>
            public float? Delay;
            /// <summary>The pattern's words are left out of the shown line.</summary>
            public bool Hidden;
            /// <summary>Starts when the typewriter reaches it.</summary>
            public bool Typed;

            public bool IsDefault
            {
                get
                {
                    return (Volume == null || Math.Abs(Volume.Value - 1f) < 0.0005f)
                           && (Delay == null || Math.Abs(Delay.Value) < 0.0005f) && !Hidden && !Typed;
                }
            }
        }

        /// <summary>
        /// Every pattern in <paramref name="text"/>, in reading order, matched
        /// as the game matches them: any case; where two start at one place
        /// the longer wins; matches do not overlap.
        /// </summary>
        /// <param name="patterns">Each pattern with the sound it plays.</param>
        public static List<Match> Find(string text, IList<KeyValuePair<string, string>> patterns)
        {
            var found = new List<Match>();
            if (string.IsNullOrEmpty(text) || patterns == null || patterns.Count == 0) return found;
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int at = 0;
            while (at < text.Length)
            {
                KeyValuePair<string, string>? hit = null;
                foreach (var p in patterns)
                {
                    string pattern = p.Key;
                    if (string.IsNullOrEmpty(pattern) || at + pattern.Length > text.Length) continue;
                    if (hit != null && pattern.Length <= hit.Value.Key.Length) continue;
                    if (string.Compare(text, at, pattern, 0, pattern.Length, StringComparison.OrdinalIgnoreCase) == 0) hit = p;
                }
                if (hit == null) { at++; continue; }
                int nth;
                seen.TryGetValue(hit.Value.Key, out nth);
                seen[hit.Value.Key] = ++nth;
                found.Add(new Match
                {
                    Order = found.Count + 1,
                    Index = at,
                    Length = hit.Value.Key.Length,
                    Pattern = hit.Value.Key,
                    Sound = hit.Value.Value,
                    OfItsPattern = nth,
                });
                at += hit.Value.Key.Length;
            }
            return found;
        }

        /// <summary>The setting a node gives this match, or null for the
        /// defaults - including a setting left at its place by a pattern that
        /// no longer stands there.</summary>
        public static Setting For(Match match, IList<Setting> settings)
        {
            if (match == null || settings == null) return null;
            foreach (var s in settings)
                if (s.At == match.Order && string.Equals(s.Pattern, match.Pattern, StringComparison.OrdinalIgnoreCase))
                    return s;
            return null;
        }

        /// <summary>A node's settings from its <c>cues</c> array.</summary>
        public static List<Setting> Read(JToken cues)
        {
            var list = new List<Setting>();
            var array = cues as JArray;
            if (array == null) return list;
            foreach (var item in array)
            {
                var o = item as JObject;
                if (o == null) continue;
                var s = new Setting
                {
                    At = (int?)o["at"] ?? 0,
                    Pattern = (string)o["pattern"] ?? "",
                    Volume = (float?)o["volume"],
                    Delay = (float?)o["delay"],
                    Hidden = (bool?)o["hidden"] ?? false,
                    Typed = (bool?)o["typed"] ?? false,
                };
                if (s.At > 0) list.Add(s);
            }
            return list;
        }

        // ── When each one starts ────────────────────────────────────────

        /// <summary>
        /// Where each sound starts, in seconds after the line begins, given how
        /// long each lasts and when the typewriter reaches each - the plan the
        /// editor's "Play the line" plays, and the game follows as it goes.
        /// </summary>
        public static double[] Plan(IList<Match> matches, IList<Setting> settings,
                                    Func<Match, double> length, Func<Match, double> typedAt)
        {
            var starts = new double[matches.Count];
            double previousEnd = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                starts[i] = StartOf(matches[i], For(matches[i], settings), i == 0, previousEnd, typedAt);
                previousEnd = starts[i] + Math.Max(0, length(matches[i]));
            }
            return starts;
        }

        /// <summary>
        /// One sound's start: <paramref name="previousEnd"/> is when the one
        /// before it ends (nothing for the first). Never before the line.
        /// </summary>
        public static double StartOf(Match match, Setting setting, bool first, double previousEnd,
                                     Func<Match, double> typedAt)
        {
            double delay = setting != null && setting.Delay.HasValue ? setting.Delay.Value : 0;
            double from;
            if (setting != null && setting.Typed) { from = typedAt(match); delay = Math.Max(0, delay); }
            else if (first) { from = 0; delay = Math.Max(0, delay); }
            else from = previousEnd;
            return Math.Max(0, from + delay);
        }

        /// <summary>A sound's volume against its own: the setting's, or as set.</summary>
        public static float VolumeOf(Setting setting)
        {
            return setting != null && setting.Volume.HasValue ? Math.Max(0f, setting.Volume.Value) : 1f;
        }

        // ── Reading a line ───────────────────────────────────────────────

        /// <summary>
        /// How many characters the text engine has shown before
        /// <paramref name="index"/> - what the typewriter must reach for a
        /// pattern there to have begun appearing.
        /// </summary>
        public static int VisibleIndex(string text, int index)
        {
            if (string.IsNullOrEmpty(text) || index <= 0) return 0;
            return RichText.VisibleLength(text.Substring(0, Math.Min(index, text.Length)));
        }

        /// <summary>
        /// The size the text is drawn at, at <paramref name="index"/>, as a
        /// share of its own - <c>&lt;size=140%&gt;</c> is 1.4 and so is
        /// <c>&lt;size=1.4em&gt;</c> - or null where no size is in effect or it
        /// is given in points, which says nothing about the line's own. A size
        /// left open runs to the end of the line, as the text engine has it.
        /// </summary>
        public static double? SizeAt(string text, int index)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var open = new List<double?>();
            int i = 0;
            int end = Math.Min(index, text.Length);
            while (i < end)
            {
                int lt = text.IndexOf('<', i);
                if (lt < 0 || lt >= end) break;
                int gt = text.IndexOf('>', lt + 1);
                if (gt < 0) break;
                string tag = text.Substring(lt + 1, gt - lt - 1).Trim();
                if (tag.StartsWith("size=", StringComparison.OrdinalIgnoreCase)) open.Add(SizeValue(tag.Substring(5)));
                else if (tag.Equals("/size", StringComparison.OrdinalIgnoreCase) && open.Count > 0) open.RemoveAt(open.Count - 1);
                i = gt + 1;
            }
            return open.Count == 0 ? null : open[open.Count - 1];
        }

        private static double? SizeValue(string value)
        {
            value = value.Trim().Trim('"', '\'');
            double n;
            if (value.EndsWith("%", StringComparison.Ordinal)
                && double.TryParse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out n))
                return n / 100.0;
            if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out n))
                return n;
            return null;
        }

        /// <summary>
        /// <paramref name="shown"/> without the patterns a node hides, found by
        /// which of their pattern's matches they are in the pack's own words
        /// (<paramref name="matches"/>) - so a translated line loses the same
        /// "*clink*", and one whose translator changed the pattern keeps it.
        /// </summary>
        public static string Hide(string shown, IList<Match> matches, IList<Setting> settings)
        {
            if (string.IsNullOrEmpty(shown) || matches == null || settings == null || settings.Count == 0) return shown;
            var cut = new List<KeyValuePair<int, int>>();
            foreach (var m in matches)
            {
                var s = For(m, settings);
                if (s == null || !s.Hidden) continue;
                int at = NthIndexOf(shown, m.Pattern, m.OfItsPattern);
                if (at >= 0) cut.Add(new KeyValuePair<int, int>(at, m.Pattern.Length));
            }
            if (cut.Count == 0) return shown;
            cut.Sort((a, b) => b.Key.CompareTo(a.Key));
            string result = shown;
            foreach (var c in cut)
            {
                int from = c.Key, length = c.Value;
                // One space goes with it, so "Oh *clink* there" does not show two.
                if (from + length < result.Length && result[from + length] == ' ' && (from == 0 || result[from - 1] == ' ')) length++;
                result = result.Remove(from, length);
            }
            return result.Trim();
        }

        private static int NthIndexOf(string text, string pattern, int nth)
        {
            int at = -1;
            for (int i = 0; i < nth; i++)
            {
                at = text.IndexOf(pattern, at + 1, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return -1;
            }
            return at;
        }
    }
}
