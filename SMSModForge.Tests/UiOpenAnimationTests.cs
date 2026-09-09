using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// How a screen, or one object inside it, arrives when it is switched on.
/// <para/>
/// The numbers come from the running game rather than from taste: the shop
/// snaps to alpha 0 and scale (1,0,1), then takes both to normal over 0.3s on
/// QuadInOut. The same dump is why this is opt-in - of 29 on-enable triggers,
/// five fade, seven grow and exactly one does both, over four different
/// durations and three different curves. There is no house style to inherit.
/// </summary>
public sealed class UiOpenAnimationTests
{
    private readonly ITestOutputHelper _out;
    public UiOpenAnimationTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Nothing_animates_unless_it_says_so()
    {
        var pack = new ModPack { PackId = "p" };
        var ui = new UiDef { Id = "screen" };
        ui.Nodes.Add(new UiNodeDef { Name = "Panel" });
        pack.Uis.Add(ui);

        Assert.Null(ui.Open);
        Assert.Null(ui.Nodes[0].Open);

        string saved = PackRepository.SerializeAsSaved(pack);
        Assert.DoesNotContain("\"open\"", saved);
    }

    [Fact]
    public void The_shops_own_animation_round_trips()
    {
        var ui = new UiDef { Id = "shop", Open = UiOpenDef.LikeTheGame() };
        string saved = PackRepository.SerializeAsSaved(new ModPack { PackId = "p", Uis = { ui } });
        _out.WriteLine(saved);

        var back = PackRepository.Deserialize(saved)!.Uis.Single().Open;
        Assert.NotNull(back);
        Assert.True(back!.Fade);
        Assert.Equal(new[] { 1f, 0f, 1f }, back.ScaleFrom);
        Assert.Equal(0.3f, back.Duration);
        Assert.Equal("QuadInOut", back.Easing);
    }

    [Fact]
    public void An_object_inside_a_screen_can_have_its_own()
    {
        // The same moment one level down: a panel a condition switches on is
        // arriving exactly as a screen does.
        var ui = new UiDef { Id = "screen" };
        var root = new UiNodeDef { Name = "Panel" };
        root.Children.Add(new UiNodeDef { Name = "Tab", Open = UiOpenDef.LikeTheGame() });
        ui.Nodes.Add(root);

        string saved = PackRepository.SerializeAsSaved(new ModPack { PackId = "p", Uis = { ui } });
        var back = PackRepository.Deserialize(saved)!.Uis.Single();

        Assert.Null(back.Open);                                   // the screen did not ask
        Assert.NotNull(back.Nodes[0].Children[0].Open);           // the object did
    }

    [Fact]
    public void An_animation_that_does_nothing_is_not_written_down()
    {
        // Neither a fade nor a scale is a duration spent changing nothing.
        var ui = new UiDef { Id = "screen", Open = new UiOpenDef { Fade = false, ScaleFrom = null } };
        Assert.False(ui.Open!.DoesAnything);

        string saved = PackRepository.SerializeAsSaved(new ModPack { PackId = "p", Uis = { ui } });
        Assert.DoesNotContain("\"open\"", saved);
    }

    // -- The controls ------------------------------------------------

    [Fact]
    public void Turning_it_on_starts_from_the_animation_people_have_seen()
    {
        // An empty animation that does nothing would be a worse starting point
        // than the one this game demonstrates.
        var row = new UiViewModel(new UiDef { Id = "screen" });
        Assert.False(row.Open.Animates);

        row.Open.Animates = true;

        Assert.True(row.Open.Fade);
        Assert.True(row.Open.Grows);
        Assert.Equal(0.3, row.Open.Duration, 3);
        Assert.Equal("QuadInOut", row.Open.Easing);
        Assert.Equal(0, row.Open.ScaleFromY, 3);          // flat, and unfolding
        _out.WriteLine(row.Open.Summary);
    }

    [Fact]
    public void Turning_it_off_leaves_nothing_behind()
    {
        var def = new UiDef { Id = "screen" };
        var row = new UiViewModel(def);

        row.Open.Animates = true;
        Assert.NotNull(def.Open);

        row.Open.Animates = false;
        Assert.Null(def.Open);
    }

    [Fact]
    public void A_fade_on_its_own_leaves_the_size_alone()
    {
        var def = new UiDef { Id = "screen" };
        var row = new UiViewModel(def);
        row.Open.Animates = true;
        row.Open.Grows = false;

        Assert.True(def.Open!.Fade);
        Assert.Null(def.Open.ScaleFrom);
        Assert.True(def.Open.DoesAnything);
        _out.WriteLine(row.Open.Summary);
    }

    [Fact]
    public void The_start_scale_is_three_numbers_because_the_game_uses_all_of_them()
    {
        // (1,0,1) unfolds, (0.5,0.5,0.5) grows from small, (2,2,1) shrinks into
        // place - all of those are in the game, so one number would not do.
        var def = new UiDef { Id = "screen" };
        var row = new UiViewModel(def);
        row.Open.Animates = true;

        row.Open.ScaleFromX = 2;
        row.Open.ScaleFromY = 2;
        row.Open.ScaleFromZ = 1;

        Assert.Equal(new[] { 2f, 2f, 1f }, def.Open!.ScaleFrom);
    }

    [Fact]
    public void Only_the_curves_the_game_can_be_pointed_at_are_offered()
    {
        // An easing nobody can find in the game is a guess dressed as a choice.
        Assert.Equal(new[] { "Linear", "QuadInOut", "BounceOut", "ElasticOut" },
                     UiOpenDef.Easings);
    }

    // -- Leaving ------------------------------------------------------

    [Fact]
    public void The_shops_way_out_is_not_the_mirror_of_its_way_in()
    {
        // Read from the close button: it folds to a flat line and does NOT
        // fade, while the arrival does both. Assuming symmetry would have got
        // this wrong.
        var opening = UiOpenDef.LikeTheGame();
        var closing = UiOpenDef.LikeTheGameClosing();

        Assert.True(opening.Fade);
        Assert.False(closing.Fade);
        Assert.Equal(new[] { 1f, 0f, 1f }, closing.ScaleFrom);
        Assert.Equal(0.3f, closing.Duration);
        Assert.Equal("QuadInOut", closing.Easing);
    }

    [Fact]
    public void A_closing_round_trips_on_its_own()
    {
        var ui = new UiDef { Id = "shop", Close = UiOpenDef.LikeTheGameClosing() };
        string saved = PackRepository.SerializeAsSaved(new ModPack { PackId = "p", Uis = { ui } });

        Assert.Contains("close", saved);
        Assert.DoesNotContain("\"open\"", saved);      // it asked for one end only

        var back = PackRepository.Deserialize(saved)!.Uis.Single();
        Assert.Null(back.Open);
        Assert.NotNull(back.Close);
        Assert.False(back.Close!.Fade);
    }

    [Fact]
    public void The_two_ends_are_kept_apart()
    {
        var def = new UiDef { Id = "s" };
        var row = new UiViewModel(def);

        row.Open.Animates = true;
        Assert.NotNull(def.Open);
        Assert.Null(def.Close);

        row.Close.Animates = true;
        Assert.NotNull(def.Close);

        // Each starts from what the game does at THAT end.
        Assert.True(def.Open!.Fade);
        Assert.False(def.Close!.Fade);

        row.Open.Animates = false;
        Assert.Null(def.Open);
        Assert.NotNull(def.Close);      // turning one off leaves the other
    }

    [Fact]
    public void The_wording_follows_which_end_it_is()
    {
        var def = new UiDef { Id = "s" };
        var row = new UiViewModel(def);

        Assert.False(row.Open.Closing);
        Assert.True(row.Close.Closing);

        Assert.Equal("appears at once", row.Open.Summary);
        Assert.Equal("vanishes at once", row.Close.Summary);

        row.Close.Animates = true;
        _out.WriteLine(row.Close.Summary);
        Assert.Contains("shrinks", row.Close.Summary);
    }

    [Fact]
    public void An_object_can_have_one_at_each_end()
    {
        var node = new UiNodeDef { Name = "Panel" };
        var row = new UiNodeViewModel(node, null);

        row.Open.Animates = true;
        row.Close.Animates = true;

        Assert.NotNull(node.Open);
        Assert.NotNull(node.Close);
        Assert.True(node.Open!.Fade);
        Assert.False(node.Close!.Fade);
    }
}
