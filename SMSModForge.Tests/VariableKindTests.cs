using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What kind a variable holds, said on the row that uses it.
/// <para/>
/// A comparison runs on STRINGS at runtime, so <c>True</c> against a variable
/// holding <c>true</c> simply does not match and nothing says why. A tick box
/// fixes it where the editor knows the kind — and until now the editor only
/// knew for the pack's own, so every one of the game's 1,386 Boolean variables
/// got a text box and an author typing "True" into it.
/// <para/>
/// The note beside the name is the other half: it is what makes an unexpected
/// text box read as "the editor does not recognise this name" rather than as
/// "this variable is text".
/// </summary>
public sealed class VariableKindTests
{
    private readonly ITestOutputHelper _out;
    public VariableKindTests(ITestOutputHelper o) => _out = o;

    // ── The catalogue ────────────────────────────────────────────────

    [Theory]
    [InlineData(PackVariableType.Bool, VariableKind.YesNo, "yes/no")]
    [InlineData(PackVariableType.Int, VariableKind.Number, "number")]
    [InlineData(PackVariableType.Float, VariableKind.Number, "number")]
    [InlineData(PackVariableType.String, VariableKind.Text, "text")]
    [InlineData(PackVariableType.List, VariableKind.List, "list")]
    public void APackVariablesTypeIsSaidInPlainWords(PackVariableType type,
                                                     VariableKind kind, string said)
    {
        // Whole against fractional is a distinction the runtime makes and an
        // author does not: both compare and increment the same way, so both
        // are called a number.
        Assert.Equal(kind, VariableTypes.Of(type));
        Assert.Equal(said, VariableTypes.Label(kind));
    }

    [Theory]
    [InlineData("Boolean", VariableKind.YesNo)]
    [InlineData("Double", VariableKind.Number)]
    [InlineData("String", VariableKind.Text)]
    [InlineData("Vector3", VariableKind.Position)]
    public void AGameVariablesTypeIsReadFromTheDump(string declared, VariableKind kind)
        => Assert.Equal(kind, VariableTypes.OfVanilla(declared));

    [Fact]
    public void AGameVariableNothingHasWrittenToYetSaysNothing()
    {
        // "null" is a real answer in that dump rather than a missing one: it is
        // what a GC2 global reads as before anything has set it, so the game
        // does not know its kind either. Claiming one would be inventing it.
        Assert.Equal(VariableKind.Unknown, VariableTypes.OfVanilla("null"));
        Assert.Equal("", VariableTypes.Label(VariableKind.Unknown));
    }

    [Fact]
    public void EveryTypeInTheShippedCatalogueIsUnderstood()
    {
        // The control for the four cases above, and the one that notices when
        // a re-dump brings a type nobody has mapped: the game's own list is
        // the input, so a new kind of variable shows up here rather than as a
        // blank note somebody eventually reports.
        var unmapped = VanillaGameVariables.All
            .Select(v => v.Type)
            .Distinct()
            .Where(t => t != "null" && VariableTypes.OfVanilla(t) == VariableKind.Unknown)
            .ToList();

        _out.WriteLine("types in the dump: "
                       + string.Join(", ", VanillaGameVariables.All.Select(v => v.Type).Distinct()));
        Assert.Empty(unmapped);
    }

    [Fact]
    public void ANameTheGameDoesNotHaveHasNoType()
    {
        Assert.Equal("", VanillaGameVariables.TypeOf("no-such-variable-anywhere"));
        Assert.Equal(VariableKind.Unknown, VariableTypes.OfVanilla(""));
    }

    [Fact]
    public void ANameInSeveralListsTakesTheFirstOnesType()
    {
        // Five of the 1,644 names appear in more than one list and three of
        // those disagree about their type. The address is name-only, so the
        // first list is the one anything resolving it will find - which makes
        // first-wins what the GAME does, not a coin toss.
        var android = VanillaGameVariables.All
            .Where(v => string.Equals(v.Name, "android", System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        _out.WriteLine(string.Join(", ", android.Select(v => $"{v.List}:{v.Type}")));
        Assert.True(android.Count > 1, "android is no longer a name in two lists");
        Assert.Equal(android[0].Type, VanillaGameVariables.TypeOf("android"));
        Assert.Equal(android[0].List, VanillaGameVariables.ListOf("android"));
    }

    // ── On a row ─────────────────────────────────────────────────────

    /// <summary>A condition row pointed at a variable, with a pack behind it.</summary>
    private static (MainViewModel Vm, NodeConditionViewModel Row) Row()
    {
        var vm = new MainViewModel();
        Declare(vm, "flag", PackVariableType.Bool);
        Declare(vm, "score", PackVariableType.Int);
        Declare(vm, "note", PackVariableType.String);

        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableCompare };
        return (vm, new NodeConditionViewModel(def, _ => { }));
    }

    /// <summary>Give the loaded pack a variable, the way the editor's own
    /// list holds them - the lookup reads the view models, not the file.</summary>
    private static void Declare(MainViewModel vm, string name, PackVariableType type)
    {
        var def = new PackVariableDef { Name = name, Type = type };
        vm.Pack.Variables.Add(def);
        vm.Variables.Add(new PackVariableViewModel(def));
    }

    [Theory]
    [InlineData("flag", "yes/no", true)]
    [InlineData("score", "number", false)]
    [InlineData("note", "text", false)]
    [InlineData("nothing-of-the-sort", "", false)]
    public void TheRowSaysWhatThePacksOwnVariableHolds(string name, string said, bool ticks)
    {
        var (_, row) = Row();
        row.VarSource = "Pack";
        row.VarName = name;

        _out.WriteLine($"{name}: '{row.VarKindNote}', tick box {row.VarValueIsBool}");
        Assert.Equal(said, row.VarKindNote);
        Assert.Equal(ticks, row.VarValueIsBool);
    }

    [Fact]
    public void TheRowSaysWhatOneOfTheGamesVariablesHoldsToo()
    {
        // The half that was missing. Nothing asked the game's catalogue, so a
        // vanilla Boolean got a text box and an author typing "True" into it -
        // which reads as a working condition and never matches.
        var (_, row) = Row();
        row.VarSource = "Vanilla";

        var aFlag = VanillaGameVariables.All.First(v => v.Type == "Boolean").Name;
        var aNumber = VanillaGameVariables.All.First(v => v.Type == "Double").Name;

        row.VarName = aFlag;
        _out.WriteLine($"{aFlag}: '{row.VarKindNote}', tick box {row.VarValueIsBool}");
        Assert.Equal("yes/no", row.VarKindNote);
        Assert.True(row.VarValueIsBool, "a game flag still gets a text box");

        row.VarName = aNumber;
        _out.WriteLine($"{aNumber}: '{row.VarKindNote}', tick box {row.VarValueIsBool}");
        Assert.Equal("number", row.VarKindNote);
        Assert.False(row.VarValueIsBool);
    }

    [Fact]
    public void SwitchingSidesReAsksTheRightCatalogue()
    {
        // The same word can be a flag in the pack and something else in the
        // game, so the answer follows the SIDE and not only the name. Without
        // this the note goes stale the moment somebody flips Source.
        var (vm, row) = Row();

        // A name the game has and the pack also declares, as a number.
        var aFlag = VanillaGameVariables.All.First(v => v.Type == "Boolean").Name;
        Declare(vm, aFlag, PackVariableType.Int);

        row.VarName = aFlag;

        row.VarSource = "Pack";
        _out.WriteLine($"as the pack's: '{row.VarKindNote}'");
        Assert.Equal("number", row.VarKindNote);

        row.VarSource = "Vanilla";
        _out.WriteLine($"as the game's: '{row.VarKindNote}'");
        Assert.Equal("yes/no", row.VarKindNote);
        Assert.True(row.VarValueIsBool);
    }

    [Fact]
    public void TheNoteAndTheTickBoxAgreeOnAnActionRow()
    {
        // Same two answers, same catalogue, the other kind of row: an action
        // WRITES the value, which is the side where a mistyped "True" is
        // stored rather than merely failing to match.
        var vm = new MainViewModel();
        Declare(vm, "flag", PackVariableType.Bool);

        var row = new NodeActionViewModel(
            new NodeActionDef { Type = NodeActionTypes.SetVariable }, _ => { });

        row.VarSource = "Pack";
        row.VarName = "flag";
        _out.WriteLine($"pack flag: '{row.VarKindNote}', tick box {row.VarValueIsBool}");
        Assert.Equal("yes/no", row.VarKindNote);
        Assert.True(row.VarValueIsBool);

        row.VarSource = "Vanilla";
        row.VarName = VanillaGameVariables.All.First(v => v.Type == "Double").Name;
        _out.WriteLine($"game number: '{row.VarKindNote}', tick box {row.VarValueIsBool}");
        Assert.Equal("number", row.VarKindNote);
        Assert.False(row.VarValueIsBool);
    }

    [Fact]
    public void AVariableNamedByAParamRowSaysItsKindToo()
    {
        // The other place a variable is named: the list actions, the dice
        // actions and the several conditions that take a variable as one of
        // their parameters rather than as the whole row. Same note, same words.
        var vm = new MainViewModel();
        Declare(vm, "picks", PackVariableType.List);
        Declare(vm, "flag", PackVariableType.Bool);

        var action = new NodeActionViewModel(
            new NodeActionDef { Type = NodeActionTypes.AddToList }, _ => { });

        var row = action.ParamRows.FirstOrDefault(
            r => r.Schema.Type == ParamType.ListVarRef);
        Assert.True(row != null, "the list action no longer names a variable by parameter");

        row!.Value = "picks";
        row.RefreshBooleanDetection();
        _out.WriteLine($"picks: '{row.VarKindNote}'");
        Assert.Equal("list", row.VarKindNote);

        row.Value = "flag";
        row.RefreshBooleanDetection();
        _out.WriteLine($"flag: '{row.VarKindNote}'");
        Assert.Equal("yes/no", row.VarKindNote);

        row.Value = "not-a-variable";
        row.RefreshBooleanDetection();
        Assert.Equal("", row.VarKindNote);
    }

    [Fact]
    public void NothingIsClaimedBeforeAPackIsLoaded()
    {
        // The control for all of it: with no lookup installed the row says
        // nothing and leaves the text box, rather than guessing.
        var was = NodeConditionViewModel.VariableKindLookup;
        try
        {
            NodeConditionViewModel.VariableKindLookup = null;

            var row = new NodeConditionViewModel(
                new NodeConditionDef { Type = NodeConditionTypes.VariableCompare }, _ => { });
            row.VarName = "flag";

            Assert.Equal("", row.VarKindNote);
            Assert.False(row.VarValueIsBool);
            Assert.True(row.VarValueIsText);
        }
        finally { NodeConditionViewModel.VariableKindLookup = was; }
    }
}
