using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The previews (the author, 1.8.0): made bigger by dragging their corner, in
/// proportion, never larger than the room, drawn crisp when enlarged; and the
/// place preview's hierarchy and selection beside the picture rather than over
/// it, as tall as it, given way only when the picture is made wider than the room.
/// </summary>
[Trait("Speed", "Slow")]   // builds windows on the harness thread
public sealed class PreviewSizeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-previews-" + Guid.NewGuid().ToString("N"));

    public PreviewSizeTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void AFramedPreviewGrowsInProportion_NoLargerThanTheRoom_AndCrisp()
    {
        WindowHarness.Run(_ =>
        {
            var picture = new Border { Width = 100, Height = 50, Background = Brushes.Gray };
            var frame = new PreviewFrame { Preview = picture };
            var room = new Grid { Width = 300 };
            room.Children.Add(frame);
            var window = new Window { Width = 500, Height = 400, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = room };
            window.Show();

            frame.Scale = 2;
            window.UpdateLayout();
            Assert.Equal(200, frame.ActualWidth, 1);
            Assert.Equal(100, frame.ActualHeight, 1);   // in proportion
            Assert.Equal(BitmapScalingMode.NearestNeighbor, RenderOptions.GetBitmapScalingMode(frame));

            // Wider than the room: shown as large as fits, the size chosen kept.
            frame.Scale = 5;
            window.UpdateLayout();
            Assert.Equal(300, frame.ActualWidth, 1);
            Assert.Equal(4, frame.Scale);   // the most it goes to

            // Made smaller than life: smoothed.
            frame.Scale = 0.5;
            window.UpdateLayout();
            Assert.Equal(50, frame.ActualWidth, 1);
            Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(frame));
            window.Close();
        });
    }

    private static T Field<T>(object owner, string name)
        => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private void WriteCave()
    {
        var pack = PackRepository.CreateEmpty("previews.pack");
        var cave = new PlaceDef { Key = "cave", DisplayName = "Cave", BaseSprite = "Art/cave.png" };
        var lantern = new GameObjectDef { Name = "Lantern on the long wall of the cave by the old well" };
        lantern.Children.Add(new GameObjectDef { Name = "Glow" });
        cave.GameObjects.Add(lantern);
        pack.Places.Add(cave);
        PackRepository.Save(pack, _dir);
        var px = Enumerable.Repeat((byte)0x80, 192 * 108 * 4).ToArray();
        var image = BitmapSource.Create(192, 108, 96, 96, PixelFormats.Bgra32, null, px, 192 * 4);
        Directory.CreateDirectory(Path.Combine(_dir, "Art"));
        using var file = File.Create(Path.Combine(_dir, "Art", "cave.png"));
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        png.Save(file);
    }

    [Fact]
    public void ThePlacesListsAreBesideThePicture_AsTallAsIt_AndGiveWayOnlyToALargerPicture()
    {
        WindowHarness.Run(window =>
        {
            WriteCave();
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = MainTab.Places;
            vm.SelectedPlace = vm.Places.Single();
            WindowHarness.Pump();
            window.UpdateLayout();

            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            var menu = Field<Border>(preview, "_objectMenu");
            var picture = Field<Border>(preview, "_viewport");
            var selection = Field<Border>(preview, "_gizmoToolbar");
            Assert.True(menu.IsVisible && picture.IsVisible && selection.IsVisible, "the panels are not beside the picture");

            double Left(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), preview).X;
            Assert.True(Left(menu) + menu.ActualWidth <= Left(picture) + 0.5, "the hierarchy covers the picture");
            Assert.True(Left(picture) + picture.ActualWidth <= Left(selection) + 0.5, "the selection covers the picture");
            Assert.Equal(picture.ActualHeight, menu.ActualHeight, 1);
            Assert.Equal(picture.ActualHeight, selection.ActualHeight, 1);

            // Not dragged: the hierarchy as wide as its longest name.
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double full = menu.DesiredSize.Width - menu.Margin.Left - menu.Margin.Right;
            preview.InvalidateMeasure();
            window.UpdateLayout();
            Assert.True(menu.ActualWidth >= full - 1, $"the hierarchy is cut short: {menu.ActualWidth:0} of {full:0}");
            double fitted = picture.ActualWidth;

            // Dragged past the room: the picture grows, the lists give way, not below their least.
            typeof(PlacePreview).GetField("_pictureScale", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(preview, 3.0);
            preview.InvalidateMeasure();
            window.UpdateLayout();
            Assert.True(picture.ActualWidth > fitted + 10, "the picture did not grow");
            Assert.True(menu.ActualWidth < full - 1, "the hierarchy did not give way to the larger picture");
            Assert.True(menu.ActualWidth >= 89, $"the hierarchy went below its least: {menu.ActualWidth:0}");
            Assert.Equal(picture.ActualHeight, menu.ActualHeight, 1);
        });
    }
}
