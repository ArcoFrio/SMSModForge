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
    // ── Layout constants ────────────────────────────────────────────────
    //
    // Measured out of the game's own scene by the UI extractor, not eyeballed
    // from screenshots. Every number below has a source; where one is derived,
    // the derivation is written out so it can be rechecked against a future
    // extraction rather than re-guessed.

    /// <summary>The UI's authoring resolution — every CanvasScaler in
    /// CoreGameScene uses 1920×1080 with referencePixelsPerUnit 100. The old
    /// 2048×1136 was a guess from a screenshot, and being close is what made
    /// the drift so hard to spot.</summary>
    private const double CanvasWidth = 1920;
    private const double CanvasHeight = 1080;

    /// <summary>Navigator button size: the buttons are 125×75, not square.</summary>
    private const double ButtonWidth = 125;
    private const double ButtonHeight = 75;

    /// <summary>Centre-to-centre pitch. MapButtons is a HorizontalLayoutGroup
    /// with spacing 15, so the pitch is the button width plus that gap — the
    /// buttons do NOT sit edge to edge.</summary>
    private const double ButtonPitch = ButtonWidth + 15;

    /// <summary>Columns before the strip wraps. Vanilla lays out one unbroken
    /// row; the wrap is ModForge's own extension for packs that add more
    /// buttons than fit, so this is our number rather than the game's.</summary>
    private const int Columns = 6;

    /// <summary>Row pitch when buttons wrap to a second row — the button height
    /// plus the same 15 the horizontal layout uses, so wrapped rows are spaced
    /// like the strip rather than overlapping.</summary>
    private const double VerticalPitch = ButtonHeight + 15;

    /// <summary>
    /// Vertical centre of the navigator row, from the canvas top.
    /// <para/>
    /// Derived: Navigator anchors bottom-centre at y −18; MapButtons sits +87.2
    /// inside it; each button is −54.305 from MapButtons' top edge (its rect is
    /// 108.61 tall, so the top edge is +54.305). That puts the button centre at
    /// UI y 69.2 above the bottom, i.e. 1080 − 69.2 from the top.
    /// </summary>
    private const double ButtonCenterY = CanvasHeight - 69.2;

    /// <summary>Local (button-space, top-left origin) centre of the order
    /// number. The number sits at +44.8 above the button's centre — far enough
    /// that it OVERHANGS the top edge, which is why a positive in-button offset
    /// never looked right.</summary>
    private const double NumberCenterX = ButtonWidth / 2.0;
    private const double NumberCenterY = ButtonHeight / 2.0 - 44.8;

    /// <summary>The label fills the button and is centred in it.</summary>
    private const double LabelCenterX = ButtonWidth / 2.0;
    private const double LabelCenterY = ButtonHeight / 2.0;

    /// <summary>Max width the label may occupy before wrapping — the button's
    /// own width, which is what its TMP rect is set to.</summary>
    private const double LabelMaxWidth = ButtonWidth;

    /// <summary>Line-spacing multiplier for wrapped label text.</summary>
    private const double LabelLineHeightFactor = 0.5;

    /// <summary>Font sizes as authored: the label is Curse Casual SDF at 24–27
    /// depending on the button, the number Barton SDF at 36.</summary>
    private const double NumberFontSize = 36;
    private const double LabelFontSize = 27;

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
    /// <summary>
    /// World units to canvas pixels.
    /// <para/>
    /// The gameplay cameras are orthographic at size 5.4, so they frame 10.8
    /// world units of height into the UI's 1080 — exactly 100 px per unit. The
    /// old 70.32 was the level SPRITE's import ppu, which says how large the
    /// texture is in world units and nothing about how world units land on
    /// screen. Conflating the two is what put every authored position slightly
    /// out while still looking plausible.
    /// </summary>
    private const double PixelsPerUnit = 100.0;
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
    /// <summary>
    /// Viewport. Clips, so a zoomed-in scene doesn't spill over the editor pane,
    /// and hosts the pan/zoom transform.
    /// <para/>
    /// Replaces a ScrollViewer. Scrollbars are a poor fit for a scene you are
    /// positioning things in: they only reach content inside the extent, they
    /// cannot go past 1:1, and reaching a corner means two separate drags.
    /// Wheel-zoom and drag-pan are what the mask editor already uses, and this
    /// is the same interaction.
    /// </summary>
    private readonly Border _viewport = new()
    {
        ClipToBounds = true,
        Focusable = false,
        ToolTip = "Scroll to zoom (toward the pointer) � middle-drag to pan � double middle-click to reset",
    };

    /// <summary>User zoom on top of the fit-to-width scale. 1 = fit.</summary>
    private double _zoom = 1.0;
    private const double ZoomMin = 0.25, ZoomMax = 8.0;

    /// <summary>Fit-to-width scale, recomputed on resize. The transform applies
    /// this times <see cref="_zoom"/>.</summary>
    private double _fitScale = FixedWidth / CanvasWidth;

    private readonly TranslateTransform _viewPan = new();
    private bool _panning;
    private Point _panStart;
    private double _panStartX, _panStartY;
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
        // Scale then translate: screen = pan + scale x local, which is the form
        // the focal-zoom maths below inverts.
        var view = new TransformGroup();
        view.Children.Add(_extentScale);
        view.Children.Add(_viewPan);
        _extentHost.RenderTransform = view;
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
        _viewport.Child = _extentHost;

        // Clicking empty room area (an unhandled click that bubbles up) clears
        // the gizmo selection; clicks on an NPC / overlay mark themselves handled.
        _canvas.Background = Brushes.Transparent;
        _canvas.MouseLeftButtonDown += (_, e) => { if (!e.Handled) Deselect(); };

        // Gizmo drags capture the (stable) gizmo layer, so a rebuild recreating
        // the handle shapes mid-drag can't drop the capture.
        _gizmoLayer.MouseMove += OnGizmoMouseMove;
        _gizmoLayer.MouseLeftButtonUp += OnGizmoMouseUp;

        // Wheel zooms, middle-drag pans, double-click resets. Bound on the
        // control rather than the canvas so they work over empty room area too.
        MouseWheel += OnViewWheel;
        MouseDown += OnViewMouseDown;
        MouseMove += OnViewMouseMove;
        MouseLeave += OnViewMouseLeave;
        MouseUp += OnViewMouseUp;

        Children.Add(_viewport);
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
        double fit = controlWidth / CanvasWidth;
        if (Math.Abs(fit - _fitScale) < 1e-4) return;
        _fitScale = fit;
        ApplyViewScale();
    }

    /// <summary>Push fit x zoom into the view transform.</summary>
    private void ApplyViewScale()
    {
        double s = _fitScale * _zoom;
        _displayScale = s;
        _extentScale.ScaleX = _extentScale.ScaleY = s;
    }

    // ── Zoom / pan ──────────────────────────────────────────────────────

    /// <summary>
    /// Wheel zoom, anchored on the cursor.
    /// <para/>
    /// The transform is screen = pan + scale x local, so the local point under
    /// the pointer is (screen - pan) / scale. Re-solving for pan at the new
    /// scale with that point pinned keeps whatever is under the cursor exactly
    /// where it is — zooming toward the thing you are looking at instead of
    /// toward the middle and then hunting for it.
    /// </summary>
    private void OnViewWheel(object sender, MouseWheelEventArgs e)
    {
        double sOld = _fitScale * _zoom;
        if (sOld <= 0) return;

        var p = e.GetPosition(this);
        double localX = (p.X - _viewPan.X) / sOld;
        double localY = (p.Y - _viewPan.Y) / sOld;

        // Multiplicative steps: a fixed increment feels coarse when zoomed out
        // and glacial when zoomed in.
        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        _zoom = Math.Clamp(_zoom * factor, ZoomMin, ZoomMax);

        double sNew = _fitScale * _zoom;
        _viewPan.X = p.X - sNew * localX;
        _viewPan.Y = p.Y - sNew * localY;
        ApplyViewScale();
        e.Handled = true;
    }

    private void OnViewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Middle button only: left belongs to selection and the gizmo.
        if (e.ChangedButton != MouseButton.Middle) return;

        // Double middle-click goes home. Without scrollbars there is nothing to
        // show how far the scene has been pushed, so it needs a way back that
        // doesn't involve dragging until something familiar appears.
        if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }

        _panning = true;
        _panStart = e.GetPosition(this);
        _panStartX = _viewPan.X;
        _panStartY = _viewPan.Y;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OnViewMouseMove(object sender, MouseEventArgs e)
    {
        // Drift follows the cursor whenever it is over the preview and not
        // dragging it around; a pan is a camera move, not a look.
        if (ParallaxPreview && !_panning && _activeHandle == GizmoHandle.None)
            ApplyParallax(e.GetPosition(_canvas));

        if (!_panning) return;
        if (e.MiddleButton != MouseButtonState.Pressed) { EndPan(); return; }
        var now = e.GetPosition(this);
        _viewPan.X = _panStartX + (now.X - _panStart.X);
        _viewPan.Y = _panStartY + (now.Y - _panStart.Y);
    }

    /// <summary>Cursor gone: settle back to the neutral, authored position
    /// rather than freezing the scene wherever it happened to be pointing.</summary>
    private void OnViewMouseLeave(object sender, MouseEventArgs e)
    {
        if (ParallaxPreview) ResetParallax();
    }

    private void OnViewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning && e.ChangedButton == MouseButton.Middle) EndPan();
    }

    private void EndPan()
    {
        _panning = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    /// <summary>Back to fit-to-width, unpanned. Panning has no scrollbars to
    /// hint at where the scene went, so there has to be a way home.</summary>
    public void ResetView()
    {
        _zoom = 1.0;
        _viewPan.X = _viewPan.Y = 0;
        ApplyViewScale();
    }

    /// <summary>
    /// Size and centre a level-art layer at its TRUE world size.
    /// <para/>
    /// The art is not screen-sized: Downtown's backdrop is 2048 px at 70.33
    /// px/unit — 29.1 world units — which at 100 px/unit is 2912 canvas pixels
    /// against a 1920-wide viewport. The camera sees the middle of it and the
    /// rest hangs off the sides, which is what makes it parallax. Stretching it
    /// to fill the canvas instead squashed it by about a third horizontally, so
    /// an object authored over a doorway drew somewhere else entirely — the
    /// positional drift that made the preview untrustworthy.
    /// </summary>
    private void SizeLevelLayer(Image img, double spritePpu)
    {
        if (img.Source is not BitmapSource bmp) return;
        double ppu = spritePpu > 0 ? spritePpu : LevelArtPpu;
        // Same WorldPpu the objects use, so art and props stay locked together
        // whatever the level's scale.
        double w = bmp.PixelWidth / ppu * WorldPpu;
        double h = bmp.PixelHeight / ppu * WorldPpu;
        img.Width = w;
        img.Height = h;
        // Centred on the world origin, which is where the camera sits.
        Canvas.SetLeft(img, WorldOriginX - w / 2.0);
        Canvas.SetTop(img, WorldOriginY - h / 2.0);
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

        // Both layers at their true world size, centred on the origin, rather
        // than stretched to the viewport — see SizeLevelLayer.
        SizeLevelLayer(_baseImage, LevelArtPpu);
        SizeLevelLayer(_secondaryImage, LevelArtPpu);

        ApplyOverlay();
        RebuildButtons();
        RebuildOverlayGos();

        _viewport.Visibility = Visibility.Visible;
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
        if (_viewport.Visibility != Visibility.Visible && _placeholder.Visibility == Visibility.Visible)
            return;

        var list = Buttons?.Cast<object>().ToList();
        int n = list?.Count ?? 0;

        // The overlay frame depends on the count, so keep it in sync here too
        // (RebuildButtons runs on add/remove/reorder, Refresh doesn't).
        if (_placeholder.Visibility != Visibility.Visible)
            ApplyOverlay();

        if (n == 0) return;

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

            // A row spans (count-1) pitches plus one button — the gaps sit
            // BETWEEN buttons, so a row of n is narrower than n × pitch. Getting
            // this wrong shifts the whole strip off centre by half a gap.
            double rowWidth = (countInRow - 1) * ButtonPitch + ButtonWidth;
            double startLeft = (CanvasWidth - rowWidth) / 2.0;
            double left = startLeft + colInRow * ButtonPitch;

            // Bottom row at the strip centre; each row above is one
            // VerticalPitch higher.
            double centerY = ButtonCenterY - (bottomRow - row) * VerticalPitch;
            double top = centerY - ButtonHeight / 2.0;

            var slot = new Canvas { Width = ButtonWidth, Height = ButtonHeight };
            Canvas.SetLeft(slot, left);
            Canvas.SetTop(slot, top);

            // Background: "Semi Rounded" at its own 30px corner radius, which is
            // what Unity's nine-slice preserves at any button size.
            var bg = ButtonBackground(ButtonWidth, ButtonHeight, SemiRoundedBorder);
            Canvas.SetLeft(bg, 0);
            Canvas.SetTop(bg, 0);
            slot.Children.Add(bg);

            // The number's disc, tinted as the game tints it.
            AddTintedSprite(slot, "Blockout_CircleSolid.png", NumberDiscColor,
                            NumberCenterX, NumberCenterY, NumberDiscSize, NumberDiscSize);

            // The small rule under the label. Its RectTransform is square while
            // the sprite is 234x34, so it is drawn to the sprite's own aspect
            // rather than squashed to fill.
            AddTintedSprite(slot, "Minus.png", Colors.Black,
                            ButtonWidth / 2.0, ButtonHeight / 2.0 + 24.16,
                            MinusWidth, MinusWidth * MinusRect.Height / (double)MinusRect.Width,
                            MinusRect);

            // Order number (Barton), 1-based, on the disc.
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

    /// <summary>Corner radius of the button background. "Semi Rounded" carries
    /// m_Border 30 on every side, which is the radius Unity's nine-slice
    /// preserves at any button size.</summary>
    private const double SemiRoundedBorder = 30;
    private const double NumberDiscSize = 45;
    private const double MinusWidth = 100;
    private static readonly Color NumberDiscColor = Color.FromRgb(0x5B, 0x2B, 0x57);

    /// <summary>Minus's own rect inside its 256x256 texture, top-left origin —
    /// Unity records it bottom-up as (11, 111, 234, 34).</summary>
    private static readonly Int32Rect MinusRect = new(11, 111, 234, 34);

    /// <summary>
    /// The button background: a rounded rectangle drawn as geometry.
    /// <para/>
    /// This was nine cropped bitmap pieces in a grid, reproducing Unity's Sliced
    /// type. It sliced correctly but showed hairline seams between the pieces,
    /// and they are not fixable in the general case: each piece is stretched
    /// independently and filtered to its own edge, with nothing beyond the crop
    /// to sample, so the joins fade wherever a cell boundary lands off a whole
    /// pixel — which the preview's arbitrary zoom guarantees.
    /// <para/>
    /// "Semi Rounded" is a flat rounded rectangle — the sheet's stroke variants
    /// are the outlined ones — and a nine-slice of a flat rounded rectangle IS a
    /// rounded rectangle with the border as its radius. Drawing it directly is
    /// both seamless and exact, with no resampling at any zoom.
    /// </summary>
    private static FrameworkElement ButtonBackground(double w, double h, double radius)
        => new Rectangle
        {
            Width = w,
            Height = h,
            RadiusX = radius,
            RadiusY = radius,
            Fill = Brushes.White,
        };

    /// <summary>Draw one of the button's decoration sprites, tinted the way the
    /// game tints it, centred on a point in the slot's local space.</summary>
    /// <param name="sourceRect">The sprite's own rect inside its texture, or
    /// empty for the whole thing. A Unity sprite is a REGION of a sheet, not
    /// necessarily the file: Minus is 234x34 at (11, 111) of a 256x256 texture,
    /// and masking with the whole file squeezed the bar into a thirteenth of the
    /// height it should have had.</param>
    private static void AddTintedSprite(Canvas slot, string file, Color tint,
                                        double cx, double cy, double w, double h,
                                        Int32Rect sourceRect = default)
    {
        var src = LoadOverlayFile(file);
        if (src == null) return;
        BitmapSource masked = sourceRect.HasArea
            ? new CroppedBitmap(src, sourceRect)
            : src;
        // Tinting is a fill masked by the sprite — these are flat shapes, so
        // masking is exact rather than an approximation.
        var el = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = new SolidColorBrush(tint),
            OpacityMask = new ImageBrush(masked),
        };
        Canvas.SetLeft(el, cx - w / 2.0);
        Canvas.SetTop(el, cy - h / 2.0);
        slot.Children.Add(el);
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
        _viewport.Visibility = Visibility.Collapsed;
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
    /// <summary>
    /// Fade objects whose PARENT starts inactive. On by default, because the
    /// game draws nothing under a switched-off parent and showing them solid
    /// misrepresents the room — a vanilla level's NPC groups all ship inactive.
    /// Off when you're positioning that content and want to see it properly.
    /// </summary>
    public static readonly DependencyProperty DimUnderInactiveParentProperty =
        DependencyProperty.Register(nameof(DimUnderInactiveParent), typeof(bool), typeof(PlacePreview),
            new PropertyMetadata(true, OnInputChanged));

    public bool DimUnderInactiveParent
    {
        get => (bool)GetValue(DimUnderInactiveParentProperty);
        set => SetValue(DimUnderInactiveParentProperty, value);
    }

    /// <summary>
    /// Fade objects that start inactive themselves. Independent of the parent
    /// rule above: an object parked for a rule to switch on is still yours to
    /// place, so you may want it solid while its switched-off ancestors stay
    /// faint, or the reverse.
    /// </summary>
    public static readonly DependencyProperty DimInactiveProperty =
        DependencyProperty.Register(nameof(DimInactive), typeof(bool), typeof(PlacePreview),
            new PropertyMetadata(true, OnInputChanged));

    public bool DimInactive
    {
        get => (bool)GetValue(DimInactiveProperty);
        set => SetValue(DimInactiveProperty, value);
    }

    /// <summary>Parallax strength of the level's MAIN sprite — the baseline every
    /// object in the place inherits.</summary>
    public static readonly DependencyProperty LevelParallaxProperty =
        DependencyProperty.Register(nameof(LevelParallax), typeof(double), typeof(PlacePreview),
            new PropertyMetadata(0.75, OnInputChanged));

    public double LevelParallax
    {
        get => (double)GetValue(LevelParallaxProperty);
        set => SetValue(LevelParallaxProperty, value);
    }

    /// <summary>Parallax strength of the SECONDARY sprite. Its own drift is on
    /// top of the main sprite's, exactly as the runtime composes them.</summary>
    public static readonly DependencyProperty LevelParallaxSecondaryProperty =
        DependencyProperty.Register(nameof(LevelParallaxSecondary), typeof(double), typeof(PlacePreview),
            new PropertyMetadata(0.75, OnInputChanged));

    public double LevelParallaxSecondary
    {
        get => (double)GetValue(LevelParallaxSecondaryProperty);
        set => SetValue(LevelParallaxSecondaryProperty, value);
    }

    public static readonly DependencyProperty LevelParallaxReversedProperty =
        DependencyProperty.Register(nameof(LevelParallaxReversed), typeof(bool), typeof(PlacePreview),
            new PropertyMetadata(false, OnInputChanged));

    public bool LevelParallaxReversed
    {
        get => (bool)GetValue(LevelParallaxReversedProperty);
        set => SetValue(LevelParallaxReversedProperty, value);
    }

    public static readonly DependencyProperty LevelParallaxSecondaryReversedProperty =
        DependencyProperty.Register(nameof(LevelParallaxSecondaryReversed), typeof(bool), typeof(PlacePreview),
            new PropertyMetadata(false, OnInputChanged));

    public bool LevelParallaxSecondaryReversed
    {
        get => (bool)GetValue(LevelParallaxSecondaryReversedProperty);
        set => SetValue(LevelParallaxSecondaryReversedProperty, value);
    }

    /// <summary>
    /// Track the cursor and drift the scene as the game would. Off by default:
    /// it changes where everything sits, which is the opposite of what you want
    /// while placing something.
    /// <para/>
    /// Toggling only arms the handler — the gains are precomputed on every
    /// rebuild regardless, so turning it on costs nothing and turning it off
    /// puts the scene straight back to its authored position.
    /// </summary>
    public static readonly DependencyProperty ParallaxPreviewProperty =
        DependencyProperty.Register(nameof(ParallaxPreview), typeof(bool), typeof(PlacePreview),
            new PropertyMetadata(false, OnParallaxPreviewChanged));

    public bool ParallaxPreview
    {
        get => (bool)GetValue(ParallaxPreviewProperty);
        set => SetValue(ParallaxPreviewProperty, value);
    }

    private static void OnParallaxPreviewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlacePreview p && !(bool)e.NewValue) p.ResetParallax();
    }

    /// <summary>
    /// Run the real shaders over the preview instead of drawing flat sprites.
    /// Off by default: it is an animated, CPU-side pass, and a still picture is
    /// what you want while positioning things.
    /// <para/>
    /// Toggling rebuilds, because the shader targets are established as the
    /// scene is laid out.
    /// </summary>
    public static readonly DependencyProperty ShadersProperty =
        DependencyProperty.Register(nameof(Shaders), typeof(bool), typeof(PlacePreview),
            new PropertyMetadata(false, OnInputChanged));

    public bool Shaders
    {
        get => (bool)GetValue(ShadersProperty);
        set => SetValue(ShadersProperty, value);
    }

    /// <summary>The place's mask sprite. Drives the level's Milking pass, and is
    /// otherwise unused by the preview — the mask changes nothing about a flat
    /// drawing, which is exactly why it was invisible here until now.</summary>
    public static readonly DependencyProperty MaskSpriteProperty =
        DependencyProperty.Register(nameof(MaskSprite), typeof(string), typeof(PlacePreview),
            new PropertyMetadata("", OnInputChanged));

    public string MaskSprite
    {
        get => (string)GetValue(MaskSpriteProperty);
        set => SetValue(MaskSpriteProperty, value);
    }

    /// <summary>Optional mask for the backdrop. Absent leaves it undisplaced,
    /// which is what vanilla does.</summary>
    public static readonly DependencyProperty SecondaryMaskSpriteProperty =
        DependencyProperty.Register(nameof(SecondaryMaskSprite), typeof(string), typeof(PlacePreview),
            new PropertyMetadata("", OnInputChanged));

    public string SecondaryMaskSprite
    {
        get => (string)GetValue(SecondaryMaskSpriteProperty);
        set => SetValue(SecondaryMaskSpriteProperty, value);
    }

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

    /// <summary>
    /// Pixels-per-unit the level art was imported at — how large the texture is
    /// in WORLD units, which is a different question from how world units map to
    /// the screen (that is <see cref="PixelsPerUnit"/>, a flat 100).
    /// <para/>
    /// Per level, not global: the extraction has it on each level's renderer and
    /// they are not all the same. Defaults to the value the pack-place prototype
    /// uses.
    /// </summary>
    public static readonly DependencyProperty LevelArtPpuProperty =
        DependencyProperty.Register(nameof(LevelArtPpu), typeof(double), typeof(PlacePreview),
            new PropertyMetadata(70.32, OnInputChanged));

    public double LevelArtPpu
    {
        get => (double)GetValue(LevelArtPpuProperty);
        set => SetValue(LevelArtPpuProperty, value);
    }

    /// <summary>
    /// The level root's own scale. Almost every vanilla level ships at 0.79,
    /// and everything authored under it — art, props, NPCs — inherits that.
    /// <para/>
    /// Ignoring it is what made the corrected 100 px/unit mapping read as far
    /// too large: the honest factor is 100 x this. It also reconciles levels
    /// that ship the same scene at different resolutions, since 2048px at
    /// 70.33 ppu and 2912px at 100 ppu are both 29.12 world units.
    /// </summary>
    public static readonly DependencyProperty LevelScaleProperty =
        DependencyProperty.Register(nameof(LevelScale), typeof(double), typeof(PlacePreview),
            new PropertyMetadata(0.79, OnInputChanged));

    public double LevelScale
    {
        get => (double)GetValue(LevelScaleProperty);
        set => SetValue(LevelScaleProperty, value);
    }

    /// <summary>Canvas pixels per world unit as actually seen: the camera's
    /// 100 px/unit through the level root's scale.</summary>
    private double WorldPpu => PixelsPerUnit * (LevelScale > 0 ? LevelScale : 1.0);

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

        _parallaxTargets.Clear();
        _shaderTargets.Clear();

        double baseGain = ParallaxGain(LevelParallax, LevelParallaxReversed, PixelsPerUnit);
        if (_secondaryImage.Source != null)
        {
            drawables.Add((LevelArtSecondaryOrder, 0, _secondaryImage));
            // The backdrop is a CHILD of the level root: it carries the root's
            // drift and adds its own, scaled by the root. That sum is the whole
            // depth effect.
            TrackParallax(_secondaryImage,
                baseGain + ParallaxGain(LevelParallaxSecondary, LevelParallaxSecondaryReversed, WorldPpu));

            // Same restore rule as the base image: it survives the rebuild, so
            // it has to be handed its flat bitmap back when the shader is off.
            bool secShaded = Shaders && !string.IsNullOrWhiteSpace(root)
                && !string.IsNullOrWhiteSpace(SecondarySprite) && !string.IsNullOrWhiteSpace(SecondaryMaskSprite)
                && TrackLevelShader(_secondaryImage, Path.Combine(root, Normalize(SecondarySprite)),
                                    Path.Combine(root, Normalize(SecondaryMaskSprite)));
            if (!secShaded && _secondaryImage.Source is WriteableBitmap && !string.IsNullOrWhiteSpace(root)
                && !string.IsNullOrWhiteSpace(SecondarySprite))
                _secondaryImage.Source = LoadCachedBitmap(Path.Combine(root, Normalize(SecondarySprite)));
        }
        drawables.Add((LevelArtOrder, 0, _baseImage));
        TrackParallax(_baseImage, baseGain);
        // Unlike the NPC images, this one outlives the rebuild, so switching the
        // shader off has to hand its flat bitmap back — otherwise the level
        // stays frozen on the last frame the pass drew.
        bool levelShaded = Shaders && !string.IsNullOrWhiteSpace(root)
            && !string.IsNullOrWhiteSpace(BaseSprite) && !string.IsNullOrWhiteSpace(MaskSprite)
            && TrackLevelShader(_baseImage, Path.Combine(root, Normalize(BaseSprite)),
                                Path.Combine(root, Normalize(MaskSprite)));
        if (!levelShaded && _baseImage.Source is WriteableBitmap && !string.IsNullOrWhiteSpace(root)
            && !string.IsNullOrWhiteSpace(BaseSprite))
            _baseImage.Source = LoadCachedBitmap(Path.Combine(root, Normalize(BaseSprite)));

        if (!string.IsNullOrEmpty(root) && _placeholder.Visibility != Visibility.Visible)
        {
            foreach (var entry in _scene)
            {
                if (entry.Hidden) continue;   // hidden from the preview only
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
                // Each object's OWN sprite ppu — the pack's 70.32 for one it
                // creates, the vanilla sprite's for one it binds to. A constant
                // here drew every vanilla sprite as though it were 100 ppu, so a
                // 512px NPC imported at 51.98 came out at less than half size.
                var m = LeafMatrix(entry.World, w, h, o.SpritePpu);

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
                              (entry.ParentInactive && DimUnderInactiveParent ? 0.15
                               : !o.StartActive && DimInactive ? 0.35
                               : 1.0),
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
                TrackParallax(img, entry.ParallaxGain);

                // Renderer tint, for a vanilla object that has one. Same masked
                // silhouette the NPC reflections use — WPF can't multiply a tint
                // into an Image, and this carries the colour while the pass
                // underneath keeps the art readable.
                if (o.HasTint)
                {
                    var (tr, tg, tb, _) = BustComposer.ParseTint(o.Tint);
                    var tintRect = new Rectangle
                    {
                        Width = w,
                        Height = h,
                        Fill = new SolidColorBrush(Color.FromRgb(
                            (byte)(tr * 255), (byte)(tg * 255), (byte)(tb * 255))),
                        OpacityMask = new ImageBrush(bmp),
                        Opacity = img.Opacity * 0.55,
                        RenderTransform = new MatrixTransform(m),
                        IsHitTestVisible = false,
                    };
                    drawables.Add((o.SortingOrder, 2, tintRect));
                    // Rides with the sprite it colours, or it would smear off it.
                    TrackParallax(tintRect, entry.ParallaxGain);
                }

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
        SyncShaderTimer();
        RefreshGizmo();
        // The object set only changes on structural rebuilds, not per drag frame.
        if (_activeHandle == GizmoHandle.None) RefreshObjectMenu();

        // The canvas stays at the origin. It used to be shifted by the content's
        // negative overhang so a scroll extent could cover it, with the scroll
        // offset put back to compensate — machinery the viewport doesn't need,
        // and which would now fight the pan by moving the scene out from under
        // it on every rebuild. Content outside the level rect is simply reached
        // by panning to it.
        Canvas.SetLeft(_canvas, 0);
        Canvas.SetTop(_canvas, 0);
        _extentHost.Width = CanvasWidth;
        _extentHost.Height = CanvasHeight;
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

        /// <summary>For each ancestor level, whether that ancestor still has
        /// siblings below it — i.e. whether the tree needs a vertical line
        /// carried down through this row at that indent.</summary>
        public bool[] Guides = System.Array.Empty<bool>();

        /// <summary>Last of its siblings, so its elbow closes the branch.</summary>
        public bool IsLast;

        /// <summary>This row's own preview-visibility toggle is switched off.
        /// Purely a viewing preference — see <see cref="_hidden"/>.</summary>
        public bool HiddenBySelf;

        /// <summary>Not drawn: either its own toggle is off, or an ancestor's is.
        /// Hiding follows the tree the way the game's own parenting does, so
        /// switching off a group takes its whole subtree with it.</summary>
        public bool Hidden;

        /// <summary>
        /// Canvas pixels this row travels per unit of cursor offset, summed down
        /// the whole ancestor chain. See <see cref="ParallaxGain"/>.
        /// </summary>
        public double ParallaxGain;
    }

    // ══ Parallax ════════════════════════════════════════════════════════
    //
    // ParallaxMouseEffect, in the game's own words:
    //
    //     v      = camera.ScreenToViewportPoint(mousePosition)   // 0..1
    //     off    = (v.x - 0.5, v.y - 0.5)                        // ±0.5
    //     amount = isUI ? strength * 1000 : strength
    //     sign   = reversed ? +1 : -1
    //     localPosition = start + (sign*off.x*amount, sign*off.y*amount*0.5, 0)
    //
    // Three consequences drive everything below. It writes localPosition, so a
    // child rides its parent's shift AND adds its own — the gains compound down
    // the tree, which is why they are summed during the walk rather than read
    // per object. Vertical travel is HALF horizontal, so a room sweeps sideways
    // far more than it bobs. And the offset saturates at ±0.5, so an object's
    // total excursion is bounded at strength/2 across and strength/4 down,
    // whatever the screen size.
    //
    // Every gain is precomputed here into ONE number per row: canvas pixels per
    // unit of cursor offset. Tracking the cursor is then a multiply and a
    // translate per element, with no rebuild, no re-layout and no re-decode.

    /// <summary>
    /// Canvas pixels an object moves per unit of horizontal cursor offset, for
    /// its own <c>ParallaxMouseEffect</c> alone.
    /// <para/>
    /// The strength is a LOCAL-space distance, so it reaches the canvas through
    /// the scale of everything above it: an object inside the level root is
    /// shrunk by that root's scale, while the level root itself is not.
    /// </summary>
    private static double ParallaxGain(double strength, bool reversed, double chainScale)
        => (reversed ? 1.0 : -1.0) * strength * chainScale;

    /// <summary>Elements that drift, each with the transform it was laid out
    /// with and its gain. Rebuilt with the scene; empty means nothing drifts.</summary>
    private readonly List<(UIElement Element, Matrix Base, double Gain)> _parallaxTargets = new();

    // ══ Shaders ═════════════════════════════════════════════════════════
    //
    // A pack NPC is built with the game's JiggleSprite material and the pack's
    // own uniforms (NpcFactory hands them to BustFactory.ApplyJiggle), and that
    // shader is already ported to the CPU for the bust preview. So a placed NPC
    // can be shown running the SAME code with the SAME numbers it will run with
    // in game — not an impression of it.
    //
    // Cost is governed by the OUTPUT grid, not the source: the pass walks output
    // pixels and samples the pose. Poses are authored large, so the pass renders
    // into a small buffer and lets WPF scale it — an NPC occupies a few hundred
    // pixels of the preview, and paying for 1024² per frame per character to
    // show that would be absurd.

    /// <summary>Longest edge of a shader pass. A placed NPC is drawn far smaller
    /// than its authored pose, and the cost is per output pixel.</summary>
    private const int ShaderMaxEdge = 384;

    /// <summary>Ceiling for the LEVEL pass, which renders at its source
    /// resolution. Base art is 2048×1136, so this is a guard against an
    /// unusually large sprite rather than a reduction in the normal case.</summary>
    private const int LevelShaderMaxEdge = 2048;

    /// <summary>Refresh rate for the animated passes. The jiggle is a slow
    /// wobble — a third of a display refresh reads as continuous and leaves the
    /// UI thread alone for everything else.</summary>
    private static readonly TimeSpan ShaderTick = TimeSpan.FromMilliseconds(33);

    private sealed class ShaderTarget
    {
        public Image Image = null!;
        public byte[] Base = null!, Mask = null!, Output = null!;
        public int SrcW, SrcH, OutW, OutH;
        /// <summary>Set for a bust-family object (NPCs). Null means the level
        /// family — the two use genuinely different shaders.</summary>
        public JiggleParams? Params;
        public MilkingShader.Settings Milking;
        public (float r, float g, float b, float a) Tint;
        public WriteableBitmap Bitmap = null!;
    }

    private readonly List<ShaderTarget> _shaderTargets = new();
    private DispatcherTimer? _shaderTimer;
    private readonly System.Diagnostics.Stopwatch _shaderClock = new();

    /// <summary>
    /// Put an NPC's pose through the jiggle shader, if it has a mask to drive
    /// one. Returns false when there is nothing to animate, leaving the caller's
    /// flat bitmap in place.
    /// </summary>
    private bool TrackShader(Image img, string poseAbs, string maskAbs, JiggleParams p)
    {
        var basePx = LoadBgraAtNative(poseAbs, out int w, out int h);
        if (basePx == null || w <= 0 || h <= 0) return false;
        // The mask has to land on the pose's grid: the CPU port samples both
        // from one array shape, where the GPU would resample in UV space.
        var maskPx = LoadBgraScaled(maskAbs, w, h);
        if (maskPx == null) return false;

        double k = (double)ShaderMaxEdge / Math.Max(w, h);
        int outW = Math.Max(1, (int)Math.Round(w * Math.Min(1.0, k)));
        int outH = Math.Max(1, (int)Math.Round(h * Math.Min(1.0, k)));

        var bmp = new WriteableBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32, null);
        img.Source = bmp;
        var (tr, tg, tb, ta) = BustComposer.ParseTint(p.Tint);

        _shaderTargets.Add(new ShaderTarget
        {
            Image = img, Base = basePx, Mask = maskPx, SrcW = w, SrcH = h,
            OutW = outW, OutH = outH, Output = new byte[outW * 4 * outH],
            Params = p, Tint = (tr, tg, tb, ta), Bitmap = bmp,
        });
        return true;
    }

    /// <summary>
    /// Put the level's base art through the Milking pass its material really
    /// runs, driven by the place's own mask sprite.
    /// <para/>
    /// The base art only — the backdrop keeps a plain material, and so does
    /// every GameObject that hasn't been given a mask of its own.
    /// </summary>
    private bool TrackLevelShader(Image img, string baseAbs, string maskAbs)
    {
        var basePx = LoadBgraAtNative(baseAbs, out int w, out int h);
        if (basePx == null || w <= 0 || h <= 0) return false;
        var maskPx = LoadBgraScaled(maskAbs, w, h);
        if (maskPx == null) return false;

        // Level art renders at its OWN resolution, unlike the NPC passes.
        // Shrinking it destroys the effect twice over: the displacement peaks
        // at about four source pixels, so at a reduced grid it is smaller than
        // one output pixel and aliases away to nothing, and the layer is drawn
        // with NearestNeighbor scaling, so the small buffer then comes back up
        // as hard blocks. Full size costs more per frame, but the wave terms
        // are tabulated per frame rather than evaluated per pixel, which is
        // what makes it affordable.
        double k = Math.Min(1.0, (double)LevelShaderMaxEdge / Math.Max(w, h));
        int outW = Math.Max(1, (int)Math.Round(w * k));
        int outH = Math.Max(1, (int)Math.Round(h * k));

        var bmp = new WriteableBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32, null);
        img.Source = bmp;
        _shaderTargets.Add(new ShaderTarget
        {
            Image = img, Base = basePx, Mask = maskPx, SrcW = w, SrcH = h,
            OutW = outW, OutH = outH, Output = new byte[outW * 4 * outH],
            Params = null, Milking = MilkingShader.Settings.Level, Bitmap = bmp,
        });
        return true;
    }

    /// <summary>Start or stop the animation loop to match what was just built.</summary>
    private void SyncShaderTimer()
    {
        if (_shaderTargets.Count == 0)
        {
            _shaderTimer?.Stop();
            _shaderClock.Reset();
            return;
        }
        if (!_shaderClock.IsRunning) _shaderClock.Restart();
        if (_shaderTimer == null)
        {
            _shaderTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ShaderTick };
            _shaderTimer.Tick += (_, _) => StepShaders();
        }
        _shaderTimer.Interval = ShaderTick;
        _shaderTimer.Start();
        StepShaders();          // draw once now rather than after the first tick
    }

    private void StepShaders()
    {
        // Nothing to show while the control is off screen, and a background
        // tab should not be burning a core on invisible pixels.
        if (!IsVisible) return;
        float t = (float)_shaderClock.Elapsed.TotalSeconds;
        foreach (var s in _shaderTargets)
        {
            if (s.Params != null)
                JiggleShader.Render(s.Base, s.Mask, s.SrcW, s.SrcH, s.Params, s.Tint, t,
                                    s.Output, s.OutW, s.OutH, superSample: false);
            else
                MilkingShader.Render(s.Base, s.Mask, s.SrcW, s.SrcH, s.Milking, t,
                                     s.Output, s.OutW, s.OutH);
            s.Bitmap.WritePixels(new Int32Rect(0, 0, s.OutW, s.OutH), s.Output, s.OutW * 4, 0);
        }
    }

    /// <summary>Register an element to drift, unless it has no reason to.</summary>
    private void TrackParallax(UIElement element, double gain)
    {
        if (Math.Abs(gain) < 1e-6) return;
        var m = (element.RenderTransform as MatrixTransform)?.Matrix ?? Matrix.Identity;
        _parallaxTargets.Add((element, m, gain));
    }

    /// <summary>
    /// Drift everything for a cursor at <paramref name="canvasPoint"/>.
    /// <para/>
    /// The canvas IS the camera's viewport — it is the 1920×1080 the game
    /// renders — so the cursor's fraction across it is exactly the viewport
    /// point the effect reads, and it needs no knowledge of the zoom or pan
    /// (WPF has already mapped the mouse into canvas space). Y is flipped twice
    /// over: once because Unity's viewport counts up from the bottom, once
    /// because the canvas counts down from the top — which cancels, leaving the
    /// vertical term positive against a raw top-down offset.
    /// </summary>
    private void ApplyParallax(Point canvasPoint)
    {
        if (_parallaxTargets.Count == 0) return;
        double offX = canvasPoint.X / CanvasWidth - 0.5;
        double offY = canvasPoint.Y / CanvasHeight - 0.5;

        foreach (var (element, baseM, gain) in _parallaxTargets)
        {
            // Vertical travel is half horizontal — the game's own * 0.5f.
            var m = baseM;
            m.Translate(gain * offX, gain * offY * 0.5);
            element.RenderTransform = new MatrixTransform(m);
        }
    }

    /// <summary>Put every drifting element back where it was authored.</summary>
    private void ResetParallax()
    {
        foreach (var (element, baseM, _) in _parallaxTargets)
            element.RenderTransform = new MatrixTransform(baseM);
    }

    /// <summary>Walk the whole GameObject tree into a flat, tree-ordered list.</summary>
    private List<SceneEntry> BuildScene()
    {
        var entries = new List<SceneEntry>();
        WalkScene(GameObjects, "", 0, Aff.Identity, false, false, new List<bool>(), entries);
        ApplyHiddenFlags(entries);
        ApplyParallaxGains(entries);
        return entries;
    }

    /// <summary>
    /// Sum each row's parallax gain with every enabled ancestor's, in canvas
    /// pixels per unit of cursor offset.
    /// <para/>
    /// Inheritance is the point: the effect writes localPosition, so a child of
    /// a drifting parent is carried by it and then adds its own on top. Every
    /// object in a place is a descendant of the level root, so the level's own
    /// gain is the baseline each row starts from.
    /// <para/>
    /// The scale chain is deliberately approximate at one point: a node's own
    /// X/Y scale is folded in for its DESCENDANTS, which is where Unity applies
    /// it, but rotation between a node and the canvas is not decomposed. Pack
    /// objects are overwhelmingly unrotated, and being a few pixels out on a
    /// rotated group is a far smaller error than ignoring the chain entirely.
    /// </summary>
    private void ApplyParallaxGains(List<SceneEntry> entries)
    {
        // Level root: its localPosition sits in the levels container, above the
        // level's own scale, so it reaches the canvas at the raw camera ppu.
        double baseGain = ParallaxGain(LevelParallax, LevelParallaxReversed, PixelsPerUnit);

        // (depth, gain, childScale) for the ancestors currently open.
        var stack = new List<(int Depth, double Gain, double Scale)>();
        foreach (var e in entries)
        {
            while (stack.Count > 0 && stack[^1].Depth >= e.Depth) stack.RemoveAt(stack.Count - 1);

            double inherited = stack.Count > 0 ? stack[^1].Gain : baseGain;
            // Scale from this row's PARENT down to the canvas. Directly under the
            // level root that is the level's own scale, i.e. WorldPpu.
            double parentScale = stack.Count > 0 ? stack[^1].Scale : WorldPpu;

            double own = 0;
            var node = e.Node;
            if (node != null && node.ParallaxEnabled)
            {
                // isUI is a different space, not just a bigger number. The game
                // multiplies by 1000 because a canvas object's localPosition is
                // in UI units, and the canvas is authored at this very
                // 1920x1080 — so one of those units is one canvas pixel, and
                // the world scale chain does not apply to it at all. Running it
                // through the chain anyway would throw the object several
                // thousand pixels off screen.
                own = node.ParallaxIsUI
                    ? ParallaxGain(node.ParallaxStrength * 1000.0, node.ParallaxReversed, 1.0)
                    : ParallaxGain(node.ParallaxStrength, node.ParallaxReversed, parentScale);
            }

            e.ParallaxGain = inherited + own;

            if (node != null)
            {
                double sx = node.ScaleX == 0 ? 1 : Math.Abs(node.ScaleX);
                stack.Add((e.Depth, e.ParallaxGain, parentScale * sx));
            }
            else
            {
                // An NPC placement carries its parent's drift and hosts children.
                stack.Add((e.Depth, e.ParallaxGain, parentScale));
            }
        }
    }

    /// <summary>
    /// Resolve each row's preview visibility, inheriting down the tree.
    /// <para/>
    /// The list is already in tree order, so a hidden row's subtree is exactly
    /// the following rows deeper than it — the same observation the fold arrows
    /// use, and the reason neither needs a structure kept in step with the tree.
    /// </summary>
    private void ApplyHiddenFlags(List<SceneEntry> entries)
    {
        // Depth of the shallowest hidden row whose subtree we are still inside.
        int cutoff = -1;
        foreach (var e in entries)
        {
            if (cutoff >= 0 && e.Depth <= cutoff) cutoff = -1;   // left that subtree
            e.HiddenBySelf = _hidden.Contains(e.Path);
            e.Hidden = cutoff >= 0 || e.HiddenBySelf;
            if (e.Hidden && cutoff < 0) cutoff = e.Depth;
        }
    }

    /// <summary>
    /// Paths of the rows hidden from the preview.
    /// <para/>
    /// A VIEWING preference and nothing else: it never reaches the pack, and is
    /// deliberately not the same thing as a node's <c>StartActive</c>, which is
    /// authored data the game reads. Hiding a wall to see what is behind it must
    /// not quietly switch that wall off in the room. Keyed by path for the same
    /// reason as <see cref="_collapsed"/> — the menu is rebuilt from scratch on
    /// every refresh, and paths that no longer exist simply never match.
    /// </summary>
    private readonly HashSet<string> _hidden = new();

    private static void WalkScene(IEnumerable? items, string prefix, int depth,
                                  Aff parentWorld, bool inNpcSubtree, bool parentInactive,
                                  List<bool> ancestorHasMore, List<SceneEntry> into)
    {
        if (items == null) return;
        // Materialised so each row knows whether it is its parent's last child —
        // which is what tells an elbow from a tee, and a tee is the only thing
        // that says "the branch continues below this".
        var siblings = items.OfType<GameObjectViewModel>().ToList();
        for (int si = 0; si < siblings.Count; si++)
        {
            var o = siblings[si];
            bool isLast = si == siblings.Count - 1;
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
                Guides = ancestorHasMore.ToArray(), IsLast = isLast,
            });

            // Everything below inherits this node's switched-off state.
            bool childrenInactive = parentInactive || !o.StartActive;

            // A row's children are its NPCs followed by its GameObjects, so the
            // last NPC only closes the branch when no GameObjects follow it.
            ancestorHasMore.Add(!isLast);

            for (int ni = 0; ni < o.Npcs.Count; ni++)
            {
                var pl = o.Npcs[ni];
                bool npcLast = ni == o.Npcs.Count - 1 && o.Children.Count == 0;
                string label = string.IsNullOrWhiteSpace(pl.Name) ? pl.Npc : pl.Name;
                if (string.IsNullOrWhiteSpace(label)) label = "(unset)";
                string npcPath = path + "/" + label;
                into.Add(new SceneEntry
                {
                    Npc = pl, Path = npcPath, Depth = depth + 1,
                    InNpcSubtree = npc, World = world, ParentWorld = world,
                    ParentInactive = childrenInactive,
                    Guides = ancestorHasMore.ToArray(), IsLast = npcLast,
                });
                // GameObjects parented under the NPC ride along with its body,
                // so they compose from the body's world transform.
                ancestorHasMore.Add(!npcLast);
                WalkScene(pl.Children, npcPath, depth + 2,
                          world.Then(LeafAff(pl.Body)), true,
                          childrenInactive || !pl.StartActive, ancestorHasMore, into);
                ancestorHasMore.RemoveAt(ancestorHasMore.Count - 1);
            }

            WalkScene(o.Children, path, depth + 1, world, npc, childrenInactive,
                      ancestorHasMore, into);
            ancestorHasMore.RemoveAt(ancestorHasMore.Count - 1);
        }
    }

    /// <summary>Maps a leaf's world affine + sprite pixel size to the WPF matrix
    /// that places its (0..w, 0..h) pixel rect on the canvas — including the
    /// Unity→canvas Y flip and the level/sprite px-per-unit ratio. Exact for any
    /// rotation / mirror / non-uniform scale (no scale/rotation decomposition).
    /// <paramref name="spritePpu"/> is the sprite's authored pixels-per-unit:
    /// NPC art loads at 100, GameObject sprites at the level's own 70.32 (so
    /// one sprite pixel is one canvas pixel).</summary>
    private Matrix LeafMatrix(in Aff w, double spx, double spy, double spritePpu = NpcSpritePpu)
    {
        // World units land on canvas at the camera's rate THROUGH the level
        // root's scale — everything authored under the level inherits it.
        double ppu = WorldPpu;
        double k = ppu / spritePpu;
        double halfW = spx / (2.0 * spritePpu);
        double halfH = spy / (2.0 * spritePpu);
        // Top-left pixel maps to local unit (-halfW, +halfH).
        double wx = w.A * (-halfW) + w.C * halfH + w.Tx;
        double wy = w.B * (-halfW) + w.D * halfH + w.Ty;
        return new Matrix(
            k * w.A, -k * w.B,      // M11, M12  (x-axis, canvas Y flipped)
            -k * w.C, k * w.D,      // M21, M22  (y-axis)
            ppu * wx + WorldOriginX,
            -ppu * wy + WorldOriginY);
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

        var placements = _scene.Where(e => e.Npc != null && !e.Hidden).ToList();
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
            double ghost = entry.ParentInactive && DimUnderInactiveParent ? 0.15
                         : !pl.StartActive && DimInactive ? 0.4
                         : 1.0;
            // The placement's own order wins — depth belongs to the room.
            int bodyOrder = pl.Model.SortingOrder ?? def.SortingOrder;
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
                TrackParallax(ell, entry.ParallaxGain);
                ExpandBounds(mShadow, circlePx, circlePx, ref minX, ref minY, ref maxX, ref maxY);
            }

            // Body pose. Still by default; with shaders on it runs the same
            // JiggleSprite pass, and the same uniforms, the runtime gives it.
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
                // The shader draws into its own smaller buffer, so the Image
                // keeps the pose's layout size and WPF scales the result — the
                // object stays exactly where and how big it was.
                if (Shaders && !string.IsNullOrWhiteSpace(def.Mask))
                    TrackShader(img, Path.Combine(root, Normalize(def.Sprite)),
                                Path.Combine(root, Normalize(def.Mask)), def.Model.Jiggle);
                var plForClick = pl;
                var plPathForClick = entry.Path;
                img.MouseLeftButtonDown += (_, e) => { SelectPlacement(plForClick, plPathForClick); e.Handled = true; };
                drawables.Add((bodyOrder, NpcBodyTie, img));
                TrackParallax(img, entry.ParallaxGain);
                ExpandBounds(mBody, bmp.PixelWidth, bmp.PixelHeight, ref minX, ref minY, ref maxX, ref maxY);

                // Reflection: the same bitmap again, mirrored on Y about the
                // pose's origin. Costs nothing beyond a second Image sharing the
                // already-cached bitmap — exactly what it costs in game, where
                // it's a child renderer reusing the parent's sprite.
                //
                // Deliberately NOT bounds-expanding: a reflection is a floor
                // effect, and letting it push the auto-fit out would shrink the
                // room to make space for a mirror image of what's already there.
                if (def.ReflectionEnabled)
                {
                    // Mirror about the sprite's FEET, not its centre.
                    //
                    // scale (1,-1) alone flips the pose within its own box, so
                    // the reflection lands exactly on top of the body — which is
                    // why a fixed offset looked like no offset at all on a large
                    // pose. Dropping it a full sprite height puts its top edge at
                    // the body's bottom edge, where a floor reflection starts,
                    // whatever the pose's size. The authored offset is then a
                    // nudge from there rather than the whole placement.
                    double poseHeight = bmp.PixelHeight / NpcSpritePpu;
                    var refl = body.Then(Aff.Trs(0, def.ReflectionOffsetY - poseHeight,
                                                 0, 0, 0, 1, -1));
                    var mRefl = LeafMatrix(refl, bmp.PixelWidth, bmp.PixelHeight);
                    var rimg = new Image
                    {
                        Source = bmp,
                        Width = bmp.PixelWidth,
                        Height = bmp.PixelHeight,
                        Stretch = Stretch.Fill,
                        Opacity = ghost * Math.Clamp(def.ReflectionAlpha, 0.0, 1.0),
                        RenderTransform = new MatrixTransform(mRefl),
                        IsHitTestVisible = false,   // click the body, not its mirror
                        // States the offset the preview ACTUALLY used, so "it
                        // isn't offsetting" can be told from "it is, and that's
                        // what -2.3 local units looks like on this pose".
                        ToolTip = tip + "\n(reflection — Y offset "
                                + def.ReflectionOffsetY.ToString("0.###",
                                      System.Globalization.CultureInfo.InvariantCulture)
                                + ", tint " + (string.IsNullOrWhiteSpace(def.ReflectionTint) ? "none" : def.ReflectionTint)
                                + ")",
                    };
                    RenderOptions.SetBitmapScalingMode(rimg, BitmapScalingMode.HighQuality);
                    drawables.Add((def.ReflectionSortingOrder, NpcBodyTie, rimg));
                    TrackParallax(rimg, entry.ParallaxGain);

                    // Tint pass: the sprite's own silhouette filled with the
                    // reflection colour, laid over the mirrored pose. WPF can't
                    // multiply a tint into an Image, and a shader effect would
                    // cost far more than this is worth — masking a filled
                    // rectangle by the same cached bitmap gets the colour across
                    // while the pass underneath keeps the pose readable.
                    var (tr, tg, tb, _) = BustComposer.ParseTint(def.ReflectionTint);
                    if (tr < 0.99 || tg < 0.99 || tb < 0.99)
                    {
                        var tintRect = new Rectangle
                        {
                            Width = bmp.PixelWidth,
                            Height = bmp.PixelHeight,
                            Fill = new SolidColorBrush(Color.FromRgb(
                                (byte)(tr * 255), (byte)(tg * 255), (byte)(tb * 255))),
                            OpacityMask = new ImageBrush(bmp),
                            Opacity = ghost * Math.Clamp(def.ReflectionAlpha, 0.0, 1.0) * 0.55,
                            RenderTransform = new MatrixTransform(mRefl),
                            IsHitTestVisible = false,
                        };
                        drawables.Add((def.ReflectionSortingOrder, NpcBodyTie, tintRect));
                        TrackParallax(tintRect, entry.ParallaxGain);
                    }
                }
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
                TrackParallax(marker.Host, entry.ParallaxGain);
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

        // The scene list is already in tree order, so a row has children exactly
        // when the next one is deeper, and a collapsed row's subtree is every
        // following row deeper than it — no second structure to keep in step.
        int hiddenBelowDepth = -1;
        for (int i = 0; i < scene.Count; i++)
        {
            var entry = scene[i];

            if (hiddenBelowDepth >= 0)
            {
                if (entry.Depth > hiddenBelowDepth) continue;   // inside a collapsed subtree
                hiddenBelowDepth = -1;
            }

            bool hasChildren = i + 1 < scene.Count && scene[i + 1].Depth > entry.Depth;
            bool collapsed = hasChildren && _collapsed.Contains(entry.Path);
            if (collapsed) hiddenBelowDepth = entry.Depth;

            string rowPath = entry.Path;
            System.Action? toggle = hasChildren
                ? () =>
                  {
                      if (!_collapsed.Remove(rowPath)) _collapsed.Add(rowPath);
                      RefreshObjectMenu();
                  }
                : null;

            // Visibility redraws the scene, not just the menu — unlike folding,
            // which only changes what the list shows.
            System.Action hide = () =>
            {
                if (!_hidden.Remove(rowPath)) _hidden.Add(rowPath);
                RebuildOverlayGos();
            };

            if (entry.Node is GameObjectViewModel node)
            {
                // ◈ the forced NPCs root, ▦ a sprite object, ⌗ a bare container.
                string icon = node.IsNpcRoot ? "◈" : (string.IsNullOrWhiteSpace(node.Sprite) ? "⌗" : "▦");
                var n = node; var p = entry.Path;
                _objectMenuList.Children.Add(MenuRow(
                    icon + "  " + node.Display, entry,
                    ReferenceEquals(_selNode, n),
                    () => SelectNode(n, p), collapsed, toggle, hide));
            }
            else if (entry.Npc is NpcPlacementViewModel pl)
            {
                var q = pl; var p = entry.Path;
                _objectMenuList.Children.Add(MenuRow(
                    "☺  " + pl.Display, entry,
                    ReferenceEquals(_selPlacement, q),
                    () => SelectPlacement(q, p), collapsed, toggle, hide));
            }
        }
    }

    /// <summary>
    /// Paths of the rows whose children are folded away.
    /// <para/>
    /// Keyed by path rather than by view-model reference because the menu is
    /// rebuilt from scratch on every refresh — including mid-drag — and a
    /// collapse that reopened itself every time the scene changed would be
    /// worse than none. Paths that no longer exist simply never match.
    /// </summary>
    private readonly HashSet<string> _collapsed = new();

    /// <summary>
    /// One clickable hierarchy row, prefixed with tree guides.
    /// <para/>
    /// Indentation alone never answered "whose child is this" — depth tells you
    /// how deep a row sits, not which row above it is its parent, and with a
    /// dozen siblings at mixed depths the eye has nothing to follow. The guides
    /// draw the actual branches: a vertical line for every ancestor that still
    /// has siblings below, and a tee or an elbow for the row itself, so a
    /// subtree reads as one connected group and the last child visibly closes it.
    /// <para/>
    /// The guides live in their own monospaced run: box-drawing characters only
    /// line up column to column in a fixed-width font, and the labels stay in
    /// the UI font where they belong.
    /// </summary>
    private Button MenuRow(string text, SceneEntry entry, bool selected, System.Action onClick,
                           bool collapsed = false, System.Action? onToggle = null,
                           System.Action? onHide = null)
    {
        var guides = new System.Text.StringBuilder();
        foreach (bool hasMore in entry.Guides) guides.Append(hasMore ? "│ " : "  ");
        if (entry.Depth > 0) guides.Append(entry.IsLast ? "└─" : "├─");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (guides.Length > 0)
        {
            row.Children.Add(new TextBlock
            {
                Text = guides.ToString(),
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                // Dimmer than the labels: structure should be readable without
                // competing with the names for attention.
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xB0, 0xB6, 0xBE)),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        // Fold arrow, or a spacer of the same width on a leaf so every label
        // starts at the same column regardless.
        var arrow = new TextBlock
        {
            Text = onToggle == null ? "  " : collapsed ? "▸ " : "▾ ",
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            Foreground = ChipText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (onToggle != null)
        {
            arrow.Cursor = Cursors.Hand;
            arrow.ToolTip = "Fold this object's children";
            // Handling the DOWN is what keeps the row's own click from firing:
            // the Button never captures, so folding does not also re-select.
            arrow.MouseLeftButtonDown += (_, e) => { onToggle(); e.Handled = true; };
        }
        row.Children.Add(arrow);

        // Preview visibility. Three states worth telling apart: shown, switched
        // off here, and switched off by an ancestor — the last one still owns its
        // toggle (clicking it does something, just nothing you can see yet), so
        // it reads as off but greyed rather than being disabled outright.
        if (onHide != null)
        {
            var eye = new TextBlock
            {
                Text = entry.Hidden ? "○ " : "◉ ",
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                Foreground = entry.HiddenBySelf ? ChipText
                           : entry.Hidden ? new SolidColorBrush(Color.FromArgb(0x66, 0xB0, 0xB6, 0xBE))
                           : new SolidColorBrush(Color.FromArgb(0xAA, 0xB0, 0xB6, 0xBE)),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = entry.HiddenBySelf ? "Hidden from the preview — click to show. Preview only; the object is untouched."
                        : entry.Hidden ? "Hidden because a parent is hidden. Its own toggle is still on."
                        : "Hide this object and everything under it. Preview only — nothing is saved to the pack.",
            };
            // Same as the fold arrow: swallowing the DOWN stops the row's own
            // click, so toggling visibility does not also change the selection.
            eye.MouseLeftButtonDown += (_, e) => { onHide(); e.Handled = true; };
            row.Children.Add(eye);
        }

        row.Children.Add(new TextBlock
        {
            Text = text,
            // A hidden row's label fades with it, so the tree shows at a glance
            // what is being left out of the picture.
            Foreground = entry.Hidden
                ? new SolidColorBrush(Color.FromArgb(0x77, 0xB0, 0xB6, 0xBE)) : ChipText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var b = new Button
        {
            // Explicit light foreground on the TextBlock: the app's implicit
            // TextBlock style would otherwise paint it Theme.Text (dark on a light
            // theme) onto this always-dark panel → invisible.
            Content = row,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 2, 6, 2),
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

    /// <summary>Decode a PNG to premultiplied BGRA at its own size, which is the
    /// buffer shape the shader port expects.</summary>
    private static byte[]? LoadBgraAtNative(string abs, out int w, out int h)
    {
        w = h = 0;
        var src = LoadCachedBitmap(abs);
        if (src == null) return null;
        w = src.PixelWidth; h = src.PixelHeight;
        return CopyBgra(src, w, h);
    }

    /// <summary>Decode a PNG and resample it onto a given grid — used to put a
    /// jiggle mask on its pose's pixel grid.</summary>
    private static byte[]? LoadBgraScaled(string abs, int w, int h)
    {
        var src = LoadCachedBitmap(abs);
        if (src == null) return null;
        int sw = src.PixelWidth, sh = src.PixelHeight;
        var srcPx = CopyBgra(src, sw, sh);
        if (srcPx == null) return null;
        if (sw == w && sh == h) return srcPx;

        // Resampled here rather than through a TransformedBitmap: the port
        // requires the mask to be EXACTLY the pose's grid, and a scale transform
        // rounds its output dimensions, which lands a pixel out often enough to
        // matter. Nearest-neighbour is also the right filter — a mask carries
        // per-channel amounts, and blending them invents displacements that were
        // never painted.
        var dst = new byte[w * 4 * h];
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Min(sh - 1, (int)((long)y * sh / h));
            int srcRow = sy * sw * 4, dstRow = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(sw - 1, (int)((long)x * sw / w));
                Buffer.BlockCopy(srcPx, srcRow + sx * 4, dst, dstRow + x * 4, 4);
            }
        }
        return dst;
    }

    private static byte[]? CopyBgra(BitmapSource src, int w, int h)
    {
        try
        {
            var conv = src.Format == PixelFormats.Pbgra32
                ? src : new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            var buf = new byte[w * 4 * h];
            conv.CopyPixels(buf, w * 4, 0);
            return buf;
        }
        catch { return null; }
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
    // ButtonNavigator.png is gone: the button is drawn from the game's own
    // "Semi Rounded", nine-sliced, with its disc and rule sprites over it.

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

