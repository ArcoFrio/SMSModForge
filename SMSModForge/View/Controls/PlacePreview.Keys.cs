using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SMSModForge.Localization;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// The picture's and its hierarchy's keys, and the place's recycle bin under the hierarchy (after 1.10.0, the
/// author: "can we add hotkey controls to the hierarchy/preview of Places too? ... copy/paste/duplicate
/// (duplicating just duplicates at the same hierarchy level, while pasting pastes under the selected GO).
/// Deleting directly from there should also be an option", and "deleting the GOs actually sends them there
/// instead"). What the keys do is <see cref="PlaceObjects"/>'s; here is only when they are the preview's.
/// <list type="bullet">
/// <item><b>Ctrl+C</b> copies the object (or character) picked, everything under it with it.</item>
/// <item><b>Ctrl+V</b> pastes what was copied under the one picked - with nothing picked, among the place's own -
/// and picks the copy.</item>
/// <item><b>Ctrl+D</b> duplicates the one picked beside itself, and picks the copy. Never the window's Ctrl+D,
/// which duplicates the whole place in the list, while the keyboard is on the picture.</item>
/// <item><b>Delete</b> sends it to the recycle bin; Ctrl+Z brings it back, picked.</item>
/// </list>
/// They are the preview's while the keyboard is on the picture or on its hierarchy - a click on either puts it
/// there, without moving the page - and never in a box: the panel of settings beside the picture keeps its
/// keys for what is typed in it.
/// </summary>
public sealed partial class PlacePreview
{
    public static readonly DependencyProperty ObjectsProperty =
        DependencyProperty.Register(nameof(Objects), typeof(PlaceObjects), typeof(PlacePreview),
            new PropertyMetadata(null, OnObjectsChanged));

    /// <summary>The place's objects as the keys and the recycle bin work on them: the place's, or the game's
    /// level's, <c>Objects</c>. Without it the keys do nothing and there is no bin.</summary>
    public PlaceObjects? Objects
    {
        get => (PlaceObjects?)GetValue(ObjectsProperty);
        set => SetValue(ObjectsProperty, value);
    }

    private static void OnObjectsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (PlacePreview)d;
        if (e.OldValue is PlaceObjects old) old.Bin.CollectionChanged -= self.OnBinChanged;
        if (e.NewValue is PlaceObjects now) now.Bin.CollectionChanged += self.OnBinChanged;
        self.OnBinChanged(null, null);
    }

    private bool _binShownAgainQueued;

    /// <summary>The hierarchy drawn again once whatever changed the bin is done (an undo makes its rows one by one).</summary>
    private void OnBinChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        if (_binShownAgainQueued) return;
        _binShownAgainQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _binShownAgainQueued = false;
            RefreshObjectMenu();
        }));
    }

    // ── The keyboard ────────────────────────────────────────────────────

    /// <summary>
    /// The keyboard here, as a click on the picture or the hierarchy puts it, without moving the page: a
    /// control taking the keyboard asks to be brought into view, and the page scrolled to show as much of the
    /// preview as it could - a jump whenever it stood part way out of sight. Leaving a box this way still
    /// writes what was typed in it.
    /// </summary>
    internal void TakeKeyboard()
    {
        if (IsKeyboardFocused) return;
        RequestBringIntoViewEventHandler still = (_, e) => e.Handled = true;
        AddHandler(RequestBringIntoViewEvent, still);
        try
        {
            Focus();
            Keyboard.Focus(this);
        }
        finally { RemoveHandler(RequestBringIntoViewEvent, still); }
    }

    /// <summary>Whether the keyboard is on the picture, on its hierarchy or on the preview itself - not in a box.</summary>
    private bool KeyboardOnObjects()
    {
        if (Keyboard.FocusedElement is not DependencyObject focused || focused is TextBoxBase or PasswordBox) return false;
        for (DependencyObject? at = focused; at != null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, _gizmoToolbar)) return false;
            if (ReferenceEquals(at, _objectMenu) || ReferenceEquals(at, _viewport)) return true;
            if (ReferenceEquals(at, this)) return ReferenceEquals(focused, this);
        }
        return false;
    }

    private void OnObjectKeys(object sender, KeyEventArgs e)
    {
        if (e.Handled || Objects is not { } objects || !KeyboardOnObjects()) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        var held = Keyboard.Modifiers;
        object? picked = (object?)_selNode ?? _selPlacement;
        if (held == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.C:
                    if (objects.CanCopy(picked)) objects.Copy(picked!, StandsAt(picked));
                    e.Handled = true;
                    break;
                case Key.V:
                    if (PlaceObjects.CanPaste) PickMade(objects.Paste(picked));
                    e.Handled = true;
                    break;
                case Key.D:
                    if (picked != null) PickMade(objects.Duplicate(picked, StandsAt(picked)));
                    e.Handled = true;
                    break;
            }
        }
        else if (held == ModifierKeys.None && key == Key.Delete)
        {
            // What was picked is let go of by the rebuild that follows, which keeps where it was: Ctrl+Z picks it
            // again (PickAgainAfterUndo).
            if (objects.CanDelete(picked)) objects.Delete(picked!);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Where in the world one of the game's lights stands, as an object of the pack's own there would say it: a
    /// copy of the light is one of the pack's own, which stands at its X and Y in the world, while the game's is
    /// held where it is in its level - the same numbers would put the copy somewhere else. Null for anything else.
    /// </summary>
    private float[]? StandsAt(object? row)
    {
        if (row is not GameObjectViewModel { IsGameLightOnly: true } light) return null;
        FlushRebuild();
        var entry = _scene.FirstOrDefault(e => ReferenceEquals(e.Node, light));
        if (entry == null) return null;
        Point onCanvas = FrameFromParent(entry.World).ParentOrigin;
        var own = FrameFromParent(WorldToLevel);
        return new[] { (float)Math.Round((onCanvas.X - own.ParentOrigin.X) / own.Ux.X, 3), (float)Math.Round((onCanvas.Y - own.ParentOrigin.Y) / own.Uy.Y, 3) };
    }

    /// <summary>What a paste, a duplicate or a restore made, picked: its handles and its settings beside the picture.</summary>
    private void PickMade(object? made)
    {
        if (made == null) return;
        FlushRebuild();
        Pick(made);
    }

    // ── Making things (the GameObjects list's buttons, moved here) ───────

    public static readonly DependencyProperty FillCommandProperty =
        DependencyProperty.Register(nameof(FillCommand), typeof(ICommand), typeof(PlacePreview),
            new PropertyMetadata(null, (d, _) => ((PlacePreview)d).RefreshObjectMenu()));

    /// <summary>A game's level's "Fill from vanilla hierarchy": an object for every one the game's level has, the
    /// ones already there left alone. Null for a pack place, which has no such button.</summary>
    public ICommand? FillCommand
    {
        get => (ICommand?)GetValue(FillCommandProperty);
        set => SetValue(FillCommandProperty, value);
    }

    /// <summary>"+ Object" and "+ Character", over the hierarchy (after 1.10.0, the author: "Now let's move the rest
    /// of the GO controls into the preview"): what the GameObjects list below the preview had at its foot.</summary>
    private System.Collections.Generic.IEnumerable<Button> MakeChips()
    {
        if (Objects == null) yield break;
        var obj = MakeChip(Chip("preview.object.add"), AsCommand(AddObjectHere));
        LocText.Bind(obj, ToolTipProperty, "preview.object.add.tip");
        TutorialAnchor.SetId(obj, "btn:addObject");
        var character = MakeChip(Chip("preview.character.add"), AsCommand(AddCharacterHere));
        LocText.Bind(character, ToolTipProperty, "preview.character.add.tip");
        TutorialAnchor.SetId(character, "btn:addPlaceCharacter");
        foreach (var chip in new[] { obj, character })
        {
            Paint(chip, false);
            yield return chip;
        }
    }

    /// <summary>
    /// What a button of these does, run as a command: a command run is where the window cuts an undo step, as for
    /// "+ Light" (RelayCommand.Executing). A click cuts one too, as the mouse is let go - but a button pressed any
    /// other way (the walk over every control presses them so, UndoEveryControlTests) left its change joined to
    /// the next, and Ctrl+Z straight after it had nothing to take back.
    /// </summary>
    private static System.Action AsCommand(System.Action does)
    {
        var command = new RelayCommand(does);
        return () => command.Execute(null);
    }

    /// <summary>"Fill from vanilla hierarchy", on a game's level.</summary>
    private Button? FillChip()
    {
        if (FillCommand is not { } fill) return null;
        var b = MakeChip(Chip("places.seedFromCatalog"), () => { if (fill.CanExecute(null)) fill.Execute(null); });
        LocText.Bind(b, ToolTipProperty, "places.seedFromCatalog.tip");
        TutorialAnchor.SetId(b, "btn:fillFromGame");
        Paint(b, false);
        return b;
    }

    /// <summary>A new object of the pack's own in the middle of what the view shows, picked, its settings beside the
    /// picture - as + Light makes a light.</summary>
    internal void AddObjectHere()
    {
        if (Objects is not { } objects) return;
        PickMade(objects.AddObject(MiddleOfTheView()));
    }

    /// <summary>A new character on what is picked, when it can hold one, else on the NPCs object - picked, so its NPC
    /// can be chosen beside the picture.</summary>
    internal void AddCharacterHere()
    {
        if (Objects is not { } objects) return;
        PickMade(objects.AddCharacter((object?)_selNode ?? _selPlacement));
    }

    /// <summary>The middle of what the view shows, where an object of the pack's own stands there (its X and Y in the
    /// world: see WorldToLevel).</summary>
    private float[] MiddleOfTheView()
    {
        var mid = new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2);
        Point onCanvas = _viewport.ActualWidth > 0 ? _viewport.TranslatePoint(mid, _canvas) : new Point(CanvasWidth / 2, CanvasHeight / 2);
        var frame = FrameFromParent(WorldToLevel);
        return new[] { (float)Math.Round((onCanvas.X - frame.ParentOrigin.X) / frame.Ux.X, 2), (float)Math.Round((onCanvas.Y - frame.ParentOrigin.Y) / frame.Uy.Y, 2) };
    }

    // ── The recycle bin ─────────────────────────────────────────────────

    /// <summary>Whether the bin's rows are shown under its heading. A viewing preference: never in the pack.</summary>
    private bool _binOpen;

    /// <summary>The bin under the hierarchy, when there is anything in it: its heading, and when open what it holds,
    /// newest first, each with its way back and its way out, and a way to empty it.</summary>
    private void AddBinRows()
    {
        if (Objects is not { Bin.Count: > 0 } objects) return;
        var heading = BinChip((_binOpen ? "▾ " : "▸ ") + "♻  " + Loc.F("preview.place.bin", "count", objects.Bin.Count.ToString(CultureInfo.CurrentCulture)),
                              () => { _binOpen = !_binOpen; RefreshObjectMenu(); });
        heading.Margin = new Thickness(0, 8, 0, 0);
        heading.HorizontalAlignment = HorizontalAlignment.Stretch;
        LocText.Bind(heading, ToolTipProperty, "preview.place.bin.tip");
        _objectMenuList.Children.Add(heading);
        if (!_binOpen) return;

        foreach (var binned in objects.Bin.Reverse().ToList())
        {
            var row = new DockPanel { Margin = new Thickness(10, 1, 0, 0), LastChildFill = true };
            var forget = BinChip("✕", AsCommand(() => objects.Forget(binned)));
            LocText.Bind(forget, ToolTipProperty, "preview.place.bin.forget.tip");
            var restore = BinChip("↶", AsCommand(() => PickMade(objects.Restore(binned))));
            LocText.Bind(restore, ToolTipProperty, "preview.place.bin.restore.tip");
            DockPanel.SetDock(forget, Dock.Right);
            DockPanel.SetDock(restore, Dock.Right);
            row.Children.Add(forget);
            row.Children.Add(restore);
            row.Children.Add(new TextBlock
            {
                Text = (binned.IsCharacter ? "☺  " : binned.Model.SwitchedOff ? "○  " : "▦  ") + binned.Display,
                Foreground = RowInk,
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = binned.Model.SwitchedOff ? Loc.T("preview.place.bin.switchedOff")
                        : binned.Under.Length > 0 ? Loc.F("preview.place.bin.under", "path", binned.Under) : Loc.T("preview.place.bin.top"),
            });
            row.Tag = binned;
            _objectMenuList.Children.Add(row);
        }
        var empty = BinChip(Loc.T("preview.place.bin.empty"), AsCommand(objects.EmptyBin));
        empty.Margin = new Thickness(10, 3, 0, 0);
        empty.HorizontalAlignment = HorizontalAlignment.Left;
        LocText.Bind(empty, ToolTipProperty, "preview.place.bin.empty.tip");
        _objectMenuList.Children.Add(empty);
    }

    /// <summary>A button of the bin's: the hierarchy's look, and never the keyboard's (it goes to the preview).</summary>
    private static Button BinChip(string text, System.Action onClick)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, Foreground = ChipText },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(2, 0, 0, 0),
            FontSize = 11,
            Background = ChipOff,
            Foreground = ChipText,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>The hierarchy's list as drawn - its rows, the bin's - for tests.</summary>
    internal Panel HierarchyList => _objectMenuList;

    /// <summary>Where the picked one's handles are drawn on the picture's canvas - where it stands - or null with
    /// nothing picked; for tests.</summary>
    internal Point? HandlesAt => _gizmoTarget == null ? null : _gizmoFrame.OriginFor(_gizmoTarget.X, _gizmoTarget.Y);

    /// <summary>The look of the picked row in the hierarchy, for tests.</summary>
    internal static Brush PickedRowBrush => ChipOn;

    /// <summary>The bin's rows as drawn: what each holds, for tests.</summary>
    internal System.Collections.Generic.IReadOnlyList<BinnedObjectViewModel> BinRowsShown
        => _objectMenuList.Children.OfType<DockPanel>().Select(r => r.Tag).OfType<BinnedObjectViewModel>().ToList();

    /// <summary>Open or fold the bin, as a click on its heading does (for tests).</summary>
    internal void ShowBin(bool open)
    {
        _binOpen = open;
        RefreshObjectMenu();
    }
}
