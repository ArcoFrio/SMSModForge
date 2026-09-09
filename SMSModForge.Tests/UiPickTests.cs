using System.Linq;
using System.Windows;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Reaching an object that something bigger is sitting on top of.
/// <para/>
/// Reported: a large object's selection box covers the pane, so nothing else
/// can be clicked, and the box is not even kept inside the preview - it spills
/// out over the tree and the property panel beside it.
/// </summary>
public sealed class UiPickTests
{
    private readonly ITestOutputHelper _out;
    public UiPickTests(ITestOutputHelper output) => _out = output;

    /// <summary>A full-canvas backdrop with one small button on it — the shape
    /// of the complaint.</summary>
    private static (UiNodeDef Root, UiNodeDef Button) BigAndSmall()
    {
        var root = new UiNodeDef { Name = "Backdrop" };
        root.Rect.AnchorMin = new[] { 0f, 0f };
        root.Rect.AnchorMax = new[] { 1f, 1f };
        root.Rect.Size = new[] { 0f, 0f };
        root.Image = new UiImageDef { Sprite = "Semi Rounded", Type = "Sliced" };

        var button = new UiNodeDef { Name = "Button" };
        button.Rect.Size = new[] { 150f, 75f };
        button.Image = new UiImageDef { Sprite = "Semi Rounded", Type = "Sliced" };
        root.Children.Add(button);

        return (root, button);
    }

    [Fact]
    public void A_small_object_wins_over_the_big_one_covering_it()
    {
        var (root, button) = BigAndSmall();

        // Dead centre, where the button is.
        var hit = UiGeometry.Pick(new[] { root }, 960, 540, 1920, 1080);
        Assert.Same(button, hit);

        // Away from it, where only the backdrop is.
        var miss = UiGeometry.Pick(new[] { root }, 100, 100, 1920, 1080);
        Assert.Same(root, miss);
    }

    [Fact]
    public void An_object_that_draws_nothing_does_not_steal_the_click()
    {
        // Containers that fill their parent and show nothing are everywhere in
        // a real tree. Picking one in front of what an author aimed at reads as
        // the click having missed.
        var (root, button) = BigAndSmall();

        var wrapper = new UiNodeDef { Name = "Group" };
        wrapper.Rect.AnchorMin = new[] { 0f, 0f };
        wrapper.Rect.AnchorMax = new[] { 1f, 1f };
        wrapper.Rect.Size = new[] { 0f, 0f };
        root.Children.Add(wrapper);       // after the button, so nearer the front

        var hit = UiGeometry.Pick(new[] { root }, 960, 540, 1920, 1080);
        _out.WriteLine($"picked {hit?.Name}");
        Assert.Same(button, hit);
    }

    [Fact]
    public void A_hidden_object_is_not_pickable()
    {
        var (root, button) = BigAndSmall();
        button.StartActive = false;

        var hit = UiGeometry.Pick(new[] { root }, 960, 540, 1920, 1080);
        Assert.Same(root, hit);
    }

    // A test that the selection box cannot reach outside the preview pane was
    // written here and then removed: it passed with the clipping and without
    // it, because this layout already clips the preview's overflow before it
    // gets anywhere. A test that cannot fail is a false guarantee, and worse
    // than none. The box measurably DOES overflow - 1918px wide inside a 614px
    // pane for a 6000px object - so ClipToBounds stays as the guard; there is
    // just nothing here that proves it earns its keep.

    [Fact]
    public void A_hidden_object_and_everything_under_it_is_off_the_map()
    {
        // A closed panel must not swallow clicks meant for what is behind it.
        // Its children have to go too - they are not switched off themselves,
        // and nothing walks up to ask whether a parent is.
        var root = new UiNodeDef { Name = "Root" };
        root.Rect.AnchorMin = new[] { 0f, 0f };
        root.Rect.AnchorMax = new[] { 1f, 1f };
        root.Rect.Size = new[] { 0f, 0f };

        var closed = new UiNodeDef { Name = "Closed", StartActive = false };
        closed.Children.Add(new UiNodeDef { Name = "Inside" });
        root.Children.Add(closed);

        var map = UiGeometry.ScreenRectsOf(new[] { root }, 1920, 1080);
        var names = map.Select(m => m.Node.Name).ToList();

        _out.WriteLine(string.Join(", ", names));
        Assert.Contains("Root", names);
        Assert.DoesNotContain("Closed", names);
        Assert.DoesNotContain("Inside", names);
    }

    [Fact]
    public void Scale_moves_where_a_thing_is_and_takes_its_children_with_it()
    {
        // Scale is not a size: it multiplies the rectangle an object already
        // has, about its own middle, and carries whatever is inside it along.
        // A label inside a scaled button must not be scaled a second time.
        var root = new UiNodeDef { Name = "Root" };
        root.Rect.AnchorMin = new[] { 0f, 0f };
        root.Rect.AnchorMax = new[] { 1f, 1f };
        root.Rect.Size = new[] { 0f, 0f };

        var button = new UiNodeDef { Name = "Button" };
        button.Rect.Size = new[] { 200f, 100f };
        var label = new UiNodeDef { Name = "Label" };
        label.Rect.Size = new[] { 200f, 100f };
        button.Children.Add(label);
        root.Children.Add(button);

        var before = UiGeometry.ScreenRects(root, 1920, 1080);
        double wasW = before[button].Width, wasLabelW = before[label].Width;

        button.Rect.Scale = new[] { 0.8f, 0.8f };
        var after = UiGeometry.ScreenRects(root, 1920, 1080);

        Assert.Equal(wasW * 0.8, after[button].Width, 3);
        Assert.Equal(wasLabelW * 0.8, after[label].Width, 3);

        // Shrunk about its own middle: the centre has not moved.
        Assert.Equal(before[button].X + before[button].Width / 2,
                     after[button].X + after[button].Width / 2, 3);
    }
}
