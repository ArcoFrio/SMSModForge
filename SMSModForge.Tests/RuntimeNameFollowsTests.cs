using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The display name carries the runtime name with it (the author, 2026-10-21):
/// a duplicate's "_copy" goes away when it is renamed, the Rename button and
/// the Display name box do the same thing, and the pencil beside the read-only
/// runtime name sets one of the author's own, which then stays.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class RuntimeNameFollowsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public RuntimeNameFollowsTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-runtimenames-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const int TabNpcs = MainTab.Npcs, TabPlaces = MainTab.Places, TabDialogues = MainTab.Dialogues, TabScenes = MainTab.Scenes, TabMusic = MainTab.Music,
                      TabSfx = MainTab.Sfx, TabWallpapers = MainTab.Wallpapers, TabIntegration = MainTab.Integration, TabQuests = MainTab.Quests;

    /// <summary>One scene of each kind the rule tells apart, each switched on
    /// by a rule so a rename that follows can be seen to.</summary>
    private void SavePack()
    {
        var pack = PackRepository.CreateEmpty("runtimenames.pack");
        pack.Scenes.Add(new SceneDef { Key = "BeachDay", DisplayName = "Beach Day" });
        pack.Scenes.Add(new SceneDef { Key = "MyOwnKey", DisplayName = "Something Else" });
        pack.Scenes.Add(new SceneDef { Key = "Scene01_copy", DisplayName = "Scene 02" });
        pack.Scenes.Add(new SceneDef { Key = "PhotoDay", DisplayName = "Photo Day", StarmakerPhoto = true });
        foreach (var key in new[] { "BeachDay", "MyOwnKey", "PhotoDay" })
        {
            var rule = new UpdateRuleDef { Key = "on" + key, DisplayName = "On " + key };
            var on = new NodeActionDef { Type = NodeActionTypes.SetGameObjectActive };
            on.Params["kind"] = "Scene";
            on.Params["target"] = key;
            rule.Actions.Add(on);
            pack.IntegrationRules.Add(rule);
        }
        pack.Places.Add(new PlaceDef { Key = "SecretBeach", DisplayName = "Remote Area" });
        pack.Npcs.Add(new NpcDef { Key = "Guard", DisplayName = "Guard" });
        pack.Dialogues.Add(new DialogueDef { Key = "Greeting", DisplayName = "Greeting" });
        pack.Music.Add(new MusicDef { Key = "Theme", DisplayName = "Theme" });
        pack.Sfx.Add(new SfxDef { Key = "Ping", DisplayName = "Ping" });
        pack.Wallpapers.Add(new WallpaperDef { Key = "Sky", DisplayName = "Sky" });
        pack.Quests.Add(new QuestDef { Key = "Errand", Title = "Errand" });
        PackRepository.Save(pack, _dir);
    }

    private MainViewModel Opened(MainWindow window)
    {
        SavePack();
        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        WindowHarness.Pump();
        return vm;
    }

    private static SceneViewModel Scene(MainViewModel vm, string key) => vm.Scenes.Single(s => s.Key == key);

    private static string Target(MainViewModel vm, string rule)
        => vm.Pack.IntegrationRules.Single(r => r.Key == rule).Actions[0].Params["target"];

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    private static TextBox BoxFor(Window window, object record, string path)
        => Descendants<TextBox>(window).First(b => ReferenceEquals(b.DataContext, record)
               && b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == path);

    /// <summary>Type into the Display name box and leave it, as the author does.</summary>
    private static void TypeDisplayName(MainWindow window, object record, string text)
    {
        var box = BoxFor(window, record, "DisplayName");
        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, box)
        { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
        box.Text = text;
        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, null)
        { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        WindowHarness.Pump();
    }

    private static void OpenScene(MainViewModel vm, string key)
    {
        vm.SelectedTabIndex = TabScenes;
        vm.SelectedScene = Scene(vm, key);
        WindowHarness.Pump();
    }

    [Fact]
    public void ALoadedScenesNameCarriesItsRuntimeNameAndTheRowsFollow()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            OpenScene(vm, "BeachDay");
            var scene = vm.SelectedScene!;
            TypeDisplayName(window, scene, "Beach Night");

            _out.WriteLine($"key '{scene.Key}', rule aims at '{Target(vm, "onBeachDay")}'");
            Assert.Equal("BeachNight", scene.Key);
            Assert.Equal("BeachNight", Target(vm, "onBeachDay"));
        });
    }

    [Fact]
    public void ARuntimeNameSomebodyTypedStaysWhateverTheDisplayName()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            OpenScene(vm, "MyOwnKey");
            TypeDisplayName(window, vm.SelectedScene!, "Beach Morning");
            Assert.Equal("MyOwnKey", vm.SelectedScene!.Key);
            Assert.Equal("MyOwnKey", Target(vm, "onMyOwnKey"));

            // The Androids pack's places: "SecretBeach" is called "Remote Area".
            vm.SelectedTabIndex = TabPlaces;
            vm.SelectedPlace = vm.Places.Single();
            WindowHarness.Pump();
            TypeDisplayName(window, vm.SelectedPlace!, "Hidden Beach");
            Assert.Equal("SecretBeach", vm.SelectedPlace!.Key);
        });
    }

    [Fact]
    public void AStarmakerPhotosSceneKeepsItsKeyForThePlayersSaves()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            OpenScene(vm, "PhotoDay");
            TypeDisplayName(window, vm.SelectedScene!, "Photo Night");
            Assert.Equal("PhotoDay", vm.SelectedScene!.Key);
        });
    }

    [Fact]
    public void TheRenameButtonDoesWhatTheDisplayNameBoxDoes()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            OpenScene(vm, "BeachDay");
            var scene = vm.SelectedScene!;
            string? asked = null, offered = null;
            vm.PromptForText = (_, label, initial) => { asked = label; offered = initial; return "Beach Night"; };
            vm.RenameItemCommand.Execute(null);
            WindowHarness.Pump();

            Assert.Equal(Loc.T("rename.displayName"), asked);
            Assert.Equal("Beach Day", offered);
            Assert.Equal("Beach Night", scene.DisplayName);
            Assert.Equal("BeachNight", scene.Key);
            Assert.Equal("BeachNight", Target(vm, "onBeachDay"));
        });
    }

    [Fact]
    public void RenamingAnOldCopyUnderTheNameItHasLosesTheCopy()
    {
        // What the author has in the Androids pack: scenes duplicated and
        // renamed before 1.8.0, whose runtime names still say "_copy".
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            OpenScene(vm, "Scene01_copy");
            var scene = vm.SelectedScene!;
            vm.PromptForText = (_, _, initial) => initial;
            vm.RenameItemCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal("Scene02", scene.Key);
        });
    }

    [Fact]
    public void ThePencilSetsARuntimeNameOfYourOwnWhichThenStays()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            OpenScene(vm, "BeachDay");
            var scene = vm.SelectedScene!;
            vm.PromptForText = (_, _, _) => "Dawn";
            vm.EditRuntimeNameCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal("Dawn", scene.Key);
            Assert.Equal("Dawn", Target(vm, "onBeachDay"));
            Assert.Equal("Beach Day", scene.DisplayName);

            TypeDisplayName(window, scene, "Beach Evening");
            Assert.Equal("Dawn", scene.Key);
        });
    }

    [Fact]
    public void ADuplicateLosesItsCopyWhenRenamed()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            OpenScene(vm, "BeachDay");
            vm.DuplicateItemCommand.Execute(null);
            WindowHarness.Pump();
            var copy = vm.Scenes.Single(s => s.Key == "BeachDay_copy");
            copy.DisplayName = "Beach Dusk";
            Assert.Equal("BeachDusk", copy.Key);

            // Dialogues are duplicated by a path of their own.
            vm.SelectedTabIndex = TabDialogues;
            vm.SelectedDialogue = vm.Dialogues.Single(d => d.Key == "Greeting");
            WindowHarness.Pump();
            vm.DuplicateItemCommand.Execute(null);
            WindowHarness.Pump();
            var talk = vm.Dialogues.Single(d => d.Key == "Greeting_copy");
            talk.DisplayName = "Farewell";
            Assert.Equal("Farewell", talk.Key);
        });
    }

    [Fact]
    public void EveryRuntimeNameHasItsPencil()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var style = (Style)window.FindResource("RuntimeNameEditButton");
            var tabs = new (int Tab, Action Select, Func<string> Key)[]
            {
                (TabNpcs,        () => vm.SelectedNpc = vm.Npcs.Single(),                              () => vm.SelectedNpc!.Key),
                (TabPlaces,      () => vm.SelectedPlace = vm.Places.Single(),                          () => vm.SelectedPlace!.Key),
                (TabDialogues,   () => vm.SelectedDialogue = vm.Dialogues.Single(d => d.Key == "Greeting"), () => vm.SelectedDialogue!.Key),
                (TabScenes,      () => vm.SelectedScene = Scene(vm, "MyOwnKey"),                       () => vm.SelectedScene!.Key),
                (TabMusic,       () => vm.SelectedMusic = vm.Music.Single(),                           () => vm.SelectedMusic!.Key),
                (TabSfx,         () => vm.SelectedSfx = vm.Sfx.Single(),                               () => vm.SelectedSfx!.Key),
                (TabWallpapers,  () => vm.SelectedWallpaper = vm.Wallpapers.Single(),                  () => vm.SelectedWallpaper!.Key),
                (TabIntegration, () => vm.SelectedIntegrationRule = vm.IntegrationRules.First(),       () => vm.SelectedIntegrationRule!.Key),
                (TabQuests,      () => vm.SelectedQuest = vm.Quests.Single(),                          () => vm.SelectedQuest!.Key),
            };
            var missing = new List<string>();
            foreach (var (tab, select, key) in tabs)
            {
                vm.SelectedTabIndex = tab;
                select();
                WindowHarness.Pump();
                var pencils = Descendants<Button>(window)
                    .Where(b => b.Style == style && b.IsVisible && b.Parent is DockPanel panel
                                && panel.Children.OfType<TextBox>().Any(t => t.IsReadOnly
                                       && t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Key"))
                    .ToList();
                if (pencils.Count != 1) { missing.Add($"tab {tab}: {pencils.Count} pencils"); continue; }

                // The pencil really renames, not just sits there.
                string wanted = "Renamed" + tab;
                vm.PromptForText = (_, _, _) => wanted;
                pencils[0].Command.Execute(pencils[0].CommandParameter);
                WindowHarness.Pump();
                if (key() != wanted) missing.Add($"tab {tab}: the pencil left '{key()}'");
            }
            _out.WriteLine(string.Join("\n", missing));
            Assert.Empty(missing);
        });
    }
}
