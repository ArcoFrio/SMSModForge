using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Variables;
using GameCreator.Runtime.VisualScripting;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// What the running game holds about each of its global name variables:
    /// every list and its values now, and every place that names one, for the
    /// research behind the editor's variable hints (1.9.0, PLAN-1.9.0.md).
    /// <para/>
    /// Structural, not by title. An instruction's title ("Set Events[x] = True")
    /// is text a title formatter wrote, and a reference with no title, or one
    /// inside a text getter, a quest or a prefab, would not show in a reading
    /// of titles. This walks every script in the scenes, on loaded prefabs and
    /// in every Game Creator asset, field by field - the rule ScriptDump
    /// follows - and wherever an object holds one of the lists, it takes that
    /// object's strings and keeps the ones that are names IN that list. A name
    /// is never guessed: one that cannot be matched is written as "?" with
    /// what was there.
    /// <para/>
    /// Names written in text (a line, a label, a string field) are counted
    /// apart, as mentions: weaker evidence, a word that may only look like a
    /// variable, but the only trace of a variable the game reads by its name
    /// from text.
    /// <para/>
    /// Debug only, like every dump. It reads memory already loaded and writes a
    /// folder beside the log; it changes nothing.
    /// </summary>
    internal static class VariableEvidence
    {
        /// <summary>Deeper than ScriptDump's 24: with a dictionary's pairs
        /// walked through their properties, a dialogue nested in branches
        /// reached 24 in 32 places on the first full run.</summary>
        private const int MaxDepth = 48;
        private const int ExamplesPerName = 4;
        private const int LongestTextScanned = 20000;

        private static readonly Regex Token = new Regex(@"[A-Za-z0-9_\-]+", RegexOptions.Compiled);

        private static readonly Dictionary<Type, FieldInfo[]> FieldsByType = new Dictionary<Type, FieldInfo[]>();

        private sealed class Root
        {
            public string Kind;
            public string Where;
            public string Scene;
            public bool Active;
        }

        private sealed class Mention
        {
            public int Count;
            public readonly List<string> Examples = new List<string>();
        }

        private sealed class Run
        {
            public Dictionary<GlobalNameVariables, HashSet<string>> NamesOf;
            public HashSet<string> AllNames;
            public TextWriter Refs;
            public Dictionary<string, Mention> Mentions = new Dictionary<string, Mention>(StringComparer.Ordinal);
            public int Rows, Unmatched, Truncated, Roots;
            public readonly List<string> TruncatedAt = new List<string>();
        }

        public static string Write(string label, ManualLogSource log)
        {
            var started = DateTime.Now;
            string safe = new string((label ?? "").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            string dir = Path.Combine(BepInEx.Paths.BepInExRootPath,
                "SMSModForge-variables-" + safe + "-" + started.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);

            var run = new Run
            {
                NamesOf = new Dictionary<GlobalNameVariables, HashSet<string>>(),
                AllNames = new HashSet<string>(StringComparer.Ordinal),
            };

            // What is walked must not depend on what happens to be loaded: Game
            // Creator's settings (the dialogue values that fill {M} and {PC}
            // among them) load only once something asks. Two runs differed by
            // exactly those 12 references until everything in Resources was
            // loaded first.
            int loaded = 0;
            try { loaded = Resources.LoadAll<UnityEngine.Object>("").Length; } catch { }
            try { var unused = GameCreator.Runtime.Common.TRepository<GameCreator.Runtime.Dialogue.DialogueRepository>.Get; } catch { }

            // Every list in memory, and each variable's value now. At the main
            // menu that is the value a new game starts with: these assets hold
            // the live values once a save is loaded.
            var lists = Resources.FindObjectsOfTypeAll<GlobalNameVariables>().Where(l => l != null).ToList();
            using (var w = Open(Path.Combine(dir, "lists.tsv")))
            {
                w.WriteLine("list\tunique_id\tsaved\tunnamed_asset\thide_flags\tvariable\ttype\tvalue_now");
                foreach (var list in lists)
                {
                    string[] names = NamesIn(list);
                    string[] types = TypesIn(list, names.Length);
                    run.NamesOf[list] = new HashSet<string>(names, StringComparer.Ordinal);
                    foreach (string n in names) run.AllNames.Add(n);
                    string id = "", saved = "";
                    try { id = list.UniqueID.String; } catch { }
                    try { saved = list.Save ? "yes" : "no"; } catch { }
                    for (int i = 0; i < names.Length; i++)
                    {
                        object value = null;
                        try { value = list.Get(names[i]); } catch { }
                        w.WriteLine(string.Join("\t", Clean(list.name), Clean(id), saved,
                            string.IsNullOrEmpty(list.name) ? "yes" : "no", list.hideFlags.ToString(),
                            Clean(names[i]), Clean(types[i]), Clean(Show(value))));
                    }
                }
            }

            using (var labels = Open(Path.Combine(dir, "texts.tsv")))
            using (run.Refs = Open(Path.Combine(dir, "refs.tsv")))
            {
                run.Refs.WriteLine("variable\tlist\tholder\tcontext\troot_kind\troot\tscene\tactive\tfield_path");
                // The words on the game's screens, so a hint can use what the
                // player reads ("UI_Love" is an object; its label is the words).
                labels.WriteLine("object\tkind\tactive\ttext");

                foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                {
                    if (behaviour == null || MadeByModForge(behaviour.transform)) continue;
                    var root = new Root
                    {
                        Kind = behaviour.gameObject.scene.IsValid() ? "scene" : "prefab",
                        Where = PathOf(behaviour.transform) + " : " + behaviour.GetType().Name,
                        Scene = behaviour.gameObject.scene.IsValid() ? behaviour.gameObject.scene.name : "",
                        Active = behaviour.gameObject.activeInHierarchy,
                    };
                    run.Roots++;
                    // Labels hold nothing but their words: read those, and skip
                    // the rest of the presentation scripts, as ScriptDump does.
                    string words = behaviour is TMPro.TMP_Text tmp ? tmp.text : behaviour is UnityEngine.UI.Text legacy ? legacy.text : null;
                    if (words != null)
                    {
                        Mentions(run, words, root, "text");
                        if (words.Trim().Length > 0)
                            labels.WriteLine(Clean(PathOf(behaviour.transform)) + "\t" + root.Kind + "\t" + (root.Active ? "yes" : "no") + "\t"
                                + Clean(words.Length > 600 ? words.Substring(0, 600) + "…" : words));
                        // A label that is also something else is walked too: Game
                        // Creator's TextPropertyString (the money counter, every
                        // on-screen value) is a uGUI Text that shows a variable. Read
                        // only for its words, the first runs counted nothing that a
                        // screen displays as read (v28's writer, 2026-11-13).
                        if (Presentation(behaviour.GetType())) continue;
                    }
                    if (Presentation(behaviour.GetType())) continue;
                    Walk(run, behaviour, 0, new HashSet<object>(ReferenceComparer.Instance), root, new List<string>(), "");
                }

                foreach (var asset in Resources.FindObjectsOfTypeAll<ScriptableObject>())
                {
                    if (asset == null || asset is TGlobalVariables || Presentation(asset.GetType())) continue;
                    var root = new Root { Kind = "asset", Where = Clean(asset.name) + " : " + asset.GetType().FullName, Scene = "", Active = true };
                    run.Roots++;
                    Walk(run, asset, 0, new HashSet<object>(ReferenceComparer.Instance), root, new List<string>(), "");
                }
            }

            int conversations = WriteStory(dir);
            WriteLists(dir);

            using (var w = Open(Path.Combine(dir, "mentions.tsv")))
            {
                w.WriteLine("variable\tcount\texamples");
                foreach (var kv in run.Mentions.OrderBy(k => k.Key, StringComparer.Ordinal))
                    w.WriteLine(Clean(kv.Key) + "\t" + kv.Value.Count + "\t" + Clean(string.Join(" || ", kv.Value.Examples)));
            }

            string summary = "lists " + lists.Count + ", variables " + run.AllNames.Count + ", conversations " + conversations
                + ", Resources loaded first " + loaded + ", scripts and assets walked "
                + run.Roots + ", references " + run.Rows + " (" + run.Unmatched + " whose name did not match its list), "
                + "names mentioned in text " + run.Mentions.Count + ", walks stopped at depth " + MaxDepth + ": " + run.Truncated
                + ", scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                + ", " + (int)(DateTime.Now - started).TotalSeconds + " s";
            File.WriteAllText(Path.Combine(dir, "summary.txt"), summary + "\n"
                + string.Concat(run.TruncatedAt.Select(t => "stopped deep: " + Clean(t) + "\n")));
            log?.LogInfo("[SMSModForge.PackPlugin] variable evidence: " + summary + " -> " + dir);
            return dir + " (" + summary + ")";
        }

        private static void Walk(Run run, object value, int depth, HashSet<object> seen, Root root, List<string> context, string path)
        {
            if (value == null) return;
            if (value is string s) { Mentions(run, s, root, path); return; }

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum) return;
            if (depth > 0 && value is UnityEngine.Object) return;   // a reference to another object, walked as its own
            if (depth >= MaxDepth)
            {
                // Counted and placed: a walk cut short could hide a reference.
                if (run.Truncated++ < 20) run.TruncatedAt.Add(root.Where + " " + path + " (" + type.Name + ")");
                return;
            }
            if (IsMetadata(type)) return;
            if (!type.IsValueType && !seen.Add(value)) return;

            int before = context.Count;
            if (value is Instruction instruction) context.Add("instruction " + TitleOf(() => instruction.Title, type));
            else if (value is Condition condition) context.Add("condition " + TitleOf(() => condition.Title, type));
            else if (value is GameCreator.Runtime.Dialogue.Node node) context.Add("node \"" + Short(TitleOf(() => node.GetText(Args.EMPTY), type)) + "\"");
            else if (value is GameCreator.Runtime.VisualScripting.Event) context.Add("event " + type.Name);

            try
            {
                if (value is IEnumerable items)
                {
                    int i = 0;
                    foreach (var item in items)
                    {
                        Walk(run, item, depth + 1, seen, root, context, path + "[" + i + "]");
                        i++;
                    }
                    return;
                }

                var fields = SerializedFields(type);
                List<GlobalNameVariables> held = null;
                foreach (var f in fields)
                {
                    object inner;
                    try { inner = f.GetValue(value); } catch { continue; }
                    if (inner is GlobalNameVariables list) (held ?? (held = new List<GlobalNameVariables>())).Add(list);
                }
                if (held != null) Record(run, value, fields, held, root, context, path);

                foreach (var f in fields)
                {
                    object inner;
                    try { inner = f.GetValue(value); } catch { continue; }
                    Walk(run, inner, depth + 1, seen, root, context, path + "." + f.Name);
                }

                // Nothing Unity would serialise, so its state is not in fields:
                // a KeyValuePair keeps Key and Value as properties, which is how
                // a dialogue holds its nodes. ScriptDump's rule, and the first
                // run without it reached no dialogue node at all.
                if (fields.Length == 0)
                    foreach (var p in ReadableProperties(type))
                    {
                        object inner;
                        try { inner = p.GetValue(value); } catch { continue; }
                        Walk(run, inner, depth + 1, seen, root, context, path + "." + p.Name);
                    }
            }
            finally
            {
                if (context.Count > before) context.RemoveRange(before, context.Count - before);
            }
        }

        /// <summary>
        /// Every conversation as Game Creator has it (dialogues\), and every
        /// script by its instructions' titles (scripts.txt): what a reader of
        /// the evidence needs to see what a reference is FOR - the line it sits
        /// on, what the player picked, what the script does next. The same form
        /// as the Gallery's evidence run, so its readers' briefs carry over.
        /// Scripts on prefabs are written too, marked so.
        /// </summary>
        private static int WriteStory(string dir)
        {
            string dialogues = Path.Combine(dir, "dialogues");
            Directory.CreateDirectory(dialogues);
            var done = new HashSet<string>(StringComparer.Ordinal);
            // A line's words as written, before the game fills its gaps: the
            // conversation files show the filled-in line ("stepmom"), which
            // hides that the word is the player's own family word.
            var lineValues = new StringBuilder("conversation\tnode\ttemplate\tvalues\tshown_now\n");
            var scripts = new StringBuilder("# Every script in the scene and on loaded prefabs: object path : component (and its event) : what it does, in order.\n"
                + "# A Conditions component runs the first branch whose conditions all hold. Conversations are in dialogues\\.\n");
            foreach (var c in Resources.FindObjectsOfTypeAll<Component>())
            {
                if (c == null || MadeByModForge(c.transform)) continue;
                bool prefab = !c.gameObject.scene.IsValid();
                string path = PathOf(c.transform) + (prefab ? " (prefab)" : "");
                NoteUnityEvents(c, path, scripts);
                if (c is GameCreator.Runtime.Dialogue.Dialogue d)
                {
                    GameCreator.Runtime.Dialogue.Content content = null;
                    try { content = d.Story?.Content; } catch { }
                    if (content == null || !done.Add(path)) continue;
                    var sb = new StringBuilder("# " + path + " (Game Creator dialogue; active in hierarchy now: " + c.gameObject.activeInHierarchy + ")\n"
                        + "# Each node: #id type \"text\"  if conditions  -> jump; start:/end: the node's actions. Children are indented; a Choice's children are its options.\n");
                    void Nodes(IEnumerable<int> ids, int level)
                    {
                        foreach (int id in ids)
                        {
                            var node = content.Get(id);
                            if (node == null) continue;
                            var conditions = Get<Condition[]>(Get<ConditionList>(Get<object>(node, "m_Conditions"), "m_Conditions"), "m_Conditions");
                            string text = TitleOf(() => node.GetText(Args.EMPTY), typeof(string));
                            // The line as written: {M}, {D} and the rest are the game's
                            // dialogue values (text-tokens.tsv), filled in as it shows.
                            var nodeText = Get<object>(node, "m_Text");
                            var template = Get<PropertyGetString>(nodeText, "m_Text");
                            string written = template == null ? text : TitleOf(() => template.Get(Args.EMPTY), typeof(string));
                            var gaps = Get<Array>(nodeText, "m_Values");
                            var fills = new List<string>();
                            for (int g = 0; gaps != null && g < gaps.Length; g++)
                            {
                                var gap = gaps.GetValue(g);
                                fills.Add("{" + g + "}=" + (gap == null ? "(none)" : TitleOf(() => gap.ToString(), gap.GetType())));
                            }
                            if (written != text || fills.Count > 0)
                                lineValues.Append(Clean(path)).Append('\t').Append(id).Append('\t').Append(Clean(written)).Append('\t')
                                    .Append(Clean(string.Join("; ", fills))).Append('\t').Append(Clean(text)).Append('\n');
                            string pad = new string(' ', level * 2);
                            sb.Append(pad).Append('#').Append(id).Append(' ').Append(node.NodeType?.GetType().Name.Replace("NodeType", "")).Append(" \"").Append(Clean(text)).Append('"');
                            if (conditions != null && conditions.Length > 0)
                                sb.Append("  if ").Append(string.Join(" & ", conditions.Where(x => x != null).Select(x => TitleOf(() => x.Title, x.GetType()))));
                            try
                            {
                                if (node.Jump.Jump != GameCreator.Runtime.Dialogue.JumpType.Continue)
                                    sb.Append("  -> ").Append(node.Jump.Jump).Append(' ').Append(node.Jump.JumpTo.String);
                            }
                            catch { }
                            sb.Append('\n');
                            foreach (string field in new[] { "m_OnStart", "m_OnFinish" })
                                foreach (var ins in Items(Get<InstructionList>(Get<object>(node, field), "m_Instructions")))
                                {
                                    sb.Append(pad).Append("    ").Append(field == "m_OnStart" ? "start: " : "end:   ").Append(Clean(Describe(ins))).Append('\n');
                                    NoteTargets(path, "node #" + id, ins);
                                }
                            Nodes(content.Children(id), level + 1);
                        }
                    }
                    try { Nodes(content.RootIds, 0); } catch (Exception e) { sb.Append("# stopped: " + e.GetType().Name + "\n"); }
                    string file = Regex.Replace(path, @"[^A-Za-z0-9_\-\. ]", "_");
                    if (file.Length > 150) file = file.Substring(0, 150) + "_" + (path.GetHashCode() & 0xffff).ToString("x4");
                    File.WriteAllText(Path.Combine(dialogues, file + ".txt"), sb.ToString(), new UTF8Encoding(false));
                    continue;
                }
                if (c is Conditions conds)
                {
                    scripts.Append(path).Append(" : Conditions\n");
                    var branches = Get<Branch[]>(Get<BranchList>(conds, "m_Branches"), "m_Branches") ?? new Branch[0];
                    for (int b = 0; b < branches.Length; b++)
                    {
                        var when = Get<Condition[]>(Get<ConditionList>(branches[b], "m_ConditionList"), "m_Conditions") ?? new Condition[0];
                        scripts.Append("  branch ").Append(b).Append(": ").Append(string.Join(" AND ", when.Where(x => x != null).Select(x => TitleOf(() => x.Title, x.GetType())))).Append('\n');
                        foreach (var ins in Items(Get<InstructionList>(branches[b], "m_InstructionList")))
                        {
                            scripts.Append("    ").Append(Clean(Describe(ins))).Append('\n');
                            NoteTargets(path + " : Conditions", "branch " + b, ins);
                        }
                    }
                    continue;
                }
                var list = c is BaseActions || c is GameCreator.Runtime.Common.UnityUI.ButtonInstructions ? Get<InstructionList>(c, "m_Instructions") : null;
                if (list == null) continue;
                string ev = c is Trigger ? " (" + (Get<object>(c, "m_TriggerEvent")?.GetType().Name ?? "?") + ")" : "";
                scripts.Append(path).Append(" : ").Append(c.GetType().Name).Append(ev).Append('\n');
                if (c is Trigger)
                {
                    string details = EventDetails(Get<object>(c, "m_TriggerEvent"));
                    if (details.Length > 0) Events.Append(Clean(path)).Append('\t').Append(Clean(ev.Trim(' ', '(', ')'))).Append('\t').Append(Clean(details)).Append('\n');
                }
                foreach (var ins in Items(list))
                {
                    scripts.Append("    ").Append(Clean(Describe(ins))).Append('\n');
                    NoteTargets(path + " : " + c.GetType().Name + ev, "", ins);
                }
            }
            File.WriteAllText(Path.Combine(dir, "scripts.txt"), scripts.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "targets.tsv"), "owner\twhere\tinstruction\tfield\ttarget\n" + Targets, new UTF8Encoding(false));
            Targets.Clear();
            File.WriteAllText(Path.Combine(dir, "trigger-events.tsv"), "object\tevent\tsettings\n" + Events, new UTF8Encoding(false));
            Events.Clear();
            File.WriteAllText(Path.Combine(dir, "line-values.tsv"), lineValues.ToString(), new UTF8Encoding(false));

            // The game's dialogue values: each {key} a line may hold, and what
            // fills it (a variable, a fixed word) - GameTextTokens reads the same list.
            var tokens = new StringBuilder("key\tfilled_with\tshown_now\tbold\titalic\tcoloured\n");
            try
            {
                foreach (var value in GameCreator.Runtime.Common.TRepository<GameCreator.Runtime.Dialogue.DialogueRepository>.Get.Values.Get ?? new GameCreator.Runtime.Dialogue.Value[0])
                {
                    if (value == null) continue;
                    var source = Get<object>(value, "m_Value");
                    tokens.Append(Clean(value.Key)).Append('\t').Append(Clean(source == null ? "" : TitleOf(() => source.ToString(), source.GetType())))
                        .Append('\t').Append(Clean(TitleOf(() => value.GetText(Args.EMPTY), typeof(string))))
                        .Append('\t').Append(value.InBold ? "yes" : "").Append('\t').Append(value.InItalic ? "yes" : "").Append('\t').Append(value.UseColor ? "yes" : "").Append('\n');
                }
            }
            catch (Exception e) { tokens.Append("# not read: " + e.GetType().Name + " " + Clean(e.Message) + "\n"); }
            File.WriteAllText(Path.Combine(dir, "text-tokens.tsv"), tokens.ToString(), new UTF8Encoding(false));
            return done.Count;
        }

        /// <summary>
        /// What each of the game's global LIST variables holds (Random3, Random10...): the
        /// game picks "Random3[Random]" from one, and without the entries a range was read
        /// from the list's name. The same way NameList is read: the list, its Length, Get(i),
        /// each entry's Title.
        /// </summary>
        private static void WriteLists(string dir)
        {
            var sb = new StringBuilder("list\tindex\tentry\n");
            foreach (var asset in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                if (asset == null || asset.GetType().Name != "GlobalListVariables") continue;
                try
                {
                    object list = null;
                    for (var t = asset.GetType(); t != null && list == null; t = t.BaseType)
                        list = t.GetField("m_IndexList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)?.GetValue(asset);
                    var length = list?.GetType().GetProperty("Length")?.GetValue(list) as int?;
                    var get = list?.GetType().GetMethod("Get", new[] { typeof(int) });
                    if (length == null || get == null) { sb.Append(Clean(asset.name)).Append("\t-\t(not readable)\n"); continue; }
                    for (int i = 0; i < length.Value; i++)
                    {
                        var entry = get.Invoke(list, new object[] { i });
                        string title = entry == null ? "" : entry.GetType().GetProperty("Title")?.GetValue(entry) as string ?? entry.ToString();
                        sb.Append(Clean(asset.name)).Append('\t').Append(i).Append('\t').Append(Clean(title)).Append('\n');
                    }
                }
                catch (Exception e) { sb.Append(Clean(asset.name)).Append("\t-\t(" + e.GetType().Name + ")\n"); }
            }
            File.WriteAllText(Path.Combine(dir, "list-variables.tsv"), sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>A trigger's event and what it is set to listen for - the signal of an
        /// EventOnReceiveSignal, the key of an input event - from its own fields.</summary>
        private static string EventDetails(object ev)
        {
            if (ev == null) return "";
            var parts = new List<string>();
            foreach (var f in SerializedFields(ev.GetType()))
            {
                object v;
                try { v = f.GetValue(ev); } catch { continue; }
                if (v == null || v is UnityEngine.Object) continue;
                string s = AsString(v);
                if (s == null && (v.GetType().IsPrimitive || v.GetType().IsEnum)) s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                if (s == null)
                {
                    string shown = TitleOf(() => v.ToString(), v.GetType());
                    if (shown != v.GetType().FullName && shown != v.GetType().Name) s = shown;
                }
                if (!string.IsNullOrEmpty(s)) parts.Add(f.Name.TrimStart('m', '_') + "=" + s);
            }
            return string.Join(", ", parts);
        }

        private static readonly Dictionary<KeyValuePair<Type, string>, FieldInfo> NamedFields = new Dictionary<KeyValuePair<Type, string>, FieldInfo>();

        private static T Get<T>(object owner, string field) where T : class
        {
            if (owner == null) return null;
            var key = new KeyValuePair<Type, string>(owner.GetType(), field);
            if (!NamedFields.TryGetValue(key, out var found))
            {
                for (var t = owner.GetType(); t != null && found == null; t = t.BaseType)
                    found = t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                NamedFields[key] = found;
            }
            try { return found?.GetValue(owner) as T; } catch { return null; }
        }

        private static Instruction[] Items(InstructionList list) => Get<Instruction[]>(list, "m_Instructions") ?? new Instruction[0];

        /// <summary>
        /// Every instruction that names an object - Play a conversation, Run a
        /// component, Set Active - with the object's full path. An instruction's
        /// title prints only the object's NAME ("Play DefaultDialoge"), and 21
        /// conversations share that one, which left the hint writers guessing
        /// which was meant; the instruction itself holds the object.
        /// </summary>
        private static readonly StringBuilder Targets = new StringBuilder();
        private static readonly StringBuilder Events = new StringBuilder();
        private static readonly Dictionary<Type, FieldInfo[]> ObjectFields = new Dictionary<Type, FieldInfo[]>();

        private static void NoteTargets(string owner, string where, Instruction ins)
        {
            if (ins == null) return;
            var type = ins.GetType();
            if (ins is InstructionLogicCallMethod invoke)
                foreach (var call in Calls(Get<UnityEngine.Events.UnityEventBase>(invoke, "m_Method")))
                    Targets.Append(Clean(owner)).Append('\t').Append(Clean(where)).Append('\t').Append(Clean("Invoke Methods: " + call.Value))
                        .Append("\tm_Method\t").Append(Clean(PathOfTarget(call.Key))).Append('\n');
            if (!ObjectFields.TryGetValue(type, out var fields))
            {
                var found = new List<FieldInfo>();
                for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (f.FieldType == typeof(PropertyGetGameObject)) found.Add(f);
                ObjectFields[type] = fields = found.ToArray();
            }
            foreach (var f in fields)
            {
                GameObject target = null;
                try { target = (f.GetValue(ins) as PropertyGetGameObject)?.EditorValue; } catch { }
                Targets.Append(Clean(owner)).Append('\t').Append(Clean(where)).Append('\t').Append(Clean(TitleOf(() => ins.Title, type)))
                    .Append('\t').Append(f.Name).Append('\t').Append(target != null ? Clean(PathOf(target.transform)) : "(worked out at run time)").Append('\n');
            }
        }

        /// <summary>
        /// The calls a Unity event makes - Game Creator's "Invoke Methods" is one,
        /// and so is every button's On Click: its target object, the method and
        /// the argument it was given. Until 1.9.0 the evidence read neither, so
        /// "nothing switches it on" was said of gallery tiles the game does
        /// switch back on (measured: measure-refresh.ps1).
        /// </summary>
        private static List<KeyValuePair<UnityEngine.Object, string>> Calls(UnityEngine.Events.UnityEventBase e)
        {
            var result = new List<KeyValuePair<UnityEngine.Object, string>>();
            if (e == null) return result;
            var calls = Get<System.Collections.IList>(Get<object>(e, "m_PersistentCalls"), "m_Calls");
            int n;
            try { n = e.GetPersistentEventCount(); } catch { return result; }
            for (int i = 0; i < n; i++)
            {
                UnityEngine.Object target = null;
                string method = "?";
                try { target = e.GetPersistentTarget(i); method = e.GetPersistentMethodName(i); } catch { }
                string arg = "";
                var call = calls != null && i < calls.Count ? calls[i] : null;
                if (call != null)
                {
                    string mode = Get<object>(call, "m_Mode")?.ToString() ?? "";
                    var args = Get<object>(call, "m_Arguments");
                    switch (mode)
                    {
                        case "Bool": arg = Convert.ToString(Get<object>(args, "m_BoolArgument")); break;
                        case "Int": arg = Convert.ToString(Get<object>(args, "m_IntArgument")); break;
                        case "Float": arg = Convert.ToString(Get<object>(args, "m_FloatArgument"), System.Globalization.CultureInfo.InvariantCulture); break;
                        case "String": arg = "\"" + Get<string>(args, "m_StringArgument") + "\""; break;
                        case "Object":
                            var o = Get<UnityEngine.Object>(args, "m_ObjectArgument");
                            arg = o == null ? "null" : o is Component oc ? PathOf(oc.transform) : o is GameObject og ? PathOf(og.transform) : o.name;
                            break;
                        case "EventDefined": arg = "(the event's own value)"; break;
                    }
                    if (Get<object>(call, "m_CallState")?.ToString() == "Off") arg += (arg.Length > 0 ? " " : "") + "(switched off)";
                }
                string on = target == null ? "(none)" : target is Component tc ? tc.GetType().Name + " on " + tc.name : target.name;
                result.Add(new KeyValuePair<UnityEngine.Object, string>(target, on + "." + method + "(" + arg + ")"));
            }
            return result;
        }

        private static string PathOfTarget(UnityEngine.Object o)
            => o is Component c ? PathOf(c.transform) : o is GameObject g ? PathOf(g.transform) : o == null ? "(none)" : o.name + " (asset)";

        private static readonly Dictionary<Type, FieldInfo[]> EventFields = new Dictionary<Type, FieldInfo[]>();

        /// <summary>A type's serialized fields holding Unity events, directly or in a
        /// list of entries (an EventTrigger's), up to Unity's own base classes.</summary>
        private static FieldInfo[] EventFieldsOf(Type type)
        {
            if (EventFields.TryGetValue(type, out var known)) return known;
            var found = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Component) && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!f.IsPublic && f.GetCustomAttributes(typeof(SerializeField), true).Length == 0) continue;
                    if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(f.FieldType)) { found.Add(f); continue; }
                    var element = f.FieldType.IsArray ? f.FieldType.GetElementType()
                                : f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>) ? f.FieldType.GetGenericArguments()[0] : null;
                    if (element != null && !typeof(UnityEngine.Object).IsAssignableFrom(element)
                        && element.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                  .Any(x => typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(x.FieldType)))
                        found.Add(f);
                }
            return EventFields[type] = found.ToArray();
        }

        /// <summary>Every call a component's own Unity events make (a Button's On Click,
        /// a Toggle's, an EventTrigger's entries), into scripts.txt and targets.tsv
        /// beside Game Creator's own instructions.</summary>
        private static void NoteUnityEvents(Component c, string path, StringBuilder scripts)
        {
            if (c is Transform) return;
            foreach (var f in EventFieldsOf(c.GetType()))
            {
                object v;
                try { v = f.GetValue(c); } catch { continue; }
                var events = new List<KeyValuePair<string, UnityEngine.Events.UnityEventBase>>();
                if (v is UnityEngine.Events.UnityEventBase single) events.Add(new KeyValuePair<string, UnityEngine.Events.UnityEventBase>(f.Name, single));
                else if (v is System.Collections.IEnumerable entries)
                {
                    int k = 0;
                    foreach (var entry in entries)
                    {
                        if (entry == null) { k++; continue; }
                        string label = f.Name + "[" + k + "]";
                        var id = entry.GetType().GetField("eventID")?.GetValue(entry);
                        if (id != null) label += " " + id;
                        foreach (var ef in entry.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            if (ef.GetValue(entry) is UnityEngine.Events.UnityEventBase inner)
                                events.Add(new KeyValuePair<string, UnityEngine.Events.UnityEventBase>(label, inner));
                        k++;
                    }
                }
                foreach (var pair in events)
                {
                    var calls = Calls(pair.Value);
                    if (calls.Count == 0) continue;
                    string owner = path + " : " + c.GetType().Name + " (" + pair.Key.TrimStart('m', '_') + ")";
                    scripts.Append(owner).Append('\n');
                    foreach (var call in calls)
                    {
                        scripts.Append("    Unity event: ").Append(Clean(call.Value)).Append('\n');
                        Targets.Append(Clean(owner)).Append("\t\t").Append(Clean("Unity event: " + call.Value)).Append('\t').Append(Clean(pair.Key))
                            .Append('\t').Append(Clean(PathOfTarget(call.Key))).Append('\n');
                    }
                }
            }
        }

        /// <summary>An instruction's title, and for "Check N Conditions" the
        /// conditions it checks: its title says only how many, which left
        /// two writers reading the conditions out of refs.tsv by hand. "Invoke
        /// Methods" says what it calls.</summary>
        private static string Describe(Instruction ins)
        {
            string title = TitleOf(() => ins.Title, ins.GetType());
            if (ins is InstructionLogicCallMethod invoke)
            {
                var calls = Calls(Get<UnityEngine.Events.UnityEventBase>(invoke, "m_Method"));
                return calls.Count == 0 ? title + ": (nothing)" : title + ": " + string.Join("; ", calls.Select(x => x.Value));
            }
            if (!(ins is InstructionLogicCheckConditions)) return title;
            var checks = Get<Condition[]>(Get<ConditionList>(ins, "m_Conditions"), "m_Conditions");
            if (checks == null || checks.Length == 0) return title;
            return title + ": " + string.Join(" AND ", checks.Where(c => c != null).Select(c => TitleOf(() => c.Title, c.GetType())));
        }

        /// <summary>One object holding a list: the names it gives that are in
        /// that list, each a reference.</summary>
        private static void Record(Run run, object holder, FieldInfo[] fields, List<GlobalNameVariables> held,
                                   Root root, List<string> context, string path)
        {
            var strings = new List<string>();
            foreach (var f in fields)
            {
                object v;
                try { v = f.GetValue(holder); } catch { continue; }
                string s = AsString(v);
                if (!string.IsNullOrEmpty(s)) strings.Add(s);
            }

            foreach (var list in held)
            {
                if (!run.NamesOf.TryGetValue(list, out var names))
                    run.NamesOf[list] = names = new HashSet<string>(NamesIn(list), StringComparer.Ordinal);
                var matched = strings.Where(names.Contains).Distinct().ToList();
                if (matched.Count == 0)
                {
                    run.Unmatched++;
                    matched.Add("?" + (strings.Count > 0 ? " " + string.Join("|", strings) : ""));
                }
                foreach (string name in matched)
                {
                    run.Refs.WriteLine(string.Join("\t", Clean(name), Clean(list.name), holder.GetType().FullName,
                        Clean(string.Join(" > ", context)), root.Kind, Clean(root.Where), Clean(root.Scene),
                        root.Active ? "yes" : "no", Clean(path)));
                    run.Rows++;
                }
            }
        }

        private static void Mentions(Run run, string text, Root root, string path)
        {
            if (string.IsNullOrEmpty(text)) return;
            string scanned = text.Length > LongestTextScanned ? text.Substring(0, LongestTextScanned) : text;
            foreach (Match m in Token.Matches(scanned))
            {
                if (!run.AllNames.Contains(m.Value)) continue;
                if (!run.Mentions.TryGetValue(m.Value, out var mention)) run.Mentions[m.Value] = mention = new Mention();
                mention.Count++;
                if (mention.Examples.Count >= ExamplesPerName) continue;
                int from = Math.Max(0, m.Index - 40), to = Math.Min(scanned.Length, m.Index + m.Length + 40);
                mention.Examples.Add(root.Kind + " " + root.Where + " " + path + ": " + scanned.Substring(from, to - from));
            }
        }

        private static string[] NamesIn(GlobalNameVariables list)
        {
            try { return list.Names ?? new string[0]; } catch { return new string[0]; }
        }

        /// <summary>Each variable's type, from the list's own type ids.</summary>
        private static string[] TypesIn(GlobalNameVariables list, int count)
        {
            var result = new string[count];
            try
            {
                var nameList = typeof(GlobalNameVariables).GetField("m_NameList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(list);
                var ids = nameList?.GetType().GetProperty("TypeIds")?.GetValue(nameList) as Array;
                for (int i = 0; ids != null && i < count && i < ids.Length; i++) result[i] = AsString(ids.GetValue(i)) ?? "";
            }
            catch { }
            for (int i = 0; i < count; i++) result[i] = result[i] ?? "";
            return result;
        }

        /// <summary>A string, or the string an id carries (IdString, IdPathString).</summary>
        private static string AsString(object value)
        {
            if (value == null) return null;
            if (value is string s) return s;
            try
            {
                var p = value.GetType().GetProperty("String", BindingFlags.Instance | BindingFlags.Public);
                if (p != null && p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0) return p.GetValue(value) as string;
            }
            catch { }
            return null;
        }

        private static FieldInfo[] SerializedFields(Type type)
        {
            if (FieldsByType.TryGetValue(type, out var known)) return known;
            var found = new List<FieldInfo>();
            for (var at = type; at != null && at != typeof(MonoBehaviour) && at != typeof(ScriptableObject) && at != typeof(object); at = at.BaseType)
                foreach (var f in at.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsStatic || f.Name.IndexOf('<') >= 0) continue;
                    if (f.GetCustomAttribute<NonSerializedAttribute>() != null) continue;
                    if (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null || f.GetCustomAttribute<SerializeReference>() != null)
                        found.Add(f);
                }
            return FieldsByType[type] = found.ToArray();
        }

        private static readonly Dictionary<Type, PropertyInfo[]> PropertiesByType = new Dictionary<Type, PropertyInfo[]>();

        private static PropertyInfo[] ReadableProperties(Type type)
        {
            if (PropertiesByType.TryGetValue(type, out var known)) return known;
            return PropertiesByType[type] = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).ToArray();
        }

        private static bool Presentation(Type type)
        {
            string space = type.Namespace;
            if (string.IsNullOrEmpty(space)) return false;
            return space == "UnityEngine.UI" || space.StartsWith("UnityEngine.UI.", StringComparison.Ordinal)
                || space == "TMPro" || space.StartsWith("TMPro.", StringComparison.Ordinal);
        }

        private static bool IsMetadata(Type type)
        {
            if (typeof(MemberInfo).IsAssignableFrom(type) || typeof(Assembly).IsAssignableFrom(type)
                || typeof(Module).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type)) return true;
            string space = type.Namespace;
            return space != null && (space == "System.Reflection" || space.StartsWith("System.Reflection.", StringComparison.Ordinal));
        }

        private static string TitleOf(Func<string> title, Type type)
        {
            try { return title() ?? type.Name; } catch { return type.Name; }
        }

        private static string Show(object value)
        {
            if (value == null) return "";
            if (value is bool b) return b ? "true" : "false";
            if (value is float f) return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (value is UnityEngine.Object o) return o == null ? "" : "(" + o.GetType().Name + ") " + o.name;
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }

        private static string Short(string text) => text == null ? "" : text.Length > 80 ? text.Substring(0, 80) + "…" : text;

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private static TextWriter Open(string file) => new StreamWriter(file, false, new UTF8Encoding(false));

        /// <summary>Whether this object is one ModForge made - a pack's place,
        /// talk or screen, the menu's notice - rather than the game's. Every
        /// object ModForge makes is named SMSModForge-something, on itself or on
        /// what it hangs from; the evidence is about the game alone.</summary>
        private static bool MadeByModForge(Transform t)
        {
            for (var at = t; at != null; at = at.parent)
                if (at.name.StartsWith("SMSModForge", StringComparison.Ordinal)) return true;
            return false;
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var at = t; at != null; at = at.parent) parts.Add(at.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
