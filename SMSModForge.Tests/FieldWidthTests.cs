using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Fields that need not stretch with the window do not (the author, 1.8.0),
/// and a preview's box is as wide as the preview - on a window made wide
/// enough that anything still stretching would show it. Measured.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class FieldWidthTests
{
    private readonly ITestOutputHelper _out;
    public FieldWidthTests(ITestOutputHelper o) => _out = o;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    private static void Wide(MainWindow window)
    {
        window.Width = 2400;
        window.Height = 1000;
        WindowHarness.Pump();
        window.UpdateLayout();
    }

    [Theory]
    [InlineData(6, "scenes.preview")]
    [InlineData(9, "wallpapers.preview")]
    [InlineData(3, "places.preview")]
    [InlineData(5, "dialogues.preview")]
    public void APreviewsBoxIsAsWideAsThePreview(int tab, string header)
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.SelectedTabIndex = tab;
            switch (tab)
            {
                case 6: vm.AddSceneCommand.Execute(null); break;
                case 9: vm.AddWallpaperCommand.Execute(null); break;
                case 3: vm.AddPlaceCommand.Execute(null); break;
                case 5:
                    vm.AddDialogueCommand.Execute(null);
                    WindowHarness.Pump();
                    vm.AddDialogueRootNodeCommand.Execute(null);
                    // Spoken by somebody with a bust: a line nobody with one
                    // speaks shows a line of text in its place (1.8.0).
                    WindowHarness.Pump();
                    vm.SelectedNode!.Actor = "anna";
                    break;
            }
            Wide(window);

            var box = Descendants<GroupBox>(window).Single(g => g.IsVisible && g.Header as string == Loc.T(header));
            var preview = Descendants<FrameworkElement>(box)
                .Where(e => e.GetType().Namespace == "SMSModForge.View.Controls" && e.IsVisible)
                .OrderByDescending(e => e.ActualWidth).First();
            var column = (FrameworkElement)VisualTreeHelper.GetParent(box);
            _out.WriteLine($"{header}: box {box.ActualWidth:0}, {preview.GetType().Name} {preview.ActualWidth:0}, space {column.ActualWidth:0}");
            Assert.True(box.ActualWidth - preview.ActualWidth < 40, "the box is wider than its preview");
            Assert.True(box.ActualWidth < column.ActualWidth - 100, "the box still stretches with the window");
        });
    }

    [Fact]
    public void NamesAndNumbersStopAtTheirWidth()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.SelectedTabIndex = 6;
            vm.AddSceneCommand.Execute(null);
            Wide(window);
            var name = Descendants<TextBox>(window).Single(t => t.IsVisible
                && t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "DisplayName");
            _out.WriteLine($"display name {name.ActualWidth:0} of a {window.ActualWidth:0} window");
            Assert.True(name.ActualWidth <= 440.5);

            // Every number box on the Characters tab (pitches, jiggle) and the
            // node's timeout, wherever they are showing.
            vm.SelectedTabIndex = 5;
            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            vm.AddDialogueRootNodeCommand.Execute(null);
            vm.SelectedNode!.Duration = SMSModForge.Model.NodeDurationMode.Timeout;
            WindowHarness.Pump();
            window.UpdateLayout();
            var style = (Style)window.FindResource("NumberBox");
            var numbers = Descendants<TextBox>(window).Where(t => t.IsVisible && t.Style == style).ToList();
            _out.WriteLine($"{numbers.Count} number boxes: " + string.Join(", ", numbers.Select(t => t.ActualWidth.ToString("0"))));
            Assert.NotEmpty(numbers);
            Assert.All(numbers, t => Assert.Equal(90, t.ActualWidth, 1));
        });
    }
}
