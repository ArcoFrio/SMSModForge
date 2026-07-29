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

            if (!pack.Has(baseRel) || !pack.Has(maskRel) || !pack.Has(blinkRel))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Skipping " + goName + " — base/mask/blink missing in archive.");
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
            GameObject mBase = newBust.transform.Find("MBase1").gameObject;
            GameObject blink = mBase.transform.Find("Blink").gameObject;
            GameObject mouthGo = mBase.transform.Find("Mouth").gameObject;
            GameObject expressions = mBase.transform.Find("Expressions").gameObject;

            // Per-outfit material clone so shader uniforms / mask are not shared
            // between outfits. Built fully BEFORE being assigned: reading back
            // .material after assigning re-instantiates, which is how the mask
            // and jiggle ended up on a copy nothing else could reach.
            Material mat = new Material(mBase.GetComponent<SpriteRenderer>().sharedMaterial);

            ApplySprite(mBase.GetComponent<SpriteRenderer>(), pack, baseRel);
            ApplySprite(blink.GetComponent<SpriteRenderer>(), pack, blinkRel);

            Texture2D maskTex = LoadTexture(pack, maskRel, linear: true);
            mat.SetTexture("_MaskTex", maskTex);

            var jiggle = (JObject)o["jiggle"];
            if (jiggle != null) ApplyJiggle(mat, jiggle);

            mBase.GetComponent<SpriteRenderer>().sharedMaterial = mat;

            // Mouth frames — load 1..4 if enabled, otherwise destroy the renderers.
            // The "prefix" carried in the manifest is an archive-relative
            // stem (e.g. "Sprites/MyChar/Mouth_"); we suffix 1.PNG..4.PNG and
            // look the result up in the archive.
            for (int i = 1; i <= 4; i++)
            {
                var slot = mouthGo.transform.Find(i.ToString());
                if (slot == null) continue;
                var sr = slot.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                if (mouthEnabled && !string.IsNullOrEmpty(mouthPrefix))
                {
                    string rel = mouthPrefix + i + ".PNG";
                    if (pack.Has(rel)) ApplySprite(sr, pack, rel);
                }
                else
                {
                    Object.Destroy(sr);
                }
            }

            // Expression overlays — same pattern as mouth.
            foreach (var name in ExpressionNames)
            {
                var slot = expressions.transform.Find(name);
                if (slot == null) continue;
                var sr = slot.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                if (exprEnabled && !string.IsNullOrEmpty(exprPrefix))
                {
                    string rel = exprPrefix + name + ".PNG";
                    if (pack.Has(rel)) ApplySprite(sr, pack, rel);
                }
                else
                {
                    Object.Destroy(sr);
                }
            }

            // Every overlay rides the SAME jiggle material as the body, so the
            // eyes, mouth and expression are displaced with it instead of
            // sitting still on a moving chest. Only the body was getting it,
            // which is why the mask appeared not to reach the other sprites.
            // Sharing one instance is what makes the displacement consistent:
            // the shader samples _MaskTex in the sprite's own UV space and
            // these overlays are authored on the body's frame.
            // Opt-in, default OFF = vanilla behaviour (overlays keep their own
            // material and only the body jiggles). Sharing the jiggle material
            // with the overlays washes them out for a reason not yet pinned
            // down — it survived removing the body's tint from the copy — so
            // the default stays on the appearance that is known good and this
            // is a switch to experiment behind rather than a silent change.
            // One-off diagnostic: what the vanilla overlay renderers are actually
            // shaded with. Sharing the body's jiggle material with them washes
            // them out, and that is only explicable if their own shader differs
            // from the body's in how it handles alpha — this says which.
            LogOverlayShadersOnce(mBase, blink, mouthGo, expressions, logger);

            if ((bool?)jiggle?["applyToOverlays"] ?? false)
                ShareMaterial(mat, blink, mouthGo, expressions);

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

        private static void ApplySprite(SpriteRenderer sr, PackManifest pack, string rel)
        {
            if (sr == null || string.IsNullOrEmpty(rel) || !pack.Has(rel)) return;
            var tex = LoadTexture(pack, rel);
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 256, 256), new Vector2(0.5f, 0.5f));
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
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false, linear);
            byte[] bytes = pack.ReadBytes(rel);
            if (bytes != null) tex.LoadImage(bytes);
            tex.filterMode = FilterMode.Point;
            return tex;
        }

        /// <summary>Give every SpriteRenderer at or under <paramref name="roots"/>
        /// the same material instance. Renderers destroyed above (a disabled
        /// mouth or expression set) are simply not there to receive it.</summary>
        private static void ShareMaterial(Material mat, params GameObject[] roots)
        {
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    var original = sr.sharedMaterial;
                    var m = new Material(mat);
                    // Keep the overlay's OWN tint. _Color on the body's material
                    // is that art's colour correction; the eyes, mouth and
                    // expression are drawn separately and inheriting it washed
                    // them out. Only the displacement should carry over, which
                    // is the mask plus the jiggle uniforms already in the copy.
                    m.SetColor("_Color",
                        original != null && original.HasProperty("_Color")
                            ? original.GetColor("_Color")
                            : Color.white);
                    sr.sharedMaterial = m;
                }
            }
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
