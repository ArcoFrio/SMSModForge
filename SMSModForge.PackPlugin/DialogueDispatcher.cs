using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Dialogue;
// Note: we deliberately do NOT import GameCreator.Runtime.VisualScripting —
// Trigger / Instruction / Condition references are kept indirect to minimise
// the plugin's DLL dependencies.
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Watches the scene, evaluates dialogue start conditions, and plays
    /// pack-authored dialogues. One dispatcher per CoreGameScene
    /// instance; built dialogues are torn down on scene exit because GC2
    /// dialogue state doesn't survive a scene transition cleanly.
    /// <para/>
    /// Procedure for actually playing a pack dialogue (per tick, per built
    /// dialogue): if all start conditions pass <em>and</em> no other
    /// dialogue is currently playing, we
    /// <list type="number">
    ///   <item>activate the dialogue's parent roomtalk GameObject under
    ///   <c>8_Room_Talk</c>,</item>
    ///   <item>activate the dialogue's own GameObject (so GC2 sees it as
    ///   the live child of the now-active roomtalk),</item>
    ///   <item>call <see cref="Dialogue.Play"/> with this carrier as the
    ///   <see cref="Args.Self"/>.</item>
    /// </list>
    /// Without step 1 GC2 would have a runnable <c>Dialogue</c> sitting
    /// under an inactive parent — the typewriter, the click-to-advance
    /// MonoBehaviours, all dormant — and the player would see nothing.
    /// <para/>
    /// Per-dialogue lifecycle:
    /// <list type="bullet">
    ///   <item>Track <see cref="Dialogue.Current"/> globally — and the
    ///   roomtalk-child polling check that mirrors the host mod's
    ///   <c>dialoguePlayingVanilla</c> — to prevent two dialogues firing
    ///   at once.</item>
    ///   <item>Hook <see cref="Dialogue.EventStartNext"/> /
    ///   <see cref="Dialogue.EventFinishNext"/> so per-node actions and
    ///   the actor/bust visual update run at the right time.</item>
    ///   <item>For dialogues marked <c>disableVanillaTrigger</c>, disable
    ///   the parent roomtalk's vanilla <c>Trigger</c> <em>only while all
    ///   start conditions pass</em>. When the conditions stop matching
    ///   the original enabled state is restored, so a dialogue gated on
    ///   e.g. a save flag doesn't permanently mute the vanilla room.</item>
    /// </list>
    /// </summary>
    public sealed class DialogueDispatcher
    {
        private readonly PackContext _ctx;
        private readonly List<DialogueBuilder.BuiltDialogue> _built = new List<DialogueBuilder.BuiltDialogue>();
        private readonly Dictionary<Dialogue, DialogueBuilder.BuiltDialogue> _byDialogue =
            new Dictionary<Dialogue, DialogueBuilder.BuiltDialogue>();

        public DialogueDispatcher(PackContext ctx) { _ctx = ctx; }

        public void Add(DialogueBuilder.BuiltDialogue built)
        {
            if (built == null) return;
            _built.Add(built);
            _byDialogue[built.Dialogue] = built;
            built.Dialogue.EventStartNext  += id => OnStartNext(built, id);
            built.Dialogue.EventFinishNext += id => OnFinishNext(built, id);
            built.Dialogue.EventFinish     += () => OnDialogueFinished(built);

            if (built.DisableVanillaTrigger && built.RoomTalkParent != null)
            {
                // Snapshot the original enabled state so UpdateVanillaTriggerSuppression
                // can restore it whenever this dialogue's start conditions stop
                // matching. Component lookup is by type name to avoid a hard ref
                // to GC2 VisualScripting.
                built.SuppressedTrigger = FindBehaviourByTypeName(built.RoomTalkParent.gameObject, "Trigger");
                if (built.SuppressedTrigger != null)
                    built.SuppressedWasEnabled = built.SuppressedTrigger.enabled;
            }
        }

        private static Behaviour FindBehaviourByTypeName(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name == typeName && c is Behaviour b) return b;
            }
            return null;
        }

        /// <summary>
        /// F12 diagnostic: for every built dialogue the author flagged with
        /// "Set for condition debugging", log a pass/fail breakdown of its
        /// start conditions — each condition with its params, the current
        /// variable value where applicable, and group nesting — plus the
        /// dialogue-level latch state that decides whether it would fire.
        /// </summary>
        public void DumpConditionDebug()
        {
            foreach (var b in _built)
            {
                if (!b.DebugConditions) continue;
                bool all = ConditionEvaluator.All(b.StartConditions, _ctx.Vars, _ctx.Log, _ctx.PackId);
                bool wouldFire = all && !b.HasPlayed && !IsAnyDialoguePlaying();
                _ctx.Log?.LogInfo("[CondDebug] ── " + _ctx.PackId + "." + b.Key +
                                  " — conditions " + (all ? "PASS" : "FAIL") +
                                  " | HasPlayed=" + b.HasPlayed + " OneShot=" + b.OneShot +
                                  " dialoguePlaying=" + IsAnyDialoguePlaying() +
                                  " → would fire: " + (wouldFire ? "YES" : "no"));
                if (b.StartConditions != null)
                    foreach (var c in b.StartConditions)
                        DumpCondition(c as JObject, "   ");
            }
        }

        private void DumpCondition(JObject c, string indent)
        {
            if (c == null) return;
            string type = (string)c["type"] ?? "?";
            bool negate = (bool?)c["negate"] ?? false;
            bool pass = ConditionEvaluator.Evaluate(c, _ctx.Vars, _ctx.Log, _ctx.PackId);
            string flag = (pass ? "[PASS] " : "[FAIL] ") + (negate ? "NOT " : "");

            // Groups recurse; the group's own PASS/FAIL is the combined verdict.
            if (type == "All" || type == "Any")
            {
                _ctx.Log?.LogInfo("[CondDebug] " + indent + flag + "group " + type);
                if (c["conditions"] is JArray kids)
                    foreach (var k in kids) DumpCondition(k as JObject, indent + "  ");
                return;
            }

            string detail = "";
            if (c["params"] is JObject p)
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var prop in p.Properties())
                    parts.Add(prop.Name + "=" + prop.Value);
                detail = " { " + string.Join(", ", parts.ToArray()) + " }";

                // For variable comparisons, append the live value so the author
                // sees WHY it passed/failed without checking anything else.
                string name = (string)p["name"];
                if (!string.IsNullOrEmpty(name))
                {
                    bool vanilla = string.Equals((string)p["source"], "vanilla",
                                       System.StringComparison.OrdinalIgnoreCase) ||
                                   type.StartsWith("GameVariable");
                    string current;
                    if (vanilla)
                    {
                        object g = GameVariableBridge.Get(name);
                        current = g != null ? g.ToString() : "<not found>";
                    }
                    else
                        current = _ctx.Vars != null ? _ctx.Vars.GetString(name) : "<no store>";
                    detail += " current=" + current;
                }
            }
            _ctx.Log?.LogInfo("[CondDebug] " + indent + flag + type + detail);
        }

        /// <summary>Called every frame by <see cref="Plugin.Update"/> while in CoreGameScene.</summary>
        public void Tick()
        {
            UpdateVanillaTriggerSuppression();
            // (Sprite-focus enforcement happens in Plugin.LateUpdate — it must
            // run AFTER the vanilla SpriteManager's Update re-sorts the busts.)

            // Edge-trigger pass: refresh the "did conditions pass last
            // tick" snapshot for every built dialogue and reset HasPlayed
            // when conditions just stopped matching. We do this BEFORE
            // the fire loop so even when one dialogue starts this tick
            // the others get their edges latched in correctly.
            UpdateConditionEdges();

            if (IsAnyDialoguePlaying()) return;

            foreach (var built in _built)
            {
                if (built.OneShot && built.HasPlayed) continue;
                if (built.HasPlayed) continue;
                if (built.Dialogue == null) continue;

                // The auto-injected LevelActive start condition is what
                // gates "is the player in the right level?", so the gate
                // *is* the conditions list — no separate roomtalk-active
                // check up front. Activation of the roomtalk happens
                // inside StartDialogue when conditions pass.
                if (!built.LastConditionsPassed) continue;

                StartDialogue(built);
                return; // one per frame is plenty.
            }
        }

        /// <summary>
        /// Per-tick edge detection for each built dialogue's start
        /// conditions. Two transitions matter:
        /// <list type="bullet">
        ///   <item><b>Rising</b> (false→true): leaves <c>HasPlayed</c>
        ///   alone so the fire loop can pick it up. The actual call to
        ///   <see cref="Dialogue.Play"/> happens in the fire loop —
        ///   we don't want to call it twice if multiple dialogues
        ///   transition on the same tick.</item>
        ///   <item><b>Falling</b> (true→false): clears <c>HasPlayed</c>
        ///   for non-<see cref="DialogueBuilder.BuiltDialogue.OneShot"/>
        ///   dialogues so the next rising edge fires again. OneShot
        ///   keeps the latch forever (per save).</item>
        /// </list>
        /// </summary>
        private void UpdateConditionEdges()
        {
            foreach (var b in _built)
            {
                bool nowPassing = ConditionEvaluator.All(b.StartConditions, _ctx.Vars, _ctx.Log, _ctx.PackId);
                if (b.LastConditionsPassed && !nowPassing && !b.OneShot)
                {
                    // Falling edge → arm for the next visit.
                    b.HasPlayed = false;
                }
                b.LastConditionsPassed = nowPassing;
            }
        }

        // ── Lifecycle hooks ────────────────────────────────────────────

        /// <summary>
        /// Number of dialogues that have emitted their start fade but not
        /// yet called <see cref="Dialogue.Play"/> (the 1-second fade
        /// window). Static so the gate + the host mod mirror see
        /// pending starts across every pack's dispatcher — without this,
        /// a second dialogue could slip in during the window because no
        /// Dialogue GO is active yet.
        /// </summary>
        private static int _pendingStarts;

        private void StartDialogue(DialogueBuilder.BuiltDialogue built)
        {
            // Latch + fade first — mirrors the host mod's
            // StartDialogueSequence: emit FadeUI (gameplay UI fades out),
            // then start the actual dialogue one second later so the fade
            // finishes before the speech UI appears.
            // A "queued" dialogue eases in instead: it skips the FadeUI
            // cinematic fade and waits a little longer, mirroring the host's
            // StartDialogueSequenceQueue (no jump-scare on arrival).
            built.HasPlayed = true;
            _ctx.RequestStop = false;
            if (!built.Queued)
                ActionRuntime.EmitSignal("FadeUI", _ctx);

            float delay = built.Queued ? 2.0f : 1.0f;
            if (_ctx.Plugin != null)
            {
                _pendingStarts++;
                _ctx.Plugin.StartCoroutine(PlayAfterFade(built, delay));
            }
            else
            {
                // No coroutine host (shouldn't happen in practice) —
                // start immediately rather than not at all.
                ActivateAndPlay(built);
            }
        }

        private IEnumerator PlayAfterFade(DialogueBuilder.BuiltDialogue built, float delay)
        {
            yield return new WaitForSeconds(delay);
            _pendingStarts--;
            ActivateAndPlay(built);
        }

        private void ActivateAndPlay(DialogueBuilder.BuiltDialogue built)
        {
            try
            {
                // Step 1: activate the parent roomtalk GameObject. GC2's
                // Dialogue.Play won't actually progress (typewriter, UI
                // wiring) when the dialogue's parent is inactiveInHierarchy.
                // Idempotent — SetActive on an already-active GO is a no-op.
                if (built.RoomTalkParent != null && !built.RoomTalkParent.gameObject.activeSelf)
                    built.RoomTalkParent.gameObject.SetActive(true);

                // Step 2: activate the dialogue GO itself.
                if (built.GameObject != null && !built.GameObject.activeSelf)
                    built.GameObject.SetActive(true);

                // Step 3: kick off GC2's async Play. We fire-and-forget
                // (the dispatcher's per-node hooks pick up the lifecycle
                // via EventStartNext/EventFinishNext).
                _ = built.Dialogue.Play(new Args(built.GameObject));
                _ctx.Log.LogInfo("[SMSModForge.PackPlugin] Started dialogue " + _ctx.PackId + "." + built.Key);
            }
            catch (System.Exception ex)
            {
                _ctx.Log.LogError("[SMSModForge.PackPlugin] Dialogue.Play threw for " + built.Key + ": " + ex.Message);
            }
        }

        private void OnStartNext(DialogueBuilder.BuiltDialogue built, int nodeId)
        {
            if (!built.NodeByGc2Id.TryGetValue(nodeId, out var nj)) return;

            // SpeechUI.Current only exists once GC2 has instantiated the
            // speech-skin prefab — and that happens during the first
            // OnStartNext, not before. So we re-apply the pack's actor
            // colours here, every time a node starts: it's a no-op when
            // the list is already up to date, and it catches the moment
            // when the prefab finally appears.
            SpeechColorApplier.Apply(_ctx.ActorFactory, _ctx.Log);

            // Per-node visuals: switch outfit (if the node sets one), then
            // route the bust + expression for the declared speaker. Done
            // first so the visual is up before any OnStart actions read state.
            string actor = (string)nj["actor"];
            string expression = (string)nj["expression"];
            string outfit = (string)nj["outfit"];
            if (!string.IsNullOrEmpty(actor))
                _ctx.Actors.ApplyNodeVisuals(actor, expression, outfit);

            // Pack-authored OnStart actions. Variable writes stay in memory
            // only — they are NOT flushed to disk here. The vanilla game
            // commits progress to the save file solely on the daily autosave
            // (when the player sleeps); a manual save at any other point just
            // copies the last committed file. We mirror that: the in-memory
            // store is the live state and only the daily autosave
            // (Plugin.TickDailyRefresh) writes it out.
            ActionRuntime.ExecuteList(nj["actionsOnStart"] as JArray, _ctx);

            // Auto text-pattern SFX detection — the port of the host mod's
            // OnDialogueLineStart → ProcessSFXTriggersForText. Runs
            // AFTER pack-authored actions so an action that mutates a
            // variable referenced in text via [PV:] / [GV:] takes
            // effect first. Currently the dialogue node text is the
            // raw authored form; in-text placeholder resolution
            // happens in DialogueBuilder, so what we scan here is
            // identical to what the player reads on the line.
            string text = (string)nj["text"];
            if (!string.IsNullOrEmpty(text) && _ctx.Sfx != null && _ctx.Plugin != null)
                _ctx.Sfx.FireMatchingPatterns(text, _ctx.Plugin, _ctx.Log);

            // If an action set RequestStop, end the dialogue immediately.
            if (_ctx.RequestStop)
            {
                _ctx.RequestStop = false;
                built.Dialogue.Stop();
            }
        }

        private void OnFinishNext(DialogueBuilder.BuiltDialogue built, int nodeId)
        {
            if (!built.NodeByGc2Id.TryGetValue(nodeId, out var nj)) return;
            // OnFinish actions — in-memory only, same as OnStart. Disk commit
            // happens exclusively at the daily autosave.
            ActionRuntime.ExecuteList(nj["actionsOnFinish"] as JArray, _ctx);

            if (_ctx.RequestStop)
            {
                _ctx.RequestStop = false;
                built.Dialogue.Stop();
            }
        }

        private void OnDialogueFinished(DialogueBuilder.BuiltDialogue built)
        {
            // FadeUI toggles back — the gameplay UI fades in as the
            // dialogue closes. One emit at start, one at end, mirroring
            // the host mod's EndDialogueSequence. This hook covers both
            // natural completion and EndDialogue-action stops, since
            // Dialogue.Stop also raises EventFinish.
            ActionRuntime.EmitSignal("FadeUI", _ctx);

            // Clear any dialogue-wide sprite focus so the cast returns to its
            // resting sorting orders. Covers both natural completion and
            // EndDialogue-action stops (Dialogue.Stop also raises EventFinish).
            _ctx.Actors?.SetSpriteFocusAll(false);

            // Deactivate the dialogue GO so its visual-listener wiring
            // stops; re-activated next time StartDialogue runs. We leave
            // the parent roomtalk alone — the vanilla level-transition
            // logic manages its activation state when the player walks
            // away, and we don't want to fight it.
            if (built.GameObject != null) built.GameObject.SetActive(false);
        }

        // ── Helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Mirrors <c>the host's dialogue-playing flags</c>
        /// without depending on it: a dialogue is "playing" if GC2's
        /// static <see cref="Dialogue.Current"/> is set OR any child
        /// under <c>8_Room_Talk</c> with a <see cref="Dialogue"/>
        /// component reports itself as active. The second check catches
        /// vanilla dialogues that the game runs through its own static
        /// state without setting <see cref="Dialogue.Current"/>.
        /// <para/>
        /// Exposed as a static helper so <see cref="Plugin"/>'s
        /// per-frame mirror tick can call the same check without
        /// holding a dispatcher reference.
        /// </summary>
        internal static bool IsAnyDialoguePlayingGlobal()
        {
            // A start that's still inside its fade window counts as
            // playing — no Dialogue GO is active yet, but one is committed.
            if (_pendingStarts > 0) return true;
            if (Dialogue.Current != null) return true;
            var roomTalk = GameObject.Find("8_Room_Talk")?.transform;
            if (roomTalk == null) return false;
            foreach (Transform child in roomTalk)
            {
                if (!child.gameObject.activeSelf) continue;
                foreach (Transform d in child)
                {
                    if (!d.gameObject.activeSelf) continue;
                    if (d.GetComponent<Dialogue>() != null) return true;
                }
            }
            return false;
        }

        private bool IsAnyDialoguePlaying()
        {
            // Pending fade-window starts gate other dialogues too.
            if (_pendingStarts > 0) return true;
            if (Dialogue.Current != null) return true;

            // Lightweight: poll active children of 8_Room_Talk for
            // anything that looks like an ongoing dialogue. We only check
            // direct grandchildren since that's where dialogue GOs live.
            var roomTalk = GameObject.Find("8_Room_Talk")?.transform;
            if (roomTalk == null) return false;
            for (int i = 0; i < roomTalk.childCount; i++)
            {
                var rt = roomTalk.GetChild(i);
                if (!rt.gameObject.activeSelf) continue;
                for (int j = 0; j < rt.childCount; j++)
                {
                    var child = rt.GetChild(j);
                    if (!child.gameObject.activeSelf) continue;
                    var d = child.GetComponent<Dialogue>();
                    if (d != null && d.enabled) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Disables the vanilla <c>Trigger</c> on a roomtalk only while
        /// the pack dialogue's <em>full</em> start-condition list passes.
        /// When the conditions stop matching the original enabled state
        /// is restored so the vanilla room behaves normally — important
        /// when a pack dialogue is gated on e.g. a save flag and the
        /// flag isn't set yet: we don't want to suppress the vanilla
        /// dialogue forever, just for the moments our dialogue would
        /// actually take over.
        /// <para/>
        /// Edge case: when several pack dialogues target the same
        /// vanilla roomtalk and more than one has
        /// <c>disableVanillaTrigger=true</c>, the last one evaluated
        /// each tick wins. Authors should keep one suppressor per
        /// roomtalk to avoid surprises.
        /// </summary>
        private void UpdateVanillaTriggerSuppression()
        {
            foreach (var b in _built)
            {
                if (!b.DisableVanillaTrigger || b.SuppressedTrigger == null) continue;
                bool wouldFire = ConditionEvaluator.All(b.StartConditions, _ctx.Vars, _ctx.Log, _ctx.PackId);
                bool shouldEnable = !wouldFire && b.SuppressedWasEnabled;
                if (b.SuppressedTrigger.enabled != shouldEnable)
                    b.SuppressedTrigger.enabled = shouldEnable;
            }
        }

        public void Cleanup()
        {
            // Restore vanilla triggers we touched.
            foreach (var b in _built)
            {
                if (b.SuppressedTrigger != null)
                    b.SuppressedTrigger.enabled = b.SuppressedWasEnabled;
            }
            _built.Clear();
            _byDialogue.Clear();
            // Scene change kills any in-flight PlayAfterFade coroutine
            // before its decrement runs — clear the latch so a stuck
            // counter can't gate every dialogue in the next scene.
            // (Static + idempotent: every dispatcher's Cleanup runs on
            // scene load, all writing the same zero.)
            _pendingStarts = 0;
        }
    }
}
