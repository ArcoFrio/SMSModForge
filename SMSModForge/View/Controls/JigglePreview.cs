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

    /// <summary>
    /// Render one of the game's own busts by name, from the shipped art,
    /// instead of the pack's sprites. Blank means the ordinary pack path.
    /// <para/>
    /// Set for a character that borrows a vanilla bust. Everything else about
    /// the control is unchanged, so a borrowed bust blinks, mouths and breathes
    /// exactly as a pack-drawn one does — the alternative was a second, lesser
    /// preview that only did expressions.
    /// </summary>
    public static readonly DependencyProperty VanillaBustKeyProperty =
        DependencyProperty.Register(nameof(VanillaBustKey), typeof(string), typeof(JigglePreview),
            new PropertyMetadata(null, (d, e) => ((JigglePreview)d).ReloadTextures()));

    public string? VanillaBustKey
    {
        get => (string?)GetValue(VanillaBustKeyProperty);
        set => SetValue(VanillaBustKeyProperty, value);
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

    // Matches SpriteManager: y = sin(Time.time * speed) * amplitude, applied as
    // a world-space position offset (not a squash), with the inspector's
    // speed 3 and amplitude 0.1 world units.
    //
    // 0.1 world units is 10 SOURCE pixels at 100 PPU — but the preview bitmap
    // is RenderSize (2x Size) shown one bitmap pixel per WPF unit, so the
    // offset has to be in render pixels or the preview breathes at half depth.
    //
    // The game also varies both per character, always DOWNWARD from these
    // (amplitude x (1 - variation x rand01)); the preview shows the nominal
    // value rather than picking a random one, so what you author is what you see.
    private const double BreathingSpeed = 3.0;

    /// <summary>
    /// Breathing depth in preview pixels.
    /// <para/>
    /// Exposed rather than derived because the world-units-to-pixels ratio
    /// isn't knowable from the pack: in game the offset is applied to the
    /// busts' PARENT (2_Bust_Manager), so what it works out to on screen
    /// depends on that transform's scale and the camera, neither of which the
    /// editor can see. Dial it until the preview matches and leave it there.
    /// </summary>
    public static readonly DependencyProperty BreathingAmplitudeProperty =
        DependencyProperty.Register(nameof(BreathingAmplitude), typeof(double), typeof(JigglePreview),
            new PropertyMetadata(4.0));

    public double BreathingAmplitude
    {
        get => (double)GetValue(BreathingAmplitudeProperty);
        set => SetValue(BreathingAmplitudeProperty, value);
    }
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
                {
                    ReloadTextures();
                    return;
                }

                // The mask editor hands the preview a live buffer while it is
                // open and clears it on close. At that moment the preview falls
                // back to the file it last READ — which, for a mask that
                // already had a path, was read before any of this editing
                // happened. The picture would change the instant the editor
                // closed, back to the mask as it was, and look like the work
                // had been thrown away.
                //
                // One read, when the live buffer goes away. Not on
                // LiveMaskRevision, which fires per brush stamp.
                if (e.PropertyName == nameof(OutfitViewModel.LiveMaskBgra) &&
                    Outfit.LiveMaskBgra == null)
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
        if (!string.IsNullOrWhiteSpace(VanillaBustKey)) { ReloadVanillaTextures(); return; }
        if (Outfit == null || PackRoot == null)
        {
            // Clear the cached buffers too: leaving them set is what let a
            // half-configured bust keep showing its predecessor's sprites.
            _base = _mask = _blink = null;
            for (int i = 1; i <= 4; i++) _mouth[i] = null;
            _expressions.Clear();
            _vanillaJiggle = null;
            return;
        }
        _vanillaJiggle = null;
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

    /// <summary>
    /// Load a borrowed bust from the shipped vanilla art instead of the pack.
    /// <para/>
    /// The layout is fixed rather than authored — Base, Blink, Mouth1..4 and
    /// Expression&lt;Name&gt; under a folder named for the bust — so there is no
    /// OutfitDef to read paths off, and none is invented. Everything downstream
    /// is identical to a pack bust, which is the point: blink, mouth and
    /// breathing behave the same because it is the same code.
    /// <para/>
    /// No mask is exported, and none is faked. The shader treats an absent mask
    /// as zero displacement, so a vanilla bust simply does not jiggle here —
    /// preferable to inventing motion the game does not have.
    /// </summary>
    private void ReloadVanillaTextures()
    {
        _base = _mask = _blink = null;
        for (int i = 1; i <= 4; i++) _mouth[i] = null;
        _expressions.Clear();

        string? root = Rendering.VanillaArtResolver.FindArtRoot();
        if (root == null) return;
        string dir = Path.Combine(root, VanillaBustKey);
        if (!Directory.Exists(dir)) return;

        _base  = LoadIfExists(Path.Combine(dir, "Base.PNG"));
        // Absent on art exported before the mask was added, in which case the
        // bust simply holds still — the shader reads a missing mask as zero
        // displacement, so nothing needs a version check.
        _mask  = LoadIfExists(Path.Combine(dir, "Mask.PNG"));
        _blink = LoadIfExists(Path.Combine(dir, "Blink.PNG"));
        for (int i = 1; i <= 4; i++)
            _mouth[i] = LoadIfExists(Path.Combine(dir, "Mouth" + i + ".PNG"));
        foreach (var name in ExpressionSpec.Names)
        {
            var px = LoadIfExists(Path.Combine(dir, "Expression" + name + ".PNG"));
            if (px != null) _expressions[name] = px;
        }
        _vanillaJiggle = LoadJiggleSettings(Path.Combine(dir, "Jiggle.txt"));
    }

    /// <summary>The borrowed bust's own shader uniforms, or null to fall back
    /// to the outfit's.</summary>
    private JiggleParams? _vanillaJiggle;

    /// <summary>
    /// Read the uniforms the extractor wrote beside a vanilla bust's art.
    /// <para/>
    /// Returns null when the file is absent — art exported before masks were
    /// added — so the caller keeps its existing default rather than snapping a
    /// bust to zero. Unknown keys are ignored, so the format can gain fields
    /// without older editors choking on them.
    /// </summary>
    private static JiggleParams? LoadJiggleSettings(string abs)
    {
        if (!File.Exists(abs)) return null;
        var p = new JiggleParams();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            foreach (var raw in File.ReadAllLines(abs))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (string.Equals(key, "Tint", System.StringComparison.OrdinalIgnoreCase))
                { p.Tint = val; continue; }
                if (!float.TryParse(val, System.Globalization.NumberStyles.Float, inv, out var f)) continue;
                switch (key)
                {
                    case "JiggleSpeed":     p.Speed = f; break;
                    case "JiggleStrength":  p.Strength = f; break;
                    case "JiggleFrequency": p.Frequency = f; break;
                    case "NoiseScale":      p.NoiseScale = f; break;
                    case "NoiseSpeed":      p.NoiseSpeed = f; break;
                    case "NoiseStrength":   p.NoiseStrength = f; break;
                }
            }
        }
        catch { return null; }
        return p;
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

    /// <summary>
    /// Composite one overlay onto the already-displaced body, shifted by the
    /// jiggle evaluated at that overlay's own centroid — the game's per-object
    /// displacement, as opposed to the body's per-pixel one.
    /// </summary>
    private static void CompositeOverlay(byte[] output, byte[]? overlay, byte[] mask,
                                         JiggleParams jiggle, float time)
    {
        if (overlay == null) return;
        // Composited where it was authored, undisplaced — the game leaves bust
        // overlays out of the jiggle (see BustFactory's applyToOverlays, now off
        // by default), so displacing them here would show motion that does not
        // happen. Still composited AFTER the shader pass rather than layered
        // into it: that keeps them undeformed, which is the point.
        BustComposer.Composite(output, overlay);
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
        // A borrowed bust jiggles by the game's own numbers, not the pack's
        // defaults — those would be motion the game never gives it.
        var jiggle = _vanillaJiggle ?? Outfit.Model.Jiggle;
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
            // Body first, displaced per pixel — then each overlay composited on
            // top with its OWN rigid offset.
            //
            // This mirrors what the game does. Overlays don't carry the jiggle
            // material: its lit pass gathers the sprite's own texture weighted by
            // (1 - alpha), so a mouth covering a fraction of a percent of its
            // canvas gets lit as though floating in void and shifts colour, where
            // the body covers about 27%. The game therefore leaves them on the
            // stock sprite shader and moves each one rigidly by the jiggle
            // sampled at its own centroid.
            //
            // The preview used to layer the overlays onto the body and displace
            // the lot in one pass, which matched the old share-the-material
            // build. Against the current runtime that overstates the motion — it
            // deforms overlays per pixel where the game translates them whole.
            JiggleShader.Render(baseSnap, maskSnap, jiggle, tint, time, output, superSample);

            // Expression composites first (below blink and mouth).
            CompositeOverlay(output, expression, maskSnap, jiggle, time);
            CompositeOverlay(output, blink, maskSnap, jiggle, time);
            CompositeOverlay(output, mouth, maskSnap, jiggle, time);
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
