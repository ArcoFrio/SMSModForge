using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SMSModForge.Model;
using SMSModForge.Rendering;

namespace SMSModForge.View.Controls;

/// <summary>
/// Preview for the UI tab: one vanilla UI base, drawn the way the game draws
/// it.
/// <para/>
/// Composed at the canvas's own resolution — 1920×1080 for almost every surface
/// in the game — and then fitted to whatever room it is given, the same
/// arrangement <see cref="PlacePreview"/> uses. Every coordinate inside is a
/// number the game would recognise, and only the last step is a scale.
/// <para/>
/// The drawing itself is <see cref="UiSceneRenderer"/>, which knows nothing
/// about WPF. This control's whole job is to decide when to run it, put the
/// result on screen, and say plainly when it could not draw something — a
/// preview that quietly omits a sprite is worse than one that refuses to
/// pretend.
/// </summary>
public sealed class UiPreview : Grid
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _message = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(24),
        Visibility = Visibility.Collapsed,
    };

    /// <summary>The handles, in control coordinates, over the picture. A
    /// Canvas with no background of its own, so it takes clicks only where a
    /// handle actually is and lets everything else through.</summary>
    private readonly Canvas _overlay = new() { Background = null };

    private readonly Rectangle _box = new()
    {
        StrokeThickness = 1,
        Stroke = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xF0)),
        StrokeDashArray = new DoubleCollection { 3, 3 },
        // Transparent rather than null: null is not hit-testable, and the
        // inside of the box is the move handle.
        Fill = Brushes.Transparent,
        Cursor = Cursors.SizeAll,
        Visibility = Visibility.Collapsed,
    };

    private readonly Rectangle[] _grips;
    private readonly UiGrip[] _gripKinds =
    {
        UiGrip.Left, UiGrip.Right, UiGrip.Top, UiGrip.Bottom,
        UiGrip.TopLeft, UiGrip.TopRight, UiGrip.BottomLeft, UiGrip.BottomRight,
    };

    public UiPreview()
    {
        // A tooltip that outlived its owner sits over the one thing an
        // author is trying to look at. See ToolTipDismisser.
        View.ToolTipDismisser.KeepClearOf(this);

        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Children.Add(_image);
        Children.Add(_message);

        _grips = _gripKinds.Select(MakeGrip).ToArray();
        _overlay.Children.Add(_box);
        foreach (var grip in _grips) _overlay.Children.Add(grip);
        Children.Add(_overlay);

        _overlay.MouseLeftButtonDown += OverlayDown;
        _overlay.MouseMove += OverlayMove;
        _overlay.MouseLeftButtonUp += OverlayUp;
        _overlay.LostMouseCapture += (_, _) => _dragging = UiGrip.None;

        // The handles are in control pixels, so they move whenever the picture
        // is fitted differently.
        SizeChanged += (_, _) => PlaceGizmo();

        // Follows the editor's theme rather than being painted a fixed colour:
        // a UI surface is mostly transparent, and what shows through it is the
        // background, so a hard-coded one would be a lie at one of the two
        // themes.
        _message.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Muted");

        // Handles belong to the picture, so they stop at its edge. A canvas
        // does not clip its children, so a full-screen object's box and grips
        // were being drawn - and hit - out over the tree and the property
        // panel beside it.
        ClipToBounds = true;

        // The same dark ground the other previews use. A UI surface is mostly
        // transparent, so whatever sits behind it is what an author judges the
        // art against - and the game's own background is dark, not white.
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));

        // Draw once on the way in. Refresh otherwise only runs when BaseToken
        // CHANGES, and its default is the empty string - so a control that is
        // never given one sat there as a blank pane with not even the message
        // saying nothing was selected, which reads as the feature being broken
        // rather than as nothing being chosen.
        Loaded += (_, _) => Refresh();
    }

    // ── What to draw ─────────────────────────────────────────────────

    public static readonly DependencyProperty BaseTokenProperty =
        DependencyProperty.Register(nameof(BaseToken), typeof(string), typeof(UiPreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Which vanilla UI to show, as a catalog token or id — for
    /// example <c>vanillaui:9_MainCanvas/Payout</c>. Empty shows nothing.</summary>
    public string BaseToken
    {
        get => (string)GetValue(BaseTokenProperty);
        set => SetValue(BaseTokenProperty, value);
    }

    public static readonly DependencyProperty AuthoredRootProperty =
        DependencyProperty.Register(nameof(AuthoredRoot), typeof(UiNodeDef), typeof(UiPreview),
            new PropertyMetadata(null, OnInputChanged));

    /// <summary>
    /// The tree to draw. When set, this is what appears — what the PACK will
    /// produce, rather than what the game currently has.
    /// <para/>
    /// That distinction is the whole point of the pane. Drawing the vanilla
    /// screen would show an author what they started from however much they
    /// edited, which is a picture that gets less true the more work goes into
    /// it. With nothing set the control falls back to the vanilla screen named
    /// by <see cref="BaseToken"/>, which is right before anything is authored.
    /// </summary>
    public UiNodeDef? AuthoredRoot
    {
        get => (UiNodeDef?)GetValue(AuthoredRootProperty);
        set => SetValue(AuthoredRootProperty, value);
    }

    public static readonly DependencyProperty HighlightProperty =
        DependencyProperty.Register(nameof(Highlight), typeof(UiNodeDef), typeof(UiPreview),
            new PropertyMetadata(null, OnInputChanged));

    /// <summary>The object to outline, so a tree selection can be seen on the
    /// picture. A UI is a pile of overlapping rectangles and a name in a tree
    /// says nothing about which one.</summary>
    public UiNodeDef? Highlight
    {
        get => (UiNodeDef?)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    // -- Playing an arrival animation ---------------------------------
    //
    // The object being animated is moved and faded INSIDE one render and put
    // straight back, the way the old press preview did it. That is what makes
    // "it will be exactly as it was" a fact rather than a promise: the model is
    // never observably different, because nothing else can run between the
    // change and its undo - not a save, not a redraw, not the tree.
    //
    // The alternative, holding the animated values in the model for the length
    // of the animation, would mean a save landing mid-play writes a screen
    // frozen half-open.

    /// <summary>
    /// The frame to draw, or null for none.
    /// <para/>
    /// ONE property, not one per value. Each dependency property that changes
    /// draws the whole 1920x1080 composite again, so handing a frame over in
    /// three pieces drew it three times - which is why playing an animation
    /// crawled. A frame is one thing; it arrives as one thing.
    /// </summary>
    public UiAnimationFrame? AnimationFrame
    {
        get => (UiAnimationFrame?)GetValue(AnimationFrameProperty);
        set => SetValue(AnimationFrameProperty, value);
    }

    public static readonly DependencyProperty AnimationFrameProperty =
        DependencyProperty.Register(nameof(AnimationFrame), typeof(UiAnimationFrame), typeof(UiPreview),
            new PropertyMetadata(null, OnAnimationFrameChanged));

    /// <summary>
    /// Queued rather than drawn, always.
    /// <para/>
    /// Frames arrive faster than a software composite of this size can be made,
    /// and drawing each one where it lands means the queue only grows. Asking
    /// instead collapses whatever piled up into the next draw - so a slow
    /// machine plays the same animation with fewer frames rather than the same
    /// frames running late, which is the difference between coarse and laggy.
    /// </summary>
    private static void OnAnimationFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((UiPreview)d).RequestRefresh();

    /// <summary>
    /// Put the animation's current frame on, and hand back the undo. The result
    /// must always be called - see DrawAuthored.
    /// </summary>
    private Action ApplyAnimationFrame()
    {
        var frame = AnimationFrame;
        var target = frame?.Target;
        if (frame == null || target == null) return static () => { };

        var undo = new List<Action>();

        if (frame.Alpha < 1.0)
        {
            float? was = target.Alpha;
            target.Alpha = (float)Math.Max(0, frame.Alpha);
            undo.Add(() => target.Alpha = was);
        }

        if (frame.Scale != null && frame.Scale.Length >= 2)
        {
            var was = target.Rect.Scale;
            target.Rect.Scale = new[] { frame.Scale[0], frame.Scale[1] };
            undo.Add(() => target.Rect.Scale = was);
        }

        return () => { for (int i = undo.Count - 1; i >= 0; i--) undo[i](); };
    }

    public static readonly DependencyProperty RevisionProperty =
        DependencyProperty.Register(nameof(Revision), typeof(int), typeof(UiPreview),
            new PropertyMetadata(0, OnInputChanged));

    /// <summary>Bumped by whoever owns the tree every time anything in it
    /// changes. The tree is edited in place, so there is nothing else for a
    /// binding to notice - see UiViewModel.Revision.</summary>
    public int Revision
    {
        get => (int)GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public static readonly DependencyProperty CanvasWidthProperty =
        DependencyProperty.Register(nameof(CanvasWidth), typeof(double), typeof(UiPreview),
            new PropertyMetadata(1920.0, OnInputChanged));

    /// <summary>The canvas an authored tree's anchors are fractions of.</summary>
    public double CanvasWidth
    {
        get => (double)GetValue(CanvasWidthProperty);
        set => SetValue(CanvasWidthProperty, value);
    }

    public static readonly DependencyProperty CanvasHeightProperty =
        DependencyProperty.Register(nameof(CanvasHeight), typeof(double), typeof(UiPreview),
            new PropertyMetadata(1080.0, OnInputChanged));

    public double CanvasHeight
    {
        get => (double)GetValue(CanvasHeightProperty);
        set => SetValue(CanvasHeightProperty, value);
    }

    public static readonly DependencyProperty ShowWholeSurfaceProperty =
        DependencyProperty.Register(nameof(ShowWholeSurface), typeof(bool), typeof(UiPreview),
            new PropertyMetadata(false, OnInputChanged));

    /// <summary>Draw everything on the canvas rather than the one base. Useful
    /// for seeing where a screen sits among the others; misleading as a default,
    /// because the game only ever shows one of them at a time.</summary>
    public bool ShowWholeSurface
    {
        get => (bool)GetValue(ShowWholeSurfaceProperty);
        set => SetValue(ShowWholeSurfaceProperty, value);
    }

    // ── What it managed to draw ──────────────────────────────────────

    private static readonly DependencyPropertyKey ReportKey =
        DependencyProperty.RegisterReadOnly(nameof(Report), typeof(UiRenderReport),
            typeof(UiPreview), new PropertyMetadata(null));

    public static readonly DependencyProperty ReportProperty = ReportKey.DependencyProperty;

    /// <summary>What the last render could not find. Read-only, and worth
    /// putting somewhere an author can see.</summary>
    public UiRenderReport? Report
    {
        get => (UiRenderReport?)GetValue(ReportProperty);
        private set => SetValue(ReportKey, value);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var preview = (UiPreview)d;

        // A drag changes the tree on every mouse move, and the revision it
        // bumps arrives back here. Drawing it straight away would put a whole
        // frame between each move and the next, which is the very thing
        // RequestRefresh exists to avoid.
        if (preview._dragging != UiGrip.None) preview.RequestRefresh();
        else preview.Refresh();
    }

    // ── Drawing ──────────────────────────────────────────────────────

    /// <summary>
    /// Redraw.
    /// <para/>
    /// Not cheap: the assets behind it are cached, but the composition itself
    /// is a full software render of a 1920x1080 surface and measures about
    /// 57ms for a screen the size of Quitagme. That is fine in answer to a
    /// typed number and far too slow in answer to a mouse move, which is why
    /// dragging goes through RequestRefresh instead.
    /// </summary>
    /// <summary>How many times the picture has actually been made. Counted so a
    /// test can show that one frame of an animation costs one draw and not
    /// three, which is the difference between playing and crawling.</summary>
    internal int RendersDone { get; private set; }

    public void Refresh()
    {
        RendersDone++;

        // An authored tree is drawn whatever else is set, because it is the
        // thing the author is working on.
        if (AuthoredRoot != null)
        {
            DrawAuthored(AuthoredRoot);
            return;
        }

        if (!VanillaUiLibrary.IsAvailable)
        {
            Say("The vanilla UI has not been extracted yet.\n\n" +
                "Run Tools ▸ SMSModForge ▸ Extract Vanilla UI Surfaces in the game's " +
                "Unity project, then Export TMP Font Assets, and put the result in " +
                "the editor's VanillaUi folder.");
            return;
        }

        var entry = VanillaUiCatalog.Find(BaseToken);
        if (entry == null)
        {
            Say(string.IsNullOrWhiteSpace(BaseToken)
                ? "Nothing selected."
                : $"There is no vanilla UI called “{BaseToken}”.");
            return;
        }

        var surface = VanillaUiLibrary.SurfaceFor(entry);
        if (surface == null)
        {
            Say($"“{entry.Surface.Path}” is in the catalog but its tree was not " +
                "shipped. The catalog and the surfaces come from the same " +
                "extraction, so one without the other means a partial copy.");
            return;
        }

        if (!surface.IsDrawable)
        {
            // Six of the game's canvases are like this: the Canvas component is
            // switched off, so Unity never gave the RectTransform a size and
            // every rectangle under it measured as a point. Saying so beats
            // showing an empty box that looks like a bug here.
            Say($"“{entry.Surface.Path}” cannot be drawn.\n\n" +
                "Its Canvas component is disabled in the game, so Unity never " +
                "gave it a size and nothing under it has a position to draw at.");
            return;
        }

        var report = new UiRenderReport();
        byte[] pixels;
        if (ShowWholeSurface)
        {
            pixels = UiSceneRenderer.Render(surface, VanillaUiLibrary.Assets, report);
        }
        else
        {
            var node = surface.Base(entry.Id);
            if (node == null)
            {
                Say($"“{entry.Id}” is not on “{entry.Surface.Path}” any more. " +
                    "The catalog and the surface may be from different extractions.");
                return;
            }
            pixels = UiSceneRenderer.RenderBase(surface, node, VanillaUiLibrary.Assets, report);
        }

        Report = report;

        if (pixels.Length == 0) { Say("Nothing was drawn."); return; }

        int w = (int)Math.Round(surface.Width), h = (int)Math.Round(surface.Height);
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null,
                                         pixels, w * 4);
        bitmap.Freeze();      // so it can be handed about without a dispatcher

        _image.Source = bitmap;
        _image.Visibility = Visibility.Visible;
        _message.Visibility = Visibility.Collapsed;
    }

    private void DrawAuthored(UiNodeDef root)
    {
        if (!VanillaUiLibrary.IsAvailable)
        {
            Say("The vanilla UI has not been extracted yet, so there are no " +
                "sprites or fonts to draw with.");
            return;
        }

        var report = new UiRenderReport();

        var restore = ApplyAnimationFrame();
        byte[] pixels;
        try
        {
            pixels = UiAuthoredRenderer.Render(Roots(), CanvasWidth, CanvasHeight,
                                               VanillaUiLibrary.Assets, report,
                                               Highlight);
        }
        finally { restore(); }
        Report = report;

        if (pixels.Length == 0) { Say("Nothing to draw yet."); return; }

        int w = (int)Math.Round(CanvasWidth), h = (int)Math.Round(CanvasHeight);
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null,
                                         pixels, w * 4);
        bitmap.Freeze();

        _image.Source = bitmap;
        _image.Visibility = Visibility.Visible;
        _message.Visibility = Visibility.Collapsed;
        PlaceGizmo();
    }

    /// <summary>What the last render could not find, in one line, or empty when
    /// it found everything.</summary>
    public string Trouble()
    {
        var r = Report;
        if (r == null) return "";
        var parts = new System.Collections.Generic.List<string>();
        if (r.MissingSprites.Count > 0) parts.Add($"{r.MissingSprites.Count} missing sprite(s)");
        if (r.MissingFonts.Count > 0) parts.Add($"{r.MissingFonts.Count} missing font(s)");
        if (r.MissingGlyphs.Count > 0) parts.Add($"{r.MissingGlyphs.Count} unbaked glyph(s)");
        if (r.LegacyText.Count > 0) parts.Add($"{r.LegacyText.Count} legacy text object(s)");
        if (r.Untrustworthy.Count > 0)
            parts.Add($"{r.Untrustworthy.Count} object(s) whose position the game will change");
        return string.Join(", ", parts);
    }

    private void Say(string what)
    {
        _message.Text = what;
        _message.Visibility = Visibility.Visible;
        _image.Visibility = Visibility.Collapsed;
        _image.Source = null;
        Report = null;
        HideGizmo();
    }

    // ── The gizmo ────────────────────────────────────────────────
    //
    // Dragging inside the box moves the object; dragging an edge or a corner
    // resizes it, the way Unity's own rect tool does.
    //
    // Everything here works in the numbers the model actually stores, which are
    // an anchored POSITION and a SIZE DELTA, not a rectangle. Resizing is
    // therefore two changes rather than one: growing the right edge by d adds d
    // to the size, and that would drag the left edge along with it by the
    // pivot's share unless the position also moves by pivotX*d to hold it
    // still. The four edge cases below are that one identity, written out.

    private const double GripSize = 9;

    private UiGrip _dragging = UiGrip.None;
    private bool _moved;
    private Point _dragFrom;
    private UiNodeDef? _dragNode;
    private UiDragStart _dragStart;

    /// <summary>Raised while an object is being dragged, so whatever is showing
    /// its numbers can re-read them. The model is already changed by the time
    /// this fires.</summary>
    public event Action<UiNodeDef>? RectEdited;

    /// <summary>Raised when a click - as opposed to a drag - lands on an
    /// object in the picture. Selecting from the picture is the way out of a
    /// large object covering everything: its box fills the pane, and without
    /// this there is no way to reach anything underneath.</summary>
    public event Action<UiNodeDef>? NodePicked;

    /// <summary>
    /// Raised once, BEFORE a drag changes anything.
    /// <para/>
    /// A drag is not a command and not a text field, so neither of the two
    /// things that normally mark an undo step notices it happening. Without
    /// this, moving an object with the handles is not undoable at all: the next
    /// Ctrl+Z steps back past it to whatever was done before, which looks like
    /// undo skipping a step.
    /// </summary>
    public event Action? EditBeginning;

    private Rectangle MakeGrip(UiGrip kind) => new()
    {
        Width = GripSize,
        Height = GripSize,
        Fill = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xF0)),
        Stroke = Brushes.White,
        StrokeThickness = 1,
        Cursor = CursorFor(kind),
        Visibility = Visibility.Collapsed,
        Tag = kind,
    };

    private static Cursor CursorFor(UiGrip kind) => kind switch
    {
        UiGrip.Left or UiGrip.Right => Cursors.SizeWE,
        UiGrip.Top or UiGrip.Bottom => Cursors.SizeNS,
        UiGrip.TopLeft or UiGrip.BottomRight => Cursors.SizeNWSE,
        UiGrip.TopRight or UiGrip.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.SizeAll,
    };

    /// <summary>The selected object's rectangle in this control's own pixels,
    /// or null when there is nothing to put handles on.</summary>
    private Rect? GizmoRect()
    {
        if (Highlight == null || AuthoredRoot == null) return null;
        if (_image.Source == null || _image.ActualWidth <= 0) return null;
        if (CanvasWidth <= 0 || CanvasHeight <= 0) return null;

        var screen = UiGeometry.ScreenRectOf(AuthoredRoot, Highlight, CanvasWidth, CanvasHeight);
        if (screen == null) return null;

        double scale = _image.ActualWidth / CanvasWidth;
        var origin = _image.TranslatePoint(new Point(0, 0), this);
        var s = screen.Value;
        return new Rect(origin.X + s.X * scale, origin.Y + s.Y * scale,
                        s.Width * scale, s.Height * scale);
    }

    private void PlaceGizmo()
    {
        var found = GizmoRect();
        if (found == null) { HideGizmo(); return; }
        var r = found.Value;

        Canvas.SetLeft(_box, r.X);
        Canvas.SetTop(_box, r.Y);
        _box.Width = Math.Max(1, r.Width);
        _box.Height = Math.Max(1, r.Height);
        _box.Visibility = Visibility.Visible;

        for (int i = 0; i < _grips.Length; i++)
        {
            var at = GripCentre(_gripKinds[i], r);
            Canvas.SetLeft(_grips[i], at.X - GripSize / 2);
            Canvas.SetTop(_grips[i], at.Y - GripSize / 2);
            _grips[i].Visibility = Visibility.Visible;
        }
    }

    private void HideGizmo()
    {
        _box.Visibility = Visibility.Collapsed;
        foreach (var grip in _grips) grip.Visibility = Visibility.Collapsed;
    }

    private static Point GripCentre(UiGrip kind, Rect r) => kind switch
    {
        UiGrip.Left => new Point(r.Left, r.Top + r.Height / 2),
        UiGrip.Right => new Point(r.Right, r.Top + r.Height / 2),
        UiGrip.Top => new Point(r.Left + r.Width / 2, r.Top),
        UiGrip.Bottom => new Point(r.Left + r.Width / 2, r.Bottom),
        UiGrip.TopLeft => new Point(r.Left, r.Top),
        UiGrip.TopRight => new Point(r.Right, r.Top),
        UiGrip.BottomLeft => new Point(r.Left, r.Bottom),
        _ => new Point(r.Right, r.Bottom),
    };

    // ── Hit testing ────────────────────────────────────────
    //
    // The same walk the drawing is, in reverse: the map comes from UiGeometry,
    // which is what positions the pictures, so an object that looks like it is
    // under the cursor IS the one under the cursor. Last drawn is nearest the
    // front, so the search runs backwards.

    /// <summary>What is on display. A list because that is what the renderer
    /// takes; only ever the one tree being edited.</summary>
    private IReadOnlyList<UiNodeDef> Roots()
        => AuthoredRoot == null ? Array.Empty<UiNodeDef>() : new[] { AuthoredRoot };

    /// <summary>
    /// The object under a point, for selecting rather than for clicking.
    /// <para/>
    /// Prefers one that actually draws something. A tree is full of containers
    /// that fill their parent and show nothing, and picking one of those in
    /// front of the thing an author aimed at feels like the click missed.
    /// </summary>
    private UiNodeDef? NodeAt(Point where)
    {
        if (_image.ActualWidth <= 0 || CanvasWidth <= 0) return null;

        double scale = _image.ActualWidth / CanvasWidth;
        var origin = _image.TranslatePoint(new Point(0, 0), this);
        double x = (where.X - origin.X) / scale;
        double y = (where.Y - origin.Y) / scale;

        return UiGeometry.Pick(Roots(), x, y, CanvasWidth, CanvasHeight);
    }

    private void OverlayDown(object sender, MouseButtonEventArgs e)
    {
        var kind = GripOf(e.OriginalSource);
        if (kind == UiGrip.None || Highlight == null) return;

        _dragNode = Highlight;
        _dragging = kind;
        _dragFrom = e.GetPosition(this);

        // A press inside the box has not committed to anything yet: it becomes
        // a move once the pointer travels, and a selection if it does not. A
        // grip is unambiguous and starts moving at once.
        _moved = kind != UiGrip.Move;
        if (_moved) EditBeginning?.Invoke();

        // Captured once, and every move applied from it, so a drag that
        // wanders about and comes back lands exactly where it started.
        var rect = UiGeometry.ScreenRectOf(AuthoredRoot, _dragNode, CanvasWidth, CanvasHeight);
        _dragStart = UiDragStart.Of(_dragNode, rect ?? default);

        _overlay.CaptureMouse();
        e.Handled = true;
    }

    private UiGrip GripOf(object? source)
    {
        if (ReferenceEquals(source, _box)) return UiGrip.Move;
        if (source is Rectangle r && r.Tag is UiGrip kind) return kind;
        return UiGrip.None;
    }

    private void OverlayMove(object sender, MouseEventArgs e)
    {
        if (_dragging == UiGrip.None || _dragNode == null) return;
        if (_image.ActualWidth <= 0 || CanvasWidth <= 0) return;

        double scale = _image.ActualWidth / CanvasWidth;
        if (scale <= 0) return;

        var now = e.GetPosition(this);

        if (!_moved)
        {
            // The system's own threshold, so a click with an unsteady hand
            // stays a click rather than nudging the object a pixel.
            var travelled = now - _dragFrom;
            if (Math.Abs(travelled.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(travelled.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _moved = true;
            EditBeginning?.Invoke();
        }

        UiDrag.Apply(_dragNode, _dragging, _dragStart,
                     (now.X - _dragFrom.X) / scale,
                     -(now.Y - _dragFrom.Y) / scale);   // canvas y counts upwards

        // The handles follow the cursor at once - they cost a walk of the tree
        // and nothing else. The picture is a 1920x1080 software composite at
        // some 57ms a frame, which is slower than the mouse reports, so it is
        // asked for rather than done: see RequestRefresh.
        PlaceGizmo();
        RequestRefresh();
        RectEdited?.Invoke(_dragNode);
        e.Handled = true;
    }

    private void OverlayUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging == UiGrip.None) return;

        bool wasDrag = _moved;
        _dragging = UiGrip.None;
        _dragNode = null;
        _overlay.ReleaseMouseCapture();

        if (!wasDrag)
        {
            // A click, not a drag: pick whatever is actually under the pointer.
            var picked = NodeAt(e.GetPosition(this));
            if (picked != null && !ReferenceEquals(picked, Highlight)) NodePicked?.Invoke(picked);
            e.Handled = true;
            return;
        }

        // One last one, unconditionally, so the picture always ends up showing
        // where the object actually finished rather than the last frame that
        // happened to get drawn.
        Refresh();
        e.Handled = true;
    }

    /// <summary>
    /// Redraw, but not more often than there is time for.
    /// <para/>
    /// Queued below input priority, so every mouse move already waiting is
    /// dealt with first and the intermediate frames are simply skipped. Drawing
    /// each one instead makes the drag lag further behind the cursor the longer
    /// it goes on, because the moves arrive faster than a frame takes.
    /// </summary>
    private void RequestRefresh()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _renderQueued = false;
            Refresh();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private bool _renderQueued;
}
