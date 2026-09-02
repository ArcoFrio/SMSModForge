using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's own font assets, read and laid out.
/// <para/>
/// The fixtures are three real assets out of the game: the most-used SDF font,
/// one with kerning, and one of the bitmap atlases that must never be put
/// through an SDF threshold.
/// </summary>
public class TmpFontTests
{
    private readonly ITestOutputHelper _out;
    public TmpFontTests(ITestOutputHelper o) => _out = o;

    private static TmpFont Font(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Fixtures", "Fonts")))
            dir = dir.Parent;
        Assert.True(dir != null, "Fixtures/Fonts is not above " + AppContext.BaseDirectory);

        var font = TmpFont.Load(Path.Combine(dir!.FullName, "Fixtures", "Fonts", name + ".json"));
        Assert.True(font != null, "could not load " + name);
        return font!;
    }

    private static TmpFont CurseCasual => Font("Curse Casual SDF");
    private static TmpFont Liberation => Font("LiberationSans SDF");
    private static TmpFont AlataBitmap => Font("Alata-Regular-Outline 28 SDF");

    // ── Reading an asset ─────────────────────────────────────────────

    [Fact]
    public void A_font_asset_carries_its_face_its_glyphs_and_its_atlas()
    {
        var f = CurseCasual;
        Assert.Equal("Curse Casual SDF", f.Name);
        Assert.Equal("Curse Casual", f.SourceFontFile);
        Assert.Equal("Curse Casual", f.Face.FamilyName);
        Assert.True(f.Face.PointSize > 0);
        Assert.True(f.Face.LineHeight > f.Face.AscentLine);
        Assert.NotEmpty(f.Glyphs);
        Assert.NotEmpty(f.Characters);
        Assert.Equal(1024, f.Atlas.Width);
        _out.WriteLine($"{f.Glyphs.Count} glyphs, pointSize {f.Face.PointSize}, " +
                       $"lineHeight {f.Face.LineHeight}");
    }

    [Fact]
    public void A_character_resolves_to_a_glyph_with_a_box_inside_the_atlas()
    {
        var f = CurseCasual;
        var a = f.GlyphFor('A');
        Assert.NotNull(a);

        var r = a!.RectTopLeft;
        Assert.True(r[2] > 0 && r[3] > 0, "the glyph has an area");
        Assert.True(r[0] >= 0 && r[0] + r[2] <= f.Atlas.Width, "inside horizontally");
        Assert.True(r[1] >= 0 && r[1] + r[3] <= f.Atlas.Height, "inside vertically");
        Assert.True(a.Metrics.Advance > 0);
        _out.WriteLine($"'A' rect [{string.Join(",", r)}] advance {a.Metrics.Advance}");
    }

    [Fact]
    public void A_character_the_atlas_never_baked_is_absent_not_a_crash()
    {
        // Real gap: this font is missing 14 printable ASCII characters, because
        // the vanilla UI never displayed them.
        var f = CurseCasual;
        Assert.True(f.Has('A'));
        Assert.False(f.Has('☃'));       // a snowman was certainly not baked
        Assert.Null(f.GlyphFor('☃'));

        var absent = Enumerable.Range(32, 95).Where(c => !f.Has(c)).ToList();
        _out.WriteLine("missing printable ASCII: " +
                       string.Join(" ", absent.Select(c => (char)c)));
        Assert.NotEmpty(absent);
    }

    // ── Distance field versus bitmap ─────────────────────────────────

    [Fact]
    public void The_spread_is_twice_the_gradient_scale()
    {
        // Measured off the real atlases across every font in the game: alpha
        // spans its full range over 2 x GradientScale texels. Curse Casual is
        // the case that proves it follows the gradient and not the padding -
        // padding 9, gradient 16.
        var f = CurseCasual;
        Assert.True(f.IsDistanceField);
        Assert.Equal(16.0, f.MaterialNumber("GradientScale")!.Value, 3);
        Assert.Equal(32.0, f.SdfSpread!.Value, 3);
        Assert.Equal(9, f.Atlas.Padding);

        var lib = Liberation;
        Assert.Equal(10.0, lib.MaterialNumber("GradientScale")!.Value, 3);
        Assert.Equal(20.0, lib.SdfSpread!.Value, 3);
    }

    [Fact]
    public void A_bitmap_atlas_is_not_a_distance_field_and_says_so()
    {
        // Seven of the game's fonts are bitmaps whose names end in SDF. Putting
        // a threshold through one would wreck it, and the discriminator is the
        // presence of GradientScale rather than the shader's name - a fact
        // about the asset instead of a string TMP could rename.
        var f = AlataBitmap;
        Assert.False(f.IsDistanceField);
        Assert.Null(f.SdfSpread);
        Assert.Null(f.MaterialNumber("GradientScale"));
        Assert.Contains("Bitmap", (string)f.Material["shader"].ToString()!);
    }

    [Fact]
    public void Coverage_sharpens_as_the_text_grows()
    {
        var f = CurseCasual;

        // Dead on the edge is half covered whatever the scale.
        Assert.Equal(0.5, f.Coverage(0.5, 1.0), 6);
        Assert.Equal(0.5, f.Coverage(0.5, 8.0), 6);

        // A texel inside the edge: barely darker when the text is tiny, solid
        // when it is large. That is what a distance field buys over a bitmap.
        double small = f.Coverage(0.5 + 1.0 / f.SdfSpread!.Value, 0.1);
        double large = f.Coverage(0.5 + 1.0 / f.SdfSpread.Value, 4.0);
        Assert.True(small < large, "larger text resolves the edge more sharply");
        Assert.Equal(1.0, large, 6);

        // Well outside is empty; well inside is solid. Never outside 0..1.
        Assert.Equal(0.0, f.Coverage(0.0, 1.0), 6);
        Assert.Equal(1.0, f.Coverage(1.0, 1.0), 6);
    }

    [Fact]
    public void A_bitmap_atlas_passes_its_alpha_straight_through()
    {
        var f = AlataBitmap;
        Assert.Equal(0.25, f.Coverage(0.25, 1.0), 6);
        Assert.Equal(0.75, f.Coverage(0.75, 99.0), 6);   // scale is irrelevant
        Assert.Equal(0.0, f.Coverage(-1, 1), 6);
        Assert.Equal(1.0, f.Coverage(2, 1), 6);
    }

    // ── Laying out a line ────────────────────────────────────────────

    [Fact]
    public void A_word_advances_by_the_sum_of_its_glyph_advances()
    {
        var f = CurseCasual;
        var layout = TmpTextLayout.Measure(f, "AVA", 36, align: TextAlign.Left);

        Assert.Equal(3, layout.Glyphs.Count);
        Assert.Single(layout.LineWidths);

        double scale = f.ScaleFor(36);
        double expected = "AVA".Sum(c => f.GlyphFor(c)!.Metrics.Advance * scale)
                        + "AVA".Zip("AVA".Skip(1)).Sum(
                              p => f.KerningBetween(f.GlyphFor(p.First), f.GlyphFor(p.Second)) * scale);
        Assert.Equal(expected, layout.Width, 3);
        _out.WriteLine($"'AVA' at 36 measures {layout.Width:0.##} wide, {layout.Height:0.##} tall");
    }

    [Fact]
    public void Kerning_is_applied_where_the_font_has_it()
    {
        // Liberation Sans carries 104 pairs. If any pair in a real string is
        // kerned, the laid-out width must differ from the naive advance sum -
        // otherwise the kerning table is being read and ignored.
        var f = Liberation;
        Assert.NotEmpty(f.Kerning);

        var pair = f.Kerning.First(k => k.FirstAdvance + k.SecondAdvance != 0);
        var chars = f.Characters.ToDictionary(c => c.GlyphIndex, c => c.Unicode);
        Assert.True(chars.ContainsKey(pair.First) && chars.ContainsKey(pair.Second),
                    "the kerned pair maps back to characters");

        string text = "" + (char)chars[pair.First] + (char)chars[pair.Second];
        var layout = TmpTextLayout.Measure(f, text, 40, align: TextAlign.Left);

        double scale = f.ScaleFor(40);
        double naive = text.Sum(c => f.GlyphFor(c)!.Metrics.Advance * scale);
        _out.WriteLine($"'{text}' kerned {layout.Width:0.###} vs unkerned {naive:0.###}");
        Assert.NotEqual(naive, layout.Width, 3);
    }

    [Fact]
    public void Text_scales_linearly_with_its_size()
    {
        var f = CurseCasual;
        var small = TmpTextLayout.Measure(f, "Hello", 20, align: TextAlign.Left);
        var large = TmpTextLayout.Measure(f, "Hello", 40, align: TextAlign.Left);

        Assert.Equal(small.Width * 2, large.Width, 3);
        Assert.Equal(small.Height * 2, large.Height, 3);
    }

    [Fact]
    public void The_first_line_hangs_from_the_ascent_not_from_zero()
    {
        // The sign that, got wrong, puts every line a glyph-height above where
        // it belongs and reads as a margin problem.
        var f = CurseCasual;
        var layout = TmpTextLayout.Measure(f, "A", 60, align: TextAlign.Left);
        var a = Assert.Single(layout.Glyphs);

        double scale = f.ScaleFor(60);
        double baseline = f.Face.AscentLine * scale;
        Assert.Equal(baseline - f.GlyphFor('A')!.Metrics.BearingY * scale, a.Y, 3);
        Assert.True(a.Y >= 0, "a capital should not stick out above the box");
        Assert.True(a.Bottom <= layout.Height + 0.01, "nor below it");
    }

    // ── Lines ────────────────────────────────────────────────────────

    [Fact]
    public void An_explicit_newline_starts_a_line()
    {
        var f = CurseCasual;
        var layout = TmpTextLayout.Measure(f, "ab\ncd", 30, align: TextAlign.Left);

        Assert.Equal(2, layout.Lines);
        Assert.Equal(2, layout.Glyphs.Count(g => g.Line == 0));
        Assert.Equal(2, layout.Glyphs.Count(g => g.Line == 1));
        Assert.True(layout.Glyphs.First(g => g.Line == 1).Y >
                    layout.Glyphs.First(g => g.Line == 0).Y,
                    "the second line is below the first");
    }

    [Fact]
    public void Wrapping_moves_a_whole_word_down_rather_than_splitting_it()
    {
        var f = Liberation;
        var wide = TmpTextLayout.Measure(f, "alpha beta", 30, align: TextAlign.Left);
        double half = wide.Width * 0.6;

        var wrapped = TmpTextLayout.Measure(f, "alpha beta", 30, wrapWidth: half,
                                            align: TextAlign.Left);
        Assert.Equal(2, wrapped.Lines);

        string First(int line) => new(wrapped.Glyphs.Where(g => g.Line == line)
                                             .Select(g => (char)g.Unicode).ToArray());
        Assert.Equal("alpha", First(0));
        Assert.Equal("beta", First(1));
        _out.WriteLine($"wrapped at {half:0.#}: '{First(0)}' / '{First(1)}'");
    }

    [Fact]
    public void A_word_too_long_to_fit_anywhere_overruns_rather_than_vanishing()
    {
        var f = Liberation;
        var layout = TmpTextLayout.Measure(f, "unbreakable", 30, wrapWidth: 10,
                                           align: TextAlign.Left);
        Assert.NotEmpty(layout.Glyphs);
        Assert.Equal(11, layout.Glyphs.Count);
    }

    [Fact]
    public void A_trailing_space_does_not_widen_the_line()
    {
        var f = Liberation;
        var bare = TmpTextLayout.Measure(f, "hi", 30, align: TextAlign.Left);
        var spaced = TmpTextLayout.Measure(f, "hi   ", 30, align: TextAlign.Left);
        Assert.Equal(bare.Width, spaced.Width, 3);
    }

    // ── Alignment ────────────────────────────────────────────────────

    [Fact]
    public void Alignment_moves_the_line_within_its_box()
    {
        var f = Liberation;
        const double box = 500;
        var left = TmpTextLayout.Measure(f, "hi", 30, box, TextAlign.Left);
        var centre = TmpTextLayout.Measure(f, "hi", 30, box, TextAlign.Center);
        var right = TmpTextLayout.Measure(f, "hi", 30, box, TextAlign.Right);

        // Measured as a shift from the left-aligned case, not as an absolute
        // position: a glyph's X is where its INK starts, and a left side
        // bearing legitimately puts that a pixel or two right of the pen. An
        // absolute assertion here tests the bearing, not the alignment.
        Assert.Equal((box - left.Width) / 2, centre.Glyphs[0].X - left.Glyphs[0].X, 3);
        Assert.Equal(box - right.Width, right.Glyphs[0].X - left.Glyphs[0].X, 3);
        Assert.Equal(left.Width, right.Width, 6);   // alignment moves, never resizes

        // And the bearing is real rather than an accident of this font.
        double scale = f.ScaleFor(30);
        Assert.Equal(f.GlyphFor('h')!.Metrics.BearingX * scale, left.Glyphs[0].X, 3);
    }

    // ── Missing glyphs and fallbacks ─────────────────────────────────

    [Fact]
    public void A_missing_character_is_reported_rather_than_silently_dropped()
    {
        var f = CurseCasual;
        int absent = Enumerable.Range(32, 95).First(c => !f.Has(c));

        var layout = TmpTextLayout.Measure(f, "a" + (char)absent + "b", 30,
                                           align: TextAlign.Left);
        Assert.Equal(2, layout.Glyphs.Count);
        Assert.Contains(absent, layout.Missing);
        _out.WriteLine($"'{(char)absent}' is not in this atlas, and the layout says so");
    }

    [Fact]
    public void A_fallback_font_fills_a_gap_the_first_one_has()
    {
        // What the game does: the scene's own fallback exists precisely because
        // Curse Casual lacks characters. A preview without the chain would fail
        // where the game succeeds.
        var f = CurseCasual;
        var fallback = Liberation;
        int absent = Enumerable.Range(32, 95).First(c => !f.Has(c) && fallback.Has(c));

        var alone = TmpTextLayout.Measure(f, "a" + (char)absent + "b", 30,
                                          align: TextAlign.Left);
        var chained = TmpTextLayout.Measure(f, "a" + (char)absent + "b", 30,
                                            align: TextAlign.Left,
                                            fallbacks: new[] { fallback });

        Assert.Equal(2, alone.Glyphs.Count);
        Assert.Equal(3, chained.Glyphs.Count);
        Assert.Empty(chained.Missing);
    }

    [Fact]
    public void Nothing_at_all_lays_out_to_nothing()
    {
        var f = CurseCasual;
        Assert.Empty(TmpTextLayout.Measure(f, "", 30).Glyphs);
        Assert.Empty(TmpTextLayout.Measure(f, null, 30).Glyphs);
        Assert.Equal(0, TmpTextLayout.Measure(f, "", 30).Width);
        Assert.Empty(TmpTextLayout.Measure(null!, "hello", 30).Glyphs);
    }
}
