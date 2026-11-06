using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Tutorials;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The UI tab in a real window: what is on screen, where it is measured to be,
/// and what its ways in actually do. Run in both looks by run-tests.ps1 -WithEx.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UiTabLayoutTests
{
    private readonly ITestOutputHelper _out;
    public UiTabLayoutTests(ITestOutputHelper output) => _out = output;

    private static MainViewModel Open(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        tabs.SelectedIndex = MainTab.Ui;
        WindowHarness.Pump();
        return (MainViewModel)window.DataContext;
    }

    private static T Named<T>(MainWindow window, string name) where T : class
        => (T)window.FindName(name);

    private static bool Shown(MainWindow window, string name)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        return Named<FrameworkElement>(window, name).IsVisible;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void With_nothing_selected_it_opens_on_what_to_do()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);
            vm.SelectedUi = null;

            Assert.True(Shown(window, "UiStartPage"));
            Assert.False(Shown(window, "UiPreviewColumn"));
            Assert.False(Shown(window, "UiInspector"));

            vm.StartWindowUiCommand.Execute(null);
            Assert.False(Shown(window, "UiStartPage"));     // the control
            Assert.True(Shown(window, "UiPreviewColumn"));
            Assert.True(Shown(window, "UiInspector"));

            // A pack with UIs still opens here, the list being one more way to begin.
            vm.SelectedUi = null;
            Assert.True(Shown(window, "UiStartPage"));
            Assert.True(vm.HasUis);
        });
    }

    [Fact]
    public void Each_way_in_does_what_it_says()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);

            void Press(string name)
            {
                vm.SelectedUi = null;
                WindowHarness.Pump();
                Named<Button>(window, name).Command.Execute(null);
                WindowHarness.Pump();
            }

            // Something that stays on screen: up from the start, nothing to arrive.
            Press("UiStartStaying");
            var staying = vm.SelectedUi!;
            Assert.True(staying.StartsOpen);
            Assert.Null(staying.Model.Open);
            Assert.False(staying.IsVanillaBased);
            Assert.NotEmpty(staying.Nodes);

            // A window that opens: shut until something opens it, and its close
            // button does something.
            Press("UiStartWindow");
            var windowUi = vm.SelectedUi!;
            Assert.False(windowUi.StartsOpen);
            var close = windowUi.Nodes[0].Children.Single(c => c.Name == UiTemplate.CloseButtonName);
            var action = Assert.Single(close.Model.OnClick);
            Assert.Equal(NodeActionTypes.SetGameObjectActive, action.Type);
            Assert.Equal("UI", action.Params["kind"]);
            Assert.Equal(windowUi.Model.Id, action.Params["target"]);
            Assert.Equal("false", action.Params["active"]);

            // The control: a shape with no close button gets no action.
            Assert.DoesNotContain(staying.Nodes[0].Children, c => c.Model.OnClick.Count > 0);

            // One of the game's: asks which, and offers nothing that belongs to a
            // screen of the pack's own.
            Press("UiStartChange");
            var game = vm.SelectedUi!;
            Assert.True(game.WantsVanilla);
            Assert.True(Shown(window, "UiScreenPickerRow"));
            Assert.False(Shown(window, "UiOwnSettings"));

            // The tutorial it offers exists.
            Assert.Contains(TutorialCatalog.All, t => t.Id == "a-screen-of-your-own");
            Assert.NotNull(Named<Button>(window, "UiStartTutorial").Command);
        });
    }

    [Fact]
    public void It_reads_left_to_right_and_the_picture_gets_the_room()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);
            vm.StartWindowUiCommand.Execute(null);
            WindowHarness.Pump();
            window.UpdateLayout();

            double Left(string name)
                => Named<FrameworkElement>(window, name).TranslatePoint(new Point(0, 0), window).X;
            double Width(string name) => Named<FrameworkElement>(window, name).ActualWidth;

            _out.WriteLine($"list {Left("UiList")}, tree {Left("UiTree")}, preview {Left("UiScreenPreview")} ({Width("UiScreenPreview")} wide), panel {Left("UiInspector")} ({Width("UiInspector")} wide)");
            Assert.True(Left("UiTree") < Left("UiScreenPreview"));
            Assert.True(Left("UiScreenPreview") < Left("UiInspector"));
            Assert.True(Width("UiScreenPreview") > Width("UiTree"));
            Assert.True(Width("UiScreenPreview") > Width("UiInspector"));

            // Nothing runs off the window's edge.
            var panel = Named<FrameworkElement>(window, "UiInspector");
            double right = panel.TranslatePoint(new Point(panel.ActualWidth, 0), window).X;
            Assert.True(right <= window.ActualWidth + 1, $"the panel ends at {right} of {window.ActualWidth}");
        });
    }

    [Fact]
    public void A_ui_of_the_packs_own_has_no_screen_picker_and_one_of_the_games_has_no_name_box()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);
            vm.AddOwnUiCommand.Execute(null);
            // The label and the box both: it used to leave a dropdown nobody could
            // explain on every screen of a pack's own.
            Assert.False(Shown(window, "UiScreenPickerRow"));
            Assert.True(Shown(window, "UiOwnSettings"));

            vm.AddVanillaUiCommand.Execute(null);
            Assert.True(Shown(window, "UiScreenPickerRow"));   // the control
            Assert.False(Shown(window, "UiOwnSettings"));
        });
    }

    [Fact]
    public void The_world_map_shows_as_itself_in_its_own_dropdown()
    {
        WindowHarness.Run(window =>
        {
            if (!VanillaUiLibrary.IsAvailable) return;
            const string token = MapButtonDef.WorldMapScreen;
            if (VanillaUiCatalog.Find(token) == null) return;

            var vm = Open(window);
            vm.AddVanillaUiCommand.Execute(null);
            var map = vm.SelectedUi!;
            map.Source = token;
            vm.AddOwnUiCommand.Execute(null);
            var own = vm.SelectedUi!;
            WindowHarness.Pump();

            // The way an author arrives at it: from another row, or from nothing, as
            // opening a pack does. Each time the list it is chosen from is rebuilt
            // for the row being looked at.
            var row = Named<FrameworkElement>(window, "UiScreenPickerRow");
            foreach (var from in new[] { own, null })
            {
                vm.SelectedUi = from;
                WindowHarness.Pump();
                vm.SelectedUi = map;
                WindowHarness.Pump();
                window.UpdateLayout();

                var box = Descendants<ComboBox>(row).Single();
                _out.WriteLine($"from {from?.Name ?? "nothing"}: selected {box.SelectedValue}, shows '{box.Text}', source {map.Source}");
                Assert.Equal(token, box.SelectedValue);
                Assert.NotNull(box.SelectedItem);
                Assert.Equal(token, map.Source);     // and looking at it changed nothing
                Assert.NotEmpty(map.Nodes);
            }

            // The control: another row's dropdown does not offer it, a screen
            // being one row's to change.
            vm.AddVanillaUiCommand.Execute(null);
            var other = vm.SelectedUi!;
            WindowHarness.Pump();
            Assert.DoesNotContain(vm.AvailableUiScreens, b => b.Token == token);

            // And going between two of the game's leaves each with its own: the
            // one being left must not take the list changing for a new choice.
            const string quit = "vanillaui:9_MainCanvas/Quitagme";
            other.Source = quit;
            WindowHarness.Pump();
            for (int i = 0; i < 3; i++)
            {
                vm.SelectedUi = map;
                WindowHarness.Pump();
                Assert.Equal(token, Descendants<ComboBox>(row).Single().SelectedValue);
                vm.SelectedUi = other;
                WindowHarness.Pump();
                Assert.Equal(quit, Descendants<ComboBox>(row).Single().SelectedValue);
            }
            Assert.Equal(token, map.Source);
            Assert.Equal(quit, other.Source);
        });
    }

    private static TreeViewItem? Row(ItemsControl parent, object item)
    {
        WindowHarness.Pump();
        parent.UpdateLayout();
        return parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
    }

    [Fact]
    public void The_tree_opens_at_the_top_and_shows_whatever_is_added()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);
            vm.StartWindowUiCommand.Execute(null);
            var tree = Named<TreeView>(window, "UiTree");
            var ui = vm.SelectedUi!;
            WindowHarness.Pump();

            var root = ui.Nodes[0];
            var rootRow = Row(tree, root);
            Assert.NotNull(rootRow);
            Assert.True(rootRow!.IsExpanded);
            var close = root.Children.Single(c => c.Name == UiTemplate.CloseButtonName);
            var closeRow = Row(rootRow, close);
            Assert.NotNull(closeRow);                 // its children are on show
            Assert.False(closeRow!.IsExpanded);       // the control: this one starts closed

            // Added inside a closed branch: it opens, and the new row is the selected one.
            ui.SelectedNode = close;
            ui.AddChildCommand.Execute(null);
            var made = ui.SelectedNode!;
            Assert.NotSame(close, made);
            Assert.True(close.IsExpanded);
            var madeRow = Row(closeRow, made);
            Assert.NotNull(madeRow);
            Assert.True(madeRow!.IsSelected);
            Assert.Same(made, tree.SelectedItem);
        });
    }

    [Fact]
    public void The_settings_row_goes_back_to_the_ui_as_a_whole()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);
            vm.StartWindowUiCommand.Execute(null);
            var ui = vm.SelectedUi!;
            var tree = Named<TreeView>(window, "UiTree");
            WindowHarness.Pump();

            Assert.True(Shown(window, "UiSettingsFields"));
            Assert.False(Shown(window, "UiNodeFields"));

            ui.SelectedNode = ui.Nodes[0];
            Assert.False(Shown(window, "UiSettingsFields"));
            Assert.True(Shown(window, "UiNodeFields"));
            Assert.Same(ui.Nodes[0], tree.SelectedItem);

            Named<Button>(window, "UiSettingsRow").Command.Execute(null);
            Assert.Null(ui.SelectedNode);
            Assert.True(Shown(window, "UiSettingsFields"));
            Assert.False(Shown(window, "UiNodeFields"));
            Assert.Null(tree.SelectedItem);

            // And back: the row can be chosen again.
            ui.SelectedNode = ui.Nodes[0];
            Assert.True(Shown(window, "UiNodeFields"));
            Assert.Same(ui.Nodes[0], tree.SelectedItem);
        });
    }

    [Fact]
    public void Sections_stay_as_the_author_left_them_going_from_object_to_object()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);
            vm.StartWindowUiCommand.Execute(null);
            var ui = vm.SelectedUi!;
            var clicks = Named<Expander>(window, "UiSectionClicks");
            var plain = ui.Nodes[0].Children.First(c => c.Name != UiTemplate.CloseButtonName);
            var clickable = ui.Nodes[0].Children.Single(c => c.Name == UiTemplate.CloseButtonName);
            Assert.True(clickable.IsClickable);

            ui.SelectedNode = plain;
            WindowHarness.Pump();
            Assert.False(clicks.IsExpanded);

            // Used to be bound to what the object held, so clicking from one object
            // to another rearranged the panel under the pointer.
            ui.SelectedNode = clickable;
            WindowHarness.Pump();
            Assert.False(clicks.IsExpanded);

            clicks.IsExpanded = true;
            ui.SelectedNode = plain;
            WindowHarness.Pump();
            Assert.True(clicks.IsExpanded);

            // Where it sits and how it looks start open: the control.
            Assert.True(Named<Expander>(window, "UiSectionWhere").IsExpanded);
            Assert.True(Named<Expander>(window, "UiSectionLooks").IsExpanded);
        });
    }

    [Fact]
    public void The_stick_grid_is_nine_places_that_light_up_and_write_through()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);
            vm.StartWindowUiCommand.Execute(null);
            var ui = vm.SelectedUi!;
            var node = ui.Nodes[0].Children.First(c => c.Name != UiTemplate.CloseButtonName);
            ui.SelectedNode = node;
            WindowHarness.Pump();
            window.UpdateLayout();

            var grid = Named<ItemsControl>(window, "UiStickGrid");
            var cells = Descendants<Button>(grid).ToList();
            Assert.Equal(9, cells.Count);

            // Laid out three by three, top left first, in the order the cells are.
            var first = cells[0].TranslatePoint(new Point(0, 0), grid);
            var ninth = cells[8].TranslatePoint(new Point(0, 0), grid);
            Assert.True(ninth.X > first.X && ninth.Y > first.Y);
            Assert.True(cells[0].ActualWidth >= 15, $"cells are {cells[0].ActualWidth} wide");

            cells[8].Command.Execute(null);      // bottom right
            WindowHarness.Pump();
            Assert.True(node.StickCells[8].IsOn);
            Assert.False(node.StickCells[0].IsOn);
            Assert.Equal(1f, node.AnchorMinX, 3);
            Assert.Equal(0f, node.AnchorMinY, 3);
        });
    }

    [Fact]
    public void A_rows_eye_hides_it_in_the_preview_and_changes_nothing_else()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);
            vm.StartWindowUiCommand.Execute(null);
            var ui = vm.SelectedUi!;
            var tree = Named<TreeView>(window, "UiTree");
            WindowHarness.Pump();

            var root = ui.Nodes[0];
            var row = root.Children.First(c => c.Name != UiTemplate.CloseButtonName);
            var chosen = root.Children.Single(c => c.Name == UiTemplate.CloseButtonName);
            ui.SelectedNode = chosen;
            WindowHarness.Pump();
            var rowItem = Row(Row(tree, root)!, row)!;
            var eye = Descendants<Button>(rowItem).First(b => b.Name == "UiRowEye");
            Assert.True(eye.IsVisible);

            // Nothing is said while nothing is hidden: the control.
            Assert.False(Shown(window, "UiPreviewHiddenNote"));

            vm.Undo.Checkpoint();
            bool unsaved = vm.HasUnsavedChanges;

            // Pressed as the mouse does: the row under it is not selected.
            eye.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            });
            eye.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent,
            });
            WindowHarness.Pump();
            if (!row.IsHiddenInPreview) eye.Command.Execute(null);   // no mouse on the run's desktop to capture

            Assert.True(row.IsHiddenInPreview);
            Assert.Same(chosen, ui.SelectedNode);
            Assert.Equal(unsaved, vm.HasUnsavedChanges);

            // The preview says so, and Show all brings it back.
            Assert.True(Shown(window, "UiPreviewHiddenNote"));
            Named<Button>(window, "UiPreviewShowAll").Command.Execute(null);
            Assert.False(row.IsHiddenInPreview);
            Assert.False(Shown(window, "UiPreviewHiddenNote"));
        });
    }

    [Fact]
    public void What_is_hidden_outlasts_an_undo_which_makes_every_row_again()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);
            vm.StartWindowUiCommand.Execute(null);
            WindowHarness.Pump();
            var ui = vm.SelectedUi!;
            string name = ui.Nodes[0].Children.First(c => c.Name != UiTemplate.CloseButtonName).Name;
            var row = ui.Nodes[0].Children.First(c => c.Name == name);
            row.IsHiddenInPreview = true;

            vm.Undo.Checkpoint();
            row.PositionX = 123;
            vm.Undo.Checkpoint();
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            var again = vm.Uis.Single().Nodes[0].Children.First(c => c.Name == name);
            Assert.NotSame(row, again);                // the control: it really was made again
            Assert.NotEqual(123f, again.PositionX);
            Assert.True(again.IsHiddenInPreview);
        });
    }

    [Fact]
    public void An_undo_takes_one_click_on_the_grid_back()
    {
        WindowHarness.Run(window =>
        {
            var vm = Open(window);
            vm.StartWindowUiCommand.Execute(null);
            var ui = vm.SelectedUi!;
            var name = ui.Nodes[0].Children.First(c => c.Name != UiTemplate.CloseButtonName).Name;
            ui.SelectedNode = ui.Nodes[0].Children.First(c => c.Name == name);
            WindowHarness.Pump();

            float before = ui.SelectedNode!.AnchorMinX;
            ui.SelectedNode.StickCells[8].Pick.Execute(null);
            Assert.Equal(1f, ui.SelectedNode.AnchorMinX, 3);
            WindowHarness.Pump();

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            var back = vm.SelectedUi!.Nodes[0].Children.First(c => c.Name == name);
            Assert.Equal(before, back.AnchorMinX, 3);
        });
    }
}
