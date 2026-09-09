using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Builds a pack's UI in the running game: screens of its own, and changes
    /// to screens the game already has.
    /// <para/>
    /// One entry in <c>uis</c> covers both, told apart by whether it names a
    /// <c>source</c>. With one, the tree is a DELTA: each node carries a
    /// <c>bind</c> path naming an object that already exists, and only the
    /// properties it actually asserts. Nodes without a bind are the pack's own
    /// and get created. Without a source the whole tree is the pack's.
    /// <para/>
    /// The editor already worked out which is which — everything reaching here
    /// has been through its pruning pass, so a node present in the manifest is
    /// a node that changes something. This does not re-derive that; it applies
    /// what it is given.
    /// </summary>
    internal static class UiFactory
    {
        /// <summary>Where a pack's own screen goes when it should fade with the
        /// rest of the interface. Everything under this canvas inherits its
        /// CanvasGroup, which is the game's whole mechanism for hiding the UI
        /// during a cutscene.</summary>
        private const string GameplayCanvas = "9_MainCanvas";

        public static void BuildAll(PackManifest pack, PackContext ctx, ManualLogSource log)
        {
            var uis = pack?.Uis;
            if (uis == null || uis.Count == 0) return;
            Log = log;

            int built = 0, patched = 0, failed = 0;
            foreach (var token in uis)
            {
                var ui = token as JObject;
                if (ui == null) continue;
                try
                {
                    string source = (string)ui["source"] ?? "";
                    if (string.IsNullOrEmpty(source)) { BuildOwn(pack, ctx, ui, log); built++; }
                    else { Patch(pack, ctx, ui, source, log); patched++; }
                }
                catch (Exception ex)
                {
                    // One bad UI must not cost the rest of the pack. A screen
                    // that fails to build is visible as a screen that is not
                    // there; a pack that fails to load is not.
                    failed++;
                    log?.LogWarning($"[UI] '{(string)ui["name"] ?? (string)ui["source"]}' "
                                  + $"failed: {ex.Message}");
                }
            }

            if (built + patched + failed > 0)
                log?.LogInfo($"[UI] {built} built, {patched} patched"
                           + (failed > 0 ? $", {failed} failed" : ""));
        }

        // ── A screen of the pack's own ───────────────────────────────

        private static void BuildOwn(PackManifest pack, PackContext ctx, JObject ui, ManualLogSource log)
        {
            var nodes = ui["nodes"] as JArray;
            if (nodes == null || nodes.Count == 0) return;

            string id = (string)ui["id"] ?? (string)ui["name"] ?? "pack-ui";
            bool hides = ui["hidesWithGameplayUi"] == null
                      || (bool)ui["hidesWithGameplayUi"];

            Transform parent;
            if (hides)
            {
                // Under the gameplay canvas, so it dims and hides with the rest
                // of the interface. That is a parenting fact rather than a
                // setting: there is nothing else to switch on.
                var canvas = TransformExtensions.FindGlobalIncludingInactive(GameplayCanvas);
                if (canvas == null)
                    throw new InvalidOperationException(
                        $"'{GameplayCanvas}' is not in this scene, so a UI that hides "
                      + "with the game's interface has nowhere to go.");
                parent = canvas.transform;
            }
            else
            {
                parent = NewCanvas(id, ReadInt(ui["sortingOrder"], 0)).transform;
            }

            var root = new GameObject("Pack_" + id, typeof(RectTransform));
            root.transform.SetParent(parent, worldPositionStays: false);

            foreach (var node in nodes)
                if (node is JObject n) Create(pack, ctx, n, root.transform, log, ButtonSoundFor(ui));

            // Off unless the pack says otherwise, matching the game: 34 of its
            // 49 canvases wait for their moment.
            ApplyOpenAnimation(root, ui["open"] as JObject);
            ApplyCloseAnimation(root, ui["close"] as JObject);

            // After the animation is attached, so a screen that starts open
            // plays it on the way in rather than appearing and then animating.
            root.SetActive(ui["startsOpen"] != null && (bool)ui["startsOpen"]);
            UiRegistry.Register(id, root);
        }

        /// <summary>A canvas of the pack's own, matching the game's own
        /// settings so a pixel authored at 1920x1080 lands where it was put.</summary>
        private static GameObject NewCanvas(string id, int sortingOrder)
        {
            var go = new GameObject("PackUiCanvas_" + id,
                                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;      // what most of the game's canvases use
            return go;
        }

        // ── A change to a screen the game has ────────────────────────

        private static void Patch(PackManifest pack, PackContext ctx, JObject ui, string source,
                                  ManualLogSource log)
        {
            var baseObject = ResolveBase(source);
            if (baseObject == null)
            {
                log?.LogWarning($"[UI] '{source}' is not in this scene. The screen may "
                              + "have changed between game versions; its edits are skipped.");
                return;
            }

            foreach (var token in ui["nodes"] as JArray ?? new JArray())
                if (token is JObject node)
                    ApplyNode(pack, ctx, node, baseObject.transform, log, ButtonSoundFor(ui));
        }

        /// <summary>
        /// Find the object a <c>vanillaui:</c> token names.
        /// <para/>
        /// The token is the surface's path and the base's name, both as the
        /// scene has them, with a <c>#n</c> suffix on the few that share a name
        /// with a sibling. The editor assigns those by sibling order, so they
        /// are resolved the same way here.
        /// </summary>
        private static GameObject ResolveBase(string token)
        {
            string id = token.StartsWith("vanillaui:", StringComparison.OrdinalIgnoreCase)
                ? token.Substring("vanillaui:".Length) : token;

            int cut = id.LastIndexOf('/');
            if (cut <= 0) return null;

            // A surface path can itself carry a suffix - one level in the game
            // holds two canvases with the same name - but ResolveGameObject
            // walks by name from a scene root and cannot pick between them. The
            // suffix is stripped so the first is found rather than nothing;
            // wrong only for that one pair, and wrong towards drawing something.
            string surfacePath = StripAll(id.Substring(0, cut));
            string baseName = Strip(id.Substring(cut + 1), out int baseNth);

            var surface = TransformExtensions.ResolveGameObject(surfacePath);
            if (surface == null) return null;

            return Child(surface.transform, baseName, baseNth);
        }

        /// <summary>Apply one node, and hand back the object it applied to so
        /// its parent can put it in the right place among its siblings.</summary>
        private static Transform ApplyNode(PackManifest pack, PackContext ctx, JObject node,
                                           Transform under, ManualLogSource log,
                                           string buttonSound = null)
        {
            string bind = (string)node["bind"] ?? "";
            if (bind.Length == 0) return Create(pack, ctx, node, under, log, buttonSound);

            var target = bind == "." ? under.gameObject : Walk(under, bind);
            if (target == null)
            {
                log?.LogWarning($"[UI] '{bind}' is not on this screen any more; skipped.");
                return null;
            }

            // Only what the node actually asserts. The editor stripped the rest
            // at save time, and re-applying a value the pack merely copied is
            // how an extension quietly pins a screen to the shape it had when
            // the pack was written.
            if (Flag(node["overrideRect"])) ApplyRect(target, node["rect"] as JObject);
            if (Flag(node["overrideImage"])) ApplyImage(pack, target, node["image"] as JObject);
            if (Flag(node["overrideText"])) ApplyText(pack, target, node["text"] as JObject);
            if (Flag(node["overrideActive"]))
                target.SetActive(node["startActive"] == null || (bool)node["startActive"]);

            ApplyExtras(pack, ctx, target, node, buttonSound);

            var placed = new List<KeyValuePair<int, Transform>>();
            foreach (var token in node["children"] as JArray ?? new JArray())
            {
                var child = token as JObject;
                if (child == null) continue;
                Remember(placed, child, ApplyNode(pack, ctx, child, target.transform, log, buttonSound));
            }
            Reorder(placed);

            return target.transform;
        }

        // ── Making objects ───────────────────────────────────────────

        /// <summary>
        /// What a button sounds like when nobody has said otherwise: the game's
        /// own button click, so a pack's buttons sound like the game's without
        /// anyone having to ask for it.
        /// <para/>
        /// Named rather than shipped - the player already has this clip, and the
        /// runtime asks the game for it. Kept in step with the editor's copy of
        /// the name by a test there, since the two projects cannot reference
        /// each other.
        /// </summary>
        internal const string DefaultButtonSound = "Mountain Audio - Bubble Button 1";

        /// <summary>
        /// What every button on one screen sounds like: what the screen asked
        /// for, or the default when it did not - and nothing at all when the
        /// screen has been silenced. An object naming its own sound overrides
        /// this either way, so silencing a screen does not gag a button that
        /// was deliberately given a voice.
        /// </summary>
        private static string ButtonSoundFor(JObject ui)
        {
            if (ui == null) return DefaultButtonSound;
            if (ui["silentButtons"] != null && (bool)ui["silentButtons"]) return null;

            string named = (string)ui["buttonSound"];
            return !string.IsNullOrEmpty(named) ? named : DefaultButtonSound;
        }

        /// <summary>
        /// A sound a pack named: one of its own first, then one of the game's.
        /// <para/>
        /// Its own first so a pack can deliberately shadow a game sound with a
        /// version of its own, which is the way round an author would expect.
        /// </summary>
        internal static AudioClip ResolveSound(PackContext ctx, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (ctx != null && ctx.Sfx != null)
            {
                var entry = ctx.Sfx.Get(name);
                if (entry != null)
                {
                    // Declared by the pack. A null clip here means its file has
                    // not finished loading, which is a moment rather than a
                    // mistake, so there is nothing to say about it.
                    return ctx.Sfx.PickRandomClip(entry);
                }
            }

            var fromGame = UiAssets.Sound(name);
            if (fromGame != null) return fromGame;

            WarnOnce(ctx, name);
            return null;
        }

        /// <summary>
        /// Say - once per name, not once per click - that a sound is not there.
        /// <para/>
        /// A button that makes no noise looks exactly like a button that was
        /// never meant to, so without this the difference between "silent on
        /// purpose" and "the name is wrong" is invisible. The nearest names the
        /// game does have go in the message, because the answer is almost always
        /// one of them spelled differently.
        /// </summary>
        private static void WarnOnce(PackContext ctx, string name)
        {
            if (!MissingSounds.Add(name)) return;
            if (ctx == null || ctx.Log == null) return;

            string message = "[SMSModForge.PackPlugin] no sound called '" + name
                           + "' - neither one of pack '" + ctx.PackId
                           + "'s own nor one the game has loaded. Buttons asking for it are silent.";

            string near = string.Join(", ", Nearest(name));
            if (near.Length > 0) message += " The game does have: " + near + ".";

            ctx.Log.LogWarning(message);
        }

        private static readonly HashSet<string> MissingSounds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Loaded sounds whose names share a word with the one asked
        /// for. Short words are skipped - matching on "the" suggests
        /// everything.</summary>
        private static List<string> Nearest(string name)
        {
            var words = new List<string>();
            foreach (var word in name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
                if (word.Length >= 4) words.Add(word);

            var hits = new List<string>();
            foreach (var candidate in UiAssets.SoundNames)
            {
                foreach (var word in words)
                    if (candidate.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hits.Add(candidate);
                        break;
                    }
                if (hits.Count >= 5) break;
            }
            return hits;
        }

        private static Transform Create(PackManifest pack, PackContext ctx, JObject node,
                                        Transform parent, ManualLogSource log,
                                        string buttonSound = null)
        {
            var go = new GameObject((string)node["name"] ?? "Object", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            ApplyRect(go, node["rect"] as JObject);
            ApplyImage(pack, go, node["image"] as JObject);
            ApplyText(pack, go, node["text"] as JObject);
            ApplyExtras(pack, ctx, go, node, buttonSound);

            var placed = new List<KeyValuePair<int, Transform>>();
            foreach (var token in node["children"] as JArray ?? new JArray())
            {
                var child = token as JObject;
                if (child == null) continue;
                Remember(placed, child, Create(pack, ctx, child, go.transform, log, buttonSound));
            }
            Reorder(placed);

            go.SetActive(node["startActive"] == null || (bool)node["startActive"]);
            return go.transform;
        }

        // ── Order ──────────────────────────────────────
        //
        // Sibling order is draw order on a canvas: there is no depth to sort
        // by, so a later sibling is simply in front. A node carries an explicit
        // place only when the editor found the author had rearranged one - and
        // then every child of that parent carries one, so the whole arrangement
        // is stated rather than half of it.

        private static void Remember(List<KeyValuePair<int, Transform>> placed,
                                     JObject node, Transform made)
        {
            if (made == null) return;
            int at = ReadInt(node["siblingIndex"], -1);
            if (at >= 0) placed.Add(new KeyValuePair<int, Transform>(at, made));
        }

        /// <summary>
        /// Put each object at the place it asked for, lowest first.
        /// <para/>
        /// Ascending matters. Each call shuffles everything after it along, so
        /// placing 0, then 1, then 2 leaves each one where it was asked for;
        /// going the other way undoes the placements already made.
        /// </summary>
        private static void Reorder(List<KeyValuePair<int, Transform>> placed)
        {
            if (placed.Count == 0) return;
            placed.Sort(delegate (KeyValuePair<int, Transform> a, KeyValuePair<int, Transform> b)
            {
                return a.Key.CompareTo(b.Key);
            });
            for (int i = 0; i < placed.Count; i++)
                if (placed[i].Value != null) placed[i].Value.SetSiblingIndex(placed[i].Key);
        }


        private static void ApplyLayout(GameObject go, JObject layout)
        {
            // Whatever was there before goes: a node switched from a row to a
            // grid would otherwise carry both and Unity would fight itself.
            var had = go.GetComponent<LayoutGroup>();
            if (had != null) UnityEngine.Object.DestroyImmediate(had);
            if (layout == null) return;

            string kind = (string)layout["kind"] ?? "Horizontal";
            var padding = new RectOffset(
                (int)ReadFloat(Element(layout["padding"], 0), 0),
                (int)ReadFloat(Element(layout["padding"], 1), 0),
                (int)ReadFloat(Element(layout["padding"], 2), 0),
                (int)ReadFloat(Element(layout["padding"], 3), 0));

            TextAnchor anchor = TextAnchor.MiddleCenter;
            try { anchor = (TextAnchor)System.Enum.Parse(typeof(TextAnchor),
                        (string)layout["alignment"] ?? "MiddleCenter"); }
            catch { }

            if (kind == "Grid")
            {
                // Ours only when it is asked for. UiCenteredGrid IS a
                // GridLayoutGroup and behaves as one otherwise, but a component
                // of the pack's own on every grid would be a difference nobody
                // asked for on screens that never needed it.
                bool centreLast = layout["centerLastLine"] != null
                               && (bool)layout["centerLastLine"];

                var grid = centreLast
                    ? go.AddComponent<UiCenteredGrid>()
                    : go.AddComponent<GridLayoutGroup>();
                grid.padding = padding;
                grid.childAlignment = anchor;
                grid.cellSize = Vec2(layout["cellSize"], new Vector2(100, 100));
                grid.spacing = Vec2(layout["spacing"], Vector2.zero);
                grid.constraintCount = Mathf.Max(1, (int)ReadFloat(layout["constraintCount"], 2));
                try { grid.constraint = (GridLayoutGroup.Constraint)System.Enum.Parse(
                        typeof(GridLayoutGroup.Constraint), (string)layout["constraint"] ?? "Flexible"); }
                catch { }
                try { grid.startAxis = (GridLayoutGroup.Axis)System.Enum.Parse(
                        typeof(GridLayoutGroup.Axis), (string)layout["startAxis"] ?? "Horizontal"); }
                catch { }
                try { grid.startCorner = (GridLayoutGroup.Corner)System.Enum.Parse(
                        typeof(GridLayoutGroup.Corner), (string)layout["startCorner"] ?? "UpperLeft"); }
                catch { }
                return;
            }

            HorizontalOrVerticalLayoutGroup line = kind == "Vertical"
                ? (HorizontalOrVerticalLayoutGroup)go.AddComponent<VerticalLayoutGroup>()
                : go.AddComponent<HorizontalLayoutGroup>();

            line.padding = padding;
            line.childAlignment = anchor;
            line.spacing = ReadFloat(Element(layout["spacing"], 0), 0);
            line.childControlWidth = Flag(layout["controlWidth"]);
            line.childControlHeight = Flag(layout["controlHeight"]);
            line.childForceExpandWidth = Flag(layout["expandWidth"]);
            line.childForceExpandHeight = Flag(layout["expandHeight"]);
            line.reverseArrangement = Flag(layout["reverse"]);
        }

        private static JToken Element(JToken array, int i)
        {
            var a = array as JArray;
            return a != null && a.Count > i ? a[i] : null;
        }

        private static void ApplyRect(GameObject go, JObject rect)
        {
            if (rect == null) return;
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;

            rt.anchorMin = Vec2(rect["anchorMin"], new Vector2(0.5f, 0.5f));
            rt.anchorMax = Vec2(rect["anchorMax"], new Vector2(0.5f, 0.5f));
            rt.pivot = Vec2(rect["pivot"], new Vector2(0.5f, 0.5f));
            rt.anchoredPosition = Vec2(rect["position"], Vector2.zero);
            rt.sizeDelta = Vec2(rect["size"], new Vector2(100, 100));

            var scale = Vec2(rect["scale"], Vector2.one);
            rt.localScale = new Vector3(scale.x, scale.y, 1f);
            rt.localEulerAngles = new Vector3(0, 0, ReadFloat(rect["rotationZ"], 0));
        }

        private static void ApplyImage(PackManifest pack, GameObject go, JObject image)
        {
            if (image == null) return;

            string spriteName = (string)image["sprite"] ?? "";
            if (spriteName.Length == 0)
            {
                // Cleared: the picture is removed rather than left as an empty
                // one, which is what the editor means by an empty sprite box.
                var existing = go.GetComponent<Image>();
                if (existing != null) UnityEngine.Object.Destroy(existing);
                return;
            }

            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            var sprite = UiAssets.Sprite(spriteName, pack);
            if (sprite != null) img.sprite = sprite;

            img.type = ParseImageType((string)image["type"]);
            img.color = ParseColor((string)image["tint"], Color.white);
            img.fillCenter = image["fillCenter"] == null || (bool)image["fillCenter"];
            img.preserveAspect = image["preserveAspect"] != null && (bool)image["preserveAspect"];
            img.pixelsPerUnitMultiplier = Mathf.Max(
                0.01f, ReadFloat(image["pixelsPerUnitMultiplier"], 1f));
            if (image["raycastTarget"] != null) img.raycastTarget = (bool)image["raycastTarget"];
        }

        private static void ApplyText(PackManifest pack, GameObject go, JObject text)
        {
            if (text == null) return;

            string value = (string)text["value"] ?? "";
            var existing = go.GetComponent<TextMeshProUGUI>();
            if (value.Length == 0)
            {
                if (existing != null) UnityEngine.Object.Destroy(existing);
                return;
            }

            var tmp = existing ?? go.AddComponent<TextMeshProUGUI>();

            // A label naming a variable follows it, rather than being the value
            // that variable happened to hold when the pack was written. Only
            // where a token actually appears - see UiLiveText.
            if (TextPlaceholders.HasAny(value))
            {
                var live = go.GetComponent<UiLiveText>() ?? go.AddComponent<UiLiveText>();
                live.Raw = value;
                live.PackId = pack != null ? pack.PackId : null;
                tmp.text = TextPlaceholders.Resolve(
                    value, Plugin.TryGetPackVars(live.PackId));
            }
            else
            {
                // Authored plain: drop any follower a previous build left, or a
                // node edited from a token to plain text would keep updating.
                var stale = go.GetComponent<UiLiveText>();
                if (stale != null) UnityEngine.Object.Destroy(stale);
                tmp.text = value;
            }

            string fontName = (string)text["font"] ?? "";
            if (fontName.Length > 0)
            {
                var font = UiAssets.Font(fontName);
                if (font != null) tmp.font = font;
            }

            tmp.fontSize = ReadFloat(text["size"], 36f);
            tmp.color = ParseColor((string)text["color"], Color.white);
            tmp.alignment = ParseAlignment((string)text["alignment"]);
            tmp.enableWordWrapping = text["wrap"] == null || (bool)text["wrap"];
            tmp.lineSpacing = ReadFloat(text["lineSpacing"], 0f);
            tmp.characterSpacing = ReadFloat(text["characterSpacing"], 0f);

            if (text["autoSize"] != null && (bool)text["autoSize"])
            {
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = ReadFloat(text["autoSizeMin"], 12f);
                tmp.fontSizeMax = ReadFloat(text["autoSizeMax"], 72f);
            }
        }

        /// <summary>Opacity, shadow, outline, clipping and behaviour — the parts
        /// that are the same whether the object is new or being changed.</summary>
        private static ManualLogSource Log;

        /// <summary>
        /// Attach the arrival animation an object or a screen asked for, if it
        /// asked for one at all. Absent is the ordinary case: most of the game's
        /// own screens simply appear.
        /// </summary>
        private static void ApplyOpenAnimation(GameObject go, JObject open)
        {
            if (go == null || open == null) return;

            bool fade = open["fade"] != null && (bool)open["fade"];
            Vector3? scaleFrom = Scale(open["scaleFrom"] as JArray);
            if (!fade && !scaleFrom.HasValue) return;    // nothing to play

            var play = go.GetComponent<UiOpenAnimation>() ?? go.AddComponent<UiOpenAnimation>();
            play.Fade = fade;
            play.ScaleFrom = scaleFrom;
            play.Duration = open["duration"] != null ? (float)open["duration"] : 0.3f;
            play.Easing = (string)open["easing"] ?? "QuadInOut";
        }

        /// <summary>
        /// Attach the leaving animation, if there is one. The same four settings
        /// as an arrival, read the other way round: the scale is where it ends
        /// rather than where it starts.
        /// </summary>
        private static void ApplyCloseAnimation(GameObject go, JObject close)
        {
            if (go == null || close == null) return;

            bool fade = close["fade"] != null && (bool)close["fade"];
            Vector3? scaleTo = Scale(close["scaleFrom"] as JArray);
            if (!fade && !scaleTo.HasValue) return;

            var play = go.GetComponent<UiCloseAnimation>() ?? go.AddComponent<UiCloseAnimation>();
            play.Fade = fade;
            play.ScaleTo = scaleTo;
            play.Duration = close["duration"] != null ? (float)close["duration"] : 0.3f;
            play.Easing = (string)close["easing"] ?? "QuadInOut";
        }

        private static Vector3? Scale(JArray a)
            => a != null && a.Count >= 3
             ? new Vector3((float)a[0], (float)a[1], (float)a[2])
             : (Vector3?)null;

        private static void ApplyExtras(PackManifest pack, PackContext ctx, GameObject go, JObject node,
                                        string buttonSound = null)
        {
            ApplyOpenAnimation(go, node["open"] as JObject);
            ApplyCloseAnimation(go, node["close"] as JObject);

            if (node["alpha"] != null)
            {
                var group = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
                group.alpha = Mathf.Clamp01(ReadFloat(node["alpha"], 1f));
            }

            // Shown or hidden by its own conditions from here on, re-checked
            // every frame - the same registry a place's objects use, which is
            // what makes "hide what has already been bought" a property of the
            // item rather than a rule about it.
            var gates = node["activeConditions"] as JArray;
            if (gates != null && gates.Count > 0 && pack != null)
                GameObjectGateRegistry.ForPack(pack.PackId).Register(go, gates, true, go.name);

            // Clickable only where the pack says so. Attached before the
            // graphic checks below because it caches the resting colour in
            // Awake, and the tint has already been applied by now.
            var clicks = node["onClick"] as JArray;
            if (clicks != null && clicks.Count > 0)
            {
                var button = go.GetComponent<UiButtonClick>() ?? go.AddComponent<UiButtonClick>();
                button.Actions = clicks;
                button.Context = ctx;
                button.Conditions = node["clickConditions"] as JArray;

                // This object's own sound if it names one, otherwise whatever
                // the screen says its buttons sound like.
                string ownSound = (string)node["clickSound"];
                button.ClickSound = !string.IsNullOrEmpty(ownSound) ? ownSound : buttonSound;

                string hover = (string)node["hoverTint"];
                if (!string.IsNullOrEmpty(hover))
                    button.HoverTint = ParseColor(hover, Color.white);

                // A click needs something for the raycast to hit. An object
                // with only text on it has a graphic; one with neither would
                // swallow nothing and never be pressed, so it gets a clear
                // image purely to be hittable.
                var graphic = go.GetComponent<Graphic>();
                if (graphic == null)
                {
                    var pad = go.AddComponent<Image>();
                    pad.color = new Color(1f, 1f, 1f, 0f);
                    graphic = pad;
                }
                graphic.raycastTarget = true;
            }

            // Arranging children: handed to Unity's own component rather than
            // positioned here, so the game does the real thing and the preview
            // is the one doing the imitating.
            ApplyLayout(go, node["layout"] as JObject);

            if (node["shadow"] is JObject shadow)
            {
                // Outline derives from Shadow, so an existing Outline would be
                // found by GetComponent<Shadow> and quietly reconfigured into
                // something else. Asked for exactly.
                var s = GetExact<Shadow>(go) ?? go.AddComponent<Shadow>();
                s.effectColor = ParseColor((string)shadow["color"], Color.black);
                s.effectDistance = Vec2(shadow["distance"], new Vector2(1, -1));
            }

            if (node["outline"] is JObject outline)
            {
                var o = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
                o.effectColor = ParseColor((string)outline["color"], Color.black);
                o.effectDistance = Vec2(outline["distance"], new Vector2(1, -1));
            }

            if (node["clipChildren"] != null && (bool)node["clipChildren"]
                && go.GetComponent<RectMask2D>() == null)
                go.AddComponent<RectMask2D>();

            // The pack's existing component vocabulary, unchanged - a fade or a
            // blink means the same thing on a UI object as anywhere else, and a
            // second implementation would be a second set of bugs.
            if (node["components"] is JArray components)
                foreach (var token in components)
                    if (token is JObject component)
                        PackComponentFactory.Apply(go, component, Log);
        }

        /// <summary>A component of exactly this type, not a subclass. Shadow and
        /// Outline are the same shape to GetComponent and very much not the same
        /// thing to look at.</summary>
        private static T GetExact<T>(GameObject go) where T : Component
        {
            foreach (var c in go.GetComponents<T>())
                if (c.GetType() == typeof(T)) return c;
            return null;
        }

        // ── Paths ────────────────────────────────────────────────────

        private static GameObject Walk(Transform root, string path)
        {
            var here = root;
            foreach (string raw in path.Split('/'))
            {
                if (raw.Length == 0) continue;
                string name = Strip(raw, out int nth);
                here = Child(here, name, nth)?.transform;
                if (here == null) return null;
            }
            return here.gameObject;
        }

        /// <summary>The nth child of this name, counting from one. Children are
        /// walked directly rather than via Transform.Find so an inactive one is
        /// still found — most of a vanilla screen is switched off.</summary>
        private static GameObject Child(Transform parent, string name, int nth)
        {
            int seen = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name != name) continue;
                if (++seen == nth) return child.gameObject;
            }
            return null;
        }

        /// <summary>Drop every #n from a path.</summary>
        private static string StripAll(string path)
        {
            var parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++) parts[i] = Strip(parts[i], out _);
            return string.Join("/", parts);
        }

        /// <summary>Split a "Name#2" segment into its name and its number.</summary>
        private static string Strip(string segment, out int nth)
        {
            nth = 1;
            int at = segment.LastIndexOf('#');
            if (at <= 0) return segment;
            if (!int.TryParse(segment.Substring(at + 1), out int parsed)) return segment;
            nth = Mathf.Max(1, parsed);
            return segment.Substring(0, at);
        }

        // ── Reading values ───────────────────────────────────────────

        private static bool Flag(JToken token) => token != null && (bool)token;

        private static Vector2 Vec2(JToken token, Vector2 fallback)
        {
            var a = token as JArray;
            if (a == null || a.Count < 2) return fallback;
            return new Vector2(ReadFloat(a[0], fallback.x), ReadFloat(a[1], fallback.y));
        }

        private static float ReadFloat(JToken token, float fallback)
        {
            if (token == null) return fallback;
            try { return (float)token; } catch { return fallback; }
        }

        private static int ReadInt(JToken token, int fallback)
        {
            if (token == null) return fallback;
            try { return (int)token; } catch { return fallback; }
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            string s = hex.TrimStart('#');
            if (s.Length != 6 && s.Length != 8) return fallback;
            try
            {
                byte r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
                byte a = s.Length == 8
                    ? byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber) : (byte)255;
                return new Color32(r, g, b, a);
            }
            catch { return fallback; }
        }

        private static Image.Type ParseImageType(string name)
        {
            if (string.IsNullOrEmpty(name)) return Image.Type.Simple;
            switch (name.ToLowerInvariant())
            {
                case "sliced": return Image.Type.Sliced;
                case "tiled": return Image.Type.Tiled;
                case "filled": return Image.Type.Filled;
                default: return Image.Type.Simple;
            }
        }

        /// <summary>TextMeshPro's own alignment names, which encode both axes at
        /// once with the vertical first — a bare "Left" is vertically middled.</summary>
        private static TextAlignmentOptions ParseAlignment(string name)
        {
            if (string.IsNullOrEmpty(name)) return TextAlignmentOptions.Center;
            try
            {
                return (TextAlignmentOptions)Enum.Parse(
                    typeof(TextAlignmentOptions), name, ignoreCase: true);
            }
            catch { return TextAlignmentOptions.Center; }
        }
    }

    /// <summary>
    /// The pack's own screens, by id, so an action can open and close them.
    /// </summary>
    internal static class UiRegistry
    {
        private static readonly Dictionary<string, GameObject> Built =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string id, GameObject go)
        {
            if (!string.IsNullOrEmpty(id) && go != null) Built[id] = go;
        }

        public static GameObject Find(string id)
            => !string.IsNullOrEmpty(id) && Built.TryGetValue(id, out var go) && go != null
               ? go : null;

        public static void Clear() => Built.Clear();
    }
}
