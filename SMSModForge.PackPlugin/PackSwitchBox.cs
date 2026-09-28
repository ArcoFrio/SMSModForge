using System;
using SMSModForge.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The box beside each pack on the main menu's pack list: ticked, the pack
    /// is loaded when a game starts or loads; empty, it is not
    /// (<see cref="PackSwitchSetting"/>).
    /// <para/>
    /// It stands where the row's bullet was. The bullet is kept in the text,
    /// only made invisible, so the pack's name and any line under it stay
    /// exactly where they were - the box takes the bullet's place rather than
    /// pushing the row along.
    /// <para/>
    /// Drawn here, in white, rather than copied from the game: its main menu
    /// has no box to copy.
    /// </summary>
    internal static class PackSwitchBox
    {
        private const string Tag = "[SMSModForge.PackPlugin] Pack switches: ";

        /// <summary>The box, a little shorter than a line of the list.</summary>
        private const float Size = 18f;

        /// <summary>What can be clicked: more than the box, which is small for a mouse.</summary>
        private const float HitSize = 28f;

        /// <summary>Between the box and the pack's name.</summary>
        private const float Gap = 6f;

        /// <summary>The bullet, hidden: <c>&lt;alpha&gt;</c> changes only how
        /// see-through the letters after it are, so the bullet still takes
        /// its room.</summary>
        private const string HiddenBullet = "<alpha=#00>•<alpha=#FF>";

        private static Sprite _frame;
        private static Sprite _tick;

        /// <summary>
        /// A pack's first row, with its bullet made invisible for the box to
        /// stand on. Anything that does not start with the bullet is left as
        /// it is.
        /// </summary>
        public static string MakeRoom(string row)
        {
            if (row == null || !row.StartsWith(PackStatus.Bullet, StringComparison.Ordinal)) return row;
            string before = PackStatus.Bullet.Substring(0, PackStatus.Bullet.IndexOf('•'));
            string after = PackStatus.Bullet.Substring(PackStatus.Bullet.IndexOf('•') + 1);
            return before + HiddenBullet + after + row.Substring(PackStatus.Bullet.Length);
        }

        /// <summary>
        /// Put a box on <paramref name="row"/>, a copy of the menu's text,
        /// ticked when <paramref name="on"/>. A click calls
        /// <paramref name="toggle"/> with what the box now says.
        /// </summary>
        public static void Add(GameObject row, bool on, Action<bool> toggle, BepInEx.Logging.ManualLogSource log)
        {
            if (row == null) return;
            var text = row.GetComponent<TMP_Text>();
            var host = new GameObject("PackSwitch", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(row.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(HitSize, HitSize);
            rect.localPosition = Where(text, log);
            // And kept there: the row's text can be laid out again after this -
            // on the first frames of the menu it is - and the box goes with it.
            host.AddComponent<PackSwitchFollow>().Text = text;

            // Takes the click, and shows nothing: the parts below are what show.
            var hit = host.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var fill = Part(rect, "Fill", null, new Color(0f, 0f, 0f, 0.55f));
            var frame = Part(rect, "Frame", Frame(), Color.white);
            var tick = Part(rect, "Tick", Tick(), Color.white);
            tick.enabled = on;

            var button = host.AddComponent<Button>();
            button.targetGraphic = frame;
            var colours = button.colors;
            colours.normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colours.highlightedColor = Color.white;
            colours.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            colours.selectedColor = colours.normalColor;
            colours.colorMultiplier = 1f;
            colours.fadeDuration = 0.1f;
            button.colors = colours;
            // Picked with the mouse, like the flags: not one of the stops the
            // game's menu keys move between.
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            button.onClick.AddListener(() => toggle?.Invoke(!on));
            fill.raycastTarget = frame.raycastTarget = tick.raycastTarget = false;
        }

        /// <summary>
        /// Where the box goes in the row: over the hidden bullet, its right
        /// edge <see cref="Gap"/> short of the pack's name, and level with the
        /// bullet - which the font sets at the middle of the line's letters.
        /// Read off the laid-out text, so it follows the font, its size and
        /// whichever way the menu's text is aligned.
        /// </summary>
        private static Vector3 Where(TMP_Text text, BepInEx.Logging.ManualLogSource log)
        {
            if (text != null)
            {
                try
                {
                    // Laid out now, even if the menu is switched off at this
                    // moment: text that is not active is otherwise left
                    // unmeasured, and would put every box at the fallback.
                    text.ForceMeshUpdate(true);
                    Vector3 at;
                    if (Measure(text, out at)) return at;
                }
                catch (Exception e)
                {
                    log?.LogWarning(Tag + "a row could not be measured, so its box sits at its left edge: " + e.Message);
                }
            }

            // Not measured: the row's left edge, halfway up.
            var rect = text == null ? null : text.rectTransform;
            if (rect == null) return Vector3.zero;
            return new Vector3(rect.rect.xMin + Size / 2f, rect.rect.center.y, 0f);
        }

        /// <summary>
        /// Where the box goes by the text as it was last laid out - without
        /// laying it out again. False when there is no bullet in it to go by.
        /// </summary>
        internal static bool Measure(TMP_Text text, out Vector3 at)
        {
            at = Vector3.zero;
            var info = text == null ? null : text.textInfo;
            if (info == null || info.characterInfo == null) return false;
            int count = Math.Min(info.characterCount, info.characterInfo.Length);
            int bullet = -1;
            for (int i = 0; i < count; i++)
                if (info.characterInfo[i].character == '•') { bullet = i; break; }
            if (bullet < 0) return false;

            var mark = info.characterInfo[bullet];
            float right = mark.topRight.x;
            for (int i = bullet + 1; i < count; i++)
                if (info.characterInfo[i].isVisible) { right = info.characterInfo[i].bottomLeft.x; break; }
            float y = (mark.bottomLeft.y + mark.topRight.y) / 2f;
            at = new Vector3(right - Gap - Size / 2f, y, 0f);
            return true;
        }

        private static Image Part(RectTransform parent, string name, Sprite sprite, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Size, Size);
            rect.anchoredPosition = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            return image;
        }

        // ── The drawings ─────────────────────────────────────────────────

        /// <summary>Pixels a side: four times the box, so it stays sharp when
        /// the screen is larger than the menu was made for.</summary>
        private const int Pixels = 72;

        /// <summary>The square outline.</summary>
        private static Sprite Frame()
        {
            if (_frame != null) return _frame;
            float centre = Pixels / 2f, outer = centre - 2f, inner = outer - 7f;
            return _frame = Draw("SMSModForge pack switch frame", (x, y) =>
            {
                float d = Mathf.Max(Mathf.Abs(x - centre), Mathf.Abs(y - centre));
                return Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
            });
        }

        /// <summary>The tick: two strokes, short then long.</summary>
        private static Sprite Tick()
        {
            if (_tick != null) return _tick;
            var a = new Vector2(0.22f, 0.52f) * Pixels;
            var b = new Vector2(0.42f, 0.30f) * Pixels;
            var c = new Vector2(0.80f, 0.76f) * Pixels;
            const float half = 5f;
            return _tick = Draw("SMSModForge pack switch tick", (x, y) =>
            {
                var p = new Vector2(x, y);
                float d = Mathf.Min(ToSegment(p, a, b), ToSegment(p, b, c));
                return Mathf.Clamp01(half - d + 0.5f);
            });
        }

        private static float ToSegment(Vector2 p, Vector2 from, Vector2 to)
        {
            Vector2 along = to - from;
            float t = Mathf.Clamp01(Vector2.Dot(p - from, along) / along.sqrMagnitude);
            return Vector2.Distance(p, from + along * t);
        }

        /// <summary>A white drawing, as solid as <paramref name="cover"/> says
        /// at each pixel's centre; the Image's colour tints it.</summary>
        private static Sprite Draw(string name, Func<float, float, float> cover)
        {
            var pixels = new Color32[Pixels * Pixels];
            for (int y = 0; y < Pixels; y++)
                for (int x = 0; x < Pixels; x++)
                    pixels[y * Pixels + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(cover(x + 0.5f, y + 0.5f)) * 255f));
            var texture = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, Pixels, Pixels), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }
    }

    /// <summary>
    /// Keeps a pack's box beside its name. The row's text can be laid out
    /// again after the box was placed - on the menu's first frames it is, and
    /// the name ran into the box - so the box reads, every frame, where
    /// the text was last laid out, and moves when that changed. Reading it is a
    /// few numbers; nothing is laid out again for it.
    /// </summary>
    internal sealed class PackSwitchFollow : MonoBehaviour
    {
        public TMP_Text Text;

        private void LateUpdate()
        {
            Vector3 at;
            if (!PackSwitchBox.Measure(Text, out at)) return;
            if ((transform.localPosition - at).sqrMagnitude > 0.01f) transform.localPosition = at;
        }
    }
}
