using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Which faces the game's own characters can pull.
/// <para/>
/// Generated from the bust art extraction, where every sprite records the
/// GameObject it came from — so the answer is the game's, not a guess. The
/// vanilla dialogues ask for Happy, Sad, Flirty and Angry by name, and until
/// now the editor offered an author nothing to connect those to.
/// </summary>
public sealed class VanillaExpressionTests
{
    private readonly ITestOutputHelper _out;
    public VanillaExpressionTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void TheFourAreTheOnesTheGamesOwnDialoguesAskFor()
    {
        // The point of the whole dataset: a name in a vanilla line has to
        // resolve to something the editor can offer.
        Assert.Equal(new[] { "Angry", "Flirty", "Happy", "Sad" },
                     VanillaBustExpressions.Standard);
    }

    [Fact]
    public void ABustEitherHasAllFourOrNone()
    {
        // The uniformity the editor's UI is built on, asserted rather than
        // assumed. The generator refuses to write if it breaks, but the
        // generator only runs when somebody remembers to run it.
        int with = VanillaBusts.All.Count(b => VanillaBustExpressions.Has(b.GoName));
        int without = VanillaBusts.All.Count - with;

        _out.WriteLine($"{with} busts can emote, {without} cannot");

        foreach (var bust in VanillaBusts.All)
        {
            var got = VanillaBustExpressions.For(bust.GoName);
            Assert.True(got.Length == 0 || got.Length == 4, bust.GoName);
        }

        Assert.True(with > 150, "almost nothing can emote - did the dataset get truncated?");
        Assert.True(without > 0, "everything can emote - the 'none' case would go untested");
    }

    [Fact]
    public void NothingHeldBackFromShippingIsNamed()
    {
        // The do-not-ship rule reaches this file too: busts excluded from the
        // catalog are excluded from the repository, and a generated dataset is
        // a side door into it.
        var catalogued = new System.Collections.Generic.HashSet<string>(
            VanillaBusts.All.Select(b => b.GoName), StringComparer.Ordinal);

        string source = File.ReadAllText(SourceOf("VanillaBustExpressions.cs"));
        var named = Regex.Matches(source, @"^\s{12}""([^""]+)"",\s*$", RegexOptions.Multiline)
                         .Select(m => m.Groups[1].Value)
                         .ToList();

        _out.WriteLine($"{named.Count} busts named in the dataset");
        Assert.NotEmpty(named);

        var strangers = named.Where(n => !catalogued.Contains(n)).ToList();
        Assert.True(strangers.Count == 0,
                    "named but not in the catalog: " + string.Join(", ", strangers));
    }

    [Fact]
    public void ACharactersFacesAreTheUnionOfWhatTheyWear()
    {
        var canEmote = VanillaCharacters.All.Where(c => c.CanEmote).ToList();
        var cannot = VanillaCharacters.All.Where(c => !c.CanEmote).ToList();

        _out.WriteLine($"{canEmote.Count} of {VanillaCharacters.All.Count} characters can emote");
        foreach (var c in cannot.Take(4))
            _out.WriteLine($"   cannot: {c.Name} ({c.Outfits.Count} outfit(s))");

        Assert.NotEmpty(canEmote);
        Assert.All(canEmote, c => Assert.Equal(4, c.Expressions.Count));

        // A character can emote exactly when something in their wardrobe can.
        foreach (var c in VanillaCharacters.All)
            Assert.Equal(c.Outfits.Any(VanillaBustExpressions.Has), c.CanEmote);
    }

    [Fact]
    public void SomethingTheGameNeverHadIsNotGivenAFace()
    {
        // The control. A lookup that answered for everything would offer
        // expressions on a pack's own bust, where they resolve to nothing.
        Assert.False(VanillaBustExpressions.Has("a-bust-the-game-never-had"));
        Assert.False(VanillaBustExpressions.Has(""));
        Assert.False(VanillaBustExpressions.Has(null!));
        Assert.Empty(VanillaBustExpressions.For("a-bust-the-game-never-had"));
    }

    private static string SourceOf(string fileName)
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null && !Directory.Exists(Path.Combine(here.FullName, "Shared")))
            here = here.Parent;

        Assert.NotNull(here);
        return Path.Combine(here!.FullName, "Shared", fileName);
    }
}
