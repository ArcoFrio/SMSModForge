using System;
using System.IO;
using SMSModForge.Model;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The conditions an author sets on an NPC placement are the ones the game
/// follows.
/// <para/>
/// The editor has always let an NPC placement carry activation conditions, and
/// saves them; the game plugin never read them. So a gated NPC showed
/// regardless - and one on a level of the game's was lost for good the moment
/// the game cleared its NPCs object, as Downtown does on every arrival (its
/// DisableChildren switches every child off before one crowd group comes back).
/// An NPC with "Always true" never appeared there (2026-09-27).
/// <para/>
/// The plugin runs inside the game, which this suite cannot start, so this
/// reads its source - the same way the other plugin checks here do.
/// </summary>
public sealed class NpcPlacementConditionsTests
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

    /// <summary>What building one placement does, start to end.</summary>
    private static string BuildPlacementBody()
    {
        string text = PluginSource("NpcFactory.cs");
        int start = text.IndexOf("public static bool BuildPlacement(", StringComparison.Ordinal);
        Assert.True(start >= 0, "NpcFactory.BuildPlacement is not where it was");
        int end = text.IndexOf("return true;", start, StringComparison.Ordinal);
        Assert.True(end > start, "BuildPlacement no longer ends where it did");
        return text.Substring(start, end - start);
    }

    [Fact]
    public void APlacementsConditionsDriveItInTheGame()
    {
        string body = BuildPlacementBody();
        Assert.Contains("pl[\"activeConditions\"]", body);
        Assert.Contains("GameObjectGateRegistry.ForPack(pack.PackId).Register(", body);
    }

    [Fact]
    public void TheGameReadsAnAbsentSwitchBackOffTheWayTheEditorMeansIt()
    {
        // The editor's default is written into every placement, but a pack
        // edited by hand can leave it out; both sides must then mean the same.
        Assert.True(new NpcPlacementDef().DeactivateWhenUnmet);
        Assert.Contains("(bool?)pl[\"deactivateWhenUnmet\"] ?? true", BuildPlacementBody());
    }
}
