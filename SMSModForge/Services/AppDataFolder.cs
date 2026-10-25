using System;
using System.IO;
using System.Linq;

namespace SMSModForge.Services;

/// <summary>
/// Where the editor keeps what it remembers between sessions: the author's
/// %LocalAppData%\SMSModForge - or, in a test run, a folder of that run's own.
/// <para/>
/// A test run used to read and write the author's real one. Their export and
/// publish history picked up the suite's temp folders, their own translations
/// were read as though they shipped with the editor (so a file on their
/// machine could change what the tests saw), and once several test processes
/// ran side by side (the parallel runner, 1.8.0) two of them could rewrite one
/// file at the same moment. Every path in there goes through
/// <see cref="PathOf"/>, so none of that can happen again.
/// <para/>
/// Asked for each time rather than kept: a path taken before the run said it
/// was a test run would be the author's for the rest of it.
/// </summary>
public static class AppDataFolder
{
    private static readonly object Gate = new();
    private static string? _testRoot;

    /// <summary>The author's own folder, whatever kind of run this is.</summary>
    public static string Real => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMSModForge");

    /// <summary>The folder this run keeps things in.</summary>
    public static string Root => TestMode.Active ? TestRoot : Real;

    /// <summary>A file or folder inside <see cref="Root"/>.</summary>
    public static string PathOf(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    /// <summary>All test runs' folders, one per test process.</summary>
    public static string TestRuns => Path.Combine(Path.GetTempPath(), "SMSModForge-tests");

    /// <summary>
    /// This process's folder, emptied the first time it is asked for: a
    /// process id comes round again, and a run must not start with what an
    /// earlier one left. Folders of processes that have gone are cleared on
    /// the way.
    /// </summary>
    private static string TestRoot
    {
        get
        {
            lock (Gate)
            {
                if (_testRoot != null) return _testRoot;
                string mine = Path.Combine(TestRuns, "appdata-" + Environment.ProcessId);
                try
                {
                    if (Directory.Exists(TestRuns))
                        foreach (var dir in Directory.GetDirectories(TestRuns, "appdata-*"))
                            if (dir == mine || !Alive(Path.GetFileName(dir)["appdata-".Length..]))
                                Directory.Delete(dir, recursive: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return _testRoot = mine;
            }
        }
    }

    private static bool Alive(string pid)
    {
        if (!int.TryParse(pid, out int id)) return false;
        try { using var p = System.Diagnostics.Process.GetProcessById(id); return !p.HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
