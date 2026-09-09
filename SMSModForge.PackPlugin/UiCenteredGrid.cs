using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// A grid that centres its final line when that line never filled up.
    /// <para/>
    /// Unity's own <see cref="GridLayoutGroup"/> packs an incomplete last row
    /// against the start corner, so nineteen things in rows of seven end with
    /// five shoved to one side under two full rows. There is no option for it,
    /// which is why this exists.
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
            if (!isActiveAndEnabled) return;

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
