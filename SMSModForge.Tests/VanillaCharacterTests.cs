using System;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's own cast, available rather than re-described.
/// <para/>
/// A pack used to declare a character and copy out a bust name or two of the
/// game's. The cast is simply there now, and a pack that predates it is
/// recognised on load — pointed at the character it always meant, and given the
/// whole wardrobe instead of the corner of it somebody copied.
/// <para/>
/// The pack's KEY is never touched. Every dialogue line and action already
/// pointing at it keeps working, which is the whole reason the conversion is
/// safe to do automatically.
/// </summary>
public sealed class VanillaCharacterTests
{
    private readonly ITestOutputHelper _out;
    public VanillaCharacterTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void TheGamesCastIsGroupedFromItsBusts()
    {
        Assert.Equal(285, VanillaBusts.All.Count);
        Assert.Equal(118, VanillaCharacters.All.Count);

        var anna = VanillaCharacters.Find("Anna");
        Assert.NotNull(anna);
        Assert.Equal("anna", anna!.Key);
        Assert.Equal(65, anna.Outfits.Count);
        Assert.Equal("65 outfits", anna.Summary);

        // Every bust belongs to exactly one character, so nothing is lost or
        // counted twice in the regrouping.
        Assert.Equal(VanillaBusts.All.Count, VanillaCharacters.All.Sum(c => c.Outfits.Count));

        // A display name is not an identifier.
        Assert.Equal("doctorfrost", VanillaCharacters.KeyFor("Doctor Frost"));
        Assert.Equal("himarifather", VanillaCharacters.KeyFor("Himari (Father)"));
    }

    /// <summary>
    /// The cast is named the way the game names them, not the way the artwork
    /// is filed.
    /// <para/>
    /// Every one of these is the actor's own name from the runtime extraction:
    /// the bust files for Chloe are misspelt "Choe", and "El Bandito" and
    /// "Succubus" describe a picture rather than name a person.
    /// </summary>
    [Fact]
    public void TheCastIsNamedTheWayTheGameNamesThem()
    {
        Assert.NotNull(VanillaCharacters.Find("Chloe"));
        Assert.Null(VanillaCharacters.All.FirstOrDefault(c => c.Name == "Choe"));

        // Her busts came with her under the corrected name.
        Assert.Equal("Chloe", VanillaCharacters.Owning("Choe_Base")!.Name);

        Assert.Equal("Frank", VanillaCharacters.SpokenName("El Bandito"));
        Assert.Equal("Zazia", VanillaCharacters.SpokenName("Succubus"));
        Assert.Equal("Nurse Nina", VanillaCharacters.SpokenName("NurseNina"));

        // An outfit filed as a person is folded back onto her, so a record
        // reads the way a pack's own character record does: one person,
        // wearing their things.
        Assert.Equal("Anna", VanillaCharacters.Owning("S_AnnaMummyOutfit")!.Name);
        Assert.Null(VanillaCharacters.All.FirstOrDefault(c => c.Name.Contains("Mummy")));

        // Himari's parents are not versions of Himari.
        Assert.Equal("Mr. Kimura", VanillaCharacters.Owning("HimariFatherBust")!.Name);
        Assert.Equal("Mrs. Kimura", VanillaCharacters.Owning("HimariMotherBust")!.Name);
        Assert.Equal(3, VanillaCharacters.Find("Himari")!.Outfits.Count);

        // Where somebody appears is not part of their name.
        Assert.NotNull(VanillaCharacters.Find("Mei"));
        Assert.Equal(3, VanillaCharacters.Find("Mei")!.Outfits.Count);
        Assert.Empty(VanillaCharacters.All.Where(c => c.Name.Contains("(Vacation)")));

        // But a parenthetical telling two people apart is kept: these are two
        // women with no other name, and folding them together loses one.
        Assert.NotNull(VanillaCharacters.Find("Mall Waifu (Blonde)"));
        Assert.NotNull(VanillaCharacters.Find("Mall Waifu (Blue Hair)"));

        // The same reasoning refuses a rename the GAME offers, when the name
        // it offers is already somebody else's: both pool chicks are called
        // "Stranger" in their scenes, and taking that would leave one woman
        // where there are two.
        Assert.NotNull(VanillaCharacters.Find("Pool Chick (Blonde)"));
        Assert.NotNull(VanillaCharacters.Find("Pool Chick (Reddie)"));
        Assert.NotEqual(VanillaCharacters.Find("Pool Chick (Blonde)")!.Name,
                        VanillaCharacters.Find("Pool Chick (Reddie)")!.Name);

        // Named by the conversations rather than the bust files, and only
        // where more than one scene forced the same answer.
        Assert.Equal("Master Zhen", VanillaCharacters.Owning("Master_Default")!.Name);
        Assert.Equal("Miss Zero", VanillaCharacters.SpokenName("Shady Vendor"));
        Assert.Equal("Cerise", VanillaCharacters.SpokenName("Android"));

        // A pack migrated before a correction stored the old name; it still
        // finds the person it meant.
        Assert.Equal("Master Zhen", VanillaCharacters.Find("Master")!.Name);

        // The inferences a single scene offered are NOT taken: an actor with
        // no name of its own cannot name anybody.
        Assert.Equal("Judge Benjamin", VanillaCharacters.SpokenName("Judge Benjamin"));

        // And a tag is only dropped when the name underneath is somebody the
        // game has. Dropping the one on "Evelyn (Alien)" would have given
        // Doctor Evelyn Frost's first name to her creation, so it is renamed
        // to what the dialogue calls it instead - and the two stay apart.
        Assert.Equal("Subject IX-Delta", VanillaCharacters.Owning("S_Evelyn_AlienBust")!.Name);
        Assert.NotNull(VanillaCharacters.Find("Doctor Frost"));
        Assert.NotEqual(VanillaCharacters.Find("Evelyn (Alien)")!.Name,
                        VanillaCharacters.Find("Doctor Frost")!.Name);

        // A pack that stored the old description still finds it, which is what
        // keeps a rename from orphaning somebody's character.
        Assert.Equal("Subject IX-Delta", VanillaCharacters.Find("Evelyn (Alien)")!.Name);

        // Named from a single scene, confirmed by the pack's author: a
        // description standing in for somebody who does have a name.
        Assert.Equal("Tristan", VanillaCharacters.Owning("Richguy")!.Name);
        Assert.Equal("Megan", VanillaCharacters.Owning("S_Minitalk_NPC_KateFriend")!.Name);

        // A name the game agrees with is left exactly alone.
        Assert.Equal("Anna", VanillaCharacters.SpokenName("Anna"));
        Assert.Equal("Kate", VanillaCharacters.SpokenName("Kate"));

        // And correcting names loses nobody: still one character per group.
        Assert.Equal(118, VanillaCharacters.All.Count);
        Assert.Equal(VanillaBusts.All.Count, VanillaCharacters.All.Sum(c => c.Outfits.Count));
    }

    /// <summary>The control: a name the game does not have is not invented.</summary>
    [Fact]
    public void SomebodyTheGameDoesNotHaveIsNotFound()
    {
        Assert.Null(VanillaCharacters.Find("Somebody Invented"));
        Assert.Null(VanillaCharacters.Find(""));
        Assert.Null(VanillaCharacters.Find(null));
        Assert.Null(VanillaCharacters.Owning("NotABustName"));

        // And one it does have is found by name and by key alike.
        Assert.NotNull(VanillaCharacters.Find("Kate"));
        Assert.NotNull(VanillaCharacters.Find("kate"));
        Assert.Equal("Anna", VanillaCharacters.Owning("Anna_Bust")!.Name);
    }

    /// <summary>
    /// A pack written before the cast existed is recognised from the busts it
    /// was wearing.
    /// </summary>
    [Fact]
    public void AnOlderPackIsPointedAtTheCharacterItAlwaysMeant()
    {
        var pack = new ModPack();
        var old = new CharacterDef
        {
            Key = "masterzhen",                 // the author's key, not the game's
            DisplayName = "Master Zhen",
            BustSource = BustSource.Vanilla,
        };
        old.Outfits.Add(new OutfitDef { Key = "default", GameObjectName = "Master_Default" });
        pack.Characters.Add(old);

        int adopted = CharacterMerge.AdoptVanillaCharacters(pack);

        Assert.Equal(1, adopted);

        // Pointed at the person, under the name the game says out loud - the
        // bust files call him "Master" and the conversations call him Master
        // Zhen, which is also what this pack's author called him.
        Assert.Equal("Master Zhen", old.VanillaCharacter);
        Assert.True(old.IsVanillaCharacter);

        // The key and the author's own outfit entry are untouched — everything
        // pointing at them keeps working.
        Assert.Equal("masterzhen", old.Key);
        Assert.Equal("Master Zhen", old.DisplayName);
        Assert.Contains(old.Outfits, o => o.Key == "default"
                                          && o.GameObjectName == "Master_Default");

        // And it runs once: adopting again changes nothing.
        Assert.Equal(0, CharacterMerge.AdoptVanillaCharacters(pack));
    }

    /// <summary>Recognised by name when the busts say nothing.</summary>
    [Fact]
    public void ACharacterWithNoRecognisableBustIsFoundByName()
    {
        var pack = new ModPack();
        var bare = new CharacterDef
        {
            Key = "kate", DisplayName = "Kate", BustSource = BustSource.Vanilla,
        };
        pack.Characters.Add(bare);

        Assert.Equal(1, CharacterMerge.AdoptVanillaCharacters(pack));
        Assert.Equal("Kate", bare.VanillaCharacter);
        Assert.Equal(7, bare.Outfits.Count);          // the whole wardrobe
    }

    /// <summary>
    /// The control for the migration: a character the pack drew itself, and a
    /// voice with no bust, are both left alone.
    /// </summary>
    [Fact]
    public void APacksOwnCharactersAreNotAdopted()
    {
        var pack = new ModPack();

        var own = new CharacterDef { Key = "android", DisplayName = "Android One" };
        own.Outfits.Add(new OutfitDef { Key = "default", GameObjectName = "MyOwnBust" });
        var voice = new CharacterDef
        {
            Key = "narrator", DisplayName = "Narrator", BustSource = BustSource.None,
        };
        pack.Characters.Add(own);
        pack.Characters.Add(voice);

        Assert.Equal(0, CharacterMerge.AdoptVanillaCharacters(pack));
        Assert.False(own.IsVanillaCharacter);
        Assert.False(voice.IsVanillaCharacter);
        Assert.Single(own.Outfits);
        Assert.Empty(voice.Outfits);
    }

    /// <summary>
    /// The whole cast is there to use, and none of it reaches the manifest
    /// unused.
    /// </summary>
    [Fact]
    public void TheCastIsPresentToAuthorWithAndAbsentFromTheFile()
    {
        var pack = new ModPack();
        int seeded = VanillaCastSeed.Seed(pack);

        Assert.Equal(118, seeded);
        Assert.Equal(118, pack.Characters.Count(c => c.IsVanillaCharacter));
        Assert.Equal(65, pack.Characters.Single(c => c.VanillaCharacter == "Anna").Outfits.Count);

        // Nothing names any of them, so nothing is written.
        string empty = PackRepository.SerializeAsSaved(pack);
        Assert.DoesNotContain("Anna_Bust", empty);

        // The live model still has all of them to choose from.
        Assert.Equal(118, pack.Characters.Count(c => c.IsVanillaCharacter));
    }

    /// <summary>
    /// Speaking through one of the game's characters does NOT write it down.
    /// <para/>
    /// It used to: a single line given to Anna pulled her whole record - her
    /// name and all sixty-five of her outfits - into the manifest, purely so
    /// the runtime could look her up when the line played. The cast is
    /// compiled into the runtime now, so it already knows who Anna is, and a
    /// pack that merely speaks through her has said nothing worth recording.
    /// </summary>
    [Fact]
    public void OneTheAuthorOnlySpeaksThroughIsNotWritten()
    {
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);

        var dialogue = new DialogueDef { Key = "d" };
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "anna", Text = "Hello." });
        pack.Dialogues.Add(dialogue);

        string written = PackRepository.SerializeAsSaved(pack);
        Assert.DoesNotContain("\"vanillaCharacter\": \"Anna\"", written);
        Assert.DoesNotContain("Anna_Bust", written);

        // The line itself is still hers, which is the whole point: the pack
        // names the speaker and the game supplies the person.
        Assert.Contains("\"actor\": \"anna\"", written);

        // And the author still sees her in the editor.
        Assert.Equal(65, pack.Characters.Single(c => c.VanillaCharacter == "Anna").Outfits.Count);
    }

    /// <summary>
    /// The pruning is only safe because loading puts it all back.
    /// <para/>
    /// A manifest that drops a character the pack still speaks through is
    /// making a bet: that whoever reads it can work out who that was. This is
    /// the bet being checked - save, reload, and the speaker is whole again,
    /// with the wardrobe the editor offers and the name the line shows.
    /// </summary>
    [Fact]
    public void WhatIsPrunedComesBackOnLoad()
    {
        // Through CreateEmpty, so this is the pack a new author actually gets:
        // the player and the whole cast, present from the first screen.
        var pack = PackRepository.CreateEmpty("roundtrip.pack");
        Assert.Equal(118, pack.Characters.Count(c => c.IsVanillaCharacter));

        var dialogue = new DialogueDef { Key = "d" };
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "anna", Text = "Hello." });
        pack.Dialogues.Add(dialogue);

        // One the author DID change, to prove the two paths are different.
        var kate = pack.Characters.Single(c => c.VanillaCharacter == "Kate");
        kate.DisplayName = "Kate (the barista)";

        string written = PackRepository.SerializeAsSaved(pack);
        Assert.DoesNotContain("\"vanillaCharacter\": \"Anna\"", written);
        Assert.Contains("\"vanillaCharacter\": \"Kate\"", written);

        // Kate is kept because she changed - but only the change is written.
        // Her seven outfits are the game's, so the file names none of them.
        Assert.DoesNotContain("KateUndies", written);

        var reloaded = Newtonsoft.Json.JsonConvert.DeserializeObject<ModPack>(written)!;
        Assert.Single(reloaded.Characters.Where(c => c.IsVanillaCharacter));
        Assert.Empty(reloaded.Characters.Single(c => c.IsVanillaCharacter).Outfits);

        CharacterMerge.Apply(reloaded);

        // Everybody is back...
        Assert.Equal(118, reloaded.Characters.Count(c => c.IsVanillaCharacter));
        var anna = reloaded.Characters.Single(c => c.VanillaCharacter == "Anna");
        Assert.Equal(65, anna.Outfits.Count);
        Assert.Equal("Anna", anna.DisplayName);
        Assert.Equal("anna", anna.Key);          // still the key the line names

        // ...and the author's change survived rather than being reseeded over,
        // with the wardrobe put back in the order it had.
        // Saving again writes the same thing, so a pack does not grow by being
        // opened and closed.
        Assert.Equal(written, PackRepository.SerializeAsSaved(reloaded));

        var back = reloaded.Characters.Single(c => c.VanillaCharacter == "Kate");
        Assert.Equal("Kate (the barista)", back.DisplayName);
        Assert.Equal(kate.Outfits.Select(o => o.GameObjectName),
                     back.Outfits.Select(o => o.GameObjectName));

        // An outfit of the author's OWN on a vanilla character is written,
        // because nothing else could put it back.
        kate.Outfits.Add(new OutfitDef { Key = "apron", GameObjectName = "Kate_Apron" });
        Assert.Contains("Kate_Apron", PackRepository.SerializeAsSaved(pack));


    }

    /// <summary>
    /// The fields that decide "untouched", which is now the only thing
    /// standing between an author's change and it being dropped at save.
    /// <para/>
    /// The old check listed six of the eleven a character carries, so a name
    /// colour or a different default outfit counted as no change at all. That
    /// was harmless while used-and-unchanged entries were kept anyway; with
    /// this pruning it would throw the change away.
    /// </summary>
    [Fact]
    public void EveryFieldAnAuthorCanChangeCountsAsAChange()
    {
        foreach (var edit in new (string What, Action<CharacterDef> Do)[]
        {
            ("display name",  c => c.DisplayName = "Katie"),
            ("name colour",   c => c.NameColor = "#FF00FF"),
            ("default outfit",c => c.DefaultOutfit = "Kate_Nude"),
            ("bust source",   c => c.BustSource = BustSource.Pack),
            ("an outfit",     c => c.Outfits[0].GameObjectName = "Something_Else"),
            ("a new outfit",  c => c.Outfits.Add(new OutfitDef { Key = "x", GameObjectName = "X" })),
            ("gift likes",    c => c.GiftLikes.Add("flowers")),
            ("typewriter",    c => c.Typewriter = new TypewriterDef()),
        })
        {
            var pack = new ModPack();
            VanillaCastSeed.Seed(pack);
            var kate = pack.Characters.Single(c => c.VanillaCharacter == "Kate");

            Assert.True(VanillaCastSeed.IsUntouched(kate), "before: " + edit.What);
            edit.Do(kate);
            Assert.False(VanillaCastSeed.IsUntouched(kate), "after: " + edit.What);

            // And it survives the write, which is what the check is FOR.
            Assert.Contains("\"vanillaCharacter\": \"Kate\"",
                            PackRepository.SerializeAsSaved(pack));
        }
    }

    /// <summary>An entry the author changed is kept even if nothing names
    /// it — the change is the point.</summary>
    [Fact]
    public void OneTheAuthorChangedIsKept()
    {
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);

        var kate = pack.Characters.Single(c => c.VanillaCharacter == "Kate");
        Assert.True(VanillaCastSeed.IsUntouched(kate));

        kate.DisplayName = "Kate (the barista)";
        Assert.False(VanillaCastSeed.IsUntouched(kate));

        Assert.Contains("Kate (the barista)", PackRepository.SerializeAsSaved(pack));
    }

    /// <summary>Seeding twice does not double the cast, and never displaces a
    /// pack's own entry for the same character.</summary>
    [Fact]
    public void SeedingIsIdempotentAndNeverDisplacesThePacksOwn()
    {
        var pack = new ModPack();
        var mine = new CharacterDef
        {
            Key = "masterzhen", DisplayName = "Master Zhen",
            BustSource = BustSource.Vanilla, VanillaCharacter = "Master",
        };
        pack.Characters.Add(mine);

        VanillaCastSeed.Seed(pack);
        int after = pack.Characters.Count;
        Assert.Equal(0, VanillaCastSeed.Seed(pack));
        Assert.Equal(after, pack.Characters.Count);

        // The author's own entry for Master survived, keyed as they keyed it.
        Assert.Single(pack.Characters, c => c.VanillaCharacter == "Master");
        Assert.Equal("masterzhen", pack.Characters.Single(c => c.VanillaCharacter == "Master").Key);
    }

    /// <summary>
    /// A vanilla character the game does not recognise at all is left as it is.
    /// <para/>
    /// Better an entry that still works exactly as it did than one adopted by
    /// the wrong character: the outfits it names are the ones the pack asked
    /// for either way, and a wrong adoption would put somebody else's wardrobe
    /// on it.
    /// </summary>
    [Fact]
    public void AnUnrecognisableVanillaCharacterIsLeftAlone()
    {
        var pack = new ModPack();
        var odd = new CharacterDef
        {
            Key = "whoever", DisplayName = "Whoever", BustSource = BustSource.Vanilla,
        };
        odd.Outfits.Add(new OutfitDef { Key = "x", GameObjectName = "NoSuchBust" });
        pack.Characters.Add(odd);

        Assert.Equal(0, CharacterMerge.AdoptVanillaCharacters(pack));
        Assert.False(odd.IsVanillaCharacter);
        Assert.Single(odd.Outfits);
    }
}
