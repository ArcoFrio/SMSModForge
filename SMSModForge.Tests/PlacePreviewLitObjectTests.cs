using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Tutorials;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// An object of the pack's own in the place preview with the game's lights on (Game look ▸ Lights, the default).
/// The look took the world layer right after a rebuild, before the pictures the rebuild had just put there were
/// laid out, so it took them as nothing - and, the scene's list being the same on the next tick, kept that picture
/// until something forced the look to start again: an object was missing after any change to it, and dragging it
/// made it vanish (found by the pictures of every tutorial step: the golden vase never showed). Measured on what is
/// drawn: the preview with the vase given its picture and moved, against the same preview with the vase's picture
/// taken away - the control, which the bug made identical to the others.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlacePreviewLitObjectTests
{
    private readonly ITestOutputHelper _out;
    public PlacePreviewLitObjectTests(ITestOutputHelper o) => _out = o;

    private static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in All<T>(c)) yield return d;
        }
    }

    private static byte[] Shot(FrameworkElement e)
    {
        WindowHarness.Wait(TimeSpan.FromMilliseconds(1500));   // the look works a picture out off the screen's thread
        e.UpdateLayout();
        WindowHarness.Pump();
        var bmp = new RenderTargetBitmap((int)e.ActualWidth, (int)e.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(e);
        var px = new byte[(int)e.ActualWidth * (int)e.ActualHeight * 4];
        bmp.CopyPixels(px, (int)e.ActualWidth * 4, 0);
        return px;
    }

    /// <summary>How many pixels differ clearly between two pictures of the same size.</summary>
    private static int Differ(byte[] a, byte[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i += 4)
            if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 60) n++;
        return n;
    }

    [Fact]
    public void An_object_of_the_packs_own_shows_in_the_lit_preview_as_soon_as_it_changes()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            using var walker = new TutorialWalker(vm);
            walker.BringInStory(TutorialCatalog.All.First(t => t.Id == "a-spotlight"));
            var gallery = StoryPack.Gallery(vm)!;
            vm.SelectedPlace = gallery;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            var vase = gallery.GameObjects.First(o => o.Name == StoryPack.VaseName);
            WindowHarness.Wait(TimeSpan.FromSeconds(1));
            var preview = All<PlacePreview>(window).First(p => p.IsVisible);
            Assert.True(preview.LookLights, "the lights are not on: the case is not the default one");

            // The control: no picture, nothing drawn for the vase.
            vase.StartActive = true;
            string picture = vase.Sprite;
            vase.Sprite = "";
            var none = Shot(preview);

            vase.Sprite = picture;
            var shown = Shot(preview);
            vase.X = -3f;
            var moved = Shot(preview);

            int area = none.Length / 4;
            int shownDiff = Differ(none, shown), movedDiff = Differ(shown, moved), stillDiff = Differ(none, moved);
            _out.WriteLine($"of {area} pixels: given its picture {shownDiff} differ, moved {movedDiff}, moved against none {stillDiff}");
            Assert.True(shownDiff > area / 50, "the vase given its picture is not drawn in the lit preview");
            Assert.True(movedDiff > area / 100, "moving the vase changed nothing in the lit preview");
            Assert.True(stillDiff > area / 50, "the vase is gone from the lit preview after it moved");
        });
    }
}
