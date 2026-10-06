using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The dropdowns under headings, as the window draws them (the author,
/// 1.7.0): the Set-Active bust list that started it, a level's objects and
/// NPCs, and the action types.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class DropdownHeadingsOnScreenTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-dropgroups-" + Guid.NewGuid().ToString("N"));

    public DropdownHeadingsOnScreenTests(ITestOutputHelper o)
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

    /// <summary>The headings a box on screen shows, and what is under each.</summary>
    private List<(string Heading, List<string> Items)> Headings(ComboBox box)
    {
        var groups = box.Items.Groups?.Cast<CollectionViewGroup>()
                     .Select(g => ((string)g.Name, g.Items.Select(i => i?.ToString() ?? "").ToList()))
                     .ToList() ?? new();
        foreach (var (heading, items) in groups)
            _out.WriteLine($"  {heading}: {string.Join(", ", items.Take(6))}{(items.Count > 6 ? ", …" : "")}");
        return groups;
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

    /// <summary>The box on screen - visible, not one a collapsed row keeps -
    /// whose list holds <paramref name="item"/>.</summary>
    private static ComboBox BoxShowing(MainWindow window, string item, object? row = null)
    {
        window.UpdateLayout();
        WindowHarness.Pump();
        return Assert.Single(Descendants<ComboBox>(window).Where(c =>
            c.IsVisible && (row == null || ReferenceEquals(c.DataContext, row))
            && c.ItemsSource is System.ComponentModel.ICollectionView v
            && v.Cast<object>().Any(o => o?.ToString() == item)));
    }

    [Fact]
    public void TheSetActiveBustListIsUnderWhoseBustEachIs()
    {
        WindowHarness.Run(window =>
        {
            var game = VanillaBusts.All.First();
            var pack = PackRepository.CreateEmpty("dropgroups.pack");
            var elf = new CharacterDef { Key = "elf", DisplayName = "Elf" };
            elf.Outfits.Add(new OutfitDef { Key = "default", GameObjectName = "Elf_Default" });
            pack.Characters.Add(elf);
            var vm = OnALine(window, pack);
            // A bust of the game's in the list, however the pack's cast came by it.
            if (!vm.BustNameOnlyOptions.Contains(game.GoName)) vm.BustNameOnlyOptions.Add(game.GoName);

            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = "Bust";
            WindowHarness.Pump();

            var box = BoxShowing(window, "Elf_Default");
            Assert.Same(vm.BustNameOnlyOptionsGrouped, box.ItemsSource);
            var headings = Headings(box);
            Assert.Contains(headings, h => h.Heading == OptionGroups.ThisPack && h.Items.Contains("Elf_Default"));
            Assert.Contains(headings, h => h.Heading == OptionGroups.GamesOwn && h.Items.Contains(game.GoName));
            Assert.DoesNotContain(headings, h => h.Heading == OptionGroups.ThisPack && h.Items.Contains(game.GoName));
            // The pack's own on top, as in the music and speaker pickers.
            Assert.Equal(OptionGroups.ThisPack, headings[0].Heading);
        });
    }

    [Fact]
    public void ALevelsObjectsAndNpcsAreUnderWhoseTheyAre()
    {
        if (VanillaLevelCatalog.FindLevel("26_Downtown") == null)
        { _out.WriteLine("no extraction shipped beside the tests - skipping"); return; }

        WindowHarness.Run(window =>
        {
            var pack = PackRepository.CreateEmpty("dropgroups.pack");
            var downtown = new VanillaPlaceExtensionDef { Source = "vanilla:26_Downtown" };
            downtown.GameObjects.Add(new GameObjectDef { Name = "PackLamp" });
            pack.VanillaExtensions.Add(downtown);
            pack.Places.Add(new PlaceDef
            {
                Key = "home", InternalName = "Home", DisplayName = "Home",
                GameObjects = { new GameObjectDef { Name = "Lamp" } },
            });
            var vm = OnALine(window, pack);

            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = NodeActionViewModel.CatOverlay;
            WindowHarness.Pump();

            // The levels: the pack's place, and the game's level it extends.
            _out.WriteLine("levels:");
            var levels = Headings(BoxShowing(window, "place:home", action));
            Assert.Contains(levels, h => h.Heading == OptionGroups.ThisPack && h.Items.Contains("place:home"));
            Assert.Contains(levels, h => h.Heading == OptionGroups.GamesOwn && h.Items.Contains("vanilla:26_Downtown"));
            Assert.Equal(OptionGroups.ThisPack, levels[0].Heading);

            action.OverlayLevel = "vanilla:26_Downtown";
            WindowHarness.Pump();
            _out.WriteLine("objects:");
            var objects = Headings(BoxShowing(window, "PackLamp", action));
            Assert.Contains(objects, h => h.Heading == OptionGroups.ThisPack && h.Items.SequenceEqual(new[] { "PackLamp" }));
            Assert.Contains(objects, h => h.Heading == OptionGroups.GamesOwn && h.Items.Contains("NPCs > Group_1"));

            action.Category = NodeActionViewModel.CatNpcs;
            action.OverlayLevel = "vanilla:26_Downtown";
            WindowHarness.Pump();
            _out.WriteLine("NPCs:");
            var npcs = Headings(BoxShowing(window, "NPCs > Group_1 > NPC", action));
            var only = Assert.Single(npcs);
            Assert.Equal(OptionGroups.GamesOwn, only.Heading);
            Assert.Contains("NPCs > Group_1 > NPC", only.Items);
        });
    }

    [Fact]
    public void TheActionTypesAreUnderWhatTheyDo()
    {
        WindowHarness.Run(window =>
        {
            var vm = OnALine(window, PackRepository.CreateEmpty("dropgroups.pack"));
            var action = vm.SelectedNode!.AddActionOnFinish();
            WindowHarness.Pump();

            var boxes = Descendants<ComboBox>(window).Where(c => ReferenceEquals(c.ItemsSource, vm.ActionTypesGrouped)).ToList();
            Assert.NotEmpty(boxes);
            var headings = Headings(boxes[0]);
            Assert.Equal(SMSModForge.Localization.Loc.T("typeGroup.characters"), headings[0].Heading);
            Assert.True(headings.Count >= 6, "the action types should be under their topics");
            // The row still shows the type it has.
            Assert.Equal(action.DisplayType, boxes[0].SelectedItem as string);
        });
    }
}
