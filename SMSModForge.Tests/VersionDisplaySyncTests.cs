using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The version an author READS and the version the pack HAS are the same thing.
/// <para/>
/// Reported after the field went in: opening a pack the migration numbered
/// 0.1.0 left the box showing what it had before, so the editor and the file
/// disagreed from the moment a pack was opened. A version field that can lie is
/// worse than no field, because it is the thing an author checks instead of the
/// manifest.
/// </summary>
public sealed class VersionDisplaySyncTests
{
    private readonly ITestOutputHelper _out;
    public VersionDisplaySyncTests(ITestOutputHelper o) => _out = o;

    private static string WriteOldPack(string root)
    {
        Directory.CreateDirectory(root);
        string manifest = Path.Combine(root, "modpack.json");

        // No "version" at all - a pack from before versioning existed.
        File.WriteAllText(manifest, """
        {
          "packId": "old.pack",
          "dialogues": [ { "key": "intro", "displayName": "Intro" } ]
        }
        """);
        return manifest;
    }

    [Fact]
    public void OpeningAPackTheMigrationNumberedShowsTheNewNumber()
    {
        using var dir = new Scratch();
        WriteOldPack(dir.Path);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(dir.Path);
            WindowHarness.Pump();

            _out.WriteLine($"pack says {vm.Pack.Version}, field says {vm.PackVersionText}");

            Assert.Equal("0.1.0", vm.Pack.Version);
            Assert.Equal(vm.Pack.Version, vm.PackVersionText);
            Assert.True(vm.PackVersionIsValid);
            Assert.True(vm.HasPack);
        });
    }

    [Fact]
    public void APublishThatMovesTheVersionShowsTheNewNumber()
    {
        using var dir = new Scratch();

        // A real saved pack to open - opening an empty folder is an error, not
        // a setup step.
        var seed = PackRepository.CreateEmpty("sync.pack");
        seed.Dialogues.Add(new DialogueDef { Key = "intro" });
        PackRepository.Save(seed, dir.Path);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(dir.Path);
            WindowHarness.Pump();

            string before = vm.PackVersionText;

            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            vm.SavePackCommand.Execute(null);
            WindowHarness.Pump();

            // Saving is not releasing, so the number has not moved and the
            // field is showing the truth by NOT changing.
            Assert.Equal(before, vm.PackVersionText);

            vm.RunPublish(Path.Combine(dir.Path, "release.zip"),
                          vm.VersionForPublish().Version);
            WindowHarness.Pump();

            _out.WriteLine($"{before} -> {vm.PackVersionText} (pack says {vm.Pack.Version})");

            // Publishing moves it, and the field has to have followed - the
            // author reads that box, not the manifest.
            Assert.NotEqual(before, vm.PackVersionText);
            Assert.Equal(vm.Pack.Version, vm.PackVersionText);
        });
    }

    [Fact]
    public void AVideoSceneIsNotAskedToBeAPng()
    {
        // The dimension check reads a PNG header, so an .mp4 failed that read
        // and was reported as "not a PNG" - true, useless, and wrong about the
        // file being a problem.
        using var dir = new Scratch();
        File.WriteAllBytes(Path.Combine(dir.Path, "clip.webm"), new byte[] { 0x1A, 0x45, 0xDF, 0xA3 });
        File.WriteAllBytes(Path.Combine(dir.Path, "loop.gif"), new byte[] { 1, 2, 3, 4 });

        var pack = PackRepository.CreateEmpty("video.pack");
        pack.Scenes.Add(new SceneDef { Key = "v", SceneSprite = "clip.webm" });
        pack.Scenes.Add(new SceneDef { Key = "g", SceneSprite = "loop.gif" });

        var issues = PackValidator.Validate(pack, dir.Path)
            .Where(i => i.Code != null && i.Code.StartsWith("art."))
            .ToList();
        foreach (var i in issues) _out.WriteLine($"{i.Severity} {i.Code} @ {i.Where}");

        Assert.Empty(issues);
    }

    [Fact]
    public void AStillSceneIsStillChecked()
    {
        // The control: silencing the animated ones must not have silenced the
        // check itself.
        using var dir = new Scratch();
        var bmp = new System.Windows.Media.Imaging.WriteableBitmap(
            512, 512, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = File.Create(Path.Combine(dir.Path, "big.png"))) enc.Save(fs);

        var pack = PackRepository.CreateEmpty("still.pack");
        pack.Scenes.Add(new SceneDef { Key = "s", SceneSprite = "big.png" });

        Assert.Contains(PackValidator.Validate(pack, dir.Path),
                        i => i.Code == ArtDimensions.CodeSceneSize);
    }

    private sealed class Scratch : IDisposable
    {
        public string Path { get; }
        public Scratch()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "smsmodforge-vsync-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
