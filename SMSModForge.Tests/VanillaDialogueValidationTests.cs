using System.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What validation says about a conversation the pack extends.
/// <para/>
/// The game's own lines are not the author's to answer for, and a screen of
/// warnings about them buries the one warning that is. But "quieter" is only
/// right if the checks still fire on what the author actually did, so every
/// silence asserted here has a matching noise asserted beside it.
/// </summary>
public sealed class VanillaDialogueValidationTests
{
    private readonly ITestOutputHelper _out;
    public VanillaDialogueValidationTests(ITestOutputHelper o) => _out = o;

    private const string Anna = "8_Room_Talk/Beach/AnnaBeachDefault";

    /// <summary>A pack holding one untouched extension of a vanilla
    /// conversation.</summary>
    private static (ModPack Pack, DialogueViewModel Dialogue) Extending()
    {
        var vm = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
        vm.VanillaSource = VanillaDialogueCatalog.Find(Anna);
        vm.Model.Key = "beach";

        var pack = PackRepository.CreateEmpty("test.pack");
        pack.Dialogues.Add(vm.Model);
        return (pack, vm);
    }

    private static string[] Codes(ModPack pack, string prefix)
        => PackValidator.Validate(pack, "")
            .Where(i => i.Code != null && i.Code.StartsWith(prefix))
            .Select(i => i.Code!)
            .ToArray();

    [Fact]
    public void AnUntouchedExtensionIsSilent()
    {
        var (pack, dialogue) = Extending();
        Assert.Equal(118, dialogue.Model.Nodes.Count);

        var complaints = PackValidator.Validate(pack, "")
            .Where(i => i.Where != null && i.Where.Contains("nodes"))
            .ToArray();

        foreach (var c in complaints) _out.WriteLine(c.Code + " @ " + c.Where);

        // Not one of the game's 118 lines is the author's problem.
        Assert.Empty(complaints);
    }

    [Fact]
    public void AnUntouchedExtensionRaisesNothingTheAuthorCannotFix()
    {
        // Wider than the node sweep above, and for a reason: filtering to
        // "nodes" hid an error raised against the DIALOGUE. An extension has
        // no level condition because the room decides where it plays, so
        // "Pick a level" was unanswerable - an error that could not be
        // cleared by doing anything at all.
        var (pack, dialogue) = Extending();

        var raised = PackValidator.Validate(pack, "")
            .Where(i => i.Where != null && i.Where.StartsWith("dialogues"))
            .ToArray();

        foreach (var i in raised) _out.WriteLine($"{i.Severity} {i.Code} @ {i.Where}");
        Assert.Empty(raised);

        // The level is not missing, though - it is simply not the pack's. It
        // is readable beside the lines.
        var gate = Assert.Single(dialogue.VanillaGates);
        Assert.Contains(gate.Children,
                        c => c.Model.Type == NodeConditionTypes.LevelActive);
    }

    [Fact]
    public void ADialogueThePackSchedulesStillHasToNameALevel()
    {
        // The control: a dialogue of the pack's own IS scheduled by the
        // plugin, so it does have to say where.
        var pack = PackRepository.CreateEmpty("test.pack");
        pack.Dialogues.Add(new DialogueDef { Key = "mine" });

        Assert.Contains("dialogue.noStartLevel", Codes(pack, ""));
    }

    [Fact]
    public void ALineTheAuthorChangedIsStillChecked()
    {
        var (pack, dialogue) = Extending();

        // Empty text is the check that reaches the game looking like a crash,
        // so it is the one worth proving still fires. A line carrying actions
        // is deliberately exempt from it, so the one emptied here must have
        // none - otherwise the test would pass for the wrong reason.
        var line = dialogue.Model.Nodes.First(
            n => n.Kind == DialogueNodeKind.Text
                 && !string.IsNullOrWhiteSpace(n.Text)
                 && n.ActionsOnStart.Count == 0
                 && n.ActionsOnFinish.Count == 0);

        Assert.Empty(Codes(pack, "node.emptyTextNode"));   // silent before

        line.Text = "   ";
        Assert.Contains("node.emptyTextNode", Codes(pack, "node."));
    }

    [Fact]
    public void ALineTheAuthorAddedIsStillChecked()
    {
        var (pack, dialogue) = Extending();

        // A brand new line the game has never had: entirely the author's.
        dialogue.Model.Nodes.Add(new DialogueNodeDef
        {
            Id = 987654321,
            Kind = DialogueNodeKind.Choice,     // a menu offering nothing
            Text = "",
        });

        var codes = Codes(pack, "");
        _out.WriteLine(string.Join(", ", codes.Distinct()));

        Assert.Contains("dialogue.choiceWithoutOptions", codes);
    }

    [Fact]
    public void ADialogueThePackWroteIsCheckedInFull()
    {
        // The control for all of the above: nothing here is vanilla, so
        // nothing is exempt.
        var pack = PackRepository.CreateEmpty("test.pack");
        pack.Dialogues.Add(new DialogueDef
        {
            Key = "mine",
            Nodes =
            {
                new DialogueNodeDef { Id = 1, Kind = DialogueNodeKind.Choice, Text = "" },
            },
            RootNodeIds = { 1 },
        });

        Assert.Contains("dialogue.choiceWithoutOptions", Codes(pack, ""));
    }

    [Fact]
    public void AJumpIntoTheGamesOwnLinesIsNotCalledMissing()
    {
        // The trap in exempting nodes: the checks that CROSS-REFERENCE have to
        // keep seeing all of them. A root or a tag that lives on a vanilla
        // line is still there, and reporting it missing would be a new false
        // warning in place of the ones just removed.
        var (pack, dialogue) = Extending();

        var tagged = dialogue.Model.Nodes.First(n => !string.IsNullOrEmpty(n.Tag));
        dialogue.Model.Nodes.Add(new DialogueNodeDef
        {
            Id = 987654321,
            Kind = DialogueNodeKind.Text,
            Text = "Added by the pack.",
            Jump = new JumpDef { Mode = JumpMode.Jump, TargetTag = tagged.Tag! },
        });

        var codes = Codes(pack, "");
        _out.WriteLine(string.Join(", ", codes.Distinct()));

        Assert.DoesNotContain(codes, c => c.Contains("rootIdMissing"));
        Assert.DoesNotContain(codes, c => c.Contains("jump"));
    }
}
