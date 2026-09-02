using System;

namespace SMSModForge.Rendering;

/// <summary>
/// A rectangle in canvas coordinates: x and y are its bottom-left corner, and y
/// increases upwards.
/// <para/>
/// Unity's convention, kept rather than converted, because every number the
/// extraction recorded is in it. Converting at the boundary would mean every
/// comparison against the game's own answers ran through a translation that
/// could itself be wrong — and then a mismatch would not say which of the two
/// was at fault. <see cref="UiLayout.ToScreen"/> flips it for drawing, once, at
/// the point where WPF needs it.
/// </summary>
public readonly record struct UiRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Top => Y + Height;

    public static UiRect FromCanvas(double width, double height)
        => new(-width / 2.0, -height / 2.0, width, height);

    public override string ToString()
        => $"({X:0.##},{Y:0.##}) {Width:0.##}×{Height:0.##}";
}

/// <summary>
/// Turning anchors into pixels — the calculation the whole preview rests on.
/// <para/>
/// A UI rectangle is not a position and a size. It is two anchors expressed as
/// fractions of the parent, a pivot, an offset from those anchors, and a size
/// delta; and what falls out of that depends on how big the parent is. That
/// indirection is the entire reason the game's UI survives resolutions nobody
/// authored it at, so a preview that stored positions instead would agree with
/// the game at exactly one window size and drift at every other.
/// <para/>
/// The two cases people trip over:
/// <list type="bullet">
///   <item>When the anchors are a POINT (min equals max on an axis), the size
///   delta is that axis's literal size.</item>
///   <item>When the anchors are a RECTANGLE (min differs from max), the object
///   stretches with the parent and the size delta becomes an inset — a delta of
///   zero means "exactly the anchor rectangle", and a negative one insets.</item>
/// </list>
/// Both fall out of one formula, which is why it is written as one rather than
/// as a branch: the anchored span contributes <c>(max - min) × parentSize</c>,
/// which is simply zero in the point case.
/// <para/>
/// None of this is guesswork. The surface extractor recorded both the authored
/// anchors and the rectangle Unity resolved them to, and UiLayoutTests checks
/// this against a hundred real ones spanning every anchor configuration in the
/// game. The formula is not allowed to be my opinion.
/// </summary>
public static class UiLayout
{
    /// <summary>Where a child sits inside its parent.</summary>
    /// <param name="parent">The parent's already-resolved rectangle.</param>
    /// <param name="anchorMin">Bottom-left anchor, as a fraction of the parent.</param>
    /// <param name="anchorMax">Top-right anchor, as a fraction of the parent.</param>
    /// <param name="pivot">The point in this rectangle that
    /// <paramref name="anchoredPosition"/> places.</param>
    /// <param name="anchoredPosition">Offset of the pivot from the anchors.</param>
    /// <param name="sizeDelta">Size when the anchors are a point; inset from
    /// them when the anchors are a rectangle.</param>
    public static UiRect Resolve(UiRect parent,
                                 double anchorMinX, double anchorMinY,
                                 double anchorMaxX, double anchorMaxY,
                                 double pivotX, double pivotY,
                                 double anchoredX, double anchoredY,
                                 double sizeDeltaX, double sizeDeltaY)
    {
        // The anchor rectangle, in canvas pixels.
        double aMinX = parent.X + anchorMinX * parent.Width;
        double aMaxX = parent.X + anchorMaxX * parent.Width;
        double aMinY = parent.Y + anchorMinY * parent.Height;
        double aMaxY = parent.Y + anchorMaxY * parent.Height;

        // Size is the anchored span plus the delta. Point anchors contribute a
        // span of zero, which is what makes the delta read as a literal size
        // there without needing a separate case.
        double width = (aMaxX - aMinX) + sizeDeltaX;
        double height = (aMaxY - aMinY) + sizeDeltaY;

        // The pivot is placed relative to the same fraction of the ANCHOR
        // rectangle, not of the parent. Using the parent here is the classic
        // error: it agrees for point anchors, where the anchor rectangle has no
        // width to take a fraction of, and is wrong for every stretched object.
        double refX = aMinX + (aMaxX - aMinX) * pivotX;
        double refY = aMinY + (aMaxY - aMinY) * pivotY;

        double pivotPosX = refX + anchoredX;
        double pivotPosY = refY + anchoredY;

        return new UiRect(pivotPosX - pivotX * width,
                          pivotPosY - pivotY * height,
                          width, height);
    }

    /// <summary>Convenience overload for callers holding pairs.</summary>
    public static UiRect Resolve(UiRect parent, double[] anchorMin, double[] anchorMax,
                                 double[] pivot, double[] anchoredPosition,
                                 double[] sizeDelta)
        => Resolve(parent,
                   At(anchorMin, 0), At(anchorMin, 1),
                   At(anchorMax, 0), At(anchorMax, 1),
                   At(pivot, 0), At(pivot, 1),
                   At(anchoredPosition, 0), At(anchoredPosition, 1),
                   At(sizeDelta, 0), At(sizeDelta, 1));

    private static double At(double[]? pair, int i)
        => pair != null && pair.Length > i ? pair[i] : 0.0;

    /// <summary>
    /// Canvas coordinates to drawing coordinates: origin to the top-left corner
    /// and y downwards, which is what WPF wants.
    /// <para/>
    /// Done once, here, rather than throughout: a flip applied twice looks
    /// almost right, because most UI is roughly symmetric about the middle of
    /// the screen.
    /// </summary>
    public static UiRect ToScreen(UiRect rect, double canvasWidth, double canvasHeight)
        => new(rect.X + canvasWidth / 2.0,
               canvasHeight / 2.0 - rect.Y - rect.Height,
               rect.Width, rect.Height);
}
