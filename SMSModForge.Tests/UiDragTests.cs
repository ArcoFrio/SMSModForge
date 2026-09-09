using System;
using System.Collections.Generic;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The gizmo's arithmetic, checked against what an author actually sees: not
/// the numbers that get stored, but where the rectangle ends up.
/// <para/>
/// That distinction is the whole risk here. A UI object stores an anchored
/// position and a size delta, and both of them move when one edge is dragged —
/// so a plausible-looking implementation that forgets the pivot leaves the
/// object growing out of its own middle while the handle stays under the
/// cursor, which looks almost right and is wrong everywhere except a pivot of
/// 0.5. These tests therefore sweep every pivot, both anchor modes, and every
/// handle, and assert the one thing that matters each time: the edge you
/// grabbed moved by exactly what you dragged, and the opposite edge did not
/// move at all.
/// </summary>
public sealed class UiDragTests
{
    private readonly ITestOutputHelper _out;
    public UiDragTests(ITestOutputHelper output) => _out = output;

    private static readonly UiRect Parent = UiRect.FromCanvas(1920, 1080);

    private static UiNodeDef Node(float pivotX, float pivotY, bool stretched)
    {
        var node = new UiNodeDef { Name = "Thing" };
        node.Rect.Pivot = new[] { pivotX, pivotY };
        node.Rect.Position = new[] { 20f, -30f };
        node.Rect.Size = new[] { 200f, 100f };
        if (stretched)
        {
            // Anchors that are a rectangle, where the size delta is an inset
            // rather than a size. Same two lines of arithmetic have to work.
            node.Rect.AnchorMin = new[] { 0.2f, 0.1f };
            node.Rect.AnchorMax = new[] { 0.8f, 0.6f };
        }
        else
        {
            node.Rect.AnchorMin = new[] { 0.5f, 0.5f };
            node.Rect.AnchorMax = new[] { 0.5f, 0.5f };
        }
        return node;
    }

    /// <summary>Drag by a multiple of 4 so that a pivot of 0.5 still lands on a
    /// whole pixel — the maths is exact, and the rounding to whole canvas
    /// pixels is deliberate, so the test should not be measuring the rounding.</summary>
    private const double D = 40;

    public static IEnumerable<object[]> Pivots()
    {
        foreach (float x in new[] { 0f, 0.5f, 1f })
            foreach (float y in new[] { 0f, 0.5f, 1f })
                foreach (bool stretched in new[] { false, true })
                    yield return new object[] { x, y, stretched };
    }

    [Theory]
    [MemberData(nameof(Pivots))]
    public void The_edge_you_grab_is_the_edge_that_moves(float pivotX, float pivotY, bool stretched)
    {
        foreach (var grip in new[]
                 {
                     UiGrip.Left, UiGrip.Right, UiGrip.Top, UiGrip.Bottom,
                     UiGrip.TopLeft, UiGrip.TopRight, UiGrip.BottomLeft, UiGrip.BottomRight,
                 })
        {
            var node = Node(pivotX, pivotY, stretched);
            var before = UiGeometry.Resolve(node, Parent);

            UiDrag.Apply(node, grip, UiDragStart.Of(node, before), D, D);
            var after = UiGeometry.Resolve(node, Parent);

            string what = $"{grip} at pivot ({pivotX},{pivotY}){(stretched ? " stretched" : "")}";

            // Horizontal: the grabbed side moves by the drag, the other stays.
            if (UiDrag.IsRight(grip))
            {
                Assert.Equal(before.Right + D, after.Right, 3);
                Assert.Equal(before.X, after.X, 3);
            }
            else if (UiDrag.IsLeft(grip))
            {
                Assert.Equal(before.X + D, after.X, 3);
                Assert.Equal(before.Right, after.Right, 3);
            }
            else
            {
                Assert.Equal(before.X, after.X, 3);
                Assert.Equal(before.Right, after.Right, 3);
            }

            // Vertical, in canvas terms: Top is the high edge, Y the low one.
            if (UiDrag.IsTop(grip))
            {
                Assert.Equal(before.Top + D, after.Top, 3);
                Assert.Equal(before.Y, after.Y, 3);
            }
            else if (UiDrag.IsBottom(grip))
            {
                Assert.Equal(before.Y + D, after.Y, 3);
                Assert.Equal(before.Top, after.Top, 3);
            }
            else
            {
                Assert.Equal(before.Y, after.Y, 3);
                Assert.Equal(before.Top, after.Top, 3);
            }

            _out.WriteLine($"{what}: {before} -> {after}");
        }
    }

    [Theory]
    [MemberData(nameof(Pivots))]
    public void Moving_changes_where_it_is_and_nothing_else(float pivotX, float pivotY, bool stretched)
    {
        var node = Node(pivotX, pivotY, stretched);
        var before = UiGeometry.Resolve(node, Parent);

        UiDrag.Apply(node, UiGrip.Move, UiDragStart.Of(node, before), D, -D);
        var after = UiGeometry.Resolve(node, Parent);

        Assert.Equal(before.X + D, after.X, 3);
        Assert.Equal(before.Y - D, after.Y, 3);
        Assert.Equal(before.Width, after.Width, 3);
        Assert.Equal(before.Height, after.Height, 3);
    }

    [Fact]
    public void A_drag_that_comes_back_leaves_the_object_where_it_started()
    {
        // Every move is applied from the state the drag began in, not added to
        // the last one. Accumulating would drift, and would make a drag that
        // returns to its origin leave the object somewhere else.
        var node = Node(0.5f, 0.5f, false);
        var start = UiDragStart.Of(node, UiGeometry.Resolve(node, Parent));

        UiDrag.Apply(node, UiGrip.Move, start, 137, -244);
        UiDrag.Apply(node, UiGrip.Move, start, -8, 96);
        UiDrag.Apply(node, UiGrip.Move, start, 0, 0);

        Assert.Equal(20f, node.Rect.Position[0]);
        Assert.Equal(-30f, node.Rect.Position[1]);
    }

    [Theory]
    [InlineData("Left")]
    [InlineData("Right")]
    [InlineData("Top")]
    [InlineData("Bottom")]
    public void An_edge_cannot_be_dragged_through_its_opposite(string gripName)
    {
        // Unclamped, this produces a negative size. Unity draws that inside
        // out, which reads as the preview being broken rather than as the drag
        // having gone too far.
        var grip = Enum.Parse<UiGrip>(gripName);
        var node = Node(0.5f, 0.5f, false);
        var before = UiGeometry.Resolve(node, Parent);

        // Far enough past the opposite edge to invert it several times over.
        double far = 10_000 * (UiDrag.IsLeft(grip) || UiDrag.IsBottom(grip) ? 1 : -1);
        UiDrag.Apply(node, grip, UiDragStart.Of(node, before), far, far);

        var after = UiGeometry.Resolve(node, Parent);
        Assert.True(after.Width >= 1, $"width {after.Width}");
        Assert.True(after.Height >= 1, $"height {after.Height}");
    }

    [Fact]
    public void The_root_of_a_tree_fills_the_canvas()
    {
        // What the handles are positioned against. If the root did not resolve
        // to the whole canvas, every rectangle under it would be offset by the
        // same amount and the gizmo would sit consistently wrong.
        var root = new UiNodeDef { Name = "Screen" };
        root.Rect.AnchorMin = new[] { 0f, 0f };
        root.Rect.AnchorMax = new[] { 1f, 1f };
        root.Rect.Size = new[] { 0f, 0f };
        root.Rect.Position = new[] { 0f, 0f };

        var rect = UiGeometry.ScreenRectOf(root, root, 1920, 1080);

        Assert.NotNull(rect);
        Assert.Equal(0, rect!.Value.X, 3);
        Assert.Equal(0, rect.Value.Y, 3);
        Assert.Equal(1920, rect.Value.Width, 3);
        Assert.Equal(1080, rect.Value.Height, 3);
    }

    [Fact]
    public void Two_objects_with_the_same_name_get_their_own_rectangles()
    {
        // The map is keyed by identity, not by name or by value. Siblings that
        // share a name are common in the vanilla UI, and a gizmo that landed on
        // the first of them whichever was selected would be worse than none.
        var root = new UiNodeDef { Name = "Screen" };
        root.Rect.AnchorMin = new[] { 0f, 0f };
        root.Rect.AnchorMax = new[] { 1f, 1f };
        root.Rect.Size = new[] { 0f, 0f };

        var a = new UiNodeDef { Name = "Image" };
        a.Rect.Position = new[] { -200f, 0f };
        var b = new UiNodeDef { Name = "Image" };
        b.Rect.Position = new[] { 200f, 0f };
        root.Children.Add(a);
        root.Children.Add(b);

        var rects = UiGeometry.ScreenRects(root, 1920, 1080);

        Assert.Equal(3, rects.Count);
        Assert.NotEqual(rects[a].X, rects[b].X);
        Assert.Equal(400, rects[b].X - rects[a].X, 3);
    }
}
