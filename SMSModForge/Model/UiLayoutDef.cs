using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// An object that arranges its own children, instead of each child sitting
/// where it was put.
/// <para/>
/// The game leans on this heavily - 322 horizontal or vertical groups and 58
/// grids across its screens - and for good reason: a list whose items appear
/// and disappear has to close ranks, and no set of fixed positions can do that.
/// A shop where a sold item leaves a hole is the shape of the problem.
/// <para/>
/// One type with a <see cref="Kind"/> rather than three, because from an
/// author's side it is one decision - "how should this arrange its children" -
/// and the fields that do not apply are simply not shown.
/// <para/>
/// The names and meanings are Unity's own, so what is stored here means the
/// same thing it means in the game and the runtime can hand them straight to a
/// real LayoutGroup.
/// </summary>
public sealed class UiLayoutDef
{
    /// <summary>Horizontal, Vertical or Grid.</summary>
    [JsonProperty("kind", Order = 1)]
    public string Kind { get; set; } = UiLayoutKinds.Horizontal;

    /// <summary>Gap between children. Both numbers are used by a grid; a
    /// horizontal or vertical group uses the first.</summary>
    [JsonProperty("spacing", Order = 2)]
    public float[] Spacing { get; set; } = { 0f, 0f };

    /// <summary>Inset from the edges: left, right, top, bottom.</summary>
    [JsonProperty("padding", Order = 3)]
    public float[] Padding { get; set; } = { 0f, 0f, 0f, 0f };

    /// <summary>Where the arranged block sits when it does not fill the space -
    /// Unity's TextAnchor names, e.g. "MiddleCenter". The game's own groups are
    /// MiddleCenter 255 times out of 322.</summary>
    [JsonProperty("alignment", Order = 4)]
    public string Alignment { get; set; } = "MiddleCenter";

    // ── Horizontal and vertical ──────────────────────────────────────

    /// <summary>Whether the group sets its children's width rather than
    /// leaving each at its own.</summary>
    [JsonProperty("controlWidth", Order = 5)]
    public bool ControlWidth { get; set; }

    [JsonProperty("controlHeight", Order = 6)]
    public bool ControlHeight { get; set; }

    /// <summary>Whether children share out the leftover space. With control on
    /// and nothing else declaring a size, this is what makes a row of items
    /// divide the width evenly.</summary>
    [JsonProperty("expandWidth", Order = 7)]
    public bool ExpandWidth { get; set; }

    [JsonProperty("expandHeight", Order = 8)]
    public bool ExpandHeight { get; set; }

    /// <summary>Fill from the far end - right to left, or bottom to top. The
    /// game uses this, and reading the order forwards where it is set puts the
    /// first item where the last one belongs.</summary>
    [JsonProperty("reverse", Order = 14)]
    public bool Reverse { get; set; }

    // ── Grid ─────────────────────────────────────────────────────────

    [JsonProperty("cellSize", Order = 9)]
    public float[] CellSize { get; set; } = { 100f, 100f };

    /// <summary>Flexible, FixedColumnCount or FixedRowCount.</summary>
    [JsonProperty("constraint", Order = 10)]
    public string Constraint { get; set; } = "Flexible";

    [JsonProperty("constraintCount", Order = 11)]
    public int ConstraintCount { get; set; } = 2;

    /// <summary>Whether a grid fills across before down, or down before
    /// across.</summary>
    [JsonProperty("startAxis", Order = 12)]
    public string StartAxis { get; set; } = "Horizontal";

    /// <summary>Which corner it starts from - Unity's names, e.g.
    /// "UpperLeft".</summary>
    /// <summary>
    /// Centre the final line when it is not full.
    /// <para/>
    /// Nineteen things in rows of seven leave a last row of five, and Unity
    /// packs it against the start corner - so a grid that is symmetrical
    /// everywhere else ends with everything shoved to one side. This centres
    /// that line and only that line; the full ones are already where they
    /// should be.
    /// <para/>
    /// Unity has no such option, so both the preview and the runtime do it
    /// themselves. For a grid that fills downwards the incomplete line is the
    /// last COLUMN, and it is centred vertically instead.
    /// </summary>
    [JsonProperty("centerLastLine", Order = 12, NullValueHandling = NullValueHandling.Ignore)]
    public bool CenterLastLine { get; set; }

    public bool ShouldSerializeCenterLastLine() => CenterLastLine;

    [JsonProperty("startCorner", Order = 13)]
    public string StartCorner { get; set; } = "UpperLeft";

    public bool IsGrid => Kind == UiLayoutKinds.Grid;
    public bool IsVertical => Kind == UiLayoutKinds.Vertical;

    public UiLayoutDef Copy() => new()
    {
        Kind = Kind,
        Spacing = (float[])Spacing.Clone(),
        Padding = (float[])Padding.Clone(),
        Alignment = Alignment,
        ControlWidth = ControlWidth,
        ControlHeight = ControlHeight,
        ExpandWidth = ExpandWidth,
        ExpandHeight = ExpandHeight,
        Reverse = Reverse,
        CellSize = (float[])CellSize.Clone(),
        Constraint = Constraint,
        ConstraintCount = ConstraintCount,
        StartAxis = StartAxis,
        StartCorner = StartCorner,
    };
}

public static class UiLayoutKinds
{
    public const string Horizontal = "Horizontal";
    public const string Vertical = "Vertical";
    public const string Grid = "Grid";

    public static readonly string[] All = { Horizontal, Vertical, Grid };
}
