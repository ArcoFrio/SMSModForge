using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// On Windows set to Turkish, <c>Assembly.GetName</c> throws for some of the
/// game's own assemblies (the Gallery found it, 2.0.0; Harmony reports it as an
/// "IL Compile Error"), so the plugin never calls it. Read off the plugin's
/// source, since the suite cannot load the game.
/// </summary>
public sealed class PluginTurkishWindowsTests
{
    private static readonly Regex GetName = new(@"\.GetName\s*\(\s*\)", RegexOptions.Compiled);

    [Fact]
    public void ThePluginNeverAsksAnAssemblyForItsName()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "SMSModForge.PackPlugin"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var files = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "SMSModForge.PackPlugin"), "*.cs", SearchOption.AllDirectories)
                             .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                             .ToList();
        Assert.True(files.Count > 50, "the plugin's source was not found, so nothing was checked");

        var found = files.SelectMany(f => File.ReadAllLines(f)
                             .Select((line, i) => (f, i, line))
                             .Where(x => !x.line.TrimStart().StartsWith("//") && GetName.IsMatch(x.line)))
                         .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
                         .ToList();
        Assert.Empty(found);

        // The control: the pattern finds the call it is looking for.
        Assert.Matches(GetName, "if (loaded.GetName().Name == x)");
    }
}
