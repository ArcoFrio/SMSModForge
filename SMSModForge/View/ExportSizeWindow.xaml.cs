using System.Windows;

namespace SMSModForge.View;

/// <summary>
/// Asks before exporting a pack folder that is larger than expected.
/// <para/>
/// A message box would do the asking, but not the remembering: a pack with
/// video scenes in it is legitimately hundreds of megabytes, and being asked
/// about it on every export is a prompt an author learns to dismiss without
/// reading — which is exactly when it stops protecting them from the export
/// that really has swept up something it should not have.
/// </summary>
public partial class ExportSizeWindow : Window
{
    /// <summary>What the author decided.</summary>
    public readonly record struct Answer(bool Export, bool Quiet);

    private ExportSizeWindow() => InitializeComponent();

    /// <summary>
    /// Show the warning and wait. Returns whether to export, and whether this
    /// pack should stop being asked.
    /// <para/>
    /// Cancelling never sets the opt-out: somebody who backed out to go and
    /// look at the folder has not agreed to anything.
    /// </summary>
    public static Answer Ask(Window? owner, int files, double megabytes, string folder)
    {
        var window = new ExportSizeWindow { Owner = owner };
        WindowOwnership.ReturnFocusToOwner(window);

        window.HeadlineText.Text =
            $"This pack folder holds at least {files:N0} files ({megabytes:N0} MB).";
        window.FolderText.Text = folder;

        bool export = window.ShowDialog() == true;
        return new Answer(export, export && window.DontAskAgain.IsChecked == true);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
