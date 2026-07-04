using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SMSModForge.Services;

/// <summary>
/// Persists user-adjusted UI sizes (resizable GridSplitter columns/rows)
/// across sessions, keyed by a stable name. Best-effort JSON in
/// <c>%LocalAppData%/SMSModForge/layout.json</c> — same pattern as
/// <see cref="RecentFilesService"/>; a missing/corrupt file just means
/// the layout falls back to its XAML defaults.
/// </summary>
public static class UiLayoutService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "layout.json");

    public static Dictionary<string, double> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonConvert.DeserializeObject<Dictionary<string, double>>(File.ReadAllText(FilePath))
                       ?? new Dictionary<string, double>();
        }
        catch { /* best-effort */ }
        return new Dictionary<string, double>();
    }

    public static void Save(Dictionary<string, double> sizes)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(sizes, Formatting.Indented));
        }
        catch { /* best-effort */ }
    }
}
