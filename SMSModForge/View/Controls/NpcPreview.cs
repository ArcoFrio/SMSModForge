using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Path = System.IO.Path;
using SMSModForge.Localization;

namespace SMSModForge.View.Controls;

/// <summary>
/// Live preview of one NPC — the same CPU JiggleSprite pass the Busts tab uses
/// for the body, with the procedural shadow and the blink overlay drawn as
/// their own WPF layers so their transforms (the shadow can tilt on all three
/// axes) are exact.
/// <para/>
/// The body shader is fixed at 256², but NPC poses are variable-resolution and
/// non-square. <see cref="BustComposer.LoadPng"/> squashes the source to 256²
/// and the jiggle runs in UV space, so displaying the output at the pose's real
/// aspect ratio undoes the squash — no distortion, just a resolution cap that a
/// small preview never notices.
/// <para/>
/// The shadow's X/Y rotation is applied as a cos-scale rather than a real 3D
/// rotate: for a flat sprite under an orthographic camera, tilting about X or Y
/// is exactly a foreshortening by cos(angle). Z is a real in-plane rotation.
/// </summary>
public sealed class NpcPreview : Grid
{
    // The preview is the game's screen around the NPC (1.9.1): the backdrop's
    // frame, one preview pixel a pixel of the game's 960x540, the NPC at its size
    // in the place, in the middle - over a backdrop and with none alike (the
    // author: "the no background and the actual backgrounds had different
    // scales, please unify those"). The frame's own scaling (PreviewFrame) makes
    // it bigger or smaller on screen.
    private const double BoxSize = BackdropRender.FrameSize;

    public static readonly DependencyProperty NpcProperty =
        DependencyProperty.Register(nameof(Npc), typeof(NpcViewModel), typeof(NpcPreview),
            new PropertyMetadata(null, (d, e) => ((NpcPreview)d).OnNpcChanged(e)));

    public NpcViewModel? Npc
    {
        get => (NpcViewModel?)GetValue(NpcProperty);
        set => SetValue(NpcProperty, value);
    }

    public static readonly DependencyProperty PackRootProperty =
        DependencyProperty.Register(nameof(PackRoot), typeof(string), typeof(NpcPreview),
            new PropertyMetadata(null, (d, e) => ((NpcPreview)d).Reload()));

    public string? PackRoot
    {
        get => (string?)GetValue(PackRootProperty);
        set => SetValue(PackRootProperty, value);
    }

    private readonly Canvas _canvas = new() { Width = BoxSize, Height = BoxSize, ClipToBounds = true };
    private readonly Ellipse _shadow = new();
    private readonly Image _body = new();
    // The NPC's wet drops drawn alone (no backdrop): the frame's square, over the body. Over a backdrop they are
    // in the frame (NpcRender.Compose, GpuFrame.DrawNpc).
    private readonly Image _dropsAlone = new() { IsHitTestVisible = false, Stretch = Stretch.Fill, Visibility = Visibility.Collapsed };
    private WriteableBitmap? _dropsBitmap;
    private byte[]? _dropsOutput;
    private readonly TextBlock _placeholder = new()
    {
        Foreground = PreviewMessage.Ink, Background = PreviewMessage.Chip, Padding = new Thickness(6, 3, 6, 4),
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
    };

    // We decouple SOURCE resolution from OUTPUT resolution. The shader samples
    // the base/mask at their (high) source resolution but writes an output only
    // as big as the pose is actually DISPLAYED — so detail is preserved (source
    // stays sharp, 4× supersampling area-averages the downsample) while the
    // per-frame cost is bounded by the display size, not the 768² it used to
    // waste rendering above the box.
    private const int SourceMaxSide = 1024;   // one-time decode cost, sampled per frame

    // Body render buffers — recreated per pose since NPC sprites vary in size.
    private WriteableBitmap? _bodyBitmap;
    private byte[]? _bodyOutput;
    private int _srcW, _srcH;    // source (sampled) resolution
    private int _outW, _outH;    // output (rendered + displayed) resolution

    // Native-resolution straight-alpha buffers, fed to the shader (base/mask)
    // and composited into its output (blink) — so blink rides the jiggle and
    // its alpha is premultiplied by CompositeSame (no white halo).
    private byte[]? _base, _mask, _blinkTex;

    // Over a backdrop (1.9.1): the game's frame around the NPC, as the game draws
    // it in that place (NpcRender) - the blink its own sprite at the art's grid,
    // the NPC at its size there (its art's own pixels times the level's scale).
    private byte[]? _blinkSrc;
    private int _nativeW = 1, _nativeH = 1;
    private WriteableBitmap? _frameBitmap;
    private readonly byte[] _frameOutput = new byte[BackdropRender.FrameSize * BackdropRender.FrameSize * 4];
    private float[]? _linear, _linearBlink, _coverage, _scratchBackground;
    private GamePost.Target? _target;
    private BackdropScene? _targetOf;
    private FakeGi? _npcGi;
    private PreviewBackdrop? _npcGiOf;
    private FakeGi.Art? _npcArt, _npcBlinkArt;
    private byte[]? _npcArtOf, _npcBlinkArtOf;
    private NpcLight? _light;
    private BackdropScene? _lightOf;
    private bool? _framed;           // null: not laid out yet
    private (float, float, int, int) _aloneAt;

    /// <summary>Whether the last frame was drawn over a backdrop - for tests.</summary>
    internal bool OverBackdrop { get; private set; }

    /// <summary>The picture on screen: over a backdrop the whole frame, alone the NPC's own (its drops are
    /// <see cref="DropsAloneShown"/>) - for tests.</summary>
    internal System.Windows.Media.Imaging.BitmapSource? Shown => _body.Source as System.Windows.Media.Imaging.BitmapSource;

    // Live mask cache — the mask editor publishes a 256×256 BGRA buffer;
    // we resize it to the source grid each time the revision bumps.
    private byte[]? _liveMaskResized;
    private int _liveMaskResizedRevision;

    private bool _renderInFlight, _renderingHooked;
    private readonly FramePacer _pacer = new();
    private TaskScheduler? _uiScheduler;


    /// <summary>How many shader passes this preview has begun - for tests, to
    /// measure the frame rate a quality preset really gives.</summary>
    /// <summary>Pictures put on screen (Options ▸ Show preview frame rate).</summary>
    internal int FramesShown { get; private set; }

    /// <summary>Whether the last frame was drawn on the graphics card - for tests.</summary>
    internal bool DrewOnCard { get; private set; }

    private Rendering.Gpu.GpuAlone? _gpuAlone;
    private Rendering.Gpu.GpuFrame? _gpuFrame;

    internal int ShaderPasses { get; private set; }
    private readonly DateTime _start = DateTime.Now;

    // Blink schedule — mirrors the game component: open a random 2-5s, shut 0.2s.
    private bool _blinkClosed;
    private double _blinkNextSec;
    private readonly Random _rng = new();

    public NpcPreview()
    {
        // A tooltip that outlived its owner sits over the one thing an
        // author is trying to look at. See ToolTipDismisser.
        View.ToolTipDismisser.KeepClearOf(this);
        LocText.Bind(_placeholder, TextBlock.TextProperty, "preview.npc.noPose");

        Width = MinWidth = MaxWidth = BoxSize;
        Height = MinHeight = MaxHeight = BoxSize;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

        _body.Stretch = Stretch.Fill;
        // The render is high-res and the display DOWNSCALES it → HighQuality is
        // a proper area-average (sharp). NearestNeighbor here would alias, and
        // HighQuality on a low-res source (the old bug) is what blurred.
        RenderOptions.SetBitmapScalingMode(_body, BitmapScalingMode.HighQuality);
        _body.SnapsToDevicePixels = true;
        _body.UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        // Z-order: shadow (behind), body (front). Blink is composited into the
        // body bitmap, so there's no separate blink element.
        _canvas.Children.Add(_shadow);
        _canvas.Children.Add(_body);
        _canvas.Children.Add(_dropsAlone);
        RenderOptions.SetBitmapScalingMode(_dropsAlone, BitmapScalingMode.NearestNeighbor);
        Children.Add(_canvas);
        Children.Add(_placeholder);

        // Gate the render loop on real visibility, not just Loaded — a tab that
        // isn't selected keeps its content loaded-but-hidden, and we don't want
        // the jiggle shader burning CPU on a preview nobody can see.
        IsVisibleChanged += (_, _) => { if (IsVisible) Hook(); else Unhook(); };
        FpsBadge.Attach(this, () => FramesShown);
    }

    private static readonly System.Collections.Generic.HashSet<string> _pathProps = new()
    {
        nameof(NpcViewModel.Sprite), nameof(NpcViewModel.Mask), nameof(NpcViewModel.BlinkSprite),
    };

    private void OnNpcChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is NpcViewModel oldVm) oldVm.PropertyChanged -= OnNpcProp;
        if (e.NewValue is NpcViewModel newVm) newVm.PropertyChanged += OnNpcProp;
        Reload();
    }

    private void OnNpcProp(object? _, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || _pathProps.Contains(e.PropertyName)) { Reload(); return; }

        // The mask editor clears its live buffer on close, and the preview then
        // falls back to the file it last read — which for an existing mask was
        // read before any editing. One read when the buffer goes away, so the
        // picture does not revert to the old mask the moment the editor shuts.
        if (e.PropertyName == nameof(NpcViewModel.LiveMaskBgra) && Npc?.LiveMaskBgra == null)
            Reload();
        else Relayout();   // jiggle / shadow / colour edits: no file I/O, just re-place
    }

    private void Reload()
    {
        if (Npc == null || string.IsNullOrEmpty(PackRoot))
        {
            _base = _mask = _blinkTex = null;
            ShowPlaceholder(true);
            return;
        }
        string poseAbs = Path.Combine(PackRoot, Norm(Npc.Sprite));
        if (!File.Exists(poseAbs)) { _base = null; ShowPlaceholder(true); return; }

        // Source at high resolution (decoded once) for sampling detail; mask at
        // the same grid so the shader lines up.
        (_base, _srcW, _srcH) = BustComposer.LoadPngNative(poseAbs, SourceMaxSide);
        (int nativeW, int nativeH) = PngSize(poseAbs);
        _nativeW = Math.Max(1, nativeW); _nativeH = Math.Max(1, nativeH);

        string maskAbs = Path.Combine(PackRoot, Norm(Npc.Mask));
        _mask = BustComposer.LoadPngAt(maskAbs, _srcW, _srcH);   // transparent if absent

        string blinkAbs = Path.Combine(PackRoot, Norm(Npc.BlinkSprite));
        _blinkAbs = !string.IsNullOrWhiteSpace(Npc.BlinkSprite) && File.Exists(blinkAbs) ? blinkAbs : null;
        // Over a backdrop the blink is a sprite of its own, through the same
        // material (NpcFactory.BuildBlink): at the art's grid. Alone it is
        // composited into the output, at the output's (EnsureAlone).
        _blinkSrc = _blinkAbs != null ? BustComposer.LoadPngAt(_blinkAbs, _srcW, _srcH) : null;
        _blinkTex = null;
        _framed = null;
        _outW = _outH = 0;
        // A first output, so the render loop starts (resized to the NPC's size there).
        EnsureAlone(1, 1);

        ShowPlaceholder(false);
        Relayout();
    }

    private string? _blinkAbs;

    /// <summary>
    /// Where the NPC stands in the frame for a backdrop (None too): its art's own
    /// pixels at the place's scale (NpcRender.ScreenPerArtPixel), in the middle at
    /// the game's x2 pixels (the Pixel Perfect Camera's snap) - its size and its
    /// offset from the frame's bottom-left, in screen pixels.
    /// </summary>
    private (float width, float height, float left, float bottom) NpcRect(Services.PreviewBackdropDef def)
    {
        const int N = BackdropRender.FrameSize;
        float scale = NpcRender.ScreenPerArtPixel(def.LevelScale);
        float width = _nativeW * scale, height = _nativeH * scale;
        float left = MathF.Round((N - width) / 2f * JiggleShader.GameZoom) / JiggleShader.GameZoom;
        float bottom = MathF.Round((N - height) / 2f * JiggleShader.GameZoom) / JiggleShader.GameZoom;
        return (width, height, left, bottom);
    }

    /// <summary>The alone output at <paramref name="w"/> x <paramref name="h"/>
    /// (the NPC's size in the frame), its blink at the same grid.</summary>
    private void EnsureAlone(int w, int h)
    {
        if (_bodyOutput != null && _outW == w && _outH == h) return;
        _outW = w; _outH = h;
        _bodyOutput = new byte[w * h * 4];
        _bodyBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
        _blinkTex = _blinkAbs != null ? BustComposer.LoadPngAt(_blinkAbs, w, h) : null;
        if (_framed == false) _body.Source = _bodyBitmap;
    }

    /// <summary>The NPC alone, where it stands over a backdrop, nothing behind it.</summary>
    private void LayoutAlone(float left, float bottom)
    {
        const int N = BackdropRender.FrameSize;
        SetBox(N);
        _body.Source = _bodyBitmap;
        _body.Width = _outW; _body.Height = _outH;
        Canvas.SetLeft(_body, left);
        Canvas.SetTop(_body, N - bottom - _outH);
        RenderOptions.SetBitmapScalingMode(_body, BitmapScalingMode.NearestNeighbor);
    }

    private void SetBox(double size)
    {
        Width = MinWidth = MaxWidth = size;
        Height = MinHeight = MaxHeight = size;
        _canvas.Width = _canvas.Height = size;
    }

    /// <summary>Over a backdrop, the game's frame around the NPC (one preview pixel
    /// a screen pixel of the game's 960x540, as the bust preview's); otherwise the
    /// NPC alone, at the same size and place in the same frame.</summary>
    private void ShowFramed(bool framed, float aloneLeft, float aloneBottom)
    {
        if (!framed)
        {
            // Every frame: the NPC's size follows the backdrop's level.
            if (_framed != false || _aloneAt != (aloneLeft, aloneBottom, _outW, _outH))
            {
                _framed = false;
                _aloneAt = (aloneLeft, aloneBottom, _outW, _outH);
                LayoutAlone(aloneLeft, aloneBottom);
            }
            return;
        }
        if (_framed == true) return;
        _framed = true;
        const int N = BackdropRender.FrameSize;
        SetBox(N);
        _frameBitmap ??= new WriteableBitmap(N, N, 96, 96, PixelFormats.Pbgra32, null);
        _body.Source = _frameBitmap;
        _body.Width = N; _body.Height = N;
        Canvas.SetLeft(_body, 0); Canvas.SetTop(_body, 0);
        RenderOptions.SetBitmapScalingMode(_body, BitmapScalingMode.NearestNeighbor);
    }

    /// <summary>
    /// The NPC tab preview is a "does this pose look right" view — sprite +
    /// jiggle + blink. Positioning (shadow / particle offsets, the NPC's own
    /// transform) is a per-placement concern, edited and previewed on the
    /// Places tab where the room gives it context. Nothing to re-place here.
    /// </summary>
    private void Relayout() { }

    private void ShowPlaceholder(bool on)
    {
        _placeholder.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _canvas.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── render loop (body jiggle) ────────────────────────────────────────

    private void Hook()
    {
        if (_renderingHooked) return;
        CompositionTarget.Rendering += OnRender;
        _renderingHooked = true;
    }

    private void Unhook()
    {
        if (!_renderingHooked) return;
        CompositionTarget.Rendering -= OnRender;
        _renderingHooked = false;
    }

    /// <summary>How many frame callbacks this preview has had - for tests.</summary>
    internal int FrameCallbacks { get; private set; }

    private void OnRender(object? sender, EventArgs e) { FrameCallbacks++; RenderBody(); }

    private void RenderBody()
    {
        if (!IsVisible || _renderInFlight || Npc == null || _base == null || _mask == null
            || _bodyOutput == null || _bodyBitmap == null) return;

        // At most the display's own rate (1.9.1: no quality caps the rate
        // below it any more - see PreviewQualityDef), on a precise clock.
        if (!_pacer.Due(Rendering.DisplayRefresh.RateOf(this))) return;

        long nowMs = Environment.TickCount64;

        // Advance the blink schedule and decide whether the eyes are shut this
        // frame — composited INTO the shader output (below), so it rides the
        // jiggle and gets proper premultiplied alpha.
        byte[]? blink = BlinkOverlayForThisFrame(nowMs / 1000.0);

        // Prefer the live mask buffer published by the mask editor.
        byte[] maskSnap;
        if (Npc.LiveMaskBgra != null)
        {
            if (_liveMaskResizedRevision != Npc.LiveMaskRevision)
            {
                _liveMaskResized = BustComposer.Resize(Npc.LiveMaskBgra, MaskBuffer.Size, MaskBuffer.Size, _srcW, _srcH);
                _liveMaskResizedRevision = Npc.LiveMaskRevision;
            }
            maskSnap = _liveMaskResized!;
        }
        else
        {
            maskSnap = _mask!;
        }

        var jiggle = Npc.Model.Jiggle;
        var tint = BustComposer.ParseTint(Npc.JiggleTint);
        float time = (float)(DateTime.Now - _start).TotalSeconds;

        _uiScheduler ??= TaskScheduler.FromCurrentSynchronizationContext();

        // Over a backdrop: the NPC as the game draws it in that place. With none:
        // the NPC alone, the same size in the same place of the same frame.
        var backdropDef = Services.PreviewBackdropManager.Current;
        var scene = backdropDef.Scene;
        var rect = NpcRect(backdropDef);
        OverBackdrop = scene != null;
        if (scene != null)
        {
            ShowFramed(true, 0f, 0f);
            ShowDropsAlone(null);
            RenderOverBackdrop(scene, backdropDef, _base, maskSnap, jiggle, tint, time, blink != null, rect);
            return;
        }
        EnsureAlone(Math.Max(1, (int)Math.Round(rect.width)), Math.Max(1, (int)Math.Round(rect.height)));
        ShowFramed(false, rect.left, rect.bottom);
        var dropsAlone = WetDropsNow(null, backdropDef, rect, time, null);
        DropsDrawn = dropsAlone?.Count ?? 0;
        ShowDropsAlone(dropsAlone);
        // The blink at this output's grid (EnsureAlone reloads it with the size).
        if (blink != null) blink = _blinkTex;
        byte[] baseSnap = _base, output = _bodyOutput!;
        var bmp = _bodyBitmap!;
        int sw = _srcW, sh = _srcH, ow = _outW, oh = _outH;
        // Sampled as the quality says (Ultra the game's 2x2 a pixel, fewer below).
        var sampling = Services.PreviewQualityManager.Current.Sampling;
        _renderInFlight = true;
        ShaderPasses++;
        bool useCard = Services.PreviewRendererManager.UseCard;
        Task.Run(() =>
            {
                // Sample the high-res source (sw×sh); render only the displayed
                // pixels (ow×oh). On the graphics card when that draws the
                // previews (the game's own program).
                var card = useCard ? (_gpuAlone ??= Rendering.Gpu.GpuAlone.TryCreate()) : null;
                if (card != null) card.Draw(baseSnap, maskSnap, sw, sh, jiggle, tint, time, output, ow, oh, sampling);
                else JiggleShader.Render(baseSnap, maskSnap, sw, sh, jiggle, tint, time, output, ow, oh, sampling);
                DrewOnCard = card != null;
                if (blink != null) BustComposer.CompositeSame(output, blink, ow, oh);
            })
            .ContinueWith(t =>
            {
                try
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        bmp.Lock();
                        try { bmp.WritePixels(new Int32Rect(0, 0, ow, oh), output, ow * 4, 0); }
                        finally { bmp.Unlock(); }
                        FramesShown++;
                    }
                }
                finally { _renderInFlight = false; }
            }, _uiScheduler);
    }

    /// <summary>
    /// One frame over a backdrop (1.9.1): the NPC through the bust's program -
    /// its jiggle, its fake GI reading the flat stand-in the game binds when no
    /// bust is drawn, times the room layer's light - at its size in that place,
    /// centred in the frame, its blink a sprite of its own over it, then the
    /// room's lamp glow or lights and the game's post-processing, by the quality
    /// (PreviewQualityDef). Drawn on the processor.
    /// </summary>
    /// <summary>What stopped the last frame that could not be drawn, if one could not - for tests to say.</summary>
    internal string? LastFault { get; private set; }

    private void RenderOverBackdrop(BackdropScene scene, Services.PreviewBackdropDef def, byte[] baseSnap, byte[] maskSnap,
                                    SMSModForge.Model.JiggleParams jiggle, (float r, float g, float b, float a) tint, float time, bool eyesShut,
                                    (float width, float height, float left, float bottom) rect)
    {
        const int N = BackdropRender.FrameSize;
        var quality = Services.PreviewQualityManager.Current;
        var backdrop = scene.Backdrop;
        int sw = _srcW, sh = _srcH;
        bool edgeGlow = quality.EdgeGlow;
        var blinkSrc = eyesShut ? _blinkSrc : null;
        int bgLength = backdrop.BackgroundWidth * backdrop.BackgroundHeight * 4;
        if (_scratchBackground == null || _scratchBackground.Length != bgLength) _scratchBackground = new float[bgLength];
        _linear ??= new float[N * N * 4];
        _coverage ??= new float[N * N];
        if (blinkSrc != null) _linearBlink ??= new float[N * N * 4];
        var scratch = _scratchBackground;
        var linear = _linear;
        var linearBlink = blinkSrc != null ? _linearBlink : null;
        var coverage = _coverage;
        var output = _frameOutput;
        var bmp = _frameBitmap!;
        var parts = new BackdropRender.PostParts(quality.Bloom, quality.Flares, quality.Colour);
        // Its size in that place, centred, at the game's x2 pixels (the Pixel
        // Perfect Camera's snap).
        float width = rect.width, height = rect.height;
        float left = scene.Left + rect.left, bottom = scene.FrameBottom + rect.bottom;
        // A shower's drops are lit by the room layer's light, as the NPC's art is: worked out on the frame's thread,
        // with the light made there when the place changes (below).
        var npcNow = Npc;

        _renderInFlight = true;
        ShaderPasses++;
        bool useCard = Services.PreviewRendererManager.UseCard;
        Task.Run(() =>
            {
                // What a place's frames share - the room's light and edge glow, the frame's target, the NPC's art
                // read for the glow - made once per place (or per art) and kept. Made here, not before the frame is
                // handed over: the first frame in a place took a quarter of a second on the interface's thread, which
                // was the NPCs tab's first visit (1.10.0, UiDelayTests). Only one frame is ever in flight
                // (_renderInFlight), so nothing else reads these while they are made.
                if (!ReferenceEquals(_npcGiOf, backdrop)) { _npcGi = NpcRender.Gi(backdrop); _npcGiOf = backdrop; _npcArtOf = _npcBlinkArtOf = null; }
                var gi = edgeGlow ? _npcGi : null;
                int reach = NpcRender.Reach(backdrop.Gi, sw, sh);
                if (gi != null && !ReferenceEquals(_npcArtOf, baseSnap)) { _npcArt = new FakeGi.Art(baseSnap, sw, sh, reach); _npcArtOf = baseSnap; }
                if (gi != null && blinkSrc != null && !ReferenceEquals(_npcBlinkArtOf, blinkSrc)) { _npcBlinkArt = new FakeGi.Art(blinkSrc, sw, sh, reach); _npcBlinkArtOf = blinkSrc; }
                var art = gi != null ? _npcArt : null;
                var blinkArt = gi != null ? _npcBlinkArt : null;
                if (!ReferenceEquals(_lightOf, scene)) { _light = new NpcLight(scene); _lightOf = scene; }
                if (!ReferenceEquals(_targetOf, scene)) { _target = BackdropRender.Scene(scene); _targetOf = scene; }
                var light = _light!;
                var target = _target!;
                var npcGi = _npcGi!;
                bool giOn = gi != null;
                var drops = WetDropsNow(scene, def, rect, time, light, npcNow);
                DropsDrawn = drops?.Count ?? 0;
                // On the graphics card when that draws the previews: the whole frame
                // there (Rendering.Gpu.GpuFrame.DrawNpc), only its bytes coming back.
                var card = useCard ? (_gpuFrame ??= Rendering.Gpu.GpuFrame.TryCreate()) : null;
                if (card != null)
                {
                    card.DrawNpc(scene, npcGi, giOn, light, baseSnap, blinkSrc, maskSnap, sw, sh, jiggle, tint, time,
                                 left, bottom, width, height, parts, JiggleShader.GameZoom, output, drops);
                    DrewOnCard = true;
                    return;
                }
                // The edge glow's blur the program's own (exact) at every quality that has it.
                NpcRender.Body(baseSnap, maskSnap, sw, sh, jiggle, tint, time, gi, art, light.At, left, bottom, width, height,
                               scene.Left, scene.FrameBottom, linear, true, scene.Decode, JiggleShader.GameZoom);
                if (linearBlink != null)
                    NpcRender.Body(blinkSrc!, maskSnap, sw, sh, jiggle, tint, time, gi, blinkArt, light.At, left, bottom, width, height,
                                   scene.Left, scene.FrameBottom, linearBlink, true, scene.Decode, JiggleShader.GameZoom);
                NpcRender.Compose(scene, target, scratch, coverage, parts, output, drops, linear, linearBlink);
                DrewOnCard = false;
            })
            .ContinueWith(t =>
            {
                try
                {
                    if (t.IsFaulted) LastFault = t.Exception?.GetBaseException().ToString();
                    if (t.IsCompletedSuccessfully && _framed == true)
                    {
                        bmp.Lock();
                        try { bmp.WritePixels(new Int32Rect(0, 0, N, N), output, N * 4, 0); }
                        finally { bmp.Unlock(); }
                        FramesShown++;
                    }
                }
                finally { _renderInFlight = false; }
            }, _uiScheduler!);
    }

    // ── The NPC's wet drops ─────────────────────────────────────────────

    /// <summary>How many drops the last frame drew - for tests.</summary>
    internal int DropsDrawn { get; private set; }

    /// <summary>Where the last frame had the NPC's Wet object, on its canvas - for tests.</summary>
    internal WetDrops.Emitter? LastEmitter { get; private set; }

    /// <summary>A test's own moment for the drops.</summary>
    internal Func<double>? WetClockForTests { get; set; }

    /// <summary>The drops drawn alone, as last drawn (the frame's square, premultiplied BGRA), or null when none
    /// are - for tests.</summary>
    internal byte[]? DropsAloneShown => _dropsAlone.Visibility == Visibility.Visible ? _dropsOutput : null;

    /// <summary>
    /// The NPC's wet drops this frame (after 1.10.0, the author: "I see the wet particle configs on Characters, but I
    /// don't see them on NPCs, and setting Enabled and Start Active also doesn't have the preview behaving
    /// accordingly"): none with its Wet particles off; faint, as the place preview's ghosts, when they do not start
    /// on; its own look (kind, tint, amount, size, fall: WetEffectDef.Merged with no placement over it) where a new
    /// placement puts its Wet object - (-0.3, 7.7) of the NPC's own units from the middle of its art, moved and sized
    /// as the art is, the drops at their own size (the game scales a spray by its own object alone: WetDrops).
    /// Over a backdrop on the screen's canvas, a shower's drops lit by the room layer's light where each one is (the
    /// NPC's own: NpcLight); alone on the frame's square at the game's 50 pixels a unit, unlit, there being no room.
    /// </summary>
    internal WetDrops.Over? WetDropsNow(BackdropScene? scene, Services.PreviewBackdropDef def,
                                        (float width, float height, float left, float bottom) rect, float time, NpcLight? light,
                                        NpcViewModel? npc = null)
    {
        npc ??= Npc;
        if (npc == null) return null;
        double t = WetClockForTests?.Invoke() ?? (Services.TestMode.Active ? WetDrops.TestTime : WetDrops.Steady + time);
        var drops = DropsFor(npc.Model.Wet, npc.Key ?? "", scene, def.LevelScale, rect, t, light, out var e);
        LastEmitter = e;
        return drops;
    }

    /// <summary>
    /// <see cref="WetDropsNow"/>'s drops for an NPC's wet settings <paramref name="wet"/> (its key seeds them), out
    /// <paramref name="t"/> seconds, the NPC at <paramref name="rect"/> of the frame in a place of scale
    /// <paramref name="levelScale"/> - what the frame on the card is held to the processor's with (GpuNpcTests).
    /// Null with its Wet particles off, without the particle texture, or with none out.
    /// </summary>
    internal static WetDrops.Over? DropsFor(SMSModForge.Model.NpcWetDef wet, string key, BackdropScene? scene, float levelScale,
                                            (float width, float height, float left, float bottom) rect, double t, NpcLight? light,
                                            out WetDrops.Emitter? emitter)
    {
        emitter = null;
        if (!wet.Enabled) return null;
        var mips = WetDrops.Texture;
        if (mips == null) return null;
        var look = SMSModForge.Model.WetEffectDef.Merged(wet, null);
        bool shower = SMSModForge.Model.WetEffectDef.ShowerKind(look.Kind);
        var at = new SMSModForge.Model.NpcPlacementDef().Wet;
        const int N = BackdropRender.FrameSize;
        double screenW = scene?.Backdrop.ScreenWidth ?? N, screenH = scene?.Backdrop.ScreenHeight ?? N;
        double frameLeft = scene?.Left ?? 0, frameBottom = scene?.FrameBottom ?? 0;
        // The game's camera sees 10.8 units top to bottom; alone the frame is the x1 picture, 50 pixels a unit.
        double ppu = scene != null ? screenH / 10.8 : 50.0;
        double toCanvas = WetDrops.PixelsPerUnit / ppu;
        // One of the NPC's own units on the screen: a hundred of its art's pixels (its sprite's 100 a unit), as the art.
        double unit = 100.0 * NpcRender.ScreenPerArtPixel(levelScale);
        double wx = frameLeft + rect.left + rect.width / 2.0 + at.X * unit;
        double wy = frameBottom + rect.bottom + rect.height / 2.0 + at.Y * unit;
        uint seed = 2166136261;
        foreach (char ch in key) seed = (seed ^ ch) * 16777619;
        float tr = 1f, tg = 1f, tb = 1f, ta = 1f;
        try { if (!string.IsNullOrWhiteSpace(look.Tint)) (tr, tg, tb, ta) = BustComposer.ParseTint(look.Tint); }
        catch (FormatException) { }
        catch (ArgumentException) { }
        double own = shower ? WetDrops.ShowerOwnScale : 1.0;
        var e = new WetDrops.Emitter(wx * toCanvas, (screenH - wy) * toCanvas, 1, 0, 0, -1,
                                     at.ScaleX * own, at.ScaleY * own, seed, wet.StartActive ? 1.0 : 0.4,
                                     SMSModForge.Model.WetEffectDef.DefaultWidth(look.Kind), SMSModForge.Model.WetEffectDef.DefaultHeight(look.Kind),
                                     shower ? WetDrops.Kind.Shower : WetDrops.Kind.Towel, false, false, tr, tg, tb, ta,
                                     look.Amount ?? 1f, look.Size ?? 1f, look.Fall ?? 1f, Unlit: light == null);
        emitter = e;
        double canvasW = screenW * toCanvas, canvasH = screenH * toCanvas;
        var list = new System.Collections.Generic.List<WetDrops.Drop>();
        WetDrops.At(t, e, list, 0, 0, canvasW, canvasH);
        if (list.Count == 0) return null;
        if (light != null)
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (!d.Lit) continue;
                // Lit where it is, as the NPC's art is: its colour in linear light times the layer's light there.
                var (lr, lg, lb) = light.At((float)(d.X / toCanvas), (float)(screenH - d.Y / toCanvas));
                d.R = SrgbOf(WetDrops.Linear(d.R) * lr);
                d.G = SrgbOf(WetDrops.Linear(d.G) * lg);
                d.B = SrgbOf(WetDrops.Linear(d.B) * lb);
                d.Lit = false;
                list[i] = d;
            }
        return new WetDrops.Over(list.ToArray(), list.Count, mips, canvasW, canvasH);
    }

    /// <summary>A linear channel back in sRGB, not cut at 1 (a light can be brighter than white): what WetDrops.Linear
    /// takes back to the same number.</summary>
    private static float SrgbOf(float linear)
        => linear <= 0.0031308f ? 12.92f * linear : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;

    /// <summary>The drops over the NPC drawn alone, or none.</summary>
    private void ShowDropsAlone(WetDrops.Over? drops)
    {
        if (drops == null)
        {
            _dropsAlone.Visibility = Visibility.Collapsed;
            return;
        }
        const int N = BackdropRender.FrameSize;
        _dropsOutput ??= new byte[N * N * 4];
        _dropsBitmap ??= new WriteableBitmap(N, N, 96, 96, PixelFormats.Pbgra32, null);
        Array.Clear(_dropsOutput);
        WetDrops.BlendAlone(_dropsOutput, N, drops);
        _dropsBitmap.WritePixels(new Int32Rect(0, 0, N, N), _dropsOutput, N * 4, 0);
        if (!ReferenceEquals(_dropsAlone.Source, _dropsBitmap)) _dropsAlone.Source = _dropsBitmap;
        _dropsAlone.Width = N; _dropsAlone.Height = N;
        Canvas.SetLeft(_dropsAlone, 0); Canvas.SetTop(_dropsAlone, 0);
        _dropsAlone.Visibility = Visibility.Visible;
    }

    /// <summary>The blink overlay (256² straight-alpha) to composite this frame,
    /// or null. Reproduces the game's BlinkingSprite: eyes open a random
    /// min..max seconds, shut for the hold, repeat.</summary>
    private byte[]? BlinkOverlayForThisFrame(double nowSec)
    {
        if (_blinkTex == null) { _blinkClosed = false; _blinkNextSec = 0; return null; }
        if (_blinkNextSec == 0) { _blinkNextSec = nowSec + NextOpen(); _blinkClosed = false; }
        else if (nowSec >= _blinkNextSec)
        {
            _blinkClosed = !_blinkClosed;
            double hold = Npc?.BlinkHold ?? 0.2;
            _blinkNextSec = nowSec + (_blinkClosed ? hold : NextOpen());
        }
        return _blinkClosed ? _blinkTex : null;
    }

    private double NextOpen()
    {
        double min = Npc?.BlinkMinWait ?? 2, max = Npc?.BlinkMaxWait ?? 5;
        if (max < min) (min, max) = (max, min);
        return min + _rng.NextDouble() * (max - min);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string Norm(string p) => p?.Replace('/', Path.DirectorySeparatorChar) ?? "";

    private static (int w, int h) PngSize(string abs)
    {
        try
        {
            using var s = File.OpenRead(abs);
            var dec = new PngBitmapDecoder(s, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var f = dec.Frames[0];
            return (f.PixelWidth, f.PixelHeight);
        }
        catch { return (1, 1); }
    }
}
