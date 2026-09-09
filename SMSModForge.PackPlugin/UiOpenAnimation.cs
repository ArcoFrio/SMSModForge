using System.Collections;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// How an object arrives when it is switched on.
    /// <para/>
    /// The game's own shape, read out of a running build: snap to a starting
    /// state, then ease to the resting one. The shop sets its CanvasGroup to 0
    /// and its scale to (1,0,1) with no duration, then takes both to normal
    /// over 0.3s on QuadInOut - it fades in while unfolding from a flat line.
    /// <para/>
    /// On enable rather than on start, because that is the moment being
    /// animated and it happens again every time: a screen closed and reopened
    /// plays it once more, which is what the game does.
    /// </summary>
    public class UiOpenAnimation : MonoBehaviour
    {
        /// <summary>Fade up from invisible.</summary>
        public bool Fade;

        /// <summary>Scale to start from, or null to leave the size alone.</summary>
        public Vector3? ScaleFrom;

        public float Duration = 0.3f;

        /// <summary>One of the names in the pack format: Linear, QuadInOut,
        /// BounceOut, ElasticOut.</summary>
        public string Easing = "QuadInOut";

        private CanvasGroup _group;
        private Vector3 _resting = Vector3.one;
        private bool _known;

        private void Awake()
        {
            // Read before anything has had a chance to move it, and kept: this
            // is what the object is supposed to look like once it has arrived.
            _resting = transform.localScale;
            _known = true;

            if (Fade)
            {
                _group = GetComponent<CanvasGroup>();
                if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void OnEnable()
        {
            if (!_known) { _resting = transform.localScale; _known = true; }
            StartCoroutine(Play());
        }

        private void OnDisable()
        {
            // Switched off part-way through: put everything back, or the object
            // comes back half faded and half folded and stays that way. The
            // same trap the button press fell into.
            StopAllCoroutines();
            transform.localScale = _resting;
            if (_group != null) _group.alpha = 1f;
        }

        private IEnumerator Play()
        {
            if (Duration <= 0f)
            {
                if (_group != null) _group.alpha = 1f;
                transform.localScale = _resting;
                yield break;
            }

            var from = ScaleFrom ?? _resting;

            // Snapped first, in one frame, so nothing is ever seen at rest
            // before it animates.
            if (_group != null) _group.alpha = 0f;
            if (ScaleFrom.HasValue) transform.localScale = from;

            // Unscaled, matching the game: a screen opening during a paused or
            // slowed moment still opens.
            for (float t = 0f; t < Duration; t += Time.unscaledDeltaTime)
            {
                float k = Ease(t / Duration, Easing);
                if (_group != null) _group.alpha = Mathf.Clamp01(k);
                if (ScaleFrom.HasValue) transform.localScale = Vector3.LerpUnclamped(from, _resting, k);
                yield return null;
            }

            if (_group != null) _group.alpha = 1f;
            transform.localScale = _resting;
        }

        /// <summary>
        /// The curves the game uses. Nothing else is offered, because nothing
        /// else is demonstrated anywhere in it - and an easing nobody can point
        /// at in the game is a guess dressed as a feature.
        /// </summary>
        public static float Ease(float t, string easing)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            if (easing == "Linear") return t;

            if (easing == "BounceOut")
            {
                // Robert Penner's, which is what every engine's BounceOut is.
                const float n = 7.5625f, d = 2.75f;
                if (t < 1f / d) return n * t * t;
                if (t < 2f / d) { t -= 1.5f / d; return n * t * t + 0.75f; }
                if (t < 2.5f / d) { t -= 2.25f / d; return n * t * t + 0.9375f; }
                t -= 2.625f / d;
                return n * t * t + 0.984375f;
            }

            if (easing == "ElasticOut")
            {
                const float period = 0.3f;
                float s = period / 4f;
                return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - s) * (2f * Mathf.PI) / period) + 1f;
            }

            // QuadInOut, the default and the shop's.
            return t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
        }
    }
}
