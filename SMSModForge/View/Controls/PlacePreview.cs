using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Path = System.IO.Path;

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
    // The preview fills whatever width its container gives it (the editor pane,
    // which grows with the window and the sidebar splitter) and derives its
    // height from the native 2048×1136 aspect, so resolution is preserved.
    // FixedWidth/FixedHeight are the default / fallback (and the size the
    // offscreen render harness uses) — 90% of half the native resolution.
    public const double FixedWidth = 921.6;
    public const double FixedHeight = 511.2;

    /// <summary>Native aspect ratio (width / height) the control locks to.</summary>
    private const double Aspect = CanvasWidth / CanvasHeight;   // 2048 / 1136 = 1.8028

    /// <summary>On-screen scale (canvas px → display px): recomputed on every
    /// resize as ActualWidth / CanvasWidth and pushed into <see cref="_extentScale"/>.
    /// A LayoutTransform (not a Viewbox) so the canvas can GROW past the level
    /// rect (far-flung GameObjects) and scroll instead of shrinking to fit.</summary>
    private double _displayScale = FixedWidth / CanvasWidth;   // 0.45 until first layout

    // ── World mapping (GameObjects) ───────────────────────────────
    // Overlay positions are authored in Unity world units; level sprites
    // load at 70.32 pixels-per-unit with the level centred on the world
    // origin, so world (0,0) is the canvas centre and +Y is up.
    private const double PixelsPerUnit = 70.32;
    private const double WorldOriginX = CanvasWidth / 2.0;
    private const double WorldOriginY = CanvasHeight / 2.0;

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

    /// <summary>
    /// The place's whole GameObject tree (<see cref="GameObjectViewModel"/>
    /// roots). Everything the preview draws comes from here: sprite objects at
    /// their world positions, the forced NPCs-root node whose subtree composes
    /// local transforms down the chain, and the NPC placements hanging off those
    /// nodes. Positions outside the level rect grow the scrollable area.
    /// </summary>
    public static readonly DependencyProperty GameObjectsProperty =
        DependencyProperty.Register(nameof(GameObjects), typeof(IEnumerable), typeof(PlacePreview),
            new PropertyMetadata(null, OnGameObjectsChanged));

    public IEnumerable? GameObjects
    {
        get => (IEnumerable?)GetValue(GameObjectsProperty);
        set => SetValue(GameObjectsProperty, value);
    }

    /// <summary>The whole pack's NPC catalog (<see cref="NpcViewModel"/>), so a
    /// placement's <c>npc</c> key can be resolved to its pose art / shadow style.
    /// Bound from the window's <c>Npcs</c> collection.</summary>
    public static readonly DependencyProperty NpcCatalogProperty =
        DependencyProperty.Register(nameof(NpcCatalog), typeof(IEnumerable), typeof(PlacePreview),
            new PropertyMetadata(null, OnNpcCatalogChanged));

    public IEnumerable? NpcCatalog
    {
        get => (IEnumerable?)GetValue(NpcCatalogProperty);
        set => SetValue(NpcCatalogProperty, value);
    }

    // ── Visual tree ─────────────────────────────────────────────────────

    // ClipToBounds deliberately OFF: GameObjects may sit outside the
    // level rect and must still render (the extent host grows to hold them).
    private readonly Canvas _canvas = new()
    {
        Width = CanvasWidth,
        Height = CanvasHeight,
        ClipToBounds = false,
    };
    private readonly Image _secondaryImage = NewLayerImage();
    private readonly Image _baseImage = NewLayerImage();

    /// <summary>
    /// The level art + every GameObject, z-ordered by sorting order.
    /// <para/>
    /// GameObjects used to live in one canvas pinned in FRONT of the
    /// base image, so an overlay authored behind the level (SecretBeach's sky
    /// / portal / gatekeeper are all negative) previewed in front of it — the
    /// opposite of what the game does. Now the base sprite, the secondary
    /// sprite and the overlays all go into this one host and are added in
    /// sorting-order sequence, so the preview's stacking is the runtime's.
    /// The HUD frame and map buttons stay above it unconditionally.
    /// </summary>
    private readonly Canvas _worldLayer = new()
    {
        Width = CanvasWidth,
        Height = CanvasHeight,
        ClipToBounds = false,
    };
    private readonly Image _overlayImage = NewLayerImage();
    private readonly Canvas _buttonLayer = new()
    {
        Width = CanvasWidth,
        Height = CanvasHeight,
        // Decorative HUD nav buttons — never a selection target, so let clicks
        // in the button strip fall through to the NPC / overlay beneath.
        IsHitTestVisible = false,
    };

    /// <summary>Scroll host: sized to the union of the level rect and every
    /// overlay sprite (so scrollbars only appear when something sits outside
    /// the level), with the fixed display scale as a LayoutTransform.</summary>
    private readonly ScaleTransform _extentScale = new(FixedWidth / CanvasWidth, FixedWidth / CanvasWidth);
    private readonly Canvas _extentHost = new()
    {
        Width = CanvasWidth,
        Height = CanvasHeight,
    };
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Focusable = false,
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
        // Fill the width the container gives us; the height follows the native
        // aspect (see MeasureOverride) and the on-screen scale is recomputed on
        // resize (SizeChanged), so the preview tracks the window + splitter.
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        _extentHost.LayoutTransform = _extentScale;
        SizeChanged += (_, e) => UpdateDisplayScale(e.NewSize.Width);
        // Focusable so grabbing a gizmo handle can pull keyboard focus off any
        // text field being edited — otherwise that field's native Ctrl+Z would
        // swallow the app's undo shortcut after a drag.
        Focusable = true;
        FocusVisualStyle = null;

        // _worldLayer's contents (level sprites + overlays) are (re)ordered by
        // sorting order in RebuildOverlayGos; the HUD frame and buttons are
        // chrome and always sit on top. The gizmo layer sits above everything so
        // its handles are always grabbable.
        _canvas.Children.Add(_worldLayer);
        _canvas.Children.Add(_overlayImage);
        _canvas.Children.Add(_buttonLayer);
        _canvas.Children.Add(_gizmoLayer);

        Canvas.SetLeft(_canvas, 0);
        Canvas.SetTop(_canvas, 0);
        _extentHost.Children.Add(_canvas);
        _scroll.Content = _extentHost;

        // Clicking empty room area (an unhandled click that bubbles up) clears
        // the gizmo selection; clicks on an NPC / overlay mark themselves handled.
        _canvas.Background = Brushes.Transparent;
        _canvas.MouseLeftButtonDown += (_, e) => { if (!e.Handled) Deselect(); };

        // Gizmo drags capture the (stable) gizmo layer, so a rebuild recreating
        // the handle shapes mid-drag can't drop the capture.
        _gizmoLayer.MouseMove += OnGizmoMouseMove;
        _gizmoLayer.MouseLeftButtonUp += OnGizmoMouseUp;

        Children.Add(_scroll);
        Children.Add(_placeholder);
        BuildObjectMenu();
        Children.Add(_objectMenu);
        BuildGizmoToolbar();
        Children.Add(_gizmoToolbar);

        // The Wet droplet markers animate only while the control is actually
        // visible (not on a hidden/unselected tab).
        IsVisibleChanged += (_, _) => { _isLoaded = IsVisible; UpdateWetTimer(); };

        Refresh();
    }

    /// <summary>Lock the control to the native aspect: take the width the parent
    /// offers and derive the height from 2048×1136, so it fills the editor pane's
    /// width (which grows with the window + sidebar splitter) at preserved
    /// proportions. Width-driven on purpose — the editor is a vertical scroller,
    /// so a taller preview at a wider window just scrolls.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double w = availableSize.Width;
        if (double.IsInfinity(w) || double.IsNaN(w) || w <= 0)
            w = FixedWidth;
        double h = w / Aspect;
        base.MeasureOverride(new Size(w, h));
        return new Size(w, h);
    }

    /// <summary>Recompute the canvas→display scale from the live width so the
    /// whole scene (level art, NPCs, gizmo, water) tracks the window + splitter.</summary>
    private void UpdateDisplayScale(double controlWidth)
    {
        // Cap the object menu's height to the current preview height (it scrolls).
        _objectMenu.MaxHeight = Math.Max(60, ActualHeight - 12);
        if (controlWidth <= 0) return;
        double scale = controlWidth / CanvasWidth;
        if (Math.Abs(scale - _displayScale) < 1e-4) return;
        _displayScale = scale;
        _extentScale.ScaleX = _extentScale.ScaleY = scale;
    }

    private static Image NewLayerImage()
    {
        var img = new Image
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
            // Base / secondary / HUD are full-canvas images. WPF hit-tests an
            // Image over its whole rectangle (transparent pixels included), so a
            // hit-testable HUD layer would swallow every click before it reached
            // an NPC. They're decoration — take them out of hit testing so the
            // gizmo's selection clicks land on the NPC / overlay underneath.
            IsHitTestVisible = false,
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
        RebuildOverlayGos();

        _scroll.Visibility = Visibility.Visible;
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
        if (_scroll.Visibility != Visibility.Visible && _placeholder.Visibility == Visibility.Visible)
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
        _worldLayer.Children.Clear();
        _scroll.Visibility = Visibility.Collapsed;
        _placeholder.Text = message;
        _placeholder.Visibility = Visibility.Visible;
    }

    // ── GameObjects layer ─────────────────────────────────────────

    private static void OnGameObjectsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (PlacePreview)d;
        if (e.OldValue is INotifyCollectionChanged oldCc)
            oldCc.CollectionChanged -= self.OnOverlayTreeChanged;
        if (e.NewValue is INotifyCollectionChanged newCc)
            newCc.CollectionChanged += self.OnOverlayTreeChanged;
        self.RebuildOverlayGos();
    }

    private void OnOverlayTreeChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildOverlayGos();
    private void OnOverlayItemChanged(object? sender, PropertyChangedEventArgs e) => RebuildOverlayGos();

    /// <summary>Everything currently subscribed for tree changes, so a rebuild
    /// can cleanly re-subscribe the whole (possibly restructured) tree.</summary>
    private readonly System.Collections.Generic.List<object> _overlaySubscriptions = new();

    /// <summary>Re-subscribe the whole GameObject tree: every node, its child and
    /// NPC collections, and each placement's four part transforms — so any edit
    /// anywhere in the hierarchy redraws the preview.</summary>
    private void ResubscribeOverlayTree()
    {
        foreach (var s in _overlaySubscriptions)
        {
            if (s is INotifyPropertyChanged inpc) inpc.PropertyChanged -= OnOverlayItemChanged;
            if (s is INotifyCollectionChanged incc) incc.CollectionChanged -= OnOverlayTreeChanged;
        }
        _overlaySubscriptions.Clear();
        SubscribeOverlayTree(GameObjects);
    }

    private void SubscribeOverlayTree(IEnumerable? items)
    {
        if (items == null) return;
        foreach (var it in items)
        {
            if (it is not GameObjectViewModel o) continue;
            o.PropertyChanged += OnOverlayItemChanged;
            _overlaySubscriptions.Add(o);
            o.Children.CollectionChanged += OnOverlayTreeChanged;
            _overlaySubscriptions.Add(o.Children);
            o.Npcs.CollectionChanged += OnOverlayTreeChanged;
            _overlaySubscriptions.Add(o.Npcs);
            foreach (var pl in o.Npcs)
            {
                foreach (var part in new INotifyPropertyChanged[] { pl, pl.Body, pl.Shadow, pl.Blink, pl.Wet })
                {
                    part.PropertyChanged += OnOverlayItemChanged;
                    _overlaySubscriptions.Add(part);
                }
                pl.Children.CollectionChanged += OnOverlayTreeChanged;
                _overlaySubscriptions.Add(pl.Children);
                SubscribeOverlayTree(pl.Children);       // props parented under the NPC
            }
            SubscribeOverlayTree(o.Children);
        }
    }

    /// <summary>
    /// Render every GameObject at its world position (70.32 px/unit,
    /// origin at the canvas centre, +Y up), ordered by sorting order, then
    /// grow the scrollable extent to the union of the level rect and every
    /// sprite so far-flung objects can be scrolled to instead of vanishing.
    /// Inactive-at-start objects render ghosted so placement stays visible.
    /// </summary>
    /// <summary>
    /// Sorting order of the level's own base sprite. Both level sprites render
    /// BEHIND the placed NPCs: an NPC's parts span shadow (-3) → body (-1/0) →
    /// blink (+1), and in the reference pack the visible room is the opaque
    /// secondary while the base is a sparse layer; any foreground that must sit
    /// over the NPCs (a couch arm, shower glass) is authored as its own overlay
    /// at a positive order. So the level art belongs in the gap between the
    /// lowest NPC part (shadow, -3) and the "behind the level" overlays
    /// (SecretBeach sky -12 … flash -9): base just under the shadow, secondary
    /// one below the base.
    /// <para/>
    /// If a level's prototype turns out to use a different base order, only
    /// these two constants need to change.
    /// </summary>
    private const int LevelBaseSortingOrder = -4;

    /// <summary>
    /// Sorting order of the level art this preview is actually drawing, and of
    /// the layer behind it. Defaults to the pack-place convention above; a
    /// vanilla extension overrides them with what the extraction found.
    /// <para/>
    /// The constants only ever described the prototype a PACK place is built
    /// from. Vanilla levels each pick their own — Downtown draws its base at
    /// -10 and its far layer at -15 — so judging "is this behind the level art"
    /// against a fixed -4 was wrong for every vanilla level, and reported NPCs
    /// at -9 as buried when they are comfortably in front.
    /// </summary>
    public static readonly DependencyProperty LevelArtOrderProperty =
        DependencyProperty.Register(nameof(LevelArtOrder), typeof(int), typeof(PlacePreview),
            new PropertyMetadata(LevelBaseSortingOrder, OnInputChanged));

    public int LevelArtOrder
    {
        get => (int)GetValue(LevelArtOrderProperty);
        set => SetValue(LevelArtOrderProperty, value);
    }

    public static readonly DependencyProperty LevelArtSecondaryOrderProperty =
        DependencyProperty.Register(nameof(LevelArtSecondaryOrder), typeof(int), typeof(PlacePreview),
            new PropertyMetadata(LevelSecondarySortingOrder, OnInputChanged));

    public int LevelArtSecondaryOrder
    {
        get => (int)GetValue(LevelArtSecondaryOrderProperty);
        set => SetValue(LevelArtSecondaryOrderProperty, value);
    }

    /// <summary>Sorting order of the level's secondary (distance/blur) sprite,
    /// which renders behind the base.</summary>
    private const int LevelSecondarySortingOrder = -5;

    private void RebuildOverlayGos()
    {
        ResubscribeOverlayTree();
        _worldLayer.Children.Clear();
        _wetMarkers.Clear();

        double minX = 0, minY = 0, maxX = CanvasWidth, maxY = CanvasHeight;

        string root = PackRoot ?? "";
        _scene = BuildScene();

        // One ordered list of everything that lives in the level's sorting
        // layer. Ties keep the level sprites underneath the overlays, matching
        // Unity's "later wins at equal order" for our purposes: an overlay
        // authored at exactly the base's order is meant to sit on top of it.
        var drawables = new System.Collections.Generic.List<(int Order, int Tie, UIElement Element)>();

        if (_secondaryImage.Source != null)
            drawables.Add((LevelArtSecondaryOrder, 0, _secondaryImage));
        drawables.Add((LevelArtOrder, 0, _baseImage));

        if (!string.IsNullOrEmpty(root) && _placeholder.Visibility != Visibility.Visible)
        {
            foreach (var entry in _scene)
            {
                var o = entry.Node;
                // PreviewSprite is the pack's own sprite, or — for a node bound
                // to an existing vanilla object — that object's extracted art,
                // so a vanilla level previews fully populated.
                string art = o?.PreviewSprite ?? "";
                if (o == null || string.IsNullOrWhiteSpace(art)) continue;   // containers have no art
                string abs = Path.Combine(root, Normalize(art));
                var bmp = LoadCachedBitmap(abs);   // cached — no per-rebuild PNG decode
                if (bmp == null) continue;

                double w = bmp.PixelWidth, h = bmp.PixelHeight;
                // GameObject sprites load at the level's own px-per-unit, so the
                // sprite's pixels are canvas pixels; the node's world affine
                // carries its position, spin and scale (composed, under NPCs).
                var m = LeafMatrix(entry.World, w, h, PixelsPerUnit);

                var img = new Image
                {
                    Source = bmp,
                    Width = w,
                    Height = h,
                    Stretch = Stretch.Fill,
                    // Dimmed harder for an inactive ANCESTOR than for a node
                    // that merely starts off itself: the game renders nothing
                    // at all under a switched-off parent, so a whole group of
                    // vanilla NPCs waiting to be activated should read as
                    // scenery notes rather than as part of the picture.
                    Opacity = Math.Clamp(o.StartAlpha, 0.0, 1.0) *
                              (entry.ParentInactive ? 0.15 : o.StartActive ? 1.0 : 0.35),
                    RenderTransform = new MatrixTransform(m),
                    ToolTip = o.Name +
                              "\nsorting order " + o.SortingOrder +
                              (o.SortingOrder < LevelArtOrder ? "  (behind the level art)" : "") +
                              (o.StartActive ? "" : "\n(starts inactive)") +
                              (entry.ParentInactive ? "\n(a parent starts inactive — the game draws nothing here)" : ""),
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                var pathForClick = entry.Path;
                var oForClick = o;
                img.MouseLeftButtonDown += (_, e) => { SelectNode(oForClick, pathForClick); e.Handled = true; };
                drawables.Add((o.SortingOrder, 1, img));

                ExpandBounds(m, w, h, ref minX, ref minY, ref maxX, ref maxY);
            }
        }

        // Placed NPCs (pose + shadow + particle marker) go into the same sorted
        // world layer, so their sorting orders interleave with the level art and
        // overlays exactly as at runtime.
        CollectNpcDrawables(root, drawables, ref minX, ref minY, ref maxX, ref maxY);

        // Stable sort: equal orders keep authored sequence, which is what the
        // runtime does for two overlays built back-to-back at the same order.
        foreach (var d in drawables.OrderBy(d => d.Order).ThenBy(d => d.Tie))
            _worldLayer.Children.Add(d.Element);

        UpdateWetTimer();
        RefreshGizmo();
        // The object set only changes on structural rebuilds, not per drag frame.
        if (_activeHandle == GizmoHandle.None) RefreshObjectMenu();

        // Extent: the level rect plus whatever sticks out. The canvas shifts
        // by the negative overhang so everything stays in positive space, and
        // the scroll offset compensates so the LEVEL stays where it was.
        double offX = -minX, offY = -minY;
        Canvas.SetLeft(_canvas, offX);
        Canvas.SetTop(_canvas, offY);
        _extentHost.Width = maxX - minX;
        _extentHost.Height = maxY - minY;
        if (offX > 0) _scroll.ScrollToHorizontalOffset(offX * _displayScale);
        if (offY > 0) _scroll.ScrollToVerticalOffset(offY * _displayScale);
    }

    // ══ NPC layer ═══════════════════════════════════════════════════════
    //
    // Placed NPCs are rendered into the same world layer as the level art and
    // GameObjects. Each NPC's screen transform is the full runtime chain: the
    // forced NPCs-root node, every container node between it and the placement
    // in the authored tree, then the placement's own body transform — composed
    // as 2-D affines in Unity space (Y up, CCW) by the scene walk, then mapped
    // to canvas pixels. The pose is drawn as a still (the animated jiggle lives on
    // the NPCs tab); the shadow is a tinted ellipse; the Wet particle emitter is
    // a lightweight animated droplet marker showing its transform.

    /// <summary>NPC / shadow / circle sprites are authored at 100 px/unit
    /// (level art is 70.32), so a sprite pixel is this many canvas pixels.</summary>
    private const double NpcSpritePpu = 100.0;

    /// <summary>Sorting-order tiers, mirroring the runtime: the NPC sits at its
    /// def order, its shadow well below, its particle marker just above it.</summary>
    private const int NpcShadowTieBelow = 0;   // shadow keeps its own (low) order
    private const int NpcBodyTie = 2;
    private const int NpcWetTie = 3;

    private readonly List<WetMarker> _wetMarkers = new();
    private DispatcherTimer? _wetTimer;
    private bool _isLoaded;
    private DateTime _wetStart = DateTime.Now;

    private static readonly Dictionary<string, BitmapImage?> _bitmapCache = new();

    /// <summary>A 2-D affine transform (Unity convention: +Y up, rotation CCW).
    /// Columns are the mapped basis vectors: x-axis (A,B), y-axis (C,D); (Tx,Ty)
    /// is the origin. Composes the container→body chain before the flip to
    /// canvas space.</summary>
    private readonly struct Aff
    {
        public readonly double A, B, C, D, Tx, Ty;
        public Aff(double a, double b, double c, double d, double tx, double ty)
        { A = a; B = b; C = c; D = d; Tx = tx; Ty = ty; }

        public static readonly Aff Identity = new(1, 0, 0, 1, 0, 0);

        /// <summary>Local transform = translate ∘ rotateZ ∘ scale. X/Y rotation
        /// on a flat sprite under an ortho camera is exactly a cos-foreshorten,
        /// so it folds into the scale (matching the NPC-tab shadow).</summary>
        public static Aff Trs(double x, double y, double rotX, double rotY, double rotZ, double sx, double sy)
        {
            double sxe = sx * Math.Abs(Math.Cos(rotY * Math.PI / 180.0));
            double sye = sy * Math.Abs(Math.Cos(rotX * Math.PI / 180.0));
            double r = rotZ * Math.PI / 180.0;
            double cos = Math.Cos(r), sin = Math.Sin(r);
            // R·S: x-axis = (cos·sxe, sin·sxe), y-axis = (-sin·sye, cos·sye).
            return new Aff(cos * sxe, sin * sxe, -sin * sye, cos * sye, x, y);
        }

        /// <summary>this ∘ child — apply <paramref name="child"/> (child→this
        /// space) then this. Used to walk parent→leaf down the chain.</summary>
        public Aff Then(in Aff c) => new(
            A * c.A + C * c.B, B * c.A + D * c.B,
            A * c.C + C * c.D, B * c.C + D * c.D,
            A * c.Tx + C * c.Ty + Tx, B * c.Tx + D * c.Ty + Ty);
    }

    private static Aff LeafAff(NpcTransformViewModel t)
        => Aff.Trs(t.X, t.Y, t.RotX, t.RotY, t.RotZ, t.ScaleX, t.ScaleY);

    // A GameObject node's own LOCAL transform (2-D: X/Y + Z-rotation + scale).
    private static Aff NodeAff(GameObjectViewModel o)
        => Aff.Trs(o.X, o.Y, 0, 0, o.RotationZ, o.ScaleX, o.ScaleY);

    // ── Scene walk ──────────────────────────────────────────────────────
    //
    // One pass over the GameObject tree resolves everything the preview needs:
    // each node's world transform, the frame its gizmo handles live in, and
    // where every NPC placement sits. Two positioning rules, mirroring the
    // runtime: nodes under the forced NPCs-root node COMPOSE their local
    // transforms down the chain (the runtime builds them with localPosition),
    // while ordinary sprite objects are positioned in WORLD space independently
    // of their parent (the runtime sets transform.position on each).

    /// <summary>One resolved entry in the place's hierarchy — either a
    /// GameObject node or an NPC placement hanging off one — in tree order.</summary>
    private sealed class SceneEntry
    {
        public GameObjectViewModel? Node;        // set for GameObject nodes
        public NpcPlacementViewModel? Npc;       // set for NPC placements
        public string Path = "";                 // stable slash path (selection identity)
        public int Depth;
        public bool InNpcSubtree;                // composed (local) rather than flat (world)
        public Aff World;                        // the node's own world transform
        public Aff ParentWorld;                  // its parent's world — the gizmo frame

        /// <summary>Whether an ANCESTOR starts inactive. Unity renders nothing
        /// under an inactive parent however active the child itself is, so a
        /// node has to be judged on the whole chain — reading its own flag alone
        /// drew every member of a switched-off group at full strength.</summary>
        public bool ParentInactive;
    }

    /// <summary>Walk the whole GameObject tree into a flat, tree-ordered list.</summary>
    private List<SceneEntry> BuildScene()
    {
        var entries = new List<SceneEntry>();
        WalkScene(GameObjects, "", 0, Aff.Identity, false, false, entries);
        return entries;
    }

    private static void WalkScene(IEnumerable? items, string prefix, int depth,
                                  Aff parentWorld, bool inNpcSubtree, bool parentInactive,
                                  List<SceneEntry> into)
    {
        if (items == null) return;
        foreach (var it in items)
        {
            if (it is not GameObjectViewModel o) continue;
            string name = string.IsNullOrWhiteSpace(o.Name) ? "(unnamed)" : o.Name;
            string path = string.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;
            // Two things make a subtree compose rather than sit flat, and both
            // mirror what the runtime does: the NPCs root builds with
            // localPosition, and so does a BOUND node — it's addressing an
            // object that already lives in a hierarchy, so its transform is
            // local by definition. Anything else is a pack-created object,
            // which the runtime places in world space.
            bool composed = inNpcSubtree || o.IsNpcRoot || o.Bind;

            Aff local = NodeAff(o);
            Aff world = composed ? parentWorld.Then(local) : local;
            Aff frameParent = composed ? parentWorld : Aff.Identity;
            bool npc = composed;

            into.Add(new SceneEntry
            {
                Node = o, Path = path, Depth = depth,
                InNpcSubtree = npc, World = world, ParentWorld = frameParent,
                ParentInactive = parentInactive,
            });

            // Everything below inherits this node's switched-off state.
            bool childrenInactive = parentInactive || !o.StartActive;

            foreach (var pl in o.Npcs)
            {
                string label = string.IsNullOrWhiteSpace(pl.Name) ? pl.Npc : pl.Name;
                if (string.IsNullOrWhiteSpace(label)) label = "(unset)";
                string npcPath = path + "/" + label;
                into.Add(new SceneEntry
                {
                    Npc = pl, Path = npcPath, Depth = depth + 1,
                    InNpcSubtree = npc, World = world, ParentWorld = world,
                    ParentInactive = childrenInactive,
                });
                // GameObjects parented under the NPC ride along with its body,
                // so they compose from the body's world transform.
                WalkScene(pl.Children, npcPath, depth + 2,
                          world.Then(LeafAff(pl.Body)), true,
                          childrenInactive || !pl.StartActive, into);
            }

            WalkScene(o.Children, path, depth + 1, world, npc, childrenInactive, into);
        }
    }

    /// <summary>Maps a leaf's world affine + sprite pixel size to the WPF matrix
    /// that places its (0..w, 0..h) pixel rect on the canvas — including the
    /// Unity→canvas Y flip and the level/sprite px-per-unit ratio. Exact for any
    /// rotation / mirror / non-uniform scale (no scale/rotation decomposition).
    /// <paramref name="spritePpu"/> is the sprite's authored pixels-per-unit:
    /// NPC art loads at 100, GameObject sprites at the level's own 70.32 (so
    /// one sprite pixel is one canvas pixel).</summary>
    private static Matrix LeafMatrix(in Aff w, double spx, double spy, double spritePpu = NpcSpritePpu)
    {
        double k = PixelsPerUnit / spritePpu;
        double halfW = spx / (2.0 * spritePpu);
        double halfH = spy / (2.0 * spritePpu);
        // Top-left pixel maps to local unit (-halfW, +halfH).
        double wx = w.A * (-halfW) + w.C * halfH + w.Tx;
        double wy = w.B * (-halfW) + w.D * halfH + w.Ty;
        return new Matrix(
            k * w.A, -k * w.B,      // M11, M12  (x-axis, canvas Y flipped)
            -k * w.C, k * w.D,      // M21, M22  (y-axis)
            PixelsPerUnit * wx + WorldOriginX,
            -PixelsPerUnit * wy + WorldOriginY);
    }

    private static void ExpandBounds(in Matrix m, double w, double h,
        ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        foreach (var p in new[] { m.Transform(new Point(0, 0)), m.Transform(new Point(w, 0)),
                                  m.Transform(new Point(0, h)), m.Transform(new Point(w, h)) })
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
    }

    private void CollectNpcDrawables(string root,
        List<(int Order, int Tie, UIElement Element)> drawables,
        ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        if (_placeholder.Visibility == Visibility.Visible) return;

        var placements = _scene.Where(e => e.Npc != null).ToList();
        if (placements.Count == 0) return;

        var catalog = NpcCatalog?.Cast<object>().OfType<NpcViewModel>()
            .GroupBy(n => n.Key).ToDictionary(g => g.Key, g => g.First());

        foreach (var entry in placements)
        {
            var pl = entry.Npc!;
            if (catalog == null || string.IsNullOrEmpty(pl.Npc)
                || !catalog.TryGetValue(pl.Npc, out var def)) continue;

            // The placement's parent is the node it hangs under, already composed
            // down the NPCs chain by the scene walk.
            Aff body = entry.ParentWorld.Then(LeafAff(pl.Body));
            // Same three-way reading the GameObject rows use: an inactive
            // ancestor means the game draws nothing here at all.
            double ghost = entry.ParentInactive ? 0.15 : pl.StartActive ? 1.0 : 0.4;
            int bodyOrder = def.SortingOrder;
            string tip = (string.IsNullOrWhiteSpace(pl.Name) ? pl.Npc : pl.Name)
                       + "  (" + pl.Npc + ")\nsorting order " + bodyOrder
                       + (pl.StartActive ? "" : "\n(starts inactive)")
                       + (entry.ParentInactive ? "\n(a parent starts inactive — the game draws nothing here)" : "");

            // Shadow (child of the body): a tinted circle, order from the def.
            if (def.ShadowEnabled)
            {
                var sw = body.Then(LeafAff(pl.Shadow));
                var (cr, cg, cb, ca) = BustComposer.ParseTint(def.ShadowColor);
                const double circlePx = 256;   // circle sprite is 256²@100
                var mShadow = LeafMatrix(sw, circlePx, circlePx);
                var ell = new Ellipse
                {
                    Width = circlePx,
                    Height = circlePx,
                    Fill = new SolidColorBrush(Color.FromArgb(
                        (byte)(ca * 255), (byte)(cr * 255), (byte)(cg * 255), (byte)(cb * 255))),
                    Opacity = ghost,
                    RenderTransform = new MatrixTransform(mShadow),
                    IsHitTestVisible = false,
                };
                drawables.Add((def.ShadowSortingOrder, NpcShadowTieBelow, ell));
                ExpandBounds(mShadow, circlePx, circlePx, ref minX, ref minY, ref maxX, ref maxY);
            }

            // Body pose (still). The animated jiggle preview lives on the NPCs tab.
            var bmp = string.IsNullOrWhiteSpace(def.Sprite) ? null
                    : LoadCachedBitmap(Path.Combine(root, Normalize(def.Sprite)));
            if (bmp != null)
            {
                var mBody = LeafMatrix(body, bmp.PixelWidth, bmp.PixelHeight);
                var img = new Image
                {
                    Source = bmp,
                    Width = bmp.PixelWidth,
                    Height = bmp.PixelHeight,
                    Stretch = Stretch.Fill,
                    Opacity = ghost,
                    RenderTransform = new MatrixTransform(mBody),
                    ToolTip = tip,
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var plForClick = pl;
                var plPathForClick = entry.Path;
                img.MouseLeftButtonDown += (_, e) => { SelectPlacement(plForClick, plPathForClick); e.Handled = true; };
                drawables.Add((bodyOrder, NpcBodyTie, img));
                ExpandBounds(mBody, bmp.PixelWidth, bmp.PixelHeight, ref minX, ref minY, ref maxX, ref maxY);
            }

            // Wet particle emitter — a lightweight animated droplet marker at the
            // emitter's transform (the runtime clones the exact vanilla particle,
            // so a transform indicator is all the preview needs). During a gizmo
            // drag we draw only the box + ring (no animated droplets) so a drag
            // doesn't recreate/animate a swarm of ellipses every rebuild.
            if (def.WetEnabled)
            {
                var ww = body.Then(LeafAff(pl.Wet));
                var marker = BuildWetMarker(ww, def.WetStartActive ? ghost : ghost * 0.6,
                                            animated: _activeHandle == GizmoHandle.None);
                drawables.Add((bodyOrder, NpcWetTie, marker.Host));
            }
        }
    }

    // ── Wet particle marker ─────────────────────────────────────────────
    //
    // Modelled on the vanilla shower emitter dumped from Unity: a Box shape,
    // shapeScale (2,1,1), startSpeed 6, startLifetime 2, gravityModifier 0.7,
    // simulationSpace Local. So the spray is an emission box twice as wide as
    // tall, and the droplets fall (gravity over the lifetime) a fixed WORLD
    // distance — not the short fixed column the old marker drew. The emission
    // WIDTH scales with the Wet transform's X (its parent chain × the wet
    // scaleX), so widening the spray is just scaling the Wet part on X; the fall
    // is the physics distance in world units (gravity is world-space, so it
    // doesn't scale with the transform), which reads the same as the game.

    private sealed class WetMarker
    {
        public required Canvas Host;
        public required Ellipse[] Drops;
        public required double[] Lx;        // local X across the emission box, [-1,1]
        public required double[] Sy;        // local Y spawn within the box, [-0.5,0.5]
        public required double[] Progress;  // 0..1 fall progress
        public required double[] Speed;
        public Point O;                     // emitter origin (canvas px)
        public Vector Ax;                   // canvas vector per +1 local X unit
        public Vector Ay;                   // canvas vector per +1 local Y unit (up at rotZ 0)
        public Vector Down;                 // unit canvas direction of the fall
        public double FallCanvas;           // fall length in canvas px (world physics)
    }

    // Vanilla emitter constants (from npc_dump.json's Anna shower particle).
    private const double WetShapeW = 2.0;          // Box shapeScale.x → emission width
    private const double WetShapeH = 1.0;          // Box shapeScale.y → emission height
    private const double WetGravityModifier = 0.7;
    private const double WetLifetime = 2.0;
    private const double UnityGravity = 9.81;
    private const int WetDropCount = 14;   // enough to read as a spray; keeps the 30fps loop cheap
    private static readonly Random _wetRng = new();

    private WetMarker BuildWetMarker(in Aff ww, double opacity, bool animated)
    {
        const double ppu = PixelsPerUnit;
        var o = new Point(WorldOriginX + ww.Tx * ppu, WorldOriginY - ww.Ty * ppu);
        var ax = new Vector(ww.A * ppu, -ww.B * ppu);   // +1 local X in canvas
        var ay = new Vector(ww.C * ppu, -ww.D * ppu);   // +1 local Y in canvas (up)
        var down = new Vector(-ay.X, -ay.Y);
        if (down.Length > 1e-6) down.Normalize(); else down = new Vector(0, 1);
        double fallWorld = 0.5 * WetGravityModifier * UnityGravity * WetLifetime * WetLifetime;
        double fallCanvas = fallWorld * ppu;

        var host = new Canvas { Opacity = opacity, IsHitTestVisible = false };

        // Emission indicator: just the top edge of the box — a line marking where
        // the droplets start. Its length is the spray width (scales with the Wet
        // transform's X); there is deliberately no box height, so the Wet Y scale
        // is irrelevant.
        double hw = WetShapeW / 2, top = WetShapeH / 2;
        Point C(double sx, double sy) => new(o.X + sx * ax.X + sy * ay.X, o.Y + sx * ax.Y + sy * ay.Y);
        Point edgeL = C(-hw, top), edgeR = C(hw, top);
        host.Children.Add(new Line
        {
            X1 = edgeL.X, Y1 = edgeL.Y, X2 = edgeR.X, Y2 = edgeR.Y,
            Stroke = new SolidColorBrush(Color.FromArgb(200, 0xCF, 0xEE, 0xFF)),
            StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });

        int n = animated ? WetDropCount : 0;
        var drops = new Ellipse[n];
        var lx = new double[n]; var sy = new double[n]; var prog = new double[n]; var spd = new double[n];
        var dropBrush = new SolidColorBrush(Color.FromArgb(235, 0x9F, 0xE0, 0xFF));
        var dropStroke = new SolidColorBrush(Color.FromArgb(150, 0x2A, 0x6E, 0xA0));
        for (int i = 0; i < n; i++)
        {
            // Positioned by a TranslateTransform (a render-only change) instead of
            // Canvas.Left/Top — moving many droplets per tick with attached props
            // re-invalidates layout; a transform only re-renders.
            var drop = new Ellipse
            {
                Width = 5, Height = 12, Fill = dropBrush, Stroke = dropStroke, StrokeThickness = 0.8,
                RenderTransform = new TranslateTransform(),
            };
            Canvas.SetLeft(drop, -2.5); Canvas.SetTop(drop, -6);   // centre at the transform's origin
            lx[i] = -0.9 + 1.8 * _wetRng.NextDouble();
            sy[i] = top;   // droplets start on the emission line (top of the box)
            prog[i] = _wetRng.NextDouble();
            spd[i] = 1.0 / WetLifetime * (0.85 + 0.3 * _wetRng.NextDouble());
            host.Children.Add(drop);
            drops[i] = drop;
        }

        var marker = new WetMarker
        {
            Host = host, Drops = drops, Lx = lx, Sy = sy, Progress = prog, Speed = spd,
            O = o, Ax = ax, Ay = ay, Down = down, FallCanvas = fallCanvas,
        };
        for (int i = 0; i < n; i++) PlaceDrop(marker, i);
        if (animated && n > 0) _wetMarkers.Add(marker);
        return marker;
    }

    private static void PlaceDrop(WetMarker m, int i)
    {
        double p = m.Progress[i];
        double cx = m.O.X + m.Lx[i] * m.Ax.X + m.Sy[i] * m.Ay.X + p * m.FallCanvas * m.Down.X;
        double cy = m.O.Y + m.Lx[i] * m.Ax.Y + m.Sy[i] * m.Ay.Y + p * m.FallCanvas * m.Down.Y;
        var t = (TranslateTransform)m.Drops[i].RenderTransform;
        t.X = cx; t.Y = cy;
        // Fade in as it leaves the emitter, out as it reaches the end of its fall.
        m.Drops[i].Opacity = Math.Clamp(Math.Min(p * 6, (1 - p) * 2.5), 0, 1);
    }

    private void UpdateWetTimer()
    {
        if (_isLoaded && _wetMarkers.Count > 0)
        {
            if (_wetTimer == null)
            {
                _wetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
                _wetTimer.Tick += OnWetTick;
                _wetStart = DateTime.Now;
            }
            if (!_wetTimer.IsEnabled) _wetTimer.Start();
        }
        else
        {
            StopWetTimer();
        }
    }

    private void StopWetTimer() => _wetTimer?.Stop();

    private double _wetLastSec;
    private void OnWetTick(object? sender, EventArgs e)
    {
        double now = (DateTime.Now - _wetStart).TotalSeconds;
        double dt = Math.Min(0.1, now - _wetLastSec);
        _wetLastSec = now;

        foreach (var m in _wetMarkers)
        {
            for (int i = 0; i < m.Drops.Length; i++)
            {
                m.Progress[i] += m.Speed[i] * dt;
                if (m.Progress[i] >= 1.0)
                {
                    m.Progress[i] -= 1.0;
                    m.Lx[i] = -0.9 + 1.8 * _wetRng.NextDouble();
                    // Sy stays on the emission line (top edge).
                }
                PlaceDrop(m, i);
            }
        }
    }

    /// <summary>Decode-once bitmap loader (NPC poses + overlay sprites). A full
    /// rebuild happens on every transform edit / gizmo drag, so decoding the
    /// 2048×1136 level-overlay PNGs each time was a big hidden cost — cache them.</summary>
    private static BitmapImage? LoadCachedBitmap(string abs)
    {
        if (_bitmapCache.TryGetValue(abs, out var cached)) return cached;
        var bmp = File.Exists(abs) ? TryLoad(abs) : null;
        _bitmapCache[abs] = bmp;
        return bmp;
    }

    // ── NPC subscriptions ───────────────────────────────────────────────

    private static void OnNpcCatalogChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (PlacePreview)d;
        if (e.OldValue is INotifyCollectionChanged oldCc)
            oldCc.CollectionChanged -= self.OnNpcCatalogCollectionChanged;
        self.UnsubscribeCatalog(e.OldValue as IEnumerable);

        if (e.NewValue is INotifyCollectionChanged newCc)
            newCc.CollectionChanged += self.OnNpcCatalogCollectionChanged;
        self.SubscribeCatalog(e.NewValue as IEnumerable);

        self.RebuildOverlayGos();
    }

    private void OnNpcCatalogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnsubscribeCatalog(e.OldItems);
        SubscribeCatalog(e.NewItems);
        RebuildOverlayGos();
    }

    private void SubscribeCatalog(IEnumerable? items)
    {
        if (items == null) return;
        foreach (var it in items)
            if (it is NpcViewModel n) n.PropertyChanged += OnNpcItemChanged;
    }

    private void UnsubscribeCatalog(IEnumerable? items)
    {
        if (items == null) return;
        foreach (var it in items)
            if (it is NpcViewModel n) n.PropertyChanged -= OnNpcItemChanged;
    }

    private void OnNpcItemChanged(object? sender, PropertyChangedEventArgs e) => RebuildOverlayGos();

    // ══ Transform gizmo ═════════════════════════════════════════════════
    //
    // A Unity-style handle set that edits one part's transform directly in the
    // preview. Click an NPC (or overlay) to select it; a toolbar picks the part
    // (Body / Shadow / Blink / Wet) and the mode (Move / Rotate / Scale). Drags
    // are captured on the (persistent) gizmo layer so a rebuild recreating the
    // handle shapes mid-drag can't drop the capture. Handle drags are converted
    // from canvas pixels into the part's LOCAL space using its parent's world
    // basis, so editing a part under a scaled/rotated container still feels
    // direct. Overlays only expose Move (their model has no rotation / scale).

    /// <summary>Raised (bubbling) when a gizmo drag commits an edit, so the host
    /// can add one undo checkpoint per drag. The gizmo edits by mouse and never
    /// loses keyboard focus, so it can't rely on the window's focus-based
    /// snapshotting.</summary>
    public static readonly RoutedEvent EditCommittedEvent = EventManager.RegisterRoutedEvent(
        nameof(EditCommitted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PlacePreview));

    public event RoutedEventHandler EditCommitted
    {
        add => AddHandler(EditCommittedEvent, value);
        remove => RemoveHandler(EditCommittedEvent, value);
    }

    private enum GizmoMode { Move, Rotate, Scale }
    private enum GizmoPart { Body, Shadow, Blink, Wet }
    private enum GizmoHandle { None, MoveX, MoveY, MoveFree, RotZ, RotX, RotY, ScaleX, ScaleY, ScaleUniform }

    /// <summary>One editable transform, abstracted so the gizmo drives NPC parts
    /// and overlays through the same handles. Channels are LOCAL (parent-space).</summary>
    private interface IGizmoTarget
    {
        double X { get; set; }
        double Y { get; set; }
        double RotZ { get; set; }
        double RotX { get; set; }
        double RotY { get; set; }
        double ScaleX { get; set; }
        double ScaleY { get; set; }
        bool CanRotate { get; }
        bool CanScale { get; }
        bool SupportsTilt { get; }   // X/Y rotation rings (NPC parts only; overlays are flat)
    }

    private sealed class NpcPartTarget : IGizmoTarget
    {
        private readonly NpcTransformViewModel _t;
        public NpcPartTarget(NpcTransformViewModel t) => _t = t;
        public double X { get => _t.X; set => _t.X = (float)value; }
        public double Y { get => _t.Y; set => _t.Y = (float)value; }
        public double RotZ { get => _t.RotZ; set => _t.RotZ = (float)value; }
        public double RotX { get => _t.RotX; set => _t.RotX = (float)value; }
        public double RotY { get => _t.RotY; set => _t.RotY = (float)value; }
        public double ScaleX { get => _t.ScaleX; set => _t.ScaleX = (float)value; }
        public double ScaleY { get => _t.ScaleY; set => _t.ScaleY = (float)value; }
        public bool CanRotate => true;
        public bool CanScale => true;
        public bool SupportsTilt => true;
    }

    private sealed class OverlayTarget : IGizmoTarget
    {
        private readonly GameObjectViewModel _o;
        public OverlayTarget(GameObjectViewModel o) => _o = o;
        public double X { get => _o.X; set => _o.X = (float)value; }
        public double Y { get => _o.Y; set => _o.Y = (float)value; }
        public double RotZ { get => _o.RotationZ; set => _o.RotationZ = (float)value; }
        public double RotX { get => 0; set { } }   // 2-D overlay: no tilt
        public double RotY { get => 0; set { } }
        public double ScaleX { get => _o.ScaleX; set => _o.ScaleX = (float)value; }
        public double ScaleY { get => _o.ScaleY; set => _o.ScaleY = (float)value; }
        public bool CanRotate => true;
        public bool CanScale => true;
        public bool SupportsTilt => false;   // flat sprite: only Z spin
    }

    /// <summary>Where a part's parent sits on the canvas and how a unit of local
    /// X / Y maps to a canvas vector — enough to place handles and invert a drag.</summary>
    private readonly struct GizmoFrame
    {
        public readonly Point ParentOrigin;
        public readonly Vector Ux;   // canvas vector for +1 local X
        public readonly Vector Uy;   // canvas vector for +1 local Y
        public GizmoFrame(Point o, Vector ux, Vector uy) { ParentOrigin = o; Ux = ux; Uy = uy; }
        public Point OriginFor(double x, double y) => ParentOrigin + x * Ux + y * Uy;
    }

    // Handle geometry, in canvas px (the whole canvas is shown at ~0.45×, so
    // these are generous on purpose).
    private const double GizmoAxisLen = 150, GizmoHead = 34, GizmoHeadW = 22;
    private const double GizmoRing = 128, GizmoSquare = 26, GizmoHit = 30;
    private const double RotDragDegPerPx = 0.4;

    private static readonly Brush GX = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));     // X red
    private static readonly Brush GY = new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x8C));     // Y green
    private static readonly Brush GZBlue = new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF)); // Z blue
    private static readonly Brush GUniform = new SolidColorBrush(Color.FromRgb(0xF2, 0xC0, 0x37)); // yellow

    private readonly Canvas _gizmoLayer = new()
    {
        Width = CanvasWidth,
        Height = CanvasHeight,
        ClipToBounds = false,
    };

    private Border _gizmoToolbar = new();
    private TextBlock _gizmoTitle = new();
    private StackPanel _partRow = new();
    private Button _btnBody = new(), _btnShadow = new(), _btnBlink = new(), _btnWet = new();
    private Button _btnMove = new(), _btnRotate = new(), _btnScale = new();

    // Object menu — the left-side hierarchy of every GameObject + NPC in the
    // place; clicking a row selects that object for the gizmo.
    private Border _objectMenu = new();
    private StackPanel _objectMenuList = new();

    /// <summary>The place's hierarchy as resolved by the last rebuild.</summary>
    private List<SceneEntry> _scene = new();

    // The selection is held as (view-model, stable path). The path survives a
    // rebind (undo/redo replaces every VM instance), so the gizmo re-adopts the
    // same object rather than dropping the selection.
    private NpcPlacementViewModel? _selPlacement;
    private string _selPlacementPath = "";
    private GameObjectViewModel? _selNode;
    private string _selNodePath = "";
    private GizmoPart _selPart = GizmoPart.Body;
    private GizmoMode _gizmoMode = GizmoMode.Move;

    private IGizmoTarget? _gizmoTarget;
    private GizmoFrame _gizmoFrame;

    // Drag state (captured on the gizmo layer).
    private GizmoHandle _activeHandle = GizmoHandle.None;
    private Point _dragStartMouse, _dragOrigin;
    private Vector _dragUx, _dragUy;
    private double _startX, _startY, _startRotZ, _startRotX, _startRotY, _startScaleX, _startScaleY;

    // ── Selection ───────────────────────────────────────────────────────

    private void SelectPlacement(NpcPlacementViewModel pl, string path)
    {
        _selPlacement = pl;
        _selPlacementPath = path;
        _selNode = null;
        UpdateGizmoToolbar();
        RefreshGizmo();
        RefreshObjectMenu();
    }

    /// <summary>Select a GameObject node (sprite object, container, or the forced
    /// NPCs root) — all three edit through the same Move / Rotate / Scale
    /// handles, in whatever frame their parent chain resolves to.</summary>
    private void SelectNode(GameObjectViewModel o, string path)
    {
        _selNode = o;
        _selNodePath = path;
        _selPlacement = null;
        UpdateGizmoToolbar();
        RefreshGizmo();
        RefreshObjectMenu();
    }

    private void Deselect()
    {
        if (_activeHandle != GizmoHandle.None) return;   // don't drop an active drag
        _selPlacement = null;
        _selNode = null;
        _gizmoTarget = null;
        _gizmoLayer.Children.Clear();
        UpdateGizmoToolbar();
        RefreshObjectMenu();
    }

    /// <summary>Resolve the current selection to its editable target + the frame
    /// that places its handles. False if the selection is gone (e.g. removed).
    /// Walks the tree fresh, so it's correct even outside a rebuild.</summary>
    private bool TryResolveSelection(out IGizmoTarget target, out GizmoFrame frame)
    {
        target = null!;
        frame = default;
        if (_selNode == null && _selPlacement == null) return false;

        var scene = BuildScene();

        if (_selNode != null)
        {
            // Re-adopt by path first (stable across the rebind an undo/redo does),
            // falling back to reference identity.
            var entry = scene.FirstOrDefault(e => e.Node != null && e.Path == _selNodePath)
                     ?? scene.FirstOrDefault(e => ReferenceEquals(e.Node, _selNode));
            if (entry?.Node == null) return false;
            _selNode = entry.Node;
            _selNodePath = entry.Path;
            target = new OverlayTarget(entry.Node);
            frame = FrameFromParent(entry.ParentWorld);
            return true;
        }

        var pe = scene.FirstOrDefault(e => e.Npc != null && e.Path == _selPlacementPath)
              ?? scene.FirstOrDefault(e => ReferenceEquals(e.Npc, _selPlacement));
        if (pe?.Npc == null) return false;
        _selPlacement = pe.Npc;
        _selPlacementPath = pe.Path;

        if (_selPart == GizmoPart.Body)
        {
            target = new NpcPartTarget(_selPlacement.Body);
            frame = FrameFromParent(pe.ParentWorld);
        }
        else
        {
            // Shadow / Blink / Wet are children of the body, so their frame is
            // the body's world transform.
            Aff parent = pe.ParentWorld.Then(LeafAff(_selPlacement.Body));
            var vm = _selPart switch
            {
                GizmoPart.Shadow => _selPlacement.Shadow,
                GizmoPart.Blink => _selPlacement.Blink,
                _ => _selPlacement.Wet,
            };
            target = new NpcPartTarget(vm);
            frame = FrameFromParent(parent);
        }
        return true;
    }

    private static GizmoFrame FrameFromParent(in Aff p) => new(
        new Point(WorldOriginX + p.Tx * PixelsPerUnit, WorldOriginY - p.Ty * PixelsPerUnit),
        new Vector(p.A * PixelsPerUnit, -p.B * PixelsPerUnit),
        new Vector(p.C * PixelsPerUnit, -p.D * PixelsPerUnit));

    private bool _deselectScheduled;

    private void RefreshGizmo()
    {
        bool hasSelection = _selPlacement != null || _selNode != null;
        if (hasSelection && TryResolveSelection(out var target, out var frame))
        {
            _gizmoTarget = target;
            _gizmoFrame = frame;
            DrawGizmo();
            return;
        }

        // Nothing resolvable right now: hide the handles but keep the selection.
        _gizmoTarget = null;
        _gizmoLayer.Children.Clear();
        if (!hasSelection) return;

        // A selection is set but unresolved. This is either a transient mid-rebind
        // state (an undo/redo momentarily selects the first place before restoring
        // the real one) or a genuine place switch. Defer the verdict to the end of
        // the dispatcher batch: if a later rebind resolves it we redraw and never
        // deselect; if it's still unresolved, the selection is really gone.
        if (_deselectScheduled) return;
        _deselectScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _deselectScheduled = false;
            if (_selPlacement == null && _selNode == null) return;
            if (TryResolveSelection(out var t, out var f))
            {
                _gizmoTarget = t;
                _gizmoFrame = f;
                DrawGizmo();
            }
            else Deselect();
        }));
    }

    // ── Drawing ─────────────────────────────────────────────────────────

    private void DrawGizmo()
    {
        _gizmoLayer.Children.Clear();
        if (_gizmoTarget == null) return;

        Point o = _gizmoFrame.OriginFor(_gizmoTarget.X, _gizmoTarget.Y);
        double frameAngle = Math.Atan2(_gizmoFrame.Ux.Y, _gizmoFrame.Ux.X) * 180 / Math.PI;

        // Origin pip — always shown so the selection is obvious.
        var pip = new Ellipse { Width = 12, Height = 12, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1 };
        Canvas.SetLeft(pip, o.X - 6); Canvas.SetTop(pip, o.Y - 6);
        _gizmoLayer.Children.Add(pip);

        switch (_gizmoMode)
        {
            case GizmoMode.Move:
                AddArrow(o, _gizmoFrame.Ux, GX, GizmoHandle.MoveX);
                AddArrow(o, _gizmoFrame.Uy, GY, GizmoHandle.MoveY);
                AddSquare(o, GUniform, GizmoHandle.MoveFree, GizmoSquare + 4);
                break;

            case GizmoMode.Rotate when _gizmoTarget.CanRotate:
                AddRing(o, GizmoRing, 1.0, frameAngle, GZBlue, GizmoHandle.RotZ);           // Z: in-plane full circle
                if (_gizmoTarget.SupportsTilt)
                {
                    // Unity convention: X (red) rotates in the Y–Z plane → a VERTICAL
                    // foreshortened ellipse; Y (green) rotates in the X–Z plane → a
                    // HORIZONTAL one. (These were swapped before.) Overlays are flat
                    // sprites, so they only get the Z ring.
                    AddRing(o, GizmoRing * 0.92, 0.30, frameAngle + 90, GX, GizmoHandle.RotX); // X: vertical ellipse
                    AddRing(o, GizmoRing * 0.92, 0.30, frameAngle, GY, GizmoHandle.RotY);      // Y: horizontal ellipse
                }
                break;

            case GizmoMode.Scale when _gizmoTarget.CanScale:
                AddScaleArm(o, _gizmoFrame.Ux, GX, GizmoHandle.ScaleX);
                AddScaleArm(o, _gizmoFrame.Uy, GY, GizmoHandle.ScaleY);
                AddSquare(o, GUniform, GizmoHandle.ScaleUniform, GizmoSquare + 4);
                break;

            default:
                // A mode the target can't do (overlay Rotate/Scale) — show Move.
                AddArrow(o, _gizmoFrame.Ux, GX, GizmoHandle.MoveX);
                AddArrow(o, _gizmoFrame.Uy, GY, GizmoHandle.MoveY);
                AddSquare(o, GUniform, GizmoHandle.MoveFree, GizmoSquare + 4);
                break;
        }
    }

    private void AddArrow(Point o, Vector u, Brush col, GizmoHandle h)
    {
        if (u.Length < 1e-6) return;
        Vector d = u; d.Normalize();
        Point tip = o + d * GizmoAxisLen;
        Vector perp = new(-d.Y, d.X);
        Point b = tip - d * GizmoHead;

        var shaft = new Line { X1 = o.X, Y1 = o.Y, X2 = tip.X, Y2 = tip.Y, Stroke = col, StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round };
        var head = new Polygon { Fill = col, Points = new PointCollection { tip, b + perp * (GizmoHeadW / 2), b - perp * (GizmoHeadW / 2) } };
        var hit = new Line { X1 = o.X, Y1 = o.Y, X2 = tip.X, Y2 = tip.Y, Stroke = Brushes.Transparent, StrokeThickness = GizmoHit };
        _gizmoLayer.Children.Add(shaft);
        _gizmoLayer.Children.Add(head);
        Attach(hit, h); Attach(head, h);
        _gizmoLayer.Children.Add(hit);
    }

    private void AddScaleArm(Point o, Vector u, Brush col, GizmoHandle h)
    {
        if (u.Length < 1e-6) return;
        Vector d = u; d.Normalize();
        Point tip = o + d * GizmoAxisLen;
        var arm = new Line { X1 = o.X, Y1 = o.Y, X2 = tip.X, Y2 = tip.Y, Stroke = col, StrokeThickness = 5 };
        _gizmoLayer.Children.Add(arm);
        AddSquareAt(tip, col, h, GizmoSquare);
    }

    private void AddSquare(Point o, Brush col, GizmoHandle h, double size) => AddSquareAt(o, col, h, size);

    private void AddSquareAt(Point c, Brush col, GizmoHandle h, double size)
    {
        var box = new Rectangle
        {
            Width = size, Height = size, Fill = col, Stroke = Brushes.White, StrokeThickness = 1.5,
        };
        Canvas.SetLeft(box, c.X - size / 2); Canvas.SetTop(box, c.Y - size / 2);
        var hit = new Rectangle { Width = size + GizmoHit, Height = size + GizmoHit, Fill = Brushes.Transparent };
        Canvas.SetLeft(hit, c.X - (size + GizmoHit) / 2); Canvas.SetTop(hit, c.Y - (size + GizmoHit) / 2);
        _gizmoLayer.Children.Add(box);
        Attach(hit, h);
        _gizmoLayer.Children.Add(hit);
    }

    private void AddRing(Point o, double radius, double squash, double angleDeg, Brush col, GizmoHandle h)
    {
        double w = radius * 2, hgt = radius * 2 * squash;
        var ring = new Ellipse { Width = w, Height = hgt, Stroke = col, StrokeThickness = 5, Fill = Brushes.Transparent };
        var hit = new Ellipse { Width = w, Height = hgt, Stroke = Brushes.Transparent, StrokeThickness = GizmoHit, Fill = Brushes.Transparent };
        foreach (var el in new[] { ring, hit })
        {
            Canvas.SetLeft(el, o.X - w / 2); Canvas.SetTop(el, o.Y - hgt / 2);
            el.RenderTransform = new RotateTransform(angleDeg, w / 2, hgt / 2);
        }
        _gizmoLayer.Children.Add(ring);
        Attach(hit, h);
        _gizmoLayer.Children.Add(hit);
    }

    private void Attach(Shape s, GizmoHandle h)
    {
        s.Cursor = Cursors.Hand;
        s.MouseLeftButtonDown += (_, e) => StartHandle(h, e);
    }

    // ── Interaction ─────────────────────────────────────────────────────

    private void StartHandle(GizmoHandle h, MouseButtonEventArgs e)
    {
        if (_gizmoTarget == null) return;
        // Pull keyboard focus here: it commits (and undo-checkpoints) any field
        // that was mid-edit, and means the post-drag Ctrl+Z reaches the app
        // rather than a still-focused text box's own undo.
        Focus();
        _activeHandle = h;
        _dragStartMouse = e.GetPosition(_canvas);
        _dragOrigin = _gizmoFrame.OriginFor(_gizmoTarget.X, _gizmoTarget.Y);
        _dragUx = _gizmoFrame.Ux; _dragUy = _gizmoFrame.Uy;
        _startX = _gizmoTarget.X; _startY = _gizmoTarget.Y;
        _startRotZ = _gizmoTarget.RotZ; _startRotX = _gizmoTarget.RotX; _startRotY = _gizmoTarget.RotY;
        _startScaleX = _gizmoTarget.ScaleX; _startScaleY = _gizmoTarget.ScaleY;
        _gizmoLayer.CaptureMouse();
        e.Handled = true;
    }

    private void OnGizmoMouseMove(object? sender, MouseEventArgs e)
    {
        if (_activeHandle == GizmoHandle.None || _gizmoTarget == null) return;
        var t = _gizmoTarget;
        Point p = e.GetPosition(_canvas);
        Vector dm = p - _dragStartMouse;

        switch (_activeHandle)
        {
            case GizmoHandle.MoveX: t.X = _startX + ProjLocal(dm, _dragUx); break;
            case GizmoHandle.MoveY: t.Y = _startY + ProjLocal(dm, _dragUy); break;
            case GizmoHandle.MoveFree:
                t.X = _startX + ProjLocal(dm, _dragUx);
                t.Y = _startY + ProjLocal(dm, _dragUy);
                break;
            case GizmoHandle.RotZ:
                t.RotZ = _startRotZ + (CanvasAngle(p, _dragOrigin) - CanvasAngle(_dragStartMouse, _dragOrigin));
                break;
            case GizmoHandle.RotX: t.RotX = _startRotX + dm.Y * RotDragDegPerPx; break;
            case GizmoHandle.RotY: t.RotY = _startRotY + dm.X * RotDragDegPerPx; break;
            case GizmoHandle.ScaleX: t.ScaleX = ScaleFrom(_startScaleX, p, _dragUx); break;
            case GizmoHandle.ScaleY: t.ScaleY = ScaleFrom(_startScaleY, p, _dragUy); break;
            case GizmoHandle.ScaleUniform:
                double f = Dist(p, _dragOrigin) / Math.Max(1.0, Dist(_dragStartMouse, _dragOrigin));
                t.ScaleX = _startScaleX * f; t.ScaleY = _startScaleY * f;
                break;
        }
        e.Handled = true;
    }

    private void OnGizmoMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_activeHandle == GizmoHandle.None) return;
        _activeHandle = GizmoHandle.None;
        _gizmoLayer.ReleaseMouseCapture();
        e.Handled = true;
        // Rebuild so the Wet droplets (suppressed mid-drag) come back.
        RebuildOverlayGos();
        // One undo step per completed drag.
        RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this));
    }

    private static double ProjLocal(Vector drag, Vector axis)
    {
        double denom = axis.X * axis.X + axis.Y * axis.Y;
        return denom < 1e-9 ? 0 : (drag.X * axis.X + drag.Y * axis.Y) / denom;
    }

    /// <summary>World-space angle (degrees, CCW) of a canvas point about a canvas
    /// origin — canvas Y is flipped, so this equals the local rotZ frame.</summary>
    private static double CanvasAngle(Point m, Point o)
        => Math.Atan2(-(m.Y - o.Y), m.X - o.X) * 180 / Math.PI;

    private double ScaleFrom(double startScale, Point mouse, Vector axis)
    {
        Vector u = axis; if (u.Length < 1e-6) return startScale; u.Normalize();
        double projNow = (mouse - _dragOrigin).X * u.X + (mouse - _dragOrigin).Y * u.Y;
        double projStart = (_dragStartMouse - _dragOrigin).X * u.X + (_dragStartMouse - _dragOrigin).Y * u.Y;
        if (Math.Abs(projStart) < 1.0) return startScale;
        return startScale * projNow / projStart;
    }

    private static double Dist(Point a, Point b) => (a - b).Length;

    // ── Toolbar ─────────────────────────────────────────────────────────

    private void BuildGizmoToolbar()
    {
        _gizmoTitle = new TextBlock { Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };

        _btnBody = MakeChip("Body", () => SetPart(GizmoPart.Body));
        _btnShadow = MakeChip("Shadow", () => SetPart(GizmoPart.Shadow));
        _btnBlink = MakeChip("Blink", () => SetPart(GizmoPart.Blink));
        _btnWet = MakeChip("Wet", () => SetPart(GizmoPart.Wet));
        _partRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var b in new[] { _btnBody, _btnShadow, _btnBlink, _btnWet }) _partRow.Children.Add(b);

        _btnMove = MakeChip("Move", () => SetMode(GizmoMode.Move));
        _btnRotate = MakeChip("Rotate", () => SetMode(GizmoMode.Rotate));
        _btnScale = MakeChip("Scale", () => SetMode(GizmoMode.Scale));
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { _btnMove, _btnRotate, _btnScale }) modeRow.Children.Add(b);

        var close = MakeChip("✕", Deselect);

        var stack = new StackPanel();
        stack.Children.Add(_gizmoTitle);
        stack.Children.Add(_partRow);
        stack.Children.Add(modeRow);
        stack.Children.Add(close);

        _gizmoToolbar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x22, 0x24, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x5A, 0x60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Right,   // left is the object menu
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Child = stack,
        };
        UpdateGizmoToolbar();
    }

    private static Button MakeChip(string text, System.Action onClick)
    {
        var b = new Button
        {
            // TextBlock content with an explicit light foreground so the app's
            // implicit (theme) TextBlock colour can't turn it dark-on-dark.
            Content = new TextBlock { Text = text, Foreground = ChipText },
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            MinWidth = 0,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void SetPart(GizmoPart part)
    {
        _selPart = part;
        UpdateGizmoToolbar();
        RefreshGizmo();
    }

    private void SetMode(GizmoMode mode)
    {
        if (_gizmoTarget != null && ((mode == GizmoMode.Rotate && !_gizmoTarget.CanRotate)
                                  || (mode == GizmoMode.Scale && !_gizmoTarget.CanScale)))
            return;
        _gizmoMode = mode;
        UpdateGizmoToolbar();
        RefreshGizmo();
    }

    private static readonly Brush ChipOn = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly Brush ChipOff = new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x42));
    // Overlay panels are always dark, so their text is explicitly light — the
    // app's implicit TextBlock style (Theme.Text) would be dark on a light theme.
    private static readonly Brush ChipText = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF6));

    private void UpdateGizmoToolbar()
    {
        if (_selPlacement == null && _selNode == null)
        {
            _gizmoToolbar.Visibility = Visibility.Collapsed;
            return;
        }
        _gizmoToolbar.Visibility = Visibility.Visible;

        bool isNpc = _selPlacement != null;
        _partRow.Visibility = isNpc ? Visibility.Visible : Visibility.Collapsed;
        _gizmoTitle.Text = isNpc
            ? $"NPC: {(string.IsNullOrWhiteSpace(_selPlacement!.Name) ? _selPlacement.Npc : _selPlacement.Name)}"
            : $"GameObject: {_selNodePath}";

        Paint(_btnBody, _selPart == GizmoPart.Body);
        Paint(_btnShadow, _selPart == GizmoPart.Shadow);
        Paint(_btnBlink, _selPart == GizmoPart.Blink);
        Paint(_btnWet, _selPart == GizmoPart.Wet);

        Paint(_btnMove, _gizmoMode == GizmoMode.Move);
        Paint(_btnRotate, _gizmoMode == GizmoMode.Rotate);
        Paint(_btnScale, _gizmoMode == GizmoMode.Scale);
        // NPC parts and overlays both support Move / Rotate / Scale now.
        _btnRotate.IsEnabled = true;
        _btnScale.IsEnabled = true;
    }

    private static void Paint(Button b, bool on)
    {
        b.Background = on ? ChipOn : ChipOff;
        b.Foreground = Brushes.White;
    }

    // ── Object menu (left-side GO list) ─────────────────────────────────

    // Resize limits for the hierarchy panel. Deep trees and long object names
    // both overflow the default, so the panel is draggable rather than fixed.
    private const double MenuMinWidth = 120, MenuMaxWidth = 640;
    private const double MenuMinHeight = 90, MenuMaxHeight = 900;

    private void BuildObjectMenu()
    {
        _objectMenuList = new StackPanel();

        // Grip in the bottom-right corner, over the scroll view rather than
        // beside it, so resizing costs no layout space when unused.
        var grip = new System.Windows.Controls.Primitives.Thumb
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Opacity = 0.75,
            ToolTip = "Drag to resize the hierarchy",
            Template = GripTemplate(),
        };
        grip.DragDelta += (_, e) =>
        {
            _objectMenu.Width = Math.Min(MenuMaxWidth,
                Math.Max(MenuMinWidth, _objectMenu.Width + e.HorizontalChange));
            _objectMenu.Height = Math.Min(MenuMaxHeight,
                Math.Max(MenuMinHeight, _objectMenu.Height + e.VerticalChange));
        };

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _objectMenuList,
        };

        var content = new Grid();
        content.Children.Add(scroller);
        content.Children.Add(grip);

        _objectMenu = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x22, 0x24, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x5A, 0x60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Margin = new Thickness(6),
            Width = 178,
            // An explicit height is what makes the panel scrollable AND
            // vertically resizable; sized-to-content it would just grow.
            Height = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Child = content,
        };
        RefreshObjectMenu();
    }

    /// <summary>Three diagonal ticks, the conventional resize-corner mark.</summary>
    private static ControlTemplate GripTemplate()
    {
        var canvas = new FrameworkElementFactory(typeof(Canvas));
        canvas.SetValue(Canvas.BackgroundProperty, Brushes.Transparent);
        for (int i = 0; i < 3; i++)
        {
            double o = 4 + i * 4;
            var line = new FrameworkElementFactory(typeof(System.Windows.Shapes.Line));
            line.SetValue(System.Windows.Shapes.Line.X1Property, 14.0);
            line.SetValue(System.Windows.Shapes.Line.Y1Property, o);
            line.SetValue(System.Windows.Shapes.Line.X2Property, o);
            line.SetValue(System.Windows.Shapes.Line.Y2Property, 14.0);
            line.SetValue(System.Windows.Shapes.Shape.StrokeProperty, ChipText);
            line.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.0);
            canvas.AppendChild(line);
        }
        return new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) { VisualTree = canvas };
    }

    /// <summary>Rebuild the left-side hierarchy: every GameObject and NPC in the
    /// place, in tree order and indented by depth, highlighting the current gizmo
    /// selection. Clicking a row selects that object.</summary>
    private void RefreshObjectMenu()
    {
        _objectMenuList.Children.Clear();

        var scene = _scene.Count > 0 ? _scene : BuildScene();
        _objectMenu.Visibility = scene.Count > 0 && _placeholder.Visibility != Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        if (scene.Count == 0) return;

        _objectMenuList.Children.Add(new TextBlock
        {
            Text = "Hierarchy", FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Brushes.White,
            Margin = new Thickness(2, 0, 0, 3),
        });

        foreach (var entry in scene)
        {
            if (entry.Node is GameObjectViewModel node)
            {
                // ◈ the forced NPCs root, ▦ a sprite object, ⌗ a bare container.
                string icon = node.IsNpcRoot ? "◈" : (string.IsNullOrWhiteSpace(node.Sprite) ? "⌗" : "▦");
                var n = node; var p = entry.Path;
                _objectMenuList.Children.Add(MenuRow(
                    icon + "  " + node.Display, entry.Depth,
                    ReferenceEquals(_selNode, n),
                    () => SelectNode(n, p)));
            }
            else if (entry.Npc is NpcPlacementViewModel pl)
            {
                var q = pl; var p = entry.Path;
                _objectMenuList.Children.Add(MenuRow(
                    "☺  " + pl.Display, entry.Depth,
                    ReferenceEquals(_selPlacement, q),
                    () => SelectPlacement(q, p)));
            }
        }
    }

    /// <summary>One clickable hierarchy row, indented by its depth in the tree.
    /// The indent is real padding rather than leading spaces so it survives
    /// trimming and stays true in a proportional font — the row has to read as
    /// a tree or there's no telling whose child is whose.</summary>
    private Button MenuRow(string text, int depth, bool selected, System.Action onClick)
    {
        var b = new Button
        {
            // Explicit light foreground on the TextBlock: the app's implicit
            // TextBlock style would otherwise paint it Theme.Text (dark on a light
            // theme) onto this always-dark panel → invisible.
            Content = new TextBlock { Text = text, Foreground = ChipText, TextTrimming = TextTrimming.CharacterEllipsis },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6 + depth * 12, 2, 6, 2),
            Margin = new Thickness(0, 1, 0, 0),
            FontSize = 11,
            Background = selected ? ChipOn : ChipOff,
            Foreground = ChipText,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        b.Click += (_, _) => onClick();
        return b;
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
