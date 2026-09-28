using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
    private const int TabCharacters = 1;
    private const int TabNpcs = 2;
    private const int TabPlaces = 3;
    private const int TabDialogues = 5;
    private const int TabScenes = 6;
    private const int TabMusic = 7;
    private const int TabSfx = 8;
    private const int TabWallpapers = 9;
    private const int TabVariables = 10;
    private const int TabIntegration = 11;
    private const int TabModForge = 0;

    internal static IReadOnlyList<TutorialDef> All { get; } = new[]
    {
        new TutorialDef
        {
            Id = "first-conversation",
            Group = "tutorial.group.dialogues",
            Title = "tutorial.firstConversation.title",
            Summary = "tutorial.firstConversation.summary",
            Level = 5,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.dialogueTree.title",
                    Body = "tutorial.firstConversation.dialogueTree.body",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.addDialogue.title",
                    Body = "tutorial.firstConversation.addDialogue.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "btn:addDialogue",
                    OnEnter = (vm, s) => s.Set("dlg", vm.Dialogues.Count),
                    IsDone = (vm, s) => s.GrewSince("dlg", vm.Dialogues.Count),
                    Hint = "tutorial.firstConversation.addDialogue.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.sayWhereHappens.title",
                    Body = "tutorial.firstConversation.sayWhereHappens.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "field:startConditions",
                    // Baselined against the level the dialogue was born with.
                    // Counting start conditions was meaningless here: the
                    // pinned level row is injected on construction, so the
                    // check was true before the author had done anything and
                    // the step taught nothing. Asking for a CHANGE is the only
                    // way to know the level was actually chosen.
                    OnEnter = (vm, s) => s.Set("level", LevelTokenOf(vm.SelectedDialogue)),
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        LevelTokenOf(d).Length > 0 &&
                                        LevelTokenOf(d) != s.Get<string>("level"),
                    Hint = "tutorial.firstConversation.sayWhereHappens.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.writeLine.title",
                    Body = "tutorial.firstConversation.writeLine.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "btn:addRootNode",
                    AlsoAllow = new[] { "panel:nodeEditor" },
                    OnEnter = (vm, s) => s.Set("nodes", vm.SelectedDialogue?.Nodes.Count ?? 0),
                    // The actor is checked as well as the text. The step always
                    // asked for both and only ever looked at one, so a line
                    // with nobody speaking it passed — and a line with no
                    // actor shows no name in game.
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && d.Nodes.Count > 0 &&
                                        vm.SelectedNode is { } n &&
                                        n.Text.Trim().Length > 0 &&
                                        n.Actor.Trim().Length > 0,
                    Hint = "tutorial.firstConversation.writeLine.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.restNodeBox.title",
                    Body = "tutorial.firstConversation.restNodeBox.body",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.givePlayerSay.title",
                    Body = "tutorial.firstConversation.givePlayerSay.body",
                    Kind = StepKind.Free,
                    Tab = TabDialogues,
                    Anchor = "field:nodeKind",
                    // Three places, all needed: Kind is the anchor, + Child and
                    // selecting a node are in the tree, and the Text box for
                    // each option is in the Node box. Leaving the last one out
                    // is what made the options impossible to write.
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor" },
                    // Free in what the options SAY, strict that they say
                    // something. The old check counted children and passed on
                    // two blank ones, which is exactly the pack that throws in
                    // game — the tutorial was teaching the bug.
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && HasWrittenChoice(d),
                    Hint = "tutorial.firstConversation.givePlayerSay.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.happensAfterNode.title",
                    Body = "tutorial.firstConversation.happensAfterNode.body",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.makeExitMeanSomething.title",
                    Body = "tutorial.firstConversation.makeExitMeanSomething.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    AlsoAllow = new[] { "panel:nodeEditor" },
                    // Not just "an Exit exists" — an Exit with nothing after it
                    // is indistinguishable from Continue, which is exactly the
                    // exercise this replaced.
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && ExitSkipsSomething(d),
                    Hint = "tutorial.firstConversation.makeExitMeanSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.actionsLineChangesThings.title",
                    Body = "tutorial.firstConversation.actionsLineChangesThings.body",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.checkBeforeExport.title",
                    Body = "tutorial.firstConversation.checkBeforeExport.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "btn:validate",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstConversation.goHaveConversation.title",
                    Body = "tutorial.firstConversation.goHaveConversation.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "lines-that-choose",
            // Rewritten since authors first ran it: gates, jumps and the returning-menu shape.
            Revision = 2,
            Group = "tutorial.group.dialogues",
            Title = "tutorial.linesThatChoose.title",
            Summary = "tutorial.linesThatChoose.summary",
            Level = 6,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.notEveryLineShould.title",
                    Body = "tutorial.linesThatChoose.notEveryLineShould.body",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.openConversationWrote.title",
                    Body = "tutorial.linesThatChoose.openConversationWrote.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueList",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue != null && vm.SelectedNode != null,
                    Hint = "tutorial.linesThatChoose.openConversationWrote.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.gateSomethingReal.title",
                    Body = "tutorial.linesThatChoose.gateSomethingReal.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    // The old check counted rows, so a condition added and left
                    // blank passed the step — and a blank condition always
                    // passes at runtime too, so the author would have learned
                    // the shape of a gate that does not gate.
                    IsDone = (vm, s) => vm.SelectedNode is { } n &&
                                        n.Conditions.Any(c =>
                                            c.Model.Type == Model.NodeConditionTypes.GameObjectActive &&
                                            c.Model.Params.TryGetValue("target", out var t) &&
                                            !string.IsNullOrWhiteSpace(t)),
                    Hint = "tutorial.linesThatChoose.gateSomethingReal.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.conditionsDoNotStop.title",
                    Body = "tutorial.linesThatChoose.conditionsDoNotStop.body",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.someoneElseRoom.title",
                    Body = "tutorial.linesThatChoose.someoneElseRoom.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => n.Actor == "player" &&
                                                         n.Text.Trim().Length > 0),
                    Hint = "tutorial.linesThatChoose.someoneElseRoom.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.lettingOneOptionSay.title",
                    Body = "tutorial.linesThatChoose.lettingOneOptionSay.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && JumpLandsOnATag(d),
                    Hint = "tutorial.linesThatChoose.lettingOneOptionSay.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.jumpingBackwardsPurpose.title",
                    Body = "tutorial.linesThatChoose.jumpingBackwardsPurpose.body",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.linesThatChoose.conversationMemoryRoom.title",
                    Body = "tutorial.linesThatChoose.conversationMemoryRoom.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "a-face-that-moves",
            // Rewritten since authors first ran it: the prefix fields and the preview controls.
            Revision = 2,
            Group = "tutorial.group.characters",
            Title = "tutorial.aFaceThatMoves.title",
            Summary = "tutorial.aFaceThatMoves.summary",
            Level = 7,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.bustStackPictures.title",
                    Body = "tutorial.aFaceThatMoves.bustStackPictures.body",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.openCharacterMade.title",
                    Body = "tutorial.aFaceThatMoves.openCharacterMade.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:characterTree",
                    IsDone = (vm, s) => vm.SelectedCharacter != null && vm.SelectedOutfit != null,
                    Hint = "tutorial.aFaceThatMoves.openCharacterMade.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.giveBlink.title",
                    Body = "tutorial.aFaceThatMoves.giveBlink.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitSprites",
                    AlsoAllow = new[] { "field:hasBlink" },
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.BlinkEnabled && o.BlinkSprite.Trim().Length > 0,
                    Hint = "tutorial.aFaceThatMoves.giveBlink.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.watchHappen.title",
                    Body = "tutorial.aFaceThatMoves.watchHappen.body",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    // The preview pane, not the bust: panel:characterPreview is the
                    // art alone, so the toggles this step is about sat outside the
                    // lit area and could not be reached.
                    Anchor = "panel:bustPreviewPane",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.fourMouthsNamedPrefix.title",
                    Body = "tutorial.aFaceThatMoves.fourMouthsNamedPrefix.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitSprites",
                    AlsoAllow = new[] { "field:hasMouth" },
                    // A bare folder does not pass. The blink path seeds this field
                    // with one, so "not empty" would be true the moment the step
                    // opened and Next would light up for work nobody had done.
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.MouthEnabled && NamesAFile(o.MouthPrefix),
                    Hint = "tutorial.aFaceThatMoves.fourMouthsNamedPrefix.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.hearTalk.title",
                    Body = "tutorial.aFaceThatMoves.hearTalk.body",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:bustPreviewPane",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.fourExpressionsNamedSame.title",
                    Body = "tutorial.aFaceThatMoves.fourExpressionsNamedSame.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitExpressions",
                    AlsoAllow = new[] { "field:hasExpressions" },
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.ExpressionEnabled && NamesAFile(o.ExpressionPrefix),
                    Hint = "tutorial.aFaceThatMoves.fourExpressionsNamedSame.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aFaceThatMoves.tryThem.title",
                    Body = "tutorial.aFaceThatMoves.tryThem.body",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:bustPreviewPane",
                },
            },
        },

        new TutorialDef
        {
            Id = "making-it-move",
            // Rewritten since authors first ran it: where masks go, and why the face stays still.
            Revision = 2,
            Group = "tutorial.group.characters",
            Title = "tutorial.makingItMove.title",
            Summary = "tutorial.makingItMove.summary",
            Level = 8,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.maskNotArtwork.title",
                    Body = "tutorial.makingItMove.maskNotArtwork.body",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.openCharacterMade.title",
                    Body = "tutorial.makingItMove.openCharacterMade.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:characterTree",
                    IsDone = (vm, s) => vm.SelectedCharacter != null && vm.SelectedOutfit != null,
                    Hint = "tutorial.makingItMove.openCharacterMade.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.pickBustSomethingMove.title",
                    Body = "tutorial.makingItMove.pickBustSomethingMove.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "field:baseSprite",
                    AlsoAllow = new[] { "panel:characterTree" },
                    // Baselined, because the outfit already HAS a base sprite by
                    // now — the old check was "not empty", which was satisfied
                    // the moment the step opened, so it taught nothing and the
                    // author kept whichever bust they picked in tutorial 1.
                    OnEnter = (vm, s) => s.Set("bust", vm.SelectedOutfit?.BaseSprite ?? ""),
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.BaseSprite.Trim().Length > 0 &&
                                        o.BaseSprite != s.Get<string>("bust"),
                    Hint = "tutorial.makingItMove.pickBustSomethingMove.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.paintWhereShouldMove.title",
                    Body = "tutorial.makingItMove.paintWhereShouldMove.body",
                    Kind = StepKind.Free,
                    Tab = TabCharacters,
                    Anchor = "btn:editMask",
                    // Free: any mask counts. Where the paint goes is the craft, and
                    // this tutorial can only point at it.
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o && o.MaskSprite.Trim().Length > 0,
                    Hint = "tutorial.makingItMove.paintWhereShouldMove.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.jiggleNumbersDo.title",
                    Body = "tutorial.makingItMove.jiggleNumbersDo.body",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "field:jiggle",
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.dropletsOverHereToo.title",
                    Body = "tutorial.makingItMove.dropletsOverHereToo.body",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitParticles",
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.previewCanShow.title",
                    Body = "tutorial.makingItMove.previewCanShow.body",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterPreview",
                    // The toggles are siblings of the preview control, not
                    // children, so the preview alone leaves them under the dim.
                    AlsoAllow = new[] { "panel:bustPreviewPane" },
                },
                new TutorialStep
                {
                    Title = "tutorial.makingItMove.thenJudgeGame.title",
                    Body = "tutorial.makingItMove.thenJudgeGame.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "populating",
            // Rewritten since authors first ran it: placements, and the preview gizmo.
            Revision = 2,
            Group = "tutorial.group.npcs",
            Title = "tutorial.populating.title",
            Summary = "tutorial.populating.summary",
            Level = 9,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.populating.npcsSceneryNotSpeakers.title",
                    Body = "tutorial.populating.npcsSceneryNotSpeakers.body",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.defineOne.title",
                    Body = "tutorial.populating.defineOne.body",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "btn:addNpc",
                    AlsoAllow = new[] { "panel:npcDetail" },
                    OnEnter = (vm, s) => s.Set("npcs", vm.Npcs.Count),
                    IsDone = (vm, s) => s.GrewSince("npcs", vm.Npcs.Count) &&
                                        vm.SelectedNpc is { } n && n.Sprite.Trim().Length > 0,
                    Hint = "tutorial.populating.defineOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.dropletsDoNotWant.title",
                    Body = "tutorial.populating.dropletsDoNotWant.body",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "panel:npcWet",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.sitThemFloor.title",
                    Body = "tutorial.populating.sitThemFloor.body",
                    // Read, not Do: the box is ticked when the NPC is created,
                    // so asking for it was a step that passed on arrival.
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "field:npcShadow",
                    Hint = "tutorial.populating.sitThemFloor.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.giveThemSomeLife.title",
                    Body = "tutorial.populating.giveThemSomeLife.body",
                    Kind = StepKind.Free,
                    Tab = TabNpcs,
                    Anchor = "btn:editMaskNpc",
                    IsDone = (vm, s) => vm.SelectedNpc is { } n && n.Mask.Trim().Length > 0,
                    Hint = "tutorial.populating.giveThemSomeLife.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.putThemRoom.title",
                    Body = "tutorial.populating.putThemRoom.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addNpcToPlace",
                    // Three places again. The + button and the row it creates
                    // are on the right; choosing WHICH place to add to happens
                    // in the list on the left, and a step that says "on the
                    // place you built" is unfinishable without it.
                    AlsoAllow = new[] { "panel:placeGameObjects", "panel:placeList" },
                    OnEnter = (vm, s) => s.Set("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0),
                    // Adding the row is half of it. The NPC field decides who is
                    // standing there, and a placement that never gets one is the
                    // quiet kind of mistake: the room simply has nobody in it.
                    IsDone = (vm, s) => s.GrewSince("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0) &&
                                        vm.SelectedPlace is { } pl &&
                                        pl.NpcsNode.Npcs.Any(n => !string.IsNullOrWhiteSpace(n.Npc)),
                    Hint = "tutorial.populating.putThemRoom.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.switchThem.title",
                    Body = "tutorial.populating.switchThem.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    AlsoAllow = new[] { "panel:placeList" },
                    // The same row from the previous step, so it has to be the
                    // one that actually names somebody.
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NpcsNode.Npcs.Any(n => n.StartActive &&
                                                                 !string.IsNullOrWhiteSpace(n.Npc)),
                    Hint = "tutorial.populating.switchThem.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.placeThemProperly.title",
                    Body = "tutorial.populating.placeThemProperly.body",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                },
                new TutorialStep
                {
                    Title = "tutorial.populating.seeWhetherRoomReads.title",
                    Body = "tutorial.populating.seeWhetherRoomReads.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "npcs-that-belong",
            // Rewritten since authors first ran it: NPC blink art, and positioning a reflection.
            Revision = 2,
            Group = "tutorial.group.npcs",
            Title = "tutorial.npcsThatBelong.title",
            Summary = "tutorial.npcsThatBelong.summary",
            Level = 10,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.definitionNotAppearance.title",
                    Body = "tutorial.npcsThatBelong.definitionNotAppearance.body",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.putSamePersonTwice.title",
                    Body = "tutorial.npcsThatBelong.putSamePersonTwice.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addNpcToPlace",
                    AlsoAllow = new[] { "panel:placeGameObjects" },
                    OnEnter = (vm, s) => s.Set("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0),
                    IsDone = (vm, s) => s.GrewSince("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0),
                    Hint = "tutorial.npcsThatBelong.putSamePersonTwice.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.tellThemApart.title",
                    Body = "tutorial.npcsThatBelong.tellThemApart.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NpcsNode.Npcs.Count >= 2 &&
                                        p.NpcsNode.Npcs.Select(n => n.Name.Trim())
                                         .Where(n => n.Length > 0).Distinct().Count() >= 2,
                    Hint = "tutorial.npcsThatBelong.tellThemApart.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.backDefinition.title",
                    Body = "tutorial.npcsThatBelong.backDefinition.body",
                    // Read, not Do: a selection made earlier is still a selection,
                    // so asking for one is a step that passes before it is read.
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "panel:npcDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.standThemSomething.title",
                    Body = "tutorial.npcsThatBelong.standThemSomething.body",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "panel:npcReflection",
                    IsDone = (vm, s) => vm.SelectedNpc is { ReflectionEnabled: true },
                    Hint = "tutorial.npcsThatBelong.standThemSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.giveThemEyesClose.title",
                    Body = "tutorial.npcsThatBelong.giveThemEyesClose.body",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "panel:npcBlink",
                    IsDone = (vm, s) => vm.SelectedNpc is { } n && n.BlinkSprite.Trim().Length > 0,
                    Hint = "tutorial.npcsThatBelong.giveThemEyesClose.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.oneLeaveAlone.title",
                    Body = "tutorial.npcsThatBelong.oneLeaveAlone.body",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "panel:npcWet",
                },
                new TutorialStep
                {
                    Title = "tutorial.npcsThatBelong.roomPeople.title",
                    Body = "tutorial.npcsThatBelong.roomPeople.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "sound-in-a-room",
            // Rewritten since authors first ran it: playing a track, and the PlaySFX action.
            Revision = 2,
            Group = "tutorial.group.media",
            Title = "tutorial.soundInARoom.title",
            Summary = "tutorial.soundInARoom.summary",
            Level = 11,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.twoDifferentJobs.title",
                    Body = "tutorial.soundInARoom.twoDifferentJobs.body",
                    Kind = StepKind.Read,
                    Tab = TabMusic,
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.addTrack.title",
                    Body = "tutorial.soundInARoom.addTrack.body",
                    Kind = StepKind.Do,
                    Tab = TabMusic,
                    Anchor = "btn:addMusic",
                    AlsoAllow = new[] { "panel:musicDetail" },
                    IsDone = (vm, s) => vm.SelectedMusic is { } m &&
                                        m.AudioPath.Trim().Length > 0,
                    Hint = "tutorial.soundInARoom.addTrack.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.hearWithoutLeavingEditor.title",
                    Body = "tutorial.soundInARoom.hearWithoutLeavingEditor.body",
                    Kind = StepKind.Read,
                    Tab = TabMusic,
                    Anchor = "panel:musicDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.makeSomethingPlay.title",
                    Body = "tutorial.soundInARoom.makeSomethingPlay.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:extNavigatorButtons",
                    AlsoAllow = new[] { "panel:placeList" },
                    IsDone = (vm, s) => vm.VanillaExtensions.Any(
                        e => e.NavigatorButtons.Any(
                            b => b.Music.Trim().Length > 0 &&
                                 vm.Music.Any(m => m.Key == b.Music.Trim()))),
                    Hint = "tutorial.soundInARoom.makeSomethingPlay.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.addEffect.title",
                    Body = "tutorial.soundInARoom.addEffect.body",
                    Kind = StepKind.Do,
                    Tab = TabSfx,
                    Anchor = "btn:addSfx",
                    AlsoAllow = new[] { "panel:sfxDetail" },
                    IsDone = (vm, s) => vm.SelectedSfx is { } fx &&
                                        fx.AudioPath.Trim().Length > 0,
                    Hint = "tutorial.soundInARoom.addEffect.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.partWouldNeverGuess.title",
                    Body = "tutorial.soundInARoom.partWouldNeverGuess.body",
                    Kind = StepKind.Do,
                    Tab = TabSfx,
                    Anchor = "panel:sfxDetail",
                    IsDone = (vm, s) => vm.SelectedSfx is { } fx &&
                                        fx.TextPatternsCsv.Trim().Length > 0,
                    Hint = "tutorial.soundInARoom.partWouldNeverGuess.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.sayLine.title",
                    Body = "tutorial.soundInARoom.sayLine.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => n.Text.Contains('*')),
                    Hint = "tutorial.soundInARoom.sayLine.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.otherWayFireOne.title",
                    Body = "tutorial.soundInARoom.otherWayFireOne.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => HasPlaySfx(n)),
                    Hint = "tutorial.soundInARoom.otherWayFireOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.moreThanOneTake.title",
                    Body = "tutorial.soundInARoom.moreThanOneTake.body",
                    Kind = StepKind.Read,
                    Tab = TabSfx,
                    Anchor = "panel:sfxDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.soundInARoom.packMakesNoise.title",
                    Body = "tutorial.soundInARoom.packMakesNoise.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "remembering",
            // Rewritten since authors first ran it: list variables.
            Revision = 3,
            Group = "tutorial.group.logic",
            Title = "tutorial.remembering.title",
            Summary = "tutorial.remembering.summary",
            Level = 12,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.remembering.packsForgetDefault.title",
                    Body = "tutorial.remembering.packsForgetDefault.body",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.declareOne.title",
                    Body = "tutorial.remembering.declareOne.body",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    OnEnter = (vm, s) => s.Set("vars", vm.Variables.Count),
                    // Rejecting the placeholder, the way the character-name step
                    // does: a variable still called var1 means the step was
                    // clicked through, and the name is the whole point of it.
                    IsDone = (vm, s) => s.GrewSince("vars", vm.Variables.Count) &&
                                        vm.SelectedVariable is { } v &&
                                        v.Name.Trim().Length > 0 &&
                                        !Regex.IsMatch(v.Name.Trim(), @"^var\d+$"),
                    Hint = "tutorial.remembering.declareOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.persistedWhyAlready.title",
                    Body = "tutorial.remembering.persistedWhyAlready.body",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                    Anchor = "field:persisted",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.setConversation.title",
                    Body = "tutorial.remembering.setConversation.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "field:actionsOnFinish",
                    // "Select a node" happens in the tree, which is a different
                    // group box from the actions list.
                    AlsoAllow = new[] { "panel:actionsOnFinishBox", "panel:dialogueNodes", "panel:dialogueList" },
                    // The old check was "has any action at all", which passed on
                    // a blank row: right shape, does nothing. This one insists
                    // the action is the one the step describes.
                    IsDone = (vm, s) => vm.SelectedNode is { } n && SetsAVariableTrue(n),
                    Hint = "tutorial.remembering.setConversation.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.giveThemSomethingUnlock.title",
                    Body = "tutorial.remembering.giveThemSomethingUnlock.body",
                    Kind = StepKind.Do,
                    Tab = TabWallpapers,
                    Anchor = "btn:addWallpaper",
                    AlsoAllow = new[] { "panel:wallpaperDetail" },
                    OnEnter = (vm, s) => s.Set("walls", vm.Wallpapers.Count),
                    // Sprite AND gate: a wallpaper with art but no condition is
                    // simply always unlocked, which is the one outcome this
                    // tutorial exists to avoid.
                    IsDone = (vm, s) => s.GrewSince("walls", vm.Wallpapers.Count) &&
                                        vm.SelectedWallpaper is { } w &&
                                        w.SpritePath.Trim().Length > 0 &&
                                        w.UnlockConditions.Count > 0,
                    Hint = "tutorial.remembering.giveThemSomethingUnlock.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.whenOneValueNot.title",
                    Body = "tutorial.remembering.whenOneValueNot.body",
                    Kind = StepKind.Do,
                    Tab = TabVariables,
                    Anchor = "btn:addVariable",
                    AlsoAllow = new[] { "panel:variableDetail" },
                    OnEnter = (vm, s) => s.Set("lists", ListCount(vm)),
                    IsDone = (vm, s) => s.GrewSince("lists", ListCount(vm)),
                    Hint = "tutorial.remembering.whenOneValueNot.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.puttingThingsAskingThere.title",
                    Body = "tutorial.remembering.puttingThingsAskingThere.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => HasListAction(n)),
                    Hint = "tutorial.remembering.puttingThingsAskingThere.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.listWillActuallyUse.title",
                    Body = "tutorial.remembering.listWillActuallyUse.body",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                },
                new TutorialStep
                {
                    Title = "tutorial.remembering.watchChangeSomething.title",
                    Body = "tutorial.remembering.watchChangeSomething.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "values-in-text",
            // Rewritten since authors first ran it: who the family tokens name, and what $ is for.
            Revision = 2,
            Group = "tutorial.group.logic",
            Title = "tutorial.valuesInText.title",
            Summary = "tutorial.valuesInText.summary",
            Level = 13,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.fourWaysTheyNot.title",
                    Body = "tutorial.valuesInText.fourWaysTheyNot.body",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.showVariableLine.title",
                    Body = "tutorial.valuesInText.showVariableLine.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => n.Text.Contains("[PV:")),
                    Hint = "tutorial.valuesInText.showVariableLine.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.namesPlayerChose.title",
                    Body = "tutorial.valuesInText.namesPlayerChose.body",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.valueDoNotKnow.title",
                    Body = "tutorial.valuesInText.valueDoNotKnow.body",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:actionsOnFinishBox",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:nodeEditor", "panel:variableDetail" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && UsesADollarValue(d),
                    Hint = "tutorial.valuesInText.valueDoNotKnow.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.oneLists.title",
                    Body = "tutorial.valuesInText.oneLists.body",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                },
                new TutorialStep
                {
                    Title = "tutorial.valuesInText.whichOneGoesWhere.title",
                    Body = "tutorial.valuesInText.whichOneGoesWhere.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "rules",
            Group = "tutorial.group.logic",
            Title = "tutorial.rules.title",
            Summary = "tutorial.rules.summary",
            Level = 14,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.rules.notEverythingWaitsSpoken.title",
                    Body = "tutorial.rules.notEverythingWaitsSpoken.body",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.scenesGameSBig.title",
                    Body = "tutorial.rules.scenesGameSBig.body",
                    Kind = StepKind.Read,
                    Tab = TabScenes,
                    Anchor = "btn:addScene",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.makeOne.title",
                    Body = "tutorial.rules.makeOne.body",
                    Kind = StepKind.Do,
                    Tab = TabScenes,
                    Anchor = "btn:addScene",
                    AlsoAllow = new[] { "panel:sceneDetail" },
                    // Deliberately NOT baselined against the scene count. Anyone
                    // re-taking this tutorial already has the scene from last
                    // time, and demanding a SECOND one blocks them on work they
                    // have already done. What the step is really asking for is
                    // that a finished scene exists, so that is what it checks.
                    IsDone = (vm, s) => vm.SelectedScene is { } sc &&
                                        sc.SceneSprite.Trim().Length > 0 &&
                                        !TutorialCatalog.StillPlaceholder(sc.DisplayName, "scenes.newName"),
                    Hint = "tutorial.rules.makeOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.frameSound.title",
                    Body = "tutorial.rules.frameSound.body",
                    // A Read, because AddScene fills the frame in already. Asking
                    // for something the editor has done for you is a step that
                    // completes itself and teaches nothing.
                    Kind = StepKind.Read,
                    Tab = TabScenes,
                    Anchor = "panel:sceneDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.soundComesTwoOther.title",
                    Body = "tutorial.rules.soundComesTwoOther.body",
                    Kind = StepKind.Read,
                    Tab = TabMusic,
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.trickWorthKnowingAbout.title",
                    Body = "tutorial.rules.trickWorthKnowingAbout.body",
                    Kind = StepKind.Read,
                    Tab = TabSfx,
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.addRule.title",
                    Body = "tutorial.rules.addRule.body",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    OnEnter = (vm, s) => s.Set("rules", vm.IntegrationRules.Count),
                    IsDone = (vm, s) => s.GrewSince("rules", vm.IntegrationRules.Count),
                    Hint = "tutorial.rules.addRule.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.decideWhenFires.title",
                    Body = "tutorial.rules.decideWhenFires.body",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "field:triggerMode",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.giveSomethingWatch.title",
                    Body = "tutorial.rules.giveSomethingWatch.body",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleConditions",
                    IsDone = (vm, s) => vm.SelectedIntegrationRule is { } r && r.Conditions.Count > 0,
                    Hint = "tutorial.rules.giveSomethingWatch.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.somethingDo.title",
                    Body = "tutorial.rules.somethingDo.body",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "field:ruleActions",
                    AlsoAllow = new[] { "panel:ruleActionsBox" },
                    // Split from the condition step deliberately. Asking for two
                    // things in two different boxes behind one "waiting…" label
                    // gives an author who has done half of it no way to tell
                    // which half is missing — which is exactly how it failed.
                    IsDone = (vm, s) => vm.SelectedIntegrationRule is { } r && ShowsAScene(r),
                    Hint = "tutorial.rules.somethingDo.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.watchThink.title",
                    Body = "tutorial.rules.watchThink.body",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "field:ruleDebug",
                },
                new TutorialStep
                {
                    Title = "tutorial.rules.shape.title",
                    Body = "tutorial.rules.shape.body",
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
