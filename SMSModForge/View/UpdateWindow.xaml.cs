using System.Windows;
using SMSModForge.Services;

namespace SMSModForge.View;

/// <summary>
/// "There is a new version — here is what changed, do you want it?"
/// </summary>
public partial class UpdateWindow : Window
{
    private UpdateWindow() => InitializeComponent();

    /// <summary>
    /// Offer <paramref name="release"/>. Returns true when the author asked for
    /// it to be installed.
    /// </summary>
    public static bool Ask(Window owner, ReleaseInfo release, string runningVersion)
    {
        var win = new UpdateWindow { Owner = owner };

        win.HeadlineText.Text = release.Name;
        win.VersionText.Text = $"Version {release.VersionText} — you are running {runningVersion}."
                             + SizeSuffix(release.EditorZipBytes);

        win.NotesText.Text = string.IsNullOrWhiteSpace(release.Notes)
            ? "This release was published without notes."
            : release.Notes;

        win.PluginText.Text = PluginLine(release);

        return win.ShowDialog() == true;
    }

    private static string SizeSuffix(long bytes)
        => bytes > 0 ? $" The download is about {bytes / 1024 / 1024} MB." : "";

    /// <summary>
    /// What the update will do about the plugin, which is worth saying before
    /// the decision rather than after it: the plugin and the editor are
    /// versioned together, and a pack written against one and run against the
    /// other can fail without saying why.
    /// </summary>
    private static string PluginLine(ReleaseInfo release)
    {
        if (release.PluginZipUrl == null)
            return "This release has no plugin download, so only the editor changes.";

        string folder = EditorPrefs.GameFolder;
        if (!UpdateInstaller.IsGameFolder(folder))
            return "The plugin in your game folder will NOT be updated — set "
                 + "Options ▸ Starmaker Story folder first, and the next update will "
                 + "keep the two in step.";

        return "The plugin in your game folder will be updated to match. Your packs "
             + "are not touched.";
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
