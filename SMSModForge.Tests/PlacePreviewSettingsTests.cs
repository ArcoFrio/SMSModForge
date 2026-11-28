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
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The picked object's, or character's, settings beside the place preview's picture (after 1.10.0, the author:
/// "all of the settings that already appear for GOs on their records in the bottom to also appear on the selected
/// GO window on the right, for quick editing" - "Same thing for NPCs too"). Measured in the drawn window:
/// <list type="bullet">
/// <item>every editor the record below shows for it is in the panel too - its own and its components' and
/// conditions' - and the records nested in it are not (the hierarchy lists those): the control, a child shown
/// below and not beside;</item>
/// <item>laid out for the panel: nothing runs past its edge, the record's buttons go under its name rather than
/// beside it (beside it, as before, in the record below);</item>
/// <item>what is typed there is the record's, and Ctrl+Z takes it back with the panel still on it;</item>
/// <item>drawn in the panel's dark in a light theme (the record below light, the control) and readable in every
/// theme, Classic and EX.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlacePreviewSettingsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-pickedsettings-" + Guid.NewGuid().ToString("N"));

    public PlacePreviewSettingsTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private void Run(Action<MainWindow, MainViewModel, PlacePreview, PlaceViewModel> body)
    {
        PackRepository.Save(UndoCrawlPack.Make(_dir), _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            var place = vm.Places.First(p => p.GameObjects.Any(g => g.HasLight));
            vm.SelectedPlace = place;
            WindowHarness.Pump();
            try { body(window, vm, (PlacePreview)window.FindName("PlacePreviewMain"), place); }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
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

    /// <summary>
    /// The record of <paramref name="row"/> as its whole template draws it - what the GameObjects list below the
    /// picture drew for it until its controls moved into the preview (after 1.10.0) - made here, on the place's page
    /// where the list was, for the panel to be held to: everything it shows must be beside the picture too.
    /// </summary>
    private static FrameworkElement RecordOf(Window window, PlacePreview preview, object row)
    {
        if (Below(window).OfType<ContentPresenter>().FirstOrDefault(p => ReferenceEquals(p.Content, row) && !IsWithin(p, preview)) is { } shown)
            return shown;
        DependencyObject at = preview;
        while (at is not GroupBox) at = VisualTreeHelper.GetParent(at);
        var page = (Panel)VisualTreeHelper.GetParent(at);
        var record = new ContentControl { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(record);
        record.UpdateLayout();
        WindowHarness.Pump();
        return Below(record).OfType<ContentPresenter>().First(p => ReferenceEquals(p.Content, row));
    }

    private static bool IsWithin(DependencyObject d, DependencyObject ancestor)
    {
        for (var at = d; at != null; at = VisualTreeHelper.GetParent(at))
            if (ReferenceEquals(at, ancestor)) return true;
        return false;
    }

    private static string PathOf(FrameworkElement fe, DependencyProperty p)
        => BindingOperations.GetBindingExpressionBase(fe, p) is BindingExpression b ? b.ParentBinding.Path?.Path ?? "" : "";

    /// <summary>Every editor shown under <paramref name="root"/> for <paramref name="owner"/> - its own, its
    /// components' and its conditions' - as what it edits; not those of another record nested in it.</summary>
    private static List<(string What, FrameworkElement Control)> Editors(DependencyObject root, object owner)
    {
        var found = new List<(string, FrameworkElement)>();
        void Walk(DependencyObject d)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is UIElement { IsVisible: false }) continue;
                if (child is FrameworkElement fe)
                {
                    // Another record nested in this one: its editors are its own.
                    if (fe.DataContext is GameObjectViewModel or NpcPlacementViewModel && !ReferenceEquals(fe.DataContext, owner)) continue;
                    string? path = fe switch
                    {
                        PathPickerBox => PathOf(fe, PathPickerBox.PathTextProperty),
                        ComboBox => new[] { PathOf(fe, Selector.SelectedItemProperty), PathOf(fe, Selector.SelectedValueProperty) }
                                        .FirstOrDefault(p => p.Length > 0) ?? "",
                        TextBox t when t.TemplatedParent is not Control => PathOf(fe, TextBox.TextProperty),
                        ToggleButton => PathOf(fe, ToggleButton.IsCheckedProperty),
                        Slider => PathOf(fe, RangeBase.ValueProperty),
                        ButtonBase => PathOf(fe, ButtonBase.CommandProperty),
                        _ => null,
                    };
                    if (path != null)
                    {
                        if (path.Length > 0) found.Add(($"{fe.GetType().Name} [{path}] of {fe.DataContext?.GetType().Name}", fe));
                        // What a box, a list or a picker is made of is that one editor.
                        if (fe is PathPickerBox or ComboBox or TextBox or Slider) continue;
                    }
                }
                Walk(child);
            }
        }
        Walk(root);
        return found;
    }

    /// <summary>Nothing in the panel runs past its right edge: every editor and every word inside it.</summary>
    private void HoldToTheEdge(FrameworkElement panel, string what)
    {
        double width = panel.ActualWidth;
        var over = new List<string>();
        foreach (var fe in Below(panel).OfType<FrameworkElement>())
        {
            if (!fe.IsVisible || fe.ActualWidth <= 0) continue;
            if (fe is not (Control or TextBlock)) continue;
            double right = fe.TranslatePoint(new Point(fe.ActualWidth, 0), panel).X;
            if (right > width + 0.5)
                over.Add($"{fe.GetType().Name} '{(fe as TextBlock)?.Text ?? (fe as ContentControl)?.Content as string ?? ""}' ends at {right:0} of {width:0}");
        }
        foreach (var o in over.Distinct().Take(20)) _out.WriteLine(what + ": " + o);
        Assert.True(over.Count == 0, $"{what}: {over.Count} things run past the panel's edge, first: {over.FirstOrDefault()}");
    }

    private static TextBox BoxOf(DependencyObject root, object row, string path)
        => Below(root).OfType<TextBox>().First(b => b.IsVisible && ReferenceEquals(b.DataContext, row) && PathOf(b, TextBox.TextProperty) == path);

    private static void TypeInto(TextBox box, string text)
    {
        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, box) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
        box.Text = text;
        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        WindowHarness.Pump();
    }

    /// <summary>Ctrl+Z, really pressed, until the history is back to <paramref name="depth"/>: what was typed
    /// is at least one step, and no more than a step a box.</summary>
    private static void TakeBack(MainWindow window, MainViewModel vm, int depth)
    {
        vm.Undo.Checkpoint();
        Assert.True(vm.Undo.Depth > depth, "what was typed beside the picture is no step");
        for (int guard = 0; guard < 4 && vm.Undo.Depth > depth; guard++)
        {
            RealKeys.Press(window, Key.Z, ModifierKeys.Control);
            Settle(window);
        }
        Assert.Equal(depth, vm.Undo.Depth);
    }

    /// <summary>The record's editors all in the panel; and the panel holding to its edge.</summary>
    private void HoldTheSame(MainWindow window, PlacePreview preview, object row, string what)
    {
        Assert.True(preview.Pick(row), what + ": not in the picture");
        Settle(window);
        var panel = preview.SettingsPanel;
        Assert.True(panel.IsVisible, what + ": no settings beside the picture");
        Assert.Same(row, panel.Content);

        var inRecord = Editors(RecordOf(window, preview, row), row).Select(e => e.What).Distinct().ToList();
        var inPanel = Editors(panel, row).Select(e => e.What).Distinct().ToList();
        _out.WriteLine($"{what}: {inRecord.Count} editors in its record, {inPanel.Count} beside the picture, {panel.ActualWidth:0} wide");
        foreach (var e in inRecord) _out.WriteLine("  " + e);
        Assert.True(inRecord.Count >= 15, what + ": the record shows too few editors to compare - is it drawn?");
        var missing = inRecord.Except(inPanel).ToList();
        Assert.True(missing.Count == 0, what + ": not beside the picture: " + string.Join("; ", missing));
        HoldToTheEdge(panel, what);
    }

    [Fact]
    public void ThePickedObjectsSettingsAreBesideThePicture_AllOfItsRecords()
    {
        Run((window, vm, preview, place) =>
        {
            var row = place.AddGameObject();
            row.Name = "Picked";
            row.ParallaxEnabled = true;
            row.AddComponentCommand.Execute(null);
            row.AddActiveConditionCommand.Execute(null);
            row.AddChildCommand.Execute(null);
            Settle(window);
            HoldTheSame(window, preview, row, "an object");
            var panel = preview.SettingsPanel;
            var record = RecordOf(window, preview, row);

            // Control: the child is in the record below, and only in the hierarchy beside the picture.
            var child = row.Children.Single();
            Assert.Contains(Below(record).OfType<ContentPresenter>(), p => ReferenceEquals(p.Content, child) && p.IsVisible);
            Assert.DoesNotContain(Below(panel).OfType<ContentPresenter>(), p => ReferenceEquals(p.Content, child) && p.IsVisible);

            // The buttons go under the name beside the picture, and stay beside it below (the control).
            double Gap(DependencyObject root)
            {
                var name = BoxOf(root, row, "Name");
                var copy = Below(root).OfType<Button>().First(b => b.IsVisible && ReferenceEquals(b.DataContext, row) && PathOf(b, ButtonBase.CommandProperty) == "CopyCommand");
                return copy.TranslatePoint(new Point(0, 0), name).Y - name.ActualHeight;
            }
            double gapPanel = Gap(panel), gapRecord = Gap(record);
            _out.WriteLine($"Copy starts {gapPanel:0.#} below the name's bottom beside the picture, {gapRecord:0.#} below");
            Assert.True(gapPanel >= 0, "beside the picture, the record's buttons are not under its name");
            Assert.True(gapRecord < 0, "below, the record's buttons moved off the name's row");
            Assert.True(BoxOf(panel, row, "Name").ActualWidth >= 100, "the name box beside the picture is too narrow to type in");

            // What is typed beside the picture is the record's, and Ctrl+Z takes it back there.
            vm.Undo.Checkpoint();
            string before = PackRepository.Serialize(vm.Pack);
            int depth = vm.Undo.Depth;
            TypeInto(BoxOf(panel, row, "Name"), "Renamed");
            TypeInto(BoxOf(panel, row, "X"), "3.5");
            Settle(window);
            Assert.Equal("Renamed", row.Name);
            Assert.Equal("Renamed", BoxOf(record, row, "Name").Text);
            Assert.Contains("Renamed", preview.PickedTitle);
            Assert.Equal(3.5, row.X, 3);
            TakeBack(window, vm, depth);
            Assert.Equal(before, PackRepository.Serialize(vm.Pack));
            var shown = Assert.IsType<GameObjectViewModel>(preview.SettingsPanel.Content);
            Assert.Equal("Picked", shown.Name);
            Assert.Equal("Picked", BoxOf(preview.SettingsPanel, shown, "Name").Text);
            // The title under which the name box sits says the name it has again, not the one it had.
            Assert.Contains("Picked", preview.PickedTitle);
            Assert.DoesNotContain("Renamed", preview.PickedTitle);

            // Taken out by the panel's own "−", and Ctrl+Z: back, and picked again, its settings beside the
            // picture as they were.
            vm.Undo.Checkpoint();
            int beforeRemove = vm.Undo.Depth;
            var minus = Below(preview.SettingsPanel).OfType<Button>()
                .First(b => b.IsVisible && ReferenceEquals(b.DataContext, shown) && PathOf(b, ButtonBase.CommandProperty) == "RemoveCommand");
            minus.Command.Execute(minus.CommandParameter);
            Settle(window);
            Assert.DoesNotContain(place.GameObjects, g => g.Name == "Picked");
            Assert.False(preview.SettingsPanel.IsVisible, "the settings of an object taken out are still beside the picture");
            TakeBack(window, vm, beforeRemove);
            var back = Assert.IsType<GameObjectViewModel>(preview.SettingsPanel.Content);
            Assert.True(preview.SettingsPanel.IsVisible, "the object Ctrl+Z put back is not picked again");
            Assert.Equal("Picked", back.Name);
            Assert.Contains(place.GameObjects, g => ReferenceEquals(g, back));

            Shots(window, preview, "object");
        });
    }

    [Fact]
    public void ThePickedCharactersSettingsAreBesideThePicture_AllOfItsRecords()
    {
        Run((window, vm, preview, place) =>
        {
            place.AddNpcCommand.Execute(null);
            var placed = place.NpcsNode.Npcs.Last();
            placed.Name = "Standing";
            placed.AddComponentCommand.Execute(null);
            placed.AddActiveConditionCommand.Execute(null);
            Settle(window);
            HoldTheSame(window, preview, placed, "a character");

            vm.Undo.Checkpoint();
            string before = PackRepository.Serialize(vm.Pack);
            int depth = vm.Undo.Depth;
            TypeInto(BoxOf(preview.SettingsPanel, placed.Body, "X"), "1.25");
            Settle(window);
            Assert.Equal(1.25, placed.Body.X, 3);
            TakeBack(window, vm, depth);
            Assert.Equal(before, PackRepository.Serialize(vm.Pack));
            Assert.IsType<NpcPlacementViewModel>(preview.SettingsPanel.Content);

            Shots(window, preview, "character");
        });
    }

    /// <summary>In the panel's dark in a light theme, where the record below is light; and every word in it
    /// readable, in every theme of both looks.</summary>
    [Fact]
    public void TheSettingsAreInThePanelsDark_AndReadableInEveryTheme()
    {
        Run((window, vm, preview, place) =>
        {
            var row = place.AddGameObject();
            row.Name = "Picked";
            row.AddComponentCommand.Execute(null);
            row.AddActiveConditionCommand.Execute(null);
            Settle(window);
            Assert.True(preview.Pick(row));
            var was = ThemeManager.Current;
            var failures = new List<string>();
            try
            {
                foreach (var theme in ThemeManager.All)
                {
                    ThemeManager.Apply(theme);
                    Settle(window);
                    var panel = preview.SettingsPanel;
                    Assert.True(panel.IsVisible, theme.Name + ": no settings beside the picture");
                    static bool Dark(Color c) => ThemeManager.Contrast(c, Colors.White) > ThemeManager.Contrast(c, Colors.Black);
                    var label = Below(panel).OfType<TextBlock>().First(t => t.IsVisible && t.Text.Length > 0);
                    var back = ContrastAudit.BackgroundOf(label);
                    Assert.True(back is { } b && Dark(b), $"{theme.Name}: the settings are not in the panel's dark ({(back is { } c ? ContrastAudit.Hex(c) : "none")})");
                    if (!theme.IsDark)
                    {
                        var below = Below(RecordOf(window, preview, row)).OfType<TextBlock>().First(t => t.IsVisible && t.Text.Length > 0);
                        Assert.True(ContrastAudit.BackgroundOf(below) is { } l && !Dark(l), theme.Name + ": the control - the record below should be light");
                    }
                    // And laid out for the panel in this look too: EX's are other shapes in another typeface.
                    HoldToTheEdge(panel, theme.Name);
                    foreach (var f in ContrastAudit.Walk(panel).Where(f => f.Enabled && f.Ratio < ContrastAudit.Needed(f.Large)))
                        failures.Add($"{theme.Name}: '{f.Text}' {ContrastAudit.Hex(f.Foreground)} on {ContrastAudit.Hex(f.Background)} = {f.Ratio:0.00} ({f.Path})");
                    if (theme.Name is "Light" or "Frost" or "Neon" or "Dark") Shots(window, preview, "theme");
                }
            }
            finally { ThemeManager.Apply(was); }
            foreach (var f in failures.Distinct().Take(40)) _out.WriteLine(f);
            Assert.Empty(failures);
        });
    }

    /// <summary>Pictures to look at, when asked for (SMSMODFORGE_SNAPSHOTS): the preview as it stands, and the
    /// panel beside it whole, scrolled or not.</summary>
    private static void Shots(Window window, PlacePreview preview, string what)
    {
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        string theme = ThemeManager.Current.Name;
        Shot(preview, Path.Combine(dir, $"picked-{what}-{theme}-preview.png"), null);
        var stack = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(preview.SettingsPanel));
        Shot(stack, Path.Combine(dir, $"picked-{what}-{theme}-panel.png"), Color.FromRgb(0x22, 0x24, 0x28));
    }

    private static void Shot(FrameworkElement e, string file, Color? back)
    {
        int w = (int)Math.Ceiling(e.ActualWidth), h = (int)Math.Ceiling(e.ActualHeight);
        if (w <= 0 || h <= 0) return;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (back is { } b) dc.DrawRectangle(new SolidColorBrush(b), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(e) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        ThemeSnapshotTests.Save(bmp, file);
    }
}
