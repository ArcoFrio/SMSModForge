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
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The buttons under a conversation's nodes (the author, 1.8.0): no "+ Root";
/// "+ Sibling" then "+ Child"; an empty conversation shows "+ New Node" alone;
/// every + moves to the new line; only Duplicate and copy and paste carry a
/// whole node - the + buttons take the speaker and nothing else. And in the
/// Dialogues tab the keys are about whichever was clicked last, the dialogue
/// or its nodes, with that bar's key caps drawn full.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NodeButtonsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public NodeButtonsTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-nodebuttons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const int TabDialogues = MainTab.Dialogues;

    /// <summary>A conversation whose first line has everything a line can
    /// have, and a second line under it that jumps back to it.</summary>
    private MainViewModel Opened(MainWindow window)
    {
        var pack = PackRepository.CreateEmpty("nodebuttons.pack");
        pack.Characters.Add(new CharacterDef { Key = "kiki", DisplayName = "Kiki" });
        var d = new DialogueDef { Key = "chat", DisplayName = "Chat" };
        var first = new DialogueNodeDef
        {
            Id = 1, Actor = "kiki", Expression = "Happy", Outfit = "Swimsuit", Text = "Hello!", Tag = "start",
            Duration = NodeDurationMode.Timeout,
        };
        first.Conditions.Add(new NodeConditionDef { Type = NodeConditionTypes.LevelActive });
        first.ActionsOnStart.Add(new NodeActionDef { Type = NodeActionTypes.SetGameObjectActive });
        first.Children.Add(2);
        var second = new DialogueNodeDef { Id = 2, Actor = "kiki", Text = "Again?", Jump = new JumpDef { Mode = JumpMode.Jump, TargetTag = "start" } };
        d.Nodes.Add(first);
        d.Nodes.Add(second);
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        pack.Dialogues.Add(new DialogueDef { Key = "empty", DisplayName = "Empty" });
        PackRepository.Save(pack, _dir);

        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        vm.SelectedTabIndex = TabDialogues;
        WindowHarness.Pump();
        return vm;
    }

    private static DialogueViewModel Open(MainViewModel vm, string key)
    {
        vm.SelectedDialogue = vm.Dialogues.Single(x => x.Key == key);
        WindowHarness.Pump();
        return vm.SelectedDialogue!;
    }

    [Fact]
    public void ThePlusButtonsTakeTheSpeakerAndNothingElse()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var chat = Open(vm, "chat");
            foreach (var command in new[] { vm.AddDialogueChildNodeCommand, vm.AddDialogueSiblingNodeCommand })
            {
                vm.SelectedNode = chat.Nodes.Single(n => n.Id == 1);
                command.Execute(null);
                WindowHarness.Pump();
                var made = vm.SelectedNode!.Model;
                _out.WriteLine($"made {made.Id}: actor '{made.Actor}', {made.Conditions.Count} conditions, {made.ActionsOnStart.Count} actions, {made.Duration}");
                Assert.NotEqual(1, made.Id);
                Assert.Equal(("kiki", "Happy", "Swimsuit"), (made.Actor, made.Expression, made.Outfit));
                Assert.Empty(made.Conditions);
                Assert.Empty(made.ActionsOnStart);
                Assert.Equal(NodeDurationMode.UntilInteraction, made.Duration);
                Assert.True(string.IsNullOrEmpty(made.Tag));
                Assert.Equal("", made.Text);
            }
        });
    }

    [Fact]
    public void DuplicateCopiesANodeWholeWithWhatIsUnderIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var chat = Open(vm, "chat");
            Services.EditorClipboard.SetNodes(Array.Empty<DialogueNodeDef>());
            vm.SelectedNode = chat.Nodes.Single(n => n.Id == 1);
            vm.DuplicateNodeCommand.Execute(null);
            WindowHarness.Pump();

            var copy = vm.SelectedNode!.Model;
            var under = chat.Model.Nodes.Single(n => n.Id == copy.Children.Single());
            _out.WriteLine($"copy {copy.Id} tagged '{copy.Tag}', its child {under.Id} jumps to '{under.Jump?.TargetTag}'");
            Assert.NotEqual(1, copy.Id);
            Assert.Equal("Hello!", copy.Text);
            Assert.Single(copy.Conditions);
            Assert.Single(copy.ActionsOnStart);
            Assert.Equal("Again?", under.Text);
            // Directly below the original, at its level.
            Assert.Equal(new[] { 1, copy.Id }, chat.Model.RootNodeIds);
            // No second "start": the copy's tag is its own, and its jump follows it.
            Assert.Equal("start_copy", copy.Tag);
            Assert.Equal("start_copy", under.Jump!.TargetTag);
            Assert.Equal("start", chat.Model.Nodes.Single(n => n.Id == 2).Jump!.TargetTag);
            // The clipboard is not touched.
            Assert.False(Services.EditorClipboard.HasNodes);
        });
    }

    /// <summary>What the bar under the nodes shows, left to right.</summary>
    private static List<string> Shown(NavigatorBar bar)
        => bar.Items.OfType<StackPanel>().Where(s => s.IsVisible)
              .Select(s => s.Children[0] switch
              {
                  Button b when (b.Tag as string)?.StartsWith("nav:") == true => b.Tag + "=" + b.Content,
                  Button b => "own=" + b.Content,
                  FrameworkElement f => f.Tag as string ?? "?",
                  _ => "?",
              }).ToList();

    [Fact]
    public void AnEmptyConversationShowsNewNodeAlone()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Open(vm, "empty");
            window.UpdateLayout();
            var bar = (NavigatorBar)window.FindName("NodeNavigator");
            var empty = Shown(bar);
            _out.WriteLine("empty: " + string.Join(" | ", empty));
            Assert.Equal(new[] { "nav:add=" + Loc.T("dialogues.addFirstNode"), "nav:keys" }, empty);

            bar.Role("add")!.Command.Execute(null);
            WindowHarness.Pump();
            window.UpdateLayout();
            var then = Shown(bar);
            _out.WriteLine("then: " + string.Join(" | ", then));
            Assert.Equal(new[]
            {
                "nav:add=" + Loc.T("dialogues.addDialogueSiblingNode"),
                "own=" + Loc.T("dialogues.addDialogueChildNode"),
                "nav:duplicate=" + Loc.T("navigator.duplicate"),
                "nav:delete=" + Loc.T("dialogues.removeDialogue"),
                "nav:keys",
            }, then);
        });
    }

    [Fact]
    public void EveryPlusMovesToTheNewLine()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var chat = Open(vm, "chat");
            var list = (ListBox)window.FindName("NodeList");
            foreach (var command in new[] { vm.AddDialogueSiblingNodeCommand, vm.AddDialogueChildNodeCommand, vm.DuplicateNodeCommand })
            {
                var from = vm.SelectedNode = chat.Nodes.Single(n => n.Id == 1);
                WindowHarness.Pump();
                var fromRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(from);
                fromRow.Focus();
                command.Execute(null);
                WindowHarness.Pump();

                var made = vm.SelectedNode!;
                var focused = FocusManager.GetFocusedElement(window);
                _out.WriteLine($"made {made.Id}, focus on {((focused as ListBoxItem)?.DataContext is DialogueNodeViewModel n ? n.Id.ToString() : focused?.GetType().Name)}");
                Assert.NotSame(from, made);
                Assert.Same(list.ItemContainerGenerator.ContainerFromItem(made), focused);
            }
        });
    }

    private static void Click(UIElement on)
    {
        on.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
        WindowHarness.Pump();
    }

    [Fact]
    public void CtrlDCopiesWhicheverWasClickedLast()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var chat = Open(vm, "chat");
            vm.SelectedNode = chat.Nodes.Single(n => n.Id == 1);
            WindowHarness.Pump();
            var nodesBar = (NavigatorBar)window.FindName("NodeNavigator");
            var tree = (UIElement)window.FindName("DialogueTreeView");
            var dialoguesBar = Descendants<NavigatorBar>(window).Single(b => ReferenceEquals(b.List, tree));

            // A node's row, then the node's Text: still the node.
            Click((UIElement)window.FindName("NodeList"));
            Assert.True(vm.NodeKeysActive);
            Click((UIElement)window.FindName("NodeEditorPane"));
            Assert.True(vm.NodeKeysActive);
            Assert.True(nodesBar.KeysActive == true);
            Assert.True(dialoguesBar.KeysActive == false);
            int nodes = chat.Nodes.Count, dialogues = vm.Dialogues.Count;
            vm.DuplicateHereCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal((nodes + 2, dialogues), (chat.Nodes.Count, vm.Dialogues.Count));   // the line and its child

            // The dialogue list: the dialogue.
            Click((UIElement)window.FindName("DialogueTreeView"));
            Assert.False(vm.NodeKeysActive);
            Assert.True(nodesBar.KeysActive == false);
            Assert.True(dialoguesBar.KeysActive == true);
            vm.DuplicateHereCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal(dialogues + 1, vm.Dialogues.Count);
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }
}
