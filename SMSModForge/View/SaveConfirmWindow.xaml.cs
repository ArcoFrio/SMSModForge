using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using SMSModForge.Model;

namespace SMSModForge.View;

/// <summary>
/// Shows everything a save is about to write, grouped by manifest section, and
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

    private void Populate(IReadOnlyList<PackChange> changes, string? packRoot)
    {
        HeadlineText.Text = changes.Count == 1
            ? "1 change will be written"
            : changes.Count + " changes will be written";
        TargetText.Text = string.IsNullOrEmpty(packRoot)
            ? "The pack folder is chosen on save."
            : System.IO.Path.Combine(packRoot, PackRepository.ManifestFileName);

        // Group by manifest section so a long list stays navigable — same
        // grouping idea the main window's unit lists use.
        var view = new CollectionViewSource { Source = changes };
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PackChange.Section)));
        ChangeList.ItemsSource = view.View;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
