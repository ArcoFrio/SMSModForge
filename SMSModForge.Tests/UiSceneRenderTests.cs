using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The whole renderer, on real vanilla screens.
/// <para/>
/// These need the extraction folder, which is ~93 MB and lives beside the
/// editor's resources rather than in the test fixtures. Where it is absent the
/// tests say so and skip — a contributor without it should not see a wall of
/// red for something they were never asked to have.
/// </summary>
public class UiSceneRenderTests
{
    private readonly ITestOutputHelper _out;
    public UiSceneRenderTests(ITestOutputHelper o) => _out = o;

    private static string? ExtractionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "SMSModForge", "Resources",
                                            "VanillaOverlays");
            if (File.Exists(Path.Combine(candidate, "index.json"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private (VanillaUiAssets Assets, VanillaUiSurface Surface)? Open(string surfaceFile)
    {
        string? root = ExtractionRoot();
        if (root == null)
        {
            _out.WriteLine("no extraction found - skipping");
            return null;
        }
        var surface = VanillaUiSurface.Load(Path.Combine(root, "Surfaces", surfaceFile));
        if (surface == null)
        {
            _out.WriteLine("no " + surfaceFile + " - skipping");
            return null;
        }
        return (new VanillaUiAssets(root), surface);
    }

    // ── A real screen ────────────────────────────────────────────────

    [Fact]
    public void The_main_canvas_loads_as_the_tree_the_census_counted()
    {
        var opened = Open("9_MainCanvas.json");
        if (opened == null) return;
        var (_, surface) = opened.Value;

        Assert.Equal(1920, surface.Width, 1);
        Assert.Equal(1080, surface.Height, 1);
        Assert.True(surface.IsDrawable);
        Assert.Equal(21, surface.Root.Children.Count);
        Assert.Equal(4088, surface.Root.Walk().Count());   // 4087 below the canvas

        var payout = surface.Base("Payout");
        Assert.NotNull(payout);
        Assert.False(payout!.ActiveSelf, "Payout is off until its moment");
    }

    [Fact]
    public void A_base_renders_to_pixels_that_are_actually_there()
    {
        var opened = Open("9_MainCanvas.json");
        if (opened == null) return;
        var (assets, surface) = opened.Value;
        if (!assets.IsAvailable) { _out.WriteLine("no sprites - skipping"); return; }

        var quit = surface.Base("Quitagme");
        Assert.NotNull(quit);

        var report = new UiRenderReport();
        var pixels = UiSceneRenderer.RenderBase(surface, quit!, assets, report);

        int w = (int)surface.Width, h = (int)surface.Height;
        Assert.Equal(w * h * 4, pixels.Length);

        int lit = 0;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 0) lit++;

        _out.WriteLine($"drew {report.Drawn} graphics, {lit} lit pixels of {w * h}");
        _out.WriteLine($"missing sprites: {report.MissingSprites.Count}, " +
                       $"fonts: {report.MissingFonts.Count}, " +
                       $"glyphs: {report.MissingGlyphs.Count}");
        foreach (var m in report.MissingSprites.Take(4)) _out.WriteLine("   sprite " + m);
        foreach (var m in report.MissingFonts.Take(4)) _out.WriteLine("   font " + m);
        foreach (var m in report.MissingGlyphs.Take(4)) _out.WriteLine("   glyph " + m);

        Assert.True(report.Drawn > 0, "nothing was drawn at all");
        Assert.True(lit > 0, "everything came out transparent");

        // A window, not a full-screen wash: it should cover a real part of the
        // canvas but nothing like all of it.
        double covered = lit / (double)(w * h);
        Assert.InRange(covered, 0.005, 0.5);
    }

    [Fact]
    public void Everything_a_base_asks_for_is_actually_present()
    {
        // The renderer reports what it could not find rather than drawing a
        // hole. For a vanilla screen the answer should be nothing missing - if
        // it is not, the extraction is incomplete and the preview would be
        // quietly lying about what the game looks like.
        var opened = Open("9_MainCanvas.json");
        if (opened == null) return;
        var (assets, surface) = opened.Value;
        if (!assets.IsAvailable) return;

        var report = new UiRenderReport();
        UiSceneRenderer.RenderBase(surface, surface.Base("Quitagme")!, assets, report);

        Assert.Empty(report.MissingSprites);
        Assert.Empty(report.MissingFonts);

        // Two things that are absent by nature rather than by omission, and are
        // reported apart so they cannot be mistaken for a broken extraction.
        foreach (var l in report.LegacyText) _out.WriteLine("legacy UI.Text: " + l);
        foreach (var g in report.MissingGlyphs) _out.WriteLine("unbaked glyph: " + g);
    }

    [Fact]
    public void A_switched_off_child_draws_nothing()
    {
        // The rule that keeps a preview from showing every screen at once. Only
        // the base being looked at is forced visible; what is off inside it
        // stays off.
        var opened = Open("9_MainCanvas.json");
        if (opened == null) return;
        var (assets, surface) = opened.Value;
        if (!assets.IsAvailable) return;

        var navigator = surface.Base("Navigator");
        Assert.NotNull(navigator);

        var all = new UiRenderReport();
        UiSceneRenderer.RenderBase(surface, navigator!, assets, all);

        // Turning the base itself off must empty it entirely - RenderBase forces
        // only the top node, so this is the child rule, not the top one.
        var off = new UiRenderReport();
        var child = navigator!.Children.FirstOrDefault(c => c.ActiveSelf);
        Assert.NotNull(child);
        bool was = child!.ActiveSelf;
        try
        {
            child.ActiveSelf = false;
            UiSceneRenderer.RenderBase(surface, navigator, assets, off);
            Assert.True(off.Drawn < all.Drawn,
                        "switching a child off has to draw fewer things");
        }
        finally { child.ActiveSelf = was; }
    }

    [Fact]
    public void A_canvas_that_was_never_sized_renders_nothing_rather_than_zeroes()
    {
        var opened = Open("Transition_Blink_Transition Root.json");
        if (opened == null) return;
        var (assets, surface) = opened.Value;

        // One of the six whose Canvas component is disabled, so Unity never gave
        // it a size. Better an empty result than a canvas of zero-sized rects
        // that looks like a rendering failure.
        Assert.False(surface.IsDrawable);
        Assert.Empty(UiSceneRenderer.Render(surface, assets));
    }

    // ── Sanity on the parts, without the disk ────────────────────────

    private sealed class NoAssets : IUiAssets
    {
        public UiSprite? Sprite(string key) => null;
        public UiFontSet? Font(string name) => null;
    }

    [Fact]
    public void A_missing_sprite_is_reported_once_and_not_drawn()
    {
        var opened = Open("9_MainCanvas.json");
        if (opened == null) return;
        var (_, surface) = opened.Value;

        var report = new UiRenderReport();
        UiSceneRenderer.RenderBase(surface, surface.Base("Quitagme")!, new NoAssets(), report);

        Assert.NotEmpty(report.MissingSprites);
        Assert.Equal(0, report.Drawn);
        Assert.False(report.Complete);
        // Deduplicated: one line per sprite, not one per use.
        Assert.Equal(report.MissingSprites.Distinct().Count(), report.MissingSprites.Count);
    }
}
