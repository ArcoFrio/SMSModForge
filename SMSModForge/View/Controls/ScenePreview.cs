using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SMSModForge.View.Controls;

/// <summary>
/// Compositing preview for the Scenes tab. Stacks the scene art (mapped to
/// <c>Core/Art</c> at runtime) behind the frame (mapped to <c>Core</c>), in the
/// same z-order the game uses. The frame comes from either the shipped
/// <c>VanillaFrames/</c> folder or a custom pack-relative path.
/// <para/>
/// The art can be any of three things, and each is shown a different way:
/// <list type="bullet">
///   <item>A <b>still</b> — a picture, drawn as it is.</item>
///   <item>A <b>GIF</b> — composed frame by frame and played, because an
///   animation whose preview never moves cannot be judged without exporting the
///   pack and starting the game.</item>
///   <item>A <b>video</b> — handed to a media player and held on its first
///   frame, since a video costs a decode pipeline rather than a blit. When
///   Windows has no decoder for it, one frame is lifted out of the file
///   instead; see <see cref="Model.VideoStill"/>.</item>
/// </list>
/// <para/>
/// Follows the same code-only control pattern as <see cref="BustPreview"/>.
/// </summary>
public sealed class ScenePreview : Grid
{
    // ── Dependency properties ──────────────────────────────────────────

    public static readonly DependencyProperty PackRootProperty =
        DependencyProperty.Register(nameof(PackRoot), typeof(string), typeof(ScenePreview),
            new PropertyMetadata(null, OnInputChanged));

    /// <summary>Absolute path to the pack folder on disk, or null if unsaved.</summary>
    public string? PackRoot
    {
        get => (string?)GetValue(PackRootProperty);
        set => SetValue(PackRootProperty, value);
    }

    public static readonly DependencyProperty SceneSpriteProperty =
        DependencyProperty.Register(nameof(SceneSprite), typeof(string), typeof(ScenePreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Pack-relative path to the scene image PNG.</summary>
    public string SceneSprite
    {
        get => (string)GetValue(SceneSpriteProperty);
        set => SetValue(SceneSpriteProperty, value);
    }

    public static readonly DependencyProperty VanillaFrameProperty =
        DependencyProperty.Register(nameof(VanillaFrame), typeof(string), typeof(ScenePreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Filename of a vanilla frame in the shipped <c>VanillaFrames/</c> folder.</summary>
    public string VanillaFrame
    {
        get => (string)GetValue(VanillaFrameProperty);
        set => SetValue(VanillaFrameProperty, value);
    }

    public static readonly DependencyProperty CustomFrameSpriteProperty =
        DependencyProperty.Register(nameof(CustomFrameSprite), typeof(string), typeof(ScenePreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Pack-relative path to a custom frame PNG. Takes precedence over <see cref="VanillaFrame"/>.</summary>
    public string CustomFrameSprite
    {
        get => (string)GetValue(CustomFrameSpriteProperty);
        set => SetValue(CustomFrameSpriteProperty, value);
    }

    // ── Visual children ────────────────────────────────────────────────

    // Both layers stay at native size (Stretch.None) and get the SAME zoom, so
    // scene + frame scale about the same centre and stay registered — the
    // compositing the game does. (Fitting each to the box independently would
    // scale them differently and misalign, since the scene and frame PNGs
    // aren't the same dimensions.)
    //
    // The art layer then divides that zoom by how far off 256x256 the file is,
    // because the runtime fits it to that square. The frame does not, and must
    // not: its size is the design.
    /// <summary>How much bigger than life both layers are drawn. One number,
    /// applied to both, so they stay registered with each other.</summary>
    private const double PreviewZoom = 1.5;

    private readonly Image _sceneImage = new()
    {
        Stretch = Stretch.None,
        LayoutTransform = new ScaleTransform(PreviewZoom, PreviewZoom),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true,
    };
    private readonly Image _frameImage = new()
    {
        Stretch = Stretch.None,
        LayoutTransform = new ScaleTransform(PreviewZoom, PreviewZoom),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true,
    };
    /// <summary>
    /// The video layer, for a scene whose art moves.
    /// <para/>
    /// Held paused on its first frame rather than played: a preview is for
    /// judging what the scene looks like, and a loop running behind every
    /// selection costs a decode for as long as the tab is open. A play button
    /// starts it when somebody actually wants to watch.
    /// <para/>
    /// Sized like the art layer, so a video occupies the same space a still of
    /// the same scene would - the runtime fits it to the 256 square, and the
    /// preview has to agree or it is showing something the game will not draw.
    /// </summary>
    private readonly MediaElement _sceneVideo = new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Manual,
        ScrubbingEnabled = true,          // shows the frame it is paused on
        Volume = 0,                       // a preview is not an audition
        Stretch = Stretch.Uniform,
        Width = ArtFitPixels * PreviewZoom,
        Height = ArtFitPixels * PreviewZoom,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>The square a scene occupies, shared with the runtime's fitting
    /// rule so the two cannot disagree.</summary>
    private static double ArtFitPixels => Rendering.ArtFit.ScenePixels;

    // ── The GIF the art layer is currently playing ─────────────────────
    //
    // A GIF animates here, where a video is held on its first frame, and the
    // difference is in what each one costs. A video is a decode pipeline: it
    // opens a file, runs a codec Windows may not even have, and carries audio.
    // A GIF is a blit — one composite of the art's own size, at the rate the
    // file itself asks for, using the decoder that is already in the box.
    //
    // It is also the thing the author is actually judging. An animation that
    // only ever shows its first frame in the editor cannot be checked without
    // exporting the pack and starting the game.

    /// <summary>The open GIF, or null when the scene is not one.</summary>
    private Model.GifFrames.Reader? _gif;

    /// <summary>Advances <see cref="_gif"/>. Stopped whenever the preview is
    /// off screen, so nothing decodes behind a tab nobody is looking at.</summary>
    private System.Windows.Threading.DispatcherTimer? _gifTimer;

    /// <summary>Which frame is showing.</summary>
    private int _gifAt;

    /// <summary>
    /// A caption under art that is showing, saying something about it.
    /// <para/>
    /// Separate from <see cref="_placeholder"/>, which replaces the picture.
    /// This one sits below a picture that IS there — a video's first frame,
    /// where the format is one Windows cannot play — so it has to stay out of
    /// the way of the thing the author is looking at.
    /// </summary>
    private readonly TextBlock _note = new()
    {
        Foreground = Brushes.WhiteSmoke,
        Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x11, 0x11, 0x11)),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Bottom,
        FontSize = 10,
        Padding = new Thickness(6, 3, 6, 4),
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };

    private readonly TextBlock _placeholder = new()
    {
        Foreground = Brushes.Gray,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 11,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4),
    };

    /// <summary>Fixed display size. Pinned the same way as <see cref="JigglePreview.FixedSize"/>.</summary>
    public const double FixedSize = 480;

    public ScenePreview()
    {
        // A tooltip that outlived its owner sits over the one thing an
        // author is trying to look at. See ToolTipDismisser.
        View.ToolTipDismisser.KeepClearOf(this);

        Width = MinWidth = MaxWidth = FixedSize;
        Height = MinHeight = MaxHeight = FixedSize;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        RenderOptions.SetBitmapScalingMode(_sceneImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_frameImage, BitmapScalingMode.NearestNeighbor);
        // Scene image behind, frame in front — same z-order as the game.
        Children.Add(_sceneImage);
        Children.Add(_sceneVideo);
        Children.Add(_frameImage);
        Children.Add(_placeholder);
        Children.Add(_note);

        // Windows decodes what it decodes. The engine ships its own decoders,
        // so a format the game plays quite happily - VP8 in a .webm, for one -
        // can be one Windows has never heard of.
        //
        // A still is most of what a preview is for, so before giving up, one is
        // lifted out of the file directly. See VideoStill.
        _sceneVideo.MediaFailed += (_, _) => FallBackToStill();

        // Paused on the first frame as soon as it opens. Holding one frame is
        // the whole cost of a video scene being selected; a loop running
        // behind every selection would decode for as long as the tab is open.
        _sceneVideo.MediaOpened += (_, _) =>
        {
            _sceneVideo.Pause();
            _sceneVideo.Position = System.TimeSpan.Zero;
        };

        // An animation only runs while somebody can see it. Switching tabs,
        // collapsing the pane or closing the window all come through here, and
        // each one should stop the clock rather than leave a timer composing
        // frames for a control that is off screen.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) ResumeGif();
            else _gifTimer?.Stop();
        };
        Unloaded += (_, _) => _gifTimer?.Stop();

        Refresh();
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ScenePreview)d).Refresh();

    private void Refresh()
    {
        string root = PackRoot ?? "";
        string sprite = SceneSprite ?? "";

        // Nothing useful to show when no sprite path is authored.
        if (string.IsNullOrWhiteSpace(sprite))
        {
            ShowPlaceholder(string.IsNullOrEmpty(root)
                ? "Save the pack and set a scene sprite path."
                : "No scene sprite path set.");
            return;
        }

        if (string.IsNullOrEmpty(root))
        {
            ShowPlaceholder("Save the pack first so paths can be resolved.");
            return;
        }

        // Resolve scene image.
        string scenePath = Path.Combine(root, sprite);
        if (!File.Exists(scenePath))
        {
            ShowPlaceholder($"Scene sprite not found:\n{scenePath}");
            return;
        }

        // A video is shown by the media layer rather than as a picture: WPF
        // cannot hand back a frame as a BitmapSource, but it can display one.
        // An empty dark box inside a frame would read as a broken path.
        if (SMSModForge.Model.MediaProbe.KindOf(sprite)
            == SMSModForge.Model.MediaProbe.MediaKind.Video)
        {
            StopGif();
            ShowVideo(scenePath);
            ShowFrame(root);
            _placeholder.Visibility = Visibility.Collapsed;
            return;
        }
        StopVideo();

        // A GIF plays on the art layer, composed frame by frame. If the file
        // turns out not to be one the decoder can read, it falls through to the
        // still path, which shows whatever the imaging stack makes of it — a
        // picture beats an error for something that is, after all, a picture.
        BitmapSource? art = null;
        if (SMSModForge.Model.MediaProbe.KindOf(sprite)
            == SMSModForge.Model.MediaProbe.MediaKind.Gif)
            art = StartGif(scenePath);

        if (art == null)
        {
            StopGif();
            art = TryLoad(scenePath);
        }

        _sceneImage.Source = art;
        _sceneImage.Visibility = Visibility.Visible;

        _sceneImage.LayoutTransform = ArtScale(art);

        ShowFrame(root);
        _placeholder.Visibility = Visibility.Collapsed;
        _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// How much to scale the art layer so it is DRAWN at the size the runtime
    /// will draw it.
    /// <para/>
    /// Two corrections, and they are separate things:
    /// <list type="bullet">
    ///   <item>Art of another size occupies the space a 256x256 scene would,
    ///   because that is what the runtime does with it. Without this a 512x512
    ///   scene previewed at twice the size of its neighbours and then played at
    ///   the same size as them, which makes the preview worse than none.</item>
    ///   <item><b>DPI.</b> Stretch.None lays a picture out at its DPI-CORRECTED
    ///   size rather than its pixel size, so a file claiming 72 dpi is laid out
    ///   a third larger than it is. The runtime knows nothing about dpi - it
    ///   fits pixels to the scene square - so the correction is undone here.
    ///   A video still is the case that exposed it: the image codec reports 72
    ///   dpi for a frame that has no such thing, and a 960x540 scene drew at
    ///   512px inside a 480px box, spilling out of its own frame.</item>
    /// </list>
    /// </summary>
    private static ScaleTransform ArtScale(BitmapSource? art)
    {
        if (art == null) return new ScaleTransform(PreviewZoom, PreviewZoom);

        double fit = Rendering.ArtFit.SceneScale(art.PixelWidth, art.PixelHeight);

        // Guarded because a bitmap with no dpi of its own reports zero, and
        // dividing the preview by that loses the picture entirely.
        double dpiX = art.DpiX > 0 ? art.DpiX : 96.0;
        double dpiY = art.DpiY > 0 ? art.DpiY : 96.0;

        return new ScaleTransform(PreviewZoom / fit * (dpiX / 96.0),
                                  PreviewZoom / fit * (dpiY / 96.0));
    }

    /// <summary>The frame around the art — custom wins over vanilla, matching
    /// the runtime. Shared by the still and video paths, because the frame is
    /// the same either way.</summary>
    private void ShowFrame(string packRoot)
    {
        string? framePath = ResolveFramePath(packRoot);
        if (framePath != null && File.Exists(framePath))
        {
            _frameImage.Source = TryLoad(framePath);
            _frameImage.Visibility = Visibility.Visible;
        }
        else
        {
            _frameImage.Source = null;
            _frameImage.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Point the media layer at a video and hold it on its first frame.
    /// <para/>
    /// Nothing decodes until this runs, and what decodes is one frame - the
    /// player is paused as soon as it opens. That is the whole of the cost
    /// while a scene is merely selected.
    /// </summary>
    private void ShowVideo(string absolutePath)
    {
        _sceneImage.Source = null;
        _sceneImage.Visibility = Visibility.Collapsed;
        _note.Visibility = Visibility.Collapsed;

        _sceneVideo.Visibility = Visibility.Visible;
        _sceneVideo.Source = new System.Uri(absolutePath);
        _sceneVideo.Play();       // opens it; MediaOpened pauses on frame one
    }

    /// <summary>
    /// The media player could not open this video. Show a frame taken out of
    /// the file instead, and say that is what it is.
    /// <para/>
    /// Only some formats can be reached this way — see
    /// <see cref="Model.VideoStill"/> — so the old message is still here for
    /// the rest, now phrased as what it is: the preview's limitation, not the
    /// pack's.
    /// </summary>
    private void FallBackToStill()
    {
        string root = PackRoot ?? "";
        string sprite = SceneSprite ?? "";
        string name = Path.GetFileName(sprite);

        var still = string.IsNullOrEmpty(root)
            ? null
            : Model.VideoStill.FirstFrame(Path.Combine(root, sprite));

        if (still == null)
        {
            ShowPlaceholder("Video scene\n" + name
                            + "\n\nWindows has no decoder for this format, so it cannot be "
                            + "previewed here. It still plays in game - the game brings its "
                            + "own.");
            return;
        }

        // Hidden rather than stopped: clearing the source from inside the
        // player's own failure handler is asking it to re-enter that handler.
        _sceneVideo.Visibility = Visibility.Collapsed;

        _sceneImage.Source = still;
        _sceneImage.Visibility = Visibility.Visible;

        _sceneImage.LayoutTransform = ArtScale(still);

        ShowFrame(root);
        _placeholder.Visibility = Visibility.Collapsed;

        _note.Text = "First frame only - Windows cannot play this format. It animates in game.";
        _note.Visibility = Visibility.Visible;
    }

    /// <summary>Let go of whatever the media layer was holding.
    /// <para/>
    /// Called whenever the preview shows something that is not a video, so a
    /// file is not kept open behind a scene nobody is looking at any more.</summary>
    private void StopVideo()
    {
        if (_sceneVideo.Visibility == Visibility.Collapsed && _sceneVideo.Source == null) return;

        _sceneVideo.Stop();
        _sceneVideo.Source = null;
        _sceneVideo.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Open a GIF and start it, returning its first composed frame — or null
    /// when the file is not a GIF the decoder can read.
    /// <para/>
    /// A single-frame GIF is opened and shown but never gets a timer: there is
    /// nothing to advance to, and a timer ticking against a still picture is
    /// pure cost.
    /// </summary>
    private BitmapSource? StartGif(string absolutePath)
    {
        StopGif();

        var reader = Model.GifFrames.Reader.Open(absolutePath);
        if (reader == null || reader.Count == 0) return null;

        _gif = reader;
        _gifAt = 0;
        var first = reader.Compose(0);

        if (reader.Count > 1) ResumeGif();
        return first;
    }

    /// <summary>
    /// Start or restart the frame timer for whatever GIF is open.
    /// <para/>
    /// The interval is set per frame rather than once, because a GIF's frames
    /// carry their own delays and half the GIFs in the world use that — a title
    /// card held for a second in front of a fast loop. A fixed interval plays
    /// those wrong, in a way that looks like a fault in the art.
    /// </summary>
    private void ResumeGif()
    {
        if (_gif == null || _gif.Count < 2) return;

        if (_gifTimer == null)
        {
            _gifTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background);
            _gifTimer.Tick += (_, _) => Advance();
        }

        _gifTimer.Interval = Delay(_gifAt);
        _gifTimer.Start();
    }

    /// <summary>Show the next frame, wrapping at the end.</summary>
    private void Advance()
    {
        var gif = _gif;
        if (gif == null || gif.Count < 2) { _gifTimer?.Stop(); return; }

        _gifAt = (_gifAt + 1) % gif.Count;
        _sceneImage.Source = gif.Compose(_gifAt);

        // Held for as long as THIS frame asks for, then the next one is asked.
        if (_gifTimer != null) _gifTimer.Interval = Delay(_gifAt);
    }

    /// <summary>How long one frame is held, floored so a malformed delay of
    /// zero cannot turn into a timer that never rests.</summary>
    private TimeSpan Delay(int frame)
    {
        double seconds = _gif == null || frame < 0 || frame >= _gif.Count
            ? Model.GifFrames.DefaultDelaySeconds
            : _gif.Delays[frame];

        if (seconds < 0.02) seconds = Model.GifFrames.DefaultDelaySeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Let go of whatever GIF was playing.</summary>
    private void StopGif()
    {
        _gifTimer?.Stop();
        _gif = null;
        _gifAt = 0;
    }

    private void ShowPlaceholder(string message)
    {
        StopGif();

        // Hidden rather than torn down: this is also what the media layer's
        // own failure handler calls, and clearing the Source from inside that
        // handler is asking the player to re-enter it.
        _sceneVideo.Visibility = Visibility.Collapsed;

        _sceneImage.Source = null;
        _frameImage.Source = null;
        _sceneImage.Visibility = Visibility.Collapsed;
        _frameImage.Visibility = Visibility.Collapsed;
        _placeholder.Text = message;
        _placeholder.Visibility = Visibility.Visible;
        _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Determine the absolute frame path using the same precedence as the
    /// runtime: <see cref="CustomFrameSprite"/> (pack-relative) wins; if
    /// absent, <see cref="VanillaFrame"/> resolves against the shipped
    /// <c>VanillaFrames/</c> folder next to the editor exe.
    /// </summary>
    private string? ResolveFramePath(string packRoot)
    {
        string custom = CustomFrameSprite ?? "";
        if (!string.IsNullOrWhiteSpace(custom))
            return Path.Combine(packRoot, custom);

        string vanilla = VanillaFrame ?? "";
        if (!string.IsNullOrWhiteSpace(vanilla))
        {
            string? frameDir = FindVanillaFramesFolder();
            if (frameDir != null)
                return Path.Combine(frameDir, vanilla);
        }

        return null;
    }

    /// <summary>
    /// Locate the <c>VanillaFrames</c> folder that ships next to the
    /// editor exe. Mirrors the runtime's
    /// <see cref="SMSModForge.PackPlugin.SceneFactory.ResolvePluginFrameRoot"/>
    /// logic — except the editor's exe sits in a different place from
    /// the BepInEx plugin DLL, so we resolve from our own assembly.
    /// </summary>
    private static string? FindVanillaFramesFolder()
    {
        try
        {
            string? exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(exeDir)) return null;
            string candidate = Path.Combine(exeDir, "VanillaFrames");
            return Directory.Exists(candidate) ? candidate : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Load a PNG into a frozen <see cref="BitmapImage"/> without holding
    /// the file open. Returns null on any I/O failure.
    /// </summary>
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
