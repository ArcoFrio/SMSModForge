using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Diagnostics;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// That the UI tab's bindings actually reach something.
/// <para/>
/// This exists because a binding to a property that does not exist fails
/// SILENTLY in WPF — the control simply shows nothing — and that has now cost
/// two working features in this tab. First the preview, which never received
/// its token; then the whole list, after a rename moved
/// <c>UiExtensions</c> to <c>Uis</c> and left the XAML pointing at the old name.
/// Both looked to a test like the view model behaving perfectly, because the
/// view model WAS behaving perfectly.
/// <para/>
/// So rather than assert about one binding at a time, this listens to WPF's own
/// data-binding trace and fails on anything it reports while the tab is driven.
/// It catches the class instead of the instance.
/// <para/>
/// The listener has to exist BEFORE the window does. WPF decides whether to
/// emit a binding trace when the binding is created, not when it fails, so a
/// watch installed inside the harness saw nothing at all - a test that could
/// not fail, which is worse than no test. Confirmed by putting the real bug
/// back and watching this go red.
/// </summary>
public class UiBindingTests
{
    private readonly ITestOutputHelper _out;
    public UiBindingTests(ITestOutputHelper o) => _out = o;

    /// <summary>Collects whatever WPF says about bindings.</summary>
    private sealed class BindingWatch : TraceListener, IDisposable
    {
        public List<string> Complaints { get; } = new();
        private readonly SourceLevels _was;

        public BindingWatch()
        {
            PresentationTraceSources.Refresh();
            _was = PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            PresentationTraceSources.DataBindingSource.Listeners.Add(this);
        }

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrEmpty(message)) return;
            // "path error" and "cannot find" are what a binding to a property
            // that is not there produces. Other chatter at this level is
            // ordinary and not worth failing over.
            if (message.Contains("path error", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Cannot find", StringComparison.OrdinalIgnoreCase))
                Complaints.Add(message);
        }

        public void Dispose()
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            PresentationTraceSources.DataBindingSource.Switch.Level = _was;
        }
    }

    [Fact]
    public void Driving_the_tab_produces_no_broken_bindings()
    {
        // Set up BEFORE the window exists: WPF decides whether to emit binding
        // traces when the binding is created, not when it fails.
        using var watch = new BindingWatch();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");

            // Show the tab, so its templates are actually realised - an
            // unrealised tab binds nothing and would pass trivially.
            tabs.SelectedIndex = tabs.Items.Count - 1;
            WindowHarness.Pump();

            vm.AddOwnUiCommand.Execute(null);
            WindowHarness.Pump();

            vm.AddVanillaUiCommand.Execute(null);
            WindowHarness.Pump();

            if (VanillaUiLibrary.IsAvailable)
            {
                vm.Uis[^1].Source = "vanillaui:9_MainCanvas/Quitagme";
                WindowHarness.Pump();

                // And with a row selected, so the tree's item template runs too.
                vm.SelectedUi = vm.Uis[^1];
                WindowHarness.Pump();
            }

            foreach (var complaint in watch.Complaints.Distinct().Take(6))
                _out.WriteLine(complaint);

            Assert.True(watch.Complaints.Count == 0,
                $"{watch.Complaints.Count} broken binding(s); first: " +
                (watch.Complaints.FirstOrDefault() ?? ""));
        });
    }

    [Fact]
    public void What_is_added_appears_in_the_list()
    {
        // The specific failure this file was written for: the list bound to a
        // property that had been renamed away, so adding a UI put a row in the
        // view model and nothing on screen. Asserting on vm.Uis alone - which is
        // what the tab tests did - could not see it.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            var list = (ListBox)window.FindName("UiList");
            Assert.NotNull(list);

            tabs.SelectedIndex = tabs.Items.Count - 1;
            WindowHarness.Pump();

            Assert.Empty(list.Items);

            vm.AddOwnUiCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Single(list.Items);

            vm.AddVanillaUiCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal(2, list.Items.Count);
            Assert.Equal(vm.Uis.Count, list.Items.Count);

            // And the selection the view model reports is the one the list shows.
            Assert.Same(vm.SelectedUi, list.SelectedItem);

            vm.RemoveUiCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Single(list.Items);
        });
    }

    [Fact]
    public void Selecting_a_row_and_editing_it_reaches_the_picture()
    {
        // The loop an author actually works in: click an object, change a
        // number, see it move. Every part of it has been broken separately at
        // some point in this tab, so it is asserted end to end rather than in
        // pieces.
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            var preview = (UiPreview)window.FindName("UiScreenPreview");

            tabs.SelectedIndex = tabs.Items.Count - 1;
            WindowHarness.Pump();

            vm.AddVanillaUiCommand.Execute(null);
            vm.Uis[0].Source = "vanillaui:9_MainCanvas/Quitagme";
            WindowHarness.Pump();

            // The preview is drawing the AUTHORED tree, not the vanilla screen.
            Assert.NotNull(preview.AuthoredRoot);
            Assert.Same(vm.Uis[0].RootNode, preview.AuthoredRoot);
            var before = (System.Windows.Media.Imaging.BitmapSource?)
                ((Image)preview.Children[0]).Source;
            Assert.NotNull(before);

            // Select something, as clicking the tree does.
            var target = vm.Uis[0].Nodes[0].Children.First(c => c.HasImage);
            vm.Uis[0].SelectedNode = target;
            WindowHarness.Pump();

            Assert.Same(target.Model, preview.Highlight);
            Assert.True(vm.Uis[0].HasSelection);

            // Move it, as typing in the position box does.
            target.PositionX += 150;
            WindowHarness.Pump();
            preview.Refresh();

            var after = (System.Windows.Media.Imaging.BitmapSource?)
                ((Image)preview.Children[0]).Source;
            Assert.NotNull(after);
            Assert.NotSame(before, after);

            // And the row is now marked as changed, by the same rule that
            // decides what gets saved.
            Assert.Equal("changed", target.Status);
            _out.WriteLine($"moved {target.Name}; status now '{target.Status}', " +
                           $"summary '{vm.Uis[0].Summary}'");
        });
    }

    [Fact]
    public void Adding_an_object_puts_it_in_the_tree_and_on_the_picture()
    {
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) return;

            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            var tree = (TreeView)window.FindName("UiTree");

            tabs.SelectedIndex = tabs.Items.Count - 1;
            WindowHarness.Pump();

            vm.AddVanillaUiCommand.Execute(null);
            vm.Uis[0].Source = "vanillaui:9_MainCanvas/Quitagme";
            WindowHarness.Pump();

            var ui = vm.Uis[0];
            int before = ui.Nodes[0].Children.Count;

            Assert.True(ui.AddChildCommand.CanExecute(null));
            ui.AddChildCommand.Execute(null);
            WindowHarness.Pump();

            Assert.Equal(before + 1, ui.Nodes[0].Children.Count);
            Assert.NotNull(ui.SelectedNode);
            Assert.Equal("new", ui.SelectedNode!.Status);

            // And it can be taken out again, which a vanilla object cannot.
            Assert.True(ui.RemoveNodeCommand.CanExecute(null));
            ui.RemoveNodeCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal(before, ui.Nodes[0].Children.Count);

            ui.SelectedNode = ui.Nodes[0].Children.First(c => c.IsVanilla);
            Assert.False(ui.RemoveNodeCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Choosing_a_screen_fills_the_tree_on_screen()
    {
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            var tree = (TreeView)window.FindName("UiTree");
            Assert.NotNull(tree);

            tabs.SelectedIndex = tabs.Items.Count - 1;
            WindowHarness.Pump();

            vm.AddVanillaUiCommand.Execute(null);
            vm.Uis[0].Source = "vanillaui:9_MainCanvas/Quitagme";
            WindowHarness.Pump();

            Assert.Single(tree.Items);
            _out.WriteLine($"tree root: {((UiNodeViewModel)tree.Items[0]!).Display}");
        });
    }
}
