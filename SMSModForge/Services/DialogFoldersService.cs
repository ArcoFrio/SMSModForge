using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SMSModForge.Services;

/// <summary>
/// Remembers the last folder used by each file dialog, keyed by purpose, so
/// (for example) the Export dialog reopens where you last exported a
/// <c>.smspack</c> rather than in the pack's source folder. Persisted across
/// launches to <c>%LocalAppData%/SMSModForge/dialogs.json</c>; best-effort,
/// same as <see cref="RecentFilesService"/> / <see cref="PreviewQualityManager"/>.
/// <para/>
/// Each dialog uses a distinct <see cref="Key"/>, so Open and Export keep
/// independent caches.
/// </summary>
public static class DialogFoldersService
{
    /// <summary>Stable cache keys, one per dialog purpose.</summary>
    public static class Key
    {
        public const string Open = "open";
        public const string Export = "export";
    }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "dialogs.json");

    /// <summary>
    /// The last folder remembered for <paramref name="key"/>, or null when none
    /// is stored or the folder no longer exists (so callers can fall back).
    /// </summary>
    public static string? Get(string key)
    {
        try
        {
            var map = Load();
            if (map.TryGetValue(key, out var dir) && Directory.Exists(dir)) return dir;
        }
        catch { /* best-effort */ }
        return null;
    }

    /// <summary>Remember <paramref name="dir"/> as the folder for <paramref name="key"/>.</summary>
    public static void Set(string key, string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        try
        {
            var map = Load();
            map[key] = dir!;
            var parent = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(map, Formatting.Indented));
        }
        catch { /* best-effort */ }
    }

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
