using System;
using System.IO;
using System.Linq;
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
            new PropertyMetadata(null, (d, e) => ((JigglePreview)d).OnOutfitChanged(e.OldValue as OutfitViewModel)));

    public OutfitViewModel? Outfit
    {
        get => (OutfitViewModel?)GetValue(OutfitProperty);
        set => SetValue(OutfitProperty, value);
    }

    /// <summary>
    /// A backdrop of its own instead of the one chosen under Options (1.10.0): the place preview stands a character
    /// in a place of the pack's with it (<see cref="Rendering.PreviewBackdrop.ForPlace"/>). Null: the chosen one.
    /// </summary>
    internal static readonly DependencyProperty SceneOverrideProperty =
        DependencyProperty.Register(nameof(SceneOverride), typeof(object), typeof(JigglePreview), new PropertyMetadata(null));

    internal Rendering.BackdropScene? SceneOverride
    {
        get => GetValue(SceneOverrideProperty) as Rendering.BackdropScene;
        set => SetValue(SceneOverrideProperty, value);
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

    /// <summary>
    /// Show the bust appearing the way the game shows it (1.9.1, asked by
    /// ArcoFrio): each time an outfit is shown, and when this is switched on, a
    /// black silhouette fades in over a second and the character fades in from
    /// it over three (<see cref="IntroFade"/>). Off by default; the Characters
    /// tab's toggle starts it on.
    /// </summary>
    public static readonly DependencyProperty FadesInProperty =
        DependencyProperty.Register(nameof(FadesIn), typeof(bool), typeof(JigglePreview),
            new PropertyMetadata(false, (d, e) =>
            {
                var preview = (JigglePreview)d;
                if ((bool)e.NewValue) preview.StartIntro();
                else preview._introStart = null;
            }));

    public bool FadesIn
    {
        get => (bool)GetValue(FadesInProperty);
        set => SetValue(FadesInProperty, value);
    }

    // When the fade-in under way began, on IntroNow's clock; null for none.
    private double? _introStart;
    private readonly System.Diagnostics.Stopwatch _introClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>The fade-in's clock, in seconds. A seam for tests, which hold
    /// the moment still to read the picture at it.</summary>
    internal Func<double> IntroNow;

    /// <summary>The body's alpha as displaced, kept for the silhouette.</summary>
    private byte[]? _bodyAlpha;

    private void StartIntro()
    {
        if (FadesIn && Outfit != null) _introStart = IntroNow();
    }

    /// <summary>Where the fade-in under way stands: whether the body shows, and
    /// the silhouette's opacity. (true, 0) when none is.</summary>
    private (bool BodyShows, float Silhouette) IntroPhase()
    {
        if (!FadesIn || _introStart == null) return (true, 0f);
        double t = IntroNow() - _introStart.Value;
        if (t >= IntroFade.TotalSeconds)
        {
            _introStart = null;
            return (true, 0f);
        }
        return IntroFade.At(t);
    }

    public static readonly DependencyProperty SelectedExpressionProperty =
        DependencyProperty.Register(nameof(SelectedExpression), typeof(string), typeof(JigglePreview),
            new PropertyMetadata(""));

    /// <summary>The face to composite, by the name the game gives it; empty
    /// for none, which is the bust's ordinary face rather than a sprite.</summary>
    public string SelectedExpression
    {
        get => (string)GetValue(SelectedExpressionProperty);
        set => SetValue(SelectedExpressionProperty, value);
    }

    /// <summary>One entry in the preview's expression list: what is stored, and
    /// what the author reads.</summary>
    public sealed record ExpressionChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly DependencyPropertyKey ExpressionChoicesKey =
        DependencyProperty.RegisterReadOnly(nameof(ExpressionChoices),
            typeof(System.Collections.Generic.IReadOnlyList<ExpressionChoice>), typeof(JigglePreview),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ExpressionChoicesProperty = ExpressionChoicesKey.DependencyProperty;

    /// <summary>
    /// The faces THIS bust can pull, offered to the picker beside the preview.
    /// <para/>
    /// Published by the preview rather than written out in the window, because
    /// the preview is the only thing that knows. It had been five fixed entries
    /// in XAML — none, happy, angry, sad, flirty — which was wrong twice over.
    /// A bust with its expressions switched off, and one of the game's busts
    /// that has no expression children at all, both offered four faces that did
    /// nothing when picked; and what the list handed the preview was the
    /// entry's LABEL, so in any language but English it looked up "Feliz" among
    /// sprites named "Happy" and quietly found nothing. Every expression in the
    /// editor stopped working the moment somebody changed language.
    /// <para/>
    /// Taken from the textures actually loaded, which is the same list the
    /// runtime will look through, in the canonical order rather than whatever
    /// order the dictionary happens to hold.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<ExpressionChoice> ExpressionChoices
        => (System.Collections.Generic.IReadOnlyList<ExpressionChoice>)GetValue(ExpressionChoicesProperty);

    /// <summary>Re-publish the list after a texture load. "None" is first and
    /// always there: a bust with no expressions still has its ordinary face,
    /// and an empty box would read as the preview being broken.</summary>
    private void PublishExpressionChoices()
    {
        var choices = new System.Collections.Generic.List<ExpressionChoice>
            { new("", Localization.Loc.T("expressionIcon.neutral")) };   // the line editor's word for the plain face (it said None here)

        // Canonical order first - the four the game has, in the order the
        // editor names them everywhere else - then anything else the pack
        // brought with it, so a custom face is reachable rather than dropped.
        foreach (string name in ExpressionSpec.Names)
            if (_expressions.ContainsKey(name)) choices.Add(new(name, Label(name)));

        foreach (var name in _expressions.Keys)
            if (!choices.Any(c => c.Key == name)) choices.Add(new(name, Label(name)));

        SetValue(ExpressionChoicesKey, choices);
    }

    /// <summary>The four the editor has words for; anything else is shown as
    /// the pack spells it.</summary>
    private static string Label(string name) => name switch
    {
        "Happy" => Localization.Loc.T("characters.happy"),
        "Angry" => Localization.Loc.T("characters.angry"),
        "Sad" => Localization.Loc.T("characters.sad"),
        "Flirty" => Localization.Loc.T("characters.flirty"),
        _ => name,
    };

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

    private readonly WriteableBitmap _bitmap;

    /// <summary>Why the last frame that failed failed - for tests.</summary>
    internal Exception? LastFault { get; private set; }
    private readonly byte[] _jiggleOutput = new byte[JiggleShader.RenderStride * JiggleShader.RenderSize];

    // Over a backdrop (1.9.1): the frame in linear light, the intro's
    // silhouette beside it, and the body's alpha as the fake GI reads it.
    private float[]? _linear, _linearSilhouette, _coverage;
    private Rendering.GamePost.Target? _scene;
    private Rendering.BackdropScene? _sceneOf;
    // The bust's shadow from the room's lamp: what its art casts, the shadow
    // texture, and the fake GI's background as the shadow leaves it.
    private Rendering.BustShadow.Caster? _caster;
    private byte[]? _casterOf;
    private float[]? _shadowTexture, _shadowBackground;
    // The post-processing's bloom and flare buffers, kept frame to frame.
    private readonly Rendering.GamePost.Work _postWork = new();
    // The body on the graphics card (Services.PreviewRendererManager), made on first use.
    private Rendering.Gpu.GpuBody? _gpuBody;
    private Rendering.Gpu.GpuPost? _gpuPost;
    private Rendering.Gpu.GpuFrame? _gpuFrame;
    private Rendering.Gpu.GpuAlone? _gpuAlone;
    /// <summary>Whether the last frame's body was drawn on the graphics card - for tests.</summary>
    internal bool DrewOnCard { get; private set; }
    /// <summary>Pictures put on screen (Options ▸ Show preview frame rate).</summary>
    internal int FramesShown { get; private set; }
    private Rendering.FakeGi.Art? _giArt;
    private byte[]? _giArtOf;
    private int _giArtReach;
    /// <summary>Whether the last frame was drawn over a backdrop - then the bust
    /// breathes inside the picture, a whole screen pixel at a time, and the
    /// picture itself stays still.</summary>
    internal bool OverBackdrop { get; private set; }
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

    // At most the display's own rate, at every quality (1.9.1, the author: all
    // "capped at display refresh rate"): a frame is drawn on a Rendering tick
    // when the last one is done and the pacer says the display is due one -
    // Rendering alone can fire faster than the display (108 a second on the
    // test suite's desktop). What a quality saves is the work in a frame - see
    // PreviewQualityDef.
    private readonly FramePacer _pacer = new();

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
        // A tooltip that outlived its owner sits over the one thing an
        // author is trying to look at. See ToolTipDismisser.
        View.ToolTipDismisser.KeepClearOf(this);
        // The list of faces names them in the language on screen.
        Localization.LocText.Follow(this, PublishExpressionChoices);

        IntroNow = () => _introClock.Elapsed.TotalSeconds;
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
        FpsBadge.Attach(this, () => FramesShown);

        ApplyZoom();
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>
    /// How much bigger than <see cref="FixedSize"/> to draw the bust.
    /// <para/>
    /// The shader runs at its own size whatever this says: what changes is how
    /// many screen pixels one of its pixels covers. 1 is the size this control
    /// was pinned to for years and the only one that needs no resampling at
    /// all; 2 doubles every pixel, which is exact as well. The halves in
    /// between are a resample, and they say so by switching off nearest
    /// neighbour — a bust at 1.5 is a softer picture, not a broken one.
    /// </summary>
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(JigglePreview),
        new PropertyMetadata(1.0, (d, _) => ((JigglePreview)d).ApplyZoom()));

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Size the control to the zoom, and pin it there.
    /// <para/>
    /// Width/Height alone are not enough when a parent layout (a DockPanel
    /// filling remaining space, say) tries to negotiate: the Min/Max bounds
    /// are what force WPF's measure pass to settle on exactly this size
    /// regardless of the room on offer. That was the whole reason the control
    /// was pinned, and it still is — the pin now simply moves.
    /// <para/>
    /// Stretch follows: None at 1 keeps one bitmap pixel on one screen pixel,
    /// and anything else has to be scaled to the box.
    /// </summary>
    private void ApplyZoom()
    {
        double zoom = Zoom <= 0 ? 1 : Zoom;
        double side = FixedSize * zoom;

        Width = MinWidth = MaxWidth = side;
        Height = MinHeight = MaxHeight = side;

        Stretch = zoom == 1 ? Stretch.None : Stretch.Uniform;

        // Made bigger, drawn pixel by pixel - at a half step too. It was
        // smoothed there, to keep every pixel the same size, and the author
        // found it blurry (1.8.0): the game is pixel art, and a pixel a shade
        // wider than its neighbour reads as crisp where a smoothed one reads
        // as out of focus. Made smaller, it is smoothed, or whole rows go.
        // At its own size it takes what the frame around it says (a frame that
        // scales it, as the Characters tab's and the mask painter's do, 1.9.1).
        if (zoom == 1) ClearValue(RenderOptions.BitmapScalingModeProperty);
        else RenderOptions.SetBitmapScalingMode(
            this, zoom > 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
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
        // Raised when the character's faces change: a face added on the
        // Speech expressions list is one more file to look for.
        nameof(OutfitViewModel.ExpressionFilesHint),
        // The textures a pack replaces on one of the game's busts. Raised by
        // the outfit whenever one of its override rows changes, and on a
        // rebuild of the rows themselves.
        nameof(OutfitViewModel.Overrides),
    };

    /// <summary>
    /// The one handler on the outfit shown, moved to each new outfit and taken
    /// off the last (1.9.0). A new one was added every time an outfit was shown
    /// and none was ever removed, so every outfit ever looked at reloaded this
    /// preview's pictures on each of its own changes - each keystroke in a path
    /// box loaded the pictures once per outfit visited.
    /// </summary>
    private System.ComponentModel.PropertyChangedEventHandler? _watching;

    private void OnOutfitChanged(OutfitViewModel? old)
    {
        if (old != null && _watching != null) old.PropertyChanged -= _watching;
        _watching = null;
        if (Outfit != null)
        {
            _watching = (_, e) =>
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
                    Outfit?.LiveMaskBgra == null)
                    ReloadTextures();
            };
            Outfit.PropertyChanged += _watching;
        }
        ReloadTextures();
        // A bust shown is a bust switched on, in the game's terms: it fades in.
        StartIntro();
    }

    /// <summary>Bumped whenever the inputs change, so a shader pass started
    /// against the previous outfit can't paint its result over the new one.</summary>
    private int _renderGeneration;

    /// <summary>How many times the pictures were loaded - for tests.</summary>
    internal int TextureLoads { get; private set; }

    private void ReloadTextures()
    {
        TextureLoads++;
        LoadTextures();
    }

    // ── Loading the bust's pictures (off the interface's thread since 1.10.0) ──────────────────────
    //
    // Decoding a bust's PNGs - base, mask, blink, four mouths, every face - took a fifth of a second and more on the
    // interface's thread, so choosing a character waited for it (measured by UiDelayTests: "character You" 320 to
    // 780 ms). What to load is read here, on that thread, into plain values (the pack is being edited on it); the
    // decoding runs on a worker; the set is put in when it is done, unless another bust was asked for meanwhile.
    // Until then the preview shows nothing rather than the bust chosen before.

    /// <summary>Everything the preview draws a bust from, as one set put in at once.</summary>
    private sealed class TextureSet
    {
        public byte[]? Base, Mask, Blink;
        public readonly byte[]?[] Mouth = new byte[]?[5];
        public readonly System.Collections.Generic.Dictionary<string, byte[]> Faces = new();
        public bool PackReplacedMask;
        public JiggleParams? VanillaJiggle;
        /// <summary>The bust art's own size, which its mask is spread over (0: unknown, taken as square).</summary>
        public int ArtW, ArtH;
    }

    /// <summary>What a load reads, copied off the outfit on the interface's thread.</summary>
    private sealed record TextureInputs(
        string? VanillaBustKey, string PackRoot,
        string BaseSprite, string MaskSprite, string BlinkSprite,
        bool MouthEnabled, string MouthPrefix, bool ExpressionEnabled, string ExpressionPrefix,
        System.Collections.Generic.IReadOnlyList<string> PackFaces,
        System.Collections.Generic.IReadOnlyList<(string Slot, string Sprite)> Overrides);

    private Task<TextureSet>? _loading;

    /// <summary>Loads under way in every preview - the test harness waits for them as a person would for the picture.</summary>
    internal static int PendingLoads;

    private void LoadTextures()
    {
        _renderGeneration++;
        bool vanilla = !string.IsNullOrWhiteSpace(VanillaBustKey);
        if (!vanilla && (Outfit == null || PackRoot == null))
        {
            // Clear the cached buffers too: leaving them set is what let a
            // half-configured bust keep showing its predecessor's sprites.
            _loading = null;
            Put(new TextureSet());
            return;
        }
        var m = Outfit?.Model;
        var inputs = new TextureInputs(
            vanilla ? VanillaBustKey : null, PackRoot ?? "",
            m?.BaseSprite ?? "", m?.MaskSprite ?? "", m?.BlinkSprite ?? "",
            m?.Mouth.Enabled ?? false, m?.Mouth.Prefix ?? "", m?.Expression.Enabled ?? false, m?.Expression.Prefix ?? "",
            Outfit?.PackFaces.ToList() ?? new System.Collections.Generic.List<string>(),
            m?.SpriteOverrides.Where(o => o != null).Select(o => (o.Slot ?? "", o.Sprite ?? "")).ToList()
                ?? new System.Collections.Generic.List<(string, string)>());

        // Nothing of the bust chosen before stays on screen while this one loads.
        Put(new TextureSet(), publish: false);
        var task = Task.Run(() => Load(inputs));
        _loading = task;
        System.Threading.Interlocked.Increment(ref PendingLoads);
        task.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() =>
        {
            try { Finish(task); }
            finally { System.Threading.Interlocked.Decrement(ref PendingLoads); }
        })), TaskScheduler.Default);
    }

    /// <summary>Put in a finished load, unless a later one took its place.</summary>
    private void Finish(Task<TextureSet> task)
    {
        if (!ReferenceEquals(_loading, task)) return;
        _loading = null;
        Put(task.IsCompletedSuccessfully ? task.Result : new TextureSet());
    }

    private void Put(TextureSet set, bool publish = true)
    {
        _base = set.Base;
        _mask = set.Mask;
        _artW = set.ArtW; _artH = set.ArtH;
        _blink = set.Blink;
        for (int i = 1; i <= 4; i++) _mouth[i] = set.Mouth[i];
        _expressions.Clear();
        foreach (var face in set.Faces) _expressions[face.Key] = face.Value;
        _packReplacedMask = set.PackReplacedMask;
        _vanillaJiggle = set.VanillaJiggle;
        _renderGeneration++;
        // After every route through the load, including the ones that end with
        // everything cleared: the picker beside the preview is driven by what
        // got loaded, so a bust with nothing to load has to say so rather than
        // keep the last bust's faces on offer.
        if (publish) PublishExpressionChoices();
    }

    /// <summary>The set, on a worker: the pack's own art, or the game's with the pack's replacements over it.</summary>
    private static TextureSet Load(TextureInputs i)
    {
        var set = new TextureSet();
        if (i.VanillaBustKey != null) { LoadVanilla(i, set); return set; }
        string Pack(string relative) => Path.Combine(i.PackRoot, Normalize(relative));
        set.Base = LoadIfExists(Pack(i.BaseSprite));
        // The mask is spread over the ART, as the game reads it (BustComposer.LoadPngOnArt):
        // on art that is not square, not over the whole frame the art is fitted in.
        if (set.Base != null) SMSModForge.Validation.ArtDimensions.TryReadPngSize(Pack(i.BaseSprite), out set.ArtW, out set.ArtH);
        string? maskFile = Shared.GameArt.IsBorrowed(i.MaskSprite)
            ? Rendering.VanillaArtResolver.GameArtFile(i.MaskSprite, "Mask.PNG")
            : Pack(i.MaskSprite);
        set.Mask = maskFile != null && File.Exists(maskFile) ? BustComposer.LoadPngOnArt(maskFile, set.ArtW, set.ArtH) : null;
        set.Blink = Shared.GameArt.IsBorrowed(i.BlinkSprite)
            ? LoadBorrowed(i.BlinkSprite, "Blink.PNG")
            : LoadIfExists(Pack(i.BlinkSprite));
        for (int n = 1; n <= 4; n++)
            set.Mouth[n] = !i.MouthEnabled ? null
                : Shared.GameArt.IsBorrowed(i.MouthPrefix) ? LoadBorrowed(i.MouthPrefix, "Mouth" + n + ".PNG")
                : LoadIfExists(Pack(i.MouthPrefix) + n + ".PNG");
        // Every face the game will build this bust with - the four, and each
        // one the character declares - not the four alone, which left a face
        // the pack invented out of the list beside this picture while the game
        // showed it.
        if (i.ExpressionEnabled)
            foreach (var name in i.PackFaces)
                set.Faces[name] = (Shared.GameArt.IsBorrowed(i.ExpressionPrefix)
                                      ? LoadBorrowed(i.ExpressionPrefix, "Expression" + name + ".PNG")
                                      : LoadIfExists(Pack(i.ExpressionPrefix) + name + ".PNG"))
                                  ?? Empty();
        return set;
    }

    /// <summary>
    /// A part the outfit borrows from one of the game's busts
    /// (<see cref="Shared.GameArt"/>), from the copy of that bust's art shipped
    /// with the editor: the blink, mouths and faces at the game's full size, so
    /// they match a pack's own bust (1.7.0), the mask smaller. Fitted to the
    /// frame here like any other art, so it sits where the game's will.
    /// </summary>
    private static byte[]? LoadBorrowed(string field, string file)
    {
        string? path = Rendering.VanillaArtResolver.GameArtFile(field, file);
        return path == null ? null : LoadIfExists(path);
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
    private static void LoadVanilla(TextureInputs i, TextureSet set)
    {
        string? root = Rendering.VanillaArtResolver.FindArtRoot();
        string? dir = root == null ? null : Path.Combine(root, i.VanillaBustKey!);
        if (dir != null && Directory.Exists(dir))
        {
            LoadGameArt(dir, set);
            set.VanillaJiggle = LoadJiggleSettings(Path.Combine(dir, "Jiggle.txt"));
        }

        // Whatever the pack paints over it - AFTER the game's art, because it
        // replaces it, and OUTSIDE the check above, because a replacement is the
        // pack's own file and does not stop existing when the shipped art is
        // missing. An editor with no extraction beside it still has something
        // true to show for a texture the author supplied.
        LoadPackReplacements(i, set);
    }

    /// <summary>The bust as the game draws it.</summary>
    private static void LoadGameArt(string dir, TextureSet set)
    {
        set.Base  = LoadIfExists(Path.Combine(dir, "Base.PNG"));
        // Absent on art exported before the mask was added, in which case the
        // bust simply holds still — the shader reads a missing mask as zero
        // displacement, so nothing needs a version check.
        set.Mask  = LoadIfExists(Path.Combine(dir, "Mask.PNG"));
        set.Blink = LoadIfExists(Path.Combine(dir, "Blink.PNG"));
        for (int n = 1; n <= 4; n++)
            set.Mouth[n] = LoadIfExists(Path.Combine(dir, "Mouth" + n + ".PNG"));
        foreach (var name in ExpressionSpec.Names)
        {
            var px = LoadIfExists(Path.Combine(dir, "Expression" + name + ".PNG"));
            if (px != null) set.Faces[name] = px;
        }
    }

    /// <summary>
    /// Whether the pack's own mask actually landed on this bust.
    /// <para/>
    /// Set while loading rather than read off the manifest, because the runtime
    /// applies the pack's jiggle exactly when the mask override LANDS - a row
    /// ticked with no file, or naming a file that is not there, replaces
    /// nothing and leaves the game's motion alone. Deciding that from the
    /// manifest alone would be a second opinion about the same question.
    /// </summary>
    private bool _packReplacedMask;

    /// <summary>
    /// Paint the pack's replacement textures over the game's own, one slot at
    /// a time.
    /// <para/>
    /// The preview showed the game's bust and nothing else, so an author
    /// replacing a mouth or a face picked their art, looked at the picture
    /// beside it, and saw no difference — which reads as the replacement not
    /// working rather than as the preview not knowing about it. The runtime had
    /// been doing this since the feature landed; only the picture had not.
    /// <para/>
    /// Same rule as the runtime's (<c>VanillaBustOverrides</c>): a slot the
    /// pack does not mention is left exactly as the game drew it, and a slot
    /// ticked with no art chosen yet changes nothing — the game keeps its own,
    /// and the picture should say so rather than going blank.
    /// </summary>
    private static void LoadPackReplacements(TextureInputs i, TextureSet set)
    {
        if (string.IsNullOrEmpty(i.PackRoot)) return;
        foreach (var (slot, sprite) in i.Overrides)
        {
            if (string.IsNullOrWhiteSpace(sprite)) continue;

            var px = LoadIfExists(Path.Combine(i.PackRoot, Normalize(sprite)));
            if (px == null) continue;   // named but not there; the game's stands

            if (slot == SMSModForge.Shared.SpriteSlotNames.Base) { set.Base = px; continue; }
            if (slot == SMSModForge.Shared.SpriteSlotNames.Mask)
            {
                set.Mask = px;
                set.PackReplacedMask = true;
                continue;
            }
            if (slot == SMSModForge.Shared.SpriteSlotNames.Blink) { set.Blink = px; continue; }

            int frame = SMSModForge.Shared.SpriteSlotNames.MouthFrame(slot);
            if (frame >= 1 && frame <= 4) { set.Mouth[frame] = px; continue; }

            string? face = SMSModForge.Shared.SpriteSlotNames.ExpressionOf(slot);
            if (!string.IsNullOrEmpty(face)) set.Faces[face!] = px;
        }
    }

    /// <summary>
    /// The uniforms the next shader pass will use.
    /// <para/>
    /// The pack's, once the pack has replaced this bust's MASK - that texture
    /// is the jiggle's input, so replacing it is the point at which the motion
    /// becomes the pack's to describe, and the runtime draws the line in the
    /// same place. A borrowed bust whose mask the pack leaves alone keeps the
    /// game's own numbers, because that is what the game will do with it.
    /// <para/>
    /// Those numbers do not reproduce the game exactly: this shader is an
    /// approximation of the game's, so the same inputs through it land
    /// somewhere close rather than somewhere identical. Matching the inputs is
    /// as honest as this can be, and is better than matching neither.
    /// <para/>
    /// A property rather than a local so a test can read the same expression
    /// the renderer reads. Working it out a second time in the test would let
    /// the two drift apart and still agree.
    /// </summary>
    internal JiggleParams JiggleInEffect
    {
        get
        {
            var outfit = Outfit;
            if (outfit == null) return new JiggleParams();
            if (!_packReplacedMask && _vanillaJiggle != null) return _vanillaJiggle;
            return outfit.Model.Jiggle;
        }
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

    /// <summary>
    /// The textures the next frame will draw. For tests only.
    /// <para/>
    /// The very fields the render path reads, handed back as they are rather
    /// than looked up again: a test that re-derived which file each slot ought
    /// to hold would agree with a preview that loads nothing at all.
    /// </summary>
    internal (byte[]? Base, byte[]? Mask, byte[]? Blink, byte[]?[] Mouth,
              System.Collections.Generic.IReadOnlyDictionary<string, byte[]> Faces) Loaded()
    {
        // A load under way is finished first, by the same step that puts it in when it arrives.
        if (_loading is { } task) { task.Wait(); Finish(task); }
        return (_base, _mask, _blink, _mouth, _expressions);
    }

    /// <summary>
    /// The mask the next frame moves the bust by, on the frame's grid: the one
    /// the mask studio is painting while it is open (so unsaved strokes show at
    /// once), else the file's. A missing mask is no jiggle, not no picture. The
    /// studio paints on the art's own uv, as the game reads a mask, so on art that
    /// is not square its buffer is laid over the art's part of the frame
    /// (BustComposer.PlaceOnArt). The render path's own, and the tests'.
    /// </summary>
    internal byte[] MaskDrawn()
    {
        var live = Outfit?.LiveMaskBgra;
        if (live == null || live.Length != JiggleShader.Stride * JiggleShader.Size) return _mask ?? NoMask;
        if (_artW <= 0 || _artH <= 0 || _artW == _artH) return live;
        // A new buffer each time: the pass before may still be reading the last one.
        return BustComposer.PlaceOnArt(live, _artW, _artH, new byte[live.Length]);
    }

    private int _artW, _artH;

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

        // The quality, read live, so switching presets takes effect at once.
        var quality = Services.PreviewQualityManager.Current;
        if (!_pacer.Due(Rendering.DisplayRefresh.RateOf(this))) return;
        long nowMs = Environment.TickCount64;

        // The mask editor publishes its in-progress buffer through the VM —
        // prefer it when set so unsaved brush strokes appear immediately.
        // A missing mask means "no jiggle", not "don't draw" — the base should
        // appear the moment it's set, with each further sprite layering in as
        // it's filled rather than the whole preview waiting on the last one.
        byte[] mask = MaskDrawn();

        // Snapshot every input on the UI thread before handing off. Field
        // assignments here are local refs, so a subsequent ReloadTextures()
        // or outfit swap will spin up the next render against new buffers
        // without disturbing the in-flight pass.
        byte[] baseSnap = _base;
        byte[] maskSnap = mask;
        // EVERY bust jiggles by the pack's numbers, borrowed ones included.
        //
        // This used to prefer the uniforms the extractor read off the game's
        // own material, on the reasoning that a borrowed bust should move the
        // way the game moves it. That reasoning only holds if this shader IS
        // the game's, and it is not - it is an approximation of it. Feeding the
        // game's numbers into a different shader does not reproduce the game;
        // it produces a third thing, and one that silently disagrees with every
        // pack bust beside it for reasons an author cannot see.
        //
        // So the preview is consistent instead of falsely authentic: one set of
        // settings, the pack's, and a difference on screen always means a
        // difference somebody made. The extracted Jiggle.txt files still ship
        // beside the art - they are the game's real numbers and worth having if
        // these uniforms ever become something a pack can set - they are simply
        // not read here.
        var jiggle = JiggleInEffect;
        var tint = BustComposer.ParseTint(Outfit.Tint);
        float time = (float)(DateTime.Now - _startTime).TotalSeconds;
        // Empty is no face. "None" is still honoured because the picker used
        // to store that word, and a layout file from before this change can
        // hand it back on the first render after an update.
        // The fade-in (1.9.1): until the silhouette is in, the body, its blink
        // and its expression are off - blink then starts its cycle afresh, as the
        // game's BlinkingSprite does when switched on. The mouth stays.
        var (bodyShows, silhouette) = IntroPhase();
        byte[]? expression = (bodyShows && !string.IsNullOrEmpty(SelectedExpression) && SelectedExpression != "None"
                              && _expressions.TryGetValue(SelectedExpression, out var exp)) ? exp : null;
        if (!bodyShows) _blinkActive = false;
        byte[]? blink = bodyShows ? BlinkOverlayForThisFrame(nowMs / 1000.0) : null;
        byte[]? mouth = MouthOverlayForThisFrame(nowMs / 1000.0);
        byte[]? bodyAlpha = bodyShows && silhouette <= 0f
            ? null
            : _bodyAlpha ??= new byte[JiggleShader.RenderSize * JiggleShader.RenderSize];
        var output = _jiggleOutput;

        // Over a backdrop: the game's screen around the bust, the room behind,
        // breathing moving the bust over it (whole pixels: the Pixel Perfect
        // Camera), its fake GI - but not at Low, the cheapest that still shows
        // the bust as it is.
        var scene = SceneOverride ?? Services.PreviewBackdropManager.Current.Scene;
        OverBackdrop = scene != null;
        int bustBottom = 0;
        float shadowY = 0f;
        Rendering.GameLamp? lamp = null;
        Rendering.FakeGi? gi = null;
        Rendering.FakeGi.Art? giArt = null;
        if (scene != null)
        {
            double breath = Breathing ? Math.Sin(time * BreathingSpeed) * BreathingAmplitude : 0.0;
            int bob = (int)Math.Round(breath);
            bustBottom = scene.Backdrop.BustRestBottom + bob;
            // The shadow follows the bust's own place, not the whole pixel the
            // Pixel Perfect Camera draws it at: the caster's transform.
            shadowY = (float)(scene.Backdrop.BustRestBottom + breath) + Rendering.BackdropRender.FrameSize / 2f;
            lamp = scene.Lamp;
            if (lamp != null)
            {
                if (!ReferenceEquals(_casterOf, baseSnap)) { _caster = new Rendering.BustShadow.Caster(baseSnap, JiggleShader.Size, JiggleShader.Size, 100f); _casterOf = baseSnap; }
                _shadowTexture ??= new float[lamp.Shadow.W * lamp.Shadow.H];
                _shadowBackground ??= new float[scene.Backdrop.BackgroundWidth * scene.Backdrop.BackgroundHeight * 4];
            }
            gi = quality.EdgeGlow ? scene.Gi : null;
            if (gi != null)
            {
                if (!ReferenceEquals(_giArtOf, baseSnap) || _giArtReach != gi.Reach)
                {
                    _giArt = new Rendering.FakeGi.Art(baseSnap, JiggleShader.Size, JiggleShader.Size, gi.Reach);
                    _giArtOf = baseSnap; _giArtReach = gi.Reach;
                }
                giArt = _giArt;
            }
            _linear ??= new float[Rendering.BackdropRender.FrameSize * Rendering.BackdropRender.FrameSize * 4];
            _coverage ??= new float[Rendering.BackdropRender.FrameSize * Rendering.BackdropRender.FrameSize];
            if (silhouette > 0f) _linearSilhouette ??= new float[_linear.Length];
            if (!ReferenceEquals(_sceneOf, scene)) { _scene = Rendering.BackdropRender.Scene(scene); _sceneOf = scene; }
        }
        var linear = _linear;
        var linearSilhouette = silhouette > 0f ? _linearSilhouette : null;
        var coverage = _coverage;
        var sceneTarget = _scene;
        var caster = _caster;
        var shadowTexture = _shadowTexture;
        var shadowBackground = _shadowBackground;
        // The body's renderer while the intro hides it is at alpha 0, and its
        // shadow on itself with it; the cast shadow is there from the start.
        float rendererAlpha = bodyShows ? tint.a : 0f;
        // What the game's post-processing adds, by quality (PreviewQualityDef).
        var parts = new Rendering.BackdropRender.PostParts(quality.Bloom, quality.Flares, quality.Colour);
        bool exactGlow = quality.EdgeGlowExact;
        var sampling = quality.Sampling;

        // A light on the characters (1.10.0) multiplies the bust between its program and the blend, which the card's
        // whole-frame pass does not do: such a frame is drawn on the processor.
        bool useCard = Services.PreviewRendererManager.UseCard && scene?.Backdrop.IsPlace != true;
        // The breathing's whole pixels for a bust drawn alone (over a backdrop it is bustBottom's).
        int bobAlone = scene == null && Breathing ? (int)Math.Round(Math.Sin(time * BreathingSpeed) * BreathingAmplitude) : 0;
        var drops = WetDropsNow(scene, bustBottom, time);
        DropsDrawn = drops?.Count ?? 0;

        _uiScheduler ??= TaskScheduler.FromCurrentSynchronizationContext();
        _renderInFlight = true;
        ShaderPasses++;
        // Which outfit this pass belongs to. A shader pass outlives a fast
        // outfit switch, and without this its result would land on screen after
        // the switch — the old bust reappearing over the new one.
        int generation = _renderGeneration;

        Task.Run(() =>
        {
            if (scene != null)
            {
                // On the graphics card when that draws the previews: the whole
                // frame there after the shadow (Rendering.Gpu.GpuFrame), only the
                // preview's bytes coming back.
                var wholeOnCard = useCard ? (_gpuFrame ??= Rendering.Gpu.GpuFrame.TryCreate()) : null;
                if (wholeOnCard != null)
                {
                    if (lamp != null)
                    {
                        lamp.Shadow.Draw(caster!, scene.Left + Rendering.BackdropRender.FrameSize / 2f, shadowY, 2f, rendererAlpha, shadowTexture!);
                        wholeOnCard.Draw(lamp, shadowTexture!, scene, baseSnap, maskSnap, jiggle, tint, time, gi != null, bustBottom, bodyShows, silhouette,
                                         expression, mouth, blink, parts, JiggleShader.GameZoom, output, drops);
                    }
                    else
                        wholeOnCard.DrawNoLamp(scene, baseSnap, maskSnap, jiggle, tint, time, gi != null, bustBottom, bodyShows, silhouette,
                                               expression, mouth, blink, parts, JiggleShader.GameZoom, output, drops);
                    DrewOnCard = true;
                    return;
                }
                // The room first, through the bust's shadow from its lamp - and
                // the background the fake GI reads, copied from it.
                if (lamp != null)
                {
                    lamp.Shadow.Draw(caster!, scene.Left + Rendering.BackdropRender.FrameSize / 2f, shadowY, 2f, rendererAlpha, shadowTexture!);
                    lamp.Room(shadowTexture, sceneTarget!, shadowBackground!, scene.Backdrop.BackgroundWidth, scene.Backdrop.BackgroundHeight);
                    gi?.UseBackground(shadowBackground!);
                }
                // As the game blends: in linear light, the face over the body,
                // the silhouette over that, the lights, the room behind. The body
                // on the graphics card when that draws the previews (the game's
                // own program, its edge glow exact at every quality that has it).
                var card = useCard ? (_gpuBody ??= Rendering.Gpu.GpuBody.TryCreate()) : null;
                if (card != null)
                    card.Draw(baseSnap, maskSnap, jiggle, tint, time, scene.Gi, gi != null, scene.Left, scene.FrameBottom, bustBottom,
                              linear!, bodyShows, linearSilhouette, silhouette, scene.Decode, JiggleShader.GameZoom);
                else
                    Rendering.BackdropRender.Body(baseSnap, maskSnap, jiggle, tint, time, gi, giArt, scene.Left, scene.FrameBottom, bustBottom,
                                                  linear!, bodyShows, linearSilhouette, silhouette, exactGlow, scene.Decode, JiggleShader.GameZoom);
                DrewOnCard = card != null;
                // Into the camera's target as the game draws them, each blend kept
                // as the target keeps it: body, face, silhouette, then the lamps.
                if (lamp == null) Rendering.BackdropRender.ResetFrame(sceneTarget!, scene);
                Array.Clear(coverage!);
                Rendering.BackdropRender.Layer(sceneTarget!, scene, linear!, coverage!);
                if (expression != null) Rendering.BackdropRender.OverlayLayer(sceneTarget!, scene, expression, bustBottom, coverage!);
                if (mouth != null) Rendering.BackdropRender.OverlayLayer(sceneTarget!, scene, mouth, bustBottom, coverage!);
                if (blink != null) Rendering.BackdropRender.OverlayLayer(sceneTarget!, scene, blink, bustBottom, coverage!);
                if (linearSilhouette != null) Rendering.BackdropRender.Layer(sceneTarget!, scene, linearSilhouette, coverage!);
                if (lamp != null)
                {
                    // A wet outfit's drops (the Bust layer at 10, over the bust): before the lamp's glow, which the game
                    // adds over them as over the bust - a shower's over the window came out the glow brighter (S22).
                    if (drops != null) Rendering.WetDrops.BlendInto(sceneTarget!, drops, scene, coverage!);
                    lamp.Glow(sceneTarget!, shadowTexture, Rendering.BackdropRender.Covered(scene, coverage!));
                    Rendering.BackdropRender.OthersOver(sceneTarget!, scene);
                }
                else
                {
                    Rendering.BackdropRender.Lamps(sceneTarget!, scene, coverage!);
                    // A wet outfit's drops, over everything and before the post, as the game blends them.
                    if (drops != null) Rendering.WetDrops.BlendInto(sceneTarget!, drops);
                }
                // The game's post-processing: on the card too when it draws.
                var cardPost = useCard ? (_gpuPost ??= Rendering.Gpu.GpuPost.TryCreate()) : null;
                if (cardPost != null) cardPost.Present(sceneTarget!, scene, parts, output);
                else Rendering.BackdropRender.Present(sceneTarget!, scene, parts, output, _postWork);
                return;
            }

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
            // On the graphics card when that draws the previews (the game's program, its fake GI off).
            // Sampled as the quality says (Ultra the game's 2x2 a pixel, fewer below).
            var alone = useCard ? (_gpuAlone ??= Rendering.Gpu.GpuAlone.TryCreate()) : null;
            if (alone != null)
                alone.Draw(baseSnap, maskSnap, JiggleShader.Size, JiggleShader.Size, jiggle, tint, time, output,
                           JiggleShader.RenderSize, JiggleShader.RenderSize, sampling);
            else
                JiggleShader.Render(baseSnap, maskSnap, JiggleShader.Size, JiggleShader.Size, jiggle, tint, time, output,
                                    JiggleShader.RenderSize, JiggleShader.RenderSize, sampling);
            DrewOnCard = alone != null;

            // During the fade-in the silhouette takes the displaced body's shape,
            // so the body's alpha is kept before anything is drawn over it.
            if (bodyAlpha != null)
            {
                for (int p = 0, i = 3; p < bodyAlpha.Length; p++, i += 4) bodyAlpha[p] = output[i];
                if (!bodyShows) Array.Clear(output);
            }

            // In the game's sorting order: expressions 3, mouth 4, blink 5.
            CompositeOverlay(output, expression, maskSnap, jiggle, time);
            CompositeOverlay(output, mouth, maskSnap, jiggle, time);
            CompositeOverlay(output, blink, maskSnap, jiggle, time);

            // The silhouette is drawn above all of them (the body's order + 8 to 16).
            if (bodyAlpha != null) IntroFade.DrawSilhouette(output, bodyAlpha, silhouette);

            // A wet outfit's drops, over the bust (they ride its breathing, as the game's do).
            if (drops != null) Rendering.WetDrops.BlendAlone(output, JiggleShader.RenderSize, drops);

            // Breathing, inside the picture as over a backdrop: the whole bust a
            // whole pixel at a time (the game's Pixel Perfect Camera), up by bob.
            // It was a WPF transform sliding the image until 1.9.1 (the author:
            // "if I put None as the backdrop I get no breathing").
            BreatheRows(output, bobAlone);
        }).ContinueWith(t =>
        {
            try
            {
                // A frame that failed is not shown - and is said, for tests: a picture that silently stops coming
                // reads exactly like one that was never asked for.
                if (t.IsFaulted) LastFault = t.Exception?.GetBaseException();
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
                    FramesShown++;
                }
            }
            finally { _renderInFlight = false; }
        }, _uiScheduler);
    }

    /// <summary>The drops to draw in place of the outfit's own - for tests (S20 draws the game's).</summary>
    internal Rendering.WetDrops.Over? DropsOverride { get; set; }

    /// <summary>A test's own moment for the drops.</summary>
    internal Func<double>? WetClockForTests { get; set; }

    /// <summary>How many drops the last frame drew - for tests.</summary>
    internal int DropsDrawn { get; private set; }

    /// <summary>Where the last frame had the outfit's Wet object, on its canvas - for tests (S20 holds it to the game's).</summary>
    internal Rendering.WetDrops.Emitter? LastEmitter { get; private set; }

    /// <summary>
    /// A wet outfit's drops this frame (after 1.10.0: "busts can carry the same wet drops in dialogues, and the bust
    /// preview doesn't show them yet" - the author: "yes"): with its Wet particle on, the game's own drops round the
    /// middle of the bust (Rendering.WetDrops.OnBust, measured in the game: InGameSuiteTests.S20), on the screen's
    /// canvas over a backdrop - the bust's square where it stands, its breathing in - or on the bust's own square
    /// alone; as after five seconds of them, the same moment every time under tests.
    /// </summary>
    internal Rendering.WetDrops.Over? WetDropsNow(Rendering.BackdropScene? scene, int bustBottom, float time)
    {
        if (DropsOverride != null) return DropsOverride;
        if (Outfit?.ParticleActive != true) return null;
        double t = WetClockForTests?.Invoke() ?? (Services.TestMode.Active ? Rendering.WetDrops.TestTime : Rendering.WetDrops.Steady + time);
        uint seed = 2166136261;
        foreach (char ch in Outfit.Key ?? "") seed = (seed ^ ch) * 16777619;
        const int N = Rendering.BackdropRender.FrameSize;
        var kind = Outfit.ParticleKindIndex == 1 ? Rendering.WetDrops.Kind.Shower : Rendering.WetDrops.Kind.Towel;
        // The game's camera sees 10.8 units top to bottom, whatever the screen's size.
        if (scene != null)
        {
            var b = scene.Backdrop;
            double ppu = b.ScreenHeight / 10.8;
            var e = Rendering.WetDrops.OnBust(scene.Left, bustBottom, N, b.ScreenHeight, ppu, seed, kind);
            LastEmitter = e;
            return Rendering.WetDrops.OnBustAt(t, e, b.ScreenWidth * Rendering.WetDrops.PixelsPerUnit / ppu, b.ScreenHeight * Rendering.WetDrops.PixelsPerUnit / ppu);
        }
        // Alone, the bust's square is the x1 picture: 50 pixels a unit, as at the game's 960x540.
        const double alonePpu = 50;
        var alone = Rendering.WetDrops.OnBust(0, 0, N, N, alonePpu, seed, kind);
        LastEmitter = alone;
        double canvas = N * Rendering.WetDrops.PixelsPerUnit / alonePpu;
        return Rendering.WetDrops.OnBustAt(t, alone, canvas, canvas);
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

    /// <summary>How many frame callbacks this preview has had - for tests, to
    /// prove one that cannot be seen is not drawing (1.9.0, the mask studio).</summary>
    internal int FrameCallbacks { get; private set; }

    /// <summary>How many shader passes this preview has begun - for tests, to
    /// measure the frame rate a quality preset really gives.</summary>
    internal int ShaderPasses { get; private set; }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        FrameCallbacks++;
        if (!IsVisible) return;
        Render();
    }

    /// <summary>
    /// The bust drawn alone moved up by <paramref name="bob"/> whole pixels
    /// (down when negative) inside its own picture, as the game's camera moves
    /// it: rows that leave the picture are gone, rows that open are clear.
    /// </summary>
    internal static void BreatheRows(byte[] output, int bob)
    {
        const int N = JiggleShader.RenderSize, stride = JiggleShader.RenderStride;
        if (bob == 0) return;
        bob = Math.Clamp(bob, -N, N);
        if (bob > 0)
        {
            Buffer.BlockCopy(output, bob * stride, output, 0, (N - bob) * stride);
            Array.Clear(output, (N - bob) * stride, bob * stride);
        }
        else
        {
            int down = -bob;
            Buffer.BlockCopy(output, 0, output, down * stride, (N - down) * stride);
            Array.Clear(output, 0, down * stride);
        }
    }
}
