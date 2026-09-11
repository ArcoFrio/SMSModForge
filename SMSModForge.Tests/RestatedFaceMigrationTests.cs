using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Dropping the expression rows a borrowed character carries that say nothing.
/// <para/>
/// Declaring one of the game's characters used to mean writing the whole thing
/// down, and the editor filled in five standard expressions while it was there:
/// <c>neutral</c> mapped to nothing, and Happy/Angry/Sad/Flirty each mapped to a
/// child of their own name. All five are exactly what the runtime does with no
/// entry at all — invisible, until the editor started listing the faces the game
/// gives a character. Then they appeared underneath those, spelling the same
/// four a second time.
/// </summary>
public sealed class RestatedFaceMigrationTests
{
    private readonly ITestOutputHelper _out;
    public RestatedFaceMigrationTests(ITestOutputHelper o) => _out = o;

    /// <summary>What the old editor wrote into every character it touched.</summary>
    private static void AddTheOldBoilerplate(CharacterDef c)
    {
        c.Expressions.Add(new ActorExpressionDef { Key = "neutral", ExpressionGoName = "" });
        foreach (string face in new[] { "Happy", "Angry", "Sad", "Flirty" })
            c.Expressions.Add(new ActorExpressionDef { Key = face, ExpressionGoName = face });
    }

    private static (ModPack Pack, CharacterDef Kate) Seeded()
    {
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        // Stamped as current on purpose. Anything OLDER than 1.3.0 is taken
        // wholesale by ResetBorrowedCharactersWrittenBefore, which leaves these
        // narrower passes nothing to find; from 1.3.0 on they are what catches
        // a pack that drifts - one hand-edited, or one the editor let into a
        // state it no longer offers.
        pack.ForgeVersion = SMSModForge.Shared.ForgeVersion.Current;

        return (pack, pack.Characters.Single(c => c.VanillaCharacter == "Kate"));
    }

    [Fact]
    public void TheOldBoilerplateGoesAndIsReported()
    {
        var (pack, kate) = Seeded();
        AddTheOldBoilerplate(kate);
        Assert.Equal(5, kate.Expressions.Count);

        var report = PackMigration.Apply(pack);
        _out.WriteLine(report.Describe());

        Assert.Empty(kate.Expressions);
        Assert.Contains("restated", report.Describe(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunningItTwiceChangesNothingTheSecondTime()
    {
        // Every migration runs on every load, including the load right after a
        // save. One that reported something each time would tell an author
        // their pack had changed when nothing had, and take a backup to say it.
        var (pack, kate) = Seeded();
        AddTheOldBoilerplate(kate);

        PackMigration.Apply(pack);
        var again = PackMigration.Apply(pack);

        Assert.DoesNotContain("restated", again.Describe(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFaceAimedAtSomebodyElsesArtIsLeftAlone()
    {
        // The control, and the reason this is not simply "delete the
        // expressions". Pointing one of the game's names at a DIFFERENT child
        // is the whole reason the mapping exists - an author who did it has
        // said something, and it is not a restatement.
        var (pack, kate) = Seeded();
        kate.Expressions.Add(new ActorExpressionDef { Key = "Happy", ExpressionGoName = "Smile" });
        kate.Expressions.Add(new ActorExpressionDef { Key = "Smirk", ExpressionGoName = "Smirk" });
        kate.Expressions.Add(new ActorExpressionDef { Key = "Angry", ExpressionGoName = "Angry" });

        PackMigration.Apply(pack);

        _out.WriteLine(string.Join(", ", kate.Expressions.Select(e => $"{e.Key}->{e.ExpressionGoName}")));
        Assert.Equal(2, kate.Expressions.Count);
        Assert.Contains(kate.Expressions, e => e.Key == "Happy" && e.ExpressionGoName == "Smile");
        Assert.Contains(kate.Expressions, e => e.Key == "Smirk");
    }

    [Fact]
    public void APacksOwnCharacterKeepsItsList()
    {
        // On one of the game's characters the editor shows the game's faces
        // above the pack's, so a copy of them is a duplicate. On a pack's own
        // character that list is the only list there is, and emptying it would
        // take away the only place the faces are named.
        var pack = PackRepository.CreateEmpty("faces.pack");
        pack.ForgeVersion = SMSModForge.Shared.ForgeVersion.Current;
        var mine = new CharacterDef { Key = "sarah", Name = "Sarah", DisplayName = "Sarah" };
        AddTheOldBoilerplate(mine);
        pack.Characters.Add(mine);

        PackMigration.Apply(pack);

        Assert.Equal(5, mine.Expressions.Count);
    }

    [Fact]
    public void ACharacterTheGameGaveNoFacesKeepsWhatThePackSaid()
    {
        // Somebody with no speaking part and no expression art: whatever the
        // pack said about their faces is the only thing saying it, restatement
        // or not - there is nothing here for it to restate.
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        pack.ForgeVersion = SMSModForge.Shared.ForgeVersion.Current;
        var mute = pack.Characters.FirstOrDefault(
            c => c.IsVanillaCharacter && VanillaFaces.Of(c).Count == 0);
        Assert.NotNull(mute);
        _out.WriteLine($"{mute!.DisplayName} has no faces of the game's");

        mute.Expressions.Add(new ActorExpressionDef { Key = "Happy", ExpressionGoName = "Happy" });
        PackMigration.Apply(pack);

        Assert.Single(mute.Expressions);

        // ...but neutral is still nothing whoever you are.
        mute.Expressions.Add(new ActorExpressionDef { Key = "neutral", ExpressionGoName = "" });
        PackMigration.Apply(pack);
        Assert.Single(mute.Expressions);
    }

    [Fact]
    public void OpeningAPackDoesNotWriteToIt()
    {
        // The part of the migration contract that is easiest to break and
        // hardest to notice: an author who opens a pack to look at it and
        // closes it has changed nothing on disk.
        var (pack, kate) = Seeded();
        AddTheOldBoilerplate(kate);

        string dir = Path.Combine(Path.GetTempPath(), "smsmodforge-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "modpack.json");
            File.WriteAllText(file, PackRepository.SerializeAsSaved(pack));
            var was = File.GetLastWriteTimeUtc(file);
            string before = File.ReadAllText(file);

            var loaded = PackRepository.Load(dir);
            Assert.NotNull(loaded);

            Assert.Equal(before, File.ReadAllText(file));
            Assert.Equal(was, File.GetLastWriteTimeUtc(file));

            // ...and in memory it IS migrated, or the test above proves nothing.
            var kateAgain = loaded!.Characters.Single(c => c.VanillaCharacter == "Kate");
            Assert.Empty(kateAgain.Expressions);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TheAuthorsOwnPackIfOneIsPointedAtUs()
    {
        // The subject that prompted this: a pack set up years of edits ago,
        // carrying the boilerplate on fifteen of the game's characters. Gated
        // on an environment variable, because a committed test must not
        // hard-code a path to anybody's work.
        string? path = Environment.GetEnvironmentVariable("SMSMODFORGE_AUTHOR_PACK");
        // The variable names either the manifest or the folder holding it;
        // both spellings are natural and neither is worth being strict about.
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            path = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            _out.WriteLine("SMSMODFORGE_AUTHOR_PACK not set; nothing to check.");
            return;
        }

        var pack = PackRepository.Load(path);
        Assert.NotNull(pack);

        var borrowed = pack!.Characters.Where(c => c.IsVanillaCharacter).ToList();
        int left = borrowed.Sum(c => c.Expressions.Count);
        _out.WriteLine($"{borrowed.Count} borrowed characters, {left} expression row(s) left after loading");

        // Whatever survived must be something an author actually said.
        foreach (var c in borrowed)
            foreach (var e in c.Expressions)
            {
                _out.WriteLine($"   kept: {c.DisplayName} {e.Key} -> {e.ExpressionGoName}");
                Assert.False(VanillaFaces.OnlyRestatesTheGame(e, VanillaFaces.Of(c)));
            }
    }
}
