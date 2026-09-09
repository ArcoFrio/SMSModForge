using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Switching between the two lists a sidebar can hold.
/// <para/>
/// Reported from a tutorial recording: with one item under "Your places" and
/// one under "Vanilla extensions", it was impossible to get back to the first.
/// Selection only ever travelled from the control to the view model, so
/// picking the other list cleared the view model and left the row highlighted;
/// clicking that row again was not a change, raised no event, and the author
/// was stuck on a record they had not chosen.
/// <para/>
/// One item in each list is the case that strands somebody, so that is the
/// case these use.
/// </summary>
public sealed class SidebarSelectionTests
{
    private readonly ITestOutputHelper _out;
    public SidebarSelectionTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void APlaceAndAVanillaExtensionCanBeSwitchedBetweenForEver()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.AddPlaceCommand.Execute(null);
            vm.AddVanillaExtensionCommand.Execute(null);
            WindowHarness.Pump();

            var place = Assert.Single(vm.Places);
            var extension = Assert.Single(vm.VanillaExtensions);

            // The tree row standing for that place - what the author clicks.
            var row = vm.PlaceTree.FlattenAll().OfType<UnitLeafNode>()
                        .Single(n => ReferenceEquals(n.Item, place));

            // Click the place, then the extension, then back - three times, so
            // a fix that merely works once does not pass.
            for (int round = 0; round < 3; round++)
            {
                vm.PlaceTree.Selected = row;
                WindowHarness.Pump();
                Assert.Same(place, vm.SelectedPlace);
                Assert.Null(vm.SelectedVanillaExtension);
                Assert.True(row.IsSelected, "the row the author clicked is not highlighted");

                vm.SelectedVanillaExtension = extension;
                WindowHarness.Pump();
                Assert.Same(extension, vm.SelectedVanillaExtension);
                Assert.Null(vm.SelectedPlace);

                // The heart of it: the tree must have LET GO, or clicking the
                // row again is not a change and nothing happens.
                Assert.False(row.IsSelected,
                             $"round {round}: the tree kept its row selected, so there is no way back");
            }
        });
    }

    [Fact]
    public void APackDialogueAndAVanillaOneCanBeSwitchedBetweenForEver()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            var mine = Assert.Single(vm.Dialogues.Where(d => !d.WantsVanilla));

            var extension = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
            extension.VanillaSource =
                VanillaDialogueCatalog.Find("8_Room_Talk/Beach/AnnaBeachDefault");
            vm.Dialogues.Add(extension);
            vm.RefreshVanillaDialogues();
            WindowHarness.Pump();

            var row = vm.DialogueTree.SelectMany(Flatten).OfType<DialogueLeafNode>()
                        .Single(n => ReferenceEquals(n.Dialogue, mine));

            for (int round = 0; round < 3; round++)
            {
                vm.SelectedDialogueTreeItem = row;
                WindowHarness.Pump();
                Assert.Same(mine, vm.SelectedDialogue);
                Assert.Null(vm.SelectedVanillaDialogue);

                vm.SelectedVanillaDialogue = extension;
                WindowHarness.Pump();
                Assert.Same(extension, vm.SelectedDialogue);
                Assert.False(row.IsSelected,
                             $"round {round}: the dialogue tree kept its row, so there is no way back");
            }
        });
    }

    [Fact]
    public void SelectingWithinOneListStillWorksNormally()
    {
        // The control. Releasing the other list must not have broken moving
        // between two items in the SAME list.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.AddPlaceCommand.Execute(null);
            vm.AddPlaceCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal(2, vm.Places.Count);

            var rows = vm.PlaceTree.FlattenAll().OfType<UnitLeafNode>().ToList();
            Assert.Equal(2, rows.Count);

            vm.PlaceTree.Selected = rows[0];
            Assert.True(rows[0].IsSelected);
            Assert.False(rows[1].IsSelected);

            vm.PlaceTree.Selected = rows[1];
            Assert.False(rows[0].IsSelected);
            Assert.True(rows[1].IsSelected);
            Assert.Same(((UnitLeafNode)rows[1]).Item, vm.SelectedPlace);
        });
    }

    private static System.Collections.Generic.IEnumerable<DialogueTreeItem> Flatten(
        DialogueTreeItem item)
    {
        yield return item;
        if (item is DialogueFolderNode folder)
            foreach (var child in folder.Children)
                foreach (var deeper in Flatten(child))
                    yield return deeper;
    }
}
