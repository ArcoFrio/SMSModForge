using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SMSModForge.Tutorials;

/// <summary>
/// The dummy art the tutorials work with, and the copy that puts it inside the
/// author's pack.
/// <para/>
/// It ships with the editor (see <c>Resources/TutorialAssets</c> in the project
/// file) rather than being something the author has to find: a tutorial whose
/// sprites only exist on the machine it was written on is not a tutorial. But
/// it is copied INTO whatever pack folder the author has saved, never read
/// from where it ships — a pack has to be self-contained to export, and an
/// author who later swaps our placeholder for their own art should be editing
/// a file that belongs to them.
/// <para/>
/// That also means no manifest is ever written next to the shipped originals,
/// so there is nothing in that folder to keep out of a build.
/// </summary>
public static class TutorialAssets
{
    /// <summary>Where the shipped art sits beside the executable.</summary>
    public static string SourceRoot => Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
        "Resources", "TutorialAssets");

    /// <summary>Folder inside the author's pack that copies land in.</summary>
    public const string PackFolder = "TutorialArt";

    // Pack-relative paths, as an author would type them into a sprite field.
    public static string Bust(int n) => $"{PackFolder}/Busts/Bust{n}/Bust{n}Base00.png";

    /// <summary>Eyes-closed frame for one of the practice busts.</summary>
    public static string BustBlink(int n) => $"{PackFolder}/Busts/Bust{n}/Bust{n}Blink.png";

    /// <summary>
    /// Stem for a bust's four mouth frames. The runtime appends 1..4 and the
    /// extension, so this is what goes in the Mouth prefix box verbatim —
    /// a step can therefore quote the exact string an author has to type.
    /// </summary>
    public static string MouthPrefix(int n) => $"{PackFolder}/Busts/Bust{n}/Mouth";

    /// <summary>
    /// Stem for a bust's four expressions. The runtime appends Happy, Angry,
    /// Sad or Flirty and the extension — the four names are fixed, so art for
    /// any other name is never looked up.
    /// </summary>
    public static string ExpressionPrefix(int n) => $"{PackFolder}/Busts/Bust{n}/Expression";

    /// <summary>The expression names the runtime knows, in the order the
    /// editor lists them. Anything else in an Expression box resolves to
    /// nothing.</summary>
    public static readonly string[] ExpressionNames = { "Happy", "Angry", "Sad", "Flirty" };

    /// <summary>How many mouth frames a bust has. Fixed: the runtime looks up
    /// exactly 1 through 4.</summary>
    public const int MouthFrameCount = 4;

    /// <summary>
    /// The size every bust frame is authored at. Art of another size is fitted
    /// to it rather than rejected, but authoring at this size is the way to
    /// know exactly what a player will see.
    /// </summary>
    public const int BustPixels = 256;
    public static string RoomBase => $"{PackFolder}/Locations/RoomB.png";
    public static string RoomSecondary => $"{PackFolder}/Locations/Room.png";
    public static string Npc(int n) => $"{PackFolder}/NPCs/Dummy/DummyNPC{n}.png";

    /// <summary>
    /// A practice jiggle mask for a bust, for "Do it for me" on the mask step (1.10.0): Bounce over the chest,
    /// soft towards its edge, nothing on the face or the outline - written beside the bust art and named as the
    /// mask studio offers (Bust4Base00.png: Bust4Base00Mask.png). Drawn for the practice busts' pose.
    /// </summary>
    public static string WriteMask(string packRoot, int bust)
        => WriteMask(packRoot, $"{PackFolder}/Busts/Bust{bust}/Bust{bust}Base00Mask.png", 112, 118, 58, 36);

    /// <summary>
    /// Where a pose's feet are, as a placement's Shadow offset (1.10.0, the shadow steps of the NPC tutorials): the
    /// middle of the lowest of its opaque pixels, in the units the shadow moves in - the pose is drawn at 100 pixels a
    /// unit around its middle, and the shadow is its child. A new placement's shadow sits 2 below the middle, where the
    /// game's own NPCs have their feet; a taller pose's are lower, and its shadow hides behind its legs. Null when
    /// the picture cannot be read.
    /// </summary>
    public static (float X, float Y)? FeetOf(string packRoot, string poseRel)
    {
        try
        {
            string abs = Path.Combine(packRoot, poseRel.Replace('/', Path.DirectorySeparatorChar));
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(abs), System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            bool Solid(int x, int y) => px[(y * w + x) * 4 + 3] > 128;
            int bottom = -1;
            for (int y = h - 1; y >= 0 && bottom < 0; y--)
                for (int x = 0; x < w; x++)
                    if (Solid(x, y)) { bottom = y; break; }
            if (bottom < 0) return null;
            // The feet: what is solid in the lowest stretch of the figure, a twentieth of the picture high.
            int minX = w, maxX = -1, band = Math.Max(1, h / 20);
            for (int y = Math.Max(0, bottom - band); y <= bottom; y++)
                for (int x = 0; x < w; x++)
                    if (Solid(x, y)) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); }
            const double perUnit = 100.0;
            float fx = (float)Math.Round(((minX + maxX) / 2.0 - w / 2.0) / perUnit, 2);
            // Lifted a little, so the shadow's middle is at the soles rather than below them.
            float fy = (float)Math.Round(-(bottom - h / 2.0) / perUnit + 0.15, 2);
            return (fx, fy);
        }
        catch (Exception) { return null; }
    }

    /// <summary>The same for a practice NPC's pose (DummyNPC0.png: DummyNPC0Mask.png): a little Bounce over the
    /// chest - breathing, not bouncing.</summary>
    public static string WriteNpcMask(string packRoot, int pose)
        => WriteMask(packRoot, $"{PackFolder}/NPCs/Dummy/DummyNPC{pose}Mask.png", 117, 76, 34, 21);

    /// <summary>A mask with Bounce in a soft ellipse (centre and radii in a 256-pixel frame), written into the pack.</summary>
    private static string WriteMask(string packRoot, string rel, double ex, double ey, double erx, double ery)
    {
        int n = Rendering.MaskBuffer.Size;
        var mask = new Rendering.MaskBuffer();
        double cx = n * ex / 256, cy = n * ey / 256, rx = n * erx / 256, ry = n * ery / 256;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double dx = (x - cx) / rx, dy = (y - cy) / ry, d2 = dx * dx + dy * dy;
                mask.R[y * n + x] = d2 >= 1 ? (byte)0 : (byte)Math.Round(200 * (1 - d2));
            }
        var bgra = new byte[n * n * 4];
        mask.ToBgra(bgra);
        var picture = System.Windows.Media.Imaging.BitmapSource.Create(
            n, n, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bgra, n * 4);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(picture));
        string abs = Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        using (var file = File.Create(abs)) png.Save(file);
        return rel;
    }

    /// <summary>Eyes-closed frame for one of the practice NPCs. Drawn over the
    /// pose the same way a bust's blink is drawn over its base.</summary>
    public static string NpcBlink(int n) => $"{PackFolder}/NPCs/Dummy/DummyNPC{n}Blink.png";
    public static string Scene => $"{PackFolder}/Scenes/Dummy/DummyScene01.png";

    /// <summary>
    /// A prop: a vase, drawn at its own size on a transparent background.
    /// <para/>
    /// Deliberately NOT a level layer and not NPC art. An object in a room is
    /// neither, and handing an author one of those to stand in for a prop
    /// teaches them the wrong shape for the next thing they draw.
    /// </summary>
    public static string Prop => $"{PackFolder}/Locations/Vase.png";

    /// <summary>
    /// A music track. An MP3 on purpose: the game takes OGG, WAV and MP3, and a
    /// practice pack that only ever shows one of them teaches that one is the
    /// format.
    /// <para/>
    /// The name is the file's own. Practice art can be called whatever we like;
    /// audio from elsewhere carries its attribution in its filename, and
    /// renaming it would quietly strip that.
    /// </summary>
    public static string Music => $"{PackFolder}/Audio/Neon Rain.mp3";   // English on purpose: a file's name.

    /// <summary>A sound effect — a door opening, as a WAV.</summary>
    public static string Sfx => $"{PackFolder}/Audio/15419__pagancow__dorm-door-opening.wav";

    /// <summary>Wallpaper art. Separate from <see cref="Scene"/> on purpose:
    /// the two are different features that happen to both be pictures, and the
    /// tutorials used the scene art for both until it was pointed out.</summary>
    public static string Wallpaper => $"{PackFolder}/Wallpapers/DummyWPP.png";

    // The deeper chapters' art (1.10.0), drawn for ModForge in the game's style with the author's Forge.
    /// <summary>A second wallpaper: her among her photos, unlocked by either of two things (the conditions chapter).</summary>
    public static string Wallpaper2 => $"{PackFolder}/Wallpapers/DummyWPP2.png";
    /// <summary>Her second and third poses, 256 px pixel art as the game's scenes are.</summary>
    public static string ScenePose(int n) => $"{PackFolder}/Scenes/Dummy/DummyScene0{n}.png";
    /// <summary>Her portfolio's cover, a picture for a screen of the UI tab.</summary>
    public static string Cover => $"{PackFolder}/UI/Cover.png";
    /// <summary>A framed picture of her for the gallery's wall: a place's object with see-through edges.</summary>
    public static string Poster => $"{PackFolder}/Locations/Poster.png";

    /// <summary>Her bolder poster, swapped in by a line (Her gallery moves, 1.10.0).</summary>
    public static string Poster2 => $"{PackFolder}/Locations/Poster2.png";

    /// <summary>A neon star that blinks (Places, more).</summary>
    public static string NeonStar => $"{PackFolder}/Locations/NeonStar.png";

    /// <summary>Three pots of flowers, one shown at a time (Places, more). English on purpose: objects' names.</summary>
    public static IReadOnlyList<(string Name, string Art)> Flowers { get; } = new[]
    {
        ("Roses", $"{PackFolder}/Locations/FlowersRoses.png"),
        ("Sunflowers", $"{PackFolder}/Locations/FlowersSunflowers.png"),
        ("Lavender", $"{PackFolder}/Locations/FlowersLavender.png"),
    };

    /// <summary>A window's panes, for a light shaped like them (Places, more).</summary>
    public static string WindowLight => $"{PackFolder}/Locations/WindowLight.png";
    /// <summary>A gallery visitor, a second kind of NPC: one 1024 px pose, like DummyNPC0.</summary>
    public static string Visitor => $"{PackFolder}/NPCs/Visitor/Visitor0.png";

    /// <summary>
    /// Copies the shipped art into <paramref name="packRoot"/> if it is not
    /// already there, and reports whether the art is now available.
    /// <para/>
    /// Existing files are left alone: re-running a tutorial must not overwrite
    /// a mask the author painted, or art they replaced with their own.
    /// </summary>
    public static bool EnsureCopied(string? packRoot)
    {
        if (string.IsNullOrEmpty(packRoot) || !Directory.Exists(packRoot)) return false;

        string src = SourceRoot;
        if (!Directory.Exists(src)) return false;

        string dst = Path.Combine(packRoot, PackFolder);
        try
        {
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(src, file);
                string target = Path.Combine(dst, rel);
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Whether the art is present in this pack — what a step checks
    /// before asking the author to pick one of the sprites.</summary>
    public static bool IsCopied(string? packRoot)
        => !string.IsNullOrEmpty(packRoot) &&
           Directory.Exists(Path.Combine(packRoot!, PackFolder)) &&
           // Any file, not just a PNG: the practice assets are audio as well as
           // art now, and a check that only counted pictures would call the
           // folder empty for a tutorial that needs a sound.
           Directory.EnumerateFiles(Path.Combine(packRoot!, PackFolder), "*",
                                    SearchOption.AllDirectories).Any();
}
