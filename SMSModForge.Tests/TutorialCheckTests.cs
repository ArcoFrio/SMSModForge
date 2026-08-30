using System;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Tutorials;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Does a step's check actually check what the step asks for?
/// <para/>
/// <see cref="TutorialProgressionTests"/> proves every step can be passed, and
/// that no step is already satisfied on arrival. Neither catches the failure
/// people actually hit: a check loose enough that Next lights up for work that
/// was not done. Both cases below were reported from a real run-through — a
/// condition counted as soon as the row existed, and a "bring the branches back
/// together" step that accepted the jump backwards it should have warned about.
/// <para/>
/// So these are the negatives. Each one arranges the near-miss a person would
/// plausibly produce and insists the step still says no.
/// </summary>
public class TutorialCheckTests
{
    private const string Tutorial = "lines-that-choose";

    /// <summary>
    /// Runs a tutorial's solutions up to <paramref name="stepTitle"/> and hands
    /// back that step, ready to be probed with states the solution never makes.
    /// </summary>
    private static (TutorialWalker Walker, TutorialStep Step, TutorialScratch Scratch)
        Reach(string tutorialId, string stepTitle)
    {
        var tut = TutorialCatalog.All.First(t => t.Id == tutorialId);
        var w = new TutorialWalker();

        foreach (var st in tut.Steps)
        {
            var scratch = new TutorialScratch();
            st.OnEnter?.Invoke(w.Vm, scratch);
            if (st.Title == stepTitle) return (w, st, scratch);
            if (st.IsDone == null) continue;
            TutorialSolutions.All[TutorialSolutions.Key(tutorialId, st.Title)](w);
        }

        w.Dispose();
        throw new InvalidOperationException(
            $"'{tutorialId}' has no step titled \"{stepTitle}\" — the test is out of " +
            "date with the tutorial, which is worth knowing either way.");
    }

    [Fact]
    public void Gating_a_line_wants_the_condition_finished_not_merely_added()
    {
        var (w, step, scratch) = Reach(Tutorial, "Gate it on something real");
        using var _ = w;

        var n = w.Vm.SelectedNode!;

        // A row and nothing else. This is what the old check accepted, and a
        // condition with no type is one that always passes at runtime — the
        // author would have been taught the shape of a gate that does not gate.
        w.Vm.AddNodeConditionCommand.Execute(null);
        Assert.False(step.IsDone!(w.Vm, scratch),
            "an empty condition row passes the step");

        var c = n.Conditions.Last();

        // Right idea, wrong condition: this one asks which level is on screen,
        // not whether an object is switched on.
        c.Model.Type = NodeConditionTypes.LevelActive;
        c.Model.Params["level"] = "place:somewhere";
        Assert.False(step.IsDone!(w.Vm, scratch),
            "a condition of the wrong type passes the step");

        // The type the step names, with the field it adds left blank — the
        // exact half-finished state that was reported.
        c.Model.Type = NodeConditionTypes.GameObjectActive;
        c.Model.Params["path"] = "";
        Assert.False(step.IsDone!(w.Vm, scratch),
            "GameObjectActive with no GO path passes the step");

        c.Model.Params["path"] = "Lamp";
        Assert.True(step.IsDone!(w.Vm, scratch),
            "the finished condition does not pass the step");
    }

    [Fact]
    public void Rejoining_branches_wants_the_jump_to_go_forward()
    {
        var (w, step, scratch) = Reach(Tutorial, "Letting one option say more");
        using var _ = w;

        var d = w.Vm.SelectedDialogue!;

        // Tag on its own, jump on its own: each half is inert, and neither
        // should read as the step being done.
        d.Nodes[^1].Tag = "ending";
        Assert.False(step.IsDone!(w.Vm, scratch), "a tag with no jump passes the step");

        d.Nodes[^1].Tag = "";
        d.Nodes[0].JumpMode = JumpMode.Jump;
        d.Nodes[0].JumpTargetTag = "ending";
        Assert.False(step.IsDone!(w.Vm, scratch), "a jump with no tag passes the step");

        // Both halves, aimed backwards: the last node sent to the first. This
        // is a conversation the player cannot leave, and it is what the step's
        // own instructions used to describe.
        d.Nodes[0].JumpMode = JumpMode.Continue;
        d.Nodes[0].JumpTargetTag = "";
        d.Nodes[0].Tag = "ending";
        d.Nodes[^1].JumpMode = JumpMode.Jump;
        d.Nodes[^1].JumpTargetTag = "ending";
        Assert.False(step.IsDone!(w.Vm, scratch), "a jump back to the first node passes the step");

        // And forwards, which is the arrangement the step teaches.
        d.Nodes[0].Tag = "";
        d.Nodes[^1].JumpMode = JumpMode.Continue;
        d.Nodes[^1].JumpTargetTag = "";
        d.Nodes[^1].Tag = "ending";
        d.Nodes[0].JumpMode = JumpMode.Jump;
        d.Nodes[0].JumpTargetTag = "ending";
        Assert.True(step.IsDone!(w.Vm, scratch), "a forward jump onto a tag does not pass the step");
    }
}
