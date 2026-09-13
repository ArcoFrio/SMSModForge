using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SMSModForge.Model;

/// <summary>How one of the game's tasks completes - Game Creator's own names.</summary>
public enum TaskCompletion { SubtasksInSequence, SubtasksInCombination, AnySubtask, Manual }

/// <summary>Whether one of the game's tasks counts, and where the count comes
/// from: set by an instruction, or read from a game variable.</summary>
public enum TaskCounter { None, Value, Property }

/// <summary>
/// The game's own quests, as a pack can point at them. The list itself is
/// generated into <c>VanillaQuests.cs</c> by <c>Tools/regen_vanilla_quests.py</c>;
/// this half is the shape and the lookups, written by hand.
/// </summary>
public static partial class VanillaQuests
{
    /// <summary>One task. <paramref name="Parent"/> is -1 for a top-level one.</summary>
    public sealed record VanillaTask(int Id, int Parent, string Name, TaskCompletion Completion,
                                     TaskCounter Counter, double CountTo, string CounterVariable, bool Hidden)
    {
        public bool IsTopLevel => Parent == -1;
    }

    /// <summary>
    /// One quest. <paramref name="Name"/> is the asset name, which is what the
    /// game's own instructions call it and what a pack stores; <paramref name="Title"/>
    /// is what the journal shows, and they differ for a third of them.
    /// </summary>
    public sealed record VanillaQuest(string Name, string Title, bool Hidden, int SortOrder,
                                      IReadOnlyList<VanillaTask> Tasks)
    {
        /// <summary>The title without its rich-text tags, for a picker.</summary>
        public string PlainTitle => StripTags(Title);

        public VanillaTask? Task(int id) => Tasks.FirstOrDefault(t => t.Id == id);

        public VanillaTask? Task(string idText)
            => int.TryParse(idText, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int id) ? Task(id) : null;

        /// <summary>How deep a task sits: 0 for a top-level one.</summary>
        public int DepthOf(VanillaTask task)
        {
            int depth = 0;
            for (var at = task; at != null && !at.IsTopLevel; at = Task(at.Parent)) depth++;
            return depth;
        }
    }

    /// <summary>A quest by the name a pack stores. Exact, as the runtime's
    /// lookup is.</summary>
    public static VanillaQuest? Find(string? name)
        => string.IsNullOrEmpty(name) ? null : All.FirstOrDefault(q => string.Equals(q.Name, name, StringComparison.Ordinal));

    private static readonly Regex Tags = new("<[^>]*>", RegexOptions.Compiled);

    internal static string StripTags(string text) => Tags.Replace(text ?? "", "");
}
