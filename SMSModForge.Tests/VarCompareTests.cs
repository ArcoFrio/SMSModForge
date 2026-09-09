using System.Globalization;
using System.Threading;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Comparing a game variable against what a pack asked for.
/// <para/>
/// Reported: gifts keyed to the game's own booleans never showed, with those
/// booleans true. The check compared <c>g.ToString()</c> against the authored
/// text, and a boxed bool prints <c>"True"</c> - so every vanilla boolean
/// condition in every pack was false whatever the variable held, and said
/// nothing about it.
/// </summary>
public sealed class VarCompareTests
{
    private readonly ITestOutputHelper _out;
    public VarCompareTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void The_reported_case_a_true_boolean_and_an_authored_true()
    {
        // What the game hands back is a boxed bool; what the pack wrote is the
        // word. These have to agree.
        object fromTheGame = true;

        _out.WriteLine($"ToString() gives {fromTheGame} - which is why the old check failed");
        Assert.NotEqual("true", fromTheGame.ToString());          // the bug, in one line

        Assert.True(VarCompare.Matches(fromTheGame, "true"));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(true, "True")]
    [InlineData(true, "TRUE")]
    [InlineData(false, "false")]
    [InlineData(false, "False")]
    public void A_boolean_reads_the_same_however_it_is_spelled(bool actual, string expected)
        => Assert.True(VarCompare.Matches(actual, expected));

    [Theory]
    [InlineData(true, "false")]
    [InlineData(false, "true")]
    [InlineData(true, "")]
    [InlineData(true, "yes")]
    [InlineData(true, "1")]
    public void And_still_says_no_when_it_should(bool actual, string expected)
        => Assert.False(VarCompare.Matches(actual, expected));

    [Fact]
    public void A_number_is_read_as_a_number_not_as_the_machines_text()
    {
        // The same bug waiting: ToString() follows the current culture, so a
        // variable holding 1.5 prints "1,5" on this machine and would never
        // match an authored "1.5".
        var was = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");   // comma decimals
            _out.WriteLine("1.5 prints as " + 1.5.ToString());

            Assert.True(VarCompare.Matches(1.5, "1.5"));
            Assert.True(VarCompare.Matches(2.0, "2"));
            Assert.True(VarCompare.Matches(7, "7"));
            Assert.False(VarCompare.Matches(1.5, "1,5"));      // not the pack's spelling
        }
        finally { Thread.CurrentThread.CurrentCulture = was; }
    }

    [Fact]
    public void A_string_is_compared_exactly()
    {
        Assert.True(VarCompare.Matches("Anis", "Anis"));
        Assert.False(VarCompare.Matches("Anis", "anis"));      // a name is a name
        Assert.True(VarCompare.Matches("", ""));
    }

    [Fact]
    public void A_variable_that_is_not_there_matches_nothing()
    {
        // Including the empty string: "no such variable" and "it is empty" are
        // different answers, and only the second should match "".
        Assert.False(VarCompare.Matches(null, ""));
        Assert.False(VarCompare.Matches(null, "true"));
        Assert.False(VarCompare.Matches(null, "0"));
    }

    [Fact]
    public void Surrounding_space_in_what_was_authored_does_not_break_it()
    {
        Assert.True(VarCompare.Matches(true, " true "));
        Assert.True(VarCompare.Matches(3, " 3"));
    }

    [Fact]
    public void Writing_a_value_down_reads_the_way_a_pack_is_authored()
    {
        var was = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");

            Assert.Equal("true", VarCompare.Text(true));
            Assert.Equal("false", VarCompare.Text(false));
            Assert.Equal("1.5", VarCompare.Text(1.5));
            Assert.Equal("Anis", VarCompare.Text("Anis"));
            Assert.Equal("", VarCompare.Text(null));
        }
        finally { Thread.CurrentThread.CurrentCulture = was; }
    }
}
