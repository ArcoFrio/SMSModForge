using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Localization;

namespace SMSModForge.Tutorials;

/// <summary>How a step lets the author move on.</summary>
public enum StepKind
{
    /// <summary>Explains something. Next advances.</summary>
    Read,

    /// <summary>Asks for something specific and waits until it has actually
    /// been done — the step advances by itself when its check passes.</summary>
    Do,

    /// <summary>Sets an open goal and accepts any reasonable answer. Advances
    /// on a deliberately loose check, so what gets made is the author's.</summary>
    Free,
}

/// <summary>One step of a tutorial.</summary>
public sealed class TutorialStep
{
    /// <summary>Heading on the callout. Short — it is read at a glance.</summary>
    public string Title { get => Loc.Optional(TitleKey); init => TitleKey = value; }

    /// <summary>The key <see cref="Title"/> is read from in the English file.</summary>
    public string TitleKey { get; private set; } = "";

    /// <summary>What to do, or what is being explained.</summary>
    public string Body { get => Loc.Optional(BodyKey); init => BodyKey = value; }

    /// <summary>The key <see cref="Body"/> is read from in the English file.</summary>
    public string BodyKey { get; private set; } = "";

    /// <summary>See <see cref="StepKind"/>.</summary>
    public StepKind Kind { get; init; } = StepKind.Read;

    /// <summary>Which control to spotlight, matched against
    /// <c>View.TutorialAnchor.Id</c>. Empty dims the whole window with no
    /// hole, which suits an opening or closing step.
    /// <para/>
    /// This one is the primary: it gets the ring, the arrival flash, and the
    /// scroll-into-view. Anything else the step needs goes in
    /// <see cref="AlsoAllow"/>.</summary>
    public string Anchor { get; init; } = "";

    /// <summary>
    /// Further controls the step needs reachable, each lit with its own hole.
    /// <para/>
    /// The dim swallows clicks outside the lit area, so the holes decide what
    /// an author is ALLOWED to touch, not merely what is emphasised. Any step
    /// whose instruction spans more than one control has to say so here or it
    /// cannot be completed — the shape "click + Something, then fill in what
    /// it created" needs the editor pane as well as the button.
    /// <para/>
    /// Missing ids are skipped rather than throwing: a pane that is not on
    /// screen yet contributes no hole until it appears - the overlay keeps
    /// looking, for these and for <see cref="Anchor"/>.
    /// </summary>
    public string[] AlsoAllow { get; init; } = Array.Empty<string>();

    /// <summary>Tab to switch to before looking for <see cref="Anchor"/>.
    /// Negative leaves the current tab alone.</summary>
    public int Tab { get; init; } = -1;

    /// <summary>
    /// Whether the step is satisfied. Null means it never satisfies itself,
    /// which is right for <see cref="StepKind.Read"/>.
    /// <para/>
    /// Written against the view-model rather than against the UI on purpose: a
    /// check should ask whether the pack actually changed, not whether a
    /// particular button was clicked. That way an author who gets to the same
    /// place another way is not told they are wrong.
    /// </summary>
    public Func<ViewModel.MainViewModel, TutorialScratch, bool>? IsDone { get; init; }

    /// <summary>
    /// Run once as the step opens. For a step that asks for something to be
    /// ADDED, this is where the "before" count is taken — without it a check
    /// like "a character exists" is already true in any pack that has one, and
    /// the step passes before the author has done anything.
    /// </summary>
    public Action<ViewModel.MainViewModel, TutorialScratch>? OnEnter { get; init; }

    /// <summary>
    /// What to open or pick for the reader once the step's tab is on screen (1.10.0): the line a step talks about,
    /// the light in the place preview. Made in <see cref="OnEnter"/>, a choice was taken back as the tab came up and
    /// its lists were bound - the review of every step's picture found steps about a line's actions arriving with
    /// no line chosen and nothing to see.
    /// </summary>
    public Action<ViewModel.MainViewModel>? OnShown { get; init; }

    /// <summary>Optional hint shown when a Do step has been sitting unsatisfied
    /// for a while — the nudge before giving up and using Exit.</summary>
    public string Hint { get => Loc.Optional(HintKey); init => HintKey = value; }

    /// <summary>The key <see cref="Hint"/> is read from in the English file.</summary>
    public string HintKey { get; private set; } = "";

    /// <summary>The whole explanation, under a fold-out beneath the short <see cref="Body"/> (1.10.0: "short
    /// explanations, the full one in a fold-out").</summary>
    public string More { get => Loc.Optional(MoreKey); init => MoreKey = value; }

    public string MoreKey { get; private set; } = "";

    /// <summary>
    /// The step done for the reader ("Do it for me", 1.10.0): what an author would do, through the editor's own
    /// commands, to the state the step's check asks about. The tests walk the tutorials through these very
    /// actions, so a step's solution and its check are proved against each other.
    /// </summary>
    public Action<ViewModel.MainViewModel, TutorialScratch>? Solve { get; init; }

    /// <summary>
    /// The step that makes the story's pack (tutorial 1's New modpack). Left out when the story so far is brought
    /// in for a later tutorial (<see cref="StoryPack.BringIn"/>), which opens or makes the pack itself.
    /// </summary>
    public bool MakesThePack { get; init; }
}

/// <summary>One tutorial: a title, a sense of what it costs, and its steps.</summary>
public sealed class TutorialDef
{
    public string Id { get; init; } = "";
    public string Title { get => Loc.Optional(TitleKey); init => TitleKey = value; }

    /// <summary>The key <see cref="Title"/> is read from in the English file.</summary>
    public string TitleKey { get; private set; } = "";

    /// <summary>One line on the button, saying what the author will end up with.</summary>
    public string Summary { get => Loc.Optional(SummaryKey); init => SummaryKey = value; }

    /// <summary>The key <see cref="Summary"/> is read from in the English file.</summary>
    public string SummaryKey { get; private set; } = "";

    /// <summary>
    /// Where this sits in the progression: 1 is the gentlest, and the catalog
    /// is kept in this order because it is the order the list shows.
    /// <para/>
    /// Zero means the tutorial is not on the ladder at all — the diagnostic
    /// walkthrough, which exists to prove the overlay still works and is not
    /// something an author is ever meant to work through. Those sort last and
    /// are left out of the progression checks.
    /// </summary>
    public int Level { get; init; } = 1;

    /// <summary>Whether this is part of the author-facing progression.</summary>
    public bool IsOnLadder => Level > 0;

    /// <summary>
    /// Bumped whenever this tutorial changes enough that somebody who has
    /// already finished it should be told.
    /// <para/>
    /// Completion is remembered per id, so a tutorial that gains steps or has
    /// its instructions corrected stays ticked for everyone who took the old
    /// one — and the people most likely to be misled by a stale memory of it
    /// are exactly those who worked through the version that was wrong. The
    /// list shows an updated tutorial as unfinished again, with a note saying
    /// why, rather than silently.
    /// <para/>
    /// Raise it for a change worth re-reading. Leave it alone for a typo.
    /// </summary>
    public int Revision { get; init; } = 1;

    /// <summary>
    /// Which run of tutorials this belongs to, shown as a heading above them.
    /// <para/>
    /// Groups follow the tabs, because that is how someone decides what to
    /// learn next: they are working on rooms, so they want the rest of the room
    /// tutorials, not the next thing in a single flat list. Within a group,
    /// <see cref="Level"/> orders them shallowest first.
    /// <para/>
    /// A group's tutorials are kept together in the catalog. Level is still
    /// global and still runs 1..n across the whole ladder, so "where am I" has
    /// one answer rather than one per group.
    /// </summary>
    public string Group { get => Loc.Optional(GroupKey); init => GroupKey = value; }

    /// <summary>The key <see cref="Group"/> is read from in the English file.</summary>
    public string GroupKey { get; private set; } = "";

    /// <summary>
    /// The tutorial this one goes deeper into, by id; empty for one of the first run (1.10.0, the author: "The
    /// sub-tutorials shouldn't be in a separate group, but indented on the base ones"). The list shows it under
    /// that one, indented. Its <see cref="Level"/> still places it after the whole first run - it builds on all of
    /// it - so Continue learning still takes the tutorials in the story's order.
    /// </summary>
    public string ParentId { get; init; } = "";

    public bool IsDeeper => ParentId.Length > 0;

    /// <summary>What it adds to the pack, in a line, beside its summary (the author: "show a summary of what tabs
    /// and changes they involve, so the user understands if they want to check the tutorial or not").</summary>
    public string Makes { get => Loc.Optional(MakesKey); init => MakesKey = value; }

    /// <summary>The key <see cref="Makes"/> is read from in the English file.</summary>
    public string MakesKey { get; private set; } = "";

    /// <summary>The editor's tabs its steps work on, in the order they first come to each (the ModForge tab, where
    /// a tutorial starts and ends, is not one of them). Read from the steps, so it is never out of date.</summary>
    public IReadOnlyList<int> Tabs => Steps.Select(s => s.Tab).Where(t => t > ViewModel.MainTab.ModForge).Distinct().ToList();

    public IReadOnlyList<TutorialStep> Steps { get; init; } = Array.Empty<TutorialStep>();

    /// <summary>
    /// Every tutorial adds to one pack, "Dummy Days" (1.10.0), and each can still be followed on its own. This
    /// says whether what this tutorial makes is in the open pack: a later tutorial whose earlier ones are not
    /// offers to bring them in (<see cref="StoryPack"/>). Null: a tutorial outside the story.
    /// </summary>
    public Func<ViewModel.MainViewModel, bool>? Leaves { get; init; }

    /// <summary>
    /// Where a pack a tutorial makes for the reader goes ("Do it for me" on its first step): a folder of their
    /// Documents, "ModForge Tutorials". The tests put it in a folder of their own - never the author's Documents.
    /// </summary>
    public static Func<string> PracticeFolder { get; set; } = () => Services.TestMode.Active
        ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SMSModForge-tutorials")
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ModForge Tutorials");   // English on purpose: a folder name.
}
