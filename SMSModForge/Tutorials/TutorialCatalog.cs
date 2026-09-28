using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Localization;

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
    private const int TabMapButtons = 4;
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
            Group = "tutorial.group.gettingStarted",
            Title = "tutorial.firstSteps.title",
            Summary = "tutorial.firstSteps.summary",
            Level = 1,
            Revision = 2,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.willEndUp.title",
                    Body = "tutorial.firstSteps.willEndUp.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.startFreshPack.title",
                    Body = "tutorial.firstSteps.startFreshPack.body",
                    Kind = StepKind.Do,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                    // Identity, not emptiness: New pack swaps the whole ModPack
                    // instance, so this is exact and cannot be satisfied by
                    // emptying the current one by hand.
                    OnEnter = (vm, s) => s.Set("pack", vm.Pack),
                    IsDone = (vm, s) => !ReferenceEquals(vm.Pack, s.Get<Model.ModPack>("pack")),
                    Hint = "tutorial.firstSteps.startFreshPack.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.saveSomewhere.title",
                    Body = "tutorial.firstSteps.saveSomewhere.body",
                    Kind = StepKind.Do,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                    IsDone = (vm, s) => !string.IsNullOrEmpty(vm.PackRoot),
                    Hint = "tutorial.firstSteps.saveSomewhere.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.someArtWork.title",
                    Body = "tutorial.firstSteps.someArtWork.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    // Copied on arrival rather than asked for: fetching files is
                    // not a skill this tutorial is here to teach.
                    OnEnter = (vm, s) => TutorialAssets.EnsureCopied(vm.PackRoot),
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.addCharacter.title",
                    Body = "tutorial.firstSteps.addCharacter.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "btn:addCharacter",
                    OnEnter = (vm, s) => s.Set("chars", vm.Characters.Count),
                    IsDone = (vm, s) => s.GrewSince("chars", vm.Characters.Count),
                    Hint = "tutorial.firstSteps.addCharacter.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.giveThemName.title",
                    Body = "tutorial.firstSteps.giveThemName.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "field:characterName",
                    // Rejecting the placeholder is the point: a character still
                    // called "New Character" means the step was skipped.
                    IsDone = (vm, s) => vm.SelectedCharacter is { } c &&
                                        !StillPlaceholder(c.DisplayName, "characters.newName"),
                    Hint = "tutorial.firstSteps.giveThemName.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.giveThemFace.title",
                    Body = "tutorial.firstSteps.giveThemFace.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "field:baseSprite",
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o && o.BaseSprite.Trim().Length > 0,
                    Hint = "tutorial.firstSteps.giveThemFace.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.thereThey.title",
                    Body = "tutorial.firstSteps.thereThey.body",
                    Kind = StepKind.Read,
                    Tab = TabCharacters,
                    Anchor = "panel:characterPreview",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.sayOutfitHas.title",
                    Body = "tutorial.firstSteps.sayOutfitHas.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    // The dim swallows clicks outside a lit hole, so a step that
                    // asks for controls in two boxes has to light both.
                    Anchor = "panel:outfitSprites",
                    AlsoAllow = new[] { "panel:outfitExpressions" },
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        !o.BlinkEnabled && !o.MouthEnabled && !o.ExpressionEnabled,
                    Hint = "tutorial.firstSteps.sayOutfitHas.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.pack.title",
                    Body = "tutorial.firstSteps.pack.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.givingSomebodyElse.title",
                    Body = "tutorial.firstSteps.givingSomebodyElse.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.writtenIn.title",
                    Body = "tutorial.firstSteps.writtenIn.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "panel:packLanguage",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.numberBesidePack.title",
                    Body = "tutorial.firstSteps.numberBesidePack.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "a-place",
            Group = "tutorial.group.places",
            Title = "tutorial.aPlace.title",
            Summary = "tutorial.aPlace.summary",
            Level = 2,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.aPlace.roomsTwoPictures.title",
                    Body = "tutorial.aPlace.roomsTwoPictures.body",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.addPlace.title",
                    Body = "tutorial.aPlace.addPlace.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addPlace",
                    OnEnter = (vm, s) => s.Set("places", vm.Places.Count),
                    IsDone = (vm, s) => s.GrewSince("places", vm.Places.Count),
                    Hint = "tutorial.aPlace.addPlace.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.roomItself.title",
                    Body = "tutorial.aPlace.roomItself.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "field:placeSecondarySprite",
                    // Follows the LABEL, not the property: the row the Places
                    // tab calls "Back sprite" is bound to SecondarySprite. The
                    // step says back, so it must land on the row that says back.
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && p.SecondarySprite.Trim().Length > 0,
                    Hint = "tutorial.aPlace.roomItself.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.somethingStanding.title",
                    Body = "tutorial.aPlace.somethingStanding.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "field:placeBaseSprite",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && p.BaseSprite.Trim().Length > 0,
                    Hint = "tutorial.aPlace.somethingStanding.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.giveSomeDepth.title",
                    Body = "tutorial.aPlace.giveSomeDepth.body",
                    Kind = StepKind.Free,
                    Tab = TabPlaces,
                    Anchor = "field:parallax",
                    AlsoAllow = new[] { "panel:placePreview" },
                    // Free: any separation counts. The judgement of how much is
                    // the author's, and it is the sort of thing only the preview
                    // can really answer.
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && !p.ParallaxSecondaryLinked &&
                                        Math.Abs(p.ParallaxSecondaryStrength - p.ParallaxStrength) > 0.001f,
                    Hint = "tutorial.aPlace.giveSomeDepth.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.wayBackOut.title",
                    Body = "tutorial.aPlace.wayBackOut.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    // The group, not the + button: the target picker appears in
                    // the row underneath, and the overlay swallows clicks that
                    // land outside its hole.
                    Anchor = "panel:navigatorButtons",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NavigatorButtons.Any(b => b.Target == BedroomToken),
                    Hint = "tutorial.aPlace.wayBackOut.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.sayButtonReads.title",
                    Body = "tutorial.aPlace.sayButtonReads.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:navigatorButtons",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NavigatorButtons.Any(b => b.Label.Trim().Length > 0),
                    Hint = "tutorial.aPlace.sayButtonReads.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.doorBedroom.title",
                    Body = "tutorial.aPlace.doorBedroom.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addVanillaSource",
                    OnEnter = (vm, s) => s.Set("ext", vm.VanillaExtensions.Count),
                    IsDone = (vm, s) => s.GrewSince("ext", vm.VanillaExtensions.Count),
                    Hint = "tutorial.aPlace.doorBedroom.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.aimBedroom.title",
                    Body = "tutorial.aPlace.aimBedroom.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:vanillaSource",
                    IsDone = (vm, s) => vm.VanillaExtensions.Any(e => e.Source == BedroomToken),
                    Hint = "tutorial.aPlace.aimBedroom.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.pointRoom.title",
                    Body = "tutorial.aPlace.pointRoom.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:extNavigatorButtons",
                    // Counted rather than checked outright: the author may already
                    // have extension buttons from earlier work, and the lesson is
                    // that THIS one leads home.
                    OnEnter = (vm, s) => s.Set("extnav", ExtensionButtonsHome(vm)),
                    IsDone = (vm, s) => s.GrewSince("extnav", ExtensionButtonsHome(vm)),
                    Hint = "tutorial.aPlace.pointRoom.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.goLook.title",
                    Body = "tutorial.aPlace.goLook.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.roomExists.title",
                    Body = "tutorial.aPlace.roomExists.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "btn:validate",
                },
            },
        },

        new TutorialDef
        {
            Id = "dressing-the-room",
            Group = "tutorial.group.places",
            Title = "tutorial.dressingTheRoom.title",
            Summary = "tutorial.dressingTheRoom.summary",
            Level = 3,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.roomMoreThanTwo.title",
                    Body = "tutorial.dressingTheRoom.roomMoreThanTwo.body",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.openRoomBuilt.title",
                    Body = "tutorial.dressingTheRoom.openRoomBuilt.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeList",
                    IsDone = (vm, s) => vm.SelectedPlace != null,
                    Hint = "tutorial.dressingTheRoom.openRoomBuilt.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.addObject.title",
                    Body = "tutorial.dressingTheRoom.addObject.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    OnEnter = (vm, s) => s.Set("gos", CountGameObjects(vm)),
                    IsDone = (vm, s) => s.GrewSince("gos", CountGameObjects(vm)),
                    Hint = "tutorial.dressingTheRoom.addObject.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.giveNamePicture.title",
                    Body = "tutorial.dressingTheRoom.giveNamePicture.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    IsDone = (vm, s) => FirstGameObject(vm) is { } g &&
                                        g.Name.Trim().Length > 0 && g.Sprite.Trim().Length > 0,
                    Hint = "tutorial.dressingTheRoom.giveNamePicture.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.putWhereWant.title",
                    Body = "tutorial.dressingTheRoom.putWhereWant.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                    AlsoAllow = new[] { "panel:placeGameObjects" },
                    OnEnter = (vm, s) => s.Set("pos", PropPosition(vm)),
                    IsDone = (vm, s) => PropPosition(vm) != s.Get<string>("pos"),
                    Hint = "tutorial.dressingTheRoom.putWhereWant.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.decideStandsFront.title",
                    Body = "tutorial.dressingTheRoom.decideStandsFront.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    AlsoAllow = new[] { "panel:placePreview" },
                    IsDone = (vm, s) => FirstGameObject(vm) is { } g && g.SortingOrder < 0,
                    Hint = "tutorial.dressingTheRoom.decideStandsFront.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.leaveSwitchedOff.title",
                    Body = "tutorial.dressingTheRoom.leaveSwitchedOff.body",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    IsDone = (vm, s) => FirstGameObject(vm) is { StartActive: false },
                    Hint = "tutorial.dressingTheRoom.leaveSwitchedOff.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.previewTelling.title",
                    Body = "tutorial.dressingTheRoom.previewTelling.body",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.roomCanChange.title",
                    Body = "tutorial.dressingTheRoom.roomCanChange.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
            },
        },

        new TutorialDef
        {
            Id = "on-the-world-map",
            Group = "tutorial.group.mapButtons",
            Title = "tutorial.onTheWorldMap.title",
            Summary = "tutorial.onTheWorldMap.summary",
            Level = 4,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.twoWaysReachRoom.title",
                    Body = "tutorial.onTheWorldMap.twoWaysReachRoom.body",
                    Kind = StepKind.Read,
                    Tab = TabMapButtons,
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.addMapButton.title",
                    Body = "tutorial.onTheWorldMap.addMapButton.body",
                    Kind = StepKind.Do,
                    Tab = TabMapButtons,
                    Anchor = "btn:addMapButton",
                    AlsoAllow = new[] { "panel:mapButtonList" },
                    OnEnter = (vm, s) => s.Set("mapbtns", vm.MapButtons.Count),
                    IsDone = (vm, s) => s.GrewSince("mapbtns", vm.MapButtons.Count),
                    Hint = "tutorial.onTheWorldMap.addMapButton.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.sayWhereGoes.title",
                    Body = "tutorial.onTheWorldMap.sayWhereGoes.body",
                    Kind = StepKind.Do,
                    Tab = TabMapButtons,
                    Anchor = "panel:mapButtonList",
                    IsDone = (vm, s) => vm.MapButtons.Count > 0 &&
                                        vm.MapButtons[^1].Target.Trim().Length > 0,
                    Hint = "tutorial.onTheWorldMap.sayWhereGoes.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.chooseWhichMenuLives.title",
                    Body = "tutorial.onTheWorldMap.chooseWhichMenuLives.body",
                    Kind = StepKind.Do,
                    Tab = TabMapButtons,
                    Anchor = "panel:mapButtonList",
                    OnEnter = (vm, s) => s.Set("district",
                        vm.MapButtons.Count > 0 ? vm.MapButtons[^1].District : ""),
                    IsDone = (vm, s) => vm.MapButtons.Count > 0 &&
                                        vm.MapButtons[^1].District.Trim().Length > 0 &&
                                        vm.MapButtons[^1].District != s.Get<string>("district"),
                    Hint = "tutorial.onTheWorldMap.chooseWhichMenuLives.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.giveSomethingRead.title",
                    Body = "tutorial.onTheWorldMap.giveSomethingRead.body",
                    Kind = StepKind.Do,
                    Tab = TabMapButtons,
                    Anchor = "panel:mapButtonList",
                    IsDone = (vm, s) => vm.MapButtons.Count > 0 &&
                                        vm.MapButtons[^1].Label.Trim().Length > 0,
                    Hint = "tutorial.onTheWorldMap.giveSomethingRead.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.twoCanLeaveAlone.title",
                    Body = "tutorial.onTheWorldMap.twoCanLeaveAlone.body",
                    Kind = StepKind.Read,
                    Tab = TabMapButtons,
                    Anchor = "panel:mapButtonList",
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.somewhereGo.title",
                    Body = "tutorial.onTheWorldMap.somewhereGo.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "btn:validate",
                },
            },
        },

    };

    /// <summary>Where the first GameObject on the selected place sits, as a
    /// string so a step can baseline it and notice any move at all — dragging
    /// is imprecise by nature, so the check asks whether it CHANGED rather than
    /// where it ended up.</summary>
    private static string PropPosition(ViewModel.MainViewModel vm)
        => FirstGameObject(vm) is { } g ? g.X + "," + g.Y : "";

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
            Group = "tutorial.group.diagnostics",
            Title = "tutorial.smoke.title",
            Summary = "tutorial.smoke.summary",
            Level = 0,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.smoke.tutorialStep.title",
                    Body = "tutorial.smoke.tutorialStep.body",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "tab:modforge",
                },
                new TutorialStep
                {
                    Title = "tutorial.smoke.nowDoSomething.title",
                    Body = "tutorial.smoke.nowDoSomething.body",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "btn:addCharacter",
                    OnEnter = (vm, s) => s.Set("chars", vm.Characters.Count),
                    IsDone = (vm, s) => s.GrewSince("chars", vm.Characters.Count),
                    Hint = "tutorial.smoke.nowDoSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.smoke.wholeIdea.title",
                    Body = "tutorial.smoke.wholeIdea.body",
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
    /// <para/>
    /// ONE expression, with the conditional around the diagnostics alone. It
    /// used to be two whole lists, one per configuration, and a tutorial added
    /// to the Debug one never reached anybody: the tests all run in Debug, so
    /// nothing could see the Release list was short. The author-facing ladder
    /// is now impossible to state twice, which is the only way that stays
    /// fixed.
    /// </summary>
    public static IReadOnlyList<TutorialDef> All { get; } =
        _first
            .Concat(TutorialsPart2.All)
            .Concat(TutorialsUi.All)
            .Concat(TutorialsQuests.All)
#if DEBUG
            .Concat(_smoke)
#endif
            .ToArray();

    /// <summary>The vanilla bedroom every save starts in.</summary>
    private const string BedroomToken = "vanilla:5_MyRoom";

    /// <summary>
    /// Whether <paramref name="name"/> is still the placeholder a new item
    /// arrives with, so a "give it a name" step can tell it was skipped.
    /// <para/>
    /// The placeholder is written in the pack's language, or the one being
    /// edited ("Nueva escena") - see <see cref="NewNames"/> - so asking only
    /// whether the name starts with the English "New " let every other
    /// language through unrenamed. Any language's counts: an item made before
    /// the pack's language was changed keeps the one it was made with, and one
    /// made by an earlier version is in whatever the editor was shown in.
    /// </summary>
    internal static bool StillPlaceholder(string name, string newNameKey)
    {
        string n = name.Trim();
        return n.Length == 0
            || n.StartsWith(Loc.T(newNameKey), StringComparison.OrdinalIgnoreCase)
            || NewNames.Everywhere(newNameKey).Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

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
