using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SMSModForge.Services;

/// <summary>
/// Small persisted editor preferences that don't warrant a service each —
/// stored next to the other local settings (recent files, theme, preview
/// quality) in <c>%LocalAppData%\SMSModForge\prefs.json</c>.
/// <para/>
/// These are the AUTHOR's settings, not the pack's: nothing here ever reaches
/// a distributed <c>modpack.json</c>. Reads and writes are best-effort — a
/// missing or corrupt file falls back to defaults rather than blocking the
/// editor, same as <see cref="RecentFilesService"/>.
/// </summary>
public static class EditorPrefs
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "prefs.json");

    private static Dictionary<string, object>? _cache;

    private const string KeyConfirmOnSave = "confirmOnSave";
    private const string KeyAutoVersion = "autoVersion";
    private const string KeyWarnLowerVersion = "warnLowerVersion";
    private const string KeyQuietExportSize = "quietExportSizeFor";
    private const string KeySpellCheckNodeText = "spellCheckNodeText";

    /// <summary>
    /// Whether Save shows the change list for confirmation first. On by
    /// default: the whole point is that an author who doesn't know the feature
    /// exists still sees what a save is about to write.
    /// </summary>
    public static bool ConfirmOnSave
    {
        get => GetBool(KeyConfirmOnSave, defaultValue: true);
        set => SetBool(KeyConfirmOnSave, value);
    }

    /// <summary>
    /// Whether a save moves the pack's version on its own.
    /// <para/>
    /// On by default. A version nobody remembers to bump is worse than no
    /// version at all: it tells a player their copy is current when it is not.
    /// Off is a real preference for an author who numbers releases by hand.
    /// </summary>
    public static bool AutoVersion
    {
        get => GetBool(KeyAutoVersion, defaultValue: true);
        set => SetBool(KeyAutoVersion, value);
    }

    /// <summary>
    /// Whether saving a version lower than the pack already had asks first.
    /// <para/>
    /// On by default, because going backwards is nearly always a typo — and
    /// the one time it is not, a prompt costs a click.
    /// </summary>
    public static bool WarnOnLowerVersion
    {
        get => GetBool(KeyWarnLowerVersion, defaultValue: true);
        set => SetBool(KeyWarnLowerVersion, value);
    }

    /// <summary>
    /// Packs whose author has said they do not want the export size warning.
    /// <para/>
    /// Per pack rather than global: a big pack is big every time, and the
    /// warning is worth keeping for the OTHER packs an author opens. Stored as
    /// a list of pack ids.
    /// </summary>
    public static IReadOnlyCollection<string> PacksQuietOnExportSize
        => GetString(KeyQuietExportSize, "").Split(',',
               StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsQuietOnExportSize(string? packId)
    {
        if (string.IsNullOrEmpty(packId)) return false;
        foreach (string had in PacksQuietOnExportSize)
            if (string.Equals(had, packId, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static void SetQuietOnExportSize(string? packId, bool quiet)
    {
        if (string.IsNullOrEmpty(packId)) return;

        var have = new List<string>();
        foreach (string had in PacksQuietOnExportSize)
            if (!string.Equals(had, packId, StringComparison.OrdinalIgnoreCase)) have.Add(had);
        if (quiet) have.Add(packId!);

        // Commas separate them, so a pack id containing one would split into
        // two. Ids are file-system names, which cannot, but strip it anyway
        // rather than trust that.
        SetString(KeyQuietExportSize,
                  string.Join(",", have.Select(h => h.Replace(",", ""))));
    }

    /// <summary>
    /// Whether the dialogue node's Text box runs Windows' spell checker. On by
    /// default — node text is the one field in the editor a player actually
    /// reads. Off is a real preference, not just an escape hatch: a pack
    /// written in a language other than the checked one is all squiggle.
    /// </summary>
    public static bool SpellCheckNodeText
    {
        get => GetBool(KeySpellCheckNodeText, defaultValue: true);
        set => SetBool(KeySpellCheckNodeText, value);
    }

    private const string KeyCheckUpdates = "checkForUpdatesOnStart";
    private const string KeyAutoInstall = "installUpdatesWithoutAsking";
    private const string KeyGameFolder = "gameFolder";

    /// <summary>
    /// Whether the editor looks for a newer version when it starts. On by
    /// default: an author who never hears about a release goes on writing packs
    /// against an editor that has since been fixed, and the only channel before
    /// this was somebody happening to read a Discord post.
    /// <para/>
    /// Off means no request is made at all, not a request whose answer is
    /// ignored - this is read before the check, so turning it off is a real
    /// opt-out of the network for people who want one.
    /// </summary>
    public static bool CheckForUpdatesOnStart
    {
        get => GetBool(KeyCheckUpdates, defaultValue: true);
        set => SetBool(KeyCheckUpdates, value);
    }

    /// <summary>
    /// Whether a found update installs itself instead of asking. Off by
    /// default: replacing the files somebody is working in, and restarting the
    /// editor to do it, is not something to decide on their behalf until they
    /// have said so once.
    /// <para/>
    /// On, the editor says what it is doing in a toast rather than a window,
    /// because a dialog that appears and acts without being asked is the thing
    /// this setting exists to avoid.
    /// </summary>
    public static bool InstallUpdatesWithoutAsking
    {
        get => GetBool(KeyAutoInstall, defaultValue: false);
        set => SetBool(KeyAutoInstall, value);
    }

    /// <summary>
    /// Where Starmaker Story is installed - the folder holding the game exe,
    /// with BepInEx beside it.
    /// <para/>
    /// The editor does not otherwise need to know: it writes packs wherever it
    /// is told to export them. It is here for the update, which has to put the
    /// new plugin next to the game or leave the pair mismatched - the plugin and
    /// the editor are versioned together, and a pack written against one and run
    /// against the other can fail without saying so.
    /// <para/>
    /// Empty until somebody sets it, and an update simply skips the plugin then,
    /// saying so rather than guessing at a path.
    /// </summary>
    public static string GameFolder
    {
        get => GetString(KeyGameFolder, "");
        set => SetString(KeyGameFolder, value ?? "");
    }
    private const string KeyTutorialsDone = "tutorialsCompleted";

    /// <summary>
    /// Ids of the tutorials this author has finished, so the list can show
    /// which are behind them. An author's progress, not the pack's — someone
    /// opening a shared pack should not inherit whose tutorials were done.
    /// <para/>
    /// Exiting early deliberately does not count. Half a tutorial teaches half
    /// a thing, and a tick against it would only mislead the person deciding
    /// what to do next.
    /// </summary>
    public static IReadOnlyCollection<string> CompletedTutorials
        => GetString(KeyTutorialsDone, "").Split(',',
               StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Which revision of a tutorial this author finished, or 0 for one they
    /// have never finished.
    /// <para/>
    /// Entries are stored as <c>id@revision</c>. An entry with no revision is
    /// from before this was recorded and counts as revision 1, so upgrading
    /// does not un-tick everything somebody has already done.
    /// </summary>
    public static int CompletedRevision(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return 0;
        foreach (var entry in CompletedTutorials)
        {
            int at = entry.LastIndexOf('@');
            string entryId = at > 0 ? entry.Substring(0, at) : entry;
            if (!string.Equals(entryId, id, StringComparison.Ordinal)) continue;
            if (at <= 0) return 1;
            return int.TryParse(entry.Substring(at + 1), out int rev) && rev > 0 ? rev : 1;
        }
        return 0;
    }

    /// <summary>Whether this author has finished the tutorial at all, whichever
    /// version of it they saw.</summary>
    public static bool IsTutorialComplete(string id) => CompletedRevision(id) > 0;

    /// <summary>
    /// Whether they finished it, but an older version of it — the case worth
    /// telling them about, since what they learned may no longer be what the
    /// tutorial says.
    /// </summary>
    public static bool IsTutorialOutdated(string id, int currentRevision)
    {
        int done = CompletedRevision(id);
        return done > 0 && done < currentRevision;
    }

    public static void MarkTutorialComplete(string id, int revision = 1)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        if (CompletedRevision(id) >= revision) return;

        // One entry per tutorial: finishing a newer revision replaces the
        // record of the older one rather than sitting beside it.
        var all = new List<string>();
        foreach (var entry in CompletedTutorials)
        {
            int at = entry.LastIndexOf('@');
            string entryId = at > 0 ? entry.Substring(0, at) : entry;
            if (!string.Equals(entryId, id, StringComparison.Ordinal)) all.Add(entry);
        }
        all.Add(id + "@" + (revision > 0 ? revision : 1));
        SetString(KeyTutorialsDone, string.Join(",", all));
    }

    private static string GetString(string key, string fallback)
    {
        var d = Load();
        return d.TryGetValue(key, out var v) && v != null ? v.ToString() ?? fallback : fallback;
    }

    private static void SetString(string key, string value)
    {
        Load()[key] = value;
        Save();
    }

    private static Dictionary<string, object> Load()
    {
        if (_cache != null) return _cache;
        try
        {
            _cache = File.Exists(FilePath)
                ? JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(FilePath))
                  ?? new Dictionary<string, object>()
                : new Dictionary<string, object>();
        }
        catch { _cache = new Dictionary<string, object>(); }
        return _cache;
    }

    private static bool GetBool(string key, bool defaultValue)
    {
        var prefs = Load();
        if (!prefs.TryGetValue(key, out var raw) || raw == null) return defaultValue;
        try { return Convert.ToBoolean(raw); }
        catch { return defaultValue; }
    }

    private static void SetBool(string key, bool value)
    {
        Load()[key] = value;
        Save();
    }

    /// <summary>Writes the whole file. Best-effort by design: a preference that
    /// cannot be saved is not worth interrupting an author over.</summary>
    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(Load(), Formatting.Indented));
        }
        catch { /* best-effort */ }
    }
}
