using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SMSModForge.Shared;
using SMSModForge.Localization;

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
/// <param name="Added">A task the pack adds to one of the game's quests.</param>
/// <param name="Removed">One of the game's tasks the pack takes out of its quest.</param>
public sealed record QuestTaskInfo(string Token, string Name, int Depth, bool HasSubtasks,
                                   string Completion, double? CountTo, bool CounterFollowsVariable,
                                   bool Added = false, bool Removed = false)
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

    /// <summary>A quest of the pack's own by key. Case-sensitive, as the
    /// runtime's id is. An entry extending one of the game's quests is not one:
    /// a row names that quest on the Vanilla side, by the game's name.</summary>
    public static QuestDef? PackQuest(IEnumerable<QuestDef>? quests, string key)
        => quests?.FirstOrDefault(q => !q.IsVanillaExtension && string.Equals(q.Key, key, StringComparison.Ordinal));

    /// <summary>The pack's entry extending one of the game's quests, or null.
    /// The first, when there are two - validation says so.</summary>
    public static QuestDef? Extension(IEnumerable<QuestDef>? quests, string gameQuest)
        => quests?.FirstOrDefault(q => q.IsVanillaExtension && string.Equals(q.Source, gameQuest, StringComparison.Ordinal));

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

            // The pack's own changes to that quest are part of it as far as the
            // pack is concerned: its added tasks can be named, and a task it
            // took out is still there to name, marked.
            var extension = Extension(packQuests, quest);
            if (extension != null && (extension.AddedTasks.Count > 0 || extension.VanillaTasks.Any(h => h.Removed)))
            {
                var rows = ExtensionTree.Build(v, extension);
                return rows.Where(r => !r.Orphan).Select(r => new QuestTaskInfo(
                    r.Token, r.Name, r.Depth,
                    rows.Any(c => ReferenceEquals(c.Parent, r)),
                    r.Completion,
                    r.Game != null
                        ? (r.Game.Counter == TaskCounter.None ? null : r.Game.CountTo)
                        : (r.Added!.CountTo is > 0 ? r.Added.CountTo : null),
                    r.Game != null ? r.Game.Counter == TaskCounter.Property : r.Added!.CountsFromVariable,
                    Added: r.Added != null,
                    Removed: r.Removed)).ToList();
            }

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
        if (task.Removed) parts.Add(Loc.T("questPicker.task.removed"));
        if (task.Added) parts.Add(Loc.T("questPicker.task.added"));
        if (task.HasSubtasks)
            parts.Add(QuestVocabulary.Is(task.Completion, QuestVocabulary.ByAction)
                ? Loc.T("questPicker.task.byAction")
                : QuestVocabulary.Is(task.Completion, QuestVocabulary.AnyOne)
                    ? Loc.T("questPicker.task.anyOne")
                    : Loc.F("questPicker.task.allSubtasks", "order", ParamSchema.ChoiceText("completion", task.Completion)));
        if (task.CounterFollowsVariable)
            parts.Add(task.CountTo is { } target
                ? Loc.F("questPicker.task.countsFromVariableTo", "target", target.ToString("0.##", CultureInfo.InvariantCulture))
                : Loc.T("questPicker.task.countsFromVariable"));
        else if (task.Counts)
            parts.Add(Loc.F(task.IsTopLevel ? "questPicker.task.countsTo" : "questPicker.task.countsToHidden",
                            "target", task.CountTo!.Value.ToString("0.##", CultureInfo.InvariantCulture)));
        return string.Join("; ", parts);
    }
}
