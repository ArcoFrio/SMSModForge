using System;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>Which part of the selection box is being dragged.</summary>
public enum UiGrip
{
    None, Move, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight,
}

/// <summary>Where an object was when a drag began.</summary>
/// <param name="Width">The object's resolved width when the drag began, which
/// the size delta alone does not give: a stretched object's width is its
/// anchored span plus that delta.</param>
public readonly record struct UiDragStart(
    double PosX, double PosY, double SizeX, double SizeY, double Width, double Height)
{
    public static UiDragStart Of(UiNodeDef node, UiRect resolved)
        => new(At(node.Rect.Position, 0), At(node.Rect.Position, 1),
               At(node.Rect.Size, 0), At(node.Rect.Size, 1),
               resolved.Width, resolved.Height);

    internal static double At(float[]? pair, int i)
        => pair != null && pair.Length > i ? pair[i] : 0.0;
}

/// <summary>
/// Turning a drag into changes to the numbers a UI object actually stores.
/// <para/>
/// Those numbers are an anchored POSITION and a SIZE DELTA, not a rectangle,
/// and that is what makes this more than arithmetic. Growing the right edge by
/// d adds d to the size — and the object grows around its pivot, so the LEFT
/// edge moves left by pivotX*d at the same time. Holding it still means moving
/// the position by pivotX*d as well. Each of the four cases below is that one
/// identity solved for a different pair of "this edge moves, that one does not".
/// <para/>
/// The same two lines work for a stretched object, where the size delta is an
/// inset rather than a size, because width is the anchored span plus the delta
/// either way and the span does not change when the delta does.
/// </summary>
public static class UiDrag
{
    /// <summary>Apply a drag, in canvas pixels, to a node. The node is changed
    /// in place; <paramref name="start"/> is where it was when the drag began,
    /// so a drag that wanders and comes back lands where it started.</summary>
    public static void Apply(UiNodeDef node, UiGrip grip, UiDragStart start,
                             double dx, double dy)
    {
        if (node == null || grip == UiGrip.None) return;

        double posX = start.PosX, posY = start.PosY;
        double sizeX = start.SizeX, sizeY = start.SizeY;
        double pivotX = UiDragStart.At(node.Rect.Pivot, 0);
        double pivotY = UiDragStart.At(node.Rect.Pivot, 1);

        if (grip == UiGrip.Move)
        {
            posX += dx;
            posY += dy;
        }
        else
        {
            // Clamped so an edge cannot be dragged through its opposite. A
            // rectangle with a negative size still draws, inside out, and reads
            // as the preview being broken rather than as the drag going too far.
            if (IsRight(grip))
            {
                double d = Math.Max(dx, 1 - start.Width);
                sizeX += d; posX += pivotX * d;
            }
            else if (IsLeft(grip))
            {
                double d = Math.Min(dx, start.Width - 1);
                sizeX -= d; posX += (1 - pivotX) * d;
            }

            if (IsTop(grip))
            {
                double d = Math.Max(dy, 1 - start.Height);
                sizeY += d; posY += pivotY * d;
            }
            else if (IsBottom(grip))
            {
                double d = Math.Min(dy, start.Height - 1);
                sizeY -= d; posY += (1 - pivotY) * d;
            }
        }

        // Whole canvas pixels. The game's own UI is authored on them, and a
        // position of 214.30003 is noise an author then has to look at.
        Set(node.Rect.Position, 0, (float)Math.Round(posX));
        Set(node.Rect.Position, 1, (float)Math.Round(posY));
        Set(node.Rect.Size, 0, (float)Math.Round(sizeX));
        Set(node.Rect.Size, 1, (float)Math.Round(sizeY));
    }

    public static bool IsLeft(UiGrip g) => g is UiGrip.Left or UiGrip.TopLeft or UiGrip.BottomLeft;
    public static bool IsRight(UiGrip g) => g is UiGrip.Right or UiGrip.TopRight or UiGrip.BottomRight;
    public static bool IsTop(UiGrip g) => g is UiGrip.Top or UiGrip.TopLeft or UiGrip.TopRight;
    public static bool IsBottom(UiGrip g) => g is UiGrip.Bottom or UiGrip.BottomLeft or UiGrip.BottomRight;

    private static void Set(float[]? pair, int i, float value)
    {
        if (pair != null && pair.Length > i) pair[i] = value;
    }
}
