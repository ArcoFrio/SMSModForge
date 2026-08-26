using System;
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
    public static string RoomBase => $"{PackFolder}/Locations/RoomB.png";
    public static string RoomSecondary => $"{PackFolder}/Locations/Room.png";
    public static string Npc(int n) => $"{PackFolder}/NPCs/Dummy/DummyNPC{n}.png";
    public static string Scene => $"{PackFolder}/Scenes/Dummy/DummyScene01.png";

    /// <summary>Wallpaper art. Separate from <see cref="Scene"/> on purpose:
    /// the two are different features that happen to both be pictures, and the
    /// tutorials used the scene art for both until it was pointed out.</summary>
    public static string Wallpaper => $"{PackFolder}/Wallpapers/DummyWPP.png";

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
           Directory.EnumerateFiles(Path.Combine(packRoot!, PackFolder), "*.png",
                                    SearchOption.AllDirectories).Any();
}
