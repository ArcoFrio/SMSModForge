using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The suite leaves the author's own settings as it found them: the recent
/// list, the preferences, export and publish history, their translations.
/// <para/>
/// Checked, never repaired: this test used to write the author's files back
/// at the end of every run whatever had happened, which with the editor open
/// beside the run could put back an older copy over a change they had just
/// made. A test run keeps its own folder now (<see cref="AppDataFolder"/>),
/// and every test process its own, so the parallel runner's testers cannot
/// share one either.
/// </summary>
public sealed class AuthorsSettingsUntouchedTests : IDisposable
{
    private static readonly string[] Files =
    {
        "recent.json", "prefs.json", "exports.json", "publishes.json", "theme.json",
        "layout.json", "dialogs.json", "preview.json",
    };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-untouched-" + Guid.NewGuid().ToString("N"));
    private readonly (string Path, byte[]? Bytes)[] _kept;

    public AuthorsSettingsUntouchedTests()
    {
        Directory.CreateDirectory(_dir);
        _kept = Files.Select(n => Path.Combine(AppDataFolder.Real, n))
                     .Select(p => (p, File.Exists(p) ? File.ReadAllBytes(p) : null))
                     .ToArray();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private void AssertUnchanged()
    {
        foreach (var (path, bytes) in _kept)
        {
            byte[]? now = File.Exists(path) ? File.ReadAllBytes(path) : null;
            Assert.True(bytes == null ? now == null : now != null && now.AsSpan().SequenceEqual(bytes),
                $"{Path.GetFileName(path)} was changed by the test suite");
        }
    }

    [Fact]
    public void ATestRunKeepsAFolderOfItsOwn()
    {
        Assert.True(TestMode.Active, "the run is not a test run from its first line");
        Assert.NotEqual(AppDataFolder.Real, AppDataFolder.Root);
        Assert.StartsWith(AppDataFolder.TestRuns, AppDataFolder.Root);
        Assert.EndsWith(Environment.ProcessId.ToString(), AppDataFolder.Root);

        // Every service that remembers something keeps it there - read off the
        // services themselves, so one added later with a path of its own fails.
        var services = typeof(AppDataFolder).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                              .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0
                                          && p.Name is "FilePath" or "DataFolder" or "UserFolder" or "StagingRoot"
                                                       or "WindowsUserDictionaryPath")
                              .Select(p => (Name: t.Name + "." + p.Name, Path: (string)p.GetValue(null)!)))
            .ToList();
        Assert.True(services.Count >= 13, $"only {services.Count} remembered paths found");
        var outside = services.Where(s => !s.Path.StartsWith(AppDataFolder.Root, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(outside.Count == 0, "kept outside the run's folder: " + string.Join(", ", outside.Select(s => $"{s.Name} = {s.Path}")));
    }

    [Fact]
    public void OpeningAPackLeavesTheRecentListAlone()
    {
        PackRepository.Save(PackRepository.CreateEmpty("untouched.pack"), _dir);

        var vm = new MainViewModel();
        vm.OpenPackFromPath(_dir);

        AssertUnchanged();
        Assert.DoesNotContain(vm.RecentFiles, p => p.Contains("smsmodforge-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void APreferenceSetByATestStaysInTheRun()
    {
        bool was = EditorPrefs.GameLookNodeRows;
        EditorPrefs.GameLookNodeRows = !was;
        Assert.Equal(!was, EditorPrefs.GameLookNodeRows);
        EditorPrefs.GameLookNodeRows = was;
        AssertUnchanged();
    }
}
