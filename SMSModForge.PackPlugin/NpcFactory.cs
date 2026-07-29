using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Builds a pack's level NPCs. An NPC is a slim bust that lives inside a
    /// place's <c>NPCs</c> hierarchy instead of the bust manager: a jiggle-
    /// material sprite, an optional eyes-closed <c>Blink</c> overlay sharing
    /// that material, an optional procedural <c>Circle</c> shadow, and an
    /// optional <c>Wet</c> particle clone.
    /// <para/>
    /// Placements are nodes in the place's GameObject tree (<c>npcs</c> on any
    /// <c>gameObjects</c> node), so the same <c>NpcDef</c> can appear in several
    /// rooms and its parent chain is simply where it sits in that tree.
    /// <see cref="PlaceFactory"/> walks the tree and calls
    /// <see cref="BuildPlacement"/> for each one, so there's no separate NPC
    /// pass. The jiggle material is cloned from the same base bust
    /// <see cref="BustFactory"/> uses, so no shader ships in the pack.
    /// </summary>
    public static class NpcFactory
    {
        /// <summary>Per-pack state every placement build needs: the pack's NPC
        /// definitions by key, and the shared jiggle material to clone.</summary>
        public sealed class Context
        {
            public Dictionary<string, JObject> Defs;
            public Material JiggleProto;
            public PackManifest Pack;
        }

        /// <summary>Index the pack's NpcDefs and capture the jiggle material once.
        /// Null <paramref name="baseBust"/> just means no jiggle material.</summary>
        public static Context CreateContext(PackManifest pack, GameObject baseBust)
        {
            var defs = new Dictionary<string, JObject>();
            if (pack.Root["npcs"] is JArray npcArr)
                foreach (var n in npcArr)
                    if (n is JObject no && !string.IsNullOrEmpty((string)no["key"]))
                        defs[(string)no["key"]] = no;

            var mBaseSr = baseBust?.transform.Find("MBase1")?.GetComponent<SpriteRenderer>();
            return new Context
            {
                Defs = defs,
                JiggleProto = mBaseSr != null ? mBaseSr.sharedMaterial : null,
                Pack = pack,
            };
        }

        /// <summary>Build one NPC placement as a child of <paramref name="parent"/>
        /// (the GameObject tree node it's authored under). <paramref name="level"/>
        /// is the host level, needed to build any GameObjects parented under the
        /// NPC (they clone the level's sprite prototype).</summary>
        public static bool BuildPlacement(JObject pl, Context ctx, Transform parent,
                                          GameObject level, ManualLogSource logger)
        {
            var defs = ctx.Defs;
            var pack = ctx.Pack;
            var jiggleProto = ctx.JiggleProto;

            string npcKey = (string)pl["npc"];
            if (string.IsNullOrEmpty(npcKey) || !defs.TryGetValue(npcKey, out var def))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] NPC placement references unknown NPC '" + npcKey + "' — skipping.");
                return false;
            }

            string spriteRel = (string)def["sprite"];
            if (!pack.Has(spriteRel))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] NPC '" + npcKey + "' sprite missing in archive — skipping.");
                return false;
            }

            string goName = (string)pl["name"];
            if (string.IsNullOrEmpty(goName)) goName = npcKey;

            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            // Part transforms all live on the PLACEMENT now (body / shadow /
            // blink / wet); the def carries only non-positional properties.
            ApplyTransform(go.transform, pl["body"] as JObject);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = MakeSprite(pack, spriteRel);
            sr.sortingOrder = (int?)def["sortingOrder"] ?? -1;

            // Per-NPC material clone: jiggle uniforms + this NPC's mask.
            if (jiggleProto != null)
            {
                var mat = new Material(jiggleProto);
                string maskRel = (string)def["mask"];
                if (pack.Has(maskRel)) mat.SetTexture("_MaskTex", LoadTex(pack, maskRel, linear: true));
                if (def["jiggle"] is JObject j) BustFactory.ApplyJiggle(mat, j);
                sr.material = mat;
            }

            BuildReflection(go.transform, def["reflection"] as JObject, sr);
            BuildBlink(go.transform, def["blink"] as JObject, pl["blink"] as JObject, pack, sr);
            BuildShadow(go.transform, def["shadow"] as JObject, pl["shadow"] as JObject);
            BuildWet(go.transform, def["wet"] as JObject, pl["wet"] as JObject);

            // Authored GameObjects parented under the NPC (props that ride along
            // with the pose), built LOCAL so they compose with the body.
            if (pl["children"] is JArray kids && kids.Count > 0 && level != null)
                PlaceFactory.BuildGameObjectList(kids, pack, level, goName, logger,
                                                 ctx, go.transform, localPos: true);

            // Components go on BEFORE activation, so an authored FadeInSprite
            // runs its fade when the NPC is switched on rather than at build
            // time (nothing fades by default — this is the opt-in).
            if (pl["components"] is JArray comps)
                foreach (var c in comps)
                {
                    try { PackComponentFactory.Apply(go, (JObject)c, logger); }
                    catch (System.Exception ex)
                    {
                        logger.LogWarning("[SMSModForge.PackPlugin] Component on NPC '" +
                            go.name + "' failed: " + ex.Message);
                    }
                }

            bool startActive = (bool?)pl["startActive"] ?? false;
            go.SetActive(startActive);
            return true;
        }

        // ── children ────────────────────────────────────────────────────

        /// <summary>Eyes-closed overlay. Shares the parent's material so the
        /// jiggle distortion lines up with the face; +1 sorting order. Uses the
        /// <summary>
        /// A downward mirror of the pose — the floor-reflection pattern several
        /// vanilla levels build by hand as a child holding the same sprite at
        /// scale (1, -1).
        /// <para/>
        /// Shares the parent's sprite and material rather than loading anything,
        /// so it costs one renderer and no extra texture: it IS the pose, drawn
        /// again upside down. Vanilla puts these in front of the body, which is
        /// what makes them read as lying on the floor rather than behind it.
        /// </summary>
        private static void BuildReflection(Transform npc, JObject reflDef, SpriteRenderer parentSr)
        {
            if (reflDef == null || parentSr == null) return;
            if ((bool?)reflDef["enabled"] != true) return;

            var go = new GameObject("Reflection");
            go.transform.SetParent(npc, false);
            // Flip on Y about the pose's origin, then drop by the authored
            // offset. Local, so it follows the body's own scale and rotation.
            go.transform.localScale = new Vector3(1f, -1f, 1f);
            go.transform.localPosition = new Vector3(0f, F(reflDef, "offsetY", 0f), 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = parentSr.sprite;
            sr.sharedMaterial = parentSr.sharedMaterial;   // same jiggle material
            sr.sortingOrder = (int?)reflDef["sortingOrder"] ?? 0;

            var c = sr.color;
            c.a = Mathf.Clamp01(F(reflDef, "alpha", 0.35f));
            sr.color = c;
        }

        /// generic BlinkingSprite component (random open, brief close).</summary>
        private static void BuildBlink(Transform npc, JObject blinkDef, JObject blinkTr, PackManifest pack, SpriteRenderer parentSr)
        {
            string rel = (string)blinkDef?["sprite"];
            if (blinkDef == null || string.IsNullOrEmpty(rel) || !pack.Has(rel)) return;

            var go = new GameObject("Blink");
            go.transform.SetParent(npc, false);
            ApplyTransform(go.transform, blinkTr);   // transform from the placement

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = MakeSprite(pack, rel);
            sr.sharedMaterial = parentSr.sharedMaterial;   // same jiggle material
            sr.sortingOrder = parentSr.sortingOrder + 1;

            // Eyes open for a random minWait..maxWait, then shut for hold.
            // This used to collapse to BlinkingSprite's even pulse at the
            // average of the two waits, which threw away the authored hold and
            // left the eyes CLOSED for seconds at a time — the editor preview
            // has always run the real two-phase cycle, so the two disagreed.
            var comp = go.AddComponent<BlinkingSprite>();
            comp.ConfigureBlink(F(blinkDef, "minWait", 2f),
                                F(blinkDef, "maxWait", 5f),
                                F(blinkDef, "hold", 0.2f),
                                0f, 1f);
        }

        /// <summary>Procedural soft floor shadow — a flat dark circle deformed
        /// entirely by its (placement) transform. Colour + order come from the
        /// def; offset / rotation / squash from the placement.</summary>
        private static void BuildShadow(Transform npc, JObject shadowDef, JObject shadowTr)
        {
            if (shadowDef != null && (bool?)shadowDef["enabled"] == false) return;

            var go = new GameObject("Circle");
            go.transform.SetParent(npc, false);
            ApplyTransform(go.transform, shadowTr);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CircleSprite();
            sr.sortingOrder = (int?)shadowDef?["sortingOrder"] ?? -3;
            if (BustFactory.TryParseHexColor((string)shadowDef?["color"] ?? "#00000082", out var col))
                sr.color = col;
        }

        /// <summary>The "Wet" droplet particle, cloned from the same vanilla
        /// source busts use, at its (placement) emitter transform.</summary>
        private static void BuildWet(Transform npc, JObject wetDef, JObject wetTr)
        {
            if (wetDef != null && (bool?)wetDef["enabled"] == false) return;

            var anna = TransformExtensions.FindGlobalIncludingInactive("Anna_Towel");
            var source = anna?.transform.Find("MBase1")?.Find("Particle System")?.gameObject;
            if (source == null) return;

            var p = Object.Instantiate(source, npc);
            p.name = "Wet";
            ApplyTransform(p.transform, wetTr);
            p.SetActive(wetDef == null || (bool?)wetDef["startActive"] != false);
        }

        /// <summary>Apply an NpcTransform JObject (x/y/z, rotX/Y/Z, scaleX/Y/Z)
        /// to a transform; identity when null. Scale defaults to 1.</summary>
        private static void ApplyTransform(Transform t, JObject tr)
        {
            SetLocalTRS(t,
                F(tr, "x"), F(tr, "y"), F(tr, "z"),
                F(tr, "rotX"), F(tr, "rotY"), F(tr, "rotZ"),
                F(tr, "scaleX", 1f), F(tr, "scaleY", 1f), F(tr, "scaleZ", 1f));
        }

        // ── shared helpers ──────────────────────────────────────────────

        /// <summary>Find a named child transform, creating an empty GameObject for
        /// it if absent. Used for the level's built-in <c>NPCs</c> container.</summary>
        public static Transform FindOrMakeContainer(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>Apply an NpcTransform-shaped JObject to a transform (public so
        /// the tree builder can offset the grafted NPCs container).</summary>
        public static void ApplyLocalTransform(Transform t, JObject tr) => ApplyTransform(t, tr);

        private static void SetLocalTRS(Transform t,
            float x, float y, float z, float rx, float ry, float rz, float sx, float sy, float sz)
        {
            t.localPosition = new Vector3(x, y, z);
            t.localEulerAngles = new Vector3(rx, ry, rz);
            t.localScale = new Vector3(sx, sy, sz);
        }

        private static Sprite MakeSprite(PackManifest pack, string rel)
        {
            var tex = LoadTex(pack, rel);
            // Full-texture rect, centre pivot, 100 px/unit — matches the
            // reference NPC/blink sprites (variable resolution, centred).
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                 new Vector2(0.5f, 0.5f), 100f);
        }

        /// <param name="linear">True for a data texture (a jiggle mask, whose
        /// channels are displacement amounts). Colour sprites stay sRGB. See
        /// BustFactory.LoadTexture for why this matters.</param>
        private static Texture2D LoadTex(PackManifest pack, string rel, bool linear = false)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            byte[] bytes = pack.ReadBytes(rel);
            if (bytes != null) tex.LoadImage(bytes);   // resizes to the PNG's real dimensions
            tex.filterMode = FilterMode.Point;
            return tex;
        }

        // One shared hard-edged white circle, tinted per shadow via the
        // SpriteRenderer colour. 256×256 @ 100 px/unit, matching Circle_0.
        private static Sprite _circle;
        private static Sprite CircleSprite()
        {
            if (_circle != null) return _circle;
            const int R = 128;
            var tex = new Texture2D(R * 2, R * 2, TextureFormat.RGBA32, false);
            var px = new Color32[R * 2 * R * 2];
            for (int y = 0; y < R * 2; y++)
                for (int x = 0; x < R * 2; x++)
                {
                    float dx = x - R + 0.5f, dy = y - R + 0.5f;
                    bool inside = dx * dx + dy * dy <= (R - 0.5f) * (R - 0.5f);
                    px[y * R * 2 + x] = new Color32(255, 255, 255, (byte)(inside ? 255 : 0));
                }
            tex.SetPixels32(px);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _circle = Sprite.Create(tex, new Rect(0, 0, R * 2, R * 2), new Vector2(0.5f, 0.5f), 100f);
            return _circle;
        }

        private static float F(JObject o, string key, float fallback = 0f)
            => (float?)o?[key] ?? fallback;
    }
}
