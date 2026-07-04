using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Sets up the extended navigator bar for packs that have places with more
    /// than 6 navigator buttons (the vanilla strip capacity).
    /// <para/>
    /// The vanilla navigator (<c>9_MainCanvas/Navigator</c>) has a single-row
    /// <c>MapButtons</c> container laid out by a <c>HorizontalLayoutGroup</c>.
    /// When a custom place needs more than 6 buttons visible at once, buttons
    /// overflow the strip and become invisible. This class:
    /// <list type="number">
    ///   <item>Creates an <b>extra nav row</b> — a clone of the nav strip
    ///         background image, doubled in height and shifted upward — that
    ///         activates automatically when buttons overflow into a second
    ///         row.</item>
    ///   <item>Replaces the vanilla <c>HorizontalLayoutGroup</c> on
    ///         <c>MapButtons</c> with a custom
    ///         <see cref="NavigatorGridLayout"/> (6-column grid) that flows
    ///         buttons into multiple rows and shifts them so the last row
    ///         stays at the original strip position.</item>
    /// </list>
    /// <para/>
    /// This is the sole owner of the navigator grid — the host mod's former
    /// equivalent (<c>Places.SetupMapButtonsGrid</c> + its custom
    /// <c>GridLayoutGroup</c> subclass) was removed when navigation moved
    /// to the pack. A defensive <see cref="HasCustomGrid"/> check remains
    /// so a re-load (or some third-party mod that installs its own grid)
    /// never double-installs.
    /// </summary>
    public static class NavigatorGridSetup
    {
        /// <summary>
        /// The cloned+enlarged background image that sits behind the second
        /// row of buttons. Toggled by <see cref="NavigatorGridLayout"/> when
        /// the visible button count exceeds one row.
        /// </summary>
        public static GameObject ExtraNavRow;

        public static void Reset()
        {
            ExtraNavRow = null;
        }

        /// <summary>
        /// Call once after all packs have finished building places and wiring
        /// navigator buttons. Installs the custom grid + extra nav-row
        /// background unless a custom grid is somehow already present.
        /// </summary>
        public static void EnsureGrid(MonoBehaviour host)
        {
            host.StartCoroutine(EnsureGridCoroutine());
        }

        private static IEnumerator EnsureGridCoroutine()
        {
            var navigator = GameObject.Find("9_MainCanvas")?.transform.Find("Navigator");
            if (navigator == null) yield break;

            var mapButtons = navigator.Find("MapButtons")?.gameObject;
            if (mapButtons == null) yield break;

            // Defensive only: nothing else is expected to install a custom
            // grid anymore (the host mod's equivalent was removed when
            // navigation moved into the pack), but skip if one exists so a
            // double EnsureGrid call or an exotic mod setup can't stack
            // layout components.
            if (HasCustomGrid(mapButtons)) yield break;

            // ── Remove existing layout groups ────────────────────────
            var hlg = mapButtons.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) Object.Destroy(hlg);
            var vlg = mapButtons.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) Object.Destroy(vlg);
            var unityGrid = mapButtons.GetComponent<GridLayoutGroup>();
            if (unityGrid != null) Object.Destroy(unityGrid);

            // Wait one more frame for the Destroy calls to take effect.
            yield return null;

            // Last-chance re-check: a custom grid may have been added
            // during the destroy frame.
            if (HasCustomGrid(mapButtons)) yield break;

            // ── Create the extra nav-row background ──────────────────
            // Only once we've committed to installing the grid, so an
            // early bail above can never leave an orphaned clone behind.
            var image = navigator.Find("Image")?.gameObject;
            if (image != null)
            {
                ExtraNavRow = Object.Instantiate(image, navigator);
                var rt = ExtraNavRow.GetComponent<RectTransform>();
                var size = rt.sizeDelta;
                var pos = rt.anchoredPosition;
                size.y *= 2;
                pos.y *= 1.5f;
                rt.sizeDelta = size;
                rt.anchoredPosition = pos;
                // Place right after the original Image so it renders behind buttons.
                ExtraNavRow.transform.SetSiblingIndex(image.transform.GetSiblingIndex() + 1);
                ExtraNavRow.SetActive(false);
            }

            // ── Add the custom grid ──────────────────────────────────
            var grid = mapButtons.AddComponent<NavigatorGridLayout>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;
            grid.cellSize = new Vector2(125, 75);
            grid.spacing = new Vector2(15, 20);
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        }

        /// <summary>True when a custom (non-base) GridLayoutGroup subclass is
        /// attached to <paramref name="mapButtons"/> — ours from a prior load
        /// or the host mod's equivalent. Either one already handles multi-row
        /// overflow, so a second install would double-layout.</summary>
        private static bool HasCustomGrid(GameObject mapButtons)
        {
            foreach (var c in mapButtons.GetComponents<Component>())
            {
                if (c == null) continue;
                var t = c.GetType();
                if (typeof(GridLayoutGroup).IsAssignableFrom(t) && t != typeof(GridLayoutGroup))
                    return true;
            }
            return false;
        }
    }
}
