using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A place's lights in the pack (1.10.0), through the real save
/// (<see cref="PackRepository.Save"/>, read back as the game reads it): the
/// template level's light every pack place starts with is in memory until it
/// is changed or removed, and only then written; a lamp of the game's in a
/// vanilla extension the same; a light writes only its own shape's settings.
/// And the preview's light model: a cone lights only inside its angle and an
/// inner radius keeps it at full strength, as the game's light program does.
/// </summary>
public sealed class PlaceLightModelTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-lightmodel-" + Guid.NewGuid().ToString("N"));

    public PlaceLightModelTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private JObject SaveAndRead(ModPack pack)
    {
        PackRepository.Save(pack, _dir);
        return JObject.Parse(File.ReadAllText(Path.Combine(_dir, "modpack.json")));
    }

    private static JObject? Node(JObject manifest, string place, string name)
        => (manifest["places"] as JArray)?.OfType<JObject>().First(p => (string?)p["key"] == place)["gameObjects"]?
            .OfType<JObject>().FirstOrDefault(n => (string?)n["name"] == name);

    [Fact]
    public void A_pack_places_template_light_is_written_only_once_changed_or_removed()
    {
        var pack = PackRepository.CreateEmpty("light.pack");
        pack.Places.Add(new PlaceDef { Key = "hall", InternalName = "Hall", DisplayName = "Hall" });
        var vm = new PlaceViewModel(pack.Places[0]);
        var template = vm.GameObjects.SingleOrDefault(g => g.IsTemplateLight);
        Assert.True(template?.Light != null, "a pack place does not start with the template's light");
        var catalog = TemplateLight.Baseline!.ComponentValues.Single(c => c.Type == "Light2D").Params;
        Assert.Equal(LightDef.NormalColor((string)catalog["m_Color"]!), template!.Light!.Color);

        // Untouched: not in the file.
        Assert.Null(Node(SaveAndRead(pack), "hall", TemplateLight.ObjectName));

        // Changed: the node, bound, with its whole light.
        template.Light.Strength = 0.6f;
        var changed = Node(SaveAndRead(pack), "hall", TemplateLight.ObjectName);
        _out.WriteLine(changed?.ToString() ?? "(none)");
        Assert.NotNull(changed);
        Assert.True((bool?)changed!["bind"] == true);
        Assert.Equal(0.6f, (float)changed["light"]!["strength"]!, 3);
        Assert.Equal(template.Light.OuterRadius, (float)changed["light"]!["outerRadius"]!, 3);
        Assert.Null(changed["x"]);   // its place is the template's: a bound node writes no transform it does not change

        // Removed: switched off, no light written; read back, it stays removed.
        template.RemoveLight();
        var removed = Node(SaveAndRead(pack), "hall", TemplateLight.ObjectName);
        Assert.True((bool?)removed?["removeLight"] == true);
        Assert.Null(removed!["light"]);
        var reread = new PlaceViewModel(PackRepository.Load(_dir).Places[0]).GameObjects.Single(g => g.IsTemplateLight);
        Assert.True(reread.LightRemoved && reread.Light == null, "a removed template light came back on reading the pack");

        // Brought back: out of the file again.
        template.RestoreLight();
        Assert.Null(Node(SaveAndRead(pack), "hall", TemplateLight.ObjectName));
    }

    [Fact]
    public void A_light_writes_only_its_own_shapes_settings()
    {
        var pack = PackRepository.CreateEmpty("light.pack");
        pack.Places.Add(new PlaceDef { Key = "hall", InternalName = "Hall", DisplayName = "Hall" });
        var vm = new PlaceViewModel(pack.Places[0]);
        var round = vm.AddGameObject();
        round.Name = "Round";
        round.AddLight().OuterAngle = 120;
        var free = vm.AddGameObject();
        free.Name = "Free";
        free.AddLight().Shape = LightDef.ShapeFree;
        var m = SaveAndRead(pack);
        var r = (JObject)Node(m, "hall", "Round")!["light"]!;
        var f = (JObject)Node(m, "hall", "Free")!["light"]!;
        _out.WriteLine(r.ToString());
        _out.WriteLine(f.ToString());
        Assert.Equal("round", (string?)r["shape"]);
        Assert.NotNull(r["outerRadius"]);
        Assert.NotNull(r["outerAngle"]);
        Assert.Null(r["points"]);
        Assert.Null(r["edge"]);
        Assert.Equal("free", (string?)f["shape"]);
        Assert.Equal(4, ((JArray)f["points"]!).Count);
        Assert.NotNull(f["edge"]);
        Assert.Null(f["outerRadius"]);
        Assert.Null(f["outerAngle"]);
    }

    [Fact]
    public void A_lamp_of_the_games_in_a_vanilla_extension_is_written_only_once_changed()
    {
        var pack = PackRepository.CreateEmpty("light.pack");
        var ext = new VanillaPlaceExtensionDef { Source = "vanilla:5_MyRoom" };
        pack.VanillaExtensions.Add(ext);
        var vm = new VanillaPlaceExtensionViewModel(ext);
        var lamp = vm.GameObjects.Single(g => g.Name == "Light 2D");
        Assert.NotNull(lamp.Light);
        Assert.True(lamp.IsGameLightOnly, "the room's lamp is not seen as a light of the game's");
        Assert.Null(SaveAndRead(pack)["vanillaExtensions"]?[0]?["gameObjects"]);

        lamp.Light!.Color = "#FF8800";
        var saved = SaveAndRead(pack)["vanillaExtensions"]![0]!["gameObjects"]!.OfType<JObject>().Single();
        _out.WriteLine(saved.ToString());
        Assert.Equal("Light 2D", (string?)saved["name"]);
        Assert.Equal("#FF8800", (string?)saved["light"]!["color"]);

        // Removing it from the tree switches it off in the game rather than forgetting the change.
        lamp.RemoveCommand.Execute(null);
        Assert.Contains(lamp, vm.GameObjects);
        saved = SaveAndRead(pack)["vanillaExtensions"]![0]!["gameObjects"]!.OfType<JObject>().Single();
        Assert.True((bool?)saved["removeLight"] == true);
        Assert.Null(saved["light"]);
    }

    [Fact]
    public void A_light_the_game_cannot_make_is_reported_and_a_good_one_is_not()
    {
        var pack = PackRepository.CreateEmpty("light.pack");
        pack.Places.Add(new PlaceDef
        {
            Key = "hall", InternalName = "Hall", DisplayName = "Hall",
            GameObjects =
            {
                new GameObjectDef { Name = "Line", Light = new LightDef { Shape = LightDef.ShapeFree, Points = { new[] { 0f, 0f }, new[] { 1f, 0f } } } },
                new GameObjectDef { Name = "Blank", Light = new LightDef { Shape = LightDef.ShapePicture } },
                new GameObjectDef { Name = "Gone", Light = new LightDef { Shape = LightDef.ShapePicture, Picture = "Lights/none.png" } },
                new GameObjectDef { Name = "Fine", Light = new LightDef() },
                new GameObjectDef { Name = "FineFree", Light = new LightDef { Shape = LightDef.ShapeFree, Points = LightDef.Square(1f) } },
            },
        });
        PackRepository.Save(pack, _dir);
        var issues = SMSModForge.Validation.PackValidator.Validate(pack, _dir).Where(i => i.Where.Contains(".light.")).ToList();
        foreach (var i in issues) _out.WriteLine($"{i.Severity} {i.Where}: {i.Message}");
        Assert.Contains(issues, i => i.Code == "light.fewPoints" && i.Where.Contains(".Line."));
        Assert.Contains(issues, i => i.Code == "light.noPicture" && i.Where.Contains(".Blank."));
        Assert.Contains(issues, i => i.Where.Contains(".Gone.light.picture"));
        Assert.DoesNotContain(issues, i => i.Where.Contains(".Fine."));
        Assert.DoesNotContain(issues, i => i.Where.Contains(".FineFree."));
    }

    [Fact]
    public void Lighting_the_characters_is_written_when_on_and_always_for_a_light_of_the_games()
    {
        var pack = PackRepository.CreateEmpty("light.pack");
        pack.Places.Add(new PlaceDef { Key = "hall", InternalName = "Hall", DisplayName = "Hall" });
        var vm = new PlaceViewModel(pack.Places[0]);
        var on = vm.AddGameObject(); on.Name = "On"; on.AddLight().LightsCharacters = true;
        var off = vm.AddGameObject(); off.Name = "Off"; off.AddLight();
        // The template's light, lighting the characters: written with it; then not, written as off (the plugin takes
        // the characters' layer off a light of the game's only when told).
        var template = vm.GameObjects.Single(g => g.IsTemplateLight);
        Assert.False(template.Light!.LightsCharacters);   // the Beach's lights the place alone
        template.Light.LightsCharacters = true;
        var m = SaveAndRead(pack);
        Assert.True((bool?)Node(m, "hall", "On")!["light"]!["lightsCharacters"] == true);
        Assert.Null(Node(m, "hall", "Off")!["light"]!["lightsCharacters"]);
        Assert.True((bool?)Node(m, "hall", TemplateLight.ObjectName)!["light"]!["lightsCharacters"] == true);
        template.Light.LightsCharacters = false;
        template.Light.Strength = 0.3f;
        m = SaveAndRead(pack);
        Assert.True((bool?)Node(m, "hall", TemplateLight.ObjectName)!["light"]!["lightsCharacters"] == false);
        // One of the game's lamps that lights the characters reads as one.
        var lamp = VanillaLevelCatalog.All.SelectMany(l => Walk(l.Hierarchy))
            .SelectMany(n => n.ComponentValues).Where(c => c.Type == "Light2D").Select(c => LightDef.FromCatalog(c.Params))
            .FirstOrDefault(d => d?.GameLayers?.Contains("Bust") == true);
        Assert.True(lamp?.LightsCharacters == true, "a lamp of the game's lighting the busts does not read as lighting the characters");
    }

    private static System.Collections.Generic.IEnumerable<VanillaLevelCatalog.Node> Walk(VanillaLevelCatalog.Node? n)
    {
        if (n == null) yield break;
        yield return n;
        foreach (var c in n.Children) foreach (var d in Walk(c)) yield return d;
    }

    [Fact]
    public void A_whole_place_light_writes_its_colour_strength_and_whether_it_takes_the_characters_only()
    {
        var pack = PackRepository.CreateEmpty("light.pack");
        pack.Places.Add(new PlaceDef { Key = "hall", InternalName = "Hall", DisplayName = "Hall" });
        var vm = new PlaceViewModel(pack.Places[0]);
        var dark = LightObjects.Dress(vm.AddGameObject(), vm.GameObjects, LightObjects.Darkness);
        Assert.Equal("Darkness", dark.Name);
        Assert.True(dark.Light!.IsAmbient);
        var night = (JObject)Node(SaveAndRead(pack), "hall", "Darkness")!["light"]!;
        _out.WriteLine(night.ToString());
        Assert.Equal(new[] { "color", "shape", "strength" }, night.Properties().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal("ambient", (string?)night["shape"]);
        // Two always on in one place: reported; one of them on a condition: not.
        var second = LightObjects.Dress(vm.AddGameObject(), vm.GameObjects, LightObjects.Darkness);
        PackRepository.Save(pack, _dir);
        bool Reported() => SMSModForge.Validation.PackValidator.Validate(pack, _dir).Any(i => i.Code == "light.twoAmbients");
        Assert.True(Reported(), "two whole-place lights always on are not reported");
        second.StartActive = false;
        Assert.False(Reported(), "control: one of them switched off at the start is still reported");
    }

    [Fact]
    public void A_place_s_own_base_light_darkens_the_place_and_its_lamps_light_it_back_up()
    {
        var lamp = PlaceLights.FromLight(new LightDef { Strength = 1f, OuterRadius = 4f }, 480, 270)!.Value;
        var day = new PlaceLights(new[] { lamp }, (1f, 1f, 1f), null);
        var night = new PlaceLights(new[] { lamp }, (0.3f, 0.3f, 0.35f), (1f, 1f, 1f), null);
        float dayFar = Texel(day, 100, 100), nightFar = Texel(night, 100, 100), nightNear = Texel(night, 480, 270);
        _out.WriteLine($"far from the lamp: day {dayFar}, night {nightFar}; at the lamp at night {nightNear}");
        Assert.Equal(1f, dayFar);
        Assert.InRange(nightFar, 0.29f, 0.31f);
        Assert.True(nightNear > 1f, "the lamp does not light the dark place back up");
        // The characters keep the scene's white light unless the place's takes theirs too.
        Assert.Equal((1f, 1f, 1f), night.CharactersLightAt(100, 100));
        Assert.False(night.ChangesCharacters);
        var both = new PlaceLights(new[] { lamp }, (0.3f, 0.3f, 0.35f), (0.3f, 0.3f, 0.35f), null);
        Assert.True(both.ChangesCharacters);
        Assert.InRange(both.CharactersLightAt(100, 100).g, 0.29f, 0.31f);
    }

    // ── The preview's light model ───────────────────────────────────────

    private static float Texel(PlaceLights lights, float sx, float sy)
    {
        var (w, h) = lights.TextureSize;
        int i = Math.Clamp((int)(sx / PlaceLights.ScreenW * w), 0, w - 1), j = Math.Clamp((int)(sy / PlaceLights.ScreenH * h), 0, h - 1);
        return lights.Texels[(j * w + i) * 3 + 1];   // green, rows from the bottom
    }

    private static PlaceLights One(LightDef def, float rotation = 0f)
        => new(new[] { PlaceLights.FromLight(def, 480, 270, rotation)!.Value }, (1f, 1f, 1f), null);

    [Fact]
    public void A_cone_lights_only_inside_its_angle_turned_with_its_object()
    {
        var cone = new LightDef { Strength = 1f, OuterRadius = 4f, OuterAngle = 90f, InnerAngle = 60f };
        var up = One(cone);
        var turned = One(cone, 180f);
        float above = Texel(up, 480, 270 + 80), below = Texel(up, 480, 270 - 80), side = Texel(up, 480 + 80, 270);
        float aboveTurned = Texel(turned, 480, 270 + 80), belowTurned = Texel(turned, 480, 270 - 80);
        _out.WriteLine($"cone up: above {above}, below {below}, beside {side}; turned round: above {aboveTurned}, below {belowTurned}");
        Assert.True(above > 1.2f, "a cone does not light along its up");
        Assert.Equal(1f, below);
        Assert.Equal(1f, side);   // 90 degrees to its up is outside a 90 degree cone
        // Control: turned round, the other way.
        Assert.Equal(1f, aboveTurned);
        Assert.True(belowTurned > 1.2f, "control: the cone turned round does not light below");
    }

    /// <summary>The game's screen transform of an object at the screen's middle, unturned, <paramref name="pixels"/>
    /// screen pixels to its unit (screen = pixels x object + middle).</summary>
    private static (double a, double b, double c, double d, double tx, double ty) Middle(double pixels) => (pixels, 0, 0, pixels, 480, 270);

    [Fact]
    public void A_free_shape_lights_its_shape_fully_and_fades_over_its_soft_edge_to_nothing()
    {
        // A square two units across, 40 screen pixels to the unit, its soft edge one unit wide.
        var (a, b, c, d, tx, ty) = Middle(40);
        var shape = LightShape.Free(new[] { -1f, 1f, 1f, -1f }, new[] { -1f, -1f, 1f, 1f }, 1f, a, b, c, d, tx, ty);
        var def = new LightDef { Shape = LightDef.ShapeFree, Strength = 1f, Falloff = 0.5f };
        var lights = new PlaceLights(new[] { PlaceLights.FromShaped(def, shape)!.Value }, (1f, 1f, 1f), null);
        float inside = Texel(lights, 480 + 30, 270), halfEdge = Texel(lights, 480 + 60, 270), past = Texel(lights, 480 + 90, 270);
        _out.WriteLine($"inside {inside}, half way across its soft edge {halfEdge}, past it {past}");
        Assert.True(inside >= 1.95f, "the shape is not lit fully inside");
        Assert.True(halfEdge > 1.05f && halfEdge < inside - 0.2f, "half way across the soft edge is not part lit");
        Assert.Equal(1f, past);
        // Control: the same square with no soft edge stops at its side.
        var hard = new PlaceLights(new[] { PlaceLights.FromShaped(def, LightShape.Free(new[] { -1f, 1f, 1f, -1f }, new[] { -1f, -1f, 1f, 1f }, 0f, a, b, c, d, tx, ty))!.Value },
                                   (1f, 1f, 1f), null);
        Assert.Equal(1f, Texel(hard, 480 + 60, 270));
    }

    [Fact]
    public void A_picture_light_lights_with_its_pictures_colours_where_it_is_opaque()
    {
        // Four pixels across, two high: the left half red, the right half see-through white.
        int w = 4, h = 2;
        var rgba = new float[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                if (x < 2) { rgba[o] = 1f; rgba[o + 3] = 1f; }
                else { rgba[o] = rgba[o + 1] = rgba[o + 2] = 1f; rgba[o + 3] = 0f; }
            }
        // At a pixel per unit, 40 screen pixels to the unit: 160 x 80 screen pixels.
        var (a, b, c, d, tx, ty) = Middle(40);
        var shape = LightShape.Picture(rgba, w, h, 1f, "test", a, b, c, d, tx, ty);
        var def = new LightDef { Shape = LightDef.ShapePicture, Strength = 1f };
        var lights = new PlaceLights(new[] { PlaceLights.FromShaped(def, shape)!.Value }, (1f, 1f, 1f), null);
        var (tw, th) = lights.TextureSize;
        float[] At(float sx, float sy)
        {
            int i = (int)(sx / PlaceLights.ScreenW * tw), j = (int)(sy / PlaceLights.ScreenH * th);
            return lights.Texels.Skip((j * tw + i) * 3).Take(3).ToArray();
        }
        var left = At(480 - 50, 270); var right = At(480 + 50, 270); var off = At(480 - 120, 270);
        _out.WriteLine($"left half {string.Join(",", left)}, right half {string.Join(",", right)}, off the picture {string.Join(",", off)}");
        Assert.True(left[0] > 1.9f && left[1] == 1f && left[2] == 1f, "the opaque red half does not light red");
        Assert.Equal(new[] { 1f, 1f, 1f }, right);   // see-through: nothing, whatever its colour
        Assert.Equal(new[] { 1f, 1f, 1f }, off);
    }

    [Fact]
    public void An_inner_radius_keeps_the_light_at_full_strength_out_to_it()
    {
        var plain = One(new LightDef { Strength = 1f, OuterRadius = 4f });
        var full = One(new LightDef { Strength = 1f, OuterRadius = 4f, InnerRadius = 2.4f });
        float atMiddle = Texel(full, 480, 270), halfway = Texel(full, 480 + 100, 270), plainHalfway = Texel(plain, 480 + 100, 270);
        _out.WriteLine($"inner 0.6 of the reach: middle {atMiddle}, half the reach out {halfway}; without: {plainHalfway}");
        Assert.Equal(atMiddle, halfway);
        Assert.True(plainHalfway < halfway - 0.1f, "control: without an inner radius the light is as strong half way out");
    }
}
