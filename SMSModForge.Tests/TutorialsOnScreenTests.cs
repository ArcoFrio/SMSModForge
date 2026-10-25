using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using SMSModForge.Services;
using SMSModForge.Tutorials;
using SMSModForge.View;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The tutorials as an author follows them, in the real window - in Classic
/// and in EX (the author, before releasing 1.8.0: "did we make sure the
/// tutorials work on EX?").
/// <para/>
/// <see cref="TutorialProgressionTests"/> walks every step against a view model
/// with no window, and <see cref="TutorialShapeTests"/> finds every anchor in
/// the XAML; neither sees what is DRAWN. EX draws the same window with other
/// templates - the tabs in a rail, panels as tiles - so a control a step
/// points at can be there in the XAML and still not be on screen. Here each
/// tutorial runs through the editor's own runner and overlay, step by step,
/// doing what each step asks, and what the overlay lights is measured: the
/// ring round the step's control, on arrival and once the step is done, and
/// the other controls it lights. Classic is the control for EX.
/// <para/>
/// The first run found no difference between the looks, and found what both
/// shared: a control that appears only during a step - a line's editor, once
/// a line is chosen - was never lit, because the overlay looked only as the
/// step began. It looks again now (TutorialOverlay.LookAgain), and
/// <see cref="A_control_that_appears_during_a_step_is_lit_when_it_does"/> holds
/// it to that.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window per tutorial and look; see CLAUDE.md
public sealed class TutorialsOnScreenTests
{
    private readonly ITestOutputHelper _out;
    public TutorialsOnScreenTests(ITestOutputHelper o) => _out = o;

    public static IEnumerable<object[]> Tutorials()
        => TutorialCatalog.All.Where(t => t.IsOnLadder).Select(t => new object[] { t.Id });

    /// <summary>What the overlay lit on one step: the ring on arrival and once
    /// the step was done, and the step's other controls lit by then.</summary>
    private sealed record Lit(string Step, string Anchor, bool RingOnArrival, bool RingWhenDone, string[] Also);

    [Theory]
    [MemberData(nameof(Tutorials))]
    public void Every_step_lights_its_control_in_Classic_and_in_EX(string tutorialId)
    {
        var tutorial = TutorialCatalog.All.First(t => t.Id == tutorialId);
        var classic = Follow(tutorial, ThemeManager.All.First(t => t.Name == "Light"));
        var ex = Follow(tutorial, ThemeManager.All.First(t => t.Name == "Neon"));

        var trouble = new List<string>();
        if (classic.Count < tutorial.Steps.Count) trouble.Add($"Classic got stuck at step {classic.Count}");
        if (ex.Count < tutorial.Steps.Count) trouble.Add($"EX got stuck at step {ex.Count}");
        for (int i = 0; i < Math.Min(classic.Count, ex.Count); i++)
        {
            var (c, e) = (classic[i], ex[i]);
            _out.WriteLine($"[{i}] {c.Step}  '{c.Anchor}': Classic {Say(c)} | EX {Say(e)}");
            if (c.Anchor.Length == 0) continue;
            if (!e.RingOnArrival && !e.RingWhenDone) trouble.Add($"[{i}] \"{e.Step}\": EX never lights '{e.Anchor}'");
            if (!c.RingOnArrival && !c.RingWhenDone) trouble.Add($"[{i}] \"{c.Step}\": Classic never lights '{c.Anchor}'");
            if (c.RingOnArrival && !e.RingOnArrival) trouble.Add($"[{i}] \"{e.Step}\": EX does not light '{e.Anchor}' on arrival, Classic does");
            foreach (var id in c.Also.Except(e.Also))
                trouble.Add($"[{i}] \"{e.Step}\": EX does not light '{id}', which Classic does");
        }
        Assert.True(trouble.Count == 0, $"'{tutorial.Title}':\n  " + string.Join("\n  ", trouble));
    }

    [Fact]
    public void A_control_that_appears_during_a_step_is_lit_when_it_does()
    {
        // "Show your variable in a line" lights the line's editor, which is
        // only drawn once a line is chosen. Arriving with none chosen, the ring
        // has nothing to go round (the control); choosing one, as the lit list
        // lets an author do, has to light it.
        var tutorial = TutorialCatalog.All.First(t => t.Id == "values-in-text");
        int stepAt = tutorial.Steps.ToList().FindIndex(s => s.Anchor == "panel:nodeEditor" && s.IsDone != null);
        Assert.True(stepAt >= 0, "the step that lights the line's editor has moved");

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var ring = Ring(window);
            using var walker = new TutorialWalker(vm);
            vm.AddPlaceCommand.Execute(null);
            vm.AddDialogueCommand.Execute(null);
            vm.AddDialogueRootNodeCommand.Execute(null);
            var line = vm.SelectedNode!;
            vm.SelectedNode = null;

            vm.TutorialRunner.Start(tutorial);
            for (int i = 0; i < stepAt; i++)
            {
                Settle(window, 0);
                vm.TutorialRunner.NextCommand.Execute(null);   // the Read steps before it
            }
            Settle(window, 0.4);
            Assert.Equal(stepAt, vm.TutorialRunner.Index);
            Assert.False(Shows(ring), "a ring with no line chosen - the control does not hold");

            vm.SelectedNode = line;
            Settle(window, 0.6);
            Assert.True(Shows(ring), "the line's editor appeared and was not lit");
            vm.TutorialRunner.Stop();
        });
    }

    private static string Say(Lit l)
        => l.Anchor.Length == 0 ? "no control" + (l.Also.Length > 0 ? $", also {l.Also.Length}" : "")
           : (l.RingOnArrival ? "lit" : "not yet") + " -> " + (l.RingWhenDone ? "lit" : "NEVER")
             + (l.Also.Length > 0 ? $", also {l.Also.Length}" : "");

    private static FrameworkElement Ring(Window window)
        => (FrameworkElement)((TutorialOverlay)window.FindName("Tutorial")).FindName("Ring");

    private static bool Shows(FrameworkElement ring) => ring.IsVisible && ring.ActualWidth > 0 && ring.ActualHeight > 0;

    /// <summary>Run one tutorial in a fresh window in one look, doing what each
    /// step asks, and say what was lit at each.</summary>
    private List<Lit> Follow(TutorialDef tutorial, ThemeDef theme)
    {
        var lit = new List<Lit>();
        WindowHarness.Run(window =>
        {
            var was = ThemeManager.Current;
            ThemeManager.Apply(theme);
            try
            {
                var vm = (MainViewModel)window.DataContext;
                var runner = vm.TutorialRunner;
                var ring = Ring(window);
                var tabs = (System.Windows.Controls.TabControl)window.FindName("MainTabs");
                using var walker = new TutorialWalker(vm);

                runner.Start(tutorial);
                for (int i = 0; i < tutorial.Steps.Count; i++)
                {
                    Settle(window, 0);
                    var step = runner.Step!;
                    bool onArrival = Shows(ring);
                    bool whenDone = onArrival;

                    if (step.IsDone != null
                        && TutorialSolutions.All.TryGetValue(TutorialSolutions.Key(tutorial.Id, step.Title), out var solve))
                    {
                        solve(walker);
                        // The runner and the overlay both look every quarter second.
                        var deadline = DateTime.Now.AddSeconds(3);
                        while (!runner.CanAdvance && DateTime.Now < deadline) WindowHarness.Pump();
                        // Back on the step's own tab, where its author is: what a
                        // walk builds to get there (a character for a room) can
                        // move the window to another.
                        if (step.Tab >= 0) tabs.SelectedIndex = step.Tab;
                        Settle(window, 0.4);
                        whenDone = Shows(ring);
                    }
                    var also = step.AlsoAllow.Where(id => id.Length > 0 && TutorialAnchor.Find(window, id) != null).ToArray();
                    lit.Add(new Lit(step.Title, step.Anchor, onArrival, whenDone, also));

                    if (!runner.CanAdvance) break;     // stuck: what was lit so far is the answer
                    runner.NextCommand.Execute(null);
                }
                if (runner.IsRunning) runner.Stop();
            }
            finally { ThemeManager.Apply(was); }
        });
        return lit;
    }

    /// <summary>Let a step arrive or a change land: laid out, the overlay's
    /// deferred measuring done, and - for <paramref name="seconds"/> - its
    /// timers given the time to look.</summary>
    private static void Settle(Window window, double seconds)
    {
        var until = DateTime.Now.AddSeconds(seconds);
        do
        {
            for (int i = 0; i < 4; i++)
            {
                WindowHarness.Pump();
                window.UpdateLayout();
            }
        }
        while (DateTime.Now < until);
    }
}
