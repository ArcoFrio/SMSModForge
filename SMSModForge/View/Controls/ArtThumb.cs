using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Localization;

namespace SMSModForge.View.Controls;

/// <summary>
/// A small picture of what an action row targets - a bust, a level's
/// GameObject, a place - fitted into a square (the author, 1.6.3). A scene has
/// <see cref="ScenePreview"/>, which draws its frame as well.
/// <para/>
/// Cheap by design, since a row is built each time its node is selected: the
/// file is decoded at the size it is shown rather than whole, away from the
/// window, and remembered (see <see cref="Thumbnails"/>). No background of its
/// own, so it sits in the row like the rest of the row does.
/// </summary>
public sealed class ArtThumb : Grid
{
    public static readonly DependencyProperty PackRootProperty =
        DependencyProperty.Register(nameof(PackRoot), typeof(string), typeof(ArtThumb),
            new PropertyMetadata(null, OnInputChanged));

    /// <summary>The pack's folder, for a pack-relative <see cref="Sprite"/>.</summary>
    public string? PackRoot
    {
        get => (string?)GetValue(PackRootProperty);
        set => SetValue(PackRootProperty, value);
    }

    public static readonly DependencyProperty SpriteProperty =
        DependencyProperty.Register(nameof(Sprite), typeof(string), typeof(ArtThumb),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>The picture: pack-relative, or a full path to art that comes
    /// with ModForge. Empty shows nothing.</summary>
    public string Sprite
    {
        get => (string)GetValue(SpriteProperty);
        set => SetValue(SpriteProperty, value);
    }

    public static readonly DependencyProperty BoxSizeProperty =
        DependencyProperty.Register(nameof(BoxSize), typeof(double), typeof(ArtThumb),
            new PropertyMetadata(128.0, (d, _) => { var t = (ArtThumb)d; t.ApplySize(); t.Refresh(); }));

    /// <summary>How big the square is.</summary>
    public double BoxSize
    {
        get => (double)GetValue(BoxSizeProperty);
        set => SetValue(BoxSizeProperty, value);
    }

    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _placeholder = new()
    {
        Foreground = Brushes.Gray,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 11,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4),
        Visibility = Visibility.Collapsed,
    };

    /// <summary>Counts what has been asked for, so a picture that arrives
    /// after the row has moved on to another is dropped.</summary>
    private int _asked;

    public ArtThumb()
    {
        View.ToolTipDismisser.KeepClearOf(this);
        LocText.Follow(this, Refresh);

        Background = Brushes.Transparent;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        // Shrunk, nearly always: smoothed, or thin lines drop out.
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Children.Add(_image);
        Children.Add(_placeholder);
        ApplySize();
        Refresh();
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ArtThumb)d).Refresh();

    private void ApplySize()
    {
        double size = BoxSize > 0 ? BoxSize : 128;
        Width = MinWidth = MaxWidth = size;
        Height = MinHeight = MaxHeight = size;
    }

    /// <summary>Pixels to decode to: twice the box, for a screen scaled up
    /// and for smoothing to work from.</summary>
    private int Side => (int)System.Math.Ceiling((BoxSize > 0 ? BoxSize : 128) * 2);

    private void Refresh()
    {
        int asked = ++_asked;
        string sprite = Sprite ?? "";
        if (string.IsNullOrWhiteSpace(sprite)) { Show(null, ""); return; }

        string? path = Shared.PackPaths.IsFullPath(sprite) ? sprite
                     : string.IsNullOrEmpty(PackRoot) ? null
                     : Path.Combine(PackRoot, sprite.Replace('/', Path.DirectorySeparatorChar));
        if (path == null) { Show(null, Loc.T("preview.saveFirst")); return; }

        string name = Path.GetFileName(sprite);
        if (!File.Exists(path)) { Show(null, Loc.F("preview.art.missing", "file", name)); return; }

        int side = Side;
        if (Thumbnails.TryCached(path, side, out var hit)) { Shown(hit, name); return; }

        // Decoded away from the window; until then the square stays empty
        // rather than showing what the row pointed at before.
        _image.Source = null;
        _placeholder.Visibility = Visibility.Collapsed;
        Task.Run(() => Thumbnails.Load(path, side)).ContinueWith(t =>
            Dispatcher.InvokeAsync(() =>
            {
                if (asked == _asked) Shown(t.Result, name);
            }), TaskScheduler.Default);
    }

    private void Shown(System.Windows.Media.Imaging.BitmapSource? picture, string name)
        => Show(picture, picture == null ? Loc.F("preview.art.unreadable", "file", name) : "");

    private void Show(System.Windows.Media.Imaging.BitmapSource? picture, string message)
    {
        _image.Source = picture;
        _image.Visibility = picture == null ? Visibility.Collapsed : Visibility.Visible;
        _placeholder.Text = message;
        _placeholder.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>What is drawn now, for tests: the picture, or null.</summary>
    internal ImageSource? Picture => _image.Source;

    /// <summary>What the square says instead of a picture, for tests.</summary>
    internal string Message => _placeholder.Visibility == Visibility.Visible ? _placeholder.Text : "";
}
