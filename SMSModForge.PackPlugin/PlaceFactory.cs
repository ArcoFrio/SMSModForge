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
            // Both of these are for EVERY pack, before the question of whether
            // it has places of its own: a pack with none can still put things
            // on the game's levels - its vanilla extensions, built after this
            // by NavigatorRuntime - and those need them just the same. Both
            // were below the "no places" return, so a pack that only added to
            // the game's levels had every NPC it placed there skipped as "no
            // NPC context", and its conditions never cleared between loads
            // (2026-09-27, a pack placing one NPC in Downtown).

            // The scene is rebuilt on every CoreGameScene load, so drop the
            // previous run's condition gates before they're re-registered
            // against the new GameObjects.
            GameObjectGateRegistry.ResetPack(pack.PackId);

            // NPC placements are nodes in each place's GameObject tree, so the
            // tree walk below builds them inline — no separate NPC pass. The
            // vanilla extensions ask for the same context by the pack's id.
            var npcCtx = NpcFactory.CreateContext(pack, baseBust);

            var places = pack.Places;
            if (places == null || places.Count == 0) return;

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

            // ONE housekeeping roomtalk for the whole pack, not one per place.
            //
            // A place's roomtalk does no place-specific work: it is a clone of
            // the Beach one with its Conditions stripped, so all that survives
            // is the room-entry housekeeping its Trigger performs — Lock-Game
            // true then false, stored-talk = Self (what the vanilla Talk button
            // activates), talk-button = true, After-Talk-Transition = false —
            // ending with SetActive(Self, false). Nothing in that reads which
            // place it belongs to, only one place is ever being entered, and it
            // switches itself off when done, so a single instance serves every
            // place in the pack and is ready for the next entry.
            //
            // It is also NOT where dialogues live any more, so re-entering a
            // room can no longer collide with a pack dialogue hosted on it.
            GameObject sharedRoomTalk = CloneRoomTalk(beachRoomTalk, roomTalkRoot,
                                                      "SMSModForge_" + pack.PackId + "_RoomTalk");

            int built = 0;
            foreach (var p in places)
            {
                try
                {
                    if (BuildOne((JObject)p, pack, level5, mapButtons, sharedRoomTalk, beachLevel, beachButton, npcCtx, logger))
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
            Transform level5, Transform mapButtons, GameObject sharedRoomTalk,
            GameObject beachLevel, GameObject beachButton,
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
            // Optional, and deliberately not part of the required-sprite guard
            // below: a place without one keeps a vanilla, undisplaced backdrop.
            string secondMaskRel = (string)p["secondaryMaskSprite"];

            // maskSprite is deliberately NOT required: an unset mask means the
            // level art does not jiggle, and LoadMaskTexture hands the shader a
            // transparent one. It used to be grounds for skipping the place.
            if (!pack.Has(baseRel) || !pack.Has(secondRel))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Place '" + key + "' in " + pack.PackId + " missing sprite(s) in archive — skipping.");
                return false;
            }

            // Allocate sibling index AFTER everything currently in 5_Levels.
            int absoluteIndex = level5.childCount;
            string goName = absoluteIndex + "_" + internalName;

            // Level
            GameObject level = CloneAndDressLevel(beachLevel, level5, goName, internalName, pack, baseRel, secondRel, maskRel, secondMaskRel,
                                                  parallax, parallaxRev, parallax2, parallax2Rev,
                                                  (int?)p["baseSortingOrder"], (int?)p["secondarySortingOrder"],
                                                  keepAudio, keepSeagulls);
            DestroyComponentByName(level, "Trigger"); // the prototype carries a Trigger we don't want

            // The place's whole GameObject hierarchy: layered sprite objects,
            // containers, the level's own NPCs object, and the NPC placements
            // nested under it — one authored tree, one build pass.
            BuildGameObjectList(p["gameObjects"] as JArray, pack, level, key, logger, npcCtx, objectMasks: (int?)p["objectMasks"] ?? 0);

            // No per-place map button here. Navigation buttons are owned by
            // NavigatorRuntime, which creates one per authored
            // navigatorButtons entry (per source place) with its own
            // click handling + condition gating. A per-destination button
            // on top of those produced visible duplicates — one gated by
            // the navigator graph, one by the host mod's legacy per-frame
            // block — for every destination.

            PlaceRegistry.RegisterPackPlace(pack.PackId, key ?? internalName, absoluteIndex, level, sharedRoomTalk, weatherStr,
                                            p["onEnter"] as JArray, p["onExit"] as JArray);
            return true;
        }

        // ─────────────────────────────── Level construction ────────────

        private static GameObject CloneAndDressLevel(GameObject beach, Transform parent,
            string goName, string internalName, PackManifest pack, string baseRel, string secondRel, string maskRel, string secondMaskRel,
            float parallax, bool parallaxRev, float parallax2, bool parallax2Rev,
            int? baseOrder, int? secondOrder, bool keepAudio, bool keepSeagulls)
        {
            GameObject newLevel = Object.Instantiate(beach, parent);
            newLevel.name = goName;

            // Centre the level on the WORLD origin — not on its parent.
            //
            // 5_Levels is itself at roughly (0.6846, -0.2), so local and world
            // are not the same frame here. A vanilla level's own local offset is
            // what cancels that: 14_Beach sits at local (-0.6846, -0.0569),
            // which lands its art at world X 0 — and world X 0 is what the
            // editor preview draws the backdrop around, and what the camera
            // frames. Cloning Beach inherits that cancellation in X but leaves
            // Y at -0.257, which is a small constant vertical drift; zeroing the
            // LOCAL position instead removes the cancellation entirely and
            // throws the art 0.68 units right, which is much worse.
            //
            // Objects on a level are placed in WORLD space (see ApplyTransform:
            // a level object uses transform.position, not localPosition), so
            // putting the level's own origin at world zero is what makes an
            // authored coordinate mean the same thing in the preview and in the
            // game. Z is left as inherited — it carries the level's depth.
            var lp = newLevel.transform.position;
            newLevel.transform.position = new Vector3(0f, 0f, lp.z);

            SetParallax(newLevel, parallax, parallaxRev, null);

            // The Beach prototype has its main sprite + a secondary sprite as
            // child(1) + an NPCs container as child(2). Mirror Places.cs:
            // rename the secondary GO, scrub the NPCs container, and discard
            // the stray "14_Beach (1)" copy if present.
            if (newLevel.transform.childCount > 1)
            {
                var secondary = newLevel.transform.GetChild(1).gameObject;
                // Named for the PLACE, not the level. It used to take the level's
                // own name — index prefix and all — which made it both a
                // duplicate of its parent's name and impossible to address from
                // a pack, since the index is allocated at build time. Actions
                // like SetSprite need to reach it; everything internal uses
                // GetChild(1) and is unaffected.
                secondary.name = internalName + "_Secondary";
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
            sr.sprite = LoadedArt.Sprite(pack, baseRel, "level|2048x1136|Bilinear",
                () => LoadLevelSprite(pack, baseRel, 2048, 1136, FilterMode.Bilinear));
            // Absent = keep the prototype's own order, so a place that never
            // asked keeps the -10 / -12 it has always had.
            if (baseOrder.HasValue) sr.sortingOrder = baseOrder.Value;
            if (newLevel.transform.childCount > 1)
            {
                var sr2 = newLevel.transform.GetChild(1).GetComponent<SpriteRenderer>();
                if (sr2 != null)
                {
                    sr2.sprite = LoadedArt.Sprite(pack, secondRel, "level|2048x1136|Point",
                        () => LoadLevelSprite(pack, secondRel, 2048, 1136, FilterMode.Point));
                    if (secondOrder.HasValue) sr2.sortingOrder = secondOrder.Value;

                    // Vanilla leaves the backdrop on plain Sprite-Lit-Default,
                    // with no mask and nothing to displace it. A pack that
                    // authors a secondary mask is asking for the backdrop to
                    // move too, so it gets its OWN clone of the level's jiggle
                    // material — its own clone, or the two sprites would share
                    // one mask and the whole point would be lost. Without a
                    // mask authored, the renderer is left exactly as it was.
                    if (pack.Has(secondMaskRel))
                    {
                        Material mat2 = new Material(mat);
                        mat2.SetTexture("_MaskTex", LoadMaskTexture(pack, secondMaskRel));
                        sr2.material = mat2;
                    }
                }
            }
            mat.SetTexture("_MaskTex", LoadMaskTexture(pack, maskRel));
            sr.material = mat;

            newLevel.SetActive(false);
            return newLevel;
        }

        // ─────────────────────────────── Overlay construction ──────────

        /// <summary>
        /// What a pack's new sprite on a level is cloned from (1.9.2): the level's
        /// second child when it is a plain sprite - a picture, perhaps drifting
        /// with the mouse, and nothing else: the back layer of every pack place
        /// and of the game's levels laid out that way. Null otherwise, and the
        /// sprite is made plain (<see cref="MakePlainSprite"/>). Of the game's 146
        /// levels only 39 have a sprite there: the others have a light, a particle
        /// system, a trigger, or no second child at all, and a clone of that (or,
        /// with none, of the level itself, its mask and its triggers) was no
        /// picture - a pack's new sign on the Hiking Path came out a particle
        /// system with no sign.
        /// </summary>
        private static Transform SpritePrototype(Transform level)
        {
            if (level.childCount < 2) return null;
            var second = level.GetChild(1);
            if (second.GetComponent<SpriteRenderer>() == null) return null;
            foreach (var c in second.GetComponents<Component>())
                if (c != null && !(c is Transform) && !(c is SpriteRenderer) && c.GetType().Name != "ParallaxMouseEffect") return null;
            return second;
        }

        /// <summary>A plain sprite object: a SpriteRenderer drawn with the level's own
        /// plain lit material (the first of its sprites using Sprite-Lit-Default),
        /// or URP's (1.9.2).</summary>
        private static GameObject MakePlainSprite(string name, Transform parent, Transform level)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            var material = PlainLitMaterial(level);
            if (material != null) sr.sharedMaterial = material;
            return go;
        }

        private static Material _plainLit;

        /// <summary>The rules the objects being built follow (<c>objectMasks</c> on their place or extension): 0 those
        /// of a pack exported before them. Set by the outermost <see cref="BuildGameObjectList"/>.</summary>
        private static int _objectMasks;

        /// <summary>
        /// How an object's picture moves (after 1.10.0, the author: "a way to add masks to other GOs in the place ...
        /// choose between the jiggle and level masks"). An object is cloned from the level's back layer and so starts
        /// with its material. Under the rules (<c>objectMasks</c> 1), <c>maskKind</c>: <c>none</c> (or no mask) a plain
        /// material where the clone's carries a mask; <c>backLayer</c> the back layer's, as cloned; <c>level</c> the
        /// level's own pass (14_Beach's, whatever the back layer has) with its mask; <c>jiggle</c> the characters'
        /// jiggle (an NPC's) with its mask and settings. Before them (a pack exported then): its own mask on the
        /// clone's material - which moved only when the back layer had a mask - and none keeping the back layer's.
        /// </summary>
        private static void ApplyObjectMask(SpriteRenderer sr, JObject ov, PackManifest pack, GameObject level, NpcFactory.Context npcCtx)
        {
            string maskRel = (string)ov["mask"];
            bool hasMask = !string.IsNullOrEmpty(maskRel) && pack.Has(maskRel);
            string kind = (string)ov["maskKind"];
            if (_objectMasks <= 0 && string.IsNullOrEmpty(kind))
            {
                if (!hasMask) return;
                // Own material so the mask doesn't bleed to siblings.
                var mat = new Material(sr.material);
                mat.SetTexture("_MaskTex", ObjectMaskTexture(pack, maskRel));
                sr.material = mat;
                return;
            }
            if (string.IsNullOrEmpty(kind)) kind = hasMask ? "level" : "none";
            if (kind == "backLayer") return;
            if (kind == "level" && hasMask && LevelPassMaterial() is Material levelPass)
            {
                var mat = new Material(levelPass);
                mat.SetTexture("_MaskTex", ObjectMaskTexture(pack, maskRel));
                sr.material = mat;
                return;
            }
            if (kind == "jiggle" && hasMask && (npcCtx?.JiggleProto ?? JigglePassMaterial()) is Material jigglePass)
            {
                var mat = new Material(jigglePass);
                mat.SetTexture("_MaskTex", ObjectMaskTexture(pack, maskRel));
                if (ov["jiggle"] is JObject j) BustFactory.ApplyJiggle(mat, j);
                sr.material = mat;
                return;
            }
            // Still: the back layer's material only when it moves nothing.
            var cloned = sr.sharedMaterial;
            if (cloned != null && cloned.HasProperty("_MaskTex"))
            {
                var plain = PlainLitMaterial(level.transform);
                if (plain != null) sr.sharedMaterial = plain;
            }
        }

        /// <summary>An object's mask: one per mask while the packs load (LoadedArt), linear (data, not colour), not
        /// readable (only the shader reads a mask, 1.9.0), its nearest texel read.</summary>
        private static Texture2D ObjectMaskTexture(PackManifest pack, string maskRel)
            => LoadedArt.Texture(pack, maskRel, "overlay-mask", () =>
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                byte[] mb = pack.ReadBytes(maskRel);
                if (mb != null) t.LoadImage(mb, true);
                t.filterMode = FilterMode.Point;
                return t;
            });

        private static Material _levelPass, _jigglePass;

        /// <summary>The level's own moving material: the Beach's, which every pack place is made from.</summary>
        private static Material LevelPassMaterial()
        {
            if (_levelPass != null) return _levelPass;
            var beach = TransformExtensions.FindGlobalIncludingInactive("14_Beach");
            var sr = beach != null ? beach.GetComponent<SpriteRenderer>() : null;
            _levelPass = sr != null ? sr.sharedMaterial : null;
            return _levelPass;
        }

        /// <summary>The characters' jiggle material, where an NPC's comes from (a bust's body) - for an extension
        /// built without the pack's NPC context.</summary>
        private static Material JigglePassMaterial()
        {
            if (_jigglePass != null) return _jigglePass;
            var anna = TransformExtensions.FindGlobalIncludingInactive("Anna_Towel");
            var sr = anna != null ? anna.transform.Find("MBase1")?.GetComponent<SpriteRenderer>() : null;
            _jigglePass = sr != null ? sr.sharedMaterial : null;
            return _jigglePass;
        }

        private static Material PlainLitMaterial(Transform level)
        {
            foreach (var sr in level.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.sharedMaterial != null && sr.sharedMaterial.name.StartsWith("Sprite-Lit-Default", System.StringComparison.Ordinal))
                    return sr.sharedMaterial;
            if (_plainLit == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
                if (shader != null) _plainLit = new Material(shader);
            }
            return _plainLit;
        }

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
                                               Transform parentOverride = null, bool localPos = false,
                                               int objectMasks = -1)
        {
            if (nodes == null || nodes.Count == 0 || level == null) return;

            // What a new sprite is made from: the level's back layer when it has
            // a plain one, else nothing - see SpritePrototype.
            Transform proto = SpritePrototype(level.transform);

            // The rules for objects' masks: the place's or extension's (-1: a tree under one of them, as it was).
            int rulesBefore = _objectMasks;
            if (objectMasks >= 0) _objectMasks = objectMasks;
            try
            {
                Transform parent = parentOverride != null ? parentOverride : level.transform;
                foreach (var o in nodes)
                    BuildNodeRecursive((JObject)o, pack, parent, proto, level, ownerLabel, logger, npcCtx, localPos);
            }
            finally { _objectMasks = rulesBefore; }
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
            string copyOf = bind || isNpcRoot ? null : (string)ov["copyOf"];
            // A copy keeps what it was copied with, as a bound object keeps
            // what it is: from here on the two are built alike.
            bool asFound = bind || !string.IsNullOrEmpty(copyOf);

            try
            {
                string name = (string)ov["name"];
                if (string.IsNullOrEmpty(name)) name = "GameObject";
                string spriteRel = (string)ov["sprite"];
                bool hasSprite = !string.IsNullOrEmpty(spriteRel) && pack.Has(spriteRel);

                if (asFound && !bind)
                {
                    // A copy of one of the game's objects, found by its path in
                    // the scene whether it is switched on or not, made here - or
                    // beside the original, for one that is not the place's (an
                    // effect a dialogue switches on by its path). Made at each
                    // load, as the place is. A miss skips it, as a bind does:
                    // the original was moved or renamed.
                    var original = TransformExtensions.ResolveGameObject(copyOf);
                    if (original == null)
                    {
                        logger.LogWarning("[SMSModForge.PackPlugin] '" + placeKey +
                            "': '" + copyOf + "' to copy as '" + name + "' is not in the game's scene - skipping it and its children.");
                        return;
                    }
                    var under = ((bool?)ov["copyBeside"] ?? false) ? original.transform.parent : parent;
                    go = Object.Instantiate(original, under, false);
                    go.name = name;
                }
                else if (isNpcRoot)
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
                else if (hasSprite && proto != null)
                {
                    go = Object.Instantiate(proto.gameObject, parent);
                    go.name = name;
                    // Strip any children that rode along on the prototype clone.
                    for (int i = go.transform.childCount - 1; i >= 0; i--)
                        Object.Destroy(go.transform.GetChild(i).gameObject);
                }
                else if (hasSprite)
                {
                    go = MakePlainSprite(name, parent, level.transform);
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
                bool applyTransform = !asFound || ((bool?)ov["overrideTransform"] ?? false);

                if (applyTransform)
                {
                    // Position: LOCAL inside the NPCs subtree (composes into a
                    // chain) and for a bound object (that's what "this object's
                    // transform" means in a hierarchy it already lives in);
                    // WORLD for level objects (same convention the host mod
                    // used, and what MoveGameObject works in).
                    float x = (float?)ov["x"] ?? 0f;
                    float y = (float?)ov["y"] ?? 0f;
                    if (localPos || asFound) go.transform.localPosition = new Vector3(x, y, 0f);
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
                var sr = asFound ? null : go.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sprite = LoadedArt.Sprite(pack, spriteRel, "overlay", () => LoadOverlaySprite(pack, spriteRel));
                    sr.sortingOrder = (int?)ov["sortingOrder"] ?? 0;
                    float alpha = (float?)ov["startAlpha"] ?? 1f;
                    var c = sr.color; c.a = alpha; sr.color = c;

                    ApplyObjectMask(sr, ov, pack, level, npcCtx);
                }

                // Parallax belongs to the existing object as well — a bound node
                // shouldn't silently disable, or retune, the vanilla level's own
                // effect. A created node clones the prototype's component, so
                // enabling is a matter of leaving it on and dressing it; there is
                // nothing to add.
                // ...unless it asks: a copy's or a bound object's own drift,
                // softened or strengthened (overrideParallax, 1.8.3).
                if (asFound && ((bool?)ov["overrideParallax"] ?? false))
                    SetParallax(go, (float?)ov["parallaxStrength"], null, null);
                if (!asFound)
                {
                    bool parallaxDisabled = (bool?)ov["parallaxDisabled"] ?? true;
                    var pe = go.GetComponent("ParallaxMouseEffect");
                    // An object that is no clone of the back picture - a light, a group, a picture in a level of the
                    // game's with no plain back picture - gets the effect when it asks to drift (after 1.10.0): the
                    // back picture's, copied, or one of its own for a strength of its own. It had none, and stayed
                    // with the level while the editor's preview drifted it - and a light set to drift with the back
                    // picture would not have. The NPCs object too: its Parallax drifted every character in the
                    // preview and none in the game.
                    if (pe == null && !parallaxDisabled)
                        pe = AddDrift(go, proto, ov["parallaxStrength"] != null && ov["parallaxStrength"].Type != JTokenType.Null);
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
                if (!asFound || ((bool?)ov["overrideActive"] ?? false))
                {
                    bool startActive = (bool?)ov["startActive"] ?? true;
                    go.SetActive(startActive);
                }

                // The colour its particles start in - the game's effects, on a
                // copy or on the original. Its own particle system only.
                string particles = (string)ov["particleColor"];
                if (!string.IsNullOrEmpty(particles))
                {
                    var ps = go.GetComponent<ParticleSystem>();
                    if (ps == null)
                        logger.LogWarning("[SMSModForge.PackPlugin] '" + placeKey + "': '" + name +
                            "' has a particle colour but no particles of its own.");
                    else if (BustFactory.TryParseHexColor(particles, out var colour))
                    {
                        var main = ps.main;
                        main.startColor = colour;
                    }
                    else
                        logger.LogWarning("[SMSModForge.PackPlugin] '" + placeKey + "': '" + name +
                            "' has a particle colour '" + particles + "' that is not #RRGGBB or #RRGGBBAA.");
                }

                // Its 2D light (1.10.0): made with an object the pack makes,
                // changed or switched off on one of the game's.
                PackLights.Apply(go, ov, pack, asFound, logger, placeKey);

                // Its effect (1.10.0): made under it, shown with it.
                if (ov["effect"] is JObject effect)
                    EffectRuntime.Attach(go, effect, pack, logger, placeKey + "/" + name);

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

        /// <summary>
        /// Load a level layer. The width and height are the frame the art is
        /// EXPECTED at, not a promise about the file: LoadImage resizes the
        /// texture to the PNG, and <see cref="FittedSprite"/> then scales the
        /// result so a room drawn at some other resolution still fills the
        /// same screen. Cutting the rect at the expected size instead, as
        /// this used to, showed one corner of anything larger.
        /// </summary>
        private static Sprite LoadLevelSprite(PackManifest pack, string rel, int width, int height, FilterMode filter)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = filter;
            byte[] bytes = pack.ReadBytes(rel);
            if (bytes != null) tex.LoadImage(bytes);
            return FittedSprite.Create(tex, width, height, FittedSprite.LevelPpu);
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
        /// <summary>
        /// Load a mask PNG as a texture the jiggle shader can read.
        /// <para/>
        /// Linear, not sRGB: the mask carries displacement amounts rather than
        /// colour, and read as sRGB a painted 0.5 samples as about 0.22, so the
        /// effect stops resembling what was authored. Point-filtered for the
        /// same reason. See BustFactory.LoadTexture.
        /// </summary>
        private static Texture2D LoadMaskTexture(PackManifest pack, string rel)
        {
            // The 256x143 is a placeholder — LoadImage resizes to the PNG's own
            // dimensions, so a mask may be authored at any resolution.
            // No mask authored, or the file is not in the archive: bind the
            // shared transparent one rather than a freshly allocated texture
            // whose contents are undefined.
            if (!MaskTextures.IsAuthored(pack, rel)) return MaskTextures.None();

            // One per mask while the packs load - see LoadedArt.
            return LoadedArt.Texture(pack, rel, "level-mask", () =>
            {
                // The 256x143 is a placeholder — LoadImage resizes to the PNG's own
                // dimensions, so a mask may be authored at any resolution.
                var tex = new Texture2D(256, 143, TextureFormat.RGBA32, false, true);
                byte[] bytes = pack.ReadBytes(rel);
                // Not readable: a mask is only ever drawn by the shader, so the
                // copy in memory beside the graphics card's was never read (1.9.0).
                if (bytes != null) tex.LoadImage(bytes, true);
                tex.filterMode = FilterMode.Point;
                return tex;
            });
        }

        /// <summary>
        /// Give <paramref name="go"/> the level's drift (after 1.10.0): a copy of its back picture's
        /// <c>ParallaxMouseEffect</c> (<paramref name="proto"/>'s, what a new sprite is cloned from), as a clone of it
        /// would have it - or, with no back picture, a new one when the object has a strength of its own. Null when
        /// there is nothing to drift by: the object stays with what it is under, as the editor's preview shows it.
        /// </summary>
        private static Component AddDrift(GameObject go, Transform proto, bool ownStrength)
        {
            var back = proto != null ? proto.GetComponent("ParallaxMouseEffect") : null;
            if (back == null && !ownStrength) return null;
            var type = back != null ? back.GetType() : ParallaxType();
            if (type == null) return null;
            var made = go.AddComponent(type);
            if (made != null && back != null)
                foreach (var name in new[] { "parallaxStrength", "reversed", "isUI" })
                {
                    var field = type.GetField(name);
                    field?.SetValue(made, field.GetValue(back));
                }
            return made;
        }

        /// <summary>
        /// A character set to drift (after 1.10.0): the effect an object of the pack's is given (see
        /// <see cref="AddDrift"/>) - the level's back picture's, unless it has a strength of its own - then its own
        /// strength and reversal on it. An unset reversal is left as the back picture's, as the editor's preview
        /// draws it; a "false" is not reversed (it was read as unset until after 1.10.0, so a character in a place
        /// whose back picture is reversed could not be made to drift forward).
        /// </summary>
        internal static void GiveDrift(GameObject go, GameObject level, float? strength, bool? reversed)
        {
            if (go == null || level == null) return;
            var made = AddDrift(go, SpritePrototype(level.transform), strength.HasValue);
            if (made is Behaviour b) b.enabled = true;
            SetParallax(go, strength, reversed, null);
        }

        private static System.Type _parallaxType;
        private static bool _parallaxLooked;

        /// <summary>The game's <c>ParallaxMouseEffect</c>, found by its name as the rest of this file reaches it.</summary>
        private static System.Type ParallaxType()
        {
            if (_parallaxLooked) return _parallaxType;
            _parallaxLooked = true;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type t = null;
                try { t = asm.GetType("ParallaxMouseEffect"); } catch { }
                if (t != null && typeof(Component).IsAssignableFrom(t)) { _parallaxType = t; break; }
            }
            return _parallaxType;
        }

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
            // every one, the first too, so the new roomtalk is empty. Its
            // Trigger names only itself and objects outside it. Kept until
            // 1.8.1 by a loop that stopped at child 1, the beach's first talk
            // stood in every pack's room talk with nothing to start it - read as
            // one of the game's conversations by anything that reads the scene.
            for (int i = roomTalk.transform.childCount - 1; i >= 0; i--)
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
