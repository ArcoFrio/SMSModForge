using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// The game's interface over a place (after 1.10.0, the author: "The UI texture shown on all previews should match as
/// well, and I think we should do a proper render there instead of just a PNG slapped onto it"): 9_MainCanvas as the
/// game shows it while a place is on screen - the money, the day, the mood, the menu button and the navigator -
/// drawn by the renderer that draws the game's screens (<see cref="UiSceneRenderer"/>, held to the game by the
/// in-game checks F10-F12), at the game's 1920x1080, with the place's own navigator buttons in it as the plugin makes
/// them:
/// <list type="bullet">
/// <item>Each a copy of the game's Beach button (NavigatorRuntime.CreateButtonGameObject clones 14_beach): its own
/// words, its number on the disc (1, 2... 10, 11 - what the game shows; the keys only reach 0), the rule under its
/// words see-through until it is hovered (NavigatorRuntime.ConfigureHoverOverlay), and the game's own buttons off.</item>
/// <item>Six to a row, the last row where the single row is, the rows above it 95 apart, a second dark strip behind
/// two rows (NavigatorGridLayout, NavigatorGridSetup).</item>
/// <item>A button without words says where it goes, as in the game (NavigatorLabels).</item>
/// </list>
/// What is live in the game is drawn as the extraction found it: the money's and the day's words, the mood's face,
/// and the navigator's photo-XP bar (empty: the navigator white, as just after a level-up; it fills pink as photos
/// earn XP).
/// </summary>
public static class GameHud
{
    public const string Surface = "9_MainCanvas";
    private const int Columns = 6;
    private const float CellW = 125f, CellH = 75f, SpaceX = 15f, SpaceY = 20f;

    /// <summary>
    /// The interface at the canvas's size (premultiplied BGRA, rows from the top), the navigator's buttons saying
    /// <paramref name="labels"/> in their order; null without the game's extracted interface.
    /// </summary>
    public static byte[]? Render(IReadOnlyList<string> labels, out int width, out int height, UiRenderReport? report = null)
    {
        width = height = 0;
        if (!VanillaUiLibrary.IsAvailable) return null;
        var source = VanillaUiLibrary.Surface(Surface);
        if (source == null || !source.IsDrawable) return null;
        var surface = WithButtons(source, labels);
        if (surface == null) return null;
        width = (int)Math.Round(surface.Width);
        height = (int)Math.Round(surface.Height);
        return UiSceneRenderer.Render(surface, VanillaUiLibrary.Assets, report);
    }

    /// <summary>The canvas with the place's buttons in its navigator: a copy of what changes - the shared, loaded
    /// surface is never touched. Null when the canvas is not the one this knows.</summary>
    internal static VanillaUiSurface? WithButtons(VanillaUiSurface source, IReadOnlyList<string> labels)
    {
        var navigator = source.Root.Children.FirstOrDefault(c => c.Name == "Navigator");
        var map = navigator?.Children.FirstOrDefault(c => c.Name == "MapButtons");
        var strip = navigator?.Children.FirstOrDefault(c => c.Name == "Image");
        var proto = map?.Children.FirstOrDefault(c => c.Name == "14_beach");
        if (navigator == null || map?.Rect == null || strip?.Rect == null || proto?.Rect == null) return null;

        int n = labels.Count;
        int rows = (n + Columns - 1) / Columns;

        // The buttons: the game's off, the place's laid out as NavigatorGridLayout lays them (a cell's top-left
        // from MapButtons' top-left, y down).
        var newMap = Shallow(map);
        foreach (var c in map.Children)
        {
            var off = Shallow(c, keepChildren: true);
            off.ActiveSelf = false;
            newMap.Children.Add(off);
        }
        float mapW = map.Rect.SizeDelta[0], mapH = map.Rect.SizeDelta[1];
        int across = Math.Min(Columns, Math.Max(n, 1));
        float needX = across * CellW + (across - 1) * SpaceX, needY = rows * CellH + Math.Max(rows - 1, 0) * SpaceY;
        float startX = (mapW - needX) * 0.5f, startY = (mapH - needY) * 0.5f;
        if (rows > 1) startY -= (rows - 1.5f) * (CellH + SpaceY);
        int lastCount = n % Columns == 0 ? Columns : n % Columns;
        float lastStartX = (mapW - (lastCount * CellW + (lastCount - 1) * SpaceX)) * 0.5f;
        int at = map.Children.Count == 0 ? 0 : map.Children.Max(c => c.SiblingIndex) + 1;
        for (int i = 0; i < n; i++)
        {
            var b = Deep(proto);
            b.Name = "navbtn_" + i;
            b.ActiveSelf = true;
            b.SiblingIndex = at + i;
            float left = (i + 1 > n - lastCount ? lastStartX : startX) + (CellW + SpaceX) * (i % Columns);
            float top = startY + (CellH + SpaceY) * (i / Columns);
            b.Rect!.AnchorMin = new[] { 0f, 1f };
            b.Rect.AnchorMax = new[] { 0f, 1f };
            b.Rect.Pivot = new[] { 0.5f, 0.5f };
            b.Rect.AnchoredPosition = new[] { left + CellW * 0.5f, -(top + CellH * 0.5f) };
            b.Rect.SizeDelta = new[] { CellW, CellH };
            foreach (var child in b.Children)
            {
                if (child.Name == "Text (TMP)" && child.Text != null) child.Text.Value = labels[i];   // English on purpose: the game's object name, never shown
                // The rule under the words: the hover's, see-through at rest.
                else if (child.Name == "Image" && child.Image != null) child.Image.Color = See(child.Image.Color);
                // Its place in the row, counted on by the game as it shows it (10, 11 - not 0 and nothing, though only
                // the first ten have keys: InGameSuiteTests.S18).
                else if (child.Name == "keyboardnumber" && child.Text != null)
                    child.Text.Value = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            newMap.Children.Add(b);
        }

        // The navigator: its children in order, the second strip (a copy of the dark one, twice as tall, half as high
        // again) right after it while there are two rows.
        var newNavigator = Shallow(navigator);
        int index = 0;
        foreach (var c in navigator.Children.OrderBy(c => c.SiblingIndex))
        {
            var copy = ReferenceEquals(c, map) ? newMap : Shallow(c, keepChildren: true);
            copy.SiblingIndex = index++;
            newNavigator.Children.Add(copy);
            if (ReferenceEquals(c, strip) && rows > 1)
            {
                var extra = Shallow(strip, keepChildren: true);
                extra.Name = "ExtraNavRow";
                extra.SiblingIndex = index++;
                extra.Rect = Deep(strip).Rect;
                extra.Rect!.SizeDelta = new[] { strip.Rect.SizeDelta[0], strip.Rect.SizeDelta[1] * 2f };
                extra.Rect.AnchoredPosition = new[] { strip.Rect.AnchoredPosition[0], strip.Rect.AnchoredPosition[1] * 1.5f };
                newNavigator.Children.Add(extra);
            }
        }

        var root = Shallow(source.Root);
        foreach (var c in source.Root.Children)
            root.Children.Add(ReferenceEquals(c, navigator) ? newNavigator : Live(c));
        return new VanillaUiSurface
        {
            Path = source.Path, Scene = source.Scene, LiveAtLoad = source.LiveAtLoad, Canvas = source.Canvas, Root = root,
        };
    }

    /// <summary>
    /// What the game puts in the interface as it starts, which the extraction (taken as the scene loads) has empty:
    /// the money, the day, the mood's face. Read from the game (the Debug bridge's legacy-text, the test save,
    /// 2026-11-27): Game Creator's TextPropertyString, an old-style UI Text in Barton - the money's "$" and its amount
    /// at 42 from the top left, the day's name at 32 to the right - and the face of the mood the player is in
    /// (Motivated: pink). The same values as the picture this replaces.
    /// </summary>
    private static VanillaUiSurface.Node Live(VanillaUiSurface.Node node)
    {
        if (node.Name != "OtherHud") return node;
        static void Words(VanillaUiSurface.Node n, string words, int size, string alignment)
        {
            n.ActiveSelf = true;
            if (n.Text == null) return;
            n.Text.Value = words;
            n.Text.Font = "Barton";
            n.Text.FontSize = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            n.Text.Alignment = alignment;
        }
        node = Along(node, new[] { "Cash", "GameObject (1)" }, 0, n => Words(n, "$", 42, "UpperLeft"));
        node = Along(node, new[] { "Cash", "GameObject" }, 0, n => Words(n, "50.000.000,00", 42, "UpperLeft"));
        node = Along(node, new[] { "Day", "Cash (1)", "dayname" }, 0, n => Words(n, "Monday", 32, "MiddleRight"));
        node = Along(node, new[] { "Mood_Indicator", "Mood_List", "Motivated" }, 0, n => n.ActiveSelf = true);
        return node;
    }

    /// <summary>A copy of <paramref name="node"/> with the object at <paramref name="names"/> under it changed (a copy
    /// of it and of everything on the way; the rest shared).</summary>
    private static VanillaUiSurface.Node Along(VanillaUiSurface.Node node, IReadOnlyList<string> names, int at, Action<VanillaUiSurface.Node> change)
    {
        if (at == names.Count)
        {
            var leaf = Deep(node);
            change(leaf);
            return leaf;
        }
        var copy = Shallow(node);
        bool done = false;
        foreach (var c in node.Children)
        {
            if (!done && c.Name == names[at]) { copy.Children.Add(Along(c, names, at + 1, change)); done = true; }
            else copy.Children.Add(c);
        }
        return copy;
    }

    /// <summary>A colour, fully see-through.</summary>
    private static string See(string colour)
        => colour.Length == 9 ? colour.Substring(0, 7) + "00" : colour.Length == 7 ? colour + "00" : "#00000000";

    /// <summary>A node of its own, sharing what is under it (never changed here).</summary>
    private static VanillaUiSurface.Node Shallow(VanillaUiSurface.Node n, bool keepChildren = false) => new()
    {
        Name = n.Name, SiblingIndex = n.SiblingIndex, ActiveSelf = n.ActiveSelf, ActiveInHierarchy = n.ActiveInHierarchy,
        Components = n.Components, Rect = n.Rect, Image = n.Image, RawImage = n.RawImage, Text = n.Text, Shadow = n.Shadow,
        Outline = n.Outline, CanvasGroup = n.CanvasGroup, Mask = n.Mask, RectMask = n.RectMask, LayoutGroup = n.LayoutGroup,
        SizeFitter = n.SizeFitter, LayoutElement = n.LayoutElement, GridLayout = n.GridLayout, RestingTint = n.RestingTint,
        Children = keepChildren ? n.Children : new List<VanillaUiSurface.Node>(),
    };

    /// <summary>A node and everything under it, all of its own.</summary>
    private static VanillaUiSurface.Node Deep(VanillaUiSurface.Node n)
        => JsonConvert.DeserializeObject<VanillaUiSurface.Node>(JsonConvert.SerializeObject(n))!;
}
