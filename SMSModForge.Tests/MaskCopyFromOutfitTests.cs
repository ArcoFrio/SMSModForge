using System;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Starting a mask from another outfit's instead of from an empty canvas.
/// <para/>
/// Most outfits of a character are the same bust in different clothes, so one
/// of them is nearly always a better starting point than nothing — and a jiggle
/// mask is three intensity planes painted by hand, which is a long way to come
/// twice.
/// <para/>
/// The thing these mostly guard is where the feature must NOT appear. A place's
/// two masks are the background and the foreground of one scene: they are not
/// versions of each other, and offering to paste one over the other is offering
/// a mistake with a button on it. That is a property of the HOST rather than of
/// the painter, so it is checked on the hosts.
/// </summary>
public sealed class MaskCopyFromOutfitTests
{
    private readonly ITestOutputHelper _out;
    public MaskCopyFromOutfitTests(ITestOutputHelper o) => _out = o;

    private static CharacterViewModel Character()
    {
        var model = new CharacterDef { Key = "anna", DefaultOutfit = "day" };
        model.Outfits.Add(new OutfitDef { Key = "day", MaskSprite = "Art/Anna/DayMask.png" });
        model.Outfits.Add(new OutfitDef { Key = "night", MaskSprite = "Art/Anna/NightMask.png" });
        model.Outfits.Add(new OutfitDef { Key = "swim" });          // no mask yet
        return new CharacterViewModel(model);
    }

    private static IMaskEditorHost Host(CharacterViewModel c, string key)
        => c.Outfits.Single(o => o.Key == key);

    [Fact]
    public void AnOutfitOffersItsSiblingsMasks()
    {
        var c = Character();
        var offered = Host(c, "day").OtherMasks;

        _out.WriteLine(string.Join(", ", offered.Select(m => $"{m.Label} -> {m.MaskPath}")));

        var one = Assert.Single(offered);
        Assert.Equal("night", one.Label);
        Assert.Equal("Art/Anna/NightMask.png", one.MaskPath);
    }

    [Fact]
    public void AnOutfitDoesNotOfferItself()
    {
        var c = Character();
        Assert.DoesNotContain(Host(c, "night").OtherMasks, m => m.Label == "night");
    }

    [Fact]
    public void AnOutfitWithNoMaskIsNotOffered()
    {
        // There is no layout on it to take, and listing it would be offering to
        // replace what somebody has painted with nothing.
        var c = Character();
        Assert.DoesNotContain(Host(c, "day").OtherMasks, m => m.Label == "swim");

        // ...but that outfit can still copy FROM the others, which is the whole
        // point of the feature for a new outfit.
        var forSwim = Host(c, "swim").OtherMasks;
        _out.WriteLine("a fresh outfit is offered: "
                       + string.Join(", ", forSwim.Select(m => m.Label)));
        Assert.Equal(2, forSwim.Count);
    }

    [Fact]
    public void AnOutfitWithNoCharacterOffersNothing()
    {
        // The vanilla-bust rows build an outfit with no owner, so there are no
        // siblings to reach.
        var lone = (IMaskEditorHost)new OutfitViewModel(
            new OutfitDef { Key = "alone", MaskSprite = "Art/Mask.png" });

        Assert.Empty(lone.OtherMasks);
    }

    [Fact]
    public void APlaceOffersNothingAtAll()
    {
        // The control, and the reason the default on IMaskEditorHost is empty
        // rather than something. A level's base and secondary masks are two
        // halves of one scene; pasting one over the other is a mistake, and the
        // painter hides the whole control when there is nothing to offer.
        var place = new PlaceViewModel(new PlaceDef { Key = "beach" });

        _out.WriteLine($"base offers {place.BaseMaskHost.OtherMasks.Count}, "
                       + $"secondary offers {place.SecondaryMaskHost.OtherMasks.Count}");

        Assert.Empty(place.BaseMaskHost.OtherMasks);
        Assert.Empty(place.SecondaryMaskHost.OtherMasks);
    }

    [Fact]
    public void AnNpcOffersNothingEither()
    {
        var npc = (IMaskEditorHost)new NpcViewModel(new NpcDef { Key = "cat" });
        Assert.Empty(npc.OtherMasks);
    }

    [Fact]
    public void TheListFollowsTheOutfitsOwnName()
    {
        // What an author reads in the painter is the outfit's key, so renaming
        // one has to show up there rather than leaving the old name behind.
        var c = Character();
        c.Outfits.Single(o => o.Key == "night").Key = "evening";

        var offered = Host(c, "day").OtherMasks;
        _out.WriteLine("after the rename: " + string.Join(", ", offered.Select(m => m.Label)));
        Assert.Equal("evening", Assert.Single(offered).Label);
    }
}
