using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SMSModForge.Services;

/// <summary>
/// Which of a UI's objects are hidden in the editor's preview, remembered per
/// pack folder (the author, 2026-11-06: so an object does not have to be moved
/// out of the way to work on what is behind it, with an eye as Unity has).
/// <para/>
/// The editor's to remember, never the pack's: hiding is how one person is
/// looking at a screen, it must not reach a player, and a pack that changed when
/// somebody hid something would show a change nobody made. Kept in
/// <c>ui-hidden.json</c> beside the editor's other settings; best-effort, like
/// them. Held by path rather than on the rows, because an undo rebuilds every
/// row from the pack, and what the author hid has to outlast that.
/// </summary>
public static class UiPreviewHiding
{
    private static string FilePath => AppDataFolder.PathOf("ui-hidden.json");

    /// <summary>pack folder → UI → the objects hidden, by path.</summary>
    private static Dictionary<string, Dictionary<string, HashSet<string>>>? _all;

    private static string _pack = "";

    /// <summary>The pack being looked at; empty for one not saved yet, whose
    /// hidden objects last the session.</summary>
    public static void Open(string? packRoot)
        => _pack = string.IsNullOrWhiteSpace(packRoot) ? "" : Path.GetFullPath(packRoot!).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    /// <summary>The paths hidden on one UI. Nothing for one with none.</summary>
    public static IReadOnlyCollection<string> Of(string? uiKey)
        => Hidden().TryGetValue(uiKey ?? "", out var paths) ? paths : Array.Empty<string>();

    /// <summary>What is hidden on one UI, all at once: a rename moves several
    /// paths, and showing everything clears them.</summary>
    public static void Set(string? uiKey, IEnumerable<string> paths)
    {
        if (string.IsNullOrEmpty(uiKey)) return;
        var hidden = Hidden();
        var now = new HashSet<string>(paths);
        if (hidden.TryGetValue(uiKey, out var was) ? was.SetEquals(now) : now.Count == 0) return;
        if (now.Count == 0) hidden.Remove(uiKey);
        else hidden[uiKey] = now;
        Save();
    }

    private static Dictionary<string, HashSet<string>> Hidden()
    {
        _all ??= Load();
        if (!_all.TryGetValue(_pack, out var hidden)) _all[_pack] = hidden = new Dictionary<string, HashSet<string>>();
        return hidden;
    }

    private static Dictionary<string, Dictionary<string, HashSet<string>>> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, HashSet<string>>>>(File.ReadAllText(FilePath))
                       ?? new();
        }
        catch { /* corrupt or unreadable: nothing hidden */ }
        return new();
    }

    private static void Save()
    {
        if (_all == null) return;
        try
        {
            // A pack never saved has no folder to file its objects under.
            var kept = _all.Where(p => p.Key.Length > 0 && p.Value.Count > 0).ToDictionary(p => p.Key, p => p.Value);
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
