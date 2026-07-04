using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SMSModForge.View.Controls;

/// <summary>
/// Compositing preview for the Scenes tab. Stacks two layers — the scene
/// image (mapped to <c>Core/Art</c> at runtime) and the frame (mapped to
/// <c>Core</c>) — in the same z-order the game uses: art behind, frame in
/// front. Both layers are loaded from PNG files: the scene sprite from a
/// pack-relative path, the frame from either the shipped
/// <c>VanillaFrames/</c> folder or a custom pack-relative path.
/// <para/>
/// Follows the same code-only control pattern as <see cref="BustPreview"/>.
/// </summary>
public sealed class ScenePreview : Grid
{
    // ── Dependency properties ──────────────────────────────────────────

    public static readonly DependencyProperty PackRootProperty =
        DependencyProperty.Register(nameof(PackRoot), typeof(string), typeof(ScenePreview),
            new PropertyMetadata(null, OnInputChanged));

    /// <summary>Absolute path to the pack folder on disk, or null if unsaved.</summary>
    public string? PackRoot
    {
        get => (string?)GetValue(PackRootProperty);
        set => SetValue(PackRootProperty, value);
    }

    public static readonly DependencyProperty SceneSpriteProperty =
        DependencyProperty.Register(nameof(SceneSprite), typeof(string), typeof(ScenePreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Pack-relative path to the scene image PNG.</summary>
    public string SceneSprite
    {
        get => (string)GetValue(SceneSpriteProperty);
        set => SetValue(SceneSpriteProperty, value);
    }

    public static readonly DependencyProperty VanillaFrameProperty =
        DependencyProperty.Register(nameof(VanillaFrame), typeof(string), typeof(ScenePreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Filename of a vanilla frame in the shipped <c>VanillaFrames/</c> folder.</summary>
    public string VanillaFrame
    {
        get => (string)GetValue(VanillaFrameProperty);
        set => SetValue(VanillaFrameProperty, value);
    }

    public static readonly DependencyProperty CustomFrameSpriteProperty =
        DependencyProperty.Register(nameof(CustomFrameSprite), typeof(string), typeof(ScenePreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Pack-relative path to a custom frame PNG. Takes precedence over <see cref="VanillaFrame"/>.</summary>
    public string CustomFrameSprite
    {
        get => (string)GetValue(CustomFrameSpriteProperty);
        set => SetValue(CustomFrameSpriteProperty, value);
    }

    // ── Visual children ────────────────────────────────────────────────

    // Both layers stay at native size (Stretch.None) and get the SAME 1.5x
    // LayoutTransform, so scene + frame scale by an identical factor about the
    // same centre and stay registered — the compositing the game does. (Fitting
    // each to the box independently would scale them differently and misalign,
    // since the scene and frame PNGs aren't the same dimensions.)
    private readonly Image _sceneImage = new()
    {
        Stretch = Stretch.None,
        LayoutTransform = new ScaleTransform(1.5, 1.5),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true,
    };
    private readonly Image _frameImage = new()
    {
        Stretch = Stretch.None,
        LayoutTransform = new ScaleTransform(1.5, 1.5),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true,
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

    /// <summary>Fixed display size. Pinned the same way as <see cref="JigglePreview.FixedSize"/>.</summary>
    public const double FixedSize = 480;

    public ScenePreview()
    {
        Width = MinWidth = MaxWidth = FixedSize;
        Height = MinHeight = MaxHeight = FixedSize;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        RenderOptions.SetBitmapScalingMode(_sceneImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_frameImage, BitmapScalingMode.NearestNeighbor);
        // Scene image behind, frame in front — same z-order as the game.
        Children.Add(_sceneImage);
        Children.Add(_frameImage);
        Children.Add(_placeholder);
        Refresh();
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ScenePreview)d).Refresh();

    private void Refresh()
    {
        string root = PackRoot ?? "";
        string sprite = SceneSprite ?? "";

        // Nothing useful to show when no sprite path is authored.
        if (string.IsNullOrWhiteSpace(sprite))
        {
            ShowPlaceholder(string.IsNullOrEmpty(root)
                ? "Save the pack and set a scene sprite path."
                : "No scene sprite path set.");
            return;
        }

        if (string.IsNullOrEmpty(root))
        {
            ShowPlaceholder("Save the pack first so paths can be resolved.");
            return;
        }

        // Resolve scene image.
        string scenePath = Path.Combine(root, sprite);
        if (!File.Exists(scenePath))
        {
            ShowPlaceholder($"Scene sprite not found:\n{scenePath}");
            return;
        }

        _sceneImage.Source = TryLoad(scenePath);
        _sceneImage.Visibility = Visibility.Visible;

        // Resolve frame — custom wins over vanilla, matching runtime.
        string? framePath = ResolveFramePath(root);
        if (framePath != null && File.Exists(framePath))
        {
            _frameImage.Source = TryLoad(framePath);
            _frameImage.Visibility = Visibility.Visible;
        }
        else
        {
            _frameImage.Source = null;
            _frameImage.Visibility = Visibility.Collapsed;
        }

        _placeholder.Visibility = Visibility.Collapsed;
    }

    private void ShowPlaceholder(string message)
    {
        _sceneImage.Source = null;
        _frameImage.Source = null;
        _sceneImage.Visibility = Visibility.Collapsed;
        _frameImage.Visibility = Visibility.Collapsed;
        _placeholder.Text = message;
        _placeholder.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Determine the absolute frame path using the same precedence as the
    /// runtime: <see cref="CustomFrameSprite"/> (pack-relative) wins; if
    /// absent, <see cref="VanillaFrame"/> resolves against the shipped
    /// <c>VanillaFrames/</c> folder next to the editor exe.
    /// </summary>
    private string? ResolveFramePath(string packRoot)
    {
        string custom = CustomFrameSprite ?? "";
        if (!string.IsNullOrWhiteSpace(custom))
            return Path.Combine(packRoot, custom);

        string vanilla = VanillaFrame ?? "";
        if (!string.IsNullOrWhiteSpace(vanilla))
        {
            string? frameDir = FindVanillaFramesFolder();
            if (frameDir != null)
                return Path.Combine(frameDir, vanilla);
        }

        return null;
    }

    /// <summary>
    /// Locate the <c>VanillaFrames</c> folder that ships next to the
    /// editor exe. Mirrors the runtime's
    /// <see cref="SMSModForge.PackPlugin.SceneFactory.ResolvePluginFrameRoot"/>
    /// logic — except the editor's exe sits in a different place from
    /// the BepInEx plugin DLL, so we resolve from our own assembly.
    /// </summary>
    private static string? FindVanillaFramesFolder()
    {
        try
        {
            string? exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(exeDir)) return null;
            string candidate = Path.Combine(exeDir, "VanillaFrames");
            return Directory.Exists(candidate) ? candidate : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Load a PNG into a frozen <see cref="BitmapImage"/> without holding
    /// the file open. Returns null on any I/O failure.
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
