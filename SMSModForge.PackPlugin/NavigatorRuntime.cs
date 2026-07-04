using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Owns every navigator button this plugin's packs add to the canvas, and
    /// drives them at runtime:
    /// <list type="bullet">
    ///   <item>Each button is visible only while its source level is active.</item>
    ///   <item>Click detection uses <see cref="NavigatorButtonClick"/>, an
    ///         <c>IPointerClickHandler</c> MonoBehaviour — the same sentinel
    ///         pattern the host mod's <c>ButtonTemplate</c> uses but without
    ///         depending on an asset bundle.</item>
    ///   <item>On click, write the resolved <em>absolute</em> sibling index into
    ///         <c>Upcoming-Level</c> and fire <c>Start-Transfer</c>, then
    ///         activate the destination roomtalk.</item>
    /// </list>
    /// </summary>
    public static class NavigatorRuntime
    {
        private sealed class Binding
        {
            public GameObject Button;
            public GameObject SourceLevel;
            public int TargetSiblingIndex;
            public GameObject TargetRoomTalk;  // may be null for vanilla targets
            public string Music;
            public ButtonConditionSet Conditions;  // null = unconditional

            // Live-label support: when the authored label carries [PV:name]
            // tokens, Tick re-resolves it against the pack store so an
            // Integration-rule (or dialogue-action) variable write renames
            // the button immediately — the data-driven replacement for
            // the host mod's hardcoded "House for Sale" -> "Home" swap.
            public string PackId;
            public string RawLabel;            // authored label, may carry [PV:] tokens
            public TextMeshProUGUI LabelTMP;   // cached label component (null when no tokens)
        }

        private static readonly List<Binding> _bindings = new List<Binding>();
        private static GameObject _pendingRoomTalk;
        private static float _pendingRoomTalkAt = -1f;

        /// <summary>
        /// Cached reference to the <c>Navigator/MapButtons</c> parent.
        /// Used in <see cref="Tick"/> to walk all active children in
        /// sibling order and assign keyboard shortcut numbers.
        /// </summary>
        private static Transform _mapButtonsParent;

        public static void Reset()
        {
            _bindings.Clear();
            _pendingRoomTalk = null;
            _pendingRoomTalkAt = -1f;
            _mapButtonsParent = null;
        }

        public static void WireAll(PackManifest pack, ManualLogSource logger)
        {
            var mapButtons = GameObject.Find("9_MainCanvas")?.transform.Find("Navigator")?.Find("MapButtons");
            var beachButton = mapButtons?.Find("14_beach")?.gameObject;
            var level5 = GameObject.Find("5_Levels")?.transform;
            if (mapButtons == null || beachButton == null || level5 == null)
            {
                logger.LogError("[SMSModForge.PackPlugin] NavigatorRuntime: missing scene anchors, skipping pack " + pack.PackId);
                return;
            }

            // Cache for keyboard-number assignment in Tick().
            _mapButtonsParent = mapButtons;

            // -- Per-pack-place navigator buttons (source = a pack place) --
            var places = pack.Places;
            if (places != null)
            {
                foreach (var p in places)
                {
                    var place = (JObject)p;
                    string sourceKey = (string)place["key"];
                    if (string.IsNullOrEmpty(sourceKey)) continue;

                    // Look up the source place we registered moments ago.
                    var sourceEntry = PlaceRegistry.Resolve("pack:" + pack.PackId + "." + sourceKey, pack.PackId, level5);
                    if (sourceEntry == null || sourceEntry.Level == null) continue;

                    WireButtons(place["navigatorButtons"] as JArray, sourceEntry.Level,
                                pack.PackId, mapButtons, beachButton, level5, logger);
                }
            }

            // -- Vanilla extensions (source = a vanilla level) --
            var vanillaExtensions = pack.Root["vanillaExtensions"] as JArray;
            if (vanillaExtensions != null)
            {
                foreach (var ext in vanillaExtensions)
                {
                    var extObj = (JObject)ext;
                    string source = (string)extObj["source"];
                    if (string.IsNullOrEmpty(source)) continue;

                    var sourceEntry = PlaceRegistry.Resolve(source, pack.PackId, level5);
                    if (sourceEntry == null || sourceEntry.Level == null)
                    {
                        logger.LogWarning("[SMSModForge.PackPlugin] Vanilla extension source '" +
                                          source + "' could not be resolved (pack " + pack.PackId + "). Skipping.");
                        continue;
                    }

                    WireButtons(extObj["navigatorButtons"] as JArray, sourceEntry.Level,
                                pack.PackId, mapButtons, beachButton, level5, logger);
                }
            }
        }

        /// <summary>
        /// Wires a list of <c>navigatorButtons</c> entries to a concrete source
        /// level. Shared between the per-pack-place pass and the vanilla
        /// extension pass — the only thing that differs between them is how
        /// the source level was resolved.
        /// </summary>
        private static void WireButtons(JArray buttons, GameObject sourceLevel, string thisPackId,
            Transform mapButtons, GameObject beachButton, Transform level5, ManualLogSource logger)
        {
            if (buttons == null) return;
            foreach (var b in buttons)
            {
                var btn = (JObject)b;
                string target = (string)btn["target"];
                if (string.IsNullOrEmpty(target)) continue;

                var targetEntry = PlaceRegistry.Resolve(target, thisPackId, level5);
                if (targetEntry == null)
                {
                    logger.LogWarning("[SMSModForge.PackPlugin] Navigator target '" + target +
                                      "' could not be resolved (pack " + thisPackId + "). Skipping.");
                    continue;
                }

                string label = (string)btn["label"];
                var go = CreateButtonGameObject(beachButton, mapButtons, label, target);
                var binding = new Binding
                {
                    Button = go,
                    SourceLevel = sourceLevel,
                    TargetSiblingIndex = targetEntry.AbsoluteSiblingIndex,
                    TargetRoomTalk = targetEntry.RoomTalk,
                    Music = (string)btn["music"],
                    // Authored visibility conditions ({"variable", "minValue"?}
                    // entries, AND-combined) — shared parser/evaluator with
                    // the World Map radial buttons.
                    Conditions = ButtonConditionSet.Parse(btn["conditions"] as JArray, thisPackId),
                    PackId = thisPackId,
                };
                // Only labels with [PV:] tokens pay the per-Tick refresh;
                // plain labels were baked by CreateButtonGameObject and the
                // TMP reference stays null.
                if (TextPlaceholders.HasAny(label))
                {
                    binding.RawLabel = label;
                    binding.LabelTMP = go.transform.Find("Text (TMP)")?.GetComponent<TextMeshProUGUI>();
                }
                _bindings.Add(binding);
            }
        }

        /// <summary>Per-frame work; called from <see cref="Plugin.Update"/>.</summary>
        public static void Tick()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                var b = _bindings[i];
                if (b.Button == null || b.SourceLevel == null) continue;
                bool visible = b.SourceLevel.activeSelf &&
                               (b.Conditions == null || b.Conditions.Pass());
                b.Button.SetActive(visible);

                // Live [PV:] label refresh — only for labels that carry
                // tokens (LabelTMP stays null otherwise) and only while
                // visible. String-compare before write so an unchanged
                // label costs no TMP re-layout.
                if (visible && b.LabelTMP != null)
                {
                    string resolved = TextPlaceholders.Resolve(
                        b.RawLabel, Plugin.TryGetPackVars(b.PackId));
                    if (b.LabelTMP.text != resolved) b.LabelTMP.text = resolved;
                }

                if (b.Button.transform.childCount > 0 &&
                    b.Button.transform.GetChild(0).gameObject.activeSelf)
                {
                    Navigate(b);
                    b.Button.transform.GetChild(0).gameObject.SetActive(false);
                }
            }

            // Assign keyboard shortcut numbers to pack buttons based on
            // their position among ALL active children under MapButtons.
            // Vanilla buttons sit earlier in the sibling order (they were
            // placed at scene-design time) and handle their own shortcuts
            // via ButtonInstructions — our pack buttons, appended later,
            // naturally get higher numbers so there's no collision.
            if (_mapButtonsParent != null)
            {
                int activeCount = 0;
                for (int ci = 0; ci < _mapButtonsParent.childCount; ci++)
                {
                    var child = _mapButtonsParent.GetChild(ci);
                    if (!child.gameObject.activeSelf) continue;
                    activeCount++;
                    var click = child.GetComponent<NavigatorButtonClick>();
                    if (click != null)
                        click.AssignShortcutNumber(activeCount);
                }
            }

            // Roomtalk activation is delayed ~1s after the click, matching
            // the cadence Places.ClickMapButton uses, so the vanilla
            // TransferScene fade-out can finish before the destination
            // dialogue infrastructure shows up.
            if (_pendingRoomTalk != null && _pendingRoomTalkAt > 0f && Time.time >= _pendingRoomTalkAt)
            {
                _pendingRoomTalk.SetActive(true);
                _pendingRoomTalk = null;
                _pendingRoomTalkAt = -1f;
            }
        }

        // -------------------------------------------------------------------

        private static void Navigate(Binding b)
        {
            // The base game listens for `Start-Transfer` going true and reads
            // `Upcoming-Level` to pick the destination from `5_Levels`'s
            // children. We replicate Places.ClickMapButton's flow using the
            // deep-reflection variable bridge (same approach the host mod's
            // Core.FindAndModifyVariable{Double,Bool} uses).
            if (GameVariableBridge.GetBool("Lock-Game")) return;
            GameVariableBridge.SetDouble("Upcoming-Level", b.TargetSiblingIndex);
            GameVariableBridge.SetBool("Start-Transfer", true);

            if (!string.IsNullOrEmpty(b.Music))
            {
                var audioPlayer = GameObject.Find("12_AudioPlayer");
                if (audioPlayer != null)
                {
                    foreach (Transform child in audioPlayer.transform)
                        child.gameObject.SetActive(false);
                    var t = audioPlayer.transform.Find(b.Music);
                    if (t != null) t.gameObject.SetActive(true);
                }
            }

            // Fire the vanilla TransferScene trigger. It's under
            // 10_Gameplay/TransferScene and carries the actual scene-transition
            // logic the game runs when Start-Transfer goes high.
            var transferScene = GameObject.Find("10_Gameplay")?.transform.Find("TransferScene")?.gameObject;
            if (transferScene != null)
            {
                var trigger = GetComponentByName(transferScene, "Trigger");
                if (trigger != null)
                {
                    var execute = trigger.GetType().GetMethod("Execute", System.Type.EmptyTypes);
                    execute?.Invoke(trigger, null);
                }
            }

            // Schedule the destination roomtalk to come up after the transition.
            if (b.TargetRoomTalk != null)
                SchedulePendingRoomTalk(b.TargetRoomTalk, 1.0f);
        }

        /// <summary>
        /// Queues a roomtalk GameObject to be activated after
        /// <paramref name="delaySeconds"/>. Shared between this runtime
        /// and <see cref="RadialButtonRuntime"/> so both flows wait for
        /// the vanilla <c>TransferScene</c> fade-out before bringing the
        /// destination dialogue infrastructure up.
        /// </summary>
        public static void SchedulePendingRoomTalk(GameObject roomTalk, float delaySeconds)
        {
            if (roomTalk == null) return;
            _pendingRoomTalk = roomTalk;
            _pendingRoomTalkAt = Time.time + delaySeconds;
        }

        /// <summary>
        /// Creates a navigator button by cloning the vanilla Beach button and
        /// converting it into the same click-detection pattern that
        /// the host mod's <c>ButtonTemplate</c> prefab uses:
        /// <list type="number">
        ///   <item>Strip the vanilla <c>ButtonInstructions</c> (its hardcoded
        ///         instructions would navigate to Beach).</item>
        ///   <item>Insert a <c>ButtonPressed</c> sentinel child at index 0,
        ///         initially inactive.</item>
        ///   <item>Attach a <see cref="NavigatorButtonClick"/>
        ///         (<c>IPointerClickHandler</c>) that activates the sentinel
        ///         on click — more reliable than a Unity <c>Button</c>
        ///         component on cloned vanilla buttons.</item>
        /// </list>
        /// <see cref="Tick"/> polls <c>child(0).activeSelf</c> each frame;
        /// when it sees the sentinel active it fires <see cref="Navigate"/>
        /// and deactivates the sentinel.
        /// </summary>
        private static GameObject CreateButtonGameObject(GameObject beachButton, Transform parent, string label, string targetToken)
        {
            GameObject mapButton = Object.Instantiate(beachButton, parent);
            mapButton.name = "navbtn_" + targetToken;
            mapButton.SetActive(false);

            // Strip the vanilla click handler — its instructions are
            // hardcoded to navigate to 14_Beach.
            DestroyComponentByName(mapButton, "ButtonInstructions");

            // Add the ButtonPressed sentinel as child(0). It starts inactive;
            // the NavigatorButtonClick handler activates it on click.
            var pressed = new GameObject("ButtonPressed");
            pressed.transform.SetParent(mapButton.transform, false);
            pressed.transform.SetAsFirstSibling();
            pressed.SetActive(false);

            // Attach our IPointerClickHandler for click detection. This is
            // more reliable than Unity's Button on cloned vanilla buttons,
            // which were never designed around Button's click system.
            var clickHandler = mapButton.AddComponent<NavigatorButtonClick>();
            clickHandler.Sentinel = pressed;
            clickHandler.EnsureRaycastTarget();

            // Attach the hover-fade behaviour. Without this the cloned
            // button would have no hover affordance (the vanilla one was
            // driven by ButtonInstructions, which we just stripped).
            ConfigureHoverOverlay(mapButton);

            // Update the label text.
            var labelGo = mapButton.transform.Find("Text (TMP)");
            if (labelGo != null)
            {
                var tmp = labelGo.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = string.IsNullOrEmpty(label) ? targetToken : label;
            }

            return mapButton;
        }

        /// <summary>
        /// Sets the hover-overlay Image at <c>child(2)</c> to alpha 0 and
        /// attaches <see cref="NavigatorButtonHover"/>. The overlay fades
        /// in on pointer-enter and out on pointer-exit (same behaviour as
        /// the host mod's <c>ButtonHover</c>).
        /// </summary>
        private static void ConfigureHoverOverlay(GameObject mapButton)
        {
            if (mapButton.transform.childCount > 2)
            {
                var hoverImage = mapButton.transform.GetChild(2).GetComponent<Image>();
                if (hoverImage != null)
                {
                    var c = hoverImage.color;
                    c.a = 0f;
                    hoverImage.color = c;
                }
            }
            mapButton.AddComponent<NavigatorButtonHover>();
        }

        // --------------------- Component helpers --------------------------

        private static Component GetComponentByName(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
                if (c != null && c.GetType().Name == typeName) return c;
            return null;
        }

        private static void DestroyComponentByName(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
                if (c != null && c.GetType().Name == typeName) { Object.Destroy(c); return; }
        }
    }
}
