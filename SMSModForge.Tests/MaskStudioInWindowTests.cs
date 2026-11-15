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
}
