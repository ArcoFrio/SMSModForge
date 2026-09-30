using System.Linq;
using System.Windows.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The two translating ticks under a line's text (the author, 1.6.3), as the
/// window draws them: there, bound to the line, and one at a time.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NodeTranslationTicksTests
{
    private readonly ITestOutputHelper _out;
    public NodeTranslationTicksTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void TheTicksAreUnderTheText_AndTickTheLine()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            for (int i = 0; i < tabs.Items.Count; i++)
                if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues") { tabs.SelectedIndex = i; break; }
            WindowHarness.Pump();
            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
            var node = vm.SelectedDialogue.Nodes.First();
            vm.SelectedNode = node;
            WindowHarness.Pump();

            var same = (CheckBox)window.FindName("NodeTextSameEverywhere");
            var letters = (CheckBox)window.FindName("NodeTextLettersOnly");
            var text = (System.Windows.FrameworkElement)window.FindName("NodeTextBox");
            Assert.True(same.IsVisible && letters.IsVisible, "the translating ticks are not on screen");
            var below = same.TranslatePoint(new System.Windows.Point(0, 0), text).Y;
            _out.WriteLine($"ticks start {below:0} below the top of the text box ({text.ActualHeight:0} tall)");
            Assert.True(below >= text.ActualHeight, "the ticks are not under the text");

            same.IsChecked = true;
            WindowHarness.Pump();
            Assert.True(node.Model.TextSameEverywhere);

            letters.IsChecked = true;       // one at a time
            WindowHarness.Pump();
            Assert.True(node.Model.TextLettersOnly);
            Assert.False(node.Model.TextSameEverywhere);
            Assert.False(same.IsChecked == true, "the other tick still shows ticked");
        });
    }
}
