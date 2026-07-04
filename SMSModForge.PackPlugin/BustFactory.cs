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

            // Per-outfit material clone so shader uniforms / mask are not shared.
            Material mat = new Material(mBase.GetComponent<SpriteRenderer>().material);

            ApplySprite(mBase.GetComponent<SpriteRenderer>(), pack, baseRel);
            ApplySprite(blink.GetComponent<SpriteRenderer>(), pack, blinkRel);

            // Mask: bound to the material as _MaskTex. Set the texture once and
            // re-assign the material so the SpriteRenderer picks up the change.
            Texture2D maskTex = LoadTexture(pack, maskRel);
            mat.SetTexture("_MaskTex", maskTex);
            mBase.GetComponent<SpriteRenderer>().material = mat;
            mBase.GetComponent<SpriteRenderer>().material.SetTexture("_MaskTex", maskTex);

            var jiggle = (JObject)o["jiggle"];
            if (jiggle != null) ApplyJiggle(mBase.GetComponent<SpriteRenderer>().material, jiggle);

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

        private static Texture2D LoadTexture(PackManifest pack, string rel)
        {
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            byte[] bytes = pack.ReadBytes(rel);
            if (bytes != null) tex.LoadImage(bytes);
            tex.filterMode = FilterMode.Point;
            return tex;
        }

        private static void ApplyJiggle(Material mat, JObject j)
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

        private static bool TryParseHexColor(string hex, out Color c)
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
