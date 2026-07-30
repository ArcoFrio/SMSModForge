using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SMSModForge.Validation;
using SMSModForge.View;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;

namespace SMSModForge;

public partial class MainWindow : Window
{
    // Track mask editors by host (outfit or NPC) so we don't open two for the same one.
    private readonly Dictionary<OutfitViewModel, MaskEditorWindow> _maskEditors = new();
    private readonly Dictionary<NpcViewModel, MaskEditorWindow> _npcMaskEditors = new();

    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is MainViewModel vm)
        {
            vm.Saved += OnPackSaved;
            vm.PromptForText = (title, label, initial) => TextPromptWindow.Prompt(this, title, label, initial);
            vm.ShowInfo = (title, message) => MessageBox.Show(this, message, title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            RegisterShortcuts(vm);
            RegisterListShortcuts(vm);
            // Switching tabs unloads the outgoing tab's visual tree, and a
            // LostFocus-bound editor (the node Timeout box, an editable combo
            // mid-type) can be torn down without ever firing LostKeyboardFocus
            // — its edit would be silently discarded. Commit on the way out:
            // PreviewMouseLeftButtonDown fires before the click changes tabs,
            // and SelectionChanged is the backstop for keyboard / programmatic
            // switches. CommitPendingEdits is idempotent, so double-firing is
            // harmless.
            MainTabs.PreviewMouseLeftButtonDown += (_, __) => CommitPendingEdits();
            MainTabs.SelectionChanged += (s, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;   // ignore inner selectors
                CommitPendingEdits();
            };

            // Shared unit trees: one handler set (selection, multi-select,
            // drag-drop) wired to each — the controllers hold the behavior.
            WireUnitTree(PlaceTreeView,     vm, vm.PlaceTree);
            WireUnitTree(SceneTreeView,     vm, vm.SceneTree);
            WireUnitTree(NpcTreeView,       vm, vm.NpcTree);
            WireUnitTree(WallpaperTreeView, vm, vm.WallpaperTree);
            WireUnitTree(MusicTreeView,     vm, vm.MusicTree);
            WireUnitTree(SfxTreeView,       vm, vm.SfxTree);
            // Checkpoint the undo history whenever a field loses focus — this
            // collapses a field's typing into a single undo step (committed when
            // you move off it). handledEventsToo so it fires even when inner
            // controls mark the event handled.
            AddHandler(LostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(OnAnyLostKeyboardFocus), handledEventsToo: true);

            // The Places-tab transform gizmo edits by mouse, so it never loses
            // keyboard focus — it raises EditCommitted at the end of a drag, and
            // we snapshot the undo history in response (one step per drag).
            AddHandler(PlacePreview.EditCommittedEvent,
                new RoutedEventHandler((_, _) => vm.Undo.Checkpoint()), handledEventsToo: true);
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
        //
        // Deliberately NOT a RelayCommand: RelayCommand.Executing is static and
        // the undo service subscribes to it, so wrapping one command in another
        // fired the checkpoint twice per keypress. On Ctrl+Z that meant pushing
        // the current state and immediately undoing back to it — the first
        // press appeared to do nothing at all.
        void BindFlush(RelayCommand command, Key key, ModifierKeys modifiers)
            => InputBindings.Add(new KeyBinding(
                new PassThroughCommand(() => { CommitPendingEdits(); command.Execute(null); },
                                       () => command.CanExecute(null)),
                key, modifiers));

        Bind(vm.NewPackCommand,      Key.N,  ModifierKeys.Control);
        Bind(vm.OpenPackCommand,     Key.O,  ModifierKeys.Control);
        BindFlush(vm.SavePackCommand,   Key.S,  ModifierKeys.Control);
        BindFlush(vm.SavePackAsCommand, Key.S,  ModifierKeys.Control | ModifierKeys.Shift);
        BindFlush(vm.ExportPackCommand,   Key.E,  ModifierKeys.Control);
        BindFlush(vm.ExportPackAsCommand, Key.E,  ModifierKeys.Control | ModifierKeys.Shift);
        Bind(vm.ValidateCommand,     Key.F5, ModifierKeys.None);
        Bind(vm.DuplicateItemCommand, Key.D, ModifierKeys.Control);

        // Undo/redo. BindFlush commits the focused field first so a not-yet-left
        // edit is captured before the snapshot; the command itself checkpoints
        // (via RelayCommand.Executing) then undoes/redoes.
        BindFlush(vm.UndoCommand, Key.Z, ModifierKeys.Control);
        BindFlush(vm.RedoCommand, Key.Y, ModifierKeys.Control);
    }

    /// <summary>
    /// Hotkeys scoped to the LEFT unit lists of every tab — they only fire
    /// while the list itself has keyboard focus, so Del can't nuke a unit
    /// while the user is editing in the right pane, and Ctrl+C/V never
    /// competes with the dialogue-node canvas or a TextBox. Del = delete,
    /// F2/F12 = rename, Ctrl+C/V = copy/paste unit (Ctrl+D duplicate is
    /// window-wide, see <see cref="RegisterShortcuts"/>).
    /// </summary>
    private void RegisterListShortcuts(MainViewModel vm)
    {
        var lists = new System.Windows.Controls.Control[]
        {
            CharacterTree, PlaceTreeView, DialogueTreeView, SceneTreeView,
            VariableTreeView, WallpaperTreeView, MusicTreeView, SfxTreeView, IntegrationTreeView,
        };
        const string hint = "Del: delete • F2/F12: rename • Ctrl+C/V: copy/paste • Ctrl+D: duplicate";
        foreach (var list in lists)
        {
            list.InputBindings.Add(new KeyBinding(vm.DeleteItemCommand, Key.Delete, ModifierKeys.None));
            list.InputBindings.Add(new KeyBinding(vm.RenameItemCommand, Key.F2, ModifierKeys.None));
            list.InputBindings.Add(new KeyBinding(vm.RenameItemCommand, Key.F12, ModifierKeys.None));
            list.InputBindings.Add(new KeyBinding(vm.CopyItemCommand, Key.C, ModifierKeys.Control));
            list.InputBindings.Add(new KeyBinding(vm.PasteItemCommand, Key.V, ModifierKeys.Control));
            if (list.ToolTip == null) list.ToolTip = hint;
        }
    }

    /// <summary>
    /// One handler set for every shared unit tree (Actors / Places / Scenes /
    /// Wallpapers / Music / SFX): selection forwarding, Ctrl/Shift
    /// multi-selection and group drag-drop into folders — the same behavior
    /// the hand-rolled Dialogues / Variables / Integration trees implement,
    /// but wired once against <see cref="UnitTreeController"/>.
    /// </summary>
    private void WireUnitTree(System.Windows.Controls.TreeView tv, MainViewModel vm, UnitTreeController ctrl)
    {
        Point dragStart = default;
        UnitTreeItem? dragItem = null;

        static UnitTreeItem? ItemAt(DependencyObject? src)
        {
            while (src != null && src is not System.Windows.Controls.TreeViewItem)
                src = System.Windows.Media.VisualTreeHelper.GetParent(src);
            return (src as System.Windows.Controls.TreeViewItem)?.DataContext as UnitTreeItem;
        }

        tv.SelectedItemChanged += (_, e) => ctrl.Selected = e.NewValue as UnitTreeItem;

        tv.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStart = e.GetPosition(null);
            var item = ItemAt(e.OriginalSource as DependencyObject);

            if (item != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                item.IsMultiSelected = !item.IsMultiSelected;
                dragItem = null;
                e.Handled = true;   // anchor selection (and detail pane) stays put
                return;
            }
            if (item != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var visible = ctrl.FlattenVisible().ToList();
                int a = ctrl.Selected != null ? visible.IndexOf(ctrl.Selected) : -1;
                int b = visible.IndexOf(item);
                if (b >= 0)
                {
                    if (a < 0) a = b;
                    for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
                        visible[i].IsMultiSelected = true;
                }
                dragItem = null;
                e.Handled = true;
                return;
            }
            // Plain click: a marked item keeps the group (so a group drag can
            // start from it); anywhere else dissolves it.
            if (item == null || !item.IsMultiSelected)
                ctrl.ClearMultiSelection();

            dragItem = item;
        };

        tv.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragItem == null) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            DragDrop.DoDragDrop(tv, new DataObject("UnitTreeItem", dragItem), DragDropEffects.Move);
            dragItem = null;
        };

        tv.Drop += (_, e) =>
        {
            if (e.Data.GetData("UnitTreeItem") is not UnitTreeItem dragged) return;
            var target = ItemAt(e.OriginalSource as DependencyObject);
            // An item dragged from ANOTHER tab's tree isn't in this tree —
            // Move's parent lookup fails and it no-ops, so cross-tree drops
            // are inert rather than corrupting.
            if (dragged.IsMultiSelected) ctrl.MoveMultiSelected(target);
            else ctrl.Move(dragged, target);
            e.Handled = true;
        };
    }

    private void UnitAddFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not UnitTreeController ctrl) return;
        (DataContext as MainViewModel)?.Undo.Checkpoint();
        ctrl.AddFolder();
    }

    private void UnitRenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not UnitTreeController ctrl) return;
        if (ctrl.Selected is not UnitFolderNode folder) return;
        var name = TextPromptWindow.Prompt(this, "Rename Folder", "Folder name:", folder.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        (DataContext as MainViewModel)?.Undo.Checkpoint();
        folder.Name = name.Trim();
        ctrl.SyncToModel();
    }

    /// <summary>
    /// Commits the focused control's in-progress edit to its bound source. A
    /// TextBox with the default LostFocus trigger (e.g. the node Timeout box)
    /// hasn't written its text to the model while it still has keyboard focus;
    /// without this, a Ctrl+S / tab switch / close that doesn't move focus
    /// would persist the stale value. Safe to call when nothing relevant is
    /// focused.
    /// <para/>
    /// Also walks up to an enclosing editable ComboBox: while its edit box has
    /// focus, the focused element is the ComboBox's internal TextBox, whose
    /// binding is NOT the view-model one — the binding lives on the ComboBox's
    /// own Text property, so committing the inner box alone would be a no-op.
    /// </summary>
    /// <summary>
    /// A plain <see cref="ICommand"/> for input bindings that need to run
    /// something before delegating. Unlike <see cref="RelayCommand"/> it raises
    /// no static Executing event, so wrapping a command doesn't duplicate the
    /// undo checkpoint that event drives.
    /// </summary>
    private sealed class PassThroughCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public PassThroughCommand(Action execute, Func<bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute();
        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    private static void CommitPendingEdits()
    {
        if (Keyboard.FocusedElement is not DependencyObject focused) return;

        if (focused is System.Windows.Controls.TextBox tb)
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();

        // Walk ancestors for an editable ComboBox hosting that TextBox.
        for (DependencyObject? d = focused; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.ComboBox cb && cb.IsEditable)
            {
                cb.GetBindingExpression(System.Windows.Controls.ComboBox.TextProperty)?.UpdateSource();
                break;
            }
        }
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
        // so you don't have to drill into an outfit just to see the bust. The
        // character is set either way — one with no outfits at all is still
        // selectable, which is the whole point of a voice-only character.
        else if (e.NewValue is CharacterViewModel ch)
        {
            vm.SelectedCharacter = ch;
            if (ch.Outfits.Count > 0) vm.SelectedOutfit = ch.Outfits[0];
        }
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

    private void EditPlaceMask_Click(object sender, RoutedEventArgs e) => OpenPlaceMask(secondary: false);

    private void EditPlaceBackdropMask_Click(object sender, RoutedEventArgs e) => OpenPlaceMask(secondary: true);

    /// <summary>
    /// Open the mask editor on one of the selected place's two masks.
    /// <para/>
    /// Keyed by (place, which) rather than by the host object, because the hosts
    /// are lightweight adapters minted per call — keying on those would open a
    /// second window every time instead of raising the one already up.
    /// </summary>
    private void OpenPlaceMask(bool secondary)
    {
        if (DataContext is not MainViewModel vm) return;
        var place = vm.SelectedPlace;
        if (place is null) return;
        if (string.IsNullOrWhiteSpace(vm.PackRoot))
        {
            MessageBox.Show(this,
                "Save the pack to disk first — the mask editor needs a folder to read the level art from and to save the mask into.",
                "Mask Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var key = (place, secondary);
        if (_placeMaskEditors.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return;
        }

        var host = secondary ? place.SecondaryMaskHost : place.BaseMaskHost;
        var win = new MaskEditorWindow(host, vm.PackRoot) { Owner = this };
        _placeMaskEditors[key] = win;
        win.Closed += (_, _) => _placeMaskEditors.Remove(key);
        win.Show();
    }

    private readonly Dictionary<(PlaceViewModel Place, bool Secondary), MaskEditorWindow> _placeMaskEditors = new();

    private void EditNpcMask_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var npc = vm.SelectedNpc;
        if (npc is null) return;
        if (string.IsNullOrWhiteSpace(vm.PackRoot))
        {
            MessageBox.Show(this,
                "Save the pack to disk first — the mask editor needs a folder to read the diffuse from and to save the mask into.",
                "Mask Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // If one is already open for this NPC, bring it forward.
        if (_npcMaskEditors.TryGetValue(npc, out var existing))
        {
            existing.Activate();
            return;
        }

        var win = new MaskEditorWindow(npc, vm.PackRoot) { Owner = this };
        _npcMaskEditors[npc] = win;
        win.Closed += (_, _) => _npcMaskEditors.Remove(npc);
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

    // ── Multi-selection (Ctrl/Shift): group folder moves ────────────────
    // The TreeView's own selection stays single and keeps driving the detail
    // pane; Ctrl/Shift only mark extra items IsMultiSelected (highlighted via
    // the ItemContainerStyle), and dropping any marked item moves the whole
    // group. Marked clicks are e.Handled so the anchor never shifts.

    private static IEnumerable<DialogueTreeItem> FlattenVisibleDialogueItems(IEnumerable<DialogueTreeItem> items)
    {
        foreach (var i in items)
        {
            yield return i;
            if (i is DialogueFolderNode f && f.IsExpanded)
                foreach (var c in FlattenVisibleDialogueItems(f.Children)) yield return c;
        }
    }

    private static IEnumerable<DialogueTreeItem> FlattenAllDialogueItems(IEnumerable<DialogueTreeItem> items)
    {
        foreach (var i in items)
        {
            yield return i;
            if (i is DialogueFolderNode f)
                foreach (var c in FlattenAllDialogueItems(f.Children)) yield return c;
        }
    }

    private void ClearDialogueMultiSelection(MainViewModel vm)
    {
        foreach (var i in FlattenAllDialogueItems(vm.DialogueTree)) i.IsMultiSelected = false;
    }

    private void DialogueTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _treeDragStart = e.GetPosition(null);
        var item = FindTreeItemData(e.OriginalSource as DependencyObject);

        if (DataContext is MainViewModel vm)
        {
            if (item != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                item.IsMultiSelected = !item.IsMultiSelected;
                _treeDragItem = null;
                e.Handled = true;   // anchor selection (and detail pane) stays put
                return;
            }
            if (item != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Range over the VISIBLE order (collapsed folder contents are
                // not silently swept in), anchored at the real selection.
                var visible = FlattenVisibleDialogueItems(vm.DialogueTree).ToList();
                int a = vm.SelectedDialogueTreeItem != null ? visible.IndexOf(vm.SelectedDialogueTreeItem) : -1;
                int b = visible.IndexOf(item);
                if (b >= 0)
                {
                    if (a < 0) a = b;
                    for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
                        visible[i].IsMultiSelected = true;
                }
                _treeDragItem = null;
                e.Handled = true;
                return;
            }
            // Plain click: clicking a marked item keeps the group intact so a
            // group drag can start from it; anywhere else dissolves the group.
            if (item == null || !item.IsMultiSelected)
                ClearDialogueMultiSelection(vm);
        }

        _treeDragItem = item;
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
        var target = FindTreeItemData(e.OriginalSource as DependencyObject);

        if (dragged.IsMultiSelected)
        {
            // Group move: every marked item — except ones sitting inside a
            // marked folder, since moving the folder already carries them
            // (moving them again would pull them OUT of it).
            var group = FlattenAllDialogueItems(vm.DialogueTree).Where(i => i.IsMultiSelected).ToList();
            var folders = group.OfType<DialogueFolderNode>().ToList();
            foreach (var item in group)
            {
                if (folders.Any(f => f != item && FlattenAllDialogueItems(f.Children).Contains(item))) continue;
                vm.MoveTreeItem(item, target);
            }
            ClearDialogueMultiSelection(vm);
        }
        else
        {
            vm.MoveTreeItem(dragged, target);
        }
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

    private static IEnumerable<VariableTreeItem> FlattenVisibleVariableItems(IEnumerable<VariableTreeItem> items)
    {
        foreach (var i in items)
        {
            yield return i;
            if (i is VariableFolderNode f && f.IsExpanded)
                foreach (var c in FlattenVisibleVariableItems(f.Children)) yield return c;
        }
    }

    private static IEnumerable<VariableTreeItem> FlattenAllVariableItems(IEnumerable<VariableTreeItem> items)
    {
        foreach (var i in items)
        {
            yield return i;
            if (i is VariableFolderNode f)
                foreach (var c in FlattenAllVariableItems(f.Children)) yield return c;
        }
    }

    private void ClearVariableMultiSelection(MainViewModel vm)
    {
        foreach (var i in FlattenAllVariableItems(vm.VariableTree)) i.IsMultiSelected = false;
    }

    private void VariableTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _varTreeDragStart = e.GetPosition(null);
        var item = FindVariableTreeItemData(e.OriginalSource as DependencyObject);

        if (DataContext is MainViewModel vm)
        {
            if (item != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                item.IsMultiSelected = !item.IsMultiSelected;
                _varTreeDragItem = null;
                e.Handled = true;
                return;
            }
            if (item != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var visible = FlattenVisibleVariableItems(vm.VariableTree).ToList();
                int a = vm.SelectedVariableTreeItem != null ? visible.IndexOf(vm.SelectedVariableTreeItem) : -1;
                int b = visible.IndexOf(item);
                if (b >= 0)
                {
                    if (a < 0) a = b;
                    for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
                        visible[i].IsMultiSelected = true;
                }
                _varTreeDragItem = null;
                e.Handled = true;
                return;
            }
            if (item == null || !item.IsMultiSelected)
                ClearVariableMultiSelection(vm);
        }

        _varTreeDragItem = item;
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
        var target = FindVariableTreeItemData(e.OriginalSource as DependencyObject);

        if (dragged.IsMultiSelected)
        {
            var group = FlattenAllVariableItems(vm.VariableTree).Where(i => i.IsMultiSelected).ToList();
            var folders = group.OfType<VariableFolderNode>().ToList();
            foreach (var item in group)
            {
                if (folders.Any(f => f != item && FlattenAllVariableItems(f.Children).Contains(item))) continue;
                vm.MoveVariableTreeItem(item, target);
            }
            ClearVariableMultiSelection(vm);
        }
        else
        {
            vm.MoveVariableTreeItem(dragged, target);
        }
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

    // ── Integration folder tree: selection, drag-drop, rename (mirrors variables) ──

    private void IntegrationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedIntegrationTreeItem = e.NewValue as IntegrationTreeItem;
    }

    private System.Windows.Point _ruleTreeDragStart;
    private IntegrationTreeItem? _ruleTreeDragItem;

    private static IEnumerable<IntegrationTreeItem> FlattenVisibleIntegrationItems(IEnumerable<IntegrationTreeItem> items)
    {
        foreach (var i in items)
        {
            yield return i;
            if (i is IntegrationFolderNode f && f.IsExpanded)
                foreach (var c in FlattenVisibleIntegrationItems(f.Children)) yield return c;
        }
    }

    private static IEnumerable<IntegrationTreeItem> FlattenAllIntegrationItems(IEnumerable<IntegrationTreeItem> items)
    {
        foreach (var i in items)
        {
            yield return i;
            if (i is IntegrationFolderNode f)
                foreach (var c in FlattenAllIntegrationItems(f.Children)) yield return c;
        }
    }

    private void ClearIntegrationMultiSelection(MainViewModel vm)
    {
        foreach (var i in FlattenAllIntegrationItems(vm.IntegrationTree)) i.IsMultiSelected = false;
    }

    private void IntegrationTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ruleTreeDragStart = e.GetPosition(null);
        var item = FindIntegrationTreeItemData(e.OriginalSource as DependencyObject);

        if (DataContext is MainViewModel vm)
        {
            if (item != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                item.IsMultiSelected = !item.IsMultiSelected;
                _ruleTreeDragItem = null;
                e.Handled = true;
                return;
            }
            if (item != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var visible = FlattenVisibleIntegrationItems(vm.IntegrationTree).ToList();
                int a = vm.SelectedIntegrationTreeItem != null ? visible.IndexOf(vm.SelectedIntegrationTreeItem) : -1;
                int b = visible.IndexOf(item);
                if (b >= 0)
                {
                    if (a < 0) a = b;
                    for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
                        visible[i].IsMultiSelected = true;
                }
                _ruleTreeDragItem = null;
                e.Handled = true;
                return;
            }
            if (item == null || !item.IsMultiSelected)
                ClearIntegrationMultiSelection(vm);
        }

        _ruleTreeDragItem = item;
    }

    private void IntegrationTree_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _ruleTreeDragItem == null) return;
        var pos = e.GetPosition(null);
        if (System.Math.Abs(pos.X - _ruleTreeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(pos.Y - _ruleTreeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop(IntegrationTreeView, new DataObject("IntegrationTreeItem", _ruleTreeDragItem), DragDropEffects.Move);
        _ruleTreeDragItem = null;
    }

    private void IntegrationTree_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Data.GetData("IntegrationTreeItem") is not IntegrationTreeItem dragged) return;
        var target = FindIntegrationTreeItemData(e.OriginalSource as DependencyObject);

        if (dragged.IsMultiSelected)
        {
            var group = FlattenAllIntegrationItems(vm.IntegrationTree).Where(i => i.IsMultiSelected).ToList();
            var folders = group.OfType<IntegrationFolderNode>().ToList();
            foreach (var item in group)
            {
                if (folders.Any(f => f != item && FlattenAllIntegrationItems(f.Children).Contains(item))) continue;
                vm.MoveIntegrationTreeItem(item, target);
            }
            ClearIntegrationMultiSelection(vm);
        }
        else
        {
            vm.MoveIntegrationTreeItem(dragged, target);
        }
        e.Handled = true;
    }

    private static IntegrationTreeItem? FindIntegrationTreeItemData(DependencyObject? src)
    {
        while (src != null && src is not System.Windows.Controls.TreeViewItem)
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        return (src as System.Windows.Controls.TreeViewItem)?.DataContext as IntegrationTreeItem;
    }

    private void RenameIntegrationFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.SelectedIntegrationTreeItem is not IntegrationFolderNode folder) return;
        var name = TextPromptWindow.Prompt(this, "Rename Folder", "Folder name:", folder.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        vm.Undo.Checkpoint();
        folder.Name = name.Trim();
        vm.SyncIntegrationFoldersToModel();
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

    // Keep in lockstep with MainViewModel's Tab* constants — index 0 is the
    // ModForge landing tab, unit tabs start at 1 in authoring-workflow order.
    // Indices into MainTabs, and they must track its order exactly. Actors
    // merging into Characters removed a tab, so everything after it moved down
    // one — the constants are the only record of that order, so a stale one
    // silently navigates to the wrong tab rather than failing.
    private const int TabBusts = 1, TabNpcs = 2, TabPlaces = 3,
                      TabMapButtons = 4, TabDialogues = 5, TabVariables = 10;

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
                Reveal(field, PlaceTreeView, pl);
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
            // Kept for issue paths written before actors merged into
            // characters: same target, matched on the key rather than the name.
            case "actors":
            {
                MainTabs.SelectedIndex = TabBusts;
                var ac = vm.Characters.FirstOrDefault(c => c.Key == inner);
                if (ac == null) return;
                Dispatcher.BeginInvoke(new Action(() => SelectInTree(ac, null)),
                                       System.Windows.Threading.DispatcherPriority.Loaded);
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
    // ── Dialogue node drag & drop ───────────────────────────────────────
    // Reorder / reparent nodes by dragging a row. Which of the three things a
    // drop does is picked from where in the target row the cursor sits: the
    // top and bottom quarter-height bands mean "sibling before/after", the
    // middle half means "make it a child". A live adorner previews the result
    // so the distinction is visible before committing.

    private System.Windows.Point _nodeDragStart;
    private DialogueNodeViewModel? _nodeDragItem;
    private NodeDropAdorner? _nodeDropAdorner;

    private void NodeList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _nodeDragStart = e.GetPosition(null);
        _nodeDragItem = FindNodeRowData(e.OriginalSource as DependencyObject);
    }

    private void NodeList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _nodeDragItem == null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _nodeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _nodeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (sender is not System.Windows.Controls.ListBox lb) return;
        try
        {
            DragDrop.DoDragDrop(lb, new DataObject("DialogueNode", _nodeDragItem), DragDropEffects.Move);
        }
        finally
        {
            // DoDragDrop is modal — by the time it returns the drop (or the
            // cancel) is done, so this is the one place guaranteed to run.
            ClearNodeDropAdorner();
            _nodeDragItem = null;
        }
    }

    private void NodeList_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearNodeDropAdorner();

        if (e.Data.GetData("DialogueNode") is not DialogueNodeViewModel dragged ||
            sender is not System.Windows.Controls.ListBox lb)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var row = FindNodeRow(e.OriginalSource as DependencyObject);
        if (row?.DataContext is not DialogueNodeViewModel target)
        {
            // Empty space below the rows — drop makes it a trailing root.
            e.Effects = DragDropEffects.Move;
            return;
        }

        var mode = DropModeFor(row, e.GetPosition(row));
        if (!CanDropNode(lb, dragged, target, mode))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;

        // Indent the insertion line to the level the node would land at:
        // one deeper than the target for Into, otherwise the target's own.
        double indent = (target.Depth + (mode == NodeDropMode.Into ? 1 : 0)) * 16;
        var layer = AdornerLayer.GetAdornerLayer(row);
        if (layer != null)
        {
            _nodeDropAdorner = new NodeDropAdorner(row, mode, indent);
            layer.Add(_nodeDropAdorner);
        }
    }

    private void NodeList_DragLeave(object sender, DragEventArgs e) => ClearNodeDropAdorner();

    private void NodeList_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearNodeDropAdorner();

        if (e.Data.GetData("DialogueNode") is not DialogueNodeViewModel dragged) return;
        if (sender is not System.Windows.Controls.ListBox lb) return;
        if (lb.DataContext is not DialogueViewModel dialogue) return;

        var row = FindNodeRow(e.OriginalSource as DependencyObject);
        var target = row?.DataContext as DialogueNodeViewModel;
        var mode = row != null ? DropModeFor(row, e.GetPosition(row)) : NodeDropMode.After;

        if (target != null && !CanDropNode(lb, dragged, target, mode)) return;

        var vm = DataContext as MainViewModel;
        vm?.Undo.Checkpoint();
        if (dialogue.MoveNode(dragged, target, mode) && vm != null)
        {
            // Keep the moved node selected so the detail pane doesn't jump
            // away from what the author just repositioned.
            vm.SelectedNode = dragged;
        }
    }

    /// <summary>Which drop band the cursor is in: top/bottom quarter = sibling
    /// before/after, middle half = child.</summary>
    private static NodeDropMode DropModeFor(System.Windows.Controls.ListBoxItem row, System.Windows.Point p)
    {
        double h = row.ActualHeight;
        if (h <= 0) return NodeDropMode.After;
        double r = p.Y / h;
        if (r < 0.25) return NodeDropMode.Before;
        if (r > 0.75) return NodeDropMode.After;
        return NodeDropMode.Into;
    }

    /// <summary>Mirrors <see cref="DialogueViewModel.MoveNode"/>'s rejection rules
    /// so DragOver can show "no drop" rather than letting the user commit a
    /// move that silently does nothing.</summary>
    private static bool CanDropNode(System.Windows.Controls.ListBox lb, DialogueNodeViewModel dragged,
                                    DialogueNodeViewModel target, NodeDropMode mode)
    {
        if (ReferenceEquals(dragged, target)) return false;
        if (lb.DataContext is not DialogueViewModel dialogue) return false;
        return dialogue.CanMoveNode(dragged, target, mode);
    }

    private void ClearNodeDropAdorner()
    {
        if (_nodeDropAdorner == null) return;
        AdornerLayer.GetAdornerLayer(_nodeDropAdorner.AdornedElement)?.Remove(_nodeDropAdorner);
        _nodeDropAdorner = null;
    }

    private static System.Windows.Controls.ListBoxItem? FindNodeRow(DependencyObject? src)
    {
        while (src != null && src is not System.Windows.Controls.ListBoxItem)
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        return src as System.Windows.Controls.ListBoxItem;
    }

    private static DialogueNodeViewModel? FindNodeRowData(DependencyObject? src)
        => FindNodeRow(src)?.DataContext as DialogueNodeViewModel;

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
