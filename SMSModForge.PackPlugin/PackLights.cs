using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// A place object's 2D light (1.10.0): the editor's <c>LightDef</c> given to
    /// URP's <c>Light2D</c>. Reached by reflection, as the plugin reaches the
    /// render pipeline everywhere: the type lives in the game's URP assembly,
    /// which the plugin does not reference.
    /// <list type="bullet">
    /// <item>An object the pack makes gets a light of its own. URP's Awake gives
    /// a light made at run time EVERY sorting layer (its layers are null then),
    /// so the layers are set after: the place's (Default) alone - the busts are
    /// not lit by it, as by 141 of the game's 144 lamps - and no shadows, which
    /// the editor's preview does not draw.</item>
    /// <item>A bound object (the template level's light, a lamp of the game's)
    /// has its light given every setting the pack wrote and keeps the rest: its
    /// layers, its shadows.</item>
    /// <item><c>removeLight</c> switches a bound object's light off; the object
    /// stays.</item>
    /// </list>
    /// What the game then draws is checked in the game itself (InGameSuiteTests).
    /// </summary>
    internal static class PackLights
    {
        private static Type _type;
        private static bool _looked;

        internal static Type Light2DType()
        {
            if (_looked) return _type;
            _looked = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType("UnityEngine.Rendering.Universal.Light2D"); } catch { }
                if (t != null) { _type = t; break; }
            }
            return _type;
        }

        public static void Apply(GameObject go, JObject ov, PackManifest pack, bool asFound, ManualLogSource log, string where)
        {
            bool remove = asFound && ((bool?)ov["removeLight"] ?? false);
            var j = ov["light"] as JObject;
            if (!remove && j == null) return;
            var type = Light2DType();
            if (type == null)
            {
                log?.LogWarning("[SMSModForge.PackPlugin] '" + where + "': the game has no 2D lights (Light2D not found) - its light is left out.");
                return;
            }
            var light = go.GetComponent(type) as Behaviour;
            if (remove)
            {
                if (light != null) light.enabled = false;
                else log?.LogWarning("[SMSModForge.PackPlugin] '" + where + "': '" + go.name + "' has no light to switch off.");
                return;
            }

            bool made = false;
            if (light == null)
            {
                light = go.AddComponent(type) as Behaviour;
                made = true;
            }
            if (light == null) return;

            try
            {
                string shape = ((string)j["shape"] ?? "round").ToLowerInvariant();
                if (shape == "ambient")
                {
                    // The place's own base light (1.10.0): a global light, on only while this object is (PackAmbient).
                    // Its layers first: a place is built switched off, so the light's Awake has not given it any yet,
                    // and making it global reads them (Light2DManager.ErrorIfDuplicateGlobalLight: null, measured).
                    var taken = ((bool?)j["lightsCharacters"] ?? false)
                        ? new[] { SortingLayer.NameToID("Default"), SortingLayer.NameToID("Bust") }
                        : new[] { SortingLayer.NameToID("Default") };
                    Set(light, type, "targetSortingLayers", taken);
                    var global = type.GetProperty("lightType");
                    global?.SetValue(light, Enum.Parse(global.PropertyType, "Global"), null);
                    light.enabled = false;
                    Color tone = Color.white;
                    if (BustFactory.TryParseHexColor((string)j["color"] ?? "#FFFFFF", out var t)) tone = t;
                    tone.a = 1f;
                    Set(light, type, "color", tone);
                    Set(light, type, "intensity", Mathf.Max(0f, (float?)j["strength"] ?? 0.35f));
                    Set(light, type, "volumetricEnabled", false);
                    Set(light, type, "shadowsEnabled", false);
                    Set(light, type, "blendStyleIndex", 0);
                    var ambient = go.GetComponent<PackAmbient>() ?? go.AddComponent<PackAmbient>();
                    ambient.enabled = false;   // configured first: its OnEnable settles which global light lights what
                    ambient.Light = light;
                    ambient.Layers = taken;
                    ambient.enabled = true;
                    return;
                }
                string kind = shape == "free" ? "Freeform" : shape == "picture" ? "Sprite" : "Point";
                // The shape first: the cookie and the path are read for the type the light has.
                if (shape == "free")
                {
                    var pts = (j["points"] as JArray)?.OfType<JArray>()
                        .Where(p => p.Count >= 2)
                        .Select(p => new Vector3((float)p[0], (float)p[1], 0f)).ToArray();
                    if (pts == null || pts.Length < 3)
                    {
                        log?.LogWarning("[SMSModForge.PackPlugin] '" + where + "': '" + go.name + "' has a free light with fewer than 3 points - left out.");
                        if (made) UnityEngine.Object.Destroy(light); else light.enabled = false;
                        return;
                    }
                    type.GetMethod("SetShapePath", BindingFlags.Public | BindingFlags.Instance)?.Invoke(light, new object[] { pts });
                    Set(light, type, "shapeLightFalloffSize", Mathf.Max(0f, (float?)j["edge"] ?? 0.5f));
                }
                else if (shape == "picture")
                {
                    string rel = (string)j["picture"];
                    if (string.IsNullOrEmpty(rel) || !pack.Has(rel))
                    {
                        log?.LogWarning("[SMSModForge.PackPlugin] '" + where + "': '" + go.name + "' has a picture light with no picture ('" + rel + "') - left out.");
                        if (made) UnityEngine.Object.Destroy(light); else light.enabled = false;
                        return;
                    }
                    Set(light, type, "lightCookieSprite", LoadedArt.Sprite(pack, rel, "light", () => LoadLightSprite(pack, rel)));
                }
                var lightType = type.GetProperty("lightType");
                lightType?.SetValue(light, Enum.Parse(lightType.PropertyType, kind), null);

                Color colour = Color.white;
                if (BustFactory.TryParseHexColor((string)j["color"] ?? "#FFFFFF", out var c)) colour = c;
                colour.a = 1f;
                Set(light, type, "color", colour);
                Set(light, type, "intensity", Mathf.Max(0f, (float?)j["strength"] ?? 0.5f));
                if (shape != "picture") Set(light, type, "falloffIntensity", Mathf.Clamp01((float?)j["falloff"] ?? 0.5f));
                float glow = Mathf.Clamp01((float?)j["glow"] ?? 0f);
                Set(light, type, "volumetricEnabled", glow > 0f);
                if (glow > 0f) Set(light, type, "volumeIntensity", glow);
                if (shape == "round")
                {
                    float outer = Mathf.Max(0.01f, (float?)j["outerRadius"] ?? 3f);
                    Set(light, type, "pointLightOuterRadius", outer);
                    Set(light, type, "pointLightInnerRadius", Mathf.Clamp((float?)j["innerRadius"] ?? 0f, 0f, outer * 0.999f));
                    float outerAngle = Mathf.Clamp((float?)j["outerAngle"] ?? 360f, 1f, 360f);
                    Set(light, type, "pointLightOuterAngle", outerAngle);
                    Set(light, type, "pointLightInnerAngle", Mathf.Clamp((float?)j["innerAngle"] ?? 360f, 0f, outerAngle));
                }
                // The characters' layer (1.10.0): with the place's on a light the pack makes; added to or taken
                // off a light of the game's only when the pack says (its other layers are the game's).
                int bust = SortingLayer.NameToID("Bust");
                bool characters = (bool?)j["lightsCharacters"] ?? false;
                if (made)
                {
                    Set(light, type, "targetSortingLayers", characters ? new[] { SortingLayer.NameToID("Default"), bust } : new[] { SortingLayer.NameToID("Default") });
                    Set(light, type, "shadowsEnabled", false);
                    Set(light, type, "blendStyleIndex", 0);
                }
                else if (j["lightsCharacters"] != null)
                {
                    var layers = (type.GetProperty("targetSortingLayers")?.GetValue(light, null) as int[]) ?? new int[0];
                    var now = layers.Where(l => l != bust).ToList();
                    if (characters) now.Add(bust);
                    Set(light, type, "targetSortingLayers", now.ToArray());
                }
                light.enabled = true;
            }
            catch (Exception ex)
            {
                var cause = ex.InnerException ?? ex;
                log?.LogWarning("[SMSModForge.PackPlugin] '" + where + "': the light on '" + go.name + "' failed: " + cause.GetType().Name + ": " + cause.Message
                                + " " + (cause.StackTrace ?? "").Split(new[] { Environment.NewLine }, StringSplitOptions.None).FirstOrDefault());
            }
        }

        private static void Set(Behaviour light, Type type, string property, object value)
            => type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.SetValue(light, value, null);

        /// <summary>A picture light's sprite: the picture at its own size, centred,
        /// at the pack's objects' pixels per unit (as an object's sprite).</summary>
        private static Sprite LoadLightSprite(PackManifest pack, string rel)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            byte[] bytes = pack.ReadBytes(rel);
            if (bytes != null) tex.LoadImage(bytes, true);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 70.32f);
        }
    }
}
