using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SMSModForge.View.Controls;

/// <summary>
/// What the player sees of the place (after 1.10.0, the author: "Could you please confirm what the player actually is
/// able to see? I know the viewable area is generally smaller than the textures we're using for places, and it should
/// be shown in an unobtrusive way in the preview too").
/// <para/>
/// The game's camera always shows 19.2 x 10.8 units round its middle - its Pixel Perfect Camera draws 960x540 at 50
/// pixels a unit (times 2 from 1080p up) and stretches that to any screen (Crop Frame "Stretch Fill"; read from the
/// game) - which is the preview's canvas, 1920x1080 at 100 a unit, exactly. A place's pictures are larger (a pack's
/// 2048x1136 art is 23 x 12.8 units at the level's scale): the player sees the middle 83% of its width and 85% of its
/// height, and the rest only as the cursor drifts the pictures under the camera. So outside the canvas is drawn a
/// little darker, with a thin line round its edge - seen when the view is zoomed out or moved, nothing at all at the
/// view's own fit. Never in the pictures the look and the tests take (it is over the scene, not in it).
/// </summary>
public sealed partial class PlacePreview
{
    private static readonly Geometry OutsideTheView = MakeOutside();

    private static Geometry MakeOutside()
    {
        const double far = 40000;
        var g = new CombinedGeometry(GeometryCombineMode.Exclude,
                                     new RectangleGeometry(new Rect(-far, -far, 2 * far + CanvasWidth, 2 * far + CanvasHeight)),
                                     new RectangleGeometry(new Rect(0, 0, CanvasWidth, CanvasHeight)));
        g.Freeze();
        return g;
    }

    /// <summary>Outside what the player sees: a little darker.</summary>
    private readonly Path _outsideView = new()
    {
        Data = OutsideTheView,
        Fill = Frozen(Color.FromArgb(0x66, 0x00, 0x00, 0x00)),
        IsHitTestVisible = false,
    };

    /// <summary>The edge of what the player sees: a thin line round it, a screen pixel and a half whatever the zoom.</summary>
    private readonly Rectangle _viewEdge = new()
    {
        Width = CanvasWidth,
        Height = CanvasHeight,
        Stroke = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
        StrokeThickness = 1.5,
        IsHitTestVisible = false,
        SnapsToDevicePixels = true,
    };

    /// <summary>Over the scene and its look, under the interface: called once, from the constructor.</summary>
    private void AddPlayersView()
    {
        _canvas.Children.Add(_outsideView);
        _canvas.Children.Add(_viewEdge);
    }

    /// <summary>The edge's line kept a screen pixel and a half wide at any zoom, just outside the edge (ApplyViewScale):
    /// at the view's own fit it is past the view's border, not over the picture.</summary>
    private void SizePlayersView()
    {
        double s = _displayScale > 1e-6 ? _displayScale : 1;
        double t = 1.5 / s;
        _viewEdge.StrokeThickness = t;
        _viewEdge.Width = CanvasWidth + 2 * t;
        _viewEdge.Height = CanvasHeight + 2 * t;
        System.Windows.Controls.Canvas.SetLeft(_viewEdge, -t);
        System.Windows.Controls.Canvas.SetTop(_viewEdge, -t);
    }

    /// <summary>Where the corners of what the player sees are drawn in the picture, its top left and bottom right, in
    /// the picture's own pixels - for tests: at the view's own fit, the picture's corners.</summary>
    internal (Point TopLeft, Point BottomRight) ViewDrawnAt
        => (_canvas.TranslatePoint(new Point(0, 0), _viewport), _canvas.TranslatePoint(new Point(CanvasWidth, CanvasHeight), _viewport));

    /// <summary>The picture's own size on screen - for tests.</summary>
    internal Size PictureSize => new(_viewport.ActualWidth, _viewport.ActualHeight);

    /// <summary>Whether outside what the player sees is marked - for tests.</summary>
    internal bool PlayersViewMarked => _outsideView.Parent != null && _viewEdge.Parent != null;
}
