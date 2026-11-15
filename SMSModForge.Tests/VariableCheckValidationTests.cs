using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Variable condition - the one check that replaced ten in 1.2.0 - as
/// the validator reads it. Until 1.8.0 it had no case there at all, so a
/// misspelled variable in any condition of any pack read the default without
/// a word (found 2026-10-25, adding a screen's "Opens when"). Each check has
/// its control: the same row put right, or a form that is fine as it is.
/// </summary>
public sealed class VariableCheckValidationTests
{
    private readonly ITestOutputHelper _out;
    public VariableCheckValidationTests(ITestOutputHelper o) => _out = o;

    private static NodeConditionDef Check(string name, string comparison = "equals", string value = "true",
                                          string? source = null, string? valueSource = null)
    {
        var c = new NodeConditionDef { Type = NodeConditionTypes.VariableCompare };
        c.Params["name"] = name;
        c.Params["comparison"] = comparison;
        c.Params["value"] = value;
        if (source != null) c.Params["source"] = source;
        if (valueSource != null) c.Params["valueSource"] = valueSource;
        return c;
    }

    /// <summary>What the validator says about one condition, on a rule.</summary>
    private List<ValidationIssue> About(NodeConditionDef condition)
    {
        var pack = PackRepository.CreateEmpty("checks.pack");
        pack.Variables.Add(new PackVariableDef { Name = "met", Type = PackVariableType.Bool, DefaultValue = "false" });
        pack.Variables.Add(new PackVariableDef { Name = "coins", Type = PackVariableType.Int, DefaultValue = "0" });
        pack.IntegrationRules.Add(new UpdateRuleDef { Key = "r", Conditions = { condition } });
        var issues = PackValidator.Validate(pack, Path.GetTempPath())
                                  .Where(i => i.Where.StartsWith("integrationRules[r].conditions[0]")).ToList();
        foreach (var i in issues) _out.WriteLine($"{i.Severity} {i.Code}: {i.Message}");
        return issues;
    }

    private static bool Says(List<ValidationIssue> issues, string code, string? naming = null)
        => issues.Any(i => i.Code == code && (naming == null || i.Message.Contains(naming)));

    [Fact]
    public void A_variable_the_pack_does_not_have_is_named()
    {
        Assert.True(Says(About(Check("mett")), "condition.undeclaredVariable", "mett"));
        Assert.Empty(About(Check("met")));                                  // the control
        Assert.True(Says(About(Check("")), "condition.paramNameMissing"));
        Assert.Empty(About(Check("{item}_met")));                           // a rule's placeholder: known only at runtime
    }

    [Fact]
    public void One_of_the_games_variables_is_looked_up_in_the_games_list()
    {
        string real = VanillaGameVariables.AllNames.First();
        Assert.Empty(About(Check(real, source: "vanilla", value: "1")));
        Assert.True(Says(About(Check("NoSuchGameVariable", source: "vanilla")), "condition.unknownVanillaVariable", "NoSuchGameVariable"));
        // Not the pack's own list: a game name is not "undeclared".
        Assert.False(Says(About(Check(real, source: "vanilla", value: "1")), "condition.undeclaredVariable"));
    }

    /// <summary>
    /// The game tells capitals apart (Game Creator's NameVariableRuntime is a
    /// plain Dictionary&lt;string, ...&gt;, read from the game's assembly, 1.9.0):
    /// "Mei-corrupted" and "mei-corrupted" are two variables, and "MEI-CORRUPTED"
    /// reaches nothing. Until 1.9.0 the check ignored case and passed it.
    /// </summary>
    [Fact]
    public void A_game_variable_with_other_capitals_is_named_with_the_games_spelling()
    {
        Assert.Empty(About(Check("Mei-corrupted", source: "vanilla")));     // the control: both are the game's
        Assert.Empty(About(Check("mei-corrupted", source: "vanilla")));
        var wrong = About(Check("MEI-CORRUPTED", source: "vanilla"));
        Assert.True(Says(wrong, "condition.unknownVanillaVariable", "MEI-CORRUPTED"));
        Assert.Contains(wrong, i => i.Message.Contains("'Mei-corrupted'") || i.Message.Contains("'mei-corrupted'"));
        // A name the game has in no spelling at all keeps the plain message.
        Assert.DoesNotContain(About(Check("NoSuchGameVariable", source: "vanilla")),
                              i => i.Message == SMSModForge.Localization.Loc.F("validation.vanillaVariableCapitals",
                                       "name", "NoSuchGameVariable", "game", ""));
        Assert.Equal("", VanillaGameVariables.SpelledOtherwise("NoSuchGameVariable"));
    }

    [Fact]
    public void A_comparison_the_game_does_not_know_and_an_order_against_nothing_are_flagged()
    {
        Assert.True(Says(About(Check("coins", comparison: "bigger than", value: "3")), "condition.unknownComparison", "bigger than"));
        Assert.Empty(About(Check("coins", comparison: "greater than", value: "3")));    // the control

        Assert.True(Says(About(Check("coins", comparison: "less than", value: " ")), "condition.paramValueMissing"));
        // Equal to nothing is a fair question - is the text empty?
        Assert.False(Says(About(Check("met", value: "")), "condition.paramValueMissing"));
    }

    [Fact]
    public void A_variable_named_in_the_value_is_checked_in_the_store_it_reads()
    {
        Assert.True(Says(About(Check("coins", comparison: "greater than", value: "$cions")), "condition.undeclaredVariable", "cions"));
        Assert.Empty(About(Check("coins", comparison: "greater than", value: "$coins")));
        Assert.True(Says(About(Check("coins", comparison: "greater than", value: "$NoSuchGameVariable", valueSource: "vanilla")),
                         "condition.unknownVanillaVariable", "NoSuchGameVariable"));
        // A price is not a variable: a name cannot start with a digit.
        Assert.Empty(About(Check("coins", comparison: "greater than", value: "$1100")));
    }
}
