using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Dialogue;
using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// A new name a pack gave one of the game's characters, in the game's own words
    /// (1.9.0): its lines, choices, quests and screens.
    /// <para/>
    /// Only where a table says the word means that character
    /// (<see cref="SMSModForge.Shared.VanillaNameMentions"/>, built from the game's texts
    /// and reviewed where a name is also a word), and only in a text that still says
    /// exactly what the table was made from - a line a pack rewrote is the pack's, and
    /// one shown in a pack's translation is in other words. Nothing is guessed: a pack's
    /// own Sakura, or "Sakura" typed into any pack's line, is never touched.
    /// <para/>
    /// The game's conversations and screens are made again from the scene at every load,
    /// so they are simply rewritten. Its quests are assets that outlive the scene: their
    /// texts are wrapped, so they read the renames as the journal draws them, and the
    /// wrappers are taken off when the scene goes.
    /// </summary>
    internal static class GameNameMentions
    {
        private const string Tag = "[SMSModForge.PackPlugin] Names in the game's words: ";

        internal sealed class Row
        {
            public string Source;
            public string Where;
            public uint Hash;
            public Span[] Spans;
        }

        internal struct Span
        {
            public int Start;
            public int Length;
            public string Key;
        }

        private static List<Row> _rows;

        private static List<Row> Rows()
        {
            if (_rows != null) return _rows;
            var rows = new List<Row>();
            foreach (string line in SMSModForge.Shared.VanillaNameMentions.Rows)
            {
                var c = line.Split('\t');
                if (c.Length < 4) continue;
                uint hash;
                if (!uint.TryParse(c[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out hash)) continue;
                var spans = new List<Span>();
                foreach (string s in c[3].Split(';'))
                {
                    var p = s.Split(':');
                    int start, length;
                    if (p.Length == 3 && int.TryParse(p[0], out start) && int.TryParse(p[1], out length))
                        spans.Add(new Span { Start = start, Length = length, Key = p[2] });
                }
                rows.Add(new Row { Source = c[0], Where = c[1], Hash = hash, Spans = spans.ToArray() });
            }
            return _rows = rows;
        }

        /// <summary>Whether any of a row's names has a new name now.</summary>
        private static bool Touched(Row row)
        {
            foreach (var s in row.Spans) if (NewNames.NameOf(s.Key) != null) return true;
            return false;
        }

        /// <summary>
        /// <paramref name="text"/> with the row's names given their new names - or null
        /// when the text is not the one the row was made from, or nobody in it has one.
        /// </summary>
        internal static string Renamed(string text, Row row)
        {
            if (string.IsNullOrEmpty(text) || SMSModForge.Shared.VanillaNameMentions.Hash(text) != row.Hash) return null;
            string result = text;
            bool any = false;
            for (int i = row.Spans.Length - 1; i >= 0; i--)
            {
                var s = row.Spans[i];
                string name = NewNames.NameOf(s.Key);
                if (name == null) continue;
                if (s.Start < 0 || s.Start + s.Length > result.Length) return null;
                // "A-Anna" renamed Bella reads "B-Bella". Letter for letter, before
                // the span: the spans to its left stay where they are.
                result = SMSModForge.Shared.NameStutters.Carry(result, s.Start, result.Substring(s.Start, s.Length), name);
                result = result.Substring(0, s.Start) + name + result.Substring(s.Start + s.Length);
                any = true;
            }
            return any ? result : null;
        }

        /// <summary>Every kind of the game's words, once all packs are in.</summary>
        public static void Apply(ManualLogSource log)
        {
            if (NewNames.Count == 0) return;
            int lines = 0, quests = 0, screens = 0, changed = 0;
            var left = new List<string>();
            var conversations = new Dictionary<string, Dialogue>(StringComparer.Ordinal);
            List<Transform> roots = null;

            foreach (var row in Rows())
            {
                if (!Touched(row)) continue;
                try
                {
                    switch (row.Source)
                    {
                        case "line":
                            if (Line(row, conversations)) lines++; else { changed++; Left(left, row); }
                            break;
                        case "quest":
                            if (Quest(row)) quests++; else { changed++; Left(left, row); }
                            break;
                        case "ui":
                            if (roots == null) roots = SceneRoots();
                            if (Screen(row, roots)) screens++; else { changed++; Left(left, row); }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    changed++;
                    log?.LogWarning(Tag + row.Where + ": " + ex.Message);
                }
            }
            log?.LogInfo(Tag + lines + " line(s), " + quests + " quest text(s) and " + screens + " screen text(s) say a new name; "
                         + changed + " left as they were (changed by a pack, translated, or not found)"
                         + (left.Count > 0 ? ": " + string.Join(", ", left.ToArray()) + (changed > left.Count ? ", ..." : "") : "") + ".");
        }

        /// <summary>The first few texts left alone, named in the log.</summary>
        private static void Left(List<string> left, Row row)
        {
            if (left.Count < 5) left.Add(row.Source + " " + row.Where);
        }

        private static bool Line(Row row, Dictionary<string, Dialogue> conversations)
        {
            int hash = row.Where.LastIndexOf('#');
            int id;
            if (hash < 0 || !int.TryParse(row.Where.Substring(hash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                return false;
            string path = row.Where.Substring(0, hash);
            Dialogue dialogue;
            if (!conversations.TryGetValue(path, out dialogue))
            {
                dialogue = VanillaDialogueInjector.Find(path);
                conversations[path] = dialogue;
            }
            var node = NodeOf(dialogue, id);
            string now = node == null ? null : Renamed(VanillaDialogueInjector.RawText(node), row);
            return now != null && VanillaDialogueInjector.SetText(node, now);
        }

        /// <summary>A line of a conversation by its id, asked the way the injector asks:
        /// <c>Content.Get</c>, which throws for an id it does not have. Its
        /// <c>Nodes</c> did not answer for ids it does have (measured, 2026-11-14).</summary>
        private static Node NodeOf(Dialogue dialogue, int id)
        {
            var content = dialogue != null && dialogue.Story != null ? dialogue.Story.Content : null;
            if (content == null) return null;
            try { return content.Get(id); }
            catch (Exception) { return null; }
        }

        // ── Quests ───────────────────────────────────────────────────────

        private struct Wrapped
        {
            public object Target;
            public string Field;
            public PropertyGetString Wrapper;
            public object Inner;
        }

        private static readonly List<Wrapped> _wrapped = new List<Wrapped>();

        private static bool Quest(Row row)
        {
            int hash = row.Where.IndexOf('#');
            if (hash < 0) return false;
            var quest = QuestRegistry.FindVanilla(row.Where.Substring(0, hash));
            if (quest == null) return false;
            string part = row.Where.Substring(hash + 1);

            object target = quest;
            string field;
            if (part == "title") field = "m_Title";
            else if (part == "description") field = "m_Description";
            else if (part.StartsWith("task ", StringComparison.Ordinal))
            {
                int id;
                if (!int.TryParse(part.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return false;
                target = quest.GetTask(id);
                if (target == null) return false;
                field = "m_Name";
            }
            else return false;

            var inner = QuestRegistry.TextOf(target, field) as PropertyGetString;
            if (inner == null) return false;
            // Wrapped already: a load the scene change did not unwrap. Read the
            // original through it rather than wrapping twice.
            foreach (var w in _wrapped)
                if (ReferenceEquals(w.Wrapper, inner)) return true;
            if (Renamed(inner.Get(Args.EMPTY), row) == null) return false;

            var wrapper = new PropertyGetString(new GetStringRenamed(inner, row));
            QuestRegistry.SetText(target, field, wrapper);
            _wrapped.Add(new Wrapped { Target = target, Field = field, Wrapper = wrapper, Inner = inner });
            return true;
        }

        /// <summary>The quests' own texts back, where a wrapper is still in place.</summary>
        public static void Restore()
        {
            foreach (var w in _wrapped)
            {
                try
                {
                    if (w.Target != null && ReferenceEquals(QuestRegistry.TextOf(w.Target, w.Field), w.Wrapper))
                        QuestRegistry.SetText(w.Target, w.Field, w.Inner);
                }
                catch (Exception) { }
            }
            _wrapped.Clear();
        }

        /// <summary>For the bridge: which step found nothing, when <see cref="TextNow"/> did.</summary>
        internal static string WhyNot(string source, string where)
        {
            if (source != "line") return "not found";
            int hash = where.LastIndexOf('#');
            int id;
            if (hash < 0 || !int.TryParse(where.Substring(hash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return "no id";
            var dialogue = VanillaDialogueInjector.Find(where.Substring(0, hash));
            if (dialogue == null) return "no conversation at that path";
            if (dialogue.Story == null || dialogue.Story.Content == null) return "the conversation has no content";
            Node node;
            try { node = dialogue.Story.Content.Get(id); }
            catch (Exception ex) { return "Get(" + id + ") threw " + ex.GetType().Name + ": " + ex.Message; }
            if (node == null) return "Get(" + id + ") gave nothing";
            try { string raw = VanillaDialogueInjector.RawText(node); return "the line's words are " + (raw == null ? "unreadable" : "'" + raw + "'"); }
            catch (Exception ex) { return "its Text threw " + ex.GetType().Name; }
        }

        /// <summary>
        /// What one of the game's texts says now, by the table's source and place -
        /// for the test bridge, which reads it back the way the player would.
        /// </summary>
        internal static string TextNow(string source, string where)
        {
            int hash = where.LastIndexOf('#');
            switch (source)
            {
                case "line":
                {
                    int id;
                    if (hash < 0 || !int.TryParse(where.Substring(hash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return null;
                    var node = NodeOf(VanillaDialogueInjector.Find(where.Substring(0, hash)), id);
                    return node == null ? null : VanillaDialogueInjector.RawText(node);
                }
                case "quest":
                {
                    int first = where.IndexOf('#');
                    var quest = first < 0 ? null : QuestRegistry.FindVanilla(where.Substring(0, first));
                    if (quest == null) return null;
                    string part = where.Substring(first + 1);
                    if (part == "title") return quest.GetTitle(Args.EMPTY);
                    if (part == "description") return (QuestRegistry.TextOf(quest, "m_Description") as PropertyGetString)?.Get(Args.EMPTY);
                    int id;
                    if (part.StartsWith("task ", StringComparison.Ordinal)
                        && int.TryParse(part.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                    {
                        var task = quest.GetTask(id);
                        return task == null ? null : (QuestRegistry.TextOf(task, "m_Name") as PropertyGetString)?.Get(Args.EMPTY);
                    }
                    return null;
                }
                case "ui":
                {
                    int slash = where.IndexOf('/');
                    string root = slash < 0 ? where : where.Substring(0, slash);
                    foreach (var r in SceneRoots())
                    {
                        if (r.name != root) continue;
                        var t = slash < 0 ? r : r.Find(where.Substring(slash + 1));
                        var text = t == null ? null : t.GetComponent<TMP_Text>();
                        if (text != null) return text.text;
                    }
                    return null;
                }
            }
            return null;
        }

        // ── Screens ──────────────────────────────────────────────────────

        private static List<Transform> SceneRoots()
        {
            var roots = new List<Transform>();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
                if (go != null) roots.Add(go.transform);
            return roots;
        }

        private static bool Screen(Row row, List<Transform> roots)
        {
            int slash = row.Where.IndexOf('/');
            string root = slash < 0 ? row.Where : row.Where.Substring(0, slash);
            foreach (var r in roots)
            {
                if (r.name != root) continue;
                var t = slash < 0 ? r : r.Find(row.Where.Substring(slash + 1));
                var text = t == null ? null : t.GetComponent<TMP_Text>();
                string now = text == null ? null : Renamed(text.text, row);
                if (now == null) continue;
                text.text = now;
                return true;
            }
            return false;
        }
    }

    /// <summary>One of the game's quest texts, read through: the game's words, with the
    /// new names packs gave its characters where they still say what they said.</summary>
    [Serializable]
    internal sealed class GetStringRenamed : PropertyTypeGetString
    {
        private readonly PropertyGetString _inner;
        private readonly GameNameMentions.Row _row;

        public GetStringRenamed(PropertyGetString inner, GameNameMentions.Row row)
        {
            _inner = inner;
            _row = row;
        }

        public override string String => _inner != null ? _inner.ToString() : "";
        public override string EditorValue => String;

        public override string Get(Args args)
        {
            string said = _inner != null ? _inner.Get(args) : "";
            return GameNameMentions.Renamed(said, _row) ?? said;
        }

        public override string Get(GameObject gameObject) => Get(Args.EMPTY);
    }
}
