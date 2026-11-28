using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The place preview over the whole screen (after 1.10.0, the author: "can we also add a fullscreen button for the
/// Places preview?"), in both looks - Classic and EX share the window (see EX look preferences). Its button, or F11,
/// puts the preview - picture, hierarchy, the picked object's settings - over the window, the window over its
/// monitor, the picture as large as the room both ways; there it is the same preview on the same place (another
/// place picked meanwhile shows), the tick boxes beside it on the page still drive it, and the keys work (Delete
/// then Ctrl+Z on a picked object). Esc puts it back where it was on the page, the window as it was, the picture its
/// size again; so does another tab. Measured on the window as drawn.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlacePreviewFullScreenTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-fullscreen-" + Guid.NewGuid().ToString("N"));

    public PlacePreviewFullScreenTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Locations"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static void Png(string path, int w, int h, Color c)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        for (var at = LogicalTreeHelper.GetParent(d); at != null; at = LogicalTreeHelper.GetParent(at))
            if (at is T t) return t;
        return null;
    }

    private static bool IsInside(DependencyObject d, DependencyObject container)
    {
        for (var at = d; at != null; at = LogicalTreeHelper.GetParent(at))
            if (ReferenceEquals(at, container)) return true;
        return false;
    }

    private static T? Find<T>(DependencyObject root, Func<T, bool> which) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && which(t)) return t;
            if (Find(child, which) is { } found) return found;
        }
        return null;
    }

    [Fact]
    public void The_place_preview_goes_over_the_whole_screen_and_comes_back_as_it_was_in_both_looks()
    {
        Png(Path.Combine(_dir, "Locations", "Room.png"), 64, 36, Colors.SaddleBrown);
        Png(Path.Combine(_dir, "Locations", "Hall.png"), 64, 36, Colors.SteelBlue);
        var pack = PackRepository.CreateEmpty("fullscreen");
        var room = new PlaceDef { Key = "room", InternalName = "Room", BaseSprite = "Locations/Room.png" };
        room.GameObjects.Add(new GameObjectDef { Name = "Lamp", X = 3 });
        pack.Places.Add(room);
        pack.Places.Add(new PlaceDef { Key = "hall", InternalName = "Hall", BaseSprite = "Locations/Hall.png" });
        PackRepository.Save(pack, _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1500;
            window.Height = 900;
            var vm = (MainViewModel)window.DataContext;
            var before = ThemeManager.Current;
            var main = (MainWindow)window;
            try
            {
                vm.OpenPackFromPath(_dir);
                Settle(window);
                var tabs = (TabControl)window.FindName("MainTabs");
                tabs.SelectedIndex = MainTab.Places;
                Settle(window);
                RealKeys.Watch(window);
                var preview = (PlacePreview)window.FindName("PlacePreviewMain");
                var spot = (Panel)window.FindName("FullScreenSpot");
                var dimSelf = (CheckBox)window.FindName("PlaceDimSelf");
                foreach (var look in new[] { "Light", "Frost" })
                {
                    ThemeManager.Apply(ThemeManager.All.First(t => t.Name == look));
                    var first = vm.Places.First(p => p.Key == "room");
                    var second = vm.Places.First(p => p.Key == "hall");
                    vm.SelectedPlace = first;
                    Settle(window);
                    var from = (Panel)preview.Parent;
                    int at = from.Children.IndexOf(preview);
                    double pictureBefore = preview.ColumnDefinitions[1].ActualWidth;
                    string sizesBefore = $"window {window.ActualWidth:0}, hierarchy {preview.ColumnDefinitions[0].ActualWidth:0}, settings {preview.ColumnDefinitions[2].ActualWidth:0}, preview {preview.ActualWidth:0}";
                    var (pageTl, pageBr) = preview.ViewDrawnAt;
                    Assert.True(Math.Abs(pageTl.X) < 1 && Math.Abs(pageTl.Y) < 1 && Math.Abs(pageBr.X - preview.PictureSize.Width) < 1,
                                $"{look}: on the page the player's view is drawn from ({pageTl.X:0.#}, {pageTl.Y:0.#}) to ({pageBr.X:0.#}, {pageBr.Y:0.#})");
                    var (styleWas, stateWas) = (window.WindowStyle, window.WindowState);

                    // ── Over the whole screen, by its button ──
                    // Beside the preview's heading (the author: "the best place for the full screen button would be right
                    // next to the Preview header"), and the page's tick boxes and quality where they were.
                    var button = (Button)window.FindName("PlaceFullScreenButton");
                    var group = FindAncestor<GroupBox>(preview);
                    Assert.True(group != null && group.Header is DependencyObject header && IsInside(button, header), look + ": the Full screen button is not in the preview's heading");
                    var rows = new FrameworkElement[] { (FrameworkElement)window.FindName("PlaceOptionsRow"), (FrameworkElement)window.FindName("PlaceLookRow"), (FrameworkElement)window.FindName("PlaceQualityBar") };
                    var rowsAt = rows.Select(r => (Parent: LogicalTreeHelper.GetParent(r), At: ((Panel)LogicalTreeHelper.GetParent(r)).Children.IndexOf(r))).ToArray();
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Settle(window);
                    Assert.Same(preview, main.FullScreenPreview);
                    Assert.Same(spot, preview.Parent);
                    Assert.Equal(WindowStyle.None, window.WindowStyle);
                    Assert.Equal(WindowState.Maximized, window.WindowState);
                    double picture = preview.ColumnDefinitions[1].ActualWidth, tall = preview.ActualHeight;
                    double lists = preview.ColumnDefinitions[0].ActualWidth + preview.ColumnDefinitions[2].ActualWidth;
                    var topLeft = preview.TranslatePoint(new Point(0, 0), window);
                    _out.WriteLine($"{look}: the picture {pictureBefore:0} wide on the page; over the screen {picture:0} x {tall:0}, the lists {lists:0}, "
                                   + $"in a room of {spot.ActualWidth:0} x {spot.ActualHeight:0}, the window {window.ActualWidth:0} x {window.ActualHeight:0}, at ({topLeft.X:0}, {topLeft.Y:0})");
                    // The window as drawn, to look at (SMSMODFORGE_SHOT_DIR).
                    if (Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR") is { Length: > 0 } shots)
                    {
                        Directory.CreateDirectory(shots);
                        var shot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        shot.Render(window);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(shot));
                        using var fs = File.Create(Path.Combine(shots, "place-fullscreen-" + look + ".png"));
                        enc.Save(fs);
                    }
                    Assert.True(picture > pictureBefore * 1.1, $"{look}: over the screen the picture is {picture:0} wide, on the page {pictureBefore:0}");
                    // What the player sees fills the picture, corner to corner, as at the view's fit on the page.
                    var (viewTl, viewBr) = preview.ViewDrawnAt;
                    var size = preview.PictureSize;
                    _out.WriteLine($"{look}: the player's view drawn from ({viewTl.X:0.#}, {viewTl.Y:0.#}) to ({viewBr.X:0.#}, {viewBr.Y:0.#}) in a picture {size.Width:0.#} x {size.Height:0.#}");
                    Assert.True(Math.Abs(viewTl.X) < 1 && Math.Abs(viewTl.Y) < 1 && Math.Abs(viewBr.X - size.Width) < 1 && Math.Abs(viewBr.Y - size.Height) < 1,
                                $"{look}: the player's view is drawn from ({viewTl.X:0.#}, {viewTl.Y:0.#}) to ({viewBr.X:0.#}, {viewBr.Y:0.#}), the picture is {size.Width:0.#} x {size.Height:0.#}");
                    // The picture over the whole screen: its width or its height the window's, neither more (the author: "the
                    // canvas should span the whole screen").
                    double spareW = spot.ActualWidth - picture, spareH = spot.ActualHeight - tall;
                    Assert.True(spot.ActualWidth >= window.ActualWidth - 1 && spot.ActualHeight >= window.ActualHeight - 1,
                                $"{look}: the room over the screen is {spot.ActualWidth:0} x {spot.ActualHeight:0}, the window {window.ActualWidth:0} x {window.ActualHeight:0}");
                    Assert.True(spareW > -1 && spareH > -1, $"{look}: the picture ({picture:0} x {tall:0}) is bigger than the screen");
                    Assert.True(Math.Min(spareW, spareH) < 2, $"{look}: the picture leaves {spareW:0} across and {spareH:0} down of the screen unused");
                    // ...and the hierarchy and the settings over its sides ("while the left and right menus overlay it").
                    var (menuAt, settingsAt, pictureAt) = preview.PanelsDrawnAt;
                    _out.WriteLine($"{look}: the hierarchy drawn at {menuAt}, the settings at {settingsAt}, the picture at {pictureAt}");
                    Assert.True(lists < 1, $"{look}: the panels still take {lists:0} beside the picture");
                    Assert.True(menuAt.Width > 50 && menuAt.Left >= pictureAt.Left - 1 && menuAt.Left < pictureAt.Left + 20 && menuAt.Bottom <= pictureAt.Bottom + 1,
                                $"{look}: the hierarchy is not over the picture's left side");
                    Assert.True(settingsAt.Width > 50 && settingsAt.Right <= pictureAt.Right + 1 && settingsAt.Right > pictureAt.Right - 20 && settingsAt.Bottom <= pictureAt.Bottom + 1,
                                $"{look}: the settings are not over the picture's right side");
                    Assert.True(topLeft.X >= 0 && topLeft.Y >= 0 && topLeft.X + preview.ActualWidth <= window.ActualWidth + 1
                                && topLeft.Y + tall <= window.ActualHeight + 1, $"{look}: the preview is drawn off the window");
                    // ...and the page's tick boxes and quality under the hierarchy, as wide as it ("in a dedicated space
                    // right below the hierarchy").
                    var extrasAt = preview.ExtrasDrawnAt;
                    _out.WriteLine($"{look}: the tick boxes drawn at {extrasAt}");
                    Assert.All(rows, r => Assert.True(preview.IsAncestorOf(r), $"{look}: {r.Name} is not in the preview over the screen"));
                    Assert.True(!extrasAt.IsEmpty && extrasAt.Top >= menuAt.Bottom - 1 && extrasAt.Top <= menuAt.Bottom + 20,
                                $"{look}: the tick boxes ({extrasAt}) are not right below the hierarchy ({menuAt})");
                    Assert.True(Math.Abs(extrasAt.Left - menuAt.Left) < 1 && Math.Abs(extrasAt.Width - menuAt.Width) < 1,
                                $"{look}: the tick boxes ({extrasAt}) are not as wide as the hierarchy ({menuAt}), under it");
                    Assert.True(extrasAt.Bottom <= pictureAt.Bottom + 1, $"{look}: the tick boxes are drawn below the picture");
                    foreach (var r in rows)
                    {
                        var rAt = r.TransformToAncestor(preview).TransformBounds(new Rect(0, 0, r.ActualWidth, r.ActualHeight));
                        Assert.True(rAt.Right <= extrasAt.Right + 1, $"{look}: {r.Name} sticks out of its panel ({rAt} in {extrasAt})");
                    }

                    // The same preview, on the place picked - another one picked meanwhile too.
                    Assert.Same(first, preview.DataContext);
                    vm.SelectedPlace = second;
                    Settle(window);
                    Assert.Same(second, preview.DataContext);
                    Assert.Equal("Locations/Hall.png", preview.BaseSprite);
                    vm.SelectedPlace = first;
                    Settle(window);
                    Assert.Equal("Locations/Room.png", preview.BaseSprite);
                    // A tick box on the page still drives it.
                    bool dim = preview.DimInactive;
                    dimSelf.IsChecked = !dimSelf.IsChecked;
                    Settle(window);
                    Assert.NotEqual(dim, preview.DimInactive);
                    dimSelf.IsChecked = !dimSelf.IsChecked;
                    Settle(window);
                    // Its keys: the lamp deleted, then back with Ctrl+Z.
                    var lamp = first.GameObjects.Single(g => g.Name == "Lamp");
                    Assert.True(preview.Pick(lamp), look + ": the lamp could not be picked");
                    preview.Focus();
                    Settle(window);
                    RealKeys.Press(window, Key.Delete);
                    Settle(window);
                    Assert.DoesNotContain(first.GameObjects, g => g.Name == "Lamp");
                    RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                    Settle(window);
                    Assert.Contains(first.GameObjects, g => g.Name == "Lamp");
                    Assert.Same(preview, main.FullScreenPreview);

                    // ── Back, by Esc ──
                    preview.Focus();
                    RealKeys.Press(window, Key.Escape);
                    Settle(window);
                    Assert.Null(main.FullScreenPreview);
                    Assert.Same(from, preview.Parent);
                    Assert.Equal(at, from.Children.IndexOf(preview));
                    Assert.Equal(styleWas, window.WindowStyle);
                    Assert.Equal(stateWas, window.WindowState);
                    Assert.Equal(DependencyProperty.UnsetValue, preview.ReadLocalValue(FrameworkElement.DataContextProperty));
                    Assert.Same(first, preview.DataContext);
                    Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("FullScreenHost")).Visibility);
                    double pictureAfter = preview.ColumnDefinitions[1].ActualWidth;
                    _out.WriteLine($"{look}: before {sizesBefore}; after window {window.ActualWidth:0}, hierarchy {preview.ColumnDefinitions[0].ActualWidth:0}, settings {preview.ColumnDefinitions[2].ActualWidth:0}, preview {preview.ActualWidth:0}");
                    Assert.True(Math.Abs(pictureAfter - pictureBefore) < 1, $"{look}: back on the page the picture is {pictureAfter:0} wide, before {pictureBefore:0}");
                    // ...the tick boxes and quality where they were...
                    for (int i = 0; i < rows.Length; i++)
                    {
                        Assert.Same(rowsAt[i].Parent, LogicalTreeHelper.GetParent(rows[i]));
                        Assert.Equal(rowsAt[i].At, ((Panel)rowsAt[i].Parent).Children.IndexOf(rows[i]));
                    }
                    Assert.True(preview.ExtrasDrawnAt.IsEmpty, $"{look}: the panel under the hierarchy is still drawn on the page");
                    // ...and the panels beside it again.
                    var (menuBack, settingsBack, pictureBack) = preview.PanelsDrawnAt;
                    Assert.True(menuBack.Right <= pictureBack.Left + 1 && settingsBack.Left >= pictureBack.Right - 1,
                                $"{look}: back on the page the panels ({menuBack}, {settingsBack}) are not beside the picture ({pictureBack})");

                    // ── F11 there, another tab back ──
                    preview.Focus();
                    RealKeys.Press(window, Key.F11);
                    Settle(window);
                    Assert.Same(preview, main.FullScreenPreview);
                    tabs.SelectedIndex = MainTab.Npcs;
                    Settle(window);
                    Assert.Null(main.FullScreenPreview);
                    Assert.Same(from, preview.Parent);
                    Assert.Equal(styleWas, window.WindowStyle);
                    tabs.SelectedIndex = MainTab.Places;
                    Settle(window);
                    // Control: on another tab F11 does nothing.
                    tabs.SelectedIndex = MainTab.Npcs;
                    Settle(window);
                    RealKeys.Press(window, Key.F11);
                    Settle(window);
                    Assert.Null(main.FullScreenPreview);
                    tabs.SelectedIndex = MainTab.Places;
                    Settle(window);
                }
            }
            finally
            {
                main.LeaveFullScreen();
                ThemeManager.Apply(before);
                typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            }
        });
    }
}
