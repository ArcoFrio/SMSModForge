using System;
using System.IO;
using System.Linq;

namespace SMSModForge.Services;

/// <summary>
/// Guards against saving a pack straight into a folder full of somebody's
/// life.
/// <para/>
/// Saving does not create a subfolder — the folder you pick <em>becomes</em>
/// the pack root — and export bundles that root whole, every file and
/// subfolder. Pick the Desktop and the pack is the Desktop: exporting sweeps
/// every document on it into a .smspack the author then hands out, and the
/// pack takes "Desktop" as its identity in the game.
/// <para/>
/// Nothing about the pack format prevents it, and the mistake is invisible
/// until the archive has already been written, so the check belongs at the
/// moment the folder is chosen.
/// </summary>
public static class PackFolderSafety
{
    /// <summary>Beyond this many existing files, a folder is somebody's
    /// workspace rather than a home for a new pack. Generous: a pack being
    /// re-saved over itself has its own art in there already.</summary>
    private const int CrowdedFileCount = 40;

    /// <summary>
    /// Why this folder is a poor choice, or null when it is fine.
    /// </summary>
    public static string? RiskOf(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;

        string full, untrimmed;
        try
        {
            untrimmed = Path.GetFullPath(dir!);
            full = untrimmed.TrimEnd(Path.DirectorySeparatorChar);
        }
        catch { return null; }

        // A drive root would make the pack the entire drive. Tested on the
        // UNTRIMMED path, and that matters: trimming turns "C:\\" into "C:",
        // which Windows reads as the current directory on drive C rather than
        // its root — so the parent comes back non-null and a drive root sails
        // through.
        try
        {
            if (Directory.GetParent(untrimmed) == null) return "the root of a drive";
        }
        catch { /* unreadable path — fall through to the other checks */ }

        // The well-known folders people actually land in when a save dialog
        // opens and they click Save without navigating.
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.DesktopDirectory,
                     Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.MyPictures,
                     Environment.SpecialFolder.MyMusic,
                     Environment.SpecialFolder.MyVideos,
                     Environment.SpecialFolder.UserProfile,
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.Windows,
                 })
        {
            string known;
            try { known = Environment.GetFolderPath(folder); } catch { continue; }
            if (known.Length == 0) continue;
            if (string.Equals(full, known.TrimEnd(Path.DirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase))
                return "a folder Windows uses for your own files";
        }

        // Downloads has no SpecialFolder on .NET, and is the other place a
        // save dialog commonly opens in.
        try
        {
            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (string.Equals(full, downloads.TrimEnd(Path.DirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase))
                return "your Downloads folder";
        }
        catch { /* ignore */ }

        // Anything already crowded, unless it is plainly a pack being re-saved.
        try
        {
            if (File.Exists(Path.Combine(full, "modpack.json"))) return null;
            int count = Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                                 .Take(CrowdedFileCount + 1).Count();
            if (count > CrowdedFileCount)
                return $"a folder that already holds more than {CrowdedFileCount} files";
        }
        catch { /* unreadable — not our business to block on */ }

        return null;
    }

    /// <summary>
    /// A folder inside <paramref name="dir"/> to offer instead, with a name
    /// that does not already exist there.
    /// </summary>
    public static string SuggestSubfolder(string dir, string packName)
    {
        string baseName = string.IsNullOrWhiteSpace(packName) || packName == "Untitled"
            ? "MyPack" : packName;
        foreach (var c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');

        string candidate = Path.Combine(dir, baseName);
        for (int i = 2; Directory.Exists(candidate) || File.Exists(candidate); i++)
            candidate = Path.Combine(dir, baseName + " " + i);
        return candidate;
    }
}
