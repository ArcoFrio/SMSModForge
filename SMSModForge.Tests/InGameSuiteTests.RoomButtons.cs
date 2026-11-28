using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Services;
using Xunit;
using Looks = SMSModForge.Tests.InGameSuitePacks.Looks;

namespace SMSModForge.Tests;

/// <summary>
/// The player's room's buttons over a pack's place (after 1.10.0, the author: "I don't see the player room buttons in
/// the preview of the Bedroom for Androids at all. Why is that?").
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The buttons place (InGameSuitePacks.Looks): a copy of <c>5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas</c>, its Sleep
    /// button moved and its Calendar switched off, as the Androids Bedroom copies it. In the game, the interface and
    /// the busts off, the copy on and then off: what the copy draws. The place preview's drawing of the canvas
    /// (Rendering/LevelCanvas, the game's interface extraction drawn by UiSceneRenderer) is held to it where it is
    /// solid and even, as S18 holds the interface. Control: the canvas as the game's room has it - Sleep where the room
    /// puts it, the Calendar on - is visibly not what the game draws.
    /// </summary>
    [Fact]
    public void S23_A_copy_of_the_rooms_buttons_is_drawn_as_the_game_draws_it()
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + Looks.Id + "." + Looks.ButtonsPlaceKey;
        var hidden = new List<string> { "9_MainCanvas" };
        string copy = "", on, off;
        try
        {
            Travel(game, token, "_" + Looks.ButtonsPlaceObject);
            game.Ask("wait 2", 10);
            string level = "5_Levels/" + game.Ask("level").Split(' ').Last().Trim();
            string found = game.Ask("find-all *" + Looks.RoomButtonsObject + "*");
            var paths = found.Substring(found.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
            _out.WriteLine("canvases: " + string.Join(" | ", paths));
            copy = paths.FirstOrDefault(p => p.StartsWith(level + "/", StringComparison.Ordinal)) ?? "";
            Assert.True(copy.Length > 0, "the place has no copy of the room's buttons in the game: " + found);
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            on = LookFolder(game.Ask("screen-look buttons-on", 15));
            game.Ask("set-active " + copy + " false");
            game.Ask("wait 0.3", 10);
            off = LookFolder(game.Ask("screen-look buttons-off", 15));
        }
        finally
        {
            if (copy.Length > 0) game.Ask("set-active " + copy + " true");
            game.Ask("freeze off");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        string keep = Path.Combine(Path.GetDirectoryName(_s.Report)!, "room-buttons");
        Directory.CreateDirectory(keep);
        File.Copy(Path.Combine(on, "screen.png"), Path.Combine(keep, "buttons-on.png"), true);
        File.Copy(Path.Combine(off, "screen.png"), Path.Combine(keep, "buttons-off.png"), true);
        var (gw, gh, gameOn) = Png(Path.Combine(keep, "buttons-on.png"));
        var (_, _, gameOff) = Png(Path.Combine(keep, "buttons-off.png"));

        // The preview's canvas, from the place as the suite's pack has it.
        string packFolder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "packs", Looks.Id);
        var pack = PackRepository.Load(packFolder);
        var node = pack.Places.Single(p => p.Key == Looks.ButtonsPlaceKey).GameObjects.Single(g => g.Name == Looks.RoomButtonsObject);
        var surface = LevelCanvas.SurfaceAt(Looks.RoomButtons);
        Assert.NotNull(surface);
        var mine = LevelCanvas.Render(surface!, LevelCanvas.MovesOf(node), out int hw, out int hh);
        var asTheRoom = LevelCanvas.Render(surface!, Array.Empty<LevelCanvas.Move>(), out _, out _);
        Assert.NotNull(mine);
        Assert.NotNull(asTheRoom);
        Assert.Equal(gw * 2, hw);
        Assert.Equal(gh * 2, hh);
        SavePbgra(Path.Combine(keep, "preview-buttons.png"), mine!, hw, hh);

        // As S18: where a picture covers a game pixel completely and evenly, its four pixels' mean against the game's.
        static (bool Covers, int R, int G, int B) At(byte[] p, int pw, int ph, int x, int y)
        {
            int r = 0, g = 0, b = 0;
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int px = x * 2 + dx, py = y * 2 + dy;
                    if (px < 0 || py < 0 || px >= pw || py >= ph) return (false, 0, 0, 0);
                    int i = (py * pw + px) * 4;
                    if (p[i + 3] != 255) return (false, 0, 0, 0);
                    b += p[i]; g += p[i + 1]; r += p[i + 2];
                }
            return (true, (r + 2) / 4, (g + 2) / 4, (b + 2) / 4);
        }
        static (bool Covers, int R, int G, int B) Flat(byte[] p, int pw, int ph, int x, int y)
        {
            var at = At(p, pw, ph, x, y);
            if (!at.Covers) return (false, 0, 0, 0);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = At(p, pw, ph, x + dx, y + dy);
                    if (!n.Covers || Math.Abs(n.R - at.R) > 6 || Math.Abs(n.G - at.G) > 6 || Math.Abs(n.B - at.B) > 6) return (false, 0, 0, 0);
                }
            return at;
        }
        bool Drawn(int x, int y)
        {
            int i = (y * gw + x) * 4;
            return Math.Max(Math.Abs(gameOn[i] - gameOff[i]), Math.Max(Math.Abs(gameOn[i + 1] - gameOff[i + 1]), Math.Abs(gameOn[i + 2] - gameOff[i + 2]))) > 3;
        }
        int drawn = 0, covered = 0, near = 0, roomCovered = 0, roomNear = 0;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                int i = (y * gw + x) * 4;
                if (Drawn(x, y)) drawn++;
                var p = Flat(mine!, hw, hh, x, y);
                if (p.Covers)
                {
                    covered++;
                    if (Math.Abs(p.R - gameOn[i + 2]) <= 6 && Math.Abs(p.G - gameOn[i + 1]) <= 6 && Math.Abs(p.B - gameOn[i]) <= 6) near++;
                }
                var r = Flat(asTheRoom!, hw, hh, x, y);
                if (r.Covers)
                {
                    roomCovered++;
                    if (Math.Abs(r.R - gameOn[i + 2]) <= 6 && Math.Abs(r.G - gameOn[i + 1]) <= 6 && Math.Abs(r.B - gameOn[i]) <= 6) roomNear++;
                }
            }
        // Each button's square in the game (x right, y up from the middle, canvas pixels; the game's at half).
        int Around(double cx, double cy)
        {
            int n = 0, gx = (int)Math.Round((960 + cx) / 2), gy = (int)Math.Round((540 - cy) / 2);
            for (int y = gy - 20; y < gy + 20; y++)
                for (int x = gx - 20; x < gx + 20; x++)
                    if (x >= 0 && y >= 0 && x < gw && y < gh && Drawn(x, y)) n++;
            return n;
        }
        var sleepRoom = LevelCanvas.LocalPosition(surface!, "Player_Room_Buttons/SleepButton")!.Value;
        var calendar = LevelCanvas.LocalPosition(surface!, "Player_Room_Buttons/CalendarButton")!.Value;
        var pc = LevelCanvas.LocalPosition(surface!, "Player_Room_Buttons/PCButton")!.Value;
        var friday = LevelCanvas.LocalPosition(surface!, "Player_Room_Buttons/SkipToFridayButton")!.Value;
        int atSleep = Around(Looks.RoomSleepAt[0], Looks.RoomSleepAt[1]), atSleepRoom = Around(sleepRoom.X, sleepRoom.Y);
        int atCalendar = Around(calendar.X, calendar.Y), atPc = Around(pc.X, pc.Y), atFriday = Around(friday.X, friday.Y);
        double Share(int n, int of) => of == 0 ? 0 : (double)n / of;
        _out.WriteLine($"game {gw}x{gh}: the copy drew {drawn} pixels - round the moved Sleep {atSleep} of 1600, where the room has Sleep {atSleepRoom}, "
                       + $"the Calendar {atCalendar}, the PC {atPc}, Skip to Friday {atFriday}; the preview's canvas solid and even at {covered} - within six "
                       + $"{Share(near, covered):P2}; the room's own canvas at {roomCovered} - within six {Share(roomNear, roomCovered):P2}");
        Assert.True(drawn > 1000, "the game drew too little of the copy to tell - is it faded out?");
        Assert.True(atSleep > 400 && atSleepRoom < 40, "the game's Sleep button is not where the place moved it");
        Assert.True(atCalendar < 40, "the game's Calendar, switched off by the place, is drawn");
        // Four buttons of 85 canvas pixels, solid inside their rims: about a thousand of the game's pixels.
        Assert.True(covered > 500, "the preview's canvas covered too little to tell");
        Assert.True(Share(near, covered) > 0.97, $"the preview's canvas is the game's at {Share(near, covered):P2} of where it is solid");
        Assert.True(Share(roomNear, roomCovered) < Share(near, covered) - 0.1, "control: the room's own canvas, unmoved, passes for the game's picture too");
    }
}
