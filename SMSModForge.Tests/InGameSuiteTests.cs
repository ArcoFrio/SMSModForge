using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SMSModForge.Tests;

/// <summary>
/// ModForge in the game itself (1.8.0, 12.13; the author, 2026-10-24: "make
/// proper ingame tests for all ModForge functionalities"). One test per area,
/// run in name order in one game: the main menu first, then the test save.
/// <para/>
/// Every check reads what the game holds - an object's state and place, a
/// variable, the map that is up - and has beside it a control that must come
/// out the other way: a check that cannot fail reads exactly like one that
/// passed.
/// <para/>
/// Off unless <c>SMSMODFORGE_INGAME</c> names the test copy; run it with
/// <c>Tools/Game/ingame-suite.ps1</c>, which builds the plugin's Debug build
/// (the one with the bridge) into the test copy first. It plays only the
/// test save, in the test copy's own saves folder - see Tools/Game.
/// </summary>
[TestCaseOrderer("SMSModForge.Tests.ByName", "SMSModForge.Tests")]
public sealed partial class InGameSuiteTests : IClassFixture<InGameSuiteTests.Session>
{
    private readonly Session _s;
    private readonly ITestOutputHelper _out;

    public InGameSuiteTests(Session s, ITestOutputHelper o)
    {
        _s = s;
        _out = o;
    }

    /// <summary>One game for the whole suite: started on the main menu with
    /// the suite's packs installed and ModForge's settings at their defaults.</summary>
    public sealed class Session : IDisposable
    {
        internal GameRun? Game { get; }
        public string Report { get; } = "";
        public List<string> Setup { get; } = new();
        private bool _inGame;

        public Session()
        {
            string? test = GameRun.TestCopy;
            if (test == null) return;
            string info = File.ReadAllText(Path.Combine(test, "Starmaker Story_Data", "app.info"));
            if (!info.Contains("Starmaker Tests"))
                throw new InvalidOperationException($"{test} is not the renamed test copy - it would save among the author's saves.");

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string scratch = Path.Combine(Path.GetTempPath(), "SMSModForge-ingame", stamp);
            Report = Path.Combine(scratch, "transcript.txt");
            Setup.AddRange(InGameSuitePacks.ExportInto(Path.Combine(test, "Mods"), Path.Combine(scratch, "packs")));

            // ModForge's own settings in the test copy - pack order, packs
            // switched off, the notice last shown - as the defaults have them,
            // but in English whatever Windows speaks: the checks read words
            // ModForge shows.
            string settings = Path.Combine(test, "BepInEx", "config", "treboy.starmakerstory.smsmodforge.packplugin.cfg");
            File.WriteAllText(settings, "[Language]\n\nLanguage = en\n");
            Setup.Add("ModForge's settings in the test copy: defaults, in English");

            Game = new GameRun(test);
            try { Game.Start(0); }
            catch
            {
                // xUnit does not dispose a fixture whose constructor threw.
                Game.Stop();
                throw;
            }
        }

        /// <summary>The test save, loaded once; every area after the main
        /// menu's plays in it.</summary>
        internal GameRun InGame()
        {
            var game = Game!;
            if (_inGame) return game;
            game.Ask("load");
            game.Ask("wait-ingame 150", 180);
            // ModForge's save warning comes up once the game has settled: the
            // test save was never played with the suite's packs. What it said
            // is kept for its own check; then the game goes on, as a player
            // choosing Continue does.
            Warning = game.AskUntil("warning", w => w.StartsWith("open", StringComparison.Ordinal), 15);
            if (Warning.StartsWith("open", StringComparison.Ordinal)) game.Ask("warning go-on");
            game.Ask("wait 2", 10);
            // Every level held where it rests (1.9.2): a level follows the mouse,
            // and on the run's desktop the mouse is wherever the author's is - the
            // backdrops were captured with each room 19 game px off its rest, and
            // a check comparing with them passed only while the mouse sat still.
            game.Ask("parallax rest");
            _inGame = true;
            return game;
        }

        /// <summary>What ModForge's save warning said as the test save loaded.</summary>
        public string Warning { get; private set; } = "";

        /// <summary>The game closed and started again on the main menu - the
        /// test save put back clean, ModForge's settings as the run left them,
        /// or as <paramref name="whileClosed"/> changes them: it runs with the
        /// game closed, so nothing the game holds writes over the change.</summary>
        internal GameRun Restart(Action? whileClosed = null)
        {
            var game = Game!;
            game.Stop();
            _inGame = false;
            Warning = "";
            whileClosed?.Invoke();
            game.Start(0);
            return game;
        }

        public void Dispose()
        {
            try { Game?.Stop(); }
            finally
            {
                if (Game != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Report)!);
                    File.WriteAllLines(Report, Setup.Concat(Game.Transcript));
                }
            }
        }
    }

    private bool Off => _s.Game == null;

    // ── What the bridge says, taken apart ───────────────────────────────

    /// <summary>One field of a <c>find</c> answer (<c>key value; key value</c>).</summary>
    private static string Field(string found, string key)
    {
        foreach (var part in found.Split("; "))
            if (part.StartsWith(key + " ", StringComparison.Ordinal)) return part.Substring(key.Length + 1);
        throw new XunitException($"No '{key}' in: {found}");
    }

    private static (float X, float Y) XY(string triple)
    {
        var n = triple.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return (n[0], n[1]);
    }

    private static void Near(float expected, float actual, string what, float within = 0.01f)
    {
        if (Math.Abs(expected - actual) > within) throw new XunitException($"{what}: expected {expected}, the game has {actual}");
    }

    private string Find(GameRun game, string path) => game.Ask("find " + path);

    private bool Active(GameRun game, string path) => Field(Find(game, path), "active").StartsWith("True", StringComparison.Ordinal);

    // ── A: the main menu ─────────────────────────────────────────────────

    [Fact]
    public void A1_The_main_menu_lists_every_installed_pack()
    {
        if (Off) return;
        var game = _s.Game!;
        string menu = game.AskUntil("menu", m => InGameSuitePacks.Ids.All(id => m.Contains(id, StringComparison.OrdinalIgnoreCase)), 30);
        _out.WriteLine(menu);
        foreach (var id in InGameSuitePacks.Ids)
            Assert.Contains(id, menu, StringComparison.OrdinalIgnoreCase);
        // Control: a pack that is not installed is not listed.
        Assert.DoesNotContain("suitenothere", menu, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A3_Two_packs_changing_one_of_the_games_scenes_are_marked_as_clashing_on_the_main_menu()
    {
        if (Off) return;
        var game = _s.Game!;
        string menu = game.AskUntil("menu", m => InGameSuitePacks.Ids.All(id => m.Contains(id, StringComparison.OrdinalIgnoreCase)), 30);
        _out.WriteLine(menu);
        string Row(string id) => menu.Split(" | ").First(r => r.Contains(id, StringComparison.OrdinalIgnoreCase));
        // The Story and Talk packs both change ameliablow (1), 01_MSmile (D4) and
        // ameliablow (2) (D5), and nothing else in common: each row names the other.
        Assert.Contains(InGameSuitePacks.Talk.Id, Row(InGameSuitePacks.Story.Id), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(InGameSuitePacks.Story.Id, Row(InGameSuitePacks.Talk.Id), StringComparison.OrdinalIgnoreCase);
        // Control: a pack that changes none of the same things names neither.
        Assert.DoesNotContain(InGameSuitePacks.Story.Id, Row(InGameSuitePacks.Rules.Id), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(InGameSuitePacks.Talk.Id, Row(InGameSuitePacks.Rules.Id), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ModForge's settings file in the test copy.</summary>
    private string Settings => Path.Combine(_s.Game!.Test, "BepInEx", "config", "treboy.starmakerstory.smsmodforge.packplugin.cfg");

    [Fact]
    public void A2_The_main_menu_notice_greets_the_player_and_closing_it_is_remembered()
    {
        if (Off) return;
        var game = _s.Game!;
        // Nothing shown yet in this game (the settings say nothing of it).
        string notice = game.AskUntil("notice", n => n.StartsWith("open", StringComparison.Ordinal), 20);
        _out.WriteLine(notice);
        Assert.StartsWith("open", notice);
        Assert.Contains("ModForge is installed", notice);
        Assert.DoesNotContain("Last shown = " + SMSModForge.Shared.ForgeVersion.Current, File.ReadAllText(Settings));

        game.Ask("click Got it");
        Assert.StartsWith("(none)", game.AskUntil("notice", n => n.StartsWith("(none)", StringComparison.Ordinal), 5));
        // Remembered - in ModForge's settings, written as it closed.
        Assert.Contains("Last shown = " + SMSModForge.Shared.ForgeVersion.Current, File.ReadAllText(Settings));
    }

    // ── B: places, their GameObjects, the game's levels ─────────────────

    private const string RoomLevel = "5_Levels/*_" + InGameSuitePacks.World.RoomObject;

    [Fact]
    public void B0_Loading_a_save_warns_of_the_pack_that_changes_the_games_conversations()
    {
        if (Off) return;
        _s.InGame();
        string warning = _s.Warning;
        _out.WriteLine(warning);
        Assert.StartsWith("open", warning);
        // gamecheck adds a line to two of the game's conversations; the test
        // save has never been played with it.
        Assert.Contains("'gamecheck': conversations", warning);
        // Control: a pack that changes nothing of the game's is not named.
        Assert.DoesNotContain(InGameSuitePacks.Rules.Id, warning);
        Assert.DoesNotContain(InGameSuitePacks.Story.Id, warning);
    }

    [Fact]
    public void B1_A_place_is_built_with_its_GameObjects_where_the_pack_put_them()
    {
        if (Off) return;
        var game = _s.InGame();

        // The art is the pack's: its size, and the colour it was painted.
        string level = Find(game, RoomLevel);
        _out.WriteLine(level);
        Assert.Contains("2048x1136", Field(level, "sprite"));
        Assert.Equal("#204060FF", Field(level, "texel"));
        string backdrop = Find(game, RoomLevel + "/CheckRoom_Secondary");
        Assert.Contains("2048x1136", Field(backdrop, "sprite"));
        Assert.Equal("#102030FF", Field(backdrop, "texel"));

        string lamp = Find(game, RoomLevel + "/Lamp");
        _out.WriteLine(lamp);
        Assert.Contains("64x32", Field(lamp, "sprite"));
        Assert.Equal("#FFCC00FF", Field(lamp, "texel"));
        var (x, y) = XY(Field(lamp, "home"));
        Near(1.5f, x, "Lamp x");
        Near(-2f, y, "Lamp y");
        Near(15f, float.Parse(Field(lamp, "rotz"), CultureInfo.InvariantCulture), "Lamp turned");
        Assert.StartsWith("2,", Field(lamp, "scale"));
        Assert.Contains("order 3", Field(lamp, "sprite"));
        Assert.StartsWith("True/", Field(lamp, "active"));
        // Control: the one authored to start off is off.
        Assert.StartsWith("False/", Field(Find(game, RoomLevel + "/Hidden"), "active"));

        string inner = Find(game, RoomLevel + "/Holder/Inner");
        var (ix, iy) = XY(Field(inner, "home"));
        Near(4f, ix, "Inner x");
        Near(1f, iy, "Inner y");
        // A node with no picture is a plain container.
        Assert.DoesNotContain("SpriteRenderer", Field(Find(game, RoomLevel + "/Holder"), "components"));
    }

    [Fact]
    public void B2_A_GameObject_with_conditions_follows_them()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.World.Id;
        game.Ask($"pset {id} signOn false");
        Assert.False(ActiveSoon(game, RoomLevel + "/Sign", false), "Sign is on while signOn is false");
        game.Ask($"pset {id} signOn true");
        Assert.True(ActiveSoon(game, RoomLevel + "/Sign", true), "Sign stayed off when signOn became true");
        game.Ask($"pset {id} signOn false");
        Assert.False(ActiveSoon(game, RoomLevel + "/Sign", false), "Sign stayed on when signOn went back to false");
    }

    [Fact]
    public void B3_Arriving_at_and_leaving_a_place_runs_its_hooks()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.World.Id;
        _out.WriteLine("the test save starts on " + game.Ask("level"));
        // Control: nothing has run before anybody arrived.
        Assert.Equal("0", game.PackVar(id, "entered"));
        Assert.Equal("0", game.PackVar(id, "left"));

        TravelToRoom(game);
        Assert.Equal("1", VarSoon(game, id, "entered", "1"));
        Assert.Equal("0", game.PackVar(id, "left"));

        Travel(game, "vanilla:" + InGameSuitePacks.World.Store, InGameSuitePacks.World.Store);
        Assert.Equal("1", VarSoon(game, id, "left", "1"));
        Assert.Equal("1", game.PackVar(id, "entered"));

        TravelToRoom(game);
        Assert.Equal("2", VarSoon(game, id, "entered", "2"));
    }

    [Fact]
    public void B8_A_Travel_on_a_line_makes_the_journey_once_its_conversation_is_over()
    {
        // The journey a navigator button makes, asked for by an action. The
        // game makes none with a conversation on screen, so one asked from a
        // line waits for the line's dialogue to end.
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.World.Id, store = InGameSuitePacks.World.Store;
        Travel(game, "vanilla:" + store, store);
        int entered = int.Parse(game.PackVar(id, "entered"), CultureInfo.InvariantCulture);

        string go = Timeline(game, "travel-line", $"dialogue {id} goroom", 10);
        double line = At(go, "on screen: .*goroom");
        double over = At(go, "on screen: -", line);
        Assert.True(At(go, "transfer: True", line) >= over, "The journey set out with the conversation still on screen.");
        Arrived(game, "_" + InGameSuitePacks.World.RoomObject, "a Travel on a line");
        // The place's arrival ran, as for any journey there.
        Assert.Equal((entered + 1).ToString(CultureInfo.InvariantCulture),
                     VarSoon(game, id, "entered", (entered + 1).ToString(CultureInfo.InvariantCulture)));

        // To one of the game's levels.
        Play(game, "gostore", id);
        Arrived(game, store, "a Travel to the Storage Room");

        // Control: to where the player already is, no journey at all.
        string stay = Timeline(game, "travel-here", $"dialogue {id} gostoreagain", 6);
        At(stay, "on screen: .*gostoreagain");
        Assert.DoesNotMatch(new Regex(@"^\+\S+ f\d+ transfer: True", RegexOptions.Multiline), stay);
        Assert.True(OnlyMapUp(game.Ask("level"), store), "The Storage Room is not the map up after a Travel to it from there.");
    }

    [Fact]
    public void B9_A_copy_of_one_of_the_games_objects_is_made_where_the_pack_puts_it_and_the_original_is_untouched()
    {
        if (Off) return;
        var game = _s.InGame();
        const string pool = InGameSuitePacks.World.PlantLevel;
        string source = InGameSuitePacks.World.PlantSource();
        var original = VanillaLevelCatalog.FindNode(VanillaLevelCatalog.FindLevel(pool), source)!;
        // Away from the room and the Pool, so neither drifts with the mouse
        // while their places are read.
        Travel(game, "vanilla:" + InGameSuitePacks.World.Store, InGameSuitePacks.World.Store);

        // In the room, where the pack put it, with the original's picture: the
        // place its drift with the mouse is anchored to - it drifts while the
        // room is up - or where it stands if the room never was.
        string copied = "5_Levels/*_" + InGameSuitePacks.World.RoomObject + "/" + InGameSuitePacks.World.PlantCopy;
        string copy = Find(game, copied);
        string parallax = game.Ask("fields ParallaxMouseEffect " + copied);
        string anchor = Regex.Match(parallax, @"startPosition=([^;]+)").Groups[1].Value;
        var (cx, cy) = XY(anchor.Length == 0 || anchor == "0,0,0" ? Field(copy, "local") : anchor);
        Near(3f, cx, "the copy's x");
        Near(2f, cy, "the copy's y");
        Assert.StartsWith(original.SpriteRenderer!.Sprite + " ", Field(copy, "sprite"));
        // Its drift with the mouse softened to 0.25, and the original's left.
        Assert.Contains("parallaxStrength=0.25;", parallax);
        Assert.DoesNotContain("parallaxStrength=0.25;", game.Ask("fields ParallaxMouseEffect 5_Levels/" + pool + "/" + source));
        // Control: the original is still the Pool's, where the game has it.
        var (ox, oy) = XY(Field(Find(game, "5_Levels/" + pool + "/" + source), "local"));
        Near(original.X, ox, "the original's x");
        Near(original.Y, oy, "the original's y");

        // Beside the original, with its particles and its Hearts' recoloured.
        const string effects = "6_Effects/";
        Assert.Equal("#FF99E6", Field(Find(game, effects + InGameSuitePacks.World.HeartsCopy), "particles"));
        Assert.Equal("#FF59B3", Field(Find(game, effects + InGameSuitePacks.World.HeartsCopy + "/Hearts"), "particles"));
        Assert.DoesNotContain("CheckRoom", game.Ask("find-all " + InGameSuitePacks.World.HeartsCopy));
        // Control: the game's own keeps its colours.
        Assert.NotEqual("#FF99E6", Field(Find(game, effects + InGameSuitePacks.World.HeartsEffect), "particles"));
        Assert.NotEqual("#FF59B3", Field(Find(game, effects + InGameSuitePacks.World.HeartsEffect + "/Hearts"), "particles"));
    }

    [Fact]
    public void B4_A_level_of_the_games_takes_the_packs_additions_and_only_the_changes_it_asks_for()
    {
        if (Off) return;
        var game = _s.InGame();
        const string beach = "5_Levels/" + InGameSuitePacks.World.Hike;

        string mark = Find(game, beach + "/HikeMark");
        var (mx, my) = XY(Field(mark, "home"));
        Near(2f, mx, "HikeMark x");
        Near(1f, my, "HikeMark y");
        Assert.Equal("#CC2222FF", Field(mark, "texel"));

        // A bound object that asks to be moved is moved...
        var (lx, ly) = XY(Field(Find(game, beach + "/Light 2D"), "local"));
        Near(0.25f, lx, "Light 2D local x");
        Near(0.5f, ly, "Light 2D local y");

        // ...and one bound only to hang a child on stays where the game has it.
        var vanilla = VanillaLevelCatalog.FindLevel(InGameSuitePacks.World.Hike);
        Assert.True(vanilla != null, "The editor's catalog of the game's levels is not here to compare with.");
        var npcs = VanillaLevelCatalog.FindNode(vanilla, "NPCs")!;
        var light = VanillaLevelCatalog.FindNode(vanilla, "Light 2D")!;
        var (nx, ny) = XY(Field(Find(game, beach + "/NPCs"), "local"));
        Near(npcs.LocalPosition[0], nx, "NPCs local x (the game's own)");
        Near(npcs.LocalPosition[1], ny, "NPCs local y (the game's own)");
        Assert.Equal("#CC2222FF", Field(Find(game, beach + "/NPCs/NpcsMark"), "texel"));
        // Control: the moved one really was somewhere else in the game.
        Assert.True(Math.Abs(light.LocalPosition[0] - 0.25f) > 0.01f || Math.Abs(light.LocalPosition[1] - 0.5f) > 0.01f,
                    "Light 2D is at 0.25,0.5 in the game already - the move proves nothing.");
    }

    [Fact]
    public void B5_Navigator_buttons_show_on_their_place_follow_their_conditions_and_take_you_there()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.World.Id, store = InGameSuitePacks.World.Store;
        TravelToRoom(game);
        game.Ask($"pset {id} navOk false");

        Assert.True(TextOnScreenSoon(game, "Check Store", true), "The room's button to the Storage Room is not on screen in the room.");
        // Control: the button whose condition does not hold is not.
        Assert.False(TextOnScreenSoon(game, "Check Gated", false), "A button whose condition fails is on screen.");
        // ...and the Storage Room's button to the room shows only there.
        Assert.False(TextOnScreenSoon(game, "Check Room", false), "The Storage Room's button shows in the room.");
        game.Ask($"pset {id} navOk true");
        Assert.True(TextOnScreenSoon(game, "Check Gated", true), "The button stayed hidden once its condition held.");

        game.Ask("press-text Check Store");
        Arrived(game, store, "pressing Check Store");
        Assert.True(TextOnScreenSoon(game, "Check Room", true), "The Storage Room's button to the room is not there.");
        game.Ask("press-text Check Room");
        Arrived(game, "_" + InGameSuitePacks.World.RoomObject, "pressing Check Room");
    }

    [Fact]
    public void B6_A_World_Map_button_shows_in_its_district_while_allowed_and_takes_you_there()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.World.Id;
        const string map = "World_Map", district = "World_Map/Canvas/Core/Radial_Buttons/Seaside";
        string button = district + "/" + InGameSuitePacks.World.Room;
        Travel(game, "vanilla:" + InGameSuitePacks.World.Store, InGameSuitePacks.World.Store);

        game.Ask($"pset {id} mapOk false");
        Assert.False(ActiveSoon(game, button, false), "A map button whose condition fails is offered.");
        game.Ask($"pset {id} mapOk true");
        Assert.True(ActiveSoon(game, button, true), "A map button whose condition holds is not offered.");
        Assert.Contains("'Map Room'", Field(Find(game, button + "/Text (TMP)"), "text"));

        // On the map, as a player opens it to that district - and only once it
        // has finished opening: a click during its opening fade goes nowhere,
        // for the game's own buttons as much as the pack's.
        game.Ask($"set-active {map} true");
        game.Ask("wait 1.5", 10);
        game.Ask($"set-active {district} true");
        game.Ask("wait 0.5", 10);
        try
        {
            game.Ask("press " + button);
            Arrived(game, "_" + InGameSuitePacks.World.RoomObject, "pressing Map Room");
        }
        finally { game.Ask($"set-active {map} false"); }
    }

    [Fact]
    public void B7_A_places_room_talk_is_empty_and_still_does_the_arrival_housekeeping_and_the_Talk_button()
    {
        if (Off) return;
        var game = _s.InGame();
        string roomTalk = "8_Room_Talk/SMSModForge_" + InGameSuitePacks.World.Id + "_RoomTalk";
        // A copy of the game's beach room talk with nothing in it - the beach's
        // first talk too (kept by mistake until 1.8.1). Control: the game's own.
        Assert.Equal("0", Field(Find(game, roomTalk), "children"));
        Assert.StartsWith("Defaultdialogue", Field(Find(game, "8_Room_Talk/Beach"), "children").Split('[')[1]);
        Assert.Throws<BridgeError>(() => Find(game, roomTalk + "/Defaultdialogue"));
        Find(game, "8_Room_Talk/Beach/Defaultdialogue");

        // Arriving: it runs its housekeeping - takes the lock, gives it back,
        // offers the Talk button - and switches itself off.
        Travel(game, "vanilla:" + InGameSuitePacks.World.Store, InGameSuitePacks.World.Store);
        string arrival = Timeline(game, "placetalk", "travel pack:" + InGameSuitePacks.World.Id + "." + InGameSuitePacks.World.Room, 6);
        string own = Regex.Escape(roomTalk.Substring(roomTalk.LastIndexOf('/') + 1));
        string running = @"talks: .*" + own + @" \(running", finished = @"talks: (?!.*" + own + ")";
        double ran = At(arrival, running);
        At(arrival, finished, after: ran);
        Assert.Equal("False", game.Ask("get Lock-Game").Split(" = ")[1]);
        Assert.Equal("True", game.Ask("get talk-button").Split(" = ")[1]);
        Assert.False(Active(game, roomTalk), "The place's room talk is still on after arriving.");

        // The Talk button switches the stored room talk - this one - back on,
        // and it does the same again.
        string talk = Timeline(game, "placetalk-button", "press 9_MainCanvas/TalkButton/Button", 6);
        double again = At(talk, running);
        At(talk, finished, after: again);
        Assert.Equal("False", game.Ask("get Lock-Game").Split(" = ")[1]);
        SeeConversationsThrough(game);
    }

    // ── C: NPCs ──────────────────────────────────────────────────────────

    [Fact]
    public void C1_An_NPC_stands_where_it_was_placed_with_its_eyes_shadow_and_reflection()
    {
        if (Off) return;
        var game = _s.InGame();
        string walker = Find(game, RoomLevel + "/NPCs/Walker");
        _out.WriteLine(walker);
        Assert.StartsWith("True/", Field(walker, "active"));
        var (x, y) = XY(Field(walker, "local"));
        Near(2f, x, "Walker local x");
        Near(-1f, y, "Walker local y");
        Assert.Contains("120x300", Field(walker, "sprite"));
        Assert.Contains("order 5", Field(walker, "sprite"));
        Assert.Equal("#33AA55FF", Field(walker, "texel"));

        string blink = Find(game, RoomLevel + "/NPCs/Walker/Blink");
        Assert.Equal("#000000FF", Field(blink, "texel"));
        Assert.Contains("BlinkingSprite", Field(blink, "components"));
        Assert.Contains("order 6", Field(blink, "sprite"));       // over the pose

        string shadow = Find(game, RoomLevel + "/NPCs/Walker/Circle");
        Assert.Contains("colour #000000 alpha 0.51", Field(shadow, "sprite"));
        var (_, sy) = XY(Field(shadow, "local"));
        Near(-2f, sy, "shadow local y");

        string reflection = Find(game, RoomLevel + "/NPCs/Walker/Reflection");
        Assert.Equal("1,-1", Field(reflection, "scale"));
        Assert.Contains("colour #AD92AA alpha 0.58", Field(reflection, "sprite"));
        // Control: an NPC's droplets are off unless asked for.
        Assert.DoesNotContain("Wet", Field(walker, "children"));
    }

    [Fact]
    public void C2_An_NPC_with_conditions_follows_them_even_in_a_crowd_the_game_puts_right()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.World.Id, town = InGameSuitePacks.World.Town;
        // Control: off while its condition fails.
        Assert.False(ActiveSoon(game, RoomLevel + "/NPCs/Ghost", false), "Ghost is out while ghostOn is false.");
        game.Ask($"pset {id} ghostOn true");
        Assert.True(ActiveSoon(game, RoomLevel + "/NPCs/Ghost", true), "Ghost stayed away once ghostOn was true.");
        game.Ask($"pset {id} ghostOn false");
        Assert.False(ActiveSoon(game, RoomLevel + "/NPCs/Ghost", false), "Ghost stayed out once ghostOn was false again.");

        // Downtown switches its whole crowd off as somebody arrives, then one
        // group back on; the pack's NPC among it must be there afterwards.
        Travel(game, "vanilla:" + town, town);
        Assert.True(ActiveSoon(game, $"5_Levels/{town}/NPCs/TownWalker", true), "The pack's NPC in Downtown went when Downtown arrived.");
        game.Ask($"pset {id} townNpc false");
        Assert.False(ActiveSoon(game, $"5_Levels/{town}/NPCs/TownWalker", false), "The pack's NPC in Downtown ignored its condition.");
        game.Ask($"pset {id} townNpc true");
    }

    // ── D: scenes and Starmaker photos ──────────────────────────────────

    private const string Scenes = "4_CG_Manager-Sexy/pack:" + InGameSuitePacks.Story.Id + ".";

    [Fact]
    public void D1_A_scene_shows_its_art_when_an_action_shows_it_and_goes_when_scenes_are_put_away()
    {
        if (Off) return;
        var game = _s.InGame();
        // Control: nothing is showing before anything asked.
        Assert.StartsWith("False/", Field(Find(game, Scenes + "photo"), "active"));
        Assert.StartsWith("False/", Field(Find(game, Scenes + "still"), "active"));

        Play(game, "showphoto");
        Assert.StartsWith("True/", Field(Find(game, Scenes + "photo"), "active"));
        Assert.Equal("#8844CCFF", Field(Find(game, Scenes + "photo/Core/Art"), "texel"));
        Assert.StartsWith("False/", Field(Find(game, Scenes + "still"), "active"));

        Play(game, "hidescenes");
        Assert.StartsWith("False/", Field(Find(game, Scenes + "photo"), "active"));
    }

    [Fact]
    public void D2_A_scene_marked_as_a_Starmaker_photo_gets_a_row_in_the_sites_gallery_and_a_plain_one_does_not()
    {
        if (Off) return;
        var game = _s.InGame();
        Play(game, "showphoto");
        Play(game, "showstill");
        Play(game, "hidescenes");
        // The row is made once the gallery is found - swept for every 2 s.
        string rows = game.AskUntil("find-all SMSModForge_Photo_*", r => !r.Contains("(none)"), 6);
        _out.WriteLine(rows);
        var paths = rows.Substring(rows.IndexOf(": ", StringComparison.Ordinal) + 2).Split(" | ");
        // The photo's, and the ones of the game's scenes the packs make photos (D3, D5:
        // two of the Story pack's and one the Talk pack shares with it); the stills
        // are not photos.
        string photo = Assert.Single(paths, p => p.EndsWith("_" + InGameSuitePacks.Story.Id + "_photo", StringComparison.Ordinal));
        foreach (var key in new[] { "gphoto", "gmoving", "gsam" })
            Assert.Contains(paths, p => p.EndsWith("_" + InGameSuitePacks.Story.Id + "_" + key, StringComparison.Ordinal));
        Assert.Contains(paths, p => p.EndsWith("_" + InGameSuitePacks.Talk.Id + "_tmoving", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.EndsWith("_still", StringComparison.Ordinal));
        Assert.Equal(5, paths.Length);
        Assert.Contains("CoreGallery/SMSModForge_Photo_", photo);
        Assert.Contains("#8844CCFF", game.Ask("find " + photo));
    }

    /// <summary>One of the game's scenes as it stands, by the bridge's
    /// <c>game-scene</c>: <c>name=value</c> parts.</summary>
    private Dictionary<string, string> GameScene(GameRun game, string path)
    {
        string said = game.Ask("game-scene " + path);
        _out.WriteLine(said);
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in said.Substring(said.IndexOf(": ", StringComparison.Ordinal) + 2).Split("; "))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) parts[part.Substring(0, eq)] = part.Substring(eq + 1);
        }
        return parts;
    }

    [Fact]
    public void D3_A_change_to_one_of_the_games_scenes_dresses_it_in_place_and_leaves_the_rest_the_games()
    {
        if (Off) return;
        var game = _s.InGame();
        var kiss = GameScene(game, InGameSuitePacks.Story.GameSceneKiss);
        var control = GameScene(game, InGameSuitePacks.Story.GameSceneControl);

        // The control first: the game's own art (a texture nobody can read back),
        // the game's frame, its own kiss.
        Assert.Equal("False", control["readable"]);
        Assert.StartsWith("SexyFrame ", control["frame"]);
        Assert.Equal("[kiss]", control["signals"]);

        // Changed: the pack's picture (its colour), the shipped photo frame at its
        // own size, the kiss turned into a flash - same name, every component kept.
        Assert.Equal("AmeliaStretching", kiss["name"]);
        Assert.Equal("True", kiss["readable"]);
        Assert.Equal("#CC4488", kiss["texel"]);
        var frame = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "VanillaFrames", "PhotoFrame.png")),
            System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None);
        Assert.EndsWith(" " + frame.PixelWidth + "x" + frame.PixelHeight, kiss["frame"]);
        Assert.Equal("[flash]", kiss["signals"]);
        Assert.Equal(control["components"], kiss["components"]);
        Assert.Equal(control["artSize"], kiss["artSize"]);

        // Sound only: the signal is still there, switched off; the picture the game's.
        var quiet = GameScene(game, InGameSuitePacks.Story.GameSceneQuiet);
        Assert.Equal("[kiss off]", quiet["signals"]);
        Assert.Equal("False", quiet["readable"]);
        Assert.Equal(control["components"], quiet["components"]);

        // A photo whose trigger raises nothing gets a kiss of its own; a scene made a
        // Starmaker photo takes it as it appears - each by a component added, none taken.
        var noSignal = GameScene(game, InGameSuitePacks.Story.GameSceneNoSignal);
        Assert.Equal("[]", noSignal["signals"]);
        Assert.Contains("PackSceneSignal", noSignal["components"]);
        var photo = GameScene(game, InGameSuitePacks.Story.GameScenePhoto);
        Assert.Equal(control["components"] + ",PackScenePhoto", photo["components"]);
        Assert.DoesNotContain("PackScene", control["components"]);

        // The scene whose art the game draws at half its size: a picture of the
        // common size comes out the size of the control's.
        var big = GameScene(game, InGameSuitePacks.Story.GameSceneBig);
        Assert.Equal("#2288CC", big["texel"]);
        Assert.Equal("1", big["artScale"]);
        Assert.Equal(control["artSize"], big["artSize"]);

        // It is one of the game's photos, and the Starmaker site's gallery draws its
        // picture too: the row follows; another photo's row keeps the game's.
        string row = game.Ask("find " + InGameSuitePacks.Story.GameSceneBigRow);
        string otherRow = game.Ask("find " + InGameSuitePacks.Story.GalleryControlRow);
        _out.WriteLine(row);
        _out.WriteLine(otherRow);
        Assert.Contains("#2288CCFF", row);
        Assert.DoesNotContain("#2288CCFF", otherRow);

        // Shown as the game shows them - the object switched on - each raises what
        // it now raises: the flash from the changed one, nothing from the silent one,
        // the kiss from the control.
        game.Ask("signals");
        string Heard(string path)
        {
            game.Ask("set-active " + path + " true");
            game.Ask("wait 0.5", 10);
            string heard = game.Ask("signals");
            game.Ask("set-active " + path + " false");
            _out.WriteLine(path + ": " + heard);
            return heard;
        }
        Assert.Contains("flash(AmeliaStretching)", Heard(InGameSuitePacks.Story.GameSceneKiss));
        Assert.DoesNotContain("(ameliajog)", Heard(InGameSuitePacks.Story.GameSceneQuiet));
        string controlHeard = Heard(InGameSuitePacks.Story.GameSceneControl);
        Assert.Contains("kiss(ameliastrip)", controlHeard);
        Assert.DoesNotContain("flash", controlHeard);

        // And by the pack's own action, by its key, as one of its own scenes.
        Play(game, "showgkiss");
        Assert.Equal("True", GameScene(game, InGameSuitePacks.Story.GameSceneKiss)["active"]);
        Play(game, "hidescenes");
        Assert.Equal("False", GameScene(game, InGameSuitePacks.Story.GameSceneKiss)["active"]);
    }

    [Fact]
    public void D4_A_changed_game_scene_takes_a_frame_and_a_moving_picture_shows_from_the_games_own_conversation_and_two_packs_share_it()
    {
        if (Off) return;
        var game = _s.InGame();
        const string clean = InGameSuitePacks.Story.GameSceneClean;

        // A frame of the pack's own, at its own size, the game's picture and sound kept.
        var framed = GameScene(game, InGameSuitePacks.Story.GameSceneFrame);
        Assert.EndsWith(" 300x320", framed["frame"]);
        Assert.Equal("False", framed["readable"]);
        Assert.Equal("[kiss]", framed["signals"]);

        // Two packs on one scene: the later pack's picture and sound show, and the earlier
        // pack's frame - which the later one leaves alone - stays.
        var shared = GameScene(game, InGameSuitePacks.Story.GameSceneShared);
        Assert.Equal("#0066AA", shared["texel"]);
        Assert.EndsWith(" 297x310", shared["frame"]);
        Assert.Equal("[kiss off]", shared["signals"]);
        // ...and one component for a sound both give a scene that has none.
        var noSignal = GameScene(game, InGameSuitePacks.Story.GameSceneNoSignal);
        Assert.Single(Regex.Matches(noSignal["components"], "PackSceneSignal"));

        // A two-frame GIF: it animates on the game's object. Read several times while
        // shown, the picture changes; the control above (a still) never does.
        Assert.Contains("AnimatedSprite", GameScene(game, InGameSuitePacks.Story.GameSceneGif)["components"]);
        game.Ask("set-active " + InGameSuitePacks.Story.GameSceneGif + " true");
        var seen = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            seen.Add(GameScene(game, InGameSuitePacks.Story.GameSceneGif)["texel"]);
            game.Ask("wait 0.07", 10);
        }
        game.Ask("set-active " + InGameSuitePacks.Story.GameSceneGif + " false");
        _out.WriteLine("the GIF drew: " + string.Join(", ", seen));
        Assert.True(seen.Count >= 2, "the GIF on one of the game's scenes did not move: " + string.Join(", ", seen));

        // Shown by one of the game's own conversations, as it always was: the pack's
        // picture, and its flash rather than the game's kiss.
        SeeConversationsThrough(game);
        game.Ask("signals");
        game.Ask("vanilla-dialogue " + InGameSuitePacks.Story.CleanTalk);
        game.AskUntil("game-scene " + clean, s => s.Contains("active=True"), 10);
        var cleaning = GameScene(game, clean);
        Assert.Equal("True", cleaning["active"]);
        Assert.Equal("#44AA22", cleaning["texel"]);
        string heard = game.Ask("signals");
        _out.WriteLine(heard);
        Assert.Contains("flash(AnnaCleaning)", heard);
        Assert.DoesNotContain("kiss(AnnaCleaning)", heard);
        SeeConversationsThrough(game);
        game.Ask("set-active " + clean + " false");

        // A sound given by two packs to one of the game's photos that has none: shown, it
        // plays once, the later pack's.
        game.Ask("signals");
        game.Ask("set-active " + InGameSuitePacks.Story.GameSceneNoSignal + " true");
        game.Ask("wait 0.5", 10);
        heard = game.Ask("signals");
        game.Ask("set-active " + InGameSuitePacks.Story.GameSceneNoSignal + " false");
        _out.WriteLine(heard);
        Assert.Single(Regex.Matches(heard, Regex.Escape("flash(01_MSmile)")));
        Assert.DoesNotContain("kiss(01_MSmile)", heard);

        // One of the game's scenes made a Starmaker photo: shown, the photo is taken - the
        // pack remembers it, and the game's own payout runs (PhotoBonus counts every
        // photo in Core_2[photos-taken-alltime] as it starts). Control: showing one of
        // the game's scenes that is no photo counts nothing.
        int Photos() => (int)double.Parse(game.Ask("gget Core_2[photos-taken-alltime]").Split('=').Last().Trim(), CultureInfo.InvariantCulture);
        game.Ask("wait 3", 10);   // the photo above has finished paying out
        int before = Photos();
        game.Ask("set-active " + InGameSuitePacks.Story.GameSceneControl + " true");
        game.Ask("wait 2", 10);
        game.Ask("set-active " + InGameSuitePacks.Story.GameSceneControl + " false");
        Assert.Equal(before, Photos());
        Assert.EndsWith("= False", game.Ask("pphoto " + InGameSuitePacks.Story.Id + " gphoto"));
        game.Ask("set-active " + InGameSuitePacks.Story.GameScenePhoto + " true");
        game.Ask("wait 2", 10);
        game.Ask("set-active " + InGameSuitePacks.Story.GameScenePhoto + " false");
        Assert.EndsWith("= True", game.Ask("pphoto " + InGameSuitePacks.Story.Id + " gphoto"));
        Assert.Equal(before + 1, Photos());
    }

    [Fact]
    public void D5_Two_packs_on_one_game_scene_show_the_later_picture_and_share_its_photo_and_a_change_reaches_nothing_else()
    {
        if (Off) return;
        var game = _s.InGame();
        int Photos() => (int)double.Parse(game.Ask("gget Core_2[photos-taken-alltime]").Split('=').Last().Trim(), CultureInfo.InvariantCulture);
        const string story = InGameSuitePacks.Story.Id, talk = InGameSuitePacks.Talk.Id;
        const string moving = InGameSuitePacks.Story.GameSceneMoving;

        // The Story pack gave this scene a GIF, the Talk pack (loaded later) a still: the
        // still shows, and stays - the GIF's frames are gone, not drawing over it.
        // Control: the Story pack's other GIF, which nobody overrides, still has its own (D4).
        var shared = GameScene(game, moving);
        Assert.DoesNotContain("AnimatedSprite", shared["components"]);
        Assert.Contains("AnimatedSprite", GameScene(game, InGameSuitePacks.Story.GameSceneGif)["components"]);
        Assert.Single(Regex.Matches(shared["components"], "PackScenePhoto"));
        game.Ask("wait 3", 10);   // any photo before has finished paying out
        int before = Photos();
        Assert.EndsWith("= False", game.Ask("pphoto " + story + " gmoving"));
        Assert.EndsWith("= False", game.Ask("pphoto " + talk + " tmoving"));
        game.Ask("set-active " + moving + " true");
        var seen = new HashSet<string>();
        for (int i = 0; i < 8; i++)
        {
            seen.Add(GameScene(game, moving)["texel"]);
            game.Ask("wait 0.07", 10);
        }
        game.Ask("wait 2", 10);
        game.Ask("set-active " + moving + " false");
        _out.WriteLine("the shared scene drew: " + string.Join(", ", seen));
        Assert.Equal("#55AA33", Assert.Single(seen));
        // Both packs made it a Starmaker photo: both remember it was taken, and the
        // game paid it out once - one photo was taken.
        Assert.EndsWith("= True", game.Ask("pphoto " + story + " gmoving"));
        Assert.EndsWith("= True", game.Ask("pphoto " + talk + " tmoving"));
        Assert.Equal(before + 1, Photos());

        // The Story pack changed Samanthabeach, the scene every pack scene is cloned
        // from: its frame, its sound, its picture, a photo. Changed there...
        var sam = GameScene(game, InGameSuitePacks.Story.GameScenePrototype);
        Assert.EndsWith(" 310x330", sam["frame"]);
        Assert.Equal("#7733AA", sam["texel"]);
        Assert.Contains("PackScenePhoto", sam["components"]);
        // ...and nowhere else: the Talk pack's own scene, built after it, is cloned as
        // the Story pack's own (built before it) is - the game's frame, no photo.
        var ownBefore = GameScene(game, "4_CG_Manager-Sexy/pack:" + story + ".still");
        var ownAfter = GameScene(game, "4_CG_Manager-Sexy/pack:" + talk + ".tstill");
        Assert.Equal("#33AA77", ownAfter["texel"]);
        Assert.Equal(ownBefore["frame"], ownAfter["frame"]);
        Assert.Equal(ownBefore["components"], ownAfter["components"]);
        Assert.DoesNotContain("PackScene", ownAfter["components"]);
        game.Ask("wait 3", 10);
        before = Photos();
        game.Ask("set-active 4_CG_Manager-Sexy/pack:" + talk + ".tstill true");
        game.Ask("wait 2", 10);
        game.Ask("set-active 4_CG_Manager-Sexy/pack:" + talk + ".tstill false");
        Assert.Equal(before, Photos());

        // A scene that is no photo but draws the picture of one (gropepark, 08_MPark's
        // "Park1"): given a picture, the scene shows it, and the photo's gallery row
        // keeps the game's. Control: a photo's row follows its picture (D3). Measured
        // 2026-11-15: with galleries redrawn for every scene, not only photos, this
        // still passes - the game draws gropepark with a sprite of its own, the same
        // picture under the same name - so this guards the row, and proves nothing
        // about the photos-only rule itself.
        Assert.Equal("#3355DD", GameScene(game, InGameSuitePacks.Story.GameSceneSharesPhotoArt)["texel"]);
        string row = game.Ask("find " + InGameSuitePacks.Story.SharedArtRow);
        _out.WriteLine(row);
        Assert.Contains("image Park1 ", row);
        Assert.DoesNotContain("#3355DDFF", row);
        Assert.Contains("#2288CCFF", game.Ask("find " + InGameSuitePacks.Story.GameSceneBigRow));
    }

    /// <summary>
    /// One of the game's scenes the pack does not change, named by where it sits in
    /// the Scene category (ArcoFrio, 1.9.0: the pack's scene handling for the game's
    /// scenes too): shown by the pack's action with its own sound, seen by a
    /// condition, repainted by a sprite swap, and put away by "hide every scene this
    /// pack showed" - which leaves a scene the pack did not show alone (the control).
    /// The same swap on one of the pack's own scenes paints its picture as well.
    /// </summary>
    [Fact]
    public void D6_One_of_the_games_scenes_by_where_it_sits_is_shown_seen_repainted_and_put_away_by_the_pack()
    {
        if (Off) return;
        var game = _s.InGame();
        const string path = InGameSuitePacks.Story.GameSceneByPath;
        const string control = InGameSuitePacks.Story.GameSceneControl;
        string badge = Panel + "/Panel/SceneBadge";

        // Before: off, the game's own picture, and the badge that asks about it out.
        var before = GameScene(game, path);
        Assert.Equal("False", before["active"]);
        Assert.Equal("False", before["readable"]);
        Play(game, "openpanel");
        try
        {
            Assert.True(ActiveSoon(game, Panel, true), "The screen did not open.");
            Assert.False(ActiveSoon(game, badge, false), "The badge is in while the scene is not showing.");

            // Shown by the pack's action: on, its own trigger raising its own kiss.
            game.Ask("signals");
            Play(game, "showgamepath");
            Assert.Equal("True", GameScene(game, path)["active"]);
            string heard = game.Ask("signals");
            _out.WriteLine(heard);
            Assert.Contains("kiss(EmmaXBigfoot (0))", heard);
            Assert.True(ActiveSoon(game, badge, true), "The condition did not see the game's scene showing.");

            // A sprite swap on it paints its picture, at the picture's size.
            Play(game, "swapgamepath");
            var swapped = GameScene(game, path);
            Assert.Equal(InGameSuitePacks.Story.SwapColour, swapped["texel"]);
            Assert.Equal(before["artSize"], swapped["artSize"]);

            // Put away with everything the pack showed; one the pack did not show stays.
            game.Ask("set-active " + control + " true");
            Play(game, "hidescenes");
            Assert.Equal("False", GameScene(game, path)["active"]);
            Assert.Equal("True", GameScene(game, control)["active"]);
            game.Ask("set-active " + control + " false");
            Assert.False(ActiveSoon(game, badge, false), "The badge stayed in once the scene was put away.");

            // The same swap on one of the pack's own scenes.
            Play(game, "swapstill");
            Assert.Equal(InGameSuitePacks.Story.SwapColour,
                         GameScene(game, "4_CG_Manager-Sexy/pack:" + InGameSuitePacks.Story.Id + ".still")["texel"]);
        }
        finally { Play(game, "closepanel"); }
    }

    // ── E: wallpapers ────────────────────────────────────────────────────

    [Fact]
    public void E1_A_wallpaper_is_offered_once_unlocked_and_put_up_when_chosen()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Story.Id;
        const string selection = "9_MainCanvas/Wallpaperselection";
        string button = $"{selection}/UI_Core/List/WallpaperButton (pack:{id}.sunset)";
        string shown = $"9_MainCanvas/Desktop/Wallpaper/pack:{id}.sunset";

        // Control: locked, it is not offered.
        game.Ask($"pset {id} wallOk false");
        Assert.False(ActiveSoon(game, button, false), "A locked wallpaper is offered.");
        game.Ask($"pset {id} wallOk true");
        Assert.True(ActiveSoon(game, button, true), "An unlocked wallpaper is not offered.");
        Assert.StartsWith("False/", Field(Find(game, shown), "active"));

        // Choosing it, on the game's own selection screen.
        game.Ask($"set-active {selection} true");
        try
        {
            game.Ask("press " + button);
            Assert.True(ActiveSoon(game, shown, true), "The chosen wallpaper is not up.");
            Assert.Equal("#E06030FF", Field(Find(game, shown), "texel"));
        }
        finally { game.Ask($"set-active {selection} false"); }
    }

    // ── F: the pack's own screens ────────────────────────────────────────

    private const string Panel = "PackUiCanvas_checkpanel/Pack_checkpanel";

    [Fact]
    public void F1_A_screen_opens_and_closes_by_action_its_buttons_act_when_allowed_and_its_parts_follow_conditions()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Story.Id;
        // Control: shut until something opens it.
        Assert.StartsWith("False/", Field(Find(game, Panel), "active"));
        Play(game, "openpanel");
        Assert.True(ActiveSoon(game, Panel, true), "The screen did not open.");
        string panel = Find(game, Panel + "/Panel");
        Assert.Contains("colour #224466", Field(panel, "image"));
        Assert.Equal("#FFFFFFFF", Field(panel, "texel"));        // the pack's picture, tinted

        // A click the screen does not allow yet does nothing...
        game.Ask($"pset {id} uiClickOk false");
        game.Ask("press-text Check Press");
        Assert.Equal("0", VarSoon(game, id, "uiClicked", "1", 1.5));
        // ...and once allowed, does what the button says.
        game.Ask($"pset {id} uiClickOk true");
        game.Ask("press-text Check Press");
        Assert.Equal("1", VarSoon(game, id, "uiClicked", "1"));

        Assert.False(ActiveSoon(game, Panel + "/Panel/Badge", false), "The badge is out while badgeOn is false.");
        game.Ask($"pset {id} badgeOn true");
        Assert.True(ActiveSoon(game, Panel + "/Panel/Badge", true), "The badge stayed in once badgeOn was true.");

        Play(game, "closepanel");
        Assert.False(ActiveSoon(game, Panel, false), "The screen did not close.");
    }

    [Fact]
    public void F3_A_screen_opens_by_itself_when_its_conditions_become_met_and_one_with_none_never_does()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Story.Id;
        const string opener = "PackUiCanvas_opener/Pack_opener", none = "PackUiCanvas_nocond/Pack_nocond";

        // Control: shut while its condition does not hold - and the screen with
        // no conditions is shut too, for good.
        game.Ask($"pset {id} openerGo false");
        game.Ask("wait 1", 10);
        Assert.StartsWith("False/", Field(Find(game, opener), "active"));
        Assert.StartsWith("False/", Field(Find(game, none), "active"));

        game.Ask($"pset {id} openerGo true");
        Assert.True(ActiveSoon(game, opener, true), "The screen did not open when its condition became met.");
        Assert.StartsWith("False/", Field(Find(game, none), "active"));

        // Closed while it is still met, it stays closed: it opens on the
        // moment, not for as long as.
        game.Ask($"set-active {opener} false");
        game.Ask("wait 1.5", 10);
        Assert.StartsWith("False/", Field(Find(game, opener), "active"));

        // Met anew, it opens again.
        game.Ask($"pset {id} openerGo false");
        game.Ask("wait 0.5", 10);
        game.Ask($"pset {id} openerGo true");
        Assert.True(ActiveSoon(game, opener, true), "The screen did not open again when its condition was met anew.");
        Assert.StartsWith("False/", Field(Find(game, none), "active"));

        game.Ask($"set-active {opener} false");
        game.Ask($"pset {id} openerGo false");
    }

    // ── G: quests ────────────────────────────────────────────────────────

    [Fact]
    public void G1_A_quest_starts_moves_through_its_tasks_completes_and_resets_as_its_pack_says()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Quests.Id;
        const string ask = "quest " + id + " chase first,count,last";
        string Now() => game.Ask(ask);
        string Soon(Func<string, bool> ready) => game.AskUntil(ask, ready, 5);

        // Control: it waits for its start condition.
        game.Ask("wait 1", 10);
        Assert.Contains("quest=Inactive", Now());
        game.Ask($"pset {id} questGo true");
        string q = Soon(s => s.Contains("quest=Active") && s.Contains("first=Active"));
        _out.WriteLine(q);
        Assert.Contains("quest=Active", q);
        Assert.Contains("first=Active", q);
        Assert.Contains("count=Inactive", q);

        game.Ask($"pset {id} firstDone true");
        q = Soon(s => s.Contains("first=Completed") && s.Contains("count=Active"));
        Assert.Contains("first=Completed", q);
        Assert.Contains("count=Active", q);
        Assert.Equal("1", VarSoon(game, id, "rewards", "1"));      // the task's own action

        // A counted task follows its variable, and only completes at its target.
        game.Ask($"pset {id} tally 1");
        q = Soon(s => s.Contains("count=Active 1"));
        Assert.Contains("count=Active 1", q);
        game.Ask($"pset {id} tally 3");
        q = Soon(s => s.Contains("count=Completed") && s.Contains("last=Active"));
        Assert.Contains("count=Completed", q);
        Assert.Contains("last=Active", q);
        Assert.Contains("quest=Active", q);       // control: not done while a task is left

        Play(game, "finishlast", id);
        q = Soon(s => s.Contains("quest=Completed"));
        Assert.Contains("last=Completed", q);
        Assert.Contains("quest=Completed", q);

        // Reset: back to not started while its reset condition holds.
        game.Ask($"pset {id} questGo false");
        game.Ask($"pset {id} questReset true");
        q = Soon(s => s.Contains("quest=Inactive"));
        Assert.Contains("quest=Inactive", q);
        game.Ask($"pset {id} questReset false");
    }

    [Fact]
    public void G2_A_task_a_pack_adds_to_one_of_the_games_quests_is_in_that_quest_and_the_save_warning_names_the_pack()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Quests.Id;
        string q = game.Ask($"gquest {id} extra,neveradded {InGameSuitePacks.Quests.GameQuest}");
        _out.WriteLine(q);
        Assert.Contains("extra=", q);
        Assert.DoesNotContain("extra=(none)", q);
        Assert.Contains("neveradded=(none)", q);      // control: a key nobody added
        // Changing a game quest's tasks can break a save part-way through it,
        // so loading one never played with the pack says so - and where this
        // save stands with that quest.
        Assert.Contains("'" + id + "': quests", _s.Warning);
        Assert.Contains(InGameSuitePacks.Quests.GameQuest + ": not started yet", _s.Warning);
    }

    // ── P: effects (the FX tab, 1.10.0) ──────────────────────────────────

    /// <summary>The objects under one, by name; nothing when there is no such object (yet).</summary>
    private static string[] Kids(GameRun game, string path)
    {
        try
        {
            string said = game.Ask("children " + path);
            int colon = said.IndexOf(": ", StringComparison.Ordinal);
            string list = colon >= 0 ? said[(colon + 2)..] : said;
            return list == "(none)" ? Array.Empty<string>() : list.Split(" | ").Select(p => p[(p.LastIndexOf('/') + 1)..]).ToArray();
        }
        catch (BridgeError) { return Array.Empty<string>(); }
    }

    private const string EffectsRoot = "SMSModForge Effects";

    [Fact]
    public void P1_An_effect_plays_when_its_rule_its_conditions_or_a_photo_say_and_the_games_own_points_are_changed()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Effects.Id;
        string burst = "Effect " + id + ".burst", fire = "Effect " + id + ".fire", hearts = "Effect " + id + ".photoHearts";

        // The game's own followers, three times as many (Harmony on the game's TriggerExplosion); its hearts,
        // which no pack changed, as many as asked - the control.
        int Clones(string name) => Kids(game, "Attractor_Canvas").Count(k => k == name + "(Clone)");
        int followers = Clones("Attractor_NewFollower"), subscribers = Clones("Attractor_NewSubscriber");
        game.Ask("call ExplosiveUIParticleAttractor TriggerExplosion 2 Attractor_Canvas/Attractor_Follower");
        game.Ask("call ExplosiveUIParticleAttractor TriggerExplosion 2 Attractor_Canvas/Attractor_Subscriber");
        Assert.Equal(followers + 6, Clones("Attractor_NewFollower"));
        Assert.Equal(subscribers + 2, Clones("Attractor_NewSubscriber"));

        // Played by a rule's actions: the burst, and the words beside it; gone once done.
        Assert.DoesNotContain(burst, Kids(game, EffectsRoot));
        game.Ask($"pset {id} burstGo true");
        game.AskUntil("children " + EffectsRoot, k => k.Contains(burst, StringComparison.Ordinal), 5);
        var now = Kids(game, EffectsRoot);
        Assert.Contains(burst, now);
        Assert.Contains("Effect popup words", now);
        game.AskUntil("children " + EffectsRoot, k => !k.Contains(burst, StringComparison.Ordinal), 6);
        Assert.DoesNotContain(burst, Kids(game, EffectsRoot));
        game.Ask($"pset {id} burstGo false");

        // While its conditions hold: the fire comes and, once they do not, goes.
        Assert.DoesNotContain(fire, Kids(game, EffectsRoot));
        game.Ask($"pset {id} fireOn true");
        game.AskUntil("children " + EffectsRoot, k => k.Contains(fire, StringComparison.Ordinal), 5);
        Assert.Contains(fire, Kids(game, EffectsRoot));
        game.Ask($"pset {id} fireOn false");
        game.AskUntil("children " + EffectsRoot, k => !k.Contains(fire, StringComparison.Ordinal), 8);
        Assert.DoesNotContain(fire, Kids(game, EffectsRoot));

        // An object of a room plays its effect where it stands, with the room - in the room, so the player goes
        // there first: a room the player is not in runs nothing (the whole run found it from another room, with
        // nothing out). The one beside it has none.
        string room = InGameSuitePacks.Effects.HearthLevel;
        Travel(game, "vanilla:" + room, room);
        string hearthPath = "5_Levels/" + room + "/" + InGameSuitePacks.Effects.Hearth;
        Assert.Contains("Effect " + id + "." + InGameSuitePacks.Effects.Hearth, Kids(game, hearthPath));
        Assert.True(Active(game, hearthPath + "/Effect " + id + "." + InGameSuitePacks.Effects.Hearth), "the hearth's effect is off");
        game.AskUntil("fx-alive " + hearthPath, n => int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out int alive) && alive > 0, 5);
        Assert.True(int.Parse(game.Ask("fx-alive " + hearthPath), CultureInfo.InvariantCulture) > 0, "the hearth's effect has nothing out");
        Assert.DoesNotContain(Kids(game, "5_Levels/" + room + "/" + InGameSuitePacks.Effects.Plain), k => k.StartsWith("Effect", StringComparison.Ordinal));

        // Each Starmaker photo shown: the pack's 12 hearts.
        Assert.DoesNotContain(hearts, Kids(game, EffectsRoot));
        try
        {
            game.Ask("set-active 4_CG_Manager-Photos/03_MLeanForwd true");
            game.AskUntil("children " + EffectsRoot, k => k.Contains(hearts, StringComparison.Ordinal), 5);
            Assert.Equal(12, Kids(game, EffectsRoot + "/" + hearts).Count(k => k == "point"));
        }
        finally { game.Ask("set-active 4_CG_Manager-Photos/03_MLeanForwd false"); }
    }

    // ── H: integration rules ─────────────────────────────────────────────

    private int Num(GameRun game, string pack, string name) => (int)double.Parse(game.PackVar(pack, name), CultureInfo.InvariantCulture);

    [Fact]
    public void H1_A_rule_fires_once_on_each_edge_it_is_told_to()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Rules.Id;
        int fired = Num(game, id, "fired"), falls = Num(game, id, "falls");

        game.Ask($"pset {id} ruleGo true");
        Assert.Equal((fired + 1).ToString(), VarSoon(game, id, "fired", (fired + 1).ToString()));
        // Control: still true is not another edge.
        game.Ask("wait 1", 10);
        Assert.Equal(fired + 1, Num(game, id, "fired"));
        Assert.Equal(falls, Num(game, id, "falls"));

        game.Ask($"pset {id} ruleGo false");
        Assert.Equal((falls + 1).ToString(), VarSoon(game, id, "falls", (falls + 1).ToString()));
        game.Ask($"pset {id} ruleGo true");
        Assert.Equal((fired + 2).ToString(), VarSoon(game, id, "fired", (fired + 2).ToString()));
        game.Ask($"pset {id} ruleGo false");
    }

    [Fact]
    public void H2_A_rule_with_branches_runs_the_first_that_holds_once_each_time_it_changes()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Rules.Id;
        int a = Num(game, id, "branchA"), b = Num(game, id, "branchB"), other = Num(game, id, "branchElse");

        game.Ask($"pset {id} mode a");
        Assert.Equal((a + 1).ToString(), VarSoon(game, id, "branchA", (a + 1).ToString()));
        game.Ask($"pset {id} mode b");
        Assert.Equal((b + 1).ToString(), VarSoon(game, id, "branchB", (b + 1).ToString()));
        game.Ask($"pset {id} mode zzz");
        Assert.Equal((other + 1).ToString(), VarSoon(game, id, "branchElse", (other + 1).ToString()));
        // Control: only the one that held ran, once each.
        game.Ask("wait 1", 10);
        Assert.Equal(a + 1, Num(game, id, "branchA"));
        Assert.Equal(b + 1, Num(game, id, "branchB"));
        Assert.Equal(other + 1, Num(game, id, "branchElse"));
    }

    [Fact]
    public void H3_A_rule_for_each_value_acts_for_that_value_alone_and_one_on_scene_load_ran()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Rules.Id;
        Assert.Equal("true", game.PackVar(id, "loaded"));

        game.Ask($"pset {id} go_Ann true");
        Assert.Equal("1", VarSoon(game, id, "count_Ann", "1"));
        // Control: Bob's own count did not move.
        game.Ask("wait 1", 10);
        Assert.Equal("0", game.PackVar(id, "count_Bob"));
        game.Ask($"pset {id} go_Bob true");
        Assert.Equal("1", VarSoon(game, id, "count_Bob", "1"));
        Assert.Equal("1", game.PackVar(id, "count_Ann"));
    }

    [Fact]
    public void H4_A_dialogue_set_for_condition_debugging_logs_its_conditions_with_no_key_and_an_unflagged_one_does_not()
    {
        // Until 1.10.0 only F12 logged it, and F12 is compiled into Debug builds alone: the tick did nothing in the
        // plugin players and authors install. It now logs when the conditions flip and once as the world loads.
        if (Off) return;
        _s.InGame();
        string log = System.IO.Path.Combine(GameRun.TestCopy!, "BepInEx", "LogOutput.log");
        string text;
        using (var f = new System.IO.FileStream(log, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                                                System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
        using (var r = new System.IO.StreamReader(f)) text = r.ReadToEnd();
        string id = InGameSuitePacks.Rules.Id;
        int flagged = text.Split('\n').Count(l => l.Contains("[CondDebug] ── " + id + ".debugflag"));
        _out.WriteLine($"debugflag: {flagged} breakdown(s) in the log");
        Assert.Equal(1, flagged);   // once as the world loaded - not once a frame
        // Control: the same dialogue, not flagged.
        Assert.DoesNotContain("[CondDebug] ── " + id + ".debugplain", text);
    }

    // ── I: installing ────────────────────────────────────────────────────

    [Fact]
    public void I1_A_pack_the_game_has_loaded_can_be_exported_over_while_the_game_runs()
    {
        // Until 1.10.0 the plugin kept every pack it loaded open for reading alone, for the whole session: an
        // export of a change over it failed with "being used by another process" until the game was closed. It now
        // shares the file for deleting, so the export replaces it and the next save loaded reads the new one.
        if (Off) return;
        var game = _s.InGame();
        string id = InGameSuitePacks.Rules.Id;
        string installed = Path.Combine(GameRun.TestCopy!, "Mods", id + PackExporter.FileExtension);
        string folder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "packs", id);
        Assert.True(Directory.Exists(folder), "the suite's own copy of the pack is not where it was exported from");

        // The game has the file open: nobody can have it to themselves.
        Assert.ThrowsAny<IOException>(() =>
        {
            using var alone = new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.None);
        });
        // Control: held as the plugin held it until now, the editor's export cannot replace it.
        using (new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => PackExporter.Export(folder, installed));

        // The game holding it does not stop the export.
        var result = PackExporter.Export(folder, installed);
        _out.WriteLine("exported over the pack the game has loaded: " + result);
        Assert.True(File.Exists(installed));
        // And the game carries on with the copy it opened.
        Assert.Equal("true", game.PackVar(id, "loaded"));
    }

    [Fact]
    public void I2_The_tutorials_practice_art_a_pack_leaves_out_is_drawn_from_the_plugins_own_copy()
    {
        // 1.10.0: the export leaves the practice art out when it is the file ModForge ships, and the plugin carries
        // the same files beside it (BepInEx/plugins/SMSModForge/TutorialArt).
        if (Off) return;
        var game = _s.InGame();
        string installed = Path.Combine(GameRun.TestCopy!, "Mods", InGameSuitePacks.Story.Id + PackExporter.FileExtension);
        using (var file = new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var zip = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Read))
            Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith(SMSModForge.Shared.TutorialArt.Folder + "/", StringComparison.OrdinalIgnoreCase));

        // What the picture holds where the bridge reads it: the middle texel, Unity counting rows from the bottom.
        var png = new System.Windows.Media.Imaging.PngBitmapDecoder(
            new Uri(Path.Combine(SMSModForge.Tutorials.TutorialAssets.SourceRoot, InGameSuitePacks.PracticeScene.Replace('/', Path.DirectorySeparatorChar))),
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(png, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var px = new byte[4];
        bgra.CopyPixels(new System.Windows.Int32Rect(w / 2, h - 1 - h / 2, 1, 1), px, 4, 0);
        string expected = $"#{px[2]:X2}{px[1]:X2}{px[0]:X2}{px[3]:X2}";

        Play(game, "showpractice");
        string drawn = Field(Find(game, Scenes + "practice/Core/Art"), "texel");
        _out.WriteLine($"the practice scene's middle texel: {drawn}, the shipped picture's {expected}");
        Assert.Equal(expected, drawn);
        // Control: a practice file that is nowhere has nothing to draw - the scene is not even built without art.
        Play(game, "showpracticemissing");
        string missing;
        try { missing = Field(Find(game, Scenes + "practicemissing/Core/Art"), "texel"); }
        catch (BridgeError) { missing = "(no scene: no art)"; }
        _out.WriteLine("the scene whose practice file is nowhere: " + missing);
        Assert.NotEqual(expected, missing);
        Play(game, "hidescenes");
    }

    // ── J: dialogues ─────────────────────────────────────────────────────

    [Fact]
    public void J1_A_conversation_offers_only_the_choices_whose_conditions_hold_and_follows_the_one_taken()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Talk.Id;
        int started = Num(game, id, "started"), finished = Num(game, id, "finished");
        game.Ask($"pset {id} gateOk false");

        SeeConversationsThrough(game);
        Begin(game, id, "flow");
        string line = UntilChoices(game, 2);
        _out.WriteLine(line);
        Assert.Contains("Pick one.", LineParts(line, "line"));
        var offered = LineParts(line, "choice");
        Assert.Contains("Left", offered);
        Assert.Contains("Right", offered);
        Assert.DoesNotContain("Secret", offered);      // control: its condition fails
        Assert.Equal(started + 1, Num(game, id, "started"));

        game.Ask("click Right");
        Assert.Equal("right", VarSoon(game, id, "took", "right"));
        // The line after it, with the pack's variable written into it.
        line = game.AskUntil("line", l => LineParts(l, "line").Contains("You took right."), 8);
        Assert.Contains("You took right.", LineParts(line, "line"));
        SeeConversationsThrough(game);
        Assert.Equal(finished + 1, Num(game, id, "finished"));

        game.Ask($"pset {id} gateOk true");
        Begin(game, id, "flow");
        line = UntilChoices(game, 3);
        Assert.Contains("Secret", LineParts(line, "choice"));
        game.Ask("click Secret");
        Assert.Equal("secret", VarSoon(game, id, "took", "secret"));
        SeeConversationsThrough(game);
    }

    [Fact]
    public void J2_A_jump_goes_to_its_tagged_line_past_the_ones_between()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Talk.Id;
        SeeConversationsThrough(game);
        Begin(game, id, "jumpy");
        SeeConversationsThrough(game);
        Assert.Equal("true", VarSoon(game, id, "jumped", "true"));
        Assert.Equal("false", game.PackVar(id, "skipped"));     // control: the line between never ran
    }

    [Fact]
    public void J2b_A_lines_children_all_play_in_order_and_an_exit_ends_the_conversation_there()
    {
        // The flow the variable research assumes (Tools/Research/Variables,
        // brief-hints.md), measured: both plain children play; an Exit in a first
        // child stops the second; an Exit in a first root stops the second root.
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Talk.Id;
        SeeConversationsThrough(game);
        Begin(game, id, "kids");
        SeeConversationsThrough(game);
        Assert.Equal("true", VarSoon(game, id, "kidA", "true"));
        Assert.Equal("true", VarSoon(game, id, "kidB", "true"));     // the second child played too

        Begin(game, id, "kidexit");
        SeeConversationsThrough(game);
        Assert.Equal("true", VarSoon(game, id, "exitA", "true"));    // control: the exiting child ran
        Assert.Equal("false", game.PackVar(id, "exitB"));

        Begin(game, id, "rootexit");
        SeeConversationsThrough(game);
        Assert.Equal("true", VarSoon(game, id, "rootExit", "true"));
        Assert.Equal("false", game.PackVar(id, "rootAfter"));
    }

    [Fact]
    public void J3_A_random_line_picks_one_of_its_lines_each_time()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Talk.Id;
        SeeConversationsThrough(game);
        int runs = 0;
        while (runs < 12 && (Num(game, id, "rolledA") == 0 || Num(game, id, "rolledB") == 0))
        {
            Play(game, "dice", id);
            runs++;
        }
        int a = Num(game, id, "rolledA"), b = Num(game, id, "rolledB");
        _out.WriteLine($"{runs} rolls: A {a}, B {b}");
        Assert.True(a > 0 && b > 0, $"In {runs} rolls one side never came up (A {a}, B {b}).");
        Assert.Equal(runs, a + b);      // one line a roll, never both
    }

    [Fact]
    public void J4_A_conversation_starts_by_itself_once_its_start_conditions_hold()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Talk.Id;
        SeeConversationsThrough(game);
        game.Ask("wait 1", 10);
        Assert.Equal("false", game.PackVar(id, "autoRan"));     // control: not before
        game.Ask($"pset {id} autoGo true");
        Assert.Equal("true", VarSoon(game, id, "autoRan", "true", 8));
        SeeConversationsThrough(game);
    }

    [Fact]
    public void J5_A_conversation_waiting_on_arrival_never_runs_over_one_the_game_starts_there()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Talk.Id;
        string gas = "vanilla:" + InGameSuitePacks.Talk.ArrivalLevel;
        string away = "vanilla:" + InGameSuitePacks.World.Store;

        // The car wash's day: unlocked, not cooling down, a Monday, and the
        // one branch before it that would take the arrival instead closed.
        // Put back as found afterwards: later areas play the same save.
        var carWash = new[] { ("specialcarwashunlocked", "true"), ("special-carwashcd", "false"), ("Day", "1"), ("Astrid-photoqueststart", "true") };
        var before = carWash.Select(v => (v.Item1, Value: game.Ask("get " + v.Item1).Split(" = ")[1])).ToList();
        try
        {
            Travel(game, away, InGameSuitePacks.World.Store);
            foreach (var (name, value) in carWash) game.Ask($"set {name} {value}");
            game.Ask($"pset {id} arriveGo true");

            string timeline = Timeline(game, "carwash", "travel " + gas);
            Assert.Matches(@"on screen: 8_Room_Talk/Gasstation/Carwash_event_", timeline);   // control: the game's own did start
            Assert.DoesNotMatch(@"on screen: \S*suitetalk_arrive", timeline);
            SeeConversationsThrough(game);
            game.Ask("wait 4", 10);
            // It lost its turn to the game's - no box checked - until the next arrival.
            Assert.Equal("0", game.PackVar(id, "arrived"));

            // Control: an arrival the game starts nothing on (the car wash
            // now cooling down) is where it plays.
            Travel(game, away, InGameSuitePacks.World.Store);
            string plain = Timeline(game, "plain", "travel " + gas);
            Assert.DoesNotMatch(@"on screen: 8_Room_Talk/", plain);
            Assert.Equal("1", VarSoon(game, id, "arrived", "1", 5));
            // And no later for the waiting: the hold after arrival counts from
            // the level coming up (1.8E: 1.42 s from it to the pack's line; a
            // hold counted from the journey's end made it 2.43 s).
            double up = At(plain, "levels: " + InGameSuitePacks.Talk.ArrivalLevel);
            double on = At(plain, Regex.Escape("on screen: SMSModForge_suitetalk_Dialogues/suitetalk_arrive"));
            Assert.True(on - up <= 1.8, $"The pack's line came {on - up:0.00} s after the gas station did.");
            SeeConversationsThrough(game);
        }
        finally
        {
            game.Ask($"pset {id} arriveGo false");
            foreach (var (name, value) in before.Where(b => b.Value != "(none)")) game.Ask($"set {name} {value}");
        }
    }

    /// <summary>When a timeline first says <paramref name="what"/> (a pattern)
    /// later than <paramref name="after"/>, in seconds from its start.</summary>
    private static double At(string timeline, string what, double after = -1)
    {
        foreach (Match m in Regex.Matches(timeline, @"^\+(\d+\.\d+) f\d+ " + what, RegexOptions.Multiline))
        {
            double at = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (at > after) return at;
        }
        throw new XunitException($"The timeline never says '{what}' after {after} s:\n{timeline}");
    }

    [Fact]
    public void J6_A_line_plays_another_dialogue_after_its_own_and_one_of_the_games_and_says_when_each_ended()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Talk.Id;
        string gameConversation = InGameSuitePacks.Talk.GameConversation;
        SeeConversationsThrough(game);
        Assert.Equal("0", game.PackVar(id, "playedTarget"));

        // "caller"'s line asks for "target": not while "caller" is up...
        Begin(game, id, "caller");
        Assert.Equal("0", game.PackVar(id, "playedTarget"));
        Assert.Equal("", game.PackVar(id, "afterTarget"));
        // ...but once it has ended - and then says so when "target" has.
        Assert.Equal("1", VarSoon(game, id, "playedTarget", "1", 10));
        Assert.Equal("done", VarSoon(game, id, "afterTarget", "done", 10));

        // One of the game's own conversations, the same way.
        Begin(game, id, "callgame");
        string line = game.AskUntil("line", l => l.StartsWith(gameConversation + " ", StringComparison.Ordinal), 12);
        _out.WriteLine(line);
        Assert.StartsWith(gameConversation + " ", line);
        Assert.Equal("", game.PackVar(id, "afterGame"));   // not while it is on
        SeeConversationsThrough(game);
        Assert.Equal("done", VarSoon(game, id, "afterGame", "done", 10));
    }

    /// <summary>What <c>timeline</c> recorded while <paramref name="command"/>
    /// ran, read back from its file and written to the test's output.</summary>
    private string Timeline(GameRun game, string name, string command, int seconds = 12)
    {
        var said = game.Send(seconds + 30, $"timeline {seconds} {name}", command);
        string answer = said.Last(l => l.Contains(" ok timeline ", StringComparison.Ordinal));
        string file = answer.Substring(answer.IndexOf("-> ", StringComparison.Ordinal) + 3).Trim();
        string text = File.ReadAllText(file);
        _out.WriteLine("── timeline " + name + " (" + command + ")");
        _out.WriteLine(text);
        return text;
    }

    // ── K: characters, and the game's own conversations ──────────────────

    [Fact]
    public void K1_A_pack_characters_name_is_drawn_in_its_own_colour()
    {
        if (Off) return;
        var game = _s.InGame();
        SeeConversationsThrough(game);
        Begin(game, "gamecheck", "sounds");
        string speaker = game.AskUntil("speaker", s => s.Contains("Tester"), 8);
        _out.WriteLine(speaker);
        Assert.Matches("Tester'[^|]*drawn #FF8800", speaker);
        game.Ask("wait-dialogue-end 20", 30);
    }

    [Fact]
    public void K2_When_two_packs_colour_one_of_the_games_characters_the_one_loaded_later_shows()
    {
        if (Off) return;
        var game = _s.InGame();
        SeeConversationsThrough(game);
        game.Ask("vanilla-dialogue 8_Room_Talk/Kitchen/Anna_DishesDecline");
        string speaker = game.AskUntil("speaker", s => s.Contains("Anna"), 8);
        _out.WriteLine(speaker);
        // gameorder loads after gamecheck (the names' order, nobody has moved them).
        Assert.Matches("Anna'[^|]*drawn #00C000", speaker);
        Assert.DoesNotMatch("Anna'[^|]*drawn #FF00FF", speaker);     // control: the earlier pack's
        SeeConversationsThrough(game);
    }

    [Fact]
    public void K8_A_new_name_for_one_of_the_games_characters_is_shown_over_every_line_they_speak_and_nobody_elses()
    {
        if (Off) return;
        var game = _s.InGame();
        SeeConversationsThrough(game);

        // The game's own line. gamecheck calls Josef "Joe", gameorder - loaded
        // later - "Joseph"; his name colour, the game's, follows the new name.
        // (The label's text opens with the game's own "<noparse></noparse>", so a
        // name is matched by what follows it.)
        game.Ask("vanilla-dialogue " + InGameCheckPacksTests.JosefTalks);
        string theirs = game.AskUntil("speaker", s => s.Contains("Jose") || s.Contains("Joe'"), 8);
        _out.WriteLine("the game's line: " + theirs);
        Assert.Matches("Joseph'[^|]*drawn #FFB999", theirs);
        Assert.DoesNotContain("Joe'", theirs);       // control: the earlier pack's
        Assert.DoesNotContain("Josef'", theirs);     // ...and the game's own
        SeeConversationsThrough(game);

        // A pack's line he speaks: gamecheck's, under gameorder's name for him.
        Begin(game, "gamecheck", "names");
        string packs = game.AskUntil("speaker", s => s.Contains("Joseph'"), 6);
        _out.WriteLine("gamecheck's line: " + packs);
        Assert.Contains("Joseph'", packs);

        // Control: gamecheck's own character called Josef is somebody else, and
        // keeps the name.
        string own = game.AskUntil("speaker", s => s.Contains("Josef'"), 8);
        _out.WriteLine("gamecheck's own Josef: " + own);
        Assert.Contains("Josef'", own);
        game.Ask("wait-dialogue-end 20", 30);
    }

    [Fact]
    public void K9_A_name_token_in_a_packs_line_reads_as_that_characters_name_now()
    {
        if (Off) return;
        var game = _s.InGame();
        SeeConversationsThrough(game);
        Begin(game, "gamecheck", "names");
        string line = game.AskUntil("line", l => l.Contains("stays."), 6);
        _out.WriteLine(line);
        // The game's Josef by the name gameorder gives him; gamecheck's own Josef
        // by his, his key in other capitals too (as the editor and Validate read
        // it; the game left that one as written before 1.9.0's review); and - the
        // control - a key nobody has stays as written.
        Assert.Contains(InGameCheckPacksTests.NamesLineShown, line);
        game.Ask("wait-dialogue-end 20", 30);

        // Not only lines: suitequest's quest title names him too, as the journal
        // reads it - a third pack, taking gameorder's name for him.
        string quest = game.Ask("quest " + InGameSuitePacks.Quests.Id + " chase");
        _out.WriteLine(quest);
        Assert.Contains("title=" + InGameSuitePacks.Quests.ChaseTitleShown, quest);
    }

    /// <summary>
    /// A name token for one of the game's characters reads as their name in a pack that touches nobody (1.10.0,
    /// reported by a pack's author: "in-game it printed {name:anna} literally"): a line with nothing else to fill
    /// in, and one beside a pack variable. Control: the line's words around the tokens are the pack's (so the line
    /// read is that line), and the variable beside the token is filled in from the pack (3).
    /// </summary>
    [Fact]
    public void K9b_A_name_token_for_one_of_the_games_characters_reads_as_their_name_in_a_pack_that_touches_nobody()
    {
        if (Off) return;
        var game = _s.InGame();
        SeeConversationsThrough(game);
        Begin(game, InGameSuitePacks.BareNames.Id, "plain");
        string plain = game.AskUntil("line", l => l.Contains("meet"), 6);
        _out.WriteLine(plain);
        Assert.Contains(InGameSuitePacks.BareNames.PlainShown, plain);
        Assert.DoesNotContain("{name:", plain);
        game.Ask("wait-dialogue-end 20", 30);

        Begin(game, InGameSuitePacks.BareNames.Id, "counted");
        string counted = game.AskUntil("line", l => l.Contains("times"), 6);
        _out.WriteLine(counted);
        Assert.Contains(InGameSuitePacks.BareNames.CountedShown, counted);
        game.Ask("wait-dialogue-end 20", 30);
    }

    [Fact]
    public void K10_A_new_name_reaches_the_games_own_words_where_they_mean_that_character()
    {
        if (Off) return;
        var game = _s.InGame();
        string Text(string source, string where)
        {
            string said = game.Ask($"game-text {source} {where}");
            _out.WriteLine(said);
            Assert.DoesNotContain("error", said);
            return said;
        }
        bool Says(string text, string word) => System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<!\w)" + word + @"(?!\w)");

        // One of the game's lines naming Josef, whom gameorder calls Joseph.
        string line = Text("line", "8_Room_Talk/Kitchen/AboutAdrian#-1600756773");
        Assert.True(Says(line, "Joseph") && !Says(line, "Josef"), line);

        // One of the game's quests (the one suitequest adds a task to): its paragraph.
        string quest = Text("quest", "Explore The City#description");
        Assert.True(Says(quest, "Joseph") && !Says(quest, "Josef"), quest);

        // One of the game's screens, naming Celeste, whom gameorder calls Celestia.
        string screen = Text("ui", "9_MainCanvas/Payout/CelesteStoleFollowers/Text (TMP)");
        Assert.Contains("Celestia lured away", screen);

        // Controls. A line of the game's that gamecheck rewrote is gamecheck's, and
        // keeps its words; a line naming Anna, whom nobody renames, keeps her name.
        string rewritten = Text("line", InGameCheckPacksTests.RewrittenConversation + "#" + InGameCheckPacksTests.RewrittenNode);
        Assert.EndsWith("= " + InGameCheckPacksTests.RewrittenLine, rewritten);
        string anna = Text("line", "8_Room_Talk/Kitchen/AnnaGropeLove#-378333926");
        Assert.True(Says(anna, "Anna"), anna);
    }

    [Fact]
    public void K3_A_line_a_pack_adds_to_one_of_the_games_conversations_is_said_in_its_place()
    {
        if (Off) return;
        var game = _s.InGame();
        const string path = "8_Room_Talk/Kitchen/Annanotthere";
        SeeConversationsThrough(game);
        var source = VanillaDialogueCatalog.Find(path);
        Assert.True(source != null, "The editor's catalogue has no " + path);
        game.Ask("vanilla-dialogue " + path);
        var said = HearConversation(game);
        _out.WriteLine(string.Join(" / ", said));
        int added = said.FindIndex(l => l.Contains("from upstairs"));
        Assert.True(added >= 1, "The pack's line was not said, or came first: " + string.Join(" / ", said));
        // Control: the game's own line before it is still the game's.
        Assert.DoesNotContain("from upstairs", said[added - 1]);
    }

    [Fact]
    public void K4_A_pack_characters_own_bust_comes_out_on_her_line_and_changes_to_the_outfit_a_variable_names()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Talk.Id;
        string day = "2_Bust_Manager/" + InGameSuitePacks.Talk.BeaDay, night = "2_Bust_Manager/" + InGameSuitePacks.Talk.BeaNight;
        SeeConversationsThrough(game);
        // Control: built, and nobody is wearing either yet.
        Assert.StartsWith("False/", Field(Find(game, day), "active"));
        Assert.StartsWith("False/", Field(Find(game, night), "active"));

        Begin(game, id, "dress");
        game.AskUntil("line", l => LineParts(l, "line").Contains("Day clothes."), 6);
        Assert.True(ActiveSoon(game, day, true), "Bea's first outfit is not out on her line.");
        Assert.StartsWith("False/", Field(Find(game, night), "active"));
        Assert.Equal("#3366FFFF", Field(Find(game, BustSprite(game, day)), "texel"));

        game.Ask("next");
        game.AskUntil("line", l => LineParts(l, "line").Contains("Changed."), 6);
        Assert.True(ActiveSoon(game, night, true), "The outfit beaOutfit names is not out.");
        Assert.False(ActiveSoon(game, day, false), "The first outfit stayed out beside the second.");
        Assert.Equal("#112244FF", Field(Find(game, BustSprite(game, night)), "texel"));
        SeeConversationsThrough(game);
    }

    [Fact]
    public void K5_A_picture_a_pack_puts_on_one_of_the_games_busts_is_drawn_there_and_only_there()
    {
        if (Off) return;
        var game = _s.InGame();
        string over = "2_Bust_Manager/" + InGameSuitePacks.World.OverriddenBust;
        string left = "2_Bust_Manager/" + InGameSuitePacks.World.UntouchedBust;
        _out.WriteLine(over + " / " + left);
        Assert.Equal("#AA33AAFF", Field(Find(game, BustSprite(game, over)), "texel"));
        // Control: her other outfit keeps the game's own picture.
        Assert.NotEqual("#AA33AAFF", Field(Find(game, BustSprite(game, left)), "texel"));
    }

    [Fact]
    public void K6_A_dialogue_that_leaves_its_busts_at_the_end_takes_its_speakers_off_and_one_that_does_not_leaves_them()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Talk.Id;
        string day = "2_Bust_Manager/" + InGameSuitePacks.Talk.BeaDay, night = "2_Bust_Manager/" + InGameSuitePacks.Talk.BeaNight;
        SeeConversationsThrough(game);

        // Control: "dress" does not leave its busts - Bea's last outfit stays up.
        Begin(game, id, "dress");
        SeeConversationsThrough(game);
        game.Ask("wait 1", 10);
        Assert.True(ActiveSoon(game, night, true), "Bea's bust went, though 'dress' does not leave its busts.");

        // "bow" does: once it has ended, nobody who spoke in it is still up.
        Begin(game, id, "bow");
        Assert.True(ActiveSoon(game, day, true), "Bea is not out on her line.");
        game.Ask("wait-dialogue-end 20", 30);
        Assert.False(ActiveSoon(game, day, false), "Bea's bust stayed up after a dialogue that leaves its busts.");
        Assert.StartsWith("False/", Field(Find(game, night), "active"));
    }

    /// <summary>
    /// A face a pack's line gives one of the game's busts stays on it, also on a
    /// portrait that follows another character's number (1.9.0): Chloe's copies
    /// Charlotte's on every variable write, and until 1.9.0 ModForge wrote
    /// Chloe's own, so her face went back at the next write.
    /// </summary>
    [Fact]
    public void K7_A_face_given_to_a_game_portrait_that_follows_another_characters_number_stays()
    {
        if (Off) return;
        var game = _s.InGame();
        string faces = "2_Bust_Manager/" + InGameSuitePacks.Talk.FollowerBust + "/MBase1/Expressions";
        SeeConversationsThrough(game);
        game.Ask("gset Expressions[Charlotte] 0");

        Begin(game, InGameSuitePacks.Talk.Id, "face");
        game.AskUntil("line", l => LineParts(l, "line").Contains("Smiling."), 6);
        Assert.True(ActiveSoon(game, faces + "/Happy", true), "Chloe's happy face is not on her line.");

        // Any variable write runs the portrait's trigger, which copies its number again.
        // An unused variable of the game's, so nothing else reacts.
        game.Ask("gset Inventory[black-lingerie] true");
        game.Ask("gset Inventory[black-lingerie] false");
        game.Ask("wait 1", 10);
        string number = game.Ask("gget Expressions[Charlotte]");
        _out.WriteLine(number);
        Assert.Matches(@"= 1(\.0+)?\s*$", number);
        Assert.True(ActiveSoon(game, faces + "/Happy", true), "Chloe's face went back after a variable write.");

        // Control: the portrait follows Charlotte's number - set it to 2 and the face is another one.
        game.Ask("gset Expressions[Charlotte] 2");
        game.Ask("wait 1", 10);
        Assert.False(ActiveSoon(game, faces + "/Happy", false), "The portrait does not follow Charlotte's number.");
        game.Ask("gset Expressions[Charlotte] 0");
        SeeConversationsThrough(game);
    }

    /// <summary>The object a bust draws its body on - MBase1 on most of the
    /// game's rigs, another name on some - by what is under the bust.</summary>
    private static string BustSprite(GameRun game, string bust)
    {
        string children = Field(game.Ask("find " + bust), "children");
        var first = System.Text.RegularExpressions.Regex.Match(children, @"\[([^,\]]+)");
        string root = children.Contains("MBase1") ? "MBase1" : first.Groups[1].Value.Replace("(off)", "");
        return bust + "/" + root;
    }

    /// <summary>Every line a conversation says, in order, seen through to
    /// its end as a player would.</summary>
    private static List<string> HearConversation(GameRun game)
    {
        var said = new List<string>();
        for (int i = 0; i < 80; i++)
        {
            string line = game.Ask("line");
            if (line.StartsWith("(none)", StringComparison.Ordinal))
            {
                if (said.Count > 0) return said;
                game.Ask("wait 0.3", 10);
                continue;
            }
            foreach (var words in LineParts(line, "line"))
                if (said.Count == 0 || said[^1] != words) said.Add(words);
            var choices = LineParts(line, "choice");
            if (choices.Count > 0) game.Ask("click " + choices[^1]);
            else if (i % 3 == 2) game.Ask("next");      // a line nobody times goes on with a click
            game.Ask("wait 0.4", 10);
        }
        throw new XunitException("The conversation would not end: " + string.Join(" / ", said));
    }

    // ── L: sound (the game's own volume up only while it is listened to) ─

    /// <summary>What a <c>listen</c> heard, row by row (its .tsv beside the
    /// bridge's answers): seconds, source, peak, pitch.</summary>
    private static List<(double T, string Source, double Peak, double Hz)> Heard(GameRun game, string name)
    {
        var rows = new List<(double, string, double, double)>();
        foreach (var row in File.ReadAllLines(Path.Combine(game.BridgeFolder, "listen-" + name + ".tsv")).Skip(1))
        {
            var c = row.Split('\t');
            rows.Add((double.Parse(c[0], CultureInfo.InvariantCulture), c[1],
                      double.Parse(c[3], CultureInfo.InvariantCulture), double.Parse(c[5], CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    /// <summary>One source's sound in bursts - a gap of a third of a second
    /// ends one - each as its start and loudest peak.</summary>
    private static List<(double Start, double Peak)> Bursts(IEnumerable<(double T, string Source, double Peak, double Hz)> rows)
    {
        var bursts = new List<(double Start, double Peak)>();
        double last = double.NegativeInfinity;
        foreach (var r in rows.OrderBy(r => r.T))
        {
            if (r.T - last > 0.33) bursts.Add((r.T, r.Peak));
            else if (r.Peak > bursts[^1].Peak) bursts[^1] = (bursts[^1].Start, r.Peak);
            last = r.T;
        }
        return bursts;
    }

    private static void Ratio(double expected, double actual, string what)
    {
        if (Math.Abs(actual / expected - 1) > 0.15)
            throw new XunitException($"{what}: expected {expected:0.##}x, the game played {actual:0.##}x");
    }

    [Fact]
    public void L1_A_lines_sounds_play_by_the_square_of_their_volume_and_a_line_with_no_sound_is_silent()
    {
        if (Off) return;
        var game = _s.InGame();
        SeeConversationsThrough(game);
        game.Ask("wait 1.5", 10);       // any conversation's closing fade
        game.Ask("mute off");
        try { game.Send(30, "listen 9.5 suitesounds", "dialogue gamecheck sounds"); }
        finally { game.Ask("mute on"); }
        var rows = Heard(game, "suitesounds");
        var clinks = Bursts(rows.Where(r => r.Source.EndsWith(" clink", StringComparison.Ordinal)));
        var buzzes = Bursts(rows.Where(r => r.Source.EndsWith(" buzz", StringComparison.Ordinal)));
        _out.WriteLine("clinks " + string.Join(", ", clinks) + "; buzzes " + string.Join(", ", buzzes));
        Assert.Equal(3, clinks.Count);
        Assert.Single(buzzes);
        double plain = clinks[0].Peak;
        Ratio(2.25, clinks[1].Peak / plain, "the 150 % clink");
        Ratio(0.25, clinks[2].Peak / plain, "the 50 % clink");
        Ratio(4, buzzes[0].Peak / plain, "the 200 % buzz");
        // Control: the line whose pattern has no sound plays nothing - nothing
        // of the pack's between the third clink and the buzz.
        Assert.True(buzzes[0].Start - clinks[2].Start > 2.5, "Something was heard during the silent line.");
        Assert.Empty(rows.Where(r => r.T > clinks[2].Start + 0.6 && r.T < buzzes[0].Start - 0.1
                                     && (r.Source.EndsWith(" clink", StringComparison.Ordinal) || r.Source.EndsWith(" buzz", StringComparison.Ordinal))));
        // And they are the pack's tones: 440 Hz and 880 Hz.
        Ratio(440, rows.Where(r => r.Source.EndsWith(" clink", StringComparison.Ordinal) && r.Peak > 0.5 * plain).Average(r => r.Hz), "the clink's pitch");
    }

    [Fact]
    public void L2_Pack_music_plays_at_its_volume_as_plain_gain()
    {
        if (Off) return;
        var game = _s.InGame();
        SeeConversationsThrough(game);
        game.Ask("wait 1.5", 10);       // any conversation's closing fade
        game.Ask("mute off");
        try { game.Send(30, "listen 8.5 suitemusic", "dialogue gamecheck music"); }
        finally { game.Ask("mute on"); }
        var rows = Heard(game, "suitemusic");
        double Loudest(string track) => rows.Where(r => r.Source.Contains("/" + track + " ", StringComparison.Ordinal))
                                            .Select(r => r.Peak).DefaultIfEmpty(0).Max();
        double half = Loudest("ToneHalf"), one = Loudest("ToneOne"), two = Loudest("ToneTwo");
        _out.WriteLine($"half {half}, one {one}, two {two}");
        Assert.True(one > 0, "The 100 % track was not heard.");
        Ratio(0.5, half / one, "the 50 % track");
        Ratio(2, two / one, "the 200 % track");
        Ratio(330, rows.Where(r => r.Source.Contains("/ToneOne ", StringComparison.Ordinal) && r.Peak > 0.5 * one).Average(r => r.Hz), "the tone's pitch");
        game.Ask("wait-dialogue-end 10", 20);
    }

    // ── M: transitions and Wait ──────────────────────────────────────────

    [Fact]
    public void M1_Waits_hold_the_line_for_their_time_and_transitions_cover_the_dialogue_box_before_it_goes_on()
    {
        if (Off) return;
        var game = _s.InGame();
        SeeConversationsThrough(game);
        game.Ask("wait 1.5", 10);       // any conversation's closing fade
        var said = game.Send(30, "watch 8 gamecheck step", "dialogue gamecheck timing");
        string watched = said.Last(l => l.Contains(" ok watch "));
        _out.WriteLine(watched);
        double At(string what)
        {
            var m = System.Text.RegularExpressions.Regex.Match(watched, @"\+([\d.]+) s " + System.Text.RegularExpressions.Regex.Escape(what) + "(;|$)");
            if (!m.Success) throw new XunitException($"No '{what}' in: {watched}");
            return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        double one = At("step=1"), two = At("step=2"), three = At("step=3"), four = At("step=4");
        Near(1f, (float)(two - one), "the 1 s Wait", 0.25f);
        Near(1f, (float)(three - two), "the fade to black covering", 0.25f);
        Near(2.25f, (float)(four - three), "the 1.5 s Wait and the words' cover", 0.3f);
        // Each transition is drawn over the dialogue box, not under it: its
        // canvas sorts above the box's (read once the line is on screen - its
        // actions, the transitions among them, run before it shows).
        var orders = System.Text.RegularExpressions.Regex.Matches(watched, @"transitions=\d+ - ([^;]*)")
                         .SelectMany(m => System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, @"order (-?\d+)").Select(o => int.Parse(o.Groups[1].Value)))
                         .ToList();
        Assert.NotEmpty(orders);
        string line = game.AskUntil("line", l => LineParts(l, "box").Count > 0, 6);
        var box = LineParts(line, "box");
        Assert.True(box.Count > 0, "The timing line's box never came up: " + line);
        int boxOrder = int.Parse(box[0].Replace("order ", ""), CultureInfo.InvariantCulture);
        Assert.All(orders, o => Assert.True(o > boxOrder, $"A transition sorts at {o}, not above the dialogue box at {boxOrder}."));
        game.Ask("wait-dialogue-end 15", 25);
    }

    // ── N: actions on a place's objects ──────────────────────────────────

    [Fact]
    public void N1_A_line_moves_fades_repaints_shows_and_spins_a_places_objects_and_puts_them_back()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.World.Id;
        // In the room: a move or a spin happens while its level is up (they
        // run on the objects' own frames, which an unseen room does not have).
        TravelToRoom(game);
        // Control: as built, before any action.
        Assert.Equal("#FFCC00FF", Field(Find(game, RoomLevel + "/Hidden"), "texel"));
        Assert.StartsWith("False/", Field(Find(game, RoomLevel + "/Hidden"), "active"));

        Play(game, "moves", id);
        game.Ask("wait 0.5", 10);
        // A moved object is held where it was sent, in the world's own terms.
        var (x, y) = XY(Field(Find(game, RoomLevel + "/Lamp"), "world"));
        Near(3f, x, "Lamp moved to x");
        Near(-1f, y, "Lamp moved to y");
        Assert.Contains("alpha 0.25", Field(Find(game, RoomLevel + "/Holder/Inner"), "sprite"));
        string hidden = Find(game, RoomLevel + "/Hidden");
        Assert.StartsWith("True/", Field(hidden, "active"));
        Assert.Equal("#CC2222FF", Field(hidden, "texel"));
        Assert.Equal("#FFCC00FF", Field(Find(game, RoomLevel + "/Lamp"), "texel"));   // control: only its own picture changed
        float turn1 = float.Parse(Field(Find(game, RoomLevel + "/Holder"), "rotz"), CultureInfo.InvariantCulture);
        game.Ask("wait 0.5", 10);
        float turn2 = float.Parse(Field(Find(game, RoomLevel + "/Holder"), "rotz"), CultureInfo.InvariantCulture);
        float turned = ((turn2 - turn1) % 360f + 360f) % 360f;
        Assert.True(turned > 20f && turned < 90f, $"Spinning at 90 a second, it turned {turned} in half a second.");

        Play(game, "back", id);
        game.Ask("wait 0.5", 10);
        var (hx, hy) = XY(Field(Find(game, RoomLevel + "/Lamp"), "home"));
        Near(1.5f, hx, "Lamp back home x", 0.05f);
        Near(-2f, hy, "Lamp back home y", 0.05f);
        float still1 = float.Parse(Field(Find(game, RoomLevel + "/Holder"), "rotz"), CultureInfo.InvariantCulture);
        game.Ask("wait 0.5", 10);
        Near(still1, float.Parse(Field(Find(game, RoomLevel + "/Holder"), "rotz"), CultureInfo.InvariantCulture), "the stopped spin", 0.5f);
    }

    [Fact]
    public void N2_A_line_sets_an_objects_own_position_and_size_eased_and_lets_go_of_a_held_one()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.World.Id;
        TravelToRoom(game);
        string inner = RoomLevel + "/Holder/Inner";
        // Control: as built - its own size, and somewhere other than where
        // the line puts it (a place converts its objects' positions as it is
        // built, so where that is is read, not assumed).
        var (x0, y0) = XY(Field(Find(game, inner), "local"));
        Assert.True(Math.Abs(x0 - 0.5f) > 0.5f, $"Inner already stands at x {x0}.");
        Assert.Equal("1,1", Field(Find(game, inner), "scale"));

        // On its way: eased over 1.5 s, so part-way after a moment.
        Begin(game, id, "frame");
        game.Ask("wait 0.3", 10);
        var (midScale, _) = XY(Field(Find(game, inner), "scale") + ",0");
        Assert.True(midScale > 1.05f && midScale < 1.95f, $"Part-way through, Inner's scale was {midScale}.");
        game.Ask("wait-dialogue-end 20", 30);
        game.Ask("wait 1.5", 10);

        var (x1, y1) = XY(Field(Find(game, inner), "local"));
        Near(0.5f, x1, "Inner's x framed");
        Near(y0, y1, "Inner's y, left as it was");
        Assert.Equal("2,2", Field(Find(game, inner), "scale"));
        // The Lamp, held by the move before, is let go where it was placed:
        // held, the move's world position would have taken it back.
        game.Ask("wait 0.5", 10);
        Near(7f, XY(Field(Find(game, RoomLevel + "/Lamp"), "local")).X, "Lamp's own x after the hold let go");

        // At once, with no seconds given.
        Play(game, "unframe", id);
        var (x2, _) = XY(Field(Find(game, inner), "local"));
        Near(4f, x2, "Inner's x set again");
        Assert.Equal("1,1", Field(Find(game, inner), "scale"));
    }

    // ── W: what a save keeps (last: it loads the save again) ─────────────

    [Fact]
    public void W1_A_night_keeps_the_packs_saved_variables_and_a_load_brings_back_exactly_what_the_night_kept()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Saving.Id;
        Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        game.Ask($"pset {id} keptNumber 7");
        game.Ask($"pset {id} keptWords kept words");
        game.Ask($"pset {id} keptList a,b");
        game.Ask($"pset {id} lostNumber 7");
        game.Ask($"pset {id} dailyCount 5");
        // The day-change rule has run once already - a pack meeting a save
        // for the first time catches the day up. Put back, it stays put until
        // a day really turns (control).
        game.Ask($"pset {id} dayTurned false");
        game.Ask("wait 1", 10);
        Assert.Equal("false", game.PackVar(id, "dayTurned"));

        // The night, as a player has it: to bed, the payout confirmed, the
        // morning chosen - and the game saves, which is when a pack's
        // variables are committed (the only time they are).
        game.Ask("press 5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas/Player_Room_Buttons/SleepButton");
        PressWhenShown(game, "Confirm");
        // The morning's question arrives with a fade, and a click during it goes
        // nowhere: once it has settled, Relax - again if the night does not go
        // on, as a player clicks again.
        game.AskUntil("find 9_MainCanvas/AfterSleepEvents", a => Field(a, "active").EndsWith("/True", StringComparison.Ordinal), 20);
        string saved = "";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            game.Ask("wait 1.5", 10);
            // After the first, only while the question is still there to answer.
            try { PressWhenShown(game, "Relax", attempt == 0 ? 10 : 2); }
            catch (BridgeError) when (attempt > 0) { }
            saved = game.AskUntil("find 6_Effects/Effect_Canvas/Game_Saved/Saved",
                                  a => Field(a, "active").StartsWith("True", StringComparison.Ordinal), 8);
            if (Field(saved, "active").StartsWith("True", StringComparison.Ordinal)) break;
        }
        Assert.StartsWith("True", Field(saved, "active"));
        game.Ask("wait 2", 10);

        // A new day: what refreshes daily is back to its default, a rule for
        // the day's turn has run, and what does not refresh is as it was.
        Assert.Equal("0", game.PackVar(id, "dailyCount"));
        Assert.Equal("true", VarSoon(game, id, "dayTurned", "true"));
        Assert.Equal("7", game.PackVar(id, "keptNumber"));

        // On disk, in the test save: what is kept, and only that.
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                                   "Arvus Games", "Starmaker Tests", "Saves", "NANOSAVE_0001", "SMSModForge_" + id + ".json");
        Assert.True(File.Exists(file), "No pack file in the test save's slot 1: " + file);
        string written = File.ReadAllText(file);
        _out.WriteLine(written);
        Assert.Contains("\"keptNumber\": \"7\"", written);
        Assert.DoesNotContain("lostNumber", written);

        // A change after the night is not kept: nothing commits it.
        game.Ask($"pset {id} keptNumber 9");

        game.Ask("load");
        game.Ask("wait-ingame 150", 180);
        game.Ask("wait 3", 10);
        // Told once a save: the warning was answered and the night kept that.
        Assert.StartsWith("(none)", game.AskUntil("warning", w => w.StartsWith("open", StringComparison.Ordinal), 4));
        Assert.Equal("7", game.PackVar(id, "keptNumber"));
        Assert.Equal("kept words", game.PackVar(id, "keptWords"));
        Assert.Contains("a", game.PackVar(id, "keptList"));
        Assert.Contains("b", game.PackVar(id, "keptList"));
        Assert.Equal("0", game.PackVar(id, "lostNumber"));        // control: not one a save keeps
    }

    [Fact]
    public void W2_A_pack_exported_again_while_the_game_runs_is_read_at_the_next_load()
    {
        // With I1 (1.10.0): an export replaces the file under the running game, and loading a save reads every
        // pack again - so testing a change is export, then load. The guide and the tutorials say so.
        if (Off) return;
        var game = _s.InGame();
        string id = InGameSuitePacks.Rules.Id;
        string folder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "packs", id);
        string installed = Path.Combine(GameRun.TestCopy!, "Mods", id + PackExporter.FileExtension);
        var pack = PackRepository.Load(folder);
        pack.Variables.Add(InGameSuitePacks.Var("exportedAgain", PackVariableType.String, "yes"));
        PackRepository.Save(pack, folder);
        _out.WriteLine("exported again: " + PackExporter.Export(folder, installed));

        // Control: the game goes on with what it loaded - it has no such variable yet.
        Assert.Throws<BridgeError>(() => game.PackVar(id, "exportedAgain"));

        game.Ask("load");
        game.Ask("wait-ingame 150", 180);
        if (game.AskUntil("warning", w => w.StartsWith("open", StringComparison.Ordinal), 4).StartsWith("open", StringComparison.Ordinal))
            game.Ask("warning go-on");
        game.Ask("wait 2", 10);
        Assert.Equal("yes", game.PackVar(id, "exportedAgain"));
    }

    // ── Z: a restart - the main menu's settings carried into the next game ─

    [Fact]
    public void Z0_After_an_update_with_news_the_notice_says_what_is_new()
    {
        // The short notice after an update (ForgeNotice.Changes) - first sent
        // to players by 1.8.2, where A2 sees only the first start's. The
        // settings of a player who last closed the notice in an older version.
        if (Off) return;
        const string updatedFrom = "1.7.0";
        string current = SMSModForge.Shared.ForgeVersion.Current;
        var news = SMSModForge.Shared.ForgeNotice.NewsSince(updatedFrom, current, SMSModForge.Shared.ForgeNotice.VersionsWithNews);
        Assert.NotEmpty(news);
        var game = _s.Restart(() => File.WriteAllText(Settings,
            Regex.Replace(File.ReadAllText(Settings), @"(?m)^Last shown = [^\r\n]*", "Last shown = " + updatedFrom)));

        string notice = game.AskUntil("notice", n => n.StartsWith("open", StringComparison.Ordinal), 20);
        _out.WriteLine(notice);
        Assert.StartsWith("open", notice);
        Assert.Contains("ModForge was updated to " + current, notice);
        // Every version since, newest first, each with its lines.
        int previous = -1;
        foreach (var version in Enumerable.Reverse(news))
        {
            int at = notice.IndexOf("New in " + version + ":", StringComparison.Ordinal);
            Assert.True(at > previous, $"'New in {version}:' is missing or out of order");
            previous = at;
            foreach (var line in SMSModForge.Shared.ForgeNotice.News(version))
                Assert.Contains(line, notice);
        }
        // Control: not the whole notice of a first start.
        Assert.DoesNotContain("ModForge is installed", notice);

        game.Ask("click Got it");
        Assert.StartsWith("(none)", game.AskUntil("notice", n => n.StartsWith("(none)", StringComparison.Ordinal), 5));
        Assert.Contains("Last shown = " + current, File.ReadAllText(Settings));
    }

    [Fact]
    public void Z1_The_main_menu_notice_does_not_come_back_once_closed()
    {
        if (Off) return;
        var game = _s.Restart();
        game.Ask("wait 4", 10);
        Assert.StartsWith("(none)", game.Ask("notice"));
        Assert.Contains("ModForge", game.Ask("menu"));       // control: the menu itself is up
    }

    [Fact]
    public void Z2_A_pack_moved_on_the_main_menu_changes_whose_colour_a_clash_shows()
    {
        if (Off) return;
        var game = _s.Game!;
        string before = game.Ask("menu");
        Assert.True(before.IndexOf("gamecheck", StringComparison.Ordinal) < before.IndexOf("gameorder", StringComparison.Ordinal),
                    "gamecheck is not above gameorder before the move: " + before);
        // gameorder let go of at the top: it loads first, so gamecheck's Anna wins.
        game.Ask("drop-pack gameorder 0");
        string after = game.AskUntil("menu", m => m.IndexOf("gameorder", StringComparison.Ordinal) < m.IndexOf("gamecheck", StringComparison.Ordinal), 5);
        Assert.True(after.IndexOf("gameorder", StringComparison.Ordinal) < after.IndexOf("gamecheck", StringComparison.Ordinal), "The list did not follow the drop: " + after);
        Assert.Contains("Order = gameorder", File.ReadAllText(Settings));

        // The language, chosen here too, for the next check: packs follow it
        // the next time a game is loaded.
        Assert.Equal(InGameSuitePacks.Talk.TranslatedInto, game.Ask("lang " + InGameSuitePacks.Talk.TranslatedInto));
        // Control for the notice below: the loads in English told nothing, as no
        // pack shows the game's lines in English.
        Assert.Contains("GameLinesNoticeShown = false", File.ReadAllText(Settings));

        game = _s.InGame();
        // The player is told, once, that a pack shows the game's own lines in this
        // language (the Talk pack translates one into pt-BR, Z5). Until 1.9.0's review
        // the notice counted them after they were shown, found none and never came.
        string body = File.ReadLines(Path.Combine(TranslationCoverageTests.EditorDir, "Languages", InGameSuitePacks.Talk.TranslatedInto + ".txt"))
                          .First(l => l.StartsWith("game.gameLines.body.one = ", StringComparison.Ordinal));
        string told = body.Substring(body.IndexOf("{language}", StringComparison.Ordinal) + "{language}".Length).Split('.')[0].Trim();
        _out.WriteLine("the save warning: " + _s.Warning);
        Assert.Contains(told, _s.Warning);
        Assert.Contains("GameLinesNoticeShown = true", File.ReadAllText(Settings));
        SeeConversationsThrough(game);
        game.Ask("vanilla-dialogue 8_Room_Talk/Kitchen/Anna_DishesDecline");
        string speaker = game.AskUntil("speaker", s => s.Contains("Anna"), 8);
        _out.WriteLine(speaker);
        Assert.Matches("Anna'[^|]*drawn #FF00FF", speaker);
        Assert.DoesNotMatch("Anna'[^|]*drawn #00C000", speaker);     // control: what K2 saw before the move
        SeeConversationsThrough(game);
    }

    [Fact]
    public void Z3_A_pack_with_a_translation_is_played_in_the_language_chosen_on_the_main_menu()
    {
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Talk.Id;
        SeeConversationsThrough(game);
        Begin(game, id, "flow");
        UntilChoices(game, 2);
        game.Ask("click Right");
        string expected = InGameSuitePacks.Talk.TranslatedLine.Replace("[PV:took]", "right");
        string line = game.AskUntil("line", l => LineParts(l, "line").Contains(expected), 8);
        Assert.Contains(expected, LineParts(line, "line"));
        SeeConversationsThrough(game);
        // Control: the lines it has no translation of stay in the pack's words.
        Begin(game, id, "jumpy");
        Assert.Contains("One.", LineParts(game.AskUntil("line", l => LineParts(l, "line").Count > 0, 8), "line"));
        SeeConversationsThrough(game);
    }

    [Fact]
    public void Z4_A_pack_switched_off_on_the_main_menu_is_not_played()
    {
        if (Off) return;
        var game = _s.Restart();
        const string off = InGameSuitePacks.Rules.Id;
        string menu = game.AskUntil("menu", m => m.Contains(off, StringComparison.Ordinal), 15);
        var row = System.Text.RegularExpressions.Regex.Match(menu, @"(\d+): '[^']*?(?<!\w)" + Regex.Escape(off) + @"\s+v\d");
        Assert.True(row.Success, "No row for " + off + ": " + menu);
        // The box on that pack's row, wherever in the row it sits.
        string switches = game.Ask("find-all PackSwitch");
        string box = switches.Substring(switches.IndexOf(": ", StringComparison.Ordinal) + 2).Split(" | ")
                             .FirstOrDefault(p => p.Contains("SMSModForgeBanner_" + row.Groups[1].Value + "/", StringComparison.Ordinal)
                                                  || p.EndsWith("SMSModForgeBanner_" + row.Groups[1].Value, StringComparison.Ordinal)) ?? "";
        Assert.True(box.Length > 0, $"No switch on row {row.Groups[1].Value}: {switches}");
        game.Ask("press " + box);
        Assert.Contains("SwitchedOff = " + off, File.ReadAllText(Settings));
        // Still listed - unticked, to be switched back on.
        Assert.Contains(off, game.AskUntil("menu", m => m.Contains(off, StringComparison.Ordinal), 5));

        game = _s.InGame();
        Assert.Throws<BridgeError>(() => game.PackVar(off, "loaded"));
        Assert.Equal("0", game.PackVar(InGameSuitePacks.Saving.Id, "keptNumber"));   // control: the others play
    }

    [Fact]
    public void Z5_The_games_own_lines_in_a_conversation_a_pack_extends_are_shown_in_its_translation()
    {
        // In pt-BR since Z2. Found while renaming characters (1.9.0): the plugin
        // compared a translation's note with Node.Text, which is empty until a line
        // plays, so none of the game's lines was ever shown translated.
        if (Off) return;
        var game = _s.InGame();
        _out.WriteLine("the save warning: " + _s.Warning);
        string Text(int id)
        {
            string said = game.Ask($"game-text line {InGameSuitePacks.Talk.GameLinesConversation}#{id}");
            _out.WriteLine(said);
            return said;
        }
        Assert.EndsWith("= " + InGameSuitePacks.Talk.GameLineInPortuguese, Text(InGameSuitePacks.Talk.GameLineTranslated));
        // Control: a line of the same conversation the translation does not have
        // stays in the game's words.
        Assert.EndsWith("= Maybe next time.", Text(InGameSuitePacks.Talk.GameLineLeft));
    }

    [Fact]
    public void Z6_Back_on_the_main_menu_with_the_renaming_packs_switched_off_the_games_characters_have_their_own_names_again()
    {
        // Put back in the same game (1.9.0's review: nothing checked it). The game's
        // characters are assets that live as long as the game runs, so a new name
        // would outlast the pack that gave it if nothing put it back as the level goes.
        if (Off) return;
        var game = _s.InGame();
        bool Says(string text, string word) => Regex.IsMatch(text, @"(?<!\w)" + word + @"(?!\w)");
        string Line()
        {
            string said = game.Ask("game-text line 8_Room_Talk/Kitchen/AboutAdrian#-1600756773");
            _out.WriteLine(said);
            return said;
        }
        string Speaker()
        {
            SeeConversationsThrough(game);
            game.Ask("vanilla-dialogue " + InGameCheckPacksTests.JosefTalks);
            string said = game.AskUntil("speaker", s => s.Contains("Jose") || s.Contains("Joe'"), 8);
            _out.WriteLine(said);
            SeeConversationsThrough(game);
            return said;
        }
        // The control: with both renaming packs on, he has a new name - gameorder's
        // Joseph (K8, K10), or gamecheck's Joe once Z2 has moved gameorder to load first.
        string before = Speaker();
        Assert.True(before.Contains("Joseph'") || before.Contains("Joe'"), before);
        Assert.DoesNotContain("Josef'", before);
        string lineBefore = Line();
        Assert.True((Says(lineBefore, "Joseph") || Says(lineBefore, "Joe")) && !Says(lineBefore, "Josef"), lineBefore);

        // The game's own Main Menu button, as a player leaves a game: on its panel,
        // which the game opens from its menu (here switched on directly).
        game.Ask("set-active 9_MainCanvas/Quitagme true");
        game.Ask("wait 0.5", 10);
        Assert.Contains("click", game.Ask("press 9_MainCanvas/Quitagme/Button (2)"));
        // Leaving a level the suite has played in for ten minutes takes the game a
        // while, and the bridge answers nothing until it has: up to three minutes.
        game.Ask("ping", 180);
        game.AskUntil("menu", m => m.Contains("gameorder", StringComparison.Ordinal), 90);
        foreach (var id in new[] { "gamecheck", "gameorder" })
        {
            string menu = game.AskUntil("menu", m => m.Contains(id, StringComparison.Ordinal), 15);
            // The pack's own row: its id where it stands, before its version - another
            // row's clash note names it too ("Clashes with gamecheck").
            var row = Regex.Match(menu, @"(\d+): '[^']*?(?<!\w)" + Regex.Escape(id) + @"\s+v\d");
            Assert.True(row.Success, "No row for " + id + ": " + menu);
            string switches = game.Ask("find-all PackSwitch");
            string box = switches.Substring(switches.IndexOf(": ", StringComparison.Ordinal) + 2).Split(" | ")
                                 .FirstOrDefault(p => p.Contains("SMSModForgeBanner_" + row.Groups[1].Value + "/", StringComparison.Ordinal)
                                                      || p.EndsWith("SMSModForgeBanner_" + row.Groups[1].Value, StringComparison.Ordinal)) ?? "";
            Assert.True(box.Length > 0, $"No switch on row {row.Groups[1].Value}: {switches}");
            game.Ask("press " + box);
        }
        string settings = File.ReadAllText(Settings);
        Assert.Contains("gamecheck", settings.Split('\n').First(l => l.StartsWith("SwitchedOff = ", StringComparison.Ordinal)));
        Assert.Contains("gameorder", settings.Split('\n').First(l => l.StartsWith("SwitchedOff = ", StringComparison.Ordinal)));

        // The save again, with nobody renaming him: the game's own Josef.
        game.Ask("load");
        game.Ask("wait-ingame 150", 180);
        string warning = game.AskUntil("warning", w => w.StartsWith("open", StringComparison.Ordinal), 15);
        if (warning.StartsWith("open", StringComparison.Ordinal)) game.Ask("warning go-on");
        game.Ask("wait 2", 10);
        string after = Speaker();
        Assert.Contains("Josef'", after);
        Assert.DoesNotContain("Joseph'", after);
        string line = Line();
        Assert.True(Says(line, "Josef") && !Says(line, "Joseph"), line);
    }

    /// <summary>
    /// The conversation's line once it offers at least <paramref name="count"/>
    /// choices. A choice line is said first and its choices follow a click, as
    /// in the game's own conversations: so a line with none yet is clicked on.
    /// </summary>
    private static string UntilChoices(GameRun game, int count)
    {
        var deadline = DateTime.Now.AddSeconds(10);
        string line = game.Ask("line");
        while (LineParts(line, "choice").Count < count && DateTime.Now < deadline)
        {
            if (LineParts(line, "line").Count > 0) game.Ask("next");
            game.Ask("wait 0.5", 10);
            line = game.Ask("line");
        }
        return line;
    }

    /// <summary>A click on what says these words, as soon as it is on
    /// screen - the game's own screens arrive in their own time.</summary>
    private void PressWhenShown(GameRun game, string words, double seconds = 30)
    {
        var deadline = DateTime.Now.AddSeconds(seconds);
        while (true)
        {
            try { game.Ask("press-text " + words); return; }
            catch (BridgeError) when (DateTime.Now < deadline) { System.Threading.Thread.Sleep(500); }
        }
    }

    [Fact]
    public void F2_A_change_to_one_of_the_games_screens_is_made_there_and_the_rest_of_it_stays_the_games()
    {
        if (Off) return;
        var game = _s.InGame();
        string screen = InGameSuitePacks.Story.GameScreen.Substring("vanillaui:".Length);
        _out.WriteLine($"{InGameSuitePacks.Story.PatchedLabel} / {InGameSuitePacks.Story.KeptLabel}");
        string patched = Find(game, screen + "/" + InGameSuitePacks.Story.PatchedLabel);
        Assert.StartsWith("'" + InGameSuitePacks.Story.PatchedWords + "'", Field(patched, "text"));
        // Control: the label the pack did not touch keeps the game's words.
        string kept = Find(game, screen + "/" + InGameSuitePacks.Story.KeptLabel);
        Assert.StartsWith("'" + InGameSuitePacks.Story.KeptWords.Replace("\n", "\\n") + "'", Field(kept, "text"));
    }

    [Fact]
    public void F4_A_change_further_down_one_of_the_games_screens_is_made_there_too()
    {
        // Objects two and three levels down, saved with their path from the
        // screen: the game looked for each under its parent instead and
        // skipped it, so only the first level was ever changed.
        if (Off) return;
        var game = _s.InGame();
        string screen = InGameSuitePacks.Story.GameScreen.Substring("vanillaui:".Length);
        Assert.StartsWith("'" + InGameSuitePacks.Story.DeepWords + "'",
                          Field(Find(game, screen + "/" + InGameSuitePacks.Story.DeepLabel), "text"));
        Assert.Contains(InGameSuitePacks.Story.DeepAdded, Field(Find(game, screen + "/" + InGameSuitePacks.Story.DeepParent), "children"));
        // Control: the label beside it keeps the game's words.
        Assert.StartsWith("'" + InGameSuitePacks.Story.DeepKeptWords + "'",
                          Field(Find(game, screen + "/" + InGameSuitePacks.Story.DeepKeptLabel), "text"));
    }

    [Fact]
    public void F5_A_screen_of_the_packs_own_is_laid_out_over_the_whole_screen()
    {
        // As the editor shows it: an object in a corner is in the screen's
        // corner, a stretched one covers the screen. Both were laid out in a
        // 100x100 box in the middle, on a canvas of the pack's own and on the
        // game's interface alike.
        if (Off) return;
        var game = _s.InGame();
        Play(game, "opencorners");
        try
        {
            foreach (var (canvas, id) in new[] { ("PackUiCanvas_" + InGameSuitePacks.Story.CornersOwn, InGameSuitePacks.Story.CornersOwn),
                                                 ("9_MainCanvas", InGameSuitePacks.Story.CornersGame) })
            {
                string at = canvas + "/Pack_" + id;
                Assert.True(ActiveSoon(game, at, true), $"{id} did not open.");
                var whole = OnScreen(Find(game, canvas));
                var corner = OnScreen(Find(game, at + "/Corner"));
                var fill = OnScreen(Find(game, at + "/Fill"));
                var middle = OnScreen(Find(game, at + "/Middle"));
                _out.WriteLine($"{id}: canvas {whole}, corner {corner}, fill {fill}, middle {middle}");
                Near(whole.R, corner.R, id + ": the corner's right");
                Near(whole.B, corner.B, id + ": the corner's bottom");
                Near(whole.L, fill.L, id + ": the stretched one's left");
                Near(whole.B, fill.B, id + ": the stretched one's bottom");
                Near(whole.R, fill.R, id + ": the stretched one's right");
                Near(whole.T, fill.T, id + ": the stretched one's top");
                // Control: the middle is the middle either way.
                Near((whole.L + whole.R) / 2, (middle.L + middle.R) / 2, id + ": the middle one, across");
                Near((whole.B + whole.T) / 2, (middle.B + middle.T) / 2, id + ": the middle one, up");
            }
        }
        finally { Play(game, "closecorners"); }
    }

    [Fact]
    public void F6_A_grid_fitted_to_what_it_holds_is_only_as_wide_as_its_cells_and_still_wraps()
    {
        // Eight 40x40 columns, 4 apart, each grid sized by a Content Size
        // Fitter to what it asks for. Unity's grid asks for all eight columns
        // whatever it holds.
        if (Off) return;
        var game = _s.InGame();
        const string at = "PackUiCanvas_" + InGameSuitePacks.Story.Grids + "/Pack_" + InGameSuitePacks.Story.Grids;
        Play(game, "opengrids");
        try
        {
            Assert.True(ActiveSoon(game, at, true), "The grids did not open.");
            string Size(string node) => Field(Find(game, at + "/" + node), "size");
            Assert.Equal("128,40", Size("Fit"));      // three of the eight columns
            Assert.Equal("348,84", Size("Full"));     // ten: a full row of eight, and a second
            // Control: Unity's grid holding the same three is eight columns wide.
            Assert.Equal("348,40", Size("Unfit"));
            // The cells are in it, from its left edge to its right.
            var fit = OnScreen(Find(game, at + "/Fit"));
            Near(fit.L, OnScreen(Find(game, at + "/Fit/C1")).L, "the first cell's left", 0.002f);
            Near(fit.R, OnScreen(Find(game, at + "/Fit/C3")).R, "the last cell's right", 0.002f);
        }
        finally { Play(game, "closegrids"); }
    }

    [Fact]
    public void F7_A_packs_picture_keeps_to_its_own_edges_and_an_upright_object_stays_level_on_a_turned_one()
    {
        if (Off) return;
        var game = _s.InGame();
        const string at = "PackUiCanvas_" + InGameSuitePacks.Story.CornersOwn + "/Pack_" + InGameSuitePacks.Story.CornersOwn;
        Play(game, "opencorners");
        try
        {
            Assert.True(ActiveSoon(game, at, true), "The screen did not open.");
            // Clamped: an edge never takes in the opposite one when the
            // picture lands between pixels. Control: a picture drawn tiled
            // repeats, so it is left wrapping.
            Assert.Equal("Clamp Simple", Field(Find(game, at + "/Corner"), "wrap"));
            Assert.Equal("Repeat Tiled", Field(Find(game, at + "/Tiles"), "wrap"));

            // On a group turned 90 degrees, the upright one is level on the
            // screen; control: the one beside it turns with the group.
            float Turn(string node) => float.Parse(Field(Find(game, at + "/Turned/" + node), "worldrotz"), CultureInfo.InvariantCulture);
            float level = Turn("Level"), along = Turn("Along");
            _out.WriteLine($"level {level}, along {along}");
            Assert.True(level < 0.5f || level > 359.5f, $"The upright object is turned {level} degrees.");
            Near(90f, along, "the other object's turn", 0.5f);
        }
        finally { Play(game, "closecorners"); }
    }

    private const string CardsAt = "PackUiCanvas_" + InGameSuitePacks.Story.Cards + "/Pack_" + InGameSuitePacks.Story.Cards;

    [Fact]
    public void F8_A_button_shrinks_while_held_over_its_hover_and_one_without_keeps_the_games_press()
    {
        // A press shown while the pointer is down - the game's own plays only
        // on the click, once let go, too late when the click closes the screen.
        if (Off) return;
        var game = _s.InGame();
        const string id = InGameSuitePacks.Story.Id;
        Play(game, "opencards");
        try
        {
            Assert.True(ActiveSoon(game, CardsAt, true), "The cards did not open.");
            float Scale(string card) => XY(Field(Find(game, CardsAt + "/" + card), "scale")).X;
            int clicks = int.Parse(game.PackVar(id, "cardClicks"), CultureInfo.InvariantCulture);

            // Held: 0.9 of the size the game's hover bounce gives it (1.05).
            game.Ask("pointer enter Check Pressy");
            game.Ask("wait 1", 10);
            Near(1.05f, Scale("Pressy"), "hovered", 0.01f);
            game.Ask("pointer down Check Pressy");
            game.Ask("wait 0.4", 10);
            Near(0.945f, Scale("Pressy"), "held", 0.01f);
            // Let go: back to the hover's size, the game's press (to 0.84)
            // not played on top, and the click still counted.
            game.Ask("pointer up Check Pressy");
            game.Ask("pointer click Check Pressy");
            var released = Enumerable.Range(0, 5).Select(_ => Scale("Pressy")).ToList();
            _out.WriteLine("released: " + string.Join(" ", released));
            Assert.All(released, s => Assert.True(s > 0.935f, $"Pressed twice: {s}"));
            game.Ask("wait 0.5", 10);
            Near(1.05f, Scale("Pressy"), "let go", 0.01f);
            Assert.Equal((clicks + 1).ToString(CultureInfo.InvariantCulture), game.PackVar(id, "cardClicks"));
            game.Ask("pointer exit Check Pressy");

            // Control: one without it does not change while held, and plays
            // the game's press when clicked.
            game.Ask("pointer down Check Plain");
            game.Ask("wait 0.4", 10);
            Near(1f, Scale("Plain"), "the plain card held", 0.001f);
            game.Ask("pointer up Check Plain");
            game.Ask("pointer click Check Plain");
            var pressed = Enumerable.Range(0, 5).Select(_ => Scale("Plain")).ToList();
            _out.WriteLine("plain clicked: " + string.Join(" ", pressed));
            Assert.True(pressed.Min() < 0.9f, "The game's press did not play on the plain card.");
        }
        finally { Play(game, "closecards"); }
    }

    [Fact]
    public void F9_An_object_that_keeps_moving_pulses_and_sweeps_while_shown_and_one_without_stays_put()
    {
        if (Off) return;
        var game = _s.InGame();
        Play(game, "opencards");
        try
        {
            Assert.True(ActiveSoon(game, CardsAt, true), "The cards did not open.");
            var glow = new List<float>();
            var shine = new List<float>();
            var still = new List<float>();
            for (int i = 0; i < 10; i++)
            {
                glow.Add(float.Parse(Field(Find(game, CardsAt + "/Glow"), "group"), CultureInfo.InvariantCulture));
                shine.Add(XY(Field(Find(game, CardsAt + "/Shine"), "local")).X);
                still.Add(XY(Field(Find(game, CardsAt + "/Still"), "local")).X);
                game.Ask("wait 0.13", 10);
            }
            _out.WriteLine("glow " + string.Join(" ", glow) + " | shine " + string.Join(" ", shine) + " | still " + string.Join(" ", still));
            // The fade breathes between 0.2 and 1 of its own.
            Assert.All(glow, a => Assert.InRange(a, 0.19f, 1.01f));
            Assert.True(glow.Max() - glow.Min() > 0.4f, "The fade did not pulse.");
            // The sweep runs between its ends, 100 either side of its place.
            Assert.All(shine, x => Assert.InRange(x, -100.5f, 100.5f));
            Assert.True(shine.Max() - shine.Min() > 80f, "The sweep did not move.");
            // Control: one with no loop stays where it is.
            Assert.True(still.Max() - still.Min() < 0.01f, "The still one moved.");
        }
        finally { Play(game, "closecards"); }
    }

    /// <summary>Where a UI object is drawn, as fractions of the screen.</summary>
    private static (float L, float B, float R, float T) OnScreen(string found)
    {
        var n = Field(found, "screen").Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return (n[0], n[1], n[2], n[3]);
    }

    /// <summary>Plays one of a check pack's short conversations to its end.</summary>
    private static void Play(GameRun game, string key, string pack = InGameSuitePacks.Story.Id)
    {
        Begin(game, pack, key);
        game.Ask("wait-dialogue-end 20", 30);
        game.Ask("wait 0.3", 10);
    }

    /// <summary>
    /// Starts one of a pack's conversations and waits until it is the one on
    /// screen: it arrives after the game's own fade, and a start that meets
    /// another conversation's end can stand down - then it is asked again,
    /// as a player would try again.
    /// </summary>
    private static void Begin(GameRun game, string pack, string key)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            game.Ask($"dialogue {pack} {key}");
            string line = game.AskUntil("line", l => l.Contains("/" + pack + "_" + key + " ", StringComparison.Ordinal), 6);
            if (line.Contains("/" + pack + "_" + key + " ", StringComparison.Ordinal)) return;
            game.Ask("wait 1", 10);
        }
        throw new XunitException($"{pack}'s '{key}' never came on screen.");
    }

    private void TravelToRoom(GameRun game)
        => Travel(game, "pack:" + InGameSuitePacks.World.Id + "." + InGameSuitePacks.World.Room, "_" + InGameSuitePacks.World.RoomObject);

    /// <summary>Goes to a place as a navigator button does, and waits until it
    /// is the one map up. The game refuses a journey while it is locked, and
    /// ignores one while a conversation is on (the navigator is hidden then).</summary>
    private void Travel(GameRun game, string token, string levelEnding)
    {
        if (OnlyMapUp(game.Ask("level"), levelEnding)) return;
        // A journey the game is not ready for does nothing - a conversation
        // closing, an arrival still settling - so it is set out on again, as
        // a player clicks again.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            SeeConversationsThrough(game);
            var deadline = DateTime.Now.AddSeconds(20);
            while (true)
            {
                try { game.Ask("travel " + token); break; }
                catch (BridgeError e) when (e.Message.Contains("locked") && DateTime.Now < deadline) { System.Threading.Thread.Sleep(500); }
            }
            if (OnlyMapUp(game.AskUntil("level", l => OnlyMapUp(l, levelEnding), 8), levelEnding)) break;
        }
        Arrived(game, levelEnding, "travel " + token);
    }

    private static bool OnlyMapUp(string level, string ending)
        => !level.Contains(',') && level.EndsWith(ending, StringComparison.Ordinal);

    /// <summary>Waits until that map is the one up, then for whatever the
    /// arrival starts: the game's housekeeping, and any of its conversations,
    /// seen through as a player would.</summary>
    private void Arrived(GameRun game, string levelEnding, string after)
    {
        string level = game.AskUntil("level", l => OnlyMapUp(l, levelEnding), 20);
        Assert.True(OnlyMapUp(level, levelEnding), $"After {after} the map up is: {level}");
        game.Ask("wait 2", 10);
        SeeConversationsThrough(game);
    }

    /// <summary>
    /// The game starts its own conversations as places are arrived at - a
    /// date on the Beach, small talk in a hallway - and nothing moves while
    /// one is on. A player clicks through; so does this: on to the next line,
    /// and the last choice when there are choices (the one that ends things,
    /// in the game's small talk).
    /// </summary>
    private void SeeConversationsThrough(GameRun game)
    {
        for (int i = 0; i < 60; i++)
        {
            string line = game.Ask("line");
            if (line.StartsWith("(none)", StringComparison.Ordinal)) return;
            if (i == 0) _out.WriteLine("a conversation of the game's is on: " + line);
            var choices = LineParts(line, "choice");
            // It may end between the look and the click: then there is nothing
            // to click, which is what was wanted.
            try
            {
                if (choices.Count > 0) game.Ask("click " + choices[^1]);
                else game.Ask("next");
            }
            catch (BridgeError) { }
            game.Ask("wait 0.8", 10);
        }
        throw new XunitException("A conversation of the game's would not end: " + game.Ask("line"));
    }

    /// <summary>The words of one kind in a <c>line</c> answer
    /// (<c>&lt;conversation&gt; node &lt;n&gt; || kind: words | kind: words</c>).</summary>
    private static List<string> LineParts(string line, string kind)
    {
        int at = line.IndexOf(" || ", StringComparison.Ordinal);
        if (at < 0) return new();
        return line.Substring(at + 4).Split(" | ").Where(p => p.StartsWith(kind + ": ", StringComparison.Ordinal))
                   .Select(p => p.Substring(kind.Length + 2)).ToList();
    }
    private static string VarSoon(GameRun game, string pack, string name, string expected, double seconds = 5)
    {
        var deadline = DateTime.Now.AddSeconds(seconds);
        string value = game.PackVar(pack, name);
        while (value != expected && DateTime.Now < deadline)
        {
            System.Threading.Thread.Sleep(250);
            value = game.PackVar(pack, name);
        }
        return value;
    }

    /// <summary>Whether those words are on screen once they have had time to
    /// be <paramref name="expected"/>.</summary>
    private static bool TextOnScreenSoon(GameRun game, string words, bool expected, double seconds = 3)
    {
        bool OnScreen()
        {
            try { return Field(game.Ask("find-text " + words), "active").EndsWith("/True", StringComparison.Ordinal); }
            catch (BridgeError) { return false; }
        }
        var deadline = DateTime.Now.AddSeconds(seconds);
        bool now = OnScreen();
        while (now != expected && DateTime.Now < deadline)
        {
            System.Threading.Thread.Sleep(250);
            now = OnScreen();
        }
        return now;
    }

    /// <summary>The object's own active flag once it has had time to become
    /// <paramref name="expected"/> - gates move on the game's frames.</summary>
    private bool ActiveSoon(GameRun game, string path, bool expected)
    {
        string answer = game.AskUntil("find " + path, a => Field(a, "active").StartsWith(expected ? "True" : "False", StringComparison.Ordinal), 3);
        return Field(answer, "active").StartsWith("True", StringComparison.Ordinal);
    }
}

/// <summary>Runs a class's tests in the order of their names - the in-game
/// suite's areas build on the game the ones before them left.</summary>
public sealed class ByName : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase
        => testCases.OrderBy(t => t.TestMethod.Method.Name, StringComparer.Ordinal);
}
