using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SMSModForge.Shared;

namespace SMSModForge.Model;

/// <summary>
/// One task of a game quest as a pack extending it leaves it: one of the game's
/// own, or one the pack adds.
/// </summary>
public sealed class ExtensionTaskNode
{
    /// <summary>The game's task, or null for one the pack adds.</summary>
    public VanillaQuests.VanillaTask? Game { get; init; }

    /// <summary>The pack's task, or null for one of the game's. An
    /// <see cref="AddedTaskDef"/> where it is placed among the game's tasks, a
    /// plain <see cref="QuestTaskDef"/> where it is a subtask of one of those.</summary>
    public QuestTaskDef? Added { get; init; }

    /// <summary>What the pack says about the game's task, when it says anything.</summary>
    public VanillaTaskHookDef? Hook { get; init; }

    public ExtensionTaskNode? Parent { get; init; }

    public int Depth { get; init; }

    /// <summary>An added task whose game task is not in this quest - written
    /// for another quest, or for a task a game update removed. The runtime
    /// leaves it out.</summary>
    public bool Orphan { get; init; }

    public bool IsGame => Game != null;

    public bool IsTopLevel => Depth == 0;

    /// <summary>What a Quest action names it by: the game's id, or the pack's key.</summary>
    public string Token => Game != null ? Game.Id.ToString(CultureInfo.InvariantCulture) : Added?.Key ?? "";

    public string Name => Game?.Name ?? Added?.Name ?? "";

    /// <summary>Taken out of the quest by the pack, itself.</summary>
    public bool RemovedSelf => Hook?.Removed == true;

    /// <summary>Taken out of the quest, itself or with a task above it.</summary>
    public bool Removed => RemovedSelf || Parent?.Removed == true;

    /// <summary>Whether the runtime puts this in the game's quest at all: an
    /// added task is added at the top level, under one of the game's tasks, or
    /// as a direct subtask of an added top-level task - nothing deeper.</summary>
    public bool IsBuilt => Game != null || (!Orphan && (Parent == null || Parent.IsGame || Parent.Parent == null));

    /// <summary>How it completes, in the pack's words.</summary>
    public string Completion => Game != null ? QuestReferences.WordFor(Game.Completion) : Added?.Completion ?? QuestVocabulary.InOrder;
}

/// <summary>
/// A game quest's task list as a pack extending it leaves it, in the order the
/// journal lists it. The placement itself is <see cref="QuestTreeEdits.Arrange"/>,
/// which the runtime uses too.
/// </summary>
public static class ExtensionTree
{
    public static List<ExtensionTaskNode> Build(QuestDef extension)
        => Build(VanillaQuests.Find(extension.Source), extension);

    public static List<ExtensionTaskNode> Build(VanillaQuests.VanillaQuest? game, QuestDef extension)
    {
        var rows = new List<ExtensionTaskNode>();
        var gameTasks = game?.Tasks ?? (IReadOnlyList<VanillaQuests.VanillaTask>)Array.Empty<VanillaQuests.VanillaTask>();
        var known = new HashSet<string>(gameTasks.Select(t => Token(t.Id)), StringComparer.Ordinal);

        void AddAdded(QuestTaskDef task, ExtensionTaskNode? parent, int depth, bool orphan)
        {
            var row = new ExtensionTaskNode { Added = task, Parent = parent, Depth = depth, Orphan = orphan };
            rows.Add(row);
            foreach (var sub in task.Subtasks) AddAdded(sub, row, depth + 1, orphan);
        }

        void Level(int parentId, ExtensionTaskNode? parentRow, int depth)
        {
            var children = gameTasks.Where(t => t.Parent == parentId).ToList();
            string under = parentId == QuestIds.NoNode ? "" : Token(parentId);
            var added = extension.AddedTasks
                .Where(a => string.Equals(QuestTreeEdits.Clean(a.Under), under, StringComparison.Ordinal))
                .ToList();

            foreach (var placed in QuestTreeEdits.Arrange(children, t => Token(t.Id), added, a => a.Before))
            {
                if (placed.IsAdded)
                {
                    AddAdded(placed.Added, parentRow, depth, orphan: false);
                    continue;
                }

                var g = placed.Game;
                string token = Token(g.Id);
                var row = new ExtensionTaskNode
                {
                    Game = g,
                    Hook = extension.VanillaTasks.FirstOrDefault(h => string.Equals(h.Task, token, StringComparison.Ordinal)),
                    Parent = parentRow,
                    Depth = depth,
                };
                rows.Add(row);
                Level(g.Id, row, depth + 1);
            }
        }

        Level(QuestIds.NoNode, null, 0);

        // Placed under a task this quest does not have: listed last, so the
        // author can see it and move or remove it, rather than it vanishing.
        foreach (var a in extension.AddedTasks)
            if (!a.IsTopLevel && !known.Contains(QuestTreeEdits.Clean(a.Under)))
                AddAdded(a, null, 1, orphan: true);

        return rows;
    }

    /// <summary>The rows directly under <paramref name="parent"/> (null for the
    /// top level), in order.</summary>
    public static List<ExtensionTaskNode> ChildrenOf(IReadOnlyList<ExtensionTaskNode> rows, ExtensionTaskNode? parent)
        => rows.Where(r => ReferenceEquals(r.Parent, parent) && !r.Orphan).ToList();

    public static string Token(int id) => id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// When a task of a game quest starts, as the pack leaves the quest.
/// <para/>
/// Nothing in the game starts a task directly - Game Creator has no
/// instruction for it - so this is the quest's structure and nothing else: the
/// quest starts its first top-level task, a finished task starts the one after
/// it, a task whose subtasks run in order starts the first, one whose subtasks
/// run in any order (or finish with any one) starts them all, and one completed
/// by an action starts none. A task the pack takes out is completed the moment
/// it starts, so the one after it starts straight away.
/// </summary>
public static class TaskStarts
{
    public static string For(ExtensionTaskNode node, IReadOnlyList<ExtensionTaskNode> rows)
    {
        // Taken out, or never put in: their own notes say what happens.
        if (!node.IsBuilt || node.Removed) return "";

        var parent = node.Parent;
        switch (parent?.Completion)
        {
            case QuestVocabulary.ByAction:
                return "Never starts: " + Quoted(parent) + " is completed by an action, and the game starts no "
                     + "subtasks under a task like that.";
            case QuestVocabulary.AnyOrder:
            case QuestVocabulary.AnyOne:
                return "Starts when " + Quoted(parent) + " does, together with every subtask beside it.";
        }

        var siblings = ExtensionTree.ChildrenOf(rows, parent).Where(r => r.IsBuilt).ToList();
        int before = siblings.IndexOf(node) - 1;

        var skipped = new List<ExtensionTaskNode>();
        while (before >= 0 && siblings[before].RemovedSelf) skipped.Add(siblings[before--]);

        string text = before >= 0
            ? "Starts once " + Quoted(siblings[before]) + " is done"
            : parent == null
                ? "Starts when the quest does"
                : "Starts when " + Quoted(parent) + " does";

        if (skipped.Count == 1)
            text += " - " + Quoted(skipped[0]) + ", which your pack takes out, is skipped on the way";
        else if (skipped.Count > 1)
            text += " - the " + skipped.Count + " tasks your pack takes out before it are skipped on the way";
        return text + ".";
    }

    private static string Quoted(ExtensionTaskNode node)
        => "“" + (string.IsNullOrWhiteSpace(node.Name) ? "(no text)" : node.Name.Trim()) + "”";
}
