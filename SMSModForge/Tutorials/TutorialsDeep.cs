using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;

namespace SMSModForge.Tutorials;

/// <summary>
/// "Going deeper" (1.10.0): chapters of Dummy Days after the ladder's first pass, each one practice in a part of the
/// editor the first pass only touched. The author asked for them ("sub-tutorials in each category that each go
/// deeper ... A place that I feel was very lacking in explanations was the Implementations, along with more in-depth
/// practice on Actions and Conditions") and chose "same story, more chapters": they add to the same mod, after
/// everything the first pass made - her quest, her portfolio, her hearts - which is why they come last, as their own
/// group, rather than inside the groups whose tabs they work in.
/// <para/>
/// They also finish what the first pass left hanging (the review of every step's picture, 2026-11-21): her
/// confidence now grows, her portfolio opens, the quest's photo task can be completed, and what she has shown you
/// is read back.
/// </summary>
internal static class TutorialsDeep
{
    private const int TabCharacters = MainTab.Characters;
    private const int TabDialogues = MainTab.Dialogues;
    private const int TabPlaces = MainTab.Places;
    private const int TabNpcs = MainTab.Npcs;
    private const int TabScenes = MainTab.Scenes;
    private const int TabVariables = MainTab.Variables;
    private const int TabWallpapers = MainTab.Wallpapers;
    private const int TabIntegration = MainTab.Integration;

    private const string Group = "tutorial.group.deeper";

    // The story's names (English on purpose: names the pack's own conditions, lines and rules use).
    private const string Confidence = TutorialsPart2.Confidence;
    private const string PosesSeen = TutorialsPart2.PosesSeen;
    private const string KeptHerSecret = TutorialsPart2.KeptHerSecret;
    private const string Mood = "Mood";
    // English on purpose: the story pack's words.
    private const string BraverLine = "I feel braver when you're here.";
    // English on purpose: the story pack's words.
    private const string ShyLine = "You've seen me shy. Next time, I'll be bold.";

    // ── Reading the pack ─────────────────────────────────────────────────────────

    private static string P(NodeActionDef a, string key) => a.Params.TryGetValue(key, out var v) ? v ?? "" : "";
    private static string P(NodeConditionDef c, string key) => c.Params.TryGetValue(key, out var v) ? v ?? "" : "";

    private static double Num(string s)
        => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;

    private static IEnumerable<NodeActionDef> ActionsOf(DialogueNodeViewModel n)
        => (n.Model.ActionsOnStart ?? new List<NodeActionDef>()).Concat(n.Model.ActionsOnFinish ?? new List<NodeActionDef>());

    /// <summary>Every condition of a list, inside groups too.</summary>
    private static IEnumerable<NodeConditionDef> Flat(IEnumerable<NodeConditionDef>? list)
    {
        if (list == null) yield break;
        foreach (var c in list)
        {
            yield return c;
            foreach (var inner in Flat(c.Conditions)) yield return inner;
        }
    }

    private static IEnumerable<DialogueNodeViewModel> HerLines(MainViewModel vm)
        => TutorialsPart2.HerSecret(vm)?.Nodes ?? Enumerable.Empty<DialogueNodeViewModel>();

    private static bool Is(string a, string b) => string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A variable compared with a number, the way the step asks ("greater ..." than or equal to).</summary>
    private static bool ComparesUp(NodeConditionDef c, string variable)
        => c.Type == NodeConditionTypes.VariableCompare && Is(P(c, "name"), variable) &&
           P(c, "comparison").StartsWith("greater", StringComparison.OrdinalIgnoreCase) && !double.IsNaN(Num(P(c, "value")));

    private static bool IncrementsConfidence(NodeActionDef a, bool up)
        => a.Type == NodeActionTypes.IncrementVariable && Is(P(a, "name"), Confidence) &&
           Num(P(a, "delta") is { Length: > 0 } d ? d : "1") is var n && !double.IsNaN(n) && (up ? n > 0 : n < 0);

    private static UpdateRuleViewModel? SceneRule(MainViewModel vm)
        => vm.IntegrationRules.FirstOrDefault(r => r.Actions.Any(a =>
               a.Category == NodeActionViewModel.CatScene && !string.IsNullOrWhiteSpace(a.Target) && a.Active));

    private static QuestViewModel? HerQuest(MainViewModel vm)
        => vm.Quests.FirstOrDefault(q => q.Title.Trim() == TutorialsQuests.QuestTitle);

    internal static UiViewModel? Portfolio(MainViewModel vm)
        => vm.Uis.FirstOrDefault(u => u.Name.Trim() == TutorialsUi.PortfolioName);

    /// <summary>A param row of a condition, set as a person sets it.</summary>
    private static void Set(NodeConditionViewModel c, string key, string value)
    {
        if (c.ParamRows.FirstOrDefault(r => r.Key == key) is { } row) row.Value = value;
        else c.Model.Params[key] = value;
    }

    private static void Set(NodeActionViewModel a, string key, string value)
    {
        if (a.ParamRows.FirstOrDefault(r => r.Key == key) is { } row) row.Value = value;
        else { a.Model.Params[key] = value; a.Reread(); }
    }

    /// <summary>A new line of hers right after <paramref name="after"/>, as + Sibling makes one.</summary>
    private static DialogueNodeViewModel NewLineAfter(MainViewModel vm, DialogueNodeViewModel after, string text)
    {
        vm.SelectedNode = after;
        vm.AddDialogueSiblingNodeCommand.Execute(null);
        var n = vm.SelectedNode!;
        n.Actor = TutorialsPart2.Speaker(vm);
        n.Text = text;
        return n;
    }

    private static DialogueNodeViewModel? LineSaying(MainViewModel vm, string text)
        => HerLines(vm).FirstOrDefault(n => n.Text.Trim() == text);

    /// <summary>Her vase line (tutorial 7): her new lines come after it, before she asks you to keep her secret.
    /// English on purpose: the story pack's words.</summary>
    private static DialogueNodeViewModel? VaseLine(DialogueViewModel d)
        => d.Nodes.FirstOrDefault(n => n.Text.StartsWith("Do you like my vase", StringComparison.Ordinal));

    /// <summary>A Variable row, comparing: through the row as the Type list and the Comparison list set it.</summary>
    private static void Compare(NodeConditionViewModel c, string variable, string comparison, string value)
    {
        c.DisplayType = NodeConditionViewModel.VariableFamilyType;
        c.VarComparison = comparison;
        c.VarName = variable;
        c.VarValue = value;
    }

    private static void PickRule(MainViewModel vm, Func<UpdateRuleViewModel, bool> which)
    {
        if (vm.IntegrationRules.FirstOrDefault(which) is { } r) vm.SelectedIntegrationRule = r;
    }

    internal static IReadOnlyList<TutorialDef> All { get; } = new[]
    {
        // ═══ Actions, in depth: "She grows braver" ═══════════════════════════════
        new TutorialDef
        {
            Id = "deeper-actions",
            Group = Group,
            Title = "tutorial.deeperActions.title",
            Summary = "tutorial.deeperActions.summary",
            Makes = "tutorial.deeperActions.makes",
            ParentId = "remembering",
            Level = 19,
            Leaves = vm => HerLines(vm).Any(n => ActionsOf(n).Any(a => IncrementsConfidence(a, up: true))),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperActions.story.title",
                    Body = "tutorial.deeperActions.story.body",
                    More = "tutorial.deeperActions.story.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, TutorialsPart2.ClosingLineOf),
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperActions.increment.title",
                    Body = "tutorial.deeperActions.increment.body",
                    More = "tutorial.deeperActions.increment.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, TutorialsPart2.ClosingLineOf),
                    IsDone = (vm, s) => HerLines(vm).Any(n => ActionsOf(n).Any(a => IncrementsConfidence(a, up: true))),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var last = TutorialsPart2.ClosingLineOf(d)!;
                        vm.SelectedNode = last;
                        var a = last.AddActionOnFinish();
                        a.DisplayType = NodeActionViewModel.VariableFamilyType;
                        a.VarOperation = "Increment";   // English on purpose: a value the code compares.
                        a.VarName = Confidence;
                        a.VarDelta = "1";
                    },
                    Hint = "tutorial.deeperActions.increment.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperActions.when.title",
                    Body = "tutorial.deeperActions.when.body",
                    More = "tutorial.deeperActions.when.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnStartBox",
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, TutorialsPart2.SweetOf),
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperActions.music.title",
                    Body = "tutorial.deeperActions.music.body",
                    More = "tutorial.deeperActions.music.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnStartBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, TutorialsPart2.SweetOf),
                    IsDone = (vm, s) => HerLines(vm).Any(n => ActionsOf(n).Any(a =>
                                            a.Type == NodeActionTypes.SwitchMusic && P(a, "music").Trim().Length > 0)),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var line = TutorialsPart2.SweetOf(d) ?? d.Nodes[^1];
                        vm.SelectedNode = line;
                        var a = line.AddActionOnStart();
                        a.DisplayType = NodeActionTypes.SwitchMusic;
                        // English on purpose: the story pack's words.
                        Set(a, "music", (vm.Music.FirstOrDefault(m => m.DisplayName.Contains("Neon Rain")) ?? vm.Music[^1]).Key);
                    },
                    Hint = "tutorial.deeperActions.music.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperActions.wait.title",
                    Body = "tutorial.deeperActions.wait.body",
                    More = "tutorial.deeperActions.wait.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, TutorialsPart2.ClosingLineOf),
                    IsDone = (vm, s) => HerLines(vm).Any(n => ActionsOf(n).Any(a =>
                                            a.Type == NodeActionTypes.Wait && Num(P(a, "seconds")) > 0)),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var last = TutorialsPart2.ClosingLineOf(d)!;
                        vm.SelectedNode = last;
                        var a = last.AddActionOnFinish();
                        a.DisplayType = NodeActionTypes.Wait;
                        Set(a, "seconds", "1");
                    },
                    Hint = "tutorial.deeperActions.wait.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperActions.portfolio.title",
                    Body = "tutorial.deeperActions.portfolio.body",
                    More = "tutorial.deeperActions.portfolio.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, TutorialsPart2.ClosingLineOf),
                    IsDone = (vm, s) => Portfolio(vm) is { } ui && HerLines(vm).Any(n => ActionsOf(n).Any(a =>
                                            a.Type == NodeActionTypes.SetGameObjectActive &&
                                            Is(P(a, "kind"), NodeActionViewModel.CatUi) && P(a, "target") == ui.Model.Id &&
                                            !Is(P(a, "active"), "false"))),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var last = TutorialsPart2.ClosingLineOf(d)!;
                        vm.SelectedNode = last;
                        var a = last.AddActionOnFinish();
                        a.DisplayType = NodeActionTypes.SetGameObjectActive;
                        a.Category = NodeActionViewModel.CatUi;
                        a.Target = Portfolio(vm)!.Model.Id;
                        a.Active = true;
                    },
                    Hint = "tutorial.deeperActions.portfolio.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperActions.quest.title",
                    Body = "tutorial.deeperActions.quest.body",
                    More = "tutorial.deeperActions.quest.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleActionsBox",
                    AlsoAllow = new[] { "field:ruleActions" },
                    // The rule that shows her first pose, chosen for the reader.
                    OnShown = vm => { if (SceneRule(vm) is { } r) vm.SelectedIntegrationRule = r; },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => r.Model.Actions.Any(a =>
                                            a.Type == Shared.QuestVocabulary.ActionType &&
                                            Is(P(a, Shared.QuestVocabulary.OperationParam), Shared.QuestVocabulary.CompleteTask) &&
                                            P(a, Shared.QuestVocabulary.TaskParam).Trim().Length > 0)),
                    Solve = (vm, s) =>
                    {
                        var rule = SceneRule(vm) ?? vm.IntegrationRules[^1];
                        vm.SelectedIntegrationRule = rule;
                        var q = HerQuest(vm)!;
                        var a = rule.AddAction();
                        a.DisplayType = Shared.QuestVocabulary.ActionType;
                        a.QuestPicker.QuestKey = q.Model.Key;
                        a.QuestOperation = Shared.QuestVocabulary.CompleteTask;
                        a.QuestPicker.TaskKey = q.Model.Tasks[^1].Key;
                    },
                    Hint = "tutorial.deeperActions.quest.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperActions.everywhere.title",
                    Body = "tutorial.deeperActions.everywhere.body",
                    More = "tutorial.deeperActions.everywhere.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleActionsBox",
                },
            },
        },

        // ═══ Conditions, in depth: "Only when she's ready" ═══════════════════════
        new TutorialDef
        {
            Id = "deeper-conditions",
            Group = Group,
            Title = "tutorial.deeperConditions.title",
            Summary = "tutorial.deeperConditions.summary",
            Makes = "tutorial.deeperConditions.makes",
            ParentId = "lines-that-choose",
            Level = 20,
            Leaves = vm => HerLines(vm).Any(n => Flat(n.Model.Conditions).Any(c => c.Type == NodeConditionTypes.ListContains)),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperConditions.story.title",
                    Body = "tutorial.deeperConditions.story.body",
                    More = "tutorial.deeperConditions.story.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, d => d.Nodes.FirstOrDefault()),
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperConditions.braver.title",
                    Body = "tutorial.deeperConditions.braver.body",
                    More = "tutorial.deeperConditions.braver.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "btn:addNode", "panel:dialogueList" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, d => VaseLine(d) ?? d.Nodes.FirstOrDefault()),
                    IsDone = (vm, s) => HerLines(vm).Any(n => n.Text.Trim().Length > 0 &&
                                            Flat(n.Model.Conditions).Any(c => ComparesUp(c, Confidence))),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var line = NewLineAfter(vm, VaseLine(d) ?? d.Nodes[0], BraverLine);
                        vm.AddNodeConditionCommand.Execute(null);
                        Compare(line.Conditions.Last(), Confidence, "greater or equal", "3");   // English on purpose: a value the code compares.
                    },
                    Hint = "tutorial.deeperConditions.braver.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperConditions.list.title",
                    Body = "tutorial.deeperConditions.list.body",
                    More = "tutorial.deeperConditions.list.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "btn:addNode", "panel:dialogueList" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, d => LineSaying(vm, BraverLine) ?? d.Nodes.FirstOrDefault()),
                    IsDone = (vm, s) => HerLines(vm).Any(n => Flat(n.Model.Conditions).Any(c =>
                                            c.Type == NodeConditionTypes.ListContains &&
                                            P(c, "list").Trim().Length > 0 && P(c, "value").Trim().Length > 0)),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var line = NewLineAfter(vm, LineSaying(vm, BraverLine) ?? d.Nodes[0], ShyLine);
                        vm.AddNodeConditionCommand.Execute(null);
                        var c = line.Conditions.Last();
                        c.DisplayType = NodeConditionTypes.ListContains;
                        Set(c, "list", PosesSeen);
                        Set(c, "value", "Shy");   // English on purpose: a value the pack keeps (tutorial 13).
                    },
                    Hint = "tutorial.deeperConditions.list.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperConditions.chance.title",
                    Body = "tutorial.deeperConditions.chance.body",
                    More = "tutorial.deeperConditions.chance.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, d => LineSaying(vm, ShyLine) ?? d.Nodes.FirstOrDefault()),
                    IsDone = (vm, s) => HerLines(vm).Any(n => Flat(n.Model.Conditions).Any(c => c.Type == NodeConditionTypes.DailyChance)),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var line = LineSaying(vm, ShyLine) ?? d.Nodes[^1];
                        vm.SelectedNode = line;
                        vm.AddNodeConditionCommand.Execute(null);
                        var c = line.Conditions.Last();
                        c.DisplayType = NodeConditionTypes.DailyChance;
                        Set(c, "chance", "50");
                    },
                    Hint = "tutorial.deeperConditions.chance.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperConditions.negate.title",
                    Body = "tutorial.deeperConditions.negate.body",
                    More = "tutorial.deeperConditions.negate.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperConditions.any.title",
                    Body = "tutorial.deeperConditions.any.body",
                    More = "tutorial.deeperConditions.any.more",
                    Kind = StepKind.Do,
                    Tab = TabWallpapers,
                    Anchor = "btn:addWallpaper",
                    AlsoAllow = new[] { "panel:wallpaperDetail" },
                    IsDone = (vm, s) => vm.Wallpapers.Any(w => Flat(w.Model.UnlockConditions).Any(c =>
                                            c.Type == NodeConditionTypes.GroupAny && (c.Conditions?.Count ?? 0) >= 2)),
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.AddWallpaperCommand.Execute(null);
                        var w = vm.SelectedWallpaper!;
                        w.DisplayName = "Sure of herself";   // English on purpose: the story pack's words.
                        w.SpritePath = TutorialAssets.Wallpaper2;
                        w.AddUnlockConditionGroupCommand.Execute(null);
                        var group = w.UnlockConditions.Last();
                        group.GroupModeIndex = 1;
                        group.AddLeafCommand.Execute(null);
                        Compare(group.Children.Last(), Confidence, "greater or equal", "8");   // English on purpose: a value the code compares.
                        group.AddLeafCommand.Execute(null);
                        var seen = group.Children.Last();
                        seen.DisplayType = NodeConditionTypes.ListContains;
                        Set(seen, "list", PosesSeen);
                        Set(seen, "value", "Bold");   // English on purpose: a value the pack keeps.
                    },
                    Hint = "tutorial.deeperConditions.any.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperConditions.photo.title",
                    Body = "tutorial.deeperConditions.photo.body",
                    More = "tutorial.deeperConditions.photo.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleConditions",
                    AlsoAllow = new[] { "panel:ruleIdentity" },
                    OnShown = vm => { if (SceneRule(vm) is { } r) vm.SelectedIntegrationRule = r; },
                    IsDone = (vm, s) => SceneRule(vm) is { } r && Flat(r.Model.Conditions).Any(c =>
                                            c.Type == NodeConditionTypes.QuestState &&
                                            P(c, Shared.QuestVocabulary.TaskParam).Trim().Length > 0 &&
                                            Is(P(c, Shared.QuestVocabulary.StateParam), Shared.QuestVocabulary.Completed)),
                    Solve = (vm, s) =>
                    {
                        var r = SceneRule(vm)!;
                        vm.SelectedIntegrationRule = r;
                        var q = HerQuest(vm)!;
                        var c = r.AddCondition();
                        c.DisplayType = NodeConditionTypes.QuestState;
                        c.QuestPicker.QuestKey = q.Model.Key;
                        c.QuestPicker.TaskKey = q.Model.Tasks[0].Key;
                        c.QuestState = Shared.QuestVocabulary.Completed;
                        r.Description = "She poses for her first photo once she is sure of herself.";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.deeperConditions.photo.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperConditions.whyNot.title",
                    Body = "tutorial.deeperConditions.whyNot.body",
                    More = "tutorial.deeperConditions.whyNot.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "field:ruleDebug",
                },
            },
        },

        // ═══ Rules, in depth: "Her nights at the gallery" ════════════════════════
        new TutorialDef
        {
            Id = "deeper-rules",
            Group = Group,
            Title = "tutorial.deeperRules.title",
            Summary = "tutorial.deeperRules.summary",
            Makes = "tutorial.deeperRules.makes",
            ParentId = "rules",
            Level = 21,
            Leaves = vm => vm.IntegrationRules.Any(r => r.Model.Branches.Count >= 2),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperRules.story.title",
                    Body = "tutorial.deeperRules.story.body",
                    More = "tutorial.deeperRules.story.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleIdentity", "field:triggerMode" },
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRules.nightly.title",
                    Body = "tutorial.deeperRules.nightly.body",
                    More = "tutorial.deeperRules.nightly.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "field:triggerMode", "panel:ruleIdentity" },
                    OnEnter = (vm, s) => s.Set("days", vm.IntegrationRules.Count(r => r.TriggerMode == UpdateRuleTriggerMode.OnDayChange)),
                    IsDone = (vm, s) => s.GrewSince("days", vm.IntegrationRules.Count(r => r.TriggerMode == UpdateRuleTriggerMode.OnDayChange)),
                    Solve = (vm, s) =>
                    {
                        vm.AddIntegrationRuleCommand.Execute(null);
                        var r = vm.SelectedIntegrationRule!;
                        r.DisplayName = "Some nights";   // English on purpose: the story pack's words.
                        r.Description = "Some nights she loses a little nerve.";   // English on purpose: the story pack's words.
                        r.TriggerMode = UpdateRuleTriggerMode.OnDayChange;
                    },
                    Hint = "tutorial.deeperRules.nightly.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRules.nightlyWhat.title",
                    Body = "tutorial.deeperRules.nightlyWhat.body",
                    More = "tutorial.deeperRules.nightlyWhat.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleActionsBox",
                    AlsoAllow = new[] { "panel:ruleConditions", "field:ruleActions" },
                    OnShown = vm => PickRule(vm, r => r.TriggerMode == UpdateRuleTriggerMode.OnDayChange),
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => r.TriggerMode == UpdateRuleTriggerMode.OnDayChange &&
                                            r.Model.Actions.Any(a => IncrementsConfidence(a, up: false)) &&
                                            Flat(r.Model.Conditions).Any(c => ComparesUp(c, Confidence)) &&
                                            Flat(r.Model.Conditions).Any(c => c.Type == NodeConditionTypes.DailyChance)),
                    Solve = (vm, s) =>
                    {
                        var r = vm.IntegrationRules.Last(x => x.TriggerMode == UpdateRuleTriggerMode.OnDayChange);
                        vm.SelectedIntegrationRule = r;
                        Compare(r.AddCondition(), Confidence, "greater than", "1");   // English on purpose: a value the code compares.
                        var chance = r.AddCondition();
                        chance.DisplayType = NodeConditionTypes.DailyChance;
                        Set(chance, "chance", "25");
                        var a = r.AddAction();
                        a.DisplayType = NodeActionViewModel.VariableFamilyType;
                        a.VarOperation = "Increment";   // English on purpose: a value the code compares.
                        a.VarName = Confidence;
                        a.VarDelta = "-1";
                    },
                    Hint = "tutorial.deeperRules.nightlyWhat.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRules.modes.title",
                    Body = "tutorial.deeperRules.modes.body",
                    More = "tutorial.deeperRules.modes.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "field:triggerMode",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRules.moodVariable.title",
                    Body = "tutorial.deeperRules.moodVariable.body",
                    More = "tutorial.deeperRules.moodVariable.more",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    IsDone = (vm, s) => vm.Variables.Any(v => Is(v.Name, Mood) && v.Model.Type == PackVariableType.String),
                    Solve = (vm, s) =>
                    {
                        vm.AddVariableCommand.Execute(null);
                        var v = vm.SelectedVariable!;
                        v.Name = Mood;
                        v.Type = PackVariableType.String;
                        v.DefaultValue = "Shy";   // English on purpose: a value the pack keeps.
                    },
                    Hint = "tutorial.deeperRules.moodVariable.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRules.branches.title",
                    Body = "tutorial.deeperRules.branches.body",
                    More = "tutorial.deeperRules.branches.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleBranches", "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => r.Model.Branches.Count >= 2 &&
                                            r.Model.Branches.Any(b => b.Conditions.Count == 0) &&
                                            r.Model.Branches.All(b => b.Actions.Count > 0) && r.Model.Actions.Count > 0),
                    Solve = (vm, s) =>
                    {
                        vm.AddIntegrationRuleCommand.Execute(null);
                        var r = vm.SelectedIntegrationRule!;
                        r.DisplayName = "Her mood";   // English on purpose: the story pack's words.
                        void SetMood(NodeActionViewModel a, string value)
                        {
                            a.DisplayType = NodeActionViewModel.VariableFamilyType;
                            a.VarName = Mood;
                            a.VarValue = value;
                        }
                        Compare(r.AddCondition(), Confidence, "greater or equal", "6");   // English on purpose: a value the code compares.
                        SetMood(r.AddAction(), "Bold");   // English on purpose: values the pack keeps.
                        r.AddBranch();
                        // English on purpose: the story pack's words.
                        Compare(r.Branches[^1].AddCondition(), Confidence, "greater or equal", "3");
                        SetMood(r.Branches[^1].AddAction(), "Brave");
                        r.AddBranch();
                        SetMood(r.Branches[^1].AddAction(), "Shy");
                    },
                    Hint = "tutorial.deeperRules.branches.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRules.timer.title",
                    Body = "tutorial.deeperRules.timer.body",
                    More = "tutorial.deeperRules.timer.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => Flat(r.Model.Conditions).Any(c => c.Type == NodeConditionTypes.Timer) &&
                                                                     r.Model.Actions.Count > 0),
                    Solve = (vm, s) =>
                    {
                        vm.AddIntegrationRuleCommand.Execute(null);
                        var r = vm.SelectedIntegrationRule!;
                        r.DisplayName = "Hearts in the air";   // English on purpose: the story pack's words.
                        var here = r.AddCondition();
                        here.DisplayType = NodeConditionTypes.LevelActive;
                        here.Level = "place:" + (StoryPack.Gallery(vm) ?? vm.Places[^1]).Key;
                        var timer = r.AddCondition();
                        timer.DisplayType = NodeConditionTypes.Timer;
                        Set(timer, "seconds", "30");
                        var a = r.AddAction();
                        a.DisplayType = NodeActionTypes.PlayEffect;
                        Set(a, Shared.Effects.EffectParam,
                            (vm.Effects.FirstOrDefault(e => e.DisplayName == TutorialsShow.HeartsName) ?? vm.Effects[^1]).Key);
                    },
                    Hint = "tutorial.deeperRules.timer.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRules.forEach.title",
                    Body = "tutorial.deeperRules.forEach.body",
                    More = "tutorial.deeperRules.forEach.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleForEach", "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r =>
                    {
                        if (string.IsNullOrWhiteSpace(r.Model.ForEach)) return false;
                        string token = "{" + (string.IsNullOrWhiteSpace(r.Model.ForEachAs) ? "item" : r.Model.ForEachAs!.Trim()) + "}";
                        return r.Model.Actions.Any(a => a.Params.Values.Any(v => v != null && v.Contains(token))) ||
                               Flat(r.Model.Conditions).Any(c => c.Params.Values.Any(v => v != null && v.Contains(token)));
                    }),
                    Solve = (vm, s) =>
                    {
                        vm.AddIntegrationRuleCommand.Execute(null);
                        var r = vm.SelectedIntegrationRule!;
                        r.DisplayName = "Hide the others";   // English on purpose: the story pack's words.
                        r.TriggerMode = UpdateRuleTriggerMode.OnSceneLoad;
                        r.ForEach = "MannequinLeft,MannequinRight";   // English on purpose: names of the pack's objects (tutorial 11).
                        r.ForEachAs = "m";
                        var c = r.AddCondition();
                        c.DisplayType = NodeConditionViewModel.VariableFamilyType;
                        c.VarName = KeptHerSecret;
                        c.VarValue = "false";
                        var a = r.AddAction();
                        a.DisplayType = NodeActionTypes.SetGameObjectActive;
                        a.Category = NodeActionViewModel.CatNpcs;
                        a.OverlayLevel = "place:" + (StoryPack.Gallery(vm) ?? vm.Places[^1]).Key;
                        a.Target = "{m}";
                        a.Active = false;
                    },
                    Hint = "tutorial.deeperRules.forEach.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRules.tools.title",
                    Body = "tutorial.deeperRules.tools.body",
                    More = "tutorial.deeperRules.tools.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "field:ruleDebug",
                },
            },
        },

        // ═══ Characters, in depth: "A wardrobe for her" ══════════════════════════
        new TutorialDef
        {
            Id = "deeper-characters",
            Group = Group,
            Title = "tutorial.deeperCharacters.title",
            Summary = "tutorial.deeperCharacters.summary",
            Makes = "tutorial.deeperCharacters.makes",
            ParentId = "a-face-that-moves",
            Level = 22,
            Leaves = vm => HerLines(vm).Any(n => n.Outfit.Trim() == "$" + Mood),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperCharacters.story.title",
                    Body = "tutorial.deeperCharacters.story.body",
                    More = "tutorial.deeperCharacters.story.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterTree",
                    OnShown = vm => OpenOutfit(vm, 0),
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperCharacters.brave.title",
                    Body = "tutorial.deeperCharacters.brave.body",
                    More = "tutorial.deeperCharacters.brave.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "field:outfitName",
                    AlsoAllow = new[] { "panel:characterTree" },
                    OnShown = vm => OpenOutfit(vm, 0),
                    IsDone = (vm, s) => OutfitNamed(vm, Brave) != null,
                    Solve = (vm, s) => OpenOutfit(vm, 0)!.GameObjectName = Brave,
                    Hint = "tutorial.deeperCharacters.brave.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperCharacters.shy.title",
                    Body = "tutorial.deeperCharacters.shy.body",
                    More = "tutorial.deeperCharacters.shy.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "btn:addOutfit",
                    AlsoAllow = new[] { "panel:characterTree", "field:outfitName", "panel:outfitSprites", "field:baseSprite" },
                    OnShown = vm => OpenOutfit(vm, 0),
                    IsDone = (vm, s) => OutfitNamed(vm, Shy) is { } o && o.BaseSprite.Contains("Bust1"),
                    Solve = (vm, s) => NewOutfit(vm, Shy, 1),
                    Hint = "tutorial.deeperCharacters.shy.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperCharacters.bold.title",
                    Body = "tutorial.deeperCharacters.bold.body",
                    More = "tutorial.deeperCharacters.bold.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "btn:addOutfit",
                    AlsoAllow = new[] { "panel:characterTree", "field:outfitName", "panel:outfitSprites", "field:baseSprite" },
                    IsDone = (vm, s) => OutfitNamed(vm, Bold) is { } o && o.BaseSprite.Contains("Bust5"),
                    Solve = (vm, s) => NewOutfit(vm, Bold, 5),
                    Hint = "tutorial.deeperCharacters.bold.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperCharacters.byMood.title",
                    Body = "tutorial.deeperCharacters.byMood.body",
                    More = "tutorial.deeperCharacters.byMood.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:dialogueList" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, d => d.Nodes.FirstOrDefault()),
                    IsDone = (vm, s) => HerLines(vm).Any(n => n.Outfit.Trim() == "$" + Mood),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var first = d.Nodes[0];
                        vm.SelectedNode = first;
                        first.UsesOutfitVariable = true;
                        first.Outfit = "$" + Mood;
                    },
                    Hint = "tutorial.deeperCharacters.byMood.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperCharacters.default.title",
                    Body = "tutorial.deeperCharacters.default.body",
                    More = "tutorial.deeperCharacters.default.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "field:defaultOutfit",
                    OnShown = vm => OpenOutfit(vm, 0),
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperCharacters.colour.title",
                    Body = "tutorial.deeperCharacters.colour.body",
                    More = "tutorial.deeperCharacters.colour.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "field:nameColour",
                    AlsoAllow = new[] { "panel:characterTree" },
                    OnShown = vm => OpenOutfit(vm, 0),
                    IsDone = (vm, s) => StoryPack.Dummy(vm) is { } c && c.NameColor.Trim().Length > 0,
                    Solve = (vm, s) => StoryPack.Dummy(vm)!.NameColor = "#F29BC0",
                    Hint = "tutorial.deeperCharacters.colour.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperCharacters.theGames.title",
                    Body = "tutorial.deeperCharacters.theGames.body",
                    More = "tutorial.deeperCharacters.theGames.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterTree",
                },
            },
        },
        // ═══ Places, in depth: "The gallery after dark" ══════════════════════════
        new TutorialDef
        {
            Id = "deeper-places",
            Group = Group,
            Title = "tutorial.deeperPlaces.title",
            Summary = "tutorial.deeperPlaces.summary",
            Makes = "tutorial.deeperPlaces.makes",
            ParentId = "dressing-the-room",
            Level = 23,
            Leaves = vm => VisitorPlacement(vm) is { } v && v.ActiveConditions.Count > 0,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperPlaces.story.title",
                    Body = "tutorial.deeperPlaces.story.body",
                    More = "tutorial.deeperPlaces.story.more",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    OnShown = vm => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperPlaces.poster.title",
                    Body = "tutorial.deeperPlaces.poster.body",
                    More = "tutorial.deeperPlaces.poster.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    AlsoAllow = new[] { "panel:placePreview" },
                    OnShown = vm => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                    IsDone = (vm, s) => Poster(vm) is { } p && p.Sprite.Trim().Length > 0 && p.SortingOrder < -10,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        var g = StoryPack.Gallery(vm)!;
                        vm.SelectedPlace = g;
                        g.AddGameObject();
                        var made = g.GameObjects.Last(o => !o.IsNpcRoot && !o.HasLight && !o.HasEffect);
                        made.Name = PosterName;
                        made.Sprite = TutorialAssets.Poster;
                        made.SortingOrder = -11;
                        made.X = -5.5f;
                        made.Y = 0.8f;
                    },
                    Hint = "tutorial.deeperPlaces.poster.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperPlaces.posterWhen.title",
                    Body = "tutorial.deeperPlaces.posterWhen.body",
                    More = "tutorial.deeperPlaces.posterWhen.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    OnShown = vm => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                    IsDone = (vm, s) => Poster(vm) is { } p && Flat(p.Model.ActiveConditions).Any(c => ComparesUp(c, Confidence)),
                    Solve = (vm, s) => Compare(Poster(vm)!.AddActiveCondition(), Confidence, "greater or equal", "3"),   // English on purpose: a value the code compares.
                    Hint = "tutorial.deeperPlaces.posterWhen.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperPlaces.order.title",
                    Body = "tutorial.deeperPlaces.order.body",
                    More = "tutorial.deeperPlaces.order.more",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperPlaces.visitor.title",
                    Body = "tutorial.deeperPlaces.visitor.body",
                    More = "tutorial.deeperPlaces.visitor.more",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "btn:addNpc",
                    AlsoAllow = new[] { "panel:npcDetail" },
                    IsDone = (vm, s) => Visitor(vm) is { } n && n.Sprite.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.AddNpcCommand.Execute(null);
                        vm.SelectedNpc!.DisplayName = VisitorName;
                        vm.SelectedNpc!.Sprite = TutorialAssets.Visitor;
                    },
                    Hint = "tutorial.deeperPlaces.visitor.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperPlaces.placeVisitor.title",
                    Body = "tutorial.deeperPlaces.placeVisitor.body",
                    More = "tutorial.deeperPlaces.placeVisitor.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addNpcToPlace",
                    AlsoAllow = new[] { "panel:placeGameObjects", "panel:placePreview" },
                    OnShown = vm => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                    IsDone = (vm, s) => VisitorPlacement(vm) != null,
                    Solve = (vm, s) =>
                    {
                        var g = StoryPack.Gallery(vm)!;
                        vm.SelectedPlace = g;
                        var placed = g.NpcsNode.AddNpc();
                        placed.Npc = Visitor(vm)!.Key;
                        placed.Name = VisitorName;
                        placed.StartActive = true;
                        placed.Body.X = 6f;
                    },
                    Hint = "tutorial.deeperPlaces.placeVisitor.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperPlaces.visitorShadow.title",
                    Body = "tutorial.deeperPlaces.visitorShadow.body",
                    More = "tutorial.deeperPlaces.visitorShadow.more",
                    Kind = StepKind.Free,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                    AlsoAllow = new[] { "panel:placeGameObjects" },
                    OnShown = vm => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                    // Her feet are lower than the game's figures' are: the shadow has to come down under them.
                    IsDone = (vm, s) => VisitorPlacement(vm) is { } v && v.Shadow.Y < -2.5f,
                    Solve = (vm, s) => TutorialsPart2.ShadowUnderFeet(vm, VisitorPlacement(vm)!),
                    Hint = "tutorial.deeperPlaces.visitorShadow.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperPlaces.visitorWhen.title",
                    Body = "tutorial.deeperPlaces.visitorWhen.body",
                    More = "tutorial.deeperPlaces.visitorWhen.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    OnShown = vm => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                    IsDone = (vm, s) => VisitorPlacement(vm) is { } v && Flat(v.ActiveConditions.Select(c => c.Model)).Any(c => ComparesUp(c, Confidence)),
                    Solve = (vm, s) => Compare(VisitorPlacement(vm)!.AddActiveCondition(), Confidence, "greater or equal", "5"),   // English on purpose: a value the code compares.
                    Hint = "tutorial.deeperPlaces.visitorWhen.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperPlaces.theGames.title",
                    Body = "tutorial.deeperPlaces.theGames.body",
                    More = "tutorial.deeperPlaces.theGames.more",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "btn:addVanillaSource",
                },
            },
        },
        // ═══ Dialogues, in depth: "Something new each visit" ═════════════════════
        new TutorialDef
        {
            Id = "deeper-dialogues",
            Group = Group,
            Title = "tutorial.deeperDialogues.title",
            Summary = "tutorial.deeperDialogues.summary",
            Makes = "tutorial.deeperDialogues.makes",
            ParentId = "first-conversation",
            Level = 24,
            Leaves = vm => HerLines(vm).Any(n => n.Kind == DialogueNodeKind.Random && n.Model.Children.Count >= 2),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperDialogues.story.title",
                    Body = "tutorial.deeperDialogues.story.body",
                    More = "tutorial.deeperDialogues.story.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, d => YouCanTalk(d) ?? d.Nodes.FirstOrDefault()),
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperDialogues.random.title",
                    Body = "tutorial.deeperDialogues.random.body",
                    More = "tutorial.deeperDialogues.random.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "field:nodeKind",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, d => YouCanTalk(d) ?? d.Nodes.FirstOrDefault()),
                    IsDone = (vm, s) => TutorialsPart2.HerSecret(vm) is { } d && d.Nodes.Any(n =>
                                            n.Kind == DialogueNodeKind.Random && n.Outfit.Trim().Length == 0 &&
                                            n.Model.Children.Count(id => d.Nodes.Any(c => c.Id == id && c.Text.Trim().Length > 0)) >= 2),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var roll = NewLineAfter(vm, YouCanTalk(d) ?? d.Nodes[0], "Hmm...");   // English on purpose: the story pack's words.
                        roll.Kind = DialogueNodeKind.Random;
                        roll.Outfit = "";
                        foreach (var said in RandomLines)
                        {
                            vm.SelectedNode = roll;
                            vm.AddDialogueChildNodeCommand.Execute(null);
                            vm.SelectedNode!.Actor = TutorialsPart2.Speaker(vm);
                            vm.SelectedNode!.Outfit = "";
                            vm.SelectedNode!.Text = said;
                        }
                        vm.SelectedNode = roll;
                    },
                    Hint = "tutorial.deeperDialogues.random.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperDialogues.timeout.title",
                    Body = "tutorial.deeperDialogues.timeout.body",
                    More = "tutorial.deeperDialogues.timeout.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, d => d.Nodes.FirstOrDefault(n => n.Kind == DialogueNodeKind.Random)),
                    IsDone = (vm, s) => HerLines(vm).Any(n => n.Kind == DialogueNodeKind.Random && n.IsTimeout && n.Timeout > 0),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var roll = d.Nodes.First(n => n.Kind == DialogueNodeKind.Random);
                        vm.SelectedNode = roll;
                        roll.Duration = NodeDurationMode.Timeout;
                        roll.Timeout = 1.5f;
                    },
                    Hint = "tutorial.deeperDialogues.timeout.hint",
                },
                new TutorialStep
                {
                    // Measured in the game (the in-game suite's J1): a choice offers only the answers whose
                    // conditions hold.
                    Title = "tutorial.deeperDialogues.laterAnswer.title",
                    Body = "tutorial.deeperDialogues.laterAnswer.body",
                    More = "tutorial.deeperDialogues.laterAnswer.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    AlsoAllow = new[] { "panel:nodeEditor", "panel:nodeConditions" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, d => d.Nodes.LastOrDefault(n => n.Kind == DialogueNodeKind.Choice)),
                    IsDone = (vm, s) => TutorialsPart2.HerSecret(vm) is { } d && d.Nodes.Where(n => n.Kind == DialogueNodeKind.Choice)
                                            .SelectMany(c => c.Model.Children).Select(id => d.Nodes.FirstOrDefault(n => n.Id == id))
                                            .Any(o => o != null && o.Text.Trim().Length > 0 && Flat(o.Model.Conditions).Any(c => ComparesUp(c, Confidence))),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        var choice = d.Nodes.Last(n => n.Kind == DialogueNodeKind.Choice);
                        vm.SelectedNode = choice;
                        vm.AddDialogueChildNodeCommand.Execute(null);
                        var answer = vm.SelectedNode!;
                        answer.Text = BraverAnswer;
                        vm.AddNodeConditionCommand.Execute(null);
                        Compare(answer.Conditions.Last(), Confidence, "greater or equal", "3");   // English on purpose: a value the code compares.
                    },
                    Hint = "tutorial.deeperDialogues.laterAnswer.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperDialogues.bold.title",
                    Body = "tutorial.deeperDialogues.bold.body",
                    More = "tutorial.deeperDialogues.bold.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, d => d.Nodes.FirstOrDefault(n => n.Text.Contains("guard"))),
                    IsDone = (vm, s) => HerLines(vm).Any(n => n.Text.Contains("<b>") && n.Text.Contains("</b>")),
                    Solve = (vm, s) =>
                    {
                        var line = HerLines(vm).First(n => n.Text.Contains("guard"));
                        vm.SelectedNode = line;
                        line.Text = line.Text.Replace("guard", "<b>guard</b>");
                    },
                    Hint = "tutorial.deeperDialogues.bold.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperDialogues.ways.title",
                    Body = "tutorial.deeperDialogues.ways.body",
                    More = "tutorial.deeperDialogues.ways.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperDialogues.settings.title",
                    Body = "tutorial.deeperDialogues.settings.body",
                    More = "tutorial.deeperDialogues.settings.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "field:dialogueName",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperDialogues.vanilla.title",
                    Body = "tutorial.deeperDialogues.vanilla.body",
                    More = "tutorial.deeperDialogues.vanilla.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "btn:addVanillaDialogue",
                },
            },
        },
        // ═══ Scenes, in depth: "Her bold pose" ═══════════════════════════════════
        new TutorialDef
        {
            Id = "deeper-scenes",
            Group = Group,
            Title = "tutorial.deeperScenes.title",
            Summary = "tutorial.deeperScenes.summary",
            Makes = "tutorial.deeperScenes.makes",
            ParentId = "rules",
            Level = 25,
            Leaves = vm => vm.IntegrationRules.Any(r => r.Model.Actions.Any(a => a.Type == NodeActionTypes.AddToList && Is(P(a, "value"), Bold))),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperScenes.story.title",
                    Body = "tutorial.deeperScenes.story.body",
                    More = "tutorial.deeperScenes.story.more",
                    Kind = StepKind.Read,
                    Tab = TabScenes,
                    Anchor = "btn:addScene",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperScenes.chaise.title",
                    Body = "tutorial.deeperScenes.chaise.body",
                    More = "tutorial.deeperScenes.chaise.more",
                    Kind = StepKind.Do,
                    Tab = TabScenes,
                    Anchor = "btn:addScene",
                    AlsoAllow = new[] { "panel:sceneDetail" },
                    IsDone = (vm, s) => SceneShowing(vm, 2) is { } sc && sc.VanillaFrame == "PhotoFrame.png",
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.AddSceneCommand.Execute(null);
                        var sc = vm.SelectedScene!;
                        sc.DisplayName = ChaiseName;
                        sc.SceneSprite = TutorialAssets.ScenePose(2);
                        sc.VanillaFrame = "PhotoFrame.png";   // English on purpose: the game's file name.
                    },
                    Hint = "tutorial.deeperScenes.chaise.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperScenes.bold.title",
                    Body = "tutorial.deeperScenes.bold.body",
                    More = "tutorial.deeperScenes.bold.more",
                    Kind = StepKind.Do,
                    Tab = TabScenes,
                    Anchor = "btn:addScene",
                    AlsoAllow = new[] { "panel:sceneDetail" },
                    IsDone = (vm, s) => SceneShowing(vm, 3) is { } sc && sc.StarmakerPhoto && sc.Sound == SceneSoundMode.Kiss,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.AddSceneCommand.Execute(null);
                        var sc = vm.SelectedScene!;
                        sc.DisplayName = BoldSceneName;
                        sc.SceneSprite = TutorialAssets.ScenePose(3);
                        sc.Sound = SceneSoundMode.Kiss;
                        sc.StarmakerPhoto = true;
                    },
                    Hint = "tutorial.deeperScenes.bold.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperScenes.byMood.title",
                    Body = "tutorial.deeperScenes.byMood.body",
                    More = "tutorial.deeperScenes.byMood.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleActionsBox", "panel:ruleConditions", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => SceneShowing(vm, 3) is { } sc && vm.IntegrationRules.Any(r =>
                                            r.Actions.Any(a => a.Category == NodeActionViewModel.CatScene && a.Target == sc.Key && a.Active) &&
                                            r.Model.Actions.Any(a => a.Type == NodeActionTypes.AddToList && Is(P(a, "value"), Bold))),
                    Solve = (vm, s) =>
                    {
                        vm.AddIntegrationRuleCommand.Execute(null);
                        var r = vm.SelectedIntegrationRule!;
                        r.DisplayName = "Her bold pose";   // English on purpose: the story pack's words.
                        var c = r.AddCondition();
                        c.DisplayType = NodeConditionViewModel.VariableFamilyType;
                        c.VarName = Mood;
                        c.VarValue = Bold;
                        var show = r.AddAction();
                        show.DisplayType = NodeActionTypes.SetGameObjectActive;
                        show.Category = NodeActionViewModel.CatScene;
                        show.Target = SceneShowing(vm, 3)!.Key;
                        show.Active = true;
                        var seen = r.AddAction();
                        seen.DisplayType = NodeActionTypes.AddToList;
                        seen.Model.Params["list"] = PosesSeen;
                        seen.Model.Params["value"] = Bold;
                        seen.Model.Params["unique"] = "true";
                        seen.Reread();
                    },
                    Hint = "tutorial.deeperScenes.byMood.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperScenes.brave.title",
                    Body = "tutorial.deeperScenes.brave.body",
                    More = "tutorial.deeperScenes.brave.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleActionsBox", "panel:ruleConditions", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => SceneShowing(vm, 2) is { } sc && vm.IntegrationRules.Any(r =>
                                            r.Actions.Any(a => a.Category == NodeActionViewModel.CatScene && a.Target == sc.Key && a.Active) &&
                                            Flat(r.Model.Conditions).Any(c => c.Type == NodeConditionTypes.LevelActive)),
                    Solve = (vm, s) =>
                    {
                        vm.AddIntegrationRuleCommand.Execute(null);
                        var r = vm.SelectedIntegrationRule!;
                        r.DisplayName = "On the chaise";   // English on purpose: the story pack's words.
                        var c = r.AddCondition();
                        c.DisplayType = NodeConditionViewModel.VariableFamilyType;
                        c.VarName = Mood;
                        c.VarValue = Brave;
                        var here = r.AddCondition();
                        here.DisplayType = NodeConditionTypes.LevelActive;
                        here.Level = "place:" + (StoryPack.Gallery(vm) ?? vm.Places[^1]).Key;
                        var a = r.AddAction();
                        a.DisplayType = NodeActionTypes.SetGameObjectActive;
                        a.Category = NodeActionViewModel.CatScene;
                        a.Target = SceneShowing(vm, 2)!.Key;
                        a.Active = true;
                    },
                    Hint = "tutorial.deeperScenes.brave.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperScenes.loop.title",
                    Body = "tutorial.deeperScenes.loop.body",
                    More = "tutorial.deeperScenes.loop.more",
                    Kind = StepKind.Read,
                    Tab = TabWallpapers,
                    Anchor = "panel:wallpaperDetail",
                    OnShown = vm => { if (vm.Wallpapers.LastOrDefault() is { } w) vm.SelectedWallpaper = w; },
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperScenes.theEnd.title",
                    Body = "tutorial.deeperScenes.theEnd.body",
                    More = "tutorial.deeperScenes.theEnd.more",
                    Kind = StepKind.Read,
                    Tab = MainTab.ModForge,
                    Anchor = "menu:file",
                },
            },
        },
    };

    // ── Her wardrobe (the characters chapter) ───────────────────────────────────
    // English on purpose: outfit names her Mood variable's values name.
    private const string Shy = "Shy", Brave = "Brave", Bold = "Bold";

    /// <summary>Her outfit at <paramref name="index"/> chosen and her row opened on the Characters tab.</summary>
    private static OutfitViewModel? OpenOutfit(MainViewModel vm, int index)
    {
        if (StoryPack.Dummy(vm) is not { } her || her.Outfits.Count <= index) return vm.SelectedOutfit;
        vm.SelectedCharacter = her;
        her.IsExpanded = true;
        vm.SelectedOutfit = her.Outfits[index];
        return vm.SelectedOutfit;
    }

    private static OutfitViewModel? OutfitNamed(MainViewModel vm, string name)
        => StoryPack.Dummy(vm)?.Outfits.FirstOrDefault(o => Is(o.GameObjectName, name));

    /// <summary>A new outfit of hers, as + Outfit makes one, dressed in one of the practice busts - its face too.</summary>
    private static void NewOutfit(MainViewModel vm, string name, int bust)
    {
        TutorialAssets.EnsureCopied(vm.PackRoot);
        vm.SelectedCharacter = StoryPack.Dummy(vm);
        vm.AddOutfitCommand.Execute(null);
        var o = vm.SelectedOutfit!;
        o.GameObjectName = name;
        o.BaseSprite = TutorialAssets.Bust(bust);
        o.BlinkEnabled = true;
        o.BlinkSprite = TutorialAssets.BustBlink(bust);
        o.MouthEnabled = true;
        o.MouthPrefix = TutorialAssets.MouthPrefix(bust);
        o.ExpressionEnabled = true;
        o.ExpressionPrefix = TutorialAssets.ExpressionPrefix(bust);
    }
    // ── The gallery after dark (the places chapter) ─────────────────────────────
    // English on purpose: names of the pack's object and NPC.
    internal const string PosterName = "Poster", VisitorName = "Visitor";

    private static GameObjectViewModel? Poster(MainViewModel vm)
        => StoryPack.Gallery(vm)?.GameObjects.FirstOrDefault(o => Is(o.Name, PosterName));

    private static NpcViewModel? Visitor(MainViewModel vm)
        => vm.Npcs.FirstOrDefault(n => Is(n.DisplayName, VisitorName));

    private static NpcPlacementViewModel? VisitorPlacement(MainViewModel vm)
        => Visitor(vm) is { } n ? StoryPack.Gallery(vm)?.NpcsNode.Npcs.FirstOrDefault(p => p.Npc == n.Key) : null;
    // ── Something new each visit (the dialogues chapter) ─────────────────────────
    // English on purpose: the story pack's words.
    private static readonly string[] RandomLines =
    {
        "I've been practicing my smile.",
        "Don't tell the guard I moved.",
        "Do I look any different today?",
    };

    /// <summary>An answer offered only once she is braver. English on purpose: the story pack's words.</summary>
    private const string BraverAnswer = "You seem braver tonight.";

    /// <summary>Your answer to her first words (tutorial 7). English on purpose: the story pack's words.</summary>
    private static DialogueNodeViewModel? YouCanTalk(DialogueViewModel d)
        => d.Nodes.FirstOrDefault(n => n.Text.Trim() == "You can talk?!");
    // ── Her bold pose (the scenes chapter) ───────────────────────────────────────
    // English on purpose: the story pack's scenes.
    internal const string ChaiseName = "On the chaise", BoldSceneName = "Bold pose";

    /// <summary>The scene of hers that shows practice pose <paramref name="n"/> (DummyScene0n), or null.</summary>
    private static SceneViewModel? SceneShowing(MainViewModel vm, int n)
        => vm.Scenes.FirstOrDefault(sc => sc.SceneSprite.Replace('\\', '/').EndsWith($"DummyScene0{n}.png", StringComparison.OrdinalIgnoreCase));
}
