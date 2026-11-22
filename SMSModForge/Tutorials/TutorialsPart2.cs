using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.ViewModel;

namespace SMSModForge.Tutorials;

/// <summary>
/// The later half of the curriculum: conversations, the mask painter, NPCs,
/// state, and rules. Split from <see cref="TutorialCatalog"/> only to keep
/// either file readable — <c>TutorialCatalog.All</c> stitches the two together
/// and remains the single list anything else reads.
/// <para/>
/// From here on the steps lean on <see cref="StepKind.Free"/>: the earlier
/// tutorials are mechanics, where there is a right answer, and these are
/// judgement, where insisting on one would be teaching the wrong lesson.
/// </summary>
internal static class TutorialsPart2
{
    private const int TabCharacters = MainTab.Characters;
    private const int TabNpcs = MainTab.Npcs;
    private const int TabPlaces = MainTab.Places;
    private const int TabDialogues = MainTab.Dialogues;
    private const int TabScenes = MainTab.Scenes;
    private const int TabMusic = MainTab.Music;
    private const int TabSfx = MainTab.Sfx;
    private const int TabWallpapers = MainTab.Wallpapers;
    private const int TabVariables = MainTab.Variables;
    private const int TabIntegration = MainTab.Integration;
    private const int TabModForge = MainTab.ModForge;

    internal static IReadOnlyList<TutorialDef> All { get; } = new[]
    {
        new TutorialDef
        {
            Id = "first-conversation",
            Group = "tutorial.group.dialogues",
            Title = "tutorial.firstConversation.title",
            Summary = "tutorial.firstConversation.summary",
            Makes = "tutorial.firstConversation.makes",
            Level = 6,
            // 2 (1.10.0): "Dummy Days" - her first words, in her gallery; short steps with fold-outs.
            Revision = 2,
            Leaves = vm => StoryPack.Gallery(vm) is { } g && vm.Dialogues.Any(d => LevelTokenOf(d) == "place:" + g.Key),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.dialogueTree.title",
                    Body = "tutorial.firstConversation.dialogueTree.body",
                    More = "tutorial.firstConversation.dialogueTree.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.addDialogue.title",
                    Body = "tutorial.firstConversation.addDialogue.body",
                    More = "tutorial.firstConversation.addDialogue.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "btn:addDialogue",
                    AlsoAllow = new[] { "panel:dialogueIdentity" },
                    OnEnter = (vm, s) => s.Set("dlg", vm.Dialogues.Count),
                    IsDone = (vm, s) => s.GrewSince("dlg", vm.Dialogues.Count),
                    Solve = (vm, s) => vm.AddDialogueCommand.Execute(null),
                    Hint = "tutorial.firstConversation.addDialogue.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.nameIt.title",
                    Body = "tutorial.firstConversation.nameIt.body",
                    More = "tutorial.firstConversation.nameIt.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "field:dialogueName",
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && !TutorialCatalog.StillPlaceholder(d.DisplayName, "dialogues.newName"),
                    Solve = (vm, s) => vm.SelectedDialogue!.DisplayName = "Her secret",   // English on purpose: the story pack's words.
                    Hint = "tutorial.firstConversation.nameIt.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.sayWhereHappens.title",
                    Body = "tutorial.firstConversation.sayWhereHappens.body",
                    More = "tutorial.firstConversation.sayWhereHappens.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "field:startConditions",
                    // Baselined against the level the dialogue was born with: the
                    // pinned level row is there from the start, so only a CHANGE
                    // says the level was chosen.
                    OnEnter = (vm, s) => s.Set("level", LevelTokenOf(vm.SelectedDialogue)),
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        LevelTokenOf(d).Length > 0 &&
                                        LevelTokenOf(d) != s.Get<string>("level"),
                    Solve = (vm, s) =>
                    {
                        // The pinned level row, pointed at her gallery: what the step asks, and what the game needs.
                        var row = vm.SelectedDialogue!.StartConditions[0];
                        row.Level = "place:" + (StoryPack.Gallery(vm) ?? vm.Places[^1]).Key;
                    },
                    Hint = "tutorial.firstConversation.sayWhereHappens.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.writeLine.title",
                    Body = "tutorial.firstConversation.writeLine.body",
                    More = "tutorial.firstConversation.writeLine.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "btn:addNode",
                    AlsoAllow = new[] { "panel:nodeEditor" },
                    OnEnter = (vm, s) => s.Set("nodes", vm.SelectedDialogue?.Nodes.Count ?? 0),
                    // The actor as well as the text: a line with nobody speaking it shows no name in game.
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && d.Nodes.Count > 0 &&
                                        vm.SelectedNode is { } n &&
                                        n.Text.Trim().Length > 0 &&
                                        n.Actor.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        vm.AddDialogueRootNodeCommand.Execute(null);
                        var n = vm.SelectedNode!;
                        n.Actor = Speaker(vm);
                        n.Text = "Oh! You saw me move?";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.firstConversation.writeLine.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.restNodeBox.title",
                    Body = "tutorial.firstConversation.restNodeBox.body",
                    More = "tutorial.firstConversation.restNodeBox.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.givePlayerSay.title",
                    Body = "tutorial.firstConversation.givePlayerSay.body",
                    More = "tutorial.firstConversation.givePlayerSay.more",
                    Kind = StepKind.Free,
                    Tab = TabDialogues,
                    // + Sibling first, as the step does: the ring on Kind invited turning her first line into the choice
                    // (review of every step, 2026-11-22). Kind and each answer's Text are in the Node box.
                    Anchor = "btn:addNode",
                    AlsoAllow = new[] { "field:nodeKind", "panel:dialogueNodes", "panel:nodeEditor" },
                    // Free in what the answers SAY, strict that they say something:
                    // two blank ones are exactly the pack that breaks in game.
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && HasWrittenChoice(d),
                    Solve = (vm, s) =>
                    {
                        vm.SelectedNode = null;
                        vm.AddDialogueRootNodeCommand.Execute(null);
                        var choice = vm.SelectedNode!;
                        choice.Kind = Model.DialogueNodeKind.Choice;
                        choice.Actor = Speaker(vm);
                        choice.Text = "Will you keep my secret?";   // English on purpose: the story pack's words.
                        // English on purpose: the story pack's words.
                        foreach (var answer in new[] { "I promise.", "What's it worth to you?" })
                        {
                            vm.SelectedNode = choice;
                            vm.AddDialogueChildNodeCommand.Execute(null);
                            vm.SelectedNode!.Text = answer;
                        }
                    },
                    Hint = "tutorial.firstConversation.givePlayerSay.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.happensAfterNode.title",
                    Body = "tutorial.firstConversation.happensAfterNode.body",
                    More = "tutorial.firstConversation.happensAfterNode.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.makeExitMeanSomething.title",
                    Body = "tutorial.firstConversation.makeExitMeanSomething.body",
                    More = "tutorial.firstConversation.makeExitMeanSomething.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    AlsoAllow = new[] { "panel:nodeEditor" },
                    // Not just "an Exit exists": an Exit with nothing after it does what Continue does.
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && ExitSkipsSomething(d),
                    Solve = (vm, s) =>
                    {
                        var d = vm.SelectedDialogue!;
                        var choice = d.Nodes.Last(n => n.Kind == Model.DialogueNodeKind.Choice);
                        vm.SelectedNode = choice;
                        vm.AddDialogueSiblingNodeCommand.Execute(null);
                        var last = vm.SelectedNode!;
                        last.Actor = Speaker(vm);
                        last.Text = "Then come back tomorrow. I'll practice my poses for you.";   // English on purpose: the story pack's words.
                        int lastAnswer = choice.Model.Children[choice.Model.Children.Count - 1];
                        d.Nodes.First(n => n.Id == lastAnswer).JumpMode = Model.JumpMode.Exit;
                    },
                    Hint = "tutorial.firstConversation.makeExitMeanSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.actionsLineChangesThings.title",
                    Body = "tutorial.firstConversation.actionsLineChangesThings.body",
                    More = "tutorial.firstConversation.actionsLineChangesThings.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.goHaveConversation.title",
                    Body = "tutorial.firstConversation.goHaveConversation.body",
                    More = "tutorial.firstConversation.goHaveConversation.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "btn:validate",
                },
            },
        },

        new TutorialDef
        {
            Id = "lines-that-choose",
            // 2: gates, jumps and the returning-menu shape. 3 (1.10.0): "Dummy Days" - her vase line and your answer.
            Revision = 3,
            Group = "tutorial.group.dialogues",
            Title = "tutorial.linesThatChoose.title",
            Summary = "tutorial.linesThatChoose.summary",
            Makes = "tutorial.linesThatChoose.makes",
            Level = 7,
            Leaves = vm => HerSecret(vm) is { } d && JumpLandsOnATag(d),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.notEveryLineShould.title",
                    Body = "tutorial.linesThatChoose.notEveryLineShould.body",
                    More = "tutorial.linesThatChoose.notEveryLineShould.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueList",
                    // Her dialogue, opened for the reader: the steps after this one work in it.
                    OnEnter = (vm, s) => { if (HerSecret(vm) is { } d) vm.SelectedDialogue = d; },
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.openConversationWrote.title",
                    Body = "tutorial.linesThatChoose.openConversationWrote.body",
                    More = "tutorial.linesThatChoose.openConversationWrote.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    AlsoAllow = new[] { "panel:nodeEditor" },
                    OnEnter = (vm, s) => s.Set("nodes", vm.SelectedDialogue?.Nodes.Count ?? 0),
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && s.GrewSince("nodes", d.Nodes.Count) &&
                                        vm.SelectedNode is { } n && n.Text.Trim().Length > 0 && n.Actor.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        var d = vm.SelectedDialogue ??= HerSecret(vm) ?? vm.Dialogues[^1];
                        vm.SelectedNode = d.Nodes.Count > 0 ? d.Nodes[0] : null;
                        if (vm.SelectedNode != null) vm.AddDialogueSiblingNodeCommand.Execute(null);
                        else vm.AddDialogueRootNodeCommand.Execute(null);
                        var n = vm.SelectedNode!;
                        n.Actor = Speaker(vm);
                        n.Text = "Do you like my vase? I polished it all night.";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.linesThatChoose.openConversationWrote.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.gateSomethingReal.title",
                    Body = "tutorial.linesThatChoose.gateSomethingReal.body",
                    More = "tutorial.linesThatChoose.gateSomethingReal.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    // A condition added and left blank always passes in the game too:
                    // the step wants it finished, down to the object.
                    IsDone = (vm, s) => vm.SelectedNode is { } n &&
                                        n.Conditions.Any(c =>
                                            c.Model.Type == Model.NodeConditionTypes.GameObjectActive &&
                                            c.Model.Params.TryGetValue("target", out var t) &&
                                            !string.IsNullOrWhiteSpace(t)),
                    Solve = (vm, s) =>
                    {
                        var n = vm.SelectedNode!;
                        vm.AddNodeConditionCommand.Execute(null);
                        // Through the view model: setting the type seeds the category,
                        // and the category clears a target of the one before.
                        var c = n.Conditions.Last();
                        c.Type = Model.NodeConditionTypes.GameObjectActive;
                        c.GoCategory = NodeActionViewModel.CatOverlay;
                        c.GoOverlayLevel = "place:" + (StoryPack.Gallery(vm) ?? vm.Places[^1]).Key;
                        c.GoTarget = StoryPack.VaseName;
                    },
                    Hint = "tutorial.linesThatChoose.gateSomethingReal.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.conditionsDoNotStop.title",
                    Body = "tutorial.linesThatChoose.conditionsDoNotStop.body",
                    More = "tutorial.linesThatChoose.conditionsDoNotStop.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.someoneElseRoom.title",
                    Body = "tutorial.linesThatChoose.someoneElseRoom.body",
                    More = "tutorial.linesThatChoose.someoneElseRoom.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => n.Actor == "player" &&
                                                         n.Text.Trim().Length > 0),
                    Solve = (vm, s) =>
                    {
                        // Right after her first line: an answer to it, played whether or not the vase is out.
                        var d = vm.SelectedDialogue!;
                        vm.SelectedNode = d.Nodes.Count > 0 ? d.Nodes[0] : null;
                        if (vm.SelectedNode != null) vm.AddDialogueSiblingNodeCommand.Execute(null);
                        else vm.AddDialogueRootNodeCommand.Execute(null);
                        var n = vm.SelectedNode!;
                        n.Actor = "player";
                        n.Text = "You can talk?!";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.linesThatChoose.someoneElseRoom.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.lettingOneOptionSay.title",
                    Body = "tutorial.linesThatChoose.lettingOneOptionSay.body",
                    More = "tutorial.linesThatChoose.lettingOneOptionSay.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && JumpLandsOnATag(d),
                    Solve = (vm, s) =>
                    {
                        var d = vm.SelectedDialogue!;
                        var choice = d.Nodes.Last(n => n.Kind == Model.DialogueNodeKind.Choice);
                        ClosingLine(d, choice)!.Tag = "ending";
                        var stays = choice.Model.Children.Select(id => d.Nodes.First(n => n.Id == id))
                                          .First(n => n.JumpMode != Model.JumpMode.Exit);
                        vm.SelectedNode = stays;
                        vm.AddDialogueChildNodeCommand.Execute(null);
                        var reaction = vm.SelectedNode!;
                        reaction.Actor = Speaker(vm);
                        reaction.Text = "You're sweet. I knew I could trust you.";   // English on purpose: the story pack's words.
                        reaction.JumpMode = Model.JumpMode.Jump;
                        reaction.JumpTargetTag = "ending";
                    },
                    Hint = "tutorial.linesThatChoose.lettingOneOptionSay.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.jumpingBackwardsPurpose.title",
                    Body = "tutorial.linesThatChoose.jumpingBackwardsPurpose.body",
                    More = "tutorial.linesThatChoose.jumpingBackwardsPurpose.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.conversationMemoryRoom.title",
                    Body = "tutorial.linesThatChoose.conversationMemoryRoom.body",
                    More = "tutorial.linesThatChoose.conversationMemoryRoom.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "a-face-that-moves",
            // 2: the prefix fields and the preview controls. 3 (1.10.0): "Dummy Days" - her face, and a flirty line.
            Revision = 3,
            Group = "tutorial.group.characters",
            Title = "tutorial.aFaceThatMoves.title",
            Summary = "tutorial.aFaceThatMoves.summary",
            Makes = "tutorial.aFaceThatMoves.makes",
            Level = 8,
            Leaves = vm => StoryPack.Dummy(vm) is { } c && c.Outfits.Count > 0 &&
                           c.Outfits[0].BlinkEnabled && c.Outfits[0].MouthEnabled && c.Outfits[0].ExpressionEnabled,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.bustStackPictures.title",
                    Body = "tutorial.aFaceThatMoves.bustStackPictures.body",
                    More = "tutorial.aFaceThatMoves.bustStackPictures.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.openCharacterMade.title",
                    Body = "tutorial.aFaceThatMoves.openCharacterMade.body",
                    More = "tutorial.aFaceThatMoves.openCharacterMade.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterTree",
                    // Her outfit, opened for the reader: the steps after this one work in it.
                    OnEnter = (vm, s) => OpenHerOutfit(vm),
                    OnShown = vm => OpenHerOutfit(vm),
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.giveBlink.title",
                    Body = "tutorial.aFaceThatMoves.giveBlink.body",
                    More = "tutorial.aFaceThatMoves.giveBlink.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitSprites",
                    AlsoAllow = new[] { "field:hasBlink" },
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.BlinkEnabled && o.BlinkSprite.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        var o = vm.SelectedOutfit ?? OpenHerOutfit(vm)!;
                        o.BlinkEnabled = true;
                        o.BlinkSprite = TutorialAssets.BustBlink(1);
                    },
                    Hint = "tutorial.aFaceThatMoves.giveBlink.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.watchHappen.title",
                    Body = "tutorial.aFaceThatMoves.watchHappen.body",
                    More = "tutorial.aFaceThatMoves.watchHappen.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    // The preview pane, not the bust alone: the toggles under it are what the step is about.
                    Anchor = "panel:bustPreviewPane",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.fourMouthsNamedPrefix.title",
                    Body = "tutorial.aFaceThatMoves.fourMouthsNamedPrefix.body",
                    More = "tutorial.aFaceThatMoves.fourMouthsNamedPrefix.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitSprites",
                    AlsoAllow = new[] { "field:hasMouth" },
                    // A bare folder does not pass: the blink's path seeds the box with
                    // one, so "not empty" would be true the moment the step opened.
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.MouthEnabled && NamesAFile(o.MouthPrefix),
                    Solve = (vm, s) =>
                    {
                        var o = vm.SelectedOutfit!;
                        o.MouthEnabled = true;
                        // A stem, with no number and no extension: the game adds 1..4 and the extension.
                        o.MouthPrefix = TutorialAssets.MouthPrefix(1);
                    },
                    Hint = "tutorial.aFaceThatMoves.fourMouthsNamedPrefix.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.hearTalk.title",
                    Body = "tutorial.aFaceThatMoves.hearTalk.body",
                    More = "tutorial.aFaceThatMoves.hearTalk.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:bustPreviewPane",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.fourExpressionsNamedSame.title",
                    Body = "tutorial.aFaceThatMoves.fourExpressionsNamedSame.body",
                    More = "tutorial.aFaceThatMoves.fourExpressionsNamedSame.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitExpressions",
                    AlsoAllow = new[] { "field:hasExpressions" },
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.ExpressionEnabled && NamesAFile(o.ExpressionPrefix),
                    Solve = (vm, s) =>
                    {
                        var o = vm.SelectedOutfit!;
                        o.ExpressionEnabled = true;
                        o.ExpressionPrefix = TutorialAssets.ExpressionPrefix(1);
                    },
                    Hint = "tutorial.aFaceThatMoves.fourExpressionsNamedSame.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.tryThem.title",
                    Body = "tutorial.aFaceThatMoves.tryThem.body",
                    More = "tutorial.aFaceThatMoves.tryThem.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:bustPreviewPane",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.flirtyLine.title",
                    Body = "tutorial.aFaceThatMoves.flirtyLine.body",
                    More = "tutorial.aFaceThatMoves.flirtyLine.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:dialogueList" },
                    // Her dialogue opened for the reader, as the step arrives on the Dialogues tab.
                    // Counted: the game's own conversations in the list have expressions already.
                    OnEnter = (vm, s) =>
                    {
                        if (HerSecret(vm) is { } d) vm.SelectedDialogue = d;
                        s.Set("faces", LinesWithAFace(vm));
                    },
                    IsDone = (vm, s) => s.GrewSince("faces", LinesWithAFace(vm)),
                    Solve = (vm, s) =>
                    {
                        var d = vm.SelectedDialogue ?? HerSecret(vm) ?? vm.Dialogues[^1];
                        vm.SelectedDialogue = d;
                        string her = Speaker(vm);
                        // English on purpose: the story pack's words.
                        var line = d.Nodes.FirstOrDefault(n => n.Actor == her && n.Text.StartsWith("You're sweet", StringComparison.Ordinal))
                                   ?? d.Nodes.Last(n => n.Actor == her);
                        vm.SelectedNode = line;
                        line.Expression = "Flirty";
                    },
                    OnShown = vm => ShowHerLine(vm, SweetOf),
                    Hint = "tutorial.aFaceThatMoves.flirtyLine.hint",
                },
            },
        },

        new TutorialDef
        {
            Id = "making-it-move",
            // 2: where masks go, and why the face stays still. 3 (1.10.0): "Dummy Days" - bolder, on Bust4.
            Revision = 3,
            Group = "tutorial.group.characters",
            Title = "tutorial.makingItMove.title",
            Summary = "tutorial.makingItMove.summary",
            Makes = "tutorial.makingItMove.makes",
            Level = 9,
            Leaves = vm => StoryPack.Dummy(vm) is { } c && c.Outfits.Count > 0 && c.Outfits[0].MaskSprite.Trim().Length > 0,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.maskNotArtwork.title",
                    Body = "tutorial.makingItMove.maskNotArtwork.body",
                    More = "tutorial.makingItMove.maskNotArtwork.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.openCharacterMade.title",
                    Body = "tutorial.makingItMove.openCharacterMade.body",
                    More = "tutorial.makingItMove.openCharacterMade.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterTree",
                    OnEnter = (vm, s) => OpenHerOutfit(vm),
                    OnShown = vm => OpenHerOutfit(vm),
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.pickBustSomethingMove.title",
                    Body = "tutorial.makingItMove.pickBustSomethingMove.body",
                    More = "tutorial.makingItMove.pickBustSomethingMove.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitSprites",
                    AlsoAllow = new[] { "panel:outfitExpressions", "field:baseSprite" },
                    // Baselined: the outfit HAS a base already, so only a change counts - and the face
                    // pictures must follow it, or they sit where the old bust's face was.
                    OnEnter = (vm, s) => s.Set("bust", vm.SelectedOutfit?.BaseSprite ?? ""),
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.BaseSprite.Trim().Length > 0 &&
                                        o.BaseSprite != s.Get<string>("bust") &&
                                        FacesFollowTheBust(o),
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        var o = vm.SelectedOutfit ?? OpenHerOutfit(vm)!;
                        o.BaseSprite = TutorialAssets.Bust(4);
                        if (o.BlinkEnabled) o.BlinkSprite = TutorialAssets.BustBlink(4);
                        if (o.MouthEnabled) o.MouthPrefix = TutorialAssets.MouthPrefix(4);
                        if (o.ExpressionEnabled) o.ExpressionPrefix = TutorialAssets.ExpressionPrefix(4);
                    },
                    Hint = "tutorial.makingItMove.pickBustSomethingMove.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.paintWhereShouldMove.title",
                    Body = "tutorial.makingItMove.paintWhereShouldMove.body",
                    More = "tutorial.makingItMove.paintWhereShouldMove.more",
                    Kind = StepKind.Free,
                    Tab = TabCharacters,
                    Anchor = "btn:editMask",
                    // Free: any mask counts. Where the paint goes is the craft.
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o && o.MaskSprite.Trim().Length > 0,
                    // Done for the reader: Bounce over her chest, soft at the edge, nothing on her face.
                    Solve = (vm, s) => vm.SelectedOutfit!.MaskSprite = TutorialAssets.WriteMask(vm.PackRoot!, 4),
                    Hint = "tutorial.makingItMove.paintWhereShouldMove.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.jiggleNumbersDo.title",
                    Body = "tutorial.makingItMove.jiggleNumbersDo.body",
                    More = "tutorial.makingItMove.jiggleNumbersDo.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "field:jiggle",
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.dropletsOverHereToo.title",
                    Body = "tutorial.makingItMove.dropletsOverHereToo.body",
                    More = "tutorial.makingItMove.dropletsOverHereToo.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitParticles",
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.previewCanShow.title",
                    Body = "tutorial.makingItMove.previewCanShow.body",
                    More = "tutorial.makingItMove.previewCanShow.more",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterPreview",
                    // The toggles are siblings of the preview, not children.
                    AlsoAllow = new[] { "panel:bustPreviewPane" },
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.thenJudgeGame.title",
                    Body = "tutorial.makingItMove.thenJudgeGame.body",
                    More = "tutorial.makingItMove.thenJudgeGame.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "populating",
            // 2: placements, and the preview gizmo. 3 (1.10.0): "Dummy Days" - the other mannequins.
            Revision = 3,
            Group = "tutorial.group.npcs",
            Title = "tutorial.populating.title",
            Summary = "tutorial.populating.summary",
            Makes = "tutorial.populating.makes",
            Level = 10,
            Leaves = vm => StoryPack.Gallery(vm) is { } g && g.NpcsNode.Npcs.Any(p => !string.IsNullOrWhiteSpace(p.Npc)),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.populating.npcsSceneryNotSpeakers.title",
                    Body = "tutorial.populating.npcsSceneryNotSpeakers.body",
                    More = "tutorial.populating.npcsSceneryNotSpeakers.more",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.defineOne.title",
                    Body = "tutorial.populating.defineOne.body",
                    More = "tutorial.populating.defineOne.more",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "btn:addNpc",
                    AlsoAllow = new[] { "panel:npcDetail" },
                    OnEnter = (vm, s) => s.Set("npcs", vm.Npcs.Count),
                    IsDone = (vm, s) => s.GrewSince("npcs", vm.Npcs.Count) &&
                                        vm.SelectedNpc is { } n && n.Sprite.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.AddNpcCommand.Execute(null);
                        vm.SelectedNpc!.DisplayName = StoryPack.MannequinName;
                        vm.SelectedNpc!.Sprite = TutorialAssets.Npc(0);
                    },
                    Hint = "tutorial.populating.defineOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.dropletsDoNotWant.title",
                    Body = "tutorial.populating.dropletsDoNotWant.body",
                    More = "tutorial.populating.dropletsDoNotWant.more",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "panel:npcWet",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.sitThemFloor.title",
                    Body = "tutorial.populating.sitThemFloor.body",
                    More = "tutorial.populating.sitThemFloor.more",
                    // Read, not Do: the box is ticked when the NPC is made.
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "field:npcShadow",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.giveThemSomeLife.title",
                    Body = "tutorial.populating.giveThemSomeLife.body",
                    More = "tutorial.populating.giveThemSomeLife.more",
                    Kind = StepKind.Free,
                    Tab = TabNpcs,
                    Anchor = "btn:editMaskNpc",
                    IsDone = (vm, s) => vm.SelectedNpc is { } n && n.Mask.Trim().Length > 0,
                    Solve = (vm, s) => vm.SelectedNpc!.Mask = TutorialAssets.WriteNpcMask(vm.PackRoot!, 0),
                    Hint = "tutorial.populating.giveThemSomeLife.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.putThemRoom.title",
                    Body = "tutorial.populating.putThemRoom.body",
                    More = "tutorial.populating.putThemRoom.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addNpcToPlace",
                    // The + and its row are on the right; choosing the place is in the list on the left.
                    AlsoAllow = new[] { "panel:placeGameObjects", "panel:placeList" },
                    OnEnter = (vm, s) =>
                    {
                        if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g;
                        s.Set("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0);
                    },
                    // The row is half of it: a placement that never names an NPC is a room with nobody in it.
                    IsDone = (vm, s) => s.GrewSince("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0) &&
                                        vm.SelectedPlace is { } pl &&
                                        pl.NpcsNode.Npcs.Any(n => !string.IsNullOrWhiteSpace(n.Npc)),
                    Solve = (vm, s) => PlaceTheMannequin(vm),
                    Hint = "tutorial.populating.putThemRoom.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.switchThem.title",
                    Body = "tutorial.populating.switchThem.body",
                    More = "tutorial.populating.switchThem.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    AlsoAllow = new[] { "panel:placeList" },
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NpcsNode.Npcs.Any(n => n.StartActive &&
                                                                 !string.IsNullOrWhiteSpace(n.Npc)),
                    Solve = (vm, s) => vm.SelectedPlace!.NpcsNode.Npcs.Last(n => !string.IsNullOrWhiteSpace(n.Npc)).StartActive = true,
                    Hint = "tutorial.populating.switchThem.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.placeThemProperly.title",
                    Body = "tutorial.populating.placeThemProperly.body",
                    More = "tutorial.populating.placeThemProperly.more",
                    // Free: where she stands is the reader's call - any move counts.
                    Kind = StepKind.Free,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                    AlsoAllow = new[] { "panel:placeGameObjects" },
                    OnEnter = (vm, s) => s.Set("at", Where(NewestPlacement(vm))),
                    IsDone = (vm, s) => NewestPlacement(vm) is { } pl && Moved(pl, s.Get<string>("at")),
                    Solve = (vm, s) => NewestPlacement(vm)!.Body.X = 3f,
                    Hint = "tutorial.populating.placeThemProperly.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.shadow.title",
                    Body = "tutorial.populating.shadow.body",
                    More = "tutorial.populating.shadow.more",
                    // Free: where her feet are is for the eye to say.
                    Kind = StepKind.Free,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                    AlsoAllow = new[] { "panel:placeGameObjects" },
                    OnEnter = (vm, s) => s.Set("shadow", NewestPlacement(vm)?.Shadow.Y ?? 0f),
                    IsDone = (vm, s) => NewestPlacement(vm) is { } pl && Math.Abs(pl.Shadow.Y - s.Get<float>("shadow")) > 0.5f,
                    Solve = (vm, s) => ShadowUnderFeet(vm, NewestPlacement(vm)!),
                    Hint = "tutorial.populating.shadow.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.seeWhetherRoomReads.title",
                    Body = "tutorial.populating.seeWhetherRoomReads.body",
                    More = "tutorial.populating.seeWhetherRoomReads.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "npcs-that-belong",
            // 2: NPC blink art, and positioning a reflection. 3 (1.10.0): "Dummy Days" - a row of mannequins.
            Revision = 3,
            Group = "tutorial.group.npcs",
            Title = "tutorial.npcsThatBelong.title",
            Summary = "tutorial.npcsThatBelong.summary",
            Makes = "tutorial.npcsThatBelong.makes",
            Level = 11,
            Leaves = vm => StoryPack.Gallery(vm) is { } g &&
                           g.NpcsNode.Npcs.Select(n => n.Name.Trim()).Where(n => n.Length > 0).Distinct().Count() >= 2,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.definitionNotAppearance.title",
                    Body = "tutorial.npcsThatBelong.definitionNotAppearance.body",
                    More = "tutorial.npcsThatBelong.definitionNotAppearance.more",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.putSamePersonTwice.title",
                    Body = "tutorial.npcsThatBelong.putSamePersonTwice.body",
                    More = "tutorial.npcsThatBelong.putSamePersonTwice.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addNpcToPlace",
                    AlsoAllow = new[] { "panel:placeGameObjects", "panel:placeList" },
                    OnEnter = (vm, s) =>
                    {
                        if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g;
                        s.Set("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0);
                    },
                    IsDone = (vm, s) => s.GrewSince("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0),
                    Solve = (vm, s) => PlaceTheMannequin(vm),
                    Hint = "tutorial.npcsThatBelong.putSamePersonTwice.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.aside.title",
                    Body = "tutorial.npcsThatBelong.aside.body",
                    More = "tutorial.npcsThatBelong.aside.more",
                    Kind = StepKind.Free,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                    AlsoAllow = new[] { "panel:placeGameObjects" },
                    // Moved, and apart from the first one: on top of her, the two read as one.
                    OnEnter = (vm, s) => s.Set("at", Where(NewestPlacement(vm))),
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && p.NpcsNode.Npcs.Count(n => !string.IsNullOrWhiteSpace(n.Npc)) >= 2 &&
                                        NewestPlacement(vm) is { } pl && Moved(pl, s.Get<string>("at")) &&
                                        p.NpcsNode.Npcs.Where(n => !ReferenceEquals(n, pl) && !string.IsNullOrWhiteSpace(n.Npc))
                                         .All(n => Math.Abs(n.Body.X - pl.Body.X) + Math.Abs(n.Body.Y - pl.Body.Y) > 1.5f),
                    Solve = (vm, s) =>
                    {
                        var pl = NewestPlacement(vm)!;
                        var first = vm.SelectedPlace!.NpcsNode.Npcs.First(n => !string.IsNullOrWhiteSpace(n.Npc));
                        pl.Body.X = first.Body.X > 0 ? first.Body.X - 6f : first.Body.X + 6f;
                    },
                    Hint = "tutorial.npcsThatBelong.aside.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.tellThemApart.title",
                    Body = "tutorial.npcsThatBelong.tellThemApart.body",
                    More = "tutorial.npcsThatBelong.tellThemApart.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NpcsNode.Npcs.Count >= 2 &&
                                        p.NpcsNode.Npcs.Count(n => n.StartActive) >= 2 &&
                                        p.NpcsNode.Npcs.Select(n => n.Name.Trim())
                                         .Where(n => n.Length > 0).Distinct().Count() >= 2,
                    Solve = (vm, s) =>
                    {
                        // Named by where they stand, the one further left first (the review found them the other
                        // way round: the first stands right of centre and the second was put on the left of her).
                        var list = vm.SelectedPlace!.NpcsNode.Npcs.OrderBy(n => n.Body.X).ToList();
                        list[0].Name = "MannequinLeft";   // English on purpose: names a rule points at.
                        list[^1].Name = "MannequinRight";
                        foreach (var n in list) n.StartActive = true;
                    },
                    Hint = "tutorial.npcsThatBelong.tellThemApart.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.shadow.title",
                    Body = "tutorial.npcsThatBelong.shadow.body",
                    More = "tutorial.npcsThatBelong.shadow.more",
                    Kind = StepKind.Free,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                    AlsoAllow = new[] { "panel:placeGameObjects" },
                    OnEnter = (vm, s) => s.Set("shadow", NewestPlacement(vm)?.Shadow.Y ?? 0f),
                    IsDone = (vm, s) => NewestPlacement(vm) is { } pl && Math.Abs(pl.Shadow.Y - s.Get<float>("shadow")) > 0.5f,
                    Solve = (vm, s) => ShadowUnderFeet(vm, NewestPlacement(vm)!),
                    Hint = "tutorial.npcsThatBelong.shadow.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.backDefinition.title",
                    Body = "tutorial.npcsThatBelong.backDefinition.body",
                    More = "tutorial.npcsThatBelong.backDefinition.more",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "panel:npcDetail",
                    // The mannequin, opened for the reader: the next two steps change it.
                    OnEnter = (vm, s) => { if (Mannequin(vm) is { } n) vm.SelectedNpc = n; },
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.standThemSomething.title",
                    Body = "tutorial.npcsThatBelong.standThemSomething.body",
                    More = "tutorial.npcsThatBelong.standThemSomething.more",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "panel:npcReflection",
                    IsDone = (vm, s) => vm.SelectedNpc is { ReflectionEnabled: true },
                    Solve = (vm, s) => (vm.SelectedNpc ??= Mannequin(vm))!.ReflectionEnabled = true,
                    Hint = "tutorial.npcsThatBelong.standThemSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.giveThemEyesClose.title",
                    Body = "tutorial.npcsThatBelong.giveThemEyesClose.body",
                    More = "tutorial.npcsThatBelong.giveThemEyesClose.more",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "panel:npcBlink",
                    IsDone = (vm, s) => vm.SelectedNpc is { } n && n.BlinkSprite.Trim().Length > 0,
                    Solve = (vm, s) => vm.SelectedNpc!.BlinkSprite = TutorialAssets.NpcBlink(0),
                    Hint = "tutorial.npcsThatBelong.giveThemEyesClose.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.oneLeaveAlone.title",
                    Body = "tutorial.npcsThatBelong.oneLeaveAlone.body",
                    More = "tutorial.npcsThatBelong.oneLeaveAlone.more",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "panel:npcWet",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.roomPeople.title",
                    Body = "tutorial.npcsThatBelong.roomPeople.body",
                    More = "tutorial.npcsThatBelong.roomPeople.more",
                    Kind = StepKind.Read,
                    // On the gallery, where what it says can be seen - not Home (review of every step, 2026-11-22).
                    Tab = MainTab.Places,
                    Anchor = "panel:placePreview",
                },
            },
        },

        new TutorialDef
        {
            Id = "sound-in-a-room",
            // 2: playing a track, and the PlaySFX action. 3 (1.10.0): "Dummy Days" - her music and the door.
            // 4: the way home gets the game's music back.
            Revision = 4,
            Group = "tutorial.group.media",
            Title = "tutorial.soundInARoom.title",
            Summary = "tutorial.soundInARoom.summary",
            Makes = "tutorial.soundInARoom.makes",
            Level = 12,
            Leaves = vm => vm.Sfx.Any(fx => fx.TextPatternsCsv.Contains(DoorPattern)),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.twoDifferentJobs.title",
                    Body = "tutorial.soundInARoom.twoDifferentJobs.body",
                    More = "tutorial.soundInARoom.twoDifferentJobs.more",
                    Kind = StepKind.Read,
                    Tab = TabMusic,
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.addTrack.title",
                    Body = "tutorial.soundInARoom.addTrack.body",
                    More = "tutorial.soundInARoom.addTrack.more",
                    Kind = StepKind.Do,
                    Tab = TabMusic,
                    Anchor = "btn:addMusic",
                    AlsoAllow = new[] { "panel:musicDetail" },
                    IsDone = (vm, s) => vm.SelectedMusic is { } m &&
                                        m.AudioPath.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.AddMusicCommand.Execute(null);
                        vm.SelectedMusic!.DisplayName = "Neon Rain";   // English on purpose: the track's name.
                        vm.SelectedMusic!.AudioPath = TutorialAssets.Music;
                    },
                    Hint = "tutorial.soundInARoom.addTrack.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.hearWithoutLeavingEditor.title",
                    Body = "tutorial.soundInARoom.hearWithoutLeavingEditor.body",
                    More = "tutorial.soundInARoom.hearWithoutLeavingEditor.more",
                    Kind = StepKind.Read,
                    Tab = TabMusic,
                    Anchor = "panel:musicDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.makeSomethingPlay.title",
                    Body = "tutorial.soundInARoom.makeSomethingPlay.body",
                    More = "tutorial.soundInARoom.makeSomethingPlay.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:extNavigatorButtons",
                    AlsoAllow = new[] { "panel:placeList" },
                    // Her door, opened for the reader: the bedroom's extension.
                    OnEnter = (vm, s) => { if (BedroomDoor(vm) is { } ext) vm.SelectedVanillaExtension = ext; },
                    IsDone = (vm, s) => vm.VanillaExtensions.Any(
                        e => e.NavigatorButtons.Any(
                            b => b.Music.Trim().Length > 0 &&
                                 vm.Music.Any(m => m.Key == b.Music.Trim()))),
                    Solve = (vm, s) =>
                    {
                        var ext = BedroomDoor(vm);
                        if (ext == null)
                        {
                            // No door yet (a pack that skipped "Her gallery"): the one that tutorial builds.
                            vm.AddVanillaExtensionCommand.Execute(null);
                            ext = vm.SelectedVanillaExtension!;
                            ext.Source = "vanilla:5_MyRoom";
                            ext.AddNavigatorButton()!.Target = "self:" + (StoryPack.Gallery(vm) ?? vm.Places[^1]).Key;
                        }
                        vm.SelectedVanillaExtension = ext;
                        // The RUNTIME name: what the game looks the track up by.
                        ext.NavigatorButtons.First(b => b.Target.StartsWith("self:", StringComparison.Ordinal)).Music = vm.Music[^1].Key;
                    },
                    Hint = "tutorial.soundInARoom.makeSomethingPlay.hint",
                },
                new TutorialStep
                {
                    // The way back out (1.10.0, from the review): with no music of its own, Neon Rain followed the
                    // player home, since one track plays at a time and a button with none leaves it playing.
                    Title = "tutorial.soundInARoom.wayHome.title",
                    Body = "tutorial.soundInARoom.wayHome.body",
                    More = "tutorial.soundInARoom.wayHome.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:navigatorButtons",
                    AlsoAllow = new[] { "panel:placeList" },
                    OnShown = vm => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                    IsDone = (vm, s) => StoryPack.Gallery(vm) is { } g &&
                                        g.NavigatorButtons.Any(b => b.Target.StartsWith("vanilla:", StringComparison.Ordinal) &&
                                                                    b.Music.Trim().Length > 0),
                    Solve = (vm, s) =>
                    {
                        var g = StoryPack.Gallery(vm)!;
                        vm.SelectedPlace = g;
                        g.NavigatorButtons.First(b => b.Target.StartsWith("vanilla:", StringComparison.Ordinal)).Music = Model.VanillaMusic.Default;
                    },
                    Hint = "tutorial.soundInARoom.wayHome.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.addEffect.title",
                    Body = "tutorial.soundInARoom.addEffect.body",
                    More = "tutorial.soundInARoom.addEffect.more",
                    Kind = StepKind.Do,
                    Tab = TabSfx,
                    Anchor = "btn:addSfx",
                    AlsoAllow = new[] { "panel:sfxDetail" },
                    IsDone = (vm, s) => vm.SelectedSfx is { } fx &&
                                        fx.AudioPath.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.AddSfxCommand.Execute(null);
                        vm.SelectedSfx!.DisplayName = "Door";   // English on purpose: the effect's name.
                        vm.SelectedSfx!.AudioPath = TutorialAssets.Sfx;
                    },
                    Hint = "tutorial.soundInARoom.addEffect.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.partWouldNeverGuess.title",
                    Body = "tutorial.soundInARoom.partWouldNeverGuess.body",
                    More = "tutorial.soundInARoom.partWouldNeverGuess.more",
                    Kind = StepKind.Do,
                    Tab = TabSfx,
                    Anchor = "panel:sfxDetail",
                    IsDone = (vm, s) => vm.SelectedSfx is { } fx &&
                                        fx.TextPatternsCsv.Trim().Length > 0,
                    Solve = (vm, s) => vm.SelectedSfx!.TextPatternsCsv = DoorPattern,
                    Hint = "tutorial.soundInARoom.partWouldNeverGuess.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.sayLine.title",
                    Body = "tutorial.soundInARoom.sayLine.body",
                    More = "tutorial.soundInARoom.sayLine.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:dialogueList" },
                    OnEnter = (vm, s) => { if (HerSecret(vm) is { } d) vm.SelectedDialogue = d; },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => n.Text.Contains('*')),
                    Solve = (vm, s) =>
                    {
                        // The pattern goes into the text as written, asterisks and all.
                        var d = vm.SelectedDialogue ??= HerSecret(vm) ?? vm.Dialogues[^1];
                        var first = d.Nodes[0];
                        vm.SelectedNode = first;
                        first.Text = DoorPattern + " " + first.Text;
                    },
                    OnShown = vm => ShowHerLine(vm, d => d.Nodes.FirstOrDefault()),
                    Hint = "tutorial.soundInARoom.sayLine.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.otherWayFireOne.title",
                    Body = "tutorial.soundInARoom.otherWayFireOne.body",
                    More = "tutorial.soundInARoom.otherWayFireOne.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => HasPlaySfx(n)),
                    Solve = (vm, s) =>
                    {
                        // The same effect, fired without anything in the line: as she sees you out.
                        var d = vm.SelectedDialogue!;
                        var choice = d.Nodes.LastOrDefault(n => n.Kind == Model.DialogueNodeKind.Choice);
                        var last = (choice != null ? ClosingLine(d, choice) : null) ?? d.Nodes[^1];
                        vm.SelectedNode = last;
                        var a = last.AddActionOnFinish();
                        a.DisplayType = Model.NodeActionTypes.PlaySFX;
                        a.Model.Params["clip"] = (vm.Sfx.FirstOrDefault(fx => fx.TextPatternsCsv.Contains(DoorPattern)) ?? vm.Sfx[^1]).Key;
                        a.Reread();
                    },
                    OnShown = vm => ShowHerLine(vm, ClosingLineOf),
                    Hint = "tutorial.soundInARoom.otherWayFireOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.moreThanOneTake.title",
                    Body = "tutorial.soundInARoom.moreThanOneTake.body",
                    More = "tutorial.soundInARoom.moreThanOneTake.more",
                    Kind = StepKind.Read,
                    Tab = TabSfx,
                    Anchor = "panel:sfxDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.packMakesNoise.title",
                    Body = "tutorial.soundInARoom.packMakesNoise.body",
                    More = "tutorial.soundInARoom.packMakesNoise.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "remembering",
            // 3: list variables. 4 (1.10.0): "Dummy Days" - she remembers your promise.
            Revision = 4,
            Group = "tutorial.group.logic",
            Title = "tutorial.remembering.title",
            Summary = "tutorial.remembering.summary",
            Makes = "tutorial.remembering.makes",
            Level = 13,
            Leaves = vm => vm.Variables.Any(v => v.Name.Trim() == KeptHerSecret),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.remembering.packsForgetDefault.title",
                    Body = "tutorial.remembering.packsForgetDefault.body",
                    More = "tutorial.remembering.packsForgetDefault.more",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.declareOne.title",
                    Body = "tutorial.remembering.declareOne.body",
                    More = "tutorial.remembering.declareOne.more",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    OnEnter = (vm, s) => s.Set("vars", vm.Variables.Count),
                    // Not the placeholder: a variable still called var1 means the step was clicked through.
                    IsDone = (vm, s) => s.GrewSince("vars", vm.Variables.Count) &&
                                        vm.SelectedVariable is { } v &&
                                        v.Name.Trim().Length > 0 &&
                                        !Regex.IsMatch(v.Name.Trim(), @"^var\d+$"),
                    Solve = (vm, s) =>
                    {
                        vm.AddVariableCommand.Execute(null);
                        vm.SelectedVariable!.Name = KeptHerSecret;
                    },
                    Hint = "tutorial.remembering.declareOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.persistedWhyAlready.title",
                    Body = "tutorial.remembering.persistedWhyAlready.body",
                    More = "tutorial.remembering.persistedWhyAlready.more",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                    Anchor = "field:persisted",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.setConversation.title",
                    Body = "tutorial.remembering.setConversation.body",
                    More = "tutorial.remembering.setConversation.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "field:actionsOnFinish",
                    // Choosing the line happens in the tree, another box than the actions.
                    AlsoAllow = new[] { "panel:actionsOnFinishBox", "panel:dialogueNodes", "panel:dialogueList" },
                    OnEnter = (vm, s) => { if (HerSecret(vm) is { } d) vm.SelectedDialogue = d; },
                    // The action the step describes, not just any row: a blank one does nothing.
                    IsDone = (vm, s) => vm.SelectedNode is { } n && SetsAVariableTrue(n),
                    Solve = (vm, s) =>
                    {
                        var d = vm.SelectedDialogue ??= HerSecret(vm) ?? vm.Dialogues[^1];
                        // English on purpose: the story pack's words.
                        var promise = d.Nodes.FirstOrDefault(n => n.Text.Trim() == "I promise.") ?? d.Nodes[0];
                        vm.SelectedNode = promise;
                        vm.AddNodeActionOnFinishCommand.Execute(null);
                        var a = promise.ActionsOnFinish.Last();
                        a.DisplayType = NodeActionViewModel.VariableFamilyType;
                        a.VarName = KeptHerSecret;
                        a.VarValue = "true";
                    },
                    OnShown = vm => ShowHerLine(vm, PromiseOf),
                    Hint = "tutorial.remembering.setConversation.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.giveThemSomethingUnlock.title",
                    Body = "tutorial.remembering.giveThemSomethingUnlock.body",
                    More = "tutorial.remembering.giveThemSomethingUnlock.more",
                    Kind = StepKind.Do,
                    Tab = TabWallpapers,
                    Anchor = "btn:addWallpaper",
                    AlsoAllow = new[] { "panel:wallpaperDetail" },
                    OnEnter = (vm, s) => s.Set("walls", vm.Wallpapers.Count),
                    // Picture AND gate: a wallpaper with no condition is simply always unlocked.
                    IsDone = (vm, s) => s.GrewSince("walls", vm.Wallpapers.Count) &&
                                        vm.SelectedWallpaper is { } w &&
                                        w.SpritePath.Trim().Length > 0 &&
                                        w.UnlockConditions.Count > 0,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.AddWallpaperCommand.Execute(null);
                        var w = vm.SelectedWallpaper!;
                        w.DisplayName = "Promise kept";   // English on purpose: the story pack's words.
                        w.SpritePath = TutorialAssets.Wallpaper;
                        w.AddUnlockConditionCommand.Execute(null);
                        var c = w.UnlockConditions.Last();
                        c.DisplayType = NodeConditionViewModel.VariableFamilyType;
                        c.VarName = KeptHerSecret;
                        c.VarValue = "true";
                    },
                    Hint = "tutorial.remembering.giveThemSomethingUnlock.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.whenOneValueNot.title",
                    Body = "tutorial.remembering.whenOneValueNot.body",
                    More = "tutorial.remembering.whenOneValueNot.more",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    OnEnter = (vm, s) => s.Set("lists", ListCount(vm)),
                    IsDone = (vm, s) => s.GrewSince("lists", ListCount(vm)),
                    Solve = (vm, s) =>
                    {
                        vm.AddVariableCommand.Execute(null);
                        vm.SelectedVariable!.Name = PosesSeen;
                        // The type is the whole point of the step: the check counts List variables.
                        vm.SelectedVariable!.Type = Model.PackVariableType.List;
                    },
                    Hint = "tutorial.remembering.whenOneValueNot.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.puttingThingsAskingThere.title",
                    Body = "tutorial.remembering.puttingThingsAskingThere.body",
                    More = "tutorial.remembering.puttingThingsAskingThere.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnEnter = (vm, s) => { if (HerSecret(vm) is { } d) vm.SelectedDialogue = d; },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => HasListAction(n)),
                    Solve = (vm, s) =>
                    {
                        var d = vm.SelectedDialogue ??= HerSecret(vm) ?? vm.Dialogues[^1];
                        var choice = d.Nodes.LastOrDefault(n => n.Kind == Model.DialogueNodeKind.Choice);
                        var last = (choice != null ? ClosingLine(d, choice) : null) ?? d.Nodes[^1];
                        vm.SelectedNode = last;
                        vm.AddNodeActionOnFinishCommand.Execute(null);
                        var a = last.ActionsOnFinish.Last();
                        a.DisplayType = Model.NodeActionTypes.AddToList;
                        a.Model.Params["list"] = PosesSeen;
                        a.Model.Params["value"] = "Shy";   // English on purpose: a value the pack keeps.
                        a.Model.Params["unique"] = "true";
                        a.Reread();
                    },
                    OnShown = vm => ShowHerLine(vm, ClosingLineOf),
                    Hint = "tutorial.remembering.puttingThingsAskingThere.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.listWillActuallyUse.title",
                    Body = "tutorial.remembering.listWillActuallyUse.body",
                    More = "tutorial.remembering.listWillActuallyUse.more",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.watchChangeSomething.title",
                    Body = "tutorial.remembering.watchChangeSomething.body",
                    More = "tutorial.remembering.watchChangeSomething.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "values-in-text",
            // 2: who the family tokens name, and what $ is for. 3 (1.10.0): "Dummy Days" - her confidence, her vase.
            Revision = 3,
            Group = "tutorial.group.logic",
            Title = "tutorial.valuesInText.title",
            Summary = "tutorial.valuesInText.summary",
            Makes = "tutorial.valuesInText.makes",
            Level = 14,
            Leaves = vm => vm.Variables.Any(v => v.Name.Trim() == Confidence),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.fourWaysTheyNot.title",
                    Body = "tutorial.valuesInText.fourWaysTheyNot.body",
                    More = "tutorial.valuesInText.fourWaysTheyNot.more",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.confidence.title",
                    Body = "tutorial.valuesInText.confidence.body",
                    More = "tutorial.valuesInText.confidence.more",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    OnEnter = (vm, s) => s.Set("ints", CountOf(vm, Model.PackVariableType.Int)),
                    IsDone = (vm, s) => s.GrewSince("ints", CountOf(vm, Model.PackVariableType.Int)) &&
                                        vm.SelectedVariable is { } v && !Regex.IsMatch(v.Name.Trim(), @"^var\d+$"),
                    Solve = (vm, s) =>
                    {
                        vm.AddVariableCommand.Execute(null);
                        var v = vm.SelectedVariable!;
                        v.Name = Confidence;
                        v.Type = Model.PackVariableType.Int;
                        v.DefaultValue = "1";
                    },
                    Hint = "tutorial.valuesInText.confidence.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.showVariableLine.title",
                    Body = "tutorial.valuesInText.showVariableLine.body",
                    More = "tutorial.valuesInText.showVariableLine.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:dialogueList" },
                    // Her dialogue and her last line, opened for the reader.
                    OnShown = vm => ShowHerLine(vm, ClosingLineOf),
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => n.Text.Contains("[PV:")),
                    Solve = (vm, s) =>
                    {
                        var d = vm.SelectedDialogue ??= HerSecret(vm) ?? vm.Dialogues[^1];
                        var line = vm.SelectedNode ?? d.Nodes[^1];
                        vm.SelectedNode = line;
                        line.Text = line.Text.TrimEnd() + " I'm at [PV:" + Confidence + "] out of 10 now.";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.valuesInText.showVariableLine.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.namesPlayerChose.title",
                    Body = "tutorial.valuesInText.namesPlayerChose.body",
                    More = "tutorial.valuesInText.namesPlayerChose.more",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.showPiece.title",
                    Body = "tutorial.valuesInText.showPiece.body",
                    More = "tutorial.valuesInText.showPiece.more",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    OnEnter = (vm, s) => s.Set("strings", CountOf(vm, Model.PackVariableType.String)),
                    IsDone = (vm, s) => s.GrewSince("strings", CountOf(vm, Model.PackVariableType.String)) &&
                                        vm.SelectedVariable is { } v && !Regex.IsMatch(v.Name.Trim(), @"^var\d+$"),
                    Solve = (vm, s) =>
                    {
                        vm.AddVariableCommand.Execute(null);
                        vm.SelectedVariable!.Name = ShowPiece;
                        vm.SelectedVariable!.Type = Model.PackVariableType.String;
                    },
                    Hint = "tutorial.valuesInText.showPiece.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.valueDoNotKnow.title",
                    Body = "tutorial.valuesInText.valueDoNotKnow.body",
                    More = "tutorial.valuesInText.valueDoNotKnow.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:dialogueList" },
                    OnEnter = (vm, s) => { if (HerSecret(vm) is { } d) vm.SelectedDialogue = d; },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && UsesADollarValue(d),
                    Solve = (vm, s) =>
                    {
                        var d = vm.SelectedDialogue ??= HerSecret(vm) ?? vm.Dialogues[^1];
                        var choice = d.Nodes.LastOrDefault(n => n.Kind == Model.DialogueNodeKind.Choice);
                        var last = (choice != null ? ClosingLine(d, choice) : null) ?? d.Nodes[^1];
                        vm.SelectedNode = last;
                        // First what is on show, then the switch that reads it.
                        vm.AddNodeActionOnFinishCommand.Execute(null);
                        var write = last.ActionsOnFinish.Last();
                        write.DisplayType = NodeActionViewModel.VariableFamilyType;
                        write.VarName = ShowPiece;
                        write.VarValue = StoryPack.VaseName;
                        vm.AddNodeActionOnFinishCommand.Execute(null);
                        var show = last.ActionsOnFinish.Last();
                        show.DisplayType = Model.NodeActionTypes.SetGameObjectActive;
                        show.Category = NodeActionViewModel.CatOverlay;
                        show.OverlayLevel = "place:" + (StoryPack.Gallery(vm) ?? vm.Places[^1]).Key;
                        show.Target = "$" + ShowPiece;
                        show.Active = true;
                    },
                    OnShown = vm => ShowHerLine(vm, ClosingLineOf),
                    Hint = "tutorial.valuesInText.valueDoNotKnow.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.oneLists.title",
                    Body = "tutorial.valuesInText.oneLists.body",
                    More = "tutorial.valuesInText.oneLists.more",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.whichOneGoesWhere.title",
                    Body = "tutorial.valuesInText.whichOneGoesWhere.body",
                    More = "tutorial.valuesInText.whichOneGoesWhere.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "rules",
            // 2 (1.10.0): "Dummy Days" - she poses for you once you've promised.
            Revision = 2,
            Group = "tutorial.group.logic",
            Title = "tutorial.rules.title",
            Summary = "tutorial.rules.summary",
            Makes = "tutorial.rules.makes",
            Level = 15,
            Leaves = vm => vm.IntegrationRules.Any(r => ShowsAScene(r)),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.rules.notEverythingWaitsSpoken.title",
                    Body = "tutorial.rules.notEverythingWaitsSpoken.body",
                    More = "tutorial.rules.notEverythingWaitsSpoken.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.scenesGameSBig.title",
                    Body = "tutorial.rules.scenesGameSBig.body",
                    More = "tutorial.rules.scenesGameSBig.more",
                    Kind = StepKind.Read,
                    Tab = TabScenes,
                    Anchor = "btn:addScene",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.makeOne.title",
                    Body = "tutorial.rules.makeOne.body",
                    More = "tutorial.rules.makeOne.more",
                    Kind = StepKind.Do,
                    Tab = TabScenes,
                    Anchor = "btn:addScene",
                    AlsoAllow = new[] { "panel:sceneDetail" },
                    // Not baselined on the count: someone re-taking this has the scene from last time,
                    // and the step asks only that a finished scene exists.
                    IsDone = (vm, s) => vm.SelectedScene is { } sc &&
                                        sc.SceneSprite.Trim().Length > 0 &&
                                        !TutorialCatalog.StillPlaceholder(sc.DisplayName, "scenes.newName"),
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.AddSceneCommand.Execute(null);
                        vm.SelectedScene!.SceneSprite = TutorialAssets.Scene;
                        vm.SelectedScene!.DisplayName = "Her first pose";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.rules.makeOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.frameSound.title",
                    Body = "tutorial.rules.frameSound.body",
                    More = "tutorial.rules.frameSound.more",
                    // Read: + Scene fills the frame in already.
                    Kind = StepKind.Read,
                    Tab = TabScenes,
                    Anchor = "panel:sceneDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.addRule.title",
                    Body = "tutorial.rules.addRule.body",
                    More = "tutorial.rules.addRule.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    AlsoAllow = new[] { "panel:ruleIdentity", "field:triggerMode" },
                    OnEnter = (vm, s) => s.Set("rules", vm.IntegrationRules.Count),
                    IsDone = (vm, s) => s.GrewSince("rules", vm.IntegrationRules.Count),
                    Solve = (vm, s) =>
                    {
                        vm.AddIntegrationRuleCommand.Execute(null);
                        vm.SelectedIntegrationRule!.DisplayName = "Her first pose";   // English on purpose: the story pack's words.
                        vm.SelectedIntegrationRule!.Description = "She poses once you've promised to keep her secret.";   // English on purpose: the story pack's words.
                    },
                    Hint = "tutorial.rules.addRule.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.decideWhenFires.title",
                    Body = "tutorial.rules.decideWhenFires.body",
                    More = "tutorial.rules.decideWhenFires.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "field:triggerMode",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.giveSomethingWatch.title",
                    Body = "tutorial.rules.giveSomethingWatch.body",
                    More = "tutorial.rules.giveSomethingWatch.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleConditions",
                    IsDone = (vm, s) => vm.SelectedIntegrationRule is { } r && r.Conditions.Count > 0,
                    Solve = (vm, s) =>
                    {
                        vm.AddIntegrationConditionCommand.Execute(null);
                        var c = vm.SelectedIntegrationRule!.Conditions.Last();
                        c.DisplayType = NodeConditionViewModel.VariableFamilyType;
                        c.VarName = KeptHerSecret;
                        c.VarValue = "true";
                    },
                    Hint = "tutorial.rules.giveSomethingWatch.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.somethingDo.title",
                    Body = "tutorial.rules.somethingDo.body",
                    More = "tutorial.rules.somethingDo.more",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "field:ruleActions",
                    AlsoAllow = new[] { "panel:ruleActionsBox" },
                    // Apart from the condition step: two things in two boxes behind one
                    // "waiting" leaves no way to tell which half is missing.
                    IsDone = (vm, s) => vm.SelectedIntegrationRule is { } r && ShowsAScene(r),
                    Solve = (vm, s) =>
                    {
                        vm.AddIntegrationActionCommand.Execute(null);
                        var a = vm.SelectedIntegrationRule!.Actions.Last();
                        // The type first, as an author does: the Category row shows only for Set active.
                        a.DisplayType = Model.NodeActionTypes.SetGameObjectActive;
                        a.Category = NodeActionViewModel.CatScene;
                        a.Target = vm.Scenes[^1].Key;
                        a.Active = true;
                    },
                    Hint = "tutorial.rules.somethingDo.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.watchThink.title",
                    Body = "tutorial.rules.watchThink.body",
                    More = "tutorial.rules.watchThink.more",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "field:ruleDebug",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.shape.title",
                    Body = "tutorial.rules.shape.body",
                    More = "tutorial.rules.shape.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

    };

    /// <summary>
    /// Whether this node's actions-on-finish actually set a pack variable to
    /// true.
    /// <para/>
    /// The step used to accept any action at all, which a freshly added row
    /// satisfies before a single field is filled in — right shape, does
    /// nothing, and the wallpaper then never unlocks with nothing to say why.
    /// Checking the value too is the difference between the tutorial working
    /// and the author reaching the last step to find that it does not.
    /// </summary>
    /// <summary>
    /// Whether a rule actually switches a scene on.
    /// <para/>
    /// A pack's scenes are built inactive and stay that way until something
    /// activates one, so "the rule has an action" is not the same claim as
    /// "the player will see the scene" — and a freshly added action row
    /// satisfies the first while never doing the second.
    /// </summary>
    /// <summary>
    /// The level a dialogue is gated on, read off the pinned LevelActive row
    /// that every dialogue carries at index 0 of its start conditions.
    /// <para/>
    /// Empty when there is no dialogue or the row has somehow gone, which a
    /// check should treat as "not chosen yet" rather than throwing.
    /// </summary>
    /// <summary>Who speaks the story's lines: the Dummy, or - in a pack without her - the first character the
    /// author made.</summary>
    internal static string Speaker(MainViewModel vm)
        => (StoryPack.Dummy(vm) ?? vm.Characters.FirstOrDefault(c => !c.IsPlayer && !c.Model.IsVanillaCharacter))?.Key ?? "";

    /// <summary>Her first outfit chosen on the Characters tab (the Dummy, or the first character the author made),
    /// for the steps that work in it; null when the pack has neither.</summary>
    private static ViewModel.OutfitViewModel? OpenHerOutfit(MainViewModel vm)
    {
        var her = StoryPack.Dummy(vm) ?? vm.Characters.FirstOrDefault(c => !c.IsPlayer && !c.Model.IsVanillaCharacter);
        if (her == null || her.Outfits.Count == 0) return vm.SelectedOutfit;
        vm.SelectedCharacter = her;
        // Opened in the tree too: her outfit chosen under a closed row showed nothing of where it was.
        her.IsExpanded = true;
        vm.SelectedOutfit = her.Outfits[0];
        return vm.SelectedOutfit;
    }

    /// <summary>Whether an outfit's face pictures come from the folder its bust does: a blink, mouths or
    /// expressions drawn for another bust sit where that bust's face was.</summary>
    private static bool FacesFollowTheBust(ViewModel.OutfitViewModel o)
    {
        static string Folder(string path)
        {
            string p = path.Trim().Replace('\\', '/');
            int cut = p.LastIndexOf('/');
            return cut < 0 ? "" : p.Substring(0, cut);
        }
        string bust = Folder(o.BaseSprite);
        return (!o.BlinkEnabled || Folder(o.BlinkSprite) == bust)
            && (!o.MouthEnabled || Folder(o.MouthPrefix) == bust)
            && (!o.ExpressionEnabled || Folder(o.ExpressionPrefix) == bust);
    }

    // The story's variables (tutorials 12 and 13). English on purpose: names the pack's own conditions and lines use.
    internal const string KeptHerSecret = "KeptHerSecret";
    internal const string PosesSeen = "PosesSeen";
    internal const string Confidence = "Confidence";
    internal const string ShowPiece = "ShowPiece";

    /// <summary>How many variables of one type the pack has.</summary>
    private static int CountOf(MainViewModel vm, Model.PackVariableType type) => vm.Variables.Count(v => v.Model.Type == type);

    /// <summary>The door's sound in a line (tutorial 11). English on purpose: text a line carries.</summary>
    internal const string DoorPattern = "*door*";

    /// <summary>The bedroom's extension with a door to a place of the pack (tutorial 2), or null.</summary>
    private static VanillaPlaceExtensionViewModel? BedroomDoor(MainViewModel vm)
        => vm.VanillaExtensions.FirstOrDefault(e => e.Source == "vanilla:5_MyRoom" &&
                                                    e.NavigatorButtons.Any(b => b.Target.StartsWith("self:", StringComparison.Ordinal)));

    /// <summary>The gallery's mannequin NPC (tutorial 9), or the first NPC of the pack; null when it has none.</summary>
    private static NpcViewModel? Mannequin(MainViewModel vm)
        => vm.Npcs.FirstOrDefault(n => string.Equals(n.DisplayName.Trim(), StoryPack.MannequinName, StringComparison.OrdinalIgnoreCase))
           ?? vm.Npcs.FirstOrDefault();

    /// <summary>Where a placement stands, to see whether it was moved.</summary>
    private static string Where(NpcPlacementViewModel? pl) => pl == null ? "" : pl.Body.X + "," + pl.Body.Y;

    private static bool Moved(NpcPlacementViewModel pl, string? was)
    {
        var at = (was ?? "").Split(',');
        if (at.Length != 2 || !float.TryParse(at[0], out float x) || !float.TryParse(at[1], out float y)) return Where(pl) != was;
        return Math.Abs(pl.Body.X - x) + Math.Abs(pl.Body.Y - y) > 0.25f;
    }

    /// <summary>The placement made last in the place chosen that names an NPC.</summary>
    private static NpcPlacementViewModel? NewestPlacement(MainViewModel vm)
        => vm.SelectedPlace?.NpcsNode.Npcs.LastOrDefault(n => !string.IsNullOrWhiteSpace(n.Npc));

    /// <summary>A placement's shadow moved under its pose's feet, read off the pose's picture.</summary>
    internal static void ShadowUnderFeet(MainViewModel vm, NpcPlacementViewModel placement)
    {
        var npc = vm.Npcs.FirstOrDefault(n => n.Key == placement.Npc);
        if (npc == null || vm.PackRoot == null || TutorialAssets.FeetOf(vm.PackRoot, npc.Sprite) is not { } feet) return;
        placement.Shadow.X = feet.X;
        placement.Shadow.Y = feet.Y;
    }

    /// <summary>One more placement of the mannequin, in the place chosen (her gallery when none is).</summary>
    private static void PlaceTheMannequin(MainViewModel vm)
    {
        vm.SelectedPlace ??= StoryPack.Gallery(vm) ?? vm.Places[^1];
        var placed = vm.SelectedPlace!.NpcsNode.AddNpc();
        placed.Npc = Mannequin(vm)!.Key;
    }

    /// <summary>Lines of any dialogue that put an expression on their speaker ("neutral", which a new line is
    /// given, is her plain face).</summary>
    private static int LinesWithAFace(MainViewModel vm)
        => vm.Dialogues.Sum(d => d.Nodes.Count(n => n.Actor.Trim().Length > 0 && n.Actor != "player" &&
                                                    n.Expression.Trim().Length > 0 &&
                                                    !n.Expression.Trim().Equals("neutral", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Her dialogue, "Her secret": the one that starts in her gallery (tutorial 5), or null.</summary>
    /// <summary>
    /// Her dialogue opened and the line a step talks about chosen, for a step's <see cref="TutorialStep.OnShown"/>
    /// (1.10.0): the review of every step's picture found steps about a line's actions arriving with no line
    /// chosen, so the box the step points at was empty.
    /// </summary>
    internal static void ShowHerLine(MainViewModel vm, Func<ViewModel.DialogueViewModel, ViewModel.DialogueNodeViewModel?> pick)
    {
        if (HerSecret(vm) is not { } d) return;
        if (!ReferenceEquals(vm.SelectedDialogue, d)) vm.SelectedDialogue = d;
        if (pick(d) is { } n) vm.SelectedNode = n;
    }

    /// <summary>Her last words before you go: the line both answers come back to.</summary>
    internal static ViewModel.DialogueNodeViewModel? ClosingLineOf(ViewModel.DialogueViewModel d)
    {
        var choice = d.Nodes.LastOrDefault(n => n.Kind == Model.DialogueNodeKind.Choice);
        return (choice != null ? ClosingLine(d, choice) : null) ?? d.Nodes.LastOrDefault();
    }

    /// <summary>Your promise to her. English on purpose: the story pack's words.</summary>
    internal static ViewModel.DialogueNodeViewModel? PromiseOf(ViewModel.DialogueViewModel d)
        // English on purpose: the story pack's words.
        => d.Nodes.FirstOrDefault(n => n.Text.Trim() == "I promise.");

    /// <summary>Her answer to your promise. English on purpose: the story pack's words.</summary>
    internal static ViewModel.DialogueNodeViewModel? SweetOf(ViewModel.DialogueViewModel d)
        // English on purpose: the story pack's words.
        => d.Nodes.FirstOrDefault(n => n.Text.StartsWith("You're sweet", StringComparison.Ordinal));

    internal static ViewModel.DialogueViewModel? HerSecret(MainViewModel vm)
        => StoryPack.Gallery(vm) is { } g ? vm.Dialogues.FirstOrDefault(d => LevelTokenOf(d) == "place:" + g.Key) : null;

    /// <summary>The line after a choice that is none of its branches: where its answers end up.</summary>
    private static ViewModel.DialogueNodeViewModel? ClosingLine(ViewModel.DialogueViewModel d, ViewModel.DialogueNodeViewModel choice)
    {
        var inside = new HashSet<int>();
        void Walk(int id)
        {
            if (!inside.Add(id)) return;
            if (d.Nodes.FirstOrDefault(x => x.Id == id) is { } n)
                foreach (var c in n.Model.Children) Walk(c);
        }
        foreach (var c in choice.Model.Children) Walk(c);
        int at = d.Nodes.IndexOf(choice);
        return d.Nodes.Skip(at + 1).FirstOrDefault(n => !inside.Contains(n.Id));
    }

    private static string LevelTokenOf(ViewModel.DialogueViewModel? d)
    {
        if (d == null || d.StartConditions.Count == 0) return "";
        var row = d.StartConditions[0];
        return row.Model.Params.TryGetValue("level", out var v) ? (v ?? "") : "";
    }

    /// <summary>
    /// Whether some node jumps to a tag another node really carries.
    /// <para/>
    /// Both halves are checked because either alone does nothing: a tag nobody
    /// jumps to is a label, and a jump to a tag nobody has stops the
    /// conversation dead. Only the pair is the lesson.
    /// </summary>
    /// <summary>
    /// Whether a prefix has a filename part, not just a folder.
    /// <para/>
    /// Setting the blink path copies its folder into the empty prefix fields,
    /// which is a head start rather than an answer: a prefix ending in a slash
    /// would send the game looking for 1.png at the top of that folder.
    /// </summary>
    private static bool NamesAFile(string prefix)
    {
        string p = (prefix ?? "").Trim();
        return p.Length > 0 && !p.EndsWith("/") && !p.EndsWith("\\\\");
    }

    /// <summary>Whether a node fires a sound through an action rather than
    /// through text. Either list counts: an effect on start and one on finish
    /// are both the action route, and which fits is the author's call.</summary>
    /// <summary>How many List-typed variables the pack declares.</summary>
    private static int ListCount(ViewModel.MainViewModel vm)
    {
        int n = 0;
        foreach (var v in vm.Variables)
            if (v.Model.Type == Model.PackVariableType.List) n++;
        return n;
    }

    /// <summary>Whether a node writes to a list. Add is what the step asks
    /// for; the other two are the same lesson, so an author who reached for
    /// Remove or Clear instead has understood it.</summary>
    private static bool HasListAction(ViewModel.DialogueNodeViewModel n)
    {
        foreach (var list in new[] { n.Model.ActionsOnStart, n.Model.ActionsOnFinish })
        {
            if (list == null) continue;
            foreach (var a in list)
                if ((a.Type == Model.NodeActionTypes.AddToList ||
                     a.Type == Model.NodeActionTypes.RemoveFromList ||
                     a.Type == Model.NodeActionTypes.ClearList) &&
                    a.Params.TryGetValue("list", out var l) &&
                    !string.IsNullOrWhiteSpace(l))
                    return true;
        }
        return false;
    }

    private static bool HasPlaySfx(ViewModel.DialogueNodeViewModel n)
    {
        foreach (var list in new[] { n.Model.ActionsOnStart, n.Model.ActionsOnFinish })
        {
            if (list == null) continue;
            foreach (var a in list)
                if (a.Type == Model.NodeActionTypes.PlaySFX &&
                    a.Params.TryGetValue("clip", out var c) &&
                    !string.IsNullOrWhiteSpace(c))
                    return true;
        }
        return false;
    }

    private static bool JumpLandsOnATag(ViewModel.DialogueViewModel d)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in d.Nodes)
            if (!string.IsNullOrWhiteSpace(n.Tag)) tags.Add(n.Tag.Trim());
        if (tags.Count == 0) return false;

        // Any direction. This used to insist the target come later in play
        // order, on the theory that jumping back writes a conversation the
        // player cannot leave — which is wrong. Jumping back to a Choice is a
        // standard shape (each option answers and returns, one option leaves),
        // and the step after this one now teaches it. The only jump that can
        // never do anything is a node aimed at its own tag, so that is the only
        // one refused here.
        foreach (var n in d.Nodes)
        {
            if (n.Model.Jump is not { Mode: Model.JumpMode.Jump } j) continue;
            if (string.IsNullOrWhiteSpace(j.TargetTag)) continue;

            string want = j.TargetTag.Trim();
            foreach (var other in d.Nodes)
            {
                if (ReferenceEquals(other, n)) continue;
                if (string.Equals(other.Tag?.Trim(), want,
                                  StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether any action in the dialogue takes its value from a variable
    /// rather than a typed-in one.
    /// <para/>
    /// Looks at the parameters as authored, since $ is a notation the runtime
    /// resolves rather than a field of its own — there is nothing else to
    /// check but the text the author put in the box.
    /// </summary>
    private static bool UsesADollarValue(ViewModel.DialogueViewModel d)
    {
        foreach (var n in d.Nodes)
            foreach (var list in new[] { n.Model.ActionsOnStart, n.Model.ActionsOnFinish })
            {
                if (list == null) continue;
                foreach (var a in list)
                    foreach (var kv in a.Params)
                    {
                        var v = kv.Value?.Trim();
                        // "$$" is an escaped dollar, not a reference.
                        if (!string.IsNullOrEmpty(v) && v.Length > 1 &&
                            v[0] == '$' && v[1] != '$')
                            return true;
                    }
            }
        return false;
    }

    private static bool ShowsAScene(ViewModel.UpdateRuleViewModel r)
    {
        foreach (var a in r.Actions)
            if (a.Category == ViewModel.NodeActionViewModel.CatScene &&
                !string.IsNullOrWhiteSpace(a.Target) && a.Active)
                return true;
        return false;
    }

    private static bool SetsAVariableTrue(ViewModel.DialogueNodeViewModel n)
    {
        foreach (var a in n.ActionsOnFinish)
        {
            if (a.Model.Type != Model.NodeActionTypes.SetVariable) continue;
            a.Model.Params.TryGetValue("name", out var name);
            a.Model.Params.TryGetValue("value", out var value);
            if (!string.IsNullOrWhiteSpace(name) &&
                string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A Choice that is actually finished: a prompt of its own, and at least
    /// two options that say something.
    /// <para/>
    /// All three matter because all three are rendered. The Choice node's text
    /// is the prompt shown beneath the options; each child's text is the label
    /// on its button. A blank one is not a missing line, it is a blank patch of
    /// UI — the shape that reaches the game looking like a fault.
    /// </summary>
    private static bool HasWrittenChoice(ViewModel.DialogueViewModel d)
    {
        var byId = new Dictionary<int, ViewModel.DialogueNodeViewModel>();
        foreach (var n in d.Nodes) byId[n.Id] = n;

        foreach (var n in d.Nodes)
        {
            if (n.Kind != Model.DialogueNodeKind.Choice) continue;
            if (n.Text.Trim().Length == 0) continue;     // no prompt yet
            int written = 0;
            foreach (var cid in n.Model.Children)
                if (byId.TryGetValue(cid, out var child) && child.Text.Trim().Length > 0)
                    written++;
            if (written >= 2) return true;
        }
        return false;
    }

    /// <summary>
    /// An Exit that actually cuts something short.
    /// <para/>
    /// Play runs down the list, so an Exit on the last node does exactly what
    /// Continue would — stop, because nothing follows. A step teaching the
    /// difference has to insist the Exit has something to skip, or it teaches
    /// nothing and the author sees two options behave identically.
    /// <para/>
    /// Sibling options under a Choice or Random do not count as "something
    /// after". They are alternatives: only one is ever taken, so an Exit on the
    /// first is not skipping the second.
    /// </summary>
    private static bool ExitSkipsSomething(ViewModel.DialogueViewModel d)
    {
        // A node's parent is whichever node lists it as a child.
        var parentOf = new Dictionary<int, int>();
        var kindOf = new Dictionary<int, Model.DialogueNodeKind>();
        foreach (var n in d.Nodes)
        {
            kindOf[n.Id] = n.Kind;
            foreach (var cid in n.Model.Children) parentOf[cid] = n.Id;
        }

        bool AlternativesTogether(int a, int b)
        {
            if (!parentOf.TryGetValue(a, out int pa)) return false;
            if (!parentOf.TryGetValue(b, out int pb)) return false;
            if (pa != pb) return false;
            return kindOf.TryGetValue(pa, out var k) &&
                   (k == Model.DialogueNodeKind.Choice || k == Model.DialogueNodeKind.Random);
        }

        for (int i = 0; i < d.Nodes.Count; i++)
        {
            if (d.Nodes[i].Model.Jump is not { Mode: Model.JumpMode.Exit }) continue;
            for (int j = i + 1; j < d.Nodes.Count; j++)
                if (!AlternativesTogether(d.Nodes[i].Id, d.Nodes[j].Id)) return true;
        }
        return false;
    }
}
