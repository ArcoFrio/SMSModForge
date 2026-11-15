using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;

namespace SMSModForge.Setup;

/// <summary>
/// One program, two jobs: with what it installs inside it, the installer; as
/// "Uninstall ModForge.exe" beside an install, the uninstaller. The
/// uninstaller first copies itself out of the folder it is about to remove and
/// runs from there, since a running program's own file cannot be deleted.
/// </summary>
public static class Program
{
    /// <summary>Set by the tests: no dialogs, no programs started.</summary>
    public static bool Testing { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var windows = new WindowsPlaces();
        var culture = CultureInfo.CurrentUICulture;
        string here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var payload = Payload.Here();

        int from = Array.IndexOf(args, "--from");
        if (from >= 0 && from + 1 < args.Length)
        {
            string folder = args[from + 1];
            // Its own words first (1.9.0): a plugin-only install has no editor, and so no
            // Languages folder to read them from.
            var texts = SetupTexts.FromSelf(culture) ?? SetupTexts.FromFolder(Path.Combine(folder, "Languages"), culture);
            return app.Run(new UninstallWindow(texts, folder, windows));
        }

        // The uninstaller is this program under its own name, beside an install. Since
        // 1.9.0 the setup carries no editor either, so the name is what tells them apart:
        // a setup saved into an install's folder stays a setup.
        if (StartsAsUninstaller(Environment.ProcessPath, here, payload.HasEditor))
        {
            // Out of the folder first, then the uninstall from the copy.
            string copy = Path.Combine(Path.GetTempPath(), "SMSModForge-uninstall-" + Guid.NewGuid().ToString("N")[..8] + ".exe");
            File.Copy(Environment.ProcessPath!, copy);
            Process.Start(new ProcessStartInfo(copy) { UseShellExecute = false, ArgumentList = { "--from", here } });
            return 0;
        }

        // Its own words, carried inside it (1.9.0); a build without them reads the
        // editor zip's, as before.
        SetupTexts? setupTexts = SetupTexts.FromSelf(culture);
        if (setupTexts == null)
            using (var editor = payload.Editor()) setupTexts = SetupTexts.FromZip(editor, culture);
        string? earlier = windows.InstalledFolder();
        string? game = GameFolder.Find(EditorGameFolder(), here);
        // Whatever it does not carry, it downloads from the release.
        return app.Run(new SetupWindow(setupTexts, payload, windows, game, earlier, new ReleaseSource()));
    }

    /// <summary>Whether this program, started from <paramref name="processPath"/> in the
    /// folder <paramref name="here"/>, is an install's uninstaller rather than a setup:
    /// under the uninstaller's name, carrying no editor, beside an install's record.</summary>
    public static bool StartsAsUninstaller(string? processPath, string here, bool hasEditor)
        => string.Equals(Path.GetFileName(processPath), Installer.UninstallerExe, StringComparison.OrdinalIgnoreCase)
           && !hasEditor && File.Exists(Path.Combine(here, Manifest.FileName));

    /// <summary>The game folder the editor was last told, from its settings.</summary>
    private static string? EditorGameFolder()
    {
        try
        {
            string path = Path.Combine(Places.EditorSettings, "prefs.json");
            if (!File.Exists(path)) return null;
            var prefs = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
            return prefs?["gameFolder"]?.GetValue<string>();
        }
        catch (Exception) { return null; }
    }
}
