using System;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Putting back the identifiers an older editor derived differently.
/// <para/>
/// A character adopted from the game came out carrying <c>"name": "Adrian"</c>
/// where seeding writes <c>"adrian"</c>, and an outfit keyed
/// <c>"Adrian_bust"</c> where seeding writes <c>"adrianbust"</c>. Nobody typed
/// either. They sat there harmlessly until the editor started marking changed
/// characters, at which point Adrian carried the tag on the strength of two
/// strings an author cannot see and has no way to reset.
/// </summary>
public sealed class BorrowedIdentifierMigrationTests
{
    private readonly ITestOutputHelper _out;
    public BorrowedIdentifierMigrationTests(ITestOutputHelper o) => _out = o;

    /// <summary>One of the game's characters as an older editor wrote them.</summary>
    private static (ModPack Pack, CharacterDef Them) TheOldWay(string name)
    {
        var one = VanillaCharacters.Find(name)!;
        var pack = PackRepository.CreateEmpty("old.pack");
        // Stamped as current on purpose. Anything OLDER than 1.3.0 is taken
        // wholesale by ResetBorrowedCharactersWrittenBefore, which leaves these
        // narrower passes nothing to find; from 1.3.0 on they are what catches
        // a pack that drifts - one hand-edited, or one the editor let into a
        // state it no longer offers.
        pack.ForgeVersion = SMSModForge.Shared.ForgeVersion.Current;

        var them = new CharacterDef
        {
            Key = one.Key,
            Name = name.Replace(" ", ""),          // the display name, not the key
            DisplayName = name,
            BustSource = BustSource.Vanilla,
            VanillaCharacter = name,
            DefaultOutfit = one.Outfits[0],
        };
        // One outfit, keyed after the bust rather than derived from it.
        them.Outfits.Add(new OutfitDef { Key = one.Outfits[0], GameObjectName = one.Outfits[0] });
        pack.Characters.Add(them);
        return (pack, them);
    }

    [Fact]
    public void ACharacterNobodyEditedStopsReadingAsChanged()
    {
        var (pack, adrian) = TheOldWay("Adrian");
        VanillaCastSeed.Seed(pack);          // the rest of his wardrobe, as loading does
        Assert.False(VanillaCastSeed.IsUntouched(adrian));

        var report = PackMigration.Apply(pack);
        _out.WriteLine(report.Describe());

        Assert.Equal("adrian", adrian.Name);
        Assert.Equal(VanillaCharacters.KeyFor(adrian.Outfits[0].GameObjectName), adrian.Outfits[0].Key);
        Assert.True(VanillaCastSeed.IsUntouched(adrian),
                    "he still reads as changed, so the tag still points at nothing");

        // ...and being untouched means he leaves the manifest entirely.
        Assert.DoesNotContain("\"vanillaCharacter\": \"Adrian\"",
                              PackRepository.SerializeAsSaved(pack));
    }

    [Fact]
    public void RunningItTwiceChangesNothingTheSecondTime()
    {
        var (pack, _) = TheOldWay("Adrian");
        VanillaCastSeed.Seed(pack);

        PackMigration.Apply(pack);
        var again = PackMigration.Apply(pack);
        Assert.DoesNotContain("Machine-written", again.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWardrobeGoesBackIntoTheGamesOrder()
    {
        // A pack that adopted somebody through one bust got that bust first and
        // the rest appended behind it. Invisible, unactionable, and enough on
        // its own to keep a character marked as changed.
        var one = VanillaCharacters.Find("Amelia")!;
        Assert.True(one.Outfits.Count > 2);

        var pack = PackRepository.CreateEmpty("order.pack");
        pack.ForgeVersion = SMSModForge.Shared.ForgeVersion.Current;
        var amelia = new CharacterDef
        {
            Key = one.Key, Name = one.Key, DisplayName = "Amelia",
            BustSource = BustSource.Vanilla, VanillaCharacter = "Amelia",
            DefaultOutfit = one.Outfits[0],
        };
        string last = one.Outfits[^1];
        amelia.Outfits.Add(new OutfitDef { Key = VanillaCharacters.KeyFor(last), GameObjectName = last });
        pack.Characters.Add(amelia);
        VanillaCastSeed.Seed(pack);

        Assert.Equal(last, amelia.Outfits[0].GameObjectName);   // the adopted one, first

        PackMigration.Apply(pack);

        _out.WriteLine(string.Join(", ", amelia.Outfits.Take(3).Select(o => o.GameObjectName)));
        Assert.Equal(one.Outfits, amelia.Outfits.Select(o => o.GameObjectName).ToList());
        Assert.True(VanillaCastSeed.IsUntouched(amelia));
    }

    [Fact]
    public void AWardrobeWithNoNamedDefaultIsLeftInTheOrderItHas()
    {
        // The control on the reorder. Where the default outfit is blank, FIRST
        // is the default - here and in the runtime - so shuffling the list
        // would quietly change which bust the character walks in wearing.
        var one = VanillaCharacters.Find("Amelia")!;
        var pack = PackRepository.CreateEmpty("order.pack");
        pack.ForgeVersion = SMSModForge.Shared.ForgeVersion.Current;
        string last = one.Outfits[^1];

        var amelia = new CharacterDef
        {
            Key = one.Key, Name = one.Key, DisplayName = "Amelia",
            BustSource = BustSource.Vanilla, VanillaCharacter = "Amelia",
            DefaultOutfit = "",
        };
        amelia.Outfits.Add(new OutfitDef { Key = VanillaCharacters.KeyFor(last), GameObjectName = last });
        pack.Characters.Add(amelia);
        VanillaCastSeed.Seed(pack);

        PackMigration.Apply(pack);
        Assert.Equal(last, amelia.Outfits[0].GameObjectName);
    }

    [Fact]
    public void ABustThePackDrewKeepsItsOwnKey()
    {
        // The control on the rename. A key on one of the game's busts is inert
        // - the runtime reads it only as a fallback for a missing
        // gameObjectName - but on a bust the pack draws it is the author's.
        var (pack, adrian) = TheOldWay("Adrian");
        adrian.Outfits.Add(new OutfitDef
        {
            Key = "Adrian_Spacesuit", GameObjectName = "Adrian_Spacesuit",
            PackArt = true, BaseSprite = "art/x.png",
        });

        PackMigration.Apply(pack);

        var mine = adrian.Outfits.Single(o => o.PackArt);
        Assert.Equal("Adrian_Spacesuit", mine.Key);
    }

    [Fact]
    public void AnOutfitWithNoGameObjectNameKeepsWhatItPointedAt()
    {
        // With no gameObjectName the runtime falls back to the key to find the
        // bust, which would make the key load-bearing and renaming it a way to
        // lose the bust.
        //
        // It never gets that far: an earlier pass in the same migration copies
        // the key into the empty gameObjectName, so by the time this one runs
        // the name is carrying the bust and the key is inert again. Asserted
        // rather than assumed, because it is only true while the two run in
        // that order - the guard in NormaliseBorrowedIdentifiers stays for the
        // day somebody reorders them.
        var (pack, adrian) = TheOldWay("Adrian");
        string bust = adrian.Outfits[0].Key;
        adrian.Outfits[0].GameObjectName = "";

        PackMigration.Apply(pack);

        _out.WriteLine($"key={adrian.Outfits[0].Key}, go={adrian.Outfits[0].GameObjectName}");
        Assert.Equal(bust, adrian.Outfits[0].GameObjectName);
        Assert.Equal(VanillaCharacters.KeyFor(bust), adrian.Outfits[0].Key);
    }

    // ── Fields a pack no longer gets to set ──────────────────────────

    [Fact]
    public void TheGreyedFieldsGoBackToTheGames()
    {
        // Who Anna is, where her busts come from and which one she walks in
        // wearing are all greyed out in the editor now. A pack written before
        // that can still be carrying its own answers, and because the fields
        // are greyed there is no way to correct them by hand.
        var (pack, adrian) = TheOldWay("Adrian");
        VanillaCastSeed.Seed(pack);

        var one = VanillaCharacters.Find("Adrian")!;
        adrian.DisplayName = "Adrian (the neighbour)";
        adrian.BustSource = BustSource.Pack;
        adrian.DefaultOutfit = one.Outfits[^1];

        var report = PackMigration.Apply(pack);
        _out.WriteLine(report.Describe());

        Assert.Equal(one.Name, adrian.DisplayName);
        Assert.Equal(BustSource.Vanilla, adrian.BustSource);
        Assert.Equal(one.Outfits[0], adrian.DefaultOutfit);
        Assert.True(VanillaCastSeed.IsUntouched(adrian));

        // It changes what a player sees, so it has to be said out loud - and
        // the contract's backup rides on the same flag.
        Assert.Contains("put back to the game's", report.Describe(), StringComparison.Ordinal);
        Assert.True(report.Migrated);
    }

    [Fact]
    public void APacksOwnCharacterKeepsItsName()
    {
        // The control. None of that applies to a character the pack drew: the
        // name, the source and the default outfit are all theirs.
        var pack = PackRepository.CreateEmpty("mine.pack");
        pack.ForgeVersion = SMSModForge.Shared.ForgeVersion.Current;
        var mine = new CharacterDef
        {
            Key = "sarah", Name = "sarah", DisplayName = "Sarah the Barista",
            BustSource = BustSource.Pack, DefaultOutfit = "Sarah_Swim",
        };
        mine.Outfits.Add(new OutfitDef { Key = "sarah", GameObjectName = "Sarah_Bust" });
        mine.Outfits.Add(new OutfitDef { Key = "swim", GameObjectName = "Sarah_Swim" });
        pack.Characters.Add(mine);

        PackMigration.Apply(pack);

        Assert.Equal("Sarah the Barista", mine.DisplayName);
        Assert.Equal(BustSource.Pack, mine.BustSource);
        Assert.Equal("Sarah_Swim", mine.DefaultOutfit);
    }

    [Fact]
    public void ASettingThatRepeatsWhatTheyAlreadyHadIsDropped()
    {
        // Invisible on screen either way - the editor shows the same numbers -
        // and it costs the character a place in the manifest and a changed tag
        // for agreeing with the game.
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        pack.ForgeVersion = SMSModForge.Shared.ForgeVersion.Current;
        var adrian = pack.Characters.Single(c => c.VanillaCharacter == "Adrian");
        var his = SMSModForge.Shared.VanillaSpeech.For("adrian")!;

        adrian.NameColor = his.NameColor!.ToLowerInvariant();   // however it is spelled
        adrian.Typewriter = new TypewriterDef
        {
            Enabled = his.UseTypewriter, Frequency = his.Frequency,
            PitchMin = his.PitchMin, PitchMax = his.PitchMax,
        };
        Assert.False(VanillaCastSeed.IsUntouched(adrian));

        PackMigration.Apply(pack);

        Assert.Null(adrian.NameColor);
        Assert.Null(adrian.Typewriter);
        Assert.True(VanillaCastSeed.IsUntouched(adrian));
    }

    [Fact]
    public void ASettingThatDiffersIsLeftAlone()
    {
        // The control, and the line this migration will not cross. Amelia's
        // pack says 45 where the game speaks her at 25. Nobody can tell now
        // whether that was meant, it is a real difference, it is visible in the
        // panel, and one click resets it - guessing at intent there would throw
        // away work rather than tidy up after an old editor.
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        pack.ForgeVersion = SMSModForge.Shared.ForgeVersion.Current;
        var amelia = pack.Characters.Single(c => c.VanillaCharacter == "Amelia");
        var hers = SMSModForge.Shared.VanillaSpeech.For("amelia")!;

        amelia.NameColor = "#123456";
        amelia.Typewriter = new TypewriterDef
        {
            Enabled = true, Frequency = 45,
            PitchMin = hers.PitchMin, PitchMax = hers.PitchMax,
        };
        Assert.NotEqual(45, hers.Frequency);

        PackMigration.Apply(pack);

        Assert.Equal("#123456", amelia.NameColor);
        Assert.NotNull(amelia.Typewriter);
        Assert.Equal(45, amelia.Typewriter!.Frequency);
    }

    [Fact]
    public void RunningTheWholeThingTwiceChangesNothingTheSecondTime()
    {
        var (pack, adrian) = TheOldWay("Adrian");
        VanillaCastSeed.Seed(pack);
        adrian.DisplayName = "Somebody Else";
        adrian.NameColor = SMSModForge.Shared.VanillaSpeech.For("adrian")!.NameColor;

        PackMigration.Apply(pack);
        var again = PackMigration.Apply(pack);

        _out.WriteLine(again.Describe().Length == 0 ? "(nothing the second time)" : again.Describe());
        Assert.False(again.Migrated);
    }

    [Fact]
    public void APacksOwnCharacterIsNotRenamed()
    {
        var pack = PackRepository.CreateEmpty("mine.pack");
        var mine = new CharacterDef { Key = "sarah", Name = "Sarah", DisplayName = "Sarah" };
        mine.Outfits.Add(new OutfitDef { Key = "Sarah_Bust", GameObjectName = "Sarah_Bust" });
        pack.Characters.Add(mine);

        PackMigration.Apply(pack);

        Assert.Equal("Sarah", mine.Name);
        Assert.Equal("Sarah_Bust", mine.Outfits[0].Key);
    }
}
