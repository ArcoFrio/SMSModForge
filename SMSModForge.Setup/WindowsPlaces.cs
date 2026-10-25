using System;
using System.IO;
using Microsoft.Win32;

namespace SMSModForge.Setup;

/// <summary>
/// Windows' own places for an installed program, for one person: its entry in
/// Settings > Apps (HKCU's Uninstall key - no administrator needed), and
/// shortcuts in that person's Start menu and on their desktop.
/// </summary>
public sealed class WindowsPlaces : IWindowsPlaces
{
    private const string AppsKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SMSModForge";

    public string StartMenuFolder => Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    public string DesktopFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public string? InstalledFolder()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppsKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception) { return null; }
    }

    public void Register(string installFolder, string uninstaller, string icon, string version, long sizeKb)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppsKey);
        key.SetValue("DisplayName", "Starmaker - ModForge");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "ArcoFrio");
        key.SetValue("DisplayIcon", icon);
        key.SetValue("InstallLocation", installFolder);
        key.SetValue("UninstallString", "\"" + uninstaller + "\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeKb), RegistryValueKind.DWord);
    }

    public void Unregister()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(AppsKey, throwOnMissingSubKey: false); }
        catch (Exception) { }
    }

    /// <summary>A .lnk, made by Windows' own Shell object.</summary>
    public void Shortcut(string path, string target, string workingFolder)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("no WScript.Shell");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = target;
            link.WorkingDirectory = workingFolder;
            link.IconLocation = target + ",0";
            link.Description = "Starmaker - ModForge";
            link.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
}
