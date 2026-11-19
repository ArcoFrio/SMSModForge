#if DEBUG   // the export is compiled only in Debug (see PlaceExport)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using SMSModForge.Model;
using SMSModForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The place pictures export (1.9.2): a game level and a pack's place drawn into a folder as the Places tab's
/// preview draws them, at the game's screen, with the game's look. Measured from the pictures written: a dark place
/// with a white square glows beside the square (the look's bloom), where the same place drawn without the look (the
/// control) does not; the game's level is its own art, not an empty screen; places.json lists what was written.
/// </summary>
[Trait("Speed", "Slow")]   // builds WPF previews on the harness's thread; see CLAUDE.md
public sealed class PlaceExportTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-placeexport-" + Guid.NewGuid().ToString("N"));

    public PlaceExportTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static void WriteSquare(string path, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool square = Math.Abs(x - w / 2) < w / 12 && Math.Abs(y - h / 2) < w / 12;
                byte v = square ? (byte)255 : (byte)30;
                px[i] = v; px[i + 1] = v; px[i + 2] = v; px[i + 3] = 255;
            }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static (int w, int h, byte[] px) Read(string path)
    {
        var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        return (bgra.PixelWidth, bgra.PixelHeight, px);
    }

    private static double Luma(byte[] px, int w, int x, int y) { int i = (y * w + x) * 4; return (px[i] + px[i + 1] + px[i + 2]) / 3.0; }

    private static void WriteBlock(string path, int w, int h, byte r, byte g, byte b)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>
    /// A pack NPC at her spot, shown when her character is there: not in the place as found on arrival, and drawn on
    /// its own as a group over it (the group's picture differs from the place's where she stands, and only there);
    /// the game's crowds the same, each of the Beach's three a group the game shows one at a time.
    /// </summary>
    [Fact]
    public void EachNpcGroupIsDrawnOnItsOwnOverThePlace()
    {
        string pack = Path.Combine(_dir, "pack"), outDir = Path.Combine(_dir, "out");
        WriteSquare(Path.Combine(pack, "Places", "front.png"), 2048, 1136);
        WriteBlock(Path.Combine(pack, "NPCs", "girl.png"), 120, 300, 220, 40, 40);
        var model = PackRepository.CreateEmpty("npc.pack");
        model.Npcs.Add(new NpcDef { Key = "girl", DisplayName = "girl", Sprite = "NPCs/girl.png" });
        var spot = new GameObjectDef { Name = "Couch" };
        // Switched off as built, as packs often have them: their spot's conditions or an action switch them on.
        spot.Npcs.Add(new NpcPlacementDef { Npc = "girl", Name = "girl", StartActive = false, Body = new NpcTransform { X = -6f, Y = 0f } });
        spot.ActiveConditions.Add(new NodeConditionDef { Type = "VariableCompare", Params = { ["name"] = "Location_Girl", ["value"] = "Couch", ["comparison"] = "equals" } });
        var npcsRoot = new GameObjectDef { Name = "NPCs" };
        npcsRoot.Children.Add(spot);
        model.Places.Add(new PlaceDef { Key = "room", InternalName = "Room", DisplayName = "Room", BaseSprite = "Places/front.png" });
        model.Places[0].GameObjects.Add(npcsRoot);
        PackRepository.Save(model, pack);
        string room = "pack:" + model.PackId + ".room";
        PlaceExport.Manifest manifest = null!;
        WindowHarness.Run(_ => manifest = PlaceExport.Run(outDir, pack, _out.WriteLine, new[] { room, "vanilla:14_Beach" },
                                                          Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "VanillaLevelArt")));

        var place = manifest.Places.Single(p => p.Token == room);
        var g = Assert.Single(place.Groups);
        Assert.Equal(("NPCs/Couch", false, true), (g.Path, g.OnArrival, g.Conditional));
        Assert.Equal(new[] { "girl" }, g.Npcs);
        var (w, h, alone) = Read(Path.Combine(outDir, place.File));
        var (_, _, with) = Read(Path.Combine(outDir, g.File));
        // Where they differ: around her, left of the centre (x -6 world units = 600 canvas px left), nowhere else.
        int differ = 0, far = 0;
        for (int y = 0; y < h; y += 4)
            for (int x = 0; x < w; x += 4)
            {
                if (Math.Abs(Luma(alone, w, x, y) - Luma(with, w, x, y)) < 12) continue;
                differ++;
                if (Math.Abs(x - (w / 2 - 600)) > 300) far++;
            }
        _out.WriteLine($"group picture differs from the place's at {differ} sampled pixels, {far} of them far from her");
        Assert.True(differ > 50, "the group's picture does not show her");
        Assert.True(far < differ / 10, "the group's picture differs from the place's away from her");

        var beach = manifest.Places.Single(p => p.Token == "vanilla:14_Beach");
        Assert.Equal(new[] { "NPCs/Group_1", "NPCs/Group_2", "NPCs/Group_3" }, beach.Groups.Select(x => x.Path));
        Assert.All(beach.Groups, x => Assert.True(x.OneOf && !x.OnArrival && x.Figures > 0 && File.Exists(Path.Combine(outDir, x.File)), x.Path));
        // A crowd the game switches on is drawn as the game draws it then, not faint as the editing view shows what
        // is switched off (its parent is: a faint crowd differs from the place by a few levels at most).
        var (bw, bh, emptyBeach) = Read(Path.Combine(outDir, beach.File));
        var (_, _, crowd) = Read(Path.Combine(outDir, beach.Groups[2].File));
        double most = 0;
        for (int y = 0; y < bh; y += 2)
            for (int x = 0; x < bw; x += 2)
                most = Math.Max(most, Math.Abs(Luma(emptyBeach, bw, x, y) - Luma(crowd, bw, x, y)));
        _out.WriteLine($"the Beach's third crowd differs from the empty Beach by up to {most:F0} levels");
        Assert.True(most > 60, "the crowd is drawn faint");
    }

    /// <summary>
    /// A spot whose outfit switches on one of its NPCs at random (RandomChildActivator: one NPC in two poses, as the
    /// pack's Anis spots) is drawn one pose to a group, never two together, and neither in the place's own picture.
    /// Two poses standing apart (x -6 and +6): each group's picture differs from the place's around its own pose and
    /// not around the other's. Drawn as before (one group for the spot), both would show in it, overlapping where they
    /// stand together in the pack.
    /// </summary>
    [Fact]
    public void EachPoseASpotPicksAtRandomIsAGroupOfItsOwn()
    {
        string pack = Path.Combine(_dir, "pack"), outDir = Path.Combine(_dir, "out");
        WriteSquare(Path.Combine(pack, "Places", "front.png"), 2048, 1136);
        WriteBlock(Path.Combine(pack, "NPCs", "lying.png"), 120, 300, 220, 40, 40);
        WriteBlock(Path.Combine(pack, "NPCs", "sitting.png"), 120, 300, 40, 200, 40);
        var model = PackRepository.CreateEmpty("poses.pack");
        model.Npcs.Add(new NpcDef { Key = "lying", DisplayName = "lying", Sprite = "NPCs/lying.png" });
        model.Npcs.Add(new NpcDef { Key = "sitting", DisplayName = "sitting", Sprite = "NPCs/sitting.png" });
        var outfit = new GameObjectDef { Name = "Default" };   // on arrival, unconditional: the place shows one pose
        outfit.Components.Add(new ComponentDef { Type = PackComponentType.RandomChildActivator });
        outfit.Npcs.Add(new NpcPlacementDef { Npc = "lying", Name = "lying", Body = new NpcTransform { X = -6f, Y = 0f } });
        outfit.Npcs.Add(new NpcPlacementDef { Npc = "sitting", Name = "sitting", Body = new NpcTransform { X = 6f, Y = 0f } });
        var spot = new GameObjectDef { Name = "Bed" };
        spot.Children.Add(outfit);
        var npcsRoot = new GameObjectDef { Name = "NPCs" };
        npcsRoot.Children.Add(spot);
        model.Places.Add(new PlaceDef { Key = "room", InternalName = "Room", DisplayName = "Room", BaseSprite = "Places/front.png" });
        model.Places[0].GameObjects.Add(npcsRoot);
        PackRepository.Save(model, pack);
        string room = "pack:" + model.PackId + ".room";
        PlaceExport.Manifest manifest = null!;
        WindowHarness.Run(_ => manifest = PlaceExport.Run(outDir, pack, _out.WriteLine, new[] { room }));

        var place = manifest.Places.Single();
        Assert.Equal(new[] { ("NPCs/Bed/Default/lying", "lying"), ("NPCs/Bed/Default/sitting", "sitting") },
                     place.Groups.Select(g => (g.Path, g.Npcs.Single())));
        Assert.All(place.Groups, g => Assert.True(g.Random && g.OneOf && !g.OnArrival && g.Set == "NPCs/Bed/Default", g.Path));
        var (w, h, alone) = Read(Path.Combine(outDir, place.File));
        foreach (var (g, own, other) in new[] { (place.Groups[0], w / 2 - 600, w / 2 + 600), (place.Groups[1], w / 2 + 600, w / 2 - 600) })
        {
            var (_, _, with) = Read(Path.Combine(outDir, g.File));
            int near = 0, far = 0;
            for (int y = 0; y < h; y += 4)
                for (int x = 0; x < w; x += 4)
                {
                    if (Math.Abs(Luma(alone, w, x, y) - Luma(with, w, x, y)) < 12) continue;
                    if (Math.Abs(x - own) < 300) near++;
                    if (Math.Abs(x - other) < 300) far++;
                }
            _out.WriteLine($"{g.Path}: differs from the place around its own pose at {near} sampled pixels, around the other's at {far}");
            Assert.True(near > 50, g.Path + " is not drawn in its group, or is already in the place's own picture");
            Assert.True(far == 0, g.Path + "'s group shows the other pose too");
        }
    }

    private static string LevelArt => Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "VanillaLevelArt");

    private static System.Collections.Generic.IEnumerable<ViewModel.GameObjectViewModel> Walk(System.Collections.Generic.IEnumerable<ViewModel.GameObjectViewModel> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Walk(n.Children)) yield return c;
        }
    }

    private static int Distance(byte[] a, int aw, int ax, int ay, byte[] b, int bw, int bx, int by)
    {
        int i = (ay * aw + ax) * 4, j = (by * bw + bx) * 4;
        return Math.Abs(a[i] - b[j]) + Math.Abs(a[i + 1] - b[j + 1]) + Math.Abs(a[i + 2] - b[j + 2]);
    }

    /// <summary>
    /// A game object whose sprite is not pivoted on its centre is drawn where the game draws it. The Beach's sea
    /// ("14_Beach (1)", A14Water) is cut out of a whole level's canvas but keeps that canvas's centre as its pivot (1.108,
    /// 2.387 of its rect): drawn centred on its rect, it hung across the sky. Measured against the game's own frame of
    /// the Beach (Backdrops/Beach/room.png): where the pivots change the picture, the picture with them is as near the
    /// game's as the rest of the picture is (which differs from it by the levels' own lamps, not drawn yet, and the crowds
    /// the game showed), and the control (every object pivoted on its centre, as the preview drew them before) is far.
    /// </summary>
    [Fact]
    public void AnObjectPivotedOffItsCentreIsDrawnWhereTheGameDrawsIt()
    {
        byte[] pivoted = null!, centred = null!;
        WindowHarness.Run(_ =>
        {
            PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited();   // the look, as the export waits for it
            var vm = PlaceExport.VanillaLevel("vanilla:14_Beach", null, LevelArt);
            Assert.Contains(Walk(vm.GameObjects), n => Math.Abs(n.Model.SpritePivotY - 0.5f) > 1f);   // the sea's, from the catalog
            pivoted = PlaceExport.Still(PlaceExport.VanillaPreview(vm, null, null, LevelArt)).Pixels;
            foreach (var n in Walk(vm.GameObjects)) { n.Model.SpritePivotX = 0.5f; n.Model.SpritePivotY = 0.5f; }
            centred = PlaceExport.Still(PlaceExport.VanillaPreview(vm, null, null, LevelArt)).Pixels;
        });
        var (gw, gh, game) = Read(Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "Backdrops", "Beach", "room.png"));
        int w = PlaceExport.Width, sx = w / gw, sy = PlaceExport.Height / gh;
        long moved = 0, offPivoted = 0, offCentred = 0, rest = 0, offRest = 0;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                if (Distance(pivoted, w, x * sx, y * sy, centred, w, x * sx, y * sy) < 90)
                {
                    rest++;
                    offRest += Distance(pivoted, w, x * sx, y * sy, game, gw, x, y);
                    continue;
                }
                moved++;
                offPivoted += Distance(pivoted, w, x * sx, y * sy, game, gw, x, y);
                offCentred += Distance(centred, w, x * sx, y * sy, game, gw, x, y);
            }
        double near = offPivoted / (double)Math.Max(1, moved), far = offCentred / (double)Math.Max(1, moved);
        double elsewhere = offRest / (double)Math.Max(1, rest);
        _out.WriteLine($"the pivots change {moved} of {gw * gh} points; off the game's frame there by {near:F0} with them, "
                       + $"{far:F0} without; the rest of the picture by {elsewhere:F0}");
        Assert.True(moved > gw * gh / 50, "the pivots change next to nothing on the Beach");
        Assert.True(near < elsewhere * 1.25, "with the pivots, where they change the Beach is still further off the game's frame than the rest");
        Assert.True(far > near * 1.5, "the control is as near the game's frame: the measure does not tell the pivots apart");
    }

    /// <summary>
    /// A game level is drawn at the game's size and where the game shows it (1.9.2). The vanilla preview took the
    /// level's own px-per-unit and fitted the art to the level frame as well, as a pack's art is fitted: twice. Most
    /// levels came out 1% small, and the seven drawn from 2912x1632 art at 100 a unit at 70% - the Kitchen and the park
    /// framed in black. And it drew every level at the camera's centre, where the game keeps them 0.2 units lower
    /// (their parent, 5_Levels, at 0.685,-0.2): 13 game pixels. Measured against the game's own frames of the PC's room
    /// (one of the seven) and the Beach, captured with the levels at rest (not following the mouse): the scale and
    /// shift at which the picture's edges line up with the frame's (the lamps, not drawn yet, move no edges) are 1 and
    /// none. Controls: the art fitted again lines up at the old scales, about 1.44 and 1.01; the PC's room at the
    /// camera's centre, 13 pixels off.
    /// </summary>
    [Fact]
    public void ALevelsOwnArtIsDrawnAtTheGamesSize()
    {
        var found = new Dictionary<string, (double Own, double Fitted)>();
        (double Scale, int Dx, int Dy, double Match) centred = default;
        foreach (var (level, backdrop) in new[] { ("5_MyRoom", "MyRoom"), ("14_Beach", "Beach") })
        {
            byte[] own = null!, fitted = null!, centredPixels = null!;
            WindowHarness.Run(_ =>
            {
                PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited();   // the look, as the export waits for it
                var vm = PlaceExport.VanillaLevel("vanilla:" + level, null, LevelArt);
                own = PlaceExport.Still(PlaceExport.VanillaPreview(vm, null, null, LevelArt)).Pixels;
                var control = PlaceExport.VanillaPreview(vm, null, null, LevelArt);
                control.LevelArtIsTheGames = false;
                fitted = PlaceExport.Still(control).Pixels;
                if (level == "5_MyRoom")
                {
                    var atCentre = PlaceExport.VanillaPreview(vm, null, null, LevelArt);
                    atCentre.LevelPositionX = 0; atCentre.LevelPositionY = 0;
                    centredPixels = PlaceExport.Still(atCentre).Pixels;
                }
            });
            var (gw, gh, game) = Read(Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "Backdrops", backdrop, "room.png"));
            var a = LinesUp(own, PlaceExport.Width, PlaceExport.Height, game, gw, gh);
            var c = LinesUp(fitted, PlaceExport.Width, PlaceExport.Height, game, gw, gh);
            if (level == "5_MyRoom") centred = LinesUp(centredPixels, PlaceExport.Width, PlaceExport.Height, game, gw, gh);
            _out.WriteLine($"{level}: its edges line up with the game's frame at scale {a.Scale:F3} (shift {a.Dx:+0;-0;0},{a.Dy:+0;-0;0} px, "
                           + $"match {a.Match:F2}); fitted again, at {c.Scale:F3} (match {c.Match:F2})"
                           + (level == "5_MyRoom" ? $"; at the camera's centre, shifted {centred.Dx:+0;-0;0},{centred.Dy:+0;-0;0}" : ""));
            Assert.True(a.Match > 0.5, $"{level}: the picture does not line up with the game's frame at any scale (best {a.Match:F2})");
            Assert.True(Math.Abs(a.Dx) <= 1 && Math.Abs(a.Dy) <= 1, $"{level} is drawn {a.Dx:+0;-0;0},{a.Dy:+0;-0;0} game px from where the game shows it");
            found[level] = (a.Scale, c.Scale);
        }
        Assert.True(Math.Abs(centred.Dy) >= 8, $"control: the PC's room at the camera's centre lines up {centred.Dy:+0;-0;0} px down - the measure cannot tell");
        Assert.True(found["5_MyRoom"].Fitted > 1.3, $"control: the PC's room fitted again lines up at {found["5_MyRoom"].Fitted:F3} - the measure cannot tell");
        Assert.True(Math.Abs(found["5_MyRoom"].Own - 1) <= 0.006, $"the PC's room is drawn at {1 / found["5_MyRoom"].Own:P1} of the game's size");
        Assert.True(found["14_Beach"].Fitted >= found["14_Beach"].Own + 0.006, "control: the Beach fitted again is no smaller - the measure cannot tell 1%");
        Assert.True(Math.Abs(found["14_Beach"].Own - 1) <= 0.005, $"the Beach is drawn at {1 / found["14_Beach"].Own:P1} of the game's size");
    }

    /// <summary>
    /// A game level is lit by its own lamps as the game lights it (1.9.2, <see cref="Rendering.PlaceLights"/>):
    /// each point light the level shows on arrival and that lights its layer, through the 2D renderer's light texture,
    /// and its glow. Measured against the game's own frames (the backdrops' room.png: the screen before the
    /// post-processing, levels at rest): the PC's room and the Beach drawn with their lights are as near the game's as
    /// the art alone gets where nothing lights it (the Temple), and without them far from it (the controls).
    /// </summary>
    [Fact]
    public void ALevelIsLitByItsOwnLightsAsTheGameLightsIt()
    {
        var found = new Dictionary<string, (double Lit, double Unlit, int Lights)>();
        foreach (var (level, backdrop) in new[] { ("5_MyRoom", "MyRoom"), ("14_Beach", "Beach"), ("69_Temple_Inside", "Temple") })
        {
            byte[] lit = null!, unlit = null!;
            int lights = 0;
            WindowHarness.Run(_ =>
            {
                PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited();
                var preview = PlaceExport.VanillaPreview(PlaceExport.VanillaLevel("vanilla:" + level, null, LevelArt), null, null, LevelArt);
                PlaceExport.Still(preview);
                lights = preview.PlaceLightsNow?.Lights.Count ?? 0;
                lit = preview.RenderStill(look: false, lights: true)!;
                unlit = preview.RenderStill(look: false, lights: false)!;
            });
            var (gw, gh, game) = Read(Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "Backdrops", backdrop, "room.png"));
            double Near(byte[] pic)
            {
                int w = PlaceExport.Width, k = w / gw, n = 0;
                for (int y = 0; y < gh; y++)
                    for (int x = 0; x < gw; x++)
                    {
                        int d = 0, g = (y * gw + x) * 4;
                        for (int c = 0; c < 3; c++)
                        {
                            int sum = 0;
                            for (int dy = 0; dy < k; dy++)
                                for (int dx = 0; dx < k; dx++) sum += pic[((y * k + dy) * w + x * k + dx) * 4 + c];
                            d += Math.Abs(sum / (k * k) - game[g + c]);
                        }
                        if (d <= 9) n++;
                    }
                return n / (double)(gw * gh);
            }
            found[level] = (Near(lit), Near(unlit), lights);
            _out.WriteLine($"{level} ({lights} lights): {found[level].Lit:P1} of the game's frame within 3 a channel lit, {found[level].Unlit:P1} unlit");
        }
        var temple = found["69_Temple_Inside"];
        Assert.Equal(0, temple.Lights);
        Assert.Equal(temple.Lit, temple.Unlit);
        foreach (var level in new[] { "5_MyRoom", "14_Beach" })
        {
            var f = found[level];
            Assert.True(f.Lights > 0, $"{level} has no lights to draw");
            Assert.True(f.Unlit < 0.4, $"control: {level} unlit is {f.Unlit:P1} the game's - its lights change nothing the check can see");
            Assert.True(f.Lit > temple.Lit - 0.06, $"{level} lit is {f.Lit:P1} the game's, the Temple's art alone {temple.Lit:P1}");
        }
    }

    /// <summary>
    /// What a level draws with the game's unlit material is left as it is by its lamps (1.9.2): 56 levels draw their
    /// own picture so, some lit by lamps of intensity 1, which only add their glow over it. Measured on Tasha's store
    /// (its picture unlit, a lamp of intensity 1 glowing at 0.2), in linear light, 8x8 block by block where the glow
    /// shows and nothing clips: a glow added over the picture is smooth, so what the lamps change does not follow the
    /// picture's own detail; a lamp lighting the picture multiplies it, so the change follows the detail - which is
    /// what the control, the same picture taken as lit, shows. The glow alone is no measure: Tasha's is not faint.
    /// Measured 2026-11-19: 4.0% of the detail, against 14.7% taken as lit (with the lit mask captured before its
    /// pictures were laid out - the scene itself, not its silhouettes - the lamps changed it nearly as much as lit).
    /// </summary>
    [Fact]
    public void WhatIsDrawnUnlitIsLeftAsItIsByTheLamps()
    {
        byte[] lit = null!, unlit = null!, wrong = null!;
        WindowHarness.Run(_ =>
        {
            PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited();
            var vm = PlaceExport.VanillaLevel("vanilla:79_TashaStore", null, LevelArt);
            Assert.True(vm.PreviewArtUnlit, "Tasha's store's own picture is not drawn unlit any more - the check needs another level");
            var preview = PlaceExport.VanillaPreview(vm, null, null, LevelArt);
            PlaceExport.Still(preview);
            Assert.NotNull(preview.PlaceLightsNow);
            lit = preview.RenderStill(look: false, lights: true)!;
            unlit = preview.RenderStill(look: false, lights: false)!;
            var control = PlaceExport.VanillaPreview(vm, null, null, LevelArt);
            control.LevelArtUnlit = false;
            PlaceExport.Still(control);
            wrong = control.RenderStill(look: false, lights: true)!;
        });
        static double Lin(byte v) { double c = v / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        static double Spread(double[] v) { double m = v.Average(); return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / v.Length); }
        int w = PlaceExport.Width, h = PlaceExport.Height;
        double followsRight = 0, followsWrong = 0, picture = 0;
        int blocks = 0;
        var basePx = new double[64]; var right = new double[64]; var taken = new double[64];
        for (int by = 0; by + 8 <= h; by += 8)
            for (int bx = 0; bx + 8 <= w; bx += 8)
            {
                bool clips = false;
                for (int k = 0; k < 64 && !clips; k++)
                {
                    int i = ((by + k / 8) * w + bx + k % 8) * 4 + 1;   // green
                    if (Math.Max(unlit[i], Math.Max(lit[i], wrong[i])) >= 250) clips = true;
                    basePx[k] = Lin(unlit[i]); right[k] = Lin(lit[i]) - basePx[k]; taken[k] = Lin(wrong[i]) - basePx[k];
                }
                // No glow to judge, or too little detail to stand above the picture's 8-bit steps.
                if (clips || right.Average() < 0.01 || Spread(basePx) < 0.03) continue;
                followsRight += Spread(right); followsWrong += Spread(taken); picture += Spread(basePx); blocks++;
            }
        double rightShare = followsRight / picture, wrongShare = followsWrong / picture;
        _out.WriteLine($"Tasha's store, {blocks} blocks: what its lamps change follows the picture's detail at {rightShare:P1} of it; with the picture taken as lit {wrongShare:P1}");
        Assert.True(blocks >= 200, $"only {blocks} blocks where the glow shows and the picture has detail - the check sees too little");
        Assert.True(wrongShare > 0.1, $"control: lit, what the lamps change follows the detail at only {wrongShare:P1} - the check cannot tell");
        Assert.True(rightShare < wrongShare / 3, $"what the lamps change over the unlit picture follows its detail at {rightShare:P1}, near the {wrongShare:P1} of a lit one");
    }

    /// <summary>
    /// A game level's video is drawn, as a still, where the game shows it (1.9.2): behind the level, in a camera-space
    /// canvas. The Inside Car's windows and the Subway Train's showed black. Measured against the control, the same
    /// level with no still: where the control is black (the windows), the picture is the video's; everywhere else it is
    /// the control's - the still is behind the car, not over it.
    /// </summary>
    [Fact]
    public void ALevelsVideoIsDrawnBehindItWhereTheGameShowsIt()
    {
        foreach (var level in new[] { "22_InsideCar", "95_JapaneseSubwaytrain" })
        {
            byte[] with = null!, without = null!;
            WindowHarness.Run(_ =>
            {
                PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited();
                var vm = PlaceExport.VanillaLevel("vanilla:" + level, null, LevelArt);
                Assert.NotNull(vm.PreviewVideoScreen);
                Assert.EndsWith("Video.PNG", vm.PreviewVideoStill);
                // The scene as drawn, without the lamps' glow or the look, which brighten the black windows too.
                var shown = PlaceExport.VanillaPreview(vm, null, null, LevelArt);
                PlaceExport.Still(shown);
                with = shown.RenderStill(look: false, lights: false)!;
                var control = PlaceExport.VanillaPreview(vm, null, null, LevelArt);
                control.LevelVideoStill = "";
                PlaceExport.Still(control);
                without = control.RenderStill(look: false, lights: false)!;
            });
            // Where the game shows the video: the catalog's screen (a camera-space canvas's Image, its centre and size in
            // the canvas's 1920x1080 units, y up), the still stretched over it.
            var screen = VanillaLevelCatalog.FindLevel(level)!.VideoScreen!;
            var (sw, sh, still) = Read(Path.Combine(LevelArt, level, screen.Still));
            double rw = screen.Width * screen.Scale, rh = screen.Height * screen.Scale;
            double left = 960 + screen.X - rw / 2, top = 540 - screen.Y - rh / 2;
            int w = PlaceExport.Width, h = PlaceExport.Height;
            long dark = 0, elsewhere = 0, changedElsewhere = 0;
            var drawn = new System.Collections.Generic.List<double>();
            var video = new System.Collections.Generic.List<double>();
            for (int y = 0; y < h; y += 2)
                for (int x = 0; x < w; x += 2)
                {
                    int i = (y * w + x) * 4;
                    bool black = Math.Max(without[i], Math.Max(without[i + 1], without[i + 2])) < 8;
                    if (black)
                    {
                        dark++;
                        int vx = (int)((x + 0.5 - left) / rw * sw), vy = (int)((y + 0.5 - top) / rh * sh);
                        if (vx < 0 || vy < 0 || vx >= sw || vy >= sh) continue;
                        drawn.Add(Luma(with, w, x, y));
                        video.Add(Luma(still, sw, vx, vy));
                    }
                    else
                    {
                        elsewhere++;
                        if (Distance(with, w, x, y, without, w, x, y) > 30) changedElsewhere++;
                    }
                }
            double md = drawn.Average(), mv = video.Average(), dv = 0, dd = 0, vv = 0;
            for (int k = 0; k < drawn.Count; k++) { dv += (drawn[k] - md) * (video[k] - mv); dd += (drawn[k] - md) * (drawn[k] - md); vv += (video[k] - mv) * (video[k] - mv); }
            double match = dv / Math.Sqrt(dd * vv + 1e-9);
            _out.WriteLine($"{level}: {dark} sampled points black without the video, {drawn.Count} of them inside its screen, where the picture "
                           + $"follows the video's frame at {match:F2}; {changedElsewhere} of the other {elsewhere} changed");
            Assert.True(dark > elsewhere / 20, $"control: {level} has next to nothing black without its video - the check cannot tell");
            Assert.True(drawn.Count > dark * 0.9, $"{level}: its black points are outside the video's screen ({drawn.Count} of {dark} inside)");
            Assert.True(match > 0.9, $"{level}: where the windows are, the picture is not the video's frame (it follows it at {match:F2})");
            Assert.True(changedElsewhere < elsewhere / 50, $"{level}: the video changes {changedElsewhere} points the level's own art covers - it is drawn over the level");
        }
    }

    /// <summary>
    /// The scale and shift (in the frame's pixels) at which a picture of the whole screen (<paramref name="pic"/>, BGRA)
    /// lines up best with the game's frame of it (<paramref name="frame"/>): the two pictures' edges, correlated. A
    /// scale of 1 is the place drawn at the game's size; above 1, drawn smaller.
    /// </summary>
    private static (double Scale, int Dx, int Dy, double Match) LinesUp(byte[] pic, int pw, int ph, byte[] frame, int fw, int fh)
    {
        static float[] Gray(byte[] px, int w, int h)
        {
            var g = new float[w * h];
            for (int i = 0; i < w * h; i++) g[i] = (px[i * 4] + px[i * 4 + 1] + px[i * 4 + 2]) / 3f;
            return g;
        }
        static float[] Halve(float[] a, int w, int h)
        {
            var o = new float[w / 2 * (h / 2)];
            for (int y = 0; y < h / 2; y++)
                for (int x = 0; x < w / 2; x++)
                    o[y * (w / 2) + x] = (a[2 * y * w + 2 * x] + a[2 * y * w + 2 * x + 1] + a[(2 * y + 1) * w + 2 * x] + a[(2 * y + 1) * w + 2 * x + 1]) / 4f;
            return o;
        }
        static float[] Edges(float[] a, int w, int h)
        {
            var e = new float[w * h];
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    float gx = a[y * w + x + 1] - a[y * w + x - 1], gy = a[(y + 1) * w + x] - a[(y - 1) * w + x];
                    e[y * w + x] = MathF.Sqrt(gx * gx + gy * gy);
                }
            return e;
        }
        static double Correlation(float[] a, float[] b)
        {
            double ma = a.Average(), mb = b.Average(), ab = 0, aa = 0, bb = 0;
            for (int i = 0; i < a.Length; i++) { double u = a[i] - ma, v = b[i] - mb; ab += u * v; aa += u * u; bb += v * v; }
            return ab / (Math.Sqrt(aa * bb) + 1e-9);
        }
        var picGray = Gray(pic, pw, ph);
        var frameGray = Gray(frame, fw, fh);
        // fw x fh as the frame sees it, the picture scaled by s about the screen's centre and shifted (dx, dy) frame px.
        double Score(float[] fe, int w, int h, double s, double dx, double dy)
        {
            var r = new float[w * h];
            double k = (double)pw / w / s;
            for (int y = 0; y < h; y++)
            {
                double sy = ph / 2.0 + (y + 0.5 - h / 2.0 - dy) * k - 0.5;
                int y0 = Math.Clamp((int)Math.Floor(sy), 0, ph - 2);
                float ty = (float)Math.Clamp(sy - y0, 0, 1);
                for (int x = 0; x < w; x++)
                {
                    double sx = pw / 2.0 + (x + 0.5 - w / 2.0 - dx) * k - 0.5;
                    int x0 = Math.Clamp((int)Math.Floor(sx), 0, pw - 2);
                    float tx = (float)Math.Clamp(sx - x0, 0, 1);
                    float top = picGray[y0 * pw + x0] * (1 - tx) + picGray[y0 * pw + x0 + 1] * tx;
                    float bottom = picGray[(y0 + 1) * pw + x0] * (1 - tx) + picGray[(y0 + 1) * pw + x0 + 1] * tx;
                    r[y * w + x] = top * (1 - ty) + bottom * ty;
                }
            }
            return Correlation(fe, Edges(r, w, h));
        }
        // Coarse at half the frame's size, then fine at its own.
        int hw = fw / 2, hh = fh / 2;
        var halfEdges = Edges(Halve(frameGray, fw, fh), hw, hh);
        var fullEdges = Edges(frameGray, fw, fh);
        double bestS = 1, bestM = double.MinValue;
        for (double s = 0.8; s <= 1.7; s += 0.02)
        {
            double m = Score(halfEdges, hw, hh, s, 0, 0);
            if (m > bestM) { bestM = m; bestS = s; }
        }
        // Scale and shift together around it: a shift found at one scale is taken up by a wrong one (the PC's room:
        // 1.018 against the 1.001 a joint search finds).
        int bestDx = 0, bestDy = 0;
        double coarseS = bestS;
        for (double s = coarseS - 0.03; s <= coarseS + 0.0301; s += 0.005)
            for (int dx = -24; dx <= 24; dx += 4)
                for (int dy = -24; dy <= 24; dy += 4)
                {
                    double m = Score(halfEdges, hw, hh, s, dx / 2.0, dy / 2.0);
                    if (m > bestM) { bestM = m; bestS = s; bestDx = dx; bestDy = dy; }
                }
        bestM = double.MinValue;
        double centreS = bestS; int centreDx = bestDx, centreDy = bestDy;
        for (double s = centreS - 0.008; s <= centreS + 0.008; s += 0.002)
            for (int dx = centreDx - 3; dx <= centreDx + 3; dx++)
                for (int dy = centreDy - 3; dy <= centreDy + 3; dy++)
                {
                    double m = Score(fullEdges, fw, fh, s, dx, dy);
                    if (m > bestM) { bestM = m; bestS = s; bestDx = dx; bestDy = dy; }
                }
        return (bestS, bestDx, bestDy, bestM);
    }

    /// <summary>
    /// A level's second child is one of its objects, drawn where it stands. The vanilla preview drew it as a back
    /// layer (the level's Secondary.PNG), centred and fitted to the level's frame: in 30 levels it is no backdrop, and
    /// the Dining Room's table, its second child and in front of the room, was drawn screen-sized behind it, so the
    /// picture had no table. Measured against the control, the same level without the table: the table changes the
    /// picture, and only in a band of it (the table is long: most of the screen's width, the room's lower third).
    /// The Bath has no art of its own (its room is its second child): it is drawn, where it came out black.
    /// </summary>
    [Fact]
    public void ALevelsSecondChildIsDrawnWhereItStands()
    {
        byte[] withTable = null!, noTable = null!, bath = null!;
        WindowHarness.Run(_ =>
        {
            PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited();
            const string token = "vanilla:121_DiningRoomHome";
            var vm = PlaceExport.VanillaLevel(token, null, LevelArt);
            var second = VanillaLevelCatalog.FindLevelByToken(token)!.Hierarchy!.Children[1];
            var table = Walk(vm.GameObjects).Single(n => ReferenceEquals(n.Model.Baseline, second));
            Assert.EndsWith("Secondary.PNG", table.Model.VanillaArtPath);
            withTable = PlaceExport.Still(PlaceExport.VanillaPreview(vm, null, null, LevelArt)).Pixels;
            table.Model.StartActive = false;
            noTable = PlaceExport.Still(PlaceExport.VanillaPreview(vm, null, null, LevelArt)).Pixels;
            var bathRoom = PlaceExport.VanillaLevel("vanilla:4_Bath", null, LevelArt);
            Assert.Empty(bathRoom.PreviewBaseSprite);
            bath = PlaceExport.Still(PlaceExport.VanillaPreview(bathRoom, null, null, LevelArt)).Pixels;
        });
        int w = PlaceExport.Width, h = PlaceExport.Height, n = 0, left = w, right = 0, top = h, bottom = 0;
        for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x += 2)
            {
                if (Distance(withTable, w, x, y, noTable, w, x, y) < 60) continue;
                n++;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        _out.WriteLine($"the table changes {n} sampled points, within x {left}-{right}, y {top}-{bottom}");
        Assert.True(n > 2000, "the Dining Room has no table");
        Assert.True(bottom - top < h / 2 && top > h / 3, "the table is drawn over the whole screen, not where it stands");

        var levels = Enumerable.Range(0, 400).Select(k => Luma(bath, w, (k * 97) % w, (k * 53) % h)).ToArray();
        double mean = levels.Average(), spread = Math.Sqrt(levels.Select(v => (v - mean) * (v - mean)).Average());
        _out.WriteLine($"4_Bath: mean {mean:F1}, spread {spread:F1}");
        Assert.True(spread > 15 && mean > 30, "the Bath came out empty");
    }

    [Fact]
    public void EachPlaceIsDrawnAsThePreviewDrawsItWithTheGamesLook()
    {
        string pack = Path.Combine(_dir, "pack"), outLook = Path.Combine(_dir, "out");
        WriteSquare(Path.Combine(pack, "Places", "front.png"), 2048, 1136);
        var model = PackRepository.CreateEmpty("look.pack");
        model.Places.Add(new PlaceDef { Key = "cave", InternalName = "Cave", DisplayName = "Cave", BaseSprite = "Places/front.png" });
        PackRepository.Save(model, pack);
        string cave = "pack:" + model.PackId + ".cave";
        PlaceExport.Manifest manifest = null!;
        byte[] plain = null!;
        WindowHarness.Run(_ =>
        {
            manifest = PlaceExport.Run(outLook, pack, _out.WriteLine, new[] { "vanilla:14_Beach", cave },
                                       Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "VanillaLevelArt"));
            // The control: the same place, the same preview, without the look.
            var vm = new ViewModel.PlaceViewModel(model.Places[0]);
            var preview = new View.Controls.PlacePreview { PackRoot = pack, BaseSprite = vm.BaseSprite, GameObjects = vm.GameObjects, Shaders = false };
            preview.Width = 1920; preview.Height = 1080;
            preview.Measure(new System.Windows.Size(1920, 1080));
            preview.Arrange(new System.Windows.Rect(0, 0, 1920, 1080));
            preview.UpdateLayout();
            WindowHarness.Pump();
            plain = preview.RenderStill(look: false)!;
        });

        Assert.Equal(new[] { "vanilla:14_Beach", cave }, manifest.Places.Select(p => p.Token));
        Assert.All(manifest.Places, p => Assert.True(p.Look, p.Token + " was drawn without the game's look"));
        var listed = JsonConvert.DeserializeObject<PlaceExport.Manifest>(File.ReadAllText(Path.Combine(outLook, "places.json")))!;
        Assert.Equal(manifest.Places.Select(p => p.File), listed.Places.Select(p => p.File));
        Assert.Equal("Beach", listed.Places[0].Name);

        var (w, h, look) = Read(Path.Combine(outLook, manifest.Places.Single(p => p.Token == cave).File));
        Assert.Equal((1920, 1080), (w, h));
        // Beside the square, where the control picture turns dark (found there, not worked out from the art's
        // scale): the bloom lights the dark.
        int cx = w / 2, cy = h / 2, edge = 0;
        while (cx + edge < w - 40 && Luma(plain, w, cx + edge, cy) > 100) edge++;
        Assert.True(edge > 10 && cx + edge < w - 40, "the square's edge is not where a square drawn at the centre has it");
        edge += 20;
        double glow = Luma(look, w, cx + edge, cy), dark = Luma(plain, w, cx + edge, cy);
        _out.WriteLine($"beside the square: {glow:F1} with the look, {dark:F1} without");
        Assert.True(Luma(look, w, cx, cy) > 200, "the square is not drawn");
        Assert.True(glow > dark + 4, "the look's bloom does not light the dark beside the square");
        Assert.True(Math.Abs(Luma(plain, w, cx, cy) - 255) < 2, "the control is not the art as it is");

        // The game's level: its own art, many colours, not an empty screen.
        var (bw, bh, beach) = Read(Path.Combine(outLook, manifest.Places[0].File));
        var levels = Enumerable.Range(0, 400).Select(k => Luma(beach, bw, (k * 97) % bw, (k * 53) % bh)).ToArray();
        double mean = levels.Average(), spread = Math.Sqrt(levels.Select(v => (v - mean) * (v - mean)).Average());
        _out.WriteLine($"14_Beach: mean {mean:F1}, spread {spread:F1}");
        Assert.True(spread > 15 && mean > 40, "the game's level came out empty or flat");

        // The command line asks for the same: the folder, the pack, the full-size art wherever it is written.
        Assert.True(PlaceExport.WasAsked(new[] { "--level-art", "art", PlaceExport.Argument, "out", "pack" }, out var f, out var p, out var a));
        Assert.Equal(("out", "pack", "art"), (f, p, a));
        Assert.True(PlaceExport.WasAsked(new[] { PlaceExport.Argument, "out" }, out f, out p, out a));
        Assert.Equal(("out", (string?)null, (string?)null), (f, p, a));
        Assert.False(PlaceExport.WasAsked(new[] { "--other" }, out _, out _, out _));
    }
}
#endif
