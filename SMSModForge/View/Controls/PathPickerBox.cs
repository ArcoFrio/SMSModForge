using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SMSModForge.View.Controls;

/// <summary>
/// A path TextBox with a trailing "…" browse button — drop-in replacement for
/// the bare TextBoxes that hold file paths. The text stays fully editable
/// (paths can still be typed or pasted); the button just fills it via an
/// OpenFileDialog.
/// <para/>
/// Two modes, chosen by <see cref="PackRoot"/>:
/// <list type="bullet">
///   <item><b>Pack-relative</b> (PackRoot set) — the picked file must live
///   inside the pack folder; the stored value is the forward-slash relative
///   path (the wire format every pack field uses). Picking a file outside
///   the pack is refused with an explanation rather than silently storing a
///   path the exporter can't bundle.</item>
///   <item><b>Absolute</b> (PackRoot null/empty) — the full path is stored
///   as-is. Used only by legacy fields like the wallpaper external path.</item>
/// </list>
/// Follows the code-only control pattern of <see cref="ScenePreview"/> etc.
/// </summary>
public sealed class PathPickerBox : DockPanel
{
    // ── Dependency properties ──────────────────────────────────────────

    public static readonly DependencyProperty PathTextProperty =
        DependencyProperty.Register(nameof(PathText), typeof(string), typeof(PathPickerBox),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>The path value — bind this where the TextBox's Text was bound.</summary>
    public string PathText
    {
        get => (string)GetValue(PathTextProperty);
        set => SetValue(PathTextProperty, value);
    }

    public static readonly DependencyProperty PackRootProperty =
        DependencyProperty.Register(nameof(PackRoot), typeof(string), typeof(PathPickerBox),
            new PropertyMetadata(null));

    /// <summary>Pack folder for relative mode; null/empty = absolute mode.</summary>
    public string? PackRoot
    {
        get => (string?)GetValue(PackRootProperty);
        set => SetValue(PackRootProperty, value);
    }

    public static readonly DependencyProperty FilterProperty =
        DependencyProperty.Register(nameof(Filter), typeof(string), typeof(PathPickerBox),
            new PropertyMetadata("Image files (*.png)|*.png|All files (*.*)|*.*"));

    /// <summary>OpenFileDialog filter. Defaults to PNG since most pack paths are sprites.</summary>
    public string Filter
    {
        get => (string)GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    // ── Visual children ────────────────────────────────────────────────

    private readonly TextBox _box = new();
    private readonly Button _browse = new()
    {
        Content = "…",
        Width = 26,
        Margin = new Thickness(4, 0, 0, 0),
        ToolTip = "Choose a file",
    };

    public PathPickerBox()
    {
        LastChildFill = true;
        SetDock(_browse, Dock.Right);
        Children.Add(_browse);
        Children.Add(_box);

        _box.SetBinding(TextBox.TextProperty, new Binding(nameof(PathText))
        {
            Source = this,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        // Forward the container's tooltip to the textbox (ToolTip doesn't inherit).
        _box.SetBinding(ToolTipProperty, new Binding(nameof(ToolTip)) { Source = this });

        _browse.Click += (_, __) => Browse();
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Filter,
            Title = "Choose file",
            InitialDirectory = ResolveInitialDirectory(),
        };
        if (dlg.ShowDialog() != true) return;

        string root = PackRoot ?? "";
        if (string.IsNullOrEmpty(root))
        {
            PathText = dlg.FileName;   // absolute mode
            return;
        }

        // Pack-relative mode: the file must be bundleable, i.e. inside the
        // pack folder — the exporter zips that folder and the runtime reads
        // paths relative to it.
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPick = Path.GetFullPath(dlg.FileName);
        if (!fullPick.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "The file must be inside the pack folder so it gets bundled on export:\n" +
                root + "\n\nCopy it into the pack folder first, then pick it from there.",
                "Outside pack folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // Wire format: forward slashes, pack-relative.
        PathText = fullPick.Substring(fullRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Open where the current value points when it resolves, else the
    /// pack root, else wherever the dialog last was.</summary>
    private string ResolveInitialDirectory()
    {
        try
        {
            string current = PathText ?? "";
            string root = PackRoot ?? "";
            string abs = string.IsNullOrWhiteSpace(current) ? ""
                : Path.IsPathRooted(current) ? current
                : string.IsNullOrEmpty(root) ? ""
                : Path.Combine(root, current.Replace('/', Path.DirectorySeparatorChar));
            string? dir = string.IsNullOrEmpty(abs) ? null : Path.GetDirectoryName(abs);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir!;
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) return root;
        }
        catch { /* fall through to dialog default */ }
        return "";
    }
}
