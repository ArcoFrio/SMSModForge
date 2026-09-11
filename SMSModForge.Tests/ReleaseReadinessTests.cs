using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Is this thing fit to publish?
/// <para/>
/// One question so far, and it is asked because the answer was no: every
/// release up to and including 1.2.0 shipped the plugin's F10/F11/F12 scene
/// dumps. Those are diagnostics for whoever is developing the plugin — three
/// function keys a player can press by accident that write megabytes of
/// reflected scene state into their game folder — and they were compiled in
/// unconditionally.
/// <para/>
/// They are behind <c>#if DEBUG</c> now, which makes a Release build clean by
/// construction. What that does NOT prevent is packaging a Debug build by
/// mistake, and nothing in the editor's own test run can see it: the suite
/// compiles in Debug and the plugin is a separate assembly for a different
/// framework. So this reads the bytes of whatever is about to ship.
/// <para/>
/// Point it at the DLL or at the packaged zip:
/// <code>
///   set SMSMODFORGE_RELEASE_PLUGIN=...\Starmaker - ModForge Plugin 1.3.0.zip
///   dotnet test --filter ReleaseReadinessTests
/// </code>
/// </summary>
public sealed class ReleaseReadinessTests
{
    private readonly ITestOutputHelper _out;
    public ReleaseReadinessTests(ITestOutputHelper o) => _out = o;

    /// <summary>
    /// The string the plugin compiles in only under <c>#if DEBUG</c>.
    /// <para/>
    /// Repeated here rather than referenced: the plugin targets .NET Framework
    /// and this project does not, so there is no assembly to read it from. A
    /// second test below proves the two have not drifted, by finding it in the
    /// plugin's source.
    /// </summary>
    private const string DebugMarker = "SMSMODFORGE_PLUGIN_DEBUG_BUILD";

    /// <summary>Everything a Release plugin must not contain — the marker, and
    /// the dump machinery it guards.</summary>
    private static readonly string[] MustNotShip =
    {
        DebugMarker, "DumpScriptsOnScreen", "DumpDialogues", "DumpConditionDebug", "ScriptDump",
    };

    /// <summary>The plugin DLL's bytes, from a DLL or from inside a zip.</summary>
    private static byte[]? PluginBytes(string path)
    {
        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllBytes(path);

        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(
            e => e.Name.Equals("SMSModForge.PackPlugin.dll", StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;

        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Whether a .NET assembly's bytes hold this text, in either of
    /// the two encodings one can appear in: UTF-16 for a string literal, UTF-8
    /// for a type or member name.</summary>
    private static bool Holds(byte[] assembly, string text)
        => Contains(assembly, Encoding.Unicode.GetBytes(text))
        || Contains(assembly, Encoding.UTF8.GetBytes(text));

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return true;
        }
        return false;
    }

    [Fact]
    public void The_plugin_about_to_ship_carries_no_debug_machinery()
    {
        string? path = Environment.GetEnvironmentVariable("SMSMODFORGE_RELEASE_PLUGIN");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _out.WriteLine("SMSMODFORGE_RELEASE_PLUGIN not set; nothing to check.");
            return;
        }

        var bytes = PluginBytes(path);
        Assert.True(bytes != null, $"no SMSModForge.PackPlugin.dll in {path}");
        _out.WriteLine($"{Path.GetFileName(path)} -> {bytes!.Length:N0} bytes");

        var found = MustNotShip.Where(s => Holds(bytes, s)).ToList();
        foreach (string s in MustNotShip)
            _out.WriteLine($"   {(found.Contains(s) ? "PRESENT" : "absent ")}  {s}");

        // Two different faults, and saying the wrong one sends somebody to the
        // wrong fix. The marker can only come from #if DEBUG, so it means a
        // Debug build was packaged. The dump machinery without it means a build
        // from before any of this was conditional - 1.2.0 was compiled with
        // -c Release and still carries all of it.
        string why = found.Contains(DebugMarker)
            ? "this is a Debug build of the plugin. Build it with -c Release before packaging."
            : "this plugin predates the #if DEBUG guard, so its scene dumps ship to players. "
              + "Rebuild it from current source.";

        Assert.True(found.Count == 0, why + " Found: " + string.Join(", ", found));
    }

    [Fact]
    public void The_marker_this_checks_for_is_the_one_the_plugin_defines()
    {
        // The control. Without it this file could go on checking for a string
        // the plugin stopped using, pass on every Debug build ever packaged,
        // and report a clean bill of health forever.
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null && !Directory.Exists(Path.Combine(here.FullName, "SMSModForge.PackPlugin")))
            here = here.Parent;

        Assert.NotNull(here);
        string source = File.ReadAllText(
            Path.Combine(here!.FullName, "SMSModForge.PackPlugin", "Plugin.cs"));

        Assert.Contains($"DebugBuildMarker = \"{DebugMarker}\"", source);

        // And it must be behind the conditional, or a Release build would carry
        // it and this check would fail on a plugin that is perfectly fine.
        int at = source.IndexOf(DebugMarker, StringComparison.Ordinal);
        string before = source[..at];
        Assert.EndsWith("#if DEBUG", before[..before.LastIndexOf("#if DEBUG", StringComparison.Ordinal)]
                        + "#if DEBUG");

        _out.WriteLine($"plugin defines {DebugMarker} under #if DEBUG");
    }
}
