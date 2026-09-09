using System.IO;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A pack's own version: reading it, moving it, and deciding which part moved.
/// <para/>
/// The number is shown to players beside the pack's name, so the failure that
/// matters is a version that says the wrong thing — one that climbs when
/// nothing happened, stands still when something did, or announces a release
/// nobody made.
/// </summary>
public sealed class PackVersionTests
{
    private readonly ITestOutputHelper _out;
    public PackVersionTests(ITestOutputHelper o) => _out = o;

    // ── Reading and writing ──────────────────────────────────────────

    [Theory]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData(" 10.0.41 ", 10, 0, 41)]
    public void AVersionReadsBackAsItself(string text, int major, int minor, int patch)
    {
        var read = PackVersion.Parse(text);
        Assert.NotNull(read);
        Assert.Equal(new PackVersion(major, minor, patch), read!.Value);
        Assert.Equal($"{major}.{minor}.{patch}", read.Value.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("v1.2.3")]
    [InlineData("-1.0.0")]
    public void SomethingThatIsNotAVersionIsRefused(string? text)
    {
        // Refused rather than coerced: a version that quietly parses as
        // something else shows the author a number they did not type.
        Assert.Null(PackVersion.Parse(text));
    }

    [Fact]
    public void MovingOnePartResetsTheOnesBelowIt()
    {
        var at = new PackVersion(1, 4, 7);

        Assert.Equal("1.4.8", at.NextPatch().ToString());
        Assert.Equal("1.5.0", at.NextMinor().ToString());

        // 2.0.3 would claim three fixes to a 2.0 nobody shipped.
        Assert.Equal("2.0.0", at.NextMajor().ToString());
    }

    [Fact]
    public void VersionsCompareTheWayPlayersReadThem()
    {
        Assert.True(new PackVersion(1, 0, 0) > new PackVersion(0, 9, 9));
        Assert.True(new PackVersion(1, 2, 0) > new PackVersion(1, 1, 9));
        Assert.True(new PackVersion(1, 2, 10) > new PackVersion(1, 2, 9));
        Assert.True(new PackVersion(1, 2, 3) == new PackVersion(1, 2, 3));
    }

    // ── Which part a save moves ──────────────────────────────────────

    private static string Manifest(string version, params string[] dialogueKeys)
    {
        var pack = new ModPack { PackId = "v.pack", Version = version };
        foreach (string key in dialogueKeys)
            pack.Dialogues.Add(new DialogueDef { Key = key, DisplayName = key });
        return PackRepository.SerializeAsSaved(pack);
    }

    [Fact]
    public void SavingWithNothingChangedMovesNothing()
    {
        // An author who saves twice without touching anything should not climb
        // a number for it.
        string same = Manifest("1.0.0", "intro");
        Assert.Equal(VersionBump.Change.None, VersionBump.Classify(same, same));

        Assert.Equal(new PackVersion(1, 0, 0),
                     VersionBump.Next(new PackVersion(1, 0, 0), VersionBump.Change.None));
    }

    [Fact]
    public void ANewRecordIsAnAddition()
    {
        var change = VersionBump.Classify(Manifest("1.0.0", "intro"),
                                          Manifest("1.0.0", "intro", "beach"));
        _out.WriteLine($"added a dialogue -> {change}");

        Assert.Equal(VersionBump.Change.Minor, change);
        Assert.Equal(new PackVersion(1, 1, 0),
                     VersionBump.Next(new PackVersion(1, 0, 0), change));
    }

    [Fact]
    public void ChangingSomethingThatWasAlreadyThereIsAPatch()
    {
        var before = new ModPack { PackId = "v.pack", Version = "1.0.0" };
        before.Dialogues.Add(new DialogueDef { Key = "intro", DisplayName = "Intro" });
        string was = PackRepository.SerializeAsSaved(before);

        before.Dialogues[0].DisplayName = "Introduction";
        string now = PackRepository.SerializeAsSaved(before);

        var change = VersionBump.Classify(was, now);
        _out.WriteLine($"renamed a display name -> {change}");

        Assert.Equal(VersionBump.Change.Patch, change);
        Assert.Equal(new PackVersion(1, 0, 1),
                     VersionBump.Next(new PackVersion(1, 0, 0), change));
    }

    [Fact]
    public void AddingAndFixingInOneSaveCountsAsAnAddition()
    {
        // A save that does both is, on the whole, an addition - the pack has
        // something it did not have, and that is the larger claim.
        var before = new ModPack { PackId = "v.pack", Version = "1.0.0" };
        before.Dialogues.Add(new DialogueDef { Key = "intro", DisplayName = "Intro" });
        string was = PackRepository.SerializeAsSaved(before);

        before.Dialogues[0].DisplayName = "Introduction";
        before.Dialogues.Add(new DialogueDef { Key = "beach", DisplayName = "Beach" });
        string now = PackRepository.SerializeAsSaved(before);

        Assert.Equal(VersionBump.Change.Minor, VersionBump.Classify(was, now));
    }

    [Fact]
    public void ChangingOneOfTheGamesConversationsCountsAsAnAddition()
    {
        // The pack did not alter that conversation before and does now, which
        // from a player's side is a new thing the pack does rather than a fix.
        var before = new ModPack { PackId = "v.pack", Version = "1.0.0" };
        string was = PackRepository.SerializeAsSaved(before);

        before.Dialogues.Add(new DialogueDef
        {
            Key = "beachextension",
            Source = VanillaDialogueCatalog.TokenPrefix + "8_Room_Talk/Beach/AnnaBeachDefault",
        });
        string now = PackRepository.SerializeAsSaved(before);

        Assert.Equal(VersionBump.Change.Minor, VersionBump.Classify(was, now));
    }

    [Fact]
    public void TheVersionItselfMovingIsNotAChange()
    {
        // Otherwise every save would see the previous save's bump as a reason
        // to bump again, and the number would climb on its own for ever.
        Assert.Equal(VersionBump.Change.None,
                     VersionBump.Classify(Manifest("1.0.0", "intro"),
                                          Manifest("9.9.9", "intro")));
    }

    [Fact]
    public void TheFirstSaveOfAPackIsAnAddition()
    {
        Assert.Equal(VersionBump.Change.Minor, VersionBump.Classify(null, Manifest("0.0.0")));
        Assert.Equal(VersionBump.Change.Minor, VersionBump.Classify("", Manifest("0.0.0")));
    }

    // ── Where a pack starts ──────────────────────────────────────────

    [Fact]
    public void ANewPackStartsAtZeroAndAnOldOneAtPointOne()
    {
        var made = PackRepository.CreateEmpty("fresh.pack");
        Assert.Equal(PackVersion.New, made.PackVersion);
        Assert.Equal("0.0.0", made.Version);

        // A pack that predates versioning has content in it, often a lot, so
        // calling it "nothing yet" would be wrong the moment somebody looked.
        var old = new ModPack { PackId = "old.pack" };
        old.Dialogues.Add(new DialogueDef { Key = "intro" });

        var report = PackMigration.Apply(old);
        _out.WriteLine(report.Describe());

        Assert.Equal(PackVersion.Existing, old.PackVersion);
        Assert.Equal("0.1.0", old.Version);
        Assert.Contains(report.Changes, c => c.What.Contains("Given a version"));
    }

    [Fact]
    public void APackThatAlreadyHasAVersionKeepsIt()
    {
        // The control, and the reason it matters: this runs on every load,
        // including the one after a save. Overwriting here would reset an
        // author's release number every time they opened their pack.
        var pack = new ModPack { PackId = "v.pack", Version = "3.4.5" };

        var report = PackMigration.Apply(pack);

        Assert.Equal("3.4.5", pack.Version);

        // Matched on what the note actually says rather than on the word
        // "version": other migrations mention one too, and a substring this
        // loose would fail the moment any of them did.
        Assert.DoesNotContain(report.Changes, c => c.What.Contains("Given a version"));
    }

    [Fact]
    public void APackWithAnUnreadableVersionIsGivenARealOne()
    {
        var pack = new ModPack { PackId = "v.pack", Version = "not a version" };
        PackMigration.Apply(pack);
        Assert.Equal("0.1.0", pack.Version);
    }
    [Fact]
    public void TheBackwardsCheckReadsAResetAsForwards()
    {
        // What the "you are going backwards" safeguard compares. If it went by
        // the last number it would see 0 arriving after 1 and warn an author
        // about the version publishing had just chosen for them.
        Assert.True(new PackVersion(0, 2, 0) > new PackVersion(0, 1, 1));
        Assert.True(new PackVersion(0, 2, 0) > new PackVersion(0, 1, 99));
        Assert.True(new PackVersion(1, 0, 0) > new PackVersion(0, 9, 9));

        // And it still catches a real step backwards.
        Assert.True(new PackVersion(0, 1, 1) < new PackVersion(0, 2, 0));
        Assert.True(new PackVersion(0, 9, 9) < new PackVersion(1, 0, 0));
    }
}
