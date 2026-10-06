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
/// Renaming in the editor (the author, 1.7.0): a record is renamed in its own
/// box, and everything that uses the name follows "on commit of the new name,
/// or just moving away from it" - in every tab, not only the few that had it.
/// The Rename buttons on the tabs' toolbars, which renamed only folders and did
/// nothing with a record selected, are gone; folders are renamed where they
/// stand.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class RenameThroughTheEditorTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public RenameThroughTheEditorTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-renames-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const int TabDialogues = 5, TabScenes = 6;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    /// <summary>The box bound to <paramref name="path"/> on <paramref name="record"/>.</summary>
    private static TextBox BoxFor(Window window, object record, string path)
        => Descendants<TextBox>(window).First(b => ReferenceEquals(b.DataContext, record)
               && b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == path);

    /// <summary>Focus arriving at and leaving a box, as the window sees it. Raised
    /// rather than moved for real: the harness window is not the foreground one,
    /// so the keyboard cannot be given to it.</summary>
    private static void Enter(TextBox box)
        => box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, box)
        { RoutedEvent = Keyboard.GotKeyboardFocusEvent });

    private static void Leave(TextBox box)
        => box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, null)
        { RoutedEvent = Keyboard.LostKeyboardFocusEvent });

    private static void PressEnter(TextBox box)
        => box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, Key.Enter)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static NodeActionDef SwitchOn(MainViewModel vm, string scene)
    {
        var rule = new UpdateRuleDef { Key = "rule" };
        var on = new NodeActionDef { Type = NodeActionTypes.SetGameObjectActive };
        on.Params["kind"] = "Scene";
        on.Params["target"] = scene;
        rule.Actions.Add(on);
        vm.Pack.IntegrationRules.Add(rule);
        return on;
    }

    [Fact]
    public void ANewScenesNameIsFollowedWhenItsBoxIsLeft()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.SelectedTabIndex = TabScenes;
            vm.AddSceneCommand.Execute(null);
            WindowHarness.Pump();
            var scene = vm.SelectedScene!;
            var on = SwitchOn(vm, scene.Key);

            var box = BoxFor(window, scene, "DisplayName");
            Enter(box);
            // Typed, a key at a time: a new scene's key follows its name.
            foreach (string so_far in new[] { "B", "Be", "Beach", "Beach N", "Beach Night" })
                box.Text = so_far;
            WindowHarness.Pump();
            _out.WriteLine($"key now '{scene.Key}', row still '{on.Params["target"]}'");
            Assert.NotEqual(scene.Key, on.Params["target"]);   // still typing

            Leave(box);
            WindowHarness.Pump();
            _out.WriteLine($"after leaving: row '{on.Params["target"]}'");
            Assert.Equal(scene.Key, on.Params["target"]);
        });
    }

    [Fact]
    public void PressingEnterInTheBoxFollowsTheRenameToo()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.SelectedTabIndex = TabScenes;
            vm.AddSceneCommand.Execute(null);
            WindowHarness.Pump();
            var scene = vm.SelectedScene!;
            var on = SwitchOn(vm, scene.Key);

            var box = BoxFor(window, scene, "DisplayName");
            Enter(box);
            box.Text = "Rooftop";
            PressEnter(box);
            WindowHarness.Pump();

            Assert.Equal(scene.Key, on.Params["target"]);
        });
    }

    [Fact]
    public void AnObjectRenamedInAPlaceTakesTheRowsAimedAtIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddPlaceCommand.Execute(null);
            WindowHarness.Pump();
            var place = vm.SelectedPlace!;
            var torch = place.AddGameObject();
            torch.Name = "Torch";
            var on = new NodeActionDef { Type = NodeActionTypes.SetGameObjectActive };
            on.Params["kind"] = "GameObjects";
            on.Params["overlayLevel"] = "place:" + place.Key;
            on.Params["target"] = "Torch";
            var rule = new UpdateRuleDef { Key = "rule" };
            rule.Actions.Add(on);
            vm.Pack.IntegrationRules.Add(rule);

            // What the window does when the object's name box takes the keyboard.
            vm.WatchRename(torch);
            torch.Name = "Lantern";
            vm.CommitPendingRenames();

            Assert.Equal("Lantern", on.Params["target"]);
        });
    }

    // ── Translations ─────────────────────────────────────────────────────

    private void Prepare()
    {
        var pack = PackRepository.CreateEmpty("renames.pack");
        pack.Characters.Add(new CharacterDef { Key = "kiki", DisplayName = "Kiki" });
        var d = new DialogueDef { Key = "chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Hello there!" });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);

        var source = PackTranslations.Source(pack);
        var es = new TextFile();
        foreach (var e in source.Entries)
            es.Add(new TextFile.Entry { Key = e.Key, Text = "ES " + e.Text, English = e.Text });
        PackTranslations.Write(pack, _dir, "es", source, es);
    }

    private TextFile Spanish() => Loc.Read(PackTranslations.PathOf(_dir, "es"))!;

    private static void RenameDialogue(MainViewModel vm, string to)
    {
        vm.SelectedTabIndex = TabDialogues;
        vm.SelectedDialogue = vm.Dialogues.Single(x => !x.IsVanillaBased);
        vm.PromptForText = (_, _, _) => to;
        vm.RenameItemCommand.Execute(null);
        WindowHarness.Pump();
    }

    [Fact]
    public void ADialoguesTranslationGoesWithItWhenItIsRenamed()
    {
        Prepare();
        Assert.Equal("ES Hello there!", Spanish().Translated("dialogue.chat.1"));

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            RenameDialogue(vm, "talk");
            Assert.True(vm.SavePack());
        });

        var es = Spanish();
        _out.WriteLine($"talk.1 = '{es.Get("dialogue.talk.1")}', chat.1 = '{es.Get("dialogue.chat.1")}'");
        Assert.Equal("ES Hello there!", es.Translated("dialogue.talk.1"));
        Assert.False(es.Has("dialogue.chat.1"));
    }

    [Fact]
    public void ARenameTakenBackLeavesTheTranslationWhereItWas()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            RenameDialogue(vm, "talk");
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Contains(vm.Dialogues, d => d.Key == "chat");
            vm.SavePack();
        });

        var es = Spanish();
        Assert.Equal("ES Hello there!", es.Translated("dialogue.chat.1"));
        Assert.False(es.Has("dialogue.talk.1"));
    }

    // ── Folders ──────────────────────────────────────────────────────────

    [Fact]
    public void NoTabHasARenameButtonAnyMore()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            string rename = Loc.T("folders.renameMenu");
            string addFolder = Loc.T("scenes.unitAddFolder");
            int folderButtons = 0;
            var found = new List<string>();

            // Only the open tab is built, so each is opened in turn.
            for (int i = 0; i < tabs.Items.Count; i++)
            {
                vm.SelectedTabIndex = i;
                WindowHarness.Pump();
                foreach (var b in Descendants<ToolBar>(window).SelectMany(t => t.Items.OfType<Button>()))
                {
                    if (b.Content is not string s) continue;
                    if (s == addFolder) folderButtons++;
                    if (s.StartsWith(rename, StringComparison.Ordinal)) found.Add($"tab {i}: {s}");
                }
            }

            _out.WriteLine($"{folderButtons} '+ Folder' buttons seen across {tabs.Items.Count} tabs");
            // The control: the toolbars were really looked at.
            Assert.True(folderButtons >= 8, "the tabs' toolbars were not found");
            Assert.Empty(found);
        });
    }

    [Fact]
    public void F2OnASelectedFolderRenamesItWhereItStands()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.SelectedTabIndex = TabScenes;
            vm.AddSceneCommand.Execute(null);
            WindowHarness.Pump();
            vm.SceneTree.AddFolder();
            WindowHarness.Pump();
            var folder = vm.SceneTree.Tree.OfType<UnitFolderNode>().Last();
            vm.SceneTree.Selected = folder;

            bool prompted = false;
            vm.PromptForText = (_, _, _) => { prompted = true; return null; };
            vm.RenameItemCommand.Execute(null);
            WindowHarness.Pump();

            Assert.False(prompted, "a folder should not be renamed through the record prompt");
            Assert.True(folder.IsRenaming);

            // The box the name turned into, with the old name in it.
            var box = Descendants<TextBox>(window).First(b => ReferenceEquals(b.DataContext, folder));
            Assert.True(box.IsVisible);
            Assert.Equal(folder.Name, box.Text);

            box.Text = "Night scenes";
            PressEnter(box);
            WindowHarness.Pump();

            Assert.False(folder.IsRenaming);
            Assert.Equal("Night scenes", folder.Name);
            Assert.Contains(vm.Pack.SceneFolders, f => f.Name == "Night scenes");
        });
    }

    [Fact]
    public void EscapeGivesTheFolderItsNameBack()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.SelectedTabIndex = TabScenes;
            vm.SceneTree.AddFolder();
            WindowHarness.Pump();
            var folder = vm.SceneTree.Tree.OfType<UnitFolderNode>().Last();
            string was = folder.Name;
            folder.IsRenaming = true;
            WindowHarness.Pump();

            var box = Descendants<TextBox>(window).First(b => ReferenceEquals(b.DataContext, folder));
            box.Text = "Something else";
            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            WindowHarness.Pump();

            Assert.False(folder.IsRenaming);
            Assert.Equal(was, folder.Name);
        });
    }
}
