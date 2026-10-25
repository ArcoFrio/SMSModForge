using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace SMSModForge.Tests;

/// <summary>
/// The test copy of the game, for the in-game suite (1.8.0, 12.13): started
/// and closed through <c>Tools/Game</c> - so by its rules: its own desktop,
/// muted, never while the author's game runs, only the test save - and driven
/// through the plugin's test bridge (DebugBridge), the way <c>bridge.ps1</c>
/// drives it.
/// <para/>
/// Off unless <c>SMSMODFORGE_INGAME</c> names the test copy;
/// <c>Tools/Game/ingame-suite.ps1</c> sets it.
/// </summary>
internal sealed class GameRun : IDisposable
{
    public const string Variable = "SMSMODFORGE_INGAME";
    public const string BridgeName = "suite";

    /// <summary>The test copy, or null when the suite was not asked for.</summary>
    public static string? TestCopy
        => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } t ? t.TrimEnd('\\', '/') : null;

    public string Test { get; }
    public string Tools { get; }
    public string BridgeFolder { get; }
    public int Pid { get; private set; }

    /// <summary>Everything the bridge answered this run, for the report.</summary>
    public List<string> Transcript { get; } = new();

    public GameRun(string test)
    {
        Test = test;
        Tools = Path.Combine(RepoRoot(), "Tools", "Game");
        BridgeFolder = Path.Combine(test, "BepInEx", "plugins", "SMSModForge", "DebugBridge", BridgeName);
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tools", "Game", "launch.ps1"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No Tools/Game above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Starts the test copy: on the main menu (slot 0) or straight into the
    /// test save, and waits for the bridge to say it is ready.
    /// </summary>
    public void Start(int slot)
    {
        // The launcher starts the bridge's folder clean: what it says from here
        // on is this game's.
        string results = Path.Combine(BridgeFolder, "results.txt");
        var output = Script("launch.ps1", "-Slot", slot.ToString(), "-Bridge", BridgeName);
        var last = output.LastOrDefault(l => Regex.IsMatch(l, @"^\d+$"));
        if (last == null) throw new InvalidOperationException("The game did not start:\n" + string.Join("\n", output));
        Pid = int.Parse(last);
        Transcript.Add("launch: " + string.Join(" / ", output));

        var deadline = DateTime.Now.AddSeconds(90);
        while (DateTime.Now < deadline)
        {
            if (File.Exists(results) && Lines(results).Any(l => l.Contains("bridge ready"))) return;
            if (!Running()) throw new InvalidOperationException($"The game closed while starting - see {Test}\\Player-{BridgeName}.log");
            Thread.Sleep(250);
        }
        throw new TimeoutException("The bridge never said it was ready.");
    }

    public bool Running()
    {
        try { using var p = Process.GetProcessById(Pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Closes the test copy (stop.ps1: asks, then ends it).</summary>
    public void Stop()
    {
        if (Pid == 0) return;
        Transcript.Add("stop: " + string.Join(" / ", Script("stop.ps1", "-Bridge", BridgeName)));
        Pid = 0;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Sends commands and waits for one answer per command - <c>ok</c>,
    /// <c>error</c> or <c>timeout</c> - returning every line the bridge wrote
    /// meanwhile.
    /// </summary>
    public List<string> Send(int timeoutSeconds, params string[] commands)
    {
        string results = Path.Combine(BridgeFolder, "results.txt");
        string file = Path.Combine(BridgeFolder, "commands.txt");
        int before = File.Exists(results) ? Lines(results).Count : 0;
        // The bridge deletes the file when it has read it; one batch at a time.
        for (int i = 0; File.Exists(file) && i < 200; i++) Thread.Sleep(50);
        File.WriteAllText(file, string.Join("\n", commands));
        var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
        List<string> said = new();
        while (DateTime.Now < deadline)
        {
            said = File.Exists(results) ? Lines(results).Skip(before).ToList() : new();
            if (said.Count(IsAnswer) >= commands.Length) break;
            if (!Running()) throw new InvalidOperationException("The game closed during: " + string.Join("; ", commands));
            Thread.Sleep(100);
        }
        foreach (var l in said) Transcript.Add(l);
        if (said.Count(IsAnswer) < commands.Length)
            throw new TimeoutException($"No answer in {timeoutSeconds} s to: {string.Join("; ", commands)}\n{string.Join("\n", said)}");
        return said;
    }

    /// <summary>One command, and what came after its <c>ok</c> and the verb
    /// the bridge says back (<c>ok level 5_MyRoom</c> answers "5_MyRoom"); an
    /// <c>error</c> or <c>timeout</c> throws, saying what the bridge said.</summary>
    public string Ask(string command, int timeoutSeconds = 30)
    {
        var said = Send(timeoutSeconds, command);
        var answer = said.Last(IsAnswer);
        var m = Regex.Match(answer, @"^\S+ ok (.*)$");
        if (!m.Success) throw new BridgeError(command, answer);
        string verb = command.Split(' ')[0] + " ";
        string rest = m.Groups[1].Value;
        return rest.StartsWith(verb, StringComparison.Ordinal) ? rest.Substring(verb.Length) : rest;
    }

    /// <summary>A pack's variable, as the game holds it now.</summary>
    public string PackVar(string pack, string name)
    {
        string a = Ask($"pvar {pack} {name}");
        return a.Substring(a.IndexOf(" = ", StringComparison.Ordinal) + 3);
    }

    /// <summary>
    /// Asks until <paramref name="ready"/> accepts the answer, or the time is
    /// up - the game does things over frames and seconds (a journey's fade,
    /// a gate's next tick). The last answer either way.
    /// </summary>
    public string AskUntil(string command, Func<string, bool> ready, double seconds)
    {
        var deadline = DateTime.Now.AddSeconds(seconds);
        string answer = Ask(command);
        while (!ready(answer) && DateTime.Now < deadline)
        {
            Thread.Sleep(250);
            answer = Ask(command);
        }
        return answer;
    }

    private static bool IsAnswer(string line) => Regex.IsMatch(line, @"^\d\d:\d\d:\d\d\.\d{3} (ok|error|timeout)");

    private static List<string> Lines(string path)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var r = new StreamReader(s);
                var lines = new List<string>();
                while (r.ReadLine() is { } l) lines.Add(l);
                return lines;
            }
            catch (IOException) when (i < 20) { Thread.Sleep(50); }
        }
    }

    /// <summary>Runs one of Tools/Game's scripts with no window anywhere,
    /// and returns what it printed. A script that throws throws here.</summary>
    private List<string> Script(string name, params string[] arguments)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Tools,
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Tools, name), "-Test", Test }.Concat(arguments))
            start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        string output = p.StandardOutput.ReadToEnd();
        string errors = p.StandardError.ReadToEnd();
        p.WaitForExit();
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        if (p.ExitCode != 0 || errors.Trim().Length > 0)
            throw new InvalidOperationException($"{name} failed ({p.ExitCode}):\n{output}\n{errors}");
        return lines;
    }
}

/// <summary>The bridge answered a command with an error.</summary>
internal sealed class BridgeError : Exception
{
    public BridgeError(string command, string answer) : base($"{command} -> {answer}") { }
}
