using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// An NPC's wet drops as the game makes them (WetDrops; after 1.10.0, the author: "a proper preview of the wet
/// effects"). The numbers are the game's own, read in the test copy on 2026-11-27 (InGameSuiteTests.S17's live
/// drops: three Wet objects, one under a body twice the size, one turned 45 degrees, one scaled 2 x 0.5).
/// </summary>
public sealed class WetDropsTests
{
    private static readonly WetDrops.Emitter Plain = new(500, 300, 1, 0, 0, -1, 1, 1, 7);

    private static List<WetDrops.Drop> At(double t, WetDrops.Emitter e)
    {
        var list = new List<WetDrops.Drop>();
        WetDrops.At(t, e, list, double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity);
        return list;
    }

    /// <summary>The colour over life the game drew its drops with, at the ages it gave them: born every half second,
    /// at 0.4265 s 151 of 255, at 4.4265 s 243, at 4.9265 s 31, between 1 and 4 s fully white.</summary>
    [Theory]
    [InlineData(0.42650, 0x97)]
    [InlineData(0.92650, 0xFF)]
    [InlineData(3.92650, 0xFF)]
    [InlineData(4.42650, 0xF3)]
    [InlineData(4.92650, 0x1F)]
    public void A_drop_fades_in_and_out_as_the_games_did(double age, int alpha)
        => Assert.InRange(WetDrops.AlphaOver(age / WetDrops.Life) * 255, alpha - 1.5, alpha + 1.5);

    /// <summary>Ten drops of the towel's first system out at any moment past the first five seconds (the game had ten
    /// in each of its three), born half a second apart, and thirty of its second (Anna_Towel's "Particle System (1)":
    /// six a second, twice as wide); none before the system started.</summary>
    [Fact]
    public void Ten_drops_are_out_born_half_a_second_apart()
    {
        double t = 3.9265 + 10 * WetDrops.Life;
        var drops = At(t, Plain);
        Assert.Equal(10, drops.Count(d => d.W == WetDrops.DropW * 100));
        Assert.Equal(30, drops.Count(d => d.W == WetDrops.TowelSecond.SizeXMin * 100));
        Assert.Equal(40, drops.Count);
        Assert.Equal(2 + 5, At(0.75, Plain).Count);   // born at 0 and at 0.5; at 0, 1/6 ... 4/6
    }

    /// <summary>The towel's second system: its box as tall as the first's and lower by 2.04 of its 8.45 (Anna's own,
    /// 0.51 under her body scaled 4), every drop 0.08 x 0.8.</summary>
    [Fact]
    public void The_towels_second_drops_start_in_a_box_lower_by_a_quarter()
    {
        for (double t = 6; t < 9; t += 0.41)
            foreach (var d in At(t, Plain).Where(d => d.W == WetDrops.TowelSecond.SizeXMin * 100))
            {
                Assert.Equal(80, d.H, 9);
                var a = d.Alpha;
                Assert.True(a > 0);
            }
        var (_, _, bottom, top) = WetDrops.Extent(Plain);
        Assert.Equal(WetDrops.BoxH / 2, top, 9);
        Assert.Equal(-WetDrops.BoxH / 2 - 2.04, bottom, 9);
    }

    /// <summary>The shower (5_Levels/4_Bath/AnnaInShower's): 200 drops a second living two seconds, prewarmed - four
    /// hundred out from the start - each its own size and colour between the game's two, solid and lit; from its top
    /// side only and gone at its floor when asked.</summary>
    [Fact]
    public void A_shower_is_four_hundred_drops_and_keeps_to_its_box_when_asked()
    {
        var shower = new WetDrops.Emitter(500, 300, 1, 0, 0, -1, 1, 1, 5, AreaW: 2, AreaH: 1, Of: WetDrops.Kind.Shower);
        var drops = At(0.5, shower);
        Assert.Equal(400, drops.Count);
        Assert.All(drops, d =>
        {
            Assert.True(d.Solid && d.Lit);
            Assert.InRange(d.W, 0.5 - 1e-9, 1 + 1e-9);
            Assert.InRange(d.H, 20 - 1e-9, 50 + 1e-9);
            Assert.InRange(d.B, 0x6F / 255f - 1e-6, 0xA4 / 255f + 1e-6);
        });
        // A box 6 units tall, from its top, with a floor: none's lower end below the floor, and the top ones at the top.
        var kept = shower with { AreaH = 6, FromTop = true, Floor = true };
        var inside = At(3.7, kept);
        double floor = 300 + 3 * 100, topLine = 300 - 3 * 100;
        Assert.NotEmpty(inside);
        Assert.All(inside, d => Assert.True(d.Y + d.H / 2 <= floor + 1e-6, $"a drop ends at {d.Y + d.H / 2:0.##}, under the floor at {floor}"));
        Assert.All(inside, d => Assert.True(d.Y >= topLine - 1e-6, $"a drop is at {d.Y:0.##}, over the top at {topLine}"));
        // Control: without the floor, drops end under it.
        Assert.Contains(At(3.7, kept with { Floor = false }), d => d.Y + d.H / 2 > floor);
    }

    /// <summary>
    /// Where a drop is: where it started in the box round the Wet object - the box turned with it and scaled by its
    /// own scale only - and below that by half of 9.81 units a second squared times its age squared, straight down
    /// whatever the turn. Read from the game: at age 1.4265 a drop of the plain system was 9.98 units below where it
    /// started, at 0.9265 one of the turned system 4.21 straight down.
    /// </summary>
    [Fact]
    public void A_drop_falls_straight_down_from_where_it_started_in_the_box()
    {
        double turn = 45 * Math.PI / 180;
        // Canvas: y down. The turned system's own x and y (up) on the canvas.
        var turned = new WetDrops.Emitter(800, 400, Math.Cos(turn), -Math.Sin(turn), -Math.Sin(turn), -Math.Cos(turn), 2, 0.5, 11);
        foreach (var e in new[] { Plain, turned })
            for (double t = 1.0; t < 12; t += 0.37)
            {
                // The first system's (the second's are its twice as wide, held to their own box above).
                var drops = At(t, e).Where(d => Math.Abs(d.W - WetDrops.DropW * 100 * Math.Abs(e.ScaleX)) < 1e-9).ToList();
                long last = (long)Math.Floor(t * WetDrops.Rate);
                foreach (var d in drops)
                {
                    // Which drop it is: its age from its alpha is ambiguous, so try each born in the last five seconds.
                    bool found = false;
                    for (long k = last; k > last - 10 && !found; k--)
                    {
                        double age = t - k / WetDrops.Rate;
                        var (bx, by) = WetDrops.Spawn(e.Seed, k);
                        double lx = bx * WetDrops.BoxW * e.ScaleX * 100, ly = by * WetDrops.BoxH * e.ScaleY * 100;
                        double sx = e.X + lx * e.AxX + ly * e.AyX, sy = e.Y + lx * e.AxY + ly * e.AyY;
                        if (Math.Abs(d.X - sx) < 1e-6 && Math.Abs(d.Y - (sy + 0.5 * 9.81 * 100 * age * age)) < 1e-6) found = true;
                    }
                    Assert.True(found, $"a drop at ({d.X:0.##}, {d.Y:0.##}) is not one started in the box and fallen straight down");
                }
            }
        Assert.Equal(998.1, 0.5 * WetDrops.Gravity * WetDrops.PixelsPerUnit * 1.4265 * 1.4265, 1);
    }

    /// <summary>Drops start anywhere in the box, and nowhere outside it: 3.21 x 8.45 units times the Wet object's
    /// own scale.</summary>
    [Fact]
    public void Drops_start_all_over_the_box_and_only_in_it()
    {
        double minX = 1, maxX = -1, minY = 1, maxY = -1;
        for (long k = 0; k < 4000; k++)
        {
            var (x, y) = WetDrops.Spawn(3, k);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        Assert.InRange(minX, -0.5, -0.49); Assert.InRange(maxX, 0.49, 0.5);
        Assert.InRange(minY, -0.5, -0.49); Assert.InRange(maxY, 0.49, 0.5);
        var corners = WetDrops.Box(new WetDrops.Emitter(0, 0, 1, 0, 0, -1, 2, 0.5, 1));
        Assert.Equal(-321, corners[0].X, 6);
        Assert.Equal(-211.25, corners[0].Y, 6);   // up is -y on the canvas
    }

    /// <summary>A drop is 0.04 x 0.5 units: 4 x 50 canvas pixels, 2 x 25 on the game's 960x540 test screen (the
    /// game's streak was 2 pixels wide and 25 tall); the scaled system's 8 x 25 (4 x 12.5 there).</summary>
    [Fact]
    public void A_drop_is_a_thin_streak_scaled_by_its_own_object_only()
    {
        var d = At(7.3, Plain).First();
        Assert.Equal(4, d.W, 9); Assert.Equal(50, d.H, 9);
        var s = At(7.3, Plain with { ScaleX = 2, ScaleY = 0.5 }).First();
        Assert.Equal(8, s.W, 9); Assert.Equal(25, s.H, 9);
    }

    /// <summary>
    /// The copy of the texture a drop is read from, as a card picks it: on the game's 960x540 screen a 2-pixel streak
    /// from the 2x2 copy (the game's streak was as bright all along, 99-108 over 16-52), at 1080p from the 4x4, the
    /// scaled one's 4 x 12.5 pixels from the 4x4. Each copy the mean of the one before.
    /// </summary>
    [Fact]
    public void A_drop_is_read_from_the_copy_its_size_on_screen_calls_for()
    {
        var m = WetDrops.Texture;
        Assert.NotNull(m);
        Assert.Equal(64, m!.Full);
        Assert.Equal(7, m.Levels);
        Assert.Equal(5, m.LevelFor(2, 25));
        Assert.Equal(4, m.LevelFor(4, 50));
        Assert.Equal(4, m.LevelFor(4, 12.5f));
        Assert.Equal(0, m.LevelFor(200, 200));
        // The 2x2 copy: each texel the mean of a quarter of the full picture.
        double quarter = 0;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++) quarter += m.Alpha[y * 64 + x];
        Assert.Equal(quarter / 1024, m.Alpha[m.At[5]], 4);
        // The game's 2-pixel streak's alpha, from the colours it drew over the dark floor in linear light: about 0.13.
        Assert.InRange(m.Sample(5, 0.25f, 0.5f), 0.11, 0.15);
    }

    /// <summary>
    /// A drop is laid over the lit place in linear light, white through its alpha: the game's streak over its floor
    /// (16, 49, 52) came out (103, 111, 111) - one alpha, 0.13, in all three colours, which sRGB blending cannot give.
    /// </summary>
    [Fact]
    public void A_drop_is_blended_in_linear_light()
    {
        var m = WetDrops.Texture!;
        static float Lin(int b) { double c = b / 255.0; return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)); }
        static int Srgb(float l) { double c = l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055; return (int)Math.Round(c * 255); }
        float r = Lin(16), g = Lin(49), b = Lin(52);
        var drop = new WetDrops.Drop { X = 10, Y = 10, W = 4, H = 50, Alpha = 1f };
        WetDrops.Blend(ref r, ref g, ref b, 11f, 10f, 0.5f, new[] { drop }, 1, m);
        Assert.InRange(Srgb(r), 103 - 8, 103 + 8);
        Assert.InRange(Srgb(g), 111 - 8, 111 + 8);
        Assert.InRange(Srgb(b), 111 - 8, 111 + 8);
        // Not where no drop is.
        float r2 = Lin(16), g2 = Lin(49), b2 = Lin(52);
        WetDrops.Blend(ref r2, ref g2, ref b2, 30f, 10f, 0.5f, new[] { drop }, 1, m);
        Assert.Equal(16, Srgb(r2));
    }
}
