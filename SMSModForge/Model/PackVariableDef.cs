using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A pack-defined variable. Variables are read/written by dialogue actions
/// and conditions and (when <see cref="Persisted"/>) survive between game
/// sessions via a per-pack save file under
/// <c>BepInEx/plugins/SMSModForge/Saves/&lt;packId&gt;.json</c>.
/// <para/>
/// The variable system is intentionally separate from GC2's
/// <c>GlobalNameVariables</c> — packs author their own state without
/// needing to ship a custom GNV asset. The plugin can be configured per
/// variable to mirror reads/writes to a GC2 global (future feature) so
/// vanilla dialogues can branch on pack state.
/// </summary>
public sealed class PackVariableDef
{
    /// <summary>Pack-local name. Used as the key in actions/conditions and as the JSON key on disk.</summary>
    [JsonProperty("name", Order = 1)]
    public string Name { get; set; } = "newvar";

    /// <summary>
    /// Storage type. Determines how the default value is interpreted and
    /// how condition comparisons run. String values are compared with
    /// ordinal equality; numerics with full ordering; bools with equality.
    /// </summary>
    [JsonProperty("type", Order = 2)]
    public PackVariableType Type { get; set; } = PackVariableType.Bool;

    /// <summary>
    /// Default value as a string (so the wire format is type-agnostic).
    /// For bool: <c>"true"</c> / <c>"false"</c>. For numerics:
    /// invariant-culture string. For strings: the literal default.
    /// </summary>
    [JsonProperty("defaultValue", Order = 3)]
    public string DefaultValue { get; set; } = "";

    /// <summary>
    /// When true, the variable's current value is persisted to disk on
    /// change (or on autosave-on-sleep, depending on plugin policy). When
    /// false, the variable resets to <see cref="DefaultValue"/> at every
    /// fresh CoreGameScene load — useful for per-session flags.
    /// </summary>
    [JsonProperty("persisted", Order = 4)]
    public bool Persisted { get; set; } = true;

    /// <summary>
    /// Auto-refresh policy. Generalises the host mod's two patterns in one
    /// field:
    /// <list type="bullet">
    ///   <item><see cref="PackVariableRefreshMode.Daily"/> — reset to
    ///   <see cref="DefaultValue"/> at the start of each in-game day.</item>
    ///   <item><see cref="PackVariableRefreshMode.DailyRandom"/> — re-roll a
    ///   fresh random integer/float in <see cref="MinValue"/>..<see cref="MaxValue"/>
    ///   at each day change. The replacement for "is today a lucky day"
    ///   gating where the same coin flip needs to hold for one in-game day.</item>
    ///   <item><see cref="PackVariableRefreshMode.LevelRandom"/> — re-roll
    ///   each time the level named by <see cref="RefreshScope"/>
    ///   transitions from inactive to active: a fresh roll on
    ///   every visit, handy for gating "rare" idle chatter.
    ///   Level-scoped rather than roomtalk-scoped so every place
    ///   can use it — many vanilla levels ship without a roomtalk node.</item>
    /// </list>
    /// Both random modes only apply to <see cref="PackVariableType.Int"/> /
    /// <see cref="PackVariableType.Float"/>; non-numeric variables under a
    /// random mode are reset to default at the same trigger instead.
    /// </summary>
    [JsonProperty("refreshMode", Order = 5, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public PackVariableRefreshMode RefreshMode { get; set; } = PackVariableRefreshMode.Never;

    /// <summary>
    /// Only used when <see cref="RefreshMode"/> is
    /// <see cref="PackVariableRefreshMode.LevelRandom"/>. Names the
    /// level whose activation re-rolls this variable, formatted as
    /// <c>vanilla:&lt;goName&gt;</c> or <c>place:&lt;key&gt;</c> (same
    /// format as the <c>LevelActive</c> condition's <c>level</c> param).
    /// Empty for the other modes.
    /// </summary>
    [JsonProperty("refreshScope", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
    public string? RefreshScope { get; set; }

    /// <summary>
    /// Legacy compat field. Older modpacks shipped a bool
    /// <c>refreshDaily</c>; the plugin and editor now use the unified
    /// <see cref="RefreshMode"/> instead. Reads silently fold a true
    /// value into <see cref="PackVariableRefreshMode.Daily"/> via the
    /// after-deserialize hook below; writes always emit
    /// <see cref="RefreshMode"/>.
    /// </summary>
    [JsonProperty("refreshDaily", Order = 99, DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
    public bool? LegacyRefreshDaily { get; set; }

    [System.Runtime.Serialization.OnDeserialized]
    internal void OnDeserializedMigrateRefresh(System.Runtime.Serialization.StreamingContext _)
    {
        // If the manifest only carried the legacy bool, promote it to the
        // enum so the rest of the codebase sees one canonical field.
        if (RefreshMode == PackVariableRefreshMode.Never &&
            LegacyRefreshDaily == true)
            RefreshMode = PackVariableRefreshMode.Daily;
        // Strip the legacy field on write — it's been migrated.
        LegacyRefreshDaily = null;
    }

    /// <summary>
    /// Optional lower clamp for <see cref="PackVariableType.Int"/> /
    /// <see cref="PackVariableType.Float"/> variables. When set, the runtime
    /// keeps every write and increment at or above this bound — the same way
    /// the host mod clamps <c>Affection_*</c> to 0–5. Invariant-culture
    /// numeric string. Ignored for non-numeric types and when unset.
    /// <para/>
    /// Doubles as the lower bound for random refresh modes
    /// (<see cref="PackVariableRefreshMode.DailyRandom"/> /
    /// <see cref="PackVariableRefreshMode.LevelRandom"/>); defaults to 0
    /// when unset.
    /// </summary>
    [JsonProperty("minValue", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
    public string? MinValue { get; set; }

    /// <summary>
    /// Optional upper clamp — the counterpart to <see cref="MinValue"/>.
    /// Doubles as the upper bound for random refresh modes; defaults to 100
    /// (matching a 0–100 random roll) when unset.
    /// </summary>
    [JsonProperty("maxValue", Order = 8, NullValueHandling = NullValueHandling.Ignore)]
    public string? MaxValue { get; set; }

    /// <summary>Free-form description shown in the editor — not used at runtime.</summary>
    [JsonProperty("description", Order = 9, NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; set; }
}

/// <summary>
/// How a pack variable's value evolves over time without explicit
/// <c>SetVariable</c> writes from dialogue actions.
/// </summary>
[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum PackVariableRefreshMode
{
    /// <summary>No auto-refresh. The value only changes via explicit dialogue actions.</summary>
    Never,

    /// <summary>
    /// Reset to <c>defaultValue</c> at the start of each in-game day. The
    /// generalised form of a daily-reset naming convention.
    /// </summary>
    Daily,

    /// <summary>
    /// Roll a fresh random number (within <c>minValue</c>..<c>maxValue</c>,
    /// defaulting to 0..100) at the start of each in-game day. Used to
    /// gate dialogues that should fire on a fraction of days.
    /// </summary>
    DailyRandom,

    /// <summary>
    /// Roll a fresh random number each time the level named by
    /// <c>refreshScope</c> transitions from inactive to active (gated on
    /// that level's <c>activeSelf</c>): a fresh roll on
    /// every visit so idle "rare" chatter can be gated by a single
    /// comparison condition.
    /// </summary>
    LevelRandom,
}

[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum PackVariableType
{
    Bool,
    Int,
    Float,
    String,

    /// <summary>
    /// Ordered list of string values. Persisted as a JSON array
    /// inside the same key/value blob other variables use. Read with
    /// <c>PickRandomFromList source="$varName"</c> (random element)
    /// and mutated with <c>AddToList</c> / <c>RemoveFromList</c> /
    /// <c>ClearList</c> node actions. Numeric / boolean comparisons
    /// against a list-typed variable evaluate on the JSON literal
    /// (rarely what you want — use list-specific actions instead).
    /// </summary>
    List,
}
