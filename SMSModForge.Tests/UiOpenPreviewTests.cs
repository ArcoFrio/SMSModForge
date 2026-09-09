using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Playing an arrival animation in the preview, and the promise that comes with
/// it: the screen is left exactly as it was.
/// <para/>
/// The promise is kept by never changing the model outside a single render. The
/// frame is put on, the picture is drawn, and it comes off again before
/// anything else can run - so a save landing mid-play writes the screen as
/// authored rather than frozen half-open. These tests check the property that
/// makes that true, not that a cleanup step was remembered.
/// </summary>
public sealed class UiOpenPreviewTests
{
    private readonly ITestOutputHelper _out;
    public UiOpenPreviewTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void A_new_screen_animates_without_being_asked()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();

            _out.WriteLine(ui.Open.Summary);
            Assert.True(ui.Open.Animates);
            Assert.True(ui.Open.Fade);
            Assert.True(ui.Open.Grows);
        });
    }

    [Fact]
    public void A_screen_the_game_owns_is_left_alone()
    {
        // Its arrival is the game's business, and the game already decided.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddVanillaUiCommand.Execute(null);
            WindowHarness.Pump();

            Assert.Null(vm.Uis.Last().Model.Open);
        });
    }

    [Fact]
    public void A_pack_written_before_this_does_not_start_animating()
    {
        // The default belongs to MAKING a screen, not to the format. An absent
        // "open" still means none, or every screen in every existing pack would
        // start animating under its author the day they update.
        var back = PackRepository.Deserialize(
            "{\"packId\":\"p\",\"uis\":[{\"id\":\"s\",\"name\":\"S\"}]}");

        Assert.NotNull(back);
        Assert.Null(back!.Uis.Single().Open);
    }

    [Fact]
    public void Playing_it_leaves_the_pack_byte_for_byte_as_it_was()
    {
        // The promise, checked the only way that means anything: the whole pack
        // serialised before and after, including part-way through.
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.SelectedNode = ui.Nodes[0];
            WindowHarness.Pump();

            var preview = (UiPreview)window.FindName("UiScreenPreview");
            Assert.NotNull(preview);

            var target = ui.Nodes[0].Model;
            string before = PackRepository.Serialize(vm.Pack);

            // Frames, as the player hands them over: part-way, then nearly done.
            foreach (double k in new[] { 0.0, 0.25, 0.5, 0.9 })
            {
                preview.AnimationFrame = new UiAnimationFrame(target, k, new[] { 1f, (float)k });
                WindowHarness.Pump();

                // Mid-play, not only at the end: this is the frame a save would
                // have caught under the other design.
                Assert.Equal(before, PackRepository.Serialize(vm.Pack));
            }

            preview.AnimationFrame = null;
            WindowHarness.Pump();

            _out.WriteLine("unchanged through " + 4 + " frames");
            Assert.Equal(before, PackRepository.Serialize(vm.Pack));
        });
    }

    [Fact]
    public void The_object_being_animated_keeps_its_own_values()
    {
        // The narrow version of the same thing, on the two fields a frame
        // actually touches - so a failure says which one leaked.
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            WindowHarness.Pump();

            var target = ui.Nodes[0].Model;
            target.Alpha = 0.75f;                       // deliberately not 1
            target.Rect.Scale = new[] { 1.5f, 1.5f };   // and not 1 either
            WindowHarness.Pump();

            var preview = (UiPreview)window.FindName("UiScreenPreview");
            preview.AnimationFrame = new UiAnimationFrame(target, 0.1, new[] { 1f, 0f });
            WindowHarness.Pump();

            _out.WriteLine($"alpha {target.Alpha}, scale [{target.Rect.Scale[0]}, {target.Rect.Scale[1]}]");
            Assert.Equal(0.75f, target.Alpha);
            Assert.Equal(new[] { 1.5f, 1.5f }, target.Rect.Scale);
        });
    }

    [Fact]
    public void The_curve_starts_where_it_should_and_ends_where_it_should()
    {
        // Whatever happens between, every curve has to begin at nothing and
        // arrive exactly - an animation that ends at 0.98 leaves a screen
        // permanently slightly wrong.
        foreach (var easing in UiOpenDef.Easings)
        {
            Assert.Equal(0, UiOpenDef.Ease(0, easing), 6);
            Assert.Equal(1, UiOpenDef.Ease(1, easing), 6);
            _out.WriteLine($"{easing}: mid {UiOpenDef.Ease(0.5, easing):0.###}");
        }
    }

    // -- What made it laggy ------------------------------------------

    [Fact]
    public void One_frame_of_animation_costs_one_drawing_of_the_screen()
    {
        // Reported as lag. Every property that changes redraws the whole
        // 1920x1080 composite, and a frame used to arrive as three properties -
        // so one moment of animation cost three drawings, at some 57ms each,
        // while frames were being asked for every 16ms.
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            WindowHarness.Pump();

            var preview = (UiPreview)window.FindName("UiScreenPreview");
            var target = ui.Nodes[0].Model;

            int before = preview.RendersDone;
            preview.AnimationFrame = new UiAnimationFrame(target, 0.5, new[] { 1f, 0.5f });
            WindowHarness.Pump();

            int cost = preview.RendersDone - before;
            _out.WriteLine(cost + " draw(s) for one frame");
            Assert.Equal(1, cost);
        });
    }

    [Fact]
    public void Frames_that_arrive_faster_than_they_can_be_drawn_collapse()
    {
        // The other half of the fix. Drawing each frame where it lands means a
        // queue that only grows; asking for one instead means a slow machine
        // plays the same animation with FEWER frames rather than the same
        // frames running late - coarse instead of laggy.
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            WindowHarness.Pump();

            var preview = (UiPreview)window.FindName("UiScreenPreview");
            var target = ui.Nodes[0].Model;

            int before = preview.RendersDone;
            for (int i = 0; i < 20; i++)          // twenty frames, no chance to draw
                preview.AnimationFrame = new UiAnimationFrame(target, i / 20.0, new[] { 1f, i / 20f });
            WindowHarness.Pump();

            int cost = preview.RendersDone - before;
            _out.WriteLine(cost + " draw(s) for 20 frames handed over at once");
            Assert.Equal(1, cost);
        });
    }
}
