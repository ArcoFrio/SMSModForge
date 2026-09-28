using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A wallpaper whose image the game cannot find has no button either. An
/// author pasted a full path to the image on his own disk; the plugin looked
/// for that path in the pack, skipped the wallpaper, and nothing said so
/// (2026-09-27).
/// </summary>
public sealed class WallpaperImageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-wallpaper-" + Guid.NewGuid().ToString("N"));
    private readonly string _elsewhere = Path.Combine(Path.GetTempPath(), "smsmodforge-elsewhere-" + Guid.NewGuid().ToString("N"));

    public WallpaperImageTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Wallpapers"));
        File.WriteAllBytes(Path.Combine(_root, "Wallpapers", "Elf.png"), new byte[] { 1 });
        Directory.CreateDirectory(_elsewhere);
        File.WriteAllBytes(Path.Combine(_elsewhere, "Far.png"), new byte[] { 1 });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
        try { Directory.Delete(_elsewhere, true); } catch (IOException) { }
    }

    private static ModPack With(string? sprite, string? external = null)
    {
        var pack = PackRepository.CreateEmpty("wallpaper.pack");
        pack.Wallpapers.Add(new WallpaperDef { Key = "elf", DisplayName = "Elf", SpritePath = sprite, ExternalSpritePath = external });
        return pack;
    }

    private string[] Codes(ModPack pack)
        => PackValidator.Validate(pack, _root).Where(i => i.Where.StartsWith("wallpapers.elf.spritePath")).Select(i => i.Code).ToArray();

    [Fact]
    public void AnImageInThePackNamedFromItsFolderIsFine()
        => Assert.Empty(Codes(With("Wallpapers/Elf.png")));

    [Fact]
    public void AFullPathOutsideThePackIsReported_ItWasCheckedAgainstTheAuthorsOwnDiskAndPassed()
        => Assert.Equal(new[] { "art.fullPathOutsidePack" }, Codes(With(Path.Combine(_elsewhere, "Far.png"))));

    [Fact]
    public void AFullPathInsideThePackIsReported()
        => Assert.Equal(new[] { "art.fullPathInPack" }, Codes(With(Path.Combine(_root, "Wallpapers", "Elf.png"))));

    [Fact]
    public void NoImageAtAllIsReported_AndTheExternalPathIsCheckedWhenItIsTheOnlyOne()
    {
        Assert.Equal(new[] { "wallpaper.noImage" }, Codes(With(null)));
        Assert.Empty(Codes(With(null, "Wallpapers/Elf.png")));
        Assert.Equal(new[] { "art.fileNotFound" }, Codes(With("Wallpapers/Missing.png")));
    }

    [Fact]
    public void OnLoad_AFullPathInsideThePackBecomesItsPathInThePack_AndItIsReported()
    {
        var pack = With(Path.Combine(_root, "Wallpapers", "Elf.png"));
        pack.Wallpapers.Add(new WallpaperDef { Key = "far", DisplayName = "Far", SpritePath = Path.Combine(_elsewhere, "Far.png") });
        PackRepository.Save(pack, _root);

        var report = PackMigration.Apply(
            Newtonsoft.Json.JsonConvert.DeserializeObject<ModPack>(File.ReadAllText(Path.Combine(_root, PackRepository.ManifestFileName)))!,
            _root);
        Assert.Contains("full path", report.Describe());

        var loaded = PackRepository.Load(_root);
        Assert.Equal("Wallpapers/Elf.png", loaded.Wallpapers.Single(w => w.Key == "elf").SpritePath);
        // Outside the pack there is nothing to give it: left, and reported by the validator.
        Assert.Equal(Path.Combine(_elsewhere, "Far.png"), loaded.Wallpapers.Single(w => w.Key == "far").SpritePath);
    }
}
