using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The anchor arithmetic, checked against the game's own answers.
/// <para/>
/// The fixture holds a hundred real objects out of the vanilla UI: what their
/// anchors were authored as, and the rectangle Unity resolved them to. So this
/// is not a test of whether the formula matches my understanding of Unity — it
/// is a test of whether it matches Unity.
/// </summary>
public class UiLayoutTests
{
    private readonly ITestOutputHelper _out;
    public UiLayoutTests(ITestOutputHelper o) => _out = o;

    private sealed class Case
    {
        public string Where { get; set; } = "";
        public string Kind { get; set; } = "";
        public Parent Parent { get; set; } = new();
        public double[] AnchorMin { get; set; } = Array.Empty<double>();
        public double[] AnchorMax { get; set; } = Array.Empty<double>();
        public double[] Pivot { get; set; } = Array.Empty<double>();
        public double[] AnchoredPosition { get; set; } = Array.Empty<double>();
        public double[] SizeDelta { get; set; } = Array.Empty<double>();
        public double[] ExpectedMin { get; set; } = Array.Empty<double>();
        public double[] ExpectedSize { get; set; } = Array.Empty<double>();
    }

    private sealed class Parent
    {
        public double[] Min { get; set; } = Array.Empty<double>();
        public double[] Size { get; set; } = Array.Empty<double>();
    }

    private sealed class Fixture
    {
        public List<Case> Cases { get; set; } = new();
    }

    private static List<Case> Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null &&
               !File.Exists(Path.Combine(dir.FullName, "Fixtures", "ui_layout_baseline.json")))
            dir = dir.Parent;
        Assert.True(dir != null,
            "ui_layout_baseline.json is not above " + AppContext.BaseDirectory);

        string json = File.ReadAllText(
            Path.Combine(dir!.FullName, "Fixtures", "ui_layout_baseline.json"));
        var fixture = JsonConvert.DeserializeObject<Fixture>(json);
        Assert.NotNull(fixture);
        Assert.NotEmpty(fixture!.Cases);
        return fixture.Cases;
    }

    private static UiRect ParentRect(Case c)
        => new(c.Parent.Min[0], c.Parent.Min[1], c.Parent.Size[0], c.Parent.Size[1]);

    // ── Against the game ─────────────────────────────────────────────

    [Fact]
    public void Every_real_rectangle_in_the_game_resolves_to_where_the_game_put_it()
    {
        var cases = Load();
        var wrong = new List<string>();

        foreach (var c in cases)
        {
            var got = UiLayout.Resolve(ParentRect(c), c.AnchorMin, c.AnchorMax,
                                       c.Pivot, c.AnchoredPosition, c.SizeDelta);

            // A tenth of a pixel. The extraction rounds to five decimals and
            // the values arrived through a matrix multiply, so demanding
            // exactness would fail on arithmetic rather than on being wrong.
            const double tolerance = 0.1;
            if (Math.Abs(got.X - c.ExpectedMin[0]) > tolerance ||
                Math.Abs(got.Y - c.ExpectedMin[1]) > tolerance ||
                Math.Abs(got.Width - c.ExpectedSize[0]) > tolerance ||
                Math.Abs(got.Height - c.ExpectedSize[1]) > tolerance)
            {
                wrong.Add($"{c.Where} [{c.Kind}]\n" +
                          $"     expected ({c.ExpectedMin[0]:0.##},{c.ExpectedMin[1]:0.##}) " +
                          $"{c.ExpectedSize[0]:0.##}×{c.ExpectedSize[1]:0.##}\n" +
                          $"     got      {got}");
            }
        }

        _out.WriteLine($"{cases.Count} real rectangles checked, {wrong.Count} wrong");
        Assert.True(wrong.Count == 0,
            "resolved differently from the game:\n  " + string.Join("\n  ", wrong.Take(8)));
    }

    [Fact]
    public void The_fixture_covers_every_way_the_game_anchors_things()
    {
        // A hundred cases that were all the same shape would prove one branch.
        var kinds = Load().Select(c => c.Kind).Distinct().OrderBy(k => k).ToList();
        _out.WriteLine(string.Join("\n", kinds));

        Assert.Contains(kinds, k => k.StartsWith("stretch:--"));  // pinned to a point
        Assert.Contains(kinds, k => k.StartsWith("stretch:xy"));  // fills its parent
        Assert.Contains(kinds, k => k.StartsWith("stretch:x-"));  // one axis only
        Assert.Contains(kinds, k => k.EndsWith("pivot:offset"));  // pivot off centre
        Assert.True(kinds.Count >= 5, "the sample should span the real variety");
    }

    // ── The rules the formula encodes, stated on their own ───────────

    [Fact]
    public void A_point_anchor_makes_the_size_delta_a_literal_size()
    {
        var parent = UiRect.FromCanvas(1920, 1080);
        var r = UiLayout.Resolve(parent,
            0.5, 0.5, 0.5, 0.5,      // pinned to the middle
            0.5, 0.5,                // centred pivot
            0, 0,
            200, 50);

        Assert.Equal(200, r.Width, 6);
        Assert.Equal(50, r.Height, 6);
        Assert.Equal(-100, r.X, 6);
        Assert.Equal(-25, r.Y, 6);
    }

    [Fact]
    public void A_stretched_anchor_makes_the_size_delta_an_inset()
    {
        var parent = UiRect.FromCanvas(1920, 1080);
        var full = UiLayout.Resolve(parent, 0, 0, 1, 1, 0.5, 0.5, 0, 0, 0, 0);
        Assert.Equal(1920, full.Width, 6);
        Assert.Equal(1080, full.Height, 6);

        // Negative delta insets from the parent's edges - the same numbers that
        // would have meant "200 wide" under a point anchor.
        var inset = UiLayout.Resolve(parent, 0, 0, 1, 1, 0.5, 0.5, 0, 0, -40, -20);
        Assert.Equal(1880, inset.Width, 6);
        Assert.Equal(1060, inset.Height, 6);
        Assert.Equal(-940, inset.X, 6);
    }

    [Fact]
    public void The_pivot_is_a_fraction_of_the_anchors_not_of_the_parent()
    {
        // The error that agrees for point anchors and is wrong everywhere else,
        // which is what makes it survive casual testing.
        var parent = UiRect.FromCanvas(1000, 1000);

        // Anchored across the right half, pivot on its left edge, no offset.
        var r = UiLayout.Resolve(parent, 0.5, 0, 1, 1, 0, 0.5, 0, 0, 0, 0);

        // The anchor rectangle spans x 0..500 in canvas terms, so its left edge
        // is at 0. Taking the pivot against the PARENT would have put it at
        // -500, half a screen out.
        Assert.Equal(0, r.X, 6);
        Assert.Equal(500, r.Width, 6);
    }

    [Fact]
    public void An_off_centre_pivot_moves_the_rectangle_not_its_size()
    {
        var parent = UiRect.FromCanvas(1920, 1080);
        var centred = UiLayout.Resolve(parent, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 100, 40);
        var corner = UiLayout.Resolve(parent, 0.5, 0.5, 0.5, 0.5, 0, 0, 0, 0, 100, 40);

        Assert.Equal(centred.Width, corner.Width, 6);
        Assert.Equal(centred.Height, corner.Height, 6);
        Assert.Equal(centred.X + 50, corner.X, 6);
        Assert.Equal(centred.Y + 20, corner.Y, 6);
    }

    // ── The flip ─────────────────────────────────────────────────────

    [Fact]
    public void Canvas_coordinates_become_screen_coordinates_exactly_once()
    {
        // Top-left of a 1920×1080 canvas is (-960, 540) with y up, and (0, 0)
        // with y down. A flip applied twice looks nearly right, because UI is
        // mostly symmetric about the middle - so it needs a test that is not.
        var topLeft = new UiRect(-960, 440, 100, 100);
        var screen = UiLayout.ToScreen(topLeft, 1920, 1080);

        Assert.Equal(0, screen.X, 6);
        Assert.Equal(0, screen.Y, 6);

        var bottomRight = new UiRect(860, -540, 100, 100);
        var s2 = UiLayout.ToScreen(bottomRight, 1920, 1080);
        Assert.Equal(1820, s2.X, 6);
        Assert.Equal(980, s2.Y, 6);

        // Applying it twice must NOT be a no-op, or the test above proves
        // nothing about direction.
        var twice = UiLayout.ToScreen(screen, 1920, 1080);
        Assert.NotEqual(screen.Y, twice.Y, 6);
    }

    [Fact]
    public void A_canvas_rectangle_is_centred_on_its_own_origin()
    {
        var canvas = UiRect.FromCanvas(1920, 1080);
        Assert.Equal(-960, canvas.X, 6);
        Assert.Equal(-540, canvas.Y, 6);
        Assert.Equal(960, canvas.Right, 6);
        Assert.Equal(540, canvas.Top, 6);
    }
}
