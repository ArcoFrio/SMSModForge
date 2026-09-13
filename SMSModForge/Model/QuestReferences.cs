using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SMSModForge.Shared;

namespace SMSModForge.Model;

/// <summary>
/// One task as a row naming it sees it, whether it belongs to the pack or to
/// the game.
/// </summary>
/// <param name="Token">What the row stores: a pack task's key, a game task's id.</param>
/// <param name="Completion">One of <see cref="QuestVocabulary.Completions"/>.</param>
/// <param name="CountTo">The target, or null when the task does not count.</param>
/// <param name="CounterFollowsVariable">A counter that follows a variable
/// rather than being set - an action writing it is overwritten.</param>
public sealed record QuestTaskInfo(string Token, string Name, int Depth, bool HasSubtasks,
                                   string Completion, double? CountTo, bool CounterFollowsVariable)
{
    public bool IsTopLevel => Depth == 0;
    public bool Counts => CountTo.HasValue;

    /// <summary>
    /// Whether it completes on its own once its subtasks are done. It can
    /// still be completed by an action before then - see
    /// <c>QuestValidation.Finishable</c> for where the game allows that.
    /// </summary>
    public bool CompletesItself => HasSubtasks && !QuestVocabulary.Is(Completion, QuestVocabulary.ByAction);
}

/// <summary>
/// What a quest row can point at: the pack's own quests by key, the game's by
/// asset name. Shared by the pickers, their notes and the validator, so all
/// three agree about what exists.
/// </summary>
public static class QuestReferences
{
    /// <summary>Whether a row's source param names the game's quests.</summary>
    public static bool IsVanilla(IReadOnlyDictionary<string, string>? ps)
        => ps != null && ps.TryGetValue(QuestVocabulary.SourceParam, out var s) && QuestVocabulary.Is(s, QuestVocabulary.Vanilla);

    public static string Param(IReadOnlyDictionary<string, string>? ps, string key)
        => ps != null && ps.TryGetValue(key, out var v) ? v ?? "" : "";

    /// <summary>A pack quest by key. Case-sensitive, as the runtime's id is.</summary>
    public static QuestDef? PackQuest(IEnumerable<QuestDef>? quests, string key)
        => quests?.FirstOrDefault(q => string.Equals(q.Key, key, StringComparison.Ordinal));

    /// <summary>Whether the quest a row names exists on the side it names.</summary>
    public static bool QuestExists(IEnumerable<QuestDef>? packQuests, bool vanilla, string quest)
        => vanilla ? VanillaQuests.Find(quest) != null : PackQuest(packQuests, quest) != null;

    /// <summary>The label a picker shows for a quest.</summary>
    public static string QuestLabel(IEnumerable<QuestDef>? packQuests, bool vanilla, string quest)
    {
        if (vanilla)
        {
            var v = VanillaQuests.Find(quest);
            if (v == null) return quest;
            return string.Equals(v.PlainTitle, v.Name, StringComparison.Ordinal) ? v.Name : v.PlainTitle + "  (" + v.Name + ")";
        }
        var p = PackQuest(packQuests, quest);
        return p == null ? quest : (string.IsNullOrWhiteSpace(p.Title) ? p.Key : p.Title);
    }

    /// <summary>
    /// Every task of the quest a row names, in journal order, or null when the
    /// quest itself cannot be found.
    /// </summary>
    public static IReadOnlyList<QuestTaskInfo>? TasksOf(IEnumerable<QuestDef>? packQuests, bool vanilla, string quest)
    {
        if (vanilla)
        {
            var v = VanillaQuests.Find(quest);
            if (v == null) return null;
            return v.Tasks.Select(t => new QuestTaskInfo(
                t.Id.ToString(CultureInfo.InvariantCulture), t.Name, v.DepthOf(t),
                v.Tasks.Any(c => c.Parent == t.Id),
                WordFor(t.Completion),
                t.Counter == TaskCounter.None ? null : t.CountTo,
                t.Counter == TaskCounter.Property)).ToList();
        }

        var p = PackQuest(packQuests, quest);
        if (p == null) return null;
        var list = new List<QuestTaskInfo>();
        void Walk(IEnumerable<QuestTaskDef> tasks, int depth)
        {
            foreach (var t in tasks)
            {
                list.Add(new QuestTaskInfo(t.Key, t.Name, depth, t.Subtasks.Count > 0,
                                           t.Completion, t.CountTo is > 0 ? t.CountTo : null, t.CountsFromVariable));
                Walk(t.Subtasks, depth + 1);
            }
        }
        Walk(p.Tasks, 0);
        return list;
    }

    /// <summary>One task of the quest a row names, or null.</summary>
    public static QuestTaskInfo? Task(IEnumerable<QuestDef>? packQuests, bool vanilla, string quest, string task)
        => TasksOf(packQuests, vanilla, quest)?.FirstOrDefault(t => string.Equals(t.Token, task, StringComparison.Ordinal));

    /// <summary>Game Creator's completion names in the pack's words.</summary>
    public static string WordFor(TaskCompletion completion) => completion switch
    {
        TaskCompletion.SubtasksInCombination => QuestVocabulary.AnyOrder,
        TaskCompletion.AnySubtask => QuestVocabulary.AnyOne,
        TaskCompletion.Manual => QuestVocabulary.ByAction,
        _ => QuestVocabulary.InOrder,
    };

    /// <summary>
    /// One short line about a task, for the note under a picker: what the game
    /// will do with it that the author cannot see from its name.
    /// </summary>
    public static string Describe(QuestTaskInfo? task)
    {
        if (task == null) return "";
        var parts = new List<string>();
        if (task.HasSubtasks)
            parts.Add(QuestVocabulary.Is(task.Completion, QuestVocabulary.ByAction)
                ? "completed by an action; its subtasks are notes"
                : "completes itself when " + (QuestVocabulary.Is(task.Completion, QuestVocabulary.AnyOne)
                    ? "any one subtask is done"
                    : "its subtasks are done, " + task.Completion));
        if (task.CounterFollowsVariable)
            parts.Add("counts from a variable"
                      + (task.CountTo is { } target ? " to " + target.ToString("0.##", CultureInfo.InvariantCulture) : ""));
        else if (task.Counts)
            parts.Add("counts to " + task.CountTo!.Value.ToString("0.##", CultureInfo.InvariantCulture)
                      + (task.IsTopLevel ? "" : " (the journal only shows counts on top-level tasks)"));
        return string.Join("; ", parts);
    }
}
