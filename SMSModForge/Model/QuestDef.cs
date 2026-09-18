using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SMSModForge.Shared;

namespace SMSModForge.Model;

/// <summary>
/// A quest of the pack's own, shown in the game's journal beside the game's.
/// <para/>
/// The runtime builds it into one of the game's own quests every session, so
/// the journal, the save and the quest popups all treat it as theirs. What the
/// player has done in it is saved by the game under an id DERIVED from the pack
/// id and <see cref="Key"/> - see <see cref="QuestIds"/> - which is why the key
/// is the one part of a quest that must not change once players have it.
/// </summary>
public sealed class QuestDef
{
    /// <summary>
    /// What actions and conditions name the quest by, and what its saved
    /// progress is filed under. Renaming it after release orphans every
    /// player's progress on it.
    /// </summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "quest1";

    /// <summary>
    /// What the journal calls it. Text in parentheses is drawn pink by the
    /// journal itself, the way the game's "Secrets (Adrian)" is.
    /// </summary>
    [JsonProperty("title", Order = 2)]
    public string Title { get; set; } = "New quest";

    /// <summary>The paragraph under the title.</summary>
    [JsonProperty("description", Order = 3)]
    public string Description { get; set; } = "";

    public bool ShouldSerializeDescription() => !string.IsNullOrEmpty(Description);

    /// <summary>
    /// When these all pass, the quest starts by itself. Checked every frame
    /// while the quest has not started.
    /// <para/>
    /// Empty does NOT mean "always", as it does on most condition lists: a
    /// quest that started the moment a pack loaded is not what an author who
    /// has not written anything yet means. Empty means only a Quest action
    /// starts it.
    /// </summary>
    [JsonProperty("startConditions", Order = 4)]
    public List<NodeConditionDef> StartConditions { get; set; } = new();

    public bool ShouldSerializeStartConditions() => StartConditions.Count > 0;

    /// <summary>
    /// The quest's top-level tasks. They run one after another: starting the
    /// quest starts the first, each one finishing starts the next, and the
    /// quest completes when the last one does.
    /// </summary>
    [JsonProperty("tasks", Order = 5)]
    public List<QuestTaskDef> Tasks { get; set; } = new();

    /// <summary>
    /// One of the game's own quests this entry extends, by the name the game's
    /// instructions use, or empty for a quest of the pack's own.
    /// <para/>
    /// The quest stays the game's: its title, its save entry and whatever the
    /// game does to move it. What the pack can say about it is what the journal
    /// shows (the paragraph under its title, and which of its tasks are
    /// listed), what happens as its tasks are done, and - the part that can
    /// break a save already under way - tasks added to it or taken out of it.
    /// </summary>
    [JsonProperty("source", Order = 6)]
    public string Source { get; set; } = "";

    public bool ShouldSerializeSource() => !string.IsNullOrEmpty(Source);

    /// <summary>Whether this entry is about one of the game's quests rather
    /// than one the pack draws itself.</summary>
    [JsonIgnore]
    public bool IsVanillaExtension => !string.IsNullOrEmpty(Source);

    /// <summary>What the pack says about the game's own tasks in this quest.
    /// Only on an extension.</summary>
    [JsonProperty("vanillaTasks", Order = 7)]
    public List<VanillaTaskHookDef> VanillaTasks { get; set; } = new();

    public bool ShouldSerializeVanillaTasks() => VanillaTasks.Count > 0;

    /// <summary>
    /// Tasks the pack adds to the game's quest, each placed among the game's
    /// own by <see cref="AddedTaskDef.Under"/> and <see cref="AddedTaskDef.Before"/>.
    /// Only on an extension. Kept apart from <see cref="Tasks"/>, which is a
    /// quest of the pack's own and means something else to a plugin that
    /// predates this list.
    /// </summary>
    [JsonProperty(QuestTreeEdits.AddedTasksKey, Order = 8)]
    public List<AddedTaskDef> AddedTasks { get; set; } = new();

    public bool ShouldSerializeAddedTasks() => AddedTasks.Count > 0;

    /// <summary>
    /// Whether this entry changes the SHAPE of the game's quest - adds tasks to
    /// it or takes some out - rather than only what the journal says about it.
    /// A save already part-way through the quest can be left unable to finish
    /// by that, which is why the game warns a player loading one.
    /// </summary>
    [JsonIgnore]
    public bool ChangesTheGamesTasks => IsVanillaExtension
                                        && (AddedTasks.Count > 0 || VanillaTasks.Any(h => h.Removed));

    /// <summary>
    /// When these all pass, a quest that has started - in progress, completed
    /// or failed - goes back to not started, the way the game's own resets do,
    /// so it can be done again. While they pass, the start conditions wait,
    /// so the two cannot take turns every frame. Empty never resets it.
    /// <para/>
    /// On the pack's own quests and on the game's, the same.
    /// </summary>
    [JsonProperty(GameConditionEdits.ResetConditionsKey, Order = 9)]
    public List<NodeConditionDef> ResetConditions { get; set; } = new();

    public bool ShouldSerializeResetConditions() => ResetConditions.Count > 0;

    /// <summary>
    /// Conditions of the pack's that one of the game's places waits for before
    /// it starts or resets this quest. Only on an extension, and only while an
    /// entry has conditions - an empty one is dropped.
    /// </summary>
    [JsonProperty(GameConditionEdits.SiteConditionsKey, Order = 10)]
    public List<SiteConditionsDef> SiteConditions { get; set; } = new();

    /// <summary>A place is written when the pack holds anything of its own for
    /// it, and when the save has worked out what it comes to - a place changed
    /// only by conditions taken out of the game's own lists has nothing else to
    /// show for itself.</summary>
    public bool ShouldSerializeSiteConditions()
        => SiteConditions.Any(s => s.Conditions.Count > 0 || s.RoomsOut.Count > 0 || s.Rule != null);
}

/// <summary>
/// One of the game's places that starts or resets a quest - a line of a
/// conversation, or a script - and the pack's conditions its step waits for.
/// The conversation or script runs as the game has it; only the quest step is
/// held back.
/// </summary>
public sealed class SiteConditionsDef
{
    /// <summary><see cref="GameConditionEdits.Starts"/> or
    /// <see cref="GameConditionEdits.Resets"/>.</summary>
    [JsonProperty(GameConditionEdits.DoesKey, Order = 1)]
    public string Does { get; set; } = GameConditionEdits.Starts;

    /// <summary>A conversation's path, for a place in one.</summary>
    [JsonProperty(GameConditionEdits.DialogueKey, Order = 2)]
    public string Dialogue { get; set; } = "";

    public bool ShouldSerializeDialogue() => !string.IsNullOrEmpty(Dialogue);

    /// <summary>The line's id in that conversation.</summary>
    [JsonProperty(GameConditionEdits.NodeKey, Order = 3, NullValueHandling = NullValueHandling.Ignore)]
    public long? Node { get; set; }

    /// <summary><see cref="GameConditionEdits.OnStart"/> or
    /// <see cref="GameConditionEdits.OnFinish"/>.</summary>
    [JsonProperty(GameConditionEdits.MomentKey, Order = 4)]
    public string Moment { get; set; } = "";

    public bool ShouldSerializeMoment() => !string.IsNullOrEmpty(Moment);

    /// <summary>The scene object, for a place in a script.</summary>
    [JsonProperty(GameConditionEdits.ByKey, Order = 5)]
    public string By { get; set; } = "";

    public bool ShouldSerializeBy() => !string.IsNullOrEmpty(By);

    [JsonProperty(GameConditionEdits.ScriptKey, Order = 6)]
    public string Script { get; set; } = "";

    public bool ShouldSerializeScript() => !string.IsNullOrEmpty(Script);

    [JsonProperty(GameConditionEdits.ConditionsKey, Order = 7)]
    public List<NodeConditionDef> Conditions { get; set; } = new();

    public bool ShouldSerializeConditions() => Conditions.Count > 0;

    /// <summary>The scripts whose room the pack has taken out, by object path,
    /// so the quest can start away from where the game's script lives.</summary>
    [JsonProperty(GameConditionEdits.RoomsOutKey, Order = 8)]
    public List<string> RoomsOut { get; set; } = new();

    public bool ShouldSerializeRoomsOut() => RoomsOut.Count > 0;

    /// <summary>
    /// Everything that has to pass for this place to happen, worked out from
    /// the game's own data when the pack is saved (<see cref="PlaceRules"/>)
    /// and empty at every other moment: the runtime has no extraction of the
    /// game to read, and the editor has no need of a copy.
    /// </summary>
    [JsonProperty(GameConditionEdits.RuleKey, Order = 9, NullValueHandling = NullValueHandling.Ignore)]
    public Newtonsoft.Json.Linq.JObject? Rule { get; set; }

    /// <summary>The same string the runtime matches places by.</summary>
    [JsonIgnore]
    public string Key => !string.IsNullOrEmpty(Dialogue)
        ? GameConditionEdits.DialogueSiteKey(Dialogue, Node ?? 0, Moment, Does)
        : GameConditionEdits.ScriptSiteKey(By, Script, Does);
}

/// <summary>
/// One of the game's condition lists - a room's branch, most often - and the
/// conditions the pack takes out of it. Stored once for the pack: the list is
/// the script's, and taking a condition out changes everything it guards.
/// </summary>
public sealed class GameGateEditDef
{
    [JsonProperty(GameConditionEdits.ByKey, Order = 1)]
    public string By { get; set; } = "";

    [JsonProperty(GameConditionEdits.ScriptKey, Order = 2)]
    public string Script { get; set; } = "";

    /// <summary>Where the list is inside the script: field names and
    /// positions.</summary>
    [JsonProperty(GameConditionEdits.AtKey, Order = 3)]
    public Newtonsoft.Json.Linq.JArray At { get; set; } = new();

    [JsonProperty(GameConditionEdits.RemovedKey, Order = 4)]
    public List<RemovedConditionDef> Removed { get; set; } = new();

    [JsonIgnore]
    public string Key => GameConditionEdits.GateKey(By, Script, GameConditionEdits.PathText(At));
}

/// <summary>One condition taken out of one of the game's lists.</summary>
public sealed class RemovedConditionDef
{
    /// <summary>Its position in the game's list.</summary>
    [JsonProperty(GameConditionEdits.IndexKey, Order = 1)]
    public int Index { get; set; }

    /// <summary>Game Creator's type for it, checked before it is taken out.</summary>
    [JsonProperty(GameConditionEdits.TypeKey, Order = 2)]
    public string Type { get; set; } = "";

    /// <summary>The game's own words for it, so a person reading the manifest
    /// - or a warning that it no longer matches - can tell which it was.</summary>
    [JsonProperty("title", Order = 3)]
    public string Title { get; set; } = "";

    public bool ShouldSerializeTitle() => !string.IsNullOrEmpty(Title);
}

/// <summary>
/// What a pack does about one task of the game's own: change the quest's
/// description once it is done, run actions when it is, keep it out of the
/// journal, or take it out of the quest.
/// <para/>
/// The task is named by the id the game files it under, which is what every
/// other reference to one of its tasks uses - its text is the game's and can be
/// rewritten by a patch, its id cannot.
/// </summary>
public sealed class VanillaTaskHookDef
{
    [JsonProperty("task", Order = 1)]
    public string Task { get; set; } = "";

    /// <summary>What the quest's description becomes once the game completes
    /// this task. Empty leaves it as it is.</summary>
    [JsonProperty("questDescription", Order = 2)]
    public string QuestDescription { get; set; } = "";

    public bool ShouldSerializeQuestDescription() => !string.IsNullOrEmpty(QuestDescription);

    /// <summary>Run once, when the game completes this task.</summary>
    [JsonProperty("actions", Order = 3)]
    public List<NodeActionDef> Actions { get; set; } = new();

    public bool ShouldSerializeActions() => Actions.Count > 0;

    /// <summary>
    /// How the journal shows the task: empty for as the game has it, or one of
    /// <see cref="QuestTreeEdits.HiddenUntilStarted"/> and
    /// <see cref="QuestTreeEdits.Hidden"/>.
    /// </summary>
    [JsonProperty(QuestTreeEdits.VisibilityKey, Order = 4)]
    public string Visibility { get; set; } = "";

    public bool ShouldSerializeVisibility() => !string.IsNullOrEmpty(Visibility);

    [JsonIgnore]
    public bool IsHidden => QuestVocabulary.Is(Visibility, QuestTreeEdits.Hidden);

    [JsonIgnore]
    public bool IsHiddenUntilStarted => QuestVocabulary.Is(Visibility, QuestTreeEdits.HiddenUntilStarted);

    [JsonIgnore]
    public bool IsHiddenUntilConditions => QuestVocabulary.Is(Visibility, QuestTreeEdits.HiddenUntilConditions);

    /// <summary>What brings the task into the journal when it is hidden until
    /// conditions pass. Kept when another choice is made, like a task's own.</summary>
    [JsonProperty(QuestTreeEdits.ShowConditionsKey, Order = 6)]
    public List<NodeConditionDef> ShowConditions { get; set; } = new();

    public bool ShouldSerializeShowConditions() => ShowConditions.Count > 0;

    /// <summary>Hide it again whenever the conditions stop passing, rather than
    /// keeping it shown once they have.</summary>
    [JsonProperty(QuestTreeEdits.ShowConditionsLiveKey, Order = 7)]
    public bool ShowConditionsLive { get; set; }

    public bool ShouldSerializeShowConditionsLive() => ShowConditionsLive;

    /// <summary>
    /// Take the task, and everything under it, out of the quest. The game's
    /// quest moves past it as though it were done the moment it starts, and
    /// the journal never lists it - which is what keeps the game's own
    /// dialogues, that still name it, from breaking on a task that is not
    /// there.
    /// </summary>
    [JsonProperty(QuestTreeEdits.RemovedKey, Order = 5)]
    public bool Removed { get; set; }

    public bool ShouldSerializeRemoved() => Removed;

    /// <summary>Whether this says anything at all - an empty row is not worth
    /// keeping in the manifest.</summary>
    [JsonIgnore]
    public bool DoesAnything => !string.IsNullOrEmpty(QuestDescription) || Actions.Count > 0
                                || !string.IsNullOrEmpty(Visibility) || Removed
                                || ShowConditions.Count > 0 || ShowConditionsLive;
}

/// <summary>
/// A task a pack adds to one of the game's own quests: everything a task of
/// the pack's own has, and where it sits among the game's.
/// <para/>
/// Placed by the game's task ids rather than by position, so a game update that
/// adds or moves one of its own tasks leaves the pack's where the author put
/// them relative to the tasks they could see.
/// </summary>
public sealed class AddedTaskDef : QuestTaskDef
{
    /// <summary>The id of the game's task this one is a subtask of, or empty
    /// for a top-level task. A subtask of one of the PACK's added tasks lives in
    /// that task's <see cref="QuestTaskDef.Subtasks"/> instead.</summary>
    [JsonProperty(QuestTreeEdits.UnderKey, Order = 20)]
    public string Under { get; set; } = "";

    public bool ShouldSerializeUnder() => !string.IsNullOrEmpty(Under);

    /// <summary>The id of the game's task this one comes just before, among
    /// the tasks it sits with, or empty for after all of the game's.</summary>
    [JsonProperty(QuestTreeEdits.BeforeKey, Order = 21)]
    public string Before { get; set; } = "";

    public bool ShouldSerializeBefore() => !string.IsNullOrEmpty(Before);

    [JsonIgnore]
    public bool IsTopLevel => string.IsNullOrEmpty(Under);
}

/// <summary>One line of a quest, and the lines under it.</summary>
public class QuestTaskDef
{
    /// <summary>
    /// What actions and conditions name the task by. Unique within its quest,
    /// not merely among its siblings: the game files a task's progress by quest
    /// and id, and the id comes from this.
    /// </summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "task1";

    /// <summary>The line the journal shows.</summary>
    [JsonProperty("name", Order = 2)]
    public string Name { get; set; } = "";

    /// <summary>
    /// How a task with subtasks completes - one of
    /// <see cref="QuestVocabulary.Completions"/>. Means nothing on a task with
    /// none, which is completed by an action.
    /// </summary>
    [JsonProperty("completion", Order = 3)]
    public string Completion { get; set; } = QuestVocabulary.InOrder;

    public bool ShouldSerializeCompletion()
        => !QuestVocabulary.Is(Completion, QuestVocabulary.InOrder) && Subtasks.Count > 0;

    /// <summary>
    /// When set, the task counts up to this and completes itself on reaching
    /// it. The journal draws the count only beside a top-level task.
    /// </summary>
    [JsonProperty("countTo", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public double? CountTo { get; set; }

    /// <summary>
    /// Where the count comes from: empty for Quest actions setting it, or
    /// <see cref="QuestVocabulary.CountFromVariable"/> for a number variable
    /// it follows. Stored rather than inferred from the name, so choosing a
    /// variable and not having named one yet is a state the file can hold.
    /// </summary>
    [JsonProperty("countFrom", Order = 5)]
    public string CountFrom { get; set; } = "";

    public bool ShouldSerializeCountFrom() => CountsFromVariable;

    /// <summary>The variable the count follows.</summary>
    [JsonProperty("countVariable", Order = 6)]
    public string CountVariable { get; set; } = "";

    public bool ShouldSerializeCountVariable() => CountsFromVariable && !string.IsNullOrEmpty(CountVariable);

    /// <summary><see cref="QuestVocabulary.Vanilla"/> for one of the game's
    /// variables; empty for one of the pack's.</summary>
    [JsonProperty("countSource", Order = 7)]
    public string CountSource { get; set; } = "";

    public bool ShouldSerializeCountSource() => CountsFromVariable && QuestVocabulary.Is(CountSource, QuestVocabulary.Vanilla);

    /// <summary>Whether this task counts, and its count follows a variable.</summary>
    [JsonIgnore]
    public bool CountsFromVariable => CountTo is > 0 && QuestVocabulary.Is(CountFrom, QuestVocabulary.CountFromVariable);

    /// <summary>Whether the variable it follows is one of the game's.</summary>
    [JsonIgnore]
    public bool CountVariableIsVanilla => QuestVocabulary.Is(CountSource, QuestVocabulary.Vanilla);

    /// <summary>
    /// When these all pass while the task is in progress, it completes. Only
    /// on a task with no subtasks - one with subtasks finishes through them.
    /// <para/>
    /// Empty means nothing completes it by itself: a Quest action, or its
    /// counter reaching the target, still can.
    /// </summary>
    [JsonProperty("conditions", Order = 8)]
    public List<NodeConditionDef> Conditions { get; set; } = new();

    public bool ShouldSerializeConditions() => Conditions.Count > 0;

    /// <summary>
    /// Run when the task completes, whatever completed it: its conditions, a
    /// Quest action, or its counter. Only on a task with no subtasks.
    /// </summary>
    [JsonProperty("actions", Order = 9)]
    public List<NodeActionDef> Actions { get; set; } = new();

    public bool ShouldSerializeActions() => Actions.Count > 0;

    [JsonProperty("subtasks", Order = 10)]
    public List<QuestTaskDef> Subtasks { get; set; } = new();

    public bool ShouldSerializeSubtasks() => Subtasks.Count > 0;

    /// <summary>
    /// What the quest's description becomes once this task is done. Empty
    /// leaves it alone.
    /// <para/>
    /// Several tasks can have one: the journal shows the one from the task
    /// furthest down the list that is done, and the quest's own description
    /// before any of them is. Worked out from where the player is, every time,
    /// rather than remembered - so it cannot come back wrong after a load.
    /// </summary>
    [JsonProperty("questDescription", Order = 11)]
    public string QuestDescription { get; set; } = "";

    public bool ShouldSerializeQuestDescription() => !string.IsNullOrEmpty(QuestDescription);

    /// <summary>
    /// Keep the task out of the journal until it has started. Offered on
    /// subtasks: the game's journal already leaves out a top-level task that
    /// has not started, and lists every subtask of a task it shows, started or
    /// not. Under an in-order task a subtask starts when the one before it is
    /// done, which is when this lets it be seen.
    /// </summary>
    [JsonProperty("hideUntilStarted", Order = 12)]
    public bool HideUntilStarted { get; set; }

    public bool ShouldSerializeHideUntilStarted() => HideUntilStarted;

    /// <summary>
    /// Keep the task out of the journal until <see cref="ShowConditions"/>
    /// pass. Offered on every task: unlike "hidden until it starts", this is
    /// about the pack's own state, not the journal's.
    /// </summary>
    [JsonProperty(QuestTreeEdits.HideUntilConditionsKey, Order = 13)]
    public bool HideUntilConditions { get; set; }

    public bool ShouldSerializeHideUntilConditions() => HideUntilConditions;

    /// <summary>What brings a task hidden until conditions pass into the
    /// journal. Checked every frame; kept when the option is switched off, so
    /// switching it back on does not lose them.</summary>
    [JsonProperty(QuestTreeEdits.ShowConditionsKey, Order = 14)]
    public List<NodeConditionDef> ShowConditions { get; set; } = new();

    public bool ShouldSerializeShowConditions() => ShowConditions.Count > 0;

    /// <summary>
    /// Hide the task again whenever <see cref="ShowConditions"/> stop passing.
    /// Off, which is the default, a task once shown stays shown for the rest of
    /// that save - the player has seen it, and taking it away reads as a bug.
    /// </summary>
    [JsonProperty(QuestTreeEdits.ShowConditionsLiveKey, Order = 15)]
    public bool ShowConditionsLive { get; set; }

    public bool ShouldSerializeShowConditionsLive() => ShowConditionsLive;

    /// <summary>This task and everything under it, depth first.</summary>
    public IEnumerable<QuestTaskDef> SelfAndDescendants()
    {
        yield return this;
        foreach (var sub in Subtasks)
            foreach (var t in sub.SelfAndDescendants())
                yield return t;
    }
}

public static class QuestDefExtensions
{
    /// <summary>
    /// Every task of a quest, depth first, top-level ones included - and on an
    /// extension, every task the pack adds to the game's quest. Each is a task
    /// of the pack's, with lists and keys like any other.
    /// </summary>
    public static IEnumerable<QuestTaskDef> AllTasks(this QuestDef quest)
    {
        foreach (var top in quest.Tasks)
            foreach (var t in top.SelfAndDescendants())
                yield return t;
        foreach (var added in quest.AddedTasks)
            foreach (var t in added.SelfAndDescendants())
                yield return t;
    }
}
