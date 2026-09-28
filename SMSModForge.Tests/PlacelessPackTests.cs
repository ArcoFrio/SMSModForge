using System;
using System.IO;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A pack with no places of its own can still put things on the game's levels,
/// NPCs included, and the game plugin has to be ready for them.
/// <para/>
/// The places pass set up the pack's NPC context - and cleared its conditions
/// from the last load - only after returning early for a pack with no places.
/// The vanilla extensions, built after it, ask for that context by the pack's
/// id, so a pack whose only level work was on the game's own levels had every
/// NPC it placed there skipped: "'vanilla:26_Downtown' has NPC placements under
/// 'NPCs' but this build path has no NPC context" (2026-09-27, a pack placing
/// one NPC in Downtown and nothing else).
/// <para/>
/// The plugin runs inside the game, which this suite cannot start, so this
/// reads its source - the same way the other plugin checks here do.
/// </summary>
public sealed class PlacelessPackTests
{
    private static string PluginSource(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "SMSModForge.PackPlugin", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("the plugin's source is not beside the tests, so nothing here is checked", name);
    }

    /// <summary>From the places pass's first line to where it starts on the
    /// places themselves.</summary>
    private static string PlacesPassOpening()
    {
        string text = PluginSource("PlaceFactory.cs");
        int start = text.IndexOf("public static void BuildAll(PackManifest pack", StringComparison.Ordinal);
        Assert.True(start >= 0, "PlaceFactory.BuildAll is not where it was");
        int end = text.IndexOf("foreach (var p in places)", start, StringComparison.Ordinal);
        Assert.True(end > start, "the places loop is not where it was");
        return text.Substring(start, end - start);
    }

    [Fact]
    public void ThePacksNpcsAndConditionsAreReadyBeforeItAsksWhetherThereAreAnyPlaces()
    {
        string opening = PlacesPassOpening();
        int noPlaces = opening.IndexOf("places.Count == 0) return;", StringComparison.Ordinal);
        Assert.True(noPlaces > 0, "the no-places return is gone or reworded; re-read what this test guards");

        int context = opening.IndexOf("NpcFactory.CreateContext(", StringComparison.Ordinal);
        int reset = opening.IndexOf("GameObjectGateRegistry.ResetPack(", StringComparison.Ordinal);
        Assert.True(context >= 0 && context < noPlaces,
            "the NPC context is made after the no-places return, so a pack without places skips the NPCs it puts on the game's levels");
        Assert.True(reset >= 0 && reset < noPlaces,
            "the conditions are cleared after the no-places return, so a pack without places keeps the last load's");
    }

    [Fact]
    public void TheGamesLevelsAreBuiltWithThatContext()
    {
        // The other half: what the extensions are built with is the context
        // the places pass made, asked for by the pack's id.
        string text = PluginSource("NavigatorRuntime.cs");
        Assert.Contains("NpcFactory.ContextFor(pack.PackId)", text);
    }
}
