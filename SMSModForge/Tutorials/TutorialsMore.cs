using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;

namespace SMSModForge.Tutorials;

/// <summary>
/// More "Going deeper" chapters (1.10.0, round 3): what the coverage check found no tutorial made (the author: "We
/// need to be teaching and giving tutorial assets to the player for every little thing they can create with
/// ModForge ... I also feel like a few more tutorials on Implementation specifically might be great, showing many
/// different things that can be done with that tab, either for creating something new or changing vanilla behavior
/// (try not to make it too invasive with the vanilla changes)"). Same story, Dummy Days, after everything before
/// them; each is listed under the tutorial it goes deeper into.
/// </summary>
internal static class TutorialsMore
{
    private const int TabDialogues = MainTab.Dialogues;
    private const int TabVariables = MainTab.Variables;
    private const int TabIntegration = MainTab.Integration;

    private const string Group = "tutorial.group.deeper";

    // The story's names (English on purpose: names the pack's own conditions, lines and rules use).
    private const string Confidence = TutorialsPart2.Confidence;
    private const string PosesSeen = TutorialsPart2.PosesSeen;
    private const string Mood = "Mood";
    internal const string VisitsToday = "VisitsToday";
    internal const string Luck = "Luck";
    internal const string PoseOfTheVisit = "PoseOfTheVisit";
    // English on purpose: the story pack's words.
    internal const string TestConversation = "Test: her lines";

    // ── Reading the pack ─────────────────────────────────────────────────────────

    private static string P(NodeActionDef a, string key) => a.Params.TryGetValue(key, out var v) ? v ?? "" : "";
    private static string P(NodeConditionDef c, string key) => c.Params.TryGetValue(key, out var v) ? v ?? "" : "";

    private static double Num(string s)
        => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;

    private static bool Is(string a, string b) => string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);

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

    private static string GalleryToken(MainViewModel vm) => "place:" + (StoryPack.Gallery(vm) ?? vm.Places[^1]).Key;

    private static bool AtGallery(NodeConditionDef c, MainViewModel vm)
        => c.Type == NodeConditionTypes.LevelActive && P(c, "level") == GalleryToken(vm);

    private static PackVariableViewModel? Variable(MainViewModel vm, string name)
        => vm.Variables.FirstOrDefault(v => Is(v.Name, name));

    private static IEnumerable<UpdateRuleViewModel> Rules(MainViewModel vm, UpdateRuleTriggerMode mode)
        => vm.IntegrationRules.Where(r => r.TriggerMode == mode);

    private static string HeartsKey(MainViewModel vm)
        => (vm.Effects.FirstOrDefault(e => e.DisplayName == TutorialsShow.HeartsName) ?? vm.Effects[^1]).Key;

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

    private static void Compare(NodeConditionViewModel c, string variable, string comparison, string value)
    {
        c.DisplayType = NodeConditionViewModel.VariableFamilyType;
        c.VarComparison = comparison;
        c.VarName = variable;
        if (comparison != "exists") c.VarValue = value;
    }

    private static void SetVar(NodeActionViewModel a, string variable, string value)
    {
        a.DisplayType = NodeActionViewModel.VariableFamilyType;
        a.VarName = variable;
        a.VarValue = value;
    }

    private static void AtTheGallery(MainViewModel vm, NodeConditionViewModel c)
    {
        c.DisplayType = NodeConditionTypes.LevelActive;
        c.Level = GalleryToken(vm);
    }

    private static UpdateRuleViewModel NewRule(MainViewModel vm, string name, UpdateRuleTriggerMode mode)
    {
        vm.AddIntegrationRuleCommand.Execute(null);
        var r = vm.SelectedIntegrationRule!;
        r.DisplayName = name;
        r.TriggerMode = mode;
        return r;
    }

    /// <summary>Her vase line (tutorial 7): her new lines go right after it. English on purpose: the story's words.</summary>
    private static DialogueNodeViewModel? VaseLine(DialogueViewModel d)
        => d.Nodes.FirstOrDefault(n => n.Text.StartsWith("Do you like my vase", StringComparison.Ordinal));

    /// <summary>A new line of hers right after the vase line, as + Sibling makes one, with one condition on it.</summary>
    private static NodeConditionViewModel NewLineWithCondition(MainViewModel vm, string text)
    {
        var d = TutorialsPart2.HerSecret(vm)!;
        vm.SelectedDialogue = d;
        vm.SelectedNode = VaseLine(d) ?? d.Nodes[0];
        vm.AddDialogueSiblingNodeCommand.Execute(null);
        var n = vm.SelectedNode!;
        n.Actor = TutorialsPart2.Speaker(vm);
        n.Outfit = "";   // Keep: what she wears does not change on this line (deeper-characters)
        n.Text = text;
        return n.AddCondition();
    }

    private static DialogueNodeViewModel? LineSaying(MainViewModel vm, string text)
        => HerLines(vm).FirstOrDefault(n => n.Text.Trim() == text);

    private static bool LineAsks(MainViewModel vm, string text, Func<NodeConditionDef, bool> what)
        => LineSaying(vm, text) is { } n && Flat(n.Model.Conditions).Any(what);

    private static void ShowLine(MainViewModel vm, string text)
        => TutorialsPart2.ShowHerLine(vm, d => d.Nodes.FirstOrDefault(n => n.Text.Trim() == text) ?? VaseLine(d));

    // English on purpose: the story pack's words, which its checks read back.
    private const string StarlightName = "Starlight",
        UnluckyLine = "Not my luckiest day.",
        HummedLine = "Mmm-hmm-hmm...",
        LuckyLine = "Today feels lucky.",
        HumLine = "Sometimes I hum when nobody's here.",
        PractisedLine = "I practiced today's pose all morning.",
        ThreePosesLine = "Three poses already! I'm a natural.",
        BLine = "Bold or brave - either way, I'm getting there.";

    internal static IReadOnlyList<TutorialDef> All { get; } = new[]
    {
        // ═══ Rules that watch: "Her gallery keeps time" ══════════════════════════
        new TutorialDef
        {
            Id = "deeper-watching",
            Group = Group,
            Title = "tutorial.deeperWatching.title",
            Summary = "tutorial.deeperWatching.summary",
            Makes = "tutorial.deeperWatching.makes",
            ParentId = "rules",
            Level = 26,
            Leaves = vm => vm.IntegrationRules.Any(r => Flat(r.Model.Conditions).Any(c => c.Type == NodeConditionTypes.InputKey)),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperWatching.story.title",
                    Body = "tutorial.deeperWatching.story.body",
                    More = "tutorial.deeperWatching.story.more",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperWatching.daily.title",
                    Body = "tutorial.deeperWatching.daily.body",
                    More = "tutorial.deeperWatching.daily.more",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    IsDone = (vm, s) => vm.Variables.Any(v => v.Model.RefreshMode == PackVariableRefreshMode.Daily && v.Model.Type == PackVariableType.Int),
                    Solve = (vm, s) =>
                    {
                        vm.AddVariableCommand.Execute(null);
                        var v = vm.SelectedVariable!;
                        v.Name = VisitsToday;
                        v.Type = PackVariableType.Int;
                        v.DefaultValue = "0";
                        v.RefreshMode = PackVariableRefreshMode.Daily;
                    },
                    Hint = "tutorial.deeperWatching.daily.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperWatching.luck.title",
                    Body = "tutorial.deeperWatching.luck.body",
                    More = "tutorial.deeperWatching.luck.more",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    IsDone = (vm, s) => vm.Variables.Any(v => v.Model.Type == PackVariableType.Float &&
                                                              v.Model.RefreshMode == PackVariableRefreshMode.DailyRandom &&
                                                              Num(v.MaxValue) > Num(v.MinValue)),
                    Solve = (vm, s) =>
                    {
                        vm.AddVariableCommand.Execute(null);
                        var v = vm.SelectedVariable!;
                        v.Name = Luck;
                        v.Type = PackVariableType.Float;
                        v.DefaultValue = "0.5";
                        v.RefreshMode = PackVariableRefreshMode.DailyRandom;
                        v.MinValue = "0";
                        v.MaxValue = "1";
                    },
                    Hint = "tutorial.deeperWatching.luck.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperWatching.perVisit.title",
                    Body = "tutorial.deeperWatching.perVisit.body",
                    More = "tutorial.deeperWatching.perVisit.more",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    IsDone = (vm, s) => vm.Variables.Any(v => v.Model.RefreshMode == PackVariableRefreshMode.LevelRandom &&
                                                              v.RefreshScope == GalleryToken(vm)),
                    Solve = (vm, s) =>
                    {
                        vm.AddVariableCommand.Execute(null);
                        var v = vm.SelectedVariable!;
                        v.Name = PoseOfTheVisit;
                        v.Type = PackVariableType.Int;
                        v.DefaultValue = "1";
                        v.RefreshMode = PackVariableRefreshMode.LevelRandom;
                        v.RefreshScope = GalleryToken(vm);
                        v.MinValue = "1";
                        v.MaxValue = "3";
                    },
                    Hint = "tutorial.deeperWatching.perVisit.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperWatching.goodbye.title",
                    Body = "tutorial.deeperWatching.goodbye.body",
                    More = "tutorial.deeperWatching.goodbye.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "field:triggerMode", "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity" },
                    IsDone = (vm, s) => Rules(vm, UpdateRuleTriggerMode.OnFallingEdge).Any(r =>
                                            Flat(r.Model.Conditions).Any(c => AtGallery(c, vm)) && r.Model.Actions.Count > 0),
                    Solve = (vm, s) =>
                    {
                        // English on purpose: the story pack's words.
                        var r = NewRule(vm, "Goodbye", UpdateRuleTriggerMode.OnFallingEdge);
                        AtTheGallery(vm, r.AddCondition());
                        var a = r.AddAction();
                        a.DisplayType = NodeActionViewModel.VariableFamilyType;
                        a.VarOperation = "Increment";   // English on purpose: a value the code compares.
                        a.VarName = VisitsToday;
                        a.VarDelta = "1";
                    },
                    Hint = "tutorial.deeperWatching.goodbye.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperWatching.whilePassing.title",
                    Body = "tutorial.deeperWatching.whilePassing.body",
                    More = "tutorial.deeperWatching.whilePassing.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "field:triggerMode", "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity" },
                    IsDone = (vm, s) => Rules(vm, UpdateRuleTriggerMode.WhilePassing).Any(r =>
                                            Flat(r.Model.Conditions).Any(c => c.Type == NodeConditionTypes.VariableCompare && Is(P(c, "name"), Confidence)) &&
                                            r.Model.Actions.Any(a => a.Type == NodeActionTypes.SetVariable && Is(P(a, "name"), Confidence))),
                    Solve = (vm, s) =>
                    {
                        // English on purpose: the story pack's words.
                        var r = NewRule(vm, "Never more than she can hold", UpdateRuleTriggerMode.WhilePassing);
                        Compare(r.AddCondition(), Confidence, "greater than", "10");   // English on purpose: a value the code compares.
                        SetVar(r.AddAction(), Confidence, "10");
                    },
                    Hint = "tutorial.deeperWatching.whilePassing.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperWatching.weather.title",
                    Body = "tutorial.deeperWatching.weather.body",
                    More = "tutorial.deeperWatching.weather.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => Flat(r.Model.Conditions).Any(c => c.Type == NodeConditionTypes.Weather) &&
                                                                     r.Model.Actions.Count > 0),
                    Solve = (vm, s) =>
                    {
                        // English on purpose: the story pack's words.
                        var r = NewRule(vm, "Rainy days", UpdateRuleTriggerMode.OnRisingEdge);
                        var c = r.AddCondition();
                        c.DisplayType = NodeConditionTypes.Weather;
                        Set(c, "state", "Raining");   // English on purpose: a value the game reads.
                        SetVar(r.AddAction(), Mood, "Shy");
                    },
                    Hint = "tutorial.deeperWatching.weather.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperWatching.key.title",
                    Body = "tutorial.deeperWatching.key.body",
                    More = "tutorial.deeperWatching.key.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => Flat(r.Model.Conditions).Any(c => c.Type == NodeConditionTypes.InputKey &&
                                                                                                        P(c, "key").Length > 0) &&
                                                                     r.Model.Actions.Count > 0),
                    Solve = (vm, s) =>
                    {
                        // English on purpose: the story pack's words.
                        var r = NewRule(vm, "Wave", UpdateRuleTriggerMode.OnRisingEdge);
                        AtTheGallery(vm, r.AddCondition());
                        var key = r.AddCondition();
                        key.DisplayType = NodeConditionTypes.InputKey;
                        key.InputKeyToken = "H";   // English on purpose: Unity's name for the key.
                        key.InputPhase = InputPhases.Pressed;
                        var a = r.AddAction();
                        a.DisplayType = NodeActionTypes.PlayEffect;
                        Set(a, Shared.Effects.EffectParam, HeartsKey(vm));
                    },
                    Hint = "tutorial.deeperWatching.key.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperWatching.always.title",
                    Body = "tutorial.deeperWatching.always.body",
                    More = "tutorial.deeperWatching.always.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => Flat(r.Model.Conditions).Any(c => c.Type == NodeConditionTypes.AlwaysTrue)),
                    Solve = (vm, s) =>
                    {
                        // English on purpose: the story pack's words.
                        var r = NewRule(vm, "Try it out", UpdateRuleTriggerMode.OnSceneLoad);
                        var c = r.AddCondition();
                        c.DisplayType = NodeConditionTypes.AlwaysTrue;
                        var a = r.AddAction();
                        a.DisplayType = NodeActionTypes.PlayEffect;
                        Set(a, Shared.Effects.EffectParam, HeartsKey(vm));
                    },
                    Hint = "tutorial.deeperWatching.always.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperWatching.wrap.title",
                    Body = "tutorial.deeperWatching.wrap.body",
                    More = "tutorial.deeperWatching.wrap.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "field:triggerMode",
                },
            },
        },

        // ═══ Conditions, more: "Her luck" ════════════════════════════════════════
        new TutorialDef
        {
            Id = "deeper-luck",
            Group = Group,
            Title = "tutorial.deeperLuck.title",
            Summary = "tutorial.deeperLuck.summary",
            Makes = "tutorial.deeperLuck.makes",
            ParentId = "lines-that-choose",
            Level = 27,
            Leaves = vm => vm.Dialogues.Any(d => d.DisplayName.Trim() == TestConversation),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperLuck.story.title",
                    Body = "tutorial.deeperLuck.story.body",
                    More = "tutorial.deeperLuck.story.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, VaseLine),
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLuck.lucky.title",
                    Body = "tutorial.deeperLuck.lucky.body",
                    More = "tutorial.deeperLuck.lucky.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowLine(vm, LuckyLine),
                    IsDone = (vm, s) => LineAsks(vm, LuckyLine, c => c.Type == NodeConditionTypes.VariableCompare && Is(P(c, "name"), Luck)),
                    Solve = (vm, s) => Compare(NewLineWithCondition(vm, LuckyLine), Luck, "greater than", "0.5"),   // English on purpose: a value the code compares.
                    Hint = "tutorial.deeperLuck.lucky.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLuck.random.title",
                    Body = "tutorial.deeperLuck.random.body",
                    More = "tutorial.deeperLuck.random.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowLine(vm, HumLine),
                    IsDone = (vm, s) => LineAsks(vm, HumLine, c => c.Type == NodeConditionTypes.Random),
                    Solve = (vm, s) =>
                    {
                        var c = NewLineWithCondition(vm, HumLine);
                        c.DisplayType = NodeConditionTypes.Random;
                        Set(c, "chance", "0.3");
                    },
                    Hint = "tutorial.deeperLuck.random.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLuck.exists.title",
                    Body = "tutorial.deeperLuck.exists.body",
                    More = "tutorial.deeperLuck.exists.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowLine(vm, PractisedLine),
                    IsDone = (vm, s) => LineAsks(vm, PractisedLine, c => c.Type == NodeConditionTypes.VariableExists),
                    Solve = (vm, s) => Compare(NewLineWithCondition(vm, PractisedLine), PoseOfTheVisit, "exists", ""),
                    Hint = "tutorial.deeperLuck.exists.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLuck.count.title",
                    Body = "tutorial.deeperLuck.count.body",
                    More = "tutorial.deeperLuck.count.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowLine(vm, ThreePosesLine),
                    IsDone = (vm, s) => LineAsks(vm, ThreePosesLine, c => c.Type == NodeConditionTypes.ListCount && Is(P(c, "list"), PosesSeen)),
                    Solve = (vm, s) =>
                    {
                        var c = NewLineWithCondition(vm, ThreePosesLine);
                        c.DisplayType = NodeConditionTypes.ListCount;
                        Set(c, "list", PosesSeen);
                        Set(c, "comparison", "greater or equal");   // English on purpose: a value the code compares.
                        Set(c, "value", "3");
                    },
                    Hint = "tutorial.deeperLuck.count.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLuck.startsWith.title",
                    Body = "tutorial.deeperLuck.startsWith.body",
                    More = "tutorial.deeperLuck.startsWith.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowLine(vm, BLine),
                    IsDone = (vm, s) => LineAsks(vm, BLine, c => c.Type == NodeConditionTypes.VariableStartsWith && Is(P(c, "name"), Mood)),
                    Solve = (vm, s) =>
                    {
                        var c = NewLineWithCondition(vm, BLine);
                        c.DisplayType = NodeConditionTypes.VariableStartsWith;
                        Set(c, "name", Mood);
                        Set(c, "value", "B");
                    },
                    Hint = "tutorial.deeperLuck.startsWith.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLuck.test.title",
                    Body = "tutorial.deeperLuck.test.body",
                    More = "tutorial.deeperLuck.test.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "btn:addDialogue",
                    AlsoAllow = new[] { "field:dialogueName", "field:startConditions", "panel:dialogueList", "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueIdentity" },
                    IsDone = (vm, s) => vm.Dialogues.Any(d => d.Queued && d.DisplayName.Trim() == TestConversation && d.Nodes.Count > 0),
                    Solve = (vm, s) =>
                    {
                        vm.AddDialogueCommand.Execute(null);
                        var d = vm.SelectedDialogue!;
                        d.DisplayName = TestConversation;
                        d.StartConditions[0].Level = GalleryToken(vm);
                        d.Queued = true;
                        if (d.Nodes.Count == 0) vm.AddDialogueRootNodeCommand.Execute(null);
                        var n = vm.SelectedNode ?? d.Nodes[0];
                        n.Actor = TutorialsPart2.Speaker(vm);
                        n.Text = "Testing, testing - every line of mine, on Talk.";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.deeperLuck.test.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLuck.negate.title",
                    Body = "tutorial.deeperLuck.negate.body",
                    More = "tutorial.deeperLuck.negate.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowLine(vm, UnluckyLine),
                    IsDone = (vm, s) => LineAsks(vm, UnluckyLine, c => c.Negate && Is(P(c, "name"), Luck)),
                    Solve = (vm, s) =>
                    {
                        var c = NewLineWithCondition(vm, UnluckyLine);
                        Compare(c, Luck, "greater than", "0.5");   // English on purpose: a value the code compares.
                        c.Negate = true;
                    },
                    Hint = "tutorial.deeperLuck.negate.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLuck.hum.title",
                    Body = "tutorial.deeperLuck.hum.body",
                    More = "tutorial.deeperLuck.hum.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:dialogueList", "panel:nodeConditions" },
                    OnShown = vm => ShowLine(vm, HumLine),
                    IsDone = (vm, s) => HerLines(vm).Any(n => n.TextSameEverywhere),
                    Solve = (vm, s) =>
                    {
                        ShowLine(vm, HumLine);
                        vm.AddDialogueChildNodeCommand.Execute(null);
                        var n = vm.SelectedNode!;
                        n.Actor = TutorialsPart2.Speaker(vm);
                        n.Outfit = "";
                        n.Text = HummedLine;
                        n.TextSameEverywhere = true;
                    },
                    Hint = "tutorial.deeperLuck.hum.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLuck.wrap.title",
                    Body = "tutorial.deeperLuck.wrap.body",
                    More = "tutorial.deeperLuck.wrap.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                },
            },
        },

        // ═══ Changing the game, gently ═══════════════════════════════════════════
        new TutorialDef
        {
            Id = "deeper-gently",
            Group = Group,
            Title = "tutorial.deeperGently.title",
            Summary = "tutorial.deeperGently.summary",
            Makes = "tutorial.deeperGently.makes",
            ParentId = "rules",
            Level = 28,
            Leaves = vm => vm.Dialogues.Any(d => IsBeachRain(d) && d.Nodes.Any(n => n.Text.Contains("gallery downtown"))),   // English on purpose: the story pack's words.
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.story.title",
                    Body = "tutorial.deeperGently.story.body",
                    More = "tutorial.deeperGently.story.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleIdentity", "field:triggerMode" },
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.weather.title",
                    Body = "tutorial.deeperGently.weather.body",
                    More = "tutorial.deeperGently.weather.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => r.Model.Actions.Any(a => a.Type == NodeActionTypes.SetWeather && P(a, "weather").Length > 0)),
                    Solve = (vm, s) =>
                    {
                        // English on purpose: the story pack's words.
                        var r = NewRule(vm, "Clear skies", UpdateRuleTriggerMode.OnRisingEdge);
                        Compare(r.AddCondition(), Mood, "equals", "Bold");   // English on purpose: values the code compares.
                        var a = r.AddAction();
                        a.DisplayType = NodeActionTypes.SetWeather;
                        Set(a, "weather", "Clear");   // English on purpose: a value the game reads.
                    },
                    Hint = "tutorial.deeperGently.weather.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.dim.title",
                    Body = "tutorial.deeperGently.dim.body",
                    More = "tutorial.deeperGently.dim.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => r.TriggerMode == UpdateRuleTriggerMode.OnRisingEdge &&
                                            r.Model.Actions.Any(a => DimsTheDoors(a, below: true))),
                    Solve = (vm, s) =>
                    {
                        // English on purpose: the story pack's words.
                        var r = NewRule(vm, "Quiet gallery", UpdateRuleTriggerMode.OnRisingEdge);
                        AtTheGallery(vm, r.AddCondition());
                        DimDoors(r.AddAction(), "0.6", "1");
                    },
                    Hint = "tutorial.deeperGently.dim.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.undim.title",
                    Body = "tutorial.deeperGently.undim.body",
                    More = "tutorial.deeperGently.undim.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleActionsBox",
                    AlsoAllow = new[] { "field:ruleActions" },
                    OnShown = vm => { if (Goodbye(vm) is { } r) vm.SelectedIntegrationRule = r; },
                    IsDone = (vm, s) => Goodbye(vm) is { } r && r.Model.Actions.Any(a => DimsTheDoors(a, below: false)),
                    Solve = (vm, s) =>
                    {
                        var r = Goodbye(vm)!;
                        vm.SelectedIntegrationRule = r;
                        DimDoors(r.AddAction(), "1", "1");
                    },
                    Hint = "tutorial.deeperGently.undim.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.flash.title",
                    Body = "tutorial.deeperGently.flash.body",
                    More = "tutorial.deeperGently.flash.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleActionsBox",
                    AlsoAllow = new[] { "field:ruleActions" },
                    OnShown = vm => { if (RuleNamed(vm, BoldPoseRule) is { } r) vm.SelectedIntegrationRule = r; },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => r.Model.Actions.Any(a => a.Type == NodeActionTypes.EmitSignal && P(a, "signal").Length > 0)),
                    Solve = (vm, s) =>
                    {
                        var r = RuleNamed(vm, BoldPoseRule) ?? vm.IntegrationRules[^1];
                        vm.SelectedIntegrationRule = r;
                        var a = r.AddAction();
                        a.DisplayType = NodeActionTypes.EmitSignal;
                        Set(a, "signal", "flash");   // English on purpose: the game's own signal.
                    },
                    Hint = "tutorial.deeperGently.flash.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.walk.title",
                    Body = "tutorial.deeperGently.walk.body",
                    More = "tutorial.deeperGently.walk.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "btn:addDialogue",
                    AlsoAllow = new[] { "field:dialogueName", "field:startConditions", "panel:dialogueList", "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueIdentity" },
                    IsDone = (vm, s) => WalkHome(vm) is { } d && d.Queued && d.Nodes.Count >= 3,
                    Solve = (vm, s) =>
                    {
                        vm.AddDialogueCommand.Execute(null);
                        var d = vm.SelectedDialogue!;
                        d.DisplayName = WalkHomeName;
                        d.StartConditions[0].Level = GalleryToken(vm);
                        d.Queued = true;
                        if (d.Nodes.Count == 0) vm.AddDialogueRootNodeCommand.Execute(null);
                        var n = vm.SelectedNode ?? d.Nodes[0];
                        n.Actor = TutorialsPart2.Speaker(vm);
                        n.Text = WalkAsk;
                        foreach (string text in new[] { WalkGo, WalkThere })
                        {
                            vm.AddDialogueSiblingNodeCommand.Execute(null);
                            vm.SelectedNode!.Actor = TutorialsPart2.Speaker(vm);
                            vm.SelectedNode.Text = text;
                        }
                    },
                    Hint = "tutorial.deeperGently.walk.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.crossFade.title",
                    Body = "tutorial.deeperGently.crossFade.body",
                    More = "tutorial.deeperGently.crossFade.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowWalkLine(vm, WalkGo),
                    IsDone = (vm, s) => WalkLine(vm, WalkGo) is { } n && n.Model.ActionsOnFinish.Any(a =>
                                            a.Type == NodeActionTypes.TransitionLevels && P(a, "toLevel").Length > 0),
                    Solve = (vm, s) =>
                    {
                        ShowWalkLine(vm, WalkGo);
                        var a = vm.SelectedNode!.AddActionOnFinish();
                        a.DisplayType = NodeActionTypes.TransitionLevels;
                        Set(a, "fromLevel", GalleryToken(vm));
                        Set(a, "toLevel", MyRoom);
                        Set(a, "seconds", "1");
                    },
                    Hint = "tutorial.deeperGently.crossFade.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.later.title",
                    Body = "tutorial.deeperGently.later.body",
                    More = "tutorial.deeperGently.later.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowWalkLine(vm, WalkThere),
                    IsDone = (vm, s) => WalkLine(vm, WalkThere) is { } n &&
                                        n.Model.ActionsOnFinish.Any(a => a.Type == NodeActionTypes.Transitions) &&
                                        n.Model.ActionsOnFinish.Any(a => a.Type == NodeActionTypes.LeaveUiFaded),
                    Solve = (vm, s) =>
                    {
                        ShowWalkLine(vm, WalkThere);
                        var a = vm.SelectedNode!.AddActionOnFinish();
                        a.DisplayType = NodeActionTypes.Transitions;
                        Set(a, Shared.Transitions.StyleParam, Shared.Transitions.TextScreen);
                        Set(a, Shared.Transitions.TextParam, "A little later...");   // English on purpose: the story pack's words.
                        vm.SelectedNode.AddActionOnFinish().DisplayType = NodeActionTypes.LeaveUiFaded;
                    },
                    Hint = "tutorial.deeperGently.later.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.shortcut.title",
                    Body = "tutorial.deeperGently.shortcut.body",
                    More = "tutorial.deeperGently.shortcut.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleConditions", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity", "field:triggerMode" },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => r.Model.Actions.Any(a => a.Type == NodeActionTypes.Travel &&
                                                                                              P(a, "target") == GalleryToken(vm))),
                    Solve = (vm, s) =>
                    {
                        // English on purpose: the story pack's words.
                        var r = NewRule(vm, "Shortcut", UpdateRuleTriggerMode.OnRisingEdge);
                        var room = r.AddCondition();
                        room.DisplayType = NodeConditionTypes.LevelActive;
                        room.Level = MyRoom;
                        var key = r.AddCondition();
                        key.DisplayType = NodeConditionTypes.InputKey;
                        key.InputKeyToken = "G";   // English on purpose: Unity's name for the key.
                        key.InputPhase = InputPhases.Pressed;
                        var a = r.AddAction();
                        a.DisplayType = NodeActionTypes.Travel;
                        Set(a, "target", GalleryToken(vm));
                    },
                    Hint = "tutorial.deeperGently.shortcut.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.gameLine.title",
                    Body = "tutorial.deeperGently.gameLine.body",
                    More = "tutorial.deeperGently.gameLine.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "btn:addVanillaDialogue",
                    AlsoAllow = new[] { "panel:dialogueList", "panel:dialogueNodes", "panel:nodeEditor" },
                    IsDone = (vm, s) => vm.Dialogues.Any(d => IsBeachRain(d) && d.Nodes.Any(n => n.Text.Contains("gallery downtown"))),   // English on purpose: the story pack's words.
                    Solve = (vm, s) =>
                    {
                        vm.AddVanillaDialogueCommand.Execute(null);
                        var d = vm.SelectedVanillaDialogue!;
                        d.VanillaSource = VanillaDialogueCatalog.Find(BeachRain);
                        var added = d.AddNode(parentId: d.Model.RootNodeIds.Count > 0 ? d.Model.RootNodeIds[0] : null);
                        added.Actor = vm.Characters.First(c => c.IsPlayer).Key;
                        added.Text = "Maybe the gallery downtown is open.";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.deeperGently.gameLine.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.talk.title",
                    Body = "tutorial.deeperGently.talk.body",
                    More = "tutorial.deeperGently.talk.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueIdentity",
                    AlsoAllow = new[] { "panel:dialogueList" },
                    OnShown = vm => { if (TutorialsPart2.HerSecret(vm) is { } d) vm.SelectedDialogue = d; },
                    IsDone = (vm, s) => TutorialsPart2.HerSecret(vm) is { ReplayOnTalk: true, QueueBehind: true },
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        d.ReplayOnTalk = true;
                        d.QueueBehind = true;
                    },
                    Hint = "tutorial.deeperGently.talk.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperGently.wrap.title",
                    Body = "tutorial.deeperGently.wrap.body",
                    More = "tutorial.deeperGently.wrap.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueList",
                },
            },
        },

        // ═══ Her gallery moves ═══════════════════════════════════════════════════
        new TutorialDef
        {
            Id = "deeper-moves",
            Group = Group,
            Title = "tutorial.deeperMoves.title",
            Summary = "tutorial.deeperMoves.summary",
            Makes = "tutorial.deeperMoves.makes",
            ParentId = "dressing-the-room",
            Level = 29,
            Leaves = vm => ShowLineOf(vm) is { } n && n.Model.ActionsOnFinish.Any(a => a.Type == NodeActionTypes.SetSprite),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperMoves.story.title",
                    Body = "tutorial.deeperMoves.story.body",
                    More = "tutorial.deeperMoves.story.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, VaseLine),
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperMoves.line.title",
                    Body = "tutorial.deeperMoves.line.body",
                    More = "tutorial.deeperMoves.line.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    AlsoAllow = new[] { "panel:nodeEditor", "panel:dialogueList", "panel:nodeConditions" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, VaseLine),
                    IsDone = (vm, s) => ShowLineOf(vm) is { } n && Flat(n.Model.Conditions).Any(c => Is(P(c, "name"), Confidence)),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        vm.SelectedNode = VaseLine(d) ?? d.Nodes[0];
                        vm.AddDialogueSiblingNodeCommand.Execute(null);
                        var n = vm.SelectedNode!;
                        n.Actor = TutorialsPart2.Speaker(vm);
                        n.Outfit = "";
                        n.Text = WatchLine;
                        Compare(n.AddCondition(), Confidence, "greater or equal", "3");   // English on purpose: a value the code compares.
                    },
                    Hint = "tutorial.deeperMoves.line.hint",
                },
                MoveStep(new[] { "tutorial.deeperMoves.move.title", "tutorial.deeperMoves.move.body", "tutorial.deeperMoves.move.more", "tutorial.deeperMoves.move.hint" }, vm => (NodeActionTypes.MoveGameObject, TutorialsDeep.PosterName),
                         (a, vm) => { Set(a, "relative", "true"); Set(a, "y", "0.3"); Set(a, "seconds", "1"); }),
                MoveStep(new[] { "tutorial.deeperMoves.spin.title", "tutorial.deeperMoves.spin.body", "tutorial.deeperMoves.spin.more", "tutorial.deeperMoves.spin.hint" }, vm => (NodeActionTypes.SpinGameObject, StoryPack.VaseName),
                         (a, vm) => { Set(a, "enable", "true"); Set(a, "speed", "45"); }),
                MoveStep(new[] { "tutorial.deeperMoves.fadeOut.title", "tutorial.deeperMoves.fadeOut.body", "tutorial.deeperMoves.fadeOut.more", "tutorial.deeperMoves.fadeOut.hint" }, vm => (NodeActionTypes.FadeSprite, TutorialsDeep.PosterName),
                         (a, vm) => { Set(a, "to", "0"); Set(a, "seconds", "0.5"); }),
                new TutorialStep
                {
                    Title = "tutorial.deeperMoves.wait.title",
                    Body = "tutorial.deeperMoves.wait.body",
                    More = "tutorial.deeperMoves.wait.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowWatchLine(vm),
                    IsDone = (vm, s) => ShowLineOf(vm) is { } n && n.Model.ActionsOnFinish.Any(a => a.Type == NodeActionTypes.Wait),
                    Solve = (vm, s) =>
                    {
                        ShowWatchLine(vm);
                        var a = vm.SelectedNode!.AddActionOnFinish();
                        a.DisplayType = NodeActionTypes.Wait;
                        Set(a, "seconds", "0.5");
                    },
                    Hint = "tutorial.deeperMoves.wait.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperMoves.swap.title",
                    Body = "tutorial.deeperMoves.swap.body",
                    More = "tutorial.deeperMoves.swap.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowWatchLine(vm),
                    IsDone = (vm, s) => ShowLineOf(vm) is { } n && n.Model.ActionsOnFinish.Any(a => a.Type == NodeActionTypes.SetSprite &&
                                                                                                    P(a, "sprite").Length > 0),
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        ShowWatchLine(vm);
                        var a = vm.SelectedNode!.AddActionOnFinish();
                        a.DisplayType = NodeActionTypes.SetSprite;
                        a.Category = NodeActionViewModel.CatOverlay;
                        a.OverlayLevel = GalleryToken(vm);
                        a.Target = TutorialsDeep.PosterName;
                        Set(a, "sprite", TutorialAssets.Poster2);
                    },
                    Hint = "tutorial.deeperMoves.swap.hint",
                },
                MoveStep(new[] { "tutorial.deeperMoves.fadeIn.title", "tutorial.deeperMoves.fadeIn.body", "tutorial.deeperMoves.fadeIn.more", "tutorial.deeperMoves.fadeIn.hint" }, vm => (NodeActionTypes.FadeSprite, TutorialsDeep.PosterName),
                         (a, vm) => { Set(a, "to", "1"); Set(a, "seconds", "0.5"); },
                         done: n => n.Model.ActionsOnFinish.Count(a => a.Type == NodeActionTypes.FadeSprite) >= 2),
                MoveStep(new[] { "tutorial.deeperMoves.zoom.title", "tutorial.deeperMoves.zoom.body", "tutorial.deeperMoves.zoom.more", "tutorial.deeperMoves.zoom.hint" }, vm => (NodeActionTypes.TransformGameObject, TutorialsDeep.PosterName),
                         (a, vm) => { Set(a, "scale", "1.2"); Set(a, "seconds", "1"); }),
                new TutorialStep
                {
                    Title = "tutorial.deeperMoves.tidy.title",
                    Body = "tutorial.deeperMoves.tidy.body",
                    More = "tutorial.deeperMoves.tidy.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleActionsBox",
                    AlsoAllow = new[] { "field:ruleActions" },
                    OnShown = vm => { if (Goodbye(vm) is { } r) vm.SelectedIntegrationRule = r; },
                    IsDone = (vm, s) => Goodbye(vm) is { } r && r.Model.Actions.Any(a => a.Type == NodeActionTypes.StopEffect) &&
                                        r.Model.Actions.Any(a => a.Type == NodeActionTypes.DeactivateAllScenes) &&
                                        r.Model.Actions.Any(a => a.Type == NodeActionTypes.SetGameObjectActive && P(a, "active") == "false"),
                    Solve = (vm, s) =>
                    {
                        var r = Goodbye(vm)!;
                        vm.SelectedIntegrationRule = r;
                        var stop = r.AddAction();
                        stop.DisplayType = NodeActionTypes.StopEffect;
                        Set(stop, Shared.Effects.EffectParam, HeartsKey(vm));
                        r.AddAction().DisplayType = NodeActionTypes.DeactivateAllScenes;
                        if (TutorialsDeep.Portfolio(vm) is { } folio)
                        {
                            var close = r.AddAction();
                            close.DisplayType = NodeActionTypes.SetGameObjectActive;
                            close.Category = NodeActionViewModel.CatUi;
                            close.Target = folio.Model.Id;
                            close.Active = false;
                        }
                    },
                    Hint = "tutorial.deeperMoves.tidy.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperMoves.wrap.title",
                    Body = "tutorial.deeperMoves.wrap.body",
                    More = "tutorial.deeperMoves.wrap.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    OnShown = vm => ShowWatchLine(vm),
                },
            },
        },

        // ═══ Lists, chance and other conversations ═══════════════════════════════
        new TutorialDef
        {
            Id = "deeper-lists",
            Group = Group,
            Title = "tutorial.deeperLists.title",
            Summary = "tutorial.deeperLists.summary",
            Makes = "tutorial.deeperLists.makes",
            ParentId = "remembering",
            Level = 30,
            Leaves = vm => vm.IntegrationRules.Any(r => r.Model.Actions.Any(a => a.Type == NodeActionTypes.CountList)),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperLists.story.title",
                    Body = "tutorial.deeperLists.story.body",
                    More = "tutorial.deeperLists.story.more",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLists.vars.title",
                    Body = "tutorial.deeperLists.vars.body",
                    More = "tutorial.deeperLists.vars.more",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    IsDone = (vm, s) => Variable(vm, FavouritePose) is { Model.Type: PackVariableType.String } &&
                                        Variable(vm, PoseCount) is { Model.Type: PackVariableType.Int },
                    Solve = (vm, s) =>
                    {
                        if (Variable(vm, FavouritePose) == null)
                        {
                            vm.AddVariableCommand.Execute(null);
                            vm.SelectedVariable!.Name = FavouritePose;
                            vm.SelectedVariable.Type = PackVariableType.String;
                        }
                        if (Variable(vm, PoseCount) == null)
                        {
                            vm.AddVariableCommand.Execute(null);
                            vm.SelectedVariable!.Name = PoseCount;
                            vm.SelectedVariable.Type = PackVariableType.Int;
                            vm.SelectedVariable.DefaultValue = "0";
                        }
                    },
                    Hint = "tutorial.deeperLists.vars.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLists.pick.title",
                    Body = "tutorial.deeperLists.pick.body",
                    More = "tutorial.deeperLists.pick.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "field:triggerMode", "panel:ruleActionsBox", "field:ruleActions", "panel:ruleIdentity" },
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => r.Model.Actions.Any(a => a.Type == NodeActionTypes.PickRandomFromList)),
                    Solve = (vm, s) =>
                    {
                        // English on purpose: the story pack's words.
                        var r = NewRule(vm, "Her favourite", UpdateRuleTriggerMode.OnSceneLoad);
                        var a = r.AddAction();
                        a.DisplayType = NodeActionViewModel.VariableFamilyType;
                        a.VarOperation = NodeActionViewModel.OpRandomFromList;
                        a.VarName = FavouritePose;
                        a.VarFromList = PosesSeen;
                    },
                    Hint = "tutorial.deeperLists.pick.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLists.count.title",
                    Body = "tutorial.deeperLists.count.body",
                    More = "tutorial.deeperLists.count.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleActionsBox",
                    AlsoAllow = new[] { "field:ruleActions" },
                    OnShown = vm => { if (RuleNamed(vm, "Her favourite") is { } r) vm.SelectedIntegrationRule = r; },   // English on purpose: the story pack's words.
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => r.Model.Actions.Any(a => a.Type == NodeActionTypes.CountList)),
                    Solve = (vm, s) =>
                    {
                        var r = RuleNamed(vm, "Her favourite") ?? vm.IntegrationRules[^1];   // English on purpose: the story pack's words.
                        vm.SelectedIntegrationRule = r;
                        var a = r.AddAction();
                        a.DisplayType = NodeActionViewModel.VariableFamilyType;
                        a.VarOperation = NodeActionViewModel.OpCountList;
                        a.VarName = PoseCount;
                        a.VarFromList = PosesSeen;
                    },
                    Hint = "tutorial.deeperLists.count.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLists.say.title",
                    Body = "tutorial.deeperLists.say.body",
                    More = "tutorial.deeperLists.say.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    AlsoAllow = new[] { "panel:nodeEditor", "panel:dialogueList", "panel:nodeConditions" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, VaseLine),
                    IsDone = (vm, s) => HerLines(vm).Any(n => n.Text.Contains("[PV:" + FavouritePose + "]", StringComparison.Ordinal)),
                    Solve = (vm, s) =>
                    {
                        var d = TutorialsPart2.HerSecret(vm)!;
                        vm.SelectedDialogue = d;
                        vm.SelectedNode = VaseLine(d) ?? d.Nodes[0];
                        vm.AddDialogueSiblingNodeCommand.Execute(null);
                        var n = vm.SelectedNode!;
                        n.Actor = TutorialsPart2.Speaker(vm);
                        n.Outfit = "";
                        // English on purpose: the story pack's words.
                        n.Text = "My favorite pose today: [PV:" + FavouritePose + "]. That's [PV:" + PoseCount + "] poses so far.";
                        var once = n.AddCondition();
                        once.DisplayType = NodeConditionTypes.ListCount;
                        Set(once, "list", PosesSeen);
                        Set(once, "comparison", "greater or equal");   // English on purpose: a value the code compares.
                        Set(once, "value", "1");
                    },
                    Hint = "tutorial.deeperLists.say.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLists.remove.title",
                    Body = "tutorial.deeperLists.remove.body",
                    More = "tutorial.deeperLists.remove.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleActionsBox",
                    AlsoAllow = new[] { "field:ruleActions" },
                    OnShown = vm => { if (RuleNamed(vm, "Rainy days") is { } r) vm.SelectedIntegrationRule = r; },   // English on purpose: the story pack's words.
                    IsDone = (vm, s) => vm.IntegrationRules.Any(r => r.Model.Actions.Any(a => a.Type == NodeActionTypes.RemoveFromList && Is(P(a, "list"), PosesSeen))),
                    Solve = (vm, s) =>
                    {
                        var r = RuleNamed(vm, "Rainy days") ?? vm.IntegrationRules[^1];   // English on purpose: the story pack's words.
                        vm.SelectedIntegrationRule = r;
                        var a = r.AddAction();
                        a.DisplayType = NodeActionTypes.RemoveFromList;
                        Set(a, "list", PosesSeen);
                        Set(a, "value", "Bold");   // English on purpose: a value the pack keeps.
                    },
                    Hint = "tutorial.deeperLists.remove.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLists.dice.title",
                    Body = "tutorial.deeperLists.dice.body",
                    More = "tutorial.deeperLists.dice.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, TutorialsPart2.ClosingLineOf),
                    IsDone = (vm, s) => HerLines(vm).Any(n => n.Model.ActionsOnFinish.Any(a => a.Type == NodeActionTypes.DiceRoll &&
                                                                                                  a.Branches.Count >= 2)),
                    Solve = (vm, s) =>
                    {
                        TutorialsPart2.ShowHerLine(vm, TutorialsPart2.ClosingLineOf);
                        var a = vm.SelectedNode!.AddActionOnFinish();
                        a.DisplayType = NodeActionTypes.DiceRoll;
                        var hearts = a.DiceBranches[0].Action;
                        hearts.DisplayType = NodeActionTypes.PlayEffect;
                        Set(hearts, Shared.Effects.EffectParam, HeartsKey(vm));
                        var creak = a.DiceBranches[1].Action;
                        creak.DisplayType = NodeActionTypes.PlaySFX;
                        Set(creak, "clip", (vm.Sfx.Count > 0 ? vm.Sfx[^1].Key : ""));
                    },
                    Hint = "tutorial.deeperLists.dice.hint",
                },
                TestLineStep(new[] { "tutorial.deeperLists.clear.title", "tutorial.deeperLists.clear.body", "tutorial.deeperLists.clear.more", "tutorial.deeperLists.clear.hint" }, (vm, n) => n.Model.ActionsOnFinish.Any(a => a.Type == NodeActionTypes.ClearList),
                             (vm, n) =>
                             {
                                 var a = n.AddActionOnFinish();
                                 a.DisplayType = NodeActionTypes.ClearList;
                                 Set(a, "list", PosesSeen);
                             }),
                TestLineStep(new[] { "tutorial.deeperLists.play.title", "tutorial.deeperLists.play.body", "tutorial.deeperLists.play.more", "tutorial.deeperLists.play.hint" }, (vm, n) => n.Model.ActionsOnFinish.Any(a => a.Type == NodeActionTypes.PlayDialogue && P(a, "dialogue").Length > 0),
                             (vm, n) =>
                             {
                                 var a = n.AddActionOnFinish();
                                 a.DisplayType = NodeActionTypes.PlayDialogue;
                                 Set(a, "dialogue", TutorialsPart2.HerSecret(vm)!.Key);
                             }),
                TestLineStep(new[] { "tutorial.deeperLists.focus.title", "tutorial.deeperLists.focus.body", "tutorial.deeperLists.focus.more", "tutorial.deeperLists.focus.hint" }, (vm, n) => n.Model.ActionsOnStart.Any(a => a.Type == NodeActionTypes.CharacterFocus),
                             (vm, n) =>
                             {
                                 var show = n.AddActionOnStart();
                                 show.DisplayType = NodeActionTypes.SetGameObjectActive;
                                 show.Category = NodeActionViewModel.CatScene;
                                 show.Target = vm.Scenes.FirstOrDefault(sc => sc.DisplayName == TutorialsDeep.ChaiseName)?.Key ?? vm.Scenes[^1].Key;
                                 show.Active = true;
                                 var focus = n.AddActionOnStart();
                                 focus.DisplayType = NodeActionTypes.CharacterFocus;
                                 Set(focus, "focused", "true");
                             }, onStart: true),
                new TutorialStep
                {
                    Title = "tutorial.deeperLists.leave.title",
                    Body = "tutorial.deeperLists.leave.body",
                    More = "tutorial.deeperLists.leave.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowWalkLine(vm, WalkThere),
                    IsDone = (vm, s) => WalkLine(vm, WalkThere) is { } n && n.Model.ActionsOnFinish.Any(a => a.Type == NodeActionTypes.LeaveBust),
                    Solve = (vm, s) =>
                    {
                        ShowWalkLine(vm, WalkThere);
                        var a = vm.SelectedNode!.AddActionOnFinish();
                        a.DisplayType = NodeActionTypes.LeaveBust;
                        Set(a, "actor", TutorialsPart2.Speaker(vm));
                    },
                    Hint = "tutorial.deeperLists.leave.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperLists.wrap.title",
                    Body = "tutorial.deeperLists.wrap.body",
                    More = "tutorial.deeperLists.wrap.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleActionsBox",
                },
            },
        },

        // ═══ More voices: characters of other kinds ══════════════════════════════
        new TutorialDef
        {
            Id = "deeper-voices",
            Group = Group,
            Title = "tutorial.deeperVoices.title",
            Summary = "tutorial.deeperVoices.summary",
            Makes = "tutorial.deeperVoices.makes",
            ParentId = "a-face-that-moves",
            Level = 31,
            Leaves = vm => vm.Characters.Any(c => !c.IsPlayer && !c.Model.IsVanillaCharacter && c.BustSource == BustSource.Vanilla),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperVoices.story.title",
                    Body = "tutorial.deeperVoices.story.body",
                    More = "tutorial.deeperVoices.story.more",
                    Kind = StepKind.Read,
                    Tab = MainTab.Characters,
                    Anchor = "panel:characterTree",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperVoices.voice.title",
                    Body = "tutorial.deeperVoices.voice.body",
                    More = "tutorial.deeperVoices.voice.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Characters,
                    Anchor = "panel:characterTree",
                    AlsoAllow = new[] { "field:characterName", "field:pronouns", "field:nameColour" },
                    IsDone = (vm, s) => Announcer(vm) is { } a && a.Pronouns != Pronouns.Unset,
                    Solve = (vm, s) =>
                    {
                        vm.AddVoiceCharacterCommand.Execute(null);
                        var c = vm.SelectedCharacter!;
                        c.DisplayName = AnnouncerName;
                        c.Pronouns = Pronouns.Neutral;
                        c.NameColor = "#E8C25A";
                    },
                    Hint = "tutorial.deeperVoices.voice.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperVoices.speaks.title",
                    Body = "tutorial.deeperVoices.speaks.body",
                    More = "tutorial.deeperVoices.speaks.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    AlsoAllow = new[] { "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowWalkLine(vm, WalkAsk),
                    IsDone = (vm, s) => Announcer(vm) is { } a && WalkHome(vm) is { } d && d.Nodes.Any(n => n.Actor == a.Key),
                    Solve = (vm, s) =>
                    {
                        ShowWalkLine(vm, WalkAsk);
                        vm.AddDialogueSiblingNodeCommand.Execute(null);
                        vm.SelectedNode!.Actor = Announcer(vm)!.Key;
                        vm.SelectedNode.Text = "The gallery will close in five minutes.";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.deeperVoices.speaks.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperVoices.borrowed.title",
                    Body = "tutorial.deeperVoices.borrowed.body",
                    More = "tutorial.deeperVoices.borrowed.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Characters,
                    Anchor = "panel:characterTree",
                    AlsoAllow = new[] { "field:characterName", "field:pronouns", "panel:outfitSprites", "btn:addOutfit", "field:bustSource", "field:outfitName" },
                    IsDone = (vm, s) => vm.Characters.Any(c => !c.IsPlayer && !c.Model.IsVanillaCharacter && c.BustSource == BustSource.Vanilla &&
                                                               c.Pronouns != Pronouns.Unset && c.Outfits.Any(o => o.GameObjectName.Length > 0)),
                    Solve = (vm, s) =>
                    {
                        vm.AddCharacterCommand.Execute(null);
                        var c = vm.SelectedCharacter!;
                        c.DisplayName = OwnerName;
                        c.Pronouns = Pronouns.Female;
                        c.BustSource = BustSource.Vanilla;
                        if (c.Outfits.Count == 0) vm.AddVanillaOutfitCommand.Execute(null);
                        c.Outfits[0].GameObjectName = OwnerBust;
                        // What typing the name in the box does: her Default outfit follows it, and the outfit is the one
                        // shown - set here directly, the Default stayed on the old name and the preview black (review
                        // of every step, 2026-11-22).
                        c.DefaultOutfit = OwnerBust;
                        vm.SelectedOutfit = c.Outfits[0];
                    },
                    Hint = "tutorial.deeperVoices.borrowed.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperVoices.owner.title",
                    Body = "tutorial.deeperVoices.owner.body",
                    More = "tutorial.deeperVoices.owner.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    AlsoAllow = new[] { "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => { if (WalkHome(vm) is { } d) vm.SelectedDialogue = d; },
                    IsDone = (vm, s) => Owner(vm) is { } o && WalkHome(vm) is { } d && d.Nodes.Any(n => n.Actor == o.Key),
                    Solve = (vm, s) =>
                    {
                        var d = WalkHome(vm)!;
                        vm.SelectedDialogue = d;
                        var after = Announcer(vm) is { } a ? d.Nodes.FirstOrDefault(n => n.Actor == a.Key) : null;
                        vm.SelectedNode = after ?? d.Nodes[0];
                        vm.AddDialogueSiblingNodeCommand.Execute(null);
                        vm.SelectedNode!.Actor = Owner(vm)!.Key;
                        vm.SelectedNode.Text = "Take your time. Her poster stays up all night.";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.deeperVoices.owner.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperVoices.games.title",
                    Body = "tutorial.deeperVoices.games.body",
                    More = "tutorial.deeperVoices.games.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Characters,
                    Anchor = "panel:characterTree",
                    AlsoAllow = new[] { "field:nameColour" },
                    IsDone = (vm, s) => GameCop(vm) is { } c && c.NameColor.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        var c = GameCop(vm)!;
                        vm.SelectedCharacter = c;
                        c.NameColor = "#5B8CFF";
                    },
                    Hint = "tutorial.deeperVoices.games.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperVoices.wrap.title",
                    Body = "tutorial.deeperVoices.wrap.body",
                    More = "tutorial.deeperVoices.wrap.more",
                    Kind = StepKind.Read,
                    Tab = MainTab.Characters,
                    Anchor = "panel:characterTree",
                },
            },
        },

        // ═══ Screens and quests, more ════════════════════════════════════════════
        new TutorialDef
        {
            Id = "deeper-journal",
            Group = Group,
            Title = "tutorial.deeperJournal.title",
            Summary = "tutorial.deeperJournal.summary",
            Makes = "tutorial.deeperJournal.makes",
            ParentId = "a-quest",
            Level = 32,
            Leaves = vm => vm.Quests.Any(q => q.Model.Source == GabrielQuest),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperJournal.story.title",
                    Body = "tutorial.deeperJournal.story.body",
                    More = "tutorial.deeperJournal.story.more",
                    Kind = StepKind.Read,
                    Tab = MainTab.Ui,
                    Anchor = "btn:addUiExtension",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperJournal.badge.title",
                    Body = "tutorial.deeperJournal.badge.body",
                    More = "tutorial.deeperJournal.badge.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Ui,
                    Anchor = "btn:addUiExtension",
                    AlsoAllow = new[] { "panel:uiDetail", "tree:uiObjects", "panel:uiObjectDetail", "preview:ui" },
                    IsDone = (vm, s) => vm.Uis.Any(u => u.StartsOpen && u.Model.Nodes.Count > 0),
                    Solve = (vm, s) =>
                    {
                        vm.AddOwnUiCommand.Execute(null);
                        var ui = vm.SelectedUi!;
                        ui.Name = "Her confidence";   // English on purpose: the story pack's words.
                        ui.StartsOpen = true;
                        var panel = ui.Nodes[0];
                        panel.Width = 320;
                        panel.Height = 60;
                        panel.StickTo(0, 1);
                        ui.SelectedNode = panel;
                        ui.AddChildCommand.Execute(null);
                        if (ui.SelectedNode is { } node) node.Text = "Confidence: [PV:" + Confidence + "]";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.deeperJournal.badge.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperJournal.counted.title",
                    Body = "tutorial.deeperJournal.counted.body",
                    More = "tutorial.deeperJournal.counted.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Quests,
                    Anchor = "panel:questDetail",
                    AlsoAllow = new[] { "btn:addQuestTask", "panel:questTaskCompletion" },
                    IsDone = (vm, s) => HerQuest(vm) is { } q && q.Model.Tasks.Any(t => t.CountsFromVariable && Is(t.CountVariable ?? "", VisitsToday)),
                    Solve = (vm, s) =>
                    {
                        var q = HerQuest(vm)!;
                        vm.SelectedQuest = q;
                        q.SelectedTask = null;
                        q.AddTaskCommand.Execute(null);
                        var t = q.SelectedTask!;
                        t.Name = "Visit her three times in one day.";   // English on purpose: the story pack's words.
                        t.Counts = true;
                        t.CountToText = "3";
                        t.CounterSource = QuestTaskViewModel.CountedByVariable;
                        t.CountVariable = VisitsToday;
                    },
                    Hint = "tutorial.deeperJournal.counted.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperJournal.counter.title",
                    Body = "tutorial.deeperJournal.counter.body",
                    More = "tutorial.deeperJournal.counter.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnShown = vm => ShowLine(vm, TwiceLine),
                    IsDone = (vm, s) => LineAsks(vm, TwiceLine, c => c.Type == NodeConditionTypes.QuestCounter),
                    Solve = (vm, s) =>
                    {
                        var c = NewLineWithCondition(vm, TwiceLine);
                        c.DisplayType = NodeConditionTypes.QuestCounter;
                        var q = HerQuest(vm)!;
                        c.QuestPicker.QuestKey = q.Model.Key;
                        c.QuestPicker.TaskKey = q.Model.Tasks.Last(t => t.CountsFromVariable).Key;
                        Set(c, Shared.QuestVocabulary.ComparisonParam, "greater or equal");   // English on purpose: a value the code compares.
                        Set(c, "value", "1");
                    },
                    Hint = "tutorial.deeperJournal.counter.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperJournal.gameQuest.title",
                    Body = "tutorial.deeperJournal.gameQuest.body",
                    More = "tutorial.deeperJournal.gameQuest.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Quests,
                    Anchor = "panel:questDetail",
                    AlsoAllow = new[] { "panel:questTaskCompletion", "panel:vanillaQuests" },
                    IsDone = (vm, s) => vm.Quests.Any(q => q.Model.Source == GabrielQuest && q.Model.VanillaTasks.Any(h => h.Actions.Count > 0)),
                    Solve = (vm, s) =>
                    {
                        vm.AddVanillaQuestCommand.Execute(null);
                        var q = vm.SelectedQuest!;
                        q.Source = GabrielQuest;
                        var row = q.VanillaTaskRows.First(r => r.Name.Trim() == "Meet Gabriel.");   // English on purpose: the story pack's words.
                        var a = row.Actions.Add();
                        a.DisplayType = NodeActionViewModel.VariableFamilyType;
                        a.VarOperation = "Increment";   // English on purpose: a value the code compares.
                        a.VarName = Confidence;
                        a.VarDelta = "1";
                    },
                    Hint = "tutorial.deeperJournal.gameQuest.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperJournal.wrap.title",
                    Body = "tutorial.deeperJournal.wrap.body",
                    More = "tutorial.deeperJournal.wrap.more",
                    Kind = StepKind.Read,
                    Tab = MainTab.Quests,
                    Anchor = "panel:questDetail",
                },
            },
        },

        // ═══ Places, more ════════════════════════════════════════════════════════
        new TutorialDef
        {
            Id = "deeper-rooms",
            Group = Group,
            Title = "tutorial.deeperRooms.title",
            Summary = "tutorial.deeperRooms.summary",
            Makes = "tutorial.deeperRooms.makes",
            ParentId = "a-place",
            Level = 33,
            Leaves = vm => GalleryObjects(vm).Any(o => o.Light is { IsPicture: true }),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperRooms.story.title",
                    Body = "tutorial.deeperRooms.story.body",
                    More = "tutorial.deeperRooms.story.more",
                    Kind = StepKind.Read,
                    Tab = MainTab.Places,
                    Anchor = "panel:placePreview",
                    OnEnter = (vm, s) => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRooms.weather.title",
                    Body = "tutorial.deeperRooms.weather.body",
                    More = "tutorial.deeperRooms.weather.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Places,
                    Anchor = "field:weather",
                    AlsoAllow = new[] { "panel:placeList" },
                    OnEnter = (vm, s) => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                    IsDone = (vm, s) => StoryPack.Gallery(vm) is { WeatherType: WeatherType.Inside },
                    Solve = (vm, s) => StoryPack.Gallery(vm)!.WeatherType = WeatherType.Inside,
                    Hint = "tutorial.deeperRooms.weather.hint",
                },
                RoomStep("sign", vm => GalleryObjects(vm).Any(o => o.Model.Components.Any(c => c.Type == PackComponentType.BlinkingSprite)),
                         vm =>
                         {
                             var o = StoryPack.Gallery(vm)!.AddGameObject();
                             o.Name = "Star sign";   // English on purpose: an object's name.
                             o.Sprite = TutorialAssets.NeonStar;
                             o.X = 3.2f; o.Y = 2.2f;
                             o.SortingOrder = -11;
                             o.AddComponent().Type = PackComponentType.BlinkingSprite;
                         }),
                RoomStep("float", vm => GalleryObjects(vm).Any(o => o.Model.Components.Any(c => c.Type == PackComponentType.FloatingSprite)),
                         vm =>
                         {
                             var o = StoryPack.Gallery(vm)!.AddGameObject();
                             o.Name = "Floating vase";   // English on purpose: an object's name.
                             o.Sprite = TutorialAssets.Prop;
                             o.X = 1.2f; o.Y = -1.0f;
                             o.SortingOrder = -10;
                             o.AddComponent().Type = PackComponentType.FloatingSprite;
                         }),
                RoomStep("flowers", vm => GalleryObjects(vm).Any(o => o.Model.Components.Any(c => c.Type == PackComponentType.RandomChildActivator) &&
                                                                      o.Model.Children.Count >= 2),
                         vm =>
                         {
                             var pots = StoryPack.Gallery(vm)!.AddGameObject();
                             pots.Name = "Flowers";   // English on purpose: an object's name.
                             pots.X = -3.4f; pots.Y = -3.6f;
                             foreach (var (name, art) in TutorialAssets.Flowers)
                             {
                                 var pot = pots.AddChild();
                                 pot.Name = name;
                                 pot.Sprite = art;
                                 pot.SortingOrder = -9;
                                 // An object of the pack's stands at its own X and Y in the place, a child too.
                                 pot.X = -3.4f; pot.Y = -3.6f;
                             }
                             pots.AddComponent().Type = PackComponentType.RandomChildActivator;
                         }),
                RoomStep("copy", vm => GalleryObjects(vm).Any(o => o.CopyOf.Trim().Length > 0),
                         vm =>
                         {
                             var o = StoryPack.Gallery(vm)!.AddGameObject();
                             o.Name = "Loving sparkles";   // English on purpose: an object's name.
                             o.CopyOf = "6_Effects/CFXR2 Expression Loving (Timed)";   // English on purpose: the game's path.
                             o.X = 0f; o.Y = 1.5f;
                         }),
                RoomStep("freeLight", vm => GalleryObjects(vm).Any(o => o.Light is { IsFree: true }),
                         vm =>
                         {
                             var g = StoryPack.Gallery(vm)!;
                             vm.SelectedPlace = g;
                             g.AddLightCommand.Execute(null);
                             var holder = g.GameObjects.Last(o => o.HasLight);
                             holder.Name = "Floor glow";   // English on purpose: an object's name.
                             holder.Light!.Shape = LightDef.ShapeFree;
                             holder.Light.Color = "#9AB8FF";
                         }, anchor: "btn:addLight"),
                RoomStep("pictureLight", vm => GalleryObjects(vm).Any(o => o.Light is { IsPicture: true }),
                         vm =>
                         {
                             var g = StoryPack.Gallery(vm)!;
                             vm.SelectedPlace = g;
                             g.AddLightCommand.Execute(null);
                             var holder = g.GameObjects.Last(o => o.HasLight);
                             holder.Name = "Moon window";   // English on purpose: an object's name.
                             holder.Light!.Shape = LightDef.ShapePicture;
                             holder.Light.Picture = TutorialAssets.WindowLight;
                         }, anchor: "btn:addLight"),
                new TutorialStep
                {
                    Title = "tutorial.deeperRooms.wet.title",
                    Body = "tutorial.deeperRooms.wet.body",
                    More = "tutorial.deeperRooms.wet.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Npcs,
                    Anchor = "panel:npcWet",
                    OnEnter = (vm, s) => { if (vm.Npcs.FirstOrDefault(n => Is(n.DisplayName, TutorialsDeep.VisitorName)) is { } n) vm.SelectedNpc = n; },
                    IsDone = (vm, s) => vm.Npcs.Any(n => Is(n.DisplayName, TutorialsDeep.VisitorName) && n.WetEnabled),
                    Solve = (vm, s) =>
                    {
                        var n = vm.Npcs.First(x => Is(x.DisplayName, TutorialsDeep.VisitorName));
                        vm.SelectedNpc = n;
                        n.WetEnabled = true;
                        n.WetStartActive = true;
                    },
                    Hint = "tutorial.deeperRooms.wet.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRooms.fades.title",
                    Body = "tutorial.deeperRooms.fades.body",
                    More = "tutorial.deeperRooms.fades.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Places,
                    Anchor = "panel:placeSettings",
                    AlsoAllow = new[] { "panel:placePreview" },
                    OnEnter = (vm, s) => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                    // Her poster first: its components are beside the picture once it is picked.
                    OnShown = vm =>
                    {
                        if (GalleryObjects(vm).FirstOrDefault(o => Is(o.Name, TutorialsDeep.PosterName)) is { } poster) vm.PickInPreview(poster);
                    },
                    IsDone = (vm, s) => GalleryObjects(vm).Any(o => Is(o.Name, TutorialsDeep.PosterName) &&
                                                                    o.Model.Components.Any(c => c.Type == PackComponentType.FadeInSprite)) &&
                                        VisitorPlacement(vm) is { } v && v.Model.Components.Any(c => c.Type == PackComponentType.FadeOutSprite),
                    Solve = (vm, s) =>
                    {
                        var poster = GalleryObjects(vm).First(o => Is(o.Name, TutorialsDeep.PosterName));
                        poster.AddComponent().Type = PackComponentType.FadeInSprite;
                        var visitor = VisitorPlacement(vm)!;
                        var fade = visitor.AddComponent();
                        fade.Type = PackComponentType.FadeOutSprite;
                        fade.StartDelay = 20;
                        fade.Duration = 2;
                        fade.DeactivateOnComplete = true;
                        visitor.Model.DeactivateWhenUnmet = false;
                    },
                    Hint = "tutorial.deeperRooms.fades.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperRooms.wrap.title",
                    Body = "tutorial.deeperRooms.wrap.body",
                    More = "tutorial.deeperRooms.wrap.more",
                    Kind = StepKind.Read,
                    Tab = MainTab.Places,
                    Anchor = "panel:placePreview",
                    OnEnter = (vm, s) => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                },
            },
        },

        // ═══ Effects, more ═══════════════════════════════════════════════════════
        new TutorialDef
        {
            Id = "deeper-sparkle",
            Group = Group,
            Title = "tutorial.deeperSparkle.title",
            Summary = "tutorial.deeperSparkle.summary",
            Makes = "tutorial.deeperSparkle.makes",
            ParentId = "effects",
            Level = 34,
            Leaves = vm => vm.Effects.Any(e => e.ChangesGame),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.deeperSparkle.story.title",
                    Body = "tutorial.deeperSparkle.story.body",
                    More = "tutorial.deeperSparkle.story.more",
                    Kind = StepKind.Read,
                    Tab = MainTab.Fx,
                    Anchor = "panel:effectDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperSparkle.glow.title",
                    Body = "tutorial.deeperSparkle.glow.body",
                    More = "tutorial.deeperSparkle.glow.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Fx,
                    Anchor = "btn:addEffect",
                    AlsoAllow = new[] { "panel:effectDetail" },
                    IsDone = (vm, s) => vm.Effects.Any(e => e.Glow && e.Model.Template == Shared.Effects.ForgePrefix + "rising"),
                    Solve = (vm, s) =>
                    {
                        vm.AddEffectCommand.Execute(null);
                        var e = vm.SelectedEffect!;
                        e.DisplayName = "Starlight";   // English on purpose: the story pack's words.
                        e.TemplateOption = EffectViewModel.TemplateOptions.First(o => o.Id == Shared.Effects.ForgePrefix + "rising");
                        e.Glow = true;
                        // Stars, not the template's own hearts: "Starlight" drew white hearts (review of every step, 2026-11-22).
                        e.GameTexture = EffectViewModel.GameTextureChoices.First(c => c.Value == "cfxr star");   // English on purpose: the game's own name for its picture.
                    },
                    Hint = "tutorial.deeperSparkle.glow.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperSparkle.starlit.title",
                    Body = "tutorial.deeperSparkle.starlit.body",
                    More = "tutorial.deeperSparkle.starlit.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Fx,
                    Anchor = "panel:effectDetail",
                    AlsoAllow = new[] { "btn:addEffect" },
                    OnShown = vm => { if (Effect(vm, StarlightName) is { } e) vm.SelectedEffect = e; },
                    IsDone = (vm, s) => Effect(vm, StarlightName) is { } e && e.Model.PlayWhen == Shared.Effects.WhileTrue &&
                                        e.Conditions.Any(c => c.Model.Type == NodeConditionTypes.LevelActive),
                    Solve = (vm, s) =>
                    {
                        var e = Effect(vm, StarlightName)!;
                        vm.SelectedEffect = e;
                        e.PlayWhen = EffectViewModel.PlayWhenChoices.First(c => c.Value == Shared.Effects.WhileTrue);
                        e.AddConditionCommand.Execute(null);
                        AtTheGallery(vm, e.Conditions[^1]);
                    },
                    Hint = "tutorial.deeperSparkle.starlit.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperSparkle.photos.title",
                    Body = "tutorial.deeperSparkle.photos.body",
                    More = "tutorial.deeperSparkle.photos.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Fx,
                    Anchor = "btn:addEffect",
                    AlsoAllow = new[] { "panel:effectDetail" },
                    IsDone = (vm, s) => vm.Effects.Any(e => e.Model.PlayWhen == Shared.Effects.WhenUnderTurnsOn && (e.Model.Under ?? "").Length > 0),
                    Solve = (vm, s) =>
                    {
                        vm.AddEffectCommand.Execute(null);
                        var e = vm.SelectedEffect!;
                        e.DisplayName = "Photo sparkle";   // English on purpose: the story pack's words.
                        e.TemplateOption = EffectViewModel.TemplateOptions.First(o => o.Id == Shared.Effects.VanillaPrefix + "sparkleRing");
                        e.PlayWhen = EffectViewModel.PlayWhenChoices.First(c => c.Value == Shared.Effects.WhenUnderTurnsOn);
                        e.Under = "4_CG_Manager-Photos";
                    },
                    Hint = "tutorial.deeperSparkle.photos.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperSparkle.proud.title",
                    Body = "tutorial.deeperSparkle.proud.body",
                    More = "tutorial.deeperSparkle.proud.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Fx,
                    Anchor = "btn:addEffect",
                    AlsoAllow = new[] { "panel:effectDetail" },
                    IsDone = (vm, s) => vm.Effects.Any(e => e.Model.PlayWhen == Shared.Effects.WhenTrue &&
                                                            e.Conditions.Any(c => Is(c.Model.Params.TryGetValue("name", out var n) ? n : "", TutorialsPart2.Confidence))),
                    Solve = (vm, s) =>
                    {
                        vm.AddEffectCommand.Execute(null);
                        var e = vm.SelectedEffect!;
                        e.DisplayName = "Proud of herself";   // English on purpose: the story pack's words.
                        e.TemplateOption = EffectViewModel.TemplateOptions.First(o => o.Id == Shared.Effects.ForgePrefix + "burst");
                        e.PlayWhen = EffectViewModel.PlayWhenChoices.First(c => c.Value == Shared.Effects.WhenTrue);
                        e.AddConditionCommand.Execute(null);
                        Compare(e.Conditions[^1], TutorialsPart2.Confidence, "greater or equal", "10");   // English on purpose: a value the code compares.
                    },
                    Hint = "tutorial.deeperSparkle.proud.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperSparkle.games.title",
                    Body = "tutorial.deeperSparkle.games.body",
                    More = "tutorial.deeperSparkle.games.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Fx,
                    Anchor = "btn:addEffect",
                    AlsoAllow = new[] { "panel:effectDetail" },
                    IsDone = (vm, s) => vm.Effects.Any(e => e.ChangesGame),
                    Solve = (vm, s) =>
                    {
                        vm.AddEffectCommand.Execute(null);
                        var e = vm.SelectedEffect!;
                        e.DisplayName = "Gold followers";   // English on purpose: the story pack's words.
                        e.TemplateOption = EffectViewModel.TemplateOptions.First(o => o.Id == Shared.Effects.FollowerPoints);
                        e.ChangesGame = true;
                        e.Color = "#FFC94A";
                    },
                    Hint = "tutorial.deeperSparkle.games.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperSparkle.scene.title",
                    Body = "tutorial.deeperSparkle.scene.body",
                    More = "tutorial.deeperSparkle.scene.more",
                    Kind = StepKind.Do,
                    Tab = MainTab.Scenes,
                    Anchor = "panel:gameScenes",
                    AlsoAllow = new[] { "panel:sceneDetail" },
                    IsDone = (vm, s) => vm.Scenes.Any(sc => sc.IsGameScene && !string.IsNullOrEmpty(sc.Model.Source) && sc.Sound == SceneSoundMode.Flash),
                    Solve = (vm, s) =>
                    {
                        vm.AddGameSceneCommand.Execute(null);
                        var sc = vm.SelectedGameScene!;
                        sc.GameSource = VanillaScenes.All.First(x => x.Photo);
                        sc.Sound = SceneSoundMode.Flash;
                    },
                    Hint = "tutorial.deeperSparkle.scene.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.deeperSparkle.wrap.title",
                    Body = "tutorial.deeperSparkle.wrap.body",
                    More = "tutorial.deeperSparkle.wrap.more",
                    Kind = StepKind.Read,
                    Tab = MainTab.Fx,
                    Anchor = "panel:effectDetail",
                },
            },
        },
    };

    // ── More voices ────────────────────────────────────────────────────────────

    // English on purpose: the story pack's names.
    private const string AnnouncerName = "Announcer", OwnerName = "Gallery owner";

    /// <summary>The game's bust the gallery owner wears: one of its own, of a character who never visits the gallery.</summary>
    private static string OwnerBust => (VanillaBusts.All.FirstOrDefault(b => b.Character == "Claudia") ?? VanillaBusts.All[0]).GoName;

    private static CharacterViewModel? Owner(MainViewModel vm)
        => vm.Characters.FirstOrDefault(c => !c.IsPlayer && !c.Model.IsVanillaCharacter && c.BustSource == BustSource.Vanilla &&
                                             Is(c.DisplayName, OwnerName));

    private static CharacterViewModel? Announcer(MainViewModel vm)
        => vm.Characters.FirstOrDefault(c => !c.IsPlayer && !c.Model.IsVanillaCharacter && c.BustSource == BustSource.None &&
                                             Is(c.DisplayName, AnnouncerName));

    /// <summary>One of the game's own cast, given a colour (a name colour is the gentlest change there is).</summary>
    private static CharacterViewModel? GameCop(MainViewModel vm)
        => vm.Characters.FirstOrDefault(c => c.Model.VanillaCharacter == "Cop") ?? vm.Characters.FirstOrDefault(c => c.Model.IsVanillaCharacter && !c.IsPlayer);

    // ── Screens and quests ─────────────────────────────────────────────────────

    /// <summary>The photographer's quest of the game ("A Trained Eye (Gabriel)"): only an action is added to it.</summary>
    // English on purpose: the game's own quest name.
    internal const string GabrielQuest = "A Trained Eye (Gabriel)";

    // English on purpose: the story pack's words.
    private const string TwiceLine = "Back again today? You must like it here.";

    private static QuestViewModel? HerQuest(MainViewModel vm)
        => vm.Quests.FirstOrDefault(q => q.Title.Trim() == TutorialsQuests.QuestTitle);

    // ── Places, more ───────────────────────────────────────────────────────────

    private static EffectViewModel? Effect(MainViewModel vm, string name) => vm.Effects.FirstOrDefault(e => Is(e.DisplayName, name));

    private static NpcPlacementViewModel? VisitorPlacement(MainViewModel vm)
        => vm.Npcs.FirstOrDefault(n => Is(n.DisplayName, TutorialsDeep.VisitorName)) is { } n
            ? StoryPack.Gallery(vm)?.NpcsNode.Npcs.FirstOrDefault(p => p.Npc == n.Key) : null;

    private static IEnumerable<GameObjectViewModel> GalleryObjects(MainViewModel vm)
    {
        if (StoryPack.Gallery(vm) is not { } g) yield break;
        var stack = new Stack<GameObjectViewModel>(g.GameObjects);
        while (stack.Count > 0)
        {
            var o = stack.Pop();
            yield return o;
            foreach (var c in o.Children) stack.Push(c);
        }
    }

    /// <summary>A step adding something to her gallery, on the Places tab.</summary>
    private static TutorialStep RoomStep(string name, Func<MainViewModel, bool> done, Action<MainViewModel> solve,
                                         string anchor = "btn:addObject")
        => new()
        {
            Title = name switch
            {
                "sign" => "tutorial.deeperRooms.sign.title", "float" => "tutorial.deeperRooms.float.title",
                "flowers" => "tutorial.deeperRooms.flowers.title",
                "copy" => "tutorial.deeperRooms.copy.title", "freeLight" => "tutorial.deeperRooms.freeLight.title",
                _ => "tutorial.deeperRooms.pictureLight.title",
            },
            Body = name switch
            {
                "sign" => "tutorial.deeperRooms.sign.body", "float" => "tutorial.deeperRooms.float.body",
                "flowers" => "tutorial.deeperRooms.flowers.body",
                "copy" => "tutorial.deeperRooms.copy.body", "freeLight" => "tutorial.deeperRooms.freeLight.body",
                _ => "tutorial.deeperRooms.pictureLight.body",
            },
            More = name switch
            {
                "sign" => "tutorial.deeperRooms.sign.more", "float" => "tutorial.deeperRooms.float.more",
                "flowers" => "tutorial.deeperRooms.flowers.more",
                "copy" => "tutorial.deeperRooms.copy.more", "freeLight" => "tutorial.deeperRooms.freeLight.more",
                _ => "tutorial.deeperRooms.pictureLight.more",
            },
            Hint = name switch
            {
                "sign" => "tutorial.deeperRooms.sign.hint", "float" => "tutorial.deeperRooms.float.hint",
                "flowers" => "tutorial.deeperRooms.flowers.hint",
                "copy" => "tutorial.deeperRooms.copy.hint", "freeLight" => "tutorial.deeperRooms.freeLight.hint",
                _ => "tutorial.deeperRooms.pictureLight.hint",
            },
            Kind = StepKind.Do,
            Tab = MainTab.Places,
            Anchor = anchor,
            AlsoAllow = new[] { "panel:placePreview", "btn:addObject", "btn:addLight" },
            OnEnter = (vm, s) => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
            IsDone = (vm, s) => done(vm),
            Solve = (vm, s) =>
            {
                TutorialAssets.EnsureCopied(vm.PackRoot);
                solve(vm);
            },
        };

    // ── Changing the game, gently ──────────────────────────────────────────────

    /// <summary>The player's beach in bad weather: one line of theirs, the game's (8_Room_Talk).</summary>
    internal const string BeachRain = "8_Room_Talk/Beach/BadWeather";

    /// <summary>A change to that conversation: stored under the catalog's token for it, not its path.</summary>
    private static bool IsBeachRain(DialogueViewModel d)
        => VanillaDialogueCatalog.Find(BeachRain) is { } e && string.Equals(d.Model.Source, e.Token, StringComparison.OrdinalIgnoreCase);

    /// <summary>The player's bedroom, one of the game's levels.</summary>
    private const string MyRoom = "vanilla:5_MyRoom";

    // English on purpose: the story pack's words.
    private const string BoldPoseRule = "Her bold pose";
    // English on purpose: the story pack's words.
    internal const string WalkHomeName = "Walk me home";
    // English on purpose: the story pack's words.
    private const string WalkAsk = "Walk me to your place?",
        WalkGo = "Let's go - before anyone sees.",
        WalkThere = "So this is your room. I should get back before they open.",
        WatchLine = "Watch the poster.";
    internal const string FavouritePose = "FavouritePose";
    internal const string PoseCount = "PoseCount";

    /// <summary>The game's strip of door buttons, which has a CanvasGroup: faded, not switched off.</summary>
    private const string Doors = "9_MainCanvas/Navigator";

    private static UpdateRuleViewModel? RuleNamed(MainViewModel vm, string name)
        => vm.IntegrationRules.FirstOrDefault(r => Is(r.DisplayName, name));

    /// <summary>"Goodbye", the rule for leaving her gallery (Rules that watch).</summary>
    private static UpdateRuleViewModel? Goodbye(MainViewModel vm)
        => Rules(vm, UpdateRuleTriggerMode.OnFallingEdge).FirstOrDefault(r => Flat(r.Model.Conditions).Any(c => AtGallery(c, vm)));

    private static void DimDoors(NodeActionViewModel a, string alpha, string seconds)
    {
        a.DisplayType = NodeActionTypes.SetComponentProperty;
        a.Category = NodeActionViewModel.CatPath;
        a.Target = Doors;
        Set(a, "component", "CanvasGroup");   // English on purpose: the component's name.
        Set(a, "property", "alpha");
        Set(a, "value", alpha);
        Set(a, "seconds", seconds);
    }

    private static bool DimsTheDoors(NodeActionDef a, bool below)
        => a.Type == NodeActionTypes.SetComponentProperty && Is(P(a, "property"), "alpha") &&
           (below ? Num(P(a, "value")) < 1 : Num(P(a, "value")) >= 1);

    private static DialogueViewModel? WalkHome(MainViewModel vm) => vm.Dialogues.FirstOrDefault(d => d.DisplayName.Trim() == WalkHomeName);

    private static DialogueNodeViewModel? WalkLine(MainViewModel vm, string text)
        => WalkHome(vm)?.Nodes.FirstOrDefault(n => n.Text.Trim() == text);

    private static void ShowWalkLine(MainViewModel vm, string text)
    {
        if (WalkHome(vm) is not { } d) return;
        if (!ReferenceEquals(vm.SelectedDialogue, d)) vm.SelectedDialogue = d;
        if (WalkLine(vm, text) is { } n) vm.SelectedNode = n;
    }

    // ── Her gallery moves ──────────────────────────────────────────────────────

    private static DialogueNodeViewModel? ShowLineOf(MainViewModel vm) => LineSaying(vm, WatchLine);

    private static void ShowWatchLine(MainViewModel vm) => ShowLine(vm, WatchLine);

    /// <summary>A step adding one object-moving action to her "Watch the poster." line.</summary>
    private static TutorialStep MoveStep(string[] keys, Func<MainViewModel, (string Type, string Target)> what,
                                         Action<NodeActionViewModel, MainViewModel> set,
                                         Func<DialogueNodeViewModel, bool>? done = null)
        => new()
        {
            Title = keys[0],
            Body = keys[1],
            More = keys[2],
            Kind = StepKind.Do,
            Tab = TabDialogues,
            Anchor = "panel:actionsOnFinishBox",
            AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
            OnShown = vm => ShowWatchLine(vm),
            IsDone = (vm, s) => ShowLineOf(vm) is { } n && (done?.Invoke(n) ?? n.Model.ActionsOnFinish.Any(a => a.Type == what(vm).Type)),
            Solve = (vm, s) =>
            {
                ShowWatchLine(vm);
                var (type, target) = what(vm);
                var a = vm.SelectedNode!.AddActionOnFinish();
                a.DisplayType = type;
                a.GoCategory = NodeActionViewModel.CatOverlay;
                a.GoOverlayLevel = GalleryToken(vm);
                a.GoTarget = target;
                set(a, vm);
            },
            Hint = keys[3],
        };

    // ── Lists, chance and other conversations ──────────────────────────────────

    private static DialogueViewModel? TestTalk(MainViewModel vm) => vm.Dialogues.FirstOrDefault(d => d.DisplayName.Trim() == TestConversation);

    /// <summary>A step adding an action to the test conversation's line (Her luck): a place to try things.</summary>
    private static TutorialStep TestLineStep(string[] keys, Func<MainViewModel, DialogueNodeViewModel, bool> done,
                                             Action<MainViewModel, DialogueNodeViewModel> solve, bool onStart = false)
        => new()
        {
            Title = keys[0],
            Body = keys[1],
            More = keys[2],
            Kind = StepKind.Do,
            Tab = TabDialogues,
            Anchor = onStart ? "panel:actionsOnStartBox" : "panel:actionsOnFinishBox",
            AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
            OnShown = vm => { if (TestTalk(vm) is { } d) { vm.SelectedDialogue = d; if (d.Nodes.Count > 0) vm.SelectedNode = d.Nodes[0]; } },
            IsDone = (vm, s) => TestTalk(vm) is { Nodes.Count: > 0 } d && done(vm, d.Nodes[0]),
            Solve = (vm, s) =>
            {
                var d = TestTalk(vm)!;
                vm.SelectedDialogue = d;
                vm.SelectedNode = d.Nodes[0];
                solve(vm, d.Nodes[0]);
            },
            Hint = keys[3],
        };
}
