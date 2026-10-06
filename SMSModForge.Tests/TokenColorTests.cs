using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's name tokens in the colour of whoever they stand in for (the
/// author, 1.7.0): {PC} the player's, {M} Anna's, {D} Josef's, {B} and {S}
/// Adrian's, {DA} Emma's - and {F}, nobody's, the token mark as before.
/// </summary>
public sealed class TokenColorTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-tokens-" + Guid.NewGuid().ToString("N"));

    public TokenColorTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private MainViewModel Open()
    {
        PackRepository.Save(PackRepository.CreateEmpty("tokens.pack"), _dir);
        var vm = new MainViewModel();
        vm.OpenPackFromPath(_dir);
        return vm;
    }

    private static UiColor Of(System.Windows.Media.Color c) => new(c.B, c.G, c.R, 255);

    [Theory]
    [InlineData("{M}", "Anna")]
    [InlineData("{D}", "Josef")]
    [InlineData("{B}", "Adrian")]
    [InlineData("{S}", "Adrian")]
    [InlineData("{DA}", "Emma")]
    public void AFamilyWordIsInTheColourOfWhomItIsSaidOf(string token, string character)
    {
        var vm = Open();
        var who = vm.Characters.Single(c => string.Equals(c.Key, VanillaCharacters.KeyFor(character), StringComparison.OrdinalIgnoreCase));
        _out.WriteLine($"{token} -> {character} {who.NameColor}");
        Assert.False(string.IsNullOrWhiteSpace(who.NameColor), $"{character} should have a name colour");
        Assert.Equal(Of(who.NameColorValue), DialogueMarkup.TokenColor!(token));
    }

    [Fact]
    public void ThePlayersNameIsInThePlayersColour_AndTheFamilyAsAWholeKeepsTheMark()
    {
        var vm = Open();
        var player = vm.Characters.Single(c => c.IsPlayer);
        _out.WriteLine($"player '{player.NameColor}'");
        // The game's: its speaker label says "You" for every player line, and
        // its word colouring has "you" at #B0B0B0 - read from 1.8E's files.
        Assert.Equal(new UiColor(0xB0, 0xB0, 0xB0, 255), DialogueMarkup.TokenColor!("{PC}"));
        Assert.Equal("#B0B0B0", player.NameColor);
        player.NameColor = "#C0FFEE";
        Assert.Equal(new UiColor(0xEE, 0xFF, 0xC0, 255), DialogueMarkup.TokenColor!("{PC}"));
        Assert.Null(DialogueMarkup.TokenColor!("{F}"));
    }

    [Fact]
    public void AColourThePackChangesIsTheOneUsed()
    {
        var vm = Open();
        var anna = vm.Characters.Single(c => string.Equals(c.Key, VanillaCharacters.KeyFor("Anna"), StringComparison.OrdinalIgnoreCase));
        anna.NameColor = "#12AB34";
        Assert.Equal(new UiColor(0x34, 0xAB, 0x12, 255), DialogueMarkup.TokenColor!("{M}"));
    }
}
