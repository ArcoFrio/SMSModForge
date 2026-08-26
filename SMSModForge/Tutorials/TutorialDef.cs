using System;
using System.Collections.Generic;

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
    public string Title { get; init; } = "";

    /// <summary>What to do, or what is being explained.</summary>
    public string Body { get; init; } = "";

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
    /// screen yet simply contributes no hole.
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

    /// <summary>Optional hint shown when a Do step has been sitting unsatisfied
    /// for a while — the nudge before giving up and using Exit.</summary>
    public string Hint { get; init; } = "";
}

/// <summary>One tutorial: a title, a sense of what it costs, and its steps.</summary>
public sealed class TutorialDef
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";

    /// <summary>One line on the button, saying what the author will end up with.</summary>
    public string Summary { get; init; } = "";

    /// <summary>Rough ordering hint shown beside the title (1 = gentlest).</summary>
    public int Level { get; init; } = 1;

    public IReadOnlyList<TutorialStep> Steps { get; init; } = Array.Empty<TutorialStep>();
}
