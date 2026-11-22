using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.ViewModel;

namespace SMSModForge.Tutorials;

/// <summary>
/// "Dummy Days" (1.10.0): the one mod every tutorial adds a piece of, and each tutorial can still be followed on
/// its own. A tutorial says what it leaves in the pack (<see cref="TutorialDef.Leaves"/>); a later one whose
/// earlier pieces are missing offers to bring them in, and bringing them in is doing those tutorials' own steps
/// for the reader (<see cref="TutorialStep.Solve"/>) - never a second description of what they make.
/// <para/>
/// The story goes into its own practice pack, never into an author's real one: with another pack open, the
/// practice pack is opened (or made, in the practice folder) first, after the usual question about unsaved work.
/// </summary>
public static class StoryPack
{
    /// <summary>The story's pack, by name. English on purpose: the name a pack is saved under.</summary>
    public const string Name = "Dummy Days";

    /// <summary>Her name in the story. English on purpose: a character's name.</summary>
    public const string DummyName = "Dummy";

    /// <summary>Her gallery, the place of tutorial 2. English on purpose: a place's name.</summary>
    public const string GalleryName = "The Gallery";

    /// <summary>The golden vase in her gallery, an object of tutorial 3. English on purpose: an object's name.</summary>
    public const string VaseName = "Vase";

    /// <summary>The gallery's other mannequins, an NPC of tutorial 9. English on purpose: an NPC's name.</summary>
    public const string MannequinName = "Mannequin";

    /// <summary>The tutorials of the story before <paramref name="tutorial"/> whose work the open pack has not
    /// got, in story order.</summary>
    public static IReadOnlyList<TutorialDef> Missing(MainViewModel vm, TutorialDef tutorial)
    {
        if (tutorial.Leaves == null && tutorial.Level < 1) return Array.Empty<TutorialDef>();
        return TutorialCatalog.All
            .Where(t => t.Leaves != null && t.Level >= 1 && t.Level < tutorial.Level)
            .OrderBy(t => t.Level)
            .Where(t => vm.PackRoot == null || !t.Leaves!(vm))
            .ToList();
    }

    /// <summary>Whether the open pack is the story's: saved somewhere, and called Dummy Days or holding the
    /// Dummy (a reader who named the pack otherwise in tutorial 1 still carries on in theirs).</summary>
    public static bool IsOpen(MainViewModel vm)
        => !string.IsNullOrEmpty(vm.PackRoot) &&
           (string.Equals(vm.Pack.PackId, Name, StringComparison.OrdinalIgnoreCase) || Dummy(vm) != null);

    /// <summary>
    /// Brings in what the tutorials before <paramref name="tutorial"/> make: the practice pack opened (or made)
    /// unless it is open already, then each missing tutorial's steps done for the reader, in story order. False
    /// when the reader kept the pack they had open (the question about unsaved work).
    /// </summary>
    public static bool BringIn(MainViewModel vm, TutorialDef tutorial)
    {
        if (!IsOpen(vm) && !OpenOrMake(vm)) return false;
        TutorialAssets.EnsureCopied(vm.PackRoot);
        foreach (var earlier in Missing(vm, tutorial)) Replay(vm, earlier);
        return true;
    }

    /// <summary>Opens the practice pack the reader has (the newest), or makes one.</summary>
    private static bool OpenOrMake(MainViewModel vm)
    {
        string folder = TutorialDef.PracticeFolder();
        string? existing = Directory.Exists(folder)
            ? new[] { folder }.Concat(Directory.GetDirectories(folder))
                .Where(d => File.Exists(Path.Combine(d, Model.PackRepository.ManifestFileName)))
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, Model.PackRepository.ManifestFileName)))
                .FirstOrDefault()
            : null;
        if (existing != null)
        {
            vm.OpenPackFromPath(existing);
            return string.Equals(vm.PackRoot, existing, StringComparison.OrdinalIgnoreCase);
        }
        return vm.MakePack(NewFolder(), Name, askFirst: true);
    }

    /// <summary>Where a new practice pack goes: "Dummy Days" in the practice folder, or "Dummy Days 2"... when
    /// that one holds something.</summary>
    public static string NewFolder()
    {
        string first = Path.Combine(TutorialDef.PracticeFolder(), Model.NewPackPlace.FolderName(Name));
        if (Model.NewPackPlace.IsFree(first)) return first;
        for (int n = 2; ; n++)
            if (Model.NewPackPlace.IsFree(first + " " + n)) return first + " " + n;
    }

    /// <summary>A tutorial's steps done for the reader, in order, as "Do it for me" does each: a step's baseline
    /// taken, then its way through it when it is not done already.</summary>
    public static void Replay(MainViewModel vm, TutorialDef tutorial)
    {
        var scratch = new TutorialScratch();
        foreach (var step in tutorial.Steps)
        {
            if (step.MakesThePack) continue;
            step.OnEnter?.Invoke(vm, scratch);
            if (step.Kind == StepKind.Read || step.Solve == null) continue;
            if (step.IsDone?.Invoke(vm, scratch) == true) continue;
            step.Solve(vm, scratch);
        }
    }

    /// <summary>The Dummy, the story's character, or null.</summary>
    public static CharacterViewModel? Dummy(MainViewModel vm)
        => vm.Characters.FirstOrDefault(c => !c.IsPlayer && !c.Model.IsVanillaCharacter &&
                                             string.Equals(c.DisplayName.Trim(), DummyName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Her gallery, or null.</summary>
    public static PlaceViewModel? Gallery(MainViewModel vm)
        => vm.Places.FirstOrDefault(p => string.Equals(p.DisplayName.Trim(), GalleryName, StringComparison.OrdinalIgnoreCase));
}
