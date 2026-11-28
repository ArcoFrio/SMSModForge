using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// How a place's objects move (after 1.10.0, the author: "a way to add masks to other GOs in the place ... choose
/// between the jiggle and level masks"; asked, "no mask" means still). A place saved before the rules: an object without
/// a mask took the back layer's (objects are cloned from it in the game), one with its own mask moved only with a back
/// layer mask too. The migration keeps each moving - or still - as it did, and says so.
/// </summary>
public sealed class ObjectMaskKindsTests
{
    private static ModPack Read(string json) => JsonConvert.DeserializeObject<ModPack>(json)!;

    private const string Before = """
        {
          "places": [
            { "key": "Wet", "baseSprite": "a.png", "secondarySprite": "b.png", "secondaryMaskSprite": "bmask.png",
              "gameObjects": [
                { "name": "Plain", "sprite": "p.png" },
                { "name": "Own", "sprite": "o.png", "mask": "omask.png" },
                { "name": "Group", "children": [ { "name": "Inner", "sprite": "i.png" } ] },
                { "name": "Lamp" } ] },
            { "key": "Dry", "baseSprite": "a.png", "secondarySprite": "b.png",
              "gameObjects": [ { "name": "Plain", "sprite": "p.png" }, { "name": "Own", "sprite": "o.png", "mask": "omask.png" } ] }
          ],
          "vanillaExtensions": [ { "source": "vanilla:5_MyRoom", "gameObjects": [ { "name": "Sign", "sprite": "s.png", "mask": "m.png" } ] } ]
        }
        """;

    [Fact]
    public void A_place_saved_before_keeps_its_objects_moving_as_they_did_and_says_so()
    {
        var pack = Read(Before);
        Assert.All(pack.Places, p => Assert.Equal(0, p.ObjectMasks));
        Assert.Equal(0, pack.VanillaExtensions[0].ObjectMasks);

        var report = PackMigration.Apply(pack);
        var wet = pack.Places[0].GameObjects;
        var dry = pack.Places[1].GameObjects;
        // Under a back layer with a mask: no mask = the back layer's, now said; its own mask = its own.
        Assert.Equal(GameObjectDef.MaskBackLayer, wet[0].MaskMeans);
        Assert.Equal(GameObjectDef.MaskLevel, wet[1].MaskMeans);
        Assert.Equal(GameObjectDef.MaskBackLayer, wet[2].Children[0].MaskMeans);
        Assert.Equal("", wet[3].MaskKind);                                   // a light: no picture, nothing to say
        // Under a plain back layer: still, its own mask too - the game drew it on a material its mask moves nothing on
        // (the author, after loading SMSAndroids: "GOs with movement that wasn't there before at all"). The mask is kept,
        // for when the author chooses it.
        Assert.Equal(GameObjectDef.MaskNone, dry[0].MaskMeans);
        Assert.Equal(GameObjectDef.MaskNone, dry[1].MaskMeans);
        Assert.Equal("omask.png", dry[1].Mask);
        // A game level's objects: cloned from a still picture (or made plain), so the same.
        Assert.Equal(GameObjectDef.MaskNone, pack.VanillaExtensions[0].GameObjects[0].MaskMeans);
        Assert.Equal("m.png", pack.VanillaExtensions[0].GameObjects[0].Mask);
        Assert.All(pack.Places, p => Assert.Equal(PlaceDef.CurrentObjectMasks, p.ObjectMasks));
        Assert.Equal(VanillaPlaceExtensionDef.CurrentObjectMasks, pack.VanillaExtensions[0].ObjectMasks);

        string said = string.Join("\n", report.Changes.Select(c => c.ToString()));
        Assert.Contains(report.Changes, c => c.Count == 3 && c.What.Contains("still unless"));      // two places, one game level
        Assert.Contains(report.Changes, c => c.Count == 2 && c.What.Contains("back layer"));        // Plain, Inner
        Assert.Contains(report.Changes, c => c.Count == 2 && c.What.Contains("Nothing (still)"));   // Dry's Own, the Sign
        Assert.True(report.Migrated, said);

        // Again: nothing to do, nothing said.
        Assert.False(PackMigration.Apply(pack).Migrated && PackMigration.Apply(pack).Changes.Any(c => c.What.Contains("mask")));
    }

    [Fact]
    public void A_new_place_follows_the_rules_and_a_read_one_says_which()
    {
        Assert.Equal(PlaceDef.CurrentObjectMasks, new PlaceDef().ObjectMasks);
        Assert.Equal(VanillaPlaceExtensionDef.CurrentObjectMasks, new VanillaPlaceExtensionDef().ObjectMasks);
        var round = JsonConvert.DeserializeObject<PlaceDef>(JsonConvert.SerializeObject(new PlaceDef { Key = "K" }))!;
        Assert.Equal(PlaceDef.CurrentObjectMasks, round.ObjectMasks);
        Assert.Equal(0, JsonConvert.DeserializeObject<PlaceDef>("""{ "key": "K" }""")!.ObjectMasks);
    }

    /// <summary>The kind is written only when the mask alone does not say it; the jiggle's settings only for a jiggle.</summary>
    [Fact]
    public void Only_what_the_mask_does_not_say_is_written()
    {
        static JObject Written(GameObjectDef o) => JObject.Parse(JsonConvert.SerializeObject(o));
        var own = Written(new GameObjectDef { Name = "A", Sprite = "a.png", Mask = "m.png", MaskKind = GameObjectDef.MaskLevel });
        Assert.Null(own["maskKind"]);
        var still = Written(new GameObjectDef { Name = "A", Sprite = "a.png", MaskKind = GameObjectDef.MaskNone });
        Assert.Null(still["maskKind"]);
        var stillWithFile = Written(new GameObjectDef { Name = "A", Sprite = "a.png", Mask = "m.png", MaskKind = GameObjectDef.MaskNone });
        Assert.Equal("none", (string?)stillWithFile["maskKind"]);
        var back = Written(new GameObjectDef { Name = "A", Sprite = "a.png", MaskKind = GameObjectDef.MaskBackLayer });
        Assert.Equal("backLayer", (string?)back["maskKind"]);
        var jiggle = Written(new GameObjectDef { Name = "A", Sprite = "a.png", Mask = "m.png", MaskKind = GameObjectDef.MaskJiggle, Jiggle = NpcDef.NewNpcJiggle() });
        Assert.Equal("jiggle", (string?)jiggle["maskKind"]);
        Assert.NotNull(jiggle["jiggle"]);
        var levelWithJiggleLeft = Written(new GameObjectDef { Name = "A", Sprite = "a.png", Mask = "m.png", MaskKind = GameObjectDef.MaskLevel, Jiggle = NpcDef.NewNpcJiggle() });
        Assert.Null(levelWithJiggleLeft["jiggle"]);
    }
}
