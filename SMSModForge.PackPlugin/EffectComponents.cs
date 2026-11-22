using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FX = SMSModForge.Shared.Effects;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The particle effects ModForge builds from nothing (the FX tab's own templates): how each is shaped, where it
    /// starts on the screen, and the material it draws with - the game's own particle shaders, so it is lit and
    /// blended as the game's effects are.
    /// <para/>
    /// In the game's units: the camera is 5.4 from the middle to the top, 10.8 tall, 19.2 wide at 16:9.
    /// </summary>
    internal static class ForgeShapes
    {
        public static Vector2 Home(string name)
        {
            switch (name)
            {
                case "falling": return new Vector2(0.5f, 1.04f);
                default: return new Vector2(0.5f, 0.5f);
            }
        }

        public static void Shape(string name, ParticleSystem ps, ParticleSystemRenderer r)
        {
            var main = ps.main;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            var emission = ps.emission;
            var shape = ps.shape;
            shape.enabled = true;
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var size = ps.sizeOverLifetime;
            var vel = ps.velocityOverLifetime;
            var noise = ps.noise;
            var spin = ps.rotationOverLifetime;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.OldestInFront;

            switch (name)
            {
                case "burst":
                    main.duration = 1f;
                    main.loop = false;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
                    main.gravityModifier = 0.5f;
                    main.maxParticles = 200;
                    emission.rateOverTime = 0f;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = 0.15f;
                    colour.color = Fade(0f, 0.6f);
                    size.enabled = true;
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.7f));
                    spin.enabled = true;
                    spin.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
                    break;
                case "rising":
                    main.duration = 5f;
                    main.loop = true;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
                    main.gravityModifier = 0f;
                    main.startRotation = 0f;
                    main.maxParticles = 100;
                    emission.rateOverTime = 4f;
                    shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;
                    shape.radius = 1.5f;
                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.World;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                    vel.y = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                    noise.enabled = true;
                    noise.strength = 0.3f;
                    noise.frequency = 0.4f;
                    colour.color = Fade(0.15f, 0.7f);
                    size.enabled = true;
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1f));
                    break;
                case "falling":
                    main.duration = 10f;
                    main.loop = true;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 11f);
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.18f);
                    main.gravityModifier = 0f;
                    main.maxParticles = 600;
                    emission.rateOverTime = 20f;
                    shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;
                    shape.radius = 10.5f;
                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.World;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                    vel.y = new ParticleSystem.MinMaxCurve(-1.8f, -1f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                    noise.enabled = true;
                    noise.strength = 0.4f;
                    noise.frequency = 0.3f;
                    spin.enabled = true;
                    spin.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
                    colour.color = Fade(0.05f, 0.9f);
                    break;
                default:   // drifting
                    main.duration = 8f;
                    main.loop = true;
                    main.prewarm = true;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
                    main.gravityModifier = 0f;
                    main.maxParticles = 120;
                    emission.rateOverTime = 3f;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(17f, 8.5f, 0.01f);
                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.World;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
                    vel.y = new ParticleSystem.MinMaxCurve(-0.15f, 0.25f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                    noise.enabled = true;
                    noise.strength = 0.25f;
                    noise.frequency = 0.3f;
                    colour.color = Fade(0.2f, 0.8f, 0.8f);
                    break;
            }
        }

        /// <summary>Clear at the start (or not), whole in the middle, clear at the end.</summary>
        private static ParticleSystem.MinMaxGradient Fade(float inUntil, float outFrom, float most = 1f)
        {
            var g = new Gradient();
            var alpha = inUntil > 0f
                ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(most, inUntil), new GradientAlphaKey(most, outFrom), new GradientAlphaKey(0f, 1f) }
                : new[] { new GradientAlphaKey(most, 0f), new GradientAlphaKey(most, outFrom), new GradientAlphaKey(0f, 1f) };
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alpha);
            return new ParticleSystem.MinMaxGradient(g);
        }

        /// <summary>A copy of a material of the game's that already draws <paramref name="picture"/> - glowing
        /// (added on) when asked, where its shader lets the blend be set.</summary>
        public static Material MaterialFor(Texture picture, bool glow)
        {
            Material found = null;
            if (picture != null)
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null || m.shader == null) continue;
                    if ((m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == picture)
                        || (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") == picture)) { found = m; break; }
                }
            if (found == null) return Material(glow);
            var copy = new Material(found);
            if (glow && copy.HasProperty("_DstBlend")) copy.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            return copy;
        }

        private static Material _blended, _glowing;

        /// <summary>One of the game's particle materials to copy: alpha-blended, or added on (glowing).</summary>
        public static Material Material(bool glow)
        {
            if (glow ? _glowing != null : _blended != null) return glow ? _glowing : _blended;
            string want = glow ? "Mobile/Particles/Additive" : "Mobile/Particles/Alpha Blended";
            Material found = null;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.shader != null && m.shader.name == want) { found = m; break; }
            if (found == null)
            {
                var shader = Shader.Find(want) ?? Shader.Find("Sprites/Default");
                found = shader != null ? new Material(shader) : null;
            }
            if (glow) _glowing = found; else _blended = found;
            return found;
        }
    }

    /// <summary>
    /// The game's social points (Attractor_Canvas): their icon, and an author's changes to them - the picture,
    /// how many a photo gets (the count the game asks for, times the author's), how far they spread, how long
    /// they float and fly, where they come from and go to.
    /// </summary>
    internal static class GamePoints
    {
        private static readonly Dictionary<int, float> Times = new Dictionary<int, float>();
        private static bool _patched;

        public static Image Icon(string attractorPath)
        {
            var go = TransformExtensions.ResolveGameObject(attractorPath);
            var a = go != null ? go.GetComponent<ExplosiveUIParticleAttractor>() : null;
            return a != null && a.particlePrefab != null ? a.particlePrefab.GetComponentInChildren<Image>(true) : null;
        }

        public static void Change(GameObject target, JObject def, PackContext ctx, ManualLogSource log)
        {
            var a = target.GetComponent<ExplosiveUIParticleAttractor>();
            if (a == null) { log?.LogWarning("[SMSModForge.PackPlugin] " + target.name + " is not the game's points any more."); return; }

            float height = FX.CameraHalfHeight * 2f;
            if (def[FX.SpreadField] != null) a.explosionRadius = EffectRuntime.F(def, FX.SpreadField, a.explosionRadius / height) * height;
            if (def[FX.FloatSecondsField] != null) a.floatDuration = EffectRuntime.F(def, FX.FloatSecondsField, a.floatDuration);
            if (def[FX.TravelSecondsField] != null) a.travelDuration = EffectRuntime.F(def, FX.TravelSecondsField, a.travelDuration);

            var cam = Camera.main;
            if (cam != null && a.parentCanvas != null)
            {
                float depth = a.parentCanvas.planeDistance;
                if (a.startPoint != null && (def[FX.XField] != null || def[FX.YField] != null))
                    a.startPoint.position = cam.ViewportToWorldPoint(new Vector3(EffectRuntime.F(def, FX.XField, FX.PointsStartX), EffectRuntime.F(def, FX.YField, FX.PointsStartY), depth));
                if (a.endPoint != null && (def[FX.EndXField] != null || def[FX.EndYField] != null))
                    a.endPoint.position = cam.ViewportToWorldPoint(new Vector3(EffectRuntime.F(def, FX.EndXField, FX.PointsEndX), EffectRuntime.F(def, FX.EndYField, FX.PointsEndY), depth));
            }

            // Its picture, colour and size: a copy of its icon, kept under something switched off, which the game
            // then copies for each point.
            string rel = (string)def[FX.TextureField];
            string game = (string)def[FX.GameTextureField];
            var tint = EffectRuntime.Colour((string)def[FX.ColorField]);
            float scale = EffectRuntime.F(def, FX.ScaleField, 1f);
            if (a.particlePrefab != null && (!string.IsNullOrEmpty(rel) || !string.IsNullOrEmpty(game) || tint.HasValue || !Mathf.Approximately(scale, 1f)))
            {
                var hold = new GameObject("SMSModForge points icon (" + target.name + ")");
                hold.SetActive(false);
                var icon = UnityEngine.Object.Instantiate(a.particlePrefab, hold.transform, false);
                var image = icon.GetComponentInChildren<Image>(true);
                Texture2D tex = null;
                if (!string.IsNullOrEmpty(rel) && ctx.Pack != null && ctx.Pack.Has(rel))
                {
                    tex = new Texture2D(4, 4, TextureFormat.RGBA32, true);
                    tex.LoadImage(ctx.Pack.ReadBytes(rel));
                }
                else if (!string.IsNullOrEmpty(game)) tex = EffectRuntime.GameTexture(game) as Texture2D;
                if (image != null && tex != null) image.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                if (image != null && tint.HasValue) image.color = image.color * tint.Value;
                icon.localScale = icon.localScale * scale;
                a.particlePrefab = icon;
            }

            float amount = EffectRuntime.F(def, FX.AmountField, 1f);
            if (!Mathf.Approximately(amount, 1f))
            {
                Times[a.GetInstanceID()] = Mathf.Max(0f, amount);
                Patch(log);
            }
            log?.LogInfo("[SMSModForge.PackPlugin] The game's " + target.name + " changed by " + ctx.PackId + ".");
        }

        private static void Patch(ManualLogSource log)
        {
            if (_patched) return;
            _patched = true;
            var harmony = new HarmonyLib.Harmony("smsmodforge.packplugin.effects");
            var method = typeof(ExplosiveUIParticleAttractor).GetMethod("TriggerExplosion");
            harmony.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(GamePoints).GetMethod(nameof(BeforeBurst),
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            log?.LogInfo("[SMSModForge.PackPlugin] The game's points: how many, changed by a pack.");
        }

        private static void BeforeBurst(ExplosiveUIParticleAttractor __instance, ref int count)
        {
            float times;
            if (__instance != null && Times.TryGetValue(__instance.GetInstanceID(), out times))
                count = Mathf.RoundToInt(count * times);
        }
    }

    /// <summary>
    /// The social points' motion, for any picture (ModForge's own points, and new points from the game's): each
    /// bursts out of where it starts to a point at random around it, drifts there, then flies to where it ends -
    /// as the game's ExplosiveUIParticleAttractor moves its followers and hearts after a Starmaker photo.
    /// </summary>
    internal sealed class PointsBurst : MonoBehaviour
    {
        public Sprite Sprite;
        public Vector2 Size = new Vector2(48, 48);
        public Color Tint = Color.white;
        public Vector2 From, To;
        public float Spread, FloatSeconds, TravelSeconds;
        public int Count;

        private sealed class Point { public RectTransform Rt; public Vector2 Out; public float Age; }
        private readonly List<Point> _points = new List<Point>();

        public void Go()
        {
            var screen = new Vector2(Screen.width, Screen.height);
            var from = Vector2.Scale(From, screen);
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("point", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.sizeDelta = Size;
                var image = go.GetComponent<Image>();
                image.sprite = Sprite;
                image.color = Tint;
                image.raycastTarget = false;
                image.preserveAspect = true;
                rt.position = from;
                // As the game: a direction at random, a distance between nothing and the spread.
                var dir = UnityEngine.Random.insideUnitCircle.normalized;
                float far = UnityEngine.Random.Range(0.05f, Mathf.Max(0.05f, Spread)) * Screen.height;
                _points.Add(new Point { Rt = rt, Out = from + dir * far });
            }
        }

        private void Update()
        {
            var screen = new Vector2(Screen.width, Screen.height);
            var from = Vector2.Scale(From, screen);
            var to = Vector2.Scale(To, screen);
            for (int i = _points.Count - 1; i >= 0; i--)
            {
                var p = _points[i];
                p.Age += Time.deltaTime;
                if (p.Age < FloatSeconds)
                    p.Rt.position = Vector2.Lerp(from, p.Out, p.Age / Mathf.Max(0.01f, FloatSeconds));
                else
                {
                    float t = Mathf.Clamp01((p.Age - FloatSeconds) / Mathf.Max(0.01f, TravelSeconds));
                    p.Rt.position = Vector2.Lerp(p.Out, to, t * t);
                    if (t >= 1f) { Destroy(p.Rt.gameObject); _points.RemoveAt(i); }
                }
            }
            if (_points.Count == 0) Destroy(gameObject);
        }
    }

    /// <summary>An object's screen effect (points, a flash, words) played each time the object turns on - with its
    /// place, or by its own conditions (1.10.0).</summary>
    internal sealed class EffectOnEnable : MonoBehaviour
    {
        public Func<Playing> Play;
        private void OnEnable()
        {
            try { Play?.Invoke(); }
            catch (Exception ex) { Plugin.Log?.LogWarning("[SMSModForge.PackPlugin] " + name + "'s effect: " + ex.Message); }
        }
    }

    /// <summary>The screen flashes a colour: up quickly, down slowly.</summary>
    internal sealed class ScreenFlash : MonoBehaviour
    {
        public Color Colour = Color.white;
        public float Seconds = 0.4f;
        private Image _image;
        private float _age;

        public void Go()
        {
            var go = new GameObject("flash", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _image = go.GetComponent<Image>();
            _image.raycastTarget = false;
            Show(0f);
        }

        private void Show(float a) { var c = Colour; c.a = Colour.a * a; _image.color = c; }

        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            float t = _age / Seconds;
            if (t >= 1f) { Destroy(gameObject); return; }
            Show(t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f);
        }
    }

    /// <summary>Words that pop in, float up and fade - in the game's own pop-up font.</summary>
    internal sealed class FloatingWords : MonoBehaviour
    {
        public string Text = "";
        public Color Colour = Color.white;
        public float Size = 54f;
        public Vector2 At = new Vector2(0.5f, 0.55f);
        public float Seconds = 1.6f;
        public TMP_FontAsset Font;
        private TextMeshProUGUI _tmp;
        private float _age;

        public void Go()
        {
            var go = new GameObject("words", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.sizeDelta = new Vector2(1400, 200);
            _tmp = go.AddComponent<TextMeshProUGUI>();
            if (Font != null) _tmp.font = Font;
            _tmp.text = Text;
            _tmp.fontSize = Size;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.raycastTarget = false;
            _tmp.color = Colour;
            Place(0f);
        }

        private void Place(float t)
        {
            var rt = _tmp.rectTransform;
            rt.position = new Vector2(At.x * Screen.width, (At.y + 0.08f * t) * Screen.height);
            float pop = t < 0.15f ? Mathf.Lerp(0.6f, 1f, 1f - (1f - t / 0.15f) * (1f - t / 0.15f)) : 1f;
            rt.localScale = Vector3.one * pop;
            var c = Colour;
            c.a = Colour.a * (t < 0.1f ? t / 0.1f : t > 0.65f ? 1f - (t - 0.65f) / 0.35f : 1f);
            _tmp.color = c;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / Seconds;
            if (t >= 1f) { Destroy(gameObject); return; }
            Place(t);
        }
    }
}
