using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Dragging a pack up or down the main menu's list (the author, 1.7.0):
    /// the pack's lines dim while it is carried, a line shows the gap it will
    /// drop into, and letting go puts it there - the way a node is moved in
    /// the editor's dialogue list.
    /// <para/>
    /// The order is what packs load in, and lower in the list wins where two
    /// change the same thing of the game's (<see cref="SMSModForge.Shared.PackOrder"/>).
    /// Nothing is loaded here: packs are read when a game starts or loads, so
    /// the setting changes and the list is drawn again.
    /// </summary>
    internal sealed class PackListDrag
    {
        private const string Tag = "[SMSModForge.PackPlugin] Pack order: ";

        /// <summary>One pack's lines on the menu.</summary>
        private sealed class Block
        {
            public string PackId;
            public readonly List<RectTransform> Rows = new List<RectTransform>();
        }

        private readonly List<Block> _blocks = new List<Block>();
        private readonly Transform _menuRoot;
        private readonly float _stride;
        private readonly Action<string, int> _dropped;
        private readonly BepInEx.Logging.ManualLogSource _log;

        private Block _carried;
        private RectTransform _line;
        private int _gap = -1;

        /// <param name="dropped">Called with the pack and the gap it was let go
        /// in, 0 above the first pack - an index into the list as it was shown.</param>
        public PackListDrag(Transform menuRoot, float stride, Action<string, int> dropped,
                            BepInEx.Logging.ManualLogSource log)
        {
            _menuRoot = menuRoot;
            _stride = stride;
            _dropped = dropped;
            _log = log;
        }

        /// <summary>The packs in the order they are shown, as ids.</summary>
        public List<string> Shown()
        {
            var ids = new List<string>();
            foreach (var b in _blocks) ids.Add(b.PackId);
            return ids;
        }

        /// <summary>
        /// Make a pack's lines something to drag by: a see-through strip the
        /// height of a line behind each, which is what the pointer finds - the
        /// text's own box is taller than a line and would reach into the pack
        /// above. Behind the box that switches the pack, so a click on the box
        /// is still the box's.
        /// </summary>
        public void Add(string packId, GameObject row)
        {
            if (row == null || string.IsNullOrEmpty(packId)) return;
            Block block = _blocks.Count > 0 && _blocks[_blocks.Count - 1].PackId == packId
                ? _blocks[_blocks.Count - 1] : null;
            if (block == null)
            {
                block = new Block { PackId = packId };
                _blocks.Add(block);
            }
            var rect = row.GetComponent<RectTransform>();
            if (rect == null) return;
            block.Rows.Add(rect);

            var hit = new GameObject("PackDrag", typeof(RectTransform), typeof(Image));
            var hitRect = (RectTransform)hit.transform;
            hitRect.SetParent(rect, false);
            hitRect.SetAsFirstSibling();
            hitRect.anchorMin = hitRect.anchorMax = hitRect.pivot = new Vector2(0.5f, 0.5f);
            hitRect.sizeDelta = new Vector2(rect.sizeDelta.x, _stride);
            hitRect.anchoredPosition = Vector2.zero;
            var image = hit.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;

            var handle = row.AddComponent<PackRowDragHandle>();
            handle.List = this;
            handle.PackId = packId;
        }

        internal void Begin(string packId, PointerEventData e)
        {
            _carried = _blocks.Find(b => b.PackId == packId);
            if (_carried == null) return;
            foreach (var r in _carried.Rows) Fade(r, 0.4f);
            if (_line == null) _line = MakeLine();
            _line.gameObject.SetActive(true);
            Move(e);
        }

        internal void Move(PointerEventData e)
        {
            if (_carried == null || _line == null) return;
            Vector3 at;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    (RectTransform)_menuRoot, e.position, e.pressEventCamera, out at)) return;

            // The gap is how many packs are above the pointer.
            int gap = 0;
            foreach (var b in _blocks)
            {
                float top = b.Rows[0].position.y, bottom = b.Rows[b.Rows.Count - 1].position.y;
                if ((top + bottom) / 2f > at.y) gap++;
            }
            _gap = gap;
            Place(gap);
        }

        internal void End(PointerEventData e)
        {
            if (_carried == null) return;
            Move(e);
            foreach (var r in _carried.Rows) Fade(r, 1f);
            if (_line != null) _line.gameObject.SetActive(false);

            string id = _carried.PackId;
            int from = _blocks.IndexOf(_carried);
            int gap = _gap;
            _carried = null;
            _gap = -1;

            // Let go where it was, or just under itself: nothing moved.
            if (gap < 0 || gap == from || gap == from + 1) return;
            try { _dropped?.Invoke(id, gap); }
            catch (Exception ex) { _log?.LogWarning(Tag + "could not be changed: " + ex.Message); }
        }

        /// <summary>Put the line in a gap: between two packs' lines, or above
        /// the first or under the last.</summary>
        private void Place(int gap)
        {
            if (_blocks.Count == 0) return;
            var first = _blocks[0].Rows[0];
            float y;
            if (gap <= 0) y = first.anchoredPosition.y + _stride / 2f;
            else if (gap >= _blocks.Count)
            {
                var lastBlock = _blocks[_blocks.Count - 1];
                y = lastBlock.Rows[lastBlock.Rows.Count - 1].anchoredPosition.y - _stride / 2f;
            }
            else
            {
                var above = _blocks[gap - 1];
                float a = above.Rows[above.Rows.Count - 1].anchoredPosition.y;
                float b = _blocks[gap].Rows[0].anchoredPosition.y;
                y = (a + b) / 2f;
            }
            _line.anchoredPosition = new Vector2(first.anchoredPosition.x, y);
            _line.SetAsLastSibling();
        }

        /// <summary>The line showing where the pack will drop: across a row,
        /// laid out like one.</summary>
        private RectTransform MakeLine()
        {
            var first = _blocks[0].Rows[0];
            var go = new GameObject("SMSModForgeBanner_DropLine", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_menuRoot, false);
            rect.anchorMin = first.anchorMin;
            rect.anchorMax = first.anchorMax;
            rect.pivot = first.pivot;
            rect.sizeDelta = new Vector2(first.sizeDelta.x, 3f);
            var image = go.GetComponent<Image>();
            image.color = new Color(1f, 0.82f, 0.35f, 0.95f);
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>Dim a line while it is carried, and bring it back.</summary>
        private static void Fade(RectTransform row, float alpha)
        {
            if (row == null) return;
            // Not ??: a Unity object's null is not C#'s.
            var group = row.GetComponent<CanvasGroup>();
            if (group == null) group = row.gameObject.AddComponent<CanvasGroup>();
            group.alpha = alpha;
        }
    }

    /// <summary>What the pointer finds on a pack's line: hands the drag to
    /// the list.</summary>
    internal sealed class PackRowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public PackListDrag List;
        public string PackId;

        public void OnBeginDrag(PointerEventData eventData) { List?.Begin(PackId, eventData); }
        public void OnDrag(PointerEventData eventData) { List?.Move(eventData); }
        public void OnEndDrag(PointerEventData eventData) { List?.End(eventData); }
    }
}
