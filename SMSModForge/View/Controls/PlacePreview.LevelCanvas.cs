using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// A level's own canvas over the place (after 1.10.0, the author: "I don't see the player room buttons in the preview
/// of the Bedroom for Androids at all. Why is that?" - see <see cref="LevelCanvas"/>): a copy of one of the game's
/// screen canvases (a pack place's <c>copyOf</c>, as the Androids Bedroom copies the player's room's buttons), or the
/// game's own in an extension of its level, drawn over the scene and under the game's interface as the game layers
/// them, with what the place says of its objects. Its objects' handles stand on them, moved in the canvas's own
/// pixels as the game reads their X and Y.
/// </summary>
public sealed partial class PlacePreview
{
    private readonly Canvas _levelCanvasLayer = new() { Width = CanvasWidth, Height = CanvasHeight, IsHitTestVisible = false };
    private static readonly Dictionary<string, BitmapSource> _levelCanvasPictures = new(StringComparer.Ordinal);
    private static readonly object LevelCanvasGate = new();

    /// <summary>The canvases drawn over the place, by the game's path, as last drawn - for tests.</summary>
    internal IReadOnlyList<string> LevelCanvasesDrawn => _levelCanvasesDrawn;
    private readonly List<string> _levelCanvasesDrawn = new();

    /// <summary>The layer the canvases are drawn on, and where it is among the picture's layers - for tests, to
    /// measure what it draws and that it is under the game's interface.</summary>
    internal Canvas LevelCanvasLayer => _levelCanvasLayer;
    internal bool LevelCanvasesUnderTheInterface => _canvas.Children.IndexOf(_levelCanvasLayer) < _canvas.Children.IndexOf(_overlayImage);

    /// <summary>Over the scene and its look, under the interface: called once, from the constructor.</summary>
    private void AddLevelCanvasLayer() => _canvas.Children.Add(_levelCanvasLayer);

    /// <summary>The game's canvas a row of the hierarchy is, or null: a copy of one, or the game's own bound in an
    /// extension of its level.</summary>
    private string? CanvasPathOf(GameObjectViewModel node)
    {
        string? path = node.IsCopy ? node.Model.CopyOf : node.Model.Bind ? Objects?.ScenePathOf(node) : null;
        return LevelCanvas.SurfaceAt(path) != null ? path : null;
    }

    /// <summary>
    /// Every object under a canvas, where the game puts it: a canvas drawn over the screen takes no notice of its own
    /// transform (it is the screen, 1920x1080 at the preview's own canvas pixels), and an object on it stands at its
    /// local position in canvas pixels from its parent's middle - the X and Y the place gives it when it moves it, the
    /// game's own otherwise. Its handles then stand on it, and a drag moves it in those pixels.
    /// </summary>
    private void PlaceOnLevelCanvases(List<SceneEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var root = entries[i];
            if (root.Node is not { } node || CanvasPathOf(node) is not { } path || LevelCanvas.SurfaceAt(path) is not { } surface) continue;
            // The screen: a canvas pixel a hundredth of a unit, its middle the camera's, y up.
            // In the level root's space, as every entry's world is (WorldToLevel): the screen's middle the camera's.
            var screen = WorldToLevel.Then(new Aff(0.01, 0, 0, 0.01, 0, 0));
            root.World = screen;
            // Its objects, down the tree: each in its parent's world.
            var worlds = new Dictionary<int, Aff> { [root.Depth] = screen };
            var names = new Dictionary<int, string> { [root.Depth] = "" };
            for (int j = i + 1; j < entries.Count && entries[j].Depth > root.Depth; j++)
            {
                var e = entries[j];
                if (e.Node is not { } child || !worlds.TryGetValue(e.Depth - 1, out var parentWorld)) continue;
                string rel = names[e.Depth - 1].Length == 0 ? child.Name : names[e.Depth - 1] + "/" + child.Name;
                names[e.Depth] = rel;
                Aff local;
                if (child.Model.Bind && !child.Model.OverrideTransform && LevelCanvas.LocalPosition(surface, rel) is { } game)
                    local = Aff.Trs((float)game.X, (float)game.Y, 0, 0, 0, 1, 1);
                else local = NodeAff(child);
                e.ParentWorld = parentWorld;
                e.World = parentWorld.Then(local);
                worlds[e.Depth] = e.World;
            }
        }
    }

    /// <summary>The canvases over the place, drawn: each as the game draws it on arrival, faint where it does not
    /// start on (as an object that does not), in the game's order of its canvases.</summary>
    private void DrawLevelCanvases(List<SceneEntry> entries)
    {
        _levelCanvasLayer.Children.Clear();
        _levelCanvasesDrawn.Clear();
        var drawn = new List<(int Order, int At, Image Picture)>();
        foreach (var e in entries)
        {
            if (e.Hidden || e.Node is not { } node || CanvasPathOf(node) is not { } path || LevelCanvas.SurfaceAt(path) is not { } surface) continue;
            bool on = node.IsCopy || node.Model.OverrideActive ? node.StartActive : surface.Root.ActiveSelf;
            if ((!on || e.ParentInactive) && AsTheGameShows) continue;
            double ghost = e.ParentInactive && DimUnderInactiveParent ? 0.15 : !on && DimInactive ? 0.4 : 1.0;
            var picture = Picture(path, surface, LevelCanvas.MovesOf(node.Model));
            if (picture == null) continue;
            var image = new Image
            {
                Source = picture, Width = CanvasWidth, Height = CanvasHeight, Stretch = Stretch.Fill,
                Opacity = ghost, IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(image, 0);
            Canvas.SetTop(image, 0);
            drawn.Add((surface.Canvas.SortingOrder, drawn.Count, image));
            _levelCanvasesDrawn.Add(path);
        }
        foreach (var (_, _, image) in drawn.OrderBy(d => d.Order).ThenBy(d => d.At)) _levelCanvasLayer.Children.Add(image);
    }

    /// <summary>A canvas with the place's moves, kept by what was asked (the same is drawn once).</summary>
    private static BitmapSource? Picture(string path, Model.VanillaUiSurface surface, List<LevelCanvas.Move> moves)
    {
        string key = path + "\u0001" + string.Join("\u0001", moves.Select(m =>
            m.Path + ":" + (m.At is { } a ? a[0] + "," + a[1] : "") + ":" + (m.Scale is { } s ? s[0] + "," + s[1] : "") + ":" + m.Turn + ":" + m.Active));
        lock (LevelCanvasGate)
            if (_levelCanvasPictures.TryGetValue(key, out var kept)) return kept;
        var pixels = LevelCanvas.Render(surface, moves, out int w, out int h);
        if (pixels == null || w <= 0 || h <= 0) return null;
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        bitmap.Freeze();
        lock (LevelCanvasGate)
        {
            // A few at a time: a button dragged is drawn again at each step.
            if (_levelCanvasPictures.Count >= 32) _levelCanvasPictures.Clear();
            _levelCanvasPictures[key] = bitmap;
        }
        return bitmap;
    }
}
