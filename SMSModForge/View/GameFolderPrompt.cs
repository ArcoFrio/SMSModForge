using System.Windows;
using SMSModForge.Services;

namespace SMSModForge.View;

/// <summary>
/// Asking where Starmaker Story is installed, and refusing an answer that is
/// not one.
/// <para/>
/// Two places need this — the Options entry, and the update prompt when it
/// notices the folder has never been set — and they must agree about what
/// counts as a game folder. A path stored without checking would be a setting
/// that looks set and does nothing at the one moment it matters, which is
/// halfway through an update.
/// </summary>
public static class GameFolderPrompt
{
    /// <summary>
    /// Ask for the folder and store it. Returns true when something was stored.
    /// <para/>
    /// Says nothing on success: the caller knows what it wanted the folder for
    /// and is better placed to say so than a second dialog would be.
    /// </summary>
    public static bool Ask(Window owner)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Where Starmaker Story is installed — the folder with the game "
                        + "exe and BepInEx in it.",
            UseDescriptionForTitle = true,
            SelectedPath = EditorPrefs.GameFolder,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;

        string picked = dialog.SelectedPath;
        if (!UpdateInstaller.IsGameFolder(picked))
        {
            MessageBox.Show(owner,
                "There is no BepInEx\\plugins folder in:\n" + picked +
                "\n\nPick the folder the game exe is in, with BepInEx beside it. " +
                "If BepInEx is not installed yet, the README covers it.",
                "Starmaker Story folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        EditorPrefs.GameFolder = picked;
        return true;
    }
}
