using System.Collections;
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

        private bool _configured;
        private SpriteRenderer _sr;
        private Coroutine _co;

        public void Configure(float blinkInterval, float minAlpha, float maxAlpha)
        {
            BlinkInterval = Mathf.Max(0.01f, blinkInterval); MinAlpha = minAlpha; MaxAlpha = maxAlpha;
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
            bool hi = false;
            while (true)
            {
                var c = _sr.color; c.a = hi ? MaxAlpha : MinAlpha; _sr.color = c;
                hi = !hi;
                yield return new WaitForSeconds(BlinkInterval);
            }
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
                    log?.LogWarning("[SMSModForge.PackPlugin] Unknown pack component type '" + type + "' — skipped.");
                    break;
            }
        }

        private static float F(JObject c, string key, float dflt) => c[key] != null ? (float)c[key] : dflt;
        private static bool B(JObject c, string key, bool dflt) => c[key] != null ? (bool)c[key] : dflt;
    }
}
