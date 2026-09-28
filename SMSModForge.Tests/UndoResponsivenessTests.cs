using System;
using System.Diagnostics;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// How long an undo takes to put the change back on screen.
/// <para/>
/// Undo is snapshot-based: every committed edit serialises the whole pack, and
/// every undo parses one back and rebinds. On a large pack those are tens of
/// milliseconds each — but the step that dominated was neither. It was
/// validating the whole pack, which ran inside the restore before the editor
/// could redraw: around 640ms on a two-megabyte pack, against 43 to parse the
/// snapshot.
/// <para/>
/// That validation is gone rather than merely deferred, because no other edit
/// does it either. The issue list is built when a pack is opened, saved or
/// published; typing a line, adding a node and deleting a character never
/// touch it. Undo refreshing it was not a rule, it was an accident of where
/// the call sat — and it made undo the slowest thing in the editor for a
/// result nobody had asked for.
/// <para/>
/// So what is checked here is that the undo does its own work and only its own
/// work. Timing thresholds are avoided: they measure the machine running the
/// tests.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UndoResponsivenessTests
{
    private readonly ITestOutputHelper _out;
    public UndoResponsivenessTests(ITestOutputHelper o) => _out = o;

    /// <summary>A pack with one thing wrong in it that only Validate reports,
    /// and a folder for Validate to resolve art against - it returns early
    /// without one, which would make every assertion below vacuous.</summary>
    private static string GiveItSomethingToFind(MainViewModel vm)
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                             "SMSModForgeUndo", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        vm.PackRoot = root;

        vm.AddDialogueCommand.Execute(null);
        var broken = vm.Dialogues.Last();
        broken.Nodes.Clear();
        broken.Model.Nodes.Clear();
        // A node naming an actor the pack does not have: something Validate
        // reports and nothing else does.
        var node = broken.AddNode();
        node.Actor = "nobody-by-that-name";
        node.Text = "Hello.";
        return root;
    }

    [Fact]
    public void AnUndoPutsThePackBackAndDoesNothingElse()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.AddDialogueCommand.Execute(null);
            vm.SelectedDialogue = vm.Dialogues.Last();
            vm.Undo.Checkpoint();

            int before = vm.Dialogues.Count;
            vm.AddDialogueCommand.Execute(null);
            vm.Undo.Checkpoint();
            Assert.Equal(before + 1, vm.Dialogues.Count);

            var watch = Stopwatch.StartNew();
            vm.Undo.Undo();
            watch.Stop();

            _out.WriteLine($"undo returned in {watch.Elapsed.TotalMilliseconds:0.0} ms "
                           + $"with {vm.Dialogues.Count} dialogues");
            Assert.Equal(before, vm.Dialogues.Count);
        });
    }

    [Fact]
    public void UndoDoesNotRebuildTheIssueList()
    {
        // The change itself. An undo leaves the list exactly as it found it -
        // which is the same thing every other edit in the editor does.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            string root = GiveItSomethingToFind(vm);

            vm.Undo.Checkpoint();
            vm.AddDialogueCommand.Execute(null);
            vm.Undo.Checkpoint();

            int listed = vm.Issues.Count;
            vm.Undo.Undo();
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            _out.WriteLine($"{listed} issue(s) before the undo, {vm.Issues.Count} after");
            Assert.Equal(listed, vm.Issues.Count);

            try { System.IO.Directory.Delete(root, true); } catch (System.IO.IOException) { }
        });
    }

    [Fact]
    public void TheIssueListIsStillBuiltWhereItAlwaysWas()
    {
        // The control, and the reason this is not simply a deletion: a
        // validation that never runs anywhere is not a faster editor, it is a
        // broken one. Opening a pack still fills the list, so the list an undo
        // leaves alone is a real one.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            string root = GiveItSomethingToFind(vm);

            Assert.True(vm.ValidateCommand.CanExecute(null),
                        "nothing in the editor can rebuild the issue list");
            vm.ValidateCommand.Execute(null);
            WindowHarness.Pump();

            _out.WriteLine($"{vm.Issues.Count} issue(s) after a check");
            foreach (var i in vm.Issues.Take(5)) _out.WriteLine("   " + i.Message);
            Assert.NotEmpty(vm.Issues);

            try { System.IO.Directory.Delete(root, true); } catch (System.IO.IOException) { }
        });
    }
}
