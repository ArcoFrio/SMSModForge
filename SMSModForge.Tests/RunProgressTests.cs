using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The test run's progress window (the author, 2026-09-28): what it counts,
/// how it weighs a test, and what it says.
/// </summary>
public sealed class RunProgressTests
{
    private readonly ITestOutputHelper _out;
    public RunProgressTests(ITestOutputHelper o) => _out = o;

    private sealed class FakeClock
    {
        public TimeSpan Now;
        public TimeSpan Read() => Now;
    }

    private static (string, string)[] Cases(params string[] ids)
    {
        var cases = new (string, string)[ids.Length];
        for (int i = 0; i < ids.Length; i++) cases[i] = (ids[i], "Tests." + ids[i]);
        return cases;
    }

    [Fact]
    public void ThisRunIsBeingCounted()
    {
        // The framework is xunit's own with the messages read on their way
        // past. Without it, or with it and nothing reaching it, there is no
        // run here - or one that does not know this test is running.
        var run = RunProgress.Current;
        Assert.NotNull(run);
        var now = run!.Now();
        _out.WriteLine($"{now.Done} of {now.Total}, running {now.Running}");
        Assert.True(now.Total >= 1);
        Assert.Equal(nameof(RunProgressTests) + "." + nameof(ThisRunIsBeingCounted), now.Running);
    }

    [Fact]
    public void TheBarIsTheTimeDone_NotTheTestsDone()
    {
        // One test that took ten seconds last time and one that took ninety:
        // with the short one done, the run is a tenth of the way, not half.
        var clock = new FakeClock();
        var run = new RunProgress(Cases("quick", "slow"),
                                  new Dictionary<string, double> { ["quick"] = 10, ["slow"] = 90 }, clock.Read);
        Assert.Equal(100, run.ExpectedSeconds);

        run.Started("quick");
        clock.Now = TimeSpan.FromSeconds(10);
        run.Finished("quick");
        run.Started("slow");
        var s = run.Now();
        Assert.Equal(1, s.Done);
        Assert.Equal(0.1, s.Fraction, 3);
        Assert.Equal(TimeSpan.FromSeconds(90), s.Left);
        Assert.Equal("Tests.slow", s.Running);
    }

    [Fact]
    public void TimeLeftGoesAtThisRunsPace()
    {
        // This run is taking twice as long as the last: what is left will too.
        var clock = new FakeClock();
        var run = new RunProgress(Cases("a", "b"),
                                  new Dictionary<string, double> { ["a"] = 10, ["b"] = 90 }, clock.Read);
        run.Started("a");
        clock.Now = TimeSpan.FromSeconds(20);
        run.Finished("a");
        Assert.Equal(TimeSpan.FromSeconds(180), run.Now().Left);
    }

    [Fact]
    public void TheOneRunningCountsForItsTimeSoFar_ButIsNotDoneUntilItIs()
    {
        var clock = new FakeClock();
        var run = new RunProgress(Cases("a", "b"),
                                  new Dictionary<string, double> { ["a"] = 50, ["b"] = 50 }, clock.Read);
        run.Started("a");
        clock.Now = TimeSpan.FromSeconds(20);
        Assert.Equal(0.2, run.Now().Fraction, 3);
        Assert.Equal(TimeSpan.FromSeconds(20), run.Now().RunningFor);

        // Past what it took last time: still short of done.
        clock.Now = TimeSpan.FromSeconds(500);
        Assert.True(run.Now().Fraction < 0.5);
        Assert.Equal(0, run.Now().Done);
    }

    [Fact]
    public void WithFewEarlierTimes_ItCountsTests_AndDoesNotGuessTheTime()
    {
        var clock = new FakeClock();
        var run = new RunProgress(Cases("a", "b", "c", "d"),
                                  new Dictionary<string, double> { ["a"] = 100 }, clock.Read);
        Assert.Equal(0, run.ExpectedSeconds);
        run.Started("a");
        clock.Now = TimeSpan.FromSeconds(100);
        run.Finished("a");
        var s = run.Now();
        Assert.Equal(0.25, s.Fraction, 3);
        Assert.Null(s.Left);
    }

    [Fact]
    public void FailuresAreCountedAndNamed_AndTheEndIsFull()
    {
        var clock = new FakeClock();
        var run = new RunProgress(Cases("a", "b"), new Dictionary<string, double>(), clock.Read);
        run.Started("a");
        run.Failed("Tests.a");
        run.Finished("a");
        run.Started("b");
        run.Skipped();
        run.Finished("b");
        run.End();
        var s = run.Now();
        Assert.Equal(1, s.Failed);
        Assert.Equal(new[] { "Tests.a" }, s.Failures);
        Assert.Equal(1, s.Skipped);
        Assert.True(s.Finished);
        Assert.Equal(1.0, s.Fraction);
        Assert.Null(s.Running);
        Assert.True(run.Ended.IsSet);
    }

    [Fact]
    public void TimingsAreKept_IncludingTheTestsThisRunDidNotRun()
    {
        string file = Path.Combine(Path.GetTempPath(), "smsmodforge-timings-" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            File.WriteAllLines(file, new[] { "old\t12.5\tTests.old", "a\t99\tTests.a" });
            var clock = new FakeClock();
            var run = new RunProgress(Cases("a"), Timings.Read(file), clock.Read);
            run.Started("a");
            clock.Now = TimeSpan.FromSeconds(3.25);
            run.Finished("a");
            Timings.Write(run, file);

            var read = Timings.Read(file);
            _out.WriteLine(File.ReadAllText(file));
            Assert.Equal(3.25, read["a"], 3);
            Assert.Equal(12.5, read["old"], 3);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TheWindowSaysWhereTheRunIs()
    {
        var clock = new FakeClock();
        var run = new RunProgress(Cases("a", "b", "c"),
                                  new Dictionary<string, double> { ["a"] = 30, ["b"] = 30, ["c"] = 240 }, clock.Read);
        run.Started("a");
        run.Failed("Tests.a");
        clock.Now = TimeSpan.FromSeconds(30);
        run.Finished("a");
        run.Started("b");
        clock.Now = TimeSpan.FromSeconds(60);
        run.Finished("b");
        run.Started("c");
        clock.Now = TimeSpan.FromSeconds(125);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var w = new RunProgressWindow(run);
                w.CreateControl();
                w.Tick();
                _out.WriteLine($"{w.Text} | {w.CountText} | {w.TimeText} | {w.NowText} | {w.FailedText} | bar {w.BarValue}");
                Assert.Equal("2 of 3 tests", w.CountText);
                Assert.Equal("2:05 elapsed  ·  about 3 min left", w.TimeText);
                Assert.Equal("Now: Tests.c  (1:05)", w.NowText);
                Assert.Equal("1 failed:", w.FailedText);
                Assert.Equal(new[] { "Tests.a" }, w.FailureNames);
                Assert.Equal(417, w.BarValue);   // 60 + 65 of 300 seconds
                Assert.Equal("41% - SMSModForge tests", w.Text);

                run.Finished("c");
                run.End();
                w.Tick();
                Assert.Equal("Finished.", w.NowText);
                Assert.Equal(1000, w.BarValue);
                Assert.Equal("Done - SMSModForge tests", w.Text);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("in the window: " + failure.Message, failure);
    }
}
