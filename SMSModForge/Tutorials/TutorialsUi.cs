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
            Group = "UI",
            Title = "A screen of your own",
            Summary = "Build a panel, put something on it, and see it against the game's canvas.",
            Level = 15,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "Screens the pack owns",
                    Body = "This tab is for anything drawn on top of the game rather than in it " +
                           "— a window, a panel, a bar in the corner. Two kinds live here " +
                           "together, and deliberately so: screens you invent, and screens the " +
                           "game already has that your pack changes.\n\n" +
                           "They share a tab because from your side they are the same job. A " +
                           "tree of objects on the left, what the selected one is underneath, " +
                           "and a picture of the result on the right.",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                },
                new TutorialStep
                {
                    Title = "Start one",
                    Body = "Press + UI. That gives you a blank panel — the plainest shape there " +
                           "is, and the one to start from when you do not yet know what you are " +
                           "making. The arrow beside the button offers the others: a window with " +
                           "a title bar and a close button, a dialog that asks something, a list, " +
                           "a tooltip.\n\n" +
                           "It arrives with a name and an id already filled in. The id is what " +
                           "everything else in the pack uses to point at this screen, so it stays " +
                           "put even when you rename the thing.",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "btn:addUiExtension",
                    OnEnter = (vm, s) => s.Set("uis", vm.Uis.Count),
                    IsDone = (vm, s) => s.GrewSince("uis", vm.Uis.Count),
                    Hint = "+ UI, at the top of the list on the left.",
                },
                new TutorialStep
                {
                    Title = "Call it something",
                    Body = "Change the Name to something you will recognise in a month — " +
                           "\"Quest note\" rather than \"Blank panel 2\". This is the name you " +
                           "will be picking out of a list every time something needs to open it.\n\n" +
                           "The id underneath does not change with it, and should not: renaming " +
                           "a screen must never quietly unhook whatever was opening it.",
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
                    Hint = "The Name box, top of the middle column.",
                },
                new TutorialStep
                {
                    Title = "Put something on it",
                    Body = "The tree is what the screen is made of. Select the panel and press " +
                           "+ Object to put an empty object inside it; the arrow beside that " +
                           "button offers ready-made pieces instead — a button, a row, a line of " +
                           "text.\n\n" +
                           "Everything is inside something else, and that is what decides where " +
                           "it sits: an object is positioned against its parent, so moving the " +
                           "panel takes everything on it along.",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "btn:addUiObject",
                    AlsoAllow = new[] { "tree:uiObjects" },
                    OnEnter = (vm, s) => s.Set("objects", ObjectsIn(vm.SelectedUi)),
                    IsDone = (vm, s) => s.GrewSince("objects", ObjectsIn(vm.SelectedUi)),
                    Hint = "Select the panel in the tree first — + Object adds INSIDE whatever "
                           + "is selected.",
                },
                new TutorialStep
                {
                    Title = "Say what it is",
                    Body = "With the new object selected, the box underneath is everything about " +
                           "it: its name, where it sits, how big it is, and what it draws.\n\n" +
                           "Position is measured from the object's anchors, in canvas pixels, " +
                           "and the canvas is the whole screen. Give it a name and a size you " +
                           "can see — a few hundred pixels each way is a good start, since " +
                           "anything smaller is hard to find in the preview.",
                    Kind = StepKind.Do,
                    Tab = TabUi,
                    Anchor = "panel:uiObjectDetail",
                    AlsoAllow = new[] { "tree:uiObjects" },
                    OnEnter = (vm, s) => s.Set("named", vm.SelectedUi?.SelectedNode?.Name ?? ""),
                    IsDone = (vm, s) => vm.SelectedUi?.SelectedNode is { } node
                                        && !string.IsNullOrWhiteSpace(node.Name)
                                        && node.Name != s.Get<string>("named")
                                        && node.Width > 0 && node.Height > 0,
                    Hint = "Select the object you just added, then fill in Name and Size.",
                },
                new TutorialStep
                {
                    Title = "What the picture is telling you",
                    Body = "The preview draws your screen against the game's own canvas at the " +
                           "size the game uses, with the selected object picked out. It is not " +
                           "an approximation of the layout — it is the layout, which is why an " +
                           "object off the edge here is off the edge in the game.\n\n" +
                           "A screen built on one of the game's own shows that screen underneath " +
                           "your changes, so you can see what you are altering rather than " +
                           "guessing at it.",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                    Anchor = "preview:ui",
                },
                new TutorialStep
                {
                    Title = "Making it appear",
                    Body = "A screen you build starts switched off, the same way a scene does. " +
                           "Something has to turn it on, and it is the action you already know: " +
                           "Set Active, with its category set to UI and your screen named as the " +
                           "target.\n\n" +
                           "Put that on a dialogue node, or in a rule, or on a button of your " +
                           "own — anywhere an action can go. Switching it off again is the same " +
                           "action with Active unticked.",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                },
                new TutorialStep
                {
                    Title = "Changing one the game already has",
                    Body = "+ Vanilla is the other half of this tab. Choose one of the game's " +
                           "screens and the tree fills with what is actually in it, ready to be " +
                           "moved, hidden, relabelled or added to.\n\n" +
                           "Only your differences are stored. An object you never touch is not " +
                           "in your pack at all, which is why Reset on an object makes it stop " +
                           "being a change and drop out — and why a pack that alters one label " +
                           "on the shop stays a pack that alters one label, rather than a copy " +
                           "of the shop that will not survive the game being patched.",
                    Kind = StepKind.Read,
                    Tab = TabUi,
                    Anchor = "btn:addUiExtension",
                },
            },
        },
    };
}
