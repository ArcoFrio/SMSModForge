using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// The place's lights in the preview window (1.10.0) - the author: "of course
/// all should be doable via the preview window". A light is an object's
/// (<see cref="GameObjectDef.Light"/>), so it is picked, moved and turned like
/// any object; on top of that:
/// <list type="bullet">
/// <item>"+ Light" over the hierarchy makes a light object in the middle of
/// the view, picked, ready to drag.</item>
/// <item>A sun at every light, over the picture (never in it: the look and its
/// pictures leave it out), to pick it by.</item>
/// <item>The picked object's light has its own handles: its reach (the outer
/// circle), how far it stays full (the inner one), and a cone's two edges.</item>
/// <item>The panel beside the picture edits its colour, strength, falloff and
/// glow, its sizes, and gives an object a light, takes it off or puts the
/// game's back.</item>
/// </list>
/// Where a light stands and how far it reaches are worked out as the look
/// draws them (<see cref="Rendering.PlaceLights"/>): its object's place through
/// the level's scale, its radii in world units whatever the scale.
/// </summary>
public sealed partial class PlacePreview
{
    /// <summary>Makes a light object in the place; its parameter is where, as a
    /// float[] { x, y } in the place's own units. The host binds the place's
    /// (or vanilla extension's) AddLightCommand.</summary>
    public static readonly DependencyProperty AddLightCommandProperty =
        DependencyProperty.Register(nameof(AddLightCommand), typeof(ICommand), typeof(PlacePreview),
            new PropertyMetadata(null, (d, _) => (d as PlacePreview)?.RefreshObjectMenu()));

    public ICommand? AddLightCommand
    {
        get => (ICommand?)GetValue(AddLightCommandProperty);
        set => SetValue(AddLightCommandProperty, value);
    }

    /// <summary>The suns, over the picture and under the handles.</summary>
    private readonly Canvas _lightLayer = new()
    {
        Width = CanvasWidth,
        Height = CanvasHeight,
        ClipToBounds = false,
    };

    private const double SunSize = 30, LightHandle = 22;
    private static readonly Brush LightRingBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4A));
    private static readonly Brush LightInnerBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xB8));

    /// <summary>The selected object's light, or null.</summary>
    private LightViewModel? SelectedLight => _selNode?.Light;

    /// <summary>Where an object of the scene stands on the canvas, as the look draws its light.</summary>
    private Point LightCentre(SceneEntry e) => new(WorldOriginX + WorldPpu * e.World.Tx, WorldOriginY - WorldPpu * e.World.Ty);

    /// <summary>The scene entry of the picked object, from the last walk.</summary>
    private SceneEntry? SelectedEntry()
        => _selNode == null ? null
         : _scene.FirstOrDefault(e => ReferenceEquals(e.Node, _selNode))
           ?? AtPath(_scene, _selNodePath, _selNodeTwin, npc: false);

    // ── The suns ────────────────────────────────────────────────────────

    /// <summary>A sun at every light the scene has (a switched-off light of the game's dimmed), clickable to pick it.</summary>
    private void DrawLightMarkers()
    {
        _lightLayer.Children.Clear();
        foreach (var e in _scene)
        {
            if (e.Node is not { } node || e.Hidden) continue;
            if (node.Model.Light is not { } def && !node.Model.RemoveLight) continue;
            var c = LightCentre(e);
            var colour = node.Model.Light != null ? ToColor(node.Model.Light.Color) : Color.FromRgb(0x80, 0x80, 0x80);
            var sun = Sun(colour, node.Model.RemoveLight);
            Canvas.SetLeft(sun, c.X - SunSize / 2); Canvas.SetTop(sun, c.Y - SunSize / 2);
            var n = node; var p = e.Path;
            // The sun is the light: on an object with a picture or an effect as well, its Light part (ObjectParts).
            sun.MouseLeftButtonDown += (_, ev) =>
            {
                SelectNode(n, p);
                if (LightIsApart(n)) SetObjectPart(ObjectPart.Light);
                ev.Handled = true;
            };
            sun.ToolTip = node.Display;
            _lightLayer.Children.Add(sun);
            TrackParallax(sun, e.ParallaxGain, e);
        }
    }

    private static Color ToColor(string hex)
    {
        var h = LightDef.NormalColor(hex);
        return Color.FromRgb(System.Convert.ToByte(h.Substring(1, 2), 16), System.Convert.ToByte(h.Substring(3, 2), 16),
                             System.Convert.ToByte(h.Substring(5, 2), 16));
    }

    /// <summary>A round disc of the light's colour with eight rays; faint and crossed through when switched off.</summary>
    private static FrameworkElement Sun(Color colour, bool off)
    {
        var g = new Grid { Width = SunSize, Height = SunSize, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var rays = new StreamGeometry();
        using (var ctx = rays.Open())
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4, r0 = SunSize * 0.34, r1 = SunSize * 0.5;
                ctx.BeginFigure(new Point(SunSize / 2 + Math.Cos(a) * r0, SunSize / 2 + Math.Sin(a) * r0), false, false);
                ctx.LineTo(new Point(SunSize / 2 + Math.Cos(a) * r1, SunSize / 2 + Math.Sin(a) * r1), true, false);
            }
        g.Children.Add(new Path { Data = rays, Stroke = Brushes.Black, StrokeThickness = 5, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round });
        g.Children.Add(new Path { Data = rays, Stroke = new SolidColorBrush(colour), StrokeThickness = 3, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round });
        g.Children.Add(new Ellipse { Width = SunSize * 0.5, Height = SunSize * 0.5, Fill = new SolidColorBrush(colour), Stroke = Brushes.Black, StrokeThickness = 1.5 });
        if (off)
        {
            g.Opacity = 0.55;
            g.Children.Add(new Line { X1 = 3, Y1 = SunSize - 3, X2 = SunSize - 3, Y2 = 3, Stroke = Brushes.Black, StrokeThickness = 3 });
        }
        return g;
    }

    // ── The picked light's handles ──────────────────────────────────────

    private Point _lightCentre;
    private double _lightTurn;     // the object's turn on screen, degrees anticlockwise
    private float _lightStartOuter, _lightStartInner;

    /// <summary>The light handles of the picked object, added after its move / turn / scale handles.</summary>
    private void DrawLightHandles()
    {
        var light = SelectedLight;
        var e = SelectedEntry();
        if (light == null || e == null) return;
        if (light.IsFree) { DrawFreeHandles(light, e); return; }
        if (!light.IsRound) return;
        _lightCentre = LightCentre(e);
        _lightTurn = Math.Atan2(e.World.B, e.World.A) * 180 / Math.PI;
        double outer = light.OuterRadius * PixelsPerUnit, inner = light.InnerRadius * PixelsPerUnit;

        AddLightRing(_lightCentre, outer, LightRingBrush, 3, null);
        if (inner > 0.5) AddLightRing(_lightCentre, inner, LightInnerBrush, 2, new DoubleCollection { 4, 3 });

        // A cone's edges, out from the centre along the light's up turned each way.
        if (light.IsCone)
        {
            foreach (double side in new[] { -1.0, 1.0 })
            {
                var edge = _lightCentre + Dir(_lightTurn + 90 + side * light.OuterAngle / 2) * outer;
                _gizmoLayer.Children.Add(new Line { X1 = _lightCentre.X, Y1 = _lightCentre.Y, X2 = edge.X, Y2 = edge.Y, Stroke = LightRingBrush, StrokeThickness = 2 });
                if (light.InnerAngle < light.OuterAngle)
                {
                    var full = _lightCentre + Dir(_lightTurn + 90 + side * light.InnerAngle / 2) * outer * 0.8;
                    _gizmoLayer.Children.Add(new Line { X1 = _lightCentre.X, Y1 = _lightCentre.Y, X2 = full.X, Y2 = full.Y, Stroke = LightInnerBrush,
                                                        StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 } });
                }
            }
        }

        // Handles: the reach on the right of the ring (along the light's own x), how far it stays full a little in,
        // and the cone's edges where they meet the ring.
        var axis = Dir(_lightTurn);
        AddLightHandle(_lightCentre + axis * outer, LightRingBrush, GizmoHandle.LightOuter, "preview.light.reach.tip");
        AddLightHandle(_lightCentre + axis * Math.Max(inner, 0), LightInnerBrush, GizmoHandle.LightInner, "preview.light.full.tip");
        double coneAt = light.IsCone ? light.OuterAngle / 2 : 180;
        AddLightHandle(_lightCentre + Dir(_lightTurn + 90 + coneAt) * outer, LightRingBrush, GizmoHandle.LightOuterAngle, "preview.light.cone.tip", diamond: true);
        if (light.IsCone)
            AddLightHandle(_lightCentre + Dir(_lightTurn + 90 + light.InnerAngle / 2) * outer * 0.8, LightInnerBrush,
                           GizmoHandle.LightInnerAngle, "preview.light.coneFull.tip", diamond: true);
    }

    /// <summary>A canvas direction for an angle in degrees anticlockwise from the right (canvas Y down).</summary>
    private static Vector Dir(double degrees)
    {
        double r = degrees * Math.PI / 180;
        return new Vector(Math.Cos(r), -Math.Sin(r));
    }

    private void AddLightRing(Point c, double radius, Brush brush, double thickness, DoubleCollection? dashes)
    {
        var ring = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false };
        if (dashes != null) ring.StrokeDashArray = dashes;
        var shade = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = Brushes.Black, StrokeThickness = thickness + 2, IsHitTestVisible = false, Opacity = 0.5 };
        foreach (var el in new[] { shade, ring })
        {
            Canvas.SetLeft(el, c.X - radius); Canvas.SetTop(el, c.Y - radius);
            _gizmoLayer.Children.Add(el);
        }
    }

    private void AddLightHandle(Point at, Brush brush, GizmoHandle h, string tipKey, bool diamond = false)
    {
        Shape knob = diamond
            ? new Polygon
            {
                Points = new PointCollection { new(LightHandle / 2, 0), new(LightHandle, LightHandle / 2), new(LightHandle / 2, LightHandle), new(0, LightHandle / 2) },
                Fill = brush, Stroke = Brushes.Black, StrokeThickness = 1.5,
            }
            : new Ellipse { Width = LightHandle, Height = LightHandle, Fill = brush, Stroke = Brushes.Black, StrokeThickness = 1.5 };
        Canvas.SetLeft(knob, at.X - LightHandle / 2); Canvas.SetTop(knob, at.Y - LightHandle / 2);
        knob.ToolTip = Loc.T(tipKey);
        Attach(knob, h);
        _gizmoLayer.Children.Add(knob);
        var hit = new Ellipse { Width = LightHandle + GizmoHit, Height = LightHandle + GizmoHit, Fill = Brushes.Transparent };
        Canvas.SetLeft(hit, at.X - (LightHandle + GizmoHit) / 2); Canvas.SetTop(hit, at.Y - (LightHandle + GizmoHit) / 2);
        hit.ToolTip = knob.ToolTip;
        Attach(hit, h);
        _gizmoLayer.Children.Add(hit);
    }

    private static bool IsLightHandle(GizmoHandle h)
        => h is GizmoHandle.LightOuter or GizmoHandle.LightInner or GizmoHandle.LightOuterAngle or GizmoHandle.LightInnerAngle
             or GizmoHandle.LightPoint or GizmoHandle.LightEdge;

    // ── A free shape's points ───────────────────────────────────────────

    private int _lightPoint = -1;
    private Aff _lightWorld;

    /// <summary>An object-space point of the picked light's object on the canvas.</summary>
    private Point ObjectToCanvas(in Aff w, double x, double y)
        => new(WorldOriginX + WorldPpu * (w.A * x + w.C * y + w.Tx), WorldOriginY - WorldPpu * (w.B * x + w.D * y + w.Ty));

    /// <summary>A canvas point in the object's own space (the inverse of <see cref="ObjectToCanvas"/>).</summary>
    private (float x, float y) CanvasToObject(in Aff w, Point p)
    {
        double wx = (p.X - WorldOriginX) / WorldPpu - w.Tx, wy = (WorldOriginY - p.Y) / WorldPpu - w.Ty;
        double det = w.A * w.D - w.C * w.B;
        if (Math.Abs(det) < 1e-12) return (0f, 0f);
        return ((float)Math.Round((w.D * wx - w.C * wy) / det, 3), (float)Math.Round((-w.B * wx + w.A * wy) / det, 3));
    }

    /// <summary>The free shape's outline, a handle on each point (drag to move it, right-click to take it away) and a
    /// "+" half way along each side (drag it to add a point there).</summary>
    private void DrawFreeHandles(LightViewModel light, SceneEntry e)
    {
        _lightWorld = e.World;
        var pts = light.Points;
        if (pts.Count < 2) return;
        var onCanvas = pts.Select(q => ObjectToCanvas(e.World, q.Length > 0 ? q[0] : 0, q.Length > 1 ? q[1] : 0)).ToList();
        var outline = new Polygon { Stroke = LightRingBrush, StrokeThickness = 3, IsHitTestVisible = false, Points = new PointCollection(onCanvas) };
        var shade = new Polygon { Stroke = Brushes.Black, StrokeThickness = 5, Opacity = 0.5, IsHitTestVisible = false, Points = new PointCollection(onCanvas) };
        _gizmoLayer.Children.Add(shade);
        _gizmoLayer.Children.Add(outline);
        DrawReach(light, e.World);
        for (int i = 0; i < onCanvas.Count; i++)
        {
            int at = i;
            var a = onCanvas[i];
            var b = onCanvas[(i + 1) % onCanvas.Count];
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            AddPointHandle(mid, LightInnerBrush, "preview.light.addPoint.tip", plus: true, ev =>
            {
                var (x, y) = CanvasToObject(e.World, mid);
                light.InsertPoint(at + 1, x, y);
                _lightPoint = at + 1;
                StartHandle(GizmoHandle.LightPoint, ev);
            });
        }
        for (int i = 0; i < onCanvas.Count; i++)
        {
            int at = i;
            var knob = AddPointHandle(onCanvas[i], LightRingBrush, "preview.light.point.tip", plus: false, ev =>
            {
                _lightPoint = at;
                StartHandle(GizmoHandle.LightPoint, ev);
            });
            knob.MouseRightButtonDown += (_, ev) =>
            {
                if (light.RemovePoint(at)) RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this));
                ev.Handled = true;
            };
        }
    }

    // ── A free shape's reach ────────────────────────────────────────────
    //
    // The author (after 1.10.0): "a hybrid round approach, where we shape how the source is going to be, and it emits
    // outward like a round light (respecting the shape of the origin)". That is what a free light is in the game: the
    // shape drawn is the light at full strength, and it shines out from all of its edge as far as its reach, fading
    // as a round light fades from its middle (the falloff), round at the corners (LightShape). What the preview did
    // not show was how far: the reach was a number in the panel. Now it is the dashed line where the light is gone,
    // with a knob to drag it out or in.

    private const double ReachKnobAway = 30;   // the knob stands at least this far out from its corner (canvas px)
    private float _lightStartEdge;
    private double _lightStartReach;

    /// <summary>An object's own space on the canvas (<see cref="ObjectToCanvas"/> as a matrix).</summary>
    private Matrix ObjectToCanvasMatrix(in Aff w)
        => new(WorldPpu * w.A, -WorldPpu * w.B, WorldPpu * w.C, -WorldPpu * w.D, WorldOriginX + WorldPpu * w.Tx, WorldOriginY - WorldPpu * w.Ty);

    private static System.Collections.Generic.List<Point> PointsOf(LightViewModel light)
        => light.Points.Where(q => q.Length >= 2).Select(q => new Point(q[0], q[1])).ToList();

    /// <summary>The line where a free light is gone, on the canvas: its shape grown by its reach all round, round at
    /// the corners - the ring URP draws round the shape (Clipper, round joins), in the object's own space. Null for a
    /// shape of fewer than three points, or no reach.</summary>
    private Geometry? ReachOutline(LightViewModel light, in Aff w)
    {
        var pts = PointsOf(light);
        if (pts.Count < 3 || light.Edge <= 0f) return null;
        var figure = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(pts.Skip(1), true));
        var shape = new PathGeometry(new[] { figure });
        // Fine enough for the canvas at any zoom: a hundredth of the reach, in the object's units.
        double tolerance = light.Edge / 100.0;
        var ring = shape.GetWidenedPathGeometry(new Pen(Brushes.Black, light.Edge * 2.0) { LineJoin = PenLineJoin.Round }, tolerance, ToleranceType.Absolute);
        var reach = Geometry.Combine(shape, ring, GeometryCombineMode.Union, null, tolerance, ToleranceType.Absolute);
        reach.Transform = new MatrixTransform(ObjectToCanvasMatrix(w));
        return reach;
    }

    /// <summary>Where the reach knob is drawn and the point of the reach line it stands for: out from the shape's
    /// corner furthest right on the canvas (always one that sticks out), half way between its two sides' outward
    /// directions - on the line, or <see cref="ReachKnobAway"/> out where the reach is less, so it is never under the
    /// corner's own knob.</summary>
    private (Point Knob, Point OnLine)? ReachKnob(LightViewModel light, in Aff w)
    {
        var pts = PointsOf(light);
        int n = pts.Count;
        if (n < 3) return null;
        var wc = w;
        var canvas = pts.Select(q => ObjectToCanvas(wc, q.X, q.Y)).ToList();
        int i = Enumerable.Range(0, n).OrderByDescending(k => canvas[k].X).First();
        double area = 0;
        for (int k = 0; k < n; k++) area += pts[k].X * pts[(k + 1) % n].Y - pts[(k + 1) % n].X * pts[k].Y;
        Vector Out(int a, int b)
        {
            var d = pts[b] - pts[a];
            if (d.Length < 1e-9) return new Vector();
            var normal = area >= 0 ? new Vector(d.Y, -d.X) : new Vector(-d.Y, d.X);
            normal.Normalize();
            return normal;
        }
        var dir = Out((i - 1 + n) % n, i) + Out(i, (i + 1) % n);
        if (dir.Length < 1e-9) dir = new Vector(1, 0);
        dir.Normalize();
        var corner = canvas[i];
        var onLine = ObjectToCanvas(w, pts[i].X + dir.X * light.Edge, pts[i].Y + dir.Y * light.Edge);
        var unit = ObjectToCanvas(w, pts[i].X + dir.X, pts[i].Y + dir.Y) - corner;
        if (unit.Length < 1e-9) return null;
        unit.Normalize();
        double away = Math.Max((onLine - corner).Length, ReachKnobAway);
        return (corner + unit * away, onLine);
    }

    /// <summary>How far a point of the object's own space is outside the shape (inside, how far in, as less than 0).</summary>
    private static double OutsideBy(System.Collections.Generic.List<Point> pts, Point p)
    {
        double best = double.MaxValue;
        bool inside = false;
        for (int k = 0, j = pts.Count - 1; k < pts.Count; j = k++)
        {
            Point a = pts[j], b = pts[k];
            if ((b.Y > p.Y) != (a.Y > p.Y) && p.X < (a.X - b.X) * (p.Y - b.Y) / (a.Y - b.Y) + b.X) inside = !inside;
            var ab = b - a;
            double t = ab.LengthSquared < 1e-18 ? 0 : Math.Clamp(Vector.Multiply(p - a, ab) / ab.LengthSquared, 0, 1);
            best = Math.Min(best, (p - (a + ab * t)).Length);
        }
        return inside ? -best : best;
    }

    private void DrawReach(LightViewModel light, in Aff w)
    {
        if (ReachOutline(light, w) is { } reach)
        {
            _gizmoLayer.Children.Add(new Path { Data = reach, Stroke = Brushes.Black, StrokeThickness = 4, Opacity = 0.5, IsHitTestVisible = false });
            _gizmoLayer.Children.Add(new Path { Data = reach, Stroke = LightInnerBrush, StrokeThickness = 2, IsHitTestVisible = false,
                                                StrokeDashArray = new DoubleCollection { 5, 4 } });
        }
        if (ReachKnob(light, w) is not { } knob) return;
        if ((knob.Knob - knob.OnLine).Length > 1)
            _gizmoLayer.Children.Add(new Line { X1 = knob.OnLine.X, Y1 = knob.OnLine.Y, X2 = knob.Knob.X, Y2 = knob.Knob.Y,
                                                Stroke = LightInnerBrush, StrokeThickness = 1.5, IsHitTestVisible = false,
                                                StrokeDashArray = new DoubleCollection { 2, 2 } });
        AddLightHandle(knob.Knob, LightInnerBrush, GizmoHandle.LightEdge, "preview.light.edge.drag.tip", diamond: true);
    }

    private Shape AddPointHandle(Point at, Brush brush, string tipKey, bool plus, Action<MouseButtonEventArgs> down)
    {
        double size = plus ? LightHandle * 0.8 : LightHandle;
        Shape knob = plus
            ? new Rectangle { Width = size, Height = size, Fill = brush, Stroke = Brushes.Black, StrokeThickness = 1.5, RadiusX = 3, RadiusY = 3 }
            : new Ellipse { Width = size, Height = size, Fill = brush, Stroke = Brushes.Black, StrokeThickness = 1.5 };
        Canvas.SetLeft(knob, at.X - size / 2); Canvas.SetTop(knob, at.Y - size / 2);
        knob.ToolTip = Loc.T(tipKey);
        knob.Cursor = Cursors.Hand;
        knob.MouseLeftButtonDown += (_, ev) => down(ev);
        _gizmoLayer.Children.Add(knob);
        if (plus)
        {
            var lines = new GeometryGroup();
            lines.Children.Add(new LineGeometry(new Point(at.X - size * 0.3, at.Y), new Point(at.X + size * 0.3, at.Y)));
            lines.Children.Add(new LineGeometry(new Point(at.X, at.Y - size * 0.3), new Point(at.X, at.Y + size * 0.3)));
            var cross = new Path { Data = lines, Stroke = Brushes.Black, StrokeThickness = 2, IsHitTestVisible = false };
            _gizmoLayer.Children.Add(cross);
        }
        return knob;
    }

    private void StartLightHandle()
    {
        var light = SelectedLight;
        if (light == null) return;
        _lightStartOuter = light.OuterRadius;
        _lightStartInner = light.InnerRadius;
        // The reach is dragged by how far the pointer moves out or in from the shape, from where it was taken: the
        // knob may stand further out than the reach (ReachKnob), and taking it must not jump the reach there.
        _lightStartEdge = light.Edge;
        var (x, y) = CanvasToObject(_lightWorld, _dragStartMouse);
        _lightStartReach = light.IsFree ? OutsideBy(PointsOf(light), new Point(x, y)) : 0;
    }

    /// <summary>A light handle dragged to <paramref name="p"/> (canvas): the radii are the pointer's distance from
    /// the centre, the cone's angles twice its angle from the light's up.</summary>
    private void DragLightHandle(Point p)
    {
        var light = SelectedLight;
        if (light == null) return;
        Vector d = p - _lightCentre;
        switch (_activeHandle)
        {
            case GizmoHandle.LightPoint:
            {
                var (x, y) = CanvasToObject(_lightWorld, p);
                light.SetPoint(_lightPoint, x, y);
                break;
            }
            case GizmoHandle.LightEdge:
            {
                var (x, y) = CanvasToObject(_lightWorld, p);
                double outside = OutsideBy(PointsOf(light), new Point(x, y));
                light.Edge = (float)Math.Round(Math.Max(0, _lightStartEdge + outside - _lightStartReach), 2);
                break;
            }
            case GizmoHandle.LightOuter:
                light.OuterRadius = (float)Math.Round(d.Length / PixelsPerUnit, 2);
                break;
            case GizmoHandle.LightInner:
                light.InnerRadius = (float)Math.Round(d.Length / PixelsPerUnit, 2);
                break;
            case GizmoHandle.LightOuterAngle:
            case GizmoHandle.LightInnerAngle:
            {
                double up = _lightTurn + 90;
                double at = Math.Atan2(-d.Y, d.X) * 180 / Math.PI;
                double off = Math.Abs(((at - up) % 360 + 540) % 360 - 180);   // 0..180 from the light's up
                float angle = (float)Math.Round(off * 2);
                if (_activeHandle == GizmoHandle.LightOuterAngle) light.OuterAngle = angle;
                else light.InnerAngle = angle;
                break;
            }
        }
    }

    // ── A shaped light's footprint ──────────────────────────────────────

    /// <summary>A freeform or picture light's footprint on the game's screen, from its object's transform (the scene's
    /// walk, through the level's scale as the canvas draws it); null for a picture that cannot be read.</summary>
    private Rendering.LightShape? ShapeOf(LightDef def, SceneEntry e, Vector shift = default)
    {
        var w = e.World;
        double kx = Rendering.PlaceLights.ScreenW / CanvasWidth, ky = Rendering.PlaceLights.ScreenH / CanvasHeight;
        // shift: how far its object has drifted on the canvas (canvas Y down).
        double a = WorldPpu * w.A * kx, c = WorldPpu * w.C * kx, tx = (WorldOriginX + WorldPpu * w.Tx + shift.X) * kx;
        double b = WorldPpu * w.B * ky, d = WorldPpu * w.D * ky, ty = (CanvasHeight - WorldOriginY + WorldPpu * w.Ty - shift.Y) * ky;
        if (def.IsFree)
        {
            var pts = def.Points.Where(q => q.Length >= 2).ToList();
            if (pts.Count < 3) return null;
            return Rendering.LightShape.Free(pts.Select(q => q[0]).ToArray(), pts.Select(q => q[1]).ToArray(), def.Edge, a, b, c, d, tx, ty);
        }
        var art = LightPicture(def.Picture);
        if (art == null) return null;
        return Rendering.LightShape.Picture(art.Value.Rgba, art.Value.W, art.Value.H, PackPicturePpu, art.Value.Identity, a, b, c, d, tx, ty);
    }

    /// <summary>The pixels per unit a pack's picture is made at in the game (the plugin's objects and picture lights).</summary>
    private const float PackPicturePpu = 70.32f;

    private static readonly System.Collections.Generic.Dictionary<string, (DateTime When, float[] Rgba, int W, int H)> _lightPictures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A picture light's picture as the game samples it: linear RGBA (sRGB decoded, as the game's picture is
    /// an sRGB texture), straight alpha, rows from the bottom. Read once per version of the file.</summary>
    private (float[] Rgba, int W, int H, string Identity)? LightPicture(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel) || string.IsNullOrWhiteSpace(PackRoot)) return null;
        string path = System.IO.Path.Combine(PackRoot, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(path)) return null;
        var when = System.IO.File.GetLastWriteTimeUtc(path);
        lock (_lightPictures)
            if (_lightPictures.TryGetValue(path, out var known) && known.When == when)
                return (known.Rgba, known.W, known.H, path + "@" + when.Ticks);
        try
        {
            BitmapSource bmp;
            using (var fs = System.IO.File.OpenRead(path))
                bmp = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            var straight = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = straight.PixelWidth, h = straight.PixelHeight;
            var px = new byte[w * h * 4];
            straight.CopyPixels(px, w * 4, 0);
            var decode = Services.PreviewBackdropManager.GameLook?.Backdrop.LookupDecode;
            float Lin(byte v) => decode != null ? decode[v] : v / 255f <= 0.04045f ? v / 255f / 12.92f : MathF.Pow((v / 255f + 0.055f) / 1.055f, 2.4f);
            var rgba = new float[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = ((h - 1 - y) * w + x) * 4, o = (y * w + x) * 4;
                    rgba[o] = Lin(px[i + 2]); rgba[o + 1] = Lin(px[i + 1]); rgba[o + 2] = Lin(px[i]); rgba[o + 3] = px[i + 3] / 255f;
                }
            lock (_lightPictures) _lightPictures[path] = (when, rgba, w, h);
            return (rgba, w, h, path + "@" + when.Ticks);
        }
        catch (Exception) { return null; }   // an unreadable picture lights nothing, as in the game
    }

    // ── "+ Light" ───────────────────────────────────────────────────────

    /// <summary>The buttons over the hierarchy that make something, as the host can: "+ Object" and "+ Character"
    /// (from the GameObjects list that was below the preview), "+ Light", "+ Darkness", "+ Effect", and a game's
    /// level's "Fill from vanilla hierarchy". Null when there are none.</summary>
    private UIElement? AddLightButton()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        // Wrapped at the list's visible width: inside a list that scrolls sideways it was given all the width it
        // wanted, never wrapped, and showing + Effect scrolled the whole list sideways - its heading read "lierarchy"
        // (review of every tutorial step, 1.10.0).
        if (_objectMenuScroll != null)
            row.SetBinding(MaxWidthProperty, new System.Windows.Data.Binding(nameof(ScrollViewer.ViewportWidth)) { Source = _objectMenuScroll });
        foreach (var made in MakeChips()) row.Children.Add(made);
        if (AddLightCommand != null)
        {
            var b = MakeChip(Chip("preview.light.add"), () => AddLightHere());
            LocText.Bind(b, ToolTipProperty, "preview.light.add.tip");
            var dark = MakeChip(Chip("preview.light.addDarkness"), () => AddLightHere(LightObjects.Darkness));
            LocText.Bind(dark, ToolTipProperty, "preview.light.addDarkness.tip");
            foreach (var chip in new[] { b, dark }) Paint(chip, false);
            // What the tutorials point at (1.10.0: the lights tutorial).
            TutorialAnchor.SetId(b, "btn:addLight");
            TutorialAnchor.SetId(dark, "btn:addDarkness");
            row.Children.Add(b);
            row.Children.Add(dark);
        }
        if (AddEffectChip() is { } effect) row.Children.Add(effect);
        if (FillChip() is { } fill) row.Children.Add(fill);
        return row.Children.Count > 0 ? row : null;
    }

    /// <summary>Make a light object in the middle of what the view shows - or, given <see cref="LightObjects.Darkness"/>,
    /// the place's own base light, darkened (1.10.0) - and pick it.</summary>
    internal void AddLightHere(string? kind = null)
    {
        var command = AddLightCommand;
        if (command == null) return;
        // The view's middle, on the canvas, in the world (a pack's own object stands at its X and Y in the world:
        // see WorldToLevel), as the handles' frame for such an object maps it.
        var mid = new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2);
        Point onCanvas = _viewport.ActualWidth > 0 ? _viewport.TranslatePoint(mid, _canvas) : new Point(CanvasWidth / 2, CanvasHeight / 2);
        var frame = FrameFromParent(WorldToLevel);
        var at = new[] { (float)Math.Round((onCanvas.X - frame.ParentOrigin.X) / frame.Ux.X, 2), (float)Math.Round((onCanvas.Y - frame.ParentOrigin.Y) / frame.Uy.Y, 2) };
        var before = new System.Collections.Generic.HashSet<object>(_scene.Where(e => e.Node != null).Select(e => (object)e.Node!));
        object parameter = kind == LightObjects.Darkness ? LightObjects.Darkness : at;
        if (!command.CanExecute(parameter)) return;
        command.Execute(parameter);
        RebuildOverlayGos();
        var made = _scene.LastOrDefault(e => e.Node != null && !before.Contains(e.Node) && e.Node.HasLight);
        if (made?.Node != null) SelectNode(made.Node, made.Path);
    }

    // ── The panel ───────────────────────────────────────────────────────

    private StackPanel _lightPanel = new();
    private TextBlock _lightNote = new();
    private Button _lightGive = new(), _lightRestore = new(), _lightRemove = new();
    private Button _shapeRound = new(), _shapeFree = new(), _shapePicture = new(), _shapeAmbient = new();
    private StackPanel _falloffField = new();
    private StackPanel _lightFields = new(), _roundFields = new(), _freeFields = new(), _pictureFields = new(), _lampFields = new();
    private TextBlock _charactersLabel = new();
    private Border _lightSwatch = new();

    private StackPanel BuildLightPanel()
    {
        _lightPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        TutorialAnchor.SetId(_lightPanel, "panel:light");
        var heading = Chip("preview.light.heading");
        heading.FontWeight = FontWeights.Bold;
        heading.Foreground = ChipText;
        heading.Margin = new Thickness(0, 0, 0, 4);
        _lightPanel.Children.Add(heading);

        _lightNote = new TextBlock { Foreground = ChipText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = 0.9, Margin = new Thickness(0, 0, 0, 4) };
        _lightPanel.Children.Add(_lightNote);

        _lightGive = MakeChip(Chip("preview.light.give"), () => _selNode?.AddLightCommand.Execute(null));
        LocText.Bind(_lightGive, ToolTipProperty, "preview.light.give.tip");
        _lightPanel.Children.Add(Row(_lightGive));

        _shapeRound = MakeChip(Chip("preview.light.shape.round"), () => SetShape(LightDef.ShapeRound));
        _shapeFree = MakeChip(Chip("preview.light.shape.free"), () => SetShape(LightDef.ShapeFree));
        _shapePicture = MakeChip(Chip("preview.light.shape.picture"), () => SetShape(LightDef.ShapePicture));
        LocText.Bind(_shapeRound, ToolTipProperty, "preview.light.shape.round.tip");
        LocText.Bind(_shapeFree, ToolTipProperty, "preview.light.shape.free.tip");
        LocText.Bind(_shapePicture, ToolTipProperty, "preview.light.shape.picture.tip");
        _shapeAmbient = MakeChip(Chip("preview.light.shape.ambient"), () => SetShape(LightDef.ShapeAmbient));
        LocText.Bind(_shapeAmbient, ToolTipProperty, "preview.light.shape.ambient.tip");

        _lightFields = new StackPanel();
        _lightFields.Children.Add(Row(_shapeRound, _shapeFree, _shapePicture, _shapeAmbient));

        // The colour: a swatch that opens the picker, and its hex.
        _lightSwatch = new Border { Width = 22, Height = 18, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand };
        LocText.Bind(_lightSwatch, ToolTipProperty, "preview.light.colour.tip");
        _lightSwatch.MouseLeftButtonDown += (_, e) => { PickLightColour(); e.Handled = true; };
        var hex = NumberBox(nameof(LightViewModel.Color), 70, null);
        _lightFields.Children.Add(Field("preview.light.colour", "preview.light.colour.tip", _lightSwatch, hex));

        _lightFields.Children.Add(SliderField("preview.light.strength", "preview.light.strength.tip", nameof(LightViewModel.Strength), 0, 2, "0.##"));
        // What a lamp has and the whole place's light has not.
        _lampFields = new StackPanel();
        _falloffField = SliderField("preview.light.falloff", "preview.light.falloff.tip", nameof(LightViewModel.Falloff), 0, 1, "0.##");
        _lampFields.Children.Add(_falloffField);
        _lampFields.Children.Add(SliderField("preview.light.glow", "preview.light.glow.tip", nameof(LightViewModel.Glow), 0, 1, "0.##"));
        _lightFields.Children.Add(_lampFields);

        // Light the characters too (1.10.0).
        var characters = new CheckBox { Foreground = ChipText, FontSize = 11, Margin = new Thickness(0, 2, 0, 4) };
        // Its words are the light's or the whole place's, set as the panel shows one (UpdateLightPanel).
        _charactersLabel = new TextBlock { Foreground = ChipText };
        characters.Content = _charactersLabel;
        LocText.Bind(characters, ToolTipProperty, "preview.light.characters.tip");
        characters.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                              new Binding(nameof(LightViewModel.LightsCharacters)) { Mode = BindingMode.TwoWay });
        characters.Click += (_, _) => RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this));
        _lightFields.Children.Add(characters);

        _roundFields = new StackPanel();
        _roundFields.Children.Add(Field("preview.light.reach", "preview.light.reach.tip", NumberBox(nameof(LightViewModel.OuterRadius), 60, "0.##")));
        _roundFields.Children.Add(Field("preview.light.full", "preview.light.full.tip", NumberBox(nameof(LightViewModel.InnerRadius), 60, "0.##")));
        _roundFields.Children.Add(SliderField("preview.light.cone", "preview.light.cone.tip", nameof(LightViewModel.OuterAngle), 1, 360, "0"));
        // Only while the light is a cone: all round, there is no edge for it to be full up to, and the pack
        // does not keep one (it was a slider that moved and changed nothing, 1.10.0).
        var coneFull = SliderField("preview.light.coneFull", "preview.light.coneFull.tip", nameof(LightViewModel.InnerAngle), 0, 360, "0");
        coneFull.SetBinding(IsEnabledProperty, new Binding(nameof(LightViewModel.IsCone)));
        _roundFields.Children.Add(coneFull);
        _lightFields.Children.Add(_roundFields);

        _freeFields = new StackPanel();
        _freeFields.Children.Add(Field("preview.light.edge", "preview.light.edge.tip", NumberBox(nameof(LightViewModel.Edge), 60, "0.##")));
        _lightFields.Children.Add(_freeFields);

        _pictureFields = new StackPanel();
        var picture = new PathPickerBox { Width = 150 };
        picture.SetBinding(PathPickerBox.PathTextProperty, new Binding(nameof(LightViewModel.Picture)) { Mode = BindingMode.TwoWay });
        picture.SetBinding(PathPickerBox.PackRootProperty, new Binding(nameof(PackRoot)) { Source = this });
        _pictureFields.Children.Add(Field("preview.light.picture", "preview.light.picture.tip", picture));
        _lightFields.Children.Add(_pictureFields);

        // How the lamp drifts (PlacePreview.Drift).
        _lightFields.Children.Add(BuildDriftField(_lightDrift, LampTips, "panel:lightDrift", "btn:lightDriftBack"));

        _lightPanel.Children.Add(_lightFields);

        _lightRestore = MakeChip(Chip("preview.light.restore"), () => _selNode?.RestoreLightCommand.Execute(null));
        LocText.Bind(_lightRestore, ToolTipProperty, "preview.light.restore.tip");
        _lightRemove = MakeChip(Chip("preview.light.remove"), () => _selNode?.RemoveLightCommand.Execute(null));
        LocText.Bind(_lightRemove, ToolTipProperty, "preview.light.remove.tip");
        _lightPanel.Children.Add(Row(_lightRestore, _lightRemove));
        foreach (var b in new[] { _lightGive, _lightRestore, _lightRemove }) Paint(b, false);
        return _lightPanel;
    }

    private static WrapPanel Row(params UIElement[] items)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var i in items) row.Children.Add(i);
        return row;
    }

    /// <summary>A labelled row: its words, then what edits it.</summary>
    private static StackPanel Field(string key, string tipKey, params UIElement[] editors)
    {
        var label = Chip(key);
        label.Foreground = ChipText;
        label.FontSize = 11;
        label.Margin = new Thickness(0, 2, 0, 1);
        LocText.Bind(label, ToolTipProperty, tipKey);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        foreach (var e in editors) if (e is FrameworkElement fe) LocText.Bind(fe, ToolTipProperty, tipKey);
        foreach (var e in editors) row.Children.Add(e);
        var field = new StackPanel();
        field.Children.Add(label);
        field.Children.Add(row);
        return field;
    }

    private static readonly Brush FieldBack = new SolidColorBrush(Color.FromRgb(0x2E, 0x31, 0x36));

    /// <summary>A box bound to one of the light's values, two-way; a typed value goes in when the box is left or
    /// Enter is pressed (and the window's undo takes it when the box is left).</summary>
    private TextBox NumberBox(string path, double width, string? format)
    {
        var box = new TextBox
        {
            Width = width, FontSize = 11, Background = FieldBack, Foreground = ChipText, CaretBrush = ChipText,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x5A, 0x60)), Padding = new Thickness(2, 0, 2, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var binding = new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, StringFormat = format,
                                          ConverterCulture = System.Globalization.CultureInfo.InvariantCulture };
        box.SetBinding(TextBox.TextProperty, binding);
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this));
        };
        return box;
    }

    /// <summary>A slider and its number, bound to one of the light's values; one undo step when the slider is let go.</summary>
    private StackPanel SliderField(string key, string tipKey, string path, double min, double max, string format)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Width = 90, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        slider.SetBinding(RangeBase.ValueProperty, new Binding(path) { Mode = BindingMode.TwoWay });
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this))));
        slider.PreviewMouseLeftButtonUp += (_, _) => RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this));
        return Field(key, tipKey, slider, NumberBox(path, 44, format));
    }

    private void SetShape(string shape)
    {
        var light = SelectedLight;
        if (light == null || light.Shape == shape) return;
        light.Shape = shape;
        RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this));
        UpdateLightPanel();
        RefreshGizmo();
    }

    private void PickLightColour()
    {
        var light = SelectedLight;
        if (light == null || Services.TestMode.Active) return;
        var owner = Window.GetWindow(this);
        if (owner == null) return;
        var chosen = ColorPickerWindow.Pick(owner, light.Color);
        if (chosen == null) return;
        light.Color = chosen;
        RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this));
        UpdateLightPanel();
    }

    /// <summary>Show what the picked object's light offers: a way to give it one, its settings, or the game's light
    /// switched off with the way back.</summary>
    private void UpdateLightPanel()
    {
        var node = _selNode;
        // With nothing picked the panel shows no light at all - not, out of sight, the last one it showed
        // (which an undo may since have taken out of the pack).
        if (node == null || _selPlacement != null) { _lightPanel.Visibility = Visibility.Collapsed; _lightPanel.DataContext = null; UpdateDrift(); return; }
        _lightPanel.Visibility = Visibility.Visible;
        var light = node.Light;
        _lightGive.Visibility = light == null && !node.LightRemoved ? Visibility.Visible : Visibility.Collapsed;
        _lightFields.Visibility = light != null ? Visibility.Visible : Visibility.Collapsed;
        _lightRemove.Visibility = light != null ? Visibility.Visible : Visibility.Collapsed;
        _lightRestore.Visibility = node.CanRestoreLight ? Visibility.Visible : Visibility.Collapsed;
        string note = node.LightRemoved ? Loc.T("preview.light.removed")
                    : node.IsTemplateLight ? Loc.T("preview.light.templateNote")
                    : "";
        _lightNote.Text = note;
        _lightNote.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lightPanel.DataContext = light;
        UpdateDrift();
        if (light == null) return;
        Paint(_shapeRound, light.IsRound);
        Paint(_shapeFree, light.IsFree);
        Paint(_shapePicture, light.IsPicture);
        Paint(_shapeAmbient, light.IsAmbient);
        _lampFields.Visibility = light.IsAmbient ? Visibility.Collapsed : Visibility.Visible;
        // A picture is its own fading: the game has none to set for it, and the pack keeps none.
        _falloffField.Visibility = light.IsPicture ? Visibility.Collapsed : Visibility.Visible;
        _charactersLabel.Text = Loc.T(light.IsAmbient ? "preview.light.ambientCharacters" : "preview.light.characters");
        _roundFields.Visibility = light.IsRound ? Visibility.Visible : Visibility.Collapsed;
        _freeFields.Visibility = light.IsFree ? Visibility.Visible : Visibility.Collapsed;
        _pictureFields.Visibility = light.IsPicture ? Visibility.Visible : Visibility.Collapsed;
        _lightSwatch.Background = new SolidColorBrush(ToColor(light.Color));
    }
}
