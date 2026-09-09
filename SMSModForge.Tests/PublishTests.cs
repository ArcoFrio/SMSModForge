using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Publishing: the release, and the only thing that moves a version.
/// <para/>
/// Two problems in one feature. The first is that people install mods wrongly,
/// because the pack has to land in a folder four levels inside BepInEx and
/// getting it wrong produces a game that starts perfectly and simply does not
/// have the mod in it. The second is that the version used to move on saves and
/// exports, neither of which is a release — an author testing in the game
/// twenty times in an afternoon has released nothing.
/// <para/>
/// Both are answered by making the archive a picture of the game folder with
/// the pack already in the right place, and by numbering that event rather than
/// the ones around it.
/// </summary>
public sealed class PublishTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;
    private readonly string _out2;

    public PublishTests(ITestOutputHelper o)
    {
        _out = o;
        string root = Path.Combine(Path.GetTempPath(), "smspub-" + Guid.NewGuid().ToString("N"));
        _dir = Path.Combine(root, "pack");
        _out2 = Path.Combine(root, "releases");
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_out2);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, true); } catch (IOException) { }
    }

    private void Seed(string version, params string[] sceneKeys)
    {
        var made = PackRepository.CreateEmpty("my.pack");
        made.Version = version;
        foreach (string key in sceneKeys)
            made.Scenes.Add(new SceneDef { Key = key, DisplayName = key });
        PackRepository.Save(made, _dir);
    }

    private string Release(string name) => Path.Combine(_out2, name);

    // ── What a player receives ───────────────────────────────────────

    [Fact]
    public void TheArchiveIsThePlayersGameFolder()
    {
        Seed("1.0.0", "opening");

        var made = PackPublisher.Publish(_dir, Release("out.zip"), "my.pack");

        using var zip = ZipFile.OpenRead(made.OutputPath);
        var entries = zip.Entries.Select(e => e.FullName).OrderBy(x => x).ToList();
        _out.WriteLine("archive holds: " + string.Join(", ", entries));

        // One thing, and it is already where it has to end up. Anything else at
        // the root is another decision for somebody who just wants to play -
        // and a readme was worse than that: two packs carrying one collide on
        // the second install, and extracting it leaves a file in the game
        // folder that does nothing.
        Assert.Equal(new[] { "Mods/my.pack.smspack" }, entries);

        // The runtime plugin is deliberately absent: two packs carrying
        // different builds of it would overwrite each other's runtime, and the
        // breakage would land on a player who did nothing wrong.
        Assert.DoesNotContain(entries, e => e.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        // And the folder is the one the RUNTIME reads, not a second name that
        // happens to match today. Both compile from Shared/ModsFolder.cs, so a
        // rename that reached only one of them would fail here rather than
        // shipping an archive that installs into a folder nothing looks in.
        Assert.StartsWith(SMSModForge.Shared.ModsFolder.Name + "/", made.EntryPath);
        Assert.Equal("Mods", SMSModForge.Shared.ModsFolder.Name);
    }

    [Fact]
    public void TheDownloadIsNamedForItsVersionAndTheInstalledFileIsNot()
    {
        // A player with three downloads has to tell them apart; a player
        // installing an update has to REPLACE what they have rather than end up
        // with two copies the game both loads.
        Assert.Equal("my.pack-1.2.0.zip",
                     PackPublisher.ArchiveNameFor("my.pack", new PackVersion(1, 2, 0)));
        Assert.Equal("my.pack.smspack", PackPublisher.PackNameFor("my.pack"));

        Assert.DoesNotContain("1.2.0", PackPublisher.PackNameFor("my.pack"));
    }

    [Fact]
    public void APackIdThatIsNotAFileNameStillProducesOne()
    {
        string name = PackPublisher.ArchiveNameFor("my:pack/of\"things",
                                                   new PackVersion(0, 1, 0));
        _out.WriteLine(name);

        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.EndsWith("-0.1.0.zip", name);
    }

    [Fact]
    public void NothingIsInTheArchiveThatTwoPacksWouldFightOver()
    {
        // Everything an archive contains lands in the same game folder, so any
        // entry not named after the pack is one that a second pack overwrites.
        // Only the Mods folder itself may be shared, because that is the point
        // of it.
        Seed("1.0.0", "opening");
        var made = PackPublisher.Publish(_dir, Release("out.zip"), "my.pack");

        using var zip = ZipFile.OpenRead(made.OutputPath);
        foreach (var entry in zip.Entries)
        {
            _out.WriteLine(entry.FullName);
            Assert.StartsWith(SMSModForge.Shared.ModsFolder.Name + "/", entry.FullName);
            Assert.Contains("my.pack", entry.FullName);
        }
    }

    [Fact]
    public void TheRecordOfWhatWasPublishedIsNotShippedToPlayers()
    {
        // It is a second copy of the manifest. Shipping it would roughly double
        // what a player downloads to tell them nothing.
        Seed("1.0.0", "opening");
        PublishRecord.Write(_dir, File.ReadAllText(Path.Combine(_dir, "modpack.json")));
        Assert.True(File.Exists(Path.Combine(_dir, PublishRecord.FileName)));

        var made = PackPublisher.Publish(_dir, Release("out.zip"), "my.pack");

        using var outer = ZipFile.OpenRead(made.OutputPath);
        string staged = Path.Combine(_out2, "inner.smspack");
        outer.GetEntry("Mods/my.pack.smspack")!.ExtractToFile(staged);

        using var inner = ZipFile.OpenRead(staged);
        var names = inner.Entries.Select(e => e.FullName).ToList();
        _out.WriteLine("pack holds: " + string.Join(", ", names));

        Assert.DoesNotContain(names, n => PublishRecord.Is(n));
        Assert.Contains("modpack.json", names);
    }

    // ── What moves the version ───────────────────────────────────────

    [Fact]
    public void SavingAndExportingDoNotMoveTheVersion()
    {
        // The whole point of the rework. An author testing in the game all
        // afternoon has released nothing, and the number has to say so.
        Seed("1.0.0", "opening");

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();

            for (int i = 0; i < 5; i++)
            {
                vm.AddSceneCommand.Execute(null);
                vm.Scenes.Last().Key = "extra" + i;
                vm.SavePackCommand.Execute(null);
            }

            _out.WriteLine($"five saves with additions -> {vm.Pack.Version}");
            Assert.Equal("1.0.0", vm.Pack.Version);

            vm.ExportPackCommand.Execute(null);
            Assert.Equal("1.0.0", vm.Pack.Version);
        });
    }

    [Fact]
    public void PublishingMovesItOnceForEverythingSinceTheLastRelease()
    {
        Seed("1.0.0", "opening");

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();

            // An addition, so the minor moves - once, however many saves it took.
            vm.AddSceneCommand.Execute(null);
            vm.Scenes.Last().Key = "beach";
            vm.SavePackCommand.Execute(null);
            vm.SavePackCommand.Execute(null);

            var first = vm.RunPublish(Release("a.zip"), Proposed(vm));
            _out.WriteLine($"after an addition -> {vm.Pack.Version}");
            Assert.NotNull(first);
            Assert.Equal("1.1.0", vm.Pack.Version);

            // A change to something that was already there is a patch.
            vm.Scenes[0].DisplayName = "Renamed";
            vm.SavePackCommand.Execute(null);

            vm.RunPublish(Release("b.zip"), Proposed(vm));
            _out.WriteLine($"after a rename -> {vm.Pack.Version}");
            Assert.Equal("1.1.1", vm.Pack.Version);

            // And publishing again with nothing changed moves nothing, so a
            // re-upload does not invent a release.
            vm.RunPublish(Release("c.zip"), Proposed(vm));
            _out.WriteLine($"after no change -> {vm.Pack.Version}");
            Assert.Equal("1.1.1", vm.Pack.Version);
        });
    }

    [Fact]
    public void AMinorReleaseAfterAPatchResetsTheLastNumber()
    {
        // 1.1.1 gaining something is 1.2.0, not 1.2.1 - the patch count
        // belonged to 1.1, and carrying it would claim a fix to a 1.2 nobody
        // had yet. Checked through the editor, not just the arithmetic.
        Seed("1.1.0", "opening");

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();

            // A first release to compare against. Without a record of one
            // there is nothing to diff, so the first publish of any pack reads
            // as an addition - see APackThatWasNeverPublishedStillGetsANumber.
            vm.RunPublish(Release("first.zip"), Proposed(vm));
            Assert.Equal("1.2.0", vm.Pack.Version);

            vm.Scenes[0].DisplayName = "Renamed";
            vm.SavePackCommand.Execute(null);
            vm.RunPublish(Release("a.zip"), Proposed(vm));
            Assert.Equal("1.2.1", vm.Pack.Version);

            vm.AddSceneCommand.Execute(null);
            vm.Scenes.Last().Key = "beach";
            vm.SavePackCommand.Execute(null);
            vm.RunPublish(Release("b.zip"), Proposed(vm));

            _out.WriteLine($"1.2.1 + an addition -> {vm.Pack.Version}");
            Assert.Equal("1.3.0", vm.Pack.Version);
        });
    }

    [Fact]
    public void ATypedVersionIsPublishedAsTyped()
    {
        // Declaring a release is a judgement about the work, and nothing
        // computed from a diff can make it.
        Seed("1.0.0", "opening");

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();

            vm.AddSceneCommand.Execute(null);
            vm.Scenes.Last().Key = "beach";
            vm.SavePackCommand.Execute(null);
            vm.RunPublish(Release("a.zip"), Proposed(vm));
            Assert.Equal("1.1.0", vm.Pack.Version);

            vm.PackVersionText = "2.0.0";
            vm.SavePackCommand.Execute(null);
            vm.RunPublish(Release("b.zip"), Proposed(vm));

            _out.WriteLine($"typed 2.0.0 -> published {vm.Pack.Version}");
            Assert.Equal("2.0.0", vm.Pack.Version);
        });
    }

    [Fact]
    public void TheArchiveCarriesTheVersionItIsNamedFor()
    {
        // The number goes into the manifest before the pack is packaged. A zip
        // called 1.1.0 holding a pack that says 1.0.0 would be the version
        // telling a player one thing and the game another.
        Seed("1.0.0", "opening");

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();

            vm.AddSceneCommand.Execute(null);
            vm.Scenes.Last().Key = "beach";
            vm.SavePackCommand.Execute(null);

            var made = vm.RunPublish(Release("a.zip"), Proposed(vm));
            Assert.NotNull(made);

            using var outer = ZipFile.OpenRead(made!.OutputPath);
            string staged = Path.Combine(_out2, "inner.smspack");
            outer.GetEntry(made.EntryPath)!.ExtractToFile(staged, overwrite: true);

            using var inner = ZipFile.OpenRead(staged);
            using var reader = new StreamReader(inner.GetEntry("modpack.json")!.Open());
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(reader.ReadToEnd());

            _out.WriteLine($"pack inside says v{(string?)manifest["version"]}");
            Assert.Equal("1.1.0", (string?)manifest["version"]);
        });
    }

    [Fact]
    public void APackThatWasNeverPublishedStillGetsANumber()
    {
        // No record to compare against - a fresh clone, a moved folder, or a
        // first release. It proposes something rather than refusing, and the
        // author sees it in the dialog before anything is written.
        Seed("0.1.0", "opening");
        Assert.Null(PublishRecord.Read(_dir));
        Assert.Null(PublishRecord.PublishedVersion(_dir));

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();

            vm.RunPublish(Release("a.zip"), Proposed(vm));
            _out.WriteLine($"first ever release -> {vm.Pack.Version}");

            Assert.Equal("0.2.0", vm.Pack.Version);
            Assert.NotNull(PublishRecord.Read(_dir));
        });
    }

    [Fact]
    public void AFailedPublishLeavesTheVersionAndTheRecordAlone()
    {
        // The control. If a failure moved either, the next release would
        // compare against something nobody ever got and under-report what
        // changed - and the pack would claim a version that never shipped.
        Seed("1.0.0", "opening");

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();

            vm.AddSceneCommand.Execute(null);
            vm.Scenes.Last().Key = "beach";
            vm.SavePackCommand.Execute(null);

            // A path that cannot be written: a directory where a file goes.
            string blocked = Path.Combine(_out2, "blocked.zip");
            Directory.CreateDirectory(blocked);

            var made = vm.RunPublish(blocked, Proposed(vm));

            _out.WriteLine($"after a failed publish -> {vm.Pack.Version}");
            Assert.Null(made);
            Assert.Equal("1.0.0", vm.Pack.Version);
            Assert.Null(PublishRecord.Read(_dir));
        });
    }

    /// <summary>
    /// What the editor would publish at, through the editor own rule rather
    /// than a copy of it here. A test that reimplemented the rule would agree
    /// with itself while disagreeing with the menu item.
    /// </summary>
    private static PackVersion Proposed(MainViewModel vm) => vm.VersionForPublish().Version;
}
