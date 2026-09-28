using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Every text of a pack that a player reads, each under a key that stays
    /// put while the pack changes around it.
    /// <para/>
    /// The editor writes a pack's translation file from this list, and the
    /// plugin lays a translation back over the manifest from the same list, so
    /// the two sides cannot name a text differently: there is one walk, not
    /// two that have to agree. It reads the manifest as saved, which is also
    /// what the plugin gets - a line of the game's that the pack did not
    /// rewrite is not in it, and so is not something to translate.
    /// <para/>
    /// A key is made of the ids the pack already keeps stable - a dialogue's
    /// key and a line's id, a quest's key and a task's - never of positions,
    /// so moving a line or a task leaves its translation where it was.
    /// </summary>
    public static class PackTexts
    {
        /// <summary>The folder in a pack that holds its translations, one file
        /// per language, named for it: <c>translations/es.txt</c>.</summary>
        public const string Folder = "translations";

        public const string Extension = ".txt";

        /// <summary>The manifest field naming the language the pack is written
        /// in - its own words, the ones every translation is made from.</summary>
        public const string LanguageField = "language";

        /// <summary>What a pack is written in when it does not say: every pack
        /// made before it could say was written in English.</summary>
        public const string DefaultLanguage = "en";

        /// <summary>
        /// Which of a pack's translations a player reading
        /// <paramref name="player"/> is given, or null to play it in its own
        /// words. The pack's own language counts as one it has: a pack written
        /// in Brazilian Portuguese is read in its own words by a Brazilian, not
        /// in a Portuguese translation it happens to carry.
        /// </summary>
        public static string Choose(string player, string own, IEnumerable<string> translations)
        {
            var offered = new List<string>(translations ?? Enumerable.Empty<string>());
            string ownCode = string.IsNullOrEmpty(own) ? DefaultLanguage : own;
            var files = new HashSet<string>(offered, StringComparer.OrdinalIgnoreCase);
            offered.Add(ownCode);
            string best = LanguageMatch.Best(player, offered);
            if (best == null || !files.Contains(best)
                || string.Equals(best, ownCode, StringComparison.OrdinalIgnoreCase)) return null;
            return best;
        }

        /// <summary>The language <paramref name="manifest"/> is written in.</summary>
        public static string LanguageOf(JObject manifest)
        {
            var value = manifest == null ? null : manifest[LanguageField];
            string code = value != null && value.Type == JTokenType.String ? ((string)value).Trim() : "";
            return code.Length > 0 && TextFile.IsKey(code) ? code : DefaultLanguage;
        }

        /// <summary>
        /// Where a dialogue line's own words are kept when a translation
        /// replaces them, for whatever still has to read the original: the
        /// sound cues that listen for words in a line are written in the
        /// pack's language, not the player's.
        /// </summary>
        public const string OriginalTextKey = "__originalText";

        /// <summary>
        /// The words a line's sound cues are listened for in: the pack's own,
        /// whatever language the player reads it in - a cue is written once, in
        /// the pack's language, and "*slap*" read as "*шлёп*" is still a slap.
        /// The text shown when the pack has no words of its own for the line
        /// (one typed only in a translation), since that is all there is.
        /// </summary>
        public static string CueText(JObject node)
        {
            if (node == null) return "";
            string own = (string)node[OriginalTextKey];
            return !string.IsNullOrEmpty(own) ? own : (string)node["text"] ?? "";
        }

        public enum Kind
        {
            Line,
            CharacterName,
            NavigatorLabel,
            MapLabel,
            QuestTitle,
            QuestDescription,
            TaskName,
            TaskQuestDescription,
            GameTaskQuestDescription,
            UiText,
        }

        /// <summary>One text, where it is, and what it belongs to.</summary>
        public sealed class Site
        {
            public string Key;
            public Kind Kind;

            /// <summary>The object holding the text, and the property it is in.</summary>
            public JObject Holder;
            public string Field;

            /// <summary>What it belongs to: the dialogue, character, quest, UI or
            /// place key; for a map button, its district.</summary>
            public string Owner;

            /// <summary>A line's id, a task's key, a button's target, a UI
            /// object's path.</summary>
            public string Detail;

            /// <summary>For a line, the character who says it, by key.</summary>
            public string Speaker;

            public string Text
            {
                get { return (string)Holder[Field] ?? ""; }
            }
        }

        /// <summary>Every text a player of this pack reads, in the order the
        /// manifest holds them.</summary>
        /// <param name="withEmpty">
        /// Also list the texts the pack leaves empty - a line typed only in a
        /// translation has no words of the pack's own, and without a key of its
        /// own its translation could not be found. Keyed after every text that
        /// has words, so listing them never moves anybody else's key: two
        /// buttons to one level are numbered by order, and an empty one counted
        /// first would have taken the number a translation was filed under.
        /// </param>
        public static List<Site> Of(JObject manifest, bool withEmpty = false)
        {
            var walk = new Walk(withEmpty);
            if (manifest == null) return walk.Sites;

            // Names above the lines, first: they are what a translator meets in
            // every conversation after.
            foreach (string list in new[] { "characters", "actors" })
                foreach (var c in Objects(manifest[list]))
                {
                    string key = (string)c["key"];
                    if (string.IsNullOrEmpty(key)) continue;
                    walk.Add(c, "displayName", Kind.CharacterName, key, null,
                             "character." + Segment(key) + ".name");
                }

            foreach (var d in Objects(manifest["dialogues"]))
            {
                string key = (string)d["key"];
                if (string.IsNullOrEmpty(key)) continue;
                foreach (var n in Objects(d["nodes"]))
                {
                    var id = n["id"];
                    if (id == null || id.Type != JTokenType.Integer) continue;
                    // A line of the game's that the pack changed something else
                    // about - its conditions, who says it - is saved with its
                    // text blanked: the words are still the game's, not the
                    // pack's, and not empty either.
                    if (KeepsTheGamesText(n)) continue;
                    string idText = ((long)id).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var site = walk.Add(n, "text", Kind.Line, key, idText, LineKey(key, idText));
                    if (site != null) site.Speaker = (string)n["actor"];
                }
            }

            foreach (var q in Objects(manifest["quests"]))
            {
                string key = (string)q["key"];
                if (string.IsNullOrEmpty(key)) continue;
                string root = "quest." + Segment(key);
                bool games = !string.IsNullOrEmpty((string)q["source"]);

                // One of the game's quests keeps the game's title: only the
                // paragraph under it and what the pack adds are the pack's.
                if (!games) walk.Add(q, "title", Kind.QuestTitle, key, null, root + ".title");
                walk.Add(q, "description", Kind.QuestDescription, key, null, root + ".description");
                Tasks(walk, q["tasks"], key, root);
                if (games)
                {
                    foreach (var h in Objects(q["vanillaTasks"]))
                    {
                        string task = ((string)h["task"] ?? "").Trim();
                        if (task.Length == 0) continue;
                        walk.Add(h, "questDescription", Kind.GameTaskQuestDescription, key, task,
                                 root + ".gameTask." + Segment(task) + ".questDescription");
                    }
                    Tasks(walk, q[QuestTreeEdits.AddedTasksKey], key, root);
                }
            }

            foreach (var p in Objects(manifest["places"]))
            {
                string key = (string)p["key"];
                if (string.IsNullOrEmpty(key)) continue;
                Buttons(walk, p["navigatorButtons"], key, "navigator." + Segment(key));
            }
            foreach (var e in Objects(manifest["vanillaExtensions"]))
            {
                string source = (string)e["source"];
                if (string.IsNullOrEmpty(source)) continue;
                Buttons(walk, e["navigatorButtons"], source, "navigator." + Segment(source));
            }

            foreach (var b in Objects(manifest["mapButtons"]))
            {
                string district = (string)b["district"] ?? "";
                string target = (string)b["target"] ?? "";
                walk.Add(b, "label", Kind.MapLabel, district, target,
                         "mapButton." + Segment(district) + "." + Segment(target) + ".label");
            }

            foreach (var ui in Objects(manifest["uis"]))
            {
                string id = (string)ui["id"];
                if (string.IsNullOrEmpty(id)) id = (string)ui["name"];
                if (string.IsNullOrEmpty(id)) continue;
                bool games = !string.IsNullOrEmpty((string)ui["source"]);
                UiNodes(walk, ui["nodes"], id, "ui." + Segment(id), "", games);
            }

            walk.KeyTheEmptyOnes();
            return walk.Sites;
        }

        /// <summary>Whether a text has no words of its own: absent, or nothing
        /// but spaces.</summary>
        public static bool IsEmpty(string text) => Normal(text).Length == 0;

        /// <summary>
        /// The key of a line of a conversation: the pack's own lines, and the
        /// game's lines in a conversation the pack extends, alike. One form for
        /// both is what makes a translation of a game line go out of date the
        /// moment the pack rewrites that line - its note is the game's words,
        /// and the line then says the pack's.
        /// </summary>
        public static string LineKey(string dialogueKey, string nodeId)
            => "dialogue." + Segment(dialogueKey) + "." + nodeId;

        /// <summary>
        /// Whether a saved line is one of the game's that the pack changed
        /// something other than its words about: it names the fields it
        /// changes, and its text is not one of them. A line the pack added
        /// names none, and neither does a line of a conversation of its own.
        /// </summary>
        public static bool KeepsTheGamesText(JObject node)
        {
            var overrides = node == null ? null : node[OverridesKey] as JArray;
            if (overrides == null) return false;
            foreach (var field in overrides)
                if (string.Equals((string)field, "text", StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>A saved line's list of what it changes about the game's.</summary>
        public const string OverridesKey = "overrides";

        /// <summary>Whether <paramref name="key"/> is a line of the conversation
        /// <paramref name="dialogueKey"/> - see <see cref="LineKey"/>.</summary>
        public static bool IsLineKeyOf(string key, string dialogueKey)
            => key != null && key.StartsWith("dialogue." + Segment(dialogueKey) + ".", StringComparison.OrdinalIgnoreCase);

        private static void Tasks(Walk walk, JToken tasks, string quest, string root)
        {
            foreach (var t in Objects(tasks))
            {
                string key = ((string)t["key"] ?? "").Trim();
                if (key.Length == 0) continue;
                string at = root + ".task." + Segment(key);
                walk.Add(t, "name", Kind.TaskName, quest, key, at + ".name");
                walk.Add(t, "questDescription", Kind.TaskQuestDescription, quest, key, at + ".questDescription");
                Tasks(walk, t["subtasks"], quest, root);
            }
        }

        private static void Buttons(Walk walk, JToken buttons, string owner, string root)
        {
            foreach (var b in Objects(buttons))
            {
                string target = (string)b["target"] ?? "";
                walk.Add(b, "label", Kind.NavigatorLabel, owner, target,
                         root + "." + Segment(target) + ".label");
            }
        }

        /// <summary>
        /// A UI's texts, by the names of the objects down to each one. On a
        /// screen of the game's, only a text the pack says it changes is the
        /// pack's: the rest of what the node holds is a copy of the game's that
        /// the plugin never writes.
        /// </summary>
        private static void UiNodes(Walk walk, JToken nodes, string ui, string root, string path, bool games)
        {
            foreach (var n in Objects(nodes))
            {
                string name = (string)n["name"] ?? "";
                string here = path.Length == 0 ? name : path + "/" + name;
                string key = root + "." + Segment(name.Length == 0 ? "object" : name);
                var text = n["text"] as JObject;
                if (text != null && (!games || IsTrue(n["overrideText"])))
                    walk.Add(text, "value", Kind.UiText, ui, here, key + ".text");
                UiNodes(walk, n["children"], ui, key, here, games);
            }
        }

        /// <summary>One walk over a manifest: the sites found, and the keys
        /// already given out.</summary>
        private sealed class Walk
        {
            public readonly List<Site> Sites = new List<Site>();
            private readonly HashSet<string> _used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly List<KeyValuePair<Site, string>> _empty = new List<KeyValuePair<Site, string>>();
            private readonly bool _withEmpty;

            public Walk(bool withEmpty) { _withEmpty = withEmpty; }

            public Site Add(JObject holder, string field, Kind kind, string owner, string detail, string key)
            {
                var value = holder[field];
                bool empty = value == null || value.Type == JTokenType.Null
                             || (value.Type == JTokenType.String && IsEmpty((string)value));
                if (empty ? !_withEmpty : value.Type != JTokenType.String) return null;

                var site = new Site
                {
                    Kind = kind, Holder = holder, Field = field,
                    Owner = owner ?? "", Detail = detail ?? "",
                };
                Sites.Add(site);
                if (empty) _empty.Add(new KeyValuePair<Site, string>(site, key));
                else site.Key = Unique(key);
                return site;
            }

            /// <summary>The empty texts' keys, from whatever the texts with
            /// words left free - see <see cref="Of"/>.</summary>
            public void KeyTheEmptyOnes()
            {
                foreach (var pair in _empty) pair.Key.Key = Unique(pair.Value);
                _empty.Clear();
            }

            // Two things with the same name in the same place - two buttons to
            // one level, two objects called "Text" side by side - are told
            // apart by order, which is the best there is when the pack itself
            // cannot tell them apart.
            private string Unique(string key)
            {
                string unique = key;
                for (int i = 2; !_used.Add(unique); i++)
                    unique = key.Substring(0, key.LastIndexOf('.')) + "-" + i + key.Substring(key.LastIndexOf('.'));
                return unique;
            }
        }

        /// <summary>
        /// A pack's name for something as one part of a key: letters, digits,
        /// <c>_</c> and <c>-</c> kept, anything else - a space, a dot, the colon
        /// in <c>vanilla:Beach</c> - made <c>_</c>.
        /// </summary>
        public static string Segment(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length == 0 ? "_" : sb.ToString();
        }

        private static IEnumerable<JObject> Objects(JToken token)
        {
            var array = token as JArray;
            return array == null ? Enumerable.Empty<JObject>() : array.OfType<JObject>();
        }

        private static bool IsTrue(JToken token)
            => token != null && token.Type == JTokenType.Boolean && (bool)token;

        // ── Laying a translation over a pack ────────────────────────────

        /// <summary>What <see cref="Apply"/> did.</summary>
        public sealed class Applied
        {
            /// <summary>Texts now in the translation's language.</summary>
            public int Translated;

            /// <summary>Which texts those are, by key.</summary>
            public readonly List<string> TranslatedKeys = new List<string>();

            /// <summary>Texts the translation has nothing for, left as the pack wrote them.</summary>
            public int Untranslated;

            /// <summary>
            /// Texts the pack has changed since they were translated: the
            /// translation's English note no longer matches, so the translation
            /// is of something the pack no longer says, and the pack's own
            /// words stand until the translation is brought up to date.
            /// </summary>
            public readonly List<string> Outdated = new List<string>();

            /// <summary>
            /// Texts the pack leaves empty and the translation did not fill,
            /// given the words of another translation instead: a line typed only
            /// in one language has no words of the pack's own to fall back on,
            /// and an empty line is worse than one in the wrong language.
            /// </summary>
            public readonly List<string> Borrowed = new List<string>();

            public int Total
            {
                get { return Translated + Untranslated + Outdated.Count; }
            }
        }

        /// <summary>
        /// Put <paramref name="translation"/>'s texts in place of the pack's,
        /// in the manifest itself, before anything reads it.
        /// <para/>
        /// A text is replaced only while the words the translator worked from -
        /// the <c># en:</c> note above it - are still what the pack says. A file
        /// with no note for a text is taken at its word.
        /// <para/>
        /// A text the pack leaves empty is one typed only in a translation. It
        /// takes <paramref name="translation"/>'s words if it has them, and
        /// otherwise the first of <paramref name="others"/> that does, so it is
        /// never blank. <paramref name="others"/> is asked for only when such a
        /// text is met, which on most packs is never.
        /// </summary>
        /// <param name="translation">The player's language, or null to play the
        /// pack in its own words.</param>
        /// <param name="others">The pack's other translations, in the order to
        /// try them.</param>
        public static Applied Apply(JObject manifest, TextFile translation,
                                    Func<IList<TextFile>> others = null)
            => Lay(manifest, translation, others, write: true);

        /// <summary>
        /// What <see cref="Apply"/> would do with <paramref name="translation"/>,
        /// without doing it: how many of the pack's texts a player would read
        /// in its language. The same count as the one the plugin logs when it
        /// loads the pack, so a warning quoting it and the log agree.
        /// <para/>
        /// Only the pack's own texts are counted: one typed only in a
        /// translation is not a text of the pack's until somebody gives it
        /// words, and borrowing is left out because it is never asked for here.
        /// </summary>
        /// <param name="translation">Null counts every text as untranslated.</param>
        public static Applied Count(JObject manifest, TextFile translation)
            => Lay(manifest, translation, null, write: false);

        private static Applied Lay(JObject manifest, TextFile translation, Func<IList<TextFile>> others, bool write)
        {
            var result = new Applied();
            if (manifest == null) return result;

            IList<TextFile> borrowFrom = null;
            foreach (var site in Of(manifest, withEmpty: true))
            {
                string own = site.Text;
                bool empty = IsEmpty(own);

                string mine = translation == null ? null : Usable(translation, site.Key, own);
                if (mine == null && translation != null && !empty && translation.Translated(site.Key) != null
                    && !Current(translation.Find(site.Key), own))
                    result.Outdated.Add(site.Key);

                if (mine == null && empty && others != null)
                {
                    if (borrowFrom == null) borrowFrom = others() ?? new List<TextFile>();
                    foreach (var other in borrowFrom)
                    {
                        if (other == null || ReferenceEquals(other, translation)) continue;
                        mine = Usable(other, site.Key, own);
                        if (mine != null) { result.Borrowed.Add(site.Key); break; }
                    }
                    if (mine == null) continue;
                }
                else if (mine == null)
                {
                    // Nothing given, or nothing that differs from the pack's own
                    // words. An empty text with no words anywhere is not a text.
                    if (!empty && !result.Outdated.Contains(site.Key)) result.Untranslated++;
                    continue;
                }
                else
                {
                    result.Translated++;
                    result.TranslatedKeys.Add(site.Key);
                }

                if (!write) continue;
                if (site.Kind == Kind.Line && site.Holder[OriginalTextKey] == null)
                    site.Holder[OriginalTextKey] = own;
                site.Holder[site.Field] = mine;
            }
            return result;
        }

        /// <summary>
        /// What <paramref name="file"/> says in place of <paramref name="own"/>
        /// at <paramref name="key"/>, or null when it says nothing usable: no
        /// words, a translation of words the pack no longer says, or the pack's
        /// own words back - a file starts with every text in the pack's own
        /// words, so one nobody has got to yet reads exactly like the pack.
        /// </summary>
        public static string Usable(TextFile file, string key, string own)
        {
            string mine = file.Translated(key);
            if (mine == null) return null;
            if (!Current(file.Find(key), own)) return null;
            if (Normal(mine) == Normal(own)) return null;
            return mine;
        }

        /// <summary>Whether <paramref name="entry"/> was translated from what the
        /// pack says now. No note is taken at its word.</summary>
        private static bool Current(TextFile.Entry entry, string own)
            => entry == null || entry.English == null || Normal(entry.English) == Normal(own);
        /// <summary>The same words however the lines were ended and whatever
        /// the file's trim took from either end.</summary>
        public static string Normal(string s)
            => (s ?? "").Replace("\r", "").Trim();

        /// <summary>
        /// The file name of each translation a pack holds, by language code,
        /// from the paths of the files in it (<c>translations/es.txt</c> is
        /// <c>es</c>). Anything else in the folder is not a translation.
        /// </summary>
        public static Dictionary<string, string> Files(IEnumerable<string> paths)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths ?? Enumerable.Empty<string>())
            {
                string p = (path ?? "").Replace('\\', '/');
                if (!p.StartsWith(Folder + "/", StringComparison.OrdinalIgnoreCase)) continue;
                string name = p.Substring(Folder.Length + 1);
                if (name.IndexOf('/') >= 0 || !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) continue;
                string code = name.Substring(0, name.Length - Extension.Length);
                if (code.Length > 0 && TextFile.IsKey(code) && !found.ContainsKey(code)) found[code] = path;
            }
            return found;
        }
    }
}
