using System.Collections.Generic;
using System.Linq;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Which installed copy of a pack actually loads.
/// <para/>
/// Packs are read from two folders now — <c>Mods</c> beside the game and the
/// older location beside the plugin — so the same pack in both is the normal
/// shape of a half-finished move. The failure it produces is nasty and quiet:
/// an author edits the copy that lost and spends an evening wondering why
/// nothing changes.
/// <para/>
/// The rule lives in <see cref="PackInstallScan"/>, compiled into the runtime
/// and into this test project, because the runtime loads into a game this
/// repository cannot start. Testing it here is the only way it gets tested at
/// all — and both the loader and the menu banner call it, so they cannot
/// disagree about which copy is live.
/// </summary>
public sealed class PackInstallScanTests
{
    private readonly ITestOutputHelper _out;
    public PackInstallScanTests(ITestOutputHelper o) => _out = o;

    private const string Mods = @"C:\Game\Mods\";
    private const string Old = @"C:\Game\BepInEx\plugins\SMSModForge\ModPacks\";

    private static List<PackInstallScan.Candidate> Found(params (string Path, string Id)[] files)
        => files.Select(f => new PackInstallScan.Candidate(f.Path, f.Id)).ToList();

    [Fact]
    public void OneCopyLoadsAndNothingIsFlagged()
    {
        // The ordinary case, and the control for everything below: a normal
        // install must not be painted as a problem.
        var live = PackInstallScan.Resolve(Found(
            (Mods + "a.smspack", "alpha"),
            (Mods + "b.smspack", "beta")));

        Assert.Equal(2, live.Count);
        Assert.All(live, one => Assert.False(one.IsDuplicated));
        Assert.All(live, one => Assert.Empty(one.Shadowed));
    }

    [Fact]
    public void TheFirstFolderWinsAndTheOtherIsNamed()
    {
        // Priority order is the caller's: Mods is passed first because that is
        // the location people are told about now. Moving a pack there has to
        // take effect even when the old copy was left behind.
        var live = PackInstallScan.Resolve(Found(
            (Mods + "mypack.smspack", "my.pack"),
            (Old + "mypack.smspack", "my.pack")));

        var one = Assert.Single(live);
        _out.WriteLine($"{one.PackId}: loading {one.Path}, ignoring "
                       + string.Join(", ", one.Shadowed));

        Assert.Equal(Mods + "mypack.smspack", one.Path);
        Assert.True(one.IsDuplicated);
        Assert.Equal(Old + "mypack.smspack", Assert.Single(one.Shadowed));

        // And the folder names are what a menu row can actually show without
        // pasting a full path into it.
        Assert.Equal("Mods", PackInstallScan.FolderName(one.Path));
        Assert.Equal("ModPacks", PackInstallScan.FolderName(one.Shadowed[0]));
    }

    [Fact]
    public void TwoFilesInTheSameFolderCollideToo()
    {
        // The pack id lives inside the archive and has nothing to do with the
        // file name, so a renamed spare copy is a duplicate the file system is
        // perfectly happy with.
        var live = PackInstallScan.Resolve(Found(
            (Mods + "mypack.smspack", "my.pack"),
            (Mods + "mypack-backup.smspack", "my.pack")));

        var one = Assert.Single(live);
        _out.WriteLine($"ignoring {one.Shadowed[0]}");
        Assert.True(one.IsDuplicated);
    }

    [Fact]
    public void ThreeCopiesAreAllReported()
    {
        // Not just the second one. Deleting one copy and still seeing the
        // warning would read as the warning being broken.
        var live = PackInstallScan.Resolve(Found(
            (Mods + "a.smspack", "my.pack"),
            (Mods + "b.smspack", "my.pack"),
            (Old + "c.smspack", "my.pack")));

        var one = Assert.Single(live);
        Assert.Equal(2, one.Shadowed.Count);
    }

    [Fact]
    public void PacksThatCouldNotBeReadAreNotDuplicatesOfEachOther()
    {
        // The control that matters most. An empty id is "this file is broken",
        // not "this file is the same pack as that other broken file" - and
        // collapsing them would hide one behind the other, so somebody would
        // fix one archive and still see a problem with no second row to
        // explain it.
        var live = PackInstallScan.Resolve(Found(
            (Mods + "broken1.smspack", ""),
            (Mods + "broken2.smspack", "")));

        Assert.Equal(2, live.Count);
        Assert.All(live, one => Assert.False(one.IsDuplicated));
    }

    [Fact]
    public void AnIdIsMatchedWithoutRegardToCase()
    {
        // Windows file systems do not care, and neither does a person typing a
        // pack id. Two copies differing only in case are one pack.
        var live = PackInstallScan.Resolve(Found(
            (Mods + "a.smspack", "My.Pack"),
            (Old + "b.smspack", "my.pack")));

        Assert.Single(live);
        Assert.True(live[0].IsDuplicated);
    }

    [Fact]
    public void NothingInstalledIsNotAProblem()
    {
        Assert.Empty(PackInstallScan.Resolve(new List<PackInstallScan.Candidate>()));
        Assert.Empty(PackInstallScan.Resolve(null!));
    }

    [Fact]
    public void AFolderNameIsAnswerableForAnythingItIsGiven()
    {
        // It goes straight into a menu row, so it must never throw and never
        // hand back something a person cannot read.
        Assert.Equal("Mods", PackInstallScan.FolderName(Mods + "a.smspack"));
        Assert.Equal("", PackInstallScan.FolderName(""));
        Assert.Equal("", PackInstallScan.FolderName(null!));
        Assert.Equal("", PackInstallScan.FolderName("bare.smspack"));
    }
}
