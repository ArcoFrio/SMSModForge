using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.Validation;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The Play dialogue action in the editor (asked for testing a pack,
/// 2026-11-01): what Validate says of what it names, a rename following it,
/// and what it sets counted among a variable's values.
/// </summary>
public sealed class PlayDialogueTests
{
    private static NodeActionDef Play(params (string, string)[] ps) => InGameSuitePacks.Act(NodeActionTypes.PlayDialogue, ps);

    private static ModPack Pack(params NodeActionDef[] actions)
    {
        var pack = PackRepository.CreateEmpty("plays");
        pack.Dialogues.Add(new DialogueDef { Key = "target", DisplayName = "target" });
        var rule = new UpdateRuleDef { Key = "r" };
        rule.Actions.AddRange(actions);
        pack.IntegrationRules.Add(rule);
        return pack;
    }

    private static List<ValidationIssue> Check(ModPack pack)
    {
        var issues = new List<ValidationIssue>();
        VariableChecks.Check(pack, issues);
        return issues.Where(i => i.Code.StartsWith("action.playDialogue")).ToList();
    }

    [Fact]
    public void What_it_names_is_checked_against_the_packs_dialogues_and_the_games_conversations()
    {
        string conversation = VanillaDialogueCatalog.All.First().Id;
        var pack = Pack(
            Play(("source", "pack"), ("dialogue", "target")),                 // fine
            Play(("dialogue", "TARGET")),                                       // fine: pack by default, any case
            Play(("source", "vanilla"), ("conversation", conversation)),        // fine
            Play(("source", "pack"), ("dialogue", "$which")),                   // read in the game: left alone
            Play(("source", "pack"), ("dialogue", "nobody")),
            Play(("source", "vanilla"), ("conversation", "8_Room_Talk/Nowhere/Nothing")),
            Play(("source", "pack")));
        var codes = Check(pack).Select(i => i.Code).ToList();
        Assert.Equal(new[] { "action.playDialogueUnknownDialogue", "action.playDialogueUnknownConversation", "action.playDialogueNone" }, codes);
    }

    [Fact]
    public void Renaming_the_dialogue_it_plays_renames_it_there_too()
    {
        var play = Play(("source", "pack"), ("dialogue", "target"));
        var other = Play(("source", "vanilla"), ("conversation", "target"));   // control: a game conversation's path is not a key
        var pack = Pack(play, other);
        Assert.Equal(1, ReferenceRenamer.Rename(pack, RefKind.Dialogue, "target", "goal"));
        Assert.Equal("goal", play.Params["dialogue"]);
        Assert.Equal("target", other.Params["conversation"]);
    }

    [Fact]
    public void What_it_sets_when_done_is_one_of_its_variables_values()
    {
        var pack = Pack(Play(("dialogue", "target"), ("afterName", "watched"), ("afterValue", "yes")));
        pack.Variables.Add(InGameSuitePacks.Var("watched", PackVariableType.String, "no"));
        pack.Dialogues[0].StartConditions.Add(InGameSuitePacks.Is("watched", "yes"));
        var issues = new List<ValidationIssue>();
        VariableChecks.Check(pack, issues);
        Assert.DoesNotContain(issues, i => i.Code == "variable.neverSetTo");
        // Control: a value it does not set is still said.
        pack.Dialogues[0].StartConditions.Add(InGameSuitePacks.Is("watched", "twice"));
        issues.Clear();
        VariableChecks.Check(pack, issues);
        Assert.Contains(issues, i => i.Code == "variable.neverSetTo");
    }
}
