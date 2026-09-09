using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Writes out what the scripts in the game are actually holding, on a key.
    /// <para/>
    /// The reason this exists: Game Creator keeps everything a Trigger does, and
    /// everything a dialogue says, in structures Unity does not serialise field
    /// by field - and that is the part of a ripped project that does not come
    /// back. The UI extraction reads 1819 empty instruction lists and 756
    /// identical placeholders. What a screen does when it opens, or what a line
    /// of dialogue is, cannot be read from the files at all.
    /// <para/>
    /// It can be read from the running game, because that is where the graph is
    /// deserialised from the build and lives in memory. This walks it and writes
    /// it down.
    /// <para/>
    /// A dump names types and values only. It reads memory that is already
    /// loaded and writes a file next to the log; it changes nothing and is off
    /// unless someone presses the key.
    /// </summary>
    internal static class ScriptDump
    {
        /// <summary>
        /// How deep to follow a field before stopping.
        /// <para/>
        /// A dialogue is the deep case, and deeper than it looks: Story,
        /// Content, the node dictionary, a pair, the item, the node, its
        /// instruction list, an instruction, a property, the getter, the value.
        /// </summary>
        private const int MaxDepth = 24;

        /// <summary>
        /// A ceiling on scripts, so a key pressed on a busy scene writes a
        /// large file rather than an unusable one.
        /// <para/>
        /// High enough that a whole-game pass cannot reach it: the 722
        /// dialogues and everything that starts them are both well under this,
        /// and clipping either would look exactly like a game with fewer
        /// conversations in it. MaxObjects is what actually bounds the size.
        /// </summary>
        private const int MaxScripts = 40000;

        /// <summary>
        /// A ceiling on objects written, across the whole dump.
        /// <para/>
        /// Belt and braces beside the object table below. The table already
        /// stops a graph repeating itself; this stops a genuinely enormous one
        /// from filling the disk while nobody is watching.
        /// </summary>
        private const int MaxObjects = 3000000;

        /// <summary>
        /// Dump only the objects carrying a component of this type, wherever
        /// they are in the scene.
        /// <para/>
        /// The everything-dump answers "what is on screen"; this answers "show
        /// me these". For dialogues that is the difference between a file to
        /// search and a file to read.
        /// </summary>
        public static string WriteOnly<T>(ManualLogSource log, string label)
            where T : Component
        {
            var roots = new List<GameObject>();
            foreach (var found in Resources.FindObjectsOfTypeAll<T>())
            {
                if (found == null) continue;
                // Prefabs and other assets are loaded but are not in the scene,
                // and they are not what "in this game right now" means.
                if (!found.gameObject.scene.IsValid()) continue;
                roots.Add(found.gameObject);
            }
            return Write(OnRoots(roots, typeof(T)), log, label);
        }

        /// <summary>
        /// Every script in the scene that points at one of these, whatever
        /// kind of script it is.
        /// <para/>
        /// This is how to find what STARTS something. A dialogue holds only
        /// what it says; the Trigger that plays it, and the conditions that
        /// decide whether it plays at all, live on some other object entirely
        /// and name it. Asking each script whether it mentions one is the only
        /// way round that does not depend on guessing where such a script
        /// sits.
        /// <para/>
        /// A UnityEvent is the blind spot: it stores its targets in a form
        /// that gives up nothing here, so a button wired through the
        /// inspector cannot be found this way and is not reported as missing.
        /// </summary>
        public static string WriteReferencing<T>(ManualLogSource log, string label)
            where T : Component
            => Write(Referencing<T>(log), log, label);

        /// <summary>
        /// Every ASSET of a type, rather than every instance in a scene.
        /// <para/>
        /// Some of what a conversation refers to is not in the scene at all. An
        /// Actor is a ScriptableObject - a file - and a node names one and then
        /// picks an expression on it BY NUMBER, so without the actors
        /// themselves "expression 4" is a fact with no meaning attached. No
        /// pass over the scene can reach them, by construction.
        /// </summary>
        public static string WriteAssets<T>(ManualLogSource log, string label)
            where T : UnityEngine.Object
        {
            var found = new List<UnityEngine.Object>();
            foreach (var asset in Resources.FindObjectsOfTypeAll<T>())
            {
                if (asset == null) continue;

                // Assets only. One living in a scene belongs to another pass,
                // and writing it here would write it twice.
                var placed = asset as Component;
                if (placed != null && placed.gameObject.scene.IsValid()) continue;
                found.Add(asset);
            }
            return Write(found, log, label);
        }

        /// <summary>
        /// Every other script sitting on the same object as one of these, or
        /// on anything above it.
        /// <para/>
        /// The other half of "what starts this". A script that plays a
        /// dialogue by holding a reference to it is found by
        /// <see cref="WriteReferencing{T}"/>; one that starts it by being
        /// switched on beside it holds no reference at all, and can only be
        /// found by looking where it sits.
        /// </summary>
        public static string WriteAround<T>(ManualLogSource log, string label)
            where T : Component
            => Write(Around<T>(log), log, label);

        /// <summary>The scripts on and above each T, each written once however
        /// many of them it sits above.</summary>
        private static IEnumerable<MonoBehaviour> Around<T>(ManualLogSource log)
            where T : Component
        {
            var already = new HashSet<object>(ReferenceComparer.Instance);
            int found = 0, given = 0;

            foreach (var anchor in Resources.FindObjectsOfTypeAll<T>())
            {
                if (anchor == null || !anchor.gameObject.scene.IsValid()) continue;
                found++;

                for (var at = anchor.transform; at != null; at = at.parent)
                {
                    foreach (var behaviour in at.GetComponents<MonoBehaviour>())
                    {
                        if (behaviour == null) continue;
                        if (behaviour is T) continue;          // already written whole
                        if (Presentation(behaviour)) continue;
                        if (!already.Add(behaviour)) continue;

                        given++;
                        yield return behaviour;
                    }
                }
            }

            if (log != null)
                log.LogInfo("[SMSModForge.PackPlugin] around " + found + " anchor(s): "
                            + given + " script(s)");
        }

        /// <summary>Every active object under these roots that carries a
        /// script.</summary>
        public static string Write(IEnumerable<GameObject> roots, ManualLogSource log)
            => Write(OnRoots(roots, null), log, "scripts");

        /// <summary>The scripts on these roots: one type of them, or every
        /// active one that is not presentation.</summary>
        private static IEnumerable<MonoBehaviour> OnRoots(IEnumerable<GameObject> roots, Type only)
        {
            foreach (var root in roots)
            {
                if (root == null) continue;

                var candidates = only == null
                    ? root.GetComponentsInChildren<MonoBehaviour>(false)
                    : root.GetComponents<MonoBehaviour>();

                foreach (var behaviour in candidates)
                {
                    if (behaviour == null) continue;          // a missing script
                    if (only != null && !only.IsInstanceOfType(behaviour)) continue;

                    // Asked for by type, an inactive one still counts: a
                    // dialogue that is not playing is exactly the kind worth
                    // reading.
                    if (only == null && !behaviour.isActiveAndEnabled) continue;
                    if (only == null && Presentation(behaviour)) continue;

                    yield return behaviour;
                }
            }
        }

        /// <summary>Every script in the scene whose own serialised state names
        /// one of the T in it.</summary>
        private static IEnumerable<MonoBehaviour> Referencing<T>(ManualLogSource log)
            where T : Component
        {
            var targets = new HashSet<UnityEngine.Object>();
            foreach (var found in Resources.FindObjectsOfTypeAll<T>())
            {
                if (found == null || !found.gameObject.scene.IsValid()) continue;
                targets.Add(found);
                targets.Add(found.gameObject);   // named either way round
            }
            if (targets.Count == 0) yield break;

            // Counted, because "nothing references a dialogue" and "the search
            // never ran" write the same empty file.
            int examined = 0, skipped = 0, matched = 0;

            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null) continue;
                if (!behaviour.gameObject.scene.IsValid()) { skipped++; continue; }
                if (behaviour is T) continue;                 // already written whole
                if (Presentation(behaviour)) { skipped++; continue; }

                examined++;
                var seen = new HashSet<object>(ReferenceComparer.Instance);
                if (Mentions(behaviour, targets, 0, seen))
                {
                    matched++;
                    yield return behaviour;
                }
            }

            if (log != null)
                log.LogInfo("[SMSModForge.PackPlugin] looked for " + targets.Count
                            + " target(s) in " + examined + " script(s), skipped "
                            + skipped + ", matched " + matched);
        }

        /// <summary>
        /// Whether this object's serialised state reaches one of the targets.
        /// <para/>
        /// The walk stops AT a Unity object rather than going through it, the
        /// same rule the dump itself follows: following one leads out into the
        /// whole scene from anywhere, and what is asked here is only whether
        /// this script holds that reference itself.
        /// </summary>
        private static bool Mentions(object value, HashSet<UnityEngine.Object> targets,
                                     int depth, HashSet<object> seen)
        {
            if (value == null) return false;

            var type = value.GetType();
            if (value is string || type.IsPrimitive || type.IsEnum) return false;

            // Below the top only. At depth 0 the value IS the script being
            // searched, and asking whether it is one of the targets answers a
            // different question entirely - which is how a first run examined
            // 14,170 scripts, walked into none of them, and reported a clean
            // "nothing references a dialogue".
            var unityObject = value as UnityEngine.Object;
            if (depth > 0 && unityObject != null) return targets.Contains(unityObject);

            if (depth >= MaxDepth) return false;
            if (IsMetadata(type)) return false;
            if (!type.IsValueType && !seen.Add(value)) return false;

            var list = value as IEnumerable;
            if (list != null)
            {
                foreach (var item in list)
                    if (Mentions(item, targets, depth + 1, seen)) return true;
                return false;
            }

            for (var at = type; at != null && at != typeof(MonoBehaviour) && at != typeof(object);
                 at = at.BaseType)
            {
                foreach (var f in at.GetFields(BindingFlags.Instance | BindingFlags.Public
                                               | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!Serialized(f)) continue;
                    if (f.Name.IndexOf('<') >= 0) continue;

                    object inner;
                    try { inner = f.GetValue(value); }
                    catch { continue; }

                    if (Mentions(inner, targets, depth + 1, seen)) return true;
                }
            }
            return false;
        }

        private static string Write(IEnumerable<UnityEngine.Object> chosen, ManualLogSource log,
                                    string label)
        {
            string file = System.IO.Path.Combine(
                BepInEx.Paths.BepInExRootPath,
                "SMSModForge-" + label + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");

            int scripts = 0;
            var run = new Run();

            // Straight to the file. Building the whole thing in memory first is
            // what broke this: a StringBuilder has a ceiling, and a dump of
            // every dialogue in the game went past it.
            using (var w = new StreamWriter(file, false, new UTF8Encoding(false)))
            {
                w.Write("{\n  \"taken\": \"");
                w.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                w.Write("\",\n  \"objects\": [");

                foreach (var behaviour in chosen)
                {
                    if (run.Stopped) break;
                    if (behaviour == null) continue;
                    if (scripts >= MaxScripts) { run.Stopped = true; break; }

                    if (scripts > 0) w.Write(',');
                    w.Write("\n    {\"object\": ");

                    var placed = behaviour as Component;
                    if (placed == null)
                    {
                        // An asset is nowhere in the scene, so it is named
                        // rather than placed, and has no siblings to be
                        // indexed among.
                        Quote(w, behaviour.name);
                        w.Write(", \"asset\": true");
                    }
                    else
                    {
                        Quote(w, Path(placed.transform));

                        // Where it sits among its siblings.
                        //
                        // Not decoration: this game plays some conversations by
                        // asking for "the child of Self at index N", where N is
                        // a variable. Without the index there is no way to say
                        // WHICH conversation such a branch reaches - only that
                        // it reaches one of them.
                        w.Write(", \"index\": ");
                        w.Write(placed.transform.GetSiblingIndex());
                    }

                    w.Write(", \"script\": ");
                    Quote(w, behaviour.GetType().FullName);
                    w.Write(", \"fields\": ");

                    // Each script starts its own object table: one dialogue is
                    // a thing to read on its own, and ids reaching across
                    // scripts would make a file nobody can open in pieces.
                    run.Ids.Clear();
                    Fields(w, behaviour, run);

                    w.Write('}');
                    scripts++;
                }

                w.Write("\n  ],\n  \"scripts\": ");
                w.Write(scripts);
                if (run.Stopped)
                    w.Write(",\n  \"note\": \"stopped early - there was more\"");
                w.Write("\n}\n");
            }

            log?.LogInfo("[SMSModForge.PackPlugin] wrote " + scripts + " script(s) to " + file);
            return file;
        }

        /// <summary>What one dump has spent, and what it has already written
        /// down.</summary>
        private sealed class Run
        {
            /// <summary>
            /// Every reference object written so far, and the id it was given.
            /// <para/>
            /// This is what makes the walk both complete and finite. Collapsing
            /// a repeat to "already written" loses it; expanding it every time
            /// makes a shared subgraph multiply until nothing finishes - which
            /// is exactly how a dump of every dialogue ran out of memory.
            /// Writing it once with an id and pointing at it afterwards does
            /// neither, and a cycle is only the same object met again, so it
            /// needs no separate handling at all.
            /// </summary>
            public readonly Dictionary<object, int> Ids =
                new Dictionary<object, int>(ReferenceComparer.Instance);

            public int Objects;
            public bool Stopped;
        }

        /// <summary>
        /// Whether a script is uGUI or TextMeshPro - an Image, a label, a layout
        /// group.
        /// <para/>
        /// Skipped, and only these. They are most of the scripts on any screen
        /// by count, they hold nothing but presentation, and every one is
        /// already in the UI extraction with its rectangle and its colours.
        /// </summary>
        private static bool Presentation(MonoBehaviour behaviour)
        {
            string space = behaviour.GetType().Namespace;
            if (string.IsNullOrEmpty(space)) return false;
            return space == "UnityEngine.UI"
                || space.StartsWith("UnityEngine.UI.", StringComparison.Ordinal)
                || space == "TMPro"
                || space.StartsWith("TMPro.", StringComparison.Ordinal);
        }

        /// <summary>Every field Unity would have serialised, which is the same
        /// rule the UI extraction follows - public unless told otherwise, and
        /// private only when marked. It is the third of those that matters:
        /// [SerializeReference] is how a polymorphic list is stored.</summary>
        private static void Fields(TextWriter w, object target, Run run)
        {
            w.Write('{');
            int written = 0;

            for (var type = target.GetType();
                 type != null && type != typeof(MonoBehaviour) && type != typeof(object);
                 type = type.BaseType)
            {
                foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public
                                                 | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!Serialized(f)) continue;
                    if (f.Name.IndexOf('<') >= 0) continue;   // a property's backing field

                    object value;
                    try { value = f.GetValue(target); }
                    catch { continue; }

                    if (written > 0) w.Write(", ");
                    Quote(w, f.Name);
                    w.Write(": ");
                    Value(w, value, 0, run);
                    written++;
                }
            }
            w.Write('}');
        }

        private static bool Serialized(FieldInfo f)
        {
            if (f.IsStatic) return false;
            if (f.GetCustomAttribute<NonSerializedAttribute>() != null) return false;
            if (f.IsPublic) return true;
            return f.GetCustomAttribute<SerializeField>() != null
                || f.GetCustomAttribute<SerializeReference>() != null;
        }

        private static void Value(TextWriter w, object value, int depth, Run run)
        {
            if (value == null) { w.Write("null"); return; }

            var type = value.GetType();

            if (value is string s) { Quote(w, s); return; }
            if (value is bool b) { w.Write(b ? "true" : "false"); return; }
            if (type.IsEnum) { Quote(w, value.ToString()); return; }
            if (type.IsPrimitive)
            {
                w.Write(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            // A reference to another object in the scene is named, never
            // followed: following one walks the whole game from any starting
            // point, and what is wanted is one script's own settings.
            if (value is UnityEngine.Object unityObject)
            {
                w.Write("{\"$unity\": ");
                Quote(w, unityObject == null ? "(destroyed)" : unityObject.name);
                w.Write(", \"$type\": ");
                Quote(w, type.Name);

                // Where in the scene, when it is in one.
                //
                // A name on its own does not identify anything: this game has
                // 35 dialogue names shared by more than one dialogue, and
                // "Defaultdialogue" alone is 30-odd different conversations. An
                // asset - a clip, a quest, a skin - has no path and gets none.
                var where = Located(unityObject);
                if (where != null)
                {
                    w.Write(", \"$path\": ");
                    Quote(w, Path(where));
                }

                w.Write('}');
                return;
            }

            // A handful of plain values that describe themselves better than
            // their innards do.
            if (value is Guid || value is DateTime || value is TimeSpan
                || value is decimal || value is IntPtr || value is UIntPtr)
            {
                Quote(w, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            // .NET's own metadata is not this game's data.
            //
            // A property handing back a Type opens on to its assembly, its
            // module, its methods, their parameters and their attributes - and
            // an instruction that calls a method by reflection holds exactly
            // that. One extraction spent 2,678,429 truncations in there, and
            // every one of the 24 types it gave up on was runtime metadata.
            // None of it is a line of dialogue.
            if (IsMetadata(type))
            {
                w.Write("{\"$meta\": ");
                Quote(w, value.ToString());
                w.Write(", \"$type\": ");
                Quote(w, type.Name);
                w.Write('}');
                return;
            }

            if (depth >= MaxDepth) { Quote(w, "(" + type.Name + ", too deep)"); return; }

            if (run.Objects >= MaxObjects)
            {
                run.Stopped = true;
                Quote(w, "(budget spent)");
                return;
            }

            // Met before? Point at it. Written once and referenced thereafter -
            // complete, finite, and a cycle needs no rule of its own.
            if (!type.IsValueType)
            {
                int already;
                if (run.Ids.TryGetValue(value, out already))
                {
                    w.Write("{\"$ref\": ");
                    w.Write(already);
                    w.Write('}');
                    return;
                }
            }

            int id = ++run.Objects;
            if (!type.IsValueType) run.Ids[value] = id;

            // A list carries an id the same way an object does, because it is
            // shared the same way an object is.
            //
            // It used to be written as a bare array, which has nowhere to put
            // one - so the id went into the table, nothing ever printed it, and
            // the second node to share a list got a "$ref" to a number that
            // appears nowhere in the file. One extraction of every dialogue
            // left 70,974 of those, 432 in a single conversation, and a lost
            // instruction list reads exactly like a node that never had one.
            if (value is IEnumerable list)
            {
                w.Write("{\"$id\": ");
                w.Write(id);
                w.Write(", \"$items\": [");
                int n = 0;
                foreach (var item in list)
                {
                    if (n > 0) w.Write(", ");
                    Value(w, item, depth + 1, run);
                    n++;
                }
                w.Write("]}");
                return;
            }

            // Anything else is a plain object - a GC2 instruction, a node, a
            // property, a struct - and its own fields are the interesting part.
            // The type goes in beside them because a polymorphic list says what
            // it holds only by the runtime type of each element.
            w.Write("{\"$id\": ");
            w.Write(id);
            w.Write(", \"$type\": ");
            Quote(w, type.Name);
            Title(w, value, type);

            int wrote = 0;
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public
                                             | BindingFlags.NonPublic))
            {
                if (!Serialized(f)) continue;
                if (f.Name.IndexOf('<') >= 0) continue;

                object inner;
                try { inner = f.GetValue(value); }
                catch { continue; }

                w.Write(", ");
                Quote(w, f.Name);
                w.Write(": ");
                Value(w, inner, depth + 1, run);
                wrote++;
            }

            // Nothing came out, so Unity's rule does not describe this type.
            // That is what a plain .NET container looks like from here: a
            // KeyValuePair keeps its Key and Value as PROPERTIES over private
            // fields carrying no Unity attribute, so a dialogue's whole node
            // dictionary dumped as empty pairs.
            if (wrote == 0) Properties(w, value, type, depth, run);

            w.Write('}');
        }

        /// <summary>
        /// What the object says it is, when it says anything.
        /// <para/>
        /// Game Creator gives every instruction and condition a Title that
        /// reads as a sentence - "Set Events_4[Mario-Plan-Agreed] = True AND
        /// True" - because its own editor draws that on the block. It is the
        /// author's description of the step, written by the thing itself, and
        /// it is the only thing to show for a construct nothing here models.
        /// <para/>
        /// Nothing about this is Game Creator's, though: any object with a
        /// readable string Title is asked, and one that has none writes none.
        /// </summary>
        private static void Title(TextWriter w, object value, Type type)
        {
            PropertyInfo prop;
            try
            {
                prop = type.GetProperty("Title", BindingFlags.Instance | BindingFlags.Public);
            }
            catch { return; }

            if (prop == null || !prop.CanRead) return;
            if (prop.PropertyType != typeof(string)) return;
            if (prop.GetIndexParameters().Length > 0) return;

            string title;
            try { title = (string)prop.GetValue(value, null); }
            catch { return; }

            if (string.IsNullOrEmpty(title)) return;

            w.Write(", \"$title\": ");
            Quote(w, title);
        }

        /// <summary>Whether this is the runtime describing itself rather than
        /// the game describing its content.</summary>
        private static bool IsMetadata(Type type)
        {
            if (typeof(MemberInfo).IsAssignableFrom(type)) return true;   // Type, methods, fields
            if (typeof(Assembly).IsAssignableFrom(type)) return true;
            if (typeof(Module).IsAssignableFrom(type)) return true;

            string space = type.Namespace;
            return space != null
                && (space == "System.Reflection"
                    || space.StartsWith("System.Reflection.", StringComparison.Ordinal));
        }

        /// <summary>
        /// The public, readable, argument-less properties of a plain object -
        /// the last resort for a type whose state is not in fields.
        /// <para/>
        /// Each getter is called inside its own try: a property is code, and
        /// code on a type nobody here has seen before can throw.
        /// </summary>
        private static void Properties(TextWriter w, object value, Type type, int depth, Run run)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!prop.CanRead) continue;
                if (prop.GetIndexParameters().Length > 0) continue;   // an indexer is not state

                object inner;
                try { inner = prop.GetValue(value, null); }
                catch { continue; }

                w.Write(", ");
                Quote(w, prop.Name);
                w.Write(": ");
                Value(w, inner, depth + 1, run);
            }
        }

        /// <summary>The transform of a Unity object that is in the scene, or
        /// null for an asset - a clip, a quest, a skin - which is nowhere.</summary>
        private static Transform Located(UnityEngine.Object value)
        {
            var component = value as Component;
            if (component != null)
                return component.gameObject.scene.IsValid() ? component.transform : null;

            var gameObject = value as GameObject;
            if (gameObject != null)
                return gameObject.scene.IsValid() ? gameObject.transform : null;

            return null;
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (var at = t; at != null; at = at.parent) parts.Add(at.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static void Quote(TextWriter w, string value)
        {
            if (value == null) { w.Write("null"); return; }

            w.Write('"');
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') { w.Write('\\'); w.Write(c); }
                else if (c == '\n') w.Write("\\n");
                else if (c == '\r') w.Write("\\r");
                else if (c == '\t') w.Write("\\t");
                else if (c < ' ') w.Write("\\u" + ((int)c).ToString("x4"));
                else w.Write(c);
            }
            w.Write('"');
        }

        /// <summary>Identity, not equality: two GC2 instructions holding the
        /// same values are still two instructions, and treating them as one
        /// would hide half of a list.</summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
