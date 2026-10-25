using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SMSModForge.Setup;

/// <summary>What the setup installs: the editor's zip and the plugin's - the
/// same two a release publishes - and the uninstaller it leaves behind.</summary>
public sealed class Payload
{
    public Func<Stream?> Editor { get; init; } = () => null;
    public Func<Stream?> Plugin { get; init; } = () => null;
    public Func<Stream?> Uninstaller { get; init; } = () => null;

    public bool HasEditor { get { using var s = Editor(); return s != null; } }

    /// <summary>Built into the setup at release; while developing, the two
    /// zips beside it.</summary>
    public static Payload Here()
    {
        var self = Assembly.GetExecutingAssembly();
        string beside = AppContext.BaseDirectory;
        Stream? Embedded(string name) => self.GetManifestResourceStream(name);
        Stream? Zip(string kind)
        {
            var found = Directory.EnumerateFiles(beside, $"Starmaker - ModForge {kind}*.zip").OrderByDescending(f => f).FirstOrDefault();
            return found == null ? null : File.OpenRead(found);
        }
        return new Payload
        {
            Editor = () => Embedded("payload.editor.zip") ?? Zip("Editor"),
            Plugin = () => Embedded("payload.plugin.zip") ?? Zip("Plugin"),
            Uninstaller = () => Embedded("payload.uninstall.exe"),
        };
    }
}

/// <summary>
/// What an install put where, kept beside the editor (install.json) so the
/// uninstaller takes away that and nothing else - not packs, not the
/// editor's settings, not a BepInEx that was there before.
/// </summary>
public sealed class Manifest
{
    public const string FileName = "install.json";

    public string Version { get; set; } = "";
    public string GameFolder { get; set; } = "";
    /// <summary>The folder held nothing before the first install: it is
    /// ModForge's, and goes whole.</summary>
    public bool FolderWasEmpty { get; set; }
    /// <summary>Files under the install folder, relative to it.</summary>
    public List<string> Files { get; set; } = new();
    /// <summary>ModForge's own files in the game, relative to its folder.</summary>
    public List<string> PluginFiles { get; set; } = new();
    public List<string> Shortcuts { get; set; } = new();

    /// <summary>Whether this install has the editor - not when only the plugin
    /// was installed.</summary>
    public bool HasEditor => Files.Any(f => string.Equals(f, Installer.EditorExe, StringComparison.OrdinalIgnoreCase));

    public static Manifest? Read(string installFolder)
    {
        try
        {
            string path = Path.Combine(installFolder, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) : null;
        }
        catch (Exception) { return null; }
    }

    public void Write(string installFolder)
        => File.WriteAllText(Path.Combine(installFolder, FileName), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>The parts of Windows an install touches beyond its own folder:
/// its entry in the Apps list, and shortcuts. Behind an interface so a test
/// never writes the registry or a Start menu.</summary>
public interface IWindowsPlaces
{
    /// <summary>Where an earlier install put ModForge, from its Apps entry.</summary>
    string? InstalledFolder();
    void Register(string installFolder, string uninstaller, string icon, string version, long sizeKb);
    void Unregister();
    string StartMenuFolder { get; }
    string DesktopFolder { get; }
    void Shortcut(string path, string target, string workingFolder);
}

public static class Installer
{
    public const string EditorExe = "SMSModForge.exe";
    public const string UninstallerExe = "Uninstall ModForge.exe";
    public const string ShortcutName = "ModForge.lnk";

    /// <param name="Editor">Everything (true), to make mods - or only the
    /// plugin, to play them: then no editor, no shortcuts, and the install
    /// folder holds just the uninstaller and what it needs.</param>
    public sealed record Options(string GameFolder, string InstallFolder, bool StartMenu, bool Desktop, bool Editor = true);

    public sealed record Result(bool BepInExWasThere, int EditorFiles, int PluginFiles);

    /// <summary>
    /// Put ModForge in: the editor in its folder, the plugin in the game, the
    /// game folder in the editor's settings, an entry in Windows' Apps list
    /// and the shortcuts asked for - or, for only the plugin, the plugin and
    /// the Apps entry that takes it out again. Throws with a sentence for a
    /// person when something cannot be done; what was written is recorded as
    /// it goes.
    /// </summary>
    public static Result Run(Options o, Payload payload, IWindowsPlaces windows, Action<string>? progress = null)
    {
        if (GameFolder.Check(o.GameFolder) != GameFolder.Verdict.Game)
            throw new InvalidOperationException("not the game: " + o.GameFolder);
        string install = Path.GetFullPath(o.InstallFolder);
        string game = Path.GetFullPath(o.GameFolder);

        var earlier = Manifest.Read(install);
        bool wasEmpty = earlier?.FolderWasEmpty
                        ?? (!Directory.Exists(install) || !Directory.EnumerateFileSystemEntries(install).Any());
        Directory.CreateDirectory(install);
        var manifest = new Manifest
        {
            Version = SMSModForge.Shared.ForgeVersion.Current,
            GameFolder = game,
            FolderWasEmpty = wasEmpty,
        };

        if (o.Editor)
        {
            progress?.Invoke("editor");
            using (var editor = payload.Editor() ?? throw new FileNotFoundException("the editor is missing from this setup"))
                manifest.Files.AddRange(Extract(editor, install, _ => true));
        }
        else if (earlier != null)
        {
            // Only the plugin, over an install that had the editor: the editor
            // stays where it is, and on the uninstaller's list.
            manifest.Files.AddRange(earlier.Files);
            manifest.Shortcuts.AddRange(earlier.Shortcuts);
        }

        progress?.Invoke("plugin");
        bool hadBepInEx = File.Exists(Path.Combine(game, "BepInEx", "core", "BepInEx.dll"));
        using (var plugin = payload.Plugin() ?? throw new FileNotFoundException("the plugin is missing from this setup"))
        {
            var written = Extract(plugin, game, entry => TakesFromPlugin(entry, hadBepInEx, game));
            manifest.PluginFiles.AddRange(written.Where(IsModForgesOwn));
        }
        Directory.CreateDirectory(Path.Combine(game, "Mods"));

        using (var uninstaller = payload.Uninstaller())
        {
            if (uninstaller != null)
            {
                using var file = File.Create(Path.Combine(install, UninstallerExe));
                uninstaller.CopyTo(file);
                manifest.Files.Add(UninstallerExe);
            }
        }

        progress?.Invoke("windows");
        string exe = Path.Combine(install, EditorExe);
        if (o.Editor)
        {
            TellTheEditor(game);
            if (o.StartMenu) manifest.Shortcuts.Add(Shortcut(windows, windows.StartMenuFolder, exe, install));
            if (o.Desktop) manifest.Shortcuts.Add(Shortcut(windows, windows.DesktopFolder, exe, install));
        }
        manifest.Files.Add(Manifest.FileName);
        manifest.Files = manifest.Files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        manifest.Shortcuts = manifest.Shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        manifest.Write(install);
        long size = manifest.Files.Sum(f => SafeLength(Path.Combine(install, f))) / 1024;
        // The Apps list shows the editor's icon, or the uninstaller's when
        // there is no editor.
        string icon = manifest.HasEditor ? exe : Path.Combine(install, UninstallerExe);
        windows.Register(install, Path.Combine(install, UninstallerExe), icon, manifest.Version, size);
        return new Result(hadBepInEx, manifest.HasEditor ? manifest.Files.Count : 0, manifest.PluginFiles.Count);
    }

    /// <summary>
    /// What of the plugin zip goes into the game. All of it into a game
    /// without BepInEx - the zip is a whole BepInEx install for that reason.
    /// Into one that has it, only what is under BepInEx/plugins: the loader
    /// there may be newer, and other mods rely on it, as the editor's own
    /// updates already treat it; and a Newtonsoft.Json already there stays.
    /// </summary>
    public static bool TakesFromPlugin(string entry, bool hadBepInEx, string game)
    {
        string p = entry.Replace('\\', '/').TrimStart('/');
        if (!hadBepInEx) return true;
        if (!p.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase)) return false;
        if (p.Equals("BepInEx/plugins/Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase))
            return !File.Exists(Path.Combine(game, "BepInEx", "plugins", "Newtonsoft.Json.dll"));
        return true;
    }

    /// <summary>ModForge's own files in the game - what an uninstall may take
    /// out again. Never BepInEx, never Newtonsoft (another mod may use it),
    /// never anything under Mods.</summary>
    public static bool IsModForgesOwn(string relative)
    {
        string p = relative.Replace('\\', '/');
        return p.StartsWith("BepInEx/plugins/SMSModForge", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith("BepInEx/plugins/VanillaFrames/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Unpack the zip's files that <paramref name="take"/> wants into
    /// <paramref name="folder"/>; their paths relative to it.</summary>
    private static List<string> Extract(Stream zipStream, string folder, Func<string, bool> take)
    {
        var written = new List<string>();
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (!take(entry.FullName)) continue;
            string target = Path.GetFullPath(Path.Combine(folder, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            // A zip can name its way out of the folder it is unpacked into.
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.FullName.EndsWith("/"))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            written.Add(target.Substring(root.Length));
        }
        return written;
    }

    /// <summary>
    /// The game folder, in the editor's own settings, so its updates know
    /// where the plugin is from the first start - merged into what is there.
    /// </summary>
    public static void TellTheEditor(string game)
    {
        string path = Path.Combine(Places.EditorSettings, "prefs.json");
        JsonObject prefs;
        try { prefs = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject() : new JsonObject(); }
        catch (Exception) { prefs = new JsonObject(); }
        prefs["gameFolder"] = game;
        Directory.CreateDirectory(Places.EditorSettings);
        File.WriteAllText(path, prefs.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Shortcut(IWindowsPlaces windows, string folder, string exe, string install)
    {
        string path = Path.Combine(folder, ShortcutName);
        windows.Shortcut(path, exe, install);
        return path;
    }

    private static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; }
        catch (Exception) { return 0; }
    }
}

public static class Uninstaller
{
    /// <summary>
    /// Take ModForge off: what the install put in its folder (the whole folder,
    /// when it was ModForge's alone), its shortcuts and Apps entry, and - when
    /// asked - its own files in the game. Packs, the Mods folder, BepInEx and
    /// the editor's settings stay.
    /// </summary>
    public static void Run(string installFolder, bool plugin, IWindowsPlaces windows)
    {
        string install = Path.GetFullPath(installFolder);
        var manifest = Manifest.Read(install) ?? throw new FileNotFoundException("no install found in " + install);
        // An install of only the plugin is the plugin: taking it off takes it out.
        plugin |= !manifest.HasEditor;

        if (plugin && GameFolder.Check(manifest.GameFolder) == GameFolder.Verdict.Game)
        {
            foreach (var file in manifest.PluginFiles.Where(Installer.IsModForgesOwn))
                TryDelete(Path.Combine(manifest.GameFolder, file));
            RemoveEmpty(Path.Combine(manifest.GameFolder, "BepInEx", "plugins", "SMSModForge"));
            RemoveEmpty(Path.Combine(manifest.GameFolder, "BepInEx", "plugins", "VanillaFrames"));
        }

        foreach (var shortcut in manifest.Shortcuts) TryDelete(shortcut);
        windows.Unregister();

        if (manifest.FolderWasEmpty)
        {
            try { Directory.Delete(install, recursive: true); }
            catch (Exception) { /* something holds a file; the rest went */ }
        }
        else
        {
            foreach (var file in manifest.Files) TryDelete(Path.Combine(install, file));
            RemoveEmpty(install);
        }
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception) { }
    }

    /// <summary>Folders left empty under <paramref name="folder"/>, deepest
    /// first, and it too if it ends empty.</summary>
    private static void RemoveEmpty(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var sub in Directory.GetDirectories(folder)) RemoveEmpty(sub);
        try { if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }
        catch (Exception) { }
    }
}
