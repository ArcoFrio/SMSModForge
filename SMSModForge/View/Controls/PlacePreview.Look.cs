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
        if (d is PlacePreview p) { p._lookSame = 0; p.SyncLook(); }
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
        bool any = LookBloom || LookFlares || LookColour;
        if (!any || !IsVisible || _placeholder.Visibility == Visibility.Visible)
        {
            LookClock(false);
            _lookImage.Visibility = Visibility.Collapsed;
            return;
        }
        if (!_lookClockOn) { _lookSame = 0; _lookPacer.Restart(); LookClock(true); }
    }

    private void LookClock(bool on)
    {
        if (on == _lookClockOn) return;
        if (on) CompositionTarget.Rendering += OnLookFrame;
        else CompositionTarget.Rendering -= OnLookFrame;
        _lookClockOn = on;
    }

    /// <summary>A display frame: the look is taken when the pacer says so -
    /// while the place stays still, up to five times a second.</summary>
    private void OnLookFrame(object? sender, EventArgs e)
    {
        if (!_lookPacer.Due(DisplayRefresh.RateOf(this))) return;
        if (_lookSame > 30 && System.Diagnostics.Stopwatch.GetTimestamp() < _lookNextAt) return;
        LookTick();
    }

    /// <summary>Take the scene layer; if it or the parts changed, put it through the look.</summary>
    private void LookTick()
    {
        if (_lookInFlight || !IsVisible) return;
        var parts = LookParts();
        var scene = Services.PreviewBackdropManager.GameLook;
        if (scene == null) return;                      // the game's settings are still loading
        if (!parts.Bloom && !parts.Flares && !parts.Colour)
        {
            _lookImage.Visibility = Visibility.Collapsed;
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
        bool same = _lookViewLast != null && parts == _lookLastParts && rect == _lookViewLastRect
                    && _lookViewTaken.AsSpan().SequenceEqual(_lookViewLast)
                    && (!parts.Bloom || (_lookLast != null && _lookTaken!.AsSpan().SequenceEqual(_lookLast)));
        if (same && _lookImage.Visibility == Visibility.Visible)
        {
            // Still: look again less often, up to five times a second.
            _lookSame++;
            _lookNextAt = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 5;
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

        // A view pixel's centre on the screen: uv, v up (past its edges too).
        float ax = (float)(rect.Width / vw / CanvasWidth), bx = (float)(rect.X / CanvasWidth);
        float ay = (float)(-rect.Height / vh / CanvasHeight), by = (float)(1.0 - rect.Y / CanvasHeight);
        var screen = parts.Bloom ? _lookLast : null;
        var view = _lookViewLast;
        var output = _lookOut != null && _lookOut.Length == vw * vh * 4 ? _lookOut : (_lookOut = new byte[vw * vh * 4]);
        var frame = _lookFrame ??= new GamePost.Target(LookW, LookH);
        var work = _lookWork;
        _lookInFlight = true;
        var ui = TaskScheduler.FromCurrentSynchronizationContext();
        bool useCard = Services.PreviewRendererManager.UseCard;
        Task.Run(() =>
            {
                // On the graphics card when that draws the previews: the exact passes there.
                var card = useCard ? (_lookCard ??= Rendering.Gpu.GpuPost.TryCreate()) : null;
                if (card != null) card.LookOverView(screen, LookW, LookH, view, vw, vh, ax, bx, ay, by, scene, parts, output);
                else GamePost.LookOverView(screen, frame, view, vw, vh, ax, bx, ay, by, scene.Backdrop.Post, scene.Backdrop.GradingTable, scene.Decode,
                                           parts, work, output);
                LookOnCard = card != null;
            })
            .ContinueWith(t =>
            {
                _lookInFlight = false;
                if (t.IsFaulted || !IsVisible) return;
                if (_lookBitmap == null || _lookBitmap.PixelWidth != vw || _lookBitmap.PixelHeight != vh)
                {
                    _lookBitmap = new WriteableBitmap(vw, vh, 96, 96, PixelFormats.Bgra32, null);
                    _lookImage.Source = _lookBitmap;
                }
                _lookBitmap.WritePixels(new Int32Rect(0, 0, vw, vh), output, vw * 4, 0);
                // Over exactly what the view showed: one picture pixel a screen pixel.
                Canvas.SetLeft(_lookImage, rect.X); Canvas.SetTop(_lookImage, rect.Y);
                _lookImage.Width = rect.Width; _lookImage.Height = rect.Height;
                _lookViewRect = rect;
                _lookImage.Visibility = Visibility.Visible;
                LookPasses++;
            }, ui);
    }
}
