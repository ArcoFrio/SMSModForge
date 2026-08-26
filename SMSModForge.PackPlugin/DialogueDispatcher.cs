using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Dialogue;
// DialogueUI lives in GameCreator.Runtime.Dialogue.dll (already referenced) and
// exposes the static IsOpen / Current flags the playing-check reads.
using GameCreator.Runtime.Dialogue.UnityUI;
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
    /// dialogue): if all start conditions pass <em>and</em> nothing blocks a
    /// start, we activate the dialogue's own GameObject and call
    /// <see cref="Dialogue.Play"/> with it as the <see cref="Args.Self"/>.
    /// <para/>
    /// Pack dialogues live under a per-pack always-active container, not under
    /// the roomtalk they were authored against. Hosting them on the roomtalk
    /// meant playing one had to switch that roomtalk on, and a roomtalk's
    /// Trigger is EventOnEnable — so that ran the whole vanilla room sequence
    /// and its dialogue landed on top of ours about 1.2s later. A roomtalk is
    /// now only ever looked up to suppress its Trigger for "Prioritize this
    /// dialogue over vanilla".
    /// <para/>
    /// Two distinct gates, and the distinction matters:
    /// <list type="bullet">
    ///   <item><see cref="IsAnyDialoguePlaying"/> — a dialogue is on screen.
    ///   Conditions passing against this BURN the dialogue's window
    ///   (<c>MissedWindow</c>), the original mod's semantics.</item>
    ///   <item><see cref="IsStartBlocked"/> — the above plus
    ///   <c>Core[Lock-Game]</c>, which spans a vanilla room sequence and every
    ///   level transfer. This only DEFERS a start. Lock-Game is true on every
    ///   level entry, so treating it as "playing" would burn every dialogue's
    ///   window the moment the player walks in.</item>
    /// </list>
    /// <para/>
    /// Per-dialogue lifecycle:
    /// <list type="bullet">
    ///   <item>Hook <see cref="Dialogue.EventStartNext"/> /
    ///   <see cref="Dialogue.EventFinishNext"/> so per-node actions and
    ///   the actor/bust visual update run at the right time.</item>
    ///   <item>For dialogues marked <c>disableVanillaTrigger</c>, disable
    ///   the vanilla roomtalk's <c>Trigger</c> <em>only while all start
    ///   conditions pass</em>, restoring it (while the roomtalk is inactive,
    ///   so the restore can't fire OnEnable) once they stop.</item>
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

            if (built.DisableVanillaTrigger && built.VanillaRoomTalk != null)
            {
                // Snapshot the original enabled state so UpdateVanillaTriggerSuppression
                // can restore it whenever this dialogue's start conditions stop
                // matching. Component lookup is by type name to avoid a hard ref
                // to GC2 VisualScripting.
                built.SuppressedTrigger = FindBehaviourByTypeName(built.VanillaRoomTalk.gameObject, "Trigger");
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
                bool playing = IsAnyDialoguePlaying();
                bool blocked = IsStartBlocked();
                bool wouldFire = all && !b.HasPlayed && !b.MissedWindow && !blocked;
                _ctx.Log?.LogInfo("[CondDebug] ── " + _ctx.PackId + "." + b.Key +
                                  " — conditions " + (all ? "PASS" : "FAIL") +
                                  " | HasPlayed=" + b.HasPlayed + " ReplayOnTalk=" + b.ReplayOnTalk +
                                  " MissedWindow=" + b.MissedWindow + " QueueBehind=" + b.QueueBehind +
                                  " ArmedForTalk=" + b.ArmedForTalk + " Priority=" + b.Priority +
                                  // dialoguePlaying burns the window; startBlocked only
                                  // defers. Lock-Game is the difference between them.
                                  " dialoguePlaying=" + playing +
                                  " startBlocked=" + blocked +
                                  " lockGame=" + GameVariableBridge.GetBool("Lock-Game") +
                                  " prioritizeOverVanilla=" + b.DisableVanillaTrigger +
                                  " → would fire: " + (wouldFire ? "YES" : "no"));
                if (b.StartConditions != null)
                    foreach (var c in b.StartConditions)
                        DumpCondition(c as JObject, "   ");
            }
        }

        private void DumpCondition(JObject c, string indent)
            => ConditionEvaluator.DumpCondition(c, _ctx, indent);

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

            // Talk-button consume runs BEFORE the "is anything playing?" gate.
            // A deliberate Talk-button click is an explicit request to start an
            // armed dialogue and must NOT be blocked by the vanilla button's
            // own side effect: its onclick does `SetActive Core[stored-talk]`,
            // which briefly activates an inert stored dialogue object and trips
            // IsAnyDialoguePlaying() for ~1s (measured). Gating the consume on
            // that made the click take ~1s to register. Only our OWN in-flight
            // start (the fade-lead wait, _pendingStarts>0) defers the consume —
            // during a real dialogue the Talk button is hidden, so no click
            // arrives to worry about.
            if (_pendingStarts == 0 && TryConsumeTalkButton())
                return;

            if (IsAnyDialoguePlaying())
            {
                // Original-mod window semantics: a dialogue whose conditions
                // pass while another dialogue is playing loses its trigger —
                // unless it opted into "Queue behind active dialogue". Marked
                // here (not in the edge pass) so it also covers the case where
                // both were passing on the same tick and this one lost the
                // race. MissedWindow clears on the falling edge, so a fresh
                // conditions cycle can fire it again.
                foreach (var b in _built)
                    if (!b.QueueBehind && !b.HasPlayed && b.LastConditionsPassed)
                        b.MissedWindow = true;
                return;
            }
            // Take the UI down as soon as a dialogue QUALIFIES, so the level's
            // own fade-in never gets to undo it. Talk-button dialogues are
            // excluded: they wait on a click that may never come, and hiding the
            // UI would hide the button.
            if (HasPendingAutoStart()) BeginUiHoldForPending();

            // NOTE: the vanilla-commitment hold lives in PeekEligible, per
            // candidate — deliberately NOT here. Holding in Tick would be
            // indiscriminate, and doing it alongside the MissedWindow pass
            // above would burn windows during a lead-in that may yet play no
            // dialogue at all (the Conditions component can pick its empty
            // fallback branch).
        }

        /// <summary>
        /// Collect this pack's armed Talk-button set (disarming any whose start
        /// conditions have since fallen) and, on a <c>talkbutton-signal</c>
        /// rising edge, ROTATE to one armed dialogue and start it. Repeat
        /// clicks alternate between every armed dialogue rather than always
        /// replaying the first; Talk dialogues are repeatable (they re-arm on
        /// finish, see <see cref="OnDialogueFinished"/>) so the set never
        /// exhausts within a visit. Returns true if a dialogue was started.
        /// <para/>
        /// Runs ahead of the IsAnyDialoguePlaying gate so the vanilla button's
        /// own stored-talk activation can't delay the click — see the caller.
        /// </summary>
        private bool TryConsumeTalkButton()
        {
            _armedScratch.Clear();
            for (int i = 0; i < _built.Count; i++)
            {
                var b = _built[i];
                if (!b.ArmedForTalk) continue;
                if (!b.LastConditionsPassed) { b.ArmedForTalk = false; continue; }
                _armedScratch.Add(b);
            }
            if (_armedScratch.Count == 0 || !GameVariableBridge.GetBool("talkbutton-signal"))
                return false;

            GameVariableBridge.SetBool("talkbutton-signal", false); // consume the click
            var chosen = _armedScratch[_talkRotation % _armedScratch.Count];
            _talkRotation++;
            chosen.ArmedForTalk = false;
            _ctx.Log.LogInfo("[SMSModForge.PackPlugin] Queued dialogue " + _ctx.PackId + "." +
                             chosen.Key + " played from Talk button (rotation " +
                             _talkRotation + "/" + _armedScratch.Count + " armed).");
            // Play through the SAME path as a non-queued dialogue: emit the
            // fade, let it play out, THEN start the dialogue. This mirrors the
            // vanilla roomtalk (e.g. Kitchen), whose parent Trigger fades on
            // click and starts the dialogue after the fade — so the fade LEADS
            // the dialogue instead of firing glued to it.
            StartDialogue(chosen);
            return true;
        }

        /// <summary>Round-robin cursor for the Talk-button armed set.</summary>
        private int _talkRotation;
        private readonly System.Collections.Generic.List<DialogueBuilder.BuiltDialogue> _armedScratch =
            new System.Collections.Generic.List<DialogueBuilder.BuiltDialogue>();

        /// <summary>
        /// This pack's best fire candidate for the current tick: the highest-
        /// Priority dialogue whose conditions pass and that hasn't played /
        /// missed its window (ties: manifest order — strictly-greater keeps
        /// the earlier one). Null while any dialogue is playing or nothing is
        /// eligible. Selection is split from firing so <see cref="Plugin"/>
        /// can compare candidates ACROSS packs and fire exactly one — without
        /// this, pack load order would trump Priority between packs.
        /// The auto-injected LevelActive start condition is what gates "is the
        /// player in the right level?", so the gate *is* the conditions list.
        /// </summary>
        public DialogueBuilder.BuiltDialogue PeekEligible()
        {
            // Two gates, by kind. A queueBehind dialogue exists to follow the
            // one on screen, so it only waits for the screen to clear — not for
            // Lock-Game or the level-entry hold. That lets it claim the vanilla
            // room's TAIL, the stretch after its dialogue ends but before its
            // branch raises the UI again, so the handover happens while the UI
            // is still down and never flashes.
            bool blocked = IsStartBlocked();
            bool playing = IsAnyDialoguePlaying();
            if (blocked && playing) return null;

            DialogueBuilder.BuiltDialogue winner = null;
            foreach (var built in _built)
            {
                if (built.HasPlayed) continue;
                if (built.Dialogue == null) continue;
                if (built.MissedWindow) continue;
                if (!built.LastConditionsPassed) continue;
                // Same rule PlayAfterFade re-checks with, so what gets nominated
                // and what is allowed to start cannot disagree. Talk-button and
                // queueBehind dialogues wait only for the screen to clear;
                // everything else waits for the full gate.
                if ((built.Queued || built.QueueBehind) ? playing : blocked) continue;
                if (winner == null || built.Priority > winner.Priority) winner = built;
            }
            return winner;
        }

        /// <summary>Fire a candidate returned by <see cref="PeekEligible"/> —
        /// either arming it behind the Talk button (queued) or starting it.</summary>
        public void FireEligible(DialogueBuilder.BuiltDialogue winner)
        {
            if (winner == null) return;

            if (winner.Queued)
            {
                // Don't play — park it behind the Talk button. Latch
                // HasPlayed so the fire loop stops re-selecting it, and
                // clear any stale click so it needs a fresh press. Mirrors
                // the original: a queued dialogue is "stored" and the
                // vanilla button starts it (talkbutton-signal), instead of
                // Dialogue.Play() firing on arrival.
                winner.HasPlayed = true;
                winner.ArmedForTalk = true;
                GameVariableBridge.SetBool("talkbutton-signal", false);
                _ctx.Log.LogInfo("[SMSModForge.PackPlugin] Queued dialogue " + _ctx.PackId + "." +
                                 winner.Key + " armed — waiting for Talk button.");
                return;
            }

            StartDialogue(winner);   // one per frame is plenty.
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
        ///   so the next rising edge fires again, and disarms the Talk
        ///   button — a dialogue is only replayable while the conditions
        ///   that introduced it still hold.</item>
        /// </list>
        /// </summary>
        private void UpdateConditionEdges()
        {
            foreach (var b in _built)
            {
                bool nowPassing = ConditionEvaluator.All(b.StartConditions, _ctx.Vars, _ctx.Log, _ctx.PackId);
                if (b.LastConditionsPassed && !nowPassing)
                {
                    // Falling edge → the player left; a queued dialogue that was
                    // parked behind the Talk button disarms (it re-arms on the
                    // next visit via the rising edge), a missed window re-arms
                    // for the next conditions cycle, and HasPlayed clears so
                    // the dialogue can fire again next time.
                    b.ArmedForTalk = false;
                    b.MissedWindow = false;
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

        /// <summary>Fade-out lead before the dialogue actually starts —
        /// matches the vanilla roomtalk feel (fade on click, dialogue after
        /// the fade). Same value for queued and non-queued: once triggered,
        /// they play identically.</summary>
        private const float FadeLeadSeconds = 1.0f;

        private void StartDialogue(DialogueBuilder.BuiltDialogue built)
        {
            // Latch + fade first — mirrors the vanilla roomtalk Trigger:
            // emit FadeUI (gameplay UI fades out), then start the actual
            // dialogue after the fade so the fade LEADS the speech UI rather
            // than firing simultaneously with it. Queued (Talk-button) and
            // non-queued (auto-condition) dialogues share this path — the
            // only difference between them is what triggers the start.
            built.HasPlayed = true;

            // Only fade the UI out if it is actually in. When one dialogue
            // hands over to another the UI is already gone, and re-emitting
            // here is what made it flash back in and straight out again.
            // Skipping the emit also means skipping the lead it exists to
            // cover, so a chained dialogue opens immediately instead of
            // sitting on a second of dead air.
            // Already hidden — by our own previous dialogue OR by a vanilla
            // room whose tail we are taking over. Either way there is nothing to
            // do, and nothing to wait for.
            // The UI is already down: BeginUiHoldForPending took it down when
            // this dialogue qualified. Nothing to fade here, and nothing to wait
            // for — the lead exists to cover a fade that has already played.
            bool chained = UiIsHidden();
            if (!chained) BeginUiHoldForPending();
            // A start cancels any pending restore — this IS the handover.
            _uiRestoreGeneration++;

            if (_ctx.Plugin != null)
            {
                _pendingStarts++;
                _ctx.Plugin.StartCoroutine(PlayAfterFade(built, chained ? 0f : FadeLeadSeconds));
            }
            else
            {
                // No coroutine host (shouldn't happen in practice) —
                // start immediately rather than not at all.
                ActivateAndPlay(built);
            }
        }

        /// <summary>
        /// True between the FadeUI that hides the gameplay UI for a dialogue and
        /// the one that brings it back. Static because a handover can cross
        /// packs: whichever dispatcher starts next needs to know the UI is
        /// already out so it does not toggle it.
        /// </summary>
        /// <summary>
        /// True when WE hid the gameplay UI, as opposed to a vanilla room doing
        /// it. Only what we hid do we put back — a vanilla sequence restores its
        /// own through the closing FadeUI in its branch, and stepping on that is
        /// what left the UI in the wrong state before.
        /// </summary>
        private static bool _weHidTheUi;

        private static GameObject _mainCanvas;

        /// <summary>
        /// Is the dialogue currently on screen one of OURS?
        /// <para/>
        /// Told apart by its parent: every pack dialogue is built under the
        /// per-pack always-active host (<c>SMSModForge_&lt;packId&gt;_Dialogues</c>),
        /// while a vanilla one lives under its roomtalk. That decides who owns
        /// the hidden UI when a start stands down.
        /// </summary>
        private static bool IsPackDialogueOnScreen()
        {
            var cur = Dialogue.Current;
            var parent = cur != null ? cur.transform.parent : null;
            return parent != null &&
                   parent.name.StartsWith("SMSModForge_", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// The gameplay UI root. Cached on first sight because
        /// <see cref="GameObject.Find"/> only returns ACTIVE objects, and this
        /// one spends every dialogue deactivated.
        /// </summary>
        private static GameObject MainCanvas()
        {
            if (_mainCanvas == null) _mainCanvas = GameObject.Find("9_MainCanvas");
            return _mainCanvas;
        }

        private static CanvasGroup UiGroup()
        {
            if (_uiGroup == null)
            {
                var c = MainCanvas();
                if (c != null) _uiGroup = c.GetComponent<CanvasGroup>();
            }
            return _uiGroup;
        }

        private static CanvasGroup _uiGroup;

        /// <summary>Is the gameplay UI hidden right now? Read from the scene, so
        /// it is true whether we or a vanilla fade hid it.</summary>
        private static bool UiIsHidden()
        {
            var c = MainCanvas();
            return c != null && !c.activeSelf;
        }

        /// <summary>
        /// Is a dialogue queued up to start on its own once the gate clears?
        /// Deliberately ignores the gate — that is the point, we want to know
        /// before it opens. Talk-button dialogues do not count.
        /// </summary>
        private bool HasPendingAutoStart()
        {
            foreach (var b in _built)
            {
                if (b.Queued) continue;
                if (b.HasPlayed || b.MissedWindow) continue;
                if (b.Dialogue == null) continue;
                if (b.LastConditionsPassed) return true;
            }
            return false;
        }

        /// <summary>
        /// Keep the gameplay UI down from the moment a dialogue QUALIFIES until
        /// it is on screen — not from the moment it starts.
        /// <para/>
        /// That gap is the whole problem. A dialogue qualifies on level entry but
        /// cannot start until Lock-Game drops and the entry hold expires, and in
        /// between the level's own FadeUI restores the UI. Hiding at start meant
        /// the UI came fully back up and was then snapped away, which is the
        /// choppiness — the fade played, and then we undid it without one.
        /// <para/>
        /// Starting the hold at qualification means the UI simply never comes
        /// back: for a vanilla-first handover it is already down and stays down,
        /// and for a plain entry the level's own fade-out still plays, we just
        /// decline to let the matching fade-in through.
        /// </summary>
        private void BeginUiHoldForPending()
        {
            if (_uiHolding) return;
            _uiHolding = true;
            _weHidTheUi = true;

            // Already down (transition fade, or a vanilla room): nothing to
            // play, just keep it there. Still up: let the game's own FadeUI do
            // the animated hide rather than snapping the object off.
            float settle = 0f;
            var c = MainCanvas();
            if (c != null && c.activeSelf)
            {
                ActionRuntime.EmitSignal("FadeUI", _ctx);
                settle = UiFadeSettleSeconds;   // let that animation finish
            }
            if (_ctx.Plugin != null)
                _ctx.Plugin.StartCoroutine(HoldUiHidden(++_uiRestoreGeneration, settle));
        }

        /// <summary>
        /// Long enough for FadeMainUI's alpha transition to finish before we
        /// start enforcing, so emitting the signal is not immediately cut short
        /// by our own SetActive.
        /// </summary>
        private const float UiFadeSettleSeconds = 0.6f;

        /// <summary>How long to hold before assuming the dialogue is not coming.</summary>
        private const float UiWatchdogSeconds = 4f;

        /// <summary>
        /// Re-assert the hidden state each frame until the dialogue is actually
        /// on screen. FadeMainUI branches on the canvas's own active state, so a
        /// level-entry FadeUI arriving mid-hold takes its "show" branch — this is
        /// what outlasts it, within a frame, before the fade-in is visible.
        /// </summary>
        private IEnumerator HoldUiHidden(int generation, float settle)
        {
            if (settle > 0f) yield return new WaitForSeconds(settle);

            float deadline = Time.unscaledTime + UiWatchdogSeconds;
            while (Time.unscaledTime < deadline)
            {
                if (generation != _uiRestoreGeneration) { _uiHolding = false; yield break; }
                if (IsAnyDialoguePlaying()) { _uiHolding = false; yield break; }  // it is up
                ApplyUiHidden();
                yield return null;
            }

            _uiHolding = false;
            if (generation == _uiRestoreGeneration && !IsAnyDialoguePlaying() && _weHidTheUi)
            {
                _ctx.Log?.LogInfo("[SMSModForge.PackPlugin] Restored the gameplay UI — " +
                                  "held down for a dialogue that never started.");
                ShowUiIfWeHidIt();
            }
        }

        private static bool _uiHolding;

        /// <summary>
        /// Mirrors FadeMainUI's hidden branch. The two flags matter: map buttons
        /// and travel blocking read them, so setting the canvas alone would
        /// leave the game half-faded in its own terms.
        /// </summary>
        private static void ApplyUiHidden()
        {
            var c = MainCanvas();
            if (c == null || !c.activeSelf) return;
            var g = UiGroup();
            if (g != null) g.alpha = 0f;
            c.SetActive(false);
            GameVariableBridge.SetBool("2025-fadein-travelblock", true);
            GameVariableBridge.SetBool("Mapbutton-fix1", true);
        }

        /// <summary>
        /// Put back only what we hid, through the game's own signal so it fades
        /// in rather than popping. FadeMainUI is state-driven — with the canvas
        /// inactive this takes its "show" branch — so emitting is safe here in a
        /// way a blind toggle would not be.
        /// </summary>
        private void ShowUiIfWeHidIt()
        {
            _uiHolding = false;
            if (!_weHidTheUi) return;
            _weHidTheUi = false;
            var c = MainCanvas();
            if (c != null && !c.activeSelf) ActionRuntime.EmitSignal("FadeUI", _ctx);
        }

        /// <summary>
        /// Give up our claim on the hidden gameplay UI without restoring it —
        /// the <c>LeaveUiFaded</c> action.
        /// <para/>
        /// A dialogue that opens something of its own (a gift panel, a shop, a
        /// minigame) and then exits wants the UI to STAY down while that thing
        /// is up, and whatever closes it emits the restoring <c>FadeUI</c>. Our
        /// end-of-dialogue restore otherwise fires first and fights it: the UI
        /// comes back under the open panel, and the panel's own closing emit
        /// then toggles it away again, leaving the fade inverted for the rest of
        /// the session.
        /// <para/>
        /// Only meaningful for a panel that lives OUTSIDE <c>9_MainCanvas</c> —
        /// <see cref="ApplyUiHidden"/> deactivates that canvas, so a panel
        /// parented under it would be hidden along with the UI it is meant to
        /// sit over.
        /// </summary>
        internal static void ReleaseUiOwnership()
        {
            _weHidTheUi = false;
            // Stop any in-flight hold from re-hiding the UI after the hand-off:
            // the coroutine drops out as soon as the generation moves, so the
            // new owner's restore can't be undone a frame later.
            _uiHolding = false;
            _uiRestoreGeneration++;
        }

        /// <summary>
        /// Bumped by every start and every scheduled restore. A restore
        /// coroutine that finds the generation has moved on knows a dialogue
        /// took over and drops its fade-in.
        /// </summary>
        private static int _uiRestoreGeneration;

        /// <summary>
        /// Fade the gameplay UI back in once nothing is playing OR waiting to.
        /// <para/>
        /// Waiting matters: at the moment a dialogue finishes the next one
        /// cannot start yet — GC2 is still running the close animation, which
        /// IsStartBlocked reports through DialogueUI.m_IsClosing — so deciding
        /// immediately would always conclude "nothing follows" and flash the UI.
        /// </summary>
        private IEnumerator RestoreUiWhenIdle(int generation)
        {
            // Let the close animation finish and give the dispatchers a tick to
            // hand over. Bounded so a stuck flag can never leave the UI hidden.
            const float MaxWaitSeconds = 3f;
            float deadline = Time.unscaledTime + MaxWaitSeconds;
            while (Time.unscaledTime < deadline)
            {
                if (generation != _uiRestoreGeneration) yield break;   // handed over
                if (!IsStartBlocked()) break;
                yield return null;
            }
            // One more frame so a dialogue eligible on this tick can claim it.
            yield return null;
            if (generation != _uiRestoreGeneration) yield break;

            // Never restore over something that is on screen. The wait above is
            // bounded, and a VANILLA dialogue holds Lock-Game for its whole
            // sequence — long enough to reach that bound — so without this the
            // deadline would fade the gameplay UI back in on top of a vanilla
            // dialogue that had taken over. Dropping the restore leaves the UI
            // out, which is what a playing dialogue wants anyway; whoever is
            // playing brings it back through its own ending.
            if (IsAnyDialoguePlaying()) yield break;

            ShowUiIfWeHidIt();
        }

        private IEnumerator PlayAfterFade(DialogueBuilder.BuiltDialogue built, float delay)
        {
            yield return new WaitForSeconds(delay);
            _pendingStarts--;

            // Re-check before playing. The gate was clear when we committed, but
            // that was a WHOLE SECOND ago (FadeLeadSeconds) and a vanilla
            // dialogue can begin inside that window — its roomtalk reaches Play
            // on its own schedule, nothing to do with us.
            //
            // _pendingStarts is decremented first so this asks about OTHER
            // dialogues, not our own committed start.
            //
            // WHICH question to ask depends on how the start was authorised:
            //
            //  · Auto-start — the FULL gate. A vanilla room that began during
            //    our lead has raised Lock-Game but may not have reached its Play
            //    yet, and asking only about visible dialogues would wave us
            //    through to land on it moments later.
            //
            //  · Talk button and queueBehind — only "is one on screen". Both are
            //    allowed to run while Lock-Game is up, and for the Talk button
            //    that is not optional: the vanilla button's own handler does
            //    SetActive(Core[stored-talk]), which starts the roomtalk whose
            //    first instruction RAISES Lock-Game. Applying the full gate here
            //    made every Talk-button dialogue stand down one second after the
            //    click, re-arm, and never play — while the player watched a
            //    faded screen. TryConsumeTalkButton has always run ahead of the
            //    gate for this reason; this check has to agree with it.
            bool playerRequested = built.Queued || built.QueueBehind;
            if (playerRequested ? IsAnyDialoguePlaying() : IsStartBlocked())
            {
                // Un-commit so a later tick can try again — the conditions that
                // qualified it are probably still true. Whether it actually gets
                // another turn is the normal MissedWindow rule's business.
                built.HasPlayed = false;
                _ctx.Log?.LogInfo("[SMSModForge.PackPlugin] Stood down " + _ctx.PackId + "." +
                                  built.Key + " — another dialogue started during its fade.");

                // The UI is down for a dialogue that never appeared. Who owns it
                // now depends on what actually took over:
                if (IsAnyDialoguePlaying())
                {
                    // Something IS on screen and wants the UI down. A vanilla
                    // dialogue restores it through its own ending, so drop our
                    // claim; a pack one keeps ours, since its ending is what
                    // restores it.
                    if (!IsPackDialogueOnScreen()) _weHidTheUi = false;
                }
                else
                {
                    // NOTHING took over — this is the stand-down that stranded
                    // the UI. Give it straight back rather than waiting for the
                    // watchdog to notice in four seconds.
                    ShowUiIfWeHidIt();
                }
                yield break;
            }

            ActivateAndPlay(built);
        }

        private void ActivateAndPlay(DialogueBuilder.BuiltDialogue built)
        {
            try
            {
                // Pack dialogues live under an always-active host container, so
                // there is nothing to switch on before playing.
                //
                // They used to be parented to the vanilla roomtalk and this
                // activated it. That is what made a pack dialogue cancel a
                // vanilla one: the roomtalk's Trigger is EventOnEnable, Unity
                // runs OnEnable synchronously inside SetActive, and the roomtalk
                // deactivates itself at the end of its own sequence — so it was
                // nearly always inactive when we fired, and activating it ran
                // the whole vanilla room sequence, which reached Play about
                // 1.2s later and stopped ours via DialogueUI.Open.
                //
                // Activate the dialogue GO itself.
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
            // The outfit accepts a $varName, so a node can pick the outfit
            // dynamically from a pack variable's current value (a literal bust
            // GO name passes through Deref unchanged); an empty resolution keeps
            // whatever bust the actor is already wearing.
            string actor = (string)nj["actor"];
            string expression = (string)nj["expression"];
            string outfit = ActionRuntime.Deref((string)nj["outfit"] ?? "", _ctx);
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
        }

        private void OnFinishNext(DialogueBuilder.BuiltDialogue built, int nodeId)
        {
            if (!built.NodeByGc2Id.TryGetValue(nodeId, out var nj)) return;
            // OnFinish actions — in-memory only, same as OnStart. Disk commit
            // happens exclusively at the daily autosave.
            ActionRuntime.ExecuteList(nj["actionsOnFinish"] as JArray, _ctx);
        }

        private void OnDialogueFinished(DialogueBuilder.BuiltDialogue built)
        {
            // FadeUI toggles back — the gameplay UI fades in as the
            // dialogue closes. One emit at start, one at end, mirroring
            // the host mod's EndDialogueSequence. This hook covers a
            // natural finish and an external Stop alike, since
            // Dialogue.Stop also raises EventFinish.
            // Deferred, not immediate. If something else is about to play —
            // a queueBehind dialogue, or a Talk-button one the player already
            // clicked — restoring here just to hide it again a moment later is
            // the flicker. RestoreUiWhenIdle waits until nothing is playing or
            // pending and only then fades back in; a start in the meantime
            // bumps the generation and cancels it.
            if (_ctx.Plugin != null)
                _ctx.Plugin.StartCoroutine(RestoreUiWhenIdle(++_uiRestoreGeneration));
            else ShowUiIfWeHidIt();

            // Put it back on the Talk button so the next click can replay it —
            // the consume pass re-checks conditions and disarms a stale arm on
            // its own. Any click made WHILE the dialogue played is discarded so
            // finishing doesn't instantly chain into another Talk dialogue.
            //
            // Queued dialogues live on that button by definition. ReplayOnTalk
            // ones auto-played on arrival and asked to stay reachable
            // afterwards, which is the whole of what it does.
            if (built.Queued || built.ReplayOnTalk) built.ArmedForTalk = true;
            GameVariableBridge.SetBool("talkbutton-signal", false);

            // Clear any dialogue-wide sprite focus so the cast returns to its
            // resting sorting orders. Covers both natural completion and
            // an external Stop (Dialogue.Stop also raises EventFinish).
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
        /// Is a dialogue actually on screen right now? Asks GC2 directly
        /// instead of guessing from the hierarchy:
        /// <list type="bullet">
        ///   <item><see cref="Dialogue.Current"/> — set synchronously on
        ///   entry to <c>Dialogue.Play</c> and cleared in <c>Stop</c>.</item>
        ///   <item><see cref="DialogueUI.IsOpen"/> — set inside
        ///   <c>DialogueUI.Open</c> <em>before</em> the open animation and
        ///   cleared in <c>OnStop</c>, so it brackets a slightly wider window
        ///   than <c>Current</c>.</item>
        ///   <item><c>DialogueUI.m_IsClosing</c> — the close animation tail,
        ///   after <c>IsOpen</c> has already gone false. <c>Open</c> itself
        ///   waits this out before showing a new dialogue.</item>
        /// </list>
        /// This replaces the old scan of <c>8_Room_Talk</c> grandchildren for
        /// an active <see cref="Dialogue"/> component. That scan was a proxy
        /// for these flags and a leaky one: it assumed a fixed nesting depth,
        /// used <c>activeSelf</c> (so a dialogue under a disabled ancestor read
        /// as playing) and cost a <c>GameObject.Find</c> plus a two-level walk
        /// every frame. These are three static reads.
        /// <para/>
        /// Exposed as a static helper so <see cref="Plugin"/>'s per-frame
        /// mirror tick — and, through it, SMSAndroids' bridge — can call the
        /// same check without holding a dispatcher reference.
        /// </summary>
        internal static bool IsAnyDialoguePlayingGlobal()
        {
            // A start that's still inside its fade window counts as
            // playing — no Dialogue GO is active yet, but one is committed.
            if (_pendingStarts > 0) return true;
            if (Dialogue.Current != null) return true;
            if (DialogueUI.IsOpen) return true;
            var ui = DialogueUI.Current;
            return ui != null && ReadPrivateBool(ui, "m_IsClosing");
        }

        private bool IsAnyDialoguePlaying() => IsAnyDialoguePlayingGlobal();

        /// <summary>
        /// May we START a dialogue this tick? Everything
        /// <see cref="IsAnyDialoguePlaying"/> covers, plus <c>Core[Lock-Game]</c>.
        /// <para/>
        /// The vanilla roomtalk raises Lock-Game as its first instruction and
        /// drops it near its last, so it spans the whole room sequence — the
        /// 0.2s settle, the Conditions run, the 1s lead before Play, the
        /// dialogue, and the tail. That is the committed-but-not-yet-visible
        /// window the dialogue flags cannot see. It is also held during level
        /// transfers, which is equally a moment not to start anything;
        /// NavigatorRuntime and RadialButtonRuntime gate on it for that reason.
        /// <para/>
        /// Deliberately NOT part of <see cref="IsAnyDialoguePlaying"/>, which
        /// drives <c>MissedWindow</c>. Lock-Game is true on EVERY level entry —
        /// exactly when a LevelActive condition starts passing — so folding the
        /// two together burns every dialogue's window the instant the player
        /// walks in, and MissedWindow only clears when they leave again. The
        /// result is a dialogue that can never fire. A blocked start just waits;
        /// if a vanilla dialogue does materialise,
        /// <see cref="IsAnyDialoguePlaying"/> catches it on a later tick and
        /// marks the window then.
        /// </summary>
        private bool IsStartBlocked()
        {
            if (IsAnyDialoguePlaying()) return true;
            if (GameVariableBridge.GetBool("Lock-Game")) return true;

            // Hold briefly after a level becomes active.
            //
            // This is the window the other two checks cannot see. A vanilla room
            // raises Lock-Game as its roomtalk's FIRST instruction, but the
            // roomtalk is only switched on partway through the LEVEL trigger
            // (after its weather/NPC steps), and LevelActive passes the moment
            // the level GO does. So on entry there is a stretch where the level
            // is active, no dialogue is playing and Lock-Game is still false —
            // and committing there is what put a pack dialogue on top of the
            // vanilla one, roughly a second and a half later.
            //
            // It also fixes the fade: committing emits FadeUI, and FadeUI is a
            // toggle, so the vanilla room's own fade landed on top of ours and
            // showed the UI during a dialogue that should have hidden it. Not
            // committing means not emitting, so there is nothing to flicker.
            PollActiveLevel();
            return _activeLevel != null &&
                   Time.unscaledTime - _levelActivatedAt < LevelEntryGraceSeconds;
        }

        /// <summary>
        /// How long to hold off after a level activates. Sized above the vanilla
        /// roomtalk's own fixed lead-in — a 0.2s settle plus a 1s wait before it
        /// reaches Play — so Lock-Game has raised by the time this expires and
        /// takes over the gating. The cost is that a pack dialogue on level
        /// entry starts a beat later, which is the same beat vanilla waits.
        /// </summary>
        private const float LevelEntryGraceSeconds = 1.4f;

        private static Transform _levels;
        private static GameObject _activeLevel;
        private static float _levelActivatedAt = float.NegativeInfinity;
        private static int _levelPollFrame = -1;

        /// <summary>
        /// Track which level under <c>5_Levels</c> is active and when it became
        /// so. Frame-guarded: several dispatchers plus Plugin's mirror tick call
        /// the gate each frame and the timestamp must not be recomputed per
        /// caller.
        /// </summary>
        private static void PollActiveLevel()
        {
            if (_levelPollFrame == Time.frameCount) return;
            _levelPollFrame = Time.frameCount;

            if (_levels == null) _levels = GameObject.Find("5_Levels")?.transform;
            GameObject active = null;
            if (_levels != null)
            {
                for (int i = 0; i < _levels.childCount; i++)
                {
                    var c = _levels.GetChild(i);
                    if (c.gameObject.activeSelf) { active = c.gameObject; break; }
                }
            }
            if (!ReferenceEquals(active, _activeLevel))
            {
                _activeLevel = active;
                _levelActivatedAt = active != null ? Time.unscaledTime : float.NegativeInfinity;
            }
        }

        private static readonly Dictionary<string, System.Reflection.FieldInfo> _privateBoolCache =
            new Dictionary<string, System.Reflection.FieldInfo>();

        private static bool ReadPrivateBool(object target, string fieldName)
        {
            if (target == null) return false;
            if (!_privateBoolCache.TryGetValue(fieldName, out var f))
            {
                f = target.GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (f != null && f.FieldType != typeof(bool)) f = null;
                _privateBoolCache[fieldName] = f;
            }
            if (f == null) return false;
            try { return (bool)f.GetValue(target); }
            catch { return false; }
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
                if (b.SuppressedTrigger.enabled == shouldEnable) continue;

                // Re-enabling a Behaviour on an ACTIVE GameObject fires its
                // OnEnable — and this Trigger is an EventOnEnable one, so
                // restoring it while the room is up would launch the very
                // vanilla sequence we suppressed. Only ever switch it back on
                // while the roomtalk is inactive; the room's own next
                // activation then behaves normally.
                if (shouldEnable && b.VanillaRoomTalk != null &&
                    b.VanillaRoomTalk.gameObject.activeInHierarchy) continue;

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
            // The scene change takes the UI with it; a flag left set here would
            // make the first dialogue of the next scene skip its fade-out.
            _weHidTheUi = false;
            _uiHolding = false;
            _mainCanvas = null;
            _uiGroup = null;
            _uiRestoreGeneration++;
            // Cached from the scene being torn down; a stale Transform would
            // leave the entry grace reading a destroyed hierarchy.
            _levels = null;
            _activeLevel = null;
            _levelActivatedAt = float.NegativeInfinity;
            _levelPollFrame = -1;
        }
    }
}
