using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Documentation;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A guide picture opens at full size to zoom into (1.10.0, the author: "add the possibility to click on them to open
/// full size with zoom in/out"), measured from what is drawn: clicked, the picture is over the window with its
/// topic's title, all of it in view and no bigger than its own size; 1 shows it pixel for pixel, the toolbar's zoom
/// makes it bigger, and Esc puts the guide back with the keyboard on the picture again. Run in each look by the
/// runner (-WithEx): EX draws the viewer its own way with the same parts.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PictureViewerTests
{
    private readonly ITestOutputHelper _out;
    public PictureViewerTests(ITestOutputHelper o) => _out = o;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    [Fact]
    public void AGuidePictureOpensAtFullSizeToZoomInto_AndEscPutsTheGuideBack()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            RealKeys.Watch(window);   // so a press says whether the key itself was taken
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.ModForge;
            var entry = vm.GuideEntries.First(e => GuidePictures.Load(e.Topic.PictureName) != null);
            vm.OpenGuideTopicCommand.Execute(entry);
            WindowHarness.Pump();
            window.UpdateLayout();

            var button = Descendants<Button>(window).First(b => b.Name == "GuideTopicPicture" && b.IsVisible);
            var picture = ((Image)button.Content).Source;
            var viewer = (PictureViewer)window.FindName("PictureViewer");
            Assert.False(viewer.IsVisible);

            button.Focus();
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowHarness.Pump();
            Assert.True(viewer.IsOpen && viewer.IsVisible, "the picture did not open");
            Assert.Same(picture, viewer.Source);
            Assert.Equal(entry.Topic.Title, viewer.Caption);

            var stage = (FrameworkElement)viewer.Template.FindName("PART_Stage", viewer);
            var image = (Image)viewer.Template.FindName("PART_Image", viewer);
            Rect Drawn() => image.TransformToAncestor(stage).TransformBounds(new Rect(image.RenderSize));

            // A picture of it, for a person to look at (SMSMODFORGE_VIEWER_SHOT: a folder).
            if (Environment.GetEnvironmentVariable("SMSMODFORGE_VIEWER_SHOT") is { Length: > 0 } shots)
            {
                window.UpdateLayout();
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(window);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var file = System.IO.File.Create(System.IO.Path.Combine(shots, "viewer-" + Services.ThemeManager.Current.Name + ".png"));
                png.Save(file);
            }

            // It opens fitting: all of it in view, as big as fits, never bigger than its own size.
            var fit = Drawn();
            _out.WriteLine($"{Services.ThemeManager.Current.Name}: picture {picture.Width:0}x{picture.Height:0}, stage {stage.ActualWidth:0}x{stage.ActualHeight:0}, drawn {fit}");
            Assert.True(fit.Left >= -0.5 && fit.Top >= -0.5 && fit.Right <= stage.ActualWidth + 0.5 && fit.Bottom <= stage.ActualHeight + 0.5,
                $"the picture opened partly out of view: {fit}");
            Assert.True(fit.Width <= picture.Width + 0.5, "it opened bigger than its own size");
            Assert.True(Math.Abs(fit.Width - stage.ActualWidth) < 1 || Math.Abs(fit.Height - stage.ActualHeight) < 1 || Math.Abs(fit.Width - picture.Width) < 1,
                "it opened smaller than fits");

            // 1: pixel for pixel.
            Assert.True(RealKeys.Press(viewer, Key.D1), "1 went nowhere");
            WindowHarness.Pump();
            var own = Drawn();
            Assert.Equal(picture.Width, own.Width, 0);
            Assert.Equal("100 %", viewer.ZoomText);

            // The toolbar's zoom makes it bigger.
            var zoomIn = Descendants<Button>(viewer).Single(b => ReferenceEquals(b.Command, PictureViewer.ZoomIn));
            ((RoutedCommand)zoomIn.Command).Execute(null, zoomIn);
            WindowHarness.Pump();
            var bigger = Drawn();
            Assert.True(bigger.Width > own.Width * 1.2, $"zooming in drew it at {bigger.Width:0} wide, from {own.Width:0}");
            Assert.Equal("125 %", viewer.ZoomText);
            // 0 fits it again.
            RealKeys.Press(viewer, Key.D0);
            WindowHarness.Pump();
            Assert.Equal(fit.Width, Drawn().Width, 0);

            // Esc: the guide again, the keyboard back on the picture.
            Assert.True(RealKeys.Press(viewer, Key.Escape), "Esc went nowhere");
            WindowHarness.Pump();
            Assert.False(viewer.IsOpen);
            Assert.False(viewer.IsVisible);
            Assert.True(button.IsKeyboardFocused, "the keyboard did not come back to the picture");
        });
    }
}
