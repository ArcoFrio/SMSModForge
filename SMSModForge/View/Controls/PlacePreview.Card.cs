using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;

namespace SMSModForge.View.Controls;

/// <summary>
/// The place's scene described for the graphics card (1.10.0, the author: "The
/// Places preview is very laggy with all checkboxes enabled on ultra, even
/// though it's barely using my PC's resources at all. When I stop moving my
/// mouse around and start again, there's also a stutter going on").
/// <para/>
/// The look over a place used to start from pictures of the world layer taken
/// by WPF's software renderer on the interface's thread (ten milliseconds and
/// more a frame, the whole editor waiting), and a still place was found still
/// by taking them again five times a second. Here the world layer is read
/// instead of photographed: every element it holds, in its order, as a
/// <see cref="PlaceItem"/> - a few microseconds - and
/// <list type="bullet">
/// <item>the card draws the scene from that list itself
/// (<see cref="GpuPlaceScene"/>), the interface's thread only putting the
/// finished picture on screen;</item>
/// <item>two lists the same are the same picture: a still place costs nothing,
/// and the frame after the mouse moves is drawn at once (no five-a-second
/// look, which showed the first movement up to a fifth of a second late);</item>
/// <item>drawing on the processor, the same list spares the captures while the
/// place is still.</item>
/// </list>
/// WPF stays the scene's owner: what is clicked, what is shown with the look
/// off, where everything is. Nothing is described twice.
/// </summary>
public sealed partial class PlacePreview
{
    private GpuPlaceScene? _cardScene;
    /// <summary>The card's way failed once (its device lost, a program that would not compile): the processor's
    /// from then on, for this preview.</summary>
    private bool _cardBroken;
    /// <summary>The look shown is the card's own drawing of the scene: the shaders' pictures stay on the card, and
    /// their own clock rests.</summary>
    private bool _cardLookCovers;
    private PlaceFrame _frameNext = new(), _frameLast = new();
    private bool _frameLastSet;
    /// <summary>The next look is worked out whether or not the scene's list changed (a tick box, the view, the lights).</summary>
    private bool _lookForce;

    /// <summary>Whether the look shown was drawn from the scene by the card itself - for tests.</summary>
    internal bool CardDrawsScene => _cardLookCovers;

    private readonly object _cardGate = new();

    /// <summary>This preview's scene on the card and its look there, made on first use - by whichever thread asks
    /// first (the look's, the character in the place's), once.</summary>
    private GpuPlaceScene? CardScene() { lock (_cardGate) return _cardScene ??= GpuPlaceScene.TryCreate(); }
    private GpuPost? CardPost() { lock (_cardGate) return _lookCard ??= GpuPost.TryCreate(); }

    /// <summary>The look is no longer the card's own drawing of the scene: the shaders' pictures, which stayed on
    /// the card meanwhile, are drawn into their bitmaps at once, and their own clock takes them from there.</summary>
    private void CardLookOver()
    {
        if (!_cardLookCovers) return;
        _cardLookCovers = false;
        StepShaders();
    }

    /// <summary>The next look is worked out anew: something outside the scene's list changed.</summary>
    private void LookAgain()
    {
        _lookSame = 0;
        _lookForce = true;
    }

    /// <summary>
    /// What the world layer draws now, into <paramref name="f"/>: every visible element in its order. False when
    /// something in it cannot be put that way (nothing of today's; a guard for what is added later) - the look is
    /// then worked out from captures, as before.
    /// </summary>
    private bool DescribeScene(PlaceFrame f, bool forCard)
    {
        bool shaders = false;
        float t = (float)_shaderClock.Elapsed.TotalSeconds;
        foreach (UIElement child in _worldLayer.Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            switch (child)
            {
                case Image img:
                    if (img.Source == null) continue;
                    if (!DescribeImage(f, img, forCard, t, ref shaders)) return false;
                    break;
                case Rectangle rect when rect.Fill is SolidColorBrush fill && rect.OpacityMask is ImageBrush { ImageSource: BitmapSource mask } && mask.IsFrozen:
                {
                    // A tint: the colour through the picture's own alpha.
                    ref var item = ref f.Add();
                    item.Bitmap = mask;
                    item.Kind = PlaceItemKind.Tint;
                    item.Sampling = PlaceSampling.Linear;
                    Colour(ref item, fill);
                    item.Opacity = (float)rect.Opacity;
                    Put(ref item, BoxOf(rect));
                    break;
                }
                case Ellipse disc when disc.Fill is SolidColorBrush fill && disc.Stroke == null:
                {
                    // An NPC's shadow: the game's circle sprite, tinted.
                    ref var item = ref f.Add();
                    item.BuiltIn = PlaceBuiltIn.Disc;
                    item.Kind = PlaceItemKind.Tint;
                    item.Sampling = PlaceSampling.Smooth;
                    Colour(ref item, fill);
                    item.Opacity = (float)disc.Opacity;
                    Put(ref item, BoxOf(disc));
                    break;
                }
                case Canvas host:
                    if (!DescribeMarker(f, host)) return false;
                    break;
                default:
                    return false;
            }
        }
        // The shaders' pictures change with their clock, the list staying the same.
        f.Stamp = !shaders ? 0 : forCard ? _shaderClock.ElapsedTicks : ShaderSteps;
        return true;
    }

    private bool DescribeImage(PlaceFrame f, Image img, bool forCard, float t, ref bool shaders)
    {
        var mode = RenderOptions.GetBitmapScalingMode(img);
        var sampling = mode == BitmapScalingMode.NearestNeighbor ? PlaceSampling.Nearest
                     : mode is BitmapScalingMode.HighQuality or BitmapScalingMode.Fant ? PlaceSampling.Smooth
                     : PlaceSampling.Linear;
        var target = img.Source is WriteableBitmap ? ShaderTargetOf(img) : null;
        if (img.Source is WriteableBitmap && target == null) return false;     // a picture that changes, not a shader's: not known here
        if (img.Source is not BitmapSource bmp) return false;
        ref var item = ref f.Add();
        if (target != null)
        {
            shaders = true;
            if (forCard)
            {
                var picture = item.OnCard = target.OnCard ??= new PlaceCardPicture();
                var quality = Services.PreviewQualityManager.Current.Sampling;
                f.DrawFirst.Add(() => picture.Drawn = DrawOnCard(target, t, quality));
                // A level's layer is read by its nearest pixel, as WPF reads it; an NPC's between four - or, drawn
                // coarser (a lower quality), by its blocks.
                if (target.Params != null) sampling = quality.Step > 1 ? PlaceSampling.Nearest : PlaceSampling.Linear;
            }
            else item.Bitmap = bmp;
        }
        else if (bmp.IsFrozen) item.Bitmap = bmp;
        else return false;
        item.Kind = PlaceItemKind.Picture;
        item.Sampling = sampling;
        item.Opacity = (float)img.Opacity;
        item.Lit = (byte)(_unlitElements.Contains(img) ? 2 : 1);
        Put(ref item, BoxOf(img));
        return true;
    }

    private ShaderTarget? ShaderTargetOf(Image img)
    {
        foreach (var s in _shaderTargets)
            if (ReferenceEquals(s.Image, img)) return s;
        return null;
    }

    /// <summary>A shader's picture drawn on the card and left there, for the scene to read; where it is.</summary>
    private static Func<(Vortice.Direct3D11.ID3D11ShaderResourceView? Bytes, int W, int H)> DrawOnCard(ShaderTarget s, float t, JiggleShader.Sampling sampling)
    {
        if (s.Params != null)
        {
            var card = s.Card ??= GpuAlone.TryCreate();
            if (card == null) return () => (null, 0, 0);
            card.DrawOnCard(s.Base, s.Mask, s.SrcW, s.SrcH, s.Params, s.Tint, t, s.OutW, s.OutH, sampling, maskStill: true);
            return () => card.OnCard;
        }
        var milk = s.Milk ??= GpuMilking.TryCreate();
        if (milk == null) return () => (null, 0, 0);
        milk.DrawOnCard(s.Base, s.SrcW, s.SrcH, s.Mask, s.MaskW, s.MaskH, s.Milking, t, s.OutW, s.OutH, maskStill: !s.LiveMask);
        return () => milk.OnCard;
    }

    /// <summary>
    /// A Wet marker (an NPC's spray): its line and its drops, each its own picture - a plain fill for the line, a
    /// disc for a drop, its outline's colour under its fill's. The marker's own opacity goes to each.
    /// </summary>
    private static bool DescribeMarker(PlaceFrame f, Canvas host)
    {
        var hostMatrix = host.RenderTransform?.Value ?? Matrix.Identity;
        float group = (float)host.Opacity;
        foreach (UIElement child in host.Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            switch (child)
            {
                case Line line when line.Stroke is SolidColorBrush stroke:
                {
                    double dx = line.X2 - line.X1, dy = line.Y2 - line.Y1, len = Math.Sqrt(dx * dx + dy * dy), th = line.StrokeThickness;
                    if (len < 1e-9 || th <= 0) break;
                    double ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
                    // Along the line and half its thickness past each end (its round caps), its thickness across.
                    var m = new Matrix(ux * (len + th), uy * (len + th), nx * th, ny * th,
                                       line.X1 - ux * th / 2 - nx * th / 2, line.Y1 - uy * th / 2 - ny * th / 2);
                    m.Append(hostMatrix);
                    ref var item = ref f.Add();
                    item.BuiltIn = PlaceBuiltIn.Solid;
                    item.Kind = PlaceItemKind.Tint;
                    item.Sampling = PlaceSampling.Nearest;
                    Colour(ref item, stroke);
                    item.Opacity = (float)line.Opacity * group;
                    Put(ref item, m);
                    break;
                }
                case Ellipse drop when drop.Fill is SolidColorBrush fill:
                {
                    float opacity = (float)drop.Opacity * group;
                    if (!(opacity > 0f)) break;
                    var box = BoxOf(drop);
                    double th = drop.Stroke is SolidColorBrush ? drop.StrokeThickness : 0;
                    if (drop.Stroke is SolidColorBrush outline && th > 0)
                    {
                        var m = box;
                        m.Append(hostMatrix);
                        ref var ring = ref f.Add();
                        ring.BuiltIn = PlaceBuiltIn.Disc;
                        ring.Kind = PlaceItemKind.Tint;
                        ring.Sampling = PlaceSampling.Smooth;
                        Colour(ref ring, outline);
                        ring.Opacity = opacity;
                        Put(ref ring, m);
                    }
                    // The fill, inside half the outline's thickness: the unit square drawn in from its sides.
                    double w = Math.Max(1e-6, drop.Width), h = Math.Max(1e-6, drop.Height);
                    double ix = Math.Min(0.49, th / 2 / w), iy = Math.Min(0.49, th / 2 / h);
                    var inner = new Matrix(1 - 2 * ix, 0, 0, 1 - 2 * iy, ix, iy);
                    inner.Append(box);
                    inner.Append(hostMatrix);
                    ref var item = ref f.Add();
                    item.BuiltIn = PlaceBuiltIn.Disc;
                    item.Kind = PlaceItemKind.Tint;
                    item.Sampling = PlaceSampling.Smooth;
                    Colour(ref item, fill);
                    item.Opacity = opacity;
                    Put(ref item, inner);
                    break;
                }
                default:
                    return false;
            }
        }
        return true;
    }

    /// <summary>An element's box on the canvas: its unit square through its size, its own transform, and where the
    /// canvas has it - as WPF places it.</summary>
    private static Matrix BoxOf(FrameworkElement e)
    {
        double w = e.Width, h = e.Height;
        if (double.IsNaN(w)) w = e.ActualWidth;
        if (double.IsNaN(h)) h = e.ActualHeight;
        var m = new Matrix(w, 0, 0, h, 0, 0);
        if (e.RenderTransform is { } transform) m.Append(transform.Value);
        double left = Canvas.GetLeft(e), top = Canvas.GetTop(e);
        m.Translate(double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top);
        return m;
    }

    private static void Put(ref PlaceItem item, Matrix m)
    {
        item.M11 = m.M11; item.M12 = m.M12; item.M21 = m.M21; item.M22 = m.M22; item.OffsetX = m.OffsetX; item.OffsetY = m.OffsetY;
    }

    private static void Colour(ref PlaceItem item, SolidColorBrush brush)
    {
        var c = brush.Color;
        item.R = c.R / 255f; item.G = c.G / 255f; item.B = c.B / 255f;
        item.A = c.A / 255f * (float)brush.Opacity;
    }

    /// <summary>The view's place in a frame: the part of the canvas shown, at how many pixels, and what goes over it.</summary>
    private void Frame(PlaceFrame f, Rect rect, int vw, int vh, BackdropRender.PostParts parts, BackdropScene scene, PlaceLights? lights)
    {
        f.ViewX = rect.X; f.ViewY = rect.Y; f.ViewW = rect.Width; f.ViewH = rect.Height;
        f.Vw = vw; f.Vh = vh;
        f.CanvasW = CanvasWidth; f.CanvasH = CanvasHeight;
        f.Parts = parts;
        f.Scene = scene;
        f.Lights = lights;
        f.Masked = lights != null && _unlitElements.Count > 0;
    }

    /// <summary>The frame just described is the one the look shows (or is being worked out for): the next is
    /// compared with it.</summary>
    private void KeepFrame(bool described)
    {
        if (described) (_frameLast, _frameNext) = (_frameNext, _frameLast);
        _frameLastSet = described;
    }

    /// <summary>
    /// The frame drawn by the card - scene, lights, the game's look - off the interface's thread, then put on screen.
    /// The caller has kept the frame (<see cref="KeepFrame"/>): it is not touched again until this look is in.
    /// </summary>
    private void CardLook(PlaceFrame frame, Rect rect, int vw, int vh)
    {
        var output = _lookOut != null && _lookOut.Length == vw * vh * 4 ? _lookOut : (_lookOut = new byte[vw * vh * 4]);
        _cardLookCovers = true;
        if (frame.DrawFirst.Count > 0) ShaderSteps++;
        _lookInFlight = true;
        var ui = TaskScheduler.FromCurrentSynchronizationContext();
        Task.Run(() =>
            {
                long w0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var card = CardScene();
                var post = CardPost();
                if (card == null || post == null) throw new InvalidOperationException("no graphics card device");   // English on purpose: caught below, never shown
                card.Draw(frame, post, output);
                LookOnCard = true;
                System.Threading.Interlocked.Add(ref LookTime.Work, System.Diagnostics.Stopwatch.GetTimestamp() - w0);
            })
            .ContinueWith(t =>
            {
                _lookInFlight = false;
                if (t.IsFaulted)
                {
                    // The processor's way from here on: the next tick takes it.
                    _cardBroken = true;
                    CardLookOver();
                    _frameLastSet = false;
                    CardFailure = t.Exception?.GetBaseException().Message;
                    return;
                }
                if (!IsVisible) return;
                ShowLook(output, vw, vh, rect);
            }, ui);
    }

    /// <summary>Why the card's way was given up, when it was - for tests and the record.</summary>
    internal string? CardFailure { get; private set; }

    /// <summary>The look's picture on screen: over exactly what the view showed, one picture pixel a screen pixel.</summary>
    private void ShowLook(byte[] output, int vw, int vh, Rect rect)
    {
        long p0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lookBitmap == null || _lookBitmap.PixelWidth != vw || _lookBitmap.PixelHeight != vh)
        {
            _lookBitmap = new WriteableBitmap(vw, vh, 96, 96, PixelFormats.Bgra32, null);
            _lookImage.Source = _lookBitmap;
        }
        _lookBitmap.WritePixels(new Int32Rect(0, 0, vw, vh), output, vw * 4, 0);
        Canvas.SetLeft(_lookImage, rect.X); Canvas.SetTop(_lookImage, rect.Y);
        _lookImage.Width = rect.Width; _lookImage.Height = rect.Height;
        _lookViewRect = rect;
        _lookImage.Visibility = Visibility.Visible;
        LookPasses++;
        LookTime.Present += System.Diagnostics.Stopwatch.GetTimestamp() - p0;
    }

    /// <summary>
    /// As <see cref="RenderStill"/>, drawn by the card from the scene itself (the whole of the game's screen at
    /// 1920x1080) - for tests, which hold it to the processor's. Null with no card, or while the game's look is
    /// still loading.
    /// </summary>
    internal byte[]? RenderStillOnCard(bool look = true, bool lights = true)
    {
        FlushRebuild();
        var scene = Services.PreviewBackdropManager.GameLook;
        if (scene == null) return null;
        var frame = new PlaceFrame();
        if (!DescribeScene(frame, forCard: true)) return null;
        int w = (int)CanvasWidth, h = (int)CanvasHeight;
        Frame(frame, new Rect(0, 0, CanvasWidth, CanvasHeight), w, h,
              look ? new BackdropRender.PostParts(true, true, true) : new BackdropRender.PostParts(false, false, false),
              scene, lights && LookLights ? _placeLights : null);
        var card = CardScene();
        var post = CardPost();
        if (card == null || post == null) return null;
        var output = new byte[w * h * 4];
        card.Draw(frame, post, output);
        return output;
    }
}
