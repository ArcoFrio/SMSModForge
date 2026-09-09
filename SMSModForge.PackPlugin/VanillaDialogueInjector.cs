using BepInEx.Logging;
using GameCreator.Runtime.Dialogue;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Applies a pack's changes to a dialogue the game already has, in place.
    /// <para/>
    /// The opposite of <see cref="DialogueBuilder"/>, which makes a whole new
    /// conversation. Here the conversation exists, belongs to the game, and the
    /// pack has something to say about a few lines of it — so nothing is built,
    /// rebuilt, or copied. The node the game loaded is the node that plays; only
    /// the fields the pack named are written into it.
    /// <para/>
    /// That the writing is FIELD BY FIELD is the whole point. An extension that
    /// wrote back a whole line would re-assert the parts it merely copied, and
    /// the next game update that rewrote one of those parts would be silently
    /// undone by every pack that had ever touched the line. The editor works out
    /// what actually changed at save time (VanillaDialogueDelta) and names it in
    /// the node's "overrides"; this writes those and leaves the rest alone.
    /// <para/>
    /// Node ids are Game Creator's own and stable across restarts, which is what
    /// lets a change bind to a line rather than to a position in a list.
    /// </summary>
    public static class VanillaDialogueInjector
    {
        private static readonly FieldInfo _fldText
            = typeof(Node).GetField("m_Text", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldNodeType
            = typeof(Node).GetField("m_NodeType", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldTag
            = typeof(Node).GetField("m_Tag", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldDuration
            = typeof(Node).GetField("m_Duration", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldTimeout
            = typeof(Node).GetField("m_Timeout", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>What one extension did, for the log and for the tests.</summary>
        public sealed class Applied
        {
            public string Source;
            public int NodesChanged;
            public int FieldsWritten;

            /// <summary>Changes the author made that this cannot apply yet.
            /// Reported rather than swallowed: a skipped change reads exactly
            /// like an applied one from the outside.</summary>
            public int FieldsSkipped;

            public int NodesRemoved;

            /// <summary>Lines the pack added to the game's conversation.</summary>
            public int NodesAdded;
            public int NodesMissing;
            public string Failure;

            public bool Worked => Failure == null;
        }

        /// <summary>
        /// Apply every vanilla dialogue extension in a loaded pack.
        /// <para/>
        /// A dialogue that cannot be found is reported and skipped rather than
        /// throwing: a pack written against a newer game can name a
        /// conversation this build does not have, and one missing extension
        /// must not stop the rest of the pack loading.
        /// </summary>
        public static List<Applied> ApplyAll(JArray dialogues, PackContext ctx)
        {
            var done = new List<Applied>();
            if (dialogues == null || ctx == null) return done;
            var log = ctx.Log;

            foreach (var entry in dialogues)
            {
                if (!(entry is JObject dialogue)) continue;
                string source = (string)dialogue[SMSModForge.Shared.VanillaDialogueKeys.Source];
                if (string.IsNullOrEmpty(source)) continue;   // the pack's own

                var applied = Apply(dialogue, source, ctx);
                done.Add(applied);

                if (!applied.Worked)
                    log?.LogWarning("[SMSModForge.PackPlugin] vanilla dialogue '"
                                    + source + "' not changed: " + applied.Failure);
                else
                {
                    log?.LogInfo("[SMSModForge.PackPlugin] vanilla dialogue '" + source
                                 + "': " + applied.FieldsWritten + " field(s) on "
                                 + applied.NodesChanged + " line(s), "
                                 + applied.NodesRemoved + " removed, "
                                 + applied.NodesAdded + " added");
                    if (applied.FieldsSkipped > 0)
                        log?.LogWarning("[SMSModForge.PackPlugin] vanilla dialogue '" + source
                                        + "': " + applied.FieldsSkipped
                                        + " authored change(s) NOT applied - see above");
                }
            }
            return done;
        }

        private static Applied Apply(JObject dialogue, string source, PackContext ctx)
        {
            var applied = new Applied { Source = source };
            var log = ctx.Log;

            var target = Find(source);
            if (target == null)
            {
                applied.Failure = "no such dialogue in this scene";
                return applied;
            }

            var content = target.Story != null ? target.Story.Content : null;
            if (content == null)
            {
                applied.Failure = "the dialogue has no content";
                return applied;
            }

            // What runs around each line, keyed by the line. Replaces whatever
            // a previous load left, so re-entering the scene does not stack a
            // second copy of every action.
            var running = Running(target, ctx);
            running.Nodes.Clear();

            // Removals first: a line that is going does not need its fields
            // written, and removing before adding keeps a re-used id from
            // colliding with itself.
            var removed = dialogue[SMSModForge.Shared.VanillaDialogueKeys.RemovedNodes] as JArray;
            if (removed != null)
            {
                foreach (var id in removed)
                {
                    if (Remove(content, (int)id)) applied.NodesRemoved++;
                }
            }

            if (dialogue["nodes"] is JArray nodes)
            {
                foreach (var entry in nodes)
                {
                    if (!(entry is JObject node)) continue;
                    int id = (int?)node["id"] ?? 0;

                    var line = Get(content, id);
                    if (line == null)
                    {
                        // A line the pack ADDS. It cannot be placed until its
                        // parent is known, and the parent is whichever line
                        // names it as a child - so it waits for the pass below.
                        applied.NodesMissing++;
                        continue;
                    }

                    int skipped = 0;
                    int written = Write(line, node, log, ref skipped);

                    // Conditions and actions do not go ON the node - Game
                    // Creator evaluates a clone of its condition list, and the
                    // pack's own pipeline is where an action belongs anyway. So
                    // they are answered from the authored JSON instead, exactly
                    // as they are for a dialogue the pack built itself.
                    if (Named(node, "conditions"))
                    {
                        PackNodeConditions.Register(line, node["conditions"] as JArray, ctx);
                        written++;
                    }
                    if (Named(node, "actionsOnStart") || Named(node, "actionsOnFinish")
                        || Named(node, "actor") || Named(node, "expression")
                        || Named(node, "outfit"))
                    {
                        running.Nodes[id] = node;
                        written++;
                    }

                    applied.FieldsSkipped += skipped;
                    if (written <= 0) continue;
                    applied.NodesChanged++;
                    applied.FieldsWritten += written;
                }
            }

            // Arrangement last: a line can only be attached once the line
            // it hangs from has been found, and a line the pack adds only
            // exists because some parent names it.
            if (dialogue["nodes"] is JArray arranging)
            {
                var added = new Dictionary<int, int>();
                foreach (var entry in arranging)
                {
                    var node = entry as JObject;
                    if (node == null || !Named(node, "children")) continue;

                    int id = (int?)node["id"] ?? 0;
                    if (Get(content, id) == null) continue;   // parent not here

                    int before = added.Count;
                    int placed = Arrange(content, id, node["children"] as JArray,
                                         arranging, added, running, ctx);
                    if (placed <= 0) continue;

                    applied.NodesChanged++;
                    applied.FieldsWritten++;
                    applied.NodesAdded += added.Count - before;
                }
                applied.NodesMissing -= added.Count;
                if (applied.NodesMissing < 0) applied.NodesMissing = 0;
            }

            return applied;
        }

        /// <summary>
        /// Make a line's children exactly what the pack says, adding any it
        /// names that the game does not have.
        /// <para/>
        /// The pack stores ids, not positions: a child is a vanilla line the
        /// game already has, or one the pack invented. The second kind is
        /// created here, through the same AddChild the builder uses, and Game
        /// Creator gives it an id of its own - so the pack's id is kept only as
        /// a way of finding it again while this runs.
        /// <para/>
        /// Returns how many entries were resolved, or 0 if the arrangement
        /// could not be written at all.
        /// </summary>
        private static int Arrange(Content content, int parentId, JArray children,
                                   JArray allNodes, Dictionary<int, int> added,
                                   Run running, PackContext ctx)
        {
            if (children == null) return 0;

            var wanted = new List<int>();
            foreach (var entry in children)
            {
                int childId = (int?)entry ?? 0;

                if (Get(content, childId) != null) { wanted.Add(childId); continue; }
                if (added.TryGetValue(childId, out int alreadyMade))
                {
                    wanted.Add(alreadyMade);
                    continue;
                }

                var authored = Find(allNodes, childId);
                if (authored == null)
                {
                    ctx.Log?.LogWarning("[SMSModForge.PackPlugin] vanilla dialogue: line "
                                        + parentId + " names a child " + childId
                                        + " that is neither the game's nor the pack's - skipped");
                    continue;
                }

                int madeId = content.AddChild(new Node((string)authored["text"] ?? ""), parentId);
                wanted.Add(madeId);

                // A new line still gets everything else it was given. Its
                // "overrides" list is empty - it has no vanilla line underneath
                // to override - so it is written whole.
                int ignored = 0;
                var made = Get(content, madeId);
                if (made != null) WriteWhole(made, authored, ctx, ref ignored);

                // And it runs the way one of the pack's own lines runs: its
                // speaker, expression, outfit and actions all come from the
                // pack. Without this a new line appeared and then did nothing -
                // no bust, no actions - because only lines that OVERRIDE
                // something were being listened for.
                added[childId] = madeId;
                running.Nodes[madeId] = authored;
            }

            return SetChildren(content, parentId, wanted, ctx) ? wanted.Count : 0;
        }

        /// <summary>The authored line with this id, or null.</summary>
        private static JObject Find(JArray nodes, int id)
        {
            foreach (var entry in nodes)
            {
                var node = entry as JObject;
                if (node != null && (int?)node["id"] == id) return node;
            }
            return null;
        }

        /// <summary>
        /// Rewrite which lines hang from this one, in order.
        /// <para/>
        /// Straight onto Game Creator's own tree entry, because there is no
        /// public way to reorder or re-parent an existing line - AddChild
        /// appends and nothing takes one back. A shape it does not recognise
        /// stops here rather than half-writing an arrangement.
        /// </summary>
        private static bool SetChildren(Content content, int parentId, List<int> children,
                                        PackContext ctx)
        {
            // Game Creator's own tree, reached through its own API.
            //
            // This was reflection until the compiler was asked what the type
            // actually is: Content.Nodes is public, TreeNodes is an
            // IDictionary<int, TreeNode> (the GENERIC one only, which is why a
            // cast to the non-generic IDictionary quietly failed), and TreeNode
            // is a class - so a change through the indexer sticks rather than
            // being made to a copy.
            var tree = content.Nodes;
            if (tree == null || !tree.ContainsKey(parentId))
            {
                ctx.Log?.LogWarning("[SMSModForge.PackPlugin] vanilla dialogue: line " + parentId
                                    + " is not in the conversation's tree - arrangement NOT applied");
                return false;
            }

            var entry = tree[parentId];
            if (entry?.Children == null)
            {
                ctx.Log?.LogWarning("[SMSModForge.PackPlugin] vanilla dialogue: line " + parentId
                                    + " has no children to arrange - NOT applied");
                return false;
            }

            entry.Children.Clear();
            entry.Children.AddRange(children);

            // Everything now hanging from this line says so.
            foreach (int childId in children)
                if (tree.ContainsKey(childId) && tree[childId] != null)
                    tree[childId].Parent = parentId;

            return true;
        }


        /// <summary>Every field of a line the pack owns outright.</summary>
        private static void WriteWhole(Node target, JObject node, PackContext ctx, ref int skipped)
        {
            SetText(target, (string)node["text"] ?? "");
            SetKind(target, (string)node["kind"]);
            SetTag(target, (string)node["tag"] ?? "");
            SetDuration(target, (string)node["duration"]);
            if (node["jump"] is JObject jump) DialogueNodeWriter.SetJump(target, jump, ctx.Log);

            // The speaker's NAME, which is Game Creator's to draw from the
            // actor on the line - the bust alone left a line showing the right
            // character with nobody's name above them.
            DialogueNodeWriter.SetActor(target, (string)node["actor"], ctx);

            // And presentation from the conversation rather than from this one
            // line. Every line in the game but one says FromSkin; a line built
            // fresh does not, so an added one typed at its own speed.
            DialogueNodeWriter.SetOptions(target, "FromSkin", ctx.Log);
        }

        /// <summary>
        /// Write the named fields, and only those.
        /// <para/>
        /// A node with no "overrides" writes nothing at all. That is deliberate:
        /// the list is what the editor computed as the difference, and a node
        /// arriving without one is a node whose difference nobody worked out —
        /// treating it as "change everything" would be the exact behaviour this
        /// class exists to avoid.
        /// </summary>
        private static int Write(Node target, JObject node, ManualLogSource log,
                                 ref int skipped)
        {
            var overrides = node[SMSModForge.Shared.VanillaDialogueKeys.Overrides] as JArray;
            if (overrides == null || overrides.Count == 0) return 0;

            int written = 0;
            foreach (var name in overrides)
            {
                switch ((string)name)
                {
                    case "text":
                        if (SetText(target, (string)node["text"] ?? "")) written++;
                        break;

                    case "kind":
                        if (SetKind(target, (string)node["kind"])) written++;
                        break;

                    case "tag":
                        if (SetTag(target, (string)node["tag"] ?? "")) written++;
                        break;

                    case "duration":
                        if (SetDuration(target, (string)node["duration"])) written++;
                        break;

                    // Paired with duration: a line set to time out and left on
                    // Game Creator's own three seconds is half a change.
                    case "timeout":
                        if (SetTimeout(target, (float?)node["timeout"] ?? 3f)) written++;
                        break;

                    case "jump":
                        if (DialogueNodeWriter.SetJump(target, node["jump"] as JObject, log))
                            written++;
                        break;

                    // Who speaks, how they look and what they wear all go
                    // through the pack's own actor system as the line plays -
                    // see Fire. A line the game already has keeps the game's
                    // actor on it untouched; anything the pack has to say about
                    // a speaker it says in its own terms, which is the same
                    // path a dialogue the pack wrote itself takes.
                    case "actor":
                    case "expression":
                    case "outfit":
                        break;

                    // Answered elsewhere, from the authored JSON. Named here so
                    // they do not fall through to "cannot be changed".
                    case "conditions":
                    case "actionsOnStart":
                    case "actionsOnFinish":
                        break;

                    // Written after every line has been found - see Arrange.
                    case "children":
                        break;

                    default:
                        // Named but not written. Counted separately and said
                        // out loud, because a change an author made and this
                        // quietly skipped is indistinguishable from one it
                        // applied - which is the one way this could be wrong
                        // and look right.
                        //
                        // Conditions and actions are the notable ones: running
                        // them means the dispatcher hooking a Dialogue the pack
                        // does not own, which it does not do yet.
                        skipped++;
                        log?.LogWarning("[SMSModForge.PackPlugin] vanilla dialogue: '"
                                        + (string)name + "' cannot be changed on an "
                                        + "existing line yet - that change is NOT applied");
                        break;
                }
            }
            return written;
        }

        // ── the writes ───────────────────────────────────────────────

        /// <summary>
        /// Replace what a line says.
        /// <para/>
        /// Built by making a throwaway Node with the new text and taking its
        /// m_Text, rather than assembling Game Creator's own chain by hand -
        /// NodeText over PropertyGetString over GetStringTextArea over
        /// TextAreaField. Its constructor already knows how, and a hand-built
        /// copy would be one refactor away from being subtly different.
        /// </summary>
        private static bool SetText(Node target, string text)
        {
            if (_fldText == null) return false;
            _fldText.SetValue(target, _fldText.GetValue(new Node(text)));
            return true;
        }

        private static bool SetKind(Node target, string kind)
        {
            if (_fldNodeType == null) return false;
            switch (kind)
            {
                case "Choice": _fldNodeType.SetValue(target, new NodeTypeChoice()); return true;
                case "Random": _fldNodeType.SetValue(target, new NodeTypeRandom()); return true;
                case "Text": _fldNodeType.SetValue(target, new NodeTypeText()); return true;
                default: return false;
            }
        }

        private static bool SetTag(Node target, string tag)
        {
            if (_fldTag == null) return false;
            var ctor = _fldTag.FieldType.GetConstructor(new[] { typeof(string) });
            if (ctor == null) return false;
            _fldTag.SetValue(target, ctor.Invoke(new object[] { tag }));
            return true;
        }

        private static bool SetDuration(Node target, string duration)
        {
            if (_fldDuration == null || string.IsNullOrEmpty(duration)) return false;
            try
            {
                _fldDuration.SetValue(target, System.Enum.Parse(_fldDuration.FieldType, duration));
                return true;
            }
            catch (System.ArgumentException) { return false; }
        }

        private static bool SetTimeout(Node target, float seconds)
        {
            if (_fldTimeout == null) return false;
            var ctor = _fldTimeout.FieldType.GetConstructor(new[] { typeof(double) });
            if (ctor != null)
            {
                _fldTimeout.SetValue(target, ctor.Invoke(new object[] { (double)seconds }));
                return true;
            }
            ctor = _fldTimeout.FieldType.GetConstructor(new[] { typeof(float) });
            if (ctor == null) return false;
            _fldTimeout.SetValue(target, ctor.Invoke(new object[] { seconds }));
            return true;
        }

        // ── finding things ───────────────────────────────────────────

        /// <summary>Whether the pack changed this field of this line.</summary>
        private static bool Named(JObject node, string field)
        {
            var overrides = node[SMSModForge.Shared.VanillaDialogueKeys.Overrides] as JArray;
            if (overrides == null) return false;
            foreach (var name in overrides)
                if ((string)name == field) return true;
            return false;
        }

        // ── running a pack's actions on the game's own lines ─────────

        /// <summary>
        /// The pack actions attached to one vanilla conversation.
        /// <para/>
        /// Hooked once per dialogue and kept, rather than hooked per load.
        /// A vanilla Dialogue outlives a pack reload - it is the game's object,
        /// not ours - so subscribing again on every entry to the scene would
        /// run every action twice, then three times.
        /// </summary>
        private sealed class Run
        {
            public readonly Dictionary<int, JObject> Nodes = new Dictionary<int, JObject>();
            public PackContext Ctx;
        }

        private static readonly Dictionary<Dialogue, Run> Hooked = new Dictionary<Dialogue, Run>();

        private static Run Running(Dialogue dialogue, PackContext ctx)
        {
            Run run;
            if (Hooked.TryGetValue(dialogue, out run))
            {
                run.Ctx = ctx;              // a reload brings a new context
                return run;
            }

            run = new Run { Ctx = ctx };
            Hooked[dialogue] = run;

            dialogue.EventStartNext += id => Fire(run, id, "actionsOnStart");
            dialogue.EventFinishNext += id => Fire(run, id, "actionsOnFinish");
            return run;
        }

        private static void Fire(Run run, int nodeId, string key)
        {
            JObject node;
            if (!run.Nodes.TryGetValue(nodeId, out node)) return;

            // Who speaks and how they look, before the line is read - the same
            // order the dispatcher uses for a dialogue the pack built, so an
            // action that reads the bust sees it already switched.
            //
            // Only for a speaker the PACK owns. One of the game's own is set on
            // the line itself when the change is applied, because that is the
            // system it belongs to - see SetActor.
            if (key == "actionsOnStart")
            {
                string actor = (string)node["actor"];
                if (!string.IsNullOrEmpty(actor) && run.Ctx.Actors != null)
                    run.Ctx.Actors.ApplyNodeVisuals(
                        actor,
                        (string)node["expression"],
                        ActionRuntime.Deref((string)node["outfit"] ?? "", run.Ctx));
            }

            ActionRuntime.ExecuteList(node[key] as JArray, run.Ctx);
        }

        /// <summary>The game's own dialogue at this path, or null when this
        /// scene has no such thing.</summary>
        private static Dialogue Find(string source)
        {
            string prefix = SMSModForge.Shared.VanillaDialogueKeys.SourcePrefix;
            string path = source.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
                ? source.Substring(prefix.Length)
                : source;

            // Inactive-aware, because that is the normal state of one of
            // these: a conversation's GameObject is switched off until the
            // room decides to play it, and GameObject.Find sees none of them.
            var found = TransformExtensions.ResolveGameObject(path);
            if (found == null) return null;

            return found.GetComponent<Dialogue>();
        }

        /// <summary>One line by the id Game Creator gave it.</summary>
        private static Node Get(Content content, int id)
        {
            try { return content.Get(id); }
            catch (System.Exception) { return null; }
        }

        private static bool Remove(Content content, int id)
        {
            try { return content.Remove(id); }
            catch (System.Exception) { return false; }
        }
    }
}
