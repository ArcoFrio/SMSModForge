using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A copy of one of the game's objects in a pack's tree (found testing a
/// pack, 2026-11-02): a host mod copied the bedroom's buttons, the living
/// room's TV and an effect in code, and a pack without it lost all three. The
/// game side is the in-game suite's B9; this is what the editor writes.
/// </summary>
public sealed class GameObjectCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForge-copies-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private JObject SavedObject(GameObjectDef node, string name)
    {
        var pack = PackRepository.CreateEmpty("copies");
        pack.Places.Add(new PlaceDef { Key = "room", InternalName = "Room", DisplayName = "Room", GameObjects = { node } });
        PackRepository.Save(pack, _root);
        var saved = JObject.Parse(File.ReadAllText(Path.Combine(_root, PackRepository.ManifestFileName)));
        return Descendants((JArray)saved["places"]![0]!["gameObjects"]!).Single(o => (string?)o["name"] == name);
    }

    private static System.Collections.Generic.IEnumerable<JObject> Descendants(JArray nodes)
        => nodes.OfType<JObject>().SelectMany(n => new[] { n }.Concat(n["children"] is JArray c ? Descendants(c) : Enumerable.Empty<JObject>()));

    [Fact]
    public void A_copy_is_saved_as_what_it_changes_like_a_bound_object()
    {
        var copy = new GameObjectDef
        {
            Name = "Hearts2", CopyOf = "6_Effects/CFXR2 Expression Loving (Timed)", CopyBeside = true,
            ParticleColor = "#FF99E6", Sprite = "never.png", SortingOrder = 7, X = 5f,
            Children = { new GameObjectDef { Name = "Hearts", Bind = true, ParticleColor = "#FF59B3" } },
        };
        var saved = SavedObject(copy, "Hearts2");
        Assert.Equal("6_Effects/CFXR2 Expression Loving (Timed)", (string?)saved["copyOf"]);
        Assert.True((bool?)saved["copyBeside"]);
        Assert.Equal("#FF99E6", (string?)saved["particleColor"]);
        // What belongs to the original stays the original's: no picture, no
        // order, no place of its own unless it overrides one.
        Assert.Null(saved["sprite"]);
        Assert.Null(saved["sortingOrder"]);
        Assert.Null(saved["x"]);
        Assert.False((bool?)saved["overrideTransform"]);
        Assert.Equal("#FF59B3", (string?)Descendants((JArray)saved["children"]!).Single()["particleColor"]);

        // Control: the same node without CopyOf is one of the pack's own and
        // writes all of it.
        copy.CopyOf = "";
        var own = SavedObject(copy, "Hearts2");
        Assert.Null(own["copyOf"]);
        Assert.Null(own["copyBeside"]);
        Assert.Equal("never.png", (string?)own["sprite"]);
        Assert.Equal(5f, (float?)own["x"]);
    }

    [Fact]
    public void A_copy_that_overrides_its_place_saves_it_and_reads_back()
    {
        var saved = SavedObject(new GameObjectDef { Name = "Desk", CopyOf = "5_Levels/32_StorageRoom/Desk", OverrideTransform = true, X = 3f, Y = 2f }, "Desk");
        Assert.True((bool?)saved["overrideTransform"]);
        Assert.Equal(3f, (float?)saved["x"]);

        var back = PackRepository.Load(_root).Places.Single().GameObjects.Single();
        Assert.True(back.IsCopy);
        Assert.Equal("5_Levels/32_StorageRoom/Desk", back.CopyOf);
        Assert.False(back.CopyBeside);
        Assert.Equal(2f, back.Y);
    }

    [Fact]
    public void Bound_wins_over_a_copy_and_neither_is_the_packs_own()
    {
        var node = new GameObjectDef { Name = "Desk", CopyOf = "5_Levels/32_StorageRoom/Desk" };
        var vm = new GameObjectViewModel(node);
        Assert.True(vm.IsCopy);
        Assert.False(vm.IsCreated);
        Assert.True(vm.KeepsWhatItFinds);

        vm.Bind = true;      // the object itself, not a copy of it
        Assert.False(vm.IsCopy);
        Assert.True(vm.KeepsWhatItFinds);
        Assert.Null(SavedObject(node, "Desk")["copyOf"]);

        // Control: neither, and it is the pack's.
        vm.Bind = false;
        vm.CopyOf = "";
        Assert.True(vm.IsCreated);
        Assert.False(vm.KeepsWhatItFinds);
    }

    [Fact]
    public void A_parallax_override_is_saved_and_without_it_the_games_drift_stays()
    {
        var node = new GameObjectDef { Name = "Plant", CopyOf = "5_Levels/8_Pool/8_Pool (2)", OverrideParallax = true, ParallaxStrength = 0.25f };
        var saved = SavedObject(node, "Plant");
        Assert.True((bool?)saved["overrideParallax"]);
        Assert.Equal(0.25f, (float?)saved["parallaxStrength"]);

        // Control: not overridden, a copy writes no strength - the original's stays.
        node.OverrideParallax = false;
        var plain = SavedObject(node, "Plant");
        Assert.Null(plain["overrideParallax"]);
        Assert.Null(plain["parallaxStrength"]);
    }

    [Fact]
    public void Opening_a_level_extension_keeps_a_bound_objects_parallax_override()
    {
        // Opening reads each bound object's drift from the game's for the
        // preview; one the pack overrides keeps its own.
        var level = VanillaLevelCatalog.FindLevel("8_Pool")!;
        var plant = VanillaLevelCatalog.FindNode(level, "8_Pool (2)")!;
        var overridden = new GameObjectDef { Name = plant.Name, Bind = true, OverrideParallax = true, ParallaxStrength = 0.25f };
        VanillaDelta.Rebase(overridden, plant, level);
        Assert.Equal(0.25f, overridden.ParallaxStrength);
        Assert.True(VanillaDelta.IsLiveVanillaChange(overridden));

        // Control: not overridden, the game's own is read back (1.5 for this plant).
        var plain = new GameObjectDef { Name = plant.Name, Bind = true, ParallaxStrength = 0.25f };
        VanillaDelta.Rebase(plain, plant, level);
        Assert.NotEqual(0.25f, plain.ParallaxStrength);
    }

    [Theory]
    [InlineData("#FF99E6", false)]
    [InlineData("FF99E6", false)]
    [InlineData("#FF99E680", false)]
    [InlineData("pink", true)]
    [InlineData("#F9E", true)]       // the game reads only six or eight digits
    public void Validate_names_a_particle_colour_the_game_cannot_read(string colour, bool named)
    {
        var pack = PackRepository.CreateEmpty("copies");
        pack.Places.Add(new PlaceDef
        {
            Key = "room", InternalName = "Room", DisplayName = "Room",
            GameObjects = { new GameObjectDef { Name = "Fx", CopyOf = "6_Effects/Fx",
                                                Children = { new GameObjectDef { Name = "Part", Bind = true, ParticleColor = colour } } } },
        });
        var issues = PackValidator.Validate(pack, _root).Where(i => i.Code == "gameObject.particleColor").ToList();
        Assert.Equal(named, issues.Count == 1);
        if (named) Assert.Contains(colour, issues[0].Message);
    }
}
