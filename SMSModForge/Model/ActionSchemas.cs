using System;
using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// Per-action-type parameter schemas consumed by the editor's row renderer.
/// Each key in <see cref="ByType"/> is a value from <see cref="NodeActionTypes"/>;
/// the corresponding <see cref="ParamSchema"/> array enumerates the params the
/// editor should expose (in display order).
/// <para/>
/// The runtime plugin reads params by string key from <see cref="NodeActionDef.Params"/>
/// — the schemas here only affect how the editor renders the row, never the on-disk
/// JSON. An action type with no schema (or no params) renders a bare type combo and
/// nothing else. An unknown action type falls back to <see cref="Empty"/>.
/// </summary>
public static class ActionSchemas
{
    /// <summary>Sentinel returned by <see cref="For"/> when an action type has no
    /// schema entry — keeps callers from having to null-check.</summary>
    public static readonly ParamSchema[] Empty = Array.Empty<ParamSchema>();

    /// <summary>Lookup from <see cref="NodeActionDef.Type"/> to its schema.</summary>
    public static readonly Dictionary<string, ParamSchema[]> ByType = new()
    {
        // ── Variable manipulation ──────────────────────────────────────
        [NodeActionTypes.SetVariable] = new[]
        {
            new ParamSchema("name", "Variable", ParamType.PackVarRef, "",
                "Pack variable to set."),
            new ParamSchema("value", "Value", ParamType.String, "",
                "New value. Strings round-trip as-is; use 'true' / 'false' for booleans, " +
                "and numbers in invariant-culture form (e.g. '3.14'). '$varName' copies " +
                "that variable's current value instead ('$$' escapes a literal dollar). " +
                "Leave it blank to CLEAR the variable to an empty string — that's kept as " +
                "a real value, not treated as an unfilled row.",
                emptyIsAValue: true),
        },
        [NodeActionTypes.IncrementVariable] = new[]
        {
            new ParamSchema("name", "Variable", ParamType.PackVarRef, "",
                "Numeric pack variable to add to (negative delta subtracts)."),
            new ParamSchema("delta", "Delta", ParamType.Float, "1",
                "Amount added to the current value each time the action runs."),
        },

        // ── Actor / bust visuals ───────────────────────────────────────
        [NodeActionTypes.SetSpriteFocus] = new[]
        {
            new ParamSchema("focused", "Focused", ParamType.Bool, "true",
                "When true, every actor's bust in the dialogue is bumped above the CG " +
                "layer; when false, their resting sorting orders are restored. No per-actor " +
                "target — it focuses the whole cast, and resets automatically when the " +
                "dialogue ends."),
        },
        [NodeActionTypes.LeaveBust] = new[]
        {
            new ParamSchema("actor", "Actor", ParamType.ActorRef, "",
                "Triggers the vanilla MBase1/Leave fade-out child if present; " +
                "falls back to an immediate deactivate."),
        },

        // ── Scene-graph + signal primitives ────────────────────────────
        [NodeActionTypes.SetGameObjectActive] = new[]
        {
            new ParamSchema("path", "GO path", ParamType.GameObjectPath, "",
                "A pack overlay/bust name (autocompleted), a vanilla GO name, or a full " +
                "hierarchy path ('5_Levels/14_Beach/Foreground/Thing'). Inactive objects " +
                "are found too, so this can activate something that starts disabled."),
            new ParamSchema("active", "Active", ParamType.Bool, "true",
                "True to SetActive(true), false for SetActive(false)."),
        },
        // Targeting params (kind/target/overlayLevel) are supplied by the
        // shared category row, same as SetGameObjectActive — only what is
        // unique to this action is declared here.
        [NodeActionTypes.SetSprite] = new[]
        {
            new ParamSchema("sprite", "Sprite", ParamType.SpriteRef, "",
                "Pack-relative PNG to draw. Applied at the image's own size."),
            new ParamSchema("mask", "Mask (optional)", ParamType.SpriteRef, "",
                "Pack-relative PNG for the material's _MaskTex. Leave blank to keep " +
                "the current mask. Warns if the target has no material or its shader " +
                "has no mask slot."),
        },
        [NodeActionTypes.EmitSignal] = new[]
        {
            new ParamSchema("signal", "Signal", ParamType.SignalRef, "",
                "GC2 signal name (the same string vanilla Trigger components use)."),
        },
        [NodeActionTypes.EmitSignalDelayed] = new[]
        {
            new ParamSchema("signal", "Signal", ParamType.SignalRef, "", ""),
            new ParamSchema("seconds", "Delay (s)", ParamType.Float, "1",
                "Seconds to wait before emitting the signal (action returns immediately)."),
        },
        [NodeActionTypes.TransitionLevels] = new[]
        {
            new ParamSchema("fromLevel", "From level", ParamType.LevelRef, "",
                "Source level (vanilla:<go> or place:<key>)."),
            new ParamSchema("toLevel", "To level", ParamType.LevelRef, "",
                "Destination level."),
            new ParamSchema("signal", "Done signal", ParamType.SignalRef, "",
                "Optional signal emitted at the end of the transition."),
            new ParamSchema("seconds", "Total time (s)", ParamType.Float, "1.5",
                "Total transition length; cross-fade and signal share this budget."),
        },
        // Target is edited via the shared Category + Target row (see
        // NodeActionViewModel.IsGoCategoryFamily); only the action-specific
        // params live in the schema now.
        [NodeActionTypes.FadeSprite] = new[]
        {
            new ParamSchema("to", "Target alpha", ParamType.Float, "1",
                "Target alpha (0..1)."),
            new ParamSchema("seconds", "Duration (s)", ParamType.Float, "0.5",
                "Time over which the alpha tween plays."),
        },
        [NodeActionTypes.SetComponentProperty] = new[]
        {
            new ParamSchema("component", "Component", ParamType.Choice, "CanvasGroup",
                "Which component on the target to write to. A closed list, not a type name: " +
                "each entry is supported on purpose so the property list below means something " +
                "and a bad value fails with a readable log line.",
                fixedOptions: new[] { "CanvasGroup" }),
            new ParamSchema("property", "Property", ParamType.Choice, "alpha",
                "Which field to set. CanvasGroup: 'alpha' hides the whole UI subtree without " +
                "deactivating it (so anything animating underneath keeps running); " +
                "'interactable' turns its controls on or off; 'blocksRaycasts' decides whether " +
                "clicks stop here or pass through to what is behind. Hiding a panel usually " +
                "wants all three, which is three rows.",
                fixedOptions: new[] { "alpha", "interactable", "blocksRaycasts" }),
            new ParamSchema("value", "Value", ParamType.String, "1",
                "The new value, read to suit the property: a number for 'alpha' (0..1), " +
                "'true' / 'false' for the two flags.",
                emptyIsAValue: true),
            new ParamSchema("seconds", "Duration (s)", ParamType.Float, "0",
                "0 sets the value immediately; anything higher tweens to it. Numeric " +
                "properties only — a flag has nothing to tween through, so this is ignored " +
                "for those and the change lands at once."),
        },

        [NodeActionTypes.MoveGameObject] = new[]
        {
            new ParamSchema("home", "Return to original", ParamType.Bool, "false",
                "Ease back to the position the object was at before the first MoveGameObject, " +
                "then release it (so e.g. parallax resumes). Ignores X / Y / Relative — use " +
                "this for the pan-back at the end of a scene instead of guessing the home coords."),
            new ParamSchema("x", "X", ParamType.Float, "0", "World X (offset if 'Relative')."),
            new ParamSchema("y", "Y", ParamType.Float, "0", "World Y (offset if 'Relative')."),
            new ParamSchema("seconds", "Duration (s)", ParamType.Float, "1",
                "Eased (ease-in + ease-out) move duration. The target is HELD afterward " +
                "so a parallax effect can't snap it back. 0 = instant snap. Note: world " +
                "units (e.g. a level pan is y = -17, not -1700)."),
            new ParamSchema("relative", "Relative", ParamType.Bool, "false",
                "When true, X/Y are added to the current local position; when false they're absolute."),
        },
        [NodeActionTypes.SpinGameObject] = new[]
        {
            new ParamSchema("speed", "Speed (°/s)", ParamType.Float, "1",
                "Degrees per second around Z. the host mod's a prop uses 1."),
            new ParamSchema("enable", "Spinning", ParamType.Bool, "true",
                "True to start/keep spinning, false to stop."),
        },

        // ── Audio ──────────────────────────────────────────────────────
        [NodeActionTypes.SwitchMusic] = new[]
        {
            new ParamSchema("music", "Music key", ParamType.MusicRef, "",
                "Pack music key, or the name of a vanilla 12_AudioPlayer child."),
        },
        [NodeActionTypes.PlaySFX] = new[]
        {
            new ParamSchema("clip", "Clip", ParamType.SfxRef, "",
                "Pack SFX key (declared on the SFX tab)."),
            new ParamSchema("volume", "Volume", ParamType.Float, "1",
                "Playback volume (0..1). Falls back to the SFX def's defaultVolume " +
                "when blank."),
            new ParamSchema("delay", "Delay (s)", ParamType.Float, "0",
                "Seconds to wait before the one-shot plays (action returns immediately)."),
        },

        // ── Flow control ───────────────────────────────────────────────
        // DeactivateAllScenes / ClearList (last has a single
        // list param). DeactivateAllScenes intentionally renders
        // no params.
        [NodeActionTypes.Wait] = new[]
        {
            new ParamSchema("seconds", "Seconds", ParamType.Float, "0.5",
                "How long to pause before the next node-on-start action runs."),
        },

        // ── List + variable helpers ────────────────────────────────────
        [NodeActionTypes.PickRandomFromList] = new[]
        {
            new ParamSchema("source", "Source", ParamType.String, "",
                "Either a comma-separated literal ('A,B,C'), or '$varName' " +
                "to read from a String or List pack variable."),
            new ParamSchema("excluding", "Excluding", ParamType.String, "",
                "Optional. Entries to remove from Source before picking — same three " +
                "shapes as Source ('$varName' is the usual one). This is how you claim " +
                "a slot nobody else holds: pick from all slots excluding the occupied list."),
            new ParamSchema("target", "Target", ParamType.PackVarRef, "",
                "Pack variable receiving the picked element."),
            new ParamSchema("fallback", "If none left", ParamType.String, "",
                "Value written to Target when Source is empty or everything was excluded. " +
                "Use it for an overflow destination with unlimited capacity. Accepts " +
                "'$varName'. Left blank, Target is cleared (the original behaviour)."),
        },
        [NodeActionTypes.AddToList] = new[]
        {
            new ParamSchema("list", "List", ParamType.ListVarRef, "",
                "List-typed pack variable to append to."),
            new ParamSchema("value", "Value", ParamType.String, "",
                "Value to append. '$varName' appends that variable's current value " +
                "instead of the literal text."),
            new ParamSchema("unique", "Only if absent", ParamType.Bool, "false",
                "When checked, the value is appended only if it isn't already in the list (no duplicates)."),
        },
        [NodeActionTypes.RemoveFromList] = new[]
        {
            new ParamSchema("list", "List", ParamType.ListVarRef, "", ""),
            new ParamSchema("value", "Value", ParamType.String, "",
                "First matching entry is removed; the action is a no-op when " +
                "the value isn't present. '$varName' removes that variable's current " +
                "value — how you free the slot a character is standing on."),
        },
        [NodeActionTypes.ClearList] = new[]
        {
            new ParamSchema("list", "List", ParamType.ListVarRef, "",
                "List-typed pack variable to reset to empty."),
        },
        // DiceRoll renders its own branch editor (chance + nested action per
        // branch) instead of schema-driven param rows.
        [NodeActionTypes.DiceRoll] = Empty,

        [NodeActionTypes.CountList] = new[]
        {
            new ParamSchema("fromList", "From list", ParamType.ListVarRef, "",
                "List-typed pack variable whose entry count is written."),
            new ParamSchema("name", "Target", ParamType.PackVarRef, "",
                "Pack variable (Int) receiving the number of entries."),
        },

        // ── Weather ────────────────────────────────────────────────────
        [NodeActionTypes.SetWeather] = new[]
        {
            new ParamSchema("weather", "Weather", ParamType.Choice, "Rain",
                "Sets today's weather: writes the vanilla rainy-day / snowy-day game " +
                "variables and refreshes the weather particles on the active level " +
                "immediately. Clear stops both.",
                new[] { "Rain", "Snow", "Clear" }),
        },

        // ── Scenes ─────────────────────────────────────────────────────
        [NodeActionTypes.ActivateScene] = new[]
        {
            new ParamSchema("scene", "Scene", ParamType.SceneRef, "",
                "Pack-local scene key declared on the Scenes tab."),
        },
        [NodeActionTypes.DeactivateAllScenes] = Empty,
        // No params: it's a hand-off, not a setting. See NodeActionTypes.LeaveUiFaded.
        [NodeActionTypes.LeaveUiFaded] = Empty,
    };

    /// <summary>Returns the schema for <paramref name="actionType"/>, or
    /// <see cref="Empty"/> when the type is unknown or has no params.</summary>
    public static ParamSchema[] For(string actionType)
        => actionType != null && ByType.TryGetValue(actionType, out var schema)
            ? schema
            : Empty;
}
