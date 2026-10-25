using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// How a pack's line on the main menu's list says what it does: it lights
    /// up under the pointer, across the panel - the part that can be picked up
    /// - and its first line ends in a grip of six dots, the usual sign of
    /// something that can be dragged into another place in a list.
    /// </summary>
    internal static class MenuRowLook
    {
        private static Sprite _grip;

        /// <summary>The see-through strip behind a line (what the drag finds)
        /// made to light under the pointer.</summary>
        public static void Hover(RectTransform strip)
        {
            var image = strip.GetComponent<Image>();
            if (image == null) return;
            image.sprite = ForgeLook.Rounded;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            image.color = new Color(1f, 1f, 1f, 0f);
            if (strip.GetComponent<RowHover>() == null) strip.gameObject.AddComponent<RowHover>().Image = image;
        }

        /// <summary>The grip, at <paramref name="at"/> in the line.</summary>
        public static void Grip(RectTransform row, Vector2 at)
        {
            var grip = ForgeLook.Rect("PackGrip", row);
            grip.anchorMin = grip.anchorMax = grip.pivot = new Vector2(0.5f, 0.5f);
            grip.sizeDelta = new Vector2(14f, 20f);
            grip.anchoredPosition = at;
            ForgeLook.Image(grip, Dots(), new Color(1f, 1f, 1f, 0.45f));
        }

        /// <summary>Six dots, two across and three down.</summary>
        private static Sprite Dots()
        {
            if (_grip != null) return _grip;
            return _grip = ForgeLook.Shape("SMSModForge grip", 64, (x, y) =>
            {
                float best = 1f;
                foreach (float cx in new[] { 0.3f, 0.7f })
                    foreach (float cy in new[] { 0.2f, 0.5f, 0.8f })
                    {
                        float dx = (x - cx) * 0.7f, dy = y - cy;
                        best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dy * dy));
                    }
                return Mathf.Clamp01((0.075f - best) * 64f);
            });
        }

        /// <summary>Lights its strip while the pointer is on it.</summary>
        private sealed class RowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Image Image;
            private float _want, _now;

            public void OnPointerEnter(PointerEventData e) { _want = 0.07f; }
            public void OnPointerExit(PointerEventData e) { _want = 0f; }
            private void OnDisable() { _want = _now = 0f; if (Image != null) Image.color = new Color(1f, 1f, 1f, 0f); }

            private void Update()
            {
                if (Image == null || Mathf.Abs(_now - _want) < 0.001f) return;
                _now = Mathf.Lerp(_now, _want, 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime));
                Image.color = new Color(1f, 1f, 1f, _now);
            }
        }
    }
}
