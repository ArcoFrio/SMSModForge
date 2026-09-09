using System.Linq;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Reported: opening the sprite dropdown alone put steps on the undo stack, and
/// undoing then walked back through sprites that were never chosen.
/// <para/>
/// The mechanism is a value-bound dropdown. An undo step is marked when focus
/// leaves a field, and opening a dropdown moves focus into its popup - so the
/// binding commits mid-browse and each visit leaves steps behind. The sprite
/// field is a box and a picker now, which removes the mechanism rather than
/// tuning it: nothing is written until Choose is pressed.
/// </summary>
public sealed class UiSpriteUndoTests
{
    private readonly ITestOutputHelper _out;
    public UiSpriteUndoTests(ITestOutputHelper output) => _out = output;

    private static void Walk(System.Windows.DependencyObject root, System.Action<ComboBox> onCombo)
    {
        if (root is ComboBox c) onCombo(c);
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            Walk(System.Windows.Media.VisualTreeHelper.GetChild(root, i), onCombo);
    }

    [Fact]
    public void No_field_in_the_tab_commits_on_focus_loss_through_a_dropdown()
    {
        // The structural guarantee. A dropdown whose value commits on LostFocus
        // is the exact shape that caused this, so the check is that none of
        // them do - which fails if one is reintroduced.
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            tabs.SelectedIndex = tabs.Items.Count - 1;
            WindowHarness.Pump();

            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.SelectedNode = ui.Nodes[0];
            WindowHarness.Pump();

            var offenders = new System.Collections.Generic.List<string>();
            Walk(window, combo =>
            {
                var binding = System.Windows.Data.BindingOperations.GetBinding(combo, ComboBox.TextProperty);
                if (binding == null) return;
                if (binding.UpdateSourceTrigger == System.Windows.Data.UpdateSourceTrigger.LostFocus)
                    offenders.Add(binding.Path?.Path ?? "(unnamed)");
            });

            foreach (var o in offenders) _out.WriteLine("commits on focus loss: " + o);
            Assert.Empty(offenders);
        });
    }

    [Fact]
    public void The_sprite_field_is_not_a_dropdown_at_all()
    {
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            tabs.SelectedIndex = tabs.Items.Count - 1;
            WindowHarness.Pump();

            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.SelectedNode = ui.Nodes[0];
            WindowHarness.Pump();

            bool spriteCombo = false;
            Walk(window, combo =>
            {
                var binding = System.Windows.Data.BindingOperations.GetBinding(combo, ComboBox.TextProperty);
                if (binding?.Path?.Path == nameof(UiNodeViewModel.Sprite)) spriteCombo = true;
            });

            Assert.False(spriteCombo, "the sprite field is a dropdown again");
        });
    }
}
