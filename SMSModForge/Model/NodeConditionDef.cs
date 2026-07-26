using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One condition gating something (a dialogue's start, a node's eligibility,
/// a choice's visibility). Same Type/Params discriminator-on-string pattern
/// as <see cref="NodeActionDef"/> for the same reasons — pack authors can
/// extend the condition vocabulary without inventing per-type wire shapes,
/// and the plugin's runtime evaluator dispatches via a single switch.
/// </summary>
public sealed class NodeConditionDef
{
    [JsonProperty("type", Order = 1)]
    public string Type { get; set; } = "";

    [JsonProperty("params", Order = 2)]
    public Dictionary<string, string> Params { get; set; } = new();

    /// <summary>
    /// When true, the condition's result is inverted before being combined
    /// with the surrounding list. Lets a single typed condition serve both
    /// directions ("is set" / "is not set"). Maps roughly to GC2's
    /// <c>Condition.m_Sign</c>.
    /// </summary>
    [JsonProperty("negate", Order = 3, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool Negate { get; set; } = false;

    /// <summary>
    /// Child conditions, present only when <see cref="Type"/> is a group
    /// (<see cref="NodeConditionTypes.GroupAll"/> / <see cref="NodeConditionTypes.GroupAny"/>).
    /// A group evaluates its children with AND (<c>All</c>) or OR (<c>Any</c>);
    /// combined with <see cref="Negate"/> this gives full parenthesised
    /// boolean logic. Null/absent for leaf conditions, which use
    /// <see cref="Params"/> instead.
    /// </summary>
    [JsonProperty("conditions", Order = 4)]
    public List<NodeConditionDef>? Conditions { get; set; }

    /// <summary>Leaf conditions serialize <c>params</c>; groups omit it (and vice-versa).</summary>
    public bool ShouldSerializeParams() => Params != null && Params.Count > 0;
    public bool ShouldSerializeConditions() => Conditions != null && Conditions.Count > 0;
}

/// <summary>
/// Stable identifiers for condition types. Each is documented with the
/// <see cref="NodeConditionDef.Params"/> keys it expects.
/// </summary>
public static class NodeConditionTypes
{
    /// <summary>Pack variable equals a value. Params: <c>name</c>, <c>value</c>.</summary>
    public const string VariableEquals = "VariableEquals";

    /// <summary>Numeric pack variable is greater than a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string VariableGreaterThan = "VariableGreaterThan";

    /// <summary>Numeric pack variable is less than a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string VariableLessThan = "VariableLessThan";

    /// <summary>
    /// Numeric pack variable is &gt;= a threshold. Params: <c>name</c>,
    /// <c>value</c>. Useful for the inclusive end of a <c>refreshMode</c>
    /// random gate (e.g. a <c>value &lt;= 30</c> check
    /// becomes a <see cref="VariableLessOrEqual"/> at 30).
    /// </summary>
    public const string VariableGreaterOrEqual = "VariableGreaterOrEqual";

    /// <summary>Numeric pack variable is &lt;= a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string VariableLessOrEqual = "VariableLessOrEqual";

    /// <summary>Pack variable has been set at all (any value). Params: <c>name</c>.</summary>
    public const string VariableExists = "VariableExists";

    /// <summary>GC2 GlobalNameVariable equals a value. Params: <c>name</c>, <c>value</c>. Reads through GlobalNameVariablesManager.</summary>
    public const string GameVariableEquals = "GameVariableEquals";

    /// <summary>
    /// GC2 numeric global is &gt;= a threshold. Params:
    /// <c>name</c>, <c>value</c> (invariant-culture number).
    /// Covers the host mod-side <c>newtrait-Athletic &gt;= 5</c>
    /// pattern used by the a level-gated random dialogue.
    /// </summary>
    public const string GameVariableNumberGreaterOrEqual = "GameVariableNumberGreaterOrEqual";

    /// <summary>GC2 numeric global is &lt;= a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string GameVariableNumberLessOrEqual = "GameVariableNumberLessOrEqual";

    /// <summary>GC2 numeric global is strictly &gt; a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string GameVariableNumberGreaterThan = "GameVariableNumberGreaterThan";

    /// <summary>GC2 numeric global is strictly &lt; a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string GameVariableNumberLessThan = "GameVariableNumberLessThan";

    /// <summary>A level is currently active in the scene. Params: <c>level</c> (the <c>5_Levels</c> child name).</summary>
    public const string LevelActive = "LevelActive";

    /// <summary>A GameObject at a scene path is active. Params: <c>path</c>.</summary>
    public const string GameObjectActive = "GameObjectActive";

    /// <summary>
    /// Probability gate re-rolled on EVERY evaluation. Params: <c>chance</c> (0..1).
    /// <para/>
    /// <b>Deprecated — kept only so packs authored before
    /// <see cref="DailyChance"/> still load.</b> It is excluded from
    /// <see cref="All"/> (the Type combo) because almost every context that
    /// hosts a condition is polled per-frame (integration rules, dialogue
    /// start conditions, button visibility), where a per-evaluation coin
    /// flip re-rolls ~60×/second and the authored probability becomes
    /// meaningless. Use <see cref="DailyChance"/> for a per-day gate, or a
    /// <c>LevelRandom</c> pack variable + a numeric comparison for a
    /// per-visit gate.
    /// </summary>
    public const string Random = "Random";

    /// <summary>
    /// Probability gate rolled ONCE PER IN-GAME DAY. Param: <c>chance</c>
    /// (a whole percentage, 0..100).
    /// <para/>
    /// Every occurrence is an independent gate — nothing is named or shared
    /// (that's what a variable is for). The result is derived
    /// deterministically from the pack id, the condition's position in the
    /// manifest, and the in-game day counter, so it holds steady no matter
    /// how many times it's evaluated, survives scene reloads and save/reload
    /// within the same day (no save-scumming), and changes at each day
    /// roll-over. The runtime stamps the position-derived id on at load and
    /// logs each gate's outcome — labelled by its owning dialogue / rule —
    /// at the day change.
    /// </summary>
    public const string DailyChance = "DailyChance";

    /// <summary>
    /// A variable's string value begins with a prefix. Params: <c>name</c>,
    /// <c>value</c> (the prefix), optional <c>source</c> (<c>pack</c> default /
    /// <c>vanilla</c>) and <c>ignoreCase</c>.
    /// <para/>
    /// The comparison the Variable family can't express. Compound identifiers
    /// are everywhere — a <c>&lt;Room&gt;&lt;Slot&gt;</c> location means "which
    /// room is she in" is a prefix test. Writing that as a stack of negated
    /// equals works until the moment a new value is added, then silently stops
    /// being correct.
    /// </summary>
    public const string VariableStartsWith = "VariableStartsWith";

    /// <summary>
    /// A List-typed pack variable contains a value. Params: <c>list</c>
    /// (variable name), <c>value</c> (exact, case-sensitive match against an
    /// entry). Combine with Negate for "doesn't contain" — the natural way to
    /// express a no-share / not-yet-used constraint against an occupancy list.
    /// <para/>
    /// Pack-only: GC2's global variables have no list type, so there's no
    /// <c>source</c> toggle here.
    /// </summary>
    public const string ListContains = "ListContains";

    /// <summary>
    /// Compares the number of entries in a List-typed pack variable against a
    /// number. Params: <c>list</c>, <c>comparison</c> (<c>equals</c> /
    /// <c>greater than</c> / <c>greater or equal</c> / <c>less than</c> /
    /// <c>less or equal</c>), <c>value</c>.
    /// <para/>
    /// One type with a comparison dropdown rather than five sibling types, to
    /// keep the Type combo short. <c>equals 0</c> is the "is empty" check, and
    /// negating it gives "has anything in it".
    /// </summary>
    public const string ListCount = "ListCount";

    /// <summary>
    /// Real-time interval gate for integration rules. Params:
    /// <c>seconds</c> (fixed wait), <c>randomize</c> (bool), and
    /// <c>minSeconds</c>/<c>maxSeconds</c> (used instead of <c>seconds</c>
    /// when <c>randomize</c> is on — a fresh interval is rolled each time).
    /// <para/>
    /// Reads as a cooldown: it passes once the interval has elapsed since
    /// the rule <em>last fired</em>, and the runtime restarts it (re-rolling
    /// a randomized interval) only when the rule's actions actually run. So
    /// the gate stays "hot" while the rule's other conditions are still
    /// false, then fires the moment they pass — which is what a roaming /
    /// wandering schedule needs (wait out the interval, but don't move a
    /// character while the player is looking at the room).
    /// <para/>
    /// Starts elapsed, so a rule fires once on load and every interval
    /// after. Timing is real seconds (<c>Time.time</c>), not in-game days,
    /// and the state is in-memory: it resets on scene reload rather than
    /// persisting, so a reload re-randomizes rather than resuming.
    /// <para/>
    /// Offered only in <see cref="ConditionContext.Rule"/> — the other
    /// condition hosts have no "fired" event to restart the interval on.
    /// </summary>
    public const string Timer = "Timer";

    /// <summary>Always-true condition (useful for testing).</summary>
    public const string AlwaysTrue = "AlwaysTrue";

    /// <summary>
    /// Current weather check. Params: <c>state</c> — <c>Raining</c>,
    /// <c>Snowing</c>, or <c>BadWeather</c> (either). Reads the vanilla
    /// <c>rainy-day</c> / <c>snowy-day</c> game variables; combine with
    /// <c>negate</c> for "clear weather".
    /// </summary>
    public const string Weather = "Weather";

    /// <summary>
    /// Group: all child conditions must pass (AND). Carries a nested
    /// <see cref="NodeConditionDef.Conditions"/> list instead of params.
    /// Not a leaf type — created via the editor's "Add AND group" button,
    /// so it is deliberately excluded from <see cref="All"/> (the Type combo).
    /// </summary>
    public const string GroupAll = "All";

    /// <summary>Group: any child condition passing is enough (OR). See <see cref="GroupAll"/>.</summary>
    public const string GroupAny = "Any";

    /// <summary>True for the two group discriminators.</summary>
    public static bool IsGroup(string type) => type == GroupAll || type == GroupAny;

    /// <summary>Types offered in the editor's Type combo. Deliberately
    /// excludes group discriminators (added via the "Add group" buttons) and
    /// deprecated types like <see cref="Random"/> — see
    /// <see cref="AllRecognized"/> for what the validator accepts.</summary>
    public static readonly string[] All =
    {
        VariableEquals, VariableGreaterThan, VariableGreaterOrEqual,
        VariableLessThan, VariableLessOrEqual, VariableExists,
        GameVariableEquals,
        GameVariableNumberGreaterThan, GameVariableNumberGreaterOrEqual,
        GameVariableNumberLessThan, GameVariableNumberLessOrEqual,
        LevelActive, GameObjectActive, DailyChance, AlwaysTrue, Weather,
        VariableStartsWith, ListContains, ListCount,
    };

    /// <summary>
    /// Types offered where the condition is evaluated exactly ONCE per
    /// occurrence rather than polled every frame — dialogue <em>node</em>
    /// conditions (GC2 runs them when it reaches the node) and level
    /// enter/exit hooks (evaluated on the activation edge). A single roll
    /// is well-defined there, so <see cref="Random"/> is offered too.
    /// </summary>
    public static readonly string[] AllOneShot =
        All.Concat(new[] { Random }).ToArray();

    /// <summary>
    /// Types offered on integration rules. Adds <see cref="Timer"/>, which
    /// needs a "the rule fired" event to restart its interval — something
    /// only a rule has (dialogue start conditions and button visibility are
    /// polled predicates with no fire edge).
    /// </summary>
    public static readonly string[] AllRule =
        All.Concat(new[] { Timer }).ToArray();

    /// <summary>Every type the runtime still understands. The validator
    /// checks against this so a pack using <see cref="Random"/> in a polled
    /// context gets a migration warning rather than a hard "unknown type"
    /// error.</summary>
    public static readonly string[] AllRecognized =
        AllOneShot.Concat(new[] { Timer }).ToArray();
}

/// <summary>
/// How often the host of a condition list evaluates it. Drives which types
/// the editor offers: a per-evaluation coin flip (<see cref="NodeConditionTypes.Random"/>)
/// is only meaningful in <see cref="OneShot"/> contexts.
/// </summary>
public enum ConditionContext
{
    /// <summary>Re-evaluated every frame: dialogue start conditions,
    /// integration rules, navigator / radial button visibility. The
    /// restrictive default — a context that forgets to declare itself gets
    /// the safe list.</summary>
    Polled,

    /// <summary>Evaluated once per occurrence: dialogue node conditions
    /// (GC2 checks them when the node is reached) and level enter/exit
    /// hooks (checked on the activation edge).</summary>
    OneShot,

    /// <summary>
    /// Integration rules. Polled like <see cref="Polled"/>, but a rule also
    /// has a discrete "fired" moment, which is what lets
    /// <see cref="NodeConditionTypes.Timer"/> restart its interval.
    /// </summary>
    Rule,
}
