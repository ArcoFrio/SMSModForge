using UnityEngine;
using UnityEngine.EventSystems;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// A button that shows it is held down: it eases to <see cref="Scale"/>
    /// while pressed and back when let go, everything under it with it - a
    /// node's <c>pressScale</c> (1.8.3).
    /// <para/>
    /// A pack button's own press is the game's, played on the click - when the
    /// button is let go - so a card whose click closes its screen or starts a
    /// conversation never showed it. This shows while the pointer is down, and
    /// stands in for that press (<see cref="UiButtonClick"/> skips it). Drawn
    /// through <see cref="UiOverlay"/>, so a hover bounce on the same object
    /// goes on working under it.
    /// </summary>
    public class UiPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IUiOverlayPart
    {
        /// <summary>The size while held, as a fraction of its own.</summary>
        public float Scale = 0.95f;

        /// <summary>Seconds to most of the way there, either way - the
        /// game's own press takes 0.2s to its end.</summary>
        private const float Ease = 0.05f;

        private bool _held;
        private float _now = 1f;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _held = true;
        }

        public void OnPointerUp(PointerEventData eventData) => _held = false;

        public void OnPointerExit(PointerEventData eventData) => _held = false;

        private void OnDisable()
        {
            _held = false;
            _now = 1f;
        }

        void IUiOverlayPart.Shape(ref float scale, ref float alpha, ref Vector2 offset)
        {
            float target = _held ? Scale : 1f;
            _now = Mathf.Lerp(_now, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / Ease));
            if (Mathf.Abs(_now - target) < 0.001f) _now = target;
            scale *= _now;
        }
    }
}
