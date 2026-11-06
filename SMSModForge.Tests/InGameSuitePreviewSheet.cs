using System.Collections.Generic;
using SMSModForge.Model;

namespace SMSModForge.Tests;

/// <summary>
/// One screen with every setting of a UI object on it, for comparing the
/// editor's preview with what the game draws: cells on a plain background, so a
/// difference can be pinned on one setting (the author, 2026-11-06: "we need to
/// guarantee that the UI in the preview looks exactly like in game").
/// <para/>
/// Each cell is an object of its own, named for what it tests, so a check reads
/// its rectangle and compares its pixels on their own.
/// </summary>
internal static class InGameSuitePreviewSheet
{
    public const string Id = "previewsheet";

    /// <summary>The canvas the sheet is laid out on, and what the game is set
    /// to while it is measured: one canvas pixel to one screen pixel.</summary>
    public const int CanvasWidth = 1920, CanvasHeight = 1080;

    private const float CellWidth = 370f, CellHeight = 255f;
    private const int Columns = 5;

    private const string Panel = "Semi Rounded", Round = "Rounded", Icon = "Close";
    private const string Body = "Curse Casual SDF", Title = "Alata-Regular-Outline 120 SDF", Other = "Barton SDF";
    private const string Words = "The quick brown fox jumps over the lazy dog and keeps on going past it.";

    /// <summary>The screen, as an author would have made it.</summary>
    public static UiDef Build()
    {
        var sheet = new UiNodeDef
        {
            Name = "Sheet",
            Rect = Stretch(),
            Image = new UiImageDef { Sprite = Round, Type = "Sliced", Tint = "#6A7078FF" },
        };

        int at = 0;
        void Cell(string name, params UiNodeDef[] inside)
        {
            int column = at % Columns, row = at / Columns;
            at++;
            var cell = new UiNodeDef
            {
                Name = name,
                Rect = new UiRectDef
                {
                    AnchorMin = new[] { 0f, 1f }, AnchorMax = new[] { 0f, 1f }, Pivot = new[] { 0f, 1f },
                    Position = new[] { 10f + column * (CellWidth + 11f), -10f - row * (CellHeight + 10f) },
                    Size = new[] { CellWidth, CellHeight },
                },
                Image = new UiImageDef { Sprite = Round, Type = "Sliced", Tint = "#C8D0D8FF" },
            };
            cell.Children.AddRange(inside);
            sheet.Children.Add(cell);
        }

        Cell("Anchors",
             Square("TL", 0f, 1f, 0f, 1f), Square("T", 0.5f, 1f, 0.5f, 1f), Square("TR", 1f, 1f, 1f, 1f),
             Square("L", 0f, 0.5f, 0f, 0.5f), Square("C", 0.5f, 0.5f, 0.5f, 0.5f), Square("R", 1f, 0.5f, 1f, 0.5f),
             Square("BL", 0f, 0f, 0f, 0f), Square("B", 0.5f, 0f, 0.5f, 0f), Square("BR", 1f, 0f, 1f, 0f));

        Cell("Stretched",
             new UiNodeDef { Name = "Across", Image = Solid("#3060C0FF"),
                             Rect = new UiRectDef { AnchorMin = new[] { 0f, 1f }, AnchorMax = new[] { 1f, 1f }, Pivot = new[] { 0.5f, 1f },
                                                    Position = new[] { 0f, -10f }, Size = new[] { -20f, 30f } } },
             new UiNodeDef { Name = "Down", Image = Solid("#C06030FF"),
                             Rect = new UiRectDef { AnchorMin = new[] { 0f, 0f }, AnchorMax = new[] { 0f, 1f }, Pivot = new[] { 0f, 0.5f },
                                                    Position = new[] { 10f, -20f }, Size = new[] { 30f, -60f } } },
             new UiNodeDef { Name = "Part", Image = Solid("#30A060FF"),
                             Rect = new UiRectDef { AnchorMin = new[] { 0.3f, 0.2f }, AnchorMax = new[] { 0.9f, 0.7f }, Pivot = new[] { 0.5f, 0.5f },
                                                    Position = new[] { 5f, -5f }, Size = new[] { 10f, -10f } } });

        Cell("Pivots",
             Placed("LowLow", 60f, 60f, 50f, 40f, 0f, 0f, "#3060C0FF"),
             Placed("HighHigh", 180f, 180f, 50f, 40f, 1f, 1f, "#C06030FF"),
             Placed("Mid", 280f, 120f, 50f, 40f, 0.5f, 0.5f, "#30A060FF"));

        Cell("Scaled",
             // Not only where things land: words, corners and a shadow under a
             // scaled parent. The game's own calendar found that one.
             new UiNodeDef
             {
                 Name = "Half", Rect = Box(95f, 128f, 170f, 230f, scale: new[] { 0.7f, 0.7f }),
                 Image = new UiImageDef { Sprite = Panel, Type = "Sliced", Tint = "#3060C0FF" },
                 Shadow = new UiEffectDef { Color = "#000000A0", Distance = new[] { 10f, -10f } },
                 Children =
                 {
                     new UiNodeDef { Name = "Inside", Rect = Box(85f, 175f, 130f, 80f), Image = new UiImageDef { Sprite = Panel, Type = "Sliced", Tint = "#FFE080FF" } },
                     new UiNodeDef { Name = "Words", Rect = Box(85f, 60f, 150f, 90f),
                                     Text = new UiTextDef { Value = "Words two lines", Font = Body, Size = 36f, Color = "#FFFFFFFF", Alignment = "Center" } },
                 },
             },
             new UiNodeDef
             {
                 Name = "Wide", Rect = Box(275f, 128f, 80f, 200f, scale: new[] { 1.5f, 0.5f }),
                 Image = new UiImageDef { Sprite = Panel, Type = "Sliced", Tint = "#C06030FF" },
                 Children =
                 {
                     new UiNodeDef { Name = "Words", Rect = Box(40f, 100f, 70f, 80f),
                                     Text = new UiTextDef { Value = "Wide", Font = Body, Size = 30f, Color = "#FFFFFFFF", Alignment = "Center" } },
                 },
             });

        Cell("Turned",
             new UiNodeDef
             {
                 Name = "Thirty", Rect = Box(110f, 130f, 120f, 60f, turn: 30f), Image = Solid("#3060C0FF"),
                 Children =
                 {
                     new UiNodeDef { Name = "Inside", Rect = Box(30f, 0f, 40f, 30f), Image = Solid("#FFE080FF") },
                     new UiNodeDef { Name = "Level", Rect = Box(90f, 60f, 50f, 20f), Image = Solid("#30A060FF"), Upright = true },
                 },
             },
             new UiNodeDef
             {
                 Name = "Words", Rect = Box(280f, 130f, 130f, 50f, turn: 270f),
                 Text = new UiTextDef { Value = "Upwards", Font = Body, Size = 30f, Color = "#102030FF", Alignment = "Center" },
             });

        Cell("Pictures",
             new UiNodeDef { Name = "Simple", Rect = Box(90f, 180f, 150f, 90f), Image = new UiImageDef { Sprite = Panel, Type = "Simple", Tint = "#FFFFFFFF" } },
             new UiNodeDef { Name = "Sliced", Rect = Box(270f, 180f, 150f, 90f), Image = new UiImageDef { Sprite = Panel, Type = "Sliced", Tint = "#3060C0FF" } },
             new UiNodeDef { Name = "Halved", Rect = Box(90f, 70f, 150f, 90f), Image = new UiImageDef { Sprite = Panel, Type = "Sliced", Tint = "#30A060FF", PixelsPerUnitMultiplier = 2f } },
             new UiNodeDef { Name = "Hollow", Rect = Box(270f, 70f, 150f, 90f), Image = new UiImageDef { Sprite = Panel, Type = "Sliced", Tint = "#C06030FF", FillCenter = false } });

        Cell("Tiled",
             new UiNodeDef { Name = "Icons", Rect = Box(110f, 128f, 190f, 210f), Image = new UiImageDef { Sprite = Icon, Type = "Tiled", Tint = "#203040FF" } },
             new UiNodeDef { Name = "Filled", Rect = Box(285f, 128f, 130f, 130f), Image = new UiImageDef { Sprite = Icon, Type = "Filled", Tint = "#203040FF" } });

        Cell("Shape",
             new UiNodeDef { Name = "Wide", Rect = Box(95f, 128f, 170f, 60f), Image = new UiImageDef { Sprite = Icon, Tint = "#203040FF", PreserveAspect = true } },
             new UiNodeDef { Name = "Tall", Rect = Box(225f, 128f, 40f, 200f), Image = new UiImageDef { Sprite = Icon, Tint = "#203040FF", PreserveAspect = true } },
             new UiNodeDef { Name = "Pivoted", Rect = Box(310f, 128f, 60f, 200f, pivot: new[] { 0f, 0f }), Image = new UiImageDef { Sprite = Icon, Tint = "#203040FF", PreserveAspect = true } });

        Cell("Opacity",
             new UiNodeDef { Name = "Behind", Rect = Box(185f, 128f, 330f, 60f), Image = Solid("#102030FF") },
             new UiNodeDef { Name = "Tinted", Rect = Box(80f, 128f, 100f, 180f), Image = Solid("#FF404080") },
             new UiNodeDef
             {
                 Name = "Group", Rect = Box(185f, 128f, 100f, 180f), Alpha = 0.5f, Image = Solid("#40FF40FF"),
                 Children = { new UiNodeDef { Name = "Inside", Rect = Box(0f, 0f, 50f, 50f), Image = Solid("#FFFFFFFF") } },
             },
             new UiNodeDef { Name = "Faint", Rect = Box(290f, 128f, 100f, 180f), Alpha = 0.25f, Image = Solid("#FFFFFFFF") });

        Cell("Effects",
             new UiNodeDef { Name = "Shadow", Rect = Box(95f, 180f, 130f, 70f), Image = Solid("#E0E8F0FF"),
                             Shadow = new UiEffectDef { Color = "#000000A0", Distance = new[] { 8f, -8f } } },
             new UiNodeDef { Name = "Outline", Rect = Box(275f, 180f, 130f, 70f), Image = Solid("#E0E8F0FF"),
                             Outline = new UiEffectDef { Color = "#C0208080", Distance = new[] { 6f, -6f } } },
             new UiNodeDef { Name = "ShadowWords", Rect = Box(95f, 70f, 160f, 80f), Shadow = new UiEffectDef { Color = "#FF2020FF", Distance = new[] { 4f, -4f } },
                             Text = new UiTextDef { Value = "Shadow", Font = Body, Size = 40f, Color = "#102030FF", Alignment = "Center" } },
             new UiNodeDef { Name = "OutlineWords", Rect = Box(275f, 70f, 160f, 80f), Outline = new UiEffectDef { Color = "#FF2020FF", Distance = new[] { 3f, -3f } },
                             Text = new UiTextDef { Value = "Outline", Font = Body, Size = 40f, Color = "#102030FF", Alignment = "Center" } });

        Cell("Aligned",
             Aligned("TopLeft", 0), Aligned("Top", 1), Aligned("TopRight", 2),
             Aligned("Left", 3), Aligned("Center", 4), Aligned("Right", 5),
             Aligned("BottomLeft", 6), Aligned("Bottom", 7), Aligned("BottomRight", 8));

        Cell("Wrapping",
             new UiNodeDef { Name = "Wraps", Rect = Box(185f, 185f, 340f, 110f),
                             Text = new UiTextDef { Value = Words, Font = Body, Size = 30f, Color = "#102030FF", Alignment = "TopLeft" } },
             new UiNodeDef { Name = "OneLine", Rect = Box(185f, 90f, 340f, 40f),
                             Text = new UiTextDef { Value = Words, Font = Body, Size = 30f, Color = "#102030FF", Alignment = "Left", Wrap = false } },
             new UiNodeDef { Name = "Centred", Rect = Box(185f, 45f, 340f, 40f),
                             Text = new UiTextDef { Value = "Centred without wrapping", Font = Body, Size = 30f, Color = "#102030FF", Alignment = "Center", Wrap = false } });

        Cell("Shrinking",
             new UiNodeDef { Name = "Fits", Rect = Box(185f, 195f, 340f, 90f),
                             Text = new UiTextDef { Value = "Few words", Font = Body, Size = 30f, Color = "#102030FF", Alignment = "Center", AutoSize = true, AutoSizeMin = 12f, AutoSizeMax = 60f } },
             new UiNodeDef { Name = "Squeezed", Rect = Box(185f, 75f, 340f, 120f),
                             Text = new UiTextDef { Value = Words + " " + Words, Font = Body, Size = 30f, Color = "#102030FF", Alignment = "Center", AutoSize = true, AutoSizeMin = 12f, AutoSizeMax = 60f } });

        Cell("Spacing",
             new UiNodeDef { Name = "Lines", Rect = Box(185f, 185f, 340f, 110f),
                             Text = new UiTextDef { Value = "First line\nSecond line", Font = Body, Size = 30f, Color = "#102030FF", Alignment = "Center", LineSpacing = 40f } },
             new UiNodeDef { Name = "Letters", Rect = Box(185f, 70f, 340f, 60f),
                             Text = new UiTextDef { Value = "Spaced out", Font = Body, Size = 30f, Color = "#102030FF", Alignment = "Center", CharacterSpacing = 20f } });

        Cell("Fonts",
             new UiNodeDef { Name = "One", Rect = Box(185f, 200f, 340f, 70f),
                             Text = new UiTextDef { Value = "Alata title", Font = Title, Size = 56f, Color = "#000E20FF", Alignment = "Center" } },
             new UiNodeDef { Name = "Two", Rect = Box(185f, 125f, 340f, 70f),
                             Text = new UiTextDef { Value = "Barton words", Font = Other, Size = 44f, Color = "#102030FF", Alignment = "Center" } },
             new UiNodeDef { Name = "Three", Rect = Box(185f, 50f, 340f, 70f),
                             Text = new UiTextDef { Value = "Outlined 123", Font = "Curse Casual SDF Outline", Size = 44f, Color = "#FFFFFFFF", Alignment = "Center" } });

        Cell("Clipped",
             new UiNodeDef
             {
                 Name = "Clips", Rect = Box(100f, 128f, 150f, 150f), ClipChildren = true, Image = Solid("#3060C0FF"),
                 Children = { new UiNodeDef { Name = "Over", Rect = Box(50f, 50f, 120f, 120f), Image = Solid("#FFE080FF") } },
             },
             new UiNodeDef
             {
                 Name = "Free", Rect = Box(280f, 128f, 100f, 100f), Image = Solid("#C06030FF"),
                 Children = { new UiNodeDef { Name = "Over", Rect = Box(30f, 30f, 80f, 80f), Image = Solid("#FFE080FF") } },
             });

        Cell("Row", Arranged("Across", UiLayoutKinds.All[0], 5));
        Cell("Column", Arranged("Down", UiLayoutKinds.All[1], 3));

        var grid = Arranged("Grid", UiLayoutKinds.Grid, 7);
        grid.Layout!.CellSize = new[] { 60f, 40f };
        grid.Layout.Spacing = new[] { 8f, 6f };
        grid.Layout.Constraint = "FixedColumnCount";
        grid.Layout.ConstraintCount = 3;
        Cell("Grid", grid);

        var centred = Arranged("Centred", UiLayoutKinds.Grid, 7);
        centred.Layout!.CellSize = new[] { 60f, 40f };
        centred.Layout.Spacing = new[] { 8f, 6f };
        centred.Layout.Constraint = "FixedColumnCount";
        centred.Layout.ConstraintCount = 3;
        centred.Layout.CenterLastLine = true;
        centred.Layout.FitToContent = true;
        Cell("Fitted", centred);

        return new UiDef
        {
            Id = Id, Name = Id, HidesWithGameplayUi = false, SortingOrder = 90,
            Nodes = { sheet },
        };
    }

    // ── Building blocks ──────────────────────────────────────────────────

    private static UiRectDef Stretch() => new()
    {
        AnchorMin = new[] { 0f, 0f }, AnchorMax = new[] { 1f, 1f }, Pivot = new[] { 0.5f, 0.5f },
        Position = new[] { 0f, 0f }, Size = new[] { 0f, 0f },
    };

    /// <summary>Middle at (x, y) from the parent's bottom left.</summary>
    private static UiRectDef Box(float x, float y, float w, float h,
                                 float[]? scale = null, float turn = 0f, float[]? pivot = null)
    {
        var p = pivot ?? new[] { 0.5f, 0.5f };
        return new UiRectDef
        {
            AnchorMin = new[] { 0f, 0f }, AnchorMax = new[] { 0f, 0f }, Pivot = p,
            Position = new[] { x + (p[0] - 0.5f) * w, y + (p[1] - 0.5f) * h }, Size = new[] { w, h },
            Scale = scale ?? new[] { 1f, 1f }, RotationZ = turn,
        };
    }

    private static UiImageDef Solid(string tint) => new() { Sprite = Round, Type = "Sliced", Tint = tint };

    private static UiNodeDef Square(string name, float ax, float ay, float px, float py) => new()
    {
        Name = name,
        Rect = new UiRectDef
        {
            AnchorMin = new[] { ax, ay }, AnchorMax = new[] { ax, ay }, Pivot = new[] { px, py },
            Position = new[] { 0f, 0f }, Size = new[] { 40f, 40f },
        },
        Image = Solid("#3060C0FF"),
    };

    private static UiNodeDef Placed(string name, float x, float y, float w, float h, float px, float py, string tint) => new()
    {
        Name = name,
        Rect = new UiRectDef
        {
            AnchorMin = new[] { 0f, 0f }, AnchorMax = new[] { 0f, 0f }, Pivot = new[] { px, py },
            Position = new[] { x, y }, Size = new[] { w, h },
        },
        Image = Solid(tint),
    };

    private static UiNodeDef Aligned(string alignment, int index)
    {
        float x = 62f + index % 3 * 123f, y = 213f - index / 3 * 85f;
        return new UiNodeDef
        {
            Name = alignment, Rect = Box(x, y, 118f, 80f), Image = Solid("#FFFFFFFF"),
            Children =
            {
                new UiNodeDef
                {
                    Name = "Words", Rect = Stretch(),
                    Text = new UiTextDef { Value = "Words", Font = Body, Size = 22f, Color = "#102030FF", Alignment = alignment },
                },
            },
        };
    }

    private static UiNodeDef Arranged(string name, string kind, int count)
    {
        var parent = new UiNodeDef
        {
            Name = name, Rect = Box(185f, 128f, 340f, 225f), Image = Solid("#FFFFFFFF"),
            Layout = new UiLayoutDef { Kind = kind, Spacing = new[] { 10f, 10f }, Padding = new[] { 8f, 8f, 8f, 8f }, Alignment = "UpperLeft" },
        };
        for (int i = 1; i <= count; i++)
            parent.Children.Add(new UiNodeDef
            {
                Name = "Item" + i,
                Rect = new UiRectDef { Size = new[] { 40f + i * 6f, 30f + i * 3f } },
                Image = Solid(i % 2 == 0 ? "#3060C0FF" : "#C06030FF"),
            });
        return parent;
    }
}
