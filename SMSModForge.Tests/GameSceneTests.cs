using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.Validation;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A catalogue of the game's scenes made for a test: three scenes whose pictures are
/// each one plain colour, so what a preview draws can be measured by its colour - and
/// nobody looks at the game's own art to check anything (the real pictures are the
/// game's explicit scenes).
/// </summary>
internal sealed class FakeSceneCatalog : IDisposable
{
    public const string Kiss = "4_CG_Manager-Sexy/TestKiss";
    public const string Photo = "4_CG_Manager-Photos/TestPhoto";
    public const string Quiet = "4_CG_Manager-Sexy_Two/TestQuiet";

    public static readonly Color KissColour = Color.FromRgb(0x30, 0xC0, 0x60);
    public static readonly Color PhotoColour = Color.FromRgb(0x30, 0x60, 0xC0);
    public static readonly Color QuietColour = Color.FromRgb(0xE0, 0x80, 0x20);

    public string Folder { get; }
    private readonly string _was;

    public FakeSceneCatalog()
    {
        Folder = Path.Combine(Path.GetTempPath(), "smsmodforge-scenes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Folder, "Thumbs"));
        WritePlain(Path.Combine(Folder, "Thumbs", "0.png"), 128, 128, KissColour);
        WritePlain(Path.Combine(Folder, "Thumbs", "1.png"), 128, 128, PhotoColour);
        WritePlain(Path.Combine(Folder, "Thumbs", "2.png"), 128, 128, QuietColour);
        var index = new JObject
        {
            ["version"] = 1,
            ["scenes"] = new JArray
            {
                Scene(Kiss, "Thumbs/0.png", "SexyFrame", "kiss", false, "8_Room_Talk/Kitchen/TestTalk"),
                Scene(Photo, "Thumbs/1.png", "PhotoFrame", "flash", true),
                Scene(Quiet, "Thumbs/2.png", "SexyFrame", "", false),
            },
        };
        File.WriteAllText(Path.Combine(Folder, "index.json"), index.ToString());
        _was = VanillaScenes.Folder;
        VanillaScenes.Folder = Folder;
        VanillaScenes.Reload();
    }

    private static JObject Scene(string path, string picture, string frame, string signal, bool photo, params string[] shownBy)
        => new()
        {
            ["path"] = path, ["picture"] = picture, ["art"] = path.Split('/')[1], ["artSize"] = new JArray(256.0, 256.0),
            ["frame"] = frame, ["signal"] = signal, ["photo"] = photo, ["shownBy"] = new JArray(shownBy),
        };

    public static void WritePlain(string path, int w, int h, Color c)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    public void Dispose()
    {
        VanillaScenes.Folder = _was;
        VanillaScenes.Reload();
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}

/// <summary>
/// Changes to the game's own scenes (1.9.0): the catalogue, what a pack writes, what
/// Validate says and what the main menu counts as two packs clashing. The window is in
/// <see cref="GameSceneOnScreenTests"/>, the runtime in the in-game suite.
/// </summary>
public sealed class GameSceneTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public GameSceneTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-gamescene-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void TheShippedCatalogueHasTheGamesScenes_EachWithItsPictureAndAFrameTheEditorShips()
    {
        VanillaScenes.Reload();
        var all = VanillaScenes.All;
        _out.WriteLine($"{all.Count} scenes, {all.Select(s => s.Picture).Distinct().Count()} pictures");
        Assert.True(all.Count > 1000, "the catalogue beside the editor is missing or short");

        // Every one found by its path, once; every picture there; every frame one the
        // editor and the plugin ship.
        Assert.Equal(all.Count, all.Select(s => s.Path).Distinct().Count());
        string frames = Path.Combine(AppContext.BaseDirectory, "VanillaFrames");
        foreach (var s in all)
        {
            Assert.Same(s, VanillaScenes.Find(s.Path));
            Assert.StartsWith(VanillaScenes.RootPrefix, s.Path);
            Assert.True(File.Exists(VanillaScenes.PictureOf(s)), "no picture for " + s.Path);
            Assert.True(File.Exists(Path.Combine(frames, s.FrameFile)), "no frame " + s.FrameFile + " for " + s.Path);
            Assert.Contains(s.Signal, new[] { "kiss", "flash", "" });
        }

        // The pictures are the small copies, not the game's art: 102 px a side for the game's
        // 256 (1/2.5, ArcoFrio's pick of 2026-11-15), every one of them.
        foreach (var file in all.Select(VanillaScenes.PictureOf).Distinct())
        {
            var frame = BitmapFrame.Create(new Uri(file), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            Assert.True(Math.Max(frame.PixelWidth, frame.PixelHeight) == 102, file + " is " + frame.PixelWidth + "x" + frame.PixelHeight);
        }

        // Control: a path that is not one of them.
        Assert.Null(VanillaScenes.Find("4_CG_Manager-Sexy/NoSuchScene"));
        Assert.Null(VanillaScenes.Find(""));
    }

    [Fact]
    public void AChangeToOneOfTheGamesScenes_IsSavedWithWhereItSits_AndKeepsItsOwnSound()
    {
        var pack = PackRepository.CreateEmpty("gamescene.pack");
        pack.Scenes.Add(new SceneDef { Source = FakeSceneCatalog.Kiss, Key = "kiss", DisplayName = "Kiss", Sound = SceneSoundMode.None, VanillaFrame = null });
        pack.Scenes.Add(new SceneDef { Source = "", Key = "later", DisplayName = "Later", Sound = SceneSoundMode.None });
        pack.Scenes.Add(new SceneDef { Key = "own", DisplayName = "Own", SceneSprite = "art/own.png" });
        PackRepository.Save(pack, _root);

        var json = JObject.Parse(File.ReadAllText(Path.Combine(_root, "modpack.json")));
        var scenes = (JArray)json["scenes"]!;
        _out.WriteLine(scenes.ToString());
        Assert.Equal(FakeSceneCatalog.Kiss, (string?)scenes[0]["source"]);
        Assert.Equal("None", (string?)scenes[0]["sound"]);
        // None picked yet is still one of the game's: written empty, not left out.
        Assert.Equal("", (string?)scenes[1]["source"]);
        // Control: one of the pack's own has no source at all, as before 1.9.0.
        Assert.Null(scenes[2]["source"]);

        var back = PackRepository.Load(_root);
        Assert.True(back.Scenes[0].IsGameScene);
        Assert.Equal(SceneSoundMode.None, back.Scenes[0].Sound);
        Assert.True(back.Scenes[1].IsGameScene);
        Assert.False(back.Scenes[2].IsGameScene);
    }

    [Fact]
    public void TheExportedPackCarriesTheChangeAndItsPicture()
    {
        // The real save and the real export, as an author makes the file players get.
        string folder = Path.Combine(_root, "pack");
        var pack = PackRepository.CreateEmpty("gamescene.pack");
        FakeSceneCatalog.WritePlain(Path.Combine(folder, "Scenes", "new.png"), 256, 256, System.Windows.Media.Colors.Red);
        pack.Scenes.Add(new SceneDef { Source = FakeSceneCatalog.Kiss, Key = "kiss", DisplayName = "Kiss",
                                       SceneSprite = "Scenes/new.png", Sound = SceneSoundMode.Flash });
        PackRepository.Save(pack, folder);

        string archive = Path.Combine(_root, "gamescene.smspack");
        PackExporter.Export(folder, archive);
        using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.Contains("Scenes/new.png", names);
        var entry = zip.Entries.First(e => e.FullName.Replace('\\', '/') == "modpack.json");
        using var reader = new StreamReader(entry.Open(), System.Text.Encoding.UTF8);
        var scene = (JObject)((JArray)JObject.Parse(reader.ReadToEnd())["scenes"]!)[0];
        _out.WriteLine(scene.ToString());
        Assert.Equal(FakeSceneCatalog.Kiss, (string?)scene["source"]);
        Assert.Equal("Scenes/new.png", (string?)scene["sceneSprite"]);
        Assert.Equal("Flash", (string?)scene["sound"]);
    }

    [Fact]
    public void TheInGameSuitesStoryAndTalkPacksClashOnlyOnTheGameScenesTheyShare()
    {
        // What the in-game check A3 relies on: the main menu marks these two as clashing
        // because of their changes to the game's scenes, and for nothing else.
        var story = InGameSuitePacks.Story.Make(Path.Combine(_root, "story"));
        var talk = InGameSuitePacks.Talk.Make(Path.Combine(_root, "talk"));
        var clashes = PackConflicts.Find(new List<KeyValuePair<string, List<PackConflicts.Change>>>
        {
            new(story.PackId, PackConflicts.Of(JObject.Parse(PackRepository.SerializeAsSaved(story)))),
            new(talk.PackId, PackConflicts.Of(JObject.Parse(PackRepository.SerializeAsSaved(talk)))),
        });
        foreach (var c in clashes) _out.WriteLine($"{c.LaterChange.Thing} ({c.LaterChange.Field})");
        Assert.NotEmpty(clashes);
        Assert.All(clashes, c => Assert.StartsWith("scene 4_CG_Manager-", c.LaterChange.Thing));
        Assert.Contains(clashes, c => c.LaterChange.Thing == "scene " + InGameSuitePacks.Story.GameSceneShared && c.LaterChange.Field == "sceneSprite");
        // The frame only the earlier pack sets is no clash: the later one leaves it alone.
        Assert.DoesNotContain(clashes, c => c.LaterChange.Field == "frame");
    }

    private List<ValidationIssue> Issues(ModPack pack)
    {
        var all = PackValidator.Validate(pack, _root)
            .Where(i => i.Code.StartsWith("scene.game", StringComparison.Ordinal)).ToList();
        foreach (var i in all) _out.WriteLine($"{i.Severity} {i.Code} {i.Where}: {i.Message}");
        return all;
    }

    [Fact]
    public void Validate_SaysWhenNoneIsPicked_WhenTheSceneIsUnknown_WhenOneIsChangedTwice_AndWhenNothingChanges()
    {
        using var catalog = new FakeSceneCatalog();
        var pack = PackRepository.CreateEmpty("gamescene.pack");
        pack.Scenes.Add(new SceneDef { Source = "", Key = "none", DisplayName = "None yet", Sound = SceneSoundMode.None });
        pack.Scenes.Add(new SceneDef { Source = "4_CG_Manager-Sexy/NotAScene", Key = "typo", Sound = SceneSoundMode.Kiss });
        pack.Scenes.Add(new SceneDef { Source = FakeSceneCatalog.Kiss, Key = "first", DisplayName = "First", Sound = SceneSoundMode.Flash });
        pack.Scenes.Add(new SceneDef { Source = FakeSceneCatalog.Kiss, Key = "second", DisplayName = "Second", Sound = SceneSoundMode.Silent });
        pack.Scenes.Add(new SceneDef { Source = FakeSceneCatalog.Quiet, Key = "nothing", Sound = SceneSoundMode.None });
        var issues = Issues(pack);

        Assert.Equal(Severity.Warning, Assert.Single(issues, i => i.Code == GameSceneChecks.CodeNotChosen).Severity);
        Assert.Equal("scenes[typo].source", Assert.Single(issues, i => i.Code == GameSceneChecks.CodeUnknown).Where);
        var twice = Assert.Single(issues, i => i.Code == GameSceneChecks.CodeTwice);
        Assert.Equal(Severity.Error, twice.Severity);
        Assert.Equal("scenes[second].source", twice.Where);
        Assert.Contains("First", twice.Message);
        Assert.Equal("scenes[nothing]", Assert.Single(issues, i => i.Code == GameSceneChecks.CodeNoChange).Where);
    }

    [Fact]
    public void Validate_SaysNothingAboutAChangeThatChangesSomething_OrAboutThePacksOwnScenes()
    {
        // The control for the test above: each of the four kinds of change on its own,
        // and a scene of the pack's own with none of the game's fields.
        using var catalog = new FakeSceneCatalog();
        var pack = PackRepository.CreateEmpty("gamescene.pack");
        pack.Scenes.Add(new SceneDef { Source = FakeSceneCatalog.Kiss, Key = "picture", SceneSprite = "art/p.png", Sound = SceneSoundMode.None });
        pack.Scenes.Add(new SceneDef { Source = FakeSceneCatalog.Photo, Key = "frame", VanillaFrame = "SexyFrame.png", Sound = SceneSoundMode.None });
        pack.Scenes.Add(new SceneDef { Source = FakeSceneCatalog.Quiet, Key = "sound", Sound = SceneSoundMode.Kiss });
        pack.Scenes.Add(new SceneDef { Key = "own", SceneSprite = "art/own.png", Sound = SceneSoundMode.None });
        Assert.Empty(Issues(pack));
    }

    [Fact]
    public void TwoPacksChangingTheSameFieldOfOneOfTheGamesScenes_Clash_OnTheMenusList()
    {
        JObject Saved(string id, Action<SceneDef> change)
        {
            var pack = PackRepository.CreateEmpty(id);
            pack.PackId = id;
            var def = new SceneDef { Source = FakeSceneCatalog.Kiss, Key = "s", Sound = SceneSoundMode.None };
            change(def);
            pack.Scenes.Add(def);
            return JObject.Parse(PackRepository.SerializeAsSaved(pack));
        }
        List<PackConflicts.Clash> Between(JObject a, JObject b) => PackConflicts.Find(new List<KeyValuePair<string, List<PackConflicts.Change>>>
        {
            new("first", PackConflicts.Of(a)), new("second", PackConflicts.Of(b)),
        });

        // Two pictures for one scene: the same path in two packs is two pictures.
        var clash = Assert.Single(Between(Saved("first", d => d.SceneSprite = "art/a.png"), Saved("second", d => d.SceneSprite = "art/a.png")));
        _out.WriteLine($"{clash.LaterChange.Thing} ({clash.LaterChange.Field})");
        Assert.Equal("sceneSprite", clash.LaterChange.Field);
        Assert.Equal("second", clash.Later);
        Assert.Single(Between(Saved("first", d => d.Sound = SceneSoundMode.Kiss), Saved("second", d => d.Sound = SceneSoundMode.Silent)));

        // Controls: different fields both show; the same shipped frame and the same
        // sound agree; leaving the game's sound is no change; two packs' OWN scenes
        // with one key are their own business.
        Assert.Empty(Between(Saved("first", d => d.SceneSprite = "art/a.png"), Saved("second", d => d.Sound = SceneSoundMode.Kiss)));
        Assert.Empty(Between(Saved("first", d => d.VanillaFrame = "SexyFrame.png"), Saved("second", d => d.VanillaFrame = "SexyFrame.png")));
        Assert.Empty(Between(Saved("first", d => d.Sound = SceneSoundMode.Kiss), Saved("second", d => d.Sound = SceneSoundMode.Kiss)));
        Assert.Empty(Between(Saved("first", _ => { }), Saved("second", _ => { })));
        Assert.Empty(Between(Saved("first", d => { d.Source = null; d.SceneSprite = "art/a.png"; }),
                             Saved("second", d => { d.Source = null; d.SceneSprite = "art/a.png"; })));
    }
}

/// <summary>
/// The Scenes tab's list of the game's scenes and its picker, driven through the real
/// window and measured as drawn: a binding to a property that is not there fails
/// silently in WPF, and a preview that draws nothing looks the same as one with
/// nothing to draw.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GameSceneOnScreenTests
{
    private readonly ITestOutputHelper _out;
    public GameSceneOnScreenTests(ITestOutputHelper o) => _out = o;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    private static void ShowScenes(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == Localization.Loc.T("scenes.scenes"))
            { tabs.SelectedIndex = i; WindowHarness.Pump(); return; }
        throw new Xunit.Sdk.XunitException("no Scenes tab");
    }

    /// <summary>The detail pane's preview, the one showing.</summary>
    private static ScenePreview Preview(MainWindow window)
        => Descendants<ScenePreview>(window).First(p => p.IsVisible && p.BoxSize >= ScenePreview.FixedSize);

    /// <summary>The colour drawn at the middle of a preview.</summary>
    private static Color Middle(ScenePreview preview)
    {
        int w = (int)Math.Ceiling(preview.ActualWidth), h = (int)Math.Ceiling(preview.ActualHeight);
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(preview);
        var px = new byte[4];
        bmp.CopyPixels(new Int32Rect(w / 2, h / 2, 1, 1), px, 4, 0);
        return Color.FromRgb(px[2], px[1], px[0]);
    }

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 6 && Math.Abs(a.G - b.G) <= 6 && Math.Abs(a.B - b.B) <= 6;

    /// <summary>The frame layer's picture size, as drawn before zoom.</summary>
    private static (int W, int H) FrameDrawn(ScenePreview preview)
    {
        var frame = preview.Children.OfType<Image>().Skip(1).First();
        var source = frame.Source as BitmapSource;
        return frame.Visibility == Visibility.Visible && source != null ? (source.PixelWidth, source.PixelHeight) : (0, 0);
    }

    private static (int W, int H) FileSize(string path)
    {
        var f = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        return (f.PixelWidth, f.PixelHeight);
    }

    private static string ShippedFrame(string name) => Path.Combine(AppContext.BaseDirectory, "VanillaFrames", name);

    [Fact]
    public void AddingOne_PickingAScene_DrawsTheGamesPictureAndFrame_AndTheRestOfThePanelFollows()
    {
        using var catalog = new FakeSceneCatalog();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowScenes(window);
            int ownBefore = vm.SceneTree.Tree.Count;

            vm.AddGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            var added = vm.SelectedScene!;
            var list = (ListBox)window.FindName("GameSceneList");
            var box = (GroupBox)window.FindName("GameSceneBox");
            var picker = (ListBox)window.FindName("GameScenePicker");
            var name = (TextBlock)window.FindName("GameSceneName");

            // In the game's list and selected there, not in the pack's tree; the
            // picker shows, with every scene of the catalogue; none chosen yet.
            Assert.Same(added, list.SelectedItem);
            Assert.Equal(1, list.Items.Count);
            Assert.Equal(ownBefore, vm.SceneTree.Tree.Count);
            Assert.True(box.IsVisible, "the game's scene box is hidden");
            Assert.Equal(3, picker.Items.Count);
            Assert.Equal(Localization.Loc.T("scenes.game.noneChosen"), name.Text);

            // Picked, as an author picks it: a row of the list.
            picker.SelectedItem = picker.Items.Cast<VanillaScenes.Scene>().Single(s => s.Path == FakeSceneCatalog.Kiss);
            WindowHarness.Pump();
            Assert.Equal(FakeSceneCatalog.Kiss, added.Model.Source);
            Assert.Equal("TestKiss", name.Text);
            Assert.Equal("TestKiss", added.DisplayName);
            Assert.Equal(added.Key, added.Key.Trim());
            _out.WriteLine($"key {added.Key}, row '{added.ChangeSummary}'");
            Assert.Equal(Localization.Loc.T("scenes.game.change.none"), added.ChangeSummary);

            // The preview draws the game's picture (its colour), in the game's frame
            // (the shipped SexyFrame, at its own size).
            var preview = Preview(window);
            var middle = Middle(preview);
            _out.WriteLine($"middle {middle}");
            Assert.True(Near(middle, FakeSceneCatalog.KissColour), $"the preview's middle is {middle}, not the game's picture");
            Assert.Equal(FileSize(ShippedFrame("SexyFrame.png")), FrameDrawn(preview));

            // The sound box offers the game's own first, saying what it is.
            var sound = (ComboBox)window.FindName("GameSceneSound");
            Assert.True(sound.IsVisible);
            var first = (SceneViewModel.SoundChoice)sound.Items[0];
            Assert.Equal(SceneSoundMode.None, first.Mode);
            Assert.Contains(Localization.Loc.T("scenes.game.sound.kiss"), first.Label);
            Assert.Equal(first, sound.SelectedItem);
            sound.SelectedItem = sound.Items.Cast<SceneViewModel.SoundChoice>().Single(c => c.Mode == SceneSoundMode.Silent);
            WindowHarness.Pump();
            Assert.Equal(SceneSoundMode.Silent, added.Model.Sound);
            Assert.Equal(Localization.Loc.T("scenes.game.change.sound"), added.ChangeSummary);
            sound.SelectedItem = sound.Items.Cast<SceneViewModel.SoundChoice>().Single(c => c.Mode == SceneSoundMode.None);
            WindowHarness.Pump();
            Assert.Equal(SceneSoundMode.None, added.Model.Sound);

            // Not one of the game's photos: the Starmaker tick is offered.
            var tick = Descendants<CheckBox>(window).First(c => (c.Content as string) == Localization.Loc.T("scenes.starmakerPhoto"));
            Assert.True(tick.IsVisible);

            // One of the game's photos: its frame is the photo frame, and the tick goes.
            picker.SelectedItem = picker.Items.Cast<VanillaScenes.Scene>().Single(s => s.Path == FakeSceneCatalog.Photo);
            WindowHarness.Pump();
            Assert.Equal("TestPhoto", added.DisplayName);   // followed the pick, since nobody named it
            Assert.True(Near(Middle(Preview(window)), FakeSceneCatalog.PhotoColour));
            Assert.Equal(FileSize(ShippedFrame("PhotoFrame.png")), FrameDrawn(Preview(window)));
            Assert.False(tick.IsVisible, "the tick to make one of the game's photos a photo is offered");
            Assert.Contains(Localization.Loc.T("scenes.game.sound.flash"), ((SceneViewModel.SoundChoice)sound.Items[0]).Label);
            // ...and the box still shows the choice made, through its list being made again.
            Assert.Equal(SceneSoundMode.None, Assert.IsType<SceneViewModel.SoundChoice>(sound.SelectedItem).Mode);

            Snapshot(window, box);
        });
    }

    [Fact]
    public void ThePacksPictureAndFrameReplaceTheGames_AndEmptyingThemBringsTheGamesBack()
    {
        using var catalog = new FakeSceneCatalog();
        string root = Path.Combine(Path.GetTempPath(), "smsmodforge-gamescene-pack-" + Guid.NewGuid().ToString("N"));
        var red = Color.FromRgb(0xD0, 0x20, 0x20);
        FakeSceneCatalog.WritePlain(Path.Combine(root, "art", "red.png"), 256, 256, red);
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.PackRoot = root;
                ShowScenes(window);
                vm.AddGameSceneCommand.Execute(null);
                WindowHarness.Pump();
                var scene = vm.SelectedScene!;
                scene.GameSource = VanillaScenes.Find(FakeSceneCatalog.Kiss);
                WindowHarness.Pump();
                Assert.True(Near(Middle(Preview(window)), FakeSceneCatalog.KissColour));
                // Under the game's picture, why it is small (the author, 1.9.0).
                var note = (TextBlock)window.FindName("SceneGameArtNote");
                window.UpdateLayout();
                Assert.True(note.IsVisible, "no note under the game's picture");
                Assert.Equal(Localization.Loc.T("preview.gameArt.scene"), note.Text);

                scene.SceneSprite = "art/red.png";
                scene.VanillaFrame = "PhotoFrame.png";
                WindowHarness.Pump();
                var middle = Middle(Preview(window));
                _out.WriteLine($"with the pack's picture: {middle}");
                Assert.True(Near(middle, red), $"the pack's picture is not drawn ({middle})");
                window.UpdateLayout();
                Assert.False(note.IsVisible, "the note stayed under the pack's own picture");
                Assert.Equal(FileSize(ShippedFrame("PhotoFrame.png")), FrameDrawn(Preview(window)));
                Assert.Equal(Localization.Loc.T("scenes.game.change.picture") + ", " + Localization.Loc.T("scenes.game.change.frame"), scene.ChangeSummary);

                scene.SceneSprite = "";
                scene.VanillaFrame = "";
                WindowHarness.Pump();
                Assert.True(Near(Middle(Preview(window)), FakeSceneCatalog.KissColour), "emptied, the game's picture did not come back");
                window.UpdateLayout();
                Assert.True(note.IsVisible, "the note did not come back with the game's picture");
                Assert.Equal(FileSize(ShippedFrame("SexyFrame.png")), FrameDrawn(Preview(window)));

                // Control: a scene of the pack's own with no picture draws none - the
                // game's picture is only ever drawn for one of the game's scenes.
                vm.AddSceneCommand.Execute(null);
                WindowHarness.Pump();
                Assert.False(vm.SelectedScene!.IsGameScene);
                Assert.Null(vm.SelectedGameScene);
                Assert.Null(Preview(window).Children.OfType<Image>().First().Source);
                window.UpdateLayout();
                Assert.False(note.IsVisible, "the note shows under a scene of the pack's own");
            });
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public void ASceneThePackAlreadyChanges_IsNotOfferedAgain_AndRemovingTheChangeLeavesThePacksOwn()
    {
        using var catalog = new FakeSceneCatalog();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowScenes(window);
            vm.AddSceneCommand.Execute(null);
            WindowHarness.Pump();
            var own = vm.SelectedScene!;
            var picker = (ListBox)window.FindName("GameScenePicker");

            vm.AddGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            var first = vm.SelectedScene!;
            first.GameSource = VanillaScenes.Find(FakeSceneCatalog.Kiss);
            vm.AddGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            var second = vm.SelectedScene!;
            Assert.NotSame(first, second);

            var offered = picker.Items.Cast<VanillaScenes.Scene>().Select(s => s.Path).ToList();
            _out.WriteLine("offered to the second: " + string.Join(", ", offered));
            Assert.DoesNotContain(FakeSceneCatalog.Kiss, offered);
            Assert.Equal(2, offered.Count);

            // ...but the first still sees its own scene in its list.
            vm.SelectedGameScene = first;
            WindowHarness.Pump();
            Assert.Contains(FakeSceneCatalog.Kiss, picker.Items.Cast<VanillaScenes.Scene>().Select(s => s.Path));
            Assert.Same(VanillaScenes.Find(FakeSceneCatalog.Kiss), picker.SelectedItem);

            // The search narrows by name.
            vm.GameSceneSearch = "quiet";
            WindowHarness.Pump();
            Assert.Equal(new[] { FakeSceneCatalog.Quiet }, picker.Items.Cast<VanillaScenes.Scene>().Select(s => s.Path).Where(p => p != FakeSceneCatalog.Kiss));
            vm.GameSceneSearch = "";

            // Removed from the game's list; the pack's own scene stays, in the tree.
            vm.RemoveGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            Assert.DoesNotContain(first, vm.Scenes);
            Assert.Contains(own, vm.Scenes);
            Assert.NotNull(vm.SceneTree.FindLeaf(own));
            Assert.Equal(new[] { second }, vm.GameScenes);
            Assert.DoesNotContain(vm.Pack.Scenes, s => s.Source == FakeSceneCatalog.Kiss);

            // Duplicating a change makes one with no scene chosen - a copy of the
            // changes for another scene, not a second change to the same one.
            vm.SelectedGameScene = second;
            second.GameSource = VanillaScenes.Find(FakeSceneCatalog.Quiet);
            vm.SelectedTabIndex = MainTab.Scenes;
            vm.DuplicateItemCommand.Execute(null);
            WindowHarness.Pump();
            var copy = vm.SelectedScene!;
            Assert.NotSame(second, copy);
            Assert.True(copy.IsGameScene);
            Assert.Equal("", copy.Model.Source);
            Assert.Same(copy, vm.SelectedGameScene);
        });
    }

    [Fact]
    public void TwoPickedChanges_EachKeepsItsOwnScene_AndTheListAndThePanelStayInStep()
    {
        using var catalog = new FakeSceneCatalog();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowScenes(window);
            var list = (ListBox)window.FindName("GameSceneList");
            var picker = (ListBox)window.FindName("GameScenePicker");
            VanillaScenes.Scene Row(string path) => picker.Items.Cast<VanillaScenes.Scene>().Single(s => s.Path == path);
            // No scenes of the pack's own, so a list made again must pick one of the game's.
            while (vm.Scenes.Any(s => !s.IsGameScene))
            {
                vm.SelectedScene = vm.Scenes.First(s => !s.IsGameScene);
                vm.RemoveSceneCommand.Execute(null);
                WindowHarness.Pump();
            }

            // Two changes, each picked as an author picks: a row of the picker.
            vm.AddGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            var first = vm.SelectedScene!;
            picker.SelectedItem = Row(FakeSceneCatalog.Quiet);
            WindowHarness.Pump();
            vm.AddGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            var second = vm.SelectedScene!;
            Assert.Null(picker.SelectedItem);                       // the new one has none yet
            Assert.Equal(FakeSceneCatalog.Quiet, first.Model.Source);
            picker.SelectedItem = Row(FakeSceneCatalog.Kiss);
            WindowHarness.Pump();

            // Named after their scenes, they sort by those names as a reopened pack
            // would list them - the one picked last first - and it stays selected.
            Assert.Equal(new[] { second, first }, list.Items.Cast<SceneViewModel>());
            Assert.Same(second, list.SelectedItem);
            Assert.Same(second, vm.SelectedGameScene);

            // Back and forth through the list: each shows its own pick, and neither
            // takes the other's.
            for (int i = 0; i < 3; i++)
            {
                list.SelectedItem = first;
                WindowHarness.Pump();
                Assert.Same(first, vm.SelectedScene);
                Assert.Same(Row(FakeSceneCatalog.Quiet), picker.SelectedItem);
                list.SelectedItem = second;
                WindowHarness.Pump();
                Assert.Same(second, vm.SelectedScene);
                Assert.Same(Row(FakeSceneCatalog.Kiss), picker.SelectedItem);
            }
            Assert.Equal(FakeSceneCatalog.Quiet, first.Model.Source);
            Assert.Equal(FakeSceneCatalog.Kiss, second.Model.Source);

            // Undone and redone, the list is made again: what shows is selected in it.
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            vm.RedoCommand.Execute(null);
            WindowHarness.Pump();
            Assert.NotNull(vm.SelectedScene);
            Assert.True(vm.SelectedScene!.IsGameScene);
            Assert.Same(vm.SelectedScene, vm.SelectedGameScene);
            Assert.Same(vm.SelectedScene, list.SelectedItem);
            Assert.True(vm.RemoveGameSceneCommand.CanExecute(null));

            // The tab's own Remove, with one of the game's showing: out of their list
            // too, and the other picked there.
            var removed = vm.SelectedScene;
            vm.RemoveSceneCommand.Execute(null);
            WindowHarness.Pump();
            var left = Assert.Single(vm.GameScenes);
            Assert.NotSame(removed, left);
            Assert.Equal(1, list.Items.Count);
            Assert.Same(left, list.SelectedItem);
            Assert.Same(left, vm.SelectedScene);

            // The last one: nothing left to show.
            vm.RemoveGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Empty(vm.GameScenes);
            Assert.Null(vm.SelectedGameScene);
            Assert.Null(vm.SelectedScene);
        });
    }

    [Fact]
    public void UndoAndRedo_KeepAChangeToOneOfTheGamesScenesInTheirList_EvenWithNoScenePicked()
    {
        using var catalog = new FakeSceneCatalog();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowScenes(window);
            int treeBefore = vm.SceneTree.Tree.Count;

            vm.AddGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            vm.AddGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            vm.SelectedScene!.GameSource = VanillaScenes.Find(FakeSceneCatalog.Quiet);
            WindowHarness.Pump();
            Assert.Equal(2, vm.GameScenes.Count);

            // Undone and redone, both come back as the game's - the one with none
            // picked too, which a null "no scene" would have turned into a scene of
            // the pack's own with no picture, in the tree.
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            vm.RedoCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal(2, vm.GameScenes.Count);
            Assert.Equal(treeBefore, vm.SceneTree.Tree.Count);
            Assert.Contains(vm.GameScenes, s => s.Model.Source == "");
            Assert.Contains(vm.GameScenes, s => s.Model.Source == FakeSceneCatalog.Quiet);

            // The one with none picked, made again by the redo, still takes the name of
            // the scene picked for it first.
            var unpicked = vm.GameScenes.Single(s => s.Model.Source == "");
            unpicked.GameSource = VanillaScenes.Find(FakeSceneCatalog.Kiss);
            WindowHarness.Pump();
            Assert.Equal("TestKiss", unpicked.DisplayName);
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            // Undone back to the start: none left, and the pack's own tree as it was.
            while (vm.UndoCommand.CanExecute(null) && vm.GameScenes.Count > 0) { vm.UndoCommand.Execute(null); WindowHarness.Pump(); }
            Assert.Empty(vm.GameScenes);
            Assert.Equal(treeBefore, vm.SceneTree.Tree.Count);
            Assert.DoesNotContain(vm.Pack.Scenes, s => s.IsGameScene);
        });
    }

    [Fact]
    public void WithTheRealCatalogue_ThePickerBuildsOnlyTheRowsOnScreen_AndOpensQuickly()
    {
        // The shipped catalogue: 1,255 scenes, each row with a picture. Measured by
        // counting the rows the list made, never by looking (they are the game's art).
        VanillaScenes.Reload();
        Assert.True(VanillaScenes.All.Count > 1000);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowScenes(window);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            vm.AddGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            clock.Stop();
            var picker = (ListBox)window.FindName("GameScenePicker");
            int made = Descendants<ListBoxItem>(picker).Count();
            _out.WriteLine($"{picker.Items.Count} offered, {made} rows made, {clock.ElapsedMilliseconds} ms to open");
            Assert.Equal(VanillaScenes.All.Count, picker.Items.Count);
            Assert.True(made < 60, $"{made} rows made for a list showing a handful: it is not virtualising");
            Assert.True(clock.ElapsedMilliseconds < 3000, $"opening the picker took {clock.ElapsedMilliseconds} ms");

            // Searching narrows it, and still builds only what shows.
            vm.GameSceneSearch = "amelia";
            WindowHarness.Pump();
            int offered = picker.Items.Count;
            _out.WriteLine($"'amelia': {offered} offered, {Descendants<ListBoxItem>(picker).Count()} rows made");
            Assert.InRange(offered, 1, VanillaScenes.All.Count - 1);
            Assert.All(picker.Items.Cast<VanillaScenes.Scene>(),
                       s => Assert.True(s.Path.Contains("amelia", StringComparison.OrdinalIgnoreCase)
                                        || s.ShownBy.Any(b => b.Contains("amelia", StringComparison.OrdinalIgnoreCase)), s.Path));
            Assert.True(Descendants<ListBoxItem>(picker).Count() < 60);

            // Picked from the real list: the box names it, the game's frame is drawn.
            var first = picker.Items.Cast<VanillaScenes.Scene>().First();
            picker.SelectedItem = first;
            WindowHarness.Pump();
            Assert.Equal(first.Path, vm.SelectedScene!.Model.Source);
            Assert.Equal(first.Name, ((TextBlock)window.FindName("GameSceneName")).Text);
            Assert.Equal(FileSize(ShippedFrame(first.FrameFile)), FrameDrawn(Preview(window)));
        });
    }

    private static void Press(UIElement list, System.Windows.Input.Key key)
    {
        list.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                                                              PresentationSource.FromVisual(list), 0, key)
        { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
        WindowHarness.Pump();
    }

    [Fact]
    public void InsAndDelOnTheGameScenesList_AddAndRemoveOneChangeToTheGamesScenes()
    {
        using var catalog = new FakeSceneCatalog();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowScenes(window);
            var list = (UIElement)window.FindName("GameSceneList");
            int own = vm.Scenes.Count(s => !s.IsGameScene);

            // The keys shown under that list's buttons act on that list.
            Press(list, System.Windows.Input.Key.Insert);
            Press(list, System.Windows.Input.Key.Insert);
            Assert.Equal(2, vm.GameScenes.Count);
            Assert.True(vm.SelectedScene!.IsGameScene);
            Assert.Equal(own, vm.Scenes.Count(s => !s.IsGameScene));

            // One Del, one change gone - not one for the list and another for the tab.
            Press(list, System.Windows.Input.Key.Delete);
            Assert.Single(vm.GameScenes);
            Assert.Equal(own, vm.Scenes.Count(s => !s.IsGameScene));

            // Control: Ins on the pack's own tree adds one of the pack's scenes.
            Press((UIElement)window.FindName("SceneTreeView"), System.Windows.Input.Key.Insert);
            Assert.Single(vm.GameScenes);
            Assert.Equal(own + 1, vm.Scenes.Count(s => !s.IsGameScene));
        });
    }

    /// <summary>A picture of the box to look at, when asked for (SMSMODFORGE_SNAPSHOTS):
    /// one per theme. Safe to look at: the catalogue's pictures are plain colours.</summary>
    private static void Snapshot(MainWindow window, FrameworkElement box)
    {
        string? shots = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(shots)) return;
        Directory.CreateDirectory(shots);
        var pane = (FrameworkElement)box.Parent;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(pane.ActualWidth), (int)Math.Ceiling(Math.Min(pane.ActualHeight, 2400)),
                                         96, 96, PixelFormats.Pbgra32);
        bmp.Render(pane);
        string theme = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light";
        ThemeSnapshotTests.Save(bmp, Path.Combine(shots, "game-scene-" + theme + ".png"));
        var list = (FrameworkElement)window.FindName("GameSceneList");
        var side = (FrameworkElement)((FrameworkElement)list.Parent).Parent;
        var bmp2 = new RenderTargetBitmap((int)Math.Ceiling(side.ActualWidth), (int)Math.Ceiling(side.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp2.Render(side);
        ThemeSnapshotTests.Save(bmp2, Path.Combine(shots, "game-scene-list-" + theme + ".png"));
    }
}
