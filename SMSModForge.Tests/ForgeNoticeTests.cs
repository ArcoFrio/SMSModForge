using System.Linq;
using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// ModForge's notice on the game's main menu (the author, 1.8.0): the whole
/// notice the first time, counted from 1.8.0 so a player of 1.7 sees it too;
/// after an update, a short list only when the update changes something for
/// players; nothing otherwise.
/// </summary>
public class ForgeNoticeTests
{
    private static readonly string[] News = { "1.8.0", "1.8.2", "1.9.0" };

    [Theory]
    [InlineData("", "1.8.0", ForgeNotice.Kind.Welcome)]          // never closed: a 1.7 player too
    [InlineData("not a version", "1.8.0", ForgeNotice.Kind.Welcome)]
    [InlineData("1.8.0", "1.8.0", ForgeNotice.Kind.None)]        // seen
    [InlineData("1.8.0", "1.8.1", ForgeNotice.Kind.None)]        // 1.8.1 changes nothing for players
    [InlineData("1.8.0", "1.8.2", ForgeNotice.Kind.Changes)]
    [InlineData("1.8.1", "1.9.0", ForgeNotice.Kind.Changes)]
    [InlineData("1.9.0", "1.8.2", ForgeNotice.Kind.None)]        // went back to an older one
    public void WhatIsShown(string lastShown, string running, ForgeNotice.Kind expected)
        => Assert.Equal(expected, ForgeNotice.Decide(lastShown, running, News));

    [Fact]
    public void TheNewsIsWhatCameAfterTheLastOneShownUpToTheOneRunning()
        => Assert.Equal(new[] { "1.8.2", "1.9.0" }, ForgeNotice.NewsSince("1.8.0", "1.9.0", new[] { "1.9.0", "1.8.2", "1.8.0", "2.0.0" }));

    [Fact]
    public void APlayerOfAnOlderVersionIsAlsoToldWhatIsNew()
    {
        var before = ForgeNotice.Welcome("1.8.0", usedBefore: true);
        var first = ForgeNotice.Welcome("1.8.0", usedBefore: false);   // the control: a first install
        var section = Assert.Single(before.Details);
        Assert.Equal("New in 1.8.0:", section.Heading);
        Assert.NotEmpty(section.Items);
        Assert.Empty(first.Details);
        Assert.Equal("ModForge is installed", first.Title);
        Assert.Contains("Mods folder", Assert.Single(first.Paragraphs));
    }

    /// <summary>Short (the author, 1.8.0: the first one had "way too much text
    /// for the regular user"): a sentence or two, and a line or two of news.</summary>
    [Fact]
    public void TheNoticeIsShort()
    {
        var text = ForgeNotice.Welcome("1.8.0", usedBefore: true);
        int words = string.Join(" ", text.Paragraphs.Concat(text.Details.SelectMany(d => d.Items)).Concat(text.After))
                          .Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(words <= 45, $"{words} words");
    }

    [Fact]
    public void AnUpdatesListIsNewestFirst()
    {
        var text = ForgeNotice.Changes("1.7.0", "1.8.0", new[] { "1.8.0" });
        Assert.Equal("ModForge was updated to 1.8.0", text.Title);
        Assert.Equal(new[] { "New in 1.8.0:" }, text.Details.Select(d => d.Heading));
    }

    /// <summary>Every version said to bring news has some, in English at
    /// least, and they are in order - a release that adds itself to the list
    /// without its lines would show an empty window.</summary>
    [Fact]
    public void EveryVersionWithNewsHasItsLines()
    {
        foreach (var version in ForgeNotice.VersionsWithNews)
        {
            Assert.NotNull(ForgeVersion.Parse(version));
            Assert.NotEmpty(ForgeNotice.News(version));
        }
        var sorted = ForgeNotice.VersionsWithNews.OrderBy(v => v, System.Collections.Generic.Comparer<string>.Create(ForgeVersion.Compare));
        Assert.Equal(sorted, ForgeNotice.VersionsWithNews);
        Assert.True(ForgeVersion.Compare(ForgeNotice.VersionsWithNews.Last(), ForgeVersion.Current) <= 0,
                    "a version with news that is not out yet");
    }
}
