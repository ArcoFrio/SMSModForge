using System.Collections.Generic;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Putting variables into a string.
/// <para/>
/// Reported: a list named <c>Gifting_Gifted_$Gifting_Target</c> did not resolve
/// to <c>Gifting_Gifted_Anis</c>. It could not - substitution only ever
/// replaced a value that was ENTIRELY a name, and a list's name was never
/// substituted at all - so the condition read a list nothing had ever written,
/// found nothing, and being negated passed every single time.
/// <para/>
/// These test the runtime's own parser. The file is compiled into both projects
/// from one source precisely so this suite can reach it: a pack that reads one
/// way while authored and another while played would be worse than either.
/// </summary>
public sealed class VarTextTests
{
    private readonly ITestOutputHelper _out;
    public VarTextTests(ITestOutputHelper o) => _out = o;

    private static readonly Dictionary<string, string> Vars = new()
    {
        ["Gifting_Target"] = "Anis",
        ["Empty"] = "",
        ["Body-Oil"] = "true",
    };

    private static string Go(string raw)
        => VarText.Resolve(raw, n => Vars.TryGetValue(n, out var v) ? v : null);

    [Fact]
    public void The_reported_case()
    {
        Assert.Equal("Gifting_Gifted_Anis", Go("Gifting_Gifted_$Gifting_Target"));
    }

    [Fact]
    public void It_follows_the_variable_rather_than_being_fixed_once()
    {
        // The whole point: pick Anis, then pick Amber a moment later, and the
        // same setting has to read the second one. Nothing is cached, so the
        // answer is whatever the lookup says at the moment it is asked.
        string target = "Anis";
        string Ask() => VarText.Resolve("Gifting_Gifted_$Gifting_Target",
                                        n => n == "Gifting_Target" ? target : null);

        Assert.Equal("Gifting_Gifted_Anis", Ask());

        target = "Amber";
        Assert.Equal("Gifting_Gifted_Amber", Ask());

        target = "Tasha";
        _out.WriteLine(Ask());
        Assert.Equal("Gifting_Gifted_Tasha", Ask());
    }

    [Fact]
    public void A_target_with_no_value_yet_comes_out_empty()
    {
        // Before anyone has been chosen. It has to be an empty answer rather
        // than the text left as written, or the condition would go looking for
        // a list literally called "Gifting_Gifted_$Gifting_Target".
        Assert.Equal("Gifting_Gifted_", VarText.Resolve("Gifting_Gifted_$Gifting_Target",
                                                        _ => null));
        Assert.Equal("Gifting_Gifted_", Go("Gifting_Gifted_$Empty"));
    }

    [Fact]
    public void Money_is_left_alone()
    {
        // 225 strings in one pack begin with a dollar and every one is a price.
        // A name cannot start with a digit, so none of them are touched.
        Assert.Equal("$1100", Go("$1100"));
        Assert.Equal("$250", Go("$250"));
        Assert.Equal("How does $100 per picture sound?", Go("How does $100 per picture sound?"));
    }

    [Fact]
    public void A_dollar_with_nothing_usable_after_it_stays_put()
    {
        Assert.Equal("$", Go("$"));
        Assert.Equal("cost: $ 5", Go("cost: $ 5"));
        Assert.Equal("a$-b", Go("a$-b"));
    }

    [Fact]
    public void Two_dollars_mean_one()
    {
        Assert.Equal("$Gifting_Target", Go("$$Gifting_Target"));
        Assert.Equal("a$b", Go("a$$b"));
    }

    [Fact]
    public void What_used_to_work_still_does()
    {
        // Whole-value substitution was all there was before. It has to keep
        // meaning exactly what it meant.
        Assert.Equal("Anis", Go("$Gifting_Target"));
        Assert.Equal("plain", Go("plain"));
        Assert.Equal("", Go("$NoSuchVariable"));
        Assert.Equal("", Go(""));
        Assert.Null(Go(null!));
    }

    [Fact]
    public void Hyphens_belong_to_the_name_because_the_games_variables_use_them()
    {
        // Body-Oil, red-meat, Inv-energydrink. Stopping at the hyphen would
        // read "$Body" and hand back nothing.
        Assert.Equal("true", Go("$Body-Oil"));
    }

    [Fact]
    public void Braces_say_where_a_name_ends_when_the_text_carries_on()
    {
        // The other side of hyphens being part of a name: sometimes the text
        // continues with one and the name has to be closed off.
        Assert.Equal("Anis-suffix", Go("${Gifting_Target}-suffix"));
        Assert.Equal("xAnisy", Go("x${Gifting_Target}y"));

        // Unclosed, so there is nothing to read: left as typed.
        Assert.Equal("${Gifting_Target", Go("${Gifting_Target"));
    }

    [Fact]
    public void More_than_one_in_a_string()
    {
        Assert.Equal("Anis and Anis", Go("$Gifting_Target and $Gifting_Target"));
        Assert.Equal("Gifting_Anis_Anis", Go("Gifting_${Gifting_Target}_$Gifting_Target"));
    }

    [Fact]
    public void A_string_with_no_dollar_is_returned_as_it_came()
    {
        // The fast path, and the common one - almost every value in a pack.
        const string plain = "Gifting_Gifted_Anis";
        Assert.Same(plain, Go(plain));
    }
}
