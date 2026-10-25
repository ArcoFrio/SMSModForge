using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Where a jump lands (the author, 1.8.0: "it should become a warning on
/// Issues"). Game Creator, once a jump lands, says again every plain line
/// above its target (Story.StackFromJump) - seen in the game when the in-game
/// suite's first jump test went round for ever. The validator warns when a
/// target has such a line above it, and says "never ends" when playing the
/// game's rebuilt list brings the jumping line round with nothing to stop it.
/// </summary>
public sealed class JumpLandingValidationTests
{
    private readonly ITestOutputHelper _out;
    public JumpLandingValidationTests(ITestOutputHelper o) => _out = o;

    private static DialogueNodeDef Line(int id, string text, params int[] children)
    {
        var n = new DialogueNodeDef { Id = id, Text = text };
        n.Children.AddRange(children);
        return n;
    }

    private static DialogueNodeDef JumpingTo(DialogueNodeDef n, string tag)
    {
        n.Jump = new JumpDef { Mode = JumpMode.Jump, TargetTag = tag };
        return n;
    }

    /// <summary>The jump warnings on a conversation's lines.</summary>
    private List<ValidationIssue> Jumps(DialogueDef d)
    {
        var pack = PackRepository.CreateEmpty("jumps.pack");
        pack.Dialogues.Add(d);
        var issues = PackValidator.Validate(pack, Path.GetTempPath())
                                  .Where(i => i.Code is "node.jumpLoops" or "node.jumpReplays").ToList();
        foreach (var i in issues) _out.WriteLine($"{i.Code} {i.Where}: {i.Message}");
        return issues;
    }

    /// <summary>The shape from the explanation: "I should go." jumps to "Bye!",
    /// which is under "Oh, one more thing…", which is under the jumping line.</summary>
    private static DialogueDef Goodbye(bool byeAtTheTop)
    {
        var d = new DialogueDef { Key = "goodbye", DisplayName = "goodbye" };
        var go = JumpingTo(Line(1, "I should go.", 2), "bye");
        var one = Line(2, "Oh, one more thing about tomorrow...", byeAtTheTop ? new int[0] : new[] { 3 });
        var bye = Line(3, "Bye!");
        bye.Tag = "bye";
        d.Nodes.AddRange(new[] { go, one, bye });
        d.RootNodeIds.Add(1);
        if (byeAtTheTop) d.RootNodeIds.Add(3);
        return d;
    }

    [Fact]
    public void A_jump_under_the_line_that_jumps_is_said_to_never_end()
    {
        var issue = Assert.Single(Jumps(Goodbye(byeAtTheTop: false)));
        Assert.Equal("node.jumpLoops", issue.Code);
        Assert.Equal(Severity.Warning, issue.Severity);
        Assert.Equal("dialogues[goodbye].nodes[id=1].jump", issue.Where);
        Assert.Contains("Bye!", issue.Message);
        Assert.Contains("Oh, one more thing about tomorrow...", issue.Message);
    }

    [Fact]
    public void A_jump_to_a_top_level_line_is_fine()
        => Assert.Empty(Jumps(Goodbye(byeAtTheTop: true)));      // the control

    [Fact]
    public void Where_a_choice_or_a_condition_could_stop_it_only_the_line_said_again_is_warned_of()
    {
        // A Choice above: the player may go elsewhere when it comes back.
        var d = new DialogueDef { Key = "pick", DisplayName = "pick" };
        var pick = Line(1, "Pick one.", 2, 3);
        pick.Kind = DialogueNodeKind.Choice;
        var goOn = Line(2, "Go on", 4);
        var stay = Line(3, "Stay", 5);
        var off = JumpingTo(Line(4, "Off we go."), "t");
        var plain = Line(5, "Plain.", 6);
        var target = Line(6, "Target.");
        target.Tag = "t";
        d.Nodes.AddRange(new[] { pick, goOn, stay, off, plain, target });
        d.RootNodeIds.Add(1);
        var issue = Assert.Single(Jumps(d));
        Assert.Equal("node.jumpReplays", issue.Code);
        Assert.Contains("Plain.", issue.Message);

        // A condition on the jumping line: it may not run when it comes round.
        var gated = Goodbye(byeAtTheTop: false);
        gated.Nodes[0].Conditions.Add(new NodeConditionDef { Type = NodeConditionTypes.AlwaysTrue });
        Assert.Equal("node.jumpReplays", Assert.Single(Jumps(gated)).Code);
    }

    [Fact]
    public void A_target_straight_under_a_choice_with_nothing_plain_above_is_fine()
    {
        // The menu comes back, which can be the point; no plain line is said again.
        var d = new DialogueDef { Key = "hub", DisplayName = "hub" };
        var pick = Line(1, "Pick one.", 2, 3);
        pick.Kind = DialogueNodeKind.Choice;
        var again = Line(2, "Again", 4);
        var done = Line(3, "Done");
        done.Tag = "done";
        d.Nodes.AddRange(new[] { pick, again, done, JumpingTo(Line(4, "Once more."), "done") });
        d.RootNodeIds.Add(1);
        Assert.Empty(Jumps(d));
    }
}
