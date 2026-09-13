using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Everything the quest journal is, read out of the running game.
    /// <para/>
    /// <b>Why this cannot be done in Unity.</b> Game Creator keeps what a
    /// trigger does, and what a button does when pressed, in
    /// <c>[SerializeReference]</c> graphs — polymorphic managed references. A
    /// ripped project does not carry those: the same UI extraction that reads
    /// this game's screens comes back with 1,819 empty instruction lists and
    /// 756 identical placeholders. An editor-side quest extractor would produce
    /// a beautifully shaped file with the answer missing from it, which is
    /// worse than no file, because it reads as an answer.
    /// <para/>
    /// In the running game the graph has been deserialised from the build and
    /// is sitting in memory, so reflection can walk it. That is what
    /// <see cref="ScriptDump"/> already does for dialogues and for whatever is
    /// on screen; this points the same machinery at one subtree and adds the
    /// two things a script dump does not carry.
    /// <para/>
    /// <b>What a script dump does not carry.</b> It writes one entry per
    /// MonoBehaviour, so an object with no script on it — and the SHAPE of the
    /// tree, which is half of what the journal is — never appears. A
    /// RectTransform is not a MonoBehaviour either. Both are written here.
    /// <para/>
    /// Nothing is switched on, moved or written to. The journal is off at load
    /// and stays off: running its OnEnable is game code that may do anything,
    /// and this is a read.
    /// </summary>
    internal static class QuestJournalDump
    {
        private const string RootName = "9_QuestJournal";

        /// <summary>
        /// The objects the journal is documented to have.
        /// <para/>
        /// Named rather than counted, because a missing one is actionable and a
        /// count is not. This is the SHAPE control: a dump that found the root
        /// and walked one object deep looks identical to a complete one until
        /// something says what was expected.
        /// </summary>
        private static readonly string[] Expected =
        {
            "Core", "Header", "Options", "Button_Active", "Button_Completed",
            "QuestsList", "Scroll", "Viewport", "Content",
            "SelectedQuest", "Title", "Description", "Tasks",
        };

        /// <summary>
        /// Write the journal: its shape, then everything its scripts hold.
        /// <para/>
        /// Two files rather than one. The shape is small and is read first —
        /// it is what answers "is Icon a placeholder" — and the script dump is
        /// large enough that opening it to find an object's name would be the
        /// wrong way round.
        /// </summary>
        public static void Write(ManualLogSource log)
        {
            var root = Find(RootName);
            if (root == null)
            {
                log?.LogWarning("[SMSModForge.PackPlugin] F8: no GameObject named '"
                                + RootName + "' in this scene.");
                return;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string shapeFile = Path.Combine(
                BepInEx.Paths.BepInExRootPath, "SMSModForge-questjournal-shape-" + stamp + ".json");

            var run = new Walk();
            var json = new StringBuilder();
            json.Append("{\n  \"taken\": \"")
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append("\",\n  \"root\": \"").Append(RootName)
                .Append("\",\n  \"objects\": [");
            run.Object(json, root.transform, RootName, true);
            json.Append("\n  ],\n  \"report\": ");
            run.Report(json);
            json.Append("\n}\n");

            File.WriteAllText(shapeFile, json.ToString(), new UTF8Encoding(false));
            log?.LogInfo("[SMSModForge.PackPlugin] F8: " + run.Objects + " object(s), "
                         + run.Components + " component(s) written to " + shapeFile);

            foreach (string line in run.Controls()) log?.LogInfo("[SMSModForge.PackPlugin] F8: " + line);

            string scripts = ScriptDump.WriteUnder(root, log, "questjournal-scripts");
            log?.LogInfo("[SMSModForge.PackPlugin] F8: scripts written to " + scripts);

            // What the first dump showed: this UI holds no quests of its own.
            // QuestListUI reads them from a Journal on 11_PlayerData, and both
            // it and SelectedQuestUI instantiate rows from prefab ASSETS when
            // the screen opens. So a dump of the scene subtree alone describes
            // an empty frame. These three are where the content lives.
            //
            // Each is COUNTED, because "the game has no quests loaded" and "the
            // search never matched" write the same empty file.

            int quests;
            string questFile = ScriptDump.WriteAssetsNamed(
                QuestType, log, "questjournal-quests", out quests);
            log?.LogInfo("[SMSModForge.PackPlugin] F8: " + quests + " quest asset(s) written to " + questFile);
            if (quests == 0)
                log?.LogWarning("[SMSModForge.PackPlugin] F8: no " + QuestType + " assets are loaded. " +
                                "Try again after a quest has been started in this session.");

            int prefabs;
            string prefabFile = ScriptDump.WritePrefabs(
                RowPrefabs, log, "questjournal-prefabs", out prefabs);
            log?.LogInfo("[SMSModForge.PackPlugin] F8: " + prefabs + " script(s) on the row prefabs written to " + prefabFile);
            if (prefabs == 0)
                log?.LogWarning("[SMSModForge.PackPlugin] F8: none of " + string.Join(", ", RowPrefabs) +
                                " were found as loaded prefab assets.");

            var playerData = Find(PlayerDataName);
            int journal;
            string journalFile = ScriptDump.WriteUnderInNamespace(
                playerData, QuestsNamespace, log, "questjournal-state", out journal);
            log?.LogInfo("[SMSModForge.PackPlugin] F8: " + journal + " quest script(s) on " + PlayerDataName +
                         " written to " + journalFile);
            if (playerData == null)
                log?.LogWarning("[SMSModForge.PackPlugin] F8: no GameObject named '" + PlayerDataName + "'.");
        }

        /// <summary>The quest asset type. Matched by name, so the shipped plugin
        /// takes no reference to the Quests assembly for a diagnostic.</summary>
        private const string QuestType = "GameCreator.Runtime.Quests.Quest";

        private const string QuestsNamespace = "GameCreator.Runtime.Quests";

        /// <summary>Where QuestListUI's m_Journal points, read off the first dump
        /// rather than assumed.</summary>
        private const string PlayerDataName = "11_PlayerData";

        /// <summary>The row templates QuestListUI and SelectedQuestUI name in
        /// m_Prefab and m_TaskPrefab. Journal_SubTask is not named by either -
        /// it is presumably named by Journal_Task - and is included so that
        /// presumption is tested rather than relied on.</summary>
        private static readonly string[] RowPrefabs = { "Journal_Quest", "Journal_Task", "Journal_SubTask" };

        /// <summary>
        /// The named root, switched on or not.
        /// <para/>
        /// GameObject.Find skips inactive objects and the journal is inactive
        /// at load — so without this the dump reports the one thing it exists
        /// to read as absent.
        /// </summary>
        private static GameObject Find(string name)
        {
            var direct = GameObject.Find(name);
            if (direct != null) return direct;

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != name) continue;
                if (!go.scene.IsValid()) continue;      // an asset, not this scene
                return go;
            }
            return null;
        }

        // ── One walk ─────────────────────────────────────────────────────

        private sealed class Walk
        {
            public int Objects;
            public int Components;

            private int _texts;
            private int _withScripts;
            private readonly HashSet<string> _found = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<string> _said = new List<string>();

            public void Object(StringBuilder json, Transform t, string path, bool first)
            {
                Objects++;
                _found.Add(t.name);

                if (!first) json.Append(',');
                json.Append("\n    {\"path\": ").Append(Quote(path));
                json.Append(", \"name\": ").Append(Quote(t.name));
                json.Append(", \"active\": ").Append(t.gameObject.activeSelf ? "true" : "false");
                json.Append(", \"index\": ").Append(t.GetSiblingIndex());

                var rect = t as RectTransform;
                if (rect != null)
                {
                    json.Append(", \"rect\": {");
                    json.Append("\"anchoredPosition\": ").Append(V2(rect.anchoredPosition));
                    json.Append(", \"sizeDelta\": ").Append(V2(rect.sizeDelta));
                    json.Append(", \"anchorMin\": ").Append(V2(rect.anchorMin));
                    json.Append(", \"anchorMax\": ").Append(V2(rect.anchorMax));
                    json.Append(", \"pivot\": ").Append(V2(rect.pivot));
                    json.Append('}');
                }

                // Every component, including the ones a script dump leaves out:
                // Transform, RectTransform, and anything else that is not a
                // MonoBehaviour. What each HOLDS is in the script dump; what is
                // ON an object is the question this file answers.
                json.Append(", \"components\": [");
                var components = t.GetComponents<Component>();
                bool anyScript = false;
                for (int i = 0; i < components.Length; i++)
                {
                    if (i > 0) json.Append(", ");
                    var c = components[i];
                    if (c == null)
                    {
                        // Worth writing down rather than skipping: it usually
                        // means an assembly did not load, and an object whose
                        // script is missing will look empty for a reason that
                        // has nothing to do with the game.
                        json.Append("\"<missing script>\"");
                        continue;
                    }
                    Components++;
                    if (c is MonoBehaviour) anyScript = true;
                    json.Append(Quote(c.GetType().FullName));
                }
                json.Append(']');
                if (anyScript) _withScripts++;

                string text = TextOn(t);
                if (text != null)
                {
                    _texts++;
                    json.Append(", \"text\": ").Append(Quote(text));
                    if (_said.Count < 60) _said.Add(path + " = " + text.Replace("\n", "\\n"));
                }

                // A ScrollRect settles what Viewport and Content each are, and
                // it settles it from the game rather than from looking at the
                // names: whichever object it points "viewport" at is the
                // window, and whichever it points "content" at is what moves.
                string scroll = ScrollWiring(t);
                if (scroll != null) json.Append(", \"scrollRect\": ").Append(scroll);

                json.Append('}');

                foreach (Transform child in t)
                    Object(json, child, path + "/" + child.name + Index(child), false);
            }

            /// <summary>A suffix when a name repeats among its siblings. Several
            /// children share one name — Journal_Quest(Clone) is the whole
            /// point — and without this every clone reads as the same path.</summary>
            private static string Index(Transform child)
            {
                var parent = child.parent;
                if (parent == null) return "";

                int sameName = 0, before = 0;
                foreach (Transform sibling in parent)
                {
                    if (sibling.name != child.name) continue;
                    sameName++;
                    if (sibling.GetSiblingIndex() < child.GetSiblingIndex()) before++;
                }
                return sameName > 1 ? "#" + before : "";
            }

            /// <summary>Whatever a component on this object is showing. By
            /// reflection, so UI.Text and TextMeshPro are both read without this
            /// having to reference either.</summary>
            private static string TextOn(Transform t)
            {
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    var property = c.GetType().GetProperty(
                        "text", BindingFlags.Public | BindingFlags.Instance);
                    if (property == null || property.PropertyType != typeof(string)) continue;

                    string said;
                    try { said = property.GetValue(c, null) as string; }
                    catch { continue; }
                    if (!string.IsNullOrEmpty(said)) return said;
                }
                return null;
            }

            private static string ScrollWiring(Transform t)
            {
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null || c.GetType().Name != "ScrollRect") continue;
                    return "{\"viewport\": " + Quote(Named(c, "m_Viewport"))
                           + ", \"content\": " + Quote(Named(c, "m_Content")) + "}";
                }
                return null;
            }

            private static string Named(Component c, string field)
            {
                var f = c.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic
                                                    | BindingFlags.Instance);
                if (f == null) return "<field not found>";

                object v;
                try { v = f.GetValue(c); }
                catch { return "<could not be read>"; }

                var component = v as Component;
                if (component != null) return PathOf(component.transform);
                var o = v as UnityEngine.Object;
                return o == null ? "<none>" : o.name;
            }

            // ── The controls ─────────────────────────────────────────

            /// <summary>
            /// Whether this dump is worth building on.
            /// <para/>
            /// A silent no-op reads exactly like success: a walk that found the
            /// root and read nothing off it writes a well-formed file full of
            /// empty objects. These three say so instead.
            /// </summary>
            public IEnumerable<string> Controls()
            {
                var missing = new List<string>();
                foreach (string name in Expected)
                    if (!_found.Contains(name)) missing.Add(name);

                yield return "control SHAPE   : " + (missing.Count == 0 ? "PASS" : "FAIL")
                             + (missing.Count == 0 ? "" : " — missing " + string.Join(", ", missing.ToArray()));
                yield return "control CONTENT : " + (_texts > 0 ? "PASS" : "FAIL")
                             + " — " + _texts + " object(s) showing text";
                yield return "control SCRIPTS : " + (_withScripts > 0 ? "PASS" : "FAIL")
                             + " — " + _withScripts + " object(s) carrying a script";

                if (_texts == 0 || _withScripts == 0)
                    yield return "this dump read nothing. Do not build on it.";
            }

            public void Report(StringBuilder json)
            {
                json.Append("{\n    \"objects\": ").Append(Objects);
                json.Append(",\n    \"components\": ").Append(Components);
                json.Append(",\n    \"objectsShowingText\": ").Append(_texts);
                json.Append(",\n    \"objectsWithScripts\": ").Append(_withScripts);

                json.Append(",\n    \"controls\": [");
                bool first = true;
                foreach (string line in Controls())
                {
                    if (!first) json.Append(", ");
                    json.Append(Quote(line));
                    first = false;
                }
                json.Append(']');

                json.Append(",\n    \"text\": [");
                for (int i = 0; i < _said.Count; i++)
                {
                    if (i > 0) json.Append(", ");
                    json.Append(Quote(_said[i]));
                }
                json.Append(']');
                json.Append("\n  }");
            }
        }

        // ── Odds and ends ────────────────────────────────────────────────

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var at = t; at != null; at = at.parent) parts.Add(at.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string V2(Vector2 v)
        {
            return "[" + v.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                   + ", " + v.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "]";
        }

        private static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
