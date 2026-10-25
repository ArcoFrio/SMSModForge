using System.Collections.Generic;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// When a runtime name follows its display name (the author, 2026-10-21: "I
/// don't want the Runtime name to be blabla_copy"). New records follow; a
/// loaded one follows only while its key is still its display name made into
/// a name, or a duplicate's "_copy"; a typed key never does.
/// </summary>
public class DerivedKeyTests
{
    private static string? RenameLoaded(string key, string display, string to, bool remembered = false)
    {
        var d = new DerivedKey();
        d.Watch(() => new List<string> { key, "Other" }, display, () => remembered);
        return d.Next(to, key);
    }

    [Fact]
    public void ALoadedKeyThatIsItsDisplayNameFollowsTheRename()
        => Assert.Equal("BeachNight", RenameLoaded("BeachDay", "Beach Day", "Beach Night"));

    [Fact]
    public void ALoadedKeySomebodyTypedStays()
        // The Androids pack's places: "SecretBeach" is called "Remote Area".
        => Assert.Null(RenameLoaded("SecretBeach", "Remote Area", "Hidden Beach"));

    [Theory]
    [InlineData("CinderellaVoyeurSecretbeachScene01_copy")]
    [InlineData("CinderellaVoyeurSecretbeachScene01_copy3")]
    public void ADuplicatesCopyLeftoverFollows(string key)
        => Assert.Equal("CinderellaVoyeurSecretbeachScene02",
                        RenameLoaded(key, "CinderellaVoyeurSecretbeachScene02", "CinderellaVoyeurSecretbeachScene02"));

    [Fact]
    public void AKeyPlayersSavesRememberStays()
        => Assert.Null(RenameLoaded("BeachDay", "Beach Day", "Beach Night", remembered: true));

    [Fact]
    public void TheDecisionIsMadeOnceFromTheNameItWasLoadedWith()
    {
        var d = new DerivedKey();
        d.Watch(() => new[] { "BeachDay" }, "Beach Day");
        Assert.Equal("Beach", d.Next("Beach", "BeachDay"));
        Assert.Equal("BeachAtNight", d.Next("Beach at Night", "Beach"));
    }

    [Fact]
    public void ATypedKeyStopsAWatchedOneForGood()
    {
        var d = new DerivedKey();
        d.Watch(() => new[] { "BeachDay" }, "Beach Day");
        d.Stop();
        Assert.Null(d.Next("Beach Night", "BeachDay"));
    }

    /// <summary>
    /// The author's own (1.8.0): GSDialogueStory06 was renamed from 05 before
    /// display names carried keys, so its key stayed GSDialogueStory05 and
    /// did not follow. Rubbing the 6 out takes the display name back into the
    /// part the key was made from, and the key follows again from there.
    /// </summary>
    [Fact]
    public void RubbingTheNameBackIntoItsKeyFollowsAgain()
    {
        var d = new DerivedKey();
        d.Watch(() => new[] { "GSDialogueStory05" }, "GSDialogueStory06");
        Assert.Null(d.Next("GSDialogueStory06 ", "GSDialogueStory05"));          // nothing rubbed out: stays
        Assert.Equal("GSDialogueStory0", d.Next("GSDialogueStory0", "GSDialogueStory05"));
        Assert.Equal("GSDialogueStory06", d.Next("GSDialogueStory06", "GSDialogueStory0"));
    }

    /// <summary>The same in a conversation, which keeps its own copy of the
    /// rule - the author's example is one.</summary>
    [Fact]
    public void AConversationsKeyFollowsAgainToo()
    {
        var d = new DialogueViewModel(new SMSModForge.Model.DialogueDef { Key = "GSDialogueStory05", DisplayName = "GSDialogueStory06" });
        d.WatchDisplayName(() => new[] { d });
        d.DisplayName = "GSDialogueStory06 ";
        Assert.Equal("GSDialogueStory05", d.Key);
        d.DisplayName = "GSDialogueStory0";
        d.DisplayName = "GSDialogueStory06";
        Assert.Equal("GSDialogueStory06", d.Key);
        Assert.True(d.KeyIsDerived);
    }

    [Theory]
    [InlineData("GSDialogueStory06", "GSDialogueStory07", "GSDialogueStory05", true)]    // replaced in one go (the F2 box)
    [InlineData("GSDialogueStory06", "GSDialogueTale06", "GSDialogueStory05", true)]     // "Story" was the key's too
    [InlineData("GSDialogueStory06", "GSDialogueStory06b", "GSDialogueStory05", false)]  // only added after it
    [InlineData("Remote Area", "Remote", "SecretBeach", false)]                          // nothing in common
    [InlineData("Sb intro", "Sb", "sb_intro", false)]                                    // the key's own spelling
    [InlineData("Beach Day Two", "Beach", "BeachNightAndDay", false)]                     // under half the key
    [InlineData("Beach Party", "Beach Night", "BeachNight", true)]                        // back to the key itself
    public void WhatCountsAsRubbingOut(string from, string to, string key, bool follows)
        => Assert.Equal(follows, DerivedKey.ErasedBackInto(from, to, key));

    [Fact]
    public void ATypedKeyIsNeverTakenBackByRubbingOut()
    {
        var d = new DerivedKey();
        d.Watch(() => new[] { "GSDialogueStory05" }, "GSDialogueStory06");
        d.Stop();
        Assert.Null(d.Next("GSDialogueStory0", "GSDialogueStory05"));
    }

    [Fact]
    public void AKeyPlayersSavesRememberIsNeverTakenBackByRubbingOut()
    {
        var d = new DerivedKey();
        d.Watch(() => new[] { "PhotoDay05" }, "PhotoDay06", () => true);
        Assert.Null(d.Next("PhotoDay0", "PhotoDay05"));
    }

    [Fact]
    public void AFollowedKeyStaysClearOfItsSiblings()
    {
        var d = new DerivedKey();
        d.Watch(() => new[] { "BeachDay", "BeachNight" }, "Beach Day");
        Assert.Equal("BeachNight2", d.Next("Beach Night", "BeachDay"));
    }
}
