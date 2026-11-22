using System;
using System.IO;
using System.Windows;
using SMSModForge.Localization;
using SMSModForge.Model;

namespace SMSModForge.View;

/// <summary>
/// File ▸ New and the start page's New (1.10.0): the pack's name and the folder it goes in, asked together, with
/// where it will be made said before anything is made.
/// </summary>
public partial class NewPackWindow : Window
{
    public NewPackWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { NameBox.Focus(); Changed(this, null); };
    }

    /// <summary>The name and the folder chosen, or null when cancelled. Nothing under the test harness: answered
    /// as a cancel (CLAUDE.md).</summary>
    public static (string Folder, string Name)? Ask(Window owner, string? startIn)
    {
        if (Services.TestMode.Active) return null;
        var w = new NewPackWindow { Owner = owner };
        w.FolderBox.Text = startIn ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return w.ShowDialog() == true ? (w.FolderBox.Text.Trim(), w.NameBox.Text.Trim()) : null;
    }

    /// <summary>What the window says will happen, for <paramref name="folder"/> and <paramref name="name"/> - for
    /// tests as for the window.</summary>
    internal static string Describe(string folder, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Loc.T("newPack.where.noName");
        if (string.IsNullOrWhiteSpace(folder)) return Loc.T("newPack.where.noFolder");
        string where;
        try { where = NewPackPlace.Where(folder.Trim(), name.Trim()); }
        catch (Exception) { return Loc.T("newPack.where.badFolder"); }
        bool inside = !string.Equals(Path.GetFullPath(folder.Trim()).TrimEnd('\\'), where.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        return inside ? Loc.F("newPack.where.inside", "folder", where) : Loc.F("newPack.where.here", "folder", where);
    }

    private void Changed(object sender, System.Windows.Controls.TextChangedEventArgs? e)
    {
        if (WhereText == null || CreateButton == null) return;
        WhereText.Text = Describe(FolderBox.Text, NameBox.Text);
        bool ok;
        try { ok = !string.IsNullOrWhiteSpace(NameBox.Text) && !string.IsNullOrWhiteSpace(FolderBox.Text) && Path.IsPathRooted(FolderBox.Text.Trim()); }
        catch (ArgumentException) { ok = false; }
        CreateButton.IsEnabled = ok;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (Services.TestMode.Active) return;   // no folder picker under the test harness
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("newPack.browse.title") };
        try { if (Directory.Exists(FolderBox.Text.Trim())) dialog.InitialDirectory = FolderBox.Text.Trim(); }
        catch (ArgumentException) { }
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private void Create_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
