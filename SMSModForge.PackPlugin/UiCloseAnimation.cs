using System;
using System.Collections;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// How an object leaves when it is switched off.
    /// <para/>
    /// The game's own shop, read out of a running build: its close button folds
    /// ShopCore to (1,0,1) over 0.3s on QuadInOut, WAITS for that to finish, and
    /// only then switches the object off. Scale alone - the way in fades and
    /// unfolds, the way out only folds.
    /// <para/>
    /// It cannot be done from OnDisable, and that is not a detail: a deactivated
    /// object stops its coroutines, so by the time anything notices it is going
    /// there is nothing left to animate with. Whoever asks for it to close has
    /// to wait, which is why <see cref="ActionRuntime"/> hands the closing over
    /// here instead of setting the object inactive itself.
    /// </summary>
    public class UiCloseAnimation : MonoBehaviour
    {
        /// <summary>Fade down to invisible.</summary>
        public bool Fade;

        /// <summary>Scale to end at, or null to leave the size alone.</summary>
        public Vector3? ScaleTo;

        public float Duration = 0.3f;

        public string Easing = "QuadInOut";

        private CanvasGroup _group;
        private Vector3 _resting = Vector3.one;
        private bool _known;
        private bool _closing;

        /// <summary>Whether anything would actually be seen.</summary>
        public bool DoesAnything => Duration > 0f && (Fade || ScaleTo.HasValue);

        private void Awake()
        {
            _resting = transform.localScale;
            _known = true;

            if (Fade)
            {
                _group = GetComponent<CanvasGroup>();
                if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            }
        }

        /// <summary>
        /// Play it, then switch the object off.
        /// <para/>
        /// Asked twice - a close button pressed twice quickly - the second is
        /// ignored rather than starting a second fold from half way.
        /// </summary>
        public void Close()
        {
            if (!isActiveAndEnabled || !DoesAnything) { Finish(); return; }
            if (_closing) return;

            if (!_known) { _resting = transform.localScale; _known = true; }
            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            _closing = true;

            var to = ScaleTo ?? _resting;
            float fromAlpha = _group != null ? _group.alpha : 1f;

            for (float t = 0f; t < Duration; t += Time.unscaledDeltaTime)
            {
                float k = UiOpenAnimation.Ease(t / Duration, Easing);
                if (_group != null) _group.alpha = Mathf.Clamp01(fromAlpha * (1f - k));
                if (ScaleTo.HasValue) transform.localScale = Vector3.LerpUnclamped(_resting, to, k);
                yield return null;
            }

            Finish();
        }

        /// <summary>
        /// Off, and back to how it looked before - so the next time it opens it
        /// does not start from the folded, faded state this left it in.
        /// </summary>
        private void Finish()
        {
            _closing = false;
            if (_group != null) _group.alpha = 1f;
            if (_known) transform.localScale = _resting;
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            // Switched off by something that did not ask - a scene change, a
            // parent going away - part-way through. Put it back, or it comes
            // back flat and stays that way.
            if (!_closing) return;
            StopAllCoroutines();
            _closing = false;
            if (_group != null) _group.alpha = 1f;
            if (_known) transform.localScale = _resting;
        }
    }
}
