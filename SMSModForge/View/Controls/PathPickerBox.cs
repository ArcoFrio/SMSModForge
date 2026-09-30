using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SMSModForge.Localization;

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
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((PathPickerBox)d).ShowBorrowed()));

    /// <summary>The path value — bind this where the TextBox's Text was bound.</summary>
    public string PathText
    {
        get => (string)GetValue(PathTextProperty);
        set => SetValue(PathTextProperty, value);
    }

    public static readonly DependencyProperty PackRootProperty =
        DependencyProperty.Register(nameof(PackRoot), typeof(string), typeof(PathPickerBox),
            new PropertyMetadata(null, (d, _) => ((PathPickerBox)d).ShowBorrowed()));

    /// <summary>Pack folder for relative mode; null/empty = absolute mode.</summary>
    public string? PackRoot
    {
        get => (string?)GetValue(PackRootProperty);
        set => SetValue(PackRootProperty, value);
    }

    public static readonly DependencyProperty FilterProperty =
        DependencyProperty.Register(nameof(Filter), typeof(string), typeof(PathPickerBox),
            new PropertyMetadata(null));

    /// <summary>OpenFileDialog filter. Defaults to PNG since most pack paths are sprites -
    /// read when the dialog opens, so it is in the language on screen then.</summary>
    public string Filter
    {
        get => (string)GetValue(FilterProperty) ?? PickerFilters.Png;
        set => SetValue(FilterProperty, value);
    }

    // ── Visual children ────────────────────────────────────────────────

    private readonly TextBox _box = new();
    private readonly Button _browse = new()
    {
        Content = "…",
        Width = 26,
        Margin = new Thickness(4, 0, 0, 0),
    };

    public PathPickerBox()
    {
        LastChildFill = true;
        LocText.Bind(_browse, ToolTipProperty, "picker.choose.tip");
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
        ForwardToolTip();

        _browse.Click += (_, __) => Browse();
        // Its words are worked out as it draws: again when the language changes.
        LocText.Follow(this, ShowBorrowed);
    }

    private void ForwardToolTip()
        => _box.SetBinding(ToolTipProperty, new Binding(nameof(ToolTip)) { Source = this });

    /// <summary>A value that borrows the game's own art rather than naming a
    /// file shows as such - see <see cref="BorrowedLook"/>.</summary>
    private void ShowBorrowed()
    {
        bool outside = OutsideLook.Apply(_box, PathText, PackRoot);
        if (BorrowedLook.Apply(_box, PathText)) _box.ToolTip = BorrowedLook.Tip(PathText);
        else if (!outside) ForwardToolTip();
    }

    private void Browse()
    {
        // A picker raised during a test run stops the suite dead, waiting for a
        // click nobody is there to give, on a dialog over somebody's work. See
        // CLAUDE.md - this is one of the four kinds that must be guarded.
        if (Services.TestMode.Active) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Filter,
            Title = Loc.T("picker.dialogTitle"),
            InitialDirectory = ResolveInitialDirectory(),
        };
        if (dlg.ShowDialog() != true) return;

        Remember(dlg.FileName);

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
            // Offered rather than refused (1.6.3): the file is copied in, and
            // saying where to is what shows an author that a pack's files live
            // in its folder.
            string to = Destination(root, fullPick, PathText);
            if (MessageBox.Show(Loc.F("picker.outsidePack", "file", fullPick, "to", to),
                                Loc.T("picker.outsidePack.title"), MessageBoxButton.YesNo,
                                MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try { PathText = BringIntoPack(root, fullPick, to); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(Loc.F("picker.outsidePack.failed", "why", ex.Message),
                                Loc.T("picker.outsidePack.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }
        // Wire format: forward slashes, pack-relative.
        PathText = fullPick.Substring(fullRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Where a file from outside the pack would be copied to, inside it: beside
    /// the file the field already names, when that is in the pack, or else in
    /// an <c>Imported</c> folder - with a number added when that name is taken
    /// by a different file. Forward slashes, pack-relative.
    /// </summary>
    internal static string Destination(string packRoot, string file, string? current)
    {
        string folder = "Imported";
        if (!string.IsNullOrWhiteSpace(current) && !Shared.PackPaths.IsFullPath(current)
            && !Shared.PackPaths.LeavesThePack(current) && !Shared.GameArt.IsBorrowed(current))
        {
            string? dir = Path.GetDirectoryName(current.Replace('/', Path.DirectorySeparatorChar));
            if (!string.IsNullOrEmpty(dir)) folder = dir.Replace(Path.DirectorySeparatorChar, '/');
        }

        string name = Path.GetFileNameWithoutExtension(file), ext = Path.GetExtension(file);
        for (int n = 1; ; n++)
        {
            string candidate = folder + "/" + name + (n == 1 ? "" : " (" + n + ")") + ext;
            string abs = Path.Combine(packRoot, candidate.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(abs) || SameBytes(abs, file)) return candidate;
        }
    }

    /// <summary>Copy <paramref name="file"/> into the pack as
    /// <paramref name="to"/> (see <see cref="Destination"/>), and give back the
    /// path to store.</summary>
    internal static string BringIntoPack(string packRoot, string file, string to)
    {
        string abs = Path.Combine(packRoot, to.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        if (!File.Exists(abs)) File.Copy(file, abs);
        return to;
    }

    private static bool SameBytes(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// The folder the last file was picked from, shared by every picker in the
    /// editor.
    /// <para/>
    /// An outfit's art sits together — base, mask, blink and the numbered
    /// frames are one folder — so after the first pick the rest are one click
    /// away rather than four levels of tree away. Per session and not saved:
    /// it is where you were a moment ago, not a setting.
    /// </summary>
    private static string _lastPicked = "";

    internal static void Remember(string file)
    {
        try
        {
            string? dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) _lastPicked = dir!;
        }
        catch { /* a path the framework will not parse is not worth keeping */ }
    }

    /// <summary>
    /// Where to open: the folder this field already points at, else the one the
    /// last pick came from, else the pack root, else wherever the dialog was.
    /// <para/>
    /// The remembered folder is only offered when it is INSIDE this pack. It is
    /// shared across every picker and lives as long as the editor does, so
    /// without that check, opening a second pack would start you off in the
    /// first one's art folder — and a pack-relative field cannot store
    /// anything from there anyway.
    /// </summary>
    internal string ResolveInitialDirectory()
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

            if (Directory.Exists(_lastPicked) && Inside(_lastPicked, root)) return _lastPicked;
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) return root;
        }
        catch { /* fall through to dialog default */ }
        return "";
    }

    /// <summary>Whether a folder is within the pack. Always true in absolute
    /// mode, which has no pack to be outside of.</summary>
    private static bool Inside(string folder, string root)
    {
        if (string.IsNullOrEmpty(root)) return true;
        try
        {
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
            return (Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar)
                   .StartsWith(full, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Forget where the last pick was. For the tests, which must not
    /// leave one another a folder.</summary>
    internal static void ForgetLastPicked() => _lastPicked = "";
}
