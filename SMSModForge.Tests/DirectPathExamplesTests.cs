using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Direct Path list (the author, 1.6.3): paths into the game's own scene
/// worth knowing, grouped, each with a short note beside it - where it used to
/// list the pack's own GameObject and bust names, which are neither what a
/// Direct Path is for nor written as one.
/// </summary>
public sealed class DirectPathExampleListTests
{
    private readonly ITestOutputHelper _out;
    public DirectPathExampleListTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void EveryExampleIsWrittenTheWayADirectPathIs()
    {
        foreach (var e in GamePathExamples.All)
        {
            // Slashes between the game's names - not the " > " the level lists
            // use, not a backslash, nothing empty, nothing trimmed away.
            Assert.DoesNotContain(" > ", e.Path);
            Assert.DoesNotContain("\\", e.Path);
            Assert.False(e.Path.StartsWith("/") || e.Path.EndsWith("/"), e.Path);
            Assert.DoesNotContain("//", e.Path);
            Assert.Equal(e.Path.Trim(), e.Path);
        }
        Assert.Equal(GamePathExamples.All.Count, GamePathExamples.All.Select(e => e.Path).Distinct().Count());
    }

    [Fact]
    public void EveryExampleHasAHeadingAndANoteInWords()
    {
        foreach (var e in GamePathExamples.All)
        {
            Assert.True(Loc.English.Has(e.GroupKey), e.GroupKey);
            Assert.True(Loc.English.Has(e.NoteKey), e.NoteKey);
        }
        // Every heading has something under it, and there is more than one.
        Assert.True(GamePathExamples.All.Select(e => e.GroupKey).Distinct().Count() >= 4);
        // Each note gives an example, not only a name.
        Assert.All(GamePathExamples.All, e => Assert.Contains("e.g.", Loc.English.Get(e.NoteKey)));
    }

    [Fact]
    public void NoneRepeatsWhatAnotherCategoryReaches()
    {
        // A bust is the Bust category's and a whole place is Places'; offering
        // them here would teach the way round.
        foreach (var e in GamePathExamples.All)
        {
            Assert.False(e.Path.StartsWith("2_Bust_Manager/", StringComparison.Ordinal), e.Path);
            var parts = e.Path.Split('/');
            Assert.False(parts[0] == "5_Levels" && parts.Length <= 2, e.Path);
        }
    }

    [Fact]
    public void WhatIsPickedIsThePath_AndWhatIsSearchedIsPathAndNote()
    {
        var charm = new GamePathOption(GamePathExamples.All.First(e => e.Path.EndsWith("Raise_Stat_Charm")));
        Assert.Equal("10_Gameplay/Raise_Stats/Raise_Stat_Charm", charm.ToString());
        Assert.Contains("10_Gameplay", charm.SearchText);
        Assert.Contains(charm.Note, charm.SearchText);
    }
}

[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class DirectPathExamplesTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-directpath-" + Guid.NewGuid().ToString("N"));

    public DirectPathExamplesTests(ITestOutputHelper o)
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

    /// <summary>A pack with a place object and a bust of its own - the names
    /// the Direct Path list used to fill up with - open on a node.</summary>
    private MainViewModel APack(MainWindow window)
    {
        var pack = PackRepository.CreateEmpty("directpath.pack");
        var elf = new CharacterDef { Key = "elf", DisplayName = "Elf" };
        elf.Outfits.Add(new OutfitDef { Key = "default", GameObjectName = "Elf_Default" });
        pack.Characters.Add(elf);
        pack.Places.Add(new PlaceDef { Key = "home", InternalName = "Home", DisplayName = "Home",
                                       GameObjects = { new GameObjectDef { Name = "Lamp" } } });
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

    /// <summary>The Target box of a row: its visible editable combo showing
    /// the Direct Path list.</summary>
    private static ComboBox TargetBox(MainWindow window, object row, MainViewModel vm)
        => Descendants<ComboBox>(window).Single(c => c.IsVisible && c.IsEditable
                                                     && ReferenceEquals(c.DataContext, row)
                                                     && ReferenceEquals(c.ItemsSource, vm.DirectPathExamples));

    [Fact]
    public void EveryDirectPathRowOffersTheGamesPaths_Grouped_AndPickingOneWritesThePath()
    {
        WindowHarness.Run(window =>
        {
            var vm = APack(window);
            var charm = vm.DirectPathExamples.Cast<GamePathOption>().First(o => o.Value.EndsWith("Raise_Stat_Charm"));
            var music = vm.DirectPathExamples.Cast<GamePathOption>().First(o => o.Value == "12_AudioPlayer/Music");

            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = NodeActionViewModel.CatPath;
            var cond = vm.SelectedNode.AddCondition();
            cond.Type = NodeConditionTypes.GameObjectActive;
            cond.GoCategory = NodeActionViewModel.CatPath;
            var fade = vm.SelectedNode.AddActionOnFinish();
            fade.Type = NodeActionTypes.FadeSprite;
            WindowHarness.Pump();

            var rows = new (object Row, Func<string> Target)[]
            {
                (action, () => action.Target),
                (cond, () => cond.GoTarget),
                (fade, () => fade.GoTarget),
            };
            foreach (var (row, target) in rows)
            {
                var box = TargetBox(window, row, vm);
                var offered = box.Items.Cast<object>().ToList();
                _out.WriteLine($"{row.GetType().Name}: {offered.Count} offered, grouped in {box.Items.Groups?.Count}");

                // The game's paths, under their headings - not the pack's names.
                Assert.All(offered, o => Assert.IsType<GamePathOption>(o));
                Assert.DoesNotContain(offered, o => o.ToString() is "Lamp" or "Elf_Default");
                Assert.Equal(GamePathExamples.All.Select(e => e.GroupKey).Distinct().Count(), box.Items.Groups!.Count);

                // Picking one puts its path in the box, slashes and all.
                box.SelectedItem = charm;
                WindowHarness.Pump();
                Assert.Equal("10_Gameplay/Raise_Stats/Raise_Stat_Charm", target());
                box.SelectedItem = music;
                WindowHarness.Pump();
                Assert.Equal("12_AudioPlayer/Music", target());
            }

            // The other rows' boxes follow their own choice, not this one's.
            Assert.Equal("12_AudioPlayer/Music", action.Target);
        });
    }

    [Fact]
    public void EachEntryShowsItsNoteBesideThePath()
    {
        WindowHarness.Run(window =>
        {
            var vm = APack(window);
            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = NodeActionViewModel.CatPath;
            WindowHarness.Pump();

            var box = TargetBox(window, action, vm);
            box.IsDropDownOpen = true;
            WindowHarness.Pump();
            try
            {
                var shop = vm.DirectPathExamples.Cast<GamePathOption>().First(o => o.Value == "9_MainCanvas/ShopCore");
                var item = (ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem(shop);
                if (item == null)
                {
                    box.Items.MoveCurrentTo(shop);
                    WindowHarness.Pump();
                    item = (ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem(shop);
                }
                Assert.NotNull(item);
                item.ApplyTemplate();
                WindowHarness.Pump();
                var words = Descendants<TextBlock>(item).Select(t => t.Text).ToList();
                _out.WriteLine("item: " + string.Join(" | ", words));
                Assert.Contains("9_MainCanvas/ShopCore", words);
                Assert.Contains(Loc.T("directPath.note.shop"), words);

                // The heading over it, in words.
                var headings = Descendants<GroupItem>(box.Template.FindName("PART_Popup", box) is System.Windows.Controls.Primitives.Popup p
                                                            ? p.Child : box)
                               .SelectMany(g => Descendants<TextBlock>(g).Take(1).Select(t => t.Text)).ToList();
                _out.WriteLine("headings: " + string.Join(" | ", headings));
                Assert.Contains(Loc.T(GamePathExamples.Interface), headings);
            }
            finally { box.IsDropDownOpen = false; WindowHarness.Pump(); }
        });
    }
}
