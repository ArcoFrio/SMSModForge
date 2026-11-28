using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SMSModForge.Documentation;
using SMSModForge.Model;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The guide's Examples (1.10.0; the author: "a section for examples of things that can be done, in a step by step
/// format, again, with pictures. Please make sure they have good coverage"): each recipe, step by step, as the
/// editor's own commands do it - what a person's clicks run. <see cref="GuideSectionShots"/> pictures every step
/// after it is done; <see cref="EveryExampleWorks"/> builds them all into the tutorials' practice pack and holds
/// the result to Validate, to what each example says it makes, and to every tab being taught by one.
/// <para/>
/// English on purpose throughout: the words a recipe types are the pack's, and the checks read them back.
/// </summary>
internal static class GuideExamples
{
    internal sealed record Step(int Tab, Action<MainViewModel> Do, Func<MainWindow, FrameworkElement?> Find, double Around = 28);

    internal sealed record Example(string Id, Func<MainViewModel, bool> Made, params Step[] Steps)
    {
        public IEnumerable<int> Tabs => Steps.Select(s => s.Tab).Distinct();
    }

    private static FrameworkElement T(MainWindow w) => (FrameworkElement)w.FindName("MainTabs");

    /// <summary>The row a record is drawn in on its tab: the outermost element showing it.</summary>
    private static FrameworkElement? RowFor(MainWindow w, object record)
        => GuideSectionShots.Below(T(w)).OfType<FrameworkElement>()
               .FirstOrDefault(f => ReferenceEquals(f.DataContext, record) && f.IsVisible && f.ActualHeight > 4
                                    && System.Windows.Media.VisualTreeHelper.GetParent(f) is FrameworkElement p
                                    && !ReferenceEquals(p.DataContext, record));
    private static Func<MainWindow, FrameworkElement?> At(string anchor) => w => GuideSectionShots.Anchor(w, anchor);

    /// <summary>The anchored part, scrolled inside itself to the words <paramref name="words"/>, at its top so what is
    /// under them shows too. The place preview's panel beside the picture scrolls inside itself: it showed its top
    /// while the words the picture marks were further down, and the picture was cut where they would have been with
    /// nothing of the panel in it.</summary>
    private static Func<MainWindow, FrameworkElement?> AtShowing(string anchor, string words) => w =>
    {
        var part = GuideSectionShots.Anchor(w, anchor);
        if (part == null || GuideSectionShots.Below(part).OfType<TextBlock>().FirstOrDefault(t => t.Text == words && t.IsVisible) is not { } shown)
            return part;
        DependencyObject? up = shown;
        while (up != null && up is not ScrollViewer) up = System.Windows.Media.VisualTreeHelper.GetParent(up);
        if (up is ScrollViewer scroller)
        {
            double y = shown.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y;
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset + y - 4);
        }
        else shown.BringIntoView();
        w.UpdateLayout();
        return part;
    };
    private static Func<MainWindow, FrameworkElement?> Group(string caption) => w => GuideSectionShots.ByHeading(T(w), caption);

    private static string Place(MainViewModel vm) => "place:" + (StoryPack.Gallery(vm) ?? vm.Places[0]).Key;

    private static void Set(NodeActionViewModel a, string key, string value)
    {
        if (a.ParamRows.FirstOrDefault(r => r.Key == key) is { } row) row.Value = value;
        else { a.Model.Params[key] = value; a.Reread(); }
    }

    private static void Set(NodeConditionViewModel c, string key, string value)
    {
        if (c.ParamRows.FirstOrDefault(r => r.Key == key) is { } row) row.Value = value;
        else c.Model.Params[key] = value;
    }

    private static void Compare(NodeConditionViewModel c, string variable, string comparison, string value)
    {
        c.DisplayType = NodeConditionViewModel.VariableFamilyType;
        c.VarComparison = comparison;
        c.VarName = variable;
        c.VarValue = value;
    }

    private static void SetVar(NodeActionViewModel a, string variable, string value)
    {
        a.DisplayType = NodeActionViewModel.VariableFamilyType;
        a.VarName = variable;
        a.VarValue = value;
    }

    private static void Rain(NodeConditionViewModel c)
    {
        c.DisplayType = NodeConditionTypes.Weather;
        Set(c, "state", "Raining");
    }

    private static PackVariableViewModel NewVariable(MainViewModel vm, string name, PackVariableType type, string value)
    {
        vm.AddVariableCommand.Execute(null);
        var v = vm.SelectedVariable!;
        v.Name = name;
        v.Type = type;
        v.DefaultValue = value;
        return v;
    }

    private static DialogueViewModel NewConversation(MainViewModel vm, string name)
    {
        vm.AddDialogueCommand.Execute(null);
        var d = vm.SelectedDialogue!;
        d.DisplayName = name;
        d.StartConditions[0].Level = Place(vm);
        return d;
    }

    private static DialogueNodeViewModel NewRoot(MainViewModel vm, string text)
    {
        vm.SelectedNode = null;
        vm.AddDialogueRootNodeCommand.Execute(null);
        var n = vm.SelectedNode!;
        n.Actor = TutorialsPart2.Speaker(vm);
        n.Text = text;
        return n;
    }

    private static DialogueNodeViewModel NewAfter(MainViewModel vm, DialogueNodeViewModel before, string text)
    {
        vm.SelectedNode = before;
        vm.AddDialogueSiblingNodeCommand.Execute(null);
        var n = vm.SelectedNode!;
        n.Actor = TutorialsPart2.Speaker(vm);
        n.Text = text;
        return n;
    }

    private static DialogueViewModel? Conversation(MainViewModel vm, string name) => vm.Dialogues.FirstOrDefault(d => d.DisplayName == name);

    private static DialogueNodeViewModel? Line(MainViewModel vm, string text)
    {
        foreach (var d in vm.Dialogues)
            if (d.Nodes.FirstOrDefault(n => n.Text == text) is { } n) { vm.SelectedDialogue = d; vm.SelectedNode = n; return n; }
        return null;
    }

    /// <summary>A line of the reader's own: the practice pack's first conversation.</summary>
    private static DialogueNodeViewModel ALine(MainViewModel vm)
    {
        var d = TutorialsPart2.HerSecret(vm) ?? vm.Dialogues.First(x => x.Model.Source.Length == 0);
        vm.SelectedDialogue = d;
        vm.SelectedNode = d.Nodes[0];
        return d.Nodes[0];
    }

    private static QuestViewModel HerQuest(MainViewModel vm) => vm.Quests.First(q => q.Title.Trim() == TutorialsQuests.QuestTitle);

    private static void QuestDone(MainViewModel vm, NodeConditionViewModel c)
    {
        var q = Tidy(vm) ?? HerQuest(vm);
        c.DisplayType = NodeConditionTypes.QuestState;
        c.QuestPicker.QuestKey = q.Model.Key;
        c.QuestPicker.TaskKey = q.Model.Tasks[0].Key;
        c.QuestState = Shared.QuestVocabulary.Completed;
    }

    private static bool Has(IEnumerable<NodeConditionDef> conditions, Func<NodeConditionDef, bool> what)
        => conditions.Any(c => what(c) || (c.Conditions != null && Has(c.Conditions, what)));

    internal static IReadOnlyList<Example> All { get; } = new[]
    {
        new Example("onceADay",
            vm => Conversation(vm, "Morning hello") is { } d && d.Nodes.Any(n => n.Model.ActionsOnFinish.Any(a => a.Type == NodeActionTypes.SetVariable))
                  && vm.Variables.Any(v => v.Name == "GreetedToday" && v.RefreshMode == PackVariableRefreshMode.Daily && v.Persisted),
            new Step(MainTab.Variables, vm =>
            {
                var v = NewVariable(vm, "GreetedToday", PackVariableType.Bool, "false");
                v.RefreshMode = PackVariableRefreshMode.Daily;
                v.Persisted = true;
            }, At("panel:variableDetail")),
            new Step(MainTab.Dialogues, vm =>
            {
                var d = NewConversation(vm, "Morning hello");
                vm.AddDialogueStartConditionCommand.Execute(null);
                Compare(d.StartConditions[^1], "GreetedToday", "equals", "false");
            }, Group("Start conditions")),
            new Step(MainTab.Dialogues, vm => NewRoot(vm, "Good morning! You're early today."), At("panel:nodeEditor")),
            new Step(MainTab.Dialogues, vm =>
            {
                var n = Line(vm, "Good morning! You're early today.")!;
                SetVar(n.AddActionOnFinish(), "GreetedToday", "true");
            }, At("panel:actionsOnFinishBox"))),

        new Example("rememberedChoice",
            vm => Line(vm, "I'll put some jazz on for you.") is { } n && Has(n.Model.Conditions, c => c.Params.ContainsValue("LikesJazz"))
                  && Line(vm, "Do you like jazz?") is { } q && q.Model.Children.Count == 2,
            new Step(MainTab.Variables, vm => NewVariable(vm, "LikesJazz", PackVariableType.Bool, "false").Persisted = true, At("panel:variableDetail")),
            new Step(MainTab.Dialogues, vm =>
            {
                NewConversation(vm, "Music taste");
                var q = NewRoot(vm, "Do you like jazz?");
                q.Kind = DialogueNodeKind.Choice;
                // + Child, then + Sibling with the first answer chosen: + Child chooses the line it makes.
                vm.SelectedNode = q;
                vm.AddDialogueChildNodeCommand.Execute(null);
                vm.SelectedNode!.Text = "Love it.";
                vm.AddDialogueSiblingNodeCommand.Execute(null);
                vm.SelectedNode!.Text = "Not really.";
                vm.SelectedNode = q;
            }, At("panel:dialogueNodes")),
            new Step(MainTab.Dialogues, vm => SetVar(Line(vm, "Love it.")!.AddActionOnFinish(), "LikesJazz", "true"), At("panel:actionsOnFinishBox")),
            new Step(MainTab.Dialogues, vm =>
            {
                var n = NewAfter(vm, Line(vm, "Do you like jazz?")!, "I'll put some jazz on for you.");
                Compare(n.AddCondition(), "LikesJazz", "equals", "true");
            }, At("panel:nodeConditions"))),

        new Example("rainyLine",
            vm => Line(vm, "What a lovely day.") is { } n && n.Model.Conditions.Any(c => c.Type == NodeConditionTypes.Weather && c.Negate),
            new Step(MainTab.Dialogues, vm =>
            {
                NewConversation(vm, "Weather talk");
                Rain(NewRoot(vm, "Did you get caught in the rain?").AddCondition());
            }, At("panel:nodeConditions")),
            new Step(MainTab.Dialogues, vm =>
            {
                var c = NewAfter(vm, Line(vm, "Did you get caught in the rain?")!, "What a lovely day.").AddCondition();
                Rain(c);
                c.Negate = true;
            }, At("panel:nodeConditions"))),

        new Example("placeMusic",
            vm => StoryPack.Gallery(vm) is { } g && g.Model.OnEnter.Any(h => h.Actions.Any(a => a.Type == NodeActionTypes.SwitchMusic)),
            new Step(MainTab.Music, vm =>
            {
                vm.AddMusicCommand.Execute(null);
                vm.SelectedMusic!.DisplayName = "Gallery Jazz";
                vm.SelectedMusic!.AudioPath = TutorialAssets.Music;
            }, At("panel:musicDetail")),
            new Step(MainTab.Places, vm =>
            {
                var g = StoryPack.Gallery(vm)!;
                vm.SelectedPlace = g;
                g.AddEnterHookCommand.Execute(null);
                var a = g.OnEnterHooks[^1].AddAction();
                a.DisplayType = NodeActionTypes.SwitchMusic;
                Set(a, "music", vm.Music.First(m => m.DisplayName == "Gallery Jazz").Key);
            }, Group("On enter"))),

        new Example("screenButton",
            vm => vm.Uis.Any(u => u.StartsOpen && u.Name == "Chime button" && u.Model.Nodes.Any(HasClick)),
            new Step(MainTab.Sfx, vm =>
            {
                vm.AddSfxCommand.Execute(null);
                vm.SelectedSfx!.DisplayName = "Chime";
                vm.SelectedSfx!.AudioPath = TutorialAssets.Sfx;
            }, At("panel:sfxDetail")),
            new Step(MainTab.Ui, vm =>
            {
                vm.AddOwnUiCommand.Execute(null);
                var ui = vm.SelectedUi!;
                ui.Name = "Chime button";
                ui.StartsOpen = true;
            }, At("panel:uiDetail")),
            new Step(MainTab.Ui, vm =>
            {
                var ui = vm.Uis.First(u => u.Name == "Chime button");
                vm.SelectedUi = ui;
                ui.SelectedNode = ui.Nodes[0];
                ui.AddChildCommand.Execute(null);
                var node = ui.SelectedNode!;
                node.Text = "Ring";
                node.AddActionCommand.Execute(null);
                var a = node.Actions[^1];
                a.DisplayType = NodeActionTypes.PlaySFX;
                Set(a, "clip", vm.Sfx.First(x => x.DisplayName == "Chime").Key);
            }, At("panel:uiObjectDetail"))),

        new Example("rainyVisitor",
            vm => StoryPack.Gallery(vm) is { } g && g.NpcsNode.Npcs.Any(p => p.Name == "Wet visitor" &&
                                                                             Has(p.Model.ActiveConditions, c => c.Type == NodeConditionTypes.Weather)),
            new Step(MainTab.Npcs, vm =>
            {
                vm.AddNpcCommand.Execute(null);
                vm.SelectedNpc!.DisplayName = "Wet visitor";
                vm.SelectedNpc!.Sprite = TutorialAssets.Npc(1);
            }, At("panel:npcDetail")),
            new Step(MainTab.Places, vm =>
            {
                var g = StoryPack.Gallery(vm)!;
                vm.SelectedPlace = g;
                var placed = g.NpcsNode.AddNpc();
                placed.Npc = vm.Npcs.First(n => n.DisplayName == "Wet visitor").Key;
                placed.Name = "Wet visitor";
                placed.StartActive = true;
                placed.Body.X = -3f;
                vm.PickInPreview(placed);
            }, At("panel:placePreview")),
            new Step(MainTab.Places, vm =>
            {
                var g = StoryPack.Gallery(vm)!;
                vm.SelectedPlace = g;
                var placed = g.NpcsNode.Npcs.First(p => p.Name == "Wet visitor");
                Rain(placed.AddActiveCondition());
                vm.PickInPreview(placed);
            }, AtShowing("panel:placeSettings", "Active when"))),

        new Example("countVisits",
            vm => vm.IntegrationRules.Any(r => r.DisplayName == "Count visits" && r.Model.Actions.Any(a => a.Type == NodeActionTypes.IncrementVariable)),
            new Step(MainTab.Variables, vm => NewVariable(vm, "Visits", PackVariableType.Int, "0").Persisted = true, At("panel:variableDetail")),
            new Step(MainTab.Integration, vm =>
            {
                vm.AddIntegrationRuleCommand.Execute(null);
                var r = vm.SelectedIntegrationRule!;
                r.DisplayName = "Count visits";
                r.TriggerMode = UpdateRuleTriggerMode.OnRisingEdge;
                var here = r.AddCondition();
                here.DisplayType = NodeConditionTypes.LevelActive;
                here.Level = Place(vm);
                var a = r.AddAction();
                a.DisplayType = NodeActionViewModel.VariableFamilyType;
                a.VarOperation = "Increment";
                a.VarName = "Visits";
                a.VarDelta = "1";
            }, At("panel:ruleConditions")),
            new Step(MainTab.Dialogues, vm =>
            {
                var n = NewAfter(vm, ALine(vm), "You again! I'm starting to like this.");
                Compare(n.AddCondition(), "Visits", "greater or equal", "3");
            }, At("panel:nodeConditions"))),

        new Example("twoTaskQuest",
            vm => vm.Quests.Any(q => q.Title == TidyQuest && q.Model.Tasks.Count == 2),
            new Step(MainTab.Quests, vm =>
            {
                vm.AddQuestCommand.Execute(null);
                vm.SelectedQuest!.Title = TidyQuest;
            }, Group("Quest")),
            new Step(MainTab.Quests, vm =>
            {
                var q = Tidy(vm)!;
                vm.SelectedQuest = q;
                foreach (string task in new[] { "Dust the vase.", "Hang the poster." })
                {
                    q.SelectedTask = null;
                    q.AddTaskCommand.Execute(null);
                    q.SelectedTask!.Name = task;
                }
            }, Group("Tasks")),
            new Step(MainTab.Dialogues, vm =>
            {
                // Started by the line's start, a task done at its finish: the quest is running before its task ends.
                var line = ALine(vm);
                QuestAction(line.AddActionOnStart(), Tidy(vm)!, Shared.QuestVocabulary.Start, -1);
            }, At("panel:actionsOnStartBox")),
            new Step(MainTab.Dialogues, vm =>
            {
                var q = Tidy(vm)!;
                var first = ALine(vm);
                var d = vm.SelectedDialogue!;
                var second = d.Nodes.Count > 1 ? d.Nodes[1] : first;
                vm.SelectedNode = second;
                QuestAction(second.AddActionOnFinish(), q, Shared.QuestVocabulary.CompleteTask, 1);
                vm.SelectedNode = first;
                QuestAction(first.AddActionOnFinish(), q, Shared.QuestVocabulary.CompleteTask, 0);
            }, At("panel:actionsOnFinishBox"))),

        new Example("rewardWallpaper",
            vm => vm.Wallpapers.Any(w => Has(w.Model.UnlockConditions, c => c.Type == NodeConditionTypes.QuestState)),
            new Step(MainTab.Wallpapers, vm =>
            {
                vm.AddWallpaperCommand.Execute(null);
                var w = vm.SelectedWallpaper!;
                w.DisplayName = "A tidy gallery";
                w.SpritePath = TutorialAssets.Wallpaper2;
            }, At("panel:wallpaperDetail")),
            new Step(MainTab.Wallpapers, vm =>
            {
                var w = vm.Wallpapers.First(x => x.DisplayName == "A tidy gallery");
                vm.SelectedWallpaper = w;
                w.AddUnlockConditionCommand.Execute(null);
                QuestDone(vm, w.UnlockConditions.Last());
            }, Group("Unlock conditions"))),

        new Example("secretRoom",
            vm => vm.MapButtons.Any(b => Has(b.Model.Conditions, c => c.Type == NodeConditionTypes.QuestState))
                  && vm.Places.Any(p => p.DisplayName == "Back room" && p.NavigatorButtons.Count > 0
                                        && p.Model.OnEnter.Any(h => h.Actions.Any(a => a.Type == NodeActionTypes.PlayEffect))),
            new Step(MainTab.Places, vm =>
            {
                vm.AddPlaceCommand.Execute(null);
                var p = vm.SelectedPlace!;
                p.DisplayName = "Back room";
                p.SecondarySprite = TutorialAssets.RoomBase;
                p.BaseSprite = TutorialAssets.RoomSecondary;
            }, Group("Level art")),
            new Step(MainTab.Places, vm =>
            {
                var room = vm.Places.First(p => p.DisplayName == "Back room");
                vm.SelectedPlace = room;
                room.AddNavigatorButton()!.Target = "self:" + (StoryPack.Gallery(vm) ?? vm.Places[0]).Key;
            }, At("panel:navigatorButtons")),
            new Step(MainTab.Fx, vm =>
            {
                vm.AddEffectCommand.Execute(null);
                var e = vm.SelectedEffect!;
                e.DisplayName = "Welcome Sparkle";
                e.TemplateOption = EffectViewModel.TemplateOptions.First(o => o.Id == Shared.Effects.VanillaPrefix + "sparkleRing");
            }, At("panel:effectDetail")),
            new Step(MainTab.MapButtons, vm =>
            {
                vm.AddMapButtonCommand.Execute(null);
                var b = vm.MapButtons[^1];
                vm.SelectedMapButton = b;
                b.Target = "self:" + vm.Places.First(p => p.DisplayName == "Back room").Key;
                b.Label = "Back room";
                b.AddConditionCommand.Execute(null);
                QuestDone(vm, b.Conditions[^1]);
            }, w => RowFor(w, ((MainViewModel)w.DataContext).MapButtons[^1]), 16),
            new Step(MainTab.Places, vm =>
            {
                var room = vm.Places.First(p => p.DisplayName == "Back room");
                vm.SelectedPlace = room;
                room.AddEnterHookCommand.Execute(null);
                var a = room.OnEnterHooks[^1].AddAction();
                a.DisplayType = NodeActionTypes.PlayEffect;
                Set(a, Shared.Effects.EffectParam, vm.Effects.First(e => e.DisplayName == "Welcome Sparkle").Key);
            }, Group("On enter"))),

        new Example("gameCharacter",
            vm => vm.Characters.Any(c => c.Model.IsVanillaCharacter && c.DisplayName == "Officer Kane"),
            new Step(MainTab.Characters, vm => vm.SelectedCharacter = Cop(vm), At("panel:characterTree"), 16),
            new Step(MainTab.Characters, vm =>
            {
                var cop = Cop(vm);
                vm.SelectedCharacter = cop;
                cop.DisplayName = "Officer Kane";
            }, Group("Identity"))),

        new Example("starmakerScene",
            vm => vm.Scenes.Any(s => s.DisplayName == "Beach Photo" && s.StarmakerPhoto)
                  && vm.Dialogues.Any(d => d.Nodes.Any(n => n.Model.ActionsOnStart.Any(a => a.Type == NodeActionTypes.DeactivateAllScenes))),
            new Step(MainTab.Scenes, vm =>
            {
                vm.AddSceneCommand.Execute(null);
                var sc = vm.SelectedScene!;
                sc.DisplayName = "Beach Photo";
                sc.SceneSprite = TutorialAssets.Scene.Replace("01", "03");
            }, Group("Scene image")),
            new Step(MainTab.Scenes, vm =>
            {
                var sc = vm.Scenes.First(s => s.DisplayName == "Beach Photo");
                vm.SelectedScene = sc;
                sc.StarmakerPhoto = true;
                sc.Sound = SceneSoundMode.Flash;
            }, Group("Frame, sound and Starmaker")),
            new Step(MainTab.Dialogues, vm =>
            {
                var line = ALine(vm);
                var d = vm.SelectedDialogue!;
                if (d.Nodes.Count > 1)
                {
                    vm.SelectedNode = d.Nodes[1];
                    d.Nodes[1].AddActionOnStart().DisplayType = NodeActionTypes.DeactivateAllScenes;
                }
                vm.SelectedNode = line;
                var a = line.AddActionOnStart();
                a.DisplayType = NodeActionTypes.SetGameObjectActive;
                a.Category = NodeActionViewModel.CatScene;
                a.Target = vm.Scenes.First(s => s.DisplayName == "Beach Photo").Key;
                a.Active = true;
            }, At("panel:actionsOnStartBox"))),
    };

    /// <summary>The controls each step uses, as the screen labels them, in the order they are used: boxed in red
    /// and numbered on the step's picture, with an arrow at the first (a beginner's tutorial picture).</summary>
    private static readonly Dictionary<string, string[][]> Clicks = new()
    {
        ["before"] = new[] { new[] { "+ Character", "+ Voice" }, new[] { "+ Place" } },
        ["onceADay"] = new[]
        {
            new[] { "+ Variable", "Name", "Type", "Starts false", "Refresh mode", "Persisted" },
            new[] { "+ Dialogue", "Required — this dialogue only starts in this level", "+ Add condition" },
            new[] { "Actor", "Text" },
            new[] { "+ Add action", "Type" },
        },
        ["rememberedChoice"] = new[]
        {
            new[] { "+ Variable", "Name", "Type", "Starts false", "Persisted" },
            new[] { "+ Child", "+ Sibling" },
            new[] { "+ Add action" },
            new[] { "+ Add condition" },
        },
        ["rainyLine"] = new[] { new[] { "+ Add condition", "Weather is" }, new[] { "+ Sibling", "Negate" } },
        ["placeMusic"] = new[] { new[] { "+ Music", "Display name", "Audio path" }, new[] { "+ Add action group", "+ Add action" } },
        ["screenButton"] = new[]
        {
            new[] { "+ SFX", "Display name", "Audio path" },
            new[] { "+ UI", "Name", "On screen from the start" },
            new[] { "+ Object", "Text", "+ Action" },
        },
        ["rainyVisitor"] = new[] { new[] { "+ NPC", "Display name", "Pose sprite" }, new[] { "+ Character" }, new[] { "Active when" } },
        ["countVisits"] = new[]
        {
            new[] { "+ Variable", "Name", "Type", "Default value", "Persisted" },
            new[] { "+ Rule", "Trigger mode", "+ Add condition", "+ Add action" },
            new[] { "+ Add condition" },
        },
        ["twoTaskQuest"] = new[] { new[] { "+ Quest", "Title" }, new[] { "+ Task", "Text" }, new[] { "+ Add action" }, new[] { "+ Add action" } },
        ["rewardWallpaper"] = new[] { new[] { "+ Wallpaper", "Display name", "Sprite path" }, new[] { "+ Add condition" } },
        ["secretRoom"] = new[]
        {
            new[] { "+ Place", "Back sprite", "Front sprite" },
            new[] { "+ Add navigator button", "Target" },
            new[] { "+ Effect", "Template" },
            new[] { "Target place", "Label", "+ Add condition" },
            new[] { "+ Add action group", "+ Add action" },
        },
        ["gameCharacter"] = new[] { new[] { "THE GAME'S CHARACTERS" }, new[] { "Character name" } },
        ["starmakerScene"] = new[] { new[] { "+ Scene", "Sprite path" }, new[] { "Treat as a Starmaker photo", "Sound" }, new[] { "+ Add action" } },
    };

    /// <summary>The labels a step's picture boxes, or null.</summary>
    internal static string[]? Labels(string id, int step)
        => Clicks.TryGetValue(id, out var steps) && step < steps.Length ? steps[step] : null;

    private const string TidyQuest = "Tidy the gallery";
    private static QuestViewModel? Tidy(MainViewModel vm) => vm.Quests.FirstOrDefault(q => q.Title == TidyQuest);

    private static void QuestAction(NodeActionViewModel a, QuestViewModel q, string operation, int task)
    {
        a.DisplayType = Shared.QuestVocabulary.ActionType;
        a.QuestPicker.QuestKey = q.Model.Key;
        a.QuestOperation = operation;
        if (task >= 0) a.QuestPicker.TaskKey = q.Model.Tasks[task].Key;
    }

    private static bool HasClick(UiNodeDef n) => n.OnClick.Count > 0 || n.Children.Any(HasClick);

    private static CharacterViewModel Cop(MainViewModel vm)
        => vm.Characters.First(c => c.Model.IsVanillaCharacter && !c.IsPlayer && !string.IsNullOrWhiteSpace(c.Model.NameColor));

    /// <summary>The guide's topic of an example, by its id.</summary>
    internal static DocTopic Topic(string id)
        => DocTopics.Parts.SelectMany(p => p.Topics).Single(t => t.TitleKey == $"docs.example.{id}.title");
}

[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GuideExamplesTests
{
    private readonly ITestOutputHelper _out;
    public GuideExamplesTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void EveryExampleHasItsStepsInTheGuide()
    {
        var part = DocTopics.Parts.Single(p => p.NameKey == "docs.part.examples");
        // The part opens on what every example assumes; the examples follow, in the order they build on each other.
        Assert.Equal("docs.example.before.title", part.Topics[0].TitleKey);
        Assert.Equal(part.Topics.Skip(1).Select(t => t.TitleKey), GuideExamples.All.Select(e => $"docs.example.{e.Id}.title"));
        foreach (var e in GuideExamples.All)
            Assert.Equal(GuideExamples.Topic(e.Id).Sections.Count, e.Steps.Length);
    }

    [Fact]
    public void BetweenThemTheExamplesTeachEveryTab()
    {
        var taught = GuideExamples.All.SelectMany(e => e.Tabs).ToHashSet();
        var missing = Enumerable.Range(0, MainTab.Integration + 1).Where(t => t != MainTab.ModForge && !taught.Contains(t))
                                .Select(MainTab.Name).ToList();
        Assert.True(missing.Count == 0, "no example uses the tab(s): " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryExampleWorks()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SMSModForge-examples-" + Guid.NewGuid().ToString("N"));
        var problems = new List<string>();
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                Assert.True(vm.MakePack(folder, StoryPack.Name));
                TutorialAssets.EnsureCopied(vm.PackRoot);
                foreach (var t in TutorialCatalog.All.Where(t => t.IsOnLadder).OrderBy(t => t.Level))
                {
                    StoryPack.Replay(vm, t);
                    WindowHarness.Pump();
                }
                foreach (var e in GuideExamples.All)
                {
                    foreach (var step in e.Steps)
                    {
                        vm.SelectedTabIndex = step.Tab;
                        WindowHarness.Pump();
                        try { step.Do(vm); }
                        catch (Exception x) { problems.Add($"{e.Id}: a step threw {x.GetType().Name}: {x.Message}"); break; }
                        WindowHarness.Pump();
                    }
                    if (!e.Made(vm)) problems.Add($"{e.Id}: not made");
                }
                foreach (var issue in Validation.PackValidator.Validate(vm.Pack, vm.PackRoot!).Where(x => x.Severity != Validation.Severity.Info))
                    problems.Add($"Validate says ({issue.Severity}) {issue.Where}: {issue.Message}");
            });
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
        foreach (var p in problems) _out.WriteLine(p);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
