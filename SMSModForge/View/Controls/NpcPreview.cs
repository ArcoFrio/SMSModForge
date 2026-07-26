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
    // Fixed preview box + how tall the pose is drawn inside it. Everything else
    // (shadow offset, circle size) is derived from PreviewPixelsPerUnit so the
    // shadow lands where it does in-game relative to the pose.
    private const double BoxSize = 600;
    // Match the shader's native render height (JiggleShader.RenderSize = 512) so
    // the common square poses display 1:1 — no resampling, exactly as crisp as
    // the bust preview. Non-square poses fit within the box at their aspect.
    private const double PoseTargetHeight = JiggleShader.RenderSize;

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
    private readonly TextBlock _placeholder = new()
    {
        Foreground = Brushes.Gray, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        Text = "Set a pose sprite to preview.",
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
    private double _poseAspect = 1;        // realWidth / realHeight
    private double _ppu = 20;              // preview px per world unit (set on load)

    private bool _renderInFlight, _renderingHooked;
    private TaskScheduler? _uiScheduler;
    private long _lastShaderTickMs;
    private readonly DateTime _start = DateTime.Now;

    // Blink schedule — mirrors the game component: open a random 2-5s, shut 0.2s.
    private bool _blinkClosed;
    private double _blinkNextSec;
    private readonly Random _rng = new();

    public NpcPreview()
    {
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
        Children.Add(_canvas);
        Children.Add(_placeholder);

        // Gate the render loop on real visibility, not just Loaded — a tab that
        // isn't selected keeps its content loaded-but-hidden, and we don't want
        // the jiggle shader burning CPU on a preview nobody can see.
        IsVisibleChanged += (_, _) => { if (IsVisible) Hook(); else Unhook(); };
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
        if (e.PropertyName is null || _pathProps.Contains(e.PropertyName)) Reload();
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
        _poseAspect = nativeH > 0 ? nativeW / (double)nativeH : 1;

        string maskAbs = Path.Combine(PackRoot, Norm(Npc.Mask));
        _mask = BustComposer.LoadPngAt(maskAbs, _srcW, _srcH);   // transparent if absent

        // Fit the pose within the box preserving aspect. px/unit derives from the
        // displayed pose height so shadow offsets land at the same relative spot.
        double poseH = BoxSize;
        double poseW = poseH * _poseAspect;
        if (poseW > BoxSize) { poseW = BoxSize; poseH = poseW / _poseAspect; }
        _ppu = poseH / (nativeH / 100.0);

        // Output = displayed size (bounded by the box), not the source size —
        // this is the optimisation. Blink loads at the OUTPUT grid so it
        // composites 1:1 onto the render.
        _outW = System.Math.Max(1, (int)System.Math.Round(poseW));
        _outH = System.Math.Max(1, (int)System.Math.Round(poseH));

        string blinkAbs = Path.Combine(PackRoot, Norm(Npc.BlinkSprite));
        _blinkTex = !string.IsNullOrWhiteSpace(Npc.BlinkSprite) && File.Exists(blinkAbs)
                    ? BustComposer.LoadPngAt(blinkAbs, _outW, _outH) : null;

        _bodyOutput = new byte[_outW * _outH * 4];
        _bodyBitmap = new WriteableBitmap(_outW, _outH, 96, 96, PixelFormats.Pbgra32, null);
        _body.Source = _bodyBitmap;

        double cx = BoxSize / 2, cy = BoxSize / 2;
        _body.Width = poseW; _body.Height = poseH;
        Canvas.SetLeft(_body, cx - poseW / 2);
        Canvas.SetTop(_body, cy - poseH / 2);

        ShowPlaceholder(false);
        Relayout();
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

    private void OnRender(object? sender, EventArgs e) => RenderBody();

    private void RenderBody()
    {
        if (!IsVisible || _renderInFlight || Npc == null || _base == null || _mask == null
            || _bodyOutput == null || _bodyBitmap == null) return;

        // Preview Quality governs the shader exactly as it does on the Busts
        // tab: MaxFps caps how often the pass runs, SuperSample toggles 4×
        // anti-aliasing. Read live so switching presets takes effect next tick.
        var quality = Services.PreviewQualityManager.Current;
        long nowMs = Environment.TickCount64;
        if (nowMs - _lastShaderTickMs < 1000L / quality.MaxFps) return;
        _lastShaderTickMs = nowMs;

        // Advance the blink schedule and decide whether the eyes are shut this
        // frame — composited INTO the shader output (below), so it rides the
        // jiggle and gets proper premultiplied alpha.
        byte[]? blink = BlinkOverlayForThisFrame(nowMs / 1000.0);

        byte[] baseSnap = _base, maskSnap = _mask, output = _bodyOutput;
        var bmp = _bodyBitmap;
        int sw = _srcW, sh = _srcH, ow = _outW, oh = _outH;
        var jiggle = Npc.Model.Jiggle;
        var tint = BustComposer.ParseTint(Npc.JiggleTint);
        float time = (float)(DateTime.Now - _start).TotalSeconds;
        bool superSample = quality.SuperSample;

        _uiScheduler ??= TaskScheduler.FromCurrentSynchronizationContext();
        _renderInFlight = true;
        Task.Run(() =>
            {
                // Sample the high-res source (sw×sh); render only the displayed
                // pixels (ow×oh). Supersampling area-averages the downsample.
                JiggleShader.Render(baseSnap, maskSnap, sw, sh, jiggle, tint, time, output, ow, oh, superSample);
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
                    }
                }
                finally { _renderInFlight = false; }
            }, _uiScheduler);
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
