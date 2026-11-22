using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A place's objects, and a game's level's, put back where they are (1.10.0,
/// GameObjectRows): an undo or a redo of something done to one object costs
/// that object, not the place's whole view made again - its tree, its rows,
/// the preview's picture of it. Held in the real window, the key really
/// pressed:
/// <list type="bullet">
/// <item>an object renamed, given another picture, given a component, a
/// character stood on it, an object added, one added under another, one taken
/// out, one moved: each taken back and done again, the pack exactly what it
/// was, and every row the step did not touch still the row it was;</item>
/// <item>the same on a game's level, where the objects keep something of the
/// game's beside what is saved (the object each was filled in from): still on
/// every one of them afterwards - control: the level has such objects;</item>
/// <item>control: something of the place itself changed goes the longer way,
/// and the rows are others afterwards - so "the same rows" can fail.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceObjectUndoTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-placeundo-" + Guid.NewGuid().ToString("N"));

    public PlaceObjectUndoTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static IEnumerable<GameObjectViewModel> Flat(IEnumerable<GameObjectViewModel> rows)
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
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            try { body(window, vm, () => PackRepository.Serialize(vm.Pack)); }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    /// <summary>The edits, each taken back and done again, on whatever holds the rows.</summary>
    private void Hold(MainWindow window, MainViewModel vm, Func<string> state, string where,
                      Func<IList<GameObjectViewModel>> top, Func<GameObjectViewModel> add, Func<GameObjectViewModel, bool> mine)
    {
        vm.Undo.Checkpoint();
        string before = state();
        var rows = Flat(top()).ToList();
        _out.WriteLine($"{where}: {rows.Count} objects");

        void Back(string what)
        {
            Press(window, Key.Z);
            _out.WriteLine($"{where}, {what}: taken back by {vm.LastRestore}");
            Assert.Equal("values", vm.LastRestore);
            Assert.Equal(before, state());
            var now = Flat(top()).ToList();
            Assert.Equal(rows.Count, now.Count);
            for (int i = 0; i < rows.Count; i++) Assert.Same(rows[i], now[i]);
        }

        void Again(string wanted)
        {
            Press(window, Key.Y);
            Assert.Equal("values", vm.LastRestore);
            Assert.Equal(wanted, state());
            foreach (var kept in rows) Assert.Contains(kept, Flat(top()));
        }

        void Both(string what, Action edit, Action? afterBack = null, Action? afterAgain = null)
        {
            edit();
            WindowHarness.Pump();
            vm.Undo.Checkpoint();
            string edited = state();
            Assert.NotEqual(before, edited);
            Back(what);
            afterBack?.Invoke();
            Again(edited);
            afterAgain?.Invoke();
            Back(what + ", again");
        }

        // One of the pack's own to work on: made first, and part of what "before" is.
        var row = add();
        row.Name = "Held";
        WindowHarness.Pump();
        vm.Undo.Checkpoint();
        before = state();
        rows = Flat(top()).ToList();
        Assert.True(mine(row));

        Both("renamed", () => row.Name = "Held, renamed", () => Assert.Equal("Held", row.Name), () => Assert.Equal("Held, renamed", row.Name));
        Both("another picture", () => row.Sprite = "Overlays/other.png", () => Assert.Equal("", row.Sprite ?? ""));
        int components = row.Components.Count;
        Both("a component", () => row.AddComponentCommand.Execute(null),
             () => Assert.Equal(components, row.Components.Count),
             () => { Assert.Equal(components + 1, row.Components.Count); Assert.Same(row.Model.Components[^1], row.Components[^1].Model); });
        int conditions = row.ActiveConditions.Count;
        Both("a condition", () => row.AddActiveConditionCommand.Execute(null),
             () => Assert.Equal(conditions, row.ActiveConditions.Count), () => Assert.Equal(conditions + 1, row.ActiveConditions.Count));
        Both("moved", () => row.X += 3, () => Assert.Equal(row.Model.X, row.X));
        Both("an object added", () => add().Name = "Another");
        Both("an object added under it", () => row.AddChildCommand.Execute(null),
             () => Assert.Empty(row.Children), () => Assert.Single(row.Children));

        // One taken out, with what was under it: back where it was.
        row.AddChildCommand.Execute(null);
        WindowHarness.Pump();
        row.Children[0].Name = "Under";
        vm.Undo.Checkpoint();
        string withChild = state();
        int count = Flat(top()).Count();
        row.RemoveCommand.Execute(null);
        WindowHarness.Pump();
        vm.Undo.Checkpoint();
        string without = state();
        Assert.NotEqual(withChild, without);
        Press(window, Key.Z);
        _out.WriteLine($"{where}, taken out: put back by {vm.LastRestore}");
        Assert.Equal("values", vm.LastRestore);
        Assert.Equal(withChild, state());
        Assert.Equal(count, Flat(top()).Count());
        foreach (var kept in rows.Where(r => !ReferenceEquals(r, row))) Assert.Contains(kept, Flat(top()));
        Press(window, Key.Y);
        Assert.Equal("values", vm.LastRestore);
        Assert.Equal(without, state());
    }

    [Fact]
    public void WhatIsDoneToAnObjectOfAPlaceIsTakenBackWhereItIs()
    {
        Run((window, vm, state) =>
        {
            var place = vm.Places.OrderByDescending(p => p.GameObjects.Count).First();
            vm.SelectedPlace = place;
            WindowHarness.Pump();
            Hold(window, vm, state, "a place", () => place.GameObjects, () => place.AddGameObject(), r => !r.Model.KeepsWhatItFinds);
            Assert.Same(place, vm.SelectedPlace);
            Assert.Same(place.GameObjects.First(g => g.IsNpcRoot), place.NpcsNode);

            // Control: something of the place itself goes the longer way, and the rows are others.
            vm.Undo.Checkpoint();
            string before = state();
            var one = place.GameObjects[0];
            place.BaseSortingOrder += 1;
            vm.Undo.Checkpoint();
            Assert.NotEqual(before, state());
            Press(window, Key.Z);
            _out.WriteLine("the place's own value: taken back by " + vm.LastRestore);
            Assert.NotEqual("values", vm.LastRestore);
            Assert.Equal(before, state());
            Assert.DoesNotContain(one, vm.Places.SelectMany(p => p.GameObjects));
        });
    }

    [Fact]
    public void WhatIsDoneToAnObjectOfAGamesLevelIsTakenBackWhereItIs_AndWhatTheGameSaysOfEachIsStillOnIt()
    {
        Run((window, vm, state) =>
        {
            var level = vm.VanillaExtensions.FirstOrDefault(v => v.HasCatalogEntry && v.GameObjects.Count > 0);
            if (level == null)
            {
                _out.WriteLine("no level of the game's to read here - nothing held");
                return;
            }
            vm.SelectedVanillaExtension = level;
            WindowHarness.Pump();
            Dictionary<GameObjectViewModel, object?> Games() => Flat(level.GameObjects).Where(r => r.Model.Bind).ToDictionary(r => r, r => (object?)r.Model.Baseline);
            var games = Games();
            int known = games.Values.Count(b => b != null);
            _out.WriteLine($"{games.Count} objects of the game's, {known} that know which");
            Assert.True(known > 0, "control: no object of this level knows the game's object it was filled in from - the check cannot tell");

            Hold(window, vm, state, "a game's level", () => level.GameObjects, () => level.AddGameObject(), r => !r.Model.Bind);
            Assert.Same(level, vm.SelectedVanillaExtension);
            var after = Games();
            Assert.Equal(games.Count, after.Count);
            foreach (var (row, baseline) in games)
            {
                Assert.True(after.ContainsKey(row), "a row of one of the game's objects is another row now");
                Assert.Same(baseline, after[row]);
            }

            // One of the game's own, changed: its row stays, and it still knows the game's object.
            var bound = Flat(level.GameObjects).First(r => r.Model.Bind && r.Model.Baseline != null && !r.IsNpcHost);
            vm.Undo.Checkpoint();
            string before = state();
            bound.SortingOrder += 7;
            vm.Undo.Checkpoint();
            Assert.NotEqual(before, state());
            Press(window, Key.Z);
            _out.WriteLine("one of the game's objects changed: taken back by " + vm.LastRestore);
            Assert.Equal("values", vm.LastRestore);
            Assert.Equal(before, state());
            Assert.Contains(bound, Flat(level.GameObjects));
            Assert.Same(games[bound], bound.Model.Baseline);
            Press(window, Key.Y);
            Assert.Equal("values", vm.LastRestore);
            Assert.Same(games[bound], bound.Model.Baseline);
            Press(window, Key.Z);
            Assert.Equal(before, state());
        });
    }
}
