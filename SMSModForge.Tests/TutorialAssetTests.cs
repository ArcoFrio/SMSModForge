using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Tutorials;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The practice art a tutorial tells someone to pick has to be there.
/// <para/>
/// A step that names a file is the most brittle thing in the catalog: the art
/// is renamed or moved, the prose still says the old name, and the author is
/// left hunting for something that does not exist with no way to tell whether
/// they misread it. That happened once already — one bust's blink shipped under
/// another bust's name.
/// </summary>
public class TutorialAssetTests
{
    /// <summary>
    /// Where the shipped art actually is. TutorialAssets looks beside the
    /// running assembly, which is the editor's own output at run time and the
    /// test host's at test time, so this finds the source folder instead.
    /// </summary>
    private static string AssetRoot
    {
        get
        {
            // The BUILD OUTPUT first, not the repo. The editor's Content glob
            // is what decides whether an asset reaches a machine that is not
            // this one, and a file present in the repo but missing from the
            // glob is the exact failure this test claims to catch - which it
            // could not, while it read the source folder.
            string output = Path.Combine(System.AppContext.BaseDirectory,
                                         "Resources", "TutorialAssets");
            if (Directory.Exists(output)) return output;

            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "SMSModForge",
                                                "Resources", "TutorialAssets");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Resources/TutorialAssets not found");
        }
    }

    private static bool Exists(string packRelative)
    {
        // Paths are written as an author types them: TutorialArt/... inside the
        // pack, which is a copy of the shipped folder.
        const string prefix = TutorialAssets.PackFolder + "/";
        Assert.StartsWith(prefix, packRelative);
        string rel = packRelative.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
        return File.Exists(Path.Combine(AssetRoot, rel));
    }

    public static IEnumerable<object[]> Busts()
        => Enumerable.Range(1, 5).Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Busts))]
    public void A_practice_bust_has_every_piece_a_tutorial_names(int n)
    {
        var missing = new List<string>();

        void Want(string path)
        {
            if (!Exists(path)) missing.Add(path);
        }

        Want(TutorialAssets.Bust(n));
        Want(TutorialAssets.BustBlink(n));

        // The runtime appends the number and the extension itself, so the
        // prefix is only correct if the files it implies are really there.
        for (int i = 1; i <= TutorialAssets.MouthFrameCount; i++)
            Want(TutorialAssets.MouthPrefix(n) + i + ".png");

        foreach (var name in TutorialAssets.ExpressionNames)
            Want(TutorialAssets.ExpressionPrefix(n) + name + ".png");

        Assert.True(missing.Count == 0,
            $"Bust {n} is missing art a tutorial points at:" + System.Environment.NewLine + "  " +
            string.Join(System.Environment.NewLine + "  ", missing));
    }

    [Fact]
    public void The_other_practice_art_is_there_too()
    {
        var missing = new[]
        {
            TutorialAssets.RoomBase, TutorialAssets.RoomSecondary,
            TutorialAssets.Npc(0), TutorialAssets.Npc(1),
            TutorialAssets.NpcBlink(0), TutorialAssets.NpcBlink(1),
            TutorialAssets.Scene, TutorialAssets.Wallpaper,
            // Audio too: the build glob was PNG-only until the Media tutorial
            // needed a sound, and a path that ships in the repo but not in the
            // output is exactly the failure this test exists to catch.
            TutorialAssets.Music, TutorialAssets.Sfx,
            TutorialAssets.Prop,
        }.Where(p => !Exists(p)).ToList();

        Assert.True(missing.Count == 0,
            "Missing practice art:" + System.Environment.NewLine + "  " +
            string.Join(System.Environment.NewLine + "  ", missing));
    }

    [Fact]
    public void Bust_art_is_the_size_the_tutorials_say_it_is()
    {
        // A tutorial states 256x256 as the size to draw. If the art it hands
        // out is not that size, the tutorial is teaching something its own
        // examples contradict.
        var wrong = new List<string>();
        for (int n = 1; n <= 5; n++)
        {
            string rel = TutorialAssets.Bust(n);
            string abs = Path.Combine(AssetRoot,
                rel.Substring((TutorialAssets.PackFolder + "/").Length)
                   .Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(abs)) continue;   // reported by the test above
            if (!SMSModForge.Validation.ArtDimensions.TryReadPngSize(abs, out int w, out int h) ||
                w != TutorialAssets.BustPixels || h != TutorialAssets.BustPixels)
                wrong.Add($"{rel} is {w}x{h}");
        }

        Assert.True(wrong.Count == 0,
            $"Practice busts should be {TutorialAssets.BustPixels} square:" +
            System.Environment.NewLine + "  " +
            string.Join(System.Environment.NewLine + "  ", wrong));
    }
}
