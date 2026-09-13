using System.Collections.Generic;
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
}

/// <summary>One line of a quest, and the lines under it.</summary>
public sealed class QuestTaskDef
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
    /// <summary>Every task of a quest, depth first, top-level ones included.</summary>
    public static IEnumerable<QuestTaskDef> AllTasks(this QuestDef quest)
    {
        foreach (var top in quest.Tasks)
            foreach (var t in top.SelfAndDescendants())
                yield return t;
    }
}
