using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// A live preview of a single outfit. Renders the JiggleSprite shader on the
/// CPU every <see cref="DispatcherTimer"/> tick and composites blink / mouth /
/// expression overlays on top.
/// </summary>
public sealed class JigglePreview : Image
{
    public static readonly DependencyProperty OutfitProperty =
        DependencyProperty.Register(nameof(Outfit), typeof(OutfitViewModel), typeof(JigglePreview),
            new PropertyMetadata(null, (d, e) => ((JigglePreview)d).OnOutfitChanged()));

    public OutfitViewModel? Outfit
    {
        get => (OutfitViewModel?)GetValue(OutfitProperty);
        set => SetValue(OutfitProperty, value);
    }

    public static readonly DependencyProperty PackRootProperty =
        DependencyProperty.Register(nameof(PackRoot), typeof(string), typeof(JigglePreview),
            new PropertyMetadata(null, (d, e) => ((JigglePreview)d).ReloadTextures()));

    public string? PackRoot
    {
        get => (string?)GetValue(PackRootProperty);
        set => SetValue(PackRootProperty, value);
    }

    public static readonly DependencyProperty SelectedExpressionProperty =
        DependencyProperty.Register(nameof(SelectedExpression), typeof(string), typeof(JigglePreview),
            new PropertyMetadata("None"));

    public string SelectedExpression
    {
        get => (string)GetValue(SelectedExpressionProperty);
        set => SetValue(SelectedExpressionProperty, value);
    }

    public static readonly DependencyProperty SelectedMouthFrameProperty =
        DependencyProperty.Register(nameof(SelectedMouthFrame), typeof(int), typeof(JigglePreview),
            new PropertyMetadata(0));

    /// <summary>0 = closed (no mouth overlay), 1..4 = mouth frame index.</summary>
    public int SelectedMouthFrame
    {
        get => (int)GetValue(SelectedMouthFrameProperty);
        set => SetValue(SelectedMouthFrameProperty, value);
    }

    public static readonly DependencyProperty BlinkingProperty =
        DependencyProperty.Register(nameof(Blinking), typeof(bool), typeof(JigglePreview),
            new PropertyMetadata(false));

    public bool Blinking
    {
        get => (bool)GetValue(BlinkingProperty);
        set => SetValue(BlinkingProperty, value);
    }

    public static readonly DependencyProperty YappingProperty =
        DependencyProperty.Register(nameof(Yapping), typeof(bool), typeof(JigglePreview),
            new PropertyMetadata(false));

    /// <summary>When true, cycles the mouth frames like the game's
    /// <c>MouthTalkAnimation</c>; overrides <see cref="SelectedMouthFrame"/>.</summary>
    public bool Yapping
    {
        get => (bool)GetValue(YappingProperty);
        set => SetValue(YappingProperty, value);
    }

    public static readonly DependencyProperty BreathingProperty =
        DependencyProperty.Register(nameof(Breathing), typeof(bool), typeof(JigglePreview),
            new PropertyMetadata(true));

    public bool Breathing
    {
        get => (bool)GetValue(BreathingProperty);
        set => SetValue(BreathingProperty, value);
    }

    // SpriteManager inspector values: speed 3 rad/s, amplitude 0.1 world units.
    // At 100 PPU the sprite is 256 px → 0.1 × 100 = 10 source px.
    private const double BreathingSpeed = 3.0;
    private const double BreathingAmplitude = 10.0;
    private readonly TranslateTransform _breathingTransform = new();

    private readonly WriteableBitmap _bitmap;
    private readonly byte[] _jiggleOutput = new byte[JiggleShader.RenderStride * JiggleShader.RenderSize];
    private bool _renderingHooked;

    // Off-thread render plumbing. The shader pass + alpha composites take far
    // longer than a single vsync interval on the CPU, so running them on the
    // UI thread saturates it (input delays, cursor judder during mask paint).
    // Instead, each Rendering tick kicks off a Task that does shader+composite
    // on the thread pool; the continuation marshals back to the UI thread to
    // blit the result. While one pass is in flight, subsequent ticks no-op —
    // so the preview runs at whatever framerate the shader can sustain while
    // the UI keeps refreshing at full vsync.
    private bool _renderInFlight;
    private TaskScheduler? _uiScheduler;

    // Frame-rate cap. CompositionTarget.Rendering fires at the display refresh
    // (~60 Hz), but the shader pass is the expensive part — rendering it every
    // vsync keeps a chunk of every core busy back-to-back. We gate the shader
    // to the active preview-quality preset's MaxFps (the breathing transform
    // below stays at full vsync — it's a cheap GPU-side translate). Tracked in
    // wall-clock ms via the monotonic tick counter.
    private long _lastShaderTickMs;

    // Cached textures keyed by absolute path. Reloaded when PackRoot / outfit paths change.
    private byte[]? _base, _mask, _blink;
    private readonly byte[]?[] _mouth = new byte[]?[5];   // index 1..4 used
    private readonly System.Collections.Generic.Dictionary<string, byte[]> _expressions = new();

    private DateTime _startTime = DateTime.Now;

    // Blink scheduler — mirrors the game's BlinkingSprite component, which
    // toggles the eyes-closed overlay's alpha: hold open a random 2–5 s, blink
    // shut for 0.2 s, loop. We render the overlay only during the 0.2 s closed
    // windows instead of compositing it statically. Driven by the monotonic
    // wall clock so it's unaffected by start-time resets.
    private bool _blinkActive;
    private bool _blinkClosed;
    private double _blinkNextChangeSec;
    private readonly Random _rng = new();

    // Yapping scheduler — mirrors the game's MouthTalkAnimation: every random
    // 0.05–0.2 s, switch to one random mouth frame. Overrides the manual mouth
    // picker while active; resets (mouth closed) when turned off.
    private bool _yapActive;
    private int _yapFrame;
    private double _yapNextChangeSec;

    /// <summary>
    /// Fixed display size of the preview, in DIPs. Matches
    /// <see cref="JiggleShader.RenderSize"/> exactly so the shader output
    /// is displayed 1:1 — no WPF-side resampling between the CPU pass and
    /// the screen, which keeps motion crisp and avoids any rounding the
    /// compositor might do at fractional ratios.
    /// <para/>
    /// Pinned with matching Min/Max bounds so no parent layout can stretch
    /// or squeeze the control. WPF's measure pass uses the Min/Max range
    /// when negotiating with parents; with Min == Max == Width, there is
    /// no slack for a DockPanel, GridSplitter, or ScrollViewer to claw at.
    /// </summary>
    public const double FixedSize = JiggleShader.RenderSize;

    public JigglePreview()
    {
        _bitmap = new WriteableBitmap(JiggleShader.RenderSize, JiggleShader.RenderSize, 96, 96, PixelFormats.Pbgra32, null);
        Source = _bitmap;
        // Stretch.None displays the bitmap at its natural pixel size. Combined
        // with Width=Height=RenderSize, that's exactly one device pixel per
        // bitmap pixel — no WPF resampling at all. NearestNeighbor is still
        // set as a belt-and-braces in case the device-pixel mapping ever isn't
        // 1:1 (HiDPI fractional scaling, etc.).
        Stretch = Stretch.None;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderTransform = _breathingTransform;

        // Drive the animation off WPF's compositor instead of a
        // DispatcherTimer. CompositionTarget.Rendering fires once per
        // render pass (vsync-locked to the display, ~60 Hz on most
        // monitors), so:
        //   • No drift between our tick rate and the screen refresh,
        //     which is what the old 30 FPS DispatcherTimer-driven setup
        //     suffered from (irregular missed frames perceived as judder).
        //   • Higher effective framerate, which halves the per-frame
        //     jiggle displacement — the eye reads that as crisp motion
        //     rather than the blurry between-frame smearing you get at
        //     30 FPS.
        // We hook on Loaded and unhook on Unloaded so a hidden tab isn't
        // burning CPU on shader passes nobody can see.
        // Gate on real visibility, not just Loaded: an unselected tab keeps its
        // content loaded-but-hidden, and we don't want the shader running for a
        // preview nobody can see.
        IsVisibleChanged += (_, _) => { if (IsVisible) HookRendering(); else UnhookRendering(); };

        // Hard-pin the size. Width/Height alone aren't enough when a parent
        // layout (e.g. a DockPanel that wants to fill remaining space) tries
        // to negotiate — the Min/Max bounds force WPF's measure pass to
        // settle on exactly FixedSize regardless of available room.
        Width = MinWidth = MaxWidth = FixedSize;
        Height = MinHeight = MaxHeight = FixedSize;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>
    /// VM property names that point at a file on disk — only these warrant a
    /// PNG reload. Lots of other properties (jiggle sliders, the mask editor's
    /// per-stamp <c>LiveMaskRevision</c> bump) change far too often to do file
    /// I/O off.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> _texturePathProps = new()
    {
        nameof(OutfitViewModel.BaseSprite),
        nameof(OutfitViewModel.MaskSprite),
        nameof(OutfitViewModel.BlinkSprite),
        nameof(OutfitViewModel.MouthEnabled),
        nameof(OutfitViewModel.MouthPrefix),
        nameof(OutfitViewModel.ExpressionEnabled),
        nameof(OutfitViewModel.ExpressionPrefix),
    };

    private void OnOutfitChanged()
    {
        if (Outfit != null)
        {
            Outfit.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is null || _texturePathProps.Contains(e.PropertyName))
                    ReloadTextures();
            };
        }
        ReloadTextures();
    }

    /// <summary>Bumped whenever the inputs change, so a shader pass started
    /// against the previous outfit can't paint its result over the new one.</summary>
    private int _renderGeneration;

    private void ReloadTextures()
    {
        _renderGeneration++;
        if (Outfit == null || PackRoot == null)
        {
            // Clear the cached buffers too: leaving them set is what let a
            // half-configured bust keep showing its predecessor's sprites.
            _base = _mask = _blink = null;
            for (int i = 1; i <= 4; i++) _mouth[i] = null;
            _expressions.Clear();
            return;
        }
        var m = Outfit.Model;
        _base = LoadIfExists(Path.Combine(PackRoot, Normalize(m.BaseSprite)));
        _mask = LoadIfExists(Path.Combine(PackRoot, Normalize(m.MaskSprite)));
        _blink = LoadIfExists(Path.Combine(PackRoot, Normalize(m.BlinkSprite)));
        for (int i = 1; i <= 4; i++)
            _mouth[i] = m.Mouth.Enabled
                ? LoadIfExists(Path.Combine(PackRoot, Normalize(m.Mouth.Prefix) + i + ".PNG"))
                : null;
        _expressions.Clear();
        if (m.Expression.Enabled)
            foreach (var name in ExpressionSpec.Names)
                _expressions[name] = LoadIfExists(Path.Combine(PackRoot, Normalize(m.Expression.Prefix) + name + ".PNG")) ?? Empty();
    }

    private static string Normalize(string p) => p?.Replace('/', Path.DirectorySeparatorChar) ?? "";

    private static byte[]? LoadIfExists(string abs)
        => File.Exists(abs) ? BustComposer.LoadPng(abs) : null;

    private static byte[] Empty() => new byte[JiggleShader.Stride * JiggleShader.Size];

    /// <summary>
    /// Decides whether the eyes-closed (blink) overlay should be composited this
    /// frame, reproducing the game's <c>BlinkingSprite</c> coroutine: start with
    /// eyes open, hold open a random 2–5 s, blink shut for 0.2 s, then repeat.
    /// Returns the overlay only during the 0.2 s closed windows. Turning blinking
    /// off (or having no blink sprite) resets to "open" so re-enabling starts a
    /// fresh cycle, matching the component's <c>OnEnable</c>.
    /// </summary>
    private byte[]? BlinkOverlayForThisFrame(double nowSec)
    {
        if (!Blinking || _blink == null)
        {
            _blinkActive = false;
            return null;
        }
        if (!_blinkActive)
        {
            _blinkActive = true;
            _blinkClosed = false;                              // eyes open first
            _blinkNextChangeSec = nowSec + NextOpenSeconds();
        }
        else if (nowSec >= _blinkNextChangeSec)
        {
            _blinkClosed = !_blinkClosed;
            _blinkNextChangeSec = nowSec + (_blinkClosed ? 0.2 : NextOpenSeconds());
        }
        return _blinkClosed ? _blink : null;
    }

    // BlinkingSprite uses UnityEngine.Random.Range(2f, 5f) for the open interval.
    private double NextOpenSeconds() => 2.0 + _rng.NextDouble() * 3.0;

    /// <summary>
    /// Picks the mouth overlay for this frame. With <see cref="Yapping"/> off,
    /// honours the manual <see cref="SelectedMouthFrame"/>. With it on, cycles
    /// to a random loaded frame every random 0.05–0.2 s, reproducing the game's
    /// <c>MouthTalkAnimation</c> (which hides all mouth children then activates
    /// one at random each tick). Resets to closed when toggled off.
    /// </summary>
    private byte[]? MouthOverlayForThisFrame(double nowSec)
    {
        if (!Yapping)
        {
            _yapActive = false;
            return (SelectedMouthFrame is >= 1 and <= 4) ? _mouth[SelectedMouthFrame] : null;
        }
        if (!_yapActive || nowSec >= _yapNextChangeSec)
        {
            _yapActive = true;
            int f = PickRandomLoadedMouthFrame();
            if (f != 0) _yapFrame = f;
            _yapNextChangeSec = nowSec + (0.05 + _rng.NextDouble() * 0.15);   // Random.Range(0.05, 0.2)
        }
        return (_yapFrame is >= 1 and <= 4) ? _mouth[_yapFrame] : null;
    }

    /// <summary>A random 1..4 frame index that's actually loaded, or 0 if none.</summary>
    private int PickRandomLoadedMouthFrame()
    {
        int n = 0;
        for (int i = 1; i <= 4; i++) if (_mouth[i] != null) n++;
        if (n == 0) return 0;
        int pick = _rng.Next(n);
        for (int i = 1; i <= 4; i++)
            if (_mouth[i] != null && pick-- == 0) return i;
        return 0;
    }

    /// <summary>All-zero mask: alpha 0 everywhere, so every displacement term
    /// falls out and the base renders undistorted. Shared and never written.</summary>
    private static readonly byte[] NoMask = new byte[JiggleShader.Stride * JiggleShader.Size];

    private bool _bitmapCleared;

    /// <summary>Blank the preview. The bitmap holds the last frame written to
    /// it, so a bust with nothing to draw has to be actively cleared or the
    /// PREVIOUS bust stays on screen looking like the current one.</summary>
    private void ClearBitmap()
    {
        if (_bitmapCleared) return;
        _bitmap.Lock();
        try
        {
            _bitmap.WritePixels(
                new Int32Rect(0, 0, JiggleShader.RenderSize, JiggleShader.RenderSize),
                new byte[JiggleShader.RenderStride * JiggleShader.RenderSize],
                JiggleShader.RenderStride, 0);
        }
        finally { _bitmap.Unlock(); }
        _bitmapCleared = true;
    }

    private void Render()
    {
        // Nothing to draw yet — an outfit whose base sprite isn't set. Checked
        // before the in-flight guard so switching to an empty bust blanks
        // immediately instead of waiting on the outgoing one's shader pass.
        if (Outfit == null || _base == null)
        {
            _renderGeneration++;      // discard whatever is in flight
            ClearBitmap();
            return;
        }

        // Skip this tick if the previous shader pass hasn't finished yet —
        // keeps the UI thread free even when the shader runs slower than vsync.
        if (_renderInFlight) return;

        // Respect the preview-quality frame cap (read live, so switching
        // presets takes effect on the next tick). Below the cap interval we
        // simply skip the shader pass for this vsync.
        var quality = Services.PreviewQualityManager.Current;
        long nowMs = Environment.TickCount64;
        if (nowMs - _lastShaderTickMs < 1000L / quality.MaxFps) return;
        _lastShaderTickMs = nowMs;

        // The mask editor publishes its in-progress buffer through the VM —
        // prefer it when set so unsaved brush strokes appear immediately.
        // A missing mask means "no jiggle", not "don't draw" — the base should
        // appear the moment it's set, with each further sprite layering in as
        // it's filled rather than the whole preview waiting on the last one.
        byte[] mask = Outfit.LiveMaskBgra ?? _mask ?? NoMask;

        // Snapshot every input on the UI thread before handing off. Field
        // assignments here are local refs, so a subsequent ReloadTextures()
        // or outfit swap will spin up the next render against new buffers
        // without disturbing the in-flight pass.
        byte[] baseSnap = _base;
        byte[] maskSnap = mask;
        var jiggle = Outfit.Model.Jiggle;
        var tint = BustComposer.ParseTint(Outfit.Tint);
        float time = (float)(DateTime.Now - _startTime).TotalSeconds;
        byte[]? expression = (SelectedExpression is not (null or "None")
                              && _expressions.TryGetValue(SelectedExpression, out var exp)) ? exp : null;
        byte[]? blink = BlinkOverlayForThisFrame(nowMs / 1000.0);
        byte[]? mouth = MouthOverlayForThisFrame(nowMs / 1000.0);
        var output = _jiggleOutput;
        bool superSample = quality.SuperSample;

        _uiScheduler ??= TaskScheduler.FromCurrentSynchronizationContext();
        _renderInFlight = true;
        // Which outfit this pass belongs to. A shader pass outlives a fast
        // outfit switch, and without this its result would land on screen after
        // the switch — the old bust reappearing over the new one.
        int generation = _renderGeneration;

        Task.Run(() =>
        {
            JiggleShader.Render(baseSnap, maskSnap, jiggle, tint, time, output, superSample);
            // Expression composites first (below blink and mouth).
            if (expression != null) BustComposer.Composite(output, expression);
            if (blink != null) BustComposer.Composite(output, blink);
            if (mouth != null) BustComposer.Composite(output, mouth);
        }).ContinueWith(t =>
        {
            try
            {
                if (t.IsCompletedSuccessfully && generation == _renderGeneration)
                {
                    _bitmap.Lock();
                    try
                    {
                        _bitmap.WritePixels(
                            new Int32Rect(0, 0, JiggleShader.RenderSize, JiggleShader.RenderSize),
                            output, JiggleShader.RenderStride, 0);
                    }
                    finally { _bitmap.Unlock(); }
                    _bitmapCleared = false;
                }
            }
            finally { _renderInFlight = false; }
        }, _uiScheduler);
    }

    /// <summary>
    /// Subscribe to <see cref="CompositionTarget.Rendering"/> exactly once.
    /// The compositor fires this event once per WPF render pass, so we get
    /// vsync-locked frames without manual timing.
    /// </summary>
    private void HookRendering()
    {
        if (_renderingHooked) return;
        CompositionTarget.Rendering += OnCompositionRendering;
        _renderingHooked = true;
    }

    private void UnhookRendering()
    {
        if (!_renderingHooked) return;
        CompositionTarget.Rendering -= OnCompositionRendering;
        _renderingHooked = false;
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        if (Breathing)
        {
            double t = (DateTime.Now - _startTime).TotalSeconds;
            _breathingTransform.Y = -Math.Sin(t * BreathingSpeed) * BreathingAmplitude;
        }
        else if (_breathingTransform.Y != 0)
        {
            _breathingTransform.Y = 0;
        }
        Render();
    }
}
