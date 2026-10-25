using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SMSModForge.Setup;

/// <summary>
/// The Starmaker Story folder: whether a folder is it, and where it probably
/// is. The game folder is required - the plugin goes there - and checked to
/// really be the game (the author, 1.8.0), so the player finds out at the
/// first step, not when ModForge is missing from the menu.
/// </summary>
public static class GameFolder
{
    public const string Exe = "Starmaker Story.exe";
    public const string Data = "Starmaker Story_Data";

    public enum Verdict { Empty, NoFolder, NotTheGame, Game }

    /// <summary>The game is its exe and its data folder side by side.</summary>
    public static Verdict Check(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return Verdict.Empty;
        string path;
        try { path = Path.GetFullPath(folder.Trim()); }
        catch (Exception) { return Verdict.NoFolder; }
        if (!Directory.Exists(path)) return Verdict.NoFolder;
        return File.Exists(Path.Combine(path, Exe)) && Directory.Exists(Path.Combine(path, Data))
            ? Verdict.Game : Verdict.NotTheGame;
    }

    /// <summary>
    /// The first of the likely places that is the game, or null: the folder
    /// the editor was last told (its settings), Steam's libraries, and the
    /// folder this setup was run from and the one above it.
    /// </summary>
    public static string? Find(string? editorSetting, string? runFrom)
    {
        foreach (var candidate in Candidates(editorSetting, runFrom))
            if (Check(candidate) == Verdict.Game) return Path.GetFullPath(candidate);
        return null;
    }

    public static IEnumerable<string> Candidates(string? editorSetting, string? runFrom)
    {
        if (!string.IsNullOrWhiteSpace(editorSetting)) yield return editorSetting;
        foreach (var library in SteamLibraries())
            yield return Path.Combine(library, "steamapps", "common", "Starmaker Story");
        if (!string.IsNullOrWhiteSpace(runFrom))
        {
            yield return runFrom;
            var up = Path.GetDirectoryName(runFrom.TrimEnd(Path.DirectorySeparatorChar));
            if (up != null) yield return up;
        }
    }

    /// <summary>Every Steam library on this computer, as Steam itself lists
    /// them (libraryfolders.vdf), its own folder first.</summary>
    public static IEnumerable<string> SteamLibraries()
    {
        string? steam = null;
        try { steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string; }
        catch (Exception) { /* no Steam, or no right to look: no libraries */ }
        if (string.IsNullOrWhiteSpace(steam)) yield break;
        steam = steam.Replace('/', Path.DirectorySeparatorChar);
        yield return steam;

        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        string text = "";
        try { if (File.Exists(vdf)) text = File.ReadAllText(vdf); }
        catch (IOException) { }
        foreach (var library in LibrariesIn(text))
            if (!string.Equals(library.TrimEnd('\\'), steam.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                yield return library;
    }

    /// <summary>The "path" values of a libraryfolders.vdf, unescaped.</summary>
    public static IEnumerable<string> LibrariesIn(string vdf)
        => Regex.Matches(vdf ?? "", "\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"")
                .Select(m => m.Groups[1].Value.Replace("\\\\", "\\"));
}

/// <summary>Where things go on this computer.</summary>
public static class Places
{
    /// <summary>
    /// Where ModForge goes unless the player says otherwise: the folder Windows
    /// itself puts a program installed for one person in
    /// (%LocalAppData%\Programs), so no administrator is asked for and the
    /// editor can update itself where it stands.
    /// </summary>
    public static string DefaultInstallFolder
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SMSModForge");

    /// <summary>The editor's own settings folder - the same one the editor
    /// uses, where its settings say which game folder to update.</summary>
    public static string EditorSettings
    {
        get => _editorSettings ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMSModForge");
        set => _editorSettings = value;
    }
    private static string? _editorSettings;

    /// <summary>Whether <paramref name="folder"/> is <paramref name="outer"/>
    /// or anywhere inside it. By whole folder names: "Starmaker Story 1.8E -
    /// copy" is not inside "Starmaker Story 1.8E".</summary>
    public static bool IsInside(string? folder, string? outer)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(outer)) return false;
        try
        {
            string a = Path.GetFullPath(folder.Trim()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string b = Path.GetFullPath(outer.Trim()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }
}
