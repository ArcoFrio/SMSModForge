using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Which params each action and condition type is read by - the game plugin's
/// own reading (ActionRuntime, ConditionEvaluator and what they hand their
/// params to) and the editor's (the type's schema). What tells a setting a
/// type has from one left behind by a type it used to be (found testing a
/// pack, 2026-11-01: a CharacterFocus holding the <c>kind</c>, <c>active</c>
/// and <c>target</c> of the Set-active it had been, an IncrementVariable the
/// <c>value</c> of a SetVariable - each read like a real setting by anyone
/// auditing the pack, and waiting for a later version to give the name a
/// meaning).
/// <para/>
/// A key is LEFT OVER on a type when the type does not read it and some
/// other type does. A key no type knows is never taken: it may be the plugin's
/// own bookkeeping (<c>_timerId</c>), something newer than this editor, or the
/// author's own. A type this list does not know is left whole.
/// <para/>
/// <see cref="ParamsReadTests"/> holds the plugin's lists to the plugin's
/// code: a key the plugin starts reading for a type must be added here, or a
/// pack would lose it the next time it is opened.
/// </summary>
public static class ParamsRead
{
    // The plugin's reading, as ParamsReadTests finds it in the plugin's code.
    // The old names packs may still hold read what the new ones do.
    private static readonly Dictionary<string, string[]> PluginActions = new(StringComparer.Ordinal)
    {
        [NodeActionTypes.SetVariable] = new[] { "name", "source", "value", "valueSource" },
        [NodeActionTypes.IncrementVariable] = new[] { "delta", "name", "source", "valueSource" },
        [NodeActionTypes.CharacterFocus] = new[] { "focused" },
        [NodeActionTypes.SetSpriteFocus] = new[] { "focused" },
        [NodeActionTypes.LeaveBust] = new[] { "actor" },
        [NodeActionTypes.SetGameObjectActive] = new[] { "active", "kind", "layer", "overlayLevel", "path", "scene", "target" },
        [NodeActionTypes.SetSprite] = new[] { "kind", "layer", "loop", "mask", "overlayLevel", "path", "sprite", "target", "valueSource", "volume" },
        [NodeActionTypes.EmitSignal] = new[] { "seconds", "signal" },
        [NodeActionTypes.EmitSignalDelayed] = new[] { "seconds", "signal" },
        [NodeActionTypes.LeaveUiFaded] = Array.Empty<string>(),
        [NodeActionTypes.TransitionLevels] = new[] { "fromLevel", "seconds", "signal", "toLevel" },
        [NodeActionTypes.Travel] = new[] { "music", "target" },
        [NodeActionTypes.FadeSprite] = new[] { "kind", "layer", "overlayLevel", "path", "seconds", "target", "to" },
        [NodeActionTypes.SetComponentProperty] = new[] { "component", "kind", "layer", "overlayLevel", "path", "property", "seconds", "target", "value" },
        [NodeActionTypes.MoveGameObject] = new[] { "home", "kind", "layer", "overlayLevel", "path", "relative", "seconds", "target", "x", "y" },
        [NodeActionTypes.TransformGameObject] = new[] { "kind", "layer", "overlayLevel", "path", "scale", "seconds", "target", "x", "y" },
        [NodeActionTypes.SpinGameObject] = new[] { "enable", "kind", "layer", "overlayLevel", "path", "speed", "target" },
        [NodeActionTypes.PlaySFX] = new[] { "clip", "delay", "volume" },
        [NodeActionTypes.SwitchMusic] = new[] { "music" },
        [NodeActionTypes.ActivateScene] = new[] { "scene", "target" },
        [NodeActionTypes.DeactivateAllScenes] = Array.Empty<string>(),
        [NodeActionTypes.Wait] = new[] { "seconds" },
        [NodeActionTypes.Transitions] = new[] { "seconds", "style", "text" },
        [NodeActionTypes.PickRandomFromList] = new[] { "excluding", "fallback", "fromList", "name", "source", "target", "valueSource" },
        [NodeActionTypes.AddToList] = new[] { "list", "unique", "value", "valueSource" },
        [NodeActionTypes.RemoveFromList] = new[] { "list", "value", "valueSource" },
        [NodeActionTypes.ClearList] = new[] { "list" },
        [NodeActionTypes.DiceRoll] = Array.Empty<string>(),
        [NodeActionTypes.Quest] = new[] { "operation", "quest", "source", "task", "value", "valueSource" },
        [NodeActionTypes.SetWeather] = new[] { "weather" },
        [NodeActionTypes.CountList] = new[] { "fromList", "list", "name", "target" },
        [NodeActionTypes.PlayDialogue] = new[] { "afterName", "afterValue", "conversation", "dialogue", "source" },
    };

    private static readonly Dictionary<string, string[]> PluginConditions = new(StringComparer.Ordinal)
    {
        [NodeConditionTypes.AlwaysTrue] = Array.Empty<string>(),
        [NodeConditionTypes.ListContains] = new[] { "list", "value", "valueSource" },
        [NodeConditionTypes.ListCount] = new[] { "comparison", "list", "value", "valueSource" },
        [NodeConditionTypes.Timer] = new[] { "_timerId", "maxSeconds", "minSeconds", "randomize", "seconds" },
        [NodeConditionTypes.VariableStartsWith] = new[] { "ignoreCase", "name", "source", "value", "valueSource" },
        [NodeConditionTypes.Weather] = new[] { "state" },
        [NodeConditionTypes.VariableCompare] = new[] { "comparison", "name", "source", "value", "valueSource" },
        [NodeConditionTypes.VariableEquals] = new[] { "name", "source", "value", "valueSource" },
        [NodeConditionTypes.VariableGreaterThan] = new[] { "name", "source", "value", "valueSource" },
        [NodeConditionTypes.VariableLessThan] = new[] { "name", "source", "value", "valueSource" },
        [NodeConditionTypes.VariableGreaterOrEqual] = new[] { "name", "source", "value", "valueSource" },
        [NodeConditionTypes.VariableLessOrEqual] = new[] { "name", "source", "value", "valueSource" },
        [NodeConditionTypes.VariableExists] = new[] { "name", "source" },
        [NodeConditionTypes.GameVariableEquals] = new[] { "name", "value", "valueSource" },
        [NodeConditionTypes.GameVariableNumberGreaterThan] = new[] { "name", "value", "valueSource" },
        [NodeConditionTypes.GameVariableNumberGreaterOrEqual] = new[] { "name", "value", "valueSource" },
        [NodeConditionTypes.GameVariableNumberLessThan] = new[] { "name", "value", "valueSource" },
        [NodeConditionTypes.GameVariableNumberLessOrEqual] = new[] { "name", "value", "valueSource" },
        [NodeConditionTypes.LevelActive] = new[] { "level" },
        [NodeConditionTypes.GameObjectActive] = new[] { "kind", "overlayLevel", "path", "target", "valueSource" },
        [NodeConditionTypes.InputKey] = new[] { "key", "phase", "quest", "source", "task" },
        [NodeConditionTypes.Random] = new[] { "chance" },
        [NodeConditionTypes.DailyChance] = new[] { "_rollId", "_rollLabel", "chance" },
        [Shared.QuestVocabulary.StateCondition] = new[] { "quest", "source", "state", "task" },
        [Shared.QuestVocabulary.CounterCondition] = new[] { "comparison", "quest", "source", "task", "value", "valueSource" },
    };

    /// <summary>The plugin's lists, for <see cref="ParamsReadTests"/>.</summary>
    internal static IReadOnlyDictionary<string, string[]> PluginActionKeys => PluginActions;
    internal static IReadOnlyDictionary<string, string[]> PluginConditionKeys => PluginConditions;

    private static readonly Lazy<HashSet<string>> AllActionKeys = new(() => new HashSet<string>(
        PluginActions.Keys.SelectMany(t => ActionKeys(t)!), StringComparer.Ordinal));
    private static readonly Lazy<HashSet<string>> AllConditionKeys = new(() => new HashSet<string>(
        PluginConditions.Keys.SelectMany(t => ConditionKeys(t)!), StringComparer.Ordinal));

    /// <summary>The keys an action type is read by, or null for a type this
    /// does not know.</summary>
    public static HashSet<string>? ActionKeys(string? type)
        => type != null && PluginActions.TryGetValue(type, out var plugin)
            ? new HashSet<string>(plugin.Concat(ActionSchemas.For(type).Select(s => s.Key)), StringComparer.Ordinal)
            : null;

    /// <summary>The keys a condition type is read by, or null for a type this
    /// does not know.</summary>
    public static HashSet<string>? ConditionKeys(string? type)
        => type != null && PluginConditions.TryGetValue(type, out var plugin)
            ? new HashSet<string>(plugin.Concat(ConditionSchemas.For(type).Select(s => s.Key)), StringComparer.Ordinal)
            : null;

    /// <summary>The keys of <paramref name="ps"/> left over on an action of
    /// <paramref name="type"/>: unread by it, read by another type.</summary>
    public static List<string> LeftOverOnAction(string? type, IReadOnlyDictionary<string, string>? ps)
        => LeftOver(ActionKeys(type), AllActionKeys.Value, ps);

    /// <summary>The same for a condition.</summary>
    public static List<string> LeftOverOnCondition(string? type, IReadOnlyDictionary<string, string>? ps)
        => LeftOver(ConditionKeys(type), AllConditionKeys.Value, ps);

    private static List<string> LeftOver(HashSet<string>? reads, HashSet<string> known, IReadOnlyDictionary<string, string>? ps)
    {
        if (reads == null || ps == null) return new List<string>();
        return ps.Keys.Where(k => !k.StartsWith("_", StringComparison.Ordinal) && !reads.Contains(k) && known.Contains(k)).ToList();
    }
}
