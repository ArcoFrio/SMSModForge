using System;
using System.Linq;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The UI tab, in the real window: that it is where it should be, that adding a
/// screen works end to end, and that the preview follows the selection.
/// </summary>
public class UiTabTests
{
    private readonly ITestOutputHelper _out;
    public UiTabTests(ITestOutputHelper o) => _out = o;

    /// <summary>The UI tab went in LAST rather than beside Places, so that
    /// every existing tab kept its index. Several tests address tabs by number
    /// and a silent renumbering would leave them asserting about the wrong
    /// screen while still passing, so the order is pinned here.</summary>
    [Fact]
    public void The_tab_is_last_and_nothing_else_moved()
    {
        WindowHarness.Run(window =>
        {
            var tabs = (TabControl)window.FindName("MainTabs");
            var headers = tabs.Items.OfType<TabItem>()
                              .Select(t => t.Header?.ToString() ?? "").ToList();

            _out.WriteLine(string.Join(" | ", headers));
            Assert.Equal("UI", headers[^1]);
            Assert.Equal(12, headers.Count - 1);

            // The indices the focus sweep and the layout memory rely on.
            Assert.Equal("Characters", headers[1]);
            Assert.Equal("Places", headers[3]);
            Assert.Equal("Integration", headers[11]);
        });
    }

    [Fact]
    public void Adding_a_screen_starts_empty_and_choosing_one_fills_it()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            Assert.Empty(vm.UiExtensions);

            vm.AddUiExtensionCommand.Execute(null);
            var added = Assert.Single(vm.UiExtensions);
            Assert.Same(added, vm.SelectedUiExtension);

            // Nothing is chosen for the author. Guessing a screen would seed
            // thousands of objects for a decision they have not made.
            Assert.Equal("", added.Source);
            Assert.Empty(added.Nodes);
            Assert.Equal("(nothing chosen)", added.Display);

            if (!VanillaUiLibrary.IsAvailable)
            {
                _out.WriteLine("no extraction - stopping after the empty case");
                return;
            }

            added.Source = "vanillaui:9_MainCanvas/Quitagme";
            var root = Assert.Single(added.Nodes);
            Assert.Equal("Quitagme", root.Name);
            Assert.NotEmpty(root.Children);
            _out.WriteLine($"{added.Display} — {added.Summary}");
        });
    }

    [Fact]
    public void Removing_a_screen_takes_it_out_of_the_pack_as_well()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddUiExtensionCommand.Execute(null);
            Assert.Single(vm.Pack.VanillaUiExtensions);

            vm.RemoveUiExtensionCommand.Execute(null);
            Assert.Empty(vm.UiExtensions);
            Assert.Empty(vm.Pack.VanillaUiExtensions);
            Assert.Null(vm.SelectedUiExtension);
        });
    }

    [Fact]
    public void The_preview_follows_whatever_is_selected()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var preview = (UiPreview)window.FindName("UiScreenPreview");
            Assert.NotNull(preview);

            vm.AddUiExtensionCommand.Execute(null);
            Assert.Equal("", vm.SelectedUiPreviewToken);

            // With nothing chosen the pane says so rather than sitting empty:
            // no report, because there was nothing to draw, but a message.
            WindowHarness.Pump();
            Assert.Null(preview.Report);

            if (!VanillaUiLibrary.IsAvailable) return;

            vm.UiExtensions[0].Source = "vanillaui:9_MainCanvas/Quitagme";
            Assert.Equal("vanillaui:9_MainCanvas/Quitagme", vm.SelectedUiPreviewToken);

            // Through the binding, NOT by assigning BaseToken here. Setting it
            // by hand is what the first version of this test did, and it hid a
            // real bug: the token only notified when the SELECTION changed, so
            // choosing a screen on a freshly added row left the pane blank
            // while every assertion passed.
            WindowHarness.Pump();

            Assert.Equal(vm.SelectedUiPreviewToken, preview.BaseToken);
            Assert.NotNull(preview.Report);
            Assert.True(preview.Report!.Drawn > 0, "the preview drew nothing");
            _out.WriteLine($"drew {preview.Report.Drawn}; {preview.Trouble()}");
        });
    }

    [Fact]
    public void An_untouched_screen_does_not_make_the_pack_look_edited()
    {
        // Seeding puts thousands of objects into the working copy, and the
        // dirty check compares serialised forms. Without the delta pass on that
        // path too, merely opening a pack with a UI extension would claim
        // unsaved changes for ever.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var pack = new ModPack { PackId = "test" };
        pack.VanillaUiExtensions.Add(new VanillaUiExtensionDef
        {
            Source = "vanillaui:9_MainCanvas/Quitagme",
        });

        // As loading does: build the view model, which seeds the whole screen.
        var seeded = new VanillaUiExtensionViewModel(pack.VanillaUiExtensions[0]);
        Assert.NotEmpty(seeded.Nodes);
        Assert.True(seeded.Model.Nodes.Sum(Count) > 20, "the screen is in memory");

        string asSaved = PackRepository.SerializeAsSaved(pack);
        _out.WriteLine($"in memory: {seeded.Model.Nodes.Sum(Count)} nodes; " +
                       $"as saved: {asSaved.Length} chars");

        // The seeded tree must not reach the saved form at all.
        Assert.DoesNotContain("Quitagme/", asSaved);
        Assert.Equal(asSaved, PackRepository.SerializeAsSaved(pack));   // and stably so

        static int Count(UiNodeDef n) => 1 + n.Children.Sum(Count);
    }
}
