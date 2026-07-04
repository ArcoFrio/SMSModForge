using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Tracks each pack-defined actor's current bust + active expression.
    /// Driven by <c>Dialogue.EventStartNext</c>: when a node fires, the
    /// registry looks up the node's <c>actor</c> + <c>expression</c>,
    /// finds the bust GameObject under <c>2_Bust_Manager</c> by name, and
    /// imperatively activates / deactivates the right SpriteRenderers.
    /// <para/>
    /// The pattern matches what the host mod's <c>MainStory</c> does to
    /// drive busts visually, but without depending on it — every operation
    /// uses scene-level <see cref="GameObject.Find"/> + transform walks.
    /// </summary>
    public sealed class ActorRegistry
    {
        public sealed class ActorEntry
        {
            public string Key = "";
            public string DisplayName = "";
            public string DefaultBustKey = "";
            public string CurrentBustKey = "";

            /// <summary>Every bust GO name this actor can wear (declared outfits +
            /// default). Used by the sprite-focus pass to recognise the actor's
            /// on-screen bust regardless of which path activated it — the
            /// generic equivalent of the host mod's <c>GetBustForActor</c>
            /// prefix scan.</summary>
            public readonly HashSet<string> OutfitNames =
                new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            /// <summary>Pack-local expression key → expression GO child name override.</summary>
            public readonly Dictionary<string, string> ExpressionMap = new Dictionary<string, string>();
        }

        private readonly Dictionary<string, ActorEntry> _byKey = new Dictionary<string, ActorEntry>();
        private readonly ManualLogSource _log;

        /// <summary>
        /// Dialogue-wide sprite-focus state. Set by <see cref="SetSpriteFocusAll"/>;
        /// while true, a bust that appears mid-dialogue is focused on arrival.
        /// The dispatcher resets it to false when the dialogue ends.
        /// </summary>
        private bool _spriteFocus;

        /// <summary>
        /// Exact bookkeeping of every bust GameObject we pushed onto the focus
        /// layer. Unfocus restores THIS set (not just name-matched busts), so a
        /// bust that left the stage — or whose actor switched to a non-outfit
        /// bust mid-focus — can never be stranded on the Photos layer.
        /// </summary>
        private readonly HashSet<GameObject> _focusedBusts = new HashSet<GameObject>();

        public ActorRegistry(ManualLogSource log) { _log = log; }

        public void Declare(JObject actor)
        {
            string key = (string)actor["key"];
            if (string.IsNullOrEmpty(key)) return;
            var entry = new ActorEntry
            {
                Key = key,
                DisplayName = (string)actor["displayName"] ?? key,
                DefaultBustKey = (string)actor["defaultBustKey"] ?? "",
            };
            entry.CurrentBustKey = entry.DefaultBustKey;
            if (!string.IsNullOrEmpty(entry.DefaultBustKey)) entry.OutfitNames.Add(entry.DefaultBustKey);
            if (actor["outfits"] is JArray outfits)
                foreach (var o in outfits)
                {
                    string name = (string)o;
                    if (!string.IsNullOrEmpty(name)) entry.OutfitNames.Add(name);
                }
            var exprs = actor["expressions"] as JArray;
            if (exprs != null)
            {
                foreach (var e in exprs)
                {
                    var eo = (JObject)e;
                    string eKey = (string)eo["key"];
                    if (string.IsNullOrEmpty(eKey)) continue;
                    entry.ExpressionMap[eKey] = (string)eo["expressionGoName"] ?? eKey;
                }
            }
            _byKey[key] = entry;
        }

        /// <summary>Reset every actor's current bust to its declared default, and clear sprite focus.</summary>
        public void ResetToDefaults()
        {
            foreach (var a in _byKey.Values) a.CurrentBustKey = a.DefaultBustKey;
            _spriteFocus = false;
            _focusedBusts.Clear();
        }

        public ActorEntry GetOrNull(string key) => _byKey.TryGetValue(key, out var a) ? a : null;

        /// <summary>
        /// Override the bust for an actor (via the SetActorBust action). If the
        /// actor's current bust is <em>on screen</em>, the swap is applied
        /// immediately — deactivate the old, activate the new — so a non-speaking
        /// actor can change outfit mid-scene (e.g. a watched character the
        /// narrator describes). If they're not on screen, the new bust is just
        /// recorded so they appear in it next time. Per-actor, so it's correct
        /// with any number of actors sharing the stage.
        /// </summary>
        public void SetBust(string actorKey, string bustKey)
        {
            if (!_byKey.TryGetValue(actorKey, out var entry)) return;
            if (!string.IsNullOrEmpty(bustKey) && !string.IsNullOrEmpty(entry.CurrentBustKey) &&
                !string.Equals(bustKey, entry.CurrentBustKey, System.StringComparison.OrdinalIgnoreCase))
            {
                var bustManager = GameObject.Find("2_Bust_Manager")?.transform;
                var oldBust = bustManager?.FindChildIgnoreCase(entry.CurrentBustKey)?.gameObject;
                if (oldBust != null && oldBust.activeSelf)
                {
                    oldBust.SetActive(false);
                    var newBust = bustManager?.FindChildIgnoreCase(bustKey)?.gameObject;
                    if (newBust != null) newBust.SetActive(true);
                }
            }
            entry.CurrentBustKey = bustKey;
        }

        /// <summary>
        /// Apply visual state for one node: optionally switch the speaker's
        /// outfit, activate their bust, and route their expression.
        /// <para/>
        /// When <paramref name="outfitKey"/> is a non-empty bust GO name
        /// that differs from the actor's current bust, this is treated as
        /// an outfit change: the actor's previously-shown bust is
        /// deactivated and <see cref="ActorEntry.CurrentBustKey"/> moves to
        /// the new one. Only the speaking actor's own old bust is touched —
        /// <em>other</em> actors' busts stay on screen until the author
        /// explicitly removes them with a <c>DeactivateBust</c> or
        /// <c>LeaveBust</c> node action, matching the host mod pattern
        /// where multiple busts can share the stage.
        /// </summary>
        public void ApplyNodeVisuals(string actorKey, string expressionKey, string outfitKey = "")
        {
            if (string.IsNullOrEmpty(actorKey)) return;
            if (!_byKey.TryGetValue(actorKey, out var entry)) return;

            var bustManager = GameObject.Find("2_Bust_Manager")?.transform;
            if (bustManager == null) return;

            // Outfit switch — move the actor onto a different bust. The
            // bust they were wearing is deactivated first so the old
            // outfit doesn't linger next to the new one. Outfit names are
            // compared case-insensitively (capitalisation isn't significant).
            if (!string.IsNullOrEmpty(outfitKey) &&
                !string.Equals(outfitKey, entry.CurrentBustKey, System.StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(entry.CurrentBustKey))
                {
                    var oldBust = bustManager.FindChildIgnoreCase(entry.CurrentBustKey)?.gameObject;
                    if (oldBust != null && oldBust.activeSelf) oldBust.SetActive(false);
                }
                entry.CurrentBustKey = outfitKey;
            }

            if (string.IsNullOrEmpty(entry.CurrentBustKey)) return;

            var bust = bustManager.FindChildIgnoreCase(entry.CurrentBustKey)?.gameObject;
            if (bust == null) return;
            if (!bust.activeSelf) bust.SetActive(true);
            // Activate the speaker's Mouth — the vanilla mouth animation only
            // runs while this GO is active, and busts can ship with it off.
            // Mirrors the host mod's OnDialogueLineStart, which did
            // MBase.Find("Mouth").SetActive(true) on every spoken line.
            var mbaseForMouth = FindMBase(bust);
            var mouth = mbaseForMouth != null ? mbaseForMouth.Find("Mouth") : null;
            if (mouth != null && !mouth.gameObject.activeSelf) mouth.gameObject.SetActive(true);
            // Keep a bust that appears while a dialogue-wide focus is active
            // focused on arrival, so late entrants match the rest of the cast.
            if (_spriteFocus) { ApplyFocus(bust, true); _focusedBusts.Add(bust); }
            RouteExpression(bust, entry, expressionKey);
        }

        /// <summary>
        /// Find the bust GameObject currently representing the actor (under
        /// <c>2_Bust_Manager</c>). Used by the <c>DeactivateBust</c> /
        /// <c>LeaveBust</c> action handlers. Null when the actor key isn't
        /// declared or the bust GO can't be located.
        /// </summary>
        public GameObject GetCurrentBustGo(string actorKey)
        {
            if (string.IsNullOrEmpty(actorKey)) return null;
            if (!_byKey.TryGetValue(actorKey, out var entry)) return null;
            if (string.IsNullOrEmpty(entry.CurrentBustKey)) return null;
            var bustManager = GameObject.Find("2_Bust_Manager")?.transform;
            return bustManager?.FindChildIgnoreCase(entry.CurrentBustKey)?.gameObject;
        }

        /// <summary>
        /// Raise or lower the <em>whole cast</em>'s busts relative to the CG /
        /// scene layer — every declared actor at once, not a single one.
        /// Mirrors the host mod's <c>SpriteFocusChange</c> +
        /// <c>ChangeBustSortingOrder</c>: when focused, each bust's
        /// <c>MBase1</c> sprite and its Blink / Expressions / Mouth children
        /// get their sorting orders bumped so they render over a full-screen
        /// CG; releasing focus restores the resting orders. Driven by the
        /// <c>SetSpriteFocus</c> node action (the ModForge replacement for the
        /// vanilla <c>SpriteFocus</c> marker).
        /// <para/>
        /// The state is remembered (<see cref="_spriteFocus"/>) so a bust that
        /// appears later in the dialogue is focused too, and the dispatcher
        /// calls this with <c>false</c> when the dialogue ends so the cast
        /// returns to resting orders.
        /// </summary>
        public void SetSpriteFocusAll(bool focused)
        {
            _spriteFocus = focused;
            var bustManager = GameObject.Find("2_Bust_Manager")?.transform;
            if (bustManager == null)
            {
                _log?.LogWarning("[SMSModForge.PackPlugin] SetSpriteFocus(" + focused + "): 2_Bust_Manager not found.");
                return;
            }

            // Resolve the cast's busts the way the host mod's GetBustForActor
            // did — by scanning what's actually under the bust manager and
            // matching against each actor's known outfit names — rather than
            // trusting the tracked CurrentBustKey (a bust activated by another
            // path, e.g. a SetGameObjectActive Bust action or vanilla code,
            // must be focused too). When focusing, only on-screen (activeSelf)
            // busts are touched (late entrants get focused on arrival via
            // ApplyNodeVisuals); unfocusing touches every match so nothing is
            // left with a bumped order.
            // Unfocusing first restores the exact set of busts we focused —
            // regardless of name matching or active state — so nothing can be
            // stranded on the focus layer by a mid-focus outfit switch or exit.
            if (!focused)
            {
                foreach (var go in _focusedBusts)
                    if (go != null) ApplyFocus(go, false);
                _focusedBusts.Clear();
            }

            int applied = 0;
            var appliedNames = new List<string>();
            foreach (Transform child in bustManager)
            {
                if (!IsKnownBustName(child.name)) continue;
                if (focused && !child.gameObject.activeSelf) continue;
                ApplyFocus(child.gameObject, focused);
                applied++;
                if (focused && child.gameObject.activeSelf)
                {
                    appliedNames.Add(child.name);
                    _focusedBusts.Add(child.gameObject);
                }
            }

            if (focused && applied == 0)
                _log?.LogWarning("[SMSModForge.PackPlugin] SetSpriteFocus(true): no active bust matched " +
                                 "any declared actor's outfits — nothing to focus.");
            else
                _log?.LogInfo("[SMSModForge.PackPlugin] SetSpriteFocus(" + focused + "): applied to " +
                              applied + " bust(s)" + (appliedNames.Count > 0 ? " [" + string.Join(", ", appliedNames.ToArray()) + "]" : "") + ".");

            if (focused) DumpFocusDiagnostics(bustManager);
        }

        /// <summary>
        /// One-shot render-state dump when focus engages, to diagnose layering:
        /// logs sorting layer + order + Unity layer for every focused bust's
        /// MBase1 and for every active renderer under the CG managers. If a
        /// focused bust still draws behind a CG, the culprit (different sorting
        /// layer, camera-culling layer, or an unnoticed high-order renderer)
        /// shows up right here in LogOutput.log.
        /// </summary>
        private void DumpFocusDiagnostics(Transform bustManager)
        {
            try
            {
                foreach (Transform child in bustManager)
                {
                    if (!child.gameObject.activeSelf || !IsKnownBustName(child.name)) continue;
                    var mbase = FindMBase(child.gameObject);
                    var sr = mbase != null ? mbase.GetComponent<SpriteRenderer>() : null;
                    if (sr != null)
                        _log?.LogInfo("[FocusDiag] BUST " + child.name + "/" + mbase.name +
                                      " sortLayer=" + sr.sortingLayerName + "(" + sr.sortingLayerID + ")" +
                                      " order=" + sr.sortingOrder +
                                      " unityLayer=" + mbase.gameObject.layer +
                                      " z=" + mbase.position.z.ToString("F2"));
                }

                foreach (var mgrName in new[] { "4_CG_Manager-Sexy", "4_CG_Manager" })
                {
                    var mgr = GameObject.Find(mgrName);
                    if (mgr == null) continue;
                    foreach (var sr in mgr.GetComponentsInChildren<SpriteRenderer>(false))
                    {
                        if (!sr.gameObject.activeInHierarchy || sr.sprite == null) continue;
                        _log?.LogInfo("[FocusDiag] CG " + GetPath(sr.transform, mgr.transform) +
                                      " sortLayer=" + sr.sortingLayerName + "(" + sr.sortingLayerID + ")" +
                                      " order=" + sr.sortingOrder +
                                      " unityLayer=" + sr.gameObject.layer +
                                      " z=" + sr.transform.position.z.ToString("F2"));
                    }
                }
            }
            catch (System.Exception ex)
            {
                _log?.LogWarning("[FocusDiag] dump failed: " + ex.Message);
            }
        }

        private static string GetPath(Transform t, Transform root)
        {
            string path = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// <summary>True when a bust GO name belongs to any declared actor
        /// (their outfit list, default, or currently-tracked bust).</summary>
        private bool IsKnownBustName(string goName)
        {
            if (string.IsNullOrEmpty(goName)) return false;
            foreach (var entry in _byKey.Values)
            {
                if (entry.OutfitNames.Contains(goName)) return true;
                if (!string.IsNullOrEmpty(entry.CurrentBustKey) &&
                    string.Equals(entry.CurrentBustKey, goName, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Per-frame focus watchdog, called from the dispatcher's Tick. The
        /// vanilla per-line machinery resets a bust's sorting order as the
        /// dialogue advances, so a one-shot bump doesn't survive — the host
        /// mod's <c>MainStory.Update</c> continuously re-applied focus while
        /// its SpriteFocus marker was active (<c>sortingOrder != 17 →
        /// SpriteFocusChange(true)</c>), and this is that watchdog. No-op
        /// unless a dialogue-wide focus is currently active; the cheap
        /// order==17 check keeps the per-frame cost near zero once applied.
        /// </summary>
        public void EnforceSpriteFocus()
        {
            if (!_spriteFocus) return;
            var bustManager = GameObject.Find("2_Bust_Manager")?.transform;
            if (bustManager == null) return;
            foreach (Transform child in bustManager)
            {
                if (!child.gameObject.activeSelf) continue;
                if (!IsKnownBustName(child.name)) continue;
                var mbase = FindMBase(child.gameObject);
                var sr = mbase != null ? mbase.GetComponent<SpriteRenderer>() : null;
                if (sr != null && (sr.sortingOrder != 17 || sr.sortingLayerName != FocusSortingLayer))
                {
                    ApplyFocus(child.gameObject, true);
                    _focusedBusts.Add(child.gameObject);
                }
            }
        }

        /// <summary>
        /// The bust's main sprite root. Named <c>MBase1</c> on every bust
        /// we've seen, but the host mod's own per-line code used
        /// <c>GetChild(0)</c> (<c>OnDialogueLineStart</c>), so fall back to
        /// the first child when the name lookup misses.
        /// </summary>
        private static Transform FindMBase(GameObject bust)
        {
            var t = bust.transform.Find("MBase1");
            if (t == null && bust.transform.childCount > 0) t = bust.transform.GetChild(0);
            return t;
        }

        // Sorting layers involved in focus. Busts rest on the "Bust" layer;
        // the CG scenes render on "Photos", which sits ABOVE "Bust" in the
        // project's layer stack — so no order value alone can lift a bust
        // over a CG. Focus therefore hops the bust's sprites onto the CG's
        // own layer (where the 17-vs-16 order contest actually decides), and
        // unfocus returns them to the bust layer.
        private const string FocusSortingLayer   = "Photos";
        private const string RestingSortingLayer = "Bust";

        /// <summary>
        /// Apply focused / resting sorting layer + orders to one bust's
        /// <c>MBase1</c> sprite and its Blink / Expressions / Mouth children.
        /// </summary>
        private static void ApplyFocus(GameObject bust, bool focused)
        {
            var mbase = FindMBase(bust);
            if (mbase == null) return;

            int baseOrder  = focused ? 17 : 0;
            int blinkOrder = focused ? 23 : 6;
            int exprOrder  = focused ? 22 : 5;
            int mouthOrder = focused ? 23 : 6;

            SetSorting(mbase.gameObject, baseOrder, focused);
            var blink = mbase.Find("Blink");
            if (blink != null) SetSorting(blink.gameObject, blinkOrder, focused);

            var expressions = mbase.Find("Expressions");
            if (expressions != null)
                foreach (var e in StandardExpressions)
                {
                    var t = expressions.Find(e);
                    if (t != null) SetSorting(t.gameObject, exprOrder, focused);
                }

            var mouth = mbase.Find("Mouth");
            if (mouth != null)
                for (int i = 1; i <= 4; i++)
                {
                    var m = mouth.Find(i.ToString());
                    if (m != null) SetSorting(m.gameObject, mouthOrder, focused);
                }
        }

        /// <summary>
        /// Set a GameObject's <see cref="SpriteRenderer"/> sorting layer +
        /// order; a <c>Wet</c> overlay child (if present and active) tracks
        /// one order above, matching the host mod's <c>ChangeBustSortingOrder</c>.
        /// </summary>
        private static void SetSorting(GameObject go, int order, bool focused)
        {
            string layer = focused ? FocusSortingLayer : RestingSortingLayer;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) { sr.sortingLayerName = layer; sr.sortingOrder = order; }

            var wet = go.transform.Find("Wet");
            if (wet != null && wet.gameObject.activeSelf)
            {
                var wsr = wet.GetComponent<SpriteRenderer>();
                if (wsr != null) { wsr.sortingLayerName = layer; wsr.sortingOrder = order + 1; }
            }
        }

        private static readonly string[] StandardExpressions = { "Happy", "Angry", "Sad", "Flirty" };

        private void RouteExpression(GameObject bust, ActorEntry entry, string expressionKey)
        {
            var expressions = bust.transform.Find("MBase1/Expressions");
            if (expressions == null) return;

            // Deactivate every standard expression first so we end up with at
            // most one active.
            foreach (var name in StandardExpressions)
            {
                var t = expressions.Find(name);
                if (t != null) t.gameObject.SetActive(false);
            }

            if (string.IsNullOrEmpty(expressionKey)) return;

            string goName = expressionKey;
            if (entry.ExpressionMap.TryGetValue(expressionKey, out var mapped)) goName = mapped;
            var picked = expressions.Find(goName);
            if (picked != null) picked.gameObject.SetActive(true);
        }
    }
}
