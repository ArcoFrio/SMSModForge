using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Standalone bust factory. Mirrors what
    /// <c>a host bust factory</c> does,
    /// but without referencing the host mod — the plugin clones
    /// <c>Anna_YellowSexy</c> (the vanilla bust prototype) and applies the
    /// per-outfit textures + shader uniforms directly.
    /// <para/>
    /// The bust hierarchy laid down by the base game:
    /// <code>
    /// &lt;new bust&gt;
    ///   ├── MBase1                 [SpriteRenderer + JiggleSprite material]
    ///   │   ├── Blink              [SpriteRenderer]
    ///   │   ├── Mouth
    ///   │   │   ├── 1..4           [SpriteRenderer per mouth frame]
    ///   │   ├── Expressions
    ///   │   │   ├── Happy / Angry / Sad / Flirty   [SpriteRenderer each]
    ///   │   └── Particle System    [the "Wet" droplet preset, copied from Anna_Towel]
    /// </code>
    /// </summary>
    public static class BustFactory
    {
        private static readonly string[] ExpressionNames = { "Happy", "Angry", "Sad", "Flirty" };

        /// <summary>
        /// Builds every outfit on every character in the pack. Failures on a
        /// single outfit are logged and skipped — one bad PNG doesn't stop
        /// the rest of the pack from loading.
        /// </summary>
        public static void BuildAll(PackManifest pack, Transform bustManager, GameObject baseBust, ManualLogSource logger)
        {
            var characters = pack.Characters;
            if (characters == null) return;

            int outfitCount = 0;
            foreach (var ch in characters)
            {
                var charObj = (JObject)ch;
                string charName = (string)charObj["name"];
                if (string.IsNullOrEmpty(charName)) continue;

                // Only a pack-drawn character has busts to build. A vanilla one
                // carries outfits too, but they are the game's OWN bust names
                // with no art behind them — building those would create empty
                // GameObjects named after real busts and collide with them
                // under 2_Bust_Manager, which is far worse than doing nothing.
                string source = (string)charObj["bustSource"];
                if (source == "Vanilla" || source == "None") continue;

                var outfits = charObj["outfits"] as JArray;
                if (outfits == null) continue;

                foreach (var o in outfits)
                {
                    try
                    {
                        var go = BuildOne((JObject)o, pack, bustManager, baseBust, logger);
                        if (go != null) outfitCount++;
                    }
                    catch (System.Exception ex)
                    {
                        logger.LogError("[SMSModForge.PackPlugin] Bust build failed for " +
                            (string)((JObject)o)["key"] + " in " + pack.PackId + ": " + ex.Message);
                    }
                }
            }

            if (outfitCount > 0)
                logger.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId + "' built " + outfitCount + " outfit(s).");
        }

        private static GameObject BuildOne(JObject o, PackManifest pack, Transform bustManager, GameObject baseBust, ManualLogSource logger)
        {
            string goName = (string)o["gameObjectName"] ?? (string)o["key"];
            if (string.IsNullOrEmpty(goName)) return null;

            // Asset paths are stored as archive-relative strings; resolve
            // through the .smspack instead of File.Exists. We keep the
            // relative form for the inner sprite/mouth/expression loaders
            // so the same path round-trips into ReadBytes below.
            string baseRel  = (string)o["baseSprite"];
            string maskRel  = (string)o["maskSprite"];
            string blinkRel = (string)o["blinkSprite"];

            // Blink is optional per outfit. Without the flag a bust with no
            // blink art was skipped entirely, so "I have no blink frame" and
            // "my outfit is broken" were the same thing to this check.
            bool blinkEnabled = (bool?)o["blinkEnabled"] ?? true;

            // maskSprite is deliberately absent from this list: an outfit with
            // no mask simply does not jiggle. Requiring it meant a bust with no
            // mask art never reached the game at all.
            if (!pack.Has(baseRel) || (blinkEnabled && !pack.Has(blinkRel)))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Skipping " + goName + " — base sprite" + (blinkEnabled ? " or blink frame" : "") + " missing in archive.");
                return null;
            }

            var mouth = (JObject)o["mouth"] ?? new JObject();
            var expr  = (JObject)o["expression"] ?? new JObject();
            bool mouthEnabled = (bool?)mouth["enabled"] ?? true;
            bool exprEnabled  = (bool?)expr["enabled"] ?? true;
            string mouthPrefix = (string)mouth["prefix"];
            string exprPrefix  = (string)expr["prefix"];

            GameObject newBust = Object.Instantiate(baseBust, bustManager);
            newBust.name = goName;

            // The sprite root is NOT consistently named across rigs: MBase1 on
            // most busts, D1Base on others. ActorRegistry.FindMBase is the one
            // place that convention lives (named lookup, else first child) and
            // every other bust-child lookup already goes through it — so this
            // does too rather than hardcoding a name that only matches some
            // characters. The overlay branches are then optional: a rig without
            // a Mouth or Expressions group is valid, and used to throw here.
            Transform mBaseT = ActorRegistry.FindMBase(newBust);
            if (mBaseT == null)
            {
                logger?.LogError("[SMSModForge.PackPlugin] Bust " + goName +
                                 " has no sprite root child — skipping.");
                Object.Destroy(newBust);
                return null;
            }
            GameObject mBase = mBaseT.gameObject;
            GameObject blink = mBaseT.Find("Blink")?.gameObject;
            GameObject mouthGo = mBaseT.Find("Mouth")?.gameObject;
            GameObject expressions = mBaseT.Find("Expressions")?.gameObject;

            // Per-outfit material clone so shader uniforms / mask are not shared
            // between outfits. Built fully BEFORE being assigned: reading back
            // .material after assigning re-instantiates, which is how the mask
            // and jiggle ended up on a copy nothing else could reach.
            Material mat = new Material(mBase.GetComponent<SpriteRenderer>().sharedMaterial);

            ApplySprite(mBase.GetComponent<SpriteRenderer>(), pack, baseRel);
            if (blink != null)
            {
                if (blinkEnabled)
                {
                    var blinkSr = blink.GetComponent<SpriteRenderer>();
                    if (blinkSr != null) ApplySprite(blinkSr, pack, blinkRel);
                }
                else
                {
                    // Emptied as well as switched off. Belt and braces: if
                    // anything ever re-activates this child, it has nothing of
                    // the prototype's left to show.
                    var blinkSr = blink.GetComponent<SpriteRenderer>();
                    if (blinkSr != null) blinkSr.sprite = null;

                    // Turn the object OFF rather than stripping its renderer.
                    //
                    // Stripping was the first attempt at this and it crashed
                    // every blinkless character. Unlike the Mouth and Expression
                    // slots below, the prototype's Blink child carries the
                    // game's own BlinkingSprite driver, and that driver's Awake
                    // caches the SpriteRenderer and dereferences it. Awake does
                    // not run at build time — it runs the first time the bust is
                    // activated, which is mid-dialogue, long after the renderer
                    // was destroyed. So the bust built fine, loaded fine, and
                    // threw a NullReferenceException at the moment it appeared.
                    //
                    // An inactive GameObject never runs Awake at all, so this
                    // removes the whole class of problem instead of this one
                    // component's version of it. Nothing is destroyed: the
                    // driver and the renderer stay as the prototype had them and
                    // simply never wake up.
                    blink.SetActive(false);
                }
            }

            Texture2D maskTex = MaskTextures.IsAuthored(pack, maskRel)
                ? LoadTexture(pack, maskRel, linear: true)
                : MaskTextures.None();
            mat.SetTexture("_MaskTex", maskTex);

            var jiggle = (JObject)o["jiggle"];
            if (jiggle != null) ApplyJiggle(mat, jiggle);

            mBase.GetComponent<SpriteRenderer>().sharedMaterial = mat;

            // Mouth frames — load 1..4 if enabled, otherwise empty the slots.
            // The "prefix" carried in the manifest is an archive-relative
            // stem (e.g. "Sprites/MyChar/Mouth_"); we suffix 1.PNG..4.PNG and
            // look the result up in the archive.
            for (int i = 1; i <= 4; i++)
            {
                var slot = mouthGo != null ? mouthGo.transform.Find(i.ToString()) : null;
                if (slot == null) continue;
                var sr = slot.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                // ApplySprite empties the slot when the frame is absent, so a
                // half-authored mouth shows gaps rather than the prototype's
                // remaining frames.
                if (mouthEnabled && !string.IsNullOrEmpty(mouthPrefix))
                    ApplySprite(sr, pack, mouthPrefix + i + ".PNG");
                else
                    sr.sprite = null;
            }

            // Expression overlays — same pattern as mouth.
            foreach (var name in ExpressionNames)
            {
                var slot = expressions != null ? expressions.transform.Find(name) : null;
                if (slot == null) continue;
                var sr = slot.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                if (exprEnabled && !string.IsNullOrEmpty(exprPrefix))
                    ApplySprite(sr, pack, exprPrefix + name + ".PNG");
                else
                    sr.sprite = null;
            }

            // Overlays keep their OWN Sprite-Lit-Default material and are moved
            // on the CPU instead of being given the jiggle shader. The shader
            // route recolours them: its lit pass runs a 193-tap "fake GI" gather
            // weighted by (1 - alpha), so a sprite that fills 0.02-0.26% of its
            // canvas is lit as though floating in void, where the body fills
            // 27.5%. That gather is present in all 64 lit variants and the URP
            // 2D renderer only dispatches the lit pass (Universal2D; the
            // UniversalForward pass exists but is never drawn), so it can be
            // neither keyword-gated nor bypassed. See OverlayJiggle.
            // Once per session: confirms the body is on the jiggle shader and the
            // overlays are still on their own. If an overlay ever shows up here
            // carrying the jiggle shader, the colour shift is back.
            LogOverlayShadersOnce(mBase, blink, mouthGo, expressions, logger);

            // OFF by default — vanilla behaviour, where only the body sprite is
            // displaced and the blink / mouth / expression ride along on it
            // unmoved. Both ways of making them move turned out worse than the
            // problem: the jiggle material recolours them (its lit pass gathers
            // the sprite's own texture weighted by 1-alpha, so art covering a
            // fraction of a percent of its canvas is lit as if floating in
            // void), and the per-object offset below travels only ~3.8 texels,
            // which is roughly 8 distinct positions per bounce and reads as
            // stepping rather than motion. A pack can still opt in per outfit
            // with "applyToOverlays": true.
            if ((bool?)jiggle?["applyToOverlays"] ?? false)
                AttachOverlayJiggle(mBaseT, maskTex, jiggle);

            // Drop the GC2 Conditions/Trigger components on Expressions so they
            // don't fire vanilla behaviour. Found by name to avoid a hard ref
            // to GameCreator.Runtime.Core for those types.
            DestroyComponentByName(expressions, "Conditions");
            DestroyComponentByName(expressions, "Trigger");

            // Particles — default Wet preset (cloned from Anna_Towel).
            var particles = o["particles"] as JArray;
            if (particles == null || particles.Count == 0)
            {
                AttachWetParticle(mBase, "Wet");
            }
            else
            {
                foreach (var p in particles)
                {
                    string preset = (string)((JObject)p)["preset"] ?? "Wet";
                    string pname  = (string)((JObject)p)["name"];
                    bool active   = (bool?)((JObject)p)["active"] ?? false;
                    if (string.Equals(preset, "Wet", System.StringComparison.OrdinalIgnoreCase))
                        AttachWetParticle(mBase, string.IsNullOrEmpty(pname) ? "Wet" : pname, active);
                    // Future presets ("Custom" particle JSON) land here.
                }
            }

            // Tell the SpriteManager about the new bust (otherwise the game's
            // sprite cache rebuild won't include it). Done via reflection so
            // we don't depend on the SpriteManager type at compile time.
            var spriteManager = bustManager.GetComponent("SpriteManager");
            if (spriteManager != null)
            {
                var targetField = spriteManager.GetType().GetField("targetObjects");
                if (targetField != null)
                {
                    var list = targetField.GetValue(spriteManager) as System.Collections.IList;
                    list?.Add(newBust);
                }
            }

            newBust.SetActive(false);
            return newBust;
        }

        /// <summary>
        /// Put the pack's sprite in this slot, or EMPTY the slot when the pack
        /// has none for it.
        /// <para/>
        /// Emptying is the part that matters. Every bust is a clone of the
        /// vanilla prototype, so each slot arrives carrying that character's
        /// art. This used to return early when the path was blank or missing
        /// from the archive, which left the prototype's own blink frame, mouth
        /// or expression showing on somebody else's character — art the author
        /// never chose and would not recognise.
        /// <para/>
        /// A SpriteRenderer with a null sprite draws nothing while staying a
        /// live component, which is what the overlay walk and the sorting pass
        /// both expect to find. Destroying it instead is what broke blink: the
        /// prototype's BlinkingSprite driver outlived its renderer and threw on
        /// the first activation.
        /// </summary>
        private static void ApplySprite(SpriteRenderer sr, PackManifest pack, string rel)
        {
            if (sr == null) return;
            if (string.IsNullOrEmpty(rel) || !pack.Has(rel))
            {
                sr.sprite = null;
                return;
            }
            var tex = LoadTexture(pack, rel);
            // Whatever size the PNG turned out to be, fitted to the 256x256
            // frame the rest of the bust rig is built around.
            sr.sprite = FittedSprite.CreateBust(tex);
        }

        /// <param name="linear">
        /// True for a DATA texture, false for colour. The jiggle mask is data:
        /// its channels are displacement amounts the shader multiplies, not a
        /// colour to look at. Loading it as sRGB puts a gamma curve on those
        /// amounts under a linear-space project — a painted 0.5 samples as
        /// about 0.22 — so the effect comes out nothing like the mask that was
        /// drawn, and nothing like a vanilla mask, whose importer has sRGB off.
        /// Sprites are genuine colour and stay sRGB.
        /// </param>
        private static Texture2D LoadTexture(PackManifest pack, string rel, bool linear = false)
        {
            // The dimensions here are a placeholder: LoadImage resizes the
            // texture to whatever the PNG actually is. FittedSprite is what
            // puts art of the wrong size back to the right on-screen size.
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false, linear);
            byte[] bytes = pack.ReadBytes(rel);
            if (bytes != null) tex.LoadImage(bytes);
            tex.filterMode = FilterMode.Point;
            return tex;
        }

        /// <summary>
        /// Give every overlay under the bust's sprite root an
        /// <see cref="OverlayJiggle"/> so it moves with the body, leaving its
        /// own material (and therefore its lighting and colour) alone.
        /// <para/>
        /// The mask is constant at runtime, so each overlay's four channels are
        /// sampled once here, at the sprite's alpha-weighted centroid — the point
        /// that best represents where the sprite actually sits on the body's
        /// frame. All bust art shares that 256x256 frame, so the overlay's own UV
        /// indexes the mask directly.
        /// <para/>
        /// Walks the subtree rather than the Blink / Mouth / Expressions groups by
        /// name: rigs differ (the sprite root alone is MBase1 on some busts and
        /// D1Base on others) and a named walk silently skips any branch a
        /// character names differently.
        /// </summary>
        private static void AttachOverlayJiggle(Transform mBase, Texture2D maskTex, JObject j)
        {
            if (mBase == null || maskTex == null) return;
            var bodySr = mBase.GetComponent<SpriteRenderer>();

            float speed         = (float?)j?["speed"]         ?? 3.0f;
            float strength      = (float?)j?["strength"]      ?? -0.02f;
            float frequency     = (float?)j?["frequency"]     ?? 4.0f;
            float noiseScale    = (float?)j?["noiseScale"]    ?? 5.0f;
            float noiseSpeed    = (float?)j?["noiseSpeed"]    ?? 0.5f;
            float noiseStrength = (float?)j?["noiseStrength"] ?? 0.06f;

            foreach (var sr in mBase.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == bodySr || sr.sprite == null) continue;

                Vector2 uv = OpaqueCentroidUv(sr.sprite);
                Color m = maskTex.GetPixelBilinear(uv.x, uv.y);

                // Overlay textures stay on FilterMode.Point, like the body.
                // Bilinear was tried to smooth the bounce and is the wrong trade:
                // it softens the sprite whenever it is sampled off a texel centre,
                // which at these display scales is essentially always — not only
                // while moving. Crisp art matters more than a smoother few-texel
                // travel. See OverlayJiggle for what the stepping actually is.
                var comp = sr.GetComponent<OverlayJiggle>();
                if (comp == null) comp = sr.gameObject.AddComponent<OverlayJiggle>();
                comp.MaskR = m.r; comp.MaskG = m.g; comp.MaskB = m.b; comp.MaskA = m.a;
                comp.Uv = uv;
                comp.Speed = speed; comp.Strength = strength; comp.Frequency = frequency;
                comp.NoiseScale = noiseScale; comp.NoiseSpeed = noiseSpeed;
                comp.NoiseStrength = noiseStrength;
                var size = sr.sprite.bounds.size;
                comp.SpriteSize = new Vector2(size.x, size.y);
            }
        }

        /// <summary>
        /// Alpha-weighted centre of a sprite's drawn pixels, in 0..1 UV.
        /// Overlays are a small mark on a mostly empty canvas, so the geometric
        /// centre would sample the mask nowhere near the art. Subsampled on a
        /// 2px grid — this only decides which mask texel to read, and it runs
        /// once per overlay at build time. Falls back to the centre for a sprite
        /// with no opaque pixels at all.
        /// </summary>
        private static Vector2 OpaqueCentroidUv(Sprite sprite)
        {
            var tex = sprite.texture;
            if (tex == null) return new Vector2(0.5f, 0.5f);

            Color32[] px;
            try { px = tex.GetPixels32(); }
            catch { return new Vector2(0.5f, 0.5f); }   // not readable

            int w = tex.width, h = tex.height;
            double sx = 0, sy = 0, sa = 0;
            for (int y = 0; y < h; y += 2)
            {
                int row = y * w;
                for (int x = 0; x < w; x += 2)
                {
                    byte a = px[row + x].a;
                    if (a == 0) continue;
                    sx += x * (double)a; sy += y * (double)a; sa += a;
                }
            }
            if (sa <= 0) return new Vector2(0.5f, 0.5f);
            return new Vector2((float)(sx / sa) / w, (float)(sy / sa) / h);
        }

        private static bool _loggedOverlayShaders;

        /// <summary>Report the body's shader and each overlay's, once per session.</summary>
        private static void LogOverlayShadersOnce(GameObject body, GameObject blink,
                                                  GameObject mouth, GameObject expressions,
                                                  ManualLogSource logger)
        {
            if (_loggedOverlayShaders || logger == null) return;
            _loggedOverlayShaders = true;

            string Describe(SpriteRenderer sr)
            {
                if (sr == null) return "(none)";
                var m = sr.sharedMaterial;
                return m == null
                    ? "(no material)"
                    : m.shader == null ? "(no shader)" : m.shader.name + "  color=" + sr.color;
            }

            logger.LogInfo("[SMSModForge.PackPlugin] Bust shaders — body: " +
                           Describe(body?.GetComponent<SpriteRenderer>()));
            foreach (var root in new[] { blink, mouth, expressions })
            {
                if (root == null) continue;
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                    logger.LogInfo("[SMSModForge.PackPlugin]   overlay '" +
                                   sr.transform.parent?.name + "/" + sr.name + "': " + Describe(sr));
            }
        }

        internal static void ApplyJiggle(Material mat, JObject j)
        {
            mat.SetFloat("_JiggleSpeed",     (float?)j["speed"]         ?? 3.0f);
            mat.SetFloat("_JiggleStrength",  (float?)j["strength"]      ?? -0.02f);
            mat.SetFloat("_JiggleFrequency", (float?)j["frequency"]     ?? 4.0f);
            mat.SetFloat("_NoiseScale",      (float?)j["noiseScale"]    ?? 5.0f);
            mat.SetFloat("_NoiseSpeed",      (float?)j["noiseSpeed"]    ?? 0.5f);
            mat.SetFloat("_NoiseStrength",   (float?)j["noiseStrength"] ?? 0.06f);
            mat.SetFloat("_PixelSnap",       ((bool?)j["pixelSnap"]     ?? false) ? 1f : 0f);
            if (TryParseHexColor((string)j["tint"], out Color c)) mat.SetColor("_Color", c);
        }

        internal static bool TryParseHexColor(string hex, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(hex)) return false;
            string s = hex.TrimStart('#');
            if (s.Length == 6) s += "FF";
            if (s.Length != 8) return false;
            try
            {
                byte r = System.Convert.ToByte(s.Substring(0, 2), 16);
                byte g = System.Convert.ToByte(s.Substring(2, 2), 16);
                byte b = System.Convert.ToByte(s.Substring(4, 2), 16);
                byte a = System.Convert.ToByte(s.Substring(6, 2), 16);
                c = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                return true;
            }
            catch { return false; }
        }

        private static void AttachWetParticle(GameObject mBase, string name, bool active = false)
        {
            // Anna_Towel is a vanilla bust and is normally inactive at load time,
            // so GameObject.Find (active-only) would miss it — use the
            // include-inactive search so the wet-particle source resolves.
            var anna = TransformExtensions.FindGlobalIncludingInactive("Anna_Towel");
            var source = anna?.transform.Find("MBase1")?.Find("Particle System")?.gameObject;
            if (source == null) return;
            var p = Object.Instantiate(source, mBase.transform);
            p.name = name;
            p.SetActive(active);
        }

        /// <summary>
        /// Removes the first component on <paramref name="go"/> whose runtime
        /// type name matches <paramref name="typeName"/>. Used to scrub
        /// GameCreator <c>Conditions</c>/<c>Trigger</c> components off cloned
        /// expressions without needing a compile-time reference to those types.
        /// </summary>
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
