using System;
using System.Diagnostics;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Seeding: opening a vanilla screen gives you the screen, editable, rather
/// than a blank panel and instructions.
/// <para/>
/// The pair to <see cref="VanillaUiDeltaTests"/> — this makes the tree big so
/// the author can work, that makes the manifest small so the pack stays a
/// delta. Neither is right without the other, so the round trip is what these
/// mostly check.
/// </summary>
public class VanillaUiSeedTests
{
    private readonly ITestOutputHelper _out;
    public VanillaUiSeedTests(ITestOutputHelper o) => _out = o;

    private static VanillaUiSurface.Node Sample() => new()
    {
        Name = "Panel",
        ActiveSelf = true,
        Rect = new VanillaUiSurface.Rect
        {
            AnchorMin = new[] { 0f, 1f },
            AnchorMax = new[] { 1f, 1f },
            Pivot = new[] { 0.5f, 1f },
            AnchoredPosition = new[] { 0f, -30f },
            SizeDelta = new[] { -40f, 90f },
            LocalScale = new[] { 1f, 1f, 1f },
            LocalEuler = new[] { 0f, 0f, 12f },
        },
        Image = new VanillaUiSurface.Image
        {
            Sprite = "Semi Rounded", Type = "Sliced", Color = "#302F46FF", FillCenter = true,
        },
        Shadow = new VanillaUiSurface.Effect { Color = "#000000AA", Distance = new[] { 2f, -2f } },
        CanvasGroup = new VanillaUiSurface.Group { Alpha = 0.5f },
        Children =
        {
            new VanillaUiSurface.Node
            {
                Name = "Label", SiblingIndex = 0, ActiveSelf = false,
                Text = new VanillaUiSurface.Text
                {
                    Kind = "TextMeshProUGUI", Value = "Cash", Font = "Barton SDF",
                    FontSize = "42", Color = "#FFFFFFFF", Alignment = "Left",
                    WordWrapping = "False",
                },
            },
            new VanillaUiSurface.Node { Name = "Icon", SiblingIndex = 1 },
            new VanillaUiSurface.Node { Name = "Icon", SiblingIndex = 2 },
        },
    };

    [Fact]
    public void The_whole_screen_comes_across_with_the_games_own_values()
    {
        var seeded = VanillaUiSeed.FromBase(Sample());

        Assert.Equal("Panel", seeded.Name);
        Assert.Equal(".", seeded.Bind);
        Assert.True(seeded.IsBound);

        Assert.Equal(new[] { 0f, 1f }, seeded.Rect.AnchorMin);
        Assert.Equal(new[] { 1f, 1f }, seeded.Rect.AnchorMax);
        Assert.Equal(new[] { 0f, -30f }, seeded.Rect.Position);
        Assert.Equal(new[] { -40f, 90f }, seeded.Rect.Size);
        Assert.Equal(12f, seeded.Rect.RotationZ);

        Assert.Equal("Semi Rounded", seeded.Image!.Sprite);
        Assert.Equal("Sliced", seeded.Image.Type);
        Assert.Equal("#302F46FF", seeded.Image.Tint);

        Assert.Equal(0.5f, seeded.Alpha);
        Assert.Equal("#000000AA", seeded.Shadow!.Color);

        var label = seeded.Children[0];
        Assert.Equal("Cash", label.Text!.Value);
        Assert.Equal("Barton SDF", label.Text.Font);
        Assert.Equal(42f, label.Text.Size);
        Assert.False(label.Text.Wrap);         // the game says WordWrapping False
        Assert.False(label.StartActive);       // and this one is switched off
    }

    [Fact]
    public void Children_are_bound_by_their_path_and_kept_in_draw_order()
    {
        var seeded = VanillaUiSeed.FromBase(Sample());

        Assert.Equal(3, seeded.Children.Count);
        Assert.Equal("Label", seeded.Children[0].Bind);

        // Two siblings called Icon, so both are suffixed - the one that
        // collides, not just the second.
        Assert.Equal("Icon#1", seeded.Children[1].Bind);
        Assert.Equal("Icon#2", seeded.Children[2].Bind);

        // And a name that does not collide stays plain, so a manifest reads
        // like the scene rather than like a database.
        Assert.DoesNotContain("#", seeded.Children[0].Bind);
    }

    [Fact]
    public void Every_seeded_path_finds_its_way_back_to_the_object_it_names()
    {
        // The two halves have to agree or an extension silently rebases onto
        // the wrong object.
        var vanilla = Sample();
        var seeded = VanillaUiSeed.FromBase(vanilla);

        foreach (string path in VanillaUiSeed.Paths(seeded))
        {
            var found = VanillaUiDelta.NodeAt(vanilla, path);
            Assert.True(found != null, $"seeded path '{path}' resolves to nothing");
        }

        Assert.Equal("Icon", VanillaUiDelta.NodeAt(vanilla, "Icon#2")!.Name);
        Assert.Equal(2, VanillaUiDelta.NodeAt(vanilla, "Icon#2")!.SiblingIndex);
    }

    [Fact]
    public void A_freshly_seeded_tree_prunes_away_to_nothing()
    {
        // The round trip that matters: open a screen, change nothing, save, and
        // the pack is unchanged. If seeding and comparison disagree anywhere,
        // this is what catches it.
        var vanilla = Sample();
        var pack = new ModPack { PackId = "test" };
        pack.Uis.Add(new UiDef
        {
            Source = "vanillaui:X/Panel",
            Nodes = { VanillaUiSeed.FromBase(vanilla) },
        });

        var restore = VanillaUiDelta.PrepareForSave(
            pack, (_, path) => VanillaUiDelta.NodeAt(vanilla, path));
        try
        {
            Assert.Empty(pack.Uis[0].Nodes);
        }
        finally { restore(); }
    }

    [Fact]
    public void One_edit_survives_the_round_trip_and_nothing_else_does()
    {
        var vanilla = Sample();
        var seeded = VanillaUiSeed.FromBase(vanilla);
        seeded.Children[0].Text!.Value = "Money";

        var pack = new ModPack { PackId = "test" };
        pack.Uis.Add(new UiDef
        {
            Source = "vanillaui:X/Panel",
            Nodes = { seeded },
        });

        var restore = VanillaUiDelta.PrepareForSave(
            pack, (_, path) => VanillaUiDelta.NodeAt(vanilla, path));
        try
        {
            var panel = Assert.Single(pack.Uis[0].Nodes);
            var label = Assert.Single(panel.Children);
            Assert.Equal("Label", label.Name);
            Assert.True(label.OverrideText);
            Assert.Equal("Money", label.Text!.Value);

            // The two Icons changed nothing and are gone.
            Assert.DoesNotContain(panel.Children, c => c.Name == "Icon");
        }
        finally { restore(); }
    }

    [Fact]
    public void An_object_with_no_picture_and_no_words_seeds_neither()
    {
        // Container objects are most of a UI tree. Giving them an empty Image
        // would make every one of them look like an assertion at save time.
        var seeded = VanillaUiSeed.FromBase(Sample());
        Assert.Null(seeded.Children[1].Image);
        Assert.Null(seeded.Children[1].Text);
        Assert.Null(seeded.Children[1].Alpha);
    }

    // ── On the real thing ────────────────────────────────────────────

    [Fact]
    public void The_biggest_vanilla_screen_seeds_in_reasonable_time()
    {
        // Starmaker is 1433 objects. Seeding is eager, like vanilla place
        // extensions, and that is only acceptable if it is quick - an author
        // clicking a screen should not wait for it.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var surface = VanillaUiLibrary.Surface("9_MainCanvas");
        if (surface == null) { _out.WriteLine("no main canvas - skipping"); return; }
        var starmaker = surface.Base("Starmaker");
        Assert.NotNull(starmaker);

        var clock = Stopwatch.StartNew();
        var seeded = VanillaUiSeed.FromBase(starmaker!);
        clock.Stop();

        int nodes = Count(seeded);
        _out.WriteLine($"Starmaker: {nodes} nodes seeded in {clock.ElapsedMilliseconds} ms");
        Assert.True(nodes > 1000, "the whole screen, not a summary of it");
        Assert.True(clock.ElapsedMilliseconds < 2000,
                    $"seeding took {clock.ElapsedMilliseconds} ms, which an author would feel");

        static int Count(UiNodeDef n) => 1 + n.Children.Sum(Count);
    }

    [Fact]
    public void A_real_screen_seeds_and_prunes_back_to_nothing()
    {
        // The same round trip as above, against the game rather than a fixture.
        // This is where seeding and comparison would disagree if any real field
        // round-trips differently from the ones in the sample.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var surface = VanillaUiLibrary.Surface("9_MainCanvas");
        var quit = surface?.Base("Quitagme");
        if (quit == null) { _out.WriteLine("no Quitagme - skipping"); return; }

        var pack = new ModPack { PackId = "test" };
        pack.Uis.Add(new UiDef
        {
            Source = "vanillaui:9_MainCanvas/Quitagme",
            // With the resolver, as the editor seeds. Nine sprite names in the
            // game belong to more than one crop, so a bare name is not the same
            // name the delta compares against - and four objects on this screen
            // would read as edited for ever.
            Nodes = { VanillaUiSeed.FromBase(quit, VanillaUiLibrary.Assets.NameForKey) },
        });

        int before = pack.Uis[0].Nodes.Sum(CountDef);
        var restore = VanillaUiDelta.PrepareForSave(
            pack, (_, path) => VanillaUiDelta.NodeAt(quit, path));
        try
        {
            int after = pack.Uis[0].Nodes.Sum(CountDef);
            _out.WriteLine($"Quitagme: {before} nodes seeded, {after} stored");
            Assert.Equal(0, after);
        }
        finally { restore(); }

        static int CountDef(UiNodeDef n) => 1 + n.Children.Sum(CountDef);
    }
}
