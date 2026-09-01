using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SMSModForge.Services;

/// <summary>
/// The few seconds between two editors: this one is the staged copy of the NEW
/// version, started by the old one, and its whole job is to copy itself over the
/// install and start what it wrote.
/// <para/>
/// It runs before any window exists — see App.OnStartup — because there is
/// nothing to show. The old editor is closing, the new one is not up yet, and
/// putting a window in between would only be something to click on while files
/// are being replaced.
/// <para/>
/// It copies rather than replacing the folder wholesale. A stale file left from
/// an older version is untidy; deleting a folder somebody extracted the editor
/// into, which may hold things of theirs, is not recoverable.
/// </summary>
public static class UpdateApplier
{
    /// <summary>The argument that turns a normal start into this.</summary>
    public const string Switch = "--apply-update";

    /// <summary>
    /// Passed to the editor this starts afterwards, with the version it is
    /// replacing.
    /// <para/>
    /// An updated editor otherwise comes up knowing nothing: it runs its
    /// ordinary check, finds it is already the latest, and says nothing at all
    /// - so the last thing anybody saw was the old editor closing itself, and
    /// whether that worked was left to be inferred from the title bar.
    /// </summary>
    public const string UpdatedSwitch = "--updated";

    /// <summary>How long to wait for the old editor to let go of its files. It
    /// has been asked to close and has nothing to do but close; well past this
    /// and something is wrong, and the copy is attempted anyway rather than
    /// leaving the author with neither version running.</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether this editor is the one an update just put in place, and which
    /// version it replaced (empty when that could not be read).
    /// </summary>
    public static bool WasJustUpdated(string[] args, out string fromVersion)
    {
        fromVersion = "";
        int at = Array.IndexOf(args, UpdatedSwitch);
        if (at < 0) return false;
        if (at + 1 < args.Length) fromVersion = args[at + 1];
        return true;
    }

    /// <summary>
    /// Whether this process was started to apply an update, and what it was
    /// told to do.
    /// </summary>
    public static bool WasAskedToApply(string[] args, out string installFolder, out int oldProcessId)
    {
        installFolder = "";
        oldProcessId = 0;

        int at = Array.IndexOf(args, Switch);
        if (at < 0 || at + 1 >= args.Length) return false;

        installFolder = args[at + 1];
        if (at + 2 < args.Length) int.TryParse(args[at + 2], out oldProcessId);
        return !string.IsNullOrWhiteSpace(installFolder);
    }

    /// <summary>
    /// Wait for the old editor, copy this build over the install, start it, and
    /// say whether that worked.
    /// </summary>
    public static bool Apply(string installFolder, int oldProcessId, out string? problem)
    {
        problem = null;
        WaitForExit(oldProcessId);

        string from = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        // Before the copy, because after it the folder holds this build and
        // the version being replaced is gone.
        string replaced = VersionOf(Path.Combine(installFolder, "SMSModForge.dll"));

        try
        {
            CopyOver(from, installFolder);
        }
        catch (UnauthorizedAccessException)
        {
            problem = "The editor's folder could not be written to:\n" + installFolder +
                      "\n\nThis usually means it is installed somewhere that needs " +
                      "administrator rights. Moving it out of Program Files, or " +
                      "unpacking the download yourself, will both work.";
            return false;
        }
        catch (Exception ex)
        {
            problem = "The update could not be copied into place: " + ex.Message;
            return false;
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(installFolder, "SMSModForge.exe"),
                WorkingDirectory = installFolder,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(UpdatedSwitch);
            start.ArgumentList.Add(replaced);
            Process.Start(start);
        }
        catch (Exception ex)
        {
            // The files ARE updated at this point, so this is not a failed
            // update - just one nobody was let back into.
            problem = "The update is installed, but the editor could not be " +
                      "restarted: " + ex.Message;
            return false;
        }
        return true;
    }

    /// <summary>The file version of a build, or empty if it cannot be read —
    /// which is not worth failing an update over, only worth saying less
    /// afterwards.</summary>
    private static string VersionOf(string assembly)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(assembly);
            // No version is not version zero: an editor that says it updated
            // "from 0.0.0" has invented a fact. Say less instead.
            if (string.IsNullOrWhiteSpace(info.FileVersion)) return "";
            return new Version(info.FileVersion).ToString(3);
        }
        catch { return ""; }
    }

    private static void WaitForExit(int processId)
    {
        if (processId <= 0) return;
        try
        {
            using var old = Process.GetProcessById(processId);
            old.WaitForExit((int)ExitWait.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // Already gone, which is the outcome being waited for.
        }
        catch { /* not worth failing an update over */ }

        // Windows can hold a file open for a moment after the process holding it
        // has gone. A short settle beats a copy that fails on one DLL.
        Thread.Sleep(400);
    }

    /// <summary>Copy every file, making folders as needed, overwriting what is
    /// there. Public for the test, which runs it over temp folders rather than
    /// over an editor.</summary>
    public static void CopyOver(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (string source in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, source);
            string target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // Clear read-only rather than failing on it: files unpacked from a
            // zip by some tools carry it, and a stale flag on one DLL should not
            // stop an update.
            if (File.Exists(target))
            {
                var attributes = File.GetAttributes(target);
                if (attributes.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
            }
            File.Copy(source, target, overwrite: true);
        }
    }
}
