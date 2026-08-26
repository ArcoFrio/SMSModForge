namespace SMSModForge.Model;

/// <summary>
/// Logical kind of a single action / condition parameter, used by the
/// editor's param-row renderer to pick a typed editor (TextBox, CheckBox,
/// ComboBox bound to a specific option list, …) instead of letting authors
/// type raw <c>key=value</c> lines.
/// <para/>
/// Each value pairs with a <see cref="ParamSchema"/> entry declared in
/// <c>ActionSchemas</c> / <c>ConditionSchemas</c>. The view layer's
/// <c>ParamTypeTemplateSelector</c> maps each enum value to a per-type
/// <c>DataTemplate</c>, so adding a new parameter shape means: append the
/// enum value, add a template, update the selector.
/// </summary>
public enum ParamType
{
    /// <summary>Free-text string input. The fallback for anything we don't
    /// have a more specific editor for.</summary>
    String,

    /// <summary>Boolean, rendered as a checkbox. Round-trips through the
    /// underlying params dict as the literal strings <c>"true"</c> /
    /// <c>"false"</c> so the JSON shape stays human-readable.</summary>
    Bool,

    /// <summary>Integer-valued number; rendered as a TextBox with light
    /// validation. Stored as the invariant-culture decimal string.</summary>
    Int,

    /// <summary>Floating-point number; same TextBox treatment as
    /// <see cref="Int"/> but accepts decimals. Used for delays, alpha
    /// targets, increment deltas, etc.</summary>
    Float,

    /// <summary>Whole-number percentage, rendered as a TextBox with a
    /// trailing <c>%</c> and stored as the plain number (<c>"30"</c> = 30%).
    /// Authors think in percentages, so a probability reads better as
    /// <c>30 %</c> than as <c>0.3</c>; the runtime divides by 100.</summary>
    Percent,

    /// <summary>Name of a pack variable declared on the Variables tab.
    /// Combobox bound to <c>MainViewModel.VariableNameOptions</c>; editable
    /// so the user can still reference a not-yet-declared variable.</summary>
    PackVarRef,

    /// <summary>Same as <see cref="PackVarRef"/> but the dropdown filters to
    /// List-typed variables only. Used by <c>AddToList</c> /
    /// <c>RemoveFromList</c> / <c>ClearList</c> to nudge authors away from
    /// applying list ops to scalar vars.</summary>
    ListVarRef,

    /// <summary>Same as <see cref="PackVarRef"/> but when the referenced
    /// variable is boolean, renders as True/False radio buttons instead of
    /// a text box. Used by conditions/actions that compare a variable's value
    /// to true or false.</summary>
    BoolVarRef,

    /// <summary>Name of a GC2 Global-Name variable (vanilla or pack-side).
    /// Open-ended — there's no authoring-time enumeration of GC2 globals,
    /// so this stays a free-text input but the param-row renderer can
    /// surface a "use GNV alias" hint.</summary>
    GameVarRef,

    /// <summary>Level token in the editor's <c>vanilla:&lt;goName&gt;</c> /
    /// <c>place:&lt;key&gt;</c> form. Combobox bound to
    /// <c>MainViewModel.LevelOptions</c>; editable.</summary>
    LevelRef,

    /// <summary>Pack-local actor key. Combobox bound to
    /// <c>MainViewModel.ActorOptions</c>; editable so authors can also
    /// reference a vanilla actor name the pack doesn't redeclare.</summary>
    ActorRef,

    /// <summary>Bust GameObject name. Combobox bound to
    /// <c>MainViewModel.BustNameOptions</c> (per-actor default busts) or
    /// <c>ActorBustOptions</c> — editable; authors can type any vanilla GO
    /// name.</summary>
    BustRef,

    /// <summary>Actor expression key (Happy/Angry/Sad/Flirty + custom).
    /// Combobox bound to <c>MainViewModel.ExpressionKeyOptions</c>;
    /// editable.</summary>
    ExpressionRef,

    /// <summary>Pack-local scene key. Combobox bound to
    /// <c>MainViewModel.SceneOptions</c>; editable.</summary>
    SceneRef,

    /// <summary>GC2 signal name. Free-text — signals are an open vocabulary
    /// shared between the game and packs.</summary>
    SignalRef,

    /// <summary>Pack-local music key. Combobox bound to
    /// <c>MainViewModel.MusicKeyOptions</c>; editable so vanilla
    /// <c>12_AudioPlayer</c> children remain reachable by name.</summary>
    MusicRef,

    /// <summary>Pack-local SFX key. Combobox bound to
    /// <c>MainViewModel.SfxKeyOptions</c>; editable.</summary>
    SfxRef,

    /// <summary>Free-text scene-graph path (e.g. <c>5_Levels/MyScene</c>).
    /// Rendered as a wider TextBox so deep paths stay readable.</summary>
    GameObjectPath,

    /// <summary>Pack-relative path to a PNG (e.g. <c>Sprites/Amber/Angry.PNG</c>),
    /// rendered as an editable text box with a Browse button that stores the
    /// path relative to the pack root. Used by SetSprite for both the sprite and
    /// its optional mask.</summary>
    SpriteRef,

    /// <summary>Fixed set of options declared on the schema itself
    /// (<see cref="ParamSchema.FixedOptions"/>). Rendered as a
    /// non-editable ComboBox — the value is always one of the options.</summary>
    Choice,
}

/// <summary>
/// Declarative description of one parameter on an action or condition type.
/// One <see cref="ParamSchema"/> turns into one editor row (label + typed
/// control) when the user picks the owning Type in the action/condition
/// combo.
/// <para/>
/// This is purely editor-side metadata — the runtime still reads params
/// from the underlying <c>Params</c> dictionary by string key. The schema
/// determines how the editor surfaces the same key, not how the plugin
/// interprets it. Adding a new <see cref="ParamSchema"/> entry never
/// changes the on-disk JSON shape.
/// </summary>
public sealed class ParamSchema
{
    /// <summary>Dictionary key in <see cref="NodeActionDef.Params"/> /
    /// <see cref="NodeConditionDef.Params"/>. Must match what the
    /// plugin's runtime reads.</summary>
    public string Key { get; }

    /// <summary>Label shown to the left of the editor row. Title-cased,
    /// short — the runtime key stays out of the UI.</summary>
    public string Label { get; }

    /// <summary>Which editor control to render for this param.</summary>
    public ParamType Type { get; }

    /// <summary>Value used when the param is missing from the dict on
    /// first render. Never overwrites an existing value; this is purely
    /// a "what does the user see in an empty editor" hint.</summary>
    public string DefaultValue { get; }

    /// <summary>Optional hover tooltip shown on the editor row.</summary>
    public string Tooltip { get; }

    /// <summary>The selectable values for a <see cref="ParamType.Choice"/>
    /// param. Ignored by every other type.</summary>
    public string[] FixedOptions { get; }

    /// <summary>
    /// Optional key of a sibling param that gates this row: when set, the row's
    /// editor is disabled unless that sibling's value equals
    /// <see cref="EnabledWhenValue"/>. Purely a UI affordance — the value stays
    /// in the params dict and the runtime is unaffected, so toggling the gate
    /// off and back on doesn't lose what was typed.
    /// <para/>
    /// Used for params that only apply in one mode, e.g. the Timer condition's
    /// min/max seconds, which mean nothing unless <c>randomize</c> is checked.
    /// </summary>
    public string EnabledWhen { get; }

    /// <summary>Value <see cref="EnabledWhen"/>'s param must hold for this row
    /// to be enabled. Compared case-insensitively.</summary>
    public string EnabledWhenValue { get; }

    /// <summary>
    /// True when an EMPTY string is a meaningful value for this param rather
    /// than "not set" — clearing a variable with <c>SetVariable</c>, say. The
    /// editor normally drops a param whose value is emptied, so the key simply
    /// disappears and the row reads as unfilled; for these params it writes the
    /// empty string instead, which keeps "deliberately cleared" distinguishable
    /// from "never filled in" both on disk and to the validator.
    /// </summary>
    public bool EmptyIsAValue { get; }

    public ParamSchema(string key, string label, ParamType type,
                       string defaultValue = "", string tooltip = "",
                       string[] fixedOptions = null,
                       string enabledWhen = null, string enabledWhenValue = "true",
                       bool emptyIsAValue = false)
    {
        Key = key;
        Label = label;
        Type = type;
        DefaultValue = defaultValue;
        Tooltip = tooltip;
        FixedOptions = fixedOptions ?? System.Array.Empty<string>();
        EnabledWhen = enabledWhen;
        EnabledWhenValue = enabledWhenValue;
        EmptyIsAValue = emptyIsAValue;
    }
}
