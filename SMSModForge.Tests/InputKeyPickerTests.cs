using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The InputKey condition's key list scrolls like any list. Grouped, it
/// scrolled a whole group per step, and the letters group is taller than the
/// list, so most letters could not be reached.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class InputKeyPickerTests
{
    private readonly ITestOutputHelper _out;
    public InputKeyPickerTests(ITestOutputHelper o) => _out = o;

    private const int TabQuests = 13;

    [Fact]
    public void TheKeyListScrollsByLinesNotByGroups()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smskeys-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var pack = PackRepository.CreateEmpty("keys.pack");
            pack.Quests.Add(new QuestDef
            {
                Key = "q", Title = "Q",
                StartConditions =
                {
                    new NodeConditionDef
                    {
                        Type = NodeConditionTypes.InputKey,
                        Params = new Dictionary<string, string> { ["key"] = "A" },
                    },
                },
            });
            PackRepository.Save(pack, dir);

            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(dir);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;
                WindowHarness.Pump();
                window.UpdateLayout();
                WindowHarness.Pump();

                var picker = FindAll<ComboBox>(window).Single(c => c.Name == "InputKeyPicker" && c.IsVisible);
                picker.IsDropDownOpen = true;
                WindowHarness.Pump();

                var popup = (Popup)(picker.Template.FindName("Popup", picker) ?? picker.Template.FindName("PART_Popup", picker));
                var scroll = FindAll<ScrollViewer>(popup.Child).First();
                scroll.UpdateLayout();
                int groups = ((System.ComponentModel.ICollectionView)picker.ItemsSource).Groups.Count;
                var panel = FindAll<Panel>(scroll).FirstOrDefault(p => p.IsItemsHost);
                _out.WriteLine($"{groups} groups, extent {scroll.ExtentHeight}, viewport {scroll.ViewportHeight}, "
                               + $"content scroll {scroll.CanContentScroll}, host {panel?.GetType().Name}, "
                               + $"unit {VirtualizingPanel.GetScrollUnit(picker)}");

                // A turn of the wheel, as the list gets it.
                double atStart = scroll.VerticalOffset;
                var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
                { RoutedEvent = UIElement.MouseWheelEvent };
                scroll.RaiseEvent(wheel);
                scroll.UpdateLayout();
                _out.WriteLine($"a wheel step moves {scroll.VerticalOffset - atStart}");
                scroll.ScrollToVerticalOffset(atStart);
                scroll.UpdateLayout();

                // Scrolled by item, a grouped list's extent is its number of
                // groups; by pixel, it is the height of everything in it.
                Assert.True(scroll.ExtentHeight > groups * 4,
                            $"the list scrolls by group: its extent is {scroll.ExtentHeight} for {groups} groups");

                double before = scroll.VerticalOffset;
                scroll.LineDown();
                scroll.UpdateLayout();
                double step = scroll.VerticalOffset - before;
                _out.WriteLine($"one step moves {step}");
                Assert.InRange(step, 1, scroll.ViewportHeight / 2);

                picker.IsDropDownOpen = false;
                WindowHarness.Pump();
            });
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { }
        }
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }
}
