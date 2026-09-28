using System.Collections.Generic;
using SMSModForge.Model;

namespace SMSModForge.Tutorials;

/// <summary>
/// The UI tab: screens the pack owns, and changes to the ones the game has.
/// <para/>
/// Written last, and that is the point — the tab shipped with nothing teaching
/// it, which nobody noticed because every check the tutorials had asked whether
/// a step could be COMPLETED rather than whether a feature had been covered at
/// all. <c>TutorialCoverageTests</c> is the answer to that, and it fails until
/// something here visits the tab.
/// <para/>
/// Kept on one tab on purpose. A UI is only useful once something switches it
/// on, but that is the Set-Active action the Logic run already teaches, and
/// dragging an author back through a dialogue to prove it would make this
/// tutorial about dialogues. The last steps explain the join and leave the
/// doing to the tutorial that owns it.
/// </summary>
internal static class TutorialsUi
{
    private const int TabUi = 12;

    /// <summary>Every object in a screen, however deep. The tree nests, so a
    /// count of the top level would miss the thing an author just added
    /// inside something.</summary>
    private static int ObjectsIn(ViewModel.UiViewModel? ui)
    {
        if (ui == null) return 0;

        int total = 0;
        void Walk(IEnumerable<UiNodeDef> nodes)
        {
            foreach (var node in nodes)
            {
                total++;
                Walk(node.Children);
            }
        }

        Walk(ui.Model.Nodes);
        return total;
    }

    internal static IReadOnlyList<TutorialDef> All { get; } = new[]
    {
        new TutorialDef
        {
            Id = "a-screen-of-your-own",
            Group = "tutorial.group.ui",
            Title = "tutorial.aScreenOfYourOwn.title",
            Summary = "tutorial.aScreenOfYourOwn.summary",
            Level = 15,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.screensPackOwns.title",
                    Body = "tutorial.aScreenOfYourOwn.screensPackOwns.body",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.startOne.title",
                    Body = "tutorial.aScreenOfYourOwn.startOne.body",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "btn:addUiExtension",
                    OnEnter = (vm, s) => s.Set("uis", vm.Uis.Count),
                    IsDone = (vm, s) => s.GrewSince("uis", vm.Uis.Count),
                    Hint = "tutorial.aScreenOfYourOwn.startOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.callSomething.title",
                    Body = "tutorial.aScreenOfYourOwn.callSomething.body",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "panel:uiDetail",
                    OnEnter = (vm, s) => s.Set("name", vm.SelectedUi?.Name ?? ""),
                    // Compared against what it arrived as rather than merely
                    // "not empty": a new screen is named the moment it is made,
                    // so an emptiness check would already be satisfied and the
                    // step would flick past without teaching anything.
                    IsDone = (vm, s) => vm.SelectedUi is { } ui
                                        && !string.IsNullOrWhiteSpace(ui.Name)
                                        && ui.Name != s.Get<string>("name"),
                    Hint = "tutorial.aScreenOfYourOwn.callSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.putSomething.title",
                    Body = "tutorial.aScreenOfYourOwn.putSomething.body",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "btn:addUiObject",
                    AlsoAllow = new[] { "tree:uiObjects" },
                    OnEnter = (vm, s) => s.Set("objects", ObjectsIn(vm.SelectedUi)),
                    IsDone = (vm, s) => s.GrewSince("objects", ObjectsIn(vm.SelectedUi)),
                    Hint = "tutorial.aScreenOfYourOwn.putSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.say.title",
                    Body = "tutorial.aScreenOfYourOwn.say.body",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "panel:uiObjectDetail",
                    AlsoAllow = new[] { "tree:uiObjects" },
                    OnEnter = (vm, s) => s.Set("named", vm.SelectedUi?.SelectedNode?.Name ?? ""),
                    IsDone = (vm, s) => vm.SelectedUi?.SelectedNode is { } node
                                        && !string.IsNullOrWhiteSpace(node.Name)
                                        && node.Name != s.Get<string>("named")
                                        && node.Width > 0 && node.Height > 0,
                    Hint = "tutorial.aScreenOfYourOwn.say.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.pictureTelling.title",
                    Body = "tutorial.aScreenOfYourOwn.pictureTelling.body",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                    Anchor = "preview:ui",
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.makingAppear.title",
                    Body = "tutorial.aScreenOfYourOwn.makingAppear.body",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.changingOneGameAlready.title",
                    Body = "tutorial.aScreenOfYourOwn.changingOneGameAlready.body",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                    Anchor = "btn:addUiExtension",
                },
            },
        },
    };
}
