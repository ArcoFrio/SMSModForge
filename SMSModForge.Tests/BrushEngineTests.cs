using System;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The mask painter's brush (1.9.0, made to feel like Paint.NET's), measured in
/// what it paints: a hard edge is antialiased, hardness is the solid share of
/// the radius, a stroke's edge is smooth along its whole length, and a dab sits
/// under the pointer. Each with a control that must come out the other way.
/// </summary>
public sealed class BrushEngineTests
{
    private readonly ITestOutputHelper _out;
    public BrushEngineTests(ITestOutputHelper o) => _out = o;

    private static float[] Buffer() => new float[MaskBuffer.Size * MaskBuffer.Size];
    private static float At(float[] b, int x, int y) => b[y * MaskBuffer.Size + x];

    [Fact]
    public void AHardBrushHasAnAntialiasedEdge_NotStairSteps()
    {
        var b = Buffer();
        BrushEngine.Stamp(b, 100.5f, 100.5f, 10f, 1f, 1f);
        // Pixel centres along the row through the centre: 0, 1, ... 12 away.
        var row = Enumerable.Range(0, 13).Select(d => At(b, 100 + d, 100)).ToArray();
        _out.WriteLine(string.Join(" ", row.Select(v => v.ToString("0.00"))));
        Assert.Equal(1f, row[9]);                 // inside: solid
        Assert.InRange(row[10], 0.4f, 0.6f);      // on the rim: half covered
        Assert.Equal(0f, row[11]);                // outside: nothing
        // The control: the inside of a hard brush is flat, no fade before the rim.
        Assert.All(row.Take(10), v => Assert.Equal(1f, v));
    }

    [Fact]
    public void HardnessIsTheSolidShareOfTheRadius_ThenASmoothFade()
    {
        var half = Buffer();
        BrushEngine.Stamp(half, 100.5f, 100.5f, 20f, 0.5f, 1f);
        var row = Enumerable.Range(0, 22).Select(d => At(half, 100 + d, 100)).ToArray();
        _out.WriteLine("50%: " + string.Join(" ", row.Select(v => v.ToString("0.00"))));
        Assert.All(row.Take(11), v => Assert.Equal(1f, v));            // 0..10 of a 20 radius: solid
        for (int d = 11; d < 21; d++) Assert.True(row[d] < row[d - 1], $"fades at {d}");
        Assert.Equal(0f, row[21]);

        // At 0% the fade starts at the centre: only the centre is full.
        var soft = Buffer();
        BrushEngine.Stamp(soft, 100.5f, 100.5f, 20f, 0f, 1f);
        _out.WriteLine("0%: " + string.Join(" ", Enumerable.Range(0, 22).Select(d => At(soft, 100 + d, 100).ToString("0.00"))));
        Assert.Equal(1f, At(soft, 100, 100));
        Assert.True(At(soft, 102, 100) < 1f);
        Assert.True(At(soft, 110, 100) < At(half, 110, 100));            // softer than 50% everywhere in between
    }

    /// <summary>
    /// A stroke's edge is as smooth as the brush's: the same distance from the
    /// path paints the same amount all along it. Until 1.9.0 a stroke was a
    /// chain of discs snapped to whole pixels, and its soft edge came out beaded.
    /// </summary>
    [Fact]
    public void AStrokesEdgeIsTheSameAllAlongIt()
    {
        var b = Buffer();
        // Drawn in pieces, the way the pointer moves, from between pixels.
        float[] xs = { 10.3f, 43.9f, 81.2f, 130.6f, 170.1f, 210.8f };
        for (int i = 1; i < xs.Length; i++)
            BrushEngine.StampLine(b, xs[i - 1], 120.5f, xs[i], 120.5f, 6f, 0.3f, 1f);

        foreach (int off in new[] { 0, 2, 4, 5, 6 })
        {
            var along = Enumerable.Range(30, 160).Select(x => At(b, x, 120 + off)).ToArray();
            float spread = along.Max() - along.Min();
            _out.WriteLine($"{off} px from the path: {along.Min():0.0000}-{along.Max():0.0000}");
            Assert.True(spread < 1e-5f, $"{off} px from the path varies by {spread}");
        }
        // The control: across the stroke it does change, so the measure sees a fade.
        Assert.True(At(b, 100, 120) > At(b, 100, 124) && At(b, 100, 124) > At(b, 100, 126));
    }

    [Fact]
    public void ADabSitsUnderThePointer()
    {
        var b = Buffer();
        BrushEngine.Stamp(b, 50.5f, 60.5f, 4f, 0.2f, 1f);     // the centre of pixel (50, 60)
        Assert.Equal(At(b, 49, 60), At(b, 51, 60));
        Assert.Equal(At(b, 50, 59), At(b, 50, 61));
        Assert.True(At(b, 50, 60) > At(b, 51, 60));

        // The control: a pointer at the corner between four pixels paints them alike, and none fully.
        var corner = Buffer();
        BrushEngine.Stamp(corner, 51f, 61f, 4f, 0.2f, 1f);
        Assert.Equal(At(corner, 50, 60), At(corner, 51, 61));
        Assert.Equal(At(corner, 51, 60), At(corner, 50, 61));
        Assert.True(At(corner, 51, 61) > At(corner, 53, 61));
    }

    // ───────────────────────── the soften brush (1.9.1)

    /// <summary>A hard edge softened to N comes out a fade N pixels wide, from 2%
    /// to 98%, for every N on the slider's way. A softness of 1 is the control
    /// at the other end: next to nothing changes.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void ASoftenedHardEdgeFadesAcrossTheWidthAsked(int width)
    {
        int size = MaskBuffer.Size;
        var layer = new byte[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size / 2; x++)
                layer[y * size + x] = 255;

        var softened = BrushEngine.Softened(layer, size, width);
        var row = Enumerable.Range(0, size).Select(x => softened[128 * size + x]).ToArray();
        int fade = row.Count(v => v > 0.02f * 255 && v < 0.98f * 255);
        _out.WriteLine($"{width}: {fade} part way - " + string.Join(" ", row.Skip(size / 2 - 6).Take(12).Select(v => v.ToString("0"))));
        Assert.Equal(width, fade);
        // Mirror-even about the edge, and no row differs from another.
        Assert.Equal(255.0 - row[size / 2 - 1], (double)row[size / 2], 3);
        Assert.Equal((double)row[size / 2 - 3], (double)softened[5 * size + size / 2 - 3], 3);

        var barely = BrushEngine.Softened(layer, size, 1);
        Assert.Equal(0, Enumerable.Range(0, size).Count(x => barely[128 * size + x] > 0.02f * 255 && barely[128 * size + x] < 0.98f * 255));
    }

    /// <summary>Softening leaves a layer of one strength as it was, right up to
    /// the mask's border - only the pixels that exist are counted there. The
    /// control: one strong pixel is spread out, and keeps its total.</summary>
    [Fact]
    public void SofteningLeavesAnEvenLayerAlone_EvenAtTheBorder()
    {
        int size = MaskBuffer.Size;
        var even = Enumerable.Repeat((byte)200, size * size).ToArray();
        var softened = BrushEngine.Softened(even, size, 12);
        Assert.All(new[] { 0, size - 1, 3 * size + 2, size * size - 1, size * (size - 1) }, i => Assert.Equal(200.0, (double)softened[i], 3));

        var dot = new byte[size * size];
        dot[100 * size + 100] = 255;
        var spread = BrushEngine.Softened(dot, size, 12);
        Assert.True(spread[100 * size + 100] < 255 && spread[100 * size + 103] > 0);
        Assert.Equal(255.0, spread.Sum(v => (double)v), 1);
    }

    /// <summary>Where the brush covers fully a pixel lands on the softened layer,
    /// and a second pass from there changes nothing; half-covered it goes half
    /// way. The control: a pixel the stroke does not reach stays as it was.</summary>
    [Fact]
    public void TheSoftenStepLandsOnItsTarget_AndStaysThere()
    {
        int size = MaskBuffer.Size;
        var original = new byte[size * size];
        original[0] = 0; original[1] = 200; original[2] = 100; original[3] = 50;
        var target = new float[size * size];
        target[0] = 100.4f; target[1] = 100.6f; target[2] = 200f; target[3] = 0f;
        var stroke = new float[size * size];
        stroke[0] = 1f; stroke[1] = 1f; stroke[2] = 0.5f;   // pixel 3 not reached

        var channel = (byte[])original.Clone();
        BrushEngine.ApplySoften(channel, original, target, stroke, 0, 0, 3, 0);
        Assert.Equal(new byte[] { 100, 101, 150, 50 }, channel.Take(4).ToArray());

        var again = (byte[])channel.Clone();
        var second = (byte[])channel.Clone();
        stroke[2] = 1f;
        BrushEngine.ApplySoften(second, again, target, stroke, 0, 0, 1, 0);
        Assert.Equal(new byte[] { 100, 101 }, second.Take(2).ToArray());
    }
}
