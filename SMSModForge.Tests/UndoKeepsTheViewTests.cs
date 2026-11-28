using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// An undo or a redo leaves the page where it is when what it changes is in front of the author (the author,
/// after 1.10.0: "some undos/redos bring the camera to the wrong place, like for example doing so after a change
/// made in the Places preview brings into view the fields for the GOs at the bottom instead of staying on the
/// preview"). A value is often shown twice - where a picked object stands, beside the picture and in its record
/// below - and the step went to the first of them in the page's order, wherever the author was. Measured in the
/// drawn window, with the keys really pressed:
/// <list type="bullet">
/// <item>what is dragged in the place preview, or typed beside its picture, taken back and brought back: the
/// page does not move;</item>
/// <item>typed in the record below while the object is picked: the page stays on the record (it went up to the
/// panel beside the picture);</item>
/// <item>the control: a step whose change is nowhere in sight is still brought into view, and marked.</item>
/// </list>
/// The walk over every control (UndoCrawler) holds every other control of every tab to the same.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UndoKeepsTheViewTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-keepview-" + Guid.NewGuid().ToString("N"));

    public UndoKeepsTheViewTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private void Run(Action<MainWindow, MainViewModel, PlacePreview, PlaceViewModel, GameObjectViewModel> body)
    {
        PackRepository.Save(UndoCrawlPack.Make(_dir), _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            try
            {
                vm.OpenPackFromPath(_dir);
                WindowHarness.Pump();
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                var place = vm.Places.First(p => p.GameObjects.Any(g => g.HasLight));
                vm.SelectedPlace = place;
                WindowHarness.Pump();
                var row = place.AddGameObject();
                row.Name = "Moved";
                row.AddComponentCommand.Execute(null);
                row.AddActiveConditionCommand.Execute(null);
                Settle(window);
                var preview = (PlacePreview)window.FindName("PlacePreviewMain");
                var page = ScrollerOf(preview);
                Assert.True(page.ExtentHeight > page.ViewportHeight * 1.2, $"the place's page does not scroll ({page.ExtentHeight:0} in {page.ViewportHeight:0}): nothing to measure");
                body(window, vm, preview, place, row);
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    private static ScrollViewer ScrollerOf(FrameworkElement inside)
    {
        for (DependencyObject? at = VisualTreeHelper.GetParent(inside); at != null; at = VisualTreeHelper.GetParent(at))
            if (at is ScrollViewer sv) return sv;
        throw new InvalidOperationException("not in a scrolled page");
    }

    private static IEnumerable<DependencyObject> Below(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Below(child)) yield return deeper;
        }
    }

    private static bool IsWithin(DependencyObject d, DependencyObject ancestor)
    {
        for (var at = d; at != null; at = VisualTreeHelper.GetParent(at))
            if (ReferenceEquals(at, ancestor)) return true;
        return false;
    }

    private static string PathOf(FrameworkElement fe, DependencyProperty p)
        => BindingOperations.GetBindingExpressionBase(fe, p) is BindingExpression b ? b.ParentBinding.Path?.Path ?? "" : "";

    /// <summary>The box of <paramref name="row"/>'s <paramref name="path"/> beside the picture, or in the record below.</summary>
    private static TextBox BoxOf(MainWindow window, PlacePreview preview, object row, string path, bool beside)
        => Below(window).OfType<TextBox>().First(b => b.IsVisible && ReferenceEquals(b.DataContext, row) && PathOf(b, TextBox.TextProperty) == path
                                                      && IsWithin(b, preview.SettingsPanel) == beside);

    private string Marked(MainWindow window, PlacePreview preview)
        => string.Join(", ", window.LastMarked.Select(m => m.GetType().Name + "[" + PathOf(m, TextBox.TextProperty) + "]"
                                                           + (IsWithin(m, preview) ? " beside the picture" : " below") + (window.InView(m) ? "" : " (out of sight)")));

    /// <summary>A box typed in as a person types: the keyboard in it, what was there chosen, the letters.</summary>
    private static void Type(TextBox box, string text)
    {
        box.Focus();
        Keyboard.Focus(box);
        WindowHarness.Pump();
        box.SelectAll();
        foreach (char c in text) TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, box, c.ToString()));
        WindowHarness.Pump();
    }

    private static void Drag(PlacePreview preview, PlacePreview.GizmoHandle h, Point from, Point to)
    {
        // As the mouse's own handler does: the keyboard to the picture, then the handle.
        preview.Focus();
        Keyboard.Focus(preview);
        preview.BeginHandle(h, from);
        preview.DragHandleTo(to);
        preview.EndHandle();
    }

    /// <summary>
    /// One edit, then Ctrl+Z and Ctrl+Y really pressed: after each, <paramref name="watched"/> - what the author
    /// is looking at - is drawn where it was as the key went down, and the step marked something in sight.
    /// </summary>
    private void Stays(MainWindow window, MainViewModel vm, PlacePreview preview, string what, Action edit, Func<FrameworkElement> watched, Action? alsoHolds = null)
    {
        vm.Undo.Checkpoint();
        int depth = vm.Undo.Depth;
        edit();
        Settle(window);
        vm.Undo.Checkpoint();
        Assert.True(vm.Undo.Depth > depth, what + ": no step");
        foreach (var key in new[] { Key.Z, Key.Y })
        {
            var look = watched();
            Assert.True(window.InView(look), $"{what}: the thing watched is not in sight before {key}");
            Point was = look.TranslatePoint(new Point(0, 0), window);
            RealKeys.Press(window, key, ModifierKeys.Control);
            Settle(window);
            Assert.Equal(key == Key.Z ? depth : depth + 1, vm.Undo.Depth);
            look = watched();
            Point now = look.TranslatePoint(new Point(0, 0), window);
            _out.WriteLine($"{what}, Ctrl+{key} ({vm.LastRestore}): drawn {was.Y:0} -> {now.Y:0} from the window's top; marked {Marked(window, preview)}");
            Assert.True(Math.Abs(now.Y - was.Y) < 1.5 && Math.Abs(now.X - was.X) < 1.5,
                        $"{what}: Ctrl+{key} moved the page - what the author was looking at was drawn {was.Y:0} px from the window's top, is {now.Y:0}; marked {Marked(window, preview)}");
            Assert.True(window.LastMarked.Count == 0 || window.LastMarked.Any(window.InView), $"{what}: Ctrl+{key} marked nothing in sight: {Marked(window, preview)}");
            alsoHolds?.Invoke();
        }
    }

    /// <summary>
    /// Two objects of one name in a place, the second picked: the pick stays on it - as the preview looks at it
    /// again, as the first is renamed, and as that is taken back and brought back - and the page with it. Their
    /// path names the first of them alone, and the pick went by the path before the object (the walk over every
    /// control, after 1.10.0: the light's settings beside the picture came and went under the keyboard).
    /// </summary>
    [Fact]
    public void TwoObjectsOfOneName_ThePickStaysOnTheOnePicked()
    {
        Run((window, vm, preview, place, row) =>
        {
            var twin = place.AddGameObject();
            twin.Name = row.Name;
            Settle(window);
            Assert.True(preview.Pick(twin));
            Settle(window);
            preview.InvalidateVisual();
            Settle(window);
            Assert.Same(twin, preview.SettingsPanel.Content);
            // The first renamed as anything could rename it (a rule's Rename, the hierarchy's own row one day): its
            // settings are only beside the picture now, and they show the second.
            Stays(window, vm, preview, "the first of two objects of one name renamed, the second picked",
                  () => row.Name = "Other",
                  () => preview,
                  () => Assert.Same(place.GameObjects.Last(g => g.Name == "Moved"), preview.SettingsPanel.Content));
            Assert.Same(twin, preview.SettingsPanel.Content);
        });
    }

    [Fact]
    public void AnUndoInThePlacePreview_StaysOnThePreview()
    {
        Run((window, vm, preview, place, row) =>
        {
            var page = ScrollerOf(preview);
            page.ScrollToVerticalOffset(0);
            Settle(window);
            Assert.True(preview.Pick(row));
            Settle(window);
            Func<FrameworkElement> picture = () => preview;

            Stays(window, vm, preview, "an object dragged", () => Drag(preview, PlacePreview.GizmoHandle.MoveFree, new Point(700, 400), new Point(760, 360)), picture);
            Stays(window, vm, preview, "an object's name typed beside the picture", () => Type(BoxOf(window, preview, row, "Name", beside: true), "Renamed"), picture);
            Stays(window, vm, preview, "an object's X typed beside the picture", () => Type(BoxOf(window, preview, row, "X", beside: true), "2.5"), picture);

            var lamp = place.GameObjects.First(g => g.HasLight);
            Assert.True(preview.Pick(lamp));
            Settle(window);
            Stays(window, vm, preview, "a light's reach dragged", () => Drag(preview, PlacePreview.GizmoHandle.LightOuter, new Point(1000, 500), new Point(1200, 520)), picture);

            place.AddNpcCommand.Execute(null);
            var placed = place.NpcsNode.Npcs.Last();
            Settle(window);
            Assert.True(preview.Pick(placed));
            Settle(window);
            Stays(window, vm, preview, "a character dragged", () => Drag(preview, PlacePreview.GizmoHandle.MoveFree, new Point(600, 500), new Point(650, 520)), picture);
        });
    }

    /// <summary>The control: what a step changes, nowhere in sight, is brought into view and marked - here a
    /// navigator button's label, low on the place's page, with the page at its top.</summary>
    [Fact]
    public void AnUndoOutOfSight_IsBroughtIntoView()
    {
        Run((window, vm, preview, place, row) =>
        {
            var page = ScrollerOf(preview);
            place.AddNavigatorButton();
            Settle(window);
            // The place's last button, as it is now: an undo may make the place's rows again.
            TextBox Label() => Below(window).OfType<TextBox>().First(b => b.IsVisible && ReferenceEquals(b.DataContext, vm.SelectedPlace!.NavigatorButtons.Last())
                                                                          && PathOf(b, TextBox.TextProperty) == "Label");
            vm.Undo.Checkpoint();
            int depth = vm.Undo.Depth;
            Type(Label(), "Far below");
            Settle(window);
            vm.Undo.Checkpoint();
            Assert.Equal(depth + 1, vm.Undo.Depth);
            page.ScrollToVerticalOffset(0);
            Settle(window);
            Assert.False(window.InView(Label()), "the label is in sight with the page at its top: nothing to bring into view");
            RealKeys.Press(window, Key.Z, ModifierKeys.Control);
            Settle(window);
            Assert.Equal(depth, vm.Undo.Depth);
            var box = Label();
            _out.WriteLine($"page at {page.VerticalOffset:0}; marked {Marked(window, preview)}");
            Assert.True(window.InView(box), "the step's change, out of sight, was not brought into view");
            Assert.Contains(box, window.LastMarked);
        });
    }
}
