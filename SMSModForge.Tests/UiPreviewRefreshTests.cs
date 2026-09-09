using System.Linq;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Whether the picture actually keeps up with the edits.
/// <para/>
/// The preview is handed the authored tree as an object, and an edit changes
/// what is INSIDE that object without replacing it. A dependency property does
/// not fire when it is set to the value it already holds, so a binding that
/// merely re-reads the same root is not enough to cause a redraw - and the
/// symptom is a preview that looks correct and is simply stale, which is worse
/// than one that is obviously broken.
/// </summary>
public sealed class UiPreviewRefreshTests
{
    private readonly ITestOutputHelper _out;
    public UiPreviewRefreshTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Editing_a_node_redraws_the_preview()
    {
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            vm.AddVanillaUiCommand.Execute(null);
            vm.Uis[0].Source = "vanillaui:9_MainCanvas/Quitagme";
            vm.SelectedUi = vm.Uis[0];

            var preview = (UiPreview)window.FindName("UiScreenPreview");
            Assert.NotNull(preview);

            // Selected FIRST. Changing the selection redraws on its own, so
            // doing it after would let this pass without the edit doing
            // anything at all - which is exactly what it did.
            var node = vm.SelectedUi!.Nodes[0].Children.FirstOrDefault() ?? vm.SelectedUi.Nodes[0];
            vm.SelectedUi.SelectedNode = node;

            WindowHarness.Pump();
            var before = preview.Report;
            _out.WriteLine($"first render: {(before == null ? "none" : "done")}");
            Assert.NotNull(before);

            node.PositionX += 40;

            WindowHarness.Pump();
            Assert.False(ReferenceEquals(before, preview.Report),
                         "the preview did not redraw after the object moved");
        });
    }

    [Fact]
    public void Reordering_redraws_the_preview()
    {
        // Order is draw order, so a reorder changes the picture even though not
        // one number on any object changed.
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            vm.AddVanillaUiCommand.Execute(null);
            vm.Uis[0].Source = "vanillaui:9_MainCanvas/Quitagme";
            vm.SelectedUi = vm.Uis[0];

            var preview = (UiPreview)window.FindName("UiScreenPreview");
            var root = vm.SelectedUi!.Nodes[0];
            if (root.Children.Count < 2) { _out.WriteLine("not enough children - skipping"); return; }

            WindowHarness.Pump();
            var before = preview.Report;

            root.MoveBy(root.Children[0], +1);

            WindowHarness.Pump();
            Assert.False(ReferenceEquals(before, preview.Report),
                         "the preview did not redraw after the order changed");
        });
    }
}
