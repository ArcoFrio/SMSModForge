using System.Collections.Generic;
using System.Linq;
using SMSModForge.Localization;

namespace SMSModForge.ViewModel;

/// <summary>A step as a tutorial's card lists it, in one line: "3  Add a place - On the Places tab, click + Place."</summary>
public sealed record StepLine(int Number, string Title, string Gist, bool IsDo)
{
    public string NumberText => Number + "  ";
    public string Dash => Gist.Length > 0 ? " - " : "";
    public string Full => Gist.Length > 0 ? Title + " - " + Gist : Title;
}

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

    /// <summary>Its picture (its own or its group's), for its card on the ModForge tab (1.10.0).</summary>
    public System.Windows.Media.ImageSource? Art => TutorialRunner.HeaderArtFor(Def);

    /// <summary>How long it is, in steps.</summary>
    public string StepsLabel => Loc.P("home.tutorials.steps", Def.Steps.Count);
    public string Summary => Def.Summary;

    /// <summary>What it adds to the pack (1.10.0).</summary>
    public string Makes => Def.Makes;
    public bool HasMakes => Makes.Length > 0;

    /// <summary>The tabs it works on, named as the window names them: "On the Places and Map Buttons tabs".</summary>
    public string TabsLabel
    {
        get
        {
            var names = Def.Tabs.Select(MainTab.Name).Where(n => n.Length > 0).ToList();
            return names.Count == 0 ? "" : Loc.P("home.tutorials.tabs", names.Count, "tabs", Loc.JoinAnd(names));
        }
    }

    /// <summary>Goes deeper into another tutorial, and is listed under it, indented (1.10.0).</summary>
    public bool IsDeeper => Def.IsDeeper;

    /// <summary>Heading this sits under in the list: a deeper one sits with the tutorial it goes deeper into.</summary>
    public string Group => Parent?.Group ?? Def.Group;

    private Tutorials.TutorialDef? Parent
        => Def.IsDeeper ? Tutorials.TutorialCatalog.All.FirstOrDefault(t => t.Id == Def.ParentId) : null;

    /// <summary>Where it sits on the ladder, shown so an author can tell a
    /// first tutorial from a later one at a glance.</summary>
    public int Level => Def.Level;

    /// <summary>The level as a badge, or nothing at all for a tutorial that is
    /// not on the ladder — a number there would imply an order it has no part
    /// in. A deeper one says "Going deeper" instead: its number would sit out of
    /// order under the tutorial it goes with.</summary>
    public string LevelLabel => Def.IsOnLadder && !Def.IsDeeper ? Def.Level.ToString() : "";

    /// <summary>The badge with the space after it, or nothing: a title with no number starts at the margin.</summary>
    public string LevelPrefix => LevelLabel.Length > 0 ? LevelLabel + "  " : "";

    /// <summary>Label on the button: finishing once makes it a revisit. An updated one is taken again from its
    /// first step - it said "What changed", and showed nothing of what had (the author, 2026-11-21).</summary>
    public string ActionLabel => Loc.T(IsOutdated ? "tutorials.takeNewVersion" : IsComplete ? "tutorials.again" : "tutorials.start");

    /// <summary>What the button does, for its tooltip.</summary>
    public string ActionTip => Loc.T(IsOutdated ? "tutorials.takeNewVersion.tip" : IsComplete ? "tutorials.again.tip" : "tutorials.start.tip");

    /// <summary>What its first step says: the more the card shows when the mouse rests on it (1.10.0).</summary>
    public string Intro => Def.Steps.Count > 0 ? Def.Steps[0].Body : "";

    /// <summary>Each step in a line, for the card opened by a rest of the mouse: its title, and the start of what it
    /// says - read from the steps, so it is never out of date. In two columns, each read top to bottom.</summary>
    // The first step's words are the Intro above the list: its line is its title alone.
    public IReadOnlyList<StepLine> StepLines => Def.Steps.Select((s, i) => new StepLine(i + 1, s.Title, i == 0 ? "" : Gist(s.Body), s.Kind != Tutorials.StepKind.Read)).ToList();
    public IReadOnlyList<StepLine> StepLinesLeft => StepLines.Take((StepLines.Count + 1) / 2).ToList();
    public IReadOnlyList<StepLine> StepLinesRight => StepLines.Skip((StepLines.Count + 1) / 2).ToList();

    /// <summary>The first sentence of a step's words, without the colon that leads into a list.</summary>
    internal static string Gist(string body)
    {
        body = (body ?? "").Replace("\n", " ").Trim();
        int end = -1;
        for (int i = 0; i < body.Length - 1; i++)
        {
            if (body[i] is not ('.' or '!' or '?' or ':') || body[i + 1] != ' ') continue;
            // Not the dots of an ellipsis or of the "..." button: "Click the ... beside Front sprite."
            if (body[i] == '.' && i > 0 && body[i - 1] == '.') continue;
            end = i;
            break;
        }
        string first = end >= 0 ? body.Substring(0, end + 1) : body;
        return first.EndsWith(":", System.StringComparison.Ordinal) ? first.TrimEnd(':') + "." : first;
    }

    /// <summary>Tick shown beside a finished one.</summary>
    // An updated tutorial is not ticked: leaving the tick on says "you know
    // this" about a version that no longer exists.
    public string Mark => IsOutdated ? "!" : IsComplete ? "✓" : "";

    /// <summary>Shown beside an updated tutorial, so the badge is not a
    /// mystery.</summary>
    public string UpdatedNote => IsOutdated ? Loc.T("tutorials.updated") : "";
}
