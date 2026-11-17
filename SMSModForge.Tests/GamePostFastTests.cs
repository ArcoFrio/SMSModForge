using System;
using System.IO;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The fast bloom and flares (GamePostFast, 1.9.1) are the transpiled passes'
/// to the bit: the same frame through both, every value of the result equal.
/// The frames: the shipped room as the camera's target holds it, and a
/// synthetic one with hot spots up to four times white (the flares work on
/// those), and each again with a buffer set kept from a frame of another
/// size (what a preview's <see cref="GamePost.Work"/> carries over).
/// </summary>
public sealed class GamePostFastTests
{
    private readonly ITestOutputHelper _out;
    public GamePostFastTests(ITestOutputHelper o) => _out = o;

    private static GamePost.Target Room()
    {
        var backdrop = PreviewBackdrop.Load(Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "Backdrops", "MyRoom"));
        return BackdropRender.Scene(new BackdropScene(backdrop));
    }

    private static GamePost.Target HotSpots()
    {
        var t = new GamePost.Target(960, 540);
        var rng = new Random(7);
        for (int i = 0; i < t.Px.Length; i += 4) { t.Px[i] = (float)rng.NextDouble() * 0.5f; t.Px[i + 1] = (float)rng.NextDouble() * 0.4f; t.Px[i + 2] = (float)rng.NextDouble() * 0.3f; t.Px[i + 3] = 1f; }
        for (int k = 0; k < 40; k++)
        {
            int cx = rng.Next(960), cy = rng.Next(540), r = rng.Next(2, 30);
            for (int y = Math.Max(0, cy - r); y < Math.Min(540, cy + r); y++)
                for (int x = Math.Max(0, cx - r); x < Math.Min(960, cx + r); x++)
                {
                    int o = (y * 960 + x) * 4;
                    t.Px[o] = 4f * (float)rng.NextDouble(); t.Px[o + 1] = 3f * (float)rng.NextDouble(); t.Px[o + 2] = 2f;
                }
        }
        return t;
    }

    [Theory]
    [InlineData("room", true)]
    [InlineData("room", false)]
    [InlineData("hot", true)]
    [InlineData("hot", false)]
    public void TheFastPassesAreTheTranspiledOnesToTheBit(string frameName, bool flares)
    {
        var frame = frameName == "room" ? Room() : HotSpots();
        var s = new GamePost.Settings();
        var reference = GamePost.BloomAndFlares(frame, s, flares);
        var work = new GamePost.Work();
        // A frame of another size first: the kept buffers must be remade, not misused.
        GamePost.BloomAndFlaresFast(new GamePost.Target(640, 360), s, flares, work);
        var fast = GamePost.BloomAndFlaresFast(frame, s, flares, work);
        var again = GamePost.BloomAndFlaresFast(frame, s, flares, work);
        Assert.Equal(reference.W, fast.W);
        Assert.Equal(reference.H, fast.H);
        int differ = 0;
        for (int i = 0; i < reference.Px.Length; i++) if (BitConverter.SingleToInt32Bits(reference.Px[i]) != BitConverter.SingleToInt32Bits(fast.Px[i])) differ++;
        int differAgain = Enumerable.Range(0, reference.Px.Length).Count(i => reference.Px[i] != again.Px[i]);
        double lit = reference.Px.Where((v, i) => i % 4 < 3 && v > 0f).Count() / (reference.Px.Length * 0.75);
        _out.WriteLine($"{frameName}, flares {flares}: {reference.W}x{reference.H}, {lit:P1} of values lit, {differ} values differ (again: {differAgain})");
        Assert.True(lit > 0.01, "the bloom is all but black - the check would pass for nothing");
        Assert.Equal(0, differ);
        Assert.Equal(0, differAgain);
    }

    /// <summary>The passes store through a half float by the bits (HalfRound) - the
    /// library's own conversion, every float from 2^-30 to past the halves' top,
    /// both signs, every 7th bit pattern (ties to even among them), equal.</summary>
    [Fact]
    public void HalfRoundIsTheLibrarysConversion()
    {
        int differ = 0, checkedCount = 0;
        for (uint bits = 0x30800000u; bits < 0x48000000u; bits += 7)
            foreach (uint sign in new[] { 0u, 0x80000000u })
            {
                float x = BitConverter.UInt32BitsToSingle(bits | sign);
                checkedCount++;
                if (BitConverter.SingleToUInt32Bits(GamePost.HalfRound(x)) != BitConverter.SingleToUInt32Bits((float)(Half)x)) differ++;
            }
        foreach (float x in new[] { 0f, -0f, 1f, 65504f, 65519.99f, 65520f, 6.1035156e-05f, 6.1035152e-05f })
        {
            checkedCount++;
            if (BitConverter.SingleToUInt32Bits(GamePost.HalfRound(x)) != BitConverter.SingleToUInt32Bits((float)(Half)x)) differ++;
        }
        _out.WriteLine($"{checkedCount} floats, {differ} rounded otherwise");
        Assert.Equal(0, differ);
    }

    /// <summary>The control: the comparison sees a difference when there is one -
    /// the flares change the bloom of the hot frame.</summary>
    [Fact]
    public void TheComparisonSeesTheFlares()
    {
        var frame = HotSpots();
        var s = new GamePost.Settings();
        var withFlares = GamePost.BloomAndFlaresFast(frame, s, true, new GamePost.Work());
        var without = GamePost.BloomAndFlaresFast(frame, s, false, new GamePost.Work());
        int differ = Enumerable.Range(0, withFlares.Px.Length).Count(i => withFlares.Px[i] != without.Px[i]);
        Assert.True(differ > withFlares.Px.Length / 100, $"only {differ} values differ with the flares on");
    }
}
