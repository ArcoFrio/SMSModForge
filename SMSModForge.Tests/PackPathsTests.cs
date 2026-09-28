using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A file a pack names by a full path on the author's machine is found in
/// the pack by its ending - the wallpaper an author pasted as
/// "Z:\...\Elfenlied\Wallpapers\Elf.png" and shipped at Wallpapers/Elf.png.
/// </summary>
public sealed class PackPathsTests
{
    private static readonly string[] InPack = { "modpack.json", "Wallpapers/Elf.png", "Elf.png", "Music/theme.ogg" };

    [Theory]
    [InlineData(@"Z:\Pixel games\Starmaker Modding\Elfenlied\Wallpapers\Elf.png", "Wallpapers/Elf.png")]
    [InlineData("Z:/Pixel games/Elfenlied/Wallpapers/Elf.png", "Wallpapers/Elf.png")]
    [InlineData(@"z:\PIXEL\elfenlied\WALLPAPERS\elf.PNG", "Wallpapers/Elf.png")]
    // "MyWallpapers" is not "Wallpapers": only the file name matches, on a folder boundary.
    [InlineData(@"Z:\Modding\MyWallpapers\Elf.png", "Elf.png")]
    [InlineData(@"\server\share\Music\theme.ogg", "Music/theme.ogg")]
    public void AFullPathIsFoundByItsEnding(string full, string expected)
        => Assert.Equal(expected, PackPaths.FindByEnding(full, InPack));

    [Theory]
    // Not in the pack at all.
    [InlineData(@"Z:\Modding\Elfenlied\Wallpapers\Other.png")]
    // "xElf.png" ends with "Elf.png" but not after a folder separator.
    [InlineData(@"Z:\Modding\xElf.png")]
    // Not a full path: looked up as it is, never by its ending.
    [InlineData("Other/Wallpapers/Elf.png")]
    public void NothingIsFoundThatTheEndingDoesNotName(string path)
        => Assert.Null(PackPaths.FindByEnding(path, InPack));

    [Theory]
    [InlineData(@"C:\x.png", true)]
    [InlineData("C:/x.png", true)]
    [InlineData(@"\server\x.png", true)]
    [InlineData("/x.png", true)]
    [InlineData("Wallpapers/x.png", false)]
    [InlineData("C:x.png", false)]
    [InlineData("", false)]
    public void WhatCountsAsAFullPath(string path, bool full)
        => Assert.Equal(full, PackPaths.IsFullPath(path));
}
