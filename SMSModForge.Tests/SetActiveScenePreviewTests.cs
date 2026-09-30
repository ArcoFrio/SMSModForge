using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A small preview of what a Set-Active or SetSprite action targets, under its
/// target (the author, 1.6.3): a scene with its frame, a bust, a level's
/// GameObject, a whole place - and for a sprite swap, before and after with an
/// arrow between. Nothing for a UI or a direct path. Measured on what the
/// window draws: whether a preview is there, how big, and what is in it.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class SetActiveScenePreviewTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-sceneaction-" + Guid.NewGuid().ToString("N"));

    public SetActiveScenePreviewTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private void WritePng(string relative, int width, int height = 0)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bmp = new WriteableBitmap(width, height > 0 ? height : width, 96, 96, PixelFormats.Bgra32, null);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>
    /// A saved pack, open on a node, with one of everything a preview can
    /// show - each picture a size of its own, so which one is drawn can be
    /// read off the picture:
    /// a scene (256), a bust (90 wide), a place (front 240, back 200) holding
    /// a Lamp (64) with a Glow (48) inside it, and art to swap in (50, and a
    /// 512x256 scene - wide, where the scene is square).
    /// </summary>
    private MainViewModel APackOnANode(MainWindow window)
    {
        WritePng("Scenes/photo.png", 256);
        WritePng("Busts/elf.png", 90, 240);
        WritePng("Places/front.png", 240, 100);
        WritePng("Places/back.png", 200, 100);
        WritePng("Objects/lamp.png", 64);
        WritePng("Objects/glow.png", 48);
        WritePng("Swaps/new.png", 50);
        WritePng("Swaps/newscene.png", 512, 256);

        var pack = PackRepository.CreateEmpty("sceneaction.pack");
        pack.Scenes.Add(new SceneDef { Key = "photo", DisplayName = "Photo", SceneSprite = "Scenes/photo.png" });
        var elf = new CharacterDef { Key = "elf", DisplayName = "Elf" };
        elf.Outfits.Add(new OutfitDef { Key = "default", GameObjectName = "Elf_Default", BaseSprite = "Busts/elf.png" });
        pack.Characters.Add(elf);
        var lamp = new GameObjectDef { Name = "Lamp", Sprite = "Objects/lamp.png" };
        lamp.Children.Add(new GameObjectDef { Name = "Glow", Sprite = "Objects/glow.png" });
        pack.Places.Add(new PlaceDef
        {
            Key = "cave", InternalName = "Cave", DisplayName = "Cave",
            BaseSprite = "Places/front.png", SecondarySprite = "Places/back.png",
            GameObjects = { lamp },
        });
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

    /// <summary>An action on the node's finish, of <paramref name="type"/>,
    /// aimed at <paramref name="target"/> in <paramref name="category"/>.</summary>
    private static NodeActionViewModel AnAction(MainViewModel vm, string type, string category,
                                                string target, string level = "")
    {
        var action = vm.SelectedNode!.AddActionOnFinish();
        action.DisplayType = type;
        action.Category = category;
        if (level.Length > 0) action.OverlayLevel = level;
        action.Target = target;
        WindowHarness.Pump();
        return action;
    }

    private NodeActionViewModel AnActionOnTheScene(MainWindow window)
        => AnAction(APackOnANode(window), NodeActionTypes.SetGameObjectActive, NodeActionViewModel.CatScene, "photo");

    private static void SwapIn(NodeActionViewModel action, string sprite)
    {
        action.ParamRows.Single(r => r.Key == "sprite").Value = sprite;
        WindowHarness.Pump();
    }

    /// <summary>The previews the row for <paramref name="action"/> shows, in
    /// the order they are drawn: before, then after.</summary>
    private static List<FrameworkElement> Previews(MainWindow window, NodeActionViewModel action)
        => Descendants<FrameworkElement>(window)
           .Where(e => (e is ScenePreview || e is ArtThumb) && e.IsVisible && ReferenceEquals(e.DataContext, action))
           .ToList();

    /// <summary>The one scene preview the row shows, or null.</summary>
    private static ScenePreview? Shown(MainWindow window, NodeActionViewModel action)
        => Previews(window, action).OfType<ScenePreview>().SingleOrDefault();

    private static bool ArrowShown(MainWindow window, NodeActionViewModel action)
        => Descendants<System.Windows.Shapes.Path>(window)
           .Any(p => "swapArrow".Equals(p.Tag) && p.IsVisible && ReferenceEquals(p.DataContext, action));

    /// <summary>The scene's picture as drawn: the art layer, laid out.</summary>
    private static (BitmapSource? Source, double Width, double Height) Art(ScenePreview preview)
    {
        var art = preview.Children.OfType<Image>().First();
        art.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return ((BitmapSource?)art.Source, art.DesiredSize.Width, art.DesiredSize.Height);
    }

    /// <summary>A small preview decodes its art no bigger than twice the size
    /// it draws it - the point of it being cheap to build.</summary>
    private static void DecodedSmall(BitmapSource source, double drawn)
        => Assert.True(Math.Max(source.PixelWidth, source.PixelHeight) <= Math.Ceiling(drawn * 2),
                       $"decoded at {source.PixelWidth}x{source.PixelHeight} to draw at {drawn}");

    /// <summary>What a small picture draws, once it has been decoded - which
    /// happens away from the window, so this waits for it.</summary>
    private static BitmapSource? Picture(ArtThumb thumb)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (thumb.Picture == null && thumb.Message.Length == 0 && DateTime.UtcNow < until)
            WindowHarness.Wait(TimeSpan.FromMilliseconds(20));
        return thumb.Picture as BitmapSource;
    }

    [Fact]
    public void ASceneTargetShowsASmallPreviewOfTheScene_WithNoBackgroundOfItsOwn()
    {
        WindowHarness.Run(window =>
        {
            var action = AnActionOnTheScene(window);
            var preview = Shown(window, action);
            Assert.NotNull(preview);

            var (source, drawn, _) = Art(preview!);
            _out.WriteLine($"preview {preview!.ActualWidth}x{preview.ActualHeight}, art {source?.PixelWidth}px drawn at {drawn}, background {preview.Background}");
            Assert.Equal(160, preview.ActualWidth, 1);
            Assert.Equal(160, preview.ActualHeight, 1);
            Assert.NotNull(source);
            // The Scenes tab's preview at a third of its size: a 256 scene,
            // drawn at 384 there, is 128 here.
            Assert.Equal(128, drawn, 1);
            // Seamless in the row: nothing drawn behind the scene.
            Assert.Equal(0, ((SolidColorBrush)preview.Background).Color.A);
            // ...where the Scenes tab's keeps its dark backdrop.
            Assert.NotEqual(0, ((SolidColorBrush)new ScenePreview().Background).Color.A);
        });
    }

    [Fact]
    public void AUiOrADirectPathShowsNothing()
    {
        WindowHarness.Run(window =>
        {
            var action = AnActionOnTheScene(window);
            Assert.NotNull(Shown(window, action));

            foreach (string other in new[] { NodeActionViewModel.CatUi, NodeActionViewModel.CatPath })
            {
                action.Category = other;
                action.Target = "photo";
                WindowHarness.Pump();
                _out.WriteLine($"{other}: {Previews(window, action).Count} previews");
                Assert.Empty(Previews(window, action));
            }

            action.Category = NodeActionViewModel.CatScene;
            action.Target = "photo";
            WindowHarness.Pump();
            Assert.NotNull(Shown(window, action));
        });
    }

    [Fact]
    public void NoPreviewForANameTheGameWouldNotFind()
    {
        WindowHarness.Run(window =>
        {
            var action = AnActionOnTheScene(window);

            // Keys are matched case for case, as the game matches them.
            action.Target = "Photo";
            WindowHarness.Pump();
            Assert.Empty(Previews(window, action));
            action.Target = "nothing-by-this-name";
            WindowHarness.Pump();
            Assert.Empty(Previews(window, action));
            // A variable's value is only known in the game.
            action.Target = "$which";
            WindowHarness.Pump();
            Assert.Empty(Previews(window, action));
            action.Target = "photo";
            WindowHarness.Pump();
            Assert.NotNull(Shown(window, action));

            // No bust by a scene's name.
            action.Category = NodeActionViewModel.CatBust;
            action.Target = "photo";
            WindowHarness.Pump();
            Assert.Empty(Previews(window, action));
        });
    }

    [Fact]
    public void ThePreviewFollowsTheScene()
    {
        // Another picture for the scene on the Scenes tab, and the row shows it.
        WindowHarness.Run(window =>
        {
            var action = AnActionOnTheScene(window);
            WritePng("Scenes/bigger.png", 1024, 512);
            var vm = (MainViewModel)window.DataContext;
            vm.Scenes.Single(s => s.Key == "photo").SceneSprite = "Scenes/bigger.png";
            WindowHarness.Pump();

            var (source, drawn, high) = Art(Shown(window, action)!);
            _out.WriteLine($"after the change: art {source?.PixelWidth}x{source?.PixelHeight} drawn at {drawn}x{high}");
            // The wide picture, fitted to a scene's square as the game does.
            Assert.Equal(128, drawn, 1);
            Assert.Equal(64, high, 1);
            DecodedSmall(source!, drawn);
        });
    }

    [Fact]
    public void ASpriteSwapOnASceneShowsItBeforeAndAfter_InItsFrame()
    {
        WindowHarness.Run(window =>
        {
            var vm = APackOnANode(window);
            var action = AnAction(vm, NodeActionTypes.SetSprite, NodeActionViewModel.CatScene, "photo");

            // No sprite chosen yet: the scene as it is, alone.
            var before = Previews(window, action);
            Assert.Single(before);
            Assert.False(ArrowShown(window, action));
            Assert.Equal(160, before[0].ActualWidth, 1);

            SwapIn(action, "Swaps/newscene.png");
            var pair = Previews(window, action).Cast<ScenePreview>().ToList();
            _out.WriteLine($"pair: {string.Join(", ", pair.Select(p => $"{p.SceneSprite} {p.ActualWidth}px"))}");
            Assert.Equal(2, pair.Count);
            Assert.True(ArrowShown(window, action));
            var (was, wasWide, wasHigh) = Art(pair[0]);
            var (becomes, wide, high) = Art(pair[1]);
            _out.WriteLine($"before drawn {wasWide}x{wasHigh}, after drawn {wide}x{high}");
            Assert.Equal(wasWide, wasHigh, 1);        // the square scene...
            Assert.Equal(wasWide, wide, 1);           // ...then the wide art, fitted the same way
            // Half as high, give or take the pixel a smaller decode rounds away.
            Assert.InRange(high, wide / 2 - 1, wide / 2 + 1);
            DecodedSmall(was!, wasWide);
            DecodedSmall(becomes!, wide);
            // The frame stays: only the art is swapped, as in the game.
            Assert.Equal(pair[0].VanillaFrame, pair[1].VanillaFrame);
            Assert.Equal(pair[0].CustomFrameSprite, pair[1].CustomFrameSprite);
            // Side by side, each smaller than one alone - and both inside the row.
            Assert.All(pair, p => Assert.Equal(128, p.ActualWidth, 1));
            var row = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(pair[1]));
            double right = pair[1].TranslatePoint(new Point(pair[1].ActualWidth, 0), row).X;
            _out.WriteLine($"after ends at {right} of a row {row.ActualWidth} wide");
            Assert.True(right <= row.ActualWidth + 0.5, $"the after preview ends at {right}, past the row's {row.ActualWidth}");

            // A Set-Active action has nothing after: back to one.
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = NodeActionViewModel.CatScene;
            action.Target = "photo";
            WindowHarness.Pump();
            Assert.Single(Previews(window, action));
            Assert.False(ArrowShown(window, action));
        });
    }

    [Fact]
    public void ABustIsShownAsThePackDrawsIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = APackOnANode(window);
            var action = AnAction(vm, NodeActionTypes.SetGameObjectActive, NodeActionViewModel.CatBust, "Elf_Default");

            var thumb = Assert.IsType<ArtThumb>(Assert.Single(Previews(window, action)));
            var picture = Picture(thumb);
            _out.WriteLine($"bust: {picture?.PixelWidth}x{picture?.PixelHeight} in {thumb.ActualWidth}px, message '{thumb.Message}'");
            Assert.Equal(90, picture!.PixelWidth);
            Assert.Equal(160, thumb.ActualWidth, 1);

            // Found as the game finds a bust: capitals aside.
            action.Target = "elf_default";
            WindowHarness.Pump();
            Assert.Single(Previews(window, action));

            action.Target = "Nobody_Default";
            WindowHarness.Pump();
            Assert.Empty(Previews(window, action));
        });
    }

    [Fact]
    public void ALevelsGameObjectIsShown_ByNameOrByPath()
    {
        WindowHarness.Run(window =>
        {
            var vm = APackOnANode(window);
            var action = AnAction(vm, NodeActionTypes.SetGameObjectActive, NodeActionViewModel.CatOverlay, "Lamp", "place:cave");

            foreach (var (target, width) in new[] { ("Lamp", 64), ("Lamp > Glow", 48), ("Glow", 48), ("Lamp/Glow", 48) })
            {
                action.Target = target;
                WindowHarness.Pump();
                var thumb = Assert.IsType<ArtThumb>(Assert.Single(Previews(window, action)));
                var picture = Picture(thumb);
                _out.WriteLine($"'{target}': {picture?.PixelWidth}px");
                Assert.Equal(width, picture!.PixelWidth);
            }

            action.Target = "Glow > Lamp";   // no such path
            WindowHarness.Pump();
            Assert.Empty(Previews(window, action));
        });
    }

    [Fact]
    public void APlaceIsShownByItsLayer_AndTheSwapBesideIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = APackOnANode(window);
            var action = AnAction(vm, NodeActionTypes.SetSprite, NodeActionViewModel.CatPlaces, "place:cave");

            var front = Assert.IsType<ArtThumb>(Assert.Single(Previews(window, action)));
            Assert.Equal(240, Picture(front)!.PixelWidth);

            action.PlaceLayerShown = NodeActionViewModel.LayerBackShown;
            WindowHarness.Pump();
            var back = Assert.IsType<ArtThumb>(Assert.Single(Previews(window, action)));
            Assert.Equal(200, Picture(back)!.PixelWidth);

            SwapIn(action, "Swaps/new.png");
            var pair = Previews(window, action).Cast<ArtThumb>().ToList();
            Assert.Equal(2, pair.Count);
            Assert.True(ArrowShown(window, action));
            Assert.Equal(200, Picture(pair[0])!.PixelWidth);
            Assert.Equal(50, Picture(pair[1])!.PixelWidth);
            Assert.All(pair, p => Assert.Equal(128, p.ActualWidth, 1));

            // A sprite that is not there says so, in its place.
            SwapIn(action, "Swaps/nothere.png");
            var missing = Previews(window, action).Cast<ArtThumb>().Last();
            Picture(missing);
            _out.WriteLine($"missing: '{missing.Message}'");
            Assert.Null(missing.Picture);
            Assert.Contains("nothere.png", missing.Message);
        });
    }

    [Fact]
    public void AHiddenPreviewLoadsNothing()
    {
        // Every part of the row's preview is bound all the time; a hidden one
        // holding a real path would still decode it, or open a video.
        WindowHarness.Run(window =>
        {
            var vm = APackOnANode(window);
            var cases = new (string Type, string Category, string Target, string Level, string Sprite)[]
            {
                (NodeActionTypes.SetGameObjectActive, NodeActionViewModel.CatScene, "photo", "", ""),
                (NodeActionTypes.SetGameObjectActive, NodeActionViewModel.CatBust, "Elf_Default", "", ""),
                (NodeActionTypes.SetSprite, NodeActionViewModel.CatBust, "Elf_Default", "", "Swaps/new.png"),
                (NodeActionTypes.SetSprite, NodeActionViewModel.CatScene, "photo", "", "Swaps/newscene.png"),
                (NodeActionTypes.SetSprite, NodeActionViewModel.CatPath, "Some/Path", "", "Swaps/new.png"),
            };
            foreach (var c in cases)
            {
                var action = AnAction(vm, c.Type, c.Category, c.Target, c.Level);
                if (c.Sprite.Length > 0) SwapIn(action, c.Sprite);

                var parts = Descendants<FrameworkElement>(window)
                    .Where(e => (e is ScenePreview || e is ArtThumb) && ReferenceEquals(e.DataContext, action))
                    .ToList();
                // Nothing to show, nothing built; otherwise all four are
                // there, visible or not.
                Assert.Equal(c.Category == NodeActionViewModel.CatPath ? 0 : 4, parts.Count);
                foreach (var hidden in parts.Where(p => !p.IsVisible))
                {
                    string input = hidden is ScenePreview s ? s.SceneSprite : ((ArtThumb)hidden).Sprite;
                    _out.WriteLine($"{c.Type}/{c.Category}: hidden {hidden.GetType().Name} holds '{input}'");
                    Assert.Equal("", input ?? "");
                }
                // The control case: what IS shown does hold something.
                Assert.All(parts.Where(p => p.IsVisible), shown =>
                    Assert.NotEqual("", shown is ScenePreview s ? s.SceneSprite : ((ArtThumb)shown).Sprite));
            }
        });
    }
}
