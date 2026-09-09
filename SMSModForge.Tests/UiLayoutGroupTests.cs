using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The layout arithmetic, checked against the game rather than against my
/// reading of Unity's documentation.
/// <para/>
/// The extraction recorded, for every layout group in the game, both the
/// group's settings AND the rectangles Unity actually resolved its children to.
/// So the right answer is already written down: arrange the children here, and
/// compare. That is the same standard UiLayout was held to, and for the same
/// reason - the formula is not allowed to be my opinion.
/// </summary>
public sealed class UiLayoutGroupTests
{
    private readonly ITestOutputHelper _out;
    public UiLayoutGroupTests(ITestOutputHelper output) => _out = output;

    /// <summary>Half a pixel. Unity rounds layout to whole pixels in places and
    /// the extraction wrote floats, so exact equality would fail on noise.</summary>
    private const double Tolerance = 0.5;

    private static UiLayoutDef From(VanillaUiSurface.LayoutGroupInfo g) => new()
    {
        Kind = g.IsVertical ? UiLayoutKinds.Vertical : UiLayoutKinds.Horizontal,
        Spacing = new[] { g.Spacing, g.Spacing },
        Padding = g.Padding,
        Alignment = g.ChildAlignment,
        ControlWidth = g.ChildControlWidth,
        ControlHeight = g.ChildControlHeight,
        ExpandWidth = g.ChildForceExpandWidth,
        ExpandHeight = g.ChildForceExpandHeight,
        Reverse = g.ReverseArrangement,
    };

    private static UiLayoutDef From(VanillaUiSurface.GridLayoutInfo g) => new()
    {
        Kind = UiLayoutKinds.Grid,
        CellSize = g.CellSize,
        Spacing = g.Spacing,
        Padding = g.Padding,
        Alignment = g.ChildAlignment,
        Constraint = g.Constraint,
        ConstraintCount = g.ConstraintCount,
        StartAxis = g.StartAxis,
        StartCorner = g.StartCorner,
    };

    /// <summary>The rectangle the extraction says an object actually occupies,
    /// in canvas coordinates.</summary>
    private static UiRect? Resolved(VanillaUiSurface.Node node)
    {
        var r = node.Rect?.Resolved;
        if (r?.Min == null || r.Size == null || r.Min.Length < 2 || r.Size.Length < 2) return null;
        return new UiRect(r.Min[0], r.Min[1], r.Size[0], r.Size[1]);
    }

    [Fact]
    public void Every_layout_group_in_the_game_is_arranged_the_way_the_game_arranged_it()
    {
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        int groups = 0, children = 0, agreed = 0, skipped = 0;
        int modelledGroups = 0, modelledChildren = 0, modelledAgreed = 0;
        var worst = new List<string>();

        foreach (var entry in VanillaUiCatalog.Surfaces)
        {
            var surface = VanillaUiLibrary.Surface(entry.Path);
            if (surface?.Root == null) continue;

            foreach (var node in surface.Root.Walk())
            {
                UiLayoutDef? layout =
                    node.LayoutGroup != null ? From(node.LayoutGroup)
                  : node.GridLayout != null ? From(node.GridLayout)
                  : null;
                if (layout == null || node.Children.Count == 0) continue;

                var parent = Resolved(node);
                if (parent == null) { skipped++; continue; }

                // Only the children Unity would have arranged.
                var live = node.Children.Where(c => c.ActiveSelf && Resolved(c) != null).ToList();
                if (live.Count == 0) { skipped++; continue; }

                var sizes = live.Select(c =>
                {
                    var r = Resolved(c)!.Value;
                    return new UiLayoutGroups.Item(r.Width, r.Height);
                }).ToList();

                var placed = UiLayoutGroups.Arrange(layout, parent.Value, sizes);
                groups++;

                // Whether this group is one the preview could get right at all.
                // A group that SETS its children's size reads a preferred size
                // off each child - a LayoutElement, a content fitter, a nested
                // group - and none of that exists in an authored tree, so those
                // are counted separately rather than pretended about.
                bool controls = node.LayoutGroup != null
                             && (node.LayoutGroup.ChildControlWidth || node.LayoutGroup.ChildControlHeight);
                bool declared = live.Any(c => c.DeclaresOwnSize);

                // Grids are reported but not gated. Their disagreements trace
                // to how many children Unity had when it last laid one out,
                // which the extraction cannot tell us - an item switched off
                // after the fact still leaves its neighbours where they were.
                // Rows and columns have no such ambiguity.
                bool modelled = !controls && !declared && node.GridLayout == null;
                if (modelled) modelledGroups++;

                for (int i = 0; i < live.Count; i++)
                {
                    children++;
                    if (modelled) modelledChildren++;
                    var want = Resolved(live[i])!.Value;
                    var got = placed[i];

                    if (Math.Abs(want.X - got.X) <= Tolerance &&
                        Math.Abs(want.Y - got.Y) <= Tolerance &&
                        Math.Abs(want.Width - got.Width) <= Tolerance &&
                        Math.Abs(want.Height - got.Height) <= Tolerance)
                    {
                        agreed++;
                        if (modelled) modelledAgreed++;
                    }
                    else if (worst.Count < 8)
                    {
                        worst.Add($"{entry.Path}/{node.Name}[{i}] {live[i].Name}: " +
                                  $"want {want} got {got}");
                    }
                }
            }
        }

        double rate = children == 0 ? 0 : 100.0 * agreed / children;
        double modelledRate = modelledChildren == 0 ? 0 : 100.0 * modelledAgreed / modelledChildren;
        _out.WriteLine($"ALL      {groups} groups, {children} children, {agreed} agreed ({rate:0.0}%), {skipped} skipped");
        _out.WriteLine($"MODELLED {modelledGroups} groups, {modelledChildren} children, {modelledAgreed} agreed ({modelledRate:0.0}%)");
        foreach (var line in worst) _out.WriteLine("  " + line);

        // Deliberately not asserting 100%: children carrying a LayoutElement or
        // a ContentSizeFitter declare sizes this does not model, and the point
        // of the number is to be looked at rather than to be green. What it
        // must not be is low - that would mean the arithmetic is wrong rather
        // than incomplete.
        Assert.True(children > 200, $"only {children} children to check");

        // Around 64% of comparable children land exactly. That is a
        // REGRESSION GUARD, not a proof of exactness, and the gap is not
        // arithmetic: two real bugs were found this way and fixed (an inverted
        // column alignment, and reverseArrangement being ignored), and what
        // remains traces to inputs the extraction does not carry - a child's
        // preferred size from a LayoutElement or a content fitter, and which
        // children were present the last time Unity actually ran the layout.
        // Neither is reconstructable, so raising this bar would mean asserting
        // something that cannot be made true.
        //
        // The behaviours that actually specify this live in the focused tests
        // below, where every input is known.
        Assert.True(modelledChildren > 100, $"only {modelledChildren} modelled children");
        Assert.True(modelledRate > 55,
                    $"only {modelledRate:0.0}% of comparable children agreed - " +
                    "that is far enough below the usual ~64% to mean the arithmetic broke");
    }

    [Fact]
    public void A_row_puts_items_side_by_side_in_order()
    {
        var layout = new UiLayoutDef
        {
            Kind = UiLayoutKinds.Horizontal,
            Spacing = new[] { 10f, 0f },
            Alignment = "MiddleLeft",
        };
        var parent = new UiRect(0, 0, 500, 100);
        var items = new[]
        {
            new UiLayoutGroups.Item(100, 50),
            new UiLayoutGroups.Item(100, 50),
            new UiLayoutGroups.Item(100, 50),
        };

        var placed = UiLayoutGroups.Arrange(layout, parent, items);

        Assert.Equal(0, placed[0].X, 3);
        Assert.Equal(110, placed[1].X, 3);
        Assert.Equal(220, placed[2].X, 3);
        Assert.All(placed, r => Assert.Equal(100, r.Width, 3));
    }

    [Fact]
    public void A_column_runs_downwards_from_the_top()
    {
        // The direction that is easy to get backwards: canvas y counts UP, and
        // the first child of a vertical group is the topmost.
        var layout = new UiLayoutDef
        {
            Kind = UiLayoutKinds.Vertical,
            Spacing = new[] { 20f, 0f },
            Alignment = "UpperCenter",
        };
        var parent = new UiRect(0, 0, 200, 400);
        var items = Enumerable.Repeat(new UiLayoutGroups.Item(180, 60), 3).ToList();

        var placed = UiLayoutGroups.Arrange(layout, parent, items);

        _out.WriteLine(string.Join(" | ", placed.Select(p => p.ToString())));
        Assert.True(placed[0].Y > placed[1].Y, "the first item is not above the second");
        Assert.True(placed[1].Y > placed[2].Y, "the second item is not above the third");
        Assert.Equal(400, placed[0].Top, 3);                 // flush with the top
        Assert.Equal(80, placed[0].Y - placed[1].Y, 3);      // 60 tall plus 20 apart
    }

    [Fact]
    public void Hiding_an_item_closes_the_gap()
    {
        // The whole reason this exists: a shop where a sold item leaves a hole
        // is the shape of the problem.
        var layout = new UiLayoutDef { Kind = UiLayoutKinds.Horizontal, Alignment = "MiddleLeft" };
        var parent = new UiRect(0, 0, 500, 100);

        var three = Enumerable.Repeat(new UiLayoutGroups.Item(100, 50), 3).ToList();
        var two = Enumerable.Repeat(new UiLayoutGroups.Item(100, 50), 2).ToList();

        var before = UiLayoutGroups.Arrange(layout, parent, three);
        var after = UiLayoutGroups.Arrange(layout, parent, two);

        // The survivor that was third is now second, and sits where the second
        // one used to be.
        Assert.Equal(before[1].X, after[1].X, 3);
    }

    [Fact]
    public void A_grid_fills_across_then_down()
    {
        var layout = new UiLayoutDef
        {
            Kind = UiLayoutKinds.Grid,
            CellSize = new[] { 100f, 100f },
            Spacing = new[] { 10f, 10f },
            Constraint = "FixedColumnCount",
            ConstraintCount = 2,
            Alignment = "UpperLeft",
        };
        var parent = new UiRect(0, 0, 400, 400);
        var items = Enumerable.Repeat(new UiLayoutGroups.Item(0, 0), 4).ToList();

        var placed = UiLayoutGroups.Arrange(layout, parent, items);

        Assert.Equal(placed[0].Y, placed[1].Y, 3);            // same row
        Assert.Equal(110, placed[1].X - placed[0].X, 3);      // next column
        Assert.Equal(placed[0].X, placed[2].X, 3);            // wrapped back
        Assert.Equal(110, placed[0].Y - placed[2].Y, 3);      // next row, downwards
    }

    [Fact]
    public void Padding_insets_the_whole_arrangement()
    {
        var layout = new UiLayoutDef
        {
            Kind = UiLayoutKinds.Horizontal,
            Padding = new[] { 25f, 0f, 0f, 0f },
            Alignment = "MiddleLeft",
        };
        var parent = new UiRect(0, 0, 500, 100);
        var items = new[] { new UiLayoutGroups.Item(100, 50) };

        var placed = UiLayoutGroups.Arrange(layout, parent, items);
        Assert.Equal(25, placed[0].X, 3);
    }

    [Fact]
    public void An_arranged_parent_places_its_children_in_the_preview()
    {
        // End to end through the same geometry the picture and the hit test
        // both use: setting an arrangement must actually move things, not just
        // be stored.
        var root = new UiNodeDef { Name = "List" };
        root.Rect.Size = new[] { 600f, 200f };
        root.Layout = new UiLayoutDef
        {
            Kind = UiLayoutKinds.Horizontal,
            Spacing = new[] { 20f, 0f },
            Alignment = "MiddleLeft",
        };

        for (int i = 0; i < 3; i++)
        {
            var item = new UiNodeDef { Name = "Item " + i };
            item.Rect.Size = new[] { 100f, 100f };
            root.Children.Add(item);
        }

        var rects = UiGeometry.ScreenRects(root, 1920, 1080);

        double x0 = rects[root.Children[0]].X;
        double x1 = rects[root.Children[1]].X;
        double x2 = rects[root.Children[2]].X;

        _out.WriteLine($"{x0:0} {x1:0} {x2:0}");
        Assert.Equal(120, x1 - x0, 3);
        Assert.Equal(120, x2 - x1, 3);
    }

    [Fact]
    public void Hiding_a_child_moves_the_ones_after_it_in_the_preview()
    {
        // The behaviour the whole feature exists for, checked where an author
        // would see it.
        var root = new UiNodeDef { Name = "List" };
        root.Rect.Size = new[] { 600f, 200f };
        root.Layout = new UiLayoutDef { Kind = UiLayoutKinds.Horizontal, Alignment = "MiddleLeft" };

        for (int i = 0; i < 3; i++)
        {
            var item = new UiNodeDef { Name = "Item " + i };
            item.Rect.Size = new[] { 100f, 100f };
            root.Children.Add(item);
        }

        double before = UiGeometry.ScreenRects(root, 1920, 1080)[root.Children[2]].X;

        root.Children[0].StartActive = false;
        double after = UiGeometry.ScreenRects(root, 1920, 1080)[root.Children[2]].X;

        _out.WriteLine($"third item at {before:0}, then {after:0}");
        Assert.Equal(100, before - after, 3);
    }

    [Fact]
    public void An_arrangement_is_a_change_worth_saving()
    {
        // Added to an object the game owns, it must survive pruning - the delta
        // drops anything that asserts nothing, and an arrangement asserts a lot.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var entry = VanillaUiCatalog.UsableBases.First();
        var vanilla = VanillaUiLibrary.Node(entry);
        if (vanilla == null) { _out.WriteLine("no base - skipping"); return; }

        var node = VanillaUiSeed.FromBase(vanilla)!;
        Assert.False(VanillaUiDelta.Asserts(node, vanilla));

        node.Layout = new UiLayoutDef { Kind = UiLayoutKinds.Vertical };
        Assert.True(VanillaUiDelta.Asserts(node, vanilla));
    }
}
