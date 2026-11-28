using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;

namespace SMSModForge.View.Controls;

/// <summary>
/// An NPC's wet drops in the place preview (after 1.10.0, the author: "Could we implement a proper preview of the wet
/// effects? Currently we have only an approximation that doesn't at all look like the game's"): the game's own drops
/// (<see cref="WetDrops"/>), where the game has them at every moment.
/// <para/>
/// With the game's look on (its lights or its post) they are in the look's picture, blended over the lit place as
/// the game blends them (PlaceLit.hlsl on the card, GamePost.LookOverView on the processor). With it off they are
/// drawn by WPF on a layer of their own over the scene - every drop over everything a level draws, as the game's
/// Bust sorting layer is. Either way nothing is kept per drop: each frame works out the few that are out from the
/// time alone, so a place full of wet NPCs costs a handful of pictures a frame. They do not move under tests.
/// </summary>
public sealed partial class PlacePreview
{
    /// <summary>One Wet object's drops, drawn by WPF while the game's look is off.</summary>
    private sealed class WetSpray : FrameworkElement
    {
        public WetDrops.Emitter Emitter;
        public PlacePreview Owner = null!;
        /// <summary>The character whose Wet object this is: what its outline is drawn for when picked.</summary>
        public ViewModel.NpcPlacementViewModel? Of;

        protected override void OnRender(DrawingContext dc) => Owner.DrawSpray(dc, this);
    }

    /// <summary>A picked Wet part's drops, each outlined where it is now - with the handles, over the look as over the
    /// WPF layer.</summary>
    private sealed class WetDropMarks : FrameworkElement
    {
        public WetSpray Spray = null!;
        public PlacePreview Owner = null!;

        protected override void OnRender(DrawingContext dc) => Owner.DrawDropMarks(dc, this);
    }

    /// <summary>The drops while the game's look is off: over the scene, under the interface. Hidden while the look is
    /// shown, whose picture has them in it.</summary>
    private readonly Canvas _wetLayer = new() { Width = CanvasWidth, Height = CanvasHeight, IsHitTestVisible = false, ClipToBounds = false };

    private readonly List<WetSpray> _sprays = new();
    private readonly System.Diagnostics.Stopwatch _wetClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<WetDrops.Drop> _dropScratch = new();
    private bool _wetTicking;

    /// <summary>The drops' moment: as after five seconds of them from the first frame, then the clock's; the same
    /// every time under tests (or what a test sets).</summary>
    internal double WetTime => WetClockForTests?.Invoke()
                               ?? (Services.TestMode.Active ? WetDrops.TestTime : WetDrops.Steady + _wetClock.Elapsed.TotalSeconds);

    /// <summary>
    /// The moment the drops are drawn at: <see cref="WetTime"/> in steps of a sixtieth of a second. Drops move all the
    /// time, so a place with nothing else moving is drawn again for them alone: at sixty a second, not at the display's
    /// own rate (165 on the author's main monitor) - the look and the WPF layer alike stay put between two steps
    /// (measured on SMSAndroids' bathroom: working out its three showers' drops takes about a microsecond a frame; it
    /// is the place drawn again that costs).
    /// </summary>
    internal double WetStep => Math.Floor(WetTime * StepsASecond) / StepsASecond;

    private const double StepsASecond = 60;
    private double _wetStepShown = double.NaN;

    /// <summary>A test's own clock for the drops (the performance test lets them fall).</summary>
    internal Func<double>? WetClockForTests { get; set; }

    /// <summary>How many sprays the preview draws - for tests.</summary>
    internal int SprayCount => _sprays.Count;

    /// <summary>Each spray's character (its name in the place) and emitter, where the preview has it on the canvas - for
    /// tests (S17 holds them to the game's).</summary>
    internal IReadOnlyList<(string Name, WetDrops.Emitter Emitter)> Emitters => _sprays.ConvertAll(s => (s.Of?.Name ?? "", s.Emitter));

    private sealed class Inverse : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>The layer over the scene: called once, from the constructor.</summary>
    private void AddWetLayer()
    {
        _wetLayer.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = _lookImage, Converter = new Inverse() });
        _canvas.Children.Add(_wetLayer);
    }

    private void ClearSprays()
    {
        _wetLayer.Children.Clear();
        _sprays.Clear();
    }

    /// <summary>
    /// A Wet object of an NPC: <paramref name="ww"/> its transform (the whole chain, Unity's units of the level), its
    /// own scale - the only one the game's drops take (scaling Local; the shower's times its own 1.053) - its seed, how
    /// see-through it is drawn (an NPC off when the place opens is drawn fainter, as the rest of it), and the
    /// placement's effect (<see cref="Model.WetEffectDef"/>). Both go on the WPF layer over everything (the game's
    /// shower sorts at 15 on the Default layer, over all but a place's very front); with the look on, the shower's are
    /// lit with the place (WetDrops.Drop.Lit).
    /// </summary>
    private void AddSpray(ViewModel.NpcPlacementViewModel of, Model.NpcWetDef? npcWet, in Aff ww, uint seed, double opacity, double parallaxGain)
    {
        // Its NPC's look with what the placement sets over it (after 1.10.0: "NPC sets them, placements can change").
        var look = of.EffectiveWet(npcWet);
        bool shower = Model.WetEffectDef.ShowerKind(look.Kind);
        double own = shower ? WetDrops.ShowerOwnScale : 1.0;
        double ownScaleX = of.Wet.ScaleX * own, ownScaleY = of.Wet.ScaleY * own;
        var (tr, tg, tb, ta) = TintOf(look.Tint ?? "");
        // Where it is: in the level's units, under the level's root, as its character is drawn (WorldPpu). Until the
        // author asked whether the bathroom's drops matched the game (after 1.10.0) this was the camera's 100 px a unit,
        // which put a Wet object 1/0.79 as far from the middle as its character - S17 drew the game's own drops, so
        // it never saw where the preview put them. Its size stays the camera's: the game scales a spray by its own
        // object alone (WetDrops).
        double ppu = WorldPpu;
        var ax = new Vector(ww.A * ppu, -ww.B * ppu);   // +1 local X on the canvas
        var ay = new Vector(ww.C * ppu, -ww.D * ppu);   // +1 local Y (up) on the canvas
        if (ax.Length < 1e-9) ax = new Vector(1, 0); else ax.Normalize();
        if (ay.Length < 1e-9) ay = new Vector(0, -1); else ay.Normalize();
        var emitter = new WetDrops.Emitter(WorldOriginX + ww.Tx * ppu, WorldOriginY - ww.Ty * ppu, ax.X, ax.Y, ay.X, ay.Y,
                                           ownScaleX, ownScaleY, seed, opacity,
                                           look.Width ?? Model.WetEffectDef.DefaultWidth(look.Kind), look.Height ?? Model.WetEffectDef.DefaultHeight(look.Kind),
                                           shower ? WetDrops.Kind.Shower : WetDrops.Kind.Towel,
                                           shower && look.FromTop == true, shower && look.Floor == true, tr, tg, tb, ta,
                                           look.Amount ?? 1f, look.Size ?? 1f, look.Fall ?? 1f);
        var spray = new WetSpray { Emitter = emitter, Owner = this, Of = of, IsHitTestVisible = false };
        // The game draws a drop thinner than a pixel as the pixels whose middles it covers: whole pixels or none.
        if (shower) RenderOptions.SetEdgeMode(spray, EdgeMode.Aliased);
        else RenderOptions.SetBitmapScalingMode(spray, BitmapScalingMode.Linear);
        _wetLayer.Children.Add(spray);
        _sprays.Add(spray);
        TrackParallax(spray, parallaxGain);
    }

    /// <summary>A placement's tint (#RRGGBB or #RRGGBBAA, straight sRGB): white when unset or not a colour.</summary>
    private static (float R, float G, float B, float A) TintOf(string tint)
    {
        try { return Rendering.BustComposer.ParseTint(tint); }
        catch (FormatException) { return (1f, 1f, 1f, 1f); }
        catch (ArgumentException) { return (1f, 1f, 1f, 1f); }
    }

    private static bool IsShower(WetSpray s) => s.Emitter.Of == WetDrops.Kind.Shower;

    private static readonly Brush WetBoxBrush = MakeWetBoxBrush();

    private static Brush MakeWetBoxBrush()
    {
        var b = new SolidColorBrush(Color.FromRgb(0x9F, 0xE0, 0xFF));
        b.Freeze();
        return b;
    }

    /// <summary>How many wet outlines the handles show - for tests.</summary>
    internal int WetBoxesShown { get; private set; }

    /// <summary>The outline's corners on the canvas, as last drawn - for tests.</summary>
    internal (double X, double Y)[]? WetBoxDrawn { get; private set; }

    /// <summary>The picked Wet part's drop outlines, while it is picked - for tests.</summary>
    internal FrameworkElement? DropMarksShown => _dropMarks?.Parent == _gizmoLayer ? _dropMarks : null;

    private WetDropMarks? _dropMarks;

    /// <summary>How far a drop's outline stands off the drop, canvas pixels: the drop itself stays clear.</summary>
    internal const double DropMarkGap = 6;

    private static readonly Pen DropMarkHalo = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), 4));
    private static readonly Pen DropMarkLine = Frozen(new Pen(WetBoxBrush, 2));

    private static Pen Frozen(Pen p)
    {
        p.Freeze();
        return p;
    }

    /// <summary>
    /// With a character's Wet part picked: the box its drops start in, outlined over the handles (after 1.10.0, the
    /// author: "When selecting the wet effect on NPCs, can we get a box outline of its bounds?"). The game's box: 3.21
    /// by 8.45 units times the Wet object's own scale, turned with it - the drops fall straight down from there.
    /// </summary>
    private void DrawWetBox()
    {
        WetBoxesShown = 0;
        WetBoxDrawn = null;
        _wetSides.Clear();
        if (_selPlacement == null || _selPart != GizmoPart.Wet) return;
        foreach (var s in _sprays)
        {
            if (!ReferenceEquals(s.Of, _selPlacement)) continue;
            var c = WetDrops.Box(s.Emitter);
            var figure = new PathFigure { StartPoint = new Point(c[0].X, c[0].Y), IsClosed = true };
            for (int i = 1; i < c.Length; i++) figure.Segments.Add(new LineSegment(new Point(c[i].X, c[i].Y), true));
            var box = new PathGeometry(new[] { figure });
            box.Freeze();
            // With the spray as it drifts (Preview parallax).
            var drift = s.RenderTransform;
            _gizmoLayer.Children.Add(new System.Windows.Shapes.Path { Data = box, Stroke = Brushes.Black, StrokeThickness = 4, Opacity = 0.5,
                                                IsHitTestVisible = false, RenderTransform = drift });
            _gizmoLayer.Children.Add(new System.Windows.Shapes.Path { Data = box, Stroke = WetBoxBrush, StrokeThickness = 2, IsHitTestVisible = false,
                                                StrokeDashArray = new DoubleCollection { 5, 4 }, RenderTransform = drift });
            WetBoxesShown++;
            WetBoxDrawn = c;
            // And each of its drops where it is now (the author: "Might also be helpful to outline the actual droplets
            // when we have the wet GO selected as well. It's a bit hard to see them"): drawn again with every step.
            // The towel's few: the shower's four hundred a box fill it, and are plain to see.
            if (!IsShower(s))
            {
                _dropMarks = new WetDropMarks { Spray = s, Owner = this, IsHitTestVisible = false, RenderTransform = drift };
                _gizmoLayer.Children.Add(_dropMarks);
            }
            AddWetSides(c, s.Emitter, drift);
        }
    }

    // ── The box's sides, dragged ────────────────────────────────────────
    // The author (after 1.10.0): "we could be able to just drag the edges of the effect to increase or decrease its
    // span. It dragging shouldn't be an aspect ratio scaling, but just pulling the actual side clicked. Also, make
    // sure we're not actually changing the droplet size by doing that, just the area of the effect." So a side
    // changes the placement's area (the game's particle shape scale) - never the Wet object's scale, which sizes the
    // drops too - and the side across from it stays where it is: the Wet object, the box's middle, goes half as far.

    /// <summary>The smallest a dragged side leaves the area, units: a side pulled past the other stops there.</summary>
    internal const double MinWetArea = 0.05;

    private const double WetGrip = 14;

    private static bool IsWetSide(GizmoHandle h)
        => h is GizmoHandle.WetAreaLeft or GizmoHandle.WetAreaRight or GizmoHandle.WetAreaTop or GizmoHandle.WetAreaBottom;

    /// <summary>The middle of each side, as the handles have it - for tests, which take a side there.</summary>
    internal IReadOnlyDictionary<GizmoHandle, Point> WetSidesDrawn => _wetSides;

    private readonly Dictionary<GizmoHandle, Point> _wetSides = new();

    /// <summary>
    /// Each side of the box <paramref name="c"/> (WetDrops.Box's corners: top left, top right, bottom right, bottom
    /// left of the Wet object's own frame) as a handle: a grip at its middle, the whole side to take.
    /// </summary>
    private void AddWetSides((double X, double Y)[] c, in WetDrops.Emitter e, Transform drift)
    {
        _wetSides.Clear();
        var sides = new[] { (GizmoHandle.WetAreaTop, 0, 1), (GizmoHandle.WetAreaRight, 1, 2),
                            (GizmoHandle.WetAreaBottom, 2, 3), (GizmoHandle.WetAreaLeft, 3, 0) };
        foreach (var (h, a, b) in sides)
        {
            var from = new Point(c[a].X, c[a].Y);
            var to = new Point(c[b].X, c[b].Y);
            var middle = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            var outward = SideOutward(h, e);
            var cursor = Math.Abs(outward.X) >= Math.Abs(outward.Y) ? Cursors.SizeWE : Cursors.SizeNS;
            var grip = new System.Windows.Shapes.Rectangle { Width = WetGrip, Height = WetGrip, Fill = WetBoxBrush, Stroke = Brushes.Black,
                                                              StrokeThickness = 1.5, RenderTransform = drift };
            Canvas.SetLeft(grip, middle.X - WetGrip / 2); Canvas.SetTop(grip, middle.Y - WetGrip / 2);
            var hit = new System.Windows.Shapes.Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = Brushes.Transparent,
                                                       StrokeThickness = GizmoHit, RenderTransform = drift };
            Attach(hit, h); Attach(grip, h);
            hit.Cursor = grip.Cursor = cursor;
            // Under the other handles: where a side crosses an arrow, the arrow is taken.
            _gizmoLayer.Children.Insert(0, hit);
            _gizmoLayer.Children.Add(grip);
            _wetSides[h] = middle;
        }
    }

    /// <summary>Which way on the canvas side <paramref name="h"/> faces out of the box: its own axis, mirrored with
    /// the Wet object's scale on that axis.</summary>
    private static Vector SideOutward(GizmoHandle h, in WetDrops.Emitter e)
    {
        bool alongX = h is GizmoHandle.WetAreaLeft or GizmoHandle.WetAreaRight;
        double sign = h is GizmoHandle.WetAreaRight or GizmoHandle.WetAreaTop ? 1 : -1;
        double scale = alongX ? e.ScaleX : e.ScaleY;
        var axis = alongX ? new Vector(e.AxX, e.AxY) : new Vector(e.AyX, e.AyY);
        return axis * (scale < 0 ? -sign : sign);
    }

    private WetDrops.Emitter? _wetDragFrom;
    private double _startAreaW, _startAreaH;

    /// <summary>A side taken: the box as it was, to drag from.</summary>
    private void StartWetSide()
    {
        _wetDragFrom = null;
        if (_selPlacement == null) return;
        foreach (var s in _sprays)
            if (ReferenceEquals(s.Of, _selPlacement)) { _wetDragFrom = s.Emitter; break; }
        _startAreaW = _wetDragFrom?.AreaW ?? _selPlacement.WetAreaWidth;
        _startAreaH = _wetDragFrom?.AreaH ?? _selPlacement.WetAreaHeight;
    }

    /// <summary>
    /// The side held, pulled to <paramref name="p"/>: the drawn box grows or shrinks along that side's axis by as far
    /// as the pointer went out or in, and the side across stays. Across the width the Wet object moves half that way.
    /// Up and down, the towel's box is its two systems' together - the second's lower by
    /// <see cref="WetDrops.SecondShift"/> of the height - so the drawn height is the area's times one and that share,
    /// and the Wet object (the first's middle) keeps the side across by moving as far as that side is from it.
    /// </summary>
    private void DragWetSide(Point p)
    {
        if (_selPlacement == null || _gizmoTarget == null || _wetDragFrom is not { } e) return;
        bool alongX = _activeHandle is GizmoHandle.WetAreaLeft or GizmoHandle.WetAreaRight;
        double perUnit = Math.Abs(alongX ? e.ScaleX : e.ScaleY) * WetDrops.PixelsPerUnit;   // canvas px a unit of area
        if (perUnit < 1e-6) return;
        var outward = SideOutward(_activeHandle, e);
        Vector dm = p - _dragStartMouse;
        double pulled = (dm.X * outward.X + dm.Y * outward.Y) / perUnit;   // own units
        double moved;   // how far the Wet object goes out of the side held, own units
        double width = _startAreaW, height = _startAreaH;
        if (alongX)
        {
            width = Math.Max(MinWetArea, _startAreaW + pulled);
            moved = (width - _startAreaW) / 2;
        }
        else
        {
            var (_, _, low, high) = WetDrops.Extent(e with { AreaH = 1 });   // the drawn box's ends a unit of height
            double tall = high - low;
            height = Math.Max(MinWetArea, (_startAreaH * tall + pulled) / tall);
            // The side across, from the Wet object: the drawn bottom when the top is held, the top when the bottom is.
            bool top = _activeHandle == GizmoHandle.WetAreaTop;
            moved = top ? -low * (height - _startAreaH) : high * (height - _startAreaH);
        }
        Vector shift = outward * (moved * perUnit);
        double det = _dragUx.X * _dragUy.Y - _dragUx.Y * _dragUy.X;
        if (Math.Abs(det) < 1e-12) return;
        _gizmoTarget.X = _startX + (shift.X * _dragUy.Y - shift.Y * _dragUy.X) / det;
        _gizmoTarget.Y = _startY + (_dragUx.X * shift.Y - _dragUx.Y * shift.X) / det;
        _selPlacement.SetWetArea((float)width, (float)height);
    }

    /// <summary>A rectangle round each drop the spray has out now, <see cref="DropMarkGap"/> clear of it.</summary>
    private void DrawDropMarks(DrawingContext dc, WetDropMarks m)
    {
        if (m.Spray.Visibility != Visibility.Visible) return;   // no drops drawn for it (CollectDrops)
        _dropScratch.Clear();
        var (l, t, r, b) = DropArea();
        WetDrops.At(DropMarksAt, m.Spray.Emitter, _dropScratch, l, t, r, b);
        foreach (var d in _dropScratch)
        {
            var around = new Rect(d.X - d.W / 2 - DropMarkGap, d.Y - d.H / 2 - DropMarkGap, d.W + 2 * DropMarkGap, d.H + 2 * DropMarkGap);
            dc.DrawRectangle(null, DropMarkHalo, around);
            dc.DrawRectangle(null, DropMarkLine, around);
        }
    }

    /// <summary>A spray's seed: its NPC's place in the scene, so its drops stay where they are when the place is
    /// built again.</summary>
    private static uint SpraySeed(string key, int index)
    {
        uint h = 2166136261;
        foreach (char ch in key) h = (h ^ ch) * 16777619;
        return (h ^ (uint)index) * 16777619;
    }

    /// <summary>The canvas around which drops are worth working out: the screen, and as far again round it (a view
    /// zoomed out or panned past the screen).</summary>
    private static (double L, double T, double R, double B) DropArea()
        => (-CanvasWidth, -CanvasHeight, 2 * CanvasWidth, 2 * CanvasHeight);

    private void DrawSpray(DrawingContext dc, WetSpray s)
    {
        _dropScratch.Clear();
        var (l, t, r, b) = DropArea();
        WetDrops.At(WetStep, s.Emitter, _dropScratch, l, t, r, b);
        if (IsShower(s))
        {
            // Solid quads of their colour (the shower's material has no texture).
            foreach (var d in _dropScratch)
                dc.DrawRectangle(DropBrush(d), null, new Rect(d.X - d.W / 2, d.Y - d.H / 2, d.W, d.H));
            return;
        }
        var mips = WetDrops.Texture;
        if (mips == null) return;
        // How large a canvas pixel is on the screen: which copy of the texture a drop is drawn from.
        double k = _displayScale * VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (var d in _dropScratch)
        {
            var picture = WetDrops.Picture(mips.LevelFor((float)(d.W * k), (float)(d.H * k)));
            if (picture == null) continue;
            var rect = new Rect(d.X - d.W / 2, d.Y - d.H / 2, d.W, d.H);
            if (d.R >= 1f && d.G >= 1f && d.B >= 1f)
            {
                dc.PushOpacity(d.Alpha);
                dc.DrawImage(picture, rect);
                dc.Pop();
                continue;
            }
            // Tinted: the colour through the texture's alpha.
            dc.PushOpacityMask(new ImageBrush(picture) { Viewport = rect, ViewportUnits = BrushMappingMode.Absolute });
            dc.DrawRectangle(DropBrush(d), null, rect);
            dc.Pop();
        }
    }

    /// <summary>A drop's colour and alpha as a WPF brush (straight sRGB, as WPF blends).</summary>
    private static SolidColorBrush DropBrush(in WetDrops.Drop d)
    {
        static byte B(float v) => (byte)Math.Clamp((int)Math.Round(v * 255f), 0, 255);
        var brush = new SolidColorBrush(Color.FromArgb(B(d.Alpha), B(d.R), B(d.G), B(d.B)));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Every drop out now, where it is drawn (its spray's drift with the cursor in it), into the frame - what the
    /// look blends into the place before its lights (a shower's) or over the lit place (a towel's).
    /// </summary>
    private void CollectDrops(PlaceFrame f)
    {
        if (DropsOverride is { } given)
        {
            for (int i = 0; i < given.Count; i++) f.AddDrop(given.Drops[i]);
            return;
        }
        if (_sprays.Count == 0 || WetDrops.Texture == null) return;
        double time = WetStep;
        _dropsCollectedAt = time;
        var (l, t, r, b) = DropArea();
        foreach (var s in _sprays)
        {
            if (s.Visibility != Visibility.Visible) continue;
            // The drift (TrackParallax) moves the spray: its drops with it, as a system simulating in its own space.
            var m = s.RenderTransform?.Value ?? Matrix.Identity;
            _dropScratch.Clear();
            WetDrops.At(time, s.Emitter, _dropScratch, l - m.OffsetX, t - m.OffsetY, r - m.OffsetX, b - m.OffsetY);
            foreach (var d in _dropScratch)
            {
                var moved = d;
                moved.X += m.OffsetX;
                moved.Y += m.OffsetY;
                f.AddDrop(moved);
            }
        }
    }

    /// <summary>The drops now, as the processor's passes take them; null without any.</summary>
    private WetDrops.Over? DropsNow()
    {
        if (DropsOverride != null) return DropsOverride;
        var f = new PlaceFrame();
        CollectDrops(f);
        return f.DropsOver(CanvasWidth, CanvasHeight);
    }

    /// <summary>The drops move with every display frame while there are any and the preview is shown (never under
    /// tests): the WPF layer is drawn again, and the look sees a new frame.</summary>
    private void SyncWetClock()
    {
        bool want = _sprays.Count > 0 && IsVisible && !Services.TestMode.Active;
        if (want == _wetTicking) return;
        if (want) CompositionTarget.Rendering += OnWetFrame;
        else CompositionTarget.Rendering -= OnWetFrame;
        _wetTicking = want;
    }

    private void OnWetFrame(object? sender, EventArgs e)
    {
        // With the look shown its picture has the drops, and the outlines follow that (ShowLook).
        if (_wetLayer.Visibility != Visibility.Visible) return;
        double step = WetStep;
        if (step == _wetStepShown) return;
        _wetStepShown = step;
        foreach (var s in _sprays) s.InvalidateVisual();
        DropMarksShown?.InvalidateVisual();
    }

    /// <summary>The moment the last drops were worked out at (CollectDrops), and the one in the look's picture on
    /// screen: the look comes in a moment or more after it is started, and a drop falls a long way in that.</summary>
    private double _dropsCollectedAt, _lookDropsAt;

    /// <summary>The look's picture is on screen with drops of <paramref name="at"/>: the outlines go where those are.</summary>
    private void LookDropsShown(double at)
    {
        _lookDropsAt = at;
        DropMarksShown?.InvalidateVisual();
    }

    /// <summary>The moment the outlines are drawn at: the look's picture's, while it is shown.</summary>
    private double DropMarksAt => _lookImage.Visibility == Visibility.Visible ? _lookDropsAt : WetStep;
}
