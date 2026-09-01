using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SMSModForge.Services;

/// <summary>
/// Downloads a release and puts it in place.
/// <para/>
/// Two halves, because they are two different problems. The PLUGIN lives in the
/// game folder, nothing is holding those files open, and it can be replaced
/// where it stands. The EDITOR is the program doing the replacing: Windows will
/// not let a running exe be overwritten, so the new build is staged next to the
/// settings, and a copy of it started from there does the swap once this process
/// has exited. See <see cref="UpdateApplier"/>.
/// <para/>
/// Everything here reports rather than throws. A failed update leaves the
/// editor exactly as it was, which is the whole reason the download is staged
/// somewhere else first instead of being unpacked over the install.
/// </summary>
public static class UpdateInstaller
{
    /// <summary>Where downloads and staged builds go: beside the editor's other
    /// settings, which is a folder the author can always write to, and never
    /// inside the install, which may not be.</summary>
    public static string StagingRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "updates");

    /// <summary>The folder the running editor was started from — what an editor
    /// update replaces.</summary>
    public static string InstallFolder =>
        Path.GetDirectoryName(Environment.ProcessPath
                              ?? System.Reflection.Assembly.GetExecutingAssembly().Location)
        ?? AppContext.BaseDirectory;

    // ── The game folder ───────────────────────────────────────────────

    /// <summary>
    /// Whether this looks like a Starmaker Story folder with BepInEx in it.
    /// <para/>
    /// The test is the plugins folder rather than the game exe: that is the
    /// thing an update writes to, and a game folder without BepInEx is one where
    /// the plugin has nowhere to go. Nothing here creates it — somebody who has
    /// not installed BepInEx needs the README, not a DLL dropped into a folder
    /// the game will never read.
    /// </summary>
    public static bool IsGameFolder(string? folder)
        => !string.IsNullOrWhiteSpace(folder)
           && Directory.Exists(Path.Combine(folder!, "BepInEx", "plugins"));

    /// <summary>
    /// Which entries of the plugin zip belong to ModForge.
    /// <para/>
    /// The published zip is a whole BepInEx bundle — the loader, its core, the
    /// doorstop shim — because that is what a first-time install needs. An
    /// UPDATE is not a first-time install: the loader is already there, it may
    /// have been updated by hand, and other mods are relying on it. Replacing it
    /// would be reaching well past what an editor update was asked to do.
    /// <para/>
    /// So: only what is directly under <c>BepInEx/plugins</c>, and never
    /// <c>BepInEx/plugins/SMSModForge</c> — that is where ModPacks lives, and it
    /// holds the author's own work.
    /// </summary>
    public static bool IsOursInPluginZip(string entryPath)
    {
        string p = entryPath.Replace('\\', '/').TrimStart('/');
        if (p.EndsWith("/")) return false;                       // a directory entry

        const string plugins = "BepInEx/plugins/";
        if (!p.StartsWith(plugins, StringComparison.OrdinalIgnoreCase)) return false;

        string rest = p.Substring(plugins.Length);
        return !rest.StartsWith("SMSModForge/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What an attempt to replace the plugin managed.</summary>
    /// <param name="Written">Relative paths replaced, in the order written.</param>
    /// <param name="Failed">Relative path and reason, for each file that could
    /// not be replaced.</param>
    public sealed record PluginUpdate(
        IReadOnlyList<string> Written,
        IReadOnlyList<(string File, string Why)> Failed)
    {
        public bool Complete => Failed.Count == 0;
    }

    /// <summary>
    /// Unpack the ModForge part of a plugin zip into a game folder.
    /// <para/>
    /// One file failing does not abandon the rest, and does not throw. A DLL
    /// held open by something — the game, a debugger, a build that put it there
    /// — used to stop the loop where it stood, leaving the folder half replaced
    /// and the caller with an exception that said nothing about how far it got.
    /// Every file is now tried, and what did not work is reported by name.
    /// </summary>
    public static PluginUpdate ApplyPluginZip(string zipPath, string gameFolder)
    {
        var written = new List<string>();
        var failed = new List<(string, string)>();
        using var zip = ZipFile.OpenRead(zipPath);

        foreach (var entry in zip.Entries)
        {
            if (!IsOursInPluginZip(entry.FullName)) continue;

            string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(gameFolder, relative));

            // A zip can name its way out of the folder it is being unpacked
            // into. Ours does not, but a downloaded file is a downloaded file.
            if (!target.StartsWith(Path.GetFullPath(gameFolder), StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);

                // Written is not the same as written CORRECTLY. Checking the
                // length turns a write that quietly did nothing into one that
                // says so, which is the difference between a plugin that is
                // out of step and an author who knows it is.
                long onDisk = new FileInfo(target).Length;
                if (onDisk != entry.Length)
                    throw new IOException(
                        $"wrote {onDisk} bytes where the download has {entry.Length}");

                written.Add(relative);
            }
            catch (Exception ex)
            {
                failed.Add((relative, ex.Message));
            }
        }
        return new PluginUpdate(written, failed);
    }

    // ── Downloading ───────────────────────────────────────────────────

    /// <summary>
    /// Fetch a URL to a file, reporting progress from 0 to 1 where the server
    /// says how big it is.
    /// </summary>
    public static async Task DownloadAsync(HttpClient http, string url, string toFile,
                                           IProgress<double>? progress,
                                           CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(toFile)!);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel)
                                       .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using var from = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        await using var to = File.Create(toFile);

        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await from.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
            done += read;
            if (total is > 0) progress?.Report((double)done / total.Value);
        }
    }

    // ── Staging the editor ────────────────────────────────────────────

    /// <summary>
    /// Whether a folder holds something that could actually replace the editor.
    /// <para/>
    /// Checked before anything is overwritten, because the failure this guards
    /// against is the expensive one: a truncated download, an HTML error page
    /// saved as a zip, a release whose editor asset was still uploading. Any of
    /// those unpack to something, and unpacking that over a working install
    /// leaves the author with no editor at all.
    /// </summary>
    public static bool LooksLikeAnEditorBuild(string folder)
        => File.Exists(Path.Combine(folder, "SMSModForge.exe"))
           && File.Exists(Path.Combine(folder, "SMSModForge.dll"))
           && File.Exists(Path.Combine(folder, "SMSModForge.runtimeconfig.json"));

    /// <summary>The staging folder for one version.</summary>
    public static string StagingFor(string versionText) => Path.Combine(StagingRoot, versionText);

    /// <summary>
    /// Unpack an editor zip into its staging folder and check what came out.
    /// </summary>
    /// <returns>The staged folder, or null with <paramref name="problem"/> set.</returns>
    public static string? StageEditorZip(string zipPath, string versionText, out string? problem)
    {
        problem = null;
        string staged = Path.Combine(StagingFor(versionText), "editor");
        try
        {
            if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
            Directory.CreateDirectory(staged);
            ZipFile.ExtractToDirectory(zipPath, staged, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            problem = "The download could not be unpacked: " + ex.Message;
            return null;
        }

        // The zip is published flat, but a release built differently could carry
        // a single top-level folder instead. Step into it rather than failing.
        if (!LooksLikeAnEditorBuild(staged))
        {
            var inner = Directory.GetDirectories(staged);
            if (inner.Length == 1 && LooksLikeAnEditorBuild(inner[0])) staged = inner[0];
        }

        if (!LooksLikeAnEditorBuild(staged))
        {
            problem = "The download does not contain an editor build, so nothing was replaced.";
            return null;
        }
        return staged;
    }

    /// <summary>
    /// Delete staged builds left behind by earlier updates.
    /// <para/>
    /// Called on startup, not after applying: the process that does the swap is
    /// RUNNING from the folder that needs deleting, so it is the editor it
    /// starts afterwards that can finally clear it.
    /// </summary>
    public static void CleanStaging(string? keepVersion = null)
    {
        try
        {
            if (!Directory.Exists(StagingRoot)) return;
            foreach (var dir in Directory.GetDirectories(StagingRoot))
            {
                if (keepVersion != null &&
                    string.Equals(Path.GetFileName(dir), keepVersion, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { Directory.Delete(dir, recursive: true); }
                catch { /* in use, or gone already; it will be tried again next start */ }
            }
        }
        catch { /* housekeeping never matters enough to report */ }
    }

    // ── Handing over ──────────────────────────────────────────────────

    /// <summary>
    /// Start the staged build as an applier and ask this editor to quit.
    /// <para/>
    /// The staged copy is a complete editor, so it can do the swap itself — no
    /// second executable to ship, no script to write out, and the code doing the
    /// copying is the code that shipped with the version being installed.
    /// </summary>
    public static bool LaunchApplier(string stagedFolder, string installFolder, out string? problem)
    {
        problem = null;
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(stagedFolder, "SMSModForge.exe"),
                WorkingDirectory = stagedFolder,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(UpdateApplier.Switch);
            start.ArgumentList.Add(installFolder);
            start.ArgumentList.Add(Environment.ProcessId.ToString());

            return Process.Start(start) != null;
        }
        catch (Exception ex)
        {
            problem = "The update could not be started: " + ex.Message;
            return false;
        }
    }
}
