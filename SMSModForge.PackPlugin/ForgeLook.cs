using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// How ModForge's own things look in the game - its windows, the pack list
    /// on the main menu, the language flags (the author, 1.8.0: the flat boxes
    /// "don't fit the actual game too well"). Built from the game's own parts
    /// where it has them - its rounded panel sprites and fonts - so ModForge
    /// reads as part of the game: a warm dark panel with a faint edge, an amber
    /// line where it is lit, buttons that lift under the pointer, windows that
    /// come in rather than appear. A sprite or font an update renamed is drawn
    /// or stood in for, never missed.
    /// <para/>
    /// The game blends in linear light, where a see-through colour darkens far
    /// less than its alpha says: the alphas here are measured, not guessed.
    /// </summary>
    internal static class ForgeLook
    {
        // ── Colours ──────────────────────────────────────────────────────

        // The game's own: the navy of its unfinished Scene Gallery canvas
        // (Canvas_Gallery_Core) and the white of its menu buttons. What marks
        // a thing as ModForge's is the amber - its lit edge, its titles, its
        // emblem - on the game's look, not a look of its own.
        public static readonly Color Panel = Hex("000E20");
        public static readonly Color Raised = Hex("0A1A33");
        public static readonly Color RaisedHover = Hex("142849");
        public static readonly Color Stroke = new Color(1f, 1f, 1f, 0.12f);
        public static readonly Color Accent = Hex("F5A93B");
        public static readonly Color AccentHover = Hex("FFC772");
        public static readonly Color ButtonFill = Color.white;
        public static readonly Color ButtonFillHover = Hex("FFE6C2");
        public static readonly Color OnButton = Hex("0A1A33");
        public static readonly Color Text = Color.white;
        public static readonly Color TextMuted = new Color(0.78f, 0.81f, 0.9f, 1f);
        public static readonly Color Warning = Hex("FFB84D");
        public static readonly Color Error = Hex("FF6A57");

        /// <summary>Behind a window: 0.84 to look like about 0.7.</summary>
        public static readonly Color Dim = new Color(0f, 0.01f, 0.04f, 0.84f);

        public static Color Hex(string rgb)
        {
            Color c;
            ColorUtility.TryParseHtmlString("#" + rgb, out c);
            return c;
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        // ── The game's parts ─────────────────────────────────────────────

        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, TMP_FontAsset> _fonts = new Dictionary<string, TMP_FontAsset>();
        private static bool _indexed;

        private static void Index()
        {
            if (_indexed) return;
            _indexed = true;
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && !_sprites.ContainsKey(s.name)) _sprites[s.name] = s;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f != null && !_fonts.ContainsKey(f.name)) _fonts[f.name] = f;
        }

        /// <summary>A new scene: what was found in the last may be gone.</summary>
        public static void Reset()
        {
            _indexed = false;
            _sprites.Clear();
            _fonts.Clear();
        }

        private static Sprite Game(string name)
        {
            Index();
            Sprite s;
            return _sprites.TryGetValue(name, out s) && s != null ? s : null;
        }

        /// <summary>The game's rounded panel: 256 pixels, 30-pixel corners.</summary>
        public static Sprite Rounded => Game("Semi Rounded") ?? Drawn(30, 0);

        /// <summary>Its outline, two pixels.</summary>
        public static Sprite RoundedStroke => Game("Semi Rounded - Stroke 2px") ?? Drawn(30, 2);

        /// <summary>A pill: ends fully round.</summary>
        public static Sprite Pill => Game("Rounded") ?? Drawn(128, 0);

        public static Sprite Circle => Game("Circle - 256") ?? Drawn(128, 0);

        /// <summary>The game's title face, for headings; <paramref name="otherwise"/>
        /// where the scene has not loaded it.</summary>
        public static TMP_FontAsset Title(TMP_FontAsset otherwise) => Font("Barton SDF") ?? Body(otherwise);

        /// <summary>The face the game's interface is written in.</summary>
        public static TMP_FontAsset Body(TMP_FontAsset otherwise)
            => Font("Curse Casual SDF") ?? otherwise ?? TMP_Settings.defaultFontAsset;

        private static TMP_FontAsset Font(string name)
        {
            Index();
            TMP_FontAsset f;
            return _fonts.TryGetValue(name, out f) && f != null ? f : null;
        }

        // ── Building ─────────────────────────────────────────────────────

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;   // UI
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform Fill(string name, Transform parent, float inset = 0f)
        {
            var rect = Rect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        /// <summary>A sliced image; <paramref name="corners"/> scales its corners
        /// down - higher, tighter.</summary>
        public static Image Image(RectTransform rect, Sprite sprite, Color colour, float corners = 2.4f, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero)
            {
                image.type = UnityEngine.UI.Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = corners;
            }
            return image;
        }

        /// <summary>
        /// A panel: the warm dark fill, its faint edge, and an amber line along
        /// the top, the way a lit edge catches the light.
        /// </summary>
        public static Image Surface(RectTransform rect, float alpha, float corners = 2.4f, bool lit = true, bool raycast = false)
        {
            var fill = Image(rect, Rounded, WithAlpha(Panel, alpha), corners, raycast);
            // Decoration, not content: a panel that lays out its children
            // must leave these where they are.
            var edge = Fill("Edge", rect);
            edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Image(edge, RoundedStroke, Stroke, corners);
            if (lit)
            {
                var line = Rect("Lit", rect);
                line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                line.anchorMin = new Vector2(0f, 1f);
                line.anchorMax = new Vector2(1f, 1f);
                line.pivot = new Vector2(0.5f, 1f);
                line.offsetMin = new Vector2(28f, -3f);
                line.offsetMax = new Vector2(-28f, 0f);
                line.anchoredPosition = new Vector2(0f, -1f);
                Image(line, Pill, WithAlpha(Accent, 0.9f), 8f);
            }
            return fill;
        }

        public static TextMeshProUGUI Label(RectTransform rect, string text, TMP_FontAsset font, float size, Color colour,
                                            TextAlignmentOptions alignment = TextAlignmentOptions.Left, bool wrap = true)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = colour;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.richText = true;
            // Obsolete in newer TextMesh Pro builds, and still what this one
            // wraps by.
#pragma warning disable 0618
            label.enableWordWrapping = wrap;
#pragma warning restore 0618
            return label;
        }

        /// <summary>
        /// A button: the game's white pill with dark words when it is the way
        /// on (<paramref name="primary"/>) - warming to amber under the pointer
        /// - the panel's raised navy with light words otherwise; lifts under
        /// the pointer and dips when pressed.
        /// </summary>
        public static ForgeButton Button(RectTransform rect, string text, TMP_FontAsset font, bool primary, Action onClick,
                                         float textSize = 30f)
        {
            var back = Image(rect, Rounded, primary ? ButtonFill : Raised, 2f, raycast: true);
            if (!primary) Image(Fill("Stroke", rect), RoundedStroke, new Color(1f, 1f, 1f, 0.25f), 2f);
            var label = Label(Fill("Label", rect, 4f), text, font, textSize, primary ? OnButton : Text,
                              TextAlignmentOptions.Center, wrap: false);
            label.fontStyle = FontStyles.Bold;
            var button = rect.gameObject.AddComponent<ForgeButton>();
            button.OnClick = onClick;
            button.Tint = back;
            button.TintNormal = primary ? ButtonFill : Raised;
            button.TintHover = primary ? ButtonFillHover : RaisedHover;
            return button;
        }

        // ── Drawn stand-ins ──────────────────────────────────────────────

        private static readonly Dictionary<string, Sprite> _drawn = new Dictionary<string, Sprite>();

        /// <summary>A rounded square (filled, or only its outline), sliced like
        /// the game's: for a game that has renamed its own.</summary>
        internal static Sprite Drawn(int radius, int stroke)
        {
            string key = radius + "-" + stroke;
            Sprite sprite;
            if (_drawn.TryGetValue(key, out sprite) && sprite != null) return sprite;
            const int size = 256;
            float r = radius / (float)size, s = stroke / (float)size;
            sprite = Shape("SMSModForge rounded " + key, size, (x, y) =>
            {
                float d = BoxDistance(x - 0.5f, y - 0.5f, 0.5f, r);
                float inside = Mathf.Clamp01(0.5f - d * size);
                if (stroke <= 0) return inside;
                return inside - Mathf.Clamp01(0.5f - (d + s) * size);
            }, new Vector4(radius, radius, radius, radius));
            _drawn[key] = sprite;
            return sprite;
        }

        /// <summary>A white drawing of <paramref name="size"/> pixels, as solid as
        /// <paramref name="cover"/> says at each point (0 to 1 across), smoothed
        /// over four samples a pixel.</summary>
        internal static Sprite Shape(string name, int size, Func<float, float, float> cover, Vector4 border = default(Vector4))
        {
            var pixels = new Color32[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float a = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            a += Mathf.Clamp01(cover((i + 0.25f + sx * 0.5f) / size, (j + 0.25f + sy * 0.5f) / size));
                    pixels[j * size + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 0.25f * 255f));
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, border);
            sprite.name = name;
            return sprite;
        }

        internal static float BoxDistance(float px, float py, float half, float r)
        {
            float qx = Mathf.Abs(px) - half + r, qy = Mathf.Abs(py) - half + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        internal static float Segment(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
            float t = Mathf.Clamp01((wx * vx + wy * vy) / (vx * vx + vy * vy));
            float dx = wx - vx * t, dy = wy - vy * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.4f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }
    }

    /// <summary>
    /// ModForge's button: grows a little under the pointer and brightens,
    /// dips when pressed, clicks the game's click. On the real clock, so it
    /// answers with the game paused.
    /// </summary>
    internal sealed class ForgeButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
                                        IPointerUpHandler, IPointerClickHandler
    {
        public Action OnClick;
        public bool Interactable = true;
        public float HoverScale = 1.04f;
        public Graphic Tint;
        public Color TintNormal = Color.white, TintHover = Color.white;

        private bool _hover, _down;
        private float _scale = 1f;

        public void OnPointerEnter(PointerEventData e) { _hover = true; }
        public void OnPointerExit(PointerEventData e) { _hover = false; _down = false; }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) _down = true; }
        public void OnPointerUp(PointerEventData e) { _down = false; }

        public void OnPointerClick(PointerEventData e)
        {
            if (!Interactable || e.button != PointerEventData.InputButton.Left) return;
            OnClick?.Invoke();
        }

        /// <summary>What a click does, for the test bridge, which has no pointer.</summary>
        internal void Press() { if (Interactable) OnClick?.Invoke(); }

        private void OnDisable()
        {
            _hover = _down = false;
            _scale = 1f;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            float target = !Interactable ? 1f : _down ? 0.97f : _hover ? HoverScale : 1f;
            if (Mathf.Abs(_scale - target) > 0.0005f)
            {
                _scale = Mathf.Lerp(_scale, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
                transform.localScale = new Vector3(_scale, _scale, 1f);
            }
            if (Tint != null)
            {
                Color want = Interactable && _hover ? TintHover : TintNormal;
                if (Tint.color != want) Tint.color = Color.Lerp(Tint.color, want, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            }
        }
    }

    /// <summary>
    /// How a ModForge window comes and goes: fades in while its panel springs
    /// up to size, and fades out a little smaller. Unscaled time. The window
    /// takes no clicks on its way out, and still covers what is behind it.
    /// </summary>
    internal sealed class ForgeAppear : MonoBehaviour
    {
        public RectTransform Panel;
        public CanvasGroup Group;

        private const float In = 0.24f, Out = 0.16f;
        private float _start;
        private bool _leaving;
        private Action _done;

        private void OnEnable()
        {
            _start = Time.unscaledTime;
            Apply(0f);
        }

        public void Leave(Action done)
        {
            if (_leaving) return;
            _leaving = true;
            _done = done;
            _start = Time.unscaledTime;
            if (Group != null) Group.interactable = false;
            if (!isActiveAndEnabled) Finish();
        }

        private void Update()
        {
            float t = (Time.unscaledTime - _start) / (_leaving ? Out : In);
            Apply(Mathf.Clamp01(t));
            if (_leaving && t >= 1f) Finish();
        }

        private void Apply(float t)
        {
            float alpha = _leaving ? 1f - ForgeLook.EaseOutCubic(t) : ForgeLook.EaseOutCubic(t);
            float scale = _leaving ? Mathf.Lerp(1f, 0.96f, ForgeLook.EaseOutCubic(t)) : Mathf.LerpUnclamped(0.92f, 1f, ForgeLook.EaseOutBack(t));
            if (Group != null) Group.alpha = alpha;
            if (Panel != null) Panel.localScale = new Vector3(scale, scale, 1f);
        }

        private void Finish()
        {
            var done = _done;
            _done = null;
            done?.Invoke();
        }
    }
}
