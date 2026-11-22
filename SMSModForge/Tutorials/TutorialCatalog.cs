using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.ViewModel;

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
    // Tab indices, from MainTab.
    private const int TabModForge = MainTab.ModForge;
    private const int TabCharacters = MainTab.Characters;
    private const int TabNpcs = MainTab.Npcs;
    private const int TabPlaces = MainTab.Places;
    private const int TabMapButtons = MainTab.MapButtons;
    private const int TabDialogues = MainTab.Dialogues;
    private const int TabScenes = MainTab.Scenes;
    private const int TabWallpapers = MainTab.Wallpapers;
    private const int TabVariables = MainTab.Variables;
    private const int TabIntegration = MainTab.Integration;

    private static readonly TutorialDef[] _first =
    {
        new TutorialDef
        {
            Id = "first-steps",
            Group = "tutorial.group.gettingStarted",
            Title = "tutorial.firstSteps.title",
            Summary = "tutorial.firstSteps.summary",
            Makes = "tutorial.firstSteps.makes",
            Level = 1,
            // 3 (1.10.0): "Dummy Days" begins - the start page's New, her pronouns, short steps with fold-outs.
            Revision = 3,
            Leaves = vm => StoryPack.Dummy(vm) != null,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.willEndUp.title",
                    Body = "tutorial.firstSteps.willEndUp.body",
                    More = "tutorial.firstSteps.willEndUp.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.startFreshPack.title",
                    Body = "tutorial.firstSteps.startFreshPack.body",
                    More = "tutorial.firstSteps.startFreshPack.more",
                    Kind = StepKind.Do,
                    // File's New, which is there with a pack open or not; the start page's New too, while it shows.
                    Anchor = "menu:file",
                    AlsoAllow = new[] { "btn:startNew" },
                    OnEnter = (vm, s) => s.Set("pack", vm.Pack),
                    IsDone = (vm, s) => !ReferenceEquals(vm.Pack, s.Get<Model.ModPack>("pack")) && !string.IsNullOrEmpty(vm.PackRoot),
                    Solve = (vm, s) => vm.MakePack(StoryPack.NewFolder(), StoryPack.Name, askFirst: true),
                    MakesThePack = true,
                    Hint = "tutorial.firstSteps.startFreshPack.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.someArtWork.title",
                    Body = "tutorial.firstSteps.someArtWork.body",
                    More = "tutorial.firstSteps.someArtWork.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    OnEnter = (vm, s) => TutorialAssets.EnsureCopied(vm.PackRoot),
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.addCharacter.title",
                    Body = "tutorial.firstSteps.addCharacter.body",
                    More = "tutorial.firstSteps.addCharacter.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "btn:addCharacter",
                    OnEnter = (vm, s) => s.Set("chars", vm.Characters.Count),
                    IsDone = (vm, s) => s.GrewSince("chars", vm.Characters.Count),
                    Solve = (vm, s) => vm.AddCharacterCommand.Execute(null),
                    Hint = "tutorial.firstSteps.addCharacter.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.giveThemName.title",
                    Body = "tutorial.firstSteps.giveThemName.body",
                    More = "tutorial.firstSteps.giveThemName.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "field:characterName",
                    IsDone = (vm, s) => vm.SelectedCharacter is { } c &&
                                        !StillPlaceholder(c.DisplayName, "characters.newName"),
                    Solve = (vm, s) => vm.SelectedCharacter!.DisplayName = StoryPack.DummyName,
                    Hint = "tutorial.firstSteps.giveThemName.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.pronouns.title",
                    Body = "tutorial.firstSteps.pronouns.body",
                    More = "tutorial.firstSteps.pronouns.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "field:pronouns",
                    IsDone = (vm, s) => vm.SelectedCharacter is { } c && c.Pronouns != Model.Pronouns.Unset,
                    Solve = (vm, s) => vm.SelectedCharacter!.Pronouns = Model.Pronouns.Female,
                    Hint = "tutorial.firstSteps.pronouns.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.giveThemFace.title",
                    Body = "tutorial.firstSteps.giveThemFace.body",
                    More = "tutorial.firstSteps.giveThemFace.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "field:baseSprite",
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o && o.BaseSprite.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.SelectedOutfit!.BaseSprite = TutorialAssets.Bust(1);
                    },
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
                    More = "tutorial.firstSteps.sayOutfitHas.more",
                    Kind = StepKind.Do,
                    Tab = TabCharacters,
                    Anchor = "panel:outfitSprites",
                    AlsoAllow = new[] { "panel:outfitExpressions" },
                    IsDone = (vm, s) => vm.SelectedOutfit is { } o &&
                                        !o.BlinkEnabled && !o.MouthEnabled && !o.ExpressionEnabled,
                    Solve = (vm, s) =>
                    {
                        var o = vm.SelectedOutfit!;
                        o.BlinkEnabled = false;
                        o.MouthEnabled = false;
                        o.ExpressionEnabled = false;
                    },
                    Hint = "tutorial.firstSteps.sayOutfitHas.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.pack.title",
                    Body = "tutorial.firstSteps.pack.body",
                    More = "tutorial.firstSteps.pack.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
                new TutorialStep
                {
                    Title = "tutorial.firstSteps.writtenIn.title",
                    Body = "tutorial.firstSteps.writtenIn.body",
                    More = "tutorial.firstSteps.writtenIn.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "panel:packLanguage",
                },
            },
        },

        new TutorialDef
        {
            Id = "a-place",
            Group = "tutorial.group.places",
            Title = "tutorial.aPlace.title",
            Summary = "tutorial.aPlace.summary",
            Makes = "tutorial.aPlace.makes",
            Level = 2,
            // 2 (1.10.0): "Dummy Days" - her gallery, with a name; short steps with fold-outs.
            Revision = 2,
            Leaves = vm => StoryPack.Gallery(vm) != null,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.aPlace.story.title",
                    Body = "tutorial.aPlace.story.body",
                    More = "tutorial.aPlace.story.more",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.addPlace.title",
                    Body = "tutorial.aPlace.addPlace.body",
                    More = "tutorial.aPlace.addPlace.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addPlace",
                    OnEnter = (vm, s) => s.Set("places", vm.Places.Count),
                    IsDone = (vm, s) => s.GrewSince("places", vm.Places.Count),
                    Solve = (vm, s) => vm.AddPlaceCommand.Execute(null),
                    Hint = "tutorial.aPlace.addPlace.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.nameIt.title",
                    Body = "tutorial.aPlace.nameIt.body",
                    More = "tutorial.aPlace.nameIt.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "field:placeName",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && !StillPlaceholder(p.DisplayName, "places.newName"),
                    Solve = (vm, s) => vm.SelectedPlace!.DisplayName = StoryPack.GalleryName,
                    Hint = "tutorial.aPlace.nameIt.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.roomItself.title",
                    Body = "tutorial.aPlace.roomItself.body",
                    More = "tutorial.aPlace.roomItself.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "field:placeSecondarySprite",
                    // Follows the LABEL, not the property: the row the Places
                    // tab calls "Back sprite" is bound to SecondarySprite. The
                    // step says back, so it must land on the row that says back.
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && p.SecondarySprite.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.SelectedPlace!.SecondarySprite = TutorialAssets.RoomBase;
                    },
                    Hint = "tutorial.aPlace.roomItself.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.somethingStanding.title",
                    Body = "tutorial.aPlace.somethingStanding.body",
                    More = "tutorial.aPlace.somethingStanding.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "field:placeBaseSprite",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && p.BaseSprite.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        vm.SelectedPlace!.BaseSprite = TutorialAssets.RoomSecondary;
                    },
                    Hint = "tutorial.aPlace.somethingStanding.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.giveSomeDepth.title",
                    Body = "tutorial.aPlace.giveSomeDepth.body",
                    More = "tutorial.aPlace.giveSomeDepth.more",
                    Kind = StepKind.Free,
                    Tab = TabPlaces,
                    Anchor = "field:parallax",
                    AlsoAllow = new[] { "panel:placePreview" },
                    // Free: any separation counts. The judgement of how much is
                    // the author's, and it is the sort of thing only the preview
                    // can really answer.
                    IsDone = (vm, s) => vm.SelectedPlace is { } p && !p.ParallaxSecondaryLinked &&
                                        Math.Abs(p.ParallaxSecondaryStrength - p.ParallaxStrength) > 0.001f,
                    Solve = (vm, s) =>
                    {
                        var p = vm.SelectedPlace!;
                        p.ParallaxSecondaryLinked = false;
                        // The 0.3 the step says: half the front's (0.38 on screen) left a reader who typed 0.3 thinking
                        // they had done it wrong when the next steps showed something else (review of 2026-11-22).
                        p.ParallaxSecondaryStrength = 0.3f;
                        if (Math.Abs(p.ParallaxSecondaryStrength - p.ParallaxStrength) <= 0.001f) p.ParallaxSecondaryStrength = p.ParallaxStrength + 0.4f;
                    },
                    Hint = "tutorial.aPlace.giveSomeDepth.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.wayBackOut.title",
                    Body = "tutorial.aPlace.wayBackOut.body",
                    More = "tutorial.aPlace.wayBackOut.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    // The group, not the + button: the target picker appears in
                    // the row underneath, and the overlay swallows clicks that
                    // land outside its hole.
                    Anchor = "panel:navigatorButtons",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NavigatorButtons.Any(b => b.Target == BedroomToken),
                    Solve = (vm, s) => vm.SelectedPlace!.AddNavigatorButton()!.Target = BedroomToken,
                    Hint = "tutorial.aPlace.wayBackOut.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.sayButtonReads.title",
                    Body = "tutorial.aPlace.sayButtonReads.body",
                    More = "tutorial.aPlace.sayButtonReads.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:navigatorButtons",
                    IsDone = (vm, s) => vm.SelectedPlace is { } p &&
                                        p.NavigatorButtons.Any(b => b.Label.Trim().Length > 0),
                    Solve = (vm, s) => vm.SelectedPlace!.NavigatorButtons.First(b => b.Target == BedroomToken).Label = "Home",   // English on purpose: the story pack's words.
                    Hint = "tutorial.aPlace.sayButtonReads.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.doorBedroom.title",
                    Body = "tutorial.aPlace.doorBedroom.body",
                    More = "tutorial.aPlace.doorBedroom.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addVanillaSource",
                    OnEnter = (vm, s) => s.Set("ext", vm.VanillaExtensions.Count),
                    IsDone = (vm, s) => s.GrewSince("ext", vm.VanillaExtensions.Count),
                    Solve = (vm, s) => vm.AddVanillaExtensionCommand.Execute(null),
                    Hint = "tutorial.aPlace.doorBedroom.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.aimBedroom.title",
                    Body = "tutorial.aPlace.aimBedroom.body",
                    More = "tutorial.aPlace.aimBedroom.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:vanillaSource",
                    IsDone = (vm, s) => vm.VanillaExtensions.Any(e => e.Source == BedroomToken),
                    Solve = (vm, s) => vm.SelectedVanillaExtension!.Source = BedroomToken,
                    Hint = "tutorial.aPlace.aimBedroom.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.pointRoom.title",
                    Body = "tutorial.aPlace.pointRoom.body",
                    More = "tutorial.aPlace.pointRoom.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:extNavigatorButtons",
                    // Counted rather than checked outright: the author may already
                    // have extension buttons from earlier work, and the lesson is
                    // that THIS one leads home.
                    OnEnter = (vm, s) => s.Set("extnav", ExtensionButtonsHome(vm)),
                    IsDone = (vm, s) => s.GrewSince("extnav", ExtensionButtonsHome(vm)),
                    Solve = (vm, s) =>
                    {
                        var ext = vm.VanillaExtensions.LastOrDefault(e => e.Source == BedroomToken) ?? vm.SelectedVanillaExtension!;
                        vm.SelectedVanillaExtension = ext;
                        var gallery = StoryPack.Gallery(vm) ?? vm.SelectedPlace ?? vm.Places[^1];
                        ext.AddNavigatorButton()!.Target = "self:" + gallery.Key;
                    },
                    Hint = "tutorial.aPlace.pointRoom.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aPlace.goLook.title",
                    Body = "tutorial.aPlace.goLook.body",
                    More = "tutorial.aPlace.goLook.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "dressing-the-room",
            Group = "tutorial.group.places",
            Title = "tutorial.dressingTheRoom.title",
            Summary = "tutorial.dressingTheRoom.summary",
            Makes = "tutorial.dressingTheRoom.makes",
            Level = 3,
            // 2 (1.10.0): "Dummy Days" - a golden vase in her gallery; short steps with fold-outs.
            Revision = 2,
            Leaves = vm => StoryPack.Gallery(vm) is { } g && g.GameObjects.Any(o => o.Name.Trim() == StoryPack.VaseName),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.story.title",
                    Body = "tutorial.dressingTheRoom.story.body",
                    More = "tutorial.dressingTheRoom.story.more",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:placeList",
                    // Her gallery, opened for the reader: the steps after this one work in it.
                    OnEnter = (vm, s) => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; },
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.addObject.title",
                    Body = "tutorial.dressingTheRoom.addObject.body",
                    More = "tutorial.dressingTheRoom.addObject.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    OnEnter = (vm, s) => s.Set("gos", CountGameObjects(vm)),
                    IsDone = (vm, s) => s.GrewSince("gos", CountGameObjects(vm)),
                    Solve = (vm, s) =>
                    {
                        vm.SelectedPlace ??= StoryPack.Gallery(vm) ?? vm.Places[^1];
                        vm.SelectedPlace!.AddGameObject();
                    },
                    Hint = "tutorial.dressingTheRoom.addObject.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.giveNamePicture.title",
                    Body = "tutorial.dressingTheRoom.giveNamePicture.body",
                    More = "tutorial.dressingTheRoom.giveNamePicture.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    IsDone = (vm, s) => FirstGameObject(vm) is { } g &&
                                        g.Name.Trim().Length > 0 && g.Sprite.Trim().Length > 0,
                    Solve = (vm, s) =>
                    {
                        TutorialAssets.EnsureCopied(vm.PackRoot);
                        var g = FirstGameObject(vm)!;
                        g.Name = StoryPack.VaseName;
                        g.Sprite = TutorialAssets.Prop;
                    },
                    Hint = "tutorial.dressingTheRoom.giveNamePicture.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.putWhereWant.title",
                    Body = "tutorial.dressingTheRoom.putWhereWant.body",
                    More = "tutorial.dressingTheRoom.putWhereWant.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                    AlsoAllow = new[] { "panel:placeGameObjects" },
                    OnEnter = (vm, s) => s.Set("pos", PropPosition(vm)),
                    IsDone = (vm, s) => PropPosition(vm) != s.Get<string>("pos"),
                    Solve = (vm, s) =>
                    {
                        // The gizmo writes the same X and Y a person could type.
                        var g = FirstGameObject(vm)!;
                        g.X += 1.5f;
                        g.Y -= 0.75f;
                    },
                    Hint = "tutorial.dressingTheRoom.putWhereWant.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.decideStandsFront.title",
                    Body = "tutorial.dressingTheRoom.decideStandsFront.body",
                    More = "tutorial.dressingTheRoom.decideStandsFront.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    AlsoAllow = new[] { "panel:placePreview" },
                    IsDone = (vm, s) => FirstGameObject(vm) is { } g && g.SortingOrder < 0,
                    Solve = (vm, s) => FirstGameObject(vm)!.SortingOrder = -11,
                    Hint = "tutorial.dressingTheRoom.decideStandsFront.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.leaveSwitchedOff.title",
                    Body = "tutorial.dressingTheRoom.leaveSwitchedOff.body",
                    More = "tutorial.dressingTheRoom.leaveSwitchedOff.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "panel:placeGameObjects",
                    IsDone = (vm, s) => FirstGameObject(vm) is { StartActive: false },
                    Solve = (vm, s) => FirstGameObject(vm)!.StartActive = false,
                    Hint = "tutorial.dressingTheRoom.leaveSwitchedOff.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.dressingTheRoom.previewTelling.title",
                    Body = "tutorial.dressingTheRoom.previewTelling.body",
                    More = "tutorial.dressingTheRoom.previewTelling.more",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                },
            },
        },

        new TutorialDef
        {
            Id = "on-the-world-map",
            Group = "tutorial.group.mapButtons",
            Title = "tutorial.onTheWorldMap.title",
            Summary = "tutorial.onTheWorldMap.summary",
            Makes = "tutorial.onTheWorldMap.makes",
            Level = 5,
            // 2 (1.10.0): "Dummy Days" - her gallery on Neon Row; short steps with fold-outs.
            Revision = 2,
            Leaves = vm => StoryPack.Gallery(vm) is { } g && vm.MapButtons.Any(b => b.Target == "self:" + g.Key),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.twoWaysReachRoom.title",
                    Body = "tutorial.onTheWorldMap.twoWaysReachRoom.body",
                    More = "tutorial.onTheWorldMap.twoWaysReachRoom.more",
                    Kind = StepKind.Read,
                    Tab = TabMapButtons,
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.addMapButton.title",
                    Body = "tutorial.onTheWorldMap.addMapButton.body",
                    More = "tutorial.onTheWorldMap.addMapButton.more",
                    Kind = StepKind.Do,
                    Tab = TabMapButtons,
                    Anchor = "btn:addMapButton",
                    AlsoAllow = new[] { "panel:mapButtonList" },
                    OnEnter = (vm, s) => s.Set("mapbtns", vm.MapButtons.Count),
                    IsDone = (vm, s) => s.GrewSince("mapbtns", vm.MapButtons.Count),
                    Solve = (vm, s) => vm.AddMapButtonCommand.Execute(null),
                    Hint = "tutorial.onTheWorldMap.addMapButton.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.sayWhereGoes.title",
                    Body = "tutorial.onTheWorldMap.sayWhereGoes.body",
                    More = "tutorial.onTheWorldMap.sayWhereGoes.more",
                    Kind = StepKind.Do,
                    Tab = TabMapButtons,
                    Anchor = "panel:mapButtonList",
                    IsDone = (vm, s) => vm.MapButtons.Count > 0 &&
                                        vm.MapButtons[^1].Target.Trim().Length > 0,
                    Solve = (vm, s) => vm.MapButtons[^1].Target = "self:" + (StoryPack.Gallery(vm) ?? vm.Places[^1]).Key,
                    Hint = "tutorial.onTheWorldMap.sayWhereGoes.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.chooseWhichMenuLives.title",
                    Body = "tutorial.onTheWorldMap.chooseWhichMenuLives.body",
                    More = "tutorial.onTheWorldMap.chooseWhichMenuLives.more",
                    Kind = StepKind.Do,
                    Tab = TabMapButtons,
                    Anchor = "panel:mapButtonList",
                    OnEnter = (vm, s) => s.Set("district",
                        vm.MapButtons.Count > 0 ? vm.MapButtons[^1].District : ""),
                    IsDone = (vm, s) => vm.MapButtons.Count > 0 &&
                                        vm.MapButtons[^1].District.Trim().Length > 0 &&
                                        vm.MapButtons[^1].District != s.Get<string>("district"),
                    // Neon Row, downtown - or, should a button already sit there, any other.
                    Solve = (vm, s) =>
                    {
                        var b = vm.MapButtons[^1];
                        b.District = b.District != "NeonRow" ? "NeonRow"
                            : Model.WorldMapDistricts.All.First(d => d.GoName != b.District).GoName;
                    },
                    Hint = "tutorial.onTheWorldMap.chooseWhichMenuLives.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.giveSomethingRead.title",
                    Body = "tutorial.onTheWorldMap.giveSomethingRead.body",
                    More = "tutorial.onTheWorldMap.giveSomethingRead.more",
                    Kind = StepKind.Do,
                    Tab = TabMapButtons,
                    Anchor = "panel:mapButtonList",
                    IsDone = (vm, s) => vm.MapButtons.Count > 0 &&
                                        vm.MapButtons[^1].Label.Trim().Length > 0,
                    Solve = (vm, s) => vm.MapButtons[^1].Label = StoryPack.GalleryName,
                    Hint = "tutorial.onTheWorldMap.giveSomethingRead.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.twoCanLeaveAlone.title",
                    Body = "tutorial.onTheWorldMap.twoCanLeaveAlone.body",
                    More = "tutorial.onTheWorldMap.twoCanLeaveAlone.more",
                    Kind = StepKind.Read,
                    Tab = TabMapButtons,
                    Anchor = "panel:mapButtonList",
                },
                new TutorialStep
                {
                    Title = "tutorial.onTheWorldMap.somewhereGo.title",
                    Body = "tutorial.onTheWorldMap.somewhereGo.body",
                    More = "tutorial.onTheWorldMap.somewhereGo.more",
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

    /// <summary>
    /// The object the reader added on the selected place - the one a step that just asked for one is talking
    /// about: + Add GameObject puts it last, before the NPCs. Not simply the first: since 1.10.0 a new place
    /// starts with the game's light (Light 2D) as an object of its own, and the steps renamed and pictured that
    /// light instead (found by the pictures of every step); a reader naming the object they added was never done.
    /// </summary>
    private static ViewModel.GameObjectViewModel? FirstGameObject(ViewModel.MainViewModel vm)
        => vm.SelectedPlace?.GameObjects.LastOrDefault(g => !g.IsNpcRoot && !g.HasLight && !g.HasEffect);

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
            .Concat(TutorialsShow.All)
            .Concat(TutorialsDeep.All)
            .Concat(TutorialsMore.All)
#if DEBUG
            .Concat(_smoke)
#endif
            // Each group's tutorials together, groups in the order they first come (1.10.0: the story's later
            // tutorials are written in their own file and join the groups they belong to).
            .GroupBy(t => t.GroupKey)
            .SelectMany(g => g)
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
