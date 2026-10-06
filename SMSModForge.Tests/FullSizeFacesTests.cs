using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's busts' faces at their full size (the author, 1.7.0): shipped for
/// an outfit of a pack's own that borrows the game's blink, mouth or faces
/// ("= default"), where the smaller copies, stretched back, never matched the
/// pack's 256x256 bust. The game's own outfits keep the smaller copies.
/// </summary>
public sealed class FullSizeFacesTests
{
    private readonly ITestOutputHelper _out;
    public FullSizeFacesTests(ITestOutputHelper o) => _out = o;

    private static readonly Regex Face = new(@"^(Blink|Mouth[1-4]|Expression[A-Za-z0-9_]+)\.PNG$");

    private static (int W, int H) SizeOf(string path)
    {
        using var fs = File.OpenRead(path);
        var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    /// <summary>The repository's Resources folder, for the extraction the
    /// faces are copied from; null when the tests run away from the source.</summary>
    private static string? Resources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "SMSModForge", "Resources");
            if (Directory.Exists(Path.Combine(candidate, "VanillaBustFaces"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void EveryShippedBustsFacesShipAtFullSize_AsTheExtractionHasThem()
    {
        string? faces = VanillaArtResolver.FindFullFacesRoot();
        string? smaller = VanillaArtResolver.FindArtRoot();
        Assert.NotNull(faces);
        Assert.NotNull(smaller);
        string? resources = Resources();

        int checkedFaces = 0;
        foreach (var bust in Directory.GetDirectories(smaller!))
        {
            string name = Path.GetFileName(bust);
            foreach (var small in Directory.GetFiles(bust).Where(f => Face.IsMatch(Path.GetFileName(f))))
            {
                string file = Path.GetFileName(small);
                string? extracted = resources == null ? null : Path.Combine(resources, "VanillaBustArt", name, file);
                if (extracted != null && !File.Exists(extracted)) continue;   // nothing at full size to ship
                string full = Path.Combine(faces!, name, file);
                Assert.True(File.Exists(full), $"{name}/{file} ships only made smaller");
                if (extracted != null)
                    Assert.Equal(File.ReadAllBytes(extracted), File.ReadAllBytes(full));
                checkedFaces++;
            }
        }
        _out.WriteLine($"{checkedFaces} faces checked");
        Assert.True(checkedFaces > 1000, "too few faces to be the shipped set");

        // Only the busts that ship: one left out of the catalog on purpose
        // does not come back by this route.
        var shippedBusts = Directory.GetDirectories(smaller!).Select(Path.GetFileName).ToHashSet();
        Assert.All(Directory.GetDirectories(faces!), d => Assert.Contains(Path.GetFileName(d), shippedBusts));
    }

    [Fact]
    public void ABorrowedFaceIsTheFullSizeOne_AndTheMaskTheSmallerCopy()
    {
        string borrowed = Shared.GameArt.From("Anna_Bust");
        string blink = VanillaArtResolver.GameArtFile(borrowed, "Blink.PNG")!;
        string mouth = VanillaArtResolver.GameArtFile(borrowed, "Mouth2.PNG")!;
        string face = VanillaArtResolver.GameArtFile(borrowed, "ExpressionHappy.PNG")!;
        string mask = VanillaArtResolver.GameArtFile(borrowed, "Mask.PNG")!;
        _out.WriteLine($"blink {blink} {SizeOf(blink)}; mask {mask} {SizeOf(mask)}");

        Assert.All(new[] { blink, mouth, face }, p =>
        {
            Assert.StartsWith(VanillaArtResolver.FindFullFacesRoot()!, p);
            Assert.Equal((256, 256), SizeOf(p));
        });
        Assert.StartsWith(VanillaArtResolver.FindArtRoot()!, mask);
    }

    [Fact]
    public void TheGamesOwnOutfitsKeepTheSmallerCopies()
    {
        // A bust of the game's own, previewed as itself: the smaller copy, as
        // its base is, so every layer of it went through the same trip.
        var pack = PackRepository.CreateEmpty("faces.pack");
        string own = VanillaArtResolver.FindExpressionSpritePath("Anna_Bust", "Happy", pack, null)!;
        _out.WriteLine($"own face {own} {SizeOf(own)}");
        Assert.StartsWith(VanillaArtResolver.FindArtRoot()!, own);
        Assert.NotEqual((256, 256), SizeOf(own));

        // An outfit of the pack's that borrows that face: the full-size one.
        string root = Path.Combine(Path.GetTempPath(), "smsmodforge-faces-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ch = new CharacterDef { Key = "mine", DisplayName = "Mine" };
            var outfit = new OutfitDef { Key = "night", GameObjectName = "Mine_Night" };
            outfit.Expression.Enabled = true;
            outfit.Expression.Prefix = Shared.GameArt.From("Anna_Bust");
            ch.Outfits.Add(outfit);
            pack.Characters.Add(ch);
            string borrowed = VanillaArtResolver.FindExpressionSpritePath("Mine_Night", "Happy", pack, root)!;
            Assert.StartsWith(VanillaArtResolver.FindFullFacesRoot()!, borrowed);
            Assert.Equal((256, 256), SizeOf(borrowed));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
