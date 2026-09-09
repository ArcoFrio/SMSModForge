using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SMSModForge.Services;

/// <summary>
/// Remembers where each pack was last published, keyed by the pack's on-disk
/// root folder, so the next release opens in the folder the last one went to.
/// <para/>
/// Kept apart from <see cref="ExportPathService"/> rather than sharing its
/// file, because the two folders are usually not the same one and should not
/// pull each other around: an export goes into the game to be tested, a
/// published archive goes wherever the author uploads from.
/// <para/>
/// Stored in the EDITOR's local settings
/// (<c>%LocalAppData%/SMSModForge/publishes.json</c>) and never in the pack
/// manifest: a path carries the author's machine — usernames, drive layout —
/// into a file that is, of all things, the one distributed to strangers.
/// Best-effort persistence, the same as its neighbours.
/// </summary>
public static class PublishPathService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "publishes.json");

    /// <summary>
    /// The last published archive for the pack rooted at
    /// <paramref name="packRoot"/>, or null when there is none or its folder
    /// has gone.
    /// </summary>
    public static string? Get(string? packRoot)
    {
        if (string.IsNullOrWhiteSpace(packRoot)) return null;
        try
        {
            var map = Load();
            if (map.TryGetValue(Normalize(packRoot!), out var file) &&
                !string.IsNullOrEmpty(Path.GetDirectoryName(file)) &&
                Directory.Exists(Path.GetDirectoryName(file)))
                return file;
        }
        catch { /* best-effort */ }
        return null;
    }

    /// <summary>Remember <paramref name="archiveFile"/> as where this pack's
    /// releases go.</summary>
    public static void Set(string? packRoot, string? archiveFile)
    {
        if (string.IsNullOrWhiteSpace(packRoot) || string.IsNullOrWhiteSpace(archiveFile)) return;
        try
        {
            var map = Load();
            map[Normalize(packRoot!)] = archiveFile!;
            var parent = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(map, Formatting.Indented));
        }
        catch { /* best-effort */ }
    }

    private static string Normalize(string root)
        => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(FilePath))
                       ?? new Dictionary<string, string>();
        }
        catch { /* corrupt / unreadable — start fresh */ }
        return new Dictionary<string, string>();
    }
}
