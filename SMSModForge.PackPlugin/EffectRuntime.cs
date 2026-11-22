using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FX = SMSModForge.Shared.Effects;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// A pack's effects (the FX tab, 1.10.0; Shared.Effects says what each template is).
    /// <para/>
    /// An effect is made when it plays and gone when it is done: one of the game's copied out of where the game keeps
    /// it (scripts and all taken off, so a copy cannot run the game's logic), or one ModForge builds - particles, the
    /// social points' motion, a flash, words that float up. "Change the game's" edits the game's own in place, once,
    /// when the pack loads.
    /// <para/>
    /// When: an action plays or stops it; or every frame its conditions are read - played each time they become
    /// true, or kept playing while they are; or each time something under an object turns on (each new Starmaker
    /// photo, though the photo before it was on until then).
    /// </summary>
    internal sealed class EffectsRegistry
    {
        internal sealed class Entry
        {
            public string Key;
            public JObject Def;
            public FX.Template Template;
            public string PlayWhen = FX.OnAction;
            public JArray Conditions;
            public string UnderPath;
            public bool WasTrue;
            public bool Primed;
            public GameObject UnderRoot;
            public readonly HashSet<int> OnBefore = new HashSet<int>();
            public readonly List<Playing> Live = new List<Playing>();
        }

        internal readonly Dictionary<string, Entry> ByKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
    }

    /// <summary>One effect on screen: what it is made of and when it ends.</summary>
    internal sealed class Playing
    {
        public GameObject Root;
        public float StopAt = float.PositiveInfinity;
        public bool Stopping;
        /// <summary>Particles: done when nothing is left alive. Others end themselves.</summary>
        public ParticleSystem[] Systems;
        public float MadeAt;
    }

    internal static class EffectRuntime
    {
        private const string Tag = "[SMSModForge.PackPlugin] ";

        // ── Building the pack's effects ────────────────────────────────────────────────────────────

        public static void BuildAll(PackContext ctx, ManualLogSource log)
        {
            ctx.Effects = new EffectsRegistry();
            var list = ctx.Pack?.Root[FX.ListKey] as JArray;
            if (list == null || list.Count == 0) return;
            int made = 0;
            foreach (var item in list)
            {
                var def = item as JObject;
                if (def == null) continue;
                string key = (string)def[FX.KeyField];
                if (string.IsNullOrEmpty(key)) continue;
                var template = FX.Find((string)def[FX.TemplateField]);
                if (template == null)
                {
                    log?.LogWarning(Tag + "Effect '" + key + "' of " + ctx.PackId + ": no template '" + (string)def[FX.TemplateField] + "'.");
                    continue;
                }
                var entry = new EffectsRegistry.Entry
                {
                    Key = key,
                    Def = def,
                    Template = template,
                    PlayWhen = (string)def[FX.PlayWhenField] ?? FX.OnAction,
                    Conditions = def[FX.ConditionsField] as JArray ?? new JArray(),
                    UnderPath = (string)def[FX.UnderField],
                };
                ctx.Effects.ByKey[key] = entry;
                made++;

                if (template.CanChangeGames && ((bool?)def[FX.ChangesGameField] ?? false))
                {
                    try { ChangeGames(entry, ctx, log); }
                    catch (Exception ex) { log?.LogWarning(Tag + "Effect '" + key + "' could not change the game's: " + ex.Message); }
                }
            }
            log?.LogInfo(Tag + ctx.PackId + ": " + made + " effect(s).");
        }

        // ── Playing and stopping ───────────────────────────────────────────────────────────────────

        /// <summary>Play the pack's effect <paramref name="key"/>; at (<paramref name="x"/>, <paramref name="y"/>)
        /// of the screen when given, else where the effect says.</summary>
        public static bool Play(PackContext ctx, string key, float? x = null, float? y = null)
        {
            var entry = Find(ctx, key);
            if (entry == null) return false;
            if (IsChangeOfGames(entry))
            {
                ctx.Log?.LogInfo(Tag + "Effect '" + key + "' changes the game's own: the game plays it.");
                return false;
            }
            Playing p = null;
            try { p = Make(entry, ctx, x, y); }
            catch (Exception ex) { ctx.Log?.LogWarning(Tag + "Effect '" + key + "' could not play: " + ex); }
            if (p == null) return false;
            entry.Live.Add(p);
            return true;
        }

        /// <summary>Stop it: particles stop coming and those out finish; the rest end now.</summary>
        public static void Stop(PackContext ctx, string key)
        {
            var entry = Find(ctx, key);
            if (entry == null) return;
            foreach (var p in entry.Live) StopOne(p);
        }

        private static void StopOne(Playing p)
        {
            if (p.Stopping) return;
            p.Stopping = true;
            if (p.Systems != null && p.Root != null)
            {
                foreach (var ps in p.Systems)
                    if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            else if (p.Root != null) UnityEngine.Object.Destroy(p.Root);
        }

        private static EffectsRegistry.Entry Find(PackContext ctx, string key)
        {
            if (ctx?.Effects == null || string.IsNullOrEmpty(key)) return null;
            EffectsRegistry.Entry entry;
            if (ctx.Effects.ByKey.TryGetValue(key, out entry)) return entry;
            ctx.Log?.LogWarning(Tag + "No effect '" + key + "' in " + ctx.PackId + ".");
            return null;
        }

        private static bool IsChangeOfGames(EffectsRegistry.Entry e)
            => e.Template.CanChangeGames && ((bool?)e.Def[FX.ChangesGameField] ?? false);

        // ── Every frame ────────────────────────────────────────────────────────────────────────────

        public static void Tick(PackContext ctx)
        {
            if (ctx?.Effects == null || ctx.Effects.ByKey.Count == 0) return;
            foreach (var entry in ctx.Effects.ByKey.Values)
            {
                Sweep(entry);
                if (IsChangeOfGames(entry)) continue;
                switch (entry.PlayWhen)
                {
                    case FX.WhenTrue:
                    case FX.WhileTrue:
                    {
                        bool now = AllPass(entry.Conditions, ctx);
                        if (entry.PlayWhen == FX.WhenTrue)
                        {
                            if (now && !entry.WasTrue) Play(ctx, entry.Key);
                        }
                        else
                        {
                            bool playing = entry.Live.Any(p => !p.Stopping);
                            if (now && !playing) Play(ctx, entry.Key);
                            else if (!now && playing) foreach (var p in entry.Live) StopOne(p);
                        }
                        entry.WasTrue = now;
                        break;
                    }
                    case FX.WhenUnderTurnsOn:
                        TickUnder(entry, ctx);
                        break;
                }
            }
        }

        private static bool AllPass(JArray conditions, PackContext ctx)
        {
            foreach (var c in conditions)
                if (c is JObject co && !ConditionEvaluator.Evaluate(co, ctx.Vars, ctx.Log, ctx.PackId)) return false;
            return true;
        }

        /// <summary>Something under the object turned on since the last frame: played, once per thing that
        /// did. The first look only learns what is on already.</summary>
        private static void TickUnder(EffectsRegistry.Entry entry, PackContext ctx)
        {
            if (string.IsNullOrEmpty(entry.UnderPath)) return;
            if (entry.UnderRoot == null)
            {
                entry.UnderRoot = TransformExtensions.ResolveGameObject(entry.UnderPath);
                if (entry.UnderRoot == null) return;
            }
            var root = entry.UnderRoot.transform;
            bool fired = false;
            var now = new HashSet<int>();
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i).gameObject;
                if (!child.activeInHierarchy) continue;
                int id = child.GetInstanceID();
                now.Add(id);
                if (entry.Primed && !entry.OnBefore.Contains(id)) fired = true;
            }
            entry.OnBefore.Clear();
            entry.OnBefore.UnionWith(now);
            entry.Primed = true;
            if (fired && AllPass(entry.Conditions, ctx)) Play(ctx, entry.Key);
        }

        /// <summary>Effects that are done, gone: particles with nothing left alive, timed ones past their time.</summary>
        private static void Sweep(EffectsRegistry.Entry entry)
        {
            for (int i = entry.Live.Count - 1; i >= 0; i--)
            {
                var p = entry.Live[i];
                if (p.Root == null) { entry.Live.RemoveAt(i); continue; }
                if (!p.Stopping && Time.time >= p.StopAt) StopOne(p);
                if (p.Systems != null && (p.Stopping || !entry.Template.Loops))
                {
                    bool alive = false;
                    foreach (var ps in p.Systems)
                        if (ps != null && ps.IsAlive(false)) { alive = true; break; }
                    // A copy plays a frame before IsAlive can say anything.
                    if (!alive && Time.time - p.MadeAt > 0.25f)
                    {
                        UnityEngine.Object.Destroy(p.Root);
                        entry.Live.RemoveAt(i);
                    }
                }
            }
        }

        // ── Making one ─────────────────────────────────────────────────────────────────────────────

        private static Transform _root;

        /// <summary>Where every effect on screen lives: one object at the scene's root.</summary>
        private static Transform Root
        {
            get
            {
                if (_root == null) _root = new GameObject("SMSModForge Effects").transform;
                return _root;
            }
        }

        private static Playing Make(EffectsRegistry.Entry e, PackContext ctx, float? x, float? y)
        {
            Playing p;
            switch (e.Template.Kind)
            {
                case FX.Kind.Points: p = MakePoints(e, ctx, x, y); break;
                case FX.Kind.Flash: p = MakeFlash(e); break;
                case FX.Kind.Popup: p = MakePopup(e, ctx, x, y); break;
                default: p = e.Template.IsVanilla ? CopyGames(e, ctx, x, y) : BuildParticles(e, ctx, x, y); break;
            }
            if (p == null) return null;
            p.MadeAt = Time.time;
            float seconds = F(e.Def, FX.SecondsField, 0f);
            if (seconds > 0f && e.Template.Kind == FX.Kind.Particles) p.StopAt = Time.time + seconds;
            return p;
        }

        // ── An object's effect, in a place (1.10.0) ────────────────────────────────────────────────

        /// <summary>
        /// The effect an object of a place carries (its <c>effect</c>), made under the object: at the object, shown
        /// while it is - with its place, and by its own conditions - and played again each time it turns on.
        /// Particles start as the object does; the screen's effects (points, a flash, words) play each time it turns
        /// on.
        /// </summary>
        public static void Attach(GameObject go, JObject def, PackManifest pack, ManualLogSource log, string where)
        {
            if (go == null || def == null) return;
            var template = FX.Find((string)def[FX.TemplateField]);
            if (template == null)
            {
                log?.LogWarning(Tag + where + ": no effect template '" + (string)def[FX.TemplateField] + "'.");
                return;
            }
            var ctx = new PackContext { PackId = pack?.PackId, Pack = pack, Log = log, Effects = new EffectsRegistry() };
            var entry = new EffectsRegistry.Entry { Key = go.name, Def = def, Template = template };
            if (template.Kind != FX.Kind.Particles)
            {
                var player = go.AddComponent<EffectOnEnable>();
                player.Play = () => Make(entry, ctx, null, null);
                return;
            }
            // Particles, made switched off under the object, then left to start with it (play on awake).
            Playing p = template.IsVanilla ? CopyGames(entry, ctx, null, null, under: go.transform) : BuildParticles(entry, ctx, null, null, under: go.transform);
            if (p?.Systems == null) return;
            foreach (var ps in p.Systems)
            {
                var main = ps.main;
                main.playOnAwake = true;
                // One that runs its course plays again each time the object turns on; one that loops, loops.
                if (!template.Loops) main.loop = false;
            }
        }

        // ── Particles: one of the game's ───────────────────────────────────────────────────────────

        private static Playing CopyGames(EffectsRegistry.Entry e, PackContext ctx, float? x, float? y, Transform under = null)
        {
            var source = TransformExtensions.ResolveGameObject(e.Template.GamePath);
            if (source == null)
            {
                ctx.Log?.LogWarning(Tag + "Effect '" + e.Key + "': the game has no '" + e.Template.GamePath + "' (a game update?).");
                return null;
            }
            // Copied under a parent that is off, so nothing on the copy wakes before its scripts are taken off: a
            // copied Trigger would run the game's logic (RipPayout's sets the game's variables), a copied Volume
            // tints the whole screen.
            var hold = new GameObject("SMSModForge effect being made");
            hold.SetActive(false);
            var copy = UnityEngine.Object.Instantiate(source, hold.transform, false);
            copy.name = "Effect " + ctx.PackId + "." + e.Key;
            foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (b != null) UnityEngine.Object.DestroyImmediate(b);

            float scale = F(e.Def, FX.ScaleField, 1f);
            var systems = copy.GetComponentsInChildren<ParticleSystem>(true);
            if (under != null)
            {
                // An object's effect: at the object, as big as the game has it, starting with the object.
                copy.transform.SetParent(under, false);
                UnityEngine.Object.Destroy(hold);
                copy.transform.localPosition = Vector3.zero;
                copy.transform.rotation = source.transform.rotation;
                var parent = under.lossyScale;
                var want = source.transform.lossyScale * scale;
                copy.transform.localScale = new Vector3(Div(want.x, parent.x), Div(want.y, parent.y), Div(want.z, parent.z));
                foreach (var ps in systems) ps.gameObject.SetActive(true);
                Adjust(e, ctx, systems, scale, forge: false);
                copy.SetActive(true);
                return new Playing { Root = copy, Systems = systems };
            }

            copy.transform.SetParent(Root, false);
            UnityEngine.Object.Destroy(hold);
            copy.transform.rotation = source.transform.rotation;
            copy.transform.localScale = source.transform.lossyScale * scale;
            copy.transform.position = Where(e, x, y, source.transform.position);
            copy.SetActive(true);

            Adjust(e, ctx, systems, scale, forge: false);
            foreach (var ps in systems)
            {
                ps.gameObject.SetActive(true);
                ps.Clear(false);
                ps.Play(false);
            }
            return new Playing { Root = copy, Systems = systems };
        }

        /// <summary>The game's own effect changed where it is, for as long as the pack runs: its picture, colour,
        /// how many, size, speed, how long each lasts. The social points: their picture, how many each photo
        /// gets, how far they spread, how long they float and fly.</summary>
        private static void ChangeGames(EffectsRegistry.Entry e, PackContext ctx, ManualLogSource log)
        {
            var target = TransformExtensions.ResolveGameObject(e.Template.GamePath);
            if (target == null)
            {
                log?.LogWarning(Tag + "Effect '" + e.Key + "': the game has no '" + e.Template.GamePath + "' to change.");
                return;
            }
            if (e.Template.Kind == FX.Kind.Points)
            {
                GamePoints.Change(target, e.Def, ctx, log);
                return;
            }
            var systems = target.GetComponentsInChildren<ParticleSystem>(true);
            Adjust(e, ctx, systems, F(e.Def, FX.ScaleField, 1f), forge: false, keepLayers: true);
            log?.LogInfo(Tag + "Effect '" + e.Key + "' changed the game's " + e.Template.GamePath + ".");
        }

        // ── Particles: ModForge's own ──────────────────────────────────────────────────────────────

        private static float Div(float a, float b) => Mathf.Abs(b) < 1e-5f ? a : a / b;

        private static Playing BuildParticles(EffectsRegistry.Entry e, PackContext ctx, float? x, float? y, Transform under = null)
        {
            var go = new GameObject("Effect " + ctx.PackId + "." + e.Key);
            go.SetActive(false);
            go.transform.SetParent(under ?? Root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            ForgeShapes.Shape(e.Template.Name, ps, r);
            float scale = F(e.Def, FX.ScaleField, 1f);
            var systems = new[] { ps };
            if (under != null)
            {
                // An object's effect: at the object, at the game's scale whatever the object's, starting with it.
                var parent = under.lossyScale;
                go.transform.localPosition = Vector3.zero;
                go.transform.localScale = new Vector3(Div(scale, parent.x), Div(scale, parent.y), Div(scale, parent.z));
                Adjust(e, ctx, systems, scale, forge: true);
                go.SetActive(true);
                return new Playing { Root = go, Systems = systems };
            }
            go.transform.localScale = Vector3.one * scale;
            var home = ForgeShapes.Home(e.Template.Name);
            go.transform.position = Where(e, x, y, ViewportToWorld(home.x, home.y));
            Adjust(e, ctx, systems, scale, forge: true);
            go.SetActive(true);
            ps.Play(false);
            return new Playing { Root = go, Systems = systems };
        }

        // ── What an author changes on particles ────────────────────────────────────────────────────

        private static void Adjust(EffectsRegistry.Entry e, PackContext ctx, ParticleSystem[] systems, float scale, bool forge,
                                   bool keepLayers = false)
        {
            var def = e.Def;
            float amount = F(def, FX.AmountField, 1f), size = F(def, FX.SizeField, 1f), speed = F(def, FX.SpeedField, 1f),
                  lifetime = F(def, FX.LifetimeField, 1f);
            float? gravity = def[FX.GravityField] != null ? F(def, FX.GravityField, 0f) : (float?)null;
            Color? tint = Colour((string)def[FX.ColorField]);
            Texture texture = Picture(def, ctx);
            bool glow = (bool?)def[FX.GlowField] ?? false;

            string layer = (string)def[FX.LayerField] ?? e.Template.DefaultLayer;
            int lowest = int.MaxValue;
            foreach (var ps in systems)
            {
                var r0 = ps.GetComponent<ParticleSystemRenderer>();
                if (r0 != null) lowest = Math.Min(lowest, r0.sortingOrder);
            }
            if (lowest == int.MaxValue) lowest = 0;

            foreach (var ps in systems)
            {
                var main = ps.main;
                if (!Mathf.Approximately(amount, 1f))
                {
                    var em = ps.emission;
                    em.rateOverTimeMultiplier *= amount;
                    em.rateOverDistanceMultiplier *= amount;
                    for (int i = 0; i < em.burstCount; i++)
                    {
                        var b = em.GetBurst(i);
                        b.count = Times(b.count, amount);
                        em.SetBurst(i, b);
                    }
                    main.maxParticles = Mathf.Max(1, Mathf.CeilToInt(main.maxParticles * amount));
                }
                if (!Mathf.Approximately(size, 1f)) main.startSizeMultiplier *= size;
                // Systems that ignore the scale of what they are under take it here.
                if (!forge && !Mathf.Approximately(scale, 1f) && main.scalingMode != ParticleSystemScalingMode.Hierarchy)
                    main.startSizeMultiplier *= scale;
                if (!Mathf.Approximately(speed, 1f))
                {
                    main.startSpeedMultiplier *= speed;
                    var vel = ps.velocityOverLifetime;
                    if (vel.enabled) { vel.xMultiplier *= speed; vel.yMultiplier *= speed; vel.zMultiplier *= speed; }
                }
                if (!Mathf.Approximately(lifetime, 1f)) main.startLifetimeMultiplier *= lifetime;
                if (gravity.HasValue) main.gravityModifierMultiplier = gravity.Value;
                if (tint.HasValue) main.startColor = Tinted(main.startColor, tint.Value);

                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r == null) continue;
                if (!keepLayers)
                {
                    r.sortingLayerName = layer == FX.Behind ? FX.BehindSortingLayer : FX.FrontSortingLayer;
                    r.sortingOrder = FX.SortingOrder + (r.sortingOrder - lowest);
                }
                // ModForge's own draw with one of the game's particle materials and the template's picture unless
                // the author gave one; the game's keep theirs unless the author gave one.
                var picture = texture ?? (forge ? GameTexture(e.Template.Texture) : null);
                if (picture != null || forge)
                {
                    // One of the game's pictures draws with a material of the game's that already draws it: the
                    // Cartoon FX pictures keep their shape in one channel, which a plain particle shader shows as
                    // squares (seen in the test copy). An author's own picture draws with the game's plain one.
                    var material = forge
                        ? (texture == null || IsGames(texture) ? ForgeShapes.MaterialFor(picture, glow) : ForgeShapes.Material(glow))
                        : r.sharedMaterial;
                    if (material != null)
                    {
                        material = new Material(material);
                        if (picture != null)
                        {
                            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", picture);
                            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", picture);
                            // A picture of the author's own is one picture, not the game's sheet of them.
                            var sheet = ps.textureSheetAnimation;
                            if (sheet.enabled && !forge) sheet.enabled = false;
                        }
                        r.sharedMaterial = material;
                    }
                }
            }
        }

        private static ParticleSystem.MinMaxCurve Times(ParticleSystem.MinMaxCurve c, float by)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant: c.constant = Mathf.Max(0, c.constant * by); break;
                case ParticleSystemCurveMode.TwoConstants: c.constantMin *= by; c.constantMax *= by; break;
                default: c.curveMultiplier *= by; break;
            }
            return c;
        }

        private static ParticleSystem.MinMaxGradient Tinted(ParticleSystem.MinMaxGradient g, Color tint)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(g.color * tint);
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(g.colorMin * tint, g.colorMax * tint);
                default: return new ParticleSystem.MinMaxGradient(tint);
            }
        }

        // ── The social points' motion ─────────────────────────────────────────────────────────────

        private static Playing MakePoints(EffectsRegistry.Entry e, PackContext ctx, float? x, float? y)
        {
            var def = e.Def;
            var canvas = ScreenCanvas("Effect " + ctx.PackId + "." + e.Key);
            var burst = canvas.gameObject.AddComponent<PointsBurst>();
            Sprite sprite = null;
            var texture = Picture(def, ctx);
            Vector2 size = new Vector2(48, 48);
            Color iconColour = Color.white;
            if (texture is Texture2D t2) sprite = Sprite.Create(t2, new Rect(0, 0, t2.width, t2.height), new Vector2(0.5f, 0.5f));
            if (e.Template.IsVanilla)
            {
                var icon = GamePoints.Icon(e.Template.GamePath);
                if (icon != null)
                {
                    if (sprite == null) { sprite = icon.sprite; iconColour = icon.color; }
                    size = icon.rectTransform.sizeDelta;
                }
            }
            if (sprite == null)
            {
                // ModForge's own points draw the game's pink heart unless the author gave a picture.
                var heart = GamePoints.Icon(GameTemplatePath(FX.SubscriberPoints));
                if (heart != null) { sprite = heart.sprite; size = heart.rectTransform.sizeDelta; iconColour = heart.color; }
            }
            float scale = F(def, FX.ScaleField, 1f);
            burst.Sprite = sprite;
            burst.Size = size * scale;
            // The icon's own colour (the game's hearts are pink, its followers light blue), times the author's.
            burst.Tint = iconColour * (Colour((string)def[FX.ColorField]) ?? Color.white);
            burst.From = new Vector2(x ?? F(def, FX.XField, FX.PointsStartX), y ?? F(def, FX.YField, FX.PointsStartY));
            burst.To = new Vector2(F(def, FX.EndXField, FX.PointsEndX), F(def, FX.EndYField, FX.PointsEndY));
            burst.Spread = F(def, FX.SpreadField, FX.DefaultPointsSpread);
            burst.FloatSeconds = F(def, FX.FloatSecondsField, FX.DefaultFloatSeconds);
            burst.TravelSeconds = F(def, FX.TravelSecondsField, FX.DefaultTravelSeconds);
            burst.Count = Mathf.Max(1, Mathf.RoundToInt(F(def, FX.AmountField, FX.DefaultPointsCount)));
            burst.Go();
            return new Playing { Root = canvas.gameObject };
        }

        private static string GameTemplatePath(string id)
        {
            var t = FX.Find(id);
            return t != null ? t.GamePath : null;
        }

        // ── A flash ────────────────────────────────────────────────────────────────────────────────

        private static Playing MakeFlash(EffectsRegistry.Entry e)
        {
            var canvas = ScreenCanvas("Effect flash " + e.Key);
            var flash = canvas.gameObject.AddComponent<ScreenFlash>();
            flash.Colour = Colour((string)e.Def[FX.ColorField]) ?? Color.white;
            flash.Seconds = Mathf.Max(0.05f, F(e.Def, FX.SecondsField, FX.DefaultFlashSeconds));
            flash.Go();
            return new Playing { Root = canvas.gameObject };
        }

        // ── Words that float up ────────────────────────────────────────────────────────────────────

        private static Playing MakePopup(EffectsRegistry.Entry e, PackContext ctx, float? x, float? y)
        {
            var canvas = ScreenCanvas("Effect popup " + e.Key);
            var popup = canvas.gameObject.AddComponent<FloatingWords>();
            string text = (string)e.Def[FX.TextField] ?? "";
            if (ctx.Vars != null) text = TextPlaceholders.ResolveAll(text, ctx.Vars);
            text = TextPlaceholders.ResolveNames(text, ctx.PackId);
            popup.Text = text;
            popup.Colour = Colour((string)e.Def[FX.ColorField]) ?? Color.white;
            popup.Size = 54f * F(e.Def, FX.SizeField, 1f);
            popup.At = new Vector2(x ?? F(e.Def, FX.XField, 0.5f), y ?? F(e.Def, FX.YField, 0.55f));
            popup.Seconds = Mathf.Max(0.2f, F(e.Def, FX.SecondsField, FX.DefaultPopupSeconds));
            popup.Font = GameFont();
            popup.Go();
            return new Playing { Root = canvas.gameObject };
        }

        // ── Shared pieces ──────────────────────────────────────────────────────────────────────────

        /// <summary>A canvas over the world and the game's own screen effects, under a conversation's box.</summary>
        private static Canvas ScreenCanvas(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = FX.ScreenCanvasOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            return canvas;
        }

        /// <summary>Where on the screen (fractions, from the bottom left) in the world, on the plane the game's
        /// art is on.</summary>
        internal static Vector3 ViewportToWorld(float x, float y)
        {
            var cam = Camera.main;
            if (cam == null) return new Vector3((x - 0.5f) * FX.CameraHalfHeight * 2f * 16f / 9f, (y - 0.5f) * FX.CameraHalfHeight * 2f, 0f);
            var w = cam.ViewportToWorldPoint(new Vector3(x, y, -cam.transform.position.z));
            return new Vector3(w.x, w.y, 0f);
        }

        private static Vector3 Where(EffectsRegistry.Entry e, float? x, float? y, Vector3 otherwise)
        {
            float? ex = x ?? (e.Def[FX.XField] != null ? F(e.Def, FX.XField, 0.5f) : (float?)null);
            float? ey = y ?? (e.Def[FX.YField] != null ? F(e.Def, FX.YField, 0.5f) : (float?)null);
            if (!ex.HasValue && !ey.HasValue) return otherwise;
            var at = ViewportToWorld(ex ?? 0.5f, ey ?? 0.5f);
            return new Vector3(ex.HasValue ? at.x : otherwise.x, ey.HasValue ? at.y : otherwise.y, otherwise.z);
        }

        /// <summary>The picture an author chose: one of the pack's, or one of the game's by name.</summary>
        private static Texture Picture(JObject def, PackContext ctx)
        {
            string rel = (string)def[FX.TextureField];
            if (!string.IsNullOrEmpty(rel) && ctx.Pack != null && ctx.Pack.Has(rel))
                return LoadedArt.Texture(ctx.Pack, rel, "effect", () => Decode(ctx.Pack, rel));
            string game = (string)def[FX.GameTextureField];
            return string.IsNullOrEmpty(game) ? null : GameTexture(game);
        }

        private static Texture2D Decode(PackManifest pack, string rel)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, true);
            byte[] bytes = pack.ReadBytes(rel);
            if (bytes != null) tex.LoadImage(bytes);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private static Dictionary<string, Texture> _gameTextures;

        internal static Texture GameTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_gameTextures == null || _gameTextures.Values.Any(t => t == null))
            {
                _gameTextures = new Dictionary<string, Texture>(StringComparer.Ordinal);
                foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                    if (t != null && !_gameTextures.ContainsKey(t.name)) _gameTextures[t.name] = t;
            }
            Texture found;
            return _gameTextures.TryGetValue(name, out found) ? found : null;
        }

        /// <summary>One of the game's pictures, not one a pack brought.</summary>
        private static bool IsGames(Texture t) => t != null && GameTexture(t.name) == t;

        private static TMP_FontAsset _font;

        /// <summary>The game's own pop-up words' font (Game_Saved's "Game Saved").</summary>
        internal static TMP_FontAsset GameFont()
        {
            if (_font != null) return _font;
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            _font = fonts.FirstOrDefault(f => f != null && f.name == "Curse Casual SDF Outline")
                    ?? fonts.FirstOrDefault(f => f != null && f.name.StartsWith("Curse Casual", StringComparison.Ordinal))
                    ?? TMP_Settings.defaultFontAsset;
            return _font;
        }

        internal static float F(JObject def, string field, float otherwise)
        {
            var v = def[field];
            if (v == null || v.Type == JTokenType.Null) return otherwise;
            if (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) return (float)v;
            float f;
            return float.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : otherwise;
        }

        internal static Color? Colour(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            Color c;
            return BustFactory.TryParseHexColor(hex, out c) ? c : (Color?)null;
        }
    }
}
