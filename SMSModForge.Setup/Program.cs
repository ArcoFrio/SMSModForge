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
            var texts = SetupTexts.FromFolder(Path.Combine(folder, "Languages"), culture);
            return app.Run(new UninstallWindow(texts, folder, windows));
        }

        if (!payload.HasEditor && File.Exists(Path.Combine(here, Manifest.FileName)))
        {
            // Out of the folder first, then the uninstall from the copy.
            string copy = Path.Combine(Path.GetTempPath(), "SMSModForge-uninstall-" + Guid.NewGuid().ToString("N")[..8] + ".exe");
            File.Copy(Environment.ProcessPath!, copy);
            Process.Start(new ProcessStartInfo(copy) { UseShellExecute = false, ArgumentList = { "--from", here } });
            return 0;
        }

        SetupTexts setupTexts;
        using (var editor = payload.Editor()) setupTexts = SetupTexts.FromZip(editor, culture);
        string? earlier = windows.InstalledFolder();
        string? game = GameFolder.Find(EditorGameFolder(), here);
        return app.Run(new SetupWindow(setupTexts, payload, windows, game, earlier));
    }

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
