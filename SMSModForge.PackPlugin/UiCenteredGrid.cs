using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// A grid with the two things Unity's lacks: centring a final line that
    /// never filled up, and being only as big as what it holds.
    /// <para/>
    /// Unity's own <see cref="GridLayoutGroup"/> packs an incomplete last row
    /// against the start corner, so nineteen things in rows of seven end with
    /// five shoved to one side under two full rows. There is no option for it,
    /// which is why this exists.
    /// <para/>
    /// And with a fixed number of columns it says it wants that many columns'
    /// width whatever it holds, so something sizing itself to it (a Content
    /// Size Fitter) is eight cells wide holding two. <see cref="FitToContent"/>
    /// counts only the columns its children fill - the same for rows with a
    /// fixed number of rows. Unity already places the cells for that width (it
    /// clamps to the child count), so only the size asked for changes.
    /// <para/>
    /// It works from where the base class actually put things rather than
    /// re-deriving the column count. Unity's own rules for that - the
    /// constraint, how many cells fit, the clamp to the child count - are
    /// private and fiddly, and a second copy of them would be a second thing to
    /// get wrong. Reading the finished positions cannot disagree with the
    /// layout, because it IS the layout.
    /// </summary>
    public class UiCenteredGrid : GridLayoutGroup
    {
        /// <summary>Rounding for deciding two children share a line. Unity lays
        /// out in floats; a line is not a line to within a thousandth.</summary>
        private const float SameLine = 0.5f;

        /// <summary>Centre a final line that never filled up.</summary>
        public bool CentreLastLine = true;

        /// <summary>Only as wide (a fixed number of columns) or as tall (a
        /// fixed number of rows) as the lines its children fill.</summary>
        public bool FitToContent;

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            if (FitToContent && constraint == Constraint.FixedColumnCount)
                Fit(0, Mathf.Min(rectChildren.Count, constraintCount), padding.horizontal, cellSize.x, spacing.x);
        }

        public override void CalculateLayoutInputVertical()
        {
            base.CalculateLayoutInputVertical();
            if (FitToContent && constraint == Constraint.FixedRowCount)
                Fit(1, Mathf.Min(rectChildren.Count, constraintCount), padding.vertical, cellSize.y, spacing.y);
        }

        /// <summary>The size of <paramref name="lines"/> cells along one axis,
        /// as both the least and the preferred - Unity's own sum for its grid.</summary>
        private void Fit(int axis, int lines, float pad, float cell, float gap)
        {
            float size = pad + Mathf.Max(0f, (cell + gap) * lines - gap);
            SetLayoutInputForAxis(size, size, -1, axis);
        }

        /// <summary>
        /// After the base class has placed everything, slide the short line.
        /// <para/>
        /// Done here rather than in SetLayoutHorizontal because both axes are
        /// settled by now - which line is last is a question about the other
        /// axis from the one being moved.
        /// </summary>
        public override void SetLayoutVertical()
        {
            base.SetLayoutVertical();
            if (!isActiveAndEnabled || !CentreLastLine) return;

            bool downFirst = startAxis == Axis.Vertical;
            Slide(downFirst);
        }

        private void Slide(bool downFirst)
        {
            var kids = new List<RectTransform>();
            for (int i = 0; i < rectChildren.Count; i++)
                if (rectChildren[i] != null) kids.Add(rectChildren[i]);
            if (kids.Count == 0) return;

            // Group by the axis the lines run ACROSS: rows share a y, columns
            // share an x.
            var lines = new List<List<RectTransform>>();
            var keys = new List<float>();

            for (int i = 0; i < kids.Count; i++)
            {
                var p = kids[i].anchoredPosition;
                float key = downFirst ? p.x : p.y;

                int found = -1;
                for (int k = 0; k < keys.Count; k++)
                    if (Mathf.Abs(keys[k] - key) <= SameLine) { found = k; break; }

                if (found < 0)
                {
                    keys.Add(key);
                    lines.Add(new List<RectTransform>());
                    found = lines.Count - 1;
                }
                lines[found].Add(kids[i]);
            }

            if (lines.Count < 2) return;   // one line is the whole grid: see below

            // The full lines say how wide a line is meant to be; the last one in
            // document order is the one that may be short. "Last" is the line
            // holding the final child, which is right however the start corner
            // turns the grid round.
            int per = 0;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Count > per) per = lines[i].Count;

            var lastChild = kids[kids.Count - 1];
            int lastLine = -1;
            for (int i = 0; i < lines.Count && lastLine < 0; i++)
                if (lines[i].Contains(lastChild)) lastLine = i;

            if (lastLine < 0) return;

            int have = lines[lastLine].Count;
            if (have >= per) return;       // it filled up; nothing to centre

            float step = downFirst ? cellSize.y + spacing.y : cellSize.x + spacing.x;
            float slide = (per - have) * 0.5f * step;

            // Which way the slack lies depends on the corner the grid fills
            // from. Sliding the same way regardless would push the short line
            // further into the corner it is already in.
            bool fromFar = downFirst
                ? startCorner == Corner.LowerLeft || startCorner == Corner.LowerRight
                : startCorner == Corner.UpperRight || startCorner == Corner.LowerRight;

            if (fromFar) slide = -slide;

            for (int i = 0; i < lines[lastLine].Count; i++)
            {
                var rt = lines[lastLine][i];
                var p = rt.anchoredPosition;

                // Unity's y counts up, and a grid fills downwards, so a line
                // that should move "down the page" moves negatively.
                if (downFirst) p.y -= slide; else p.x += slide;
                rt.anchoredPosition = p;
            }
        }
    }
}
