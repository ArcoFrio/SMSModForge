using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A run of several parts in one window with one estimate (the author, 2026-11-22: the pre-release tests on "a
/// single prompt instead of one large one and a few small ones, so I have a correct estimate of time").
/// </summary>
public sealed class WholeRunTests
{
    private readonly ITestOutputHelper _out;
    public WholeRunTests(ITestOutputHelper o) => _out = o;

    private static WholeRun.Part Part(string label, string title, string theme = "", int testers = 1)
        => new(label, testers, theme, title);

    private static RunProgress.Snapshot Going(int done, int total, double fraction, double leftSeconds,
                                              string? running = null, params string[] failures)
        => new(done, total, failures.Length, 0, fraction, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(leftSeconds),
               running, TimeSpan.FromSeconds(3), failures, false);

    private static RunProgress.Snapshot Over(int total, params string[] failures)
        => new(total, total, failures.Length, 0, 1, TimeSpan.FromSeconds(10), TimeSpan.Zero, null, TimeSpan.Zero, failures, true);

    private static WholeRun.Plan Planned(int total, params double[] shares)
    {
        var shards = new (int, double)[shares.Length];
        for (int i = 0; i < shares.Length; i++) shards[i] = (total / shares.Length, shares[i]);
        return new WholeRun.Plan(total, true, shards);
    }

    private static WholeRun.PartView View(WholeRun.Part part, double? expected, int total, bool started = false,
                                          bool done = false, RunProgress.Snapshot? now = null)
        => new(part, Planned(total, expected ?? 0), expected, started, done, now);

    [Fact]
    public void BeforeAnythingRuns_TheEstimateIsEveryPart()
    {
        var s = WholeRun.Combine(new[]
        {
            View(Part("main", "every test"), 60, 3000),
            View(Part("alone", "the timing tests"), 10, 20),
            View(Part("ingame", "the in-game suite"), 50, 70),
        }, TimeSpan.FromSeconds(4));

        Assert.Equal(0, s.Done);
        Assert.Equal(3090, s.Total);
        Assert.Equal(0, s.Fraction);
        Assert.Equal(TimeSpan.FromSeconds(120), s.Left);
        Assert.StartsWith("Getting ready", s.Part);
        Assert.False(s.Finished);
    }

    [Fact]
    public void InTheFirstPart_ThePartsStillToComeAreInTheTimeLeft()
    {
        // The control is the window as it was: the first part alone said 30 seconds left.
        var first = Going(1500, 3000, 0.5, 30, running: "Tests.x");
        var s = WholeRun.Combine(new[]
        {
            View(Part("main", "every test, 12 testers"), 60, 3000, started: true, now: first),
            View(Part("alone", "the timing tests, alone"), 10, 20),
            View(Part("ingame", "the in-game suite"), 50, 70),
        }, TimeSpan.FromSeconds(40));

        Assert.Equal(TimeSpan.FromSeconds(30 + 10 + 50), s.Left);
        Assert.Equal(0.25, s.Fraction, 3);   // 30 of 120 seconds
        Assert.Equal(1500, s.Done);
        Assert.Equal("Part 1 of 3: every test, 12 testers", s.Part);
        Assert.Equal("Tests.x", s.Running);
    }

    [Fact]
    public void ADonePartCountsWhole_AndThePartsToComeGoAtThePaceSoFar()
    {
        var s = WholeRun.Combine(new[]
        {
            View(Part("main", "every test"), 60, 3000, started: true, done: true, now: Over(3000)),
            View(Part("alone", "the timing tests"), 10, 20),
            View(Part("ingame", "the in-game suite"), 50, 70),
        }, TimeSpan.FromSeconds(130), pace: 2);

        Assert.Equal(0.5, s.Fraction, 3);
        Assert.Equal(TimeSpan.FromSeconds((10 + 50) * 2), s.Left);
        Assert.Equal("Part 2 of 3: the timing tests (starting)", s.Part);
    }

    [Fact]
    public void AFailureSaysWhichLookItWasIn()
    {
        var s = WholeRun.Combine(new[]
        {
            View(Part("main", "every test"), 60, 10, started: true, done: true, now: Over(10, "Tests.a")),
            View(Part("ex", "the window tests in Neon", "Neon"), 30, 5, started: true, now: Going(2, 5, 0.4, 18, null, "Tests.b")),
        }, TimeSpan.FromSeconds(80));

        Assert.Equal(2, s.Failed);
        Assert.Equal(new[] { "Tests.a", "Tests.b - in Neon" }, s.Failures);
    }

    [Fact]
    public void WithoutAnEstimateForEveryPart_ItCountsTests_AndDoesNotGuessTheTime()
    {
        var s = WholeRun.Combine(new[]
        {
            View(Part("main", "every test"), 60, 100, started: true, now: Going(50, 100, 0.5, 30)),
            View(Part("other", "a part never timed"), null, 100),
        }, TimeSpan.FromSeconds(30));

        Assert.Equal(0.25, s.Fraction, 3);   // 50 of 200 tests
        Assert.Null(s.Left);
    }

    [Fact]
    public void TheEndIsFull()
    {
        var s = WholeRun.Combine(new[]
        {
            View(Part("main", "every test"), 60, 10, started: true, done: true, now: Over(10)),
            View(Part("alone", "the timing tests"), 0, 0, started: true, done: true),
        }, TimeSpan.FromSeconds(70));

        Assert.True(s.Finished);
        Assert.Equal(1, s.Fraction);
        Assert.Equal(TimeSpan.Zero, s.Left);
        Assert.Null(s.Part);
    }

    [Fact]
    public void APartsEstimateIsItsSlowestShare_AndWhatItTookBeyondItLastTime()
    {
        var history = new Dictionary<string, WholeRun.Took>
        {
            ["main"] = new(Wall: 110, Planned: 90),       // 20 s of starting and building beyond the tests
            ["ingame"] = new(Wall: 900, Planned: 600),
        };
        Assert.Equal(100 + 20, WholeRun.Expected(Part("main", ""), Planned(3000, 80, 100, 95), history));
        Assert.Equal(700 + 300, WholeRun.Expected(Part("ingame", ""), Planned(70, 700), history));
        // No tests' times: what it took last time; the in-game suite never timed: its measured length.
        Assert.Equal(900, WholeRun.Expected(Part("ingame", ""), null, history));
        Assert.Equal(WholeRun.InGameUntilTimed.TotalSeconds, WholeRun.Expected(Part("ingame", ""), null, new Dictionary<string, WholeRun.Took>()));
        // The control: nothing known about a part is said as nothing, not as a guess.
        Assert.Null(WholeRun.Expected(Part("ex", ""), null, history));
        // A part with nothing to run takes nothing, whatever it took once.
        Assert.Equal(0, WholeRun.Expected(Part("main", ""), new WholeRun.Plan(0, true, new[] { (0, 0.0) }), history));
    }

    [Fact]
    public void ThePlansThePartsAndTheHistoryReadBackAsWritten()
    {
        var plan = new WholeRun.Plan(3071, true, new[] { (260, 88.5), (255, 91.25) });
        var back = WholeRun.ParsePlan(WholeRun.Format(plan))!;
        Assert.Equal(3071, back.Total);
        Assert.True(back.Weighted);
        Assert.Equal(plan.Shards, back.Shards);
        Assert.Equal(91.25, back.Wall);
        Assert.Null(WholeRun.ParsePlan("half a fi"));   // written halfway

        var parts = WholeRun.ParseParts("main\t12\t\tevery test, 12 testers\r\nex\t12\tNeon\tthe window tests in Neon\n\n");
        Assert.Equal(2, parts.Count);
        Assert.Equal(new WholeRun.Part("ex", 12, "Neon", "the window tests in Neon"), parts[1]);

        string file = Path.Combine(Path.GetTempPath(), "smsmodforge-parts-" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            WholeRun.WriteHistory(new Dictionary<string, WholeRun.Took> { ["ingame"] = new(912.5, 640) }, file);
            Assert.Equal(new WholeRun.Took(912.5, 640), WholeRun.ReadHistory(file)["ingame"]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TheInGameSuitesTimesAreKeptApart()
    {
        // Its checks take minutes with the game there and nothing elsewhere: one file had each run overwrite the
        // other's, and a normal run planned a check of the game as twenty seconds of a tester's share.
        string? before = Environment.GetEnvironmentVariable("SMSMODFORGE_INGAME");
        try
        {
            Environment.SetEnvironmentVariable("SMSMODFORGE_INGAME", null);
            Assert.Equal("test-timings.tsv", Path.GetFileName(Timings.DefaultPath));
            Environment.SetEnvironmentVariable("SMSMODFORGE_INGAME", @"C:\a test copy");
            Assert.Equal("test-timings.ingame.tsv", Path.GetFileName(Timings.DefaultPath));
        }
        finally { Environment.SetEnvironmentVariable("SMSMODFORGE_INGAME", before); }
    }

    /// <summary>
    /// The window reads the run's folder as the script and the testers leave it: the parts, each part's plan, its
    /// testers' notes, its start and end. The first part over, the second half done by one of its two testers.
    /// </summary>
    [Fact]
    public void TheWindowReadsTheRunAsTheScriptAndTheTestersLeaveIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "smsmodforge-wholerun-" + Guid.NewGuid().ToString("N"));
        string history = Path.Combine(root, "history.tsv");
        try
        {
            void Put(string relative, string text)
            {
                string file = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, text);
            }
            string Note(int done, int total, double fraction, bool finished, double left, params string[] failures)
                => Shards.Format(new Shards.Status(0, new RunProgress.Snapshot(done, total, failures.Length, 0, fraction,
                       TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(left), finished ? null : "Tests.slow", TimeSpan.FromSeconds(2),
                       failures, finished)));

            Put("parts.txt", "main\t2\t\tevery test, 2 testers\nex\t2\tNeon\tthe window tests in Neon\n");
            Put("main/plan.txt", WholeRun.Format(new WholeRun.Plan(10, true, new[] { (5, 40.0), (5, 40.0) })));
            Put("ex/plan.txt", WholeRun.Format(new WholeRun.Plan(4, true, new[] { (2, 20.0), (2, 20.0) })));
            Put("main/started", ""); Put("main/done", "");
            Put("main/tester-1.status", Note(5, 5, 1, true, 0));
            Put("main/tester-2.status", Note(5, 5, 1, true, 0, "Tests.a"));
            Put("ex/started", "");
            Put("ex/tester-1.status", Note(2, 2, 1, true, 0));
            Put("ex/tester-2.status", Note(0, 2, 0.1, false, 18, "Tests.b"));

            var watch = new WholeRun.Watcher(root, history);
            var s = watch.Now();
            _out.WriteLine($"{s.Part} | {s.Done}/{s.Total} | {s.Fraction:0.###} | left {s.Left} | {string.Join(", ", s.Failures)}");

            Assert.Equal(2, watch.Parts.Count);
            Assert.Equal("Part 2 of 2: the window tests in Neon", s.Part);
            Assert.Equal(12, s.Done);
            Assert.Equal(14, s.Total);
            Assert.Equal(new[] { "Tests.a", "Tests.b - in Neon" }, s.Failures);
            Assert.Equal(TimeSpan.FromSeconds(18), s.Left);            // the slower of the part's two testers
            Assert.InRange(s.Fraction, (40 + 20 * 0.55) / 60 - 0.01, (40 + 20 * 0.55) / 60 + 0.01);
            Assert.Equal("Tests.slow", s.Running);

            // Over: the script's word ends it, and what each part took is filed for next time - against what its
            // tests took this time (its slower tester's) where its testers left their times, else its plan.
            Put("ex/tester-1.timings.tsv", "id1\t12.5\tTests.x\nid2\t3\tTests.y\n");
            Put("ex/tester-2.timings.tsv", "id3\t9\tTests.z\n");
            Put("ex/done", ""); Put("done", "");
            Assert.True(watch.Now().Finished);
            watch.FileWhatEachPartTook();
            var took = WholeRun.ReadHistory(history);
            Assert.Equal(40, took["main"].Planned);
            Assert.Equal(15.5, took["ex"].Planned);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public void TheWindowSaysWhichPartItIsIn()
    {
        var s = WholeRun.Combine(new[]
        {
            View(Part("main", "every test, 12 testers"), 60, 30, started: true, now: Going(15, 30, 0.5, 30, "Tests.x")),
            View(Part("ingame", "the in-game suite"), 50, 7),
        }, TimeSpan.FromSeconds(40));

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                TestDesktop.Enter();
                using var w = new RunProgressWindow(() => s, 1);
                w.CreateControl();
                w.Tick();
                _out.WriteLine($"{w.Text} | {w.CountText} | {w.PartText} | {w.TimeText}");
                Assert.Equal("Part 1 of 2: every test, 12 testers", w.PartText);
                Assert.Equal("15 of 37 tests", w.CountText);
                Assert.Equal("0:40 elapsed  ·  about 2 min left", w.TimeText);   // 30 + 50 s

                // The control: a run of one part shows no such line, as before.
                using var one = new RunProgressWindow(() => s with { Part = null }, 1);
                one.CreateControl();
                one.Tick();
                Assert.Equal("", one.PartText);
                Assert.True(w.ClientSize.Height > one.ClientSize.Height, $"{w.ClientSize.Height} against {one.ClientSize.Height}");   // room made for the line
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("in the window: " + failure.Message, failure);
    }
}
