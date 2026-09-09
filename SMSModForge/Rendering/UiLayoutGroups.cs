using System;
using System.Collections.Generic;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// Working out where a layout group puts its children.
/// <para/>
/// Unity's own arithmetic, reimplemented, because the preview has to agree with
/// the game about it. For a screen the game owns this is not needed - the
/// extraction recorded where Unity actually put everything - but a pack
/// authoring its own list has no such record, and a preview that ignored the
/// group would draw every item on top of every other.
/// <para/>
/// Checked against the game rather than against my reading of the docs:
/// UiLayoutGroupTests arranges every layout group in the extraction and
/// compares the result to the rectangles Unity itself produced.
/// </summary>
public static class UiLayoutGroups
{
    /// <summary>What a child brings to the arrangement: the size it would have
    /// on its own, used whenever the group is not setting that axis.</summary>
    public readonly record struct Item(double Width, double Height);

    /// <summary>
    /// Where each child goes, in canvas coordinates.
    /// <para/>
    /// <paramref name="parent"/> is the group's own rectangle. Children are in
    /// sibling order; the result matches one-to-one.
    /// </summary>
    public static UiRect[] Arrange(UiLayoutDef layout, UiRect parent, IReadOnlyList<Item> children)
    {
        if (layout == null || children == null || children.Count == 0)
            return Array.Empty<UiRect>();

        // Padding is stated the way Unity states it - left, right, top, bottom -
        // and top is a distance DOWN from the top edge, while y counts up.
        double padL = At(layout.Padding, 0), padR = At(layout.Padding, 1);
        double padT = At(layout.Padding, 2), padB = At(layout.Padding, 3);

        var inner = new UiRect(parent.X + padL,
                               parent.Y + padB,
                               Math.Max(0, parent.Width - padL - padR),
                               Math.Max(0, parent.Height - padT - padB));

        return layout.IsGrid ? Grid(layout, inner, children) : Line(layout, inner, children);
    }

    // ── A row or a column ────────────────────────────────────────────

    private static UiRect[] Line(UiLayoutDef layout, UiRect inner, IReadOnlyList<Item> children)
    {
        bool vertical = layout.IsVertical;
        int n = children.Count;
        double spacing = At(layout.Spacing, 0);

        // Along the axis the group runs in.
        double along = vertical ? inner.Height : inner.Width;
        bool control = vertical ? layout.ControlHeight : layout.ControlWidth;
        bool expand = vertical ? layout.ExpandHeight : layout.ExpandWidth;

        var sizes = new double[n];
        for (int i = 0; i < n; i++)
            sizes[i] = vertical ? children[i].Height : children[i].Width;

        if (control)
        {
            // The group is setting this axis. Nothing here declares a preferred
            // size - that would be a LayoutElement, which the authored model
            // does not have - so a controlled axis without expansion collapses
            // to nothing, exactly as it does in Unity. With expansion the
            // children share the space, which is what a row of equal items is.
            double share = expand && n > 0
                ? Math.Max(0, (along - spacing * (n - 1)) / n)
                : 0;
            for (int i = 0; i < n; i++) sizes[i] = share;
        }
        else if (expand)
        {
            // Sizes stay, and the surplus is shared out on top of them.
            double used = spacing * (n - 1);
            for (int i = 0; i < n; i++) used += sizes[i];

            double surplus = along - used;
            if (surplus > 0 && n > 0)
                for (int i = 0; i < n; i++) sizes[i] += surplus / n;
        }

        double total = spacing * (n - 1);
        for (int i = 0; i < n; i++) total += sizes[i];

        // Where the block sits when it is smaller than the space it is in.
        // A column is walked DOWNWARDS from the top edge below, so its offset
        // is already measured the way the anchor names read and must not be
        // flipped as well - flipping it put an "Upper" column at the bottom.
        double alongFraction = vertical
            ? VerticalFraction(layout.Alignment)
            : HorizontalFraction(layout.Alignment);
        double start = (along - total) * alongFraction;

        // The cross axis: either the group fills it, or each child keeps its
        // own size and is placed by the same alignment.
        bool controlCross = vertical ? layout.ControlWidth : layout.ControlHeight;
        bool expandCross = vertical ? layout.ExpandWidth : layout.ExpandHeight;
        double across = vertical ? inner.Width : inner.Height;
        double crossFraction = vertical
            ? HorizontalFraction(layout.Alignment)
            : 1 - VerticalFraction(layout.Alignment);

        var placed = new UiRect[n];
        double at = start;

        for (int slot = 0; slot < n; slot++)
        {
            // Reversed, the first child takes the LAST slot. The sizes are
            // walked in slot order either way, so a row of differently sized
            // items reverses without the gaps moving.
            int i = layout.Reverse ? n - 1 - slot : slot;
            double size = sizes[i];
            double crossSize = controlCross || expandCross
                ? across
                : (vertical ? children[i].Width : children[i].Height);
            double crossAt = (across - crossSize) * crossFraction;

            if (vertical)
            {
                // Down the column: the first child is at the TOP, so its y is
                // measured from the top edge downwards.
                double top = inner.Top - at;
                placed[i] = new UiRect(inner.X + crossAt, top - size, crossSize, size);
            }
            else
            {
                placed[i] = new UiRect(inner.X + at, inner.Y + crossAt, size, crossSize);
            }

            at += size + spacing;
        }

        return placed;
    }

    // ── A grid ───────────────────────────────────────────────────────

    private static UiRect[] Grid(UiLayoutDef layout, UiRect inner, IReadOnlyList<Item> children)
    {
        int n = children.Count;
        double cellW = At(layout.CellSize, 0), cellH = At(layout.CellSize, 1);
        double gapX = At(layout.Spacing, 0), gapY = At(layout.Spacing, 1);

        // Unity's own two-step: work out how many cells COULD fit, then how
        // many are actually used. Getting this from the item count alone is
        // what put a Flexible grid that fills downwards into a single row.
        bool downFirst = string.Equals(layout.StartAxis, "Vertical", StringComparison.Ordinal);

        int couldX, couldY;
        if (string.Equals(layout.Constraint, "FixedColumnCount", StringComparison.Ordinal))
        {
            couldX = Math.Max(1, layout.ConstraintCount);
            couldY = Ceil(n, couldX);
        }
        else if (string.Equals(layout.Constraint, "FixedRowCount", StringComparison.Ordinal))
        {
            couldY = Math.Max(1, layout.ConstraintCount);
            couldX = Ceil(n, couldY);
        }
        else
        {
            // How many fit, on each axis independently. A grid that fills
            // downwards is limited by the height, not the width.
            couldX = cellW + gapX > 0
                ? Math.Max(1, (int)Math.Floor((inner.Width + gapX + 0.001) / (cellW + gapX)))
                : n;
            couldY = cellH + gapY > 0
                ? Math.Max(1, (int)Math.Floor((inner.Height + gapY + 0.001) / (cellH + gapY)))
                : n;
        }

        int columns, rows;
        if (downFirst)
        {
            rows = Math.Max(1, Math.Min(couldY, n));
            columns = Math.Max(1, Math.Min(couldX, Ceil(n, rows)));
        }
        else
        {
            columns = Math.Max(1, Math.Min(couldX, n));
            rows = Math.Max(1, Math.Min(couldY, Ceil(n, columns)));
        }

        double blockW = columns * cellW + (columns - 1) * gapX;
        double blockH = rows * cellH + (rows - 1) * gapY;

        double offsetX = (inner.Width - blockW) * HorizontalFraction(layout.Alignment);
        double offsetY = (inner.Height - blockH) * VerticalFraction(layout.Alignment);

        bool fromRight = layout.StartCorner.IndexOf("Right", StringComparison.Ordinal) >= 0;
        bool fromBottom = layout.StartCorner.IndexOf("Lower", StringComparison.Ordinal) >= 0;

        var placed = new UiRect[n];
        for (int i = 0; i < n; i++)
        {
            int col = downFirst ? i / Math.Max(1, rows) : i % columns;
            int row = downFirst ? i % Math.Max(1, rows) : i / columns;

            // How much to slide THIS item, if it is in a final line that never
            // filled up. Worked out before the start corner is applied, then
            // turned round with it - centring is symmetrical, but which way the
            // slack lies is not.
            double slide = 0;
            if (layout.CenterLastLine)
            {
                int line = downFirst ? col : row;
                int lines = downFirst ? columns : rows;
                int per = downFirst ? rows : columns;

                if (line == lines - 1)
                {
                    int inLine = n - line * per;             // what the last one got
                    if (inLine > 0 && inLine < per)
                        slide = (per - inLine) * 0.5
                              * (downFirst ? cellH + gapY : cellW + gapX);
                }
            }

            if (fromRight) col = columns - 1 - col;
            if (fromBottom) row = rows - 1 - row;

            double slideX = downFirst ? 0 : (fromRight ? -slide : slide);
            double slideY = downFirst ? (fromBottom ? -slide : slide) : 0;

            double x = inner.X + offsetX + col * (cellW + gapX) + slideX;
            // Row 0 is the top one, and y counts up from the bottom.
            double top = inner.Top - offsetY - row * (cellH + gapY) - slideY;
            placed[i] = new UiRect(x, top - cellH, cellW, cellH);
        }
        return placed;
    }

    // ── Unity's TextAnchor, as two fractions ─────────────────────────

    /// <summary>0 at the left, 0.5 centred, 1 at the right.</summary>
    private static double HorizontalFraction(string? anchor)
    {
        string a = anchor ?? "";
        if (a.EndsWith("Left", StringComparison.Ordinal)) return 0;
        if (a.EndsWith("Right", StringComparison.Ordinal)) return 1;
        return 0.5;
    }

    /// <summary>0 at the top, 0.5 middled, 1 at the bottom - counting DOWN, the
    /// way the anchor names read.</summary>
    private static double VerticalFraction(string? anchor)
    {
        string a = anchor ?? "";
        if (a.StartsWith("Upper", StringComparison.Ordinal)) return 0;
        if (a.StartsWith("Lower", StringComparison.Ordinal)) return 1;
        return 0.5;
    }

    private static int Ceil(int count, int per) => per <= 0 ? count : (count + per - 1) / per;

    private static double At(float[]? values, int i)
        => values != null && values.Length > i ? values[i] : 0.0;
}
