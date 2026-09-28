using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// How long undo and redo take on a real, large pack, split into the parts
/// that cost something - so a complaint that undo is slow can be answered with
/// a number and a place rather than a guess.
/// <para/>
/// Only against an author's pack, named by <c>SMSMODFORGE_AUTHOR_PACK</c> (the
/// manifest or its folder): a committed test must not hard-code a path to
/// anybody's work, and a small made-up pack would time nothing worth knowing.
/// The manifest is COPIED to a folder of its own first, so the pack itself is
/// only ever read.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UndoTimingTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-undo-" + Guid.NewGuid().ToString("N"));

    public UndoTimingTests(ITestOutputHelper o) => _out = o;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void UndoingAMovedLineOnARealPack()
    {
        string? path = Environment.GetEnvironmentVariable("SMSMODFORGE_AUTHOR_PACK");
        if (string.IsNullOrEmpty(path)) { _out.WriteLine("SMSMODFORGE_AUTHOR_PACK not set - nothing to time."); return; }
        string manifest = Directory.Exists(path) ? Path.Combine(path, "modpack.json") : path;
        Directory.CreateDirectory(_dir);
        File.Copy(manifest, Path.Combine(_dir, "modpack.json"));
        _out.WriteLine($"pack: {new FileInfo(manifest).Length / 1024} KB");

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = 5;
            var dialogue = vm.Dialogues.OrderByDescending(d => d.Nodes.Count).First();
            vm.SelectedDialogue = dialogue;
            WindowHarness.Pump();
            vm.SelectedNode = dialogue.Nodes[1];
            WindowHarness.Pump();
            _out.WriteLine($"conversation '{dialogue.Key}': {dialogue.Nodes.Count} lines, {vm.Dialogues.Count} conversations in all");

            var clock = Stopwatch.StartNew();
            vm.Undo.Checkpoint();
            _out.WriteLine($"a checkpoint with nothing changed: {clock.ElapsedMilliseconds} ms");

            // A line dragged below the one after it - the move being asked about.
            Assert.True(dialogue.MoveNode(dialogue.Nodes[1], dialogue.Nodes[2], NodeDropMode.After));
            clock.Restart();
            vm.Undo.Checkpoint();
            _out.WriteLine($"the checkpoint that records the move: {clock.ElapsedMilliseconds} ms");
            WindowHarness.Pump();

            for (int round = 1; round <= 3; round++)
            {
                clock.Restart();
                vm.Undo.Undo();
                long undo = clock.ElapsedMilliseconds;
                WindowHarness.Pump();
                long drawn = clock.ElapsedMilliseconds;

                clock.Restart();
                vm.Undo.Redo();
                long redo = clock.ElapsedMilliseconds;
                WindowHarness.Pump();
                long redrawn = clock.ElapsedMilliseconds;

                _out.WriteLine($"round {round}: undo {undo} ms (+{drawn - undo} ms to draw), redo {redo} ms (+{redrawn - redo} ms to draw)");
            }

            // Where the drawing goes: the speller alone, over the same lines.
            clock.Restart();
            int words = 0;
            foreach (var n in dialogue.Nodes) words += Services.Speller.Check(n.Text).Count;
            _out.WriteLine($"the speller over all {dialogue.Nodes.Count} lines: {clock.ElapsedMilliseconds} ms ({words} marked)");
            clock.Restart();
            foreach (var n in dialogue.Nodes) Services.Speller.Check(n.Text);
            _out.WriteLine($"...and again: {clock.ElapsedMilliseconds} ms");
        });
    }

    /// <summary>
    /// An object on a place moved in the preview, then undone and redone -
    /// with the pack's art, since drawing the place is what the preview does.
    /// Needs <c>SMSMODFORGE_AUTHOR_PACK</c> to name the pack's FOLDER, which is
    /// copied whole; the pack itself is only read.
    /// </summary>
    [Fact]
    public void UndoingAnObjectMovedOnAPlace()
    {
        string? path = Environment.GetEnvironmentVariable("SMSMODFORGE_AUTHOR_PACK");
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        { _out.WriteLine("SMSMODFORGE_AUTHOR_PACK does not name a pack folder - nothing to time."); return; }
        foreach (string dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(path, _dir));
        foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(path, _dir));

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = 3;   // Places
            WindowHarness.Pump();
            var place = vm.Places.OrderByDescending(p => p.GameObjects.Count).First();
            var clock = Stopwatch.StartNew();
            vm.SelectedPlace = place;
            WindowHarness.Pump();
            _out.WriteLine($"place '{place.Key}': {place.GameObjects.Count} top-level objects; selecting it and drawing: {clock.ElapsedMilliseconds} ms");

            var go = place.GameObjects.First();
            clock.Restart();
            vm.Undo.Checkpoint();
            _out.WriteLine($"a checkpoint with nothing changed: {clock.ElapsedMilliseconds} ms");

            // What the end of a drag leaves: the object somewhere else, and a checkpoint.
            go.X += 1.5f;
            WindowHarness.Pump();
            clock.Restart();
            vm.Undo.Checkpoint();
            _out.WriteLine($"the checkpoint that records the move: {clock.ElapsedMilliseconds} ms");

            for (int round = 1; round <= 4; round++)
            {
                // The last round from another tab: what is left is the Places
                // tab drawing itself, and this says how much of it that is.
                if (round == 4) { vm.SelectedTabIndex = 1; WindowHarness.Pump(); _out.WriteLine("-- on the Characters tab now"); }
                clock.Restart();
                vm.Undo.Undo();
                long undo = clock.ElapsedMilliseconds;
                WindowHarness.Pump();
                long drawn = clock.ElapsedMilliseconds;

                clock.Restart();
                vm.Undo.Redo();
                long redo = clock.ElapsedMilliseconds;
                WindowHarness.Pump();
                long redrawn = clock.ElapsedMilliseconds;

                _out.WriteLine($"round {round}: undo {undo} ms (+{drawn - undo} ms to draw), redo {redo} ms (+{redrawn - redo} ms to draw)");
            }
        });
    }
}
