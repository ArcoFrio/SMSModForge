using System;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Drawing what the pack will produce, rather than what the game currently has.
/// <para/>
/// The important test here is not that it draws something — it is that an
/// UNTOUCHED seed draws the same pixels as the extraction does. Any difference
/// is information the seed dropped on the way across, and it would show up as
/// an author's screen quietly not matching the game before they changed
/// anything.
/// </summary>
public class UiAuthoredRenderTests
{
    private readonly ITestOutputHelper _out;
    public UiAuthoredRenderTests(ITestOutputHelper o) => _out = o;

    private (VanillaUiSurface Surface, VanillaUiSurface.Node Node)? Screen(string name)
    {
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return null; }
        var surface = VanillaUiLibrary.Surface("9_MainCanvas");
        var node = surface?.Base(name);
        if (surface == null || node == null) { _out.WriteLine("no " + name); return null; }
        return (surface, node);
    }

    private static (int Differing, int Lit) Compare(byte[] a, byte[] b)
    {
        int differing = 0, lit = 0;
        for (int i = 0; i + 3 < a.Length; i += 4)
        {
            if (a[i + 3] > 0 || b[i + 3] > 0) lit++;
            for (int c = 0; c < 4; c++)
                if (Math.Abs(a[i + c] - b[i + c]) > 1) { differing++; break; }
        }
        return (differing, lit);
    }

    [Theory]
    [InlineData("Quitagme")]
    [InlineData("Payout")]
    [InlineData("Tooltip_Finances")]
    public void An_untouched_seed_draws_the_same_screen_the_game_does(string name)
    {
        var opened = Screen(name);
        if (opened == null) return;
        var (surface, node) = opened.Value;

        var assets = VanillaUiLibrary.Assets;
        var fromGame = UiSceneRenderer.RenderBase(surface, node, assets);
        // Seeded the way the editor does it, with the resolver that turns a
        // sprite key into the name identifying that exact crop. Without it the
        // nine shared names pick whichever crop comes first, which is precisely
        // the bug this comparison caught.
        var seeded = VanillaUiSeed.FromBase(node, assets.NameForKey);
        var fromPack = UiAuthoredRenderer.Render(seeded, surface.Width, surface.Height, assets);

        Assert.Equal(fromGame.Length, fromPack.Length);
        var (differing, lit) = Compare(fromGame, fromPack);

        _out.WriteLine($"{name}: {lit} lit pixels, {differing} differing " +
                       $"({(lit == 0 ? 0 : 100.0 * differing / lit):0.##}%)");

        if (differing > 0)
        {
            // Where, not just how many. A count says the seed lost something;
            // a bounding box says which object.
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            int w = (int)Math.Round(surface.Width);
            for (int i = 0, px = 0; i + 3 < fromGame.Length; i += 4, px++)
            {
                bool same = true;
                for (int c = 0; c < 4; c++)
                    if (Math.Abs(fromGame[i + c] - fromPack[i + c]) > 1) { same = false; break; }
                if (same) continue;
                int x = px % w, y = px / w;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            _out.WriteLine($"   differing box: x {minX}..{maxX}, y {minY}..{maxY}");
        }

        // Exactly the same, not approximately. The two walks read from
        // different objects but every number should have survived the copy.
        Assert.Equal(0, differing);
    }

    [Fact]
    public void An_edit_actually_changes_the_picture()
    {
        // The whole reason this renderer exists. Drawing the vanilla surface
        // would have shown an author what they started from, however much they
        // edited.
        var opened = Screen("Quitagme");
        if (opened == null) return;
        var (surface, node) = opened.Value;

        var assets = VanillaUiLibrary.Assets;
        var seeded = VanillaUiSeed.FromBase(node);
        var before = UiAuthoredRenderer.Render(seeded, surface.Width, surface.Height, assets);

        var label = seeded.Children.FirstOrDefault(c => c.Text != null);
        Assert.NotNull(label);
        label!.Text!.Value = "Wildly Different Text Here";

        var after = UiAuthoredRenderer.Render(seeded, surface.Width, surface.Height, assets);
        var (differing, _) = Compare(before, after);

        _out.WriteLine($"retyping a label moved {differing} pixels");
        Assert.True(differing > 100, "an edit has to reach the picture");
    }

    [Fact]
    public void Moving_something_moves_it_on_screen()
    {
        var opened = Screen("Quitagme");
        if (opened == null) return;
        var (surface, node) = opened.Value;

        var assets = VanillaUiLibrary.Assets;
        var seeded = VanillaUiSeed.FromBase(node);
        var before = UiAuthoredRenderer.Render(seeded, surface.Width, surface.Height, assets);

        var target = seeded.Children.First(c => c.Image != null);
        target.Rect.Position = new[] { target.Rect.Position[0] + 120f, target.Rect.Position[1] };

        var after = UiAuthoredRenderer.Render(seeded, surface.Width, surface.Height, assets);
        var (differing, _) = Compare(before, after);
        _out.WriteLine($"moving an object 120px moved {differing} pixels");
        Assert.True(differing > 1000);
    }

    [Fact]
    public void An_object_the_pack_adds_appears()
    {
        var opened = Screen("Quitagme");
        if (opened == null) return;
        var (surface, node) = opened.Value;

        var assets = VanillaUiLibrary.Assets;
        var seeded = VanillaUiSeed.FromBase(node);
        var before = UiAuthoredRenderer.Render(seeded, surface.Width, surface.Height, assets);

        seeded.Children.Add(new UiNodeDef
        {
            Name = "My Panel",
            Rect = new UiRectDef { Size = new[] { 300f, 120f }, Position = new[] { 0f, 260f } },
            Image = new UiImageDef { Sprite = "Semi Rounded", Type = "Sliced", Tint = "#FF4444FF" },
        });

        var report = new UiRenderReport();
        var after = UiAuthoredRenderer.Render(seeded, surface.Width, surface.Height, assets, report);
        var (differing, _) = Compare(before, after);

        _out.WriteLine($"a new panel drew {differing} pixels; missing: " +
                       $"{report.MissingSprites.Count}");
        Assert.Empty(report.MissingSprites);
        Assert.True(differing > 10000, "a 300x120 panel should be plainly visible");
    }

    [Fact]
    public void A_sprite_is_found_by_the_name_an_author_would_type()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        var assets = VanillaUiLibrary.Assets;

        Assert.NotNull(assets.SpriteByName("Semi Rounded"));
        Assert.NotNull(assets.SpriteByName("Rounded"));
        Assert.Null(assets.SpriteByName("Not A Sprite"));

        // And its slicing comes with it, because that belongs to the art.
        var border = assets.BorderByName("Semi Rounded");
        Assert.Equal(30, border.Left, 3);
        Assert.Equal(128, assets.BorderByName("Rounded").Left, 3);
        Assert.True(assets.BorderByName("Not A Sprite").IsEmpty);

        _out.WriteLine($"{assets.SpriteNames.Count()} sprites an author can choose from");
    }

    [Fact]
    public void The_selected_object_is_marked_on_the_picture()
    {
        var opened = Screen("Quitagme");
        if (opened == null) return;
        var (surface, node) = opened.Value;

        var assets = VanillaUiLibrary.Assets;
        var seeded = VanillaUiSeed.FromBase(node);
        var plain = UiAuthoredRenderer.Render(seeded, surface.Width, surface.Height, assets);
        var marked = UiAuthoredRenderer.Render(seeded, surface.Width, surface.Height, assets,
                                               highlight: seeded.Children[0]);

        var (differing, _) = Compare(plain, marked);
        _out.WriteLine($"the selection marker drew {differing} pixels");
        Assert.True(differing > 0, "selecting a row has to show on the picture");
    }
}
