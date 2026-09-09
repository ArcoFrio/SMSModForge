using System;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The half of a TextMeshPro font that is not the glyph shape.
/// <para/>
/// Reported from a screenshot: the preview's text was thinner than the game's,
/// carried no outline and cast no shadow. All three live in the font's
/// material, and the raster was reading only the atlas - so it drew the right
/// letters in the wrong hand.
/// <para/>
/// Every test here compares against the SAME font with the material's own
/// setting turned off. A test that only asserted "there is ink here" would pass
/// on the broken renderer too; what has to be shown is that the setting is what
/// puts it there.
/// </summary>
public sealed class UiTextMaterialTests
{
    private readonly ITestOutputHelper _out;
    public UiTextMaterialTests(ITestOutputHelper o) => _out = o;

    /// <summary>The game's outlined display font: dilate 0.351, outline 0.2,
    /// and an underlay offset down and to the left.</summary>
    private const string Outlined = "Curse Casual SDF Outline";

    private UiFontSet? Load(string name)
    {
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return null; }
        var set = VanillaUiLibrary.Assets.Font(name);
        if (set == null) _out.WriteLine("no font " + name);
        return set;
    }

    /// <summary>Draw one word and hand back the pixels, 300x120.</summary>
    private static byte[] Draw(UiFontSet set, UiColor colour, double size = 72)
    {
        var layout = TmpTextLayout.Measure(set.Font, "Shop", size);
        return UiTextRaster.Draw(layout, set.Font, set.Alpha, set.Width, set.Height,
                                 300, 120, colour);
    }

    private static int Lit(byte[] pixels)
    {
        int n = 0;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 8) n++;
        return n;
    }

    /// <summary>Strip a material setting, so the same font can be drawn with and
    /// without it.</summary>
    private static UiFontSet Without(UiFontSet set, params string[] keys)
    {
        var copy = new TmpFont
        {
            Face = set.Font.Face,
            Glyphs = set.Font.Glyphs,
            Characters = set.Font.Characters,
            Kerning = set.Font.Kerning,
            Atlas = set.Font.Atlas,
            Material = set.Font.Material.ToDictionary(p => p.Key, p => p.Value),
        };
        foreach (var key in keys) copy.Material[key] = 0.0;
        return new UiFontSet(copy, set.Alpha, set.Width, set.Height);
    }

    [Fact]
    public void Dilate_makes_the_stroke_heavier()
    {
        var set = Load(Outlined);
        if (set == null) return;

        // Outline off in both, so only the face is being compared.
        var with = Without(set, "OutlineWidth", "UnderlayColor");
        var without = Without(set, "OutlineWidth", "UnderlayColor", "FaceDilate");

        int heavy = Lit(Draw(with, new UiColor(255, 255, 255, 255)));
        int light = Lit(Draw(without, new UiColor(255, 255, 255, 255)));

        _out.WriteLine($"dilate {set.Font.FaceShift:0.###}: {heavy} px vs {light} px without");
        Assert.True(heavy > light * 1.05,
                    $"dilate did nothing: {heavy} px with, {light} px without");
    }

    [Fact]
    public void An_outline_puts_its_own_colour_around_the_letters()
    {
        var set = Load(Outlined);
        if (set == null) return;
        Assert.True(set.Font.HasOutline, "this font is supposed to carry an outline");

        // White letters. Anything DARK in the result can only be the outline,
        // which this material paints black.
        var white = new UiColor(255, 255, 255, 255);
        var outlined = Draw(Without(set, "UnderlayColor"), white);
        var plain = Draw(Without(set, "UnderlayColor", "OutlineWidth"), white);

        int Dark(byte[] p)
        {
            int n = 0;
            for (int i = 0; i < p.Length; i += 4)
                if (p[i + 3] > 128 && p[i + 2] < 100) n++;   // opaque, but not white
            return n;
        }

        _out.WriteLine($"dark px: {Dark(outlined)} outlined, {Dark(plain)} plain");
        Assert.True(Dark(outlined) > 50, "no outline was painted");
        Assert.True(Dark(plain) < Dark(outlined) / 4, "the control has an outline too");
    }

    [Fact]
    public void The_shadow_falls_the_way_the_material_points_it()
    {
        var set = Load(Outlined);
        if (set == null) return;
        Assert.True(set.Font.HasUnderlay, "this font is supposed to cast a shadow");

        var (ox, oy) = set.Font.UnderlayOffsetTexels;
        _out.WriteLine($"underlay offset {ox:0.##}, {oy:0.##} texels (+y is up)");
        Assert.True(ox < 0 && oy < 0, "this material's shadow goes down and left");

        // Drawn white on nothing: the shadow is the only ink that is not white.
        var with = Draw(set, new UiColor(255, 255, 255, 255));
        var without = Draw(Without(set, "UnderlayColor"), new UiColor(255, 255, 255, 255));

        // Where the shadow lands: below and left of where the glyph itself is.
        // Comparing the two runs column by column, the extra ink has to sit on
        // the left of the word, not the right.
        int leftGain = 0, rightGain = 0;
        for (int y = 0; y < 120; y++)
            for (int x = 0; x < 300; x++)
            {
                int i = (y * 300 + x) * 4;
                int gain = with[i + 3] - without[i + 3];
                if (gain <= 8) continue;
                if (x < 150) leftGain += gain; else rightGain += gain;
            }

        _out.WriteLine($"extra ink: {leftGain} left, {rightGain} right");
        Assert.True(leftGain > 0, "the shadow added nothing");
        Assert.True(leftGain > rightGain, "the shadow fell the wrong way");
    }

    [Fact]
    public void A_font_with_a_bare_material_is_drawn_exactly_as_before()
    {
        // The change must not touch fonts that ask for none of it: seven of the
        // game's are plain bitmaps with nothing to apply.
        var set = Load(Outlined);
        if (set == null) return;

        var bare = Without(set, "FaceDilate", "OutlineWidth", "UnderlayColor",
                           "UnderlayDilate", "UnderlayOffsetX", "UnderlayOffsetY");
        Assert.False(bare.Font.HasOutline);
        Assert.False(bare.Font.HasUnderlay);

        var layout = TmpTextLayout.Measure(bare.Font, "Shop", 72);
        var drawn = UiTextRaster.Draw(layout, bare.Font, bare.Alpha, bare.Width, bare.Height,
                                      300, 120, new UiColor(255, 255, 255, 255));

        // Every lit pixel is the face colour, with nothing else mixed in.
        for (int i = 0; i < drawn.Length; i += 4)
            if (drawn[i + 3] > 200)
                Assert.True(drawn[i] > 200 && drawn[i + 1] > 200 && drawn[i + 2] > 200,
                            $"a bare material painted something other than the face colour");
    }
}
