using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.ViewModel;

namespace SMSModForge.Tutorials;

/// <summary>
/// Pieces of "Dummy Days" (1.10.0) written apart from the rest: her spotlight (tutorial 4, with the other places),
/// and her Starmaker photo with effects for her (the last). The three things
/// 1.10.0 added for a place's look - lights, a character standing in the place, effects - each taught where the
/// story needs it.
/// </summary>
internal static class TutorialsShow
{
    private const int TabPlaces = MainTab.Places;
    private const int TabScenes = MainTab.Scenes;
    private const int TabDialogues = MainTab.Dialogues;
    private const int TabFx = MainTab.Fx;
    private const int TabModForge = MainTab.ModForge;

    /// <summary>Her hearts, the effect of tutorial 19. English on purpose: an effect's name.</summary>
    internal const string HeartsName = "Hearts for her";

    /// <summary>Every object of the place chosen, at any depth.</summary>
    private static IEnumerable<GameObjectViewModel> ObjectsOf(MainViewModel vm)
    {
        IEnumerable<GameObjectViewModel> Walk(IEnumerable<GameObjectViewModel> nodes)
        {
            foreach (var n in nodes)
            {
                yield return n;
                foreach (var c in Walk(n.Children)) yield return c;
            }
        }
        return vm.SelectedPlace is { } p ? Walk(p.GameObjects) : Enumerable.Empty<GameObjectViewModel>();
    }

    /// <summary>The lamps of the place chosen (not its darkness).</summary>
    private static List<LightViewModel> Lamps(MainViewModel vm)
        => ObjectsOf(vm).Where(o => o.HasLight && !o.Light!.IsAmbient).Select(o => o.Light!).ToList();

    private static int Darknesses(MainViewModel vm) => ObjectsOf(vm).Count(o => o.HasLight && o.Light!.IsAmbient);

    private static int EffectObjects(MainViewModel vm) => ObjectsOf(vm).Count(o => o.HasEffect);

    /// <summary>Her gallery chosen on the Places tab, when the pack has it.</summary>
    private static void OpenTheGallery(MainViewModel vm)
    {
        if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g;
    }

    /// <summary>Her spotlight: the lamp added last.</summary>
    private static LightViewModel? Spotlight(MainViewModel vm) => Lamps(vm).LastOrDefault();

    /// <summary>
    /// Her spotlight picked in the place preview, as a click on it does, so its Light box is beside the preview
    /// (1.10.0): the steps about its colour and cone arrived with nothing picked and the box they talk about hidden.
    /// </summary>
    private static void PickSpotlight(MainViewModel vm)
    {
        if (Spotlight(vm) is not { } l) return;
        if (ObjectsOf(vm).LastOrDefault(o => o.HasLight && ReferenceEquals(o.Light!.Model, l.Model)) is { } holder)
            vm.PickInPreview(holder);
    }

    internal static IReadOnlyList<TutorialDef> All { get; } = new[]
    {
        new TutorialDef
        {
            Id = "a-spotlight",
            Group = "tutorial.group.places",
            Title = "tutorial.aSpotlight.title",
            Summary = "tutorial.aSpotlight.summary",
            Makes = "tutorial.aSpotlight.makes",
            Level = 4,
            Leaves = vm => StoryPack.Gallery(vm) is { } g && Contains(g.GameObjects, o => o.HasLight && !o.Light!.IsAmbient),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.aSpotlight.story.title",
                    Body = "tutorial.aSpotlight.story.body",
                    More = "tutorial.aSpotlight.story.more",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                    OnEnter = (vm, s) => OpenTheGallery(vm),
                },
                new TutorialStep
                {
                    Title = "tutorial.aSpotlight.addLight.title",
                    Body = "tutorial.aSpotlight.addLight.body",
                    More = "tutorial.aSpotlight.addLight.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addLight",
                    AlsoAllow = new[] { "panel:placePreview" },
                    OnEnter = (vm, s) => s.Set("lamps", Lamps(vm).Count),
                    IsDone = (vm, s) => s.GrewSince("lamps", Lamps(vm).Count),
                    Solve = (vm, s) =>
                    {
                        vm.SelectedPlace ??= StoryPack.Gallery(vm) ?? vm.Places[^1];
                        vm.SelectedPlace!.AddLightCommand.Execute(null);
                        PickSpotlight(vm);
                    },
                    Hint = "tutorial.aSpotlight.addLight.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aSpotlight.colour.title",
                    Body = "tutorial.aSpotlight.colour.body",
                    More = "tutorial.aSpotlight.colour.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    // The preview: the Light box beside it shows while the light is picked there.
                    Anchor = "panel:placePreview",
                    AlsoAllow = new[] { "panel:light" },
                    // Any change of colour or strength: how warm and how bright is the reader's call.
                    OnEnter = (vm, s) => s.Set("look", string.Join(";", Lamps(vm).Select(l => l.Color + "|" + l.Strength))),
                    IsDone = (vm, s) => Lamps(vm).Count > 0 &&
                                        string.Join(";", Lamps(vm).Select(l => l.Color + "|" + l.Strength)) != s.Get<string>("look"),
                    Solve = (vm, s) =>
                    {
                        var light = Spotlight(vm)!;
                        light.Color = "#FFD9A0";
                        light.Strength = 1.2f;
                    },
                    OnShown = PickSpotlight,
                    Hint = "tutorial.aSpotlight.colour.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aSpotlight.cone.title",
                    Body = "tutorial.aSpotlight.cone.body",
                    More = "tutorial.aSpotlight.cone.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    // The preview: the Light box beside it shows while the light is picked there.
                    Anchor = "panel:placePreview",
                    AlsoAllow = new[] { "panel:light" },
                    IsDone = (vm, s) => Lamps(vm).Any(l => l.OuterAngle < 360f),
                    Solve = (vm, s) =>
                    {
                        var light = Spotlight(vm)!;
                        light.OuterAngle = 60f;
                        light.InnerAngle = 30f;
                        // Turned to point down: a cone points the way its object is turned.
                        var holder = ObjectsOf(vm).Last(o => o.HasLight && ReferenceEquals(o.Light!.Model, light.Model));
                        holder.RotationZ = 180f;
                    },
                    OnShown = PickSpotlight,
                    Hint = "tutorial.aSpotlight.cone.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aSpotlight.reach.title",
                    Body = "tutorial.aSpotlight.reach.body",
                    More = "tutorial.aSpotlight.reach.more",
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:placePreview",
                },
                new TutorialStep
                {
                    Title = "tutorial.aSpotlight.standIn.title",
                    Body = "tutorial.aSpotlight.standIn.body",
                    More = "tutorial.aSpotlight.standIn.more",
                    // Read: who stands in the preview is a view of it, not part of the pack.
                    Kind = StepKind.Read,
                    Tab = TabPlaces,
                    Anchor = "panel:standIn",
                    AlsoAllow = new[] { "panel:placePreview" },
                },
                new TutorialStep
                {
                    Title = "tutorial.aSpotlight.characters.title",
                    Body = "tutorial.aSpotlight.characters.body",
                    More = "tutorial.aSpotlight.characters.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    // The preview: the Light box beside it shows while the light is picked there.
                    Anchor = "panel:placePreview",
                    AlsoAllow = new[] { "panel:light" },
                    IsDone = (vm, s) => Lamps(vm).Any(l => l.LightsCharacters),
                    Solve = (vm, s) => Spotlight(vm)!.LightsCharacters = true,
                    OnShown = PickSpotlight,
                    Hint = "tutorial.aSpotlight.characters.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aSpotlight.darkness.title",
                    Body = "tutorial.aSpotlight.darkness.body",
                    More = "tutorial.aSpotlight.darkness.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addDarkness",
                    AlsoAllow = new[] { "panel:placePreview", "panel:light" },
                    OnEnter = (vm, s) => s.Set("dark", Darknesses(vm)),
                    IsDone = (vm, s) => s.GrewSince("dark", Darknesses(vm)),
                    Solve = (vm, s) => vm.SelectedPlace!.AddLightCommand.Execute(LightObjects.Darkness),
                    Hint = "tutorial.aSpotlight.darkness.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aSpotlight.goSee.title",
                    Body = "tutorial.aSpotlight.goSee.body",
                    More = "tutorial.aSpotlight.goSee.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },

        new TutorialDef
        {
            Id = "effects",
            Group = "tutorial.group.fx",
            Title = "tutorial.effects.title",
            Summary = "tutorial.effects.summary",
            Makes = "tutorial.effects.makes",
            Level = 18,
            // 18: her pose a real Starmaker photo, then effects for her.
            Leaves = vm => vm.Effects.Count > 0,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.effects.story.title",
                    Body = "tutorial.effects.story.body",
                    More = "tutorial.effects.story.more",
                    Kind = StepKind.Read,
                    Tab = TabFx,
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.photo.title",
                    Body = "tutorial.effects.photo.body",
                    More = "tutorial.effects.photo.more",
                    Kind = StepKind.Read,
                    Tab = TabScenes,
                    Anchor = "panel:sceneDetail",
                    // Her pose, opened for the reader (the scene of "Rules that run themselves").
                    OnEnter = (vm, s) => { if (vm.Scenes.LastOrDefault() is { } sc) vm.SelectedScene = sc; },
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.photoTick.title",
                    Body = "tutorial.effects.photoTick.body",
                    More = "tutorial.effects.photoTick.more",
                    Kind = StepKind.Do,
                    Tab = TabScenes,
                    Anchor = "panel:sceneDetail",
                    IsDone = (vm, s) => vm.SelectedScene is { StarmakerPhoto: true },
                    Solve = (vm, s) =>
                    {
                        if (vm.SelectedScene == null)
                        {
                            // No pose yet (a pack that skipped "Rules that run themselves"): one made here.
                            TutorialAssets.EnsureCopied(vm.PackRoot);
                            vm.AddSceneCommand.Execute(null);
                            vm.SelectedScene!.SceneSprite = TutorialAssets.Scene;
                            vm.SelectedScene!.DisplayName = "Her first pose";   // English on purpose: the story pack's words.
                        }
                        vm.SelectedScene!.StarmakerPhoto = true;
                    },
                    Hint = "tutorial.effects.photoTick.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.photoPays.title",
                    Body = "tutorial.effects.photoPays.body",
                    More = "tutorial.effects.photoPays.more",
                    Kind = StepKind.Read,
                    Tab = TabScenes,
                    Anchor = "panel:sceneDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.photoPoints.title",
                    Body = "tutorial.effects.photoPoints.body",
                    More = "tutorial.effects.photoPoints.more",
                    Kind = StepKind.Read,
                    Tab = TabScenes,
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.add.title",
                    Body = "tutorial.effects.add.body",
                    More = "tutorial.effects.add.more",
                    Kind = StepKind.Do,
                    Tab = TabFx,
                    Anchor = "btn:addEffect",
                    AlsoAllow = new[] { "panel:effectDetail" },
                    OnEnter = (vm, s) => s.Set("fx", vm.Effects.Count),
                    IsDone = (vm, s) => s.GrewSince("fx", vm.Effects.Count),
                    Solve = (vm, s) => vm.AddEffectCommand.Execute(null),
                    Hint = "tutorial.effects.add.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.template.title",
                    Body = "tutorial.effects.template.body",
                    More = "tutorial.effects.template.more",
                    Kind = StepKind.Do,
                    Tab = TabFx,
                    Anchor = "panel:effectDetail",
                    // The template it arrived with is only a start: a change says one was chosen.
                    OnEnter = (vm, s) => s.Set("template", vm.SelectedEffect?.Model.Template ?? ""),
                    IsDone = (vm, s) => vm.SelectedEffect is { } e && e.Model.Template != s.Get<string>("template"),
                    Solve = (vm, s) =>
                    {
                        var e = vm.SelectedEffect!;
                        e.DisplayName = HeartsName;
                        e.TemplateOption = EffectViewModel.TemplateOptions.First(o => o.Id == Shared.Effects.VanillaPrefix + "heartShower");
                    },
                    Hint = "tutorial.effects.template.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.change.title",
                    Body = "tutorial.effects.change.body",
                    More = "tutorial.effects.change.more",
                    Kind = StepKind.Read,
                    Tab = TabFx,
                    Anchor = "panel:effectDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.play.title",
                    Body = "tutorial.effects.play.body",
                    More = "tutorial.effects.play.more",
                    Kind = StepKind.Do,
                    Tab = TabDialogues,
                    Anchor = "panel:nodeEditor",
                    AlsoAllow = new[] { "panel:dialogueNodes", "panel:dialogueList", "panel:actionsOnStartBox" },
                    OnEnter = (vm, s) => { if (TutorialsPart2.HerSecret(vm) is { } d) vm.SelectedDialogue = d; },
                    IsDone = (vm, s) => vm.Dialogues.Any(d => d.Nodes.Any(PlaysAnEffect)),
                    Solve = (vm, s) =>
                    {
                        var d = vm.SelectedDialogue ??= TutorialsPart2.HerSecret(vm) ?? vm.Dialogues[^1];
                        // English on purpose: the story pack's words, as tutorial 6 wrote them.
                        var line = d.Nodes.FirstOrDefault(n => n.Text.StartsWith("You're sweet", StringComparison.Ordinal)) ?? d.Nodes[^1];
                        vm.SelectedNode = line;
                        var a = line.AddActionOnStart();
                        a.DisplayType = Model.NodeActionTypes.PlayEffect;
                        a.Model.Params["effect"] = (vm.Effects.FirstOrDefault(e => e.DisplayName == HeartsName) ?? vm.Effects[^1]).Key;
                        a.Reread();
                    },
                    OnShown = vm => TutorialsPart2.ShowHerLine(vm, TutorialsPart2.SweetOf),
                    Hint = "tutorial.effects.play.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.when.title",
                    Body = "tutorial.effects.when.body",
                    More = "tutorial.effects.when.more",
                    Kind = StepKind.Read,
                    Tab = TabFx,
                    Anchor = "panel:effectDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.inPlace.title",
                    Body = "tutorial.effects.inPlace.body",
                    More = "tutorial.effects.inPlace.more",
                    Kind = StepKind.Do,
                    Tab = TabPlaces,
                    Anchor = "btn:addPlaceEffect",
                    AlsoAllow = new[] { "panel:placePreview", "panel:placeEffect" },
                    OnEnter = (vm, s) =>
                    {
                        OpenTheGallery(vm);
                        s.Set("effects", EffectObjects(vm));
                    },
                    IsDone = (vm, s) => s.GrewSince("effects", EffectObjects(vm)),
                    Solve = (vm, s) =>
                    {
                        vm.SelectedPlace ??= StoryPack.Gallery(vm) ?? vm.Places[^1];
                        vm.SelectedPlace!.AddEffectCommand.Execute(null);
                        var made = ObjectsOf(vm).Last(o => o.HasEffect);
                        made.Effect!.TemplateOption = EffectViewModel.TemplateOptions.First(o => o.Id == Shared.Effects.VanillaPrefix + "lightOrbs");
                    },
                    Hint = "tutorial.effects.inPlace.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.effects.theEnd.title",
                    Body = "tutorial.effects.theEnd.body",
                    More = "tutorial.effects.theEnd.more",
                    Kind = StepKind.Read,
                    Tab = TabModForge,
                    Anchor = "menu:file",
                },
            },
        },
    };

    /// <summary>Whether a line plays one of the pack's effects.</summary>
    private static bool PlaysAnEffect(DialogueNodeViewModel n)
    {
        foreach (var list in new[] { n.Model.ActionsOnStart, n.Model.ActionsOnFinish })
        {
            if (list == null) continue;
            foreach (var a in list)
                if (a.Type == Model.NodeActionTypes.PlayEffect &&
                    a.Params.TryGetValue("effect", out var e) && !string.IsNullOrWhiteSpace(e))
                    return true;
        }
        return false;
    }

    /// <summary>Whether any object in a tree, at any depth, is one the test asks for.</summary>
    private static bool Contains(IEnumerable<GameObjectViewModel> nodes, Func<GameObjectViewModel, bool> test)
        => nodes.Any(n => test(n) || Contains(n.Children, test));
}
