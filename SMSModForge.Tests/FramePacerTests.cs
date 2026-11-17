using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The previews' frame pacing (1.9.1), on a simulated display: each preview
/// quality's rate is the rate drawn, on a 60 Hz display with a little jitter
/// and on a 144 Hz one, evenly spaced where the display allows. The control is
/// the check every preview made until 1.9.1 - Environment.TickCount64, which
/// moves in 15.6 ms steps, against the cap - on the same frames: it falls short
/// (Ultra 38, High and Medium 21 on a 60 Hz display), as measured in the
/// editor's window, or this proves nothing.
/// </summary>
public sealed class FramePacerTests
{
    private readonly ITestOutputHelper _out;
    public FramePacerTests(ITestOutputHelper o) => _out = o;

    /// <summary>The moments a display of <paramref name="hz"/> offers frames over
    /// <paramref name="seconds"/>, each up to <paramref name="jitterMs"/> early or late.</summary>
    private static List<double> Frames(double hz, double seconds, double jitterMs, int seed = 7)
    {
        var random = new Random(seed);
        var frames = new List<double>();
        for (int i = 0; i < hz * seconds; i++)
            frames.Add(1000.0 + i * 1000.0 / hz + (random.NextDouble() * 2 - 1) * jitterMs);
        return frames;
    }

    private static List<double> Drawn(IEnumerable<double> frames, int fps)
    {
        double now = 0;
        var pacer = new FramePacer(() => now);
        var drawn = new List<double>();
        foreach (double t in frames)
        {
            now = t;
            if (pacer.Due(fps)) drawn.Add(t);
        }
        return drawn;
    }

    /// <summary>The check every preview made before 1.9.1, with the 15.625 ms clock.</summary>
    private static int DrawnTheOldWay(IEnumerable<double> frames, int fps)
    {
        long last = 0;
        int drawn = 0;
        foreach (double t in frames)
        {
            long tick = (long)(Math.Floor(t / 15.625) * 15.625);
            if (tick - last < 1000L / fps) continue;
            last = tick;
            drawn++;
        }
        return drawn;
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(30, 60)]
    [InlineData(20, 60)]
    [InlineData(15, 60)]
    [InlineData(60, 144)]
    [InlineData(30, 144)]
    [InlineData(20, 144)]
    [InlineData(15, 144)]
    public void EachQualitysRateIsTheRateDrawn(int fps, int hz)
    {
        const double seconds = 20;
        var frames = Frames(hz, seconds, jitterMs: 1.5);
        var drawn = Drawn(frames, fps);
        double rate = drawn.Count / seconds;
        double old = DrawnTheOldWay(frames, fps) / seconds;
        _out.WriteLine($"{fps} fps on {hz} Hz: drawn {rate:0.0} a second (the old way {old:0.0})");
        Assert.InRange(rate, fps * 0.98, fps * 1.02);

        if (hz == 60)
        {
            // On 60 Hz each rate divides the display's, so the frames drawn are
            // evenly spaced: every one, every 2nd, 3rd or 4th.
            var gaps = drawn.Zip(drawn.Skip(1), (a, b) => (int)Math.Round((b - a) / (1000.0 / hz))).Distinct().ToList();
            _out.WriteLine("   gaps in display frames: " + string.Join(", ", gaps));
            Assert.Equal(new[] { hz / fps }, gaps);
        }
    }

    [Fact]
    public void TheOldCheckFellShort_TheControl()
    {
        var frames = Frames(60, 20, jitterMs: 1.5);
        double Old(int fps) => DrawnTheOldWay(frames, fps) / 20.0;
        _out.WriteLine($"the old way on 60 Hz: 60 -> {Old(60):0.0}, 30 -> {Old(30):0.0}, 20 -> {Old(20):0.0}");
        Assert.True(Old(60) < 45, "the old check reached 60 - the simulation is not the fault measured");
        Assert.True(Old(30) < 25);
        Assert.True(Old(20) < 17);
    }

    /// <summary>A preview hidden for a while draws on the first frame back, then
    /// keeps its rate - no burst of frames to catch up.</summary>
    [Fact]
    public void AfterAPauseItCarriesOnWithoutABurst()
    {
        // Frames from 1000 to 3000 ms, then none until 6000, then two seconds more.
        var frames = Frames(60, 2, 0).Concat(Frames(60, 2, 0).Select(t => t + 5000)).ToList();
        var drawn = Drawn(frames, 30);
        var afterPause = drawn.Where(t => t >= 6000).ToList();
        _out.WriteLine($"{afterPause.Count} drawn in the 2 s after the pause, the first at {afterPause.First():0.0} ms");
        Assert.Equal(6000, afterPause.First(), 3);                    // the first frame back
        Assert.InRange(afterPause.Count, 59, 61);                     // 30 a second, not more
    }
}
