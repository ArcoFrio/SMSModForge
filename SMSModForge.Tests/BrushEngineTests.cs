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
}
