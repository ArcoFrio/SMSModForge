using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Services;
using SMSModForge.View;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The mask studio in the main window (the author, 1.9.0): "integrated within the
/// main window instead of a separate one, separated in 2 displays, one for the
/// actual mask editor and one for the preview", and "make sure we're not leaving
/// the previous tabs' previews rendering in the background". Measured on the
/// window: which window holds it, what is collapsed, how many frames each
/// preview draws, what reaches the preview and the disk.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class MaskStudioInWindowTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-studio-" + Guid.NewGuid().ToString("N"));

    public MaskStudioInWindowTests(ITestOutputHelper o)
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

    /// <summary>A saved pack with a character whose outfit has a base picture, and a
    /// place with art and a level mask; open, on the tab named.</summary>
    private MainViewModel APack(MainWindow window, string tab)
    {
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Busts", "anna.png"), 256, 256, Color.FromRgb(0xC0, 0x80, 0x60));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Places", "front.png"), 256, 144, Color.FromRgb(0x30, 0x50, 0x70));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Places", "mask.png"), 256, 256, Color.FromRgb(0xFF, 0xFF, 0xFF));
        var pack = PackRepository.CreateEmpty("studio.pack");
        // A second outfit with a mask of its own, a different strength on each
        // layer: Bounce 200, Wave 100, Noise 50.
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Busts", "nightmask.png"), 256, 256, Color.FromRgb(200, 100, 50));
        var anna = new CharacterDef { Key = "anna", Name = "Anna", DisplayName = "Anna" };
        anna.Outfits.Add(new OutfitDef { Key = "casual", GameObjectName = "Anna_Casual", BaseSprite = "Busts/anna.png" });
        anna.Outfits.Add(new OutfitDef { Key = "night", GameObjectName = "Anna_Night", BaseSprite = "Busts/anna.png",
                                         MaskSprite = "Busts/nightmask.png" });
        pack.Characters.Add(anna);
        pack.Places.Add(new PlaceDef { Key = "cave", InternalName = "Cave", DisplayName = "Cave",
                                       BaseSprite = "Places/front.png", MaskSprite = "Places/mask.png" });
        PackRepository.Save(pack, _dir);

        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == tab) { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();
        return vm;
    }

    private static void Click(MainWindow window, string button)
    {
        ((Button)window.FindName(button)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        WindowHarness.Pump();
        window.UpdateLayout();
    }

    /// <summary>Frames a preview draws over a while.</summary>
    private static int FramesOver(Func<int> counter, int ms = 600)
    {
        int before = counter();
        WindowHarness.Wait(TimeSpan.FromMilliseconds(ms));
        return counter() - before;
    }

    /// <summary>
    /// The painter slides in from the right as the tabs go out to the left, and back the other way on closing
    /// (1.10.0, the author: "animations for the mask editors ... sliding horizontally into view and out"), measured
    /// from where each is drawn with the movements on (four times slower, Motions.SlowForTests). The tabs stay up
    /// while they go and are put away once gone; on closing, the painter stays in the window until it has left it.
    /// The rest of the studio's behaviour, with the movements off, is the test after this one.
    /// </summary>
    [Fact]
    [Trait("Timing", "Alone")]
    public void ThePainterSlidesInAndBackOut_TheTabsGoingTheOtherWay()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _out.WriteLine("Windows' own Animation effects are off on this machine: nothing moves - nothing to hold");
            return;
        }
        WindowHarness.Run(window =>
        {
            var vm = APack(window, "Characters");
            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
            WindowHarness.Pump();
            window.UpdateLayout();
            var tabs = (TabControl)window.FindName("MainTabs");
            var host = (ContentControl)window.FindName("MaskStudioHost");
            var area = (FrameworkElement)window.Content;
            double X(UIElement e) => e.TransformToAncestor(area).Transform(new Point()).X;
            bool was = EditorPrefs.Animations;
            Motion.ForTests = true;
            Motions.SlowForTests = 4;
            vm.Animations = true;
            try
            {
                WindowHarness.Wait(TimeSpan.FromMilliseconds(400));
                Click(window, "EditMaskButton");
                var studio = ((MainWindow)window).CurrentMaskStudio!;
                Assert.Equal(Visibility.Visible, tabs.Visibility);   // going, not gone
                double studioFrom = 0, tabsTo = 0;
                var until = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
                while (DateTime.UtcNow < until)
                {
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(8));
                    studioFrom = Math.Max(studioFrom, X(host));
                    if (tabs.IsVisible) tabsTo = Math.Min(tabsTo, X(tabs));
                }
                WindowHarness.Wait(TimeSpan.FromMilliseconds(1600));
                _out.WriteLine($"{ThemeManager.Current.Name}: the painter came from {studioFrom:0} px right, the tabs went {tabsTo:0} px left; area {area.ActualWidth:0}");
                Assert.True(studioFrom > 40, "the painter did not slide in from the right");
                Assert.True(tabsTo < -40, "the tabs did not slide out to the left");
                Assert.Equal(Visibility.Collapsed, tabs.Visibility);
                Assert.Equal(0, X(host), 1);

                // Closing: the tabs come back from the left; the painter leaves to the right and only then the window.
                studio.AnswerUnsaved = () => MessageBoxResult.No;
                Assert.True(studio.TryClose());
                Assert.Equal(Visibility.Visible, tabs.Visibility);
                Assert.Same(studio, host.Content);   // still there, on its way out
                double tabsFrom = 0, studioTo = 0;
                until = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
                while (DateTime.UtcNow < until)
                {
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(8));
                    tabsFrom = Math.Min(tabsFrom, X(tabs));
                    if (host.IsVisible) studioTo = Math.Max(studioTo, X(host));
                }
                WindowHarness.Wait(TimeSpan.FromMilliseconds(1600));
                _out.WriteLine($"closing: the tabs came from {tabsFrom:0} px, the painter went {studioTo:0} px right");
                Assert.True(tabsFrom < -40, "the tabs did not come back from the left");
                Assert.True(studioTo > 40, "the painter did not leave to the right");
                Assert.Null(host.Content);
                Assert.Equal(Visibility.Collapsed, host.Visibility);
                Assert.Equal(0, X(tabs), 1);
                Assert.True(tabs.IsHitTestVisible);
            }
            finally
            {
                Motion.ForTests = false;
                Motions.SlowForTests = 1;
                vm.Animations = was;
            }
        });
    }

    [Fact]
    public void ThePainterOpensInTheMainWindow_TheTabsPreviewStopsWhileItsOwnDraws_AndItAllComesBack()
    {
        WindowHarness.Run(window =>
        {
            var vm = APack(window, "Characters");
            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
            WindowHarness.Pump();
            window.UpdateLayout();

            var tabPreview = (JigglePreview)window.FindName("PreviewControl");
            int tabBefore = FramesOver(() => tabPreview.FrameCallbacks);
            _out.WriteLine($"the tab's preview before: {tabBefore} frames");
            int windows = Application.Current.Windows.Count;

            Click(window, "EditMaskButton");
            var studio = ((MainWindow)window).CurrentMaskStudio;
            Assert.NotNull(studio);
            Assert.Equal(windows, Application.Current.Windows.Count);                       // no window of its own
            Assert.Same(studio, ((ContentControl)window.FindName("MaskStudioHost")).Content);
            Assert.Equal(Visibility.Collapsed, ((TabControl)window.FindName("MainTabs")).Visibility);
            Assert.Equal(SMSModForge.Localization.Loc.F("maskStudio.title", "name", "Anna - casual"), ((TextBlock)studio!.FindName("TitleText")).Text);

            var studioPreview = Descendants<JigglePreview>(studio).Single();
            Assert.Same(vm.SelectedOutfit, studioPreview.Outfit);
            int tabDuring = FramesOver(() => tabPreview.FrameCallbacks);
            int studioDuring = FramesOver(() => studioPreview.FrameCallbacks);
            _out.WriteLine($"while the studio is open: the tab's {tabDuring} frames, the studio's {studioDuring}");
            Assert.True(studioDuring > 0, "the studio's preview draws nothing (and so this proves nothing)");
            Assert.Equal(0, tabDuring);

            // A stroke reaches the preview's live mask at once.
            var outfit = vm.SelectedOutfit!;
            int revision = outfit.LiveMaskRevision;
            studio.BeginStrokeAt(128, 128);
            studio.EndStroke();
            Assert.NotNull(outfit.LiveMaskBgra);
            Assert.True(outfit.LiveMaskRevision > revision);
            Assert.True(studio.IsDirty);
            Assert.Equal(Visibility.Visible, ((TextBlock)studio.FindName("UnsavedMark")).Visibility);

            // A picture of the whole window for a person to look at, when asked for
            // (SMSMODFORGE_SNAPSHOTS, as ThemeSnapshotTests): one per theme.
            string? shots = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
            if (!string.IsNullOrWhiteSpace(shots) && window.Content is FrameworkElement root)
            {
                Directory.CreateDirectory(shots);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bmp.Render(root);
                ThemeSnapshotTests.Save(bmp, Path.Combine(shots, "mask-studio-" +
                    (Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light") + ".png"));
            }

            // Leaving with strokes not saved asks: Cancel stays, and another pack is
            // not opened over it either.
            studio.AnswerUnsaved = () => MessageBoxResult.Cancel;
            Assert.False(studio.TryClose());
            string other = Path.Combine(_dir + "-other");
            PackRepository.Save(PackRepository.CreateEmpty("other.pack"), other);
            vm.OpenPackFromPath(other);
            WindowHarness.Pump();
            Assert.Equal(_dir, vm.PackRoot);
            Assert.Same(studio, ((MainWindow)window).CurrentMaskStudio);

            // No: the strokes go, the preview is the file's again, the tabs come back
            // and their preview draws again.
            studio.AnswerUnsaved = () => MessageBoxResult.No;
            Assert.True(studio.TryClose());
            WindowHarness.Pump();
            window.UpdateLayout();
            Assert.Null(((MainWindow)window).CurrentMaskStudio);
            Assert.Null(outfit.LiveMaskBgra);
            Assert.Equal(Visibility.Visible, ((TabControl)window.FindName("MainTabs")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((ContentControl)window.FindName("MaskStudioHost")).Visibility);
            int tabAfter = FramesOver(() => tabPreview.FrameCallbacks);
            int studioAfter = FramesOver(() => studioPreview.FrameCallbacks);
            _out.WriteLine($"after: the tab's {tabAfter} frames, the closed studio's {studioAfter}");
            Assert.True(tabAfter > 0, "the tab's preview did not come back");
            Assert.Equal(0, studioAfter);

            // The closed studio's preview no longer watches the outfit: a new
            // picture reloads the tab's preview (the control) and not it.
            Assert.Null(studioPreview.Outfit);
            int studioLoads = studioPreview.TextureLoads, tabLoads = tabPreview.TextureLoads;
            outfit.BaseSprite = "Busts/nightmask.png";
            WindowHarness.Pump();
            _out.WriteLine($"after a new picture: the tab's preview loaded {tabPreview.TextureLoads - tabLoads}, the closed studio's {studioPreview.TextureLoads - studioLoads}");
            Assert.True(tabPreview.TextureLoads > tabLoads, "the tab's preview did not reload, so this proves nothing");
            Assert.Equal(studioLoads, studioPreview.TextureLoads);
            try { Directory.Delete(other, true); } catch (IOException) { }
        });
    }

    /// <summary>
    /// The soften brush's Edge softness (1.9.1) takes Opacity's place: with the
    /// soften brush it shows where Opacity was and Opacity goes, and every word
    /// on the tools panel reaches the screen whole, in every language. The
    /// control: back on the brush, Opacity is there again and Edge softness gone.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("zh-Hans")]
    public void EdgeSoftnessTakesOpacitysPlace_AndNothingOnTheToolsIsCut(string language)
    {
        // A window reads its words as it is built, so the language comes first.
        SMSModForge.Localization.Loc.Use(language);
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = APack(window, "");
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Characters;
                WindowHarness.Pump();
                vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
                vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
                WindowHarness.Pump();
                Click(window, "EditMaskButton");
                var studio = ((MainWindow)window).CurrentMaskStudio!;
                var tools = (FrameworkElement)studio.FindName("ToolsGroup");
                FrameworkElement Named(string name) => (FrameworkElement)studio.FindName(name);
                double Top(string name) => Named(name).TranslatePoint(new Point(0, 0), tools).Y;

                WindowHarness.Pump();
                window.UpdateLayout();
                Assert.True(Named("OpacityLabel").IsVisible && Named("OpacityRow").IsVisible);
                Assert.False(Named("SoftnessLabel").IsVisible || Named("SoftnessRow").IsVisible);
                double opacityAt = Top("OpacityLabel"), opacityRowAt = Top("OpacityRow");
                var cutWithBrush = UiTabLanguageFitTests.CutWords(tools).ToList();

                ((System.Windows.Controls.Primitives.ToggleButton)studio.FindName("ToolSoftenBtn"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                WindowHarness.Pump();
                window.UpdateLayout();
                _out.WriteLine($"{language}: Opacity at {opacityAt:0.#}/{opacityRowAt:0.#}, Edge softness at {Top("SoftnessLabel"):0.#}/{Top("SoftnessRow"):0.#}");
                Assert.True(Named("SoftnessLabel").IsVisible && Named("SoftnessRow").IsVisible);
                Assert.False(Named("OpacityLabel").IsVisible || Named("OpacityRow").IsVisible);
                Assert.Equal(opacityAt, Top("SoftnessLabel"), 1);
                Assert.Equal(opacityRowAt, Top("SoftnessRow"), 1);
                Assert.False(((CheckBox)studio.FindName("CapToggle")).IsEnabled);
                var cut = cutWithBrush.Concat(UiTabLanguageFitTests.CutWords(tools)).ToList();
                Assert.True(cut.Count == 0, language + ":\n" + string.Join("\n", cut));

                // The tools panel as drawn, for a person to look at when asked for.
                string? shots = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
                if (!string.IsNullOrWhiteSpace(shots) && window.Content is FrameworkElement root)
                {
                    // The whole window, cut to the panel: a nested element rendered
                    // on its own lands at its offset in the window, off the picture.
                    Directory.CreateDirectory(shots);
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(root);
                    var at = tools.TransformToAncestor(root).TransformBounds(new Rect(0, 0, tools.ActualWidth, tools.ActualHeight));
                    var panel = new System.Windows.Media.Imaging.CroppedBitmap(bmp,
                        new Int32Rect((int)at.X, (int)at.Y, (int)Math.Ceiling(at.Width), (int)Math.Ceiling(at.Height)));
                    ThemeSnapshotTests.Save(panel, Path.Combine(shots, $"mask-soften-tools-{language}-" +
                        (Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light") + ".png"));
                }

                // The control: back on the brush.
                ((System.Windows.Controls.Primitives.ToggleButton)studio.FindName("ToolBrushBtn"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                WindowHarness.Pump();
                window.UpdateLayout();
                Assert.True(Named("OpacityRow").IsVisible);
                Assert.False(Named("SoftnessRow").IsVisible);
                Assert.True(((CheckBox)studio.FindName("CapToggle")).IsEnabled);

                studio.AnswerUnsaved = () => MessageBoxResult.No;
                studio.TryClose();
                WindowHarness.Pump();
            });
        }
        finally { SMSModForge.Localization.Loc.Use(SMSModForge.Localization.Loc.EnglishCode); }
    }

    [Fact]
    public void OneLayerCanBeCopiedFromAnotherOutfit_TheOthersStay()
    {
        // The author, 1.9.0: "copy only a single channel of mask from another
        // outfit". Measured on the layers' bytes; the control is the copy of
        // every layer, which does change the others.
        WindowHarness.Run(window =>
        {
            var vm = APack(window, "Characters");
            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First(o => o.Key == "casual");
            WindowHarness.Pump();
            Click(window, "EditMaskButton");
            var studio = ((MainWindow)window).CurrentMaskStudio!;
            Assert.Equal(Visibility.Visible, ((ComboBox)studio.FindName("CopyLayerList")).Visibility);

            int Max(int layer) => studio.LayerBytes(layer).Max();
            int Min(int layer) => studio.LayerBytes(layer).Min();
            Assert.Equal(0, Max(0) + Max(1) + Max(2));

            studio.ChooseCopySource(0);
            studio.CopyLayer = 1;                          // Wave only
            studio.CopyChosen();
            WindowHarness.Pump();
            _out.WriteLine($"Wave only: {Max(0)}/{Min(1)}-{Max(1)}/{Max(2)}");
            Assert.Equal(0, Max(0));
            Assert.Equal(100, Min(1));
            Assert.Equal(100, Max(1));
            Assert.Equal(0, Max(2));
            Assert.Equal(1, studio.ActiveLayer);            // the layer copied is painted next
            Assert.True(studio.IsDirty);

            // One Ctrl+Z puts that layer back.
            ApplicationCommands.Undo.Execute(null, studio);
            WindowHarness.Pump();
            Assert.Equal(0, Max(1));

            // The control: every layer copies all three.
            studio.CopyLayer = -1;
            studio.CopyChosen();
            WindowHarness.Pump();
            _out.WriteLine($"every layer: {Max(0)}/{Max(1)}/{Max(2)}");
            Assert.Equal(200, Max(0));
            Assert.Equal(100, Max(1));
            Assert.Equal(50, Max(2));

            studio.AnswerUnsaved = () => MessageBoxResult.No;
            Assert.True(studio.TryClose());
        });
    }

    [Fact]
    public void SavingWritesTheMaskBesideTheArt_AndTheOutfitPointsAtIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = APack(window, "Characters");
            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
            WindowHarness.Pump();
            Click(window, "EditMaskButton");
            var studio = ((MainWindow)window).CurrentMaskStudio!;
            studio.BeginStrokeAt(100, 100);
            studio.EndStroke();

            string target = Path.Combine(_dir, "Busts", "annaMask.png");
            studio.AskSavePath = () => target;
            Assert.True(studio.Save());
            Assert.True(File.Exists(target));
            Assert.Equal("Busts/annaMask.png", vm.SelectedOutfit!.Model.MaskSprite);
            Assert.False(studio.IsDirty);
            var saved = BustComposer.LoadPng(target);
            _out.WriteLine($"saved: red at the stroke {saved[(100 * 256 + 100) * 4 + 2]}, away {saved[(10 * 256 + 10) * 4 + 2]}");
            Assert.True(saved[(100 * 256 + 100) * 4 + 2] > 0);
            Assert.Equal(0, saved[(10 * 256 + 10) * 4 + 2]);
            Assert.True(studio.TryClose());
        });
    }

    [Fact]
    public void APlacesMask_ItsTabsShadersStop_AndTheStudiosRun()
    {
        WindowHarness.Run(window =>
        {
            var vm = APack(window, "Places");
            vm.SelectedPlace = vm.Places.First();
            WindowHarness.Pump();
            ((CheckBox)window.FindName("PlaceShaders")).IsChecked = true;
            WindowHarness.Pump();
            window.UpdateLayout();
            var tabPreview = (PlacePreview)window.FindName("PlacePreviewMain");
            WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
            Assert.True(tabPreview.ShaderClockRunning, "the tab's shaders are not running (and so this proves nothing)");

            Click(window, "EditPlaceMaskButton");
            var studio = ((MainWindow)window).CurrentMaskStudio!;
            Assert.NotNull(studio);
            WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
            var studioPreview = Descendants<PlacePreview>(studio).Single();
            _out.WriteLine($"tab's shader clock {tabPreview.ShaderClockRunning}, studio's {studioPreview.ShaderClockRunning}");
            Assert.False(tabPreview.ShaderClockRunning);
            Assert.True(studioPreview.ShaderClockRunning);
            Assert.Same(vm.SelectedPlace, studioPreview.DataContext);

            Assert.True(studio.TryClose());
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
            Assert.True(tabPreview.ShaderClockRunning, "the tab's shaders did not start again");
            Assert.False(studioPreview.ShaderClockRunning);
        });
    }

    [Fact]
    public void TheQualityButtonsChangeEveryPreviewsQuality_AndEveryBarShowsIt()
    {
        WindowHarness.Run(window =>
        {
            var was = PreviewQualityManager.Current;
            try
            {
                APack(window, "Characters");
                var bars = new[] { "BustQualityBar", "NpcQualityBar", "PlaceQualityBar", "ExtQualityBar" }
                    .Select(n => (PreviewQualityBar)window.FindName(n)).ToList();
                var bust = bars[0];
                bust.UpdateLayout();
                foreach (string name in new[] { "Ultra", "Low" })
                {
                    var button = bust.Children.OfType<Button>().Single(b => (string)b.Tag == name);
                    button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    WindowHarness.Pump();
                    _out.WriteLine($"pressed {name}: current {PreviewQualityManager.Current.Name}, bars mark {string.Join(", ", bars.Select(b => b.Marked()))}");
                    Assert.Equal(name, PreviewQualityManager.Current.Name);
                    Assert.All(bars, b => Assert.Equal(name, b.Marked()));
                }
            }
            finally { PreviewQualityManager.Apply(was); }
        });
    }

    /// <summary>
    /// The mask painter's bust preview draws over the backdrop as the tab's does
    /// (1.9.1, the author: "we still want the changes here to affect the mask
    /// editor previews"): with the player's room chosen, its top row - above the
    /// bust, which the game's screen cuts 11 pixels under its bottom - is the
    /// room's own pixels; with none chosen (the control), the whole bust shows
    /// and that row is the art's own colour.
    /// </summary>
    [Fact]
    public void ThePaintersPreviewDrawsOverTheBackdrop_AsTheTabsDoes()
    {
        var room = PreviewBackdropManager.All.First(d => d.Name == "MyRoom");
        var scene = room.SceneWaited();
        Assert.NotNull(scene);
        byte[] TopRow(JigglePreview p)
        {
            var px = new byte[JiggleShader.RenderStride];
            ((System.Windows.Media.Imaging.WriteableBitmap)p.Source).CopyPixels(new Int32Rect(0, 0, JiggleShader.RenderSize, 1), px, JiggleShader.RenderStride, 0);
            return px;
        }
        BustFadeInTests.WithBackdrop("MyRoom", () => WindowHarness.Run(window =>
        {
            var vm = APack(window, "Characters");
            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
            WindowHarness.Pump();
            // The tab's own preview, still (no breathing), past the fade-in: what
            // the room behind the bust looks like there - with the bust's shadow
            // in it since 1.9.1, so not the bare room.
            var tab = (JigglePreview)window.FindName("PreviewControl");
            tab.Breathing = false;
            tab.IntroNow = () => 100;
            // Its first picture, however long that takes: on the graphics card the bust's program is compiled the
            // first time (7 s, measured, with the empty cache every test process starts with).
            Assert.True(WindowHarness.Until(() => tab.FramesShown > 0, TimeSpan.FromSeconds(60)), "the tab's preview never drew (" + tab.LastFault + ")");
            WindowHarness.Wait(TimeSpan.FromMilliseconds(600));
            Assert.True(tab.OverBackdrop, "the tab's preview does not draw over the backdrop");
            var tabTop = TopRow(tab);
            Click(window, "EditMaskButton");
            var studio = ((MainWindow)window).CurrentMaskStudio!;
            var preview = Descendants<JigglePreview>(studio).Single();
            preview.Breathing = false;
            preview.IntroNow = () => 100;                     // past the fade-in
            Assert.True(WindowHarness.Until(() => preview.FramesShown > 0, TimeSpan.FromSeconds(60)), "the painter's preview never drew (" + preview.LastFault + ")");
            WindowHarness.Wait(TimeSpan.FromMilliseconds(600));
            Assert.True(preview.OverBackdrop, "the painter's preview does not draw over the backdrop");
            var top = TopRow(preview);
            int same = 0, room = 0;
            for (int x = 0; x < JiggleShader.RenderSize; x++)
            {
                int i = x * 4;
                if (top[i] == tabTop[i] && top[i + 1] == tabTop[i + 1] && top[i + 2] == tabTop[i + 2] && top[i + 3] == 255) same++;
                if (top[i] != 0x60 || top[i + 1] != 0x80 || top[i + 2] != 0xC0) room++;
            }
            _out.WriteLine($"the painter's preview, top row: {same} of {JiggleShader.RenderSize} pixels as the tab's, {room} not the art's own colour");
            Assert.Equal(JiggleShader.RenderSize, same);
            Assert.Equal(JiggleShader.RenderSize, room);

            // The control: no backdrop, the bust alone - its art opaque to the top.
            PreviewBackdropManager.Apply(PreviewBackdropManager.All.First(d => d.IsNone));
            WindowHarness.Wait(TimeSpan.FromMilliseconds(600));
            Assert.False(preview.OverBackdrop);
            var bare = TopRow(preview);
            Assert.True(Enumerable.Range(0, JiggleShader.RenderSize).All(x => bare[x * 4] == 0x60 && bare[x * 4 + 1] == 0x80 && bare[x * 4 + 2] == 0xC0),
                        "with no backdrop the top row is not the art's own colour");
            studio.AnswerUnsaved = () => MessageBoxResult.No;
            studio.TryClose();
            WindowHarness.Pump();
        }));
    }
}
