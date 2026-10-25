using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SMSModForge.Services;

/// <summary>
/// Which of a dialogue's panels are folded - its Identity, its Start
/// conditions - remembered for that dialogue (the author, 1.8.0: "folding
/// should be remembered by dialogue instead of applying to all"). Set once,
/// those panels rarely change again, and folded the node list gets their room.
/// <para/>
/// The editor's to remember, never the pack's: a fold is how one person likes
/// to look at a dialogue, and a pack that changed when somebody folded a panel
/// would show a change nobody made. Kept in <c>folds.json</c> beside the
/// editor's other settings, per pack folder; best-effort, like them.
/// </summary>
public static class DialogueFolds
{
    public const string Identity = "identity", Start = "start";

    private static string FilePath => AppDataFolder.PathOf("folds.json");

    /// <summary>pack folder → dialogue key → the panels folded.</summary>
    private static Dictionary<string, Dictionary<string, HashSet<string>>>? _all;

    private static string _pack = "";

    /// <summary>The pack whose dialogues are being looked at; empty for one
    /// not saved yet, whose folds last the session.</summary>
    public static void Open(string? packRoot)
        => _pack = string.IsNullOrWhiteSpace(packRoot) ? "" : Path.GetFullPath(packRoot!).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    public static bool IsFolded(string dialogueKey, string panel)
        => Folds().TryGetValue(dialogueKey ?? "", out var panels) && panels.Contains(panel);

    public static void Set(string dialogueKey, string panel, bool folded)
    {
        if (string.IsNullOrEmpty(dialogueKey)) return;
        var folds = Folds();
        if (!folds.TryGetValue(dialogueKey, out var panels)) folds[dialogueKey] = panels = new HashSet<string>();
        if (folded ? !panels.Add(panel) : !panels.Remove(panel)) return;
        if (panels.Count == 0) folds.Remove(dialogueKey);
        Save();
    }

    /// <summary>A dialogue renamed keeps its folds.</summary>
    public static void Rename(string from, string to)
    {
        var folds = Folds();
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || !folds.Remove(from, out var panels)) return;
        folds[to] = panels;
        Save();
    }

    private static Dictionary<string, HashSet<string>> Folds()
    {
        _all ??= Load();
        if (!_all.TryGetValue(_pack, out var folds)) _all[_pack] = folds = new Dictionary<string, HashSet<string>>();
        return folds;
    }

    private static Dictionary<string, Dictionary<string, HashSet<string>>> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, HashSet<string>>>>(File.ReadAllText(FilePath))
                       ?? new();
        }
        catch { /* corrupt or unreadable: nothing folded */ }
        return new();
    }

    private static void Save()
    {
        if (_all == null) return;
        try
        {
            // A pack never saved has no folder to file its folds under.
            var kept = new Dictionary<string, Dictionary<string, HashSet<string>>>(_all);
            kept.Remove("");
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(kept, Formatting.Indented));
        }
        catch { /* best-effort */ }
    }

    /// <summary>Forget what was read, so the next look reads the file again.
    /// For tests.</summary>
    internal static void Reload() => _all = null;
}
