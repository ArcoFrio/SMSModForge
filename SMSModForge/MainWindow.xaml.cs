using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SMSModForge.Validation;
using SMSModForge.View;
using SMSModForge.ViewModel;

namespace SMSModForge;

public partial class MainWindow : Window
{
    // Track mask editors by outfit so we don't open two for the same one.
    private readonly Dictionary<OutfitViewModel, MaskEditorWindow> _maskEditors = new();

    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is MainViewModel vm)
        {
            vm.Saved += OnPackSaved;
            RegisterShortcuts(vm);
            // Checkpoint the undo history whenever a field loses focus — this
            // collapses a field's typing into a single undo step (committed when
            // you move off it). handledEventsToo so it fires even when inner
            // controls mark the event handled.
            AddHandler(LostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(OnAnyLostKeyboardFocus), handledEventsToo: true);
        }
        RestoreUiLayout();
    }

    /// <summary>Restore user-adjusted resizable column widths from the last session.</summary>
    private void RestoreUiLayout()
    {
        var sizes = SMSModForge.Services.UiLayoutService.Load();
        if (sizes.TryGetValue("DlgListCol", out var listW) && listW > 0)
            DlgListCol.Width = new GridLength(listW);
        if (sizes.TryGetValue("DlgMiddleCol", out var midW) && midW > 0)
            DlgMiddleCol.Width = new GridLength(midW);
    }

    /// <summary>Persist the resizable column widths so the layout survives a restart.</summary>
    private void SaveUiLayout()
    {
        var sizes = new Dictionary<string, double>();
        if (DlgListCol.ActualWidth > 0) sizes["DlgListCol"] = DlgListCol.ActualWidth;
        if (DlgMiddleCol.ActualWidth > 0) sizes["DlgMiddleCol"] = DlgMiddleCol.ActualWidth;
        if (sizes.Count > 0) SMSModForge.Services.UiLayoutService.Save(sizes);
    }

    /// <summary>
    /// Commits the leaving field's edit (for LostFocus-bound boxes) and snapshots
    /// the undo history. Keeps text-field edits as one undo step each.
    /// </summary>
    private void OnAnyLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.OldFocus is System.Windows.Controls.TextBox tb)
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        vm.Undo.Checkpoint();
    }

    /// <summary>
    /// Wires the File-menu accelerators (Ctrl+N/O/S, Ctrl+Shift+S, Ctrl+E, F5) to
    /// their commands. The menu items only carry <c>InputGestureText</c>, which is
    /// display-only — it shows the hint but registers nothing. WPF
    /// <see cref="KeyBinding"/>s don't inherit DataContext either, so a XAML
    /// <c>{Binding}</c> on their Command would bind to null; registering them here
    /// against the VM directly is what actually makes the shortcuts fire.
    /// </summary>
    private void RegisterShortcuts(MainViewModel vm)
    {
        void Bind(ICommand command, Key key, ModifierKeys modifiers)
            => InputBindings.Add(new KeyBinding(command, key, modifiers));

        // Keyboard shortcuts don't move focus, so a LostFocus-bound field being
        // edited wouldn't have committed yet. Flush first for anything that
        // reads/persists the model. (Menu clicks already move focus, so they're
        // fine without this.)
        void BindFlush(RelayCommand command, Key key, ModifierKeys modifiers)
            => InputBindings.Add(new KeyBinding(
                new RelayCommand(() => { CommitPendingEdits(); command.Execute(null); },
                                 () => command.CanExecute(null)),
                key, modifiers));

        Bind(vm.NewPackCommand,      Key.N,  ModifierKeys.Control);
        Bind(vm.OpenPackCommand,     Key.O,  ModifierKeys.Control);
        BindFlush(vm.SavePackCommand,   Key.S,  ModifierKeys.Control);
        BindFlush(vm.SavePackAsCommand, Key.S,  ModifierKeys.Control | ModifierKeys.Shift);
        BindFlush(vm.ExportPackCommand, Key.E,  ModifierKeys.Control);
        Bind(vm.ValidateCommand,     Key.F5, ModifierKeys.None);
        Bind(vm.DuplicateItemCommand, Key.D, ModifierKeys.Control);

        // Undo/redo. BindFlush commits the focused field first so a not-yet-left
        // edit is captured before the snapshot; the command itself checkpoints
        // (via RelayCommand.Executing) then undoes/redoes.
        BindFlush(vm.UndoCommand, Key.Z, ModifierKeys.Control);
        BindFlush(vm.RedoCommand, Key.Y, ModifierKeys.Control);
    }

    /// <summary>
    /// Commits the focused control's in-progress edit to its bound source. A
    /// TextBox with the default LostFocus trigger (e.g. the node Timeout box)
    /// hasn't written its text to the model while it still has keyboard focus;
    /// without this, a Ctrl+S / close that doesn't move focus would persist the
    /// stale value. Safe to call when nothing relevant is focused.
    /// </summary>
    private static void CommitPendingEdits()
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox tb)
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
    }

    /// <summary>
    /// Flashes the top-right "Saved" toast: a quick fade-in, a short hold, then a
    /// fade-out. Driven entirely from code so it stays self-contained (no XAML
    /// trigger plumbing). The Saved event is raised on the UI thread, so touching
    /// <c>SaveToast</c> directly here is safe.
    /// </summary>
    private void OnPackSaved(object? sender, EventArgs e)
    {
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0))));
        SaveToast.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Guards against losing work: if the pack has unsaved edits, offer
    /// Save / Don't Save / Cancel before the window closes. Save runs the
    /// normal save command (which may open a Save-As dialog for a never-saved
    /// pack); if that's cancelled or fails, the changes are still pending, so
    /// we cancel the close too rather than silently discarding them.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // Commit any field being edited first, so the dirty-check sees it and a
        // subsequent save persists it (the X / Alt+F4 don't move focus).
        CommitPendingEdits();
        SaveUiLayout();   // remember resizable column widths for next session

        if (DataContext is MainViewModel vm && vm.HasUnsavedChanges)
        {
            var choice = MessageBox.Show(this,
                "You have unsaved changes. Save before closing?",
                "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            switch (choice)
            {
                case MessageBoxResult.Yes:
                    vm.SavePackCommand.Execute(null);
                    if (vm.HasUnsavedChanges) e.Cancel = true; // save cancelled / failed
                    break;
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    break;
                // No → fall through and close, discarding changes.
            }
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// TreeView's SelectedItem is read-only — bouncing it through the VM
    /// requires a code-behind nudge. Only outfit selection drives the editor;
    /// selecting a character is a no-op (the user can expand it).
    /// </summary>
    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.NewValue is OutfitViewModel outfit)
            vm.SelectedOutfit = outfit;
        // Selecting a character shows its default (first) outfit in the preview,
        // so you don't have to drill into an outfit just to see the bust.
        else if (e.NewValue is CharacterViewModel ch && ch.Outfits.Count > 0)
            vm.SelectedOutfit = ch.Outfits[0];
    }

    private void EditMask_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var outfit = vm.SelectedOutfit;
        if (outfit is null) return;
        if (string.IsNullOrWhiteSpace(vm.PackRoot))
        {
            MessageBox.Show(this,
                "Save the pack to disk first — the mask editor needs a folder to read the diffuse from and to save the mask into.",
                "Mask Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // If one is already open for this outfit, bring it forward.
        if (_maskEditors.TryGetValue(outfit, out var existing))
        {
            existing.Activate();
            return;
        }

        var win = new MaskEditorWindow(outfit, vm.PackRoot) { Owner = this };
        _maskEditors[outfit] = win;
        win.Closed += (_, _) => _maskEditors.Remove(outfit);
        win.Show();
    }

    /// <summary>
    /// Map-button rows don't sit in a ListBox (each row is its own inline
    /// editor), so we bounce row clicks through the VM to drive
    /// <see cref="MainViewModel.SelectedMapButton"/> — the toolbar's
    /// "Remove selected" command keys off that selection.
    /// </summary>
    private void MapButtonRow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MapButtonViewModel vm &&
            DataContext is MainViewModel main)
        {
            main.SelectedMapButton = vm;
        }
    }

    /// <summary>
    /// Pops the Win32 colour-picker dialog seeded with the actor's
    /// current name colour, and writes the chosen colour back as a
    /// <c>#RRGGBB</c> hex string. Bypassing a third-party colour-picker
    /// keeps the editor's package surface down to just Newtonsoft.Json.
    /// </summary>
    private void PickActorColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: ActorViewModel actor }) return;
        var current = actor.NameColorValue;
        var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = false,
            Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B),
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var picked = dlg.Color;
        actor.NameColorValue = System.Windows.Media.Color.FromArgb(picked.A, picked.R, picked.G, picked.B);
    }

    /// <summary>
    /// Prompt for a custom roomtalk name and point the current dialogue at it
    /// (vanilla:&lt;name&gt;, created on the fly at runtime). Mirrors the host's
    /// CreateNewRoomTalk; registered pack-side so the picker + validator know it.
    /// </summary>
    private void NewRoomTalk_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedDialogue is null) return;
        var name = TextPromptWindow.Prompt(this, "New RoomTalk",
            "Name for the new custom roomtalk. The dialogue will use vanilla:<name>, " +
            "and the runtime creates the roomtalk node on the fly.");
        if (!string.IsNullOrWhiteSpace(name))
            vm.AddCustomRoomTalk(name);
    }

    // ── Dialogue folder tree: selection, drag-drop, rename ───────────────

    private void DialogueTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedDialogueTreeItem = e.NewValue as DialogueTreeItem;
    }

    private System.Windows.Point _treeDragStart;
    private DialogueTreeItem? _treeDragItem;

    private void DialogueTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _treeDragStart = e.GetPosition(null);
        _treeDragItem = FindTreeItemData(e.OriginalSource as DependencyObject);
    }

    private void DialogueTree_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _treeDragItem == null) return;
        var pos = e.GetPosition(null);
        if (System.Math.Abs(pos.X - _treeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(pos.Y - _treeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop(DialogueTreeView, new DataObject("DialogueTreeItem", _treeDragItem), DragDropEffects.Move);
        _treeDragItem = null;
    }

    private void DialogueTree_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Data.GetData("DialogueTreeItem") is not DialogueTreeItem dragged) return;
        vm.MoveTreeItem(dragged, FindTreeItemData(e.OriginalSource as DependencyObject));
        e.Handled = true;
    }

    /// <summary>The tree node data under a hit element (walking up to its TreeViewItem), or null.</summary>
    private static DialogueTreeItem? FindTreeItemData(DependencyObject? src)
    {
        while (src != null && src is not System.Windows.Controls.TreeViewItem)
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        return (src as System.Windows.Controls.TreeViewItem)?.DataContext as DialogueTreeItem;
    }

    private void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.SelectedDialogueTreeItem is not DialogueFolderNode folder) return;
        var name = TextPromptWindow.Prompt(this, "Rename Folder", "Folder name:", folder.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        vm.Undo.Checkpoint();
        folder.Name = name.Trim();
        vm.SyncFoldersToModel();
    }

    // ── Variable folder tree: selection, drag-drop, rename (mirrors dialogues) ──

    private void VariableTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedVariableTreeItem = e.NewValue as VariableTreeItem;
    }

    private System.Windows.Point _varTreeDragStart;
    private VariableTreeItem? _varTreeDragItem;

    private void VariableTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _varTreeDragStart = e.GetPosition(null);
        _varTreeDragItem = FindVariableTreeItemData(e.OriginalSource as DependencyObject);
    }

    private void VariableTree_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _varTreeDragItem == null) return;
        var pos = e.GetPosition(null);
        if (System.Math.Abs(pos.X - _varTreeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(pos.Y - _varTreeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop(VariableTreeView, new DataObject("VariableTreeItem", _varTreeDragItem), DragDropEffects.Move);
        _varTreeDragItem = null;
    }

    private void VariableTree_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Data.GetData("VariableTreeItem") is not VariableTreeItem dragged) return;
        vm.MoveVariableTreeItem(dragged, FindVariableTreeItemData(e.OriginalSource as DependencyObject));
        e.Handled = true;
    }

    private static VariableTreeItem? FindVariableTreeItemData(DependencyObject? src)
    {
        while (src != null && src is not System.Windows.Controls.TreeViewItem)
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        return (src as System.Windows.Controls.TreeViewItem)?.DataContext as VariableTreeItem;
    }

    private void RenameVariableFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.SelectedVariableTreeItem is not VariableFolderNode folder) return;
        var name = TextPromptWindow.Prompt(this, "Rename Folder", "Folder name:", folder.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        vm.Undo.Checkpoint();
        folder.Name = name.Trim();
        vm.SyncVariableFoldersToModel();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Issue navigation. Double-clicking a validation issue jumps to the tab
    // and selects the exact thing it refers to, then flashes it. The issue's
    // Where string is the source of truth, e.g.:
    //   dialogues[Key].nodes[id=-5].SetGameObjectActive
    //   dialogues[Key].roomTalk
    //   places[Key].baseSprite | actors[Key] | variables[Name]
    //   characters[Name].outfits[OutfitKey].mouth[2]
    //   vanillaExtensions[3:Beach] | mapButtons[1:District→Target]
    // ──────────────────────────────────────────────────────────────────────

    private const int TabBusts = 0, TabPlaces = 1, TabMapButtons = 2,
                      TabDialogues = 3, TabActors = 4, TabVariables = 6;

    private void IssueList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssueList.SelectedItem is ValidationIssue issue)
            NavigateToIssue(issue);
    }

    private void NavigateToIssue(ValidationIssue issue)
    {
        if (DataContext is not MainViewModel vm) return;
        var where = issue.Where ?? "";
        var head = Regex.Match(where, @"^(?<g>[A-Za-z]+)\[(?<inner>[^\]]*)\]");
        if (!head.Success) return;   // "$.packId" and the like: no per-item target
        string inner = head.Groups["inner"].Value;
        string field = ExtractField(where);   // the offending field, e.g. "defaultBustKey"

        switch (head.Groups["g"].Value)
        {
            case "dialogues":
            {
                MainTabs.SelectedIndex = TabDialogues;
                var dlg = vm.Dialogues.FirstOrDefault(d => d.Key == inner);
                if (dlg == null) return;
                vm.SelectedDialogue = dlg;

                var nodeM = Regex.Match(where, @"nodes\[id=(?<id>-?\d+)\]");
                if (nodeM.Success && int.TryParse(nodeM.Groups["id"].Value, out var nid))
                {
                    var node = dlg.Nodes.FirstOrDefault(n => n.Id == nid);
                    if (node != null)
                    {
                        // Defer: the node list rebinds to the new dialogue first,
                        // then the node editor binds to SelectedNode.
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            vm.SelectedNode = node;
                            Reveal(field, NodeList, node);
                        }), System.Windows.Threading.DispatcherPriority.Background);
                        return;
                    }
                }
                Reveal(field, null, null);   // roomTalk / node fields flash; editor already shows SelectedDialogue
                break;
            }
            case "places":
            {
                MainTabs.SelectedIndex = TabPlaces;
                var pl = vm.Places.FirstOrDefault(p => p.Key == inner);
                if (pl == null) return;
                vm.SelectedPlace = pl;
                Reveal(field, PlaceList, pl);
                break;
            }
            case "vanillaExtensions":
            {
                MainTabs.SelectedIndex = TabPlaces;
                if (TryLeadingIndex(inner, vm.VanillaExtensions.Count, out var i))
                {
                    vm.SelectedVanillaExtension = vm.VanillaExtensions[i];
                    Reveal(field, VanillaExtList, vm.VanillaExtensions[i]);
                }
                break;
            }
            case "mapButtons":
            {
                MainTabs.SelectedIndex = TabMapButtons;
                if (TryLeadingIndex(inner, vm.MapButtons.Count, out var i))
                {
                    var mb = vm.MapButtons[i];
                    vm.SelectedMapButton = mb;
                    // Per-row editors share field tags, so a tree walk would hit the
                    // wrong row — just flash the selected row.
                    Reveal("", MapButtonList, mb, scroll: false);
                }
                break;
            }
            case "actors":
            {
                MainTabs.SelectedIndex = TabActors;
                var ac = vm.Actors.FirstOrDefault(a => a.Key == inner);
                if (ac == null) return;
                vm.SelectedActor = ac;
                Reveal(field, ActorList, ac);
                break;
            }
            case "variables":
            {
                MainTabs.SelectedIndex = TabVariables;
                var va = vm.Variables.FirstOrDefault(v => v.Name == inner);
                if (va == null) return;
                vm.SelectedVariable = va;
                Reveal(field, null, null);   // editor already shows SelectedVariable
                break;
            }
            case "characters":
            {
                MainTabs.SelectedIndex = TabBusts;
                var ch = vm.Characters.FirstOrDefault(c => c.Name == inner);
                if (ch == null) return;
                var outfitM = Regex.Match(where, @"outfits\[(?<k>[^\]]+)\]");
                var outfit = outfitM.Success
                    ? ch.Outfits.FirstOrDefault(o => o.Key == outfitM.Groups["k"].Value)
                    : null;
                // Defer: lets the Busts tab content + tree containers realize, and
                // the outfit editor bind to the selection, before we look for the field.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var treeItem = SelectInTree(ch, outfit);
                    if (FindIssueElement(field) is FrameworkElement fe)
                    {
                        fe.BringIntoView();
                        FlashElement(fe);
                    }
                    else
                    {
                        FlashElement(treeItem);   // fall back to the outfit/character row
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
                break;
            }
        }
    }

    /// <summary>
    /// The offending field token, relative to the most specific item the issue
    /// names: everything after <c>group[key]</c> and any <c>.outfits[…]</c> /
    /// <c>.nodes[id=…]</c> sub-item identifier. e.g.
    /// <c>actors[K].defaultBustKey</c> → <c>defaultBustKey</c>;
    /// <c>characters[N].outfits[K].mouth[2]</c> → <c>mouth[2]</c>;
    /// <c>dialogues[K].nodes[id=5].actor</c> → <c>actor</c>. Empty when the
    /// issue is about the item itself.
    /// </summary>
    private static string ExtractField(string where)
    {
        var rest = Regex.Replace(where, @"^[A-Za-z]+\[[^\]]*\]", "");
        rest = Regex.Replace(rest, @"^\.outfits\[[^\]]*\]", "");
        rest = Regex.Replace(rest, @"^\.nodes\[id=[^\]]*\]", "");
        return rest.TrimStart('.');
    }

    /// <summary>Parses the leading integer of an issue index token ("3" in "3:Beach").</summary>
    private static bool TryLeadingIndex(string inner, int count, out int index)
    {
        index = -1;
        return int.TryParse(inner.Split(':')[0], out index) && index >= 0 && index < count;
    }

    /// <summary>
    /// Flash the exact offending field if one is tagged in the active tab
    /// (see <see cref="View.IssueTarget"/>); otherwise fall back to the item's
    /// row/container. Deferred so the tab switch + selection have laid out.
    /// </summary>
    private void Reveal(string field, System.Windows.Controls.ItemsControl? list, object? item, bool scroll = true)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (scroll && list is System.Windows.Controls.ListBox lb && item != null)
            {
                lb.ScrollIntoView(item);
                lb.UpdateLayout();
            }

            // 1) the precise field / list-row, if locatable in the active tab
            if (FindIssueElement(field) is FrameworkElement fe)
            {
                fe.BringIntoView();
                FlashElement(fe);
                return;
            }
            // 2) fall back to the item's row/container
            if (list != null && item != null &&
                list.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement c)
                FlashElement(c);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// The element to flash for an issue's field. First a statically-tagged
    /// control (<c>actor</c>, <c>baseSprite</c>, <c>roomTalk</c>, …); failing
    /// that, the specific row in a tagged action/condition list — node actions
    /// arrive as <c>actionsOnFinish.LeaveBust</c>, start conditions as
    /// <c>startConditions.VariableEquals</c>, and a node condition as a bare
    /// type like <c>VariableEquals</c>.
    /// </summary>
    private FrameworkElement? FindIssueElement(string field)
    {
        if (string.IsNullOrEmpty(field)) return null;

        // List-row fields first: the list itself carries the tag (e.g.
        // "actionsOnFinish"), which would otherwise prefix-match the whole list.
        var m = Regex.Match(field, @"^(?<list>actionsOnStart|actionsOnFinish|startConditions)\.(?<type>.+)$");
        if (m.Success) return FindListRow(m.Groups["list"].Value, m.Groups["type"].Value);

        if (FindFieldElement(this, field) is FrameworkElement tagged) return tagged;

        // A bare token with no matching static control is a node condition type.
        if (!field.Contains('.')) return FindListRow("conditions", field);
        return null;
    }

    /// <summary>Finds the row in a tagged action/condition list whose VM type matches.</summary>
    private FrameworkElement? FindListRow(string listToken, string type)
    {
        if (FindFieldElement(this, listToken) is not System.Windows.Controls.ItemsControl list) return null;
        foreach (var item in list.Items)
        {
            string? t = item switch
            {
                NodeActionViewModel a => a.Type,
                NodeConditionViewModel c => c.Type,
                _ => null,
            };
            if (t == type && list.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement fe)
                return fe;
        }
        return null;
    }

    /// <summary>Depth-first search of the visual tree for the control tagged with a matching field token.</summary>
    private static FrameworkElement? FindFieldElement(DependencyObject root, string field)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && View.IssueTarget.GetField(fe) is string tag && FieldMatches(tag, field))
                return fe;
            if (FindFieldElement(child, field) is FrameworkElement found)
                return found;
        }
        return null;
    }

    /// <summary>A tag matches its field, or stands in for an indexed/sub-field family ("mouth" ↦ "mouth[2]", "jiggle" ↦ "jiggle.strength").</summary>
    private static bool FieldMatches(string tag, string field)
        => field == tag || field.StartsWith(tag + "[") || field.StartsWith(tag + ".");

    /// <summary>Expand the character and select the offending outfit in the tree; returns the selected tree item (for fallback flashing).</summary>
    private FrameworkElement? SelectInTree(CharacterViewModel character, OutfitViewModel? outfit)
    {
        if (CharacterTree.ItemContainerGenerator.ContainerFromItem(character)
                is not System.Windows.Controls.TreeViewItem charItem) return null;
        charItem.IsExpanded = true;
        charItem.BringIntoView();
        charItem.UpdateLayout();

        if (outfit != null &&
            charItem.ItemContainerGenerator.ContainerFromItem(outfit)
                is System.Windows.Controls.TreeViewItem outfitItem)
        {
            outfitItem.IsSelected = true;   // drives SelectedOutfit via SelectedItemChanged
            outfitItem.BringIntoView();
            return outfitItem;
        }
        charItem.IsSelected = true;
        return charItem;
    }

    /// <summary>
    /// Brief gold glow that fades over ~1.7s — draws the eye to a just-
    /// navigated-to control without touching layout or selection colours.
    /// </summary>
    private void FlashElement(UIElement? target)
    {
        if (target == null) return;
        var prev = target.Effect;
        var glow = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = System.Windows.Media.Color.FromRgb(255, 196, 40),
            ShadowDepth = 0,
            Opacity = 0.95,
            BlurRadius = 0,   // base; the animation drives it
        };
        target.Effect = glow;

        var anim = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(26, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(26, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0,  KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.7))));
        anim.Completed += (_, _) => target.Effect = prev;
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, anim);
    }

    /// <summary>
    /// When the node list can't scroll any further on its own (it's short, or
    /// already at a scroll limit), hand the wheel to the dialogue editor's
    /// outer ScrollViewer so hovering the node area still scrolls the whole tab.
    /// Otherwise the node list scrolls itself as usual.
    /// </summary>
    private void NodeList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not System.Windows.Controls.ListBox lb) return;

        var inner = FindVisualChild<System.Windows.Controls.ScrollViewer>(lb);
        bool innerCanScroll = inner != null &&
            ((e.Delta > 0 && inner.VerticalOffset > 0) ||
             (e.Delta < 0 && inner.VerticalOffset < inner.ScrollableHeight));
        if (innerCanScroll) return;

        e.Handled = true;
        if (System.Windows.Media.VisualTreeHelper.GetParent(lb) is UIElement parent)
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = lb,
            });
    }

    /// <summary>First descendant of the given type in the visual tree, or null.</summary>
    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindVisualChild<T>(child) is T found) return found;
        }
        return null;
    }
}
