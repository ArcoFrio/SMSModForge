using System.Linq;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Quests tab's two lists: the pack's own quests in the folder tree, and
/// its entries about the game's quests in a list of their own - the same split
/// the Places and Dialogues tabs make.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class QuestSidebarTests
{
    private readonly ITestOutputHelper _out;
    public QuestSidebarTests(ITestOutputHelper o) => _out = o;

    private const int TabQuests = 13;

    private static VanillaQuests.VanillaQuest Game => VanillaQuests.All.First(q => q.Tasks.Count >= 2);

    private static (MainViewModel Vm, QuestViewModel Mine, QuestViewModel Theirs) TwoKinds()
    {
        var vm = new MainViewModel();
        vm.AddQuestCommand.Execute(null);
        var mine = vm.SelectedQuest!;
        vm.AddVanillaQuestCommand.Execute(null);
        var theirs = vm.SelectedQuest!;
        theirs.Source = Game.Name;
        return (vm, mine, theirs);
    }

    private static QuestViewModel[] InTree(MainViewModel vm)
        => vm.QuestTree.FlattenAll().OfType<UnitLeafNode>().Select(l => (QuestViewModel)l.Item).ToArray();

    [Fact]
    public void EachKindGoesInItsOwnList()
    {
        var (vm, mine, theirs) = TwoKinds();

        Assert.Equal(new[] { mine }, InTree(vm));
        Assert.Equal(new[] { theirs }, vm.VanillaQuestEntries);
        Assert.Same(theirs, vm.SelectedVanillaQuest);
        // Both are still quests of the pack, for everything that reads the whole list.
        Assert.Equal(new[] { mine, theirs }, vm.Quests);

        // An entry still waiting for its quest is already one of the game's.
        vm.AddVanillaQuestCommand.Execute(null);
        var waiting = vm.SelectedVanillaQuest!;
        Assert.Equal("", waiting.Model.Source);
        Assert.Contains(waiting, vm.VanillaQuestEntries);
        Assert.DoesNotContain(waiting, InTree(vm));
        Assert.Equal("(choose one of the game's quests)", waiting.VanillaListName);
    }

    [Fact]
    public void AReopenedPackSplitsTheSameWay()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smsquests-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var made = PackRepository.CreateEmpty("my.pack");
            made.Quests.Add(new QuestDef { Key = "mine", Title = "Mine" });
            made.Quests.Add(new QuestDef { Key = "theirs", Title = "", Source = Game.Name, Description = "Changed." });
            PackRepository.Save(made, dir);

            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(dir);
                WindowHarness.Pump();

                _out.WriteLine("tree: " + string.Join(", ", InTree(vm).Select(q => q.Key))
                               + " | vanilla: " + string.Join(", ", vm.VanillaQuestEntries.Select(q => q.Key)));
                Assert.Equal(new[] { "mine" }, InTree(vm).Select(q => q.Key));
                Assert.Equal(new[] { "theirs" }, vm.VanillaQuestEntries.Select(q => q.Key));
                Assert.Equal("mine", vm.SelectedQuest!.Key);
                Assert.Null(vm.SelectedVanillaQuest);

                // Clearing its quest does not turn it into a quest of the pack's
                // while it is open.
                var reopened = vm.VanillaQuestEntries.Single();
                reopened.Source = "";
                Assert.True(reopened.ShowsVanillaPanel);
                Assert.False(reopened.ShowsOwnPanel);
            });
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { }
        }
    }

    [Fact]
    public void EachRemoveButtonRemovesOnlyItsOwnKind()
    {
        var (vm, mine, theirs) = TwoKinds();

        // The vanilla entry is selected: the tree's Remove does nothing to it.
        Assert.False(vm.RemoveQuestCommand.CanExecute(null));
        Assert.True(vm.RemoveVanillaQuestCommand.CanExecute(null));
        vm.RemoveVanillaQuestCommand.Execute(null);
        Assert.Empty(vm.VanillaQuestEntries);
        Assert.Equal(new[] { mine.Model }, vm.Pack.Quests);
        Assert.Same(mine, vm.SelectedQuest);
        Assert.Null(vm.SelectedVanillaQuest);
        Assert.False(vm.RemoveVanillaQuestCommand.CanExecute(null));

        // And the tree's Remove takes the pack's quest.
        vm.AddVanillaQuestCommand.Execute(null);
        vm.QuestTree.Selected = vm.QuestTree.FindLeaf(mine);
        Assert.Same(mine, vm.SelectedQuest);
        Assert.True(vm.RemoveQuestCommand.CanExecute(null));
        vm.RemoveQuestCommand.Execute(null);
        Assert.Empty(InTree(vm));
        Assert.Single(vm.VanillaQuestEntries);
        Assert.Single(vm.Pack.Quests);
        Assert.True(vm.Pack.Quests[0] != mine.Model);
    }

    [Fact]
    public void ADuplicatedEntryStaysWithItsKind()
    {
        var (vm, mine, theirs) = TwoKinds();
        vm.SelectedTabIndex = TabQuests;

        vm.SelectedVanillaQuest = theirs;
        vm.DuplicateItemCommand.Execute(null);
        Assert.Equal(2, vm.VanillaQuestEntries.Count);
        Assert.Equal(new[] { mine }, InTree(vm));
        Assert.Equal(Game.Name, vm.SelectedVanillaQuest!.Model.Source);

        vm.QuestTree.Selected = vm.QuestTree.FindLeaf(mine);
        vm.DuplicateItemCommand.Execute(null);
        Assert.Equal(2, InTree(vm).Length);
        Assert.Equal(2, vm.VanillaQuestEntries.Count);
    }

    [Fact]
    public void TheVanillaListSaysWhatEachEntryChanges()
    {
        var (_, _, theirs) = TwoKinds();
        Assert.Equal(Game.PlainTitle, theirs.VanillaListName);
        Assert.Equal("unchanged", theirs.ChangeSummary);

        theirs.Description = "Something is off.";
        Assert.Equal("1 other change", theirs.ChangeSummary);

        theirs.VanillaTaskRows[0].IsRemoved = true;
        theirs.SelectedExtensionRow = null;
        theirs.AddTaskCommand.Execute(null);
        theirs.AddSubtaskCommand.Execute(null);
        _out.WriteLine(theirs.ChangeSummary);
        Assert.Equal("2 added, 1 taken out, 1 other change", theirs.ChangeSummary);

        theirs.VanillaTaskRows[1].QuestDescription = "x";
        Assert.Equal("2 added, 1 taken out, 2 other changes", theirs.ChangeSummary);
    }

    [Fact]
    public void TheTwoListsCanBeSwitchedBetweenForEver()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;
            vm.AddQuestCommand.Execute(null);
            var mine = vm.SelectedQuest!;
            vm.AddVanillaQuestCommand.Execute(null);
            var theirs = vm.SelectedQuest!;
            theirs.Source = Game.Name;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var list = (ListBox)window.FindName("VanillaQuestList");
            Assert.Single(list.Items);
            Assert.True(((Button)window.FindName("AddVanillaQuestButton")).IsVisible);
            Assert.True(((Button)window.FindName("RemoveVanillaQuestButton")).IsEnabled);
            Assert.False(((Button)window.FindName("RemoveQuestButton")).IsEnabled,
                         "the tree's Remove is live while one of the game's quests is selected");

            var row = vm.QuestTree.FindLeaf(mine)!;
            for (int round = 0; round < 3; round++)
            {
                vm.QuestTree.Selected = row;
                WindowHarness.Pump();
                Assert.Same(mine, vm.SelectedQuest);
                Assert.Null(list.SelectedItem);
                Assert.True(((GroupBox)window.FindName("VanillaQuestPanel")).IsVisible == false,
                            "the game's quest panel stayed up over a quest of the pack's");

                list.SelectedItem = theirs;
                WindowHarness.Pump();
                Assert.Same(theirs, vm.SelectedQuest);
                Assert.False(row.IsSelected, $"round {round}: the tree kept its row, so there is no way back");
                window.UpdateLayout();
                WindowHarness.Pump();
                Assert.True(((GroupBox)window.FindName("VanillaQuestPanel")).IsVisible);
            }

            // The list names the game's quest, not the entry's key.
            var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(theirs);
            var texts = FindAll<TextBlock>(container).Select(t => t.Text).ToList();
            _out.WriteLine(string.Join(" | ", texts));
            Assert.Contains(Game.PlainTitle, texts);
            Assert.Contains("unchanged", texts);
        });
    }

    private static System.Collections.Generic.IEnumerable<T> FindAll<T>(System.Windows.DependencyObject root)
        where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }
}
