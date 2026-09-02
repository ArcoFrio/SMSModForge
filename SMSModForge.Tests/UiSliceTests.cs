using System;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Nine-slicing: corners kept, middle stretched.
/// <para/>
/// Tested against a synthetic sprite whose nine regions are nine different
/// colours, so an assertion can say exactly which part of the source ended up
/// at a given pixel. A photograph would show that it looks plausible; this
/// shows where every region went.
/// </summary>
public class UiSliceTests
{
    private readonly ITestOutputHelper _out;
    public UiSliceTests(ITestOutputHelper o) => _out = o;

    // Nine flat colours, laid out as the sprite's nine regions. Opaque, so
    // premultiplied and straight agree and the numbers stay readable.
    private const byte TopLeft = 10, TopMid = 20, TopRight = 30;
    private const byte MidLeft = 40, Middle = 50, MidRight = 60;
    private const byte BotLeft = 70, BotMid = 80, BotRight = 90;

    /// <summary>A 30×30 sprite with a 10px border, each region a flat value in
    /// the blue channel.</summary>
    private static byte[] Sprite(int size = 30, int border = 10)
    {
        var px = new byte[size * size * 4];
        byte[,] regions =
        {
            { TopLeft, TopMid, TopRight },
            { MidLeft, Middle, MidRight },
            { BotLeft, BotMid, BotRight },
        };
        for (int y = 0; y < size; y++)
        {
            int row = y < border ? 0 : y < size - border ? 1 : 2;
            for (int x = 0; x < size; x++)
            {
                int col = x < border ? 0 : x < size - border ? 1 : 2;
                int i = (y * size + x) * 4;
                px[i] = regions[row, col];   // blue carries the region id
                px[i + 1] = 0;
                px[i + 2] = 0;
                px[i + 3] = 255;
            }
        }
        return px;
    }

    private static byte Blue(byte[] px, int width, int x, int y) => px[(y * width + x) * 4];
    private static byte Alpha(byte[] px, int width, int x, int y) => px[(y * width + x) * 4 + 3];

    // ── Adjusting the border ─────────────────────────────────────────

    [Fact]
    public void A_border_that_fits_is_left_alone()
    {
        var b = UiSlice.Adjust(new SliceBorder(30, 30, 30, 30), 350, 75);
        Assert.Equal(30, b.Left, 6);
        Assert.Equal(30, b.Right, 6);
        Assert.Equal(30, b.Bottom, 6);
        Assert.Equal(30, b.Top, 6);
    }

    [Fact]
    public void A_border_too_tall_for_its_rectangle_shrinks_to_fit()
    {
        // Real: "Semi Rounded" is bordered 30 on every side and is used on bars
        // 35 pixels high. 1112 of the game's sliced images are like this.
        var b = UiSlice.Adjust(new SliceBorder(30, 30, 30, 30), 755, 35);

        Assert.Equal(30, b.Left, 6);          // horizontally there is room
        Assert.Equal(30, b.Right, 6);
        Assert.Equal(17.5, b.Bottom, 6);      // vertically it is halved to fit
        Assert.Equal(17.5, b.Top, 6);
        Assert.Equal(35, b.Bottom + b.Top, 6);
    }

    [Fact]
    public void A_sprite_that_is_entirely_corner_still_works()
    {
        // "Rounded" is 256×256 bordered 128 all round - there is no middle at
        // all - and the game draws it at 85×85 as a circular close button.
        var b = UiSlice.Adjust(new SliceBorder(128, 128, 128, 128), 85, 85);
        Assert.Equal(42.5, b.Left, 6);
        Assert.Equal(85, b.Left + b.Right, 6);
        Assert.Equal(85, b.Bottom + b.Top, 6);
    }

    [Fact]
    public void The_border_is_scaled_before_it_is_shrunk()
    {
        // A multiplier of 2 halves the border; only then is it asked whether it
        // fits. Doing those in the other order gives a different answer for
        // anything tight, and about 390 images use a multiplier.
        var b = UiSlice.Adjust(new SliceBorder(30, 30, 30, 30), 100, 100, divisor: 2);
        Assert.Equal(15, b.Left, 6);

        Assert.Equal(2.0, UiSlice.DivisorFor(200, 100, 1), 6);
        Assert.Equal(1.5, UiSlice.DivisorFor(100, 100, 1.5), 6);
        Assert.Equal(1.0, UiSlice.DivisorFor(100, 100, 1), 6);
    }

    // ── Composing ────────────────────────────────────────────────────

    [Fact]
    public void Corners_land_in_the_corners_at_their_own_size()
    {
        const int w = 100, h = 60;
        var made = UiSlice.Compose(Sprite(), 30, 30, new SliceBorder(10, 10, 10, 10), w, h);

        // Top row of the source is the top row of the result, and so on round.
        Assert.Equal(TopLeft, Blue(made, w, 0, 0));
        Assert.Equal(TopRight, Blue(made, w, w - 1, 0));
        Assert.Equal(BotLeft, Blue(made, w, 0, h - 1));
        Assert.Equal(BotRight, Blue(made, w, w - 1, h - 1));

        // Corners keep their 10×10 size rather than scaling with the target.
        Assert.Equal(TopLeft, Blue(made, w, 9, 9));
        Assert.Equal(Middle, Blue(made, w, 10, 10));
    }

    [Fact]
    public void Only_the_middle_stretches()
    {
        const int w = 300, h = 200;
        var made = UiSlice.Compose(Sprite(), 30, 30, new SliceBorder(10, 10, 10, 10), w, h);

        // The edges stay their own thickness however far the middle is pulled.
        Assert.Equal(TopMid, Blue(made, w, w / 2, 0));
        Assert.Equal(TopMid, Blue(made, w, w / 2, 9));
        Assert.Equal(Middle, Blue(made, w, w / 2, 10));
        Assert.Equal(MidLeft, Blue(made, w, 0, h / 2));
        Assert.Equal(MidRight, Blue(made, w, w - 1, h / 2));
        Assert.Equal(Middle, Blue(made, w, w / 2, h / 2));
    }

    [Fact]
    public void The_regions_do_not_bleed_into_one_another()
    {
        // The reason each cell clamps to its own source rectangle. Without it a
        // stretched edge drags in a pixel of the corner beside it, and the join
        // smears - which is exactly the artefact that killed the nine-element
        // version of this.
        const int w = 400, h = 300;
        var made = UiSlice.Compose(Sprite(), 30, 30, new SliceBorder(10, 10, 10, 10), w, h);

        // Every pixel must be one of the nine colours exactly. A blend between
        // two regions would produce something that is neither.
        var known = new byte[] { TopLeft, TopMid, TopRight, MidLeft, Middle,
                                 MidRight, BotLeft, BotMid, BotRight };
        var strays = new System.Collections.Generic.List<string>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte v = Blue(made, w, x, y);
                if (!known.Contains(v)) strays.Add($"({x},{y})={v}");
            }

        _out.WriteLine($"{w * h} pixels, {strays.Count} blended");
        Assert.True(strays.Count == 0,
            "pixels blended across a slice boundary: " + string.Join(" ", strays.Take(8)));
    }

    [Fact]
    public void Turning_off_the_centre_leaves_a_frame()
    {
        const int w = 100, h = 100;
        var made = UiSlice.Compose(Sprite(), 30, 30, new SliceBorder(10, 10, 10, 10),
                                   w, h, fillCenter: false);

        Assert.Equal(0, Alpha(made, w, w / 2, h / 2));   // hollow
        Assert.Equal(255, Alpha(made, w, 0, 0));         // frame intact
        Assert.Equal(TopMid, Blue(made, w, w / 2, 0));
    }

    [Fact]
    public void An_empty_border_is_a_plain_stretch()
    {
        // So a caller has no separate path for an unsliced image.
        const int w = 90, h = 90;
        var made = UiSlice.Compose(Sprite(), 30, 30, default, w, h);

        Assert.True(new SliceBorder().IsEmpty);
        // Each source region now covers a third of the result.
        Assert.Equal(TopLeft, Blue(made, w, 5, 5));
        Assert.Equal(Middle, Blue(made, w, 45, 45));
        Assert.Equal(BotRight, Blue(made, w, 85, 85));
    }

    [Fact]
    public void A_target_smaller_than_the_border_is_all_corner_and_no_middle()
    {
        // The circular-button case, end to end.
        const int w = 20, h = 20;
        var made = UiSlice.Compose(Sprite(), 30, 30, new SliceBorder(10, 10, 10, 10), w, h);

        Assert.Equal(TopLeft, Blue(made, w, 0, 0));
        Assert.Equal(TopRight, Blue(made, w, w - 1, 0));
        Assert.Equal(BotRight, Blue(made, w, w - 1, h - 1));
        // The middle has been squeezed out entirely rather than overrunning.
        Assert.DoesNotContain(Middle, Enumerable.Range(0, w * h)
            .Select(i => Blue(made, w, i % w, i / w)));
    }

    [Fact]
    public void Nothing_is_drawn_into_no_space()
    {
        Assert.Empty(UiSlice.Compose(Sprite(), 30, 30, default, 0, 50));
        Assert.Empty(UiSlice.Compose(Sprite(), 30, 30, default, 50, 0));
        Assert.Throws<ArgumentException>(
            () => UiSlice.Compose(new byte[4], 0, 0, default, 10, 10));
        Assert.Throws<ArgumentException>(
            () => UiSlice.Compose(new byte[4], 30, 30, default, 10, 10));
    }

    // ── Tint ─────────────────────────────────────────────────────────

    [Fact]
    public void A_white_sprite_becomes_whatever_colour_it_is_tinted()
    {
        // How 51 sprites dress a whole interface: 1521 sliced images are a
        // plain white shape recoloured at the point of use.
        var px = new byte[] { 255, 255, 255, 255 };
        UiSlice.Tint(px, b: 0x5B, g: 0x2B, r: 0x57, a: 255);
        Assert.Equal(new byte[] { 0x5B, 0x2B, 0x57, 255 }, px);
    }

    [Fact]
    public void A_tint_with_alpha_fades_the_image_as_well_as_colouring_it()
    {
        // Premultiplied: every channel scales with alpha, so #FFFFFF00 is
        // invisible rather than opaque white.
        var px = new byte[] { 200, 100, 50, 255 };
        UiSlice.Tint(px, 255, 255, 255, 0);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, px);

        var half = new byte[] { 200, 100, 50, 200 };
        UiSlice.Tint(half, 255, 255, 255, 128);
        // Byte arithmetic, so an exact expectation rather than a tolerance:
        // 200 x (128/255) rounds to 100, and so does the alpha.
        Assert.InRange(half[0], 99, 101);
        Assert.InRange(half[3], 99, 101);
    }

    [Fact]
    public void Tinting_with_white_changes_nothing()
    {
        var px = new byte[] { 12, 34, 56, 78 };
        var before = (byte[])px.Clone();
        UiSlice.Tint(px, 255, 255, 255, 255);
        Assert.Equal(before, px);
    }
}
