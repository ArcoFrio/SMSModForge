using System;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Stacking premultiplied images, and the two effects the vanilla UI leans on:
/// 667 shadows and 323 outlines, so about a thousand objects render flat
/// without them.
/// </summary>
public class UiCompositorTests
{
    private readonly ITestOutputHelper _out;
    public UiCompositorTests(ITestOutputHelper o) => _out = o;

    private static byte[] Solid(int w, int h, byte b, byte g, byte r, byte a)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a;
        }
        return px;
    }

    private static (byte b, byte g, byte r, byte a) At(byte[] px, int w, int x, int y)
    {
        int i = (y * w + x) * 4;
        return (px[i], px[i + 1], px[i + 2], px[i + 3]);
    }

    // ── Colour ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("#FFFFFF", 255, 255, 255, 255)]
    [InlineData("#FFFFFFFF", 255, 255, 255, 255)]
    [InlineData("#302F46FF", 0x46, 0x2F, 0x30, 255)]
    [InlineData("302F46", 0x46, 0x2F, 0x30, 255)]
    [InlineData("#0084FF67", 0xFF, 0x84, 0x00, 0x67)]
    public void A_tint_reads_as_the_game_writes_it(string hex, int b, int g, int r, int a)
    {
        var c = UiColor.Parse(hex);
        Assert.Equal(b, c.B);
        Assert.Equal(g, c.G);
        Assert.Equal(r, c.R);
        Assert.Equal(a, c.A);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("not a colour")]
    public void An_unreadable_tint_draws_the_sprite_as_it_is(string? hex)
    {
        // The same thing a missing tint means. Visibly the sprite's own
        // colours, rather than invisible - a wrong tint that hid the object
        // would read as the object not existing.
        Assert.True(UiColor.Parse(hex).IsOpaqueWhite);
    }

    // ── Blending ─────────────────────────────────────────────────────

    [Fact]
    public void An_opaque_layer_covers_what_is_under_it()
    {
        var target = Solid(10, 10, 0, 0, 255, 255);      // red
        var source = Solid(4, 4, 255, 0, 0, 255);        // blue
        UiCompositor.Blend(target, 10, 10, source, 4, 4, 3, 3);

        Assert.Equal<(byte, byte, byte, byte)>((255, 0, 0, 255), At(target, 10, 4, 4));
        Assert.Equal<(byte, byte, byte, byte)>((0, 0, 255, 255), At(target, 10, 0, 0));
    }

    [Fact]
    public void A_half_transparent_layer_mixes_rather_than_replaces()
    {
        // Premultiplied: a 50% blue layer carries 128 in every channel it uses.
        var target = Solid(4, 4, 0, 0, 0, 255);
        var source = Solid(4, 4, 128, 0, 0, 128);
        UiCompositor.Blend(target, 4, 4, source, 4, 4, 0, 0);

        var (b, _, _, a) = At(target, 4, 0, 0);
        Assert.InRange(b, 126, 130);
        Assert.Equal(255, a);
    }

    [Fact]
    public void A_group_alpha_fades_a_whole_layer()
    {
        // What a CanvasGroup does, and the mechanism behind the gameplay canvas
        // dimming everything beneath it.
        var target = UiCompositor.NewLayer(4, 4);
        UiCompositor.Blend(target, 4, 4, Solid(4, 4, 255, 255, 255, 255), 4, 4, 0, 0,
                           alpha: 0.5);

        var (b, _, _, a) = At(target, 4, 0, 0);
        Assert.InRange(a, 126, 129);
        Assert.InRange(b, 126, 129);     // premultiplied, so colour fades too
    }

    [Fact]
    public void Nothing_is_drawn_outside_the_target()
    {
        // Shadow offsets reach here, and a shadow that wrapped to the far edge
        // would look like a bug in the pack rather than in the preview.
        var target = UiCompositor.NewLayer(8, 8);
        var source = Solid(4, 4, 255, 255, 255, 255);

        UiCompositor.Blend(target, 8, 8, source, 4, 4, -2, -2);   // overhangs top-left
        UiCompositor.Blend(target, 8, 8, source, 4, 4, 6, 6);     // overhangs bottom-right
        UiCompositor.Blend(target, 8, 8, source, 4, 4, 100, 100); // wholly outside

        Assert.Equal(255, At(target, 8, 0, 0).a);      // the part that landed
        Assert.Equal(255, At(target, 8, 7, 7).a);
        Assert.Equal(0, At(target, 8, 4, 0).a);        // nothing wrapped round
        Assert.Equal(0, At(target, 8, 0, 4).a);
    }

    // ── Silhouettes ──────────────────────────────────────────────────

    [Fact]
    public void A_silhouette_keeps_the_shape_and_throws_away_the_colour()
    {
        var shape = new byte[] { 200, 100, 50, 255,    0, 0, 0, 0 };   // one lit, one clear
        var made = UiCompositor.Silhouette(shape, new UiColor(0, 0, 0, 255));

        Assert.Equal<(byte, byte, byte, byte)>((0, 0, 0, 255), At(made, 2, 0, 0));
        Assert.Equal(0, At(made, 2, 1, 0).a);   // and does not invent shape
    }

    [Fact]
    public void A_silhouette_fades_where_the_original_does()
    {
        // useGraphicAlpha, on by default: this is what stops a shadow appearing
        // at full strength under a half-transparent icon.
        var shape = new byte[] { 128, 128, 128, 128 };
        var faded = UiCompositor.Silhouette(shape, new UiColor(0, 0, 0, 255));
        Assert.InRange(faded[3], 126, 130);

        var forced = UiCompositor.Silhouette(shape, new UiColor(0, 0, 0, 255),
                                             useGraphicAlpha: false);
        Assert.Equal(255, forced[3]);
    }

    // ── Shadow and outline ───────────────────────────────────────────

    [Fact]
    public void A_shadow_sits_behind_the_graphic_and_offset_from_it()
    {
        var target = UiCompositor.NewLayer(20, 20);
        var graphic = Solid(6, 6, 255, 255, 255, 255);

        UiEffects.Draw(target, 20, 20, graphic, 6, 6, left: 5, top: 5,
                       shadow: new UiEffect(new UiColor(0, 0, 0, 255), 3, -3));

        // The graphic occupies x,y 5..10 inclusive; the shadow 8..13. So 12,12
        // is shadow alone and 5,5 is graphic alone - picking a pixel the two
        // share would only prove which was drawn last.
        Assert.Equal<(byte, byte, byte, byte)>((255, 255, 255, 255), At(target, 20, 5, 5));
        // The shadow is down and to the right: Unity writes the offset in UI
        // space where positive Y is up, so -3 goes DOWN the screen.
        Assert.Equal<(byte, byte, byte, byte)>((0, 0, 0, 255), At(target, 20, 12, 12));
        // Nothing above-left of the graphic, which is where it would land if
        // that sign were inverted.
        Assert.Equal(0, At(target, 20, 2, 2).a);
    }

    [Fact]
    public void An_outline_is_four_offset_copies_not_a_ring()
    {
        // uGUI's own construction, which is why an outline's setting is a
        // distance rather than a width, and why a large one reads as four
        // corners.
        var target = UiCompositor.NewLayer(30, 30);
        var graphic = Solid(6, 6, 255, 255, 255, 255);

        UiEffects.Draw(target, 30, 30, graphic, 6, 6, left: 12, top: 12,
                       outline: new UiEffect(new UiColor(0, 0, 0, 255), 4, 4));

        // All four diagonals carry a copy.
        foreach (var (x, y) in new[] { (8, 8), (16, 8), (8, 16), (16, 16) })
            Assert.True(At(target, 30, x, y).a > 0, $"no outline copy at ({x},{y})");

        // The graphic still sits on top, undimmed.
        Assert.Equal<(byte, byte, byte, byte)>((255, 255, 255, 255), At(target, 30, 14, 14));
    }

    [Fact]
    public void Outline_is_drawn_under_shadow_and_both_under_the_graphic()
    {
        // Order matters: uGUI puts the outline furthest back. Drawing them the
        // other way round shows a shadow's colour inside the outline.
        var target = UiCompositor.NewLayer(30, 30);
        var graphic = Solid(10, 10, 255, 255, 255, 255);

        UiEffects.Draw(target, 30, 30, graphic, 10, 10, left: 10, top: 10,
                       shadow: new UiEffect(new UiColor(255, 0, 0, 255), 0, 0),
                       outline: new UiEffect(new UiColor(0, 0, 255, 255), 0, 0));

        // With every offset zero, all three land on the same pixels and the
        // graphic - drawn last - is what is seen.
        Assert.Equal<(byte, byte, byte, byte)>((255, 255, 255, 255), At(target, 30, 15, 15));
    }

    [Fact]
    public void A_graphic_with_no_effects_draws_exactly_once()
    {
        var target = UiCompositor.NewLayer(10, 10);
        var graphic = Solid(4, 4, 100, 100, 100, 100);

        UiEffects.Draw(target, 10, 10, graphic, 4, 4, 3, 3);
        var once = At(target, 10, 4, 4);

        var again = UiCompositor.NewLayer(10, 10);
        UiCompositor.Blend(again, 10, 10, graphic, 4, 4, 3, 3);
        Assert.Equal(At(again, 10, 4, 4), once);
    }

    [Fact]
    public void Effects_fade_with_the_group_they_are_in()
    {
        var full = UiCompositor.NewLayer(20, 20);
        var half = UiCompositor.NewLayer(20, 20);
        var graphic = Solid(6, 6, 255, 255, 255, 255);
        var shadow = new UiEffect(new UiColor(0, 0, 0, 255), 3, -3);

        UiEffects.Draw(full, 20, 20, graphic, 6, 6, 5, 5, shadow: shadow);
        UiEffects.Draw(half, 20, 20, graphic, 6, 6, 5, 5, shadow: shadow, alpha: 0.5);

        Assert.True(At(half, 20, 10, 10).a < At(full, 20, 10, 10).a,
                    "a group alpha has to reach the shadow too, not just the graphic");
    }
}
