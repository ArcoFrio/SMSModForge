using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Dialogue;
using GameCreator.Runtime.VisualScripting;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Constructs a runtime GC2 <see cref="Dialogue"/> MonoBehaviour from
    /// a pack-authored JSON dialogue definition. The build is in two
    /// passes:
    /// <list type="number">
    ///   <item>Allocate one GC2 <see cref="Node"/> per pack node, attach to <see cref="Content"/>, and record the (pack-id → GC2-id) map.</item>
    ///   <item>For each node, link its children (using GC2 ids), set the node type / acting / jump / tag, and register any authored conditions with <see cref="PackNodeConditions"/>.</item>
    /// </list>
    /// Action wiring is <em>not</em> done here — the dispatcher hooks
    /// <see cref="Dialogue.EventStartNext"/> / <see cref="Dialogue.EventFinishNext"/>
    /// and runs them through <see cref="ActionRuntime"/>. That avoids
    /// reflection on <see cref="Node.m_OnStart"/>/<see cref="Node.m_OnFinish"/>
    /// and keeps actions in a single typed pipeline.
    /// </summary>
    public static class DialogueBuilder
    {
        public sealed class BuiltDialogue
        {
            public string PackId;
            public string Key;
            /// <summary>Stay on the Talk button after auto-playing, so the
            /// player can replay it while the conditions still hold.</summary>
            public bool ReplayOnTalk;
            public bool Queued;
            public bool DisableVanillaTrigger;
            /// <summary>Author opted this dialogue into the F12 condition-state dump.</summary>
            public bool DebugConditions;

            /// <summary>
            /// Runtime-only: a queued dialogue whose conditions have passed but
            /// which is waiting for the player to press the vanilla Talk button
            /// (a <c>talkbutton-signal</c> rising edge) before it actually
            /// plays. Set by the dispatcher's fire loop, cleared when it plays
            /// or when its conditions fall.
            /// </summary>
            public bool ArmedForTalk;

            /// <summary>
            /// When true, this dialogue stays latched if its conditions pass
            /// while another dialogue is playing, and starts right after that
            /// one ends. When false (default — the original mod behavior), the
            /// dispatcher marks it <see cref="MissedWindow"/> instead.
            /// </summary>
            public bool QueueBehind;

            /// <summary>Highest wins when several dialogues are eligible on
            /// the same tick; ties fall back to build (manifest) order.</summary>
            public int Priority;

            /// <summary>
            /// Runtime-only: the conditions passed while another dialogue was
            /// playing and this one doesn't <see cref="QueueBehind"/> — the
            /// trigger is spent. Cleared on the falling edge so a fresh
            /// conditions cycle can fire it again. Kept separate from
            /// <see cref="HasPlayed"/> so a trigger isn't permanently
            /// consumed without ever playing.
            /// </summary>
            public bool MissedWindow;
            public Dialogue Dialogue;
            public GameObject GameObject;
            public Transform RoomTalkParent;
            /// <summary>
            /// The vanilla <c>Trigger</c> MonoBehaviour on the parent roomtalk
            /// we may be temporarily disabling. Typed as <see cref="Behaviour"/>
            /// (Unity base, exposes <c>.enabled</c>) so we don't need a hard
            /// reference to <c>GameCreator.Runtime.VisualScripting</c>; the
            /// dispatcher looks it up by component-type-name.
            /// </summary>
            public Behaviour SuppressedTrigger;
            public bool SuppressedWasEnabled;

            public JArray StartConditions;
            public JObject ManifestRoot;            // the raw dialogue JObject

            /// <summary>GC2 node id → pack node json.</summary>
            public Dictionary<int, JObject> NodeByGc2Id = new Dictionary<int, JObject>();

            /// <summary>
            /// Set true once the dispatcher kicks off <c>Play</c>. The
            /// dispatcher uses this together with
            /// <see cref="LastConditionsPassed"/> to fire on the
            /// false→true edge of the conditions: HasPlayed latches at
            /// start, then resets when conditions stop matching (e.g.
            /// the player leaves the level), letting the dialogue
            /// re-trigger on the next visit.
            /// <para/>
            /// Runtime-only and never persisted: every BuiltDialogue is
            /// rebuilt from the manifest on each CoreGameScene load.
            /// Retiring a dialogue for good is a pack-authoring matter —
            /// set a variable on its last node and test that in the
            /// start conditions.
            /// </summary>
            public bool HasPlayed;

            /// <summary>
            /// Snapshot of "did all start conditions pass on the previous
            /// tick?". Used by the dispatcher to detect rising / falling
            /// edges without each tick having to re-derive prior state.
            /// </summary>
            public bool LastConditionsPassed;
        }

        // Reflection handles for Node's private fields. Resolved once and
        // cached — GC2 doesn't move these around between versions, but the
        // up-front lookup makes the per-node loop a tight set of field
        // writes rather than a string-keyed dispatch each time.
        private static readonly FieldInfo _fldNodeType   = typeof(Node).GetField("m_NodeType",   BindingFlags.NonPublic | BindingFlags.Instance);
        // No handle to Node.m_Conditions: pack conditions are answered by
        // PackNodeConditions instead of being written into the node, because
        // GC2 evaluates that list on a clone the binding can't survive.
        private static readonly FieldInfo _fldTag        = typeof(Node).GetField("m_Tag",        BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldJump       = typeof(Node).GetField("m_Jump",       BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldActing     = typeof(Node).GetField("m_Acting",     BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldDuration   = typeof(Node).GetField("m_Duration",   BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldTimeout    = typeof(Node).GetField("m_Timeout",    BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldActingActor = typeof(GameCreator.Runtime.Dialogue.Acting).GetField("m_Actor", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Build one dialogue. Returns null on irrecoverable errors
        /// (logged). The created GameObject lives under the provided
        /// roomtalk parent so GC2 sees it as part of the same chain its
        /// vanilla siblings use.
        /// </summary>
        public static BuiltDialogue Build(JObject manifest, PackContext ctx, Transform roomTalkParent,
                                          UnityEngine.ScriptableObject sharedDialogueSkin)
        {
            string key = (string)manifest["key"];
            if (string.IsNullOrEmpty(key))
            {
                ctx.Log.LogError("[SMSModForge.PackPlugin] Dialogue missing key in pack " + ctx.PackId);
                return null;
            }

            // Create the host GameObject under the roomtalk. The GO is left
            // INACTIVE until the dispatcher decides to play this dialogue —
            // we don't want GC2 to do anything before our turn.
            var go = new GameObject(ctx.PackId + "_" + key);
            go.transform.SetParent(roomTalkParent, false);
            go.SetActive(false);

            var dlg = go.AddComponent<Dialogue>();

            // Required: a DialogueSkin asset. We reuse one harvested from a
            // vanilla dialogue at startup — no asset bundles needed.
            if (sharedDialogueSkin != null)
            {
                // dlg.Story.Content.DialogueSkin has a public setter.
                dlg.Story.Content.DialogueSkin = (DialogueSkin)sharedDialogueSkin;
            }
            else
            {
                ctx.Log.LogWarning("[SMSModForge.PackPlugin] No DialogueSkin harvested — dialogue " +
                                   key + " will fail to play unless a vanilla one becomes available.");
            }

            var built = new BuiltDialogue
            {
                PackId = ctx.PackId,
                Key = key,
                ReplayOnTalk = (bool?)manifest["replayOnTalk"] ?? false,
                Queued = (bool?)manifest["queued"] ?? false,
                QueueBehind = (bool?)manifest["queueBehind"] ?? false,
                Priority = (int?)manifest["priority"] ?? 0,
                DebugConditions = (bool?)manifest["debugConditions"] ?? false,
                DisableVanillaTrigger = (bool?)manifest["disableVanillaTrigger"] ?? false,
                Dialogue = dlg,
                GameObject = go,
                RoomTalkParent = roomTalkParent,
                StartConditions = manifest["startConditions"] as JArray,
                ManifestRoot = manifest,
            };

            // Build the node tree.
            var nodes = manifest["nodes"] as JArray;
            if (nodes == null || nodes.Count == 0)
            {
                ctx.Log.LogWarning("[SMSModForge.PackPlugin] Dialogue " + key + " has no nodes — skipping");
                return built;
            }

            // Pass 1: create one GC2 node per pack node and record the id map.
            var packIdToGc2 = new Dictionary<int, int>();
            var packIdToJson = new Dictionary<int, JObject>();
            foreach (var n in nodes)
            {
                var nj = (JObject)n;
                int packId = (int?)nj["id"] ?? 0;
                if (packIdToGc2.ContainsKey(packId)) continue;
                packIdToJson[packId] = nj;
            }

            // Allocate roots first so root ordering is preserved.
            var rootIds = manifest["rootNodeIds"] as JArray;
            var roots = new List<int>();
            if (rootIds != null) foreach (var r in rootIds) roots.Add((int)r);
            // Fallback: if no roots authored, use the first node as a root so
            // the dialogue isn't a no-op.
            if (roots.Count == 0 && packIdToJson.Count > 0)
            {
                foreach (var kv in packIdToJson) { roots.Add(kv.Key); break; }
            }

            var content = dlg.Story.Content;
            foreach (var pid in roots)
            {
                if (!packIdToJson.TryGetValue(pid, out var nj)) continue;
                int gc2 = content.AddToRoot(BuildBareNode(nj, ctx));
                packIdToGc2[pid] = gc2;
                built.NodeByGc2Id[gc2] = nj;
            }

            // BFS children, since AddChild requires the parent to already
            // exist in the tree.
            var queue = new Queue<int>(roots);
            while (queue.Count > 0)
            {
                int parentPackId = queue.Dequeue();
                if (!packIdToJson.TryGetValue(parentPackId, out var parentJson)) continue;
                if (!packIdToGc2.TryGetValue(parentPackId, out var parentGc2)) continue;

                var children = parentJson["children"] as JArray;
                if (children == null) continue;
                foreach (var c in children)
                {
                    int childPackId = (int)c;
                    if (packIdToGc2.ContainsKey(childPackId)) continue; // already added (cycle or shared)
                    if (!packIdToJson.TryGetValue(childPackId, out var childJson)) continue;
                    int childGc2 = content.AddChild(BuildBareNode(childJson, ctx), parentGc2);
                    packIdToGc2[childPackId] = childGc2;
                    built.NodeByGc2Id[childGc2] = childJson;
                    queue.Enqueue(childPackId);
                }
            }

            // Pass 2: per-node fixups that depend on knowing the GC2 ids.
            foreach (var kv in packIdToGc2)
            {
                int packId = kv.Key;
                int gc2 = kv.Value;
                if (!packIdToJson.TryGetValue(packId, out var nj)) continue;
                var node = content.Get(gc2);
                if (node == null) continue;
                FinaliseNode(node, nj, ctx, packIdToGc2);
            }

            return built;
        }

        /// <summary>
        /// Build a Node carrying just the text. The actor is wired in
        /// pass 2 (<see cref="FinaliseNode"/>) via reflection on the
        /// node's private <c>m_Acting</c> field — see
        /// <see cref="RuntimeActorFactory"/> for how the runtime Actor
        /// SO is created. Children / tag / jump / node-type / conditions
        /// are also filled in by <see cref="FinaliseNode"/>
        /// once we know the GC2 ids for parents and siblings.
        /// </summary>
        private static Node BuildBareNode(JObject nj, PackContext ctx)
        {
            string text = ResolvePlaceholders((string)nj["text"] ?? "", ctx);
            return new Node(text);
        }

        // Pack-variable-only substitution token. GC2 globals — including
        // the proxy-variable mirror of the host mod's SaveManager flags —
        // are intentionally NOT handled here: GC2's native `{X}` syntax
        // resolves them at line-display time via the GlobalNameVariables
        // manager, which is the right semantic (the value re-resolves
        // each line, so e.g. mid-session player-name changes show up
        // immediately). Authors should write `{PCName}` rather than
        // `[GV:PCName]` for any GNV — the build-time `[GV:]` path used
        // to exist here, but it bakes the load-time value which is
        // strictly worse for any variable that can change in-session.
        //
        // Pack variables live in <see cref="PackVariableStore"/> and
        // are NOT registered as GC2 globals, so `{X}` can't resolve
        // them. <c>[PV:name]</c> remains the supported way to inline
        // a pack variable. Resolution still happens at build time;
        // for a pack variable that needs to update mid-dialogue,
        // register it as a GC2 GNV through a future bridge and use
        // `{X}` instead.
        /// <summary>
        /// Substitute <c>[PV:name]</c> tokens in a line of dialogue text
        /// against the pack variable store. Shared implementation in
        /// <see cref="TextPlaceholders"/> (button labels resolve the same
        /// syntax live, per Tick).
        /// </summary>
        private static string ResolvePlaceholders(string text, PackContext ctx)
            => TextPlaceholders.Resolve(text, ctx.Vars);

        private static void FinaliseNode(Node node, JObject nj, PackContext ctx, Dictionary<int, int> idMap)
        {
            // Kind → TNodeType
            string kind = (string)nj["kind"] ?? "Text";
            switch (kind)
            {
                case "Choice": _fldNodeType?.SetValue(node, new NodeTypeChoice()); break;
                case "Random": _fldNodeType?.SetValue(node, new NodeTypeRandom()); break;
                // "Text" is the default new NodeTypeText() already on the field.
            }

            // Actor → m_Acting.m_Actor. The GC2 DialogueUI reads the
            // node's Actor via SpeechUI.OnStartText → node.Actor.GetName(args)
            // to render the speaker header above the line. We build one
            // ScriptableObject Actor per pack actor (lazy, cached in
            // ctx.ActorFactory) and stuff it into the slot directly via
            // reflection — both the Acting class and its m_Actor field
            // are now typed (no field-name probing).
            string actorKey = (string)nj["actor"];
            if (!string.IsNullOrEmpty(actorKey) && _fldActing != null && _fldActingActor != null && ctx.ActorFactory != null)
            {
                var actorEntry = ctx.Actors?.GetOrNull(actorKey);
                string displayName = actorEntry?.DisplayName;
                if (string.IsNullOrEmpty(displayName)) displayName = actorKey;

                var runtimeActor = ctx.ActorFactory.GetOrCreate(actorKey, displayName);
                var acting = _fldActing.GetValue(node);
                if (acting != null && runtimeActor != null)
                    _fldActingActor.SetValue(acting, runtimeActor);
            }

            // Tag
            string tag = (string)nj["tag"];
            if (!string.IsNullOrEmpty(tag) && _fldTag != null)
            {
                // IdString takes a string in its constructor; do it via reflection
                // to avoid pulling in the type explicitly.
                var idStringType = _fldTag.FieldType;
                var ctor = idStringType.GetConstructor(new[] { typeof(string) });
                if (ctor != null) _fldTag.SetValue(node, ctor.Invoke(new object[] { tag }));
            }

            // Jump
            var jump = nj["jump"] as JObject;
            if (jump != null && _fldJump != null)
            {
                string mode = (string)jump["mode"] ?? "Continue";
                var jumpType = _fldJump.FieldType;   // GC2 NodeJump struct
                System.Reflection.MethodInfo make = null;
                switch (mode)
                {
                    case "Exit":  make = jumpType.GetMethod("Exit",     BindingFlags.Public | BindingFlags.Static); break;
                    // GC2's tag-jump factory is NodeJump.To(IdString). The older
                    // probes are kept as fallbacks for other GC2 versions.
                    case "Jump":  make = jumpType.GetMethod("To",       BindingFlags.Public | BindingFlags.Static)
                                      ?? jumpType.GetMethod("Jump",    BindingFlags.Public | BindingFlags.Static)
                                      ?? jumpType.GetMethod("JumpTo",  BindingFlags.Public | BindingFlags.Static); break;
                    default:      make = jumpType.GetMethod("Continue", BindingFlags.Public | BindingFlags.Static); break;
                }
                try
                {
                    if (mode == "Jump" && make != null && make.GetParameters().Length == 1)
                    {
                        // Method expects an IdString tag.
                        var idStringType = make.GetParameters()[0].ParameterType;
                        var ctor = idStringType.GetConstructor(new[] { typeof(string) });
                        if (ctor != null)
                            _fldJump.SetValue(node, make.Invoke(null, new[] { ctor.Invoke(new object[] { (string)jump["targetTag"] ?? "" }) }));
                        else
                            ctx.Log.LogWarning("[SMSModForge.PackPlugin] Jump: IdString(string) ctor not found — tag jump not applied.");
                    }
                    else if (make != null && make.GetParameters().Length == 0)
                    {
                        _fldJump.SetValue(node, make.Invoke(null, null));
                    }
                    else
                    {
                        // Loud, not silent: a probe miss here is exactly how a
                        // "jump quietly behaves like Continue" bug looks.
                        ctx.Log.LogWarning("[SMSModForge.PackPlugin] Jump: no matching NodeJump factory for mode '" +
                                           mode + "' — node keeps default Continue flow.");
                    }
                }
                catch (System.Exception ex)
                {
                    ctx.Log.LogWarning("[SMSModForge.PackPlugin] Jump finalisation failed: " + ex.Message);
                }
            }

            // Duration: how a Text line advances. Default / "UntilInteraction"
            // leaves GC2's default (wait for input) — and a fresh `new Node()`
            // already carries that plus a valid 3s m_Timeout, so we only touch
            // the fields when the author picked Timeout.
            string duration = (string)nj["duration"];
            if (!string.IsNullOrEmpty(duration) && duration != "UntilInteraction" && _fldDuration != null)
            {
                try
                {
                    _fldDuration.SetValue(node, System.Enum.Parse(_fldDuration.FieldType, duration));
                    if (duration == "Timeout" && _fldTimeout != null)
                    {
                        float seconds = (float?)nj["timeout"] ?? 3f;
                        var ctor = _fldTimeout.FieldType.GetConstructor(
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                            null, new[] { typeof(float) }, null);
                        if (ctor != null) _fldTimeout.SetValue(node, ctor.Invoke(new object[] { seconds }));
                    }
                }
                catch (System.Exception ex)
                {
                    ctx.Log.LogWarning("[SMSModForge.PackPlugin] Duration finalisation failed: " + ex.Message);
                }
            }

            // Conditions.
            //
            // Registered with PackNodeConditions rather than written into the
            // node's own RunConditionsList. GC2 evaluates that list on a pooled
            // CLONE of each condition, which cannot carry a pack condition's
            // runtime binding — and every fail-open path in that chain then
            // reports "passed", so authored conditions were ignored outright.
            // See PackNodeConditions for the full chain.
            var conditions = nj["conditions"] as JArray;
            if (conditions != null && conditions.Count > 0)
                PackNodeConditions.Register(node, conditions, ctx);
        }
    }
}
