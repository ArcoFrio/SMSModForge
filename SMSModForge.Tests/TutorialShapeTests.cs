using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Tutorials;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// What has to be true of a tutorial before anyone tries to follow it.
/// <para/>
/// None of this opens a window. These are the failures that have actually
/// happened — a step pointing at an anchor that no longer exists, a step whose
/// instructions span two controls but only lights one, a Do step with no way to
/// pass — and every one of them is visible in the data.
/// </summary>
public class TutorialShapeTests
{
    private static IEnumerable<(TutorialDef Tut, TutorialStep Step, int Index)> AllSteps()
    {
        foreach (var t in TutorialCatalog.All)
            for (int i = 0; i < t.Steps.Count; i++)
                yield return (t, t.Steps[i], i);
    }

    private static string Name(TutorialDef t, TutorialStep s, int i)
        => $"{t.Id}[{i}] \"{s.Title}\"";

    // ── Anchors ───────────────────────────────────────────────────────

    [Fact]
    public void Every_anchor_a_step_points_at_exists_in_the_window()
    {
        var declared = Xaml.AnchorIds();
        var missing = new List<string>();

        foreach (var (t, s, i) in AllSteps())
        {
            if (s.Anchor.Length > 0 && !declared.Contains(s.Anchor))
                missing.Add($"{Name(t, s, i)} -> Anchor '{s.Anchor}'");
            foreach (var extra in s.AlsoAllow)
                if (!declared.Contains(extra))
                    missing.Add($"{Name(t, s, i)} -> AlsoAllow '{extra}'");
        }

        Assert.True(missing.Count == 0,
            "A step points at a control that does not exist. The overlay skips a\n" +
            "missing id, so the step lights nothing and cannot be completed:\n  " +
            string.Join("\n  ", missing));
    }

    [Fact]
    public void An_anchor_is_declared_once()
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(
                     Xaml.Text, @"TutorialAnchor\.Id\s*=\s*""([^""]+)"""))
        {
            string id = m.Groups[1].Value;
            seen[id] = seen.TryGetValue(id, out int c) ? c + 1 : 1;
        }

        var dupes = seen.Where(kv => kv.Value > 1).Select(kv => $"{kv.Key} x{kv.Value}").ToList();
        Assert.True(dupes.Count == 0,
            "Two controls share an anchor id, so a step lights whichever the\n" +
            "overlay finds first:\n  " + string.Join("\n  ", dupes));
    }

    // ── Step shape ────────────────────────────────────────────────────

    [Fact]
    public void A_step_that_asks_for_something_can_be_passed()
    {
        var bad = AllSteps()
            .Where(x => x.Step.Kind != StepKind.Read && x.Step.IsDone == null)
            .Select(x => Name(x.Tut, x.Step, x.Index))
            .ToList();

        Assert.True(bad.Count == 0,
            "A Do or Free step with no IsDone never completes — the tutorial\n" +
            "stops there for good:\n  " + string.Join("\n  ", bad));
    }

    [Fact]
    public void A_step_that_only_explains_does_not_wait_for_anything()
    {
        var bad = AllSteps()
            .Where(x => x.Step.Kind == StepKind.Read && x.Step.IsDone != null)
            .Select(x => Name(x.Tut, x.Step, x.Index))
            .ToList();

        Assert.True(bad.Count == 0,
            "A Read step carries an IsDone. Read advances on Next, so the check\n" +
            "is either dead or the step is mislabelled:\n  " + string.Join("\n  ", bad));
    }

    [Fact]
    public void A_step_that_asks_for_something_offers_a_hint()
    {
        var bad = AllSteps()
            .Where(x => x.Step.Kind == StepKind.Do && string.IsNullOrWhiteSpace(x.Step.Hint))
            .Select(x => Name(x.Tut, x.Step, x.Index))
            .ToList();

        Assert.True(bad.Count == 0,
            "A Do step with no Hint has nothing to offer someone who is stuck,\n" +
            "and being stuck is the failure this whole batch exists to catch:\n  " +
            string.Join("\n  ", bad));
    }

    [Fact]
    public void Every_step_says_something()
    {
        var bad = AllSteps()
            .Where(x => string.IsNullOrWhiteSpace(x.Step.Title) ||
                        string.IsNullOrWhiteSpace(x.Step.Body))
            .Select(x => Name(x.Tut, x.Step, x.Index))
            .ToList();

        Assert.True(bad.Count == 0, "Step with no title or no body:\n  " + string.Join("\n  ", bad));
    }

    // ── Tabs ──────────────────────────────────────────────────────────

    [Fact]
    public void A_steps_tab_is_a_real_tab()
    {
        int tabCount = Xaml.TabHeaders().Count;
        var bad = AllSteps()
            .Where(x => x.Step.Tab >= tabCount)
            .Select(x => $"{Name(x.Tut, x.Step, x.Index)} -> Tab {x.Step.Tab} (only {tabCount} tabs)")
            .ToList();

        Assert.True(bad.Count == 0, "Step switches to a tab that is not there:\n  " +
                                    string.Join("\n  ", bad));
    }

    [Fact]
    public void A_step_opens_the_tab_its_anchor_lives_on()
    {
        var where = Xaml.AnchorTabIndex();
        var bad = new List<string>();

        foreach (var (t, s, i) in AllSteps())
        {
            if (s.Anchor.Length == 0 || s.Tab < 0) continue;
            if (!where.TryGetValue(s.Anchor, out int actual)) continue;  // reported elsewhere
            // -1 means the anchor is outside every tab (a menu, the title bar):
            // reachable from anywhere, so any tab is fine.
            if (actual >= 0 && actual != s.Tab)
                bad.Add($"{Name(t, s, i)} opens tab {s.Tab} but '{s.Anchor}' is on tab {actual}");
        }

        Assert.True(bad.Count == 0,
            "A step switches to one tab and then lights a control on another,\n" +
            "so the author sees a dimmed window with nothing lit:\n  " +
            string.Join("\n  ", bad));
    }

    // ── The catalog itself ────────────────────────────────────────────

    [Fact]
    public void Tutorials_have_unique_ids()
    {
        var dupes = TutorialCatalog.All.GroupBy(t => t.Id)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} x{g.Count()}")
            .ToList();

        Assert.True(dupes.Count == 0,
            "Completion is remembered per id, so duplicates tick each other off:\n  " +
            string.Join("\n  ", dupes));
    }

    [Fact]
    public void Tutorials_are_listed_in_the_order_they_should_be_taken()
    {
        // Level is the progression an author sees. Out of order in the catalog
        // means the list reads as a jumble even though each entry is fine.
        // Level 0 is off the ladder (the diagnostic walkthrough) and is left
        // out rather than dragged to the front by a numeric sort.
        var levels = TutorialCatalog.All.Where(t => t.IsOnLadder).Select(t => t.Level).ToList();
        var sorted = levels.OrderBy(l => l).ToList();

        Assert.True(levels.SequenceEqual(sorted),
            "Tutorials are not in Level order: " + string.Join(", ", levels));

        // And they number 1, 2, 3... with no gaps, because the number is shown.
        Assert.True(levels.SequenceEqual(Enumerable.Range(1, levels.Count)),
            "Levels should run 1..n with no gaps or repeats: " + string.Join(", ", levels));
    }

    [Fact]
    public void Every_tutorial_has_a_title_and_a_summary()
    {
        var bad = TutorialCatalog.All
            .Where(t => string.IsNullOrWhiteSpace(t.Title) || string.IsNullOrWhiteSpace(t.Summary))
            .Select(t => t.Id)
            .ToList();

        Assert.True(bad.Count == 0, "Tutorial with no title or summary:\n  " + string.Join("\n  ", bad));
    }
}
