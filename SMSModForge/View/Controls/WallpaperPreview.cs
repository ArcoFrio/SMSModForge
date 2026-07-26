using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SMSModForge.View.Controls;

/// <summary>
/// Preview for the Wallpapers tab. A wallpaper is a single full-bleed
/// 1920×1080 image, so — unlike <see cref="ScenePreview"/>, which stacks
/// registered layers at native size — this one just fits the image to the
/// box and letterboxes it (<see cref="Stretch.Uniform"/>). It's sized 16:9
/// and large, since judging a wallpaper is the whole point of looking at it.
/// <para/>
/// Resolves the sprite with the same precedence as the runtime + model:
/// pack-relative <c>SpritePath</c> wins over the legacy absolute
/// <c>ExternalSpritePath</c>.
/// <para/>
/// Follows the same code-only control pattern as <see cref="ScenePreview"/>.
/// </summary>
public sealed class WallpaperPreview : Grid
{
    // ── Dependency properties ──────────────────────────────────────────

    public static readonly DependencyProperty PackRootProperty =
        DependencyProperty.Register(nameof(PackRoot), typeof(string), typeof(WallpaperPreview),
            new PropertyMetadata(null, OnInputChanged));

    /// <summary>Absolute path to the pack folder on disk, or null if unsaved.</summary>
    public string? PackRoot
    {
        get => (string?)GetValue(PackRootProperty);
        set => SetValue(PackRootProperty, value);
    }

    public static readonly DependencyProperty SpritePathProperty =
        DependencyProperty.Register(nameof(SpritePath), typeof(string), typeof(WallpaperPreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Pack-relative path to the wallpaper PNG.</summary>
    public string SpritePath
    {
        get => (string)GetValue(SpritePathProperty);
        set => SetValue(SpritePathProperty, value);
    }

    public static readonly DependencyProperty ExternalSpritePathProperty =
        DependencyProperty.Register(nameof(ExternalSpritePath), typeof(string), typeof(WallpaperPreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Absolute on-disk path (legacy). Used only when
    /// <see cref="SpritePath"/> is empty, matching the model's precedence.</summary>
    public string ExternalSpritePath
    {
        get => (string)GetValue(ExternalSpritePathProperty);
        set => SetValue(ExternalSpritePathProperty, value);
    }

    // ── Visual children ────────────────────────────────────────────────

    private readonly Image _image = new()
    {
        // Uniform, not None: wallpaper PNGs are 1920×1080 and would dwarf the
        // pane at native size. Letterboxing preserves aspect so what's shown
        // is framed the way the game frames it.
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
    };
    private readonly TextBlock _dimensions = new()
    {
        Foreground = Brushes.Gray,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        FontSize = 10,
        Margin = new Thickness(0, 0, 4, 2),
    };

    /// <summary>Fixed display size — 16:9, matching the 1920×1080 source.
    /// Pinned like <see cref="ScenePreview.FixedSize"/>.</summary>
    public const double FixedWidth = 640;
    public const double FixedHeight = 360;

    public WallpaperPreview()
    {
        Width = MinWidth = MaxWidth = FixedWidth;
        Height = MinHeight = MaxHeight = FixedHeight;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        // Bilinear (the default), not NearestNeighbor as the pixel-art scene
        // frames use: this is a downscale of a large photo-like image, where
        // point sampling would just alias.
        Children.Add(_image);
        Children.Add(_dimensions);
        Children.Add(_placeholder);
        Refresh();
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WallpaperPreview)d).Refresh();

    private void Refresh()
    {
        string? path = ResolvePath();

        if (path == null)
        {
            ShowPlaceholder(string.IsNullOrEmpty(PackRoot)
                ? "Save the pack and set a wallpaper image path."
                : "No wallpaper image path set.");
            return;
        }
        if (!File.Exists(path))
        {
            ShowPlaceholder($"Wallpaper image not found:\n{path}");
            return;
        }

        var img = TryLoad(path);
        if (img == null)
        {
            ShowPlaceholder($"Couldn't read image:\n{path}");
            return;
        }

        _image.Source = img;
        _image.Visibility = Visibility.Visible;
        // Surface the real dimensions — a wallpaper that isn't 1920×1080 will
        // be stretched by the runtime, and that's invisible in a fitted preview.
        _dimensions.Text = $"{img.PixelWidth} × {img.PixelHeight}" +
                           (img.PixelWidth == 1920 && img.PixelHeight == 1080 ? "" : "  (expected 1920 × 1080)");
        _dimensions.Visibility = Visibility.Visible;
        _placeholder.Visibility = Visibility.Collapsed;
    }

    /// <summary>Pack-relative wins over the legacy absolute path — the same
    /// precedence the model documents and the runtime applies.</summary>
    private string? ResolvePath()
    {
        string rel = SpritePath ?? "";
        if (!string.IsNullOrWhiteSpace(rel))
        {
            string root = PackRoot ?? "";
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, rel);
        }
        string ext = ExternalSpritePath ?? "";
        return string.IsNullOrWhiteSpace(ext) ? null : ext;
    }

    private void ShowPlaceholder(string message)
    {
        _image.Source = null;
        _image.Visibility = Visibility.Collapsed;
        _dimensions.Visibility = Visibility.Collapsed;
        _placeholder.Text = message;
        _placeholder.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Load a PNG into a frozen <see cref="BitmapImage"/> without holding the
    /// file open. Returns null on any I/O failure.
    /// </summary>
    private static BitmapImage? TryLoad(string path)
    {
        try
        {
            var img = new BitmapImage();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = fs;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
