using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Tutorials;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Can a tutorial actually be finished?
/// <para/>
/// Walks each one step by step against a real view model, doing what the step
/// asks and checking the step agrees. Failures name the step, so a broken
/// tutorial says where it broke instead of leaving someone to find it by
/// getting stuck.
/// </summary>
public class TutorialProgressionTests
{
    public static IEnumerable<object[]> Tutorials()
        => TutorialCatalog.All.Select(t => new object[] { t.Id });

    [Theory]
    [MemberData(nameof(Tutorials))]
    public void A_tutorial_can_be_completed(string tutorialId)
    {
        var tut = TutorialCatalog.All.First(t => t.Id == tutorialId);
        using var w = new TutorialWalker();
        var trouble = new List<string>();

        for (int i = 0; i < tut.Steps.Count; i++)
        {
            var step = tut.Steps[i];
            string where = $"{tutorialId}[{i}] \"{step.Title}\"";

            var scratch = new TutorialScratch();
            step.OnEnter?.Invoke(w.Vm, scratch);

            if (step.IsDone == null) continue;   // Read: nothing to satisfy

            // 1. Arriving at a step that is already true teaches nothing and
            //    flicks past before it can be read.
            if (step.IsDone(w.Vm, scratch))
            {
                trouble.Add(where + " — already satisfied on arrival; its check " +
                            "does not distinguish 'done' from 'was already true'");
                continue;
            }

            // 2. Doing what it says has to satisfy it.
            if (!TutorialSolutions.All.TryGetValue(
                    TutorialSolutions.Key(tutorialId, step.Title), out var solve))
            {
                trouble.Add(where + " — no solution written, so nobody has " +
                            "checked this step can be passed");
                continue;
            }

            try { solve(w); }
            catch (Exception ex)
            {
                trouble.Add(where + " — doing what it asks threw " +
                            ex.GetType().Name + ": " + ex.Message);
                continue;
            }

            if (!step.IsDone(w.Vm, scratch))
                trouble.Add(where + " — did what it asks and the step still " +
                            "says no; an author here is stuck for good");
        }

        Assert.True(trouble.Count == 0,
            $"'{tut.Title}' cannot be followed to the end:\n  " + string.Join("\n  ", trouble));
    }
}
