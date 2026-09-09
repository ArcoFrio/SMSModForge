using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Ten variable conditions folded into one.
/// <para/>
/// Five asked about a pack's variables and five asked the same five questions
/// of the game's, so both the operator and the store lived in the TYPE. The
/// editor already drew them as a single row with Source and Comparison
/// pickers — the split existed nowhere but the model.
/// <para/>
/// Every gate in every pack goes through this, so it is the largest structural
/// change the tool has made. A gate that quietly inverts, or silently stops
/// matching, is a bug found in play rather than in the editor.
/// </summary>
public sealed class VariableMergeMigrationTests
{
    private readonly ITestOutputHelper _out;
    public VariableMergeMigrationTests(ITestOutputHelper o) => _out = o;

    /// <summary>Each superseded type, and what it meant.</summary>
    public static TheoryData<string, string, bool> Superseded() => new()
    {
        { NodeConditionTypes.VariableEquals,         "equals",           false },
        { NodeConditionTypes.VariableGreaterThan,    "greater than",     false },
        { NodeConditionTypes.VariableGreaterOrEqual, "greater or equal", false },
        { NodeConditionTypes.VariableLessThan,       "less than",        false },
        { NodeConditionTypes.VariableLessOrEqual,    "less or equal",    false },

        { NodeConditionTypes.GameVariableEquals,               "equals",           true },
        { NodeConditionTypes.GameVariableNumberGreaterThan,    "greater than",     true },
        { NodeConditionTypes.GameVariableNumberGreaterOrEqual, "greater or equal", true },
        { NodeConditionTypes.GameVariableNumberLessThan,       "less than",        true },
        { NodeConditionTypes.GameVariableNumberLessOrEqual,    "less or equal",    true },
    };

    private static ModPack PackWith(params NodeConditionDef[] conditions)
    {
        var pack = new ModPack { PackId = "merge.pack" };
        var dialogue = new DialogueDef { Key = "d" };
        var node = new DialogueNodeDef { Id = 1, Text = "hi" };
        node.Conditions.AddRange(conditions);
        dialogue.Nodes.Add(node);
        pack.Dialogues.Add(dialogue);
        return pack;
    }

    private static NodeConditionDef Old(string type)
    {
        var c = new NodeConditionDef { Type = type };
        c.Params["name"] = "score";
        c.Params["value"] = "5";
        return c;
    }

    [Theory]
    [MemberData(nameof(Superseded))]
    public void EachOldTypeKeepsItsOperatorAndItsStore(string type, string comparison, bool vanilla)
    {
        var pack = PackWith(Old(type));
        PackMigration.Apply(pack);

        var only = pack.Dialogues[0].Nodes[0].Conditions[0];
        _out.WriteLine($"{type,-34} -> {only.Type} {comparison}"
                       + (vanilla ? " (vanilla)" : " (pack)"));

        Assert.Equal(NodeConditionTypes.VariableCompare, only.Type);
        Assert.Equal(comparison, only.Params["comparison"]);

        // Which store it reads is the other half the type used to carry.
        if (vanilla) Assert.Equal("vanilla", only.Params["source"]);
        else Assert.False(only.Params.ContainsKey("source"));

        // And what it compares is untouched.
        Assert.Equal("score", only.Params["name"]);
        Assert.Equal("5", only.Params["value"]);
    }

    [Fact]
    public void APackVariableConditionDoesNotBecomeAVanillaOne()
    {
        // The failure that would be hardest to spot: a gate that reads the
        // wrong STORE still evaluates, just against something else entirely.
        var explicitlyVanilla = Old(NodeConditionTypes.VariableGreaterThan);
        explicitlyVanilla.Params["source"] = "vanilla";

        var pack = PackWith(Old(NodeConditionTypes.VariableGreaterThan), explicitlyVanilla);
        PackMigration.Apply(pack);

        var conditions = pack.Dialogues[0].Nodes[0].Conditions;
        Assert.False(conditions[0].Params.ContainsKey("source"));
        Assert.Equal("vanilla", conditions[1].Params["source"]);
    }

    [Fact]
    public void ExistsIsNotSweptUpWithThem()
    {
        // "Is this set at all" is a different question: it takes no value and
        // it survives as its own type.
        var pack = PackWith(Old(NodeConditionTypes.VariableExists));
        PackMigration.Apply(pack);

        Assert.Equal(NodeConditionTypes.VariableExists,
                     pack.Dialogues[0].Nodes[0].Conditions[0].Type);
        Assert.Contains(NodeConditionTypes.All, t => t == NodeConditionTypes.VariableExists);
    }

    [Fact]
    public void OnlyTheMergedOneIsOfferedNow()
    {
        Assert.Contains(NodeConditionTypes.All, t => t == NodeConditionTypes.VariableCompare);
        foreach (var gone in new[]
        {
            NodeConditionTypes.VariableEquals, NodeConditionTypes.VariableGreaterThan,
            NodeConditionTypes.VariableGreaterOrEqual, NodeConditionTypes.VariableLessThan,
            NodeConditionTypes.VariableLessOrEqual, NodeConditionTypes.GameVariableEquals,
            NodeConditionTypes.GameVariableNumberGreaterThan,
            NodeConditionTypes.GameVariableNumberGreaterOrEqual,
            NodeConditionTypes.GameVariableNumberLessThan,
            NodeConditionTypes.GameVariableNumberLessOrEqual,
        })
            Assert.DoesNotContain(NodeConditionTypes.All, t => t == gone);
    }

    [Fact]
    public void RunningItTwiceChangesNothingTheSecondTime()
    {
        var pack = PackWith(Old(NodeConditionTypes.GameVariableNumberLessThan));

        Assert.True(PackMigration.Apply(pack).Migrated);
        var only = pack.Dialogues[0].Nodes[0].Conditions[0];
        Assert.Equal("less than", only.Params["comparison"]);

        var again = PackMigration.Apply(pack);
        Assert.DoesNotContain(again.Changes, c => c.What.Contains("Variable checks"));
        Assert.Equal("less than", only.Params["comparison"]);
    }

    [Fact]
    public void ARowBuiltFromAnUnmigratedConditionStillShowsItsComparison()
    {
        // A condition can reach a row without passing the migration - pasted
        // from another pack, or produced by the vanilla translator. The row
        // has to read it correctly rather than defaulting to "equals".
        foreach (var (type, comparison, _) in Superseded()
                     .Select(r => ((string)r[0], (string)r[1], (bool)r[2])))
        {
            var vm = new NodeConditionViewModel(Old(type));
            Assert.Equal(comparison, vm.VarComparison);
            Assert.Equal(NodeConditionTypes.VariableCompare, vm.Model.Type);
        }
    }

    [Fact]
    public void ChangingTheComparisonInTheEditorKeepsWhatItCompares()
    {
        var vm = new NodeConditionViewModel(Old(NodeConditionTypes.VariableEquals));

        vm.VarComparison = "less or equal";
        Assert.Equal(NodeConditionTypes.VariableCompare, vm.Model.Type);
        Assert.Equal("less or equal", vm.Model.Params["comparison"]);
        Assert.Equal("score", vm.Model.Params["name"]);
        Assert.Equal("5", vm.Model.Params["value"]);

        // "exists" is still its own type, and going back returns to the merged
        // one rather than stranding the row.
        vm.VarComparison = "exists";
        Assert.Equal(NodeConditionTypes.VariableExists, vm.Model.Type);
        Assert.False(vm.ShowVariableValue);

        vm.VarComparison = "greater than";
        Assert.Equal(NodeConditionTypes.VariableCompare, vm.Model.Type);
        Assert.Equal("greater than", vm.Model.Params["comparison"]);
        Assert.True(vm.ShowVariableValue);
    }
}
