using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SMSModForge.Services;

/// <summary>
/// Remembers where each pack was last exported, keyed by the pack's on-disk
/// root folder, so "Export pack" can re-export in one click while "Export
/// pack as…" stays the choose-a-file path.
/// <para/>
/// Deliberately stored in the EDITOR's local settings
/// (<c>%LocalAppData%/SMSModForge/exports.json</c>) and never in the pack
/// manifest: an export path is a local filesystem detail (usernames, drive
/// layout) that would leak the author's machine into a file that gets
/// distributed. Best-effort persistence, same as
/// <see cref="DialogFoldersService"/>.
/// </summary>
public static class ExportPathService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "exports.json");

    /// <summary>
    /// The last export target for the pack rooted at <paramref name="packRoot"/>,
    /// or null when none is stored or its folder no longer exists (caller
    /// falls back to the Save-as dialog).
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

    /// <summary>Remember <paramref name="exportFile"/> as the export target
    /// for the pack rooted at <paramref name="packRoot"/>.</summary>
    public static void Set(string? packRoot, string? exportFile)
    {
        if (string.IsNullOrWhiteSpace(packRoot) || string.IsNullOrWhiteSpace(exportFile)) return;
        try
        {
            var map = Load();
            map[Normalize(packRoot!)] = exportFile!;
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
