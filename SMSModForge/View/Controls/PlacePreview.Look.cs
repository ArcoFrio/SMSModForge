using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMSModForge.Rendering;

namespace SMSModForge.View.Controls;

/// <summary>
/// The game's look over a place (1.9.1, the author: "I wanted to see them having
/// shader effects as well, all manually disabled through the checkboxes below
/// it along the already existing ones"): the post-processing the game runs on
/// every frame of every place - Beautify's bloom and light streaks (its
/// anamorphic flares), then its colour (tonemapping, vignette) and URP's
/// grading - over the place as the preview draws it.
/// <para/>
/// The scene layer (the level's art, its objects, its NPCs) is taken twice: the
/// whole screen at the game's size (960x540, where the game's look was measured)
/// for the bloom and the streaks, which are soft, and what the view shows at the
/// view's own pixels for the rest - Beautify's compose and URP's grading read the
/// picture at their own pixel - so zoomed in, the art keeps its own detail
/// (1.9.1, the author: "the zoom on the Places preview is not wired correctly,
/// and even some art from the pack itself looks low res when zoomed in"; the
/// look had been one 960x540 picture, enlarged with the view, and ended at the
/// screen's edge). Put through <see cref="GamePost"/> - the same passes, held to
/// the game's own frames - and drawn over exactly what the view shows, past the
/// screen's edge too, under the interface (the game's HUD frame and map buttons
/// are not post-processed) and the editing handles.
/// Each part has a tick box and is drawn when ticked and the preview quality
/// draws it (Ultra all three, High no streaks, Medium and Low the colour only).
/// A still picture is not processed again: the layer is compared with the
/// last one taken, and looked at less often while it stays the same.
/// </summary>
public sealed partial class PlacePreview
{
    public static readonly DependencyProperty LookBloomProperty =
        DependencyProperty.Register(nameof(LookBloom), typeof(bool), typeof(PlacePreview), new PropertyMetadata(true, OnLookChanged));
    public static readonly DependencyProperty LookFlaresProperty =
        DependencyProperty.Register(nameof(LookFlares), typeof(bool), typeof(PlacePreview), new PropertyMetadata(true, OnLookChanged));
    public static readonly DependencyProperty LookColourProperty =
        DependencyProperty.Register(nameof(LookColour), typeof(bool), typeof(PlacePreview), new PropertyMetadata(true, OnLookChanged));
    public static readonly DependencyProperty LookLightsProperty =
        DependencyProperty.Register(nameof(LookLights), typeof(bool), typeof(PlacePreview), new PropertyMetadata(true, OnLookChanged));

    /// <summary>
    /// The level's own 2D lights (1.9.2): each point light's share of the light
    /// the level's sprites are multiplied by, and its glow (see
    /// <see cref="Rendering.PlaceLights"/>), before the post-processing - the
    /// bloom takes a lamp's brightness as the game's does.
    /// </summary>
    public bool LookLights { get => (bool)GetValue(LookLightsProperty); set => SetValue(LookLightsProperty, value); }

    private PlaceLights? _placeLights;

    /// <summary>The game's unlit sprite material: what is drawn with it, its lamps leave as it is.</summary>
    internal const string UnlitMaterial = "Sprites-Default";

    /// <summary>What the scene draws with the unlit material (1.9.2): the level's own picture when it is
    /// (<see cref="LevelArtUnlit"/>), and the game's objects whose renderer uses it.</summary>
    private readonly System.Collections.Generic.HashSet<UIElement> _unlitElements = new();

    /// <summary>The level's own picture is drawn with the game's unlit material (Sprites-Default): 56 of its levels,
    /// some lit by lamps of intensity 1 that leave the picture as it is.</summary>
    public static readonly DependencyProperty LevelArtUnlitProperty =
        DependencyProperty.Register(nameof(LevelArtUnlit), typeof(bool), typeof(PlacePreview), new PropertyMetadata(false, OnInputChanged));

    public bool LevelArtUnlit { get => (bool)GetValue(LevelArtUnlitProperty); set => SetValue(LevelArtUnlitProperty, value); }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, ImageSource[]> _silhouettes = new();

    /// <summary>A picture's silhouette in white (lit) or black (unlit), its alpha kept: what the lit mask draws.</summary>
    private ImageSource Silhouette(ImageSource source, bool lit)
    {
        bool still = source is not WriteableBitmap;
        if (still && _silhouettes.TryGetValue(source, out var both)) return both[lit ? 0 : 1];
        if (source is not BitmapSource bmp) return source;
        var conv = new FormatConvertedBitmap(bmp, PixelFormats.Pbgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);
        var white = new byte[px.Length];
        var black = new byte[px.Length];
        for (int i = 0; i < px.Length; i += 4)
        {
            byte a = px[i + 3];
            white[i] = white[i + 1] = white[i + 2] = a; white[i + 3] = a;
            black[i + 3] = a;
        }
        var made = new ImageSource[]
        {
            BitmapSource.Create(w, h, bmp.DpiX, bmp.DpiY, PixelFormats.Pbgra32, null, white, w * 4),
            BitmapSource.Create(w, h, bmp.DpiX, bmp.DpiY, PixelFormats.Pbgra32, null, black, w * 4),
        };
        if (still) _silhouettes.AddOrUpdate(source, made);
        return made[lit ? 0 : 1];
    }

    /// <summary>
    /// The scene's lit mask (1.9.2): every picture the world layer draws as a silhouette, white where it is lit and
    /// black where it is drawn with the unlit material, in the scene's own order - captured by <paramref name="render"/>
    /// as the scene is, its pictures put back after. False (and nothing captured) when nothing is unlit: all of it is
    /// lit, and no mask is needed. The overlays (tints, reflections, the wet spots) are left out of it.
    /// </summary>
    private bool WithSilhouettes(Action render)
    {
        if (_unlitElements.Count == 0) return false;
        var swapped = new System.Collections.Generic.List<(Image Image, ImageSource Source)>();
        var hidden = new System.Collections.Generic.List<UIElement>();
        foreach (UIElement child in _worldLayer.Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            if (child is Image img && img.Source != null)
            {
                swapped.Add((img, img.Source));
                img.Source = Silhouette(img.Source, !_unlitElements.Contains(img));
            }
            else
            {
                hidden.Add(child);
                child.Visibility = Visibility.Hidden;
            }
        }
        try
        {
            // An Image draws a new Source only at the next layout pass; a capture before it takes the old pictures.
            _worldLayer.UpdateLayout();
            render();
            return true;
        }
        finally
        {
            foreach (var (img, source) in swapped) img.Source = source;
            foreach (var child in hidden) child.Visibility = Visibility.Visible;
            _worldLayer.UpdateLayout();
        }
    }

    /// <summary>The place's lights as the look draws them now, or null for none - for tests.</summary>
    internal PlaceLights? PlaceLightsNow => LookLights ? _placeLights : null;

    /// <summary>
    /// The place's lights, from what it shows on arrival: each object's light
    /// (<see cref="Model.GameObjectDef.Light"/>: the pack's own, the game's lamps
    /// and a pack place's template light, seeded from the catalog, 1.10.0), where
    /// it stands and how it is turned (the scene's walk) - not one switched off,
    /// under one, waiting on conditions, or hidden from the preview. Made again
    /// only when they change.
    /// </summary>
    private void UpdateLights()
    {
        var found = new System.Collections.Generic.List<PlaceLights.Light>();
        // The place's own base light (1.10.0): the last one on, as the game's last switched on.
        Model.LightDef? ambient = null;
        foreach (var entry in _scene)
        {
            var node = entry.Node;
            if (node?.Model.Light is not { } def || node.Model.RemoveLight || entry.Hidden) continue;
            if (entry.ParentInactive || entry.Conditional || !node.StartActive || entry.Choice != null) continue;
            if (def.IsAmbient) { ambient = def; continue; }
            if (!def.IsRound)
            {
                if (ShapeOf(def, entry) is { } shape && PlaceLights.FromShaped(def, shape) is { } shaped) found.Add(shaped);
                continue;
            }
            var (sx, sy) = ScreenPoint(entry.World.Tx, entry.World.Ty);
            if (PlaceLights.FromLight(def, sx, sy, WorldRotation(entry.World)) is { } light) found.Add(light);
        }
        var global = (1f, 1f, 1f);
        var charactersGlobal = (1f, 1f, 1f);
        if (ambient != null)
        {
            var (ar, ag, ab) = ambient.Rgb();
            global = (ar * ambient.Strength, ag * ambient.Strength, ab * ambient.Strength);
            if (ambient.LightsCharacters) charactersGlobal = global;
        }
        if (_placeLights != null && System.Linq.Enumerable.SequenceEqual(_placeLights.Lights, found)
            && _placeLights.Global == global && _placeLights.CharactersGlobal == charactersGlobal) return;
        _placeLights = found.Count == 0 && ambient == null ? null
            : new PlaceLights(found, global, charactersGlobal, Services.PreviewBackdropManager.GameLook?.Backdrop.LookupDecode);
        LookAgain();
    }

    /// <summary>How far an object is turned on screen, degrees anticlockwise: its world transform's x axis.</summary>
    private static float WorldRotation(in Aff world) => (float)(Math.Atan2(world.B, world.A) * 180.0 / Math.PI);

    /// <summary>A point in the level's own units (under its root) on the game's 960x540 screen, pixels from the
    /// bottom left - where the canvas draws it.</summary>
    private (float x, float y) ScreenPoint(double localX, double localY)
    {
        double cx = WorldOriginX + WorldPpu * localX, cy = WorldOriginY - WorldPpu * localY;
        return ((float)(cx * PlaceLights.ScreenW / CanvasWidth), (float)((CanvasHeight - cy) * PlaceLights.ScreenH / CanvasHeight));
    }

    /// <summary>The post-processing's bloom.</summary>
    public bool LookBloom { get => (bool)GetValue(LookBloomProperty); set => SetValue(LookBloomProperty, value); }
    /// <summary>The post-processing's light streaks (anamorphic flares).</summary>
    public bool LookFlares { get => (bool)GetValue(LookFlaresProperty); set => SetValue(LookFlaresProperty, value); }
    /// <summary>The post-processing's colour: tonemapping, vignette, URP's grading.</summary>
    public bool LookColour { get => (bool)GetValue(LookColourProperty); set => SetValue(LookColourProperty, value); }

    private const int LookW = 960, LookH = 540;
    /// <summary>The largest view the look is worked out at, each way (a 4K screen's).</summary>
    private const int LookViewMax = 3840;
    private readonly Image _lookImage = new()
    {
        Width = CanvasWidth,
        Height = CanvasHeight,
        IsHitTestVisible = false,
        Stretch = Stretch.Fill,
        Visibility = Visibility.Collapsed,
    };
    private byte[]? _lookViewTaken, _lookViewLast;
    private Rect _lookViewRect, _lookViewLastRect;
    private int _lookVw, _lookVh;
    private WriteableBitmap? _lookBitmap;
    private RenderTargetBitmap? _lookCapture;
    private byte[]? _lookTaken, _lookLast, _lookOut;
    private BackdropRender.PostParts _lookLastParts;
    private PlaceLights? _lookLastLights;
    private byte[]? _lookViewLit, _lookScreenLit;
    private bool _lookInFlight;
    private int _lookSame;
    private readonly GamePost.Work _lookWork = new();
    private GamePost.Target? _lookFrame;
    // Its clock: the display's frames (CompositionTarget.Rendering, paced to
    // the window's monitor), as the shaders under it - a timer's ticks fall on
    // the system's 15.6 ms clock, 64 a second at most.
    private bool _lookClockOn;
    private readonly FramePacer _lookPacer = new();
    private long _lookNextAt;

    /// <summary>How many times the place has been put through the game's look - for tests.</summary>
    internal int LookPasses { get; private set; }

    private Rendering.Gpu.GpuPost? _lookCard;

    /// <summary>Whether the last look was worked out on the graphics card - for tests.</summary>
    internal bool LookOnCard { get; private set; }

    /// <summary>Whether the game's look is drawn over the place now - for tests.</summary>
    internal bool LookShown => _lookImage.Visibility == Visibility.Visible;

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlacePreview p) { p.LookAgain(); p.SyncLook(); }
    }

    /// <summary>What of the game's look is drawn now: the tick boxes, within
    /// what the preview quality draws.</summary>
    private BackdropRender.PostParts LookParts()
    {
        var q = Services.PreviewQualityManager.Current;
        return new BackdropRender.PostParts(LookBloom && q.Bloom, LookFlares && q.Flares, LookColour && q.Colour);
    }

    /// <summary>Set the look's clock going, or stop it, for what is ticked and shown.</summary>
    private void SyncLook()
    {
        bool any = LookBloom || LookFlares || LookColour || (LookLights && _placeLights != null);
        if (!any || !IsVisible || _placeholder.Visibility == Visibility.Visible)
        {
            LookClock(false);
            _lookImage.Visibility = Visibility.Collapsed;
            CardLookOver();
            return;
        }
        if (!_lookClockOn) { LookAgain(); _lookPacer.Restart(); LookClock(true); }
    }

    private void LookClock(bool on)
    {
        if (on == _lookClockOn) return;
        if (on) CompositionTarget.Rendering += OnLookFrame;
        else CompositionTarget.Rendering -= OnLookFrame;
        _lookClockOn = on;
    }

    /// <summary>A display frame: the look is worked out when the pacer says so
    /// and the scene is not what the look already shows (<see cref="LookTick"/>).</summary>
    private void OnLookFrame(object? sender, EventArgs e)
    {
        if (!_lookPacer.Due(DisplayRefresh.RateOf(this))) return;
        LookTick();
    }

    /// <summary>
    /// The place as this preview draws it, over the whole of the game's screen (1920x1080), put through the game's
    /// look in full (bloom, light streaks, colour) on the processor, now - for the place pictures export (1.9.2,
    /// <c>Services.PlaceExport</c>, Debug builds only). The same captures and the same passes as the look over the view
    /// (<see cref="LookTick"/>), with the view being the whole screen. Straight BGRA, rows from the top; null while
    /// the game's look is still loading. <paramref name="look"/> false: the scene as drawn, without the look.
    /// </summary>
    internal byte[]? RenderStill(bool look = true, bool lights = true)
    {
        FlushRebuild();
        var placeLights = lights && LookLights ? _placeLights : null;
        int w = (int)CanvasWidth, h = (int)CanvasHeight;
        var shot = new DrawingVisual();
        using (var dc = shot.RenderOpen())
        {
            var brush = new VisualBrush(_worldLayer)
            {
                Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, CanvasWidth, CanvasHeight),
                ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, w, h),
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }
        var capture = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        capture.Render(shot);
        var view = new byte[w * h * 4];
        if (!look && placeLights == null)
        {
            new FormatConvertedBitmap(capture, PixelFormats.Bgra32, null, 0).CopyPixels(view, w * 4, 0);
            return view;
        }
        capture.CopyPixels(view, w * 4, 0);
        var scene = Services.PreviewBackdropManager.GameLook;
        if (scene == null) return null;
        var screenCapture = new RenderTargetBitmap(LookW, LookH, 48, 48, PixelFormats.Pbgra32);
        screenCapture.Render(_worldLayer);
        var screen = new byte[LookW * LookH * 4];
        screenCapture.CopyPixels(screen, LookW * 4, 0);
        // Where what is drawn is lit, and where it is drawn with the unlit material: the same two captures, of the
        // scene's silhouettes.
        byte[]? viewLit = null, screenLit = null;
        if (placeLights != null)
            WithSilhouettes(() =>
            {
                var viewMask = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                viewMask.Render(shot);
                viewLit = new byte[w * h * 4];
                viewMask.CopyPixels(viewLit, w * 4, 0);
                var screenMask = new RenderTargetBitmap(LookW, LookH, 48, 48, PixelFormats.Pbgra32);
                screenMask.Render(_worldLayer);
                screenLit = new byte[LookW * LookH * 4];
                screenMask.CopyPixels(screenLit, LookW * 4, 0);
            });
        var output = new byte[w * h * 4];
        // A screen pixel's centre on the screen: uv, v up (LookTick's mapping, for a view that is the whole screen).
        // Without the look, the lit scene alone (as the game's camera target holds it before the post).
        GamePost.LookOverView(screen, new GamePost.Target(LookW, LookH), view, w, h, (float)(1.0 / CanvasWidth), 0f,
                              (float)(-1.0 / CanvasHeight), 1f, scene.Backdrop.Post, scene.Backdrop.GradingTable, scene.Decode,
                              look ? new BackdropRender.PostParts(true, true, true) : new BackdropRender.PostParts(false, false, false),
                              new GamePost.Work(), output, placeLights, screenLit, viewLit);
        return output;
    }

    /// <summary>Where the look's time goes, summed since it was last cleared - for the performance tests.
    /// Stopwatch ticks: on the interface's thread taking the pictures (Capture), the lit masks (Mask), comparing
    /// and copying (Compare) and putting the picture on screen (Present); off it working the look out (Work).</summary>
    internal sealed class LookTimes
    {
        public long Capture, Mask, Compare, Work, Present;
        public int Ticks, Same, Busy;
        public void Clear() { Capture = Mask = Compare = Work = Present = 0; Ticks = Same = Busy = 0; }
        public static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    internal LookTimes LookTime { get; } = new();

    /// <summary>Take the scene layer; if it or the parts changed, put it through the look.</summary>
    private void LookTick()
    {
        if (_lookInFlight) LookTime.Busy++;
        if (_lookInFlight || !IsVisible) return;
        FlushRebuild();
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var parts = LookParts();
        var scene = Services.PreviewBackdropManager.GameLook;
        if (scene == null) return;                      // the game's settings are still loading
        var lights = LookLights ? _placeLights : null;
        if (!parts.Bloom && !parts.Flares && !parts.Colour && lights == null)
        {
            _lookImage.Visibility = Visibility.Collapsed;
            CardLookOver();
            return;
        }
        // What the view shows: the canvas (the game's 1920x1080 screen) through the
        // view's zoom and pan, at the view's own pixels.
        double vwDip = _viewport.ActualWidth, vhDip = _viewport.ActualHeight;
        double sc = _fitScale * _zoom;
        if (vwDip < 1 || vhDip < 1 || sc <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int vw = Math.Clamp((int)Math.Round(vwDip * dpi.DpiScaleX), 1, LookViewMax);
        int vh = Math.Clamp((int)Math.Round(vhDip * dpi.DpiScaleY), 1, LookViewMax);
        var rect = new Rect(-_viewPan.X / sc, -_viewPan.Y / sc, vwDip / sc, vhDip / sc);
        if (vw != _lookVw || vh != _lookVh) { _lookVw = vw; _lookVh = vh; _lookViewTaken = null; _lookViewLast = null; }

        // What the scene draws now, read off the world layer (1.10.0, PlacePreview.Card.cs): the card draws from
        // it, and a place that draws the same as the look shown is still - nothing is taken or worked out.
        bool useCard = Services.PreviewRendererManager.UseCard && !_cardBroken;
        var next = _frameNext;
        next.Clear();
        bool described = DescribeScene(next, useCard);
        // Captures that keep coming out the same are taken up to five times a second (the processor's way).
        bool resting = _lookSame > 30 && System.Diagnostics.Stopwatch.GetTimestamp() < _lookNextAt;
        if (described)
        {
            Frame(next, rect, vw, vh, parts, scene, lights);
            if (!_lookForce && _frameLastSet && _lookImage.Visibility == Visibility.Visible && next.SameAs(_frameLast))
            {
                LookTime.Ticks++; LookTime.Same++;
                LookTime.Capture += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                return;
            }
            // Only the shaders' clock has moved, and what they move has not shown in the captures: at rest still.
            if (resting && !useCard && !_lookForce && _frameLastSet && next.SameAs(_frameLast, butStamp: true)) return;
        }
        // Not put as a list: found still by its captures alone, as before.
        else if (resting) return;
        _lookForce = false;
        if (described && useCard)
        {
            KeepFrame(true);
            LookTime.Ticks++;
            LookTime.Capture += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            CardLook(_frameLast, rect, vw, vh);
            return;
        }
        CardLookOver();
        var shot = new DrawingVisual();
        using (var dc = shot.RenderOpen())
        {
            var brush = new VisualBrush(_worldLayer)
            {
                Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = rect,
                ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, vw, vh),
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, vw, vh));
        }
        var viewCapture = new RenderTargetBitmap(vw, vh, 96, 96, PixelFormats.Pbgra32);
        viewCapture.Render(shot);
        _lookViewTaken ??= new byte[vw * vh * 4];
        viewCapture.CopyPixels(_lookViewTaken, vw * 4, 0);
        // The whole screen at the game's size: what the bloom and the streaks are made from.
        if (parts.Bloom)
        {
            _lookCapture ??= new RenderTargetBitmap(LookW, LookH, 48, 48, PixelFormats.Pbgra32);
            _lookCapture.Clear();
            _lookCapture.Render(_worldLayer);
            _lookTaken ??= new byte[LookW * LookH * 4];
            _lookCapture.CopyPixels(_lookTaken, LookW * 4, 0);
        }
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        // The scene's lit mask, while lamps light it and something in it is unlit (WithSilhouettes): reused buffers,
        // as the captures' - nothing is taken again while a look is being worked out.
        bool masked = lights != null && WithSilhouettes(() =>
        {
            var viewMask = new RenderTargetBitmap(vw, vh, 96, 96, PixelFormats.Pbgra32);
            viewMask.Render(shot);
            if (_lookViewLit == null || _lookViewLit.Length != vw * vh * 4) _lookViewLit = new byte[vw * vh * 4];
            viewMask.CopyPixels(_lookViewLit, vw * 4, 0);
            if (parts.Bloom)
            {
                var screenMask = new RenderTargetBitmap(LookW, LookH, 48, 48, PixelFormats.Pbgra32);
                screenMask.Render(_worldLayer);
                _lookScreenLit ??= new byte[LookW * LookH * 4];
                screenMask.CopyPixels(_lookScreenLit, LookW * 4, 0);
            }
        });
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        LookTime.Ticks++; LookTime.Capture += t1 - t0; LookTime.Mask += t2 - t1;
        bool same = _lookViewLast != null && parts == _lookLastParts && rect == _lookViewLastRect && ReferenceEquals(lights, _lookLastLights)
                    && _lookViewTaken.AsSpan().SequenceEqual(_lookViewLast)
                    && (!parts.Bloom || (_lookLast != null && _lookTaken!.AsSpan().SequenceEqual(_lookLast)));
        // The scene as just taken is what the look shows, or is about to: the next list is held against this one.
        KeepFrame(described);
        if (same && _lookImage.Visibility == Visibility.Visible)
        {
            // Still: look again less often, up to five times a second.
            _lookSame++;
            _lookNextAt = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 5;
            LookTime.Same++; LookTime.Compare += System.Diagnostics.Stopwatch.GetTimestamp() - t2;
            return;
        }
        _lookSame = 0;
        _lookViewLast ??= new byte[_lookViewTaken.Length];
        Buffer.BlockCopy(_lookViewTaken, 0, _lookViewLast, 0, _lookViewTaken.Length);
        _lookViewLastRect = rect;
        if (parts.Bloom)
        {
            _lookLast ??= new byte[_lookTaken!.Length];
            Buffer.BlockCopy(_lookTaken!, 0, _lookLast, 0, _lookTaken!.Length);
        }
        _lookLastParts = parts;
        _lookLastLights = lights;

        // A view pixel's centre on the screen: uv, v up (past its edges too).
        float ax = (float)(rect.Width / vw / CanvasWidth), bx = (float)(rect.X / CanvasWidth);
        float ay = (float)(-rect.Height / vh / CanvasHeight), by = (float)(1.0 - rect.Y / CanvasHeight);
        var screen = parts.Bloom ? _lookLast : null;
        var view = _lookViewLast;
        var viewLit = masked ? _lookViewLit : null;
        var screenLit = masked && parts.Bloom ? _lookScreenLit : null;
        var output = _lookOut != null && _lookOut.Length == vw * vh * 4 ? _lookOut : (_lookOut = new byte[vw * vh * 4]);
        var frame = _lookFrame ??= new GamePost.Target(LookW, LookH);
        var work = _lookWork;
        _lookInFlight = true;
        var ui = TaskScheduler.FromCurrentSynchronizationContext();
        // The card for the look alone (its bloom and compose) when it draws the previews but not this scene.
        bool postOnCard = Services.PreviewRendererManager.UseCard;
        LookTime.Compare += System.Diagnostics.Stopwatch.GetTimestamp() - t2;
        Task.Run(() =>
            {
                long w0 = System.Diagnostics.Stopwatch.GetTimestamp();
                // On the graphics card when that draws the previews: the exact passes there.
                var card = postOnCard ? CardPost() : null;
                if (card != null) card.LookOverView(screen, LookW, LookH, view, vw, vh, ax, bx, ay, by, scene, parts, output, lights, screenLit, viewLit);
                else GamePost.LookOverView(screen, frame, view, vw, vh, ax, bx, ay, by, scene.Backdrop.Post, scene.Backdrop.GradingTable, scene.Decode,
                                           parts, work, output, lights, screenLit, viewLit);
                LookOnCard = card != null;
                System.Threading.Interlocked.Add(ref LookTime.Work, System.Diagnostics.Stopwatch.GetTimestamp() - w0);
            })
            .ContinueWith(t =>
            {
                _lookInFlight = false;
                if (t.IsFaulted) { _frameLastSet = false; return; }
                if (!IsVisible) return;
                ShowLook(output, vw, vh, rect);
            }, ui);
    }
}
