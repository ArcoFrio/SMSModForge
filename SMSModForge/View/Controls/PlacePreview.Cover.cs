using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Localization;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// What covers a layer of the place (after 1.10.0): the parts of the front layer, the back layer or an object that a
/// picture drawn in front of it hides - so the mask studio can say what is in front of the spot under the pointer. A
/// stroke there moves the layer, and in the game nobody sees it move: what shows there is the picture in front (in
/// SMSAndroids' bathroom, BathroomNear over 43% of the front layer). Worked out on the mask's grid - the layer's art
/// spread over 256x256, as the game reads a mask - from the pictures of the scene as the preview draws them, each at
/// its alpha. While the studio paints, this preview draws what is in front see-through (below).
/// </summary>
public sealed partial class PlacePreview
{
    /// <summary>The name each picture of the scene is known by (an object's, a character's, a layer's): what the
    /// studio says is in front.</summary>
    private readonly Dictionary<UIElement, string> _drawnNames = new();

    /// <summary>Each object's picture in the scene now.</summary>
    private readonly Dictionary<GameObjectViewModel, Image> _objectImages = new();

    // ── See-through while a layer is painted ───────────────────────────
    //
    // The author (after 1.10.0): "What I thought would happen was ... the preview would show the layer on the front a bit
    // transparent so we could see what we're painting behind it ... I really don't like the front layer overlayed on the
    // actual painting screen." So the studio's canvas shows the layer alone, and this preview - the one beside the
    // studio - draws everything in front of the layer painted see-through, the look and the card included (they read
    // each picture's opacity). Only the studio's own preview is told: the Places tab's is never see-through.

    /// <summary>How much of what is in front of the layer painted is drawn: enough to know it is there, little enough
    /// to watch the layer move behind it.</summary>
    internal const double SeeThroughOpacity = 0.35;

    private bool _paintingLayer, _paintingBack;
    private GameObjectViewModel? _paintingObject;

    /// <summary>Each picture made see-through by the last rebuild, and its opacity before: put back before the next
    /// one, so a picture that outlives a rebuild (the level's two layers) is never made fainter twice.</summary>
    private readonly Dictionary<UIElement, double> _seeThroughWas = new();

    /// <summary>How many pictures are see-through now - for tests.</summary>
    internal int SeenThrough => _seeThroughWas.Count;

    /// <summary>The mask studio paints the front layer's mask (or, <paramref name="back"/>, the back layer's) beside
    /// this preview: what is in front of it is see-through from now on. What covers it, to ask once it has drawn.</summary>
    internal System.Func<MaskCover?> PaintingLayer(bool back)
    {
        _paintingLayer = true; _paintingBack = back; _paintingObject = null;
        QueueRebuild();
        return () => CoverOverLayer(back);
    }

    /// <summary>As <see cref="PaintingLayer"/>, for an object's own mask.</summary>
    internal System.Func<MaskCover?> PaintingObject(GameObjectViewModel o)
    {
        _paintingLayer = false; _paintingObject = o;
        QueueRebuild();
        return () => CoverOver(o);
    }

    /// <summary>Before a rebuild: every picture back to its own opacity.</summary>
    private void SeeThroughUndo()
    {
        foreach (var (element, was) in _seeThroughWas) element.Opacity = was;
        _seeThroughWas.Clear();
    }

    /// <summary>After a rebuild: everything drawn after the picture painted, see-through.</summary>
    private void SeeThroughInFront()
    {
        UIElement? painted = _paintingObject != null ? (_objectImages.TryGetValue(_paintingObject, out var img) ? img : null)
                           : _paintingLayer ? (_paintingBack ? _secondaryImage : _baseImage)
                           : null;
        if (painted == null) return;
        int at = _worldLayer.Children.IndexOf(painted);
        if (at < 0) return;
        for (int i = at + 1; i < _worldLayer.Children.Count; i++)
        {
            var e = _worldLayer.Children[i];
            _seeThroughWas[e] = e.Opacity;
            e.Opacity *= SeeThroughOpacity;
        }
    }

    /// <summary>What covers the front layer (or, <paramref name="back"/>, the back layer).</summary>
    internal MaskCover? CoverOverLayer(bool back) => CoverOver(back ? _secondaryImage : _baseImage);

    /// <summary>What covers an object's picture.</summary>
    internal MaskCover? CoverOver(GameObjectViewModel o)
    {
        FlushRebuild();
        return _objectImages.TryGetValue(o, out var img) ? CoverOver(img) : null;
    }

    private MaskCover? CoverOver(Image layer)
    {
        FlushRebuild();
        int at = _worldLayer.Children.IndexOf(layer);
        if (at < 0 || layer.Source == null) return null;
        var box = BoxOf(layer);
        var over = new List<(Matrix Inverse, byte[] Px, int W, int H, string Name)>();
        for (int i = at + 1; i < _worldLayer.Children.Count; i++)
        {
            if (_worldLayer.Children[i] is not Image img || img.Visibility != Visibility.Visible || img.Source is not BitmapSource bmp) continue;
            var m = BoxOf(img);
            if (!m.HasInverse) continue;
            m.Invert();
            var conv = new FormatConvertedBitmap(bmp, PixelFormats.Pbgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            over.Add((m, px, w, h, _drawnNames.TryGetValue(img, out var name) ? name : Loc.T("preview.place.coverUnnamed")));
        }
        const int size = MaskBuffer.Size;
        var covered = new bool[size * size];
        var by = new string?[size * size];
        for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                var point = box.Transform(new Point((gx + 0.5) / size, (gy + 0.5) / size));
                // The topmost first: what shows there.
                for (int c = over.Count - 1; c >= 0; c--)
                {
                    var (inverse, px, w, h, name) = over[c];
                    var uv = inverse.Transform(point);
                    if (!(uv.X >= 0 && uv.X < 1 && uv.Y >= 0 && uv.Y < 1)) continue;
                    int tx = (int)(uv.X * w), ty = (int)(uv.Y * h);
                    if (px[(ty * w + tx) * 4 + 3] < 128) continue;
                    covered[gy * size + gx] = true;
                    by[gy * size + gx] = name;
                    break;
                }
            }
        return new MaskCover(covered, by);
    }
}
