using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The round buttons at the right of the pack list's heading on the main
    /// menu: "?", which shows ModForge's notice again
    /// (<see cref="MainMenuNotice.ShowAgain"/>), and a folder, which opens the
    /// folder packs go in (the author, 1.8.0). Drawn the way the pack boxes
    /// are - a white ring, lit when pointed at - and lifting under the pointer
    /// like ModForge's other buttons.
    /// </summary>
    internal static class MenuAboutButton
    {
        private static Sprite _folder;

        /// <param name="glyph">What is drawn in the ring; null for a "?".</param>
        public static void Add(Transform menuRoot, RectTransform like, Vector2 centre, float size, TMP_FontAsset font,
                               Action onClick, Sprite glyph = null, string name = "SMSModForgeBanner_About")
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(menuRoot, false);
            rect.anchorMin = like.anchorMin;
            rect.anchorMax = like.anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = centre;

            var ring = host.GetComponent<Image>();
            ring.sprite = PackSwitchBox.Ring();
            ring.color = ForgeLook.TextMuted;
            ring.raycastTarget = true;

            if (glyph != null)
            {
                var mark = ForgeLook.Fill("Mark", rect);
                mark.anchorMin = new Vector2(0.24f, 0.24f);
                mark.anchorMax = new Vector2(0.76f, 0.76f);
                ForgeLook.Image(mark, glyph, Color.white).preserveAspect = true;
            }
            else
            {
                var text = ForgeLook.Label(ForgeLook.Fill("Mark", rect), "?", font, size * 0.66f, Color.white,
                                           TextAlignmentOptions.Center, wrap: false);
                text.fontStyle = FontStyles.Bold;
            }

            var button = host.AddComponent<ForgeButton>();
            button.OnClick = () =>
            {
                SaveLoadWarning.Click();
                onClick?.Invoke();
            };
            button.Tint = ring;
            button.TintNormal = ForgeLook.TextMuted;
            button.TintHover = ForgeLook.Accent;
            button.HoverScale = 1.1f;
        }

        /// <summary>A folder: its tab at the top left, its body under it.</summary>
        public static Sprite Folder()
        {
            if (_folder != null) return _folder;
            return _folder = ForgeLook.Shape("SMSModForge folder", 64, (x, y) =>
            {
                float body = Mathf.Clamp01(0.5f - ForgeLook.BoxDistance((x - 0.5f) / 0.9f, (y - 0.42f) / 0.62f, 0.5f, 0.12f) * 64f);
                bool tab = x > 0.05f && x < 0.45f && y > 0.62f && y < 0.82f;
                return Mathf.Max(body, tab ? 1f : 0f);
            });
        }
    }
}
