using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Validation;
using SMSModForge.View;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's own scenes in the Scene category of actions and conditions
/// (ArcoFrio, 1.9.0: "the same previewing and handling we have for pack scene
/// conditions/actions on vanilla scenes as well"): listed after the pack's own,
/// by where each sits; previewed with the game's picture and frame, or the
/// pack's change to it; and Validate knows them. Measured on what the window
/// draws, with a catalogue of plain colours (FakeSceneCatalog) - nobody looks at
/// the game's scenes to check anything.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GameSceneTargetTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-scenetargets-" + Guid.NewGuid().ToString("N"));
    private static readonly Color Red = Color.FromRgb(0xD0, 0x20, 0x20);
    private static readonly Color Ochre = Color.FromRgb(0xB0, 0x90, 0x20);

    public GameSceneTargetTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    /// <summary>
    /// A pack of its own scene ("photo", ochre) and a change to the game's
    /// Quiet scene (red picture), saved and open on a node.
    /// </summary>
    private MainViewModel APackOnANode(MainWindow window)
    {
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Scenes", "photo.png"), 256, 256, Ochre);
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Scenes", "red.png"), 256, 256, Red);
        var pack = PackRepository.CreateEmpty("scenetargets.pack");
        pack.Scenes.Add(new SceneDef { Key = "photo", DisplayName = "Photo", SceneSprite = "Scenes/photo.png", VanillaFrame = "SexyFrame.png" });
        pack.Scenes.Add(new SceneDef { Key = "quietchange", DisplayName = "TestQuiet", Source = FakeSceneCatalog.Quiet, SceneSprite = "Scenes/red.png" });
        PackRepository.Save(pack, _dir);

        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues") { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();
        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();
        if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
        vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
        WindowHarness.Pump();
        return vm;
    }

    private static NodeActionViewModel ShowScene(MainViewModel vm, string target)
    {
        var action = vm.SelectedNode!.AddActionOnFinish();
        action.DisplayType = NodeActionTypes.SetGameObjectActive;
        action.Category = NodeActionViewModel.CatScene;
        action.Target = target;
        WindowHarness.Pump();
        return action;
    }

    /// <summary>The one scene preview drawn for <paramref name="row"/>, or null.</summary>
    private static ScenePreview? Shown(MainWindow window, object row)
    {
        window.UpdateLayout();
        WindowHarness.Pump();
        return Descendants<ScenePreview>(window).SingleOrDefault(p => p.IsVisible && ReferenceEquals(p.DataContext, row));
    }

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

    /// <summary>The frame layer's picture size, (0, 0) when none is drawn.</summary>
    private static (int W, int H) FrameDrawn(ScenePreview preview)
    {
        var frame = preview.Children.OfType<Image>().Skip(1).First();
        var source = frame.Source as BitmapSource;
        return frame.Visibility == Visibility.Visible && source != null ? (source.PixelWidth, source.PixelHeight) : (0, 0);
    }

    private static (int W, int H) ShippedFrame(string name)
    {
        var f = BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "VanillaFrames", name)),
                                   BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        return (f.PixelWidth, f.PixelHeight);
    }

    /// <summary>
    /// The list: the pack's scenes first, then the game's under "The game's own:
    /// &lt;group&gt;", by where each sits; the game's scene the pack changes is
    /// there once, by the pack's key; beside a game's scene, the conversation
    /// that shows it. Read off the open list as drawn.
    /// </summary>
    [Fact]
    public void TheSceneCategoryListsThePacksScenes_ThenTheGamesUnderTheirGroups()
    {
        using var catalog = new FakeSceneCatalog();
        WindowHarness.Run(window =>
        {
            var vm = APackOnANode(window);
            var action = ShowScene(vm, "");
            var options = vm.SceneTargetOptions.ToList();
            _out.WriteLine("offered: " + string.Join(" | ", options));
            Assert.Equal(new[] { "photo", "quietchange", FakeSceneCatalog.Kiss, FakeSceneCatalog.Photo }, options);

            window.UpdateLayout();
            var box = Descendants<ComboBox>(window).First(c => c.IsVisible && ReferenceEquals(c.DataContext, action)
                                                                && c.IsEditable && c.Items.Contains(FakeSceneCatalog.Kiss));
            var headings = box.Items.Groups!.Cast<CollectionViewGroup>().Select(g => (string)g.Name).ToList();
            _out.WriteLine("headings: " + string.Join(" | ", headings));
            string games = OptionGroups.GamesOwn;
            Assert.Equal(new[] { OptionGroups.NotInFolder, games + ": Photos", games + ": Sexy" }, headings);

            box.IsDropDownOpen = true;
            WindowHarness.Pump();
            box.UpdateLayout();
            var kiss = (ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem(FakeSceneCatalog.Kiss);
            var texts = Descendants<TextBlock>(kiss).Where(t => t.IsVisible && t.Text.Length > 0).Select(t => t.Text).ToList();
            _out.WriteLine("Kiss row: " + string.Join(" | ", texts));
            Assert.Equal(new[] { FakeSceneCatalog.Kiss, "Kitchen/TestTalk" }, texts);
            // Typing the conversation's name finds the scene it shows.
            ComboBoxSearch.SetTyped(box, "TestTalk");
            WindowHarness.Pump();
            box.UpdateLayout();
            var found = kiss.Visibility;
            var other = ((ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem(FakeSceneCatalog.Photo)).Visibility;
            ComboBoxSearch.SetTyped(box, "");
            box.IsDropDownOpen = false;
            WindowHarness.Pump();
            Assert.Equal(Visibility.Visible, found);
            Assert.Equal(Visibility.Collapsed, other);

            // The change stops being one: the game's scene comes back to the list.
            vm.SelectedGameScene = vm.GameScenes.Single();
            vm.RemoveGameSceneCommand.Execute(null);
            WindowHarness.Pump();
            _out.WriteLine("after removing the change: " + string.Join(" | ", vm.SceneTargetOptions));
            Assert.Equal(new[] { "photo", FakeSceneCatalog.Kiss, FakeSceneCatalog.Photo, FakeSceneCatalog.Quiet }, vm.SceneTargetOptions.ToList());
        });
    }

    /// <summary>
    /// The preview under the target: the game's picture in the game's frame for
    /// one of the game's scenes, the pack's change for one the pack changes
    /// (named by its key or by where it sits), on an action and on a condition
    /// alike. Controls: the pack's own scene draws its own picture; a path the
    /// catalogue does not have draws nothing.
    /// </summary>
    [Fact]
    public void AGameSceneTarget_IsPreviewedWithTheGamesPicture_OrThePacksChange_OnActionsAndConditions()
    {
        using var catalog = new FakeSceneCatalog();
        WindowHarness.Run(window =>
        {
            var vm = APackOnANode(window);

            var kiss = ShowScene(vm, FakeSceneCatalog.Kiss);
            var preview = Shown(window, kiss);
            Assert.NotNull(preview);
            _out.WriteLine($"Kiss: {Middle(preview!)}, frame {FrameDrawn(preview!)}");
            Assert.True(Near(Middle(preview!), FakeSceneCatalog.KissColour), "the game's picture is not drawn");
            Assert.Equal(ShippedFrame("SexyFrame.png"), FrameDrawn(preview!));

            var photo = ShowScene(vm, FakeSceneCatalog.Photo);
            Assert.Equal(ShippedFrame("PhotoFrame.png"), FrameDrawn(Shown(window, photo)!));

            // One the pack changes: the change is what is drawn, by key or by path.
            var byPath = ShowScene(vm, FakeSceneCatalog.Quiet);
            var byKey = ShowScene(vm, "quietchange");
            _out.WriteLine($"Quiet by path {Middle(Shown(window, byPath)!)}, by key {Middle(Shown(window, byKey)!)}");
            Assert.True(Near(Middle(Shown(window, byPath)!), Red), "by path, the pack's change is not drawn");
            Assert.True(Near(Middle(Shown(window, byKey)!), Red), "by key, the pack's change is not drawn");

            // Controls.
            var own = ShowScene(vm, "photo");
            Assert.True(Near(Middle(Shown(window, own)!), Ochre));
            var none = ShowScene(vm, "4_CG_Manager-Sexy/NoSuchScene");
            Assert.Null(Shown(window, none));

            // A condition asking whether the game's scene is showing.
            var condition = vm.SelectedNode!.AddCondition();
            condition.DisplayType = NodeConditionTypes.GameObjectActive;
            condition.GoCategory = NodeActionViewModel.CatScene;
            condition.GoTarget = FakeSceneCatalog.Photo;
            WindowHarness.Pump();
            var asked = Shown(window, condition);
            Assert.NotNull(asked);
            _out.WriteLine($"condition on Photo: {Middle(asked!)}");
            Assert.True(Near(Middle(asked!), FakeSceneCatalog.PhotoColour), "the condition's preview is not the game's picture");
        });
    }
}

/// <summary>
/// With the shipped catalogue (1,255 scenes): the Scene category's list opens
/// in about the time the game's variables' list (1,647 names) does, and a
/// condition's and an action's both hold every scene.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GameSceneTargetRealCatalogueTests
{
    private readonly ITestOutputHelper _out;
    public GameSceneTargetRealCatalogueTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void TheSceneCategorysListOpensAsQuicklyAsTheGamesVariables()
    {
        VanillaScenes.Reload();
        Assert.True(VanillaScenes.All.Count > 1000);
        string dir = Path.Combine(Path.GetTempPath(), "smsmodforge-scenetargets-real-" + Guid.NewGuid().ToString("N"));
        try
        {
            WindowHarness.Run(window =>
            {
                PackRepository.Save(PackRepository.CreateEmpty("scenetargets.pack"), dir);
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(dir);
                var tabs = (TabControl)window.FindName("MainTabs");
                for (int i = 0; i < tabs.Items.Count; i++)
                    if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues") { tabs.SelectedIndex = i; break; }
                WindowHarness.Pump();
                vm.AddDialogueCommand.Execute(null);
                WindowHarness.Pump();
                if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
                vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
                WindowHarness.Pump();

                long Open(ComboBox box)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    box.IsDropDownOpen = true;
                    WindowHarness.Pump();
                    box.UpdateLayout();
                    clock.Stop();
                    box.IsDropDownOpen = false;
                    WindowHarness.Pump();
                    return clock.ElapsedMilliseconds;
                }

                var action = vm.SelectedNode!.AddActionOnFinish();
                action.DisplayType = NodeActionTypes.SetGameObjectActive;
                action.Category = NodeActionViewModel.CatScene;
                WindowHarness.Pump();
                window.UpdateLayout();
                var scenes = Descendants(window).First(c => c.IsVisible && ReferenceEquals(c.DataContext, action)
                                                             && c.IsEditable && c.Items.Contains(VanillaScenes.All[0].Path));
                Assert.Equal(VanillaScenes.All.Count, scenes.Items.Count);
                long sceneMs = Open(scenes);

                var condition = vm.SelectedNode!.AddCondition();
                condition.DisplayType = NodeConditionViewModel.VariableFamilyType;
                condition.VarSource = "Vanilla";
                WindowHarness.Pump();
                window.UpdateLayout();
                var vars = Descendants(window).First(c => c.IsVisible && ReferenceEquals(c.DataContext, condition)
                                                           && OptionNotes.GetKind(c) == OptionNotes.GameVariable);
                long varMs = Open(vars);
                _out.WriteLine($"scenes: {scenes.Items.Count} in {sceneMs} ms; the game's variables: {vars.Items.Count} in {varMs} ms");
                Assert.True(sceneMs < Math.Max(3000, varMs * 2), $"the Scene list took {sceneMs} ms to open (variables {varMs} ms)");
            });
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static IEnumerable<ComboBox> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ComboBox c) yield return c;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}

/// <summary>Validate and the game's scenes as Scene targets (1.9.0).</summary>
public sealed class GameSceneTargetValidationTests
{
    private static ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("scenetargets.pack");
        pack.Scenes.Add(new SceneDef { Key = "photo", DisplayName = "Photo" });
        var d = new DialogueDef { Key = "talk" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1 });
        pack.Dialogues.Add(d);
        return pack;
    }

    private static NodeActionDef Show(string target) => new()
    {
        Type = NodeActionTypes.SetGameObjectActive,
        Params = { ["kind"] = "Scene", ["target"] = target, ["active"] = "true" },
    };

    private static NodeConditionDef Showing(string target) => new()
    {
        Type = NodeConditionTypes.GameObjectActive,
        Params = { ["kind"] = "Scene", ["target"] = target },
    };

    private static string[] Codes(ModPack pack)
        => PackValidator.Validate(pack, "").Where(i => i.Code is "action.unknownScene" or "condition.unknownScene")
                                           .Select(i => i.Code + " " + i.Message).ToArray();

    [Fact]
    public void AGameSceneByWhereItSits_IsKnown_AnUnknownOneIsNot_OnActionsAndConditions()
    {
        using var catalog = new FakeSceneCatalog();
        var pack = Pack();
        var node = pack.Dialogues[0].Nodes[0];
        node.ActionsOnFinish.Add(Show(FakeSceneCatalog.Kiss));
        node.ActionsOnFinish.Add(Show("photo"));
        node.Conditions.Add(Showing(FakeSceneCatalog.Photo));
        node.Conditions.Add(Showing("photo"));
        Assert.Empty(Codes(pack));

        // Controls: a path the catalogue lacks, and a key the pack lacks, on both.
        node.ActionsOnFinish.Add(Show("4_CG_Manager-Sexy/NoSuchScene"));
        node.Conditions.Add(Showing("nosuchscene"));
        var codes = Codes(pack);
        Assert.Equal(2, codes.Length);
        Assert.Contains(codes, c => c.StartsWith("action.unknownScene ") && c.Contains("NoSuchScene"));
        Assert.Contains(codes, c => c.StartsWith("condition.unknownScene ") && c.Contains("nosuchscene"));
    }
}
