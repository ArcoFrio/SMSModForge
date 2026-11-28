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
using SMSModForge.Localization;

namespace SMSModForge.View.Controls;

/// <summary>
/// Compositing preview for the Places tab. Builds the whole scene at the
/// game's native level resolution (2048×1136) inside a <see cref="Viewbox"/>
/// so every pixel coordinate from the layout spec maps 1:1, then the Viewbox
/// scales it down to the on-screen display size. Layers, bottom to top:
/// <list type="number">
///   <item>Back sprite — the layer behind (manifest: secondarySprite).</item>
///   <item>Front sprite — the layer in front (manifest: baseSprite).</item>
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
public sealed partial class PlacePreview : Grid
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

    /// <summary>Where the level's own origin is on the canvas: the camera's
    /// centre, moved by where the level stands from the camera
    /// (<see cref="LevelPositionX"/>, <see cref="LevelPositionY"/>; 1.9.2).
    /// Everything on the level is drawn from it; the camera's screen is not.</summary>
    private double WorldOriginX => CanvasWidth / 2.0 + LevelPositionX * PixelsPerUnit;
    private double WorldOriginY => CanvasHeight / 2.0 - LevelPositionY * PixelsPerUnit;

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

    /// <summary>A game level's video screen, as a still (<see cref="LevelVideoScreen"/>; 1.9.2).</summary>
    private readonly Image _videoImage = NewLayerImage();

    /// <summary>
    /// The level art + every GameObject, z-ordered by sorting order.
    /// <para/>
    /// GameObjects used to live in one canvas pinned in FRONT of the
    /// base image, so an overlay authored behind the level (SecretBeach's sky
    /// / portal / gatekeeper are all negative) previewed in front of it — the
    /// opposite of what the game does. Now the front sprite, the back
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
        // At the picture's top left, whatever its size: centred (WPF's way with a child of a set size), a picture
        // wider than the canvas's 1920 - its corner dragged out, or over the whole screen - had the view 86 pixels
        // right and 48 down of it before the scale, its right and bottom cut off (after 1.10.0).
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
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
        Foreground = PreviewMessage.Ink,
        Background = PreviewMessage.Chip,
        Padding = new Thickness(6, 3, 6, 4),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 13,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8),
    };

    private static BitmapSource? _cachedOverlay;
    private static readonly Dictionary<string, BitmapSource> _buttonSprites = new(StringComparer.Ordinal);
    private static BitmapSource? _cachedOverlayExtended;
    private static FontFamily? _numberFont;
    private static FontFamily? _labelFont;

    public PlacePreview()
    {
        // A tooltip that outlived its owner sits over the one thing an
        // author is trying to look at. See ToolTipDismisser.
        View.ToolTipDismisser.KeepClearOf(this);
        LocText.Bind(_viewport, ToolTipProperty, "preview.place.zoom.tip");
        // Its words are worked out as it draws: drawn again when the language
        // changes, so none is left in the old one.
        LocText.Follow(this, () => { Refresh(); RefreshObjectMenu(); UpdateGizmoToolbar(); });

        // Fill the width the container gives us; the height follows the native
        // aspect (see MeasureOverride) and the on-screen scale is recomputed on
        // resize (SizeChanged), so the preview tracks the window + splitter.
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        // The hierarchy, the picture and what is selected, side by side
        // (the author, 1.8.0: the panels covered the picture's corners).
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FixedWidth) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        _viewport.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        // Scale then translate: screen = pan + scale x local, which is the form
        // the focal-zoom maths below inverts.
        var view = new TransformGroup();
        view.Children.Add(_extentScale);
        view.Children.Add(_viewPan);
        _extentHost.RenderTransform = view;
        _viewport.SizeChanged += (_, e) => UpdateDisplayScale(e.NewSize.Width);
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
        // The game's look over the scene, under the interface (PlacePreview.Look):
        // one of its pixels a screen pixel, so never smoothed.
        RenderOptions.SetBitmapScalingMode(_lookImage, BitmapScalingMode.NearestNeighbor);
        _canvas.Children.Add(_lookImage);
        // An NPC's wet drops while the look is off (PlacePreview.Wet): over the scene, under the interface.
        AddWetLayer();
        // What the player sees of the place: outside it a little darker (PlacePreview.View).
        AddPlayersView();
        // The level's own canvases over the screen (the player's room's buttons): under the interface (PlacePreview.LevelCanvas).
        AddLevelCanvasLayer();
        _canvas.Children.Add(_overlayImage);
        _canvas.Children.Add(_buttonLayer);
        // The place's objects' effects (1.10.0), playing over the picture at each object.
        _canvas.Children.Add(_effectLayer);
        // The lights' suns (1.10.0): over the picture, never in it.
        _canvas.Children.Add(_lightLayer);
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
        // picture rather than the canvas so they work over empty room area too,
        // and not on the control: the lists beside it scroll with the wheel.
        _viewport.MouseWheel += OnViewWheel;
        _viewport.MouseDown += OnViewMouseDown;
        _viewport.MouseMove += OnViewMouseMove;
        _viewport.MouseLeave += OnViewMouseLeave;
        _viewport.MouseUp += OnViewMouseUp;
        // A click on the picture puts the keyboard here, for its keys (PlacePreview.Keys).
        _viewport.PreviewMouseLeftButtonDown += (_, _) => TakeKeyboard();
        // And the click stays the picture's: a click on the room's empty floor, left unhandled, went on up to
        // the page, which took the keyboard for itself - and the keys went with it (PlaceObjectKeysTests).
        _viewport.MouseLeftButtonDown += (_, e) => e.Handled = true;
        KeyDown += OnObjectKeys;

        SetColumn(_viewport, 1);
        SetColumn(_placeholder, 1);
        Children.Add(_viewport);
        Children.Add(_placeholder);
        BuildObjectMenu();
        Children.Add(_objectMenu);
        BuildGizmoToolbar();
        SetColumn(_gizmoToolbar, 2);
        Children.Add(_gizmoToolbar);
        // What the tutorials point at, now that every object's settings are here (after 1.10.0).
        TutorialAnchor.SetId(_objectMenu, "panel:placeHierarchy");
        TutorialAnchor.SetId(_gizmoToolbar, "panel:placeSettings");
        BuildSizeGrip();

        // The wet drops move only while the control is actually visible (not on a
        // hidden/unselected tab).
        FpsBadge.Attach(this, () => FramesShown);
        IsVisibleChanged += (_, _) =>
        {
            SyncWetClock();
            // The shaders' clock too (1.9.0): it ticked on behind another tab or
            // the mask studio, waking the editor 30 times a second to draw
            // nothing. Back on screen, it starts again where the shaders are.
            SyncAnimClock();
            if (!IsVisible) StopShaders();
            else if (_shaderTargets.Count > 0) SyncShaderTimer();
            SyncLook();
            SyncParallaxFollow();
        };

        Refresh();
    }

    /// <summary>Lock the control to the native aspect: take the width the parent
    /// offers and derive the height from 2048×1136, so it fills the editor pane's
    /// width (which grows with the window + sidebar splitter) at preserved
    /// proportions. Width-driven on purpose — the editor is a vertical scroller,
    /// so a taller preview at a wider window just scrolls.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        // The picture as large as its corner was dragged to, the hierarchy and
        // the selection beside it as wide as their longest name - and when
        // that is more than the room, the lists give way first, down to
        // ListMin each, and only then the picture (the author, 1.8.0).
        double room = availableSize.Width;
        bool lists = _objectMenu.Visibility == Visibility.Visible;
        // Over the picture (filling the screen) the panels are given their widths below: measured free of them.
        if (FillsRoom) { _objectMenu.ClearValue(WidthProperty); _gizmoToolbar.ClearValue(WidthProperty); }
        double left = lists ? HierarchyNaturalWidth() : 0;
        double right = lists ? Math.Max(SelectionMinWidth, NaturalSelectionWidth()) : 0;
        bool roomKnown = !double.IsInfinity(room) && !double.IsNaN(room) && room > 0;
        // Not dragged: as large as fits beside the lists at their full width,
        // up to its own size. Dragged: that size, the lists giving way.
        bool tallKnown = !double.IsInfinity(availableSize.Height) && !double.IsNaN(availableSize.Height) && availableSize.Height > 0;
        // Filling the screen: the picture as large as the room both ways, the panels over its sides at their widths
        // (after 1.10.0, the author: "the canvas should span the whole screen, while the left and right menus overlay it").
        if (FillsRoom && roomKnown)
        {
            double whole = Math.Max(PictureMin, Math.Min(room, tallKnown ? availableSize.Height * Aspect : room));
            if (lists)
            {
                // The hierarchy, and under it the page's tick boxes when they are here: one column, as wide as the
                // widest of the two, the hierarchy taking the height the boxes leave (PlacePreview.Panels).
                double column = Math.Max(ListMin, left - 8);
                if (_extrasHost.Visibility == Visibility.Visible)
                {
                    column = Math.Max(column, ExtrasLeast());
                    _extrasHost.Width = column;
                    _extrasHost.Measure(new Size(column, double.PositiveInfinity));
                    _objectMenu.Margin = new Thickness(8, 8, 0, 8 + _extrasHost.DesiredSize.Height + 8);
                }
                else _objectMenu.Margin = new Thickness(8, 8, 0, 8);
                _objectMenu.Width = column;
                _gizmoToolbar.Width = Math.Max(ListMin, right - _settingsMargin.Left - _settingsMargin.Right);
            }
            SetWidth(0, 0);
            SetWidth(1, whole);
            SetWidth(2, 0);
            base.MeasureOverride(new Size(whole, whole / Aspect));
            return new Size(whole, whole / Aspect);
        }
        double picture = !double.IsNaN(_pictureScale) ? FixedWidth * _pictureScale
                       : roomKnown ? Math.Max(PictureMin, Math.Min(FixedWidth, room - left - right))
                       : FixedWidth;
        if (roomKnown && picture + left + right > room)
        {
            double listsNow = left + right;
            // Each list's least is the panel's own, its margin beside it.
            double leftLeast = Math.Min(left, ListMin + _objectMenu.Margin.Left + _objectMenu.Margin.Right);
            double rightLeast = Math.Min(right, ListMin + _gizmoToolbar.Margin.Left + _gizmoToolbar.Margin.Right);
            double listsCan = Math.Max(lists ? leftLeast + rightLeast : 0, room - picture);
            if (listsNow > 0 && listsCan < listsNow)
            {
                // Each gives way by what it has above its least, so neither goes below it: in proportion to
                // their widths, the narrow hierarchy went under it once the selection beside the picture held
                // the picked one's settings and was twice as wide (after 1.10.0).
                double leftCan = Math.Max(0, left - leftLeast), rightCan = Math.Max(0, right - rightLeast);
                if (leftCan + rightCan > 0)
                {
                    double share = Math.Min(1, (listsNow - listsCan) / (leftCan + rightCan));
                    left -= leftCan * share;
                    right -= rightCan * share;
                }
            }
            picture = Math.Max(PictureMin, room - left - right);
        }
        double height = picture / Aspect;
        SetWidth(0, left);
        SetWidth(1, picture);
        SetWidth(2, right);
        base.MeasureOverride(new Size(left + picture + right, height));
        return new Size(left + picture + right, height);
    }

    /// <summary>The narrowest a list beside the picture is squeezed to, and the
    /// narrowest the picture itself goes.</summary>
    private const double ListMin = 90, PictureMin = 240;

    /// <summary>The selection panel's width when there is little in it.</summary>
    private const double SelectionMinWidth = 170;

    private void SetWidth(int column, double width)
    {
        var def = ColumnDefinitions[column];
        if (Math.Abs(def.Width.Value - width) > 0.5 || !def.Width.IsAbsolute) def.Width = new GridLength(width);
    }

    /// <summary>The row of + buttons over the hierarchy, as last made: it wraps at the width the list shows.</summary>
    private FrameworkElement? _addRow;

    /// <summary>
    /// The hierarchy's width with all the room it wants: its widest row - the + buttons counted by their widest one,
    /// since they wrap at the width the list shows. Counted at the width they had wrapped to, the list kept whatever
    /// width it last had: widened to fit the tick boxes under it over the whole screen, it stayed that wide back on the
    /// page (after 1.10.0, PlacePreviewFullScreenTests).
    /// </summary>
    private double HierarchyNaturalWidth()
    {
        double widest = 0;
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (UIElement row in _objectMenuList.Children)
        {
            if (ReferenceEquals(row, _addRow) && row is Panel buttons)
            {
                foreach (UIElement b in buttons.Children)
                {
                    b.Measure(infinite);
                    widest = Math.Max(widest, b.DesiredSize.Width + buttons.Margin.Left + buttons.Margin.Right);
                }
                continue;
            }
            row.Measure(infinite);
            widest = Math.Max(widest, row.DesiredSize.Width);
        }
        var m = _objectMenu.Margin; var pad = _objectMenu.Padding; var border = _objectMenu.BorderThickness;
        return widest + pad.Left + pad.Right + border.Left + border.Right + m.Left + m.Right;
    }

    /// <summary>What a panel would take with all the room it wants: its
    /// longest row, unwrapped.</summary>
    private static double NaturalWidth(FrameworkElement panel)
    {
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return panel.DesiredSize.Width;
    }

    /// <summary>The selection panel's rows of buttons, whatever is shown in it
    /// now - so it does not change width as objects are picked.</summary>
    private double NaturalSelectionWidth()
    {
        // Worked out from the words rather than measured: a chip that is
        // hidden measures nothing, and every chip counts.
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double Chip(Button b)
        {
            string words = (b.Content as TextBlock)?.Text ?? "";
            var text = new FormattedText(words, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                         new Typeface(b.FontFamily, b.FontStyle, b.FontWeight, b.FontStretch), b.FontSize, Brushes.White, dip);
            return text.WidthIncludingTrailingWhitespace + b.Padding.Left + b.Padding.Right + b.Margin.Left + b.Margin.Right + 2;
        }
        // Its rows wrap: as wide as its widest button, or as the three ways
        // of moving side by side, which read best on one line.
        double widest = Math.Max(_partRow.Children.OfType<Button>().Select(Chip).DefaultIfEmpty().Max(),
                                 _modeRow.Children.OfType<Button>().Sum(Chip));
        // An object's parts, side by side as well (PlacePreview.ObjectParts).
        widest = Math.Max(widest, _objPartRow.Children.OfType<Button>().Sum(Chip));
        // And as wide as the picked one's settings are laid out (after 1.10.0), picked or not:
        // picking something must not move the picture.
        widest = Math.Max(widest, SettingsWidth);
        // The panel's padding, border and margin, and a scrollbar's room.
        return widest + 12 + 2 + 6 + SystemParameters.VerticalScrollBarWidth;
    }

    // ── The picture's size, from its corner (the author, 1.8.0) ─────────────

    /// <summary>How large the picture is drawn, 1 being <see cref="FixedWidth"/>;
    /// not a number until it is dragged, when it fits beside its lists.
    /// Remembered between sessions, for both kinds of place.</summary>
    private double _pictureScale = Services.EditorPrefs.GetPreviewScale(SizeKey, double.NaN);

    private const string SizeKey = "place";

    /// <summary>
    /// The picture as large as the room it is given, wide and tall, its corner's size set aside: while the preview is
    /// over the whole screen (MainWindow.FullScreen, after 1.10.0: "can we also add a fullscreen button for the Places
    /// preview?"). Its corner is not there to drag meanwhile.
    /// </summary>
    public bool FillsRoom
    {
        get => _fillsRoom;
        set
        {
            if (_fillsRoom == value) return;
            _fillsRoom = value;
            if (_sizeGrip != null) _sizeGrip.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            PanelsOver(value);
        }
    }

    private bool _fillsRoom;
    private System.Windows.Controls.Primitives.Thumb? _sizeGrip;

    private void BuildSizeGrip()
    {
        var grip = _sizeGrip = new System.Windows.Controls.Primitives.Thumb
        {
            Width = PreviewFrame.GripSize, Height = PreviewFrame.GripSize,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Template = PreviewFrame.GripTemplate(),
            Focusable = false,
        };
        LocText.Bind(grip, ToolTipProperty, "preview.resize.tip");
        SetColumn(grip, 1);
        SetZIndex(grip, 20);
        grip.DragDelta += (_, e) =>
        {
            double w = Math.Max(1, _viewport.ActualWidth), h = Math.Max(1, _viewport.ActualHeight);
            double along = (e.HorizontalChange * w + e.VerticalChange * h) / (w * w + h * h);
            double shown = w / FixedWidth;
            _pictureScale = Math.Round(Math.Clamp(shown * (1 + along), 0.4, 3), 3);
            InvalidateMeasure();
        };
        grip.DragCompleted += (_, _) => Services.EditorPrefs.SetPreviewScale(SizeKey, _pictureScale);
        grip.MouseDoubleClick += (_, e) =>
        {
            _pictureScale = double.NaN;
            Services.EditorPrefs.SetPreviewScale(SizeKey, double.NaN);
            InvalidateMeasure();
            e.Handled = true;
        };
        Children.Add(grip);
    }

    /// <summary>Recompute the canvas→display scale from the live width so the
    /// whole scene (level art, NPCs, gizmo, water) tracks the window + splitter.</summary>
    private void UpdateDisplayScale(double controlWidth)
    {
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
        SizePlayersView();
        SharpenWhenEnlarged();
        // A new view: the game's look worked out for it at once, and the NPCs'
        // pictures sized for it (TrackShader) once the zoom settles.
        LookAgain();
        ScheduleShaderResize();
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
        ZoomAt(e.GetPosition(_viewport), e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    /// <summary>The wheel's zoom: <paramref name="steps"/> notches in (or out, below 0) at <paramref name="p"/> of the
    /// picture - what the wheel runs, and tests with it.</summary>
    internal void ZoomAt(Point p, int steps)
    {
        double sOld = _fitScale * _zoom;
        if (sOld <= 0 || steps == 0) return;
        double localX = (p.X - _viewPan.X) / sOld;
        double localY = (p.Y - _viewPan.Y) / sOld;

        // Multiplicative steps: a fixed increment feels coarse when zoomed out
        // and glacial when zoomed in.
        double factor = Math.Pow(1.15, steps);
        _zoom = Math.Clamp(_zoom * factor, ZoomMin, ZoomMax);

        double sNew = _fitScale * _zoom;
        _viewPan.X = p.X - sNew * localX;
        _viewPan.Y = p.Y - sNew * localY;
        ApplyViewScale();
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
        _panStart = e.GetPosition(_viewport);
        _panStartX = _viewPan.X;
        _panStartY = _viewPan.Y;
        _viewport.CaptureMouse();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OnViewMouseMove(object sender, MouseEventArgs e)
    {
        // The drift follows the cursor wherever it is on the screen (PlacePreview.Parallax.cs), not only here.
        if (!_panning) return;
        if (e.MiddleButton != MouseButtonState.Pressed) { EndPan(); return; }
        var now = e.GetPosition(_viewport);
        _viewPan.X = _panStartX + (now.X - _panStart.X);
        _viewPan.Y = _panStartY + (now.Y - _panStart.Y);
        LookAgain();
    }

    /// <summary>The cursor off the preview: nothing to do - the drift goes on following it over the rest of the
    /// screen (1.10.0; it used to settle back here, the preview being all it was read over).</summary>
    private void OnViewMouseLeave(object sender, MouseEventArgs e)
    {
    }

    private void OnViewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning && e.ChangedButton == MouseButton.Middle) EndPan();
    }

    private void EndPan()
    {
        _panning = false;
        _viewport.ReleaseMouseCapture();
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
        double pivotX = 0.5, pivotY = 0.5;

        if (LevelArtIsTheGames)
        {
            // The game's own art, as the game draws it: its pixels (a shipped
            // thumbnail is back at its own size by now, VanillaArtSizes) over
            // its own px-per-unit, placed by its pivot (1.9.2). It was fitted
            // to the level frame below as well, on top of its own ppu - scaled
            // twice: 1% small for most levels, and to 70% for the seven drawn
            // from 2912x1632 art at 100 a unit (the PC's room, the Kitchen),
            // which came out framed in black.
            pivotX = LevelArtPivotX;
            pivotY = LevelArtPivotY;
        }
        else
        {
            // A pack's layer, authored at whatever resolution, is fitted to the
            // level frame, exactly as the runtime fits it (centred), so the room
            // fills the same screen either way. Without this the preview scaled
            // with the file: art at twice the resolution drew twice the size
            // here and the same size in game. Art at 2048x1136 comes through
            // untouched, the factor being 1.
            ppu *= Rendering.ArtFit.LevelScale(bmp.PixelWidth, bmp.PixelHeight);
        }
        // Same WorldPpu the objects use, so art and props stay locked together
        // whatever the level's scale.
        double w = bmp.PixelWidth / ppu * WorldPpu;
        double h = bmp.PixelHeight / ppu * WorldPpu;
        img.Width = w;
        img.Height = h;
        // Pivoted on the world origin, which is where the camera sits: the
        // pivot is a share of the art from its bottom-left (Unity's), the
        // centre for all but two of the game's levels.
        Canvas.SetLeft(img, WorldOriginX - w * pivotX);
        Canvas.SetTop(img, WorldOriginY - h * (1.0 - pivotY));
    }

    /// <summary>
    /// A game level's video screen, drawn as its still where the game shows the
    /// video (1.9.2): a camera-space canvas's Image, sized in the canvas's units
    /// (its reference resolution over the screen), fixed to the camera - it does
    /// not move with the level - and sorted with the level's art (behind it, at
    /// -25). The Inside Car's windows and the Subway Train's were black.
    /// False when the level has none, or its still is not there.
    /// </summary>
    private bool PlaceVideoStill(string root)
    {
        var screen = LevelVideoScreen;
        string? path = string.IsNullOrWhiteSpace(LevelVideoStill) ? null : Path.Combine(root ?? "", Normalize(LevelVideoStill));
        var bmp = screen == null || path == null || !File.Exists(path) ? null : LoadCachedBitmap(path);
        _videoImage.Source = bmp;
        if (bmp == null || screen == null) return false;
        double k = screen.ReferenceWidth > 0 ? CanvasWidth / screen.ReferenceWidth : 1.0;
        double w = screen.Width * screen.Scale * k, h = screen.Height * screen.Scale * k;
        _videoImage.Width = w;
        _videoImage.Height = h;
        _videoImage.Stretch = Stretch.Fill;
        Canvas.SetLeft(_videoImage, CanvasWidth / 2.0 + screen.X * k - w / 2.0);
        Canvas.SetTop(_videoImage, CanvasHeight / 2.0 - screen.Y * k - h / 2.0);
        return true;
    }

    /// <summary>Whether any object in the tree is bound to one of the game's own with art (a vanilla level's).</summary>
    private static bool HasVanillaArt(IEnumerable? nodes)
    {
        if (nodes == null) return false;
        foreach (var n in nodes)
            if (n is GameObjectViewModel g
                && (!string.IsNullOrWhiteSpace(g.Model.VanillaArtPath) || HasVanillaArt(g.Children)))
                return true;
        return false;
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
        // (and where one goes, which a button without words says).
        if (e.PropertyName is nameof(NavigatorButtonViewModel.Label) or nameof(NavigatorButtonViewModel.Target) || string.IsNullOrEmpty(e.PropertyName))
            RebuildButtons();
    }

    // ── Composition ─────────────────────────────────────────────────────

    private void Refresh()
    {
        string root = PackRoot ?? "";

        // Spelled out because the names cross over: BaseSprite is the FRONT
        // layer (-10) and SecondarySprite the BACK one (-12). That is the
        // manifest's naming, not the tab's.
        string frontPath = BaseSprite ?? "";
        string backPath = SecondarySprite ?? "";

        // A level can be all objects: the game's Bath has no art of its own, its room is its second child (1.9.2).
        bool objectsOnly = string.IsNullOrWhiteSpace(frontPath) && string.IsNullOrWhiteSpace(backPath)
                           && HasVanillaArt(GameObjects);
        if (string.IsNullOrWhiteSpace(frontPath) && string.IsNullOrWhiteSpace(backPath) && !objectsOnly)
        {
            ShowPlaceholder(string.IsNullOrEmpty(root)
                ? Loc.T("preview.place.saveAndSet")
                : Loc.T("preview.place.noSprites"));
            return;
        }
        if (string.IsNullOrEmpty(root))
        {
            ShowPlaceholder(Loc.T("preview.saveFirst"));
            return;
        }

        // Either layer alone is a valid place, so an unset path is not a
        // failure — it just means that layer draws nothing. This used to
        // require the front layer and report "no base sprite" for a place
        // that had perfectly good back art. A path that IS set but resolves
        // nowhere is still reported, because that is a typo, not a choice.
        string? absFront = string.IsNullOrWhiteSpace(frontPath)
            ? null : Path.Combine(root, Normalize(frontPath));
        string? absBack = string.IsNullOrWhiteSpace(backPath)
            ? null : Path.Combine(root, Normalize(backPath));

        _secondaryImage.Source = absBack != null && File.Exists(absBack) ? LoadArt(absBack) : null;
        _secondaryImage.Visibility = _secondaryImage.Source != null ? Visibility.Visible : Visibility.Collapsed;

        _baseImage.Source = absFront != null && File.Exists(absFront) ? LoadArt(absFront) : null;
        _baseImage.Visibility = _baseImage.Source != null ? Visibility.Visible : Visibility.Collapsed;

        if (_baseImage.Source == null && _secondaryImage.Source == null && !objectsOnly)
        {
            // Every path that was given failed to load, so name one.
            ShowPlaceholder(absFront != null && !File.Exists(absFront)
                ? Loc.F("preview.place.frontMissing", "path", absFront)
                : Loc.F("preview.place.backMissing", "path", absBack));
            return;
        }

        // Both layers at their true world size, centred on the origin, rather
        // than stretched to the viewport — see SizeLevelLayer.
        SizeLevelLayer(_baseImage, LevelArtPpu);
        SizeLevelLayer(_secondaryImage, LevelArtPpu);

        ApplyOverlay();
        RebuildButtons();
        RebuildOverlayGos();

        _viewport.Visibility = Visibility.Visible;
        _placeholder.Visibility = Visibility.Collapsed;
        LookAgain();
        SyncLook();
    }

    /// <summary>Chooses the standard or extended HUD frame based on the
    /// current navigator-button count.</summary>
    private void ApplyOverlay()
    {
        // The game's own interface, drawn with the place's buttons in it (PlacePreview.Hud); the old picture only
        // without the game's extracted interface.
        if (ShowGameHud()) return;
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

        // The buttons are in the game's interface as drawn.
        if (HudDrawn) return;
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
        // The editor's own button sprites, read once (1.9.0): they were decoded
        // again for every button of every redraw - each label keystroke. They
        // ship with the editor and never change; one that would not load is
        // tried again next time, as before.
        if (!_buttonSprites.TryGetValue(file, out var src))
        {
            src = LoadOverlayFile(file);
            if (src != null) _buttonSprites[file] = src;
        }
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
        _videoImage.Source = null;
        _buttonLayer.Children.Clear();
        SeeThroughUndo();
        _worldLayer.Children.Clear();
        _viewport.Visibility = Visibility.Collapsed;
        _placeholder.Text = message;
        _placeholder.Visibility = Visibility.Visible;
        SyncLook();
        // The hierarchy and the picked one's settings stay: a place's objects are made and set there, art or not.
        RefreshObjectMenu();
    }

    // ── GameObjects layer ─────────────────────────────────────────

    private static void OnGameObjectsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (PlacePreview)d;
        // Another place shown, or another pack: what was picked in the one before is not to be picked in it.
        if (!self._undoing) self._lostNodePath = self._lostPlacementPath = null;
        if (e.OldValue is INotifyCollectionChanged oldCc)
            oldCc.CollectionChanged -= self.OnOverlayTreeChanged;
        if (e.NewValue is INotifyCollectionChanged newCc)
            newCc.CollectionChanged += self.OnOverlayTreeChanged;
        self.RebuildOverlayGos();
        // Without level art, whether there is anything to draw is the objects' to say (a level that is all objects).
        if (string.IsNullOrWhiteSpace(self.BaseSprite) && string.IsNullOrWhiteSpace(self.SecondarySprite))
            self.Refresh();
    }

    private void OnOverlayTreeChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRebuild();
    private void OnOverlayItemChanged(object? sender, PropertyChangedEventArgs e) => QueueRebuild();

    private bool _rebuildQueued;

    /// <summary>
    /// Something in the tree changed: the scene is built again once, as soon as whatever is changing it has
    /// finished (1.10.0) - before anything is drawn and before the next mouse move is looked at. Until then every
    /// notification rebuilt the whole scene on the spot, and one change is several: an object's X says "X" and
    /// then "differs from the game's", so dragging one across the place rebuilt it four times for every move of
    /// the mouse (measured: 42 ms a rebuild with the shaders on, some 170 ms a move).
    /// </summary>
    private void QueueRebuild()
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(FlushRebuild));
    }

    /// <summary>The rebuild that is waiting, now: for whatever is about to read the scene.</summary>
    private void FlushRebuild()
    {
        if (_rebuildQueued) RebuildOverlayGos();
        // Laid out before anything reads the world layer: a picture the rebuild has just put there has no size until
        // the next layout pass, and the look took it as nothing - then, the scene's list being the same on the next
        // tick, kept that picture. An object of the pack's own was missing from the lit preview after any change to
        // it until something forced the look to start again (found by the tutorials' pictures, 1.10.0). Nothing to
        // do when nothing has changed.
        _worldLayer.UpdateLayout();
    }

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

    /// <summary>A row's components, their settings and the game's ones' named values: what they do is played in the
    /// preview, so the preview is drawn again as they are edited (after 1.10.0: "Including live preview when changing
    /// parameters").</summary>
    private void SubscribeComponents(System.Collections.ObjectModel.ObservableCollection<ComponentRowViewModel> components)
    {
        components.CollectionChanged += OnOverlayTreeChanged;
        _overlaySubscriptions.Add(components);
        foreach (var c in components)
        {
            c.PropertyChanged += OnOverlayItemChanged;
            _overlaySubscriptions.Add(c);
            c.ParamRows.CollectionChanged += OnOverlayTreeChanged;
            _overlaySubscriptions.Add(c.ParamRows);
            foreach (var row in c.ParamRows)
            {
                row.PropertyChanged += OnOverlayItemChanged;
                _overlaySubscriptions.Add(row);
            }
        }
    }

    private void SubscribeOverlayTree(IEnumerable? items)
    {
        if (items == null) return;
        foreach (var it in items)
        {
            if (it is not GameObjectViewModel o) continue;
            o.PropertyChanged += OnOverlayItemChanged;
            _overlaySubscriptions.Add(o);
            SubscribeComponents(o.Components);
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
                SubscribeComponents(pl.Components);
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

    /// <summary>
    /// The place as the game shows it on arrival (1.9.2, the place pictures export): what starts switched off, sits
    /// under a switched-off parent or is shown only when its conditions hold (an NPC waiting for its character to be
    /// there, an outfit's copy of one) is left out, not drawn faint - the editing view's dimming is for finding them.
    /// </summary>
    public static readonly DependencyProperty AsTheGameShowsProperty =
        DependencyProperty.Register(nameof(AsTheGameShows), typeof(bool), typeof(PlacePreview),
            new PropertyMetadata(false, OnInputChanged));

    public bool AsTheGameShows
    {
        get => (bool)GetValue(AsTheGameShowsProperty);
        set => SetValue(AsTheGameShowsProperty, value);
    }

    /// <summary>
    /// With <see cref="AsTheGameShows"/>: the objects (by their path in the place's tree, "NPCs/Group_2") drawn as the
    /// game draws them once switched on or once their conditions hold - a crowd the game picks, an NPC at her spot -
    /// with what is under them; what is switched off inside them stays off (1.9.2, the export's NPC groups).
    /// </summary>
    public static readonly DependencyProperty ShowAlsoProperty =
        DependencyProperty.Register(nameof(ShowAlso), typeof(IReadOnlyCollection<string>), typeof(PlacePreview),
            new PropertyMetadata(null, OnInputChanged));

    public IReadOnlyCollection<string>? ShowAlso
    {
        get => (IReadOnlyCollection<string>?)GetValue(ShowAlsoProperty);
        set => SetValue(ShowAlsoProperty, value);
    }

    /// <summary>Whether a scene entry is left out of what the place shows by itself (<see cref="AsTheGameShows"/>).</summary>
    private bool LeftOutOnArrival(SceneEntry e, bool ownStartActive)
    {
        if (!AsTheGameShows) return false;
        var also = ShowAlso;
        // One of a set the game picks from at random each time it switches on (RandomChildActivator: a spot's poses
        // of one NPC in one outfit): drawn only when asked for itself, never with the rest of its set.
        if (e.Choice != null && (also == null || !also.Contains(e.Choice))) return true;
        if (also != null && also.Any(p => e.Path == p || e.Path.StartsWith(p + "/", StringComparison.Ordinal)))
        {
            // The object asked for, and its own NPC placements: those start off as often as not and are switched on
            // by their conditions or by an action (the plugin builds them as authored, NpcFactory) - they are what
            // the group is for. Anything deeper keeps its own state.
            if (also.Contains(e.Path)) return false;
            int cut = e.Path.LastIndexOf('/');
            if (e.Npc != null && cut > 0 && also.Contains(e.Path[..cut])) return false;
            return !ownStartActive;
        }
        return e.ParentInactive || e.Conditional || !ownStartActive;
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
    /// Whether the level has a back picture that drifts, as the game's plugin finds it: always on a pack place (the
    /// back sprite, <see cref="LevelParallaxSecondary"/>), on 22 of the game's levels (their second object, its drift
    /// given here as the secondary's). An object the pack makes that drifts with the level takes its drift from it -
    /// cloned from it with its sprite, or copied (a light, a group) - and without one stays with the level (after
    /// 1.10.0; the preview drifted it at 0.75 whatever the back did).
    /// </summary>
    public static readonly DependencyProperty HasBackLayerProperty =
        DependencyProperty.Register(nameof(HasBackLayer), typeof(bool), typeof(PlacePreview),
            new PropertyMetadata(true, OnInputChanged));

    public bool HasBackLayer
    {
        get => (bool)GetValue(HasBackLayerProperty);
        set => SetValue(HasBackLayerProperty, value);
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
        if (d is PlacePreview p) p.SyncParallaxFollow();
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

    /// <summary>The base mask as it is being painted, or null to read the file.
    /// Set while a mask editor is open on it.</summary>
    public static readonly DependencyProperty LiveMaskBgraProperty =
        DependencyProperty.Register(nameof(LiveMaskBgra), typeof(byte[]), typeof(PlacePreview),
            new PropertyMetadata(null, OnInputChanged));

    public byte[]? LiveMaskBgra
    {
        get => (byte[]?)GetValue(LiveMaskBgraProperty);
        set => SetValue(LiveMaskBgraProperty, value);
    }

    /// <summary>Same, for the backdrop's mask.</summary>
    public static readonly DependencyProperty LiveSecondaryMaskBgraProperty =
        DependencyProperty.Register(nameof(LiveSecondaryMaskBgra), typeof(byte[]), typeof(PlacePreview),
            new PropertyMetadata(null, OnInputChanged));

    public byte[]? LiveSecondaryMaskBgra
    {
        get => (byte[]?)GetValue(LiveSecondaryMaskBgraProperty);
        set => SetValue(LiveSecondaryMaskBgraProperty, value);
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
    /// The level's art is the game's own (a vanilla level, 1.9.2): drawn at its
    /// own size, over <see cref="LevelArtPpu"/>, and placed by
    /// <see cref="LevelArtPivotX"/>/<see cref="LevelArtPivotY"/>, as the game draws
    /// it. Otherwise (a pack's place) it is fitted to the level frame and
    /// centred, as the runtime fits a pack's art. See SizeLevelLayer.
    /// </summary>
    public static readonly DependencyProperty LevelArtIsTheGamesProperty =
        DependencyProperty.Register(nameof(LevelArtIsTheGames), typeof(bool), typeof(PlacePreview),
            new PropertyMetadata(false, OnInputChanged));

    public bool LevelArtIsTheGames
    {
        get => (bool)GetValue(LevelArtIsTheGamesProperty);
        set => SetValue(LevelArtIsTheGamesProperty, value);
    }

    /// <summary>Where the game's level art is pivoted, as a share of it from its
    /// bottom-left: the centre, but for two levels whose art sits a little off it
    /// (104_WinterHotspring, 110_BadlandsParkingLot). Only with
    /// <see cref="LevelArtIsTheGames"/>.</summary>
    public static readonly DependencyProperty LevelArtPivotXProperty =
        DependencyProperty.Register(nameof(LevelArtPivotX), typeof(double), typeof(PlacePreview),
            new PropertyMetadata(0.5, OnInputChanged));

    public double LevelArtPivotX
    {
        get => (double)GetValue(LevelArtPivotXProperty);
        set => SetValue(LevelArtPivotXProperty, value);
    }

    public static readonly DependencyProperty LevelArtPivotYProperty =
        DependencyProperty.Register(nameof(LevelArtPivotY), typeof(double), typeof(PlacePreview),
            new PropertyMetadata(0.5, OnInputChanged));

    public double LevelArtPivotY
    {
        get => (double)GetValue(LevelArtPivotYProperty);
        set => SetValue(LevelArtPivotYProperty, value);
    }

    /// <summary>
    /// Where the level stands from the camera, in world units (1.9.2): a pack's
    /// place at the camera's centre (the runtime puts it there, (0, 0)); one of
    /// the game's levels where the game keeps it - its parent's place plus its
    /// own (<see cref="Model.VanillaLevelCatalog.LevelPositionX"/>). Drawn at the
    /// centre, the game's levels sat 13 game pixels above where the game shows
    /// them, and those placed further left (x -0.94) 13 to the right.
    /// </summary>
    public static readonly DependencyProperty LevelPositionXProperty =
        DependencyProperty.Register(nameof(LevelPositionX), typeof(double), typeof(PlacePreview),
            new PropertyMetadata(0.0, OnInputChanged));

    public double LevelPositionX
    {
        get => (double)GetValue(LevelPositionXProperty);
        set => SetValue(LevelPositionXProperty, value);
    }

    /// <summary>A game level's video screen, from the catalog (1.9.2): where the
    /// game shows its video, which the preview draws as <see cref="LevelVideoStill"/>.</summary>
    public static readonly DependencyProperty LevelVideoScreenProperty =
        DependencyProperty.Register(nameof(LevelVideoScreen), typeof(Model.VanillaLevelCatalog.VideoScreen), typeof(PlacePreview),
            new PropertyMetadata(null, OnInputChanged));

    public Model.VanillaLevelCatalog.VideoScreen? LevelVideoScreen
    {
        get => (Model.VanillaLevelCatalog.VideoScreen?)GetValue(LevelVideoScreenProperty);
        set => SetValue(LevelVideoScreenProperty, value);
    }

    /// <summary>The still of that video (a path, absolute or under the pack root).</summary>
    public static readonly DependencyProperty LevelVideoStillProperty =
        DependencyProperty.Register(nameof(LevelVideoStill), typeof(string), typeof(PlacePreview),
            new PropertyMetadata("", OnInputChanged));

    public string LevelVideoStill
    {
        get => (string)GetValue(LevelVideoStillProperty);
        set => SetValue(LevelVideoStillProperty, value);
    }

    public static readonly DependencyProperty LevelPositionYProperty =
        DependencyProperty.Register(nameof(LevelPositionY), typeof(double), typeof(PlacePreview),
            new PropertyMetadata(0.0, OnInputChanged));

    public double LevelPositionY
    {
        get => (double)GetValue(LevelPositionYProperty);
        set => SetValue(LevelPositionYProperty, value);
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

    /// <summary>
    /// The scale the level's own NPCs object keeps in the game while the place's NPCs node says 1 (the plugin sets one
    /// there only when it is not 1): the template level's for a pack place, the level's own for a game level the pack
    /// extends (its preview binds it). Measured: S17, after 1.10.0.
    /// </summary>
    public static readonly DependencyProperty NpcsObjectScaleProperty =
        DependencyProperty.Register(nameof(NpcsObjectScale), typeof(double), typeof(PlacePreview),
            new PropertyMetadata((double)VanillaLevelCatalog.TemplateNpcsScale, OnInputChanged));

    public double NpcsObjectScale
    {
        get => (double)GetValue(NpcsObjectScaleProperty);
        set => SetValue(NpcsObjectScaleProperty, value);
    }

    /// <summary>Sorting order of the level's secondary (distance/blur) sprite,
    /// which renders behind the base.</summary>
    private const int LevelSecondarySortingOrder = -5;

    /// <summary>Where the scene's rebuilds spend their time, summed since last cleared - for the performance
    /// tests (a rebuild is every step of a drag: the interface's thread is held for the whole of it).</summary>
    internal sealed class RebuildTimes
    {
        public static readonly string[] Names = { "tree", "scene", "lights", "level", "objects", "NPCs", "layer", "shaders", "handles", "list" };
        public readonly long[] Part = new long[Names.Length];
        public int Count;
        public void Clear() { Array.Clear(Part); Count = 0; }
    }

    internal RebuildTimes RebuildTime { get; } = new();

    private void RebuildOverlayGos()
    {
        long mark = System.Diagnostics.Stopwatch.GetTimestamp();
        void Mark(int part)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            RebuildTime.Part[part] += now - mark;
            mark = now;
        }
        RebuildTime.Count++;
        _rebuildQueued = false;
        ResubscribeOverlayTree();
        SeeThroughUndo();
        _worldLayer.Children.Clear();
        ClearSprays();
        _drawnNames.Clear();
        _objectImages.Clear();
        Mark(0);

        double minX = 0, minY = 0, maxX = CanvasWidth, maxY = CanvasHeight;

        string root = PackRoot ?? "";
        _scene = BuildScene();
        DrawLevelCanvases(_scene);
        Mark(1);
        // The place's own lights, from what it shows on arrival (1.9.2).
        UpdateLights();
        SyncLook();
        DrawEffects();
        ScheduleStandIn();
        Mark(2);

        // One ordered list of everything that lives in the level's sorting
        // layer. Ties keep the level sprites underneath the overlays, matching
        // Unity's "later wins at equal order" for our purposes: an overlay
        // authored at exactly the base's order is meant to sit on top of it.
        var drawables = new System.Collections.Generic.List<(int Order, int Tie, UIElement Element)>();

        _parallaxTargets.Clear();
        _enlargeable.Clear();
        _animTargets.Clear();
        // The suns drift with their lights (after 1.10.0), so they are made with what drifts.
        DrawLightMarkers();
        // The level's two pictures outlive this rebuild, and are placed by Canvas.Left/Top alone: whatever transform
        // they carry now is the last drift, never where they were made. Tracked from it, every rebuild while drifted
        // added the drift again - switching places with Preview parallax on walked the art away from the objects
        // (the author, 2026-11-21; PlaceParallaxFollowsTheScreenTests.SwitchingPlacesWhileDrifted...).
        _secondaryImage.RenderTransform = Transform.Identity;
        _baseImage.RenderTransform = Transform.Identity;
        _unlitElements.Clear();
        _shadersBefore.Clear();
        _shadersBefore.AddRange(_shaderTargets);
        _shaderTargets.Clear();

        double baseGain = ParallaxGain(LevelParallax, LevelParallaxReversed, PixelsPerUnit);
        if (_secondaryImage.Source != null)
        {
            drawables.Add((LevelArtSecondaryOrder, 0, _secondaryImage));
            _drawnNames[_secondaryImage] = Loc.T("maskStudio.layer.back");
            // The backdrop is a CHILD of the level root: it carries the root's
            // drift and adds its own, scaled by the root. That sum is the whole
            // depth effect.
            TrackParallax(_secondaryImage,
                baseGain + ParallaxGain(LevelParallaxSecondary, LevelParallaxSecondaryReversed, WorldPpu));

            // Same restore rule as the base image: it survives the rebuild, so
            // it has to be handed its flat bitmap back when the shader is off.
            bool secShaded = Shaders && !string.IsNullOrWhiteSpace(root)
                && !string.IsNullOrWhiteSpace(SecondarySprite)
                && (LiveSecondaryMaskBgra != null || !string.IsNullOrWhiteSpace(SecondaryMaskSprite))
                && TrackLevelShader(_secondaryImage, Path.Combine(root, Normalize(SecondarySprite)),
                                    Path.Combine(root, Normalize(SecondaryMaskSprite)), LiveSecondaryMaskBgra);
            if (!secShaded && _secondaryImage.Source is WriteableBitmap && !string.IsNullOrWhiteSpace(root)
                && !string.IsNullOrWhiteSpace(SecondarySprite))
                _secondaryImage.Source = LoadCachedBitmap(Path.Combine(root, Normalize(SecondarySprite)));
        }
        drawables.Add((LevelArtOrder, 0, _baseImage));
        _drawnNames[_baseImage] = Loc.T("maskStudio.layer.front");
        TrackParallax(_baseImage, baseGain);
        if (LevelArtUnlit) _unlitElements.Add(_baseImage);
        if (PlaceVideoStill(root)) drawables.Add((LevelVideoScreen!.SortingOrder, -1, _videoImage));
        // Unlike the NPC images, this one outlives the rebuild, so switching the
        // shader off has to hand its flat bitmap back — otherwise the level
        // stays frozen on the last frame the pass drew.
        bool levelShaded = Shaders && !string.IsNullOrWhiteSpace(root)
            && !string.IsNullOrWhiteSpace(BaseSprite)
            && (LiveMaskBgra != null || !string.IsNullOrWhiteSpace(MaskSprite))
            && TrackLevelShader(_baseImage, Path.Combine(root, Normalize(BaseSprite)),
                                Path.Combine(root, Normalize(MaskSprite)), LiveMaskBgra);
        if (!levelShaded && _baseImage.Source is WriteableBitmap && !string.IsNullOrWhiteSpace(root)
            && !string.IsNullOrWhiteSpace(BaseSprite))
            _baseImage.Source = LoadCachedBitmap(Path.Combine(root, Normalize(BaseSprite)));
        Mark(3);

        if (!string.IsNullOrEmpty(root) && _placeholder.Visibility != Visibility.Visible)
        {
            foreach (var entry in _scene)
            {
                if (entry.Hidden) continue;   // hidden from the preview only
                if (entry.Node != null && LeftOutOnArrival(entry, entry.Node.StartActive))
                    continue;                 // what the place shows by itself: nothing switched off or waiting on conditions
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
                var m = LeafMatrix(entry.World, w, h, o.SpritePpu, o.Model.SpritePivotX, o.Model.SpritePivotY);

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
                              "\n" + Loc.F("preview.place.sortingOrder", "order", o.SortingOrder) +
                              (o.SortingOrder < LevelArtOrder ? "  " + Loc.T("preview.place.behindArt") : "") +
                              (o.StartActive ? "" : "\n" + Loc.T("preview.place.startsInactive")) +
                              (entry.ParentInactive ? "\n" + Loc.T("preview.place.parentInactive") : ""),
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                // How it moves (after 1.10.0): by its own mask, the level's pass or the characters' jiggle, or by the
                // back layer's read over its own picture - as the game moves it (PlaceFactory.ApplyObjectMask).
                if (Shaders && o.Model.AppliesOwnVisuals) ShadeObject(o, img, abs, root, m);
                var pathForClick = entry.Path;
                var oForClick = o;
                img.MouseLeftButtonDown += (_, e) => { SelectNode(oForClick, pathForClick); e.Handled = true; };
                drawables.Add((o.SortingOrder, 1, img));
                _drawnNames[img] = o.Display;
                _objectImages[o] = img;
                // Drawn with the game's unlit material: the level's lamps leave it as it is (1.9.2).
                if (string.Equals(o.Model.Baseline?.SpriteRenderer?.Material, UnlitMaterial, StringComparison.Ordinal)) _unlitElements.Add(img);
                TrackParallax(img, entry.ParallaxGain, entry);

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
                    TrackParallax(tintRect, entry.ParallaxGain, entry);
                }

                ExpandBounds(m, w, h, ref minX, ref minY, ref maxX, ref maxY);
            }
        }

        Mark(4);
        // Placed NPCs (pose + shadow + particle marker) go into the same sorted
        // world layer, so their sorting orders interleave with the level art and
        // overlays exactly as at runtime.
        CollectNpcDrawables(root, drawables, ref minX, ref minY, ref maxX, ref maxY);
        Mark(5);

        // Stable sort: equal orders keep authored sequence, which is what the
        // runtime does for two overlays built back-to-back at the same order.
        foreach (var d in drawables.OrderBy(d => d.Order).ThenBy(d => d.Tie))
            _worldLayer.Children.Add(d.Element);
        // The characters' pictures crisp where the view enlarges them past their own pixels.
        SharpenWhenEnlarged();
        // What the components do, played from where it is now (PlacePreview.Float).
        SyncAnimClock();
        if (_animClockOn) AnimateTo(AnimNow);
        // Beside the mask studio: what is in front of the layer painted, see-through (PlacePreview.Cover).
        SeeThroughInFront();

        SyncWetClock();
        Mark(6);
        AdoptCardPasses();
        SyncShaderTimer();
        Mark(7);
        RefreshGizmo();
        Mark(8);
        // The object set only changes on structural rebuilds, not per drag frame.
        if (_activeHandle == GizmoHandle.None) RefreshObjectMenu();
        Mark(9);

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
    // the NPCs tab); the shadow is a tinted ellipse; the Wet object's drops are
    // the game's own, on a layer over the scene (PlacePreview.Wet).

    /// <summary>NPC / shadow / circle sprites are authored at 100 px/unit
    /// (level art is 70.32), so a sprite pixel is this many canvas pixels.</summary>
    private const double NpcSpritePpu = 100.0;

    /// <summary>Sorting-order tiers, mirroring the runtime: the NPC sits at its
    /// def order, its shadow well below.</summary>
    private const int NpcShadowTieBelow = 0;   // shadow keeps its own (low) order
    private const int NpcBodyTie = 2;


    private static readonly Dictionary<string, BitmapSource?> _bitmapCache = new();

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

        /// <summary>Whether it, or an ancestor, is shown only when its conditions hold (1.9.2): what the place
        /// shows by itself leaves it out (<see cref="AsTheGameShows"/>).</summary>
        public bool Conditional;

        /// <summary>When it is, or is under, one of the children of an object that switches on one of its children at
        /// random (<see cref="PicksAtRandom"/>): that child's path, the choice it belongs to (1.9.2). Null otherwise.</summary>
        public string? Choice;

        /// <summary>For each ancestor level, whether that ancestor still has
        /// siblings below it — i.e. whether the tree needs a vertical line
        /// carried down through this row at that indent.</summary>
        public bool[] Guides = System.Array.Empty<bool>();

        /// <summary>Last of its siblings, so its elbow closes the branch.</summary>
        public bool IsLast;
        // What its components - and those above it - do to it (PlacePreview.Float): what moves it, its own picture's
        // alpha, its parent's blend of it, whether it is on; a picker over its own children and the next child's index.
        public Mover[]? Movers;
        public Fader? Fader, Blend;
        public Func<double, bool>[]? Gates;
        public ChildPicker? Pickers;
        public int ChildIndex;

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
    // pixels and samples the pose. An NPC's pass renders the screen pixels it
    // covers at the view's scale (NpcPictureSize) - a few hundred at the fitted
    // view, its art's own when zoomed in that far - and WPF places it.

    /// <summary>Ceiling for the LEVEL pass, which renders at its source
    /// resolution. Base art is 2048×1136, so this is a guard against an
    /// unusually large sprite rather than a reduction in the normal case.</summary>
    private const int LevelShaderMaxEdge = 2048;

    /// <summary>Whether the shaders' clock is running - for tests.</summary>
    internal bool ShaderClockRunning => _shaderClockOn;

    /// <summary>How many times the shaders have been drawn - for tests, to
    /// measure the frame rate a quality preset really gives.</summary>
    internal int ShaderSteps { get; private set; }

    /// <summary>Pictures put on screen: the shaders' and the game's look's
    /// (Options ▸ Show preview frame rate).</summary>
    internal int FramesShown => ShaderSteps + LookPasses;

    private sealed class ShaderTarget
    {
        public Image Image = null!;
        public byte[] Base = null!, Mask = null!, Output = null!;
        public int SrcW, SrcH, OutW, OutH;
        /// <summary>Mask dimensions, which need not match the art's — the level
        /// pass samples it in UV space. Zero means "same as the source", which
        /// is what the bust pass requires.</summary>
        public int MaskW, MaskH;
        /// <summary>Set when the mask is the editor's live buffer. That array is
        /// mutated in place as strokes land, so holding the reference is all the
        /// live update needs: the next tick already sees the new paint.</summary>
        public bool LiveMask;
        /// <summary>A jiggle mask being painted (the studio's 256x256 buffer, mutated as strokes land): laid on the
        /// art's grid again before each draw, as the jiggle pass reads its mask at the art's own size.</summary>
        public byte[]? LiveSource;
        /// <summary>Set for a bust-family object (NPCs). Null means the level
        /// family — the two use genuinely different shaders.</summary>
        public JiggleParams? Params;
        public MilkingShader.Settings Milking;
        /// <summary>An NPC's pass on the graphics card, made on first use there.</summary>
        public Rendering.Gpu.GpuAlone? Card;
        /// <summary>A level layer's pass on the graphics card, made on first use there.</summary>
        public Rendering.Gpu.GpuMilking? Milk;
        /// <summary>Its picture where the card's own drawing of the scene reads it (1.10.0).</summary>
        public Rendering.Gpu.PlaceCardPicture? OnCard;
        public (float r, float g, float b, float a) Tint;
        public WriteableBitmap Bitmap = null!;
        /// <summary>An NPC's canvas units per pixel of its art, each way: what its
        /// picture is sized from, at the view's scale (1.9.1).</summary>
        public double CanvasPerArtX, CanvasPerArtY;
        /// <summary>What it was made from (1.10.0): the art's picture, the mask's file as it was on disk (or
        /// "live" for one being painted), the tint as written. A rebuild that finds a shader over the same three
        /// keeps it - its buffers, its picture, its pass on the card - rather than making it again.</summary>
        public BitmapSource? BaseSource;
        public string MaskKey = "", TintKey = "";
        /// <summary>Its picture has been drawn at least once: there is something in its bitmap to show.</summary>
        public bool Shown;
    }

    private readonly List<ShaderTarget> _shaderTargets = new();
    /// <summary>The shaders of the scene being replaced, while it is rebuilt: what the new scene's may take over.</summary>
    private readonly List<ShaderTarget> _shadersBefore = new();

    /// <summary>A file as it is on disk now - its path, when it was written, its size - or "" when it is not there.</summary>
    private static string FileKey(string abs)
    {
        try
        {
            var info = new FileInfo(abs);
            return info.Exists ? abs + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length : "";
        }
        catch (Exception) { return ""; }
    }

    /// <summary>One of the shaders of the scene being replaced that <paramref name="fits"/>, taken out of the
    /// list so no other takes it; null for none.</summary>
    private ShaderTarget? TakeBefore(Func<ShaderTarget, bool> fits)
    {
        for (int i = 0; i < _shadersBefore.Count; i++)
        {
            var t = _shadersBefore[i];
            if (!fits(t)) continue;
            _shadersBefore.RemoveAt(i);
            return t;
        }
        return null;
    }
    private readonly System.Diagnostics.Stopwatch _shaderClock = new();

    // The shaders' clock (1.9.1): the display's own frames, as the bust
    // preview's - CompositionTarget.Rendering, with the FramePacer choosing the
    // frames to draw on at the rate of the monitor the window is on - and the
    // drawing itself on a worker thread, so a frame costs the interface's
    // thread only the copy onto the screen. Until 1.9.1 it was a
    // Background-priority timer drawing on the interface's thread: its ticks
    // fall on the system's 15.6 ms clock, so it never drew more than 64 frames
    // a second whatever the display, and hooking Rendering then (with the
    // drawing still on that thread) kept the work waiting - Medium drew 8.
    private bool _shaderClockOn, _shaderInFlight;
    private int _shaderGeneration;
    private System.Threading.Tasks.TaskScheduler? _shaderUi;
    private readonly FramePacer _shaderPacer = new();

    /// <summary>
    /// Put an NPC's pose through the jiggle shader, if it has a mask to drive
    /// one. Returns false when there is nothing to animate, leaving the caller's
    /// flat bitmap in place.
    /// </summary>
    private bool TrackShader(Image img, string poseAbs, string maskAbs, JiggleParams p, Matrix toCanvas, byte[]? liveMask = null)
    {
        var baseSource = LoadCachedBitmap(poseAbs);
        bool live = liveMask != null && liveMask.Length == Rendering.MaskBuffer.Size * Rendering.MaskBuffer.Size * 4;
        string maskKey = live ? "live" : FileKey(maskAbs);   // English on purpose: a key, never shown
        if (baseSource == null || maskKey.Length == 0) return false;
        string tintKey = p.Tint ?? "";
        double perArtX = Math.Sqrt(toCanvas.M11 * toCanvas.M11 + toCanvas.M12 * toCanvas.M12);
        double perArtY = Math.Sqrt(toCanvas.M21 * toCanvas.M21 + toCanvas.M22 * toCanvas.M22);

        // The shader this NPC had before the rebuild, when nothing it reads has changed (1.10.0): kept whole.
        // Every step of a drag rebuilds the scene, and each rebuild read the mask's file again, copied the
        // art's pixels and made new buffers and a new picture for every NPC.
        var kept = TakeBefore(t => t.Params != null && ReferenceEquals(t.Params, p) && ReferenceEquals(t.BaseSource, baseSource)
                                   && t.MaskKey == maskKey && t.TintKey == tintKey && ReferenceEquals(t.LiveSource, live ? liveMask : null));
        if (kept != null)
        {
            double wasX = kept.CanvasPerArtX, wasY = kept.CanvasPerArtY;
            kept.CanvasPerArtX = perArtX; kept.CanvasPerArtY = perArtY;
            var (nw, nh) = NpcPictureSize(kept);
            bool sized = Resized(kept, nw, nh);
            // New buffers only while nothing is drawing into the old ones.
            if (!sized || (!_shaderInFlight && !_lookInFlight))
            {
                kept.Image = img;
                if (sized) SizeNpcPicture(kept, nw, nh);
                else img.Source = kept.Bitmap;
                _shaderTargets.Add(kept);
                return true;
            }
            kept.CanvasPerArtX = wasX; kept.CanvasPerArtY = wasY;
            _shadersBefore.Add(kept);      // its art and mask are still taken over below
        }

        // The art's pixels and the mask from a shader of the scene before over the same files, else read now.
        var same = _shadersBefore.Find(t => t.Params != null && ReferenceEquals(t.BaseSource, baseSource));
        int w = baseSource.PixelWidth, h = baseSource.PixelHeight;
        var basePx = same?.Base ?? CopyBgra(baseSource, w, h);
        if (basePx == null || w <= 0 || h <= 0) return false;
        // The mask has to land on the pose's grid: the CPU port samples both
        // from one array shape, where the GPU would resample in UV space.
        var maskPx = live ? OnArtGrid(liveMask!, w, h, null)
                   : same != null && same.MaskKey == maskKey && same.LiveSource == null ? same.Mask : LoadBgraScaled(maskAbs, w, h);
        if (maskPx == null) return false;

        var (tr, tg, tb, ta) = BustComposer.ParseTint(p.Tint);
        var target = new ShaderTarget
        {
            Image = img, Base = basePx, Mask = maskPx, SrcW = w, SrcH = h,
            Params = p, Tint = (tr, tg, tb, ta),
            CanvasPerArtX = perArtX, CanvasPerArtY = perArtY,
            BaseSource = baseSource, MaskKey = maskKey, TintKey = tintKey,
            LiveMask = live, LiveSource = live ? liveMask : null,
        };
        var (outW, outH) = NpcPictureSize(target);
        SizeNpcPicture(target, outW, outH);
        _shaderTargets.Add(target);
        return true;
    }

    /// <summary>
    /// The size an NPC's moving picture is drawn at: the screen pixels it covers
    /// at the view's scale - so zoomed in it keeps its art's detail (1.9.1, the
    /// author: "even some art from the pack itself looks low res when zoomed in";
    /// it was drawn at no more than 384 pixels and enlarged) - no more than the
    /// art's own, no fewer than 16.
    /// </summary>
    private (int w, int h) NpcPictureSize(ShaderTarget t)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double sw = t.SrcW * t.CanvasPerArtX * _displayScale * dpi.DpiScaleX, sh = t.SrcH * t.CanvasPerArtY * _displayScale * dpi.DpiScaleY;
        int w = Math.Clamp((int)Math.Ceiling(sw), Math.Min(16, t.SrcW), t.SrcW);
        int h = Math.Clamp((int)Math.Ceiling(sh), Math.Min(16, t.SrcH), t.SrcH);
        return (w, h);
    }

    /// <summary>
    /// Whether a character's picture is drawn again at <paramref name="w"/> x <paramref name="h"/>: for a real change
    /// only (each costs a new buffer) - and always for its art's own size, which a picture within an eighth of it stayed
    /// short of: zoomed in, a picture a few percent smaller than its art beat against its pixels in bands (after 1.10.0,
    /// PlaceZoomDetailTests).
    /// </summary>
    private static bool Resized(ShaderTarget t, int w, int h)
        => w == t.SrcW && h == t.SrcH ? w != t.OutW || h != t.OutH
         : Math.Abs(w - t.OutW) > Math.Max(2, t.OutW / 8) || Math.Abs(h - t.OutH) > Math.Max(2, t.OutH / 8);

    /// <param name="showNow">Put the new picture up at once (blank until drawn) - else the last one stays up until the
    /// new one is drawn (<see cref="Show"/>).</param>
    private static void SizeNpcPicture(ShaderTarget t, int w, int h, bool showNow = true)
    {
        t.OutW = w; t.OutH = h;
        t.Output = new byte[w * 4 * h];
        t.Bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
        if (showNow) t.Image.Source = t.Bitmap;
        t.Shown = false;
    }

    /// <summary>The characters' pictures (bodies, reflections) and where each lands on the canvas (its pixel rect's
    /// matrix): drawn crisp once enlarged past their own pixels, smooth while shrunk.</summary>
    private readonly List<(Image Image, Matrix ToCanvas)> _enlargeable = new();

    /// <summary>
    /// Each character's picture crisp - nearest pixel - where the view enlarges it past its own pixels, smooth (WPF's
    /// high-quality scaling) where it shrinks it (after 1.10.0: zoomed in, a character came out a smooth blur of its
    /// art while the place's art beside it showed its own pixels). Shrunk, as at the view's fit, nothing changes.
    /// </summary>
    private void SharpenWhenEnlarged()
    {
        if (_enlargeable.Count == 0) return;
        double dip = VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (var (img, m) in _enlargeable)
        {
            if (img.Source is not BitmapSource src || src.PixelWidth <= 0 || img.Width <= 0) continue;
            double canvasPerLayout = Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
            double screenPerPixel = canvasPerLayout * (img.Width / src.PixelWidth) * _displayScale * dip;
            var mode = screenPerPixel >= 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
            if (RenderOptions.GetBitmapScalingMode(img) == mode) continue;
            RenderOptions.SetBitmapScalingMode(img, mode);
            img.InvalidateVisual();
        }
    }

    /// <summary>The characters' shader pictures' sizes against their art's - for tests.</summary>
    internal IReadOnlyList<string> CharacterPictureSizes
        => _shaderTargets.Where(t => t.Params != null).Select(t => $"{t.OutW}x{t.OutH}/{t.SrcW}x{t.SrcH}").ToList();

    /// <summary>How the characters' pictures are scaled now - for tests.</summary>
    internal IReadOnlyList<BitmapScalingMode> CharacterScaling => _enlargeable.Select(e => RenderOptions.GetBitmapScalingMode(e.Image)).ToList();

    private System.Windows.Threading.DispatcherTimer? _shaderResize;

    /// <summary>The characters' pictures are to be sized once the pass drawing into them lands.</summary>
    private bool _resizeWhenLanded;

    /// <summary>The NPCs' pictures sized for the view once the zoom settles.</summary>
    private void ScheduleShaderResize()
    {
        if (_shaderTargets.Count == 0) return;
        // At the interface's own priority: below it, the shaders' frames (every display frame) starved it, and zoomed
        // in a character stayed at the fitted view's size for many seconds (after 1.10.0, PlaceZoomDetailTests).
        _shaderResize ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(200), System.Windows.Threading.DispatcherPriority.Normal,
                                                                        (_, _) => ResizeNpcPictures(), Dispatcher) { IsEnabled = false };
        _shaderResize.Stop();
        _shaderResize.Start();
    }

    private void ResizeNpcPictures()
    {
        // A pass drawing into the old buffers lands first - and then they are sized (OnShaderFrame).
        if (_shaderInFlight) { _resizeWhenLanded = true; return; }
        _resizeWhenLanded = false;
        _shaderResize?.Stop();
        bool any = false;
        foreach (var t in _shaderTargets)
        {
            if (t.Params == null) continue;
            var (w, h) = NpcPictureSize(t);
            // Only for a real change: each one costs a new buffer.
            if (!Resized(t, w, h)) continue;
            // While the shaders' clock runs, the next frame draws them at the new size off this thread, the last
            // picture up until then: drawn here, a zoom held the interface for up to a second (after 1.10.0,
            // PlacePreviewPerformanceTests).
            SizeNpcPicture(t, w, h, showNow: !_shaderClockOn);
            any = true;
        }
        if (any) { _shaderGeneration++; if (!_shaderClockOn) StepShaders(); SharpenWhenEnlarged(); }
    }

    /// <summary>
    /// Put the level's base art through the Milking pass its material really
    /// runs, driven by the place's own mask sprite.
    /// <para/>
    /// The base art only — the backdrop keeps a plain material, and so does
    /// every GameObject that hasn't been given a mask of its own.
    /// </summary>
    private bool TrackLevelShader(Image img, string baseAbs, string maskAbs, byte[]? liveMask, bool anyImage = false)
    {
        var baseSource = LoadCachedBitmap(baseAbs);
        if (baseSource == null) return false;
        int w = baseSource.PixelWidth, h = baseSource.PixelHeight;
        if (w <= 0 || h <= 0) return false;
        bool live = liveMask != null && liveMask.Length == Rendering.MaskBuffer.Size * Rendering.MaskBuffer.Size * 4;
        string maskKey = live ? "live" : FileKey(maskAbs);   // English on purpose: a key, never shown
        if (maskKey.Length == 0) return false;

        // The layer's shader as it was before the rebuild, when nothing it reads has changed (1.10.0): kept
        // whole - its buffers, its picture (which goes on showing the last frame drawn), its pass on the card.
        // Every step of a drag rebuilds the scene, and each rebuild copied the art's nine megabytes, read the
        // mask's file again and made a new picture of the art's size for each layer.
        // An object's picture is made again with the scene (anyImage): its shader is kept for the new one.
        var kept = TakeBefore(t => t.Params == null && (anyImage || ReferenceEquals(t.Image, img)) && ReferenceEquals(t.BaseSource, baseSource)
                                   && t.MaskKey == maskKey && (!live || ReferenceEquals(t.Mask, liveMask)));
        if (kept != null)
        {
            kept.Image = img;
            img.Source = kept.Bitmap;
            _shaderTargets.Add(kept);
            return true;
        }
        var same = _shadersBefore.Find(t => t.Params == null && ReferenceEquals(t.BaseSource, baseSource));
        var basePx = same?.Base ?? CopyBgra(baseSource, w, h);
        if (basePx == null) return false;

        // A mask being painted right now wins over the one on disk, and is used
        // AT ITS OWN SIZE — the pass samples in UV space, so the editor's square
        // buffer drives the art directly with nothing in between to keep in
        // step. The array is the editor's own and is mutated as strokes land,
        // so the running loop shows each one without being told.
        int maskW, maskH;
        byte[]? maskPx;
        if (live)
        {
            maskPx = liveMask;
            maskW = maskH = Rendering.MaskBuffer.Size;
        }
        else if (same != null && same.MaskKey == maskKey && !same.LiveMask)
        {
            maskPx = same.Mask; maskW = same.MaskW; maskH = same.MaskH;
        }
        else
        {
            maskPx = LoadBgraAtNative(maskAbs, out maskW, out maskH, cached: false);
        }
        if (maskPx == null || maskW <= 0 || maskH <= 0) return false;

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
            MaskW = maskW, MaskH = maskH, LiveMask = liveMask != null,
            OutW = outW, OutH = outH, Output = new byte[outW * 4 * outH],
            Params = null, Milking = MilkingShader.Settings.Level, Bitmap = bmp,
            BaseSource = baseSource, MaskKey = maskKey,
        });
        return true;
    }

    private readonly List<(IDisposable Pass, long At)> _retiredPasses = new();

    /// <summary>
    /// The shaders' passes on the card handed on from the scene before to the one just built (1.10.0). Every
    /// rebuild - one for each step of a drag - makes its shaders anew, and each made itself a new pass on the card
    /// on first use: its art sent to the card again (nine megabytes for a level's), and the old pass never let
    /// go of (a picture on the card is only freed when told to be). Here a shader over the same art takes the
    /// old one's pass, art and all; a pass nothing took is let go of a little later, once no draw still under way
    /// can be reading it.
    /// </summary>
    private void AdoptCardPasses()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        var before = _shadersBefore.ToArray();     // those the new scene did not keep whole
        _shadersBefore.Clear();
        foreach (var old in before)
        {
            if (old.Card == null && old.Milk == null) continue;
            ShaderTarget? heir = null;
            foreach (var s in _shaderTargets)
                if (ReferenceEquals(s.Base, old.Base) && (s.Params != null) == (old.Params != null) && s.Card == null && s.Milk == null)
                {
                    heir = s;
                    break;
                }
            if (heir != null) { heir.Card = old.Card; heir.Milk = old.Milk; }
            else
            {
                if (old.Card != null) _retiredPasses.Add((old.Card, now));
                if (old.Milk != null) _retiredPasses.Add((old.Milk, now));
            }
        }
        if (_retiredPasses.Count == 0) return;
        long grace = System.Diagnostics.Stopwatch.Frequency * 2;
        List<IDisposable>? gone = null;
        for (int i = _retiredPasses.Count - 1; i >= 0; i--)
        {
            if (now - _retiredPasses[i].At < grace) continue;
            (gone ??= new()).Add(_retiredPasses[i].Pass);
            _retiredPasses.RemoveAt(i);
        }
        // Off the interface's thread: letting go waits for the card.
        if (gone != null) System.Threading.Tasks.Task.Run(() => { foreach (var pass in gone) pass.Dispose(); });
    }

    /// <summary>Start or stop the animation loop to match what was just built.</summary>
    private void SyncShaderTimer()
    {
        // A pass still drawing belongs to what was there before.
        _shaderGeneration++;
        if (_shaderTargets.Count == 0)
        {
            StopShaders();
            _shaderClock.Reset();
            return;
        }
        if (!_shaderClock.IsRunning) _shaderClock.Restart();
        // Built while off screen: started when it comes on (IsVisibleChanged).
        if (!IsVisible) { StopShaders(); return; }
        if (!_shaderClockOn)
        {
            CompositionTarget.Rendering += OnShaderFrame;
            _shaderClockOn = true;
        }
        _shaderPacer.Restart();
        _shaderPacer.Due(Rendering.DisplayRefresh.RateOf(this));
        // A shader with no picture yet is drawn once now rather than on the next frame - not one kept from the
        // scene before (its picture shows its last frame), and none while the card draws the scene for the look
        // over it (it draws them with it).
        if (_cardLookCovers || _shaderInFlight) return;
        bool any = false;
        float t = (float)_shaderClock.Elapsed.TotalSeconds;
        bool useCard = Services.PreviewRendererManager.UseCard;
        var sampling = Services.PreviewQualityManager.Current.Sampling;
        foreach (var target in _shaderTargets)
        {
            if (target.Shown) continue;
            Draw(target, t, useCard, sampling);
            Show(target);
            any = true;
        }
        if (any) ShaderSteps++;
    }

    /// <summary>The shaders' clock off: off screen, or nothing to animate.</summary>
    private void StopShaders()
    {
        if (!_shaderClockOn) return;
        CompositionTarget.Rendering -= OnShaderFrame;
        _shaderClockOn = false;
    }

    /// <summary>A display frame: the shaders are drawn for it, off the
    /// interface's thread, when the pacer says so and the last pass is in.</summary>
    private void OnShaderFrame(object? sender, EventArgs e)
    {
        // While the card draws the scene for the look over it, it draws the shaders' pictures with it and keeps
        // them (1.10.0): nothing to bring back or put on screen here.
        if (_cardLookCovers) return;
        if (_shaderInFlight || !IsVisible || _shaderTargets.Count == 0) return;
        if (!_shaderPacer.Due(Rendering.DisplayRefresh.RateOf(this))) return;
        var targets = _shaderTargets.ToArray();
        int generation = _shaderGeneration;
        float t = (float)_shaderClock.Elapsed.TotalSeconds;
        bool useCard = Services.PreviewRendererManager.UseCard;
        var sampling = Services.PreviewQualityManager.Current.Sampling;
        _shaderInFlight = true;
        _shaderUi ??= System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext();
        System.Threading.Tasks.Task.Run(() => { foreach (var target in targets) Draw(target, t, useCard, sampling); })
            .ContinueWith(task =>
            {
                _shaderInFlight = false;
                if (_resizeWhenLanded) { ResizeNpcPictures(); return; }
                if (!task.IsCompletedSuccessfully || generation != _shaderGeneration || !IsVisible) return;
                ShaderSteps++;
                foreach (var target in targets) Show(target);
            }, _shaderUi);
    }

    /// <summary>Draw every shader now, on this thread (the first frame of what
    /// was just built).</summary>
    private void StepShaders()
    {
        // Nothing to show while the control is off screen, and a background
        // tab should not be burning a core on invisible pixels. A pass in
        // flight draws into the same buffers and lands next.
        if (!IsVisible || _shaderInFlight) return;
        ShaderSteps++;
        float t = (float)_shaderClock.Elapsed.TotalSeconds;
        bool useCard = Services.PreviewRendererManager.UseCard;
        var sampling = Services.PreviewQualityManager.Current.Sampling;
        foreach (var target in _shaderTargets)
        {
            Draw(target, t, useCard, sampling);
            Show(target);
        }
    }

    /// <summary>One shader's picture: an NPC's sampled as the quality says (as the
    /// NPC tab's with no backdrop), a level layer's at its art's own grid.</summary>
    private static void Draw(ShaderTarget s, float t, bool useCard, JiggleShader.Sampling sampling)
    {
        // An NPC's mask is the pack's file, and a level's unless it is being painted: sent to the card once (1.10.0).
        if (s.LiveSource != null) OnArtGrid(s.LiveSource, s.SrcW, s.SrcH, s.Mask);
        // At the art's own size (zoomed in that far): one sample a pixel, at its middle - the art's own pixel. The
        // game's 2x2 a screen pixel there took four points between the art's pixels and blended neighbours, and
        // zoomed in a character was a blur of its art (after 1.10.0, the author: "when I zoom in I can still see
        // things pixelated ... things should show at full size when zoomed in close enough"; PlaceZoomDetailTests).
        if (s.Params != null && s.OutW >= s.SrcW && s.OutH >= s.SrcH) sampling = new JiggleShader.Sampling(1, 1, 1);
        var card = s.Params != null && useCard ? (s.Card ??= Rendering.Gpu.GpuAlone.TryCreate()) : null;
        if (card != null)
            card.Draw(s.Base, s.Mask, s.SrcW, s.SrcH, s.Params!, s.Tint, t, s.Output, s.OutW, s.OutH, sampling, maskStill: !s.LiveMask);
        else if (s.Params != null)
            JiggleShader.Render(s.Base, s.Mask, s.SrcW, s.SrcH, s.Params, s.Tint, t,
                                s.Output, s.OutW, s.OutH, sampling);
        else if (useCard && (s.Milk ??= Rendering.Gpu.GpuMilking.TryCreate()) is { } milk)
            milk.Draw(s.Base, s.SrcW, s.SrcH, s.Mask, s.MaskW, s.MaskH, s.Milking, t, s.Output, s.OutW, s.OutH, maskStill: !s.LiveMask);
        else
            MilkingShader.Render(s.Base, s.SrcW, s.SrcH, s.Mask, s.MaskW, s.MaskH,
                                 s.Milking, t, s.Output, s.OutW, s.OutH);
    }

    /// <summary>A mask on the studio's 256x256 grid laid on art of <paramref name="w"/> x <paramref name="h"/>,
    /// stretched over all of it (the game reads a mask at the art's own uv), nearest texel - into
    /// <paramref name="into"/> when given.</summary>
    internal static byte[] OnArtGrid(byte[] grid, int w, int h, byte[]? into)
    {
        const int size = Rendering.MaskBuffer.Size;
        var o = into != null && into.Length == w * h * 4 ? into : new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Min(size - 1, (int)((y + 0.5) * size / h));
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(size - 1, (int)((x + 0.5) * size / w));
                Buffer.BlockCopy(grid, (sy * size + sx) * 4, o, (y * w + x) * 4, 4);
            }
        }
        return o;
    }

    private static void Show(ShaderTarget s)
    {
        s.Bitmap.WritePixels(new Int32Rect(0, 0, s.OutW, s.OutH), s.Output, s.OutW * 4, 0);
        // A picture sized again goes up as it is first drawn.
        if (!ReferenceEquals(s.Image.Source, s.Bitmap)) s.Image.Source = s.Bitmap;
        s.Shown = true;
    }

    /// <summary>Register an element to drift, unless it has no reason to.</summary>
    private void TrackParallax(UIElement element, double gain, SceneEntry? entry = null)
    {
        var m = (element.RenderTransform as MatrixTransform)?.Matrix ?? Matrix.Identity;
        // Floating too, with whatever floats above it (PlacePreview.Float).
        TrackAnimated(element, m, gain, entry);
        if (Math.Abs(gain) < 1e-6) return;
        _parallaxTargets.Add((element, m, gain));
        // Built again while the place is drifted: drifted from the start, not shown where it was made until
        // the cursor next moves.
        if (Drift is { } drift)
        {
            var drifted = m;
            drifted.Translate(gain * drift.Across, gain * drift.Down * 0.5);
            element.RenderTransform = new MatrixTransform(drifted);
        }
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
        // The light they cast: worked out again when the look is next drawn.
        DriftLights();
    }

    /// <summary>Put every drifting element back where it was authored.</summary>
    private void ResetParallax()
    {
        foreach (var (element, baseM, _) in _parallaxTargets)
            element.RenderTransform = new MatrixTransform(baseM);
        DriftLights();
    }

    /// <summary>Walk the whole GameObject tree into a flat, tree-ordered list.</summary>
    private List<SceneEntry> BuildScene()
    {
        var entries = new List<SceneEntry>();
        WalkScene(GameObjects, "", 0, Aff.Identity, false, false, new List<bool>(), entries, worldToLevel: WorldToLevel,
                  npcsKeptScale: NpcsObjectScale);
        PlaceOnLevelCanvases(entries);
        ApplyHiddenFlags(entries);
        ApplyParallaxGains(entries);
        WorkOutAnimations(entries);
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
    /// <summary>
    /// The drift of something the pack makes, set to drift: the back picture's effect, as the game gives it (cloned or
    /// copied from it, PlaceFactory): its strength and its reversal unless the thing sets them (an unset field is left
    /// as it was; a reversal set to false is not reversed, after 1.10.0). With no back picture an unset strength is
    /// nothing, and an unset reversal not reversed.
    /// </summary>
    private double PackOwnDrift(float? ownStrength, bool? ownReversed, double parentScale)
    {
        double? strength = ownStrength is { } set ? set : HasBackLayer ? LevelParallaxSecondary : null;
        bool reversed = ownReversed ?? (HasBackLayer && LevelParallaxSecondaryReversed);
        return strength is { } s ? ParallaxGain(s, reversed, parentScale) : 0;
    }

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
                if (node.ParallaxIsUI)
                    own = ParallaxGain(node.ParallaxStrength * 1000.0, node.ParallaxReversed, 1.0);
                else if (node.Model.KeepsWhatItFinds)
                    own = ParallaxGain(node.ParallaxStrength, node.ParallaxReversed, parentScale);   // the game's own effect, as extracted
                else own = PackOwnDrift(node.Model.ParallaxStrength, node.Model.ParallaxReversed, parentScale);
            }
            // A character drifting by its own (after 1.10.0) is given its effect as an object of the pack's is
            // (NpcFactory, PlaceFactory.GiveDrift).
            else if (e.Npc is { ParallaxEnabled: true } standing)
                own = PackOwnDrift(standing.Model.ParallaxStrength, standing.Model.ParallaxReversed, parentScale);

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

    /// <summary>Whether a node switches on one of its children at random each time it switches on, its NPC placements
    /// among them: the pack's RandomChildActivator, or the game's own component of that name on one of its objects.</summary>
    internal static bool PicksAtRandom(GameObjectDef node)
        => node.Components.Any(c => c.Type == "RandomChildActivator")
           || (node.Baseline?.Components?.Contains("RandomChildActivator") ?? false);

    /// <summary>
    /// The game's world in the level's own units (under its root, which the canvas draws through
    /// <see cref="WorldPpu"/>): an object the pack makes stands at its X and Y in the WORLD (the plugin sets
    /// <c>transform.position</c>), while its size is the level root's child's. Measured in the game (1.10.0,
    /// InGameSuiteTests.S11 and the light objects' places): before, the preview put such an object at X and Y
    /// in the level's units - 0.79 of the way out from the middle - while its own handle and the game had it
    /// at X and Y in the world.
    /// </summary>
    private Aff WorldToLevel
    {
        get
        {
            double s = LevelScale > 0 ? LevelScale : 1.0;
            return new Aff(1 / s, 0, 0, 1 / s, -LevelPositionX / s, -LevelPositionY / s);
        }
    }

    private static void WalkScene(IEnumerable? items, string prefix, int depth,
                                  Aff parentWorld, bool inNpcSubtree, bool parentInactive,
                                  List<bool> ancestorHasMore, List<SceneEntry> into, bool parentConditional = false,
                                  string? parentChoice = null, bool parentPicks = false, Aff? worldToLevel = null,
                                  double npcsKeptScale = 1.0)
    {
        var toLevel = worldToLevel ?? Aff.Identity;
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
            // local by definition - and so does a copy of one, made under its
            // parent. Anything else is a pack-created object, which the
            // runtime places in world space.
            bool composed = inNpcSubtree || o.IsNpcRoot || o.Bind || o.IsCopy;

            Aff local = NodeAff(o);
            // The NPCs root grafts onto the level's own NPCs object, and the plugin sets a scale there only when it is
            // not exactly 1: at 1 the object keeps the game's (0.96 in the level every pack place is made from).
            // Measured in the game (S17, after 1.10.0): a place's characters - their wet drops with them - stood 1/0.96
            // as far apart, and as large, in the preview as in the game while the node said 1, as a new place's does.
            if (o.IsNpcRoot && o.ScaleX == 1f && o.ScaleY == 1f && npcsKeptScale != 1.0)
                local = Aff.Trs(o.X, o.Y, 0, 0, o.RotationZ, npcsKeptScale, npcsKeptScale);
            // A pack's own object stands at its X and Y in the world, sized as a child of the level's root:
            // its place taken into the level's units, its turn and size left as they are (see WorldToLevel).
            Aff world = composed ? parentWorld.Then(local)
                      : new Aff(local.A, local.B, local.C, local.D,
                                toLevel.A * local.Tx + toLevel.C * local.Ty + toLevel.Tx,
                                toLevel.B * local.Tx + toLevel.D * local.Ty + toLevel.Ty);
            Aff frameParent = composed ? parentWorld : toLevel;
            bool npc = composed;

            bool conditional = parentConditional || o.Model.ActiveConditions.Count > 0;
            string? choice = parentPicks ? path : parentChoice;
            bool picks = PicksAtRandom(o.Model);
            into.Add(new SceneEntry
            {
                Node = o, Path = path, Depth = depth,
                InNpcSubtree = npc, World = world, ParentWorld = frameParent,
                ParentInactive = parentInactive, Conditional = conditional, Choice = choice,
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
                bool npcConditional = conditional || pl.Model.ActiveConditions.Count > 0;
                string? npcChoice = picks ? npcPath : choice;
                into.Add(new SceneEntry
                {
                    Npc = pl, Path = npcPath, Depth = depth + 1,
                    InNpcSubtree = npc, World = world, ParentWorld = world,
                    ParentInactive = childrenInactive, Conditional = npcConditional, Choice = npcChoice,
                    Guides = ancestorHasMore.ToArray(), IsLast = npcLast,
                });
                // GameObjects parented under the NPC ride along with its body,
                // so they compose from the body's world transform.
                ancestorHasMore.Add(!npcLast);
                WalkScene(pl.Children, npcPath, depth + 2,
                          world.Then(LeafAff(pl.Body)), true,
                          childrenInactive || !pl.StartActive, ancestorHasMore, into, npcConditional, npcChoice,
                          worldToLevel: toLevel);
                ancestorHasMore.RemoveAt(ancestorHasMore.Count - 1);
            }

            WalkScene(o.Children, path, depth + 1, world, npc, childrenInactive,
                      ancestorHasMore, into, conditional, choice, picks, toLevel);
            ancestorHasMore.RemoveAt(ancestorHasMore.Count - 1);
        }
    }

    /// <summary>Maps a leaf's world affine + sprite pixel size to the WPF matrix
    /// that places its (0..w, 0..h) pixel rect on the canvas — including the
    /// Unity→canvas Y flip and the level/sprite px-per-unit ratio. Exact for any
    /// rotation / mirror / non-uniform scale (no scale/rotation decomposition).
    /// <paramref name="spritePpu"/> is the sprite's authored pixels-per-unit:
    /// NPC art loads at 100, GameObject sprites at the level's own 70.32 (so
    /// one sprite pixel is one canvas pixel). <paramref name="pivotX"/>, <paramref name="pivotY"/>: where the sprite
    /// is pivoted, as a share of its rect from its bottom-left (Unity's own convention; the centre unless a vanilla
    /// sprite says otherwise, 1.9.2).</summary>
    private Matrix LeafMatrix(in Aff w, double spx, double spy, double spritePpu = NpcSpritePpu,
                              double pivotX = 0.5, double pivotY = 0.5)
    {
        // World units land on canvas at the camera's rate THROUGH the level
        // root's scale — everything authored under the level inherits it.
        double ppu = WorldPpu;
        double k = ppu / spritePpu;
        // The rect's left and top edges from the pivot, in local units (the centre: -w/2, +h/2).
        double left = -pivotX * spx / spritePpu;
        double top = (1.0 - pivotY) * spy / spritePpu;
        // Top-left pixel maps to local unit (left, top).
        double wx = w.A * left + w.C * top + w.Tx;
        double wy = w.B * left + w.D * top + w.Ty;
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

        var placements = _scene.Where(e => e.Npc != null && !e.Hidden && !LeftOutOnArrival(e, e.Npc.StartActive)).ToList();
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
                       + "  (" + pl.Npc + ")\n" + Loc.F("preview.place.sortingOrder", "order", bodyOrder)
                       + (pl.StartActive ? "" : "\n" + Loc.T("preview.place.startsInactive"))
                       + (entry.ParentInactive ? "\n" + Loc.T("preview.place.parentInactive") : "");

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
                TrackParallax(ell, entry.ParallaxGain, entry);
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
                _enlargeable.Add((img, mBody));
                // The shader draws into its own smaller buffer, so the Image
                // keeps the pose's layout size and WPF scales the result — the
                // object stays exactly where and how big it was.
                if (Shaders && !string.IsNullOrWhiteSpace(def.Mask))
                    TrackShader(img, Path.Combine(root, Normalize(def.Sprite)),
                                Path.Combine(root, Normalize(def.Mask)), def.Model.Jiggle, mBody);
                var plForClick = pl;
                var plPathForClick = entry.Path;
                img.MouseLeftButtonDown += (_, e) => { SelectPlacement(plForClick, plPathForClick); e.Handled = true; };
                drawables.Add((bodyOrder, NpcBodyTie, img));
                _drawnNames[img] = string.IsNullOrWhiteSpace(pl.Name) ? pl.Npc : pl.Name;
                TrackParallax(img, entry.ParallaxGain, entry);
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
                        ToolTip = tip + "\n" + Loc.F("preview.place.reflection",
                                "offset", def.ReflectionOffsetY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                                "tint", string.IsNullOrWhiteSpace(def.ReflectionTint) ? Loc.T("preview.place.noTint") : def.ReflectionTint),
                    };
                    RenderOptions.SetBitmapScalingMode(rimg, BitmapScalingMode.HighQuality);
                    _enlargeable.Add((rimg, mRefl));
                    drawables.Add((def.ReflectionSortingOrder, NpcBodyTie, rimg));
                    TrackParallax(rimg, entry.ParallaxGain, entry);

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
                        TrackParallax(tintRect, entry.ParallaxGain, entry);
                    }
                }
            }

            // The game's wet drops (PlacePreview.Wet, WetDrops): the Wet object's place and turn from the whole
            // chain, its size from its own scale alone. Not drawn when the game would not show them on arrival.
            if (def.WetEnabled && (def.WetStartActive || !AsTheGameShows))
            {
                var ww = body.Then(LeafAff(pl.Wet));
                AddSpray(pl, def.Model.Wet, ww, SpraySeed(pl.Name + "/" + pl.Npc, placements.IndexOf(entry)),
                         def.WetStartActive ? ghost : ghost * 0.4, entry.ParallaxGain);
            }
        }
    }

    /// <summary>Decode-once bitmap loader (NPC poses + overlay sprites). A full
    /// rebuild happens on every transform edit / gizmo drag, so decoding the
    /// 2048×1136 level-overlay PNGs each time was a big hidden cost — cache them.</summary>
    private static BitmapSource? LoadCachedBitmap(string abs)
    {
        if (_bitmapCache.TryGetValue(abs, out var cached)) return cached;
        var bmp = File.Exists(abs) ? TryLoad(abs) : null;
        _bitmapCache[abs] = bmp;
        _bitmapOrder.Enqueue(abs);
        _bitmapBytes += BytesOf(bmp);
        while (_bitmapBytes > BitmapsKept && _bitmapOrder.Count > 1)
            if (_bitmapCache.Remove(_bitmapOrder.Dequeue(), out var gone)) _bitmapBytes -= BytesOf(gone);
        return bmp;
    }

    // The pictures kept, oldest first, and what they take decoded (after 1.10.0). Kept without end until then: every
    // picture of every place and game level shown stayed decoded, and the check drawing the game's 146 levels in one
    // process grew past 13 GB. A place shows far less than this; the oldest go first past it.
    private static readonly Queue<string> _bitmapOrder = new();
    private static long _bitmapBytes;
    private const long BitmapsKept = 768L * 1024 * 1024;

    private static long BytesOf(BitmapSource? b) => b == null ? 0 : (long)b.PixelWidth * b.PixelHeight * Math.Max(1, (b.Format.BitsPerPixel + 7) / 8);

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

    private void OnNpcItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The picked character's wet panel shows what it takes from its NPC (its kind, the box's size for it): read
        // again when the NPC's own changes. Others read it again when they are picked.
        if (e.PropertyName?.StartsWith("Wet", System.StringComparison.Ordinal) == true
            && sender is NpcViewModel n && _selPlacement is { } pl && pl.Npc == n.Key)
            pl.NpcWetChanged();
        QueueRebuild();
    }

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

    internal enum GizmoMode { Move, Rotate, Scale }
    internal enum GizmoPart { Body, Shadow, Blink, Wet, Reflection }
    internal enum GizmoHandle { None, MoveX, MoveY, MoveFree, RotZ, RotX, RotY, ScaleX, ScaleY, ScaleUniform,
                                LightOuter, LightInner, LightOuterAngle, LightInnerAngle, LightPoint, LightEdge,
                                WetAreaLeft, WetAreaRight, WetAreaTop, WetAreaBottom,
                                EffectMove, EffectSize }

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

    /// <summary>
    /// The reflection's vertical nudge, which is not a transform like the
    /// other parts and does not live in the same place.
    /// <para/>
    /// Two differences the toolbar has to respect. It is ONE number — the
    /// mirror is dropped a full pose height automatically, and this is the
    /// correction on top — so there is no X, no rotation and no scale to drag.
    /// And it belongs to the NPC DEFINITION rather than the placement, so
    /// dragging it here moves the reflection under that NPC in every room it
    /// stands in. The gizmo title says so; without that the edit looks local
    /// and is not.
    /// </summary>
    private sealed class NpcReflectionTarget : IGizmoTarget
    {
        private readonly NpcViewModel _def;
        public NpcReflectionTarget(NpcViewModel def) => _def = def;

        // X is pinned: the reflection sits directly under the pose by
        // construction, and a sideways nudge has nowhere to be stored.
        public double X { get => 0; set { } }
        public double Y
        {
            get => _def.ReflectionOffsetY;
            set => _def.ReflectionOffsetY = (float)value;
        }
        public double RotZ { get => 0; set { } }
        public double RotX { get => 0; set { } }
        public double RotY { get => 0; set { } }
        public double ScaleX { get => 1; set { } }
        public double ScaleY { get => 1; set { } }
        public bool CanRotate => false;
        public bool CanScale => false;
        public bool SupportsTilt => false;
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
    private WrapPanel _partRow = new(), _modeRow = new();
    private StackPanel _gizmoBody = new();
    private TextBlock _gizmoHint = new();
    private Button _btnBody = new(), _btnShadow = new(), _btnBlink = new(), _btnWet = new();
    /// <summary>Only shown when the selected placement's NPC has its
    /// reflection switched on — there is nothing to drag otherwise.</summary>
    private Button _btnReflection = new();
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
    /// <summary>The part chosen with the chips: what <see cref="_selPart"/> is while the character offers it. A
    /// reflection whose NPC no longer has one (another NPC chosen) shows the body meanwhile, and comes back with it -
    /// with Ctrl+Z most of all (after 1.10.0, the walk over every control: the NPC taken back, the part stayed Body).</summary>
    private GizmoPart _partChosen = GizmoPart.Body;
    private GizmoMode _gizmoMode = GizmoMode.Move;

    private IGizmoTarget? _gizmoTarget;
    private GizmoFrame _gizmoFrame;

    // Drag state (captured on the gizmo layer).
    private GizmoHandle _activeHandle = GizmoHandle.None;
    private Point _dragStartMouse, _dragOrigin;
    private Vector _dragUx, _dragUy;
    private double _startX, _startY, _startRotZ, _startRotX, _startRotY, _startScaleX, _startScaleY;

    // ── Selection ───────────────────────────────────────────────────────

    /// <summary>
    /// Pick an object, or a character standing in the place, as a click on it in the picture or on its row
    /// picks it: the row and where it is in the tree, handed to the same method. False when it is not in the
    /// place. (For tests: the mouse has no place on the desktop they run on.)
    /// </summary>
    internal bool Pick(object row)
    {
        foreach (var entry in BuildScene())
        {
            if (entry.Node != null && ReferenceEquals(entry.Node, row)) { SelectNode(entry.Node, entry.Path); return true; }
            if (entry.Npc != null && ReferenceEquals(entry.Npc, row)) { SelectPlacement(entry.Npc, entry.Path); return true; }
        }
        return false;
    }

    /// <summary>Whether something is picked and has handles to drag.</summary>
    internal bool HasHandles => _gizmoTarget != null;

    /// <summary>What is picked: a character standing in the place, an object, or nothing - for tests.</summary>
    internal object? PickedRow => (object?)_selPlacement ?? _selNode;

    private void SelectPlacement(NpcPlacementViewModel pl, string path)
    {
        _lostNodePath = _lostPlacementPath = null;
        _pickedFrom = GameObjects;
        _selPlacement = pl;
        _selPlacementPath = path;
        _selPlacementTwin = TwinInScene(pl);
        (_selPlacementAt, _selPlacementCount) = OrdinalInScene(pl);
        _selNode = null;
        UpdateGizmoToolbar();
        RefreshGizmo();
        RefreshObjectMenu();
    }

    /// <summary>Select a GameObject node (sprite object, container, or the forced
    /// NPCs root) — all three edit through the same Move / Rotate / Scale
    /// handles, in whatever frame their parent chain resolves to.</summary>
    /// <summary>Pick an object of the scene, as a click on it does (a tutorial step, 1.10.0). False when the
    /// object is not in this preview's scene.</summary>
    internal bool PickNode(GameObjectViewModel o) => PickRow(o);

    /// <summary>Pick an object or a character standing in the place, as a click on it does: what a tutorial step or a
    /// validation issue asks for, the settings of an object being beside the picture only. False when it is not in
    /// this preview's scene.</summary>
    internal bool PickRow(object row)
    {
        FlushRebuild();
        foreach (var e in _scene)
        {
            if (e.Node != null && ReferenceEquals(e.Node, row)) { SelectNode(e.Node, e.Path); return true; }
            if (e.Npc != null && ReferenceEquals(e.Npc, row)) { SelectPlacement(e.Npc, e.Path); return true; }
        }
        return false;
    }

    private void SelectNode(GameObjectViewModel o, string path)
    {
        _lostNodePath = _lostPlacementPath = null;
        _pickedFrom = GameObjects;
        _selNode = o;
        _selNodePath = path;
        _selNodeTwin = TwinInScene(o);
        (_selNodeAt, _selNodeCount) = OrdinalInScene(o);
        _selPlacement = null;
        UpdateGizmoToolbar();
        RefreshGizmo();
        RefreshObjectMenu();
    }

    private void Deselect()
    {
        if (_activeHandle != GizmoHandle.None) return;   // don't drop an active drag
        _lostNodePath = _lostPlacementPath = null;
        _selPlacement = null;
        _selNode = null;
        _gizmoTarget = null;
        _gizmoLayer.Children.Clear();
        UpdateGizmoToolbar();
        RefreshObjectMenu();
    }

    /// <summary>
    /// The <paramref name="twin"/>-th object (or character) at <paramref name="path"/>, else the first there. A
    /// place can have two objects of one name side by side, and their path alone names the first: a place's
    /// "Lamp" picture and "Lamp" light, the light picked, and the next look the preview took picked the picture
    /// instead; renaming the picture moved the pick back to the light (found by the walk over every control,
    /// after 1.10.0, as the panel beside the picture grew and shrank by a light's settings under the keyboard).
    /// </summary>
    private static SceneEntry? AtPath(List<SceneEntry> scene, string path, int twin, bool npc)
    {
        int seen = 0;
        foreach (var e in scene)
        {
            if ((npc ? e.Npc == null : e.Node == null) || e.Path != path) continue;
            if (seen++ == twin) return e;
        }
        return null;
    }

    /// <summary>
    /// Whether the views of what is shown may have been made again since the pick: an undo or a redo being put
    /// back, or the objects shown being another list (a pack read again, the editor's language changed). Then
    /// the pick is found again where it was in the tree. Otherwise only the object itself is the pick: one
    /// taken out of the place is unpicked, not swapped for another of its name.
    /// </summary>
    private bool ViewsMadeAgain => _undoing || !ReferenceEquals(GameObjects, _pickedFrom);

    /// <summary>How many objects (or characters) of the same path come before <paramref name="entry"/>.</summary>
    private static int TwinOf(List<SceneEntry> scene, SceneEntry entry)
    {
        int n = 0;
        foreach (var e in scene)
        {
            if (ReferenceEquals(e, entry)) return n;
            if ((entry.Npc != null ? e.Npc != null : e.Node != null) && e.Path == entry.Path) n++;
        }
        return 0;
    }

    /// <summary>Which of the objects (or characters) at the picked one's path it is: see <see cref="AtPath"/>.</summary>
    private int _selNodeTwin, _selPlacementTwin, _lostNodeTwin, _lostPlacementTwin;

    /// <summary>
    /// Where the picked one stands among the scene's objects (or characters), and how many there were: what finds it
    /// again after an undo that took back what it is called - a name typed in the panel beside the picture, or the NPC
    /// a character with no name of its own goes by (after 1.10.0, found by the walk over every control once it picked
    /// things in the preview). The undo makes the row and its record again, and its path of names is the one the undo
    /// took away, so neither finds it; nothing has moved, though. Only while there are as many as there were: one an
    /// undo took out of the place is not swapped for its neighbour.
    /// </summary>
    private int _selNodeAt = -1, _selNodeCount, _selPlacementAt = -1, _selPlacementCount;

    /// <summary>The <paramref name="at"/>-th object (or character) of the scene, when it has <paramref name="count"/>.</summary>
    private static SceneEntry? AtOrdinal(List<SceneEntry> scene, int at, int count, bool npc)
    {
        if (at < 0) return null;
        int n = 0;
        SceneEntry? found = null;
        foreach (var e in scene)
        {
            if (npc ? e.Npc == null : e.Node == null) continue;
            if (n == at) found = e;
            n++;
        }
        return n == count ? found : null;
    }

    /// <summary>Where <paramref name="entry"/> stands among the scene's objects (or characters), and how many there are.</summary>
    private static (int At, int Count) OrdinalOf(List<SceneEntry> scene, SceneEntry entry)
    {
        bool npc = entry.Npc != null;
        int n = 0, at = -1;
        foreach (var e in scene)
        {
            if (npc ? e.Npc == null : e.Node == null) continue;
            if (ReferenceEquals(e, entry)) at = n;
            n++;
        }
        return (at, n);
    }

    /// <summary>Its place among the scene's objects (or characters) as last walked, for a pick being made.</summary>
    private (int At, int Count) OrdinalInScene(object picked)
    {
        var entry = _scene.FirstOrDefault(e => ReferenceEquals(e.Node, picked) || ReferenceEquals(e.Npc, picked));
        return entry == null ? (-1, 0) : OrdinalOf(_scene, entry);
    }

    /// <summary>Its twin number in the scene as last walked, for a pick being made.</summary>
    private int TwinInScene(object picked)
    {
        var entry = _scene.FirstOrDefault(e => ReferenceEquals(e.Node, picked) || ReferenceEquals(e.Npc, picked));
        return entry == null ? 0 : TwinOf(_scene, entry);
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
            // The object itself while it is in the place, renamed or not; else the one where it was in the tree,
            // for the views an undo or a redo made again.
            var entry = scene.FirstOrDefault(e => ReferenceEquals(e.Node, _selNode))
                     ?? (ViewsMadeAgain ? AtPath(scene, _selNodePath, _selNodeTwin, npc: false)
                                          ?? AtOrdinal(scene, _selNodeAt, _selNodeCount, npc: false) : null);
            if (entry?.Node == null) return false;
            _pickedFrom = GameObjects;
            _selNode = entry.Node;
            _selNodePath = entry.Path;
            _selNodeTwin = TwinOf(scene, entry);
            (_selNodeAt, _selNodeCount) = OrdinalOf(scene, entry);
            target = new OverlayTarget(entry.Node);
            frame = FrameFromParent(entry.ParentWorld);
            return true;
        }

        var pe = scene.FirstOrDefault(e => ReferenceEquals(e.Npc, _selPlacement))
              ?? (ViewsMadeAgain ? AtPath(scene, _selPlacementPath, _selPlacementTwin, npc: true)
                                   ?? AtOrdinal(scene, _selPlacementAt, _selPlacementCount, npc: true) : null);
        if (pe?.Npc == null) return false;
        _pickedFrom = GameObjects;
        _selPlacement = pe.Npc;
        _selPlacementPath = pe.Path;
        _selPlacementTwin = TwinOf(scene, pe);
        (_selPlacementAt, _selPlacementCount) = OrdinalOf(scene, pe);
        // The part chosen while the character offers it (_partChosen): a reflection whose NPC has none now shows the
        // body. An undo or a redo puts that NPC there before the toolbar has looked, and the character was taken for
        // gone - unpicked (after 1.10.0, the walk over every control: a character's NPC brought back with Ctrl+Y).
        if (_selPart == GizmoPart.Reflection && DefFor(_selPlacement) is not { ReflectionEnabled: true }) _selPart = GizmoPart.Body;

        if (_selPart == GizmoPart.Body)
        {
            target = new NpcPartTarget(_selPlacement.Body);
            frame = FrameFromParent(pe.ParentWorld);
        }
        else if (_selPart == GizmoPart.Reflection)
        {
            // Not a placement part: the offset lives on the NPC definition, so
            // the chip is only offered when that definition can be resolved and
            // has its reflection switched on.
            var def = DefFor(_selPlacement);
            if (def == null || !def.ReflectionEnabled) return false;
            target = new NpcReflectionTarget(def);
            frame = FrameFromParent(pe.ParentWorld.Then(LeafAff(_selPlacement.Body)));
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

    /// <summary>The NPC definition a placement points at, or null when its
    /// npc key names nothing in the catalog (an unfinished placement).</summary>
    private NpcViewModel? DefFor(NpcPlacementViewModel? pl)
    {
        if (pl == null || string.IsNullOrEmpty(pl.Npc)) return null;
        return NpcCatalog?.Cast<object>().OfType<NpcViewModel>()
            .FirstOrDefault(n => n.Key == pl.Npc);
    }

    /// <summary>The handles' frame: a parent's transform (in the level's units) on the canvas, through
    /// <see cref="WorldPpu"/> as everything is drawn (1.10.0: through the camera's 100 px a unit before, so an
    /// object's handle sat 1/0.79 as far from the middle as the object it moves, and a drag ran ahead of it).</summary>
    private GizmoFrame FrameFromParent(in Aff p) => new(
        new Point(WorldOriginX + p.Tx * WorldPpu, WorldOriginY - p.Ty * WorldPpu),
        new Vector(p.A * WorldPpu, -p.B * WorldPpu),
        new Vector(p.C * WorldPpu, -p.D * WorldPpu));

    private bool _deselectScheduled;

    private void RefreshGizmo()
    {
        FlushRebuild();
        // Toolbar first. Which chips apply can change without the selection
        // changing - switching an NPC's reflection off is the case that matters,
        // and its chip would otherwise sit there until something else was
        // clicked, offering a part that no longer exists.
        UpdateGizmoToolbar();

        bool hasSelection = _selPlacement != null || _selNode != null;
        var nodeWas = _selNode;
        var placementWas = _selPlacement;
        string pathWas = _selNodePath, placementPathWas = _selPlacementPath;
        if (hasSelection && TryResolveSelection(out var target, out var frame))
        {
            // The place's views were made again (an undo, a redo) and the picked object is the new one at the
            // same place in the tree: what is beside the picture shows the new one too (1.10.0). It was left on
            // the object that had been replaced - the light's panel most of all: after every undo its sliders
            // showed the light as it was before the undo, and moving one changed a light that was no longer the
            // place's.
            if (!ReferenceEquals(nodeWas, _selNode) || !ReferenceEquals(placementWas, _selPlacement)
                || !ReferenceEquals(_lightPanel.DataContext, _selPlacement == null ? _selNode?.Light : null)
                || !ReferenceEquals(_settings.Content, (object?)_selPlacement ?? _selNode)
                // Renamed (its name box is right under the title now, after 1.10.0): the title says the new
                // name, not the one it had a keystroke ago.
                || pathWas != _selNodePath || placementPathWas != _selPlacementPath)
                UpdateGizmoToolbar();
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
            else
            {
                // Gone from the place: removed, or an undo took it out. Kept by where it was, for an undo or
                // a redo that puts it back to pick it again (PickAgainAfterUndo) - not when it went with the
                // place itself, another one shown or another pack opened.
                bool samePlace = _undoing || ReferenceEquals(GameObjects, _pickedFrom);
                string? node = _selNode != null && samePlace ? _selNodePath : null;
                string? placed = _selPlacement != null && samePlace ? _selPlacementPath : null;
                int nodeTwin = _selNodeTwin, placedTwin = _selPlacementTwin;
                Deselect();
                _lostNodePath = node;
                _lostPlacementPath = placed;
                _lostNodeTwin = nodeTwin;
                _lostPlacementTwin = placedTwin;
            }
        }));
    }

    // ── Drawing ─────────────────────────────────────────────────────────

    private void DrawGizmo()
    {
        _gizmoLayer.Children.Clear();
        WetBoxesShown = 0;
        EffectBoxDrawn = null;
        if (_gizmoTarget == null) return;
        // An object's light or effect picked (PlacePreview.ObjectParts): its handles alone, the object's out of the way.
        if (DrawObjectPart()) return;

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

        // The picked object's light (1.10.0), over its other handles - unless the light is a part of its own to pick
        // (an object with a picture or an effect as well): then the object's handles are the object's alone.
        if (_selNode == null || !LightIsApart(_selNode)) DrawLightHandles();
        // A character's Wet part: the box its drops start in (PlacePreview.Wet).
        DrawWetBox();
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

    // The mouse's three moments - a handle taken, dragged, let go - are each a method of their own that the
    // mouse's handler calls with where the pointer is (1.10.0): a test drags a handle through the very same
    // code, with a place of its own for the pointer. (The desktop tests run on has no mouse.)

    private void StartHandle(GizmoHandle h, MouseButtonEventArgs e)
    {
        if (_gizmoTarget == null) return;
        // Pull keyboard focus here: it commits (and undo-checkpoints) any field
        // that was mid-edit, and means the post-drag Ctrl+Z reaches the app
        // rather than a still-focused text box's own undo. Without moving the page.
        TakeKeyboard();
        BeginHandle(h, e.GetPosition(_canvas));
        _gizmoLayer.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>A handle taken, the pointer at <paramref name="at"/> in the picture's own units.</summary>
    internal void BeginHandle(GizmoHandle h, Point at)
    {
        if (_gizmoTarget == null) return;
        _activeHandle = h;
        _dragStartMouse = at;
        _dragOrigin = _gizmoFrame.OriginFor(_gizmoTarget.X, _gizmoTarget.Y);
        _dragUx = _gizmoFrame.Ux; _dragUy = _gizmoFrame.Uy;
        _startX = _gizmoTarget.X; _startY = _gizmoTarget.Y;
        _startRotZ = _gizmoTarget.RotZ; _startRotX = _gizmoTarget.RotX; _startRotY = _gizmoTarget.RotY;
        _startScaleX = _gizmoTarget.ScaleX; _startScaleY = _gizmoTarget.ScaleY;
        if (IsLightHandle(h)) StartLightHandle();
        if (IsWetSide(h)) StartWetSide();
        if (IsEffectHandle(h)) StartEffectHandle();
    }

    private void OnGizmoMouseMove(object? sender, MouseEventArgs e)
    {
        if (_activeHandle == GizmoHandle.None || _gizmoTarget == null) return;
        DragHandleTo(e.GetPosition(_canvas));
        e.Handled = true;
    }

    /// <summary>The handle that is held, dragged to <paramref name="p"/>.</summary>
    internal void DragHandleTo(Point p)
    {
        if (_activeHandle == GizmoHandle.None || _gizmoTarget == null) return;
        var t = _gizmoTarget;
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
            case GizmoHandle.ScaleX: t.ScaleX = _startScaleX * AxisScaleFactor(_dragOrigin, _dragStartMouse, p, _dragUx); break;
            case GizmoHandle.ScaleY: t.ScaleY = _startScaleY * AxisScaleFactor(_dragOrigin, _dragStartMouse, p, _dragUy); break;
            case GizmoHandle.ScaleUniform:
                double f = UniformScaleFactor(_dragStartMouse, p, _dragUx, _dragUy);
                t.ScaleX = _startScaleX * f; t.ScaleY = _startScaleY * f;
                break;
            default:
                if (IsLightHandle(_activeHandle)) DragLightHandle(p);
                else if (IsWetSide(_activeHandle)) DragWetSide(p);
                else if (IsEffectHandle(_activeHandle)) DragEffectHandle(p);
                break;
        }
    }

    private void OnGizmoMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_activeHandle == GizmoHandle.None) return;
        _gizmoLayer.ReleaseMouseCapture();
        e.Handled = true;
        EndHandle();
    }

    /// <summary>The handle let go: the drag is one undo step.</summary>
    internal void EndHandle()
    {
        if (_activeHandle == GizmoHandle.None) return;
        _activeHandle = GizmoHandle.None;
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

    /// <summary>
    /// What dragging an axis handle from <paramref name="grab"/> to
    /// <paramref name="mouse"/> multiplies the scale by: how far along the axis
    /// the pointer is from the centre now, against how far it was. The handle
    /// sits at the end of its arm, so a drag the arm's length doubles it.
    /// </summary>
    internal static double AxisScaleFactor(Point origin, Point grab, Point mouse, Vector axis)
    {
        Vector u = axis; if (u.Length < 1e-6) return 1; u.Normalize();
        double projNow = (mouse - origin).X * u.X + (mouse - origin).Y * u.Y;
        double projStart = (grab - origin).X * u.X + (grab - origin).Y * u.Y;
        if (Math.Abs(projStart) < 1.0) return 1;
        return projNow / projStart;
    }

    /// <summary>
    /// What dragging the square in the middle multiplies the scale by: up and
    /// to the right grows, down and to the left shrinks, at the rate an axis
    /// handle does - a drag the length of an arm doubles it.
    /// <para/>
    /// It measured the pointer's distance from the centre against where it
    /// was grabbed, which is the square itself - a few pixels from the centre,
    /// so the same drag that nudged an axis handle multiplied the size several
    /// times over (2026-09-27). Never below a hundredth: the square makes
    /// things bigger or smaller, and a mirror image is the axis handles' to make.
    /// </summary>
    internal static double UniformScaleFactor(Point grab, Point mouse, Vector ux, Vector uy)
    {
        Vector along = Unit(ux) + Unit(uy);
        if (along.Length < 1e-6) along = new Vector(1, -1);   // up and right, on screen
        along.Normalize();
        Vector drag = mouse - grab;
        double f = 1 + (drag.X * along.X + drag.Y * along.Y) / GizmoAxisLen;
        return Math.Max(0.01, f);
    }

    private static Vector Unit(Vector v)
    {
        if (v.Length < 1e-6) return new Vector(0, 0);
        v.Normalize();
        return v;
    }

    // ── Toolbar ─────────────────────────────────────────────────────────

    private void BuildGizmoToolbar()
    {
        _gizmoTitle = new TextBlock { Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };

        _btnBody = MakeChip(Chip("preview.gizmo.body"), () => SetPart(GizmoPart.Body));
        _btnShadow = MakeChip(Chip("preview.gizmo.shadow"), () => SetPart(GizmoPart.Shadow));
        _btnBlink = MakeChip(Chip("preview.gizmo.blink"), () => SetPart(GizmoPart.Blink));
        _btnWet = MakeChip(Chip("preview.gizmo.wet"), () => SetPart(GizmoPart.Wet));
        _btnReflection = MakeChip(Chip("preview.gizmo.reflection"), () => SetPart(GizmoPart.Reflection));
        // Rows that wrap, so the panel beside the picture can be narrow.
        _partRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var b in new[] { _btnBody, _btnShadow, _btnBlink, _btnWet, _btnReflection })
            _partRow.Children.Add(b);

        _btnMove = MakeChip(Chip("preview.gizmo.move"), () => SetMode(GizmoMode.Move));
        _btnRotate = MakeChip(Chip("preview.gizmo.rotate"), () => SetMode(GizmoMode.Rotate));
        _btnScale = MakeChip(Chip("preview.gizmo.scale"), () => SetMode(GizmoMode.Scale));
        _modeRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var b in new[] { _btnMove, _btnRotate, _btnScale }) _modeRow.Children.Add(b);

        var close = MakeChip(new TextBlock { Text = "✕" }, Deselect);
        // A chip as the others are, where they are: it was the theme's button, the width of the panel, its
        // light cross on a light button in a light theme.
        Paint(close, false);
        close.HorizontalAlignment = HorizontalAlignment.Left;

        _gizmoBody = new StackPanel();
        _gizmoBody.Children.Add(_gizmoTitle);
        _gizmoBody.Children.Add(_partRow);
        // An object's own, its light and its effect (PlacePreview.ObjectParts).
        _gizmoBody.Children.Add(BuildObjectPartRow());
        _gizmoBody.Children.Add(_modeRow);
        _gizmoBody.Children.Add(BuildLightPanel());
        _gizmoBody.Children.Add(BuildEffectPanel());
        // How a character drifts (after 1.10.0): PlacePreview.Drift.
        var characterDrift = BuildDriftField(_characterDrift, CharacterTips, "panel:characterDrift", "btn:characterDriftBack");
        characterDrift.Margin = new Thickness(0, 2, 0, 2);
        _gizmoBody.Children.Add(characterDrift);
        // Then everything its record below has (after 1.10.0): PlacePreview.Settings.
        _gizmoBody.Children.Add(BuildSettings());
        _gizmoBody.Children.Add(close);

        // Said while nothing is picked, so the panel beside the picture is
        // never an empty box (it stays, so the picture does not move).
        _gizmoHint = new TextBlock { Foreground = ChipText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = 0.9 };
        LocText.Bind(_gizmoHint, TextBlock.TextProperty, "preview.place.selectHint");

        var stack = new StackPanel();
        // A character standing in the place (1.10.0): chosen here, whatever is picked.
        stack.Children.Add(BuildStandInRow());
        stack.Children.Add(_gizmoHint);
        stack.Children.Add(_gizmoBody);

        _gizmoToolbar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x22, 0x24, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x5A, 0x60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            // Docked to the right of the picture, as tall as it, scrolling.
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = stack,
            },
        };
        UpdateGizmoToolbar();
    }

    /// <summary>A chip's word for <paramref name="key"/>, bound rather than set:
    /// the toolbar is built once, and a word set here would stay in whatever
    /// language the editor had when the preview first loaded.</summary>
    private static TextBlock Chip(string key)
    {
        var label = new TextBlock();
        LocText.Bind(label, TextBlock.TextProperty, key);
        return label;
    }

    private static Button MakeChip(TextBlock label, System.Action onClick)
    {
        // An explicit light foreground so the app's implicit (theme) TextBlock
        // colour can't turn it dark-on-dark.
        label.Foreground = ChipText;
        var b = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            MinWidth = 0,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    internal void SetPart(GizmoPart part)
    {
        _selPart = _partChosen = part;
        UpdateGizmoToolbar();
        RefreshGizmo();
    }

    internal void SetMode(GizmoMode mode)
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
        // Beside the picture whenever the hierarchy is, picked or not.
        _gizmoToolbar.Visibility = _objectMenu.Visibility;
        bool picked = _selPlacement != null || _selNode != null;
        _gizmoHint.Visibility = picked ? Visibility.Collapsed : Visibility.Visible;
        _gizmoBody.Visibility = picked ? Visibility.Visible : Visibility.Collapsed;
        UpdateLightPanel();
        UpdateEffectPanel();
        UpdateSettings();
        if (!picked) return;

        bool isNpc = _selPlacement != null;
        _partRow.Visibility = isNpc ? Visibility.Visible : Visibility.Collapsed;
        UpdateObjectParts();
        // A light's or an effect's handles are their own: moving, turning and sizing are the object's.
        _modeRow.Visibility = isNpc || PickedObjectPart == ObjectPart.Object ? Visibility.Visible : Visibility.Collapsed;

        // The reflection is the NPC's, not this placement's, and dragging it
        // moves it in every room that NPC stands in. Saying so in the title is
        // the only warning there is — the drag itself looks local.
        bool reflectable = isNpc && DefFor(_selPlacement) is { ReflectionEnabled: true };
        _btnReflection.Visibility = reflectable ? Visibility.Visible : Visibility.Collapsed;
        _selPart = _partChosen == GizmoPart.Reflection && !reflectable ? GizmoPart.Body : _partChosen;

        _gizmoTitle.Text = !isNpc
            ? Loc.F("preview.place.selectedObject", "path", _selNodePath)
            : _selPart == GizmoPart.Reflection
                ? Loc.F("preview.place.selectedReflection", "npc", _selPlacement!.Npc)
                : Loc.F("preview.place.selectedNpc", "npc", string.IsNullOrWhiteSpace(_selPlacement!.Name) ? _selPlacement.Npc : _selPlacement.Name);

        Paint(_btnBody, _selPart == GizmoPart.Body);
        Paint(_btnShadow, _selPart == GizmoPart.Shadow);
        Paint(_btnBlink, _selPart == GizmoPart.Blink);
        Paint(_btnWet, _selPart == GizmoPart.Wet);
        Paint(_btnReflection, _selPart == GizmoPart.Reflection);

        // A reflection has one number to it — a vertical nudge — so Rotate and
        // Scale would be handles that move nothing. Force Move rather than
        // leave a mode selected that silently does nothing.
        bool transformable = _selPart != GizmoPart.Reflection;
        if (!transformable) _gizmoMode = GizmoMode.Move;

        Paint(_btnMove, _gizmoMode == GizmoMode.Move);
        Paint(_btnRotate, _gizmoMode == GizmoMode.Rotate);
        Paint(_btnScale, _gizmoMode == GizmoMode.Scale);
        _btnRotate.IsEnabled = transformable;
        _btnScale.IsEnabled = transformable;
    }

    private static void Paint(Button b, bool on)
    {
        b.Background = on ? ChipOn : ChipOff;
        b.Foreground = Brushes.White;
    }

    // ── Object menu (left-side GO list) ─────────────────────────────────

    /// <summary>
    /// The hierarchy, docked to the left of the picture (the author, 1.8.0: it
    /// covered the picture's top-left). Not resized by hand any more: as tall
    /// as the picture, scrolling, and as wide as its longest name - children
    /// included - unless the picture is made wider than the room, when it
    /// gives way (see MeasureOverride). A bigger picture shows more of it.
    /// </summary>
    private void BuildObjectMenu()
    {
        _objectMenuList = new StackPanel();

        _objectMenu = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x22, 0x24, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x5A, 0x60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            Child = _objectMenuScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _objectMenuList,
                // A click on its empty room leaves the keyboard on the preview, for its keys.
                Focusable = false,
            },
        };
        // A click in the hierarchy puts the keyboard on the preview, for its keys: its rows are made again on every
        // change, and the keyboard went with the row clicked.
        _objectMenu.PreviewMouseLeftButtonDown += (_, _) => TakeKeyboard();
        RefreshObjectMenu();
    }

    /// <summary>The list's own scroller: the row of + buttons wraps at what it shows (see AddLightButton).</summary>
    private ScrollViewer? _objectMenuScroll;

    /// <summary>Rebuild the left-side hierarchy: every GameObject and NPC in the
    /// place, in tree order and indented by depth, highlighting the current gizmo
    /// selection. Clicking a row selects that object.</summary>
    private void RefreshObjectMenu()
    {
        FlushRebuild();
        _objectMenuList.Children.Clear();

        var scene = _scene.Count > 0 ? _scene : BuildScene();
        // Where a place's objects are made and set, there being nowhere else (after 1.10.0: the GameObjects list
        // below the preview went): shown with no art yet behind them, and with no object yet, so + Object can make
        // the first. A preview of no place's objects (Objects unset) only lists what it draws.
        bool shown = Objects != null || (scene.Count > 0 && _placeholder.Visibility != Visibility.Visible);
        _objectMenu.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _gizmoToolbar.Visibility = _objectMenu.Visibility;
        if (!shown) return;

        _objectMenuList.Children.Add(new TextBlock
        {
            Text = Loc.T("preview.place.hierarchy"), FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Brushes.White,
            Margin = new Thickness(2, 0, 0, 3),
            ToolTip = Objects != null ? Loc.T("preview.place.keys.tip") : null,
        });
        _addRow = null;
        if (AddLightButton() is FrameworkElement addLight)
        {
            _addRow = addLight;
            foreach (var chip in addLight is Panel adds ? adds.Children.OfType<Button>() : Enumerable.Empty<Button>()) chip.Focusable = false;
            _objectMenuList.Children.Add(addLight);
        }

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
                // ◈ the forced NPCs root, ▦ a sprite object, ☼ a light, ✦ an effect (1.10.0), ⌗ a bare container.
                string icon = node.IsNpcRoot ? "◈" : !string.IsNullOrWhiteSpace(node.Sprite) ? "▦"
                            : node.HasLight || node.LightRemoved ? "☼" : node.HasEffect ? "✦" : "⌗";
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
        AddBinRows();
        // As wide as the widest row of every place shown (PlacePreview.Panels).
        AddWidestRow();
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
    // What every row shares, made once (1.10.0): a font and three inks were made anew for every row of every
    // rebuild, and setting them was most of a rebuild's time (the Places tab's first visit, UiDelayTests).
    private static readonly FontFamily RowMono = new("Consolas, Courier New, monospace");   // English on purpose: fonts' names.
    private static readonly SolidColorBrush RowInk = Frozen(Color.FromArgb(0xE6, 0xB0, 0xB6, 0xBE));
    private static readonly SolidColorBrush RowHiddenByParentInk = Frozen(Color.FromArgb(0x66, 0xB0, 0xB6, 0xBE));
    private static readonly SolidColorBrush RowHiddenInk = Frozen(Color.FromArgb(0x77, 0xB0, 0xB6, 0xBE));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

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
                FontFamily = RowMono,
                // Dimmer than the labels: structure should be readable without
                // competing with the names for attention. 90%, not the 60% it
                // was: on the panel's dark grey that measured 2.97:1 (1.8.0).
                Foreground = RowInk,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        // Fold arrow, or a spacer of the same width on a leaf so every label
        // starts at the same column regardless.
        var arrow = new TextBlock
        {
            Text = onToggle == null ? "  " : collapsed ? "▸ " : "▾ ",
            FontFamily = RowMono,
            Foreground = ChipText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (onToggle != null)
        {
            arrow.Cursor = Cursors.Hand;
            arrow.ToolTip = Loc.T("preview.place.fold.tip");
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
                FontFamily = RowMono,
                Foreground = entry.HiddenBySelf ? ChipText
                           : entry.Hidden ? RowHiddenByParentInk
                           : RowInk,   // shown: readable (was 3.31:1)
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = entry.HiddenBySelf ? Loc.T("preview.place.hidden.tip")
                        : entry.Hidden ? Loc.T("preview.place.hiddenByParent.tip")
                        : Loc.T("preview.place.hide.tip"),
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
                ? RowHiddenInk : ChipText,
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
            // The keyboard stays on the preview, for its keys: the row is made again on every change.
            Focusable = false,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Decode a PNG to premultiplied BGRA at its own size, which is the
    /// buffer shape the shader port expects.</summary>
    private static byte[]? LoadBgraAtNative(string abs, out int w, out int h, bool cached = true)
    {
        w = h = 0;
        // Masks bypass the cache. It is keyed by path with no invalidation, so
        // a mask re-saved from the editor would keep previewing at its old
        // content for the rest of the session. They are 256px and read once per
        // rebuild rather than per frame, so decoding afresh costs nothing worth
        // protecting — unlike the level art, which is 2048px and stays cached.
        var src = cached ? LoadCachedBitmap(abs) : (File.Exists(abs) ? TryLoad(abs) : null);
        if (src == null) return null;
        w = src.PixelWidth; h = src.PixelHeight;
        return CopyBgra(src, w, h);
    }

    /// <summary>Decode a PNG and resample it onto a given grid — used to put a
    /// jiggle mask on its pose's pixel grid.</summary>
    private static byte[]? LoadBgraScaled(string abs, int w, int h)
    {
        // Uncached for the same reason as above — this only ever loads masks.
        var src = File.Exists(abs) ? TryLoad(abs) : null;
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

    private static BitmapSource? LoadOverlay() => _cachedOverlay ??= LoadOverlayFile("GameplayUI.png");
    private static BitmapSource? LoadOverlayExtended() => _cachedOverlayExtended ??= LoadOverlayFile("GameplayUIExtended.png");
    // ButtonNavigator.png is gone: the button is drawn from the game's own
    // "Semi Rounded", nine-sliced, with its disc and rule sprites over it.

    private static BitmapSource? LoadOverlayFile(string fileName)
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

    private static FontFamily NumberFont() => _numberFont ??= LoadFont("Barton.otf", "Segoe UI");   // English on purpose: font names.
    private static FontFamily LabelFont() => _labelFont ??= LoadFont("Curse Casual.ttf", "Segoe UI");   // English on purpose: a font file's name.

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

    // ── The level's own art, decoded once per version of the file (1.10.0) ─────────────────────────
    //
    // A place's front and back art (2048x1136) was decoded afresh on every refresh, on the interface's thread: a
    // tenth of a second and more each time, which was most of the Places tab's first visit. Kept now by the file's
    // size and time of writing, so art the author saves again is read again and the same art is not; a few levels'
    // worth (each about 9 MB decoded). PrefetchArt decodes the art a pack is likely to show first on a worker as the
    // pack opens.

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapSource?> _artCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _artOrder = new();
    private const int ArtKept = 12;

    private static string ArtKey(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks : path + "|-";
    }

    /// <summary>The art at <paramref name="path"/>, from the cache when this version of the file was read before.</summary>
    internal static BitmapSource? LoadArt(string path)
    {
        string key = ArtKey(path);
        if (_artCache.TryGetValue(key, out var known)) return known;
        var bmp = File.Exists(path) ? TryLoad(path) : null;
        if (_artCache.TryAdd(key, bmp))
        {
            _artOrder.Enqueue(key);
            while (_artOrder.Count > ArtKept && _artOrder.TryDequeue(out var oldest)) _artCache.TryRemove(oldest, out _);
        }
        return bmp;
    }

    /// <summary>Decode these files' art on a worker, so the place they belong to shows at once when chosen.</summary>
    internal static System.Threading.Tasks.Task PrefetchArt(IEnumerable<string> paths)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(ArtKept).ToList();
        return System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var p in list)
            {
                try { LoadArt(p); }
                catch (Exception) { /* a picture that cannot be read ahead is read, and reported, when shown */ }
            }
        });
    }

    private static BitmapSource? TryLoad(string path)
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

            return Rendering.VanillaArtSizes.RestoreIfThumbnail(path, img);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

