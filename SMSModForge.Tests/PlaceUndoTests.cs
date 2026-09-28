using System;
using System.IO;
using System.Linq;
using System.Windows;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Undo on the Places tab puts back what changed without rebuilding the
/// editor. Undoing an object moved in the preview rebuilt every tab and then
/// the Places tab's panels - a second and more on a large pack (2026-09-27).
/// <para/>
/// The faster ways are held to the same result as the full one: whatever path
/// an undo takes, the pack afterwards is exactly the snapshot it went back to.
/// These check that, on each path, and which path a step takes.
/// </summary>
public sealed class PlaceUndoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-placeundo-" + Guid.NewGuid().ToString("N"));
    private readonly bool _wasTestMode = Services.TestMode.Active;

    public PlaceUndoTests()
    {
        Directory.CreateDirectory(_dir);
        Services.TestMode.Active = true;
    }

    public void Dispose()
    {
        Services.TestMode.Active = _wasTestMode;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>Two places, the first with a nested object and an NPC standing
    /// in it; and a variable, so a step can reach outside the places.</summary>
    private MainViewModel Open()
    {
        var pack = PackRepository.CreateEmpty("placeundo.pack");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard" });
        var inner = new GameObjectDef { Name = "Lamp", X = 1, Y = 2 };
        var root = new GameObjectDef { Name = "Props", Children = { inner } };
        root.Npcs.Add(new NpcPlacementDef { Npc = "guard", Name = "Guard" });
        pack.Places.Add(new PlaceDef { Key = "beach", InternalName = "Beach", DisplayName = "Beach", GameObjects = { root } });
        pack.Places.Add(new PlaceDef { Key = "park", InternalName = "Park", DisplayName = "Park",
                                       GameObjects = { new GameObjectDef { Name = "Bench" } } });
        pack.Variables.Add(new PackVariableDef { Name = "met", Type = PackVariableType.Bool });
        PackRepository.Save(pack, _dir);

        var vm = new MainViewModel();
        vm.OpenPackFromPath(_dir);
        vm.SelectedPlace = vm.Places.First(p => p.Key == "beach");
        vm.Undo.Checkpoint();
        return vm;
    }

    private static string Snapshot(MainViewModel vm) => PackRepository.Serialize(vm.Pack);

    [Fact]
    public void AMovedObjectGoesBack_OnTheViewsAlreadyThere()
    {
        var vm = Open();
        var place = vm.SelectedPlace!;
        var lamp = place.GameObjects[0].Children[0];
        string before = Snapshot(vm);

        lamp.X = 9; lamp.ScaleY = 2; lamp.RotationZ = 30;
        vm.Undo.Checkpoint();
        string after = Snapshot(vm);

        vm.Undo.Undo();
        Assert.Equal(before, Snapshot(vm));
        Assert.Equal(1f, lamp.X);
        // Nothing was rebuilt: the same place, the same object, still selected.
        Assert.Same(place, vm.Places.First(p => p.Key == "beach"));
        Assert.Same(lamp, place.GameObjects[0].Children[0]);
        Assert.Same(place, vm.SelectedPlace);

        vm.Undo.Redo();
        Assert.Equal(after, Snapshot(vm));
        Assert.Equal(9f, lamp.X);
    }

    [Fact]
    public void AMovedNpcPartGoesBack_OnTheViewsAlreadyThere()
    {
        var vm = Open();
        var place = vm.SelectedPlace!;
        var guard = place.GameObjects[0].Npcs[0];
        string before = Snapshot(vm);

        guard.Body.X = 4; guard.Shadow.ScaleX = 3;
        vm.Undo.Checkpoint();

        vm.Undo.Undo();
        Assert.Equal(before, Snapshot(vm));
        Assert.Same(guard, place.GameObjects[0].Npcs[0]);
    }

    [Fact]
    public void AnyOtherChangeToOnePlace_IsPutBackExactly_AndTheSelectionStays()
    {
        var vm = Open();
        string before = Snapshot(vm);

        vm.SelectedPlace!.GameObjects[0].Children[0].Model.Name = "Lantern";
        vm.SelectedPlace.GameObjects[0].Children[0].Sprite = "art/lantern.png";
        vm.Undo.Checkpoint();

        vm.Undo.Undo();
        Assert.Equal(before, Snapshot(vm));
        Assert.Equal("beach", vm.SelectedPlace?.Key);
        Assert.Equal("Lamp", vm.SelectedPlace!.GameObjects[0].Children[0].Model.Name);
    }

    [Fact]
    public void AStepThatReachesPastOnePlace_IsPutBackExactly()
    {
        var vm = Open();
        string before = Snapshot(vm);

        // Two places in one step, and a place with a variable.
        vm.Places.First(p => p.Key == "beach").GameObjects[0].Children[0].X = 7;
        vm.Places.First(p => p.Key == "park").GameObjects[0].X = 7;
        vm.Undo.Checkpoint();
        vm.Undo.Undo();
        Assert.Equal(before, Snapshot(vm));

        vm.SelectedPlace!.GameObjects[0].Children[0].X = 5;
        vm.Variables.First().Name = "metRiver";
        vm.Undo.Checkpoint();
        vm.Undo.Undo();
        Assert.Equal(before, Snapshot(vm));
    }

    [Fact]
    public void AnObjectAddedOrTakenAway_IsPutBackExactly()
    {
        var vm = Open();
        string before = Snapshot(vm);

        vm.SelectedPlace!.GameObjects[0].AddChild();
        vm.Undo.Checkpoint();
        vm.Undo.Undo();
        Assert.Equal(before, Snapshot(vm));
        Assert.Single(vm.SelectedPlace!.GameObjects[0].Children);
    }

    // ── The gizmo's scale handles ───────────────────────────────────────

    private static Vector Unit(Vector v) { v.Normalize(); return v; }

    /// <summary>
    /// The square in the middle scales both ways at the rate an axis handle
    /// scales one: the same drag, the same change. It measured the pointer
    /// against where it was grabbed - a few pixels from the centre - so a
    /// drag that nudged an axis handle multiplied the size several times over.
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(-30)]
    [InlineData(75)]
    public void TheMiddleSquareIsNoTouchierThanTheArms(double travel)
    {
        var origin = new Point(900, 500);
        var ux = new Vector(80, 0);    // +1 local X, on the canvas
        var uy = new Vector(0, -80);   // +1 local Y: up

        // An axis handle is grabbed at the end of its arm, 150 from the centre.
        var grabX = origin + Unit(ux) * 150;
        double arm = PlacePreview.AxisScaleFactor(origin, grabX, grabX + Unit(ux) * travel, ux);

        // The square is grabbed on the centre, give or take a few pixels, and
        // dragged the same distance up and to the right.
        var grab = origin + new Vector(4, -3);
        var diagonal = Unit(Unit(ux) + Unit(uy));
        double square = PlacePreview.UniformScaleFactor(grab, grab + diagonal * travel, ux, uy);

        Assert.Equal(arm, square, 3);
    }

    [Fact]
    public void TheMiddleSquareMakesThingsSmaller_ButNeverTurnsThemInsideOut()
    {
        var ux = new Vector(80, 0);
        var uy = new Vector(0, -80);
        var grab = new Point(900, 500);
        Assert.True(PlacePreview.UniformScaleFactor(grab, grab + new Vector(-60, 60), ux, uy) < 1);
        Assert.True(PlacePreview.UniformScaleFactor(grab, grab + new Vector(-900, 900), ux, uy) > 0);
    }
}
