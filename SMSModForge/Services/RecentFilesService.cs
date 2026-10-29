using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SMSModForge.Services;

public static class RecentFilesService
{
    private const int MaxEntries = 10;

    private static string FilePath => AppDataFolder.PathOf("recent.json");

    public static List<string> Load()
    {
        // The suite opens packs from temporary folders by the hundred; they
        // went into the author's own File > Recent list, and pushed their
        // real packs off it (2026-09-27). Nothing the suite does is theirs.
        if (TestMode.Active) return new List<string>();
        try
        {
            if (!File.Exists(FilePath)) return new List<string>();
            var json = File.ReadAllText(FilePath);
            return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
        }
        catch { return new List<string>(); }
    }

    public static void Add(string path)
    {
        if (TestMode.Active) return;
        var list = Load();
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);
        Save(list);
    }

    /// <summary>Take a pack off the list - the ✕ beside it in File > Recent
    /// files. Only the list changes; the pack's folder is not touched.</summary>
    public static void Remove(string path)
    {
        if (TestMode.Active) return;
        var list = Load();
        if (list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0) Save(list);
    }

    private static void Save(List<string> list)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(list, Formatting.Indented));
        }
        catch { /* best-effort */ }
    }
}
