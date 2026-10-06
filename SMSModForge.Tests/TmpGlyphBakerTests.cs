using System;
using System.IO;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The dialogue font's missing characters (1.7.0). Its export holds 87
/// characters and no 8 or 9, because it is a dynamic asset: the game draws
/// those from the Curse Casual font file as a line needs them, and the editor
/// now does the same. The test of whether that is done right is the game's own
/// work - the digits it DID bake, 0 to 7, drawn again from the font file and
/// set beside the originals.
/// </summary>
public sealed class TmpGlyphBakerTests
{
    private readonly ITestOutputHelper _out;
    public TmpGlyphBakerTests(ITestOutputHelper o) => _out = o;

    private static string FontJson => Path.Combine(VanillaUiLibrary.Root, "Fonts", DialogueLook.FontName + ".json");
    private static string FontFile => Path.Combine(AppContext.BaseDirectory, "VanillaOverlays", "Curse Casual.ttf");

    private static UiFontSet Shipped()
    {
        Assert.True(VanillaUiLibrary.IsAvailable, "the game's extracted fonts should be beside the tests");
        var set = VanillaUiLibrary.Assets.Font(DialogueLook.FontName);
        Assert.NotNull(set);
        return set!;
    }

    /// <summary>The same font with every glyph taken out, so everything it
    /// draws is drawn from the font file.</summary>
    private static UiFontSet AllBaked()
    {
        var font = TmpFont.Load(FontJson)!;
        font.Glyphs.Clear();
        font.Characters.Clear();
        var shipped = Shipped();
        int w = shipped.Width, h = shipped.Height;
        var alpha = new byte[w * h];
        Assert.True(TmpGlyphBaker.Attach(font, FontFile, alpha, w, 0, h));
        return new UiFontSet(font, alpha, w, h);
    }

    [Fact]
    public void TheExportReallyLacksThem_AndTheGameDrawsThemFromTheFontFile()
    {
        var raw = TmpFont.Load(FontJson)!;
        Assert.Null(raw.GlyphFor('8'));
        Assert.Null(raw.GlyphFor('9'));
        Assert.NotNull(raw.GlyphFor('7'));
        // Dynamic: it names the font file it fills in from.
        Assert.Equal("Curse Casual", raw.SourceFontFile);
        Assert.True(File.Exists(FontFile), FontFile);

        var font = Shipped().Font;
        foreach (char c in "89+@#<=>{}|")
            Assert.True(font.GlyphFor(c) != null, $"'{c}' should be drawn from the font file");
        // What the export has is still the export's.
        Assert.Same(font.Glyphs.First(g => g.Index == font.Characters.First(x => x.Unicode == '7').GlyphIndex),
                    font.GlyphFor('7'));
    }

    [Fact]
    public void ADrawnDigitHasTheGamesOwnMetrics()
    {
        var game = Shipped().Font;
        var drawn = AllBaked().Font;
        foreach (char c in "01234567")
        {
            var a = game.GlyphFor(c)!.Metrics;
            var b = drawn.GlyphFor(c)!.Metrics;
            _out.WriteLine($"'{c}' game w{a.Width:0.00} h{a.Height:0.00} bx{a.BearingX:0.00} by{a.BearingY:0.00} adv{a.Advance:0.00}"
                           + $" | drawn w{b.Width:0.00} h{b.Height:0.00} bx{b.BearingX:0.00} by{b.BearingY:0.00} adv{b.Advance:0.00}");
            Assert.InRange(b.Advance - a.Advance, -0.5, 0.5);
            Assert.InRange(b.BearingX - a.BearingX, -1, 1);
            Assert.InRange(b.BearingY - a.BearingY, -1, 1);
            Assert.InRange(b.Width - a.Width, -1.5, 1.5);
            Assert.InRange(b.Height - a.Height, -1.5, 1.5);
        }
    }

    // ── Drawn, and compared as drawn ─────────────────────────────────────

    private const int W = 420, H = 90;

    private static double[] Ink(UiFontSet set, string text)
    {
        var layout = TmpTextLayout.Measure(set.Font, text, 38, 0, TextAlign.Left, 0, 0, null);
        var shifted = new TextLayout { Height = layout.Height };
        foreach (var g in layout.Glyphs) shifted.Glyphs.Add(g with { X = g.X + 10, Y = g.Y + 10 });
        var pixels = UiTextRaster.Draw(shifted, set.Font, set.Alpha, set.Width, set.Height, W, H, UiColor.White);
        // The face alone: it is white, and the outline and shadow round it are
        // black, so red is the face. The shadow is a soft blob under every
        // glyph alike, and comparing it would make any two digits look close.
        var ink = new double[W * H];
        for (int i = 0; i < ink.Length; i++) ink[i] = pixels[i * 4 + 2] / 255.0;
        return ink;
    }

    private static double Difference(double[] a, double[] b)
    {
        double diff = 0, ink = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff += Math.Abs(a[i] - b[i]);
            ink += Math.Max(a[i], b[i]);
        }
        return diff / Math.Max(ink, 1);
    }

    [Fact]
    public void DrawnFromTheFontFile_TheDigitsLookAsTheGameBakedThem()
    {
        var game = Ink(Shipped(), "01234567");
        var drawn = Ink(AllBaked(), "01234567");
        double same = Difference(game, drawn);

        // The control: the same eight digits in another order are as unlike
        // as digits of the one font get, and the comparison has to tell.
        double other = Difference(game, Ink(AllBaked(), "76543210"));
        _out.WriteLine($"drawn vs the game's: {same:P1} of the ink differs; in another order: {other:P1}");
        _out.WriteLine($"ink: the game's {game.Sum():0}, drawn {drawn.Sum():0}");

        Assert.True(other > 0.4, $"the comparison should tell digits apart ({other:P1})");
        Assert.True(same < 0.08, $"drawn from the font file, the digits should look as baked ({same:P1})");
        // As heavy as the game's: neither bolder nor thinner.
        Assert.InRange(drawn.Sum() / game.Sum(), 0.98, 1.02);
    }
}
