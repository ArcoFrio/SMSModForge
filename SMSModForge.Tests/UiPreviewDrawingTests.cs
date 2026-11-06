using System;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What the UI preview draws, for each thing the game's screenshots showed it had
/// wrong (1.8.3). The comparison with the game itself is F10 to F12 in the in-game
/// suite, which is run before a release; these hold the same ground in the tier
/// that runs all the time. Each measures the picture and has a control.
/// </summary>
public sealed class UiPreviewDrawingTests
{
    private readonly ITestOutputHelper _out;
    public UiPreviewDrawingTests(ITestOutputHelper output) => _out = output;

    private const int W = 800, H = 600;

    private static UiNodeDef Screen(params UiNodeDef[] inside)
    {
        var root = new UiNodeDef
        {
            Name = "Root",
            Rect = new UiRectDef { AnchorMin = new[] { 0f, 0f }, AnchorMax = new[] { 1f, 1f }, Size = new[] { 0f, 0f } },
        };
        root.Children.AddRange(inside);
        return root;
    }

    private static UiNodeDef Box(string name, float w, float h, float turn = 0f, float scale = 1f) => new()
    {
        Name = name,
        Rect = new UiRectDef { Size = new[] { w, h }, RotationZ = turn, Scale = new[] { scale, scale } },
        Image = new UiImageDef { Sprite = "Semi Rounded", Type = "Sliced", Tint = "#FFFFFFFF" },
    };

    private static byte[] Draw(UiNodeDef root) => UiAuthoredRenderer.Render(root, W, H, VanillaUiLibrary.Assets);

    /// <summary>The box round everything drawn: width, height, and where its
    /// left and right fall.</summary>
    private static (int Width, int Height, int Left, int Right) Lit(byte[] pixels)
    {
        int minX = W, minY = H, maxX = -1, maxY = -1;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (pixels[(y * W + x) * 4 + 3] > 40)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        return maxX < 0 ? (0, 0, 0, 0) : (maxX - minX + 1, maxY - minY + 1, minX, maxX);
    }

    private static void Near(int expected, int actual, int within = 3)
        => Assert.InRange(actual, expected - within, expected + within);

    [Fact]
    public void A_turned_object_is_drawn_turned_with_what_is_inside_it()
    {
        if (!VanillaUiLibrary.IsAvailable) return;

        var level = Lit(Draw(Screen(Box("Bar", 300, 40))));
        Near(300, level.Width);    // the control
        Near(40, level.Height);

        var turned = Lit(Draw(Screen(Box("Bar", 300, 40, turn: 90f))));
        _out.WriteLine($"level {level}, turned {turned}");
        Near(40, turned.Width);
        Near(300, turned.Height);
    }

    [Fact]
    public void One_that_stays_upright_stays_level_while_its_parent_turns()
    {
        if (!VanillaUiLibrary.IsAvailable) return;

        UiNodeDef Parent(bool upright)
        {
            var parent = new UiNodeDef { Name = "Turns", Rect = new UiRectDef { Size = new[] { 10f, 10f }, RotationZ = 90f } };
            var bar = Box("Bar", 300, 40);
            bar.Upright = upright;
            parent.Children.Add(bar);
            return parent;
        }

        var along = Lit(Draw(Screen(Parent(false))));
        Near(40, along.Width);     // the control: it turned with its parent
        var level = Lit(Draw(Screen(Parent(true))));
        _out.WriteLine($"turned with it {along}, upright {level}");
        Near(300, level.Width);
        Near(40, level.Height);
    }

    [Fact]
    public void A_picture_keeping_its_shape_is_as_large_as_fits_and_goes_by_its_pivot()
    {
        var (x, y, w, h) = UiSlice.FitShape(200, 60, 256, 256, 0.5, 0.5);
        Assert.Equal(60, w, 3);
        Assert.Equal(60, h, 3);
        Assert.Equal(70, x, 3);
        Assert.Equal(0, y, 3);

        // Pivot at the left: the spare room goes to the right, as Unity's rule says.
        (x, _, _, _) = UiSlice.FitShape(200, 60, 256, 256, 0, 0.5);
        Assert.Equal(0, x, 3);

        // Pivot at the bottom, of a box too tall. In drawing coordinates y runs down,
        // so all the room is above it.
        (_, y, w, h) = UiSlice.FitShape(60, 200, 256, 256, 0.5, 0);
        Assert.Equal(60, h, 3);
        Assert.Equal(140, y, 3);

        if (!VanillaUiLibrary.IsAvailable) return;
        UiNodeDef Icon(bool keep) => new()
        {
            Name = "Icon", Rect = new UiRectDef { Size = new[] { 200f, 60f } },
            Image = new UiImageDef { Sprite = "Close", Type = "Simple", PreserveAspect = keep },
        };
        Near(200, Lit(Draw(Screen(Icon(false)))).Width);    // the control
        Near(60, Lit(Draw(Screen(Icon(true)))).Width);
    }

    [Fact]
    public void A_tiled_picture_repeats_at_its_own_size_from_the_bottom_left()
    {
        // Two by two: blue, green over red, white (rows from the top, BGRA).
        byte[] source =
        {
            255, 0, 0, 255,   0, 255, 0, 255,
            0, 0, 255, 255,   255, 255, 255, 255,
        };
        var tiled = UiSlice.Tile(source, 2, 2, default, 5, 3);
        (byte B, byte G, byte R) At(int x, int y) { int i = (y * 5 + x) * 4; return (tiled[i], tiled[i + 1], tiled[i + 2]); }

        // The bottom row of the target is the sprite's bottom row, starting on its left.
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(0, 2));
        Assert.Equal(((byte)255, (byte)255, (byte)255), At(1, 2));
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(2, 2));          // repeated across
        Assert.Equal(((byte)255, (byte)0, (byte)0), At(0, 1));          // and up
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(0, 0));
    }

    [Fact]
    public void A_trimmed_sprite_covers_only_what_it_kept_of_its_box()
    {
        // The game's own check mark: 256 across, with this much trimmed each side.
        var sprite = new UiSprite(new byte[4], 1, 1, 256, 256, new[] { 59.0, 67.0, 59.0, 70.0 });
        var area = UiSlice.DrawnArea(new UiRect(0, 0, 55, 55), sprite, false, false, false, 1, 0.5, 0.5);
        _out.WriteLine($"{area.X},{area.Y} {area.Width}x{area.Height}");
        Assert.Equal(59 * 55.0 / 256, area.X, 3);
        Assert.Equal(70 * 55.0 / 256, area.Y, 3);        // the top, in drawing coordinates
        Assert.Equal((256 - 118) * 55.0 / 256, area.Width, 3);

        // The control: one with nothing trimmed covers all of it.
        var whole = UiSlice.DrawnArea(new UiRect(0, 0, 55, 55), new UiSprite(new byte[4], 1, 1), false, false, false, 1, 0.5, 0.5);
        Assert.Equal(55, whole.Width, 3);
    }

    private static UiNodeDef Words(string value, float w, float h, bool wrap = true, string alignment = "Center", float size = 30f) => new()
    {
        Name = "Words",
        Rect = new UiRectDef { Size = new[] { w, h } },
        Text = new UiTextDef { Value = value, Font = "Curse Casual SDF", Size = size, Color = "#000000FF", Alignment = alignment, Wrap = wrap },
    };

    [Fact]
    public void Words_shrink_with_their_parent_and_break_where_their_own_width_does()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        const string line = "A line of words";

        var full = Lit(Draw(Screen(Words(line, 500, 80))));
        Assert.True(full.Width > 100);     // the control: there is something to measure

        var parent = new UiNodeDef { Name = "Half", Rect = new UiRectDef { Size = new[] { 500f, 80f }, Scale = new[] { 0.5f, 0.5f } } };
        parent.Children.Add(Words(line, 500, 80));
        var half = Lit(Draw(Screen(parent)));
        _out.WriteLine($"full {full}, under half scale {half}");
        Near(full.Width / 2, half.Width, 6);
    }

    [Fact]
    public void Unwrapped_words_run_past_their_box_and_stay_lined_up_in_it()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        const string line = "A long line of words that does not fit its box";

        var wrapped = Lit(Draw(Screen(Words(line, 100, 300))));
        Assert.True(wrapped.Width < 140, $"wrapped to {wrapped.Width}");    // the control

        var centred = Lit(Draw(Screen(Words(line, 100, 40, wrap: false))));
        _out.WriteLine($"wrapped {wrapped}, unwrapped {centred}");
        Assert.True(centred.Width > 300, $"cut off at {centred.Width}");
        Near(W / 2, (centred.Left + centred.Right) / 2, 8);     // about the box's middle, not its edge

        var left = Lit(Draw(Screen(Words(line, 100, 40, wrap: false, alignment: "Left"))));
        Near(W / 2 - 50, left.Left, 6);
    }

    [Fact]
    public void Shrink_to_fit_picks_the_largest_size_that_fits()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        var set = VanillaUiLibrary.Assets.Font("Curse Casual SDF");
        Assert.NotNull(set);
        const string many = "A great many words that will not fit the box at the largest size allowed here";

        double squeezed = UiTextBlock.FitSize(set!.Font, many, default, 300, 80, true, 12, 60, 0, 0, null);
        Assert.InRange(squeezed, 12.01, 59.99);

        // It fits at that size, and the next step up does not.
        double Height(double size) => TmpTextLayout.Measure(set.Font, many, size, 300, TextAlign.Left).Height;
        Assert.True(Height(squeezed) - 8 <= 80, $"{Height(squeezed)} at {squeezed}");
        Assert.True(Height(squeezed + 1) > 80, $"{Height(squeezed + 1)} at {squeezed + 1}");

        // A few words go to the top of the range, and too many stop at the bottom.
        Assert.Equal(60, UiTextBlock.FitSize(set.Font, "Few", default, 300, 80, true, 12, 60, 0, 0, null));
        Assert.Equal(12, UiTextBlock.FitSize(set.Font, string.Concat(Enumerable.Repeat(many, 20)), default, 100, 20, true, 12, 60, 0, 0, null));
    }

    [Fact]
    public void Words_have_no_shadow_or_outline_and_a_picture_does()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        var effect = new UiEffectDef { Color = "#FF0000FF", Distance = new[] { 6f, -6f } };

        var bare = Draw(Screen(Words("Words", 300, 80)));
        var withShadow = Words("Words", 300, 80);
        withShadow.Shadow = effect;
        withShadow.Outline = effect;
        Assert.Equal(bare, Draw(Screen(withShadow)));

        var plainBox = Draw(Screen(Box("Bar", 100, 100)));
        var shadowed = Box("Bar", 100, 100);
        shadowed.Shadow = effect;
        Assert.NotEqual(plainBox, Draw(Screen(shadowed)));     // the control
    }

    [Fact]
    public void Letter_spacing_goes_between_letters_so_a_centred_line_stays_centred()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        var set = VanillaUiLibrary.Assets.Font("Curse Casual SDF")!;
        var plain = TmpTextLayout.Measure(set.Font, "AB", 30, 0, TextAlign.Left);
        var spaced = TmpTextLayout.Measure(set.Font, "AB", 30, 0, TextAlign.Left, 0, 100);

        // One gap between two letters, not two: 100 units is the font's size.
        Assert.Equal(plain.Width + 30, spaced.Width, 1);
    }

    [Fact]
    public void The_games_label_styles_are_drawn()
    {
        Assert.Equal(new UiTextStyle(true, false, false, false, true, false, false), UiTextStyle.Parse("Bold, UpperCase"));
        Assert.Equal(new UiTextStyle(false, false, true, false, false, false, true), UiTextStyle.Parse("Underline, SmallCaps"));
        Assert.Equal(default, UiTextStyle.Parse("Normal"));
        Assert.Equal("QUIT", UiTextStyle.Parse("UpperCase").Cased("Quit"));
        Assert.Equal("Quit", UiTextStyle.Parse("Bold").Cased("Quit"));

        var runs = UiTextStyle.Parse("SmallCaps").SmallCapRuns("Quit");
        Assert.Equal(2, runs.Count);
        Assert.Equal("Q", runs[0].Text);
        Assert.Equal(1, runs[0].SizeScale);
        Assert.Equal("UIT", runs[1].Text);
        Assert.Equal(0.8, runs[1].SizeScale);

        if (!VanillaUiLibrary.IsAvailable) return;
        var set = VanillaUiLibrary.Assets.Font("Curse Casual SDF")!;
        var plain = UiTextBlock.Measure(set.Font, "Words", default, 30, 0, TextAlign.Left, 0, 0, null);
        var bold = UiTextBlock.Measure(set.Font, "Words", UiTextStyle.Parse("Bold"), 30, 0, TextAlign.Left, 0, 0, null);
        Assert.True(bold.Width > plain.Width + 5, $"bold {bold.Width}, plain {plain.Width}");   // spaced further apart
    }

    [Fact]
    public void An_object_the_game_rests_invisible_is_not_drawn()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        var box = Box("Hotspot", 100, 100);
        Assert.True(Lit(Draw(Screen(box))).Width > 90);      // the control

        box.GameTint = "#FFFFFF00";
        Assert.Equal(0, Lit(Draw(Screen(box))).Width);
    }

    [Fact]
    public void The_tint_a_game_object_rests_at_multiplies_its_own_colour()
    {
        var rested = UiGameLook.Rested(new UiColor(255, 128, 64, 255), "#00000080");
        Assert.Equal(0, rested.R);
        Assert.Equal(128, rested.A);

        Assert.Equal(UiColor.White, UiGameLook.Rested(UiColor.White, ""));   // the control: nothing to multiply by
    }

    [Fact]
    public void A_copy_of_a_game_screen_has_none_of_what_the_games_objects_bring()
    {
        // The plugin builds a copy itself: no button behind it, no font style.
        if (!VanillaUiLibrary.IsAvailable) return;
        var vanilla = VanillaUiLibrary.Node(VanillaUiCatalog.Find("vanillaui:9_MainCanvas/Quitagme"));
        if (vanilla == null) return;

        var seeded = VanillaUiSeed.FromBase(vanilla);
        Assert.Contains(Flatten(seeded), n => n.GameTint.Length > 0 || n.Text?.GameStyle is { Length: > 0 });   // the control

        var copied = VanillaUiSeed.CopyOf(vanilla);
        Assert.DoesNotContain(Flatten(copied), n => n.GameTint.Length > 0 || n.Text?.GameStyle is { Length: > 0 });
    }

    private static System.Collections.Generic.IEnumerable<UiNodeDef> Flatten(UiNodeDef node)
        => new[] { node }.Concat(node.Children.SelectMany(Flatten));

    [Fact]
    public void A_font_with_a_coloured_atlas_is_read_as_one()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        var outlined = VanillaUiLibrary.Assets.Font("Alata-Regular-Outline 120 SDF");
        Assert.NotNull(outlined);
        Assert.True(outlined!.Font.IsColourAtlas);
        Assert.NotNull(outlined.Colour);

        // Its letters have a white face and a black outline in the atlas itself.
        var colour = outlined.Colour!;
        bool white = false, black = false;
        for (int i = 0; i + 3 < colour.Length && !(white && black); i += 4)
        {
            if (colour[i + 3] < 200) continue;
            if (colour[i] > 240 && colour[i + 1] > 240 && colour[i + 2] > 240) white = true;
            if (colour[i] < 15 && colour[i + 1] < 15 && colour[i + 2] < 15) black = true;
        }
        Assert.True(white && black, $"white {white}, black {black}");

        // The control: an ordinary font is a distance field, with no colours.
        var plain = VanillaUiLibrary.Assets.Font("Curse Casual SDF")!;
        Assert.False(plain.Font.IsColourAtlas);
        Assert.Null(plain.Colour);
    }

    [Fact]
    public void A_letter_a_font_lacks_comes_from_the_last_resort_font()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        var assets = VanillaUiLibrary.Assets;
        var set = assets.Font("Vegur-Regular SDF")!;
        Assert.False(set.Font.Has('’'));         // the control: it really lacks it

        var laid = TmpTextLayout.Measure(set.Font, "Don’t", 24, 0, TextAlign.Left, 0, 0, UiTextBlock.Fallbacks(set, assets));
        Assert.Empty(laid.Missing);
        Assert.Contains(laid.Glyphs, g => g.Glyph.Owner?.Name == UiTextBlock.LastResortFont);
        Assert.Contains(laid.Glyphs, g => ReferenceEquals(g.Glyph.Owner, set.Font));
    }

    [Fact]
    public void An_old_style_label_is_drawn_from_the_asset_of_its_font()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        Assert.NotNull(VanillaUiLibrary.Assets.Font("Curse Casual"));     // names a font file, not an asset
        Assert.Null(VanillaUiLibrary.Assets.Font("No Such Font At All"));  // the control
    }
}
