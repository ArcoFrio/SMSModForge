using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Who is speaking, carried rather than re-picked.
/// <para/>
/// Two halves of the same complaint. A new ROOT used to start blank, so every
/// strand of a scene meant picking the actor, the expression and the outfit
/// again — while + Child and + Sibling had inherited all three for ages. And
/// naming an actor who is already in the conversation left the previous
/// speaker's expression and outfit sitting on the line, which are keys that
/// mean nothing to the new one.
/// <para/>
/// A root takes the speaker and NOTHING else, which is the part worth pinning:
/// inheriting the whole node would carry a condition, a jump and a timeout
/// written about somewhere else.
/// </summary>
public sealed class SpeakerCarriesOverTests
{
    private readonly ITestOutputHelper _out;
    public SpeakerCarriesOverTests(ITestOutputHelper o) => _out = o;

    private static DialogueViewModel ADialogue() => new(new DialogueDef { Key = "Scene" });

    [Fact]
    public void ANewRootTakesTheSpeakerFromTheLineBeforeIt()
    {
        var d = ADialogue();
        var first = d.AddNode();
        first.Actor = "amber";
        first.Expression = "Angry";
        first.Outfit = "AmberBeach";

        var root = d.AddNode(parentId: null, speakerFrom: first.Model);

        _out.WriteLine($"root: actor '{root.Actor}', expression '{root.Expression}', outfit '{root.Outfit}'");
        Assert.Equal("amber", root.Actor);
        Assert.Equal("Angry", root.Expression);
        Assert.Equal("AmberBeach", root.Outfit);
        Assert.True(d.Model.RootNodeIds.Contains(root.Id), "it is not a root");
    }

    [Fact]
    public void AndNothingElse()
    {
        // The control. A root that inherited the whole node would carry these,
        // and each one of them would be wrong somewhere else in the tree.
        var d = ADialogue();
        var first = d.AddNode();
        first.Actor = "amber";
        first.Model.Tag = "ending";
        first.Model.Timeout = 4.5f;
        first.Model.Duration = NodeDurationMode.Timeout;
        first.Model.Jump = new JumpDef { Mode = JumpMode.Exit };
        first.Model.Conditions.Add(new NodeConditionDef { Type = NodeConditionTypes.AlwaysTrue });
        first.Model.ActionsOnFinish.Add(new NodeActionDef { Type = NodeActionTypes.SetVariable });
        first.Text = "Something.";

        var root = d.AddNode(parentId: null, speakerFrom: first.Model);

        Assert.Equal("amber", root.Actor);
        Assert.Null(root.Model.Tag);
        Assert.Empty(root.Model.Conditions);
        Assert.Empty(root.Model.ActionsOnFinish);
        Assert.Null(root.Model.Jump);
        Assert.Equal("", root.Text);
        Assert.NotEqual(4.5f, root.Model.Timeout);
    }

    [Fact]
    public void NamingAnActorSeenEarlierBringsBackTheirLook()
    {
        var d = ADialogue();
        var one = d.AddNode();
        one.Actor = "amber";
        one.Expression = "Happy";
        one.Outfit = "AmberDay";

        var two = d.AddNode();
        two.Actor = "player";

        var three = d.AddNode();
        three.Actor = "amber";      // she has been on before

        _out.WriteLine($"third line: '{three.Expression}' / '{three.Outfit}'");
        Assert.Equal("Happy", three.Expression);
        Assert.Equal("AmberDay", three.Outfit);
    }

    [Fact]
    public void ItIsTheLastLookSheWore_NotTheFirst()
    {
        // The case the author asked about: an actor who changed partway
        // through. What she is wearing now is what the next line means.
        var d = ADialogue();
        var one = d.AddNode();
        one.Actor = "amber"; one.Expression = "Happy"; one.Outfit = "AmberDay";

        var two = d.AddNode();
        two.Actor = "amber"; two.Expression = "Sad"; two.Outfit = "AmberNight";

        var three = d.AddNode();
        three.Actor = "amber";

        _out.WriteLine($"third line: '{three.Expression}' / '{three.Outfit}'");
        Assert.Equal("Sad", three.Expression);
        Assert.Equal("AmberNight", three.Outfit);
    }

    [Fact]
    public void AFirstAppearanceIsLeftAlone()
    {
        // Nothing to copy, so nothing is claimed — rather than clearing the
        // fields, which would look like the editor throwing work away.
        var d = ADialogue();
        var one = d.AddNode();
        one.Actor = "amber"; one.Expression = "Happy";

        var two = d.AddNode();
        two.Expression = "Angry";
        two.Actor = "josef";        // never been on

        Assert.Equal("Angry", two.Expression);
    }

    [Fact]
    public void ClearingTheActorClaimsNothing()
    {
        var d = ADialogue();
        var one = d.AddNode();
        one.Actor = "amber"; one.Expression = "Happy"; one.Outfit = "AmberDay";

        var two = d.AddNode();
        two.Actor = "amber";
        Assert.Equal("Happy", two.Expression);

        two.Expression = "Sad";
        two.Actor = "";             // nobody named: leave what is there
        Assert.Equal("Sad", two.Expression);
    }

    [Fact]
    public void ALineLooksBackwardsOnly()
    {
        // A later appearance is not "before", however the tree is shaped.
        var d = ADialogue();
        var one = d.AddNode();
        var two = d.AddNode();
        two.Actor = "amber"; two.Expression = "Sad";

        one.Expression = "";
        one.Actor = "amber";        // the FIRST line, nothing earlier than it

        _out.WriteLine($"first line: '{one.Expression}'");
        Assert.Equal("", one.Expression);
    }
}
