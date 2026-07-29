using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    // Generic, reusable utility MonoBehaviours a pack can attach to its Extra
    // GameObjects (overlays). These mirror the vanilla game's own components by
    // name — FadeInSprite / FadeOutSprite / RandomChildActivator /
    // BlinkingSprite — but are re-implemented generically here so any mod can
    // use them without a game-specific dependency. Each reacts to being
    // *enabled*: OnEnable (re)starts its effect, so toggling the host
    // GameObject active (e.g. via SetGameObjectActive) drives the behaviour.
    // Config is pushed in via Configure() before/around enable; whichever of
    // Configure()/OnEnable lands last kicks the effect off, so attach-order vs.
    // active-state never matters.

    /// <summary>Fades a <see cref="SpriteRenderer"/> in — alpha 0 → target over
    /// a duration, after an optional delay.</summary>
    public sealed class FadeInSprite : MonoBehaviour
    {
        public float FadeDuration = 1f;
        public float TargetAlpha = 1f;
        public float StartDelay = 0f;

        private bool _configured;
        private SpriteRenderer _sr;
        private Coroutine _co;

        public void Configure(float fadeDuration, float targetAlpha, float startDelay)
        {
            FadeDuration = fadeDuration; TargetAlpha = targetAlpha; StartDelay = startDelay;
            _configured = true;
            if (isActiveAndEnabled) Restart();
        }

        private void OnEnable() { if (_configured) Restart(); }
        private void OnDisable() { if (_co != null) { StopCoroutine(_co); _co = null; } }
        private void Restart() { if (_co != null) StopCoroutine(_co); _co = StartCoroutine(Run()); }

        private IEnumerator Run()
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) yield break;
            SetAlpha(0f);
            if (StartDelay > 0f) yield return new WaitForSeconds(StartDelay);
            float dur = Mathf.Max(0.0001f, FadeDuration);
            float t = 0f;
            while (t < dur) { t += Time.deltaTime; SetAlpha(Mathf.Lerp(0f, TargetAlpha, t / dur)); yield return null; }
            SetAlpha(TargetAlpha);
            _co = null;
        }

        private void SetAlpha(float a) { var c = _sr.color; c.a = a; _sr.color = c; }
    }

    /// <summary>Fades a <see cref="SpriteRenderer"/> out — current alpha → 0 over
    /// a duration, after an optional delay. Optionally deactivates the
    /// GameObject once fully faded (a flash that vanishes).</summary>
    public sealed class FadeOutSprite : MonoBehaviour
    {
        public float Duration = 1f;
        public float StartDelay = 0f;
        public bool DeactivateOnComplete = false;

        private bool _configured;
        private SpriteRenderer _sr;
        private Coroutine _co;

        public void Configure(float duration, float startDelay, bool deactivateOnComplete)
        {
            Duration = duration; StartDelay = startDelay; DeactivateOnComplete = deactivateOnComplete;
            _configured = true;
            if (isActiveAndEnabled) Restart();
        }

        private void OnEnable() { if (_configured) Restart(); }
        private void OnDisable() { if (_co != null) { StopCoroutine(_co); _co = null; } }
        private void Restart() { if (_co != null) StopCoroutine(_co); _co = StartCoroutine(Run()); }

        private IEnumerator Run()
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) yield break;
            if (StartDelay > 0f) yield return new WaitForSeconds(StartDelay);
            float start = _sr.color.a;
            float dur = Mathf.Max(0.0001f, Duration);
            float t = 0f;
            while (t < dur) { t += Time.deltaTime; SetAlpha(Mathf.Lerp(start, 0f, t / dur)); yield return null; }
            SetAlpha(0f);
            _co = null;
            if (DeactivateOnComplete) gameObject.SetActive(false);
        }

        private void SetAlpha(float a) { var c = _sr.color; c.a = a; _sr.color = c; }
    }

    /// <summary>On enable, activates exactly one random child GameObject and
    /// disables the rest — a generic "pick a random variant" switch.</summary>
    public sealed class RandomChildActivator : MonoBehaviour
    {
        public bool ReshuffleOnEnable = true;

        private bool _configured;

        public void Configure(bool reshuffleOnEnable)
        {
            ReshuffleOnEnable = reshuffleOnEnable;
            _configured = true;
            if (isActiveAndEnabled) Activate();
        }

        private void OnEnable() { if (_configured && ReshuffleOnEnable) Activate(); }

        private void Activate()
        {
            int n = transform.childCount;
            if (n == 0) return;
            int pick = Random.Range(0, n);
            for (int i = 0; i < n; i++) transform.GetChild(i).gameObject.SetActive(i == pick);
        }
    }

    /// <summary>Toggles a <see cref="SpriteRenderer"/>'s alpha between a low and
    /// high value on a fixed interval — a blink / pulse.</summary>
    public sealed class BlinkingSprite : MonoBehaviour
    {
        public float BlinkInterval = 0.5f;
        public float MinAlpha = 0f;
        public float MaxAlpha = 1f;

        // Two-phase mode: eyes open a random OpenMin..OpenMax, shut for
        // ClosedHold. Off (ClosedHold <= 0) leaves the even pulse above, which
        // is what a pack author attaching this as a utility component wants.
        public float OpenMin, OpenMax, ClosedHold;

        private bool _configured;
        private SpriteRenderer _sr;
        private Coroutine _co;

        public void Configure(float blinkInterval, float minAlpha, float maxAlpha)
        {
            BlinkInterval = Mathf.Max(0.01f, blinkInterval); MinAlpha = minAlpha; MaxAlpha = maxAlpha;
            ClosedHold = 0f;
            _configured = true;
            if (isActiveAndEnabled) Restart();
        }

        /// <summary>
        /// Configure an actual BLINK rather than a pulse: a long random open
        /// phase and a brief shut one. An even pulse can't express this — with a
        /// single interval the eyes stay closed exactly as long as they stay
        /// open, which at NPC blink timings is seconds at a time.
        /// </summary>
        public void ConfigureBlink(float openMin, float openMax, float closedHold,
                                   float minAlpha, float maxAlpha)
        {
            if (openMax < openMin) { var t = openMin; openMin = openMax; openMax = t; }
            OpenMin = Mathf.Max(0.01f, openMin);
            OpenMax = Mathf.Max(OpenMin, openMax);
            ClosedHold = Mathf.Max(0.01f, closedHold);
            MinAlpha = minAlpha; MaxAlpha = maxAlpha;
            _configured = true;
            if (isActiveAndEnabled) Restart();
        }

        private void OnEnable() { if (_configured) Restart(); }
        private void OnDisable() { if (_co != null) { StopCoroutine(_co); _co = null; } }
        private void Restart() { if (_co != null) StopCoroutine(_co); _co = StartCoroutine(Run()); }

        private IEnumerator Run()
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) yield break;

            if (ClosedHold > 0f)
            {
                // Start open, matching the vanilla component's OnEnable.
                while (true)
                {
                    SetAlpha(MinAlpha);
                    yield return new WaitForSeconds(Random.Range(OpenMin, OpenMax));
                    SetAlpha(MaxAlpha);
                    yield return new WaitForSeconds(ClosedHold);
                }
            }

            bool hi = false;
            while (true)
            {
                SetAlpha(hi ? MaxAlpha : MinAlpha);
                hi = !hi;
                yield return new WaitForSeconds(BlinkInterval);
            }
        }

        private void SetAlpha(float a)
        {
            var c = _sr.color; c.a = a; _sr.color = c;
        }
    }

    /// <summary>
    /// Adds + configures one of the generic pack utility components on a
    /// GameObject from its authored JSON (<c>{ "type": ..., ...fields }</c>).
    /// Unknown types are logged and skipped so a typo can't crash the build.
    /// </summary>
    public static class PackComponentFactory
    {
        public static void Apply(GameObject go, JObject c, ManualLogSource log)
        {
            if (go == null || c == null) return;
            string type = (string)c["type"];
            switch (type)
            {
                case "FadeInSprite":
                // "FadeSprite" was the pre-rename name — accept it as a fade-in.
                case "FadeSprite":
                    go.AddComponent<FadeInSprite>().Configure(
                        F(c, "fadeDuration", 1f), F(c, "targetAlpha", 1f), F(c, "startDelay", 0f));
                    break;
                case "FadeOutSprite":
                // "DisappearAfterDelay" was the pre-rename name — map delay→startDelay,
                // fadeOutDuration→duration, destroy→deactivate.
                case "DisappearAfterDelay":
                    go.AddComponent<FadeOutSprite>().Configure(
                        F(c, "duration", F(c, "fadeOutDuration", 1f)),
                        F(c, "startDelay", F(c, "delay", 0f)),
                        B(c, "deactivateOnComplete", B(c, "destroy", true)));
                    break;
                case "RandomChildActivator":
                    go.AddComponent<RandomChildActivator>().Configure(B(c, "reshuffleOnEnable", true));
                    break;
                case "BlinkingSprite":
                // "BlinkSprite" was the pre-rename name.
                case "BlinkSprite":
                    go.AddComponent<BlinkingSprite>().Configure(
                        F(c, "blinkInterval", 0.5f), F(c, "minAlpha", 0f), F(c, "maxAlpha", 1f));
                    break;
                default:
                    // Not one of ours — try the game's own. A vanilla level is
                    // mostly its own behaviour scripts (ParallaxMouseEffect,
                    // OffsetScrolling, DisableChildren…), and there is no reason
                    // a pack should be limited to the handful reimplemented
                    // above when the real ones are already loaded.
                    if (!ApplyByReflection(go, type, c, log))
                        log?.LogWarning("[SMSModForge.PackPlugin] Unknown pack component type '" + type +
                                        "' — no pack component and no loaded type by that name; skipped.");
                    break;
            }
        }

        /// <summary>
        /// Attach a component the GAME defines, by type name, and set whatever
        /// authored values match its fields or properties.
        /// <para/>
        /// Names come straight from the vanilla extraction, which reads them off
        /// Unity's serialized properties — so for the game's own scripts a key
        /// like <c>parallaxStrength</c> is the actual field. Engine components
        /// are a different matter: their serialized names are Unity internals
        /// (<c>m_CastShadows</c>) that don't correspond to anything settable by
        /// that name, so those keys simply find no target and are reported.
        /// Nothing here throws — a pack naming a type that isn't loaded, or a
        /// value that won't convert, costs that one component and not the build.
        /// </summary>
        private static bool ApplyByReflection(GameObject go, string typeName, JObject c, ManualLogSource log)
        {
            if (string.IsNullOrEmpty(typeName)) return false;
            var t = ResolveType(typeName);
            if (t == null || !typeof(Component).IsAssignableFrom(t)) return false;

            Component comp;
            try { comp = go.AddComponent(t); }
            catch (System.Exception ex)
            {
                log?.LogWarning("[SMSModForge.PackPlugin] Could not add '" + typeName + "': " + ex.Message);
                return true;   // resolved, so don't also report it as unknown
            }
            if (comp == null) return true;

            foreach (var prop in c)
            {
                if (prop.Key == "type") continue;
                if (!TrySetMember(comp, t, prop.Key, prop.Value))
                    log?.LogInfo("[SMSModForge.PackPlugin] " + typeName + "." + prop.Key +
                                 " — no settable field or property by that name; left at its default.");
            }
            return true;
        }

        private static readonly Dictionary<string, System.Type> _typeCache =
            new Dictionary<string, System.Type>();

        /// <summary>Find a loaded type by its short name. Cached — the scan walks
        /// every loaded assembly and a level can carry hundreds of components.</summary>
        private static System.Type ResolveType(string name)
        {
            if (_typeCache.TryGetValue(name, out var hit)) return hit;
            System.Type found = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                        if (t.Name == name && typeof(Component).IsAssignableFrom(t)) { found = t; break; }
                }
                catch { /* a dynamic or partially-loaded assembly — skip it */ }
                if (found != null) break;
            }
            _typeCache[name] = found;
            return found;
        }

        private static bool TrySetMember(Component target, System.Type t, string name, JToken value)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.IgnoreCase;
            try
            {
                var f = t.GetField(name, Flags);
                if (f != null && !f.IsInitOnly)
                {
                    f.SetValue(target, Convert(value, f.FieldType));
                    return true;
                }
                var p = t.GetProperty(name, Flags);
                if (p != null && p.CanWrite)
                {
                    p.SetValue(target, Convert(value, p.PropertyType), null);
                    return true;
                }
            }
            catch { /* wrong shape for this member — treated as not settable */ }
            return false;
        }

        /// <summary>Coerce an authored JSON value to a member's type. Covers what
        /// the extractor emits: numbers, bools, strings, enums by name, and the
        /// Vector2/3 and Color it writes as arrays and hex.</summary>
        private static object Convert(JToken v, System.Type target)
        {
            if (target == typeof(string)) return (string)v;
            if (target.IsEnum) return System.Enum.Parse(target, (string)v, true);
            if (target == typeof(Vector2)) return new Vector2((float)v[0], (float)v[1]);
            if (target == typeof(Vector3)) return new Vector3((float)v[0], (float)v[1], (float)v[2]);
            if (target == typeof(Color))
            {
                ColorUtility.TryParseHtmlString("#" + ((string)v).TrimStart('#'), out var col);
                return col;
            }
            return v.ToObject(target);
        }

        private static float F(JObject c, string key, float dflt) => c[key] != null ? (float)c[key] : dflt;
        private static bool B(JObject c, string key, bool dflt) => c[key] != null ? (bool)c[key] : dflt;
    }
}
