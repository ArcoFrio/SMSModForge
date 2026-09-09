using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Whether swapping frames can actually hold a source's frame rate.
/// <para/>
/// The question this answers is a real one and it was asked before any of it
/// was built: a screen refreshes at 60 or 144 or 165 Hz, and a video is 24 or
/// 30, and none of those divide evenly. The naive answer — step to the next
/// frame each update — plays a 24fps clip at whatever the monitor happens to
/// be, and a 30fps clip at double speed on a 60Hz screen.
/// <para/>
/// So the clock maps TIME to a frame instead of counting updates, and these
/// measure what that actually delivers: the right number of frames, in the
/// right order, with no drift after an hour, on refresh rates that share no
/// factors with the source.
/// </summary>
public sealed class AnimationClockTests
{
    private readonly ITestOutputHelper _out;
    public AnimationClockTests(ITestOutputHelper o) => _out = o;

    /// <summary>Run a clock past a simulated display and report what was
    /// shown, exactly as a game loop would drive it.</summary>
    private static List<int> Play(AnimationClock clock, double refreshHz,
                                  double seconds, bool loop = true)
    {
        var shown = new List<int>();
        int last = -1;
        int updates = (int)Math.Round(seconds * refreshHz);
        for (int u = 0; u < updates; u++)
        {
            int frame = clock.FrameAt(u / refreshHz, loop);
            if (frame != last) { shown.Add(frame); last = frame; }
        }
        return shown;
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(144.0)]
    [InlineData(165.0)]      // shares no factor with 24 or 30
    [InlineData(59.94)]      // a real monitor, not a round number
    public void AClipPlaysItsOwnFrameCountWhateverTheScreenDoes(double refreshHz)
    {
        foreach (int fps in new[] { 12, 24, 25, 30, 50 })
        {
            // Ten seconds of source, played for ten seconds.
            var clock = AnimationClock.AtRate(fps * 10, fps);
            var shown = Play(clock, refreshHz, 10.0);

            int wanted = fps * 10;
            _out.WriteLine($"{fps}fps on {refreshHz}Hz: {shown.Count} of {wanted} frames"
                           + $" ({100.0 * shown.Count / wanted:F2}%)");

            // Always in order, never repeated, never backwards. Judder is a
            // frame shown twice or out of turn, and neither happens.
            Assert.Equal(shown, shown.OrderBy(x => x).ToList());
            Assert.Equal(shown.Count, shown.Distinct().Count());

            // Every frame lands while the source rate is at most half the
            // refresh - which is every normal case: 24 and 30 on any modern
            // screen. Closer than that and the display physically cannot show
            // them all: at 50fps on a 59.94Hz screen there are 1.1988 updates
            // per frame, so now and then two frames fall in one update and one
            // is coalesced. Every video player on earth does the same; the
            // bound asserted here is that it stays under one percent.
            if (fps * 2 <= refreshHz) Assert.Equal(wanted, shown.Count);
            else Assert.True(shown.Count >= wanted * 0.99,
                             $"{fps}fps on {refreshHz}Hz dropped too many: {shown.Count}/{wanted}");
        }
    }

    [Fact]
    public void AnHourInItIsStillOnTheRightFrame()
    {
        // Counting updates accumulates error; folding time does not. A scene
        // left open in a room the player walked away from is the case nobody
        // tests and everybody eventually sees.
        var clock = AnimationClock.AtRate(30, 30.0);          // a 1-second loop

        foreach (double hours in new[] { 0.0, 0.5, 1.0, 6.0 })
        {
            double t = hours * 3600.0;
            // Exactly on a frame boundary, an hour in.
            Assert.Equal(0, clock.FrameAt(t, loop: true));
            Assert.Equal(15, clock.FrameAt(t + 0.5, loop: true));
            Assert.Equal(29, clock.FrameAt(t + 29.0 / 30.0, loop: true));
        }
        _out.WriteLine("no drift at 0, 0.5, 1 and 6 hours");
    }

    [Fact]
    public void AGifsOwnPerFrameDelaysAreHonoured()
    {
        // Half of all GIFs hold one frame far longer than the rest — a title
        // card, a pause before a loop. A constant-rate clock plays those wrong
        // in a way that looks like broken art rather than a broken player.
        var clock = new AnimationClock(new[] { 1.0, 0.05, 0.05, 0.05 }, 0.1);

        Assert.Equal(1.15, clock.Duration, 6);
        Assert.Equal(0, clock.FrameAt(0.0, true));
        Assert.Equal(0, clock.FrameAt(0.99, true));     // still the long one
        Assert.Equal(1, clock.FrameAt(1.01, true));
        Assert.Equal(2, clock.FrameAt(1.06, true));
        Assert.Equal(3, clock.FrameAt(1.11, true));
        Assert.Equal(0, clock.FrameAt(1.16, true));     // looped

        // And on a real screen the long frame really is held ~20x longer.
        var shown = Play(clock, 60.0, 1.15);
        Assert.Equal(4, shown.Count);
    }

    [Fact]
    public void AZeroDelayIsTreatedTheWayEveryBrowserTreatsIt()
    {
        // GIFs in the wild carry delay 0 constantly. Refusing the file would
        // be a worse answer than agreeing with every other program.
        var clock = new AnimationClock(new[] { 0.0, 0.0 }, 0.1);
        Assert.Equal(0.2, clock.Duration, 6);
        Assert.Equal(1, clock.FrameAt(0.15, true));
    }

    [Fact]
    public void AOneShotHoldsItsLastFrameRatherThanVanishing()
    {
        var clock = AnimationClock.AtRate(3, 10.0);
        Assert.Equal(2, clock.FrameAt(99.0, loop: false));
        Assert.Equal(0, clock.FrameAt(99.0, loop: true));
    }

    [Fact]
    public void LookingUpAFrameCostsNothingPerUpdate()
    {
        // The claim being checked: a frame swap is free, so the cost of an
        // animated scene is memory rather than time. A binary search over a
        // few hundred frames is the whole per-update cost.
        var clock = AnimationClock.AtRate(1800, 30.0);      // a minute at 30fps

        var watch = System.Diagnostics.Stopwatch.StartNew();
        const int updates = 1_000_000;
        int sink = 0;
        for (int i = 0; i < updates; i++) sink += clock.FrameAt(i / 60.0, true);
        watch.Stop();

        double nsEach = watch.Elapsed.TotalMilliseconds * 1e6 / updates;
        _out.WriteLine($"{nsEach:F1} ns per update over {updates:N0} lookups (sink {sink})");

        // A 60Hz frame is 16,000,000 ns. Anything in this range is noise.
        Assert.True(nsEach < 1000, $"{nsEach} ns per update is not free");
    }
}
