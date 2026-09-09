using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using SMSModForge.Rendering;

namespace SMSModForge.View;

/// <summary>
/// Choosing the picture on a UI object.
/// <para/>
/// A dialog rather than a dropdown, for two reasons. There are 840 vanilla
/// sprites, which is not a list anybody scrolls; and a dropdown bound to the
/// value writes as you move through it, so merely LOOKING put sprites nobody
/// chose onto the undo stack. Nothing here touches the object until Choose is
/// pressed.
/// <para/>
/// It also answers the other half of the question - a picture can be one the
/// game ships or a PNG the pack ships - behind one button, because from the
/// object's side both are just a name.
/// </summary>
public partial class SpritePickerWindow : Window
{
    private List<string> _all = new();

    /// <summary>What was chosen: a vanilla sprite name, or a pack-relative
    /// file path.</summary>
    public string Chosen { get; private set; } = "";

    private string? _packRoot;

    public SpritePickerWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { SearchBox.Focus(); };
    }

    private void Search_Changed(object sender, RoutedEventArgs e) => Refill();

    private void Refill()
    {
        string term = SearchBox.Text.Trim();
        var shown = term.Length == 0
            ? _all
            : _all.Where(n => n.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        // Capped, because typing one letter matches hundreds and a list box
        // asked to realise all of them stutters on every keystroke.
        List.ItemsSource = shown.Take(400).ToList();
        if (List.Items.Count > 0) List.SelectedIndex = 0;
    }

    private void List_Changed(object sender, RoutedEventArgs e)
    {
        Preview.Source = null;
        if (List.SelectedItem is not string name) return;

        try
        {
            string file = VanillaUiLibrary.Assets.FileFor(name);
            if (!string.IsNullOrEmpty(file) && File.Exists(file))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(file);
                image.EndInit();
                image.Freeze();
                Preview.Source = image;
            }
        }
        catch { /* a preview that will not load is not worth an error */ }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a picture from the pack",
            Filter = "Images (*.png;*.jpg)|*.png;*.jpg|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(_packRoot) ? _packRoot : null,
        };
        if (dialog.ShowDialog(this) != true) return;

        // Stored pack-relative, so the pack stays movable. A file from outside
        // the pack is taken as-is and flagged by the validator rather than
        // silently rewritten to something that will not resolve.
        Chosen = Relative(dialog.FileName);
        DialogResult = true;
    }

    private string Relative(string path)
    {
        if (string.IsNullOrEmpty(_packRoot)) return path;
        try
        {
            string root = Path.GetFullPath(_packRoot).TrimEnd(Path.DirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                 ? full.Substring(root.Length).Replace('\\', '/')
                 : path;
        }
        catch { return path; }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is string name) { Chosen = name; DialogResult = true; }
    }

    /// <summary>Show the picker; returns what was chosen, or null when it was
    /// cancelled. Null means leave the object exactly as it was.</summary>
    public static string? Pick(Window owner, string? packRoot, string current)
    {
        var w = new SpritePickerWindow { Owner = owner, _packRoot = packRoot };
        w._all = VanillaUiLibrary.SpriteNames.ToList();
        w.SearchBox.Text = current ?? "";
        w.Refill();

        // Start on what the object already has, so opening it and pressing
        // Choose changes nothing.
        if (!string.IsNullOrEmpty(current) && w.List.Items.Contains(current))
            w.List.SelectedItem = current;

        return w.ShowDialog() == true ? w.Chosen : null;
    }
}
