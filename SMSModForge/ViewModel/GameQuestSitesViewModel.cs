using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using Refs = SMSModForge.Model.VanillaQuestReferences;
using SMSModForge.Localization;

namespace SMSModForge.ViewModel;

/// <summary>
/// A quest entry that keeps the pack's conditions for the game's places that
/// start or reset its quest.
/// </summary>
internal interface ISiteConditionsOwner
{
    /// <summary>The pack's conditions for a place: the entry's own list when
    /// it has one, or a new list that joins the entry once it holds
    /// anything.</summary>
    SiteConditionsDef ConditionsFor(SiteConditionsDef place);

    /// <summary>A place's list changed: keep it while it holds anything.</summary>
    void SiteConditionsChanged(SiteConditionsDef place);
}

/// <summary>
/// One place the game does something to one of its quests or tasks: what it
/// does, where, at which moment, and what has to be true for it to happen - in
/// the editor's own condition rows, the way a conversation shows what the game
/// checks before playing it.
/// <para/>
/// A place that starts or resets the quest can be changed: any of the game's
/// conditions around it taken out (and put back), and conditions of the
/// pack's that the quest step waits for. Any other place is shown as it is,
/// with whatever the pack has taken out of the same lists elsewhere.
/// </summary>
public sealed class GameQuestSiteViewModel : ObservableObject
{
    public GameQuestSiteViewModel(Refs.Site site) : this(site, null, null) { }

    internal GameQuestSiteViewModel(Refs.Site site, string? does, ISiteConditionsOwner? owner)
    {
        Site = site;
        Does = does;
        IsEditable = does != null && owner != null;

        if (IsEditable)
        {
            _owner = owner;
            _place = owner!.ConditionsFor(PlaceRules.NewPlace(site, does!));
            // Asked over and over, with everything else left at the place - a
            // key press or a place works here like anywhere else that is
            // checked continuously.
            ExtraConditions = new ConditionListViewModel(_place.Conditions, ConditionContext.Polled);
            ExtraConditions.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ConditionListViewModel.Count)) return;
                owner.SiteConditionsChanged(_place);
                RaiseChanged();
            };
        }
        ConditionGroups = BuildGroups(site, IsEditable);
        ResetCommand = new RelayCommand(Reset, () => IsEditable && IsChanged);

        var untranslated = new List<string>();
        Reached = Locked(ReachedConditions(site, untranslated));
        Plays = site.IsDialogue
            ? Locked(VanillaDialogueSeed.AnyOf(site.Plays, untranslated))
            : Array.Empty<NodeConditionViewModel>();
        Untranslated = untranslated.Count == 0
            ? ""
            : Loc.P("quests.site.untranslated", untranslated.Distinct().Count(),
                    "conditions", string.Join("; ", untranslated.Distinct()));
    }

    public Refs.Site Site { get; }

    /// <summary><see cref="Shared.GameConditionEdits.Starts"/> or
    /// <see cref="Shared.GameConditionEdits.Resets"/> for a place that can be
    /// changed; null for one only shown.</summary>
    public string? Does { get; }

    private readonly SiteConditionsDef? _place;
    private readonly ISiteConditionsOwner? _owner;

    // -- What can be changed ------------------------------------------

    /// <summary>Whether this place's conditions can be changed here.</summary>
    public bool IsEditable { get; }

    /// <summary>The game's conditions around the step, by where each lives:
    /// the lines on the way, the line itself, what plays the conversation, or
    /// the script.</summary>
    public IReadOnlyList<GameConditionGroupViewModel> ConditionGroups { get; }

    public bool HasConditionGroups => ConditionGroups.Count > 0;

    /// <summary>The pack's own conditions the quest step waits for, on a place
    /// that can be changed.</summary>
    public ConditionListViewModel? ExtraConditions { get; }

    public string ExtraNote =>
        Loc.T("quests.site.extraNote");

    /// <summary>
    /// What the pack's version of this place does, once it changes anything
    /// about it: everything left above becomes the pack's own rule for
    /// starting the quest, and the game no longer has to get here at all.
    /// </summary>
    public string RuleNote
    {
        get
        {
            if (!IsEditable || !IsChanged) return "";
            bool ahead = Site.IsDialogue ? Site.Plays.Any(p => p.Ahead.Count > 0) : Site.Ahead.Count > 0;
            return Loc.T(Does == Shared.GameConditionEdits.Resets ? "quests.site.rule.resets" : "quests.site.rule.starts")
                   + (ahead ? " " + Loc.T("quests.site.rule.ahead") : "");
        }
    }

    public bool HasRuleNote => RuleNote.Length > 0;

    /// <summary>Whether the pack changes anything about this place.</summary>
    public bool IsChanged => ConditionGroups.Any(g => g.RemovedCount > 0) || (ExtraConditions?.Count ?? 0) > 0
                             || (_place?.RoomsOut.Count ?? 0) > 0;

    /// <summary>What it changes, for the marker's tooltip.</summary>
    public string ChangedText
    {
        get
        {
            int removed = ConditionGroups.Sum(g => g.RemovedCount);
            int added = ExtraConditions?.Count ?? 0;
            var parts = new List<string>();
            if (removed > 0) parts.Add(Loc.P("quests.site.changed.removed", removed));
            if ((_place?.RoomsOut.Count ?? 0) > 0) parts.Add(Loc.T("quests.site.changed.anywhere"));
            if (added > 0) parts.Add(Loc.P("quests.site.changed.added", added));
            return parts.Count == 0 ? "" : Loc.F("quests.site.changed", "parts", Loc.JoinList(parts));
        }
    }

    /// <summary>Put the game's conditions back and drop the pack's.</summary>
    public RelayCommand ResetCommand { get; }

    private void Reset()
    {
        if (_place != null && _place.RoomsOut.Count > 0)
        {
            _place.RoomsOut.Clear();
            _owner?.SiteConditionsChanged(_place);
        }
        foreach (var group in ConditionGroups) group.RestoreAll();
        while (ExtraConditions != null && ExtraConditions.Count > 0)
            ExtraConditions.Remove(ExtraConditions.Items[0]);
        Refresh();
    }

    /// <summary>Something about the game's conditions changed - here or on
    /// another tab.</summary>
    internal void Refresh()
    {
        foreach (var group in ConditionGroups) group.Refresh();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(ChangedText));
        OnPropertyChanged(nameof(RuleNote));
        OnPropertyChanged(nameof(HasRuleNote));
        ResetCommand?.Raise();
    }

    /// <summary>Take the room this script sits on out of the place, or put it
    /// back. Only where the place can be changed.</summary>
    private Action<bool>? RoomOut(string by)
    {
        if (!IsEditable || _place == null) return null;
        return out_ =>
        {
            if (out_) { if (!_place.RoomsOut.Contains(by)) _place.RoomsOut.Add(by); }
            else _place.RoomsOut.Remove(by);
            _owner?.SiteConditionsChanged(_place);
            RaiseChanged();
        };
    }

    private Func<bool>? RoomIsOut(string by)
        => !IsEditable || _place == null ? null : () => _place.RoomsOut.Contains(by);

    private IReadOnlyList<GameConditionGroupViewModel> BuildGroups(Refs.Site site, bool canEdit)
    {
        var groups = new List<GameConditionGroupViewModel>();
        if (site.IsDialogue)
        {
            if (string.IsNullOrEmpty(site.Dialogue) || site.Node == null) return groups;
            foreach (var lead in site.After)
            {
                if (lead.When.Count == 0) continue;
                var group = GameConditionGroups.ForLine(Loc.F("quests.site.lineOnTheWay", "line", Line(lead.Actor, lead.Line)),
                                                        site.Dialogue!, lead.Node, canEdit);
                if (group != null) groups.Add(group);
            }
            var own = GameConditionGroups.ForLine(Loc.T("quests.site.thisLine"), site.Dialogue!, site.Node.Value, canEdit);
            if (own != null) groups.Add(own);

            var lines = groups.Where(g => g.Rows.Count > 0).ToList();
            foreach (var play in site.Plays)
            {
                var group = GameConditionGroups.ForScript(
                    string.IsNullOrEmpty(play.Event)
                        ? Loc.F("quests.site.playsFrom", "script", ScriptName(play.By, play.Script))
                        : Loc.F("quests.site.playsFromSetOff", "script", ScriptName(play.By, play.Script), "event", EventWords(play.Event!)),
                    play.By, play.Script, play.When, play.Gates, canEdit, showLocation: true, play.Ahead,
                    RoomIsOut(play.By), RoomOut(play.By));
                // Kept with nothing in it: where the conversation is played
                // from is worth knowing even when nothing is checked there.
                lines.Add(group.Rows.Count > 0 || group.HasNote
                    ? group
                    : new GameConditionGroupViewModel(group.Title, Loc.T("quests.site.checksNothing"),
                                                      group.Rows));
            }
            return lines;
        }

        groups.Add(GameConditionGroups.ForScript(
            Loc.T("quests.site.scriptConditions"), site.By ?? "", site.Script ?? "", site.When, site.Gates, canEdit,
            showLocation: true, site.Ahead, RoomIsOut(site.By ?? ""), RoomOut(site.By ?? "")));
        return groups.Where(g => g.Rows.Count > 0 || g.HasNote).ToList();
    }

    /// <summary>
    /// A script, by its object and type - and, for a room's script, the room:
    /// it only runs while the player is there. Said here rather than as a
    /// condition row, because it is not one of the game's conditions but where
    /// its script lives.
    /// </summary>
    private static string ScriptName(string? by, string? script)
    {
        string name = string.IsNullOrEmpty(by) ? Loc.T("quests.site.unnamedObject") : by!;
        if (!string.IsNullOrEmpty(script)) name = Loc.F("quests.site.objectScript", "object", name, "script", script);
        return VanillaDialogueSeed.PlaceOf(by) is { } place
            ? Loc.F("quests.site.inPlace", "script", name, "place", place.DisplayName)
            : name;
    }

    /// <summary>What the step does, in a few words.</summary>
    public string What
    {
        get
        {
            string type = Site.Step.Type;
            switch (type)
            {
                case "InstructionQuestsActivate": return Loc.T("quests.site.step.starts");
                case "InstructionQuestsDeactivate": return Loc.T("quests.site.step.resets");
                case "InstructionQuestsTaskComplete": return Loc.T("quests.site.step.completes");
                case "InstructionQuestsTaskFail": return Loc.T("quests.site.step.fails");
                case "InstructionQuestsTaskAbandon": return Loc.T("quests.site.step.abandons");
                case "InstructionQuestTaskValue":
                    return AddsTo(Site.Step.Value) is { } added
                        ? Loc.F("quests.site.step.addsToCount", "amount", added)
                        : Loc.F("quests.site.step.changesCount", "step", Site.Step.Title ?? type);
            }
            return Site.Step.Title is { Length: > 0 } title ? title : type;
        }
    }

    /// <summary>
    /// How much a counter step adds, when it adds a fixed number. Only the
    /// shape the game uses is read - an operation of "Add" and a plain value -
    /// and anything else is left to the step's own words rather than guessed.
    /// </summary>
    private static string? AddsTo(JToken? value)
    {
        if (value is not JObject change) return null;
        if (!string.Equals((string?)change["m_Operation"], "Add", StringComparison.Ordinal)) return null;
        if (change["m_Value"] is not JObject amount || (string?)amount["kind"] != "value") return null;
        var number = amount["value"];
        if (number == null || (number.Type != JTokenType.Integer && number.Type != JTokenType.Float)) return null;
        return ((double)number).ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Where it lives.</summary>
    public string Where
    {
        get
        {
            if (Site.IsDialogue) return Loc.F("quests.site.inConversation", "conversation", Site.Dialogue ?? Loc.T("quests.site.unnamed"));

            string on = string.IsNullOrEmpty(Site.By) ? Loc.T("quests.site.unnamedObject") : Site.By!;
            string text = string.IsNullOrEmpty(Site.Script)
                ? Loc.F("quests.site.scriptOn", "object", on)
                : Loc.F("quests.site.namedScriptOn", "script", Site.Script, "object", on);
            if (VanillaDialogueSeed.PlaceOf(Site.By) is { } room) text = Loc.F("quests.site.inRoom", "text", text, "room", room.DisplayName);
            if (!string.IsNullOrEmpty(Site.Event)) text = Loc.F("quests.site.setOffBy", "text", text, "event", EventWords(Site.Event!));
            if (Site.AroundDialogue is { } around)
            {
                if (around.After is { } after)
                {
                    string name = Named(after);
                    text = after.Waits switch
                    {
                        true => Loc.F("quests.site.afterFinished", "text", text, "conversation", name),
                        false => Loc.F("quests.site.afterStarts", "text", text, "conversation", name),
                        _ => Loc.F("quests.site.afterStep", "text", text, "conversation", name),
                    };
                }
                if (around.Before is { } before)
                    text = Loc.F(around.After != null ? "quests.site.andBefore" : "quests.site.before",
                                 "text", text, "conversation", Named(before));
            }
            return text;
        }
    }

    private static string Named(Refs.Play play)
        => string.IsNullOrEmpty(play.Dialogue) ? Loc.T("quests.site.pickedWhileRunning") : play.Dialogue!;

    /// <summary>What an author reads about the object being there at all.</summary>
    public string Standing
    {
        get
        {
            var parts = new List<string>();
            if (Site.Prefab) parts.Add(Loc.T("quests.site.onPrefab"));
            else if (Site.Active == false) parts.Add(Loc.T("quests.site.switchedOff"));
            if (Site.Branches.Count > 0)
                parts.Add(Loc.F("quests.site.insideBranch", "branch", string.Join(" › ", Site.Branches.Select(b => "“" + b + "”"))));
            return string.Join(" ", parts);
        }
    }

    public bool HasStanding => Standing.Length > 0;

    /// <summary>
    /// The moment within the conversation, and the line it hangs on. Empty
    /// for a script, whose moment is <see cref="Where"/>.
    /// </summary>
    public string When
    {
        get
        {
            if (!Site.IsDialogue) return "";
            return Loc.F(Site.Moment == "onFinish" ? "quests.site.onceLineDone" : "quests.site.asLineAppears",
                         "line", Line(Site.Actor, Site.Line));
        }
    }

    public bool HasWhen => When.Length > 0;

    /// <summary>The lines the player passes through to get there, top first.</summary>
    public string Path => Site.After.Count == 0
        ? ""
        : Loc.F("quests.site.reachedThrough", "lines", string.Join("  →  ", Site.After.Select(l => Line(l.Actor, l.Line))));

    public bool HasPath => Path.Length > 0;

    /// <summary>What has to be true where the step sits: the conditions on
    /// every line on the way and on the line itself, or on the script.</summary>
    public IReadOnlyList<NodeConditionViewModel> Reached { get; }

    public bool HasReached => Reached.Count > 0;

    public string ReachedLabel => Site.IsDialogue ? Loc.T("quests.site.onlyIfAlongTheWay") : Loc.T("quests.site.onlyIf");

    /// <summary>What the conversation needs to be played at all. Several
    /// places read as an Any-of.</summary>
    public IReadOnlyList<NodeConditionViewModel> Plays { get; }

    public bool HasPlays => Plays.Count > 0;

    /// <summary>
    /// Said when nothing is known of what plays the conversation - the
    /// difference between "nothing gates it" and "nothing names it".
    /// </summary>
    public string PlaysNote
    {
        get
        {
            if (!Site.IsDialogue) return "";
            if (Site.Plays.Count == 0)
                return Loc.T("quests.site.nothingPlaysIt");
            if (Plays.Count == 0 && Site.Plays.All(p => p.Ahead.Count == 0))
                return Loc.T("quests.site.playsUnchecked");
            return "";
        }
    }

    public bool HasPlaysNote => PlaysNote.Length > 0;

    /// <summary>Conditions that have no equivalent here, by the game's words,
    /// so a site never looks less guarded than it is.</summary>
    public string Untranslated { get; }

    public bool HasUntranslated => Untranslated.Length > 0;

    /// <summary>The conversation to open from this row, when there is one -
    /// for a script, the one the step follows, or else the one it leads
    /// into.</summary>
    public string? Conversation => Site.IsDialogue
        ? Site.Dialogue
        : Site.AroundDialogue?.After?.Dialogue ?? Site.AroundDialogue?.Before?.Dialogue;

    public bool CanOpenConversation => VanillaDialogueCatalog.Find(Conversation) != null;

    private static List<NodeConditionDef> ReachedConditions(Refs.Site site, List<string> untranslated)
    {
        var all = new List<NodeConditionDef>();
        foreach (var lead in site.After) Translate(lead.When, all, untranslated);

        if (site.IsDialogue)
        {
            Translate(site.When, all, untranslated);
            return all;
        }

        // A script: where it is and what it checks, read the same way as a
        // conversation's start - a room talk only runs in its room.
        var start = new VanillaDialogueCatalog.Start { By = site.By ?? "", Script = site.Script ?? "", When = site.When };
        all.AddRange(VanillaDialogueSeed.GateOf(start, untranslated));
        return all;
    }

    private static void Translate(IEnumerable<VanillaDialogueCatalog.Step>? steps, List<NodeConditionDef> into,
                                  List<string> untranslated)
    {
        foreach (var step in steps ?? Array.Empty<VanillaDialogueCatalog.Step>())
        {
            if (step == null) continue;
            var one = VanillaDialogueConditions.Translate(step);
            if (one != null) into.Add(one);
            else untranslated.Add(step.Describe());
        }
    }

    private static IReadOnlyList<NodeConditionViewModel> Locked(IEnumerable<NodeConditionDef> conditions)
        => conditions.Select(c => new NodeConditionViewModel(c, isLocked: true, lockedHeader: "")).ToList();

    private static string Line(string? actor, string? line)
    {
        string said = "“" + (string.IsNullOrWhiteSpace(line) ? Loc.T("quests.noText") : line!.Trim()) + "”";
        string who = VanillaDialogueCatalog.SpokenActorName(actor);
        return who.Length > 0 ? who + ": " + said : said;
    }

    /// <summary>Game Creator's trigger event, in words: "EventOnEnable" reads
    /// "On Enable".</summary>
    internal static string EventWords(string eventType)
    {
        string name = eventType;
        int dot = name.LastIndexOf('.');
        if (dot >= 0) name = name.Substring(dot + 1);
        if (name.StartsWith("Event", StringComparison.Ordinal) && name.Length > 5) name = name.Substring(5);

        var spaced = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) spaced.Append(' ');
            spaced.Append(name[i]);
        }
        return "“" + spaced + "”";
    }
}

/// <summary>A heading and the places under it.</summary>
public sealed class GameQuestSiteGroup
{
    public GameQuestSiteGroup(string heading, IEnumerable<Refs.Site> sites)
        : this(heading, sites, null, null) { }

    internal GameQuestSiteGroup(string heading, IEnumerable<Refs.Site> sites, string? does,
                                ISiteConditionsOwner? owner)
    {
        Heading = heading;
        Sites = sites.Select(s => new GameQuestSiteViewModel(s, does, owner)).ToList();
    }

    public string Heading { get; }

    public IReadOnlyList<GameQuestSiteViewModel> Sites { get; }

    public string Title => Heading + " (" + Sites.Count + ")";

    internal void Refresh()
    {
        foreach (var site in Sites) site.Refresh();
    }
}

/// <summary>
/// What the game does with a quest or a task, grouped, with the notes that say
/// how much of the game was read to find it.
/// </summary>
public static class GameQuestSites
{
    /// <summary>
    /// Said beside every list while only the conversations have been read: a
    /// list with the scene half missing reads exactly like a whole one.
    /// </summary>
    public static string CoverageNote
        => !Refs.IsAvailable
            ? Loc.T("quests.site.noRecord")
            : Refs.IsComplete
                ? ""
                : Loc.T("quests.site.partialRecord");

    public static IReadOnlyList<GameQuestSiteGroup> ForQuest(string? quest) => ForQuest(quest, null);

    /// <summary>With an <paramref name="owner"/>, the places that start or
    /// reset the quest can be changed.</summary>
    internal static IReadOnlyList<GameQuestSiteGroup> ForQuest(string? quest, ISiteConditionsOwner? owner)
    {
        var sites = Refs.For(quest);
        var groups = new List<GameQuestSiteGroup>();
        if (sites == null) return groups;
        Add(groups, Loc.T("quests.site.group.startedBy"), sites.Starts, Shared.GameConditionEdits.Starts, owner);
        Add(groups, Loc.T("quests.site.group.resetBy"), sites.Resets, Shared.GameConditionEdits.Resets, owner);
        Add(groups, Loc.T("quests.site.group.askedBy"), sites.Checks);
        Add(groups, Loc.T("quests.site.group.namedBy"), sites.Other);
        return groups;
    }

    public static IReadOnlyList<GameQuestSiteGroup> ForTask(string? quest, string? taskId)
    {
        var sites = Refs.For(quest, taskId);
        var groups = new List<GameQuestSiteGroup>();
        if (sites == null) return groups;
        Add(groups, Loc.T("quests.site.group.completedBy"), sites.Completes);
        Add(groups, Loc.T("quests.site.group.countedBy"), sites.Counts);
        Add(groups, Loc.T("quests.site.group.failedBy"), sites.Fails);
        Add(groups, Loc.T("quests.site.group.abandonedBy"), sites.Abandons);
        Add(groups, Loc.T("quests.site.group.askedBy"), sites.Checks);
        Add(groups, Loc.T("quests.site.group.namedBy"), sites.Other);
        return groups;
    }

    /// <summary>What the quest panel says when nothing is known to start it.</summary>
    public static string QuestNote(string? quest)
    {
        if (!Refs.IsAvailable || string.IsNullOrEmpty(quest)) return "";
        var sites = Refs.For(quest);
        return sites == null || sites.Starts.Count == 0
            ? Loc.T("quests.site.nothingStarts")
            : "";
    }

    /// <summary>
    /// What the task panel says about how the game moves the task along, and
    /// when nothing is known to.
    /// </summary>
    /// <param name="throughSubtasks">It has subtasks and they finish it -
    /// in order, in any order or with any one. A task completed by an action
    /// is not finished by its subtasks.</param>
    public static string TaskNote(string? quest, string? taskId, bool throughSubtasks, bool counts)
    {
        if (!Refs.IsAvailable || string.IsNullOrEmpty(quest)) return "";
        var sites = Refs.For(quest, taskId);
        bool any = sites != null
                   && (sites.Completes.Count + sites.Counts.Count + sites.Fails.Count + sites.Abandons.Count) > 0;
        bool completed = sites != null && (sites.Completes.Count > 0 || (counts && sites.Counts.Count > 0));

        var parts = new List<string>();
        if (!completed)
            parts.Add(throughSubtasks
                ? Loc.T("quests.site.throughSubtasks")
                : Loc.T("quests.site.nothingCompletes"));
        if (any)
        {
            parts.Add(Loc.T("quests.site.onlyInProgress"));
            if (counts) parts.Add(Loc.T("quests.site.countCompletes"));
        }
        return string.Join(" ", parts);
    }

    private static void Add(List<GameQuestSiteGroup> groups, string heading, List<Refs.Site>? sites,
                            string? does = null, ISiteConditionsOwner? owner = null)
    {
        if (sites == null || sites.Count == 0) return;
        groups.Add(new GameQuestSiteGroup(heading, sites, owner == null ? null : does, owner));
    }
}
