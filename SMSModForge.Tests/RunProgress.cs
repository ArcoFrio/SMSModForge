using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace SMSModForge.Tests;

/// <summary>
/// How far along a test run is, for the window that shows it (the author,
/// 2026-09-28: the full suite had grown to a quarter of an hour with nothing
/// to say how much of it was left).
/// <para/>
/// Counting tests alone says the wrong thing. The suite runs one test at a
/// time (see Parallelism.cs), and the thousand-odd fast ones are done in
/// seconds while the window tests take the rest - a bar by count is two thirds
/// full at once and then barely moves. So each test weighs what it took the
/// last time it ran, read from <see cref="Timings"/>; the bar is time done out
/// of time expected, and what is left is the expected time of what has not run
/// yet, at the pace this run is actually going.
/// <para/>
/// With no earlier times for most of the run (the first run on a machine) it
/// falls back to counting, and says nothing about time left rather than
/// guessing.
/// </summary>
internal sealed class RunProgress
{
    /// <summary>The run in progress, once xunit has handed it over.</summary>
    public static RunProgress? Current { get; private set; }

    public sealed record Snapshot(
        int Done, int Total, int Failed, int Skipped,
        double Fraction, TimeSpan Elapsed, TimeSpan? Left,
        string? Running, TimeSpan RunningFor,
        IReadOnlyList<string> Failures, bool Finished);

    private readonly object _gate = new();
    private readonly Func<TimeSpan> _clock;
    private readonly Dictionary<string, string> _names = new();
    private readonly Dictionary<string, double> _expected = new();
    private readonly bool _weighted;
    private readonly Dictionary<string, TimeSpan> _running = new();
    private readonly Dictionary<string, double> _measured = new();
    private readonly List<string> _failures = new();
    private int _skipped;
    private double _doneExpected;
    private bool _finished;

    /// <summary>Set when the run is over.</summary>
    public ManualResetEventSlim Ended { get; } = new();

    /// <summary>Seconds the whole run is expected to take, from the last
    /// times; 0 when too few are known to say.</summary>
    public double ExpectedSeconds { get; }

    /// <param name="cases">Each test's id and the name to show for it.</param>
    /// <param name="lastSeconds">What each test took the last time, by id.</param>
    /// <param name="clock">Time since the run began.</param>
    public RunProgress(IEnumerable<(string Id, string Name)> cases,
                       IReadOnlyDictionary<string, double> lastSeconds,
                       Func<TimeSpan> clock)
    {
        _clock = clock;
        foreach (var (id, name) in cases) _names[id] = name;

        var known = _names.Keys.Where(lastSeconds.ContainsKey).ToList();
        _weighted = _names.Count > 0 && known.Count * 2 >= _names.Count;
        if (!_weighted) return;

        // A test with no time yet is new: it weighs what an average one does.
        double average = known.Average(id => lastSeconds[id]);
        foreach (var id in _names.Keys)
            _expected[id] = Math.Max(0.001, lastSeconds.TryGetValue(id, out var s) ? s : average);
        ExpectedSeconds = _expected.Values.Sum();
    }

    /// <summary>Starts counting the run xunit is about to execute, and makes
    /// it <see cref="Current"/>.</summary>
    public static RunProgress Begin(IEnumerable<(string Id, string Name)> cases,
                                    IReadOnlyDictionary<string, double> lastSeconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        return Current = new RunProgress(cases, lastSeconds, () => watch.Elapsed);
    }

    public int Total => _names.Count;

    public void Started(string id)
    {
        lock (_gate) _running[id] = _clock();
    }

    public void Failed(string name)
    {
        lock (_gate) _failures.Add(name);
    }

    public void Skipped()
    {
        lock (_gate) _skipped++;
    }

    public void Finished(string id)
    {
        lock (_gate)
        {
            var now = _clock();
            if (_running.Remove(id, out var began)) _measured[id] = (now - began).TotalSeconds;
            else _measured.TryAdd(id, 0);
            if (_expected.TryGetValue(id, out var e)) _doneExpected += e;
        }
    }

    public void End()
    {
        lock (_gate) _finished = true;
        Ended.Set();
    }

    /// <summary>What each test that finished took this time, by id.</summary>
    public IReadOnlyDictionary<string, double> Measured
    {
        get { lock (_gate) return new Dictionary<string, double>(_measured); }
    }

    public string NameOf(string id) => _names.TryGetValue(id, out var n) ? n : id;

    public Snapshot Now()
    {
        lock (_gate)
        {
            var now = _clock();
            int done = _measured.Count;
            string? running = null;
            TimeSpan runningFor = TimeSpan.Zero;
            foreach (var (id, began) in _running)
            {
                if (now - began < runningFor && running != null) continue;
                running = NameOf(id);
                runningFor = now - began;
            }

            double fraction;
            TimeSpan? left = null;
            if (_weighted)
            {
                // The one running now counts for as long as it has been going,
                // short of what it took last time: it is not done until it is.
                double partial = 0;
                foreach (var (id, began) in _running)
                    if (_expected.TryGetValue(id, out var e))
                        partial += Math.Min((now - began).TotalSeconds, e * 0.95);
                double doneSoFar = _doneExpected + partial;
                fraction = doneSoFar / ExpectedSeconds;

                // At this run's own pace once there is enough of it to go on:
                // a busy machine is slower than the last run was.
                double pace = 1;
                if (doneSoFar >= 5 && doneSoFar >= ExpectedSeconds * 0.05)
                    pace = Math.Clamp(now.TotalSeconds / doneSoFar, 0.5, 4);
                left = TimeSpan.FromSeconds(Math.Max(0, ExpectedSeconds - doneSoFar) * pace);
            }
            else
            {
                fraction = Total == 0 ? 0 : (double)done / Total;
            }

            if (_finished) { fraction = 1; left = TimeSpan.Zero; running = null; }
            return new Snapshot(done, Total, _failures.Count, _skipped,
                                Math.Clamp(fraction, 0, 1), now, left,
                                running, runningFor, _failures.ToList(), _finished);
        }
    }
}

/// <summary>
/// What each test took the last time it ran, kept beside the test assembly
/// (bin is not tracked) so the next run can say how long it has left. A run
/// updates the tests it ran and keeps the times of the ones it did not, so a
/// filtered run does not forget the rest.
/// </summary>
internal static class Timings
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "test-timings.tsv");

    public static Dictionary<string, double> Read(string? path = null)
    {
        var times = new Dictionary<string, double>();
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return times;
            foreach (var line in File.ReadLines(file))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                    times[parts[0]] = s;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return times;
    }

    public static void Write(RunProgress run, string? path = null)
    {
        var file = path ?? DefaultPath;
        var times = Read(file);
        var names = new Dictionary<string, string>();
        try
        {
            foreach (var line in File.Exists(file) ? File.ReadLines(file) : Enumerable.Empty<string>())
            {
                var parts = line.Split('\t');
                if (parts.Length >= 3) names[parts[0]] = parts[2];
            }
        }
        catch (IOException) { }

        foreach (var (id, seconds) in run.Measured)
        {
            times[id] = seconds;
            names[id] = run.NameOf(id);
        }

        var lines = times.OrderBy(t => names.TryGetValue(t.Key, out var n) ? n : t.Key, StringComparer.Ordinal)
                         .Select(t => t.Key + "\t" + t.Value.ToString("0.###", CultureInfo.InvariantCulture)
                                      + "\t" + (names.TryGetValue(t.Key, out var n) ? n : ""));
        try
        {
            var temp = file + ".tmp";
            File.WriteAllLines(temp, lines);
            File.Move(temp, file, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
