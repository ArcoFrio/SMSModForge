using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The checks that know every value a pack's variable can hold (found testing
/// a pack in the game, 2026-11-01): a comparison nothing in the pack can make
/// match, and lines whose outfit follows a variable that can name no outfit.
/// </summary>
public sealed class VariableChecksTests
{
    private static NodeActionDef Set(string name, string value)
        => InGameSuitePacks.Act(NodeActionTypes.SetVariable, ("name", name), ("value", value));

    private static List<ValidationIssue> Check(ModPack pack)
    {
        var issues = new List<ValidationIssue>();
        VariableChecks.Check(pack, issues);
        return issues;
    }

    private static ModPack Pack(params PackVariableDef[] variables)
    {
        var pack = PackRepository.CreateEmpty("checks");
        pack.Variables.AddRange(variables);
        return pack;
    }

    /// <summary>A rule that runs the actions, so they are somewhere in the pack.</summary>
    private static void Actions(ModPack pack, params NodeActionDef[] actions)
    {
        var rule = new UpdateRuleDef { Key = "r" + pack.IntegrationRules.Count };
        rule.Actions.AddRange(actions);
        pack.IntegrationRules.Add(rule);
    }

    private static DialogueDef Talk(ModPack pack, string key, params NodeConditionDef[] start)
    {
        var d = new DialogueDef { Key = key, DisplayName = key };
        d.StartConditions.AddRange(start);
        pack.Dialogues.Add(d);
        return d;
    }

    private static NodeConditionDef Compared(string name, string value) => InGameSuitePacks.Is(name, value);

    [Fact]
    public void A_comparison_with_a_value_nothing_sets_is_named_with_the_values_that_are()
    {
        var pack = Pack(InGameSuitePacks.Var("mode", PackVariableType.String, "Default"));
        Actions(pack, Set("mode", "AnisBase"), Set("mode", "AnisSwim"));
        Talk(pack, "renamed", Compared("mode", "Swim"));
        Talk(pack, "fine", Compared("mode", "AnisSwim"));          // control: set by an action
        Talk(pack, "start", Compared("mode", "Default"));          // control: its default

        var issue = Assert.Single(Check(pack));
        Assert.Equal("variable.neverSetTo", issue.Code);
        Assert.Equal("dialogues[renamed]", issue.Where);
        Assert.Contains("'Swim'", issue.Message);
        Assert.Contains("'Default', 'AnisBase', 'AnisSwim'", issue.Message);
    }

    [Fact]
    public void A_flag_nothing_ever_sets_is_named_and_one_compared_by_its_type_is_not()
    {
        var pack = Pack(InGameSuitePacks.Var("gifted", PackVariableType.Bool, "false"),
                        InGameSuitePacks.Var("count", PackVariableType.Int, "0"),
                        InGameSuitePacks.Var("done", PackVariableType.Bool, "false"));
        Actions(pack, Set("count", "3"), Set("done", "True"));
        Talk(pack, "gift", Compared("gifted", "true"));            // nothing sets it: the soft-lock
        Talk(pack, "three", Compared("count", "3.0"));             // a number, by value
        Talk(pack, "done", Compared("done", "TRUE"));              // true/false, any case
        var issue = Assert.Single(Check(pack));
        Assert.Equal("dialogues[gift]", issue.Where);
    }

    [Fact]
    public void Nothing_is_said_of_a_variable_the_pack_writes_in_ways_not_written_down()
    {
        var pack = Pack(InGameSuitePacks.Var("counted", PackVariableType.Int, "0"),
                        InGameSuitePacks.Var("copied", PackVariableType.String, ""),
                        InGameSuitePacks.Var("Location_Anis", PackVariableType.String, ""),
                        InGameSuitePacks.Var("other", PackVariableType.String, "x"));
        var rolled = InGameSuitePacks.Var("rolled", PackVariableType.Int, "0");
        rolled.RefreshMode = PackVariableRefreshMode.DailyRandom;
        pack.Variables.Add(rolled);
        Actions(pack,
            InGameSuitePacks.Add("counted"),
            Set("copied", "$other"),
            Set("Location_{char}", "Lab"));                      // a rule's placeholder
        Talk(pack, "a", Compared("counted", "7"));
        Talk(pack, "b", Compared("copied", "y"));
        Talk(pack, "c", Compared("Location_Anis", "Beach"));
        Talk(pack, "d", Compared("rolled", "42"));
        Talk(pack, "e", Compared("Location_{char}", "Beach"));   // a name built from a placeholder
        // The game's own variables are the game's to set.
        var game = Compared("other", "never");
        game.Params["source"] = "vanilla";
        Talk(pack, "f", game);
        Assert.Empty(Check(pack));
        // Control: the same comparison on a variable written down is said.
        Talk(pack, "g", Compared("other", "never"));
        Assert.Equal("dialogues[g]", Assert.Single(Check(pack)).Where);
    }

    [Fact]
    public void A_pick_gives_its_list_and_its_fallback_and_a_list_check_knows_what_was_added()
    {
        var pack = Pack(InGameSuitePacks.Var("picked", PackVariableType.String, ""),
                        InGameSuitePacks.Var("guests", PackVariableType.List, "[\"Anis\"]"),
                        InGameSuitePacks.Var("empty", PackVariableType.List, ""));
        Actions(pack,
            InGameSuitePacks.Act(NodeActionTypes.PickRandomFromList, ("source", "Beach, Lab"), ("target", "picked"), ("fallback", "Home")),
            InGameSuitePacks.Act(NodeActionTypes.AddToList, ("list", "guests"), ("value", "Rapi")));
        Talk(pack, "beach", Compared("picked", "Beach"));
        Talk(pack, "home", Compared("picked", "Home"));
        Talk(pack, "pool", Compared("picked", "Pool"));
        NodeConditionDef Contains(string list, string value) => new()
        {
            Type = NodeConditionTypes.ListContains, Params = { ["list"] = list, ["value"] = value },
        };
        Talk(pack, "anis", Contains("guests", "Anis"));
        Talk(pack, "rapi", Contains("guests", "Rapi"));
        Talk(pack, "neon", Contains("guests", "Neon"));
        Talk(pack, "nobody", Contains("empty", "Neon"));

        var issues = Check(pack).OrderBy(i => i.Where).ToList();
        Assert.Equal(new[] { "dialogues[neon]", "dialogues[nobody]", "dialogues[pool]" }, issues.Select(i => i.Where));
        Assert.Contains("'Anis', 'Rapi'", issues[0].Message);
        Assert.Equal("list.neverHolds", issues[1].Code);
        Assert.DoesNotContain("''", issues[1].Message);   // nothing ever in it: said so, not listed
    }

    [Fact]
    public void Lines_whose_outfit_follows_a_variable_that_can_name_no_outfit_are_said_once_a_conversation()
    {
        var pack = Pack(InGameSuitePacks.Var("look", PackVariableType.String, "Default"),
                        InGameSuitePacks.Var("good", PackVariableType.String, "AnnBase"));
        var ann = new CharacterDef { Key = "ann", DisplayName = "Ann", Pronouns = Pronouns.Female };
        ann.Outfits.Add(new OutfitDef { Key = "base", GameObjectName = "AnnBase" });
        ann.Outfits.Add(new OutfitDef { Key = "swim", GameObjectName = "AnnSwim" });
        pack.Characters.Add(ann);
        Actions(pack, Set("look", "annswim"), Set("good", "AnnSwim"), Set("good", ""));   // any case; empty keeps her look

        foreach (var key in new[] { "one", "two" })
        {
            var d = Talk(pack, key);
            for (int id = 1; id <= 3; id++)
                d.Nodes.Add(new DialogueNodeDef { Id = id, Actor = "ann", Outfit = "$look", Text = "Hi." });
            d.Nodes.Add(new DialogueNodeDef { Id = 4, Actor = "ann", Outfit = "$good", Text = "Fine." });   // control
            d.RootNodeIds.Add(1);
        }

        var issues = Check(pack);
        Assert.Equal(new[] { "dialogues[one].nodes[id=1].outfit", "dialogues[two].nodes[id=1].outfit" },
                     issues.Select(i => i.Where));
        Assert.All(issues, i => Assert.Equal("node.outfitVariableNotOutfit", i.Code));
        Assert.Contains("'Default'", issues[0].Message);
        Assert.DoesNotContain("annswim", issues[0].Message);
        Assert.Contains("Ann", issues[0].Message);
    }

    [Fact]
    public void One_of_the_games_characters_is_checked_against_the_games_own_busts()
    {
        var pack = Pack(InGameSuitePacks.Var("annaLook", PackVariableType.String, "NotABust"));
        var d = Talk(pack, "anna");
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "Anna", Outfit = "$annaLook", Text = "Hi." });
        d.RootNodeIds.Add(1);
        Assert.Equal("node.outfitVariableNotOutfit", Assert.Single(Check(pack)).Code);

        // Control: one of Anna's own busts, as the game names it.
        pack.Variables[0].DefaultValue = Shared.VanillaCastData.Busts
            .First(b => Shared.VanillaCastData.KeyFor(Shared.VanillaCastData.SpokenName(b.Character)).Equals("Anna", System.StringComparison.OrdinalIgnoreCase)).GoName;
        Assert.Empty(Check(pack));
    }
}
