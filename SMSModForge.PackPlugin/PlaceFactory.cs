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
        public static void BuildAll(PackManifest pack, ManualLogSource logger)
        {
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

            int built = 0;
            foreach (var p in places)
            {
                try
                {
                    if (BuildOne((JObject)p, pack, level5, mapButtons, roomTalkRoot, beachLevel, beachButton, beachRoomTalk, logger))
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
            ManualLogSource logger)
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
            float parallax = (float?)p["parallaxStrength"] ?? 0.75f;
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
            GameObject level = CloneAndDressLevel(beachLevel, level5, goName, pack, baseRel, secondRel, maskRel, parallax, keepAudio, keepSeagulls);
            DestroyComponentByName(level, "Trigger"); // the prototype carries a Trigger we don't want

            // Extra sprite overlays layered onto the level (Sky / Flash /
            // props, a cameo, …) — authored per place.
            BuildOverlays(p, pack, level, logger);

            // No per-place map button here. Navigation buttons are owned by
            // NavigatorRuntime, which creates one per authored
            // navigatorButtons entry (per source place) with its own
            // click handling + condition gating. A per-destination button
            // on top of those produced visible duplicates — one gated by
            // the navigator graph, one by the host mod's legacy per-frame
            // block — for every destination.

            // RoomTalk
            GameObject roomTalk = CloneRoomTalk(beachRoomTalk, roomTalkRoot, internalName);

            PlaceRegistry.RegisterPackPlace(pack.PackId, key ?? internalName, absoluteIndex, level, roomTalk, weatherStr);
            return true;
        }

        // ─────────────────────────────── Level construction ────────────

        private static GameObject CloneAndDressLevel(GameObject beach, Transform parent,
            string goName, PackManifest pack, string baseRel, string secondRel, string maskRel,
            float parallax, bool keepAudio, bool keepSeagulls)
        {
            GameObject newLevel = Object.Instantiate(beach, parent);
            newLevel.name = goName;

            SetParallax(newLevel, parallax);

            // The Beach prototype has its main sprite + a secondary sprite as
            // child(1) + an NPCs container as child(2). Mirror Places.cs:
            // rename the secondary GO, scrub the NPCs container, and discard
            // the stray "14_Beach (1)" copy if present.
            if (newLevel.transform.childCount > 1)
            {
                var secondary = newLevel.transform.GetChild(1).gameObject;
                secondary.name = goName;
                SetParallax(secondary, parallax);
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
            if (newLevel.transform.childCount > 1)
            {
                var sr2 = newLevel.transform.GetChild(1).GetComponent<SpriteRenderer>();
                if (sr2 != null) sr2.sprite = LoadLevelSprite(pack, secondRel, 2048, 1136, FilterMode.Point);
            }
            var maskTex = new Texture2D(256, 143, TextureFormat.RGBA32, false);
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
        /// Instantiate each authored Extra GameObject under <paramref name="level"/>,
        /// recursing into nested <c>children</c> so the authored hierarchy is
        /// rebuilt as a real transform hierarchy. Mirrors the host mod's level
        /// setup: clone the level's secondary-sprite child (a SpriteRenderer +
        /// ParallaxMouseEffect), swap in the sprite, then apply position /
        /// sorting / parallax / alpha and name it so dialogue actions can target it.
        /// </summary>
        private static void BuildOverlays(JObject p, PackManifest pack, GameObject level, ManualLogSource logger)
        {
            var overlays = p["overlays"] as JArray;
            if (overlays == null || overlays.Count == 0) return;

            // The secondary sprite child is the cleanest prototype (plain sprite
            // + parallax, no level-mask material clone); fall back to the level
            // root if a place somehow has no secondary child.
            Transform proto = level.transform.childCount > 1
                ? level.transform.GetChild(1)
                : level.transform;

            string placeKey = (string)p["key"];
            foreach (var o in overlays)
                BuildOverlayRecursive((JObject)o, pack, level.transform, proto, placeKey, logger);
        }

        /// <summary>
        /// Build one Extra GameObject under <paramref name="parent"/>, then its
        /// nested children under it (recursively). Components are applied after
        /// children exist, so a RandomChildActivator can see them.
        /// </summary>
        private static void BuildOverlayRecursive(JObject ov, PackManifest pack, Transform parent,
                                                   Transform proto, string placeKey, ManualLogSource logger)
        {
            GameObject go = null;
            try
            {
                string name = (string)ov["name"];
                if (string.IsNullOrEmpty(name)) name = "Overlay";
                string spriteRel = (string)ov["sprite"];
                if (string.IsNullOrEmpty(spriteRel) || !pack.Has(spriteRel))
                {
                    logger.LogWarning("[SMSModForge.PackPlugin] Extra GameObject '" + name + "' on place '" +
                        placeKey + "' missing sprite in archive — skipping (its children too).");
                    return;
                }

                go = Object.Instantiate(proto.gameObject, parent);
                go.name = name;
                // Strip any children that rode along on the prototype clone.
                for (int i = go.transform.childCount - 1; i >= 0; i--)
                    Object.Destroy(go.transform.GetChild(i).gameObject);

                // World position (x,y) — same convention the host mod used
                // (transform.position), and what the pan's MoveGameObject works
                // in. Setting world position after parenting means a nested child
                // is placed in world space but still rides along with its parent.
                float x = (float?)ov["x"] ?? 0f;
                float y = (float?)ov["y"] ?? 0f;
                go.transform.position = new Vector3(x, y, go.transform.position.z);

                var sr = go.GetComponent<SpriteRenderer>();
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
                        var maskTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        byte[] mb = pack.ReadBytes(maskRel);
                        if (mb != null) maskTex.LoadImage(mb);
                        maskTex.filterMode = FilterMode.Point;
                        mat.SetTexture("_MaskTex", maskTex);
                        sr.material = mat;
                    }
                }

                bool parallaxDisabled = (bool?)ov["parallaxDisabled"] ?? true;
                if (parallaxDisabled)
                {
                    var pe = go.GetComponent("ParallaxMouseEffect");
                    if (pe is Behaviour b) b.enabled = false;
                }

                bool startActive = (bool?)ov["startActive"] ?? true;
                go.SetActive(startActive);
            }
            catch (System.Exception ex)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Extra GameObject build failed on place '" +
                    placeKey + "': " + ex.Message);
            }

            if (go == null) return;

            // Children first (so components like RandomChildActivator see them)…
            if (ov["children"] is JArray kids)
                foreach (var k in kids)
                    BuildOverlayRecursive((JObject)k, pack, go.transform, proto, placeKey, logger);

            // …then attach + configure this object's utility components.
            if (ov["components"] is JArray comps)
                foreach (var c in comps)
                {
                    try { PackComponentFactory.Apply(go, (JObject)c, logger); }
                    catch (System.Exception ex)
                    {
                        logger.LogWarning("[SMSModForge.PackPlugin] Component on Extra GameObject '" +
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

        private static void SetParallax(GameObject go, float strength)
        {
            var p = go.GetComponent("ParallaxMouseEffect");
            if (p == null) return;
            var f = p.GetType().GetField("parallaxStrength");
            f?.SetValue(p, strength);
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
