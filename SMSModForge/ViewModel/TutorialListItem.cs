namespace SMSModForge.ViewModel;

/// <summary>
/// A tutorial as the ModForge tab lists it: the definition plus whether this
/// author has finished it. Completion is an editor preference rather than
/// anything in the pack, so it is joined on here rather than stored on the
/// tutorial itself.
/// </summary>
public sealed record TutorialListItem(Tutorials.TutorialDef Def, bool IsComplete)
{
    /// <summary>Finished, but an older version of it. Worth saying out loud:
    /// what this author learned may not be what the tutorial says now, and
    /// they are the last people who would think to check.</summary>
    public bool IsOutdated => Services.EditorPrefs.IsTutorialOutdated(Def.Id, Def.Revision);

    public string Title => Def.Title;
    public string Summary => Def.Summary;

    /// <summary>Heading this sits under in the list.</summary>
    public string Group => Def.Group;

    /// <summary>Where it sits on the ladder, shown so an author can tell a
    /// first tutorial from a later one at a glance.</summary>
    public int Level => Def.Level;

    /// <summary>The level as a badge, or nothing at all for a tutorial that is
    /// not on the ladder — a number there would imply an order it has no part
    /// in.</summary>
    public string LevelLabel => Def.IsOnLadder ? Def.Level.ToString() : "";

    /// <summary>Label on the button: finishing once makes it a revisit.</summary>
    public string ActionLabel => IsOutdated ? "What changed" : IsComplete ? "Again" : "Start";

    /// <summary>Tick shown beside a finished one.</summary>
    // An updated tutorial is not ticked: leaving the tick on says "you know
    // this" about a version that no longer exists.
    public string Mark => IsOutdated ? "!" : IsComplete ? "✓" : "";

    /// <summary>Shown beside an updated tutorial, so the badge is not a
    /// mystery.</summary>
    public string UpdatedNote => IsOutdated ? "Updated since you took it" : "";
}
