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
    private const int TabWallpapers = 9;
    private const int TabVariables = 10;
    private const int TabIntegration = 11;
    private const int TabModForge = 0;

    internal static IReadOnlyList<TutorialDef> All { get; } = new[]
    {
        new TutorialDef
        {
            Id = "first-conversation",
            Title = "Your first conversation",
            Summary = "Write a dialogue that starts in your room, with a choice in it.",
            Level = 3,
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
                    IsDone = (vm, s) => vm.SelectedDialogue is { } d && d.StartConditions.Count > 0,
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
            Id = "making-it-move",
            Title = "Making it move",
            Summary = "Paint a jiggle mask and tune it until the bust moves the way you want.",
            Level = 4,
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
                           "far the pixels travel and Speed how fast the cycle runs. Frequency " +
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
            Title = "Populating the room",
            Summary = "Add standing figures to your place, with shadows that sit them on the floor.",
            Level = 5,
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
                    Body = "Turn on the shadow. Without one a figure looks pasted onto the room " +
                           "rather than standing in it — it is the cheapest thing you can do for " +
                           "how a room reads.",
                    Kind = StepKind.Do,
                    Tab = TabNpcs,
                    Anchor = "field:npcShadow",
                    IsDone = (vm, s) => vm.SelectedNpc is { ShadowEnabled: true },
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
            Id = "remembering",
            Title = "Remembering things",
            Summary = "Give the pack a memory, and use it to gate what players can see.",
            Level = 6,
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
            Title = "Rules that run themselves",
            Summary = "Make the pack do something without anyone talking to it.",
            Level = 7,
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
                    Title = "That is the whole editor",
                    Body = "Characters, places, conversations, art, memory and rules — and one " +
                           "door from the bedroom to go and stand in all of it. Everything else " +
                           "is more of the same shape, and the Documentation section covers the " +
                           "parts these tutorials did not reach.",
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
