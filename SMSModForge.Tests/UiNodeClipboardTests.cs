using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Copying objects about the UI tree, and the keys that do it.
/// <para/>
/// The property that matters is separateness: a card built once and pasted nine
/// times must be nine cards, not nine views of one. The clipboard clones on the
/// way in and again on the way out, so neither the original nor an earlier
/// paste can be changed by editing a later one.
/// </summary>
public sealed class UiNodeClipboardTests
{
    private readonly ITestOutputHelper _out;
    public UiNodeClipboardTests(ITestOutputHelper output) => _out = output;

    private static UiViewModel Screen()
    {
        var def = new UiDef { Id = "s", Name = "Screen" };
        var root = new UiNodeDef { Name = "Panel" };
        def.Nodes.Add(root);
        return new UiViewModel(def);
    }

    [Fact]
    public void A_copied_object_brings_everything_inside_it()
    {
        var ui = Screen();
        var card = ui.Nodes[0].AddChild(UiTemplate.Find("button")!.Build());
        ui.SelectedNode = card;

        ui.CopyNodeCommand.Execute(null);
        ui.SelectedNode = ui.Nodes[0];
        ui.PasteNodeCommand.Execute(null);

        var pasted = ui.Nodes[0].Children.Last();
        _out.WriteLine($"{pasted.Model.Name} with {pasted.Children.Count} inside");

        Assert.Equal(card.Children.Count, pasted.Children.Count);   // its label came too
        Assert.NotEmpty(pasted.Children);
    }

    [Fact]
    public void Every_paste_is_its_own_copy()
    {
        // One Copy, three Pastes: editing any of them must leave the others
        // alone, or a shop of nine cards is one card shown nine times.
        var ui = Screen();
        var card = ui.Nodes[0].AddChild(UiTemplate.Find("button")!.Build());
        ui.SelectedNode = card;
        ui.CopyNodeCommand.Execute(null);

        for (int i = 0; i < 3; i++)
        {
            ui.SelectedNode = ui.Nodes[0];
            ui.PasteNodeCommand.Execute(null);
        }

        var all = ui.Nodes[0].Children.ToList();
        Assert.Equal(4, all.Count);                                  // the original and three

        all[1].Model.Rect.Position[0] = 500;
        Assert.All(all.Where((_, i) => i != 1),
                   n => Assert.NotEqual(500, n.Model.Rect.Position[0]));

        // And the original the copy was taken from is untouched.
        Assert.Equal(0, card.Model.Rect.Position[0]);
    }

    [Fact]
    public void A_pasted_object_does_not_share_a_name_with_its_neighbour()
    {
        // Two siblings with one name make a bind path ambiguous.
        var ui = Screen();
        ui.SelectedNode = ui.Nodes[0].AddChild(UiTemplate.Find("button")!.Build());
        ui.CopyNodeCommand.Execute(null);

        ui.SelectedNode = ui.Nodes[0];
        ui.PasteNodeCommand.Execute(null);
        ui.SelectedNode = ui.Nodes[0];
        ui.PasteNodeCommand.Execute(null);

        var names = ui.Nodes[0].Children.Select(c => c.Model.Name).ToList();
        _out.WriteLine(string.Join(", ", names));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Duplicating_lands_beside_the_original_not_inside_it()
    {
        var ui = Screen();
        var first = ui.Nodes[0].AddChild(new UiNodeDef { Name = "A" });
        ui.Nodes[0].AddChild(new UiNodeDef { Name = "B" });
        ui.SelectedNode = first;

        ui.DuplicateNodeCommand.Execute(null);

        var names = ui.Nodes[0].Children.Select(c => c.Model.Name).ToList();
        _out.WriteLine(string.Join(", ", names));

        Assert.Equal(3, names.Count);
        Assert.Equal("A", names[0]);
        Assert.Equal("B", names[2]);          // the copy went between them
        Assert.Empty(first.Children);          // and not inside A
    }

    [Fact]
    public void The_root_cannot_be_duplicated()
    {
        // It has no "beside": there is nothing above it to hold a second one.
        var ui = Screen();
        ui.SelectedNode = ui.Nodes[0];
        Assert.False(ui.DuplicateNodeCommand.CanExecute(null));
    }

    [Fact]
    public void Pasting_with_nothing_copied_does_nothing()
    {
        SMSModForge.Services.EditorClipboard.SetItem(new PlaceDef { Key = "somewhere" });

        var ui = Screen();
        ui.SelectedNode = ui.Nodes[0];

        Assert.False(ui.PasteNodeCommand.CanExecute(null));
        ui.PasteNodeCommand.Execute(null);
        Assert.Empty(ui.Nodes[0].Children);
    }

    [Fact]
    public void The_tree_carries_the_shortcuts()
    {
        // Scoped to the tree, not the window: Ctrl+C means three different
        // things in this editor depending on what has focus, and whichever
        // list is focused is the one that should answer.
        WindowHarness.Run(window =>
        {
            var tree = (TreeView)window.FindName("UiTree");
            Assert.NotNull(tree);

            var keys = tree.InputBindings.OfType<KeyBinding>()
                .Select(b => (b.Key, b.Modifiers)).ToList();

            _out.WriteLine(string.Join(", ", keys.Select(k => $"{k.Modifiers}+{k.Key}")));

            Assert.Contains((Key.C, ModifierKeys.Control), keys);
            Assert.Contains((Key.V, ModifierKeys.Control), keys);
            Assert.Contains((Key.D, ModifierKeys.Control), keys);
            Assert.Contains((Key.Delete, ModifierKeys.None), keys);
            Assert.Contains((Key.Insert, ModifierKeys.None), keys);
        });
    }

    [Fact]
    public void The_shortcut_follows_whichever_screen_is_selected()
    {
        // The commands belong to the selected screen, and that changes as an
        // author moves about - a binding captured once would go on talking to
        // the screen they were on when the window opened.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tree = (TreeView)window.FindName("UiTree");

            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var second = vm.Uis.Last();
            vm.SelectedUi = second;
            second.SelectedNode = second.Nodes[0];
            WindowHarness.Pump();

            var copy = tree.InputBindings.OfType<KeyBinding>()
                .First(b => b.Key == Key.C && b.Modifiers == ModifierKeys.Control);

            Assert.True(copy.Command.CanExecute(null));
            copy.Command.Execute(null);

            // It copied the SECOND screen's object, which is the selected one.
            var copied = SMSModForge.Services.EditorClipboard.GetItem<UiNodeDef>();
            Assert.NotNull(copied);
            Assert.Equal(second.Nodes[0].Model.Name, copied!.Name);
        });
    }

    [Fact]
    public void A_paste_can_be_undone()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;

            ui.SelectedNode = ui.Nodes[0].AddChild(UiTemplate.Find("button")!.Build());
            ui.CopyNodeCommand.Execute(null);
            WindowHarness.Pump();

            int before = vm.Pack.Uis.Last().Nodes[0].Children.Count;

            ui.SelectedNode = ui.Nodes[0];
            ui.PasteNodeCommand.Execute(null);
            WindowHarness.Pump();

            int after = vm.Pack.Uis.Last().Nodes[0].Children.Count;
            _out.WriteLine($"before {before}, after paste {after}, undo depth {vm.Undo.Depth}");
            Assert.Equal(before + 1, after);

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            int undone = vm.Pack.Uis.Last().Nodes[0].Children.Count;
            _out.WriteLine($"after undo {undone}");
            Assert.Equal(before, undone);
        });
    }

    [Fact]
    public void A_paste_from_the_keyboard_can_be_undone_from_the_keyboard()
    {
        // The path the report describes: both through the real key bindings,
        // not through the commands they wrap.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tree = (TreeView)window.FindName("UiTree");

            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.SelectedNode = ui.Nodes[0].AddChild(UiTemplate.Find("button")!.Build());
            WindowHarness.Pump();

            ICommand Tree(Key k, ModifierKeys m) => tree.InputBindings.OfType<KeyBinding>()
                .First(b => b.Key == k && b.Modifiers == m).Command;
            ICommand Win(Key k, ModifierKeys m) => window.InputBindings.OfType<KeyBinding>()
                .First(b => b.Key == k && b.Modifiers == m).Command;

            Tree(Key.C, ModifierKeys.Control).Execute(null);
            ui.SelectedNode = ui.Nodes[0];
            WindowHarness.Pump();

            int before = vm.Pack.Uis.Last().Nodes[0].Children.Count;
            Tree(Key.V, ModifierKeys.Control).Execute(null);
            WindowHarness.Pump();

            int after = vm.Pack.Uis.Last().Nodes[0].Children.Count;
            _out.WriteLine($"before {before}, after paste {after}, depth {vm.Undo.Depth}");
            Assert.Equal(before + 1, after);

            Win(Key.Z, ModifierKeys.Control).Execute(null);
            WindowHarness.Pump();

            int undone = vm.Pack.Uis.Last().Nodes[0].Children.Count;
            _out.WriteLine($"after Ctrl+Z {undone}");
            Assert.Equal(before, undone);
        });
    }

    [Fact]
    public void A_paste_is_undoable_even_as_the_first_thing_you_do()
    {
        // The reported case. A checkpoint marks the state BEFORE a command, so
        // the first change of a session pushes nothing - and Ctrl+Z is disabled
        // because the stack is empty, which means the step that would have made
        // it available never gets taken.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.SelectedNode = ui.Nodes[0].AddChild(UiTemplate.Find("button")!.Build());
            ui.CopyNodeCommand.Execute(null);
            ui.SelectedNode = ui.Nodes[0];
            WindowHarness.Pump();

            // As if the pack had just been opened: no history at all.
            vm.Undo.Reset();
            Assert.False(vm.UndoCommand.CanExecute(null));

            int before = vm.Pack.Uis.Last().Nodes[0].Children.Count;
            ui.PasteNodeCommand.Execute(null);
            WindowHarness.Pump();

            _out.WriteLine($"after paste: depth {vm.Undo.Depth}, canUndo {vm.UndoCommand.CanExecute(null)}");
            Assert.True(vm.UndoCommand.CanExecute(null), "the paste left nothing to undo");

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal(before, vm.Pack.Uis.Last().Nodes[0].Children.Count);
        });
    }
}
