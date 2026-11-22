using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A screen's objects put back where they are (1.10.0, UiViewModel.ReadNodesAgain):
/// an undo or a redo of something done to one object of a screen costs that
/// object, not the screen read and every row of its tree built again. Held in
/// the real window, the key really pressed:
/// <list type="bullet">
/// <item>an object moved, something added to what a click of it does, an object
/// added inside another, one duplicated, one moved among its siblings, one
/// removed: each taken back and done again, the pack exactly what it was, and
/// every row the step did not touch still the row it was;</item>
/// <item>control: something of the screen itself changed goes the longer way,
/// and the rows are others afterwards - so "the same rows" can fail;</item>
/// <item>what the game says of its own objects and a pack never saves (a
/// button's resting tint, a label's style) is still on them after an undo,
/// whichever way it was put back - control: the game says something of some
/// object of the screen.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UiObjectUndoTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-uiundo-" + Guid.NewGuid().ToString("N"));

    public UiObjectUndoTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static IEnumerable<UiNodeViewModel> Flat(IEnumerable<UiNodeViewModel> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            foreach (var deeper in Flat(row.Children)) yield return deeper;
        }
    }

    private static void Press(MainWindow window, Key key)
    {
        RealKeys.Press(window, key, ModifierKeys.Control);
        WindowHarness.Pump();
    }

    private void Run(Action<MainWindow, MainViewModel, Func<string>> body)
    {
        PackRepository.Save(UndoCrawlPack.Make(_dir), _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Ui;
            WindowHarness.Pump();
            try { body(window, vm, () => PackRepository.Serialize(vm.Pack)); }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    [Fact]
    public void WhatIsDoneToAnObjectIsTakenBackWhereItIs_AndEveryOtherRowIsTheRowItWas()
    {
        Run((window, vm, state) =>
        {
            var ui = vm.Uis.First(u => !u.Model.IsVanillaBased && Flat(u.Nodes).Any(r => r.Children.Count >= 2));
            vm.SelectedUi = ui;
            WindowHarness.Pump();
            var holder = Flat(ui.Nodes).First(r => r.Children.Count >= 2);
            var row = holder.Children[1];
            ui.SelectedNode = row;
            WindowHarness.Pump();
            vm.Undo.Checkpoint();
            string before = state();
            var rows = Flat(ui.Nodes).ToList();

            void Back(string what)
            {
                Press(window, Key.Z);
                _out.WriteLine($"{what}: taken back by {vm.LastRestore}");
                Assert.Equal("values", vm.LastRestore);
                Assert.Equal(before, state());
                Assert.Same(ui, vm.SelectedUi);
                var now = Flat(ui.Nodes).ToList();
                Assert.Equal(rows.Count, now.Count);
                for (int i = 0; i < rows.Count; i++) Assert.Same(rows[i], now[i]);
                // And each row is under the row that holds it.
                foreach (var r in now)
                    foreach (var child in r.Children) Assert.Same(r, child.Parent);
            }

            void Again(string what, string wanted)
            {
                Press(window, Key.Y);
                Assert.Equal("values", vm.LastRestore);
                Assert.Equal(wanted, state());
                foreach (var kept in rows) Assert.Contains(kept, Flat(ui.Nodes));
            }

            // Moved.
            row.PositionX += 25;
            vm.Undo.Checkpoint();
            string moved = state();
            Assert.NotEqual(before, moved);
            float there = row.PositionX;
            Back("an object moved");
            Assert.Equal(there - 25, row.PositionX);
            Assert.Same(row, ui.SelectedNode);
            Again("an object moved", moved);
            Assert.Equal(there, row.PositionX);
            Back("an object moved, again");

            // Something added to what a click does: its row goes with it and comes back with it.
            int actions = row.Actions.Count;
            row.AddActionCommand.Execute(null);
            WindowHarness.Pump();
            string clicking = state();
            Assert.Equal(actions + 1, row.Actions.Count);
            Back("an action added");
            Assert.Equal(actions, row.Actions.Count);
            Again("an action added", clicking);
            Assert.Equal(actions + 1, row.Actions.Count);
            Assert.Same(row.Model.OnClick[^1], row.Actions[^1].Model);
            Back("an action added, again");

            // An object added inside another.
            ui.SelectedNode = holder;
            ui.AddChildCommand.Execute(null);
            WindowHarness.Pump();
            string added = state();
            Assert.Equal(rows.Count + 1, Flat(ui.Nodes).Count());
            Back("an object added");
            Again("an object added", added);
            Assert.Equal(rows.Count + 1, Flat(ui.Nodes).Count());
            Back("an object added, again");

            // One duplicated: beside the one it was made from - and what the author copied stays copied.
            SMSModForge.Services.EditorClipboard.SetItem(new UiNodeDef { Name = "Copied before" });
            ui.SelectedNode = row;
            ui.DuplicateNodeCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal("Copied before", SMSModForge.Services.EditorClipboard.GetItem<UiNodeDef>()?.Name);
            string doubled = state();
            Assert.Equal(rows.Count + 1, Flat(ui.Nodes).Count());
            Back("an object duplicated");
            Assert.Equal(holder.Children.IndexOf(row) < holder.Children.Count - 1, row.MoveDownCommand.CanExecute(null));
            Again("an object duplicated", doubled);
            Back("an object duplicated, again");

            // One moved among its siblings.
            var firstChild = holder.Children[0];
            firstChild.MoveDownCommand.Execute(null);
            WindowHarness.Pump();
            string reordered = state();
            Assert.NotEqual(before, reordered);
            Assert.Same(firstChild, holder.Children[1]);
            Back("an object moved down");
            Assert.Same(firstChild, holder.Children[0]);
            Again("an object moved down", reordered);
            Assert.Same(firstChild, holder.Children[1]);
            Back("an object moved down, again");

            // One removed: it comes back where it was, with what was inside it.
            ui.SelectedNode = holder;
            ui.AddChildCommand.Execute(null);
            WindowHarness.Pump();
            var made = ui.SelectedNode!;
            made.AddChildCommand.Execute(null);
            WindowHarness.Pump();
            vm.Undo.Checkpoint();
            string withTwo = state();
            var rowsWithTwo = Flat(ui.Nodes).ToList();
            ui.SelectedNode = made;
            ui.RemoveNodeCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal(before, state());
            Press(window, Key.Z);
            Assert.Equal("values", vm.LastRestore);
            Assert.Equal(withTwo, state());
            Assert.Equal(rowsWithTwo.Count, Flat(ui.Nodes).Count());
            foreach (var kept in rows) Assert.Contains(kept, Flat(ui.Nodes));
            Press(window, Key.Y);
            Assert.Equal(before, state());
            Press(window, Key.Z);
            Press(window, Key.Z);   // the object inside it
            Press(window, Key.Z);   // the object itself
            Assert.Equal(before, state());

            // Control: something of the screen itself goes the longer way, and the rows are others.
            ui.StartsOpen = !ui.StartsOpen;
            vm.Undo.Checkpoint();
            Assert.NotEqual(before, state());
            Press(window, Key.Z);
            _out.WriteLine("the screen's own value: taken back by " + vm.LastRestore);
            Assert.NotEqual("values", vm.LastRestore);
            Assert.Equal(before, state());
            var after = vm.Uis.First(u => u.Model.Id == ui.Model.Id);
            Assert.DoesNotContain(row, Flat(after.Nodes));
        });
    }

    /// <summary>
    /// Back to the game's, on an object of a game's screen, takes away what the pack added to it - and its
    /// rows with it. They stayed listed until 1.10.0: the object's values were replaced and its row was told
    /// to read them again, which did not make the rows of its lists again. The same reading-again is what an
    /// undo put back where it is relies on.
    /// </summary>
    [Fact]
    public void AnObjectPutBackToTheGamesShowsNoRowsOfWhatWasTakenAway()
    {
        Run((window, vm, state) =>
        {
            var screen = vm.Uis.FirstOrDefault(u => u.Model.IsVanillaBased && u.Nodes.Count > 0);
            if (screen == null || !VanillaUiLibrary.IsAvailable)
            {
                _out.WriteLine("no screen of the game's to read here - nothing held");
                return;
            }
            vm.SelectedUi = screen;
            WindowHarness.Pump();
            var row = Flat(screen.Nodes).First(r => r.Model.IsBound && r.Parent != null && r.Actions.Count == 0
                                                    && r.ActiveConditions.Count == 0 && r.ClickConditions.Count == 0 && r.Components.Count == 0);
            screen.SelectedNode = row;
            WindowHarness.Pump();
            vm.Undo.Checkpoint();
            string before = state();

            row.AddActionCommand.Execute(null);
            row.AddActiveConditionCommand.Execute(null);
            row.AddClickConditionCommand.Execute(null);
            row.AddComponentCommand.Execute(null);
            WindowHarness.Pump();
            // Control: the rows are there to be taken away.
            Assert.Equal((1, 1, 1, 1), (row.Actions.Count, row.ActiveConditions.Count, row.ClickConditions.Count, row.Components.Count));
            string added = state();
            Assert.NotEqual(before, added);

            Assert.True(row.ResetCommand.CanExecute(null));
            row.ResetCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal(before, state());
            Assert.Equal((0, 0, 0, 0), (row.Actions.Count, row.ActiveConditions.Count, row.ClickConditions.Count, row.Components.Count));

            // And taken back, they are there again: the rows of what the object has now.
            Press(window, Key.Z);
            Assert.Equal(added, state());
            Assert.Equal((1, 1, 1, 1), (row.Actions.Count, row.ActiveConditions.Count, row.ClickConditions.Count, row.Components.Count));
            Assert.Same(row.Model.OnClick[0], row.Actions[0].Model);
            Assert.Same(row.Model.Components[0], row.Components[0].Model);
        });
    }

    [Fact]
    public void WhatTheGameSaysOfItsOwnObjectsIsStillOnThemAfterAnUndo()
    {
        Run((window, vm, state) =>
        {
            var first = vm.Uis.FirstOrDefault(u => u.Model.IsVanillaBased && u.Nodes.Count > 0);
            if (first == null || !VanillaUiLibrary.IsAvailable)
            {
                _out.WriteLine("no screen of the game's to read here - nothing held");
                return;
            }
            string source = first.Model.Source;
            UiViewModel Screen() => vm.Uis.First(u => u.Model.Source == source);
            vm.SelectedUi = first;
            WindowHarness.Pump();
            Dictionary<string, string> Look() => Flat(Screen().Nodes).Where(r => r.Model.IsBound)
                .GroupBy(r => r.Model.Bind).ToDictionary(g => g.Key, g => g.First().Model.GameTint + "|" + (g.First().Model.Text?.GameStyle ?? ""));
            var before = Look();
            int said = before.Values.Count(v => v != "|");
            _out.WriteLine($"{before.Count} objects of the game's, {said} with something the game says of them");
            Assert.True(said > 0, "control: the game says nothing of any object of this screen - the check cannot tell");
            vm.Undo.Checkpoint();
            string packBefore = state();

            // A label of the game's given other words: its text is a record of its own, replaced by the step's.
            var label = Flat(Screen().Nodes).FirstOrDefault(r => r.Model.IsBound && !string.IsNullOrEmpty(r.Model.Text?.GameStyle))
                        ?? Flat(Screen().Nodes).First(r => r.Model.IsBound && r.Model.Text != null);
            Screen().SelectedNode = label;
            WindowHarness.Pump();
            label.Text += " (changed)";
            vm.Undo.Checkpoint();
            Assert.NotEqual(packBefore, state());
            Press(window, Key.Z);
            _out.WriteLine("a label's words: taken back by " + vm.LastRestore);
            Assert.Equal("values", vm.LastRestore);
            Assert.Equal(packBefore, state());
            Assert.Equal(before, Look());
            Press(window, Key.Y);
            Assert.Equal(before, Look());
            Press(window, Key.Z);
            Assert.Equal(before, Look());

            // Something of the screen itself: the screen is read back whole.
            Screen().StartsOpen = !Screen().StartsOpen;
            vm.Undo.Checkpoint();
            Assert.NotEqual(packBefore, state());
            Press(window, Key.Z);
            _out.WriteLine("the screen's own value: taken back by " + vm.LastRestore);
            Assert.NotEqual("values", vm.LastRestore);
            Assert.Equal(packBefore, state());
            Assert.Equal(before, Look());
            Press(window, Key.Y);
            Assert.Equal(before, Look());
        });
    }
}
