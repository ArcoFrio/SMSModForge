using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using FX = SMSModForge.Shared.Effects;
using Ellipse = System.Windows.Shapes.Ellipse;
using Line = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace SMSModForge.View.Controls;

/// <summary>
/// An object's light and effect as parts of it, as a character's shadow and blink are (after 1.10.0, the author:
/// "when we add an effect to a GO, can we have the effect appear like if it were an extra GO in an NPC (Shadow,
/// Blink, etc)? And similarly to the wet particles, I'd like to when we have it selected I have a scalable box around
/// it. And if we add a light, I'd also like to have it appear up there so we can select it and edit like we would a
/// normal light. Keep in mind that when either is created, we'd also need to have a button for the main GO").
/// <list type="bullet">
/// <item><b>Object</b>: the object's own move / turn / size handles, and nothing of its light's over them.</item>
/// <item><b>Light</b>: the light's handles only, as a light of its own has them. The light is where the object is.</item>
/// <item><b>Effect</b>: a box round where the effect's particles start. Dragged inside, the effect moves away from the
/// object - an offset in the object's own units, the effect's local position under it in the game; its grips size it
/// (the effect's Scale, one number for both ways, as the game's).</item>
/// </list>
/// Offered when there is something to tell apart: an effect, or a light on an object that draws a picture. A light
/// and nothing else is a light, and keeps the handles it always had - its own and the light's together.
/// </summary>
public sealed partial class PlacePreview
{
    internal enum ObjectPart { Object, Light, Effect }

    private WrapPanel _objPartRow = new();
    private Button _btnObject = new(), _btnObjLight = new(), _btnObjEffect = new();

    /// <summary>The part chosen with the chips: the part shown while the picked object has it (as
    /// <see cref="_partChosen"/> is for a character's).</summary>
    private ObjectPart _objPartChosen = ObjectPart.Object;

    /// <summary>The part of the picked object the handles are for: the one chosen, while the object has it.</summary>
    internal ObjectPart PickedObjectPart
        => _selPlacement == null && _selNode is { } n && HasParts(n) && Offers(n, _objPartChosen) ? _objPartChosen : ObjectPart.Object;

    /// <summary>Whether the picked object's parts are offered (tests).</summary>
    internal bool ObjectPartsShown => _objPartRow.Visibility == Visibility.Visible;

    /// <summary>Which of the parts' chips are shown (tests).</summary>
    internal ObjectPart[] ObjectPartsOffered
        => ObjectPartsShown
            ? new[] { (_btnObject, ObjectPart.Object), (_btnObjLight, ObjectPart.Light), (_btnObjEffect, ObjectPart.Effect) }
                  .Where(c => c.Item1.Visibility == Visibility.Visible).Select(c => c.Item2).ToArray()
            : Array.Empty<ObjectPart>();

    private static bool DrawsAPicture(GameObjectViewModel n)
        => !string.IsNullOrEmpty(n.Model.Sprite) || n.Model.Baseline?.SpriteRenderer != null;

    /// <summary>Whether <paramref name="n"/>'s light is a part of its own to pick: there is something else of the
    /// object's for its handles to be in the way of.</summary>
    private static bool LightIsApart(GameObjectViewModel n) => n.HasLight && (DrawsAPicture(n) || n.HasEffect);

    private static bool HasParts(GameObjectViewModel n) => LightIsApart(n) || n.HasEffect;

    private static bool Offers(GameObjectViewModel n, ObjectPart p) => p switch
    {
        ObjectPart.Light => LightIsApart(n),
        ObjectPart.Effect => n.HasEffect,
        _ => true,
    };

    private WrapPanel BuildObjectPartRow()
    {
        _btnObject = MakeChip(Chip("preview.gizmo.object"), () => SetObjectPart(ObjectPart.Object));
        _btnObjLight = MakeChip(Chip("preview.gizmo.light"), () => SetObjectPart(ObjectPart.Light));
        _btnObjEffect = MakeChip(Chip("preview.gizmo.effect"), () => SetObjectPart(ObjectPart.Effect));
        LocText.Bind(_btnObject, ToolTipProperty, "preview.gizmo.object.tip");
        LocText.Bind(_btnObjLight, ToolTipProperty, "preview.gizmo.light.tip");
        LocText.Bind(_btnObjEffect, ToolTipProperty, "preview.gizmo.effect.tip");
        _objPartRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed };
        foreach (var b in new[] { _btnObject, _btnObjLight, _btnObjEffect }) _objPartRow.Children.Add(b);
        return _objPartRow;
    }

    /// <summary>Pick a part of the picked object, as its chip does.</summary>
    internal void SetObjectPart(ObjectPart part)
    {
        _objPartChosen = part;
        UpdateGizmoToolbar();
        RefreshGizmo();
    }

    /// <summary>The object's chips as the picked object offers them, the part shown lit.</summary>
    private void UpdateObjectParts()
    {
        var n = _selPlacement == null ? _selNode : null;
        bool shown = n != null && HasParts(n);
        _objPartRow.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) return;
        _btnObjLight.Visibility = LightIsApart(n!) ? Visibility.Visible : Visibility.Collapsed;
        _btnObjEffect.Visibility = n!.HasEffect ? Visibility.Visible : Visibility.Collapsed;
        var part = PickedObjectPart;
        Paint(_btnObject, part == ObjectPart.Object);
        Paint(_btnObjLight, part == ObjectPart.Light);
        Paint(_btnObjEffect, part == ObjectPart.Effect);
    }

    /// <summary>The handles of the picked object's light or effect, when one is the part picked: true when drawn,
    /// and the object's own are not.</summary>
    private bool DrawObjectPart()
    {
        switch (PickedObjectPart)
        {
            case ObjectPart.Light: DrawLightHandles(); return true;
            case ObjectPart.Effect: DrawEffectBox(); return true;
            default: return false;
        }
    }

    // ── The effect's box ────────────────────────────────────────────────

    /// <summary>The least a box is across before its Scale, canvas px: an effect whose particles start at one point
    /// (a burst, a flame) still has a box to take hold of, sized with the effect as any other.</summary>
    internal const double EffectBoxLeast = 60;

    /// <summary>The smallest Scale a grip leaves an effect.</summary>
    internal const double MinEffectScale = 0.05;

    private const double EffectGrip = 14;
    private static readonly Brush EffectBoxBrush = MakeEffectBoxBrush();

    private static Brush MakeEffectBoxBrush()
    {
        var b = new SolidColorBrush(Color.FromRgb(0xFF, 0xA6, 0x4D));
        b.Freeze();
        return b;
    }

    /// <summary>Where an object's effect plays on the canvas: at the object, moved by its offset in the object's own
    /// units - as its copy stands under the object in the game.</summary>
    private Point EffectCentre(SceneEntry e, EffectDef def) => ObjectToCanvas(e.World, def.LocalX ?? 0, def.LocalY ?? 0);

    /// <summary>The box round where the effect's particles start, as the preview starts them (EffectPreview's
    /// spawn area: the template's, in the screen's height, times the effect's Scale) - never less than
    /// <see cref="EffectBoxLeast"/> across before the Scale.</summary>
    private static Size EffectBoxSize(EffectDef def)
    {
        var look = EffectPreview.EffectLook.For(FX.Find(def.Template) ?? FX.Catalog[0]);
        double scale = def.Scale ?? 1;
        return new Size(Math.Max(look.SpawnWidth * CanvasHeight, EffectBoxLeast) * scale,
                        Math.Max(look.SpawnHeight * CanvasHeight, EffectBoxLeast) * scale);
    }

    /// <summary>The effect's box as last drawn, on the canvas (tests); null when none is.</summary>
    internal Rect? EffectBoxDrawn { get; private set; }

    /// <summary>Where the effect's grips were drawn, the corners first (tests take a grip there).</summary>
    internal Point[] EffectGripsDrawn { get; private set; } = Array.Empty<Point>();

    /// <summary>The picked object's effect: its box, a grip at each corner and side, and a line back to the object
    /// when it has been moved away from it.</summary>
    private void DrawEffectBox()
    {
        EffectBoxDrawn = null;
        EffectGripsDrawn = Array.Empty<Point>();
        var e = SelectedEntry();
        if (e?.Node?.Model.Effect is not { } def) return;
        var c = EffectCentre(e, def);
        var size = EffectBoxSize(def);
        var box = new Rect(c.X - size.Width / 2, c.Y - size.Height / 2, size.Width, size.Height);
        EffectBoxDrawn = box;

        // Inside it: drag to move it.
        var inside = new Rectangle { Width = box.Width, Height = box.Height, Fill = Brushes.Transparent };
        Canvas.SetLeft(inside, box.X); Canvas.SetTop(inside, box.Y);
        Attach(inside, GizmoHandle.EffectMove);
        inside.Cursor = Cursors.SizeAll;
        inside.ToolTip = Loc.T("preview.effect.box.tip");
        _gizmoLayer.Children.Add(inside);

        // Back to the object, when it is away from it: what it moves with.
        var at = ObjectToCanvas(e.World, 0, 0);
        if ((at - c).Length > 2)
        {
            _gizmoLayer.Children.Add(new Line { X1 = at.X, Y1 = at.Y, X2 = c.X, Y2 = c.Y, Stroke = EffectBoxBrush, StrokeThickness = 1.5,
                                                StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false });
            var pip = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false };
            Canvas.SetLeft(pip, at.X - 5); Canvas.SetTop(pip, at.Y - 5);
            _gizmoLayer.Children.Add(pip);
        }

        foreach (var (stroke, thickness, opacity, dashed) in new[] { (Brushes.Black, 4.0, 0.5, false), (EffectBoxBrush, 2.0, 1.0, true) })
        {
            var outline = new Rectangle { Width = box.Width, Height = box.Height, Stroke = stroke, StrokeThickness = thickness, Opacity = opacity,
                                          IsHitTestVisible = false };
            if (dashed) outline.StrokeDashArray = new DoubleCollection { 5, 4 };
            Canvas.SetLeft(outline, box.X); Canvas.SetTop(outline, box.Y);
            _gizmoLayer.Children.Add(outline);
        }

        // Its middle, a small cross.
        foreach (var (dx, dy) in new[] { (1.0, 0.0), (0.0, 1.0) })
            _gizmoLayer.Children.Add(new Line { X1 = c.X - dx * 7, Y1 = c.Y - dy * 7, X2 = c.X + dx * 7, Y2 = c.Y + dy * 7, Stroke = EffectBoxBrush,
                                                StrokeThickness = 2, IsHitTestVisible = false });

        // The grips: the corners, then the sides' middles.
        var grips = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1), (0, -1), (1, 0), (0, 1), (-1, 0) };
        EffectGripsDrawn = grips.Select(g => new Point(c.X + g.Item1 * box.Width / 2, c.Y + g.Item2 * box.Height / 2)).ToArray();
        for (int i = 0; i < grips.Length; i++)
        {
            var (gx, gy) = grips[i];
            var where = EffectGripsDrawn[i];
            var cursor = gx == 0 ? Cursors.SizeNS : gy == 0 ? Cursors.SizeWE : gx == gy ? Cursors.SizeNWSE : Cursors.SizeNESW;
            var hit = new Rectangle { Width = EffectGrip + 10, Height = EffectGrip + 10, Fill = Brushes.Transparent };
            Canvas.SetLeft(hit, where.X - hit.Width / 2); Canvas.SetTop(hit, where.Y - hit.Height / 2);
            var grip = new Rectangle { Width = EffectGrip, Height = EffectGrip, Fill = EffectBoxBrush, Stroke = Brushes.Black, StrokeThickness = 1.5 };
            Canvas.SetLeft(grip, where.X - EffectGrip / 2); Canvas.SetTop(grip, where.Y - EffectGrip / 2);
            foreach (var s in new[] { grip, hit })
            {
                Attach(s, GizmoHandle.EffectSize);
                s.Cursor = cursor;
                s.ToolTip = Loc.T("preview.effect.grip.tip");
            }
            _gizmoLayer.Children.Add(grip);
            _gizmoLayer.Children.Add(hit);
        }
    }

    private static bool IsEffectHandle(GizmoHandle h) => h is GizmoHandle.EffectMove or GizmoHandle.EffectSize;

    private Point _effectFrom;      // the box's middle as it was taken
    private Vector _effectGrab;     // from its middle to where a grip was taken
    private double _effectStartScale;

    /// <summary>The box taken: where it was, and how big.</summary>
    private void StartEffectHandle()
    {
        var e = SelectedEntry();
        if (e?.Node?.Model.Effect is not { } def) return;
        _effectFrom = EffectCentre(e, def);
        _effectGrab = _dragStartMouse - _effectFrom;
        _effectStartScale = def.Scale ?? 1;
    }

    /// <summary>
    /// The box dragged to <paramref name="p"/>. Inside: its middle goes where the pointer went, the offset read back in
    /// the object's own units. A grip: the box grows or shrinks about its middle by how far the pointer is from it
    /// now, along the way it was taken, against how far it was - the grip stays under the pointer.
    /// </summary>
    private void DragEffectHandle(Point p)
    {
        var e = SelectedEntry();
        var effect = _selNode?.Effect;
        if (e == null || effect == null) return;
        if (_activeHandle == GizmoHandle.EffectMove)
        {
            var (x, y) = CanvasToObject(e.World, _effectFrom + (p - _dragStartMouse));
            effect.LocalX = x;
            effect.LocalY = y;
        }
        else
        {
            double grab = _effectGrab.Length;
            if (grab < 1) return;
            var along = _effectGrab / grab;
            var now = p - _effectFrom;
            double reach = now.X * along.X + now.Y * along.Y;
            effect.Scale = Math.Max(MinEffectScale, _effectStartScale * reach / grab);
        }
        DrawEffects();
        DrawGizmo();
    }
}
