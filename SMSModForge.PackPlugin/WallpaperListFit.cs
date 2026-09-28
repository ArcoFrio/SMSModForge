using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Keeps every wallpaper button on the wallpaper screen, however many mods
    /// add them.
    /// <para/>
    /// The screen's list is a grid of 240x135 buttons in a 1500x700 box, with no
    /// scrolling and no mask - read from the game's scene, 2026-09-27. That is
    /// five across and four down: twenty. The game's own fourteen come first,
    /// so the sixth mod wallpaper is the last one that fits, and every one after
    /// it is drawn below the panel, then off the screen - there, and never seen.
    /// Nothing about the mods has to change for that to happen: one more pack
    /// with a wallpaper, or one more of the game's unlocked, and the last
    /// buttons are gone. A player reported exactly that - mod wallpapers,
    /// ModForge's and another mod's alike, gone from a save where they had been
    /// (2026-09-27).
    /// <para/>
    /// So when the buttons showing are more than the box holds, they are made
    /// smaller - the cell and the gap between cells together, in proportion -
    /// until all of them fit; and when they fit again at the game's own size,
    /// that size comes back. Every mod's buttons are in the same grid, so this
    /// keeps theirs on screen too.
    /// </summary>
    internal static class WallpaperListFit
    {
        private static GridLayoutGroup _grid;
        private static RectTransform _box;
        private static Vector2 _cell, _spacing;
        private static bool _haveOriginal;
        private static int _lastCount = -1;
        private static float _nextLook;

        public static void Tick(ManualLogSource log)
        {
            if (_grid == null)
            {
                // Found once per scene; looked for again at most once a second
                // until it is there.
                if (Time.unscaledTime < _nextLook) return;
                _nextLook = Time.unscaledTime + 1f;
                var list = GameObject.Find("9_MainCanvas")?.transform
                    .FindPathIncludingInactive("Wallpaperselection/UI_Core/List");
                if (list == null) return;
                _grid = list.GetComponent<GridLayoutGroup>();
                _box = list as RectTransform;
                if (_grid == null || _box == null) { _grid = null; return; }
                if (!_haveOriginal)
                {
                    _cell = _grid.cellSize;
                    _spacing = _grid.spacing;
                    _haveOriginal = true;
                }
                _lastCount = -1;
            }

            // Only the buttons showing take a place in the grid.
            int showing = 0;
            var t = _grid.transform;
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeSelf) showing++;
            if (showing == _lastCount) return;
            _lastCount = showing;

            float scale = ScaleToFit(showing, _box.rect.size, _cell, _spacing);
            var cell = _cell * scale;
            var spacing = _spacing * scale;
            if (_grid.cellSize == cell && _grid.spacing == spacing) return;
            _grid.cellSize = cell;
            _grid.spacing = spacing;
            if (scale < 1f)
                log?.LogInfo("[SMSModForge.PackPlugin] " + showing + " wallpaper buttons: made "
                             + Mathf.RoundToInt(scale * 100) + "% of their size so all of them fit on the screen.");
        }

        /// <summary>
        /// The largest size, as a share of the game's own, at which
        /// <paramref name="count"/> buttons fit in <paramref name="box"/>: 1
        /// when they fit already.
        /// </summary>
        internal static float ScaleToFit(int count, Vector2 box, Vector2 cell, Vector2 spacing)
        {
            for (float s = 1f; s > 0.3f; s -= 0.05f)
            {
                int across = Mathf.FloorToInt((box.x + spacing.x * s) / ((cell.x + spacing.x) * s));
                int down = Mathf.FloorToInt((box.y + spacing.y * s) / ((cell.y + spacing.y) * s));
                if (across * down >= count) return s;
            }
            return 0.3f;
        }

        /// <summary>The scene is going: what was found belongs to it.</summary>
        public static void Reset()
        {
            _grid = null;
            _box = null;
            _lastCount = -1;
            _nextLook = 0f;
            _haveOriginal = false;
        }
    }
}
