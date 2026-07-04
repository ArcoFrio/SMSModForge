using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// Static portrait preview used by the Actors tab and the Dialogue node
/// editor. Composes two layers — a bust base sprite and (optionally) an
/// expression overlay — picked up either from a pack-authored outfit
/// (PNGs under the pack folder) or from the plugin's
/// <c>VanillaBustArt/&lt;bustName&gt;/</c> dump.
/// <para/>
/// Unlike <see cref="JigglePreview"/>, this control runs no shader —
/// it's a flat 2-layer alpha composite, which is the right fidelity for
/// "what does this actor look like right now?" without re-running the
/// CPU jiggle every time a node selection changes.
/// </summary>
public sealed class BustPreview : Grid
{
    public static readonly DependencyProperty MainViewModelProperty =
        DependencyProperty.Register(nameof(MainViewModel), typeof(MainViewModel), typeof(BustPreview),
            new PropertyMetadata(null, OnInputChanged));

    /// <summary>The top-level VM — we read <see cref="MainViewModel.Pack"/> and <see cref="MainViewModel.PackRoot"/> from it.</summary>
    public MainViewModel? MainViewModel
    {
        get => (MainViewModel?)GetValue(MainViewModelProperty);
        set => SetValue(MainViewModelProperty, value);
    }

    public static readonly DependencyProperty BustKeyProperty =
        DependencyProperty.Register(nameof(BustKey), typeof(string), typeof(BustPreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>GO name to look up. Same format the runtime uses for <c>2_Bust_Manager.Find(...)</c>.</summary>
    public string BustKey
    {
        get => (string)GetValue(BustKeyProperty);
        set => SetValue(BustKeyProperty, value);
    }

    public static readonly DependencyProperty ExpressionKeyProperty =
        DependencyProperty.Register(nameof(ExpressionKey), typeof(string), typeof(BustPreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Optional expression key (e.g. "Happy"). Empty = no overlay.</summary>
    public string ExpressionKey
    {
        get => (string)GetValue(ExpressionKeyProperty);
        set => SetValue(ExpressionKeyProperty, value);
    }

    private readonly Image _baseImage = new()
    {
        Stretch = Stretch.Uniform,
        SnapsToDevicePixels = true,
    };
    private readonly Image _expressionImage = new()
    {
        Stretch = Stretch.Uniform,
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
    public const double FixedSize = 320;

    public BustPreview()
    {
        Width = MinWidth = MaxWidth = FixedSize;
        Height = MinHeight = MaxHeight = FixedSize;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        RenderOptions.SetBitmapScalingMode(_baseImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_expressionImage, BitmapScalingMode.NearestNeighbor);
        Children.Add(_baseImage);
        Children.Add(_expressionImage);
        Children.Add(_placeholder);
        Refresh();
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((BustPreview)d).Refresh();

    private void Refresh()
    {
        var vm = MainViewModel;
        if (vm?.Pack == null || string.IsNullOrEmpty(BustKey))
        {
            ShowPlaceholder("No bust selected");
            return;
        }

        string? basePath = VanillaArtResolver.FindBaseSpritePath(BustKey, vm.Pack, vm.PackRoot);
        if (basePath == null)
        {
            // Inform the user *why* — most common causes are an unsaved
            // pack (so pack-relative paths can't resolve) or the shipped
            // vanilla art folder being absent from this build.
            string reason;
            if (VanillaArtResolver.FindArtRoot() == null)
                reason = "No VanillaBustArt folder shipped with this build. " +
                         "Run Tools/UnityEditor/SMSModForgeArtExtractor.cs inside the vanilla " +
                         "game's Unity project, drop the output into " +
                         "SMSModForge/Resources/VanillaBustArt/, and rebuild.";
            else if (string.IsNullOrEmpty(vm.PackRoot))
                reason = "Save the pack first so the editor can resolve pack-relative paths.";
            else
                reason = "No art for '" + BustKey + "' — neither a pack outfit " +
                         "nor a shipped vanilla bust matches that GameObject name.";
            ShowPlaceholder(reason);
            return;
        }

        _baseImage.Source = TryLoad(basePath);
        var exprPath = VanillaArtResolver.FindExpressionSpritePath(BustKey, ExpressionKey, vm.Pack, vm.PackRoot);
        _expressionImage.Source = exprPath != null ? TryLoad(exprPath) : null;
        _placeholder.Visibility = Visibility.Collapsed;
        _baseImage.Visibility = Visibility.Visible;
        _expressionImage.Visibility = Visibility.Visible;
    }

    private void ShowPlaceholder(string message)
    {
        _baseImage.Source = null;
        _expressionImage.Source = null;
        _baseImage.Visibility = Visibility.Collapsed;
        _expressionImage.Visibility = Visibility.Collapsed;
        _placeholder.Text = message;
        _placeholder.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Loads a PNG with cached caching mode and a forced-stream read so
    /// the file isn't held open after the bitmap is materialised. Returns
    /// null on any I/O failure (so a broken file doesn't blow up the
    /// dialogue editor).
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
