using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;

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
    private const int TabUi = MainTab.Ui;

    /// <summary>Her portfolio, the screen of tutorial 15. English on purpose: a screen's name.</summary>
    internal const string PortfolioName = "Portfolio";

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
            Makes = "tutorial.aScreenOfYourOwn.makes",
            Level = 16,
            // 2 (1.10.0): "Dummy Days" - her portfolio; short steps with fold-outs. 3: her cover's picture.
            Revision = 3,
            Leaves = vm => vm.Uis.Any(u => u.Name.Trim() == PortfolioName),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.screensPackOwns.title",
                    Body = "tutorial.aScreenOfYourOwn.screensPackOwns.body",
                    More = "tutorial.aScreenOfYourOwn.screensPackOwns.more",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.startOne.title",
                    Body = "tutorial.aScreenOfYourOwn.startOne.body",
                    More = "tutorial.aScreenOfYourOwn.startOne.more",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "btn:addUiExtension",
                    OnEnter = (vm, s) => s.Set("uis", vm.Uis.Count),
                    IsDone = (vm, s) => s.GrewSince("uis", vm.Uis.Count),
                    Solve = (vm, s) => vm.AddOwnUiCommand.Execute(null),
                    Hint = "tutorial.aScreenOfYourOwn.startOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.callSomething.title",
                    Body = "tutorial.aScreenOfYourOwn.callSomething.body",
                    More = "tutorial.aScreenOfYourOwn.callSomething.more",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "panel:uiDetail",
                    OnEnter = (vm, s) => s.Set("name", vm.SelectedUi?.Name ?? ""),
                    // Against what it arrived as: a new screen is named the moment it is made.
                    IsDone = (vm, s) => vm.SelectedUi is { } ui
                                        && !string.IsNullOrWhiteSpace(ui.Name)
                                        && ui.Name != s.Get<string>("name"),
                    Solve = (vm, s) => vm.SelectedUi!.Name = PortfolioName,
                    Hint = "tutorial.aScreenOfYourOwn.callSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.putSomething.title",
                    Body = "tutorial.aScreenOfYourOwn.putSomething.body",
                    More = "tutorial.aScreenOfYourOwn.putSomething.more",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "btn:addUiObject",
                    AlsoAllow = new[] { "tree:uiObjects" },
                    OnEnter = (vm, s) => s.Set("objects", ObjectsIn(vm.SelectedUi)),
                    IsDone = (vm, s) => s.GrewSince("objects", ObjectsIn(vm.SelectedUi)),
                    Solve = (vm, s) =>
                    {
                        // Inside the panel the screen came with: + Object adds into what is selected.
                        var ui = vm.SelectedUi!;
                        ui.SelectedNode = ui.Nodes[0];
                        ui.AddChildCommand.Execute(null);
                    },
                    Hint = "tutorial.aScreenOfYourOwn.putSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.say.title",
                    Body = "tutorial.aScreenOfYourOwn.say.body",
                    More = "tutorial.aScreenOfYourOwn.say.more",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "panel:uiObjectDetail",
                    AlsoAllow = new[] { "tree:uiObjects" },
                    OnEnter = (vm, s) => s.Set("named", vm.SelectedUi?.SelectedNode?.Name ?? ""),
                    IsDone = (vm, s) => vm.SelectedUi?.SelectedNode is { } node
                                        && !string.IsNullOrWhiteSpace(node.Name)
                                        && node.Name != s.Get<string>("named")
                                        && node.Width > 0 && node.Height > 0,
                    Solve = (vm, s) =>
                    {
                        var ui = vm.SelectedUi!;
                        var made = ui.Nodes[0].Children[^1];
                        ui.SelectedNode = made;
                        made.Name = "Cover";   // English on purpose: an object's name.
                        made.Width = 400;
                        made.Height = 300;
                    },
                    Hint = "tutorial.aScreenOfYourOwn.say.hint",
                },
                new TutorialStep
                {
                    // Her cover (1.10.0): the portfolio had a cover with nothing on it.
                    Title = "tutorial.aScreenOfYourOwn.cover.title",
                    Body = "tutorial.aScreenOfYourOwn.cover.body",
                    More = "tutorial.aScreenOfYourOwn.cover.more",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "panel:uiObjectDetail",
                    AlsoAllow = new[] { "tree:uiObjects", "preview:ui" },
                    IsDone = (vm, s) => vm.Uis.Any(u => u.Name.Trim() == PortfolioName &&
                                                        Flat(u.Nodes).Any(n => n.Sprite.Trim().Length > 0 && n.Name == "Cover")),
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        var ui = vm.SelectedUi!;
                        var cover = Flat(ui.Nodes).FirstOrDefault(n => n.Name == "Cover") ?? ui.Nodes[0].Children[^1];   // English on purpose: an object's name.
                        ui.SelectedNode = cover;
                        cover.Sprite = TutorialAssets.Cover;
                    },
                    Hint = "tutorial.aScreenOfYourOwn.cover.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.pictureTelling.title",
                    Body = "tutorial.aScreenOfYourOwn.pictureTelling.body",
                    More = "tutorial.aScreenOfYourOwn.pictureTelling.more",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                    Anchor = "preview:ui",
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.makingAppear.title",
                    Body = "tutorial.aScreenOfYourOwn.makingAppear.body",
                    More = "tutorial.aScreenOfYourOwn.makingAppear.more",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                },
                new TutorialStep
                {
                    Title = "tutorial.aScreenOfYourOwn.changingOneGameAlready.title",
                    Body = "tutorial.aScreenOfYourOwn.changingOneGameAlready.body",
                    More = "tutorial.aScreenOfYourOwn.changingOneGameAlready.more",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                    Anchor = "btn:addVanillaUi",
                },
            },
        },
    };

    /// <summary>Every object of a screen, inside others too.</summary>
    private static IEnumerable<UiNodeViewModel> Flat(IEnumerable<UiNodeViewModel> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Flat(n.Children)) yield return c;
        }
    }
}
