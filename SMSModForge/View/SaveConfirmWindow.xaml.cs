using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using SMSModForge.Model;
using SMSModForge.Localization;

namespace SMSModForge.View;

/// <summary>
/// Shows everything a save is about to write, under the thing each change
/// belongs to and in the editor's words (<see cref="ChangeWords"/>), and
/// asks for a yes. Purely a confirmation surface — it never mutates the pack;
/// the caller decides what to do with the answer.
/// </summary>
public partial class SaveConfirmWindow : Window
{
    private SaveConfirmWindow()
    {
        InitializeComponent();
    }

    /// <summary>True when the user ticked "Don't ask again" before saving. Only
    /// meaningful once <see cref="Ask"/> has returned true.</summary>
    public bool SuppressFuturePrompts => DontAskAgain.IsChecked == true;

    /// <summary>
    /// Present <paramref name="changes"/> and return whether to go ahead with
    /// the save. <paramref name="suppressFuturePrompts"/> reports the checkbox.
    /// </summary>
    public static bool Ask(Window? owner, IReadOnlyList<PackChange> changes, string? packRoot,
                           out bool suppressFuturePrompts)
    {
        var window = new SaveConfirmWindow { Owner = owner };
        window.Populate(changes, packRoot);
        bool ok = window.ShowDialog() == true;
        suppressFuturePrompts = ok && window.SuppressFuturePrompts;
        return ok;
    }

    /// <summary>The window filled in and not shown: for the pictures of it
    /// (<c>SaveConfirmSnapshotTests</c>) and the tests of what it lists.</summary>
    internal static SaveConfirmWindow Build(IReadOnlyList<PackChange> changes, string? packRoot)
    {
        var window = new SaveConfirmWindow();
        window.Populate(changes, packRoot);
        return window;
    }

    private void Populate(IReadOnlyList<PackChange> changes, string? packRoot)
    {
        HeadlineText.Text = Loc.P("saveConfirm.headline", changes.Count);
        TargetText.Text = string.IsNullOrEmpty(packRoot)
            ? Loc.T("saveConfirm.folderChosenOnSave")
            : System.IO.Path.Combine(packRoot, PackRepository.ManifestFileName);

        // Under the thing each belongs to - a dialogue, a scene, a
        // translation - in the order the file has them.
        var view = new ListCollectionView(changes as System.Collections.IList ?? new List<PackChange>(changes));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PackChange.Group)));
        ChangeList.ItemsSource = view;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
