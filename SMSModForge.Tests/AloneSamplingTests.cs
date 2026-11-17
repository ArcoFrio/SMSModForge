using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The preview qualities with no backdrop (1.9.1, the author: "we still want the
/// graphics options to affect the performance on no backdrops, for both
/// Characters, and NPCs"): each samples a bust or an NPC drawn alone less finely
/// than the one above (PreviewQualityDef.Sampling) - Ultra the game's two by two
/// in each preview pixel, High two rows, Medium one, Low one per two by two
/// pixels. Measured on what is drawn - the edge shades an art with hard edges
/// takes, the blocks Low leaves - and on the work it takes the processor.
/// </summary>
[Collection("AloneSampling")]
[Trait("Timing", "Alone")]  // measures the processor's work a frame: run by a tester alone (run-tests.ps1)
public sealed class AloneSamplingTests
{
    private readonly ITestOutputHelper _out;
    public AloneSamplingTests(ITestOutputHelper o) => _out = o;

    private const int S = 256, O = 512;

    /// <summary>A hard-edged disc (alpha 0 or 255 only), moved by the wave.</summary>
    private static (byte[] art, byte[] mask) Disc()
    {
        var art = new byte[S * S * 4];
        var mask = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int i = (y * S + x) * 4;
                double dx = x - 128.3, dy = y - 127.6;
                if (dx * dx + dy * dy < 100 * 100) { art[i] = 60; art[i + 1] = 120; art[i + 2] = 200; art[i + 3] = 255; }
                mask[i + 1] = 255; mask[i + 3] = 255;     // the wave (green)
            }
        return (art, mask);
    }

    private static readonly JiggleParams Wave = new() { Speed = 3f, Strength = 0.03f, Frequency = 9f };

    private static byte[] Draw(PreviewQualityDef q)
    {
        var (art, mask) = Disc();
        var output = new byte[O * O * 4];
        JiggleShader.Render(art, mask, S, S, Wave, (1f, 1f, 1f, 1f), 1.3f, output, O, O, q.Sampling);
        return output;
    }

    private static PreviewQualityDef Q(string name) => PreviewQualityManager.All.First(q => q.Name == name);

    [Fact]
    public void EachQualityShadesAHardEdgeAsFinelyAsItSamples()
    {
        // The alpha values a hard-edged art takes at its edge: a pixel's samples, averaged.
        var shades = new Dictionary<string, SortedSet<byte>>();
        foreach (var q in PreviewQualityManager.All)
        {
            var px = Draw(q);
            var set = new SortedSet<byte>();
            for (int i = 3; i < px.Length; i += 4) set.Add(px[i]);
            shades[q.Name] = set;
            _out.WriteLine($"{q.Name} ({q.Sampling}): alpha takes {string.Join(", ", set)}");
        }
        // Ultra: four samples a pixel - quarters; High: two - halves; Medium and Low: one - none between.
        Assert.Contains((byte)64, shades["Ultra"]);
        Assert.Contains((byte)191, shades["Ultra"]);
        Assert.Contains((byte)128, shades["High"]);
        Assert.DoesNotContain((byte)64, shades["High"]);
        Assert.Equal(new byte[] { 0, 255 }, shades["Medium"]);
        Assert.Equal(new byte[] { 0, 255 }, shades["Low"]);
    }

    [Fact]
    public void LowDrawsEveryTwoByTwoBlockAsOneAndTheOthersDoNot()
    {
        int Blocks(byte[] px)
        {
            int uneven = 0;
            for (int y = 0; y < O; y += 2)
                for (int x = 0; x < O; x += 2)
                {
                    int a = (y * O + x) * 4;
                    foreach (int b in new[] { a + 4, a + O * 4, a + O * 4 + 4 })
                        if (px[a] != px[b] || px[a + 1] != px[b + 1] || px[a + 2] != px[b + 2] || px[a + 3] != px[b + 3]) { uneven++; break; }
                }
            return uneven;
        }
        int low = Blocks(Draw(Q("Low"))), medium = Blocks(Draw(Q("Medium")));
        _out.WriteLine($"2x2 blocks not of one colour: Low {low}, Medium {medium}");
        Assert.Equal(0, low);
        Assert.True(medium > 100, "control: Medium's blocks are all one colour too - the check cannot tell");
    }

    [DllImport("kernel32.dll")]
    private static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);

    [Fact]
    public void EachQualityTakesTheProcessorLessWorkThanTheOneAbove()
    {
        var (art, mask) = Disc();
        var output = new byte[O * O * 4];
        double Cost(PreviewQualityDef q)
        {
            for (int k = 0; k < 3; k++) JiggleShader.Render(art, mask, S, S, Wave, (1f, 1f, 1f, 1f), k, output, O, O, q.Sampling);
            var best = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                QueryProcessCycleTime(Process.GetCurrentProcess().Handle, out ulong a);
                for (int k = 0; k < 10; k++) JiggleShader.Render(art, mask, S, S, Wave, (1f, 1f, 1f, 1f), k * 0.1f, output, O, O, q.Sampling);
                QueryProcessCycleTime(Process.GetCurrentProcess().Handle, out ulong b);
                best = Math.Min(best, (b - a) / 10.0);
            }
            return best;
        }
        var cost = PreviewQualityManager.All.ToDictionary(q => q.Name, Cost);
        foreach (var (name, c) in cost) _out.WriteLine($"{name}: {c / 1e6:0.0} million cycles a frame ({c / cost["Ultra"]:P0} of Ultra's)");
        Assert.True(cost["High"] < cost["Ultra"] * 0.75, "High is not much lighter than Ultra with no backdrop");
        Assert.True(cost["Medium"] < cost["High"] * 0.8, "Medium is not lighter than High with no backdrop");
        Assert.True(cost["Low"] < cost["Medium"] * 0.6, "Low is not lighter than Medium with no backdrop");
    }
}

[CollectionDefinition("AloneSampling", DisableParallelization = true)]
public sealed class AloneSamplingCollection { }
