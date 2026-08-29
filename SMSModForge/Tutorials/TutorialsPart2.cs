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
            Group = "Dialogues",
            Title = "Your first conversation",
            Summary = "Write a dialogue that starts in your room, with a choice in it.",
            Level = 5,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "A dialogue is a tree",
                    Body = "Play runs down the node list from the top, and children are just " +
                           "nodes nested under a parent — reaching the end of a branch carries " +
                           "on to whatever is next in the list, not back out. Every node is one " +
                           "of three kinds. Text plays its line and moves on. Choice stops and " +
                           "offers its children to the player. Random picks one child itself. " +
                           "Everything else on a node — who speaks, what happens after, what it " +
                           "sets — hangs off that.",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                },
                new TutorialStep
                {
                    Title = "Add a dialogue",
                    Body = "Use + Dialogue. It needs a place to live in, which is why the room " +
                           "came first.",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "btn:addDialogue",
                    OnEnter = (vm, s) => s.Set("dlg", vm.Dialogues.Count),
                    IsDone = (vm, s) => s.GrewSince("dlg", vm.Dialogues.Count),
                    Hint = "+ Dialogue is in the toolbar above the list.",
                },
                new TutorialStep
                {
                    Title = "Say where it happens",
                    Body = "Set the level in the start conditions to the place you built. A " +
                           "dialogue with no level will never start, because nothing tells the " +
                           "game where to look for it.",
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
                    Hint = "Start conditions, then the level row pinned at the top.",
                },
                new TutorialStep
                {
                    Title = "Write a line",
                    Body = "Add a root node, choose your character as the actor, and give them " +
                           "something to say. One line is a conversation.",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "btn:addRootNode",
                    AlsoAllow = new[] { "panel:nodeEditor" },
                    OnEnter = (vm, s) => s.Set("nodes", vm.SelectedDialogue?.Nodes.Count ?? 0),
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && d.Nodes.Count > 0 &&
                                        vm.SelectedNode is { } n && n.Text.Trim().Length > 0,
                    Hint = "+ Root, then fill in Actor and Text in the Node box.",
                },
                new TutorialStep
                {
                    Title = "The rest of the Node box",
                    Body = "Actor is who speaks, and leaving it empty plays the line with " +
                           "whoever is already on screen. Expression and Outfit swap the bust " +
                           "mid-conversation — your practice character has neither, so leave " +
                           "them be. Tag names a node so a jump can find it. Duration decides " +
                           "whether the line waits for a click or times out on its own.",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "Give the player a say",
                    Body = "Add a second node, set its Kind to Choice, and hang two children " +
                           "off it with + Child. The Choice node's own Text is the prompt, " +
                           "shown under the options — it is what the player is answering. Each " +
                           "child IS an option, and the child's Text is the label on its button, " +
                           "not a line somebody says. Write all three: anything left blank shows " +
                           "up in game as blank, which is what an empty node looks like from " +
                           "the outside.",
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
                    Hint = "Set Kind to Choice, write its Text, + Child twice, then write each child's Text.",
                },
                new TutorialStep
                {
                    Title = "What happens after a node",
                    Body = "Jump decides where play goes next. Continue — the default — moves to " +
                           "the next node down the list, whether that is a child of this one or " +
                           "something further out. Exit stops the conversation and skips " +
                           "everything below. Jump resumes at whichever node carries the Tag you " +
                           "name.",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "Make Exit mean something",
                    Body = "At the very bottom of the list Continue and Exit are the same thing, " +
                           "because there is nothing left to skip either way. So give Exit " +
                           "something to skip: add one more root node below the choice with a " +
                           "closing line, then set ONE of your two options to Exit. Take that " +
                           "option in game and the conversation stops; take the other and it " +
                           "carries on into the closing line.",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueNodes",
                    AlsoAllow = new[] { "panel:nodeEditor" },
                    // Not just "an Exit exists" — an Exit with nothing after it
                    // is indistinguishable from Continue, which is exactly the
                    // exercise this replaced.
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && ExitSkipsSomething(d),
                    Hint = "+ Root for the closing line, then select an option and set Jump to Exit.",
                },
                new TutorialStep
                {
                    Title = "Actions are how a line changes things",
                    Body = "Actions on start and on finish hang off any node, and they are what " +
                           "makes a conversation matter: set a variable, unlock a wallpaper, " +
                           "swap a sprite, play a sound. Later tutorials use them properly. " +
                           "Worth knowing now that the line is the visible half and the actions " +
                           "are the other one.",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                },
                new TutorialStep
                {
                    Title = "Check it before you export",
                    Body = "Validate catches what the editor can see: an option with no text, " +
                           "an actor the pack never defines, a jump aimed at a tag no node " +
                           "carries, a level renamed underneath the dialogue. Run it now — a " +
                           "dialogue is the easiest thing in a pack to leave half-finished, " +
                           "because a blank node looks the same as a filled one in the tree.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "btn:validate",
                },
                new TutorialStep
                {
                    Title = "Go and have the conversation",
                    Body = "Export, load a save, and take the bedroom button through to your " +
                           "room. The dialogue starts on arrival, because that is what the " +
                           "level in its start conditions means. Reading your own line in the " +
                           "game tells you things the editor cannot — whether it is too long, " +
                           "and whether the choice actually reads as a choice.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "lines-that-choose",
            Group = "Dialogues",
            Title = "Lines that choose themselves",
            Summary = "Gate a line on the state of the room, and let two speakers share a scene.",
            Level = 6,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "Not every line should play",
                    Body = "So far every node in your dialogue runs when play reaches it. A " +
                           "conversation that reacts to anything needs the opposite: lines that " +
                           "sometimes do not.\n\n" +
                           "That is what a node's own Conditions box is for, and it behaves in " +
                           "two different ways depending on where the node sits. An ordinary " +
                           "node whose conditions fail is SKIPPED — play carries on to the next " +
                           "one. A node directly under a Choice is not offered at all: the " +
                           "player never sees that option, rather than seeing it greyed out.",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                },
                new TutorialStep
                {
                    Title = "Open the conversation you wrote",
                    Body = "Same pack, same dialogue. Pick it in the list, then select any node " +
                           "in the tree — the one you want the condition on.",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:dialogueList",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue != null && vm.SelectedNode != null,
                    Hint = "Click the dialogue, then a node in the list beside it.",
                },
                new TutorialStep
                {
                    Title = "Gate it on something real",
                    Body = "Add a condition to that node and make it GameObjectActive, pointing " +
                           "at the object you put in your room and left switched off.\n\n" +
                           "This is worth doing with something you already built rather than an " +
                           "invented example: the object is genuinely off, so the line genuinely " +
                           "will not play, and when a later rule switches the object on the line " +
                           "starts appearing. The conversation is reading the state of the room.",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                    IsDone = (vm, s) => vm.SelectedNode is { } n && n.Conditions.Count > 0,
                    Hint = "+ Add condition in the Conditions box, then pick the type.",
                },
                new TutorialStep
                {
                    Title = "Conditions do not stop at objects",
                    Body = "The same box offers everything the pack can ask about: which place " +
                           "is on screen, what a variable holds, whether it is raining, a " +
                           "percentage rolled once a day. Every condition in the list has to " +
                           "pass, and an empty list always passes — which is why a node with no " +
                           "conditions always plays.\n\n" +
                           "Any condition can also be negated, which is usually shorter than " +
                           "writing the opposite one.",
                    Kind = StepKind.Read,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeConditions",
                },
                new TutorialStep
                {
                    Title = "Someone else in the room",
                    Body = "A conversation is rarely one voice. The Actor field on a node says " +
                           "who speaks it, and it holds from that node onward until another one " +
                           "changes it — you do not set it per line.\n\n" +
                           "Every pack has a speaker it did not create: the player. Add a node " +
                           "and set its Actor to the player, and the line is the player talking " +
                           "back. Any mod addressing you reaches that same character, which is " +
                           "why its name and colour are fixed and yours to use rather than " +
                           "define.",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => n.Actor == "player" &&
                                                         n.Text.Trim().Length > 0),
                    Hint = "Add a node, write a line, then set Actor to player.",
                },
                new TutorialStep
                {
                    Title = "Bringing branches back together",
                    Body = "Two options that both end the conversation are easy. Two that " +
                           "rejoin a shared ending are what tags are for.\n\n" +
                           "Give the node you want to come back to a Tag — any short word. Then " +
                           "on another node set Jump to Jump and put that word in Jump tag. Play " +
                           "resumes there instead of carrying on down the list.\n\n" +
                           "Give a tag to exactly one node. Two nodes with the same tag makes " +
                           "every jump aimed at it ambiguous, and it resolves to whichever the " +
                           "game reaches first.",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && JumpLandsOnATag(d),
                    Hint = "Tag on one node, then Jump = Jump and Jump tag on another.",
                },
                new TutorialStep
                {
                    Title = "A conversation with a memory of the room",
                    Body = "Your dialogue now has a line that only plays when the room is in a " +
                           "particular state, a second speaker, and a branch that rejoins.\n\n" +
                           "What it still cannot do is remember anything across visits — the " +
                           "room's state is the room's, not the conversation's. That is what " +
                           "variables are for, and they are the next group.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "a-face-that-moves",
            Group = "Characters",
            Title = "A face that moves",
            Summary = "Give a bust its blink, its mouth and its expressions.",
            Level = 7,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "A bust is a stack of pictures",
                    Body = "The base sprite is the whole character. Everything else is a small " +
                           "picture laid over the same 256 by 256 frame, in the same place, " +
                           "swapped in when the game wants it.\n\n" +
                           "There are three kinds. A BLINK frame is the eyes closed, shown for a " +
                           "fifth of a second every few seconds. Four MOUTH frames cycle while a " +
                           "line types, so the character looks like they are speaking. Four " +
                           "EXPRESSIONS — Happy, Angry, Sad and Flirty — are swapped in by a " +
                           "dialogue line and stay until something changes them.\n\n" +
                           "Each is drawn on a transparent background at the same size as the " +
                           "base, so it lines up. Draw only the part that changes: for a blink, " +
                           "that is a pair of closed eyes and nothing else.",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                },
                new TutorialStep
                {
                    Title = "Open the character you made",
                    Body = "This carries on in the same pack. Overlays belong to an OUTFIT, not " +
                           "to the character, because a character in a different outfit blinks " +
                           "with different eyes. Click your character in the tree, then the " +
                           "outfit underneath it.",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:characterTree",
                    IsDone = (vm, s) => vm.SelectedCharacter != null && vm.SelectedOutfit != null,
                    Hint = "Expand the character to see its outfits, and click one.",
                },
                new TutorialStep
                {
                    Title = "Give it a blink",
                    Body = "Tick Has Blink frame, then point Blink at the blink art for the bust " +
                           "you chose — in TutorialArt/Busts, beside the base sprite, named for " +
                           "that bust. Bust1's is Bust1Blink.png.\n\n" +
                           "Remember what the tickbox means: ticked with nothing behind it is a " +
                           "broken outfit, and the character will not appear at all.",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitSprites",
                    AlsoAllow = new[] { "field:hasBlink" },
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.BlinkEnabled && o.BlinkSprite.Trim().Length > 0,
                    Hint = "Has Blink frame, then Browse on the Blink row under Sprites.",
                },
                new TutorialStep
                {
                    Title = "Watch it happen",
                    Body = "Tick Blinking under the preview. The eyes shut every few seconds, on " +
                           "a random wait, exactly as the game runs it — so if the blink art is " +
                           "off by a few pixels you will see it here rather than in game.",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterPreview",
                },
                new TutorialStep
                {
                    Title = "Four mouths, named by a prefix",
                    Body = "Mouth frames are not chosen one by one. You give a PREFIX, and the " +
                           "game adds 1, 2, 3 and 4 to it — so a prefix of " +
                           "TutorialArt/Busts/Bust1/Mouth finds Mouth1.png through Mouth4.png.\n\n" +
                           "That is exactly four frames: not three, not five. Tick Has Mouth " +
                           "frames and type the prefix, without the number and without .png.",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitSprites",
                    AlsoAllow = new[] { "field:hasMouth" },
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.MouthEnabled && o.MouthPrefix.Trim().Length > 0,
                    Hint = "Has Mouth frames, then the Mouth prefix box below it.",
                },
                new TutorialStep
                {
                    Title = "Hear it talk",
                    Body = "Tick Yapping under the preview and the mouth cycles the way it does " +
                           "while a line types. Mouth frame beside it holds one frame still, " +
                           "which is how you check a single drawing.\n\n" +
                           "Both are preview controls. Neither changes the pack.",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterPreview",
                },
                new TutorialStep
                {
                    Title = "Four expressions, named the same way",
                    Body = "Expressions work like mouths, except the game appends a NAME rather " +
                           "than a number — and only four names exist: Happy, Angry, Sad and " +
                           "Flirty. A prefix of TutorialArt/Busts/Bust1/Expression finds " +
                           "ExpressionHappy.png and its three siblings.\n\n" +
                           "Any other name is never looked for. If you want a fifth mood, it has " +
                           "to be a different outfit.",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitExpressions",
                    AlsoAllow = new[] { "field:hasExpressions" },
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        o.ExpressionEnabled && o.ExpressionPrefix.Trim().Length > 0,
                    Hint = "Has Expressions, then the Expr. prefix box in the same panel.",
                },
                new TutorialStep
                {
                    Title = "Try them on",
                    Body = "Expression under the preview swaps between them. In a conversation " +
                           "this is a node's Expression field, and it holds until another node " +
                           "changes it — an angry line does not go back to neutral by itself.\n\n" +
                           "That is the whole bust: one base picture, one blink, four mouths, " +
                           "four expressions. Everything a character does on screen comes out of " +
                           "those, and you now have all of them.",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterPreview",
                },
            },
        },

        new TutorialDef
        {
            Id = "making-it-move",
            Group = "Characters",
            Title = "Making it move",
            Summary = "Paint a jiggle mask and tune it until the bust moves the way you want.",
            Level = 8,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "A mask is not artwork",
                    Body = "It records how much of an effect each part of a sprite gets, and it " +
                           "holds three separate layers. Bounce moves those pixels up and down. " +
                           "Wave slides them side to side, in a wave that travels up the sprite. " +
                           "Noise scatters them irregularly. A fourth value, the overall " +
                           "intensity, scales all three at once — so black is no movement, and " +
                           "brighter is more. Painting a mask is deciding where a bust moves, " +
                           "which way, and by how much.",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                },
                new TutorialStep
                {
                    Title = "Open the character you made",
                    Body = "This carries on in the same pack, on the same character as the " +
                           "first tutorial — masks belong to an outfit, so there has to be one " +
                           "selected before anything here applies to it. Click your character " +
                           "in the tree, then the outfit underneath it.",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:characterTree",
                    IsDone = (vm, s) => vm.SelectedCharacter != null && vm.SelectedOutfit != null,
                    Hint = "Expand the character to see its outfits, and click one.",
                },
                new TutorialStep
                {
                    Title = "Pick a bust with something to move",
                    Body = "The five practice busts differ in shape on purpose, and the one you " +
                           "chose first was picked before any of that mattered. Point this " +
                           "outfit's base sprite at one of the fuller ones instead — the effect " +
                           "is far easier to judge when there is more of it to see.",
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
                    Hint = "TutorialArt/Busts/Bust4 or Bust5 give you the most to work with.",
                },
                new TutorialStep
                {
                    Title = "Paint where it should move",
                    Body = "Open Edit Mask and paint the Bounce layer over the parts that should " +
                           "move. Keep away from the edges — a mask that reaches the outline " +
                           "drags the whole silhouette and reads as a wobble rather than weight.",
                    Kind = StepKind.Free,
                    Tab = TabCharacters,
                    Anchor = "btn:editMask",
                    // Free: any mask counts. Where the paint goes is the craft, and
                    // this tutorial can only point at it.
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o && o.MaskSprite.Trim().Length > 0,
                    Hint = "Edit Mask, brush with B, paint, then Save (Ctrl+S) in that window.",
                },
                new TutorialStep
                {
                    Title = "What the Jiggle numbers do",
                    Body = "The mask says WHERE the sprite moves; these say how. Strength is how " +
                           "far the pixels travel and Speed how fast the cycle runs. Wave frequency " +
                           "is how many waves fit across the sprite, so a low number sways and a " +
                           "high one ripples. The three Noise settings keep it from ticking like " +
                           "a metronome — Scale is how fine the grain is, Speed how fast it " +
                           "drifts, Strength how much of it shows through. Tint multiplies the " +
                           "sprite's colour and is almost always left white. The defaults are a " +
                           "sensible bust: change one at a time and watch the preview, because " +
                           "the numbers mean far less than what you see.",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "field:jiggle",
                },
                new TutorialStep
                {
                    Title = "What the preview can show you",
                    Body = "Breathing is already on, and it is the mask doing its work — the " +
                           "fastest way to tell whether you painted too much. Depth beside it is " +
                           "how pronounced that idle motion is. The other controls drive the " +
                           "overlays: Blinking cycles the blink frame, Yapping runs the mouth, " +
                           "Mouth frame holds one frame still so you can look at it, and " +
                           "Expression swaps in one of the four overlay faces. On this character " +
                           "those four do nothing, because you turned blink, mouth and " +
                           "expressions off in the first tutorial — which is itself worth seeing " +
                           "once, so you recognise it later.",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterPreview",
                    // The toggles are siblings of the preview control, not
                    // children, so the preview alone leaves them under the dim.
                    AlsoAllow = new[] { "panel:bustPreviewPane" },
                },
                new TutorialStep
                {
                    Title = "Then judge it in the game",
                    Body = "Export and go through to your room, so the character speaks with " +
                           "the mask running. The preview and the game use the same code and " +
                           "the same numbers, but a bust at the size players see it, mid-line, " +
                           "is where too much movement gives itself away.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "populating",
            Group = "NPCs",
            Title = "Populating the room",
            Summary = "Add standing figures to your place, with shadows that sit them on the floor.",
            Level = 9,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "NPCs are scenery, not speakers",
                    Body = "They stand in rooms and make them look lived in. Anyone who talks is " +
                           "a character instead — the two are separate on purpose, because most " +
                           "figures in a room never say anything.",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                },
                new TutorialStep
                {
                    Title = "Define one",
                    Body = "Use + NPC and point it at one of the poses in TutorialArt/NPCs. " +
                           "Defining an NPC does not put it anywhere yet — that comes next.",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "btn:addNpc",
                    AlsoAllow = new[] { "panel:npcDetail" },
                    OnEnter = (vm, s) => s.Set("npcs", vm.Npcs.Count),
                    IsDone = (vm, s) => s.GrewSince("npcs", vm.Npcs.Count) &&
                                        vm.SelectedNpc is { } n && n.Sprite.Trim().Length > 0,
                    Hint = "+ NPC, then set the Pose sprite.",
                },
                new TutorialStep
                {
                    Title = "The droplets you do not want",
                    Body = "Wet particles are the droplet effect the game uses for swim and " +
                           "shower variants. They are off for a new NPC, which is what you want " +
                           "here — on a dry figure standing in a room they read as rain indoors. " +
                           "Tick Enabled when you do want them, and the emitter's position is " +
                           "then set per placement rather than here, because the same NPC can " +
                           "stand in more than one room.",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "panel:npcWet",
                },
                new TutorialStep
                {
                    Title = "Sit them on the floor",
                    Body = "The shadow under an NPC is already on, and it is the cheapest thing " +
                           "there is for how a room reads: without one a figure looks pasted onto " +
                           "the picture rather than standing in it. Colour sets how dark it is, " +
                           "and Sorting order how far forward it draws — well under the figure, " +
                           "so it never crosses a leg. Worth turning OFF only for someone who is " +
                           "not standing on anything, like a face at a window.",
                    // Read, not Do: the box is ticked when the NPC is created,
                    // so asking for it was a step that passed on arrival.
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "field:npcShadow",
                    Hint = "Shadow, then tick Enabled.",
                },
                new TutorialStep
                {
                    Title = "Give them some life",
                    Body = "Paint a jiggle mask for the NPC, the same way you did for a bust. " +
                           "A standing figure with no mask is completely still, which reads as " +
                           "a cardboard cut-out the moment anything else on screen moves.",
                    Kind = StepKind.Free,
                    Tab = TabNpcs,
                    Anchor = "btn:editMaskNpc",
                    IsDone = (vm, s) => vm.SelectedNpc is { } n && n.Mask.Trim().Length > 0,
                    Hint = "Edit Mask, beside the Jiggle mask field.",
                },
                new TutorialStep
                {
                    Title = "Put them in the room",
                    Body = "Back on Places, use + Add NPC on the place you built, and choose the " +
                           "one you just made. The same NPC can stand in as many rooms as you " +
                           "like — you are placing a copy, not moving the original.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addNpcToPlace",
                    // Three places again. The + button and the row it creates
                    // are on the right; choosing WHICH place to add to happens
                    // in the list on the left, and a step that says "on the
                    // place you built" is unfinishable without it.
                    AlsoAllow = new[] { "panel:placeGameObjects", "panel:placeList" },
                    OnEnter = (vm, s) => s.Set("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0),
                    IsDone = (vm, s) => s.GrewSince("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0),
                    Hint = "+ Add NPC, under GameObjects on the Places tab.",
                },
                new TutorialStep
                {
                    Title = "Switch them on",
                    Body = "A placement starts INACTIVE. The default assumes the common pattern — " +
                           "a parked variant that some rule switches on later — and yours has " +
                           "nothing to switch it on. Tick Start active on the placement, or the " +
                           "room will be empty when you walk into it and nothing will say why.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    AlsoAllow = new[] { "panel:placeList" },
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NpcsNode.Npcs.Any(n => n.StartActive),
                    Hint = "Start active, on the placement's own row under GameObjects.",
                },
                new TutorialStep
                {
                    Title = "Place them properly",
                    Body = "Drag them somewhere that makes sense for the room, and check the " +
                           "sorting order so they are not standing behind the furniture. The " +
                           "preview shows it at the size players will see.",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                },
                new TutorialStep
                {
                    Title = "See whether the room reads",
                    Body = "Export and walk in through the bedroom button. A room with people " +
                           "in it is the first time a place stops looking like artwork, and " +
                           "standing in it is the only way to tell whether they are in the way " +
                           "or in the right place.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "npcs-that-belong",
            Group = "NPCs",
            Title = "One person, two places",
            Summary = "Reuse an NPC, and give it a reflection and a blink so it sits in the room.",
            Level = 10,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "A definition is not an appearance",
                    Body = "The NPCs tab does not put anybody in a room. It DEFINES one: the " +
                           "art, the movement, the blink, the shadow. Putting them somewhere is " +
                           "a separate act, on the Places tab, and it is called a placement.\n\n" +
                           "One definition can have any number of placements — the same figure " +
                           "in three rooms, or twice in one. Change the definition and every " +
                           "placement changes with it. Change a placement and only that one " +
                           "moves.\n\n" +
                           "That split is the whole point of the tab. It is also the thing to " +
                           "get straight before you wonder why editing one NPC moved another.",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                },
                new TutorialStep
                {
                    Title = "Put the same person in twice",
                    Body = "Go to your room and add the NPC you already made a SECOND time. Two " +
                           "placements, one definition — the same drawing standing in two spots.\n\n" +
                           "In a real pack this is a crowd: one figure, placed six times at " +
                           "different positions and scales, is six people as far as the player " +
                           "is concerned.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addNpcToPlace",
                    AlsoAllow = new[] { "panel:placeGameObjects" },
                    OnEnter = (vm, s) => s.Set("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0),
                    IsDone = (vm, s) => s.GrewSince("placed", vm.SelectedPlace?.NpcsNode.Npcs.Count ?? 0),
                    Hint = "+ Add NPC on your place, then pick the NPC you defined.",
                },
                new TutorialStep
                {
                    Title = "Tell them apart",
                    Body = "Give the new placement its own Name. This is the placement's name, " +
                           "not the NPC's: it is what a Set-Active action points at when a rule " +
                           "wants to hide THIS one and leave the other standing.\n\n" +
                           "Two placements with the same name in one room is the mistake to " +
                           "avoid — a rule aiming at that name reaches whichever the game finds " +
                           "first, and which one that is may change.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NpcsNode.Npcs.Count >= 2 &&
                                        p.NpcsNode.Npcs.Select(n => n.Name.Trim())
                                         .Where(n => n.Length > 0).Distinct().Count() >= 2,
                    Hint = "Select the new placement and type a Name unlike the other one's.",
                },
                new TutorialStep
                {
                    Title = "Back to the definition",
                    Body = "Now change something on the NPC itself and watch BOTH placements " +
                           "follow. Open the NPCs tab and select the NPC you defined — the " +
                           "next two steps act on it.",
                    // Read, not Do: a selection made earlier is still a selection,
                    // so asking for one is a step that passes before it is read.
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "panel:npcDetail",
                },
                new TutorialStep
                {
                    Title = "Stand them on something",
                    Body = "Turn on Reflection. It draws a mirrored copy beneath the pose, and " +
                           "it is what makes a figure look like it is standing on a wet street " +
                           "or a polished floor rather than in front of one.\n\n" +
                           "It is off by default because most floors are not reflective, and a " +
                           "reflection on a carpet reads as a mistake. Alpha is how visible it " +
                           "is — the game's own reflections are a faint wash, not a second " +
                           "character — and Y offset moves it to meet the feet rather than the " +
                           "middle of the sprite.",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "panel:npcReflection",
                    IsDone = (vm, s) => vm.SelectedNpc is { ReflectionEnabled: true },
                    Hint = "Enabled, in the Reflection box.",
                },
                new TutorialStep
                {
                    Title = "Give them eyes that close",
                    Body = "An NPC blinks the same way a bust does, from one eyes-closed frame " +
                           "drawn over the pose. Point Blink sprite at art for it — the practice " +
                           "NPC art will do to see the mechanism.\n\n" +
                           "Open wait min and max are the range the gap between blinks is drawn " +
                           "from, so a room full of the same figure does not blink in unison, " +
                           "and Closed hold is how long each blink lasts. Leave a blank blink " +
                           "sprite and the NPC simply does not blink, which is a fine answer.",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "panel:npcBlink",
                    IsDone = (vm, s) => vm.SelectedNpc is { } n && n.BlinkSprite.Trim().Length > 0,
                    Hint = "Blink sprite, in the Blink box.",
                },
                new TutorialStep
                {
                    Title = "The one to leave alone",
                    Body = "Wet particles are the droplet effect, and they are off by default " +
                           "for a reason: they suit somebody who has just come out of the water " +
                           "and look like a fault on anybody else. Enabled turns the effect on " +
                           "at all; Start active decides whether it is already running when the " +
                           "room loads, rather than waiting for something to start it.\n\n" +
                           "Leave both off. Knowing where they are is enough.",
                    Kind = StepKind.Read,
                    Tab = TabNpcs,
                    Anchor = "panel:npcWet",
                },
                new TutorialStep
                {
                    Title = "A room with people in it",
                    Body = "Both placements now reflect and blink, because both are the same " +
                           "definition — and they stand in different spots under different " +
                           "names, because a placement is its own thing.\n\n" +
                           "That is the shape to remember: art and behaviour on the NPC, " +
                           "position and identity on the placement. A crowd is one definition " +
                           "and many placements; a cast is many definitions.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "sound-in-a-room",
            Group = "Media",
            Title = "Sound in a room",
            Summary = "Add a music track and a sound effect, and make one fire from a line.",
            Level = 11,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "Two different jobs",
                    Body = "Music is the track underneath everything: one at a time, looping, " +
                           "changed by an action or by a map button. SFX are one-shots — a door, " +
                           "a slap, a splash — and several can play at once without cutting each " +
                           "other off.\n\n" +
                           "Both take OGG, WAV or MP3, so use whatever your source gives you " +
                           "rather than converting for the sake of it. The practice files are " +
                           "deliberately one of each: an MP3 track and a WAV effect.\n\n" +
                           "Keep music long enough to loop without becoming obvious, and effects " +
                           "short. An effect that runs for two seconds is still playing when the " +
                           "line that fired it has gone.",
                    Kind = StepKind.Read,
                    Tab = TabMusic,
                },
                new TutorialStep
                {
                    Title = "Add a track",
                    Body = "Use + Music, then point Audio path at the practice track in " +
                           "TutorialArt/Audio. Give it a display name you would recognise in a " +
                           "list of twenty.",
                    Kind = StepKind.Do,
                    Tab = TabMusic,
                    Anchor = "btn:addMusic",
                    AlsoAllow = new[] { "panel:musicDetail" },
                    IsDone = (vm, s) => vm.SelectedMusic is { } m &&
                                        m.AudioPath.Trim().Length > 0,
                    Hint = "+ Music, then Audio path in the pane on the right.",
                },
                new TutorialStep
                {
                    Title = "Hear it without leaving the editor",
                    Body = "Play sounds the track at the volume beside it. It plays ONCE here " +
                           "whatever Loop says — looping is a runtime behaviour, and the preview " +
                           "is for checking the file is the file you meant.\n\n" +
                           "Loop and Volume can both be left blank, and blank means \"do what " +
                           "the game normally does\" rather than zero. Fill them in only when " +
                           "you want to differ from that.",
                    Kind = StepKind.Read,
                    Tab = TabMusic,
                    Anchor = "panel:musicDetail",
                },
                new TutorialStep
                {
                    Title = "Add an effect",
                    Body = "Now the SFX tab. Same shape: + SFX, then Audio path at the door " +
                           "sound in TutorialArt/Audio.\n\n" +
                           "Default volume is what plays when something asks for this effect " +
                           "without saying how loud — which is most of the time.",
                    Kind = StepKind.Do,
                    Tab = TabSfx,
                    Anchor = "btn:addSfx",
                    AlsoAllow = new[] { "panel:sfxDetail" },
                    IsDone = (vm, s) => vm.SelectedSfx is { } fx &&
                                        fx.AudioPath.Trim().Length > 0,
                    Hint = "+ SFX, then Audio path in the pane on the right.",
                },
                new TutorialStep
                {
                    Title = "The part you would never guess",
                    Body = "An effect does not need an action. Auto-trigger patterns is a " +
                           "comma-separated list of text, and whenever one of those appears in a " +
                           "line of dialogue the effect fires by itself.\n\n" +
                           "The convention is a lowercase word between asterisks — *door* — " +
                           "because it reads as a stage direction in the script and never " +
                           "collides with an ordinary word. Put a pattern in now.\n\n" +
                           "This is how a scene gets its sound without every line carrying an " +
                           "action. It is also completely invisible from the tab, which is the " +
                           "only reason this step exists.",
                    Kind = StepKind.Do,
                    Tab = TabSfx,
                    Anchor = "panel:sfxDetail",
                    IsDone = (vm, s) => vm.SelectedSfx is { } fx &&
                                        fx.TextPatternsCsv.Trim().Length > 0,
                    Hint = "Auto-trigger patterns, on the SFX you just made. Try *door*.",
                },
                new TutorialStep
                {
                    Title = "Say it in a line",
                    Body = "Go back to your conversation and put the pattern into a node's text " +
                           "— the actual characters, asterisks and all. When that line plays, " +
                           "the effect plays with it.\n\n" +
                           "The pattern stays in the text as written, so pick something you are " +
                           "content for a player to read, or keep it to a line where a stage " +
                           "direction belongs.",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes" },
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d &&
                                        d.Nodes.Any(n => n.Text.Contains('*')),
                    Hint = "Select a node and add your pattern to its Text.",
                },
                new TutorialStep
                {
                    Title = "More than one take",
                    Body = "One more thing with no field for it. Put extra recordings beside the " +
                           "first with _1, _2, _3 on the end of the same name, and the game picks " +
                           "between them at random each time the effect plays.\n\n" +
                           "Audio/Door.wav picks up Audio/Door_1.wav and Audio/Door_2.wav on its " +
                           "own. Number them without gaps — counting stops at the first missing " +
                           "number, so _1, _2, _4 loads two of the three. The extensions do not " +
                           "have to match, so a WAV alongside an OGG is fine.\n\n" +
                           "It is the difference between a door that sounds the same every time " +
                           "and one that does not.",
                    Kind = StepKind.Read,
                    Tab = TabSfx,
                    Anchor = "panel:sfxDetail",
                },
                new TutorialStep
                {
                    Title = "A pack that makes noise",
                    Body = "You have a track, an effect, and a line that fires it without being " +
                           "asked. Scenes and Wallpapers are the other two media tabs and you " +
                           "have already met both — a scene in the rules tutorial, a wallpaper " +
                           "in the variables one.\n\n" +
                           "That is every tab in the editor. What is left is not more kinds of " +
                           "thing, it is more of what you can already do.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "remembering",
            Group = "Logic",
            Title = "Remembering things",
            Summary = "Give the pack a memory, and use it to gate what players can see.",
            Level = 12,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "Packs forget by default",
                    Body = "Every conversation starts the same way unless something remembers " +
                           "what happened. Variables are that memory, and almost everything " +
                           "interesting in a pack is built on them.",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                },
                new TutorialStep
                {
                    Title = "Declare one",
                    Body = "Press + Variable. It arrives called var1, with Type already set to " +
                           "Bool — a yes/no value, which is what you want. Change Name to " +
                           "something that says what it MEANS: Met_Anna reads far better in a " +
                           "condition six months from now than Flag_3 or var1 does. No spaces, " +
                           "and you will be picking this name from a list later, so make it one " +
                           "you will recognise.",
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
                    Hint = "+ Variable, then type over var1 in the Name box.",
                },
                new TutorialStep
                {
                    Title = "Persisted, and why it is already on",
                    Body = "Persisted is ticked for you, and it means the value is written to " +
                           "the pack's save file — so what the player did is still true next " +
                           "time they load. Leave it on here. You would untick it for something " +
                           "that only matters during one visit, like a flag saying \"this " +
                           "conversation already greeted them\", which should start fresh every " +
                           "session rather than being remembered forever.",
                    Kind = StepKind.Read,
                    Tab = TabVariables,
                    Anchor = "field:persisted",
                },
                new TutorialStep
                {
                    Title = "Set it from a conversation",
                    Body = "Nothing has changed the variable yet — declaring one only says it " +
                           "exists. Four things, in order. One: on the Dialogues tab, click a " +
                           "node in the tree to select it. Two: under Actions on finish, press " +
                           "+ Add action. Three: set that row's Type to Variable, and leave " +
                           "Operation on Set and Source on Pack. Four: choose your variable in " +
                           "the Variable box, and tick Set to true — a Bool gets a tick box " +
                           "rather than a text field, so there is no spelling to get wrong. " +
                           "Actions on finish runs when the player moves past the line, so the " +
                           "value changes once they have read it.",
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
                    Hint = "+ Add action, Type = Variable, Operation = Set, pick your Variable, tick it true.",
                },
                new TutorialStep
                {
                    Title = "Give them something to unlock",
                    Body = "Now something the variable can gate. Press + Wallpaper and set its " +
                           "Sprite to TutorialArt/Wallpapers/DummyWPP.png. Then press " +
                           "+ Add condition and fill that row in: Type is VariableEquals, " +
                           "Variable is the one you named, and tick is true. That is the whole " +
                           "mechanism — one place writes the value, another asks what it says.",
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
                    Hint = "+ Wallpaper, set Sprite, then + Add condition with Type = VariableEquals.",
                },
                new TutorialStep
                {
                    Title = "Watch it change something",
                    Body = "Export and go through. Check the wallpaper is locked, have the " +
                           "conversation that sets your variable, then check again. That " +
                           "before-and-after is the whole point of state, and it is the one " +
                           "thing no preview can show you.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "rules",
            Group = "Logic",
            Title = "Rules that run themselves",
            Summary = "Make the pack do something without anyone talking to it.",
            Level = 13,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "Not everything waits to be spoken to",
                    Body = "Integration rules watch the game and act on their own — moving " +
                           "characters around, rebuilding lists, reacting to the day changing. " +
                           "This is where a pack stops being a set of conversations.",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                },
                new TutorialStep
                {
                    Title = "Scenes are the game's big pictures",
                    Body = "A scene is a full-screen CG: the art, a frame around it, and a sound " +
                           "when it appears. Most of what players remember about this game is " +
                           "scenes, so they are worth understanding properly. A pack's scenes are " +
                           "built switched OFF — something has to turn one on, which is what you " +
                           "are about to make a rule do.",
                    Kind = StepKind.Read,
                    Tab = TabScenes,
                    Anchor = "btn:addScene",
                },
                new TutorialStep
                {
                    Title = "Make one",
                    Body = "Press + Scene, set Sprite path to " +
                           "TutorialArt/Scenes/Dummy/DummyScene01.png, and change Display name " +
                           "off \"New Scene\" — that is the name a gallery shows.",
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
                                        sc.DisplayName.Trim().Length > 0 &&
                                        !sc.DisplayName.Trim().StartsWith("New ", StringComparison.OrdinalIgnoreCase),
                    Hint = "+ Scene, then the Sprite path and Display name rows.",
                },
                new TutorialStep
                {
                    Title = "The frame and the sound",
                    Body = "A new scene already has a Vanilla frame — one of the game's own " +
                           "borders, so your CG sits in the picture the way the rest do. Custom " +
                           "frame takes your own art instead. Mode decides what plays when the " +
                           "scene appears: the prototype carries its own audio trigger, and " +
                           "picking kiss or flash swaps that for one of the game's signals, " +
                           "while Silent strips it. Change them if you like — nothing here has " +
                           "to be touched.",
                    // A Read, because AddScene fills the frame in already. Asking
                    // for something the editor has done for you is a step that
                    // completes itself and teaches nothing.
                    Kind = StepKind.Read,
                    Tab = TabScenes,
                    Anchor = "panel:sceneDetail",
                },
                new TutorialStep
                {
                    Title = "Sound comes from two other tabs",
                    Body = "Music holds background tracks, and an action switches between " +
                           "them. SFX holds one-shot effects, played by an action the same " +
                           "way. Both take an OGG, WAV or MP3 from your pack folder, and both " +
                           "have a Play button so you can hear one without starting the game.",
                    Kind = StepKind.Read,
                    Tab = TabMusic,
                },
                new TutorialStep
                {
                    Title = "The trick worth knowing about SFX",
                    Body = "An effect does not need an action at all. Auto-trigger patterns is " +
                           "a list of words that fire it on their own whenever one of them " +
                           "shows up in a line, written between asterisks by convention, like " +
                           "*plap*. Drop extra recordings beside the first one named _1, _2 " +
                           "and so on and the game picks between them at random, so a sound " +
                           "used often does not wear thin. Neither is discoverable from the " +
                           "tab, which is the only reason this step exists.",
                    Kind = StepKind.Read,
                    Tab = TabSfx,
                },
                new TutorialStep
                {
                    Title = "Add a rule",
                    Body = "Use + Rule. Give it a description while you are there — in six " +
                           "months the conditions will still be readable and the reason will " +
                           "not.",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "btn:addRule",
                    OnEnter = (vm, s) => s.Set("rules", vm.IntegrationRules.Count),
                    IsDone = (vm, s) => s.GrewSince("rules", vm.IntegrationRules.Count),
                    Hint = "+ Rule, in the toolbar above the list.",
                },
                new TutorialStep
                {
                    Title = "Decide when it fires",
                    Body = "OnRisingEdge fires once when the conditions start passing, which is " +
                           "what you want almost every time. WhilePassing fires every frame they " +
                           "hold — reach for it only to keep something continuously in step.",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "field:triggerMode",
                },
                new TutorialStep
                {
                    Title = "Give it something to watch",
                    Body = "Under Conditions, press + Add condition and gate it on the variable " +
                           "you made in the last tutorial. A rule with no conditions has nothing " +
                           "to wait for.",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "panel:ruleConditions",
                    IsDone = (vm, s) => vm.SelectedIntegrationRule is { } r && r.Conditions.Count > 0,
                    Hint = "+ Add condition, in the Conditions box above the Actions one.",
                },
                new TutorialStep
                {
                    Title = "And something to do",
                    Body = "Under Actions, press + Add action. Set Type to Set active, Category " +
                           "to Scene, and Target to the scene you built, leaving Active ticked. " +
                           "That is what finally puts it on screen: a scene is built switched " +
                           "off, and this is the switch.",
                    Kind = StepKind.Do,
                    Tab = TabIntegration,
                    Anchor = "field:ruleActions",
                    AlsoAllow = new[] { "panel:ruleActionsBox" },
                    // Split from the condition step deliberately. Asking for two
                    // things in two different boxes behind one "waiting…" label
                    // gives an author who has done half of it no way to tell
                    // which half is missing — which is exactly how it failed.
                    IsDone = (vm, s) => vm.SelectedIntegrationRule is { } r && ShowsAScene(r),
                    Hint = "+ Add action, Type = Set active, Category = Scene, pick your scene.",
                },
                new TutorialStep
                {
                    Title = "Watch it think",
                    Body = "Tick Set for condition debugging and the game logs what this rule " +
                           "decided, whenever that changes. It is the fastest way to find out " +
                           "why a rule did nothing — and worth clearing again before you share " +
                           "the pack.",
                    Kind = StepKind.Read,
                    Tab = TabIntegration,
                    Anchor = "field:ruleDebug",
                },
                new TutorialStep
                {
                    Title = "That is the shape of it",
                    Body = "Characters, places, conversations, art, memory and rules — and one " +
                           "door from the bedroom to go and stand in all of it. Three things " +
                           "these seven never made you build: Map Buttons, which put a place on " +
                           "the world map rather than on another room's strip; Music; and SFX. " +
                           "All three work like everything you have already done, and the " +
                           "Documentation section on this tab covers them field by field — " +
                           "start with Start here if any of it stopped making sense.",
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
    private static bool JumpLandsOnATag(ViewModel.DialogueViewModel d)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in d.Nodes)
            if (!string.IsNullOrWhiteSpace(n.Tag)) tags.Add(n.Tag.Trim());
        if (tags.Count == 0) return false;

        foreach (var n in d.Nodes)
            if (n.Model.Jump is { Mode: Model.JumpMode.Jump } j &&
                !string.IsNullOrWhiteSpace(j.TargetTag) &&
                tags.Contains(j.TargetTag.Trim()))
                return true;
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
