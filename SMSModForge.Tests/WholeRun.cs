using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace SMSModForge.Tests;

/// <summary>
/// A run of several parts as one: the suite, its timing tests alone, both again in EX, and the in-game suite, in
/// one progress window with one estimate (the author, 2026-11-22: "It'd be nice to have all of the pre-release tests
/// show on a single prompt instead of one large one and a few small ones, so I have a correct estimate of time").
/// Each part used to open a window of its own, each saying only how long IT had left.
/// <para/>
/// Tools/Testing/run-tests.ps1 writes the parts into the run's folder (<c>parts.txt</c>), has each part planned
/// before anything runs (a test process per part that works out its tests and their last times and runs none:
/// <see cref="PlanOnly"/>), and starts this assembly as a program to hold the window (<see cref="Host"/>). The
/// testers then open no window of their own (<c>SMSMODFORGE_PROGRESS_HOST</c>) and leave their notes in their
/// part's folder as before (<see cref="Shards"/>). A part's start and end are files the script leaves there.
/// <para/>
/// What the tests' own times leave out - starting test processes, building windows, launching the game, which a
/// class fixture does outside any test - is learned per part from the last run (<c>test-parts.tsv</c>): a part's
/// estimate is its tests' times plus what that part took beyond them last time.
/// </summary>
internal static class WholeRun
{
    /// <summary>One part of the run, as the script describes it.</summary>
    internal sealed record Part(string Label, int Testers, string Theme, string Title);

    /// <summary>A part's tests, worked out before it runs: how many, and how long its testers will take.</summary>
    internal sealed record Plan(int Total, bool Weighted, (int Total, double Expected)[] Shards)
    {
        /// <summary>The part's own wall time: its slowest tester's share.</summary>
        public double Wall => Shards.Length == 0 ? 0 : Shards.Max(s => s.Expected);
    }

    /// <summary>A part as the window sees it at one moment.</summary>
    internal sealed record PartView(Part Part, Plan? Plan, double? Expected, bool Started, bool Done, RunProgress.Snapshot? Now);

    /// <summary>What a part took last time: the whole of it, and what its tests took within it (its slowest
    /// tester's), the difference being what it costs beyond its tests.</summary>
    internal sealed record Took(double Wall, double Planned);

    /// <summary>
    /// The in-game suite before it has ever been timed in a run of parts: its last measured length (1.9.2,
    /// 2026-11-19: 85 checks in 19.6 minutes). After one run its own times and launch costs take over.
    /// </summary>
    internal static readonly TimeSpan InGameUntilTimed = TimeSpan.FromMinutes(20);

    // ── Planning a part (in a test process that runs nothing) ─────────────

    /// <summary>The number of testers to plan for, when this process is only to plan its part
    /// (<c>SMSMODFORGE_PLAN_ONLY</c>); null otherwise.</summary>
    public static int? PlanOnly
        => int.TryParse(Environment.GetEnvironmentVariable("SMSMODFORGE_PLAN_ONLY"), out int n) && n > 0 ? n : null;

    /// <summary>Whether a window of the whole run is up, so a tester opens none of its own.</summary>
    public static bool Hosted => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SMSMODFORGE_PROGRESS_HOST"));

    public static void WritePlan(string folder, IReadOnlyList<(string Id, string Class)> cases,
                                 IReadOnlyDictionary<string, double> lastSeconds, int testers)
    {
        var assignment = Shards.Assign(cases, lastSeconds, testers);
        var shares = Shards.Plan(cases, lastSeconds, assignment, testers);
        bool weighted = cases.Count > 0 && cases.Count(c => lastSeconds.ContainsKey(c.Id)) * 2 >= cases.Count;
        Write(Path.Combine(folder, "plan.txt"), Format(new Plan(cases.Count, weighted, shares)));
    }

    public static string Format(Plan plan)
    {
        string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
        var lines = new List<string> { "total=" + plan.Total, "weighted=" + (plan.Weighted ? 1 : 0) };
        lines.AddRange(plan.Shards.Select(s => $"shard={s.Total}|{F(s.Expected)}"));
        return string.Join("\n", lines) + "\n";
    }

    public static Plan? ParsePlan(string text)
    {
        int total = -1;
        bool weighted = false;
        var shards = new List<(int, double)>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("total=", StringComparison.Ordinal) && int.TryParse(line[6..], out int t)) total = t;
            else if (line == "weighted=1") weighted = true;
            else if (line.StartsWith("shard=", StringComparison.Ordinal))
            {
                var bits = line[6..].Split('|');
                if (bits.Length == 2 && int.TryParse(bits[0], out int n)
                    && double.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double e))
                    shards.Add((n, e));
            }
        }
        return total < 0 || shards.Count == 0 ? null : new Plan(total, weighted, shards.ToArray());
    }

    // ── The parts, and what they took last time ──────────────────────────

    /// <summary>The parts, one a line: label, testers, theme, title - tab-separated.</summary>
    public static List<Part> ParseParts(string text)
    {
        var parts = new List<Part>();
        foreach (var raw in text.Split('\n'))
        {
            var bits = raw.TrimEnd('\r').Split('\t');
            if (bits.Length == 4 && bits[0].Length > 0 && int.TryParse(bits[1], out int testers))
                parts.Add(new Part(bits[0], Math.Max(1, testers), bits[2], bits[3]));
        }
        return parts;
    }

    public static string HistoryPath => Path.Combine(AppContext.BaseDirectory, "test-parts.tsv");

    public static Dictionary<string, Took> ReadHistory(string? path = null)
    {
        var took = new Dictionary<string, Took>(StringComparer.Ordinal);
        try
        {
            var file = path ?? HistoryPath;
            if (!File.Exists(file)) return took;
            foreach (var line in File.ReadLines(file))
            {
                var bits = line.Split('\t');
                if (bits.Length >= 3
                    && double.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double wall)
                    && double.TryParse(bits[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double planned))
                    took[bits[0]] = new Took(wall, planned);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return took;
    }

    public static void WriteHistory(IReadOnlyDictionary<string, Took> took, string? path = null)
    {
        string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
        Write(path ?? HistoryPath, string.Join("\n", took.OrderBy(t => t.Key, StringComparer.Ordinal)
                                                         .Select(t => $"{t.Key}\t{F(t.Value.Wall)}\t{F(t.Value.Planned)}")) + "\n");
    }

    /// <summary>
    /// How long a part should take, in seconds: its slowest tester's share by the tests' last times, plus what
    /// that part took beyond its tests' times last time. Without the tests' times, what it took last time; for
    /// the in-game suite never yet timed, <see cref="InGameUntilTimed"/>. Null when nothing is known.
    /// </summary>
    public static double? Expected(Part part, Plan? plan, IReadOnlyDictionary<string, Took> history)
    {
        history.TryGetValue(part.Label, out var last);
        if (plan is { Total: 0 }) return 0;   // nothing to run: the script skips it
        if (plan is { Weighted: true })
            return plan.Wall + (last is { Planned: > 0 } ? Math.Max(0, last.Wall - last.Planned) : 0);
        if (last != null) return last.Wall;
        return part.Label == "ingame" ? InGameUntilTimed.TotalSeconds : null;
    }

    // ── All the parts as one run ─────────────────────────────────────────

    /// <summary>
    /// The whole run as the window shows it. The bar is the time done of the time expected, across the parts;
    /// time left is the part running now's own estimate (at its pace) and the parts still to come, at the pace
    /// of the parts already run (<paramref name="pace"/>). Without an estimate for every part it counts tests
    /// and says nothing about time, as one part does.
    /// </summary>
    public static RunProgress.Snapshot Combine(IReadOnlyList<PartView> parts, TimeSpan elapsed, double pace = 1)
    {
        int done = 0, total = 0, failed = 0, skipped = 0;
        var failures = new List<string>();
        foreach (var p in parts)
        {
            done += p.Now?.Done ?? 0;
            total += p.Plan?.Total ?? p.Now?.Total ?? 0;
            failed += p.Now?.Failed ?? 0;
            skipped += p.Now?.Skipped ?? 0;
            string where = p.Part.Theme.Length > 0 ? " - in " + p.Part.Theme : "";
            if (p.Now != null) failures.AddRange(p.Now.Failures.Select(f => f + where));
        }

        bool finished = parts.All(p => p.Done);
        bool timed = parts.All(p => p.Expected != null);
        double Fraction(PartView p) => p.Done ? 1 : p.Started ? p.Now?.Fraction ?? 0 : 0;

        double fraction;
        if (timed && parts.Sum(p => p.Expected!.Value) > 0)
            fraction = parts.Sum(p => p.Expected!.Value * Fraction(p)) / parts.Sum(p => p.Expected!.Value);
        else
            fraction = total == 0 ? 0 : (double)done / total;

        int at = -1;
        for (int i = 0; i < parts.Count; i++)
            if (!parts[i].Done) { at = i; break; }

        TimeSpan? left = TimeSpan.Zero;
        if (!finished)
        {
            double seconds = 0;
            for (int i = at; i < parts.Count && left != null; i++)
            {
                var p = parts[i];
                if (p.Started && p.Now?.Left is { } own) { seconds += own.TotalSeconds; continue; }
                if (p.Expected is not { } e) { left = null; break; }
                seconds += e * (1 - Fraction(p)) * (p.Started ? 1 : pace);
            }
            if (left != null) left = TimeSpan.FromSeconds(seconds);
        }

        var running = at >= 0 && parts[at].Started ? parts[at].Now : null;
        string? title = finished ? null
                      : at >= 0 && parts[at].Started ? $"Part {at + 1} of {parts.Count}: {parts[at].Part.Title}"
                      : at > 0 ? $"Part {at + 1} of {parts.Count}: {parts[at].Part.Title} (starting)"
                      : "Getting ready: working out what each part will run";
        return new RunProgress.Snapshot(done, total, failed, skipped, finished ? 1 : Math.Clamp(fraction, 0, 1),
                                        elapsed, finished ? TimeSpan.Zero : left,
                                        finished ? null : running?.Running, running?.RunningFor ?? TimeSpan.Zero,
                                        failures, finished, title);
    }

    // ── The window's process ─────────────────────────────────────────────

    /// <summary>
    /// Holds the window for the run in <paramref name="root"/> until the script says it is done (a "done" file)
    /// or the script is gone, then files what each part took.
    /// </summary>
    public static int Host(string root, int scriptPid)
    {
        var watch = new Watcher(root);
        if (watch.Parts.Count == 0) return 1;
        RunProgressWindow.OfferFor(watch.Now, testers: 1);
        while (!File.Exists(Path.Combine(root, "done")) && Alive(scriptPid)) Thread.Sleep(250);
        watch.FileWhatEachPartTook();
        Thread.Sleep(1500);   // the window says Done before it goes
        return 0;
    }

    /// <summary>The run in a folder, read as the window needs it: the parts, their plans, their testers' notes.</summary>
    internal sealed class Watcher
    {
        private readonly string _root;
        private readonly Dictionary<string, Took> _history;
        private readonly string? _historyPath;
        private readonly Plan?[] _plans;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        public IReadOnlyList<Part> Parts { get; }

        public Watcher(string root, string? historyPath = null)
        {
            _root = root;
            _historyPath = historyPath;
            Parts = ParseParts(SafeRead(Path.Combine(root, "parts.txt")) ?? "");
            _history = ReadHistory(historyPath);
            _plans = new Plan?[Parts.Count];
        }

        public RunProgress.Snapshot Now()
        {
            var views = new List<PartView>();
            double ranFor = 0, ranExpected = 0;
            for (int i = 0; i < Parts.Count; i++)
            {
                string folder = Path.Combine(_root, Parts[i].Label);
                _plans[i] ??= SafeRead(Path.Combine(folder, "plan.txt")) is { } text ? ParsePlan(text) : null;
                var started = Stamp(Path.Combine(folder, "started"));
                var ended = Stamp(Path.Combine(folder, "done"));
                double? expected = Expected(Parts[i], _plans[i], _history);
                views.Add(new PartView(Parts[i], _plans[i], expected, started != null, ended != null,
                                       started != null ? PartNow(folder, Parts[i], _plans[i], ended != null) : null));
                if (started != null && ended != null && expected is > 0)
                {
                    ranFor += (ended.Value - started.Value).TotalSeconds;
                    ranExpected += expected.Value;
                }
            }
            // Later parts at the pace of those already run: a machine busy with something else is slower for all.
            double pace = ranExpected >= 30 ? Math.Clamp(ranFor / ranExpected, 0.5, 4) : 1;
            return Combine(views, _clock.Elapsed, pace);
        }

        /// <summary>What each part took, against what its tests' times said, for the next run's estimate.</summary>
        public void FileWhatEachPartTook()
        {
            for (int i = 0; i < Parts.Count; i++)
            {
                string folder = Path.Combine(_root, Parts[i].Label);
                _plans[i] ??= SafeRead(Path.Combine(folder, "plan.txt")) is { } text ? ParsePlan(text) : null;
                if (Stamp(Path.Combine(folder, "started")) is not { } s || Stamp(Path.Combine(folder, "done")) is not { } e
                    || _plans[i] is not { Total: > 0 } plan) continue;
                // Against what its tests took THIS time (its slowest tester's), so a part planned without times -
                // the in-game suite's first run - still learns what it costs beyond them (the game's launch).
                double tests = Enumerable.Range(0, Parts[i].Testers)
                                         .Select(k => Timings.Read(Path.Combine(folder, $"tester-{k + 1}.timings.tsv")).Values.Sum())
                                         .DefaultIfEmpty(0).Max();
                double planned = tests > 0 ? tests : plan.Weighted ? plan.Wall : 0;
                if (planned > 0) _history[Parts[i].Label] = new Took((e - s).TotalSeconds, planned);
            }
            WriteHistory(_history, _historyPath);
        }
    }

    /// <summary>One part's testers as one, from their notes (the same sum a tester 1 made for its own window).</summary>
    private static RunProgress.Snapshot? PartNow(string folder, Part part, Plan? plan, bool ended)
    {
        var statuses = Enumerable.Range(0, part.Testers).Select(i => ReadStatus(folder, i)).ToList();
        if (statuses.All(s => s == null) && plan == null) return null;
        var shares = plan?.Shards.Length == part.Testers ? plan.Shards
                   : statuses.Select(s => (s?.Now.Total ?? 0, 0.0)).ToArray();
        var own = statuses[0]?.Now ?? new RunProgress.Snapshot(0, shares[0].Item1, 0, 0, 0, TimeSpan.Zero,
            shares[0].Item2 > 0 ? TimeSpan.FromSeconds(shares[0].Item2) : null, null, TimeSpan.Zero, Array.Empty<string>(), false);
        var others = statuses.Skip(1).ToList();
        var now = Shards.Combine(own, others, others.Select(Shards.Gone).ToList(), shares);
        // Over, by the script's word: a tester that never said it was done is not going to.
        return ended ? now with { Finished = true, Fraction = 1, Left = TimeSpan.Zero, Running = null } : now;
    }

    private static Shards.Status? ReadStatus(string folder, int index)
    {
        var text = SafeRead(Path.Combine(folder, $"tester-{index + 1}.status"));
        return text == null ? null : Shards.Parse(text);
    }

    private static DateTime? Stamp(string file)
    {
        try { return File.Exists(file) ? File.GetLastWriteTimeUtc(file) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? SafeRead(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void Write(string file, string text)
    {
        try
        {
            string temp = file + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, file, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool Alive(int pid)
    {
        if (pid <= 0) return true;
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}

/// <summary>
/// The test assembly started as a program (<c>dotnet SMSModForge.Tests.dll --whole-run &lt;folder&gt;</c>): the
/// window of a run of several parts (<see cref="WholeRun.Host"/>). A test run never calls this - the test host
/// loads the assembly as a library - and it does nothing without that argument.
/// </summary>
internal static class Entry
{
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--whole-run")
        {
            int script = args.Length >= 4 && args[2] == "--script" && int.TryParse(args[3], out int pid) ? pid : 0;
            return WholeRun.Host(args[1], script);
        }
        // What the window would show for a run's folder, once, as text: for looking into a run.
        if (args.Length >= 2 && args[0] == "--whole-run-state")
        {
            var s = new WholeRun.Watcher(args[1]).Now();
            Console.WriteLine(s.Part);
            Console.Write(Shards.Format(new Shards.Status(0, s)));
            return 0;
        }
        return 0;
    }
}
