using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The notes beside dropdown items and the last lists put under headings
/// (the author, 1.7.0), as the window draws them.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class OptionNotesOnScreenTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-notes-" + Guid.NewGuid().ToString("N"));

    public OptionNotesOnScreenTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private MainViewModel OnALine(MainWindow window, ModPack pack)
    {
        PackRepository.Save(pack, _dir);
        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues") { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();
        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();
        if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
        vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
        WindowHarness.Pump();
        return vm;
    }

    /// <summary>The visible box on the row whose kind of note is <paramref name="kind"/>.</summary>
    private static ComboBox Box(MainWindow window, object row, string kind)
    {
        window.UpdateLayout();
        WindowHarness.Pump();
        return Descendants<ComboBox>(window).First(c => c.IsVisible && ReferenceEquals(c.DataContext, row)
                                                        && OptionNotes.GetKind(c) == kind);
    }

    /// <summary>What the open list draws beside an item: the item's container's
    /// texts, as laid out.</summary>
    private List<string> Drawn(ComboBox box, object item)
    {
        box.IsDropDownOpen = true;
        WindowHarness.Pump();
        box.UpdateLayout();
        var container = (ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem(item);
        Assert.NotNull(container);
        var texts = Descendants<TextBlock>(container).Where(t => t.IsVisible).Select(t => t.Text).ToList();
        _out.WriteLine($"  {item}: {string.Join(" | ", texts)}");
        box.IsDropDownOpen = false;
        WindowHarness.Pump();
        return texts;
    }

    [Fact]
    public void TheActionTypesSayWhatEachDoes_InTheOpenListOnly()
    {
        WindowHarness.Run(window =>
        {
            var vm = OnALine(window, PackRepository.CreateEmpty("notes.pack"));
            var action = vm.SelectedNode!.AddActionOnFinish();
            WindowHarness.Pump();
            var box = Box(window, action, OptionNotes.ActionType);

            var texts = Drawn(box, NodeActionTypes.CharacterFocus);
            Assert.Contains(SMSModForge.Localization.Loc.T("note.action.characterFocus"), texts);

            // The closed box shows the type and nothing else.
            box.IsDropDownOpen = false;
            WindowHarness.Pump();
            Assert.DoesNotContain(Descendants<TextBlock>(box).Where(t => t.IsVisible).Select(t => t.Text),
                                  t => t == SMSModForge.Localization.Loc.T("note.action.setActive"));
        });
    }

    [Fact]
    public void TypingANotesWordsFindsTheItem()
    {
        WindowHarness.Run(window =>
        {
            var vm = OnALine(window, PackRepository.CreateEmpty("notes.pack"));
            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.EmitSignal;
            WindowHarness.Pump();
            var box = Box(window, action.ParamRows.First(), OptionNotes.Signal);
            box.IsDropDownOpen = true;
            WindowHarness.Pump();

            // "camera" is in no signal's name - only in flash's note.
            ComboBoxSearch.SetTyped(box, "camera");
            WindowHarness.Pump();
            ComboBoxItem Of(string s) => (ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem(s);
            var flash = Of("flash").Visibility;
            var kiss = Of("kiss").Visibility;
            ComboBoxSearch.SetTyped(box, "");
            Assert.Contains(SMSModForge.Localization.Loc.T("note.signal.flash"), Drawn(box, "flash"));
            Assert.Equal(Visibility.Visible, flash);
            Assert.Equal(Visibility.Collapsed, kiss);
        });
    }

    [Fact]
    public void TheSetActiveBustsSayWhoseBustEachIs()
    {
        WindowHarness.Run(window =>
        {
            var game = VanillaBusts.All.First();
            var pack = PackRepository.CreateEmpty("notes.pack");
            var elf = new CharacterDef { Key = "elf", DisplayName = "Elf" };
            elf.Outfits.Add(new OutfitDef { Key = "party", GameObjectName = "Elf_Party" });
            pack.Characters.Add(elf);
            var vm = OnALine(window, pack);
            if (!vm.BustNameOnlyOptions.Contains(game.GoName)) vm.BustNameOnlyOptions.Add(game.GoName);

            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = NodeActionViewModel.CatBust;
            WindowHarness.Pump();
            var box = Box(window, action, OptionNotes.Target);

            Assert.Contains("Elf - party", Drawn(box, "Elf_Party"));
            Assert.Contains(game.Character, Drawn(box, game.GoName));
        });
    }

    private static List<string> Headings(ComboBox box)
        => box.Items.Groups?.Cast<CollectionViewGroup>().Select(g => (string)g.Name).ToList() ?? new();

    [Fact]
    public void TheGamesConversationsAreUnderTheirFolders()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var view = vm.AvailableVanillaDialoguesGrouped;
            var headings = view.Groups!.Cast<CollectionViewGroup>().Select(g => (string)g.Name).ToList();
            _out.WriteLine($"{headings.Count} folders, e.g. {string.Join(", ", headings.Take(5))}");
            Assert.True(headings.Count > 5);
            foreach (var g in view.Groups!.Cast<CollectionViewGroup>())
                Assert.All(g.Items.Cast<VanillaDialogueCatalog.Entry>(), e => Assert.Equal((string)g.Name, e.Folder));
        });
    }

    [Fact]
    public void ComponentsAreUnderWhatTheyAre()
    {
        var row = new ComponentRowViewModel(new ComponentDef { Type = PackComponentType.FadeInSprite }, _ => { });
        WindowHarness.Run(_ =>
        {
            var groups = row.ComponentTypesGrouped.Groups!.Cast<CollectionViewGroup>().ToList();
            foreach (var g in groups) _out.WriteLine($"{g.Name}: {g.ItemCount}");
            Assert.Equal(SMSModForge.Localization.Loc.T("componentGroup.pack"), (string)groups[0].Name);
            Assert.Equal(PackComponentType.BuiltIn, groups[0].Items.Cast<string>());
            if (VanillaComponentCatalog.All.Any(e => e.IsEngineComponent))
                Assert.Equal(SMSModForge.Localization.Loc.T("componentGroup.unity"), (string)groups[^1].Name);
        });
    }
}
