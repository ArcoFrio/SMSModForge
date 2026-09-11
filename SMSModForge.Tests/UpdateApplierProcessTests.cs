using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The hand-over: an editor started with <c>--apply-update</c> copies itself
/// over the install and starts what it wrote.
/// <para/>
/// This is the one part of the updater that had never been run. Everything
/// around it was tested — reading the release, matching the assets, downloading,
/// unpacking, replacing the plugin — and all of it passed while the step they
/// exist to reach **crashed on its first line every time**:
/// <code>
///   StartupUri = null;   // WPF's setter throws ArgumentNullException
/// </code>
/// So the editor downloaded an update, staged it, closed itself, and the staged
/// build died before copying anything. From the outside: the progress prompt,
/// the editor closing, and the same version when you opened it again. No error,
/// because a process that dies of an unhandled exception has nothing left to
/// show one with.
/// <para/>
/// It cannot be tested in-process — the fault is in <c>App.OnStartup</c>, which
/// only WPF calls, and the applier's whole job is to be a second process. So a
/// real editor is started with the real switch against a real folder, and what
/// is measured is what ended up on disk.
/// </summary>
public sealed class UpdateApplierProcessTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public UpdateApplierProcessTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmf-apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* tidying is not the test */ }
    }

    /// <summary>The editor as built beside these tests.</summary>
    private static string? EditorExe()
    {
        string here = Path.Combine(AppContext.BaseDirectory, "SMSModForge.exe");
        return File.Exists(here) ? here : null;
    }

    /// <summary>A process id that has certainly exited, so the applier's wait
    /// for the old editor returns at once.</summary>
    private static int AlreadyGone()
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit")
        { UseShellExecute = false, CreateNoWindow = true })!;
        p.WaitForExit();
        return p.Id;
    }

    [Fact]
    public void TheApplierReplacesTheInstallAndExitsCleanly()
    {
        string? editor = EditorExe();
        if (editor == null) { _out.WriteLine("no built editor beside the tests - skipping"); return; }

        // Somewhere to be replaced, holding a file only the "old" version has,
        // so a copy that did nothing is not mistaken for one that worked.
        string install = Path.Combine(_dir, "install");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "SMSModForge.exe"), "not really the editor");
        File.WriteAllText(Path.Combine(install, "old-version-only.txt"), "1.2.0 was here");

        var start = new ProcessStartInfo(editor)
        {
            WorkingDirectory = Path.GetDirectoryName(editor)!,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(install);
        start.ArgumentList.Add(AlreadyGone().ToString());

        using var applier = Process.Start(start)!;

        // Bounded, and killed rather than waited on: when the applier fails it
        // puts a MessageBox on screen, and this runs on somebody's desktop.
        // A regression must fail the run, never sit on it.
        bool finished = applier.WaitForExit(90_000);
        if (!finished)
        {
            try { applier.Kill(entireProcessTree: true); } catch { }
            Assert.Fail("the applier never exited - it is most likely showing a dialog");
        }

        _out.WriteLine($"applier exit code: {applier.ExitCode} (0x{applier.ExitCode:X8})");

        // 0xE0434352 is an unhandled .NET exception, which is exactly how this
        // failed for every release that shipped the updater.
        Assert.True(applier.ExitCode == 0,
                    $"the applier did not exit cleanly: 0x{applier.ExitCode:X8}"
                    + (applier.ExitCode == unchecked((int)0xE0434352)
                       ? " - an unhandled .NET exception" : ""));

        // It said it would copy itself over the install, so the install should
        // now be this build.
        string landed = Path.Combine(install, "SMSModForge.dll");
        _out.WriteLine("SMSModForge.dll in place: " + File.Exists(landed));
        Assert.True(File.Exists(landed), "nothing was copied into the install folder");

        Assert.Equal(new FileInfo(Path.Combine(AppContext.BaseDirectory, "SMSModForge.dll")).Length,
                     new FileInfo(landed).Length);

        // ...and it copies rather than wiping, because the install folder may
        // hold things that are not the editor's.
        Assert.True(File.Exists(Path.Combine(install, "old-version-only.txt")),
                    "the applier deleted the install folder instead of copying over it");
    }

    [Fact]
    public void StartingNormallyStillOpensTheEditor()
    {
        // The control for the fix, and the thing it could plausibly break: the
        // applier path is escaped by stopping WPF opening a window, and the
        // ordinary path must still open one.
        string? editor = EditorExe();
        if (editor == null) { _out.WriteLine("no built editor beside the tests - skipping"); return; }

        var start = new ProcessStartInfo(editor)
        {
            WorkingDirectory = Path.GetDirectoryName(editor)!,
            UseShellExecute = false,
        };
        using var app = Process.Start(start)!;
        try
        {
            // Polled rather than waited out: the editor is on somebody's screen
            // while this runs, so it is closed the moment the question is
            // answered. A crash on startup answers it immediately.
            var until = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < until)
            {
                if (app.WaitForExit(250))
                {
                    _out.WriteLine($"exited early: {app.ExitCode} (0x{app.ExitCode:X8})");
                    Assert.Fail($"the editor exited on startup instead of opening a window: "
                                + $"0x{app.ExitCode:X8}");
                }
                app.Refresh();
                if (app.MainWindowHandle != IntPtr.Zero) break;
            }

            _out.WriteLine("main window handle: " + app.MainWindowHandle);
            Assert.True(app.MainWindowHandle != IntPtr.Zero,
                        "the editor is running but never opened a window");
        }
        finally
        {
            try { app.Kill(entireProcessTree: true); } catch { }
        }
    }
}
