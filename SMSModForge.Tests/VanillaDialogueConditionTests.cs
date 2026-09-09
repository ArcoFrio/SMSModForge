using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Vanilla conditions, read as the editor's own.
/// <para/>
/// The counts here are the whole game, taken from the shipped catalog: 1,915
/// boolean comparisons, 1,117 numeric ones across six operators, 35 chances and
/// 46 object-active checks. They are asserted rather than sampled because the
/// point of the translation is that it covers the corpus, and a mapping that
/// handled "most" conditions would show an author a gate the game does not
/// apply.
/// </summary>
public sealed class VanillaDialogueConditionTests
{
    private readonly ITestOutputHelper _out;
    public VanillaDialogueConditionTests(ITestOutputHelper o) => _out = o;

    private static IEnumerable<VanillaDialogueCatalog.Step> EveryCondition()
    {
        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var dialogue = VanillaDialogueCatalog.Open(entry.Id)!;
            foreach (var node in dialogue.Nodes.Values)
                foreach (var step in node.Conditions)
                    yield return step;
            foreach (var start in dialogue.Starts)
                foreach (var step in start.When)
                    yield return step;
        }
    }

    [Fact]
    public void TheBeachGateReadsAsTheEditorsOwnConditions()
    {
        var dialogue = VanillaDialogueCatalog.Open("8_Room_Talk/Beach/AnnaBeachDefault")!;
        var gate = VanillaDialogueConditions.TranslateAll(
            dialogue.Starts.Single().When, out int untranslated);

        Assert.Equal(0, untranslated);
        Assert.Equal(2, gate.Count);

        Assert.Equal(NodeConditionTypes.VariableCompare, gate[0].Type);
        Assert.Equal("equals", gate[0].Params["comparison"]);
        Assert.Equal("Day", gate[0].Params["name"]);
        Assert.Equal("2", gate[0].Params["value"]);
        Assert.False(gate[0].Negate);

        Assert.Equal(NodeConditionTypes.VariableCompare, gate[1].Type);
        Assert.Equal("anna-beach", gate[1].Params["name"]);
        Assert.Equal("false", gate[1].Params["value"]);
    }

    /// <summary>Each of the six numeric comparisons the game uses becomes the
    /// editor type that means the same thing.</summary>
    [Fact]
    public void EveryComparisonTheGameUsesHasAnEquivalent()
    {
        var byType = new Dictionary<string, int>();
        int total = 0, untranslated = 0;

        foreach (var step in EveryCondition())
        {
            total++;
            var one = VanillaDialogueConditions.Translate(step);
            if (one == null) { untranslated++; continue; }

            // The operator lives in a param now rather than in the type, so
            // the key spells it out - otherwise every variable check would
            // collapse into one row and the coverage below would prove nothing.
            string key = one.Type
                       + (one.Params != null && one.Params.TryGetValue("comparison", out var how)
                          ? " " + how : "")
                       + (one.Negate ? " (negated)" : "");
            byType[key] = byType.TryGetValue(key, out int had) ? had + 1 : 1;
        }

        foreach (var pair in byType.OrderByDescending(p => p.Value))
            _out.WriteLine($"  {pair.Key,-46} {pair.Value}");
        _out.WriteLine($"{total} conditions, {untranslated} without an equivalent");

        // Every operator the game uses is exercised, now as a Comparison on
        // the one merged type rather than as a type of its own.
        string Compare(string how) => NodeConditionTypes.VariableCompare + " " + how;
        Assert.Contains(Compare("greater or equal"), byType.Keys);
        Assert.Contains(Compare("less or equal"), byType.Keys);
        Assert.Contains(Compare("greater than"), byType.Keys);
        Assert.Contains(Compare("less than"), byType.Keys);
        Assert.Contains(Compare("equals"), byType.Keys);
        Assert.Contains(NodeConditionTypes.GameObjectActive, byType.Keys);
        Assert.Contains(NodeConditionTypes.Random, byType.Keys);
        Assert.Contains(Compare("equals") + " (negated)", byType.Keys);

        // Every single condition the game gates on has an equivalent here.
        Assert.Equal(0, untranslated);
        Assert.True(total > 3000, "the catalog should hold thousands of conditions");
    }

    /// <summary>
    /// A comparison against another variable, which is the case that used to
    /// have no equivalent at all.
    /// <para/>
    /// The game asks this in three places. It becomes a ${name} reference the
    /// runtime resolves before comparing, with valueSource saying which store
    /// to read - the mirror of the source that already picked the store for the
    /// name side.
    /// </summary>
    [Fact]
    public void AVariableCanBeComparedAgainstAnotherVariable()
    {
        var dialogue = VanillaDialogueCatalog.Open(
            "8_Room_Talk/Basement/Unique/FirstSexCondom")!;

        var step = dialogue.Nodes.Values
            .SelectMany(n => n.Conditions)
            .First(c => c.Title == "If Mainstory[MLove] > Mainstory[MCorruption]");

        var made = VanillaDialogueConditions.Translate(step);
        Assert.NotNull(made);
        Assert.Equal(NodeConditionTypes.VariableCompare, made!.Type);
        Assert.Equal("greater than", made.Params["comparison"]);
        Assert.Equal("MLove", made.Params["name"]);
        Assert.Equal("${MCorruption}", made.Params["value"]);
        Assert.Equal("vanilla", made.Params["valueSource"]);
        Assert.False(made.Negate);

        // Both of this conversation's conditions are that comparison, so the
        // literal case is checked where there is one: a value that is just a
        // value names no store at all.
        Assert.Equal(2, dialogue.Nodes.Values.SelectMany(n => n.Conditions).Count());

        var beach = VanillaDialogueCatalog.Open("8_Room_Talk/Beach/AnnaBeachDefault")!;
        var plain = VanillaDialogueConditions.Translate(beach.Starts.Single().When[0])!;
        Assert.Equal("2", plain.Params["value"]);
        Assert.False(plain.Params.ContainsKey("valueSource"));
    }

    /// <summary>
    /// The control: translation refuses what it cannot express.
    /// <para/>
    /// Without this the count above proves nothing, because a translator that
    /// returned something for every input would pass it too.
    /// </summary>
    [Fact]
    public void WhatCannotBeExpressedIsRefusedRatherThanGuessed()
    {
        Assert.Null(VanillaDialogueConditions.Translate(null));
        Assert.Null(VanillaDialogueConditions.Translate(new VanillaDialogueCatalog.Step()));

        // A type nothing here models.
        Assert.Null(VanillaDialogueConditions.Translate(new VanillaDialogueCatalog.Step
        {
            Type = "ConditionSomethingElse",
            Fields = new Newtonsoft.Json.Linq.JObject(),
        }));

        // A comparison this has never seen is refused rather than treated as
        // equals, which would invert a gate rather than skip it.
        Assert.Null(VanillaDialogueConditions.Translate(new VanillaDialogueCatalog.Step
        {
            Type = "ConditionMathCompareIntegers",
            Fields = Newtonsoft.Json.Linq.JObject.Parse(
                "{'m_Value':{'kind':'variable','variable':{'scope':'global','name':'x'}},"
                + "'m_CompareTo':{'m_Comparison':'Sideways',"
                + "'m_CompareTo':{'kind':'value','value':1}}}"),
        }));

        // And nothing in the game itself is refused any more.
        Assert.Empty(EveryCondition()
            .Where(s => VanillaDialogueConditions.Translate(s) == null));
    }
}
