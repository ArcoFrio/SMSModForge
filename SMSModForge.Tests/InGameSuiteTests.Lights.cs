using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A pack place's lights (1.10.0, the author: lights placed, shaped, as many
/// as wanted, their strength and colour, "all doable via the preview window"):
/// what the place preview draws them as, held to the game.
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The place preview lights a pack place as the game does: the light place
    /// (flat grey, InGameSuitePacks.Looks) with the template level's light
    /// changed, a round light narrowed to a turned cone with an inner radius and
    /// a glow, a free shape and a picture light. In the game, the interface and
    /// the busts off, the post-processing off, time frozen: its frame, and its
    /// frame with those four lights off. In the editor, the place as the Places
    /// tab previews it, lit as the look lights it (without the post), at the
    /// game's screen. Where the game's lights change the picture by more than
    /// three levels, the preview is within three of the game's at 98% of the
    /// pixels. Controls: the game's frame with the lights off is not within three
    /// there; the unlit frame and the preview's agree away from the lights.
    /// </summary>
    [Fact]
    public void S11_The_place_preview_lights_a_pack_place_as_the_game_does()
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + InGameSuitePacks.Looks.Id + "." + InGameSuitePacks.Looks.LightPlaceKey;
        string ending = "_" + InGameSuitePacks.Looks.LightPlaceObject;
        var hidden = new List<string> { "9_MainCanvas" };
        var lightsOff = new List<string>();
        string on, off, level;
        try
        {
            Travel(game, token, ending);
            game.Ask("wait 2", 10);
            level = game.Ask("level").Split(' ').Last().Trim();
            string canvases = game.Ask("find-all *Canvas*");
            hidden.AddRange(canvases.Substring(canvases.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Contains("5_Levels/" + level + "/")));
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            game.Ask("postfx off");
            game.Ask("wait 0.3", 10);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            _out.WriteLine("   " + game.Ask("lights-list"));
            on = LookFolder(game.Ask("screen-look light-place-on", 15));
            // The template's light stays on in both: it reaches the whole place, so it is held where the others
            // do not reach (the rest of the screen, which it tints).
            foreach (var name in InGameSuitePacks.Looks.LightObjects.Where(n => n != TemplateLight.ObjectName))
            {
                string path = "5_Levels/" + level + "/" + name;
                _out.WriteLine("   " + game.Ask("set-active " + path + " false"));
                lightsOff.Add(path);
            }
            game.Ask("wait 0.3", 10);
            off = LookFolder(game.Ask("screen-look light-place-off", 15));
        }
        finally
        {
            game.Ask("freeze off");
            game.Ask("postfx on");
            foreach (var p in lightsOff) game.Ask("set-active " + p + " true");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }

        var (gw, gh, gameOn) = Png(Path.Combine(on, "screen.png"));
        var (_, _, gameOff) = Png(Path.Combine(off, "screen.png"));

        // The preview: the place as the Places tab draws it, at the game's screen, lit as the look lights it.
        string packFolder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "packs", InGameSuitePacks.Looks.Id);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        byte[]? still = null;
        WindowHarness.Run(_ =>
        {
            var pack = PackRepository.Load(packFolder);
            var vm = new PlaceViewModel(pack.Places.Single(p => p.Key == InGameSuitePacks.Looks.LightPlaceKey));
            var preview = new PlacePreview
            {
                PackRoot = packFolder, BaseSprite = vm.BaseSprite, SecondarySprite = vm.SecondarySprite,
                LevelArtOrder = vm.BaseSortingOrder, LevelArtSecondaryOrder = vm.SecondarySortingOrder,
                GameObjects = vm.GameObjects, Shaders = false, ParallaxPreview = false, AsTheGameShows = true,
                DimInactive = false, DimUnderInactiveParent = false,
                Width = 1920, Height = 1080,
            };
            preview.Measure(new Size(1920, 1080));
            preview.Arrange(new Rect(0, 0, 1920, 1080));
            preview.UpdateLayout();
            WindowHarness.Pump();
            still = preview.RenderStill(look: false, lights: true);
        });
        Assert.NotNull(still);
        int pw = 1920;
        // A game pixel is two of the preview's each way: their mean.
        int Mine(int x, int y, int c)
        {
            int s = 0;
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                    s += still![((y * 2 + dy) * pw + x * 2 + dx) * 4 + c];
            return (s + 2) / 4;
        }
        int Diff(byte[] a, int i, int x, int y)
            => Math.Max(Math.Abs(a[i] - Mine(x, y, 0)), Math.Max(Math.Abs(a[i + 1] - Mine(x, y, 1)), Math.Abs(a[i + 2] - Mine(x, y, 2))));

        int lit = 0, litNear = 0, litOffNear = 0, rest = 0, restNear = 0;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                int i = (y * gw + x) * 4;
                int change = Math.Max(Math.Abs(gameOn[i] - gameOff[i]), Math.Max(Math.Abs(gameOn[i + 1] - gameOff[i + 1]), Math.Abs(gameOn[i + 2] - gameOff[i + 2])));
                if (change > 3)
                {
                    lit++;
                    if (Diff(gameOn, i, x, y) <= 3) litNear++;
                    if (Diff(gameOff, i, x, y) <= 3) litOffNear++;
                }
                else
                {
                    rest++;
                    if (Diff(gameOn, i, x, y) <= 3) restNear++;
                }
            }
        double Share(int n, int of) => of == 0 ? 0 : (double)n / of;
        _out.WriteLine($"game {gw}x{gh}: lit by the pack's lights {lit} pixels - the preview within three {Share(litNear, lit):P2}, "
                       + $"the game's unlit frame within three of the preview {Share(litOffNear, lit):P2}; elsewhere {rest} - within three {Share(restNear, rest):P2}");
        Assert.True(lit > 10000, "the pack's lights lit too little of the screen to tell");   // 27,704 game pixels, 2026-11-19
        Assert.True(Share(litOffNear, lit) < 0.5, "control: the game's frame without the lights passes for the preview's with them");
        Assert.True(Share(restNear, rest) >= 0.98, "control: away from the lights the preview is not the game's place - the comparison is off");
        Assert.True(Share(litNear, lit) >= 0.98, $"the preview lights the place more than three levels off the game at {lit - litNear} of {lit} lit pixels");
    }

    /// <summary>
    /// A character standing in the place preview is lit as the game lights a bust
    /// there (1.10.0): the figure bust in the character place, by a light that
    /// lights the characters too, and by the same light lighting the place only
    /// (the bridge takes the characters' layer off it). In the game, the interface
    /// off, the post-processing on, time frozen: both frames. In the editor, the
    /// suite's pack opened, the character place picked, the figure outfit chosen
    /// to stand in it - the place preview's own backdrop of the place - and the
    /// bust drawn over it at the game's time and drawn height, as JigglePreview
    /// draws it, for the light on and off the characters, each at its frame's own
    /// time and bust height (bust-look, as S8). Each within three levels of the
    /// game's at 99% of the frame, and where lighting the characters changes the
    /// frame; there the preview without it is not (the control). In the place that
    /// keeps its template light, its shadow of the bust too: without it the preview
    /// is not the game's (the control).
    /// </summary>
    [Theory]
    [InlineData(InGameSuitePacks.Looks.CharacterPlaceKey, InGameSuitePacks.Looks.CharacterPlaceObject)]
    [InlineData(InGameSuitePacks.Looks.ShadowPlaceKey, InGameSuitePacks.Looks.ShadowPlaceObject)]
    [InlineData(InGameSuitePacks.Looks.NightPlaceKey, InGameSuitePacks.Looks.NightPlaceObject)]
    public void S13_A_character_standing_in_the_place_preview_is_lit_as_the_game_lights_a_bust_there(string placeKey, string placeObject)
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + InGameSuitePacks.Looks.Id + "." + placeKey;
        string root = "2_Bust_Manager/" + InGameSuitePacks.Looks.FigureBust;
        var hidden = new List<string> { "9_MainCanvas" };
        string saidOn, saidOff, lamp = "", meshOn = "", meshOff = "";
        try
        {
            Travel(game, token, "_" + placeObject);
            game.Ask("wait 2", 10);
            string level = game.Ask("level").Split(' ').Last().Trim();
            lamp = "5_Levels/" + level + "/" + InGameSuitePacks.Looks.CharacterLamp;
            string canvases = game.Ask("find-all *Canvas*");
            hidden.AddRange(canvases.Substring(canvases.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Contains("5_Levels/" + level + "/")));
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            _out.WriteLine("   " + game.Ask("lights-list"));
            game.Ask("set-active " + root + " true");
            game.Ask("wait 5", 15);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            // Each frame with the bust's own place and time in it (the bust breathes even with time frozen, so a
            // later capture would not be the one measured): bust-look takes both at once, as S8.
            saidOn = game.Ask("bust-look " + root + "/MBase1 char-on", 30);
            meshOn = game.Ask("shadow-mesh " + root + "/MBase1", 20);
            _out.WriteLine("   " + game.Ask("light " + lamp + " layers=Default"));
            game.Ask("wait 0.3", 10);
            saidOff = game.Ask("bust-look " + root + "/MBase1 char-off", 30);
            meshOff = game.Ask("shadow-mesh " + root + "/MBase1", 20);
        }
        finally
        {
            if (lamp.Length > 0) game.Ask("light " + lamp + " layers=Default,Bust");
            game.Ask("freeze off");
            game.Ask("set-active " + root + " false");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        var lookOn = LookOf(saidOn.Substring("char-on".Length).Trim());
        var lookOff = LookOf(saidOff.Substring("char-off".Length).Trim());

        // The editor: the place preview's own backdrop of the place, with the figure standing in it.
        string packFolder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "packs", InGameSuitePacks.Looks.Id);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        BackdropScene? sceneOn = null, sceneOff = null;
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(packFolder);
            ((System.Windows.Controls.TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.Single(pl => pl.Key == placeKey);
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            var box = (System.Windows.Controls.ComboBox)typeof(PlacePreview)
                .GetField("_standInBox", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(preview)!;
            box.SelectedItem = box.Items.Cast<object>().Single(i => i.GetType().GetProperty("Outfit")!.GetValue(i) is OutfitViewModel o && o.Key == "figure");
            Assert.True(WindowHarness.Until(() => preview.StandInBuilt > 0, TimeSpan.FromSeconds(30)), "the character's place was not made");
            sceneOn = preview.StandInScene;
            int built = preview.StandInBuilt;
            vm.SelectedPlace.GameObjects.Single(g => g.Name == InGameSuitePacks.Looks.CharacterLamp).Light!.LightsCharacters = false;
            Assert.True(WindowHarness.Until(() => preview.StandInBuilt > built, TimeSpan.FromSeconds(30)), "the character's place was not made again");
            sceneOff = preview.StandInScene;
        });
        Assert.True(sceneOn?.CharactersFrame != null, "the place's backdrop has no light on the characters");
        Assert.True(sceneOff != null && sceneOff.CharactersFrame == null, "the light off the characters still lights them");
        bool keepsTemplate = placeKey != InGameSuitePacks.Looks.CharacterPlaceKey;
        Assert.True((sceneOn!.Backdrop.Lamp != null) == keepsTemplate, $"the place's backdrop has a lamp: {sceneOn.Backdrop.Lamp != null}, its template light kept: {keepsTemplate}");

        int size = InGameSuitePacks.Looks.Size;
        const int N = BackdropRender.FrameSize;
        var art = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var c = InGameSuitePacks.Looks.FigureAt(x, y);
                int i = (y * size + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
            }
        var mask = new byte[size * size * 4];
        // The preview's frame for one of the game's, as JigglePreview makes it over the place's backdrop: at that
        // frame's time and the bust's drawn height, with a lamp the bust's shadow from it as the game casts it (its
        // shadow mesh's place, as S8).
        byte[] Frame(BackdropScene scene, Look look, string meshReply, bool castShadow = true)
        {
            int bustBottom = (int)Math.Round(look.Corners("drawnCorners")[0].y);
            float time = look.F("time");
            var p = new JiggleParams
            {
                Speed = look.F("mat_JiggleSpeed"), Strength = look.F("mat_JiggleStrength"), Frequency = look.F("mat_JiggleFrequency"),
                NoiseScale = look.F("mat_NoiseScale"), NoiseSpeed = look.F("mat_NoiseSpeed"), NoiseStrength = look.F("mat_NoiseStrength"),
            };
            var gi = scene.Prepare();
            GamePost.Target target;
            float[]? shadow = null;
            var lampNow = scene.Lamp;
            if (lampNow != null)
            {
                var origin = meshReply.Split("; ").First(q => q.StartsWith("origin=", StringComparison.Ordinal)).Substring(7).Split(',')
                                      .Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                shadow = new float[lampNow.Shadow.W * lampNow.Shadow.H];
                if (castShadow) lampNow.Shadow.Draw(new BustShadow.Caster(art, size, size, 100f), origin[0], origin[1], 2f, 1f, shadow);
                target = new GamePost.Target(scene.Backdrop.ScreenWidth, scene.Backdrop.ScreenHeight);
                var background = new float[scene.Backdrop.BackgroundWidth * scene.Backdrop.BackgroundHeight * 4];
                lampNow.Room(shadow, target, background, scene.Backdrop.BackgroundWidth, scene.Backdrop.BackgroundHeight);
                gi.UseBackground(background);
            }
            else target = BackdropRender.Scene(scene);
            var body = new float[N * N * 4];
            BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), time, gi, new FakeGi.Art(art, size, size, gi.Reach), scene.Left, scene.FrameBottom, bustBottom,
                                body, true, null, 0f, true, scene.Decode);
            var coverage = new float[N * N];
            BackdropRender.Layer(target, scene, body, coverage);
            if (lampNow != null)
            {
                lampNow.Glow(target, shadow, BackdropRender.Covered(scene, coverage));
                BackdropRender.OthersOver(target, scene);
            }
            else BackdropRender.Lamps(target, scene, coverage);
            var output = new byte[N * N * 4];
            BackdropRender.Present(target, scene, BackdropRender.PostParts.All, output);
            return output;
        }
        var previewOn = Frame(sceneOn, lookOn, meshOn);
        var previewOff = Frame(sceneOff!, lookOff, meshOff);
        // The same light lighting the place only, at the moment the game's frame with it on the characters was taken:
        // what the control compares with that frame.
        var previewOffThen = Frame(sceneOff!, lookOn, meshOn);
        SaveFrame(previewOn, Path.Combine(lookOn.Folder, "preview.png"));
        SaveFrame(previewOff, Path.Combine(lookOff.Folder, "preview.png"));

        int Diff(byte[] f, Look look, int x, int y)
        {
            int sx = sceneOn.Left + x, sy = sceneOn.FrameBottom + (N - 1 - y);
            var g = look.At(sx, sy);
            int o = (y * N + x) * 4;
            return Math.Max(Math.Abs(f[o] - g.b), Math.Max(Math.Abs(f[o + 1] - g.g), Math.Abs(f[o + 2] - g.r)));
        }
        int all = 0, onNear = 0, offNear = 0, lit = 0, litOnNear = 0, litOffNear = 0;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                all++;
                int dOn = Diff(previewOn, lookOn, x, y);
                if (dOn <= 3) onNear++;
                if (Diff(previewOff, lookOff, x, y) <= 3) offNear++;
                // What lighting the characters changes, at the moment of the frame with it.
                int o = (y * N + x) * 4;
                if (Math.Max(Math.Abs(previewOn[o] - previewOffThen[o]), Math.Max(Math.Abs(previewOn[o + 1] - previewOffThen[o + 1]), Math.Abs(previewOn[o + 2] - previewOffThen[o + 2]))) > 3)
                {
                    lit++;
                    if (dOn <= 3) litOnNear++;
                    if (Diff(previewOffThen, lookOn, x, y) <= 3) litOffNear++;
                }
            }
        double Share(int n, int of) => of == 0 ? 0 : (double)n / of;
        _out.WriteLine($"{placeObject}: the frame ({all} pixels): lighting the characters, the preview within three {Share(onNear, all):P2}; lighting the place only {Share(offNear, all):P2}; "
                       + $"where lighting the characters changes the frame ({lit}): the preview's with it {Share(litOnNear, lit):P2}, without it {Share(litOffNear, lit):P2}");
        Assert.True(lit > 2000, "lighting the characters changed too little of the frame to tell");
        Assert.True(Share(litOffNear, lit) < 0.5, "control: the preview without the light on the characters passes for the game's with it");
        Assert.True(Share(onNear, all) >= 0.99, $"lighting the characters, the preview is more than three levels off the game at {all - onNear} of {all}");
        Assert.True(Share(offNear, all) >= 0.99, $"lighting the place only, the preview is more than three levels off the game at {all - offNear} of {all}");
        Assert.True(Share(litOnNear, lit) >= 0.99, $"where it lights the character, the preview is more than three levels off at {lit - litOnNear} of {lit}");
        if (keepsTemplate)
        {
            // Control: without the bust's shadow from the template light, the preview is not the game's.
            var unshadowed = Frame(sceneOn, lookOn, meshOn, castShadow: false);
            int near = 0;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    if (Diff(unshadowed, lookOn, x, y) <= 3) near++;
            _out.WriteLine($"without the bust's shadow from the template light: within three {Share(near, all):P2}");
            Assert.True(Share(near, all) < Share(onNear, all) - 0.05, "control: without the bust's shadow the preview is as close - the check does not see it");
        }
    }

    /// <summary>
    /// A place's own base light (1.10.0, darkening a place): the night place - its
    /// darkness object, its template light, a lamp - as the place preview draws it
    /// against the game's frame, as S11 (the interface off, the post-processing
    /// off, time frozen): within three levels at 99% of the screen; control: the
    /// game's frame with the darkness object switched off is not. While the
    /// darkness is on, the scene's global light lets go of the place's layer and
    /// keeps the characters'; switched off, and once the player is somewhere else,
    /// it has it back (the control the other way).
    /// </summary>
    [Fact]
    public void S14_A_place_darkened_by_its_own_base_light_is_drawn_as_the_game_draws_it_and_gives_the_light_back()
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + InGameSuitePacks.Looks.Id + "." + InGameSuitePacks.Looks.NightPlaceKey;
        var hidden = new List<string> { "9_MainCanvas" };
        string darkness = "", on, off, listDark, listDay, listAway;
        string GlobalLayers(string list) => list.Split(" | ").Select(l => l.Trim()).FirstOrDefault(l => l.Contains("Global") && !l.Contains("5_Levels/")) is { } g
            ? g.Substring(g.IndexOf(" layers ", StringComparison.Ordinal) + 8).Split(' ')[0] : "(none)";
        try
        {
            Travel(game, token, "_" + InGameSuitePacks.Looks.NightPlaceObject);
            game.Ask("wait 2", 10);
            string level = game.Ask("level").Split(' ').Last().Trim();
            darkness = "5_Levels/" + level + "/" + InGameSuitePacks.Looks.NightDarkness;
            string canvases = game.Ask("find-all *Canvas*");
            hidden.AddRange(canvases.Substring(canvases.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Contains("5_Levels/" + level + "/")));
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            game.Ask("postfx off");
            game.Ask("wait 0.3", 10);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            listDark = game.Ask("lights-list");
            on = LookFolder(game.Ask("screen-look night-on", 15));
            game.Ask("set-active " + darkness + " false");
            game.Ask("wait 0.3", 10);
            listDay = game.Ask("lights-list");
            off = LookFolder(game.Ask("screen-look night-off", 15));
            game.Ask("set-active " + darkness + " true");
        }
        finally
        {
            game.Ask("freeze off");
            game.Ask("postfx on");
            if (darkness.Length > 0) game.Ask("set-active " + darkness + " true");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        listAway = game.Ask("lights-list");
        _out.WriteLine("the scene's global light lights: at night " + GlobalLayers(listDark) + "; the darkness off " + GlobalLayers(listDay)
                       + "; back in the PC's room " + GlobalLayers(listAway));
        Assert.DoesNotContain("Default", GlobalLayers(listDark).Split(','));
        Assert.Contains("Bust", GlobalLayers(listDark).Split(','));
        Assert.Contains("Default", GlobalLayers(listDay).Split(','));     // control: given back as it goes off
        Assert.Contains("Default", GlobalLayers(listAway).Split(','));

        var (gw, gh, gameOn) = Png(Path.Combine(on, "screen.png"));
        var (_, _, gameOff) = Png(Path.Combine(off, "screen.png"));
        string packFolder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "packs", InGameSuitePacks.Looks.Id);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        byte[]? still = null;
        WindowHarness.Run(_ =>
        {
            var pack = PackRepository.Load(packFolder);
            var vm = new PlaceViewModel(pack.Places.Single(p => p.Key == InGameSuitePacks.Looks.NightPlaceKey));
            var preview = new PlacePreview
            {
                PackRoot = packFolder, BaseSprite = vm.BaseSprite, SecondarySprite = vm.SecondarySprite,
                LevelArtOrder = vm.BaseSortingOrder, LevelArtSecondaryOrder = vm.SecondarySortingOrder,
                GameObjects = vm.GameObjects, Shaders = false, ParallaxPreview = false, AsTheGameShows = true,
                DimInactive = false, DimUnderInactiveParent = false, Width = 1920, Height = 1080,
            };
            preview.Measure(new Size(1920, 1080));
            preview.Arrange(new Rect(0, 0, 1920, 1080));
            preview.UpdateLayout();
            WindowHarness.Pump();
            still = preview.RenderStill(look: false, lights: true);
        });
        Assert.NotNull(still);
        int Mine(int x, int y, int c)
        {
            int s = 0;
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                    s += still![((y * 2 + dy) * 1920 + x * 2 + dx) * 4 + c];
            return (s + 2) / 4;
        }
        int near = 0, nearDay = 0, all = gw * gh;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                int i = (y * gw + x) * 4;
                int D(byte[] g) => Math.Max(Math.Abs(g[i] - Mine(x, y, 0)), Math.Max(Math.Abs(g[i + 1] - Mine(x, y, 1)), Math.Abs(g[i + 2] - Mine(x, y, 2))));
                if (D(gameOn) <= 3) near++;
                if (D(gameOff) <= 3) nearDay++;
            }
        double Share(int n, int of) => of == 0 ? 0 : (double)n / of;
        _out.WriteLine($"the night place ({all} game pixels): the preview within three of the game's {Share(near, all):P2}; of the game's with the darkness off {Share(nearDay, all):P2}");
        Assert.True(Share(nearDay, all) < 0.5, "control: the game's place without its darkness passes for the preview's with it");
        Assert.True(Share(near, all) >= 0.99, $"the preview of the dark place is more than three levels off the game at {all - near} of {all}");
    }

    /// <summary>
    /// A light of the game's that a pack removes is off in the game: the dark
    /// place's template light (removed) is not among the lights the game draws
    /// there, while the light place's (changed, not removed) is - the control.
    /// </summary>
    [Fact]
    public void S12_A_removed_light_is_off_in_the_game()
    {
        if (Off) return;
        var game = _s.InGame();
        string LightsAt(string key, string obj)
        {
            Travel(game, "pack:" + InGameSuitePacks.Looks.Id + "." + key, "_" + obj);
            game.Ask("wait 1", 10);
            string level = game.Ask("level").Split(' ').Last().Trim();
            string said = game.Ask("lights-list");
            return string.Join("\n", said.Split(" | ").Where(l => l.Contains("5_Levels/" + level + "/")));
        }
        string lit, dark;
        try
        {
            lit = LightsAt(InGameSuitePacks.Looks.LightPlaceKey, InGameSuitePacks.Looks.LightPlaceObject);
            dark = LightsAt(InGameSuitePacks.Looks.DarkPlaceKey, InGameSuitePacks.Looks.DarkPlaceObject);
        }
        finally { Travel(game, "vanilla:5_MyRoom", "5_MyRoom"); }
        _out.WriteLine("the light place's lights:\n" + lit);
        _out.WriteLine("the dark place's lights:\n" + (dark.Length == 0 ? "(none)" : dark));
        Assert.Contains("/" + TemplateLight.ObjectName + " ", lit);   // control: kept where it is not removed
        Assert.DoesNotContain("/" + TemplateLight.ObjectName + " ", dark);
    }
}
