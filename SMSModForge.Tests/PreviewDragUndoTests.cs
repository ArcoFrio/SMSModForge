using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What is dragged in a preview is a step like any other (the author, 1.10.0:
/// "are we making sure non-conventional UI sections also get the undo/redo
/// rework? Such as preview edits (places, UI, etc), which felt separate from
/// the usual system before"). The handles are dragged through the very methods
/// the mouse's own handlers call (PlacePreview.BeginHandle / DragHandleTo /
/// EndHandle, UiPreview.BeginDrag / DragTo and the mouse let go) - the desktop
/// tests run on has no mouse - and the key is really pressed:
/// <list type="bullet">
/// <item>in the place preview: an object moved, turned and resized, a
/// character's part moved, a light's reach dragged - on a place of the pack's
/// and on one of the game's levels;</item>
/// <item>in the UI preview: an object moved and resized by a grip;</item>
/// <item>each drag is one step however far it wandered, Ctrl+Z puts back
/// exactly what was there, where it is (the rows, and what is picked in the
/// preview, stay), Ctrl+Y brings the drag back;</item>
/// <item>control: a handle taken and let go where it was, and a press on an
/// object without a move, are no step and change nothing.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PreviewDragUndoTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-dragundo-" + Guid.NewGuid().ToString("N"));

    public PreviewDragUndoTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static void Press(MainWindow window, Key key)
    {
        RealKeys.Press(window, key, ModifierKeys.Control);
        WindowHarness.Pump();
    }

    private static IEnumerable<GameObjectViewModel> Flat(IEnumerable<GameObjectViewModel> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            foreach (var deeper in Flat(row.Children)) yield return deeper;
        }
    }

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

    /// <summary>One drag held to undo and redo: a step, back exactly, forward exactly, put back where it is.</summary>
    private void Hold(MainWindow window, MainViewModel vm, Func<string> state, string what, Action drag, Func<string> shown, Func<bool> stillPicked)
    {
        vm.Undo.Checkpoint();
        string before = state(), shownBefore = shown();
        int depth = vm.Undo.Depth;
        drag();
        WindowHarness.Pump();
        string after = state(), shownAfter = shown();
        Assert.True(before != after, what + ": the drag changed nothing in the pack");
        Assert.NotEqual(shownBefore, shownAfter);
        Assert.Equal(depth + 1, vm.Undo.Depth);

        Press(window, Key.Z);
        _out.WriteLine($"{what}: {shownBefore} -> {shownAfter}; taken back by {vm.LastRestore}");
        Assert.Equal("values", vm.LastRestore);
        Assert.Equal(before, state());
        Assert.Equal(shownBefore, shown());
        Assert.True(stillPicked(), what + ": what was picked in the preview is no longer picked after the undo");
        Assert.Equal(depth, vm.Undo.Depth);

        Press(window, Key.Y);
        Assert.Equal("values", vm.LastRestore);
        Assert.Equal(after, state());
        Assert.Equal(shownAfter, shown());
        Assert.True(stillPicked());

        Press(window, Key.Z);
        Assert.Equal(before, state());
    }

    private void DragInAPlace(MainWindow window, MainViewModel vm, Func<string> state, PlacePreview preview, string where,
                              GameObjectViewModel row, NpcPlacementViewModel? placed, GameObjectViewModel? lamp)
    {
        string F(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(preview.Pick(row), where + ": the object is not in the picture");
        WindowHarness.Pump();
        Assert.True(preview.HasHandles, where + ": the object picked has no handles");

        void Drag(PlacePreview.GizmoHandle handle, Point from, params Point[] through)
        {
            preview.BeginHandle(handle, from);
            foreach (var p in through) preview.DragHandleTo(p);
            preview.EndHandle();
        }

        // Control: a handle taken and let go where it was taken is no step.
        vm.Undo.Checkpoint();
        string still = state();
        int depth = vm.Undo.Depth;
        Drag(PlacePreview.GizmoHandle.MoveFree, new Point(700, 400), new Point(760, 380), new Point(700, 400));
        WindowHarness.Pump();
        Assert.Equal(still, state());
        Assert.Equal(depth, vm.Undo.Depth);

        Hold(window, vm, state, where + ", an object moved",
             () => Drag(PlacePreview.GizmoHandle.MoveFree, new Point(700, 400), new Point(720, 390), new Point(790, 350)),
             () => F(row.X) + "," + F(row.Y), () => preview.HasHandles);
        Hold(window, vm, state, where + ", an object moved along one line",
             () => Drag(PlacePreview.GizmoHandle.MoveX, new Point(700, 400), new Point(640, 470)),
             () => F(row.X) + "," + F(row.Y), () => preview.HasHandles);

        preview.SetMode(PlacePreview.GizmoMode.Rotate);
        WindowHarness.Pump();
        Hold(window, vm, state, where + ", an object turned",
             () => Drag(PlacePreview.GizmoHandle.RotZ, new Point(900, 400), new Point(880, 300), new Point(800, 260)),
             () => F(row.RotationZ), () => preview.HasHandles);

        preview.SetMode(PlacePreview.GizmoMode.Scale);
        WindowHarness.Pump();
        Hold(window, vm, state, where + ", an object resized",
             () => Drag(PlacePreview.GizmoHandle.ScaleUniform, new Point(700, 400), new Point(740, 360)),
             () => F(row.ScaleX) + "," + F(row.ScaleY), () => preview.HasHandles);
        preview.SetMode(PlacePreview.GizmoMode.Move);
        WindowHarness.Pump();

        if (placed != null)
        {
            Assert.True(preview.Pick(placed), where + ": the character is not in the picture");
            WindowHarness.Pump();
            Assert.True(preview.HasHandles);
            Hold(window, vm, state, where + ", a character moved",
                 () => Drag(PlacePreview.GizmoHandle.MoveFree, new Point(600, 500), new Point(650, 520)),
                 () => F(placed.Body.X) + "," + F(placed.Body.Y), () => preview.HasHandles);
            preview.SetPart(PlacePreview.GizmoPart.Shadow);
            WindowHarness.Pump();
            if (preview.HasHandles)
                Hold(window, vm, state, where + ", a character's shadow moved",
                     () => Drag(PlacePreview.GizmoHandle.MoveFree, new Point(600, 500), new Point(570, 530)),
                     () => F(placed.Shadow.X) + "," + F(placed.Shadow.Y), () => preview.HasHandles);
            preview.SetPart(PlacePreview.GizmoPart.Body);
        }

        if (lamp?.Light != null)
        {
            Assert.True(preview.Pick(lamp), where + ": the lamp is not in the picture");
            WindowHarness.Pump();
            Assert.True(preview.HasHandles);
            Hold(window, vm, state, where + ", a light's reach dragged",
                 () => Drag(PlacePreview.GizmoHandle.LightOuter, new Point(1000, 500), new Point(1150, 500), new Point(1300, 520)),
                 () => F(lamp.Light!.Model.OuterRadius), () => preview.HasHandles);
        }
    }

    [Fact]
    public void WhatIsDraggedInThePlacePreviewIsAStep_TakenBackWhereItIs()
    {
        Run(MainTab.Places, (window, vm, state) =>
        {
            // A place of the pack's: an object, a character standing in it, a lamp.
            var place = vm.Places.First(p => p.GameObjects.Any(g => g.HasLight));
            vm.SelectedPlace = place;
            WindowHarness.Pump();
            var row = place.AddGameObject();
            row.Name = "Dragged";
            var placed = place.GameObjects.SelectMany(g => Flat(new[] { g })).SelectMany(g => g.Npcs).FirstOrDefault();
            if (placed == null) { place.AddNpcCommand.Execute(null); placed = place.NpcsNode.Npcs.Last(); }
            WindowHarness.Pump();
            DragInAPlace(window, vm, state, (PlacePreview)window.FindName("PlacePreviewMain"), "a place", row, placed,
                         place.GameObjects.First(g => g.HasLight));

            // One of the game's levels: one of its own objects.
            var level = vm.VanillaExtensions.FirstOrDefault(v => v.HasCatalogEntry && v.GameObjects.Count > 0);
            if (level == null)
            {
                _out.WriteLine("no level of the game's to read here - the place alone held");
                return;
            }
            vm.SelectedVanillaExtension = level;
            WindowHarness.Pump();
            var games = Flat(level.GameObjects).First(r => r.Model.Bind && !r.IsNpcHost && r.Model.Baseline != null);
            DragInAPlace(window, vm, state, (PlacePreview)window.FindName("ExtPreview"), "a game's level", games, null, null);
            Assert.NotNull(games.Model.Baseline);
        });
    }

    private static void LetGo(UiPreview preview)
    {
        var overlay = (UIElement)typeof(UiPreview).GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
        // The mouse let go, as the window and the preview hear it: before it arrives, and as it does.
        overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
        overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        WindowHarness.Pump();
    }

    [Fact]
    public void WhatIsDraggedInTheUiPreviewIsAStep_TakenBackWhereItIs()
    {
        Run(MainTab.Ui, (window, vm, state) =>
        {
            static IEnumerable<UiNodeViewModel> Rows(IEnumerable<UiNodeViewModel> rows)
            {
                foreach (var r in rows) { yield return r; foreach (var d in Rows(r.Children)) yield return d; }
            }
            string F(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            var ui = vm.Uis.First(u => !u.Model.IsVanillaBased && Rows(u.Nodes).Any(r => r.Width > 20 && r.Height > 20 && !r.IsArranged));
            vm.SelectedUi = ui;
            WindowHarness.Pump();
            var row = Rows(ui.Nodes).First(r => r.Width > 20 && r.Height > 20 && !r.IsArranged);
            ui.SelectedNode = row;
            WindowHarness.Pump();
            var preview = (UiPreview)window.FindName("UiScreenPreview");
            Assert.True(WindowHarness.Until(() => ReferenceEquals(preview.Highlight, row.Model), TimeSpan.FromSeconds(10)), "the preview's box is not on the object picked");
            var rows = Rows(ui.Nodes).ToList();
            bool Picked() => ReferenceEquals(ui.SelectedNode, row) && ReferenceEquals(preview.Highlight, row.Model)
                             && Rows(ui.Nodes).SequenceEqual(rows);

            // Control: pressed and let go without a move is no step, and changes nothing.
            vm.Undo.Checkpoint();
            string still = state();
            int depth = vm.Undo.Depth;
            preview.BeginDrag(UiGrip.Move, new Point(300, 300));
            Assert.False(preview.DragTo(new Point(300, 300)));
            LetGo(preview);
            Assert.Equal(still, state());
            Assert.Equal(depth, vm.Undo.Depth);
            ui.SelectedNode = row;      // the click picked what was under it, wherever that was
            WindowHarness.Pump();
            Assert.True(WindowHarness.Until(() => ReferenceEquals(preview.Highlight, row.Model), TimeSpan.FromSeconds(10)));

            Hold(window, vm, state, "an object moved in the UI preview", () =>
            {
                preview.BeginDrag(UiGrip.Move, new Point(300, 300));
                Assert.True(preview.DragTo(new Point(330, 280)), "the drag did nothing: the picture is not on screen");
                preview.DragTo(new Point(380, 250));
                LetGo(preview);
            }, () => F(row.PositionX) + "," + F(row.PositionY), Picked);

            Hold(window, vm, state, "an object resized by a grip in the UI preview", () =>
            {
                preview.BeginDrag(UiGrip.Right, new Point(400, 300));
                Assert.True(preview.DragTo(new Point(450, 300)));
                LetGo(preview);
            }, () => F(row.Width) + "x" + F(row.Height), Picked);

            Hold(window, vm, state, "an object resized by a corner in the UI preview", () =>
            {
                preview.BeginDrag(UiGrip.BottomRight, new Point(400, 300));
                Assert.True(preview.DragTo(new Point(430, 340)));
                LetGo(preview);
            }, () => F(row.Width) + "x" + F(row.Height), Picked);
        });
    }
}
