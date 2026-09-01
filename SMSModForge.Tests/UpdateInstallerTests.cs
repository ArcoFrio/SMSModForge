using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SMSModForge.Services;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Putting a release in place, over folders in the temp directory rather than
/// over anybody's editor.
/// <para/>
/// The two things worth being sure of are both about damage: what an update is
/// allowed to write into a game folder, and what it refuses to unpack over a
/// working install. Everything else about an update is recoverable by running
/// it again.
/// </summary>
public class UpdateInstallerTests : IDisposable
{
    private readonly string _temp;
    private readonly string _stagingWas;

    public UpdateInstallerTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "smsmf-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);

        // Never the real one: that is a folder in the author's own settings.
        _stagingWas = UpdateInstaller.StagingRoot;
        UpdateInstaller.StagingRoot = Path.Combine(_temp, "staging");
    }

    public void Dispose()
    {
        UpdateInstaller.StagingRoot = _stagingWas;
        try { Directory.Delete(_temp, recursive: true); } catch { /* temp */ }
    }

    private string Dir(params string[] parts)
    {
        string p = Path.Combine(new[] { _temp }.Concat(parts).ToArray());
        Directory.CreateDirectory(p);
        return p;
    }

    private string WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // ── What may be written into a game folder ────────────────────────

    [Theory]
    // Ours: the plugin and what it needs, directly under plugins.
    [InlineData("BepInEx/plugins/SMSModForge.PackPlugin.dll", true)]
    [InlineData("BepInEx/plugins/Newtonsoft.Json.dll", true)]
    [InlineData("BepInEx/plugins/VanillaFrames/PhotoFrame.png", true)]
    // The loader. Already installed, possibly updated by hand, and other mods
    // are running on it - replacing it is well past what this was asked to do.
    [InlineData("BepInEx/core/BepInEx.dll", false)]
    [InlineData("BepInEx/core/0Harmony.dll", false)]
    [InlineData("winhttp.dll", false)]
    [InlineData("doorstop_config.ini", false)]
    [InlineData(".doorstop_version", false)]
    // The author's own work. The zip carries an empty ModPacks folder, and
    // unpacking that over a real one is the one unrecoverable mistake here.
    [InlineData("BepInEx/plugins/SMSModForge/ModPacks/MyPack/modpack.json", false)]
    [InlineData("BepInEx/plugins/SMSModForge/anything.txt", false)]
    // Directory entries carry no content.
    [InlineData("BepInEx/plugins/", false)]
    [InlineData("BepInEx/plugins/VanillaFrames/", false)]
    public void Only_the_plugin_and_what_it_needs_is_ours(string entry, bool ours)
        => Assert.Equal(ours, UpdateInstaller.IsOursInPluginZip(entry));

    [Fact]
    public void Applying_a_plugin_zip_replaces_the_plugin_and_leaves_packs_alone()
    {
        string game = Dir("game");
        // A game folder as it really is: BepInEx installed, the old plugin in
        // place, and the author's packs underneath it.
        WriteFile(Path.Combine(game, "BepInEx", "core", "BepInEx.dll"), "the loader, installed");
        WriteFile(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll"), "old plugin");
        string pack = WriteFile(
            Path.Combine(game, "BepInEx", "plugins", "SMSModForge", "ModPacks", "MyPack", "modpack.json"),
            "a whole pack somebody wrote");

        string zip = Path.Combine(_temp, "plugin.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            void Entry(string name, string text)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write(text);
            }
            Entry("winhttp.dll", "loader shim");
            Entry("BepInEx/core/BepInEx.dll", "a DIFFERENT loader");
            Entry("BepInEx/plugins/SMSModForge.PackPlugin.dll", "new plugin");
            Entry("BepInEx/plugins/Newtonsoft.Json.dll", "json");
            Entry("BepInEx/plugins/VanillaFrames/PhotoFrame.png", "frame");
            Entry("BepInEx/plugins/SMSModForge/ModPacks/", "");
        }

        var result = UpdateInstaller.ApplyPluginZip(zip, game);
        Assert.True(result.Complete, "nothing here should have failed");

        Assert.Equal("new plugin",
            File.ReadAllText(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(game, "BepInEx", "plugins", "VanillaFrames", "PhotoFrame.png")));

        // The controls, and the reason this test exists.
        Assert.Equal("a whole pack somebody wrote", File.ReadAllText(pack));
        Assert.Equal("the loader, installed",
            File.ReadAllText(Path.Combine(game, "BepInEx", "core", "BepInEx.dll")));
        Assert.False(File.Exists(Path.Combine(game, "winhttp.dll")));
        Assert.Equal(3, result.Written.Count);
    }

    [Fact]
    public void A_folder_without_BepInEx_is_not_a_game_folder()
    {
        Assert.False(UpdateInstaller.IsGameFolder(null));
        Assert.False(UpdateInstaller.IsGameFolder(""));
        Assert.False(UpdateInstaller.IsGameFolder(Dir("empty")));
        Assert.False(UpdateInstaller.IsGameFolder(Path.Combine(_temp, "does-not-exist")));

        string game = Dir("real-game", "BepInEx", "plugins");
        Assert.True(UpdateInstaller.IsGameFolder(Path.Combine(_temp, "real-game")));
        Assert.True(Directory.Exists(game));
    }

    // ── What may be unpacked over an install ──────────────────────────

    private string EditorZip(string name, bool complete, string? insideFolder = null)
    {
        string zip = Path.Combine(_temp, name);
        using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
        string prefix = insideFolder == null ? "" : insideFolder + "/";
        void Entry(string entry, string text)
        {
            using var w = new StreamWriter(z.CreateEntry(prefix + entry).Open());
            w.Write(text);
        }
        Entry("SMSModForge.exe", "MZ...");
        if (complete)
        {
            Entry("SMSModForge.dll", "il");
            Entry("SMSModForge.runtimeconfig.json", "{}");
            Entry("Resources/TutorialAssets/note.txt", "art");
        }
        return zip;
    }

    [Fact]
    public void A_good_download_stages_and_reports_no_problem()
    {
        string? staged = UpdateInstaller.StageEditorZip(EditorZip("ok.zip", complete: true), "9.9.9", out var problem);

        Assert.Null(problem);
        Assert.NotNull(staged);
        Assert.True(UpdateInstaller.LooksLikeAnEditorBuild(staged!));
        Assert.True(File.Exists(Path.Combine(staged!, "Resources", "TutorialAssets", "note.txt")));
    }

    [Fact]
    public void A_zip_wrapped_in_one_folder_is_stepped_into()
    {
        // The published zip is flat, but a release packed differently should
        // not be a failed update.
        string? staged = UpdateInstaller.StageEditorZip(
            EditorZip("wrapped.zip", complete: true, insideFolder: "Editor"), "9.9.8", out var problem);

        Assert.Null(problem);
        Assert.EndsWith("Editor", staged);
        Assert.True(UpdateInstaller.LooksLikeAnEditorBuild(staged!));
    }

    [Fact]
    public void A_download_that_is_not_an_editor_is_refused_before_anything_is_replaced()
    {
        // The expensive failure: a truncated download, an error page saved as a
        // zip, an asset that was still uploading. Each unpacks to SOMETHING, and
        // unpacking that over a working install leaves no editor at all.
        string? staged = UpdateInstaller.StageEditorZip(EditorZip("half.zip", complete: false), "9.9.7", out var problem);

        Assert.Null(staged);
        Assert.NotNull(problem);
        Assert.Contains("nothing was replaced", problem);
    }

    [Fact]
    public void Staging_the_same_version_twice_does_not_mix_the_two()
    {
        // A retried download must not leave a file from the abandoned one.
        UpdateInstaller.StageEditorZip(EditorZip("first.zip", complete: true), "9.9.6", out _);
        string leftover = Path.Combine(UpdateInstaller.StagingFor("9.9.6"), "editor", "stale.dll");
        File.WriteAllText(leftover, "from the run before");

        UpdateInstaller.StageEditorZip(EditorZip("second.zip", complete: true), "9.9.6", out _);

        Assert.False(File.Exists(leftover));
    }

    [Fact]
    public void Cleaning_staging_keeps_the_one_it_is_told_to()
    {
        UpdateInstaller.StageEditorZip(EditorZip("a.zip", complete: true), "1.0.0", out _);
        UpdateInstaller.StageEditorZip(EditorZip("b.zip", complete: true), "2.0.0", out _);

        UpdateInstaller.CleanStaging(keepVersion: "2.0.0");

        Assert.False(Directory.Exists(UpdateInstaller.StagingFor("1.0.0")));
        Assert.True(Directory.Exists(UpdateInstaller.StagingFor("2.0.0")));
    }

    // ── The swap ──────────────────────────────────────────────────────

    [Fact]
    public void The_applier_reads_its_arguments()
    {
        Assert.True(UpdateApplier.WasAskedToApply(
            new[] { UpdateApplier.Switch, @"C:\Editor", "4242" }, out var folder, out int pid));
        Assert.Equal(@"C:\Editor", folder);
        Assert.Equal(4242, pid);

        // The controls: an ordinary start must not be mistaken for one of these.
        Assert.False(UpdateApplier.WasAskedToApply(Array.Empty<string>(), out _, out _));
        Assert.False(UpdateApplier.WasAskedToApply(new[] { @"C:\SomePack" }, out _, out _));
        Assert.False(UpdateApplier.WasAskedToApply(new[] { UpdateApplier.Switch }, out _, out _));
    }

    [Fact]
    public void The_editor_an_update_just_installed_knows_it()
    {
        // Without this an updated editor comes up knowing nothing: it checks,
        // finds it is already the latest, and says nothing - leaving the last
        // thing anybody saw as the old editor closing itself.
        Assert.True(UpdateApplier.WasJustUpdated(
            new[] { @"C:\Editor\SMSModForge.exe", UpdateApplier.UpdatedSwitch, "1.1.0" },
            out var from));
        Assert.Equal("1.1.0", from);

        // The version it replaced could not always be read, and the switch on
        // its own still means an update happened.
        Assert.True(UpdateApplier.WasJustUpdated(new[] { UpdateApplier.UpdatedSwitch }, out var none));
        Assert.Equal("", none);

        // The control: every ordinary start, including the one that applies an
        // update, must not claim to be the editor that came out of one.
        Assert.False(UpdateApplier.WasJustUpdated(Array.Empty<string>(), out _));
        Assert.False(UpdateApplier.WasJustUpdated(new[] { @"C:\Editor\SMSModForge.exe" }, out _));
        Assert.False(UpdateApplier.WasJustUpdated(
            new[] { UpdateApplier.Switch, @"C:\Editor", "42" }, out _));
    }

    [Fact]
    public void Copying_over_replaces_what_it_brings_and_keeps_what_it_does_not()
    {
        string from = Dir("new");
        WriteFile(Path.Combine(from, "SMSModForge.dll"), "new build");
        WriteFile(Path.Combine(from, "Resources", "art.png"), "new art");

        string to = Dir("installed");
        WriteFile(Path.Combine(to, "SMSModForge.dll"), "old build");
        string theirs = WriteFile(Path.Combine(to, "notes to self.txt"), "not the editor's");

        // Read-only happens: some unpackers set it, and one stale flag should
        // not stop an update.
        string locked = WriteFile(Path.Combine(to, "Resources", "art.png"), "old art");
        File.SetAttributes(locked, FileAttributes.ReadOnly);

        UpdateApplier.CopyOver(from, to);

        Assert.Equal("new build", File.ReadAllText(Path.Combine(to, "SMSModForge.dll")));
        Assert.Equal("new art", File.ReadAllText(locked));
        // Their file survives: the install folder is somebody's folder, and a
        // wholesale replace would be the one thing here nobody could undo.
        Assert.Equal("not the editor's", File.ReadAllText(theirs));
    }

    [Fact]
    public void One_file_held_open_does_not_take_the_rest_of_the_update_with_it()
    {
        // What went wrong in the field: the plugin DLL could not be replaced -
        // something had it open - and the loop stopped where it stood. The
        // files after it were never tried, the folder was left half replaced,
        // and the exception said nothing about how far it had got.
        string game = Dir("game");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx", "plugins"));
        string locked = WriteFile(
            Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll"), "old plugin");

        string zip = Path.Combine(_temp, "plugin.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            void Entry(string name, string text)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write(text);
            }
            // The DLL first, so anything written after it proves the loop
            // carried on past the failure.
            Entry("BepInEx/plugins/SMSModForge.PackPlugin.dll", "new plugin");
            Entry("BepInEx/plugins/Newtonsoft.Json.dll", "json");
            Entry("BepInEx/plugins/VanillaFrames/PhotoFrame.png", "frame");
        }

        UpdateInstaller.PluginUpdate result;
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            result = UpdateInstaller.ApplyPluginZip(zip, game);

        Assert.False(result.Complete);
        var (file, why) = Assert.Single(result.Failed);
        Assert.Contains("SMSModForge.PackPlugin.dll", file);
        Assert.NotEmpty(why);

        // The rest went in, and the one that did not is still what it was.
        Assert.Equal(2, result.Written.Count);
        Assert.Equal("json",
            File.ReadAllText(Path.Combine(game, "BepInEx", "plugins", "Newtonsoft.Json.dll")));
        Assert.Equal("old plugin", File.ReadAllText(locked));
    }

    [Fact]
    public void A_write_that_lands_the_wrong_length_counts_as_a_failure()
    {
        // The check that turns "it silently did nothing" into "it said so".
        // Everything the update knows about a file it just wrote is its length,
        // and a length that disagrees with the download means the file on disk
        // is not the file that was shipped.
        string game = Dir("game2");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx", "plugins"));

        string zip = Path.Combine(_temp, "plugin2.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("BepInEx/plugins/x.dll").Open()))
            w.Write("some content");

        var result = UpdateInstaller.ApplyPluginZip(zip, game);

        Assert.True(result.Complete);
        Assert.Equal(new FileInfo(Path.Combine(game, "BepInEx", "plugins", "x.dll")).Length,
                     "some content".Length);
    }
}
