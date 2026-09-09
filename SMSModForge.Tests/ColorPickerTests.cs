using System.Linq;
using System.Windows;
using SMSModForge.View;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Choosing a colour, and being able to take it back.
/// <para/>
/// Reported: picking from the gradient did nothing - only the ready-made
/// swatches took - and a colour change could not be undone. The first was the
/// Windows colour dialog and is why there is a picker of our own now; the
/// second was ours, and is what most of these cover.
/// </summary>
public sealed class ColorPickerTests
{
    private readonly ITestOutputHelper _out;
    public ColorPickerTests(ITestOutputHelper o) => _out = o;

    // -- The maths behind the wheel ----------------------------------

    [Theory]
    [InlineData("#FF0000FF")]
    [InlineData("#00FF00FF")]
    [InlineData("#0000FFFF")]
    [InlineData("#7F3C1AFF")]
    [InlineData("#123456FF")]
    [InlineData("#FFFFFFFF")]
    [InlineData("#000000FF")]
    [InlineData("#808080FF")]     // a grey, whose hue is undefined
    public void A_colour_survives_the_trip_through_the_wheel(string hex)
    {
        Assert.True(ColorMath.TryParse(hex, out byte r, out byte g, out byte b, out byte a));

        var (h, s, v) = ColorMath.ToHsv(r, g, b);
        var (r2, g2, b2) = ColorMath.FromHsv(h, s, v);

        _out.WriteLine($"{hex} -> h{h:0.#} s{s:0.###} v{v:0.###} -> {ColorMath.ToHex(r2, g2, b2, a)}");
        Assert.Equal((r, g, b), (r2, g2, b2));
    }

    [Fact]
    public void Alpha_is_carried_rather_than_quietly_made_opaque()
    {
        // The old dialog had no alpha at all. A picker that drops it turns
        // every faded thing solid the first time anyone recolours it.
        Assert.True(ColorMath.TryParse("#3366CC80", out byte r, out byte g, out byte b, out byte a));
        Assert.Equal(0x80, a);
        Assert.Equal("#3366CC80", ColorMath.ToHex(r, g, b, a));
    }

    [Fact]
    public void Six_digits_mean_opaque_and_the_hash_is_optional()
    {
        Assert.True(ColorMath.TryParse("#3366CC", out _, out _, out _, out byte a));
        Assert.Equal(255, a);

        Assert.True(ColorMath.TryParse("3366CC80", out byte r, out _, out _, out byte a2));
        Assert.Equal(0x33, r);
        Assert.Equal(0x80, a2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Something_unreadable_is_refused_rather_than_guessed(string? hex)
        => Assert.False(ColorMath.TryParse(hex, out _, out _, out _, out _));

    /// <summary>The saturation/brightness field maps a position to a colour.
    /// The corners are what an author reaches for, so they are what is
    /// checked.</summary>
    [Fact]
    public void The_corners_of_the_field_are_the_colours_they_look_like()
    {
        const double red = 0;

        Assert.Equal<(byte, byte, byte)>((255, 255, 255), ColorMath.FromHsv(red, 0, 1));  // top left
        Assert.Equal<(byte, byte, byte)>((255, 0, 0), ColorMath.FromHsv(red, 1, 1));      // top right
        Assert.Equal<(byte, byte, byte)>((0, 0, 0), ColorMath.FromHsv(red, 0, 0));        // bottom left
        Assert.Equal<(byte, byte, byte)>((0, 0, 0), ColorMath.FromHsv(red, 1, 0));        // bottom right
    }

    [Fact]
    public void The_hue_bar_runs_all_the_way_round()
    {
        // Both ends are red, and the six primaries land where the gradient
        // paints them.
        Assert.Equal(ColorMath.FromHsv(0, 1, 1), ColorMath.FromHsv(360, 1, 1));
        Assert.Equal<(byte, byte, byte)>((255, 255, 0), ColorMath.FromHsv(60, 1, 1));
        Assert.Equal<(byte, byte, byte)>((0, 255, 0), ColorMath.FromHsv(120, 1, 1));
        Assert.Equal<(byte, byte, byte)>((0, 255, 255), ColorMath.FromHsv(180, 1, 1));
        Assert.Equal<(byte, byte, byte)>((0, 0, 255), ColorMath.FromHsv(240, 1, 1));
        Assert.Equal<(byte, byte, byte)>((255, 0, 255), ColorMath.FromHsv(300, 1, 1));
    }

    // -- Undo --------------------------------------------------------

    [Fact]
    public void A_picked_colour_can_be_undone()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            var node = ui.Nodes[0];
            node.Tint = "#FFFFFFFF";
            WindowHarness.Pump();

            // As if the pack had just been opened, which is when this failed
            // hardest: nothing on the stack for a checkpoint to push.
            vm.Undo.Reset();
            Assert.False(vm.UndoCommand.CanExecute(null));

            vm.EditWithUndo(() => node.Tint = "#FF0000FF");
            WindowHarness.Pump();

            _out.WriteLine($"after picking: {node.Tint}, depth {vm.Undo.Depth}");
            Assert.Equal("#FF0000FF", node.Tint);
            Assert.True(vm.UndoCommand.CanExecute(null), "the colour was not an undo step");

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            string after = vm.Pack.Uis.Last().Nodes[0].Image?.Tint ?? "";
            _out.WriteLine($"after undo: {after}");
            Assert.Equal("#FFFFFFFF", after);
        });
    }

    [Fact]
    public void Assigning_without_it_is_what_used_to_be_lost()
    {
        // The control. Writing the colour straight onto the object - which is
        // what every colour button did - leaves nothing to undo, so this test
        // would pass on the broken code and the one above would not.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.Nodes[0].Tint = "#FFFFFFFF";
            WindowHarness.Pump();
            vm.Undo.Reset();

            ui.Nodes[0].Tint = "#FF0000FF";        // no checkpoint anywhere
            WindowHarness.Pump();

            Assert.False(vm.UndoCommand.CanExecute(null));
        });
    }

    [Fact]
    public void One_pick_is_one_step_not_two()
    {
        // Checkpointing on both edges must not turn a single choice into two
        // presses of Ctrl+Z, which is how a "safe" double checkpoint goes wrong.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.Nodes[0].Tint = "#FFFFFFFF";
            WindowHarness.Pump();
            vm.Undo.Reset();

            vm.EditWithUndo(() => ui.Nodes[0].Tint = "#FF0000FF");
            WindowHarness.Pump();

            _out.WriteLine($"depth after one pick: {vm.Undo.Depth}");
            Assert.Equal(1, vm.Undo.Depth);
        });
    }

    // -- The window itself -------------------------------------------

    /// <summary>Show the picker off screen, laid out, so its bars have a real
    /// size to click within.</summary>
    private static void OnPicker(string seed, System.Action<ColorPickerWindow> body)
    {
        WindowHarness.Run(_ =>
        {
            var w = new ColorPickerWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
                Left = -32000,
                Top = -32000,
            };
            w.Seed(seed);
            try
            {
                w.Show();
                WindowHarness.Pump();
                body(w);
            }
            finally { try { w.Close(); } catch { } }
        });
    }

    [Fact]
    public void Clicking_in_the_gradient_changes_the_colour()
    {
        // The reported bug, in the place it was reported: a press inside the
        // saturation/brightness field, nowhere near a ready-made swatch.
        OnPicker("#FFFFFFFF", w =>
        {
            string before = w.Current;
            w.HueAt(new Point(0, 0));                 // red
            w.FieldAt(new Point(1000, 0));            // fully saturated, full brightness
            _out.WriteLine($"{before} -> {w.Current}");

            Assert.NotEqual(before, w.Current);
            Assert.Equal("#FF0000FF", w.Current);
        });
    }

    [Fact]
    public void The_alpha_bar_is_reachable_at_all()
    {
        // The old dialog had no alpha; this one has to actually move it.
        OnPicker("#FF0000FF", w =>
        {
            w.AlphaAt(new Point(0, 100000));          // the bottom: fully clear
            _out.WriteLine("cleared: " + w.Current);
            Assert.EndsWith("00", w.Current);

            w.AlphaAt(new Point(0, -100000));         // and back to solid
            Assert.EndsWith("FF", w.Current);
        });
    }

    [Fact]
    public void The_picker_opens_on_the_colour_it_was_given()
    {
        // Opening it and pressing Choose must not change anything.
        OnPicker("#3366CC80", w => Assert.Equal("#3366CC80", w.Current));
    }
}
