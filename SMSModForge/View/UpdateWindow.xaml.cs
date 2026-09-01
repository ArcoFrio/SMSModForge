using System.Windows;
using SMSModForge.Services;

namespace SMSModForge.View;

/// <summary>
/// "There is a new version — here is what changed, do you want it?"
/// </summary>
public partial class UpdateWindow : Window
{
    private ReleaseInfo _release = null!;

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

        win.NotesView.Document = MarkdownFlow.ToDocument(
            string.IsNullOrWhiteSpace(release.Notes)
                ? "This release was published without notes."
                : release.Notes);

        win._release = release;
        win.ShowPluginLine();

        return win.ShowDialog() == true;
    }

    private static string SizeSuffix(long bytes)
        => bytes > 0 ? $" The download is about {bytes / 1024 / 1024} MB." : "";

    /// <summary>
    /// What the update will do about the plugin, and — when it cannot do
    /// anything — the button that changes that.
    /// <para/>
    /// Said before the decision rather than after it: the plugin and the editor
    /// are versioned together, and a pack written against one and run against
    /// the other can fail without saying why. Somebody about to update is
    /// exactly the person who should be asked where their game is.
    /// </summary>
    /// <summary>
    /// The line, and whether to offer the folder alongside it. Separate from
    /// the window and from the stored setting so it can be checked in all
    /// three of its states without a window and without writing over somebody’s
    /// real preferences to get at the third one.
    /// </summary>
    public static (string Text, bool OfferFolder) PluginLine(ReleaseInfo release, string? gameFolder)
    {
        if (release.PluginZipUrl == null)
            return ("This release has no plugin download, so only the editor changes.", false);

        if (!UpdateInstaller.IsGameFolder(gameFolder))
            return ("This release updates the plugin too, but the editor does not know "
                  + "where your game is. Set it and both halves stay in step — otherwise "
                  + "only the editor changes.", true);

        return ("The plugin in your game folder will be updated to match. Your packs "
              + "are not touched.", false);
    }

    private void ShowPluginLine()
    {
        var (text, offer) = PluginLine(_release, EditorPrefs.GameFolder);
        PluginText.Text = text;
        SetFolderButton.Visibility = offer ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetFolder_Click(object sender, RoutedEventArgs e)
    {
        // The line and the button both answer to the stored folder, so
        // re-reading it is the whole of the update.
        if (GameFolderPrompt.Ask(this)) ShowPluginLine();
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
