using System;
using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// The one rewrite that turns ten variable conditions into one.
/// <para/>
/// Five types asked about a pack's variables and five more asked the same five
/// questions of the game's, so the operator and the store were both encoded in
/// the TYPE. The editor already drew them as a single row with Source and
/// Comparison pickers, which is the tell: the split existed nowhere but the
/// model, and it made a new operator cost a type, a schema entry, a runtime
/// case and a line in every list that names one.
/// <para/>
/// Kept here rather than in the migration because three callers need the same
/// answer: the migration on load, the view model when a row is built from
/// something the migration has not seen, and the vanilla-dialogue translator.
/// Three copies of a mapping table is three chances to disagree.
/// </summary>
public static class VariableMerge
{
    /// <summary>The comparisons a merged condition can carry, in the order they
    /// are offered.</summary>
    public static readonly string[] Comparisons =
        { "equals", "greater than", "greater or equal", "less than", "less or equal" };

    /// <summary>What each superseded type meant: its operator, and whether it
    /// read the game's store rather than the pack's.</summary>
    private static readonly Dictionary<string, (string Comparison, bool Vanilla)> Superseded =
        new(StringComparer.Ordinal)
    {
        [NodeConditionTypes.VariableEquals]         = ("equals", false),
        [NodeConditionTypes.VariableGreaterThan]    = ("greater than", false),
        [NodeConditionTypes.VariableGreaterOrEqual] = ("greater or equal", false),
        [NodeConditionTypes.VariableLessThan]       = ("less than", false),
        [NodeConditionTypes.VariableLessOrEqual]    = ("less or equal", false),

        [NodeConditionTypes.GameVariableEquals]                = ("equals", true),
        [NodeConditionTypes.GameVariableNumberGreaterThan]     = ("greater than", true),
        [NodeConditionTypes.GameVariableNumberGreaterOrEqual]  = ("greater or equal", true),
        [NodeConditionTypes.GameVariableNumberLessThan]        = ("less than", true),
        [NodeConditionTypes.GameVariableNumberLessOrEqual]     = ("less or equal", true),
    };

    /// <summary>
    /// Every type that has been folded away, for anything that needs to know
    /// what no longer exists rather than merely whether one name does.
    /// <para/>
    /// The tutorials are checked against this: prose naming a condition the
    /// editor stopped offering sends an author looking for a dropdown entry
    /// that is not there, and no amount of walking the steps would catch it -
    /// a step's check reads the pack, not the words above it.
    /// </summary>
    public static IReadOnlyCollection<string> SupersededTypes => Superseded.Keys;

    /// <summary>Whether this type has been folded into VariableCompare.</summary>
    public static bool IsSuperseded(string? type)
        => type != null && Superseded.ContainsKey(type);

    /// <summary>
    /// Rewrite one condition in place. Returns whether anything changed, so a
    /// caller can count it — and so running it twice reports nothing the
    /// second time.
    /// <para/>
    /// A condition that already reads from the game keeps doing so: the five
    /// GameVariable types said it in their name, and an older pack may have
    /// said it in a param instead. Neither is overwritten by the other.
    /// </summary>
    public static bool Rewrite(NodeConditionDef? condition)
    {
        if (condition?.Type == null) return false;
        if (!Superseded.TryGetValue(condition.Type, out var meaning)) return false;

        condition.Type = NodeConditionTypes.VariableCompare;
        condition.Params ??= new Dictionary<string, string>();
        condition.Params["comparison"] = meaning.Comparison;

        if (meaning.Vanilla) condition.Params["source"] = "vanilla";
        return true;
    }

    /// <summary>
    /// The comparison a condition is asking for, whether it says so in a param
    /// or in its type.
    /// <para/>
    /// Falls back to "equals" for anything unrecognised, which is what the
    /// editor showed before a comparison could be spelled out.
    /// </summary>
    public static string ComparisonOf(NodeConditionDef? condition)
    {
        if (condition == null) return "equals";

        if (condition.Params != null
            && condition.Params.TryGetValue("comparison", out string? said)
            && Array.IndexOf(Comparisons, said) >= 0)
            return said!;

        if (condition.Type != null && Superseded.TryGetValue(condition.Type, out var meaning))
            return meaning.Comparison;

        return "equals";
    }
}
