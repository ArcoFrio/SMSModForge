using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The hints under the game's variables (1.9.0) as the editor finds them: one
/// for every variable the picker offers, the right one for a name two
/// variables share, none for a name the game does not have.
/// </summary>
public sealed class VanillaVariableHintsTests
{
    private readonly ITestOutputHelper _out;
    public VanillaVariableHintsTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void EveryVariableTheGameHasHasItsHint()
    {
        var missing = VanillaGameVariables.All
            .Where(v => !Loc.English.Has(VanillaVariableHints.TextKeyOf(v.List, v.Name)))
            .Select(v => v.List + "[" + v.Name + "]").ToList();
        foreach (string m in missing.Take(20)) _out.WriteLine("no hint: " + m);
        Assert.Empty(missing);

        // And every name the picker offers shows one.
        Assert.All(VanillaGameVariables.AllNames, n => Assert.NotEqual("", VanillaVariableHints.For(n)));
        // The control: nothing for a name the game never had.
        Assert.Equal("", VanillaVariableHints.For("a-name-the-game-never-had"));
        Assert.Equal("", VanillaVariableHints.For(""));
    }

    /// <summary>
    /// The texts' keys ignore case and the game's names do not: "Mei-corrupted"
    /// (her Japan star) and "mei-corrupted" (her first hotel visit) are two
    /// variables with two meanings, and the picker offers and describes both.
    /// </summary>
    [Fact]
    public void TwoNamesThatDifferOnlyInCapitalsAreTwoVariablesWithTwoHints()
    {
        Assert.Contains("Mei-corrupted", VanillaGameVariables.AllNames);
        Assert.Contains("mei-corrupted", VanillaGameVariables.AllNames);
        string upper = VanillaVariableHints.For("Mei-corrupted"), lower = VanillaVariableHints.For("mei-corrupted");
        _out.WriteLine("Mei-corrupted: " + upper);
        _out.WriteLine("mei-corrupted: " + lower);
        Assert.NotEqual("", upper);
        Assert.NotEqual("", lower);
        Assert.NotEqual(upper, lower);
        // Another spelling reaches neither, in the game or here.
        Assert.Equal("", VanillaVariableHints.For("MEI-CORRUPTED"));
        Assert.False(VanillaGameVariables.Contains("MEI-CORRUPTED"));
    }

    [Fact]
    public void AVariableTheGameNeverUsesSaysSo_AndOnlyThose()
    {
        var notUsed = VanillaGameVariables.All.Where(v => VanillaVariableHints.IsNotUsed(v.List, v.Name)).ToList();
        _out.WriteLine($"{notUsed.Count} of {VanillaGameVariables.All.Count} never used");
        Assert.NotEmpty(notUsed);
        Assert.All(notUsed, v => Assert.Equal(VanillaVariableHints.NotUsedKey, VanillaVariableHints.TextKeyOf(v.List, v.Name)));
        // The control: a variable the game uses has a text of its own.
        var used = VanillaGameVariables.All.First(v => v.List == "Inventory" && v.Name == "Black-Lingerie");
        Assert.False(VanillaVariableHints.IsNotUsed(used.List, used.Name));
        Assert.NotEqual(VanillaVariableHints.NotUsedKey, VanillaVariableHints.TextKeyOf(used.List, used.Name));
    }

    /// <summary>A name in several lists is described as the FIRST list's, the
    /// one a pack's name-only address reaches.</summary>
    [Fact]
    public void ANameInSeveralListsIsDescribedAsTheFirstListsVariable()
    {
        var shared = VanillaGameVariables.All.GroupBy(v => v.Name).First(g => g.Select(v => v.List).Distinct().Count() > 1
                                                                            && g.Select(v => Loc.T(VanillaVariableHints.TextKeyOf(v.List, v.Name))).Distinct().Count() > 1);
        var first = shared.First();
        _out.WriteLine($"{shared.Key}: {string.Join(", ", shared.Select(v => v.List))}");
        Assert.StartsWith(Loc.T(VanillaVariableHints.TextKeyOf(first.List, first.Name)), VanillaVariableHints.For(shared.Key));
    }
}
