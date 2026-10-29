using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A variable's default the validator cannot read as its type. After 1.8.0 an
/// editor bug was found writing "true" over the default of whichever variable
/// was open (see PickedConverter): an Int or Float was already flagged, and a
/// List said nothing while it lost its initial values. This is how an author
/// finds the ones it reached. Each with its control - the default put right.
/// </summary>
public sealed class VariableDefaultValidationTests
{
    private readonly ITestOutputHelper _out;
    public VariableDefaultValidationTests(ITestOutputHelper o) => _out = o;

    /// <summary>What the validator says about one variable's default.</summary>
    private List<ValidationIssue> About(PackVariableType type, string defaultValue)
    {
        var pack = PackRepository.CreateEmpty("defaults.pack");
        pack.Variables.Add(new PackVariableDef { Name = "v", Type = type, DefaultValue = defaultValue });
        var issues = PackValidator.Validate(pack, Path.GetTempPath())
                                  .Where(i => i.Where == "variables[v].defaultValue").ToList();
        foreach (var i in issues) _out.WriteLine($"{type} '{defaultValue}': {i.Severity} {i.Code}: {i.Message}");
        return issues;
    }

    [Fact]
    public void A_list_that_is_not_a_list_is_named()
    {
        var issues = About(PackVariableType.List, "true");
        Assert.Contains(issues, i => i.Code == "variable.listDefault" && i.Severity == Severity.Warning
                                     && i.Message.Contains("true"));

        // The controls: what the editor writes, from no values to several.
        Assert.Empty(About(PackVariableType.List, "[]"));
        Assert.Empty(About(PackVariableType.List, "[\"Elf\",\"Orc\"]"));
        Assert.Empty(About(PackVariableType.List, ""));     // starts empty, and says so
    }

    [Fact]
    public void A_number_that_is_not_a_number_is_named_as_before()
    {
        Assert.Contains(About(PackVariableType.Int, "true"), i => i.Code == "variable.intDefault");
        Assert.Contains(About(PackVariableType.Float, "true"), i => i.Code == "variable.floatDefault");
        Assert.Empty(About(PackVariableType.Int, "0"));
        Assert.Empty(About(PackVariableType.Float, "0.5"));
    }
}
