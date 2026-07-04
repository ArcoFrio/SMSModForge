using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// Compositing preview for the Places tab. Builds the whole scene at the
/// game's native level resolution (2048×1136) inside a <see cref="Viewbox"/>
/// so every pixel coordinate from the layout spec maps 1:1, then the Viewbox
/// scales it down to the on-screen display size. Layers, bottom to top:
/// <list type="number">
///   <item>Secondary sprite — the distance/blur background.</item>
///   <item>Base sprite — the main foreground art.</item>
///   <item>GameplayUI overlay — the vanilla HUD frame
///         (<c>VanillaOverlays/GameplayUI.png</c>), or
///         <c>GameplayUIExtended.png</c> once the place has more than six
///         navigator buttons (mirrors the runtime's extended nav strip).</item>
///   <item>Navigator buttons — one <c>ButtonNavigator.png</c> per authored
///         navigator button, laid out in a single horizontal row that stays
///         centred on the canvas as buttons are added, at the in-game strip
///         height. Each carries its order number (Barton font) and its
///         label override (Curse Casual font).</item>
/// </list>
/// </summary>
public sealed class PlacePreview : Grid
{
    // ── Layout constants (all in native 2048×1136 canvas pixels) ────────

    private const double CanvasWidth = 2048;
    private const double CanvasHeight = 1136;

    /// <summary>ButtonNavigator.png native size. Buttons sit edge-to-edge at
    /// this pitch — no overlap, no extra gap. The wrap row pitch reuses it so
    /// rows touch vertically too (same "no extra spacing" rule).</summary>
    private const double ButtonSize = 150;

    /// <summary>Columns per navigator row. Matches the runtime grid: the
    /// strip holds six buttons, then wraps to a second row above.</summary>
    private const int Columns = 6;

    /// <summary>Row pitch (centre-to-centre) when buttons wrap to a second
    /// row. 70% of the sprite height (the touching pitch reduced by 30%) so
    /// the upper row sits a little closer to the strip row.</summary>
    private const double VerticalPitch = ButtonSize * 0.7;

    /// <summary>Vertical centre of the bottom (strip) navigator row, measured
    /// from the canvas top (pixel 1052). The button midpoint lands here; when
    /// a second row is needed it stacks above by <see cref="VerticalPitch"/>.</summary>
    private const double ButtonCenterY = 1052;

    /// <summary>Local (button-space, top-left origin) centre of the order
    /// number.</summary>
    private const double NumberCenterX = 74;
    private const double NumberCenterY = 44;

    /// <summary>Local centre of the label-override text.</summary>
    private const double LabelCenterX = 74;
    private const double LabelCenterY = 93;

    /// <summary>Max width (button-local px) the label may occupy before it
    /// wraps to the next line. 128 of the 150-wide button → ~11px side
    /// margins.</summary>
    private const double LabelMaxWidth = 128;

    /// <summary>Line-spacing multiplier for wrapped label text: 0.5 = the
    /// font's natural line height reduced by 50%, pulling the lines closer.</summary>
    private const double LabelLineHeightFactor = 0.5;

    // Font sizes aren't part of the positional spec; these read close to the
    // in-game HUD and are easy to nudge.
    private const double NumberFontSize = 46;
    private const double LabelFontSize = 35.7;  // 42px reduced 15%

    // ── Display size ────────────────────────────────────────────────────
    // 90% of half the native resolution (half scale, then 10% smaller).
    public const double FixedWidth = 921.6;
    public const double FixedHeight = 511.2;

    // ── Dependency properties ──────────────────────────────────────────

    public static readonly DependencyProperty PackRootProperty =
        DependencyProperty.Register(nameof(PackRoot), typeof(string), typeof(PlacePreview),
            new PropertyMetadata(null, OnInputChanged));

    public string? PackRoot
    {
        get => (string?)GetValue(PackRootProperty);
        set => SetValue(PackRootProperty, value);
    }

    public static readonly DependencyProperty BaseSpriteProperty =
        DependencyProperty.Register(nameof(BaseSprite), typeof(string), typeof(PlacePreview),
            new PropertyMetadata("", OnInputChanged));

    public string BaseSprite
    {
        get => (string)GetValue(BaseSpriteProperty);
        set => SetValue(BaseSpriteProperty, value);
    }

    public static readonly DependencyProperty SecondarySpriteProperty =
        DependencyProperty.Register(nameof(SecondarySprite), typeof(string), typeof(PlacePreview),
            new PropertyMetadata("", OnInputChanged));

    public string SecondarySprite
    {
        get => (string)GetValue(SecondarySpriteProperty);
        set => SetValue(SecondarySpriteProperty, value);
    }

    /// <summary>The place's navigator-button view models, in instantiation
    /// order. The preview numbers them 1..N and renders their labels.</summary>
    public static readonly DependencyProperty ButtonsProperty =
        DependencyProperty.Register(nameof(Buttons), typeof(IEnumerable), typeof(PlacePreview),
            new PropertyMetadata(null, OnButtonsChanged));

    public IEnumerable? Buttons
    {
        get => (IEnumerable?)GetValue(ButtonsProperty);
        set => SetValue(ButtonsProperty, value);
    }

    // ── Visual tree ─────────────────────────────────────────────────────

    private readonly Canvas _canvas = new()
    {
        Width = CanvasWidth,
        Height = CanvasHeight,
        ClipToBounds = true,
    };
    private readonly Image _secondaryImage = NewLayerImage();
    private readonly Image _baseImage = NewLayerImage();
    private readonly Image _overlayImage = NewLayerImage();
    private readonly Canvas _buttonLayer = new()
    {
        Width = CanvasWidth,
        Height = CanvasHeight,
    };
    private readonly Viewbox _viewbox = new()
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
        FontSize = 13,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8),
    };

    private static BitmapImage? _cachedOverlay;
    private static BitmapImage? _cachedOverlayExtended;
    private static BitmapImage? _cachedButton;
    private static FontFamily? _numberFont;
    private static FontFamily? _labelFont;

    public PlacePreview()
    {
        Width = MinWidth = MaxWidth = FixedWidth;
        Height = MinHeight = MaxHeight = FixedHeight;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));

        _canvas.Children.Add(_secondaryImage);
        _canvas.Children.Add(_baseImage);
        _canvas.Children.Add(_overlayImage);
        _canvas.Children.Add(_buttonLayer);
        _viewbox.Child = _canvas;

        Children.Add(_viewbox);
        Children.Add(_placeholder);

        Refresh();
    }

    private static Image NewLayerImage()
    {
        var img = new Image
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
        };
        Canvas.SetLeft(img, 0);
        Canvas.SetTop(img, 0);
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        return img;
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PlacePreview)d).Refresh();

    // ── Buttons binding plumbing ────────────────────────────────────────

    private static void OnButtonsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (PlacePreview)d;
        if (e.OldValue is INotifyCollectionChanged oldCc)
            oldCc.CollectionChanged -= self.OnButtonsCollectionChanged;
        self.UnsubscribeItems(e.OldValue as IEnumerable);

        if (e.NewValue is INotifyCollectionChanged newCc)
            newCc.CollectionChanged += self.OnButtonsCollectionChanged;
        self.SubscribeItems(e.NewValue as IEnumerable);

        self.RebuildButtons();
    }

    private void OnButtonsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Re-sync per-item subscriptions (labels feed the rendered text) and
        // rebuild — the list is tiny (≤12) so a full rebuild is cheap.
        UnsubscribeItems(e.OldItems);
        SubscribeItems(e.NewItems);
        RebuildButtons();
    }

    private void SubscribeItems(IEnumerable? items)
    {
        if (items == null) return;
        foreach (var it in items)
            if (it is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnButtonItemPropertyChanged;
    }

    private void UnsubscribeItems(IEnumerable? items)
    {
        if (items == null) return;
        foreach (var it in items)
            if (it is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnButtonItemPropertyChanged;
    }

    private void OnButtonItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Label edits change the rendered text; nothing else affects layout.
        if (e.PropertyName == nameof(NavigatorButtonViewModel.Label) || string.IsNullOrEmpty(e.PropertyName))
            RebuildButtons();
    }

    // ── Composition ─────────────────────────────────────────────────────

    private void Refresh()
    {
        string root = PackRoot ?? "";
        string basePath = BaseSprite ?? "";

        if (string.IsNullOrWhiteSpace(basePath))
        {
            ShowPlaceholder(string.IsNullOrEmpty(root)
                ? "Save the pack and set a base sprite path."
                : "No base sprite path set.");
            return;
        }
        if (string.IsNullOrEmpty(root))
        {
            ShowPlaceholder("Save the pack first so paths can be resolved.");
            return;
        }

        string absBase = Path.Combine(root, Normalize(basePath));
        if (!File.Exists(absBase))
        {
            ShowPlaceholder($"Base sprite not found:\n{absBase}");
            return;
        }

        // Secondary (distance/blur) layer — behind base.
        string secondary = SecondarySprite ?? "";
        if (!string.IsNullOrWhiteSpace(secondary))
        {
            string absSecondary = Path.Combine(root, Normalize(secondary));
            _secondaryImage.Source = File.Exists(absSecondary) ? TryLoad(absSecondary) : null;
        }
        else
        {
            _secondaryImage.Source = null;
        }
        _secondaryImage.Visibility = _secondaryImage.Source != null ? Visibility.Visible : Visibility.Collapsed;

        _baseImage.Source = TryLoad(absBase);
        _baseImage.Visibility = Visibility.Visible;

        ApplyOverlay();
        RebuildButtons();

        _viewbox.Visibility = Visibility.Visible;
        _placeholder.Visibility = Visibility.Collapsed;
    }

    /// <summary>Chooses the standard or extended HUD frame based on the
    /// current navigator-button count.</summary>
    private void ApplyOverlay()
    {
        bool extended = CountButtons() > Columns;
        var src = extended ? LoadOverlayExtended() : LoadOverlay();
        _overlayImage.Source = src;
        _overlayImage.Visibility = src != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildButtons()
    {
        _buttonLayer.Children.Clear();

        // Buttons only render alongside the rest of the composition.
        if (_viewbox.Visibility != Visibility.Visible && _placeholder.Visibility == Visibility.Visible)
            return;

        var list = Buttons?.Cast<object>().ToList();
        int n = list?.Count ?? 0;

        // The overlay frame depends on the count, so keep it in sync here too
        // (RebuildButtons runs on add/remove/reorder, Refresh doesn't).
        if (_placeholder.Visibility != Visibility.Visible)
            ApplyOverlay();

        if (n == 0) return;

        var buttonSprite = LoadButton();
        FontFamily numberFont = NumberFont();
        FontFamily labelFont = LabelFont();

        // Wrap exactly like the runtime navigator grid: up to six buttons per
        // row, then a second row. The LATER buttons fill the bottom (strip)
        // row and earlier ones stack above — matching the runtime's "last row
        // sits where the single row normally would" shift. Each row is
        // centred on its own button count, so the group centre stays fixed.
        int rows = (n + Columns - 1) / Columns;   // ceil(n / 6), ≤ 2 (cap is 12)
        int bottomRow = rows - 1;

        for (int i = 0; i < n; i++)
        {
            int row = i / Columns;
            int colInRow = i - row * Columns;
            int countInRow = (row < bottomRow) ? Columns : (n - row * Columns);

            double rowWidth = countInRow * ButtonSize;
            double startLeft = (CanvasWidth - rowWidth) / 2.0;
            double left = startLeft + colInRow * ButtonSize;

            // Bottom row at the strip centre (y=1052); each row above is one
            // VerticalPitch higher.
            double centerY = ButtonCenterY - (bottomRow - row) * VerticalPitch;
            double top = centerY - ButtonSize / 2.0;

            var slot = new Canvas { Width = ButtonSize, Height = ButtonSize };
            Canvas.SetLeft(slot, left);
            Canvas.SetTop(slot, top);

            if (buttonSprite != null)
            {
                var img = new Image
                {
                    Source = buttonSprite,
                    Width = ButtonSize,
                    Height = ButtonSize,
                    Stretch = Stretch.Fill,
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(img, 0);
                Canvas.SetTop(img, 0);
                slot.Children.Add(img);
            }

            // Order number (Barton), 1-based, centred at (74,44).
            AddCenteredText(slot, (i + 1).ToString(), numberFont, NumberFontSize,
                            NumberCenterX, NumberCenterY, Brushes.White, FontWeights.Normal, 0, 0);

            // Label override (Curse Casual), centred at (74,93), black,
            // wrapping past LabelMaxWidth with tightened line spacing.
            string label = GetLabel(list![i]);
            if (!string.IsNullOrEmpty(label))
                AddCenteredText(slot, label, labelFont, LabelFontSize, LabelCenterX, LabelCenterY,
                                Brushes.Black, FontWeights.Normal, LabelMaxWidth, LabelLineHeightFactor);

            _buttonLayer.Children.Add(slot);
        }
    }

    /// <summary>
    /// Adds a TextBlock to <paramref name="slot"/> whose geometric centre sits
    /// exactly at (<paramref name="cx"/>, <paramref name="cy"/>) in the slot's
    /// local (top-left origin) space. The block is unconstrained so it
    /// measures to its content; a SizeChanged hook re-centres it once the size
    /// is known (also handles the async font load settling in).
    /// </summary>
    private static void AddCenteredText(Canvas slot, string text, FontFamily font,
                                        double fontSize, double cx, double cy, Brush foreground,
                                        FontWeight fontWeight, double maxWidth, double lineHeightFactor)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = font,
            FontSize = fontSize,
            FontWeight = fontWeight,
            Foreground = foreground,
            TextAlignment = TextAlignment.Center,
        };
        // maxWidth > 0 caps the measure so long labels wrap to extra lines;
        // the SizeChanged recenter then keeps the whole (possibly multi-line)
        // block centred on (cx, cy) both ways.
        if (maxWidth > 0)
        {
            tb.MaxWidth = maxWidth;
            tb.TextWrapping = TextWrapping.Wrap;
        }
        // lineHeightFactor > 0 overrides the natural line spacing (scaled off
        // the font's own line height) so wrapped lines sit closer together.
        if (lineHeightFactor > 0)
        {
            tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            tb.LineHeight = font.LineSpacing * fontSize * lineHeightFactor;
        }
        void Recenter()
        {
            Canvas.SetLeft(tb, cx - tb.ActualWidth / 2.0);
            Canvas.SetTop(tb, cy - tb.ActualHeight / 2.0);
        }
        tb.SizeChanged += (_, _) => Recenter();
        // Initial placement so it's roughly right before the first measure.
        Canvas.SetLeft(tb, cx);
        Canvas.SetTop(tb, cy);
        slot.Children.Add(tb);
    }

    private int CountButtons() => Buttons?.Cast<object>().Count() ?? 0;

    private static string GetLabel(object item)
        => item is NavigatorButtonViewModel vm ? (vm.Label ?? "") : "";

    private void ShowPlaceholder(string message)
    {
        _secondaryImage.Source = null;
        _baseImage.Source = null;
        _buttonLayer.Children.Clear();
        _viewbox.Visibility = Visibility.Collapsed;
        _placeholder.Text = message;
        _placeholder.Visibility = Visibility.Visible;
    }

    private static string Normalize(string p) => p?.Replace('/', Path.DirectorySeparatorChar) ?? "";

    // ── Asset loading ───────────────────────────────────────────────────

    private static string? OverlayDir()
    {
        string? exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrEmpty(exeDir) ? null : Path.Combine(exeDir, "VanillaOverlays");
    }

    private static BitmapImage? LoadOverlay() => _cachedOverlay ??= LoadOverlayFile("GameplayUI.png");
    private static BitmapImage? LoadOverlayExtended() => _cachedOverlayExtended ??= LoadOverlayFile("GameplayUIExtended.png");
    private static BitmapImage? LoadButton() => _cachedButton ??= LoadOverlayFile("ButtonNavigator.png");

    private static BitmapImage? LoadOverlayFile(string fileName)
    {
        try
        {
            string? dir = OverlayDir();
            if (dir == null) return null;
            string path = Path.Combine(dir, fileName);
            return File.Exists(path) ? TryLoad(path) : null;
        }
        catch { return null; }
    }

    private static FontFamily NumberFont() => _numberFont ??= LoadFont("Barton.otf", "Segoe UI");
    private static FontFamily LabelFont() => _labelFont ??= LoadFont("Curse Casual.ttf", "Segoe UI");

    private static FontFamily LoadFont(string fileName, string fallback)
    {
        try
        {
            string? dir = OverlayDir();
            if (dir != null)
            {
                string path = Path.Combine(dir, fileName);
                if (File.Exists(path))
                {
                    var fam = Fonts.GetFontFamilies(path).FirstOrDefault();
                    if (fam != null) return fam;
                }
            }
        }
        catch { /* fall through to system fallback */ }
        return new FontFamily(fallback);
    }

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
