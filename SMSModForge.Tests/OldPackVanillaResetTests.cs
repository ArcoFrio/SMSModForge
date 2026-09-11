using System;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Packs written before 1.3.0 start their borrowed characters again.
/// <para/>
/// Before that version a pack could hold a name, a wardrobe, a colour and a set
/// of expressions for one of the game's characters and almost none of it went
/// anywhere — the runtime skipped a borrowed character's whole wardrobe, and
/// the editor has since taken the name, the bust source and the default outfit
/// away as fields a pack may set. What is left on those packs is sediment an
/// older editor wrote unbidden, and it is what makes half the cast read as
/// changed.
/// </summary>
public sealed class OldPackVanillaResetTests
{
    private readonly ITestOutputHelper _out;
    public OldPackVanillaResetTests(ITestOutputHelper o) => _out = o;

    /// <summary>One of the game's characters, as an old pack held them: a
    /// hand-keyed wardrobe of one, a colour, a voice, the boilerplate faces.</summary>
    private static (ModPack Pack, CharacterDef Them) OldPack(string forgeVersion = "1.2.0",
                                                             string who = "Adrian")
    {
        var one = VanillaCharacters.Find(who)!;
        var pack = PackRepository.CreateEmpty("old.pack");
        pack.ForgeVersion = forgeVersion;

        // A new pack already has the game's whole cast seeded into it, so the
        // character to make look old is the one ALREADY THERE. Adding a second
        // would be a pack with two Adrians, which no real one has - and the
        // duplicate key would make the reset's collision guard refuse to rename
        // for a reason that has nothing to do with what is being tested.
        var them = pack.Characters.Single(c => c.VanillaCharacter == who);

        them.Name = who;                                  // the display name, not the key
        them.DisplayName = who + " (the neighbour)";
        them.DefaultOutfit = one.Outfits[^1];
        them.NameColor = "#123456";
        them.Typewriter = new TypewriterDef { Frequency = 45, PitchMin = 1.0f, PitchMax = 1.5f };

        // One outfit, keyed after the bust rather than derived from it.
        them.Outfits.Clear();
        them.Outfits.Add(new OutfitDef { Key = one.Outfits[0], GameObjectName = one.Outfits[0] });
        them.Expressions.Add(new ActorExpressionDef { Key = "neutral", ExpressionGoName = "" });
        them.Expressions.Add(new ActorExpressionDef { Key = "Happy", ExpressionGoName = "Happy" });
        them.GiftLikes.Add("flowers");

        return (pack, them);
    }

    [Fact]
    public void EverythingAnOldPackSaidAboutThemGoes()
    {
        var (pack, adrian) = OldPack();

        var report = PackMigration.Apply(pack);
        _out.WriteLine(report.Describe());

        var one = VanillaCharacters.Find("Adrian")!;
        Assert.Equal(one.Name, adrian.DisplayName);
        Assert.Equal(one.Key, adrian.Name);
        Assert.Equal(one.Outfits[0], adrian.DefaultOutfit);
        Assert.Null(adrian.NameColor);
        Assert.Null(adrian.Typewriter);
        Assert.Empty(adrian.Expressions);
        Assert.Empty(adrian.GiftLikes);
        Assert.Equal(one.Outfits, adrian.Outfits.Select(o => o.GameObjectName).ToList());

        Assert.True(VanillaCastSeed.IsUntouched(adrian));
        Assert.DoesNotContain("\"vanillaCharacter\": \"Adrian\"",
                              PackRepository.SerializeAsSaved(pack));
    }

    [Fact]
    public void ABustThePackDrewSurvivesAndFinallyGetsBuilt()
    {
        // The one thing here that is unambiguously somebody's work. The flag
        // that says "this is the pack's art" did not exist when it was written,
        // so the reset is also where it gets one - and 1.3.0 is the version
        // that finally builds it.
        var (pack, adrian) = OldPack();
        adrian.Outfits.Add(new OutfitDef
        {
            Key = "spacesuit", GameObjectName = "Adrian_Spacesuit",
            BaseSprite = "art/adrian-spacesuit.png",
        });

        PackMigration.Apply(pack);

        var drawn = adrian.Outfits.Single(o => o.GameObjectName == "Adrian_Spacesuit");
        Assert.True(drawn.PackArt);
        Assert.Equal("art/adrian-spacesuit.png", drawn.BaseSprite);

        // ...and it is the only thing keeping him in the manifest.
        Assert.False(VanillaCastSeed.IsUntouched(adrian));
        Assert.Contains("Adrian_Spacesuit", PackRepository.SerializeAsSaved(pack));
    }

    [Fact]
    public void TheDialogueKeyIsPutBackAsARenameSoTheLinesCome()
    {
        // Mobster, keyed "mobster" where the catalog says "mobster1", with
        // seven lines naming them by it.
        var (pack, mobster) = OldPack(who: "Mobster 1");
        mobster.Key = "mobster";

        var d = new DialogueDef { Key = "chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "hi", Actor = "mobster" });
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);

        var one = VanillaCharacters.Find("Mobster 1")!;

        PackMigration.Apply(pack);

        _out.WriteLine($"key '{mobster.Key}', line names '{d.Nodes[0].Actor}'");
        Assert.Equal(one.Key, mobster.Key);
        Assert.Equal(one.Key, d.Nodes[0].Actor);
    }

    [Fact]
    public void APackAlreadyOnThisVersionIsLeftAlone()
    {
        // The control, and the reason the version gate exists at all: from
        // 1.3.0 these fields mean something, and an author who sets a colour
        // must not find it wiped on the next load.
        var (pack, adrian) = OldPack(forgeVersion: "1.3.0");

        PackMigration.Apply(pack);

        Assert.Equal("#123456", adrian.NameColor);
        Assert.NotNull(adrian.Typewriter);
        Assert.Contains("flowers", adrian.GiftLikes);
    }

    [Fact]
    public void APackWithNoStampAtAllCountsAsOlder()
    {
        // The stamp is newer than the oldest packs, so "no stamp" means older
        // than anything that has one. ForgeVersion.Compare answers zero for a
        // stamp it cannot read - deliberately - which read as "not older" would
        // skip exactly the packs this is for.
        var (pack, adrian) = OldPack(forgeVersion: "");

        PackMigration.Apply(pack);

        Assert.Null(adrian.NameColor);
        Assert.True(VanillaCastSeed.IsUntouched(adrian));
    }

    [Fact]
    public void APacksOwnCharacterIsNotTouched()
    {
        var pack = PackRepository.CreateEmpty("old.pack");
        pack.ForgeVersion = "1.2.0";
        var mine = new CharacterDef
        {
            Key = "sarah", Name = "Sarah", DisplayName = "Sarah the Barista",
            NameColor = "#ABCDEF",
            Typewriter = new TypewriterDef { Frequency = 30 },
        };
        mine.Outfits.Add(new OutfitDef { Key = "swim", GameObjectName = "Sarah_Swim" });
        mine.Expressions.Add(new ActorExpressionDef { Key = "Happy", ExpressionGoName = "Happy" });
        pack.Characters.Add(mine);

        PackMigration.Apply(pack);

        Assert.Equal("Sarah the Barista", mine.DisplayName);
        Assert.Equal("#ABCDEF", mine.NameColor);
        Assert.NotNull(mine.Typewriter);
        Assert.Single(mine.Expressions);
        Assert.Single(mine.Outfits);
    }

    [Fact]
    public void RunningItTwiceChangesNothingTheSecondTime()
    {
        // It runs on every load, including the load right after a save - and a
        // pack saved by THIS build stamps 1.3.0, so the gate closes behind it.
        // But the pass has to be idempotent on its own terms too, or a pack
        // opened twice would report a migration twice and take two backups.
        var (pack, _) = OldPack();

        PackMigration.Apply(pack);
        var again = PackMigration.Apply(pack);

        _out.WriteLine(again.Describe().Length == 0 ? "(nothing)" : again.Describe());
        Assert.False(again.Migrated);
    }

    [Fact]
    public void OpeningAnOldPackDoesNotWriteToIt()
    {
        var (pack, _) = OldPack();

        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                            "smsmodforge-old-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            string file = System.IO.Path.Combine(dir, "modpack.json");
            System.IO.File.WriteAllText(file, PackRepository.SerializeAsSaved(pack));
            string before = System.IO.File.ReadAllText(file);

            var loaded = PackRepository.Load(dir);
            Assert.NotNull(loaded);

            Assert.Equal(before, System.IO.File.ReadAllText(file));

            // ...and in memory it IS reset, or the test above proves nothing.
            var adrian = loaded!.Characters.FirstOrDefault(c => c.VanillaCharacter == "Adrian");
            if (adrian != null) Assert.Null(adrian.NameColor);
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }
}
