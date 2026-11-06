using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>Something that changes how a UI object is drawn on top of what
    /// it is: a press held down (<see cref="UiPress"/>), a loop
    /// (<see cref="UiLoop"/>). Asked once a frame, after everything else.</summary>
    internal interface IUiOverlayPart
    {
        void Shape(ref float scale, ref float alpha, ref Vector2 offset);
    }

    /// <summary>
    /// Draws an object's press and loop over what it really is, without taking
    /// its size, fade or place from anything else that sets them (1.8.3).
    /// <para/>
    /// Others write those outright every frame - its arrival animation, the
    /// game's hover bounce - so a press or a loop that wrote them too would
    /// fight them. So this puts the real values back first thing in the frame,
    /// before any other script runs, and multiplies its parts in after every
    /// script has run. What is drawn is both; what everything else - the
    /// game's scripts, the event system's hit test - reads is the object as it
    /// is, so a button shrinking under the pointer is still clicked where it
    /// really is.
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    public class UiOverlay : MonoBehaviour
    {
        private readonly List<IUiOverlayPart> _parts = new List<IUiOverlayPart>();
        private RectTransform _rect;
        private CanvasGroup _group;
        private bool _scaled, _faded, _moved;
        private Vector3 _scale;
        private float _alpha;
        private Vector2 _position;

        /// <summary>The overlay on this object, made when it has none.</summary>
        internal static void On(GameObject go)
        {
            if (go.GetComponent<UiOverlay>() == null) go.AddComponent<UiOverlay>();
        }

        private void Awake() => _rect = transform as RectTransform;

        // First of the frame: the object as it really is again.
        private void Update() => Restore();

        // After every Update and coroutine of the frame: the parts on top.
        private void LateUpdate()
        {
            float scale = 1f, alpha = 1f;
            var offset = Vector2.zero;
            GetComponents(_parts);
            for (int i = 0; i < _parts.Count; i++) _parts[i].Shape(ref scale, ref alpha, ref offset);

            if (scale != 1f)
            {
                _scale = transform.localScale;
                transform.localScale = _scale * scale;
                _scaled = true;
            }
            if (alpha != 1f)
            {
                if (_group == null) _group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
                _alpha = _group.alpha;
                _group.alpha = _alpha * alpha;
                _faded = true;
            }
            if (offset != Vector2.zero && _rect != null)
            {
                _position = _rect.anchoredPosition;
                _rect.anchoredPosition = _position + offset;
                _moved = true;
            }
        }

        private void OnDisable() => Restore();

        private void Restore()
        {
            if (_scaled) { transform.localScale = _scale; _scaled = false; }
            if (_faded) { if (_group != null) _group.alpha = _alpha; _faded = false; }
            if (_moved) { if (_rect != null) _rect.anchoredPosition = _position; _moved = false; }
        }
    }
}
