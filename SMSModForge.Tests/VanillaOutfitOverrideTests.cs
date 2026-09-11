using System.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Two things a pack can now say about one of the game's characters: replace a
/// texture on a bust they already have, and give them a bust they do not.
/// <para/>
/// Both live in the same place — the character's wardrobe — and both have to
/// survive the prune that keeps a manifest down to what its author actually
/// changed. That prune is the whole risk here: it drops any outfit seeding
/// would put back, and an outfit carrying an override looks exactly like one
/// that would.
/// </summary>
public sealed class VanillaOutfitOverrideTests
{
    private readonly ITestOutputHelper _out;
    public VanillaOutfitOverrideTests(ITestOutputHelper o) => _out = o;

    private static (ModPack Pack, CharacterDef Kate) Seeded()
    {
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        return (pack, pack.Characters.Single(c => c.VanillaCharacter == "Kate"));
    }

    [Fact]
    public void ACharacterNobodyTouchedStillWritesNothing()
    {
        // The baseline the rest of this rests on, and the promise to every
        // pack that already exists: none of these new fields appear anywhere
        // until somebody uses one.
        var (pack, _) = Seeded();
        string json = PackRepository.SerializeAsSaved(pack);

        Assert.DoesNotContain("spriteOverrides", json);
        Assert.DoesNotContain("packArt", json);
        Assert.DoesNotContain("\"vanillaCharacter\": \"Kate\"", json);
    }

    [Fact]
    public void ReplacingOneTextureIsKept()
    {
        var (pack, kate) = Seeded();
        var plain = kate.Outfits[0];

        plain.SpriteOverrides.Add(new SpriteOverrideDef
        {
            Slot = SpriteSlotNames.Mouth[0],
            Sprite = "art/kate-mouth1.png",
        });

        Assert.False(VanillaCastSeed.IsUntouched(kate));

        string json = PackRepository.SerializeAsSaved(pack);
        _out.WriteLine(json.Substring(0, System.Math.Min(400, json.Length)));

        Assert.Contains("art/kate-mouth1.png", json);
        Assert.Contains("\"slot\": \"mouth1\"", json);
        // And the outfit it hangs on came with it, or the path names nothing.
        Assert.Contains(plain.GameObjectName, json);
    }

    [Fact]
    public void TheRestOfTheWardrobeStillGoesAway()
    {
        // Keeping the overridden outfit must not drag the other sixty back in.
        // The prune is what stops a pack asserting a whole cast it never
        // edited, and one change is not a reason to abandon it.
        var (pack, kate) = Seeded();
        int wardrobe = kate.Outfits.Count;
        Assert.True(wardrobe > 3, "Kate should have several outfits to prune");

        kate.Outfits[0].SpriteOverrides.Add(new SpriteOverrideDef
        {
            Slot = SpriteSlotNames.Base,
            Sprite = "art/kate.png",
        });

        var restore = VanillaCastSeed.PrepareForSave(pack);
        try
        {
            var written = pack.Characters.Single(c => c.VanillaCharacter == "Kate");
            _out.WriteLine($"{written.Outfits.Count} of {wardrobe} outfits written");
            Assert.Single(written.Outfits);
            Assert.Equal(SpriteSlotNames.Base, written.Outfits[0].SpriteOverrides[0].Slot);

            // The default outfit has to be spelled out before the list it
            // points into is taken away - the same reason Reduced already
            // does it for a character with an added outfit.
            Assert.False(string.IsNullOrEmpty(written.DefaultOutfit));
        }
        finally { restore(); }

        // Saving changed nothing on screen.
        Assert.Equal(wardrobe, kate.Outfits.Count);
    }

    [Fact]
    public void ATickWithNoArtYetIsStillRemembered()
    {
        // An author who ticks "replace the blink" and closes the editor before
        // choosing a file has said something. Dropping it would silently untick
        // the box; the editor's job is to complain about it, not this one's.
        var (pack, kate) = Seeded();
        kate.Outfits[0].SpriteOverrides.Add(new SpriteOverrideDef { Slot = SpriteSlotNames.Blink });

        Assert.False(VanillaCastSeed.IsUntouched(kate));
        Assert.Contains("\"slot\": \"blink\"", PackRepository.SerializeAsSaved(pack));
    }

    [Fact]
    public void ANewBustForOneOfTheGamesCharactersIsKept()
    {
        var (pack, kate) = Seeded();

        kate.Outfits.Add(new OutfitDef
        {
            Key = "spacesuit",
            GameObjectName = "Kate_Spacesuit",
            PackArt = true,
            BaseSprite = "art/kate-spacesuit.png",
        });

        Assert.False(VanillaCastSeed.IsUntouched(kate));

        var restore = VanillaCastSeed.PrepareForSave(pack);
        try
        {
            var written = pack.Characters.Single(c => c.VanillaCharacter == "Kate");
            Assert.Single(written.Outfits);
            Assert.Equal("Kate_Spacesuit", written.Outfits[0].GameObjectName);
            Assert.True(written.Outfits[0].PackArt);
        }
        finally { restore(); }
    }

    [Fact]
    public void ANewBustSaysSoEvenBeforeItHasArt()
    {
        // The reason packArt is written down rather than worked out from
        // whether the outfit has sprites: for its first few minutes a new
        // outfit has none, and "has art" would call it one of the game's own
        // and grey out every field the author opened it to fill in.
        var (pack, kate) = Seeded();
        kate.Outfits.Add(new OutfitDef { Key = "wip", GameObjectName = "Kate_WIP", PackArt = true });

        string json = PackRepository.SerializeAsSaved(pack);
        Assert.Contains("\"packArt\": true", json);
        Assert.Contains("Kate_WIP", json);
    }

    // ── What the editor complains about ───────────────────────────────

    private static System.Collections.Generic.List<Validation.ValidationIssue> Complaints(
        ModPack pack, string code)
        => Validation.PackValidator.Validate(pack, "").Where(i => i.Code == code).ToList();

    [Fact]
    public void ATickWithNoArtIsWorthSayingOutLoud()
    {
        var (pack, kate) = Seeded();
        kate.Outfits[0].SpriteOverrides.Add(new SpriteOverrideDef { Slot = SpriteSlotNames.Blink });

        var one = Assert.Single(Complaints(pack, "outfit.overrideNoArt"));
        _out.WriteLine($"{one.Severity} {one.Where}: {one.Message}");
        Assert.Equal(Validation.Severity.Warning, one.Severity);
        Assert.Contains("Blink", one.Message);

        // The other half of the pair: with art chosen there is nothing to say.
        // Without this, a rule that complained about everything would pass.
        kate.Outfits[0].SpriteOverrides[0].Sprite = "art/blink.png";
        Assert.Empty(Complaints(pack, "outfit.overrideNoArt"));
    }

    [Fact]
    public void ReplacingTheSameTextureTwiceIsAnError()
    {
        var (pack, kate) = Seeded();
        kate.Outfits[0].SpriteOverrides.Add(
            new SpriteOverrideDef { Slot = SpriteSlotNames.Base, Sprite = "a.png" });
        kate.Outfits[0].SpriteOverrides.Add(
            new SpriteOverrideDef { Slot = SpriteSlotNames.Base, Sprite = "b.png" });

        var one = Assert.Single(Complaints(pack, "outfit.overrideDuplicate"));
        Assert.Equal(Validation.Severity.Error, one.Severity);

        // Two DIFFERENT slots are the ordinary case and must stay quiet.
        kate.Outfits[0].SpriteOverrides[1].Slot = SpriteSlotNames.Blink;
        Assert.Empty(Complaints(pack, "outfit.overrideDuplicate"));
    }

    [Fact]
    public void ANewBustNamedAfterOneOfTheGamesIsAnError()
    {
        // The collision the runtime cannot resolve, and the reason a vanilla
        // character's whole wardrobe used to be skipped at build time: two
        // GameObjects with one name under 2_Bust_Manager, and every reference
        // to that name - the game's own included - reaching whichever won.
        var (pack, kate) = Seeded();
        string real = kate.Outfits[1].GameObjectName;

        kate.Outfits.Add(new OutfitDef
        {
            Key = "clash", GameObjectName = real, PackArt = true, BaseSprite = "art/x.png",
        });

        var one = Assert.Single(Complaints(pack, "outfit.collidesWithVanillaBust"));
        _out.WriteLine($"{one.Severity} {one.Where}: {one.Message}");
        Assert.Equal(Validation.Severity.Error, one.Severity);

        // A name of its own is fine.
        kate.Outfits[^1].GameObjectName = "Kate_SomethingNobodyHas";
        Assert.Empty(Complaints(pack, "outfit.collidesWithVanillaBust"));
    }

    [Fact]
    public void ANewBustIsCheckedForArtLikeAnyOther()
    {
        // A bust the pack draws for one of the game's characters is the pack's
        // art, so the missing-file checks that skip a borrowed outfit have to
        // find it. Before packArt existed there was no way to tell the two
        // apart, and this one would have been waved through with no sprites.
        var (pack, kate) = Seeded();
        kate.Outfits.Add(new OutfitDef
        {
            Key = "spacesuit", GameObjectName = "Kate_Spacesuit", PackArt = true,
        });

        var missing = Validation.PackValidator.Validate(pack, "")
            .Where(i => i.Where.Contains("Kate_Spacesuit") || i.Where.Contains("spacesuit"))
            .ToList();
        foreach (var i in missing.Take(5)) _out.WriteLine($"{i.Severity} {i.Where}: {i.Message}");
        Assert.NotEmpty(missing);

        // ...and one of the game's own busts beside it is still left alone.
        Assert.DoesNotContain(Validation.PackValidator.Validate(pack, ""),
                              i => i.Where.Contains(kate.Outfits[0].GameObjectName)
                                && i.Message.Contains("baseSprite"));
    }

    [Fact]
    public void SlotNamesAreOneVocabulary()
    {
        // The editor, the validator and the runtime all spell these; a typo in
        // one of the three is a texture that silently never gets replaced.
        Assert.Equal(7, SpriteSlotNames.Fixed.Length);
        Assert.Contains(SpriteSlotNames.Base, SpriteSlotNames.Fixed);
        Assert.Contains(SpriteSlotNames.Mask, SpriteSlotNames.Fixed);
        Assert.Contains(SpriteSlotNames.Blink, SpriteSlotNames.Fixed);
        foreach (string m in SpriteSlotNames.Mouth) Assert.Contains(m, SpriteSlotNames.Fixed);

        // A face is namespaced so it can never collide with a fixed slot, and
        // round-trips.
        Assert.Equal("expression:Happy", SpriteSlotNames.Expression("Happy"));
        Assert.Equal("Happy", SpriteSlotNames.ExpressionOf("expression:Happy"));

        // The control: a fixed slot is not a face, however much it looks like
        // a name. Without this, "blink" would read as an expression called
        // blink and the runtime would hunt for a child that is not there.
        Assert.Null(SpriteSlotNames.ExpressionOf(SpriteSlotNames.Blink));
        Assert.Null(SpriteSlotNames.ExpressionOf("mouth1"));
        Assert.Null(SpriteSlotNames.ExpressionOf(null));
    }

    [Fact]
    public void OneSlotIsAskedForByName()
    {
        var outfit = new OutfitDef();
        Assert.Null(outfit.OverrideFor(SpriteSlotNames.Base));

        outfit.SpriteOverrides.Add(new SpriteOverrideDef { Slot = SpriteSlotNames.Base, Sprite = "a.png" });
        Assert.Equal("a.png", outfit.OverrideFor(SpriteSlotNames.Base));

        // Absent and empty are different answers: one is "the pack says
        // nothing", the other "the pack asked and has not chosen yet".
        outfit.SpriteOverrides.Add(new SpriteOverrideDef { Slot = SpriteSlotNames.Blink });
        Assert.Equal("", outfit.OverrideFor(SpriteSlotNames.Blink));
        Assert.Null(outfit.OverrideFor(SpriteSlotNames.Mask));
    }
}
