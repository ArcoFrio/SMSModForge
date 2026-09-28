using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// "Editing in" sits at the right end of the tab row, under the game version
/// in the menu bar - moved there because beside that text it crowded it - and
/// no tab is ever underneath it, however narrow the window. Measured on the
/// window as drawn.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class EditingLanguagePlacementTests
{
    private readonly ITestOutputHelper _out;
    public EditingLanguagePlacementTests(ITestOutputHelper o) => _out = o;

    private static Rect Bounds(FrameworkElement e, Visual root)
        => e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    [Theory]
    [InlineData(1400)]
    [InlineData(700)]   // narrow enough that the tabs cannot all fit on one row beside it
    public void ItSitsOnTheTabRowAtTheRight_AndNoTabIsUnderIt(double width)
    {
        WindowHarness.Run(window =>
        {
            window.Width = width;
            window.UpdateLayout();
            WindowHarness.Pump();
            window.UpdateLayout();

            var bar = (FrameworkElement)window.FindName("EditingLanguageBar");
            var tabs = (TabControl)window.FindName("MainTabs");
            var root = (Visual)window.Content;
            Assert.True(bar.ActualWidth > 0, "the language list was never laid out");

            var at = Bounds(bar, root);
            var tabsAt = Bounds(tabs, root);
            _out.WriteLine($"window {width}: list at {at}, tabs start at y {tabsAt.Top:0}");

            // Right end, with the same margin the game version keeps.
            double rootWidth = ((FrameworkElement)root).ActualWidth;
            Assert.InRange(rootWidth - at.Right, 5, 20);
            // In the tab row: below the menu bar, at the top of the tabs.
            Assert.InRange(at.Top - tabsAt.Top, -1, 6);

            // No tab header under it.
            foreach (var item in tabs.Items.OfType<TabItem>().Where(t => t.IsVisible && t.ActualWidth > 0))
            {
                var header = Bounds(item, root);
                _out.WriteLine($"  {item.Header}: {header}");
                Assert.False(header.IntersectsWith(at) && Rect.Intersect(header, at).Width > 0.5,
                             $"the '{item.Header}' tab ({header}) is under the language list ({at})");
            }
        });
    }
}
