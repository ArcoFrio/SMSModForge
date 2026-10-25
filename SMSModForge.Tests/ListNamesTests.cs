using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// How records are named in their tab's lists (the author, 1.8.0): the runtime
/// name in brackets only when it differs; places by their runtime name,
/// spelled out; a place's GameObject named by its runtime name.
/// </summary>
public class ListNamesTests
{
    [Theory]
    [InlineData("Beach Day", "BeachDay", "Beach Day")]                                    // the same name run together
    [InlineData("AnisAffection01Scene01", "AnisAffection01Scene01", "AnisAffection01Scene01")]
    [InlineData("Remote Area", "SecretBeach", "Remote Area (SecretBeach)")]                // says something: kept
    [InlineData("Scene 02", "Scene01_copy", "Scene 02 (Scene01_copy)")]
    [InlineData("", "BeachDay", "BeachDay")]
    public void TheRuntimeNameIsInBracketsOnlyWhenItDiffers(string display, string key, string shown)
        => Assert.Equal(shown, ListNames.Of(display, key));

    [Theory]
    [InlineData("HarborHomeBedroom", "Harbor Home Bedroom")]
    [InlineData("MountainLabCorridorNikke1", "Mountain Lab Corridor Nikke 1")]
    [InlineData("_SecretBeach", "Secret Beach")]
    [InlineData("GiftShop", "Gift Shop")]
    [InlineData("NPCRoom", "NPC Room")]
    public void APlacesRuntimeNameIsSpelledOut(string key, string spaced)
        => Assert.Equal(spaced, ListNames.Spaced(key));

    [Fact]
    public void APlacesObjectIsNamedByItsRuntimeNameAndFollowsIt()
    {
        var place = new PlaceViewModel(new PlaceDef { Key = "Beach", InternalName = "Beach", DisplayName = "Beach" });
        place.WatchDisplayName(() => new[] { "Beach" });
        Assert.False(place.InternalNameIsOwn);
        place.DisplayName = "Beach Night";
        Assert.Equal("BeachNight", place.Key);
        Assert.Equal("BeachNight", place.InternalName);
    }

    /// <summary>The control: an older place whose object has a name of its
    /// own keeps it - another mod may find the level by it - until the author
    /// says otherwise.</summary>
    [Fact]
    public void AnObjectNamedOtherwiseKeepsItsNameUntilAsked()
    {
        var place = new PlaceViewModel(new PlaceDef { Key = "Beach", InternalName = "_OldBeach", DisplayName = "Beach" });
        place.WatchDisplayName(() => new[] { "Beach" });
        Assert.True(place.InternalNameIsOwn);
        place.DisplayName = "Beach Night";
        Assert.Equal("_OldBeach", place.InternalName);
        place.UseRuntimeNameForObject();
        Assert.Equal("BeachNight", place.InternalName);
        Assert.False(place.InternalNameIsOwn);
    }
}
