using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The level backdrops shipped beside the PC's room (1.9.1, the author: "if
/// we can look through all levels and find the 4 most diverse and interesting
/// ones to show as backdrops as well alongside the player room"; "One of the
/// levels has to be the beach").
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>Each level backdrop: the folder it ships in (Resources/Backdrops) and the level.
    /// Chosen (2026-11-16) by numbers from a survey of every level, never by looking: against the
    /// PC's room (dim, blue, a warm lamp) and the beach (asked for), the most saturated green-family
    /// art (Forest), the brightest art (the Villa inside, warm), and a saturated rose one unlike
    /// the others in theme (the Temple inside) - all lit on the bust only by the global light,
    /// which is what a level backdrop models exactly (a level whose lights reach the Bust layer,
    /// the Club or the Casino, would need the bust layer's light texture). A character the place
    /// shows is switched off for the captures (<see cref="OnBusts"/>).
    /// The third column is the lamp a bust's shadow is cast from, as the PC's room's: the Beach's
    /// faint light sits in the middle of the screen, behind the bust, and casts shadows, so a bust
    /// there darkens nearly the whole screen (measured 2026-11-16: 99.6% of it). The Villa's casts
    /// from far above the screen, with no glow: S8 holds it without.
    /// The rose one was the Haunted House first; its light, just left of the bust, faint but with a
    /// glow five times that and soft shadows, left 1-1.5% of a bust's frame unlike the game even as
    /// a lamp (S8, 2026-11-16) - the Temple, rose too and lit by nothing of its own, took its place.</summary>
    public static readonly TheoryData<string, string, string> LevelBackdrops = new()
    {
        { "Beach", "14_Beach", "5_Levels/14_Beach/Light 2D" },
        { "Forest", "16_Forest", "" },
        { "Villa", "71_Villa_Inside", "" },
        { "Temple", "69_Temple_Inside", "" },
    };

    /// <summary>The busts the game shows now: the paths of 2_Bust_Manager's children that are on.</summary>
    private static List<string> OnBusts(GameRun game)
    {
        string said = game.Ask("children 2_Bust_Manager");
        string list = said.Substring(said.IndexOf(':') + 1).Trim();
        return list == "(none)" ? new List<string>()
                                : list.Split(" | ", StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
    }

    /// <summary>
    /// A shipped level backdrop is the level the game draws behind a bust: as
    /// S4 holds the PC's room. In the level (travelled to as a player goes),
    /// the interface and the level's own buttons off, the post-processing off:
    /// its frame (8-bit and HDR), the background the fake GI reads, the level
    /// with its lights off, its lights, its post-processing's settings and
    /// grading table, and what its lights do to a bust - two flat grey busts,
    /// drawn = grey x multiply + add per screen pixel. Written to the looks
    /// folder (level-&lt;name&gt;-*); Tools/Game/backdrop-from-game.py makes the
    /// shipped copy from it. The shipped copy is held to it: the still pixels
    /// within a level, the lights' add within 0.004 where they only add.
    /// Controls: the level unlit does not pass for it, nor does the PC's room.
    /// The game goes back to the PC's room after.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelBackdrops))]
    public void S7_A_shipped_level_backdrop_is_the_level_the_game_draws_behind_a_bust(string name, string level, string lamp)
    {
        if (Off) return;
        var game = _s.InGame();
        string prefix = "level-" + name.ToLowerInvariant() + "-";
        string room, later, unlit, dark = "", light = "", neutral = "";
        var laters = new List<string>();
        var hidden = new List<string> { "9_MainCanvas" };
        try
        {
            Travel(game, "vanilla:" + level, level);
            game.Ask("wait 4", 20);                 // past the arrival's fade
            // The level's own buttons (drawn over busts) and the interface.
            string canvases = game.Ask("find-all *Canvas*");
            hidden.AddRange(canvases.Substring(canvases.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Contains("5_Levels/" + level + "/")));
            // A character the place shows (one who lives there, or the arrival's
            // speaker): off, or the room is taken with them in it and the check's
            // bust is moved aside for them (Sauna, Temple, Sofia's home, the
            // Haunted House, 2026-11-16).
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            game.Ask("wait 0.3", 10);
            game.Ask("rt-save _InternalGradingLut post-lut.bin", 20);
            game.Ask("material-dump Hidden/Kronnect/Beautify", 20);
            File.Copy(Path.Combine(game.BridgeFolder, "material-dump.txt"), Path.Combine(game.BridgeFolder, "post-beautify.txt"), true);
            game.Ask("material-dump Hidden/Universal Render Pipeline/UberPost", 20);
            File.Copy(Path.Combine(game.BridgeFolder, "material-dump.txt"), Path.Combine(game.BridgeFolder, "post-uberpost.txt"), true);
            game.Ask("postfx off");
            void Hdr(string folder)
            {
                string at = Path.GetRelativePath(game.BridgeFolder, folder);
                game.Ask("rt-save _CameraTarget " + Path.Combine(at, "frame.bin"), 20);
                game.Ask("rt-save _CameraSortingLayerTexture " + Path.Combine(at, "background.bin"), 20);
            }
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            room = LookFolder(game.Ask("screen-look " + prefix + "room", 15));
            Hdr(room);
            foreach (var f in new[] { "post-lut.bin", "post-beautify.txt", "post-uberpost.txt" })
                File.Copy(Path.Combine(game.BridgeFolder, f), Path.Combine(room, f), true);
            File.WriteAllText(Path.Combine(room, "lights-list.txt"), game.Ask("lights-list"));
            File.WriteAllText(Path.Combine(room, "hidden.txt"), string.Join(Environment.NewLine, hidden));
            game.Ask("texture-save _FalloffLookup falloff.bin", 20);
            File.Copy(Path.Combine(game.BridgeFolder, "falloff.bin"), Path.Combine(room, "falloff.bin"), true);
            // Later, three times over five seconds: what moves by itself - the
            // beach's water moves slowly enough that one look 1.3 s on missed some.
            foreach (var (wait, n) in new[] { (1.3, ""), (1.3, "-2"), (2.6, "-3") })
            {
                game.Ask("freeze off");
                game.Ask("wait " + wait.ToString(CultureInfo.InvariantCulture), 10);
                game.Ask("freeze on");
                game.Ask("wait 0.3", 10);
                laters.Add(LookFolder(game.Ask("screen-look " + prefix + "room-later" + n, 15)));
            }
            later = laters[0];
            neutral = game.Ask("lights neutral");
            _out.WriteLine("   " + neutral);
            game.Ask("wait 0.3", 10);
            unlit = LookFolder(game.Ask("screen-look " + prefix + "unlit", 15));
            Hdr(unlit);
            game.Ask("lights restore");
            if (lamp.Length > 0)
            {
                // A lamp a bust's shadow is cast from, as S4 takes the PC's room's: the
                // room before its glow is added, and the numbers it and its shadow are
                // drawn with (GameLamp).
                game.Ask("set-field Light2D m_LightVolumeEnabled false " + lamp);
                game.Ask("wait 0.3", 10);
                Hdr(LookFolder(game.Ask("screen-look " + prefix + "noglow", 15)));
                game.Ask("set-field Light2D m_LightVolumeEnabled true " + lamp);
                File.WriteAllText(Path.Combine(room, "shadow-light.txt"), game.Ask("shadow-light " + lamp));
                File.WriteAllText(Path.Combine(room, "lamp-fields.txt"), game.Ask("fields Light2D " + lamp));
                game.Ask("asset-fields Renderer2DData", 20);
                File.Copy(Path.Combine(game.BridgeFolder, "asset-fields.txt"), Path.Combine(room, "renderer2d.txt"), true);
            }
            game.Ask("freeze off");
            game.Ask("fake-gi off");
            foreach (var (key, bust, _) in InGameSuitePacks.Looks.Greys)
            {
                string root = "2_Bust_Manager/" + bust;
                game.Ask("set-active " + root + " true");
                game.Ask("wait 5", 15);
                game.Ask("freeze on");
                game.Ask("wait 0.3", 10);
                try
                {
                    string said = game.Ask("bust-look " + root + "/MBase1 " + prefix + key, 30);
                    string folder = said.Substring((prefix + key).Length).Trim();
                    if (key == "grey-dark") dark = folder; else light = folder;
                }
                finally
                {
                    game.Ask("freeze off");
                    game.Ask("set-active " + root + " false");
                }
            }
            string manager = game.Ask("fields SpriteManager 2_Bust_Manager");
            string anchor = game.Ask("find Screen_Center");
            File.WriteAllText(Path.Combine(room, "bust-manager.txt"), manager + Environment.NewLine + anchor);
        }
        finally
        {
            game.Ask("fake-gi on");
            game.Ask("postfx on");
            game.Ask("freeze off");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        _out.WriteLine($"{name} ({level}): {room}; hidden {string.Join(", ", hidden)}");
        foreach (var line in File.ReadAllLines(Path.Combine(dark, "info.txt")).Where(l => l.StartsWith("light="))) _out.WriteLine("   bust " + line);

        // The lights on a bust, per screen pixel, from the two greys.
        var lookDark = LookOf(dark);
        var lookLight = LookOf(light);
        float g1 = Srgb.ToLinear(InGameSuitePacks.Looks.Greys[0].Grey), g2 = Srgb.ToLinear(InGameSuitePacks.Looks.Greys[1].Grey);
        var cd = lookDark.Corners("drawnCorners"); var cl = lookLight.Corners("drawnCorners");
        // Alone, a bust stands where it does in the PC's room; beside another, it is moved aside.
        int aloneLeft = PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom")).BustLeft;
        Assert.True(Math.Abs(cd[0].x - aloneLeft) < 2, $"the check's bust stands at x {cd[0].x}, not {aloneLeft} where it stands alone - another bust is shown in {level}");
        int xa = (int)Math.Ceiling(Math.Max(cd[0].x, cl[0].x) - 0.5f), xb = (int)Math.Floor(Math.Min(cd[1].x, cl[1].x) - 0.5f) + 1;
        int ya = Math.Max(0, (int)Math.Ceiling(Math.Max(cd[0].y, cl[0].y) - 0.5f)), yb = Math.Min(lookDark.H, (int)Math.Floor(Math.Min(cd[2].y, cl[2].y) - 0.5f) + 1);
        int mw = xb - xa, mh = yb - ya;
        var map = new float[mw * mh * 6];
        int far = 0;
        double mulSum = 0;
        for (int y = ya; y < yb; y++)
            for (int x = xa; x < xb; x++)
            {
                var d1 = lookDark.At(x, y); var d2 = lookLight.At(x, y);
                int i = ((y - ya) * mw + (x - xa)) * 6;
                bool off = false;
                foreach (var (c, v1, v2) in new[] { (0, d1.r, d2.r), (1, d1.g, d2.g), (2, d1.b, d2.b) })
                {
                    float l1 = Srgb.ToLinear(v1), l2 = Srgb.ToLinear(v2);
                    float m = (l2 - l1) / (g2 - g1);
                    map[i + c] = m; map[i + 3 + c] = l1 - m * g1;
                    mulSum += m;
                    if (Math.Abs(m - 1) > 0.05f) off = true;
                }
                if (off) far++;
            }
        File.WriteAllBytes(Path.Combine(room, "lights.bin"), map.SelectMany(BitConverter.GetBytes).ToArray());
        File.WriteAllLines(Path.Combine(room, "lights.txt"), new[] { $"region={xa},{ya},{xb},{yb}  (screen pixels from the bottom left, both greys over it)" });
        _out.WriteLine($"lights over {mw}x{mh} px: multiply {mulSum / (mw * mh * 3):0.000} on average, {far} pixels multiplied by something other than 1");
        Assert.True(far < mw * mh / 100, $"{name}'s lights multiply a bust at {far} of {mw * mh} pixels - the backdrop takes them only as an add");

        // The shipped copy, held to what the game just drew.
        string shipped = Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", name);
        string refresh = "python Tools/Game/backdrop-from-game.py \"" + room + "\" " + name;
        Assert.True(File.Exists(Path.Combine(shipped, "backdrop.json")), "no backdrop shipped - make it: " + refresh);
        var (sw, sh, shippedRoom) = Png(Path.Combine(shipped, "room.png"));
        var (rw, rh, drawnRoom) = Png(Path.Combine(room, "screen.png"));
        var (_, _, unlitRoom) = Png(Path.Combine(unlit, "screen.png"));
        var laterRooms = laters.Select(l => Png(Path.Combine(l, "screen.png")).Item3).ToList();
        var (_, _, pcRoom) = Png(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom", "room.png"));
        Assert.True(sw == rw && sh == rh, $"the shipped {name} is {sw}x{sh}, the game's screen {rw}x{rh}");
        static bool Near(byte[] a, byte[] b, int i) => Math.Abs(a[i] - b[i]) <= 1 && Math.Abs(a[i + 1] - b[i + 1]) <= 1 && Math.Abs(a[i + 2] - b[i + 2]) <= 1;
        // Still: the same in every look over the five seconds.
        var still = new bool[drawnRoom.Length / 4];
        int moving = 0;
        for (int i = 0; i < drawnRoom.Length; i += 4)
        {
            still[i / 4] = laterRooms.All(l => Near(drawnRoom, l, i));
            if (!still[i / 4]) moving++;
        }
        double Same(byte[] a, byte[] b)
        {
            int n = 0, of = 0;
            for (int i = 0; i < a.Length; i += 4)
            {
                if (!still[i / 4]) continue;
                of++;
                if (Near(a, b, i)) n++;
            }
            return of == 0 ? 0 : n / (double)of;
        }
        double same = Same(shippedRoom, drawnRoom), unlitControl = Same(shippedRoom, unlitRoom), otherControl = Same(pcRoom, drawnRoom);
        _out.WriteLine($"{moving} of {drawnRoom.Length / 4} pixels move by themselves (left out); the shipped {name}: {same:P2} of the still pixels within a level; "
                       + $"unlit {unlitControl:P2}; the PC's room {otherControl:P2}");
        Assert.True(otherControl < 0.5, "control: the PC's room passes for " + name);
        // A place lit by the global light alone, white at 1, has nothing to put out: the
        // unlit control holds only where "lights neutral" changed something (the Forest).
        bool lightsToPutOut = !neutral.Contains(": 0 off") || Same(unlitRoom, drawnRoom) < 0.999;
        if (lightsToPutOut)
            Assert.True(unlitControl < 0.97, $"control: {name} with its lights off passes for the shipped one - the check cannot tell");
        else _out.WriteLine($"   {name} has no light but the global white at 1: the unlit control does not apply");
        // 99%: what moves slower than five seconds tell (the beach's water: 99.75% of
        // the rest alike, the Haunted House's: 99.30%, 2026-11-16) is not a change; new
        // art or light in a place differs far more.
        Assert.True(same >= 0.99, $"the shipped {name} is not the game's any more - refresh it: " + refresh);

        var backdrop = PreviewBackdrop.Load(shipped);
        Assert.True((backdrop.Lamp != null) == (lamp.Length > 0),
                    lamp.Length > 0 ? $"the shipped {name} has no lamp - refresh it: " + refresh : $"the shipped {name} has a lamp the level's list does not name");
        int near = 0, all = 0;
        double worst = 0;
        for (int y = ya; y < yb; y++)
            for (int x = xa; x < xb; x++)
            {
                int i = ((y - ya) * mw + (x - xa)) * 6;
                if (Math.Abs(map[i] - 1) > 0.05f || Math.Abs(map[i + 1] - 1) > 0.05f || Math.Abs(map[i + 2] - 1) > 0.05f) continue;
                var (ar, ag, ab) = backdrop.LightAt(x + 0.5f, y + 0.5f);
                double d = Math.Max(Math.Abs(ar - map[i + 3]), Math.Max(Math.Abs(ag - map[i + 4]), Math.Abs(ab - map[i + 5])));
                worst = Math.Max(worst, d);
                all++;
                if (d <= 0.004) near++;
            }
        _out.WriteLine($"the shipped lights: {near} of {all} pixels within 0.004 of the game's add (worst {worst:0.0000})");
        Assert.True(near >= all * 0.99, $"the shipped lights of {name} are not the game's any more - refresh them: " + refresh);
    }

    /// <summary>
    /// The bust preview over a level backdrop draws what the game draws there,
    /// as S3 holds it over the PC's room: the coded busts shown in the level -
    /// the figure (a person's shape: its edge glow falls on the level), the
    /// still and the noise outfits - with the post-processing on and the
    /// interface off, against the preview's frame for the same outfit at the
    /// game's frozen time and the bust's drawn height, the processor's and the
    /// graphics card's (the test copy's x1). The level's moving parts are left
    /// out. Control: without its fake GI the preview misses the bust's edge.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelBackdrops))]
    public void S8_The_bust_preview_over_a_level_backdrop_draws_what_the_game_draws_there(string name, string level, string lamp)
    {
        if (Off) return;
        var game = _s.InGame();
        var backdrop = PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", name));
        var scene = new BackdropScene(backdrop);
        var gi = scene.Prepare();
        Assert.True((scene.Lamp != null) == (lamp.Length > 0), $"{name}'s lamp: shipped {scene.Lamp != null}, listed {lamp.Length > 0}");
        int size = InGameSuitePacks.Looks.Size;
        const int N = BackdropRender.FrameSize;
        // The preview's room alone: where the game's room now is not it (the
        // level's own movement, at another moment than the shipped capture's),
        // what the bust adds cannot be told apart.
        var roomAlone = new byte[N * N * 4];
        if (scene.Lamp != null)
        {
            var t = new GamePost.Target(backdrop.ScreenWidth, backdrop.ScreenHeight);
            scene.Lamp.Room(new float[scene.Lamp.Shadow.W * scene.Lamp.Shadow.H], t, new float[backdrop.BackgroundWidth * backdrop.BackgroundHeight * 4],
                            backdrop.BackgroundWidth, backdrop.BackgroundHeight);
            scene.Lamp.Glow(t, new float[scene.Lamp.Shadow.W * scene.Lamp.Shadow.H], BackdropRender.Covered(scene, new float[N * N]));
            BackdropRender.Present(t, scene, BackdropRender.PostParts.All, roomAlone);
        }
        else BackdropRender.Present(BackdropRender.Scene(scene), scene, BackdropRender.PostParts.All, roomAlone);
        byte[] ArtOf(Func<int, int, System.Windows.Media.Color> at)
        {
            var art = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var c = at(x, y);
                    int i = (y * size + x) * 4;
                    art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                }
            return art;
        }
        var failures = new List<string>();
        var hidden = new List<string> { "9_MainCanvas" };
        using var card = SMSModForge.Rendering.Gpu.GpuFrame.TryCreate() ?? throw new InvalidOperationException("no graphics card device");
        try
        {
            Travel(game, "vanilla:" + level, level);
            game.Ask("wait 4", 20);
            string canvases = game.Ask("find-all *Canvas*");
            hidden.AddRange(canvases.Substring(canvases.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Contains("5_Levels/" + level + "/")));
            hidden.AddRange(OnBusts(game));   // as S7: the place's own character off
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            foreach (var key in new[] { "figure", "still", "noise" })
            {
                Func<int, int, System.Windows.Media.Color> artAt = key == "figure" ? InGameSuitePacks.Looks.FigureAt : InGameSuitePacks.Looks.ArtAt;
                string bust = key == "figure" ? InGameSuitePacks.Looks.FigureBust : InGameSuitePacks.Looks.Outfits.First(o => o.Key == key).Bust;
                var maskColour = key == "figure" ? System.Windows.Media.Color.FromArgb(0, 0, 0, 0) : InGameSuitePacks.Looks.Outfits.First(o => o.Key == key).Mask;
                var art = ArtOf(artAt);
                string root = "2_Bust_Manager/" + bust;
                string tag = "lvl-" + name + "-" + key;
                string said, still, meshReply = "";
                var laters = new List<string>();
                game.Ask("set-active " + root + " true");
                game.Ask("wait 5", 15);
                game.Ask("freeze on");
                game.Ask("wait 0.3", 10);
                try
                {
                    said = game.Ask("bust-look " + root + "/MBase1 " + tag, 30);
                    if (lamp.Length > 0) meshReply = game.Ask("shadow-mesh " + root + "/MBase1", 20);
                    game.Ask("set-active " + root + " false");
                    game.Ask("wait 0.3", 10);
                    still = LookFolder(game.Ask("screen-look " + tag + "-still", 15));
                    // Three times over five seconds, as S7: what moves by itself.
                    foreach (var (wait, n) in new[] { (1.3, ""), (1.3, "-2"), (2.6, "-3") })
                    {
                        game.Ask("freeze off");
                        game.Ask("wait " + wait.ToString(CultureInfo.InvariantCulture), 10);
                        game.Ask("freeze on");
                        game.Ask("wait 0.3", 10);
                        laters.Add(LookFolder(game.Ask("screen-look " + tag + "-later" + n, 15)));
                    }
                }
                finally
                {
                    game.Ask("freeze off");
                    game.Ask("set-active " + root + " false");
                }
                var look = LookOf(said.Substring(tag.Length).Trim());
                var (_, _, stillPx) = Png(Path.Combine(still, "screen.png"));
                var laterPxs = laters.Select(l => Png(Path.Combine(l, "screen.png")).Item3).ToList();
                var corners = look.Corners("drawnCorners");
                int bustBottom = (int)Math.Round(corners[0].y);
                float time = look.F("time");
                var p = new SMSModForge.Model.JiggleParams
                {
                    Speed = look.F("mat_JiggleSpeed"), Strength = look.F("mat_JiggleStrength"), Frequency = look.F("mat_JiggleFrequency"),
                    NoiseScale = look.F("mat_NoiseScale"), NoiseSpeed = look.F("mat_NoiseSpeed"), NoiseStrength = look.F("mat_NoiseStrength"),
                };
                var mask = new byte[size * size * 4];
                for (int i = 0; i < mask.Length; i += 4) { mask[i] = maskColour.B; mask[i + 1] = maskColour.G; mask[i + 2] = maskColour.R; mask[i + 3] = maskColour.A; }
                // A lamp's shadow, as S3 draws the PC's room's: from the bust's
                // shadow mesh as the game has it.
                float[]? shadow = null;
                var giArt = new FakeGi.Art(art, size, size, gi.Reach);
                if (scene.Lamp != null)
                {
                    var origin = meshReply.Split("; ").First(q => q.StartsWith("origin=", StringComparison.Ordinal)).Substring(7).Split(',')
                                          .Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                    shadow = new float[scene.Lamp.Shadow.W * scene.Lamp.Shadow.H];
                    scene.Lamp.Shadow.Draw(new BustShadow.Caster(art, size, size, 100f), origin[0], origin[1], 2f, 1f, shadow);
                }
                // The preview's frame, as JigglePreview makes it over the backdrop.
                byte[] Frame(FakeGi? withGi)
                {
                    GamePost.Target target;
                    if (scene.Lamp != null)
                    {
                        target = new GamePost.Target(backdrop.ScreenWidth, backdrop.ScreenHeight);
                        var background = new float[backdrop.BackgroundWidth * backdrop.BackgroundHeight * 4];
                        scene.Lamp.Room(shadow!, target, background, backdrop.BackgroundWidth, backdrop.BackgroundHeight);
                        withGi?.UseBackground(background);
                    }
                    else target = BackdropRender.Scene(scene);
                    var body = new float[N * N * 4];
                    BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), time, withGi, giArt, scene.Left, scene.FrameBottom, bustBottom,
                                        body, true, null, 0f, true, scene.Decode);
                    var coverage = new float[N * N];
                    BackdropRender.Layer(target, scene, body, coverage);
                    if (scene.Lamp != null) scene.Lamp.Glow(target, shadow!, BackdropRender.Covered(scene, coverage));
                    else BackdropRender.Lamps(target, scene, coverage);
                    var output = new byte[N * N * 4];
                    BackdropRender.Present(target, scene, BackdropRender.PostParts.All, output);
                    return output;
                }
                var preview = Frame(gi);
                var bare = Frame(null);
                var onCard = new byte[N * N * 4];
                if (scene.Lamp != null)
                    card.Draw(scene.Lamp, shadow!, scene, art, mask, p, (1f, 1f, 1f, 1f), time, true, bustBottom, true, 0f, null, null, null,
                              BackdropRender.PostParts.All, 1, onCard);
                else card.DrawNoLamp(scene, art, mask, p, (1f, 1f, 1f, 1f), time, true, bustBottom, true, 0f, null, null, null, BackdropRender.PostParts.All, 1, onCard);
                SaveFrame(preview, Path.Combine(look.Folder, "preview.png"));
                int compared = 0, within1 = 0, within3 = 0, moving = 0, unlikeNow = 0, edgeCompared = 0, edgeWithin3 = 0, bareEdgeWithin3 = 0, cardWithin3 = 0, cardEdgeWithin3 = 0;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        int sx = scene.Left + x, sy = scene.FrameBottom + (N - 1 - y);
                        var g = look.At(sx, sy);
                        int li = ((look.H - 1 - sy) * look.W + sx) * 4;
                        int o = (y * N + x) * 4;
                        float uv = (sy + 0.5f - bustBottom) / N;
                        int tx = x / 2, trow = size - 1 - (int)MathF.Floor(uv * size);
                        bool onArt = uv >= 0 && uv < 1 && trow >= 0 && trow < size && artAt(tx, trow).A > 0;
                        if (!onArt)
                        {
                            if (laterPxs.Any(l => Math.Abs(stillPx[li] - l[li]) > 1 || Math.Abs(stillPx[li + 1] - l[li + 1]) > 1 || Math.Abs(stillPx[li + 2] - l[li + 2]) > 1))
                            { moving++; continue; }
                            if (Math.Max(Math.Abs(roomAlone[o] - stillPx[li]), Math.Max(Math.Abs(roomAlone[o + 1] - stillPx[li + 1]), Math.Abs(roomAlone[o + 2] - stillPx[li + 2]))) > 3)
                            { unlikeNow++; continue; }
                        }
                        int D(byte[] f) => Math.Max(Math.Abs(f[o] - g.b), Math.Max(Math.Abs(f[o + 1] - g.g), Math.Abs(f[o + 2] - g.r)));
                        int d = D(preview), db = D(bare), dc = D(onCard);
                        compared++;
                        if (d <= 1) within1++;
                        if (d <= 3) within3++;
                        if (dc <= 3) cardWithin3++;
                        if (Math.Max(Math.Abs(preview[o] - bare[o]), Math.Max(Math.Abs(preview[o + 1] - bare[o + 1]), Math.Abs(preview[o + 2] - bare[o + 2]))) >= 4)
                        {
                            edgeCompared++;
                            if (d <= 3) edgeWithin3++;
                            if (db <= 3) bareEdgeWithin3++;
                            if (dc <= 3) cardEdgeWithin3++;
                        }
                    }
                double Share(int n, int of) => of == 0 ? 0 : (double)n / of;
                _out.WriteLine($"{name} {key}: {compared} pixels ({moving} moving and {unlikeNow} of the room unlike the shipped capture now left out): within one {Share(within1, compared):P2}, within three {Share(within3, compared):P2}; "
                               + $"lit edge ({edgeCompared}) within three {Share(edgeWithin3, edgeCompared):P2}, without the fake GI {Share(bareEdgeWithin3, edgeCompared):P2}; "
                               + $"on the card within three {Share(cardWithin3, compared):P2}, its edge {Share(cardEdgeWithin3, edgeCompared):P2}");
                if (Share(within3, compared) < 0.99) failures.Add($"{name} {key}: the preview is more than three levels from the game at {compared - within3} of {compared}");
                if (Share(cardWithin3, compared) < 0.99) failures.Add($"{name} {key}: the card's preview is more than three levels off at {compared - cardWithin3} of {compared}");
                if (edgeCompared >= 300)
                {
                    if (Share(edgeWithin3, edgeCompared) < 0.9) failures.Add($"{name} {key}: the lit edge is more than three levels off at {edgeCompared - edgeWithin3} of {edgeCompared}");
                    if (Share(bareEdgeWithin3, edgeCompared) > Share(edgeWithin3, edgeCompared) - 0.3) failures.Add($"{name} {key}: without its fake GI the edge is nearly as close - the check does not see it");
                }
            }
        }
        finally
        {
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
