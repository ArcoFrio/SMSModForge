using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// One history for the whole window (MainWindow.History.cs, 1.10.0), held in the
/// real window on the things a person does:
/// <list type="bullet">
/// <item>Ctrl+Z with the keyboard in a box is the editor's undo, not the box's
/// own - control: a box the window has not met keeps its own;</item>
/// <item>a tick is a step as the mouse is let go, and two on the same tick are
/// two steps - control: the mouse let go with nothing changed looks at
/// nothing;</item>
/// <item>typing is cut into steps where the caret is moved and where typing
/// turns to deleting - control: two runs of letters with nothing between are
/// one;</item>
/// <item>after an undo that builds the page again, the keyboard is in the same
/// box, of the record now shown, with the caret where the change was;</item>
/// <item>a box that only loses the keyboard, or has a shortcut pressed in it,
/// writes nothing into the pack - control: typed in, it does;</item>
/// <item>a folder added, taken back and added again is added (the tree's own
/// pick was a folder no longer in it).</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UndoKeysTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-undokeys-" + Guid.NewGuid().ToString("N"));

    public UndoKeysTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static IEnumerable<T> Under<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement { IsVisible: false }) continue;
            if (child is T hit) yield return hit;
            foreach (var deeper in Under<T>(child)) yield return deeper;
        }
    }

    private static DependencyObject Page(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        return (DependencyObject)tabs.Template.FindName("PART_SelectedContentHost", tabs);
    }

    private static string? PathOf(DependencyObject d, DependencyProperty property) => BindingOperations.GetBinding(d, property)?.Path?.Path;

    private static TextBox Box(MainWindow window, string path, Type of)
        => Under<TextBox>(Page(window)).First(b => b.IsEnabled && !b.IsReadOnly && PathOf(b, TextBox.TextProperty) == path && of.IsInstanceOfType(b.DataContext));

    /// <summary>The key itself, pressed: down WPF's own route from whatever has the keyboard (RealKeys).</summary>
    private static void Press(MainWindow window, Key key, ModifierKeys held = ModifierKeys.Control)
    {
        RealKeys.Press(window, key, held);
        WindowHarness.Pump();
    }

    /// <summary>The keyboard off whatever has it and somewhere else in the window (RealKeys.Elsewhere).</summary>
    private static void OnTheWindow(MainWindow window) => RealKeys.Elsewhere(window);

    private static void Into(TextBox box)
    {
        box.Focus();
        Keyboard.Focus(box);
        WindowHarness.Pump();
    }

    private static void KeyDown(UIElement on, Key key)
        => on.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(on)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static void Mouse(UIElement on, RoutedEvent what)
        => on.RaiseEvent(new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = what });

    private static readonly MethodInfo Click = typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private void Run(int tab, Action<MainWindow, MainViewModel, Func<string>> body)
    {
        PackRepository.Save(UndoCrawlPack.Make(_dir), _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = tab;
            WindowHarness.Pump();
            try { body(window, vm, () => PackRepository.Serialize(vm.Pack)); }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    [Fact]
    public void CtrlZWithTheKeyboardInABoxIsTheEditors_AndTheKeyboardStaysInTheBox()
    {
        Run(MainTab.Characters, (window, vm, state) =>
        {
            // A character whose name can be typed over, whichever box the page gives it.
            TextBox? NameBox() => Under<TextBox>(Page(window)).FirstOrDefault(b => b.IsEnabled && !b.IsReadOnly && b.DataContext is CharacterViewModel
                                                                                  && PathOf(b, TextBox.TextProperty) is "Name" or "NameBox");
            TextBox? found = null;
            foreach (var c in vm.Characters)
            {
                vm.SelectedCharacter = c;
                WindowHarness.Pump();
                if ((found = NameBox()) != null) break;
            }
            Assert.NotNull(found);
            var box = found!;
            string path = PathOf(box, TextBox.TextProperty)!;
            string key = vm.SelectedCharacter!.Key;
            string before = state(), was = box.Text;

            // Control: before the keyboard has been in it, the box is as Windows makes it - it remembers what
            // is typed in it, and would take it back itself.
            Assert.True(box.IsUndoEnabled);
            box.CaretIndex = box.Text.Length;
            box.SelectedText = "typed";
            Assert.True(box.CanUndo, "control: a text box of Windows' own has nothing to undo after typing - the check cannot tell");
            box.Undo();
            WindowHarness.Pump();
            Assert.Equal(was, box.Text);
            vm.Undo.Checkpoint();
            Assert.Equal(before, state());

            Into(box);
            box.CaretIndex = box.Text.Length;
            box.SelectedText = " the Second";
            WindowHarness.Pump();

            // With the keyboard in it, the box does not answer Ctrl+Z itself.
            Assert.False(box.CanUndo, "the box would answer Ctrl+Z itself");

            Press(window, Key.Z);
            _out.WriteLine("put back by: " + vm.LastRestore);
            Assert.Equal(before, state());
            Assert.Equal(key, vm.SelectedCharacter?.Key);
            var now = Keyboard.FocusedElement as TextBox;
            Assert.NotNull(now);
            Assert.Equal(path, PathOf(now!, TextBox.TextProperty));
            Assert.Same(vm.SelectedCharacter, now.DataContext);
            Assert.Equal(was, now.Text);
            Assert.Equal(was.Length, now.CaretIndex);

            // Forward again, by either key.
            Press(window, Key.Y);
            string again = state();
            Assert.NotEqual(before, again);
            Assert.Equal(was + " the Second", NameBox()!.Text);
            Press(window, Key.Z);
            Assert.Equal(before, state());
            Press(window, Key.Z, ModifierKeys.Control | ModifierKeys.Shift);
            Assert.Equal(again, state());
            Press(window, Key.Z);
            Assert.Equal(before, state());
        });
    }

    /// <summary>
    /// The key itself, pressed (RealKeys): down WPF's own route from whatever has the keyboard. Every other
    /// test here runs the window's shortcut by hand, which says what the shortcut does and nothing about
    /// whether the key gets to it - and from a text box it did not: the box maps Ctrl+Z to its own Undo, has
    /// nothing to undo (its own memory is off), and WPF marks a key mapped to a command as dealt with whether
    /// or not the command could run. The window answers the Undo and Redo commands themselves for that.
    /// </summary>
    [Fact]
    public void TheKeyItselfPressedWhereverTheKeyboardIsReachesTheEditor()
    {
        Run(MainTab.Characters, (window, vm, state) =>
        {
            RealKeys.Watch(window);

            // Control: the press is a real one - a box of Windows' own, in a window of its own, takes its
            // typing back at it.
            var loose = new TextBox { Width = 200 };
            var other = new Window { Width = 300, Height = 120, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = loose };
            other.Show();
            other.Activate();
            loose.Focus();
            Keyboard.Focus(loose);
            WindowHarness.Pump();
            Assert.True(loose.IsKeyboardFocused, "control: the loose box never had the keyboard");
            loose.SelectedText = "typed";
            Assert.Equal("typed", loose.Text);
            RealKeys.Press(other, Key.Z, ModifierKeys.Control);
            WindowHarness.Pump();
            Assert.Equal("", loose.Text);
            other.Close();
            window.Activate();
            WindowHarness.Pump();

            // A text box.
            TextBox? NameBox() => Under<TextBox>(Page(window)).FirstOrDefault(b => b.IsEnabled && !b.IsReadOnly && b.DataContext is CharacterViewModel
                                                                                  && PathOf(b, TextBox.TextProperty) is "Name" or "NameBox");
            TextBox? box = null;
            foreach (var c in vm.Characters)
            {
                vm.SelectedCharacter = c;
                WindowHarness.Pump();
                if ((box = NameBox()) != null) break;
            }
            Assert.NotNull(box);
            vm.Undo.Checkpoint();
            string before = state();
            Into(box!);
            Assert.True(box!.IsKeyboardFocused, "the name's box never had the keyboard");
            box.CaretIndex = box.Text.Length;
            box.SelectedText = " the Third";
            WindowHarness.Pump();

            RealKeys.Press(window, Key.Z, ModifierKeys.Control);
            WindowHarness.Pump();
            Assert.Equal(before, state());
            RealKeys.Press(window, Key.Y, ModifierKeys.Control);
            WindowHarness.Pump();
            string edited = state();
            Assert.NotEqual(before, edited);
            Assert.Contains(" the Third", NameBox()!.Text);
            RealKeys.Press(window, Key.Z, ModifierKeys.Control);
            WindowHarness.Pump();
            Assert.Equal(before, state());
            RealKeys.Press(window, Key.Z, ModifierKeys.Control | ModifierKeys.Shift);
            WindowHarness.Pump();
            Assert.Equal(edited, state());
            RealKeys.Press(window, Key.Z, ModifierKeys.Control);
            WindowHarness.Pump();
            Assert.Equal(before, state());

            // A tick with the keyboard on it.
            vm.SelectedCharacter = vm.Characters.First(c => c.Outfits.Count > 0 && !c.Model.IsVanillaCharacter);
            WindowHarness.Pump();
            var tick = Under<CheckBox>(Page(window)).First(c => c.IsEnabled && PathOf(c, ToggleButton.IsCheckedProperty) == "PixelSnap");
            vm.Undo.Checkpoint();
            before = state();
            tick.Focus();
            Keyboard.Focus(tick);
            WindowHarness.Pump();
            Click.Invoke(tick, null);
            WindowHarness.Pump();
            Assert.NotEqual(before, state());
            RealKeys.Press(window, Key.Z, ModifierKeys.Control);
            WindowHarness.Pump();
            Assert.Equal(before, state());

            // The line's box.
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Dialogues;
            WindowHarness.Pump();
            vm.SelectedDialogue = vm.Dialogues.First(d => d.Nodes.Count > 0);
            vm.SelectedNode = vm.SelectedDialogue.Nodes.First(n => !string.IsNullOrEmpty(n.Text));
            WindowHarness.Pump();
            var line = (MarkupTextBox)window.FindName("NodeTextBox");
            line.Focus();
            Keyboard.Focus(line);
            WindowHarness.Pump();
            Assert.True(line.IsKeyboardFocusWithin, "the line's box never had the keyboard");
            vm.Undo.Checkpoint();
            before = state();
            string said = line.Text;
            line.CaretIndex = line.Text.Length;
            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, line, "!"));
            WindowHarness.Pump();
            Assert.Equal(said + "!", line.Text);
            RealKeys.Press(window, Key.Z, ModifierKeys.Control);
            WindowHarness.Pump();
            Assert.Equal(before, state());
            Assert.Equal(said, line.Text);
            RealKeys.Press(window, Key.Y, ModifierKeys.Control);
            WindowHarness.Pump();
            Assert.Equal(said + "!", line.Text);
            RealKeys.Press(window, Key.Z, ModifierKeys.Control);
            WindowHarness.Pump();
            Assert.Equal(before, state());
        });
    }

    [Fact]
    public void AnUndoGoesToTheTabAndTheRecordOfTheChange_AndMarksWhatItChanged()
    {
        Run(MainTab.Characters, (window, vm, state) =>
        {
            var tabs = (TabControl)window.FindName("MainTabs");
            TextBox? NameBox() => Under<TextBox>(Page(window)).FirstOrDefault(b => b.IsEnabled && !b.IsReadOnly && b.DataContext is CharacterViewModel
                                                                                  && PathOf(b, TextBox.TextProperty) is "Name" or "NameBox");
            TextBox? box = null;
            foreach (var c in vm.Characters.Reverse())
            {
                vm.SelectedCharacter = c;
                WindowHarness.Pump();
                if ((box = NameBox()) != null) break;
            }
            Assert.NotNull(box);
            string key = vm.SelectedCharacter!.Key, was = box!.Text, before = state();
            Into(box);
            box.CaretIndex = box.Text.Length;
            box.SelectedText = " Renamed";
            OnTheWindow(window);
            WindowHarness.Pump();
            string edited = state();
            Assert.NotEqual(before, edited);

            // Somewhere else altogether: another tab, and another character picked on the way.
            vm.SelectedCharacter = vm.Characters.First(c => c.Key != key);
            WindowHarness.Pump();
            tabs.SelectedIndex = MainTab.Variables;
            WindowHarness.Pump();
            Assert.Empty(window.LastMarked);

            Press(window, Key.Z);
            Assert.Equal(before, state());
            Assert.Equal(MainTab.Characters, tabs.SelectedIndex);
            Assert.Equal(key, vm.SelectedCharacter?.Key);
            _out.WriteLine($"undone ({vm.LastRestore}); marked: " + string.Join(", ", window.LastMarked.Select(m => m.GetType().Name + " [" + PathOf(m, TextBox.TextProperty) + "] " + ((m as TextBox)?.Text ?? ""))));
            Assert.NotEmpty(window.LastMarked);
            Assert.All(window.LastMarked, m => Assert.True(m is TextBox t && t.Text.Contains(was, StringComparison.Ordinal) && !t.Text.Contains("Renamed", StringComparison.Ordinal),
                                                           "something that does not show the name was marked"));
            Assert.Contains(window.LastMarked, m => PathOf(m, TextBox.TextProperty) is "Name" or "NameBox");

            // Away again, and forward: the same tab, the same record, the name marked with what it is now.
            tabs.SelectedIndex = MainTab.Dialogues;
            WindowHarness.Pump();
            Press(window, Key.Y);
            Assert.Equal(edited, state());
            Assert.Equal(MainTab.Characters, tabs.SelectedIndex);
            Assert.Equal(key, vm.SelectedCharacter?.Key);
            Assert.NotEmpty(window.LastMarked);
            Assert.All(window.LastMarked, m => Assert.Contains("Renamed", ((TextBox)m).Text));

            // Control: a step that changes nothing the page shows marks nothing.
            vm.Undo.Checkpoint();
            vm.Pack.Version = "9.9.9";
            vm.Undo.Checkpoint();
            Press(window, Key.Z);
            Assert.Empty(window.LastMarked);
        });
    }

    [Fact]
    public void ATickIsAStepAsTheMouseIsLetGo_AndTwoOnTheSameTickAreTwo()
    {
        Run(MainTab.Characters, (window, vm, state) =>
        {
            vm.SelectedCharacter = vm.Characters.First(c => c.Outfits.Count > 0 && !c.Model.IsVanillaCharacter);
            WindowHarness.Pump();
            var tick = Under<CheckBox>(Page(window)).First(c => c.IsEnabled && PathOf(c, ToggleButton.IsCheckedProperty) == "PixelSnap");
            vm.Undo.Checkpoint();
            string before = state();
            int depth = vm.Undo.Depth;

            // Control: the mouse pressed and let go with nothing changed is no step.
            Mouse(tick, System.Windows.Input.Mouse.PreviewMouseDownEvent);
            Mouse(tick, System.Windows.Input.Mouse.PreviewMouseUpEvent);
            WindowHarness.Pump();
            Assert.Equal(depth, vm.Undo.Depth);

            void ClickIt()
            {
                Mouse(tick, System.Windows.Input.Mouse.PreviewMouseDownEvent);
                Click.Invoke(tick, null);
                Mouse(tick, System.Windows.Input.Mouse.PreviewMouseUpEvent);
                WindowHarness.Pump();
            }
            ClickIt();
            string once = state();
            Assert.NotEqual(before, once);
            Assert.Equal(depth + 1, vm.Undo.Depth);
            ClickIt();
            Assert.Equal(before, state());
            Assert.Equal(depth + 2, vm.Undo.Depth);

            Press(window, Key.Z);
            Assert.Equal(once, state());
            Press(window, Key.Z);
            Assert.Equal(before, state());
            Assert.Equal(depth, vm.Undo.Depth);
        });
    }

    [Fact]
    public void TypingIsCutIntoStepsWhereTheCaretIsMovedAndWhereItTurnsToDeleting()
    {
        Run(MainTab.Places, (window, vm, state) =>
        {
            var place = vm.Places.First(p => p.GameObjects.Any(g => g.CanEditIdentity));
            vm.SelectedPlace = place;
            WindowHarness.Pump();
            // An object's settings are beside the place preview's picture, for the object picked (after 1.10.0).
            vm.PickInPreview(place.GameObjects.First(g => g.CanEditIdentity));
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            var box = Under<TextBox>(Page(window)).First(b => b.IsEnabled && b.IsVisible && PathOf(b, TextBox.TextProperty) == "Name" && b.DataContext is GameObjectViewModel);
            Assert.Equal(UpdateSourceTrigger.PropertyChanged, BindingOperations.GetBinding(box, TextBox.TextProperty)!.UpdateSourceTrigger);
            var thing = (GameObjectViewModel)box.DataContext;
            string was = thing.Name;
            vm.Undo.Checkpoint();
            int depth = vm.Undo.Depth;

            void Typed(string text)
            {
                KeyDown(box, Key.A);
                box.CaretIndex = box.Text.Length;
                box.SelectedText = text;
                box.CaretIndex = box.Text.Length;
                WindowHarness.Pump();
            }

            Into(box);
            // Control: two runs of letters with nothing between them are one step (none yet: the box still has
            // the keyboard).
            Typed("ab");
            Typed("cd");
            Assert.Equal(depth, vm.Undo.Depth);
            Assert.Equal(was + "abcd", thing.Name);

            // The caret moved by hand: what was typed so far is a step.
            KeyDown(box, Key.Left);
            Assert.Equal(depth + 1, vm.Undo.Depth);
            Typed("ef");
            // Deleting after typing: another.
            KeyDown(box, Key.Back);
            Assert.Equal(depth + 2, vm.Undo.Depth);
            box.Select(box.Text.Length - 1, 1);
            box.SelectedText = "";
            WindowHarness.Pump();
            Assert.Equal(was + "abcde", thing.Name);
            // More deleting: the same run.
            KeyDown(box, Key.Back);
            Assert.Equal(depth + 2, vm.Undo.Depth);

            // Back through them, one press each, the keyboard still in the box.
            GameObjectViewModel Thing() => Under<TextBox>(Page(window)).First(b => PathOf(b, TextBox.TextProperty) == "Name" && b.DataContext is GameObjectViewModel g && g.Model.Name.StartsWith(was, StringComparison.Ordinal)).DataContext as GameObjectViewModel
                                           ?? throw new InvalidOperationException("the object's row is gone");
            Press(window, Key.Z);
            Assert.Equal(was + "abcdef", Thing().Name);
            Press(window, Key.Z);
            Assert.Equal(was + "abcd", Thing().Name);
            Press(window, Key.Z);
            Assert.Equal(was, Thing().Name);
            Assert.True(Keyboard.FocusedElement is TextBox, "the keyboard left the box");
        });
    }

    [Fact]
    public void TheLinesBoxLeavesCtrlZToTheEditor_AndTheCaretIsWhereTheChangeWas()
    {
        Run(MainTab.Dialogues, (window, vm, state) =>
        {
            vm.SelectedDialogue = vm.Dialogues.First(d => d.Nodes.Any(n => n.Text.Length > 4));
            WindowHarness.Pump();
            vm.SelectedNode = vm.SelectedDialogue.Nodes.First(n => n.Text.Length > 4);
            WindowHarness.Pump();
            var box = (MarkupTextBox)window.FindName("NodeTextBox");
            string was = vm.SelectedNode.Text, before = state();

            // Control: on its own, the box keeps its history of the line.
            var own = new MarkupTextBox { Text = "a line" };
            own.CaretPosition = own.Document.ContentEnd;
            own.CaretPosition.InsertTextInRun(" typed");
            Assert.True(own.CanUndoLine, "control: the box on its own has nothing to undo after typing - the check cannot tell");

            box.Focus();
            Keyboard.Focus(box);
            WindowHarness.Pump();
            box.CaretPosition = box.Document.ContentEnd;
            box.CaretPosition.InsertTextInRun(" more");
            WindowHarness.Pump();
            Assert.Equal(was + " more", vm.SelectedNode.Text);
            Assert.False(box.CanUndoLine, "the line's box would answer Ctrl+Z itself");

            Press(window, Key.Z);
            _out.WriteLine("put back by: " + vm.LastRestore);
            Assert.Equal(before, state());
            Assert.Equal(was, vm.SelectedNode!.Text);
            Assert.Equal(was, box.Text);
            Assert.True(box.IsKeyboardFocusWithin, "the keyboard left the line's box");
            Assert.Equal(was.Length, box.SelectionStart);

            Press(window, Key.Y);
            Assert.Equal(was + " more", vm.SelectedNode!.Text);
            Assert.Equal(was + " more", box.Text);
            Press(window, Key.Z);
            Assert.Equal(before, state());
        });
    }

    [Fact]
    public void ABoxThatOnlyLosesTheKeyboardWritesNothing()
    {
        Run(MainTab.Music, (window, vm, state) =>
        {
            Assert.NotEmpty(vm.Music);
            vm.SelectedMusic = vm.Music[0];
            WindowHarness.Pump();
            var box = Box(window, "VolumeText", typeof(MusicViewModel));
            vm.Undo.Checkpoint();
            string before = state();
            int depth = vm.Undo.Depth;

            Into(box);
            OnTheWindow(window);
            WindowHarness.Pump();
            Assert.Equal(before, state());
            // A shortcut pressed in it: the same.
            Into(box);
            Press(window, Key.Y);
            Assert.Equal(before, state());
            vm.Undo.Checkpoint();
            Assert.Equal(depth, vm.Undo.Depth);

            // Control: typed in, it writes.
            Into(box);
            box.SelectAll();
            box.SelectedText = "0.5";
            OnTheWindow(window);
            WindowHarness.Pump();
            Assert.NotEqual(before, state());
            Assert.Equal(depth + 1, vm.Undo.Depth);
            Press(window, Key.Z);
            Assert.Equal(before, state());
            // And what was undone can be redone, with the keyboard in the box the undo left it in.
            Press(window, Key.Y);
            Assert.NotEqual(before, state());
            Press(window, Key.Z);
            Assert.Equal(before, state());
        });
    }

    [Fact]
    public void AFolderAddedTakenBackAndAddedAgainIsAdded()
    {
        Run(MainTab.Variables, (window, vm, state) =>
        {
            vm.Undo.Checkpoint();
            string before = state();
            foreach (var (name, add) in new (string, RelayCommand)[]
                     {
                         ("variables", vm.AddVariableFolderCommand), ("dialogues", vm.AddDialogueFolderCommand), ("rules", vm.AddIntegrationFolderCommand),
                     })
            {
                add.Execute(null);
                WindowHarness.Pump();
                string added = state();
                Assert.NotEqual(before, added);
                vm.UndoCommand.Execute(null);
                WindowHarness.Pump();
                Assert.Equal(before, state());
                add.Execute(null);
                WindowHarness.Pump();
                Assert.True(added == state(), name + ": the folder added again after an undo is not in the pack");
                vm.UndoCommand.Execute(null);
                WindowHarness.Pump();
                Assert.Equal(before, state());
            }
            // The lists with a tree of their own.
            foreach (var (name, tree) in new (string, UnitTreeController)[] { ("places", vm.PlaceTree), ("scenes", vm.SceneTree), ("quests", vm.QuestTree) })
            {
                vm.AddUnitFolderCommand.Execute(tree);
                WindowHarness.Pump();
                string added = state();
                Assert.NotEqual(before, added);
                vm.UndoCommand.Execute(null);
                WindowHarness.Pump();
                Assert.Equal(before, state());
                vm.AddUnitFolderCommand.Execute(tree);
                WindowHarness.Pump();
                Assert.True(added == state(), name + ": the folder added again after an undo is not in the pack");
                vm.UndoCommand.Execute(null);
                WindowHarness.Pump();
                Assert.Equal(before, state());
            }
        });
    }
}
