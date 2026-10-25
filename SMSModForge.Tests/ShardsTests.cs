using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The parallel runner's arithmetic (<see cref="Shards"/>): every test goes to
/// exactly one tester, the same way in every tester, the testers' shares come
/// out even, and the window adds them up into one run.
/// </summary>
public class ShardsTests
{
    private static List<(string Id, string Class)> Suite(params (string Class, double[] Seconds)[] classes)
        => classes.SelectMany(c => c.Seconds.Select((_, i) => ($"{c.Class}.{i}", c.Class))).ToList();

    private static Dictionary<string, double> Times(params (string Class, double[] Seconds)[] classes)
        => classes.SelectMany(c => c.Seconds.Select((s, i) => ($"{c.Class}.{i}", s))).ToDictionary(t => t.Item1, t => t.s);

    private static readonly (string, double[])[] Classes =
    {
        ("Tutorials", new double[] { 36, 34, 26, 20, 10 }),
        ("Dialogues", new double[] { 9, 8, 7, 6, 5, 4 }),
        ("Scenes", new double[] { 12, 3 }),
        ("Fast", Enumerable.Repeat(0.01, 300).ToArray()),
        ("Themes", new double[] { 11, 11 }),
        ("Quests", new double[] { 5, 5, 5, 5 }),
    };

    [Fact]
    public void EveryTestGoesToExactlyOneTester()
    {
        var cases = Suite(Classes);
        var split = Shards.Assign(cases, Times(Classes), 4);
        Assert.Equal(cases.Select(c => c.Id).OrderBy(x => x), split.Keys.OrderBy(x => x));
        Assert.All(split.Values, i => Assert.InRange(i, 0, 3));
    }

    [Fact]
    public void EveryTesterWorksOutTheSameSplit()
    {
        // Each tester works it out on its own, from the cases in whatever order
        // they came: the order must not matter.
        var cases = Suite(Classes);
        var once = Shards.Assign(cases, Times(Classes), 5);
        var shuffled = cases.OrderBy(c => c.Id.GetHashCode() ^ 0x5f3759df).ToList();
        Assert.Equal(once, Shards.Assign(shuffled, Times(Classes), 5));
    }

    [Fact]
    public void AClassStaysTogetherUnlessItIsMoreThanAFairShare()
    {
        var split = Shards.Assign(Suite(Classes), Times(Classes), 4);
        // 300 fast tests, two themes, the scenes: one tester each.
        foreach (var cls in new[] { "Fast", "Themes", "Scenes", "Dialogues" })
            Assert.Single(split.Where(kv => kv.Key.StartsWith(cls + ".")).Select(kv => kv.Value).Distinct());
        // The tutorials are 126 s of a 297 s run: more than a quarter, so split.
        Assert.True(split.Where(kv => kv.Key.StartsWith("Tutorials.")).Select(kv => kv.Value).Distinct().Count() > 1);
    }

    [Fact]
    public void TheSharesComeOutEven()
    {
        var times = Times(Classes);
        var split = Shards.Assign(Suite(Classes), times, 4);
        var loads = Enumerable.Range(0, 4).Select(i => split.Where(kv => kv.Value == i).Sum(kv => times[kv.Key])).ToList();
        // The heaviest single test is 36 s; no share is further than that from the mean.
        Assert.True(loads.Max() - loads.Average() <= 36, string.Join(", ", loads));
    }

    [Fact]
    public void TheStatusNoteReadsBackAsItWasWritten()
    {
        var now = new RunProgress.Snapshot(12, 40, 2, 1, 0.375, TimeSpan.FromSeconds(61.5), TimeSpan.FromSeconds(90),
                                           "NodeButtonsTests.Duplicate", TimeSpan.FromSeconds(3.25),
                                           new[] { "A.fails", "B.fails too" }, false);
        var back = Shards.Parse(Shards.Format(new Shards.Status(4242, now)))!;
        Assert.Equal(4242, back.Pid);
        Assert.Equal(now with { Failures = back.Now.Failures }, back.Now);
        Assert.Equal(now.Failures, back.Now.Failures);
        Assert.Null(Shards.Parse("pid=1\ndone=3\n"));   // written halfway: not taken as anything
    }

    private static RunProgress.Snapshot At(int done, int total, double fraction, double left, bool finished = false,
                                           string? running = null, double runningFor = 0, params string[] failures)
        => new(done, total, failures.Length, 0, fraction, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(left),
               running, TimeSpan.FromSeconds(runningFor), failures, finished);

    [Fact]
    public void TheWindowAddsTheTestersUpIntoOneRun()
    {
        var plan = new[] { (10, 100.0), (20, 300.0), (5, 100.0) };
        var own = At(10, 10, 1, 0, finished: true);
        var second = new Shards.Status(1, At(5, 20, 0.5, 150, running: "Slow.one", runningFor: 70, failures: "Slow.broken"));
        var all = Shards.Combine(own, new Shards.Status?[] { second, null }, new[] { false, false }, plan);

        Assert.Equal(15, all.Done);
        Assert.Equal(35, all.Total);        // the third tester's share counts before it has said anything
        Assert.Equal(1, all.Failed);
        Assert.Equal(new[] { "Slow.broken" }, all.Failures);
        Assert.Equal((100 * 1 + 300 * 0.5 + 0) / 500, all.Fraction, 3);
        Assert.Equal(TimeSpan.FromSeconds(150), all.Left);   // the slowest tester decides
        Assert.Equal("Slow.one", all.Running);
        Assert.False(all.Finished);
    }

    [Fact]
    public void ATesterThatStoppedIsAFailureNotAWait()
    {
        var plan = new[] { (1, 10.0), (4, 40.0) };
        var crashed = new Shards.Status(99999, At(1, 4, 0.25, 30));
        var all = Shards.Combine(At(1, 1, 1, 0, finished: true), new Shards.Status?[] { crashed }, new[] { true }, plan);
        Assert.True(all.Finished);
        Assert.Equal(1, all.Failed);
        Assert.Contains(all.Failures, f => f.StartsWith("tester 2 stopped before finishing"));
    }

    [Fact]
    public void TheTestersTimesAreFiledTogether()
    {
        string dir = Path.Combine(Path.GetTempPath(), "smsmodforge-shards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string main = Path.Combine(dir, "test-timings.tsv"), a = Path.Combine(dir, "a.tsv"), b = Path.Combine(dir, "b.tsv");
            File.WriteAllLines(main, new[] { "x\t1\tX.old", "y\t2\tY.kept" });
            File.WriteAllLines(a, new[] { "x\t5\tX.new" });
            File.WriteAllLines(b, new[] { "z\t3\tZ.added" });
            Timings.Merge(new[] { a, b, Path.Combine(dir, "never-written.tsv") }, main);

            var times = Timings.Read(main);
            Assert.Equal(new Dictionary<string, double> { ["x"] = 5, ["y"] = 2, ["z"] = 3 }, times);
            Assert.Contains("x\t5\tX.new", File.ReadAllLines(main));
        }
        finally { Directory.Delete(dir, true); }
    }
}
