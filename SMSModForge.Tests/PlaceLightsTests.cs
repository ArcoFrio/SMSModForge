using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The place preview's lights (1.9.2, <see cref="PlaceLights"/>) are the game's: built from the PC's room's lamp as
/// the game captured it, their light texture is, value for value, the bust preview's room lamp's
/// (<see cref="GameLamp"/>, held to the game's frames by InGameSuiteTests.S6); built from the level catalog's
/// Light2D, the lamp is the one the game drew - where, how far, what colour - and a lamp that lights no level (the
/// room's of intensity 25, on the Foreground and Photos layers) is left out. Control: its sibling, lighting the room's
/// layer, is kept.
/// </summary>
public sealed class PlaceLightsTests
{
    private readonly ITestOutputHelper _out;
    public PlaceLightsTests(ITestOutputHelper o) => _out = o;

    private static PreviewBackdrop Room() => PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom"));

    [Fact]
    public void TheLightTextureIsTheRoomLampsWhichTheGameDrew()
    {
        var backdrop = Room();
        var lamp = backdrop.Lamp!;
        var game = new GameLamp(backdrop);
        var lights = new PlaceLights(new[] { new PlaceLights.Light(lamp.X, lamp.Y, lamp.Radius, lamp.R, lamp.G, lamp.B, lamp.Falloff, lamp.VolumeOpacity) },
                                     lamp.Global, backdrop.LookupDecode);
        Assert.Equal(game.LightTextureSize, lights.TextureSize);
        var a = game.LitNoShadow; var b = lights.Texels;
        int same = Enumerable.Range(0, a.Length).Count(i => a[i] == b[i]);
        _out.WriteLine($"{same} of {a.Length} light texture values the bust preview's room lamp's");
        Assert.Equal(a.Length, same);
        // Control: the lamp somewhere else is not the room's.
        var moved = new PlaceLights(new[] { new PlaceLights.Light(lamp.X - 200, lamp.Y, lamp.Radius, lamp.R, lamp.G, lamp.B, lamp.Falloff, lamp.VolumeOpacity) },
                                    lamp.Global, backdrop.LookupDecode);
        Assert.True(Enumerable.Range(0, a.Length).Count(i => a[i] != moved.Texels[i]) > a.Length / 10, "control: the lamp moved gives the same light");
    }

    [Fact]
    public void FromTheCatalogTheLampIsTheOneTheGameDrew_AndOneLightingNoLevelIsLeftOut()
    {
        var lamp = Room().Lamp!;
        var level = VanillaLevelCatalog.FindLevel("5_MyRoom")!;
        var nodes = level.Hierarchy!.Children.Where(c => c.ComponentValues.Any(v => v.Type == "Light2D")).ToList();
        var lit = nodes.Single(n => n.Name == "Light 2D");
        var other = nodes.Single(n => n.Name == "Light 2D (1)");
        float x = PlaceLights.ScreenW / 2f + (level.LevelPositionX + level.Hierarchy.ScaleX * lit.X) * PlaceLights.PixelsPerWorldUnit;
        float y = PlaceLights.ScreenH / 2f + (level.LevelPositionY + level.Hierarchy.ScaleY * lit.Y) * PlaceLights.PixelsPerWorldUnit;
        var fromCatalog = PlaceLights.FromComponent(lit.ComponentValues.Single(v => v.Type == "Light2D").Params, x, y);
        Assert.NotNull(fromCatalog);
        var l = fromCatalog!.Value;
        _out.WriteLine($"catalog: at {l.X:F2},{l.Y:F2}, radius {l.Radius}, colour {l.R:F4},{l.G:F4},{l.B:F4}, falloff {l.Falloff}, glow {l.VolumeOpacity}; "
                       + $"the game's: {lamp.X:F2},{lamp.Y:F2}, {lamp.Radius}, {lamp.R:F4},{lamp.G:F4},{lamp.B:F4}, {lamp.Falloff}, {lamp.VolumeOpacity}");
        Assert.InRange(l.X - lamp.X, -0.1f, 0.1f);
        Assert.InRange(l.Y - lamp.Y, -0.1f, 0.1f);
        Assert.Equal(lamp.Radius, l.Radius);
        Assert.Equal(lamp.Falloff, l.Falloff);
        Assert.Equal(lamp.VolumeOpacity, l.VolumeOpacity);
        // The catalog keeps the colour as bytes: within a byte's step of the game's.
        Assert.InRange(l.R - lamp.R, -0.0025f, 0.0025f);
        Assert.InRange(l.G - lamp.G, -0.0025f, 0.0025f);
        Assert.InRange(l.B - lamp.B, -0.0025f, 0.0025f);
        // The room's other lamp lights the Foreground and Photos layers alone, not the room's.
        Assert.Null(PlaceLights.FromComponent(other.ComponentValues.Single(v => v.Type == "Light2D").Params, x, y));
    }
}
