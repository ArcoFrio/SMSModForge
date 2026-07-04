using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Builds and drives pack-defined buttons on the World Map's radial
    /// district menus (e.g. the host mod's "a place button" added to
    /// the Foundry district).
    /// <para/>
    /// Each manifest entry under <c>mapButtons</c> describes:
    /// <list type="bullet">
    ///   <item><c>target</c> — same token format as navigator buttons
    ///         (<c>vanilla:&lt;name&gt;</c>, <c>pack:&lt;packId&gt;.&lt;key&gt;</c>,
    ///         or <c>self:&lt;key&gt;</c>).</item>
    ///   <item><c>district</c> — name of a radial-buttons child under
    ///         <c>World_Map/Canvas/Core/Radial_Buttons</c>
    ///         (e.g. <c>"Foundry"</c>, <c>"Seaside"</c>).</item>
    ///   <item><c>label</c> — display text.</item>
    ///   <item><c>music</c> (optional) — name of a child under
    ///         <c>12_AudioPlayer</c> to swap to on click.</item>
    ///   <item><c>conditions</c> (optional) — visibility gating, same
    ///         shape + semantics as navigator-button conditions
    ///         (<see cref="ButtonConditionSet"/>). Re-evaluated every
    ///         frame in <see cref="Tick"/>; without conditions the
    ///         button is always present in its district menu.</item>
    /// </list>
    /// <para/>
    /// Implementation mirrors the host mod's
    /// <c>CreateHarborHouseEntranceRadialButton</c>: clone the
    /// <c>Seaside/Beach</c> button as a template, strip its
    /// <c>ButtonInstructions</c>, set up a Unity <c>Button</c> with a
    /// ColorTint transition, and route clicks through the same
    /// <c>Upcoming-Level</c> / <c>Start-Transfer</c> / <c>TransferScene</c>
    /// flow the navigator buttons use.
    /// </summary>
    public static class RadialButtonRuntime
    {
        private sealed class Binding
        {
            public GameObject Button;
            public int TargetSiblingIndex;
            public GameObject TargetRoomTalk;
            public string Music;
            public ButtonConditionSet Conditions;  // null = always visible

            // Live-label support — same mechanism as NavigatorRuntime:
            // labels carrying [PV:name] tokens re-resolve every Tick so a
            // variable write (Integration rule, dialogue action) renames
            // the button in place. The a place's "For Sale" ->
            // "Home" swap is authored this way.
            public string PackId;
            public string RawLabel;
            public TextMeshProUGUI LabelTMP;
        }

        private static readonly List<Binding> _bindings = new List<Binding>();

        public static void Reset() => _bindings.Clear();

        /// <summary>
        /// Per-frame visibility gating; called from <see cref="Plugin.Update"/>.
        /// Only touches buttons that actually carry conditions — buttons
        /// without them keep whatever visibility their district menu gives
        /// them (the vanilla World Map shows/hides districts itself).
        /// </summary>
        public static void Tick()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                var b = _bindings[i];
                if (b.Button == null) continue;
                if (b.Conditions != null)
                {
                    bool pass = b.Conditions.Pass();
                    if (b.Button.activeSelf != pass)
                        b.Button.SetActive(pass);
                }

                // Live [PV:] label refresh. Unconditional on visibility —
                // radials live under the (often inactive) World_Map, so
                // gating on activeSelf would leave the label stale at the
                // moment the map opens. A string compare per binding per
                // frame is negligible (packs ship a handful of radials).
                if (b.LabelTMP != null)
                {
                    string resolved = TextPlaceholders.Resolve(
                        b.RawLabel, Plugin.TryGetPackVars(b.PackId));
                    if (b.LabelTMP.text != resolved) b.LabelTMP.text = resolved;
                }
            }
        }

        public static void WireAll(PackManifest pack, ManualLogSource logger)
        {
            var mapButtons = pack.Root["mapButtons"] as JArray;
            if (mapButtons == null || mapButtons.Count == 0) return;

            var worldMap = FindInActive("World_Map");
            if (worldMap == null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] World_Map not found — skipping radial buttons for pack " + pack.PackId);
                return;
            }
            var radialRoot = worldMap.transform.Find("Canvas")?.Find("Core")?.Find("Radial_Buttons");
            if (radialRoot == null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Radial_Buttons container not found — skipping radial buttons for pack " + pack.PackId);
                return;
            }
            var seasideBeach = radialRoot.Find("Seaside")?.Find("Beach")?.gameObject;
            if (seasideBeach == null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Seaside/Beach radial button (template) not found — skipping radial buttons for pack " + pack.PackId);
                return;
            }

            var level5 = GameObject.Find("5_Levels")?.transform;
            if (level5 == null) return;

            int built = 0;
            foreach (var b in mapButtons)
            {
                var btn = b as JObject;
                if (btn == null) continue;

                string district = (string)btn["district"];
                string target = (string)btn["target"];
                if (string.IsNullOrEmpty(district) || string.IsNullOrEmpty(target))
                {
                    logger.LogWarning("[SMSModForge.PackPlugin] mapButtons entry in " + pack.PackId + " missing district/target — skipping");
                    continue;
                }

                var districtT = radialRoot.Find(district);
                if (districtT == null)
                {
                    logger.LogWarning("[SMSModForge.PackPlugin] District '" + district + "' not found on World Map (pack " + pack.PackId + ")");
                    continue;
                }

                var targetEntry = PlaceRegistry.Resolve(target, pack.PackId, level5);
                if (targetEntry == null)
                {
                    logger.LogWarning("[SMSModForge.PackPlugin] Radial button target '" + target + "' could not be resolved (pack " + pack.PackId + ")");
                    continue;
                }

                string label = (string)btn["label"];
                var go = CreateRadialButton(seasideBeach, districtT, label, target);
                var binding = new Binding
                {
                    Button = go,
                    TargetSiblingIndex = targetEntry.AbsoluteSiblingIndex,
                    TargetRoomTalk = targetEntry.RoomTalk,
                    Music = (string)btn["music"],
                    Conditions = ButtonConditionSet.Parse(btn["conditions"] as JArray, pack.PackId),
                    PackId = pack.PackId,
                };
                if (TextPlaceholders.HasAny(label))
                {
                    binding.RawLabel = label;
                    binding.LabelTMP = go.transform.Find("Text (TMP)")?.GetComponent<TextMeshProUGUI>();
                }
                _bindings.Add(binding);

                // Conditioned buttons start hidden — Tick() reveals them
                // the moment their conditions pass, so there's no first-
                // frame flash of a locked destination.
                if (binding.Conditions != null)
                    go.SetActive(false);

                // Wire the click. Captures binding by reference.
                var unityButton = go.GetComponent<Button>();
                if (unityButton != null)
                    unityButton.onClick.AddListener(() => OnRadialClick(binding, worldMap));

                built++;
            }

            if (built > 0)
                logger.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId + "' built " + built + " radial map button(s).");
        }

        // ----- Build ----------------------------------------------------

        /// <summary>
        /// Clones the Seaside/Beach radial button into the target district,
        /// strips its GC2 <c>ButtonInstructions</c>, and configures a Unity
        /// <c>Button</c> with the same ColorTint feedback the host mod uses.
        /// </summary>
        private static GameObject CreateRadialButton(GameObject seasideBeach, Transform districtParent, string label, string targetToken)
        {
            GameObject btn = Object.Instantiate(seasideBeach, districtParent);
            // Name the button after the bare place key (drop the
            // self:/pack:<id>./vanilla: prefix) so external integrations
            // that look radial buttons up by name — the host mod's
            // ScheduleVisualizer maps "Foundry/HarborHouseEntrance" for
            // its character-icon overlays — resolve the pack-built button
            // transparently.
            btn.name = BareKeyFromToken(targetToken);

            // Set label.
            var textTMP = btn.transform.Find("Text (TMP)");
            if (textTMP != null)
            {
                var tmp = textTMP.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = string.IsNullOrEmpty(label) ? targetToken : label;
            }

            // Strip the vanilla click handler that would navigate to Beach.
            foreach (var c in btn.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name == "ButtonInstructions")
                {
                    Object.DestroyImmediate(c);
                    break;
                }
            }

            // Set up Unity Button + ColorTint feedback. Matches the values
            // the host mod uses on its a place button radial button.
            var button = btn.GetComponent<Button>();
            if (button == null) button = btn.AddComponent<Button>();
            var targetImage = btn.GetComponent<Image>();
            if (targetImage != null) button.targetGraphic = targetImage;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.RemoveAllListeners();

            return btn;
        }

        /// <summary>Strip the target-token prefix down to the bare place /
        /// level key: <c>self:X</c> → <c>X</c>, <c>pack:Id.X</c> → <c>X</c>,
        /// <c>vanilla:X</c> → <c>X</c>, anything else unchanged.</summary>
        private static string BareKeyFromToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;
            if (token.StartsWith("self:")) return token.Substring("self:".Length);
            if (token.StartsWith("vanilla:")) return token.Substring("vanilla:".Length);
            if (token.StartsWith("pack:"))
            {
                int dot = token.IndexOf('.');
                if (dot >= 0 && dot < token.Length - 1) return token.Substring(dot + 1);
            }
            return token;
        }

        // ----- Click handler -------------------------------------------

        /// <summary>
        /// Fires the same navigation flow the navigator buttons use,
        /// plus pings the world map's <c>Click_Effect</c> child (the
        /// vanilla visual feedback for radial-button clicks).
        /// </summary>
        private static void OnRadialClick(Binding b, GameObject worldMap)
        {
            // Visual feedback — the swooshy click effect on the world map.
            if (worldMap != null)
            {
                var clickEffect = worldMap.transform.Find("Click_Effect");
                if (clickEffect != null) clickEffect.gameObject.SetActive(true);
            }

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

            // Fire vanilla TransferScene trigger.
            var transferScene = GameObject.Find("10_Gameplay")?.transform.Find("TransferScene")?.gameObject;
            if (transferScene != null)
            {
                foreach (var c in transferScene.GetComponents<Component>())
                {
                    if (c == null) continue;
                    if (c.GetType().Name != "Trigger") continue;
                    var execute = c.GetType().GetMethod("Execute", System.Type.EmptyTypes);
                    execute?.Invoke(c, null);
                    break;
                }
            }

            // Schedule destination roomtalk after the transition fade.
            if (b.TargetRoomTalk != null)
                NavigatorRuntime.SchedulePendingRoomTalk(b.TargetRoomTalk, 1.0f);
        }

        // ----- Helpers --------------------------------------------------

        /// <summary>
        /// Finds a root GameObject by name, including inactive ones. The
        /// World Map is often hierarchy-inactive on start.
        /// </summary>
        private static GameObject FindInActive(string name)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null) continue;
                if (t.hideFlags != HideFlags.None) continue;
                if (t.parent != null) continue; // only roots
                if (t.name == name) return t.gameObject;
            }
            // Fallback: walk every transform.
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t != null && t.name == name && t.hideFlags == HideFlags.None && t.gameObject.scene.IsValid())
                    return t.gameObject;
            }
            return null;
        }
    }
}
