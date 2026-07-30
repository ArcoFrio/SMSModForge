using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.IO;
using TMPro;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Builds a custom place: a new level GO cloned from <c>14_Beach</c> with
    /// the pack's sprites swapped in, a map button cloned from the Beach
    /// navigator entry, and a roomtalk node cloned from the Beach roomtalk.
    /// <para/>
    /// The allocated sibling index inside <c>5_Levels</c> is captured at the
    /// moment of construction and recorded into <see cref="PlaceRegistry"/>.
    /// Because every place is appended (rather than inserted), two packs
    /// running in different load orders deterministically get
    /// <em>different</em> sibling indices — no collisions. Navigator buttons
    /// reference targets by name, so the absolute index is irrelevant to the
    /// pack author.
    /// </summary>
    public static class PlaceFactory
    {
        public static void BuildAll(PackManifest pack, GameObject baseBust, ManualLogSource logger)
        {
            var places = pack.Places;
            if (places == null || places.Count == 0) return;

            // The scene is rebuilt on every CoreGameScene load, so drop the
            // previous run's condition gates before they're re-registered
            // against the new GameObjects.
            GameObjectGateRegistry.ResetPack(pack.PackId);

            // NPC placements are nodes in each place's GameObject tree, so the
            // tree walk below builds them inline — no separate NPC pass.
            var npcCtx = NpcFactory.CreateContext(pack, baseBust);

            var level5 = GameObject.Find("5_Levels")?.transform;
            var navigator = GameObject.Find("9_MainCanvas")?.transform.Find("Navigator");
            var mapButtons = navigator?.Find("MapButtons");
            var roomTalkRoot = GameObject.Find("8_Room_Talk")?.transform;
            var beachLevel = level5?.Find("14_Beach")?.gameObject;
            var beachButton = mapButtons?.Find("14_beach")?.gameObject;
            var beachRoomTalk = roomTalkRoot?.Find("Beach")?.gameObject;

            if (level5 == null || mapButtons == null || roomTalkRoot == null ||
                beachLevel == null || beachButton == null || beachRoomTalk == null)
            {
                logger.LogError("[SMSModForge.PackPlugin] PlaceFactory: missing vanilla scene anchors (5_Levels / Navigator/MapButtons/14_beach / 8_Room_Talk/Beach) — aborting place build for pack " + pack.PackId);
                return;
            }

            int built = 0;
            foreach (var p in places)
            {
                try
                {
                    if (BuildOne((JObject)p, pack, level5, mapButtons, roomTalkRoot, beachLevel, beachButton, beachRoomTalk, npcCtx, logger))
                        built++;
                }
                catch (System.Exception ex)
                {
                    logger.LogError("[SMSModForge.PackPlugin] Place build failed for " +
                        (string)((JObject)p)["key"] + " in " + pack.PackId + ": " + ex.Message);
                }
            }

            if (built > 0)
                logger.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId + "' built " + built + " place(s).");
        }

        private static bool BuildOne(JObject p, PackManifest pack,
            Transform level5, Transform mapButtons, Transform roomTalkRoot,
            GameObject beachLevel, GameObject beachButton, GameObject beachRoomTalk,
            NpcFactory.Context npcCtx, ManualLogSource logger)
        {
            string key = (string)p["key"];
            string internalName = (string)p["internalName"];
            if (string.IsNullOrEmpty(internalName)) internalName = key;
            if (string.IsNullOrEmpty(internalName))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Place skipped in " + pack.PackId + " — no key or internalName");
                return false;
            }
            string displayName = (string)p["displayName"] ?? internalName;
            // The depth IS the difference between these two: vanilla pins the
            // main sprite at 0.75 and moves the backdrop at something else
            // (0.1 / 0.5 / 1.5). Driving both from one number, which is all this
            // used to be able to do, slides the level as a single flat card.
            // An absent secondary still does exactly that, so packs written
            // before the split are untouched.
            float parallax = (float?)p["parallaxStrength"] ?? 0.75f;
            bool parallaxRev = (bool?)p["parallaxReversed"] ?? false;
            float parallax2 = (float?)p["parallaxSecondaryStrength"] ?? parallax;
            bool parallax2Rev = (bool?)p["parallaxSecondaryReversed"] ?? false;
            bool keepAudio = (bool?)p["keepAudio"] ?? false;
            bool keepSeagulls = (bool?)p["keepSeagulls"] ?? false;
            string weatherStr = (string)p["weatherType"] ?? "None";

            // Sprite paths are now archive-relative; the loaders below
            // read bytes from the pack instead of a loose file.
            string baseRel   = (string)p["baseSprite"];
            string secondRel = (string)p["secondarySprite"];
            string maskRel   = (string)p["maskSprite"];

            if (!pack.Has(baseRel) || !pack.Has(secondRel) || !pack.Has(maskRel))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Place '" + key + "' in " + pack.PackId + " missing sprite(s) in archive — skipping.");
                return false;
            }

            // Allocate sibling index AFTER everything currently in 5_Levels.
            int absoluteIndex = level5.childCount;
            string goName = absoluteIndex + "_" + internalName;

            // Level
            GameObject level = CloneAndDressLevel(beachLevel, level5, goName, pack, baseRel, secondRel, maskRel,
                                                  parallax, parallaxRev, parallax2, parallax2Rev,
                                                  (int?)p["baseSortingOrder"], (int?)p["secondarySortingOrder"],
                                                  keepAudio, keepSeagulls);
            DestroyComponentByName(level, "Trigger"); // the prototype carries a Trigger we don't want

            // The place's whole GameObject hierarchy: layered sprite objects,
            // containers, the level's own NPCs object, and the NPC placements
            // nested under it — one authored tree, one build pass.
            BuildGameObjectList(p["gameObjects"] as JArray, pack, level, key, logger, npcCtx);

            // No per-place map button here. Navigation buttons are owned by
            // NavigatorRuntime, which creates one per authored
            // navigatorButtons entry (per source place) with its own
            // click handling + condition gating. A per-destination button
            // on top of those produced visible duplicates — one gated by
            // the navigator graph, one by the host mod's legacy per-frame
            // block — for every destination.

            // RoomTalk
            GameObject roomTalk = CloneRoomTalk(beachRoomTalk, roomTalkRoot, internalName);

            PlaceRegistry.RegisterPackPlace(pack.PackId, key ?? internalName, absoluteIndex, level, roomTalk, weatherStr,
                                            p["onEnter"] as JArray, p["onExit"] as JArray);
            return true;
        }

        // ─────────────────────────────── Level construction ────────────

        private static GameObject CloneAndDressLevel(GameObject beach, Transform parent,
            string goName, PackManifest pack, string baseRel, string secondRel, string maskRel,
            float parallax, bool parallaxRev, float parallax2, bool parallax2Rev,
            int? baseOrder, int? secondOrder, bool keepAudio, bool keepSeagulls)
        {
            GameObject newLevel = Object.Instantiate(beach, parent);
            newLevel.name = goName;

            SetParallax(newLevel, parallax, parallaxRev, null);

            // The Beach prototype has its main sprite + a secondary sprite as
            // child(1) + an NPCs container as child(2). Mirror Places.cs:
            // rename the secondary GO, scrub the NPCs container, and discard
            // the stray "14_Beach (1)" copy if present.
            if (newLevel.transform.childCount > 1)
            {
                var secondary = newLevel.transform.GetChild(1).gameObject;
                secondary.name = goName;
                SetParallax(secondary, parallax2, parallax2Rev, null);
            }
            if (newLevel.transform.childCount > 2)
            {
                var npcs = newLevel.transform.GetChild(2).gameObject;
                DestroyComponentByName(npcs, "Actions");
                DestroyComponentByName(npcs, "Conditions");
                DestroyComponentByName(npcs, "DisableChildren");
                for (int i = npcs.transform.childCount - 1; i >= 0; i--)
                    Object.Destroy(npcs.transform.GetChild(i).gameObject);
            }
            for (int i = newLevel.transform.childCount - 1; i >= 0; i--)
            {
                var child = newLevel.transform.GetChild(i);
                if (child.name == "14_Beach (1)") { Object.Destroy(child.gameObject); break; }
            }
            for (int i = newLevel.transform.childCount - 1; i >= 0; i--)
            {
                var c = newLevel.transform.GetChild(i);
                if (!keepAudio && c.name == "Audio Source")
                    c.gameObject.SetActive(false);
                if (!keepSeagulls && c.name == "Particle System (2)")
                    c.gameObject.SetActive(false);
            }

            // Per-level material clone so the mask is not shared across all
            // levels that share the Beach material.
            var sr = newLevel.GetComponent<SpriteRenderer>();
            Material mat = new Material(sr.material);
            sr.sprite = LoadLevelSprite(pack, baseRel, 2048, 1136, FilterMode.Bilinear);
            // Absent = keep the prototype's own order, so a place that never
            // asked keeps the -10 / -12 it has always had.
            if (baseOrder.HasValue) sr.sortingOrder = baseOrder.Value;
            if (newLevel.transform.childCount > 1)
            {
                var sr2 = newLevel.transform.GetChild(1).GetComponent<SpriteRenderer>();
                if (sr2 != null)
                {
                    sr2.sprite = LoadLevelSprite(pack, secondRel, 2048, 1136, FilterMode.Point);
                    if (secondOrder.HasValue) sr2.sortingOrder = secondOrder.Value;
                }
            }
            // linear: the mask carries displacement amounts, not colour. As sRGB
            // a painted 0.5 samples as ~0.22 and the effect stops resembling the
            // authored mask. See BustFactory.LoadTexture.
            var maskTex = new Texture2D(256, 143, TextureFormat.RGBA32, false, true);
            byte[] maskBytes = pack.ReadBytes(maskRel);
            if (maskBytes != null) maskTex.LoadImage(maskBytes);
            maskTex.filterMode = FilterMode.Point;
            mat.SetTexture("_MaskTex", maskTex);
            sr.material = mat;
            sr.material.SetTexture("_MaskTex", maskTex);

            newLevel.SetActive(false);
            return newLevel;
        }

        // ─────────────────────────────── Overlay construction ──────────

        /// <summary>
        /// Public entry point for building an authored <c>gameObjects</c> tree
        /// onto ANY level — used both by pack places and by the vanilla-extension
        /// pass (via <see cref="NavigatorRuntime"/>) to decorate levels the pack
        /// doesn't own. <paramref name="ownerLabel"/> is log-only (the place key,
        /// or the extension's source token). Pass <paramref name="npcCtx"/> to
        /// allow NPC placements in the tree; null skips them.
        /// </summary>
        public static void BuildGameObjectList(JArray nodes, PackManifest pack, GameObject level,
                                               string ownerLabel, ManualLogSource logger,
                                               NpcFactory.Context npcCtx = null,
                                               Transform parentOverride = null, bool localPos = false)
        {
            if (nodes == null || nodes.Count == 0 || level == null) return;

            // The secondary sprite child is the cleanest prototype (plain sprite
            // + parallax, no level-mask material clone); fall back to the level
            // root if a level somehow has no second child. Vanilla levels share
            // this layout — pack places are clones of one.
            Transform proto = level.transform.childCount > 1
                ? level.transform.GetChild(1)
                : level.transform;

            Transform parent = parentOverride != null ? parentOverride : level.transform;
            foreach (var o in nodes)
                BuildNodeRecursive((JObject)o, pack, parent, proto, level, ownerLabel, logger, npcCtx, localPos);
        }

        /// <summary>
        /// Build one GameObject under <paramref name="parent"/>, then its nested
        /// children and NPC placements under it (recursively). Components are
        /// applied last, once children exist, so a RandomChildActivator can see
        /// them. A blank sprite makes a pure container GameObject (no
        /// SpriteRenderer).
        /// <para/>
        /// The node whose <c>role</c> is <c>npcRoot</c> is special: instead of
        /// creating a GameObject it GRAFTS onto the level's built-in <c>NPCs</c>
        /// object (applying its transform and components to what's already
        /// there), and its whole subtree switches to LOCAL positioning so
        /// containers compose down the chain the way a Unity hierarchy does.
        /// Ordinary objects keep WORLD positioning (what MoveGameObject works in).
        /// </summary>
        private static void BuildNodeRecursive(JObject ov, PackManifest pack, Transform parent,
                                               Transform proto, GameObject level, string placeKey,
                                               ManualLogSource logger, NpcFactory.Context npcCtx,
                                               bool localPos = false)
        {
            GameObject go = null;
            bool isNpcRoot = (string)ov["role"] == "npcRoot";
            if (isNpcRoot) localPos = true;   // the NPCs subtree composes locally
            bool bind = (bool?)ov["bind"] ?? false;

            try
            {
                string name = (string)ov["name"];
                if (string.IsNullOrEmpty(name)) name = "GameObject";
                string spriteRel = (string)ov["sprite"];
                bool hasSprite = !string.IsNullOrEmpty(spriteRel) && pack.Has(spriteRel);

                if (isNpcRoot)
                {
                    // The level already owns its NPCs container — adopt it rather
                    // than adding a second one.
                    go = NpcFactory.FindOrMakeContainer(level.transform, "NPCs").gameObject;
                }
                else if (bind)
                {
                    // Bind: the object is claimed to exist already (a vanilla
                    // level's own furniture, typically). Resolve it under this
                    // node's parent — bare name or path — and apply only the
                    // authored delta below. Never create: a miss means the
                    // target moved or was renamed, and quietly adding a
                    // look-alike would hide that.
                    go = TransformExtensions.FindDescendantIncludingInactive(parent, name);
                    if (go == null)
                    {
                        logger.LogWarning("[SMSModForge.PackPlugin] '" + placeKey +
                            "': bound GameObject '" + name + "' not found under '" +
                            parent.name + "' — skipping it and its children.");
                        return;
                    }
                }
                else if (hasSprite)
                {
                    go = Object.Instantiate(proto.gameObject, parent);
                    go.name = name;
                    // Strip any children that rode along on the prototype clone.
                    for (int i = go.transform.childCount - 1; i >= 0; i--)
                        Object.Destroy(go.transform.GetChild(i).gameObject);
                }
                else
                {
                    // Pure container — a plain empty GameObject (no SpriteRenderer).
                    go = new GameObject(name);
                    go.transform.SetParent(parent, false);
                }

                // A bound object keeps everything about itself unless the author
                // explicitly opted into overriding it — otherwise merely listing
                // an existing object in order to hang a child off it would snap
                // it to the origin at native scale.
                bool applyTransform = !bind || ((bool?)ov["overrideTransform"] ?? false);

                if (applyTransform)
                {
                    // Position: LOCAL inside the NPCs subtree (composes into a
                    // chain) and for a bound object (that's what "this object's
                    // transform" means in a hierarchy it already lives in);
                    // WORLD for level objects (same convention the host mod
                    // used, and what MoveGameObject works in).
                    float x = (float?)ov["x"] ?? 0f;
                    float y = (float?)ov["y"] ?? 0f;
                    if (localPos || bind) go.transform.localPosition = new Vector3(x, y, 0f);
                    else go.transform.position = new Vector3(x, y, go.transform.position.z);

                    // Full transforms — apply rotation / scale (defaults leave it
                    // unrotated at native size).
                    float rotZ = (float?)ov["rotationZ"] ?? 0f;
                    if (rotZ != 0f)
                        go.transform.localEulerAngles = new Vector3(0f, 0f, rotZ);
                    float scaleX = (float?)ov["scaleX"] ?? 1f;
                    float scaleY = (float?)ov["scaleY"] ?? 1f;
                    if (scaleX != 1f || scaleY != 1f)
                    {
                        var s = go.transform.localScale;
                        go.transform.localScale = new Vector3(scaleX, scaleY, s.z);
                    }
                }

                // Renderer properties belong to the existing object too, so a
                // bound node never touches them (it has no sprite of its own).
                var sr = bind ? null : go.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sprite = LoadOverlaySprite(pack, spriteRel);
                    sr.sortingOrder = (int?)ov["sortingOrder"] ?? 0;
                    float alpha = (float?)ov["startAlpha"] ?? 1f;
                    var c = sr.color; c.a = alpha; sr.color = c;

                    string maskRel = (string)ov["mask"];
                    if (!string.IsNullOrEmpty(maskRel) && pack.Has(maskRel))
                    {
                        // Own material so the mask doesn't bleed to siblings.
                        Material mat = new Material(sr.material);
                        var maskTex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);   // linear: data, not colour
                        byte[] mb = pack.ReadBytes(maskRel);
                        if (mb != null) maskTex.LoadImage(mb);
                        maskTex.filterMode = FilterMode.Point;
                        mat.SetTexture("_MaskTex", maskTex);
                        sr.material = mat;
                    }
                }

                // Parallax belongs to the existing object as well — a bound node
                // shouldn't silently disable, or retune, the vanilla level's own
                // effect. A created node clones the prototype's component, so
                // enabling is a matter of leaving it on and dressing it; there is
                // nothing to add.
                if (!bind)
                {
                    bool parallaxDisabled = (bool?)ov["parallaxDisabled"] ?? true;
                    var pe = go.GetComponent("ParallaxMouseEffect");
                    if (pe is Behaviour b) b.enabled = !parallaxDisabled;
                    // Nulls inherit: an unset field keeps whatever the cloned
                    // level sprite had, so an overlay in a 0.05 room drifts with
                    // the room instead of snapping to a default of its own.
                    if (!parallaxDisabled)
                        SetParallax(go, (float?)ov["parallaxStrength"],
                                        (bool?)ov["parallaxReversed"],
                                        (bool?)ov["parallaxIsUI"]);
                }

                // Same opt-in rule as the transform: listing an existing object
                // must not switch it on/off unless that was the point.
                if (!bind || ((bool?)ov["overrideActive"] ?? false))
                {
                    bool startActive = (bool?)ov["startActive"] ?? true;
                    go.SetActive(startActive);
                }

                // Authored activation conditions: the object's active state is
                // driven by them from here on (evaluated per frame), instead of
                // staying at whatever startActive said. Registering is enough —
                // the first tick puts it in the right state.
                if (ov["activeConditions"] is JArray gateConds && gateConds.Count > 0)
                    GameObjectGateRegistry.ForPack(pack.PackId).Register(
                        go, gateConds, (bool?)ov["deactivateWhenUnmet"] ?? true,
                        placeKey + "/" + name);
            }
            catch (System.Exception ex)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] GameObject build failed on place '" +
                    placeKey + "': " + ex.Message);
            }

            if (go == null) return;

            // Children first (so components like RandomChildActivator see them)…
            if (ov["children"] is JArray kids)
                foreach (var k in kids)
                    BuildNodeRecursive((JObject)k, pack, go.transform, proto, level, placeKey, logger, npcCtx, localPos);

            // …then the NPC placements parented at this node (also children, so
            // they must exist before components run)…
            if (ov["npcs"] is JArray npcs && npcs.Count > 0)
            {
                if (npcCtx == null)
                {
                    logger.LogWarning("[SMSModForge.PackPlugin] '" + placeKey + "' has NPC placements under '" +
                        go.name + "' but this build path has no NPC context — skipping them.");
                }
                else
                {
                    foreach (var nTok in npcs)
                    {
                        if (!(nTok is JObject pl)) continue;
                        try { NpcFactory.BuildPlacement(pl, npcCtx, go.transform, level, logger); }
                        catch (System.Exception ex)
                        {
                            logger.LogError("[SMSModForge.PackPlugin] NPC build failed for '" +
                                (string)pl["npc"] + "' on place '" + placeKey + "': " + ex.Message);
                        }
                    }
                }
            }

            // …then attach + configure this object's utility components.
            if (ov["components"] is JArray comps)
                foreach (var c in comps)
                {
                    try { PackComponentFactory.Apply(go, (JObject)c, logger); }
                    catch (System.Exception ex)
                    {
                        logger.LogWarning("[SMSModForge.PackPlugin] Component on GameObject '" +
                            go.name + "' failed: " + ex.Message);
                    }
                }
        }

        /// <summary>Load an overlay sprite at its own native pixel size (the
        /// PNG dimensions), at the level pixels-per-unit so it scales to match.</summary>
        private static Sprite LoadOverlaySprite(PackManifest pack, string rel)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            byte[] bytes = pack.ReadBytes(rel);
            if (bytes != null) tex.LoadImage(bytes); // auto-resizes to the image's dimensions
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 70.32f);
        }

        private static Sprite LoadLevelSprite(PackManifest pack, string rel, int width, int height, FilterMode filter)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = filter;
            byte[] bytes = pack.ReadBytes(rel);
            if (bytes != null) tex.LoadImage(bytes);
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 70.32f);
        }

        /// <summary>
        /// Push settings onto an object's existing <c>ParallaxMouseEffect</c>.
        /// These three fields are everything the effect serializes.
        /// <para/>
        /// A null argument LEAVES THAT FIELD ALONE, which is what makes an older
        /// pack behave identically: an object is cloned from the level's own
        /// sprite, component and all, so inheriting is the meaningful default
        /// and every setting here is an override on top of it.
        /// <para/>
        /// Set by reflection because the component belongs to the game, not to
        /// us. The names come straight from the extraction, and a field that
        /// isn't found is skipped rather than throwing, so a future build that
        /// renames one degrades instead of breaking the level.
        /// </summary>
        private static void SetParallax(GameObject go, float? strength, bool? reversed, bool? isUI)
        {
            var p = go.GetComponent("ParallaxMouseEffect");
            if (p == null) return;
            var t = p.GetType();
            if (strength.HasValue) t.GetField("parallaxStrength")?.SetValue(p, strength.Value);
            if (reversed.HasValue) t.GetField("reversed")?.SetValue(p, reversed.Value);
            if (isUI.HasValue) t.GetField("isUI")?.SetValue(p, isUI.Value);
        }

        // ─────────────────────────────── Button construction ───────────

        // ─────────────────────────────── RoomTalk construction ─────────

        private static GameObject CloneRoomTalk(GameObject beachRoomTalk, Transform roomTalkRoot, string internalName)
        {
            GameObject roomTalk = Object.Instantiate(beachRoomTalk, roomTalkRoot);
            roomTalk.name = internalName;
            // The Beach roomtalk carries scene-specific dialogue children; strip
            // them so the new roomtalk is empty (the pack author can wire their
            // own dialogues later — and the current dialogue editor isn't
            // shipping yet anyway).
            for (int i = roomTalk.transform.childCount - 1; i > 0; i--)
                Object.Destroy(roomTalk.transform.GetChild(i).gameObject);
            DestroyComponentByName(roomTalk, "Conditions");
            return roomTalk;
        }

        // ─────────────────────────────── Helpers ───────────────────────

        private static void DestroyComponentByName(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name == typeName) { Object.Destroy(c); return; }
            }
        }
    }
}
