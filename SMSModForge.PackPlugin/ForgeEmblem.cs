using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// ModForge's mark: an anvil on an amber disc, glowing softly - beside the
    /// title of its windows and the heading of its pack list, so a player can
    /// tell at a glance which of the things on screen are ModForge's.
    /// </summary>
    internal static class ForgeEmblem
    {
        private static Sprite _anvil, _glow;

        /// <summary>Draw the mark into <paramref name="rect"/>, filling it.</summary>
        public static void Draw(RectTransform rect)
        {
            var glow = ForgeLook.Rect("Glow", rect);
            glow.anchorMin = new Vector2(-0.45f, -0.45f);
            glow.anchorMax = new Vector2(1.45f, 1.45f);
            glow.offsetMin = glow.offsetMax = Vector2.zero;
            var glowImage = ForgeLook.Image(glow, Glow(), ForgeLook.WithAlpha(ForgeLook.Accent, 0.3f));
            var breath = glow.gameObject.AddComponent<ForgeGlow>();
            breath.Image = glowImage;

            ForgeLook.Image(ForgeLook.Fill("Disc", rect), ForgeLook.Circle, ForgeLook.Accent);
            // A share of the disc, not a distance: the disc's size is often
            // only settled by a layout after this.
            var anvil = ForgeLook.Fill("Anvil", rect);
            anvil.anchorMin = new Vector2(0.17f, 0.17f);
            anvil.anchorMax = new Vector2(0.83f, 0.83f);
            ForgeLook.Image(anvil, Anvil(), ForgeLook.OnButton).preserveAspect = true;
        }

        /// <summary>An anvil: its face with the horn to the left, the waist,
        /// the foot.</summary>
        private static Sprite Anvil()
        {
            if (_anvil != null) return _anvil;
            return _anvil = ForgeLook.Shape("SMSModForge anvil", 96, (x, y) =>
            {
                bool face = x > 0.3f && x < 0.92f && y > 0.56f && y < 0.74f;
                bool horn = InTriangle(x, y, 0.3f, 0.74f, 0.3f, 0.6f, 0.06f, 0.71f);
                bool waist = x > 0.46f && x < 0.74f && y > 0.36f && y < 0.57f;
                bool foot = x > 0.32f && x < 0.88f && y > 0.22f && y < 0.37f;
                return face || horn || waist || foot ? 1f : 0f;
            });
        }

        private static Sprite Glow()
        {
            if (_glow != null) return _glow;
            return _glow = ForgeLook.Shape("SMSModForge glow", 64, (x, y) =>
            {
                float dx = x - 0.5f, dy = y - 0.5f;
                float d = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                return d * d;
            });
        }

        private static bool InTriangle(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (x - bx) * (ay - by) - (ax - bx) * (y - by);
            float d2 = (x - cx) * (by - cy) - (bx - cx) * (y - cy);
            float d3 = (x - ax) * (cy - ay) - (cx - ax) * (y - ay);
            bool negative = d1 < 0 || d2 < 0 || d3 < 0, positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive);
        }
    }

    /// <summary>The emblem's glow, breathing slowly (unscaled time).</summary>
    internal sealed class ForgeGlow : MonoBehaviour
    {
        public Image Image;
        public float Low = 0.18f, High = 0.42f, Seconds = 2.6f;

        private void Update()
        {
            if (Image == null) return;
            float t = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * 2f * Mathf.PI / Seconds);
            var c = Image.color;
            c.a = Mathf.Lerp(Low, High, t);
            Image.color = c;
        }
    }

    /// <summary>
    /// The game's own blur behind its windows, for as long as a ModForge
    /// window is up. The game blurs behind its Load panel with a
    /// post-processing volume (<c>blur volume</c>, under NanoSave), switched on
    /// with the panel - its parent, so switching the volume alone does nothing.
    /// A copy of it, of ModForge's own and on its own, is what blurs instead:
    /// made with the first window and gone with the last. Nothing happens in a
    /// scene that has none: the window's dimming is still there.
    /// </summary>
    internal sealed class GameBlur : MonoBehaviour
    {
        /// <summary>The game's blur volumes by name, in the order tried: the
        /// main menu's (under NanoSave), the one the game's save panel and Quest
        /// Journal use in the game itself.</summary>
        private static readonly string[] VolumeNames = { "blur volume", "NanoSave_Volume" };

        private static GameObject _copy;
        private static int _holders;

        private void OnEnable()
        {
            if (_holders++ > 0 && _copy != null) return;
            var game = Find();
            if (game == null) return;
            _copy = Instantiate(game);
            _copy.name = "SMSModForge blur";
            _copy.transform.SetParent(null, false);
            _copy.SetActive(true);
        }

        private void OnDisable()
        {
            if (--_holders > 0) return;
            _holders = 0;
            if (_copy != null) Destroy(_copy);
            _copy = null;
        }

        private static GameObject Find()
        {
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (string name in VolumeNames)
                foreach (var t in all)
                {
                    if (t == null || !t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded) continue;
                    if (!string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var c in t.GetComponents<Component>())
                        if (c != null && c.GetType().Name == "Volume") return t.gameObject;
                }
            return null;
        }
    }
}
