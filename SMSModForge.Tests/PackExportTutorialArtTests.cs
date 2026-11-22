using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Tutorials;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The tutorials' practice art stays out of an export when it is the file ModForge ships, unchanged - the plugin
/// carries it (1.10.0; the author: "I've seen cases of modpacks packed with the tutorial art. Maybe we can make
/// them default assets packed within the plugin instead?"). A copy the author changed, and a file of their own in
/// that folder, are theirs, and ship.
/// </summary>
public sealed class PackExportTutorialArtTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForge-exporttutorialart-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Pack => Path.Combine(_root, "pack");

    private string Copy(string shippedInside, string asRel)
    {
        string to = Path.Combine(Pack, asRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(Path.Combine(TutorialAssets.SourceRoot, shippedInside.Replace('/', Path.DirectorySeparatorChar)), to, true);
        return to;
    }

    [Fact]
    public void UnchangedPracticeArtIsLeftOut_AChangedCopyAndTheAuthorsOwnFilesShip()
    {
        Assert.True(Directory.Exists(TutorialAssets.SourceRoot), "the editor's practice art is not beside the tests");
        Directory.CreateDirectory(Pack);
        File.WriteAllText(Path.Combine(Pack, "modpack.json"), "{ \"packId\": \"practice\" }");
        Copy("Busts/Bust1/Mouth1.png", "TutorialArt/Busts/Bust1/Mouth1.png");                 // unchanged: left out
        Copy("Wallpapers/DummyWPP.png", "TutorialArt/Wallpapers/DummyWPP.png");               // unchanged: left out
        // A copy the author painted over: theirs now.
        string changed = Copy("Busts/Bust1/Mouth2.png", "TutorialArt/Busts/Bust1/Mouth2.png");
        using (var f = File.OpenWrite(changed)) { f.Seek(0, SeekOrigin.End); f.WriteByte(0); }
        // A file of their own in that folder, a shipped file put elsewhere, and a mask the tutorials write.
        File.WriteAllText(Path.Combine(Pack, "TutorialArt", "Mine.png"), "mine");
        Copy("Busts/Bust1/Mouth3.png", "Art/Mouth3.png");
        File.WriteAllText(Path.Combine(Pack, "TutorialArt", "Busts", "Bust1", "Bust1Base00Mask.png"), "mask");

        string output = Path.Combine(_root, "out.smspack");
        var result = PackExporter.Export(Pack, output);

        using var zip = ZipFile.OpenRead(output);
        var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "Art/Mouth3.png", "TutorialArt/Busts/Bust1/Bust1Base00Mask.png", "TutorialArt/Busts/Bust1/Mouth2.png",
            "TutorialArt/Mine.png", "modpack.json",
        }, names);
        Assert.Equal(new[] { "TutorialArt/Busts/Bust1/Mouth1.png", "TutorialArt/Wallpapers/DummyWPP.png" },
                     result.TutorialArtLeftOut.Select(p => p.Replace('\\', '/')).OrderBy(n => n, StringComparer.Ordinal));
        Assert.True(result.TutorialArtBytes > 0);
    }

    [Theory]
    [InlineData("TutorialArt/Busts/Bust1/Mouth1.png", "Busts/Bust1/Mouth1.png")]
    [InlineData("TutorialArt\\Busts\\Bust1\\Mouth1.png", "Busts/Bust1/Mouth1.png")]
    [InlineData("./tutorialart/UI/Cover.png", "UI/Cover.png")]
    [InlineData("Art/TutorialArt/Cover.png", null)]
    [InlineData("TutorialArt/../modpack.json", null)]
    [InlineData("TutorialArt/", null)]
    [InlineData("", null)]
    public void APathMeansTheShippedArtOnlyInsideItsFolder(string packPath, string? inside)
        => Assert.Equal(inside, SMSModForge.Shared.TutorialArt.Inside(packPath));
}
