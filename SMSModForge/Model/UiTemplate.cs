using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Starting shapes for a UI of the pack's own.
/// <para/>
/// Every sprite, size, font and colour here is taken from the game's own
/// screens rather than invented, because a window that is nearly the game's is
/// worse than one that plainly is not: a 44px title in the wrong blue reads as
/// a bug in the pack. The numbers come from the extraction — the window body is
/// the shape Quitagme uses, the buttons are the ones the whole game uses, and
/// the list rows are the ones the finances tooltip uses.
/// <para/>
/// What is NOT copied is the arrangement. Vanilla screens position their pieces
/// with absolute offsets inside a fixed-size parent, which stops being right the
/// moment an author resizes anything. Templates anchor instead — a title to the
/// top edge, a close button to the top-right corner, buttons to the bottom — so
/// the shape survives being made bigger, which is the first thing anyone does.
/// <para/>
/// Nothing here uses a layout group. The game does, in places, but neither the
/// preview nor the runtime implements one, and a template that quietly relies
/// on something that does not exist yet would draw correctly nowhere.
/// </summary>
public sealed class UiTemplate
{
    public string Key { get; }
    public string Name { get; }

    /// <summary>What it is and when to reach for it, in one line.</summary>
    public string Summary { get; }

    /// <summary>Whether this is a whole screen to start a UI from, as opposed
    /// to a piece to drop inside one.</summary>
    public bool IsScreen { get; }

    private readonly Func<UiNodeDef> _build;

    private UiTemplate(string key, string name, string summary, bool isScreen,
                       Func<UiNodeDef> build)
    {
        Key = key;
        Name = name;
        Summary = summary;
        IsScreen = isScreen;
        _build = build;
    }

    /// <summary>A fresh tree. Never a shared one: two UIs made from the same
    /// template are two separate things, and handing out the same objects would
    /// make editing one edit the other.</summary>
    public UiNodeDef Build() => _build();

    public override string ToString() => Name;

    public static UiTemplate? Find(string? key)
        => string.IsNullOrEmpty(key)
         ? null
         : All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<UiTemplate> Screens => All.Where(t => t.IsScreen);
    public static IEnumerable<UiTemplate> Pieces => All.Where(t => !t.IsScreen);

    // ── The game's own values ────────────────────────────────────────
    //
    // Named rather than repeated, so that a sprite the game renames in a patch
    // is one edit here instead of a hunt through six trees.

    private const string PanelSprite = "Semi Rounded";
    private const string WindowSprite = "Rounded";
    private const string ButtonSprite = "Button_Rectangle_05_Deco_White_Bg";
    private const string CloseIcon = "Close";

    private const string TitleFont = "Alata-Regular-Outline 120 SDF";
    private const string BodyFont = "Curse Casual SDF";
    private const string TitleColour = "#000E20FF";
    private const string BodyColour = "#000000FF";

    /// <summary>The outline the game puts on its window bodies.</summary>
    private static UiEffectDef WindowOutline()
        => new() { Color = "#00000070", Distance = new[] { 3f, -3f } };

    public static readonly IReadOnlyList<UiTemplate> All = new[]
    {
        new UiTemplate("panel", "Blank panel",
            "One panel and nothing else. For when the shape is yours to decide.",
            true, BlankPanel),

        new UiTemplate("window", "Window",
            "A titled window with a close button, the shape the game's own menus use.",
            true, Window),

        new UiTemplate("dialog", "Dialog",
            "A window that asks something, with a message and two buttons.",
            true, Dialog),

        new UiTemplate("list", "List panel",
            "A titled panel with a column of rows, like the game's tooltips.",
            true, ListPanel),

        new UiTemplate("tooltip", "Tooltip",
            "A small panel with a heading and a line or two of text.",
            true, Tooltip),

        new UiTemplate("button", "Button",
            "One button with a label on it.",
            false, () => Button("Button", "Button", 0, 0)),

        new UiTemplate("row", "List row",
            "One row of a list, the grey bar the game's tooltips are made of.",
            false, () => Row("Row", "Row", 0)),

        new UiTemplate("label", "Label",
            "A line of text on its own.",
            false, () => Text("Label", "Label", 400, 60, 0, 0, BodyFont, 40, BodyColour)),
    };

    // ── The screens ──────────────────────────────────────────────────

    private static UiNodeDef BlankPanel()
        => Panel("Panel", PanelSprite, 600, 400);

    private static UiNodeDef Window()
    {
        var window = Panel("Window", WindowSprite, 650, 600);
        window.Outline = WindowOutline();
        window.Children.Add(Title("Title", "Title"));
        window.Children.Add(CloseButton());
        return window;
    }

    private static UiNodeDef Dialog()
    {
        var window = Window();
        window.Name = "Dialog";

        var message = Text("Message", "Are you sure?", 500, 160, 0, 20, BodyFont, 44, BodyColour);
        window.Children.Add(message);

        // Side by side and clear of each other: 150 wide at 95 either side of
        // the middle leaves 40 between them, which is what the game leaves.
        window.Children.Add(Bottom(Button("Confirm", "Yes", 95, 60)));
        window.Children.Add(Bottom(Button("Cancel", "No", -95, 60)));
        return window;
    }

    private static UiNodeDef ListPanel()
    {
        var panel = Panel("List", PanelSprite, 400, 620);
        panel.Children.Add(Title("Title", "List"));

        // 77 apart, which is the spacing the game's own list rows use.
        for (int i = 0; i < 6; i++)
            panel.Children.Add(Row($"Row {i + 1}", $"Row {i + 1}", -(120 + i * 77)));

        return panel;
    }

    private static UiNodeDef Tooltip()
    {
        var panel = Panel("Tooltip", PanelSprite, 340, 220);
        panel.Children.Add(Title("Heading", "Heading", 300, 40));

        var body = Text("Body", "What this is for.", 300, 100, 0, -25, BodyFont, 30, BodyColour);
        panel.Children.Add(body);
        return panel;
    }

    // ── The pieces ───────────────────────────────────────────────────

    /// <summary>A button, with its label stretched to fill it — so the text
    /// stays centred when the button is resized, which is how the game does it
    /// on all ten of its own.</summary>
    private static UiNodeDef Button(string name, string caption, float x, float y)
    {
        var button = new UiNodeDef
        {
            Name = name,
            Rect = Rect(150, 75, x, y),
            Image = Sprite(ButtonSprite),
        };

        var label = new UiNodeDef
        {
            Name = "Label",
            Rect = new UiRectDef
            {
                AnchorMin = new[] { 0f, 0f },
                AnchorMax = new[] { 1f, 1f },
                Pivot = new[] { 0.5f, 0.5f },
                Position = new[] { 0f, 0f },
                Size = new[] { 0f, 0f },       // an inset of nothing: exactly the button
            },
            Text = new UiTextDef
            {
                Value = caption,
                Font = BodyFont,
                Size = 26,
                Color = BodyColour,
                Alignment = "Center",
            },
        };

        button.Children.Add(label);
        return button;
    }

    private static UiNodeDef Row(string name, string caption, float y)
    {
        var row = new UiNodeDef
        {
            Name = name,
            Rect = Top(Rect(310, 45, 0, y)),
            Image = Sprite(ButtonSprite),
        };
        row.Image!.Tint = "#C5C5C5FF";

        // The game halves the nine-slice on these, which is what keeps a 45px
        // bar from being all corner and no middle.
        row.Image.PixelsPerUnitMultiplier = 2f;

        row.Children.Add(new UiNodeDef
        {
            Name = "Label",
            Rect = new UiRectDef
            {
                AnchorMin = new[] { 0f, 0f },
                AnchorMax = new[] { 1f, 1f },
                Pivot = new[] { 0.5f, 0.5f },
                Position = new[] { 0f, 0f },
                Size = new[] { 0f, 0f },
            },
            Text = new UiTextDef
            {
                Value = caption,
                Font = BodyFont,
                Size = 26,
                Color = BodyColour,
                Alignment = "Center",
            },
        });
        return row;
    }

    private static UiNodeDef CloseButton()
    {
        var button = new UiNodeDef
        {
            Name = "Close",
            Rect = new UiRectDef
            {
                // Pinned to the corner, so it stays in the corner when the
                // window is resized rather than drifting into the middle.
                AnchorMin = new[] { 1f, 1f },
                AnchorMax = new[] { 1f, 1f },
                Pivot = new[] { 1f, 1f },
                // Far enough in to clear the panel's rounded corner - at 20 it
                // sat on the curve and hung over the edge.
                Position = new[] { -40f, -40f },
                Size = new[] { 85f, 85f },
            },
            Image = Sprite(WindowSprite),
        };

        // Tinted, and deliberately not the way the game does it. Every close
        // button in the game is a white 'Rounded' carrying a white 'Close' -
        // both sprites are pure white art - so it is legible only through the
        // dark outline on the button, and only where the window behind it is
        // not also white. That is a poor thing to hand someone as a starting
        // point, so the button takes the navy the game titles its windows in
        // and the white cross reads against it.
        button.Image!.Tint = TitleColour;

        button.Children.Add(new UiNodeDef
        {
            Name = "Icon",
            Rect = Rect(55, 55, 0, 0),
            Image = new UiImageDef { Sprite = CloseIcon, Type = "Simple", Tint = "#FFFFFFFF" },
        });
        return button;
    }

    // ── Building blocks ──────────────────────────────────────────────

    private static UiNodeDef Panel(string name, string sprite, float w, float h)
        => new()
        {
            Name = name,
            Rect = Rect(w, h, 0, 0),
            Image = Sprite(sprite),
        };

    /// <summary>A heading pinned to the top edge. Sized to the panel it goes
    /// on rather than always 72pt: the game's window titles are 72, and 72 in a
    /// 340-wide tooltip is wider than the tooltip.</summary>
    private static UiNodeDef Title(string name, string caption,
                                   float width = 400, float size = 72)
        => Top(Text(name, caption, width, size + 8, 0, -30, TitleFont, size, TitleColour));

    private static UiNodeDef Text(string name, string value, float w, float h,
                                  float x, float y, string font, float size, string colour)
        => new()
        {
            Name = name,
            Rect = Rect(w, h, x, y),
            Text = new UiTextDef
            {
                Value = value,
                Font = font,
                Size = size,
                Color = colour,
                Alignment = "Center",
                Wrap = true,
            },
        };

    private static UiImageDef Sprite(string sprite)
        => new() { Sprite = sprite, Type = "Sliced", Tint = "#FFFFFFFF" };

    /// <summary>Centred in its parent, which is what an unanchored object
    /// wants to be — the parent's middle is the only point that is still there
    /// after a resize.</summary>
    private static UiRectDef Rect(float w, float h, float x, float y)
        => new()
        {
            AnchorMin = new[] { 0.5f, 0.5f },
            AnchorMax = new[] { 0.5f, 0.5f },
            Pivot = new[] { 0.5f, 0.5f },
            Position = new[] { x, y },
            Size = new[] { w, h },
        };

    /// <summary>Pin to the top edge, keeping the position it was given as an
    /// offset from there.</summary>
    private static UiRectDef Top(UiRectDef rect)
    {
        rect.AnchorMin = new[] { 0.5f, 1f };
        rect.AnchorMax = new[] { 0.5f, 1f };
        rect.Pivot = new[] { 0.5f, 1f };
        return rect;
    }

    private static UiNodeDef Top(UiNodeDef node)
    {
        Top(node.Rect);
        return node;
    }

    private static UiNodeDef Bottom(UiNodeDef node)
    {
        node.Rect.AnchorMin = new[] { 0.5f, 0f };
        node.Rect.AnchorMax = new[] { 0.5f, 0f };
        node.Rect.Pivot = new[] { 0.5f, 0f };
        return node;
    }
}
