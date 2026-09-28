using System;
using System.IO;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The suite leaves the author's own settings as it found them: the recent
/// list, the preferences. Checked against the real files, which are put back
/// byte for byte afterwards whatever happens.
/// </summary>
public sealed class AuthorsSettingsUntouchedTests : IDisposable
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMSModForge");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-untouched-" + Guid.NewGuid().ToString("N"));
    private readonly (string Path, byte[]? Bytes)[] _kept;

    public AuthorsSettingsUntouchedTests()
    {
        Directory.CreateDirectory(_dir);
        _kept = new[] { "recent.json", "prefs.json" } is var names
            ? Array.ConvertAll(names, n => (Path.Combine(Folder, n), File.Exists(Path.Combine(Folder, n)) ? File.ReadAllBytes(Path.Combine(Folder, n)) : null))
            : Array.Empty<(string, byte[]?)>();
    }

    public void Dispose()
    {
        foreach (var (path, bytes) in _kept)
        {
            try
            {
                if (bytes != null) File.WriteAllBytes(path, bytes);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
        }
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
    public void OpeningAPackLeavesTheRecentListAlone()
    {
        Assert.True(Services.TestMode.Active, "the run is not a test run from its first line");
        PackRepository.Save(PackRepository.CreateEmpty("untouched.pack"), _dir);

        var vm = new MainViewModel();
        vm.OpenPackFromPath(_dir);

        AssertUnchanged();
        Assert.DoesNotContain(vm.RecentFiles, p => p.Contains("smsmodforge-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void APreferenceSetByATestStaysInTheRun()
    {
        bool was = Services.EditorPrefs.GameLookNodeRows;
        Services.EditorPrefs.GameLookNodeRows = !was;
        Assert.Equal(!was, Services.EditorPrefs.GameLookNodeRows);
        Services.EditorPrefs.GameLookNodeRows = was;
        AssertUnchanged();
    }
}
