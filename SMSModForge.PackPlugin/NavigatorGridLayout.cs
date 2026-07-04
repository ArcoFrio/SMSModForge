using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Custom <see cref="GridLayoutGroup"/> that handles overflow into a second
    /// row when more navigator buttons are visible than fit in a single row
    /// (vanilla supports 6).
    /// <para/>
    /// Ported from <c>a Unity grid layout</c> (originally by
    /// MrBeardy). The key differences from the base Unity implementation:
    /// <list type="bullet">
    ///   <item>When more than one row is needed, the layout shifts everything
    ///         upward so the <em>last</em> row sits at the original strip
    ///         position (top-anchored) and earlier rows extend above it.</item>
    ///   <item>Toggles <see cref="NavigatorGridSetup.ExtraNavRow"/> via a
    ///         deferred coroutine so the nav strip background expands to cover
    ///         both rows.</item>
    /// </list>
    /// </summary>
    public class NavigatorGridLayout : GridLayoutGroup
    {
        public override void SetLayoutHorizontal()
        {
            SetCellsAlongAxis(0);
        }

        public override void SetLayoutVertical()
        {
            SetCellsAlongAxis(1);
        }

        private void SetCellsAlongAxis(int axis)
        {
            // Source: MrBeardy's BeardyGridLayout
            // https://github.com/mrbeardy/BeardyGridLayout/blob/main/src/Runtime/GridLayoutGroup.cs

            var rectChildrenCount = rectChildren.Count;
            if (axis == 0)
            {
                for (int i = 0; i < rectChildrenCount; i++)
                {
                    RectTransform rect = rectChildren[i];

                    m_Tracker.Add(this, rect,
                        DrivenTransformProperties.Anchors |
                        DrivenTransformProperties.AnchoredPosition |
                        DrivenTransformProperties.SizeDelta);

                    rect.anchorMin = Vector2.up;
                    rect.anchorMax = Vector2.up;
                    rect.sizeDelta = cellSize;
                }
                return;
            }

            float width = rectTransform.rect.size.x;
            float height = rectTransform.rect.size.y;

            int cellCountX = 1;
            int cellCountY = 1;
            if (m_Constraint == Constraint.FixedColumnCount)
            {
                cellCountX = m_ConstraintCount;
                if (rectChildrenCount > cellCountX)
                    cellCountY = rectChildrenCount / cellCountX + (rectChildrenCount % cellCountX > 0 ? 1 : 0);
            }
            else if (m_Constraint == Constraint.FixedRowCount)
            {
                cellCountY = m_ConstraintCount;
                if (rectChildrenCount > cellCountY)
                    cellCountX = rectChildrenCount / cellCountY + (rectChildrenCount % cellCountY > 0 ? 1 : 0);
            }
            else
            {
                if (cellSize.x + spacing.x <= 0)
                    cellCountX = int.MaxValue;
                else
                    cellCountX = Mathf.Max(1, Mathf.FloorToInt((width - padding.horizontal + spacing.x + 0.001f) / (cellSize.x + spacing.x)));

                if (cellSize.y + spacing.y <= 0)
                    cellCountY = int.MaxValue;
                else
                    cellCountY = Mathf.Max(1, Mathf.FloorToInt((height - padding.vertical + spacing.y + 0.001f) / (cellSize.y + spacing.y)));
            }

            int cornerX = (int)startCorner % 2;
            int cornerY = (int)startCorner / 2;

            int cellsPerMainAxis, actualCellCountX, actualCellCountY;
            if (startAxis == Axis.Horizontal)
            {
                cellsPerMainAxis = cellCountX;
                actualCellCountX = Mathf.Clamp(cellCountX, 1, rectChildrenCount);
                actualCellCountY = Mathf.Clamp(cellCountY, 1, Mathf.CeilToInt(rectChildrenCount / (float)cellsPerMainAxis));
            }
            else
            {
                cellsPerMainAxis = cellCountY;
                actualCellCountY = Mathf.Clamp(cellCountY, 1, rectChildrenCount);
                actualCellCountX = Mathf.Clamp(cellCountX, 1, Mathf.CeilToInt(rectChildrenCount / (float)cellsPerMainAxis));
            }

            Vector2 requiredSpace = new Vector2(
                actualCellCountX * cellSize.x + (actualCellCountX - 1) * spacing.x,
                actualCellCountY * cellSize.y + (actualCellCountY - 1) * spacing.y
            );
            Vector2 startOffset = new Vector2(
                GetStartOffset(0, requiredSpace.x),
                GetStartOffset(1, requiredSpace.y)
            );

            // Vertical shift: when >1 row, push everything up so the last row
            // sits where the single row normally would.
            float verticalShift = (actualCellCountY > 1)
                ? (actualCellCountY - 1.5f) * (cellSize.y + spacing.y)
                : 0f;
            startOffset.y -= verticalShift;

            // Toggle the extended nav-row background when the row count changes.
            var extraRow = NavigatorGridSetup.ExtraNavRow;
            if (extraRow != null)
            {
                bool shouldBeActive = actualCellCountY > 1;
                if (extraRow.activeSelf != shouldBeActive)
                {
                    if (this is MonoBehaviour mb)
                        mb.StartCoroutine(DeferSetActive(extraRow, shouldBeActive));
                }
            }

            int actualLastCellsCount = (rectChildrenCount % cellsPerMainAxis);
            if (actualLastCellsCount == 0) actualLastCellsCount = cellsPerMainAxis;
            int cellsX = startAxis == Axis.Horizontal ? actualLastCellsCount : actualCellCountX;
            int cellsY = startAxis == Axis.Vertical ? actualLastCellsCount : actualCellCountY;

            Vector2 lastCellsRequiredSpace = new Vector2(
                cellsX * cellSize.x + (cellsX - 1) * spacing.x,
                cellsY * cellSize.y + (cellsY - 1) * spacing.y
            );
            Vector2 lastCellsStartOffset = new Vector2(
                GetStartOffset(0, lastCellsRequiredSpace.x),
                GetStartOffset(1, lastCellsRequiredSpace.y)
            );
            lastCellsStartOffset.y -= verticalShift;

            for (int i = 0; i < rectChildrenCount; i++)
            {
                int positionX;
                int positionY;
                Vector2 cellStartOffset = (i + 1 > rectChildrenCount - actualLastCellsCount)
                    ? lastCellsStartOffset
                    : startOffset;

                if (startAxis == Axis.Horizontal)
                {
                    positionX = i % cellsPerMainAxis;
                    positionY = i / cellsPerMainAxis;
                }
                else
                {
                    positionX = i / cellsPerMainAxis;
                    positionY = i % cellsPerMainAxis;
                }

                if (cornerX == 1)
                    positionX = actualCellCountX - 1 - positionX;
                if (cornerY == 1)
                    positionY = actualCellCountY - 1 - positionY;

                SetChildAlongAxis(rectChildren[i], 0, cellStartOffset.x + (cellSize[0] + spacing[0]) * positionX, cellSize[0]);
                SetChildAlongAxis(rectChildren[i], 1, cellStartOffset.y + (cellSize[1] + spacing[1]) * positionY, cellSize[1]);
            }
        }

        private IEnumerator DeferSetActive(GameObject target, bool active)
        {
            yield return null;
            if (target != null)
                target.SetActive(active);
        }
    }
}
