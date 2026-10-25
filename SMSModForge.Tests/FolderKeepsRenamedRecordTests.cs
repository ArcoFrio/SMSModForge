using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A record made inside a folder stays in it when naming it changes its key
/// (the author, 2026-10-21: a new Scene in a new folder jumped out of the
/// folder once its Sprite was picked).
/// <para/>
/// Folders remember their records by key, written when the tree changes. A
/// record the author has just added takes its key from the display name as
/// they type it, and nothing wrote the folder again - so the folder still
/// held the key the record was born with, and the next time the tree was
/// built from the pack (an undo, a redo, a reload) the record was found in no
/// folder and went to the top level.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class FolderKeepsRenamedRecordTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-folderkey-" + Guid.NewGuid().ToString("N"));

    public FolderKeepsRenamedRecordTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private MainViewModel Opened(MainWindow window, int tab)
    {
        PackRepository.Save(PackRepository.CreateEmpty("folders.pack"), _dir);
        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = tab;
        WindowHarness.Pump();
        return vm;
    }

    private static string? FolderOf(UnitTreeController tree, object record)
    {
        foreach (var top in tree.Tree)
            if (top is UnitFolderNode f && f.Children.OfType<UnitLeafNode>().Any(l => ReferenceEquals(l.Item, record)))
                return f.Name;
        return null;
    }

    [Fact]
    public void ASceneNamedInsideAFolderIsStillThereAfterAnUndo()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window, 6);
            vm.SceneTree.AddFolder();                       // selected: the new folder
            string folder = ((UnitFolderNode)vm.SceneTree.Selected!).Name;
            vm.AddSceneCommand.Execute(null);
            WindowHarness.Pump();
            var scene = vm.SelectedScene!;
            string born = scene.Key;
            vm.Undo.Checkpoint();

            scene.DisplayName = "Beach Kiss";               // the key follows it
            vm.CommitPendingRenames();
            vm.Undo.Checkpoint();
            _out.WriteLine($"key {born} -> {scene.Key}; folder '{folder}' holds: "
                           + string.Join(", ", vm.Pack.SceneFolders.SelectMany(f => f.Items)));
            Assert.NotEqual(born, scene.Key);

            scene.SceneSprite = "Scenes/kiss.png";          // what the author did next
            vm.Undo.Checkpoint();
            vm.UndoCommand.Execute(null);                   // the tree is built again from the pack
            WindowHarness.Pump();

            var again = vm.Scenes.Single(s => s.Key == scene.Key);
            Assert.Equal(folder, FolderOf(vm.SceneTree, again));
        });
    }

    [Fact]
    public void EveryKindThatFollowsItsNameKeepsItsFolder()
    {
        // The same rule holds wherever a new record's key follows its name -
        // places, NPCs, wallpapers, music and sounds share the scenes' tree.
        WindowHarness.Run(window =>
        {
            var vm = Opened(window, 3);
            var cases = new (string Name, int Tab, UnitTreeController Tree, Action Add, Func<object> Added, Action<object, string> Rename, Func<object, string> Key)[]
            {
                ("place", 3, vm.PlaceTree, () => vm.AddPlaceCommand.Execute(null), () => vm.SelectedPlace!,
                    (o, n) => ((PlaceViewModel)o).DisplayName = n, o => ((PlaceViewModel)o).Key),
                ("npc", 2, vm.NpcTree, () => vm.AddNpcCommand.Execute(null), () => vm.SelectedNpc!,
                    (o, n) => ((NpcViewModel)o).DisplayName = n, o => ((NpcViewModel)o).Key),
                ("wallpaper", 9, vm.WallpaperTree, () => vm.AddWallpaperCommand.Execute(null), () => vm.SelectedWallpaper!,
                    (o, n) => ((WallpaperViewModel)o).DisplayName = n, o => ((WallpaperViewModel)o).Key),
                ("music", 7, vm.MusicTree, () => vm.AddMusicCommand.Execute(null), () => vm.SelectedMusic!,
                    (o, n) => ((MusicViewModel)o).DisplayName = n, o => ((MusicViewModel)o).Key),
                ("sfx", 8, vm.SfxTree, () => vm.AddSfxCommand.Execute(null), () => vm.SelectedSfx!,
                    (o, n) => ((SfxViewModel)o).DisplayName = n, o => ((SfxViewModel)o).Key),
            };
            foreach (var c in cases)
            {
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = c.Tab;
                WindowHarness.Pump();
                c.Tree.Selected = null;
                c.Tree.AddFolder();
                string folder = ((UnitFolderNode)c.Tree.Selected!).Name;
                c.Add();
                WindowHarness.Pump();
                var record = c.Added();
                string born = c.Key(record);
                c.Rename(record, "Renamed " + c.Name);
                WindowHarness.Pump();

                var held = c.Tree.Tree.OfType<UnitFolderNode>().Single(f => f.Name == folder);
                var def = FolderDefs(vm.Pack, c.Name).Single(f => f.Name == folder);
                _out.WriteLine($"{c.Name}: {born} -> {c.Key(record)}; folder holds {string.Join(", ", def.Items)}");
                Assert.Contains(c.Key(record), def.Items);
                Assert.DoesNotContain(born, def.Items);
            }
        });
    }

    [Fact]
    public void DialoguesAndRulesKeepTheirFoldersToo()
    {
        // Their trees are their own, but folders hold them by key all the same.
        WindowHarness.Run(window =>
        {
            var vm = Opened(window, 5);
            vm.AddDialogueFolderCommand.Execute(null);
            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            var dialogue = vm.SelectedDialogue!;
            string born = dialogue.Key;
            dialogue.DisplayName = "Pier talk";
            WindowHarness.Pump();
            var held = vm.Pack.DialogueFolders.Single().Dialogues;
            _out.WriteLine($"dialogue: {born} -> {dialogue.Key}; folder holds {string.Join(", ", held)}");
            Assert.NotEqual(born, dialogue.Key);
            Assert.Equal(new[] { dialogue.Key }, held);

            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Integration;
            WindowHarness.Pump();
            vm.AddIntegrationFolderCommand.Execute(null);
            vm.AddIntegrationRuleCommand.Execute(null);
            WindowHarness.Pump();
            var rule = vm.SelectedIntegrationRule!;
            born = rule.Key;
            rule.DisplayName = "Daily refill";
            WindowHarness.Pump();
            var rules = vm.Pack.IntegrationFolders.Single().Rules;
            _out.WriteLine($"rule: {born} -> {rule.Key}; folder holds {string.Join(", ", rules)}");
            Assert.NotEqual(born, rule.Key);
            Assert.Equal(new[] { rule.Key }, rules);
        });
    }

    [Fact]
    public void ANewSceneIsFramedAndSoundedLikeTheGamesOwn()
    {
        // The author, 2026-10-21: the game's scenes beside a pack's use the
        // "sexy" frame and the kiss sound; a new one used the photo frame and
        // no sound, so every scene needed both changed by hand.
        WindowHarness.Run(window =>
        {
            var vm = Opened(window, 6);
            vm.AddSceneCommand.Execute(null);
            var scene = vm.Pack.Scenes.Last();
            Assert.Equal("SexyFrame.png", scene.VanillaFrame);
            Assert.Equal(SceneSoundMode.Kiss, scene.Sound);
            Assert.Contains(VanillaFrames.All, f => f.FileName == scene.VanillaFrame);   // a frame the list offers
        });
    }

    private static System.Collections.Generic.List<UnitFolderDef> FolderDefs(ModPack pack, string kind) => kind switch
    {
        "place" => pack.PlaceFolders,
        "npc" => pack.NpcFolders,
        "wallpaper" => pack.WallpaperFolders,
        "music" => pack.MusicFolders,
        _ => pack.SfxFolders,
    };
}
