using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace SMSModForge.Tests;

/// <summary>
/// One run of the suite split across several test processes at once - the
/// testers - by Tools/Testing/run-tests.ps1 (the author, 2026-10-22: a full
/// run had reached forty minutes, nearly all of it window tests that can only
/// go one at a time inside one process).
/// <para/>
/// Each tester is told which one it is (SMSMODFORGE_SHARD = "k/N") and where
/// the run keeps its notes (SMSMODFORGE_SHARD_DIR). Every tester sees the same
/// tests and the same last times, so each works out the same split on its own
/// and keeps only its share - nothing has to be handed round. Tester 1 shows
/// the one progress window, reading the others' notes, and stays until the
/// last of them is done, then files everybody's times.
/// </summary>
internal static class Shards
{
    /// <summary>This tester's place in the run (0-based) and how many there
    /// are, or null for an ordinary run.</summary>
    public static (int Index, int Count)? FromEnvironment()
    {
        string? shard = Environment.GetEnvironmentVariable("SMSMODFORGE_SHARD");
        if (string.IsNullOrWhiteSpace(shard) || string.IsNullOrWhiteSpace(Folder)) return null;
        var parts = shard.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int k) || !int.TryParse(parts[1], out int n)) return null;
        if (n < 1 || k < 1 || k > n) return null;
        return (k - 1, n);
    }

    /// <summary>Where the run's testers leave notes for each other.</summary>
    public static string? Folder => Environment.GetEnvironmentVariable("SMSMODFORGE_SHARD_DIR");

    // ── The split ────────────────────────────────────────────────────────

    /// <summary>
    /// Which tester runs each test, by id. A class stays together - its tests
    /// share set-up, and a class split over testers pays for it in each - unless
    /// it alone is more than a tester's fair share, when its tests go out one
    /// by one. Then the heaviest first, each to the tester with the least so
    /// far: within a few seconds of even for a suite like this one.
    /// </summary>
    public static Dictionary<string, int> Assign(IReadOnlyList<(string Id, string Class)> cases,
                                                 IReadOnlyDictionary<string, double> lastSeconds, int count)
    {
        var known = cases.Where(c => lastSeconds.ContainsKey(c.Id)).Select(c => lastSeconds[c.Id]).ToList();
        double typical = known.Count > 0 ? known.Average() : 1;
        double Weight(string id) => Math.Max(0.001, lastSeconds.TryGetValue(id, out var s) ? s : typical);

        double fair = cases.Sum(c => Weight(c.Id)) / Math.Max(1, count);
        var units = new List<(string Key, double Weight, List<string> Ids)>();
        foreach (var cls in cases.GroupBy(c => c.Class))
        {
            double weight = cls.Sum(c => Weight(c.Id));
            if (weight > fair && cls.Count() > 1)
                units.AddRange(cls.Select(c => (cls.Key + "|" + c.Id, Weight(c.Id), new List<string> { c.Id })));
            else
                units.Add((cls.Key, weight, cls.Select(c => c.Id).ToList()));
        }

        var load = new double[count];
        var result = new Dictionary<string, int>();
        foreach (var unit in units.OrderByDescending(u => u.Weight).ThenBy(u => u.Key, StringComparer.Ordinal))
        {
            int lightest = 0;
            for (int i = 1; i < count; i++) if (load[i] < load[lightest]) lightest = i;
            load[lightest] += unit.Weight;
            foreach (var id in unit.Ids) result[id] = lightest;
        }
        return result;
    }

    /// <summary>Each tester's share: how many tests, and how long they took
    /// last time (0 where too few are known to say).</summary>
    public static (int Total, double Expected)[] Plan(IReadOnlyList<(string Id, string Class)> cases,
                                                     IReadOnlyDictionary<string, double> lastSeconds,
                                                     IReadOnlyDictionary<string, int> assignment, int count)
    {
        var known = cases.Where(c => lastSeconds.ContainsKey(c.Id)).Select(c => lastSeconds[c.Id]).ToList();
        bool weighted = cases.Count > 0 && known.Count * 2 >= cases.Count;
        double typical = known.Count > 0 ? known.Average() : 0;
        var plan = new (int Total, double Expected)[count];
        foreach (var c in cases)
        {
            int i = assignment[c.Id];
            plan[i].Total++;
            if (weighted) plan[i].Expected += Math.Max(0.001, lastSeconds.TryGetValue(c.Id, out var s) ? s : typical);
        }
        return plan;
    }

    // ── Notes between testers ────────────────────────────────────────────

    /// <summary>What one tester says about itself.</summary>
    public sealed record Status(int Pid, RunProgress.Snapshot Now);

    public static string StatusPath(int index) => Path.Combine(Folder!, $"tester-{index + 1}.status");
    public static string TimingsPath(int index) => Path.Combine(Folder!, $"tester-{index + 1}.timings.tsv");

    public static string Format(Status s)
    {
        var n = s.Now;
        string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            "pid=" + s.Pid, "done=" + n.Done, "total=" + n.Total, "failed=" + n.Failed, "skipped=" + n.Skipped,
            "fraction=" + F(n.Fraction), "elapsed=" + F(n.Elapsed.TotalSeconds),
            "left=" + (n.Left is { } left ? F(left.TotalSeconds) : ""),
            "running=" + (n.Running ?? ""), "runningFor=" + F(n.RunningFor.TotalSeconds),
            "finished=" + (n.Finished ? 1 : 0),
        };
        lines.AddRange(n.Failures.Select(f => "failure=" + f.Replace('\n', ' ')));
        return string.Join("\n", lines) + "\n";
    }

    public static Status? Parse(string text)
    {
        var values = new Dictionary<string, string>();
        var failures = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq], value = line[(eq + 1)..].TrimEnd('\r');
            if (key == "failure") failures.Add(value);
            else values[key] = value;
        }
        if (!values.ContainsKey("finished")) return null;   // written halfway, or not one of ours

        int I(string k) => values.TryGetValue(k, out var v) && int.TryParse(v, out int x) ? x : 0;
        double D(string k) => values.TryGetValue(k, out var v)
                              && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
        TimeSpan? left = values.TryGetValue("left", out var l) && l.Length > 0 ? TimeSpan.FromSeconds(D("left")) : null;
        string running = values.TryGetValue("running", out var r) ? r : "";
        return new Status(I("pid"), new RunProgress.Snapshot(
            I("done"), I("total"), I("failed"), I("skipped"), D("fraction"),
            TimeSpan.FromSeconds(D("elapsed")), left, running.Length > 0 ? running : null,
            TimeSpan.FromSeconds(D("runningFor")), failures, I("finished") == 1));
    }

    public static void Write(int index, RunProgress run)
    {
        try
        {
            string path = StatusPath(index), temp = path + ".tmp";
            File.WriteAllText(temp, Format(new Status(Environment.ProcessId, run.Now())));
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static Status? Read(int index)
    {
        try
        {
            string path = StatusPath(index);
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Whether a tester's process has ended without saying it was done.</summary>
    public static bool Gone(Status? status)
    {
        if (status == null || status.Now.Finished || status.Pid <= 0) return false;
        try { using var p = System.Diagnostics.Process.GetProcessById(status.Pid); return p.HasExited; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }

    // ── All testers as one run ───────────────────────────────────────────

    /// <summary>
    /// The whole run as the window shows it: tester 1's own progress and what
    /// the others last said. A tester not heard from yet counts as not started
    /// on its share; one whose process ended without finishing counts as done,
    /// with a failure saying so - it is not going to run the rest.
    /// </summary>
    public static RunProgress.Snapshot Combine(RunProgress.Snapshot own, IReadOnlyList<Status?> others,
                                               IReadOnlyList<bool> gone, IReadOnlyList<(int Total, double Expected)> plan)
    {
        var all = new List<RunProgress.Snapshot> { own };
        var failures = new List<string>(own.Failures);
        for (int i = 0; i < others.Count; i++)
        {
            int index = i + 1;
            var s = others[i]?.Now ?? new RunProgress.Snapshot(0, plan[index].Total, 0, 0, 0, TimeSpan.Zero,
                                                               plan[index].Expected > 0 ? TimeSpan.FromSeconds(plan[index].Expected) : null,
                                                               null, TimeSpan.Zero, Array.Empty<string>(), false);
            failures.AddRange(s.Failures);
            if (gone[i] && !s.Finished)
            {
                failures.Add($"tester {index + 1} stopped before finishing ({s.Done} of {s.Total} done)");
                s = s with { Finished = true, Failed = s.Failed + 1, Running = null, Left = TimeSpan.Zero };
            }
            all.Add(s);
        }

        double expected = plan.Sum(p => p.Expected);
        int done = all.Sum(s => s.Done), total = all.Sum(s => s.Total);
        double fraction = expected > 0
            ? all.Select((s, i) => s.Fraction * plan[i].Expected).Sum() / expected
            : total == 0 ? 0 : (double)done / total;

        bool finished = all.All(s => s.Finished);
        TimeSpan? left = TimeSpan.Zero;
        foreach (var (s, i) in all.Select((s, i) => (s, i)))
        {
            if (s.Finished) continue;
            var mine = s.Left ?? (plan[i].Expected > 0 ? TimeSpan.FromSeconds(plan[i].Expected * (1 - s.Fraction)) : null);
            if (mine == null) { left = null; break; }
            if (mine > left) left = mine;
        }

        var longest = all.Where(s => !s.Finished && s.Running != null).OrderByDescending(s => s.RunningFor).FirstOrDefault();
        return new RunProgress.Snapshot(
            done, total, all.Sum(s => s.Failed), all.Sum(s => s.Skipped),
            finished ? 1 : Math.Clamp(fraction, 0, 1), own.Elapsed, finished ? TimeSpan.Zero : left,
            finished ? null : longest?.Running, longest?.RunningFor ?? TimeSpan.Zero, failures, finished);
    }

    // ── The end of a tester ──────────────────────────────────────────────

    /// <summary>
    /// A tester's run is over: say so, and leave this run's times. Tester 1
    /// then waits for every other - its window is the run's, and closes with
    /// its process - and files all the times into the shared file, which no
    /// tester writes while others may still be reading it.
    /// </summary>
    public static void Finish(int index, int count, RunProgress run)
    {
        Write(index, run);
        Timings.Write(run, TimingsPath(index), keepOthers: false);
        if (index != 0) return;

        // A tester never heard from at all - it failed before its first
        // test - is given a while, then let go.
        var started = DateTime.UtcNow;
        while (true)
        {
            bool waiting = false;
            for (int i = 1; i < count; i++)
            {
                var s = Read(i);
                if (s == null) { if (DateTime.UtcNow - started < TimeSpan.FromMinutes(10)) waiting = true; continue; }
                if (!s.Now.Finished && !Gone(s)) waiting = true;
            }
            if (!waiting) break;
            Thread.Sleep(250);
        }
        Timings.Merge(Enumerable.Range(0, count).Select(TimingsPath));
    }
}
