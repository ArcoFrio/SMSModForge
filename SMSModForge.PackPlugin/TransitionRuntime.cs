using System.Collections.Generic;
using BepInEx.Logging;
using SMSModForge.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Plays the Transitions action (1.7.0): the game's own fades, blink and
    /// flash by their signals - in, and out again once the time on screen is
    /// up - and a black screen with the author's words on it, drawn here in the
    /// look of the game's "A few minutes later...".
    /// <para/>
    /// <b>On top of the dialogue.</b> The game's transition canvases sit at
    /// sorting order 100, its "minutes later" screens at 5 and its travel fade
    /// at -1, under the gameplay UI; the dialogue box is built at run time, so
    /// its order is not in any extraction. Rather than guess, a transition is
    /// put above every canvas on screen when it starts - the game's own canvas
    /// raised for as long as it plays, then put back - and kept above them
    /// while it lasts, so it covers the lines whatever their order.
    /// <para/>
    /// <b>Timed on the real clock, from the plugin's own frame</b> - not a
    /// coroutine on whatever started it, which a dialogue ending would stop,
    /// and not game time, which stands still when the game's clock is set to
    /// zero. While a transition is up, an invisible panel above everything
    /// takes the clicks, so a player cannot click through lines they cannot
    /// see.
    /// </summary>
    internal sealed class TransitionRuntime : MonoBehaviour
    {
        private const string Tag = "[SMSModForge.PackPlugin] Transitions: ";

        /// <summary>Above this, a canvas is a debugging overlay ([Graphy] sits at
        /// 30000), not something a transition should climb over.</summary>
        private const int OverlayCeiling = 30000;

        private sealed class Playing
        {
            public string Style;
            public float Started;
            public float Cover;
            public float Hold;
            public float Clear;
            public bool OutSent;
            public Canvas Raised;
            public GameObject Screen;
            public CanvasGroup Group;
            public PackContext Ctx;
        }

        private static TransitionRuntime _host;
        private static Canvas _canvas;
        private static GameObject _blocker;
        private static ManualLogSource _log;
        private static readonly List<Playing> _playing = new List<Playing>();
        private static readonly Dictionary<Canvas, KeyValuePair<int, int>> _raised =
            new Dictionary<Canvas, KeyValuePair<int, int>>();   // canvas -> (its own order, how many raised it)
        private float _nextTopCheck;

        /// <summary>
        /// Start a transition. Returns the seconds until the screen is covered:
        /// the actions after it wait that long, so what they change is not seen.
        /// </summary>
        public static float Play(string style, float seconds, string text, PackContext ctx)
        {
            _log = ctx?.Log;
            if (System.Array.IndexOf(Transitions.Styles, style) < 0) style = Transitions.FadeToBlack;
            float hold = Transitions.IsTimed(style) ? Mathf.Max(0f, seconds) : 0f;
            var host = Host();

            var p = new Playing
            {
                Style = style,
                Started = Time.unscaledTime,
                Cover = Transitions.CoverSeconds(style),
                Hold = hold,
                Clear = Transitions.ClearSeconds(style),
                Ctx = ctx,
            };

            if (style == Transitions.TextScreen)
            {
                p.Screen = MakeScreen(text, ctx, out p.Group);
                if (p.Screen == null) return 0f;
            }
            else
            {
                p.Raised = Raise(style);
                string signal = Transitions.InSignal(style);
                if (!string.IsNullOrEmpty(signal)) ActionRuntime.EmitSignal(signal, ctx);
            }

            _playing.Add(p);
            host.KeepOnTop(force: true);
            if (_blocker != null) _blocker.SetActive(true);
            ctx?.Log?.LogInfo(Tag + style + (hold > 0f ? " for " + hold + " s" : "") + ".");
            return p.Cover;
        }

        /// <summary>Put everything back: the scene the transitions were in is
        /// gone.</summary>
        public static void Reset()
        {
            foreach (var p in _playing)
                if (p.Screen != null) Object.Destroy(p.Screen);
            _playing.Clear();
            foreach (var kv in _raised)
                if (kv.Key != null) kv.Key.sortingOrder = kv.Value.Key;
            _raised.Clear();
            if (_blocker != null) _blocker.SetActive(false);
        }

        private void Update()
        {
            if (_playing.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _playing.Count - 1; i >= 0; i--)
            {
                var p = _playing[i];
                float t = now - p.Started;
                float outAt = p.Cover + p.Hold;

                if (p.Group != null)
                {
                    float a = t < p.Cover ? t / Mathf.Max(0.01f, p.Cover)
                            : t < outAt ? 1f
                            : 1f - (t - outAt) / Mathf.Max(0.01f, p.Clear);
                    p.Group.alpha = Mathf.Clamp01(a);
                }

                if (!p.OutSent && t >= outAt)
                {
                    p.OutSent = true;
                    string signal = Transitions.OutSignal(p.Style);
                    if (!string.IsNullOrEmpty(signal)) ActionRuntime.EmitSignal(signal, p.Ctx);
                }

                if (t >= outAt + p.Clear)
                {
                    if (p.Screen != null) Destroy(p.Screen);
                    Lower(p.Raised);
                    _playing.RemoveAt(i);
                }
            }
            if (_playing.Count == 0)
            {
                if (_blocker != null) _blocker.SetActive(false);
                return;
            }
            if (now >= _nextTopCheck) KeepOnTop(force: false);
        }

        // ── Staying on top ───────────────────────────────────────────────

        /// <summary>Above every canvas on screen - the dialogue box among them,
        /// which may have come up after the transition began.</summary>
        private void KeepOnTop(bool force)
        {
            _nextTopCheck = Time.unscaledTime + 0.25f;
            int top = TopOrder();
            foreach (var p in _playing)
                if (p.Raised != null && (force || p.Raised.sortingOrder < top)) p.Raised.sortingOrder = top;
            if (_canvas != null && (force || _canvas.sortingOrder <= top)) _canvas.sortingOrder = top + 1;
        }

        /// <summary>One above the highest screen-space canvas on screen, other
        /// than the transitions' own.</summary>
        private static int TopOrder()
        {
            int top = 0;
            foreach (var c in Object.FindObjectsOfType<Canvas>())
            {
                if (c == null || !c.isRootCanvas || c == _canvas) continue;
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;   // overlays draw over every camera's canvas anyway
                if (_raised.ContainsKey(c)) continue;
                if (c.sortingOrder >= OverlayCeiling) continue;
                if (c.sortingOrder > top) top = c.sortingOrder;
            }
            return top + 1;
        }

        /// <summary>The game's canvas a style plays on, raised for as long as
        /// it plays.</summary>
        private static Canvas Raise(string style)
        {
            string root, child;
            switch (style)
            {
                case Transitions.FadeToBlack: root = "Transition_FadeBlackDefault"; child = "Transition Root"; break;
                case Transitions.TravelFade: root = "Transition_TravelNew"; child = "Transition Root"; break;
                case Transitions.Blink: root = "Transition_Blink"; child = "Transition Root"; break;
                case Transitions.WhiteFlash: root = "6_Effects"; child = "Effect_Canvas"; break;
                default: return null;
            }
            var go = GameObject.Find(root);
            var canvas = go?.transform.Find(child)?.GetComponent<Canvas>();
            if (canvas == null)
            {
                _log?.LogWarning(Tag + "'" + root + "/" + child + "' is not in this scene; the " + style
                                 + " plays where the game puts it.");
                return null;
            }
            KeyValuePair<int, int> was;
            _raised[canvas] = _raised.TryGetValue(canvas, out was)
                ? new KeyValuePair<int, int>(was.Key, was.Value + 1)
                : new KeyValuePair<int, int>(canvas.sortingOrder, 1);
            return canvas;
        }

        private static void Lower(Canvas canvas)
        {
            KeyValuePair<int, int> was;
            if (canvas == null || !_raised.TryGetValue(canvas, out was)) return;
            if (was.Value > 1) { _raised[canvas] = new KeyValuePair<int, int>(was.Key, was.Value - 1); return; }
            canvas.sortingOrder = was.Key;
            _raised.Remove(canvas);
        }

        // ── The transitions' own canvas ──────────────────────────────────

        private static TransitionRuntime Host()
        {
            if (_host != null) return _host;
            var go = new GameObject("SMSModForge Transitions", typeof(RectTransform), typeof(Canvas),
                                    typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(go);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;
            MatchGameScaler(go.GetComponent<CanvasScaler>());

            // Takes the clicks while anything plays.
            _blocker = new GameObject("Blocker", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)_blocker.transform;
            rect.SetParent(go.transform, false);
            Stretch(rect);
            var image = _blocker.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            _blocker.SetActive(false);

            _host = go.AddComponent<TransitionRuntime>();
            return _host;
        }

        /// <summary>Lay the canvas out the way the game's effects canvas is, so
        /// the words come out the size the game's own do.</summary>
        private static void MatchGameScaler(CanvasScaler scaler)
        {
            var game = GameObject.Find("6_Effects")?.transform.Find("Effect_Canvas")?.GetComponent<CanvasScaler>();
            if (game != null)
            {
                scaler.uiScaleMode = game.uiScaleMode;
                scaler.referenceResolution = game.referenceResolution;
                scaler.screenMatchMode = game.screenMatchMode;
                scaler.matchWidthOrHeight = game.matchWidthOrHeight;
                scaler.scaleFactor = game.scaleFactor;
                return;
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// The black screen with words: a copy of the game's "A few minutes
        /// later..." screen - its black, its font, its animated text - with the
        /// game's own timing taken off it, since this plays it for as long as
        /// the author asked. A plain black panel when the game's screen is not
        /// there to copy.
        /// </summary>
        private static GameObject MakeScreen(string text, PackContext ctx, out CanvasGroup group)
        {
            group = null;
            string words = string.IsNullOrEmpty(text) ? Transitions.DefaultText : text;
            if (ctx?.Vars != null) words = TextPlaceholders.ResolveAll(words, ctx.Vars);

            var original = GameObject.Find("6_Effects")?.transform.Find("Effect_Canvas/AFewMinutesLater")?.gameObject;
            GameObject screen;
            if (original != null)
            {
                // A copy of something switched off is switched off: its parts
                // can go before any of them wakes up.
                screen = Object.Instantiate(original, _canvas.transform, false);
                foreach (var c in screen.GetComponentsInChildren<Component>(true))
                    if (c != null && c.GetType().Name == "Trigger") Object.DestroyImmediate(c);
                screen.name = "SMSModForge TextScreen";
            }
            else
            {
                screen = new GameObject("SMSModForge TextScreen", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                screen.transform.SetParent(_canvas.transform, false);
                screen.GetComponent<Image>().color = Color.black;
                var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(screen.transform, false);
                Stretch((RectTransform)label.transform);
                var tmp = label.GetComponent<TextMeshProUGUI>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontSize = 64;
                tmp.color = Color.white;
                ctx?.Log?.LogWarning(Tag + "the game's 'A few minutes later' screen was not found; a plain one stands in.");
            }

            var rect = (RectTransform)screen.transform;
            Stretch(rect);
            // Not ??: a Unity object's null is not C#'s.
            group = screen.GetComponent<CanvasGroup>();
            if (group == null) group = screen.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = true;

            foreach (var t in screen.GetComponentsInChildren<TMP_Text>(true)) SetWords(t, words);
            screen.SetActive(true);
            return screen;
        }

        /// <summary>
        /// The words, through the game's text animator when the text has one -
        /// set straight on the text, an animator can put back what it held -
        /// and on the text itself either way.
        /// </summary>
        private static void SetWords(TMP_Text text, string words)
        {
            text.text = words;
            foreach (var c in text.GetComponents<Component>())
            {
                if (c == null || !c.GetType().Name.StartsWith("TextAnimator", System.StringComparison.Ordinal)) continue;
                var type = c.GetType();
                var two = type.GetMethod("SetText", new[] { typeof(string), typeof(bool) });
                var one = type.GetMethod("SetText", new[] { typeof(string) });
                try
                {
                    if (two != null) two.Invoke(c, new object[] { words, false });
                    else if (one != null) one.Invoke(c, new object[] { words });
                }
                catch (System.Exception ex)
                {
                    _log?.LogWarning(Tag + "the text animator would not take the words: " + ex.Message);
                }
            }
        }
    }
}
