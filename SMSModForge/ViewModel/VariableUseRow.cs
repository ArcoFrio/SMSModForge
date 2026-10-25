using SMSModForge.Localization;
using SMSModForge.Services;

namespace SMSModForge.ViewModel;

/// <summary>One place that uses a variable, as the Variables tab lists it:
/// how ("Sets", "Reads", "Shows"), where, and whether the editor can go there.</summary>
public sealed class VariableUseRow
{
    public VariableUseRow(VariableRenamer.Use use) => Use = use;

    public VariableRenamer.Use Use { get; }

    public string How => Use.Kind switch
    {
        VariableRenamer.UseKind.Sets => Loc.T("variables.uses.sets"),
        VariableRenamer.UseKind.Reads => Loc.T("variables.uses.reads"),
        _ => Loc.T("variables.uses.shows"),
    };

    public string HowTip => Use.Kind switch
    {
        VariableRenamer.UseKind.Sets => Loc.T("variables.uses.sets.tip"),
        VariableRenamer.UseKind.Reads => Loc.T("variables.uses.reads.tip"),
        _ => Loc.T("variables.uses.shows.tip"),
    };

    public string Where => Use.Where;

    /// <summary>Somewhere that is one record - not "elsewhere in the pack".</summary>
    public bool CanGo => Use.Path.Length > 0;
}
