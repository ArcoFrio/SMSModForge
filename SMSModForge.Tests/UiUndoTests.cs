using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Undo and redo on the UI tab, driven the way a person drives them.
/// <para/>
/// Reported: Ctrl+Z drops the selection, so there is nothing on screen to tell
/// you whether it worked; and Ctrl+Y afterwards does not put the change back.
/// </summary>
public sealed class UiUndoTests
{
    private readonly ITestOutputHelper _out;
    public UiUndoTests(ITestOutputHelper output) => _out = output;

    private static int Children(MainViewModel vm)
        => vm.Pack.Uis.Count == 0 ? -1 : vm.Pack.Uis[0].Nodes[0].Children.Count;

    [Fact]
    public void Undo_then_redo_puts_the_change_back()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            WindowHarness.Pump();

            int before = Children(vm);
            ui.SelectedNode = ui.Nodes[0];
            ui.AddChildCommand.Execute(null);
            WindowHarness.Pump();

            int added = Children(vm);
            Assert.Equal(before + 1, added);

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            _out.WriteLine($"after undo: {Children(vm)} children (was {added}, expected {before})");
            Assert.Equal(before, Children(vm));

            vm.RedoCommand.Execute(null);
            WindowHarness.Pump();
            _out.WriteLine($"after redo: {Children(vm)} children (expected {added})");
            Assert.Equal(added, Children(vm));
        });
    }

    [Fact]
    public void Undo_keeps_you_looking_at_the_same_screen()
    {
        // Without this the change may well have been undone correctly and
        // there is no way to see it: the list clears, the preview empties, and
        // an author is left pressing Ctrl+Z again to find out.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("window"));
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            WindowHarness.Pump();

            var second = vm.Uis.Last();
            vm.SelectedUi = second;
            second.SelectedNode = second.Nodes[0];
            second.AddChildCommand.Execute(null);
            WindowHarness.Pump();

            string wasSelected = second.Model.Name;
            _out.WriteLine($"selected before undo: {wasSelected}");

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            _out.WriteLine($"selected after undo: {vm.SelectedUi?.Model.Name ?? "(nothing)"}");
            Assert.NotNull(vm.SelectedUi);
            Assert.Equal(wasSelected, vm.SelectedUi!.Model.Name);
        });
    }

    [Fact]
    public void Redo_still_works_after_looking_around()
    {
        // The reported sequence exactly: undo, click the record again to see
        // whether it worked, then redo.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.SelectedNode = ui.Nodes[0];
            WindowHarness.Pump();

            int before = Children(vm);
            ui.AddChildCommand.Execute(null);
            WindowHarness.Pump();
            int added = Children(vm);

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            // Click about, the way you would to check.
            vm.SelectedUi = vm.Uis.FirstOrDefault();
            WindowHarness.Pump();
            vm.SelectedUi = vm.Uis.LastOrDefault();
            WindowHarness.Pump();

            Assert.True(vm.RedoCommand.CanExecute(null), "redo went away just from looking");

            vm.RedoCommand.Execute(null);
            WindowHarness.Pump();
            _out.WriteLine($"after redo: {Children(vm)} (expected {added}, undone was {before})");
            Assert.Equal(added, Children(vm));
        });
    }

    [Fact]
    public void Redo_survives_selecting_a_vanilla_screen()
    {
        // The difference from the test above: selecting a vanilla screen SEEDS
        // it - a thousand-odd objects appear in the tree so they can be edited.
        // If that counts as an edit, merely looking at a screen wipes the redo
        // stack, and the author's next Ctrl+Y does nothing.
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            vm.AddVanillaUiCommand.Execute(null);
            var ui = vm.Uis.Last();
            ui.Source = "vanillaui:9_MainCanvas/Quitagme";
            vm.SelectedUi = ui;
            WindowHarness.Pump();

            var node = ui.Nodes[0].Children.First();
            ui.SelectedNode = node;
            ui.AddChildCommand.Execute(null);
            WindowHarness.Pump();
            int added = node.Model.Children.Count;

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            // Look at the screen again, the way you would to check.
            vm.SelectedUi = null;
            WindowHarness.Pump();
            vm.SelectedUi = vm.Uis.Last();
            WindowHarness.Pump();

            _out.WriteLine($"can redo after looking: {vm.RedoCommand.CanExecute(null)}");
            Assert.True(vm.RedoCommand.CanExecute(null),
                        "looking at a vanilla screen threw the redo away");
        });
    }

    [Fact]
    public void A_gizmo_drag_is_one_undo_step()
    {
        // A drag is neither a command nor a text field, so neither of the two
        // things that normally mark a step notices it. Left unmarked, Ctrl+Z
        // after moving something steps back past the move to whatever was done
        // before it - which reads as undo skipping.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            var node = ui.Nodes[0];
            ui.SelectedNode = node;
            WindowHarness.Pump();

            float wasX = node.Model.Rect.Position[0];

            // Exactly what the preview does: checkpoint, then move.
            vm.Undo.Checkpoint();
            UiDrag.Apply(node.Model, UiGrip.Move,
                         UiDragStart.Of(node.Model, default), 120, 0);
            WindowHarness.Pump();

            Assert.Equal(wasX + 120, node.Model.Rect.Position[0]);

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            var after = vm.Uis.Last().Nodes[0].Model.Rect.Position[0];
            _out.WriteLine($"x was {wasX}, dragged to {wasX + 120}, after undo {after}");
            Assert.Equal(wasX, after);
        });
    }

    [Fact]
    public void A_reorder_is_one_undo_step()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("dialog"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            WindowHarness.Pump();

            var root = ui.Nodes[0];
            var was = root.Model.Children.Select(c => c.Name).ToList();

            vm.Undo.Checkpoint();
            root.MoveBy(root.Children[0], +1);
            WindowHarness.Pump();
            Assert.NotEqual(was, root.Model.Children.Select(c => c.Name).ToList());

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            var after = vm.Uis.Last().Nodes[0].Model.Children.Select(c => c.Name).ToList();
            _out.WriteLine(string.Join(", ", after));
            Assert.Equal(was, after);
        });
    }
}
