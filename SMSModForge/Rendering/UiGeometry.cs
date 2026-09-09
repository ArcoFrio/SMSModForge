using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// Where the objects of an authored tree land, without drawing any of them.
/// <para/>
/// The renderer already works this out on its way through a tree, but it works
/// it out and throws it away. A gizmo needs the same answer for one object,
/// before anything is drawn, so it can put handles on it — and it must be the
/// SAME answer, or the handles sit somewhere the picture does not.
/// <para/>
/// So the resolve itself lives here and the renderer calls it, rather than the
/// two each having their own copy of a formula that is easy to get subtly
/// wrong.
/// </summary>
public static class UiGeometry
{
    /// <summary>One object's rectangle, in canvas coordinates, given where its
    /// parent landed. Ignores scale - see <see cref="Place"/>.</summary>
    public static UiRect Resolve(UiNodeDef node, UiRect parent)
        => UiLayout.Resolve(parent,
                            Doubles(node.Rect.AnchorMin), Doubles(node.Rect.AnchorMax),
                            Doubles(node.Rect.Pivot), Doubles(node.Rect.Position),
                            Doubles(node.Rect.Size));

    /// <summary>
    /// Where an object lands and where its children should be laid out from.
    /// <para/>
    /// Two answers because scale makes them different things. Unity scales an
    /// object about its pivot and carries that scale down the whole subtree,
    /// but a child's own anchors and size are expressed in its parent's
    /// UNSCALED space - so laying children out against the scaled rectangle
    /// would shrink their offsets twice and leave them drifting inwards.
    /// <para/>
    /// So <paramref name="parent"/> is the unscaled rectangle, the returned
    /// Layout is the unscaled rectangle to give this node's children, and Drawn
    /// is where it actually appears once every ancestor's scale is applied.
    /// </summary>
    /// <param name="placed">Where the parent's layout put this node, when the
    /// parent arranges its children. Its own anchors are then not consulted -
    /// that is what being in a layout group means.</param>
    public static (UiRect Layout, UiRect Drawn, UiXform Inner) Place(
        UiNodeDef node, UiRect parent, UiXform inherited, UiRect? placed = null)
    {
        var layout = placed ?? Resolve(node, parent);

        double sx = At(node.Rect.Scale, 0, 1), sy = At(node.Rect.Scale, 1, 1);
        double pivotX = layout.X + At(node.Rect.Pivot, 0, 0.5) * layout.Width;
        double pivotY = layout.Y + At(node.Rect.Pivot, 1, 0.5) * layout.Height;

        // Scaling about the pivot, written as the affine map it is.
        var own = new UiXform(sx, sy, pivotX * (1 - sx), pivotY * (1 - sy));
        var inner = own.Then(inherited);

        return (layout, inner.Apply(layout), inner);
    }

    private static double At(float[]? values, int i, double fallback)
        => values != null && values.Length > i ? values[i] : fallback;

    /// <summary>
    /// Where a node's children go, when the node arranges them itself.
    /// <para/>
    /// Null when it does not, which is the normal case - the caller then
    /// resolves each child from its own anchors as usual. Hidden children are
    /// left out of the arrangement exactly as Unity leaves them out, which is
    /// the whole point: that is what makes a list close ranks.
    /// </summary>
    public static UiRect[]? ArrangeChildren(UiNodeDef node, UiRect nodeRect)
    {
        var layout = node?.Layout;
        if (layout == null || node!.Children.Count == 0) return null;

        var taking = new List<int>();
        var sizes = new List<UiLayoutGroups.Item>();
        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            if (!child.StartActive) continue;

            // The size the child would have had on its own, which is what a
            // group that is not setting sizes arranges from.
            var natural = Resolve(child, nodeRect);
            taking.Add(i);
            sizes.Add(new UiLayoutGroups.Item(natural.Width, natural.Height));
        }

        var placed = UiLayoutGroups.Arrange(layout, nodeRect, sizes);

        // Back onto every child, hidden ones included: they keep a rectangle so
        // that switching one on does not need the tree rebuilt, and they simply
        // took no space while off.
        var all = new UiRect[node.Children.Count];
        for (int i = 0; i < node.Children.Count; i++)
            all[i] = Resolve(node.Children[i], nodeRect);
        for (int slot = 0; slot < taking.Count; slot++)
            all[taking[slot]] = placed[slot];

        return all;
    }

    /// <summary>
    /// Every object in the tree, in drawing coordinates — origin top-left, y
    /// downwards, the same frame the composed bitmap is in.
    /// <para/>
    /// Keyed by the node object itself rather than by path, because that is
    /// what a selection is: two siblings can share a name, and a gizmo must not
    /// end up on the wrong one of them.
    /// </summary>
    public static Dictionary<UiNodeDef, UiRect> ScreenRects(
        UiNodeDef? root, double canvasWidth, double canvasHeight)
    {
        var found = new Dictionary<UiNodeDef, UiRect>(ReferenceComparer.Instance);
        if (root == null || canvasWidth <= 0 || canvasHeight <= 0) return found;
        Walk(root, UiRect.FromCanvas(canvasWidth, canvasHeight), UiXform.Identity);
        return found;

        void Walk(UiNodeDef node, UiRect parent, UiXform inherited, UiRect? placed = null)
        {
            var (layout, drawn, inner) = Place(node, parent, inherited, placed);
            found[node] = UiLayout.ToScreen(drawn, canvasWidth, canvasHeight);

            // A node that arranges its children hands each one its rectangle;
            // everything below that child still resolves against it as usual.
            var arranged = ArrangeChildren(node, layout);
            for (int i = 0; i < node.Children.Count; i++)
                Walk(node.Children[i], layout, inner, arranged?[i]);
        }
    }

    /// <summary>
    /// Every object of several trees, first at the back - the map hit testing
    /// needs while more than one screen is on display.
    /// </summary>
    public static List<(UiNodeDef Node, UiRect Rect)> ScreenRectsOf(
        IEnumerable<UiNodeDef> roots, double canvasWidth, double canvasHeight)
    {
        var found = new List<(UiNodeDef, UiRect)>();
        if (roots == null) return found;

        foreach (var root in roots)
        {
            if (root == null) continue;
            Walk(root, UiRect.FromCanvas(canvasWidth, canvasHeight), UiXform.Identity, true, null);
        }
        return found;

        void Walk(UiNodeDef node, UiRect parent, UiXform inherited, bool forced,
                  UiRect? placed = null)
        {
            // Hidden objects are not hit, exactly as in the game - a closed
            // panel does not swallow clicks meant for what is behind it.
            if (!node.StartActive && !forced) return;

            var (layout, drawn, inner) = Place(node, parent, inherited, placed);
            found.Add((node, UiLayout.ToScreen(drawn, canvasWidth, canvasHeight)));

            var arranged = ArrangeChildren(node, layout);
            for (int i = 0; i < node.Children.Count; i++)
                Walk(node.Children[i], layout, inner, false, arranged?[i]);
        }
    }

    /// <summary>
    /// The object at a point, in drawing coordinates - what a click on the
    /// picture should select.
    /// <para/>
    /// Nearest the front wins, so the search runs backwards through the order
    /// things were drawn in. An object that draws nothing is only the answer
    /// when nothing else is: a tree is full of containers that fill their
    /// parent and show nothing, and picking one of those in front of the thing
    /// an author aimed at feels like the click missed.
    /// </summary>
    /// <param name="accept">Which objects may be the answer. Null takes any -
    /// what selecting wants. Clicking passes the clickable ones, so that the
    /// pointer falls through decoration to whatever is behind it.</param>
    public static UiNodeDef? Pick(IEnumerable<UiNodeDef> roots, double x, double y,
                                  double canvasWidth, double canvasHeight,
                                  Func<UiNodeDef, bool>? accept = null)
    {
        var map = ScreenRectsOf(roots, canvasWidth, canvasHeight);
        UiNodeDef? anything = null;

        for (int i = map.Count - 1; i >= 0; i--)
        {
            var (node, r) = map[i];
            if (x < r.X || x > r.Right || y < r.Y || y > r.Top) continue;
            if (accept != null && !accept(node)) continue;

            if (node.Image != null || node.Text != null) return node;
            anything ??= node;
        }
        return anything;
    }

    /// <summary>One object's rectangle in drawing coordinates, or null when it
    /// is not in this tree — which is the normal answer while a selection is
    /// being changed, not an error.</summary>
    public static UiRect? ScreenRectOf(UiNodeDef? root, UiNodeDef? node,
                                       double canvasWidth, double canvasHeight)
    {
        if (root == null || node == null) return null;
        return ScreenRects(root, canvasWidth, canvasHeight)
               .TryGetValue(node, out var rect) ? rect : null;
    }

    private static double[] Doubles(float[]? pair)
        => pair == null ? new[] { 0.0, 0.0 } : pair.Select(v => (double)v).ToArray();

    /// <summary>Identity, not equality. Two nodes with the same contents are
    /// still two different objects on the screen.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<UiNodeDef>
    {
        public static readonly ReferenceComparer Instance = new();
        public bool Equals(UiNodeDef? a, UiNodeDef? b) => ReferenceEquals(a, b);
        public int GetHashCode(UiNodeDef n) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(n);
    }
}

/// <summary>
/// A scale and a shift, per axis: <c>x -&gt; Scale*x + Offset</c>.
/// <para/>
/// All that is needed to carry Unity's localScale down a UI tree. Rotation is
/// deliberately absent: a rotated rectangle is no longer a rectangle, and every
/// other thing here - hit testing, clipping, the slice grid - is built on the
/// assumption that it is. A pack that rotates a UI object will see it drawn
/// unrotated rather than drawn wrongly.
/// </summary>
public readonly record struct UiXform(double ScaleX, double ScaleY, double OffsetX, double OffsetY)
{
    public static readonly UiXform Identity = new(1, 1, 0, 0);

    public bool IsIdentity => ScaleX == 1 && ScaleY == 1 && OffsetX == 0 && OffsetY == 0;

    public UiRect Apply(UiRect r)
        => new(ScaleX * r.X + OffsetX, ScaleY * r.Y + OffsetY,
               ScaleX * r.Width, ScaleY * r.Height);

    /// <summary>This transform, then <paramref name="outer"/>.</summary>
    public UiXform Then(UiXform outer)
        => new(outer.ScaleX * ScaleX,
               outer.ScaleY * ScaleY,
               outer.ScaleX * OffsetX + outer.OffsetX,
               outer.ScaleY * OffsetY + outer.OffsetY);
}
