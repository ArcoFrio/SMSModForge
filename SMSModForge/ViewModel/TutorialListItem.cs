namespace SMSModForge.ViewModel;

/// <summary>
/// A tutorial as the ModForge tab lists it: the definition plus whether this
/// author has finished it. Completion is an editor preference rather than
/// anything in the pack, so it is joined on here rather than stored on the
/// tutorial itself.
/// </summary>
public sealed record TutorialListItem(Tutorials.TutorialDef Def, bool IsComplete)
{
    public string Title => Def.Title;
    public string Summary => Def.Summary;

    /// <summary>Label on the button: finishing once makes it a revisit.</summary>
    public string ActionLabel => IsComplete ? "Again" : "Start";

    /// <summary>Tick shown beside a finished one.</summary>
    public string Mark => IsComplete ? "✓" : "";
}
