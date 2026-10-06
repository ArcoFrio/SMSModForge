using System;
using System.Collections.Generic;
using System.Windows;
using SMSModForge.Localization;
using SMSModForge.Model;

namespace SMSModForge.View;

/// <summary>
/// A short note beside a dropdown's items, the way the Direct Path list has
/// had one since 1.6.3 (the author, 1.7.0): what an action does, what a
/// category reaches, what one of the game's signals sets off, whose bust a
/// bust is.
/// <para/>
/// A ComboBox opts in with <see cref="KindProperty"/> and the
/// <c>DescribedItem</c> item style; the note shows only in the open list, never
/// in the closed box, and a search matches it as well as the item. Each list
/// says which kind of note it wants, because one string can mean different
/// things - "Variable" is a write among the actions and a question among the
/// conditions.
/// </summary>
public static class OptionNotes
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.RegisterAttached("Kind", typeof(string), typeof(OptionNotes), new PropertyMetadata(""));

    public static string GetKind(DependencyObject o) => (string)o.GetValue(KindProperty);
    public static void SetKind(DependencyObject o, string value) => o.SetValue(KindProperty, value);

    public const string ActionType = "actionType";
    public const string ConditionType = "conditionType";
    public const string Category = "category";
    public const string Signal = "signal";
    public const string VariableOperation = "variableOperation";
    public const string Component = "component";
    public const string Target = "target";
    public const string GameScreen = "gameScreen";
    public const string GameQuest = "gameQuest";

    /// <summary>
    /// The notes that depend on the pack open - whose bust a bust is, what a
    /// scene is called. Set by the window's view model: (kind, value) to note.
    /// </summary>
    public static Func<string, string, string?>? FromPack { get; set; }

    private static readonly Dictionary<string, string> ActionNotes = new(StringComparer.Ordinal)
    {
        [NodeActionTypes.CharacterFocus] = "note.action.characterFocus",
        [NodeActionTypes.LeaveBust] = "note.action.leaveBust",
        [NodeActionTypes.SetGameObjectActive] = "note.action.setActive",
        [NodeActionTypes.SetSprite] = "note.action.setSprite",
        [NodeActionTypes.FadeSprite] = "note.action.fadeSprite",
        [NodeActionTypes.MoveGameObject] = "note.action.move",
        [NodeActionTypes.SpinGameObject] = "note.action.spin",
        [NodeActionTypes.SetComponentProperty] = "note.action.component",
        [NodeActionTypes.DeactivateAllScenes] = "note.action.deactivateAllScenes",
        [NodeActionTypes.TransitionLevels] = "note.action.transitionLevels",
        [NodeActionTypes.Transitions] = "note.action.transitions",
        [NodeActionTypes.LeaveUiFaded] = "note.action.leaveUiFaded",
        [NodeActionTypes.EmitSignal] = "note.action.emitSignal",
        [NodeActionTypes.SetWeather] = "note.action.setWeather",
        [NodeActionTypes.SwitchMusic] = "note.action.switchMusic",
        [NodeActionTypes.PlaySFX] = "note.action.playSfx",
        [ViewModel.NodeActionViewModel.VariableFamilyType] = "note.action.variable",
        [NodeActionTypes.AddToList] = "note.action.addToList",
        [NodeActionTypes.RemoveFromList] = "note.action.removeFromList",
        [NodeActionTypes.ClearList] = "note.action.clearList",
        [NodeActionTypes.Wait] = "note.action.wait",
        [NodeActionTypes.DiceRoll] = "note.action.diceRoll",
        [NodeActionTypes.Quest] = "note.action.quest",
    };

    private static readonly Dictionary<string, string> ConditionNotes = new(StringComparer.Ordinal)
    {
        [ViewModel.NodeConditionViewModel.VariableFamilyType] = "note.condition.variable",
        [NodeConditionTypes.VariableExists] = "note.condition.variableExists",
        [NodeConditionTypes.VariableStartsWith] = "note.condition.variableStartsWith",
        [NodeConditionTypes.ListContains] = "note.condition.listContains",
        [NodeConditionTypes.ListCount] = "note.condition.listCount",
        [NodeConditionTypes.LevelActive] = "note.condition.levelActive",
        [NodeConditionTypes.GameObjectActive] = "note.condition.gameObjectActive",
        [NodeConditionTypes.Weather] = "note.condition.weather",
        [NodeConditionTypes.QuestState] = "note.condition.questState",
        [NodeConditionTypes.QuestCounter] = "note.condition.questCounter",
        [NodeConditionTypes.DailyChance] = "note.condition.dailyChance",
        [NodeConditionTypes.Random] = "note.condition.random",
        [NodeConditionTypes.Timer] = "note.condition.timer",
        [NodeConditionTypes.InputKey] = "note.condition.inputKey",
        [NodeConditionTypes.AlwaysTrue] = "note.condition.alwaysTrue",
    };

    private static readonly Dictionary<string, string> CategoryNotes = new(StringComparer.Ordinal)
    {
        [ViewModel.NodeActionViewModel.CatBust] = "note.kind.bust",
        [ViewModel.NodeActionViewModel.CatOverlay] = "note.kind.gameObjects",
        [ViewModel.NodeActionViewModel.CatNpcs] = "note.kind.npcs",
        [ViewModel.NodeActionViewModel.CatPlaces] = "note.kind.places",
        [ViewModel.NodeActionViewModel.CatScene] = "note.kind.scene",
        [ViewModel.NodeActionViewModel.CatUi] = "note.kind.ui",
        [ViewModel.NodeActionViewModel.CatPath] = "note.kind.directPath",
    };

    // What each of the game's signals sets off, read from the triggers that
    // listen for them under 6_Effects (1.8E).
    private static readonly Dictionary<string, string> SignalNotes = new(StringComparer.Ordinal)
    {
        ["drink"] = "note.signal.drink",
        ["flash"] = "note.signal.flash",
        ["kiss"] = "note.signal.kiss",
        ["FadeUI"] = "note.signal.fadeUi",
        ["ForceEnableUI"] = "note.signal.forceEnableUi",
    };

    private static readonly Dictionary<string, string> OperationNotes = new(StringComparer.Ordinal)
    {
        ["Set"] = "note.operation.set",
        ["Increment"] = "note.operation.increment",
        [ViewModel.NodeActionViewModel.OpRandomFromList] = "note.operation.randomFromList",
        [ViewModel.NodeActionViewModel.OpCountList] = "note.operation.countList",
    };

    private static readonly Dictionary<string, string> ComponentNotes = new(StringComparer.Ordinal)
    {
        [PackComponentType.FadeInSprite] = "note.component.fadeIn",
        [PackComponentType.FadeOutSprite] = "note.component.fadeOut",
        [PackComponentType.RandomChildActivator] = "note.component.randomChild",
        [PackComponentType.BlinkingSprite] = "note.component.blinking",
    };

    /// <summary>The note beside <paramref name="item"/> in a list of this
    /// kind, or empty when it has none.</summary>
    public static string NoteFor(string? kind, object? item)
    {
        if (string.IsNullOrEmpty(kind) || item == null) return "";
        string? text = item as string;
        switch (kind)
        {
            case ActionType: return Keyed(ActionNotes, text);
            case ConditionType: return Keyed(ConditionNotes, text);
            case Category: return Keyed(CategoryNotes, text);
            case Signal: return Keyed(SignalNotes, text);
            case VariableOperation: return Keyed(OperationNotes, text);
            case Component:
                if (text == null) return "";
                if (ComponentNotes.TryGetValue(text, out var key)) return Loc.T(key);
                return VanillaComponentCatalog.Find(text)?.IsEngineComponent == true ? Loc.T("note.component.engine") : "";
            case GameScreen:
                return item is VanillaUiCatalog.Base screen ? screen.Summary : "";
            case GameQuest:
                return Short(VanillaQuests.Find(item is ViewModel.NavigatorTargetOption q ? q.Token : text)?.Description);
            case Target:
                return text == null ? "" : FromPack?.Invoke(kind, text) ?? "";
            default:
                return "";
        }
    }

    private static string Keyed(Dictionary<string, string> notes, string? value)
        => value != null && notes.TryGetValue(value, out var key) ? Loc.T(key) : "";

    /// <summary>A journal description cut to its first sentence and a line's
    /// worth, with the game's markup taken out.</summary>
    private static string Short(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string plain = System.Text.RegularExpressions.Regex.Replace(text!, "<[^>]*>", "").Replace('\n', ' ').Trim();
        int stop = plain.IndexOfAny(new[] { '.', '!', '?' });
        if (stop > 0 && stop < 90) plain = plain.Substring(0, stop + 1);
        return plain.Length <= 90 ? plain : plain.Substring(0, 87).TrimEnd() + "...";
    }
}
