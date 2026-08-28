using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Tutorials;

/// <summary>
/// The tutorials offered on the ModForge tab, in the order they should be
/// taken. Each covers ground the ones before it did not, and none tries to
/// cover everything.
/// <para/>
/// A step's check asks about the PACK, not the click: it is satisfied by the
/// state actually changing, so an author who gets there by another route is
/// never told they did it wrong. That is also why a check has to be specific
/// enough to fail — one that a freshly added, still-empty row satisfies
/// teaches nothing and passes on arrival.
/// <para/>
/// Baselines live in the per-run <see cref="TutorialScratch"/> rather than in
/// fields here, so a check can ask whether something was ADDED — which is
/// almost always the real question, and is not the same as asking whether it
/// exists in a pack the author has already been working in.
/// <para/>
/// The smoke walkthrough at the end is not for authors: it proves the overlay,
/// the anchoring and the three step kinds still work after a change, which is
/// quicker than starting a real tutorial and clicking to the part that broke.
/// </summary>
public static class TutorialCatalog
{
    // Tab indices, mirroring MainWindow's constants.
    private const int TabModForge = 0;
    private const int TabCharacters = 1;
    private const int TabNpcs = 2;
    private const int TabPlaces = 3;
    private const int TabDialogues = 5;
    private const int TabScenes = 6;
    private const int TabWallpapers = 9;
    private const int TabVariables = 10;
    private const int TabIntegration = 11;

    private static readonly TutorialDef[] _first =
    {
        new TutorialDef
        {
            Id = "first-steps",
            Group = "Getting started",
            Title = "First steps",
            Summary = "Make a pack, put a character in it, and send it to the game.",
            Level = 1,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "What you will end up with",
                    Body = "A pack on disk with one character in it, ready for the game to " +
                           "load. Nothing here is throwaway — this is a real pack, and the next " +
                           "tutorial carries on in the same one.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
                new TutorialStep
                {
                    Title = "Start a fresh pack",
                    Body = "Use File then New pack. Starting clean matters here: the steps that " +
                           "follow watch for things appearing, and in a pack you have already " +
                           "been working in some of them are there before you begin.",
                    Kind = StepKind.Do,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                    // Identity, not emptiness: New pack swaps the whole ModPack
                    // instance, so this is exact and cannot be satisfied by
                    // emptying the current one by hand.
                    OnEnter = (vm, s) => s.Set("pack", vm.Pack),
                    IsDone = (vm, s) => !ReferenceEquals(vm.Pack, s.Get<Model.ModPack>("pack")),
                    Hint = "File then New pack. Anything unsaved will be offered back to you first.",
                },
                new TutorialStep
                {
                    Title = "Save it somewhere",
                    Body = "Use File then Save, and make a NEW empty folder for it — not your " +
                           "Desktop or Documents. A pack owns its whole folder: everything " +
                           "inside ends up in the exported file. Saving here also matters " +
                           "because previews and file pickers resolve paths against that " +
                           "folder, and stay empty until there is one.",
                    Kind = StepKind.Do,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                    IsDone = (vm, s) => !string.IsNullOrEmpty(vm.PackRoot),
                    Hint = "File then Save, or Ctrl+S. Make a new folder rather than reusing one.",
                },
                new TutorialStep
                {
                    Title = "Some art to work with",
                    Body = "Practice sprites have been copied into a TutorialArt folder inside " +
                           "your pack: busts, a room, a couple of NPCs and a scene. They are " +
                           "yours now — edit them, replace them, or delete the folder when you " +
                           "are done with it.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    // Copied on arrival rather than asked for: fetching files is
                    // not a skill this tutorial is here to teach.
                    OnEnter = (vm, s) => TutorialAssets.EnsureCopied(vm.PackRoot),
                },
                new TutorialStep
                {
                    Title = "Add a character",
                    Body = "Characters are anyone who speaks. Use + Character, which is the one " +
                           "that draws its own bust rather than borrowing the game's.",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "btn:addCharacter",
                    OnEnter = (vm, s) => s.Set("chars", vm.Characters.Count),
                    IsDone = (vm, s) => s.GrewSince("chars", vm.Characters.Count),
                    Hint = "+ Character sits in the toolbar above the character list.",
                },
                new TutorialStep
                {
                    Title = "Give them a name",
                    Body = "This is what players read above a line of dialogue, and the only " +
                           "name you have to write — the identifiers the pack uses internally " +
                           "follow from it.",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "field:characterName",
                    // Rejecting the placeholder is the point: a character still
                    // called "New Character" means the step was skipped.
                    IsDone = (vm, s) => vm.SelectedCharacter is { } c &&
                                        c.DisplayName.Trim().Length > 0 &&
                                        !c.DisplayName.StartsWith("New ", StringComparison.OrdinalIgnoreCase),
                    Hint = "Type over the placeholder name in the Identity box.",
                },
                new TutorialStep
                {
                    Title = "Give them a face",
                    Body = "Every character needs at least one outfit, and every outfit needs a " +
                           "base sprite: the picture of them from the chest up. Point this one " +
                           "at any bust in TutorialArt/Busts — they differ in shape, which will " +
                           "matter when you paint a jiggle mask later.\n\n" +
                           "Bust art is 256 by 256 pixels, PNG, with a transparent background. " +
                           "Other sizes are scaled to fit rather than refused, but 256 is the " +
                           "only size whose pixels land exactly as you drew them, and anything " +
                           "that is not square gets transparent bars on two sides. When you come " +
                           "to draw your own, that is the shape to draw.",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "field:baseSprite",
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o && o.BaseSprite.Trim().Length > 0,
                    Hint = "Use the Browse button beside Base, under Sprites.",
                },
                new TutorialStep
                {
                    Title = "There they are",
                    Body = "The preview draws the bust the way the game will, and keeps up as " +
                           "you work — worth watching rather than exporting to find out.",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterPreview",
                },
                new TutorialStep
                {
                    Title = "Say what this outfit has",
                    Body = "A bust can carry more than its base picture: a blink frame, four " +
                           "mouth frames that animate while it talks, and four expressions. " +
                           "The three tickboxes are how you DECLARE which of those exist — they " +
                           "are not features to switch on, they are you telling the game what " +
                           "art to go looking for.\n\n" +
                           "You are not using any of them yet, so untick all three: Has Blink " +
                           "frame, Has Mouth frames, Has Expressions. Getting this wrong is " +
                           "worth understanding now, because it fails silently — a ticked box " +
                           "with no art behind it reads as a BROKEN outfit, and the game skips " +
                           "the whole character rather than skipping the blink. A missing " +
                           "character with no error is almost always this.\n\n" +
                           "Leave Mask empty. An empty mask means the bust does not move, which " +
                           "is a perfectly good answer and the one you want until you paint one.",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    // The dim swallows clicks outside a lit hole, so a step that
                    // asks for controls in two boxes has to light both.
                    Anchor = "panel:outfitSprites",
                    AlsoAllow = new[] { "panel:outfitExpressions" },
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        !o.BlinkEnabled && !o.MouthEnabled && !o.ExpressionEnabled,
                    Hint = "Two tickboxes in the Sprites box, one in Expressions just below it.",
                },
                new TutorialStep
                {
                    Title = "That is a pack",
                    Body = "Save it, and File then Export bundles the folder into a single " +
                           ".smspack — the file the game loads, which goes in " +
                           "BepInEx/plugins/SMSModForge/ModPacks/. There is nothing to see in " +
                           "the game yet, because a character only appears once something gives " +
                           "them a line. A place of your own is next: it builds a room and puts " +
                           "a door to it in the bedroom you wake up in, and every tutorial after " +
                           "that goes through the same door.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "a-place",
            Group = "Places",
            Title = "A place of your own",
            Summary = "Build a room out of two layers, give it depth, and put it on the map.",
            Level = 2,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "Rooms are two pictures",
                    Body = "A place is built from a base layer — the room itself — and a " +
                           "secondary layer seen past it. Keeping them apart is what lets a " +
                           "room have depth instead of looking like a painted backdrop.",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                },
                new TutorialStep
                {
                    Title = "Add a place",
                    Body = "Use + Place, under Your places. The other list is for adding things " +
                           "to rooms the game already has, which is a different job.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addPlace",
                    OnEnter = (vm, s) => s.Set("places", vm.Places.Count),
                    IsDone = (vm, s) => s.GrewSince("places", vm.Places.Count),
                    Hint = "+ Place is in the toolbar above the left-hand list.",
                },
                new TutorialStep
                {
                    Title = "The room itself",
                    Body = "Set the back sprite to TutorialArt/Locations/RoomB.png. That is the " +
                           "layer players read as the room they are standing in.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "field:placeSecondarySprite",
                    // Follows the LABEL, not the property: the row the Places
                    // tab calls "Back sprite" is bound to SecondarySprite. The
                    // step says back, so it must land on the row that says back.
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && p.SecondarySprite.Trim().Length > 0,
                    Hint = "Level art, then the Back sprite row.",
                },
                new TutorialStep
                {
                    Title = "And something standing in it",
                    Body = "Now set the front sprite to TutorialArt/Locations/Room.png — a vase " +
                           "on a transparent background. This layer draws in front of the back " +
                           "one, so it reads as an object in the room rather than part of the " +
                           "wall behind it.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "field:placeBaseSprite",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && p.BaseSprite.Trim().Length > 0,
                    Hint = "Level art, then the Front sprite row.",
                },
                new TutorialStep
                {
                    Title = "Give it some depth",
                    Body = "Turn off Same as front under Behaviour, and set the back layer to " +
                           "drift by a different amount from the front. Anything different will " +
                           "do — the further apart they are, the deeper the room reads. Watch " +
                           "the preview with Preview parallax switched on.",
                    Kind = StepKind.Free,
                    Tab = TabPlaces,
                    Anchor = "field:parallax",
                    AlsoAllow = new[] { "panel:placePreview" },
                    // Free: any separation counts. The judgement of how much is
                    // the author's, and it is the sort of thing only the preview
                    // can really answer.
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && !p.ParallaxSecondaryLinked &&
                                        Math.Abs(p.ParallaxSecondaryStrength - p.ParallaxStrength) > 0.001f,
                    Hint = "Untick Same as front, then change the Parallax — back value.",
                },
                new TutorialStep
                {
                    Title = "A way back out",
                    Body = "Add a navigator button and set its Target to My Room. These are the " +
                           "buttons along the bottom of a room, and without one your place is " +
                           "somewhere players can arrive but not leave. Sending it back to the " +
                           "bedroom closes the loop: in through the door you are about to build, " +
                           "out through this.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    // The group, not the + button: the target picker appears in
                    // the row underneath, and the overlay swallows clicks that
                    // land outside its hole.
                    Anchor = "panel:navigatorButtons",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NavigatorButtons.Any(b => b.Target == BedroomToken),
                    Hint = "+ Add navigator button, then pick My Room in the row's Target box.",
                },
                new TutorialStep
                {
                    Title = "A door from your bedroom",
                    Body = "Now the way in. The bedroom every save starts in is the one place " +
                           "you can always reach, so that is where the door goes — a vanilla " +
                           "extension puts your own buttons onto a vanilla place's strip. Under " +
                           "Vanilla extensions, below the places list, use + Source.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addVanillaSource",
                    OnEnter = (vm, s) => s.Set("ext", vm.VanillaExtensions.Count),
                    IsDone = (vm, s) => s.GrewSince("ext", vm.VanillaExtensions.Count),
                    Hint = "+ Source is under Vanilla extensions, below the places list.",
                },
                new TutorialStep
                {
                    Title = "Aim it at the bedroom",
                    Body = "It arrives pointing at the Beach, which is only a default and not " +
                           "what you want. Set Source place to My Room — that is 5_MyRoom, the " +
                           "one you wake up in. An extension attaches your buttons to whichever " +
                           "vanilla place this names, so this box decides where the door is.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:vanillaSource",
                    IsDone = (vm, s) => vm.VanillaExtensions.Any(e => e.Source == BedroomToken),
                    Hint = "Source place, in the pane on the right. Pick My Room.",
                },
                new TutorialStep
                {
                    Title = "Point it at your room",
                    Body = "Add a navigator button on that extension, and set its Target to the " +
                           "place you built. This is the whole trick for seeing your work: the " +
                           "bedroom is there the moment a save loads, so one button there " +
                           "reaches anything you build from now on without playing through to " +
                           "find it.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:extNavigatorButtons",
                    // Counted rather than checked outright: the author may already
                    // have extension buttons from earlier work, and the lesson is
                    // that THIS one leads home.
                    OnEnter = (vm, s) => s.Set("extnav", ExtensionButtonsHome(vm)),
                    IsDone = (vm, s) => s.GrewSince("extnav", ExtensionButtonsHome(vm)),
                    Hint = "+ Add navigator button, then set its Target to your place.",
                },
                new TutorialStep
                {
                    Title = "Go and look at it",
                    Body = "Save, export, and load any game. The button is on the bedroom's " +
                           "strip, and it takes you straight into the room you just built. " +
                           "Everything the later tutorials add goes in that room, so this one " +
                           "door is all you need from here.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
                new TutorialStep
                {
                    Title = "A room that exists",
                    Body = "Two layers, some depth, and a round trip to your bedroom and back. " +
                           "Export and you can walk into it. Validate first — it will tell you " +
                           "if either button is pointing at nothing.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "btn:validate",
                },
            },
        },

        new TutorialDef
        {
            Id = "dressing-the-room",
            Group = "Places",
            Title = "Putting things in the room",
            Summary = "Add objects to a place, layer them, and leave one switched off.",
            Level = 3,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "A room is more than two pictures",
                    Body = "The back and front layers are the room itself. Everything ELSE in it " +
                           "is a GameObject: its own picture, at its own position, with its own " +
                           "place in the stack — and, unlike the layers, something a dialogue or " +
                           "a rule can switch on and off while the game runs.\n\n" +
                           "That is how a room changes. A door that opens is two objects, one " +
                           "switched off. A coat left on a chair is an object that appears once " +
                           "a conversation has happened. A lamp that comes on at night is the " +
                           "same trick again.\n\n" +
                           "Object art is NOT a level layer, so it is not 2048 by 1136. Draw it " +
                           "at whatever size the thing really is, on a transparent background; " +
                           "it is placed in the room rather than filling it.",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                },
                new TutorialStep
                {
                    Title = "Open the room you built",
                    Body = "This carries on in the same pack. Click your place in the list on " +
                           "the left — the one you made two tutorials ago, not a vanilla " +
                           "extension.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeList",
                    IsDone = (vm, s) => vm.SelectedPlace != null,
                    Hint = "Your places are the upper list; vanilla extensions are below it.",
                },
                new TutorialStep
                {
                    Title = "Add an object",
                    Body = "Use + Add GameObject. It arrives empty and unnamed, which is fine — " +
                           "the next step is what makes it a thing.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    OnEnter = (vm, s) => s.Set("gos", CountGameObjects(vm)),
                    IsDone = (vm, s) => s.GrewSince("gos", CountGameObjects(vm)),
                    Hint = "+ Add GameObject, under the GameObjects box.",
                },
                new TutorialStep
                {
                    Title = "Give it a name and a picture",
                    Body = "The name is how everything else finds this object — a rule that " +
                           "switches it on names it, and names repeat all over a level, so pick " +
                           "something you would recognise months from now.\n\n" +
                           "For the picture, any of the practice art will do; the NPC art in " +
                           "TutorialArt/NPCs is a convenient shape to see. It does not have to " +
                           "be art you drew for this — an object is just a sprite in a room.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    IsDone = (vm, s) => FirstGameObject(vm) is { } g &&
                                        g.Name.Trim().Length > 0 && g.Sprite.Trim().Length > 0,
                    Hint = "Select the object in the tree, then fill in Name and its sprite.",
                },
                new TutorialStep
                {
                    Title = "Decide what it stands in front of",
                    Body = "Sorting order is the stack. The back layer sits at -12 and the front " +
                           "layer at -10, so an object at -11 stands BETWEEN them: behind the " +
                           "furniture in the front layer, in front of the wall behind it. " +
                           "Anything above -10 draws over the whole room.\n\n" +
                           "A new object starts at 0, which is over everything. Set it to -11 " +
                           "and watch the preview put it into the room rather than on top of it.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    AlsoAllow = new[] { "panel:placePreview" },
                    IsDone = (vm, s) => FirstGameObject(vm) is { } g && g.SortingOrder < 0,
                    Hint = "Sorting order, on the selected object. -11 puts it between the layers.",
                },
                new TutorialStep
                {
                    Title = "Leave it switched off",
                    Body = "Untick Start active. The object is still in the room, at the position " +
                           "you gave it — it simply is not shown when the player arrives.\n\n" +
                           "This is half of every change a room ever makes. The other half is a " +
                           "Set-Active action, from a dialogue node or an integration rule, " +
                           "naming this object. Build the object switched off now and the rule " +
                           "that reveals it has something to point at later.",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    IsDone = (vm, s) => FirstGameObject(vm) is { StartActive: false },
                    Hint = "Start active, on the selected object. Untick it.",
                },
                new TutorialStep
                {
                    Title = "What the preview is telling you",
                    Body = "An object that starts switched off is drawn faded, so you can still " +
                           "position it — the game will draw nothing there until something turns " +
                           "it on. Fade objects that start inactive turns that off while you " +
                           "work.\n\n" +
                           "The same goes for anything under a switched-off parent: the game " +
                           "draws none of it, so the preview fades all of it. If a whole group " +
                           "of your objects has gone pale, that is why.",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                },
                new TutorialStep
                {
                    Title = "A room that can change",
                    Body = "You now have a room with something in it that is not part of the " +
                           "picture — placed, layered, and waiting to be switched on.\n\n" +
                           "Objects nest, so one can be a container for several and switching " +
                           "the parent moves all of them at once. And the Documentation section " +
                           "covers the rest of what a place carries: masks, weather, and the " +
                           "beach ambience a room can keep or drop.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

    };

    /// <summary>Every GameObject on the selected place, at any depth. Objects
    /// nest, so a count that only saw the top level would miss one added inside
    /// a container.</summary>
    private static int CountGameObjects(ViewModel.MainViewModel vm)
    {
        int n = 0;
        void Walk(ViewModel.GameObjectViewModel g)
        {
            n++;
            foreach (var c in g.Children) Walk(c);
        }
        if (vm.SelectedPlace != null)
            foreach (var g in vm.SelectedPlace.GameObjects) Walk(g);
        return n;
    }

    /// <summary>The first GameObject on the selected place, which is the one a
    /// step that just asked for one is talking about.</summary>
    private static ViewModel.GameObjectViewModel? FirstGameObject(ViewModel.MainViewModel vm)
        => vm.SelectedPlace?.GameObjects.Count > 0 ? vm.SelectedPlace.GameObjects[0] : null;

    /// <summary>
    /// The diagnostic walkthrough. Not for authors — it exercises the overlay,
    /// the anchoring and the three step kinds so a change to the tutorial
    /// system can be checked without starting a real tutorial and clicking to
    /// the part that broke.
    /// <para/>
    /// Debug builds only. It is a test harness, and a released editor offering
    /// it alongside the real curriculum would read as an eighth lesson that
    /// teaches nothing. Kept rather than deleted so the harness still exists
    /// the next time this system is changed.
    /// </summary>
#if DEBUG
    private static readonly TutorialDef[] _smoke =
    {
        new TutorialDef
        {
            Id = "smoke",
            Group = "Diagnostics",
            Title = "Check the tutorial system",
            Summary = "Three steps that prove the highlighting, the gating and the exit all work.",
            Level = 0,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "This is a tutorial step",
                    Body = "Everything outside the bright area is dimmed, and clicks there do " +
                           "nothing, so there is only one place to go. Exit tutorial is always " +
                           "available, at any step.",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "tab:modforge",
                },
                new TutorialStep
                {
                    Title = "Now do something",
                    Body = "Add a character. This step will not move on until one has actually " +
                           "been added, and it will notice however you add it — the button here, " +
                           "or any other way.",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "btn:addCharacter",
                    OnEnter = (vm, s) => s.Set("chars", vm.Characters.Count),
                    IsDone = (vm, s) => s.GrewSince("chars", vm.Characters.Count),
                    Hint = "Use + Character in the toolbar above the list.",
                },
                new TutorialStep
                {
                    Title = "That is the whole idea",
                    Body = "Read steps wait for Next, Do steps wait for the work, and Free steps " +
                           "accept anything reasonable. Finish to close the overlay.",
                    Kind = StepKind.Read,
                    Anchor = "",
                },
            },
        },
    };
#endif

    /// <summary>
    /// Everything, in the order it should be taken.
    /// <para/>
    /// A released build carries the seven authoring tutorials and nothing else.
    /// The diagnostic walkthrough is compiled in for Debug only — it is a test
    /// harness for this system, and shipping it would offer an eighth lesson
    /// that teaches an author nothing.
    /// <para/>
    /// Declared after every array it reads, and that is load-bearing: static
    /// initialisers run in textual order, so building this above _smoke left it
    /// concatenating null. That throws inside the static constructor, and the
    /// binding layer swallows the failure — the list simply renders empty, with
    /// nothing to say why.
    /// </summary>
#if DEBUG
    public static IReadOnlyList<TutorialDef> All { get; } =
        _first.Concat(TutorialsPart2.All).Concat(_smoke).ToArray();
#else
    public static IReadOnlyList<TutorialDef> All { get; } =
        _first.Concat(TutorialsPart2.All).ToArray();
#endif

    /// <summary>The vanilla bedroom every save starts in.</summary>
    private const string BedroomToken = "vanilla:5_MyRoom";

    /// <summary>
    /// Whether a navigator / map target points at a place in THIS pack.
    /// <para/>
    /// Adding a button and choosing where it goes are two separate acts, and
    /// only the second one is the lesson: a button with an empty target
    /// compiles, exports, and does nothing. Steps that teach travel therefore
    /// check the destination rather than the count.
    /// </summary>
    private static bool PointsAtOwnPlace(ViewModel.MainViewModel vm, string? token)
    {
        if (!Model.PlaceTargetRef.TryParse(token, out var r)) return false;
        return r.Kind switch
        {
            Model.PlaceTargetKind.Self => true,
            Model.PlaceTargetKind.Pack => r.PackId == vm.Pack.PackId,
            _ => false,
        };
    }

    /// <summary>Navigator buttons on vanilla extensions that lead into this
    /// pack. Totalled across extensions because the step asks for a way in,
    /// not for which extension carries it.</summary>
    private static int ExtensionButtonsHome(ViewModel.MainViewModel vm)
    {
        int n = 0;
        foreach (var e in vm.VanillaExtensions)
            foreach (var b in e.NavigatorButtons)
                if (PointsAtOwnPlace(vm, b.Target)) n++;
        return n;
    }

    public static TutorialDef? ById(string id)
    {
        foreach (var t in All) if (t.Id == id) return t;
        return null;
    }
}
