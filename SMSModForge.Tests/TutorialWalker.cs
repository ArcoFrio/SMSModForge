using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using SMSModForge.Model;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;

namespace SMSModForge.Tests;

/// <summary>
/// Walks a tutorial the way an author would, and says where it stops.
/// <para/>
/// The editor's view model runs perfectly well with no window: nothing here
/// opens one. What each step ASKS for is written out as a solution below, and
/// the walk asserts two things per step — that it is not already satisfied when
/// the author arrives, and that doing what it says satisfies it.
/// <para/>
/// Those are the two ways a tutorial has actually failed. A step that is
/// already true on arrival flicks past and teaches nothing; a step that cannot
/// be satisfied strands whoever is following it, and until now the only way to
/// find either was for a person to sit and click.
/// </summary>
internal sealed class TutorialWalker : IDisposable
{
    public MainViewModel Vm { get; } = new();

    /// <summary>A pack folder of its own, thrown away afterwards. Several steps
    /// only make sense against a pack that exists on disk.</summary>
    public string Root { get; } = Path.Combine(
        Path.GetTempPath(), "SMSModForgeTutorialTest", Guid.NewGuid().ToString("N"));

    public TutorialWalker() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        catch (IOException) { /* a temp dir that outlives the run is not a failure */ }
    }

    /// <summary>Save the pack where the walker put it, as the author would.</summary>
    public void SaveToRoot()
    {
        Vm.PackRoot = Root;
        PackRepository.Save(Vm.Pack, Root);
    }

    /// <summary>Put the practice art in the pack, which several steps rely on.</summary>
    public void CopyAssets() => TutorialAssets.EnsureCopied(Root);

    /// <summary>
    /// Write a file the pack can point at, for a step that needs art present
    /// without caring what it looks like. Real PNG header, so the dimension
    /// checks read it as the size asked for.
    /// </summary>
    public string WritePng(string rel, int w = 256, int h = 256)
    {
        string abs = Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        var b = new byte[24];
        byte[] sig = { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 };
        sig.CopyTo(b, 0);
        b[11] = 13;
        b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        File.WriteAllBytes(abs, b);
        return rel;
    }
}

/// <summary>
/// What to do to satisfy each step, keyed by tutorial id and step title.
/// <para/>
/// These live with the tests, not with the tutorials. A tutorial stays plain
/// data that the editor renders; knowing how to complete one is a testing
/// concern, and putting it in the catalog would mean every tutorial shipped
/// with a second description of itself to keep in step.
/// <para/>
/// A solution acts through the view model, which is the same thing the buttons
/// act through. It is deliberately NOT allowed to poke the step's own check:
/// the point is to reach the state honestly and let the check judge it.
/// </summary>
internal static class TutorialSolutions
{
    public static string Key(string tutorialId, string stepTitle) => tutorialId + " / " + stepTitle;

    public static readonly Dictionary<string, Action<TutorialWalker>> All = new(StringComparer.Ordinal)
    {
        // ── 1. First steps ────────────────────────────────────────────
        ["first-steps / Start a fresh pack"] = w =>
        {
            // The check watches for a different ModPack instance, which is what
            // File > New pack produces.
            w.Vm.NewPackCommand.Execute(null);
        },
        ["first-steps / Save it somewhere"] = w => w.SaveToRoot(),
        ["first-steps / Add a character"] = w => w.Vm.AddCharacterCommand.Execute(null),
        ["first-steps / Give them a name"] = w =>
        {
            w.Vm.SelectedCharacter!.DisplayName = "Test Girl";
        },
        ["first-steps / Give them a face"] = w =>
        {
            w.CopyAssets();
            w.Vm.SelectedOutfit!.BaseSprite = TutorialAssets.Bust(1);
        },
        ["first-steps / Say what this outfit has"] = w =>
        {
            var o = w.Vm.SelectedOutfit!;
            o.BlinkEnabled = false;
            o.MouthEnabled = false;
            o.ExpressionEnabled = false;
        },

        // ── 4. A face that moves ──────────────────────────────────────
        ["a-face-that-moves / Open the character you made"] = w =>
        {
            GiveACharacter(w);
            w.Vm.SelectedOutfit = w.Vm.Characters[0].Outfits[0];
        },
        ["a-face-that-moves / Give it a blink"] = w =>
        {
            var o = w.Vm.SelectedOutfit!;
            o.BlinkEnabled = true;
            o.BlinkSprite = TutorialAssets.BustBlink(1);
        },
        ["a-face-that-moves / Four mouths, named by a prefix"] = w =>
        {
            var o = w.Vm.SelectedOutfit!;
            o.MouthEnabled = true;
            // A stem, with no number and no extension: the runtime appends
            // 1..4 and the extension itself.
            o.MouthPrefix = TutorialAssets.MouthPrefix(1);
        },
        ["a-face-that-moves / Four expressions, named the same way"] = w =>
        {
            var o = w.Vm.SelectedOutfit!;
            o.ExpressionEnabled = true;
            o.ExpressionPrefix = TutorialAssets.ExpressionPrefix(1);
        },

        // ── 2. A place of your own ────────────────────────────────────
        ["a-place / Add a place"] = w =>
        {
            w.SaveToRoot();
            w.CopyAssets();
            w.Vm.AddPlaceCommand.Execute(null);
        },
        ["a-place / The room itself"] = w =>
            w.Vm.SelectedPlace!.SecondarySprite = TutorialAssets.RoomBase,
        ["a-place / And something standing in it"] = w =>
            w.Vm.SelectedPlace!.BaseSprite = TutorialAssets.RoomSecondary,
        ["a-place / Give it some depth"] = w =>
        {
            var p = w.Vm.SelectedPlace!;
            p.ParallaxSecondaryLinked = false;
            p.ParallaxSecondaryStrength = p.ParallaxStrength + 0.4f;
        },
        ["a-place / A way back out"] = w =>
        {
            var b = w.Vm.SelectedPlace!.AddNavigatorButton();
            b!.Target = Bedroom;
        },
        ["a-place / A door from your bedroom"] = w =>
            w.Vm.AddVanillaExtensionCommand.Execute(null),
        ["a-place / Aim it at the bedroom"] = w =>
            w.Vm.SelectedVanillaExtension!.Source = Bedroom,
        ["a-place / Point it at your room"] = w =>
        {
            // The check counts buttons on an extension that lead to a pack
            // place, so the target has to be this pack's place, not the bedroom.
            var b = w.Vm.SelectedVanillaExtension!.AddNavigatorButton();
            b!.Target = "self:" + w.Vm.Places[0].Key;
        },

        // ── 3. Putting things in the room ─────────────────────────────
        ["dressing-the-room / Open the room you built"] = w =>
        {
            GiveAPlace(w);
            w.Vm.SelectedPlace = w.Vm.Places[0];
        },
        ["dressing-the-room / Add an object"] = w =>
            w.Vm.SelectedPlace!.AddGameObject(),
        ["dressing-the-room / Give it a name and a picture"] = w =>
        {
            var g = w.Vm.SelectedPlace!.GameObjects[0];
            g.Name = "Lamp";
            g.Sprite = TutorialAssets.Npc(0);
        },
        ["dressing-the-room / Decide what it stands in front of"] = w =>
            // Between the back layer at -12 and the front at -10.
            w.Vm.SelectedPlace!.GameObjects[0].SortingOrder = -11,
        ["dressing-the-room / Leave it switched off"] = w =>
            w.Vm.SelectedPlace!.GameObjects[0].StartActive = false,

        // ── 4. On the world map ───────────────────────────────────────
        ["on-the-world-map / Add a map button"] = w =>
        {
            GiveAPlace(w);
            w.Vm.AddMapButtonCommand.Execute(null);
        },
        ["on-the-world-map / Say where it goes"] = w =>
            w.Vm.MapButtons[^1].Target = "self:" + w.Vm.Places[0].Key,
        ["on-the-world-map / Choose which menu it lives in"] = w =>
        {
            // Any district but the one it arrived in: the step is about making
            // a choice, and a new button already sits in Foundry.
            var b = w.Vm.MapButtons[^1];
            var other = SMSModForge.Model.WorldMapDistricts.All
                .First(d => d.GoName != b.District);
            b.District = other.GoName;
        },
        ["on-the-world-map / Give it something to read"] = w =>
            w.Vm.MapButtons[^1].Label = "The Old Workshop",

        // ── 5. Your first conversation ────────────────────────────────
        ["first-conversation / Add a dialogue"] = w =>
        {
            GiveAPlace(w);
            w.Vm.AddDialogueCommand.Execute(null);
        },
        ["first-conversation / Say where it happens"] = w =>
        {
            // Point the pinned level row at the pack's own place, which is what
            // the step asks for and what the game needs to find the dialogue.
            var row = w.Vm.SelectedDialogue!.StartConditions[0];
            row.Model.Params["level"] = "place:" + w.Vm.Places[0].Key;
        },
        ["first-conversation / Write a line"] = w =>
        {
            w.Vm.AddDialogueRootNodeCommand.Execute(null);
            w.Vm.SelectedNode!.Text = "Hello there.";
        },
        ["first-conversation / Give the player a say"] = w =>
        {
            // A finished Choice: a prompt of its own, and two options that say
            // something. All three are rendered, so all three are required.
            var d = w.Vm.SelectedDialogue!;
            w.Vm.AddDialogueRootNodeCommand.Execute(null);
            var choice = w.Vm.SelectedNode!;
            choice.Kind = SMSModForge.Model.DialogueNodeKind.Choice;
            choice.Text = "What do you say?";

            foreach (var label in new[] { "Say hello", "Say nothing" })
            {
                w.Vm.SelectedNode = choice;
                w.Vm.AddDialogueChildNodeCommand.Execute(null);
                w.Vm.SelectedNode!.Text = label;
            }
        },
        ["first-conversation / Make Exit mean something"] = w =>
        {
            // Exit only means anything when something comes after it that is
            // not simply the other branch of the same choice.
            var d = w.Vm.SelectedDialogue!;
            var option = d.Nodes.First(n => n.Text == "Say nothing");
            option.JumpMode = SMSModForge.Model.JumpMode.Exit;

            w.Vm.SelectedNode = null;
            w.Vm.AddDialogueRootNodeCommand.Execute(null);
            w.Vm.SelectedNode!.Text = "A closing line, which Exit skips.";
        },

        // ── 4. Making it move ─────────────────────────────────────────
        ["making-it-move / Open the character you made"] = w =>
        {
            GiveACharacter(w);
            w.Vm.SelectedOutfit = w.Vm.Characters[0].Outfits[0];
        },
        ["making-it-move / Pick a bust with something to move"] = w =>
            // The check is baselined against whatever was already there, so it
            // has to CHANGE, not merely be non-empty.
            w.Vm.SelectedOutfit!.BaseSprite = TutorialAssets.Bust(4),
        ["making-it-move / Paint where it should move"] = w =>
            w.Vm.SelectedOutfit!.MaskSprite = w.WritePng("Sprites/Test/Mask.png"),

        // ── 5. Populating the room ────────────────────────────────────
        ["populating / Define one"] = w =>
        {
            GiveAPlace(w);
            w.Vm.AddNpcCommand.Execute(null);
            w.Vm.SelectedNpc!.Sprite = TutorialAssets.Npc(0);
        },
        ["populating / Give them some life"] = w =>
            w.Vm.SelectedNpc!.Mask = w.WritePng("NPCs/Test/Mask.png"),
        ["populating / Put them in the room"] = w =>
        {
            w.Vm.SelectedPlace = w.Vm.Places[0];
            var placed = w.Vm.SelectedPlace!.NpcsNode.AddNpc();
            placed.Npc = w.Vm.Npcs[0].Key;
        },
        ["populating / Switch them on"] = w =>
            w.Vm.SelectedPlace!.NpcsNode.Npcs[0].StartActive = true,

        // ── 6. Remembering things ─────────────────────────────────────
        ["remembering / Declare one"] = w =>
        {
            GiveADialogueLine(w);
            w.Vm.AddVariableCommand.Execute(null);
            // The check rejects the generated "varN" name, because a variable
            // called that teaches nothing about naming one.
            w.Vm.SelectedVariable!.Name = "MetTheGirl";
        },
        ["remembering / Set it from a conversation"] = w =>
        {
            w.Vm.SelectedNode = w.Vm.SelectedDialogue!.Nodes[0];
            w.Vm.AddNodeActionOnFinishCommand.Execute(null);
            var a = w.Vm.SelectedNode!.ActionsOnFinish.Last();
            a.Model.Type = SMSModForge.Model.NodeActionTypes.SetVariable;
            a.Model.Params["name"] = "MetTheGirl";
            a.Model.Params["value"] = "true";
        },
        ["remembering / Give them something to unlock"] = w =>
        {
            w.Vm.AddWallpaperCommand.Execute(null);
            w.Vm.SelectedWallpaper!.SpritePath = TutorialAssets.Wallpaper;
            w.Vm.SelectedWallpaper!.AddUnlockConditionCommand.Execute(null);
        },

        // ── 7. Rules that run themselves ──────────────────────────────
        ["rules / Make one"] = w =>
        {
            w.SaveToRoot();
            w.CopyAssets();
            w.Vm.AddSceneCommand.Execute(null);
            w.Vm.SelectedScene!.SceneSprite = TutorialAssets.Scene;
            w.Vm.SelectedScene!.DisplayName = "The Big Picture";
        },
        ["rules / Add a rule"] = w => w.Vm.AddIntegrationRuleCommand.Execute(null),
        ["rules / Give it something to watch"] = w =>
            w.Vm.AddIntegrationConditionCommand.Execute(null),
        ["rules / And something to do"] = w =>
        {
            w.Vm.AddIntegrationActionCommand.Execute(null);
            var a = w.Vm.SelectedIntegrationRule!.Actions.Last();
            a.Category = SMSModForge.ViewModel.NodeActionViewModel.CatScene;
            a.Target = w.Vm.Scenes[0].Key;
            a.Active = true;
        },

        // ── The diagnostic walkthrough ────────────────────────────────
        ["smoke / Now do something"] = w => w.Vm.AddCharacterCommand.Execute(null),
    };

    // ── Getting a walker to a starting point ──────────────────────────
    //
    // Later tutorials carry on in the pack an earlier one left, so a walk that
    // starts at tutorial 5 has to be handed the same ground. These do that
    // through the view model, exactly as the earlier tutorial would have.

    private static void GiveACharacter(TutorialWalker w)
    {
        if (w.Vm.Characters.Count > 0) return;
        w.SaveToRoot();
        w.CopyAssets();
        w.Vm.AddCharacterCommand.Execute(null);
        w.Vm.SelectedCharacter!.DisplayName = "Test Girl";
        w.Vm.SelectedOutfit!.BaseSprite = TutorialAssets.Bust(1);
    }

    private static void GiveAPlace(TutorialWalker w)
    {
        GiveACharacter(w);
        if (w.Vm.Places.Count > 0) return;
        w.Vm.AddPlaceCommand.Execute(null);
        w.Vm.SelectedPlace!.SecondarySprite = TutorialAssets.RoomBase;
    }

    private static void GiveADialogueLine(TutorialWalker w)
    {
        GiveAPlace(w);
        if (w.Vm.Dialogues.Count > 0) return;
        w.Vm.AddDialogueCommand.Execute(null);
        w.Vm.AddDialogueRootNodeCommand.Execute(null);
        w.Vm.SelectedNode!.Text = "Hello there.";
    }

    /// <summary>The bedroom every save starts in — what tutorial 2 navigates
    /// to and from. Spelled here so a rename shows up as a test failure rather
    /// than as a tutorial that quietly cannot be finished.</summary>
    private const string Bedroom = "vanilla:5_MyRoom";
}
