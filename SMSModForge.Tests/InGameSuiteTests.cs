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
public sealed class InGameSuiteTests : IClassFixture<InGameSuiteTests.Session>
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
            _inGame = true;
            return game;
        }

        /// <summary>What ModForge's save warning said as the test save loaded.</summary>
        public string Warning { get; private set; } = "";

        /// <summary>The game closed and started again on the main menu - the
        /// test save put back clean, ModForge's settings as the run left them.</summary>
        internal GameRun Restart()
        {
            var game = Game!;
            game.Stop();
            _inGame = false;
            Warning = "";
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
    public void B4_A_level_of_the_games_takes_the_packs_additions_and_only_the_changes_it_asks_for()
    {
        if (Off) return;
        var game = _s.InGame();
        const string beach = "5_Levels/14_Beach";

        string mark = Find(game, beach + "/BeachMark");
        var (mx, my) = XY(Field(mark, "home"));
        Near(2f, mx, "BeachMark x");
        Near(1f, my, "BeachMark y");
        Assert.Equal("#CC2222FF", Field(mark, "texel"));

        // A bound object that asks to be moved is moved...
        var (lx, ly) = XY(Field(Find(game, beach + "/Light 2D"), "local"));
        Near(0.25f, lx, "Light 2D local x");
        Near(0.5f, ly, "Light 2D local y");

        // ...and one bound only to hang a child on stays where the game has it.
        var vanilla = VanillaLevelCatalog.FindLevel("14_Beach");
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
        Assert.Single(paths);       // the photo's; the still is not a photo
        Assert.Contains("CoreGallery/SMSModForge_Photo_", paths[0]);
        Assert.Contains("#8844CCFF", game.Ask("find " + paths[0]));
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

    // ── Z: a restart - the main menu's settings carried into the next game ─

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

        game = _s.InGame();
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
        var row = System.Text.RegularExpressions.Regex.Match(menu, @"(\d+): '[^']*" + off);
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
